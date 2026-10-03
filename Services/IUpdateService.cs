namespace GitCheckoutManager.Services
{
    public interface IUpdateService
    {
        /// <summary>True when running from a Velopack install (false for Visual Studio / loose exe runs).</summary>
        bool IsInstalled { get; }

        /// <summary>Checks GitHub Releases and, if a newer version exists, downloads it.
        /// Returns the downloaded version, or null when already up to date. Throws on network/update errors.</summary>
        Task<string?> CheckAndDownloadAsync();

        /// <summary>Applies the downloaded update and restarts the app.</summary>
        void ApplyUpdatesAndRestart();
    }
}
