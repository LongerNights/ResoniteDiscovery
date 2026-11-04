# ResoniteDiscovery

A client-side [Resonite](https://resonite.com) mod that **replaces the dash's Worlds tab** with a cleaner, searchable
one. The stock tab's button is hidden while the mod runs and a "Classic browser" button at the bottom of the left rail
opens it again. The look follows the stock tab (its background, greys and cyan accent, the engine's own icons).

Personal-use mod for now. A ResoniteModLoader (RML) mod, loaded through MonkeyLoader's RML compatibility pack.
Targets Resonite **2026.10.29.1239**. MIT licensed.

## What it does

| Rail section | Tabs |
| --- | --- |
| **Now** | Current Worlds (the worlds you have open, with Go there and Leave), Live Now, Friends |
| **Discover** | Browse (recommended tags), Featured, New, Random |
| **Yours** | Favorites, your own lists, Recent, My Worlds, Group Worlds |
| **Insights** (off by default) | Trending, Rising, Hidden Gems, from a local history the mod builds over time (see below) |

- Search with `+required -excluded tag:name author:name "quoted phrases"`, tag chips (required, then excluded), sort and
  filters that change with the tab, and saved views (the Views menu).
- Cards in a grid or list, with the world's 360 degree preview that pans as you hover it. Press a card for the detail
  panel: preview, description, tags, every live session (pick one to see its uptime, visibility and who is in), Join,
  favorite, lists, copy link, more by author, hide.
- Starting a new session asks who can join (and mobile friendly), like the stock settings dialog.
- Favorites, named lists, personal tags, hide lists (worlds, authors, words), favorite alerts, export and import.
- Cards say when you have been in a world (Visited), and Current Worlds cards have a red x to leave quickly.
- Settings: start tab, live-only mode (no search requests), animations and their speed, layout (grid or list, cards per
  row, rows per page), the sidebar, alerts, what is hidden, data.

## Trending, Rising and Hidden Gems: read this first

These three tabs (the **Insights** section, switched off by default; turn it on in Settings > Sidebar) are **not** fed by
a Resonite server list. Resonite has no "trending" data to ask for. The mod builds it itself, **on your PC, from what
it sees while the game is running**.

- **History starts empty.** The first time you open Trending you will see "Nothing ranked yet". That is normal. Leave
  the game running for an hour or two (or several play sessions) and the tabs fill in.
- **It only sees what you see.** While Resonite runs the mod notes how many people are in each public session it can
  see (every minute with the Worlds screen open, every five minutes with it closed). Nothing is recorded while the game
  is closed, so a long break leaves a gap.
- **It is your history, not everyone's.** Two people running the mod will get slightly different rankings, depending on
  when they were online and which sessions their account can see.
- **Old data expires.** Samples older than 14 days are dropped. History is stored in
  `%APPDATA%\ResoniteDiscovery\<account>\history.json` (world keys, counts and timestamps only; never session
  addresses or user names). Delete the file to start over.

What each tab means:

| Tab | Meaning | Needs |
| --- | --- | --- |
| **Trending** | Worlds with the most people in them over the **last 6 hours**, slightly favouring worlds that were busy at many checks, not just once | At least 2 checks with people in them inside the window |
| **Rising** | Worlds whose attendance over the last 6 hours is **higher than their own average over the 24 hours before** | A busy enough recent average, and earlier history to compare against (so it takes about a day to become useful) |
| **Hidden Gems** | Newer, smaller worlds that are **gaining visits** relative to their size | Published within 180 days, between 50 and 5,000 visits, seen with people in it within 7 days, and visit counts you have seen on more than one day |

So: Trending works after an hour or two, Rising after about a day, Hidden Gems after a few days of play. A world that
only shows up on one check, or that nobody was in, is never ranked.

## Install

1. Install [MonkeyLoader](https://github.com/ResoniteModdingGroup/MonkeyLoader.GamePacks.Resonite) with its RML
   compatibility pack (or ResoniteModLoader), as described by those projects.
2. Build the mod (below) or take `Discover.Resonite.dll` from a build, and put it in `<Resonite>\rml_mods\`.
3. Restart Resonite. Open the dash: the **Worlds** tab is now this one.

To go back, delete the DLL from `rml_mods`. Nothing in your worlds or the stock tab is changed (the stock button is only
hidden while the mod runs, and restored if the mod stops or fails).

Your data (favorites, lists, tags, history, saved views, settings) is kept per account in
`%APPDATA%\ResoniteDiscovery\<account>\`. Export and import use that folder's `exports` directory.

## Build

Requires the .NET 10 SDK and a Resonite install (the path is set in `Directory.Build.props`).

```powershell
dotnet build Discover.slnx
dotnet test tests/Discover.Tests
dotnet build src/Discover.Resonite -p:ResoniteInstallOnBuild=true   # copies the DLL into <Resonite>\rml_mods (restart Resonite)
tools\redeploy-resonite.ps1                                         # closes Resonite, builds and installs, starts it again
```

The core (`src/Discover.Core`) has no engine reference and is covered by the unit tests; the engine-facing code
(`src/Discover.Resonite`) is checked in game against the test plans in `docs/`.

## Behaviour you can rely on

- All networking goes through the engine (`Engine.Cloud`), within a fixed request budget; live tabs cost no extra requests.
- Joins always go through `Userspace.OpenWorld`, so the engine's own block checks apply.
- Session keys, host machine ids and raw session URLs are never shown, logged or stored; history keeps world keys,
  counts and timestamps only.
- No Harmony patches and no changes to worlds. The only stock UI change is hiding the stock Worlds button while the
  mod runs.
- No Resonite artwork is bundled: the icons are the engine's own, read from the install when the mod runs.

## Documents

- [`docs/spec.md`](docs/spec.md): the technical spec (source of truth)
- [`docs/ui-redesign.md`](docs/ui-redesign.md): the UI plan and its status
- [`docs/decisions.md`](docs/decisions.md): decision log
- [`docs/verification.md`](docs/verification.md): the engine questions, how each was answered
- In-game test plans: `docs/m*-test-plan.md` and `docs/ui-step*-test-plan.md`
