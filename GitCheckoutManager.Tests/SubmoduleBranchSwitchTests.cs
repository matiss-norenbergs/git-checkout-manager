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
        Assert.Equal(SwitchOutcome.LeftAsIs, result.Outcome);

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
        public int ListCalls { get; private set; }
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
        public Task<List<string>> ListRemoteBranchesAsync(SubmoduleInfo sub, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
        {
            ListCalls++;
            return _inner.ListRemoteBranchesAsync(sub, resolveAuth, ct);
        }
        public Task<SwitchCheck> CheckSwitchSafetyAsync(SubmoduleInfo sub, CancellationToken ct = default) => _inner.CheckSwitchSafetyAsync(sub, ct);
        public Task<SwitchResult> SwitchBranchAsync(SubmoduleInfo sub, string branch, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
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

        public SubmoduleBranchModel? LastBranchModel { get; private set; }
        public List<BranchOption>? LastOptions { get; private set; }

        public string? ShowSubmoduleBranch(SubmoduleBranchModel model)
        {
            PickerCalls++;
            LastBranchModel = model;
            // Like the window: the loader runs while the picker is open.
            if (model.IsMulti && model.LoadBranchOptionsAsync != null)
                LastOptions = model.LoadBranchOptionsAsync(default).GetAwaiter().GetResult();
            else
                model.LoadBranchesAsync(default).GetAwaiter().GetResult();
            return BranchToPick;
        }
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

    /// <summary>A fresh clone of <paramref name="main"/>: the submodule is registered but empty (NotInitialized).</summary>
    private static string FreshClone(GitFixture fx, string main)
    {
        var clone = Path.Combine(fx.Root, "clone");
        GitFixture.Git(fx.Root, "clone", main, clone);
        return clone;
    }

    [RequiresGitFact]
    public async Task Target_is_pinned_after_initializing_closing_and_reopening()
    {
        using var fx = new GitFixture();
        var main = FreshClone(fx, AddSubmodule(fx, MakeOrigin(fx, "lib", "feature"), "external/lib"));
        var settings = new AppSettings();
        var spy = new SpyService();
        var dialogs = new FakeDialogs();

        var first = Reopen(spy, main, dialogs, settings);
        await first.RefreshAsync();
        Assert.Equal(SubmoduleTarget.Pinned, first.Target);
        Assert.Equal(SubmoduleState.NotInitialized, first.Rows.Single().State);
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
        var main = FreshClone(fx, AddSubmodule(fx, MakeOrigin(fx, "lib", "feature"), "external/lib"));
        var spy = new SpyService();
        var dialogs = new FakeDialogs { Confirm = false };
        var vm = Reopen(spy, main, dialogs, new AppSettings());
        await vm.RefreshAsync();
        Assert.Equal(SubmoduleState.NotInitialized, vm.Rows.Single().State);
        vm.Target = SubmoduleTarget.LatestFromBranch;
        foreach (var r in vm.Rows) r.IsSelected = true;

        await vm.InitializeSelectedCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Confirmations);
        Assert.Empty(spy.InitPaths);
        Assert.Equal(SubmoduleTarget.LatestFromBranch, vm.Target);
    }

    // ── Bulk pull / switch / reset ────────────────────────────────────────

    private static void CommitOnCurrentBranch(string repo, string file)
    {
        GitFixture.Write(Path.Combine(repo, file), file);
        GitFixture.Git(repo, "add", ".");
        GitFixture.Git(repo, "commit", "-m", file);
    }

    private static string Head(string folder) => GitFixture.Git(folder, "rev-parse", "HEAD").Trim();

    private static string FolderOf(string main, string path) => Path.Combine(main, path.Replace('/', Path.DirectorySeparatorChar));

    private static async Task Tick(SubmodulesViewModel vm, params string[] paths)
    {
        await Task.CompletedTask;
        foreach (var r in vm.Rows) r.IsSelected = paths.Length == 0 || paths.Contains(r.DisplayPath);
    }

    [RequiresGitFact]
    public async Task OriginUrl_is_read_for_populated_rows_and_null_otherwise()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "liba", "feature");
        var main = AddSubmodules(fx, (origin, "external/a"));
        var clone = FreshClone(fx, main);
        var svc = NewService();

        var populated = Assert.Single(await svc.ListAsync(main));
        Assert.Equal(new Uri(origin).AbsoluteUri, populated.OriginUrl);

        var empty = Assert.Single(await svc.ListAsync(clone));
        Assert.Equal(SubmoduleState.NotInitialized, empty.State);
        Assert.Null(empty.OriginUrl);
    }

    [RequiresGitFact]
    public async Task Switch_reports_BranchNotOnRemote_when_origin_has_no_such_branch()
    {
        using var fx = new GitFixture();
        var main = AddSubmodule(fx, MakeOrigin(fx, "lib", "feature"), "external/lib");
        var svc = NewService();

        var result = await svc.SwitchBranchAsync(await Row(svc, main, "external/lib"), "nope", NoAuth);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(SwitchOutcome.BranchNotOnRemote, result.Outcome);
    }

    [RequiresGitFact]
    public async Task Selection_model_lets_clean_ready_rows_be_ticked_but_only_problems_initialize()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx, (MakeOrigin(fx, "liba", "feature"), "external/a"));
        var (vm, spy, _) = await OpenWindowModel(main);

        var row = vm.Rows.Single();
        Assert.Equal(SubmoduleState.Ready, row.State);
        Assert.False(row.CanInitialize);
        Assert.True(row.CanSwitch);
        Assert.True(row.CanPull);
        Assert.True(row.IsSelectable);

        Assert.Equal("Pull all (1)", vm.PullButtonText);
        Assert.Equal("Switch branch… (0)", vm.SwitchButtonText);
        Assert.Equal("0 selected", vm.SelectedText);

        row.IsSelected = true;
        Assert.Equal("Pull (1)", vm.PullButtonText);
        Assert.Equal("Switch branch… (1)", vm.SwitchButtonText);
        Assert.Equal("1 selected", vm.SelectedText);

        // Pinned target: a clean Ready row has nothing to initialize.
        Assert.False(vm.InitializeSelectedCommand.CanExecute(null));
        vm.SelectAllWithProblemsCommand.Execute(null);
        Assert.Empty(spy.InitPaths);

        // Latest from branch: the ticked Ready row is updated too.
        vm.Target = SubmoduleTarget.LatestFromBranch;
        Assert.True(vm.InitializeSelectedCommand.CanExecute(null));
        await vm.InitializeSelectedCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "external/a" }, spy.InitPaths);
    }

    [RequiresGitFact]
    public async Task Pull_reports_updated_and_already_up_to_date_and_never_confirms()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "liba", "feature");
        var main = AddSubmodules(fx, (origin, "external/a"));
        var (vm, spy, dialogs) = await OpenWindowModel(main);

        await vm.PullSelectedCommand.ExecuteAsync(null);
        Assert.Equal(1, spy.SwitchCalls);
        Assert.Equal("Pulled 1 of 1: 0 updated, 1 already up to date.", vm.ResultText);

        CommitOnCurrentBranch(origin, "c.txt");
        var tip = Head(origin);

        await vm.PullSelectedCommand.ExecuteAsync(null);
        Assert.Equal("Pulled 1 of 1: 1 updated, 0 already up to date.", vm.ResultText);
        var row = vm.Rows.Single();
        Assert.Equal(tip, row.Info.CurrentSha);
        Assert.Equal("on main", row.BranchLineText);
        Assert.Empty(dialogs.Confirmations);
    }

    [RequiresGitFact]
    public async Task Pull_leaves_a_diverged_branch_as_is_and_says_so()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx, "liba", "feature");
        var main = AddSubmodules(fx, (origin, "external/a"));
        var folder = FolderOf(main, "external/a");
        CommitOnCurrentBranch(folder, "local.txt");
        var local = Head(folder);
        CommitOnCurrentBranch(origin, "remote.txt");

        var (vm, _, _) = await OpenWindowModel(main);
        await vm.PullSelectedCommand.ExecuteAsync(null);

        var row = vm.Rows.Single();
        Assert.Equal(SubmoduleSkipReason.LocalCommits, row.Skip);
        Assert.Equal("Skipped", row.DisplayStateText);
        Assert.Equal("Pulled 0 of 1. 1 left as is (local commits): external/a.", vm.ResultText);
        Assert.Equal(local, Head(folder));
        Assert.True(File.Exists(Path.Combine(folder, "local.txt")));
    }

    [RequiresGitFact]
    public async Task Pull_skips_a_dirty_row_without_calling_switch_and_a_detached_row_as_not_on_a_branch()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx,
            (MakeOrigin(fx, "liba", "feature"), "external/a"),
            (MakeOrigin(fx, "libb", "feature"), "external/b"),
            (MakeOrigin(fx, "libc", "feature"), "external/c"));
        GitFixture.Write(Path.Combine(FolderOf(main, "external/a"), "a.txt"), "changed");
        GitFixture.Git(FolderOf(main, "external/b"), "checkout", "--detach");

        var (vm, spy, _) = await OpenWindowModel(main);
        Assert.False(vm.Rows.Single(r => r.DisplayPath == "external/b").CanPull);
        await Tick(vm);

        await vm.PullSelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, spy.SwitchCalls);
        Assert.Equal(SubmoduleSkipReason.UncommittedChanges, vm.Rows.Single(r => r.DisplayPath == "external/a").Skip);
        Assert.Equal(SubmoduleSkipReason.NotOnBranch, vm.Rows.Single(r => r.DisplayPath == "external/b").Skip);
        Assert.False(vm.Rows.Single(r => r.DisplayPath == "external/c").HasError);
        Assert.Equal(
            "Pulled 1 of 3: 0 updated, 1 already up to date. " +
            "1 skipped (uncommitted changes): external/a. 1 skipped (not on a branch): external/b.",
            vm.ResultText);
        // Skipped rows keep their tick, the pulled one does not.
        Assert.True(vm.Rows.Single(r => r.DisplayPath == "external/a").IsSelected);
        Assert.False(vm.Rows.Single(r => r.DisplayPath == "external/c").IsSelected);
    }

    [RequiresGitFact]
    public async Task Pull_skips_a_local_only_branch_as_not_on_its_remote()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx, (MakeOrigin(fx, "liba", "feature"), "external/a"));
        GitFixture.Git(FolderOf(main, "external/a"), "checkout", "-b", "local-only");

        var (vm, _, _) = await OpenWindowModel(main);
        await vm.PullSelectedCommand.ExecuteAsync(null);

        Assert.Equal(SubmoduleSkipReason.BranchNotOnRemote, vm.Rows.Single().Skip);
        Assert.Contains("1 skipped (branch not on its remote): external/a.", vm.ResultText);
    }

    [RequiresGitFact]
    public async Task Bulk_switch_offers_branches_per_remote_and_skips_rows_whose_remote_lacks_it()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx,
            (MakeOrigin(fx, "liba", "feature"), "external/a"),
            (MakeOrigin(fx, "libb", "other"), "external/b"));
        var (vm, spy, dialogs) = await OpenWindowModel(main);
        await Tick(vm);
        dialogs.BranchToPick = "feature";

        await vm.SwitchSelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, dialogs.LastBranchModel!.RowCount);
        Assert.Equal(new[]
        {
            new BranchOption("feature", 1, 0),
            new BranchOption("main", 2, 2),
            new BranchOption("other", 1, 0),
        }, dialogs.LastOptions);

        Assert.Equal(1, spy.SwitchCalls);
        Assert.Equal("feature", vm.Rows.Single(r => r.DisplayPath == "external/a").Info.CurrentBranch);
        var b = vm.Rows.Single(r => r.DisplayPath == "external/b");
        Assert.Equal("main", b.Info.CurrentBranch);
        Assert.Equal(SubmoduleSkipReason.BranchNotOnRemote, b.Skip);
        Assert.Equal("Switched 1 of 2 to feature. 1 skipped (branch not on its remote): external/b.", vm.ResultText);
    }

    [RequiresGitFact]
    public async Task Bulk_switch_lists_branches_once_per_distinct_remote()
    {
        using var fx = new GitFixture();
        var shared = MakeOrigin(fx, "shared", "feature");
        var main = AddSubmodules(fx,
            (shared, "external/a"), (shared, "external/b"),
            (MakeOrigin(fx, "libc", "feature"), "external/c"));
        var (vm, spy, dialogs) = await OpenWindowModel(main);
        await Tick(vm);
        dialogs.BranchToPick = "feature";

        await vm.SwitchSelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, spy.ListCalls);
        Assert.Equal(3, spy.SwitchCalls);
        Assert.Equal("Switched 3 of 3 to feature.", vm.ResultText);
    }

    [RequiresGitFact]
    public async Task Bulk_switch_fast_forwards_rows_already_on_the_branch()
    {
        using var fx = new GitFixture();
        var originA = MakeOrigin(fx, "liba", "feature");
        var main = AddSubmodules(fx, (originA, "external/a"), (MakeOrigin(fx, "libb", "feature"), "external/b"));
        var svc = NewService();
        Assert.Equal(0, (await svc.SwitchBranchAsync(await Row(svc, main, "external/a"), "feature", NoAuth)).ExitCode);

        GitFixture.Git(originA, "checkout", "feature");
        CommitOnCurrentBranch(originA, "more.txt");
        var tip = Head(originA);
        GitFixture.Git(originA, "checkout", "main");

        var (vm, _, dialogs) = await OpenWindowModel(main);
        await Tick(vm);
        dialogs.BranchToPick = "feature";

        await vm.SwitchSelectedCommand.ExecuteAsync(null);

        Assert.Contains(new BranchOption("feature", 2, 1), dialogs.LastOptions!);
        Assert.Equal(tip, vm.Rows.Single(r => r.DisplayPath == "external/a").Info.CurrentSha);
        Assert.Equal("feature", vm.Rows.Single(r => r.DisplayPath == "external/b").Info.CurrentBranch);
        Assert.Equal("Switched 2 of 2 to feature.", vm.ResultText);
    }

    [RequiresGitFact]
    public async Task Bulk_switch_skips_dirty_rows_in_preflight_and_does_not_count_them_in_the_picker()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx,
            (MakeOrigin(fx, "liba", "feature"), "external/a"),
            (MakeOrigin(fx, "libb", "feature"), "external/b"),
            (MakeOrigin(fx, "libc", "feature"), "external/c"));
        GitFixture.Write(Path.Combine(FolderOf(main, "external/a"), "a.txt"), "changed");
        var (vm, spy, dialogs) = await OpenWindowModel(main);
        await Tick(vm);
        dialogs.BranchToPick = "feature";

        await vm.SwitchSelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, dialogs.LastBranchModel!.RowCount);
        Assert.Equal(2, spy.SwitchCalls);
        Assert.Equal("main", vm.Rows.Single(r => r.DisplayPath == "external/a").Info.CurrentBranch);
        Assert.Equal(SubmoduleSkipReason.UncommittedChanges, vm.Rows.Single(r => r.DisplayPath == "external/a").Skip);
        Assert.Equal("Switched 2 of 3 to feature. 1 skipped (uncommitted changes): external/a.", vm.ResultText);
    }

    [RequiresGitFact]
    public async Task Bulk_switch_asks_once_for_unreferenced_rows_and_No_skips_only_those()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx,
            (MakeOrigin(fx, "liba", "feature"), "external/a"),
            (MakeOrigin(fx, "libb", "feature"), "external/b"),
            (MakeOrigin(fx, "libc", "feature"), "external/c"));
        var folderA = FolderOf(main, "external/a");
        GitFixture.Git(folderA, "checkout", "--detach");
        CommitOnCurrentBranch(folderA, "d.txt");
        var orphan = Head(folderA);

        var (vm, spy, dialogs) = await OpenWindowModel(main);
        await Tick(vm);
        dialogs.BranchToPick = "feature";
        dialogs.Confirm = false;

        await vm.SwitchSelectedCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Confirmations);
        Assert.True(Assert.Single(dialogs.DestructiveFlags));
        Assert.Equal(2, spy.SwitchCalls);
        Assert.Equal(SubmoduleSkipReason.Declined, vm.Rows.Single(r => r.DisplayPath == "external/a").Skip);
        Assert.Equal(orphan, Head(folderA));
        Assert.Equal("Switched 2 of 3 to feature. 1 skipped (declined): external/a.", vm.ResultText);
    }

    [RequiresGitFact]
    public async Task Bulk_switch_re_reads_a_nested_child_after_its_parent_was_switched()
    {
        using var fx = new GitFixture();
        var inner = fx.Sub("inner");
        GitFixture.Git(inner, "init");
        CommitOnCurrentBranch(inner, "i.txt");
        GitFixture.Git(inner, "branch", "nosub");

        // lib: on main it has vendor/x; its branch "nosub" drops that submodule.
        var lib = fx.Sub("lib");
        GitFixture.Git(lib, "init");
        CommitOnCurrentBranch(lib, "l.txt");
        GitFixture.Git(lib, "submodule", "add", new Uri(inner).AbsoluteUri, "vendor/x");
        GitFixture.Git(lib, "commit", "-m", "add inner");
        GitFixture.Git(lib, "checkout", "-b", "nosub");
        GitFixture.Git(lib, "rm", "vendor/x");
        GitFixture.Git(lib, "commit", "-m", "drop inner");
        GitFixture.Git(lib, "checkout", "main");

        var main = AddSubmodules(fx, (lib, "external/lib"));
        var svc = NewService();
        var nested = (await svc.ListAsync(main))[1];
        Assert.Equal(0, (await svc.InitAndUpdateAsync(nested.RepoRoot, nested, false, false, NoAuth)).ExitCode);

        var (vm, spy, dialogs) = await OpenWindowModel(main);
        Assert.Equal(2, vm.Rows.Count);
        await Tick(vm);
        dialogs.BranchToPick = "nosub";

        await vm.SwitchSelectedCommand.ExecuteAsync(null);

        // Parent first (list order); the child is gone by the time its turn comes, so it is skipped, not switched.
        Assert.Equal(1, spy.SwitchCalls);
        Assert.Equal("nosub", (await svc.GetAsync(main, "external/lib"))!.CurrentBranch);
        Assert.Contains("1 skipped (nothing to do): external/lib/vendor/x.", vm.ResultText);
        Assert.StartsWith("Switched 1 of 2 to nosub.", vm.ResultText);
    }

    [RequiresGitFact]
    public async Task Reset_selected_confirms_once_resets_clean_rows_and_skips_dirty_ones()
    {
        using var fx = new GitFixture();
        var main = AddSubmodules(fx,
            (MakeOrigin(fx, "liba", "feature"), "external/a"),
            (MakeOrigin(fx, "libb", "feature"), "external/b"));
        var svc = NewService();
        foreach (var path in new[] { "external/a", "external/b" })
            Assert.Equal(0, (await svc.SwitchBranchAsync(await Row(svc, main, path), "feature", NoAuth)).ExitCode);
        GitFixture.Write(Path.Combine(FolderOf(main, "external/a"), "a.txt"), "changed");

        var (vm, spy, dialogs) = await OpenWindowModel(main);
        await Tick(vm);

        await vm.ResetSelectedCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Confirmations);
        Assert.True(Assert.Single(dialogs.DestructiveFlags));
        Assert.Equal(1, spy.ResetCalls);
        Assert.Equal("Reset 1 of 2 to the recorded commit. 1 skipped (uncommitted changes): external/a.", vm.ResultText);
        Assert.Equal("detached", vm.Rows.Single(r => r.DisplayPath == "external/b").BranchLineText);
        Assert.Equal("on feature", vm.Rows.Single(r => r.DisplayPath == "external/a").BranchLineText);
    }

    [RequiresGitFact]
    public async Task Select_all_with_this_remote_ticks_rows_sharing_the_ticked_remote()
    {
        using var fx = new GitFixture();
        var shared = MakeOrigin(fx, "shared", "feature");
        var main = AddSubmodules(fx,
            (shared, "external/a"), (shared, "external/b"),
            (MakeOrigin(fx, "libc", "feature"), "external/c"));
        var (vm, _, _) = await OpenWindowModel(main);

        Assert.False(vm.SelectAllWithRemoteCommand.CanExecute(null));
        vm.Rows.Single(r => r.DisplayPath == "external/a").IsSelected = true;
        Assert.True(vm.SelectAllWithRemoteCommand.CanExecute(null));

        vm.SelectAllWithRemoteCommand.Execute(null);

        Assert.Equal(new[] { "external/a", "external/b" }, vm.Rows.Where(r => r.IsSelected).Select(r => r.DisplayPath));

        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
    }
}
