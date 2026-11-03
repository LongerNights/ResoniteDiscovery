using Discover.Core.Model;
using Discover.Core.Sources;
using FrooxEngine;
using SkyFrost.Base;

namespace Discover.Resonite.Adapters;

/// <summary>
/// <see cref="IRecordSource"/> over <c>Engine.Cloud.Records.FindRecords</c> (<c>records/pagedSearch</c>), shaped like
/// the stock browser's search (verification V8): RecordType "world", <c>SubmittedTo</c> = the platform group for public
/// searches, <c>ExtraSignatures</c> never set. Never throws: every failure becomes <see cref="RecordPage.Failed"/>.
/// The engine completes the request on a thread-pool thread; the caller marshals the result back to the update thread.
/// </summary>
internal sealed class EngineRecordSource(Engine engine) : IRecordSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task<RecordPage> SearchAsync(RecordQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = new SearchParameters
            {
                RecordType = "world",
                Count = query.Count,
                Offset = query.Offset,
                SortBy = Enum.Parse<SearchSortParameter>(query.SortBy.ToString()),
                SortDirection = Enum.Parse<SearchSortDirection>(query.Direction.ToString()),
                RequiredTags = query.RequiredTags.ToList(),
                OptionalTags = query.OptionalTags.ToList(),
                ExcludedTags = query.ExcludedTags.ToList(),
                OnlyFeatured = query.OnlyFeatured,
                Private = query.Private,
                ByOwner = query.ByOwner,
                OwnerType = query.OwnerType == OwnerKind.Group ? OwnerType.Group : OwnerType.User,
                SubmittedTo = query.Private ? null! : query.SubmittedTo ?? engine.Cloud.Platform.GroupId,
            };

            var result = await engine.Cloud.Records
                .FindRecords<SkyFrost.Base.Record>(parameters, Timeout, throwOnError: false)
                .ConfigureAwait(false);

            if (!result.IsOK || result.Entity is null)
            {
                // The status only: the response body is server text of unknown length.
                return RecordPage.Failed($"{(int)result.State} {result.State}");
            }

            var records = (result.Entity.Records ?? [])
                .Where(r => r is not null && !r.IsDeleted)
                .Select(Map)
                .ToList();
            return new RecordPage(true, records, result.Entity.HasMoreResults);
        }
        catch (Exception ex)
        {
            return RecordPage.Failed(ex.GetType().Name);
        }
    }

    /// <summary>
    /// One record by owner and record id through the engine's <c>RecordCache</c>, which memoizes and batches misses
    /// (32 ids per request, 0.25 s window; spec "Networking budget"). Null when missing, deleted or on any error.
    /// </summary>
    public async Task<RecordSnapshot?> GetAsync(string ownerId, string recordId, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await engine.Cloud.Records.RecordCache<SkyFrost.Base.Record>().Get(ownerId, recordId).ConfigureAwait(false);
            return record is { IsDeleted: false } ? Map(record) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static RecordSnapshot Map(SkyFrost.Base.Record record) => new()
    {
        // The same RecordId.ToString() a session's ActualCorrespondingWorldId gives, so live sessions merge onto it.
        Key = record.CombinedRecordId?.ToString() ?? record.OwnerId + ":" + record.RecordId,
        OwnerId = record.OwnerId ?? "",
        RecordId = record.RecordId ?? "",
        Name = SessionMapper.Plain(record.Name),
        Description = SessionMapper.Plain(record.Description),
        OwnerName = SessionMapper.Plain(record.OwnerName),
        Tags = record.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? [],
        ThumbnailUri = record.ThumbnailURI,
        Visits = record.Visits,
        FirstPublishTime = record.FirstPublishTime,
        LastUpdate = record.LastModificationTime,
        IsFeatured = record.Submissions?.Any(s => s is not null && s.Featured) ?? false,
    };
}
