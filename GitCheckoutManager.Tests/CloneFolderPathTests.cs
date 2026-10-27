using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

public class CloneFolderPathTests
{
    [Theory]
    [InlineData("repo")]
    [InlineData("a/b/c")]
    [InlineData("a\\b\\c")]
    [InlineData("a/b\\c/")]
    [InlineData("a//b")]
    [InlineData("repo_feature-login")]
    public void Accepts_names_and_relative_paths(string input)
    {
        Assert.NotNull(CloneFolderPath.Split(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("/")]
    [InlineData("/abs")]
    [InlineData("\\\\server\\share")]
    [InlineData("C:\\x")]
    [InlineData("C:/x")]
    [InlineData("a/../b")]
    [InlineData("..")]
    [InlineData("./a")]
    [InlineData("a/b.")]
    [InlineData("a/b /c")]
    [InlineData("a/ /c")]
    [InlineData("a/b*c")]
    [InlineData("a/b?c")]
    public void Rejects_rooted_traversing_and_invalid_paths(string? input)
    {
        Assert.Null(CloneFolderPath.Split(input));
    }

    [Fact]
    public void Split_returns_segments_without_empty_entries()
    {
        Assert.Equal(new[] { "a", "b", "c" }, CloneFolderPath.Split("a//b\\c/"));
    }

    [Fact]
    public void Normalize_uses_the_platform_separator()
    {
        Assert.Equal(string.Join(Path.DirectorySeparatorChar, "a", "b", "c"), CloneFolderPath.Normalize("a/b\\c"));
        Assert.Null(CloneFolderPath.Normalize("a/../b"));
    }

    [Fact]
    public void WithSeparator_converts_both_slash_kinds()
    {
        Assert.Equal("a\\b\\c", CloneFolderPath.WithSeparator("a/b\\c", '\\'));
        Assert.Equal("a/b/c", CloneFolderPath.WithSeparator("a/b\\c", '/'));
    }
}
