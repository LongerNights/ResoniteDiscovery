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
| **Insights** (off by default) | Trending, Rising, Hidden Gems, from a local history the mod keeps |

- Search with `+required -excluded tag:name author:name "quoted phrases"`, tag chips (required, then excluded), sort and
  filters that change with the tab, and saved views (the Views menu).
- Cards in a grid or list, with the world's 360 degree preview that pans as you hover it. Press a card for the detail
  panel: preview, description, tags, every live session (pick one to see its uptime, visibility and who is in), Join,
  favorite, lists, copy link, more by author, hide.
- Starting a new session asks who can join (and mobile friendly), like the stock settings dialog.
- Favorites, named lists, personal tags, hide lists (worlds, authors, words), favorite alerts, export and import.
- Settings: start tab, live-only mode (no search requests), animations, layout (grid or list, cards per row), the
  sidebar, alerts, what is hidden, data.

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
