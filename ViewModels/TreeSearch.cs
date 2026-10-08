namespace GitCheckoutManager.ViewModels
{
    /// <summary>Outcome of applying a search filter to a tree.</summary>
    /// <param name="MatchCount">Nodes whose own name (or path) matched the filter.</param>
    /// <param name="Expanded">False when auto-expansion was skipped because there were too many matches.</param>
    public readonly record struct TreeSearchResult(int MatchCount, bool Expanded);

    /// <summary>
    /// UI-free search logic for the checkout tree: matching, visibility, ancestor auto-expansion and
    /// expansion snapshot/restore. Never touches checked state. Nodes hidden by the checked-out filter
    /// (<see cref="TreeNodeViewModel.IsVisibleInFilter"/> false) are treated as absent: never matched, counted or expanded to.
    /// </summary>
    public static class TreeSearch
    {
        /// <summary>Above this many matches the filter still applies, but nothing is auto-expanded.</summary>
        public const int MaxAutoExpandMatches = 200;

        /// <summary>A filter containing "/" is matched against the full path instead of the name.</summary>
        public static bool IsPathQuery(string filter) => filter.Contains('/');

        /// <summary>Case-insensitive match of the filter against the node's name, or its full path for path queries.</summary>
        public static bool Matches(TreeNodeViewModel node, string filter)
            => !string.IsNullOrEmpty(filter) && MatchRange(node, filter) != null;

        /// <summary>
        /// The part of the node's <em>name</em> to highlight, or null when there is none. For path queries
        /// this is the overlap between the match in the full path and the name (the path's last segment).
        /// </summary>
        public static (int Start, int Length)? FindNameHighlight(TreeNodeViewModel node, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return null;

            if (!IsPathQuery(filter))
            {
                var i = node.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase);
                return i < 0 ? null : (i, filter.Length);
            }

            var range = MatchRange(node, filter);
            if (range == null) return null;

            var (text, matchStart) = (PathText(node), range.Value);
            var nameStart = text.Length - node.Name.Length;
            if (nameStart < 0 || !text.EndsWith(node.Name, StringComparison.Ordinal)) return null;

            var start = Math.Max(matchStart, nameStart);
            var end = Math.Min(matchStart + filter.Length, text.Length);
            return end > start ? (start - nameStart, end - start) : null;
        }

        /// <summary>
        /// Applies the filter: sets visibility and highlights, then (unless there are too many matches)
        /// expands every ancestor of every match. Matching nodes themselves are not expanded, and a matching
        /// folder keeps its whole subtree visible. Nodes are never collapsed here.
        /// </summary>
        public static TreeSearchResult Apply(IReadOnlyCollection<TreeNodeViewModel> roots, string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                Clear(roots);
                return new TreeSearchResult(0, true);
            }

            var matches = new List<TreeNodeViewModel>();
            foreach (var root in roots)
                Visit(root, filter, false, matches);

            var expand = matches.Count <= MaxAutoExpandMatches;
            if (expand)
            {
                foreach (var match in matches)
                {
                    for (var p = match.Parent; p != null; p = p.Parent)
                        p.IsExpanded = true;
                }
            }

            return new TreeSearchResult(matches.Count, expand);
        }

        /// <summary>Every node matching the filter, in tree (pre-)order. Empty for an empty filter.</summary>
        public static List<TreeNodeViewModel> FindMatches(IEnumerable<TreeNodeViewModel> roots, string filter)
        {
            var matches = new List<TreeNodeViewModel>();
            if (!string.IsNullOrEmpty(filter))
                Collect(roots, filter, matches);
            return matches;
        }

        /// <summary>
        /// The match to select after <paramref name="current"/> (-1 = none yet): wraps around at both ends,
        /// starts at the first match going forward and the last going backward, and is -1 when there are no matches.
        /// </summary>
        public static int NextIndex(int current, int count, bool forward)
        {
            if (count <= 0) return -1;
            if (current < 0 || current >= count) return forward ? 0 : count - 1;
            return forward ? (current + 1) % count : (current - 1 + count) % count;
        }

        private static void Collect(IEnumerable<TreeNodeViewModel> nodes, string filter, List<TreeNodeViewModel> matches)
        {
            foreach (var node in nodes)
            {
                // Hidden by the checked-out filter: treated as absent, subtree included.
                if (!node.IsVisibleInFilter) continue;

                if (Matches(node, filter)) matches.Add(node);
                Collect(node.Children, filter, matches);
            }
        }

        /// <summary>Shows every node and removes highlights.</summary>
        public static void Clear(IEnumerable<TreeNodeViewModel> roots)
        {
            foreach (var node in roots)
            {
                node.IsVisible = true;
                node.SetHighlight(null);
                Clear(node.Children);
            }
        }

        public static Dictionary<TreeNodeViewModel, bool> CaptureExpansion(IEnumerable<TreeNodeViewModel> roots)
        {
            var snapshot = new Dictionary<TreeNodeViewModel, bool>();
            Capture(roots, snapshot);
            return snapshot;
        }

        public static void RestoreExpansion(Dictionary<TreeNodeViewModel, bool> snapshot)
        {
            foreach (var (node, expanded) in snapshot)
                node.IsExpanded = expanded;
        }

        private static void Capture(IEnumerable<TreeNodeViewModel> nodes, Dictionary<TreeNodeViewModel, bool> snapshot)
        {
            foreach (var node in nodes)
            {
                snapshot[node] = node.IsExpanded;
                Capture(node.Children, snapshot);
            }
        }

        private static bool Visit(TreeNodeViewModel node, string filter, bool ancestorMatched, List<TreeNodeViewModel> matches)
        {
            if (!node.IsVisibleInFilter)
            {
                node.IsVisible = false;
                node.SetHighlight(null);
                return false;
            }

            var matched = MatchRange(node, filter) != null;
            if (matched) matches.Add(node);
            node.SetHighlight(FindNameHighlight(node, filter));

            var keepSubtree = ancestorMatched || matched;
            var anyChildVisible = false;
            foreach (var child in node.Children)
            {
                if (Visit(child, filter, keepSubtree, matches))
                    anyChildVisible = true;
            }

            node.IsVisible = keepSubtree || anyChildVisible;
            return node.IsVisible;
        }

        private static string PathText(TreeNodeViewModel node)
            => string.IsNullOrEmpty(node.FullPath) ? node.Name : node.FullPath;

        private static int? MatchRange(TreeNodeViewModel node, string filter)
        {
            var text = IsPathQuery(filter) ? PathText(node) : node.Name;
            var i = text.IndexOf(filter, StringComparison.OrdinalIgnoreCase);
            return i < 0 ? null : i;
        }
    }
}
