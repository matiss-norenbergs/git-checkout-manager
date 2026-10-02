using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitSparseManager.Models;
using GitSparseManager.Services;

namespace GitSparseManager.ViewModels
{
    /// <summary>
    /// Backs the Settings window. Theme changes are pushed to <paramref name="applyTheme"/> immediately,
    /// so the owner applies and persists them exactly as it did when the picker lived in the main window.
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IRemoteTreeService _remoteTreeService;
        private readonly Action<ThemeMode> _applyTheme;

        public IReadOnlyList<ThemeMode> ThemeModes { get; } = Enum.GetValues<ThemeMode>();

        [ObservableProperty] private ThemeMode _selectedThemeMode;
        [ObservableProperty] private string _cacheSizeText = string.Empty;

        public SettingsViewModel(ThemeMode currentTheme, IRemoteTreeService remoteTreeService,
            Action<ThemeMode> applyTheme)
        {
            _remoteTreeService = remoteTreeService;
            _applyTheme = applyTheme;
            _selectedThemeMode = currentTheme;
            RefreshCacheSize();
        }

        partial void OnSelectedThemeModeChanged(ThemeMode value) => _applyTheme(value);

        [RelayCommand]
        private void ClearTreeCache()
        {
            _remoteTreeService.ClearCache();
            RefreshCacheSize();
        }

        private void RefreshCacheSize() =>
            CacheSizeText = MainViewModel.FormatSize(_remoteTreeService.GetCacheSizeBytes());
    }
}
