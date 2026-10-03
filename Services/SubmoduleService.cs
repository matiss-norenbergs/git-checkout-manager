using System.IO;
using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    /// <summary>
    /// Reads submodule state through plain git plumbing. <c>git submodule status</c> is avoided on
    /// purpose: it aborts at the first submodule that has no .gitmodules entry.
    /// </summary>
    public sealed class SubmoduleService : ISubmoduleService
    {
        private const string GitlinkMode = "160000";
        private const int MaxParallelGitCalls = 8;

        private readonly IGitService _gitService;

        public SubmoduleService(IGitService gitService) => _gitService = gitService;

        public async Task<List<SubmoduleInfo>> ListAsync(string root, CancellationToken ct = default)
        {
            var pinned = await GetPinnedAsync(root, ct);
            if (pinned.Count == 0) return new List<SubmoduleInfo>();

            var byPath = await GetGitmodulesAsync(root, ct);

            using var gate = new SemaphoreSlim(MaxParallelGitCalls);
            var tasks = pinned.Select(async entry =>
            {
                await gate.WaitAsync(ct);
                try { return await BuildAsync(root, entry.Path, entry.Sha, byPath, ct); }
                finally { gate.Release(); }
            });

            var list = (await Task.WhenAll(tasks)).ToList();
            list.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public async Task<SubmoduleInfo?> GetAsync(string root, string path, CancellationToken ct = default)
        {
            var pinned = await GetPinnedAsync(root, ct);
            var entry = pinned.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.Ordinal));
            if (entry.Path == null) return null;

            var byPath = await GetGitmodulesAsync(root, ct);
            return await BuildAsync(root, entry.Path, entry.Sha, byPath, ct);
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
            Dictionary<string, (string Name, ModuleConfig Config)> byPath, CancellationToken ct)
        {
            byPath.TryGetValue(path, out var entry);
            var name = entry.Config == null ? null : entry.Name;
            var url = entry.Config?.Url;
            var branch = string.IsNullOrWhiteSpace(entry.Config?.Branch) ? null : entry.Config!.Branch;

            SubmoduleInfo Make(SubmoduleState state, string? current = null) =>
                new(path, name, url, branch, pinnedSha, current, state);

            var folder = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(folder)) return Make(SubmoduleState.OutsideCheckout);
            if (entry.Config == null) return Make(SubmoduleState.MissingFromGitmodules);

            // A populated submodule has a ".git" file (gitdir pointer) or folder of its own.
            var dotGit = Path.Combine(folder, ".git");
            if (!File.Exists(dotGit) && !Directory.Exists(dotGit)) return Make(SubmoduleState.NotInitialized);

            var head = await _gitService.RunAsync(new[] { "-C", folder, "rev-parse", "HEAD" }, null, null, ct);
            var current = head.ExitCode == 0 ? head.StdOut.Trim() : string.Empty;

            // No readable HEAD means nothing usable is checked out there.
            if (current.Length == 0) return Make(SubmoduleState.NotInitialized);

            return Make(
                string.Equals(current, pinnedSha, StringComparison.OrdinalIgnoreCase)
                    ? SubmoduleState.Ready
                    : SubmoduleState.DifferentCommit,
                current);
        }

        private static string? FirstLine(string text) => text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);
    }
}
