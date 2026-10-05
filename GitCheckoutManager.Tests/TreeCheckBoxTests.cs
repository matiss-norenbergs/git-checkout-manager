using GitCheckoutManager.Controls;
using GitCheckoutManager.Models;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Tests;

public class TreeCheckBoxTests
{
    private static TreeNodeViewModel Node(string path, TreeNodeViewModel? parent, bool folder = true)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var node = new TreeNodeViewModel(
            new TreeNode { Path = path, Name = name, Type = folder ? "tree" : "blob" }, parent);
        parent?.Children.Add(node);
        return node;
    }

    // What TreeCheckBox.OnToggle does to the bound value.
    private static void Toggle(TreeNodeViewModel n) => n.IsChecked = n.IsChecked != true;

    private sealed class Tree
    {
        public TreeNodeViewModel Src, Api, Web, Docs, Readme, ApiFile;

        public Tree()
        {
            Src = Node("src", null);
            Api = Node("src/api", Src);
            ApiFile = Node("src/api/a.cs", Api, folder: false);
            Web = Node("src/web", Src);
            Docs = Node("docs", null);
            Readme = Node("docs/readme.md", Docs, folder: false);
        }

        public IEnumerable<string> Paths => Src.GetCheckedPaths().Concat(Docs.GetCheckedPaths());
    }

    [Fact]
    public void LeafFolder_Toggle_NeverMiddle_AndFilesMatchPaths()
    {
        var t = new Tree();
        for (var i = 0; i < 4; i++)
        {
            Toggle(t.Docs);
            Assert.NotNull(t.Docs.IsChecked);
            Assert.Equal(t.Docs.IsChecked == true, t.Readme.IsIncluded);
            Assert.Equal(t.Readme.IsIncluded, t.Paths.Contains("docs"));
        }
    }

    [Fact]
    public void MiddleParent_Toggle_SelectsWholeSubtree()
    {
        var t = new Tree();
        t.Api.IsChecked = true;
        Assert.Null(t.Src.IsChecked);

        Toggle(t.Src);

        Assert.True(t.Src.IsChecked);
        Assert.True(t.Api.IsChecked);
        Assert.True(t.Web.IsChecked);
        Assert.Equal(new[] { "src" }, t.Paths);
    }

    [Fact]
    public void Ticked_Toggle_UnticksSubtree_AndUnticked_Toggle_Ticks()
    {
        var t = new Tree();
        Toggle(t.Src);
        Assert.True(t.Api.IsChecked);

        Toggle(t.Src);
        Assert.False(t.Src.IsChecked);
        Assert.False(t.Api.IsChecked);
        Assert.False(t.Web.IsChecked);
        Assert.Empty(t.Paths);

        Toggle(t.Src);
        Assert.True(t.Src.IsChecked);
    }

    [Fact]
    public void NullSetFromOutside_IsNormalisedToTicked()
    {
        var t = new Tree();
        t.Src.IsChecked = null;

        Assert.True(t.Src.IsChecked);
        Assert.True(t.Api.IsChecked);
        Assert.True(t.Web.IsChecked);
        Assert.Equal(new[] { "src" }, t.Paths);
    }

    [Fact]
    public void ManageBaseline_SingleChild_ShowsParentInMiddle()
    {
        var t = new Tree();
        t.Api.IsChecked = true;

        Assert.Null(t.Src.IsChecked);
        Assert.False(t.Web.IsChecked);
        Assert.Equal(new[] { "src/api" }, t.Paths);
    }

    private sealed class ExposedTreeCheckBox : TreeCheckBox
    {
        public void Toggle() => OnToggle();
    }

    [Fact]
    public void TreeCheckBox_OnToggle_MiddleAndUnticked_BecomeTicked_TickedBecomesUnticked()
    {
        bool? Run(bool? start)
        {
            bool? result = null;
            var thread = new Thread(() =>
            {
                var box = new ExposedTreeCheckBox { IsChecked = start };
                box.Toggle();
                result = box.IsChecked;
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }

        Assert.True(Run(null));
        Assert.False(Run(true));
        Assert.True(Run(false));
    }
}
