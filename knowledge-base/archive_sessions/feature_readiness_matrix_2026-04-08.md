# SWFOC Editor Feature Readiness Matrix

**Generated:** 2026-04-08
**Author:** Autonomous session (Claude Code Opus 4.6 [1M], swfoc_memory orchestrator)
**Scope:** All 23 services in `src/SwfocTrainer.Core/Services/` that wrap a Lua-bridge feature.
**Method:** Mechanical inspection of files on disk — UI bindings, unit tests, replay tests,
bridge harness test suites, and end-to-end probe shape.

## Column legend

| Column | Meaning | Pass criterion |
|---|---|---|
| **UI wired?** | Does an editor button or ViewModel reference the service interface (`IXxxService`)? | Yes if `src/SwfocTrainer.App/**/*.{cs,xaml}` references the interface or class. |
| **Unit test** | `tests/SwfocTrainer.Tests/Core/<Service>Tests.cs` exists with `[Fact]` methods. | OK if file exists. |
| **Replay test** | `tests/SwfocTrainer.Tests/Replay/<Service>ReplayTests.cs` exists. | OK if file exists. |
| **Bridge harness test** | `swfoc_lua_bridge/test_harness.cpp` exercises the underlying `Lua_*` helper. | OK if `Lua_<Helper>` is invoked from a TEST function. |
| **End-to-end probe** | A test starts `swfoc_replay.exe`, sends the service's command, and reads back a *meaningful* post-state via a `SWFOC_Replay*` helper. | OK only when the post-state probe is service-specific (not the generic `SWFOC_GetVersion()` liveness probe). |
| **Status** | One of READY, NEEDS-UI, NEEDS-E2E, NEEDS-REPLAY-HELPER, BLOCKED-MEMORY, BLOCKED-LIVE-ONLY. | See definitions in the task spec. |

## Symbols

- **OK** — column requirement met
- **--** — column requirement not met
- **n/a** — column does not apply (e.g. service does not call the bridge)

---

## Matrix

| # | Feature | Service | UI wired? | Unit test | Replay test | Bridge harness test | End-to-end probe | Status |
|---|---|---|---|---|---|---|---|---|
| 1 | AI suspend / control | `AiControlService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 2 | Camera director / cinematics | `CameraDirectorService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 3 | Cooldown reset | `CooldownManagerService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 4 | Corruption events | `CorruptionService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 5 | Damage logging | `DamageLogService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 6 | Diplomacy (Make_Ally / Make_Enemy) | `DiplomacyService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 7 | Enhanced spawn | `EnhancedSpawnService` | OK | OK | OK | -- | OK | NEEDS-E2E (partial) |
| 8 | Faction dashboard | `FactionDashboardService` | OK | OK | OK | -- | OK | NEEDS-E2E (partial) |
| 9 | Faction switch (human player) | `FactionSwitchService` | OK | OK | OK | -- | -- | BLOCKED-MEMORY |
| 10 | Fleet manager / Assemble_Fleet | `FleetManagerService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 11 | Mod conflict detector (XML scan) | `ModConflictDetectorService` | OK | OK | OK | n/a | n/a | READY |
| 12 | Ownership transfer | `OwnershipTransferService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 13 | Planet manager (Change_Owner) | `PlanetManagerService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 14 | Roster browser | `RosterBrowserService` | OK | OK | OK | -- | OK | NEEDS-E2E (partial) |
| 15 | Story event firing | `StoryEventService` | OK | OK | OK | -- | -- | NEEDS-REPLAY-HELPER |
| 16 | Tactical god mode hook | `GodModeService` (-> `SWFOC_GodMode`) | -- | OK | -- | OK | -- | NEEDS-UI |
| 17 | One-hit kill hook | `OneHitKillService` (-> `SWFOC_OneHitKill`) | -- | OK | -- | OK | -- | NEEDS-UI |
| 18 | Credits / tech / drain | `EconomyService` (slot-aware credit/tech) | -- | OK | -- | OK | -- | NEEDS-UI |
| 19 | Fog reveal (FOWManager) | `MaphackService` (-> `FOWManager.Reveal_All`) | -- | OK | -- | -- | -- | NEEDS-UI |
| 20 | Unit inspector dump | `UnitInspectorService` (-> `SWFOC_InspectUnit`) | -- | OK | -- | OK | -- | NEEDS-UI |
| 21 | Hardpoint enumeration | `HardpointService` (-> `SWFOC_GetHardpoints`) | -- | OK | -- | OK | -- | NEEDS-UI |
| 22 | Hero respawn | `HeroRespawnService` (-> `SWFOC_SetHeroRespawn` + `SWFOC_HeroInstantRespawn`) | -- | OK | -- | OK | -- | NEEDS-UI |
| 23 | Snapshot dump / crash analyzer | `CrashAnalyzerService` (-> `SWFOC_DumpState`) | -- | OK | -- | OK | -- | NEEDS-UI |

---

## Status summary

| Status | Count | Services |
|---|---:|---|
| READY | 1 | ModConflictDetector |
| NEEDS-UI | 8 | GodMode, OneHitKill, Economy, Maphack, UnitInspector, Hardpoint, HeroRespawn, CrashAnalyzer |
| NEEDS-E2E (partial e2e exists) | 3 | EnhancedSpawn, FactionDashboard, RosterBrowser |
| NEEDS-REPLAY-HELPER | 10 | AiControl, CameraDirector, CooldownManager, Corruption, DamageLog, Diplomacy, FleetManager, OwnershipTransfer, PlanetManager, StoryEvent |
| BLOCKED-MEMORY | 1 | FactionSwitch |
| BLOCKED-LIVE-ONLY | 0 | -- |
| **Total** | **23** | -- |

**Headline:** **1 READY, 8 NEEDS-UI, 3 NEEDS-E2E (partial), 10 NEEDS-REPLAY-HELPER, 1 BLOCKED-MEMORY**.

---

## Column-by-column evidence

### UI wired?

- The **15 v5 services** are constructed in
  `src/SwfocTrainer.App/ViewModels/MainViewModelV5FeaturesBase.cs` (constructor
  fields for `_aiControl`, `_cameraDirector`, `_cooldownManager`, `_corruption`,
  `_damageLog`, `_diplomacy`, `_enhancedSpawn`, `_factionDashboard`,
  `_factionSwitch`, `_fleetManager`, `_modConflicts`, `_ownershipTransfer`,
  `_planetManager`, `_rosterBrowser`, `_storyEvents`) and registered in
  `src/SwfocTrainer.App/Program.cs` and
  `src/SwfocTrainer.App/ViewModels/MainViewModelDependencies.cs`.
- **The 8 new bridge helper services** (`GodModeService`, `OneHitKillService`,
  `EconomyService`, `MaphackService`, `UnitInspectorService`, `HardpointService`,
  `HeroRespawnService`, `CrashAnalyzerService`) have **zero references** in
  `src/SwfocTrainer.App/`. The XAML's "God Mode" and "One Hit Kill" quick action
  buttons (`MainWindow.xaml:75` and `MainViewModelQuickActionsBase.cs:221`) are
  wired through the older `ActionSymbolRegistry` -> `SdkOperationRouter` symbol
  pipeline (`ActionToggleTacticalGodMode`/`SymbolTacticalGodMode`) which
  *predates* the new `GodModeService`/`OneHitKillService` helper-wrapper layer
  introduced in session 2026-04-07. They do **not** drive the new service.

### Unit test

All 23 services have a `tests/SwfocTrainer.Tests/Core/<Service>Tests.cs` file
with `[Fact]` methods. Confirmed by directory listing of
`tests/SwfocTrainer.Tests/Core/`.

### Replay test

- Each of the **15 v5 services** has a corresponding
  `tests/SwfocTrainer.Tests/Replay/<Service>ReplayTests.cs` file
  (e.g. `DiplomacyReplayTests.cs`, `FactionSwitchReplayTests.cs`).
- The **8 helper services** have **no** replay-coverage files. Their helpers
  *are* exercised by the C++ bridge harness directly (see next column).

### Bridge harness test

`swfoc_lua_bridge/test_harness.cpp` contains TEST functions that invoke each of
the new SWFOC_* helper functions. Confirmed by grep on the harness:

| Helper | Test sites in `test_harness.cpp` |
|---|---|
| `Lua_SetCredits` | invoked at lines 1335, 1767 |
| `Lua_SetTechLevel` | invoked at lines 1350, 1780 |
| `Lua_SetCreditsForSlot` | invoked at line 2497 |
| `Lua_HeroInstantRespawn` | invoked at lines 1363, 1370 |
| `Lua_SetHeroRespawn` | invoked at lines 2615, 2625, 2633 |
| `Lua_DumpState` | invoked at line 1840 (Test Suite 9) |
| `Lua_InspectUnit` | invoked at lines 2166, 2184 |
| `Lua_GetHardpoints` | invoked at lines 2205, 2221, 2227 |
| `Lua_GodMode` | invoked at lines 2257, 2292, 2389, 2418 |
| `Lua_OneHitKill` | invoked at lines 2328, 2354, 2392, 2435 |

`MaphackService` does **not** have a `Lua_Maphack` helper because the editor
calls the engine's existing `FOWManager.Reveal_All` Lua global directly (see
`MaphackService.cs` remarks). So its bridge-harness column is `--` and that is
correct, not a gap.

The 15 v5 services do **not** have dedicated bridge-harness tests in
`test_harness.cpp`. They drive engine functions (`Make_Ally`, `Story_Event`,
`Spawn_Unit`, etc.) which are validated by the replay harness instead.

### End-to-end probe

Per `knowledge-base/replay_stub_gaps.md`, the only services whose
`PostStateProbe` is service-specific (rather than the generic
`SWFOC_GetVersion()` liveness probe) are:

| Service | Post-state probe used | Verifies |
|---|---|---|
| `EnhancedSpawnService` | `SWFOC_ReplayObjectCount("TIE_Fighter")` | Object count after spawn |
| `FactionDashboardService` | `SWFOC_GetCredits()` | Local-player credits read |
| `RosterBrowserService` | `SWFOC_ReplayPlayerCount()` | Player count |
| `FactionSwitchService` | `SWFOC_GetLocalPlayer()` | Local player slot read (only validates the read path; the write path is BLOCKED-MEMORY) |

Note even these are only "partial" — they confirm a probe round-trip
succeeds, but they do not exercise a full mutate-then-observe loop on the
same data. Until per-service `SWFOC_Replay*` helpers land
(`SWFOC_ReplayDiplomacyState`, `SWFOC_ReplayPlanetOwner`,
`SWFOC_ReplayLastStoryEvent`, etc.), nine v5 services are stuck on
`SWFOC_GetVersion()` as a liveness fallback. These are graded
**NEEDS-REPLAY-HELPER**.

The 8 bridge helper services have **no** `swfoc_replay.exe`-driven probes at
all because they have no replay tests yet (see Replay test column). Their
status is **NEEDS-UI** since the harness already exercises the underlying
helper end-to-end and the next blocking item is wiring them to the editor.

---

## Notes per status code

### READY (1)

`ModConflictDetectorService` is the only service that does not need either a
new replay helper, a UI button, or a memory-level operation. It scans XML files
and emits a structured report; its tests do not require a live game.

### NEEDS-UI (8)

All 8 of the new bridge-helper services have:
- A unit test `<Service>Tests.cs` ([Fact])
- A `Lua_<Helper>` test in `test_harness.cpp` (except `MaphackService`, which
  uses the engine global instead — its `FOWManager` call still has a unit test
  but the bridge-harness column is intentionally `--`).
- Zero `src/SwfocTrainer.App/` references.

The work item is: build a "Bridge Helpers" tab (or fold them into the existing
tabs) in `MainWindow.xaml` and wire `IGodModeService`, `IOneHitKillService`,
`IEconomyService`, `IMaphackService`, `IUnitInspectorService`,
`IHardpointService`, `IHeroRespawnService`, `ICrashAnalyzerService` into a
new `MainViewModelBridgeHelpersBase.cs` file plus DI registration in
`Program.cs`.

### NEEDS-E2E partial (3)

`EnhancedSpawn`, `FactionDashboard`, `RosterBrowser` already have a
service-specific post-state probe but it is **read-only / count-only**. They
need either:
- A `SWFOC_ReplaySimulateSpawn` write seam paired with the existing
  object-count probe, so the test can mutate then observe.
- Or extend `g_replay` with a per-instance ledger so the same probe can
  observe spawn deltas instead of static counts.

### NEEDS-REPLAY-HELPER (10)

All 10 are listed in `replay_stub_gaps.md` Section 3 (Per-service gap log). The
suggested helpers are:
- `SWFOC_ReplayLastAIDirective` (AI control)
- `SWFOC_ReplayCameraState` (camera director)
- `SWFOC_ReplayLastCooldownReset` (cooldown manager)
- `SWFOC_ReplayLastStoryEvent` (corruption + story event)
- Extend `ReplayLoad` matcher for `SWFOC_EventControl(<n>)` (damage log -- trivial)
- `SWFOC_ReplayDiplomacyState(f1, f2)` (diplomacy)
- `SWFOC_ReplayLastFleetAction` (fleet manager)
- `SWFOC_ReplayObjectOwner(targetId)` (ownership transfer)
- `SWFOC_ReplayPlanetOwner(planet)` (planet manager)
- See `replay_stub_gaps.md` for the full signature list.

### BLOCKED-MEMORY (1)

`FactionSwitchService` cannot be implemented in pure Lua. Phase 3 IDA
investigation (2026-04-07) confirmed there is no Lua API for switching the
human-controlled player slot — the operation is a memory write into the
"human player slot" global. The service currently emits a `BLOCKED-NEEDS-MEMORY`
marker via `error()` to surface a clear diagnostic. Unblocking requires:

1. Port the Cheat Engine trainer's AOB scan + memory-write logic into
   `swfoc_lua_bridge/lua_bridge.cpp` as `SWFOC_SetHumanPlayer(slot)`.
2. Update `FactionSwitchService.BuildFactionSwitchLuaCommand` to call
   `SWFOC_SetHumanPlayer(<slot>)`.
3. Add a `SWFOC_ReplayHumanPlayerSlot()` reply helper for the e2e column.

### BLOCKED-LIVE-ONLY (0)

No service is currently graded BLOCKED-LIVE-ONLY. Every service can be
verified at least at the `BuildLuaCommand` shape level via unit tests. The
worst case (FactionSwitch) is BLOCKED-MEMORY, not LIVE-ONLY, because the
issue is a missing memory-write helper, not a missing live game.

---

## Cross-references

- `knowledge-base/v5_service_fixes_applied.md` -- per-service fix history,
  used to drive the regression test pairs.
- `knowledge-base/replay_stub_gaps.md` -- detailed per-service e2e gap log
  and the proposed `SWFOC_Replay*` helper signatures.
- `knowledge-base/verified_facts.json` -- ledger entries
  `rva_lua_make_ally_wrapper`, `rva_lua_make_enemy_wrapper`,
  `rva_teleport_lua_wrapper`, `rva_make_invulnerable_lua_ghidra` (DEPRECATED),
  cited by the regression tests.
- `tests/SwfocTrainer.Tests/Regression/` -- the regression test directory
  added in this session (companion deliverable).
