using Discover.Core.Model;

namespace Discover.Core.Sources;

public enum OwnerKind
{
    User,
    Group,
}

/// <summary>
/// Engine-free subset of SkyFrost SearchParameters. RecordType is always "world" and is set by the adapter.
/// Value equality ignores tag order and case, so it can key the search cache (spec "Search cache").
/// The adapter, not the lens, supplies <see cref="SubmittedTo"/> = <c>Cloud.Platform.GroupId</c> for public queries
/// (queries with <see cref="Private"/> leave it unset), matching the stock browser (verification V8).
/// </summary>
public sealed record RecordQuery : IEquatable<RecordQuery>
{
    public RecordSort SortBy { get; init; } = RecordSort.TotalVisits;
    public SortDirection Direction { get; init; } = SortDirection.Descending;
    public int Count { get; init; } = 50;
    public int Offset { get; init; }
    public IReadOnlyList<string> RequiredTags { get; init; } = [];
    public IReadOnlyList<string> OptionalTags { get; init; } = [];
    public IReadOnlyList<string> ExcludedTags { get; init; } = [];
    public bool OnlyFeatured { get; init; }
    public string? ByOwner { get; init; }
    public OwnerKind OwnerType { get; init; } = OwnerKind.User;

    /// <summary>True only when searching the signed-in user's own worlds.</summary>
    public bool Private { get; init; }
    public string? SubmittedTo { get; init; }

    public bool Equals(RecordQuery? other) =>
        other is not null
        && SortBy == other.SortBy
        && Direction == other.Direction
        && Count == other.Count
        && Offset == other.Offset
        && OnlyFeatured == other.OnlyFeatured
        && string.Equals(ByOwner, other.ByOwner, StringComparison.Ordinal)
        && OwnerType == other.OwnerType
        && Private == other.Private
        && string.Equals(SubmittedTo, other.SubmittedTo, StringComparison.Ordinal)
        && Normalize(RequiredTags).SequenceEqual(Normalize(other.RequiredTags))
        && Normalize(OptionalTags).SequenceEqual(Normalize(other.OptionalTags))
        && Normalize(ExcludedTags).SequenceEqual(Normalize(other.ExcludedTags));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SortBy);
        hash.Add(Direction);
        hash.Add(Count);
        hash.Add(Offset);
        hash.Add(OnlyFeatured);
        hash.Add(ByOwner);
        hash.Add(OwnerType);
        hash.Add(Private);
        hash.Add(SubmittedTo);
        foreach (var tags in new[] { RequiredTags, OptionalTags, ExcludedTags })
        {
            var normalized = Normalize(tags);
            hash.Add(normalized.Length);
            foreach (var tag in normalized)
            {
                hash.Add(tag);
            }
        }

        return hash.ToHashCode();
    }

    private static string[] Normalize(IReadOnlyList<string> tags) => tags
        .Select(t => t.Trim().ToLowerInvariant())
        .Where(t => t.Length > 0)
        .Distinct()
        .Order(StringComparer.Ordinal)
        .ToArray();
}

/// <summary>One page of results. On failure Ok is false and Error holds the cloud's message.</summary>
public sealed record RecordPage(bool Ok, IReadOnlyList<RecordSnapshot> Records, bool HasMoreResults, string? Error = null)
{
    public static RecordPage Failed(string error) => new(false, [], false, error);
}

/// <summary>
/// Published-world search and lookup, backed in game by Engine.Cloud.Records
/// (FindRecords for search, RecordCache for batched lookups).
/// Implementations must never throw for cloud errors; return RecordPage.Failed instead.
/// </summary>
public interface IRecordSource
{
    Task<RecordPage> SearchAsync(RecordQuery query);

    /// <summary>Hydrates one record by owner and record id; null when missing or on error.</summary>
    Task<RecordSnapshot?> GetAsync(string ownerId, string recordId);
}
