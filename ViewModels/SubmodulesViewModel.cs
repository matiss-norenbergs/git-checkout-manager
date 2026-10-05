using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
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

        /// <summary>Path relative to the checkout root; differs from <see cref="Path"/> for nested submodules.</summary>
        public string DisplayPath => Info.DisplayPath.Length > 0 ? Info.DisplayPath : Info.Path;

        public int Depth => Info.Depth;

        private const double IndentPerLevel = 18;

        /// <summary>Margin of the path column: indents nested rows under their parent.</summary>
        public Thickness PathMargin => new(Depth * IndentPerLevel, 0, 12, 0);

        /// <summary>Margin of the error details, aligned with the path text.</summary>
        public Thickness ErrorMargin => new(Depth * IndentPerLevel, 6, 0, 0);
        public string? Name => Info.Name;
        public string? Url => Info.Url;

        /// <summary>The URL column: .gitmodules, or for a manual clone the origin it was cloned from.</summary>
        public string? DisplayUrl => State == SubmoduleState.ManuallyCloned ? Info.EffectiveUrl : Info.Url;
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
            SubmoduleState.ManuallyCloned => "Cloned manually",
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
            SubmoduleState.ManuallyCloned =>
                "Not a registered submodule — git submodule commands won't manage it. " +
                "Fix .gitmodules in the repository to make it a proper submodule.",
            _ => null
        };

        /// <summary>Key of the theme brush the state text uses; resolved by the view so it follows the theme.</summary>
        public string StateBrushKey => State switch
        {
            SubmoduleState.Ready => "SuccessBrush",
            SubmoduleState.DifferentCommit => "AccentBrush",
            SubmoduleState.ManuallyCloned => "AccentBrush",
            SubmoduleState.MissingFromGitmodules => "DangerBrush",
            _ => "MutedTextBrush"
        };

        public bool UrlOverridden => Info.UrlOverridden;
        public string OverrideText => $"URL overridden locally: {Info.EffectiveUrl}";
        public string OverrideToolTip => "Only this checkout uses this URL. .gitmodules still needs fixing in the repository.";

        /// <summary>Has a .gitmodules entry and is on disk, so its URL can be replaced for this checkout.</summary>
        public bool CanSetUrl => Info.Name != null && State != SubmoduleState.OutsideCheckout;
        public bool CanResetUrl => UrlOverridden;
        public bool CanCloneManually => State == SubmoduleState.MissingFromGitmodules;

        private bool IsPopulated => State is SubmoduleState.Ready or SubmoduleState.DifferentCommit or SubmoduleState.ManuallyCloned;

        public bool CanSwitchBranch => IsPopulated;

        /// <summary>Normalized origin URL of a populated row; rows with the same value share one remote.</summary>
        public string? RemoteKey => SubmodulesViewModel.NormalizeRemote(Info.OriginUrl);

        public bool CanResetToRecorded =>
            IsPopulated && !string.Equals(Info.CurrentSha, Info.PinnedSha, StringComparison.OrdinalIgnoreCase);

        public bool HasActions => CanSetUrl || CanResetUrl || CanCloneManually || CanSwitchBranch || CanResetToRecorded;

        /// <summary>"on main" or "detached"; empty for rows that are not populated.</summary>
        public string BranchLineText => !IsPopulated ? string.Empty
            : Info.CurrentBranch != null ? $"on {Info.CurrentBranch}" : "detached";

        public bool HasBranchLine => BranchLineText.Length > 0;

        public bool ShowMovedNote => State == SubmoduleState.DifferentCommit;

        public string MovedNoteText => "Moved from the recorded commit — the main repo will show this submodule as changed.";

        /// <summary>Why the last action on this submodule failed. Empty until an action fills it.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError), nameof(CanInitialize), nameof(CanSelect), nameof(IsSelectable),
            nameof(DisplayStateText), nameof(DisplayBrushKey), nameof(ShowUnderlyingState))]
        private string _error = string.Empty;

        public bool HasError => !string.IsNullOrEmpty(Error);

        /// <summary>Set when the last action left this row alone on purpose; the reason is then in <see cref="Error"/>.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayStateText), nameof(DisplayBrushKey))]
        private SubmoduleSkipReason _skip;

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
        [NotifyPropertyChangedFor(nameof(IsSelectable), nameof(ActionsEnabled))]
        private bool _isLocked;

        /// <summary>The row's action menu is usable only while no run is active.</summary>
        public bool ActionsEnabled => !IsLocked;

        /// <summary>
        /// Initialize can do something here: the row is missing or off its pinned commit, or its last action
        /// failed (a nested submodule can fail while this one already sits on its pinned commit).
        /// </summary>
        public bool CanInitialize => State switch
        {
            SubmoduleState.NotInitialized or SubmoduleState.DifferentCommit => true,
            SubmoduleState.Ready => HasError,
            _ => false
        };

        /// <summary>Populated, so a branch can be checked out in it.</summary>
        public bool CanSwitch => IsPopulated;

        /// <summary>Populated and on a branch, so that branch can be fast-forwarded.</summary>
        public bool CanPull => IsPopulated && Info.CurrentBranch != null;

        /// <summary>Some bulk action applies to this row, so its checkbox can be ticked.</summary>
        public bool CanSelect => CanInitialize || CanSwitch || CanPull;

        public bool IsSelectable => CanSelect && !IsLocked;

        public string? SelectToolTip => State switch
        {
            SubmoduleState.MissingFromGitmodules =>
                "No entry in .gitmodules, so there is no URL to clone from. Use the row menu to clone it manually, or ask the repo maintainer to add it.",
            SubmoduleState.OutsideCheckout => "Not in your sparse checkout, so it is not on disk.",
            SubmoduleState.ManuallyCloned =>
                "Cloned by hand, so git submodule commands don't manage it: Initialize skips it, Pull and Switch branch work.",
            _ when CanSwitch && !CanPull => "Not on a branch, so Pull skips it.",
            _ => null
        };

        public string DisplayStateText => IsWorking ? "Working…" : IsQueued ? "Queued" : HasError ? (Skip != SubmoduleSkipReason.None ? "Skipped" : "Failed") : StateText;

        public string DisplayBrushKey => IsWorking ? "AccentBrush" : IsQueued ? "MutedTextBrush" : HasError ? (Skip != SubmoduleSkipReason.None ? "MutedTextBrush" : "DangerBrush") : StateBrushKey;

        /// <summary>A failed or skipped row shows that word, so its real state moves to a second line.</summary>
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
        private readonly Func<IReadOnlyList<Repository>> _getRepositories;
        private readonly CheckoutInfo? _checkout;
        private readonly Action<string>? _copyToClipboard;
        private readonly CancellationTokenSource _cts = new();
        private CancellationTokenSource? _runCts;
        private List<SubmoduleRowViewModel> _all = new();

        /// <summary>Last failure text per path. Survives the reload after a run, so failed rows keep their error.</summary>
        private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SubmoduleSkipReason> _skips = new(StringComparer.Ordinal);

        private void SetRowError(SubmoduleRowViewModel row, string key, string text, SubmoduleSkipReason skip = SubmoduleSkipReason.None)
        {
            row.Skip = skip;
            row.Error = _errors[key] = text;
            if (skip == SubmoduleSkipReason.None) _skips.Remove(key);
            else _skips[key] = skip;
        }

        private void ClearRowError(SubmoduleRowViewModel row, string key)
        {
            row.Error = string.Empty;
            row.Skip = SubmoduleSkipReason.None;
            _errors.Remove(key);
            _skips.Remove(key);
        }

        public SubmodulesViewModel(ISubmoduleService submoduleService, string root,
            Func<string, GitAuth?> resolveAuth, IDialogService dialogService,
            AppSettings settings, ISettingsService settingsService,
            Func<IReadOnlyList<Repository>>? getRepositories = null,
            CheckoutInfo? checkout = null, Action<string>? copyToClipboard = null,
            bool forceShowOnlyProblems = false)
        {
            _checkout = checkout;
            _copyToClipboard = copyToClipboard;
            _getRepositories = getRepositories ?? (() => Array.Empty<Repository>());
            _submoduleService = submoduleService;
            _root = root;
            _resolveAuth = resolveAuth;
            _dialogService = dialogService;
            _settings = settings;
            _settingsService = settingsService;

            // Backing fields: restoring the saved choice must not write the settings file again.
            _target = settings.SubmoduleTarget
                ?? (settings.SubmoduleLatestFromBranch ? SubmoduleTarget.LatestFromBranch : SubmoduleTarget.Pinned);
            _includeNested = settings.SubmoduleIncludeNested;
            // Forced for this opening only: the backing field is set, so the saved preference is not touched.
            _showOnlyProblems = forceShowOnlyProblems || settings.SubmodulesShowOnlyProblems;
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
        [NotifyCanExecuteChangedFor(nameof(SetUrlCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResetUrlCommand))]
        [NotifyCanExecuteChangedFor(nameof(CloneManuallyCommand))]
        [NotifyCanExecuteChangedFor(nameof(SwitchBranchCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResetToRecordedCommand))]
        [NotifyCanExecuteChangedFor(nameof(PullSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(SwitchSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ResetSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(SelectAllWithRemoteCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
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

        /// <summary>The two radio buttons bind to this one property (via EnumToBoolConverter), so they cannot disagree.</summary>
        [ObservableProperty] private SubmoduleTarget _target;

        [ObservableProperty] private bool _includeNested;

        partial void OnTargetChanged(SubmoduleTarget value)
        {
            RefreshSelectionState();
            _settings.SubmoduleTarget = value;
            _settings.SubmoduleLatestFromBranch = value == SubmoduleTarget.LatestFromBranch;
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
            _skips.Clear();
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
            var key = info.DisplayPath.Length > 0 ? info.DisplayPath : info.Path;
            if (_skips.TryGetValue(key, out var skip)) row.Skip = skip;
            if (_errors.TryGetValue(key, out var error)) row.Error = error;

            row.PropertyChanged += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.PropertyName) ||
                    e.PropertyName == nameof(SubmoduleRowViewModel.IsSelected) ||
                    e.PropertyName == nameof(SubmoduleRowViewModel.CanSelect) ||
                    e.PropertyName == nameof(SubmoduleRowViewModel.CanInitialize) ||
                    e.PropertyName == nameof(SubmoduleRowViewModel.CanPull))
                    RefreshSelectionState();

                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SubmoduleRowViewModel.Error))
                    CopyReportCommand.NotifyCanExecuteChanged();
            };
            return row;
        }

        // ── Actions ───────────────────────────────────────────────────────────

        [RelayCommand(CanExecute = nameof(CanSelectAll))]
        private void SelectAllWithProblems()
        {
            foreach (var row in Rows.Where(r => r.CanInitialize))
                row.IsSelected = true;
        }

        private bool CanSelectAll() => !IsRunning;

        // -- Selection ---------------------------------------------------------

        /// <summary>Visible rows that are ticked and can be acted on.</summary>
        private List<SubmoduleRowViewModel> TickedRows() =>
            Rows.Where(r => r.IsSelected && r.CanSelect).ToList();

        /// <summary>
        /// Rows Initialize selected acts on: ticked rows it can do something for. With "Latest from branch" a
        /// ticked clean Ready row is updated to the branch tip too.
        /// </summary>
        private List<SubmoduleRowViewModel> SelectedRows()
        {
            var latest = Target == SubmoduleTarget.LatestFromBranch;
            return TickedRows()
                .Where(r => r.CanInitialize || (latest && r.State == SubmoduleState.Ready))
                .ToList();
        }

        /// <summary>Pull acts on the ticked populated rows, or on every visible one only when nothing is ticked.</summary>
        private List<SubmoduleRowViewModel> PullRows()
        {
            var ticked = TickedRows();
            return ticked.Count == 0
                ? Rows.Where(r => r.CanPull).ToList()
                : ticked.Where(r => r.CanSwitch).ToList();
        }

        private List<SubmoduleRowViewModel> SwitchRows() => TickedRows().Where(r => r.CanSwitch).ToList();

        private List<SubmoduleRowViewModel> ResetRows() => TickedRows().Where(r => r.CanResetToRecorded).ToList();

        /// <summary>"Pull all" only when nothing is ticked.</summary>
        private bool PullIsAll => TickedRows().Count == 0;

        public int SelectedCount => TickedRows().Count;
        public bool HasSelection => SelectedCount > 0;
        public string SelectedText => $"{SelectedCount} selected";

        public string PullButtonText
        {
            get
            {
                var n = PullRows().Count(r => r.CanPull);
                return PullIsAll ? $"Pull all ({n})" : $"Pull ({n})";
            }
        }

        public string InitializeButtonText => $"Initialize selected ({SelectedRows().Count})";

        public string SwitchButtonText => $"Switch branch… ({SwitchRows().Count})";

        public string PullToolTip
        {
            get
            {
                var text = "Fast-forward the branch each submodule is on. Never merges or rebases. " +
                           "Unlike “Latest from branch”, which detaches at the tip of the .gitmodules branch, " +
                           "this keeps each submodule on its own branch.";
                if (PullIsAll) return text + " Nothing ticked: pulls every submodule shown that is on a branch.";

                var ticked = TickedRows();
                if (!PullRows().Any(r => r.CanPull)) text += " None of the ticked rows can be pulled.";
                var detached = ticked.Count(r => r.CanSwitch && !r.CanPull);
                var ignored = ticked.Count(r => !r.CanSwitch);
                if (detached > 0) text += $" {detached} ticked not on a branch will be skipped.";
                if (ignored > 0) text += $" {ignored} ticked not on disk will be ignored.";
                return text;
            }
        }

        public string SwitchToolTip
        {
            get
            {
                var text = "Check out one remote branch in every ticked submodule, fast-forwarding it. " +
                           "Submodules with uncommitted changes are skipped.";
                var ignored = TickedRows().Count(r => !r.CanSwitch);
                return ignored > 0 ? text + $" {ignored} ticked not on disk will be ignored." : text;
            }
        }

        /// <summary>The remote shared by every ticked row that has one; null when they differ or none is ticked.</summary>
        private string? TickedRemote()
        {
            var keys = TickedRows().Select(r => r.RemoteKey).Where(k => k != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return keys.Count == 1 ? keys[0] : null;
        }

        /// <summary>Trailing slash and ".git" don't make a different remote.</summary>
        internal static string? NormalizeRemote(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var t = url.Trim().TrimEnd('/');
            if (t.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) t = t[..^4];
            return t.TrimEnd('/');
        }

        private void RefreshSelectionState()
        {
            InitializeSelectedCommand.NotifyCanExecuteChanged();
            PullSelectedCommand.NotifyCanExecuteChanged();
            SwitchSelectedCommand.NotifyCanExecuteChanged();
            ResetSelectedCommand.NotifyCanExecuteChanged();
            SelectAllWithRemoteCommand.NotifyCanExecuteChanged();
            ClearSelectionCommand.NotifyCanExecuteChanged();
            foreach (var name in new[]
            {
                nameof(SelectedCount), nameof(HasSelection), nameof(SelectedText), nameof(PullButtonText),
                nameof(SwitchButtonText), nameof(InitializeButtonText), nameof(PullToolTip), nameof(SwitchToolTip)
            })
                OnPropertyChanged(name);
        }

        [RelayCommand(CanExecute = nameof(CanSelectAll))]
        private void ClearSelection()
        {
            foreach (var row in _all) row.IsSelected = false;
        }

        private bool CanSelectAllWithRemote() => !IsRunning && TickedRemote() != null;

        [RelayCommand(CanExecute = nameof(CanSelectAllWithRemote))]
        private void SelectAllWithRemote()
        {
            var remote = TickedRemote();
            if (remote == null) return;
            foreach (var row in Rows.Where(r => r.CanSelect && string.Equals(r.RemoteKey, remote, StringComparison.OrdinalIgnoreCase)))
                row.IsSelected = true;
        }

        private bool CanInitializeSelected() => !IsRunning && !IsBusy && SelectedRows().Count > 0;

        private bool CanPullSelected() => !IsRunning && !IsBusy && PullRows().Any(r => r.CanPull);

        private bool CanSwitchSelected() => !IsRunning && !IsBusy && SwitchRows().Count > 0;

        private bool CanResetSelected() => !IsRunning && !IsBusy && ResetRows().Count > 0;

        partial void OnIsBusyChanged(bool value)
        {
            RefreshSelectionState();
            CopyReportCommand.NotifyCanExecuteChanged();
            SetUrlCommand.NotifyCanExecuteChanged();
            ResetUrlCommand.NotifyCanExecuteChanged();
            CloneManuallyCommand.NotifyCanExecuteChanged();
            SwitchBranchCommand.NotifyCanExecuteChanged();
            ResetToRecordedCommand.NotifyCanExecuteChanged();
        }

        // -- Batch runner ---------------------------------------------------------

        private enum BatchStatus { Succeeded, Skipped, Failed }

        /// <summary>What one row's action came to. Explicit, never parsed from git's text.</summary>
        private sealed record BatchOutcome(BatchStatus Status, string Text = "",
            SubmoduleSkipReason Skip = SubmoduleSkipReason.None, SubmoduleInfo? Fresh = null, string? Tag = null)
        {
            public static BatchOutcome Success(SubmoduleInfo? fresh = null, string? tag = null) =>
                new(BatchStatus.Succeeded, Fresh: fresh, Tag: tag);
            public static BatchOutcome Skipped(SubmoduleSkipReason reason, string text) =>
                new(BatchStatus.Skipped, text, reason);
            public static BatchOutcome Failed(string text) => new(BatchStatus.Failed, text);
        }

        private sealed record BatchRun(List<(SubmoduleRowViewModel Row, BatchOutcome Outcome)> Done,
            int Total, bool Cancelled)
        {
            public List<(SubmoduleRowViewModel Row, BatchOutcome Outcome)> Succeeded =>
                Done.Where(d => d.Outcome.Status == BatchStatus.Succeeded).ToList();
        }

        /// <summary>
        /// Runs <paramref name="action"/> on each target strictly one after another, in list order. Locks the
        /// rows, shows Queued/Working, records skips and failures on their rows (a failure never stops the
        /// others), reloads, and re-ticks the ticked rows (all of them, or with <c>clearSucceeded</c> only those that did not succeed) so the user can simply run again.
        /// <paramref name="buildResultText"/> turns the outcome into the line shown above the list.
        /// </summary>
        private async Task RunBatchAsync(List<SubmoduleRowViewModel> targets,
            Func<SubmoduleRowViewModel, CancellationToken, Task<BatchOutcome>> action,
            Func<BatchRun, string> buildResultText, bool clearSucceeded)
        {
            if (targets.Count == 0) return;

            IsRunning = true;
            HasInitialized = true;
            ResultText = string.Empty;
            foreach (var row in _all) row.IsLocked = true;

            _runCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var ct = _runCts.Token;

            foreach (var queued in targets) queued.IsQueued = true;

            // Ticked rows that are not targets (e.g. not populated during a Pull) keep their tick too.
            var ticked = Rows.Where(r => r.IsSelected).Select(r => r.DisplayPath).ToList();

            var done = new List<(SubmoduleRowViewModel, BatchOutcome)>();
            var cancelled = false;

            try
            {
                foreach (var row in targets)
                {
                    row.IsQueued = false;
                    row.IsWorking = true;
                    BatchOutcome outcome;
                    try
                    {
                        outcome = await action(row, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        outcome = BatchOutcome.Failed(TailOf(new GitResult(-1, string.Empty, ex.Message)));
                    }
                    finally
                    {
                        row.IsWorking = false;
                    }

                    done.Add((row, outcome));

                    if (outcome.Status == BatchStatus.Succeeded)
                    {
                        ClearRowError(row, row.DisplayPath);

                        // Show the real state now instead of waiting for the end-of-run reload.
                        try
                        {
                            var fresh = outcome.Fresh ?? await _submoduleService.GetAsync(
                                row.Info.RepoRoot, row.Path, ct, row.Info.DisplayPrefix, row.Depth);
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
                    else if (outcome.Status == BatchStatus.Skipped)
                    {
                        SetRowError(row, row.DisplayPath, outcome.Text, outcome.Skip);
                    }
                    else
                    {
                        SetRowError(row, row.DisplayPath, outcome.Text);
                    }
                }

                var run = new BatchRun(done, targets.Count, cancelled);
                // Initialize unticks what succeeded; the other actions keep every tick (Pull all ticked nothing).
                var stillSelected = (clearSucceeded
                        ? ticked.Except(run.Succeeded.Select(d => d.Row.DisplayPath))
                        : ticked)
                    .ToHashSet(StringComparer.Ordinal);

                await LoadAsync();

                foreach (var row in Rows.Where(r => r.CanSelect && stillSelected.Contains(r.DisplayPath)))
                    row.IsSelected = true;

                ResultText = buildResultText(run);
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

        /// <summary>
        /// Initializes the selected submodules strictly one after another, in list order. A failure is
        /// recorded on its row and never stops the others.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanInitializeSelected))]
        private async Task InitializeSelectedAsync()
        {
            var targets = SelectedRows();
            if (targets.Count == 0) return;

            var latest = Target == SubmoduleTarget.LatestFromBranch;
            var nested = IncludeNested;

            if (latest && !ConfirmLatest()) return;

            await RunBatchAsync(targets,
                async (row, ct) =>
                {
                    // A populated row is about to be moved, so it gets the same safety rules as Switch branch.
                    var refusal = await CheckBeforeUpdateAsync(row, latest, ct);
                    if (refusal != null)
                    {
                        if (refusal.ExitCode is DirtyExitCode or DeclinedExitCode)
                            return BatchOutcome.Skipped(
                                refusal.ExitCode == DirtyExitCode ? SubmoduleSkipReason.UncommittedChanges : SubmoduleSkipReason.Declined,
                                refusal.StdErr);
                        return BatchOutcome.Failed(TailOf(refusal));
                    }

                    var result = await _submoduleService.InitAndUpdateAsync(row.Info.RepoRoot, row.Info, latest, nested, _resolveAuth, ct);
                    return result.ExitCode == 0 ? BatchOutcome.Success() : BatchOutcome.Failed(TailOf(result));
                },
                run => BuildResultText(run.Succeeded.Count, run.Total,
                    run.Done.Where(d => d.Outcome.Status == BatchStatus.Failed).Select(d => d.Row.DisplayPath).ToList(),
                    run.Done.Where(d => d.Outcome.Status == BatchStatus.Skipped).Select(d => d.Row.DisplayPath).ToList(),
                    run.Cancelled),
                clearSucceeded: true);
        }

        /// <summary>Marks a refusal in <see cref="GitResult.ExitCode"/>; its StdErr is the row's error text.</summary>
        private const int DirtyExitCode = 3;

        /// <summary>Marks a row the user chose not to update (the unreferenced-commit question was answered No).</summary>
        private const int DeclinedExitCode = 4;

        /// <summary>
        /// Safety check for a row that is already on disk and is about to be moved (a different commit, or
        /// "latest from branch"). Returns null to go ahead, a dirty/declined result to skip the row, or a
        /// failure to record on the row. Missing submodules have nothing to lose.
        /// </summary>
        private async Task<GitResult?> CheckBeforeUpdateAsync(SubmoduleRowViewModel row, bool latest, CancellationToken ct)
        {
            var populated = row.State == SubmoduleState.DifferentCommit ||
                            (row.State == SubmoduleState.Ready && latest);
            if (!populated) return null;

            SwitchCheck check;
            try
            {
                check = await _submoduleService.CheckSwitchSafetyAsync(row.Info, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new GitResult(-1, string.Empty, ex.Message);
            }

            if (check.IsDirty)
            {
                return new GitResult(DirtyExitCode, string.Empty, DirtyText(row.DisplayPath, check));
            }

            if (check.UnreferencedCommits && !_dialogService.ShowConfirmation(
                    "Initialize selected",
                    $"The current commit in {row.DisplayPath} isn't on any branch. Updating will leave it behind. Continue?",
                    destructive: true))
                return new GitResult(DeclinedExitCode, string.Empty,
                    $"Skipped: the current commit in {row.DisplayPath} isn't on any branch, and you chose not to leave it behind.");

            return null;
        }

        private static string DirtyText(string displayPath, SwitchCheck check) =>
            $"Uncommitted changes in {displayPath} — commit or discard them first." +
            Environment.NewLine + string.Join(Environment.NewLine, check.DirtyFiles.Take(MaxListedFiles));

        // -- Row actions: fix a broken submodule for this checkout only ----------

        private bool CanRowAction(SubmoduleRowViewModel? row) => !IsRunning && !IsBusy && row != null;

        [RelayCommand(CanExecute = nameof(CanRowAction))]
        private async Task SetUrlAsync(SubmoduleRowViewModel? row)
        {
            if (row == null) return;

            var latest = Target == SubmoduleTarget.LatestFromBranch;
            var nested = IncludeNested;
            if (latest && !ConfirmLatest()) return;

            var url = PickUrl(row);
            if (url == null) return;

            await RunRowActionAsync(row, "Set URL for",
                ct => _submoduleService.SetUrlAndInitAsync(row.Info.RepoRoot, row.Info, url, latest, nested, _resolveAuth, ct));
        }

        [RelayCommand(CanExecute = nameof(CanRowAction))]
        private async Task ResetUrlAsync(SubmoduleRowViewModel? row)
        {
            if (row == null) return;
            if (!_dialogService.ShowConfirmation("Reset URL", "Use the URL from .gitmodules again?")) return;

            await RunRowActionAsync(row, "Reset URL for",
                ct => _submoduleService.ResetUrlAsync(row.Info.RepoRoot, row.Info, ct));
        }

        [RelayCommand(CanExecute = nameof(CanRowAction))]
        private async Task CloneManuallyAsync(SubmoduleRowViewModel? row)
        {
            if (row == null) return;

            if (!_dialogService.ShowConfirmation(
                    "Clone manually",
                    $"This repository has no .gitmodules entry for {row.DisplayPath}. The app will clone the repository " +
                    $"you choose into that folder at the commit the main repo expects ({row.PinnedShortSha}). " +
                    "Git won't treat it as a registered submodule."))
                return;

            var url = PickUrl(row);
            if (url == null) return;

            await RunRowActionAsync(row, "Cloned",
                ct => _submoduleService.CloneManuallyAsync(row.Info.RepoRoot, row.Info, url, _resolveAuth, ct));
        }

        private const int MaxListedFiles = 50;

        [RelayCommand(CanExecute = nameof(CanRowAction))]
        private async Task SwitchBranchAsync(SubmoduleRowViewModel? row)
        {
            if (row == null) return;

            var check = await CheckSafetyAsync(row);
            if (check == null) return;

            var branch = _dialogService.ShowSubmoduleBranch(new SubmoduleBranchModel
            {
                DisplayPath = row.DisplayPath,
                CurrentBranch = row.Info.CurrentBranch,
                LoadBranchesAsync = ct => _submoduleService.ListRemoteBranchesAsync(row.Info, _resolveAuth, ct)
            });
            if (branch == null) return;

            if (check.UnreferencedCommits && !_dialogService.ShowConfirmation(
                    "Switch branch",
                    $"The current commit in {row.DisplayPath} isn't on any branch. After switching it will be hard to find. Continue?"))
                return;

            await RunRowActionAsync(row, "Switched",
                async ct => (await _submoduleService.SwitchBranchAsync(row.Info, branch, _resolveAuth, ct)).ToGitResult(),
                showNote: true);
        }

        [RelayCommand(CanExecute = nameof(CanRowAction))]
        private async Task ResetToRecordedAsync(SubmoduleRowViewModel? row)
        {
            if (row == null) return;

            if (await CheckSafetyAsync(row) == null) return;

            if (!_dialogService.ShowConfirmation(
                    "Reset to recorded commit",
                    $"Move {row.DisplayPath} back to {row.PinnedShortSha}, the commit the main repo expects?",
                    destructive: true))
                return;

            await RunRowActionAsync(row, "Reset", ct => _submoduleService.ResetToRecordedAsync(row.Info, ct));
        }

        // -- Bulk actions -----------------------------------------------------------

        private static bool IsPopulatedState(SubmoduleState state) =>
            state is SubmoduleState.Ready or SubmoduleState.DifferentCommit or SubmoduleState.ManuallyCloned;

        /// <summary>
        /// Run right before acting on a row: a parent switched earlier in the same batch can change or remove a
        /// nested child, so the row is read again, then checked for uncommitted changes. Returns the fresh state
        /// and check, or the outcome that skips the row.
        /// </summary>
        private async Task<(SubmoduleInfo? Fresh, SwitchCheck? Check, BatchOutcome? Stop)> PrepareRowAsync(
            SubmoduleRowViewModel row, CancellationToken ct)
        {
            var fresh = await _submoduleService.GetAsync(
                row.Info.RepoRoot, row.Path, ct, row.Info.DisplayPrefix, row.Depth);
            if (fresh == null || !IsPopulatedState(fresh.State))
                return (null, null, BatchOutcome.Skipped(SubmoduleSkipReason.NotAvailable,
                    $"{row.DisplayPath} is no longer a populated submodule."));

            // The VM never calls a switch on a dirty folder.
            var check = await _submoduleService.CheckSwitchSafetyAsync(fresh, ct);
            if (check.IsDirty)
                return (fresh, check, BatchOutcome.Skipped(SubmoduleSkipReason.UncommittedChanges, DirtyText(row.DisplayPath, check)));

            return (fresh, check, null);
        }

        /// <summary>Maps a switch result to the row's outcome. <paramref name="after"/> is read only on success.</summary>
        private async Task<BatchOutcome> SwitchOutcomeAsync(SubmoduleRowViewModel row, SwitchResult result, string branch,
            string? shaBefore, bool pull, CancellationToken ct)
        {
            if (result.Outcome == SwitchOutcome.BranchNotOnRemote)
                return BatchOutcome.Skipped(SubmoduleSkipReason.BranchNotOnRemote,
                    $"The branch {branch} doesn't exist on this submodule's origin.");
            if (result.ExitCode != 0) return BatchOutcome.Failed(TailOf(result.ToGitResult()));
            // Pull: nothing moved. Switch: the checkout already happened, only the fast-forward was left out.
            if (result.Outcome == SwitchOutcome.LeftAsIs && pull)
                return BatchOutcome.Skipped(SubmoduleSkipReason.LocalCommits, result.StdOut);

            var after = await _submoduleService.GetAsync(
                row.Info.RepoRoot, row.Path, ct, row.Info.DisplayPrefix, row.Depth);
            if (result.Outcome == SwitchOutcome.LeftAsIs) return BatchOutcome.Success(after, "notff");
            var moved = !string.Equals(after?.CurrentSha, shaBefore, StringComparison.OrdinalIgnoreCase);
            return BatchOutcome.Success(after, pull ? (moved ? "updated" : "uptodate") : null);
        }

        [RelayCommand(CanExecute = nameof(CanPullSelected))]
        private async Task PullSelectedAsync()
        {
            var targets = PullRows();

            await RunBatchAsync(targets,
                async (row, ct) =>
                {
                    var (fresh, _, stop) = await PrepareRowAsync(row, ct);
                    if (stop != null) return stop;

                    // Re-read on purpose: the row may have been detached or moved since it was listed.
                    var branch = fresh!.CurrentBranch;
                    if (branch == null)
                        return BatchOutcome.Skipped(SubmoduleSkipReason.NotOnBranch,
                            $"{row.DisplayPath} is not on a branch, so there is nothing to pull.");

                    var result = await _submoduleService.SwitchBranchAsync(fresh, branch, _resolveAuth, ct);
                    return await SwitchOutcomeAsync(row, result, branch, fresh.CurrentSha, pull: true, ct);
                },
                run =>
                {
                    var ok = run.Succeeded;
                    var updated = ok.Count(d => d.Outcome.Tag == "updated");
                    var text = $"Pulled {ok.Count} of {run.Total}";
                    text += ok.Count > 0 ? $": {updated} updated, {ok.Count - updated} already up to date." : ".";
                    return JoinText(text, ProblemSummary(run));
                },
                clearSucceeded: false);
        }

        /// <summary>Remote branches of every row, one listing per distinct remote. Fills <paramref name="availability"/> by row path.</summary>
        private async Task LoadAvailabilityAsync(List<SubmoduleRowViewModel> rows,
            Dictionary<string, HashSet<string>> availability, CancellationToken ct)
        {
            // Rows without an origin URL are listed on their own.
            foreach (var group in rows.GroupBy(r => r.RemoteKey ?? "\0" + r.DisplayPath, StringComparer.OrdinalIgnoreCase))
            {
                var rep = group.First();
                HashSet<string> names;
                try
                {
                    names = (await _submoduleService.ListRemoteBranchesAsync(rep.Info, _resolveAuth, ct))
                        .ToHashSet(StringComparer.Ordinal);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && rows.Count > 1)
                {
                    throw new InvalidOperationException($"{rep.DisplayPath}: {ex.Message}", ex);
                }
                foreach (var row in group) availability[row.DisplayPath] = names;
            }
        }

        [RelayCommand(CanExecute = nameof(CanSwitchSelected))]
        private async Task SwitchSelectedAsync()
        {
            var ticked = SwitchRows();
            if (ticked.Count == 0) return;

            // Preflight: dirty rows are reported as skipped and never block the others.
            var checks = await PreflightAsync(ticked);
            if (checks == null) return;

            bool IsDirty(SubmoduleRowViewModel r) => checks.TryGetValue(r, out var c) && c.IsDirty;
            var clean = ticked.Where(r => !IsDirty(r)).ToList();

            if (clean.Count == 0)
            {
                foreach (var row in ticked)
                    SetRowError(row, row.DisplayPath, DirtyText(row.DisplayPath, checks[row]), SubmoduleSkipReason.UncommittedChanges);
                ResultText = $"Nothing to switch: {ticked.Count} skipped (uncommitted changes): " +
                             string.Join(", ", ticked.Select(r => r.DisplayPath)) + ".";
                return;
            }

            var availability = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            var model = clean.Count == 1
                ? new SubmoduleBranchModel
                {
                    DisplayPath = clean[0].DisplayPath,
                    CurrentBranch = clean[0].Info.CurrentBranch,
                    LoadBranchesAsync = async ct =>
                    {
                        await LoadAvailabilityAsync(clean, availability, ct);
                        return availability[clean[0].DisplayPath].OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
                    }
                }
                : new SubmoduleBranchModel
                {
                    DisplayPath = $"{clean.Count} submodules",
                    RowCount = clean.Count,
                    LoadBranchOptionsAsync = async ct =>
                    {
                        await LoadAvailabilityAsync(clean, availability, ct);
                        return availability.Values
                            .SelectMany(v => v).Distinct(StringComparer.Ordinal)
                            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                            .Select(b => new BranchOption(b,
                                clean.Count(r => availability[r.DisplayPath].Contains(b)),
                                clean.Count(r => r.Info.CurrentBranch == b)))
                            .ToList();
                    }
                };

            var branch = _dialogService.ShowSubmoduleBranch(model);
            if (branch == null) return;

            bool HasBranch(SubmoduleRowViewModel r) =>
                !availability.TryGetValue(r.DisplayPath, out var set) || set.Contains(branch);

            // One question for all rows whose current commit is on no branch; No skips only those.
            var accepted = new HashSet<string>(StringComparer.Ordinal);
            var unreferenced = clean.Where(r => HasBranch(r) && checks.TryGetValue(r, out var c) && c.UnreferencedCommits).ToList();
            if (unreferenced.Count > 0)
            {
                var yes = _dialogService.ShowConfirmation(
                    "Switch branch",
                    $"The current commit in {unreferenced.Count} submodule{(unreferenced.Count == 1 ? "" : "s")} isn't on any branch. " +
                    "After switching it will be hard to find. Continue for them? (No skips only those; the others still switch.)",
                    string.Join(Environment.NewLine, unreferenced.Select(r => r.DisplayPath)),
                    destructive: true);
                if (yes) foreach (var r in unreferenced) accepted.Add(r.DisplayPath);
            }

            await RunBatchAsync(ticked,
                async (row, ct) =>
                {
                    var (fresh, check, stop) = await PrepareRowAsync(row, ct);
                    if (stop != null) return stop;

                    if (!HasBranch(row))
                        return BatchOutcome.Skipped(SubmoduleSkipReason.BranchNotOnRemote,
                            $"The branch {branch} doesn't exist on this submodule's origin.");

                    if (check!.UnreferencedCommits && !accepted.Contains(row.DisplayPath))
                        return BatchOutcome.Skipped(SubmoduleSkipReason.Declined,
                            $"Skipped: the current commit in {row.DisplayPath} isn't on any branch, and switching wasn't confirmed for it.");

                    // A row already on the branch simply gets fast-forwarded: same code path.
                    var result = await _submoduleService.SwitchBranchAsync(fresh!, branch, _resolveAuth, ct);
                    return await SwitchOutcomeAsync(row, result, branch, fresh!.CurrentSha, pull: false, ct);
                },
                run =>
                {
                    var notFf = run.Succeeded.Where(d => d.Outcome.Tag == "notff").Select(d => d.Row.DisplayPath).ToList();
                    var note = notFf.Count > 0
                        ? $" ({notFf.Count} not fast-forwarded, local commits: {string.Join(", ", notFf)})"
                        : string.Empty;
                    return JoinText($"Switched {run.Succeeded.Count} of {run.Total} to {branch}{note}.", ProblemSummary(run));
                },
                clearSucceeded: false);
        }

        /// <summary>Safety check of every row up front. Null when cancelled. A row whose check fails is left out (its own run reports it).</summary>
        private async Task<Dictionary<SubmoduleRowViewModel, SwitchCheck>?> PreflightAsync(List<SubmoduleRowViewModel> rows)
        {
            IsBusy = true;
            try
            {
                var map = new Dictionary<SubmoduleRowViewModel, SwitchCheck>();
                foreach (var row in rows)
                {
                    try
                    {
                        map[row] = await _submoduleService.CheckSwitchSafetyAsync(row.Info, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                    catch (Exception)
                    {
                        // Reported on the row when its turn comes.
                    }
                }
                return map;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanResetSelected))]
        private async Task ResetSelectedAsync()
        {
            var targets = ResetRows();
            if (targets.Count == 0) return;

            if (!_dialogService.ShowConfirmation(
                    "Reset to recorded commit",
                    $"Move {targets.Count} submodule{(targets.Count == 1 ? "" : "s")} back to the commit the main repo expects? " +
                    "Submodules with uncommitted changes are skipped.",
                    string.Join(Environment.NewLine, targets.Select(r => r.DisplayPath)),
                    destructive: true))
                return;

            await RunBatchAsync(targets,
                async (row, ct) =>
                {
                    var (fresh, _, stop) = await PrepareRowAsync(row, ct);
                    if (stop != null) return stop;

                    if (string.Equals(fresh!.CurrentSha, fresh.PinnedSha, StringComparison.OrdinalIgnoreCase))
                        return BatchOutcome.Skipped(SubmoduleSkipReason.NotAvailable,
                            $"{row.DisplayPath} is already on the recorded commit.");

                    var result = await _submoduleService.ResetToRecordedAsync(fresh, ct);
                    return result.ExitCode == 0 ? BatchOutcome.Success() : BatchOutcome.Failed(TailOf(result));
                },
                run => JoinText($"Reset {run.Succeeded.Count} of {run.Total} to the recorded commit.", ProblemSummary(run)),
                clearSucceeded: false);
        }

        private static string JoinText(string head, string tail) => tail.Length == 0 ? head : $"{head} {tail}";

        private static string SkipLabel(SubmoduleSkipReason reason) => reason switch
        {
            SubmoduleSkipReason.UncommittedChanges => "skipped (uncommitted changes)",
            SubmoduleSkipReason.NotOnBranch => "skipped (not on a branch)",
            SubmoduleSkipReason.BranchNotOnRemote => "skipped (branch not on its remote)",
            SubmoduleSkipReason.Declined => "skipped (declined)",
            SubmoduleSkipReason.NotAvailable => "skipped (nothing to do)",
            SubmoduleSkipReason.LocalCommits => "left as is (local commits)",
            _ => "skipped"
        };

        /// <summary>"1 failed: a. 1 skipped (uncommitted changes): b. 1 left as is (local commits): c. Cancelled."</summary>
        private static string ProblemSummary(BatchRun run)
        {
            var parts = new List<string>();

            var failed = run.Done.Where(d => d.Outcome.Status == BatchStatus.Failed).Select(d => d.Row.DisplayPath).ToList();
            if (failed.Count > 0) parts.Add($"{failed.Count} failed: {string.Join(", ", failed)}.");

            foreach (var group in run.Done.Where(d => d.Outcome.Status == BatchStatus.Skipped)
                         .GroupBy(d => d.Outcome.Skip).OrderBy(g => g.Key))
                parts.Add($"{group.Count()} {SkipLabel(group.Key)}: {string.Join(", ", group.Select(d => d.Row.DisplayPath))}.");

            if (run.Cancelled) parts.Add("Cancelled.");
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Runs the safety check. Returns null (after telling the user why) when the folder has uncommitted
        /// changes or the check itself failed; the switch is never attempted then.
        /// </summary>
        private async Task<SwitchCheck?> CheckSafetyAsync(SubmoduleRowViewModel row)
        {
            IsBusy = true;
            try
            {
                var check = await _submoduleService.CheckSwitchSafetyAsync(row.Info, _cts.Token);
                if (!check.IsDirty) return check;

                var shown = check.DirtyFiles.Take(MaxListedFiles).ToList();
                var more = check.DirtyFiles.Count - shown.Count;
                var list = string.Join(Environment.NewLine, shown) +
                           (more > 0 ? $"{Environment.NewLine}…and {more} more" : string.Empty);

                _dialogService.ShowMessage(
                    "Uncommitted changes",
                    $"Commit or discard these changes in {row.DisplayPath} first.",
                    list);
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                SetRowError(row, row.DisplayPath, ex.Message);
                return null;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool ConfirmLatest() => _dialogService.ShowConfirmation(
            "Latest from branch",
            "Latest from branch moves submodules away from the commit the main repo expects. " +
            "The main repo will then show them as changed. Continue?");

        private string? PickUrl(SubmoduleRowViewModel row) => _dialogService.ShowSubmoduleUrl(new SubmoduleUrlModel
        {
            Path = row.DisplayPath,
            GitmodulesUrl = row.Url,
            Repositories = _getRepositories(),
            TestUrlAsync = async (url, ct) =>
            {
                var result = await _submoduleService.TestUrlAsync(url, _resolveAuth(url), ct);
                return result.ExitCode == 0 ? null : FirstErrorLine(result);
            }
        });

        /// <summary>Runs one action on one row with the same live states as an initialize run (Working... then the result).</summary>
        private async Task RunRowActionAsync(SubmoduleRowViewModel row, string verb,
            Func<CancellationToken, Task<GitResult>> action, bool showNote = false)
        {
            IsRunning = true;
            HasInitialized = true;
            ResultText = string.Empty;
            foreach (var r in _all) r.IsLocked = true;

            _runCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var ct = _runCts.Token;
            var path = row.DisplayPath;

            try
            {
                row.IsWorking = true;
                GitResult result;
                try
                {
                    result = await action(ct);
                }
                catch (OperationCanceledException)
                {
                    ResultText = "Cancelled.";
                    await LoadAsync();
                    return;
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
                    ClearRowError(row, path);
                    ResultText = $"{verb} {path}: done.";
                    var note = showNote ? FirstErrorLine(result, fallback: string.Empty) : string.Empty;
                    if (note.Length > 0) ResultText += $" {note}";
                }
                else
                {
                    SetRowError(row, path, TailOf(result));
                    ResultText = $"{verb} {path}: failed.";
                }

                await LoadAsync();
            }
            finally
            {
                _runCts.Dispose();
                _runCts = null;
                foreach (var r in _all) r.IsLocked = false;
                IsRunning = false;
            }
        }

        private static string FirstErrorLine(GitResult result, string? fallback = null) =>
            (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr)
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? fallback ?? $"git exited with code {result.ExitCode}.";

        // -- Copy report -----------------------------------------------------------

        private List<SubmoduleReportItem> ReportItems() => _all
            .Select(r => new SubmoduleReportItem(r.Info, r.HasError ? r.Error : null, r.Skip))
            .Where(SubmoduleReportBuilder.IsReportable)
            .ToList();

        private bool CanCopyReport() => !IsBusy && _copyToClipboard != null && ReportItems().Count > 0;

        [RelayCommand(CanExecute = nameof(CanCopyReport))]
        private void CopyReport()
        {
            var report = SubmoduleReportBuilder.Build(
                _checkout?.RemoteUrl, _checkout?.Branch, _checkout?.HeadSha, AppVersion.Current,
                DateTime.Now, ReportItems());

            try
            {
                _copyToClipboard!(report);
                ResultText = "Report copied to the clipboard.";
            }
            catch (Exception ex)
            {
                ResultText = $"Could not copy the report: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(IsRunning))]
        private void CancelRun() => _runCts?.Cancel();

        private static string BuildResultText(int succeeded, int total, List<string> failed, List<string> skipped, bool cancelled)
        {
            var text = $"Initialized {succeeded} of {total}.";
            if (failed.Count > 0) text += $" {failed.Count} failed: {string.Join(", ", failed)}";
            if (skipped.Count > 0) text += $" {skipped.Count} skipped: {string.Join(", ", skipped)}";
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
            RefreshSelectionState();
            CopyReportCommand.NotifyCanExecuteChanged();

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
            Add(SubmoduleState.ManuallyCloned, "cloned manually");

            var overridden = _all.Count(r => r.UrlOverridden);
            if (overridden > 0) parts.Add($"{overridden} with a local URL override");

            var text = inCheckout == 0
                ? "No submodules in your checkout"
                : $"{inCheckout} submodule{(inCheckout == 1 ? "" : "s")} in your checkout: {string.Join(", ", parts)}";

            return outside > 0 ? $"{text} ({outside} not in your checkout)" : text;
        }
    }
}
