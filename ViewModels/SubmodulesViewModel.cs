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
        [NotifyPropertyChangedFor(nameof(IsSelectable), nameof(ActionsEnabled))]
        private bool _isLocked;

        /// <summary>The row's action menu is usable only while no run is active.</summary>
        public bool ActionsEnabled => !IsLocked;

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
                "No entry in .gitmodules, so there is no URL to clone from. Use the row menu to clone it manually, or ask the repo maintainer to add it.",
            SubmoduleState.OutsideCheckout => "Not in your sparse checkout, so it is not on disk.",
            SubmoduleState.ManuallyCloned => "Cloned by hand, so git submodule commands don't manage it.",
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
        private readonly Func<IReadOnlyList<Repository>> _getRepositories;
        private readonly CheckoutInfo? _checkout;
        private readonly Action<string>? _copyToClipboard;
        private readonly CancellationTokenSource _cts = new();
        private CancellationTokenSource? _runCts;
        private List<SubmoduleRowViewModel> _all = new();

        /// <summary>Last failure text per path. Survives the reload after a run, so failed rows keep their error.</summary>
        private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

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
            _latestFromBranch = settings.SubmoduleLatestFromBranch;
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
            var key = info.DisplayPath.Length > 0 ? info.DisplayPath : info.Path;
            if (_errors.TryGetValue(key, out var error)) row.Error = error;

            row.PropertyChanged += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.PropertyName) ||
                    e.PropertyName == nameof(SubmoduleRowViewModel.IsSelected) ||
                    e.PropertyName == nameof(SubmoduleRowViewModel.CanSelect))
                    InitializeSelectedCommand.NotifyCanExecuteChanged();

                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SubmoduleRowViewModel.Error))
                    CopyReportCommand.NotifyCanExecuteChanged();
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

        partial void OnIsBusyChanged(bool value)
        {
            InitializeSelectedCommand.NotifyCanExecuteChanged();
            CopyReportCommand.NotifyCanExecuteChanged();
            SetUrlCommand.NotifyCanExecuteChanged();
            ResetUrlCommand.NotifyCanExecuteChanged();
            CloneManuallyCommand.NotifyCanExecuteChanged();
            SwitchBranchCommand.NotifyCanExecuteChanged();
            ResetToRecordedCommand.NotifyCanExecuteChanged();
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

            var latest = LatestFromBranch;
            var nested = IncludeNested;

            if (latest && !ConfirmLatest()) return;

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
                        result = await _submoduleService.InitAndUpdateAsync(row.Info.RepoRoot, row.Info, latest, nested, _resolveAuth, ct);
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
                        _errors.Remove(row.DisplayPath);
                        succeeded.Add(row.DisplayPath);

                        // Show the real state now instead of waiting for the end-of-run reload.
                        try
                        {
                            var fresh = await _submoduleService.GetAsync(
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
                    else
                    {
                        row.Error = _errors[row.DisplayPath] = TailOf(result);
                        failed.Add(row.DisplayPath);
                    }
                }

                // Rows that did not succeed keep their tick, so the user can simply run again.
                var stillSelected = targets.Select(r => r.DisplayPath).Except(succeeded).ToHashSet(StringComparer.Ordinal);

                await LoadAsync();

                foreach (var row in Rows.Where(r => r.CanSelect && stillSelected.Contains(r.DisplayPath)))
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

        // -- Row actions: fix a broken submodule for this checkout only ----------

        private bool CanRowAction(SubmoduleRowViewModel? row) => !IsRunning && !IsBusy && row != null;

        [RelayCommand(CanExecute = nameof(CanRowAction))]
        private async Task SetUrlAsync(SubmoduleRowViewModel? row)
        {
            if (row == null) return;

            var latest = LatestFromBranch;
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
                ct => _submoduleService.SwitchBranchAsync(row.Info, branch, _resolveAuth, ct),
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
                row.Error = _errors[row.DisplayPath] = ex.Message;
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
                    row.Error = string.Empty;
                    _errors.Remove(path);
                    ResultText = $"{verb} {path}: done.";
                    var note = showNote ? FirstErrorLine(result, fallback: string.Empty) : string.Empty;
                    if (note.Length > 0) ResultText += $" {note}";
                }
                else
                {
                    row.Error = _errors[path] = TailOf(result);
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
            .Select(r => new SubmoduleReportItem(r.Info, r.HasError ? r.Error : null))
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
