using GitCheckoutManager.Models;

namespace GitCheckoutManager.ViewModels
{
    /// <summary>What an in-place tree update changed.</summary>
    public readonly record struct TreeDiffResult(int Added, int Removed);

    /// <summary>
    /// UI-free in-place update of a loaded tree: removes entries that are gone and adds new ones, leaving every
    /// other node object alone, so the view keeps its scroll position, expanded folders and ticks.
    /// </summary>
    public static class TreeDiff
    {
        /// <summary>Above this many changes, and more than half the tree, a rebuild is cheaper than patching.</summary>
        public const int RebuildThreshold = 1000;

        /// <summary>
        /// Applies the difference between <paramref name="roots"/> and the new flat entry list.
        /// <paramref name="shownRoots"/> is the collection the view is bound to, when it is a separate copy of the roots.
        /// Returns null, changing nothing, when so much changed that the caller should rebuild the tree instead.
        /// </summary>
        public static TreeDiffResult? TryApply(
            List<TreeNodeViewModel> roots,
            IReadOnlyCollection<TreeNode> flat,
            Comparison<TreeNodeViewModel> order,
            IList<TreeNodeViewModel>? shownRoots = null)
        {
            var live = new Dictionary<string, TreeNodeViewModel>(StringComparer.Ordinal);
            var pending = new Stack<TreeNodeViewModel>(roots);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                live[node.FullPath] = node;
                foreach (var child in node.Children) pending.Push(child);
            }

            var fresh = new Dictionary<string, TreeNode>(StringComparer.Ordinal);
            foreach (var entry in flat) fresh[entry.Path] = entry;

            // A path whose kind changed (file/folder/submodule) counts as removed and added again.
            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (path, node) in live)
            {
                if (!fresh.TryGetValue(path, out var entry) ||
                    entry.IsFolder != node.IsFolder || entry.IsSubmodule != node.IsSubmodule)
                    removed.Add(path);
            }

            var added = fresh.Values
                .Where(e => !live.ContainsKey(e.Path) || removed.Contains(e.Path))
                .OrderBy(e => e.Path, StringComparer.Ordinal) // a folder sorts before everything inside it
                .ToList();

            var changes = removed.Count + added.Count;
            if (changes > RebuildThreshold && changes > live.Count / 2)
                return null;

            var touched = new HashSet<TreeNodeViewModel>();

            foreach (var path in removed)
            {
                // A removed folder takes its subtree with it; only the topmost removed node is detached.
                if (removed.Contains(ParentPath(path))) continue;

                var node = live[path];
                if (node.Parent == null)
                {
                    roots.Remove(node);
                    shownRoots?.Remove(node);
                }
                else
                {
                    node.Parent.Children.Remove(node);
                    touched.Add(node.Parent);
                }
            }
            foreach (var path in removed) live.Remove(path);

            foreach (var entry in added)
            {
                if (live.TryGetValue(ParentPath(entry.Path), out var parent))
                {
                    live[entry.Path] = parent.AddChildInOrder(entry, order);
                    touched.Add(parent);
                }
                else
                {
                    var node = new TreeNodeViewModel(entry);
                    InsertSorted(roots, node, order);
                    if (shownRoots != null) InsertSorted(shownRoots, node, order);
                    live[entry.Path] = node;
                }
            }

            foreach (var parent in touched)
                parent.RefreshCheckedAfterChildrenChanged();

            return new TreeDiffResult(added.Count, removed.Count);
        }

        /// <summary>Inserts before the first element that sorts after <paramref name="item"/>.</summary>
        internal static void InsertSorted(IList<TreeNodeViewModel> list, TreeNodeViewModel item, Comparison<TreeNodeViewModel> order)
        {
            var index = 0;
            while (index < list.Count && order(list[index], item) <= 0) index++;
            list.Insert(index, item);
        }

        private static string ParentPath(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? string.Empty : path[..slash];
        }
    }
}
