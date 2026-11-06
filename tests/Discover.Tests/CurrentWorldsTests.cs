using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Presentation;

namespace Discover.Tests;

public class CurrentWorldsTests
{
    private static readonly DateTime T = TestData.Now;

    private static OpenWorldSnapshot World(string id, string name = "", bool focused = false, int focusedMinutesAgo = 60, string? record = null, bool host = false, AccessLevel access = AccessLevel.Private) => new()
    {
        SessionId = id,
        Name = name.Length > 0 ? name : id,
        RecordKey = record,
        IsFocused = focused,
        IsHost = host,
        AccessLevel = access,
        Users = 3,
        MaxUsers = 16,
        OpenedAt = T.AddHours(-2),
        LastFocusedAt = T.AddMinutes(-focusedMinutesAgo),
    };

    [Fact]
    public void FocusedWorldComesFirstThenMostRecentlyFocused()
    {
        var items = CurrentWorlds.Build(
            [World("a", focusedMinutesAgo: 30), World("b", focused: true, focusedMinutesAgo: 90), World("c", focusedMinutesAgo: 5)],
            _ => null);

        Assert.Equal(["b", "c", "a"], items.Select(i => i.World.SessionId));
    }

    [Fact]
    public void PrivateWorldsAreListedBecauseTheUserIsAlreadyInThem()
    {
        var item = Assert.Single(CurrentWorlds.Build([World("S-secret", access: AccessLevel.Private)], _ => null));

        Assert.Single(item.Entry.Sessions);
        Assert.Empty(item.Entry.PublicSessions); // never counted in public lenses
    }

    [Fact]
    public void KeyIsTheRecordWhenThereIsOneElseTheSession()
    {
        var withRecord = CurrentWorlds.EntryFor(World("S-1", record: "U-x:R-1"), k => TestData.Record(k, name: "Kitchen"));
        var without = CurrentWorlds.EntryFor(World("S-U-winter:den"), _ => null);

        Assert.Equal("U-x:R-1", withRecord.Key);
        Assert.Equal("Kitchen", withRecord.Record!.Name);
        Assert.True(withRecord.CanSave);
        Assert.Equal("S:s-u-winter:den", without.Key);
        Assert.True(without.CanSave);                                         // custom session ids outlive the session
        Assert.False(CurrentWorlds.EntryFor(World("S-guid"), _ => null).CanSave); // random ids do not
    }

    [Fact]
    public void CardGoesThereAndShowsRoleAccessAndWhereYouAre()
    {
        var items = CurrentWorlds.Build([World("a", focused: true, host: true, access: AccessLevel.Contacts)], _ => null);

        var card = CardBuilder.BuildOpenWorld(items[0], T);

        Assert.Equal(CardAction.GoThere, card.Action);
        Assert.Equal("3/16", card.Users);
        Assert.Equal(["You are here", "Host", "Contacts"], card.Badges.Take(3).Select(b => b.Text));
        Assert.False(card.IsOpen); // the focused world shows "You are here" instead of the Open pill
    }

    [Fact]
    public void UnfocusedWorldKeepsTheOpenPillAndShowsGuest()
    {
        var card = CardBuilder.BuildOpenWorld(CurrentWorlds.Build([World("a")], _ => null)[0], T);

        Assert.True(card.IsOpen);
        Assert.Contains(card.Badges, b => b.Text == "Guest");
        Assert.DoesNotContain(card.Badges, b => b.Kind == BadgeKind.Here);
    }

    [Fact]
    public void CurrentWorldsLeadsTheNowSectionAndCostsNoRequests()
    {
        Assert.Equal(LensKind.CurrentWorlds, LensCatalog.LensesOf(RailSectionId.Now)[0]);
        Assert.Equal(LensSource.OpenWorlds, LensEngine.SourceOf(LensKind.CurrentWorlds));
        Assert.All(Sorting.All, k => Assert.False(Sorting.IsSupported(LensKind.CurrentWorlds, k)));

        var rail = RailBuilder.Build(LensKind.CurrentWorlds, 0, 0, currentWorldsCount: 3);
        Assert.Equal("3", rail[0].Items[0].Count);
        Assert.Equal(LensKind.CurrentWorlds, Assert.Single(rail.SelectMany(s => s.Items), i => i.Selected).Lens);
        Assert.Equal("3 open", RailBuilder.Count(LensKind.CurrentWorlds, 3, 0));
    }

    [Fact]
    public void AnOpenWorldWithoutARecordIsMarkedVisitedUnderItsStableSessionKey()
    {
        var library = new Discover.Core.Persistence.UserLibrary();
        var headless = World("S-U-Elysium:Fastdraw");
        var random = World("S-5f1c2e0a-0000-0000-0000-000000000000");

        Assert.Equal("S:s-u-elysium:fastdraw", CurrentWorlds.KeyOf(headless));
        library.MarkVisited(CurrentWorlds.KeyOf(headless));
        library.MarkVisited(CurrentWorlds.KeyOf(random));

        Assert.True(library.IsVisited("S:s-u-elysium:fastdraw"));
        Assert.False(library.IsVisited(CurrentWorlds.KeyOf(random))); // a random session id vanishes with the session
    }
}
