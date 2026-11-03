using Discover.Core.Model;
using Discover.Core.Sources;
using Discover.Tests.Fakes;

namespace Discover.Tests;

public class FakeSourceTests
{
    [Fact]
    public void SessionSourceRaisesChangesInOrder()
    {
        var source = new FakeSessionSource();
        var seen = new List<SessionChangeKind>();
        source.Changed += c => seen.Add(c.Kind);

        var s = new SessionSnapshot { SessionId = "S-abc", ActiveUsers = 3 };
        source.Add(s);
        source.Update(s with { ActiveUsers = 4 });
        source.Remove("s-ABC"); // ids are case-insensitive, like the engine's lookup

        Assert.Equal(new[] { SessionChangeKind.Added, SessionChangeKind.Updated, SessionChangeKind.Removed }, seen);
        Assert.Empty(source.GetSessions());
    }

    [Fact]
    public async Task RecordSourcePagesUntilNoMore()
    {
        var source = new FakeRecordSource();
        for (var i = 0; i < 120; i++)
        {
            source.Records.Add(new RecordSnapshot { Key = $"R-{i}", OwnerId = "U-x", RecordId = $"R-{i}" });
        }

        var first = await source.SearchAsync(new RecordQuery { Count = 100 });
        var second = await source.SearchAsync(new RecordQuery { Count = 100, Offset = 100 });

        Assert.True(first.HasMoreResults);
        Assert.Equal(20, second.Records.Count);
        Assert.False(second.HasMoreResults);
    }
}
