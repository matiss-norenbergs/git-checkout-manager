namespace GitCheckoutManager.Models
{
    /// <summary>Display groups of the Local changes window, in display order.</summary>
    public enum LocalChangeGroup
    {
        Conflicted,
        Staged,
        Modified,
        Deleted,
        Renamed,
        Untracked
    }

    /// <summary>
    /// One changed file (one row). <paramref name="Path"/> is repository-relative with forward slashes;
    /// <paramref name="OriginalPath"/> is the old path of a rename/copy. <paramref name="Hint"/> is extra text
    /// for the row (the Submodules-window hint for gitlinks).
    /// </summary>
    public sealed record LocalChange(string Path, string? OriginalPath, LocalChangeGroup Group, string Label,
        bool IsSubmodule, string? Hint = null);

    /// <summary>The parsed result of one <c>git status</c> run: one entry per file, ordered by group, then path.</summary>
    public sealed class LocalChangeSet
    {
        public static readonly LocalChangeSet Empty = new(Array.Empty<LocalChange>());

        public IReadOnlyList<LocalChange> Changes { get; }

        public LocalChangeSet(IEnumerable<LocalChange> changes) =>
            Changes = changes
                .OrderBy(c => c.Group)
                .ThenBy(c => c.Path, StringComparer.Ordinal)
                .ToList();

        public int Count => Changes.Count;

        /// <summary>
        /// Entries that block a branch switch: any tracked change (staged, modified, deleted, renamed, conflicted).
        /// Untracked files and submodule entries don't; git itself refuses the checkout when one would be overwritten.
        /// </summary>
        public IEnumerable<LocalChange> Blocking => Changes.Where(c => c.Group != LocalChangeGroup.Untracked && !c.IsSubmodule);

        public int BlockingCount => Blocking.Count();

        public int UntrackedCount => Changes.Count(c => c.Group == LocalChangeGroup.Untracked);

        public int SubmoduleCount => Changes.Count(c => c.IsSubmodule);

        public int CountOf(LocalChangeGroup group) => Changes.Count(c => c.Group == group);

        /// <summary>"12 files changed: 3 staged, 7 modified, 2 untracked" (only non-empty groups), or "No local changes".</summary>
        public string SummaryText
        {
            get
            {
                if (Count == 0) return "No local changes";

                var parts = Enum.GetValues<LocalChangeGroup>()
                    .Select(g => (Group: g, N: CountOf(g)))
                    .Where(x => x.N > 0)
                    .Select(x => $"{x.N} {x.Group.ToString().ToLowerInvariant()}");

                return $"{Count} {(Count == 1 ? "file" : "files")} changed: {string.Join(", ", parts)}";
            }
        }
    }
}
