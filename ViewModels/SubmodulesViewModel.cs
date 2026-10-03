using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using GitCheckoutManager.Models;
using GitCheckoutManager.Services;

namespace GitCheckoutManager.ViewModels
{
    /// <summary>One line of the Submodules window.</summary>
    public partial class SubmoduleRowViewModel : ObservableObject
    {
        public SubmoduleRowViewModel(SubmoduleInfo info) => Info = info;

        public SubmoduleInfo Info { get; private set; }

        /// <summary>Swaps in a freshly read state (e.g. right after this row was initialized) and refreshes every bound value.</summary>
        public void UpdateInfo(SubmoduleInfo info)
        {
            Info = info;
            OnPropertyChanged(string.Empty);
        }

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
        [NotifyPropertyChangedFor(nameof(HasError), nameof(CanSelect), nameof(IsSelectable),
            nameof(DisplayStateText), nameof(DisplayBrushKey), nameof(ShowUnderlyingState))]
        private string _error = string.Empty;

        public bool HasError => !string.IsNullOrEmpty(Error);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayStateText), nameof(DisplayBrushKey), nameof(ShowUnderlyingState))]
        private bool _isWorking;

        /// <summary>Selected for the current run and waiting for its turn.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayStateText), nameof(DisplayBrushKey), nameof(ShowUnderlyingState))]
        private bool _isQueued;

        [ObservableProperty] private bool _isSelected;

        /// <summary>Set by the window while a run is active, so the checkbox can't change under it.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSelectable))]
        private bool _isLocked;

        /// <summary>
        /// Something can be done to this row: it is missing or off its pinned commit, or its last action
        /// failed (a nested submodule can fail while this one already sits on its pinned commit).
        /// </summary>
        public bool CanSelect => State switch
        {
            SubmoduleState.NotInitialized or SubmoduleState.DifferentCommit => true,
            SubmoduleState.Ready => HasError,
            _ => false
        };

        public bool IsSelectable => CanSelect && !IsLocked;

        public string? SelectToolTip => State switch
        {
            SubmoduleState.Ready => "Already on the commit the main repo expects. Nothing to do.",
            SubmoduleState.MissingFromGitmodules =>
                "No entry in .gitmodules, so there is no URL to clone from. Ask the repo maintainer to add it.",
            SubmoduleState.OutsideCheckout => "Not in your sparse checkout, so it is not on disk.",
            _ => null
        };

        public string DisplayStateText => IsWorking ? "Working…" : IsQueued ? "Queued" : HasError ? "Failed" : StateText;

        public string DisplayBrushKey => IsWorking ? "AccentBrush" : IsQueued ? "MutedTextBrush" : HasError ? "DangerBrush" : StateBrushKey;

        /// <summary>A failed row shows "Failed", so its real state moves to a second line.</summary>
        public bool ShowUnderlyingState => HasError && !IsWorking && !IsQueued;

        private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;
    }

    /// <summary>Backs the Submodules window: lists submodules and initializes the selected ones, one at a time.</summary>
    public partial class SubmodulesViewModel : ObservableObject
    {
        private const int MaxErrorLines = 6;

        private readonly ISubmoduleService _submoduleService;
        private readonly string _root;
        private readonly Func<string, GitAuth?> _resolveAuth;
        private readonly IDialogService _dialogService;
        private readonly AppSettings _settings;
        private readonly ISettingsService _settingsService;
        private readonly CancellationTokenSource _cts = new();
        private CancellationTokenSource? _runCts;
        private List<SubmoduleRowViewModel> _all = new();

        /// <summary>Last failure text per path. Survives the reload after a run, so failed rows keep their error.</summary>
        private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

        public SubmodulesViewModel(ISubmoduleService submoduleService, string root,
            Func<string, GitAuth?> resolveAuth, IDialogService dialogService,
            AppSettings settings, ISettingsService settingsService)
        {
            _submoduleService = submoduleService;
            _root = root;
            _resolveAuth = resolveAuth;
            _dialogService = dialogService;
            _settings = settings;
            _settingsService = settingsService;

            // Backing fields: restoring the saved choice must not write the settings file again.
            _latestFromBranch = settings.SubmoduleLatestFromBranch;
            _includeNested = settings.SubmoduleIncludeNested;
            _showOnlyProblems = settings.SubmodulesShowOnlyProblems;
        }

        public ObservableCollection<SubmoduleRowViewModel> Rows { get; } = new();

        /// <summary>True once an initialize run started while this window was open, so the caller knows the checkout may have changed.</summary>
        public bool HasInitialized { get; private set; }

        [ObservableProperty] private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIdle))]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyCanExecuteChangedFor(nameof(InitializeSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
        [NotifyCanExecuteChangedFor(nameof(SelectAllWithProblemsCommand))]
        private bool _isRunning;

        /// <summary>No run is active; the options, Refresh and Close are available.</summary>
        public bool IsIdle => !IsRunning;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLoadError))]
        private string _loadError = string.Empty;

        public bool HasLoadError => LoadError.Length > 0;

        [ObservableProperty] private bool _showOnlyProblems;
        [ObservableProperty] private bool _showOutsideCheckout;

        [ObservableProperty] private string _summaryText = "Loading submodules…";

        /// <summary>Outcome of the last run, e.g. "Initialized 2 of 3. 1 failed: external/broken-url".</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResultText))]
        private string _resultText = string.Empty;

        public bool HasResultText => ResultText.Length > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UsePinned))]
        private bool _latestFromBranch;

        /// <summary>Radio partner of <see cref="LatestFromBranch"/>; the other radio does the unsetting.</summary>
        public bool UsePinned
        {
            get => !LatestFromBranch;
            set { if (value) LatestFromBranch = false; }
        }

        [ObservableProperty] private bool _includeNested;

        partial void OnLatestFromBranchChanged(bool value)
        {
            _settings.SubmoduleLatestFromBranch = value;
            _settingsService.SaveSettings(_settings);
        }

        partial void OnIncludeNestedChanged(bool value)
        {
            _settings.SubmoduleIncludeNested = value;
            _settingsService.SaveSettings(_settings);
        }

        /// <summary>Shown instead of the list when no row passes the filters.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasEmptyText))]
        private string _emptyText = string.Empty;

        public bool HasEmptyText => EmptyText.Length > 0;

        partial void OnShowOnlyProblemsChanged(bool value)
        {
            _settings.SubmodulesShowOnlyProblems = value;
            _settingsService.SaveSettings(_settings);
            ApplyFilter();
        }

        partial void OnShowOutsideCheckoutChanged(bool value) => ApplyFilter();

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        public Task RefreshAsync()
        {
            // A manual refresh starts clean; only the reload after a run keeps its error texts.
            _errors.Clear();
            ResultText = string.Empty;
            return LoadAsync();
        }

        private bool CanRefresh() => !IsRunning;

        private async Task LoadAsync()
        {
            IsBusy = true;
            LoadError = string.Empty;
            SummaryText = "Loading submodules…";

            try
            {
                var list = await _submoduleService.ListAsync(_root, _cts.Token);
                _all = list.Select(CreateRow).ToList();
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

        private SubmoduleRowViewModel CreateRow(SubmoduleInfo info)
        {
            var row = new SubmoduleRowViewModel(info) { IsLocked = IsRunning };
            if (_errors.TryGetValue(info.Path, out var error)) row.Error = error;

            row.PropertyChanged += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.PropertyName) ||
                    e.PropertyName == nameof(SubmoduleRowViewModel.IsSelected) ||
                    e.PropertyName == nameof(SubmoduleRowViewModel.CanSelect))
                    InitializeSelectedCommand.NotifyCanExecuteChanged();
            };
            return row;
        }

        // ── Actions ───────────────────────────────────────────────────────────

        [RelayCommand(CanExecute = nameof(CanSelectAll))]
        private void SelectAllWithProblems()
        {
            foreach (var row in Rows.Where(r => r.CanSelect))
                row.IsSelected = true;
        }

        private bool CanSelectAll() => !IsRunning;

        private List<SubmoduleRowViewModel> SelectedRows() =>
            Rows.Where(r => r.IsSelected && r.CanSelect).ToList();

        private bool CanInitializeSelected() => !IsRunning && !IsBusy && SelectedRows().Count > 0;

        partial void OnIsBusyChanged(bool value) => InitializeSelectedCommand.NotifyCanExecuteChanged();

        /// <summary>
        /// Initializes the selected submodules strictly one after another, in list order. A failure is
        /// recorded on its row and never stops the others.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanInitializeSelected))]
        private async Task InitializeSelectedAsync()
        {
            var targets = SelectedRows();
            if (targets.Count == 0) return;

            var latest = LatestFromBranch;
            var nested = IncludeNested;

            if (latest && !_dialogService.ShowConfirmation(
                    "Latest from branch moves submodules away from the commit the main repo expects. " +
                    "The main repo will then show them as changed. Continue?",
                    "Latest from branch"))
                return;

            IsRunning = true;
            HasInitialized = true;
            ResultText = string.Empty;
            foreach (var row in _all) row.IsLocked = true;

            _runCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var ct = _runCts.Token;

            foreach (var queued in targets) queued.IsQueued = true;

            var succeeded = new List<string>();
            var failed = new List<string>();
            var cancelled = false;

            try
            {
                foreach (var row in targets)
                {
                    row.IsQueued = false;
                    row.IsWorking = true;
                    GitResult result;
                    try
                    {
                        result = await _submoduleService.InitAndUpdateAsync(_root, row.Info, latest, nested, _resolveAuth, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        result = new GitResult(-1, string.Empty, ex.Message);
                    }
                    finally
                    {
                        row.IsWorking = false;
                    }

                    if (result.ExitCode == 0)
                    {
                        row.Error = string.Empty;
                        _errors.Remove(row.Path);
                        succeeded.Add(row.Path);

                        // Show the real state now instead of waiting for the end-of-run reload.
                        try
                        {
                            var fresh = await _submoduleService.GetAsync(_root, row.Path, ct);
                            if (fresh != null) row.UpdateInfo(fresh);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            break;
                        }
                        catch
                        {
                            // The reload after the run will show it.
                        }
                    }
                    else
                    {
                        row.Error = _errors[row.Path] = TailOf(result);
                        failed.Add(row.Path);
                    }
                }

                // Rows that did not succeed keep their tick, so the user can simply run again.
                var stillSelected = targets.Select(r => r.Path).Except(succeeded).ToHashSet(StringComparer.Ordinal);

                await LoadAsync();

                foreach (var row in Rows.Where(r => r.CanSelect && stillSelected.Contains(r.Path)))
                    row.IsSelected = true;

                ResultText = BuildResultText(succeeded.Count, targets.Count, failed, cancelled);
            }
            finally
            {
                // Rows that never started go back to their previous state.
                foreach (var queued in targets) queued.IsQueued = false;
                _runCts.Dispose();
                _runCts = null;
                foreach (var row in _all) row.IsLocked = false;
                IsRunning = false;
            }
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void CancelRun() => _runCts?.Cancel();

        private static string BuildResultText(int succeeded, int total, List<string> failed, bool cancelled)
        {
            var text = $"Initialized {succeeded} of {total}.";
            if (failed.Count > 0) text += $" {failed.Count} failed: {string.Join(", ", failed)}";
            if (cancelled) text += " Cancelled.";
            return text;
        }

        /// <summary>The last few non-empty lines of git's error output: where git says what went wrong.</summary>
        private static string TailOf(GitResult result)
        {
            var lines = (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr)
                .Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => l.Trim().Length > 0)
                .ToList();

            return lines.Count == 0
                ? $"git exited with code {result.ExitCode}."
                : string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Count - MaxErrorLines)));
        }

        /// <summary>Stops any load or run still going; called when the window closes.</summary>
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
            InitializeSelectedCommand.NotifyCanExecuteChanged();

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
