using GitSparseManager.Services;

namespace GitSparseManager.Tests;

public class CheckoutServiceIntegrationTests
{
    private static string MakeSource(GitFixture fx)
    {
        var src = fx.Sub("src");
        GitFixture.Git(src, "init");
        foreach (var f in new[] { "apps/api/a.txt", "apps/web/w.txt", "docs/d.md", "root.txt" })
            GitFixture.Write(Path.Combine(src, f.Replace('/', Path.DirectorySeparatorChar)), f);
        GitFixture.Git(src, "add", ".");
        GitFixture.Git(src, "commit", "-m", "init");
        return src;
    }

    [RequiresGitFact]
    public async Task OpenAsync_reports_cone_sparse_checkout()
    {
        using var fx = new GitFixture();
        var src = MakeSource(fx);
        var clone = Path.Combine(fx.Root, "clone");

        GitFixture.Git(fx.Root, "clone", "--no-checkout", new Uri(src).AbsoluteUri, clone);
        GitFixture.Git(clone, "sparse-checkout", "init", "--cone");
        GitFixture.Git(clone, "sparse-checkout", "set", "apps/web", "docs");
        GitFixture.Git(clone, "checkout", "main");

        var info = await new CheckoutService(new GitService()).OpenAsync(clone);

        Assert.True(info.IsSparse);
        Assert.True(info.IsCone);
        Assert.Equal(new[] { "apps/web", "docs" }, info.SparsePaths.OrderBy(p => p));
        Assert.Equal("main", info.Branch);
        Assert.Equal(0, info.ChangedFileCount);
        Assert.NotNull(info.RemoteUrl);
        Assert.Equal(Path.GetFullPath(clone), info.Root, ignoreCase: true);
    }

    [RequiresGitFact]
    public async Task OpenAsync_reports_full_checkout_as_not_sparse()
    {
        using var fx = new GitFixture();
        var src = MakeSource(fx);

        var info = await new CheckoutService(new GitService()).OpenAsync(src);

        Assert.False(info.IsSparse);
        Assert.False(info.IsCone);
        Assert.Empty(info.SparsePaths);
    }

    [RequiresGitFact]
    public async Task OpenAsync_throws_outside_a_repository()
    {
        using var fx = new GitFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CheckoutService(new GitService()).OpenAsync(fx.Root));
    }
}
