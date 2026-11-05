using Discover.Core.History;
using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Persistence;
using Discover.Core.Presentation;
using Discover.Core.Search;
using Discover.Core.Settings;
using Discover.Core.Sources;
using Discover.Resonite.Adapters;
using Discover.Resonite.Services;
using Discover.Resonite.Ui;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

namespace Discover.Resonite;

/// <summary>
/// The plain C# controller under a plain <see cref="RadiantDashScreen"/> (CLAUDE.md rule 6). It runs one tick per
/// userspace update, started with <c>World.RunInUpdates</c> (no component of ours is added to any world):
/// session events are drained (at most the configured number per tick), the screen is attached or re-attached, search
/// lenses are fetched through <see cref="IRecordSource"/> (one request at a time, cached, retried with backoff), and the
/// screen is re-bound only while it is shown. A tick that throws is caught at this boundary; after 5 consecutive
/// failures the controller pauses (spec "Error handling"). The stock Worlds tab is only hidden at runtime (TabTakeover).
/// </summary>
internal sealed partial class DiscoverController : IBrowseActions
{
    private const int MaxConsecutiveFailures = 5;
    private const double RefreshIntervalSeconds = 0.25;
    private const string ScreenLabel = "Worlds";
    private const string TabLabel = "Worlds"; // V31: not yet driven from the locale key
    private static readonly int[] UserSteps = [0, 1, 2, 3, 5, 10, 20];
    private static readonly int[] VisitSteps = [0, 10, 100, 1000, 10000];
    private static readonly int[] UptimeSteps = [0, 5, 15, 30, 60, 180];
    private static readonly TimeSpan IconWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(1);

    private readonly Engine _engine;
    private readonly World _world;
    private DiscoverSettings _settings;
    private readonly DiscoveryIndex _index = new();
    private readonly SessionChangeQueue _queue = new();
    private readonly EngineSessionAdapter _adapter;
    private readonly EngineRecordSource _records;
    private readonly JoinService _join;
    private readonly AuthorResolver _authors;
    private readonly EngineOpenWorldAdapter _openWorlds;
    private SearchCache _cache;
    private readonly SearchPager _pager;
    private readonly Backoff _backoff = new();
    private readonly TagChips _chips = new();
    private readonly Random _random = new();

    // View state
    private LensKind _lens;
    private SortKey _sort;
    private bool _descending;
    private BrowseFilters _filters = BrowseFilters.None;
    private SearchQuery _search = SearchQuery.Empty;
    private string _searchRaw = "";
    private bool _showOtherVersions;
    private int _livePage;
    private int _seed;
    private List<string> _pageKeys = [];
    private string? _listId;
    private string? _groupId;
    private string? _groupName;
    private string? _authorId;
    private string? _authorName;

    // The user's own data, per account (spec Integration §5). Loaded once the signed-in account is known.
    private string? _accountId;
    private UserLibrary? _library;
    private UserLibraryStore? _store;
    private LibrarySaver? _saver;
    private HistoryLog _history = new();
    private bool _historyLoaded;
    private DateTime _lastSnapshotUtc = DateTime.MinValue;
    private DateTime _lastHistorySaveUtc = DateTime.UtcNow;
    private int _accountFrame;

    // The entries currently on screen by key (cards can be saved worlds that are not in the index), and the saved
    // worlds whose records are being fetched.
    private readonly Dictionary<string, DiscoveryEntry> _shown = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hydrationRequested = new(StringComparer.OrdinalIgnoreCase);
    private int _hydrationOutstanding;
    private string? _organizeKey;

    // Current Worlds: the open worlds as of the last render, by entry key (Go there and Leave need the session id).
    private readonly Dictionary<string, OpenWorldItem> _openItems = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastOpenWorldsPollUtc = DateTime.MinValue;

    // Timing and flags
    private RadiantDashScreen? _screen;
    private DiscoverUi? _ui;
    private TabTakeover? _takeover;
    private volatile bool _dirty = true;
    private bool _loadedInitial;
    private bool _wasShown;
    private bool _paused;
    private bool _uiFailed;
    private bool _stopped;
    private bool _justShown;
    private bool _transitioning;
    private bool _searchPending;
    private int _failures;
    private int _frame;
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private DateTime _transitionStartUtc;
    private DateTime _searchChangedUtc;
    private DateTime _retryAtUtc = DateTime.MaxValue;
    private DateTime _loadingSinceUtc = DateTime.MaxValue;
    private bool _slowNoticed;
    private bool _revealPending;
    private DateTime _revealDeadlineUtc;
    private DateTime _lastManualRefreshUtc = DateTime.MinValue;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private volatile Uri? _iconUri;
    private volatile bool _iconDone;

    public DiscoverController(Engine engine, World userspace, DiscoverSettings settings)
    {
        _engine = engine;
        _world = userspace;
        _settings = settings;
        _join = new JoinService(engine);
        _authors = new AuthorResolver(engine);
        _openWorlds = new EngineOpenWorldAdapter(engine);
        _records = new EngineRecordSource(engine);
        _cache = new SearchCache(settings.SearchCacheFirstPageTtl, settings.SearchCacheLaterPageTtl);
        _pager = new SearchPager(settings.PageSizeFor());
        _showOtherVersions = settings.ShowIncompatibleSessions;
        _adapter = new EngineSessionAdapter(engine) { HideIncompatible = !_showOtherVersions };
        _adapter.Changed += _queue.Enqueue;
        engine.WorldManager.WorldsChanged += OnWorldsChanged;

        _lens = settings.DefaultLens;
        ResetSortForLens();
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        _iconUri = await IconLoader.ImportAsync(_engine);
        _iconDone = true;
    }

    public void Start()
    {
        UniLog.Log("[Discover] Controller started");
        _world.RunInUpdates(1, Tick);
    }

    public void Stop()
    {
        _stopped = true;
        RestoreStockTab();
        _engine.WorldManager.WorldsChanged -= OnWorldsChanged;
        _adapter.Dispose();
        FlushLibrary();
        UniLog.Log("[Discover] Controller stopped");
    }

    private void OnWorldsChanged() => _dirty = true; // the "You're here" state depends on which worlds are open

    // ------------------------------------------------------------------ tick

    private void Tick()
    {
        if (_stopped)
        {
            return;
        }

        try
        {
            if (!_paused)
            {
                Step();
            }

            _failures = 0;
        }
        catch (Exception ex)
        {
            _failures++;
            UniLog.Warning($"[Discover] Tick failed ({_failures}/{MaxConsecutiveFailures}): {ex.GetType().Name} at {FirstFrame(ex)}");
            if (_failures >= MaxConsecutiveFailures)
            {
                Pause();
            }
        }
        finally
        {
            if (!_stopped)
            {
                try
                {
                    _world.RunInUpdates(1, Tick);
                }
                catch (Exception)
                {
                    _stopped = true; // the world is gone
                }
            }
        }
    }

    private readonly FavoriteNotifier _notifier = new();
    private bool _notifierBaselined;

    /// <summary>Favorite notifications (F26): messages for favorited worlds among the changed keys.</summary>
    private void NotifyFavorites(IReadOnlyCollection<string> touchedKeys)
    {
        if (_library is null)
        {
            return;
        }

        if (!_notifierBaselined)
        {
            _notifierBaselined = true;
            foreach (var favorite in _library.Favorites)
            {
                _notifier.Baseline(favorite.Key, _index.TryGetEntry(favorite.Key, out var known) ? known : null);
            }
        }

        var now = DateTime.UtcNow;
        foreach (var key in touchedKeys)
        {
            if (!_library.IsStarred(key))
            {
                continue;
            }

            _index.TryGetEntry(key, out var entry);
            foreach (var message in _notifier.Observe(key, entry, now, _settings))
            {
                Notice(message);
            }
        }
    }

    private void Step()
    {
        if (!_loadedInitial)
        {
            _loadedInitial = true;
            _index.LoadSessions(_adapter.GetSessions());
            _dirty = true;
        }

        _adapter.Pump(_settings.SessionChangesPerTick);
        var touched = _queue.Drain(_index, _settings.SessionChangesPerTick);
        if (touched.Count > 0)
        {
            _dirty = true;
        }

        NotifyFavorites(touched);

        if (_accountFrame++ % 60 == 0)
        {
            CheckAccount();
        }

        if (_accountFrame % 120 == 1)
        {
            TrackVisits();
        }

        _saver?.Tick();
        TickHistory();

        // Cheap liveness check every frame; the heavier attach conditions only every 30 frames.
        if (_screen is null || _screen.IsRemoved)
        {
            _screen = null;
            _ui = null;
            RestoreStockTab();
            _takeover = null;
            if (_frame++ % 30 == 0)
            {
                TryAttach();
            }
        }

        if (_screen is null)
        {
            return;
        }

        var shown = _screen.IsShown;
        if (shown && !_wasShown)
        {
            _justShown = true; // refresh and fade in when the user opens the screen
        }

        _wasShown = shown;

        // With the takeover on, the UI is built before the screen is first opened: the stock tab is hidden only once ours
        // is ready (spec Integration §2), and it must not wait for a click on a tab the user can no longer see.
        var takeOver = !_settings.ShowStockWorldsTab;
        if (_ui is null && !_uiFailed && (shown || takeOver))
        {
            _ui = BuildUi();
        }

        _takeover?.Tick(_screen, takeOver && _ui is not null);
        if (!shown)
        {
            return; // screen closed: no UI work and no requests (spec: zero requests with the screen closed)
        }

        if (_ui is null)
        {
            return;
        }

        _ui.PumpCards();

        var now = DateTime.UtcNow;
        if (_revealPending && ((!_ui.CardsPending && !AwaitingResults) || now >= _revealDeadlineUtc))
        {
            _revealPending = false;
            _ui.Reveal(UiMotion.Seconds(0.45f), 0f);
        }

        if (!_slowNoticed && _pager.Loading && now - _loadingSinceUtc >= _settings.SlowSearchNotice)
        {
            _slowNoticed = true; // one re-render to show the banner
            _dirty = true;
        }

        ApplyPendingSearch(now);
        MaintainSearchLens(now);

        if (_transitioning)
        {
            if (now - _transitionStartUtc < TransitionTimeout)
            {
                return; // the curtain is closing; the swap happens in its callback
            }

            UniLog.Warning("[Discover] Lens transition timed out; swapping without it");
            _transitioning = false;
            _ui.Settle();
            _dirty = true;
        }

        if (_justShown)
        {
            _justShown = false;
            _dirty = false;
            _lastRefreshUtc = now;
            Render(now);
            return;
        }

        // User counts and focus change without the world list changing, so keep the open-worlds view fresh.
        if (_lens == LensKind.CurrentWorlds && (now - _lastOpenWorldsPollUtc).TotalSeconds >= 2)
        {
            _lastOpenWorldsPollUtc = now;
            _dirty = true;
        }

        if (_dirty && (now - _lastRefreshUtc).TotalSeconds >= RefreshIntervalSeconds)
        {
            _dirty = false;
            _lastRefreshUtc = now;
            Render(now);
        }
    }

    /// <summary>Applies the typed search after the debounce (spec: typing is debounced 400 ms).</summary>
    private void ApplyPendingSearch(DateTime now)
    {
        if (!_searchPending || now - _searchChangedUtc < _settings.SearchDebounce)
        {
            return;
        }

        _searchPending = false;
        _search = SearchQuery.Parse(_searchRaw);

        // author:name: look the name up once, then the server can search that owner's worlds (until then, and when
        // nobody has that name, the term filters the loaded results by name).
        foreach (var name in _search.AuthorNames)
        {
            _authors.Request(name, () => Post(() =>
            {
                if (!_stopped)
                {
                    QueryChanged();
                }
            }));
        }

        QueryChanged();
    }

    /// <summary>
    /// Keeps the visible search lens fresh within the network budget: the first fetch when it has none, a retry after a
    /// failure once its backoff has elapsed, and a refresh at most once per auto-refresh interval.
    /// </summary>
    private void MaintainSearchLens(DateTime now)
    {
        if (LensEngine.SourceOf(_lens) != LensSource.Search || _settings.LiveLensesOnly || _pager.Loading)
        {
            return;
        }

        if (_pager.BaseQuery is null)
        {
            RequestPage(0, force: false);
        }
        else if (_pager.Error is not null && now >= _retryAtUtc)
        {
            RequestPage(_pager.PageIndex, force: true);
        }
        else if (_pager.Error is null && _pager.UpdatedUtc is { } updated && now - updated >= _settings.AutoRefreshInterval)
        {
            RequestPage(_pager.PageIndex, force: true);
        }
    }

    // ------------------------------------------------------------------ attach

    /// <summary>
    /// Attaches the screen once the dash exists and its saved workspace has finished loading: loading clears the
    /// screens container, which would destroy a screen attached earlier. If that happens anyway, the liveness check in
    /// <see cref="Step()"/> notices and this runs again.
    /// </summary>
    private void TryAttach()
    {
        var dash = _world.GetGloballyRegisteredComponent<UserspaceRadiantDash>()?.Dash;
        var container = dash?.ScreensContainer;
        if (dash is null || container is null)
        {
            return;
        }

        var workspace = container.GetComponent<Workspace>();
        if (workspace is null || workspace.IsLoadingWorkspace || workspace.CurrentWorkspacePath is null || !dash.Screens.Any())
        {
            return;
        }

        // RadiantDashButton.Setup reads the icon once, so wait (briefly) for the galaxy icon before attaching.
        if (!_iconDone && DateTime.UtcNow - _startedUtc < IconWait)
        {
            return;
        }

        // Replacing the stock tab: look like it (label and icon). Beside it: say what it is, with the galaxy icon.
        var replacing = !_settings.ShowStockWorldsTab;
        var label = replacing ? TabLabel : ScreenLabel;
        var icon = !replacing || _settings.GalaxyTabIcon ? _iconUri : null;
        var screen = dash.AttachScreen(ScreenLabel, Theme.Tab, icon ?? OfficialAssets.Graphics.Icons.Dash.Worlds);

        // The tab's label is a single line of auto-sized text; without <nobr> the space wraps a two-word name onto two
        // lines and it spills out of its box (the engine uses the same tag for its own "Old Home" label).
        screen.Label.Value = "<nobr>" + label + "</nobr>";

        // The dash screens are a saved cloud workspace; non-persistent slots are skipped when it saves
        // (verification V15, V22a). Without this the screen would be saved and duplicated on every launch.
        screen.Slot.PersistentSelf = false;

        // Sit right after the stock Worlds tab. Only our own slot is moved; the stock screen is only read.
        var worlds = StockWorlds.Find(dash);
        screen.Slot.OrderOffset = worlds is null ? 5 : worlds.Slot.OrderOffset + 1;

        _screen = screen;
        _takeover = new TabTakeover(dash);
        _uiFailed = false;
        _wasShown = false;
        _dirty = true;
        UniLog.Log("[Discover] Screen attached");
    }

    private DiscoverUi? BuildUi()
    {
        var ui = new DiscoverUi(_screen!, DiscoverSettings.CardSlotLimit, this)
        {
            CardBindsPerTick = Math.Max(1, _settings.CardBindsPerTick),
            CardCreatesPerTick = Math.Max(1, _settings.CardCreatesPerTick),
            CardsPerRow = _settings.CardsPerRow,
            Animate = _settings.AnimateTransitions,
            AnimationSpeed = _settings.AnimationSpeed,
        };
        try
        {
            ui.Build();
            return ui;
        }
        catch (Exception ex)
        {
            // Build once only: remove the half-built UI and stop retrying, so a layout bug cannot pause the whole mod.
            _uiFailed = true;
            _screen!.ScreenCanvas.Slot.DestroyChildren();
            UniLog.Warning($"[Discover] UI build failed: {ex.GetType().Name} at {FirstFrame(ex)}");
            return null;
        }
    }

    /// <summary>Every world the user has open counts as visited (the Visited mark on cards).</summary>
    private void TrackVisits()
    {
        if (_library is null)
        {
            return;
        }

        try
        {
            foreach (var open in _openWorlds.GetOpenWorlds())
            {
                if (!string.IsNullOrWhiteSpace(open.RecordKey))
                {
                    _library.MarkVisited(open.RecordKey);
                }
            }
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Visit tracking failed: {ex.GetType().Name}");
        }
    }

    /// <summary>The page size changed (rows, columns or layout): the search restarts at the first page.</summary>
    private void ApplyPageSize()
    {
        var size = _settings.PageSizeFor();
        if (_pager.PageSize == size)
        {
            return;
        }

        _pager.PageSize = size;
        _pager.Reset();
        _livePage = 0;
        if (LensEngine.SourceOf(_lens) == LensSource.Search && !_settings.LiveLensesOnly)
        {
            RequestPage(0, force: false);
        }

        _dirty = true;
    }

    private static string FirstFrame(Exception ex) =>
        ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "unknown";

    // ------------------------------------------------------------------ transitions

    /// <summary>
    /// A lens switch: the sweep rises over the results (the old cards go), the new lens is rendered while they are
    /// covered, and the sweep retracts once its cards are filled in (RevealWhenFilled). Off with animations.
    /// </summary>
    private void StartTransition()
    {
        _dirty = true;
        _lastRefreshUtc = DateTime.MinValue;
        if (_ui is null || !_settings.AnimateTransitions)
        {
            return;
        }

        var ui = _ui;
        ui.CloseMenus();
        _transitioning = true;
        _transitionStartUtc = DateTime.UtcNow;

        // A second click mid-sweep restarts it; the newest lens is what the callback shows.
        ui.Cover(UiMotion.Seconds(0.30f), () =>
        {
            if (!_transitioning || ui != _ui)
            {
                return;
            }

            _transitioning = false;
            try
            {
                // This runs inside the engine's tween update, outside the tick's try/catch.
                var now = DateTime.UtcNow;
                _lastRefreshUtc = now;
                _dirty = false;
                Render(now);
                RevealWhenFilled();
            }
            catch (Exception ex)
            {
                UniLog.Warning($"[Discover] Lens switch failed: {ex.GetType().Name} at {FirstFrame(ex)}");
                ui.Settle();
                _dirty = true;
            }
        });
    }

    /// <summary>A search tab whose page is still on its way: the scan holds until it has arrived (or the timeout).</summary>
    private bool AwaitingResults => LensEngine.SourceOf(_lens) == LensSource.Search && !_settings.LiveLensesOnly && _pager.Loading;

    /// <summary>
    /// Lifts the curtain once the queued cards are filled in (or after <see cref="DiscoverSettings.CardFillTimeout"/>), so the
    /// cards appearing one by one, and the old tab's images still on them, stay out of sight.
    /// </summary>
    private void RevealWhenFilled()
    {
        _revealPending = true;
        _revealDeadlineUtc = DateTime.UtcNow + _settings.CardFillTimeout;
    }

    private void Pause()
    {
        _paused = true;
        RestoreStockTab(); // F25: never leave the user without a working Worlds tab
        _adapter.Dispose();
        UniLog.Warning("[Discover] Paused after repeated errors");
        try
        {
            _ui?.Render(new BrowseView(
                _lens, [], UiStrings.Get("screen.title"), "", "", RailBuilder.SortLabel(_sort), 0, [], [],
                UiStrings.Get("status.paused"), null, false, null, "", false, false, [], _descending, _filters, _showOtherVersions, true));
        }
        catch (Exception)
        {
            // The UI may be the thing that failed; the log line above is enough.
        }
    }
}
