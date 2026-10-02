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

        /// <summary>Whether the Script expander in the main window was left open.</summary>
        public bool ScriptPanelExpanded { get; set; } = false;

        /// <summary>Submodules window: update to the branch tip (--remote) instead of the pinned commit.</summary>
        public bool SubmoduleLatestFromBranch { get; set; } = false;

        /// <summary>Submodules window: also initialize nested submodules (--recursive).</summary>
        public bool SubmoduleIncludeNested { get; set; } = true;

        public const string DefaultFolderNamePattern = "{repo}_{branch}";

        /// <summary>Folder where Clone checkouts are created; remembered between runs.</summary>
        public string CloneParentFolder { get; set; } = string.Empty;

        /// <summary>Tokens: {repo}, {branch} (new branch if set, else the selected one), {base} (selected branch).</summary>
        public string FolderNamePattern { get; set; } = DefaultFolderNamePattern;
    }
}
