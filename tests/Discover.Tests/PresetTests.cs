using Discover.Core.Lenses;
using Discover.Core.Persistence;
using Discover.Core.Settings;

namespace Discover.Tests;

public class PresetTests
{
    private static FilterPreset Sample(string name) => new()
    {
        Name = name,
        Lens = LensKind.Featured,
        Search = "+horror -avatar",
        Chips = [new TagChip("social", ChipState.Required, false), new TagChip("pvp", ChipState.Excluded, false)],
        Sort = SortKey.Visits,
        Descending = false,
        Filters = new BrowseFilters { MinVisits = 100, FeaturedOnly = true },
    };

    [Fact]
    public void SaveReplacesSameNameAndCapsTheList()
    {
        var settings = FilterPresets.Save(new DiscoverSettings(), Sample("Spooky"))!;
        settings = FilterPresets.Save(settings, Sample("spooky") with { Search = "x" })!;
        Assert.Single(settings.Presets);
        Assert.Equal("x", settings.Presets[0].Search);

        for (var i = 1; i < FilterPresets.MaxPresets; i++)
        {
            settings = FilterPresets.Save(settings, Sample("p" + i))!;
        }

        Assert.Null(FilterPresets.Save(settings, Sample("one too many")));
        Assert.Null(FilterPresets.Save(new DiscoverSettings(), Sample("   ")));
        Assert.Equal(FilterPresets.MaxPresets - 1, FilterPresets.Delete(settings, "SPOOKY").Presets.Count);
    }

    [Fact]
    public void PresetSurvivesTheSettingsFile()
    {
        using var dir = new TempDir();
        var store = new UserLibraryStore(dir.Path);
        store.SaveSettings(FilterPresets.Save(new DiscoverSettings(), Sample("Spooky"))!);

        var loaded = store.LoadSettings().Presets.Single();
        Assert.Equal(LensKind.Featured, loaded.Lens);
        Assert.Equal("+horror -avatar", loaded.Search);
        Assert.Equal(SortKey.Visits, loaded.Sort);
        Assert.False(loaded.Descending);
        Assert.Equal(100, loaded.Filters.MinVisits);
        Assert.True(loaded.Filters.FeaturedOnly);
        Assert.Equal(["social", "pvp"], loaded.Chips.Select(c => c.Tag));
        Assert.Equal(ChipState.Excluded, loaded.Chips[1].State);
    }
}
