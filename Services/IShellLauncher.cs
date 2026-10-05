namespace GitCheckoutManager.Services
{
    /// <summary>Outcome of a launch; <see cref="Error"/> is a user-facing message when it failed.</summary>
    public sealed record LaunchResult(bool Success, string Error = "")
    {
        public static LaunchResult Ok { get; } = new(true);
        public static LaunchResult Fail(string error) => new(false, error);
    }

    /// <summary>Opens folders in Explorer / VS Code. Never throws: failures come back as a <see cref="LaunchResult"/>.</summary>
    public interface IShellLauncher
    {
        /// <summary>True when a VS Code executable was found (detected once, then cached for the app run).</summary>
        bool IsVsCodeAvailable { get; }

        bool FolderExists(string path);

        LaunchResult OpenInExplorer(string folder);

        LaunchResult OpenInVsCode(string folder);
    }

    /// <summary>The machine lookups <see cref="ShellLauncher"/> needs, so tests can fake them.</summary>
    public interface IShellEnvironment
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);

        /// <summary>First match of <paramref name="fileName"/> (e.g. "code.cmd") on PATH, or null.</summary>
        string? FindOnPath(string fileName);

        string? GetEnvironmentVariable(string name);

        /// <summary>Starts the process without waiting for it.</summary>
        void Start(System.Diagnostics.ProcessStartInfo startInfo);
    }
}
