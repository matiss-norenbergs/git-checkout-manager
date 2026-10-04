using GitCheckoutManager.Models;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Tests;

public class TreeSearchTests
{
    private static TreeNodeViewModel Node(string path, TreeNodeViewModel? parent, bool folder = true)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var node = new TreeNodeViewModel(
            new TreeNode { Path = path, Name = name, Type = folder ? "tree" : "blob" }, parent);
        parent?.Children.Add(node);
        return node;
    }

    private sealed class Tree
    {
        public TreeNodeViewModel Apps, Api, Controllers, UserController, Web, Libs, Core;
        public List<TreeNodeViewModel> Roots => new() { Apps, Libs };

        public Tree()
        {
            Apps = Node("apps", null);
            Api = Node("apps/api", Apps);
            Controllers = Node("apps/api/controllers", Api);
            UserController = Node("apps/api/controllers/UserController.cs", Controllers, folder: false);
            Web = Node("apps/web", Apps);
            Libs = Node("libs", null);
            Core = Node("libs/core", Libs);
        }
    }

    [Fact]
    public void Matches_IsCaseInsensitive_OnName()
    {
        var t = new Tree();
        Assert.True(TreeSearch.Matches(t.UserController, "usercon"));
        Assert.False(TreeSearch.Matches(t.UserController, "apps"));
    }

    [Fact]
    public void Matches_SlashQuery_UsesFullPath()
    {
        var t = new Tree();
        Assert.True(TreeSearch.Matches(t.Controllers, "api/controllers"));
        Assert.False(TreeSearch.Matches(t.Web, "api/controllers"));
    }

    [Fact]
    public void Apply_ExpandsAncestors_ButNotTheMatch()
    {
        var t = new Tree();
        var result = TreeSearch.Apply(t.Roots, "UserController");

        Assert.Equal(1, result.MatchCount);
        Assert.True(t.Apps.IsExpanded);
        Assert.True(t.Api.IsExpanded);
        Assert.True(t.Controllers.IsExpanded);
        Assert.False(t.UserController.IsExpanded);
        Assert.False(t.Libs.IsExpanded);
    }

    [Fact]
    public void Apply_HidesNonMatchingBranches()
    {
        var t = new Tree();
        TreeSearch.Apply(t.Roots, "UserController");

        Assert.True(t.Apps.IsVisible);
        Assert.True(t.UserController.IsVisible);
        Assert.False(t.Web.IsVisible);
        Assert.False(t.Libs.IsVisible);
    }

    [Fact]
    public void Apply_MatchingFolder_KeepsAllChildrenVisible_AndIsNotExpanded()
    {
        var t = new Tree();
        var result = TreeSearch.Apply(t.Roots, "api");

        Assert.Equal(1, result.MatchCount);
        Assert.True(t.Api.IsVisible);
        Assert.True(t.Controllers.IsVisible);
        Assert.True(t.UserController.IsVisible);
        Assert.False(t.Api.IsExpanded);
        Assert.True(t.Apps.IsExpanded);
    }

    [Fact]
    public void Apply_OverLimit_FiltersButSkipsExpansion()
    {
        var root = Node("root", null);
        for (var i = 0; i < TreeSearch.MaxAutoExpandMatches + 1; i++)
            Node($"root/item{i}", root);

        var result = TreeSearch.Apply(new[] { root }, "item");

        Assert.Equal(TreeSearch.MaxAutoExpandMatches + 1, result.MatchCount);
        Assert.False(result.Expanded);
        Assert.False(root.IsExpanded);
    }

    [Fact]
    public void Apply_AtLimit_StillExpands()
    {
        var root = Node("root", null);
        for (var i = 0; i < TreeSearch.MaxAutoExpandMatches; i++)
            Node($"root/item{i}", root);

        var result = TreeSearch.Apply(new[] { root }, "item");

        Assert.True(result.Expanded);
        Assert.True(root.IsExpanded);
    }

    [Fact]
    public void Apply_NoMatches_HidesEverything()
    {
        var t = new Tree();
        var result = TreeSearch.Apply(t.Roots, "zzz");

        Assert.Equal(0, result.MatchCount);
        Assert.False(t.Apps.IsVisible);
        Assert.False(t.Libs.IsVisible);
    }

    [Fact]
    public void Apply_NeverTouchesCheckedState()
    {
        var t = new Tree();
        t.Core.IsChecked = true;
        t.Web.IsChecked = true;

        TreeSearch.Apply(t.Roots, "UserController");
        TreeSearch.Apply(t.Roots, "");

        Assert.True(t.Core.IsChecked);
        Assert.True(t.Web.IsChecked);
        Assert.Null(t.Apps.IsChecked); // indeterminate: web checked, api not
    }

    [Fact]
    public void Restore_PutsExpansionBackExactly()
    {
        var t = new Tree();
        t.Libs.IsExpanded = true;
        t.Controllers.IsExpanded = true;

        var snapshot = TreeSearch.CaptureExpansion(t.Roots);
        TreeSearch.Apply(t.Roots, "UserController");
        Assert.True(t.Apps.IsExpanded);

        TreeSearch.Clear(t.Roots);
        TreeSearch.RestoreExpansion(snapshot);

        Assert.False(t.Apps.IsExpanded);
        Assert.False(t.Api.IsExpanded);
        Assert.True(t.Controllers.IsExpanded);
        Assert.True(t.Libs.IsExpanded);
        Assert.True(t.Apps.IsVisible);
        Assert.True(t.Web.IsVisible);
    }

    [Fact]
    public void Apply_EmptyFilter_ClearsVisibilityAndHighlight()
    {
        var t = new Tree();
        TreeSearch.Apply(t.Roots, "UserController");
        TreeSearch.Apply(t.Roots, "");

        Assert.True(t.Web.IsVisible);
        Assert.Equal(string.Empty, t.UserController.NameMatch);
        Assert.Equal("UserController.cs", t.UserController.NameBefore);
    }

    [Fact]
    public void Highlight_SplitsNameIntoBeforeMatchAfter()
    {
        var t = new Tree();
        TreeSearch.Apply(t.Roots, "usercon");

        Assert.Equal("", t.UserController.NameBefore);
        Assert.Equal("UserCon", t.UserController.NameMatch);
        Assert.Equal("troller.cs", t.UserController.NameAfter);
    }

    [Fact]
    public void Highlight_PathQuery_HighlightsOverlapWithName()
    {
        var t = new Tree();
        TreeSearch.Apply(t.Roots, "api/contr");

        Assert.Equal("contr", t.Controllers.NameMatch);
        Assert.Equal("", t.Controllers.NameBefore);
        Assert.Equal("ollers", t.Controllers.NameAfter);
        // Matched via path, but the match does not reach its name
        Assert.Equal(string.Empty, t.Api.NameMatch);
    }

    [Theory]
    [InlineData(-1, 3, true, 0)]
    [InlineData(-1, 3, false, 2)]
    [InlineData(0, 3, true, 1)]
    [InlineData(2, 3, true, 0)]
    [InlineData(0, 3, false, 2)]
    [InlineData(2, 3, false, 1)]
    [InlineData(0, 1, true, 0)]
    [InlineData(0, 1, false, 0)]
    [InlineData(5, 3, true, 0)]
    public void NextIndex_wraps_around(int current, int count, bool forward, int expected)
    {
        Assert.Equal(expected, TreeSearch.NextIndex(current, count, forward));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(3, true)]
    public void NextIndex_is_minus_one_without_matches(int current, bool forward)
    {
        Assert.Equal(-1, TreeSearch.NextIndex(current, 0, forward));
    }

    [Fact]
    public void FindMatches_returns_matches_in_tree_order()
    {
        var t = new Tree();
        var matches = TreeSearch.FindMatches(t.Roots, "api");

        Assert.Equal(new[] { t.Api }, matches);
        Assert.Equal(new[] { t.Controllers, t.UserController, t.Core }, TreeSearch.FindMatches(t.Roots, "c"));
        Assert.Empty(TreeSearch.FindMatches(t.Roots, ""));
        Assert.Empty(TreeSearch.FindMatches(t.Roots, "zzz"));
    }
}
