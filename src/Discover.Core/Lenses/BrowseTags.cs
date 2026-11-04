using Discover.Core.Model;
using Discover.Core.Settings;

namespace Discover.Core.Lenses;

public enum BrowseTagKind
{
    Category,
    Popular,
    Personal,
}

/// <summary>One chip in Browse's recommended-tags row.</summary>
public sealed record BrowseTag(string Label, string Tag, BrowseTagKind Kind);

/// <summary>One button in the chip bar: <see cref="State"/> is null for a recommended tag nobody has picked yet.</summary>
public sealed record ChipView(string Label, string Tag, ChipState? State, bool Personal);

/// <summary>
/// The recommended-tags row for Browse (spec "Browse and recommended tags"): the fixed categories, then the most common
/// published tags in what is on screen, then the user's own tags. Pure and deterministic; costs no requests.
/// </summary>
public static class BrowseTags
{
    public static IReadOnlyList<BrowseTag> Build(
        DiscoverSettings settings,
        IEnumerable<DiscoveryEntry> visible,
        IEnumerable<TagChip> activeChips,
        IReadOnlyCollection<string> personalTags)
    {
        var chips = activeChips.ToList();
        var result = settings.BrowseCategories
            .Where(c => !string.IsNullOrWhiteSpace(c.Tag))
            .Select(c => new BrowseTag(c.Label, c.Tag.Trim(), BrowseTagKind.Category))
            .ToList();

        var taken = new HashSet<string>(result.SelectMany(r => new[] { r.Tag, r.Label }), StringComparer.OrdinalIgnoreCase);
        var excluded = chips.Where(c => c.State == ChipState.Excluded).Select(c => c.Tag);
        foreach (var tag in excluded)
        {
            taken.Add(tag);
        }

        var popular = visible
            .SelectMany(e => PublishedTags(e))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => !taken.Contains(g.Key))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, settings.BrowsePopularTagCount))
            .Select(g => new BrowseTag(g.Key, g.Key, BrowseTagKind.Popular))
            .ToList();
        result.AddRange(popular);
        foreach (var p in popular)
        {
            taken.Add(p.Tag);
        }

        result.AddRange(personalTags
            .Where(t => !string.IsNullOrWhiteSpace(t) && !taken.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(t => new BrowseTag(t, t, BrowseTagKind.Personal)));
        return result;
    }

    /// <summary>The chip bar on a lens that has no recommendations: just the chips the user has set.</summary>
    public static IReadOnlyList<ChipView> ActiveOnly(IEnumerable<TagChip> active) =>
        active.Select(c => new ChipView(c.Tag, c.Tag, c.State, c.Personal)).ToList();

    /// <summary>
    /// Browse's chip bar: each recommended tag, lit when the user has it as a chip, then any chip that is not
    /// recommended so it stays visible and can be cleared. When over <paramref name="max"/>, unlit recommendations
    /// are dropped from the end first; a chip the user set is never dropped.
    /// </summary>
    public static IReadOnlyList<ChipView> Row(IEnumerable<BrowseTag> recommended, IEnumerable<TagChip> active, int max)
    {
        var chips = active.ToList();
        var row = recommended
            .Select(r =>
            {
                var chip = chips.FirstOrDefault(c => string.Equals(c.Tag, r.Tag, StringComparison.OrdinalIgnoreCase));
                return new ChipView(r.Label, r.Tag, chip?.State, r.Kind == BrowseTagKind.Personal || chip?.Personal == true);
            })
            .ToList();
        row.AddRange(chips
            .Where(c => !row.Any(r => string.Equals(r.Tag, c.Tag, StringComparison.OrdinalIgnoreCase)))
            .Select(c => new ChipView(c.Tag, c.Tag, c.State, c.Personal)));

        while (row.Count > max)
        {
            var drop = row.FindLastIndex(r => r.State is null);
            if (drop < 0)
            {
                break;
            }

            row.RemoveAt(drop);
        }

        return row;
    }

    private static IEnumerable<string> PublishedTags(DiscoveryEntry entry) =>
        (entry.Record?.Tags ?? []).Concat(entry.Sessions.SelectMany(s => s.Tags ?? []));
}
