using Discover.Core.Model;
using Discover.Core.Settings;

namespace Discover.Core.History;

/// <summary>One local observation of an entry (spec "Local history"): world key, count, timestamp. No user data.</summary>
public readonly record struct HistorySample(DateTime Utc, int LiveUsers);

/// <summary>The record's visit count seen at a time; the first hydration each day (spec "Local history").</summary>
public readonly record struct VisitSample(DateTime Utc, int Visits);

/// <summary>An entry with the metric that ranked it, for the "seen by this client" footer.</summary>
public sealed record ScoredEntry(DiscoveryEntry Entry, double Score);

/// <summary>
/// Trending, Rising and Hidden Gems formulas (spec "Formulas"). Pure functions of history and
/// <see cref="FormulaSettings"/>. History only holds snapshots with live users above 0, so every mean below is
/// over such snapshots. These are approximations of what one client saw, and the UI says so.
/// </summary>
public static class HistoryFormulas
{
    /// <summary>
    /// <c>mean(liveUsers over last 6h) * (1 + log2(1 + n / 10))</c> where n is the number of snapshots in the window.
    /// Null when fewer than <see cref="FormulaSettings.TrendingMinSnapshots"/> snapshots fall in the window.
    /// </summary>
    public static double? Trending(IEnumerable<HistorySample> samples, DateTime nowUtc, FormulaSettings settings)
    {
        var window = InWindow(samples, nowUtc - settings.TrendingWindow, nowUtc);
        if (window.Count < settings.TrendingMinSnapshots || window.Count == 0)
        {
            return null;
        }

        return window.Average() * (1 + Math.Log2(1 + window.Count / settings.TrendingSnapshotDivisor));
    }

    /// <summary>
    /// <c>mean(last 6h) / max(1, mean(prior 24h))</c>, where "prior" is the 24 hours directly before the recent window.
    /// Null unless the recent mean is at least <see cref="FormulaSettings.RisingMinRecentMean"/>.
    /// </summary>
    public static double? Rising(IEnumerable<HistorySample> samples, DateTime nowUtc, FormulaSettings settings)
    {
        var all = samples as IReadOnlyCollection<HistorySample> ?? samples.ToList();
        var recentStart = nowUtc - settings.RisingRecentWindow;
        var recent = InWindow(all, recentStart, nowUtc);
        if (recent.Count == 0)
        {
            return null;
        }

        var recentMean = recent.Average();
        if (recentMean < settings.RisingMinRecentMean)
        {
            return null;
        }

        var prior = InWindow(all, recentStart - settings.RisingPriorWindow, recentStart);
        var priorMean = prior.Count == 0 ? 0 : prior.Average();
        return recentMean / Math.Max(1, priorMean);
    }

    /// <summary>
    /// <c>(visitsGainedLast7d + 1) / (totalVisits + 25)</c>. Null unless the world was published in the last 180 days,
    /// has between 50 and 5,000 visits, and was seen live in the last 7 days. Visits gained is the current count minus
    /// the earliest sample inside the window (0 with no sample, never negative).
    /// </summary>
    public static double? GemScore(
        int totalVisits,
        DateTime? firstPublishUtc,
        IEnumerable<VisitSample> visitSamples,
        IEnumerable<HistorySample> liveSamples,
        DateTime nowUtc,
        FormulaSettings settings)
    {
        if (totalVisits < settings.GemMinVisits || totalVisits > settings.GemMaxVisits)
        {
            return null;
        }

        if (firstPublishUtc is null || nowUtc - firstPublishUtc.Value > settings.GemPublishedWithin)
        {
            return null;
        }

        var seenLive = liveSamples.Any(s => s.LiveUsers > 0 && s.Utc > nowUtc - settings.GemSeenLiveWithin && s.Utc <= nowUtc);
        if (!seenLive)
        {
            return null;
        }

        var earliest = visitSamples
            .Where(v => v.Utc > nowUtc - settings.GemVisitWindow && v.Utc <= nowUtc)
            .OrderBy(v => v.Utc)
            .Select(v => (int?)v.Visits)
            .FirstOrDefault();
        var gained = earliest is null ? 0 : Math.Max(0, totalVisits - earliest.Value);
        return (gained + 1) / (totalVisits + settings.GemSmoothing);
    }

    private static List<int> InWindow(IEnumerable<HistorySample> samples, DateTime afterUtc, DateTime throughUtc) => samples
        .Where(s => s.LiveUsers > 0 && s.Utc > afterUtc && s.Utc <= throughUtc)
        .Select(s => s.LiveUsers)
        .ToList();
}

/// <summary>In-memory history for the insight lenses (spec "Local history", v1.1). Persistence to disk comes with the lenses' UI.</summary>
public sealed class HistoryLog
{
    private readonly Dictionary<string, List<HistorySample>> _live = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<VisitSample>> _visits = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Keys => _live.Keys;

    /// <summary>Records a live-user count. Zero counts are not stored (spec: "live users above 0").</summary>
    public void RecordLive(string worldKey, DateTime utc, int liveUsers)
    {
        if (liveUsers <= 0 || !DiscoveryKeys.IsWorldKey(worldKey))
        {
            return;
        }

        Get(_live, worldKey).Add(new HistorySample(utc, liveUsers));
    }

    /// <summary>Records the visit count the first time it is seen each UTC day.</summary>
    public void RecordVisits(string worldKey, DateTime utc, int visits)
    {
        if (!DiscoveryKeys.IsWorldKey(worldKey))
        {
            return;
        }

        var list = Get(_visits, worldKey);
        if (list.Count == 0 || list[^1].Utc.Date != utc.Date)
        {
            list.Add(new VisitSample(utc, visits));
        }
    }

    public IReadOnlyList<HistorySample> LiveSamples(string worldKey) =>
        _live.TryGetValue(worldKey, out var list) ? list : [];

    public IReadOnlyList<VisitSample> VisitSamples(string worldKey) =>
        _visits.TryGetValue(worldKey, out var list) ? list : [];

    /// <summary>Drops samples older than the retention (default 14 days) and empties.</summary>
    public void Prune(DateTime nowUtc, TimeSpan retention)
    {
        var cutoff = nowUtc - retention;
        foreach (var key in _live.Keys.ToList())
        {
            _live[key].RemoveAll(s => s.Utc < cutoff);
            if (_live[key].Count == 0)
            {
                _live.Remove(key);
            }
        }

        foreach (var key in _visits.Keys.ToList())
        {
            _visits[key].RemoveAll(s => s.Utc < cutoff);
            if (_visits[key].Count == 0)
            {
                _visits.Remove(key);
            }
        }
    }

    private static List<T> Get<T>(Dictionary<string, List<T>> map, string key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        return list;
    }
}

/// <summary>Ranks entries for the insight lenses from <see cref="HistoryLog"/>. Entries without a score are omitted.</summary>
public static class InsightLenses
{
    public static IReadOnlyList<ScoredEntry> Trending(IEnumerable<DiscoveryEntry> entries, HistoryLog log, DateTime nowUtc, FormulaSettings settings) =>
        Rank(entries, e => HistoryFormulas.Trending(log.LiveSamples(e.Key), nowUtc, settings));

    public static IReadOnlyList<ScoredEntry> Rising(IEnumerable<DiscoveryEntry> entries, HistoryLog log, DateTime nowUtc, FormulaSettings settings) =>
        Rank(entries, e => HistoryFormulas.Rising(log.LiveSamples(e.Key), nowUtc, settings));

    public static IReadOnlyList<ScoredEntry> HiddenGems(IEnumerable<DiscoveryEntry> entries, HistoryLog log, DateTime nowUtc, FormulaSettings settings) =>
        Rank(entries, e => e.Record is null
            ? null
            : HistoryFormulas.GemScore(e.Record.Visits, e.Record.FirstPublishTime, log.VisitSamples(e.Key), log.LiveSamples(e.Key), nowUtc, settings));

    private static IReadOnlyList<ScoredEntry> Rank(IEnumerable<DiscoveryEntry> entries, Func<DiscoveryEntry, double?> score) => entries
        .Select(e => (Entry: e, Score: score(e)))
        .Where(x => x.Score is not null)
        .Select(x => new ScoredEntry(x.Entry, x.Score!.Value))
        .OrderByDescending(x => x.Score)
        .ThenBy(x => x.Entry.Key, StringComparer.Ordinal)
        .ToList();
}
