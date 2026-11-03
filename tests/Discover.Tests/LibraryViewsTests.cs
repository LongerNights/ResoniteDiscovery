using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Persistence;

namespace Discover.Tests;

public class LibraryViewsTests
{
    private static UserLibrary Library(TestClock? clock = null) => new(clock ?? new TestClock());

    [Fact]
    public void RecordKeysSplitAtTheFirstColon()
    {
        Assert.True(RecordKeys.TryParse("U-winter:R-1234-abc", out var owner, out var record));
        Assert.Equal("U-winter", owner);
        Assert.Equal("R-1234-abc", record);

        Assert.True(RecordKeys.TryParse("G-Group:R-a:b", out _, out var tail));
        Assert.Equal("R-a:b", tail);

        foreach (var bad in new[] { "", "nocolon", ":R-1", "U-x:" })
        {
            Assert.False(RecordKeys.TryParse(bad, out _, out _));
        }
    }

    [Fact]
    public void FavoritesAndListsFeedTheSavedLens()
    {
        var clock = new TestClock();
        var lib = Library(clock);
        lib.Star("U-a:R-1", "One");
        clock.Advance(TimeSpan.FromHours(1));
        lib.Star("U-a:R-2", "Two");
        var list = lib.CreateList("Games")!;
        lib.AddToList(list.Id, "U-a:R-2", "Two");

        var favorites = LensEngine.Saved(LibraryViews.FavoriteMembers(lib), _ => null);
        var inList = LensEngine.Saved(LibraryViews.ListMembers(lib, list.Id), _ => null);

        Assert.Equal(["U-a:R-2", "U-a:R-1"], favorites.Select(i => i.Key)); // newest saved first
        Assert.Equal(["U-a:R-2"], inList.Select(i => i.Key));
        Assert.Empty(LibraryViews.ListMembers(lib, "missing"));
        Assert.Equal(("Games", 1), (LibraryViews.ListInfos(lib)[0].Name, LibraryViews.ListInfos(lib)[0].Count));
    }

    [Fact]
    public void ASavedWorldWithNoLiveSessionStillGetsAnEntryWithItsIds()
    {
        var item = new SavedItem("U-winter:R-abc", "Cozy Cabin", TestData.Now, null);

        var entry = LibraryViews.EntryFor(item);

        Assert.Equal("Cozy Cabin", entry.DisplayName);
        Assert.Equal("U-winter", entry.Record!.OwnerId);
        Assert.Equal("R-abc", entry.Record.RecordId);
        Assert.Empty(entry.Sessions);
    }

    [Fact]
    public void ALiveEntryIsKeptAsIs()
    {
        var index = TestData.IndexOf(TestData.Session("s", world: "U-a:R-1", active: 4));
        index.TryGetEntry("U-a:R-1", out var live);

        Assert.Same(live, LibraryViews.EntryFor(new SavedItem("U-a:R-1", "One", TestData.Now, live)));
    }

    [Fact]
    public void DecorateAppliesListsAndTagsToWorldsOnly()
    {
        var lib = Library();
        var list = lib.CreateList("Games")!;
        lib.AddToList(list.Id, "U-a:R-1", "One");
        lib.AddTag("U-a:R-1", "chill");

        var world = LibraryViews.Decorate(new DiscoveryEntry { Key = "U-a:R-1" }, lib);
        var standalone = LibraryViews.Decorate(new DiscoveryEntry { Key = "S:abc" }, lib);

        Assert.Equal([list.Id], world.Lists);
        Assert.Equal(["chill"], world.PersonalTags);
        Assert.Empty(standalone.Lists);
        Assert.Same(world, LibraryViews.Decorate(world, null)); // no library: unchanged
    }

    [Fact]
    public void PersonalTagsMakeEntriesSearchable()
    {
        var lib = Library();
        lib.AddTag("U-a:R-1", "chill");
        var entry = LibraryViews.Decorate(new DiscoveryEntry { Key = "U-a:R-1", Record = TestData.Record("U-a:R-1") }, lib);

        Assert.True(Discover.Core.Search.SearchQuery.Parse("+tag:chill").Matches(entry));
        Assert.False(Discover.Core.Search.SearchQuery.Parse("-tag:chill").Matches(entry));
    }

    [Fact]
    public void LocalOrderingByNameAndDirection()
    {
        var t = TestData.Now;
        var items = new[]
        {
            new SavedItem("K3", "charlie", t.AddDays(-1), null),
            new SavedItem("K1", "Alpha", t.AddDays(-3), null),
            new SavedItem("K2", "bravo", t.AddDays(-2), null),
        };

        Assert.Equal(["Alpha", "bravo", "charlie"], LibraryViews.Order(items, SortKey.Name, false).Select(i => i.Name));
        Assert.Equal(["charlie", "bravo", "Alpha"], LibraryViews.Order(items, SortKey.Name, true).Select(i => i.Name));
        Assert.Equal(["K3", "K1", "K2"], LibraryViews.Order(items, SortKey.DateAdded, true).Select(i => i.Key)); // incoming order kept
        Assert.Equal(["K1", "K2", "K3"], LibraryViews.Order(items, SortKey.DateAdded, false).Select(i => i.Key)); // oldest first
    }

    [Fact]
    public void RecentMembersAreNewestFirst()
    {
        var clock = new TestClock();
        var lib = Library(clock);
        lib.RecordOpened("U-a:R-1", "One");
        clock.Advance(TimeSpan.FromMinutes(5));
        lib.RecordOpened("U-a:R-2", "Two");

        var recent = LensEngine.Recent(LibraryViews.RecentMembers(lib), _ => null);

        Assert.Equal(["U-a:R-2", "U-a:R-1"], recent.Select(i => i.Key));
    }
}
