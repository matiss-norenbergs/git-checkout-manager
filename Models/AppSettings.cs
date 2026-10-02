namespace GitSparseManager.Models
{
    public class AppSettings
    {
        public GitHostType HostType { get; set; } = GitHostType.GitLab;

        /// <summary>Server URL per host type, keyed by <see cref="GitHostType"/> name.</summary>
        public Dictionary<string, string> ServerUrls { get; set; } = new();

        /// <summary>DPAPI-protected token per host type, keyed by <see cref="GitHostType"/> name.</summary>
        public Dictionary<string, string> EncryptedTokens { get; set; } = new();

        /// <summary>Most recently opened Manage checkouts, newest first, capped at 10.</summary>
        public List<RecentCheckout> RecentCheckouts { get; set; } = new();

        // Legacy single-host settings, kept so existing settings.json files still load.
        public string GitLabUrl { get; set; } = "http://gitlab.local";
        public string EncryptedToken { get; set; } = string.Empty;
        public bool InitSubmodules { get; set; } = false;
        public bool KeepWindowOpen { get; set; } = true;
        public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    }
}
