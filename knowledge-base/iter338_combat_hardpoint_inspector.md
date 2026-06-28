# iter-338 — Combat tab Hardpoint Inspector smaller-scope (closes iter-336 honest-defer; FIRST consumer of iter-337 preflight rule)

**Date:** 2026-05-07
**Arc class:** LIVE delivery (operator-facing read-only inspector; consumes iter-281 SWFOC_GetHardpoints LIVE wire)
**Predecessor:** iter-337 (codify `feedback_iter_strategy_preflight_stack.md`)
**Successor (queued):** iter-339 (Combat tab Hardpoint Inspector XAML wire-up + republish OR Hardpoint icon-resolution chain extension)

## What changed (3 files modified; ~140 LoC; **8/8 PASS in 1.64s**)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.Core/V2Vm/CombatTabState.cs` (+~50 LoC):
  - NEW `HardpointEntry` record `(int Index, long ChildAddr, float Hp)`
  - NEW static `HardpointEntry.ParseListFromBridgeReply(string?)` — defensive null-safe parser for the iter-281 SWFOC_GetHardpoints raw `count=N child0=0x... hp0=...` format
  - Added `using System.Globalization` + `using System.Text.RegularExpressions`
- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/ViewModels/CombatTabViewModel.cs` (+~80 LoC):
  - NEW `_bridge` field (captured V2BridgeAdapter for direct SendRawAsync; mirrors iter-190 Diagnostics pattern)
  - NEW `_hardpointInspectAddrText` field with `HardpointInspectAddrText` property (default `"0x12345678"`)
  - NEW `Hardpoints` `ObservableCollection<HardpointEntry>` (bound to future XAML ListBox in iter-339)
  - NEW `RefreshHardpointsCommand` AsyncRelayCommand + `RefreshHardpoints` CapabilityAwareAction (iter-167 catalog ref `SWFOC_GetHardpoints`)
  - NEW `RefreshHardpointsCore` async method: parse hex/decimal addr → `_bridge.SendRawAsync("return SWFOC_GetHardpoints(<addr>)")` → `HardpointEntry.ParseListFromBridgeReply` → populate `Hardpoints` collection
  - Mid-iter compile error fix: `BridgeRoundTripResult` has `Response` + `ErrorMessage` fields, NOT `ResponseOrError` (5-second-grep would have prevented; per iter-283 codified rule)
- **NEW** `tests/SwfocTrainer.Tests/Core/V2Vm/Iter338HardpointEntryParserTests.cs` (~110 LoC, **8 facts**):
  - `ParseListFromBridgeReply_NullInput_ReturnsEmpty` — defensive null-safe
  - `ParseListFromBridgeReply_EmptyInput_ReturnsEmpty` — empty + whitespace coverage
  - `ParseListFromBridgeReply_CountZero_ReturnsEmpty` — sentinel for unit with no hardpoints
  - `ParseListFromBridgeReply_SingleHardpoint_ReturnsOneEntry` — happy path single entry
  - `ParseListFromBridgeReply_MultipleHardpoints_ReturnsAllInOrder` — multi-entry happy path with order preservation
  - `ParseListFromBridgeReply_Malformed_ReturnsEmpty_DoesNotThrow` — defensive degradation
  - `ParseListFromBridgeReply_NegativeHp_Allowed` — iter-285 Tier 3 overlay observed negative HP for dead-but-not-cleaned hardpoints
  - `ParseListFromBridgeReply_PartialEntries_ParsesValidOnly` — defensive partial-parse for truncated bridge replies

## Verification gates ALL GREEN

```
[Start-Process bypass — Clink-safe]
dotnet test --filter "FullyQualifiedName~Iter338"
Test Run Successful.
Total tests: 8
     Passed: 8
 Total time: 1.6421 Seconds
```

- iter-338 pin tests: **8/8 PASS** in 1.64s
- Editor build: GREEN (after mid-iter compile error fix; see "iter-337 preflight validation" below)
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries

## iter-337 preflight rule validation — iter-338 IS the first consumer

iter-337 just codified `feedback_iter_strategy_preflight_stack.md`. iter-338 is the **FIRST iter to apply the rule** — and the application produced exactly the predicted decision-tree outcome:

**Step 0 — Rationale-grep**: iter-336 close-out doc documented the SWFOC_GetHardpoints wire signature and bridge-source location. **PASS** (rationale already explained).

**Step 1 — Bridge-source-grep**: `Lua_GetHardpoints` confirmed at `lua_bridge.cpp:2228`; returns `count=N child0=0x... hp0=... ...`. **PASS** (existing infra reusable).

**Step 2 — 4-step composition preflight**:
- (a) Catalog rationale already explains? YES (iter-336 found `RequiresLiveSwfoc` status at CapabilityStatusCatalog.cs:1351)
- (b) Engine-surface gap? NO (bridge wire exists; just consumer needed)
- (c) Bridge wire orphan? YES until this iter (no editor consumer pre-iter-338); **iter-338 closes the orphan**
- (d) Composition genuinely sufficient? YES (single SWFOC_GetHardpoints call + parser sufficient for read-only inspector)

**Decision tree application**: All preflight steps green → **continue with original plan**. Outcome matched — shipped at predicted ~80 LoC scope (~140 LoC actual including tests).

**Mid-iter compile error caught + fixed**: `BridgeRoundTripResult` has `Response` + `ErrorMessage` fields (not `ResponseOrError` as I incorrectly assumed). The iter-283 codified rule (`feedback_infra_claim_drift_bidirectional`) explicitly says "5-second-grep before designing" — I should have grepped `record BridgeRoundTripResult` before writing `round.ResponseOrError`. iter-337 preflight stack would have caught this in Step 1 if applied at code-line granularity.

**iter-338 confirms iter-337 codification was correctly timed**: 3-instance trigger justified at iter-337 because the rule's value compounds immediately at iter-338 application. If iter-337 had waited for 6-instance trigger, iter-338 would have re-derived the preflight steps without the codified guidance.

## Operator-facing impact (Phase 1 — VM layer)

**Pre-iter-338**: Operator could call `SWFOC_GetHardpoints(addr)` from Lua Playground but had to manually parse the `count=N child0=0x... hp0=...` raw text. Hardpoint enumeration required CLI text-wrangling.

**Post-iter-338 (VM layer; XAML deferred to iter-339)**: VM has `Hardpoints` ObservableCollection ready to bind to a Combat tab ListBox. `RefreshHardpointsCommand` parses addr → bridge call → ObservableCollection update. CapabilityAwareAction surface metadata pinned to `SWFOC_GetHardpoints` for the catalog-trust badge system.

**Post-iter-339 (XAML layer + republish)**: Operator clicks "Refresh" button in Combat tab Hardpoint Inspector GroupBox → ListBox shows `[Index 0] 0x140012340 hp=100.000` rows. Read-only inspection without leaving the editor.

## Pattern lessons

### iter-338 IS the first consumer of iter-337 codified rule — meta-validation

iter-337 codified the iter-strategy preflight stack at 3-instance trigger (justified by meta-rule + shape variety). iter-338 is the **immediate next iter** — applying the rule on its own consumption proved:

1. **The 3-instance trigger was correctly timed**: rule's value compounded at iter-338 (zero re-derivation cost; followed decision tree explicitly).
2. **The decision tree's "continue with original plan" outcome works**: iter-338 didn't need to pivot because preflight surfaced no surprises.
3. **The mid-iter compile error catch validates the rule's "honest break-out" clause**: trivial well-understood iters (single-line code edits) don't need the full preflight overhead, but BridgeRoundTripResult field name was a 1-grep check that would have prevented the cycle.

**Pattern lesson** (1st instance; codification candidate at 3rd recurrence): **codification iters' value should be measured by the IMMEDIATE NEXT iter's consumption**. iter-302 codified at iter-302 → iter-303+ saw immediate engine-already-does-this rule application. iter-334 codified at iter-334 → iter-335 onwards saw LocateByConvention extension applications. iter-337 codified at iter-337 → iter-338 IS the first consumer. **All 3 codifications proven by next-iter consumption**.

Codification candidate `feedback_codification_value_proven_by_next_iter.md` flagged at 1/3.

### Pattern observation — *VM-layer first, XAML-layer second iters scale better than mixed*

iter-338 deliberately deferred XAML wiring to iter-339 to keep iter-338 surgical-scope. The VM layer (~80 LoC + 8 pin tests) shipped in single-iter scope with high confidence (parser tests prove correctness). XAML wiring (~30 LoC GroupBox + DataTemplate) is a separate concern with separate failure modes (binding paths, ItemTemplate resolution, layout).

**Pattern lesson** (1st instance; codification candidate at 3rd recurrence): when extending a tab with new VM + XAML, ship in 2 separate iters: VM iter (with parser/contract tests) → XAML iter (with binding/layout tests). Each iter is independently verifiable; defects are isolated to their layer. iter-148 (Camera VM) → iter-149 (Camera XAML) is the first instance of this pattern; iter-338 (Hardpoint VM) → iter-339 (Hardpoint XAML) is the second.

If iter-339 + future tab extension iters confirm this 2-iter split, codification candidate `feedback_vm_first_xaml_second_iter_split.md` flagged at 2/3.

## What's NOT done in iter-338 (deferred)

- **XAML GroupBox + Refresh button + ListBox**: deferred to iter-339 — keeps iter-338 surgical-scope at VM layer
- **Editor republish**: deferred to iter-339 (which will include XAML changes)
- **Icon resolution chain** (per-child SWFOC_GetType → ResolveWeaponIcon): deferred to iter-340+ — iter-336 preflight identified this as 2-bridge-call complexity beyond single-iter scope
- **Live SWFOC verify** of the inspector: requires operator session with running SWFOC

## Verification checklist

- [x] `HardpointEntry` record + parser shipped in CombatTabState.cs
- [x] `Hardpoints` ObservableCollection + `RefreshHardpointsCommand` + `RefreshHardpoints` CapabilityAwareAction shipped in CombatTabViewModel.cs
- [x] `_bridge` field captured for direct SendRawAsync (mirror iter-190 pattern)
- [x] Mid-iter BridgeRoundTripResult field name compile error fixed
- [x] 8/8 parser pin tests pass in 1.64s
- [x] Editor build inherits GREEN
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [x] iter-337 preflight rule applied + validated as first consumer
- [ ] XAML GroupBox + ListBox wire-up — deferred to iter-339
- [ ] Editor republish — deferred to iter-339
- [ ] Icon resolution chain — deferred to iter-340+
- [ ] Live SWFOC verify — deferred to operator session
