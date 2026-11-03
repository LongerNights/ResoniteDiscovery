using Discover.Core.History;
using Discover.Core.Settings;

namespace Discover.Tests;

public class FormulaTests
{
    private static readonly DateTime Now = TestData.Now;
    private static readonly FormulaSettings S = new();

    private static HistorySample At(double hoursAgo, int users) => new(Now.AddHours(-hoursAgo), users);

    [Fact]
    public void TrendingGoldenValue()
    {
        // mean(4, 6, 8) = 6 ; 1 + log2(1 + 3/10) = 1.378512 ; 6 * 1.378512 = 8.27107
        var score = HistoryFormulas.Trending([At(1, 4), At(2, 6), At(3, 8)], Now, S);

        Assert.Equal(8.27107, score!.Value, precision: 4);
    }

    [Fact]
    public void TrendingNeedsTwoSnapshotsInTheWindow()
    {
        Assert.Null(HistoryFormulas.Trending([], Now, S));
        Assert.Null(HistoryFormulas.Trending([At(1, 9)], Now, S));
        Assert.Null(HistoryFormulas.Trending([At(1, 9), At(7, 9)], Now, S)); // second one is outside 6 h
        Assert.NotNull(HistoryFormulas.Trending([At(1, 9), At(5.9, 9)], Now, S));
    }

    [Fact]
    public void TrendingIgnoresZeroUserSnapshotsAndFutureOnes()
    {
        Assert.Null(HistoryFormulas.Trending([At(1, 0), At(2, 0), At(-1, 9)], Now, S));
    }

    [Fact]
    public void TrendingRewardsMoreSnapshotsAtTheSameMean()
    {
        var few = HistoryFormulas.Trending([At(1, 5), At(2, 5)], Now, S)!.Value;
        var many = HistoryFormulas.Trending(Enumerable.Range(1, 20).Select(i => At(i / 4.0, 5)), Now, S)!.Value;

        Assert.True(many > few);
    }

    [Fact]
    public void RisingGoldenValue()
    {
        // recent mean (4, 6) = 5 ; prior mean (2, 2, 2, 4) = 2.5 ; 5 / 2.5 = 2
        var samples = new[] { At(1, 4), At(2, 6), At(8, 2), At(12, 2), At(20, 2), At(29, 4) };

        Assert.Equal(2.0, HistoryFormulas.Rising(samples, Now, S)!.Value, precision: 6);
    }

    [Fact]
    public void RisingWithNoPriorHistoryDividesByOne()
    {
        Assert.Equal(5.0, HistoryFormulas.Rising([At(1, 4), At(2, 6)], Now, S)!.Value, precision: 6);
    }

    [Fact]
    public void RisingDenominatorNeverDropsBelowOne()
    {
        // prior mean 0.5 cannot occur from whole users, so use a tiny setting to prove the clamp
        var loose = S with { RisingMinRecentMean = 0 };
        Assert.Equal(1.0, HistoryFormulas.Rising([At(1, 1)], Now, loose)!.Value, precision: 6);
    }

    [Fact]
    public void RisingIsHiddenBelowTheRecentMeanThreshold()
    {
        Assert.Null(HistoryFormulas.Rising([At(1, 1), At(2, 2)], Now, S)); // mean 1.5 < 2
        Assert.Null(HistoryFormulas.Rising([], Now, S));
        Assert.Null(HistoryFormulas.Rising([At(8, 50)], Now, S)); // nothing recent
    }

    [Fact]
    public void RisingPriorWindowEndsWhereRecentBegins()
    {
        // 31 h ago is outside the 6 h + 24 h span, so the prior mean is empty and the score is the recent mean.
        Assert.Equal(4.0, HistoryFormulas.Rising([At(1, 4), At(31, 100)], Now, S)!.Value, precision: 6);
    }

    [Fact]
    public void GemScoreGoldenValue()
    {
        // (25 + 1) / (100 + 25) = 0.208
        var score = HistoryFormulas.GemScore(
            totalVisits: 100,
            firstPublishUtc: Now.AddDays(-30),
            visitSamples: [new VisitSample(Now.AddDays(-6), 75), new VisitSample(Now.AddDays(-1), 90)],
            liveSamples: [At(10, 3)],
            Now,
            S);

        Assert.Equal(0.208, score!.Value, precision: 6);
    }

    [Fact]
    public void GemScoreWithNoVisitSamplesGainsNothing()
    {
        var score = HistoryFormulas.GemScore(100, Now.AddDays(-30), [], [At(10, 3)], Now, S);

        Assert.Equal(1.0 / 125, score!.Value, precision: 9);
    }

    [Fact]
    public void GemScoreNeverGoesNegativeWhenVisitsDrop()
    {
        var score = HistoryFormulas.GemScore(100, Now.AddDays(-30), [new VisitSample(Now.AddDays(-2), 400)], [At(10, 3)], Now, S);

        Assert.Equal(1.0 / 125, score!.Value, precision: 9);
    }

    [Theory]
    [InlineData(49, 30, 10, false)]   // too few visits
    [InlineData(50, 30, 10, true)]
    [InlineData(5000, 30, 10, true)]
    [InlineData(5001, 30, 10, false)] // too popular
    [InlineData(100, 181, 10, false)] // too old
    [InlineData(100, 180, 10, true)]
    [InlineData(100, 30, 200, false)] // not seen live in the last 7 days (200 h = 8.3 d)
    public void GemFiltersFollowTheSpec(int visits, int publishedDaysAgo, int liveHoursAgo, bool included)
    {
        var score = HistoryFormulas.GemScore(visits, Now.AddDays(-publishedDaysAgo), [], [At(liveHoursAgo, 2)], Now, S);

        Assert.Equal(included, score is not null);
    }

    [Fact]
    public void GemNeedsAPublishDate()
    {
        Assert.Null(HistoryFormulas.GemScore(100, null, [], [At(1, 2)], Now, S));
    }

    [Fact]
    public void ConstantsComeFromSettings()
    {
        var tighter = S with { TrendingWindow = TimeSpan.FromHours(1) };

        Assert.Null(HistoryFormulas.Trending([At(0.5, 5), At(2, 5)], Now, tighter));
        Assert.NotNull(HistoryFormulas.Trending([At(0.5, 5), At(2, 5)], Now, S));
    }

    [Fact]
    public void HistoryLogStoresOnlyLiveWorldSamplesAndPrunes()
    {
        var log = new HistoryLog();
        log.RecordLive("W", Now.AddDays(-20), 3);
        log.RecordLive("W", Now.AddHours(-1), 4);
        log.RecordLive("W", Now, 0);          // zero users: not stored
        log.RecordLive("S:abc", Now, 5);      // standalone: not stored
        log.RecordVisits("W", Now.AddHours(-3), 100);
        log.RecordVisits("W", Now.AddHours(-2), 101); // same day: not stored again

        Assert.Equal(2, log.LiveSamples("W").Count);
        Assert.Single(log.VisitSamples("W"));
        Assert.Empty(log.LiveSamples("S:abc"));

        log.Prune(Now, TimeSpan.FromDays(14));

        Assert.Single(log.LiveSamples("W"));
    }

    [Fact]
    public void InsightLensesRankByScoreAndOmitUnscored()
    {
        var log = new HistoryLog();
        foreach (var h in new[] { 1, 2, 3 })
        {
            log.RecordLive("W-hot", Now.AddHours(-h), 10);
            log.RecordLive("W-warm", Now.AddHours(-h), 3);
        }

        log.RecordLive("W-once", Now.AddHours(-1), 50);

        var index = new Discover.Core.Model.DiscoveryIndex();
        foreach (var key in new[] { "W-hot", "W-warm", "W-once", "W-never" })
        {
            index.SetRecord(TestData.Record(key));
        }

        var trending = InsightLenses.Trending(index.Entries, log, Now, S);

        Assert.Equal(["W-hot", "W-warm"], trending.Select(t => t.Entry.Key));
        Assert.True(trending[0].Score > trending[1].Score);
    }
}
