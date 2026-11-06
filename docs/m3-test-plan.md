# M3 in-game test plan

M3's gate (spec "Milestones"): F3, F4, F5, F11, F12, F14, F27 pass in game, and the network budget holds over a
30-minute trace. Nothing here has run yet; the Core logic (sorting, filters, chips, cache, pager, backoff, view model)
is unit tested (304 tests). Install and first-check steps are in `m2-test-plan.md`.

## First look
1. Log: `[Discover] Started`, `Controller started`, `Screen attached`; no `UI build failed` or `Tick failed`.
2. Open the tab: rail with LIVE and BROWSE, top bar, no empty dash.

## Cases

| ID | Steps | Expected |
| --- | --- | --- |
| F3 | Open **Most Visited**; press **Next** until it is disabled | Pages of up to 40 worlds; Next is hidden on the last page; Previous goes back; nothing loads while paging back inside the cache TTL |
| F4 | In Most Visited type `+club -avatar`; in Live Now type a world name | Results narrow after about 0.4 s; the search applies to the current lens only |
| F5 | Click a card's tag button once, twice, three times | Required chip (purple), excluded chip (red), gone; the next search carries the tag |
| F11 | Live Now: Filters > Has room, Mobile friendly, Has VR, At least 2 people, Hosted by Headless | Each narrows the count in the expected direction; the Filters button shows how many are on; Reset clears them |
| F12 | Open Most Visited, switch to New and back within a few minutes | Results return at once with no new request (cache), no skeleton |
| F14 | Open Sort on Live Now and on Most Visited | Live Now enables live users, friends, newest session, name, random; Visits, Newest published and Recently updated are greyed with "Not available for this lens"; on Most Visited it is the other way round; the direction buttons flip the order |
| F27 | New lens > Filters > "At least N visits" | Worlds under N visits disappear (client-side) |
| Join menu | Press **Join** on a live world | The engine's dialog: Cancel and Join (or a button per session when there are several); Join enters the session; Cancel does nothing |
| New session | Press **New session** on a published world with nobody in it | Dialog "Start a new session of ..."; Start opens it |
| Focus world | Press **Focus world** on a world you have open | Focuses it with no dialog and no second `Opening world` log line |
| Full / other version | Find a full world; turn on "Show other versions" | Full shows a disabled button; other-version worlds are dimmed with a disabled button |
| Errors | Disconnect the network, press Refresh on Most Visited | Banner with Retry; the last results stay; retries at 5 s, 15 s, 60 s |
| Empty | Search for a nonsense string | "No worlds match", the hint, and a Clear filters button that works |
| Network budget | Watch `Opening world`/network over 30 minutes with the screen open on a search lens | One search at a time, at most one automatic refresh per minute; zero requests with the screen closed |
| Transitions | Switch lenses quickly | Fade and rise as in M2; no stuck curtain |

## Known limits
- Cards beyond 40 per page do not exist; Live Now pages locally (Previous/Next).
- No list view, favorites or lists yet (M4).
- Visits and tags show only for worlds that appeared in a search page.
