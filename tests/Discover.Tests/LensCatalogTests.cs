using Discover.Core.Lenses;
using Discover.Core.Presentation;
using Discover.Core.Settings;

namespace Discover.Tests;

public class LensCatalogTests
{
    private static readonly DiscoverSettings Defaults = new();

    [Fact]
    public void EverythingIsOnByDefault()
    {
        Assert.All(LensCatalog.Sections.SelectMany(LensCatalog.LensesOf), l => Assert.True(Defaults.IsVisible(l)));
        Assert.Equal(LensKind.LiveNow, LensCatalog.FirstVisible(Defaults));
    }

    [Fact]
    public void HidingASectionTakesItsLensesButRemembersTheirOwnSwitches()
    {
        var withoutLiveNow = LensCatalog.ToggleLens(Defaults, LensKind.LiveNow);
        var browseOff = LensCatalog.ToggleSection(withoutLiveNow, RailSectionId.Browse);

        Assert.All(LensCatalog.LensesOf(RailSectionId.Browse), l => Assert.False(browseOff.IsVisible(l)));

        var browseOn = LensCatalog.ToggleSection(browseOff, RailSectionId.Browse);
        Assert.True(browseOn.IsVisible(LensKind.New));
        Assert.False(browseOn.IsVisible(LensKind.LiveNow)); // its own switch was kept
    }

    [Fact]
    public void TheDefaultLensMovesWhenItIsHidden()
    {
        var hidden = LensCatalog.ToggleLens(Defaults, LensKind.LiveNow);

        Assert.Equal(LensKind.Friends, hidden.DefaultLens);
        Assert.Equal(LensKind.Friends, LensCatalog.FirstVisible(hidden));
    }

    [Fact]
    public void ThereIsAlwaysOneLensLeft()
    {
        var settings = Defaults;
        foreach (var lens in LensCatalog.Sections.SelectMany(LensCatalog.LensesOf).Where(l => l != LensKind.Recent))
        {
            settings = LensCatalog.ToggleLens(settings, lens);
        }

        Assert.Equal([LensKind.Recent], LensCatalog.Openable(settings));
        Assert.Same(settings, LensCatalog.ToggleLens(settings, LensKind.Recent)); // refused: nothing would be left
        Assert.Same(settings, LensCatalog.ToggleSection(settings, RailSectionId.Mine));
    }

    [Fact]
    public void LiveOnlyOverridesTheSearchLenses()
    {
        var live = Defaults with { LiveLensesOnly = true };

        Assert.False(live.IsVisible(LensKind.Featured));
        Assert.False(live.IsVisible(LensKind.MyWorlds));
        Assert.True(live.IsVisible(LensKind.Favorites));
    }

    [Fact]
    public void TheRailFollowsTheSettings()
    {
        var settings = LensCatalog.ToggleLens(LensCatalog.ToggleSection(Defaults, RailSectionId.Browse), LensKind.Recent);

        var rail = RailBuilder.Build(LensKind.LiveNow, 0, 0, visibility: settings);

        Assert.Equal([RailSectionId.Live, RailSectionId.Mine], rail.Select(s => s.Id));
        Assert.DoesNotContain(rail.SelectMany(s => s.Items), i => i.Lens == LensKind.Recent);
    }

    [Fact]
    public void ListsAreOneSwitch()
    {
        var settings = LensCatalog.ToggleLens(Defaults, LensKind.NamedList);
        var lists = new[] { new ListInfo("a", "Games", 1) };

        var mine = RailBuilder.Build(LensKind.Favorites, 0, 0, lists: lists, visibility: settings).Single(s => s.Id == RailSectionId.Mine);

        Assert.DoesNotContain(mine.Items, i => i.Lens == LensKind.NamedList);
    }
}
