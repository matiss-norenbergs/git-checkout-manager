using GitCheckoutManager.Models;

namespace GitCheckoutManager.ViewModels
{
    /// <summary>Everything one tab needs to show its tree again after the user comes back to it.</summary>
    public sealed class TreeState
    {
        public List<TreeNodeViewModel> Roots { get; set; } = new();
        public string SearchText { get; set; } = string.Empty;
        public Dictionary<TreeNodeViewModel, bool>? ExpansionSnapshot { get; set; }

        /// <summary>What the nodes were loaded for. Clone: repo URL + branch. Manage: checkout root + HEAD sha. Null = nothing loaded.</summary>
        public string? Key { get; set; }

        /// <summary>Clone only: the repository the nodes belong to, so a branch change can keep the ticks.</summary>
        public string? RepoUrl { get; set; }

        /// <summary>Status line to show when the tab is shown again as is.</summary>
        public string StatusMessage { get; set; } = string.Empty;

        public static string CloneKey(string repoUrl, string branch) => repoUrl + "\n" + branch;

        public static string ManageKey(string root, string headSha) => root + "\n" + headSha;

        public List<string> GetCheckedPaths()
        {
            var paths = new List<string>();
            foreach (var node in Roots)
                paths.AddRange(node.GetCheckedPaths());
            return paths;
        }

        public void Clear()
        {
            Roots = new List<TreeNodeViewModel>();
            SearchText = string.Empty;
            ExpansionSnapshot = null;
            Key = null;
            RepoUrl = null;
            StatusMessage = string.Empty;
        }
    }

    public enum CloneSwitchAction { ShowAsIs, Load, ShowEmpty }

    public enum ManageSwitchAction { ShowAsIs, AutoOpenMostRecent }

    /// <summary>Holds the per-tab trees and decides what switching tabs has to do.</summary>
    public sealed class TreeTabs
    {
        public TreeState Clone { get; } = new();
        public TreeState Manage { get; } = new();

        public TreeState For(AppMode mode) => mode == AppMode.Manage ? Manage : Clone;

        /// <summary>Remembers the tab being left; its nodes stay where they are.</summary>
        public void Leave(AppMode leaving, string searchText) => For(leaving).SearchText = searchText;

        /// <summary>
        /// Never shows Manage nodes. With no repo and branch selected the tree is empty (and stale nodes are dropped);
        /// with a matching key the loaded tree is reused without a fetch.
        /// </summary>
        public CloneSwitchAction EnterClone(string? repoUrl, string? branch)
        {
            if (string.IsNullOrEmpty(repoUrl) || string.IsNullOrEmpty(branch))
            {
                Clone.Clear();
                return CloneSwitchAction.ShowEmpty;
            }

            return Clone.Key == TreeState.CloneKey(repoUrl, branch)
                ? CloneSwitchAction.ShowAsIs
                : CloneSwitchAction.Load;
        }

        /// <summary>An open (or opening) checkout is kept as is, with its pending changes. Only an empty tab auto-opens.</summary>
        public ManageSwitchAction EnterManage(bool checkoutOpenOrOpening) =>
            checkoutOpenOrOpening || Manage.Key != null
                ? ManageSwitchAction.ShowAsIs
                : ManageSwitchAction.AutoOpenMostRecent;

        /// <summary>Ticks to carry into a new Clone load: only from the same repository, never from Manage.</summary>
        public List<string> PreviousCloneSelection(string repoUrl) =>
            string.Equals(Clone.RepoUrl, repoUrl, StringComparison.OrdinalIgnoreCase)
                ? Clone.GetCheckedPaths()
                : new List<string>();
    }
}
