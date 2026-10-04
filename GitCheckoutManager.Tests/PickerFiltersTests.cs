using GitCheckoutManager.Controls;
using GitCheckoutManager.Models;

public class PickerFiltersTests
{
    private static readonly Repository Repo = new()
    {
        Name = "Widget",
        PathWithNamespace = "acme/tools/Widget"
    };

    [Theory]
    [InlineData("")]
    [InlineData("widget")]
    [InlineData("WIDGET")]
    [InlineData("idg")]
    public void Repository_MatchesNameCaseInsensitiveSubstring(string filter) =>
        Assert.True(PickerFilters.MatchesRepository(Repo, filter));

    [Theory]
    [InlineData("acme")]
    [InlineData("ACME/Tools")]
    [InlineData("tools/wid")]
    public void Repository_MatchesNamespaceAndPath(string filter) =>
        Assert.True(PickerFilters.MatchesRepository(Repo, filter));

    [Fact]
    public void Repository_NoMatch_AndWrongType() 
    {
        Assert.False(PickerFilters.MatchesRepository(Repo, "gadget"));
        Assert.False(PickerFilters.MatchesRepository("acme", "acme"));
    }

    [Theory]
    [InlineData("feature/login", "LOGIN", true)]
    [InlineData("feature/login", "feat", true)]
    [InlineData("feature/login", "main", false)]
    [InlineData("main", "", true)]
    public void Branch_CaseInsensitiveSubstring(string name, string filter, bool expected) =>
        Assert.Equal(expected, PickerFilters.MatchesBranch(new Branch { Name = name }, filter));
}
