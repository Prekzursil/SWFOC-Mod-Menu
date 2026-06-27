# Editor GUI Audit — 2026-04-10

Agent C workstream, sprint day 2026-04-10.

**Target repo:** `C:\Users\Prekzursil\Downloads\SWFOC editor\` (project `SwfocTrainer.App`).
**Authoritative source for this audit:** `src/SwfocTrainer.App/MainWindow.xaml`, `src/SwfocTrainer.App/ViewModels/MainViewModel*.cs`, `src/SwfocTrainer.App/Program.cs`, `src/SwfocTrainer.Core/Services/*.cs`.
**Legacy pipeline in scope:** `SdkOperationRouter` + `ActionSymbolRegistry` + `SymbolTacticalGodMode` symbol table.
**New pipeline in scope:** `LuaBridgeExecutor` → `NamedPipeLuaBridgeClient` → `\\.\pipe\swfoc_bridge`.

This document is the "before" snapshot captured by Agent C immediately before the V2 rebuild lands. It exists to justify the rebuild, track quarantine candidates, and give the next session a single entry point into "what was broken and why".

---

## 1. Tab inventory

The current `MainWindow.xaml` declares **16** `TabItem` elements inside the main `TabControl` (`Grid.Row="1"`). Line numbers are from `src/SwfocTrainer.App/MainWindow.xaml` at the time of this audit (file length: 882 lines).

| # | Tab Name | Purpose | Backend Pipeline | Status |
|---|----------|---------|-----------------|--------|
| 1 | Runtime | Action execution + quick-action toggles (Credits, Fog, AI, God Mode, One Hit Kill, Unit Cap, Instant Build, Freeze Timer) | `SdkOperationRouter` + `ActionSymbolRegistry` + `SymbolTacticalGodMode` | LEGACY-BROKEN |
| 2 | Live Ops | Selected-unit transaction lab (HP/shield/speed/damage/cooldown/vet/owner draft); spawn preset execution; action reliability refresh | `SelectedUnitTransactionService` / `SpawnPresetService` / `ActionReliabilityService` (internal, non-bridge) | STALE — services are memory-editor vintage; many depend on symbols that no longer resolve |
| 3 | Save Editor | Load / parse / patch / validate save files; apply savepatch packs | `SaveCodec` / `SavePatchApplyService` (file-based, NOT bridge) | USEFUL (orthogonal to bridge) |
| 4 | Profiles & Updates | Profile list + GitHub profile update poll | `IProfileRepository` / `IProfileUpdateService` | USEFUL (orthogonal to bridge) |
| 5 | Unit Roster | Load + browse v5 roster entries | `IRosterBrowserService` (data-only, reads profile JSON) | STALE — pure read-only view, no bridge calls |
| 6 | Spawner | Enhanced v5 spawn batch execution | `IEnhancedSpawnService` → internally `ILuaBridgeExecutor` | DEAD — service exists and ought to work but UI is wrapped around the legacy router for side effects |
| 7 | Faction Dashboard | Capture per-faction snapshot cards | `IFactionDashboardService` | STALE — returns a stub snapshot, no live bridge call |
| 8 | Planet Manager | Load planets + set planet owner | `IPlanetManagerService` → `SdkOperationRouter` (`set_planet_owner` action) | LEGACY-BROKEN |
| 9 | Fleet Manager | Load fleet composition | `IFleetManagerService` (data-only read) | STALE |
| 10 | Ownership | Transfer unit/planet ownership | `IOwnershipTransferService` → `SdkOperationRouter` | LEGACY-BROKEN |
| 11 | AI Control | Suspend/resume/difficulty for AI | `IAiControlService` → `ILuaBridgeExecutor` (partial) | DEAD — service exists but UI pipes through legacy router on failure path |
| 12 | Camera Director | Record/play camera paths | `ICameraDirectorService` → `ILuaBridgeExecutor` | DEAD |
| 13 | Story Events | Fire story events | `IStoryEventService` → `ILuaBridgeExecutor` (`FireEventAsync`) | DEAD |
| 14 | Damage Log | Poll + summarize combat damage | `IDamageLogService` (internal poll-only) | STALE |
| 15 | Diplomacy | Set faction×faction relation | `IDiplomacyService` → `ILuaBridgeExecutor` | DEAD |
| 16 | Corruption | Apply corruption on planet | `ICorruptionService` → `ILuaBridgeExecutor` | DEAD |

### Status legend

- **USEFUL** — Provides real value; uses a backend that still works.
- **STALE** — Service compiles, UI renders, but the backend is a stub / data-only reader / hasn't been exercised against the current game binary.
- **DEAD** — Tab exists, backend service genuinely routes through `ILuaBridgeExecutor`, but the UI hands control off through a helper-hook path that the current bridge build does not recognize, so nothing happens in-game. **This is the real opportunity.**
- **LEGACY-BROKEN** — UI is wired to `SdkOperationRouter` / `ActionSymbolRegistry` / `SymbolTacticalGodMode` which fail at symbol resolution on the current build.

---

## 2. Button inventory (sampled)

For each tab, the 2–3 most prominent buttons and where they ultimately route.

### Tab 1 — Runtime

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| God Mode | `QuickGodModeCommand` | `QuickGodModeAsync()` in `MainViewModelQuickActionsBase.cs` (line 221) | Builds payload `{ symbol: SymbolTacticalGodMode, boolValue: !active }` and routes through `QuickRunActionAsync(ActionToggleTacticalGodMode, …)` → `SdkOperationRouter` — **LEGACY-BROKEN** |
| One Hit Kill | `QuickOneHitCommand` | `QuickOneHitAsync()` (line 230) | Same as God Mode but `SymbolTacticalOneHitMode` / `ActionToggleTacticalOneHitMode` — **LEGACY-BROKEN** |
| Set Credits | `QuickSetCreditsCommand` | `QuickSetCreditsAsync()` | `QuickRunActionAsync(ActionSetCredits, …)` → `SdkOperationRouter` — **LEGACY-BROKEN** |

### Tab 2 — Live Ops

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Apply Draft | `ApplySelectedUnitDraftCommand` | `ApplySelectedUnitDraftAsync()` | `ISelectedUnitTransactionService.ApplyAsync` — internal transaction record on snapshot diff; does not touch the bridge |
| Capture Unit Baseline | `CaptureSelectedUnitBaselineCommand` | `CaptureSelectedUnitBaselineAsync()` | `ISelectedUnitTransactionService.CaptureBaseline` — reads stale symbols, usually returns empty |
| Refresh Reliability | `RefreshActionReliabilityCommand` | `IActionReliabilityService.GetAllAsync` | Reads in-memory reliability stats; no bridge |

### Tab 3 — Save Editor

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Load Save | `LoadSaveCommand` | `LoadSaveAsync()` | `ISaveCodec.ReadAsync` — local file |
| Apply Patch | `ApplySavePatchCommand` | `ApplySavePatchAsync()` | `ISavePatchApplyService.ApplyAsync` — file-level patch |

### Tab 4 — Profiles & Updates

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Load Profiles | `LoadProfilesCommand` | `LoadProfilesAsync()` | `IProfileRepository.LoadProfilesAsync` — JSON file |
| Check Updates | `CheckProfileUpdatesCommand` | `CheckProfileUpdatesAsync()` | `IProfileUpdateService.GetLatestManifestAsync` — HTTP |

### Tab 5 — Unit Roster

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Load Roster | `LoadRosterCommand` | `LoadRosterAsync()` | `IRosterBrowserService.LoadRosterAsync` — reads profile data |

### Tab 6 — Spawner

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Spawn | `ExecuteEnhancedSpawnCommand` | `ExecuteEnhancedSpawnAsync()` | `IEnhancedSpawnService.ExecuteSpawnAsync` → internally `ILuaBridgeExecutor` — **DEAD** (UI-wired, but default payload attempts helper-hook path that is currently stubbed) |

### Tab 7 — Faction Dashboard

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Capture Snapshots | `CaptureFactionSnapshotsCommand` | `CaptureFactionSnapshotsAsync()` | `IFactionDashboardService.CaptureSnapshotsAsync` — returns a static stub snapshot — **STALE** |

### Tab 8 — Planet Manager

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Load Planets | `LoadPlanetsCommand` | `LoadPlanetsAsync()` | `IPlanetManagerService.LoadPlanetsAsync` — stub |
| Set Owner | `SetPlanetOwnerCommand` | `SetPlanetOwnerAsync()` | `IPlanetManagerService.SetPlanetOwnerAsync` → `SdkOperationRouter` — **LEGACY-BROKEN** |

### Tab 9 — Fleet Manager

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Load Fleets | `LoadFleetsCommand` | `LoadFleetsAsync()` | `IFleetManagerService.LoadFleetsAsync` — stub |

### Tab 10 — Ownership

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Transfer Ownership | `TransferOwnershipCommand` | `TransferOwnershipAsync()` | `IOwnershipTransferService.TransferOwnershipAsync` → `SdkOperationRouter` — **LEGACY-BROKEN** |

### Tab 11 — AI Control

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Suspend AI | `SuspendAiCommand` | `SuspendAiAsync()` | `IAiControlService.ExecuteAiControlAsync` → `ILuaBridgeExecutor` — **DEAD** (helper-hook path) |

### Tab 12 — Camera Director

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Capture Keyframe | `CaptureCameraKeyframeCommand` | `CaptureCameraKeyframeAsync()` | `ICameraDirectorService.ExecuteCameraCommandAsync` → `ILuaBridgeExecutor` — **DEAD** |

### Tab 13 — Story Events

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Load Events | `LoadStoryEventsCommand` | `LoadStoryEventsAsync()` | `IStoryEventService.LoadEventsAsync` — data read |
| Fire Event | `FireStoryEventCommand` | `FireStoryEventAsync()` | `IStoryEventService.FireEventAsync` → `ILuaBridgeExecutor` — **DEAD** |

### Tab 14 — Damage Log

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Poll Entries | `PollDamageLogCommand` | `PollDamageLogAsync()` | `IDamageLogService.PollEntriesAsync` — polls internal buffer, no bridge |

### Tab 15 — Diplomacy

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Set Relation | `SetDiplomacyRelationCommand` | `SetDiplomacyRelationAsync()` | `IDiplomacyService.SetRelationAsync` → `ILuaBridgeExecutor` — **DEAD** |

### Tab 16 — Corruption

| Button | Command binding | Invokes | Backend path |
|---|---|---|---|
| Set Corruption | `SetCorruptionCommand` | `SetCorruptionAsync()` | `ICorruptionService.SetCorruptionAsync` → `ILuaBridgeExecutor` — **DEAD** |

---

## 3. Service reach (dark services)

Grep of `src/SwfocTrainer.Core/Services/*.cs` cross-referenced against `src/SwfocTrainer.App/**/*.xaml` for their interface names.

| Service (interface) | ViewModel refs (C# files) | XAML refs (actual UI bindings) | Verdict |
|---|---|---|---|
| `IGodModeService` | 5 | **0** | DARK (bridge call exists, no button) |
| `IOneHitKillService` | 5 | **0** | DARK |
| `IEconomyService` | 23 | **0** | DARK |
| `IHeroRespawnService` | 8 | **0** | DARK |
| `IUnitInspectorService` | 6 | **0** | DARK |
| `IHardpointService` | 5 | **0** | DARK |
| `ICrashAnalyzerService` | 5 | **0** | DARK |
| `IMaphackService` | 8 | **0** | DARK |
| `IFactionSwitchService` | 22 | **0** | DARK (registered in DI, unused in UI) |
| `ICorruptionService` | 28 | **0** | DARK |
| `IDiplomacyService` | 28 | **0** | DARK |
| `IStoryEventService` | 28 | **0** | DARK |
| `IAiControlService` | 22 | **0** | DARK |
| `ICooldownManagerService` | 22 | **0** | DARK |
| `ICameraDirectorService` | 22 | **0** | DARK |
| `IOwnershipTransferService` | 22 | **0** | DARK |
| `IPlanetManagerService` | 28 | **0** | DARK |
| `IFleetManagerService` | 22 | **0** | DARK |
| `IDamageLogService` | 28 | **0** | DARK |
| `IModConflictDetectorService` | 22 | **0** | DARK |
| `IRosterBrowserService` | 22 | **0** | DARK |
| `IFactionDashboardService` | 22 | **0** | DARK |
| `IEnhancedSpawnService` | 22 | **0** | DARK |

**23 out of 23 sampled services have zero direct XAML bindings.** ViewModel "references" are exclusively via the `MainViewModelDependencies` struct and factory injection (see `MainViewModelDependencies.cs`). The commands eventually constructed from these services do exist, but their button-side bindings route through the `ActionId`-based `QuickRunActionAsync` / `ExecuteActionCommand` pattern that resolves against the symbol registry. The symbol registry failing is why nothing reaches the bridge.

### The "8 missing from DI" problem

The 8 bridge-direct services below are NOT even registered in `src/SwfocTrainer.App/Program.cs`. They are present in source, instantiable, and have contracts (`IBridgeHelperServices.cs`), but `Program.cs::RegisterCoreServices` does not include them:

- `IGodModeService`
- `IOneHitKillService`
- `IEconomyService`
- `IHeroRespawnService`
- `IUnitInspectorService`
- `IHardpointService`
- `ICrashAnalyzerService`
- `IMaphackService`

**V2 must register these itself** (Program.cs addition or manual instantiation in `MainWindowV2.xaml.cs`). Agent A and Agent B are not expected to touch this.

---

## 4. Legacy pipeline reach

Grep results for `SdkOperationRouter` / `ActionSymbolRegistry` / `SymbolTacticalGodMode` across the editor source tree (non-test files).

### In `src/SwfocTrainer.App/`:

- `src/SwfocTrainer.App/Program.cs:119` — DI registration `services.AddSingleton<ISdkOperationRouter, SdkOperationRouter>();` (still needed for the legacy UI fallback)
- `src/SwfocTrainer.App/ViewModels/MainViewModelDefaults.cs:25` — `internal const string SymbolTacticalGodMode = "tactical_god_mode";`
- `src/SwfocTrainer.App/ViewModels/MainViewModelDefaults.cs:65` — `[ActionToggleTacticalGodMode] = SymbolTacticalGodMode,` (action→symbol lookup table)
- `src/SwfocTrainer.App/ViewModels/MainViewModelCoreStateBase.cs:46` — `protected const string SymbolTacticalGodMode = MainViewModelDefaults.SymbolTacticalGodMode;` (re-export)
- `src/SwfocTrainer.App/ViewModels/MainViewModelQuickActionsBase.cs:225-228` — Quick-action `QuickGodModeAsync()` builds payload with `SymbolTacticalGodMode` and routes through `QuickRunActionAsync` → `SdkOperationRouter`

### In `src/SwfocTrainer.Core/`:

- `src/SwfocTrainer.Core/Services/SdkOperationRouter.cs` — implementation
- `src/SwfocTrainer.Core/Services/ActionSymbolRegistry.cs` — implementation
- `src/SwfocTrainer.Core/Contracts/ISdkOperationRouter.cs` — contract
- `src/SwfocTrainer.Core/Services/ActionReliabilityService.cs` — consumes `ISdkOperationRouter` for reliability probes

### In `src/SwfocTrainer.Runtime/`:

- `src/SwfocTrainer.Runtime/Services/RuntimeAdapter.cs` — consumes `ISdkOperationRouter`
- `src/SwfocTrainer.Runtime/Services/RuntimeAdapter.State.cs` — same
- `src/SwfocTrainer.Runtime/Services/ModMechanicDetectionService.cs` — same

### In test projects:

- `tests/SwfocTrainer.Tests/Core/SdkOperationRouterTests.cs`
- `tests/SwfocTrainer.Tests/Core/CoreWave6Tests.cs`
- `tests/SwfocTrainer.Tests/Core/CoreWave11CoverageTests.cs`
- `tests/SwfocTrainer.Tests/Core/CoreWave8bCoverageTests.cs`
- `tests/SwfocTrainer.Tests/Core/CoreWave8CoverageTests.cs`
- `tests/SwfocTrainer.Tests/Runtime/RuntimeAdapterAsyncWave7Tests.cs`
- `tests/SwfocTrainer.Tests/Runtime/RuntimeAdapterWave6Tests.cs`
- `tests/SwfocTrainer.Tests/Core/CoreWave2CoverageTests.cs`
- `tests/SwfocTrainer.Tests/App/MainViewModelDefaultsWave5Tests.cs`

**Total reach: 20 files.** The 5 most-important legacy touches are in `src/SwfocTrainer.App/ViewModels/MainViewModel{Defaults,CoreStateBase,QuickActionsBase}.cs`, `src/SwfocTrainer.App/Program.cs`, and the legacy `MainWindow.xaml` command bindings. V2 must touch zero of these.

---

## 5. Summary verdict

**Full rebuild is justified.** The audit evidence:

1. **Zero XAML bindings point at bridge services.** Every one of the 23 bridge-aware services exists in source and is referenced only through the `MainViewModelDependencies` injection record. The actual button bindings (`QuickGodModeCommand`, `ExecuteActionCommand`, etc.) route through `QuickRunActionAsync` / `ExecuteActionAsync`, which both hit `SdkOperationRouter` first and fall back to helper-hook paths that are not wired to the current bridge build.
2. **The 8 newest bridge services (GodMode, OneHitKill, Economy, HeroRespawn, UnitInspector, Hardpoint, CrashAnalyzer, Maphack) are not even in Program.cs DI.** A surgical rewire cannot succeed without editing `Program.cs::RegisterCoreServices`, `MainViewModelDependencies`, the factory context record, and the 21 `MainViewModel*.cs` partial-class files. That blast radius is strictly larger than writing a new window from scratch.
3. **The symbol registry is the chokepoint.** `SymbolTacticalGodMode` / `ActionToggleTacticalGodMode` / `ActionSymbolRegistry` exist to map stable button IDs to the hand-maintained symbol table that was the pre-bridge approach. Every broken button in the user's live test came back with the `"symbol":"invalid"` error from `SdkOperationRouter`. This pipeline will not become correct by adding buttons — it needs to be bypassed.
4. **The correct pipeline already exists.** `LuaBridgeExecutor` + `NamedPipeLuaBridgeClient` are production-quality, unit-tested, and directly call `\\.\pipe\swfoc_bridge`. The per-feature services (`GodModeService.cs`, `OneHitKillService.cs`, etc.) already wrap them correctly. What's missing is the six-tab window that actually uses them.
5. **The previous session's B3 refusal was correct.** A surgical fix inside the legacy MainViewModel would have touched 30+ files and required re-plumbing the symbol registry. Building `MainWindowV2` alongside, with a launch switch for fallback, preserves the legacy UI while delivering a working alternative.

**Recommendation:** ship the V2 rebuild, validate against the live bridge in the next session, then execute the quarantine plan below once a human has clicked at least one V2 button successfully in-game.

---

## Quarantine plan for legacy pipeline

The following files should be moved to `legacy_to_delete/` **after V2 is validated live** (not this session). Do not move these files yet.

### Core legacy services (delete after V2 validated)

1. `src/SwfocTrainer.Core/Services/SdkOperationRouter.cs`
2. `src/SwfocTrainer.Core/Services/ActionSymbolRegistry.cs`
3. `src/SwfocTrainer.Core/Contracts/ISdkOperationRouter.cs`

### Legacy view-model spaghetti (21 partial files)

The `MainViewModel` partial class lives across 21 files. All of them must be deleted together — they form a single `partial class MainViewModel` and cannot be removed piecemeal without compile errors.

4. `src/SwfocTrainer.App/ViewModels/MainViewModel.cs`
5. `src/SwfocTrainer.App/ViewModels/MainViewModelAttachHelpers.cs`
6. `src/SwfocTrainer.App/ViewModels/MainViewModelBindableMembersBase.cs`
7. `src/SwfocTrainer.App/ViewModels/MainViewModelCoreStateBase.cs`
8. `src/SwfocTrainer.App/ViewModels/MainViewModelCreditsHelpers.cs`
9. `src/SwfocTrainer.App/ViewModels/MainViewModelDefaults.cs`
10. `src/SwfocTrainer.App/ViewModels/MainViewModelDependencies.cs`
11. `src/SwfocTrainer.App/ViewModels/MainViewModelDiagnostics.cs`
12. `src/SwfocTrainer.App/ViewModels/MainViewModelDraftBuildResult.cs`
13. `src/SwfocTrainer.App/ViewModels/MainViewModelFactories.cs`
14. `src/SwfocTrainer.App/ViewModels/MainViewModelHotkeyHelpers.cs`
15. `src/SwfocTrainer.App/ViewModels/MainViewModelLiveOpsBase.cs`
16. `src/SwfocTrainer.App/ViewModels/MainViewModelPayloadHelpers.cs`
17. `src/SwfocTrainer.App/ViewModels/MainViewModelQuickActionHelpers.cs`
18. `src/SwfocTrainer.App/ViewModels/MainViewModelQuickActionsBase.cs`
19. `src/SwfocTrainer.App/ViewModels/MainViewModelRuntimeModeOverrideHelpers.cs`
20. `src/SwfocTrainer.App/ViewModels/MainViewModelSaveOpsBase.cs`
21. `src/SwfocTrainer.App/ViewModels/MainViewModelSelectedUnitDraftHelpers.cs`
22. `src/SwfocTrainer.App/ViewModels/MainViewModelSelectedUnitParsingHelpers.cs`
23. `src/SwfocTrainer.App/ViewModels/MainViewModelSpawnHelpers.cs`
24. `src/SwfocTrainer.App/ViewModels/MainViewModelV5FeaturesBase.cs`

### Legacy WPF window

25. `src/SwfocTrainer.App/MainWindow.xaml`
26. `src/SwfocTrainer.App/MainWindow.xaml.cs`

### Legacy-pipeline-only tests

These tests exclusively cover `SdkOperationRouter` / `ActionSymbolRegistry` / `SymbolTacticalGodMode`. Other tests that merely mention those symbols while testing something else should NOT be deleted — verify by grep before moving.

27. `tests/SwfocTrainer.Tests/Core/SdkOperationRouterTests.cs` — named after the target, exclusively legacy

**Tests that are NOT candidates for deletion** (they exercise adjacent systems and only reference the legacy symbols through transitive dependencies):

- `tests/SwfocTrainer.Tests/Core/CoreWave6Tests.cs`
- `tests/SwfocTrainer.Tests/Core/CoreWave11CoverageTests.cs`
- `tests/SwfocTrainer.Tests/Core/CoreWave8bCoverageTests.cs`
- `tests/SwfocTrainer.Tests/Core/CoreWave8CoverageTests.cs`
- `tests/SwfocTrainer.Tests/Runtime/RuntimeAdapterAsyncWave7Tests.cs`
- `tests/SwfocTrainer.Tests/Runtime/RuntimeAdapterWave6Tests.cs`
- `tests/SwfocTrainer.Tests/Core/CoreWave2CoverageTests.cs`
- `tests/SwfocTrainer.Tests/App/MainViewModelDefaultsWave5Tests.cs`

These will lose coverage when the legacy code is deleted. Re-verify their still-relevant assertions against V2 code during quarantine execution.

### Quarantine execution order (future session)

1. Confirm V2 has been used live against the bridge (human click-test).
2. Delete legacy tests marked "exclusively legacy" above.
3. Delete the 21 `MainViewModel*.cs` partial files together, then `MainWindow.xaml` + `MainWindow.xaml.cs`.
4. Delete `SdkOperationRouter.cs`, `ActionSymbolRegistry.cs`, `ISdkOperationRouter.cs`.
5. Run `dotnet build --no-incremental --verbosity normal` and fix transitive breakages in `RuntimeAdapter.cs`, `ActionReliabilityService.cs`, `ModMechanicDetectionService.cs`.
6. Simplify `Program.cs::RegisterCoreServices` — drop the DI registration and the `MainViewModelDependencies` wiring.
7. Remove the `--legacy-ui` flag from V2's `Main()` and make V2 the only window.
8. Re-run tests. Expect ~10 deletions in `SdkOperationRouterTests.cs` and friends.
