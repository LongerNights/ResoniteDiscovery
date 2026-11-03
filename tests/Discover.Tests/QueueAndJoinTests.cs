using Discover.Core.Model;
using Discover.Core.Sources;

namespace Discover.Tests;

public class QueueAndJoinTests
{
    [Fact]
    public void DrainAppliesAtMostFiftyPerTickAndCarriesTheRest()
    {
        var queue = new SessionChangeQueue();
        var index = new DiscoveryIndex();
        for (var i = 0; i < 120; i++)
        {
            queue.Enqueue(new SessionChange(SessionChangeKind.Added, TestData.Session($"s{i}")));
        }

        queue.Drain(index);
        Assert.Equal(50, index.Count);
        Assert.Equal(70, queue.Pending);

        queue.Drain(index);
        queue.Drain(index);
        Assert.Equal(120, index.Count);
        Assert.Equal(0, queue.Pending);
    }

    [Fact]
    public void ChangesApplyInArrivalOrder()
    {
        var queue = new SessionChangeQueue();
        var index = new DiscoveryIndex();
        queue.Enqueue(new SessionChange(SessionChangeKind.Added, TestData.Session("a", active: 1)));
        queue.Enqueue(new SessionChange(SessionChangeKind.Updated, TestData.Session("a", active: 6)));
        queue.Enqueue(new SessionChange(SessionChangeKind.Removed, TestData.Session("a")));
        queue.Enqueue(new SessionChange(SessionChangeKind.Added, TestData.Session("a", active: 2)));

        queue.Drain(index);

        Assert.Equal(2, Assert.Single(index.Entries).LiveUsers);
    }

    [Fact]
    public async Task EnqueueIsSafeFromManyThreads()
    {
        var queue = new SessionChangeQueue();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
            {
                queue.Enqueue(new SessionChange(SessionChangeKind.Added, TestData.Session($"t{t}-{i}")));
            }
        })));

        Assert.Equal(2000, queue.Pending);
    }

    [Fact]
    public void JoinPrefersTheSessionWithContactsEvenIfSmaller()
    {
        var index = TestData.IndexOf(
            TestData.Session("big", world: "W", active: 8, contacts: 0),
            TestData.Session("small", world: "W", active: 2, contacts: 1));

        var target = JoinPlanner.PickJoinTarget(Assert.Single(index.Entries));

        Assert.Equal("small", target!.SessionId);
    }

    [Fact]
    public void JoinFallsBackToBusiestWithSpace()
    {
        var index = TestData.IndexOf(
            TestData.Session("full", world: "W", active: 10, max: 10),
            TestData.Session("busy", world: "W", active: 6),
            TestData.Session("quiet", world: "W", active: 2));

        Assert.Equal("busy", JoinPlanner.PickJoinTarget(Assert.Single(index.Entries))!.SessionId);
    }

    [Fact]
    public void JoinIsNullWhenEverySessionIsFull()
    {
        var index = TestData.IndexOf(TestData.Session("a", world: "W", active: 10, max: 10));

        Assert.Null(JoinPlanner.PickJoinTarget(Assert.Single(index.Entries)));
    }

    [Fact]
    public void JoinTiesGoToTheNewestSession()
    {
        var index = TestData.IndexOf(
            TestData.Session("old", world: "W", active: 4, ageMinutes: 300),
            TestData.Session("new", world: "W", active: 4, ageMinutes: 5));

        Assert.Equal("new", JoinPlanner.PickJoinTarget(Assert.Single(index.Entries))!.SessionId);
    }
}
