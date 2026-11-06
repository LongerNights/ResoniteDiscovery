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
        Post(() =>
        {
            _hydrationOutstanding--;
            if (snapshot is not null)
            {
                _index.SetRecord(snapshot);
                _dirty = true;
            }
        });
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
        _saver = new LibrarySaver(_store, _library, _settings.MinWriteInterval, log: message => UniLog.Warning(message));
        _library.Changed += _ => _dirty = true;
        var firstAccount = _accountId is null;
        _accountId = id;
        _organizeKey = null;
        _settings = LensCatalog.Normalize(_store.LoadSettings());
        _cache = new SearchCache(_settings.SearchCacheFirstPageTtl, _settings.SearchCacheLaterPageTtl); // the account's TTLs, not the defaults
        _pager.PageSize = _settings.PageSizeFor(); // no request here: the lens fetches when it is shown
        _pager.Reset();
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

    private volatile bool _historyWriting;

    /// <summary>
    /// Prunes and thins the history and copies it (cheap, update thread), then serializes and writes the copy on a pool
    /// thread. One background write at a time; a tick that finds one running skips and the next tick saves. Shutdown and
    /// account switches pass <paramref name="background"/> false and write inline.
    /// </summary>
    private void SaveHistory(DateTime now, bool background = true)
    {
        _lastHistorySaveUtc = now;
        var store = _store;
        if (store is null || (background && _historyWriting))
        {
            return;
        }

        HistoryFile file;
        try
        {
            _history.Prune(now, _settings.HistoryRetention);
            var formulas = _settings.Formulas;
            _history.Downsample(now, formulas.RisingRecentWindow + formulas.RisingPriorWindow + TimeSpan.FromHours(6));
            file = _history.ToFile();
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Could not save history: {ex.GetType().Name}");
            return;
        }

        if (!background)
        {
            WriteHistory(store, file);
            return;
        }

        _historyWriting = true;
        _ = Task.Run(() =>
        {
            try
            {
                WriteHistory(store, file);
            }
            finally
            {
                _historyWriting = false;
            }
        });
    }

    private static void WriteHistory(UserLibraryStore store, HistoryFile file)
    {
        try
        {
            store.SaveHistory(file);
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
            SaveHistory(DateTime.UtcNow, background: false);
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

    private static void Notice(string message) => Toasts.Show(message);

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
        if (!TryFind(key, out var entry))
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
        if (_ui is null || (!TryFind(key, out var entry)))
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
        var name = RichText.Neutralize(open.Entry.DisplayName);
        var host = open.World.IsHost;

        // Like the stock tab: a world that can be saved offers Save, Save as and Discard changes instead of a plain leave.
        if (_join.SavePromptFor(sessionId) is { } save)
        {
            var choices = new List<(string, Action)>();
            if (save.CanSave)
            {
                choices.Add((UiStrings.Get("confirm.save"), () => _ = SaveAndCloseAsync(sessionId, SaveType.Overwrite)));
            }

            if (save.CanSaveAs)
            {
                choices.Add((UiStrings.Get("confirm.saveAs"), () => _ = SaveAndCloseAsync(sessionId, SaveType.SaveAs)));
            }

            choices.Add((UiStrings.Get("confirm.discard"), () => _ = LeaveAsync(sessionId)));
            _ui.ShowConfirm(UiStrings.Get("confirm.closeTitle"), UiStrings.Format("confirm.closeBody", name), choices);
            return;
        }

        _ui.ShowConfirm(
            UiStrings.Get("confirm.leaveTitle"),
            UiStrings.Format(host ? "confirm.endBody" : "confirm.leaveBody", name),
            [(UiStrings.Get(host ? "confirm.end" : "confirm.leave"), () => _ = LeaveAsync(sessionId))]);
    }

    private async Task SaveAndCloseAsync(string sessionId, SaveType saveType)
    {
        var ok = await _join.SaveAndCloseAsync(sessionId, saveType);
        Post(() =>
        {
            if (!ok)
            {
                Notice(UiStrings.Get("notice.saveFailed"));
            }

            _dirty = true;
        });
    }

    private async Task LeaveAsync(string sessionId)
    {
        var ok = await _join.LeaveAsync(sessionId);
        Post(() =>
        {
            if (!ok)
            {
                Notice(UiStrings.Get("notice.leaveFailed"));
            }

            _dirty = true;
        });
    }

    // ---- settings, export and import

    private SettingsModel SettingsView() =>
        new(_settings.AnimateTransitions, RailBuilder.Title(_settings.DefaultLens), _settings,
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
            UniLog.Warning($"[Discover] Restoring the stock Worlds tab failed: {ex.GetType().Name}");
        }
    }

    public void OpenClassicBrowser()
    {
        if (_takeover?.OpenClassic() != true)
        {
            Notice(UiStrings.Get("notice.classicMissing"));
        }
    }

    /// <summary>Saves the settings; a disk error is logged and never reaches the button that caused the change.</summary>
    private void PersistSettings()
    {
        try
        {
            _store?.SaveSettings(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UniLog.Warning($"[Discover] Could not save settings: {ex.GetType().Name}");
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
            case SettingKey.ListView:
                SetListView(!_settings.ListView);
                return;
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
                    PersistSettings();
                    _dirty = true;
                });
                return; // the prompt replaces the settings panel
            case SettingKey.CardsPerRow:
                var choices = DiscoverSettings.CardsPerRowChoices;
                var index = Array.IndexOf(choices, _settings.CardsPerRow);
                SetCardsPerRow(index < 0 ? choices[0] : choices[(index + 1) % choices.Length]);
                return;
            case SettingKey.StartLens:
                var openable = LensCatalog.Openable(_settings);
                var at = openable.ToList().IndexOf(_settings.DefaultLens);
                _settings = _settings with { DefaultLens = openable[(at + 1) % openable.Count] };
                break;
        }

        PersistSettings();
        _ui?.ShowSettings(SettingsView());
    }

    public void RestoreHidden(HiddenKind kind, string id)
    {
        _settings = HideRules.Restore(_settings, kind, id);
        PersistSettings();
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
        PersistSettings();
        _ui?.ShowSettings(SettingsView());
    }

    /// <summary>The animation speed slider moved. Applied at once; the file is written once the slider has rested (see <see cref="FlushSettings"/>).</summary>
    public void SetAnimationSpeed(float speed)
    {
        speed = Math.Clamp(speed, DiscoverSettings.AnimationSpeedMin, DiscoverSettings.AnimationSpeedMax);
        if (!float.IsFinite(speed) || Math.Abs(_settings.AnimationSpeed - speed) < 0.005f)
        {
            return;
        }

        _settings = _settings with { AnimationSpeed = speed };
        if (_ui is not null)
        {
            _ui.AnimationSpeed = speed;
        }

        _settingsWriteDueUtc = DateTime.UtcNow.AddSeconds(0.6);
    }

    /// <summary>The preview pan speed slider moved; same handling as <see cref="SetAnimationSpeed"/>.</summary>
    public void SetPreviewPanSpeed(float speed)
    {
        speed = Math.Clamp(speed, DiscoverSettings.PreviewPanSpeedMin, DiscoverSettings.PreviewPanSpeedMax);
        if (!float.IsFinite(speed) || Math.Abs(_settings.PreviewPanSpeed - speed) < 0.005f)
        {
            return;
        }

        _settings = _settings with { PreviewPanSpeed = speed };
        if (_ui is not null)
        {
            _ui.PreviewPanSpeed = speed;
        }

        _settingsWriteDueUtc = DateTime.UtcNow.AddSeconds(0.6);
    }

    private DateTime? _settingsWriteDueUtc;

    /// <summary>Writes settings changed by a slider once it has been still for a moment, so a drag is one write, not hundreds.</summary>
    private void FlushSettings(DateTime now)
    {
        if (_settingsWriteDueUtc is { } due && now >= due)
        {
            _settingsWriteDueUtc = null;
            PersistSettings();
        }
    }

    public void SetRowsPerPage(int rows)
    {
        if (!DiscoverSettings.RowsPerPageChoices.Contains(rows) || _settings.RowsPerPage == rows)
        {
            return;
        }

        _settings = _settings with { RowsPerPage = rows };
        ApplyPageSize();
        PersistSettings();
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

        ApplyPageSize();

        PersistSettings();
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
        ApplyPageSize();
        PersistSettings();
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
        PersistSettings();
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

        try
        {
            var result = _library.Import(data);
            Notice(UiStrings.Format("notice.imported", result.FavoritesAdded, result.ListsAdded, result.TagsAdded));
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Import failed: {ex.GetType().Name}");
            Notice(UiStrings.Get("notice.importFailed"));
        }
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
                    UiStrings.Format("confirm.deleteListBody", RichText.Neutralize(list.Name)),
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
}
