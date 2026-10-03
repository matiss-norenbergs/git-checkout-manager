using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests
{
    public class AppPathsMigrationTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "gcm-migration-" + Guid.NewGuid().ToString("N"));
        private string AppData => Path.Combine(_root, "Roaming");
        private string Local => Path.Combine(_root, "Local");
        private string OldDir => Path.Combine(AppData, "GitSparseManager");
        private string NewDir => Path.Combine(AppData, "GitCheckoutManager");

        public AppPathsMigrationTests()
        {
            Directory.CreateDirectory(AppData);
            Directory.CreateDirectory(Local);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void Copies_settings_and_presets_and_leaves_old_folder_intact()
        {
            Directory.CreateDirectory(OldDir);
            File.WriteAllText(Path.Combine(OldDir, "settings.json"), "S");
            File.WriteAllText(Path.Combine(OldDir, "presets.json"), "P");

            AppPaths.MigrateLegacyData(AppData, Local);

            Assert.Equal("S", File.ReadAllText(Path.Combine(NewDir, "settings.json")));
            Assert.Equal("P", File.ReadAllText(Path.Combine(NewDir, "presets.json")));
            Assert.True(File.Exists(Path.Combine(OldDir, "settings.json")));
            Assert.True(File.Exists(Path.Combine(OldDir, "presets.json")));
        }

        [Fact]
        public void Does_nothing_when_new_folder_already_exists()
        {
            Directory.CreateDirectory(OldDir);
            File.WriteAllText(Path.Combine(OldDir, "settings.json"), "old");
            Directory.CreateDirectory(NewDir);
            File.WriteAllText(Path.Combine(NewDir, "settings.json"), "new");

            AppPaths.MigrateLegacyData(AppData, Local);

            Assert.Equal("new", File.ReadAllText(Path.Combine(NewDir, "settings.json")));
            Assert.False(File.Exists(Path.Combine(NewDir, "presets.json")));
        }

        [Fact]
        public void Does_not_create_new_folder_when_old_folder_is_missing()
        {
            AppPaths.MigrateLegacyData(AppData, Local);
            Assert.False(Directory.Exists(NewDir));
        }

        [Fact]
        public void Tolerates_missing_files_in_old_folder()
        {
            Directory.CreateDirectory(OldDir);
            File.WriteAllText(Path.Combine(OldDir, "settings.json"), "S");

            AppPaths.MigrateLegacyData(AppData, Local);

            Assert.True(File.Exists(Path.Combine(NewDir, "settings.json")));
            Assert.False(File.Exists(Path.Combine(NewDir, "presets.json")));
        }

        [Fact]
        public void Deletes_old_tree_cache_but_not_new_one()
        {
            var oldCache = Path.Combine(Local, "GitSparseManager", "tree-cache", "abc", "repo");
            Directory.CreateDirectory(oldCache);
            File.WriteAllText(Path.Combine(oldCache, "f"), "x");
            var newCache = Path.Combine(Local, "GitCheckoutManager", "tree-cache");
            Directory.CreateDirectory(newCache);

            AppPaths.MigrateLegacyData(AppData, Local);

            Assert.False(Directory.Exists(Path.Combine(Local, "GitSparseManager", "tree-cache")));
            Assert.True(Directory.Exists(newCache));
        }

        [Fact]
        public void Old_cache_is_deleted_even_when_settings_were_already_migrated()
        {
            Directory.CreateDirectory(NewDir);
            Directory.CreateDirectory(Path.Combine(Local, "GitSparseManager", "tree-cache"));

            AppPaths.MigrateLegacyData(AppData, Local);

            Assert.False(Directory.Exists(Path.Combine(Local, "GitSparseManager", "tree-cache")));
        }
    }
}
