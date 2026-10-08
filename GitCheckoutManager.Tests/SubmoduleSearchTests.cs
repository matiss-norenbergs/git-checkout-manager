using GitCheckoutManager.Models;
using GitCheckoutManager.Services;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Tests;

/// <summary>Submodules window: search box, header select-all and the hidden-tick count (no git involved).</summary>
public class SubmoduleSearchTests
{
    private static SubmoduleInfo Info(string displayPath, string? name = null, SubmoduleState state = SubmoduleState.Ready,
        string? branch = "main") =>
        new(displayPath, name, "https://example.com/x.git", null, "aaaaaaaa", state == SubmoduleState.NotInitialized ? null : "aaaaaaaa",
            state, DisplayPath: displayPath, CurrentBranch: state == SubmoduleState.NotInitialized ? null : branch);

    private static readonly SubmoduleInfo[] Standard =
    {
        Info("vendor/Alpha", "vendor/Alpha"),
        Info("vendor/beta", "beta-lib", SubmoduleState.NotInitialized),
        Info("libs/core", "core"),
        Info("libs/core/vendor/nested", "nested"),
        Info("tools/outside", "outside", SubmoduleState.OutsideCheckout),
        Info("tools/broken", "broken", SubmoduleState.MissingFromGitmodules),
    };

    private static async Task<(SubmodulesViewModel Vm, FakeService Svc)> Open(params SubmoduleInfo[] infos)
    {
        var svc = new FakeService(infos.Length == 0 ? Standard : infos);
        var vm = new SubmodulesViewModel(svc, "root", _ => null, new FakeDialogs(), new AppSettings(), new NullSettings());
        await vm.RefreshAsync();
        return (vm, svc);
    }

    private static string[] Shown(SubmodulesViewModel vm) => vm.Rows.Select(r => r.DisplayPath).ToArray();

    // ── Search ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_matches_path_and_name_case_insensitively()
    {
        var (vm, _) = await Open();

        vm.SearchText = "ALPHA";
        Assert.Equal(new[] { "vendor/Alpha" }, Shown(vm));

        vm.SearchText = "beta-LIB"; // name only
        Assert.Equal(new[] { "vendor/beta" }, Shown(vm));
    }

    [Fact]
    public async Task Search_shows_nested_rows_by_full_path_without_their_parent()
    {
        var (vm, _) = await Open();

        vm.SearchText = "core/vendor";

        Assert.Equal(new[] { "libs/core/vendor/nested" }, Shown(vm));
    }

    [Fact]
    public async Task Search_combines_with_the_existing_filters()
    {
        var (vm, _) = await Open();

        vm.SearchText = "tools/";
        Assert.Equal(new[] { "tools/broken" }, Shown(vm)); // outside checkout stays hidden

        vm.ShowOutsideCheckout = true;
        Assert.Equal(new[] { "tools/outside", "tools/broken" }, Shown(vm));

        vm.SearchText = "libs/core";
        vm.ShowOnlyProblems = true; // Ready rows are hidden
        Assert.Empty(Shown(vm));
        vm.ShowOnlyProblems = false;
    }

    [Fact]
    public async Task Search_reports_counts_and_the_no_match_text()
    {
        var (vm, _) = await Open();
        Assert.Equal(string.Empty, vm.SearchCountText);

        vm.SearchText = "vendor";
        Assert.Equal("Showing 3 of 5", vm.SearchCountText); // 5 rows pass the other filters (outside is hidden)

        vm.SearchText = "zzz";
        Assert.Equal("Showing 0 of 5", vm.SearchCountText);
        Assert.Equal("No submodules match \"zzz\".", vm.EmptyText);

        vm.SearchText = string.Empty;
        Assert.Equal(string.Empty, vm.SearchCountText);
        Assert.Equal(string.Empty, vm.EmptyText);
    }

    [Theory]
    [InlineData("alpha, core")]
    [InlineData("  alpha  ,core  ")]
    [InlineData("alpha,,core")]
    [InlineData("alpha, , core,")]
    public async Task Comma_separated_terms_match_with_or_and_ignore_blanks(string text)
    {
        var (vm, _) = await Open();

        vm.SearchText = text;

        Assert.Equal(new[] { "vendor/Alpha", "libs/core", "libs/core/vendor/nested" }, Shown(vm));
    }

    [Fact]
    public async Task Only_commas_and_spaces_is_no_search()
    {
        var (vm, _) = await Open();

        vm.SearchText = " , ,";

        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal(string.Empty, vm.SearchCountText);
    }

    // ── Header checkbox ──────────────────────────────────────────────────

    [Fact]
    public async Task Header_state_follows_the_shown_selectable_rows_and_ignores_the_rest()
    {
        var (vm, _) = await Open();
        // Selectable: Alpha, beta, core, nested (broken is shown but cannot be ticked).
        Assert.Equal(false, vm.HeaderSelectAll);
        Assert.True(vm.HeaderSelectAllEnabled);

        vm.Rows.Single(r => r.DisplayPath == "libs/core").IsSelected = true;
        Assert.Null(vm.HeaderSelectAll);

        foreach (var row in vm.Rows.Where(r => r.CanSelect)) row.IsSelected = true;
        Assert.True(vm.HeaderSelectAll);
    }

    [Fact]
    public async Task Header_is_disabled_and_unticked_when_no_selectable_row_is_shown()
    {
        var (vm, _) = await Open();

        vm.SearchText = "broken";

        Assert.Single(vm.Rows);
        Assert.Equal(false, vm.HeaderSelectAll);
        Assert.False(vm.HeaderSelectAllEnabled);
    }

    [Fact]
    public async Task Header_state_updates_when_the_search_changes()
    {
        var (vm, _) = await Open();
        vm.Rows.Single(r => r.DisplayPath == "vendor/Alpha").IsSelected = true;
        Assert.Null(vm.HeaderSelectAll);

        vm.SearchText = "alpha";
        Assert.True(vm.HeaderSelectAll);

        vm.SearchText = "core";
        Assert.Equal(false, vm.HeaderSelectAll);
    }

    [Fact]
    public async Task Header_toggle_ticks_and_unticks_only_the_shown_rows()
    {
        var (vm, _) = await Open();
        vm.Rows.Single(r => r.DisplayPath == "libs/core").IsSelected = true; // will be hidden
        vm.SearchText = "vendor/";
        Assert.Equal(false, vm.HeaderSelectAll);

        // Middle state first: tick one of the two shown, then toggle.
        vm.Rows.First(r => r.CanSelect).IsSelected = true;
        Assert.Null(vm.HeaderSelectAll);
        vm.HeaderSelectAll = true; // what a click on the middle state produces
        Assert.True(vm.HeaderSelectAll);
        Assert.All(vm.Rows.Where(r => r.CanSelect), r => Assert.True(r.IsSelected));

        vm.HeaderSelectAll = false;
        Assert.Equal(false, vm.HeaderSelectAll);
        Assert.All(vm.Rows, r => Assert.False(r.IsSelected));

        vm.SearchText = string.Empty;
        Assert.True(vm.Rows.Single(r => r.DisplayPath == "libs/core").IsSelected); // hidden tick untouched
    }

    [Fact]
    public async Task Header_tooltip_names_the_next_action()
    {
        var (vm, _) = await Open();
        Assert.Equal("Select all shown", vm.HeaderSelectAllToolTip);

        vm.HeaderSelectAll = true;
        Assert.Equal("Clear selection of shown rows", vm.HeaderSelectAllToolTip);

        vm.Rows.First(r => r.CanSelect).IsSelected = false;
        Assert.Equal("Select all shown", vm.HeaderSelectAllToolTip); // middle → next click ticks all
    }

    // ── Selection text and bulk actions ─────────────────────────────────

    [Fact]
    public async Task Selection_text_counts_ticked_rows_hidden_by_the_search()
    {
        var (vm, _) = await Open();
        vm.Rows.Single(r => r.DisplayPath == "libs/core").IsSelected = true;
        vm.Rows.Single(r => r.DisplayPath == "vendor/Alpha").IsSelected = true;
        Assert.Equal("2 selected", vm.SelectedText);

        vm.SearchText = "alpha";

        Assert.Equal("1 selected (1 hidden)", vm.SelectedText);
        Assert.Equal(1, vm.SelectedCount);

        vm.SearchText = string.Empty;
        Assert.Equal("2 selected", vm.SelectedText);
    }

    [Fact]
    public async Task Pull_all_with_nothing_ticked_acts_on_shown_rows_only()
    {
        var (vm, svc) = await Open();
        vm.SearchText = "vendor/";

        await vm.PullSelectedCommand.ExecuteAsync(null);

        // beta is not initialized (cannot pull); nested is shown and populated.
        Assert.Equal(new[] { "vendor/Alpha", "libs/core/vendor/nested" }, svc.Pulled);
    }

    [Fact]
    public async Task Bulk_actions_ignore_ticked_rows_hidden_by_the_search()
    {
        var (vm, svc) = await Open();
        vm.Rows.Single(r => r.DisplayPath == "libs/core").IsSelected = true;
        vm.SearchText = "alpha";
        vm.Rows.Single().IsSelected = true;

        await vm.PullSelectedCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "vendor/Alpha" }, svc.Pulled);
    }

    [Fact]
    public async Task Select_all_with_problems_ticks_shown_rows_only()
    {
        var (vm, _) = await Open(
            Info("vendor/a", "a", SubmoduleState.NotInitialized),
            Info("libs/b", "b", SubmoduleState.NotInitialized));
        vm.SearchText = "vendor";

        vm.SelectAllWithProblemsCommand.Execute(null);
        vm.SearchText = string.Empty;

        Assert.Equal(new[] { "vendor/a" }, vm.Rows.Where(r => r.IsSelected).Select(r => r.DisplayPath));
    }

    [Fact]
    public async Task Header_is_disabled_while_a_run_is_active()
    {
        var gate = new TaskCompletionSource();
        var (vm, svc) = await Open();
        svc.PullGate = gate.Task;
        vm.SearchText = "alpha";

        var run = vm.PullSelectedCommand.ExecuteAsync(null);
        Assert.True(vm.IsRunning);
        Assert.False(vm.HeaderSelectAllEnabled);

        gate.SetResult();
        await run;
        Assert.False(vm.IsRunning);
        Assert.True(vm.HeaderSelectAllEnabled);
    }

    [Fact]
    public async Task Ticks_of_hidden_rows_survive_the_reload_after_a_run()
    {
        var (vm, _) = await Open();
        vm.Rows.Single(r => r.DisplayPath == "libs/core").IsSelected = true;
        vm.SearchText = "alpha";

        await vm.PullSelectedCommand.ExecuteAsync(null); // nothing shown is ticked → Pull all on shown rows

        vm.SearchText = string.Empty;
        Assert.True(vm.Rows.Single(r => r.DisplayPath == "libs/core").IsSelected);
    }

    // ── Fakes ────────────────────────────────────────────────────────────

    private sealed class FakeService : ISubmoduleService
    {
        private readonly List<SubmoduleInfo> _infos;
        public FakeService(IEnumerable<SubmoduleInfo> infos) => _infos = infos.ToList();
        public List<string> Pulled { get; } = new();
        public Task? PullGate { get; set; }

        public Task<List<SubmoduleInfo>> ListAsync(string root, CancellationToken ct = default) => Task.FromResult(_infos.ToList());

        public Task<SubmoduleInfo?> GetAsync(string root, string path, CancellationToken ct = default, string displayPrefix = "", int depth = 0) =>
            Task.FromResult(_infos.FirstOrDefault(i => i.Path == path));

        public Task<SwitchCheck> CheckSwitchSafetyAsync(SubmoduleInfo sub, CancellationToken ct = default) =>
            Task.FromResult(new SwitchCheck(Array.Empty<string>(), false));

        public async Task<SwitchResult> SwitchBranchAsync(SubmoduleInfo sub, string branch, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default)
        {
            if (PullGate != null) await PullGate;
            Pulled.Add(sub.DisplayPath);
            return new SwitchResult(0, string.Empty, string.Empty);
        }

        public Task<GitResult> InitAndUpdateAsync(string root, SubmoduleInfo sub, bool latestFromBranch, bool includeNested, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GitResult> TestUrlAsync(string url, GitAuth? auth, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GitResult> SetUrlAndInitAsync(string root, SubmoduleInfo sub, string newUrl, bool latestFromBranch, bool includeNested, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GitResult> ResetUrlAsync(string root, SubmoduleInfo sub, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GitResult> CloneManuallyAsync(string root, SubmoduleInfo sub, string url, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<string>> ListRemoteBranchesAsync(SubmoduleInfo sub, Func<string, GitAuth?> resolveAuth, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GitResult> ResetToRecordedAsync(SubmoduleInfo sub, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NullSettings : ISettingsService
    {
        public AppSettings LoadSettings() => new();
        public void SaveSettings(AppSettings settings) { }
        public string? GetDecryptedToken(AppSettings settings) => null;
        public void SetToken(AppSettings settings, string token) { }
        public string? GetDecryptedToken(AppSettings settings, GitHostType hostType) => null;
        public void SetToken(AppSettings settings, GitHostType hostType, string token) { }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ShowConfirmation(string title, string message, string? details = null, bool destructive = false) => true;
        public void ShowMessage(string title, string message, string? details = null) { }
        public ThreeWayChoice ShowThreeWayChoice(string title, string message, string primary, string? secondary = null) => throw new NotSupportedException();
        public void ShowLocalChanges(LocalChangesViewModel vm) => throw new NotSupportedException();
        public string? ShowSaveFileDialog(string filter, string defaultExtension, string defaultFileName) => throw new NotSupportedException();
        public string? ShowOpenFileDialog(string filter, string title = "Open File") => throw new NotSupportedException();
        public string? ShowOpenFolderDialog(string title = "Select Folder", string? initialPath = null) => throw new NotSupportedException();
        public string? ShowInputDialog(string title, string prompt, string defaultValue = "") => throw new NotSupportedException();
        public RemovalReviewChoices? ShowRemovalReview(RemovalReviewModel model) => throw new NotSupportedException();
        public void ShowSettings(SettingsViewModel viewModel) => throw new NotSupportedException();
        public void ShowSubmodules(SubmodulesViewModel vm) => throw new NotSupportedException();
        public string? ShowSubmoduleUrl(SubmoduleUrlModel model) => throw new NotSupportedException();
        public string? ShowSubmoduleBranch(SubmoduleBranchModel model) => throw new NotSupportedException();
    }
}
