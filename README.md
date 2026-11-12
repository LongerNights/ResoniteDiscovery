# ResoniteDiscovery

> **DISCLAIMER:** I’ve written applications and small mods for personal use before, but this mod was written almost entirely by Claude Code. It’s my first time using AI tools on a coding project. I wanted to see what they can really do, and I was surprised at every step. I know AI tooling is a polarizing topic, and I respect that people feel strongly about it either way. I’m reading through all the code myself, but there’s a lot of it. Since the mod is in early testing and release, please keep that in mind, and feel free to open an issue if you spot something off.

A client-side [Resonite](https://resonite.com) mod that **replaces the dash's Worlds tab**. It keeps the stock look but
changes how you find and return to worlds. A "Classic browser" button at the bottom of the left rail opens the stock tab
again.

Targets Resonite **2026.11.6.1136** (the current release; older builds are not supported). Loaded by
[MonkeyLoader](https://github.com/ResoniteModdingGroup/MonkeyLoader.GamePacks.Resonite) with its ResoniteModLoader
compatibility pack, which provides ResoniteModLoader **5.0.1.2**. Older loader versions are not supported. MIT licensed.

## How it differs from the stock Worlds tab

- **One search box.** `+required -excluded tag:name author:name "quoted phrases"`, tag chips, and the stock "Custom"
  options folded into Sort and Filters. The six category buttons are replaced by one Browse tab with recommended tags.
- **Live and published worlds together.** Current Worlds, Live Now and Friends show sessions; Featured, New and Random
  search published worlds. Live tabs cost no extra requests, and a live-only mode turns record searches off entirely.
- **Your own things in one place.** Favorites, named lists, personal tags, Recent, My Worlds and Group Worlds.
- **Hide what you don't want.** Hide worlds, authors or words.
- **Detail panel.** Press a card for the preview, description, every live session (with who is in it), Join, and more by
  the author just like the default Worlds tab.
- **Small extras.** A Visited mark, favorite alerts, saved views, export/import functionality (for hides, favorites, and lists), and a
  choice of grid or list with rows per page.

## Trending, Rising and Hidden Gems

> **Off by default.** These three tabs (the **Insights** section) are hidden until you switch them on in
> Settings > Sidebar (under "Experimental", with a note explaining the state of things). Testers found them not very
> useful on local data alone. They may get much better later: if enough people ask for it, the plan is a server that
> watches public world data and keeps a continuously updating list for Trending, Rising and Hidden Gems to read. That does
> not exist yet.

Resonite has no "trending" data to ask for, so for now the mod builds it **on your PC, from what it sees while the game is running**:

- **History starts empty.** Trending shows "Nothing ranked yet" until the game has been running for an hour or two.
- **It only sees what you see.** The mod notes how many people are in each public session it can see (every minute with
  the Worlds screen open, every five minutes otherwise). Nothing is recorded while the game is closed.
- **It is your history, not everyone's.** Two people running the mod will get slightly different rankings.
- **Old data expires.** Samples older than 14 days are dropped. History is stored in
  `%APPDATA%\ResoniteDiscovery\<account>\history.json` (world keys, counts and timestamps only). Delete it to start over.

| Tab | Meaning | Becomes useful after |
| --- | --- | --- |
| **Trending** | Most people in the world over the last 6 hours | An hour or two |
| **Rising** | Last 6 hours of attendance well above the world's own average for the 24 hours before | About a day |
| **Hidden Gems** | Newer, smaller worlds (50 to 5,000 visits, published within 180 days) that are gaining visits | A few days of play |

## Install

1. Make sure you are on Resonite 2026.11.6.1136 or newer, and install MonkeyLoader with its ResoniteModLoader
   compatibility pack (see the link above). The mod needs ResoniteModLoader 5.0.1.2 or newer; plain ResoniteModLoader
   5.0.1 (assembly 5.0.1.0) is too old and the mod will not load.
2. Download `Discover.Resonite.dll` from the [latest release](../../releases/latest) and put it in
   `<Resonite>\rml_mods\` (for Steam: `...\steamapps\common\Resonite\rml_mods`).
3. Restart Resonite. The dash's **Worlds** tab is now this one.

To uninstall, delete the DLL. Nothing in your dash is changed, and the stock button is only hidden while the mod runs.

Your data (favorites, lists, tags, history, saved views, settings) lives per account in
`%APPDATA%\ResoniteDiscovery\<account>\`.

## Building from source

Needs the .NET 10 SDK and a Resonite install (path set in `Directory.Build.props`).

```powershell
dotnet test tests/Discover.Tests
dotnet publish src/Discover.Resonite -c Release
```

The engine-free core is in `src/Discover.Core`, the engine-facing code in `src/Discover.Resonite`. The technical spec
is [`docs/spec.md`](docs/spec.md) and the decision log is [`docs/decisions.md`](docs/decisions.md).
