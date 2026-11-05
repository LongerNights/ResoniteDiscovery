# ResoniteDiscovery: initial Claude Code run (M0 Verification)

You're starting work on ResoniteDiscovery. `CLAUDE.md` is already loaded, so I won't repeat it. Read `docs/spec.md` (at least Summary, Data sources, Integration, Performance/safety, Milestones), `docs/verification.md`, `docs/decisions.md`, and `docs/reference/README.md` first. The repo is scaffolded but has never been built. This session is **Milestone M0**. Do not start M1 unless I say so.

## Goal

Get the repo building and close as many `docs/verification.md` rows as you can with evidence, leaving a precise, minimal list of what I still need to run for you.

## Steps

1. **Build setup (B1, B2, B3).** From the repo root run `dotnet --info`, `dotnet build Discover.slnx`, then `dotnet test tests/Discover.Tests`.
   - If `Remora.Resonite.Sdk` 2.3.4 doesn't restore, find the real current version on NuGet, update `global.json`, and log it in `docs/decisions.md`.
   - Fix scaffold errors (csproj, usings, test fakes). Keep changes minimal and in the spirit of the hard rules.
   - Confirm Core and Tests build with no engine references. Check that `CoreIsolationTests` actually passes.
   - Confirm what entry-point shape the SDK generates for MonkeyLoader, and whether `ResoniteInstallOnBuild` targets my install at `E:\Program Files (x86)\Steam\steamapps\common\Resonite` (set it only if safe).
   - Update B1 to B3 in the tracker with the evidence.

2. **Close D-type rows with `tools/netmeta.py`.** Run it against the DLLs in my install (FrooxEngine.dll takes ~35 s; use `pip install dnfile` if needed). Prioritise, in order: V2, V3, V4/V5, V9, V11, V12, V14, V18, V19, V20, V21. Netmeta gives signatures only. Where an answer needs a method body, don't guess: mark the row Partial and add the type to the next dumper request list.

3. **Close P-type rows with McpLink if `mcp__mcplink__*` tools are listed** (the game must be running). Use only read-only probes and `world:"userspace"` as read-only, with no unbounded loops. If the tools aren't available, say so once and skip; don't try to work around it. Good targets are V3 (does `ScreenEnabled=false` remove the tab, and does re-enabling restore it?), V6, V7, V8, V12, and the V22 save-path question. **Before any probe that writes, even a toggle, ask me first.** A userspace crash kills the game.

4. **Update the spec.** For every row you close, replace the matching `VERIFY` marker in `docs/spec.md` (Integration, Data sources, UI) with the confirmed fact. Add a `docs/decisions.md` line for anything that changes a design choice. V15/V22 (persistence of the dash screens record) and V23 (compatibility with installed dash mods) are the highest-risk. If they force a design change, stop and describe the options instead of picking one silently.

5. **Write the dumper request.** Produce `docs/reference/next-dumper-request.md`. It should contain the exact types and members I still need ResoniteDumper to emit (method bodies for `RadiantDashScreen.OnAttach`, `WorldListFacetPreset`'s world-open call, `SessionsManager` event dispatch, and so on), grouped into one batch, with a one-line reason each, so I can run it in one go.

## Constraints

- All hard rules in `CLAUDE.md` still apply. Ground every engine member in the decompile, netmeta output, or a probe, and cite it in the tracker's evidence column.
- Don't run `git init` (ask first). Don't edit files under `docs/reference/dumper-*.txt`.
- No feature code. The only code changes are build and scaffold fixes.

## Finish with

A short report: what builds and passes, rows closed (ID plus one-line answer), rows still Open or Partial and why, any spec or design risks found, and what I need to run for you next (dumper batch, in-game test, or a decision). If every Integration `VERIFY` is closed, say so and propose the first M1 task list.
