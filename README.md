# Galaxy Sieve

A client-side Resonite mod that adds a world-discovery tab to the dash, beside the stock Worlds tab:
live sessions and published worlds merged into ranked, searchable lenses, plus favorites,
named lists and personal tags.

Status: pre-alpha. Milestone M2 (live screen: Live Now and Friends lenses, joining, galaxy-themed tab)
runs in game; record search, favorites and lists come next. Targets Resonite 2026.10.29.1239.
A ResoniteModLoader (RML) mod.

- Spec: [`docs/spec.md`](docs/spec.md)
- Verification tracker: [`docs/verification.md`](docs/verification.md)
- Decisions: [`docs/decisions.md`](docs/decisions.md)
- In-game test plan: [`docs/m2-test-plan.md`](docs/m2-test-plan.md)

## Build

Requires the .NET 10 SDK.

```powershell
dotnet build Discover.slnx
dotnet test tests/Discover.Tests
dotnet build src/Discover.Resonite -p:ResoniteInstallOnBuild=true   # copy the mod DLL into <Resonite>\rml_mods (restart Resonite)
```
