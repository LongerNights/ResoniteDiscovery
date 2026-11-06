using Discover.Core.Model;

namespace Discover.Core.Lenses;

/// <summary>An open world as a discovery entry, plus what the card needs that an entry does not carry.</summary>
public sealed record OpenWorldItem(DiscoveryEntry Entry, OpenWorldSnapshot World);

/// <summary>
/// The Current Worlds lens (spec "Lens definitions"): every open world, focused first, then the most recently focused.
/// It is exempt from the session visibility rule because the user is already in these worlds, so entries are built
/// directly instead of through <see cref="DiscoveryIndex"/>.
/// </summary>
public static class CurrentWorlds
{
    public static IReadOnlyList<OpenWorldItem> Build(IEnumerable<OpenWorldSnapshot> worlds, Func<string, RecordSnapshot?> lookupRecord) => worlds
        .OrderByDescending(w => w.IsFocused)
        .ThenByDescending(w => w.LastFocusedAt)
        .ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(w => w.SessionId, StringComparer.Ordinal)
        .Select(w => new OpenWorldItem(EntryFor(w, lookupRecord), w))
        .ToList();

    /// <summary>
    /// The key an open world's card has: its record, else (a world with no record, such as one a headless host started)
    /// its standalone session key.
    /// </summary>
    public static string KeyOf(OpenWorldSnapshot world) =>
        !string.IsNullOrWhiteSpace(world.RecordKey) ? world.RecordKey : DiscoveryKeys.ForStandaloneSession(world.SessionId);

    public static DiscoveryEntry EntryFor(OpenWorldSnapshot world, Func<string, RecordSnapshot?> lookupRecord)
    {
        var key = KeyOf(world);
        var session = new SessionSnapshot
        {
            SessionId = world.SessionId,
            Name = world.Name,
            WorldKey = string.IsNullOrWhiteSpace(world.RecordKey) ? null : world.RecordKey,
            HostUsername = world.HostUsername,
            ThumbnailUrl = world.ThumbnailUrl,
            ActiveUsers = world.Users,
            JoinedUsers = world.Users,
            MaximumUsers = world.MaxUsers,
            Users = world.UserList,
            AccessLevel = world.AccessLevel,
            SessionBeginTime = world.OpenedAt,
            LastUpdate = world.LastFocusedAt,
        };

        return new DiscoveryEntry
        {
            Key = key,
            Record = world.RecordKey is null ? null : lookupRecord(world.RecordKey),
            Sessions = [session],
            PublicSessions = [],
        };
    }
}
