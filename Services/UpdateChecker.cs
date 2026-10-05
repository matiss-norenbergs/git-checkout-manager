namespace GitCheckoutManager.Services
{
    /// <summary>Result of one update check. Errors are not an outcome: they throw.</summary>
    public abstract record UpdateCheckOutcome
    {
        public sealed record UpToDate : UpdateCheckOutcome;
        public sealed record UpdateReady(string Version) : UpdateCheckOutcome;
        public sealed record AlreadyChecking : UpdateCheckOutcome;
    }

    /// <summary>
    /// Update-check logic shared by the startup check, the periodic timer and the manual check in Settings.
    /// No UI or timer here (MainViewModel owns those), so it can be tested with a fake <see cref="IUpdateService"/>.
    /// </summary>
    public class UpdateChecker
    {
        private readonly IUpdateService _updateService;
        private readonly Action<string> _reportError;
        private readonly Action<string> _onUpdateReady;
        private bool _errorReported;

        public UpdateChecker(IUpdateService updateService, Action<string> reportError, Action<string> onUpdateReady)
        {
            _updateService = updateService;
            _reportError = reportError;
            _onUpdateReady = onUpdateReady;
        }

        public bool IsChecking { get; private set; }

        /// <summary>Version of the update that is already downloaded, or null.</summary>
        public string? ReadyVersion { get; private set; }

        /// <summary>One check + download. Throws on errors. Never starts a second check or re-checks once an update is downloaded.</summary>
        public async Task<UpdateCheckOutcome> RunCheckAsync()
        {
            if (ReadyVersion != null) return new UpdateCheckOutcome.UpdateReady(ReadyVersion);
            if (IsChecking) return new UpdateCheckOutcome.AlreadyChecking();

            IsChecking = true;
            try
            {
                var version = await Task.Run(() => _updateService.CheckAndDownloadAsync());
                if (version == null) return new UpdateCheckOutcome.UpToDate();

                ReadyVersion = version;
                _onUpdateReady(version);
                return new UpdateCheckOutcome.UpdateReady(version);
            }
            finally
            {
                IsChecking = false;
            }
        }

        /// <summary>Silent check (startup and periodic): no dialogs; a failure goes to the status bar at most once per app run.</summary>
        public async Task RunSilentCheckAsync()
        {
            try
            {
                await RunCheckAsync();
            }
            catch (Exception ex)
            {
                if (_errorReported) return;
                _errorReported = true;
                _reportError($"Update check failed: {ex.Message}");
            }
        }

        /// <summary>Periodic timer tick: skipped while a check runs or after an update is downloaded.</summary>
        public Task TickAsync() =>
            IsChecking || ReadyVersion != null ? Task.CompletedTask : RunSilentCheckAsync();

        /// <summary>Manual check from Settings. Returns the message to show.
        /// When a check is already running it answers right away instead of waiting for it.</summary>
        public async Task<string> RunManualCheckAsync()
        {
            if (!_updateService.IsInstalled)
                return "Updates are only available in the installed version.";

            try
            {
                return await RunCheckAsync() switch
                {
                    UpdateCheckOutcome.UpdateReady ready => $"Version {ready.Version} is ready. Restart to update.",
                    UpdateCheckOutcome.AlreadyChecking => "An update check is already running…",
                    _ => "You're up to date."
                };
            }
            catch (Exception ex)
            {
                return $"Update check failed: {ex.Message}";
            }
        }
    }
}
