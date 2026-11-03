using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Persistence;
using Discover.Core.Presentation;

namespace Discover.Tests;

public class StableSessionTests
{
    private const string Custom = "S:s-u-elysiumheadless:blackjack";

    [Theory]
    [InlineData("U-owner:R-123", true)]
    [InlineData("S:s-u-elysiumheadless:blackjack", true)]
    [InlineData("S:S-U-Elysium:Room", true)]
    [InlineData("S:s-9f3a-random-guid", false)]
    [InlineData("S:abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyWorldsAndCustomSessionsAreSavable(string? key, bool savable)
    {
        Assert.Equal(savable, DiscoveryKeys.IsSavable(key));
    }

    [Fact]
    public void CustomSessionKeysMatchWhatTheIndexUses()
    {
        var index = TestData.IndexOf(TestData.Session("S-U-ElysiumHeadless:Blackjack", name: "Elysium"));

        var entry = Assert.Single(index.Entries);

        Assert.Equal(Custom, entry.Key);
        Assert.True(entry.CanSave);
        Assert.False(entry.IsWorld);
    }

    [Fact]
    public void ARandomSessionCannotBeSaved()
    {
        var index = TestData.IndexOf(TestData.Session("S-0f8a1c", name: "Open Meditation"));

        Assert.False(Assert.Single(index.Entries).CanSave);
    }

    [Fact]
    public void ALibrarySavesCustomSessionsEverywhere()
    {
        var lib = new UserLibrary(new TestClock());
        var list = lib.CreateList("Clubs")!;

        Assert.True(lib.Star(Custom, "Elysium"));
        Assert.True(lib.AddToList(list.Id, Custom, "Elysium"));
        Assert.True(lib.AddTag(Custom, "club"));
        lib.RecordOpened(Custom, "Elysium");

        Assert.True(lib.IsStarred(Custom));
        Assert.Equal([list.Id], lib.ListIdsContaining(Custom));
        Assert.Equal(["club"], lib.TagsFor(Custom));
        Assert.Equal(Custom, Assert.Single(lib.Recent).Key);

        Assert.False(lib.Star("S:s-random", "nope"));
    }

    [Fact]
    public void AnOfflineSavedSessionShowsItsNameButCannotStartAnything()
    {
        var item = new SavedItem(Custom, "Elysium", TestData.Now, null);

        var entry = LibraryViews.EntryFor(item);
        var card = CardBuilder.Build(entry, null, TestData.Now);

        Assert.Equal("Elysium", entry.DisplayName);
        Assert.Equal("", entry.Record!.OwnerId);
        Assert.Equal(CardAction.Offline, card.Action);
    }

    [Fact]
    public void ALiveCustomSessionJoinsAsNormalWhenSaved()
    {
        var index = TestData.IndexOf(TestData.Session("S-U-ElysiumHeadless:Blackjack", name: "Elysium", active: 13, max: 35));
        index.TryGetEntry(Custom, out var live);

        var item = Assert.Single(LensEngine.Saved([(Custom, "Elysium", TestData.Now)], k => index.TryGetEntry(k, out var e) ? e : null));
        var card = CardBuilder.Build(LibraryViews.EntryFor(item), null, TestData.Now);

        Assert.Same(live, item.Entry);
        Assert.Equal(CardAction.Join, card.Action);
    }

    [Fact]
    public void ExportAndImportKeepCustomSessions()
    {
        var a = new UserLibrary(new TestClock());
        a.Star(Custom, "Elysium");

        var b = new UserLibrary(new TestClock());
        b.Import(a.Export());

        Assert.True(b.IsStarred(Custom));
    }

    [Fact]
    public void ADecoratedCustomSessionCarriesItsTagsAndLists()
    {
        var lib = new UserLibrary(new TestClock());
        lib.AddTag(Custom, "club");

        var entry = LibraryViews.Decorate(new DiscoveryEntry { Key = Custom }, lib);

        Assert.Equal(["club"], entry.PersonalTags);
    }
}
