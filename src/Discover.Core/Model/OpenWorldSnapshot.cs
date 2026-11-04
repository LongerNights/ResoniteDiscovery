namespace Discover.Core.Model;

/// <summary>
/// One world the user has open (spec "OpenWorldEntry"). Plain data copied by the adapter on the update thread; never
/// persisted (the session id is kept only in memory).
/// </summary>
public sealed record OpenWorldSnapshot
{
    /// <summary>The engine's session id for the world. Identifies it to focus or leave; not stored.</summary>
    public required string SessionId { get; init; }

    public string Name { get; init; } = "";

    /// <summary>Combined record id when the world was loaded from a published record, else null.</summary>
    public string? RecordKey { get; init; }

    public string HostUsername { get; init; } = "";
    public bool IsHost { get; init; }
    public AccessLevel AccessLevel { get; init; }
    public int Users { get; init; }
    public int MaxUsers { get; init; }

    /// <summary>Who is in the world (names and contact marks), for the detail panel's user list. In memory only.</summary>
    public IReadOnlyList<SessionUserSnapshot> UserList { get; init; } = [];
    public bool IsFocused { get; init; }
    public string? ThumbnailUrl { get; init; }

    /// <summary>When the adapter first saw the world. In memory only.</summary>
    public DateTime OpenedAt { get; init; }

    public DateTime LastFocusedAt { get; init; }

    /// <summary>False for the local home, which the engine refuses to exit.</summary>
    public bool CanLeave { get; init; } = true;
}
