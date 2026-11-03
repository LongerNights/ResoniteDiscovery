using Discover.Core.Model;
using Discover.Core.Sources;

namespace Discover.Tests;

public class SearchStateTests
{
    private static readonly DateTime Now = TestData.Now;
    private static RecordPage Page(int n, bool more = false) =>
        new(true, Enumerable.Range(0, n).Select(i => TestData.Record("R" + i)).ToList(), more);

    // ---- Cache

    [Fact]
    public void CacheHonoursFirstAndLaterPageTtls()
    {
        var cache = new SearchCache(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));
        var first = new RecordQuery { Offset = 0 };
        var later = new RecordQuery { Offset = 40 };
        cache.Put(first, Page(3), Now);
        cache.Put(later, Page(3), Now);

        Assert.True(cache.TryGet(first, Now.AddMinutes(4), out _));
        Assert.False(cache.TryGet(first, Now.AddMinutes(6), out _));
        Assert.True(cache.TryGet(later, Now.AddMinutes(14), out _));
        Assert.False(cache.TryGet(later, Now.AddMinutes(16), out _));
    }

    [Fact]
    public void CacheKeyIgnoresTagOrder()
    {
        var cache = new SearchCache(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));
        cache.Put(new RecordQuery { RequiredTags = ["a", "b"] }, Page(2), Now);

        Assert.True(cache.TryGet(new RecordQuery { RequiredTags = ["B", "a"] }, Now, out var hit));
        Assert.Equal(2, hit.Records.Count);
    }

    [Fact]
    public void CacheNeverStoresFailuresAndEvictsTheOldest()
    {
        var cache = new SearchCache(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), capacity: 2);
        cache.Put(new RecordQuery { Offset = 1 }, RecordPage.Failed("down"), Now);
        Assert.Equal(0, cache.Count);

        cache.Put(new RecordQuery { Offset = 1 }, Page(1), Now);
        cache.Put(new RecordQuery { Offset = 2 }, Page(1), Now.AddSeconds(1));
        cache.Put(new RecordQuery { Offset = 3 }, Page(1), Now.AddSeconds(2));

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet(new RecordQuery { Offset = 1 }, Now.AddSeconds(3), out _));
    }

    // ---- Pager

    [Fact]
    public void PagerPagesWithOffsetsAndDropsStaleResponses()
    {
        var pager = new SearchPager(40);
        var (g1, q1) = pager.Begin(new RecordQuery(), 0);
        var (g2, q2) = pager.Begin(new RecordQuery(), 1);

        Assert.Equal(0, q1.Offset);
        Assert.Equal(40, q2.Offset);
        Assert.Equal(40, q2.Count);
        Assert.False(pager.Complete(g1, Page(40, more: true), Now)); // overtaken by g2
        Assert.True(pager.Loading);

        Assert.True(pager.Complete(g2, Page(12), Now));
        Assert.False(pager.Loading);
        Assert.Equal(12, pager.Records.Count);
        Assert.False(pager.HasMore);
        Assert.True(pager.HasPrevious);
        Assert.Equal(Now, pager.UpdatedUtc);
    }

    [Fact]
    public void PagerKeepsTheLastResultsOnFailure()
    {
        var pager = new SearchPager(40);
        var (g1, _) = pager.Begin(new RecordQuery(), 0);
        pager.Complete(g1, Page(5, more: true), Now);

        var (g2, _) = pager.Begin(new RecordQuery(), 0);
        pager.Complete(g2, RecordPage.Failed("Service unavailable"), Now.AddMinutes(1));

        Assert.Equal(5, pager.Records.Count);
        Assert.Equal("Service unavailable", pager.Error);
        Assert.Equal(Now, pager.UpdatedUtc);
    }

    [Fact]
    public void ANewQueryClearsTheOldResults()
    {
        var pager = new SearchPager(40);
        var (g1, _) = pager.Begin(new RecordQuery { OnlyFeatured = true }, 0);
        pager.Complete(g1, Page(5), Now);

        pager.Begin(new RecordQuery { OnlyFeatured = false }, 0);

        Assert.Empty(pager.Records);
        Assert.Null(pager.UpdatedUtc);
    }

    // ---- Backoff

    [Fact]
    public void BackoffStepsFiveFifteenSixtyThenStays()
    {
        var b = new Backoff();

        Assert.Equal(TimeSpan.FromSeconds(5), b.Next());
        Assert.Equal(TimeSpan.FromSeconds(15), b.Next());
        Assert.Equal(TimeSpan.FromSeconds(60), b.Next());
        Assert.Equal(TimeSpan.FromSeconds(60), b.Next());
        b.Reset();
        Assert.Equal(TimeSpan.FromSeconds(5), b.Next());
    }
}
