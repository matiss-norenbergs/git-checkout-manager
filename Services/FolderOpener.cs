using System.IO;

namespace GitCheckoutManager.Services
{
    /// <summary>
    /// Turns "folder inside a checkout" into a launch and a user-facing message. Shared by the Manage tree
    /// and the Submodules window so both report errors the same way.
    /// </summary>
    public class FolderOpener
    {
        private readonly IShellLauncher _launcher;

        public FolderOpener(IShellLauncher launcher) => _launcher = launcher;

        public bool IsVsCodeAvailable => _launcher.IsVsCodeAvailable;

        /// <summary>Absolute folder for a path relative to <paramref name="root"/> (forward or back slashes).</summary>
        public static string Resolve(string root, string relativePath) =>
            Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>Checked when a context menu opens, so it reflects the disk right now.</summary>
        public bool IsOnDisk(string root, string relativePath) =>
            !string.IsNullOrEmpty(root) && _launcher.FolderExists(Resolve(root, relativePath));

        /// <summary>Returns null on success, else the message to show.</summary>
        public string? Open(string root, string relativePath, bool vsCode)
        {
            if (string.IsNullOrEmpty(root)) return "No checkout is open.";

            var folder = Resolve(root, relativePath);
            if (!_launcher.FolderExists(folder))
                return $"Can't open {relativePath}: it isn't on disk (outside the sparse selection or not applied yet).";

            var result = vsCode ? _launcher.OpenInVsCode(folder) : _launcher.OpenInExplorer(folder);
            return result.Success ? null : result.Error;
        }
    }
}
