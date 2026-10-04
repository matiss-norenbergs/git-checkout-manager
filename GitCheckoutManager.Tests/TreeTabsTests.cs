using GitCheckoutManager.Models;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Tests;

public class TreeTabsTests
{
    private const string RepoA = "http://host/a.git";
    private const string RepoB = "http://host/b.git";

    private static TreeNodeViewModel Folder(string path) =>
        new(new TreeNode { Path = path, Name = path, Type = "tree" }, null);

    private static void Load(TreeState state, string key, string? repoUrl, params string[] folders)
    {
        state.Roots = folders.Select(Folder).ToList();
        state.Key = key;
        state.RepoUrl = repoUrl;
    }

    private static TreeTabs CloneLoaded(string repo = RepoA, string branch = "main", params string[] ticked)
    {
        var tabs = new TreeTabs();
        Load(tabs.Clone, TreeState.CloneKey(repo, branch), repo, "src", "docs");
        foreach (var root in tabs.Clone.Roots.Where(r => ticked.Contains(r.FullPath)))
            root.IsChecked = true;
        return tabs;
    }

    [Fact]
    public void ManageToClone_WithNoRepo_ShowsEmpty_AndDropsStaleNodes()
    {
        var tabs = new TreeTabs();
        Load(tabs.Manage, TreeState.ManageKey("C:/repo", "abc"), null, "manage-only");

        tabs.Leave(AppMode.Clone, "");
        var action = tabs.EnterClone(null, null);

        Assert.Equal(CloneSwitchAction.ShowEmpty, action);
        Assert.Empty(tabs.For(AppMode.Clone).Roots);
        Assert.NotEmpty(tabs.Manage.Roots);
    }

    [Fact]
    public void CloneTicks_SurviveRoundTrip_WithoutRefetch()
    {
        var tabs = CloneLoaded(ticked: "src");

        tabs.Leave(AppMode.Clone, "src");
        tabs.EnterManage(false);
        tabs.Leave(AppMode.Manage, "");
        var action = tabs.EnterClone(RepoA, "main");

        Assert.Equal(CloneSwitchAction.ShowAsIs, action);
        Assert.Equal(new[] { "src" }, tabs.Clone.GetCheckedPaths());
        Assert.Equal("src", tabs.Clone.SearchText);
    }

    [Fact]
    public void ManagePendingChanges_SurviveRoundTrip()
    {
        var tabs = new TreeTabs();
        Load(tabs.Manage, TreeState.ManageKey("C:/repo", "abc"), null, "src", "docs");
        tabs.Manage.Roots[1].IsChecked = true; // a pending tick

        tabs.Leave(AppMode.Manage, "doc");
        tabs.EnterClone(null, null);
        tabs.Leave(AppMode.Clone, "");
        var action = tabs.EnterManage(checkoutOpenOrOpening: true);

        Assert.Equal(ManageSwitchAction.ShowAsIs, action);
        Assert.Equal(new[] { "docs" }, tabs.Manage.GetCheckedPaths());
        Assert.Equal("doc", tabs.Manage.SearchText);
    }

    [Fact]
    public void ManageSelections_NeverReachClone()
    {
        var tabs = new TreeTabs();
        Load(tabs.Manage, TreeState.ManageKey("C:/repo", "abc"), null, "src");
        tabs.Manage.Roots[0].IsChecked = true;

        Assert.Empty(tabs.PreviousCloneSelection(RepoA));
        Assert.Equal(CloneSwitchAction.Load, tabs.EnterClone(RepoA, "main"));
        Assert.Empty(tabs.Clone.GetCheckedPaths());
    }

    [Fact]
    public void BranchChange_InSameRepo_KeepsTicks_OtherRepoStartsEmpty()
    {
        var tabs = CloneLoaded(ticked: "src");

        Assert.Equal(CloneSwitchAction.Load, tabs.EnterClone(RepoA, "dev"));
        Assert.Equal(new[] { "src" }, tabs.PreviousCloneSelection(RepoA));
        Assert.Empty(tabs.PreviousCloneSelection(RepoB));
    }

    [Fact]
    public void FirstManageVisit_AutoOpens_LaterVisitsDoNot()
    {
        var tabs = new TreeTabs();

        Assert.Equal(ManageSwitchAction.AutoOpenMostRecent, tabs.EnterManage(checkoutOpenOrOpening: false));

        // The auto-open finished and stored its tree.
        Load(tabs.Manage, TreeState.ManageKey("C:/repo", "abc"), null, "src");

        Assert.Equal(ManageSwitchAction.ShowAsIs, tabs.EnterManage(checkoutOpenOrOpening: true));
        Assert.Equal(ManageSwitchAction.ShowAsIs, tabs.EnterManage(checkoutOpenOrOpening: false));
    }

    [Fact]
    public void HostChange_ClearsCloneOnly()
    {
        var tabs = CloneLoaded(ticked: "src");
        Load(tabs.Manage, TreeState.ManageKey("C:/repo", "abc"), null, "src");

        tabs.Clone.Clear();

        Assert.Empty(tabs.Clone.Roots);
        Assert.Null(tabs.Clone.Key);
        Assert.NotEmpty(tabs.Manage.Roots);
        Assert.NotNull(tabs.Manage.Key);
    }
}
