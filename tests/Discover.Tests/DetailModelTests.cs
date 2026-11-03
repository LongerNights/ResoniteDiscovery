using Discover.Core.Model;
using Discover.Core.Presentation;

namespace Discover.Tests;

public class DetailModelTests
{
    [Fact]
    public void ADetailCarriesTheWorldsFacts()
    {
        var entry = new DiscoveryEntry
        {
            Key = "U-a:R-1",
            Record = TestData.Record("U-a:R-1", tags: ["music", "chill"]) with { Description = "A calm place", OwnerName = "Winter" },
            PersonalTags = ["Chill", "mine"],
        };

        var detail = DetailBuilder.Build(entry, CardBuilder.Build(entry, null, TestData.Now), TestData.Now);

        Assert.Equal("A calm place", detail.Description);
        Assert.Contains("by Winter", detail.ByLine);
        Assert.Equal("music, chill, mine", detail.Tags); // the personal "Chill" is not repeated
        Assert.Equal("Not live right now", detail.Sessions);
        Assert.True(detail.CanSave);
    }

    [Fact]
    public void ADetailListsLiveSessionsAndTheDeviceMix()
    {
        var index = TestData.IndexOf(TestData.Session("S-U-Club:Room", name: "Club", active: 12, max: 20));
        var entry = Assert.Single(index.Entries);

        var detail = DetailBuilder.Build(entry, CardBuilder.Build(entry, null, TestData.Now), TestData.Now);

        Assert.Contains("12/20", detail.Sessions.Replace(" ", ""));
        Assert.Equal(CardAction.Join, detail.Action);
    }

    [Fact]
    public void EmptyFieldsGetReadableFallbacks()
    {
        var entry = new DiscoveryEntry { Key = "S:s-random" };

        var detail = DetailBuilder.Build(entry, CardBuilder.Build(entry, null, TestData.Now), TestData.Now);

        Assert.Equal("No description", detail.Description);
        Assert.Equal("No tags", detail.Tags);
        Assert.False(detail.CanSave);
    }
}
