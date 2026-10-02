using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    public class SettingsService : ISettingsService
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private readonly string _settingsPath;

        public SettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "GitSparseManager");
            Directory.CreateDirectory(folder);
            _settingsPath = Path.Combine(folder, "settings.json");
        }

        public AppSettings LoadSettings()
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();
            try
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                MigrateLegacySettings(settings);
                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }

        private static void MigrateLegacySettings(AppSettings settings)
        {
            var gitLabKey = GitHostType.GitLab.ToString();

            if (!settings.ServerUrls.ContainsKey(gitLabKey) && !string.IsNullOrWhiteSpace(settings.GitLabUrl))
                settings.ServerUrls[gitLabKey] = settings.GitLabUrl;

            if (!settings.EncryptedTokens.ContainsKey(gitLabKey) && !string.IsNullOrEmpty(settings.EncryptedToken))
                settings.EncryptedTokens[gitLabKey] = settings.EncryptedToken;
        }

        public void SaveSettings(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, WriteOptions);
            var tmp = _settingsPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _settingsPath, overwrite: true);
        }

        public string? GetDecryptedToken(AppSettings settings)
        {
            return Unprotect(settings.EncryptedToken);
        }

        public void SetToken(AppSettings settings, string token)
        {
            settings.EncryptedToken = Protect(token);
        }

        public string? GetDecryptedToken(AppSettings settings, GitHostType hostType)
        {
            return settings.EncryptedTokens.TryGetValue(hostType.ToString(), out var encrypted)
                ? Unprotect(encrypted)
                : null;
        }

        public void SetToken(AppSettings settings, GitHostType hostType, string token)
        {
            var key = hostType.ToString();
            settings.EncryptedTokens[key] = Protect(token);

            if (hostType == GitHostType.GitLab)
                settings.EncryptedToken = settings.EncryptedTokens[key];
        }

        private static string Protect(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(token);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        private static string? Unprotect(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64))
                return null;
            try
            {
                var encrypted = Convert.FromBase64String(encryptedBase64);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return null;
            }
        }
    }
}
