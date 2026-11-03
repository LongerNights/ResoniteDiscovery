using Discover.Core.Model;
using Discover.Core.Sources;
using Discover.Tests.Fakes;

namespace Discover.Tests;

public class MergeTests
{
    [Fact]
    public void SessionsOfOneWorldCollapseToOneEntry()
    {
        var index = TestData.IndexOf(
            TestData.Session("a", world: "U-x:R-1", active: 3),
            TestData.Session("b", world: "u-x:r-1", active: 5));

        var entry = Assert.Single(index.Entries);
        Assert.Equal(2, entry.Sessions.Count);
        Assert.Equal(8, entry.LiveUsers);
        Assert.Equal("b", entry.BestSession!.SessionId);
    }

    [Fact]
    public void SessionsWithoutWorldIdStaySeparate()
    {
        var index = TestData.IndexOf(TestData.Session("a"), TestData.Session("b"));

        Assert.Equal(2, index.Count);
        Assert.All(index.Entries, e => Assert.False(e.IsWorld));
        Assert.True(index.TryGetEntry("S:a", out _));
    }

    [Fact]
    public void RemovingLastSessionRemovesSessionOnlyEntry()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W"), TestData.Session("b", world: "W"));

        index.Apply(new SessionChange(SessionChangeKind.Removed, TestData.Session("a", world: "W")));
        Assert.Equal(1, index.Count);

        index.Apply(new SessionChange(SessionChangeKind.Removed, TestData.Session("B", world: "W")));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void RecordKeepsEntryAfterLastSessionLeaves()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W"));
        index.SetRecord(TestData.Record("W", name: "Cool World"));

        index.Apply(new SessionChange(SessionChangeKind.Removed, TestData.Session("a", world: "W")));

        Assert.True(index.TryGetEntry("W", out var entry));
        Assert.Empty(entry.Sessions);
        Assert.Equal("Cool World", entry.DisplayName);
    }

    [Fact]
    public void HiddenSessionsNeverReachAnEntry()
    {
        var index = TestData.IndexOf(
            TestData.Session("a", world: "W", hidden: true),
            TestData.Session("b", world: "W", access: AccessLevel.Private),
            TestData.Session("c", world: "W", access: AccessLevel.LAN),
            TestData.Session("d", world: "W", access: AccessLevel.Contacts, contacts: 0));

        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void ContactsSessionIsListedButNotPublic()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W", access: AccessLevel.Contacts, contacts: 1, active: 4));

        var entry = Assert.Single(index.Entries);
        Assert.Single(entry.Sessions);
        Assert.Empty(entry.PublicSessions);
        Assert.Equal(0, entry.LiveUsers);
        Assert.Equal(1, entry.FriendsPresent);
    }

    [Fact]
    public void UpdateThatEndsASessionDropsIt()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W"));

        var touched = index.Apply(new SessionChange(SessionChangeKind.Updated, TestData.Session("a", world: "W", ended: true)));

        Assert.Equal(0, index.Count);
        Assert.Contains("W", touched);
    }

    [Fact]
    public void SessionMovingBetweenWorldsRebuildsBothEntries()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W1"), TestData.Session("x", world: "W1"));

        var touched = index.Apply(new SessionChange(SessionChangeKind.Updated, TestData.Session("a", world: "W2")));

        Assert.Equal(2, touched.Count);
        Assert.Single(index.Entries.Single(e => e.Key == "W1").Sessions);
        Assert.Single(index.Entries.Single(e => e.Key == "W2").Sessions);
    }

    [Fact]
    public void SessionIdsAreCaseInsensitive()
    {
        var index = TestData.IndexOf(TestData.Session("S-ABC", active: 2));
        index.Apply(new SessionChange(SessionChangeKind.Updated, TestData.Session("s-abc", active: 7)));

        Assert.Equal(7, Assert.Single(index.Entries).LiveUsers);
    }

    [Fact]
    public void DeviceMixCountsOnlyPresentUsers()
    {
        var users = new SessionUserSnapshot[]
        {
            new("U-1", "a", true, OutputDevice.VR),
            new("U-2", "b", true, OutputDevice.VR),
            new("U-3", "c", true, OutputDevice.Screen),
            new("U-4", "d", false, OutputDevice.Screen),
            new("U-5", "e", true, OutputDevice.Unknown),
        };
        var index = TestData.IndexOf(TestData.Session("a", users: users));

        Assert.Equal(new DeviceMix(2, 1, 0, 1), Assert.Single(index.Entries).DeviceMix);
    }

    [Fact]
    public void DecorationSurvivesSessionChanges()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W"));
        index.Decorate("W", ["list-1"], ["chill"]);

        index.Apply(new SessionChange(SessionChangeKind.Updated, TestData.Session("a", world: "W", active: 9)));

        var entry = Assert.Single(index.Entries);
        Assert.Equal(["list-1"], entry.Lists);
        Assert.Equal(["chill"], entry.PersonalTags);
    }

    [Fact]
    public void FullWorldHasNoBestSession()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W", active: 10, max: 10));

        var entry = Assert.Single(index.Entries);
        Assert.True(entry.IsFull);
        Assert.Null(entry.BestSession);
    }

    [Fact]
    public void IndexFollowsAFakeSourceThroughTheQueue()
    {
        var source = new FakeSessionSource();
        var queue = new SessionChangeQueue();
        source.Changed += queue.Enqueue;
        var index = new DiscoveryIndex();

        source.Add(TestData.Session("a", world: "W"));
        Assert.Equal(0, index.Count); // nothing is applied until the tick

        var touched = queue.Drain(index);

        Assert.Contains("W", touched);
        Assert.Equal(1, index.Count);
    }
}
