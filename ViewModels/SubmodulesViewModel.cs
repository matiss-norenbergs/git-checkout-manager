using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using GitSparseManager.Models;
using GitSparseManager.Services;

namespace GitSparseManager.ViewModels
{
    /// <summary>One line of the Submodules window.</summary>
    public partial class SubmoduleRowViewModel : ObservableObject
    {
        public SubmoduleRowViewModel(SubmoduleInfo info) => Info = info;

        public SubmoduleInfo Info { get; }

        public string Path => Info.Path;
        public string? Name => Info.Name;
        public string? Url => Info.Url;
        public string? Branch => Info.Branch;
        public SubmoduleState State => Info.State;

        public string BranchText => Branch == null ? string.Empty : $"branch: {Branch}";

        public string PinnedShortSha => Short(Info.PinnedSha);
        public string CurrentShortSha => Info.CurrentSha == null ? "—" : Short(Info.CurrentSha);

        public string StateText => State switch
        {
            SubmoduleState.Ready => "Ready",
            SubmoduleState.DifferentCommit => "On a different commit",
            SubmoduleState.NotInitialized => "Not initialized",
            SubmoduleState.MissingFromGitmodules => "Missing from .gitmodules",
            SubmoduleState.OutsideCheckout => "Not in your checkout",
            _ => State.ToString()
        };

        public string? StateToolTip => State switch
        {
            SubmoduleState.DifferentCommit =>
                $"Checked out at {CurrentShortSha}, the main repo expects {PinnedShortSha}. " +
                "Normal after 'latest from branch'.",
            SubmoduleState.MissingFromGitmodules =>
                "This submodule is in the repository but has no entry in .gitmodules, so git doesn't know " +
                "its URL. Ask the repo maintainer to add it.",
            _ => null
        };

        /// <summary>Key of the theme brush the state text uses; resolved by the view so it follows the theme.</summary>
        public string StateBrushKey => State switch
        {
            SubmoduleState.Ready => "SuccessBrush",
            SubmoduleState.DifferentCommit => "AccentBrush",
            SubmoduleState.MissingFromGitmodules => "DangerBrush",
            _ => "MutedTextBrush"
        };

        /// <summary>Why the last action on this submodule failed. Empty until an action fills it.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string _error = string.Empty;

        public bool HasError => !string.IsNullOrEmpty(Error);

        private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;
    }

    /// <summary>Backs the read-only Submodules window.</summary>
    public partial class SubmodulesViewModel : ObservableObject
    {
        private readonly ISubmoduleService _submoduleService;
        private readonly string _root;
        private readonly CancellationTokenSource _cts = new();
        private List<SubmoduleRowViewModel> _all = new();

        public SubmodulesViewModel(ISubmoduleService submoduleService, string root)
        {
            _submoduleService = submoduleService;
            _root = root;
        }

        public ObservableCollection<SubmoduleRowViewModel> Rows { get; } = new();

        [ObservableProperty] private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLoadError))]
        private string _loadError = string.Empty;

        public bool HasLoadError => LoadError.Length > 0;

        [ObservableProperty] private bool _showOnlyProblems = true;
        [ObservableProperty] private bool _showOutsideCheckout;

        [ObservableProperty] private string _summaryText = "Loading submodules…";

        /// <summary>Shown instead of the list when no row passes the filters.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasEmptyText))]
        private string _emptyText = string.Empty;

        public bool HasEmptyText => EmptyText.Length > 0;

        partial void OnShowOnlyProblemsChanged(bool value) => ApplyFilter();
        partial void OnShowOutsideCheckoutChanged(bool value) => ApplyFilter();

        [RelayCommand]
        public async Task RefreshAsync()
        {
            IsBusy = true;
            LoadError = string.Empty;
            SummaryText = "Loading submodules…";

            try
            {
                var list = await _submoduleService.ListAsync(_root, _cts.Token);
                _all = list.Select(i => new SubmoduleRowViewModel(i)).ToList();
                ApplyFilter();
            }
            catch (OperationCanceledException)
            {
                // The window was closed while git was still running.
            }
            catch (Exception ex)
            {
                _all = new List<SubmoduleRowViewModel>();
                ApplyFilter();
                LoadError = $"Could not read submodules: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Stops any load still running; called when the window closes.</summary>
        public void Cancel() => _cts.Cancel();

        private bool IsVisible(SubmoduleRowViewModel row) => row.State switch
        {
            SubmoduleState.OutsideCheckout => ShowOutsideCheckout,
            SubmoduleState.Ready => !ShowOnlyProblems,
            _ => true
        };

        private void ApplyFilter()
        {
            Rows.Clear();
            foreach (var row in _all.Where(IsVisible))
                Rows.Add(row);

            SummaryText = BuildSummary();

            EmptyText = Rows.Count > 0 ? string.Empty
                : _all.Count == 0 ? "This checkout has no submodules."
                : "Nothing to show with the current filters.";
        }

        private string BuildSummary()
        {
            if (_all.Count == 0) return "No submodules.";

            int Count(SubmoduleState s) => _all.Count(r => r.State == s);

            var outside = Count(SubmoduleState.OutsideCheckout);
            var inCheckout = _all.Count - outside;

            var parts = new List<string>();
            void Add(SubmoduleState s, string label)
            {
                var n = Count(s);
                if (n > 0) parts.Add($"{n} {label}");
            }

            Add(SubmoduleState.Ready, "ready");
            Add(SubmoduleState.DifferentCommit, "on a different commit");
            Add(SubmoduleState.NotInitialized, "not initialized");
            Add(SubmoduleState.MissingFromGitmodules, "missing from .gitmodules");

            var text = inCheckout == 0
                ? "No submodules in your checkout"
                : $"{inCheckout} submodule{(inCheckout == 1 ? "" : "s")} in your checkout: {string.Join(", ", parts)}";

            return outside > 0 ? $"{text} ({outside} not in your checkout)" : text;
        }
    }
}
