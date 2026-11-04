using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Sources;

namespace Discover.Tests;

public class LensTests
{
    [Fact]
    public void LiveNowSortsByUsersThenFriendsThenName()
    {
        var index = TestData.IndexOf(
            TestData.Session("a", world: "W-a", active: 5, name: "Alpha"),
            TestData.Session("b", world: "W-b", active: 9, name: "Bravo"),
            TestData.Session("c", world: "W-c", active: 5, contacts: 2, name: "Charlie"),
            TestData.Session("d", world: "W-d", active: 5, name: "Able"),
            TestData.Session("e", world: "W-e", active: 0, name: "Empty"));

        var names = LensEngine.LiveNow(index.Entries).Select(e => e.DisplayName);

        Assert.Equal(["Bravo", "Charlie", "Able", "Alpha"], names);
    }

    [Fact]
    public void LiveNowIgnoresContactsOnlySessions()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W", access: AccessLevel.Contacts, contacts: 1, active: 4));

        Assert.Empty(LensEngine.LiveNow(index.Entries));
    }

    [Fact]
    public void FriendsListsContactSessionsAtAnyContactLevel()
    {
        var index = TestData.IndexOf(
            TestData.Session("pub", world: "W1", contacts: 1),
            TestData.Session("con", world: "W2", access: AccessLevel.Contacts, contacts: 3),
            TestData.Session("plus", world: "W3", access: AccessLevel.ContactsPlus, contacts: 2),
            TestData.Session("none", world: "W4", contacts: 0),
            TestData.Session("empty", world: "W5", access: AccessLevel.Contacts, contacts: 0));

        var keys = LensEngine.Friends(index.Entries).Select(e => e.Key);

        Assert.Equal(["W2", "W3", "W1"], keys);
    }

    [Fact]
    public void SavedPutsLiveFirstThenNewestStarred()
    {
        var index = TestData.IndexOf(
            TestData.Session("a", world: "W-live-small", active: 2),
            TestData.Session("b", world: "W-live-big", active: 7));
        var t = TestData.Now;
        var members = new[]
        {
            ("W-old", "Old", t.AddDays(-9)),
            ("W-live-small", "Small", t.AddDays(-30)),
            ("W-new", "New", t.AddDays(-1)),
            ("W-live-big", "Big", t.AddDays(-20)),
        };

        var order = LensEngine.Saved(members, key => index.TryGetEntry(key, out var e) ? e : null).Select(i => i.Key);

        Assert.Equal(["W-live-big", "W-live-small", "W-new", "W-old"], order);
    }

    [Fact]
    public void SavedKeepsMembersWithoutAnEntry()
    {
        var item = Assert.Single(LensEngine.Saved([("W", "Gone", TestData.Now)], _ => null));

        Assert.Null(item.Entry);
        Assert.Equal("Gone", item.Name);
    }

    [Fact]
    public void RecentIsNewestFirstAndCappedAtFifty()
    {
        var opened = Enumerable.Range(0, 80).Select(i => ($"W{i}", $"n{i}", TestData.Now.AddMinutes(i))).ToList();

        var recent = LensEngine.Recent(opened, _ => null);

        Assert.Equal(50, recent.Count);
        Assert.Equal("W79", recent[0].Key);
        Assert.Equal("W30", recent[^1].Key);
    }

    [Theory]
    [InlineData(LensKind.LiveNow, LensSource.Sessions)]
    [InlineData(LensKind.Friends, LensSource.Sessions)]
    [InlineData(LensKind.New, LensSource.Search)]
    [InlineData(LensKind.GroupWorlds, LensSource.Search)]
    [InlineData(LensKind.Trending, LensSource.History)]
    [InlineData(LensKind.Favorites, LensSource.Local)]
    [InlineData(LensKind.Recent, LensSource.Local)]
    public void EachLensKnowsItsSource(LensKind lens, LensSource source)
    {
        Assert.Equal(source, LensEngine.SourceOf(lens));
    }

    [Fact]
    public void LiveLensesNeedNoRecordSource()
    {
        foreach (var lens in new[] { LensKind.LiveNow, LensKind.Friends })
        {
            Assert.Equal(LensSource.Sessions, LensEngine.SourceOf(lens));
        }
    }

    [Fact]
    public void SearchLensesBuildTheDocumentedQueries()
    {
        Assert.Equal(new RecordQuery { SortBy = RecordSort.FirstPublishTime, Direction = SortDirection.Descending }, RecordLenses.New());
        Assert.Equal(RecordSort.TotalVisits, RecordLenses.Browse().SortBy);
        Assert.Equal(RecordSort.Random, RecordLenses.Random().SortBy);

        var featured = RecordLenses.Featured();
        Assert.True(featured.OnlyFeatured);
        Assert.Equal(RecordSort.LastUpdateDate, featured.SortBy);

        var mine = RecordLenses.MyWorlds("U-me");
        Assert.True(mine.Private);
        Assert.Equal("U-me", mine.ByOwner);
        Assert.Equal(OwnerKind.User, mine.OwnerType);

        var group = RecordLenses.GroupWorlds("G-crew");
        Assert.False(group.Private);
        Assert.Equal(OwnerKind.Group, group.OwnerType);
        Assert.Equal("G-crew", group.ByOwner);
    }

    [Fact]
    public void MinimumVisitsChipFiltersClientSide()
    {
        var records = new[] { TestData.Record("a", visits: 3), TestData.Record("b", visits: 10), TestData.Record("c", visits: 400) };

        Assert.Equal(["b", "c"], RecordLenses.WithMinVisits(records, 10).Select(r => r.Key));
        Assert.Equal(3, RecordLenses.WithMinVisits(records, 0).Count);
    }

    [Fact]
    public async Task PagingWalksTheFakeSourceUntilNoMoreResults()
    {
        var source = new Fakes.FakeRecordSource();
        for (var i = 0; i < 120; i++)
        {
            source.Records.Add(TestData.Record($"R-{i}"));
        }

        var query = RecordLenses.Browse();
        var seen = 0;
        RecordPage page;
        do
        {
            page = await source.SearchAsync(query with { Offset = seen, Count = 50 });
            seen += page.Records.Count;
        }
        while (page.HasMoreResults);

        Assert.Equal(120, seen);
        Assert.Equal(3, source.Queries.Count);
    }
}
