using Discover.Core.Model;
using Discover.Core.Presentation;

namespace Discover.Tests;

public class RecordUrlTests
{
    [Fact]
    public void APublishedWorldHasARecordLink()
    {
        var entry = new DiscoveryEntry
        {
            Key = "U-a:R-1",
            Record = TestData.Record("U-a:R-1") with { OwnerId = "U-a", RecordId = "R-1" },
        };

        Assert.Equal("resrec:///U-a/R-1", RecordUrls.For(entry));
        Assert.True(DetailBuilder.Build(entry, CardBuilder.Build(entry, null, TestData.Now), TestData.Now).CanCopyLink);
    }

    [Fact]
    public void ASessionWithNoRecordHasNoLink()
    {
        var entry = new DiscoveryEntry { Key = "S:s-u-club:room" };

        Assert.Null(RecordUrls.For(entry));
        Assert.False(DetailBuilder.Build(entry, CardBuilder.Build(entry, null, TestData.Now), TestData.Now).CanCopyLink);
    }

    [Fact]
    public void ASavedOfflineSessionHasNoLinkEither()
    {
        var entry = Discover.Core.Persistence.LibraryViews.EntryFor(new Discover.Core.Lenses.SavedItem("S:s-u-club:room", "Club", TestData.Now, null));

        Assert.Null(RecordUrls.For(entry)); // its placeholder record has empty ids
    }
}

public class RecordUrlFromKeyTests
{
    [Fact]
    public void ALiveWorldWithoutARecordUsesItsKey()
    {
        var index = TestData.IndexOf(TestData.Session("S1", world: "G-Resonite:R-abc", name: "Cloud"));
        var entry = Assert.Single(index.Entries);

        Assert.Null(entry.Record);
        Assert.Equal("resrec:///G-Resonite/R-abc", RecordUrls.For(entry));
    }
}
