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
/// failures the controller pauses (spec "Error handling"). The stock Worlds tab is never touched.
/// </summary>
internal sealed class DiscoverController : IBrowseActions
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
    private readonly SearchCache _cache;
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
        _pager = new SearchPager(Math.Min(settings.SearchPageSize, settings.MaxCardSlots));
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
            UniLog.Warning($"[Discover] Tick failed ({_failures}/{MaxConsecutiveFailures}): {ex.GetType().Name}: {ex.Message} at {FirstFrame(ex)}");
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
        if (_revealPending && (!_ui.CardsPending || now >= _revealDeadlineUtc))
        {
            _revealPending = false;
            _ui.Reveal((float)_settings.TransitionIn.TotalSeconds, _settings.TransitionRise);
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
            _authors.Request(name, () => _world.RunSynchronously(() =>
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

        // The tab's label is a single line of auto-sized text; without <nobr> the space wraps "Galaxy Sieve" onto two
        // lines and it spills out of its box (the engine uses the same tag for its own "Old Home" label).
        screen.Label.Value = "<nobr>" + label + "</nobr>";

        // The dash screens are a saved cloud workspace; non-persistent slots are skipped when it saves
        // (verification V15, V22a). Without this the screen would be saved and duplicated on every launch.
        screen.Slot.PersistentSelf = false;

        // Sit right after the stock Worlds tab. Only our own slot is moved; the stock screen is only read.
        var worlds = dash.GetScreen<GridContainerScreen>(g => g.HasPreset(typeof(WorldsScreenInitializer)));
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
        var ui = new DiscoverUi(_screen!, _settings.MaxCardSlots, this)
        {
            CardBindsPerTick = Math.Max(1, _settings.CardBindsPerTick),
            CardCreatesPerTick = Math.Max(1, _settings.CardCreatesPerTick),
            CardsPerRow = _settings.CardsPerRow,
            Animate = _settings.AnimateTransitions,
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
            UniLog.Warning($"[Discover] UI build failed: {ex.GetType().Name}: {ex.Message} at {FirstFrame(ex)}");
            return null;
        }
    }

    private static string FirstFrame(Exception ex) =>
        ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "unknown";

    // ------------------------------------------------------------------ data

    private void ResetSortForLens()
    {
        _sort = Sorting.Default(_lens);
        _descending = Sorting.DefaultDescending(_sort);
        _seed = _random.Next();
    }

    private RecordQuery BuildQuery()
    {
        var query = _lens switch
        {
            LensKind.New => RecordLenses.New(),
            LensKind.Featured => RecordLenses.Featured(),
            LensKind.Random => RecordLenses.Random(),
            LensKind.MyWorlds when _accountId is not null => RecordLenses.MyWorlds(_accountId),
            LensKind.GroupWorlds when _groupId is not null => RecordLenses.GroupWorlds(_groupId),
            _ => RecordLenses.Browse(),
        };

        if (_sort != Sorting.Default(_lens) || _descending != Sorting.DefaultDescending(_sort))
        {
            if (Sorting.ToRecordSort(_sort) is { } sort)
            {
                query = query with { SortBy = sort, Direction = _descending ? SortDirection.Descending : SortDirection.Ascending };
            }
        }

        if (_authorId is not null && _lens is LensKind.Browse or LensKind.New or LensKind.Featured or LensKind.Random)
        {
            query = RecordLenses.ByAuthor(query, _authorId);
        }

        if (_authorId is null && _lens is LensKind.Browse or LensKind.New or LensKind.Featured or LensKind.Random
            && _search.AuthorNames.Select(_authors.IdFor).FirstOrDefault(id => id is not null) is { } searched)
        {
            query = RecordLenses.ByAuthor(query, searched);
        }

        if (_filters.FeaturedOnly)
        {
            query = query with { OnlyFeatured = true };
        }

        query = _chips.ApplyTo(query);
        return _search.ApplyTo(query, tag => _library?.HasTagNamed(tag) ?? false);
    }

    /// <summary>A change to the search text, chips, sort or lens: restart paging, and fetch when it is a search lens.</summary>
    private void QueryChanged()
    {
        _livePage = 0;
        if (LensEngine.SourceOf(_lens) == LensSource.Search && !_settings.LiveLensesOnly)
        {
            RequestPage(0, force: false);
        }

        _dirty = true;
    }

    /// <summary>
    /// Fetches a page of the current search lens: from the cache when fresh, else from the record source (one request at
    /// a time; a newer request makes the older response stale). A manual or automatic refresh bypasses the cache.
    /// </summary>
    private void RequestPage(int page, bool force)
    {
        var now = DateTime.UtcNow;
        var (generation, query) = _pager.Begin(BuildQuery(), page);
        _retryAtUtc = DateTime.MaxValue;
        _loadingSinceUtc = now;
        _slowNoticed = false;

        if (!force && _cache.TryGet(query, now, out var hit))
        {
            ApplyPage(generation, query, hit, now, cached: true);
            return;
        }

        _dirty = true;
        _ = FetchAsync(generation, query);
    }

    private async Task FetchAsync(int generation, RecordQuery query)
    {
        var page = await _records.SearchAsync(query);
        try
        {
            // The engine completes the request on a pool thread; every state change happens on the update thread.
            _world.RunSynchronously(() => ApplyPage(generation, query, page, DateTime.UtcNow, cached: false));
        }
        catch (Exception)
        {
            // The world is gone; nothing to update.
        }
    }

    private void ApplyPage(int generation, RecordQuery query, RecordPage page, DateTime now, bool cached)
    {
        if (!_pager.Complete(generation, page, now))
        {
            return; // a newer request started; this response is stale
        }

        if (page.Ok)
        {
            if (!cached)
            {
                _cache.Put(query, page, now);
            }

            _backoff.Reset();
            _retryAtUtc = DateTime.MaxValue;

            // Hand the page's records to the index (merging onto live sessions); the previous page's go away.
            foreach (var key in _pageKeys)
            {
                _index.RemoveRecord(key);
            }

            _pageKeys = page.Records.Select(r => r.Key).ToList();
            foreach (var record in page.Records)
            {
                _index.SetRecord(record);
            }
        }
        else
        {
            _retryAtUtc = now + _backoff.Next();
            UniLog.Warning($"[Discover] Search failed: {page.Error}");
        }

        _dirty = true;
    }

    // ------------------------------------------------------------------ view

    private DiscoveryEntry Dec(DiscoveryEntry entry) => LibraryViews.Decorate(entry, _library);

    private IReadOnlyList<DiscoveryEntry> LiveEntries()
    {
        var source = _lens == LensKind.Friends ? LensEngine.Friends(_index.Entries) : LensEngine.LiveNow(_index.Entries, _filters.IncludeEmpty);
        return Sorting.SortLive(
            source.Select(Dec).Where(e => !HideRules.IsHidden(e, _settings) && _search.Matches(e) && _chips.Matches(e) && _filters.MatchesLive(e)),
            _sort, _descending, _seed);
    }

    /// <summary>Trending, Rising or Hidden Gems: the index ranked from the local history, then searched and chip-filtered.</summary>
    private IReadOnlyList<DiscoveryEntry> HistoryEntries(DateTime now)
    {
        var formulas = _settings.Formulas;
        var scored = _lens switch
        {
            LensKind.Trending => InsightLenses.Trending(_index.Entries, _history, now, formulas),
            LensKind.Rising => InsightLenses.Rising(_index.Entries, _history, now, formulas),
            _ => InsightLenses.HiddenGems(_index.Entries, _history, now, formulas),
        };

        return scored.Select(s => Dec(s.Entry)).Where(e => !HideRules.IsHidden(e, _settings) && _search.Matches(e) && _chips.Matches(e)).ToList();
    }

    private IReadOnlyList<DiscoveryEntry> SearchEntries()
    {
        var entries = new List<DiscoveryEntry>(_pager.Records.Count);
        foreach (var record in _pager.Records)
        {
            if (_filters.MatchesRecord(record) && _index.TryGetEntry(record.Key, out var found))
            {
                var entry = Dec(found);
                if (!HideRules.IsHidden(entry, _settings) && _search.Matches(entry) && _chips.Matches(entry))
                {
                    entries.Add(entry);
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Current Worlds: every open world (userspace excluded), focused first, built directly so private and LAN worlds the
    /// user is already in are not hidden by the session visibility rule. Searched and chip-filtered like any lens.
    /// </summary>
    private IReadOnlyList<DiscoveryEntry> OpenWorldEntries()
    {
        RecordSnapshot? Lookup(string key) => _index.TryGetEntry(key, out var e) ? e.Record : null;
        var items = CurrentWorlds.Build(_openWorlds.GetOpenWorlds(), Lookup);
        _openItems.Clear();
        var entries = new List<DiscoveryEntry>(items.Count);
        foreach (var item in items)
        {
            var entry = Dec(item.Entry);
            if (_search.Matches(entry) && _chips.Matches(entry))
            {
                _openItems[entry.Key] = item with { Entry = entry };
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Favorites, a named list or Recent: the saved items, ordered, then searched and chip-filtered like any lens.</summary>
    private IReadOnlyList<DiscoveryEntry> LocalEntries()
    {
        if (_library is null)
        {
            return [];
        }

        DiscoveryEntry? Lookup(string key) => _index.TryGetEntry(key, out var e) ? e : null;
        var items = _lens switch
        {
            LensKind.Favorites => LensEngine.Saved(LibraryViews.FavoriteMembers(_library), Lookup),
            LensKind.NamedList when _listId is not null => LensEngine.Saved(LibraryViews.ListMembers(_library, _listId), Lookup),
            LensKind.Recent => LensEngine.Recent(LibraryViews.RecentMembers(_library), Lookup),
            _ => [],
        };

        return LibraryViews.Order(items, _sort, _descending)
            .Select(LibraryViews.EntryFor)
            .Select(Dec)
            .Where(e => _search.Matches(e) && _chips.Matches(e))
            .ToList();
    }

    private bool AnyNarrowing =>
        !_search.IsEmpty || !_chips.IsEmpty || _filters.ActiveCount(LensEngine.SourceOf(_lens)) > 0;

    private void Render(DateTime now)
    {
        // A list that no longer exists (deleted, or another account) falls back to Favorites.
        if (_lens == LensKind.NamedList && (_library is null || _listId is null || _library.GetList(_listId) is null))
        {
            _lens = LensKind.Favorites;
            _listId = null;
            ResetSortForLens();
        }

        var source = LensEngine.SourceOf(_lens);
        var searchLens = source == LensSource.Search;
        IReadOnlyList<DiscoveryEntry> all;
        IReadOnlyList<DiscoveryEntry> shown;
        var maxCards = _ui!.MaxCards;
        bool hasPrevious, hasNext;

        if (searchLens)
        {
            all = _settings.LiveLensesOnly ? [] : SearchEntries();
            shown = all;
            hasPrevious = _pager.HasPrevious;
            hasNext = _pager.HasMore;
        }
        else
        {
            all = source switch
            {
                LensSource.Local => LocalEntries(),
                LensSource.OpenWorlds => OpenWorldEntries(),
                LensSource.History => HistoryEntries(now),
                _ => LiveEntries(),
            };
            var pages = Math.Max(1, (all.Count + maxCards - 1) / maxCards);
            _livePage = Math.Clamp(_livePage, 0, pages - 1);
            shown = all.Skip(_livePage * maxCards).Take(maxCards).ToList();
            hasPrevious = _livePage > 0;
            hasNext = _livePage < pages - 1;
        }

        _shown.Clear();
        foreach (var entry in shown)
        {
            _shown[entry.Key] = entry;
        }

        if (source == LensSource.Local)
        {
            HydrateMissing(shown);
        }

        var cards = shown
            .Select(e => source == LensSource.OpenWorlds && _openItems.TryGetValue(e.Key, out var open)
                ? CardBuilder.BuildOpenWorld(open with { Entry = e }, now, _library?.IsStarred(e.Key) ?? false)
                : CardBuilder.Build(e, _join.OpenSession(e), now, _library?.IsStarred(e.Key) ?? false))
            .ToList();
        var people = source == LensSource.Local ? 0 : all.Sum(e => _lens == LensKind.Friends ? e.Sessions.Sum(s => s.ActiveUsers) : e.LiveUsers);

        string? emptyMessage = null;
        string? emptyHint = null;
        if (cards.Count == 0)
        {
            if (_settings.LiveLensesOnly && searchLens)
            {
                emptyMessage = UiStrings.Get("empty.searchDisabled");
            }
            else if (searchLens && _pager.Loading)
            {
                emptyMessage = UiStrings.Get("empty.loading");
            }
            else if (source == LensSource.Local && _library is null)
            {
                emptyMessage = UiStrings.Get("empty.noAccount");
            }
            else if (AnyNarrowing)
            {
                emptyMessage = UiStrings.Get("empty.noMatch");
                emptyHint = UiStrings.Get("empty.noMatchHint");
            }
            else if (source == LensSource.Local)
            {
                emptyMessage = UiStrings.Get(_lens switch { LensKind.Favorites => "empty.favorites", LensKind.Recent => "empty.recent", _ => "empty.list" });
            }
            else if (source == LensSource.OpenWorlds)
            {
                emptyMessage = UiStrings.Get("empty.currentWorlds");
            }
            else if (source == LensSource.History)
            {
                emptyMessage = UiStrings.Get("empty.history");
            }
            else if (!searchLens)
            {
                emptyMessage = UiStrings.Get(_lens == LensKind.Friends ? "empty.friends" : "empty.liveNow");
            }
            else if (_pager.Error is null)
            {
                emptyMessage = UiStrings.Get(_pager.UpdatedUtc is not null
                    ? _lens switch { LensKind.MyWorlds => "empty.myWorlds", LensKind.GroupWorlds => "empty.groupWorlds", _ => "empty.noMatch" }
                    : "empty.loading");
            }
        }

        string? banner = null;
        if (searchLens && _pager.Error is null && _pager.Loading && now - _loadingSinceUtc >= _settings.SlowSearchNotice)
        {
            banner = UiStrings.Get("banner.searchSlow");
        }

        if (searchLens && _pager.Error is not null)
        {
            var wait = Math.Max(1, (int)Math.Ceiling((_retryAtUtc - now).TotalSeconds));
            banner = UiStrings.Format("banner.searchFailed", _pager.Error, wait);
        }

        var listInfos = _library is null ? [] : LibraryViews.ListInfos(_library);
        var title = _lens == LensKind.NamedList && _library?.GetList(_listId ?? "") is { } list ? list.Name
            : _lens == LensKind.GroupWorlds && _groupName is not null ? RailBuilder.Title(_lens) + " · " + CardText.Ellipsize(_groupName, 24)
            : _lens == LensKind.Browse && _authorName is not null ? RailBuilder.Title(_lens) + " · " + UiStrings.Format("title.byAuthor", CardText.Ellipsize(_authorName, 24))
            : RailBuilder.Title(_lens);

        var view = new BrowseView(
            _lens,
            RailBuilder.Build(
                _lens,
                LensEngine.LiveNow(_index.Entries).Count,
                LensEngine.Friends(_index.Entries).Count,
                _library?.Favorites.Count ?? 0,
                listInfos,
                _library?.Recent.Count ?? 0,
                _listId,
                _settings,
                _openWorlds.GetOpenWorlds().Count),
            title,
            RailBuilder.Subtitle(_lens),
            RailBuilder.Count(_lens, all.Count, people),
            RailBuilder.SortLabel(_sort),
            _filters.ActiveCount(source),
            _lens == LensKind.Browse
                ? BrowseTags.Row(BrowseTags.Build(_settings, all, _chips.Items, _library?.AllTags.ToList() ?? []), _chips.Items, DiscoverUi.MaxChips)
                : BrowseTags.ActiveOnly(_chips.Items),
            cards,
            emptyMessage,
            emptyHint,
            emptyMessage is not null && AnyNarrowing,
            banner,
            StatusText(searchLens, all.Count, maxCards, now),
            hasPrevious,
            hasNext,
            RailBuilder.SortMenu(_lens, _sort),
            _descending,
            _filters,
            _showOtherVersions,
            source == LensSource.Sessions,
            ListTools: _lens == LensKind.NamedList && _library is not null,
            ShowNewList: _settings.IsVisible(LensKind.NamedList),
            ListView: _settings.ListView);

        _ui.SetPresets(_settings.Presets.Select(p => p.Name).ToList());
        _ui.Render(view);
    }

    private string StatusText(bool searchLens, int total, int maxCards, DateTime now)
    {
        if (searchLens)
        {
            var page = _pager.PageIndex + 1;
            if (_pager.Loading)
            {
                return UiStrings.Format("status.pageUpdating", page);
            }

            var updated = _pager.UpdatedUtc is { } u
                ? now - u < TimeSpan.FromMinutes(1) ? UiStrings.Get("status.updatedNow") : UiStrings.Format("status.updated", CardText.Age(u, now))
                : "";
            return string.Join(" · ", new[] { UiStrings.Format("status.page", page), updated }.Where(s => s.Length > 0));
        }

        var pages = Math.Max(1, (total + maxCards - 1) / maxCards);
        return pages > 1 ? UiStrings.Format("status.pageOf", _livePage + 1, pages) : "";
    }

    // ------------------------------------------------------------------ saved worlds: records and the account's data

    /// <summary>
    /// Saved worlds that are neither live nor in the index get their record fetched (thumbnail, visits, tags) through
    /// the engine's batching record cache: at most 20 outstanding, each key once per session.
    /// </summary>
    private void HydrateMissing(IEnumerable<DiscoveryEntry> shown)
    {
        foreach (var entry in shown)
        {
            if (_index.TryGetEntry(entry.Key, out _)
                || !DiscoveryKeys.IsWorldKey(entry.Key)
                || !RecordKeys.TryParse(entry.Key, out var owner, out var record)
                || _hydrationOutstanding >= 20
                || !_hydrationRequested.Add(entry.Key))
            {
                continue;
            }

            _hydrationOutstanding++;
            _ = HydrateAsync(owner, record);
        }
    }

    private async Task HydrateAsync(string ownerId, string recordId)
    {
        var snapshot = await _records.GetAsync(ownerId, recordId);
        try
        {
            _world.RunSynchronously(() =>
            {
                _hydrationOutstanding--;
                if (snapshot is not null)
                {
                    _index.SetRecord(snapshot);
                    _dirty = true;
                }
            });
        }
        catch (Exception)
        {
            // The world is gone.
        }
    }

    /// <summary>Loads the signed-in account's files when the account becomes known or changes (never before login).</summary>
    private void CheckAccount()
    {
        string? id;
        try
        {
            id = _engine.Cloud.Session.CurrentUserID;
        }
        catch (Exception)
        {
            return;
        }

        if (string.IsNullOrEmpty(id) || id == _accountId)
        {
            return;
        }

        FlushLibrary();
        var folder = AccountPaths.AccountDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), id);
        _store = new UserLibraryStore(folder, message => UniLog.Warning(message));
        _library = _store.LoadLibrary();
        _history = _store.LoadHistory();
        _historyLoaded = true;
        _saver = new LibrarySaver(_store, _library, _settings.MinWriteInterval);
        _library.Changed += _ => _dirty = true;
        var firstAccount = _accountId is null;
        _accountId = id;
        _organizeKey = null;
        _settings = LensCatalog.Normalize(_store.LoadSettings());
        if (firstAccount && _settings.DefaultLens != _lens && _settings.DefaultLens != LensKind.NamedList)
        {
            _lens = _settings.DefaultLens;
            ResetSortForLens();
            QueryChanged();
        }

        _hydrationRequested.Clear();
        _dirty = true;
        UniLog.Log("[Discover] Account data loaded");

        if (_store.RecoveredFiles.Count > 0)
        {
            Notice(UiStrings.Get("notice.fileReset"));
        }
    }

    /// <summary>Snapshots the index into the history on the settings' intervals and saves it every few minutes.</summary>
    private void TickHistory()
    {
        if (!_historyLoaded)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var interval = _wasShown ? _settings.SnapshotIntervalScreenOpen : _settings.SnapshotIntervalScreenClosed;
        if (now - _lastSnapshotUtc >= interval)
        {
            _lastSnapshotUtc = now;
            _history.Snapshot(_index.Entries, now);
            _dirty |= LensEngine.SourceOf(_lens) == LensSource.History;
        }

        if (now - _lastHistorySaveUtc >= TimeSpan.FromMinutes(5))
        {
            SaveHistory(now);
        }
    }

    private void SaveHistory(DateTime now)
    {
        _lastHistorySaveUtc = now;
        try
        {
            _history.Prune(now, _settings.HistoryRetention);
            _store?.SaveHistory(_history);
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Could not save history: {ex.GetType().Name}");
        }
    }

    private void FlushLibrary()
    {
        if (_historyLoaded)
        {
            SaveHistory(DateTime.UtcNow);
        }

        try
        {
            _saver?.Flush();
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Could not save: {ex.GetType().Name}");
        }
    }

    private static void Notice(string message) =>
        NotificationPanel.ShowNotification(null!, message, null!, RadiantUI_Constants.Neutrals.DARK, NotificationType.ToastOnly);

    // ------------------------------------------------------------------ actions (IBrowseActions)

    public void SelectLens(LensKind lens)
    {
        if (lens == LensKind.GroupWorlds)
        {
            PickGroup();
            return;
        }

        var hadAuthor = _authorId is not null;
        _authorId = null;
        _authorName = null;
        GoToLens(lens, null, force: hadAuthor);
    }

    // ---- saved views (F17)

    public void SavePresetPrompt() => _ui?.ShowPrompt(UiStrings.Get("prompt.presetTitle"), UiStrings.Get("prompt.presetPlaceholder"), "", name =>
    {
        var preset = new FilterPreset
        {
            Name = name,
            Lens = _lens,
            ListId = _lens == LensKind.NamedList ? _listId : null,
            Search = _searchRaw,
            Chips = _chips.Items.ToList(),
            Sort = _sort,
            Descending = _descending,
            Filters = _filters,
        };

        var next = FilterPresets.Save(_settings, preset);
        if (next is null)
        {
            Notice(UiStrings.Format("presets.full", FilterPresets.MaxPresets));
            return;
        }

        _settings = next;
        _store?.SaveSettings(_settings);
        _dirty = true;
        Notice(UiStrings.Format("presets.saved", Safe(preset.Name)));
    });

    public void DeletePreset(int index)
    {
        if (index < 0 || index >= _settings.Presets.Count)
        {
            return;
        }

        _settings = FilterPresets.Delete(_settings, _settings.Presets[index].Name);
        _store?.SaveSettings(_settings);
        _dirty = true;
    }

    public void ApplyPreset(int index)
    {
        if (index < 0 || index >= _settings.Presets.Count)
        {
            return;
        }

        var preset = _settings.Presets[index];
        if (!_settings.IsVisible(preset.Lens) || (preset.Lens == LensKind.NamedList && (preset.ListId is null || _library?.GetList(preset.ListId) is null)))
        {
            Notice(UiStrings.Get("presets.gone"));
            return;
        }

        _chips.Clear();
        foreach (var chip in preset.Chips)
        {
            _chips.Cycle(chip.Tag, chip.Personal);
            if (chip.State == ChipState.Excluded)
            {
                _chips.Cycle(chip.Tag, chip.Personal);
            }
        }

        _filters = preset.Filters;
        _searchRaw = preset.Search;
        _search = SearchQuery.Parse(preset.Search);
        _searchPending = false;
        _ui?.SetSearchText(preset.Search);
        _authorId = null;
        _authorName = null;
        GoToLens(preset.Lens, preset.ListId, force: true);
        if (Sorting.IsSupported(preset.Lens, preset.Sort))
        {
            _sort = preset.Sort;
            _descending = preset.Descending;
        }

        QueryChanged();
    }

    /// <summary>Detail panel, Hide...: hide the world or everything by its author from the live, search and insight lenses (F19).</summary>
    /// <summary>Detail panel: start a new session of the world (asks for its settings first).</summary>
    public void StartNewSession(string key)
    {
        if ((_shown.TryGetValue(key, out var entry) || _index.TryGetEntry(key, out entry)) && entry.Record is not null)
        {
            ConfirmNewSession(entry);
        }
    }

    /// <summary>Detail panel: join one specific live session of a world.</summary>
    public void JoinSession(string key, string sessionId)
    {
        if (!_shown.TryGetValue(key, out var entry) && !_index.TryGetEntry(key, out entry))
        {
            return;
        }

        _ = RunAsync(() => _join.JoinAsync(entry, sessionId), entry);
    }

    public void HideOptions(string key)
    {
        if (!_shown.TryGetValue(key, out var entry) && !_index.TryGetEntry(key, out entry))
        {
            return;
        }

        var worldKey = entry.Key;
        var authorId = HideRules.AuthorOf(entry);
        var authorName = entry.Record is { OwnerName.Length: > 0 } record ? record.OwnerName : authorId;
        var choices = new List<(string, Action)> { (UiStrings.Get("hide.world"), () => ApplyHide(HideRules.HideWorld(_settings, worldKey, entry.DisplayName))) };
        if (authorId.Length > 0)
        {
            choices.Add((UiStrings.Format("hide.author", CardText.Ellipsize(authorName, 22)), () => ApplyHide(HideRules.HideAuthor(_settings, authorId, authorName))));
        }

        _ui?.ShowConfirm(UiStrings.Get("hide.title"), Safe(entry.DisplayName), choices);
    }

    private void ApplyHide(DiscoverSettings next)
    {
        _settings = next;
        _store?.SaveSettings(_settings);
        _dirty = true;
        Notice(UiStrings.Get("hide.done"));
    }

    /// <summary>Detail panel, More by author: Browse narrowed to that owner (stock "By owner"), cleared by picking any lens.</summary>
    public void MoreByAuthor(string key)
    {
        if (!_shown.TryGetValue(key, out var entry) && !_index.TryGetEntry(key, out entry))
        {
            return;
        }

        if (entry.Record is not { OwnerId.Length: > 0 } record)
        {
            return;
        }

        _authorId = record.OwnerId;
        _authorName = string.IsNullOrWhiteSpace(record.OwnerName) ? record.OwnerId : record.OwnerName;
        _chips.Clear();
        GoToLens(LensKind.Browse, null, force: true);
    }

    /// <summary>
    /// Group Worlds: the engine's cached memberships (no request). One group opens directly; several ask through the
    /// confirmation panel, which holds four choices.
    /// </summary>
    private void PickGroup()
    {
        List<(string Id, string Name)> groups;
        try
        {
            groups = _engine.Cloud.Groups.CurrentUserMemberships
                .Where(m => !string.IsNullOrEmpty(m.GroupId))
                .Select(m => (m.GroupId, m.GroupName ?? m.GroupId))
                .OrderBy(g => g.Item2, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            groups = [];
        }

        if (groups.Count == 0)
        {
            Notice(UiStrings.Get("notice.noGroups"));
            return;
        }

        void Choose((string Id, string Name) group)
        {
            _groupId = group.Id;
            _groupName = group.Name;
            GoToLens(LensKind.GroupWorlds, null, force: true);
        }

        if (groups.Count == 1)
        {
            Choose(groups[0]);
            return;
        }

        const int Max = 4;
        if (groups.Count > Max)
        {
            Notice(UiStrings.Format("notice.moreGroups", Max));
        }

        var choices = groups.Take(Max).Select(g => (CardText.Ellipsize(_ui!.PlainText(g.Name), 28), (Action)(() => Choose(g)))).ToList();
        _ui?.ShowConfirm(UiStrings.Get("confirm.groupTitle"), UiStrings.Get("confirm.groupBody"), choices);
    }

    public void SelectList(string listId) => GoToLens(LensKind.NamedList, listId);

    private void GoToLens(LensKind lens, string? listId, bool force = false)
    {
        if (!force && lens == _lens && listId == _listId && !_transitioning)
        {
            return;
        }

        _lens = lens;
        _listId = listId;
        ResetSortForLens();
        _livePage = 0;
        _pager.Reset();
        if (LensEngine.SourceOf(lens) == LensSource.Search && !_settings.LiveLensesOnly)
        {
            RequestPage(0, force: false);
        }

        StartTransition();
    }

    public void SearchTextChanged(string text)
    {
        _searchRaw = text;
        _searchChangedUtc = DateTime.UtcNow;
        _searchPending = true;
    }

    public void ChooseSort(SortKey key)
    {
        if (!Sorting.IsSupported(_lens, key))
        {
            return;
        }

        if (key == SortKey.Random)
        {
            _seed = _random.Next();
        }

        _sort = key;
        _descending = Sorting.DefaultDescending(key);
        QueryChanged();
    }

    public void SetDescending(bool descending)
    {
        _descending = descending;
        QueryChanged();
    }

    public void Filter(FilterOp op)
    {
        switch (op)
        {
            case FilterOp.MinUsersUp: _filters = _filters with { MinUsers = Step(UserSteps, _filters.MinUsers, +1) }; break;
            case FilterOp.MinUsersDown: _filters = _filters with { MinUsers = Step(UserSteps, _filters.MinUsers, -1) }; break;
            case FilterOp.MinVisitsUp: _filters = _filters with { MinVisits = Step(VisitSteps, _filters.MinVisits, +1) }; break;
            case FilterOp.MinVisitsDown: _filters = _filters with { MinVisits = Step(VisitSteps, _filters.MinVisits, -1) }; break;
            case FilterOp.MinUptimeUp: _filters = _filters with { MinUptimeMinutes = Step(UptimeSteps, _filters.MinUptimeMinutes, +1) }; break;
            case FilterOp.MinUptimeDown: _filters = _filters with { MinUptimeMinutes = Step(UptimeSteps, _filters.MinUptimeMinutes, -1) }; break;
            case FilterOp.ToggleFeatured: _filters = _filters with { FeaturedOnly = !_filters.FeaturedOnly }; break;
            case FilterOp.ToggleIncludeEmpty: _filters = _filters with { IncludeEmpty = !_filters.IncludeEmpty }; break;
            case FilterOp.ToggleHasRoom: _filters = _filters with { HasRoom = !_filters.HasRoom }; break;
            case FilterOp.ToggleMobile: _filters = _filters with { MobileFriendly = !_filters.MobileFriendly }; break;
            case FilterOp.ToggleVr: _filters = _filters with { HasVr = !_filters.HasVr }; break;
            case FilterOp.HostAny: _filters = _filters with { Host = HostFilter.Any }; break;
            case FilterOp.HostPerson: _filters = _filters with { Host = HostFilter.Person }; break;
            case FilterOp.HostHeadless: _filters = _filters with { Host = HostFilter.Headless }; break;
            case FilterOp.Reset: _filters = BrowseFilters.None; break;
            case FilterOp.ToggleOtherVersions:
                _showOtherVersions = !_showOtherVersions;
                _adapter.HideIncompatible = !_showOtherVersions;
                _index.LoadSessions(_adapter.GetSessions());
                break;
        }

        QueryChanged();
    }

    private static int Step(int[] steps, int current, int direction)
    {
        var index = Array.FindLastIndex(steps, s => s <= current);
        return steps[Math.Clamp(Math.Max(index, 0) + direction, 0, steps.Length - 1)];
    }

    public void CycleChip(string tag)
    {
        _chips.Cycle(tag);
        QueryChanged();
    }

    public void ClearChips()
    {
        _chips.Clear();
        _filters = BrowseFilters.None;
        _searchRaw = "";
        _search = SearchQuery.Empty;
        _searchPending = false;
        if (_ui is not null)
        {
            _ui.SearchText = "";
        }

        QueryChanged();
    }

    public void Refresh()
    {
        var now = DateTime.UtcNow;
        if (now - _lastManualRefreshUtc < _settings.ManualRefreshCooldown)
        {
            return; // spec: 15 s cooldown
        }

        _lastManualRefreshUtc = now;
        _engine.Cloud.Sessions.ForceFetch();
        if (LensEngine.SourceOf(_lens) == LensSource.Search && !_settings.LiveLensesOnly)
        {
            _seed = _random.Next();
            RequestPage(_pager.PageIndex, force: true);
        }

        _dirty = true;
    }

    public void Retry()
    {
        _backoff.Reset();
        RequestPage(_pager.PageIndex, force: true);
    }

    public void PreviousPage()
    {
        if (LensEngine.SourceOf(_lens) == LensSource.Search)
        {
            RequestPage(Math.Max(0, _pager.PageIndex - 1), force: false);
        }
        else
        {
            _livePage = Math.Max(0, _livePage - 1);
        }

        _dirty = true;
    }

    public void NextPage()
    {
        if (LensEngine.SourceOf(_lens) == LensSource.Search)
        {
            if (_pager.HasMore)
            {
                RequestPage(_pager.PageIndex + 1, force: false);
            }
        }
        else
        {
            _livePage++;
        }

        _dirty = true;
    }

    public void CardTagPressed(string tag, bool personal)
    {
        _chips.Cycle(tag.Trim(), personal);
        QueryChanged();
    }

    // ------------------------------------------------------------------ favorites, lists and tags (M4a)

    public void ToggleFavorite(string key)
    {
        if (_library is null || !_shown.TryGetValue(key, out var entry))
        {
            return;
        }

        if (_library.IsStarred(key))
        {
            _library.Unstar(key);
        }
        else
        {
            _library.Star(key, entry.DisplayName);
        }
    }

    public void OpenOrganize(string key)
    {
        if (_library is null || !_shown.ContainsKey(key))
        {
            return;
        }

        _organizeKey = key;
        ShowOrganize();
    }

    private void ShowOrganize()
    {
        if (_library is null || _organizeKey is null || !_shown.TryGetValue(_organizeKey, out var entry))
        {
            return;
        }

        var memberOf = _library.ListIdsContaining(_organizeKey).ToHashSet();
        var lists = _library.Lists.Select(l => new OrganizeList(l.Id, l.Name, memberOf.Contains(l.Id))).ToList();
        _ui?.ShowOrganize(new OrganizeModel(_organizeKey, entry.DisplayName, lists, _library.TagsFor(_organizeKey)));
    }

    public void OrganizeToggleList(string listId)
    {
        if (_library is null || _organizeKey is null || !_shown.TryGetValue(_organizeKey, out var entry))
        {
            return;
        }

        if (_library.ListIdsContaining(_organizeKey).Contains(listId))
        {
            _library.RemoveFromList(listId, _organizeKey);
        }
        else
        {
            _library.AddToList(listId, _organizeKey, entry.DisplayName);
        }

        ShowOrganize();
    }

    public IReadOnlyList<string> SuggestTags(string typed, bool forSearch)
    {
        IEnumerable<string> Known()
        {
            var entries = _index.Entries.Concat(_shown.Values.Where(e => !_index.TryGetEntry(e.Key, out _)));
            foreach (var entry in entries)
            {
                foreach (var tag in (entry.Record?.Tags ?? []).Concat(entry.Sessions.SelectMany(s => s.Tags)).Concat(entry.PersonalTags))
                {
                    yield return tag;
                }
            }

            // The user's own tags weigh double: they have already chosen to use them.
            foreach (var tag in _library?.AllTags ?? [])
            {
                yield return tag;
                yield return tag;
            }
        }

        IReadOnlyList<string> mine = forSearch || _library is null || _organizeKey is null ? [] : _library.TagsFor(_organizeKey);
        var known = forSearch ? Known().Where(t => !t.Any(char.IsWhiteSpace)) : Known();
        return TagSuggester.Suggest(typed, known, mine, forSearch ? 4 : 5);
    }

    /// <summary>Copies the published world's record link. The clipboard call is the engine's own (InventoryBrowser.CopyURL).</summary>
    public void CopyLink(string key)
    {
        if (!_shown.TryGetValue(key, out var entry) && !_index.TryGetEntry(key, out entry))
        {
            return;
        }

        var url = RecordUrls.For(entry);
        if (url is null)
        {
            return;
        }

        try
        {
            _engine.InputInterface.Clipboard.SetText(url);
            Notice(UiStrings.Get("notice.linkCopied"));
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Copy link failed: {ex.GetType().Name}");
            Notice(UiStrings.Get("notice.linkFailed"));
        }
    }

    public void OpenDetails(string key)
    {
        if (_ui is null || (!_shown.TryGetValue(key, out var entry) && !_index.TryGetEntry(key, out entry)))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_lens == LensKind.CurrentWorlds && _openItems.TryGetValue(key, out var open))
        {
            var openCard = CardBuilder.BuildOpenWorld(open with { Entry = entry }, now, _library?.IsStarred(key) ?? false);
            _ui.ShowDetails(DetailBuilder.Build(entry, openCard, now, open.World));
            return;
        }

        var card = CardBuilder.Build(entry, _join.OpenSession(entry), now, _library?.IsStarred(key) ?? false);
        _ui.ShowDetails(DetailBuilder.Build(entry, card, now));
    }

    /// <summary>Leave (or, for a world you host, end) an open world, after asking. Spec F30.</summary>
    public void LeaveWorld(string key)
    {
        if (_ui is null || _lens != LensKind.CurrentWorlds || !_openItems.TryGetValue(key, out var open) || !open.World.CanLeave)
        {
            return;
        }

        var sessionId = open.World.SessionId;
        var name = Safe(open.Entry.DisplayName);
        var host = open.World.IsHost;
        _ui.ShowConfirm(
            UiStrings.Get("confirm.leaveTitle"),
            UiStrings.Format(host ? "confirm.endBody" : "confirm.leaveBody", name),
            [(UiStrings.Get(host ? "confirm.end" : "confirm.leave"), () => _ = LeaveAsync(sessionId))]);
    }

    private async Task LeaveAsync(string sessionId)
    {
        var ok = await _join.LeaveAsync(sessionId);
        try
        {
            _world.RunSynchronously(() =>
            {
                if (!ok)
                {
                    Notice(UiStrings.Get("notice.leaveFailed"));
                }

                _dirty = true;
            });
        }
        catch (Exception)
        {
            // The userspace world is gone.
        }
    }

    // ---- settings, export and import

    private SettingsModel SettingsView() =>
        new(_settings.AnimateTransitions, _settings.LiveLensesOnly, RailBuilder.Title(_settings.DefaultLens), _settings,
            LensCatalog.Openable(_settings).Select(l => (l, RailBuilder.Title(l), l == _settings.DefaultLens)).ToList(),
            HideRules.Items(_settings));

    /// <summary>Restores the stock Worlds button; never throws (it runs on the failure paths).</summary>
    private void RestoreStockTab()
    {
        try
        {
            _takeover?.Restore();
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Restoring the stock Worlds tab failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void OpenClassicBrowser()
    {
        if (_takeover?.OpenClassic() != true)
        {
            Notice(UiStrings.Get("notice.classicMissing"));
        }
    }

    public void OpenSettings() => _ui?.ShowSettings(SettingsView());

    public void ToggleSetting(SettingKey key)
    {
        switch (key)
        {
            case SettingKey.Animations:
                _settings = _settings with { AnimateTransitions = !_settings.AnimateTransitions };
                if (_ui is not null)
                {
                    _ui.Animate = _settings.AnimateTransitions;
                }

                break;
            case SettingKey.LiveOnly:
                _settings = LensCatalog.Normalize(_settings with { LiveLensesOnly = !_settings.LiveLensesOnly });
                if (!_settings.LiveLensesOnly)
                {
                    QueryChanged();
                }

                EnsureLensVisible();
                break;
            case SettingKey.ListView:
                _settings = _settings with { ListView = !_settings.ListView };
                _dirty = true;
                break;
            case SettingKey.NotifyLive:
                _settings = _settings with { NotifyFavoriteLive = !_settings.NotifyFavoriteLive };
                break;
            case SettingKey.NotifyContacts:
                _settings = _settings with { NotifyContactsInFavorite = !_settings.NotifyContactsInFavorite };
                break;
            case SettingKey.HiddenRestore:
                _settings = HideRules.RestoreAll(_settings);
                _dirty = true;
                break;
            case SettingKey.HiddenWord:
                _ui?.ShowPrompt(UiStrings.Get("prompt.hiddenWordTitle"), UiStrings.Get("prompt.hiddenWordPlaceholder"), "", word =>
                {
                    _settings = HideRules.HideWord(_settings, word);
                    _store?.SaveSettings(_settings);
                    _dirty = true;
                });
                return; // the prompt replaces the settings panel
            case SettingKey.CardsPerRow:
                var choices = DiscoverSettings.CardsPerRowChoices;
                var index = Array.IndexOf(choices, _settings.CardsPerRow);
                _settings = _settings with { CardsPerRow = index < 0 ? choices[0] : choices[(index + 1) % choices.Length] };
                if (_ui is not null)
                {
                    _ui.CardsPerRow = _settings.CardsPerRow;
                }

                break;
            case SettingKey.StartLens:
                var openable = LensCatalog.Openable(_settings);
                var at = openable.ToList().IndexOf(_settings.DefaultLens);
                _settings = _settings with { DefaultLens = openable[(at + 1) % openable.Count] };
                break;
        }

        _store?.SaveSettings(_settings);
        _ui?.ShowSettings(SettingsView());
    }

    public void RestoreHidden(HiddenKind kind, string id)
    {
        _settings = HideRules.Restore(_settings, kind, id);
        _store?.SaveSettings(_settings);
        _dirty = true;
        _ui?.ShowSettings(SettingsView());
    }

    public void SetStartLens(LensKind lens)
    {
        if (!LensCatalog.Openable(_settings).Contains(lens) || _settings.DefaultLens == lens)
        {
            return;
        }

        _settings = _settings with { DefaultLens = lens };
        _store?.SaveSettings(_settings);
        _ui?.ShowSettings(SettingsView());
    }

    public void SetCardsPerRow(int count)
    {
        if (!DiscoverSettings.CardsPerRowChoices.Contains(count) || _settings.CardsPerRow == count)
        {
            return;
        }

        _settings = _settings with { CardsPerRow = count };
        if (_ui is not null)
        {
            _ui.CardsPerRow = count;
        }

        _store?.SaveSettings(_settings);
        _ui?.ShowSettings(SettingsView());
    }

    public void SetListView(bool list)
    {
        if (_settings.ListView == list)
        {
            return;
        }

        _settings = _settings with { ListView = list };
        _dirty = true;
        _store?.SaveSettings(_settings);
        _ui?.ShowSettings(SettingsView());
    }

    public void ToggleLens(LensKind lens) => ApplyVisibility(LensCatalog.ToggleLens(_settings, lens));

    public void ToggleSection(RailSectionId section) => ApplyVisibility(LensCatalog.ToggleSection(_settings, section));

    /// <summary>Spec F28. The same instance back means the change would have left nothing to open.</summary>
    private void ApplyVisibility(DiscoverSettings next)
    {
        if (ReferenceEquals(next, _settings))
        {
            Notice(UiStrings.Get("notice.keepOne"));
            return;
        }

        _settings = next;
        EnsureLensVisible();
        _store?.SaveSettings(_settings);
        _ui?.ShowSettings(SettingsView());
        _dirty = true;
    }

    /// <summary>If the lens being shown was switched off, move to the first one that is on.</summary>
    private void EnsureLensVisible()
    {
        if (!_settings.IsVisible(_lens))
        {
            GoToLens(LensCatalog.FirstVisible(_settings), null);
        }
    }

    public void ExportLibrary()
    {
        if (_store is null || _library is null)
        {
            Notice(UiStrings.Get("notice.signIn"));
            return;
        }

        try
        {
            _store.WriteExport(_library);
            Notice(UiStrings.Get("notice.exported"));
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Export failed: {ex.GetType().Name}");
        }
    }

    public void ImportLibrary()
    {
        if (_store is null || _library is null)
        {
            Notice(UiStrings.Get("notice.signIn"));
            return;
        }

        var data = _store.ReadExport();
        if (data is null)
        {
            Notice(UiStrings.Get("notice.noExport"));
            return;
        }

        var result = _library.Import(data);
        Notice(UiStrings.Format("notice.imported", result.FavoritesAdded, result.ListsAdded, result.TagsAdded));
    }

    public void OrganizeAddTag(string tag)
    {
        if (_library is null || _organizeKey is null)
        {
            return;
        }

        if (!_library.AddTag(_organizeKey, tag))
        {
            Notice(UiStrings.Get("notice.tagsFull"));
        }

        ShowOrganize();
    }

    public void OrganizeRemoveTag(string tag)
    {
        if (_library is null || _organizeKey is null)
        {
            return;
        }

        _library.RemoveTag(_organizeKey, tag);
        ShowOrganize();
    }

    /// <summary>"+ New list" inside the organize panel: creates the list and puts this world in it.</summary>
    public void OrganizeNewList()
    {
        if (_library is null || _ui is null)
        {
            return;
        }

        _ui.ShowPrompt(UiStrings.Get("prompt.newListTitle"), UiStrings.Get("prompt.listPlaceholder"), "", name =>
        {
            if (_library?.CreateList(name) is { } list && _organizeKey is not null && _shown.TryGetValue(_organizeKey, out var entry))
            {
                _library.AddToList(list.Id, _organizeKey, entry.DisplayName);
            }

            ShowOrganize();
        });
    }

    /// <summary>"+ New list" in the rail: creates the list and opens it.</summary>
    public void NewListPrompt()
    {
        if (_library is null || _ui is null)
        {
            return;
        }

        _ui.ShowPrompt(UiStrings.Get("prompt.newListTitle"), UiStrings.Get("prompt.listPlaceholder"), "", name =>
        {
            if (_library?.CreateList(name) is { } list)
            {
                SelectList(list.Id);
            }
        });
    }

    public void ListToolPressed(ListTool tool)
    {
        if (_library is null || _ui is null || _listId is null || _library.GetList(_listId) is not { } list)
        {
            return;
        }

        var id = list.Id;
        switch (tool)
        {
            case ListTool.Rename:
                _ui.ShowPrompt(UiStrings.Get("prompt.renameTitle"), UiStrings.Get("prompt.listPlaceholder"), list.Name, name => _library?.RenameList(id, name));
                break;

            case ListTool.Delete:
                _ui.ShowConfirm(
                    UiStrings.Get("confirm.deleteListTitle"),
                    UiStrings.Format("confirm.deleteListBody", Safe(list.Name)),
                    [(UiStrings.Get("confirm.delete"), () =>
                    {
                        _library?.DeleteList(id);
                        GoToLens(LensKind.Favorites, null);
                    })]);
                break;

            case ListTool.MoveUp:
            case ListTool.MoveDown:
                var ids = _library.Lists.Select(l => l.Id).ToList();
                var at = ids.IndexOf(id);
                var to = tool == ListTool.MoveUp ? at - 1 : at + 1;
                if (at >= 0 && to >= 0 && to < ids.Count)
                {
                    (ids[at], ids[to]) = (ids[to], ids[at]);
                    _library.ReorderLists(ids);
                }

                break;
        }
    }

    // ------------------------------------------------------------------ join (with the confirmation menu)

    public void CardPressed(string key)
    {
        if (_lens == LensKind.CurrentWorlds && _openItems.TryGetValue(key, out var openWorld))
        {
            if (_join.Focus(openWorld.World.SessionId))
            {
                if (openWorld.Entry.CanSave)
                {
                    _library?.RecordOpened(openWorld.Entry.Key, openWorld.Entry.DisplayName); // F16: Recent includes worlds opened from here
                }
            }
            else
            {
                Notice(UiStrings.Get("notice.leaveFailed"));
            }

            _dirty = true;
            return;
        }

        if (!_shown.TryGetValue(key, out var entry) && !_index.TryGetEntry(key, out entry))
        {
            return;
        }

        if (_join.OpenSession(entry) is { } open)
        {
            _ = RunAsync(() => _join.JoinAsync(entry, open.SessionId), entry); // already open: focus, no menu
            return;
        }

        if (entry.Sessions.Count == 0)
        {
            ConfirmNewSession(entry);
        }
        else
        {
            ConfirmJoin(entry);
        }
    }

    /// <summary>
    /// The extra menu before joining, as on the stock Worlds tab: a themed confirmation panel with a button per
    /// joinable session (best first, at most three) and Cancel. (The engine's own choice dialog stretches its buttons
    /// across the screen, so the panel is our own.)
    /// </summary>
    private void ConfirmJoin(DiscoveryEntry entry)
    {
        var sessions = JoinPlanner.JoinableSessions(entry).Take(3).ToList();
        if (sessions.Count == 0)
        {
            JoinService.Report(entry.Sessions.Count > 0 ? JoinOutcome.Full : JoinOutcome.Ended);
            return;
        }

        var now = DateTime.UtcNow;
        var choices = new List<(string, Action)>();
        foreach (var session in sessions)
        {
            var label = sessions.Count == 1
                ? UiStrings.Get("confirm.join")
                : Safe(UiStrings.Format("confirm.sessionRow", CardText.Ellipsize(session.HostUsername, 14), CardText.Age(session.SessionBeginTime, now), session.ActiveUsers));
            var id = session.SessionId;
            choices.Add((label, () => _ = RunAsync(() => _join.JoinAsync(entry, id), entry)));
        }

        _ui?.ShowConfirm(UiStrings.Get("confirm.joinTitle"), UiStrings.Format("confirm.joinBody", Safe(entry.DisplayName)), choices);
    }

    private void ConfirmNewSession(DiscoveryEntry entry)
    {
        if (entry.Record is not { } record)
        {
            return;
        }

        _ui?.ShowNewSession(Safe(entry.DisplayName), (level, mobile) => StartNew(entry, record, level, mobile));
    }

    private void StartNew(DiscoveryEntry entry, RecordSnapshot record, SkyFrost.Base.SessionAccessLevel? level, bool mobileFriendly) =>
        _ = RunAsync(() => _join.StartNewSessionAsync(record, level, mobileFriendly), entry);

    private async Task RunAsync(Func<Task<JoinOutcome>> action, DiscoveryEntry? entry = null)
    {
        var outcome = await action();
        JoinService.Report(outcome);
        if (outcome == JoinOutcome.Opened && entry is { CanSave: true })
        {
            // Worlds opened through this screen make up the Recent lens (spec F16). Back on the update thread.
            var key = entry.Key;
            var name = entry.DisplayName;
            try
            {
                _world.RunSynchronously(() => _library?.RecordOpened(key, name));
            }
            catch (Exception)
            {
                // The world is gone.
            }
        }

        _dirty = true;
    }

    private static string Safe(string value) => StringParsingHelper.SanitizeFormatTags(value) ?? "";

    // ------------------------------------------------------------------ transitions

    /// <summary>A lens switch re-renders on the next tick (the old fade to black is gone; panels animate, the results do not).</summary>
    private void StartTransition()
    {
        _dirty = true;
        _lastRefreshUtc = DateTime.MinValue;
    }

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
