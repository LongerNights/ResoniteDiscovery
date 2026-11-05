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

internal sealed partial class DiscoverController
{
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
        // The engine completes the request on a pool thread; every state change happens on the update thread.
        Post(() => ApplyPage(generation, query, page, DateTime.UtcNow, cached: false));
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

    /// <summary>The entry for a key: what is on screen first, else the index.</summary>
    private bool TryFind(string key, out DiscoveryEntry entry) =>
        _shown.TryGetValue(key, out entry!) || _index.TryGetEntry(key, out entry!);

    /// <summary>
    /// Runs <paramref name="action"/> on the update thread, for continuations of engine calls that finish on a pool thread.
    /// Does nothing when the userspace world is already gone.
    /// </summary>
    private void Post(Action action)
    {
        try
        {
            _world.RunSynchronously(action);
        }
        catch (Exception)
        {
            // The world is gone; nothing to update.
        }
    }

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
        // The ranking only changes with a new history snapshot, an index change or other formulas, so it is reused between
        // renders; decoration, hiding, search and chips below are applied fresh each time.
        var formulas = _settings.Formulas;
        if (_insightRank is null || _insightLens != _lens || _insightIndexVersion != _index.Version
            || _insightSnapshotUtc != _lastSnapshotUtc || !ReferenceEquals(_insightFormulas, formulas))
        {
            _insightRank = _lens switch
            {
                LensKind.Trending => InsightLenses.Trending(_index.Entries, _history, now, formulas),
                LensKind.Rising => InsightLenses.Rising(_index.Entries, _history, now, formulas),
                _ => InsightLenses.HiddenGems(_index.Entries, _history, now, formulas),
            };
            _insightLens = _lens;
            _insightIndexVersion = _index.Version;
            _insightSnapshotUtc = _lastSnapshotUtc;
            _insightFormulas = formulas;
        }

        return _insightRank.Select(s => Dec(s.Entry)).Where(e => !HideRules.IsHidden(e, _settings) && _search.Matches(e) && _chips.Matches(e)).ToList();
    }

    private IReadOnlyList<ScoredEntry>? _insightRank;
    private LensKind _insightLens;
    private int _insightIndexVersion;
    private DateTime _insightSnapshotUtc;
    private FormulaSettings? _insightFormulas;

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
        var maxCards = Math.Min(_ui!.MaxCards, _settings.PageSizeFor());
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
                    with { Visited = true, CanLeave = open.World.CanLeave }
                : CardBuilder.Build(e, _join.OpenSession(e), now, _library?.IsStarred(e.Key) ?? false)
                    with { Visited = _library?.IsVisited(e.Key) ?? false })
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
                _openWorlds.CountOpenWorlds()),
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
}
