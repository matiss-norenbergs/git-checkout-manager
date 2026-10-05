namespace GitCheckoutManager.Models
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

        public const double DefaultMainSplitRatio = 2.0 / 3.0;

        /// <summary>Main window: tree width as a fraction of tree + right panel (0.4–0.8).</summary>
        public double MainSplitRatio { get; set; } = DefaultMainSplitRatio;

        /// <summary>Submodules window: pinned commit or branch tip (--remote). Null in settings saved before this existed; then <see cref="SubmoduleLatestFromBranch"/> decides.</summary>
        public SubmoduleTarget? SubmoduleTarget { get; set; }

        /// <summary>Legacy form of <see cref="SubmoduleTarget"/>: still read for old settings files and kept in sync on save so an older build sees the same choice.</summary>
        public bool SubmoduleLatestFromBranch { get; set; } = false;

        /// <summary>Submodules window: also initialize nested submodules (--recursive).</summary>
        public bool SubmoduleIncludeNested { get; set; } = true;

        /// <summary>Submodules window: list only submodules that need attention.</summary>
        public bool SubmodulesShowOnlyProblems { get; set; } = false;

        /// <summary>Clone tab: sparse checkout of selected folders, or a full clone.</summary>
        public CloneMode CloneMode { get; set; } = CloneMode.Sparse;

        public const string DefaultFolderNamePattern = "{repo}_{branch}";

        /// <summary>Folder where Clone checkouts are created; remembered between runs.</summary>
        public string CloneParentFolder { get; set; } = string.Empty;

        /// <summary>Tokens: {repo}, {branch} (new branch if set, else the selected one), {base} (selected branch).</summary>
        public string FolderNamePattern { get; set; } = DefaultFolderNamePattern;
    }
}
