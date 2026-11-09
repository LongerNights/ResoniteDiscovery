# Decompiled reference

Excerpts of Resonite assemblies produced by ResoniteDumper are kept **locally only** (`dumper-*.txt` is git-ignored): decompiled engine source is not published in this repository, because Resonite's Mod and Plugin Policy does not allow redistributing engine components. Findings are recorded in `../verification.md`. The first batch (below) was removed from the repository on 2026-11-08. Originally: excerpts of Resonite assemblies produced by ResoniteDumper from Winter's install
(`E:\Program Files (x86)\Steam\steamapps\common\Resonite`). Read-only reference — never edit
a dump; add a new batch instead. Findings go in `../verification.md`.

| File | Build | Types |
| --- | --- | --- |
| `dumper-2026-11-02-batch1.txt` | 2026.10.29.1239 | `WorldsScreenInitializer`, `GridContainerScreen`, `GridContainerPreset`, `SearchSortParameter`, `SearchSortDirection`, `OutputDevice` (SkyFrost.Base), `BatchQuery<,>`. Not found: `FrooxEngine.Store.SearchQueryParser`, `Elements.Assets.OutputDevice` |

The spec's Sources section lists further types read in ILSpy that are not saved here yet. On 2026-11-02 many
more bodies were read live through ILSpy-MCP (cited as "ilspy" in `../verification.md`); `next-dumper-request.md`
lists them as batch 2 so they get recorded here.
If a dump is needed again, re-run it and add it as a new batch.

Naming: `dumper-<yyyy-mm-dd>-batch<N>.txt`. On a Resonite update, re-dump the types the code
depends on and diff against the previous batch.
