using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using GitSparseManager.Models;

namespace GitSparseManager.ViewModels
{
    public partial class TreeNodeViewModel : ObservableObject
    {
        private readonly TreeNodeViewModel? _parent;
        private bool _suppressPropagation;

        [ObservableProperty] private bool? _isChecked = false;
        [ObservableProperty] private bool _isExpanded = false;
        [ObservableProperty] private bool _isVisible = true;

        public string Name { get; private set; } = string.Empty;
        public string FullPath { get; private set; } = string.Empty;
        public bool IsFolder { get; private set; }

        /// <summary>True when this entry is a git submodule.</summary>
        public bool IsSubmodule { get; }

        /// <summary>
        /// Whether a file node ends up in the checkout. Cone mode has no per-file selection: it checks out
        /// every file sitting directly in a folder that is selected, or that has a selected sub-folder.
        /// </summary>
        public bool IsIncluded => _parent == null || _parent.IsChecked != false;

        public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

        public TreeNodeViewModel(TreeNode model, TreeNodeViewModel? parent = null)
        {
            Name     = model.Name;
            FullPath = model.Path;
            IsFolder = model.IsFolder;
            IsSubmodule = model.IsSubmodule;
            _parent  = parent;
        }

        // ── Three-state checkbox logic ────────────────────────────────────────

        /// <summary>
        /// Raised once per user-driven checkbox change (not for the cascade onto children/parents),
        /// so the owning view model can recompute derived state without walking the tree on every node.
        /// </summary>
        public static event Action? CheckedChanged;

        partial void OnIsCheckedChanged(bool? value)
        {
            // Runs even while propagation is suppressed: the files below still change inclusion.
            NotifyFileChildrenIncludedChanged();

            if (_suppressPropagation) return;

            if (value.HasValue)
                SetChildrenChecked(value.Value);

            _parent?.RefreshCheckedFromChildren();

            CheckedChanged?.Invoke();
        }

        private void NotifyFileChildrenIncludedChanged()
        {
            foreach (var child in Children)
            {
                if (!child.IsFolder)
                    child.OnPropertyChanged(nameof(IsIncluded));
            }
        }

        private void SetChildrenChecked(bool value)
        {
            foreach (var child in Children)
            {
                if (!child.IsFolder) continue;
                child._suppressPropagation = true;
                child.IsChecked = value;
                child._suppressPropagation = false;
                child.SetChildrenChecked(value);
            }
        }

        private void RefreshCheckedFromChildren()
        {
            // A folder with no sub-folders carries its own state; its files follow it.
            var folders = Children.Where(c => c.IsFolder).ToList();
            if (folders.Count == 0) return;

            var allChecked   = folders.All(c => c.IsChecked == true);
            var allUnchecked = folders.All(c => c.IsChecked == false);

            _suppressPropagation = true;
            IsChecked = allChecked ? true : allUnchecked ? false : (bool?)null;
            _suppressPropagation = false;

            _parent?.RefreshCheckedFromChildren();
        }

        // ── Path collection ───────────────────────────────────────────────────

        /// <summary>
        /// Yields the minimal set of folder paths that covers the selection (cone-mode friendly).
        /// A fully-checked folder returns only its own path – its entire subtree is implied.
        /// File paths are never returned: cone mode rejects them.
        /// </summary>
        public IEnumerable<string> GetCheckedPaths()
        {
            if (!IsFolder || string.IsNullOrEmpty(FullPath)) yield break;

            if (IsChecked == true)
            {
                yield return FullPath;
            }
            else if (IsChecked == null) // indeterminate – recurse into sub-folders
            {
                foreach (var child in Children)
                {
                    if (!child.IsFolder) continue;
                    foreach (var path in child.GetCheckedPaths())
                        yield return path;
                }
            }
        }

        // ── Filter ────────────────────────────────────────────────────────────

        public void ApplyFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                IsVisible = true;
                foreach (var child in Children)
                    child.ApplyFilter(filter);
                return;
            }

            var anyChildVisible = false;
            foreach (var child in Children)
            {
                child.ApplyFilter(filter);
                if (child.IsVisible) anyChildVisible = true;
            }

            IsVisible = anyChildVisible ||
                        Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }
    }
}
