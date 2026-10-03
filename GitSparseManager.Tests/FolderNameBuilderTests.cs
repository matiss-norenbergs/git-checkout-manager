using GitSparseManager.Services;

namespace GitSparseManager.Tests;

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
    public void Trailing_dots_and_spaces_are_trimmed()
    {
        Assert.Equal("repo", FolderNameBuilder.Build("{repo}. . ", "repo", null, "main"));
    }
}
