using Discover.Core.Model;
using Discover.Core.Sources;

namespace Discover.Core.Lenses;

/// <summary>The sort menu (spec F14). Session lenses sort locally; search lenses change the server's sort.</summary>
public enum SortKey
{
    LiveUsers,
    FriendsPresent,
    NewestSession,
    DateAdded,
    Visits,
    NewestPublished,
    RecentlyUpdated,
    Name,
    Random,
    Created,
}

public static class Sorting
{
    private static readonly SortKey[] Menu =
    [
        SortKey.LiveUsers, SortKey.FriendsPresent, SortKey.NewestSession, SortKey.DateAdded,
        SortKey.Visits, SortKey.NewestPublished, SortKey.RecentlyUpdated, SortKey.Created,
        SortKey.Name, SortKey.Random,
    ];

    /// <summary>Every sort in menu order; <see cref="IsSupported"/> says which the lens can honour (the rest show disabled).</summary>
    public static IReadOnlyList<SortKey> All => Menu;

    public static bool IsSupported(LensKind lens, SortKey key) => LensEngine.SourceOf(lens) switch
    {
        LensSource.Sessions => key is SortKey.LiveUsers or SortKey.FriendsPresent or SortKey.NewestSession or SortKey.Name or SortKey.Random,
        LensSource.Search => key is SortKey.Visits or SortKey.NewestPublished or SortKey.RecentlyUpdated or SortKey.Created or SortKey.Name or SortKey.Random,
        LensSource.Local => key is SortKey.DateAdded or SortKey.Name,
        _ => false, // open worlds keep their own order: focused first, then last focused
    };

    /// <summary>The lens's own order, as the spec's lens table defines it.</summary>
    public static SortKey Default(LensKind lens) => lens switch
    {
        LensKind.Friends => SortKey.FriendsPresent,
        LensKind.CurrentWorlds => SortKey.LiveUsers,
        LensKind.New => SortKey.NewestPublished,
        LensKind.Browse => SortKey.Visits,
        LensKind.Featured => SortKey.Visits,
        LensKind.Random => SortKey.Random,
        LensKind.MyWorlds or LensKind.GroupWorlds => SortKey.RecentlyUpdated,
        LensKind.Favorites or LensKind.NamedList or LensKind.Recent => SortKey.DateAdded,
        _ => SortKey.LiveUsers,
    };

    /// <summary>Name sorts read A to Z by default; every other sort reads high to low.</summary>
    public static bool DefaultDescending(SortKey key) => key != SortKey.Name;

    public static RecordSort? ToRecordSort(SortKey key) => key switch
    {
        SortKey.Visits => RecordSort.TotalVisits,
        SortKey.NewestPublished => RecordSort.FirstPublishTime,
        SortKey.RecentlyUpdated => RecordSort.LastUpdateDate,
        SortKey.Created => RecordSort.CreationDate,
        SortKey.Name => RecordSort.Name,
        SortKey.Random => RecordSort.Random,
        _ => null,
    };

    /// <summary>
    /// Orders a session lens. Ties fall back to live users, then name, then key, so the order is stable between ticks.
    /// <paramref name="seed"/> fixes the Random order until the user reshuffles.
    /// </summary>
    public static IReadOnlyList<DiscoveryEntry> SortLive(IEnumerable<DiscoveryEntry> entries, SortKey key, bool descending, int seed = 0)
    {
        var list = entries.ToList();
        if (key == SortKey.Random)
        {
            return list
                .OrderBy(e => StableHash(e.Key, seed))
                .ThenBy(e => e.Key, StringComparer.Ordinal)
                .ToList();
        }

        IOrderedEnumerable<DiscoveryEntry> ordered = key switch
        {
            SortKey.FriendsPresent => By(list, e => e.FriendsPresent, descending),
            SortKey.NewestSession => By(list, e => e.Sessions.Count == 0 ? DateTime.MinValue : e.Sessions.Max(s => s.SessionBeginTime), descending),
            SortKey.Name => descending
                ? list.OrderByDescending(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                : list.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => By(list, e => e.Sessions.Sum(s => s.ActiveUsers), descending),
        };

        return ordered
            .ThenByDescending(e => e.Sessions.Sum(s => s.ActiveUsers))
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static IOrderedEnumerable<DiscoveryEntry> By<T>(List<DiscoveryEntry> list, Func<DiscoveryEntry, T> key, bool descending) =>
        descending ? list.OrderByDescending(key) : list.OrderBy(key);

    /// <summary>FNV-1a over the key and seed: deterministic across runs, unlike string.GetHashCode.</summary>
    private static uint StableHash(string key, int seed)
    {
        var hash = 2166136261u ^ (uint)seed;
        foreach (var c in key)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }
}

public enum HostFilter
{
    Any,
    Person,
    Headless,
}

/// <summary>
/// The Filters popover (spec F11, F27). Session filters apply to session lenses; the visit minimum applies to search
/// lenses (the New lens's minimum-visits chip, applied to each returned page client-side).
/// </summary>
public sealed record BrowseFilters
{
    public int MinUsers { get; init; }
    public bool HasRoom { get; init; }
    public bool MobileFriendly { get; init; }
    public bool HasVr { get; init; }
    public HostFilter Host { get; init; } = HostFilter.Any;
    public int MinVisits { get; init; }

    /// <summary>Search lenses: only worlds the server marks featured (stock "Featured only").</summary>
    public bool FeaturedOnly { get; init; }

    /// <summary>Session lenses: only sessions that have been running at least this long (stock "Minimum uptime"); 0 is any.</summary>
    public int MinUptimeMinutes { get; init; }

    /// <summary>Live Now: also list sessions with nobody in them, such as idle headless hosts (stock Headless Sessions).</summary>
    public bool IncludeEmpty { get; init; }

    public static BrowseFilters None { get; } = new();

    /// <summary>How many filters are set for this kind of lens (the badge on the Filters button).</summary>
    public int ActiveCount(LensSource source) => source switch
    {
        LensSource.Search => (MinVisits > 0 ? 1 : 0) + (FeaturedOnly ? 1 : 0),
        LensSource.Local or LensSource.OpenWorlds => 0, // saved and open worlds are filtered by search and chips only
        _ => (MinUsers > 0 ? 1 : 0) + (HasRoom ? 1 : 0) + (MobileFriendly ? 1 : 0) + (HasVr ? 1 : 0) + (Host != HostFilter.Any ? 1 : 0)
            + (MinUptimeMinutes > 0 ? 1 : 0) + (IncludeEmpty ? 1 : 0),
    };

    public bool MatchesLive(DiscoveryEntry entry) => MatchesLive(entry, DateTime.UtcNow);

    public bool MatchesLive(DiscoveryEntry entry, DateTime now)
    {
        if (MinUptimeMinutes > 0)
        {
            if (!entry.Sessions.Any(s => now - s.SessionBeginTime >= TimeSpan.FromMinutes(MinUptimeMinutes)))
            {
                return false;
            }
        }

        if (MinUsers > 0 && entry.Sessions.Sum(s => s.ActiveUsers) < MinUsers)
        {
            return false;
        }

        if (HasRoom && (entry.IsFull || entry.IsOtherVersionOnly))
        {
            return false;
        }

        if (MobileFriendly && !entry.IsMobileFriendly)
        {
            return false;
        }

        if (HasVr && entry.DeviceMix.Vr == 0)
        {
            return false;
        }

        return Host switch
        {
            HostFilter.Person => entry.Sessions.Any(s => !s.HeadlessHost),
            HostFilter.Headless => entry.HasHeadlessHost,
            _ => true,
        };
    }

    public bool MatchesRecord(RecordSnapshot record) => MinVisits <= 0 || record.Visits >= MinVisits;
}

public enum ChipState
{
    Required,
    Excluded,
}

/// <summary>A tag chip: the tag text, its state, and whether it is one of the user's personal tags (never sent to the server).</summary>
public sealed record TagChip(string Tag, ChipState State, bool Personal);

/// <summary>
/// Tag chips (spec F5): clicking a tag adds it as required; clicking the chip again excludes it; a third click clears
/// it. Chips filter session lenses locally and become RequiredTags/ExcludedTags on search lenses.
/// </summary>
public sealed class TagChips
{
    private readonly List<TagChip> _chips = [];

    public IReadOnlyList<TagChip> Items => _chips;
    public bool IsEmpty => _chips.Count == 0;

    /// <summary>Required, then excluded, then gone. Returns the new state, or null when the chip was removed.</summary>
    public ChipState? Cycle(string tag, bool personal = false)
    {
        tag = tag.Trim();
        if (tag.Length == 0)
        {
            return null;
        }

        var index = _chips.FindIndex(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            _chips.Add(new TagChip(tag, ChipState.Required, personal));
            return ChipState.Required;
        }

        if (_chips[index].State == ChipState.Required)
        {
            _chips[index] = _chips[index] with { State = ChipState.Excluded };
            return ChipState.Excluded;
        }

        _chips.RemoveAt(index);
        return null;
    }

    public bool Remove(string tag) => _chips.RemoveAll(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase)) > 0;

    public void Clear() => _chips.Clear();

    /// <summary>Local filtering: required tags must be present, excluded tags absent (record, session or personal tags).</summary>
    public bool Matches(DiscoveryEntry entry)
    {
        foreach (var chip in _chips)
        {
            var has = HasTag(entry, chip.Tag);
            if (chip.State == ChipState.Required ? !has : has)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Adds the non-personal chips to the query's tag lists.</summary>
    public RecordQuery ApplyTo(RecordQuery query) => query with
    {
        RequiredTags = query.RequiredTags.Concat(_chips.Where(c => !c.Personal && c.State == ChipState.Required).Select(c => c.Tag)).ToList(),
        ExcludedTags = query.ExcludedTags.Concat(_chips.Where(c => !c.Personal && c.State == ChipState.Excluded).Select(c => c.Tag)).ToList(),
    };

    private static bool HasTag(DiscoveryEntry entry, string tag) =>
        Contains(entry.Record?.Tags, tag) || Contains(entry.PersonalTags, tag) || entry.Sessions.Any(s => Contains(s.Tags, tag));

    private static bool Contains(IReadOnlyList<string>? tags, string tag) =>
        tags is not null && tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
}
