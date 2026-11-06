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

/// <summary>
/// A Browse category chip: the label shown and the tag sent. The tags are placeholders until the stock Worlds tab's
/// category buttons are read (spec V24), which is why they are settings and not literals.
/// </summary>
public sealed record BrowseCategory(string Label, string Tag);

/// <summary>Everything tunable in one place (CLAUDE.md: tunable numbers live in settings, not literals). Stored as settings.json.</summary>
public sealed record DiscoverSettings
{
    public int Version { get; init; } = 2;

    public Lenses.LensKind DefaultLens { get; init; } = Lenses.LensKind.LiveNow;

    /// <summary>Results as one row per world instead of a grid of cards.</summary>
    public bool ListView { get; init; }

    /// <summary>Show a message when a favorited world gets a public live session (spec F26).</summary>
    public bool NotifyFavoriteLive { get; init; } = true;

    /// <summary>Show a message when contacts join a live favorited world (spec F26); off by default.</summary>
    public bool NotifyContactsInFavorite { get; init; }

    /// <summary>The same world is not announced again within this time.</summary>
    public TimeSpan NotifyMinGapPerWorld { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>At most <see cref="NotifyMaxPerWindow"/> messages in any <see cref="NotifyWindow"/>.</summary>
    public int NotifyMaxPerWindow { get; init; } = 3;

    public TimeSpan NotifyWindow { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Saved views (spec F17), in the order they were saved.</summary>
    public IReadOnlyList<FilterPreset> Presets { get; init; } = [];

    /// <summary>The names shown for hidden worlds and authors (key or id to name), so Settings can list them.</summary>
    public IReadOnlyDictionary<string, string> HiddenNames { get; init; } = new Dictionary<string, string>();

    /// <summary>Worlds hidden from the live, search and insight lenses (keys). Restored from Settings.</summary>
    public IReadOnlyList<string> HiddenWorlds { get; init; } = [];

    /// <summary>Authors or hosts (user and group ids) whose worlds are hidden from the live, search and insight lenses.</summary>
    public IReadOnlyList<string> HiddenAuthors { get; init; } = [];

    /// <summary>Words that hide any world or session whose name contains them (case-insensitive).</summary>
    public IReadOnlyList<string> HiddenWords { get; init; } = [];

    /// <summary>Rail sections switched off (spec F28). Their lenses keep their own switches.</summary>
    public IReadOnlyList<Lenses.RailSectionId> HiddenSections { get; init; } = [];

    /// <summary>Lenses switched off one by one (spec F28).</summary>
    public IReadOnlyList<Lenses.LensKind> HiddenLenses { get; init; } = [];

    /// <summary>The fixed chips at the start of Browse's recommended-tags row (spec "Browse and recommended tags"). Tags are the ones the stock category buttons send (V24, WorldFilterFacetPreset).</summary>
    public IReadOnlyList<BrowseCategory> BrowseCategories { get; init; } =
    [
        new("Social", "social"),
        new("Games", "game"),
        new("Avatars", "avatar"),
        new("Art", "art"),
        new("Tutorials", "tutorial"),
        new("Educational", "educational"),
    ];

    /// <summary>How many "popular here" chips Browse adds after the categories.</summary>
    public int BrowsePopularTagCount { get; init; } = 8;

    /// <summary>Keep the stock Worlds tab visible beside ResoniteDiscovery's (spec F31). Off: the stock button is hidden at runtime.</summary>
    public bool ShowStockWorldsTab { get; init; }

    /// <summary>Use the galaxy icon for the tab instead of the stock Worlds icon (applies on the next start).</summary>
    public bool GalaxyTabIcon { get; init; }

    public bool ShowIncompatibleSessions { get; init; }
    public TimeSpan SearchDebounce { get; init; } = TimeSpan.FromMilliseconds(400);
    public TimeSpan SearchCacheFirstPageTtl { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan SearchCacheLaterPageTtl { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan AutoRefreshInterval { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ManualRefreshCooldown { get; init; } = TimeSpan.FromSeconds(15);

    public int SessionChangesPerTick { get; init; } = Model.SessionChangeQueue.DefaultMaxPerTick;
    /// <summary>Card slots the screen can hold; the page size is rows times columns, capped here.</summary>
    public const int CardSlotLimit = 100;

    /// <summary>Rows of worlds on a page (Settings > Display). A list view row counts as a quarter of a grid row.</summary>
    public int RowsPerPage { get; init; } = 10;

    public static readonly int[] RowsPerPageChoices = [3, 4, 5, 6, 8, 10, 12, 15, 20];

    public const int ListRowsPerGridRow = 4;

    /// <summary>Worlds per page: whole rows of the chosen layout, never more than <see cref="CardSlotLimit"/>.</summary>
    public int PageSizeFor()
    {
        var perRow = ListView ? ListRowsPerGridRow : Math.Max(1, CardsPerRow);
        var rows = Math.Max(1, Math.Min(RowsPerPage, CardSlotLimit / perRow));
        return rows * perRow;
    }

    /// <summary>Cards filled in per tick when a lens is shown (the rest follow on later ticks, so no one frame does all 40).</summary>
    public int CardBindsPerTick { get; init; } = 2;

    /// <summary>Card slots built per tick. Building one is the expensive part, and only happens the first time a slot is needed.</summary>
    public int CardCreatesPerTick { get; init; } = 1;

    /// <summary>The curtain stays down while cards fill in, but never longer than this.</summary>
    public TimeSpan CardFillTimeout { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>A search that has not answered after this long shows the "servers are slow" banner.</summary>
    public TimeSpan SlowSearchNotice { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan SnapshotIntervalScreenOpen { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan SnapshotIntervalScreenClosed { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan HistoryRetention { get; init; } = TimeSpan.FromDays(14);

    public TimeSpan MinWriteInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Fade through black when switching lenses. On by default: it hides the cards loading, but covers the whole results pane.</summary>
    public bool AnimateTransitions { get; init; } = true;

    /// <summary>How fast animations run: 1 is normal, above 1 faster, below 1 slower.</summary>
    public float AnimationSpeed { get; init; } = 1f;

    /// <summary>The range of Settings' animation speed slider.</summary>
    public const float AnimationSpeedMin = 0.5f, AnimationSpeedMax = 2f;

    /// <summary>How fast the 360 previews pan as the pointer moves over them, as a multiple of the engine's own pan speed.</summary>
    public float PreviewPanSpeed { get; init; } = 1.3f;

    /// <summary>The range of Settings' preview pan speed slider.</summary>
    public const float PreviewPanSpeedMin = 0.3f, PreviewPanSpeedMax = 2.5f;

    /// <summary>Cards per row in the grid layout. Fewer per row means larger cards. A value outside the choices counts as 4.</summary>
    public int CardsPerRow { get; init; } = 4;

    /// <summary>The values the settings button cycles through.</summary>
    public static readonly int[] CardsPerRowChoices = [2, 3, 4, 5, 6, 7];
    public FormulaSettings Formulas { get; init; } = new();

    /// <summary>
    /// Clamps every number and time span that guards the network budget, the update tick or disk writes to a safe range, so
    /// an edited or corrupt settings.json cannot turn them off (e.g. a zero refresh interval).
    /// </summary>
    public DiscoverSettings Sanitize() => this with
    {
        SearchDebounce = Clamp(SearchDebounce, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5)),
        SearchCacheFirstPageTtl = Clamp(SearchCacheFirstPageTtl, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1)),
        SearchCacheLaterPageTtl = Clamp(SearchCacheLaterPageTtl, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1)),
        AutoRefreshInterval = Clamp(AutoRefreshInterval, MinAutoRefresh, TimeSpan.FromHours(1)),
        ManualRefreshCooldown = Clamp(ManualRefreshCooldown, MinManualCooldown, TimeSpan.FromMinutes(10)),
        SessionChangesPerTick = Math.Clamp(SessionChangesPerTick, 1, 500),
        CardBindsPerTick = Math.Clamp(CardBindsPerTick, 1, 20),
        CardCreatesPerTick = Math.Clamp(CardCreatesPerTick, 1, 10),
        CardFillTimeout = Clamp(CardFillTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)),
        SlowSearchNotice = Clamp(SlowSearchNotice, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2)),
        SnapshotIntervalScreenOpen = Clamp(SnapshotIntervalScreenOpen, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1)),
        SnapshotIntervalScreenClosed = Clamp(SnapshotIntervalScreenClosed, TimeSpan.FromMinutes(1), TimeSpan.FromHours(6)),
        HistoryRetention = Clamp(HistoryRetention, TimeSpan.FromDays(1), TimeSpan.FromDays(60)),
        MinWriteInterval = Clamp(MinWriteInterval, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5)),
        NotifyMinGapPerWorld = Clamp(NotifyMinGapPerWorld, TimeSpan.FromMinutes(1), TimeSpan.FromDays(1)),
        NotifyWindow = Clamp(NotifyWindow, TimeSpan.FromMinutes(1), TimeSpan.FromHours(6)),
        NotifyMaxPerWindow = Math.Clamp(NotifyMaxPerWindow, 1, 20),
        RowsPerPage = Math.Clamp(RowsPerPage, 1, 30),
        CardsPerRow = Math.Clamp(CardsPerRow, 1, 8),
        BrowsePopularTagCount = Math.Clamp(BrowsePopularTagCount, 0, 20),
        PreviewPanSpeed = float.IsFinite(PreviewPanSpeed) ? Math.Clamp(PreviewPanSpeed, 0.2f, 4f) : 1.3f,
        AnimationSpeed = float.IsFinite(AnimationSpeed) ? Math.Clamp(AnimationSpeed, 0.25f, 3f) : 1f,
    };

    /// <summary>Floors that protect the networking budget (spec): one automatic refresh a minute at most, a manual one every 15 s.</summary>
    public static readonly TimeSpan MinAutoRefresh = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MinManualCooldown = TimeSpan.FromSeconds(5);

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => value < min ? min : value > max ? max : value;
}
