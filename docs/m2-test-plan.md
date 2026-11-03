# M2 in-game test plan

M2's gate (spec "Milestones"): F1, F2, F6, F7, F8 pass in game. Nothing here has been run yet; the code
only compiles and the Core logic is unit tested.

## Install and remove

Galaxy Sieve is a ResoniteModLoader (RML) mod. On this install, MonkeyLoader's RML compatibility pack loads it
from `rml_mods\`, like McpLink. It was installed on 2026-11-02; restart Resonite to load it. To reinstall after a
rebuild (copies `Discover.Resonite.dll`, `.pdb` and `.xml` into `<Resonite>\rml_mods\`):

```powershell
dotnet build src/Discover.Resonite -p:ResoniteInstallOnBuild=true
```

Remove: delete `Discover.Resonite.dll` (and the `.pdb` and `.xml` beside it) from
`E:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods\` and restart. Nothing else is written outside the
game's normal logs: the screen's slot is non-persistent, so the saved dash workspace never contains it. Do not
leave a `Discover.Resonite.nupkg` in `MonkeyLoader\Mods\` (an earlier MonkeyLoader build; it has been removed).

## What to look at first

1. Log lines (McpLink `logs` with pattern `\[Discover\]`, or the game log): `Started`, `Controller started`,
   `Screen attached`. Any `Tick failed` or `Paused` line is a bug.
2. The RML pack loaded the mod at all. If there is no `Started` line, look in the MonkeyLoader log for a load error
   naming `Discover.Resonite`, and check the mod list ("Galaxy Sieve") in the RML mod settings.

## Cases

| ID | Steps | Expected |
| --- | --- | --- |
| F1 | Open the dash on desktop, then in VR | A "Galaxy Sieve" tab sits right after Worlds; the stock Worlds tab looks and works exactly as before |
| F1 restart | Restart the game twice | Exactly one Galaxy Sieve tab each time (no duplicates saved into the dash) |
| F2 | With two public sessions of one saved world online (or any world with several sessions) open Live Now | One card for that world with `+N sessions` |
| F6 | Open Live Now | Each card shows thumbnail, name, host and age, `active/max users`, friend badge only when a contact is present, Mobile badge, Full badge |
| F7 | A world with two sessions where only the smaller has a contact: press Join | Joins the smaller one. A full world shows Full and its button is disabled |
| F8 | Join a session, return to the dash | Its card shows Open and the button reads Open; pressing it focuses the world with no second connection |
| Friends | Press Friends | Only sessions with a contact present (public, contacts, or contacts+) |
| Empty state | Friends with no contacts online | The empty message shows, no cards |
| Closed cost | Close the dash | No UI work (check `perf` frame cost against a run without the mod) |
| Stability | Log out and in again | The screen is re-attached (the saved workspace reload clears it); one tab |
| Update day | n/a | Tick failures never throw past the controller |

## Results (2026-11-02)

| ID | Result | Evidence |
| --- | --- | --- |
| Load | Pass, after one fix | Run 1: `Started` but no controller (`IsUserspace()` is false inside `WorldAdded`; fixed with a re-check). Run 2: controller and tab fine, UI build threw (`Panel` already nests; fixed). Run 3: clean log (`Started`, `Controller started`, `Screen attached`, no failures) |
| F1 | Pass | Tab sits after Worlds, stock tab unchanged; McpLink finds exactly one Galaxy Sieve screen after three restarts |
| F2 | Pass | Screenshot (run 4): Avatar Station "+4 sessions", The Hidden Star Theater "+3 sessions", one card each |
| F6 | Pass | Screenshot: thumbnail, name, host and age, users/max, friend badge |
| F7 | Pass | Join opened Elysium through `Userspace.OpenWorld` (engine log `Opening world`), the session with contacts present |
| F8 | Partial | After joining, the Elysium card showed the Open badge and an Open button. Pressing Open on an already-open world (expected: focus, no second `Opening world` connection) has not been exercised yet |
| Icon and animation | Pass | Run 5: tab icon is the imported `local://….png`; lens switches animate; no `[Discover]` warnings; one tab |
| Live Now count | Explained | 215 sessions cached but most are on the release build; this client is a beta, so `IsCompatible()` hides them (decisions Q2, stock behaviour) |

## Known limits of this milestone

- Cards: only the first 40 are built (no paging or row recycling until M3), no device mix or tag chips (drawer, M4).
- All thumbnails of the first 40 cards are loaded; lazy loading comes with row recycling.
- No search box, sort menu or filters yet; settings are defaults, not read from disk.
- Join works for sessions only; "Open New Instance" for published worlds comes with the record lenses (M3).
