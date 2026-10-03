using System.IO;

namespace GitCheckoutManager.Services
{
    /// <summary>
    /// Single source of truth for where the app keeps its data, plus the one-time migration from the
    /// folders used before the project was renamed from GitSparseManager.
    /// </summary>
    public static class AppPaths
    {
        public const string AppFolderName = "GitCheckoutManager";
        public const string LegacyAppFolderName = "GitSparseManager";

        private static readonly string AppDataRoot =
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly string LocalAppDataRoot =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static string DataFolder { get; } = Path.Combine(AppDataRoot, AppFolderName);
        public static string SettingsFile { get; } = Path.Combine(DataFolder, "settings.json");
        public static string PresetsFile { get; } = Path.Combine(DataFolder, "presets.json");
        public static string TreeCacheFolder { get; } = Path.Combine(LocalAppDataRoot, AppFolderName, "tree-cache");

        private static readonly string[] MigratedFiles = { "settings.json", "presets.json" };

        /// <summary>Runs the legacy migration against the real %AppData% / %LocalAppData% folders.</summary>
        public static void MigrateLegacyData() => MigrateLegacyData(AppDataRoot, LocalAppDataRoot);

        /// <summary>
        /// If the new data folder doesn't exist but the old one does, creates it and copies settings.json
        /// and presets.json (the old folder is left untouched). Independently, deletes the old tree cache
        /// (best effort) since it is only a cache and would otherwise sit there as wasted disk space.
        /// </summary>
        public static void MigrateLegacyData(string appDataRoot, string localAppDataRoot)
        {
            try
            {
                var newFolder = Path.Combine(appDataRoot, AppFolderName);
                var oldFolder = Path.Combine(appDataRoot, LegacyAppFolderName);
                if (!Directory.Exists(newFolder) && Directory.Exists(oldFolder))
                {
                    Directory.CreateDirectory(newFolder);
                    foreach (var name in MigratedFiles)
                    {
                        var source = Path.Combine(oldFolder, name);
                        if (File.Exists(source))
                            File.Copy(source, Path.Combine(newFolder, name), overwrite: false);
                    }
                }
            }
            catch
            {
                // Migration is best effort; the app starts with defaults if it fails.
            }

            try
            {
                var oldCache = Path.Combine(localAppDataRoot, LegacyAppFolderName, "tree-cache");
                if (Directory.Exists(oldCache))
                    Directory.Delete(oldCache, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
