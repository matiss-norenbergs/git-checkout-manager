namespace GitCheckoutManager.ViewModels
{
    /// <summary>
    /// UI-free logic for the Manage tab's "Show only checked-out paths" filter. It only sets
    /// <see cref="TreeNodeViewModel.IsVisibleInFilter"/>; check state is never touched. "Checked out" is the
    /// sparse definition (baseline plus ticks), not disk state.
    /// </summary>
    public static class CheckedOutFilter
    {
        public const string FullCheckoutToolTip = "This checkout includes all files.";
        public const string NonConeToolTip = "Only available for cone-mode sparse checkouts.";

        /// <summary>The filter needs an open checkout that is sparse and in cone mode.</summary>
        public static bool IsAvailable(bool hasCheckout, bool isSparse, bool isCone)
            => hasCheckout && isSparse && isCone;

        /// <summary>Why the checkbox is disabled, or null when there is no reason to explain (available, or nothing open).</summary>
        public static string? UnavailableToolTip(bool hasCheckout, bool isSparse, bool isCone)
            => !hasCheckout ? null
             : !isSparse ? FullCheckoutToolTip
             : !isCone ? NonConeToolTip
             : null;

        /// <summary>
        /// Full re-evaluation in one pass. Off: everything is visible. On: a node is visible when it qualifies
        /// itself or has a visible descendant. A folder qualifies when it is (or sits under) a baseline folder, or is
        /// ticked or partial; a file qualifies when it is included (cone-mode rule) or its folder is in or under the baseline.
        /// </summary>
        public static void Evaluate(IEnumerable<TreeNodeViewModel> roots, bool enabled, IEnumerable<string> baselinePaths)
        {
            if (!enabled)
            {
                ShowAll(roots);
                return;
            }

            var baseline = new HashSet<string>(baselinePaths, StringComparer.Ordinal);
            foreach (var root in roots)
                Visit(root, baseline, false);
        }

        /// <summary>
        /// Grow-only update after <paramref name="changed"/> changed its check state: reveals what is now
        /// included (the node's subtree, its ancestors and the files directly in those ancestors). Never hides anything.
        /// </summary>
        public static void Grow(TreeNodeViewModel changed)
        {
            if (changed.IsChecked == false) return;

            RevealSubtree(changed);

            for (var p = changed.Parent; p != null; p = p.Parent)
            {
                p.IsVisibleInFilter = true;
                foreach (var child in p.Children)
                {
                    if (!child.IsFolder && child.IsIncluded)
                        child.IsVisibleInFilter = true;
                }
            }
        }

        /// <summary>True when no folder is visible at the top level, i.e. only root files (if any) are shown.</summary>
        public static bool IsEmpty(IEnumerable<TreeNodeViewModel> roots)
            => !roots.Any(r => r.IsFolder && r.IsVisibleInFilter);

        private static bool Visit(TreeNodeViewModel node, HashSet<string> baseline, bool underBaseline)
        {
            var inBaseline = underBaseline || (node.IsFolder && baseline.Contains(node.FullPath));
            // A file also stays while its folder is in or under the baseline: after an untick it is still on disk until apply.
            var visible = node.IsFolder ? inBaseline || node.IsChecked != false : node.IsIncluded || underBaseline;

            // No short-circuit: every descendant needs its own value.
            foreach (var child in node.Children)
            {
                if (Visit(child, baseline, inBaseline))
                    visible = true;
            }

            node.IsVisibleInFilter = visible;
            return visible;
        }

        private static void RevealSubtree(TreeNodeViewModel node)
        {
            node.IsVisibleInFilter = true;
            foreach (var child in node.Children)
            {
                if (child.IsFolder)
                {
                    if (child.IsChecked != false) RevealSubtree(child);
                }
                else if (child.IsIncluded)
                {
                    child.IsVisibleInFilter = true;
                }
            }
        }

        private static void ShowAll(IEnumerable<TreeNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                node.IsVisibleInFilter = true;
                ShowAll(node.Children);
            }
        }
    }
}
