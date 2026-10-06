using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    public enum BranchSwitchStatus
    {
        /// <summary>The user backed out, or a preflight stopped the flow before anything changed.</summary>
        Cancelled,
        /// <summary>Pending folder changes were applied first; the user starts the switch again.</summary>
        AppliedFirst,
        /// <summary>Tracked local changes block the switch.</summary>
        Blocked,
        /// <summary>The fetch or checkout failed (or the branch is gone from origin).</summary>
        Failed,
        /// <summary>Checked out; the local branch may not have been fast-forwarded (see <see cref="BranchSwitchResult.Message"/>).</summary>
        Switched
    }

    /// <param name="Attempted">True once the checkout may have changed something, so the caller must re-read the checkout.</param>
    public sealed record BranchSwitchResult(BranchSwitchStatus Status, string Message, string? Branch = null, bool Attempted = false);

    /// <summary>What the caller supplies for one switch run; everything UI-specific comes in as a delegate.</summary>
    public sealed class BranchSwitchRequest
    {
        public required string Root { get; init; }
        public string? CurrentBranch { get; init; }
        public bool HasPendingFolderChanges { get; init; }
        public required Func<Task> ApplyPendingAsync { get; init; }
        public required Func<Task> DiscardPendingAsync { get; init; }
        public required Func<GitAuth?> ResolveAuth { get; init; }
        public Action<string> SetStatus { get; init; } = _ => { };
        /// <summary>Called with the fresh status read, so the summary count stays in step.</summary>
        public Action<LocalChangeSet> LocalChangesRead { get; init; } = _ => { };
        /// <summary>Opens the existing Local changes window.</summary>
        public Action ShowLocalChanges { get; init; } = () => { };
        /// <summary>Called when the fetch has ended: from here on Cancel no longer applies.</summary>
        public Action FetchFinished { get; init; } = () => { };
    }

    /// <summary>What the new branch looks like compared with the sparse selection and the submodule pins.</summary>
    public sealed record AfterSwitchReport(IReadOnlyList<string> MissingFolders, int SubmodulesNeedingAttention);

    /// <summary>
    /// The Manage tab's "switch branch" flow: preflight, branch picker, switch. UI-free apart from
    /// <see cref="IDialogService"/>, so it runs against real repos in tests. It never commits and never
    /// updates submodules.
    /// </summary>
    public sealed class BranchSwitchFlow
    {
        private readonly ICheckoutService _checkout;
        private readonly ISubmoduleService _submodules;
        private readonly IDialogService _dialogs;

        public BranchSwitchFlow(ICheckoutService checkout, ISubmoduleService submodules, IDialogService dialogs)
        {
            _checkout = checkout;
            _submodules = submodules;
            _dialogs = dialogs;
        }

        public async Task<BranchSwitchResult> RunAsync(BranchSwitchRequest req, CancellationToken fetchCt)
        {
            // 1. Folder changes that are ticked but not applied.
            if (req.HasPendingFolderChanges)
            {
                var choice = _dialogs.ShowThreeWayChoice("Folder changes not applied",
                    "You have folder changes that are not applied yet. Apply them, or discard them, before switching branch.",
                    "Apply", "Discard");
                switch (choice)
                {
                    case ThreeWayChoice.Primary:
                        await req.ApplyPendingAsync();
                        return new BranchSwitchResult(BranchSwitchStatus.AppliedFirst,
                            "Folder changes applied. Start the branch switch again when you're ready.");
                    case ThreeWayChoice.Secondary:
                        await req.DiscardPendingAsync();
                        break;
                    default:
                        return new BranchSwitchResult(BranchSwitchStatus.Cancelled, "Branch switch cancelled.");
                }
            }

            // 2. A fresh status read: tracked changes block; untracked files and submodule entries don't.
            LocalChangeSet changes;
            try
            {
                changes = await _checkout.GetLocalChangesAsync(req.Root, fetchCt);
            }
            catch (OperationCanceledException)
            {
                return new BranchSwitchResult(BranchSwitchStatus.Cancelled, "Branch switch cancelled.");
            }
            catch (Exception ex)
            {
                return new BranchSwitchResult(BranchSwitchStatus.Failed, $"Could not check for local changes: {ex.Message}");
            }
            req.LocalChangesRead(changes);

            if (changes.BlockingCount > 0)
            {
                var n = changes.BlockingCount;
                var message = $"You have {n} local {(n == 1 ? "change" : "changes")}. Commit or discard them first.";
                if (_dialogs.ShowThreeWayChoice("Local changes", message, "Show local changes") == ThreeWayChoice.Primary)
                    req.ShowLocalChanges();
                return new BranchSwitchResult(BranchSwitchStatus.Blocked, message);
            }

            // 3. A detached HEAD on a commit no ref contains would be stranded by the switch.
            try
            {
                if (await _checkout.IsDetachedOnUnreferencedCommitAsync(req.Root, fetchCt) &&
                    !_dialogs.ShowConfirmation("Switch away from a detached commit",
                        "HEAD is detached on a commit that no branch or tag contains. After switching, that commit is only " +
                        "reachable through the reflog and may be lost.\n\nSwitch anyway?", destructive: true))
                    return new BranchSwitchResult(BranchSwitchStatus.Cancelled, "Branch switch cancelled.");
            }
            catch (OperationCanceledException)
            {
                return new BranchSwitchResult(BranchSwitchStatus.Cancelled, "Branch switch cancelled.");
            }
            catch (Exception ex)
            {
                return new BranchSwitchResult(BranchSwitchStatus.Failed, $"Could not check HEAD: {ex.Message}");
            }

            // 4. Pick a branch.
            var branch = _dialogs.ShowSubmoduleBranch(new SubmoduleBranchModel
            {
                DisplayPath = req.Root,
                Header = "Switch the branch of this checkout",
                CurrentBranch = req.CurrentBranch,
                LoadBranchesAsync = ct => _checkout.ListRemoteBranchesAsync(req.Root, req.ResolveAuth(), ct)
            });
            if (string.IsNullOrWhiteSpace(branch))
                return new BranchSwitchResult(BranchSwitchStatus.Cancelled, "Branch switch cancelled.");

            // Untracked files and submodule changes don't block, but the user should know they stay as they are.
            if (changes.UntrackedCount > 0 || changes.SubmoduleCount > 0)
            {
                var notes = new List<string>();
                if (changes.UntrackedCount > 0) notes.Add($"{changes.UntrackedCount} untracked file(s)");
                if (changes.SubmoduleCount > 0) notes.Add($"{changes.SubmoduleCount} changed submodule(s)");
                if (!_dialogs.ShowConfirmation("Switch branch",
                        $"Switch to {branch}? This checkout has {string.Join(" and ", notes)}; they stay as they are. " +
                        "If the branch tracks a file that is also untracked here, git will refuse the switch and nothing is lost."))
                    return new BranchSwitchResult(BranchSwitchStatus.Cancelled, "Branch switch cancelled.");
            }

            // 5. Switch. Cancel only reaches the fetch; once checkout starts it finishes.
            req.SetStatus($"Switching to {branch}…");
            SwitchResult result;
            try
            {
                result = await _checkout.SwitchBranchAsync(req.Root, branch, req.ResolveAuth(), fetchCt, req.FetchFinished);
            }
            catch (OperationCanceledException)
            {
                return new BranchSwitchResult(BranchSwitchStatus.Cancelled, "Branch switch cancelled.");
            }
            catch (Exception ex)
            {
                return new BranchSwitchResult(BranchSwitchStatus.Failed, $"Could not switch to {branch}: {ex.Message}", branch, Attempted: true);
            }

            if (result.ExitCode != 0)
            {
                var message = result.Outcome == SwitchOutcome.BranchNotOnRemote
                    ? $"Branch {branch} does not exist on origin."
                    : $"Could not switch to {branch}: {GitText(result)}";
                return new BranchSwitchResult(BranchSwitchStatus.Failed, message, branch, Attempted: true);
            }

            return new BranchSwitchResult(BranchSwitchStatus.Switched,
                result.Outcome == SwitchOutcome.LeftAsIs
                    ? $"Switched to {branch} (local branch has its own commits, not fast-forwarded)."
                    : $"Switched to {branch}.",
                branch, Attempted: true);
        }

        /// <summary>
        /// Compares the (re-read) checkout with what the user had selected: selected folders the new branch lacks,
        /// and submodules that are now on another commit than recorded or not initialized. Read-only.
        /// </summary>
        public async Task<AfterSwitchReport> InspectAsync(string root, IEnumerable<string> sparsePaths, CancellationToken ct = default)
        {
            var tree = await _checkout.GetTreeAsync(root, ct);
            var folders = tree.Where(n => n.IsFolder).Select(n => n.Path).ToHashSet(StringComparer.Ordinal);
            var missing = sparsePaths
                .Select(p => p.Trim('/'))
                .Where(p => p.Length > 0 && !folders.Contains(p))
                .ToList();

            var subs = await _submodules.ListAsync(root, ct);
            var attention = subs.Count(s => s.State is SubmoduleState.DifferentCommit or SubmoduleState.NotInitialized);

            return new AfterSwitchReport(missing, attention);
        }

        private static string GitText(SwitchResult r)
        {
            var text = !string.IsNullOrWhiteSpace(r.StdErr) ? r.StdErr : r.StdOut;
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0);
            var joined = string.Join(" ", lines);
            return joined.Length > 0 ? joined : "git failed.";
        }
    }
}
