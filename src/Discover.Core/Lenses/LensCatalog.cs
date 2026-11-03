using Discover.Core.Settings;

namespace Discover.Core.Lenses;

public enum RailSectionId
{
    Live,
    Browse,
    Mine,
}

/// <summary>
/// The rail's sections and the lenses in them, and which are switched on (spec F28). Pure functions over
/// <see cref="DiscoverSettings"/>; nothing here knows about the engine or the UI.
/// </summary>
public static class LensCatalog
{
    public static IReadOnlyList<RailSectionId> Sections { get; } = [RailSectionId.Live, RailSectionId.Browse, RailSectionId.Mine];

    public static IReadOnlyList<LensKind> LensesOf(RailSectionId section) => section switch
    {
        RailSectionId.Live => [LensKind.LiveNow, LensKind.Friends],
        RailSectionId.Browse => [LensKind.New, LensKind.MostVisited, LensKind.Featured, LensKind.Random],
        _ => [LensKind.Favorites, LensKind.NamedList, LensKind.Recent, LensKind.MyWorlds, LensKind.GroupWorlds],
    };

    public static RailSectionId SectionOf(LensKind lens) =>
        Sections.FirstOrDefault(s => LensesOf(s).Contains(lens), RailSectionId.Browse);

    /// <summary>Whether the section's own switch is on (its lenses may still be off one by one).</summary>
    public static bool IsSectionOn(this DiscoverSettings settings, RailSectionId section) =>
        !settings.HiddenSections.Contains(section);

    /// <summary>
    /// On when the lens's switch and its section's switch are on, and "Live lenses only" is not hiding it (search lenses).
    /// Lenses that are not in the rail (Trending and the other history lenses) follow their section's default, Browse.
    /// </summary>
    public static bool IsVisible(this DiscoverSettings settings, LensKind lens) =>
        settings.IsSectionOn(SectionOf(lens))
        && !settings.HiddenLenses.Contains(lens)
        && !(settings.LiveLensesOnly && LensEngine.SourceOf(lens) == LensSource.Search);

    /// <summary>Lenses a screen can open on its own: visible ones, not counting the named lists (which need a list id).</summary>
    public static IReadOnlyList<LensKind> Openable(DiscoverSettings settings) => Sections
        .SelectMany(LensesOf)
        .Where(l => l != LensKind.NamedList && settings.IsVisible(l))
        .ToList();

    /// <summary>The first lens that is on, in rail order. Live Now when everything is somehow off.</summary>
    public static LensKind FirstVisible(DiscoverSettings settings) =>
        Openable(settings).Cast<LensKind?>().FirstOrDefault() ?? LensKind.LiveNow;

    /// <summary>
    /// Flips a lens's switch. Returns the same instance when that would leave no openable lens, so callers can tell
    /// nothing changed.
    /// </summary>
    public static DiscoverSettings ToggleLens(DiscoverSettings settings, LensKind lens)
    {
        var hidden = settings.HiddenLenses.ToList();
        if (!hidden.Remove(lens))
        {
            hidden.Add(lens);
        }

        return Guard(settings, settings with { HiddenLenses = hidden });
    }

    public static DiscoverSettings ToggleSection(DiscoverSettings settings, RailSectionId section)
    {
        var hidden = settings.HiddenSections.ToList();
        if (!hidden.Remove(section))
        {
            hidden.Add(section);
        }

        return Guard(settings, settings with { HiddenSections = hidden });
    }

    /// <summary>Makes the default lens something that can open (hidden or a named list falls back to the first visible lens).</summary>
    public static DiscoverSettings Normalize(DiscoverSettings settings) =>
        settings.DefaultLens != LensKind.NamedList && settings.IsVisible(settings.DefaultLens)
            ? settings
            : settings with { DefaultLens = FirstVisible(settings) };

    private static DiscoverSettings Guard(DiscoverSettings before, DiscoverSettings after) =>
        Openable(after).Count == 0 ? before : Normalize(after);
}
