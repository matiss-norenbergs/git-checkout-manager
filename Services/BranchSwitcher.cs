using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    /// <summary>
    /// Branch switching on one repository folder, shared by <see cref="SubmoduleService"/> and
    /// <see cref="CheckoutService"/>. Nothing here may discard commits: no <c>checkout -B</c>, no
    /// <c>reset --hard</c>. A local branch that is not a fast-forward of origin is left exactly as it is.
    /// Callers check for a dirty working tree before calling.
    /// </summary>
    public static class BranchSwitcher
    {
        /// <summary><c>git ls-remote --heads origin</c> in <paramref name="folder"/>; throws with git's first message line.</summary>
        public static async Task<List<string>> ListRemoteBranchesAsync(
            IGitService git, string folder, GitAuth? auth, CancellationToken ct)
        {
            var result = await git.RunAsync(
                new[] { "-C", folder, "ls-remote", "--heads", "origin" }, null, auth, ct, allowInteractiveAuth: true);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(result.StdErr) ?? "git ls-remote failed.");

            const string prefix = "refs/heads/";
            return result.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().Split('\t'))
                .Where(parts => parts.Length == 2 && parts[1].StartsWith(prefix, StringComparison.Ordinal))
                .Select(parts => parts[1][prefix.Length..])
                .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// True when HEAD is detached and no branch or tag contains its commit, i.e. switching away would
        /// leave those commits reachable only through the reflog.
        /// </summary>
        public static async Task<bool> IsDetachedOnUnreferencedCommitAsync(
            IGitService git, string folder, CancellationToken ct)
        {
            var symbolic = await git.RunAsync(
                new[] { "-C", folder, "symbolic-ref", "--short", "-q", "HEAD" }, null, null, ct);
            if (symbolic.ExitCode == 0) return false;

            // A detached HEAD lists itself as "(HEAD detached at …)"; only real refs count.
            var contains = await git.RunAsync(
                new[] { "-C", folder, "branch", "-a", "--contains", "HEAD", "--format=%(refname)" }, null, null, ct);
            if (contains.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(contains.StdErr) ?? "git branch failed.");

            return !contains.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(l => l.Trim().StartsWith("refs/", StringComparison.Ordinal));
        }

        /// <summary>The one branch-name rule: not empty or whitespace, and not option-like. Callers check it before any git call.</summary>
        public static bool IsValidBranchName(string? branch) =>
            !string.IsNullOrWhiteSpace(branch) && !branch.StartsWith('-');

        /// <summary>
        /// True when one of the <c>remote.origin.fetch</c> refspecs (<c>[+]src:dst</c>) has a source that covers
        /// <c>refs/heads/&lt;branch&gt;</c>: an exact match or the <c>refs/heads/*</c> wildcard. A single-branch
        /// clone lists only its own branch, and then <c>checkout --track origin/&lt;b&gt;</c> can't set up tracking.
        /// </summary>
        public static bool FetchRefspecCovers(IEnumerable<string> refspecs, string branch)
        {
            var wanted = $"refs/heads/{branch}";
            foreach (var line in refspecs)
            {
                var spec = line.Trim().TrimStart('+');
                var colon = spec.IndexOf(':');
                var source = colon >= 0 ? spec[..colon] : spec;
                if (source == wanted || source == "refs/heads/*") return true;
            }
            return false;
        }

        /// <summary>
        /// Fetches <paramref name="branch"/> and checks it out. <paramref name="fetchCt"/> only covers the
        /// fetch (and its probe); once checkout starts it runs under <paramref name="ct"/>, so callers that
        /// want "cancel during the fetch only" pass <see cref="CancellationToken.None"/> there.
        /// <paramref name="interactiveCheckout"/>: a blobless checkout downloads blobs, so a user-triggered
        /// switch lets Git Credential Manager prompt; submodule switches keep it off.
        /// <paramref name="fetchFinished"/> is called once the fetch has ended (success or not), so a UI can stop offering Cancel.
        /// </summary>
        public static async Task<SwitchResult> SwitchAsync(
            IGitService git, string folder, string branch, GitAuth? auth,
            CancellationToken fetchCt, CancellationToken ct, bool interactiveCheckout = false, Action? fetchFinished = null)
        {
            if (!IsValidBranchName(branch))
                return new SwitchResult(1, string.Empty, $"'{branch}' is not a valid branch name.");

            // A single-branch clone only fetches its own branch; add this one to remote.origin.fetch so that
            // plain `git fetch` keeps origin/<branch> current and `--track` works. Local config only, no network.
            var configured = await git.RunAsync(
                new[] { "-C", folder, "config", "--get-all", "remote.origin.fetch" }, null, null, fetchCt);
            var refspecs = configured.ExitCode == 0
                ? configured.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();
            if (!FetchRefspecCovers(refspecs, branch))
            {
                var add = await git.RunAsync(
                    new[] { "-C", folder, "remote", "set-branches", "--add", "origin", branch }, null, null, fetchCt);
                if (add.ExitCode != 0) return SwitchResult.From(add);
            }

            // An explicit refspec makes sure origin/<branch> exists even in a single-branch clone.
            var fetch = await git.RunAsync(
                new[] { "-C", folder, "fetch", "origin", $"+refs/heads/{branch}:refs/remotes/origin/{branch}" }, null,
                auth, fetchCt, allowInteractiveAuth: true);
            fetchFinished?.Invoke();
            if (fetch.ExitCode != 0)
            {
                // Exit code 2 of ls-remote --exit-code = the remote answered and has no such branch.
                var probe = await git.RunAsync(
                    new[] { "-C", folder, "ls-remote", "--exit-code", "--heads", "origin", $"refs/heads/{branch}" }, null,
                    auth, fetchCt, allowInteractiveAuth: false);
                return SwitchResult.From(fetch, probe.ExitCode == 2 ? SwitchOutcome.BranchNotOnRemote : SwitchOutcome.None);
            }

            // A blobless checkout (and the fast-forward after it) downloads blobs from origin, so it needs the same URL-scoped auth as the fetch.
            var checkoutAuth = interactiveCheckout ? auth : null;

            var local = await git.RunAsync(
                new[] { "-C", folder, "rev-parse", "--verify", "-q", $"refs/heads/{branch}" }, null, null, ct);

            if (local.ExitCode != 0)
            {
                var create = await git.RunAsync(
                    new[] { "-C", folder, "checkout", "-b", branch, "--track", $"origin/{branch}" },
                    null, checkoutAuth, ct, interactiveCheckout);
                return create.ExitCode == 0 ? new SwitchResult(0, string.Empty, string.Empty) : SwitchResult.From(create);
            }

            var checkout = await git.RunAsync(
                new[] { "-C", folder, "checkout", branch, "--" }, null, checkoutAuth, ct, interactiveCheckout);
            if (checkout.ExitCode != 0) return SwitchResult.From(checkout);

            var merge = await git.RunAsync(
                new[] { "-C", folder, "merge", "--ff-only", $"origin/{branch}" }, null, checkoutAuth, ct, interactiveCheckout);
            if (merge.ExitCode == 0) return new SwitchResult(0, string.Empty, string.Empty);

            // Only a branch with commits of its own is "left as is"; any other merge failure is a real error.
            var behind = await git.RunAsync(
                new[] { "-C", folder, "merge-base", "--is-ancestor", "HEAD", $"origin/{branch}" }, null, null, ct);
            return behind.ExitCode == 1
                ? new SwitchResult(0, $"Local branch {branch} has commits that aren't on origin — left as is.", string.Empty,
                    SwitchOutcome.LeftAsIs)
                : SwitchResult.From(merge);
        }

        private static string? FirstLine(string text) => text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);
    }
}
