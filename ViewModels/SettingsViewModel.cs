using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitSparseManager.Models;
using GitSparseManager.Services;

namespace GitSparseManager.ViewModels
{
    /// <summary>
    /// Backs the Settings window. Theme and folder-pattern changes are pushed to the callbacks immediately,
    /// so the owner applies and persists them exactly as it did when the picker lived in the main window.
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IRemoteTreeService _remoteTreeService;
        private readonly Action<ThemeMode> _applyTheme;
        private readonly Action<string> _applyFolderNamePattern;

        public IReadOnlyList<ThemeMode> ThemeModes { get; } = Enum.GetValues<ThemeMode>();

        [ObservableProperty] private ThemeMode _selectedThemeMode;
        [ObservableProperty] private string _cacheSizeText = string.Empty;
        [ObservableProperty] private string _folderNamePattern;
        [ObservableProperty] private string _folderNameExample = string.Empty;

        public SettingsViewModel(ThemeMode currentTheme, string folderNamePattern,
            IRemoteTreeService remoteTreeService, Action<ThemeMode> applyTheme,
            Action<string> applyFolderNamePattern)
        {
            _remoteTreeService = remoteTreeService;
            _applyTheme = applyTheme;
            _applyFolderNamePattern = applyFolderNamePattern;
            _selectedThemeMode = currentTheme;
            _folderNamePattern = folderNamePattern;
            RefreshCacheSize();
            RefreshExample();
        }

        partial void OnSelectedThemeModeChanged(ThemeMode value) => _applyTheme(value);

        partial void OnFolderNamePatternChanged(string value)
        {
            RefreshExample();
            _applyFolderNamePattern(value);
        }

        private void RefreshExample() =>
            FolderNameExample = "Example: " +
                MainViewModel.BuildFolderName(FolderNamePattern, "tmp", null, "feature/login");

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
