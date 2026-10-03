using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

public class FolderNameBuilderTests
{
    [Fact]
    public void Default_pattern_uses_repo_and_sanitized_branch()
    {
        Assert.Equal("repo_feature-login", FolderNameBuilder.Build("{repo}_{branch}", "repo", null, "feature/login"));
    }

    [Fact]
    public void Empty_pattern_falls_back_to_default()
    {
        Assert.Equal("repo_main", FolderNameBuilder.Build("  ", "repo", null, "main"));
    }

    [Fact]
    public void New_branch_wins_over_base_for_branch_token_but_not_base_token()
    {
        Assert.Equal("repo_topic_main", FolderNameBuilder.Build("{repo}_{branch}_{base}", "repo", "topic", "main"));
    }

    [Fact]
    public void Invalid_characters_become_single_dashes()
    {
        Assert.Equal("a-b-c-d", FolderNameBuilder.Build("a/\\b:*c?\"<>|d", null, null, null));
    }

    [Fact]
    public void Empty_token_yields_empty_name()
    {
        Assert.Equal(string.Empty, FolderNameBuilder.Build("{repo}_{branch}", "repo", null, null));
        Assert.Equal(string.Empty, FolderNameBuilder.Build("{repo}_{branch}", "repo", " ", ""));
        Assert.Equal(string.Empty, FolderNameBuilder.Build("{repo}_{base}", "repo", "topic", null));
    }

    [Fact]
    public void Unused_empty_token_is_ignored()
    {
        Assert.Equal("repo", FolderNameBuilder.Build("{repo}", "repo", null, null));
    }

    [Fact]
    public void Leading_and_trailing_separators_are_trimmed()
    {
        Assert.Equal("repo", FolderNameBuilder.Build("_-. {repo} .-_", "repo", null, "main"));
        Assert.Equal("repo_main", FolderNameBuilder.Build("{repo}_{branch}_", "repo", null, "main"));
    }

    [Fact]
    public void Repeated_separators_are_collapsed()
    {
        Assert.Equal("a_b", FolderNameBuilder.Build("a__b", null, null, null));
        Assert.Equal("repo_feature-x", FolderNameBuilder.Build("{repo}__{branch}", "repo", null, "feature//x"));
    }

    [Fact]
    public void Trailing_dots_and_spaces_are_trimmed()
    {
        Assert.Equal("repo", FolderNameBuilder.Build("{repo}. . ", "repo", null, "main"));
    }
}
