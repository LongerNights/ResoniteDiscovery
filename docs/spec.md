# ResoniteDiscovery — Worlds Tab Overhaul: Technical Specification

Nov 3, 2026 · @Winter · rewritten Nov 3, 2026 for the Worlds tab overhaul (supersedes the Nov 2 "sit beside" spec; see `decisions.md`)

**ResoniteDiscovery** is a client-side Resonite mod that **replaces the dash's Worlds tab with a cleaner one** (built against 2026.10.29.1239). It keeps everything the stock world browser can do, puts the search tools the stock tab hides behind its "Custom" button in plain sight, folds the six category buttons into one Browse view with recommended tags, adds a dedicated view of the worlds you are in right now, and brings over ResoniteDiscovery's own extras: Favorites, Recent, named lists and personal tags.

## Summary and scope

**Why the change.** An audit of the stock Worlds tab (Nov 2–3, 2026) found that most of the discovery features ResoniteDiscovery set out to add already exist in the stock browser, but are hard to find:

- The useful search controls (sort key and direction, required and excluded tags, featured-only, date window, owner) sit behind a **Custom** button at the bottom of the filter rail, as one long, cluttered list. Nothing on the main screen hints that they exist.
- **Social, Games, Avatars, Art, Tutorials and Educational** are six rail buttons that each just add one tag to the search. They take a third of the rail and teach nothing about tags.
- Several preset buttons use odd defaults, for example sorting by publish date where a popularity sort (total visits) is what people expect.
- Worlds the user is already in are mixed into the normal results, which makes it unclear what is "open" and what is "out there".

So instead of a second discovery tab next to the stock one, ResoniteDiscovery becomes **the Worlds tab**: a full-featured replacement that takes the stock tab's place in the dash, with the stock browser still one click away as a fallback.

**Target.** Resonite 2026.10.29.1239 client (FrooxEngine 2026.10.29.1239, SkyFrost.Base 1.0.0.0). Not the headless client. Built with the Remora.Resonite.Sdk as a **ResoniteModLoader (RML) mod**: a plain DLL in `rml_mods/`. On the author's install, RML mods load through MonkeyLoader's RML compatibility pack. Other loaders would need only a new shim.

**Goals**

1. **Replace, don't add.** The dash shows one Worlds tab, and it is ResoniteDiscovery's. The stock tab is hidden at runtime only (nothing saved), and can be shown again from Settings or reached from a "Classic browser" button.
2. **Parity first.** Everything the stock Worlds tab can do is reachable in the new tab in the same number of clicks or fewer (the parity table below is the checklist).
3. **Search in plain sight.** A search box, sort menu (key and direction) and a Filters popover on every search lens, replacing the hidden Custom panel.
4. **One Browse view** instead of six category buttons: a row of recommended tags (the six categories first, then popular tags from what is on screen), defaulting to the most-visited sort.
5. **A Current Worlds view** that lists exactly the worlds the user has open, with focus, info and leave.
6. **ResoniteDiscovery extras:** Live Now and Friends (live sessions merged by world), Favorites, named lists, personal tags, Recent, export and import; Trending, Rising and Hidden Gems from local history (off by default; see "Insights status and future").
7. **Sensible defaults:** popularity sorts by default, the user's choices remembered.
8. Joining and opening always go through the engine's own checks.

**Non-goals**

- No Harmony patches of stock code and no edits to the stock Worlds screen's saved facets. The overhaul is a replacement screen plus one runtime-only change to the stock tab's button (Integration §2).
- No server component, account system or external API.
- No content moderation, rating or reporting system.
- No change to other players' experience. The mod is local only.
- No cloud sync of lists or tags. They move between machines through export and import.
- No personalization or machine-learned recommendation.

**Release plan**

| Release | Contents | Exit criterion |
| --- | --- | --- |
| Overhaul MVP | Tab takeover with fallback; rail: Current Worlds, Live Now, Friends, Browse, Featured, New, Random, Favorites, lists, Recent, My Worlds, Group Worlds; search, sort, filters (stock Custom parity); recommended tags; detail panel; join, open and new-session options; settings; export and import | Every row of the parity table passes; a user can find and join a populated public session in 3 clicks, find "Games" worlds in 2 clicks from opening the tab, and see every world they have open in 1 click |
| v1.1 | Local history, Trending, Rising, Hidden Gems, saved filter presets, favorite notifications, hide world or host, hidden word list | Trending list is stable across a 2-hour session and survives restart |
| v2 | Optional shared aggregation service, content tags if the platform adds them, watchlists | Decided after v1.1 usage |

**What carries over from the ResoniteDiscovery build (M0–M4b).** The engine-free core (merge index, visibility rule, lenses, search syntax, persistence, formulas), the session and record adapters, the join service, the card grid, detail panel, organize panel, settings panel, theme and transitions all stay. The changes are: the tab's identity and placement (Integration §1–2), the rail catalog (Browse replaces Most Visited and absorbs the categories; Current Worlds is new), the search controls (Custom parity), the new-session options, and settings migration.

## The stock Worlds tab: audit and parity

The stock tab is a `GridContainerScreen` whose facets are laid out by `WorldsScreenInitializer` (batch 1): `WorldListFacetPreset` at `[12,0,52,33]`, `SearchBarFacetPreset` `[12,33,38,3]`, `PagingControlFacetPreset` `[50,33,14,3]`, `WorldFilterFacetPreset` (the left rail) `[0,0,12,36]`. Its list is `LegacyWorldListManager` → `LegacyWorldThumbnailItem` → `LegacyWorldDetail`. Search parameters come from `LegacyWorldListManager.GenerateSearchParameters`, which can set every `SearchParameters` field below except `ExtraSignatures`.

**Audit result (V24, closed Nov 3, 2026).** Read from the decompile of `WorldFilterFacetPreset` and `LegacyWorldListManager`, and checked against the live screen (the rail's button slots match). The stock rail has 16 preset buttons plus a **Custom** button that slides in a second panel with every other control. Each preset button sets a bundle of data-source, filter, sort and paging values, plus a hidden search term:

| Stock button | Sources | Hidden search | Sort | Notes |
| --- | --- | --- | --- | --- |
| Everything | open worlds, sessions, published | none | first published ↓ | |
| Featured | published | none | first published ↓ | featured only |
| MMC26 | published | `+mmc26` | random | event button, 200 per page |
| Published Worlds | published | none | first published ↓ | |
| Active Sessions | open worlds, sessions | none | first published ↓ | sessions with at least 1 user |
| Contacts Sessions | open worlds, sessions | none | first published ↓ | at least 1 contact present, access up to Contacts |
| Headless Sessions | open worlds, sessions | none | first published ↓ | headless hosts only, **empty sessions shown** |
| Social | all but local | `+social` | first published ↓ | |
| Avatars | all but local | `+avatar` | **total visits ↓** | the only category that sorts by popularity |
| Games | all but local | `+game` | first published ↓ | |
| Art | all but local | `+art` | first published ↓ | |
| Tutorials | all but local | `+tutorial` | *keeps whatever sort was set* | the button sets no sort |
| Educational | all but local | `+educational` | first published ↓ | the tag is `educational`, not `education` |
| Random | published | none | random | |
| My Worlds | local + published | none | first published ↓ (creation date for own worlds) | own worlds, private search |
| Group Worlds | local + published | none | same | own-worlds search with no owner name |

Page size is 60 (200 for MMC26). The **Custom** panel holds: four data-source checkboxes (open worlds, active sessions, published worlds, local worlds); a hidden "base search" text; By owner with User or Group radios; "Submitted to group" (editable only in a universe); Featured only; My worlds; minimum users (0 to 32) and minimum contacts (0 to 32) sliders; Only headless; Incompatible sessions; a maximum access level radio (Anyone, Registered users, Contacts, LAN, Private); minimum uptime; sort direction; and six sort parameters (first published, last updated, created, total visits, last visit, random). `MinDate` and `MaxDate` exist as variables but have no control. Two sort details: "last visit" has no server equivalent, so the stock list fetches its default sort and orders the page client-side by the user's last visit; the stock Random comparator reads `Rating` (a stock bug, ignored).

**Parity table.** Each stock capability and where it lives in the overhaul. "Not ported" and "Gap" rows carry the reason; the F-ids are in the requirements table.

| Capability | Stock tab | Overhaul | F |
| --- | --- | --- | --- |
| Free-text search | Search bar, bottom | Search box in the top bar, every lens; Ctrl+F | F4 |
| Hidden base search | Custom, Base search | Not needed: the search box, chips and `+tag:` cover it | F4 |
| Sort: first published, last updated, total visits, random | Custom, Sorting | Sort menu | F14 |
| Sort: created | Custom, Sorting | Not ported yet: the server supports `CreationDate`, but the sort popover has no room for a tenth row. Add with M9 when the popover is reworked | F14 |
| Sort: last visit | Custom, Sorting (client-side) | Not ported: needs the engine's visit history; candidate for a local sort | none |
| Sort direction | Custom, Sorting | Direction toggle in the sort menu | F14 |
| Required and excluded tags | Hidden search terms (`+tag`, `-tag`) | Tag chips (click to require, again to exclude), `+tag:` syntax, recommended tags in Browse | F5, F29 |
| Category buttons | Six rail buttons (`social`, `avatar`, `game`, `art`, `tutorial`, `educational`) | One Browse lens with the six as chips, sorted by total visits | F29 |
| Preset sorts | Mostly first published ↓ | Browse defaults to total visits ↓ (stock does that only for Avatars) | F29 |
| Everything, Published Worlds | Rail | Browse | F3, F29 |
| Featured | Rail, and Custom, Featured only | Featured lens; "Featured only" in Filters on Browse and New | F11, F32 |
| Random | Rail | Random lens | F3 |
| Event button (MMC26) | Rail | Not ported: event-specific; type `+mmc26` in search | none |
| Active Sessions | Rail | Live Now | F2 |
| Contacts Sessions | Rail | Friends | F10 |
| Headless Sessions, Only headless | Rail; Custom | Filters, Host: Headless on Live Now. Stock also lists *empty* headless sessions, so Live Now has an "Include empty" filter (built M9) | F11 |
| Minimum users | Custom slider 0 to 32 | Filters, Minimum users (steps to 20) | F11 |
| Minimum contacts | Custom slider 0 to 32 | Partial: the Friends lens (at least 1 contact). Add a minimum-friends filter in M9 if wanted | F11 |
| Minimum uptime | Custom | Filters, "Up for N+" stepper on session lenses (built M9) | F32 |
| Show incompatible sessions | Custom checkbox | "Show other versions" in Filters | F11, Q2 |
| Max access level (Anyone to Private) | Custom radios | By design not ported for other people's sessions: the visibility rule hides private, LAN and hidden sessions. Your own private worlds are in Current Worlds | none |
| Data source: open worlds | Custom checkbox | Current Worlds lens, and the Open badge everywhere | F30, F8 |
| Data sources: sessions, published | Custom checkboxes | Sessions and worlds are merged on every card; Live Now and Browse are the two views | F2, F3 |
| Data source: local worlds | Custom checkbox; included in My Worlds and Group Worlds | **Not ported** (worlds saved only on this machine). Decide in M9 whether My Worlds should include them | F24 |
| By owner, user or group | Custom, By owner and radios | "More by author" in the detail panel (built M9, by owner id so no name lookup, V30 not needed; works for users and groups); `author:name` in search works client-side on loaded results (built M9; server-side resolution still open on V30); Group Worlds lens with a group picker | F32, F24 |
| Submitted to group | Custom (universe only) | Not ported: a client in a universe is outside this mod's scope (V14) | none |
| My worlds | Rail and Custom | My Worlds lens | F24 |
| Date window (`MinDate`, `MaxDate`) | No control in the stock tab | Extra: "Published" in Filters (needs V7) | F32 |
| World detail | `LegacyWorldDetail` | Detail panel (Info on each card) | F18 |
| Open (join best session or focus) | Detail, Open | Join on the card, confirmation menu | F7 |
| New session with options | Detail, `CustomWorldStart` and `NewWorldDialog` | "New session" menu with access level and Advanced | F33 |
| Paging | Paging facet, 60 per page | Previous and Next, 40 per page (card-slot limit) | F3 |
| Anything not yet ported | none | "Classic browser" button opens the hidden stock tab | F31 |

The gate for the overhaul MVP is that every row passes in game, or is explicitly marked "not ported" with its reason in `decisions.md`.

## Platform research

VRChat and Roblox both treat discovery as several ranked views plus search, not one list. This spec borrows the patterns that work from client-visible data alone and drops the ones that need server-side signals.

| Pattern | Where seen | Adopted here as |
| --- | --- | --- |
| Curated rows: Popular, Active, Featured | [VRChat Worlds tab](https://wiki.vrchat.com/wiki/Worlds) | Browse (most visited), Live Now, Featured |
| Search across title, description, tag, author, sortable by relevance, last updated, popular | [VRChat Worlds tab](https://wiki.vrchat.com/wiki/Worlds) | Search box with term syntax and sort menu on every lens |
| Tag include (any-of) and exclude lists, many sort keys | [VRChat world API](https://vrchat.community/reference/get-active-worlds) | Tag chips with include and exclude states |
| One "browse by category" entry with chips instead of one button per category | Common store pattern | Browse lens with recommended tags |
| "My Worlds" group: recents and favorite lists | [VRChat Worlds tab](https://wiki.vrchat.com/wiki/Worlds) | Yours section: Favorites, lists, Recent |
| Live occupancy plus daily snapshots powering weekly trending and growth ranks | [VRCmap](https://vrcmap.com/) | Local snapshot history for Trending and Rising (v1.1) |
| Favorites-to-visits ratio and "hidden gems" sorts | [VRChat Worlds Search](https://search.vrcwwt.com/worlds/) | Hidden Gems lens (v1.1) |
| Platform and capacity filters | [VRCmap](https://vrcmap.com/) | Device-mix, capacity and mobile-friendly filters |
| Like-ratio thresholds gate visibility in top-rated sorts | [Roblox discovery guide](https://bloxg.com/guides/roblox-algorithm) | Not adopted: Resonite exposes no like or vote data |
| Non-personalized charts so every user sees the same ranking | [Roblox DevForum](https://devforum.roblox.com/t/testing-an-enhanced-discover-page-top-charts-and-new-sorts/2954676) | All lenses are deterministic and explainable |

**What Resonite cannot support yet.** Like ratios, per-user ratings, dwell time and unique-visitor counts are not exposed to clients. `Record.Rating` exists in the data model but whether the cloud populates it is unverified (V6). Trending and Rising are approximations from what one client observes while running, and the UI labels them that way. Testers found them too sparse to be useful on local data alone, so the Insights section is off by default (see "Insights status and future").

**Community requests that match this mod.** Resonite's issue tracker has open requests for [an engagement-based trending tab](https://github.com/Yellow-Dog-Man/Resonite-Issues/issues/7332) and [content-warning world tags](https://github.com/Yellow-Dog-Man/Resonite-Issues/issues/7321). If the platform ships either, consume it in place of the local approximations.

**Prior art.** [WorldsTabOverhaul](https://github.com/MissMolly22/WorldsTabOverhaul) reworks the stock tab with folders, favorites and a Popular list. Review its approach (in particular how it replaces or patches the stock tab) and licence before M6; this spec does not depend on it.

## Architecture

```text
Engine.Cloud.Sessions ─┐                     ┌─ DiscoverController ─ UI (the Worlds tab screen)
Engine.Cloud.Records  ─┤─ adapters ─ Core ───┤
Engine.Cloud.Contacts ─┤  (engine)   (no     └─ JoinService ─ Userspace.OpenWorld / FocusWorld
Engine.WorldManager   ─┘             engine)
                                             TabTakeover ─ stock Worlds button (runtime hide/restore only)
```

Data flows left to right: the engine's cloud modules and world manager feed thin adapters, the adapters feed an engine-free core, and one controller turns the core's lenses into UI and the user's clicks into joins. Only the adapters, the controller and `TabTakeover` touch the engine, which keeps the core unit-testable.

1. Session events from the cloud are queued and applied per tick, never in the callback.
2. Record searches start only from controller actions and respect the networking budget.
3. All UI changes happen on the engine's update thread.
4. The core never calls the engine; it reads `ISessionSource`, `IRecordSource` and `IOpenWorldSource` and returns plain data.
5. Joins and opens leave the controller only through `Userspace.OpenWorld`; focusing an open world uses the engine's focus call.
6. The only write outside the mod's own slots is `TabTakeover` toggling the stock Worlds button slot's `ActiveSelf` (and switching the dash away from the stock screen), both runtime-only and reverted on shutdown or failure.

## Data sources

Calls marked ✓ were read in the decompile of 2026.10.29.1239 or a live probe (evidence in `verification.md`). Calls marked VERIFY are new for the overhaul and must be confirmed before use. `Engine.Cloud` is the `SkyFrostInterface`; its modules are `Sessions`, `Records`, `Stats`, `Contacts`, `Groups` and `Status`.

| Need | Call | Behaviour that matters |
| --- | --- | --- |
| Live session list ✓ | `Engine.Cloud.Sessions.GetSessions(List<SessionInfo>)` | Returns cached sessions, skipping expired ones. Cache is filled by one full public fetch at startup, then incremental updates. |
| Live changes ✓ | Events `SessionAdded`, `SessionUpdated`, `SessionRemoved` (`Action<SessionInfo>`), `SessionsChanged(bool addedOrRemoved)` | Expiry sweep every 15 s; heartbeat 5 s. The per-session events fire synchronously **while holding `SessionsManager`'s internal lock**, on whatever thread delivered the update. Handlers only enqueue (V9). |
| One session ✓ | `Sessions.TryGetInfo(string sessionId)` or `TryGetInfo(Uri)` | Case-insensitive id lookup. |
| Sessions of a world ✓ | `Sessions.GetSessionsForWorldId(RecordId, List<SessionInfo>)`, `RegisterWorldIdChanges(…)` | Matches on `SessionInfo.ActualCorrespondingWorldId`. Pair every register with `UnregisterWorldIdChanges`. |
| Force refresh ✓ | `Sessions.ForceFetch()` | Manual refresh button only, rate limited. |
| Published-world search ✓ | `Engine.Cloud.Records.FindRecords<SkyFrost.Base.Record>(SearchParameters, TimeSpan? timeout = null, bool throwOnError = true)` | Posts to `records/pagedSearch`. Returns `CloudResult<SearchResults<R>>` with `Records` and `HasMoreResults`. Check `IsOK`. |
| Batched record lookup ✓ | `Records.RecordCache<SkyFrost.Base.Record>().Get(RecordId)` | Memoized; misses batch up to 32 ids per `records/list` call. |
| Record URI ✓ | `Records.GenerateRecordUri(ownerId, recordId)` | Builds the platform URI to open a world. |
| Friends in session ✓ | `Engine.Cloud.Contacts.CountPresentContacts(SessionInfo)` | Counts present `SessionUsers` in the contacts list. |
| Group membership ✓ | `Engine.Cloud.Groups.CurrentUserMemberships`, `GetCurrentGroups(List<Group>)`, event `MembershipsUpdated` (V18) | Cached, zero requests. |
| Open or join ✓ | `Userspace.OpenWorld(WorldStartSettings)` returns `Task<World>` | Reuses an existing world, resolves session ids, blocks on mutual block, shows the load indicator. |
| Already open? ✓ | `Userspace.GetExistingWorld(WorldStartSettings)`, `Userspace.FocusWorld(WorldStartSettings)`, `WorldManager.FocusWorld(World)` | Open badge; focus instead of re-joining (V4). |
| Open worlds list (VERIFY V25) | `Engine.WorldManager.Worlds` (or the enumerator the session switcher uses), `WorldManager.WorldAdded` ✓, `WorldManager.WorldRemoved` | Source of Current Worlds. Exclude the userspace world. Changes are applied on the update thread. |
| Open world details (VERIFY V25) | `World.Name`, `World.SessionId`, `World.AccessLevel`, `World.UserCount`, `World.MaxUsers`, `World.HostUser`, `World.IsAuthority`, `World.CorrespondingRecord`, `World.Focus`, `World.Time` / session start, `WorldManager.FocusedWorld` | Card text, saveability (record or custom session id), focus badge, uptime. |
| Open world thumbnail (VERIFY V26) | `Userspace.GetThumbnailData(world)?.LocalThumbnail` | The stock card uses it first (V10). |
| Leave or close a world (VERIFY V27) | The call the engine's own session control / world switcher uses to leave (candidates: `Userspace.ExitWorld`, `Userspace.EndSession`, `World.Destroy`) | Leave must ask first and must use the engine's own path, so the engine's own "save changes?" and host-migration behaviour apply. |
| New-session options (VERIFY V28) | `WorldStartSettings.DefaultAccessLevel`, `UnsafeMode`, `ForcePorts`, as set by `LegacyWorldDetail.CustomWorldStart` from `NewWorldDialog` ✓ (V4) | Parity for "start new session". |
| Stock tab structure ✓ (V24) | `WorldFilterFacetPreset` (rail presets and Custom panel), `LegacyWorldListManager.GenerateSearchParameters` | Category tags and Custom controls, recorded in the parity table. |
| Add a dash screen ✓ | `UserspaceRadiantDash.Dash.AttachScreen(string name, colorX color, Uri icon)` | Returns a plain `RadiantDashScreen`. |
| Stock tab button ✓ (V29) | `RadiantDash.Slot`, then `GetComponentsInChildren<RadiantDashButton>()` (not `ButtonsRoot`: that is the overlay's Buttons slot) and match the public `RadiantDashButton.Screen` reference to the stock screen; `RadiantDash.CurrentScreen` (public `SyncRef`) | Takeover: hide the stock button; switch away from the stock screen. A button press is exactly `dash.CurrentScreen.Target = button.Screen.Target`. |
| Toasts ✓ | `NotificationPanel.ShowNotification(userId, message, thumbnail, colorX, NotificationType)` (V19) | Safe from any thread. Put the whole text in `message`. |
| Clipboard ✓ (decision 2026-11-02) | `InputInterface.Clipboard.SetText` | Copy link. |

**Two record types.** Search and batch calls return `SkyFrost.Base.Record`. `WorldStartSettings` takes `FrooxEngine.Store.Record` or a `Uri`. Open worlds with `new WorldStartSettings(uri, hostUserId)` where `uri` comes from `GenerateRecordUri` for records, or from the session's own URLs for sessions (V4, V5).

**SearchParameters fields** (every field the stock browser can set; the overhaul exposes all of them)

| Field | Type | Notes | Overhaul control |
| --- | --- | --- | --- |
| `RecordType` | string | Always `"world"`. | — |
| `Count`, `Offset` | int | Page size 40; `HasMoreResults` ends paging. | Status bar paging |
| `SortBy` | `SearchSortParameter` | `CreationDate`, `LastUpdateDate`, `FirstPublishTime`, `TotalVisits`, `Name`, `Random`. | Sort menu |
| `SortDirection` | `SearchSortDirection` | `Ascending`, `Descending`. | Direction toggle |
| `RequiredTags`, `OptionalTags`, `ExcludedTags` | `List<string>` | Sorted and normalized by the class itself. | Chips, recommended tags, search syntax |
| `OnlyFeatured` | bool | Staff-featured worlds. | Featured lens; Filters → Featured only |
| `MinDate`, `MaxDate` | `DateTime?` | Which date field applies is unverified (V7, server-side). Label the control "Published" until V7 says otherwise. | Filters → Published |
| `ByOwner`, `OwnerType`, `SubmittedTo` | string, enum, string | The stock browser sets `SubmittedTo = Engine.Cloud.Platform.GroupId` (`"G-Resonite"`) for public searches and leaves it unset for own-world searches; do the same. | `author:` syntax, My/Group Worlds |
| `Private` | bool | True only when searching the user's own worlds, and then a `FirstPublishTime` sort becomes `CreationDate`. | My Worlds |
| `ExtraSignatures` | `List<string>` | Not used by the stock browser. Do not set. | — |

The stock browser sends every search term as a tag (`SearchQueryParser.Parse`), then filters names and descriptions client-side. A record is featured when any of its `Submissions` has `Featured` set.

**`author:` search (F32).** `ByOwner` takes an owner id, not a name. `author:name` therefore resolves a user id first through the engine's own user lookup (VERIFY V30: the cached user lookup the contacts UI uses; never an own HTTP call), then searches `ByOwner`. If resolution fails, the term falls back to a client-side filter on the author name of loaded results, and the chip says so.

**Session fields: privacy footgun.** `SessionInfo.ToString()` includes the session URLs, so never log a `SessionInfo`. `GetSessionURLs()` throws when `SessionURLs` is null; check `HasEnded` first.

**Session fields used.** `Name`, `Description`, `Tags`, `SessionId`, `HostUserId`, `HostUsername`, `HeadlessHost`, `ThumbnailUrl`, `ActualCorrespondingWorldId`, `JoinedUsers`, `ActiveUsers`, `MaximumUsers`, `MobileFriendly`, `SessionBeginTime`, `LastUpdate`, `AccessLevel`, `HideFromListing`, `SessionUsers` (`Username`, `UserID`, `IsPresent`, `OutputDevice`), `AppVersion`, `UniverseId`, plus `HasEnded`, `IsExpired`, `SanitizedHostUsername`.

## Data model and lenses

The model mirrors the engine's own merge rule: published records are keyed by the combined record id, sessions by their world id when they have one, and entries with the same key merge.

**DiscoveryEntry** (one per world or standalone session) — unchanged from the ResoniteDiscovery build.

| Field | Source | Notes |
| --- | --- | --- |
| `Key` | `RecordId.ToString()` or `"S:" + normalized session id` | Stable across refreshes. |
| `Record` | `RecordCache.Get` or search result | Null until hydrated. |
| `Sessions` | Session cache | Only sessions that pass the visibility rule. |
| `DisplayName` | Record name, else best session name | Plain text from `StringRenderTree.GetRawString()` (V11). |
| `LiveUsers` | Sum of `ActiveUsers` over public sessions | |
| `FriendsPresent` | Sum of `CountPresentContacts` | Recomputed on session events. |
| `DeviceMix` | Present `SessionUsers` by `OutputDevice` | Shown in the detail panel. |
| `BestSession` | Busiest with free capacity, then newest | Card numbers. |
| `Thumbnail` | Open-world thumbnail, else session `ThumbnailUrl`, else record `ThumbnailURI` | Same priority as the engine. |
| `Flags` | Derived | `AlreadyOpen`, `Full`, `MobileFriendly`, `HeadlessHost`, `Featured`. |
| `Lists`, `PersonalTags` | Local | |

Worlds with a record key, and standalone sessions with a custom id (`S-U-owner:name`), can be starred, listed and tagged; random-id sessions cannot (decision 2026-11-02).

**OpenWorldEntry** (new, one per world the user has open; Current Worlds lens)

| Field | Source | Notes |
| --- | --- | --- |
| `Key` | Record key when the world has a corresponding record, else `"S:" + session id`, else `"L:" + local handle` | Joins it to `DiscoveryEntry` for stars, lists and tags. The local handle is never stored. |
| `Name` | `World.Name`, stripped | |
| `Role` | Host when `IsAuthority`, else guest | Shown as a badge. |
| `HostName` | Host user's name | |
| `AccessLevel` | `World.AccessLevel` | Badge: Public, Registered, Contacts+, Contacts, LAN, Private. |
| `Users`, `MaxUsers` | World user count and limit | |
| `IsFocused` | `WorldManager.FocusedWorld == world` | "You are here" badge. |
| `OpenedAt`, `LastFocusedAt` | Recorded by the adapter when the world appears / gains focus | Sort keys; in memory only. |
| `Thumbnail` | `GetThumbnailData(world)?.LocalThumbnail`, else the matching session or record thumbnail | |

Core sees open worlds through a new `IOpenWorldSource` (list plus change events, plain data), so the Current Worlds lens is unit-testable like the others. The adapter never exposes the `World` object to Core.

**Session visibility rule** — unchanged. A session is listed in public lenses only if `AccessLevel` is `RegisteredUsers` or `Anyone`, `HideFromListing` is false, `HasEnded` is false and `IsExpired` is false. `Contacts`/`ContactsPlus` sessions appear only in Friends, and only when `FriendsPresent` is above 0. `Private`, `LAN` and hidden sessions are never listed. The engine's own feed skips this filtering for privileged (userspace) callers, so the mod applies it itself. Incompatible sessions (`!IsCompatible()`) are dropped unless "Show other versions" is on (Q2). Universe-only sessions never reach the cache (V14). **Current Worlds is exempt:** it shows the user's own open worlds whatever their access level, because the user is already in them.

**Local history** (v1.1) — unchanged: snapshots every 60 s with the tab open and every 5 min otherwise, `{timestamp, worldKey, liveUsers}`, 14 days, one JSON file per account.

**The rail** (replaces the Live / Browse / Mine catalog)

| Section | Lenses | Notes |
| --- | --- | --- |
| **Now** | Current Worlds, Live Now, Friends | Current Worlds shows a count badge (open worlds, excluding userspace) |
| **Discover** | Browse, Featured, New, Random | Browse replaces Most Visited and the six stock category buttons |
| **Yours** | Favorites, each named list (up to five shown, then "More lists…"), "+ New list", Recent, My Worlds, Group Worlds | Was "Mine" |
| **Insights** | Trending, Rising, Hidden Gems | **Off by default** (turn on in Settings > Sidebar, under "Experimental"). Built only from this client's own history, so thin (see "Insights status and future") |
| Footer | Settings, Classic browser | Classic browser opens the hidden stock tab (F31) |

**Lens definitions**

| Lens | Source | Filter | Default sort | Stock equivalent |
| --- | --- | --- | --- | --- |
| Current Worlds | Open worlds | All worlds the user has open except userspace | Focused first, then last focused | None (mixed into results) |
| Live Now | Sessions | Visible sessions with `ActiveUsers` ≥ 1 | Live users ↓, ties by friends present | Active Sessions button |
| Friends | Sessions | `FriendsPresent` > 0, any contact-visible access level | Friends present ↓ | None |
| Browse | Search | Optional recommended-tag chips and search text | **Total visits ↓** | Everything, Published Worlds and the six category buttons (all first published ↓ except Avatars) |
| Featured | Search | `OnlyFeatured = true` | Last updated ↓ | Featured button |
| New | Search | `SortBy = FirstPublishTime`; optional minimum-visits chip, off by default | First published ↓ | Published Worlds button (first published ↓ is the stock default) |
| Random | Search | `SortBy = Random` | Server order; reshuffle button | Random button |
| Favorites | Local | Starred keys | Live first by users, then date starred ↓ | None |
| Named list | Local | Keys in the list | As Favorites | None |
| Recent | Local | Worlds opened through this tab | Last opened ↓ | None |
| My Worlds | Search | `ByOwner` = user id, `Private = true` | Last updated ↓ | My Worlds button |
| Group Worlds | Search | `ByOwner` = group id, `OwnerType` group; group picker | Last updated ↓ | Group Worlds button |
| Trending, Rising, Hidden Gems (v1.1) | History | As formulas below | Score ↓ | None |

The sort menu can change any search lens's sort for the session; the per-lens default can be changed in Settings. Search-backed sorts the source cannot honour are disabled (F14).

**Browse and recommended tags.** Browse is one lens with a **recommended-tags row** under the top bar:

1. **Categories** — six fixed chips in this order: Social, Games, Avatars, Art, Tutorials, Educational. Each sends the same tag the stock category button sends (V24, closed): `social`, `game`, `avatar`, `art`, `tutorial`, `educational`. The strings live in settings (`BrowseCategories`, label + tag), so a changed tag needs no rebuild and users can add their own.
2. **Popular here** — up to 8 more chips: the most frequent published tags across the current results page and the live session cache, excluding the categories and the user's excluded tags. Computed locally, zero requests, recomputed when the page changes.
3. **Your tags** — the user's personal tags with at least one world, in orange, filtering locally as elsewhere.

Chips follow F5 (require, exclude, clear). With no chips and no text, Browse shows the most-visited worlds, which is what Most Visited showed. Opening Browse from a category deep link (Settings or a tag on a card) arrives with that chip already required.

**Insights status and future** (decision 2026-11-08). Trending, Rising and Hidden Gems rank worlds from the local history file, which only holds what this one client saw while the game ran. Testers found that too sparse to be useful, so:

- The Insights section is **off by default** (`HiddenSections` defaults to `[Insights]`). Saved settings keep their own choice.
- Settings > Sidebar lists it in its own **Experimental** group: a horizontal bar under the Now, Discover and Yours switches, then the heading, then a note explaining that the rankings use only the player's own game history and that a shared server tracking public worlds is planned if enough people ask (strings `settings.experimental`, `settings.insightsNote`), then the Insights switches in one row under the note. The Insights tabs themselves carry no banner: whoever switched them on has read the note.
- **Planned, not started:** a small hosted service that watches public world data and publishes a continuously updating ranking, which these three tabs would read instead of (or merged with) local history. It is only built if demand is high enough. Until then the local formulas below stay as they are, and the mod makes no requests to any server of its own (hard rule 7 is unchanged).

**Formulas** (inputs from the history file; to be replaced or supplemented by the shared source above if it is ever built) — unchanged.

```text
Trending = mean(liveUsers over last 6h) * (1 + log2(1 + distinctSnapshotsWithUsers/10))
Rising   = mean(liveUsers last 6h) / max(1, mean(liveUsers prior 24h))
           shown only when mean(last 6h) >= 2
GemScore = (visitsGainedLast7d + 1) / (totalVisits + 25)
```

Readings fixed in M1 are in `decisions.md`; constants live in `FormulaSettings`.

## Feature requirements

Each requirement has one acceptance test a developer can run in game. IDs F1–F28 keep their numbers from the ResoniteDiscovery spec (code and test plans cite them); revised rows say so.

| ID | Requirement | Acceptance test | Release |
| --- | --- | --- | --- |
| F1 (rev) | The dash shows exactly one Worlds tab, and it is ResoniteDiscovery's: label "Worlds" (localized like the stock label where possible), the stock Worlds icon by default (galaxy icon optional), in the stock tab's position | Open the dash in desktop and VR: one Worlds tab, in the usual place, opens ResoniteDiscovery; restart twice: still exactly one | MVP |
| F2 | Live Now lists visible sessions merged by world | Two sessions of one world show as one card with a session count of 2 | MVP |
| F3 (rev) | Browse, Featured, New, Random, My Worlds and Group Worlds use `FindRecords` with paging | Next page loads while `HasMoreResults` is true and is disabled after | MVP |
| F4 | Search box on every lens, with term syntax: plain terms optional, `+term` required, `-term` excluded, `+tag:name`, `author:name` | `+horror -avatar` returns only worlds tagged or named horror and none matching avatar; in Favorites it searches only favorites | MVP |
| F5 | Tag chips: click once to require, twice to exclude, third time to clear | Chip states map to `RequiredTags` and `ExcludedTags` on the next query | MVP |
| F6 | Cards show thumbnail, name, author or host, users and max, friends present, mobile badge, tags (max 3), visits, age | A session with 5 of 10 users and 1 friend shows `5/10` and the friend badge | MVP |
| F7 | Join asks first in the mod's confirmation menu: one button per joinable session (best first, at most three) and Cancel; a published world with no session offers "New session" (F33); an open world shows "Focus world" and focuses with no menu | With two sessions where only the smaller has a contact, the first button joins the smaller; a full session's button is disabled with "Full" | MVP |
| F8 | Worlds already open show an Open badge on every lens, and Join focuses the existing world | Join on an open session focuses it with no new connection | MVP |
| F9 | Favorites: star toggles, stored per account | Stars survive a restart and do not appear for a different account | MVP |
| F10 | Friends lens lists sessions where contacts are present | A session with one contact present appears; one with none does not | MVP |
| F11 | Filters popover: minimum users, not full, mobile-friendly, headless or user host, has VR, has screen, show other versions, plus the search filters of F32 | Each filter changes the result count in the expected direction on a known set | MVP |
| F12 | Cache: the last result of each lens shows instantly on open, then refreshes | Reopening the tab shows cards in under 200 ms with an "updating" indicator | MVP |
| F13 (rev) | Settings: start lens, per-lens default sort, rail visibility (F28), Browse categories, show the stock Worlds tab, tab icon, join behaviour, animations, live lenses only, export and import, snapshot interval and formula constants (v1.1), notifications (v1.1) | Changes apply without restart, except the tab icon (next start, because the dash reads it once) | MVP |
| F14 (rev) | Sort menu: live users, visits, newest published, recently updated, created, friends present, name, random; **direction toggle** for search lenses; sorts a lens cannot honour are disabled | Each sort reorders a known set correctly; visits ascending puts the least-visited first | MVP |
| F15 | Local history and Trending, Rising, Hidden Gems | After a 2-hour session the lenses populate and survive a restart | v1.1 |
| F16 | Recent lens | Worlds opened through the tab (including from Current Worlds and the detail panel) appear newest first, capped at 50 | MVP |
| F17 | Saved filter presets: name, save, load, delete | A preset restores lens, search text, chips, sort and filters exactly | v1.1 |
| F18 | Detail panel: description, all tags, author (with "More by author"), host, uptime, per-session user list with device icons, device mix, list toggles, personal tags, copy link | Opens from Info; Copy link puts a valid record URL on the clipboard | MVP |
| F19 | Hide world or host, hidden word list | A hidden host's sessions disappear from all lenses and can be restored in Settings | v1.1 |
| F20 | Watchlists, shared service, platform content tags | Decided after v1.1 | v2 |
| F21 | Named lists: create, rename, reorder, delete; a world can be in many lists; lists appear under Yours | Create "Games", add 2 worlds, restart: the list and members persist | MVP |
| F22 | Personal tags: add and remove; orange chips; filter through chips and `+tag:` | Tag a world "chill"; `+tag:chill` in Favorites returns it | MVP |
| F23 | Export and import favorites, lists and tags as one versioned JSON file; import merges and never deletes | Export, clear the data folder, import: identical data | MVP |
| F24 | My Worlds and Group Worlds lenses | The user's own world appears in My Worlds; a group's world under that group | MVP |
| F25 (rev) | Failsafe: if the tab fails to attach, or live updates pause after repeated errors, the mod **restores the stock Worlds button**, logs once, and (if attached) shows a "ResoniteDiscovery paused — Classic browser" banner. Nothing the mod did survives a crash or uninstall | Force an exception during attach: the stock Worlds tab is visible and works; kill the game with the takeover active and restart without the mod: the stock tab is visible | MVP |
| F26 | Toasts when a favorite gets a live session (on) or contacts join a favorite (off), rate-limited | Starting a public session in a favorited world from a second account shows exactly one toast | v1.1 |
| F27 | New lens minimum-visits chip, off by default | At 10, worlds under 10 visits are hidden | MVP |
| F28 (rev) | Customizable rail: Settings can switch off a section (Now, Discover, Yours) or any lens in it. At least one lens outside Your lists stays on. If the current or start lens is hidden, the tab moves to the first visible lens. "Live lenses only" overrides and hides every search lens. Saved per account (`hiddenSections`, `hiddenLenses`) | Hide Discover: Browse, Featured, New, Random disappear and no search request is sent; show it again: the lenses return with their own switches | MVP |
| F29 | **Browse** lens with the recommended-tags row (categories, popular here, your tags), default sort total visits ↓ | Press Games: results are worlds with the Games tag, most visited first, in 2 clicks from opening the tab; the chip shows as required and Clear all removes it | MVP |
| F30 | **Current Worlds** lens: every world the user has open (not userspace), one card each with name, host, your role, access level, users/max, uptime, focused badge; buttons Focus world, Info, star and Lists (when saveable); Leave (End session for a world you host) is in the Info panel, asks first and uses the engine's own `Userspace.ExitWorld` path | Open three worlds: Current Worlds shows three cards, the focused one first with "You are here"; Focus world focuses each; Leave in the Info panel of a non-focused world asks, then closes it and the card disappears within one tick; no network request is made | MVP |
| F31 | **Tab takeover**: on attach, hide the stock Worlds button at runtime and move the dash off the stock screen if it is current; "Show the stock Worlds tab" in Settings (off by default) shows both; a **Classic browser** button in the rail footer and the paused banner opens the stock screen even while its button is hidden; on shutdown, failure or setting change, restore the button | With the setting off there is one Worlds tab; Classic browser opens the stock list and the dash returns to ResoniteDiscovery when the user next picks Worlds; turning the setting on shows both tabs without restart | MVP |
| F32 | **Custom-panel parity** in the Filters popover for search lenses: Featured only, Published (any time, past week, month, year, custom `MinDate`/`MaxDate`), sort direction (with F14), and `author:` search with "More by author" in the detail panel | Each stock Custom control (V24 inventory) has a row in the parity table that passes; "Published: past week" on Browse sends `MinDate` = now − 7 d | MVP |
| F33 | **New session options**: "New session" in the join menu offers access level (Anyone, Registered users, Contacts+, Contacts, LAN, Private; default as the engine chooses) and an Advanced row (unsafe mode, force ports) matching `CustomWorldStart` | Start a new session at Contacts: the world opens and its access level is Contacts; with no choice made, the access level matches what the stock "Open" gives | MVP |
| F34 | **Settings migration**: an account's ResoniteDiscovery `settings.json` from before the overhaul loads cleanly; `MostVisited` maps to Browse, `Live`/`Browse`/`Mine` sections map to Now/Discover/Yours; favorites, lists, tags and Recent are untouched | Copy an M4b `settings.json` with Most Visited hidden and Mine off into the data folder: Browse is hidden, Yours is off, nothing else changes | MVP |

**Search term syntax.** The mod uses its own parser (Core is engine-free) with the stock meaning: plain terms optional, `+` required, `-` excluded. Plain terms match name, description, tags and author case-insensitively. For record search, tag terms go to the server as tag lists and the rest filter the returned page; `author:` follows the rule in Data sources. Search applies to the current lens only: search lenses send it with the query, local and live lenses filter in memory. Personal tags never go to the server. When a search on a non-search lens finds nothing, the empty state offers "Search all worlds" (opens Browse with the same text).

**Out of scope for acceptance.** Ranking quality is judged by the manual review in the testing section.

## UI and interaction design

The tab must look like part of the dash. It reuses the stock screen grid (64 by 36 cells), the same region map as the stock Worlds screen (rail on the left, list on the right), and the engine's palette constants. Users who know the stock tab find the rail and list where they expect them.

**Layout regions** (cell units on the 64 by 36 grid)

| Region | Cells (X, Y, W, H) | Contents |
| --- | --- | --- |
| Rail | 0, 0, 12, 36 | Sections Now, Discover, Yours (and Insights, off by default) as in "The rail"; footer with Settings and Classic browser. Same slot as the stock filter rail |
| Top bar | 12, 0, 52, 3 | Search box (prompt "Search worlds or tags…"), Sort button with direction toggle, Filters button with a count, grid/list toggle, refresh |
| Chip bar | 12, 3, 52, 2 | Active tag chips and Clear all. **On Browse** this row is the recommended-tags row (categories, popular here, your tags) and grows to 3 cells, the results shrink by 1 |
| Results | 12, 5, 52, 28 | Card grid, 4 columns, or list view |
| Status bar | 12, 33, 52, 3 | Page number and "updated" age, Previous and Next. Same place as the stock paging facet |

**Current Worlds layout.** Same regions; the chip bar is replaced by a one-line summary ("3 worlds open · you are in Kitchen"). Cards use the normal card with: a "You are here" pill instead of the Open pill on the focused world, a role badge (Host/Guest), an access-level badge, and Focus world in place of Join (Leave is in the Info panel). List view is the default for this lens (it is a short list) and is remembered separately.

**Palette.** Purple and black (author's request, Nov 2, 2026), all from `RadiantUI_Constants` by name in `Ui/Theme.cs`. Hex values are for review only.

| Use | Constant | Hex |
| --- | --- | --- |
| Dash tab (label tint, selected colour) | `MidLight.PURPLE` | #824aab |
| Screen background | `colorX.Black` at 0.965 alpha | #000000 |
| Rail and card surface | `Dark.PURPLE` | #241e35 |
| Buttons | `Sub.PURPLE` | #492f64 |
| Selected lens | `MidLight.PURPLE` | #824aab |
| Text | `TEXT_COLOR` (`Neutrals.LIGHT`) | #e1e1e0 |
| Secondary text | `Neutrals.MIDLIGHT` | #86888b |
| Section headings and status line | `HEADING_COLOR` (`Hero.PURPLE`) | #ba64f2 |
| Friends present, "You are here" | `Hero.GREEN` | #59eb5c |
| VR users | `Hero.CYAN` | #61d1fa |
| Full, error, Leave | `Hero.RED` | #ff7676 |
| Featured star | `Hero.YELLOW` | #f8f770 |
| Personal tag chips | `Hero.ORANGE` | #e69e50 |
| Category chips | `Sub.PURPLE` with `Hero.PURPLE` outline | — |

**Card anatomy** (grid view) — unchanged: 16:9 thumbnail with users/max bottom left, star top right, Lists bottom right; one-line title; host or author and age; badge row (friends, mobile, Full, Open); up to 3 tags (personal first, orange; click adds a chip); footer with visits. Worlds with several sessions show the busiest session's numbers and `+N sessions`.

**List view** — one row per entry: thumbnail, title and subtitle, badges inline, Info, Lists, star, action. Saved as `listView` (and `currentWorldsListView`).

**Interaction rules**

- Every control works with laser, mouse or touch. No hover-only affordances.
- Minimum hit target: 2 grid cells on the short side.
- The grid uses the engine's scroll rect. Pages of at most 40 pooled `PersistentSelf = false` card slots (UIX has no virtualization hook, V12).
- Thumbnails load lazily when a card is first visible and unload when the page changes (V10: `StaticTexture2D`, 360° projection).
- Info opens the detail panel over the results; Join and Focus world stay one click away on the card.
- Desktop keys: Ctrl+F focuses search; Escape clears search, then closes the panel.
- Colour is never the only signal; every coloured badge has text or an icon.
- Lens switches animate (curtain 120 ms out, 280 ms in with a 28-unit rise; `AnimateTransitions`). Tweens only on the mod's own slots. While the covered results wait for the new page and its cards (more than 300 ms), the cleared area shows "Loading…" with three pulsing dots (`empty.loading`); it goes when the reveal starts, and a quick switch shows none.
- Leave always asks ("Leave {world}? Unsaved changes may be lost." Leave / Cancel), and is disabled for the userspace world (never listed anyway).

**States**

| State | Behaviour |
| --- | --- |
| First load, no cache | Skeleton cards, search disabled up to 1 s |
| Cached | Cards with a thin "updating" bar |
| Empty result | Message naming the active filters, "Clear filters", and on local or live lenses "Search all worlds" |
| Search failed | Banner with the result text and Retry; cards stay |
| Signed out or cloud unreachable | Banner; live lenses and Current Worlds keep working |
| Paused (F25) | Banner "ResoniteDiscovery paused" with Classic browser; stock tab button restored |
| Thumbnail missing | World-icon placeholder |

**Copy.** All strings in one table keyed by id. Labels: 2 words where possible. The tab label is "Worlds"; the mod name appears only in Settings, banners and the log.

## Integration

Members marked ✓ were confirmed in the 2026.10.29.1239 decompile (ILSpy-MCP) or a live probe; evidence is in `verification.md`. Members marked VERIFY are new for the overhaul and gate the milestone that needs them.

**1. Attaching the screen without patching** ✓ — unchanged mechanism. The mod waits for the userspace world (re-checking `IsUserspace()` on the world's next updates, V1 correction), for the dash workspace to finish loading, and for the icon import, then:

```csharp
var dash = userspace.GetGloballyRegisteredComponent<UserspaceRadiantDash>().Dash;
_screen = dash.AttachScreen("ResoniteDiscovery", RadiantUI_Constants.MidLight.PURPLE, icon);   // label set to "Worlds" below
_screen.Slot.PersistentSelf = false;   // same tick: never saved into Workspaces/Private/RadiantDash/Screens (V15, V22a)
_screen.Slot.OrderOffset = stockWorlds.Slot.OrderOffset + 1;   // our own slot only: sits right after the (hidden) stock tab
_controller = new DiscoverController(Engine, _screen);
_takeover = new TabTakeover(dash, stockWorlds, _screen, settings);   // §2
```

Use the non-generic `AttachScreen`; all logic lives in a plain C# controller. Find the stock Worlds screen with `dash.GetScreen<GridContainerScreen>(g => g.HasPreset(typeof(WorldsScreenInitializer)))` ✓, never by label (it is localized). The screen is re-attached if destroyed (workspace reload on login or logout).

**Label.** The label is "Worlds". To match the stock label in every UI language, drive it from the same locale key (`Dash.Screens.Worlds`) the way the stock screen does (VERIFY V31: `DriveLocalized` on our own `Label` field; the driver component lives on our non-persistent slot, so nothing is saved). Fallback: the string table's "Worlds".

**Icon.** Default: the stock Worlds icon (`OfficialAssets.Graphics.Icons.Dash.Worlds`) ✓, so the replacement looks like the tab users know. Setting "Tab icon: Galaxy" uses the embedded galaxy icon (imported with `LocalDB.ImportLocalAssetAsync`) ✓. `RadiantDashButton.Setup` reads the icon once ✓, so this applies on the next start.

**What `AttachScreen` builds (V2)** ✓, **dash persistence (V15, V22)** ✓ — unchanged; see `verification.md`. Key facts: the screens container is a cloud-saved `Workspace` (`Private/RadiantDash/Screens`, autosave 60 s and on exit); non-persistent slots are skipped by `Slot.Save`; any field written on a persistent stock screen **is** saved by the next save. Hence §2 never writes a stock screen field.

**2. Taking over the Worlds tab (decided Nov 3, 2026: Option B; supersedes "sit beside").**

The analysis from M0 stands: `ScreenEnabled = false` only greys the button (V3) and is saved to the cloud workspace (V22b), so it is never used. The overhaul uses **Option B**, which was the candidate kept for exactly this case:

| Step | What `TabTakeover` does | Why it is safe |
| --- | --- | --- |
| Hide | Find the button whose `Screen` is the stock Worlds screen under `dash.Slot`, and set its slot `ActiveSelf = false` | The button is built at runtime by `RadiantDash.UpdateScreens` under `Render/Buttons/Image/Horizontal Layout`, a different subtree from the saved `ScreensRoot/Screens` workspace (V29, probed live: the slot reads `persistent: true` but sits outside the workspace root, so a workspace save never writes it) |
| Keep hidden | Each tick (cheap check), if the stock button slot exists and is active while takeover is on, hide it again | `RadiantDash.UpdateScreens` may rebuild buttons when screens change (another mod adds one, our re-attach) |
| Switch away | If `dash.CurrentScreen` is the stock Worlds screen when takeover starts, or becomes it while takeover is on and the user did not press Classic browser, set `dash.CurrentScreen.Target` to ours | This is the same single write a tab-button press makes (`RadiantDashButton.Pressed`), so it is no different from a click |
| Classic browser | Set `CurrentScreen` to the stock screen (button stays hidden); mark "classic mode" until the user picks another tab | Gives full stock access for anything not ported |
| Restore | Set the stock button slot `ActiveSelf = true` | On `Stop()`, attach failure after retries, controller pause (5 failed ticks), takeover setting off, or our screen removed |

Order of operations: the stock button is hidden **only after** our screen is attached and its UI built, so a build failure never leaves the user without a Worlds tab. Because hiding is runtime-only, a crash or uninstall leaves the stock tab exactly as the engine builds it on the next launch (F25 test).

V32 (closed): an inactive slot drops out of the dash's button `HorizontalLayout`. `RectTransform.PrepareCompute` unregisters a rect from its parent when its slot is not active, and layout controllers only walk the registered rect children, so the strip closes the gap. The buttons strip is a `HorizontalLayout` with `ForceExpandWidth` off, built in `RadiantDash.OnAttach`. Still confirm by eye in the M6 test. `UpdateScreens` re-syncs only each button's `OrderOffset`, so `ActiveSelf` survives it; a button the dash rebuilds (screen removed and re-added) comes back active, which is what the keep-hidden check is for.

**Options considered** (B chosen)

| Option | What the mod does | One tab? | Persistence risk | Verdict |
| --- | --- | --- | --- | --- |
| A. Sit beside | Nothing to the stock tab | No | None | Was the ResoniteDiscovery choice; fails the overhaul goal |
| B. Hide the stock button at runtime, replacement screen | As above | Yes (V32) | None (V29) | **Chosen** |
| C. `ScreenEnabled = false` | Grey out the stock tab | No, greyed | Saved to the cloud workspace | Rejected (V3, V22b) |
| D. Rebuild the stock screen's facets | Replace `WorldFilterFacetPreset` etc. in the stock `GridContainer` | Yes | Facets are saved in the workspace; a removed mod leaves a broken tab | Rejected |
| E. Harmony-patch the stock list (`LegacyWorldListManager`, `WorldFilterFacetPreset`) | Change the stock tab in place | Yes | None | Rejected: deep patches on legacy internals break on updates, conflict with other mods that already patch `LegacyWorldDetail` (V23), and would bring no ResoniteDiscovery features without a rebuild anyway |

**Dash-mod interaction (V23, partial).** Harmony patches exist on this install on `RadiantDash.UpdateScreens`, `RadiantDash.get_AutoAdaptWidth`, `get_BASE_RESOLUTION`, `UserspaceScreensManager.SetupDefaults`/`AddHelpScreen`, `UserspaceRadiantDash.set_Open`, `LegacyWorldDetail.OnOpen`/`CustomWorldStart`. Owners are not identified. Mods that patch the stock world detail stop affecting the default path once the stock tab is hidden; they still apply in Classic browser. A mod that patches `UpdateScreens` could re-show the stock button; the keep-hidden check handles that. In-game test: the takeover works with the installed dash mods (ResoTabbed, ConsistentDashboard, NoHeadMenuDash, SessionTabOverhaul, SessionTweaks).

**3. Threading** ✓ — unchanged. Session handlers only enqueue; the controller drains ≤ 50 changes per tick; UI mutations after any `await` go through `world.RunSynchronously(...)`; no `.Result` or `.Wait()`. `WorldAdded`/`WorldRemoved` may fire off the userspace world's update; the open-world adapter only enqueues too, and reads world fields on the update thread.

**4. Joining, opening, focusing and leaving.** All joins and opens go through `Userspace.OpenWorld` ✓ (never `JoinSession`, which skips the mutual-block check). Code shapes are unchanged from the ResoniteDiscovery build (`JoinService`). New for the overhaul:

- **New session with options (F33).** Build the same `WorldStartSettings` as the stock "Open" (record URI from `GenerateRecordUri`, `GetExisting = false`) and set `DefaultAccessLevel`, `UnsafeMode` and `ForcePorts` only when the user chose them, exactly as `CustomWorldStart` does (V4). VERIFY V28: the property names and types on `WorldStartSettings`, and that leaving `DefaultAccessLevel` unset gives the stock default (V5 says it does: `Anyone` for a public record, `Private` when the user's status defaults to private, `LAN` under ForceLANOnly).
- **Focus world (F30).** `WorldManager.FocusWorld(world)` ✓ (stock already-open path, V4).
- **Leave (F30).** Use the engine's own leave path (VERIFY V27); never destroy a world directly unless the decompile shows that is what the engine's own leave does. Ask first. Disabled when the world is the only non-userspace world and the engine would refuse (follow the engine's rule; VERIFY with V27).

Before enabling Join, check `Userspace.GetExistingWorld(settings)` to show Open; disable when full. Wrap every call in try/catch, log through `UniLog`, toast on failure.

**5. Persistence** ✓ — unchanged location: `%APPDATA%\ResoniteDiscovery\<userId>\`.

| File | Contents | Write policy |
| --- | --- | --- |
| `settings.json` | Settings (incl. takeover, icon, per-lens sorts, Browse categories, rail visibility, view modes) with a `version` field for F34 | On change |
| `favorites.json`, `lists.json`, `tags.json` | As before | On change |
| `recent.json` | Last 50 opened keys with time | On open |
| `history.json`, `presets.json`, `hidden.json` | v1.1 | As before |
| `cache/` | Last result per lens | Best effort |

Account files load once `Cloud.Session.CurrentUserID` is known and reload on account change; atomic writes; 5 s debounce; corrupt files renamed `.bad`. Open-world data (Current Worlds) is never written to disk. Export and import use the fixed `exports\` folder (V20).

**6. Loader shim** ✓ — unchanged: `ResoniteDiscoveryMod : ResoniteMod` in `Loader/ResoniteModLoaderEntry.cs` behind `#if RESONITE_RML`; `OnEngineInit` registers `Engine.OnShutdown += DiscoverMod.Stop` (which restores the stock tab button) and defers `Start` with `Engine.RunPostInit`.

## Performance, networking and safety

**Networking budget.** No own HTTP clients; everything through `Engine.Cloud`.

| Activity | Budget |
| --- | --- |
| Tab closed | Zero requests. Only local snapshots run (v1.1). |
| Current Worlds, Live Now, Friends | Zero extra requests. They read the world manager and the engine's session cache. |
| Record search | Page size 40; one search at a time (a new one cancels the previous); typing debounced 400 ms. |
| Browse "popular here" chips | Zero requests: computed from loaded results and the session cache. |
| `author:` resolution | At most one lookup per distinct name (3+ characters, 20 per session), through the engine's cached user lookup (V30). |
| Search cache | Keyed by `SearchParameters`; 5 min for page 1, 15 min for later pages. |
| Auto-refresh | Visible search lens at most every 60 s (setting floor 30 s). |
| Manual refresh | 15 s cooldown (setting floor 5 s); also `Sessions.ForceFetch()`. |
| Record hydration | Through `RecordCache` (32 ids per call); only visible cards; at most 100 outstanding. |
| Thumbnails | Through the engine's asset system. |

**Performance targets** — unchanged: first cards from cache < 200 ms; < 2 ms average and < 6 ms worst per frame with the tab open; < 0.1 ms closed (the takeover's keep-hidden check included); ≤ 100 card slots (rows per page times cards per row); ≤ 60 thumbnails on screen; ≤ 50 session events per tick.

**Error handling**

| Failure | Behaviour |
| --- | --- |
| `FindRecords` not OK or timeout | Keep cards, banner, back off 5 s, 15 s, 60 s |
| Null `Tags`, `SessionUsers`, `ThumbnailUrl` | Treat as empty |
| Record missing for a session's world | Card from session data |
| `OpenWorld` throws or returns null | Toast, log, re-enable the button |
| Mutual block with the host | Engine's own message; no retry |
| Leave fails | Toast, log; the card stays |
| Stock Worlds screen not found | No takeover (nothing to hide); ResoniteDiscovery attaches as an extra tab and logs once |
| Data file unreadable | Rename `.bad`, defaults, notify once |
| Exception in the controller tick | Catch at the tick boundary; after 5 in a row pause live updates, **restore the stock tab button**, show the paused banner |

**Safety and policy rules** (hard requirements)

1. The visibility rule is applied to every session before any lens. Current Worlds lists only the user's own open worlds.
2. Joins and opens use only `Userspace.OpenWorld`; focus uses the engine's focus call; leave uses the engine's own leave path.
3. Never display, log or store `BroadcastKey`, `HostMachineId`, `HostUserSessionId` or raw session URLs.
4. History stores world keys, counts and timestamps only.
5. The mod adds no data-model types and writes nothing to worlds.
6. **Stock tab:** no Harmony patches, no `ScreenEnabled` writes, no edits to the stock screen or its facets. The only writes are the runtime `ActiveSelf` of the stock Worlds button slot and the dash's current screen, both reverted on shutdown and failure. The ResoniteDiscovery screen slot is `PersistentSelf = false`.
7. The record search endpoint is work in progress, so parsing is tolerant and a failed search never blocks the live lenses.
8. A user-defined hidden word list (v1.1) filters names and tags. Defaults to empty.
9. "Live lenses only" disables all record search and hides every search lens (F28).
10. The mod does not bypass moderation, asset protection or access levels ([mod and plugin policy](https://policies.resonite.com/ModAndPlugin)).

## Testing, verification and open questions

**Testability requirement.** Merge, visibility, lens, rail, search, Browse-chip and Current Worlds ordering logic must not reference the engine. Core reads `ISessionSource`, `IRecordSource` and `IOpenWorldSource`; thin adapters wrap `Engine.Cloud.Sessions`, `Engine.Cloud.Records` and `Engine.WorldManager`. `CoreIsolationTests` enforces it.

**Verification tasks for the overhaul** (rows V1–V23 are in `verification.md`; V6, V7, V8 and V23 are still open)

| ID | Question | Method | Needed by |
| --- | --- | --- | --- |
| V24 | Stock Worlds tab inventory: every rail button, every control in the Custom panel | D, P | **Closed Nov 3, 2026** (see the audit result and `verification.md`) |
| V25 | How to enumerate open worlds and read name, session id, access level, user count, max users, host, authority, corresponding record, focus; is there a `WorldRemoved` event; which thread raises `WorldAdded`/`WorldRemoved` | D (`WorldManager`, `World`), P | F30 |
| V26 | `Userspace.GetThumbnailData(world)` shape and `LocalThumbnail` | D | F30 thumbnail |
| V27 | The engine's own "leave world" path (session control dialog / world switcher) and its rules (last world, host, unsaved changes) | D | F30 Leave |
| V28 | `WorldStartSettings` `DefaultAccessLevel`, `UnsafeMode`, `ForcePorts` names and types; `NewWorldDialog` choices | D | F33 |
| V29 | Stock button slot, its parent chain and persistence | D, P | **Closed Nov 3, 2026** |
| V30 | Engine's cached user-by-name lookup usable for `author:` (no own HTTP) | D | F32 `author:` |
| V31 | Can our own screen's `Label` be driven by the `Dash.Screens.Worlds` locale key with a component on our non-persistent slot | D, P | F1 label |
| V32 | Does an inactive button slot drop out of the dash button strip's layout | D | **Closed Nov 3, 2026** (yes; confirm by eye in the M6 test) |

Add each to `verification.md` as it is worked on; mark code that depends on an open row `// VERIFY: Vnn`.

**Automated tests** (pure logic, CI) — existing suites stay (merge, visibility, lenses, formulas, search syntax, persistence, cache, rail catalog). New:

- Rail catalog: the Now/Discover/Yours sections, Browse replacing Most Visited, Insights off by default, the at-least-one-lens guard.
- Settings migration (F34): golden M4b `settings.json` files map to the new ids.
- Browse chips: categories first and in order; "popular here" excludes categories and excluded tags, caps at 8, is deterministic for a fixed input.
- Current Worlds ordering and keys: focused first, then last focused; record key, custom session key, local key; local keys never persisted.
- Search filters (F32): "Published: past week" produces `MinDate`; direction toggle maps to `SortDirection`; `author:` falls back to a client filter when unresolved.

**In-game test plan**

| Scenario | Expected |
| --- | --- |
| Fresh start with the mod | One Worlds tab in the stock position, opening ResoniteDiscovery |
| Kill the game with the takeover active, restart without the mod | Stock Worlds tab present and working |
| Classic browser | Stock list opens; picking Worlds again returns to ResoniteDiscovery |
| Parity table, row by row | Every row passes or is recorded as not ported |
| Browse → Games | Games-tagged worlds, most visited first |
| Open three worlds, Current Worlds | Three cards, focused first; Focus world and Leave work |
| Host two public sessions of one world from two accounts | One card, session count 2 |
| Contact hosts a contacts-only session | Appears in Friends and, while a contact is in it with 1 or more active users, in Live Now (and in Current Worlds for the host) |
| Hidden-from-listing session | Appears in no discovery lens |
| Block a host, then Join | Engine refusal, no join |
| Cloud offline | Live lenses and Current Worlds keep working; search shows a banner |
| Installed dash mods | Takeover holds with ResoTabbed, ConsistentDashboard, NoHeadMenuDash, SessionTabOverhaul, SessionTweaks |
| 2-hour soak with the tab open | Frame cost within targets, memory flat |
| Desktop and VR | All controls reachable by mouse and laser |

**Ranking review** (v1.1) — unchanged.

**Author decisions**

| Question | Decision |
| --- | --- |
| Loader | ResoniteModLoader (RML) via MonkeyLoader's compatibility pack |
| Replace or sit beside the stock Worlds tab | **Replace** (Nov 3, 2026): hide the stock button at runtime, Classic browser fallback (was "sit beside", Nov 2) |
| Category buttons | One Browse lens with recommended tags (Nov 3, 2026) |
| Worlds the user is in | Dedicated Current Worlds lens (Nov 3, 2026) |
| Distribution | Personal use for now |
| Name and icon | ResoniteDiscovery (mod name); the tab is labelled "Worlds"; stock Worlds icon by default, galaxy icon optional |
| Licence | MIT |

## Milestones and build setup

**Done (ResoniteDiscovery build).** M0 verification (V6, V7, V8, V23 open), M1 core, M2 live screen, M3 record lenses, M4a your stuff, M4b My/Group Worlds, detail panel, settings, customizable rail. 348 tests. These carry over as described in Summary.

**Overhaul milestones.** Each gate passes before the next starts.

| Milestone | Scope | Gate | Size |
| --- | --- | --- | --- |
| M5 Stock audit | **Done Nov 3, 2026.** V24, V29, V32 closed; parity table filled from the decompile; the Educational tag fixed | Parity table complete; V24, V29, V32 closed | Small |
| M6 Takeover | `TabTakeover` (hide, keep hidden, switch away, Classic browser, restore), label "Worlds" (V31), icon setting, failsafe (F25 rev), settings toggle | F1 (rev), F25 (rev), F31 pass, including the crash-and-uninstall test | Medium |
| M7 Rail and Browse | New rail catalog (Now/Discover/Yours), Browse with recommended tags, Most Visited folded in, per-lens default sorts, direction toggle, settings migration | F3 (rev), F14 (rev), F28 (rev), F29, F34 pass | Medium |
| M8 Current Worlds | `IOpenWorldSource`, adapter (V25, V26), lens, cards, Focus world, Leave (V27) | F30, F16 pass | Medium |
| M9 Parity | Filters popover search rows (F32), `author:` (V30), new-session options (F33, V28), detail panel "More by author" | Every parity row passes; F13 (rev), F18 pass; in-game test plan complete | Medium |
| M10 v1.1 | History, Trending, Rising, Hidden Gems, presets, notifications, hide lists, word list | F15, F17, F19, F26 pass; ranking review done | Large |

**Project layout** — unchanged.

| Project | Role | Engine reference |
| --- | --- | --- |
| `Discover.Core` | Entry model, open-world model, lenses, rail catalog, search syntax, formulas, persistence, interfaces | None |
| `Discover.Resonite` | Adapters over `Engine.Cloud` and `WorldManager`, `UIBuilder` screen, join and leave, `TabTakeover`, controller, loader shims | FrooxEngine, SkyFrost.Base |
| `Discover.Tests` | Unit tests against `Discover.Core` with fakes | None |

**SDK setup** — unchanged. Remora.Resonite.Sdk pinned in `global.json`; `ResoniteUsePublicizedAssemblies=false`; `ResonitePath` set to the E: install in `Directory.Build.props`; explicit `ResoniteReference` items (FrooxEngine, FrooxEngine.Store, Elements.Core, Elements.Assets, Renderite.Shared, SkyFrost.Base, SkyFrost.Base.Models); Core's sources compiled into the one mod DLL; `ResoniteInstallOnBuild=false` by default (opt in with `-p:ResoniteInstallOnBuild=true`). Output: a plain DLL in `bin/<Configuration>/mod/client/rml_mods/`.

**Compatibility policy.** Pin the SDK and Resonite version. On each Resonite update, rebuild, run the tests, and smoke-test in game: the takeover (one Worlds tab, Classic browser), one search, one join, Current Worlds. Re-run the V24 inventory if the stock tab changed, since the parity table depends on it.

## Sources

**Decompiled from Resonite 2026.10.29.1239 (author's install, ILSpy and ResoniteDumper)**

- FrooxEngine: `Userspace`, `World`, `WorldStartSettings`, `IWorldLink`, `SessionThumbnailData`, `WorldsDataFeed`, `MergedWorldData`, `RadiantDash`, `RadiantDashScreen`, `RadiantDashButton`, `UserspaceRadiantDash`, `UserspaceScreensManager`, `GridContainerScreen`, `GridContainerPreset`, `WorldsScreenInitializer`, `LegacyWorldListManager`, `LegacyWorldThumbnailItem`, `LegacyWorldDetail`, `RadiantUI_Constants`, `Workspace`, `Slot`.
- SkyFrost.Base: `SkyFrostInterface`, `SessionsManager`, `RecordsManager`, `RecordCache`, `BatchQuery`, `ContactManager`, `GroupsManager`, `SessionInfo`, `SessionUser`, `SearchQueryParser`.
- SkyFrost.Base.Models: `SearchParameters`, `SearchResults`, `SearchSortParameter`, `SearchSortDirection`, `SessionAccessLevel`, `OutputDevice`.
- To read for the overhaul: `WorldFilterFacetPreset`, `WorldManager`, `NewWorldDialog`, `HorizontalLayout`, `LayoutController`, the session control dialog's leave path.

**Web pages**

- [Resonite Wiki: World browser](https://wiki.resonite.com/World_browser)
- [Resonite Wiki: API](https://wiki.resonite.com/API)
- [Resonite mod and plugin policy](https://policies.resonite.com/ModAndPlugin)
- [Remora.Resonite.Sdk on NuGet](https://www.nuget.org/packages/Remora.Resonite.Sdk)
- [Resonite-Issues #7332: trending worlds tab](https://github.com/Yellow-Dog-Man/Resonite-Issues/issues/7332)
- [Resonite-Issues #7321: content warning tags](https://github.com/Yellow-Dog-Man/Resonite-Issues/issues/7321)
- [WorldsTabOverhaul](https://github.com/MissMolly22/WorldsTabOverhaul)
- [VRChat Wiki: Worlds](https://wiki.vrchat.com/wiki/Worlds)
- [VRChat community API reference: list active worlds](https://vrchat.community/reference/get-active-worlds)
- [VRCmap](https://vrcmap.com/)
- [VRChat Worlds Search](https://search.vrcwwt.com/worlds/)
- [Roblox DevForum: enhanced Discover page](https://devforum.roblox.com/t/testing-an-enhanced-discover-page-top-charts-and-new-sorts/2954676)
- [Roblox algorithm guide (third party)](https://bloxg.com/guides/roblox-algorithm)

Third-party pages describing VRChat and Roblox ranking internals are community or vendor write-ups, used only for feature ideas.
