using GitCheckoutManager.Models;
using GitCheckoutManager.Services;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Tests;

/// <summary>Switching the branch of the checkout itself (Manage tab), against real git and file:// repos.</summary>
public class ManageBranchSwitchTests
{
    static ManageBranchSwitchTests()
    {
        // GitService children inherit this: newer git blocks the file transport for submodule clones and lazy fetches.
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "protocol.file.allow");
        Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "always");
    }

    private static CheckoutService NewCheckout() => new(new GitService());

    private static BranchSwitchFlow NewFlow(FakeDialogs dialogs) =>
        new(NewCheckout(), new SubmoduleService(new GitService()), dialogs);

    /// <summary>main: a.txt, apps/api/x.txt, apps/web/w.txt, docs/d.md. feature = main + b.txt, new apps/api/x.txt, apps/new/n.txt, without docs.</summary>
    private static string MakeOrigin(GitFixture fx, string name = "origin", string? submoduleUrl = null)
    {
        var o = fx.Sub(name);
        GitFixture.Git(o, "init");
        GitFixture.Git(o, "config", "uploadpack.allowFilter", "true");
        GitFixture.Git(o, "config", "uploadpack.allowAnySHA1InWant", "true");
        foreach (var (path, text) in new[]
                 { ("a.txt", "a"), ("apps/api/x.txt", "x1"), ("apps/web/w.txt", "w"), ("docs/d.md", "d") })
            GitFixture.Write(Path.Combine(o, path), text);
        GitFixture.Git(o, "add", ".");
        GitFixture.Git(o, "commit", "-m", "first");
        if (submoduleUrl != null)
        {
            GitFixture.Git(o, "submodule", "add", submoduleUrl, "external/lib");
            GitFixture.Git(o, "commit", "-m", "add submodule");
        }
        GitFixture.Git(o, "checkout", "-b", "feature");
        GitFixture.Write(Path.Combine(o, "b.txt"), "b");
        GitFixture.Write(Path.Combine(o, "apps/api/x.txt"), "x2");
        GitFixture.Write(Path.Combine(o, "apps/new/n.txt"), "n");
        GitFixture.Git(o, "rm", "-r", "-q", "docs");
        GitFixture.Git(o, "add", ".");
        GitFixture.Git(o, "commit", "-m", "on feature");
        GitFixture.Git(o, "checkout", "main");
        return o;
    }

    private static string Clone(GitFixture fx, string origin, string name = "clone", params string[] extra)
    {
        var clone = Path.Combine(fx.Root, name);
        GitFixture.Git(fx.Root, new[] { "clone" }.Concat(extra).Concat(new[] { new Uri(origin).AbsoluteUri, clone }).ToArray());
        return clone;
    }

    private static string Head(string repo) => GitFixture.Git(repo, "rev-parse", "HEAD").Trim();
    private static string Branch(string repo) => GitFixture.Git(repo, "branch", "--show-current").Trim();

    // ── Helper: the switch itself ─────────────────────────────────────────

    [RequiresGitFact]
    public async Task Switch_to_a_branch_without_a_local_branch_creates_a_tracking_branch()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));

        var result = await NewCheckout().SwitchBranchAsync(clone, "feature", null);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(SwitchOutcome.None, result.Outcome);
        Assert.Equal("feature", Branch(clone));
        Assert.Equal("origin/feature", GitFixture.Git(clone, "rev-parse", "--abbrev-ref", "feature@{upstream}").Trim());
        Assert.True(File.Exists(Path.Combine(clone, "b.txt")));
    }

    [RequiresGitFact]
    public async Task Switch_works_in_a_single_branch_clone()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx), "single", "--single-branch", "-b", "main");

        var result = await NewCheckout().SwitchBranchAsync(clone, "feature", null);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("feature", Branch(clone));
    }

    [RequiresGitFact]
    public async Task Switch_to_an_existing_local_branch_fast_forwards_it()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx);
        var clone = Clone(fx, origin);
        GitFixture.Git(clone, "branch", "--no-track", "feature", "origin/main");

        var result = await NewCheckout().SwitchBranchAsync(clone, "feature", null);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(SwitchOutcome.None, result.Outcome);
        Assert.Equal("feature", Branch(clone));
        Assert.Equal(GitFixture.Git(origin, "rev-parse", "feature").Trim(), Head(clone));
    }

    [RequiresGitFact]
    public async Task Switch_to_a_diverged_local_branch_leaves_it_as_is()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx);
        var clone = Clone(fx, origin);
        GitFixture.Git(clone, "checkout", "-b", "feature", "--no-track", "origin/main");
        GitFixture.Write(Path.Combine(clone, "mine.txt"), "mine");
        GitFixture.Git(clone, "add", ".");
        GitFixture.Git(clone, "commit", "-m", "local only");
        var local = Head(clone);
        GitFixture.Git(clone, "checkout", "main");

        var result = await NewCheckout().SwitchBranchAsync(clone, "feature", null);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(SwitchOutcome.LeftAsIs, result.Outcome);
        Assert.Equal("feature", Branch(clone));
        Assert.Equal(local, Head(clone));
        Assert.True(File.Exists(Path.Combine(clone, "mine.txt")));
    }

    [RequiresGitFact]
    public async Task Switch_to_a_branch_missing_on_origin_reports_it_and_stays_put()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        var before = Head(clone);

        var result = await NewCheckout().SwitchBranchAsync(clone, "nope", null);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(SwitchOutcome.BranchNotOnRemote, result.Outcome);
        Assert.Equal("main", Branch(clone));
        Assert.Equal(before, Head(clone));
    }

    [RequiresGitFact]
    public async Task Untracked_file_the_target_branch_tracks_makes_git_refuse_and_nothing_is_lost()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        var mine = Path.Combine(clone, "b.txt");
        File.WriteAllText(mine, "mine");
        var before = Head(clone);

        var result = await NewCheckout().SwitchBranchAsync(clone, "feature", null);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("b.txt", result.StdErr);
        Assert.Equal("main", Branch(clone));
        Assert.Equal(before, Head(clone));
        Assert.Equal("mine", File.ReadAllText(mine));
    }

    [RequiresGitFact]
    public async Task Blobless_sparse_clone_switches_and_downloads_the_new_blobs()
    {
        using var fx = new GitFixture();
        var origin = MakeOrigin(fx);
        var clone = Clone(fx, origin, "blobless", "--filter=blob:none", "--no-checkout");
        GitFixture.Git(clone, "sparse-checkout", "init", "--cone");
        GitFixture.Git(clone, "sparse-checkout", "set", "apps/api");
        GitFixture.Git(clone, "checkout", "main");
        Assert.Equal("x1", File.ReadAllText(Path.Combine(clone, "apps", "api", "x.txt")));
        Assert.False(Directory.Exists(Path.Combine(clone, "apps", "web")));

        var result = await NewCheckout().SwitchBranchAsync(clone, "feature", null);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("feature", Branch(clone));
        Assert.Equal("x2", File.ReadAllText(Path.Combine(clone, "apps", "api", "x.txt")));
        Assert.False(Directory.Exists(Path.Combine(clone, "apps", "web")));
    }

    [RequiresGitFact]
    public async Task ListRemoteBranches_reads_origin_heads()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));

        Assert.Equal(new[] { "feature", "main" }, await NewCheckout().ListRemoteBranchesAsync(clone, null));
    }

    // ── Flow: preflight ───────────────────────────────────────────────────

    private static BranchSwitchRequest Request(string root, bool pending = false,
        Action? apply = null, Action? discard = null, Action? showChanges = null, string? current = "main") => new()
    {
        Root = root,
        CurrentBranch = current,
        HasPendingFolderChanges = pending,
        ApplyPendingAsync = () => { apply?.Invoke(); return Task.CompletedTask; },
        DiscardPendingAsync = () => { discard?.Invoke(); return Task.CompletedTask; },
        ResolveAuth = () => null,
        ShowLocalChanges = showChanges ?? (() => { })
    };

    [RequiresGitFact]
    public async Task Tracked_changes_block_the_switch_and_offer_the_local_changes_window()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        File.WriteAllText(Path.Combine(clone, "a.txt"), "edited");
        GitFixture.Write(Path.Combine(clone, "staged.txt"), "s");
        GitFixture.Git(clone, "add", "staged.txt");
        var dialogs = new FakeDialogs { ThreeWay = ThreeWayChoice.Primary, BranchToPick = "feature" };
        var shown = false;

        var result = await NewFlow(dialogs).RunAsync(Request(clone, showChanges: () => shown = true), default);

        Assert.Equal(BranchSwitchStatus.Blocked, result.Status);
        Assert.Contains("You have 2 local changes. Commit or discard them first.", dialogs.ThreeWayMessages.Single());
        Assert.True(shown);
        Assert.Equal(0, dialogs.PickerCalls);
        Assert.Equal("main", Branch(clone));
        Assert.Equal("edited", File.ReadAllText(Path.Combine(clone, "a.txt")));
    }

    [RequiresGitFact]
    public async Task Untracked_only_changes_do_not_block_and_are_mentioned_in_the_confirmation()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        GitFixture.Write(Path.Combine(clone, "scratch.txt"), "keep me");
        var dialogs = new FakeDialogs { BranchToPick = "feature" };

        var result = await NewFlow(dialogs).RunAsync(Request(clone), default);

        Assert.Equal(BranchSwitchStatus.Switched, result.Status);
        Assert.Equal("Switched to feature.", result.Message);
        Assert.Contains("1 untracked", dialogs.Confirmations.Single());
        Assert.Equal("feature", Branch(clone));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(clone, "scratch.txt")));
        Assert.Equal("Switch the branch of this checkout", dialogs.LastBranchModel!.Header);
    }

    [RequiresGitFact]
    public async Task Submodule_only_changes_do_not_block()
    {
        using var fx = new GitFixture();
        var lib = fx.Sub("lib");
        GitFixture.Git(lib, "init");
        GitFixture.Write(Path.Combine(lib, "l.txt"), "l");
        GitFixture.Git(lib, "add", ".");
        GitFixture.Git(lib, "commit", "-m", "lib");

        var origin = MakeOrigin(fx, "parent", new Uri(lib).AbsoluteUri);
        var clone = Clone(fx, origin, "pclone", "--recurse-submodules");

        // A new commit inside the submodule makes the parent report the gitlink as modified.
        var sub = Path.Combine(clone, "external", "lib");
        GitFixture.Write(Path.Combine(sub, "more.txt"), "m");
        GitFixture.Git(sub, "add", ".");
        GitFixture.Git(sub, "commit", "-m", "more");

        var changes = await NewCheckout().GetLocalChangesAsync(clone);
        Assert.Equal(1, changes.SubmoduleCount);
        Assert.Equal(0, changes.BlockingCount);

        var dialogs = new FakeDialogs { BranchToPick = "feature" };
        var result = await NewFlow(dialogs).RunAsync(Request(clone), default);

        Assert.Equal(BranchSwitchStatus.Switched, result.Status);
        Assert.Contains("1 changed submodule", dialogs.Confirmations.Single());
        Assert.Equal("feature", Branch(clone));
    }

    [RequiresGitFact]
    public async Task Pending_folder_changes_stop_the_flow_until_applied_or_discarded()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));

        // Cancel: nothing happens.
        var cancel = new FakeDialogs { ThreeWay = ThreeWayChoice.Cancel, BranchToPick = "feature" };
        var r1 = await NewFlow(cancel).RunAsync(Request(clone, pending: true), default);
        Assert.Equal(BranchSwitchStatus.Cancelled, r1.Status);
        Assert.Equal(0, cancel.PickerCalls);

        // Apply: runs the apply, then stops so the user starts again.
        var applied = false;
        var apply = new FakeDialogs { ThreeWay = ThreeWayChoice.Primary, BranchToPick = "feature" };
        var r2 = await NewFlow(apply).RunAsync(Request(clone, pending: true, apply: () => applied = true), default);
        Assert.Equal(BranchSwitchStatus.AppliedFirst, r2.Status);
        Assert.True(applied);
        Assert.Equal(0, apply.PickerCalls);
        Assert.Equal("main", Branch(clone));

        // Discard: resets the selection, then carries on.
        var discarded = false;
        var discard = new FakeDialogs { ThreeWay = ThreeWayChoice.Secondary, BranchToPick = "feature" };
        var r3 = await NewFlow(discard).RunAsync(Request(clone, pending: true, discard: () => discarded = true), default);
        Assert.True(discarded);
        Assert.Equal(BranchSwitchStatus.Switched, r3.Status);
        Assert.Equal("feature", Branch(clone));
    }

    [RequiresGitFact]
    public async Task Detached_unreferenced_commit_needs_an_explicit_destructive_confirmation()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        GitFixture.Git(clone, "checkout", "--detach");
        GitFixture.Write(Path.Combine(clone, "lost.txt"), "x");
        GitFixture.Git(clone, "add", ".");
        GitFixture.Git(clone, "commit", "-m", "unreferenced");
        var detachedHead = Head(clone);

        var no = new FakeDialogs { Confirm = false, BranchToPick = "feature" };
        var declined = await NewFlow(no).RunAsync(Request(clone, current: null), default);
        Assert.Equal(BranchSwitchStatus.Cancelled, declined.Status);
        Assert.True(no.DestructiveFlags.Single());
        Assert.Equal(0, no.PickerCalls);
        Assert.Equal(detachedHead, Head(clone));

        var yes = new FakeDialogs { Confirm = true, BranchToPick = "feature" };
        var accepted = await NewFlow(yes).RunAsync(Request(clone, current: null), default);
        Assert.Equal(BranchSwitchStatus.Switched, accepted.Status);
        Assert.Equal("feature", Branch(clone));
    }

    [RequiresGitFact]
    public async Task Detached_head_on_a_referenced_commit_needs_no_confirmation()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        GitFixture.Git(clone, "checkout", "--detach");
        var dialogs = new FakeDialogs { BranchToPick = "feature" };

        var result = await NewFlow(dialogs).RunAsync(Request(clone, current: null), default);

        Assert.Equal(BranchSwitchStatus.Switched, result.Status);
        Assert.Empty(dialogs.Confirmations);
    }

    // ── Flow: result and aftermath ────────────────────────────────────────

    [RequiresGitFact]
    public async Task Refused_checkout_reports_gits_message_and_keeps_the_file()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        var mine = Path.Combine(clone, "b.txt");
        File.WriteAllText(mine, "mine");
        var dialogs = new FakeDialogs { BranchToPick = "feature" };

        var result = await NewFlow(dialogs).RunAsync(Request(clone), default);

        Assert.Equal(BranchSwitchStatus.Failed, result.Status);
        Assert.True(result.Attempted);
        Assert.Contains("b.txt", result.Message);
        Assert.Equal("mine", File.ReadAllText(mine));
        Assert.Equal("main", Branch(clone));
    }

    [RequiresGitFact]
    public async Task Diverged_local_branch_is_reported_as_not_fast_forwarded()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        GitFixture.Git(clone, "checkout", "-b", "feature", "--no-track", "origin/main");
        GitFixture.Write(Path.Combine(clone, "mine.txt"), "mine");
        GitFixture.Git(clone, "add", ".");
        GitFixture.Git(clone, "commit", "-m", "local only");
        GitFixture.Git(clone, "checkout", "main");
        var dialogs = new FakeDialogs { BranchToPick = "feature" };

        var result = await NewFlow(dialogs).RunAsync(Request(clone), default);

        Assert.Equal(BranchSwitchStatus.Switched, result.Status);
        Assert.Equal("Switched to feature (local branch has its own commits, not fast-forwarded).", result.Message);
    }

    [RequiresGitFact]
    public async Task Missing_branch_on_origin_is_a_failure_that_changes_nothing()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        var dialogs = new FakeDialogs { BranchToPick = "nope" };

        var result = await NewFlow(dialogs).RunAsync(Request(clone), default);

        Assert.Equal(BranchSwitchStatus.Failed, result.Status);
        Assert.Contains("does not exist on origin", result.Message);
        Assert.Equal("main", Branch(clone));
    }

    [RequiresGitFact]
    public async Task Picker_cancel_changes_nothing()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx));
        var dialogs = new FakeDialogs { BranchToPick = null };

        var result = await NewFlow(dialogs).RunAsync(Request(clone), default);

        Assert.Equal(BranchSwitchStatus.Cancelled, result.Status);
        Assert.False(result.Attempted);
        Assert.Equal("main", Branch(clone));
    }

    [RequiresGitFact]
    public async Task Sparse_selection_is_kept_and_a_selected_folder_missing_on_the_new_branch_is_reported()
    {
        using var fx = new GitFixture();
        var clone = Clone(fx, MakeOrigin(fx), "sparse", "--no-checkout");
        GitFixture.Git(clone, "sparse-checkout", "init", "--cone");
        GitFixture.Git(clone, "sparse-checkout", "set", "apps/api", "docs");
        GitFixture.Git(clone, "checkout", "main");
        var dialogs = new FakeDialogs { BranchToPick = "feature" };
        var flow = NewFlow(dialogs);

        var result = await flow.RunAsync(Request(clone), default);
        Assert.Equal(BranchSwitchStatus.Switched, result.Status);

        var checkout = NewCheckout();
        var info = await checkout.OpenAsync(clone);
        Assert.Equal("feature", info.Branch);
        Assert.Equal(new[] { "apps/api", "docs" }, info.SparsePaths.OrderBy(p => p));
        Assert.Equal("x2", File.ReadAllText(Path.Combine(clone, "apps", "api", "x.txt")));
        Assert.False(Directory.Exists(Path.Combine(clone, "apps", "web")));

        var report = await flow.InspectAsync(clone, info.SparsePaths);
        Assert.Equal(new[] { "docs" }, report.MissingFolders);
        Assert.Equal(0, report.SubmodulesNeedingAttention);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? BranchToPick { get; set; }
        public bool Confirm { get; set; } = true;
        public ThreeWayChoice ThreeWay { get; set; } = ThreeWayChoice.Cancel;
        public List<string> Confirmations { get; } = new();
        public List<bool> DestructiveFlags { get; } = new();
        public List<string> ThreeWayMessages { get; } = new();
        public int PickerCalls { get; private set; }
        public SubmoduleBranchModel? LastBranchModel { get; private set; }

        public string? ShowSubmoduleBranch(SubmoduleBranchModel model)
        {
            PickerCalls++;
            LastBranchModel = model;
            model.LoadBranchesAsync(default).GetAwaiter().GetResult(); // like the window: the loader runs while the picker is open
            return BranchToPick;
        }

        public bool ShowConfirmation(string title, string message, string? details = null, bool destructive = false)
        {
            Confirmations.Add(message);
            DestructiveFlags.Add(destructive);
            return Confirm;
        }

        public ThreeWayChoice ShowThreeWayChoice(string title, string message, string primary, string? secondary = null)
        {
            ThreeWayMessages.Add(message);
            return ThreeWay;
        }

        public void ShowMessage(string title, string message, string? details = null) { }
        public void ShowLocalChanges(LocalChangesViewModel vm) => throw new NotSupportedException();
        public string? ShowSaveFileDialog(string filter, string defaultExtension, string defaultFileName) => throw new NotSupportedException();
        public string? ShowOpenFileDialog(string filter, string title = "Open File") => throw new NotSupportedException();
        public string? ShowOpenFolderDialog(string title = "Select Folder", string? initialPath = null) => throw new NotSupportedException();
        public string? ShowInputDialog(string title, string prompt, string defaultValue = "") => throw new NotSupportedException();
        public RemovalReviewChoices? ShowRemovalReview(RemovalReviewModel model) => throw new NotSupportedException();
        public void ShowSettings(SettingsViewModel viewModel) => throw new NotSupportedException();
        public void ShowSubmodules(SubmodulesViewModel vm) => throw new NotSupportedException();
        public string? ShowSubmoduleUrl(SubmoduleUrlModel model) => throw new NotSupportedException();
    }
}
