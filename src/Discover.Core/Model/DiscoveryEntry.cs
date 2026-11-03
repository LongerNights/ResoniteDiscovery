namespace Discover.Core.Model;

/// <summary>Present-user counts by output device across an entry's listed sessions.</summary>
public readonly record struct DeviceMix(int Vr, int Screen, int Camera, int Unknown)
{
    public int Total => Vr + Screen + Camera + Unknown;
}

public static class DiscoveryKeys
{
    public const string StandalonePrefix = "S:";

    public static string ForStandaloneSession(string sessionId) => StandalonePrefix + sessionId.ToLowerInvariant();

    /// <summary>The key of a published world (<c>OwnerId:RecordId</c>), not of a standalone session.</summary>
    public static bool IsWorldKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && !key.StartsWith(StandalonePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Custom session ids (<c>S-U-owner:name</c>, chosen by whoever hosts, e.g. a headless Elysium) outlive the session,
    /// so a standalone session with one has a stable key. Mirrors the engine's <c>SessionInfo.IsCustomSessionId</c>.
    /// </summary>
    public static bool IsStableSessionKey(string? key) =>
        key is not null && key.StartsWith(StandalonePrefix + "s-u-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What can be starred, listed, tagged or kept in Recent: published worlds, and standalone sessions with a custom
    /// (stable) id. A random session id (<c>S-guid</c>) vanishes when the session ends, so it cannot be saved.
    /// </summary>
    public static bool IsSavable(string? key) => IsWorldKey(key) || IsStableSessionKey(key);
}

/// <summary>One world, or one standalone session (spec "DiscoveryEntry"). Built by <see cref="DiscoveryIndex"/>.</summary>
public sealed record DiscoveryEntry
{
    public required string Key { get; init; }

    /// <summary>Null until hydrated, or for worlds that exist only as sessions.</summary>
    public RecordSnapshot? Record { get; init; }

    /// <summary>Every listed session (public and friends-only), busiest first. Hidden sessions are never here.</summary>
    public IReadOnlyList<SessionSnapshot> Sessions { get; init; } = [];

    /// <summary>The subset visible in public lenses. Contacts-only sessions are excluded.</summary>
    public IReadOnlyList<SessionSnapshot> PublicSessions { get; init; } = [];

    /// <summary>Ids of the user's named lists containing this world. Filled from <see cref="Persistence.UserLibrary"/>.</summary>
    public IReadOnlyList<string> Lists { get; init; } = [];

    public IReadOnlyList<string> PersonalTags { get; init; } = [];

    public bool IsWorld => DiscoveryKeys.IsWorldKey(Key);

    /// <summary>Can be starred, listed, tagged and recorded in Recent (a world, or a session with a stable custom id).</summary>
    public bool CanSave => DiscoveryKeys.IsSavable(Key);

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Record?.Name))
            {
                return Record.Name;
            }

            var session = BestSession ?? Sessions.FirstOrDefault();
            return string.IsNullOrWhiteSpace(session?.Name) ? Key : session.Name;
        }
    }

    /// <summary>Sum of ActiveUsers over public sessions.</summary>
    public int LiveUsers => PublicSessions.Sum(s => s.ActiveUsers);

    /// <summary>Sum of present contacts over all listed sessions.</summary>
    public int FriendsPresent => Sessions.Sum(s => s.PresentContacts);

    public DeviceMix DeviceMix
    {
        get
        {
            int vr = 0, screen = 0, camera = 0, unknown = 0;
            foreach (var user in Sessions.SelectMany(s => s.Users).Where(u => u.IsPresent))
            {
                switch (user.Device)
                {
                    case OutputDevice.VR: vr++; break;
                    case OutputDevice.Screen: screen++; break;
                    case OutputDevice.Camera: camera++; break;
                    default: unknown++; break;
                }
            }

            return new DeviceMix(vr, screen, camera, unknown);
        }
    }

    /// <summary>Highest ActiveUsers with free capacity, then newest. Null when every session is full or there are none.</summary>
    public SessionSnapshot? BestSession => Sessions
        .Where(s => !SessionRules.IsFull(s))
        .OrderByDescending(s => s.ActiveUsers)
        .ThenByDescending(s => s.SessionBeginTime)
        .ThenBy(s => s.SessionId, StringComparer.Ordinal)
        .FirstOrDefault();

    public string? ThumbnailUrl => Sessions.Select(s => s.ThumbnailUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? Record?.ThumbnailUri;

    public bool IsFull => Sessions.Count > 0 && Sessions.Where(s => s.IsCompatible).All(SessionRules.IsFull) && Sessions.Any(s => s.IsCompatible);

    /// <summary>Every listed session is on another build: shown greyed out, never joinable.</summary>
    public bool IsOtherVersionOnly => Sessions.Count > 0 && Sessions.All(s => !s.IsCompatible);
    public bool IsMobileFriendly => Sessions.Any(s => s.MobileFriendly);
    public bool HasHeadlessHost => Sessions.Any(s => s.HeadlessHost);
    public bool IsFeatured => Record?.IsFeatured ?? false;
}
