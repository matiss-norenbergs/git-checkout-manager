using Velopack;
using Velopack.Sources;

namespace GitCheckoutManager.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly UpdateManager _manager = new(new GithubSource(AppVersion.RepositoryUrl, null, false));
        private UpdateInfo? _pending;

        public bool IsInstalled => _manager.IsInstalled;

        public async Task<string?> CheckAndDownloadAsync()
        {
            if (!_manager.IsInstalled) return null;

            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info == null) return null;

            await _manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
            _pending = info;
            return info.TargetFullRelease.Version.ToString();
        }

        public void ApplyUpdatesAndRestart()
        {
            if (_pending != null)
                _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        }
    }
}
