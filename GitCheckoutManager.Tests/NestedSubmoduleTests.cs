using GitCheckoutManager.Models;
using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

/// <summary>Submodules inside submodules, against real git and file:// repos.</summary>
public class NestedSubmoduleTests
{
    static NestedSubmoduleTests()
    {
        // GitService children inherit this: newer git blocks the file transport for submodule clones.
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "protocol.file.allow");
        Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "always");
    }

    private static string Commit(string repo, string file)
    {
        GitFixture.Git(repo, "init");
        GitFixture.Write(Path.Combine(repo, file), file);
        GitFixture.Git(repo, "add", ".");
        GitFixture.Git(repo, "commit", "-m", "c");
        return GitFixture.Git(repo, "rev-parse", "HEAD").Trim();
    }

    /// <summary>main → external/lib → vendor/x. Cloning a submodule does not initialize the one inside it.</summary>
    private static (string Main, string InnerSha) Setup(GitFixture fx)
    {
        var inner = fx.Sub("inner");
        var innerSha = Commit(inner, "i.txt");

        var lib = fx.Sub("lib");
        Commit(lib, "l.txt");
        GitFixture.Git(lib, "submodule", "add", new Uri(inner).AbsoluteUri, "vendor/x");
        GitFixture.Git(lib, "commit", "-m", "add inner");

        var main = fx.Sub("main");
        Commit(main, "m.txt");
        GitFixture.Git(main, "submodule", "add", new Uri(lib).AbsoluteUri, "external/lib");
        GitFixture.Git(main, "commit", "-m", "add lib");
        return (main, innerSha);
    }

    [RequiresGitFact]
    public async Task ListAsync_lists_the_submodules_of_a_populated_submodule_as_nested_rows()
    {
        using var fx = new GitFixture();
        var (main, _) = Setup(fx);
        var svc = new SubmoduleService(new GitService());

        var list = await svc.ListAsync(main);

        Assert.Equal(2, list.Count);

        var top = list[0];
        Assert.Equal("external/lib", top.Path);
        Assert.Equal("external/lib", top.DisplayPath);
        Assert.Equal(0, top.Depth);
        Assert.Equal(main, top.RepoRoot);
        Assert.Equal(SubmoduleState.Ready, top.State);

        var nested = list[1];
        Assert.Equal("vendor/x", nested.Path);
        Assert.Equal("external/lib/vendor/x", nested.DisplayPath);
        Assert.Equal(1, nested.Depth);
        Assert.Equal(Path.Combine(main, "external", "lib"), nested.RepoRoot);
        Assert.Equal(SubmoduleState.NotInitialized, nested.State);
    }

    [RequiresGitFact]
    public async Task InitAndUpdate_on_a_nested_row_runs_against_its_RepoRoot()
    {
        using var fx = new GitFixture();
        var (main, innerSha) = Setup(fx);
        var svc = new SubmoduleService(new GitService());

        var nested = (await svc.ListAsync(main))[1];
        var result = await svc.InitAndUpdateAsync(nested.RepoRoot, nested, false, false, _ => null);
        Assert.True(result.ExitCode == 0, result.StdErr);

        var fresh = await svc.GetAsync(nested.RepoRoot, nested.Path, default, nested.DisplayPrefix, nested.Depth);
        Assert.NotNull(fresh);
        Assert.Equal(SubmoduleState.Ready, fresh!.State);
        Assert.Equal(innerSha, fresh.CurrentSha);
        Assert.Equal("external/lib/vendor/x", fresh.DisplayPath);
        Assert.Equal(1, fresh.Depth);
        Assert.Equal(nested.RepoRoot, fresh.RepoRoot);

        // The refreshed listing keeps the same shape, now with the nested row ready.
        var list = await svc.ListAsync(main);
        Assert.Equal(2, list.Count);
        Assert.All(list, s => Assert.Equal(SubmoduleState.Ready, s.State));
    }

    [RequiresGitFact]
    public async Task Unpopulated_parent_has_no_children()
    {
        using var fx = new GitFixture();
        var (main, _) = Setup(fx);
        var svc = new SubmoduleService(new GitService());

        // Empty the populated submodule's folder: it is then a plain gitlink folder again.
        var folder = Path.Combine(main, "external", "lib");
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);

        var list = await svc.ListAsync(main);

        var only = Assert.Single(list);
        Assert.Equal(SubmoduleState.NotInitialized, only.State);
    }
}
