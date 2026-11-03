using Discover.Core.Model;
using Discover.Core.Sources;

namespace Discover.Tests.Fakes;

/// <summary>In-memory record source. Supports only the query features a test sets up; records every query.</summary>
public sealed class FakeRecordSource : IRecordSource
{
    public List<RecordSnapshot> Records { get; } = [];
    public List<RecordQuery> Queries { get; } = [];
    public string? FailWith { get; set; }

    public Task<RecordPage> SearchAsync(RecordQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        if (FailWith is not null)
        {
            return Task.FromResult(RecordPage.Failed(FailWith));
        }

        var page = Records.Skip(query.Offset).Take(query.Count).ToList();
        var hasMore = query.Offset + page.Count < Records.Count;
        return Task.FromResult(new RecordPage(true, page, hasMore));
    }

    public Task<RecordSnapshot?> GetAsync(string ownerId, string recordId, CancellationToken cancellationToken = default)
        => Task.FromResult(Records.FirstOrDefault(r => r.OwnerId == ownerId && r.RecordId == recordId));
}
