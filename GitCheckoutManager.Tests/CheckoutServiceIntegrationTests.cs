using GitCheckoutManager.Models;
using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

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
    [RequiresGitFact]
    public async Task Local_changes_of_every_kind_are_parsed_from_a_real_repository()
    {
        using var fx = new GitFixture();

        var subSrc = fx.Sub("subsrc");
        GitFixture.Git(subSrc, "init");
        GitFixture.Write(Path.Combine(subSrc, "s.txt"), "s");
        GitFixture.Git(subSrc, "add", ".");
        GitFixture.Git(subSrc, "commit", "-m", "sub");

        var repo = fx.Sub("repo");
        GitFixture.Git(repo, "init");
        string P(string name) => Path.Combine(repo, name.Replace('/', Path.DirectorySeparatorChar));
        foreach (var f in new[] { "staged.txt", "modified.txt", "both.txt", "deleted.txt", "staged-del.txt",
                     "old.txt", "conflict.txt", "dir/with space.txt" })
            GitFixture.Write(P(f), "base\n" + f);
        GitFixture.Git(repo, "add", ".");
        GitFixture.Git(repo, "commit", "-m", "base");

        // A merge conflict: both branches change conflict.txt.
        GitFixture.Git(repo, "checkout", "-b", "other");
        GitFixture.Write(P("conflict.txt"), "other\n");
        GitFixture.Git(repo, "commit", "-am", "other");
        GitFixture.Git(repo, "checkout", "main");
        GitFixture.Write(P("conflict.txt"), "main\n");
        GitFixture.Git(repo, "commit", "-am", "main");
        Assert.Throws<InvalidOperationException>(() => GitFixture.Git(repo, "merge", "other"));

        GitFixture.Write(P("staged.txt"), "changed\n");
        GitFixture.Git(repo, "add", "staged.txt");
        GitFixture.Write(P("modified.txt"), "changed\n");
        GitFixture.Write(P("both.txt"), "one\n");
        GitFixture.Git(repo, "add", "both.txt");
        GitFixture.Write(P("both.txt"), "two\n");
        File.Delete(P("deleted.txt"));
        GitFixture.Git(repo, "rm", "staged-del.txt");
        GitFixture.Git(repo, "mv", "old.txt", "renamed.txt");
        GitFixture.Write(P("added.txt"), "new\n");
        GitFixture.Git(repo, "add", "added.txt");
        GitFixture.Write(P("untracked dir/ünï 100%.txt"), "u");
        GitFixture.Git(repo, "submodule", "add", new Uri(subSrc).AbsoluteUri, "libs/sub");
        GitFixture.Write(P(".gitignore"), "ignored.log\n");
        GitFixture.Write(P("ignored.log"), "x");

        var service = new CheckoutService(new GitService());
        var set = await service.GetLocalChangesAsync(repo);
        var byPath = set.Changes.ToDictionary(c => c.Path);

        void Expect(string path, LocalChangeGroup group, string label)
        {
            Assert.True(byPath.TryGetValue(path, out var c), $"missing {path}");
            Assert.Equal(group, c!.Group);
            Assert.Equal(label, c.Label);
        }

        Expect("conflict.txt", LocalChangeGroup.Conflicted, "Conflict: both modified");
        Expect("staged.txt", LocalChangeGroup.Staged, "Staged (modified)");
        Expect("added.txt", LocalChangeGroup.Staged, "Added");
        Expect("both.txt", LocalChangeGroup.Staged, "Staged + modified");
        Expect("modified.txt", LocalChangeGroup.Modified, "Modified");
        Expect("deleted.txt", LocalChangeGroup.Deleted, "Deleted");
        Expect("staged-del.txt", LocalChangeGroup.Deleted, "Deleted (staged)");
        Expect("renamed.txt", LocalChangeGroup.Renamed, "Renamed");
        Expect("untracked dir/ünï 100%.txt", LocalChangeGroup.Untracked, "Untracked");
        Expect(".gitignore", LocalChangeGroup.Untracked, "Untracked");

        Assert.Equal("old.txt", byPath["renamed.txt"].OriginalPath);
        Assert.False(byPath.ContainsKey("old.txt"));
        Assert.False(byPath.ContainsKey("ignored.log"));

        var sub = byPath["libs/sub"];
        Assert.True(sub.IsSubmodule);
        Assert.Equal("Submodule", sub.Label);
        Assert.Contains("Submodules window", sub.Hint);
        Assert.Equal(1, set.Changes.Count(c => c.Path == "both.txt")); // staged + modified is one row

        // OpenAsync uses the same status run, so the summary count matches the window's list.
        var info = await service.OpenAsync(repo);
        Assert.Equal(set.Count, info.ChangedFileCount);
        Assert.Equal(set.Changes.Select(c => c.Path), info.LocalChanges.Changes.Select(c => c.Path));
    }

    [RequiresGitFact]
    public async Task Local_changes_are_empty_for_a_clean_checkout_and_fail_outside_a_repository()
    {
        using var fx = new GitFixture();
        var src = MakeSource(fx);
        var service = new CheckoutService(new GitService());

        Assert.Equal(0, (await service.GetLocalChangesAsync(src)).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLocalChangesAsync(fx.Root));
    }
}
