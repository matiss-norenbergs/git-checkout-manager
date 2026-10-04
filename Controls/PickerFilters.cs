using GitCheckoutManager.Models;

namespace GitCheckoutManager.Controls
{
    /// <summary>Filter predicates for <see cref="SearchablePicker"/>: case-insensitive substring matches.</summary>
    public static class PickerFilters
    {
        public static bool Contains(string? text, string filter) =>
            string.IsNullOrEmpty(filter) ||
            (text?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

        /// <summary>Matches the repository name or its full path/namespace.</summary>
        public static bool MatchesRepository(object item, string filter) =>
            item is Repository r && (Contains(r.Name, filter) || Contains(r.PathWithNamespace, filter));

        public static readonly Func<object, string, bool> RepositoryPredicate = MatchesRepository;
        public static readonly Func<object, string, bool> BranchPredicate = MatchesBranch;

        public static bool MatchesBranch(object item, string filter) =>
            item is Branch b && Contains(b.Name, filter);
    }
}
