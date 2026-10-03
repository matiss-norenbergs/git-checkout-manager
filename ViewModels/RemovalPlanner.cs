namespace GitSparseManager.ViewModels
{
    /// <summary>The outcome of comparing the selected folders with what a checkout currently has.</summary>
    /// <param name="Added">Selected folders not covered by the baseline.</param>
    /// <param name="Removed">Baseline folders no longer covered by the selection.</param>
    /// <param name="Targets">Folders that really leave the worktree.</param>
    /// <param name="Dropped">Targets discarded because they are, or contain, a selected path.</param>
    public sealed record RemovalPlan(
        List<string> Added, List<string> Removed, List<string> Targets, List<string> Dropped);

    /// <summary>Works out which folders a Manage selection adds and removes.</summary>
    public static class RemovalPlanner
    {
        public static RemovalPlan Plan(
            IReadOnlyList<string> baseline, IReadOnlyList<string> selected, IReadOnlyList<TreeNodeViewModel> tree)
        {
            var added = selected.Where(p => !IsCoveredBy(baseline, p)).ToList();
            var removed = baseline.Where(p => !IsCoveredBy(selected, p)).ToList();

            // Safety net: a kept folder must never be a removal target.
            var expanded = ExpandRemovalTargets(removed, selected, tree);
            var dropped = expanded.Where(t => ContainsOrEquals(t, selected)).ToList();
            var targets = expanded.Where(t => !ContainsOrEquals(t, selected)).ToList();

            return new RemovalPlan(added, removed, targets, dropped);
        }

        /// <summary>
        /// Narrows removed folders to what actually leaves the worktree. A removed folder with no selected
        /// path under it is a target itself; otherwise only its sibling folders off the path to the selection
        /// are (its direct files stay, as cone mode keeps them). Selected paths are never targets.
        /// </summary>
        public static List<string> ExpandRemovalTargets(
            IEnumerable<string> removedPaths, IReadOnlyList<string> selectedPaths,
            IReadOnlyList<TreeNodeViewModel> tree)
        {
            var targets = new List<string>();

            foreach (var removed in removedPaths)
            {
                if (!HasSelectedUnder(removed, selectedPaths))
                {
                    targets.Add(removed);
                    continue;
                }

                var node = FindNode(tree, removed);
                if (node != null) CollectTargets(node, selectedPaths, targets);
            }

            return targets;
        }

        /// <summary>True when <paramref name="path"/> equals an entry of <paramref name="set"/> or sits under one.</summary>
        public static bool IsCoveredBy(IEnumerable<string> set, string path) =>
            set.Any(s => string.Equals(s, path, StringComparison.OrdinalIgnoreCase) ||
                         path.StartsWith(s + "/", StringComparison.OrdinalIgnoreCase));

        /// <summary>True when <paramref name="target"/> equals a selected path or contains one.</summary>
        public static bool ContainsOrEquals(string target, IEnumerable<string> selected) =>
            selected.Any(s => string.Equals(s, target, StringComparison.OrdinalIgnoreCase) ||
                              s.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase));

        private static void CollectTargets(TreeNodeViewModel node, IReadOnlyList<string> selected, List<string> targets)
        {
            foreach (var child in node.Children.Where(c => c.IsFolder))
            {
                if (selected.Any(s => string.Equals(s, child.FullPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (HasSelectedUnder(child.FullPath, selected))
                    CollectTargets(child, selected, targets);
                else
                    targets.Add(child.FullPath);
            }
        }

        private static bool HasSelectedUnder(string folder, IEnumerable<string> selected) =>
            selected.Any(s => s.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));

        private static TreeNodeViewModel? FindNode(IEnumerable<TreeNodeViewModel> nodes, string path)
        {
            foreach (var node in nodes)
            {
                if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase)) return node;
                if (node.IsFolder && path.StartsWith(node.FullPath + "/", StringComparison.OrdinalIgnoreCase))
                    return FindNode(node.Children, path);
            }

            return null;
        }
    }
}
