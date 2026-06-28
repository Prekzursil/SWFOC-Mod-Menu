# Iter 260 — A1.x SetUnitField max_* UnitStatEditor UX verification (iter 4/5 of multi-iter arc)

**Date**: 2026-05-07
**Iter**: 260 (close-out)
**Arc**: 6th multi-iter A1.x arc; iter 4 of 5.
**Predecessor**: iter 259 simulator handler + DirectorMode cascade catch.
**Successor**: iter 261 (live verify + close-out finale).

## Headline

**Verification iter — NO UI EXTENSION NEEDED**, exactly as predicted in the
iter-258 close-out queue. The UnitStatEditor staging dropdown's
`EditFieldOptions` already lists `max_hull` + `max_shield` from iter-245
(which added them as Phase-1 mirrors). iter-258 promoted the bridge
branches to LIVE without touching the UI — the Apply path now produces
LIVE engine effect because the bridge wire format is unchanged.

**+6 NEW pin tests** in `Iter260UnitStatEditorMaxFieldsLivePromotionTests.cs`
verifying the seamless promotion at the VM/UI layer.
**3 iter-245 sibling tests updated** for the 5-LIVE → 7-LIVE / 7-Phase-1
→ 5-Phase-1 ratio shift.
**1 VM-source comment** extended to cite iter-258 LIVE promotion +
TYPE-LEVEL caveat.

## What shipped

### VM source comment extension

`src/SwfocTrainer.App/V2/ViewModels/UnitStatEditorTabViewModel.cs` —
the `EditFieldOptions` declaration block's preceding comment extended to
add a 3rd LIVE-branch entry citing iter-258:

> "max_hull / max_shield (iter 258; TYPE-LEVEL writes via GameObj+0x298 →
> UnitType chain — affects EVERY unit of this type for the session, NOT
> per-instance. Operator should be aware that buff/nerf is global-by-type.
> The staging UI input fields already existed since iter-245 as Phase-1
> mirrors; iter-258 promoted the bridge branches to LIVE without touching
> the UI. Iter-260 verification iter pins this seamless promotion.)"

The Phase-1 mirror count drops from 7 → 5 (max_hull + max_shield removed
from the deferred list).

### Iter-245 sibling test updates

`tests/SwfocTrainer.Tests/Regression/Iter245UnitStatEditorStagingFieldsTests.cs`:

| Method (was → now) | Assertion (was → now) |
|---|---|
| `EditFieldOptions_IncludesAllFiveLiveFields` → `EditFieldOptions_IncludesAllSevenLiveFields` | +2 LIVE assertions: `max_hull`, `max_shield` |
| `EditFieldOptions_IncludesAllSevenPhase1MirrorFields` → `EditFieldOptions_IncludesAllFivePhase1MirrorFields` | -2 Phase-1 assertions: removed `max_hull`, `max_shield` |
| `EditFieldOptions_TotalCountIs12` reason text | reflects 7-LIVE + 5-Phase-1 = 12 (count UNCHANGED) |

Class docstring updated with iter-260 verification context + the iter-258
seamless-promotion provenance.

### Iter-260 NEW pin test file

`Iter260UnitStatEditorMaxFieldsLivePromotionTests.cs` — 6 tests:

| Test | Purpose |
|---|---|
| `StagingFieldOptions_MaxHullAndMaxShieldStillPresent_PostIter258` | Pin: iter-258 LIVE promotion preserves staging-UI option (no removal). |
| `CatalogStatus_SetUnitFieldStillLive_AcrossIter258Promotion` | Pin: catalog status stays Live (already was, since iter-136). |
| `CatalogRationale_DocumentsIter258TypeLevelCaveat_AtVMLayerToo` | Pin: `max_hull` / `max_shield` / "EVERY unit of this type" all appear in catalog Note (drives staging UI tooltip). |
| `StagingApplyPath_MaxHull_ProducesLiveEngineEffect_NoUIChanges` | **Cardinal test**: 2-AT_AT fixture; SendRawAsync `SWFOC_SetUnitField(addr, 'max_hull', 999)` → both AT-ATs' MaxHull == 999 (TYPE-shared via iter-259 sim handler). |
| `StagingApplyPath_MaxShield_ProducesLiveEngineEffect_NoUIChanges` | Sibling test for max_shield (single FakeUnit field collapses front+rear). |
| `StagingComment_DocumentsIter258Promotion` | Source-grep pin: VM source must cite "iter 258" + "max_hull / max_shield (iter 258" + "TYPE-LEVEL writes". Catches future comment decay. |

The cardinal pair (StagingApplyPath_*) exercises the actual Apply path —
the wire format that flows through `BridgeUnitStatEditDispatcher` →
`SwfocSimulator.HandleSetUnitField` → `FakeUnit.MaxHull/MaxShield`. This
is end-to-end at the VM/sim contract level.

## Verification gates (all GREEN, ZERO FAILURES)

| Gate | Result | Δ vs iter-259 |
|---|---|---|
| Bridge harness | **1100/0** | unchanged (no bridge changes) |
| Verifier ledger lint | **0/0** (318 entries) | unchanged |
| Editor full suite | **8162 passed / 0 failed / 8162 total** | **+6 from iter-260 NEW pin tests** (was 8156/0/8156 at iter-259 close) |
| Editor binary | `publish/SwfocTrainer.App.exe` 157.83 MB (165,495,627 B) | unchanged |
| New pin tests | 6 GREEN | NEW |
| Iter-245 sibling rename | 3 tests updated, all GREEN | NEW |
| Capability surface markdown | not regenerated (no catalog rationale change this iter; iter-258 already covered max_hull/max_shield) | unchanged |

## Pattern lessons

### #1 — "Seamless LIVE promotion" pattern proven across 2 instances

iter-245 verified iter-243's invuln_flag/prevent_death LIVE flip needed
**no UI extension** because the staging-UI input fields already existed
as Phase-1 mirrors. iter-260 verifies iter-258's max_hull/max_shield
LIVE flip needed **no UI extension** for the same reason. **Pattern
proven**: when a Phase-1 mirror has a staging-UI input field already,
LIVE promotion is a 0-line UI cost. The cost is concentrated in:

- **Bridge** (~50 LoC for the LIVE branches; iter-258 +50 LoC).
- **Simulator** (~5 LoC for the type-shared loop; iter-259 +6 LoC).
- **Tests** (NEW pin file + sibling test updates; iter-260 ~150 LoC).
- **Docs** (close-out doc; iter-260 ~120 LoC).

Total iter-260 surface change: **VM comment extension (5 lines) + zero
UI XAML changes**. The most operator-impactful arc step (LIVE engine
effect on Apply) shipped without any UI churn.

### #2 — Source-grep pin tests for VM comments

The new `StagingComment_DocumentsIter258Promotion` test reads the VM
source file and asserts the iter-258 reference is present. Same source-
grep pattern as iter-259's `HandleSetUnitField_SnakeCaseAndPascalCaseSubFields_CountIsTwelve`
test — proves the pattern is reusable for ANY documentation-decay
problem. Future arcs that need to keep VM source comments in sync with
catalog rationale should add a similar source-grep pin.

### #3 — Sibling test renaming preserves test history

The iter-245 tests `IncludesAllFiveLiveFields` /
`IncludesAllSevenPhase1MirrorFields` could have been **deleted and
replaced** with new iter-260 tests. Instead, they were **renamed in
place** with updated assertions. Benefits:

1. **xUnit run report** continues to show iter-245 as the file owner;
   git blame on the file still surfaces iter-245's design intent.
2. **No silent test-count drift** — the focused suite went from
   `Iter245.6 + Iter259.7 = 13` tests to `Iter245.6 + Iter259.7 +
   Iter260.6 = 19` tests. If iter-245 tests had been deleted, the count
   would have dropped to 13 + 6 = 19 (same total but lost iter-245
   coverage continuity).
3. **Test-name git history** preserves the iter-245 → iter-260 promotion
   trail — future readers see "this test was renamed when the field
   moved from Phase-1 to LIVE" rather than "this test was deleted, new
   one created".

**Pattern**: when a LIVE promotion shifts the partition between LIVE and
Phase-1 sets, **rename in place**, don't delete + recreate.

### #4 — Cardinal-test fixture choice mirrors sim-layer fixture

The iter-260 cardinal tests (`StagingApplyPath_*`) use the same
two-AT_AT fixture as iter-259's simulator tests. This is intentional: the
VM/UI layer test should provoke the same TYPE-shared semantic the
simulator pin test validated. If the VM layer accidentally collapsed
to per-instance scope, the iter-260 test fails because the second
AT_AT's MaxHull stays at the seed value.

**Pattern**: when extending coverage from layer N to layer N+1, reuse
the layer-N test fixture so layer-N+1 tests can detect scope collapse.

## Iter 261+ queued

- **Iter 261**: Live game smoke verify of max_hull + max_shield direct
  type-stats writes (with attached SwfocTrainer + running Swfoc.exe; if
  no live process, doc the limitation per iter-249 honest-defer pattern).
  Final all-gates verification + close-out doc covering iter 257-261
  arc with arc-level capstone pattern lessons.
- **Iter 262**: Operator changelog covering iter 257-261 5-iter arc
  (mirrors iter-247 / iter-241 / iter-235 / iter-229 / iter-253
  precedents).
- **Iter 263+**: Triage queue at the end of the arc — DirectorMode
  baseline failures already cleared at iter-259; reverse-orphan audit
  at iter-263 mirroring the iter-255 / iter-238 cadence; next A1.x
  arc (max_speed via iter-99 path / attack_power via iter-94 retry).
