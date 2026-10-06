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

        /// <summary>Remote branch names from <c>git ls-remote --heads origin</c> (may prompt for credentials: user-triggered). Throws with a readable message on failure.</summary>
        Task<List<string>> ListRemoteBranchesAsync(string root, GitAuth? auth, CancellationToken ct = default);

        /// <summary>True when HEAD is detached on a commit no branch or tag contains (switching away would strand it).</summary>
        Task<bool> IsDetachedOnUnreferencedCommitAsync(string root, CancellationToken ct = default);

        /// <summary>
        /// Fetches <paramref name="branch"/> from origin and checks it out; never discards commits (see <c>BranchSwitcher</c>).
        /// <paramref name="fetchCt"/> only covers the fetch; once checkout starts it always finishes.
        /// </summary>
        Task<SwitchResult> SwitchBranchAsync(string root, string branch, GitAuth? auth, CancellationToken fetchCt = default, Action? fetchFinished = null);

        /// <summary>Returns the full folder/file tree of the checked-out commit.</summary>
        Task<List<TreeNode>> GetTreeAsync(string root, CancellationToken ct = default);
    }
}
