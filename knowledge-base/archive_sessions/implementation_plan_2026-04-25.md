Sufficient context. Now I'll deliver the comprehensive metaswarm-style implementation plan.

---

# Implementation Plan: SWFOC Trainer Consolidation & Completion

## Overview

Consolidate the two parallel trees (`swfoc_memory/` and `SWFOC editor/`) into a single canonical workspace; converge on a single-version (v3) Lua bridge surface; close the V2 UI parity gap (16 Pure-Core ViewModels not yet bound to XAML); land cross-cutting infrastructure (theme polish, toggle persistence wiring, capability-state badges); ship the AI-brain remediation flow that solves the user's currently broken save (slot 1 EMPIRE stale +0x360); deliver a categorised Phase 2 hook backlog with explicit IDA + frida_runtime cross-validation gates.

## Requirements (lifted from the audit; numbered to anchor traceability)

R1. Build with `dotnet build SwfocTrainer.sln -c Release --no-restore --warnaserror` returns 0 warnings and 0 errors.
R2. `bridge_test_harness.exe` >= 1091 / 0 unchanged.
R3. `python smoke_test_replay.py` 12/12 and `python smoke_test_replay_units.py` 34/34 unchanged.
R4. `cd tools && python -m verifier lint` 0 errors / 0 warnings unchanged.
R5. Editor non-live tests >= 6883 passing; new tests added per work unit.
R6. Single canonical bridge surface for `SWFOC_SetHumanPlayer*` (v3 default; v2 retained as registered fallback only; v1 removed from Lua callers but kept registered for diagnostic curl).
R7. Every V2Vm Pure-Core state has a corresponding XAML tab in `MainWindowV2.xaml` (or an explicit defer-with-reason note in the readiness matrix).
R8. Save / Profiles / Mods workflows reachable from V2 without `--legacy-ui`.
R9. FeatureToggleCoordinator state survives editor restart (StartUp/Shutdown wiring exists and is tested).
R10. Every bridge helper visible in V2 surfaces a state badge from a fixed taxonomy: `Live`, `ReplayVerified`, `Phase2HookPending`, `RequiresLiveSwfoc`, `Unavailable`. Badge data comes from a single source of truth, not per-tab strings.
R11. ThemeService verified visually green in both Light and Dark with no obscured controls; theme switch round-trips through V2Settings.
R12. AI-brain remediation flow exists end-to-end: slot 1 "stuck" save can be repaired without restarting the campaign (NullAiBrain live-verified; AttachAiBrain implemented or explicitly deferred with documented unblock).
R13. F-key debug switch_sides hypothesis answered (does it actually fix the AI-driver split-brain or does it inherit the same bug?).
R14. Phase 2 hooks land only with `tools_consensus.length >= 2` (`ida_pro` + `frida_runtime` minimum) and a regression-pair test for each.
R15. Enemy-unit READ-ONLY discipline holds in every new code path.
R16. DLL redeploy stays gated on "SWFOC closed" + backup preserved; replay-binary verification precedes every live-game change.

## Architecture Changes

### Consolidation target (one of three shapes â€” see Tradeoff D1)

Tentative recommendation pending user decision: `swfoc_memory/` becomes the canonical root; the editor moves under `swfoc_memory/editor/`. Bridge stays at `swfoc_memory/swfoc_lua_bridge/`. Knowledge base, tools, and replay binaries stay where they are.

```
swfoc_memory/
â”œâ”€â”€ editor/                       <- moved from "SWFOC editor/"
â”‚   â”œâ”€â”€ SwfocTrainer.sln
â”‚   â”œâ”€â”€ src/
â”‚   â”œâ”€â”€ tests/
â”‚   â””â”€â”€ ...
â”œâ”€â”€ swfoc_lua_bridge/             <- already here
â”œâ”€â”€ knowledge-base/               <- already here, single source of truth
â”œâ”€â”€ tools/                        <- python suites, fixture library
â”œâ”€â”€ reports/, docs/, re-findings/, archive/  <- as today
â””â”€â”€ README.md (updated to describe the consolidated tree)
```

### Subsystem changes

- `editor/src/SwfocTrainer.App/V2/Infrastructure/CapabilityStatusRegistry.cs` (new): single source of truth for the per-helper status badge taxonomy. Loaded from a JSON file that mirrors the Phase 2 backlog table.
- `editor/src/SwfocTrainer.App/V2/Themes/Dark.xaml` and `Light.xaml`: contrast-audited; obscured ComboBox/DataGrid foregrounds covered by visual-regression screenshots.
- `editor/src/SwfocTrainer.App/V2/MainWindowV2.xaml`: extended from 8 tabs to 14 tabs (or explicit defer-with-reason notes per skipped tab).
- `editor/src/SwfocTrainer.App/V2/ViewModels/`: 8 new XAML-binding ViewModels wrapping the existing Pure-Core V2Vm states.
- `editor/src/SwfocTrainer.Core/Diagnostics/CapabilityStatusCatalog.cs` (new): typed loader for the badge JSON.
- `editor/src/SwfocTrainer.Core/Services/AiControlService.cs`: extended with `NullAiBrainAsync(slot)` and `AttachAiBrainAsync(slot)` (the latter behind an UNVERIFIED gate until R14 is met).
- `swfoc_lua_bridge/lua_bridge.cpp`: `SWFOC_AttachAiBrain` either lands as a live detour (if R14 cleared) or stays a stub with a clearer reason string.
- `tools/frida_harness/probes/ai_brain.json` (new): the frida_runtime probe that resolves the F-key/AI-brain question (Tradeoff D3).
- `knowledge-base/feature_readiness_matrix_2026-04-25.md` (new): refreshed matrix; supersedes `feature_readiness_matrix_2026-04-08.md`.
- `knowledge-base/blocked_items_2026-04-25.md` (new): refreshed blocked-items list with explicit unblock conditions.

---

## Decomposition into Work Units (Epics)

Ten units, lettered A through J. Each is mergeable independently and verifiable through its own test column. Dependencies recorded explicitly. Phase numbers reflect execution order, not unit identity (a low-letter unit can land in a later phase if needed).

### Unit A â€” Repository Consolidation (the move)

**Scope**: Physically merge `SWFOC editor/` into `swfoc_memory/editor/`. Rewrite all relative paths in shipped tooling, README, CLAUDE.md, knowledge-base references. Update `tools/run_editor_tests_v2.ps1`. Update `bridge/` and `tools/frida_harness/` if they reference the editor path. Reconcile any duplicate `.gitignore` / `.editorconfig`.

**Files touched** (representative; not exhaustive):
- New: `swfoc_memory/editor/**` (everything moved verbatim).
- Modified: `swfoc_memory/CLAUDE.md`, `swfoc_memory/README.md`, `swfoc_memory/STATUS.md`, `swfoc_memory/.remember/now.md`, `swfoc_memory/tools/run_editor_tests_v2.ps1`, `swfoc_memory/tools/frida_harness/run_autonomous.py` (path-from-cwd transforms â€” see CLAUDE.md gotcha about `_` -> `-`).
- Deleted (post-move): `SWFOC editor/` empty shell once everything is verified moved.
- New: `swfoc_memory/MIGRATION_2026-04-25.md` documenting the rename map for forensic clarity.

**Definition of Done**:
- [ ] All editor-side `dotnet test` runs invoked via `powershell -File tools/run_editor_tests_v2.ps1` from the consolidated root pass at >= the pre-move count.
- [ ] `python -m verifier lint` 0/0.
- [ ] `bridge_test_harness.exe` >= 1091/0.
- [ ] `python smoke_test_replay.py` 12/12, `python smoke_test_replay_units.py` 34/34.
- [ ] No file path in any tracked `.md`, `.json`, `.cs`, `.cpp`, `.py`, `.ps1` file references the old `SWFOC editor/` path.
- [ ] CLAUDE.md and `.remember/now.md` updated to match the new layout.

**Blocks**: D1 (the placement decision).

**Risk**: Low. Pure rename + path-rewrite. Risk is path-shaped: missed references in cross-directory tools cause silent regressions. Mitigation: run the full verification quartet (R1-R5) end-to-end after the move.

**Estimated complexity**: Low (mechanical), but bounded by the breadth of search-and-replace.

---

### Unit B â€” Build Warnings to Zero (the gate)

**Scope**: Drive `dotnet build SwfocTrainer.sln -c Release --no-restore --warnaserror` to 0 warnings 0 errors. Catalog every warning suppressed today and choose: fix or annotate-with-reason.

**Known offenders to investigate first**:
1. `editor/src/SwfocTrainer.App/V2/Infrastructure/ThemeService.cs:14`: `<see cref="ApplyPreference"/>` cref. The `ApplyPreference` method exists once so CS1574 is unlikely; CS1734 (no XML param doc match) is more probable. Fix: add explicit `(SwfocTrainer.App.V2.Infrastructure.ThemePreference)` overload signature OR collapse to `<see cref="ApplyPreference(ThemePreference)"/>`.
2. CS3001/CS3003 (CLS-noncompliant `ulong` in public API). The user's CLAUDE.md says: public API exposing pointer addresses must use `long` (CLS-compliant). Confirmed offenders observed in `Core.V2Vm.CombatTabState.SelectedObjAddr`, `Core.V2Vm.SpeedTabState.SelectedObjAddr`, `Core.V2Vm.HeroLabTabState.SelectedHeroAddr`, `Core.V2Vm.UnitStatEditorState.SetUnitFieldAsync(ulong, ...)`, `Core.V2Vm.EventStreamViewState.{TimestampMs,ObjAddr,ObjAddrFilter}`, `Core.V2Vm.InspectorTabState.InspectUnitAsync`, `Core.V2Vm.HeroLabTabState.{SetHeroRespawnTimerAsync,SetPermadeathAsync,KillHeroAsync,ReviveHeroAsync,EditHeroStatAsync}`, `Core.V2Vm.CrossFactionRecruitmentState.TransferOwnershipAsync`, `Core.Models.TacticalUnitSelection.{ObjAddr,ApplySelection,Parser.Parse}`, and likely several `Core.Services.*` helpers.
3. Other XML doc warnings (CS1570 malformed XML, CS1591 missing-on-public).
4. Nullable-reference warnings (CS8xxx) that may have accumulated under a non-strict project.

**Approach** (TDD-friendly):
1. Add a baseline warning-snapshot test under `editor/tests/SwfocTrainer.Tests/Build/WarningBaselineTests.cs` that:
   - Shells out to `dotnet build` with `--warnaserror`,
   - Captures the (currently failing) exit code as the baseline,
   - Fails the test until exit code is 0 with 0 warnings.
   This makes the "drive to zero" loop a green bar instead of a manual chore.
2. Walk every offender with the user's CLAUDE.md rule: pointer addresses use `long` in public surface; parse as `ulong` internally; bounds-check against `long.MaxValue` before casting. For each public `ulong objAddr` parameter, change the parameter to `long`, do `var unsigned = (ulong)objAddr;` inside the method body. Keep internal/private members as `ulong` if they don't cross CLS boundaries.
3. For every fix, add or update unit tests to cover the cast boundary (negative `long` value should throw `ArgumentOutOfRangeException` with a clear message).
4. For deliberate suppressions, use `#pragma warning disable CSXXXX` with a same-line `// Reason: ...` comment, never project-level suppressions.

**Files touched**: every file flagged by the build. Estimated 15-30 files based on grep above (the 14 V2Vm states + a handful of Core services).

**Definition of Done**:
- [ ] `dotnet build SwfocTrainer.sln -c Release --no-restore --warnaserror` returns exit code 0 with no warnings emitted.
- [ ] `WarningBaselineTests` passes.
- [ ] No new public method exposes `ulong` (private/internal `ulong` allowed).
- [ ] All explicit `#pragma warning disable` carry a one-line reason comment.
- [ ] No coverage regression on the touched files.

**Blocks**: A (consolidation) ideally lands first so paths in suppressions and test ledgers are stable.

**Risk**: Medium. The `ulong`-to-`long` API change is signature-breaking for any internal caller. Mitigation: change internal callers in the same commit; the test suite catches accidental breakage.

**Estimated complexity**: Medium. Mostly mechanical, but each casting-boundary needs a guard test.

---

### Unit C â€” Stale Test Cleanup + V1/V2/V3 Convergence

**Scope**: Make `SWFOC_SetHumanPlayer_v3` the canonical caller; v2 stays a registered diagnostic fallback; v1 stays registered but no Lua emitter calls it. Remove stale comments. Update the regression file.

**Sub-tasks**:
1. `editor/tests/SwfocTrainer.Tests/Regression/FactionSwitchServiceRegressionTests.cs:79` â€” `Regression_VersionOneShape_NotGenerated` has stale phrasing ("v2 must be emitted") that doesn't match the v3-canonical reality. Update doc-comments and assertions to: "v3 must be emitted; v1 and v2 forms must NOT appear."
2. `editor/src/SwfocTrainer.Core/Services/FactionSwitchService.cs:51` â€” XML doc still names "the SWFOC_SetHumanPlayer_v2 bridge helper" in the summary block. Update to v3 with a v2-fallback note.
3. Add a new regression `Regression_VersionTwoShape_NotGenerated` mirroring the v1 test for the v2 form. The v1 and v2 emitters are now both forbidden by the canonical Lua emitter; only v3.
4. Ensure `editor/src/SwfocTrainer.App/V2/MainWindowV2.xaml:204` (the "Switch to selected slot (v3 + AI swap)" button) and any other v3-only UI label remains accurate.
5. Audit `editor/src/SwfocTrainer.Core/Diagnostics/BuildLuaCommandInventory.cs` to confirm only v3 appears for FactionSwitch; keep v2 in the inventory but tagged as `RealEngine` Fallback path.

**Files touched** (5-7 files):
- `editor/src/SwfocTrainer.Core/Services/FactionSwitchService.cs`
- `editor/tests/SwfocTrainer.Tests/Regression/FactionSwitchServiceRegressionTests.cs`
- `editor/src/SwfocTrainer.Core/Diagnostics/BuildLuaCommandInventory.cs`
- `editor/src/SwfocTrainer.App/V2/MainWindowV2.xaml` (toolTip strings)
- `editor/src/SwfocTrainer.App/V2/ViewModels/PlayerStateTabViewModel.cs`

**Definition of Done**:
- [ ] FactionSwitchServiceRegressionTests has 8 tests: 4 negative (no_set_context_allegiance, no_BLOCKED_marker, no_v1_shape, **new** no_v2_shape), 1 positive (v3_emitted), 6 InlineData faction mappings (canonical + 3 aliases each), 1 unknown_faction.
- [ ] `BuildLuaCommandInventory` enumerates only v3 as the live faction-switch path; v2 + v1 tagged as `RealEngine` Fallback.
- [ ] No editor source file or XAML references `SWFOC_SetHumanPlayer_v2(` or `SWFOC_SetHumanPlayer(` (raw v1) as the live path.
- [ ] All editor tests still green.

**Blocks**: B (no warnings means baseline test is green and we can iterate on Regression tests without noise).

**Risk**: Low. Pure cleanup.

**Estimated complexity**: Low.

---

### Unit D â€” V2 ViewModel + XAML Wiring (parity for the 16 Pure-Core states)

**Scope**: Map every Pure-Core V2Vm state to either (a) a V2 XAML tab and matching App-side ViewModel, or (b) an explicit defer-with-reason in the readiness matrix.

**Mapping** (Pure-Core state -> V2 binding ViewModel -> tab):
| Pure-Core state (Core/V2Vm/) | V2 Binding ViewModel (App/V2/ViewModels/) | XAML tab |
|---|---|---|
| EconomyTabState | Already wired (EconomyTabViewModel) | Tab 8 (exists) |
| TacticalUnitsFilterTabState | Already wired (TacticalUnitsFilterTabViewModel) | Tab 7 (exists) |
| **CombatTabState** | **NEW** CombatTabViewModel | NEW Tab 9 |
| **SpeedTabState** | **NEW** SpeedTabViewModel | NEW Tab 10 |
| **InspectorTabState** | **NEW** InspectorTabViewModel | (extend Tab 3 Unit Control) |
| **SpawningTabState** | **NEW** SpawningTabViewModel | (extend Tab 3 Unit Control) |
| **GalacticTabState** | **NEW** GalacticTabViewModel | NEW Tab 11 |
| **HeroLabTabState** | **NEW** HeroLabTabViewModel | NEW Tab 12 |
| **BattleControlTabState** | **NEW** BattleControlTabViewModel | NEW Tab 13 |
| **StoryEventsTabState** | **NEW** StoryEventsTabViewModel | (extend Tab 4 World State) |
| **CameraDebugTabState** | **NEW** CameraDebugTabViewModel | NEW Tab 14 |
| **LuaPlaygroundTabState** | **NEW** LuaPlaygroundTabViewModel | (extend Tab 5 Probes) |
| **EventStreamViewState** | **NEW** EventStreamTabViewModel | NEW Tab 15 |
| **DirectorModeState** | **NEW** DirectorModeTabViewModel | NEW Tab 16 |
| **CrossFactionRecruitmentState** | **NEW** CrossFactionTabViewModel | (rolled into Tab 11 Galactic) |
| **UnitStatEditorState** | **NEW** UnitStatEditorTabViewModel | (extend Tab 3 Unit Control) |

10 NEW Binding ViewModels, 8 new top-level tabs, 4 sub-panels in existing tabs. The audit suggests this gap explains the user's "16 vs 8" tab count complaint.

**Sub-tasks per ViewModel** (template; same shape for all 10):
1. Build a `<Name>TabViewModel` in `editor/src/SwfocTrainer.App/V2/ViewModels/` that:
   - Takes the relevant Core service interfaces in the constructor.
   - Owns an instance of the matching `<Name>TabState` from `Core.V2Vm`.
   - Exposes `RelayCommand` properties for each operator action.
   - Subscribes to the shared `IUxFeedbackSink` (from `Core.Ux`) to surface status messages.
   - Subscribes to `FeatureToggleCoordinator` for toggle state propagation.
2. Build the matching XAML panel/tab; bind ItemsSource / IsChecked / Text via DynamicResource styles already in `MainWindowV2.xaml`.
3. Register the ViewModel in `Program.cs::RegisterV2Services`.
4. Add a unit test class `editor/tests/SwfocTrainer.Tests/App/V2/<Name>TabViewModelTests.cs` covering: ctor null guards, command execution, output ring buffer (where applicable), feedback sink integration.

**Files touched**:
- 10 new ViewModels (one per uncovered V2Vm).
- ~50 lines of XAML per tab/sub-panel.
- 10 new test classes.
- `Program.cs::RegisterV2Services` extended with 10 new singletons.
- `MainWindowV2.xaml` extended with 8 new `<TabItem Header="...">` sections.

**Definition of Done**:
- [ ] All 16 V2Vm states are reachable from V2 (either via own tab or sub-panel).
- [ ] Operator can switch to V2 (no `--legacy-ui`) and access every helper that was previously legacy-only.
- [ ] Each NEW ViewModel has a unit test class with >= 80% line coverage.
- [ ] `dotnet build` still 0 warnings/0 errors.
- [ ] `MainWindowV2.xaml` line-count increase audited; if approaching 800 lines (per project convention), split into per-tab UserControl files (`editor/src/SwfocTrainer.App/V2/Views/<Name>Tab.xaml`).

**Blocks**: B (warnings clean). C (v3 canonical) doesn't strictly block but makes the touched ToolTips correct.

**Risk**: Medium-High. WPF DataTemplate / style overrides have surprised this codebase before (see `MainWindowV2.xaml:24-32` ComboBox foreground bug). Mitigation: visual regression screenshots after each tab lands (see Unit I).

**Estimated complexity**: High. Largest unit by raw LOC; ~700-1000 lines of XAML and ~1500 lines of ViewModel code.

---

### Unit E â€” V2 Save / Profiles / Mods Workflows

**Scope**: Lift the Save Editor, Profiles & Updates, and Mods/Helper workflows from `MainWindow.xaml` (legacy) into V2-native tabs that don't depend on the partial-class `MainViewModel` plumbing.

**Why this is a separate unit from D**: The legacy MainViewModel has 6+ `MainViewModelXxxBase.cs` partial classes plus a `MainViewModelDependencies` dependency struct. V2's design rule (see comment in `Program.cs:262`) explicitly says V2 does NOT depend on TrainerOrchestrator, IProfileRepository, SdkOperationRouter, ActionSymbolRegistry, or any MainViewModel partial. So Unit E is "rebuild these workflows fresh against V2BridgeAdapter + the existing Core services," not a drop-in lift.

**Sub-tasks**:
1. **SaveOps tab** â€” `SaveTab.xaml` + `SaveTabViewModel.cs`. Wires `ISaveCodec`, `ISavePatchPackService`, `ISavePatchApplyService` directly. Three sub-panels: Open save / Apply patch / Pack patch.
2. **Profiles tab** â€” `ProfilesTab.xaml` + `ProfilesTabViewModel.cs`. Wires `IProfileRepository`, `IProfileUpdateService`, `IProfileVariantResolver`. Two sub-panels: Active profile picker / Update from manifest.
3. **Mods tab** â€” `ModsTab.xaml` + `ModsTabViewModel.cs`. Wires `IHelperModService`, `IModOnboardingService`, `IModCalibrationService`, `IModDependencyValidator`. Sub-panels: Helper-mod install / Mod onboarding / Calibration / Dependency check.
4. Add per-workflow regression test files following the pair-test pattern (red-on-old-shape / green-on-new-shape) in `editor/tests/SwfocTrainer.Tests/V2/`.

**Files touched**:
- 3 new XAML user-controls or tab definitions.
- 3 new V2 binding ViewModels.
- 6+ new test files (1 happy-path + 1 regression-pair per workflow).
- `Program.cs::RegisterV2Services` (add 3 ViewModels).
- `MainWindowV2.xaml` (add 3 TabItems or split to UserControl).

**Definition of Done**:
- [ ] User can: open a `.sav` file, view and patch fields, save back â€” entirely from V2 (no `--legacy-ui`).
- [ ] User can: switch profiles, fetch updates, see active profile metadata â€” entirely from V2.
- [ ] User can: install/uninstall helper mod, run onboarding, run calibration â€” entirely from V2.
- [ ] Each workflow has a happy-path test + at least one regression-pair test.
- [ ] `dotnet build` clean.
- [ ] Coverage on the 3 ViewModels >= 80%.

**Blocks**: D (V2 ViewModel infrastructure pattern established). B (warnings).

**Risk**: Medium. Save editor in legacy uses `SaveFieldViewItem` (App-layer model) which already depends on `Core.Models.SaveModels`; migration should be straightforward but field-by-field validation in a different XAML scope could expose edge cases.

**Estimated complexity**: Medium-High.

---

### Unit F â€” FeatureToggleCoordinator Wiring + Capability Status Badges

**Scope**: Wire FeatureTogglePersistence into App lifecycle. Build the Capability Status Registry as a single source of truth for badge text. Surface the badge in every V2 helper button or row.

**Sub-task F1 â€” FeatureToggle persistence (the easy half)**:
1. In `Program.cs` after `services.BuildServiceProvider()`, after `ThemeService.ApplyPreference(...)`, call `FeatureTogglePersistence.LoadInto(coordinator, statePath)` where `statePath` is `Path.Combine(appData, "feature_toggles.json")`.
2. Hook `app.Exit` to call `FeatureTogglePersistence.SaveTo(coordinator, statePath)`.
3. Add a smoke test in `editor/tests/SwfocTrainer.Tests/App/AppLifecyclePersistenceTests.cs` that: creates a coordinator, toggles a feature on, calls SaveTo, creates a fresh coordinator, calls LoadInto, asserts the toggled feature comes back enabled.

**Sub-task F2 â€” Capability Status Registry (the harder half)**:
1. Create `editor/profiles/default/capability_status.json` listing every helper from `BuildLuaCommandInventory` plus every bridge helper (cross-reference `lua_bridge.cpp` Lua entry points), one row each:
   ```json
   {
     "helper_id": "SWFOC_SetIncomeMultiplier",
     "kind": "bridge_helper",
     "ida_consensus": ["ida_pro"],
     "frida_consensus": [],
     "replay_verified": true,
     "live_verified": false,
     "phase2_blocker_rva": "0x...",
     "status": "Phase2HookPending"
   }
   ```
   Status values from R10's enum: `Live | ReplayVerified | Phase2HookPending | RequiresLiveSwfoc | Unavailable`.
2. Build `editor/src/SwfocTrainer.Core/Diagnostics/CapabilityStatusCatalog.cs` with a typed loader, identical pattern to `VerifiedFactsLedger`.
3. Add `editor/src/SwfocTrainer.App/V2/Infrastructure/CapabilityStatusBadge.cs` (a tiny `INotifyPropertyChanged` view-helper) and a XAML resource pack `editor/src/SwfocTrainer.App/V2/Themes/Badges.xaml` defining a `BadgeStyle` data-template that maps status values to coloured pills (Green=Live, Blue=ReplayVerified, Amber=Phase2HookPending, Grey=RequiresLiveSwfoc, Red=Unavailable).
4. In every V2 ViewModel, expose a `CapabilityStatusBadge BadgeFor(string helperId)` method that resolves through the registry. In every XAML row that has a button, place the badge next to it.
5. Add a drift-guard test `editor/tests/SwfocTrainer.Tests/Diagnostics/CapabilityStatusCatalogTests.cs` that walks `BuildLuaCommandInventory` and asserts every RealBridge entry has a row in `capability_status.json`. Same drift-guard pattern that already protects the verified_facts.json schema.

**Files touched**:
- New JSON: `editor/profiles/default/capability_status.json`.
- New: `editor/src/SwfocTrainer.Core/Diagnostics/CapabilityStatusCatalog.cs`.
- New: `editor/src/SwfocTrainer.App/V2/Infrastructure/CapabilityStatusBadge.cs`.
- New: `editor/src/SwfocTrainer.App/V2/Themes/Badges.xaml`.
- Modified: every V2 binding ViewModel (small change: expose `BadgeFor`).
- Modified: every V2 tab in `MainWindowV2.xaml` (small XAML insertion per button row).
- New: `editor/tests/SwfocTrainer.Tests/App/AppLifecyclePersistenceTests.cs`.
- New: `editor/tests/SwfocTrainer.Tests/Diagnostics/CapabilityStatusCatalogTests.cs`.

**Definition of Done**:
- [ ] Restarting the editor preserves toggle state (verified by F1 unit test + a manual probe).
- [ ] Every V2 button surfaces a state badge.
- [ ] The badge data is loaded from JSON, not hardcoded in XAML or strings.
- [ ] Drift-guard catches any new helper that lacks a status row.
- [ ] `dotnet build` clean.

**Blocks**: D, E (the badge needs places to render).

**Risk**: Low-Medium. The drift-guard test does most of the safety work.

**Estimated complexity**: Medium.

---

### Unit G â€” Theme Polish + Visual Regression

**Scope**: Verify the just-landed ThemeService renders both palettes correctly. Wire visual regression screenshots into the test pipeline so future XAML changes can't reintroduce dark-on-dark bugs (the ComboBox bug was reported just now in `MainWindowV2.xaml:24-32`).

**Sub-tasks**:
1. Use the `visual-review` skill (per `~/.claude/CLAUDE.md` project notes) to take Playwright screenshots of `MainWindowV2.xaml` at 1280x820 and at minimum size (960x640) in both Dark and Light themes. Also capture the Settings tab dropdown and the PlayerState tab ComboBox open states.
2. Stash the baseline screenshots under `editor/tests/SwfocTrainer.Tests/Visual/baseline/`.
3. Add a `editor/tests/SwfocTrainer.Tests/Visual/V2ThemeVisualTests.cs` that runs the WPF app under a test harness, captures the same screenshots, diffs them against baseline at <= 0.5% pixel difference.
4. Do the same for the 8 new tabs landed in Unit D (Combat, Speed, Galactic, HeroLab, BattleControl, CameraDebug, EventStream, DirectorMode). One screenshot each per theme = 16 baselines.
5. Run the visual harness; confirm no regression. Manually inspect the diff on each baseline to catch any new dark-on-dark or hidden-text issues.

**Files touched**:
- New: `editor/tests/SwfocTrainer.Tests/Visual/V2ThemeVisualTests.cs`.
- New: `editor/tests/SwfocTrainer.Tests/Visual/baseline/*.png` (24+ screenshots).
- Possibly modified: `editor/src/SwfocTrainer.App/V2/Themes/Dark.xaml` + `Light.xaml` if the visual review surfaces any contrast bugs. Check ComboBox, DataGrid, DataGridCheckBoxColumn, DataGridTextColumn header colour, Border background of the green/red "BridgeReady" badge in DiagnosticsTab.
- Possibly modified: `MainWindowV2.xaml` if any inline color swatch needs to move to DynamicResource.

**Definition of Done**:
- [ ] 24+ baseline screenshots captured.
- [ ] V2ThemeVisualTests pass against the baseline.
- [ ] No control on any tab is invisible or low-contrast in either theme.
- [ ] Manual smoke: theme switch round-trips through V2Settings, persists across restart, default `system` correctly follows Windows AppsUseLightTheme.

**Blocks**: D, E (need the new tabs to exist before screenshotting them).

**Risk**: Low. The hard bug-class (ComboBox dark-on-dark) is already caught and fixed; this unit prevents regression.

**Estimated complexity**: Low-Medium (Playwright + WPF capture is fiddly but well-trodden).

---

### Unit H â€” AI Brain Remediation Flow

**Scope**: Solve the user's currently-broken save (slot 1 EMPIRE has stale `+0x360` after old-v2 switch from slot 6 UNDERWORLD). Live-verify `SWFOC_NullAiBrain`. Either implement `SWFOC_AttachAiBrain` or formally defer with concrete unblock criteria.

**Sub-task H1 â€” Live-verify NullAiBrain**:
1. Frida-runtime probe at `lua_bridge.cpp:941` (`Lua_NullAiBrain`). Snapshot `PlayerObject+0x360` at slots 1-7 before the call; call `SWFOC_NullAiBrain(1)`; re-snapshot. Assert previous_ptr returned matches the pre-snapshot value, post-snapshot is null. Add as a new probe in `tools/frida_harness/probes/ai_brain.json`.
2. After live-verification, update `verified_facts.json` entry `rva_player_object_ai_brain_null_helper` (new) with `tools_consensus: ["ida_pro", "frida_runtime", "lua_runtime"]`, evidence the live snapshot diff.
3. Surface the user's specific repair recipe in V2 Tab 2 (Player State): a "Null AI on slot 1 (repair stuck save)" recipe button, hardcoded for the user's exact scenario in the canned-recipes JSON loaded by `LuaPlaygroundTabState`.
4. Add a regression-pair test in `editor/tests/SwfocTrainer.Tests/Regression/AiBrainNullingRegressionTests.cs`.

**Sub-task H2 â€” AttachAiBrain decision** (Tradeoff D3 lives here):
1. **Option H2-a (do it now)**: IDA-side, decompile `AIPlayerClass::ctor` at RVA `0x4AF810`. Identify allocation size, ctor signature (`this`, plus probably `PlayerClass*`, plus probably `HFactionType*` based on the AI subsystem patterns at `0x524CE0` `AIExecutionSystemClass::ctor` and friends in `docs/rvas.md`). Add `SWFOC_AttachAiBrain` as a real implementation in `lua_bridge.cpp`: `malloc(size); call_via_rva(0x4AF810, this_ptr, player_ptr, faction_ptr); player[0x360] = this_ptr;`. Live-verify with the same Frida probe pattern as H1.
2. **Option H2-b (defer)**: Keep the stub. Document the unblock criteria in `knowledge-base/blocked_items_2026-04-25.md`: must IDA-derive size, ctor arity, ctor arg types via Hex-Rays decompile + xref hunt of existing callers at game-startup; must cross-validate with frida_runtime (snapshot a known-AI slot's `+0x360` content and verify it matches the proposed ctor output); must add a regression test.

**Sub-task H3 â€” F-key debug switch_sides RE thread** (the user's question):
1. IDA-side, find the F-key handler for SWFOC's debug build. Search for keyboard input dispatch at the engine's debug-key table; xref `Switch_Sides` (RVA `0x297E80`) for callers that look like a key handler.
2. Decompile the caller. Compare its body with the v3 helper. The user hypothesises the F-key path doesn't touch `+0x360` either; if confirmed, then (a) Petroglyph never solved this, (b) v3 is genuinely the better path, (c) the 16-tab editor surface for the symmetric-case helpers + NullAiBrain is the canonical fix.
3. Frida-runtime probe: Press F-key in a test save with known starting state; snapshot `+0x360` for all slots before and after. Add as `tools/frida_harness/probes/fkey_switch_sides.json`.
4. Document findings in `knowledge-base/fkey_switch_sides_investigation_2026-04-25.md`.

**Files touched**:
- `swfoc_lua_bridge/lua_bridge.cpp` (H2-a only).
- New: `tools/frida_harness/probes/ai_brain.json`, `tools/frida_harness/probes/fkey_switch_sides.json`.
- New: `editor/tests/SwfocTrainer.Tests/Regression/AiBrainNullingRegressionTests.cs`.
- Modified: `knowledge-base/verified_facts.json` (new RVA entries).
- New: `knowledge-base/fkey_switch_sides_investigation_2026-04-25.md`.
- Modified: V2 Tab 2 (PlayerStateTabViewModel.cs + MainWindowV2.xaml around lines 168-249).
- New (recipe): hardcoded recipe in `editor/profiles/default/recipes/repair_stuck_save_slot1.lua`.

**Definition of Done**:
- [ ] User can run "Null AI on slot 1 (repair stuck save)" from V2 Tab 2 and the bridge response confirms previous_ptr was non-null and post-state is null.
- [ ] Live game-test (with user-at-keyboard, gated on R16) confirms slot 1 EMPIRE no longer has the AI driver stuck on it.
- [ ] H2-a OR H2-b: AttachAiBrain is either live-verified-implemented OR explicitly documented with unblock criteria and a stub-with-clearer-error-string.
- [ ] H3: F-key switch_sides hypothesis is answered; investigation document committed.
- [ ] `bridge_test_harness.exe` >= 1091/0.

**Blocks**: B, F (need the badge registry to mark NullAiBrain as Live and AttachAiBrain as either Live or Phase2HookPending).

**Risk**: H1 Low (Frida observation only). H2-a High (untested live-call into engine ctor; can crash the game if size/arity wrong). H2-b Low (status-quo with documentation). H3 Medium (IDA hunt + Frida probe; no live engine modification).

**Estimated complexity**: Medium overall.

---

### Unit I â€” Phase 2 Hook Backlog (the long tail)

**Scope**: Convert each "Phase 2 hook pending" stub from `lua_bridge.cpp` (line numbers 2803, 2819, 2835, 2850, 2878, 2914, 2954, 2965, 2988, 3078, 3147, 3167, 3204, 3235, 3250, 3618, 3626, 3636 â€” 18 stubs total) into a live detour, gated on R14 (multi-tool consensus).

**Approach: catalogue + size + execute in batches**:
1. Build the Phase 2 hook backlog table in `knowledge-base/phase2_hook_backlog_2026-04-25.md`:
   | Helper | RVA target | IDA status | Frida probe status | Replay parity | Risk | Order |
   |---|---|---|---|---|---|---|
   | `SWFOC_SetBuildSpeed` | per-tick build progress incrementer | TBD | TBD | green | Med | 2 |
   | `SWFOC_SetIncomeMultiplier` | per-tick income site | TBD | TBD | green | Med | 3 |
   | `SWFOC_SetGameSpeed` | game-tick scheduler | TBD | TBD | green | High | 6 |
   | `SWFOC_FreezeCredits` | credits-deduction site | TBD | TBD | green | Low | 1 |
   | `SWFOC_ToggleOHKAttackPower` | unit.attack_power offset | TBD | TBD | green | Low | 4 |
   | `SWFOC_SetFireRate` | weapon cooldown reset | TBD | TBD | green | Med | 5 |
   | `SWFOC_AreaDamage` | Take_Damage_Outer splash branch | TBD | TBD | green | High | 8 |
   | `SWFOC_SetTargetFilter` | targeting filter predicate | TBD | TBD | green | High | 9 |
   | `SWFOC_SetUnitField` (offset table) | per-field offset table | TBD | TBD | green | Med | 7 |
   | `SWFOC_SetBuildCost` | credits-deduction site | TBD | TBD | green | Low | 1 (shared with FreezeCredits) |
   | `SWFOC_SetUnitCapOverride` | unit-cap check | TBD | TBD | green | Med | 10 |
   | `SWFOC_FreezeAI` | AI scheduler dispatch | TBD | TBD | green | High | 12 |
   | `SWFOC_FreeCam` | camera singleton pointer chain | TBD | TBD | green | Med | 11 |
   | `SWFOC_SetCameraPos` | camera setter | TBD | TBD | green | Med | 11 |
   | `SWFOC_SetUnitShield` | shield offset | TBD | TBD | green | Low | 4 (shared with attack_power) |
   | `SWFOC_SetUnitSpeed` | locomotor two-deref | TBD | TBD | green | Med | 5 (shared with FireRate) |
   | `SWFOC_SetHeroRespawnTimer` | hero respawn detection | TBD | TBD | green | Med | 13 |
   | `SWFOC_InstantBuild` / `SWFOC_FreeBuild` | AOB-scan build-progress + credits | TBD | TBD | green | High | 14 |

2. For each batch (Order column groups), execute the four-step IDA + Frida + replay + live cycle:
   - IDA: identify the exact instruction site and detour shape. Update `verified_facts.json` with the RVA + IDA tool consensus.
   - Frida: write a runtime probe that observes the site (read pre/post values without writing) on a real game session. Update `verified_facts.json` with Frida tool consensus (R14 satisfied â€” `length >= 2`).
   - Replay parity: confirm the replay-mirror writer in `replay_state.cpp` already exercises the same field. (Per the audit, all 18 helpers already have replay parity.)
   - Implement the detour in `lua_bridge.cpp`. Replace the "Phase 2 hook pending" string with "OK: applied" (or appropriate post-state).
   - Live-test gated on R16 (game closed) + R5 (replay smoke 12/12, 34/34 first).
   - Add a regression-pair test in `editor/tests/SwfocTrainer.Tests/Regression/Phase2HookRegressionTests.cs`: red-on-old-stub, green-on-new-detour.

3. The order column favours: low-risk + offset-table-shared first (FreezeCredits + BuildCost share the credits-deduction site so they unlock together; ShieldEdit + AttackPower share the per-unit field offset model so they unlock together), AI/free-cam last (most engine-state risk).

**Files touched**:
- `swfoc_lua_bridge/lua_bridge.cpp`: 18 detours.
- `knowledge-base/verified_facts.json`: 18+ new RVA entries.
- `knowledge-base/phase2_hook_backlog_2026-04-25.md` (new).
- `tools/frida_harness/probes/phase2_*.json`: 14 batched probe files.
- `editor/tests/SwfocTrainer.Tests/Regression/Phase2HookRegressionTests.cs` (one shared file with 18 facts).

**Definition of Done**:
- [ ] All 18 "Phase 2 hook pending" string-grep results in `lua_bridge.cpp` are gone.
- [ ] Each detour has `tools_consensus: >= 2` in the ledger.
- [ ] All 18 regression-pair tests green.
- [ ] `bridge_test_harness.exe` >= 1091/0 (likely grows by 18+ tests).
- [ ] `python smoke_test_replay.py` 12/12, `python smoke_test_replay_units.py` 34/34.
- [ ] Capability Status Registry updated: all 18 from `Phase2HookPending` to `Live`.
- [ ] V2 UI badges flip to green.

**Blocks**: F (the badge registry). H is recommended first (smaller AI-brain piece sets the live-detour pattern).

**Risk**: High overall, but mitigated by batching. The order column favours unblocking the user's most-asked features first.

**Estimated complexity**: Very High (this is the project's "long tail" â€” 18 stubs each requiring 1-2 hours of IDA + 30 minutes of Frida + 30 minutes of detour code + 30 minutes of test). Estimated 50-80 hours total. Not all of it has to ship in this plan; recommend slicing this unit into Phase 1 (orders 1-5; 6 detours, low-risk) and Phase 2 (orders 6-14; the remainder, deferred).

---

### Unit J â€” Documentation + Knowledge Base Refresh

**Scope**: Bring `STATUS.md`, `feature_readiness_matrix_*.md`, `blocked_items_*.md`, `.remember/now.md`, and `CLAUDE.md` up to date with the consolidated tree, the refreshed tab count, the Phase 2 backlog, and the new readiness states.

**Sub-tasks**:
1. New: `knowledge-base/feature_readiness_matrix_2026-04-25.md` superseding `2026-04-08.md`. Every feature classified per R10's taxonomy. Each row links to the relevant file path in the consolidated tree.
2. New: `knowledge-base/blocked_items_2026-04-25.md`. Each blocked item gets explicit unblock criteria. Items resolved this session are removed.
3. Updated: `STATUS.md` reflecting `bridge_test_harness.exe`, replay smoke counts, editor test count, ledger size, and phase status.
4. Updated: `swfoc_memory/CLAUDE.md` reflecting the consolidated tree and any new "Execution Gotchas" learned during this plan.
5. Updated: `swfoc_memory/.remember/now.md` with a fresh delta table and handoff state.
6. New: `MIGRATION_2026-04-25.md` (if Unit A lands; this is a moot item if D1 leaves things in place).

**Files touched**:
- New: 3 markdown files.
- Updated: 4 markdown files.

**Definition of Done**:
- [ ] All four "anchor" docs (`STATUS.md`, the readiness matrix, the blocked items, `CLAUDE.md`) reference paths from the consolidated tree.
- [ ] No reference to the old `feature_readiness_matrix_2026-04-08.md` exists in any active code path or doc-link.
- [ ] `python -m verifier lint` 0/0 unchanged.

**Blocks**: All other units (it documents what the others changed).

**Risk**: Low.

**Estimated complexity**: Low.

---

## Suggested Execution Order

Layered for dependency safety. Each phase is independently mergeable.

### Phase 1 â€” Foundation (Gate Everything Else)

1. **Unit A â€” Repository consolidation.** Stable paths first. Tradeoff D1 must be resolved before Unit A starts.
2. **Unit B â€” Build warnings to zero.** With paths stable, walk warnings to 0 with `--warnaserror`. The `WarningBaselineTests` keeps the bar from sliding.
3. **Unit C â€” Stale tests + v3 convergence.** Once B is green, the regression-pair pattern is robust. Land C alongside B if both touch the same files.

End-of-Phase-1 acceptance:
- `dotnet build SwfocTrainer.sln -c Release --no-restore --warnaserror` = 0/0.
- All editor tests green.
- All bridge / replay / verifier counts >= baseline.
- Single canonical v3 path; no v1 or v2 emitter.

### Phase 2 â€” V2 UI Surface Completion

4. **Unit D â€” V2 ViewModel + XAML wiring.** 10 new ViewModels; 8 new XAML tabs; 4 sub-panels. Largest single unit; ship as a series of PRs (one per tab).
5. **Unit E â€” V2 Save / Profiles / Mods.** Independent of D's tab count and ordering; can ship in parallel with later D PRs.
6. **Unit F â€” FeatureToggle wiring + Capability badges.** Lands AFTER D and E so the badges have places to render.
7. **Unit G â€” Theme polish + visual regression.** Lands AFTER all new tabs from D and E exist (so baseline screenshots cover the full surface).

End-of-Phase-2 acceptance:
- All 16 V2Vm states reachable from V2 (no `--legacy-ui`).
- Save / Profiles / Mods reachable from V2.
- Restart preserves toggle state.
- Every V2 button surfaces a state badge.
- 24+ visual regression baselines committed and green.

### Phase 3 â€” Engine-State Remediation

8. **Unit H â€” AI brain remediation.** This is the user's biggest pain point right now (broken save). Lands AFTER F so the badge can flip live.
9. **Unit I (Phase 1 only) â€” Phase 2 hooks orders 1-5 (FreezeCredits + BuildCost; AttackPower + Shield; FireRate + Speed).** 6 detours; shared offset patterns; lower risk first.

End-of-Phase-3 acceptance:
- User's stuck save repaired.
- F-key switch_sides hypothesis answered.
- 6 of 18 Phase 2 hooks live.

### Phase 4 â€” Long Tail

10. **Unit I (Phase 2) â€” Phase 2 hooks orders 6-14.** 12 remaining detours; higher risk. Defer if scope-bound; each batch is independently mergeable.
11. **Unit J â€” Documentation refresh.** Lands at the very end so it captures everything.

End-of-Phase-4 acceptance:
- All 18 Phase 2 hooks live.
- Knowledge base up to date.
- Plan complete.

---

## Tradeoffs the User Needs to Decide

These decisions block individual units, not the plan as a whole. Reviewers can scrutinise the plan even before these are made; the user should answer them before Unit A starts.

### D1. Consolidated tree shape

| Option | Pros | Cons |
|---|---|---|
| **A. `swfoc_memory/editor/`** (recommended) | One git history; one CLAUDE.md; one `.remember/`. Bridge + KB + editor in same root. | Editor's existing absolute path `C:\...\SWFOC editor\...` breaks. Some IDE memory and workspace files invalidate. |
| **B. `SWFOC-suite/{bridge,editor,knowledge-base,tools}/`** | Symmetric layout; both roots are equal citizens. | Double migration cost â€” both trees move. Disrupts established CLAUDE.md instructions and tools/run_editor_tests_v2.ps1 even more. |
| **C. Leave as-is** (do nothing) | Zero migration cost. | Long-term confusion; the user already mentioned the parallel-tree problem. Tools and scripts continue carrying double-handling. |

**Recommendation**: A. The `swfoc_memory/` tree is the larger one and contains the canonical knowledge base. The editor is the natural subordinate.

### D2. `--legacy-ui` fate

| Option | Pros | Cons |
|---|---|---|
| **A. Keep as-is until E lands, then delete** (recommended) | Lowest user-disruption; safety net while V2 catches up. | Carries two UI surfaces in the codebase during Phase 2. |
| **B. Delete now** | Forces V2 to be the only path; clears `MainViewModel*` partials and ~6000 LoC. | High risk if Save / Profiles / Mods migration uncovers a gap mid-deletion. |
| **C. Keep forever as a fallback** | Defensive. | Test maintenance overhead; already 6883 tests on the legacy side. |

**Recommendation**: A. Wait until Unit E ships (V2 has Save / Profiles / Mods), then delete `--legacy-ui` + the legacy MainWindow + the partial-class MainViewModel chain in a single later commit. This becomes a **Unit K** (deletion follow-up) added at the end of the plan if the user picks A.

### D3. AttachAiBrain ctor reconstruction

| Option | Pros | Cons |
|---|---|---|
| **A. Implement now (H2-a)** | Closes the AI-brain story end-to-end; gives the user the symmetric remediation. | Untested live-call into engine ctor; small chance of game crash if size/arity wrong. |
| **B. Defer (H2-b)** (recommended for Phase 3) | Lower risk for this plan; document unblock criteria. | The user's "broken slot 1" save still needs the workaround (NullAiBrain only) for one cycle. |

**Recommendation**: B for Phase 3; revisit H2-a as a follow-up after the F-key Frida probe in H3 confirms or rejects the symmetric-case hypothesis.

### D4. Visual review scope

| Option | Pros | Cons |
|---|---|---|
| **A. Snapshot + diff every tab** (recommended) | Catches future contrast regressions. | 24+ baselines = larger repo. |
| **B. Snapshot + diff only the new tabs from Unit D + E** | Smaller repo. | Doesn't catch theme regressions in pre-existing tabs (the ComboBox bug just landed). |
| **C. Manual visual review only** | No infra. | Doesn't survive subsequent edits. |

**Recommendation**: A.

### D5. Unit I phasing

The plan slices Unit I into Phase 1 (orders 1-5; low-risk) and Phase 2 (orders 6-14; deferred). The user should explicitly approve this slice. If the user wants all 18 in this scope, the plan grows by ~50-80 hours; if the user wants only orders 1-5, the plan stays at the 14-hook deferred state.

---

## Risks and Unknowns

### Concrete risks already identified

- **R-A1 (Unit A)**: Some `.csproj` or hint-path file references the absolute path `C:\Users\Prekzursil\Downloads\SWFOC editor\...`. Mitigation: grep before move; verify `dotnet restore` after move.
- **R-B1 (Unit B)**: A CLS-noncompliant `ulong` change cascades into the test fixture types. Mitigation: signature changes done in a single commit per file; test fixture types updated alongside.
- **R-D1 (Unit D)**: The new XAML tabs reach `MainWindowV2.xaml > 800` lines (current 668). Mitigation: split into `editor/src/SwfocTrainer.App/V2/Views/<Name>Tab.xaml` UserControls; the main window references each via `<TabItem><local:CombatTabView/></TabItem>`.
- **R-D2**: The 16 V2Vm states have implicit cross-tab dependencies (e.g. `EventStreamViewState` filter by `ObjAddrFilter` is normally written by the Inspector tab). Mitigation: shared services (`IUxFeedbackSink`, `FeatureToggleCoordinator`) already absorb this; verify each tab's tests pass in isolation.
- **R-F1 (Unit F)**: The capability status JSON drifts from reality. Mitigation: drift-guard test + the badge has a fallback "Unknown" colour (purple) for any helper not in the registry.
- **R-G1 (Unit G)**: Visual regression has flaky pixel diffs because of WPF font hinting at different DPIs. Mitigation: pin DPI to 96 in the test harness; use `<= 0.5%` tolerance; baseline-on-CI not on developer machine.
- **R-H1 (Unit H2-a, only if D3 picks A)**: Crash on AttachAiBrain ctor call. Mitigation: live test in a throwaway save first; backup save preserved per R16.
- **R-I1 (Unit I)**: A Phase 2 detour writes to a wrong offset and corrupts game state. Mitigation: replay-mirror first, Frida observation before write, regression-pair test, R16 game-closed gate.

### Unknowns requiring investigation before implementation

- **U1 (Unit B)**: The actual warning count and severity. The user's audit says "build with `--warnaserror` currently FAILS" but doesn't enumerate. **Spike step**: run `dotnet build` once and dump the warning catalogue to `editor/build/warnings_baseline_2026-04-25.log`. Estimate: 30 minutes.
- **U2 (Unit H3, F-key)**: The F-key handler RVA + decompile + Frida observation. **Concrete simplest probe**:
  1. IDA: open `StarWarsG.exe`. In the strings view, search for "Switch_Sides" or hex `0x297E80`. Find xrefs to that callsite. Filter for callers that look like keyboard input handlers (typically with a `vkCode` parameter or a `WM_KEYDOWN` upstream).
  2. Frida: in `tools/frida_harness/probes/fkey_switch_sides.json`, add a probe that:
     - Snapshots `PlayerObject+0x360` for slots 0-7 by reading `PlayerArray[i] + 0x360`.
     - Hooks the F-key handler entry. Logs entry timestamp.
     - Re-snapshots `+0x360` for all slots after the F-key handler returns.
     - Diffs the two snapshots.
  3. Run probe. Press F-key. If `+0x360` changes for all slots: F-key path DOES touch the AI brain pointer (so v3 + F-key are siblings, both correct). If `+0x360` is unchanged: the user's hypothesis is confirmed; the F-key path has the same dual-control bug; v3 is uniquely correct; this is publishable as a Petroglyph-debugging-story-of-its-own.
- **U3 (Unit I)**: For each of the 18 Phase 2 hooks, the exact RVA + ctor-style detour is unknown. **Approach**: each hook's IDA hunt is scoped at "estimated 1-2 hours" in the backlog table. The plan deliberately doesn't enumerate them yet; it commits only to delivering the table + delivering orders 1-5 in Phase 3.
- **U4 (Unit D)**: Some V2Vm states (e.g. `DirectorModeState`) have inputs that aren't currently surfaced even in legacy. The XAML for those tabs needs a freshly-designed UI rather than a port. **Mitigation**: each ViewModel ships with a unit-test-driven minimum-viable XAML; design polish deferred to a later UX pass if the operator surface is functional.

---

## Coverage / Testing Plan

| Unit | Test Type | Test Target | File(s) |
|---|---|---|---|
| A | Smoke | All tools post-move | full verification quartet (R1-R5) |
| B | Snapshot | Build warnings = 0 | `editor/tests/SwfocTrainer.Tests/Build/WarningBaselineTests.cs` |
| C | Regression-pair | v3-only emitter | `editor/tests/SwfocTrainer.Tests/Regression/FactionSwitchServiceRegressionTests.cs` |
| D (per ViewModel) | Unit | Ctor null-guards + each command | `editor/tests/SwfocTrainer.Tests/App/V2/<Name>TabViewModelTests.cs` |
| D (overall) | Drift-guard | Every V2Vm state has either a tab or a defer-with-reason | `editor/tests/SwfocTrainer.Tests/App/V2/V2VmCoverageDriftTests.cs` |
| E | Regression-pair | Save/Profiles/Mods workflows | `editor/tests/SwfocTrainer.Tests/V2/SaveOpsTabRegressionTests.cs` etc. |
| F1 | Unit + smoke | Toggle persistence round-trip | `editor/tests/SwfocTrainer.Tests/App/AppLifecyclePersistenceTests.cs` |
| F2 | Drift-guard | Capability status catalogue covers every helper | `editor/tests/SwfocTrainer.Tests/Diagnostics/CapabilityStatusCatalogTests.cs` |
| G | Visual regression | Both themes, 24+ baselines | `editor/tests/SwfocTrainer.Tests/Visual/V2ThemeVisualTests.cs` |
| H1 | Frida runtime + regression-pair | NullAiBrain live | `tools/frida_harness/probes/ai_brain.json` + `editor/tests/SwfocTrainer.Tests/Regression/AiBrainNullingRegressionTests.cs` |
| H2 (decision-dependent) | Frida runtime + regression-pair | AttachAiBrain | `tools/frida_harness/probes/ai_brain.json` extended |
| H3 | Frida runtime | F-key path | `tools/frida_harness/probes/fkey_switch_sides.json` |
| I (per detour) | Regression-pair | red-on-old-stub, green-on-new-detour | `editor/tests/SwfocTrainer.Tests/Regression/Phase2HookRegressionTests.cs` |
| I (overall) | E2E live + smoke | All replays + bridge harness | `bridge_test_harness.exe` + `python smoke_test_replay.py` |
| J | Doc lint | All anchor docs reference consolidated paths | manual + `python -m verifier lint` |

### Coverage targets

- New code in Units D, E, F follows the global `.coverage-thresholds.json` (per the project's CLAUDE.md, 100% required as a blocking gate).
- Visual regression has its own pass criterion: `<= 0.5%` pixel diff per baseline.
- Bridge harness count NEVER decreases (per project's hard rule). Each unit asserts this.

### TDD order per unit

1. Write the regression-pair / drift-guard test FIRST.
2. Watch it fail.
3. Implement the change.
4. Watch the test pass.
5. Add the unit-test class for the new code.
6. Verify coverage.
7. Verify the build (R1).

---

## Definition of Done for the Whole Plan

The user can check this list at the end. Every item is verifiable from a terminal session.

### Build + test gates (blocking)

- [ ] `dotnet build SwfocTrainer.sln -c Release --no-restore --warnaserror` returns exit 0 with 0 warnings 0 errors.
- [ ] `bridge_test_harness.exe` >= 1091 / 0.
- [ ] `python smoke_test_replay.py` 12/12.
- [ ] `python smoke_test_replay_units.py` 34/34.
- [ ] Python suites >= 188 + 42 (or whatever the post-Unit-I count is).
- [ ] `cd tools && python -m verifier lint` 0 errors / 0 warnings.
- [ ] Editor non-live tests >= (6883 + new unit count) passing.
- [ ] Editor coverage meets `.coverage-thresholds.json`.

### Workflow gates (blocking)

- [ ] User can: launch the app without `--legacy-ui` and access every helper that was previously legacy-only.
- [ ] User can: switch theme; restart; theme persists.
- [ ] User can: toggle a feature, restart, toggle state restored.
- [ ] User can: see a capability status badge on every V2 button.
- [ ] User can: run "Null AI on slot 1 (repair stuck save)" and observe the `+0x360` change in the bridge response.
- [ ] User can: read `knowledge-base/fkey_switch_sides_investigation_2026-04-25.md` and know whether F-key has the same bug.

### Knowledge base gates (blocking)

- [ ] `verified_facts.json` has new entries for: `rva_player_object_ai_brain_null_helper`, `rva_player_object_ai_brain_attach_helper` (or marked DEPRECATED with documented unblock per H2-b), `rva_fkey_switch_sides_handler` (per H3), 6+ Phase 2 hook RVAs (per I orders 1-5).
- [ ] All new entries have `tools_consensus.length >= 2`.
- [ ] `knowledge-base/feature_readiness_matrix_2026-04-25.md` exists and supersedes the 2026-04-08 version.
- [ ] `knowledge-base/blocked_items_2026-04-25.md` exists.
- [ ] `knowledge-base/phase2_hook_backlog_2026-04-25.md` exists with all 18 hooks classified.

### Repository-shape gates (blocking, conditional on D1)

- [ ] (If D1 = A) `SWFOC editor/` no longer exists; everything moved to `swfoc_memory/editor/`.
- [ ] (If D1 = A) `MIGRATION_2026-04-25.md` documents the move.
- [ ] (If D1 = A) No file path in any tracked source references the old root.

### Documentation gates (blocking)

- [ ] `STATUS.md` reflects current verified counts.
- [ ] `swfoc_memory/CLAUDE.md` reflects the consolidated tree.
- [ ] `swfoc_memory/.remember/now.md` has a fresh delta table and handoff state.

### Soft gates (signalling, not blocking)

- [ ] All 18 Phase 2 hooks live (this is Phase 4; deferral is acceptable per D5).
- [ ] AttachAiBrain implemented (deferral acceptable per D3 = B).

---

## Sequencing Notes for the Reviewers

Three things the Plan Review Gate is likely to scrutinise. I want to flag them so the next pass goes smoothly.

### Why Unit B before Unit D

Unit D adds 10 new ViewModels with public surface. If Unit B doesn't land first, every new public `ulong objAddr` in those ViewModels rebuilds the warning pile. By driving warnings to 0 first AND adding `WarningBaselineTests`, every Unit D PR is automatically gated on "no new warnings" and the long-tail spending pattern doesn't reopen.

### Why Unit F before Unit I

The Capability Status Registry (F2) is the source of truth for badge state. Unit I converts 18 helpers from `Phase2HookPending` to `Live`. If F doesn't ship first, those 18 conversions have to update XAML strings instead of a single JSON file, and the badge taxonomy fragments across files. The drift-guard test (CapabilityStatusCatalogTests) explicitly catches the case where a helper is implemented in `lua_bridge.cpp` but the registry still says `Phase2HookPending` â€” this is the kind of post-implementation cleanup that's normally lost in a long PR series.

### Why Unit H is Phase 3 not Phase 1

The user's broken save is high-pain. But H depends on F (for the badge to flip live) and on the IDA hunt for AttachAiBrain (which is the open D3 question). Doing H earlier means either (a) H ships without the badge surfacing the right state â€” operator confusion â€” or (b) D3 is forced before the user has had a chance to weigh in. Phase 3 places H after F (so badges are live) and after H3 (the F-key probe) gives the user data to inform D3.

### What can ship as a hot-fix outside this plan

- The user's broken slot 1 save can be repaired by manually running `SWFOC_NullAiBrain(1)` from the bridge today (the helper landed at `lua_bridge.cpp:941`). The user does not have to wait for the full plan.
- The theme polish bug (ComboBox dark-on-dark, fixed at `MainWindowV2.xaml:24-32`) is already in flight; Unit G is the regression-protection layer.
- The single `<see cref="ApplyPreference"/>` warning in `ThemeService.cs:14` is small enough to hot-fix; Unit B uses it as the kickoff.

---

## Relevant File Anchors (for the implementer)

Reference list of the files this plan refers to most. All paths absolute, as requested.

### Source of truth files

- `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\verified_facts.json`
- `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\VERIFIED_RVAS_v3.md`
- `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\feature_readiness_matrix_2026-04-08.md`
- `C:\Users\Prekzursil\Downloads\swfoc_memory\STATUS.md`
- `C:\Users\Prekzursil\Downloads\swfoc_memory\CLAUDE.md`
- `C:\Users\Prekzursil\Downloads\swfoc_memory\.remember\now.md`

### Bridge

- `C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge\lua_bridge.cpp` (`SWFOC_SetHumanPlayer_v3` at line 762; `SWFOC_NullAiBrain` at line 941; `SWFOC_AttachAiBrain` at line 985; 18 Phase 2 stubs at lines 2803, 2819, 2835, 2850, 2878, 2914, 2954, 2965, 2988, 3078, 3147, 3167, 3204, 3235, 3250, 3618, 3626, 3636)
- `C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge\replay_harness.cpp`

### Editor V2 surface

- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.App\Program.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.App\V2\MainWindowV2.xaml` (8 tabs at lines 97, 170, 251, 336, 395, 437, 494, 560)
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.App\V2\Infrastructure\ThemeService.cs` (`<see cref="ApplyPreference"/>` warning at line 14)
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.App\V2\Infrastructure\V2Settings.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.App\V2\Themes\Dark.xaml`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.App\V2\Themes\Light.xaml`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.App\V2\ViewModels\MainViewModelV2.cs`

### Editor Pure-Core V2Vm states (the 16 to wire)

- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\EconomyTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\CombatTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\SpeedTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\InspectorTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\SpawningTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\GalacticTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\HeroLabTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\BattleControlTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\StoryEventsTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\CameraDebugTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\TacticalUnitsFilterTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\LuaPlaygroundTabState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\EventStreamViewState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\DirectorModeState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\CrossFactionRecruitmentState.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\V2Vm\UnitStatEditorState.cs`

### Editor regression and smoke tests

- `C:\Users\Prekzursil\Downloads\SWFOC editor\tests\SwfocTrainer.Tests\Regression\FactionSwitchServiceRegressionTests.cs` (line 79: stale comment per Unit C)
- `C:\Users\Prekzursil\Downloads\SWFOC editor\tests\SwfocTrainer.Tests\SmokeRun\FullFeatureSmokeTest.cs` (canonical end-to-end smoke)
- `C:\Users\Prekzursil\Downloads\SWFOC editor\tests\SwfocTrainer.Tests\Regression\` (other regression files in this directory)

### Editor v3-canonical service

- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\Services\FactionSwitchService.cs`

### Editor diagnostics infrastructure (for Unit F)

- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\Diagnostics\BuildLuaCommandInventory.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\Diagnostics\VerifiedFactsLedger.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\Diagnostics\ExecutionPath.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\Ux\FeatureToggleCoordinator.cs`
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.Core\Ux\FeatureTogglePersistence.cs`

### Tools

- `C:\Users\Prekzursil\Downloads\swfoc_memory\tools\run_editor_tests_v2.ps1`
- `C:\Users\Prekzursil\Downloads\swfoc_memory\tools\frida_harness\` (probe directory)
- `C:\Users\Prekzursil\Downloads\swfoc_memory\tools\verifier\` (lint command)
- `C:\Users\Prekzursil\Downloads\swfoc_memory\tools\fixture_library\`

### Legacy UI (target of removal in optional Unit K)

- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.App\MainWindow.xaml` (16 tabs at lines 45-846)
- `C:\Users\Prekzursil\Downloads\SWFOC editor\src\SwfocTrainer.App\ViewModels\MainViewModel*.cs` (the partial-class chain)