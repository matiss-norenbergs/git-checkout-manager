using GitSparseManager.Models;
using GitSparseManager.Services;

namespace GitSparseManager.Tests;

public class SubmoduleServiceIntegrationTests
{
    [RequiresGitFact]
    public async Task ListAsync_classifies_healthy_orphan_and_broken_submodules()
    {
        using var fx = new GitFixture();

        // A local repo that serves as the healthy submodule's remote.
        var lib = fx.Sub("lib");
        GitFixture.Git(lib, "init");
        GitFixture.Write(Path.Combine(lib, "a.txt"), "a");
        GitFixture.Git(lib, "add", ".");
        GitFixture.Git(lib, "commit", "-m", "lib");
        var libSha = GitFixture.Git(lib, "rev-parse", "HEAD").Trim();

        var main = fx.Sub("main");
        GitFixture.Git(main, "init");
        GitFixture.Write(Path.Combine(main, "readme.md"), "hi");
        GitFixture.Git(main, "add", ".");
        GitFixture.Git(main, "commit", "-m", "init");

        // Healthy: cloned and checked out at the recorded commit.
        GitFixture.Git(main, "submodule", "add", new Uri(lib).AbsoluteUri, "libs/healthy");

        // Bad URL: registered in .gitmodules, but nothing was ever cloned.
        GitFixture.Git(main, "config", "-f", ".gitmodules", "submodule.broken.path", "libs/broken");
        GitFixture.Git(main, "config", "-f", ".gitmodules", "submodule.broken.url", "file:///does/not/exist.git");
        GitFixture.Git(main, "update-index", "--add", "--cacheinfo", $"160000,{libSha},libs/broken");
        Directory.CreateDirectory(Path.Combine(main, "libs", "broken"));

        // Orphan: a gitlink with no .gitmodules entry.
        GitFixture.Git(main, "update-index", "--add", "--cacheinfo", $"160000,{libSha},libs/orphan");
        Directory.CreateDirectory(Path.Combine(main, "libs", "orphan"));

        var list = await new SubmoduleService(new GitService()).ListAsync(main);

        var byPath = list.ToDictionary(s => s.Path);
        Assert.Equal(3, byPath.Count);

        Assert.Equal(SubmoduleState.Ready, byPath["libs/healthy"].State);
        Assert.Equal(libSha, byPath["libs/healthy"].PinnedSha);
        Assert.Equal(libSha, byPath["libs/healthy"].CurrentSha);
        Assert.Equal("libs/healthy", byPath["libs/healthy"].Name);

        Assert.Equal(SubmoduleState.MissingFromGitmodules, byPath["libs/orphan"].State);
        Assert.Null(byPath["libs/orphan"].Name);

        Assert.Equal(SubmoduleState.NotInitialized, byPath["libs/broken"].State);
        Assert.Equal("file:///does/not/exist.git", byPath["libs/broken"].Url);
    }
}
