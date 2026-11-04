using Discover.Core.History;
using Discover.Core.Model;
using Discover.Core.Persistence;
using Xunit;

namespace Discover.Tests;

public class HistoryPersistenceTests
{
    private const string World = "U-winter:R-abc";

    [Fact]
    public void SnapshotRecordsLiveUsersAndVisitsForWorldsOnly()
    {
        var log = new HistoryLog();
        var live = new DiscoveryEntry
        {
            Key = World,
            Sessions = [TestData.Session("a", active: 4)],
            PublicSessions = [TestData.Session("a", active: 4)],
            Record = new RecordSnapshot { Key = World, OwnerId = "U-winter", RecordId = "R-abc", Visits = 120 },
        };
        var standalone = new DiscoveryEntry { Key = DiscoveryKeys.ForStandaloneSession("xyz"), Sessions = [TestData.Session("b", active: 2)], PublicSessions = [TestData.Session("b", active: 2)] };

        log.Snapshot([live, standalone], TestData.Now);

        Assert.Equal(4, Assert.Single(log.LiveSamples(World)).LiveUsers);
        Assert.Equal(120, Assert.Single(log.VisitSamples(World)).Visits);
        Assert.Equal([World], log.Keys);
    }

    [Fact]
    public void HistorySurvivesASaveAndLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gs-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new HistoryLog();
            log.RecordLive(World, TestData.Now, 5);
            log.RecordLive(World, TestData.Now.AddMinutes(1), 7);
            log.RecordVisits(World, TestData.Now, 300);

            var store = new UserLibraryStore(dir);
            store.SaveHistory(log);
            var loaded = new UserLibraryStore(dir).LoadHistory();

            Assert.Equal([5, 7], loaded.LiveSamples(World).Select(s => s.LiveUsers));
            Assert.Equal(300, Assert.Single(loaded.VisitSamples(World)).Visits);
            Assert.Equal(DateTimeKind.Utc, loaded.LiveSamples(World)[0].Utc.Kind);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
