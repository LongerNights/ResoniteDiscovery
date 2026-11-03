# Resonite Discovery Mod — Technical Specification

Nov 2, 2026 · @Winter · revised Nov 2, 2026 after the feature interview (see `decisions.md`)

**Galaxy Sieve** is a client-side mod that adds a world-discovery screen to the Resonite dash (built against 2026.10.29.1239), merging live sessions and published worlds into ranked, searchable lenses and personal lists. It sits beside the stock Worlds tab and never patches or modifies it.

## Summary and scope

The mod answers two questions fast: "what should I join or open right now?" and "where are the worlds I saved?" It adds a Galaxy Sieve screen to the dash next to the stock Worlds tab, fed by data the engine already caches and by the same cloud record search the engine uses. It does not patch or change the stock browser, so a mod failure never affects it.

**Target.** Resonite 2026.10.29.1239 client (FrooxEngine 2026.10.29.1239, SkyFrost.Base 1.0.0.0). Not the headless client. Built with the Remora.Resonite.Sdk as a **ResoniteModLoader (RML) mod**: a plain DLL in `rml_mods/`. On the author's install, RML mods load through MonkeyLoader's RML compatibility pack. Other loaders would need only a new shim.

**Goals**

1. One screen merges live sessions and published worlds, so a world appears once with its sessions nested.
2. Lenses for the questions people actually ask: Live Now, Most Visited, New, Friends, Featured, Random, Favorites, Recent, My Worlds, Group Worlds, then Trending, Rising and Hidden Gems.
3. Personal organization: Favorites, any number of named lists, and personal tags, stored locally with export and import.
4. Search with include and exclude terms and tag chips, and filters for device mix, capacity, mobile-friendly and host type.
5. Cards that show what the stock list omits: friends present, VR and desktop mix, uptime, tags, visits and age.
6. Instant open from a local cache, with skeleton loading and incremental paging.
7. Joining always goes through the engine's own checks.

**Non-goals**

- No server component, account system or external API in v1.
- No content moderation, rating or reporting system.
- No change to other players' experience. The mod is local only.
- No patching, hiding or removal of the stock Worlds tab. Both tabs are shown (decision Q1, Option A).
- No cloud sync of lists or tags. They move between machines through export and import.
- No personalization or machine-learned recommendation in v1.

**Release plan**

| Release | Contents | Exit criterion |
| --- | --- | --- |
| MVP | Live Now, Most Visited, New, Friends, Featured, Random, Favorites, Recent, My Worlds, Group Worlds; named lists and personal tags with export and import; search, sort menu and filters; cards and detail drawer; join and open; settings | A user can find and join a populated public session in 3 clicks from the dash, and can save a world to a list and find it there after a restart |
| v1.1 | Local history, Trending, Rising, Hidden Gems, saved filter presets, favorite notifications, hide world or host, hidden word list | Trending list is stable across a 2-hour session and survives restart |
| v2 | Optional shared aggregation service, content tags if the platform adds them, watchlists | Decided after v1.1 usage |

## Platform research

VRChat and Roblox both treat discovery as several ranked views plus search, not one list. This spec borrows the patterns that work from client-visible data alone and drops the ones that need server-side signals.

| Pattern | Where seen | Adopted here as |
| --- | --- | --- |
| Curated rows: Popular, Active, Featured | [VRChat Worlds tab](https://wiki.vrchat.com/wiki/Worlds) | Lenses (Live Now, Most Visited, Featured filter) |
| Search across title, description, tag, author, sortable by relevance, last updated, popular | [VRChat Worlds tab](https://wiki.vrchat.com/wiki/Worlds) | Unified search with term syntax and sort menu |
| Tag include (any-of) and exclude lists, many sort keys | [VRChat world API](https://vrchat.community/reference/get-active-worlds) | Tag chips with include and exclude states |
| "My Worlds" group: recents and favorite lists | [VRChat Worlds tab](https://wiki.vrchat.com/wiki/Worlds) | Recent and Favorites lenses |
| Live occupancy plus daily snapshots powering weekly trending and growth ranks | [VRCmap](https://vrcmap.com/) | Local snapshot history for Trending and Rising |
| Favorites-to-visits ratio and "hidden gems" sorts | [VRChat Worlds Search](https://search.vrcwwt.com/worlds/) | Hidden Gems lens (see formula) |
| Platform and capacity filters | [VRCmap](https://vrcmap.com/) | Device-mix, capacity and mobile-friendly filters |
| Like-ratio thresholds gate visibility in top-rated sorts | [Roblox discovery guide](https://bloxg.com/guides/roblox-algorithm) | Not adopted: Resonite exposes no like or vote data |
| Non-personalized charts so every user sees the same ranking | [Roblox DevForum](https://devforum.roblox.com/t/testing-an-enhanced-discover-page-top-charts-and-new-sorts/2954676) | All v1 lenses are deterministic and explainable |
| Fastest growing, reviving, new records, saved screener presets, watchlists | [RoGold features](https://rogold.live/features) | Rising lens, saved filter presets, v2 watchlists |

**What Resonite cannot support yet.** Like ratios, per-user ratings, dwell time and unique-visitor counts are not exposed to clients. `Record.Rating` exists in the data model but whether the cloud populates it is unverified (see Open questions). Trending and Rising are therefore approximations from what one client observes while running, and the UI must label them that way.

**Community requests that match this mod.** Resonite's own issue tracker has open requests for [an engagement-based trending tab](https://github.com/Yellow-Dog-Man/Resonite-Issues/issues/7332) and [content-warning world tags](https://github.com/Yellow-Dog-Man/Resonite-Issues/issues/7321). If the platform ships either, the Galaxy Sieve screen should consume it in place of the local approximations.

**Prior art.** [WorldsTabOverhaul](https://github.com/MissMolly22/WorldsTabOverhaul) reworks the stock tab with folders, favorites and a Popular list. Review its approach and licence before implementation; this spec does not depend on it.

## Architecture

&#91;embedded content: architecture · 4 engine modules, 1 core, 1 controller\]

Data flows left to right: the engine's cloud modules feed thin adapters, the adapters feed an engine-free core, and one controller turns the core's lenses into UI and the user's clicks into joins. Only the adapters and the controller touch the engine, which keeps the core unit-testable.

1. Session events from the cloud are queued and applied per tick, never in the callback.
2. Record searches start only from controller actions and respect the networking budget.
3. All UI changes happen on the engine's update thread.
4. The core never calls the engine; it reads `ISessionSource` and `IRecordSource` and returns plain data.
5. Joins and opens leave the controller only through `Userspace.OpenWorld`.

## Data sources

Every call below was read in the decompile of 2026.10.29.1239. `Engine.Cloud` is the `SkyFrostInterface`; its modules are `Sessions`, `Records`, `Stats`, `Contacts` and `Status`.

| Need | Call | Behaviour that matters |
| --- | --- | --- |
| Live session list | `Engine.Cloud.Sessions.GetSessions(List<SessionInfo>)` | Returns cached sessions, skipping expired ones. Cache is filled by one full public fetch at startup, then incremental updates. |
| Live changes | Events `SessionAdded`, `SessionUpdated`, `SessionRemoved` (`Action<SessionInfo>`), `SessionsChanged(bool addedOrRemoved)` | Expiry sweep runs every 15 s. Session heartbeat interval is 5 s. The three per-session events fire synchronously from `UpdateSessionInfo`/`RemoveSession` **while holding `SessionsManager`'s internal lock**, on whatever thread delivered the update (the initial fetch runs in `Task.Run`). `SessionsChanged` fires from `SessionsManager.Update()`. Handlers must only enqueue (V9). |
| One session | `Sessions.TryGetInfo(string sessionId)` or `TryGetInfo(Uri)` | Case-insensitive id lookup. |
| Sessions of a world | `Sessions.GetSessionsForWorldId(RecordId, List<SessionInfo>)`, `RegisterWorldIdChanges(RecordId, Action<IReadOnlyList<SessionInfo>>, bool callImmediately)` | Matches on `SessionInfo.ActualCorrespondingWorldId`. Pair every register with `UnregisterWorldIdChanges`. |
| Force refresh | `Sessions.ForceFetch()` | Re-runs the full public fetch on the next `Update`. Use only for a manual refresh button, rate limited. |
| Published-world search | `Engine.Cloud.Records.FindRecords<SkyFrost.Base.Record>(SearchParameters, TimeSpan? timeout = null, bool throwOnError = true)` | Posts to `records/pagedSearch`. Returns `Task<CloudResult<SearchResults<R>>>` with `Records` and `HasMoreResults`. Check `IsOK`. |
| Batched record lookup | `Records.RecordCache<SkyFrost.Base.Record>().Get(RecordId)` | Memoized. Misses go through `RecordBatch<R>().Request(id)`: up to 32 ids per request, 0.25 s collection window, calls `GetRecords<R>(List<RecordId>)` (`records/list`). |
| Record URI | `Records.GenerateRecordUri(ownerId, recordId)` | Builds the platform URI to open a world. |
| Online stats | `Engine.Cloud.Stats.OnlineStats` | Refreshed automatically every 10 s. Holds `UsersByClientType`, `UsersBySessionAccessLevel`, visible and hidden session counts by access level. May be null before the first fetch. Not used: the online header was dropped. |
| Friends in session | `Engine.Cloud.Contacts.CountPresentContacts(SessionInfo)` | Counts `SessionUsers` with `IsPresent` whose `UserID` is in the contacts list. |
| Open or join | `Userspace.OpenWorld(WorldStartSettings)` returns `Task<World>` | Reuses an existing world, resolves session ids to URLs, blocks if the host and user are mutually blocked, shows the load indicator. |
| Check if already open | `Userspace.GetExistingWorld(WorldStartSettings)`, `Userspace.FocusWorld(WorldStartSettings)` | Used to show an "Open" badge and to focus instead of re-joining. |
| Add a dash screen | `world.GetGloballyRegisteredComponent<UserspaceRadiantDash>().Dash.AttachScreen(string name, colorX color, Uri icon)` | Returns a plain `RadiantDashScreen`. `UserspaceRadiantDash.FocusScreenOrAttach<T>()` also exists. |
| Group membership | `Engine.Cloud.Groups.CurrentUserMemberships` (`Membership.GroupId`, `GroupName`), `GetCurrentGroups(List<Group>)`, event `MembershipsUpdated` (V18) | Cached by the engine, zero requests. Needed for Group Worlds (`ByOwner` per group, `OwnerType` group). |
| Toasts | `NotificationPanel.ShowNotification(userId, message, thumbnail, colorX, NotificationType)` (static, V19) | Marshals itself onto the panel's world (`RunSynchronously`), so safe from any thread. Suppressed when the user is Busy; queued (max 5) when Away. With a null or non-contact `userId` the bold title line reads "N/A", so put the whole text in `message`. Use `NotificationType.ToastOnly`. |

**Two record types.** Search and batch calls return `SkyFrost.Base.Record`. `WorldStartSettings` takes `FrooxEngine.Store.Record` or a `Uri`. To avoid depending on a conversion, open a world with `new WorldStartSettings(uri, hostUserId)` where `uri` comes from `GenerateRecordUri` for records, or from the session's own URLs for sessions.

**SearchParameters fields**

| Field | Type | Notes |
| --- | --- | --- |
| `RecordType` | string | Always `"world"`. |
| `Count`, `Offset` | int | Count defaults to 100. Paging is offset-based and `HasMoreResults` ends it. |
| `SortBy` | `SearchSortParameter` | `CreationDate`, `LastUpdateDate`, `FirstPublishTime`, `TotalVisits`, `Name`, `Random`. |
| `SortDirection` | `SearchSortDirection` | `Ascending`, `Descending`. |
| `RequiredTags`, `OptionalTags`, `ExcludedTags` | `List<string>` | Sorted and normalized by the class itself. |
| `OnlyFeatured` | bool | Staff-featured worlds. |
| `MinDate`, `MaxDate` | `DateTime?` | Date window. Which date field applies is unverified (V7, server-side). |
| `ByOwner`, `OwnerType`, `SubmittedTo` | string, enum, string | Author and group filters. The stock browser always sets `SubmittedTo = Engine.Cloud.Platform.GroupId` (`"G-Resonite"`) for public searches and leaves it unset for own-world searches; do the same. |
| `Private` | bool | Stock code sets true only when searching the user's own worlds, and then swaps a `FirstPublishTime` sort for `CreationDate`. Leave false otherwise. |
| `ExtraSignatures` | `List<string>` | Not used by the stock browser (`LegacyWorldListManager.GenerateSearchParameters`). Do not set. |

The stock browser sends every search term as a tag (`SearchQueryParser.Parse` into `OptionalTags`, `RequiredTags`, `ExcludedTags`), then filters names and descriptions client-side. A record is featured when any of its `Submissions` has `Featured` set; this is how the stock card decides to show its featured marker.

**Session fields: privacy footgun.** `SessionInfo.ToString()` includes the session URLs, so never log a `SessionInfo` directly. `GetSessionURLs()` throws when `SessionURLs` is null, so check `HasEnded` first.

**Session fields used.** `Name`, `Description`, `Tags`, `SessionId`, `HostUserId`, `HostUsername`, `HeadlessHost`, `ThumbnailUrl`, `ActualCorrespondingWorldId`, `JoinedUsers`, `ActiveUsers`, `MaximumUsers`, `MobileFriendly`, `SessionBeginTime`, `LastUpdate`, `AccessLevel`, `HideFromListing`, `SessionUsers` (`Username`, `UserID`, `IsPresent`, `OutputDevice` of `Unknown`, `Screen`, `VR`, `Camera`), `AppVersion`, `UniverseId`, plus helpers `HasEnded`, `IsExpired`, `SanitizedHostUsername`.

## Data model and lenses

The model mirrors the engine's own merge rule: published records are keyed by the combined record id, sessions by their world id when they have one, and entries with the same key merge.

**DiscoveryEntry** (one per world or standalone session)

| Field | Source | Notes |
| --- | --- | --- |
| `Key` | `RecordId.ToString()` or `"S:" + normalized session id` | Stable across refreshes. |
| `Record` | `RecordCache.Get` or search result | Null until hydrated. Name, tags, visits, publish date, thumbnail. |
| `Sessions` | `Sessions.GetSessionsForWorldId` | Only sessions that pass the visibility rule below. |
| `DisplayName` | Record name, else best session name | Never render raw rich text on cards. Two engine helpers exist (V11, both in `Elements.Assets.dll`). `StringParsingHelper.SanitizeFormatTags(this string)`, which backs `SessionInfo.SanitizedHostUsername` and `SessionUser.SanitizedUsername`, does **not** strip tags: it prefixes `<noparse=N>` so the markup shows literally. `new StringRenderTree(s).GetRawString()` strips tags to plain text; `SessionInfo.IsAllowedName` uses it. The adapter stores plain text from `GetRawString()` in the snapshots (Core search and sort need clean strings), and the UI may also wrap text in `SanitizeFormatTags` before putting it in a `Text`. Needs a `ResoniteReference` to `Elements.Assets`. |
| `LiveUsers` | Sum of `ActiveUsers` over visible sessions |  |
| `FriendsPresent` | Sum of `CountPresentContacts` | Recomputed on session events. |
| `DeviceMix` | Count of present `SessionUsers` by `OutputDevice` | Shown as VR and screen counts. |
| `BestSession` | Highest `ActiveUsers` with free capacity, then newest | Target of the Join button. |
| `Thumbnail` | Session `ThumbnailUrl`, else record `ThumbnailURI` | Same priority the engine uses, minus its open-world thumbnail. |
| `Flags` | Derived | `AlreadyOpen`, `Full`, `MobileFriendly`, `HeadlessHost`, `Featured` (record has a `Submissions` entry with `Featured`, as the stock card checks). |
| `Lists` | Local | Ids of the user's named lists containing this world. |
| `PersonalTags` | Local | The user's own tags for this world. |

Only entries with a record key (a published world) can be starred, listed or tagged. Standalone sessions without a world id cannot.

**Session visibility rule.** A session is listed in public lenses only if `AccessLevel` is `RegisteredUsers` or `Anyone`, `HideFromListing` is false, `HasEnded` is false and `IsExpired` is false. Sessions at `Contacts` or `ContactsPlus` appear only in the Friends lens, and only when `FriendsPresent` is above 0. Sessions at `Private`, `LAN` or hidden are never listed, including the user's own hidden sessions. The engine's own feed skips this filtering when the caller is privileged (userspace), so the mod must apply it itself. The stock list confirms this: `LegacyWorldListManager` drops `AccessLevel <= Contacts` only when it is *not* in userspace. The stock list also drops sessions where `!SessionInfo.IsCompatible()` unless its "incompatible sessions" filter is on. Whether to do the same is open question Q2; the default is yes. For a normal (non-universe) client, `Sessions.ListingSettings` is an `EngineSessionListingSettings` whose `AcceptSession` returns true, but `SessionsManager` still drops any session whose `UniverseId` differs from the engine's (null), so universe-only sessions never reach the cache (V14, probed: `InUniverse = false`).

**Local history** (v1.1). A snapshot is taken every 60 s while the dash screen is open and every 5 min otherwise: `{timestamp, worldKey, liveUsers}` per entry with live users above 0, plus the record's `Visits` the first time it is hydrated each day. Keep 14 days, then drop. Store as one JSON file per account in the mod's data folder. History reflects only the time this client was running, and the UI says so.

**Lens definitions**

| Lens | Source | Filter | Sort |
| --- | --- | --- | --- |
| Live Now | Sessions | Visible sessions with `ActiveUsers` of 1 or more | `LiveUsers` descending, ties by `FriendsPresent` |
| Friends | Sessions | `FriendsPresent` above 0, any contact-visible access level | `FriendsPresent` descending |
| New | Search | `SortBy = FirstPublishTime`, descending; optional minimum-visits chip, off by default, applied client-side | Server order |
| Most Visited | Search | `SortBy = TotalVisits`, descending | Server order |
| Featured | Search | `OnlyFeatured = true`, `SortBy = LastUpdateDate`, descending | Server order |
| Random | Search | `SortBy = Random` | Server order; reshuffle button |
| Trending | History | Entries with 2 or more snapshots in the last 6 h | Score below |
| Rising | History | Entries whose 6 h average users exceed their prior 24 h average | Ratio below |
| Hidden Gems | Search + history | Published in the last 180 days, visits between 50 and 5,000, seen live in the last 7 days | Score below |
| Favorites | Local | User-starred world keys | Live entries first by `LiveUsers`, then date starred, newest first |
| Named list | Local | World keys in that list | Same as Favorites |
| My Worlds | Search | `ByOwner` = signed-in user id, `Private = true` as the stock UI does for own worlds | `LastUpdateDate` descending |
| Group Worlds | Search | `ByOwner` = group id, `OwnerType` group; group picker when the user is in several groups | `LastUpdateDate` descending |
| Recent | Local | Worlds opened through this screen | Last opened |

**Formulas** (all inputs come from the history file)

```text
Trending = mean(liveUsers over last 6h) * (1 + log2(1 + distinctSnapshotsWithUsers/10))
Rising   = mean(liveUsers last 6h) / max(1, mean(liveUsers prior 24h))
           shown only when mean(last 6h) >= 2
GemScore = (visitsGainedLast7d + 1) / (totalVisits + 25)
```

Readings fixed in M1 (decisions.md): the means and n are over snapshots with users inside the window; the "prior 24 h" for Rising is the 24 h directly before the 6 h recent window; visits gained is the current count minus the earliest sample in the last 7 days.

The constants are starting values. Expose them in settings under an Advanced group so they can be tuned without a rebuild (`FormulaSettings` in Core). Trending, Rising and Hidden Gems cards carry a small "based on what this client has seen" label.

## Feature requirements

Each requirement has one acceptance test a developer can run in game.

| ID | Requirement | Acceptance test | Release |
| --- | --- | --- | --- |
| F1 | Galaxy Sieve screen appears in the dash with its own icon and label, beside the unchanged stock Worlds tab | Open the dash in desktop and VR: Galaxy Sieve and Worlds are both listed and selectable; restart twice and there is still exactly one Galaxy Sieve tab | MVP |
| F2 | Live Now lens lists visible sessions merged by world | Two sessions of one world show as one card with a session count of 2 | MVP |
| F3 | New, Most Visited, Featured, Random, My Worlds and Group Worlds use `FindRecords` with paging | Scrolling past 100 results loads the next page while `HasMoreResults` is true and stops after | MVP |
| F4 | Search box filters the current lens only, with term syntax: plain terms optional, `+term` required, `-term` excluded, `+tag:name` required tag | `+horror -avatar` returns only worlds tagged or named horror and none matching avatar; searching in Favorites returns only favorites | MVP |
| F5 | Tag chips: click once to require, twice to exclude, third time to clear | Chip states map to `RequiredTags` and `ExcludedTags` on the next query | MVP |
| F6 | Cards show thumbnail, sanitized name, author or host, users and max, friends present, mobile badge, tags (max 3), visits, age. Device mix is in the drawer | A card for a session with 5 of 10 users and 1 friend shows `5/10` and the friend badge | MVP |
| F7 | Join asks for confirmation first, in the engine's own choice dialog (as the stock Worlds tab does): Cancel, then one button per joinable session, best first (most contacts present, else busiest with free capacity), at most three. A published world with no live session offers "New session", which also asks first. A world you already have open shows "Go there", which focuses it with no menu | With two sessions where only the smaller has a contact, Join enters the smaller; Join on a full session is disabled with a "Full" label | MVP |
| F8 | Already-open worlds show an Open badge and Join focuses the existing world | Clicking Join on an open session focuses it without a new connection | MVP |
| F9 | Favorites: star toggles, stored per account | Stars survive a restart and do not appear for a different account | MVP |
| F10 | Friends lens lists sessions where contacts are present | A session with one contact present appears; one with none does not | MVP |
| F11 | Filters: minimum users, capacity (not full), mobile-friendly, headless or user host, device mix (has VR, has screen) | Each filter changes the result count in the expected direction on a known session set | MVP |
| F12 | Local cache: last result of each lens is shown instantly on open, then refreshed | Reopening the screen shows cards in under 200 ms with an "updating" indicator | MVP |
| F13 | Settings: default lens (Live Now), which sections and lenses the rail shows (F28), join behaviour, show incompatible sessions, snapshot interval, history retention, formula constants, notification toggles | Changes apply without restart | MVP |
| F14 | Sort menu: live users, visits, newest, recently updated, friends present, name, random. Sorts a lens's source cannot honour are disabled | Each sort reorders a known set correctly; server-only sorts are disabled where they cannot apply | MVP |
| F15 | Local history snapshots and the Trending, Rising and Hidden Gems lenses | After a 2-hour session the lenses populate and a restart keeps them | v1.1 |
| F16 | Recent lens | Worlds opened through the screen appear newest first, capped at 50 | MVP |
| F17 | Saved filter presets: name, save, load, delete | A saved preset restores the lens, search text, chips and filters exactly | v1.1 |
| F18 | Detail drawer: description, all tags, host, uptime, per-session user list with device icons, device mix, list checkboxes, personal tag editor, copy world URL | Drawer opens on card select; the URL copy puts a valid record URL on the clipboard | MVP |
| F19 | Hide world or host, and a hidden word list over names and tags | A hidden host's sessions disappear from all lenses and can be restored from settings | v1.1 |
| F20 | Watchlists, shared aggregation service, platform content tags | Not specified here; decided after v1.1 | v2 |
| F21 | Named lists: create, rename, reorder, delete; a world can be in many lists; lists appear under Mine in the rail; a "+ list" button on each card opens a list picker | Create "Games", add 2 worlds from cards, restart: the list and its members persist | MVP |
| F22 | Personal tags: add and remove in the drawer; shown as chips in a distinct colour; filter through chips and `+tag:` | Tag a world "chill"; `+tag:chill` in Favorites returns it | MVP |
| F23 | Export and import favorites, lists and tags as one versioned JSON file; import merges by world key and never deletes | Export, clear the data folder, import: identical favorites, lists and tags | MVP |
| F24 | My Worlds and Group Worlds lenses | The user's own published world appears in My Worlds; a group's world appears under that group | MVP |
| F25 | Failsafe: if the screen fails to attach, the mod logs once and leaves the dash untouched; if live updates pause after repeated errors, the screen shows a "Discover paused" banner. The stock Worlds tab is never modified | Force an exception during attach; the dash and the Worlds tab work normally and the Galaxy Sieve slot is removed | MVP |
| F26 | Notifications: toast when a favorited world gets a visible live session (on by default) and when contacts join a favorited world (off by default); each toggled in settings and rate-limited | Starting a public session in a favorited world from a second account shows exactly one toast | v1.1 |
| F27 | New lens minimum-visits chip, off by default | With the chip at 10, worlds under 10 visits are hidden | MVP |
| F28 | Customizable rail: Settings can switch off a whole section (Live, Browse, Mine) or any single lens in it (Live Now, Friends, New, Most Visited, Featured, Random, Favorites, Your lists, Recent, My Worlds, Group Worlds). Hidden lenses leave the rail and cannot be opened; a hidden section takes all its lenses with it but remembers their own switches. At least one lens outside Your lists always stays on. If the current lens or the default lens is hidden, the screen moves to the first visible lens. "Live lenses only" (item 9 under Safety) stays and acts as an override that also hides every search lens. Choices are saved per account in `settings.json` (`hiddenSections`, `hiddenLenses`) and apply without restart | Hide Browse: New, Most Visited, Featured and Random disappear and no search request is sent. Show it again: the lenses return with their own switches as they were | MVP |

**Search term syntax.** The engine's own feed parses optional, required and excluded terms. The mod uses its own parser with the same meaning, because the engine parser's location in the assemblies is unconfirmed. Plain terms are matched case-insensitively against name, description, tags and author, as the engine does for its merged entries. For record search, terms that match a known tag syntax are sent as tag lists and the rest are filtered client-side on the returned page. Search applies to the current lens only: search-backed lenses send it with the query, local and live lenses filter in memory. `+tag:` and `-tag:` also match personal tags, which never go to the server.

**Out of scope for acceptance.** Ranking quality is judged by the manual review in the testing section, not by an automated test.

## UI and interaction design

The screen must look like part of the dash. It reuses the stock screen layout grid (64 by 36 cells) and the engine's palette constants, so it follows any future palette change.

**Layout regions** (cell units on the 64 by 36 grid)

| Region | Cells (X, Y, W, H) | Contents |
| --- | --- | --- |
| Lens rail | 0, 0, 12, 36 | Lens buttons grouped as Live (Live Now, Friends), Browse (New, Most Visited, Featured, Random), Insights (Trending, Rising, Hidden Gems), Mine (Favorites, each named list, "+ New list", Recent, My Worlds, Group Worlds). Each section and lens can be switched off in Settings (F28). Filters are not in the rail: they open from the top bar (M3 design) |
| Top bar | 12, 0, 52, 3 | Search box, Sort button (opens the sort menu), Filters button with a count (opens the filters popover), refresh button; the grid or list toggle arrives later |
| Chip bar | 12, 3, 52, 2 | Tag chips (click to cycle required, excluded, cleared; the user's own tags in orange) and Clear all. Filter settings are counted on the Filters button instead of shown as chips |
| Results | 12, 5, 52, 28 | Scrolling card grid, 4 columns |
| Status bar | 12, 33, 52, 3 | Page number and "updated" age, Previous and Next page buttons (replacing Load More: a page is shown in at most 40 pooled cards, so paging replaces the page instead of appending) |

**Palette.** A purple-and-black theme (author's request, Nov 2, 2026; it replaced the stock dark-blue and yellow-label palette). Reference `RadiantUI_Constants` by name rather than hex values; the theme lives in one class, `Ui/Theme.cs`. Values below are for the design review only. The status colours below are unchanged.

| Use | Constant | Hex |
| --- | --- | --- |
| Dash tab (label tint, selected colour) | `MidLight.PURPLE` | #824aab |
| Screen background | `colorX.Black` at 0.965 alpha | #000000 |
| Lens rail and card surface | `Dark.PURPLE` | #241e35 |
| Buttons | `Sub.PURPLE` | #492f64 |
| Selected lens | `MidLight.PURPLE` | #824aab |
| Text | `TEXT_COLOR` (`Neutrals.LIGHT`) | #e1e1e0 |
| Secondary text | `Neutrals.MIDLIGHT` | #86888b |
| Section headings and status line | `HEADING_COLOR` (`Hero.PURPLE`) | #ba64f2 |
| Friends present | `Hero.GREEN` | #59eb5c |
| VR users | `Hero.CYAN` | #61d1fa |
| Full or error | `Hero.RED` | #ff7676 |
| Featured star | `Hero.YELLOW` | #f8f770 |
| Personal tag chips | `Hero.ORANGE` | #e69e50 |

**Card anatomy** (grid view)

1. Thumbnail, 16:9, with a bottom-left overlay showing users and max (for example `12/20`) and top-right a star for Favorites plus a "+ list" button.
2. Title, one line, ellipsized, sanitized.
3. Subtitle: host or author name, then relative age ("3 d").
4. Badge row: friends present (green, only if above 0), mobile icon, Full (red), Open (purple, when already open). Device mix is shown in the drawer.
5. Tag row: up to 3 tags as small chips. Clicking a tag adds it as a required chip.
6. Footer: visits (for example `12.4k`) and, on insight lenses, the metric that ranked the card with its "seen by this client" label.

Cards for worlds with several sessions show the busiest session's numbers and a small `+N sessions` link that opens the drawer.

**List view** shows one row per entry: 64 px thumbnail, title and subtitle, then the same badges inline. It exists for fast scanning in VR with the laser pointer. The view toggle starts on grid and remembers the last choice.

**Interaction rules**

- Every control is usable with a laser pointer, mouse or touch. No hover-only affordances; tooltips are optional extras.
- Minimum hit target: 2 grid cells on the short side.
- The card grid uses the engine's scroll rect so wheel, drag and laser-drag scrolling all work.
- Only build card UI for the visible rows plus one row above and below. Recycle card slots while scrolling. Never build more than 40 card slots at once. UIX has no virtualization hook (V12): `ScrollRect` exposes only `NormalizedPosition`, `AbsolutePosition`, `MoveToTop/Bottom` and the static `ScrollRect.CreateScrollRect`. The stock list uses `UIBuilder.ScrollArea`-style `ScrollRect` plus `GridLayout` (cell 320×180, spacing 12) plus `ContentSizeFitter`, and simply pages 50 items with a 40-slot pool. So the mod either pages like the stock list (simplest) or sizes a spacer for the full row count and repositions pooled cards from `NormalizedPosition` each tick. Card slots must be `PersistentSelf = false`, as the stock pool's are.
- Thumbnails load lazily when a card first becomes visible, and unload when it is 3 rows out of view.
- Selecting a card opens the detail drawer on the right over the results. Join and Open stay one click away on the card itself.
- Keyboard in desktop mode: Ctrl+F focuses search, Escape clears the search then closes the drawer, arrow keys move card focus.
- Color is never the only signal. Every colored badge also has an icon or text.
- Lens switches animate (setting `AnimateTransitions`, on by default): a black curtain over the main pane fades in (`TransitionOut`, 120 ms), the lens swaps underneath, then the curtain fades out while the results rise `TransitionRise` (28) units into place (`TransitionIn`, 280 ms); the selected rail button eases to its colour over the same time. Opening the screen plays the fade-in. Only the curtain and the results viewport move: UIX ignores slot scale, and the grid drives each card's rect. Animations use the engine's `Tween<T>` (non-persistent, removes itself) on the mod's own slots only.

**Mine lenses and organizing (M4a).** The rail's MINE section holds Favorites, each named list (up to five shown), "+ New list" and Recent. Local lenses sort by Date added (default: live worlds first, then newest saved) or Name, and are searched and chip-filtered like any lens. A world card has a star (top right of the thumbnail; yellow when starred) and a Lists button (bottom right) that opens the **organize panel**: toggles for each list the world is in, "+ New list" (creates the list and adds the world), the user's personal tags as orange buttons (press to remove) and a field to add one. Personal tags also appear on cards, orange and first, and pressing one adds an orange chip that filters locally (personal tags never go to the server). Above a named list the header shows Rename, Move up, Move down and Delete (Delete asks first). Names are typed in a small prompt panel (a field, OK, Cancel). Only world cards have a star and Lists button; standalone sessions do not. The detail drawer (F18) and the settings screen come with M4b.

**States**

| State | Behaviour |
| --- | --- |
| First load, no cache | Skeleton cards with a shimmer, search disabled for up to 1 s |
| Cached | Cards from cache with a thin "updating" bar |
| Empty result | Message naming the active filters and a "Clear filters" button |
| Search failed | Inline banner with the HTTP result text and Retry; cached cards stay visible |
| Signed out or cloud unreachable | Banner; live lenses use whatever `GetSessions` already holds |
| Thumbnail missing | World-icon placeholder tinted from `Neutrals.MID` (the stock card uses `OfficialAssets.Skyboxes.Thumbnails.NoThumbnail`) |

**Thumbnails (V10).** The stock card puts a `StaticTexture2D` on the card slot and sets `URL` to the thumbnail URI, with `Uncompressed` and `DirectLoad` true unless the URI uses the asset DB scheme (`Engine.Cloud.Assets.DBScheme`). It defers setting the URL until the card slot is active. World thumbnails are 360° images, so the stock card draws them through a shared `Projection360Material` in perspective mode (110° FOV) on a `RawGraphic`. A plain `RawImage` would show the raw equirectangular image. Unload by clearing `URL`. Source order matches the engine: open-world local thumbnail, else `SessionInfo.ThumbnailUrl`, else `Record.ThumbnailURI`.

**Copy.** All strings go in one table keyed by id so they can be localized later. Keep labels to 2 words where possible.

## Integration

Every engine member below was confirmed in the 2026.10.29.1239 decompile (ILSpy-MCP) or a live McpLink probe; evidence is in `verification.md`. The stock-tab question (§2) is decided: sit beside.

**1. Attaching the screen without patching.** The stock dash builds its tabs in `UserspaceScreensManager.OnAttach`. The mod adds its tab later, at runtime, the same way the engine adds its tutorial screen.

```csharp
// Hook: Engine.WorldManager.WorldAdded (Action<World>), then re-check the world on its next updates (World.RunInUpdates):
// FrooxEngine.WorldExtensions.IsUserspace(this World) is public (V1) but is just `world == Userspace.UserspaceWorld`,
// and that static is NOT assigned yet when WorldAdded fires for the userspace world (found in game, 2026-11-02).
// The mod can also start before the userspace world exists, so Start() attaches at once if it is already set.
void TryAttach(World userspace)
{
    var dashHost = userspace.GetGloballyRegisteredComponent<UserspaceRadiantDash>();
    if (dashHost == null) return;                        // dash not built yet: retry next update, max 600 updates
    var dash = dashHost.Dash;
    if (_screen != null && !_screen.IsRemoved) return;   // Worker.IsRemoved (V16)
    _screen = dash.AttachScreen("Discover", RadiantUI_Constants.Sub.PURPLE, DiscoverIcon);
    _screen.Slot.PersistentSelf = false;                 // same tick as attach: keeps it out of the dash workspace save (V15/V22a)
    _controller = new DiscoverController(Engine, _screen);
    _controller.BuildUi();                               // new UIBuilder(_screen.ScreenCanvas)
}
```

Use the non-generic `AttachScreen`. A custom `RadiantDashScreen` subclass would need its own component type registered in the data model, which this design avoids. All logic lives in a plain C# `DiscoverController`.

**What `AttachScreen` builds (V2).** `RadiantDash.AttachScreen` does `World.AddSlot(name).AttachComponent<RadiantDashScreen>()`, sets `Label`, `ActiveColor` and `Icon`, reparents the slot under `Dash.ScreensContainer`, and hides it. `RadiantDashScreen.OnAwake` sets `ScreenEnabled = true` and `BaseResolution = 1920×1080`. `OnAttach` creates a child slot `Screen` (the `ScreenRoot`, inactive until shown) with a child `Canvas` holding a `Canvas` component sized to `BaseResolution` with a trigger collider. It also adds a `StaticTexture2D` driven from `Icon` and a `ModalOverlayManager` on the screen slot. The canvas starts empty, so build straight under `ScreenCanvas`. The dash resizes the canvas width to `BaseResolution.y × ScreenAspectRatio` (`SetAspectRatio`), and on desktop the width follows the window aspect, so the layout must anchor proportionally, not use fixed pixel widths. The tab button is built by `RadiantDash.UpdateScreens` on the next change pass. Buttons live under the dash's `Buttons` canvas, outside the saved workspace. `RadiantDashScreen.Show/Hide` and the virtual `OnShow/OnHide` are not overridable without a subclass, so the controller detects visibility by polling `IsShown` (= `ScreenRoot.ActiveSelf`) once per tick.

**Persistence of dash screens (V15, V22).** `UserspaceRadiantDash.OnAttach` puts a `Workspace` on `Dash.ScreensContainer`, with `WorkspacePath = "Private/RadiantDash/Screens"`, `AutosaveDelay = 60` s and a save on exit (`Userspace.RegisterExitTask`). This was confirmed live: `ReadOnly = false`. `Workspace.SaveChanges` runs `ScreensContainer.SaveObject(DependencyHandling.CollectAssets)` and uploads it as the cloud record `Workspaces/Private/RadiantDash/Screens`, but only when `_unsavedChanges` is set. That flag is set by `MarkModified`, which `ModificationNotificationHelper.NotifyModified` raises from any component inside the hierarchy (user edits, facet changes, the engine's own cleanup). `Slot.Save` skips any child slot that is not `IsPersistent`, so a `PersistentSelf = false` screen slot is never written. Another installed mod's "Mods" screen already uses exactly this pattern (`PersistentSelf = false`, probed). Field values on persistent stock screens, such as `ScreenEnabled` on Worlds, **are** saved by the next save that happens for any reason.

**Finding the stock Worlds screen.** Its `Label` is driven by `DriveLocalized("Dash.Screens.Worlds")`, so the text changes with the UI language. Find it the way the engine does: `dash.GetScreen<GridContainerScreen>(g => g.HasPreset(typeof(WorldsScreenInitializer)))`.

The icon is a texture URI. The mod ships its own galaxy icon (`src/Discover.Resonite/Assets/galaxy-icon.png`, generated by `tools/make_icon.py` in `Hero.PURPLE`/`MidLight.PURPLE`), embedded in the DLL. At startup it is written to a temp file and imported with `Engine.LocalDB.ImportLocalAssetAsync(path, LocalDB.ImportLocation.Copy)`, which returns a `local://` URI and dedupes by content hash, so repeated launches store nothing new. `RadiantDashButton.Setup` reads `Icon` only once, when the button is built, so the controller waits up to 10 s for the import before attaching and falls back to `OfficialAssets.Graphics.Icons.Dash.Worlds` if it fails.

**2. The stock tab (decided Nov 2, 2026: sit beside; Q1 resolved to Option A).** The mod does not touch the stock Worlds screen at all: no `ScreenEnabled` write, no button change. Both tabs show in the dash. There is therefore no hide setting and no restore logic; a mod failure cannot affect the stock browser, and nothing is written to the saved dash workspace except what the engine writes itself. Hiding can be revisited after M4 (option B below is the candidate). The analysis behind the decision follows. The original plan was to set `ScreenEnabled` to false on the stock Worlds screen. The decompile shows this does not meet the old F1:

- **It does not remove the button (V3).** `RadiantDashButton.UpdateColors` computes `Button.Enabled = (switching allowed || current || AlwaysSwitchable) && ScreenEnabled`, then dims the icon and label to 25 % alpha. `RadiantDash.UpdateScreens` creates a button for every screen under `ScreensContainer` whatever its `ScreenEnabled`. So the tab stays visible, greyed out and unclickable, the way Contacts looks when logged out. It also does not switch away if Worlds is the current screen.
- **It persists (V22b).** `ScreenEnabled` on the persistent Worlds screen is saved into `Workspaces/Private/RadiantDash/Screens` by the next workspace save, so after a crash or uninstall the stock tab stays disabled until something re-enables it. Our own failsafe would not run in those cases.

Options considered (A chosen):

| Option | What the mod does | Tab gone? | Persistence risk | Rule 6 |
| --- | --- | --- | --- | --- |
| A. Sit beside | Nothing to the stock tab | No, both tabs visible | None | Unchanged |
| B. Hide the button slot | Each launch, set the stock Worlds button's slot (`screen._button.Target.Slot`, under the dash `Buttons` canvas, outside the workspace) `ActiveSelf = false`; switch `CurrentScreen` away if it is Worlds; set it back on failure or shutdown | Probably; the inactive slot should drop out of the `HorizontalLayout`, needs an in-game check | None: the button hierarchy is rebuilt every launch and is not in the saved workspace | Needs rewording: it is a runtime write to a stock UI slot, though not a code patch |
| C. `ScreenEnabled = false` | As originally specified | No, greyed | Saved to the cloud record; stays disabled if the mod is removed without restoring | As written |
| D. Hide via drive | Drive `ScreenEnabled` from a non-persistent field | No, greyed | Saving writes the driven value (false) | Adds a component to a stock screen |

If hiding is revisited, B is the candidate and needs the dumper Part B layout read plus an in-game check. If attach fails after its retries, or the controller pauses after 5 consecutive tick failures, the mod just logs once and shows its "Discover paused" banner; the stock tab is always available.

**Dash-mod interaction (V23, partial).** On this install, Harmony patches exist on `RadiantDash.UpdateScreens` (the method that builds the tab buttons), `RadiantDash.get_AutoAdaptWidth`, `get_BASE_RESOLUTION`, `UserspaceScreensManager.SetupDefaults`/`AddHelpScreen`, `UserspaceRadiantDash.set_Open`, and `LegacyWorldDetail.OnOpen`. The owning mods are not identified yet. The installed dash mods (ResoTabbed, ConsistentDashboard, NoHeadMenuDash, SessionTabOverhaul, SessionTweaks) are the likely candidates, but that is unconfirmed. SessionTabOverhaul's only confirmed patch overlap is `SessionControlDialog.GenerateUi`. Galaxy Sieve only adds a screen through the public `AttachScreen`, so the test is that its tab appears and works with these mods loaded.

**3. Threading.** SignalR and `Task.Run` continuations are off the engine's update thread. Rules:

- Session event handlers only enqueue the changed `SessionInfo` into a lock-free queue.
- The controller drains the queue once per update tick (hook: a world update callback on the userspace world) and applies at most 50 changes per tick.
- Any UI mutation after an `await` goes through `world.RunSynchronously(...)`. The engine itself uses this for world state changes.
- No blocking calls (`.Result`, `.Wait()`) anywhere.

**4. Joining and opening.** All joins go through `Userspace.OpenWorld`. Never call `Userspace.JoinSession` or `WorldManager.JoinSession` directly, because they skip the mutual-block check.

```csharp
async Task JoinSessionAsync(SessionInfo info)
{
    if (info.HasEnded) return;                           // GetSessionURLs() throws on null SessionURLs
    var urls = info.GetSessionURLs();                    // SessionInfo.GetSessionURLs(): well-formed absolute URIs only
    var settings = new WorldStartSettings(urls.First(), info.HostUserId)
    {
        URIs = urls,
        SessionID = info.SessionId,
        HostUserId = info.HostUserId,                    // required for the block check
        GetExisting = true,                              // focus if already open
        AutoFocus = true,
        CreateLoadIndicator = true,
        FetchedWorldName = info.Name
    };
    await Userspace.OpenWorld(settings);
}

async Task OpenNewInstanceAsync(Record world)            // SkyFrost.Base.Record
{
    var uri = Engine.Cloud.Records.GenerateRecordUri(world.OwnerId, world.RecordId);
    var settings = new WorldStartSettings(uri) { FetchedWorldName = world.Name, GetExisting = false };
    await Userspace.OpenWorld(settings);
}
```

**How the stock UI opens records (V4, V5).** `LegacyWorldDetail.OnOpen` calls `Userspace.OpenWorld(new WorldStartSettings(record, Cloud.Platform) { GetExisting = !Expanded })` with a `FrooxEngine.Store.Record`. It leaves `Relation` at the default `Nest` and sets no `DefaultAccessLevel`. "Start new session" (`CustomWorldStart`) adds `DefaultAccessLevel`, `UnsafeMode` and `ForcePorts` from `NewWorldDialog`. That record constructor only does `SetUri(record.GetUrl(platform))`, which is `platform.GetRecordUri(ownerId, recordId)`, the same call as `RecordsManager.GenerateRecordUri`, and sets `Record` and `FetchedWorldName`. `OpenWorldInternal` accepts any URI that passes `PlatformProfile.IsValidRecordUri`. For record-scheme URIs, `LoadWorld` always re-fetches the record and assigns `startInfo.Record`, so `CorrespondingRecord` and the default access level come out the same as the stock path: `Anyone` when the record is public, `Private` when the user's status defaults to private, `LAN` under ForceLANOnly. Opening by URI therefore needs no record-type conversion. Sessions are joined the same way the stock UI does: `URIs = GetSessionURLs()`, `HostUserId` set (mutual-block check), `GetExisting = true`. The engine itself logs the URIs it opens (`"Opening world. URI's: …"`); the mod adds no logging of its own.

Before enabling the Join button, check `Userspace.GetExistingWorld(settings)` to show Open instead, and disable it when `JoinedUsers >= MaximumUsers`. Wrap every call in try and catch, log through `UniLog`, and show a toast on failure.

**5. Persistence.** One folder per account under `Environment.SpecialFolder.ApplicationData`, named `GalaxySieve\<userId>\`, so data survives game reinstalls and works under Proton.

| File | Contents | Write policy |
| --- | --- | --- |
| `settings.json` | Settings, formula constants | On change |
| `favorites.json` | `[{key, name, starredAt}]` | On change |
| `lists.json` | Named lists: id, name, order, members `{key, name, addedAt}` | On change |
| `tags.json` | Personal tags per world key | On change |
| `ui.json` | Last view (grid or list) | On change |
| `recent.json` | Last 50 opened keys with time | On open |
| `history.json` | Snapshots (v1.1) | Every 5 min and on exit |
| `presets.json` | Saved filter presets (v1.1) | On change |
| `hidden.json` | Hidden world keys and host ids (v1.1) | On change |
| `cache/` | Last result per lens and thumbnails | Best effort |

Account files load once the signed-in account is known (`Engine.Cloud.Session.CurrentUserID`, checked about once a second, never before login) and reload if the account changes; they flush on shutdown. Writes are atomic: write to a temp file, then replace. Debounce to at most one write per file every 5 s. A corrupt file is renamed to `.bad`, logged, and replaced with defaults. Store only ids, names and numbers, never session user lists.

**Export and import.** Settings has Export, which writes favorites, lists and tags to one JSON file with a format version, and Import, which merges by world key and never deletes. FrooxEngine has no OS file-picker API: there are no `*FileDialog` or picker types, and `FileBrowser` is an in-world browser that imports, not a callback picker (V20). So use a fixed `exports\` folder inside the data folder and show its path in Settings.

**6. Loader shim.** One `DiscoverMod` class holds all logic. The loader-specific entry point calls `DiscoverMod.Start(engine)` and `Stop()`. The shipping shim is `GalaxySieveMod`, a `ResoniteModLoader.ResoniteMod` subclass in `Discover.Resonite/Loader/ResoniteModLoaderEntry.cs` behind `#if RESONITE_RML`: it overrides `Name`, `Author`, `Version` and `OnEngineInit()`. `OnEngineInit` runs before `Engine.Cloud` and the userspace world are ready, so it only registers `Engine.OnShutdown += DiscoverMod.Stop` and defers `Start` with `Engine.RunPostInit`. Other loaders (MonkeyLoader native, BepisLoader) would get their own shim in the same folder, selected by the SDK's `ResoniteTargetModLoader` setting; none is built today.

## Performance, networking and safety

**Networking budget.** The mod makes no HTTP clients of its own. Every request goes through `Engine.Cloud`, which already handles authentication and retries.

| Activity | Budget |
| --- | --- |
| Screen closed | Zero requests. Only local snapshots run. |
| Live lenses | Zero extra requests. They read the engine's session cache and its pushed updates. |
| Record search | Page size 40 (was 50: a page is shown in at most 40 pooled card slots). One search at a time; starting a new one cancels the previous. Typing is debounced 400 ms. |
| Search cache | Keyed by `SearchParameters` (the class implements equality and hashing). Time to live 5 min for page 1, 15 min for later pages. |
| Auto-refresh | The visible search lens refreshes at most every 60 s while the screen is open. |
| Manual refresh | 15 s cooldown. Calls `Sessions.ForceFetch()` and re-runs the search. |
| Record hydration | Through `RecordCache`, which already batches 32 ids per call. Hydrate only entries whose card is visible or within 1 row of view. At most 100 ids outstanding. |
| Thumbnails | Loaded through the engine's asset system. No custom downloads. |

**Performance targets**

| Measure | Target |
| --- | --- |
| Screen open with cache to first cards | Under 200 ms |
| Mod work per frame with screen open | Under 2 ms average, under 6 ms worst case |
| Mod work per frame with screen closed | Under 0.1 ms |
| Card slots alive | At most 40 |
| Cached entries in memory | At most 500, least recently used evicted |
| Thumbnails resident | At most 60 |
| Session events applied per tick | At most 50, remainder carried to the next tick |

Aggregation is incremental: a session event marks only its world entry dirty, and sorting runs only for the visible lens, and only when a dirty entry could change its order.

**Error handling**

| Failure | Behaviour |
| --- | --- |
| `FindRecords` returns not OK or times out | Keep cached cards, show a retry banner, back off 5 s then 15 s then 60 s |
| Session has null `Tags`, `SessionUsers` or `ThumbnailUrl` | Treat as empty; never throw |
| Record missing for a session's world id | Show the card from session data alone |
| `OpenWorld` throws or returns null | Toast with the reason, log through `UniLog`, re-enable the button |
| Mutual block with the host | Show the engine's own message; do not retry |
| Unexpected field shape from the cloud | Skip the entry, count it, log once per minute |
| Data file unreadable | Rename to `.bad`, use defaults, notify once |
| Any exception in the controller tick | Catch at the tick boundary, log, disable the screen's live updates after 5 consecutive failures, show a "Discover paused" banner |

**Safety and policy rules** (hard requirements, tested in the verification plan)

1. The visibility rule in the data model section is applied to every session before it reaches any lens.
2. Joining and opening use only `Userspace.OpenWorld`.
3. The mod never displays, logs or stores `BroadcastKey`, `HostMachineId`, `HostUserSessionId` or raw session URLs.
4. Local history stores world keys, counts and timestamps only. No usernames and no user ids, except host ids in the user's own hidden list.
5. The mod adds no components or data-model types and writes nothing to worlds, so it cannot change session compatibility or other users' experience.
6. The mod does not bypass moderation, asset protection or access levels, as the [mod and plugin policy](https://policies.resonite.com/ModAndPlugin) requires.
7. The record search endpoint is documented as work in progress with no guarantees, so all parsing is tolerant and an unavailable search never blocks the live lenses.
8. A user-defined hidden word list (v1.1) filters on tags and names, since the platform itself has no content-warning tags. Defaults to empty.
9. Settings include a "Live lenses only" switch that disables all record search calls. It overrides the per-lens choices (F28): every search lens is hidden while it is on.

## Testing, verification and open questions

**Testability requirement.** The merge, visibility, lens and search logic must not reference the engine. It reads from two small interfaces, `ISessionSource` (list of sessions plus change events) and `IRecordSource` (search and lookup), with thin adapters over `Engine.Cloud.Sessions` and `Engine.Cloud.Records`. This lets unit tests run in a plain test project with fake data.

**Verification tasks before coding** (ILSpy or in-game probe)

| ID | Question | Why it matters |
| --- | --- | --- |
| V1 | Is `World.IsUserspace()` public, and which assembly holds it? | Finding the userspace world to attach to |
| V2 | What does a plain `RadiantDashScreen` create under `ScreenRoot` and `ScreenCanvas`, and what is `BaseResolution`? | Where `UIBuilder` content goes |
| V3 | Does setting `ScreenEnabled` false remove the dash tab button? (No; see §2.) | Hiding the stock tab, now dropped |
| V4 | How does the stock UI call `OpenWorld` for a published record (`Relation`, access level)? Not in `WorldsScreenInitializer`; look in the facet presets, for example `WorldListFacetPreset` | Open New Instance behaviour |
| V5 | Does `OpenWorld` accept a URI from `GenerateRecordUri` (`IsValidRecordUri`)? | Skipping any record type conversion |
| V6 | Is `Record.Rating` ever non-zero on live search results? Log 200 results | Whether a Top Rated lens is possible |
| V7 | Which date do `MinDate` and `MaxDate` filter on? | New and Rising windows |
| V8 | What do the search results look like when `OnlyFeatured` is set, and is `ExtraSignatures` used by the stock UI? | Featured lens and unknown field |
| V9 | Which thread raises `SessionAdded`, `SessionUpdated`, `SessionRemoved`? | Queue design |
| V10 | How does the engine load a thumbnail URI into a texture for UIX (see `SessionThumbnailData` and the `WorldListFacetPreset` card)? | Thumbnail loading code |
| V11 | Name and namespace of the format-tag sanitizer used by `SanitizedHostUsername` | Card titles |
| V12 | Which scroll and layout components `UIBuilder` offers in this version, and virtualization hooks | Card grid implementation |
| V13 | Where is `SearchQueryParser`? Reuse only if public | Optional |
| V14 | Does `Sessions.ListingSettings` filter anything for a normal client (universe)? | Missing sessions |

Answers and evidence for V1 to V23 are recorded in `verification.md`. As of Nov 2, 2026 the rows left open are V6, V7 and V8 (live search probes) and V23 (dash-mod interaction); the stock-tab question is decided (sit beside).

**Automated tests** (pure logic, run in CI)

- Merge: sessions of one world collapse to one entry; sessions without a world id stay separate; removing the last session removes the entry.
- Visibility: a table test over every `AccessLevel` value, `HideFromListing`, `HasEnded` and `IsExpired` combination.
- Lenses: fixed input sets produce the documented order, including tie-breaks.
- Formulas: golden values for Trending, Rising and GemScore, including empty history and a single snapshot.
- Search syntax: `+a -b c`, quoted phrases, tag prefix, empty string, very long input.
- Persistence: atomic write, corrupt file recovery, per-account separation.
- Cache: `SearchParameters` equality ignores tag order.

**In-game test plan**

| Scenario | Expected |
| --- | --- |
| Host two public sessions of one saved world from two accounts | One card, session count 2, busiest session joined |
| Contact hosts a contacts-only session | Appears only in Friends |
| Hidden-from-listing session | Appears in no lens |
| Block a host, then Join | Engine refusal message shown, no join |
| Cloud offline mid-session | Live lenses keep working, search shows banner |
| 2-hour soak with screen open | Frame cost within targets, memory flat |
| Restart the game | Favorites, recents and history present |
| Desktop and VR | All controls reachable by mouse and laser |
| Game update day | Mod loads or fails cleanly with one log line and no dash breakage |

**Ranking review.** Before v1.1 ships, compare Trending, Rising and Hidden Gems against 3 known busy sessions and 3 known quiet ones over one evening, and tune constants if the top 5 includes obviously empty worlds.

**Author decisions** (interview, Nov 2, 2026)

| Question | Decision |
| --- | --- |
| Loader target first | ResoniteModLoader (RML), loaded through MonkeyLoader's RML compatibility pack on the author's install; other shims later (revised Nov 2, 2026 after M2; was "MonkeyLoader") |
| Replace or sit beside the stock Worlds tab | Sit beside, no changes to the stock tab (revised Nov 2, 2026 after M0: hiding only greys the button and persists to the cloud; was "replace by default") |
| Distribution | Personal use for now |
| Mod name and icon | Galaxy Sieve; stock Worlds icon in v1 |
| Licence | MIT |

## Milestones and build setup

**Milestones.** Each gate must pass before the next milestone starts.

| Milestone | Scope | Gate | Size |
| --- | --- | --- | --- |
| M0 Verification | Close V1 to V23 and record answers in `verification.md` | No `VERIFY` line left in Integration; the §2 decision resolved (done) | Small |
| M1 Core logic | Merge, visibility rule, lenses, search syntax, formulas, persistence, interfaces and fakes | All automated tests pass; core project has no engine reference | Medium |
| M2 Live screen | Attach screen, card grid, Live Now, join and open, open badge | F1, F2, F6, F7, F8 pass in game | Medium |
| M3 Record lenses | Record search adapter, paging, cache, hydration, tag chips, filters, search box | F3, F4, F5, F11, F12, F14, F27 pass; network budget respected in a 30-minute trace | Large |
| M4 MVP release | Favorites, named lists, personal tags, export and import, Friends, Recent, My Worlds, Group Worlds, drawer, settings, state handling, attach failsafe | F9, F10, F13, F16, F18, F21 to F25 pass; in-game test plan complete | Large |
| M5 v1.1 | History, Trending, Rising, Hidden Gems, presets, notifications, hide lists and word list | F15, F17, F19, F26 pass; ranking review done | Large |

**Project layout**

| Project | Role | Engine reference |
| --- | --- | --- |
| `Discover.Core` | Entry model, lenses, search syntax, formulas, persistence, interfaces | None |
| `Discover.Resonite` | Adapters over `Engine.Cloud`, `UIBuilder` screen, join logic, controller | FrooxEngine, SkyFrost.Base |
| `Discover.Loader.<Name>` | Loader entry shim, one per supported loader | Loader package |
| `Discover.Tests` | Unit tests against `Discover.Core` with fakes | None |

**SDK setup.** The [Remora.Resonite.Sdk](https://www.nuget.org/packages/Remora.Resonite.Sdk) builds without a Resonite install, supplies publicized reference assemblies for mods, and defaults the target framework to the one the chosen Resonite version uses (net10.0 for the client at the time of writing).

```xml
<!-- version pinned in global.json (msbuild-sdks) -->
<Project Sdk="Remora.Resonite.Sdk">
  <PropertyGroup>
    <ResoniteTarget>client</ResoniteTarget>
    <ResoniteProjectType>mod</ResoniteProjectType>
    <ResoniteTargetModLoader>ResoniteModLoader</ResoniteTargetModLoader>
    <ResoniteInstallOnBuild>false</ResoniteInstallOnBuild>          <!-- opt in: -p:ResoniteInstallOnBuild=true -->
    <ResoniteUsePublicizedAssemblies>false</ResoniteUsePublicizedAssemblies>
  </PropertyGroup>
  <ItemGroup>
    <ResoniteReference Include="FrooxEngine" />
    <ResoniteReference Include="FrooxEngine.Store" />
    <ResoniteReference Include="Elements.Core" />
    <ResoniteReference Include="Elements.Assets" />
    <ResoniteReference Include="Renderite.Shared" />
    <ResoniteReference Include="SkyFrost.Base" />
    <ResoniteReference Include="SkyFrost.Base.Models" />
  </ItemGroup>
  <ItemGroup>
    <!-- Core's sources are compiled into the one mod DLL; there is no ProjectReference. -->
    <Compile Include="..\Discover.Core\**\*.cs" Exclude="..\Discover.Core\obj\**;..\Discover.Core\bin\**" LinkBase="Core" />
  </ItemGroup>
</Project>
```

`dotnet publish` produces a zip ready to unpack into the Resonite folder. SDK 2.3.4 bundles reference assemblies from Resonite 2026.8.27.1094. Mods default to the bundled *publicized* references and ignore the install, so `ResoniteUsePublicizedAssemblies` is off: the project compiles against the real install's public API when `ResonitePath` exists. The SDK's auto-detect checks only the C: Steam path, so `Directory.Build.props` sets `ResonitePath` to the E: install when present. Engine references are explicit `ResoniteReference` items; the SDK adds none by itself. For ResoniteModLoader the SDK generates no entry point and builds a plain DLL into `bin/<Configuration>/mod/client/rml_mods/` (dependencies would go to `rml_libs/`); install-on-build copies it to `<Resonite>/rml_mods/`. RML finds the one `ResoniteMod` subclass in the DLL (shim shape: B3). RML loads a single DLL per mod, which is why Core's sources are compiled into it rather than shipped as a second assembly.

**Compatibility policy.** Pin the SDK and the Resonite version in the repo. On each Resonite update, rebuild, run the automated tests and the in-game smoke test (open screen, one join, one search), then publish a new release. Keep the engine-facing code in `Discover.Resonite` small so that breakage stays contained.

## Sources

**Decompiled from Resonite 2026.10.29.1239 (author's own install, via ILSpy)**

- FrooxEngine: `Userspace`, `World`, `WorldStartSettings`, `IWorldLink`, `SessionThumbnailData`, `WorldsDataFeed`, `WorldsDataFeedData`, `MergedWorldData`, `MergedWorldDataFeedItem`, `MergedWorldDataItemInterface`, `RadiantDash`, `RadiantDashScreen`, `UserspaceRadiantDash`, `UserspaceScreensManager`, `GridContainerScreen`, `GridContainerPreset`, `WorldsScreenInitializer`, `RadiantUI_Constants`, `StandaloneFrooxEngineRunner`.
- SkyFrost.Base: `SkyFrostInterface`, `SessionsManager`, `RecordsManager`, `RecordBatchQuery`, `RecordCache`, `BatchQuery`, `StatisticsManager`, `ContactManager`, `VisitsManager`, `ISessionListingSettings`, `SessionInfo`, `SessionUser`.
- SkyFrost.Base.Models: `SearchParameters`, `SearchResults`, `SearchSortParameter`, `SearchSortDirection`, `SessionAccessLevel`, `OutputDevice`, `OnlineStats`.
- FrooxEngine.Store: `Record`.

**Web pages**

- [Resonite Wiki: API](https://wiki.resonite.com/API)
- [Resonite Wiki: World browser](https://wiki.resonite.com/World_browser)
- [Resonite Wiki: Beta 2024.6.24.1317 (Worlds Data Feed)](https://wiki.resonite.com/Beta_2024.6.24.1317)
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
- [RoGold features](https://rogold.live/features)

Third-party pages describing VRChat and Roblox ranking internals are community or vendor write-ups, not official documentation, and are used only for feature ideas.
