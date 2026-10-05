using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    /// <summary>State of an existing local checkout, read straight from git.</summary>
    public sealed record CheckoutInfo(string Root, string? RemoteUrl, string? Branch, string HeadSha,
        bool IsSparse, bool IsCone, List<string> SparsePaths, LocalChangeSet LocalChanges)
    {
        /// <summary>Number of locally changed files; always the same number the Local changes window lists.</summary>
        public int ChangedFileCount => LocalChanges.Count;
    }

    public interface ICheckoutService
    {
        /// <summary>Resolves <paramref name="folder"/> to its repository root and reads the checkout state.</summary>
        Task<CheckoutInfo> OpenAsync(string folder, CancellationToken ct = default);

        /// <summary>Runs <c>git status --porcelain=v2</c> in <paramref name="root"/>. Throws when git fails.</summary>
        Task<LocalChangeSet> GetLocalChangesAsync(string root, CancellationToken ct = default);

        /// <summary>Returns the full folder/file tree of the checked-out commit.</summary>
        Task<List<TreeNode>> GetTreeAsync(string root, CancellationToken ct = default);
    }
}
