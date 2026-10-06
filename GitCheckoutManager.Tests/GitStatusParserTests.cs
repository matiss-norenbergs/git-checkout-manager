using GitCheckoutManager.Models;
using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

public class GitStatusParserTests
{
    private const string H = "1111111111111111111111111111111111111111";

    private static string Z(params string[] tokens) => string.Join("\0", tokens) + "\0";

    private static string Ordinary(string xy, string path, string sub = "N...", string mH = "100644",
        string mI = "100644", string mW = "100644") =>
        $"1 {xy} {sub} {mH} {mI} {mW} {H} {H} {path}";

    private static string Renamed(string xy, string kind, string path, string sub = "N...") =>
        $"2 {xy} {sub} 100644 100644 100644 {H} {H} {kind}100 {path}";

    private static string Unmerged(string xy, string path, string sub = "N...", string m = "100644") =>
        $"u {xy} {sub} {m} {m} {m} {m} {H} {H} {H} {path}";

    private static LocalChange Single(string output)
    {
        var set = GitStatusParser.Parse(output);
        return Assert.Single(set.Changes);
    }

    [Theory]
    [InlineData("M.", LocalChangeGroup.Staged, "Staged (modified)")]
    [InlineData("A.", LocalChangeGroup.Staged, "Added")]
    [InlineData("T.", LocalChangeGroup.Staged, "Staged (type changed)")]
    [InlineData("D.", LocalChangeGroup.Deleted, "Deleted (staged)")]
    [InlineData(".M", LocalChangeGroup.Modified, "Modified")]
    [InlineData(".T", LocalChangeGroup.Modified, "Type changed")]
    [InlineData(".D", LocalChangeGroup.Deleted, "Deleted")]
    [InlineData(".A", LocalChangeGroup.Staged, "Intent to add")]
    public void Ordinary_entries_are_grouped_and_labelled(string xy, LocalChangeGroup group, string label)
    {
        var c = Single(Z(Ordinary(xy, "a.txt")));

        Assert.Equal("a.txt", c.Path);
        Assert.Null(c.OriginalPath);
        Assert.Equal(group, c.Group);
        Assert.Equal(label, c.Label);
        Assert.False(c.IsSubmodule);
    }

    [Theory]
    [InlineData("MM", LocalChangeGroup.Staged, "Staged + modified")]
    [InlineData("AM", LocalChangeGroup.Staged, "Added + modified")]
    [InlineData("MD", LocalChangeGroup.Deleted, "Staged + deleted")]
    [InlineData("AD", LocalChangeGroup.Deleted, "Added + deleted")]
    public void Staged_and_unstaged_gets_one_combined_row(string xy, LocalChangeGroup group, string label)
    {
        var c = Single(Z(Ordinary(xy, "a.txt")));

        Assert.Equal(group, c.Group);
        Assert.Equal(label, c.Label);
    }

    [Fact]
    public void Rename_keeps_both_paths()
    {
        var c = Single(Z(Renamed("R.", "R", "new name.txt"), "old name.txt"));

        Assert.Equal("new name.txt", c.Path);
        Assert.Equal("old name.txt", c.OriginalPath);
        Assert.Equal(LocalChangeGroup.Renamed, c.Group);
        Assert.Equal("Renamed", c.Label);
    }

    [Fact]
    public void Copy_and_renamed_plus_modified()
    {
        var set = GitStatusParser.Parse(Z(
            Renamed("C.", "C", "copy.txt"), "orig.txt",
            Renamed("RM", "R", "moved.txt"), "before.txt"));

        var copy = set.Changes.Single(c => c.Path == "copy.txt");
        Assert.Equal("Copied", copy.Label);
        Assert.Equal("orig.txt", copy.OriginalPath);

        var moved = set.Changes.Single(c => c.Path == "moved.txt");
        Assert.Equal(LocalChangeGroup.Renamed, moved.Group);
        Assert.Equal("Renamed + modified", moved.Label);
        Assert.Equal("before.txt", moved.OriginalPath);
    }

    [Fact]
    public void Entry_after_a_rename_is_not_swallowed_as_its_original_path()
    {
        var set = GitStatusParser.Parse(Z(Renamed("R.", "R", "b.txt"), "a.txt", "? c.txt", Ordinary(".M", "d.txt")));

        Assert.Equal(new[] { "b.txt", "c.txt", "d.txt" }, set.Changes.Select(c => c.Path).OrderBy(p => p));
    }

    [Theory]
    [InlineData("DD", "Conflict: both deleted")]
    [InlineData("AU", "Conflict: added by us")]
    [InlineData("UD", "Conflict: deleted by them")]
    [InlineData("UA", "Conflict: added by them")]
    [InlineData("DU", "Conflict: deleted by us")]
    [InlineData("AA", "Conflict: both added")]
    [InlineData("UU", "Conflict: both modified")]
    public void Unmerged_variants_are_conflicts(string xy, string label)
    {
        var c = Single(Z(Unmerged(xy, "dir/c file.txt")));

        Assert.Equal("dir/c file.txt", c.Path);
        Assert.Equal(LocalChangeGroup.Conflicted, c.Group);
        Assert.Equal(label, c.Label);
    }

    [Fact]
    public void Untracked_is_listed_and_ignored_and_headers_are_skipped()
    {
        var set = GitStatusParser.Parse(Z(
            "# branch.oid " + H, "# branch.head main",
            "? new.txt", "! ignored.log", Ordinary(".M", "m.txt")));

        Assert.Equal(new[] { "m.txt", "new.txt" }, set.Changes.Select(c => c.Path).OrderBy(p => p));
        Assert.Equal(LocalChangeGroup.Untracked, set.Changes.Single(c => c.Path == "new.txt").Group);
        Assert.Equal("Untracked", set.Changes.Single(c => c.Path == "new.txt").Label);
    }

    [Theory]
    [InlineData("N...", "160000", "160000", "160000")]   // mode fields only
    [InlineData("S.M.", "160000", "160000", "160000")]
    [InlineData("S..U", "100644", "160000", "160000")]
    [InlineData("S.M.", "100644", "100644", "100644")]    // state field only
    [InlineData("N...", "160000", "100644", "100644")]    // one mode field only (e.g. type change)
    [InlineData("N...", "100644", "100644", "160000")]
    public void Gitlinks_are_labelled_submodule(string sub, string mH, string mI, string mW)
    {
        var c = Single(Z(Ordinary(".M", "libs/sub", sub, mH, mI, mW)));

        Assert.True(c.IsSubmodule);
        Assert.Equal("Submodule", c.Label);
        Assert.Contains("Submodules window", c.Hint);
    }

    [Fact]
    public void Submodule_hint_names_what_changed_inside()
    {
        var c = Single(Z(Ordinary(".M", "sub", "SCMU", "160000", "160000", "160000")));

        Assert.Contains("new commits", c.Hint);
        Assert.Contains("modified content", c.Hint);
        Assert.Contains("untracked files", c.Hint);
    }

    [Fact]
    public void Regular_file_is_not_a_submodule()
    {
        var c = Single(Z(Ordinary(".M", "a.txt")));

        Assert.False(c.IsSubmodule);
        Assert.Null(c.Hint);
    }

    [Fact]
    public void Unmerged_gitlink_is_a_conflicted_submodule()
    {
        var c = Single(Z(Unmerged("UU", "sub", "S...", "160000")));

        Assert.True(c.IsSubmodule);
        Assert.Equal(LocalChangeGroup.Conflicted, c.Group);
        Assert.Contains("both modified", c.Hint);
    }

    [Theory]
    [InlineData("dir with spaces/a b.txt")]
    [InlineData("  leading and  double  spaces .txt")]
    [InlineData("ünï/cödé/日本語.txt")]
    [InlineData("100% done/%20 50%.txt")]
    [InlineData("quote\"s and 'apostrophes'.txt")]
    [InlineData("new\nline.txt")]
    public void Unusual_paths_arrive_unchanged_for_every_entry_type(string path)
    {
        Assert.Equal(path, Single(Z(Ordinary(".M", path))).Path);
        Assert.Equal(path, Single(Z(Unmerged("UU", path))).Path);
        Assert.Equal(path, Single(Z("? " + path)).Path);

        var renamed = Single(Z(Renamed("R.", "R", path), path + " old"));
        Assert.Equal(path, renamed.Path);
        Assert.Equal(path + " old", renamed.OriginalPath);
    }

    [Fact]
    public void Nul_handling_trailing_separator_is_optional_and_empty_input_is_empty()
    {
        Assert.Equal(0, GitStatusParser.Parse("").Count);
        Assert.Equal(0, GitStatusParser.Parse("\0").Count);
        Assert.Equal(1, GitStatusParser.Parse("? a.txt").Count);
        Assert.Equal(2, GitStatusParser.Parse("? a.txt\0? b.txt").Count);
        Assert.Equal(2, GitStatusParser.Parse("? a.txt\0? b.txt\0").Count);
    }

    [Fact]
    public void Malformed_entries_are_skipped()
    {
        var set = GitStatusParser.Parse(Z("1 .M N... too short", "garbage", "x", "? ok.txt"));

        Assert.Equal("ok.txt", Assert.Single(set.Changes).Path);
    }

    [Fact]
    public void Path_deleted_from_index_and_present_again_is_one_row()
    {
        var set = GitStatusParser.Parse(Z(Ordinary("D.", "a.txt"), "? a.txt"));

        var c = Assert.Single(set.Changes);
        Assert.Equal(LocalChangeGroup.Deleted, c.Group);
    }

    [Fact]
    public void Counts_do_not_double_count_staged_and_modified_files()
    {
        var set = GitStatusParser.Parse(Z(
            Ordinary("A.", "added.txt"),
            Ordinary("MM", "both.txt"),
            Ordinary(".M", "m1.txt"),
            Ordinary(".M", "m2.txt"),
            "? u1.txt", "? u2.txt"));

        Assert.Equal(6, set.Count);
        Assert.Equal(2, set.CountOf(LocalChangeGroup.Staged));
        Assert.Equal(2, set.CountOf(LocalChangeGroup.Modified));
        Assert.Equal(2, set.CountOf(LocalChangeGroup.Untracked));
        Assert.Equal(set.Count, Enum.GetValues<LocalChangeGroup>().Sum(set.CountOf));
        Assert.Equal("6 files changed: 2 staged, 2 modified, 2 untracked", set.SummaryText);
    }

    [Fact]
    public void Changes_are_ordered_by_group_then_path()
    {
        var set = GitStatusParser.Parse(Z(
            "? z.txt",
            Renamed("R.", "R", "r.txt"), "old.txt",
            Ordinary(".D", "d.txt"),
            Ordinary(".M", "b.txt"), Ordinary(".M", "a.txt"),
            Ordinary("M.", "s.txt"),
            Unmerged("UU", "c.txt")));

        Assert.Equal(
            new[] { "c.txt", "s.txt", "a.txt", "b.txt", "d.txt", "r.txt", "z.txt" },
            set.Changes.Select(c => c.Path));
    }

    [Fact]
    public void Summary_text_singular_and_empty()
    {
        Assert.Equal("1 file changed: 1 conflicted", GitStatusParser.Parse(Z(Unmerged("UU", "a"))).SummaryText);
        Assert.Equal("No local changes", LocalChangeSet.Empty.SummaryText);
    }
}
