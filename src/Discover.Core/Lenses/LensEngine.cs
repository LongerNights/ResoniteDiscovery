using Discover.Core.Model;
using Discover.Core.Sources;

namespace Discover.Core.Lenses;

[System.Text.Json.Serialization.JsonConverter(typeof(LegacyNames.LensConverter))]
public enum LensKind
{
    LiveNow,
    Friends,
    New,
    Browse,
    Featured,
    Random,
    Trending,
    Rising,
    HiddenGems,
    Favorites,
    NamedList,
    MyWorlds,
    GroupWorlds,
    Recent,
    CurrentWorlds,
}

public enum LensSource
{
    /// <summary>Engine session cache; costs no requests.</summary>
    Sessions,

    /// <summary>Record search through <see cref="IRecordSource"/>.</summary>
    Search,

    /// <summary>Local history snapshots (v1.1).</summary>
    History,

    /// <summary>User data on disk.</summary>
    Local,

    /// <summary>The worlds the user has open, from the world manager; costs no requests.</summary>
    OpenWorlds,
}

/// <summary>A saved or recent world. <see cref="Entry"/> is null when the world has no live session and no hydrated record yet.</summary>
public sealed record SavedItem(string Key, string Name, DateTime Date, DiscoveryEntry? Entry);

/// <summary>Lens definitions (spec "Lens definitions"). Pure functions over entries; no engine, no I/O.</summary>
public static class LensEngine
{
    public const int RecentLimit = 50;

    public static LensSource SourceOf(LensKind lens) => lens switch
    {
        LensKind.CurrentWorlds => LensSource.OpenWorlds,
        LensKind.LiveNow or LensKind.Friends => LensSource.Sessions,
        LensKind.Browse or LensKind.New or LensKind.Featured or LensKind.Random
            or LensKind.MyWorlds or LensKind.GroupWorlds => LensSource.Search,
        LensKind.Trending or LensKind.Rising or LensKind.HiddenGems => LensSource.History,
        _ => LensSource.Local,
    };

    /// <summary>Public sessions, and contacts-only ones with a contact present, with 1 or more active users. Sorted by LiveUsers, ties by FriendsPresent, then name.</summary>
    public static IReadOnlyList<DiscoveryEntry> LiveNow(IEnumerable<DiscoveryEntry> entries, bool includeEmpty = false) => entries
        .Where(e => e.LiveSessions.Any(s => includeEmpty || s.ActiveUsers >= 1))
        .OrderByDescending(e => e.LiveNowUsers)
        .ThenByDescending(e => e.FriendsPresent)
        .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(e => e.Key, StringComparer.Ordinal)
        .ToList();

    /// <summary>Entries with a contact present, at any contact-visible access level. Sorted by FriendsPresent.</summary>
    public static IReadOnlyList<DiscoveryEntry> Friends(IEnumerable<DiscoveryEntry> entries) => entries
        .Where(e => e.FriendsPresent > 0)
        .OrderByDescending(e => e.FriendsPresent)
        .ThenByDescending(e => e.LiveUsers)
        .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(e => e.Key, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Favorites and named lists: live entries first by LiveUsers, then date added newest first
    /// (spec "Favorites" row; decisions.md). Members without an entry keep their place after the live ones.
    /// </summary>
    public static IReadOnlyList<SavedItem> Saved(IEnumerable<(string Key, string Name, DateTime AddedAt)> members, Func<string, DiscoveryEntry?> lookup) => members
        .Select(m => new SavedItem(m.Key, m.Name, m.AddedAt, lookup(m.Key)))
        .OrderByDescending(i => i.Entry?.LiveUsers > 0)
        .ThenByDescending(i => i.Entry?.LiveUsers ?? 0)
        .ThenByDescending(i => i.Date)
        .ThenBy(i => i.Key, StringComparer.Ordinal)
        .ToList();

    /// <summary>Worlds opened through the screen, newest first, capped at <see cref="RecentLimit"/>.</summary>
    public static IReadOnlyList<SavedItem> Recent(IEnumerable<(string Key, string Name, DateTime OpenedAt)> opened, Func<string, DiscoveryEntry?> lookup, int limit = RecentLimit) => opened
        .OrderByDescending(o => o.OpenedAt)
        .Take(limit)
        .Select(o => new SavedItem(o.Key, o.Name, o.OpenedAt, lookup(o.Key)))
        .ToList();
}

/// <summary>The search each search-backed lens sends (spec "Lens definitions"). Paging fields are the caller's.</summary>
public static class RecordLenses
{
    public static RecordQuery New() => new() { SortBy = RecordSort.FirstPublishTime, Direction = SortDirection.Descending };

    public static RecordQuery Browse() => new() { SortBy = RecordSort.TotalVisits, Direction = SortDirection.Descending };

    public static RecordQuery Featured() => new() { SortBy = RecordSort.TotalVisits, Direction = SortDirection.Descending, OnlyFeatured = true };

    public static RecordQuery Random() => new() { SortBy = RecordSort.Random };

    /// <summary>The signed-in user's own worlds. <c>Private = true</c> as the stock UI does for own worlds (V8).</summary>
    public static RecordQuery MyWorlds(string userId) => new()
    {
        SortBy = RecordSort.LastUpdateDate,
        Direction = SortDirection.Descending,
        ByOwner = userId,
        OwnerType = OwnerKind.User,
        Private = true,
    };

    public static RecordQuery GroupWorlds(string groupId) => new()
    {
        SortBy = RecordSort.LastUpdateDate,
        Direction = SortDirection.Descending,
        ByOwner = groupId,
        OwnerType = OwnerKind.Group,
    };

    /// <summary>The New lens's optional minimum-visits chip (F27). Applied client-side; 0 or less turns it off.</summary>
    /// <summary>Narrows a search lens to one author's worlds. Group ids start with "G-", user ids with "U-".</summary>
    public static RecordQuery ByAuthor(RecordQuery query, string ownerId) => query with
    {
        ByOwner = ownerId,
        OwnerType = ownerId.StartsWith("G-", StringComparison.OrdinalIgnoreCase) ? OwnerKind.Group : OwnerKind.User,
    };

    public static IReadOnlyList<RecordSnapshot> WithMinVisits(IEnumerable<RecordSnapshot> records, int minVisits) =>
        minVisits <= 0 ? records.ToList() : records.Where(r => r.Visits >= minVisits).ToList();
}
