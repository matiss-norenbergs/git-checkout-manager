using GitSparseManager.Models;
using GitSparseManager.ViewModels;

namespace GitSparseManager.Tests;

public class RemovalPlannerTests
{
    private static TreeNodeViewModel Folder(string path, TreeNodeViewModel? parent = null, params string[] children)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var node = new TreeNodeViewModel(new TreeNode { Path = path, Name = name, Type = "tree" }, parent);
        foreach (var c in children) node.Children.Add(Folder(path + "/" + c, node));
        return node;
    }

    /// <summary>apps/{api,web,worker}, libs/{core}, docs.</summary>
    private static List<TreeNodeViewModel> SampleTree() => new()
    {
        Folder("apps", null, "api", "web", "worker"),
        Folder("libs", null, "core"),
        Folder("docs"),
    };

    [Theory]
    [InlineData("apps", "apps", true)]
    [InlineData("apps", "APPS", true)]
    [InlineData("apps/web", "apps", true)]
    [InlineData("apps", "apps/web", false)]
    [InlineData("apps2", "apps", false)]
    public void IsCoveredBy_matches_equal_or_nested(string path, string entry, bool expected)
    {
        Assert.Equal(expected, RemovalPlanner.IsCoveredBy(new[] { entry }, path));
    }

    [Fact]
    public void Added_and_removed_are_computed_against_baseline()
    {
        var plan = RemovalPlanner.Plan(
            new[] { "apps", "docs" }, new[] { "apps/web", "libs" }, SampleTree());

        Assert.Equal(new[] { "libs" }, plan.Added); // apps/web is covered by baseline "apps"
        Assert.Contains("docs", plan.Removed);
        Assert.Contains("apps", plan.Removed);      // narrowed: "apps" itself is no longer covered
    }

    [Fact]
    public void Plain_removal_targets_the_removed_folder()
    {
        var plan = RemovalPlanner.Plan(new[] { "apps", "docs" }, new[] { "apps" }, SampleTree());

        Assert.Equal(new[] { "docs" }, plan.Removed);
        Assert.Equal(new[] { "docs" }, plan.Targets);
        Assert.Empty(plan.Dropped);
    }

    [Fact]
    public void Narrowing_keeps_selected_subfolder_and_removes_siblings()
    {
        var tree = SampleTree();
        var selected = new[] { "apps/web" };

        var plan = RemovalPlanner.Plan(new[] { "apps" }, selected, tree);

        Assert.Equal(new[] { "apps" }, plan.Removed);
        Assert.Equal(new[] { "apps/api", "apps/worker" }, plan.Targets.OrderBy(t => t));
        Assert.Empty(plan.Dropped);
        AssertNoSelectedOrAncestor(plan.Targets, selected);
    }

    [Fact]
    public void Nested_removal_descends_to_siblings_off_the_selected_path()
    {
        var apps = Folder("apps");
        apps.Children.Add(Folder("apps/web", apps, "ui", "server"));
        apps.Children.Add(Folder("apps/api", apps));
        var tree = new List<TreeNodeViewModel> { apps };
        var selected = new[] { "apps/web/ui" };

        var targets = RemovalPlanner.ExpandRemovalTargets(new[] { "apps" }, selected, tree);

        Assert.Equal(new[] { "apps/api", "apps/web/server" }, targets.OrderBy(t => t));
        AssertNoSelectedOrAncestor(targets, selected);
    }

    [Fact]
    public void Full_clone_baseline_of_all_roots_can_be_narrowed()
    {
        var tree = SampleTree();
        var baseline = tree.Select(n => n.FullPath).ToList(); // full clone: every root folder
        var selected = new[] { "apps/api", "docs" };

        var plan = RemovalPlanner.Plan(baseline, selected, tree);

        Assert.Equal(new[] { "apps/web", "apps/worker", "libs" }, plan.Targets.OrderBy(t => t));
        Assert.Empty(plan.Added);
        AssertNoSelectedOrAncestor(plan.Targets, selected);
    }

    [Fact]
    public void Unknown_removed_folder_with_selection_under_it_yields_no_targets()
    {
        var targets = RemovalPlanner.ExpandRemovalTargets(new[] { "ghost" }, new[] { "ghost/x" }, SampleTree());
        Assert.Empty(targets);
    }

    private static void AssertNoSelectedOrAncestor(IEnumerable<string> targets, IEnumerable<string> selected)
    {
        foreach (var t in targets)
            foreach (var s in selected)
            {
                Assert.NotEqual(s, t);
                Assert.False(s.StartsWith(t + "/"), $"{t} is an ancestor of selected {s}");
            }
    }
}
