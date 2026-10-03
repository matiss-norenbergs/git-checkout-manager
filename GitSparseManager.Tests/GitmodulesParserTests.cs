using GitSparseManager.Services;

namespace GitSparseManager.Tests;

public class GitmodulesParserTests
{
    [Fact]
    public void Parses_names_containing_dots()
    {
        var output =
            "submodule.vendor.lib.v2.path\nthird_party/lib.v2\0" +
            "submodule.vendor.lib.v2.url\nhttps://example.com/lib.v2.git\0" +
            "submodule.vendor.lib.v2.branch\nmain\0" +
            "submodule.plain.path\nplain\0" +
            "submodule.plain.url\n../plain.git\0";

        var map = GitmodulesParser.ParseByPath(output);

        Assert.Equal(2, map.Count);

        var dotted = map["third_party/lib.v2"];
        Assert.Equal("vendor.lib.v2", dotted.Name);
        Assert.Equal("https://example.com/lib.v2.git", dotted.Config.Url);
        Assert.Equal("main", dotted.Config.Branch);

        var plain = map["plain"];
        Assert.Equal("plain", plain.Name);
        Assert.Equal("../plain.git", plain.Config.Url);
        Assert.Null(plain.Config.Branch);
    }

    [Fact]
    public void Normalizes_path_separators_and_skips_entries_without_path()
    {
        var output =
            "submodule.a.path\nlibs\\a/\0" +
            "submodule.nopath.url\nhttps://example.com/x.git\0" +
            "unrelated.key\nvalue\0";

        var map = GitmodulesParser.ParseByPath(output);

        Assert.Single(map);
        Assert.True(map.ContainsKey("libs/a"));
    }

    [Fact]
    public void Empty_output_is_empty()
    {
        Assert.Empty(GitmodulesParser.ParseByPath(string.Empty));
    }
}
