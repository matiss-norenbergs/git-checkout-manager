using GitCheckoutManager.Models;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Tests;

public class CheckedOutFilterTests
{
    private static TreeNodeViewModel Node(string path, TreeNodeViewModel? parent, bool folder = true, bool submodule = false)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var node = new TreeNodeViewModel(
            new TreeNode { Path = path, Name = name, Type = folder ? "tree" : "blob", IsSubmodule = submodule }, parent);
        parent?.Children.Add(node);
        return node;
    }

    private sealed class Tree
    {
        public TreeNodeViewModel RootFile, Apps, Api, ApiFile, Controllers, Web, WebFile, Libs, Core, Vendor, Docs, DocsFile;
        public List<TreeNodeViewModel> Roots => new() { RootFile, Apps, Libs, Docs };

        public Tree()
        {
            RootFile = Node("README.md", null, folder: false);
            Apps = Node("apps", null);
            Api = Node("apps/api", Apps);
            ApiFile = Node("apps/api/api.csproj", Api, folder: false);
            Controllers = Node("apps/api/controllers", Api);
            Web = Node("apps/web", Apps);
            WebFile = Node("apps/web/index.html", Web, folder: false);
            Libs = Node("libs", null);
            Core = Node("libs/core", Libs);
            Vendor = Node("libs/vendor", Libs, submodule: true);
            Docs = Node("docs", null);
            DocsFile = Node("docs/guide.md", Docs, folder: false);
        }

        public IEnumerable<TreeNodeViewModel> All() => Walk(Roots);

        private static IEnumerable<TreeNodeViewModel> Walk(IEnumerable<TreeNodeViewModel> nodes)
        {
            foreach (var n in nodes)
            {
                yield return n;
                foreach (var c in Walk(n.Children)) yield return c;
            }
        }
    }

    [Theory]
    [InlineData(false, false, false, false, null)]
    [InlineData(true, false, false, false, "This checkout includes all files.")]
    [InlineData(true, true, false, false, "Only available for cone-mode sparse checkouts.")]
    [InlineData(true, true, true, true, null)]
    public void Availability_RequiresOpenSparseConeCheckout(bool open, bool sparse, bool cone, bool available, string? tip)
    {
        Assert.Equal(available, CheckedOutFilter.IsAvailable(open, sparse, cone));
        Assert.Equal(tip, CheckedOutFilter.UnavailableToolTip(open, sparse, cone));
    }

    [Fact]
    public void FilterOff_ShowsEverything()
    {
        var t = new Tree();
        CheckedOutFilter.Evaluate(t.Roots, true, new[] { "apps/api" });
        Assert.False(t.Docs.IsVisibleInFilter);

        CheckedOutFilter.Evaluate(t.Roots, false, new[] { "apps/api" });

        Assert.All(t.All(), n => Assert.True(n.IsVisibleInFilter));
    }

    [Fact]
    public void BaselineFolder_ShowsAncestorsDescendantsAndDirectFiles()
    {
        var t = new Tree();
        t.Api.IsChecked = true;

        CheckedOutFilter.Evaluate(t.Roots, true, new[] { "apps/api" });

        Assert.True(t.Apps.IsVisibleInFilter);
        Assert.True(t.Api.IsVisibleInFilter);
        Assert.True(t.ApiFile.IsVisibleInFilter);
        Assert.True(t.Controllers.IsVisibleInFilter);
    }

    [Fact]
    public void RootFiles_AreAlwaysShown()
    {
        var t = new Tree();

        CheckedOutFilter.Evaluate(t.Roots, true, Array.Empty<string>());

        Assert.True(t.RootFile.IsVisibleInFilter);
        Assert.True(CheckedOutFilter.IsEmpty(t.Roots));
    }

    [Fact]
    public void UnrelatedFoldersAndFiles_AreHidden()
    {
        var t = new Tree();
        t.Api.IsChecked = true;

        CheckedOutFilter.Evaluate(t.Roots, true, new[] { "apps/api" });

        Assert.False(t.Web.IsVisibleInFilter);
        Assert.False(t.WebFile.IsVisibleInFilter);
        Assert.False(t.Libs.IsVisibleInFilter);
        Assert.False(t.Core.IsVisibleInFilter);
        Assert.False(t.Docs.IsVisibleInFilter);
        Assert.False(t.DocsFile.IsVisibleInFilter);
        Assert.False(CheckedOutFilter.IsEmpty(t.Roots));
    }

    [Fact]
    public void PendingAdd_IsShown_AndPendingRemovalStaysShown()
    {
        var t = new Tree();
        t.Api.IsChecked = true;
        t.Docs.IsChecked = true;      // pending add
        t.Api.IsChecked = false;      // pending removal (still in baseline)

        CheckedOutFilter.Evaluate(t.Roots, true, new[] { "apps/api" });

        Assert.True(t.Docs.IsVisibleInFilter);
        Assert.True(t.DocsFile.IsVisibleInFilter);
        Assert.True(t.Api.IsVisibleInFilter);
        Assert.True(t.Controllers.IsVisibleInFilter);
    }

    [Fact]
    public void GitlinkInsideSelectedFolder_IsShown()
    {
        var t = new Tree();
        t.Libs.IsChecked = true;

        CheckedOutFilter.Evaluate(t.Roots, true, new[] { "libs" });

        Assert.True(t.Vendor.IsSubmodule);
        Assert.True(t.Vendor.IsVisibleInFilter);
        Assert.True(t.Core.IsVisibleInFilter);
    }

    [Fact]
    public void Grow_RevealsNewlySelectedSubtree_WholeFolderSelectOfPartialNode()
    {
        var t = new Tree();
        t.Controllers.IsChecked = true; // apps and apps/api become partial
        CheckedOutFilter.Evaluate(t.Roots, true, new[] { "apps/api/controllers" });
        Assert.False(t.Web.IsVisibleInFilter);
        Assert.False(t.WebFile.IsVisibleInFilter);

        t.Apps.IsChecked = true;        // click on a partial folder: select the whole folder
        CheckedOutFilter.Grow(t.Apps);

        Assert.True(t.Web.IsVisibleInFilter);
        Assert.True(t.WebFile.IsVisibleInFilter);
        Assert.True(t.Api.IsVisibleInFilter);
        Assert.True(t.ApiFile.IsVisibleInFilter);
        Assert.False(t.Docs.IsVisibleInFilter);
    }

    [Fact]
    public void Grow_RevealsAncestorsAndTheirFiles_ForANewlyTickedFolder()
    {
        var t = new Tree();
        CheckedOutFilter.Evaluate(t.Roots, true, Array.Empty<string>());
        Assert.False(t.Apps.IsVisibleInFilter);

        t.Controllers.IsChecked = true;
        CheckedOutFilter.Grow(t.Controllers);

        Assert.True(t.Apps.IsVisibleInFilter);
        Assert.True(t.Api.IsVisibleInFilter);
        Assert.True(t.ApiFile.IsVisibleInFilter);
        Assert.True(t.Controllers.IsVisibleInFilter);
        Assert.False(t.Web.IsVisibleInFilter);
    }

    [Fact]
    public void Grow_NeverHides_AnUntickedNode()
    {
        var t = new Tree();
        t.Api.IsChecked = true;
        CheckedOutFilter.Evaluate(t.Roots, true, Array.Empty<string>());
        Assert.True(t.Api.IsVisibleInFilter);

        t.Api.IsChecked = false;
        CheckedOutFilter.Grow(t.Api);

        Assert.True(t.Api.IsVisibleInFilter);
        Assert.True(t.Apps.IsVisibleInFilter);
        Assert.True(t.ApiFile.IsVisibleInFilter);
    }

    [Fact]
    public void FullReevaluation_AfterUntick_HidesNodeNoLongerInBaseline()
    {
        var t = new Tree();
        t.Api.IsChecked = true;
        CheckedOutFilter.Evaluate(t.Roots, true, new[] { "apps/api" });
        Assert.True(t.Api.IsVisibleInFilter);

        t.Api.IsChecked = false;
        CheckedOutFilter.Evaluate(t.Roots, true, Array.Empty<string>());

        Assert.False(t.Api.IsVisibleInFilter);
        Assert.False(t.Apps.IsVisibleInFilter);
        Assert.False(t.ApiFile.IsVisibleInFilter);
        Assert.True(t.RootFile.IsVisibleInFilter);
    }

    [Fact]
    public void Evaluate_DoesNotChangeCheckState()
    {
        var t = new Tree();
        t.Api.IsChecked = true;
        var before = t.All().Select(n => n.IsChecked).ToList();

        CheckedOutFilter.Evaluate(t.Roots, true, new[] { "apps/api" });
        CheckedOutFilter.Evaluate(t.Roots, false, new[] { "apps/api" });

        Assert.Equal(before, t.All().Select(n => n.IsChecked).ToList());
    }

    [Fact]
    public void UntickedBaselineFolder_KeepsItsDirectFilesVisible()
    {
        var t = new Tree();
        t.Api.IsChecked = true;
        t.Api.IsChecked = false;      // pending removal; its files stay on disk until apply

        CheckedOutFilter.Evaluate(t.Roots, true, new[] { "apps/api" });

        Assert.False(t.ApiFile.IsIncluded);
        Assert.True(t.ApiFile.IsVisibleInFilter);
        Assert.True(t.Api.IsVisibleInFilter);
        Assert.False(t.WebFile.IsVisibleInFilter);
    }
}
