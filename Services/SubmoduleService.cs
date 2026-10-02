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

        private sealed record ModuleConfig(string? Path, string? Url, string? Branch);

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

            const string prefix = "submodule.";
            var byName = new Dictionary<string, ModuleConfig>(StringComparer.Ordinal);

            foreach (var entry in result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                // "key\nvalue"; a key without a value has no newline.
                var nl = entry.IndexOf('\n');
                var key = nl < 0 ? entry : entry[..nl];
                var value = nl < 0 ? string.Empty : entry[(nl + 1)..];

                // The name may contain dots: strip the leading prefix, then split at the last dot.
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var rest = key[prefix.Length..];
                var dot = rest.LastIndexOf('.');
                if (dot <= 0) continue;

                var name = rest[..dot];
                var field = rest[(dot + 1)..];

                var cfg = byName.GetValueOrDefault(name) ?? new ModuleConfig(null, null, null);
                cfg = field.ToLowerInvariant() switch
                {
                    "path" => cfg with { Path = value },
                    "url" => cfg with { Url = value },
                    "branch" => cfg with { Branch = value },
                    _ => cfg
                };
                byName[name] = cfg;
            }

            foreach (var (name, cfg) in byName)
            {
                if (string.IsNullOrWhiteSpace(cfg.Path)) continue;
                var path = cfg.Path.Trim().Replace('\\', '/').TrimEnd('/');
                byPath.TryAdd(path, (name, new ModuleConfig(path, cfg.Url, cfg.Branch)));
            }

            return byPath;
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
