namespace GitCheckoutManager.Models
{
    public enum SubmoduleState
    {
        /// <summary>Populated, and on the commit the main repository records.</summary>
        Ready,
        /// <summary>Populated, but HEAD differs from the recorded commit.</summary>
        DifferentCommit,
        /// <summary>Its folder exists but holds no repository yet.</summary>
        NotInitialized,
        /// <summary>In the tree, but .gitmodules has no entry for it, so its URL is unknown.</summary>
        MissingFromGitmodules,
        /// <summary>No .gitmodules entry, but its folder holds a repository (cloned by hand or by this app).</summary>
        ManuallyCloned,
        /// <summary>Its folder is not on disk because the sparse selection leaves it out.</summary>
        OutsideCheckout
    }

    /// <summary>One submodule of a checkout, combining the tree, .gitmodules and the working folder.</summary>
    /// <param name="Path">Forward slashes, relative to <paramref name="RepoRoot"/>.</param>
    /// <param name="Name">From .gitmodules; null when missing.</param>
    /// <param name="Url">As written in .gitmodules (may be relative, e.g. ../lib.git).</param>
    /// <param name="Branch">submodule.&lt;name&gt;.branch from .gitmodules, if any.</param>
    /// <param name="PinnedSha">Commit recorded in the main repository.</param>
    /// <param name="CurrentSha">HEAD inside the submodule when populated.</param>
    /// <param name="UrlOverridden">The checkout's local config carries a URL set by this app (<c>gcmUrlOverride</c>).</param>
    /// <param name="EffectiveUrl">submodule.&lt;name&gt;.url from the local config, when set.</param>
    /// <param name="RepoRoot">Absolute path of the repository that contains this submodule: the checkout root for top-level ones, the parent submodule's folder for nested ones.</param>
    /// <param name="DisplayPath">Forward slashes, relative to the checkout root (e.g. <c>external/lib/vendor/x</c>).</param>
    /// <param name="Depth">0 for a top-level submodule, 1 for one inside it, and so on.</param>
    /// <param name="CurrentBranch">Local branch HEAD is on inside a populated submodule; null when detached (or not populated).</param>
    public sealed record SubmoduleInfo(
        string Path,
        string? Name,
        string? Url,
        string? Branch,
        string PinnedSha,
        string? CurrentSha,
        SubmoduleState State,
        bool UrlOverridden = false,
        string? EffectiveUrl = null,
        string RepoRoot = "",
        string DisplayPath = "",
        int Depth = 0,
        string? CurrentBranch = null)
    {
        /// <summary>The part of <see cref="DisplayPath"/> in front of <see cref="Path"/>, including its trailing slash; empty at depth 0.</summary>
        public string DisplayPrefix =>
            DisplayPath.Length > Path.Length && DisplayPath.EndsWith(Path, StringComparison.Ordinal)
                ? DisplayPath[..^Path.Length]
                : string.Empty;
    }
}
