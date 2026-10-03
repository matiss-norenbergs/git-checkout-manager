using GitCheckoutManager.Models;
using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

/// <summary>Local, checkout-only fixes for broken submodules, against real git and file:// repos.</summary>
public class SubmoduleRepairTests
{
    static SubmoduleRepairTests()
    {
        // GitService children inherit this: newer git blocks the file transport for submodule clones.
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "protocol.file.allow");
        Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "always");
    }

    private static readonly Func<string, GitAuth?> NoAuth = _ => null;

    private static SubmoduleService NewService() => new(new GitService());

    /// <summary>A library repo plus a committed main repo whose tree records a gitlink for each given path.</summary>
    private static (string Main, string LibUrl, string LibSha) Setup(GitFixture fx, string? registeredPath, params string[] orphanPaths)
    {
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

        if (registeredPath != null)
        {
            GitFixture.Git(main, "config", "-f", ".gitmodules", $"submodule.{registeredPath}.path", registeredPath);
            GitFixture.Git(main, "config", "-f", ".gitmodules", $"submodule.{registeredPath}.url", "file:///does/not/exist.git");
            GitFixture.Git(main, "add", ".gitmodules");
            GitFixture.Git(main, "update-index", "--add", "--cacheinfo", $"160000,{libSha},{registeredPath}");
            Directory.CreateDirectory(Path.Combine(main, registeredPath));
        }

        foreach (var orphan in orphanPaths)
        {
            GitFixture.Git(main, "update-index", "--add", "--cacheinfo", $"160000,{libSha},{orphan}");
            Directory.CreateDirectory(Path.Combine(main, orphan));
        }

        GitFixture.Git(main, "commit", "-m", "init");
        return (main, new Uri(lib).AbsoluteUri, libSha);
    }

    [RequiresGitFact]
    public async Task SetUrl_fixes_a_broken_url_locally_and_Reset_restores_gitmodules()
    {
        using var fx = new GitFixture();
        var (main, libUrl, libSha) = Setup(fx, "libs/broken");
        var gitmodules = Path.Combine(main, ".gitmodules");
        var before = File.ReadAllBytes(gitmodules);

        var svc = NewService();
        var sub = await svc.GetAsync(main, "libs/broken");
        Assert.NotNull(sub);
        Assert.Equal(SubmoduleState.NotInitialized, sub!.State);
        Assert.False(sub.UrlOverridden);

        var result = await svc.SetUrlAndInitAsync(main, sub, libUrl, false, false, NoAuth);
        Assert.True(result.ExitCode == 0, result.StdErr);

        var fixedSub = await svc.GetAsync(main, "libs/broken");
        Assert.Equal(SubmoduleState.Ready, fixedSub!.State);
        Assert.Equal(libSha, fixedSub.CurrentSha);
        Assert.True(fixedSub.UrlOverridden);
        Assert.Equal(libUrl, fixedSub.EffectiveUrl);
        Assert.Equal("file:///does/not/exist.git", fixedSub.Url);
        Assert.Equal(before, File.ReadAllBytes(gitmodules));
        Assert.Empty(GitFixture.Git(main, "status", "--porcelain", "--", ".gitmodules").Trim());

        var reset = await svc.ResetUrlAsync(main, fixedSub);
        Assert.Equal(0, reset.ExitCode);

        var after = await svc.GetAsync(main, "libs/broken");
        Assert.False(after!.UrlOverridden);
        Assert.Equal(before, File.ReadAllBytes(gitmodules));

        // sync wrote the .gitmodules URL back, so the submodule stays registered.
        Assert.Equal("file:///does/not/exist.git",
            GitFixture.Git(main, "config", "--get", "submodule.libs/broken.url").Trim());
        Assert.Equal("file:///does/not/exist.git", after.EffectiveUrl);
        Assert.False(GitFixture.Git(main, "submodule", "status", "--", "libs/broken").StartsWith("-"));

        // Resetting again is harmless.
        Assert.Equal(0, (await svc.ResetUrlAsync(main, after)).ExitCode);
    }

    [RequiresGitFact]
    public async Task CloneManually_populates_a_missing_gitmodules_entry_at_the_pinned_commit()
    {
        using var fx = new GitFixture();
        var (main, libUrl, libSha) = Setup(fx, null, "libs/orphan");

        var svc = NewService();
        var sub = await svc.GetAsync(main, "libs/orphan");
        Assert.Equal(SubmoduleState.MissingFromGitmodules, sub!.State);

        var result = await svc.CloneManuallyAsync(main, sub, libUrl, NoAuth);
        Assert.True(result.ExitCode == 0, result.StdErr);

        var cloned = await svc.GetAsync(main, "libs/orphan");
        Assert.Equal(SubmoduleState.ManuallyCloned, cloned!.State);
        Assert.Equal(libSha, cloned.CurrentSha);
        Assert.Equal(cloned.PinnedSha, cloned.CurrentSha);
        Assert.Equal(libUrl, cloned.EffectiveUrl);

        Assert.DoesNotContain("libs/orphan", GitFixture.Git(main, "status", "--porcelain"));
    }

    [RequiresGitFact]
    public async Task CloneManually_with_a_wrong_url_fails_and_leaves_the_folder_empty()
    {
        using var fx = new GitFixture();
        var (main, _, _) = Setup(fx, null, "libs/orphan");
        var folder = Path.Combine(main, "libs", "orphan");

        var svc = NewService();
        var sub = (await svc.GetAsync(main, "libs/orphan"))!;

        var result = await svc.CloneManuallyAsync(main, sub, "file:///does/not/exist.git", NoAuth);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.Equal(SubmoduleState.MissingFromGitmodules, (await svc.GetAsync(main, "libs/orphan"))!.State);
    }

    [RequiresGitFact]
    public async Task CloneManually_refuses_a_non_empty_folder()
    {
        using var fx = new GitFixture();
        var (main, libUrl, _) = Setup(fx, null, "libs/orphan");
        var keep = Path.Combine(main, "libs", "orphan", "mine.txt");
        GitFixture.Write(keep, "keep me");

        var svc = NewService();
        var sub = (await svc.GetAsync(main, "libs/orphan"))!;

        var result = await svc.CloneManuallyAsync(main, sub, libUrl, NoAuth);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("keep me", File.ReadAllText(keep));
        Assert.Single(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(keep)!));
    }

    [RequiresGitFact]
    public async Task TestUrl_succeeds_for_a_repo_and_fails_for_a_missing_one()
    {
        using var fx = new GitFixture();
        var (_, libUrl, _) = Setup(fx, null);
        var svc = NewService();

        Assert.Equal(0, (await svc.TestUrlAsync(libUrl, null)).ExitCode);
        Assert.NotEqual(0, (await svc.TestUrlAsync("file:///does/not/exist.git", null)).ExitCode);
    }
}
