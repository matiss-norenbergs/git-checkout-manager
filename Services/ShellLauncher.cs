using System.Diagnostics;

namespace GitCheckoutManager.Services
{
    public class ShellLauncher : IShellLauncher
    {
        private readonly IShellEnvironment _env;
        private readonly Lazy<string?> _vsCodeExe;

        public ShellLauncher() : this(new SystemShellEnvironment()) { }

        public ShellLauncher(IShellEnvironment env)
        {
            _env = env;
            _vsCodeExe = new Lazy<string?>(FindVsCode);
        }

        public bool IsVsCodeAvailable => _vsCodeExe.Value != null;

        public bool FolderExists(string path)
        {
            try { return _env.DirectoryExists(Path.GetFullPath(path)); }
            catch (Exception) { return false; }
        }

        /// <summary>explorer.exe returns exit code 1 on success, so the exit code is never inspected.</summary>
        public LaunchResult OpenInExplorer(string folder) => Launch("explorer.exe", folder, "Explorer");

        public LaunchResult OpenInVsCode(string folder)
        {
            var exe = _vsCodeExe.Value;
            return exe == null ? LaunchResult.Fail("VS Code was not found.") : Launch(exe, folder, "VS Code");
        }

        private LaunchResult Launch(string exe, string folder, string what)
        {
            string full;
            try { full = Path.GetFullPath(folder); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return LaunchResult.Fail($"Can't open {what}: invalid path.");
            }

            if (!_env.DirectoryExists(full))
                return LaunchResult.Fail($"Can't open {what}: the folder doesn't exist on disk ({full}).");

            try
            {
                var psi = new ProcessStartInfo { FileName = exe, UseShellExecute = false };
                psi.ArgumentList.Add(full); // one argument, never string-concatenated (security rule 5)
                _env.Start(psi);
                return LaunchResult.Ok;
            }
            catch (Exception ex)
            {
                return LaunchResult.Fail($"Couldn't open {what}: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves Code.exe, never code.cmd (a batch file runs through cmd.exe, which interprets % &amp; ^ in a path).
        /// Order: Code.exe next to the bin\ folder of code.cmd on PATH, then the per-user install, then the machine-wide one.
        /// </summary>
        private string? FindVsCode()
        {
            try
            {
                var cmd = _env.FindOnPath("code.cmd");
                if (cmd != null)
                {
                    var binDir = Path.GetDirectoryName(cmd);
                    if (binDir != null)
                    {
                        var candidate = Path.GetFullPath(Path.Combine(binDir, "..", "Code.exe"));
                        if (_env.FileExists(candidate)) return candidate;
                    }
                }

                foreach (var (variable, subPath) in new[]
                {
                    ("LocalAppData", Path.Combine("Programs", "Microsoft VS Code", "Code.exe")),
                    ("ProgramFiles", Path.Combine("Microsoft VS Code", "Code.exe")),
                })
                {
                    var baseDir = _env.GetEnvironmentVariable(variable);
                    if (string.IsNullOrWhiteSpace(baseDir)) continue;
                    var candidate = Path.Combine(baseDir, subPath);
                    if (_env.FileExists(candidate)) return candidate;
                }
            }
            catch (Exception) { /* detection is best effort: treat as not installed */ }

            return null;
        }
    }

    public class SystemShellEnvironment : IShellEnvironment
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

        public string? FindOnPath(string fileName)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return null;

            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim().Trim('"'), fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { /* malformed PATH entry */ }
            }
            return null;
        }

        public void Start(ProcessStartInfo startInfo)
        {
            using var process = Process.Start(startInfo);
        }
    }
}
