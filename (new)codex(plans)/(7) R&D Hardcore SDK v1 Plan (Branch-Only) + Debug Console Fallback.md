# R&D Hardcore SDK v1 Plan (Branch-Only) + Debug Console Fallback

## Summary
As of February 16, 2026, we will run a **branch-only experimental track** focused on a “custom SDK-like” runtime while keeping mainline safe and stable.  
Locked choices applied:

- Strategy: **Hardcore SDK first**
- Boundary: **R&D branch only**
- Integration: **Parallel runtime adapter**
- v1 scope: **10-command extended set**
- Lua policy: **Mixed by operation**
- Calibration: **Signature + safe-disable (no blind offsets)**
- Path A inclusion: **Yes, include official debug-console fallback track**

Critical prerequisite (must be done first in this branch): fix runtime mode/process detection so tactical state is inferred from live runtime state, not launch parameters.

---

## Phase 0: Branch Guardrails and Scope Isolation
1. Create R&D branch: `rd/hardcore-sdk-v1`.
2. Add branch-local feature gate:
- `SWFOC_EXPERIMENTAL_SDK=1` enables SDK backend.
- Default remains current runtime path.
3. Add branch policy doc:
- `docs/EXPERIMENTAL_SDK_BRANCH.md`
- Explicitly states this is non-mainline research and not default shipping path.
4. Keep existing Runtime tab/actions functional and unchanged when feature gate is off.

Exit gate:
- Branch builds and runs with SDK gate off (no behavior change from mainline).

---

## Phase 1: Reliability Prerequisite Fix (Mode + Host Selection)
1. Fix process classification in `src/SwfocTrainer.Runtime/Services/ProcessLocator.cs`:
- Stop using generic command-line token leakage for executable family classification.
- Prioritize process name/path and explicit launch-context signals.
2. Add deterministic host selection rule:
- For FoC with both `swfoc.exe` and `StarWarsG.exe`, always bind to host `StarWarsG.exe`.
3. Replace launch-param mode inference with attach-time live mode probing in runtime:
- New service: `RuntimeModeProbeResolver` (runtime internal).
- Use symbol-readability + sanity checks to classify `Tactical` vs `Galactic`.
4. Persist diagnostics:
- `runtimeModeHint`
- `runtimeModeEffective`
- `runtimeModeReasonCode`
- `processSelectionReason`

Exit gate:
- Tactical live tests no longer skip solely due command-line-derived `Unknown`.
- Attach PID is consistent between locator and runtime attach.

---

## Phase 2: SDK Core Contracts (Parallel Backend)
1. Add SDK domain models:
- `SdkOperationId`
- `SdkCommandRequest`
- `SdkCommandResult`
- `SdkCapabilityStatus` (`Available`, `Degraded`, `Unavailable`)
- `SdkCapabilityReport`
2. Add contracts:
- `ISdkRuntimeAdapter`
- `ISdkCapabilityResolver`
- `IDebugConsoleFallbackAdapter`
3. Extend execution routing (parallel path):
- Add `ExecutionKind.Sdk` in `src/SwfocTrainer.Core/Models/Enums.cs`.
- `TrainerOrchestrator` routes SDK actions only when feature flag is enabled.
- Existing `Memory/Helper/CodePatch/Save/Freeze` routing remains intact.

Exit gate:
- SDK action dispatch compiles and is no-op disabled when gate is off.

---

## Phase 3: Signature-Based Capability Resolver (Safe Disable)
1. Add profile-scoped SDK operation map contract:
- `profiles/default/sdk/<profileId>/sdk_operation_map.json`
2. Map each operation to:
- resolver signatures/pattern families
- expected argument schema
- mode requirements
- pointer/value sanity validators
3. Resolver behavior:
- If all required anchors resolve + validators pass -> `Available`
- Partial resolution -> `Degraded` (read-only operations allowed, mutating blocked by policy)
- Uncertain/missing -> `Unavailable` with reason code
4. No hardcoded raw addresses as acceptance path.
5. Optional fallback offsets allowed only as diagnostic hints, never auto-executed blindly.

Exit gate:
- Capability report generated at attach and surfaced in diagnostics.
- Mutating SDK actions are blocked when capability is uncertain.

---

## Phase 4: SDK v1 Command Surface (10 Commands)
Mandatory v1 operations:

1. `list_selected`
2. `list_nearby`
3. `spawn`
4. `kill`
5. `set_owner`
6. `teleport`
7. `set_planet_owner`
8. `set_hp`
9. `set_shield`
10. `set_cooldown`

Implementation rules:
1. Read-only operations (`list_*`) can run in degraded mode.
2. Mutating operations require `Available` capability.
3. Mode guards:
- Tactical ops: `set_hp`, `set_shield`, `set_cooldown`, `teleport`, tactical `set_owner`, `kill`
- Galactic ops: `set_planet_owner`, galactic `set_owner`
- `spawn` allowed per profile capability + mode rule from map
4. Rollback strategy where feasible:
- Snapshot before mutation for owner/stat/position updates.
- If validation fails post-write, auto-revert snapshot.
5. Reason-code-first failures:
- `capability_unavailable`
- `mode_mismatch`
- `validation_failed`
- `target_not_found`
- `unsafe_pointer_state`

Exit gate:
- All 10 operations wired through SDK backend with explicit capability/mode gating and diagnostics.

---

## Phase 5: Debug Console Fallback Track (Path A in Same Branch)
1. Add fallback adapter:
- `DebugConsoleFallbackAdapter`
2. Fallback registry model:
- maps SDK operation -> known debug-console command template (if supported).
3. v1 fallback policy:
- If SDK capability unavailable and console mapping exists: emit fallback-ready command payload.
- If mapping does not exist: return `fallback_not_supported`.
4. Keep dispatch safe/predictable:
- default mode is **prepare-only** (emit command text/artifact and status).
- no mandatory input automation dependency.
5. Include `SwitchControl` support where profile/game context allows.

Exit gate:
- Fallback report generated with operation-level coverage and reasoned unsupported cases.

---

## Phase 6: App/Diagnostics Integration
1. Add experimental “SDK Ops” panel behind feature flag in app:
- capability grid by operation
- mode + process host diagnostics
- command execution history with reason codes
2. Keep existing Live Ops and Runtime tabs untouched for non-R&D use.
3. Log schema extension:
- `sdkOperationId`
- `sdkCapabilityState`
- `sdkResolverSignatureSet`
- `fallbackMode` (`none`, `debug_console_prepared`, `debug_console_executed`)

Exit gate:
- Operator can see clearly why an operation ran, degraded, fell back, or was blocked.

---

## Phase 7: Testing and Validation Matrix
### Deterministic tests
1. `ProcessLocator` host-selection determinism (swfoc + StarWarsG pairs).
2. Runtime mode probe resolver tests (tactical, galactic, unknown conflict cases).
3. SDK capability resolver tests from fixture maps.
4. SDK command router tests:
- gate off -> legacy path
- gate on + unavailable capability -> blocked with reason code
- gate on + available -> dispatch
5. Debug-console fallback registry tests (supported vs unsupported operation mapping).

### Live tests (local machine)
1. AOTR Galactic:
- helper workflow pass
- capability report generated
2. AOTR Tactical Space + Ground:
- tactical mode effective detection
- at least one tactical mutating SDK operation succeeds
3. Campaign mode:
- `set_planet_owner` gated and executed only in galactic context
4. Failure safety:
- unresolved capability blocks mutation and reports explicit reason code
- no silent success on unresolved signatures

Acceptance gate:
- No tactical skip due launch-param-only mode logic.
- SDK ops are deterministic in capability reporting and fail safe when uncertain.

---

## Important Changes or Additions to Public APIs / Interfaces / Types
1. `ExecutionKind`:
- add `Sdk`.
2. New core models (`src/SwfocTrainer.Core/Models/SdkModels.cs`):
- `SdkOperationId`
- `SdkCommandRequest`
- `SdkCommandResult`
- `SdkCapabilityStatus`
- `SdkCapabilityReport`
3. New contracts (`src/SwfocTrainer.Core/Contracts`):
- `ISdkRuntimeAdapter`
- `ISdkCapabilityResolver`
- `IDebugConsoleFallbackAdapter`
4. `ProcessMetadata.Metadata` additions:
- `runtimeModeHint`
- `runtimeModeEffective`
- `runtimeModeReasonCode`
- `processSelectionReason`
5. New profile-side config contract:
- `profiles/default/sdk/<profileId>/sdk_operation_map.json`

---

## Explicit Assumptions and Defaults
1. This work is isolated to `rd/hardcore-sdk-v1` and does not change default mainline behavior.
2. No blind fixed-address execution is accepted; unresolved capabilities are disabled, not guessed.
3. Live validation remains Windows-local; CI remains deterministic-only.
4. Mixed Lua policy means operations may still use helper paths where SDK capability is unavailable, but SDK v1 core targets non-Lua internal execution first.
5. Debug-console fallback is included as **prepare-first** to keep behavior deterministic and auditable.
6. Tactical/galactic are treated as runtime states inferred from live process state, not launch arguments.
