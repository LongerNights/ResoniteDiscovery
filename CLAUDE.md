# Galaxy Sieve — Resonite Discovery Mod

*Auto-loaded project guide for Claude sessions in this folder. Keep it short and current.*

**Galaxy Sieve** is a client-side Resonite mod that **replaces the dash's Worlds tab** with a
cleaner one (decision 2026-11-03, supersedes "sit beside"): the stock tab's button is hidden
at runtime only, and a "Classic browser" button still opens it. The rail is Now (Current
Worlds, Live Now, Friends), Discover (Browse with recommended tags replacing the six stock
category buttons, Featured, New, Random), Yours (Favorites, lists, Recent, My Worlds, Group
Worlds); v1.1 adds Trending/Rising/Hidden Gems. The stock "Custom" search options move into
the top bar's sort and Filters controls; the spec's parity table is the checklist. Code
namespaces stay `Discover.*`; the tab is labelled "Worlds". A ResoniteModLoader (RML) mod;
personal use for now; MIT.

- **Target:** Resonite client **2026.10.29.1239** (FrooxEngine 2026.10.29.1239,
  SkyFrost.Base 1.0.0.0), net10.0. Not the headless client.
- **Owner:** Winter. Windows, Steam install at
  `E:\Program Files (x86)\Steam\steamapps\common\Resonite` (MonkeyLoader with its RML compatibility pack loads RML mods from `rml_mods\`; see `docs/reference/environment.md`).

## Where things are

| Path | What |
| --- | --- |
| `docs/spec.md` | **Technical spec — source of truth** for scope, lenses, formulas, UI, budgets, safety rules, milestones |
| `docs/verification.md` | M0 tracker: every `VERIFY` / V-question, how to close it, and the recorded answer |
| `docs/decisions.md` | Decision log + open questions with their defaults |
| `docs/reference/` | Decompiled engine excerpts (ResoniteDumper output) + index of what has been dumped |
| `src/Discover.Core/` | Engine-free core: model, lenses, search syntax, formulas, persistence, source interfaces |
| `src/Discover.Resonite/` | Engine-facing: adapters over `Engine.Cloud`, dash screen, controller, joins. Remora.Resonite.Sdk |
| `tests/Discover.Tests/` | xUnit tests against Core with fakes (no engine) |
| `CLAUDE-MCPLINK.md` | How to drive the live game via the McpLink MCP server — read before using `mcp__mcplink__*` |

Loader shims live inside `src/Discover.Resonite/Loader/` behind `#if RESONITE_<LOADER>` (spec Integration §6);
the shipping one is RML (`ResoniteModLoaderEntry.cs`, `#if RESONITE_RML`). `Discover.Resonite` compiles Core's
sources in (no ProjectReference), so the mod is one DLL in `rml_mods/`.

## Current status

**Overhaul spec written 2026-11-03** (`docs/spec.md`): next milestones are M5 stock-tab audit
(V24, V29, V32), M6 takeover, M7 rail and Browse, M8 Current Worlds, M9 parity, M10 v1.1.
The M0–M4b entries below describe the Galaxy Sieve build the overhaul starts from.

**Milestone M4b — My Worlds, Group Worlds, detail panel, settings with a customizable rail (F28), export/import: installed 2026-11-02; grid/list toggle and copy-URL deferred** (`docs/m4b-test-plan.md`). 348 tests.

**Milestone M4a — Your stuff: working in game 2026-11-02** (favorites, lists, tags with typed entry and prediction, Recent; custom-id sessions are saveable, random-id ones are not). Favorites (star on cards), named lists (rail, rename/move/delete, organize panel), personal tags (orange chips and card tags), Recent, per-account files loaded once the account is known. 315 tests. In-game steps: `docs/m4a-test-plan.md`. Next: M4b (My Worlds, Group Worlds, detail drawer, settings screen, export/import buttons, grid/list toggle).

**Milestone M3 — Record lenses: working in game 2026-11-03 (F27 and the 30-minute network trace not yet confirmed).** Search lenses (New,
Most Visited, Featured, Random) over `Engine.Cloud.Records.FindRecords` with a cache, paging, backoff and a
failure banner; search box with term syntax; sort menu; filters popover; tag chips; the join confirmation
menu (the engine's choice dialog) and "New session" for published worlds; the full purple/black screen per
`docs/m3-design.md`. 304 tests pass. The gate (F3, F4, F5, F11, F12, F14, F27 and the network budget) needs
Winter to install and restart: see `docs/m3-test-plan.md`. Next after the gate: M4 (favorites, lists, tags,
export/import, Recent, My Worlds, Group Worlds, detail drawer, settings).

**Milestone M2 — Live screen: passed in game 2026-11-02 (F8's Open press not exercised).** `Discover.Resonite` has the
session adapter, join service, controller (tick via `World.RunInUpdates`), dash attach (non-persistent
slot, after the workspace loads), the purple/black card-grid UI for Live Now and Friends with lens
transitions, the galaxy tab icon (embedded, imported into the local asset DB), and the RML shim
(`Loader/ResoniteModLoaderEntry.cs`). Gate: F1, F2, F6, F7 pass; F8 partial (Open badge shown, the
Open-press focus not yet exercised). Results in `docs/m2-test-plan.md`. Next: design the M3 screen
(search, chips, sort, paging) and build the record lenses.

**Milestone M1 — Core logic: done 2026-11-02.** `Discover.Core`
now has the merge index (`DiscoveryIndex`), visibility rule (`SessionRules`), change queue,
join planner, search syntax (`SearchQuery`), lenses (`LensEngine`, `RecordLenses`), Trending/
Rising/Gem formulas and history log, settings, and persistence (`UserLibrary`, `UserLibraryStore`,
`LibrarySaver`, export/import). 215 tests pass; `CoreIsolationTests` confirms no engine reference.
No adapters or UI yet (M2). M0 is not formally closed: V6/V7/V8 and V23 remain (below).

**M0 — Verification.** The solution builds and tests pass (2026-11-02). M0's gate: every row in
`docs/verification.md` answered and no `VERIFY` left in the spec's Integration section (none
left). Q1 is decided (sit beside). Still open: V6/V7/V8 (need McpLink `eval`) and V23 (in-game
test). Next: M2 (live screen) once Winter says go. See spec "Milestones".

## Build & test (Windows, from the repo root)

```powershell
dotnet build Discover.slnx
dotnet test tests/Discover.Tests
dotnet publish src/Discover.Resonite -c Release   # mod zip for the Resonite folder
dotnet build src/Discover.Resonite -p:ResoniteInstallOnBuild=true   # copies the DLL into <Resonite>\rml_mods (restart needed); never by default
```

- SDK and Resonite versions are pinned in `global.json` (`msbuild-sdks` pins
  Remora.Resonite.Sdk). The SDK's bundled reference assemblies can lag the install — build
  against the real install before in-game testing.
- Core and Tests must build and pass with **no Resonite install and no engine references**.

## Hard rules (from the spec — don't relax without updating it)

1. **Core never references the engine.** `Discover.Core` reads only `ISessionSource` /
   `IRecordSource` and returns plain data. A test enforces this.
2. **Threading:** session event handlers only enqueue; the controller drains ≤ 50 changes per
   update tick. UI mutations after any `await` go through `world.RunSynchronously(...)`.
   Never `.Result` / `.Wait()`. (`BatchQuery`/`RecordCache` results complete on thread-pool
   threads — confirmed in the decompile.)
3. **Joins/opens only via `Userspace.OpenWorld`** — never `JoinSession` (skips mutual-block check).
4. **Session visibility rule** is applied to every session before any lens (spec, Data model).
5. **Privacy:** never display/log/store `BroadcastKey`, `HostMachineId`, `HostUserSessionId`
   or raw session URLs. History stores world keys, counts, timestamps only.
6. **No new data-model types, no world writes, no Harmony patches, no edits to the stock Worlds
   screen or its facets, no `ScreenEnabled` writes.** The only stock writes allowed are the
   runtime `ActiveSelf` of the stock Worlds *button* slot and the dash's current screen
   (`TabTakeover`, spec Integration §2), reverted on shutdown and failure. The Galaxy Sieve
   screen slot is `PersistentSelf = false`. Plain C# controller under a plain `RadiantDashScreen`.
7. **Networking:** no own HTTP clients; everything through `Engine.Cloud`, within the
   spec's budget table. Live lenses cost zero extra requests.

## Working with engine APIs

- **Ground every engine member in the decompile or a live probe — never guess.** If a member
  isn't in `docs/reference/` or confirmed in `docs/verification.md`, mark it `// VERIFY:` in
  code and add a row to the tracker.
- **Member lookups (fast, no game needed):** `tools/netmeta.py` lists types and member
  signatures straight from the DLL metadata. In Cowork: stage the needed DLLs from the
  Resonite install, `pip install dnfile`, then
  `python3 -I tools/netmeta.py <dll> type RadiantDashScreen` or `... find '<regex>'`.
  FrooxEngine.dll takes ~35 s to load. It shows signatures, not method bodies.
- **Method bodies:** Winter runs ResoniteDumper; or ILSpy-Mcp if registered (`ilspy-mcp`).
- To get more decompiled code, Winter runs ResoniteDumper; save each run as
  `docs/reference/dumper-<date>-batch<N>.txt` and update `docs/reference/README.md`.
- **McpLink** is an RML mod serving MCP at `http://localhost:7357/mcp` on Winter's PC. It is
  reachable from Claude Code running on Windows (McpLink 2.14.0, 98 tools; `eval` needs `McpLink_libs`, not installed yet) (registered with `claude mcp add mcplink -- python
  <path>\proxy\mcplink_proxy.py`), **not** from Cowork's cloud session or its Linux VM. If no
  `mcp__mcplink__*` tools are listed, say so and fall back to the dumper or ask Winter to probe.
  When available, it closes questions fastest: `describe_type`,
  `reflect_get`, `env`, `logs`, and `eval` for read-only probes. The dash lives in
  **userspace — treat `world:"userspace"` as read-only** unless Winter explicitly OKs a
  write (a userspace crash kills the game). No unbounded loops in `eval`.

## Conventions

- C# latest, nullable enabled, file-scoped namespaces, `Discover.*` namespaces.
- UI strings go in one string table keyed by id; colors reference `RadiantUI_Constants`
  by name, never hex.
- Tunable numbers (formula constants, budgets, intervals) live in settings, not literals.
- Log through `UniLog` with a `[Discover]` prefix.
- When a spec detail changes, edit `docs/spec.md` and add a line to `docs/decisions.md`.
- Git repo since 2026-11-02 (branch `main`). Commit only when Winter asks.

# Resonite / McpLink
@CLAUDE-MCPLINK.md
