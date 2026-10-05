using System.IO;
using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    /// <summary>
    /// Reads submodule state through plain git plumbing. <c>git submodule status</c> is avoided on
    /// purpose: it aborts at the first submodule that has no .gitmodules entry.
    /// </summary>
    public sealed class SubmoduleService : ISubmoduleService
    {
        private const string GitlinkMode = "160000";
        private const int MaxParallelGitCalls = 8;

        /// <summary>Deepest nesting level that is listed (top-level submodules are depth 0).</summary>
        private const int MaxDepth = 5;

        /// <summary>Local-config marker written next to an overridden URL, so the app can tell its own override from git's.</summary>
        private const string OverrideKey = "gcmUrlOverride";

        private readonly IGitService _gitService;

        public SubmoduleService(IGitService gitService) => _gitService = gitService;

        public async Task<List<SubmoduleInfo>> ListAsync(string root, CancellationToken ct = default)
        {
            using var gate = new SemaphoreSlim(MaxParallelGitCalls);
            return await ListRepoAsync(root, string.Empty, 0, gate, ct);
        }

        /// <summary>Lists one repository's submodules, each followed by the submodules found inside its populated folder.</summary>
        private async Task<List<SubmoduleInfo>> ListRepoAsync(
            string repoRoot, string displayPrefix, int depth, SemaphoreSlim gate, CancellationToken ct)
        {
            var pinned = await GetPinnedAsync(repoRoot, ct);
            if (pinned.Count == 0) return new List<SubmoduleInfo>();

            var byPath = await GetGitmodulesAsync(repoRoot, ct);

            var tasks = pinned.Select(async entry =>
            {
                await gate.WaitAsync(ct);
                try { return await BuildAsync(repoRoot, entry.Path, entry.Sha, byPath, displayPrefix, depth, ct); }
                finally { gate.Release(); }
            });

            var level = (await Task.WhenAll(tasks)).ToList();
            level.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

            // The gate is not held while recursing, so a deep tree cannot starve itself.
            var children = await Task.WhenAll(level.Select(sub =>
                depth < MaxDepth && IsPopulated(sub.State)
                    ? ListChildrenAsync(sub, gate, ct)
                    : Task.FromResult(new List<SubmoduleInfo>())));

            var flat = new List<SubmoduleInfo>(level.Count);
            for (var i = 0; i < level.Count; i++)
            {
                flat.Add(level[i]);
                flat.AddRange(children[i]);
            }
            return flat;
        }

        private async Task<List<SubmoduleInfo>> ListChildrenAsync(SubmoduleInfo parent, SemaphoreSlim gate, CancellationToken ct)
        {
            var folder = Path.Combine(parent.RepoRoot, parent.Path.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                return await ListRepoAsync(folder, parent.DisplayPath + "/", parent.Depth + 1, gate, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A folder that is not a readable repository simply has no children to show.
                return new List<SubmoduleInfo>();
            }
        }

        private static bool IsPopulated(SubmoduleState state) =>
            state is SubmoduleState.Ready or SubmoduleState.DifferentCommit or SubmoduleState.ManuallyCloned;

        public async Task<SubmoduleInfo?> GetAsync(string root, string path, CancellationToken ct = default,
            string displayPrefix = "", int depth = 0)
        {
            var pinned = await GetPinnedAsync(root, ct);
            var entry = pinned.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.Ordinal));
            if (entry.Path == null) return null;

            var byPath = await GetGitmodulesAsync(root, ct);
            return await BuildAsync(root, entry.Path, entry.Sha, byPath, displayPrefix, depth, ct);
        }

        public async Task<GitResult> InitAndUpdateAsync(string root, SubmoduleInfo sub, bool latestFromBranch,
            bool includeNested, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sub.Name))
                return new GitResult(1, string.Empty, "This submodule has no entry in .gitmodules, so there is no URL to clone from.");

            // Registers the submodule and turns a relative URL (../x.git) into an absolute one.
            var init = await _gitService.RunAsync(
                new[] { "submodule", "init", "--", sub.Path }, root, null, ct);
            if (init.ExitCode != 0) return init;

            // Credentials are scoped to the resolved URL, so they never reach another server.
            GitAuth? auth = null;
            var urlResult = await _gitService.RunAsync(
                new[] { "config", "--get", $"submodule.{sub.Name}.url" }, root, null, ct);
            var url = urlResult.ExitCode == 0 ? urlResult.StdOut.Trim() : string.Empty;
            if (url.Length > 0) auth = resolveAuth(url);

            var args = new List<string> { "submodule", "update" };
            if (latestFromBranch) args.Add("--remote");
            if (includeNested) args.Add("--recursive");
            args.Add("--");
            args.Add(sub.Path);

            return await _gitService.RunAsync(args, root, auth, ct, allowInteractiveAuth: true);
        }

        public Task<GitResult> TestUrlAsync(string url, GitAuth? auth, CancellationToken ct = default) =>
            _gitService.RunAsync(new[] { "ls-remote", "--heads", url }, null, auth, ct, allowInteractiveAuth: true);

        public async Task<GitResult> SetUrlAndInitAsync(string root, SubmoduleInfo sub, string newUrl,
            bool latestFromBranch, bool includeNested, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sub.Name))
                return new GitResult(1, string.Empty, "This submodule has no entry in .gitmodules, so there is no name to attach a URL to.");

            // Only the checkout's own .git/config is written. `submodule init` keeps a URL that is already
            // set there, so this one wins over .gitmodules.
            var url = await _gitService.RunAsync(
                new[] { "config", $"submodule.{sub.Name}.url", newUrl }, root, null, ct);
            if (url.ExitCode != 0) return url;

            var marker = await _gitService.RunAsync(
                new[] { "config", $"submodule.{sub.Name}.{OverrideKey}", "true" }, root, null, ct);
            if (marker.ExitCode != 0) return marker;

            return await InitAndUpdateAsync(root, sub, latestFromBranch, includeNested, resolveAuth, ct);
        }

        public async Task<GitResult> ResetUrlAsync(string root, SubmoduleInfo sub, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sub.Name)) return new GitResult(0, string.Empty, string.Empty);

            var unset = await _gitService.RunAsync(
                new[] { "config", "--unset", $"submodule.{sub.Name}.{OverrideKey}" }, root, null, ct);

            // Exit code 5 means the marker was not set, which is the state we want anyway.
            if (unset.ExitCode != 0 && unset.ExitCode != 5) return unset;

            // Writes the .gitmodules URL back into .git/config (and the submodule's own origin), so the
            // submodule stays registered. Unsetting the url instead would make git treat it as uninitialized.
            return await _gitService.RunAsync(new[] { "submodule", "sync", "--", sub.Path }, root, null, ct);
        }

        public async Task<GitResult> CloneManuallyAsync(string root, SubmoduleInfo sub, string url,
            Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
        {
            var folder = Path.Combine(root, sub.Path.Replace('/', Path.DirectorySeparatorChar));
            var existed = Directory.Exists(folder);

            if (File.Exists(folder) || (existed && Directory.EnumerateFileSystemEntries(folder).Any()))
                return new GitResult(1, string.Empty, $"The folder '{sub.Path}' is not empty, so nothing was cloned into it.");

            try
            {
                var clone = await _gitService.RunAsync(
                    new[] { "clone", "--no-checkout", "--", url, sub.Path }, root, resolveAuth(url), ct,
                    allowInteractiveAuth: true);
                if (clone.ExitCode != 0)
                {
                    RestoreEmptyFolder(folder, existed);
                    return clone;
                }

                var checkout = await _gitService.RunAsync(
                    new[] { "-C", folder, "checkout", "--detach", sub.PinnedSha }, null, null, ct);
                if (checkout.ExitCode != 0) RestoreEmptyFolder(folder, existed);
                return checkout;
            }
            catch
            {
                // Cancelled or crashed half way: leave the checkout as it was.
                RestoreEmptyFolder(folder, existed);
                throw;
            }
        }

        // ── Branch switching ──────────────────────────────────────────────────
        // Nothing here may discard commits: no `checkout -B`, no `reset --hard`. A dirty folder is refused
        // up front, and a local branch that is not a fast-forward of origin is left exactly as it is.

        private static string FolderOf(SubmoduleInfo sub) =>
            Path.Combine(sub.RepoRoot, sub.Path.Replace('/', Path.DirectorySeparatorChar));

        public async Task<List<string>> ListRemoteBranchesAsync(SubmoduleInfo sub,
            Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
        {
            var folder = FolderOf(sub);
            var result = await _gitService.RunAsync(
                new[] { "-C", folder, "ls-remote", "--heads", "origin" }, null,
                await AuthForOriginAsync(folder, resolveAuth, ct), ct, allowInteractiveAuth: true);

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

        /// <summary>Credentials scoped to the submodule's own origin URL, so they never reach another server.</summary>
        private async Task<GitAuth?> AuthForOriginAsync(string folder, Func<string, GitAuth?> resolveAuth, CancellationToken ct)
        {
            var origin = await _gitService.RunAsync(new[] { "-C", folder, "remote", "get-url", "origin" }, null, null, ct);
            var url = origin.ExitCode == 0 ? origin.StdOut.Trim() : string.Empty;
            return url.Length > 0 ? resolveAuth(url) : null;
        }

        public async Task<SwitchCheck> CheckSwitchSafetyAsync(SubmoduleInfo sub, CancellationToken ct = default)
        {
            var folder = FolderOf(sub);

            var status = await _gitService.RunAsync(new[] { "-C", folder, "status", "--porcelain" }, null, null, ct);
            if (status.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(status.StdErr) ?? "git status failed.");

            var dirty = status.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .ToList();

            var unreferenced = false;
            var symbolic = await _gitService.RunAsync(
                new[] { "-C", folder, "symbolic-ref", "--short", "-q", "HEAD" }, null, null, ct);
            var detached = symbolic.ExitCode != 0;

            if (detached && !string.Equals(sub.CurrentSha, sub.PinnedSha, StringComparison.OrdinalIgnoreCase))
            {
                // A detached HEAD lists itself as "(HEAD detached at …)"; only real refs count.
                var contains = await _gitService.RunAsync(
                    new[] { "-C", folder, "branch", "-a", "--contains", "HEAD", "--format=%(refname)" }, null, null, ct);
                if (contains.ExitCode != 0)
                    throw new InvalidOperationException(FirstLine(contains.StdErr) ?? "git branch failed.");

                unreferenced = !contains.StdOut
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Any(l => l.Trim().StartsWith("refs/", StringComparison.Ordinal));
            }

            return new SwitchCheck(dirty, unreferenced);
        }

        public async Task<SwitchResult> SwitchBranchAsync(SubmoduleInfo sub, string branch,
            Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-'))
                return new SwitchResult(1, string.Empty, $"'{branch}' is not a valid branch name.");

            var folder = FolderOf(sub);

            var auth = await AuthForOriginAsync(folder, resolveAuth, ct);

            // An explicit refspec makes sure origin/<branch> exists even in a single-branch clone.
            var fetch = await _gitService.RunAsync(
                new[] { "-C", folder, "fetch", "origin", $"+refs/heads/{branch}:refs/remotes/origin/{branch}" }, null,
                auth, ct, allowInteractiveAuth: true);
            if (fetch.ExitCode != 0)
            {
                // Exit code 2 of ls-remote --exit-code = the remote answered and has no such branch.
                var probe = await _gitService.RunAsync(
                    new[] { "-C", folder, "ls-remote", "--exit-code", "--heads", "origin", $"refs/heads/{branch}" }, null,
                    auth, ct, allowInteractiveAuth: true);
                return SwitchResult.From(fetch, probe.ExitCode == 2 ? SwitchOutcome.BranchNotOnRemote : SwitchOutcome.None);
            }

            var local = await _gitService.RunAsync(
                new[] { "-C", folder, "rev-parse", "--verify", "-q", $"refs/heads/{branch}" }, null, null, ct);

            if (local.ExitCode != 0)
            {
                var create = await _gitService.RunAsync(
                    new[] { "-C", folder, "checkout", "-b", branch, "--track", $"origin/{branch}" }, null, null, ct);
                return create.ExitCode == 0 ? new SwitchResult(0, string.Empty, string.Empty) : SwitchResult.From(create);
            }

            var checkout = await _gitService.RunAsync(new[] { "-C", folder, "checkout", branch, "--" }, null, null, ct);
            if (checkout.ExitCode != 0) return SwitchResult.From(checkout);

            var merge = await _gitService.RunAsync(
                new[] { "-C", folder, "merge", "--ff-only", $"origin/{branch}" }, null, null, ct);
            if (merge.ExitCode == 0) return new SwitchResult(0, string.Empty, string.Empty);

            // Only a branch with commits of its own is "left as is"; any other merge failure is a real error.
            var behind = await _gitService.RunAsync(
                new[] { "-C", folder, "merge-base", "--is-ancestor", "HEAD", $"origin/{branch}" }, null, null, ct);
            return behind.ExitCode == 1
                ? new SwitchResult(0, $"Local branch {branch} has commits that aren't on origin — left as is.", string.Empty,
                    SwitchOutcome.LeftAsIs)
                : SwitchResult.From(merge);
        }

        public async Task<GitResult> ResetToRecordedAsync(SubmoduleInfo sub, CancellationToken ct = default)
        {
            var check = await CheckSwitchSafetyAsync(sub, ct);
            if (check.IsDirty)
                return new GitResult(1, string.Empty,
                    $"Commit or discard the changes in {sub.DisplayPath} first ({check.DirtyFiles.Count} changed).");

            var result = await _gitService.RunAsync(
                new[] { "-C", FolderOf(sub), "checkout", "--detach", sub.PinnedSha }, null, null, ct);
            return result.ExitCode == 0 ? new GitResult(0, string.Empty, string.Empty) : result;
        }

        /// <summary>Removes whatever a failed clone left behind and puts the original (empty or absent) folder back.</summary>
        private static void RestoreEmptyFolder(string folder, bool existed)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    // git marks object files read-only, which blocks deletion on Windows.
                    foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                        File.SetAttributes(f, FileAttributes.Normal);
                    Directory.Delete(folder, recursive: true);
                }
                if (existed) Directory.CreateDirectory(folder);
            }
            catch
            {
                // Best effort; the next Refresh shows what is really on disk.
            }
        }

        // ── Steps ─────────────────────────────────────────────────────────────

        /// <summary>Gitlink entries of the index. These are listed even when the sparse selection leaves them off disk.</summary>
        private async Task<List<(string Path, string Sha)>> GetPinnedAsync(string root, CancellationToken ct)
        {
            var result = await _gitService.RunAsync(new[] { "ls-files", "-s", "-z" }, root, null, ct);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(result.StdErr) ?? "git ls-files failed.");

            // A conflicted path appears once per stage; keep stage 0, or else the first stage seen.
            var found = new Dictionary<string, (string Sha, int Stage)>(StringComparer.Ordinal);

            foreach (var record in result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                // "<mode> <sha> <stage>\t<path>"
                var tab = record.IndexOf('\t');
                if (tab < 0) continue;

                var fields = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 3 || fields[0] != GitlinkMode) continue;

                var path = record[(tab + 1)..];
                var stage = int.TryParse(fields[2], out var s) ? s : 0;

                if (!found.TryGetValue(path, out var existing) || (stage == 0 && existing.Stage != 0))
                    found[path] = (fields[1], stage);
            }

            return found.Select(kv => (kv.Key, kv.Value.Sha)).ToList();
        }

        /// <summary>Reads .gitmodules and indexes its entries by submodule path.</summary>
        private async Task<Dictionary<string, (string Name, ModuleConfig Config)>> GetGitmodulesAsync(
            string root, CancellationToken ct)
        {
            var byPath = new Dictionary<string, (string, ModuleConfig)>(StringComparer.Ordinal);
            if (!File.Exists(Path.Combine(root, ".gitmodules"))) return byPath;

            var result = await _gitService.RunAsync(
                new[] { "config", "-f", ".gitmodules", "-z", "--get-regexp", @"^submodule\." }, root, null, ct);

            // Exit code 1 only means "no matching keys", which is a valid empty file.
            if (result.ExitCode > 1)
                throw new InvalidOperationException(FirstLine(result.StdErr) ?? "Could not read .gitmodules.");

            return GitmodulesParser.ParseByPath(result.StdOut);
        }

        private async Task<SubmoduleInfo> BuildAsync(
            string root, string path, string pinnedSha,
            Dictionary<string, (string Name, ModuleConfig Config)> byPath,
            string displayPrefix, int depth, CancellationToken ct)
        {
            var displayPath = displayPrefix + path;
            byPath.TryGetValue(path, out var entry);
            var name = entry.Config == null ? null : entry.Name;
            var url = entry.Config?.Url;
            var branch = string.IsNullOrWhiteSpace(entry.Config?.Branch) ? null : entry.Config!.Branch;

            var folder = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(folder))
                return new SubmoduleInfo(path, name, url, branch, pinnedSha, null, SubmoduleState.OutsideCheckout,
                    RepoRoot: root, DisplayPath: displayPath, Depth: depth);

            var overridden = false;
            string? effectiveUrl = null;
            if (name == null)
            {
                // Cloned by hand: show where it came from.
                var origin = await _gitService.RunAsync(
                    new[] { "-C", folder, "remote", "get-url", "origin" }, null, null, ct);
                if (origin.ExitCode == 0 && origin.StdOut.Trim().Length > 0) effectiveUrl = origin.StdOut.Trim();
            }
            else
            {
                var marker = await _gitService.RunAsync(
                    new[] { "config", "--local", "--get", $"submodule.{name}.{OverrideKey}" }, root, null, ct);
                overridden = marker.ExitCode == 0 &&
                    string.Equals(marker.StdOut.Trim(), "true", StringComparison.OrdinalIgnoreCase);

                var local = await _gitService.RunAsync(
                    new[] { "config", "--local", "--get", $"submodule.{name}.url" }, root, null, ct);
                if (local.ExitCode == 0 && local.StdOut.Trim().Length > 0) effectiveUrl = local.StdOut.Trim();
            }

            SubmoduleInfo Make(SubmoduleState state, string? current = null, string? currentBranch = null,
                string? originUrl = null) =>
                new(path, name, url, branch, pinnedSha, current, state, overridden, effectiveUrl, root, displayPath, depth,
                    currentBranch, originUrl);

            // A populated submodule has a ".git" file (gitdir pointer) or folder of its own.
            var dotGit = Path.Combine(folder, ".git");
            var populated = File.Exists(dotGit) || Directory.Exists(dotGit);
            var registered = entry.Config != null;

            if (!populated) return Make(registered ? SubmoduleState.NotInitialized : SubmoduleState.MissingFromGitmodules);

            var head = await _gitService.RunAsync(new[] { "-C", folder, "rev-parse", "HEAD" }, null, null, ct);
            var current = head.ExitCode == 0 ? head.StdOut.Trim() : string.Empty;

            // No readable HEAD means nothing usable is checked out there.
            if (current.Length == 0)
                return Make(registered ? SubmoduleState.NotInitialized : SubmoduleState.MissingFromGitmodules);

            // Exit code 1 means a detached HEAD.
            var symbolic = await _gitService.RunAsync(
                new[] { "-C", folder, "symbolic-ref", "--short", "-q", "HEAD" }, null, null, ct);
            var currentBranch = symbolic.ExitCode == 0 && symbolic.StdOut.Trim().Length > 0 ? symbolic.StdOut.Trim() : null;

            // Local only (no network): lets the window group rows by remote.
            var originResult = await _gitService.RunAsync(
                new[] { "-C", folder, "remote", "get-url", "origin" }, null, null, ct);
            var originUrl = originResult.ExitCode == 0 && originResult.StdOut.Trim().Length > 0
                ? originResult.StdOut.Trim()
                : null;

            if (!registered) return Make(SubmoduleState.ManuallyCloned, current, currentBranch, originUrl);

            return Make(
                string.Equals(current, pinnedSha, StringComparison.OrdinalIgnoreCase)
                    ? SubmoduleState.Ready
                    : SubmoduleState.DifferentCommit,
                current, currentBranch, originUrl);
        }

        private static string? FirstLine(string text) => text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);
    }
}
