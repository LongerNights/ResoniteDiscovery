namespace Discover.Core.Model;

/// <summary>
/// Immutable, engine-free copy of the SessionInfo fields the mod uses (spec: "Session fields used").
/// Deliberately has no fields for BroadcastKey, HostMachineId, HostUserSessionId or session URLs
/// (spec safety rule 3): the adapter resolves URLs itself at join time.
/// </summary>
public sealed record SessionSnapshot
{
    public required string SessionId { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];

    public string HostUserId { get; init; } = "";
    /// <summary>Already sanitized (no rich-text tags).</summary>
    public string HostUsername { get; init; } = "";
    public bool HeadlessHost { get; init; }

    public string? ThumbnailUrl { get; init; }
    /// <summary>ActualCorrespondingWorldId as a string, or null for standalone sessions.</summary>
    public string? WorldKey { get; init; }

    public int JoinedUsers { get; init; }
    public int ActiveUsers { get; init; }
    public int MaximumUsers { get; init; }
    public bool MobileFriendly { get; init; }

    public DateTime SessionBeginTime { get; init; }
    public DateTime LastUpdate { get; init; }

    public AccessLevel AccessLevel { get; init; }
    public bool HideFromListing { get; init; }
    public bool HasEnded { get; init; }
    public bool IsExpired { get; init; }

    /// <summary>Result of Contacts.CountPresentContacts, computed by the adapter.</summary>
    public int PresentContacts { get; init; }

    /// <summary>
    /// False for a session on another build this client cannot join (engine <c>IsCompatible()</c>). Such sessions only
    /// reach the index when "Show other versions" is on, and are never join targets.
    /// </summary>
    public bool IsCompatible { get; init; } = true;

    public IReadOnlyList<SessionUserSnapshot> Users { get; init; } = [];
}

/// <summary>Present-user info used for the device mix and the detail panel's user list (as on the stock tab); never persisted (spec safety rule 4).</summary>
public sealed record SessionUserSnapshot(string UserId, string Username, bool IsPresent, OutputDevice Device, bool IsContact = false);
