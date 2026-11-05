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
        PersistSettings();
        _dirty = true;
        Notice(UiStrings.Format("presets.saved", RichText.Neutralize(preset.Name)));
    });

    public void DeletePreset(int index)
    {
        if (index < 0 || index >= _settings.Presets.Count)
        {
            return;
        }

        _settings = FilterPresets.Delete(_settings, _settings.Presets[index].Name);
        PersistSettings();
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

    /// <summary>Detail panel: start a new session of the world (asks for its settings first).</summary>
    public void StartNewSession(string key)
    {
        if (TryFind(key, out var entry) && entry.Record is not null)
        {
            ConfirmNewSession(entry);
        }
    }

    /// <summary>Detail panel: join one specific live session of a world.</summary>
    public void JoinSession(string key, string sessionId)
    {
        if (!TryFind(key, out var entry))
        {
            return;
        }

        _ = RunAsync(() => _join.JoinAsync(entry, sessionId), entry);
    }

    /// <summary>Detail panel, Hide...: hide the world or everything by its author from the live, search and insight lenses (F19).</summary>
    public void HideOptions(string key)
    {
        if (!TryFind(key, out var entry))
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

        _ui?.ShowConfirm(UiStrings.Get("hide.title"), RichText.Neutralize(entry.DisplayName), choices);
    }

    private void ApplyHide(DiscoverSettings next)
    {
        _settings = next;
        PersistSettings();
        _dirty = true;
        Notice(UiStrings.Get("hide.done"));
    }

    /// <summary>Detail panel, More by author: Browse narrowed to that owner (stock "By owner"), cleared by picking any lens.</summary>
    public void MoreByAuthor(string key)
    {
        if (!TryFind(key, out var entry))
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

        var choices = groups.Take(Max).Select(g => (CardText.Ellipsize(RichText.Neutralize(g.Name), 28), (Action)(() => Choose(g)))).ToList();
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
        StartTransition();
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
        StartTransition();
    }

    public void CardTagPressed(string tag, bool personal)
    {
        _chips.Cycle(tag.Trim(), personal);
        QueryChanged();
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

        if (!TryFind(key, out var entry))
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
                : RichText.Neutralize(UiStrings.Format("confirm.sessionRow", CardText.Ellipsize(session.HostUsername, 14), CardText.Age(session.SessionBeginTime, now), session.ActiveUsers));
            var id = session.SessionId;
            choices.Add((label, () => _ = RunAsync(() => _join.JoinAsync(entry, id), entry)));
        }

        _ui?.ShowConfirm(UiStrings.Get("confirm.joinTitle"), UiStrings.Format("confirm.joinBody", RichText.Neutralize(entry.DisplayName)), choices);
    }

    private void ConfirmNewSession(DiscoveryEntry entry)
    {
        if (entry.Record is not { } record)
        {
            return;
        }

        _ui?.ShowNewSession(RichText.Neutralize(entry.DisplayName), (level, mobile) => StartNew(entry, record, level, mobile));
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
            Post(() => _library?.RecordOpened(key, name));
        }

        _dirty = true;
    }
}
