using Discover.Core.Model;

namespace Discover.Core.Sources;

/// <summary>
/// Search results keyed by <see cref="RecordQuery"/> (spec "Search cache"): page 1 lives 5 minutes, later pages 15,
/// both from settings. Only successful pages are kept. At most <paramref name="capacity"/> entries; the oldest goes first.
/// </summary>
public sealed class SearchCache(TimeSpan firstPageTtl, TimeSpan laterPageTtl, int capacity = 64)
{
    private readonly Dictionary<RecordQuery, (RecordPage Page, DateTime StoredUtc)> _entries = [];

    public int Count => _entries.Count;

    public bool TryGet(RecordQuery query, DateTime nowUtc, out RecordPage page)
    {
        if (_entries.TryGetValue(query, out var hit) && nowUtc - hit.StoredUtc <= TtlFor(query))
        {
            page = hit.Page;
            return true;
        }

        _entries.Remove(query);
        page = null!;
        return false;
    }

    public void Put(RecordQuery query, RecordPage page, DateTime nowUtc)
    {
        if (!page.Ok)
        {
            return;
        }

        if (!_entries.ContainsKey(query) && _entries.Count >= capacity)
        {
            var oldest = _entries.MinBy(e => e.Value.StoredUtc).Key;
            _entries.Remove(oldest);
        }

        _entries[query] = (page, nowUtc);
    }

    public bool Remove(RecordQuery query) => _entries.Remove(query);

    public void Clear() => _entries.Clear();

    private TimeSpan TtlFor(RecordQuery query) => query.Offset == 0 ? firstPageTtl : laterPageTtl;
}

/// <summary>
/// One search lens's paging state. Each request gets a generation number; a response that arrives after a newer
/// request started is dropped, which is how "starting a new search cancels the previous" (spec networking budget)
/// works without cancelling the engine's request. On failure the previous page stays on screen.
/// </summary>
public sealed class SearchPager(int pageSize)
{
    private int _generation;

    public int PageSize { get; } = pageSize;
    public RecordQuery? BaseQuery { get; private set; }
    public int PageIndex { get; private set; }
    public IReadOnlyList<RecordSnapshot> Records { get; private set; } = [];
    public bool HasMore { get; private set; }
    public bool Loading { get; private set; }
    public string? Error { get; private set; }
    public DateTime? UpdatedUtc { get; private set; }
    public bool HasPrevious => PageIndex > 0;

    /// <summary>Starts a request for <paramref name="page"/> of <paramref name="baseQuery"/>; returns its generation and the paged query.</summary>
    public (int Generation, RecordQuery Query) Begin(RecordQuery baseQuery, int page)
    {
        if (BaseQuery is null || !BaseQuery.Equals(baseQuery))
        {
            Records = [];
            HasMore = false;
            UpdatedUtc = null;
        }

        BaseQuery = baseQuery;
        PageIndex = Math.Max(0, page);
        Loading = true;
        Error = null;
        return (++_generation, baseQuery with { Offset = PageIndex * PageSize, Count = PageSize });
    }

    /// <summary>Applies a response. False when it is stale (a newer request started) and was ignored.</summary>
    public bool Complete(int generation, RecordPage page, DateTime nowUtc)
    {
        if (generation != _generation)
        {
            return false;
        }

        Loading = false;
        if (!page.Ok)
        {
            Error = string.IsNullOrWhiteSpace(page.Error) ? "Search failed" : page.Error;
            return true;
        }

        Records = page.Records;
        HasMore = page.HasMoreResults;
        Error = null;
        UpdatedUtc = nowUtc;
        return true;
    }

    /// <summary>Forgets everything, e.g. when the lens's query changes shape.</summary>
    public void Reset()
    {
        _generation++;
        BaseQuery = null;
        PageIndex = 0;
        Records = [];
        HasMore = false;
        Loading = false;
        Error = null;
        UpdatedUtc = null;
    }
}

/// <summary>Retry delays after a failed search (spec "Error handling"): 5 s, then 15 s, then 60 s from then on.</summary>
public sealed class Backoff
{
    private static readonly TimeSpan[] Steps = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60)];
    private int _failures;

    public int Failures => _failures;

    /// <summary>Records a failure and returns how long to wait before the next try.</summary>
    public TimeSpan Next() => Steps[Math.Min(_failures++, Steps.Length - 1)];

    public void Reset() => _failures = 0;
}
