using System;
using System.Windows;
using GitSparseManager.Models;
using GitSparseManager.Services;
using Microsoft.Win32;
using Velopack;

namespace GitSparseManager
{
    public partial class App : Application
    {
        private const string LightThemePath = "Themes/LightTheme.xaml";
        private const string DarkThemePath = "Themes/DarkTheme.xaml";
        private ThemeMode _themeMode = ThemeMode.System;

        [STAThread]
        private static void Main(string[] args)
        {
            // Velopack must run before anything else (install/update hooks exit the process early).
            VelopackApp.Build().Run();

            App app = new();
            app.InitializeComponent();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings = new SettingsService().LoadSettings();
            _themeMode = settings.ThemeMode;

            ApplyTheme();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            new MainWindow().Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            base.OnExit(e);
        }

        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (_themeMode != ThemeMode.System)
                return;

            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
                ApplyTheme();
        }

        public void SetThemeMode(ThemeMode themeMode)
        {
            _themeMode = themeMode;
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            var isDark = _themeMode == ThemeMode.Dark || (_themeMode == ThemeMode.System && IsDarkModeEnabled());
            var themePath = isDark ? DarkThemePath : LightThemePath;
            ReplaceThemeDictionary(themePath);
        }

        private void ReplaceThemeDictionary(string themePath)
        {
            var dictionaries = Resources.MergedDictionaries;

            for (var i = dictionaries.Count - 1; i >= 0; i--)
            {
                var source = dictionaries[i].Source?.OriginalString;
                if (string.Equals(source, LightThemePath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(source, DarkThemePath, StringComparison.OrdinalIgnoreCase))
                {
                    dictionaries.RemoveAt(i);
                }
            }

            dictionaries.Add(new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) });
        }

        internal static bool IsDarkModeEnabled()
        {
            const string personalizeSubKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            const string appsUseLightThemeValue = "AppsUseLightTheme";

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(personalizeSubKey);
                var value = key?.GetValue(appsUseLightThemeValue);
                if (value is int intValue)
                    return intValue == 0;
            }
            catch
            {
                // If registry read fails, keep the light theme as a safe default.
            }

            return false;
        }
    }
}
