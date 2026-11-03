namespace Discover.Core.Settings;

/// <summary>
/// Formula constants (spec "Formulas"). Starting values; tunable under Settings > Advanced without a rebuild.
/// Windows are measured back from "now".
/// </summary>
public sealed record FormulaSettings
{
    /// <summary>Trending: only snapshots (with users) inside this window count.</summary>
    public TimeSpan TrendingWindow { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Trending: an entry needs at least this many snapshots inside the window.</summary>
    public int TrendingMinSnapshots { get; init; } = 2;

    /// <summary>Trending: the n in <c>1 + log2(1 + n / divisor)</c>.</summary>
    public double TrendingSnapshotDivisor { get; init; } = 10;

    /// <summary>Rising: the "recent" window, which also ends the "prior" window.</summary>
    public TimeSpan RisingRecentWindow { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Rising: the prior window, directly before the recent window.</summary>
    public TimeSpan RisingPriorWindow { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Rising: shown only when the recent mean is at least this.</summary>
    public double RisingMinRecentMean { get; init; } = 2;

    public TimeSpan GemVisitWindow { get; init; } = TimeSpan.FromDays(7);
    public double GemSmoothing { get; init; } = 25;
    public int GemMinVisits { get; init; } = 50;
    public int GemMaxVisits { get; init; } = 5000;
    public TimeSpan GemPublishedWithin { get; init; } = TimeSpan.FromDays(180);
    public TimeSpan GemSeenLiveWithin { get; init; } = TimeSpan.FromDays(7);
}

/// <summary>Everything tunable in one place (CLAUDE.md: tunable numbers live in settings, not literals). Stored as settings.json.</summary>
public sealed record DiscoverSettings
{
    public int Version { get; init; } = 1;

    public Lenses.LensKind DefaultLens { get; init; } = Lenses.LensKind.LiveNow;
    public bool LiveLensesOnly { get; init; }
    public bool ShowIncompatibleSessions { get; init; }
    public bool JoinShowsSessionList { get; init; }

    public int SearchPageSize { get; init; } = 50;
    public TimeSpan SearchDebounce { get; init; } = TimeSpan.FromMilliseconds(400);
    public TimeSpan SearchCacheFirstPageTtl { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan SearchCacheLaterPageTtl { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan AutoRefreshInterval { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ManualRefreshCooldown { get; init; } = TimeSpan.FromSeconds(15);

    public int SessionChangesPerTick { get; init; } = Model.SessionChangeQueue.DefaultMaxPerTick;
    public int MaxCachedEntries { get; init; } = 500;
    public int MaxCardSlots { get; init; } = 40;
    public int MaxRecentWorlds { get; init; } = 50;

    public TimeSpan SnapshotIntervalScreenOpen { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan SnapshotIntervalScreenClosed { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan HistoryRetention { get; init; } = TimeSpan.FromDays(14);

    public TimeSpan MinWriteInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Lens switches fade the content out and back in with a short rise; off for an instant swap.</summary>
    public bool AnimateTransitions { get; init; } = true;
    public TimeSpan TransitionOut { get; init; } = TimeSpan.FromMilliseconds(120);
    public TimeSpan TransitionIn { get; init; } = TimeSpan.FromMilliseconds(280);

    /// <summary>How far, in canvas units, the results rise into place as they fade in.</summary>
    public float TransitionRise { get; init; } = 28f;

    public FormulaSettings Formulas { get; init; } = new();
}
