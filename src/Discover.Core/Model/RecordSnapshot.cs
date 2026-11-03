namespace Discover.Core.Model;

/// <summary>Engine-free copy of the SkyFrost.Base.Record fields the mod uses for published worlds.</summary>
public sealed record RecordSnapshot
{
    /// <summary>Combined record id (RecordId.ToString()); matches SessionSnapshot.WorldKey.</summary>
    public required string Key { get; init; }
    public required string OwnerId { get; init; }
    public required string RecordId { get; init; }

    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string OwnerName { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? ThumbnailUri { get; init; }

    public int Visits { get; init; }
    public DateTime? FirstPublishTime { get; init; }
    public DateTime? LastUpdate { get; init; }
    public bool IsFeatured { get; init; }
}
