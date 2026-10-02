using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    /// <summary>
    /// Builds a repository tree from the remote without downloading file contents.
    /// It keeps a blobless, no-checkout partial clone per repository under
    /// %LOCALAPPDATA%\GitSparseManager\tree-cache\ and reads the tree objects from it.
    ///
    /// IMPORTANT: never run checkout, grep, diff, log -p, blame, or anything else that reads file
    /// contents inside the scan repo. It is a blobless partial clone, so any such command would make
    /// git start downloading blobs from the server — exactly what this service exists to avoid.
    /// </summary>
    public sealed class RemoteTreeService : IRemoteTreeService
    {
        private static readonly string CacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitSparseManager", "tree-cache");

        private const string FilterIgnoredMarker = "filtering not recognized by server";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        private readonly IGitService _gitService;

        // One gate per cache folder so two loads of the same repo never run git concurrently.
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
            new(StringComparer.OrdinalIgnoreCase);

        // "<cacheDir>|<sha>" → parsed nodes
        private readonly ConcurrentDictionary<string, RemoteTreeResult> _memoryCache =
            new(StringComparer.OrdinalIgnoreCase);

        public RemoteTreeService(IGitService gitService) => _gitService = gitService;

        public async Task<RemoteTreeResult> GetTreeAsync(string repoUrl, string branch, GitAuth? auth,
                                                         bool forceRefresh = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(repoUrl)) throw new ArgumentException("Repository URL is required.", nameof(repoUrl));
            if (string.IsNullOrWhiteSpace(branch)) throw new ArgumentException("Branch is required.", nameof(branch));

            var cacheDir = GetCacheDir(repoUrl);
            var gate = _gates.GetOrAdd(cacheDir, _ => new SemaphoreSlim(1, 1));

            await gate.WaitAsync(ct);
            try
            {
                var repoDir = Path.Combine(cacheDir, "repo");
                var treesDir = Path.Combine(cacheDir, "trees");

                var tipSha = await GetRemoteTipShaAsync(repoUrl, branch, auth, ct);

                if (!forceRefresh)
                {
                    var cached = TryLoadCached(cacheDir, treesDir, tipSha);
                    if (cached != null) return cached;
                }

                var (sha, filterIgnored) = await EnsureObjectsAsync(repoUrl, branch, repoDir, auth, ct);

                if (!forceRefresh && !string.Equals(sha, tipSha, StringComparison.OrdinalIgnoreCase))
                {
                    var cached = TryLoadCached(cacheDir, treesDir, sha);
                    if (cached != null) return cached with { FilterIgnored = filterIgnored };
                }

                var nodes = await ReadTreeAsync(repoDir, sha, auth, ct);
                var submodules = nodes.Count(n => n.IsSubmodule);

                SaveToDisk(treesDir, sha, nodes);

                var result = new RemoteTreeResult(sha, nodes, submodules) { FilterIgnored = filterIgnored };
                _memoryCache[MemoryKey(cacheDir, sha)] = result;
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public long GetCacheSizeBytes()
        {
            if (!Directory.Exists(CacheRoot)) return 0;
            try
            {
                return new DirectoryInfo(CacheRoot)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => { try { return f.Length; } catch { return 0L; } });
            }
            catch
            {
                return 0;
            }
        }

        public void ClearCache()
        {
            _memoryCache.Clear();
            if (!Directory.Exists(CacheRoot)) return;
            TryDeleteDirectory(CacheRoot);
        }

        // ── Steps ─────────────────────────────────────────────────────────────

        private async Task<string> GetRemoteTipShaAsync(string repoUrl, string branch, GitAuth? auth, CancellationToken ct)
        {
            var result = await _gitService.RunAsync(
                new[] { "ls-remote", repoUrl, $"refs/heads/{branch}" }, null, auth, ct);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(result.StdErr) ?? "git ls-remote failed.");

            var line = result.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);

            var sha = line?.Split('\t').FirstOrDefault();
            if (string.IsNullOrWhiteSpace(sha))
                throw new InvalidOperationException($"Branch '{branch}' was not found on the remote.");

            return sha;
        }

        /// <summary>Creates or updates the blobless scan clone and returns the commit sha it resolved to.</summary>
        private async Task<(string Sha, bool FilterIgnored)> EnsureObjectsAsync(
            string repoUrl, string branch, string repoDir, GitAuth? auth, CancellationToken ct)
        {
            // A clone cancelled part-way leaves .git without a HEAD; start over rather than trust it.
            if (!File.Exists(Path.Combine(repoDir, ".git", "HEAD")))
            {
                TryDeleteDirectory(repoDir);
                Directory.CreateDirectory(Path.GetDirectoryName(repoDir)!);

                GitResult clone;
                try
                {
                    clone = await _gitService.RunAsync(new[]
                    {
                        "clone", "--filter=blob:none", "--no-checkout", "--depth", "1",
                        "--branch", branch, repoUrl, repoDir
                    }, null, auth, ct);
                }
                catch
                {
                    TryDeleteDirectory(repoDir);
                    throw;
                }

                if (clone.ExitCode != 0)
                {
                    TryDeleteDirectory(repoDir);
                    throw new InvalidOperationException(FirstLine(clone.StdErr) ?? "git clone failed.");
                }

                var head = await _gitService.RunAsync(new[] { "-C", repoDir, "rev-parse", "HEAD" }, null, auth, ct);
                if (head.ExitCode != 0)
                    throw new InvalidOperationException(FirstLine(head.StdErr) ?? "git rev-parse HEAD failed.");

                return (head.StdOut.Trim(), WasFilterIgnored(clone.StdErr));
            }

            var fetch = await _gitService.RunAsync(new[]
            {
                "-C", repoDir, "fetch", "--depth", "1", "--filter=blob:none", "origin", $"refs/heads/{branch}"
            }, null, auth, ct);

            if (fetch.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(fetch.StdErr) ?? "git fetch failed.");

            var fetchHead = await _gitService.RunAsync(new[] { "-C", repoDir, "rev-parse", "FETCH_HEAD" }, null, auth, ct);
            if (fetchHead.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(fetchHead.StdErr) ?? "git rev-parse FETCH_HEAD failed.");

            return (fetchHead.StdOut.Trim(), WasFilterIgnored(fetch.StdErr));
        }

        private async Task<List<TreeNode>> ReadTreeAsync(string repoDir, string sha, GitAuth? auth, CancellationToken ct)
        {
            // -z keeps paths intact (no quoting/escaping). No --format, so older Git versions work too.
            var result = await _gitService.RunAsync(
                new[] { "-C", repoDir, "ls-tree", "-r", "-t", "-z", sha }, null, auth, ct);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(result.StdErr) ?? "git ls-tree failed.");

            return GitTreeParser.Parse(result.StdOut);
        }

        // ── Cache plumbing ────────────────────────────────────────────────────

        private static string GetCacheDir(string repoUrl)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(repoUrl.ToLowerInvariant()));
            var folder = Convert.ToHexString(hash)[..16].ToLowerInvariant();
            return Path.Combine(CacheRoot, folder);
        }

        private static string MemoryKey(string cacheDir, string sha) => $"{cacheDir}|{sha}";

        private RemoteTreeResult? TryLoadCached(string cacheDir, string treesDir, string sha)
        {
            if (_memoryCache.TryGetValue(MemoryKey(cacheDir, sha), out var inMemory))
                return inMemory;

            var file = Path.Combine(treesDir, $"{sha}.json");
            if (!File.Exists(file)) return null;

            try
            {
                var nodes = JsonSerializer.Deserialize<List<TreeNode>>(File.ReadAllText(file), JsonOptions);
                if (nodes == null) return null;

                var result = new RemoteTreeResult(sha, nodes, nodes.Count(n => n.IsSubmodule));
                _memoryCache[MemoryKey(cacheDir, sha)] = result;
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveToDisk(string treesDir, string sha, List<TreeNode> nodes)
        {
            try
            {
                Directory.CreateDirectory(treesDir);
                var file = Path.Combine(treesDir, $"{sha}.json");
                var tmp = file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(nodes, JsonOptions));
                File.Move(tmp, file, overwrite: true);
            }
            catch
            {
                // A cache miss next time is acceptable; never fail the load because of it.
            }
        }

        private static bool WasFilterIgnored(string stdErr) =>
            stdErr.Contains(FilterIgnoredMarker, StringComparison.OrdinalIgnoreCase);

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;

                // Git writes pack and object files read-only, which makes Directory.Delete fail part-way.
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }

                Directory.Delete(path, recursive: true);
            }
            catch { /* locked files — best effort */ }
        }

        private static string? FirstLine(string text) =>
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);
    }
}
