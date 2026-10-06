using GitCheckoutManager.Models;
using GitCheckoutManager.Services;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Tests;

public class LocalChangesViewModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "repo");

    private static LocalChange C(string path, LocalChangeGroup g, string? orig = null, string? hint = null) =>
        new(path, orig, g, g.ToString(), hint != null, hint);

    private static LocalChangeSet Many(LocalChangeGroup g, int n, string prefix = "f") =>
        new(Enumerable.Range(0, n).Select(i => C($"{prefix}{i:D5}.txt", g)));

    [Fact]
    public void Groups_follow_the_display_order_and_skip_empty_ones()
    {
        var set = new LocalChangeSet(new[]
        {
            C("u.txt", LocalChangeGroup.Untracked), C("r.txt", LocalChangeGroup.Renamed, "o.txt"),
            C("d.txt", LocalChangeGroup.Deleted), C("m.txt", LocalChangeGroup.Modified),
            C("s.txt", LocalChangeGroup.Staged), C("c.txt", LocalChangeGroup.Conflicted),
        });

        var (groups, more) = LocalChangesViewModel.BuildGroups(Root, set, 2000);

        Assert.Equal(0, more);
        Assert.Equal(new[] { "Conflicted", "Staged", "Modified", "Deleted", "Renamed", "Untracked" },
            groups.Select(g => g.Title));

        var (only, _) = LocalChangesViewModel.BuildGroups(Root, Many(LocalChangeGroup.Modified, 2), 2000);
        Assert.Equal("Modified", Assert.Single(only).Title);
    }

    [Fact]
    public void Rows_show_relative_path_and_label_with_the_full_path_in_the_tooltip()
    {
        var set = new LocalChangeSet(new[] { C("dir/a b.txt", LocalChangeGroup.Modified) });

        var row = Assert.Single(Assert.Single(LocalChangesViewModel.BuildGroups(Root, set, 10).Groups).Rows);

        Assert.Equal("dir/a b.txt", row.Path);
        Assert.Equal("Modified", row.Label);
        Assert.Equal(Path.GetFullPath(Path.Combine(Root, "dir", "a b.txt")), row.ToolTip);
    }

    [Fact]
    public void Rename_row_shows_the_original_path()
    {
        var set = new LocalChangeSet(new[] { C("new.txt", LocalChangeGroup.Renamed, "old.txt") });

        var row = Assert.Single(Assert.Single(LocalChangesViewModel.BuildGroups(Root, set, 10).Groups).Rows);

        Assert.Equal("from old.txt", row.Detail);
        Assert.Contains("old.txt", row.ToolTip);
    }

    [Fact]
    public void More_than_the_cap_lists_the_first_rows_in_group_order_and_counts_the_rest()
    {
        var set = new LocalChangeSet(
            Many(LocalChangeGroup.Staged, 1500, "s").Changes.Concat(Many(LocalChangeGroup.Untracked, 1000, "u").Changes));

        var (groups, more) = LocalChangesViewModel.BuildGroups(Root, set, 2000);

        Assert.Equal(500, more);
        Assert.Equal(2000, groups.Sum(g => g.Rows.Count));
        Assert.Equal(1500, groups[0].Rows.Count);
        Assert.Equal(500, groups[1].Rows.Count);
        // The title keeps the real total even though only some rows are listed.
        Assert.Equal("Untracked (1000)", groups[1].Header);
    }

    [Fact]
    public void Exactly_the_cap_shows_no_more_text_and_one_over_shows_one_more()
    {
        var vm = new LocalChangesViewModel("r", Many(LocalChangeGroup.Modified, LocalChangesViewModel.MaxRows),
            _ => Task.FromResult(LocalChangeSet.Empty));
        Assert.False(vm.HasMore);
        Assert.Equal("2000 files changed: 2000 modified", vm.HeaderText);

        var over = new LocalChangesViewModel("r", Many(LocalChangeGroup.Modified, LocalChangesViewModel.MaxRows + 1),
            _ => Task.FromResult(LocalChangeSet.Empty));
        Assert.True(over.HasMore);
        Assert.Equal("…and 1 more", over.MoreText);
        Assert.Equal("2001 files changed: 2001 modified", over.HeaderText);
    }

    [Fact]
    public async Task Refresh_replaces_the_list_and_reports_the_new_set()
    {
        LocalChangeSet? reported = null;
        var fresh = Many(LocalChangeGroup.Untracked, 3);
        var vm = new LocalChangesViewModel("r", Many(LocalChangeGroup.Modified, 1),
            _ => Task.FromResult(fresh), s => reported = s);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Same(fresh, reported);
        Assert.Equal("3 files changed: 3 untracked", vm.HeaderText);
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task Refresh_failure_shows_an_error_and_keeps_the_old_list()
    {
        var vm = new LocalChangesViewModel("r", Many(LocalChangeGroup.Modified, 2),
            _ => throw new InvalidOperationException("boom"));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Contains("boom", vm.ErrorText);
        Assert.Equal("2 files changed: 2 modified", vm.HeaderText);
    }

    [Fact]
    public async Task Closing_the_window_cancels_a_running_status_without_reporting()
    {
        var started = new TaskCompletionSource();
        var reported = false;
        var vm = new LocalChangesViewModel("r", LocalChangeSet.Empty, async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Many(LocalChangeGroup.Modified, 1);
        }, _ => reported = true);

        var run = vm.RefreshCommand.ExecuteAsync(null);
        await started.Task;
        vm.Cancel();
        await run;

        Assert.False(reported);
        Assert.False(vm.HasError);
        Assert.False(vm.IsLoading);
    }
}
