# Next ResoniteDumper request: batch 2

For Winter to run in one go against build **2026.10.29.1239**. Save the output as
`docs/reference/dumper-<date>-batch2.txt` (git-ignored, local only) and record findings in `../verification.md`.

**Why this batch.** On 2026-11-02 most M0 rows were closed by reading method bodies live through
ILSpy-MCP (see `verification.md`, evidence "ilspy"). Those reads are not stored in the repo.
Part A puts the bodies the spec now depends on into `docs/reference/`, so the evidence survives
and can be diffed on the next game update. Part B covers bodies not read yet, mostly for M2.

Full bodies unless a member list is given. All types are in `FrooxEngine.dll` unless noted.

## Part A: record what the spec now relies on

| Type | Members | Reason |
| --- | --- | --- |
| `RadiantDashScreen` | whole type | V2/V3: screen children, `BaseResolution` default, `Show/Hide`, `SetAspectRatio` |
| `RadiantDash` | `AttachScreen` (all 3 overloads), `RegisterScreen`, `Screens`, `GetScreen<T>`, `UpdateScreens`, `BuildScreenButton`, `OnCommonUpdate` | Attach path and button generation (V2, V3, Q1) |
| `RadiantDashButton` | `Setup`, `UpdateColors`, `Pressed` | Proof that `ScreenEnabled` only greys the button (V3) |
| `UserspaceRadiantDash` | `OnAttach`, `FocusScreenOrAttach<T>`, `SetupDefaultScreens` | Screens workspace path and autosave (V15) |
| `UserspaceScreensManager` | `OnAttach`, `AddWorldsScreen`, `SetupLoggedInOnlyScreen`, `SetupDefaults` | Localized Worlds label; find-by-preset idiom |
| `Workspace` | `SaveChanges`, `MarkModified`, `Modified`, `SaveOnExit`, `LoadTargetWorkspace`, `OnCommonUpdate` | When the dash is saved (V22) |
| `Slot` | `Save(SaveControl)`, `SaveObject(DependencyHandling, bool, ReferenceTranslator)` | Non-persistent children skipped (V22a) |
| `ModificationNotificationHelper` | `NotifyModified` | What marks the workspace dirty (V22b) |
| `Userspace` | `OpenWorld`, `OpenWorldInternal`, `LoadWorld`, `ResolveUris`, `GetExistingWorld`, `FocusWorld` | Open/join path, block check, access level (V4, V5, F8) |
| `WorldStartSettings` | whole type | Constructors and defaults (V4, V5) |
| `LegacyWorldDetail` | `OnOpen`, `CustomWorldStart` | Stock open calls (V4) |
| `LegacyWorldListManager` | `UpdateList`, `GenerateSearchParameters`, `Filter`, `AddRecords`, `SubmitList`, `GetFreeWorldItem` | Stock search parameters, session filters, pooling (V8, V12, Q2) |
| `LegacyWorldThumbnailItem` | `UpdateThumbnailURL`, `OnAttach`, `OnActivated`, `UpdateInfo`, `GetWorldPreviewMaterial` | Thumbnail loading and 360 projection (V10) |
| `WorldListFacetPreset` | `Build` | Stock list layout (V12) |
| `NotificationPanel` | `ShowNotification`, both `AddNotification` overloads | Toast API behaviour (V19) |
| `EngineSessionListingSettings` | whole type | V14 |
| `SkyFrost.Base.SessionsManager` (SkyFrost.Base.dll) | whole type | Event threading and lock (V9), listing filter (V14) |
| `SkyFrost.Base.SessionInfo` (SkyFrost.Base.dll) | `SanitizedHostUsername`, `HasEnded`, `IsExpired`, `GetSessionURLs`, `ToString`, `IsAllowedName` | Privacy footguns and visibility inputs |
| `SkyFrost.Base.GroupsManager` (SkyFrost.Base.dll) | whole type | Group Worlds (V18) |
| `SkyFrost.Base.RecordsManager` (SkyFrost.Base.dll) | `GenerateRecordUri`, `FindRecords<R>`, `RecordCache<R>`, `RecordBatch<R>` | Search and record URIs (V5, F3) |
| `SkyFrost.Base.RecordHelper` (SkyFrost.Base.Models.dll) | `GetUrl` | Same URI as `GenerateRecordUri` (V5) |
| `Elements.Assets.StringParsingHelper` (Elements.Assets.dll) | `SanitizeFormatTags` | V11: it neutralizes rather than strips |
| `Elements.Assets.StringRenderTree` (Elements.Assets.dll) | ctor(string), `GetRawString` | Tag stripping for card names (V11) |
| `RadiantUI_Constants` | whole type | Palette names (V21) |

## Part B: not read yet

Q1 was decided as "sit beside" on 2026-11-02, so the two layout rows below are optional (only needed if hiding is revisited after M4).

| Type | Members | Reason |
| --- | --- | --- |
| `FrooxEngine.UIX.HorizontalLayout` and its base `FrooxEngine.UIX.LayoutController` (optional) | whole types | Hiding option B: does an inactive button slot drop out of the dash button strip's layout? |
| `FrooxEngine.UIX.RectTransform` (optional) | the methods that collect children for layout (search for `ActiveSelf` / `IsActive` checks) | Same question as above |
| `SkyFrost.Base.SessionInfo` (SkyFrost.Base.dll) | `IsCompatible` (probably an extension; search `IsCompatible`) | Q2: what "compatible" means |
| `LegacyWorldItem` | `GetBestSession`, `GetBestOpenedWorld`, `SetTarget`, `GenerateSessionIcons` | F7 join-target choice; compare with the stock rule |
| `SkyFrost.Base.ContactManager` (SkyFrost.Base.dll) | `CountPresentContacts`, `GetContact` | Friends lens; check lock and thread use |
| `SkyFrost.Base.SkyFrostInterface` (SkyFrost.Base.dll) | `Update`, and the `Engine` method that calls `Cloud.Update` | Which thread raises `SessionsChanged` (V9, informational) |
| `SkyFrost.Base.RecordSearch<R>` (SkyFrost.Base.dll) | whole type | Stock paging helper. Possibly reuse for F3 paging instead of hand-rolled `FindRecords` calls |
| `World` | `RunSynchronously`, `RunInUpdates`, `RunInSeconds` | Rule 2 marshalling API shapes |
| `FrooxEngine.Userspace` | `GetThumbnailData` | Open-world thumbnail for the Open badge (V10) |

## Not for the dumper: McpLink probes (game running)

| Probe | Reason |
| --- | --- |
| For each patched method in V23, `HarmonyLib.Harmony.GetPatchInfo(method)` → owners | Identify which installed mod patches `RadiantDash.UpdateScreens`, `LegacyWorldDetail.OnOpen` and the others. Needs a `MethodBase` argument, which is easiest through `eval` once `McpLink_libs` is installed |
| V6/V7/V8 bounded `FindRecords` probes | Need `eval` (`McpLink_libs`) |
