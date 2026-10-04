using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    public interface ISubmoduleService
    {
        /// <summary>
        /// Lists every submodule recorded in the checkout's tree with its state. Purely local: it never
        /// touches the network and never changes the checkout. Populated submodules are listed recursively
        /// (up to depth 5); the result is flat, in tree order (a parent followed by its children).
        /// </summary>
        Task<List<SubmoduleInfo>> ListAsync(string root, CancellationToken ct = default);

        /// <summary>
        /// Reads the state of a single submodule, or null when the path is no longer a submodule.
        /// <paramref name="root"/> is the repository that contains it (<see cref="SubmoduleInfo.RepoRoot"/>).
        /// </summary>
        /// <param name="displayPrefix">Where that repository sits in the checkout, with a trailing slash (<see cref="SubmoduleInfo.DisplayPrefix"/>).</param>
        /// <param name="depth">Nesting depth of the submodule (<see cref="SubmoduleInfo.Depth"/>).</param>
        Task<SubmoduleInfo?> GetAsync(string root, string path, CancellationToken ct = default,
            string displayPrefix = "", int depth = 0);

        /// <summary>
        /// Registers and populates one submodule (<paramref name="root"/> is its <see cref="SubmoduleInfo.RepoRoot"/>): <c>git submodule init</c>, then <c>update</c>. Needs the
        /// network and may open the Git Credential Manager login window, so only call it for an action
        /// the user started. A failure is returned, not thrown; cancellation throws.
        /// </summary>
        /// <param name="latestFromBranch">Pass <c>--remote</c>: use the branch tip instead of the commit the main repo records.</param>
        /// <param name="includeNested">Pass <c>--recursive</c>.</param>
        /// <param name="resolveAuth">Maps the submodule's resolved URL to saved credentials, or null. The header is scoped to that URL's host.</param>
        Task<GitResult> InitAndUpdateAsync(string root, SubmoduleInfo sub, bool latestFromBranch,
            bool includeNested, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default);

        /// <summary>Checks that a repository URL is reachable: <c>git ls-remote --heads</c>. Success is exit code 0.</summary>
        Task<GitResult> TestUrlAsync(string url, GitAuth? auth, CancellationToken ct = default);

        /// <summary>
        /// Points one submodule at another URL for this checkout only (local .git/config, marked with
        /// <c>gcmUrlOverride</c>), then initializes it. .gitmodules is never touched.
        /// </summary>
        Task<GitResult> SetUrlAndInitAsync(string root, SubmoduleInfo sub, string newUrl, bool latestFromBranch,
            bool includeNested, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default);

        /// <summary>Removes the local URL override so the row follows .gitmodules again.</summary>
        Task<GitResult> ResetUrlAsync(string root, SubmoduleInfo sub, CancellationToken ct = default);

        /// <summary>
        /// For a gitlink without a .gitmodules entry: clones <paramref name="url"/> into its (missing or empty)
        /// folder and detaches HEAD at the pinned commit. On failure the folder is left empty again.
        /// </summary>
        Task<GitResult> CloneManuallyAsync(string root, SubmoduleInfo sub, string url,
            Func<string, GitAuth?> resolveAuth, CancellationToken ct = default);

        /// <summary>Branch names on the submodule's <c>origin</c> (<c>ls-remote --heads</c>), sorted. Throws with git's first error line.</summary>
        Task<List<string>> ListRemoteBranchesAsync(SubmoduleInfo sub, Func<string, GitAuth?> resolveAuth,
            CancellationToken ct = default);

        /// <summary>Reads what a switch could lose: uncommitted changes (block) and a detached commit on no branch (warn).</summary>
        Task<SwitchCheck> CheckSwitchSafetyAsync(SubmoduleInfo sub, CancellationToken ct = default);

        /// <summary>
        /// Fetches <paramref name="branch"/> from origin and checks it out: a new tracking branch, or the existing
        /// local one fast-forwarded. A local branch with commits of its own stays as it is and the result's
        /// <c>StdOut</c> says so (exit code 0). Never discards commits. Call <see cref="CheckSwitchSafetyAsync"/> first.
        /// </summary>
        Task<GitResult> SwitchBranchAsync(SubmoduleInfo sub, string branch, Func<string, GitAuth?> resolveAuth,
            CancellationToken ct = default);

        /// <summary>Refuses a dirty folder, then detaches HEAD at the commit the main repo records.</summary>
        Task<GitResult> ResetToRecordedAsync(SubmoduleInfo sub, CancellationToken ct = default);
    }
}
