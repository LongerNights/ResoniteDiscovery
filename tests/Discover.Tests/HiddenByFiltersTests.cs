using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Presentation;

namespace Discover.Tests;

public class HiddenByFiltersTests
{
    private static DiscoveryEntry Entry(params SessionSnapshot[] sessions) =>
        new() { Key = "U-a:R-1", Sessions = sessions, PublicSessions = sessions };

    [Fact]
    public void RevealingOnlyTurnsOnEmptyWhenTheHiddenWorldIsEmpty()
    {
        var empty = Entry(TestData.Session("S-1", active: 0));
        var filters = new BrowseFilters { HasVr = true, MinVisits = 100 };

        Assert.False(filters.IsListedLive(empty, TestData.Now));
        var revealed = filters.RevealingHidden([empty], TestData.Now);

        Assert.True(revealed.IncludeEmpty);
        Assert.False(revealed.HasVr); // an empty session has no VR users, so that filter was in the way too
        Assert.Equal(100, revealed.MinVisits); // search-lens filters are not touched
        Assert.True(revealed.IsListedLive(empty, TestData.Now));
    }

    [Fact]
    public void RevealingLeavesFiltersThatAreNotInTheWay()
    {
        var busy = Entry(TestData.Session("S-1", active: 3));
        var filters = new BrowseFilters { MinUsers = 10, MinUptimeMinutes = 5 };

        var revealed = filters.RevealingHidden([busy], TestData.Now);

        Assert.Equal(0, revealed.MinUsers);        // 3 users fail "10 or more"
        Assert.Equal(5, revealed.MinUptimeMinutes); // 30 minutes of uptime passes, so it stays
        Assert.False(revealed.IncludeEmpty);
        Assert.True(revealed.IsListedLive(busy, TestData.Now));
    }

    [Fact]
    public void FriendsDoNotNeedEmptySessionsIncluded()
    {
        var empty = Entry(TestData.Session("S-1", active: 0));
        Assert.False(BrowseFilters.None.RevealingHidden([empty], TestData.Now, emptyApplies: false).IncludeEmpty);
        Assert.True(BrowseFilters.None.IsListedLive(empty, TestData.Now, emptyApplies: false));
    }

    [Fact]
    public void AnEmptySessionIsOnlyListedWhenEmptyIsIncluded()
    {
        var entries = new[] { Entry(TestData.Session("S-1", active: 0)) };

        Assert.Empty(LensEngine.LiveNow(entries));
        Assert.Single(LensEngine.LiveNow(entries, includeEmpty: true));
    }

    [Fact]
    public void ThePanelOffersAWorldOrbOnlyWhenTheWorldHasARecord()
    {
        var withRecord = new DiscoveryEntry { Key = "U-a:R-1", Record = TestData.Record("U-a:R-1") };
        var without = Entry(TestData.Session("S-1"));

        Assert.True(DetailBuilder.Build(withRecord, CardBuilder.Build(withRecord, null, TestData.Now), TestData.Now).CanWorldOrb);
        Assert.False(DetailBuilder.Build(without, CardBuilder.Build(without, null, TestData.Now), TestData.Now).CanWorldOrb);
    }
}
