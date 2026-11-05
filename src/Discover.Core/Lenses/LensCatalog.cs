using System.Text.Json;
using System.Text.Json.Serialization;
using Discover.Core.Settings;

namespace Discover.Core.Lenses;

/// <summary>
/// Rail sections (spec "The rail"). Older settings files used Live, Browse and Mine; <see cref="LegacyNames"/> reads those.
/// </summary>
[JsonConverter(typeof(LegacyNames.SectionConverter))]
public enum RailSectionId
{
    Now,
    Discover,
    Yours,

    /// <summary>Trending, Rising and Hidden Gems, from this client's local history (v1.1). Off until switched on in Settings.</summary>
    Insights,
}

/// <summary>
/// The rail's sections and the lenses in them, and which are switched on (spec F28). Pure functions over
/// <see cref="DiscoverSettings"/>; nothing here knows about the engine or the UI.
/// </summary>
public static class LensCatalog
{
    public static IReadOnlyList<RailSectionId> Sections { get; } = [RailSectionId.Now, RailSectionId.Discover, RailSectionId.Yours, RailSectionId.Insights];

    public static IReadOnlyList<LensKind> LensesOf(RailSectionId section) => section switch
    {
        RailSectionId.Now => [LensKind.CurrentWorlds, LensKind.LiveNow, LensKind.Friends],
        RailSectionId.Discover => [LensKind.Browse, LensKind.Featured, LensKind.New, LensKind.Random],
        RailSectionId.Insights => [LensKind.Trending, LensKind.Rising, LensKind.HiddenGems],
        _ => [LensKind.Favorites, LensKind.NamedList, LensKind.Recent, LensKind.MyWorlds, LensKind.GroupWorlds],
    };

    public static RailSectionId SectionOf(LensKind lens) =>
        Sections.FirstOrDefault(s => LensesOf(s).Contains(lens), RailSectionId.Discover);

    /// <summary>Whether the section's own switch is on (its lenses may still be off one by one).</summary>
    public static bool IsSectionOn(this DiscoverSettings settings, RailSectionId section) =>
        !settings.HiddenSections.Contains(section);

    /// <summary>
    /// On when the lens's switch and its section's switch are on, and "Live lenses only" is not hiding it (search lenses).
    /// Insights lenses are in their own section, which is on by default like the others.
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
    public static DiscoverSettings Normalize(DiscoverSettings settings)
    {
        settings = settings.Sanitize();
        return settings.DefaultLens != LensKind.NamedList && settings.IsVisible(settings.DefaultLens)
            ? settings
            : settings with { DefaultLens = FirstVisible(settings) };
    }

    private static DiscoverSettings Guard(DiscoverSettings before, DiscoverSettings after) =>
        Openable(after).Count == 0 ? before : Normalize(after);
}

/// <summary>
/// Reads the names settings files used before the Worlds tab overhaul (spec F34): the MostVisited lens is now Browse, and
/// the Live, Browse and Mine sections are now Now, Discover and Yours. Writing always uses the new names.
/// </summary>
public static class LegacyNames
{
    internal static string MapLens(string name) => name == "MostVisited" ? nameof(LensKind.Browse) : name;

    internal static string MapSection(string name) => name switch
    {
        "Live" => nameof(RailSectionId.Now),
        "Browse" => nameof(RailSectionId.Discover),
        "Mine" => nameof(RailSectionId.Yours),
        _ => name,
    };

    public sealed class LensConverter : JsonConverter<LensKind>
    {
        public override LensKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Enum.TryParse<LensKind>(MapLens(reader.GetString() ?? ""), ignoreCase: true, out var lens) ? lens : LensKind.LiveNow;

        public override void Write(Utf8JsonWriter writer, LensKind value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    public sealed class SectionConverter : JsonConverter<RailSectionId>
    {
        public override RailSectionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Enum.TryParse<RailSectionId>(MapSection(reader.GetString() ?? ""), ignoreCase: true, out var section) ? section : RailSectionId.Discover;

        public override void Write(Utf8JsonWriter writer, RailSectionId value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
