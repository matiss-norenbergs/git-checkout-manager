using System.IO;
using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    /// <summary>
    /// Reads an existing local checkout through plain git commands. No credentials are ever needed
    /// here: every command below works entirely on the local object store and working tree.
    /// </summary>
    public sealed class CheckoutService : ICheckoutService
    {
        private readonly IGitService _gitService;

        public CheckoutService(IGitService gitService) => _gitService = gitService;

        public async Task<CheckoutInfo> OpenAsync(string folder, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new InvalidOperationException("This folder is not inside a Git repository.");

            // --show-toplevel also accepts a sub-folder of the checkout and walks up to the root.
            var toplevel = await _gitService.RunAsync(
                new[] { "-C", folder, "rev-parse", "--show-toplevel" }, null, null, ct);

            if (toplevel.ExitCode != 0 || string.IsNullOrWhiteSpace(toplevel.StdOut))
                throw new InvalidOperationException("This folder is not inside a Git repository.");

            // git reports the root with forward slashes (C:/src/repo).
            var root = Path.GetFullPath(toplevel.StdOut.Trim().Replace('/', Path.DirectorySeparatorChar));

            var remoteUrl = await GetRemoteUrlAsync(root, ct);

            var branchResult = await RunAsync(root, ct, "branch", "--show-current");
            var branch = branchResult.ExitCode == 0 ? branchResult.StdOut.Trim() : string.Empty;

            var headResult = await RunAsync(root, ct, "rev-parse", "HEAD");
            var headSha = headResult.ExitCode == 0 ? headResult.StdOut.Trim() : string.Empty;

            var isSparse = await GetBoolConfigAsync(root, "core.sparseCheckout", ct);
            var isCone = await GetBoolConfigAsync(root, "core.sparseCheckoutCone", ct);

            var sparsePaths = isSparse ? await GetSparsePathsAsync(root, isCone, ct) : new List<string>();

            var status = await RunAsync(root, ct, "status", "--porcelain=v1", "-z");
            var changedFileCount = status.ExitCode == 0
                ? status.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries).Length
                : 0;

            return new CheckoutInfo(root, remoteUrl, string.IsNullOrEmpty(branch) ? null : branch,
                headSha, isSparse, isCone, sparsePaths, changedFileCount);
        }

        public async Task<List<TreeNode>> GetTreeAsync(string root, CancellationToken ct = default)
        {
            // Reading tree objects is purely local, even in a blobless clone: a blobless clone still
            // holds every folder listing, so this downloads nothing and needs no network. It also
            // matches exactly what is checked out, including unpushed local commits.
            var result = await RunAsync(root, ct, "ls-tree", "-r", "-t", "-z", "HEAD");

            if (result.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(result.StdErr) ?? "git ls-tree failed.");

            return GitTreeParser.Parse(result.StdOut);
        }

        // ── Steps ─────────────────────────────────────────────────────────────

        private async Task<string?> GetRemoteUrlAsync(string root, CancellationToken ct)
        {
            var origin = await RunAsync(root, ct, "remote", "get-url", "origin");
            if (origin.ExitCode == 0 && !string.IsNullOrWhiteSpace(origin.StdOut))
                return origin.StdOut.Trim();

            var remotes = await RunAsync(root, ct, "remote");
            if (remotes.ExitCode != 0) return null;

            var first = remotes.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);

            if (first == null) return null;

            var url = await RunAsync(root, ct, "remote", "get-url", first);
            return url.ExitCode == 0 && !string.IsNullOrWhiteSpace(url.StdOut) ? url.StdOut.Trim() : null;
        }

        private async Task<bool> GetBoolConfigAsync(string root, string key, CancellationToken ct)
        {
            var result = await RunAsync(root, ct, "config", "--bool", key);
            return result.ExitCode == 0 &&
                   string.Equals(result.StdOut.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<List<string>> GetSparsePathsAsync(string root, bool isCone, CancellationToken ct)
        {
            var result = await RunAsync(root, ct, "sparse-checkout", "list");
            if (result.ExitCode != 0) return new List<string>();

            var lines = result.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0);

            // Cone entries are plain directories; strip the slashes so they match tree paths.
            if (isCone)
                lines = lines.Select(l => l.Trim('/')).Where(l => l.Length > 0);

            return lines.ToList();
        }

        private Task<GitResult> RunAsync(string root, CancellationToken ct, params string[] args)
        {
            var full = new List<string> { "-C", root };
            full.AddRange(args);
            return _gitService.RunAsync(full, null, null, ct);
        }

        private static string? FirstLine(string text) => text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);
    }
}
