using GitSparseManager.Services;

namespace GitSparseManager.Tests;

public class GitTreeParserTests
{
    [Fact]
    public void Parses_blobs_trees_and_gitlinks()
    {
        var output =
            "040000 tree aaaa\tapps\0" +
            "100644 blob bbbb\tapps/readme.md\0" +
            "160000 commit cccc\tlibs/sub\0";

        var nodes = GitTreeParser.Parse(output);

        Assert.Equal(3, nodes.Count);

        var tree = nodes[0];
        Assert.Equal("tree", tree.Type);
        Assert.True(tree.IsFolder);
        Assert.False(tree.IsSubmodule);

        var blob = nodes[1];
        Assert.Equal("blob", blob.Type);
        Assert.Equal("readme.md", blob.Name);
        Assert.Equal("apps/readme.md", blob.Path);
        Assert.False(blob.IsFolder);

        var link = nodes[2];
        Assert.True(link.IsSubmodule);
        Assert.True(link.IsFolder); // shown as a folder so it can be selected
        Assert.Equal("sub", link.Name);
    }

    [Fact]
    public void Handles_spaces_and_non_ascii_paths()
    {
        var output =
            "040000 tree aaaa\tmy folder\0" +
            "100644 blob bbbb\tmy folder/résumé ü.txt\0" +
            "100644 blob cccc\t日本語/ファイル.md\0";

        var nodes = GitTreeParser.Parse(output);

        Assert.Equal("my folder", nodes[0].Name);
        Assert.Equal("résumé ü.txt", nodes[1].Name);
        Assert.Equal("my folder/résumé ü.txt", nodes[1].Path);
        Assert.Equal("ファイル.md", nodes[2].Name);
    }

    [Fact]
    public void Empty_and_malformed_input_yields_nothing()
    {
        Assert.Empty(GitTreeParser.Parse(string.Empty));
        Assert.Empty(GitTreeParser.Parse("garbage\0no-tab-here\0"));
    }
}
