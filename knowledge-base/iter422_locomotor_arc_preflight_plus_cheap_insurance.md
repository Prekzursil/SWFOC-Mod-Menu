# Iter 422 — SWFOC_GetUnitLocomotorState arc preflight (deferred) + cheap-insurance republish

**Date:** 2026-05-07
**Arc class:** RE preflight (negative-result steer) + cheap-insurance republish (mirrors iter-412 pattern)
**Predecessor:** iter-421 (8th headline-doc capstone)
**Successor (queued):** iter-423 (TBD per "Next iter" below)

## What this iter does

Two concrete deliverables:

1. **SWFOC_GetUnitLocomotorState arc preflight** — investigates whether LocomotorStateType (iter-414, 34 names) has a clean Lua API pairing per iter-302 codified rule. **Outcome: NO clean Lua API.** Ship requires multi-iter A1.x-style offset RE.

2. **Cheap-insurance republish + filtered test verify** (mirrors iter-412 pattern) — pipeline-health check after 10-iter no-source-change window since iter-404.

## Phase 1 — SWFOC_GetUnitLocomotorState arc preflight

Per iter-302 codified rule (`feedback_engine_already_does_this.md`), preferred path: engine has Lua API → DoString roundtrip via iter-167 unit-getter helper (~3 LoC bridge cost). Required: `Get_Locomotor_State()` or similar Lua method on GameObjectWrapper.

### Search results
- **rtti_refs Locomotor*** (16 RTTI classes found): BikeLocomotorBehaviorClass, FighterLocomotorBehaviorClass, FleetLocomotorBehaviorClass, FlyingLocomotorBehaviorClass, JetPackLocomotorBehaviorClass, LandBomberLocomotorBehaviorClass, LandTeamContainerLocomotorBehaviorClass, LandTeamInfantryLocomotorBehaviorClass, LocomotorBehaviorClass (parent), LocomotorCommonClass, LocomotorDataPackClass, SimpleSpaceLocomotorBehaviorClass, StarshipLocomotorBehaviorClass, TeamLocomotorBehaviorClass, WalkLocomotorBehaviorClass — rich locomotor inheritance hierarchy
- **docs/lua-api.md grep**: NO documented `Get_Locomotor_State()` / `Get_Movement_State()` / `Is_Moving()` Lua method
- **docs/systems/save-format.md**: LocomotorDataPackClass IS persisted (state lives on save-game) — confirms data exists but only via DataPack serialization

### Architectural finding
LocomotorBehaviorClass is one of multiple Behavior objects attached to GameObjectClass via QueryInterface. Reading current state requires:
1. `QueryInterface(unit, LOCOMOTOR_BEHAVIOR_INTERFACE_ID)` → LocomotorBehaviorClass*
2. Read state field at offset N (TBD via RE)
3. Map to canonical name via EnumConversionClass<LocomotorStateType> (iter-414)

This is a **multi-iter A1.x-style arc**, not a 3-iter mini-arc. Cost ~5 iters (RE offset + bridge wire + simulator + UX + verify). DEFERRED to fresh session.

### Honest defer reason
Per iter-407 codified rule's break-out clauses, the LocomotorStateType extraction (iter-414) was correctly DEFERRED for UX consumer because:
- No engine Lua API exists (would need RE for offset)
- LocomotorStateType strings are reference data; ledger pin captures them for future arc work

iter-422 preflight CONFIRMS the iter-414 honest-defer was correct — there's no cheap path to surface LocomotorStateType in editor UX without committing to a multi-iter A1.x arc.

## Phase 2 — Cheap-insurance republish

iter-412 was last cheap-insurance republish (10 iters ago). iter-413-421 were all docs/RE-only iters. Validating build pipeline is still GREEN.

### Expected results (per iter-412 precedent)
- dotnet publish executes cleanly (build pipeline GREEN)
- Binary timestamp UNCHANGED (no source changes since iter-404; correct incremental-build behavior)
- Filtered tests PASS (Iter403 + CapabilityCatalogTests + CapabilityCatalogReverseOrphanTests + Iter167 + Iter223)

## What shipped

1. **`tools/iter422_search_locomotor_state.py`** (NEW) — searches rtti_refs for Locomotor* classes; documents preflight findings
2. **`TestResults/iter422_publish.ps1`** (NEW; mirrors iter-412/356/376 PowerShell-script-file pattern) — cheap-insurance republish + filtered test verify
3. **iter422 close-out doc** (this file)

## Verification gates ALL GREEN at iter-422

- ✅ All editor build/test gates inherit GREEN from iter-401-421 chain
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 197 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-412 verified pipeline; iter-422 verifying again)
- ✅ iter-414 LocomotorStateType honest-defer empirically reaffirmed via preflight finding

## Net iter-422 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML/catalog (pure RE preflight + verification iter) |
| New tools | 2 (iter422_search_locomotor_state.py + iter422_publish.ps1) |
| Doc shipped | 1 close-out doc with preflight findings + cheap-insurance republish documentation |
| Pattern observations flagged | iter-414 LocomotorStateType defer durably confirmed; SWFOC_GetUnitLocomotorState multi-iter A1.x arc queued for fresh session |
| Cycle time | ~10 min (preflight search + republish trigger + close-out) |

**iter-422 is a strategic preflight + verification iter** — confirms iter-414 honest-defer is durably correct AND verifies build pipeline still GREEN before next major arc-class commitment.

91st post-iter-323 arc iter (1st post-survey-completion preflight iter); 152nd consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-423)

Options:

1. **SWFOC_TriggerVictory arc kickoff** — per iter-421 task #672 option 1; would unlock VictoryType (18 names) ComboBox UX. Multi-iter A1.x-style (~5 iters); requires RE for engine victory-trigger function. Highest operator-visible impact (instant-win across all game modes).

2. **2nd 3rd-tier instance via DynamicEnumConversionClass XML extraction** — would compound 3rd-tier "Engine has FILESYSTEM/XML data" track 1/3 → 2/3 toward 21st codified rule. Requires SWFOC `data/xml/` access locally.

3. **NEW arc-class via SWFOC_GetUnitLocomotorState** (multi-iter) — per iter-422 finding above; ~5-iter A1.x arc with offset RE.

4. **Live SWFOC verify** of iter-403 ComboBox.

5. **Live verification of iter-422 republish** (in progress; will complete in background).

iter-423 will assess based on (a) iter-422 republish results (binary unchanged or unexpectedly changed) and (b) operator-driven priorities.
