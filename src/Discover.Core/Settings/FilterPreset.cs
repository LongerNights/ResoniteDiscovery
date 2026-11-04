using Discover.Core.Lenses;

namespace Discover.Core.Settings;

/// <summary>
/// A saved view (spec F17): the lens, search text, tag chips, sort and filters, restored exactly. Stored in settings.json.
/// </summary>
public sealed record FilterPreset
{
    public string Name { get; init; } = "";
    public LensKind Lens { get; init; } = LensKind.Browse;

    /// <summary>The named list, for a preset taken on a list; null otherwise.</summary>
    public string? ListId { get; init; }

    public string Search { get; init; } = "";
    public IReadOnlyList<TagChip> Chips { get; init; } = [];
    public SortKey Sort { get; init; }
    public bool Descending { get; init; } = true;
    public BrowseFilters Filters { get; init; } = BrowseFilters.None;
}

public static class FilterPresets
{
    public const int MaxPresets = 10;
    public const int MaxNameLength = 32;

    /// <summary>Adds the preset, or replaces the one with the same name (case-insensitive). Null when the name is blank or the list is full.</summary>
    public static DiscoverSettings? Save(DiscoverSettings settings, FilterPreset preset)
    {
        var name = preset.Name.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (name.Length > MaxNameLength)
        {
            name = name[..MaxNameLength];
        }

        preset = preset with { Name = name };
        var list = settings.Presets.ToList();
        var at = list.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
        {
            list[at] = preset;
        }
        else if (list.Count >= MaxPresets)
        {
            return null;
        }
        else
        {
            list.Add(preset);
        }

        return settings with { Presets = list };
    }

    public static DiscoverSettings Delete(DiscoverSettings settings, string name) =>
        settings with { Presets = settings.Presets.Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList() };
}
