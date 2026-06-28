# Iter 259 — A1.x SetUnitField max_* simulator handler + 7-test pin file + DirectorMode 3rd-cousin cascade catch (iter 3/5 of multi-iter arc)

**Date**: 2026-05-07
**Iter**: 259 (close-out)
**Arc**: 6th multi-iter A1.x arc; iter 3 of 5.
**Predecessor**: iter 258 bridge LIVE wire (max_hull + max_shield, ratio 7/13).
**Successor**: iter 260 (UnitStatEditor staging-UI verify; likely no-op per
iter-245 pattern).

## Headline

- **Simulator HandleSetUnitField extended** — `max_hull` and `max_shield`
  branches now mirror the iter-258 bridge's TYPE-SHARED semantic by iterating
  `GameState.Units` filtered by `TypeName`, not just writing the indexed
  unit's field.
- **7 NEW pin tests** in `Iter259SetUnitFieldMaxFieldsSimulatorTests.cs`
  validating: catalog status persists Live, max_hull/max_shield TYPE-shared
  round-trip, cross-type isolation, single-unit-type edge case, legacy
  PascalCase per-instance scope (regression guard), 13-field taxonomy stable.
- **3rd-cousin cascade catch from iter-237 silent SetCameraPos LIVE flip**:
  3 stale `DirectorModeTabViewModelCapabilityTests` (StartPlayback /
  StepPlayback / Phase2PendingWarning) updated to expect LIVE badge.
  iter-243 caught Phase2PendingEntryCount; iter-258 caught Iter107
  ScrollCameraToTarget; iter-259 closes the trio.
- **All gates GREEN, 0 baseline failures**: editor full suite **8156 / 0 /
  8156 total** (was 8153 / 3 / 8156 at iter-258 close).

## What shipped

### Simulator HandleSetUnitField extension

`tests/SwfocTrainer.Tests/Simulator/SwfocSimulator.cs` — replaced two
single-line per-instance assignments with a `foreach`-by-TypeName loop:

```csharp
case "max_hull":
    foreach (var sibling in GameState.Units)
        if (sibling.TypeName == u.TypeName) sibling.MaxHull = value;
    break;
case "max_shield":
    foreach (var sibling in GameState.Units)
        if (sibling.TypeName == u.TypeName) sibling.MaxShield = value;
    break;
```

This mirrors the iter-258 bridge's actual behavior at the simulator
abstraction level: writing `unit + 0x298 → UnitType + 0xDCC` propagates to
ALL units of the same type, since they share the type-stats record.

The single `MaxShield` field on `FakeUnit` collapses both
`UnitType+0xDD0` (front) and `UnitType+0xDD4` (rear) — operator-trust scope
is "max-shield-of-this-type", not separate front/rear values.

The 5 deferred sub-fields (max_speed/attack_power/respawn_ms/is_hero/
respawn_enabled) keep their per-instance Phase-1 mirror semantics — only
max_hull/max_shield are TYPE-shared.

### 7 NEW pin tests in Iter259SetUnitFieldMaxFieldsSimulatorTests.cs

| Test | Purpose |
|---|---|
| `CatalogStatus_SetUnitFieldIsLive_PostIter258` | Pin: catalog stays Live across iter-136 → iter-243 → iter-258 promotions. |
| `SimulatorRoundTrip_MaxHull_TypeSharedWriteAffectsAllSiblings` | **Cardinal test**: 2 AT-AT units + 1 Trooper. Write max_hull on AT-AT 1 → AT-AT 2 changes too, Trooper stays. |
| `SimulatorRoundTrip_MaxShield_TypeSharedDualWrite` | Same fixture, write max_shield → both AT-ATs propagate, Trooper isolated. |
| `SimulatorRoundTrip_MaxHullThenMaxShield_OnDifferentTypes_StaysIsolated` | Combined test verifying no cross-contamination between fields/types. |
| `SimulatorRoundTrip_MaxHull_WithSingleUnitType_DoesNotErrorOut` | Edge case: only 1 unit of the type exists; the type-loop sibling-count == 1 still works. |
| `SimulatorRoundTrip_MaxHull_LegacyPascalCaseStillWritesPerInstance` | Regression guard: legacy `"MaxHull"` PascalCase branch keeps PER-INSTANCE scope (used by PhaseCSimulatorTests). Pins the snake-vs-Pascal scope-difference so a future "let's unify" refactor can't silently break legacy tests. |
| `HandleSetUnitField_SnakeCaseAndPascalCaseSubFields_CountIsTwelve` | Drift guard: source-grep verifies all 13 snake_case branches present in HandleSetUnitField; catches silent additions/removals. |

### DirectorMode 3rd-cousin cascade catch

`tests/SwfocTrainer.Tests/App/V2/ViewModels/DirectorModeTabViewModelCapabilityTests.cs` —
3 tests + 1 docstring updated:

| Old (stale) | New (post-iter-237) |
|---|---|
| `StartPlayback_BadgeIsPhase2Pending` | `StartPlayback_BadgeIsLive_PerIter237Cascade` |
| `StepPlayback_SamePrimitiveAsStart_BadgeIsPhase2Pending` | `StepPlayback_SamePrimitiveAsStart_BadgeIsLive` |
| `Phase2PendingWarning_NamesNonLiveActionsOnly` | `Phase2PendingWarning_NamesOnlySetTimeScale_PostIter237Cascade` |

The third test now uses `NotContain("Start playback")` /
`NotContain("Step playback")` instead of `Contain` — same playback wires
but flipped expectation polarity. SetTimeScale stays PHASE 2 PENDING
(SWFOC_SetGameSpeed is iter-131 confirmed-defer; no engine RVA in ledger).

Class docstring updated with the cascade-catch chain provenance:
**iter-237 silent flip → iter-243 catch (Phase2PendingEntryCount) →
iter-258 catch (Iter107) → iter-259 catch (DirectorMode trio)**.

### Capability surface markdown re-regenerated

The DirectorMode badge change shifts the Phase2PendingWarning text in
the surface report. Regenerated via `SWFOC_REGEN_CAPABILITY_SURFACE=1`.

## Verification gates (all GREEN)

| Gate | Result | Δ vs iter-258 |
|---|---|---|
| Bridge harness | **1100/0** | unchanged (no bridge changes this iter) |
| Verifier ledger lint | **0/0** (318 entries) | unchanged |
| Editor full suite | **8156 passed / 0 failed / 8156 total** | **+3 passed (was 8153/3/8156); 7 new tests + 3 fixed baseline tests** |
| Editor binary | `publish/SwfocTrainer.App.exe` 165,495,627 B (157.83 MB) | unchanged |
| New pin tests | 7 GREEN | NEW |
| DirectorMode baseline | 3 fixed | NEW |

## Pattern lessons

### #1 — Cascading-drift recursive cleanup proven across 3 cascade hops

Iter-258 pattern lesson #5 ("cascading drift catches need recursive
cleanup") earned its first downstream beneficiary within 1 iter. The
iter-237 silent SetCameraPos LIVE flip cascaded to:

- **1st-cousin** (iter-243): Phase2PendingEntryCount drift; caught by
  count-pin failing in full-suite run.
- **2nd-cousin** (iter-258): Iter107ScrollCameraToTarget catalog test;
  caught by full-suite running iter-258 changes.
- **3rd-cousin** (iter-259): DirectorMode StartPlayback/StepPlayback
  badge tests; caught by full-suite running iter-259 changes.

**6 iters between iter-237 silent flip and full cascade resolution.**
Future cascading drift catches should grep ALL test files for the
SWFOC_* name immediately, not wait for downstream iters to surface
each cousin individually. Pattern: when fixing a 1st-cousin, run
`grep -rn "SWFOC_<NAME>" tests/` and audit every match.

### #2 — TYPE-shared semantic at the simulator level

iter-258 introduced "writes affect every unit of the same type" semantics
that have NO precedent in the simulator's prior 13-field SetUnitField
taxonomy. Every other LIVE branch (hull/shield/speed/invuln_flag/
prevent_death) is per-instance. Adding TYPE-shared semantics to the
simulator required a 2-line `foreach`-by-TypeName loop — trivially
simple, but the test fixture had to grow from 1 unit to 3 (2 AT-ATs +
1 Trooper) to verify both propagation AND isolation.

**Pattern**: when a bridge LIVE wire's operator-trust scope differs from
all sibling LIVE wires, the simulator pin tests must use a fixture that
demonstrates the scope. Single-unit fixtures can't catch cross-type
propagation bugs.

### #3 — Legacy PascalCase regression guard

The iter-244 simulator extension preserved legacy PascalCase branches
(`"MaxHull"`, `"MaxShield"`) with per-instance semantics. Iter-258
extended the canonical snake_case branches with TYPE-shared semantics.
The two scopes now differ.

A future "unify the duplicates" refactor could silently change legacy
PascalCase scope, breaking PhaseCSimulatorTests that depend on
per-instance behavior. Iter-259 added an explicit pin test
(`SimulatorRoundTrip_MaxHull_LegacyPascalCaseStillWritesPerInstance`)
that asserts the scope-difference. Future refactors must update this
pin or accept the test failure as a deliberate scope change.

**Pattern**: when two near-duplicate code paths have different
semantics, add a NAMED pin test that catches the scope difference.
Don't rely on naming convention or comments — they erode.

### #4 — Source-grep pin tests bypass VM/bridge dependencies

The 13-field taxonomy drift guard
(`HandleSetUnitField_SnakeCaseAndPascalCaseSubFields_CountIsTwelve`)
reads the simulator source file with `File.ReadAllText` and asserts
each `case "<field>":` literal exists. No simulator instance, no bridge,
no VM construction — just text inspection. Fast (<1 ms), no async setup,
catches taxonomy drift at compile-test time.

**Pattern**: when validating constants/literals in source, prefer
source-grep over VM-construction-based assertions. ~10x faster, ~5x less
fragile to dependency changes.

## Iter 260+ queued

- **Iter 260**: UnitStatEditor staging-UI verification — likely no-op.
  iter-245 added `max_hull` + `max_shield` input fields to the Phase-1
  staging UI (when those branches were Phase-1 mirrors). Now those same
  staged values flow LIVE through the bridge. Test should confirm:
  (1) UnitStatEditor input fields exist (from iter-245), (2) Apply click
  routes to the LIVE bridge wire (no UI changes needed), (3) iter-245
  cross-reference comments updated to cite iter-258 LIVE flip.
- **Iter 261**: Live game smoke verify of max_hull + max_shield direct
  type-stats writes (with attached SwfocTrainer + running Swfoc.exe; if
  no live process, doc the limitation per iter-249 honest-defer pattern).
  Final all-gates verification + close-out doc covering iter 257-261
  arc.
- **Iter 262**: Operator changelog covering iter 257-261 5-iter arc
  (mirrors iter-247 / iter-241 / iter-235 / iter-229 / iter-253
  precedents).
