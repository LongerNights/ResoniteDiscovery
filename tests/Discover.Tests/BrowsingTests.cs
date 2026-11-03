using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Sources;

namespace Discover.Tests;

public class BrowsingTests
{
    private static DiscoveryEntry Entry(string key, params SessionSnapshot[] sessions) =>
        new() { Key = key, Sessions = sessions, PublicSessions = sessions };

    // ---- Sorting (F14)

    [Theory]
    [InlineData(LensKind.LiveNow, SortKey.LiveUsers, true)]
    [InlineData(LensKind.LiveNow, SortKey.Visits, false)]
    [InlineData(LensKind.Friends, SortKey.NewestSession, true)]
    [InlineData(LensKind.MostVisited, SortKey.Visits, true)]
    [InlineData(LensKind.MostVisited, SortKey.LiveUsers, false)]
    [InlineData(LensKind.New, SortKey.FriendsPresent, false)]
    [InlineData(LensKind.Featured, SortKey.Name, true)]
    public void SortsTheLensCannotHonourAreUnsupported(LensKind lens, SortKey key, bool supported)
    {
        Assert.Equal(supported, Sorting.IsSupported(lens, key));
    }

    [Fact]
    public void EveryLensDefaultIsSupported()
    {
        foreach (var lens in new[] { LensKind.LiveNow, LensKind.Friends, LensKind.New, LensKind.MostVisited, LensKind.Featured, LensKind.Random })
        {
            Assert.True(Sorting.IsSupported(lens, Sorting.Default(lens)), lens.ToString());
        }
    }

    [Fact]
    public void SearchSortsMapToServerSorts()
    {
        Assert.Equal(RecordSort.TotalVisits, Sorting.ToRecordSort(SortKey.Visits));
        Assert.Equal(RecordSort.FirstPublishTime, Sorting.ToRecordSort(SortKey.NewestPublished));
        Assert.Null(Sorting.ToRecordSort(SortKey.LiveUsers));
    }

    [Fact]
    public void LiveSortByUsersFriendsAndNewest()
    {
        var a = Entry("A", TestData.Session("a1", active: 3, contacts: 2, ageMinutes: 60));
        var b = Entry("B", TestData.Session("b1", active: 9, contacts: 0, ageMinutes: 10));
        var c = Entry("C", TestData.Session("c1", active: 5, contacts: 1, ageMinutes: 200));
        var all = new[] { a, b, c };

        Assert.Equal(["B", "C", "A"], Sorting.SortLive(all, SortKey.LiveUsers, true).Select(e => e.Key));
        Assert.Equal(["A", "C", "B"], Sorting.SortLive(all, SortKey.LiveUsers, false).Select(e => e.Key));
        Assert.Equal(["A", "C", "B"], Sorting.SortLive(all, SortKey.FriendsPresent, true).Select(e => e.Key));
        Assert.Equal(["B", "A", "C"], Sorting.SortLive(all, SortKey.NewestSession, true).Select(e => e.Key));
    }

    [Fact]
    public void NameSortIsCaseInsensitive()
    {
        var entries = new[] { "beta", "Alpha", "charlie" }.Select(n => Entry(n, TestData.Session(n, name: n))).ToList();

        Assert.Equal(["Alpha", "beta", "charlie"], Sorting.SortLive(entries, SortKey.Name, false).Select(e => e.DisplayName));
    }

    [Fact]
    public void RandomIsStableForASeedAndChangesWithIt()
    {
        var entries = Enumerable.Range(0, 30).Select(i => Entry("K" + i, TestData.Session("s" + i))).ToList();

        var first = Sorting.SortLive(entries, SortKey.Random, true, seed: 7).Select(e => e.Key).ToList();
        var again = Sorting.SortLive(entries, SortKey.Random, true, seed: 7).Select(e => e.Key).ToList();
        var other = Sorting.SortLive(entries, SortKey.Random, true, seed: 8).Select(e => e.Key).ToList();

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.Equal(30, other.Distinct().Count());
    }

    // ---- Filters (F11, F27)

    [Fact]
    public void FiltersNarrowTheSetInTheExpectedDirection()
    {
        var vr = new SessionUserSnapshot("U-1", "a", true, OutputDevice.VR);
        var busyFull = Entry("full", TestData.Session("f", active: 10, max: 10));
        var mobile = TestData.Session("m", active: 2) with { MobileFriendly = true };
        var mobileEntry = Entry("mobile", mobile);
        var headless = Entry("headless", TestData.Session("h", active: 1) with { HeadlessHost = true, Users = [vr] });
        var all = new[] { busyFull, mobileEntry, headless };

        int Count(BrowseFilters f) => all.Count(f.MatchesLive);

        Assert.Equal(3, Count(BrowseFilters.None));
        Assert.Equal(2, Count(BrowseFilters.None with { MinUsers = 2 }));
        Assert.Equal(2, Count(BrowseFilters.None with { HasRoom = true }));
        Assert.Equal(1, Count(BrowseFilters.None with { MobileFriendly = true }));
        Assert.Equal(1, Count(BrowseFilters.None with { HasVr = true }));
        Assert.Equal(1, Count(BrowseFilters.None with { Host = HostFilter.Headless }));
        Assert.Equal(2, Count(BrowseFilters.None with { Host = HostFilter.Person }));
    }

    [Fact]
    public void ActiveCountDependsOnTheLensKind()
    {
        var f = BrowseFilters.None with { HasRoom = true, HasVr = true, MinVisits = 10 };

        Assert.Equal(2, f.ActiveCount(LensSource.Sessions));
        Assert.Equal(1, f.ActiveCount(LensSource.Search));
    }

    [Fact]
    public void MinimumVisitsAppliesToRecords()
    {
        var f = BrowseFilters.None with { MinVisits = 10 };

        Assert.False(f.MatchesRecord(TestData.Record("a", visits: 9)));
        Assert.True(f.MatchesRecord(TestData.Record("b", visits: 10)));
        Assert.True(BrowseFilters.None.MatchesRecord(TestData.Record("c", visits: 0)));
    }

    [Fact]
    public void OtherVersionSessionsAreNeverJoinTargets()
    {
        var entry = Entry("W",
            TestData.Session("old", active: 9) with { IsCompatible = false },
            TestData.Session("ok", active: 2));

        Assert.Equal("ok", JoinPlanner.PickJoinTarget(entry)!.SessionId);
        Assert.Single(JoinPlanner.JoinableSessions(entry));

        var onlyOld = Entry("W2", TestData.Session("old2") with { IsCompatible = false });
        Assert.True(onlyOld.IsOtherVersionOnly);
        Assert.False(onlyOld.IsFull);
        Assert.Null(JoinPlanner.PickJoinTarget(onlyOld));
        Assert.False((BrowseFilters.None with { HasRoom = true }).MatchesLive(onlyOld));
    }

    // ---- Tag chips (F5)

    [Fact]
    public void ChipsCycleRequiredExcludedCleared()
    {
        var chips = new TagChips();

        Assert.Equal(ChipState.Required, chips.Cycle("horror"));
        Assert.Equal(ChipState.Excluded, chips.Cycle("HORROR"));
        Assert.Null(chips.Cycle("horror"));
        Assert.True(chips.IsEmpty);
        Assert.Null(chips.Cycle("   "));
    }

    [Fact]
    public void ChipsBecomeQueryTagsExceptPersonalOnes()
    {
        var chips = new TagChips();
        chips.Cycle("horror");
        chips.Cycle("avatar");
        chips.Cycle("avatar");
        chips.Cycle("chill", personal: true);

        var query = chips.ApplyTo(new RecordQuery());

        Assert.Equal(["horror"], query.RequiredTags);
        Assert.Equal(["avatar"], query.ExcludedTags);
    }

    [Fact]
    public void ChipsFilterLocally()
    {
        var spooky = Entry("A", TestData.Session("a", tags: ["horror", "dark"]));
        var cozy = new DiscoveryEntry { Key = "B", Record = TestData.Record("B", tags: ["cozy"]), PersonalTags = ["chill"] };
        var chips = new TagChips();

        chips.Cycle("horror");
        Assert.True(chips.Matches(spooky));
        Assert.False(chips.Matches(cozy));

        chips.Clear();
        chips.Cycle("chill", personal: true);
        Assert.True(chips.Matches(cozy));
        chips.Cycle("chill");
        Assert.False(chips.Matches(cozy));
    }
}
