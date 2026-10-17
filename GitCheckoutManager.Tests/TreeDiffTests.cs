using GitCheckoutManager.Models;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Tests;

public class TreeDiffTests
{
    private static TreeNode Dir(string path) => new() { Path = path, Name = path[(path.LastIndexOf('/') + 1)..], Type = "tree" };
    private static TreeNode File(string path) => new() { Path = path, Name = path[(path.LastIndexOf('/') + 1)..], Type = "blob" };
    private static TreeNode Sub(string path) => new() { Path = path, Name = path[(path.LastIndexOf('/') + 1)..], Type = "tree", IsSubmodule = true };

    private static int Order(TreeNodeViewModel a, TreeNodeViewModel b) =>
        a.IsFolder != b.IsFolder ? (a.IsFolder ? -1 : 1) : string.CompareOrdinal(a.Name, b.Name);

    /// <summary>Builds the tree the same way the app does: parents first, children attached to them.</summary>
    private static List<TreeNodeViewModel> Build(params TreeNode[] flat)
    {
        var roots = new List<TreeNodeViewModel>();
        var map = new Dictionary<string, TreeNodeViewModel>();
        foreach (var n in flat.OrderBy(n => n.Path, StringComparer.Ordinal))
        {
            var slash = n.Path.LastIndexOf('/');
            if (slash < 0) { var vm = new TreeNodeViewModel(n); roots.Add(vm); map[n.Path] = vm; }
            else { var p = map[n.Path[..slash]]; var vm = new TreeNodeViewModel(n, p); p.Children.Add(vm); map[n.Path] = vm; }
        }
        roots.Sort(Order);
        return roots;
    }

    private static TreeNodeViewModel Find(IEnumerable<TreeNodeViewModel> nodes, string path)
    {
        foreach (var n in nodes)
        {
            if (n.FullPath == path) return n;
            if (path.StartsWith(n.FullPath + "/") && Find(n.Children, path) is { } hit) return hit;
        }
        return null!;
    }

    private static List<string> Names(IEnumerable<TreeNodeViewModel> nodes) => nodes.Select(n => n.Name).ToList();

    [Fact]
    public void NoChange_KeepsEveryNodeObject()
    {
        var roots = Build(Dir("src"), File("src/a.cs"), File("readme"));
        var src = roots.Single(r => r.Name == "src");
        src.IsExpanded = true;

        var result = TreeDiff.TryApply(roots, new[] { Dir("src"), File("src/a.cs"), File("readme") }, Order);

        Assert.Equal(new TreeDiffResult(0, 0), result);
        Assert.Same(src, roots.Single(r => r.Name == "src"));
        Assert.True(src.IsExpanded);
    }

    [Fact]
    public void AddedFile_IsInsertedInOrder_ExistingNodesKept()
    {
        var roots = Build(Dir("src"), File("src/a.cs"), File("src/c.cs"));
        var a = Find(roots, "src/a.cs");

        var result = TreeDiff.TryApply(roots, new[] { Dir("src"), File("src/a.cs"), File("src/b.cs"), File("src/c.cs") }, Order);

        Assert.Equal(new TreeDiffResult(1, 0), result);
        var src = roots.Single();
        Assert.Equal(new[] { "a.cs", "b.cs", "c.cs" }, Names(src.Children));
        Assert.Same(a, src.Children[0]);
    }

    [Fact]
    public void RemovedFolder_TakesItsSubtreeWith_AndCountsAllRemovedEntries()
    {
        var roots = Build(Dir("a"), Dir("a/b"), File("a/b/x"), Dir("keep"));

        var result = TreeDiff.TryApply(roots, new[] { Dir("keep") }, Order);

        Assert.Equal(new TreeDiffResult(0, 3), result);
        Assert.Equal(new[] { "keep" }, Names(roots));
    }

    [Fact]
    public void NewFolderWithChildren_IsAdded_ParentBeforeChild()
    {
        var roots = Build(Dir("src"));

        var result = TreeDiff.TryApply(roots, new[] { Dir("src"), Dir("src/new"), File("src/new/f.txt") }, Order);

        Assert.Equal(new TreeDiffResult(2, 0), result);
        Assert.Equal("f.txt", Find(roots, "src/new").Children.Single().Name);
    }

    [Fact]
    public void NewFolder_UnderFullyTickedFolder_IsTicked()
    {
        var roots = Build(Dir("src"), Dir("src/a"));
        roots.Single().IsChecked = true;

        TreeDiff.TryApply(roots, new[] { Dir("src"), Dir("src/a"), Dir("src/b") }, Order);

        Assert.True(Find(roots, "src/b").IsChecked);
        Assert.True(roots.Single().IsChecked);
    }

    [Fact]
    public void NewFolder_UnderUntickedFolder_StaysUnticked_AndPartialParentRecomputes()
    {
        var roots = Build(Dir("src"), Dir("src/a"), Dir("src/b"));
        Find(roots, "src/a").IsChecked = true; // src becomes partly ticked
        Assert.Null(roots.Single().IsChecked);

        // The only unticked sibling disappears, so the parent is now fully ticked.
        TreeDiff.TryApply(roots, new[] { Dir("src"), Dir("src/a") }, Order);

        Assert.True(roots.Single().IsChecked);
    }

    [Fact]
    public void RemovingTheTickedSubfolder_ClearsThePartialState()
    {
        var roots = Build(Dir("src"), Dir("src/a"), Dir("src/b"));
        Find(roots, "src/a").IsChecked = true;

        TreeDiff.TryApply(roots, new[] { Dir("src") }, Order);

        Assert.False(roots.Single().IsChecked);
        Assert.Empty(roots.Single().GetCheckedPaths());
    }

    [Fact]
    public void KindChange_ReplacesTheNode()
    {
        var roots = Build(Dir("x"), Dir("x/inner"));
        var old = roots.Single();

        var result = TreeDiff.TryApply(roots, new[] { File("x") }, Order);

        Assert.Equal(new TreeDiffResult(1, 2), result);
        Assert.NotSame(old, roots.Single());
        Assert.False(roots.Single().IsFolder);
    }

    [Fact]
    public void FolderBecomingSubmodule_IsReplaced()
    {
        var roots = Build(Dir("lib"));

        TreeDiff.TryApply(roots, new[] { Sub("lib") }, Order);

        Assert.True(roots.Single().IsSubmodule);
    }

    [Fact]
    public void RootChanges_AreMirroredIntoTheBoundCollection()
    {
        var roots = Build(Dir("a"), Dir("c"), File("old"));
        var shown = new System.Collections.ObjectModel.ObservableCollection<TreeNodeViewModel>(roots);

        TreeDiff.TryApply(roots, new[] { Dir("a"), Dir("b"), Dir("c") }, Order, shown);

        Assert.Equal(new[] { "a", "b", "c" }, Names(roots));
        Assert.Equal(Names(roots), Names(shown));
    }

    [Fact]
    public void HugeChange_DeclinesAndChangesNothing()
    {
        var oldEntries = Enumerable.Range(0, 10).Select(i => File($"old{i}")).ToArray();
        var roots = Build(oldEntries);
        var newEntries = Enumerable.Range(0, TreeDiff.RebuildThreshold + 1).Select(i => File($"new{i}")).ToArray();

        var result = TreeDiff.TryApply(roots, newEntries, Order);

        Assert.Null(result);
        Assert.Equal(10, roots.Count);
    }
}
