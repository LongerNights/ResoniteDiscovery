using Discover.Core.Model;
using Discover.Core.Search;
using Discover.Core.Sources;

namespace Discover.Tests;

public class SearchTests
{
    private static DiscoveryEntry World(string name, string[]? tags = null, string description = "", string owner = "", string[]? personal = null) =>
        new()
        {
            Key = "K-" + name,
            Record = TestData.Record("K-" + name, name: name, tags: tags, description: description, owner: owner),
            PersonalTags = personal ?? [],
        };

    [Fact]
    public void AuthorNamesListsEachNonExcludedAuthorOnce()
    {
        var q = SearchQuery.Parse("author:Winter +author:winter -author:Spammer \"author:Two Words\" plain");

        Assert.Equal(["Winter"], q.AuthorNames);
        Assert.Empty(SearchQuery.Parse("-author:x plain").AuthorNames);
    }

    [Fact]
    public void AuthorTermMatchesOwnerNameOnly()
    {
        var mine = World("Plaza", owner: "Winter");
        var other = World("Winter Lodge", owner: "Someone");

        var q = SearchQuery.Parse("author:wint");
        var term = Assert.Single(q.Optional);
        Assert.True(term.IsAuthor);
        Assert.True(q.Matches(mine));
        Assert.False(q.Matches(other)); // the name matches, the owner does not
        Assert.True(SearchQuery.Parse("+author:\"Some\" +winter").Matches(other));
        Assert.False(SearchQuery.Parse("-author:winter").Matches(mine));
    }

    [Fact]
    public void ParsesOptionalRequiredAndExcluded()
    {
        var q = SearchQuery.Parse("+a -b c");

        Assert.Equal(["a"], q.Required.Select(t => t.Text));
        Assert.Equal(["b"], q.Excluded.Select(t => t.Text));
        Assert.Equal(["c"], q.Optional.Select(t => t.Text));
    }

    [Fact]
    public void ParsesTagPrefixAndQuotedPhrases()
    {
        var q = SearchQuery.Parse("+tag:horror -tag:\"low poly\" \"big room\" +\"no way\"");

        Assert.Equal(new SearchTerm("horror", true), Assert.Single(q.Required, t => t.IsTag));
        Assert.Equal(new SearchTerm("low poly", true), Assert.Single(q.Excluded));
        Assert.Equal("big room", Assert.Single(q.Optional).Text);
        Assert.Contains(q.Required, t => t is { Text: "no way", IsTag: false });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("+")]
    [InlineData("- + -")]
    [InlineData("tag:")]
    [InlineData("\"\"")]
    public void EmptyAndDegenerateInputsAreEmpty(string? input)
    {
        Assert.True(SearchQuery.Parse(input).IsEmpty);
    }

    [Fact]
    public void UnterminatedQuoteTakesTheRest()
    {
        Assert.Equal("open ended", Assert.Single(SearchQuery.Parse("\"open ended").Optional).Text);
    }

    [Fact]
    public void VeryLongInputIsCapped()
    {
        var q = SearchQuery.Parse(string.Join(' ', Enumerable.Range(0, 5000).Select(i => "w" + i)));
        var count = q.Optional.Count + q.Required.Count + q.Excluded.Count;

        Assert.True(count <= SearchQuery.MaxTerms);

        var oneHuge = SearchQuery.Parse(new string('x', 100_000));
        Assert.Equal(SearchQuery.MaxLength, Assert.Single(oneHuge.Optional).Text.Length);
    }

    [Fact]
    public void RequiredAndExcludedWorkTogether()
    {
        var q = SearchQuery.Parse("+horror -avatar");
        var worlds = new[]
        {
            World("Haunted House", tags: ["horror"]),
            World("Horror Avatar Hub", tags: ["avatar"]),
            World("Cozy Cabin", tags: ["chill"]),
            World("Scary Place", description: "pure horror"),
        };

        Assert.Equal(["Haunted House", "Scary Place"], q.Filter(worlds).Select(e => e.DisplayName));
    }

    [Fact]
    public void OptionalTermsNeedAtLeastOneMatch()
    {
        var q = SearchQuery.Parse("cat dog");
        var worlds = new[] { World("Cat Cafe"), World("Dog Park"), World("Fish Tank") };

        Assert.Equal(["Cat Cafe", "Dog Park"], q.Filter(worlds).Select(e => e.DisplayName));
    }

    [Fact]
    public void MatchingIsCaseInsensitiveAndCoversAuthorAndTags()
    {
        var worlds = new[] { World("Plain", tags: ["Sci-Fi"], owner: "Winter") };

        Assert.Single(SearchQuery.Parse("WINTER").Filter(worlds));
        Assert.Single(SearchQuery.Parse("sci").Filter(worlds));
        Assert.Single(SearchQuery.Parse("+tag:sci-fi").Filter(worlds));
        Assert.Empty(SearchQuery.Parse("+tag:sci").Filter(worlds)); // tag terms are exact
    }

    [Fact]
    public void TagTermsMatchPersonalTags()
    {
        var worlds = new[] { World("Plain", personal: ["chill"]), World("Other") };

        Assert.Equal(["Plain"], SearchQuery.Parse("+tag:chill").Filter(worlds).Select(e => e.DisplayName));
        Assert.Equal(["Other"], SearchQuery.Parse("-tag:chill").Filter(worlds).Select(e => e.DisplayName));
    }

    [Fact]
    public void SessionNameAndHostAreSearchable()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W", name: "Movie Night"));
        var entry = Assert.Single(index.Entries);

        Assert.True(SearchQuery.Parse("movie").Matches(entry));
        Assert.False(SearchQuery.Parse("-movie").Matches(entry));
    }

    [Fact]
    public void EmptyQueryKeepsEverything()
    {
        var worlds = new[] { World("A"), World("B") };
        Assert.Equal(2, SearchQuery.Parse("").Filter(worlds).Count);
    }

    [Fact]
    public void TagTermsBecomeServerTagListsExceptPersonalOnes()
    {
        var q = SearchQuery.Parse("+tag:horror +tag:chill -tag:avatar tag:fun plain");

        var query = q.ApplyTo(new RecordQuery(), isPersonalTag: t => t == "chill");

        Assert.Equal(["horror"], query.RequiredTags);
        Assert.Equal(["avatar"], query.ExcludedTags);
        Assert.Equal(["fun"], query.OptionalTags);
    }

    [Fact]
    public void ApplyToKeepsExistingTags()
    {
        var query = SearchQuery.Parse("+tag:b").ApplyTo(new RecordQuery { RequiredTags = ["a"] });

        Assert.Equal(["a", "b"], query.RequiredTags);
    }

    [Fact]
    public void RecordQueryEqualityIgnoresTagOrderAndCase()
    {
        var a = new RecordQuery { RequiredTags = ["Horror", "game"], ExcludedTags = ["x"] };
        var b = new RecordQuery { RequiredTags = ["GAME", "horror", "horror"], ExcludedTags = ["X"] };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Single(new HashSet<RecordQuery> { a, b });
    }

    [Fact]
    public void RecordQueryEqualityStillSeesRealDifferences()
    {
        var a = new RecordQuery { RequiredTags = ["a"] };

        Assert.NotEqual(a, a with { RequiredTags = ["b"] });
        Assert.NotEqual(a, a with { Offset = 50 });
        Assert.NotEqual(a, a with { OnlyFeatured = true });
        Assert.NotEqual(a, a with { ByOwner = "U-x" });
        Assert.NotEqual(a, a with { ExcludedTags = ["a"] });
    }
}
