namespace GitSparseManager.Models
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
        /// <summary>Its folder is not on disk because the sparse selection leaves it out.</summary>
        OutsideCheckout
    }

    /// <summary>One submodule of a checkout, combining the tree, .gitmodules and the working folder.</summary>
    /// <param name="Path">Forward slashes, relative to the checkout root.</param>
    /// <param name="Name">From .gitmodules; null when missing.</param>
    /// <param name="Url">As written in .gitmodules (may be relative, e.g. ../lib.git).</param>
    /// <param name="Branch">submodule.&lt;name&gt;.branch from .gitmodules, if any.</param>
    /// <param name="PinnedSha">Commit recorded in the main repository.</param>
    /// <param name="CurrentSha">HEAD inside the submodule when populated.</param>
    public sealed record SubmoduleInfo(
        string Path,
        string? Name,
        string? Url,
        string? Branch,
        string PinnedSha,
        string? CurrentSha,
        SubmoduleState State);
}
