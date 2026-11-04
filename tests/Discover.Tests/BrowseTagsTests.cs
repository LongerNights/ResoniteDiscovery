using Discover.Core.Lenses;
using Discover.Core.Settings;

namespace Discover.Tests;

public class BrowseTagsTests
{
    private static readonly DiscoverSettings Defaults = new();

    [Fact]
    public void CategoriesComeFirstAndInOrder()
    {
        var tags = BrowseTags.Build(Defaults, [], [], []);

        Assert.Equal(["Social", "Games", "Avatars", "Art", "Tutorials", "Educational"], tags.Select(t => t.Label));
        Assert.All(tags, t => Assert.Equal(BrowseTagKind.Category, t.Kind));
    }

    [Fact]
    public void CategoryTagsAreTheOnesTheStockButtonsSend()
    {
        // WorldFilterFacetPreset (V24): +social, +avatar, +game, +art, +tutorial, +educational
        var tags = BrowseTags.Build(Defaults, [], [], []);

        Assert.Equal(["social", "game", "avatar", "art", "tutorial", "educational"], tags.Select(t => t.Tag));
    }

    [Fact]
    public void PopularTagsAreTheMostCommonOnScreenAndSkipCategoriesAndExclusions()
    {
        var index = TestData.IndexOf(
            TestData.Session("a", world: "W1", tags: ["horror", "game", "chill"]),
            TestData.Session("b", world: "W2", tags: ["horror", "chill", "spooky"]),
            TestData.Session("c", world: "W3", tags: ["horror", "banned"]));
        var excluded = new[] { new TagChip("banned", ChipState.Excluded, false) };

        var tags = BrowseTags.Build(Defaults, index.Entries, excluded, []);

        var popular = tags.Where(t => t.Kind == BrowseTagKind.Popular).Select(t => t.Tag).ToList();
        Assert.Equal(["horror", "chill", "spooky"], popular);
    }

    [Fact]
    public void PopularTagsAreCappedAndDeterministic()
    {
        var settings = Defaults with { BrowsePopularTagCount = 2 };
        var index = TestData.IndexOf(TestData.Session("a", world: "W1", tags: ["b", "a", "c"]));

        var first = BrowseTags.Build(settings, index.Entries, [], []).Where(t => t.Kind == BrowseTagKind.Popular).Select(t => t.Tag);

        Assert.Equal(["a", "b"], first); // equal counts break ties alphabetically
    }

    [Fact]
    public void PersonalTagsFollowAndAreNotRepeated()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W1", tags: ["chill"]));

        var tags = BrowseTags.Build(Defaults, index.Entries, [], ["chill", "mine", "Games"]);

        Assert.Equal(["mine"], tags.Where(t => t.Kind == BrowseTagKind.Personal).Select(t => t.Tag));
    }

    [Fact]
    public void RowLightsActiveChipsAndKeepsUnrecommendedOnesVisible()
    {
        var recommended = BrowseTags.Build(Defaults, [], [], []);
        var active = new[] { new TagChip("game", ChipState.Required, false), new TagChip("zzz", ChipState.Excluded, false) };

        var row = BrowseTags.Row(recommended, active, 12);

        Assert.Equal(ChipState.Required, row.Single(r => r.Label == "Games").State);
        Assert.Null(row.Single(r => r.Label == "Social").State);
        Assert.Equal(ChipState.Excluded, row[^1].State);
        Assert.Equal("zzz", row[^1].Tag);
    }

    [Fact]
    public void RowDropsUnlitRecommendationsFromTheEndButNeverAnActiveChip()
    {
        var recommended = BrowseTags.Build(Defaults, [], [], []);
        var active = new[] { new TagChip("educational", ChipState.Required, false) };

        var row = BrowseTags.Row(recommended, active, 3);

        Assert.Equal(3, row.Count);
        Assert.Contains(row, r => r.Label == "Educational" && r.State == ChipState.Required);
    }
}
