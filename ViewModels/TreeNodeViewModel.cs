using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using GitSparseManager.Models;

namespace GitSparseManager.ViewModels
{
    public partial class TreeNodeViewModel : ObservableObject
    {
        private readonly TreeNodeViewModel? _parent;
        private bool _suppressPropagation;
        private bool _isLoaded = true;
        private Func<TreeNodeViewModel, Task>? _lazyLoader;

        [ObservableProperty] private bool? _isChecked = false;
        [ObservableProperty] private bool _isExpanded = false;
        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private bool _isLoadingChildren = false;

        public string Name { get; private set; } = string.Empty;
        public string FullPath { get; private set; } = string.Empty;
        public bool IsFolder { get; private set; }

        /// <summary>True for synthetic "Loading…" / "Error: …" nodes.</summary>
        public bool IsPlaceholder { get; }

        /// <summary>True when this folder was not fully scanned during the initial pass.</summary>
        public bool HasUnscannedChildren { get; }

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
            HasUnscannedChildren = model.HasUnscannedChildren;
            IsSubmodule = model.IsSubmodule;
            _parent  = parent;
        }

        // Private constructor used only for placeholder nodes.
        private TreeNodeViewModel(string placeholderName)
        {
            Name        = placeholderName;
            IsPlaceholder = true;
        }

        internal static TreeNodeViewModel CreateLoadingPlaceholder() => new("Loading\u2026");
        internal static TreeNodeViewModel CreateErrorPlaceholder(string msg) => new($"Error: {msg}");

        // ── Lazy-load API ─────────────────────────────────────────────────────

        /// <summary>
        /// Registers a lazy loader for this folder and adds a visible "Loading…" placeholder
        /// child so the TreeView renders the expand arrow.
        /// </summary>
        public void SetLazyLoader(Func<TreeNodeViewModel, Task> loader)
        {
            _lazyLoader = loader;
            _isLoaded   = false;
            Children.Add(CreateLoadingPlaceholder());
        }

        /// <summary>
        /// After lazy-loading completes, propagates this node's checked state to its new children.
        /// </summary>
        public void PropagateCheckedToNewChildren()
        {
            if (IsChecked != true) return;
            SetChildrenChecked(true);
        }

        /// <summary>Internal access so the lazy loader can mark this node as done.</summary>
        internal bool IsLoaded
        {
            get => _isLoaded;
            set => _isLoaded = value;
        }

        // ── IsExpanded → trigger lazy load ────────────────────────────────────

        partial void OnIsExpandedChanged(bool value)
        {
            // IsLoadingChildren acts as a concurrency guard (prevents double-load)
            if (value && IsFolder && !_isLoaded && !IsLoadingChildren && _lazyLoader != null)
            {
                IsLoadingChildren = true;
                _ = _lazyLoader(this);
            }
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
                if (!child.IsPlaceholder && !child.IsFolder)
                    child.OnPropertyChanged(nameof(IsIncluded));
            }
        }

        private void SetChildrenChecked(bool value)
        {
            foreach (var child in Children)
            {
                if (child.IsPlaceholder || !child.IsFolder) continue;
                child._suppressPropagation = true;
                child.IsChecked = value;
                child._suppressPropagation = false;
                child.SetChildrenChecked(value);
            }
        }

        private void RefreshCheckedFromChildren()
        {
            // A folder with no sub-folders carries its own state; its files follow it.
            var folders = Children.Where(c => !c.IsPlaceholder && c.IsFolder).ToList();
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
        /// A fully-checked folder returns only its own path – its entire subtree is implied,
        /// even if it has not been lazy-loaded yet. File paths are never returned: cone mode rejects them.
        /// </summary>
        public IEnumerable<string> GetCheckedPaths()
        {
            if (IsPlaceholder || !IsFolder || string.IsNullOrEmpty(FullPath)) yield break;

            if (IsChecked == true)
            {
                yield return FullPath;
            }
            else if (IsChecked == null) // indeterminate – recurse into loaded sub-folders only
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
            if (IsPlaceholder)
            {
                IsVisible = false; // hide placeholders while filtering
                return;
            }

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
                if (child.IsPlaceholder) continue;
                child.ApplyFilter(filter);
                if (child.IsVisible) anyChildVisible = true;
            }

            IsVisible = anyChildVisible ||
                        Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }
    }
}

