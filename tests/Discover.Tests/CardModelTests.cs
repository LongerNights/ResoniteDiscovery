using Discover.Core.Settings;
using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Presentation;

namespace Discover.Tests;

public class CardModelTests
{
    private static readonly DateTime Now = TestData.Now;

    private static DiscoveryEntry Live(params SessionSnapshot[] sessions)
    {
        var index = new DiscoveryIndex();
        index.LoadSessions(sessions);
        return index.Entries.Single();
    }

    [Fact]
    public void LiveWorldShowsUsersFriendsAndJoin()
    {
        var entry = Live(TestData.Session("a", world: "W", active: 12, max: 24, contacts: 2, name: "Elysium") with { HostUsername = "ElysiumHeadless" });

        var card = CardBuilder.Build(entry, null, Now);

        Assert.Equal(CardAction.Join, card.Action);
        Assert.Equal("12/24", card.Users);
        Assert.Equal("Elysium", card.Title);
        Assert.StartsWith("ElysiumHeadless · 30 m", card.Meta);
        Assert.Equal("2 friends", Assert.Single(card.Badges).Text);
        Assert.False(card.IsOpen);
    }

    [Fact]
    public void AnOpenWorldOffersGoThereWithAnOpenFlag()
    {
        var session = TestData.Session("a", world: "W");
        var card = CardBuilder.Build(Live(session), session, Now);

        Assert.Equal(CardAction.GoThere, card.Action);
        Assert.True(card.IsOpen);
    }

    [Fact]
    public void FullAndOtherVersionWorldsCannotBeJoined()
    {
        var full = CardBuilder.Build(Live(TestData.Session("a", world: "W", active: 10, max: 10)), null, Now);
        var other = CardBuilder.Build(Live(TestData.Session("b", world: "X") with { IsCompatible = false }), null, Now);

        Assert.Equal(CardAction.Full, full.Action);
        Assert.Contains(full.Badges, b => b.Kind == BadgeKind.Full);
        Assert.Equal(CardAction.OtherVersion, other.Action);
        Assert.True(other.Dimmed);
        Assert.Contains(other.Badges, b => b.Kind == BadgeKind.OtherVersion);
    }

    [Fact]
    public void APublishedWorldWithNoSessionOffersANewSession()
    {
        var entry = new DiscoveryEntry
        {
            Key = "W",
            Record = TestData.Record("W", name: "Lizard Lounge", visits: 97_300, tags: ["dev", "social", "chill", "extra"], owner: "vak", published: Now.AddDays(-180)),
        };

        var card = CardBuilder.Build(entry, null, Now);

        Assert.Equal(CardAction.NewSession, card.Action);
        Assert.Null(card.Users);
        Assert.Equal("by vak · 6 mo", card.Meta);
        Assert.Equal("97.3k visits", card.Footer);
        Assert.Equal(["dev", "social", "chill"], card.Tags.Select(t => t.Text)); // at most three
        Assert.All(card.Tags, t => Assert.False(t.Personal));
    }

    [Fact]
    public void ManySessionsAreCountedInTheMeta()
    {
        var entry = Live(
            TestData.Session("a", world: "W", active: 5), TestData.Session("b", world: "W", active: 3), TestData.Session("c", world: "W", active: 1));

        Assert.EndsWith("+3 sessions", CardBuilder.Build(entry, null, Now).Meta);
    }

    [Fact]
    public void LongTitlesAreCutAndFeaturedIsCarried()
    {
        var entry = new DiscoveryEntry
        {
            Key = "W",
            Record = TestData.Record("W", name: new string('x', 80), featured: true),
        };

        var card = CardBuilder.Build(entry, null, Now);

        Assert.Equal(CardModel.MaxTitleChars, card.Title.Length);
        Assert.EndsWith("…", card.Title);
        Assert.True(card.Featured);
    }

    [Fact]
    public void ContactsOnlySessionsStillShowUsersOnTheFriendsLens()
    {
        var entry = Live(TestData.Session("a", world: "W", access: AccessLevel.Contacts, contacts: 1, active: 4));

        var card = CardBuilder.Build(entry, null, Now);

        Assert.Equal("4/10", card.Users);
        Assert.Equal(CardAction.Join, card.Action);
    }

    // ---- Rail, sort menu, counts

    [Fact]
    public void RailMarksTheSelectedLensAndShowsLiveCounts()
    {
        var rail = RailBuilder.Build(LensKind.Browse, 128, 0);

        Assert.Equal(["NOW", "DISCOVER", "YOURS", "INSIGHTS"], rail.Select(s => s.Label));
        Assert.Equal("128", rail[0].Items[1].Count); // Current Worlds leads the section
        Assert.Null(rail[0].Items[2].Count); // zero friends shows no number
        Assert.Equal(LensKind.Browse, Assert.Single(rail.SelectMany(s => s.Items), i => i.Selected).Lens);
    }

    [Fact]
    public void MineSectionListsFavoritesNamedListsAndRecent()
    {
        var lists = new[] { new ListInfo("a", "Games", 3), new ListInfo("b", "Art", 0) };

        var mine = RailBuilder.Build(LensKind.NamedList, 0, 0, favoritesCount: 5, lists: lists, recentCount: 2, selectedListId: "b")[2];

        Assert.Equal(["Favorites", "Games", "Art", "Recent", "My Worlds", "Group Worlds"], mine.Items.Select(i => i.Label));
        Assert.Equal(["5", "3", null, "2", null, null], mine.Items.Select(i => i.Count));
        Assert.Equal("Art", Assert.Single(mine.Items, i => i.Selected).Label); // only the chosen list is lit
        Assert.Equal(["a", "b"], mine.Items.Where(i => i.Lens == LensKind.NamedList).Select(i => i.ListId));
    }

    [Fact]
    public void LiveOnlyLeavesSearchLensesOffTheRail()
    {
        var rail = RailBuilder.Build(LensKind.LiveNow, 0, 0, visibility: new DiscoverSettings { LiveLensesOnly = true });

        var lenses = rail.SelectMany(s => s.Items).Select(i => i.Lens).ToList();
        Assert.DoesNotContain(LensKind.New, lenses);
        Assert.DoesNotContain(LensKind.MyWorlds, lenses);
        Assert.Contains(LensKind.Favorites, lenses);
        Assert.DoesNotContain(rail, s => s.Items.Count == 0 && s.Id == RailSectionId.Discover); // Browse goes with its lenses
    }

    [Fact]
    public void PersonalTagsComeFirstAndAreMarked()
    {
        var entry = new DiscoveryEntry
        {
            Key = "W",
            Record = TestData.Record("W", tags: ["music", "CHILL", "social"]),
            PersonalTags = ["chill", "cozy"],
        };

        var card = CardBuilder.Build(entry, null, Now, isFavorite: true);

        Assert.Equal(["chill", "cozy", "music"], card.Tags.Select(t => t.Text)); // the duplicate "CHILL" is dropped
        Assert.Equal([true, true, false], card.Tags.Select(t => t.Personal));
        Assert.True(card.IsFavorite);
    }

    [Fact]
    public void LocalLensesOnlySortByDateAddedOrName()
    {
        foreach (var lens in new[] { LensKind.Favorites, LensKind.NamedList, LensKind.Recent })
        {
            Assert.Equal(SortKey.DateAdded, Sorting.Default(lens));
            Assert.True(Sorting.IsSupported(lens, SortKey.DateAdded));
            Assert.True(Sorting.IsSupported(lens, SortKey.Name));
            Assert.False(Sorting.IsSupported(lens, SortKey.Visits));
            Assert.False(Sorting.IsSupported(lens, SortKey.LiveUsers));
        }

        Assert.False(Sorting.IsSupported(LensKind.LiveNow, SortKey.DateAdded));
    }

    [Fact]
    public void SortMenuDisablesWhatTheLensCannotDo()
    {
        var live = RailBuilder.SortMenu(LensKind.LiveNow, SortKey.LiveUsers);
        Assert.Equal(Sorting.All.Count, live.Count);
        Assert.True(live.Single(i => i.Key == SortKey.LiveUsers).Selected);
        Assert.False(live.Single(i => i.Key == SortKey.Visits).Enabled);
        Assert.Equal("Not for this lens", live.Single(i => i.Key == SortKey.Visits).Hint);

        var search = RailBuilder.SortMenu(LensKind.Browse, SortKey.Visits);
        Assert.True(search.Single(i => i.Key == SortKey.Visits).Enabled);
        Assert.False(search.Single(i => i.Key == SortKey.LiveUsers).Enabled);
    }

    [Fact]
    public void EverySortAndLensHasText()
    {
        foreach (var key in Sorting.All)
        {
            Assert.NotEqual("sort." + key, UiStrings.Get("sort." + key));
            Assert.NotEqual("sort." + key + ".hint", UiStrings.Get("sort." + key + ".hint"));
        }

        foreach (var lens in new[] { LensKind.LiveNow, LensKind.Friends, LensKind.New, LensKind.Browse, LensKind.Featured, LensKind.Random })
        {
            Assert.NotEmpty(RailBuilder.Title(lens));
            Assert.NotEmpty(RailBuilder.Subtitle(lens));
        }
    }

    [Fact]
    public void CountReadsDifferentlyForLiveAndSearchLenses()
    {
        Assert.Equal("7 worlds · 55 people", RailBuilder.Count(LensKind.LiveNow, 7, 55));
        Assert.Equal("40 worlds on this page", RailBuilder.Count(LensKind.New, 40, 0));
    }
}
