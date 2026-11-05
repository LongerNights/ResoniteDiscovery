# Winter's Resonite environment

From the game log `GALACTICRAFTER - 2026.10.29.1239 - 2026-11-02 17_11_58.log` and a read of the install folder (2026-11-02).

| Item | Value |
| --- | --- |
| Install | `E:\Program Files (x86)\Steam\steamapps\common\Resonite` (not C:) |
| Data / cache | `C:\Users\jay\AppData\LocalLow\Yellow Dog Man Studios\Resonite`, `C:\Users\jay\AppData\Local\Temp\Yellow Dog Man Studios\Resonite` |
| Build | Beta 2026.10.29.1239, .NET runtime 10.0.9, Windows 11, x64 |
| Mod loader | **MonkeyLoader** (`MonkeyLoaderWrapper`) with game packs `MonkeyLoader.GamePacks.Resonite` and `MonkeyLoader.GamePacks.ResoniteModLoader`. RML-style mods in `rml_mods\` load through the RML compatibility pack (69 mods loaded) |
| McpLink | 2.14.0, loaded via MonkeyLoader's RML pack; "Tool registry built: 98 tools", listening on `http://localhost:7357/mcp`. Config under `MonkeyLoader\Configs` (defaults) |
| McpLink `eval` | **Unavailable**: `rml_mods\McpLink_libs\` is not installed |
| McpLink `hot_reload` | Unavailable: `ResoniteHotReloadLib` not in `rml_libs` (optional, only for hot reload) |
| User id | `U-Winter-Storm` |

## Dash-related mods already installed (test ResoniteDiscovery alongside these)

`SessionTabOverhaul` 2.1.1, `ResoTabbed` 2.1.1, `ConsistentDashboard` 1.0.0, `NoHeadMenuDash` 2.0.0,
`ResoniteModSettings` 2.3.0 (mod settings UI), `SessionTweaks` 5.1.0. `WorldsTabOverhaul` is not installed.

## Facts learned from the log

- The dash's screens are a **cloud workspace record**: `Loading Private/RadiantDash/Screens for U-Winter-Storm`
  → `resrec:///U-Winter-Storm/Workspaces/Private/RadiantDash/Screens`. See verification V15/V22.
- SignalR messages are dispatched on thread-pool workers (`HubConnection.DispatchInvocationAsync` →
  `ThreadPoolWorkQueue`); the engine marshals UI work through `World.RunSynchronousActions`. Supports V9's queue design.
- `FrooxEngine.NotificationPanel.AddNotification(userId, message, thumbnail, colorX, NotificationType, mainMessage, overrideProfile, …)`
  exists (seen in a stack trace). Candidate for V19.
- MonkeyLoader's Resonite pack ships `FacetPresetLoadedHook`, `ForceUpdateFacets` and `DataFeedInjectors` monkeys — possible
  supported hooks for dash/facet work.
