namespace GitCheckoutManager.Models
{
    /// <summary>
    /// What git reports inside the folders a Manage apply is about to remove, grouped so the user can
    /// decide per group whether those files may be deleted from disk.
    /// </summary>
    public sealed class RemovalReviewModel
    {
        public required IReadOnlyList<string> RemovedFolders { get; init; }

        /// <summary>Files matched by .gitignore, such as build output or node_modules.</summary>
        public required IReadOnlyList<string> IgnoredFiles { get; init; }

        public required IReadOnlyList<string> UntrackedFiles { get; init; }

        /// <summary>Modified, staged, deleted or renamed files.</summary>
        public required IReadOnlyList<string> ChangedFiles { get; init; }

        public bool HasAnyFiles =>
            IgnoredFiles.Count > 0 || UntrackedFiles.Count > 0 || ChangedFiles.Count > 0;
    }

    /// <summary>The per-group deletion choices made in the removal review dialog.</summary>
    public sealed record RemovalReviewChoices(bool DeleteIgnored, bool DeleteUntracked, bool DeleteChanged);
}
