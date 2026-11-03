using Discover.Core.Search;

namespace Discover.Tests;

public class TagSuggesterTests
{
    private static readonly string[] Tags = ["social", "social", "social", "sandbox", "sandbox", "game", "avatars", "art", "ssocial-club"];

    [Fact]
    public void PrefixMatchesComeFirstBusiestFirst()
    {
        Assert.Equal(["social", "sandbox"], TagSuggester.Suggest("s", Tags, max: 2));
    }

    [Fact]
    public void SubstringMatchesFollowPrefixMatches()
    {
        Assert.Equal(["art", "avatars", "social", "sandbox", "game"], TagSuggester.Suggest("a", Tags));
    }

    [Fact]
    public void BlankPrefixReturnsTheMostUsed()
    {
        Assert.Equal(["social", "sandbox"], TagSuggester.Suggest("", Tags, max: 2));
    }

    [Fact]
    public void ExcludedAndCaseDuplicatesAreHandled()
    {
        var result = TagSuggester.Suggest("so", ["Social", "social", "SOCIAL"], ["social"]);
        Assert.Empty(result);

        Assert.Equal(["Social"], TagSuggester.Suggest("so", ["Social", "social"]));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("hello ", null)]
    [InlineData("\"big wor", null)]
    public void NothingToCompleteAfterSpacesOrInsideQuotes(string text, string? typed)
    {
        Assert.Null(TagSuggester.LastWord(text));
        Assert.Null(typed);
    }

    [Fact]
    public void LastWordSplitsSignAndTagPrefix()
    {
        var word = TagSuggester.LastWord("cool worlds -tag:soc")!.Value;

        Assert.Equal("cool worlds ", word.Head);
        Assert.Equal("-", word.Sign);
        Assert.Equal("soc", word.Typed);
        Assert.Equal("cool worlds -tag:social ", TagSuggester.Complete(word, "social"));
        Assert.Equal("so", TagSuggester.LastWord("so")!.Value.Typed);
        Assert.Equal("tag:art ", TagSuggester.Complete(TagSuggester.LastWord("ar")!.Value, "art"));
    }
}
