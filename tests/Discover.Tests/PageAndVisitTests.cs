using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Persistence;
using Discover.Core.Settings;

namespace Discover.Tests;

public class PageAndVisitTests
{
    [Theory]
    [InlineData(10, 4, false, 40)]
    [InlineData(5, 3, false, 15)]
    [InlineData(20, 7, false, 98)]   // whole rows only, under the 100 slot cap
    [InlineData(10, 4, true, 40)]    // a list row counts as a quarter of a grid row
    public void PageSizeIsWholeRowsUnderTheSlotCap(int rows, int perRow, bool list, int expected)
    {
        var settings = new DiscoverSettings { RowsPerPage = rows, CardsPerRow = perRow, ListView = list };

        Assert.Equal(expected, settings.PageSizeFor());
    }

    [Fact]
    public void FeaturedSortsByVisitsByDefault()
    {
        Assert.Equal(SortKey.Visits, Sorting.Default(LensKind.Featured));
        Assert.Equal(RecordSort.TotalVisits, RecordLenses.Featured().SortBy);
    }

    [Fact]
    public void VisitedIsRememberedOnceAndOnlyForWorlds()
    {
        var lib = new UserLibrary(new TestClock());
        var changes = 0;
        lib.Changed += _ => changes++;

        lib.MarkVisited("S:abc");
        Assert.False(lib.IsVisited("S:abc"));

        lib.MarkVisited("U-x:R-1");
        lib.MarkVisited("u-x:r-1");

        Assert.True(lib.IsVisited("U-X:R-1"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void OpeningAWorldFromHereCountsAsVisited()
    {
        var lib = new UserLibrary(new TestClock());

        lib.RecordOpened("U-x:R-2", "Two");

        Assert.True(lib.IsVisited("U-x:R-2"));
    }

    [Fact]
    public void OpenWorldEntriesCarryTheirUsers()
    {
        var world = new OpenWorldSnapshot
        {
            SessionId = "S-1",
            RecordKey = "U-x:R-3",
            Users = 2,
            UserList = [new SessionUserSnapshot("U-a", "Ann", true, OutputDevice.Unknown, true), new SessionUserSnapshot("U-b", "Bob", true, OutputDevice.Unknown)],
        };

        var entry = CurrentWorlds.EntryFor(world, _ => null);

        Assert.Equal(["Ann", "Bob"], entry.Sessions.Single().Users.Select(u => u.Username));
    }
}
