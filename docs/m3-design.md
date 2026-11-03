# M3 screen design

The visual design was made as a canvas (four boards) on 2026-11-03: https://claude.ai/artifact/TcPphpLC1ckYrmaXqx7mzE
(private to Winter). This page records what it showed and what was built, since the canvas can change.

## The four boards

1. **Browse lens** (Most Visited): rail with LIVE and BROWSE sections (MINE arrives in M4), top bar (search, sort, filters, refresh), tag chip bar, lens title and counts, card grid, status bar with paging.
2. **Menus**: the sort menu (unsupported sorts greyed with the reason) and the filters popover (minimum people, has room, mobile friendly, has VR, hosted by, show other versions).
3. **States**: first load, search failed (banner with Retry, last results kept), nothing found (names the filters; Clear filters).
4. **Cards**: live with friends, already open ("You're here" and "Go there"), full, published with no session ("New session"), other version (dimmed, disabled); a list view (not built yet).

## What is built (M3)

| Piece | Where |
| --- | --- |
| Sorting, filters, chips | `Discover.Core/Lenses/Browsing.cs` |
| Search cache, pager (stale responses dropped), backoff | `Discover.Core/Sources/SearchState.cs` |
| Card, rail and sort-menu view model; string table | `Discover.Core/Presentation/` |
| Record search over `Engine.Cloud.Records.FindRecords` | `Discover.Resonite/Adapters/EngineRecordSource.cs` |
| Join to a chosen session, new session of a published world | `Discover.Resonite/Services/JoinService.cs` |
| Screen: rail, top bar, chips, header, grid, footer, popovers, curtain | `Discover.Resonite/Ui/DiscoverUi.cs` |
| State, requests, paging, join confirmation menu | `Discover.Resonite/DiscoverController.cs` |

## Not built yet (and why)

- List view and its toggle, favorites star and "+ list" buttons, the Mine rail section, detail drawer: M4.
- Disk cache of last results (F12 beyond a session): an in-memory cache covers lens switches; disk comes with the account files in M4.
- Row recycling for more than 40 cards: paging replaces the page instead.
- Single-record hydration (`IRecordSource.GetAsync`): live-only worlds show no visits or tags until they appear in a search page.
