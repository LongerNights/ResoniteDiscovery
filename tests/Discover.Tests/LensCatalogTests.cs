using Discover.Core.Lenses;
using Discover.Core.Presentation;
using Discover.Core.Settings;

namespace Discover.Tests;

public class LensCatalogTests
{
    private static readonly DiscoverSettings Defaults = new();

    [Fact]
    public void EverythingIsOnByDefaultExceptInsights()
    {
        var withoutInsights = LensCatalog.Sections.Where(s => s != RailSectionId.Insights);
        Assert.All(withoutInsights.SelectMany(LensCatalog.LensesOf), l => Assert.True(Defaults.IsVisible(l)));
        Assert.All(LensCatalog.LensesOf(RailSectionId.Insights), l => Assert.False(Defaults.IsVisible(l)));
        Assert.Equal(LensKind.CurrentWorlds, LensCatalog.FirstVisible(Defaults));
    }

    [Fact]
    public void InsightsCanBeSwitchedOn()
    {
        var on = LensCatalog.ToggleSection(Defaults, RailSectionId.Insights);

        Assert.All(LensCatalog.LensesOf(RailSectionId.Insights), l => Assert.True(on.IsVisible(l)));
    }

    [Fact]
    public void HidingASectionTakesItsLensesButRemembersTheirOwnSwitches()
    {
        var withoutLiveNow = LensCatalog.ToggleLens(Defaults, LensKind.LiveNow);
        var browseOff = LensCatalog.ToggleSection(withoutLiveNow, RailSectionId.Discover);

        Assert.All(LensCatalog.LensesOf(RailSectionId.Discover), l => Assert.False(browseOff.IsVisible(l)));

        var browseOn = LensCatalog.ToggleSection(browseOff, RailSectionId.Discover);
        Assert.True(browseOn.IsVisible(LensKind.New));
        Assert.False(browseOn.IsVisible(LensKind.LiveNow)); // its own switch was kept
    }

    [Fact]
    public void TheDefaultLensMovesWhenItIsHidden()
    {
        var hidden = LensCatalog.ToggleLens(Defaults, LensKind.LiveNow);

        Assert.Equal(LensKind.CurrentWorlds, hidden.DefaultLens);
        Assert.Equal(LensKind.CurrentWorlds, LensCatalog.FirstVisible(hidden));
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
        Assert.Same(settings, LensCatalog.ToggleSection(settings, RailSectionId.Yours));
    }

    [Fact]
    public void TheRailFollowsTheSettings()
    {
        var settings = LensCatalog.ToggleLens(LensCatalog.ToggleSection(Defaults, RailSectionId.Discover), LensKind.Recent);

        var rail = RailBuilder.Build(LensKind.LiveNow, 0, 0, visibility: LensCatalog.ToggleSection(settings, RailSectionId.Insights));

        Assert.Equal([RailSectionId.Now, RailSectionId.Yours, RailSectionId.Insights], rail.Select(s => s.Id));
        Assert.DoesNotContain(RailBuilder.Build(LensKind.LiveNow, 0, 0, visibility: settings), s => s.Id == RailSectionId.Insights);
        Assert.DoesNotContain(rail.SelectMany(s => s.Items), i => i.Lens == LensKind.Recent);
    }

    [Fact]
    public void ListsAreOneSwitch()
    {
        var settings = LensCatalog.ToggleLens(Defaults, LensKind.NamedList);
        var lists = new[] { new ListInfo("a", "Games", 1) };

        var mine = RailBuilder.Build(LensKind.Favorites, 0, 0, lists: lists, visibility: settings).Single(s => s.Id == RailSectionId.Yours);

        Assert.DoesNotContain(mine.Items, i => i.Lens == LensKind.NamedList);
    }
}

public class OverhaulCatalogTests
{
    [Fact]
    public void DiscoverHoldsBrowseFirstAndNoMostVisited()
    {
        Assert.Equal([LensKind.Browse, LensKind.Featured, LensKind.New, LensKind.Random], LensCatalog.LensesOf(RailSectionId.Discover));
        Assert.DoesNotContain("MostVisited", Enum.GetNames<LensKind>());
        Assert.Equal(RailSectionId.Discover, LensCatalog.SectionOf(LensKind.Browse));
    }

    [Fact]
    public void BrowseSortsByVisitsDescendingByDefault()
    {
        Assert.Equal(SortKey.Visits, Sorting.Default(LensKind.Browse));
        Assert.Equal(Discover.Core.Model.RecordSort.TotalVisits, RecordLenses.Browse().SortBy);
    }

    [Fact]
    public void SettingsFilesFromBeforeTheOverhaulStillLoad()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"),
            """{"version":1,"defaultLens":"MostVisited","hiddenSections":["Mine","Live"],"hiddenLenses":["MostVisited","Recent"],"listView":true}""");

        var loaded = new Discover.Core.Persistence.UserLibraryStore(dir.Path).LoadSettings();

        Assert.Equal(LensKind.Browse, loaded.DefaultLens);
        Assert.Equal([RailSectionId.Yours, RailSectionId.Now], loaded.HiddenSections);
        Assert.Equal([LensKind.Browse, LensKind.Recent], loaded.HiddenLenses);
        Assert.True(loaded.ListView);
        Assert.Equal(6, loaded.BrowseCategories.Count); // new fields take their defaults
    }
}
