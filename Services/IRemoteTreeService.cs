using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    public sealed record RemoteTreeResult(string CommitSha, List<TreeNode> Nodes, int SubmoduleCount)
    {
        /// <summary>True when the server refused the blob filter, so file contents were downloaded anyway.</summary>
        public bool FilterIgnored { get; init; }
    }

    public interface IRemoteTreeService
    {
        Task<RemoteTreeResult> GetTreeAsync(string repoUrl, string branch, GitAuth? auth,
                                            bool forceRefresh = false, CancellationToken ct = default);

        /// <summary>Total size on disk of the tree cache, in bytes.</summary>
        long GetCacheSizeBytes();

        void ClearCache();
    }
}
