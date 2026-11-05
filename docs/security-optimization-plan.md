# Security and optimization plan

*Status: S1–S5, O1–O3, C1–C12 and most of S6 are implemented (2026-11-05); remaining: clear-history control, GalaxySieve folder removal, shutdown-thread check, C9 done as partial files.*

*From a full audit of v0.2.2 (commit 49c592e), 2026-11-04. Build and all 401 tests pass at that commit.*

## What the audit found is already right

The hard rules in `CLAUDE.md` hold. Nothing in this plan relaxes them.

- Joins and opens go only through `Userspace.OpenWorld`, with `HostUserId` set for the mutual-block check (`JoinService`).
- There are no Harmony patches, no own HTTP clients, no world writes, and no `ScreenEnabled` writes. The only stock writes are the
  Worlds button's `ActiveSelf` and `RadiantDash.CurrentScreen` (`TabTakeover`). Both are reverted on pause, stop and screen loss.
- `SessionSnapshot` has no field for `BroadcastKey`, `HostMachineId`, `HostUserSessionId` or URLs, so they cannot leak. Join errors
  log only the exception type.
- Session events are only queued, and `Pump` maps them on the update thread. Every async continuation in the controller
  goes back through `RunSynchronously`. `NotificationPanel.ShowNotification` can be called from any thread (V19).
- The account folder name is sanitized (`AccountPaths`), export file names go through `Path.GetFileName`, the search input is
  capped (512 characters, 24 terms), and most display text goes through `SanitizeFormatTags`.

## Priority changes

Ordered by risk. Each item has its location, the problem, the fix and how to test it.

### S1. Allow only known schemes for thumbnail URIs (security, high)

- **Where:** `CardView.SetThumbnail` (`Ui/DiscoverUi.cs:3958`) and `DiscoverUi.SetDetailThumbnail` (`:2092`).
- **Problem:** `SessionInfo.ThumbnailUrl` is set by whoever hosts a session. The card loads any absolute URI that
  `Uri.TryCreate` accepts, with `DirectLoad = true` for anything that is not `resdb`. That includes `http://` to LAN or tracking hosts,
  and `file://` paths. On Windows, a `file://host/share/x.png` (UNC) path can start an SMB connection that sends the user's NTLM
  credentials. V10 says the stock card uses the same source, so this may match stock behaviour. It is still cheap to close.
- **Fix:** add one `ThumbnailPolicy.TryAccept(string? raw, out Uri uri)` helper, used by both sites. It accepts `resdb`,
  `https` and `local`, and `local` only for open-world thumbnails from `Userspace.GetThumbnailData`. Anything else gets
  `NoThumbnail`. Put the scheme list in settings so it is not a literal.
- **VERIFY:** ask the dumper for `LegacyWorldThumbnailItem` / `WorldThumbnailItem` and the asset manager's handling of `file` and
  `http` URIs. Record what stock filters in `docs/verification.md` (new row). Keep the allowlist either way.
- **Test:** a Core test of the policy with `file://`, `\\host\share`, `http://`, `resdb:///…`, `https://…`, and an empty or null value.

### S2. Neutralize rich text in tag labels (security, medium)

- **Where:** card tag buttons (`DiscoverUi.cs:3883`, `_tags[i].Label.Content.Value = card.Tags[i].Text`) and chip labels
  (`:3246`, `mark + chip.Label`).
- **Problem:** tags come from other players' sessions and records. `SessionMapper` strips markup from names, descriptions and host
  names, but not from tags (`SessionMapper.cs:20`, `EngineRecordSource.cs:87`). These are the two places that show tag text raw.
  A tag like `<size=4000>` or `<color=…>` breaks the card layout or makes a card look like something else.
- **Fix:** strip at the source: `Tags = info.Tags?.Select(Plain)` in both mappers. Also wrap both label sites in the shared
  sanitizer from C1, so text from the library (personal tags, imported files) is covered too.
- **Test:** a mapper-level test is engine-bound, so add a check to the in-game test plan: a session tagged `<size=300>big` shows
  the literal text.

### S3. Validate settings loaded from disk (safety and network budget, medium)

- **Where:** `UserLibraryStore.LoadSettings` → `LensCatalog.Normalize` (`DiscoverController.cs:1042`); `DiscoverSettings`.
- **Problem:** nothing range-checks the tunables in `settings.json`. A hand edit, a corrupt write or a future bug can break the
  networking budget (rule 7) or stall the mod:
  - `autoRefreshInterval: "00:00:00"` makes `MaintainSearchLens` (`:423`) search again each time the last search finishes,
    back to back, for as long as the tab is open.
  - `manualRefreshCooldown: 0` removes the 15 s guard on `Sessions.ForceFetch()`.
  - `sessionChangesPerTick: 0` stops the session queue from ever draining. A huge value hitches the frame.
  - `snapshotIntervalScreenClosed: 0` takes a history snapshot every frame. `minWriteInterval: 0` writes to disk every frame.
    `historyRetention` has no upper bound.
- A related bug: `_cache` and `_pager` are built in the constructor from the defaults (`DiscoverMod` passes `new DiscoverSettings()`).
  Cache TTLs loaded from the account's settings are never applied.
- **Fix:** add `DiscoverSettings.Sanitize()`, which clamps every number and time span to a documented range (the ranges go in
  `docs/spec.md`, budget table). Call it in `LoadSettings` and after every in-app change. Have `CheckAccount` rebuild the cache
  and pager, or give them a settings accessor instead of copies.
- **Test:** Core tests that feed zero, negative and huge values through `Sanitize` and assert the clamped result, plus one for the
  minimum auto-refresh interval.

### S4. Make import bounded and exception-safe (robustness, medium)

- **Where:** `DiscoverController.ImportLibrary` (`:1900`), `UserLibrary.Import`, `JsonFileStore.Load`.
- **Problem:**
  - `Import` throws `InvalidDataException` for a newer format, and `ImportLibrary` does not catch it. The exception escapes the
    button handler and the user gets no message.
  - JSON with `"favorites": null` (or `items`, `lists`, `members`) deserializes to a null list. That causes a
    `NullReferenceException` in import, and also on load for the account's own files: `JsonFileStore` only falls back when the
    whole object is null.
  - There are no size caps. Every incoming favorite runs `IsStarred` (a linear scan) and every list member runs `Any`. That is
    O(n²) on the update thread, so a large or hostile export file freezes the game. Export files are meant to be shared.
- **Fix:** catch exceptions in `ImportLibrary` and show `notice.importFailed`. Normalize null collections after every `Load`
  (an `INormalizable` step in `JsonFileStore`). Cap the import (for example 5,000 favorites, 200 lists, 2,000 members per list),
  put the caps in settings, and use the key sets from O1 so the merge is linear.
- **Test:** Core tests for an import with null collections, an import over the caps, and an import with a future format version.

### S5. A failed save must not pause the mod (robustness, medium)

- **Where:** `Step` → `_saver?.Tick()` (`DiscoverController.cs:274`), `JsonFileStore.Save`.
- **Problem:** `LibrarySaver.Tick` is not inside a try. If a save throws (disk full, or the file locked by an editor, antivirus or
  OneDrive), the exception fails the whole tick. The file stays dirty, so the next ticks fail too, and after 5 failures `Pause()`
  turns the mod off. A `.tmp` file is also left behind.
- **Fix:** catch inside `LibrarySaver`, keep the file dirty, back off (for example 5 s, 30 s, 2 min), log once per file, delete the
  temp file on failure, and show one notice. Do the same for the direct `_store?.SaveSettings` calls (12 sites, folded into C3).
- **Test:** a Core test with a store whose `Save` throws: `Tick` returns without throwing, the file stays pending, and the retry
  waits for the backoff.

### O1. Move history I/O off the update thread and bound the file (performance, high)

- **Where:** `TickHistory` / `SaveHistory` (`DiscoverController.cs:1061`), `JsonFileStore.Save` (indented, `Flush(true)`).
- **Problem:** every 5 minutes, even with the tab closed, the update thread prunes the history, serializes it as indented JSON and
  fsyncs it. The local `history.json` is already 252 KB after about two days of on-and-off use. With 14 days of retention and 60 s
  snapshots it will reach several MB, which turns into a visible hitch. The spec's target with the tab closed is under 0.1 ms per
  frame.
- **Fix:** copy the samples into a `HistoryFile` on the update thread (cheap), then serialize and write it on a thread-pool task.
  Allow one write at a time and drop a write that is already superseded. Write compact JSON for history. Downsample samples older
  than 24 h, for example to the hourly maximum, which keeps Trending and Rising correct and makes the file much smaller. Use the
  same background path for the shutdown flush, with a bounded wait.
- **Test:** Core tests for downsampling (formula outputs unchanged on a fixture) and for the shape of the copied snapshot. Then
  measure in game with `perf` while the tab is closed.

### O2. Remove per-render linear scans (performance, medium)

- **Where:** `UserLibrary.IsStarred`, `ListIdsContaining`, `HideRules.IsHidden`, `InsightLenses.*`, and `Render`.
- **Problem:** `Render` runs up to 4 times a second while sessions change. For every entry it calls `IsStarred` (scans all
  favorites), `ListIdsContaining` (scans every member of every list) and `IsHidden` (`List.Contains` over the hidden worlds and
  authors). The insight lenses rescore every key's full sample history on every render, although the scores only change when a
  snapshot is taken.
- **Fix:** in `UserLibrary`, keep a favorite key set and a key-to-list-ids index, both updated on every change. In `HideRules`, build
  a `HashSet` of hidden ids once per settings change, cached on the settings record. Cache the insight lens rankings per
  (snapshot time, index version) and recompute only when either changes.
- **Test:** the existing library and hide tests still pass, plus a test that the index stays correct after add, remove, delete and
  import.

### O3. Stop the repeated scans in the takeover and Current Worlds (performance, low-medium)

- **Where:** `TabTakeover.FindButton` (`TabTakeover.cs:104`) and `Render` (`DiscoverController.cs:918`).
- **Problem:** if the stock Worlds button is missing, `FindButton` searches the whole dash for `RadiantDashButton`s on every tick,
  forever. Separately, every render on every lens calls `GetOpenWorlds()` just to count them for the rail. That builds each open
  world's user list and runs a contact lookup for each user. Current Worlds then calls it again.
- **Fix:** when the button is missing, look again at most once every 2 s. Add `IOpenWorldSource.Count` (no user lists), and reuse
  one `GetOpenWorlds()` result per render.

### S6. Smaller hardening items (security and privacy, low)

- **Logging:** the tick, UI-build and lens-switch handlers log `ex.Message` (`DiscoverController.cs:188, 503, 2169`). Engine
  exception text can contain session URLs, which is why `JoinService` already logs only the type. Log the type and first frame only.
- **Shutdown thread:** `Stop()` runs from `Engine.OnShutdown`. Which thread that is has not been verified, and `Stop()` flushes the
  library and writes `ActiveSelf` while `Tick` may still be running. VERIFY the thread. If it is not the update thread, set `_stopped`
  first, then do the flush and restore under one lock shared with `Step`.
- **`author:` lookups:** every settled prefix is a new distinct name (`author:w`, `author:wi`, …). Resolve only names of 3 or more
  characters and cap lookups per session (for example 20).
- **Privacy controls:** `recent.json` keeps up to 5,000 visited world keys, including private worlds the user was in, and
  `history.json` keeps 14 days. Neither can be cleared from the UI. Add "Clear history and visited marks" under
  Settings > Your data. The old `%APPDATA%\GalaxySieve` folder is copied, not moved, so a second copy of the history stays on disk.
  Delete it after a successful copy (ask Winter first).
- **Temp icon:** `IconLoader` writes to a fixed `%TEMP%\ResoniteDiscovery\galaxy-icon.png` and never deletes it. Delete it after the
  import.

## Consolidation and cleanup

These are lower risk than the items above. Do them after, or alongside, the fixes that touch the same code.

| # | What | Where | Change |
| --- | --- | --- | --- |
| C1 | Four copies of the same sanitizer (`Safe` ×2, `Plain`, `PlainText`) next to `SessionMapper.Plain`, which does something different (strip, not neutralize) | Controller `:2127`, `DiscoverUi.cs:2991, 2993, 4005` | One `RichText` static class: `Neutralize` (display) and `Strip` (mapping), each named for what it does |
| C2 | Two copies of the toast call | `DiscoverController.Notice`, `JoinService.Report` | One `Toasts.Show(message)` |
| C3 | The "change settings, save, re-show panel" pattern, repeated 12 times; `ToggleSetting.CardsPerRow` and `ListView` duplicate `SetCardsPerRow` and `SetListView` | Controller `:1703-1870` | `UpdateSettings(Func<DiscoverSettings, DiscoverSettings>, reshow)`, which also runs `Sanitize` (S3) and saves through the debounced saver (S5) |
| C4 | The lookup `_shown.TryGetValue(...) \|\| _index.TryGetEntry(...)`, repeated 7 times | Controller | `TryFind(key, out entry)` |
| C5 | `await …; try { _world.RunSynchronously(...) } catch { }`, repeated 5 times | `FetchAsync`, `HydrateAsync`, `LeaveAsync`, `RunAsync`, `ApplyPendingSearch` | `Post(Action)`, which also checks `_stopped` |
| C6 | Dead settings, never read: `MaxCachedEntries`, `MaxCardSlots`, `SearchPageSize`, `TransitionOut`, `TransitionIn`, `TransitionRise`, `JoinShowsSessionList`, `MaxRecentWorlds` (and `UserLibrary.MaxRecent` duplicates the last one) | `DiscoverSettings.cs` | Remove them (unknown JSON properties are ignored on load), or wire up `MaxRecentWorlds` and `MaxCachedEntries` if the spec still wants them |
| C7 | The stock-screen query is written out twice | `TryAttach` `:472`, `TabTakeover.FindStock` | One static `StockWorlds.Find(dash)` |
| C8 | The transparent "catcher" button colour setup is copied for the card and the detail panel (10 `SetA(0.02f)` lines) | `DiscoverUi.cs:2075-2083`, `:3655-3663` | `Theme.MakeCatcher(Slot)` |
| C9 | File size: `DiscoverUi.cs` is 4,006 lines and `DiscoverController.cs` 2,206 | — | Split into `partial` files by panel (Detail, Settings, Organize, NewSession, Prompt, Menus, CardView) and by concern (Actions, Settings, Library, Join, Render). A move only, no behaviour change, in its own commit |
| C10 | Stale or doubled comments: two `<summary>` blocks on `StartNewSession`, `AwaitingResults`, `AnimateTransitions` and `NotifyFavoriteLive`; "The stock Worlds tab is never touched" (`:25`); "Galaxy Sieve" (`:463`); "V30 is open" (`SearchQuery.cs:181`) | — | Fix them during the split |
| C11 | Debug logging left in: `Popover placed: …` on every popover | `DiscoverUi.cs:1329` | Remove it, or put it behind a debug setting |
| C12 | `CancellationToken` parameters that nothing passes or reads; `ReadExport` passes `() => null!` as defaults | `IRecordSource`, `UserLibraryStore.cs:80` | Drop the token, or honour it; give `Load` a nullable overload |

## Spec drift to settle

- The spec says at most 40 card slots and 60 thumbnails, but `CardSlotLimit` is 100 (rows per page times cards per row). Either
  update the spec targets or cap the page size at 60.
- The "≤ 500 cached entries" target is not enforced (C6). Records from hydration are never evicted from the index.

## Suggested order

1. **Security fixes (S1, S2, S3):** small, local and testable. Include the S1 verification row.
2. **Robustness (S4, S5):** these prevent freezes and the mod pausing itself.
3. **Performance (O1, then O2 and O3):** measure before and after with `perf` (tab open and tab closed).
4. **Cleanup:** C1 to C8 next to the fixes that touch the same code, then C9 as a separate move-only commit, then C10 to C12.
5. **Docs:** update the spec's budget table and limits (S3 ranges, spec drift), add one line per change to `docs/decisions.md`,
   and add the in-game checks (S2 tag markup, S1 thumbnail fallback, history with the tab closed) to a test plan.
