using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows;
using GitCheckoutManager.Models;
using GitCheckoutManager.Services;

namespace GitCheckoutManager.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly GitHostServiceFactory _hostServiceFactory;
        private readonly ICommandGenerator _commandGenerator;
        private readonly ISettingsService _settingsService;
        private readonly ClipboardService _clipboardService;
        private readonly IDialogService _dialogService;
        private readonly IPresetService _presetService;
        private readonly IGitService _gitService;
        private readonly IRemoteTreeService _remoteTreeService;
        private readonly ICheckoutService _checkoutService;
        private readonly ISubmoduleService _submoduleService;
        private readonly IUpdateService _updateService;

        private IGitHostService _hostService;
        private AppSettings _appSettings;
        private List<TreeNodeViewModel> _allRootNodes = new();
        private bool _suppressHostSync;
        private CancellationTokenSource? _treeLoadCts;

        // Manage-mode checkout state
        // Debounces live script regeneration (Clone) and pending-change recomputation (Manage).
        private readonly DispatcherTimer _regenerateTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private List<string> _baselinePaths = new();
        private List<string> _addedPaths = new();
        private List<string> _removedPaths = new();
        // _removedPaths narrowed to the folders that really leave the worktree (see ExpandRemovalTargets).
        private List<string> _removalTargets = new();
        private List<string> _droppedTargets = new();
        private string _checkoutSummarySuffix = string.Empty;

        // Path of the checkout being opened or already open; guards against re-opening on selection echoes.
        private string? _activeCheckoutPath;

        // .bat files carry `chcp 65001`, so cmd needs UTF-8 and a BOM would break the first line.
        private static readonly System.Text.UTF8Encoding Utf8NoBom = new(false);

        // ── Connection ────────────────────────────────────────────────────────
        [ObservableProperty] private string _serverUrl = "http://gitlab.local";
        [ObservableProperty] private string _token = string.Empty;
        [ObservableProperty] private GitHostType _selectedHostType = GitHostType.GitLab;

        public IReadOnlyList<GitHostType> HostTypes { get; } = Enum.GetValues<GitHostType>();

        // ── Repository / Branch selection ─────────────────────────────────────
        [ObservableProperty] private ObservableCollection<Repository> _repositories = new();
        [ObservableProperty] private Repository? _selectedRepository;
        [ObservableProperty] private ObservableCollection<Branch> _branches = new();
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NewBranchHint))]
        private Branch? _selectedBranch;
        [ObservableProperty] private string _branchFilterText = string.Empty;
        [ObservableProperty] private string _repositoryFilterText = string.Empty;

        private ICollectionView? _branchesView;
        public ICollectionView? BranchesView => _branchesView;

        private ICollectionView? _repositoriesView;
        public ICollectionView? RepositoriesView => _repositoriesView;

        // ── Tree ──────────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<TreeNodeViewModel> _treeNodes = new();
        [ObservableProperty] private string _searchFilter = string.Empty;
        [ObservableProperty] private string _searchResultText = string.Empty;

        // ── Output ────────────────────────────────────────────────────────────
        [ObservableProperty] private string _selectedPathsText = string.Empty;
        [ObservableProperty] private string _generatedScript = string.Empty;

        /// <summary>True when <see cref="GeneratedScript"/> is a real script rather than an explanatory comment.</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CopyScriptCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveBatCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveShCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExecuteScriptCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveManageBatCommand))]
        [NotifyCanExecuteChangedFor(nameof(SaveManageShCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyManageCommand))]
        private bool _hasValidScript;

        [ObservableProperty] private string _newBranchName = string.Empty;

        // ── Destination ───────────────────────────────────────────────────────
        [ObservableProperty] private string _cloneParentFolder = string.Empty;
        [ObservableProperty] private string _folderName = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ResetFolderNameCommand))]
        private bool _isFolderNameAuto = true;

        [ObservableProperty] private string _destinationPreview = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExecuteScriptCommand))]
        private bool _hasDestinationError;

        /// <summary>True when the preview is only a prompt (nothing selected yet), not a user mistake.</summary>
        [ObservableProperty] private bool _isDestinationHint;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NewBranchHint))]
        [NotifyCanExecuteChangedFor(nameof(ExecuteScriptCommand))]
        private bool _isNewBranchInvalid;

        // True from a keystroke in New Branch until its git validation has answered.
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExecuteScriptCommand))]
        private bool _isBranchCheckPending;

        private bool _settingFolderName;
        private CancellationTokenSource? _branchCheckCts;

        public string NewBranchHint => IsNewBranchInvalid
            ? "Not a valid branch name"
            : $"Created locally from {SelectedBranch?.Name ?? "the selected branch"} after checkout. Not pushed.";

        /// <summary>Output of the last Manage apply (git warnings). Cleared when the next apply starts.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLastApplyLog))]
        private string _lastApplyLog = string.Empty;

        public bool HasLastApplyLog => !string.IsNullOrWhiteSpace(LastApplyLog);

        /// <summary>Folders the last Manage apply could not delete. Cleared on the next apply or when another checkout opens.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasCleanupLeftovers))]
        private ObservableCollection<(string Path, string Reason)> _cleanupLeftovers = new();

        public bool HasCleanupLeftovers => CleanupLeftovers.Count > 0;

        [ObservableProperty] private bool _scriptPanelExpanded;

        // ── Manage checkout ───────────────────────────────────────────────────
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ShowSubmodulesCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisableSparseCheckoutCommand))]
        private CheckoutInfo? _checkoutInfo;
        [ObservableProperty] private string _checkoutSummary = string.Empty;
        [ObservableProperty] private ObservableCollection<RecentCheckout> _recentCheckouts = new();
        [ObservableProperty] private RecentCheckout? _selectedRecentCheckout;
        [ObservableProperty] private string _pendingChangesText = "No changes";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ApplyManageCommand))]
        private bool _isApplyEnabled = false;

        /// <summary>Repository root the Manage actions operate on.</summary>
        private string ManageRoot => CheckoutInfo?.Root ?? string.Empty;

        // ── Script options ────────────────────────────────────────────────────
        [ObservableProperty] private bool _initSubmodules = false;
        [ObservableProperty] private bool _keepWindowOpen = true;
        [ObservableProperty] private ThemeMode _selectedThemeMode = ThemeMode.System;

        // ── UI state ──────────────────────────────────────────────────────────
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RestartToUpdateCommand))]
        private bool _isLoading = false;
        [ObservableProperty] private bool _isConnected = false;
        [ObservableProperty] private string _statusMessage = "Enter your server URL and Personal Access Token, then click Connect.";

        // ── Mode ──────────────────────────────────────────────────────────────
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCloneMode))]
        [NotifyPropertyChangedFor(nameof(IsManageMode))]
        [NotifyPropertyChangedFor(nameof(IsOperationEnabled))]
        [NotifyPropertyChangedFor(nameof(IsFullClone))]
        [NotifyPropertyChangedFor(nameof(IsTreeSelectionEnabled))]
        private AppMode _activeMode = AppMode.Clone;

        // ── Clone mode (sparse vs full) ───────────────────────────────────────
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFullClone))]
        [NotifyPropertyChangedFor(nameof(IsTreeSelectionEnabled))]
        [NotifyPropertyChangedFor(nameof(IsSparseCloneSelected))]
        [NotifyPropertyChangedFor(nameof(IsFullCloneSelected))]
        private CloneMode _selectedCloneMode = CloneMode.Sparse;

        /// <summary>RadioButton binding for the sparse option.</summary>
        public bool IsSparseCloneSelected
        {
            get => SelectedCloneMode == CloneMode.Sparse;
            set { if (value) SelectedCloneMode = CloneMode.Sparse; }
        }

        /// <summary>RadioButton binding for the full-clone option.</summary>
        public bool IsFullCloneSelected
        {
            get => SelectedCloneMode == CloneMode.Full;
            set { if (value) SelectedCloneMode = CloneMode.Full; }
        }

        /// <summary>True only on the Clone tab with Full clone chosen; Manage always works on a selection.</summary>
        public bool IsFullClone => IsCloneMode && SelectedCloneMode == CloneMode.Full;

        /// <summary>Tree checkboxes and presets are usable unless a full clone includes everything anyway.</summary>
        public bool IsTreeSelectionEnabled => !IsFullClone;

        public bool IsCloneMode
        {
            get => ActiveMode == AppMode.Clone;
            set { if (value) ActiveMode = AppMode.Clone; }
        }

        public bool IsManageMode
        {
            get => ActiveMode == AppMode.Manage;
            set { if (value) ActiveMode = AppMode.Manage; }
        }

        public bool IsOperationEnabled => IsConnected || IsManageMode;

        // ── Presets ───────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<TreePreset> _availablePresets = new();

        /// <summary>
        /// Presets are keyed by repository so that Clone and Manage share them for the same repo.
        /// A checkout without a remote falls back to its root path.
        /// </summary>
        private string PresetKey
        {
            get
            {
                if (IsCloneMode)
                    return SelectedRepository == null ? string.Empty : RepoKey(SelectedRepository.HttpUrlToRepo);

                if (CheckoutInfo == null) return string.Empty;

                return string.IsNullOrWhiteSpace(CheckoutInfo.RemoteUrl)
                    ? CheckoutInfo.Root
                    : RepoKey(CheckoutInfo.RemoteUrl);
            }
        }

        private static string RepoKey(string url)
        {
            var trimmed = (url ?? string.Empty).Trim().TrimEnd('/');
            if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^4].TrimEnd('/');
            return "remote:" + trimmed.ToLowerInvariant();
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadPresetCommand))]
        [NotifyCanExecuteChangedFor(nameof(RenamePresetCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
        private TreePreset? _selectedPreset;

        public MainViewModel(
            GitHostServiceFactory hostServiceFactory,
            ICommandGenerator commandGenerator,
            ISettingsService settingsService,
            ClipboardService clipboardService,
            IDialogService dialogService,
            IPresetService presetService,
            IGitService gitService,
            IRemoteTreeService remoteTreeService,
            ICheckoutService checkoutService,
            ISubmoduleService submoduleService,
            IUpdateService updateService)
        {
            _updateService = updateService;
            _hostServiceFactory = hostServiceFactory;
            _gitService = gitService;
            _remoteTreeService = remoteTreeService;
            _checkoutService = checkoutService;
            _submoduleService = submoduleService;
            _commandGenerator = commandGenerator;
            _settingsService = settingsService;
            _clipboardService = clipboardService;
            _dialogService = dialogService;
            _presetService = presetService;

            _regenerateTimer.Tick += (_, _) =>
            {
                _regenerateTimer.Stop();
                RegenerateScript();
            };

            _appSettings = settingsService.LoadSettings();
            _scriptPanelExpanded = _appSettings.ScriptPanelExpanded;
            _cloneParentFolder = _appSettings.CloneParentFolder ?? string.Empty;
            _selectedCloneMode = _appSettings.CloneMode;
            InitSubmodules = _appSettings.InitSubmodules;
            KeepWindowOpen = _appSettings.KeepWindowOpen;
            SelectedThemeMode = _appSettings.ThemeMode;

            _suppressHostSync = true;
            SelectedHostType = _appSettings.HostType;
            ServerUrl = GetSavedServerUrl(_appSettings.HostType);
            Token = _settingsService.GetDecryptedToken(_appSettings, _appSettings.HostType) ?? string.Empty;
            _suppressHostSync = false;

            _hostService = _hostServiceFactory.Create(SelectedHostType);

            _branchesView = CollectionViewSource.GetDefaultView(Branches);
            _branchesView.Filter = FilterBranch;

            _repositoriesView = CollectionViewSource.GetDefaultView(Repositories);
            _repositoriesView.Filter = FilterRepository;

            RecentCheckouts = new ObservableCollection<RecentCheckout>(_appSettings.RecentCheckouts);
            RefreshRecentCheckoutState();

            // Ticking a large folder changes thousands of nodes; collapse that into one regeneration.
            TreeNodeViewModel.CheckedChanged += ScheduleRegenerate;
            UpdateDestination();
            RegenerateScript();
        }

        private static string DefaultServerUrl(GitHostType hostType) =>
            hostType == GitHostType.GitHub ? "https://github.com" : "http://gitlab.local";

        private string GetSavedServerUrl(GitHostType hostType) =>
            _appSettings.ServerUrls.TryGetValue(hostType.ToString(), out var url) && !string.IsNullOrWhiteSpace(url)
                ? url
                : DefaultServerUrl(hostType);

        // ── Property change hooks ─────────────────────────────────────────────

        partial void OnSelectedHostTypeChanged(GitHostType oldValue, GitHostType newValue)
        {
            if (_suppressHostSync) return;

            _suppressHostSync = true;
            try
            {
                // Preserve whatever was typed for the host we are leaving
                _appSettings.ServerUrls[oldValue.ToString()] = ServerUrl;
                _settingsService.SetToken(_appSettings, oldValue, Token);

                ServerUrl = GetSavedServerUrl(newValue);
                Token = _settingsService.GetDecryptedToken(_appSettings, newValue) ?? string.Empty;

                _appSettings.HostType = newValue;
                _settingsService.SaveSettings(_appSettings);

                _hostService = _hostServiceFactory.Create(newValue);

                IsConnected = false;
                Repositories.Clear();
                Branches.Clear();
                TreeNodes.Clear();
                _allRootNodes.Clear();
                SelectedPathsText = string.Empty;
                ScheduleRegenerate();
                StatusMessage = $"Enter your {newValue} server URL and Personal Access Token, then click Connect.";
            }
            finally
            {
                _suppressHostSync = false;
            }
        }

        partial void OnServerUrlChanged(string value)
        {
            if (_suppressHostSync || SelectedHostType == GitHostType.GitHub) return;

            if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
                uri.Host.Contains("github", StringComparison.OrdinalIgnoreCase))
                SelectedHostType = GitHostType.GitHub;
        }

        partial void OnInitSubmodulesChanged(bool value)
        {
            _appSettings.InitSubmodules = value;
            _settingsService.SaveSettings(_appSettings);
            ScheduleRegenerate();
        }

        partial void OnSelectedCloneModeChanged(CloneMode value)
        {
            _appSettings.CloneMode = value;
            _settingsService.SaveSettings(_appSettings);
            RegenerateScript();
        }

        partial void OnKeepWindowOpenChanged(bool value)
        {
            _appSettings.KeepWindowOpen = value;
            _settingsService.SaveSettings(_appSettings);
            ScheduleRegenerate();
        }

        public double MainSplitRatio => _appSettings.MainSplitRatio;

        public void SaveMainSplitRatio(double ratio)
        {
            _appSettings.MainSplitRatio = ratio;
            _settingsService.SaveSettings(_appSettings);
        }

        partial void OnScriptPanelExpandedChanged(bool value)
        {
            _appSettings.ScriptPanelExpanded = value;
            _settingsService.SaveSettings(_appSettings);
        }

        partial void OnNewBranchNameChanged(string value)
        {
            UpdateDestination();
            ScheduleRegenerate();
            _ = ValidateNewBranchAsync(value);
        }

        partial void OnCloneParentFolderChanged(string value)
        {
            _appSettings.CloneParentFolder = value ?? string.Empty;
            _settingsService.SaveSettings(_appSettings);
            UpdateDestination();
        }

        partial void OnFolderNameChanged(string value)
        {
            if (!_settingFolderName) IsFolderNameAuto = false;
            UpdateDestination();
            ScheduleRegenerate();
        }

        // ── Destination ───────────────────────────────────────────────────────

        private static bool IsValidFolderName(string name) =>
            !string.IsNullOrWhiteSpace(name) &&
            name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            name != "." && name != ".." &&
            !name.EndsWith('.') && !name.EndsWith(' ');

        /// <summary>The settings window changed the pattern; apply it to the automatic name.</summary>
        private void ApplyFolderNamePattern(string pattern)
        {
            _appSettings.FolderNamePattern = pattern;
            _settingsService.SaveSettings(_appSettings);
            UpdateDestination();
        }

        /// <summary>Regenerates the automatic folder name, then refreshes the path preview and its error state.</summary>
        private void UpdateDestination()
        {
            if (IsFolderNameAuto)
            {
                var name = SelectedRepository == null
                    ? string.Empty
                    : FolderNameBuilder.Build(_appSettings.FolderNamePattern, SelectedRepository.Name,
                        NewBranchName.Trim(), SelectedBranch?.Name);

                if (name != FolderName)
                {
                    _settingFolderName = true;
                    try { FolderName = name; }
                    finally { _settingFolderName = false; }
                }
            }

            string? error = null;
            var isHint = false;
            var path = string.Empty;

            if (string.IsNullOrWhiteSpace(CloneParentFolder))
                error = "Choose where to clone";
            else if (IsFolderNameAuto && SelectedRepository == null && string.IsNullOrWhiteSpace(FolderName))
            {
                error = "Select a repository";
                isHint = true;
            }
            else if (IsFolderNameAuto && string.IsNullOrWhiteSpace(FolderName))
            {
                error = "Select a branch";
                isHint = true;
            }
            else if (!IsValidFolderName(FolderName))
                error = "Folder name is invalid";
            else if (CloneParentFolder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                error = "Clone location is invalid";
            else
            {
                path = Path.Combine(CloneParentFolder, FolderName);
                try
                {
                    if (File.Exists(path) ||
                        (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()))
                        error = "Folder already exists and isn't empty";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    error = "Folder already exists and isn't empty";
                }
            }

            HasDestinationError = error != null;
            IsDestinationHint = isHint;
            DestinationPreview = error == null
                ? "→ " + path
                : path.Length == 0 ? error : $"→ {path} — {error}";
        }

        [RelayCommand]
        private void BrowseCloneParent()
        {
            var initial = Directory.Exists(CloneParentFolder) ? CloneParentFolder : null;
            var folder = _dialogService.ShowOpenFolderDialog("Select folder to clone into", initial);
            if (folder != null) CloneParentFolder = folder;
        }

        [RelayCommand(CanExecute = nameof(CanResetFolderName))]
        private void ResetFolderName()
        {
            IsFolderNameAuto = true;
            UpdateDestination();
            ScheduleRegenerate();
        }

        private bool CanResetFolderName() => !IsFolderNameAuto;

        // ── New branch validation ─────────────────────────────────────────────

        private async Task ValidateNewBranchAsync(string name)
        {
            _branchCheckCts?.Cancel();
            _branchCheckCts?.Dispose();

            name = name.Trim();
            if (name.Length == 0)
            {
                _branchCheckCts = null;
                IsNewBranchInvalid = false;
                IsBranchCheckPending = false;
                return;
            }

            var cts = new CancellationTokenSource();
            _branchCheckCts = cts;
            IsBranchCheckPending = true;

            try
            {
                await Task.Delay(300, cts.Token);

                // A leading dash would be read as a git option.
                var invalid = name.StartsWith('-');
                if (!invalid)
                {
                    var result = await _gitService.RunAsync(
                        new[] { "check-ref-format", "--branch", name }, ct: cts.Token);
                    invalid = result.ExitCode != 0;
                }

                cts.Token.ThrowIfCancellationRequested();
                IsNewBranchInvalid = invalid;
                IsBranchCheckPending = false;
            }
            catch (OperationCanceledException)
            {
                // A newer keystroke replaced this check.
            }
            catch (Exception)
            {
                // git could not be run; don't block the user on a check that couldn't happen.
                if (ReferenceEquals(_branchCheckCts, cts))
                {
                    IsNewBranchInvalid = false;
                    IsBranchCheckPending = false;
                }
            }
        }

        partial void OnSelectedThemeModeChanged(ThemeMode value)
        {
            _appSettings.ThemeMode = value;
            _settingsService.SaveSettings(_appSettings);

            if (Application.Current is App app)
                app.SetThemeMode(value);
        }

        partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(IsOperationEnabled));

        partial void OnActiveModeChanged(AppMode value)
        {
            if (value == AppMode.Manage)
            {
                _treeLoadCts?.Cancel();
                TreeNodes.Clear();
                _allRootNodes.Clear();

                RefreshRecentCheckoutState();
                var mostRecent = RecentCheckouts.FirstOrDefault(r => !r.IsMissing);
                if (mostRecent != null)
                {
                    SelectedRecentCheckout = mostRecent;
                    _ = OpenCheckoutAsync(mostRecent.Path);
                }
                else
                {
                    StatusMessage = "Click Browse\u2026 to open a Git checkout.";
                }
            }
            else
            {
                StatusMessage = "Select a repository and branch. The tree loads automatically.";
                _ = LoadRemoteTreeAsync();
            }

            RegenerateScript();
        }

        partial void OnSearchFilterChanged(string value) => DebounceApplyFilter(value);

        partial void OnBranchFilterTextChanged(string value)
        {
            _branchesView?.Refresh();
            if (string.IsNullOrWhiteSpace(value) && SelectedBranch != null)
                SelectedBranch = null;
        }

        partial void OnSelectedBranchChanged(Branch? value)
        {
            if (BranchFilterText != (value?.Name ?? string.Empty))
                BranchFilterText = value?.Name ?? string.Empty;

            if (value != null && IsCloneMode)
                _ = LoadRemoteTreeAsync(debounceMs: BranchChangeDebounceMs);

            UpdateDestination();
            ScheduleRegenerate();
        }

        partial void OnRepositoryFilterTextChanged(string value)
        {
            _repositoriesView?.Refresh();
            if (string.IsNullOrWhiteSpace(value) && SelectedRepository != null)
                SelectedRepository = null;
        }

        partial void OnSelectedRepositoryChanged(Repository? value)
        {
            if (RepositoryFilterText != (value?.PathWithNamespace ?? string.Empty))
                RepositoryFilterText = value?.PathWithNamespace ?? string.Empty;

            if (value != null)
                _ = LoadBranchesAsync();

            IsFolderNameAuto = true;
            UpdateDestination();
            ScheduleRegenerate();
        }

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(Token))
            {
                StatusMessage = $"Please enter a {SelectedHostType} server URL and Personal Access Token.";
                return;
            }

            IsLoading = true;
            StatusMessage = $"Connecting to {SelectedHostType}…";

            try
            {
                _hostService.Configure(ServerUrl, Token);
                var repos = await _hostService.GetRepositoriesAsync();

                Repositories.Clear();
                foreach (var repo in repos)
                    Repositories.Add(repo);

                _appSettings.HostType = SelectedHostType;
                _appSettings.ServerUrls[SelectedHostType.ToString()] = ServerUrl;
                _settingsService.SetToken(_appSettings, SelectedHostType, Token);
                if (SelectedHostType == GitHostType.GitLab)
                    _appSettings.GitLabUrl = ServerUrl;
                _settingsService.SaveSettings(_appSettings);

                IsConnected = true;
                StatusMessage = $"Connected to {SelectedHostType}. {repos.Count} repositories loaded.";
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusMessage = $"Connection failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool FilterBranch(object obj) =>
            obj is Branch b && (string.IsNullOrEmpty(BranchFilterText) ||
            b.Name.Contains(BranchFilterText, StringComparison.OrdinalIgnoreCase));

        private bool FilterRepository(object obj) =>
            obj is Repository r && (string.IsNullOrEmpty(RepositoryFilterText) ||
            r.PathWithNamespace.Contains(RepositoryFilterText, StringComparison.OrdinalIgnoreCase));

        private async Task LoadBranchesAsync()
        {
            if (SelectedRepository == null) return;

            IsLoading = true;
            StatusMessage = "Loading branches…";
            BranchFilterText = string.Empty;
            Branches.Clear();
            TreeNodes.Clear();
            // Drop the previous repository's nodes so its selection cannot leak into the new tree
            _allRootNodes.Clear();
            SelectedPathsText = string.Empty;
            ScheduleRegenerate();

            try
            {
                var auth = string.IsNullOrWhiteSpace(Token)
                    ? null
                    : new GitAuth(_hostService.GitHttpUsername, Token, SelectedRepository.HttpUrlToRepo);

                var branches = await _gitService.ListRemoteBranchesAsync(
                    SelectedRepository.HttpUrlToRepo, auth);

                foreach (var b in branches)
                    Branches.Add(b);

                SelectedBranch = branches.FirstOrDefault(b => b.Name == SelectedRepository.DefaultBranch)
                                 ?? branches.FirstOrDefault();

                StatusMessage = $"{branches.Count} branches loaded.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading branches: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ApplyFlatNodesAsync(List<TreeNode> flatNodes)
        {
            var roots = await Task.Run(() => BuildTree(flatNodes));
            _allRootNodes = roots;
            TreeNodes = new ObservableCollection<TreeNodeViewModel>(roots);
            // The snapshot belongs to the old nodes; start fresh and re-apply any active search
            _expansionSnapshot = null;
            ApplyFilter(SearchFilter);
            ScheduleRegenerate();
        }

        // ── Remote tree (Clone mode) ──────────────────────────────────────────

        private const int BranchChangeDebounceMs = 400;

        private async Task LoadRemoteTreeAsync(bool forceRefresh = false, int debounceMs = 0)
        {
            if (!IsCloneMode || SelectedRepository == null || SelectedBranch == null) return;

            _treeLoadCts?.Cancel();
            _treeLoadCts?.Dispose();
            var cts = new CancellationTokenSource();
            _treeLoadCts = cts;

            var previous = GetSelectedPaths();
            var repoName = SelectedRepository.Name;
            var repoUrl = SelectedRepository.HttpUrlToRepo;
            var branchName = SelectedBranch.Name;

            try
            {
                // Settle first: a newer branch change cancels this token before any git command starts.
                if (debounceMs > 0)
                    await Task.Delay(debounceMs, cts.Token);

                IsLoading = true;
                StatusMessage = $"Fetching tree for {repoName}@{branchName}…";

                var auth = new GitAuth(_hostService.GitHttpUsername, Token, repoUrl);
                var result = await _remoteTreeService.GetTreeAsync(
                    repoUrl, branchName, auth, forceRefresh, cts.Token);

                cts.Token.ThrowIfCancellationRequested();

                // The whole tree is in memory, so no lazy loading is needed.
                await ApplyFlatNodesAsync(result.Nodes);

                // Keep whatever selection still exists on the new branch.
                if (previous.Count > 0)
                    ApplyPresetToTree(previous);

                LoadPresetsForCurrentScan();
                SavePresetCommand.NotifyCanExecuteChanged();

                var shortSha = result.CommitSha.Length >= 8 ? result.CommitSha[..8] : result.CommitSha;
                var message = $"Tree loaded: {result.Nodes.Count} items at {shortSha}";
                if (result.SubmoduleCount > 0)
                    message += $", {result.SubmoduleCount} submodule(s)";
                if (result.FilterIgnored)
                    message += ". Warning: the server ignored the blob filter, so file contents were downloaded";
                StatusMessage = message;
            }
            catch (OperationCanceledException)
            {
                // A newer load replaced this one.
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not load tree: {ex.Message}";
            }
            finally
            {
                if (ReferenceEquals(_treeLoadCts, cts))
                    IsLoading = false;
            }
        }

        [RelayCommand]
        private void OpenSettings() =>
            _dialogService.ShowSettings(
                new SettingsViewModel(SelectedThemeMode, _appSettings.FolderNamePattern, _remoteTreeService,
                    mode => SelectedThemeMode = mode, ApplyFolderNamePattern, CheckForUpdatesManualAsync));

        // ── Auto-update ───────────────────────────────────────────────────────
        [ObservableProperty] private bool _isUpdateBarVisible;
        [ObservableProperty] private string _updateMessage = string.Empty;
        private bool _updateCheckRunning;

        /// <summary>Silent startup check: errors surface in the status bar at most once, never block.</summary>
        public async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                if (!_updateService.IsInstalled) return;
                await RunUpdateCheckAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Update check failed: {ex.Message}";
            }
        }

        private async Task<string> CheckForUpdatesManualAsync()
        {
            if (!_updateService.IsInstalled)
                return "Updates are only available in the installed version.";

            try
            {
                var version = await RunUpdateCheckAsync();
                return version == null ? "You're up to date." : $"Version {version} is ready. Restart to update.";
            }
            catch (Exception ex)
            {
                return $"Update check failed: {ex.Message}";
            }
        }

        private async Task<string?> RunUpdateCheckAsync()
        {
            if (_updateCheckRunning) return null;
            _updateCheckRunning = true;
            try
            {
                var version = await Task.Run(() => _updateService.CheckAndDownloadAsync());
                if (version != null)
                {
                    UpdateMessage = $"Version {version} is ready — Restart to update";
                    IsUpdateBarVisible = true;
                }
                return version;
            }
            finally
            {
                _updateCheckRunning = false;
            }
        }

        // Restart is held back while an operation runs, so an update never interrupts one.
        [RelayCommand(CanExecute = nameof(CanRestartToUpdate))]
        private void RestartToUpdate() => _updateService.ApplyUpdatesAndRestart();

        private bool CanRestartToUpdate() => !IsLoading;

        [RelayCommand]
        private void DismissUpdate() => IsUpdateBarVisible = false;

        [RelayCommand]
        private Task RefreshTree() => LoadRemoteTreeAsync(forceRefresh: true);

        internal static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:0.#} {units[unit]}";
        }

        // ── Live script ───────────────────────────────────────────────────────

        private void ScheduleRegenerate()
        {
            _regenerateTimer.Stop();
            _regenerateTimer.Start();
        }

        /// <summary>Rebuilds the script for the active mode right away and cancels any pending rebuild.</summary>
        private void RegenerateScript()
        {
            _regenerateTimer.Stop();
            if (IsManageMode) RecomputePendingChanges();
            else RegenerateCloneScript();
        }

        private void RegenerateCloneScript()
        {
            var full = IsFullClone;
            var paths = full ? new List<string>() : DropFilePaths(GetSelectedPaths(), out _);
            SelectedPathsText = full ? "All folders" : string.Join(Environment.NewLine, paths);

            string? problem = null;
            if (SelectedRepository == null) problem = "Select a repository";
            else if (string.IsNullOrWhiteSpace(SelectedBranch?.Name)) problem = "Select a branch";
            else if (!full && paths.Count == 0) problem = "Select at least one folder";

            if (problem != null)
            {
                SetScript("rem " + problem, valid: false);
                return;
            }

            SetScript(_commandGenerator.GenerateBatScript(
                SelectedRepository!.HttpUrlToRepo,
                SelectedBranch!.Name,
                paths,
                FolderName,
                string.IsNullOrWhiteSpace(NewBranchName) ? null : NewBranchName,
                InitSubmodules,
                KeepWindowOpen,
                fullClone: full), valid: true);
        }

        private void RegenerateManageScript(List<string> paths)
        {
            var root = ManageRoot;
            if (string.IsNullOrWhiteSpace(root))
                SetScript("rem Open a checkout first", valid: false);
            else if (!Directory.Exists(Path.Combine(root, ".git")))
                SetScript("rem The selected path is not a Git repository (no .git folder found)", valid: false);
            else
                SetScript(_commandGenerator.GenerateManageBatScript(root, paths, KeepWindowOpen, _removalTargets),
                    valid: true);
        }

        private void SetScript(string script, bool valid)
        {
            GeneratedScript = script;
            HasValidScript = valid;
        }

        [RelayCommand(CanExecute = nameof(HasValidScript))]
        private void CopyScript()
        {
            _clipboardService.CopyText(GeneratedScript);
            StatusMessage = "Script copied to clipboard.";
        }

        [RelayCommand(CanExecute = nameof(HasValidScript))]
        private void SaveBat()
        {
            var path = _dialogService.ShowSaveFileDialog(
                "Batch files (*.bat)|*.bat|All files (*.*)|*.*", ".bat", "sparse-checkout");

            if (path != null)
            {
                File.WriteAllText(path, GeneratedScript, Utf8NoBom);
                StatusMessage = $"Script saved to {path}";
            }
        }

        [RelayCommand(CanExecute = nameof(HasValidScript))]
        private void SaveSh()
        {
            var full = IsFullClone;
            var paths = full ? new List<string>() : DropFilePaths(GetSelectedPaths(), out _);
            if (!full && paths.Count == 0)
            {
                StatusMessage = "No paths selected.";
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedBranch?.Name))
            {
                StatusMessage = "A branch must be selected before generating the script.";
                return;
            }

            var script = _commandGenerator.GenerateShScript(
                SelectedRepository?.HttpUrlToRepo ?? string.Empty,
                SelectedBranch?.Name ?? string.Empty,
                paths,
                FolderName,
                string.IsNullOrWhiteSpace(NewBranchName) ? null : NewBranchName,
                InitSubmodules,
                KeepWindowOpen,
                fullClone: full);

            var savePath = _dialogService.ShowSaveFileDialog(
                "Shell scripts (*.sh)|*.sh|All files (*.*)|*.*", ".sh", "sparse-checkout");

            if (savePath != null)
            {
                File.WriteAllText(savePath, script);
                StatusMessage = $"Shell script saved to {savePath}";
            }
        }

        private bool CanExecuteScript() =>
            HasValidScript && !HasDestinationError && !IsNewBranchInvalid && !IsBranchCheckPending;

        [RelayCommand(CanExecute = nameof(CanExecuteScript))]
        private async Task ExecuteScriptAsync()
        {
            var full = IsFullClone;
            var paths = full ? new List<string>() : DropFilePaths(GetSelectedPaths(), out _);
            if (!full && paths.Count == 0)
            {
                StatusMessage = "No paths selected. Check items in the tree first.";
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedBranch?.Name))
            {
                StatusMessage = "A branch must be selected before generating the script.";
                return;
            }

            var targetPath = Path.Combine(CloneParentFolder, FolderName);
            var newBranch = string.IsNullOrWhiteSpace(NewBranchName) ? null : NewBranchName;

            var scriptToRun = _commandGenerator.GenerateBatScript(
                SelectedRepository?.HttpUrlToRepo ?? string.Empty,
                SelectedBranch?.Name ?? string.Empty,
                paths,
                targetPath,
                newBranch,
                InitSubmodules,
                KeepWindowOpen,
                fullClone: full);

            var message = $"{(full ? "Full clone of" : "Clone")} {SelectedRepository?.Name} @ {SelectedBranch?.Name} into\n{targetPath}";
            if (newBranch != null) message += $"\nand create branch {newBranch}";

            if (!_dialogService.ShowConfirmation("Execute Script", message + "?"))
                return;

            DismissSubmoduleBar();
            IsLoading = true;
            StatusMessage = "Executing script…";

            var tempFile = Path.Combine(Path.GetTempPath(), $"sparse_{Guid.NewGuid():N}.bat");
            try
            {
                File.WriteAllText(tempFile, scriptToRun, Utf8NoBom);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{tempFile}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var exitCode = process.ExitCode;
                    // 0xC000013A (-1073741510) = window closed by user
                    StatusMessage = exitCode switch
                    {
                        0 => "Checkout created.",
                        1 => "Script failed, see the console window.",
                        2 => "Checkout created, but some submodules failed to initialize.",
                        -1073741510 => "Script window was closed by the user.",
                        _ => $"Script finished with exit code {exitCode}."
                    };

                    if (exitCode is 0 or 2)
                    {
                        if (Directory.Exists(Path.Combine(targetPath, ".git")))
                            AddOrUpdateRecentCheckout(
                                targetPath,
                                SelectedRepository?.HttpUrlToRepo,
                                newBranch ?? SelectedBranch?.Name);

                        UpdateDestination();
                    }

                    if (exitCode == 2 && Directory.Exists(targetPath))
                        ShowSubmoduleBar(targetPath);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Execution error: {ex.Message}";
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                IsLoading = false;
            }
        }

        // ── Manage: opening a checkout ────────────────────────────────────────

        [RelayCommand]
        private async Task BrowseCheckoutAsync()
        {
            var folder = _dialogService.ShowOpenFolderDialog("Select a Git checkout", CheckoutInfo?.Root);
            if (folder == null) return;
            await OpenCheckoutAsync(folder);
        }

        [RelayCommand]
        private async Task ReloadCheckoutAsync()
        {
            var root = CheckoutInfo?.Root;
            if (string.IsNullOrWhiteSpace(root))
            {
                StatusMessage = "No checkout is open.";
                return;
            }
            await OpenCheckoutAsync(root);
        }

        // ── Post-clone bar: some submodules failed ────────────────────────────
        [ObservableProperty] private bool _isSubmoduleBarVisible;
        [ObservableProperty] private string _submoduleBarMessage = string.Empty;
        private string? _submoduleBarPath;

        private void ShowSubmoduleBar(string checkoutPath)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(checkoutPath));
            _submoduleBarPath = checkoutPath;
            SubmoduleBarMessage = $"Some submodules need attention in {name}.";
            IsSubmoduleBarVisible = true;
        }

        [RelayCommand]
        private void DismissSubmoduleBar()
        {
            IsSubmoduleBarVisible = false;
            _submoduleBarPath = null;
        }

        [RelayCommand]
        private async Task ReviewSubmodulesAsync()
        {
            var path = _submoduleBarPath;
            DismissSubmoduleBar();
            if (string.IsNullOrWhiteSpace(path)) return;

            ActiveMode = AppMode.Manage;
            await OpenCheckoutAsync(path);

            if (CheckoutInfo != null)
                await ShowSubmodulesCoreAsync(onlyProblems: true);
        }

        [RelayCommand(CanExecute = nameof(HasCheckout))]
        private Task ShowSubmodulesAsync() => ShowSubmodulesCoreAsync(onlyProblems: false);

        private async Task ShowSubmodulesCoreAsync(bool onlyProblems)
        {
            var root = CheckoutInfo?.Root;
            if (string.IsNullOrWhiteSpace(root)) return;

            var vm = new SubmodulesViewModel(
                _submoduleService, root, ResolveAuthForRemote, _dialogService, _appSettings, _settingsService,
                () => Repositories.ToList(), CheckoutInfo, _clipboardService.CopyText, onlyProblems);
            _ = vm.RefreshCommand.ExecuteAsync(null); // loads while the window opens; it reports its own errors
            _dialogService.ShowSubmodules(vm);

            // A full reload would rebuild the tree and drop the user's ticks, so only re-read the summary,
            // and only when an initialize run could have changed something.
            if (vm.HasInitialized)
                await RefreshCheckoutSummaryAsync(root);
        }

        /// <summary>Re-reads the checkout state for the summary line (local-change count) without touching the tree, ticks or baseline.</summary>
        private async Task RefreshCheckoutSummaryAsync(string root)
        {
            try
            {
                CheckoutInfo = await _checkoutService.OpenAsync(root);
                UpdateCheckoutSummary();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not refresh the checkout summary: {ex.Message}";
            }
        }

        private bool HasCheckout => CheckoutInfo != null;

        public async Task OpenCheckoutAsync(string path)
        {
            // Opening a different checkout makes the post-clone hint stale.
            if (_submoduleBarPath != null &&
                !string.Equals(Path.TrimEndingDirectorySeparator(path),
                    Path.TrimEndingDirectorySeparator(_submoduleBarPath), StringComparison.OrdinalIgnoreCase))
                DismissSubmoduleBar();

            // Reopening the same checkout (reload, retry, post-apply refresh) keeps the leftovers.
            if (!string.Equals(Path.TrimEndingDirectorySeparator(path),
                    Path.TrimEndingDirectorySeparator(CheckoutInfo?.Root ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase))
                CleanupLeftovers = new();

            _activeCheckoutPath = path;
            IsLoading = true;
            StatusMessage = "Opening checkout…";

            try
            {
                var info = await _checkoutService.OpenAsync(path);
                var nodes = await _checkoutService.GetTreeAsync(info.Root);
                await ApplyFlatNodesAsync(nodes);

                CheckoutInfo = info;
                _checkoutSummarySuffix = string.Empty;

                if (!info.IsSparse)
                {
                    // Everything is on disk today, so the current state is "all root folders".
                    foreach (var root in _allRootNodes.Where(n => n.IsFolder))
                        root.IsChecked = true;

                    _baselinePaths = _allRootNodes
                        .Where(n => n.IsFolder)
                        .Select(n => n.FullPath)
                        .ToList();

                    _checkoutSummarySuffix = " · Full checkout. Apply will turn on sparse checkout.";
                }
                else if (!info.IsCone)
                {
                    _baselinePaths = new List<string>();
                    _checkoutSummarySuffix =
                        " · This checkout uses non-cone sparse patterns, which this app can't edit safely.";
                }
                else
                {
                    var skippedFiles = ApplySparseCheckoutState(info.SparsePaths);
                    _baselinePaths = DropFilePaths(info.SparsePaths, out _);

                    if (skippedFiles > 0)
                        _checkoutSummarySuffix =
                            $" · {skippedFiles} file path(s) in the sparse list are ignored; only folders can be selected.";
                }

                RecomputePendingChanges();
                LoadPresetsForCurrentScan();
                SavePresetCommand.NotifyCanExecuteChanged();
                AddOrUpdateRecentCheckout(info.Root, info.RemoteUrl, info.Branch);

                _activeCheckoutPath = info.Root;
                StatusMessage = $"Checkout opened: {info.Root}";
            }
            catch (Exception ex)
            {
                _activeCheckoutPath = null;
                StatusMessage = $"Could not open checkout: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void UpdateCheckoutSummary()
        {
            var info = CheckoutInfo;
            if (info == null)
            {
                CheckoutSummary = string.Empty;
                return;
            }

            var shortSha = info.HeadSha.Length >= 8 ? info.HeadSha[..8] : info.HeadSha;
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(info.RemoteUrl))
                parts.Add($"origin: {info.RemoteUrl}");

            parts.Add(info.Branch == null
                ? $"detached at {shortSha}"
                : $"branch: {info.Branch} ({shortSha})");

            parts.Add($"{GetSelectedPaths().Count} folders selected");
            parts.Add($"{info.ChangedFileCount} local changes");

            CheckoutSummary = string.Join(" · ", parts) + _checkoutSummarySuffix;
        }

        // ── Manage: pending changes ───────────────────────────────────────────

        private void RecomputePendingChanges()
        {
            var selected = GetSelectedPaths();

            var plan = RemovalPlanner.Plan(_baselinePaths, selected, _allRootNodes);
            _addedPaths = plan.Added;
            _removedPaths = plan.Removed;
            _droppedTargets = plan.Dropped;
            _removalTargets = plan.Targets;

            var lines = _addedPaths.Select(p => "+ " + p)
                .Concat(_removalTargets.Select(p => "\u2212 " + p))
                .ToList();

            PendingChangesText = lines.Count == 0 ? "No changes" : string.Join(Environment.NewLine, lines);
            UpdateApplyEnabled();
            UpdateCheckoutSummary();

            SelectedPathsText = string.Join(Environment.NewLine, selected);
            RegenerateManageScript(selected);
        }

        /// <summary>
        /// Apply needs a loaded checkout this app can edit safely (cone mode, or not sparse yet) and
        /// something to do.
        /// </summary>
        private void UpdateApplyEnabled()
        {
            var info = CheckoutInfo;
            if (info == null || (info.IsSparse && !info.IsCone))
            {
                IsApplyEnabled = false;
                return;
            }

            IsApplyEnabled = !info.IsSparse || _addedPaths.Count > 0 || _removedPaths.Count > 0;
        }

        // ── Manage: recent checkouts ──────────────────────────────────────────

        private const int MaxRecentCheckouts = 10;

        public void RefreshRecentCheckoutState()
        {
            foreach (var entry in RecentCheckouts)
                entry.IsMissing = !Directory.Exists(Path.Combine(entry.Path, ".git"));
        }

        private void AddOrUpdateRecentCheckout(string path, string? remoteUrl, string? branch)
        {
            var stored = _appSettings.RecentCheckouts;
            stored.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            stored.Insert(0, new RecentCheckout
            {
                Path = path,
                RemoteUrl = remoteUrl,
                Branch = branch,
                LastOpenedUtc = DateTime.UtcNow
            });

            if (stored.Count > MaxRecentCheckouts)
                stored.RemoveRange(MaxRecentCheckouts, stored.Count - MaxRecentCheckouts);

            _settingsService.SaveSettings(_appSettings);
            RebuildRecentCheckouts(selectPath: CheckoutInfo?.Root);
        }

        private void RemoveRecentCheckout(RecentCheckout entry)
        {
            _appSettings.RecentCheckouts.RemoveAll(
                r => string.Equals(r.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            _settingsService.SaveSettings(_appSettings);
            RebuildRecentCheckouts(selectPath: CheckoutInfo?.Root);
        }

        private void RebuildRecentCheckouts(string? selectPath)
        {
            RecentCheckouts = new ObservableCollection<RecentCheckout>(_appSettings.RecentCheckouts);
            RefreshRecentCheckoutState();
            SelectedRecentCheckout = selectPath == null
                ? null
                : RecentCheckouts.FirstOrDefault(
                    r => string.Equals(r.Path, selectPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Handles a pick in the recent-checkouts dropdown. Driven by the control's selection event
        /// rather than the bound property, so re-picking the same entry is handled again.
        /// </summary>
        [RelayCommand]
        private void SelectRecentCheckout(RecentCheckout? entry)
        {
            if (entry == null || !IsManageMode) return;

            if (!Directory.Exists(Path.Combine(entry.Path, ".git")))
            {
                entry.IsMissing = true;
                StatusMessage = $"This checkout no longer exists: {entry.Path}";

                if (_dialogService.ShowConfirmation("Missing checkout", "Remove it from the recent list?"))
                    RemoveRecentCheckout(entry);
                else
                    RebuildRecentCheckouts(selectPath: CheckoutInfo?.Root);
                return;
            }

            if (string.Equals(_activeCheckoutPath, entry.Path, StringComparison.OrdinalIgnoreCase)) return;

            _ = OpenCheckoutAsync(entry.Path);
        }

        // ── Manage: auth for the checkout's remote ────────────────────────────

        /// <summary>
        /// Finds saved credentials for the host a checkout's remote points at. Returns null when no
        /// configured server matches, so a token is never offered to an unknown host.
        /// </summary>
        private GitAuth? ResolveAuthForRemote(string? remoteUrl)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl)) return null;
            if (!Uri.TryCreate(remoteUrl.Trim(), UriKind.Absolute, out var remote)) return null;

            foreach (var type in Enum.GetValues<GitHostType>())
            {
                var matches = type == GitHostType.GitHub &&
                              string.Equals(remote.Host, "github.com", StringComparison.OrdinalIgnoreCase);

                if (!matches &&
                    _appSettings.ServerUrls.TryGetValue(type.ToString(), out var savedUrl) &&
                    Uri.TryCreate(savedUrl?.Trim(), UriKind.Absolute, out var server))
                {
                    matches = string.Equals(server.Host, remote.Host, StringComparison.OrdinalIgnoreCase) &&
                              server.Port == remote.Port;
                }

                if (!matches) continue;

                var token = _settingsService.GetDecryptedToken(_appSettings, type);
                if (string.IsNullOrWhiteSpace(token)) continue;

                return new GitAuth(_hostServiceFactory.Create(type).GitHttpUsername, token, remoteUrl);
            }

            return null;
        }

        // ── Manage mode ───────────────────────────────────────────────────────

        [RelayCommand(CanExecute = nameof(HasValidScript))]
        private void SaveManageBat()
        {
            var path = _dialogService.ShowSaveFileDialog(
                "Batch files (*.bat)|*.bat|All files (*.*)|*.*", ".bat", "manage-sparse-checkout");
            if (path != null)
            {
                File.WriteAllText(path, GeneratedScript, Utf8NoBom);
                StatusMessage = $"Script saved to {path}";
            }
        }

        [RelayCommand(CanExecute = nameof(HasValidScript))]
        private void SaveManageSh()
        {
            var root = ManageRoot;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(Path.Combine(root, ".git")))
            {
                StatusMessage = "The selected path does not appear to be a Git repository (no .git folder found).";
                return;
            }

            RecomputePendingChanges();
            var paths = GetSelectedPaths();
            var script = _commandGenerator.GenerateManageShScript(root, paths, KeepWindowOpen, _removalTargets);
            var savePath = _dialogService.ShowSaveFileDialog(
                "Shell scripts (*.sh)|*.sh|All files (*.*)|*.*", ".sh", "manage-sparse-checkout");
            if (savePath != null)
            {
                File.WriteAllText(savePath, script);
                StatusMessage = $"Shell script saved to {savePath}";
            }
        }

        private bool CanApplyManage => IsApplyEnabled && HasValidScript;

        [RelayCommand(CanExecute = nameof(CanApplyManage))]
        private async Task ApplyManageAsync()
        {
            var info = CheckoutInfo;
            if (info == null)
            {
                StatusMessage = "Open a checkout first.";
                return;
            }

            if (info.IsSparse && !info.IsCone)
            {
                StatusMessage = "This checkout uses non-cone sparse patterns, which this app can't edit safely.";
                return;
            }

            var root = info.Root;
            RecomputePendingChanges();

            var selected = GetSelectedPaths();
            var added = new List<string>(_addedPaths);
            var removed = new List<string>(_removalTargets);
            var dropped = new List<string>(_droppedTargets);

            if (info.IsSparse && added.Count == 0 && _removedPaths.Count == 0)
            {
                StatusMessage = "Nothing to apply.";
                return;
            }

            IsLoading = true;
            try
            {
                RemovalReviewModel? review = null;
                if (removed.Count > 0)
                {
                    StatusMessage = "Checking what removal would delete…";
                    review = await BuildRemovalReviewAsync(root, removed);
                    if (review == null) return; // StatusMessage already carries the git error
                }

                RemovalReviewChoices choices;
                if (review == null || !review.HasAnyFiles)
                {
                    if (!_dialogService.ShowConfirmation(
                            "Apply sparse-checkout changes",
                            $"Add {added.Count} folder(s), remove {removed.Count} folder(s) from {root}?"))
                        return;

                    choices = new RemovalReviewChoices(false, false, false);
                }
                else
                {
                    var picked = _dialogService.ShowRemovalReview(review);
                    if (picked == null) return;
                    choices = picked;

                    var permanent = (choices.DeleteUntracked ? review.UntrackedFiles.Count : 0)
                                  + (choices.DeleteChanged ? review.ChangedFiles.Count : 0);

                    if (permanent > 0 && !_dialogService.ShowConfirmation(
                            "Confirm permanent deletion",
                            $"{permanent} changed/untracked file(s) in the removed folders will be permanently " +
                            "deleted from disk.\nThis cannot be undone. Continue?",
                            destructive: true))
                        return;
                }

                await ExecuteManageApplyAsync(root, info, selected, added, removed, dropped, review, choices);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExecuteManageApplyAsync(
            string root, CheckoutInfo info, List<string> selected, List<string> added, List<string> removed,
            List<string> dropped, RemovalReviewModel? review, RemovalReviewChoices choices)
        {
            LastApplyLog = string.Empty;
            CleanupLeftovers = new();

            // a. Changed files must go back to HEAD before git will let the folder leave the worktree.
            if (review != null && choices.DeleteChanged && review.ChangedFiles.Count > 0)
            {
                StatusMessage = "Discarding local changes in removed folders…";
                var restored = await RunPathBatchesAsync(root, review.ChangedFiles, null,
                    _ => new[] { "restore", "--source=HEAD", "--staged", "--worktree", "--" });

                if (!restored.Success) { StatusMessage = restored.Error; return; }
            }

            // b. Downloads blobs for newly added folders, so this is the only step that needs credentials.
            StatusMessage = "Downloading files for added folders…";
            var conePaths = DropFilePaths(selected, out var droppedFiles);
            var auth = ResolveAuthForRemote(info.RemoteUrl);
            var applied = await RunPathBatchesAsync(root, conePaths, auth,
                batch => new[] { "sparse-checkout", batch == 0 ? "set" : "add" });

            if (!applied.Success) { StatusMessage = applied.Error; return; }

            // sparse-checkout exits 0 even when it leaves files behind, so its warnings are the only signal.
            var warnings = applied.Warnings;

            // c. Clean whatever the user agreed to delete. -ff also removes nested repositories.
            var cleanArgs = (choices.DeleteIgnored, choices.DeleteUntracked) switch
            {
                (true, true) => new[] { "clean", "-ffdx", "--" },
                (true, false) => new[] { "clean", "-ffdX", "--" },
                (false, true) => new[] { "clean", "-ffd", "--" },
                _ => null
            };

            if (cleanArgs != null && removed.Count > 0)
            {
                StatusMessage = "Cleaning removed folders…";
                var cleaned = await RunPathBatchesAsync(root, removed, null, _ => cleanArgs);
                if (!cleaned.Success) { StatusMessage = cleaned.Error; return; }
            }

            // d. Git leaves the directory skeleton behind; drop the parts that hold nothing.
            foreach (var path in removed)
            {
                var full = ToFullPath(root, path);
                if (Directory.Exists(full)) DeleteEmptyDirectories(full);
            }

            // e. Last resort, and only when the user agreed to delete everything that was found.
            var everythingTicked = review == null ||
                ((review.IgnoredFiles.Count == 0 || choices.DeleteIgnored) &&
                 (review.UntrackedFiles.Count == 0 || choices.DeleteUntracked) &&
                 (review.ChangedFiles.Count == 0 || choices.DeleteChanged));

            var failures = new List<(string Path, string Reason)>();
            if (everythingTicked)
            {
                foreach (var path in removed)
                {
                    var full = ToFullPath(root, path);
                    if (!Directory.Exists(full)) continue;
                    if (!TryForceDeleteDirectory(full, out var reason))
                        failures.Add((path, reason));
                }
            }

            // Re-read everything from git so the report describes reality, not intent.
            StatusMessage = "Verifying…";
            await OpenCheckoutAsync(root);
            CleanupLeftovers = new ObservableCollection<(string Path, string Reason)>(failures);

            var failedPaths =failures.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keptPartly = removed
                .Where(p => Directory.Exists(ToFullPath(root, p)) && !failedPaths.Contains(p))
                .ToList();

            var parts = new List<string> { $"Applied: +{added.Count} added, \u2212{removed.Count} removed." };

            if (dropped.Count > 0)
                parts.Add($"Skipped (still selected, not deleted): {string.Join(", ", dropped)}.");

            if (keptPartly.Count > 0)
                parts.Add($"Kept partly (files you chose to keep): {string.Join(", ", keptPartly)}.");

            if (failures.Count > 0)
                parts.Add($"Could not delete: {string.Join(", ", failures.Select(f => $"{f.Path} ({f.Reason})"))}.");

            if (droppedFiles.Count > 0)
                parts.Add(DroppedFilePathsNote(droppedFiles).Trim());

            if (warnings.Count > 0)
            {
                parts.Add("(see warnings)");

                LastApplyLog = string.Join(Environment.NewLine, warnings);
            }

            StatusMessage = string.Join(" ", parts);
        }

        private bool CanDisableSparseCheckout() => CheckoutInfo?.IsSparse == true;

        [RelayCommand(CanExecute = nameof(CanDisableSparseCheckout))]
        private async Task DisableSparseCheckoutAsync()
        {
            var info = CheckoutInfo;
            if (info == null || !info.IsSparse) return;

            if (!_dialogService.ShowConfirmation(
                    "Disable sparse checkout", "This checks out ALL files of the repository. Continue?"))
                return;

            IsLoading = true;
            try
            {
                StatusMessage = "Disabling sparse checkout…";
                var auth = ResolveAuthForRemote(info.RemoteUrl);
                var result = await _gitService.RunAsync(new[] { "sparse-checkout", "disable" }, info.Root, auth);
                if (result.ExitCode != 0)
                {
                    StatusMessage = FirstLines(result.StdErr);
                    return;
                }

                await OpenCheckoutAsync(info.Root);
                StatusMessage = "Sparse checkout disabled. All files are checked out.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task RetryCleanupAsync()
        {
            var root = CheckoutInfo?.Root;
            if (string.IsNullOrWhiteSpace(root) || CleanupLeftovers.Count == 0) return;

            var leftovers = CleanupLeftovers.ToList();
            var remaining = new List<(string Path, string Reason)>();

            IsLoading = true;
            try
            {
                StatusMessage = "Retrying cleanup…";
                foreach (var (path, reason) in leftovers)
                {
                    var full = ToFullPath(root, path);
                    if (!Directory.Exists(full)) continue;
                    if (!TryForceDeleteDirectory(full, out var newReason))
                        remaining.Add((path, newReason));
                }

                await OpenCheckoutAsync(root);
                CleanupLeftovers = new ObservableCollection<(string Path, string Reason)>(remaining);

                var message = $"Cleaned up {leftovers.Count - remaining.Count} of {leftovers.Count}.";
                if (remaining.Count > 0)
                    message += $" Could not delete: {string.Join(", ", remaining.Select(f => $"{f.Path} ({f.Reason})"))}.";
                StatusMessage = message;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ── Manage: removal review ────────────────────────────────────────────

        /// <summary>
        /// Asks git what lives inside the folders about to be removed. Returns null when git failed, in
        /// which case <see cref="StatusMessage"/> already describes the problem.
        /// </summary>
        private async Task<RemovalReviewModel?> BuildRemovalReviewAsync(string root, IReadOnlyList<string> removed)
        {
            var leading = new[]
            {
                "status", "--porcelain=v1", "-z", "--ignored=matching", "--untracked-files=all", "--"
            };

            var ignored = new List<string>();
            var untracked = new List<string>();
            var changed = new List<string>();

            foreach (var chunk in ChunkPaths(removed, ReservedLength(root, leading)))
            {
                var args = new List<string>(leading);
                args.AddRange(chunk);

                var result = await _gitService.RunAsync(args, root);
                if (result.ExitCode != 0)
                {
                    StatusMessage = FirstLines(result.StdErr);
                    return null;
                }

                ParseStatusEntries(result.StdOut, ignored, untracked, changed);
            }

            ignored.Sort(StringComparer.OrdinalIgnoreCase);
            untracked.Sort(StringComparer.OrdinalIgnoreCase);
            changed.Sort(StringComparer.OrdinalIgnoreCase);

            return new RemovalReviewModel
            {
                RemovedFolders = new List<string>(removed),
                IgnoredFiles = ignored,
                UntrackedFiles = untracked,
                ChangedFiles = changed
            };
        }

        /// <summary>Splits NUL-separated <c>git status --porcelain=v1 -z</c> output into the three groups.</summary>
        private static void ParseStatusEntries(string stdOut, List<string> ignored, List<string> untracked,
            List<string> changed)
        {
            var entries = stdOut.Split('\0');

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.Length < 4) continue;

                var xy = entry[..2];
                var path = entry[3..];

                // A rename or copy stores the original path in the next entry.
                if (xy.Contains('R') || xy.Contains('C')) i++;

                if (xy == "!!") ignored.Add(path);
                else if (xy == "??") untracked.Add(path);
                else changed.Add(path);
            }
        }

        // ── Manage: running git over long path lists ──────────────────────────

        private sealed record GitBatchOutcome(bool Success, string Error, List<string> Warnings);

        /// <summary>Windows caps a command line well below this; stay clear of it with room to spare.</summary>
        private const int MaxCommandLineChars = 30_000;

        /// <summary>
        /// Runs one git command per batch of paths. <paramref name="leadingArgs"/> receives the batch index
        /// so a command can differ on continuation batches (for example <c>sparse-checkout set</c> then
        /// <c>add</c>).
        /// </summary>
        private async Task<GitBatchOutcome> RunPathBatchesAsync(string root, IReadOnlyList<string> paths,
            GitAuth? auth, Func<int, IReadOnlyList<string>> leadingArgs)
        {
            var warnings = new List<string>();
            var chunks = ChunkPaths(paths, ReservedLength(root, leadingArgs(0)));

            for (var i = 0; i < chunks.Count; i++)
            {
                var args = new List<string>(leadingArgs(i));
                args.AddRange(chunks[i]);

                var result = await _gitService.RunAsync(args, root, auth);

                warnings.AddRange(result.StdErr
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith("warning:", StringComparison.OrdinalIgnoreCase)));

                if (result.ExitCode != 0)
                    return new GitBatchOutcome(false, FirstLines(result.StdErr), warnings);
            }

            return new GitBatchOutcome(true, string.Empty, warnings);
        }

        private static int ReservedLength(string root, IReadOnlyList<string> leadingArgs) =>
            root.Length + leadingArgs.Sum(a => a.Length + 3) + 16;

        private static List<List<string>> ChunkPaths(IReadOnlyList<string> paths, int reserved)
        {
            var chunks = new List<List<string>>();
            var current = new List<string>();
            var length = reserved;

            foreach (var path in paths)
            {
                var cost = path.Length + 3; // quotes and a separating space
                if (current.Count > 0 && length + cost > MaxCommandLineChars)
                {
                    chunks.Add(current);
                    current = new List<string>();
                    length = reserved;
                }

                current.Add(path);
                length += cost;
            }

            if (current.Count > 0) chunks.Add(current);
            if (chunks.Count == 0) chunks.Add(new List<string>());
            return chunks;
        }

        // ── Manage: disk clean-up helpers ─────────────────────────────────────

        private static string ToFullPath(string root, string relativePath) =>
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Deletes directories that hold nothing, deepest first, leaving any file untouched.</summary>
        private static void DeleteEmptyDirectories(string directory)
        {
            try
            {
                foreach (var sub in Directory.GetDirectories(directory))
                    DeleteEmptyDirectories(sub);

                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, recursive: false);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool TryForceDeleteDirectory(string directory, out string reason)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }

                Directory.Delete(directory, recursive: true);
                reason = string.Empty;
                return true;
            }
            catch (IOException)
            {
                reason = "file in use";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                reason = "access denied";
                return false;
            }
        }

        /// <summary>Condenses git's stderr for the status bar. Credentials never travel through stderr.</summary>
        private static string FirstLines(string text, int max = 3)
        {
            var lines = (text ?? string.Empty)
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Take(max)
                .ToList();

            return lines.Count == 0 ? "The git command failed." : string.Join(" ", lines);
        }

        // ── Presets ───────────────────────────────────────────────────────────────

        [RelayCommand(CanExecute = nameof(CanSavePreset))]
        private void SavePreset()
        {
            var paths = GetSelectedPaths();
            if (paths.Count == 0)
            {
                StatusMessage = "No paths selected. Check folders in the tree first.";
                return;
            }

            var name = _dialogService.ShowInputDialog("Save Preset", "Enter a name for this preset:");
            if (string.IsNullOrWhiteSpace(name)) return;

            var existing = AvailablePresets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!_dialogService.ShowConfirmation("Overwrite Preset", $"Overwrite preset \"{existing.Name}\"?"))
                    return;
                existing.Paths = paths;
            }
            else
            {
                existing = new TreePreset { Name = name, Paths = paths };
                AvailablePresets.Add(existing);
            }

            _presetService.SavePresets(PresetKey, AvailablePresets);
            SelectedPreset = existing;
            StatusMessage = $"Preset \"{name}\" saved ({paths.Count} path(s)).";
        }

        private bool CanSavePreset() => !string.IsNullOrWhiteSpace(PresetKey) && _allRootNodes.Count > 0;

        [RelayCommand(CanExecute = nameof(CanModifyPreset))]
        private void LoadPreset()
        {
            if (SelectedPreset == null) return;
            var (matched, skippedFiles) = ApplyPresetToTree(SelectedPreset.Paths);
            StatusMessage = $"Preset \"{SelectedPreset.Name}\" applied — {matched} of {SelectedPreset.Paths.Count} path(s) matched.{SkippedFilesNote(skippedFiles)}";
        }

        [RelayCommand(CanExecute = nameof(CanModifyPreset))]
        private void RenamePreset()
        {
            if (SelectedPreset == null) return;
            var newName = _dialogService.ShowInputDialog("Rename Preset", "New name:", SelectedPreset.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == SelectedPreset.Name) return;
            if (AvailablePresets.Any(p => string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = $"A preset named \"{newName}\" already exists.";
                return;
            }
            SelectedPreset.Name = newName;
            _presetService.SavePresets(PresetKey, AvailablePresets);
            StatusMessage = $"Preset renamed to \"{newName}\".";
        }

        [RelayCommand(CanExecute = nameof(CanModifyPreset))]
        private void DeletePreset()
        {
            if (SelectedPreset == null) return;
            if (!_dialogService.ShowConfirmation("Delete Preset", $"Delete preset \"{SelectedPreset.Name}\"?", destructive: true))
                return;
            var name = SelectedPreset.Name;
            AvailablePresets.Remove(SelectedPreset);
            SelectedPreset = null;
            _presetService.SavePresets(PresetKey, AvailablePresets);
            StatusMessage = $"Preset \"{name}\" deleted.";
        }

        private bool CanModifyPreset() => SelectedPreset != null;

        // ── Helpers ───────────────────────────────────────────────────────────

        private static List<TreeNodeViewModel> BuildTree(List<TreeNode> flat)
        {
            var roots = new List<TreeNodeViewModel>();
            var map = new Dictionary<string, TreeNodeViewModel>(StringComparer.Ordinal);

            // Sort ensures parents precede their children
            var sorted = flat.OrderBy(n => n.Path, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var node in sorted)
            {
                var slash = node.Path.LastIndexOf('/');
                if (slash < 0)
                {
                    var vm = new TreeNodeViewModel(node);
                    roots.Add(vm);
                    map[node.Path] = vm;
                }
                else
                {
                    var parentPath = node.Path[..slash];
                    if (map.TryGetValue(parentPath, out var parent))
                    {
                        var vm = new TreeNodeViewModel(node, parent);
                        parent.Children.Add(vm);
                        map[node.Path] = vm;
                    }
                    else
                    {
                        // Parent folder not in API response – attach to root
                        var vm = new TreeNodeViewModel(node);
                        roots.Add(vm);
                        map[node.Path] = vm;
                    }
                }
            }

            SortRootNodesForDisplay(roots);

            return roots;
        }

        private static void SortRootNodesForDisplay(List<TreeNodeViewModel> roots)
        {
            roots.Sort(CompareForExplorerDisplay);
            foreach (var root in roots)
                SortChildrenForDisplay(root);
        }

        private static void SortChildrenForDisplay(TreeNodeViewModel parent)
        {
            var sorted = parent.Children.ToList();
            sorted.Sort(CompareForExplorerDisplay);

            if (!sorted.SequenceEqual(parent.Children))
            {
                parent.Children.Clear();
                foreach (var child in sorted)
                    parent.Children.Add(child);
            }

            foreach (var child in parent.Children)
                SortChildrenForDisplay(child);
        }

        private static int CompareForExplorerDisplay(TreeNodeViewModel? x, TreeNodeViewModel? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return 1;
            if (y is null) return -1;

            // Explorer-style grouping: folders before files.
            if (x.IsFolder != y.IsFolder)
                return x.IsFolder ? -1 : 1;

            // Natural, case-insensitive name ordering close to Explorer.
            var byName = StrCmpLogicalW(x.Name, y.Name);
            if (byName != 0) return byName;

            return StringComparer.OrdinalIgnoreCase.Compare(x.FullPath, y.FullPath);
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string left, string right);

        private List<string> GetSelectedPaths()
        {
            var paths = new List<string>();
            foreach (var node in _allRootNodes)
                paths.AddRange(node.GetCheckedPaths());
            return paths;
        }

        private const int SearchDebounceMs = 250;
        private CancellationTokenSource? _searchCts;
        private Dictionary<TreeNodeViewModel, bool>? _expansionSnapshot;

        private async void DebounceApplyFilter(string filter)
        {
            _searchCts?.Cancel();
            var cts = _searchCts = new CancellationTokenSource();
            try
            {
                await Task.Delay(SearchDebounceMs, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            ApplyFilter(filter);
        }

        private void ApplyFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                TreeSearch.Clear(_allRootNodes);
                if (_expansionSnapshot != null)
                {
                    TreeSearch.RestoreExpansion(_expansionSnapshot);
                    _expansionSnapshot = null;
                }
                SearchResultText = string.Empty;
                return;
            }

            // Search is starting: remember how the tree was expanded so clearing can put it back
            _expansionSnapshot ??= TreeSearch.CaptureExpansion(_allRootNodes);

            var result = TreeSearch.Apply(_allRootNodes, filter);
            SearchResultText = result.MatchCount == 0 ? "No matches"
                : !result.Expanded ? $"{result.MatchCount} matches — keep typing to narrow it down"
                : result.MatchCount == 1 ? "1 match"
                : $"{result.MatchCount} matches";
        }

        private void LoadPresetsForCurrentScan()
        {
            AvailablePresets.Clear();
            SelectedPreset = null;
            var key = PresetKey;
            if (string.IsNullOrWhiteSpace(key)) return;
            foreach (var p in _presetService.GetPresets(key))
                AvailablePresets.Add(p);
        }

        private (int Matched, int SkippedFiles) ApplyPresetToTree(List<string> paths)
        {
            foreach (var root in _allRootNodes)
                root.IsChecked = false;

            var map = new Dictionary<string, TreeNodeViewModel>(StringComparer.Ordinal);
            BuildFlatPathMap(_allRootNodes, map);

            var matched = 0;
            var skippedFiles = 0;
            foreach (var path in paths)
            {
                if (!map.TryGetValue(path, out var vm)) continue;

                // Presets saved by older versions may still hold file paths, which cone mode rejects.
                if (!vm.IsFolder) { skippedFiles++; continue; }

                vm.IsChecked = true;
                matched++;
            }
            return (matched, skippedFiles);
        }

        private static void BuildFlatPathMap(IEnumerable<TreeNodeViewModel> nodes, Dictionary<string, TreeNodeViewModel> map)
        {
            foreach (var node in nodes)
            {
                if (string.IsNullOrEmpty(node.FullPath)) continue;
                map[node.FullPath] = node;
                BuildFlatPathMap(node.Children, map);
            }
        }

        /// <summary>Checks the tree to match a sparse list, returning how many entries were files.</summary>
        private int ApplySparseCheckoutState(List<string> checkedPaths)
        {
            var pathSet = new HashSet<string>(checkedPaths, StringComparer.Ordinal);
            var map = new Dictionary<string, TreeNodeViewModel>(StringComparer.Ordinal);
            BuildFlatPathMap(_allRootNodes, map);

            var skippedFiles = 0;
            foreach (var (path, vm) in map)
            {
                if (!pathSet.Contains(path)) continue;

                if (vm.IsFolder) vm.IsChecked = true;
                else skippedFiles++;
            }

            return skippedFiles;
        }

        private static string SkippedFilesNote(int skippedFiles) =>
            skippedFiles == 0
                ? string.Empty
                : $" {skippedFiles} file path(s) skipped \u2014 only folders can be selected.";

        /// <summary>
        /// Drops paths that resolve to a file in the current tree. Cone mode only accepts folders and
        /// fails the whole command with "is not a directory" on the first file it sees.
        /// </summary>
        private List<string> DropFilePaths(IEnumerable<string> paths, out List<string> dropped)
        {
            var map = new Dictionary<string, TreeNodeViewModel>(StringComparer.Ordinal);
            BuildFlatPathMap(_allRootNodes, map);

            var kept = new List<string>();
            dropped = new List<string>();

            foreach (var path in paths)
            {
                if (map.TryGetValue(path, out var node) && !node.IsFolder) dropped.Add(path);
                else kept.Add(path);
            }

            return kept;
        }

        private static string DroppedFilePathsNote(List<string> dropped)
        {
            if (dropped.Count == 0) return string.Empty;

            var shown = string.Join(", ", dropped.Take(3));
            if (dropped.Count > 3) shown += ", \u2026";
            return $" Skipped {dropped.Count} file path(s) that cone mode cannot accept: {shown}.";
        }

    }
}
