using GitCheckoutManager.Models;
using GitCheckoutManager.Services;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Tests;

/// <summary>Branch switching inside submodules, against real git and file:// repos.</summary>
public class SubmoduleBranchSwitchTests
{
    static SubmoduleBranchSwitchTests()
    {
        // GitService children inherit this: newer git blocks the file transport for submodule clones.
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "protocol.file.allow");
        Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "always");
    }

    private static readonly Func<string, GitAuth?> NoAuth = _ => null;

    private static SubmoduleService NewService() => new(new GitService());

    /// <summary>A repo with one commit on main and, on <paramref name="extraBranch"/>, one more commit.</summary>
    private static string MakeOrigin(GitFixture fx, string name, string extraBranch)
    {
        var repo = fx.Sub(name);
        GitFixture.Git(repo, "init");
        GitFixture.Write(Path.Combine(repo, "a.txt"), "a");
        GitFixture.Git(repo, "add", ".");
        GitFixture.Git(repo, "commit", "-m", "first");
        GitFixture.Git(repo, "checkout", "-b", extraBranch);
        GitFixture.Write(Path.Combine(repo, "b.txt"), "b");
        GitFixture.Git(repo, "add", ".");
        GitFixture.Git(repo, "commit", "-m", "on " + extraBranch);
        GitFixture.Git(repo, "checkout", "main");
        return repo;
    }

    private static string AddSubmodule(GitFixture fx, string origin, string path)
    {
        var main = fx.Sub("main");
        GitFixture.Git(main, "init");
        GitFixture.Write(Path.Combine(main, "m.txt"), "m");
        GitFixture.Git(main, "add", ".");
        GitFixture.Git(main, "commit", "-m", "m");
        GitFixture.Git(main, "submodule", "add", new Uri(origin).AbsoluteUri, path);
        GitFixture.Git(main, "commit", "-m", "add submodule");
        return main;
    }

    private static async Task<SubmoduleInfo> Row(SubmoduleService svc, string main, string path)
    {
        var sub = await svc.GetAsync(main, path);
        Assert.NotNull(sub);
        return sub!;
    }

    [RequiresGitFact]
    public async Task Switch_to_a_branch_that_exists_only_on_origin_creates_a_tracking_branch()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");
        var svc = NewService();

        var sub = await Row(svc, main, "external/lib");
        Assert.Equal("main", sub.CurrentBranch);

        var branches = await svc.ListRemoteBranchesAsync(sub, NoAuth);
        Assert.Equal(new[] { "feature", "main" }, branches);

        var result = await svc.SwitchBranchAsync(sub, "feature", NoAuth);
        Assert.True(result.ExitCode == 0, result.StdErr);

        var after = await Row(svc, main, "external/lib");
        Assert.Equal("feature", after.CurrentBranch);
        Assert.Equal(SubmoduleState.DifferentCommit, after.State);

        var folder = Path.Combine(main, "external", "lib");
        Assert.Equal("origin/feature", GitFixture.Git(folder, "rev-parse", "--abbrev-ref", "feature@{upstream}").Trim());
    }

    [RequiresGitFact]
    public async Task Switch_to_a_local_branch_with_extra_commits_keeps_them_and_says_so()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");
        var svc = NewService();
        var folder = Path.Combine(main, "external", "lib");

        var sub = await Row(svc, main, "external/lib");
        Assert.Equal(0, (await svc.SwitchBranchAsync(sub, "feature", NoAuth)).ExitCode);

        // A local-only commit on feature, and a different new commit on origin's feature: they diverge.
        GitFixture.Write(Path.Combine(folder, "local.txt"), "mine");
        GitFixture.Git(folder, "add", ".");
        GitFixture.Git(folder, "commit", "-m", "local only");
        var localSha = GitFixture.Git(folder, "rev-parse", "HEAD").Trim();

        GitFixture.Git(origin, "checkout", "feature");
        GitFixture.Write(Path.Combine(origin, "remote.txt"), "theirs");
        GitFixture.Git(origin, "add", ".");
        GitFixture.Git(origin, "commit", "-m", "remote only");
        GitFixture.Git(origin, "checkout", "main");

        sub = await Row(svc, main, "external/lib");
        Assert.Equal(0, (await svc.SwitchBranchAsync(sub, "main", NoAuth)).ExitCode);

        sub = await Row(svc, main, "external/lib");
        var result = await svc.SwitchBranchAsync(sub, "feature", NoAuth);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Local branch feature has commits that aren't on origin — left as is.", result.StdOut);

        var after = await Row(svc, main, "external/lib");
        Assert.Equal("feature", after.CurrentBranch);
        Assert.Equal(localSha, after.CurrentSha);
        Assert.True(File.Exists(Path.Combine(folder, "local.txt")));
    }

    [RequiresGitFact]
    public async Task Switch_fast_forwards_a_local_branch_that_is_behind_origin()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");
        var svc = NewService();
        var folder = Path.Combine(main, "external", "lib");

        var sub = await Row(svc, main, "external/lib");
        Assert.Equal(0, (await svc.SwitchBranchAsync(sub, "feature", NoAuth)).ExitCode);
        Assert.Equal(0, (await svc.SwitchBranchAsync(await Row(svc, main, "external/lib"), "main", NoAuth)).ExitCode);

        GitFixture.Git(origin, "checkout", "feature");
        GitFixture.Write(Path.Combine(origin, "c.txt"), "c");
        GitFixture.Git(origin, "add", ".");
        GitFixture.Git(origin, "commit", "-m", "more");
        var originSha = GitFixture.Git(origin, "rev-parse", "HEAD").Trim();
        GitFixture.Git(origin, "checkout", "main");

        var result = await svc.SwitchBranchAsync(await Row(svc, main, "external/lib"), "feature", NoAuth);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StdOut);
        Assert.Equal(originSha, GitFixture.Git(folder, "rev-parse", "HEAD").Trim());
    }

    [RequiresGitFact]
    public async Task CheckSwitchSafety_reports_dirty_files_and_unreferenced_commits()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");
        var svc = NewService();
        var folder = Path.Combine(main, "external", "lib");

        var clean = await svc.CheckSwitchSafetyAsync(await Row(svc, main, "external/lib"));
        Assert.False(clean.IsDirty);
        Assert.False(clean.UnreferencedCommits);

        GitFixture.Write(Path.Combine(folder, "a.txt"), "changed");
        GitFixture.Write(Path.Combine(folder, "new.txt"), "new");
        var dirty = await svc.CheckSwitchSafetyAsync(await Row(svc, main, "external/lib"));
        Assert.Equal(2, dirty.DirtyFiles.Count);
        Assert.Contains(dirty.DirtyFiles, l => l.EndsWith("a.txt"));
        Assert.Contains(dirty.DirtyFiles, l => l.EndsWith("new.txt"));

        // A commit made on a detached HEAD belongs to no branch.
        GitFixture.Git(folder, "checkout", "--", "a.txt");
        File.Delete(Path.Combine(folder, "new.txt"));
        GitFixture.Git(folder, "checkout", "--detach");
        GitFixture.Write(Path.Combine(folder, "d.txt"), "d");
        GitFixture.Git(folder, "add", ".");
        GitFixture.Git(folder, "commit", "-m", "orphan");

        var orphan = await svc.CheckSwitchSafetyAsync(await Row(svc, main, "external/lib"));
        Assert.False(orphan.IsDirty);
        Assert.True(orphan.UnreferencedCommits);
    }

    [RequiresGitFact]
    public async Task ResetToRecorded_detaches_at_the_pinned_commit_and_refuses_a_dirty_folder()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");
        var svc = NewService();
        var folder = Path.Combine(main, "external", "lib");

        var sub = await Row(svc, main, "external/lib");
        Assert.Equal(0, (await svc.SwitchBranchAsync(sub, "feature", NoAuth)).ExitCode);

        sub = await Row(svc, main, "external/lib");
        Assert.NotEqual(sub.PinnedSha, sub.CurrentSha);

        GitFixture.Write(Path.Combine(folder, "a.txt"), "changed");
        var refused = await svc.ResetToRecordedAsync(sub);
        Assert.NotEqual(0, refused.ExitCode);
        Assert.Equal("feature", (await Row(svc, main, "external/lib")).CurrentBranch);

        GitFixture.Git(folder, "checkout", "--", "a.txt");
        var result = await svc.ResetToRecordedAsync(sub);
        Assert.True(result.ExitCode == 0, result.StdErr);

        var after = await Row(svc, main, "external/lib");
        Assert.Equal(after.PinnedSha, after.CurrentSha);
        Assert.Null(after.CurrentBranch);
        Assert.Equal(SubmoduleState.Ready, after.State);

        // The feature branch is still there.
        Assert.Contains("feature", GitFixture.Git(folder, "branch", "--list", "feature"));
    }

    [RequiresGitFact]
    public async Task Switch_works_on_a_nested_submodule_against_its_own_folder()
    {
        using var fx = new GitFixture();
        var inner = MakeOrigin(fx, "inner", "dev");

        var lib = fx.Sub("lib");
        GitFixture.Git(lib, "init");
        GitFixture.Write(Path.Combine(lib, "l.txt"), "l");
        GitFixture.Git(lib, "add", ".");
        GitFixture.Git(lib, "commit", "-m", "l");
        GitFixture.Git(lib, "submodule", "add", new Uri(inner).AbsoluteUri, "vendor/x");
        GitFixture.Git(lib, "commit", "-m", "add inner");

        var main = AddSubmodule(fx, lib, "external/lib");
        var svc = NewService();

        var nested = (await svc.ListAsync(main))[1];
        Assert.Equal(0, (await svc.InitAndUpdateAsync(nested.RepoRoot, nested, false, false, NoAuth)).ExitCode);
        nested = (await svc.ListAsync(main))[1];
        Assert.Equal("external/lib/vendor/x", nested.DisplayPath);

        var result = await svc.SwitchBranchAsync(nested, "dev", NoAuth);
        Assert.True(result.ExitCode == 0, result.StdErr);

        var list = await svc.ListAsync(main);
        Assert.Equal("dev", list[1].CurrentBranch);
        Assert.Equal(SubmoduleState.DifferentCommit, list[1].State);
        // The parent submodule is untouched.
        Assert.Equal(SubmoduleState.Ready, list[0].State);
    }

    // ── View model ────────────────────────────────────────────────────────

    /// <summary>Real service underneath; counts the calls that would change a submodule.</summary>
    private sealed class SpyService : ISubmoduleService
    {
        private readonly SubmoduleService _inner = NewService();
        public int SwitchCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public List<string> InitPaths { get; } = new();

        public Task<List<SubmoduleInfo>> ListAsync(string root, CancellationToken ct = default) => _inner.ListAsync(root, ct);
        public Task<SubmoduleInfo?> GetAsync(string root, string path, CancellationToken ct = default, string displayPrefix = "", int depth = 0) =>
            _inner.GetAsync(root, path, ct, displayPrefix, depth);
        public Task<GitResult> InitAndUpdateAsync(string root, SubmoduleInfo sub, bool latestFromBranch, bool includeNested,
            Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
        {
            InitPaths.Add(sub.DisplayPath);
            return _inner.InitAndUpdateAsync(root, sub, latestFromBranch, includeNested, resolveAuth, ct);
        }
        public Task<GitResult> TestUrlAsync(string url, GitAuth? auth, CancellationToken ct = default) => _inner.TestUrlAsync(url, auth, ct);
        public Task<GitResult> SetUrlAndInitAsync(string root, SubmoduleInfo sub, string newUrl, bool latestFromBranch,
            bool includeNested, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default) =>
            _inner.SetUrlAndInitAsync(root, sub, newUrl, latestFromBranch, includeNested, resolveAuth, ct);
        public Task<GitResult> ResetUrlAsync(string root, SubmoduleInfo sub, CancellationToken ct = default) => _inner.ResetUrlAsync(root, sub, ct);
        public Task<GitResult> CloneManuallyAsync(string root, SubmoduleInfo sub, string url,
            Func<string, GitAuth?> resolveAuth, CancellationToken ct = default) =>
            _inner.CloneManuallyAsync(root, sub, url, resolveAuth, ct);
        public Task<List<string>> ListRemoteBranchesAsync(SubmoduleInfo sub, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default) =>
            _inner.ListRemoteBranchesAsync(sub, resolveAuth, ct);
        public Task<SwitchCheck> CheckSwitchSafetyAsync(SubmoduleInfo sub, CancellationToken ct = default) => _inner.CheckSwitchSafetyAsync(sub, ct);
        public Task<GitResult> SwitchBranchAsync(SubmoduleInfo sub, string branch, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
        {
            SwitchCalls++;
            return _inner.SwitchBranchAsync(sub, branch, resolveAuth, ct);
        }
        public Task<GitResult> ResetToRecordedAsync(SubmoduleInfo sub, CancellationToken ct = default)
        {
            ResetCalls++;
            return _inner.ResetToRecordedAsync(sub, ct);
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? BranchToPick { get; set; }
        public bool Confirm { get; set; } = true;
        public List<string> Messages { get; } = new();
        public List<string> Confirmations { get; } = new();
        public List<bool> DestructiveFlags { get; } = new();
        public int PickerCalls { get; private set; }

        public string? ShowSubmoduleBranch(SubmoduleBranchModel model) { PickerCalls++; return BranchToPick; }
        public List<string?> MessageDetails { get; } = new();
        public void ShowMessage(string title, string message, string? details = null)
        {
            Messages.Add(message);
            MessageDetails.Add(details);
        }
        public bool ShowConfirmation(string title, string message, string? details = null, bool destructive = false) { Confirmations.Add(message); DestructiveFlags.Add(destructive); return Confirm; }

        public string? ShowSaveFileDialog(string filter, string defaultExtension, string defaultFileName) => throw new NotSupportedException();
        public string? ShowOpenFileDialog(string filter, string title = "Open File") => throw new NotSupportedException();
        public string? ShowOpenFolderDialog(string title = "Select Folder", string? initialPath = null) => throw new NotSupportedException();
        public string? ShowInputDialog(string title, string prompt, string defaultValue = "") => throw new NotSupportedException();
        public RemovalReviewChoices? ShowRemovalReview(RemovalReviewModel model) => throw new NotSupportedException();
        public void ShowSettings(SettingsViewModel viewModel) => throw new NotSupportedException();
        public void ShowSubmodules(SubmodulesViewModel vm) => throw new NotSupportedException();
        public string? ShowSubmoduleUrl(SubmoduleUrlModel model) => throw new NotSupportedException();
    }

    private sealed class NullSettings : ISettingsService
    {
        public AppSettings LoadSettings() => new();
        public void SaveSettings(AppSettings settings) { }
        public string? GetDecryptedToken(AppSettings settings) => null;
        public void SetToken(AppSettings settings, string token) { }
        public string? GetDecryptedToken(AppSettings settings, GitHostType hostType) => null;
        public void SetToken(AppSettings settings, GitHostType hostType, string token) { }
    }

    private static async Task<(SubmodulesViewModel Vm, SpyService Spy, FakeDialogs Dialogs)> OpenWindowModel(string main)
    {
        var spy = new SpyService();
        var dialogs = new FakeDialogs();
        var vm = new SubmodulesViewModel(spy, main, NoAuth, dialogs, new AppSettings(), new NullSettings());
        await vm.RefreshAsync();
        return (vm, spy, dialogs);
    }

    [RequiresGitFact]
    public async Task ViewModel_refuses_a_dirty_submodule_and_never_calls_switch()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");
        GitFixture.Write(Path.Combine(main, "external", "lib", "a.txt"), "changed");

        var (vm, spy, dialogs) = await OpenWindowModel(main);
        dialogs.BranchToPick = "feature";

        await vm.SwitchBranchCommand.ExecuteAsync(vm.Rows.Single());

        Assert.Equal(0, spy.SwitchCalls);
        Assert.Equal(0, dialogs.PickerCalls);
        var message = Assert.Single(dialogs.Messages);
        Assert.Contains("Commit or discard these changes in external/lib first.", message);
        Assert.DoesNotContain("a.txt", message);
        Assert.Contains("a.txt", Assert.Single(dialogs.MessageDetails));
    }

    [RequiresGitFact]
    public async Task ViewModel_switches_and_marks_the_row_as_moved()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");

        var (vm, spy, dialogs) = await OpenWindowModel(main);
        Assert.Equal("on main", vm.Rows.Single().BranchLineText);
        Assert.False(vm.Rows.Single().CanResetToRecorded);
        dialogs.BranchToPick = "feature";

        await vm.SwitchBranchCommand.ExecuteAsync(vm.Rows.Single());

        Assert.Equal(1, spy.SwitchCalls);
        var row = vm.Rows.Single();
        Assert.Equal("on feature", row.BranchLineText);
        Assert.True(row.ShowMovedNote);
        Assert.True(row.CanResetToRecorded);
        Assert.False(row.HasError);

        // Reset asks first, then goes back to the recorded commit.
        await vm.ResetToRecordedCommand.ExecuteAsync(row);
        Assert.Equal(1, spy.ResetCalls);
        Assert.Contains(dialogs.Confirmations, c => c.StartsWith("Move external/lib back to "));
        row = vm.Rows.Single();
        Assert.Equal("detached", row.BranchLineText);
        Assert.False(row.ShowMovedNote);
    }

    [RequiresGitFact]
    public async Task ViewModel_asks_before_leaving_a_commit_that_is_on_no_branch()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");
        var folder = Path.Combine(main, "external", "lib");
        GitFixture.Git(folder, "checkout", "--detach");
        GitFixture.Write(Path.Combine(folder, "d.txt"), "d");
        GitFixture.Git(folder, "add", ".");
        GitFixture.Git(folder, "commit", "-m", "orphan");

        var (vm, spy, dialogs) = await OpenWindowModel(main);
        dialogs.BranchToPick = "feature";
        dialogs.Confirm = false;

        await vm.SwitchBranchCommand.ExecuteAsync(vm.Rows.Single());

        Assert.Equal(0, spy.SwitchCalls);
        Assert.Contains(dialogs.Confirmations, c => c.Contains("isn't on any branch"));
    }

    // ── Hardening: more switch cases ──────────────────────────────────────

    [RequiresGitFact]
    public async Task Switch_to_a_branch_with_a_slash_tracks_origin_with_the_same_name()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature/x");
        var main = AddSubmodule(fx, origin, "external/lib");
        var svc = NewService();

        var sub = await Row(svc, main, "external/lib");
        Assert.Contains("feature/x", await svc.ListRemoteBranchesAsync(sub, NoAuth));

        var result = await svc.SwitchBranchAsync(sub, "feature/x", NoAuth);
        Assert.True(result.ExitCode == 0, result.StdErr);

        var after = await Row(svc, main, "external/lib");
        Assert.Equal("feature/x", after.CurrentBranch);
        var folder = Path.Combine(main, "external", "lib");
        Assert.Equal("origin/feature/x", GitFixture.Git(folder, "rev-parse", "--abbrev-ref", "feature/x@{upstream}").Trim());
    }

    [RequiresGitFact]
    public async Task Switch_to_a_branch_deleted_on_origin_after_listing_returns_the_fetch_error_and_changes_nothing()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");
        var svc = NewService();
        var folder = Path.Combine(main, "external", "lib");

        var sub = await Row(svc, main, "external/lib");
        Assert.Contains("feature", await svc.ListRemoteBranchesAsync(sub, NoAuth));
        var headBefore = GitFixture.Git(folder, "rev-parse", "HEAD").Trim();

        GitFixture.Git(origin, "branch", "-D", "feature");

        var result = await svc.SwitchBranchAsync(sub, "feature", NoAuth);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("feature", result.StdErr);
        Assert.Equal(headBefore, GitFixture.Git(folder, "rev-parse", "HEAD").Trim());
        var after = await Row(svc, main, "external/lib");
        Assert.Equal("main", after.CurrentBranch);
        Assert.Equal(SubmoduleState.Ready, after.State);
        Assert.Equal(string.Empty, GitFixture.Git(folder, "branch", "--list", "feature").Trim());
    }

    [RequiresGitFact]
    public async Task CheckSwitchSafety_counts_an_untracked_file_as_dirty()
    {
        // Documents current behaviour: git status --porcelain lists untracked files, so they block a switch too.
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "lib", "feature");
        var main = AddSubmodule(fx, origin, "external/lib");
        var svc = NewService();
        GitFixture.Write(Path.Combine(main, "external", "lib", "notes.txt"), "mine");

        var check = await svc.CheckSwitchSafetyAsync(await Row(svc, main, "external/lib"));

        Assert.True(check.IsDirty);
        var line = Assert.Single(check.DirtyFiles);
        Assert.StartsWith("??", line);
        Assert.EndsWith("notes.txt", line);
    }

    [RequiresGitFact]
    public async Task CheckSwitchSafety_reports_a_parent_submodule_dirty_when_its_nested_submodule_moved()
    {
        // Documents current behaviour: the moved nested gitlink shows up as a modified path in the parent.
        using var fx = new GitFixture();
        var inner = MakeOrigin(fx, "inner", "dev");

        var lib = fx.Sub("lib");
        GitFixture.Git(lib, "init");
        GitFixture.Write(Path.Combine(lib, "l.txt"), "l");
        GitFixture.Git(lib, "add", ".");
        GitFixture.Git(lib, "commit", "-m", "l");
        GitFixture.Git(lib, "submodule", "add", new Uri(inner).AbsoluteUri, "vendor/x");
        GitFixture.Git(lib, "commit", "-m", "add inner");

        var main = AddSubmodule(fx, lib, "external/lib");
        var svc = NewService();

        var nested = (await svc.ListAsync(main))[1];
        Assert.Equal(0, (await svc.InitAndUpdateAsync(nested.RepoRoot, nested, false, false, NoAuth)).ExitCode);

        var list = await svc.ListAsync(main);
        Assert.False((await svc.CheckSwitchSafetyAsync(list[0])).IsDirty);

        Assert.Equal(0, (await svc.SwitchBranchAsync(list[1], "dev", NoAuth)).ExitCode);

        var parent = (await svc.ListAsync(main))[0];
        var check = await svc.CheckSwitchSafetyAsync(parent);
        Assert.True(check.IsDirty);
        Assert.Contains(check.DirtyFiles, l => l.EndsWith("vendor/x"));
    }

    private sealed class ThrowingGit : IGitService
    {
        public Task<List<string>?> GetSparseCheckoutPathsAsync(string localRepoPath) => throw new InvalidOperationException("git was run");
        public Task<GitResult> RunAsync(IEnumerable<string> args, string? workingDirectory = null, GitAuth? auth = null,
            CancellationToken ct = default, bool allowInteractiveAuth = false) => throw new InvalidOperationException("git was run");
        public Task<List<Branch>> ListRemoteBranchesAsync(string repoUrl, GitAuth? auth, CancellationToken ct = default) =>
            throw new InvalidOperationException("git was run");
    }

    [Theory]
    [InlineData("-x")]
    [InlineData("--upload-pack=evil")]
    [InlineData("")]
    public async Task Switch_rejects_an_option_like_or_empty_branch_name_without_running_git(string branch)
    {
        var svc = new SubmoduleService(new ThrowingGit());
        var sub = new SubmoduleInfo("external/lib", "external/lib", null, null, "abc", "abc", SubmoduleState.Ready,
            RepoRoot: "C:\\nowhere", DisplayPath: "external/lib");

        var result = await svc.SwitchBranchAsync(sub, branch, NoAuth);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not a valid branch name", result.StdErr);
    }

    // ── Hardening: Initialize selected respects the switch safety rules ───

    /// <summary>One main repo with several submodules, each cloned from its own origin.</summary>
    private static string AddSubmodules(GitFixture fx, params (string Origin, string Path)[] subs)
    {
        var main = fx.Sub("main");
        GitFixture.Git(main, "init");
        GitFixture.Write(Path.Combine(main, "m.txt"), "m");
        GitFixture.Git(main, "add", ".");
        GitFixture.Git(main, "commit", "-m", "m");
        foreach (var (origin, path) in subs)
            GitFixture.Git(main, "submodule", "add", new Uri(origin).AbsoluteUri, path);
        GitFixture.Git(main, "commit", "-m", "add submodules");
        return main;
    }

    [RequiresGitFact]
    public async Task InitializeSelected_skips_a_dirty_row_marks_it_skipped_and_still_processes_the_others()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx,
            (MakeOrigin(fx, "liba", "feature"), "external/a"),
            (MakeOrigin(fx, "libb", "feature"), "external/b"));
        var svc = NewService();

        // Move both off their recorded commit, then leave a tracked change in a.
        foreach (var path in new[] { "external/a", "external/b" })
            Assert.Equal(0, (await svc.SwitchBranchAsync(await Row(svc, main, path), "feature", NoAuth)).ExitCode);
        var folderA = Path.Combine(main, "external", "a");
        GitFixture.Write(Path.Combine(folderA, "a.txt"), "changed");
        var headA = GitFixture.Git(folderA, "rev-parse", "HEAD").Trim();

        var (vm, spy, dialogs) = await OpenWindowModel(main);
        Assert.All(vm.Rows, r => Assert.Equal(SubmoduleState.DifferentCommit, r.State));
        foreach (var r in vm.Rows) r.IsSelected = true;

        await vm.InitializeSelectedCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "external/b" }, spy.InitPaths);
        Assert.Empty(dialogs.Confirmations);

        var rowA = vm.Rows.Single(r => r.DisplayPath == "external/a");
        Assert.True(rowA.HasError);
        Assert.Equal("Skipped", rowA.DisplayStateText);
        Assert.Equal("MutedTextBrush", rowA.DisplayBrushKey);
        Assert.Equal(SubmoduleSkipReason.UncommittedChanges, rowA.Skip);
        Assert.Contains("1 skipped: external/a", vm.ResultText);
        var lines = rowA.Error.Split(Environment.NewLine);
        Assert.Equal("Uncommitted changes in external/a — commit or discard them first.", lines[0]);
        Assert.Contains(lines.Skip(1), l => l.EndsWith("a.txt"));

        // Not touched: same commit, same branch, the edit is still there.
        Assert.Equal(headA, GitFixture.Git(folderA, "rev-parse", "HEAD").Trim());
        Assert.Equal("changed", File.ReadAllText(Path.Combine(folderA, "a.txt")));
        Assert.Equal(SubmoduleState.DifferentCommit, rowA.State);

        var rowB = vm.Rows.Single(r => r.DisplayPath == "external/b");
        Assert.False(rowB.HasError);
        Assert.Equal(SubmoduleState.Ready, rowB.State);
        Assert.Contains("1 skipped: external/a", vm.ResultText);
    }

    [RequiresGitFact]
    public async Task InitializeSelected_lists_at_most_50_files_of_a_dirty_row()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx, (MakeOrigin(fx, "liba", "feature"), "external/a"));
        var svc = NewService();
        Assert.Equal(0, (await svc.SwitchBranchAsync(await Row(svc, main, "external/a"), "feature", NoAuth)).ExitCode);
        var folder = Path.Combine(main, "external", "a");
        for (var i = 0; i < 60; i++) GitFixture.Write(Path.Combine(folder, $"junk{i:D2}.txt"), "x");

        var (vm, spy, _) = await OpenWindowModel(main);
        vm.Rows.Single().IsSelected = true;
        await vm.InitializeSelectedCommand.ExecuteAsync(null);

        Assert.Empty(spy.InitPaths);
        Assert.Equal(51, vm.Rows.Single().Error.Split(Environment.NewLine).Length);
    }

    [RequiresGitFact]
    public async Task InitializeSelected_asks_before_leaving_a_commit_on_no_branch_and_stops_on_No()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx, (MakeOrigin(fx, "liba", "feature"), "external/a"));
        var folder = Path.Combine(main, "external", "a");
        GitFixture.Git(folder, "checkout", "--detach");
        GitFixture.Write(Path.Combine(folder, "d.txt"), "d");
        GitFixture.Git(folder, "add", ".");
        GitFixture.Git(folder, "commit", "-m", "orphan");
        var orphan = GitFixture.Git(folder, "rev-parse", "HEAD").Trim();

        var (vm, spy, dialogs) = await OpenWindowModel(main);
        dialogs.Confirm = false;
        vm.Rows.Single().IsSelected = true;

        await vm.InitializeSelectedCommand.ExecuteAsync(null);

        var message = Assert.Single(dialogs.Confirmations);
        Assert.Equal("The current commit in external/a isn't on any branch. Updating will leave it behind. Continue?", message);
        Assert.True(Assert.Single(dialogs.DestructiveFlags));

        Assert.Empty(spy.InitPaths);
        Assert.Equal(orphan, GitFixture.Git(folder, "rev-parse", "HEAD").Trim());
        Assert.Equal(orphan, vm.Rows.Single().Info.CurrentSha);
        // The reason stays in Error details, but the row is "Skipped", not "Failed".
        var row = vm.Rows.Single();
        Assert.True(row.HasError);
        Assert.Equal("Skipped", row.DisplayStateText);
        Assert.Equal("MutedTextBrush", row.DisplayBrushKey);
        Assert.Equal(SubmoduleSkipReason.Declined, row.Skip);
        Assert.Contains("1 skipped: external/a", vm.ResultText);
        Assert.True(File.Exists(Path.Combine(folder, "d.txt")));
    }

    [RequiresGitFact]
    public async Task InitializeSelected_moves_the_unreferenced_row_when_the_user_says_Yes()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx, (MakeOrigin(fx, "liba", "feature"), "external/a"));
        var folder = Path.Combine(main, "external", "a");
        GitFixture.Git(folder, "checkout", "--detach");
        GitFixture.Write(Path.Combine(folder, "d.txt"), "d");
        GitFixture.Git(folder, "add", ".");
        GitFixture.Git(folder, "commit", "-m", "orphan");

        var (vm, spy, dialogs) = await OpenWindowModel(main);
        dialogs.Confirm = true;
        vm.Rows.Single().IsSelected = true;

        await vm.InitializeSelectedCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Confirmations);
        Assert.Equal(new[] { "external/a" }, spy.InitPaths);
        Assert.Equal(SubmoduleState.Ready, vm.Rows.Single().State);
    }

    [RequiresGitFact]
    public async Task InitializeSelected_does_not_check_a_submodule_that_is_not_initialized()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "liba", "feature");
        var main = AddSubmodules(fx, (origin, "external/a"));

        // A fresh clone of main has the submodule registered but empty.
        var clone = Path.Combine(fx.Root, "clone");
        GitFixture.Git(fx.Root, "clone", main, clone);

        var (vm, spy, dialogs) = await OpenWindowModel(clone);
        Assert.Equal(SubmoduleState.NotInitialized, vm.Rows.Single().State);
        vm.Rows.Single().IsSelected = true;

        await vm.InitializeSelectedCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "external/a" }, spy.InitPaths);
        Assert.Empty(dialogs.Confirmations);
        Assert.Equal(SubmoduleState.Ready, vm.Rows.Single().State);
    }

    // ── Target radio: one property, survives close/reopen ────────────────

    private static SubmodulesViewModel Reopen(SpyService spy, string main, FakeDialogs dialogs, AppSettings settings) =>
        new(spy, main, NoAuth, dialogs, settings, new NullSettings());

    [RequiresGitFact]
    public async Task Target_is_pinned_after_initializing_closing_and_reopening()
    {
        using var fx = new GitFixture();
        var main = AddSubmodule(fx, MakeOrigin(fx, "lib", "feature"), "external/lib");
        var settings = new AppSettings();
        var spy = new SpyService();
        var dialogs = new FakeDialogs();

        var first = Reopen(spy, main, dialogs, settings);
        await first.RefreshAsync();
        Assert.Equal(SubmoduleTarget.Pinned, first.Target);
        foreach (var r in first.Rows) r.IsSelected = true;
        await first.InitializeSelectedCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "external/lib" }, spy.InitPaths);

        var second = Reopen(spy, main, dialogs, settings);
        await second.RefreshAsync();
        Assert.Equal(SubmoduleTarget.Pinned, second.Target);
    }

    [RequiresGitFact]
    public async Task Target_latest_is_remembered_after_reopening()
    {
        using var fx = new GitFixture();
        var main = AddSubmodule(fx, MakeOrigin(fx, "lib", "feature"), "external/lib");
        var settings = new AppSettings();
        var spy = new SpyService();
        var dialogs = new FakeDialogs();

        var first = Reopen(spy, main, dialogs, settings);
        first.Target = SubmoduleTarget.LatestFromBranch;

        var second = Reopen(spy, main, dialogs, settings);
        Assert.Equal(SubmoduleTarget.LatestFromBranch, second.Target);
        Assert.True(settings.SubmoduleLatestFromBranch);

        second.Target = SubmoduleTarget.Pinned;
        Assert.Equal(SubmoduleTarget.Pinned, Reopen(spy, main, dialogs, settings).Target);
        Assert.False(settings.SubmoduleLatestFromBranch);
    }

    [Fact]
    public void Target_falls_back_to_the_legacy_bool_in_old_settings()
    {
        var spy = new SpyService();
        var dialogs = new FakeDialogs();
        var legacy = new AppSettings { SubmoduleLatestFromBranch = true };
        Assert.Equal(SubmoduleTarget.LatestFromBranch, Reopen(spy, "unused", dialogs, legacy).Target);

        // The new value wins once present.
        var current = new AppSettings { SubmoduleTarget = SubmoduleTarget.Pinned, SubmoduleLatestFromBranch = true };
        Assert.Equal(SubmoduleTarget.Pinned, Reopen(spy, "unused", dialogs, current).Target);
    }

    [RequiresGitFact]
    public async Task Declining_the_latest_confirmation_leaves_the_selection_unchanged()
    {
        using var fx = new GitFixture();
        var main = AddSubmodule(fx, MakeOrigin(fx, "lib", "feature"), "external/lib");
        var spy = new SpyService();
        var dialogs = new FakeDialogs { Confirm = false };
        var vm = Reopen(spy, main, dialogs, new AppSettings());
        await vm.RefreshAsync();
        vm.Target = SubmoduleTarget.LatestFromBranch;
        foreach (var r in vm.Rows) r.IsSelected = true;

        await vm.InitializeSelectedCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Confirmations);
        Assert.Empty(spy.InitPaths);
        Assert.Equal(SubmoduleTarget.LatestFromBranch, vm.Target);
    }
}
