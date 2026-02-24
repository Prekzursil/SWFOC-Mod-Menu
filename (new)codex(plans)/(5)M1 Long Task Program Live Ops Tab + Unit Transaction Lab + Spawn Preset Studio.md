# M1 Long Task Program: Live Ops Tab + Unit Transaction Lab + Spawn Preset Studio

## Summary
This is the best next long task because M0 reliability foundations are largely in place, open PR backlog is cleared, CI/security are green, and the highest-impact unfinished scope is M1 (`#7`) with clear acceptance criteria.

Chosen direction (locked from your decisions):

- Primary track: **M1 Live Action Command Surface**
- Delivery style: **Milestone slices**
- Validation: **Hybrid live-machine + deterministic CI**
- First implementation slice inside M1: **Unit Transaction Lab**
- UI shape: **New `Live Ops` tab**
- Operational stance: **Close remaining M0 carryover issues early**
- Plan archive policy: **Track entire `(new)codex(plans)/` folder in repo**

---

## Execution Program (Decision-Complete)

### Slice 0: Stabilization + M0 Carryover Closure

- Objective: close or reconcile `#15`, `#16`, `#17`, `#18` before deep M1 implementation.
- Implementation: verify runtime telemetry/re-resolve/calibration/doc guardrail acceptance against current code, then either close with evidence comments or open exact follow-up deltas scoped to M1.
- Implementation: add explicit repo convention for `(new)codex(plans)/` as versioned planning history, including naming and evidence linkage rules.
- Files: `TODO.md`, `CONTRIBUTING.md`, `docs/TEST_PLAN.md`, `(new)codex(plans)/README.md` (new), optionally `docs/ROADMAP_WORKFLOW.md` (new).
- Exit gate: issues `#15/#16/#17/#18` are closed or each has a narrowly scoped follow-up issue with owner, acceptance, and milestone mapping.

### Slice 1: Live Ops Domain Foundation (Core Contracts + Services)

- Objective: introduce reusable domain services so UI is thin and testable.
- Implementation: add action reliability evaluation service that classifies each action as `stable`, `experimental`, or `unavailable` using runtime mode, symbol health/source, dependency disable list, and helper availability.
- Implementation: add selected-unit transaction service with snapshot capture, staged apply, rollback-on-failure, and transaction history.
- Implementation: add spawn preset service that loads presets, expands batch plans, and executes helper spawn actions with throttling.
- Files: `src/SwfocTrainer.Core/Contracts`, `src/SwfocTrainer.Core/Models`, `src/SwfocTrainer.Core/Services`, `src/SwfocTrainer.App/App.xaml.cs` (DI registration).
- Exit gate: deterministic unit tests cover reliability classification matrix, transaction rollback semantics, and spawn plan expansion.

### Slice 2: Unit Transaction Lab Engine (First Functional Vertical)

- Objective: deliver safe apply/revert editing for selected-unit stats.
- Implementation: capture baseline snapshot for selected-unit symbols (`hp`, `shield`, `speed`, `damage`, `cooldown`, `veterancy`, `owner_faction`).
- Implementation: apply transaction as ordered writes through orchestrator/runtime with per-field diagnostics; if any write fails, auto-rollback already-applied fields.
- Implementation: maintain in-memory transaction stack with `Revert Last` and `Restore Baseline`.
- Implementation: enforce tactical-only strict gating for transaction actions; unknown mode is treated as unavailable for this feature.
- Files: `src/SwfocTrainer.Core/Services`, `src/SwfocTrainer.App/ViewModels/MainViewModel.cs`, new app models under `src/SwfocTrainer.App/Models`.
- Exit gate: unit tests prove atomic-like behavior (apply failure triggers rollback) and tactical gating behavior.

### Slice 3: New Live Ops Tab (UI Integration)

- Objective: add dedicated M1 UX without destabilizing existing runtime tab.
- Implementation: new `Live Ops` tab with three panes:
- Implementation: Selected Unit Lab pane (capture baseline, edit fields, apply, revert last, restore baseline, transaction history).
- Implementation: Action Reliability pane (action list with state badge + reason code + confidence).
- Implementation: Operational Diagnostics pane (current runtime mode, dependency state, helper readiness, unresolved/degraded symbols summary).
- Implementation: keep existing Runtime tab for low-level action execution; no behavior regressions in existing commands.
- Files: `src/SwfocTrainer.App/MainWindow.xaml`, `src/SwfocTrainer.App/ViewModels/MainViewModel.cs`, new view item models.
- Exit gate: manual UX pass on desktop + minimum window size, no binding errors, and deterministic view-model tests for tab command flows.

### Slice 4: Spawn Preset Studio + Batch Operations

- Objective: deliver catalog-driven spawn workflows for base/AOTR/ROE.
- Implementation: add preset schema and files under `profiles/default/presets/<profileId>/spawn_presets.json`.
- Implementation: build batch planner with configurable quantity, delay, faction, and entry marker override.
- Implementation: execute spawn plans via `spawn_unit_helper` with per-item result logging and early-stop-on-failure option.
- Implementation: provide profile-specific defaults seeded from `unit_catalog`, `hero_catalog`, `faction_catalog`, and `action_constraints`.
- Files: `profiles/default/presets/*` (new), `src/SwfocTrainer.Core/Services`, `src/SwfocTrainer.App/ViewModels/MainViewModel.cs`, `docs/PROFILE_FORMAT.md` (preset contract section).
- Exit gate: deterministic tests for preset loading and plan expansion across `base_swfoc`, `aotr_1397421866_swfoc`, and `roe_3447786229_swfoc`.

### Slice 5: Deterministic Mode Bundle Gatekeeper + Reliability Surfacing

- Objective: make high-impact bundles deterministic and transparent.
- Implementation: enforce strict mode gate for bundle actions (transaction/spawn/tactical bundles) independent from manual low-level action execution.
- Implementation: reliability state rules:
- Implementation: `stable`: required symbols healthy/signature or validated fallback with pass checks; mode/dep/helper gates pass.
- Implementation: `experimental`: fallback/degraded symbol health or unknown-mode soft uncertainty for non-critical actions.
- Implementation: `unavailable`: mode mismatch, dependency disabled action, unresolved critical symbol, missing helper.
- Implementation: propagate reliability state + reason code into UI and action audit diagnostics.
- Files: `src/SwfocTrainer.Core/Services`, `src/SwfocTrainer.Core/Logging`, `src/SwfocTrainer.App/ViewModels/MainViewModel.cs`.
- Exit gate: deterministic classification tests and audit record verification for reliability metadata.

### Slice 6: Live Validation, Documentation, and Issue Closure

- Objective: finalize M1 with live-machine evidence and close milestone issues cleanly.
- Implementation: add/extend live tests for tactical toggles and hero-state helper workflows with skip-graceful behavior.
- Implementation: produce standardized live checklist artifacts for AOTR/ROE (`#19`) including launch-context reason codes, attach diagnostics, and action evidence format.
- Implementation: update roadmap board lanes and close M1 sub-issues with links to CI runs and manual validation notes.
- Files: `tests/SwfocTrainer.Tests/Profiles`, `docs/TEST_PLAN.md`, `docs/SYMBOL_CALIBRATION_TODO.md`, `TODO.md`.
- Exit gate: M1 epic `#7` acceptance criteria met with evidence links; `#19` checklist completed.

---

## Important Changes or Additions to Public APIs / Interfaces / Types

1. Add `ActionReliabilityState` enum in `src/SwfocTrainer.Core/Models/Enums.cs`.
Values: `Stable`, `Experimental`, `Unavailable`.

2. Add live-ops models in `src/SwfocTrainer.Core/Models/LiveOpsModels.cs` (new).
Types: `ActionReliabilityInfo`, `SelectedUnitSnapshot`, `SelectedUnitDraft`, `SelectedUnitTransactionRecord`, `SpawnPreset`, `SpawnBatchPlan`, `SpawnBatchExecutionResult`.

3. Add `IActionReliabilityService` in `src/SwfocTrainer.Core/Contracts`.
Method: `IReadOnlyList<ActionReliabilityInfo> Evaluate(TrainerProfile profile, AttachSession session, IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog = null)`.

4. Add `ISelectedUnitTransactionService` in `src/SwfocTrainer.Core/Contracts`.
Methods: `CaptureAsync`, `ApplyAsync`, `RevertLastAsync`, `RestoreBaselineAsync`, plus `History` read model.

5. Add `ISpawnPresetService` in `src/SwfocTrainer.Core/Contracts`.
Methods: `LoadPresetsAsync`, `BuildBatchPlan`, `ExecuteBatchAsync`.

6. Extend profile/preset documentation contract.

- New preset file convention: `profiles/default/presets/<profileId>/spawn_presets.json`.
- Keep profile JSON backward compatible; no breaking required fields added.

7. Extend action audit diagnostics payload (non-breaking).

- Add keys such as `reliabilityState`, `reliabilityReasonCode`, `transactionId`, `bundleGateResult`.

---

## Test Cases and Scenarios

1. Deterministic reliability classification tests.

- `mode=tactical`, healthy symbols, helper valid => relevant tactical actions classify `stable`.
- `mode=unknown` and tactical-only bundle action => `unavailable` with `mode_unknown_strict_gate`.
- dependency soft-disabled helper action => `unavailable` with `dependency_soft_blocked`.
- fallback + degraded symbol for non-critical action => `experimental`.

2. Unit transaction engine tests.

- capture snapshot reads all selected-unit symbols successfully.
- apply draft with all writes successful => transaction committed and history incremented.
- apply draft with mid-stream failure => automatic rollback of previously written fields and failure reason code.
- revert last transaction restores prior snapshot values.
- restore baseline after multiple transactions returns to baseline values.

3. Spawn preset and batch tests.

- preset loader resolves profile-specific preset files and validates required fields.
- batch plan expands quantity/repeat correctly with deterministic item count.
- execution early-stop path halts on first failed spawn when configured.
- execution continue-on-failure path records partial failures and final aggregate status.

4. ViewModel/UI behavior tests.

- Live Ops commands are disabled when not attached.
- Unit Transaction pane disabled outside tactical mode.
- Reliability list updates after attach and after mode changes.
- spawn controls reflect helper/dependency availability state.

5. Live tests (skip-graceful).

- tactical toggles smoke run in tactical session and revert correctly.
- hero-state helper workflow smoke on AOTR/ROE runs when helper is present.
- no-process and wrong-mode conditions skip gracefully with explicit skip notes.

6. CI acceptance.

- deterministic suite remains green in `ci.yml` (live tests excluded by filter).
- launch-context fixture smoke remains green.
- added M1 deterministic tests run under `windows-latest`.

---

## Issue / Board Mapping

1. Keep `#7` as umbrella epic and create M1 sub-issues for each slice (`Slice1` through `Slice6`), each with acceptance gates copied from this plan.
2. Execute Slice 0 first and close/reconcile `#15/#16/#17/#18`.
3. Keep `#19` open until Slice 6 live checklist is delivered.
4. Keep `#29` deferred (no Pages/deployment in this program).
5. Keep assignee policy as `Prekzursil` only unless you explicitly choose otherwise later.

---

## Assumptions and Defaults

1. Safe-method boundary remains strict: no new invasive injection architecture beyond current runtime model.
2. Runtime target remains Windows; this shell may not have `dotnet`, so compile/test authority is Windows dev machine + GitHub Actions.
3. Existing four shipped profiles remain first-class and are the only hard acceptance targets for M1.
4. `(new)codex(plans)/` is intentionally tracked in git as versioned planning history, and additional plan files can be added over time.
5. Existing Runtime tab behavior must remain backward compatible while new M1 functionality is delivered in a dedicated `Live Ops` tab.
6. Bundle gating is strict for high-impact workflows (transactions/spawn bundles), while low-level manual action execution remains available for expert workflows.
