# Iter 451 — SWFOC_TriggerVictory Phase 12: simulator handler + 8 pin tests

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc Phase 12 (canonical "iter-N+1 = simulator + tests" sibling to iter-450 bridge wrapper)
**Predecessor:** iter-450 (scaffolding LIVE: RVA pins + wrapper + DORMANT detour)
**Successor (queued):** iter-450a (active injection: capture-on-CTOR hook + AwaitingVictoryTest layout RE + flip MH_EnableHook) AND iter-452 (editor UX wire)

## What this iter does

iter-451 ships the C# editor-side simulator handler that mirrors the iter-450 bridge wrapper's input-validation contract. With this handler in place, editor unit tests can verify the wrapper's behavior without invoking a live SWFOC game — the canonical Phase 3 of every multi-iter A1.x arc (per iter-226/232/238/244/258/259 prior arcs).

## Why iter-451 ships before iter-450a (active injection)

iter-450 ships a wrapper whose ONLY observable behavior is input validation (the dormant detour doesn't fire). Pinning tests NOW (before iter-450a flips MH_EnableHook on) means iter-450a inherits a green test suite that catches both regression types:

1. **Validation-path regressions** — iter-451's tests catch any rewrite of Lua_TriggerVictory or HandleTriggerVictory that breaks the 14-name allow-list, the error-message contracts, or the PHASE2_PENDING staging shape.
2. **Injection-path regressions** — iter-450a will add NEW tests on top of iter-451's pin set, asserting that the actual MinHook injection lands an AwaitingVictoryTest into VictoryMonitor's vector.

If iter-451 came AFTER iter-450a, an iter-450a regression would conflate "validation broke" with "injection broke" and the operator wouldn't know which to fix first.

## What shipped

### 1. FakeGameState.cs — 2 new fields

```csharp
/// <summary>iter-451: SWFOC_TriggerVictory pending-state mirror.</summary>
public bool VictoryTriggerPending { get; set; } = false;

/// <summary>iter-451: companion holding the validated victory_type.</summary>
public string VictoryTriggerType { get; set; } = "";
```

These mirror the bridge's `g_victoryTriggerPending` + `g_victoryTriggerType` globals (iter-450 lua_bridge.cpp).

### 2. SwfocSimulator.cs — registration + handler + known-types array

- **Registration**: `Reg("return SWFOC_TriggerVictory", HandleTriggerVictory);` (after the FreezeCredits block; iter-451 sibling)
- **Known-types array**: 14-of-18 names (Galactic_Conquer / Galactic_Control / Galactic_Cycles / Galactic_Kill_Enemy / Galactic_Super_Weapon / Skirmish_All_Enemies / Skirmish_Control / Skirmish_Enemy_Capitulate / Skirmish_Space_Eradication / Sub_Tactical_All / Sub_Tactical_Enemy / Sub_Tactical_Land / Sub_Tactical_Space / Sub_Tactical_Story) — exact same set as the bridge wrapper. iter-450a will extract the remaining 4 entries from the full enum-init decompile.
- **Handler**: ~30 LoC inline string-arg parser (extracts content between parens, trims quotes/whitespace, validates against known-types, stages pending state, returns same PHASE2_PENDING / ERR_NO_ARG / ERR_BAD_ARG / ERR_UNKNOWN_TYPE error taxonomy as the bridge wrapper).

### 3. NEW Iter451_TriggerVictoryHandlerTests.cs — 8 pin tests

| # | Test | Asserts |
|---|---|---|
| 1 | `NoArg_ReturnsErrNoArg` | Empty parens → ERR_NO_ARG; pending stays false |
| 2 | `EmptyString_ReturnsErrBadArg` | Empty quoted string → ERR_BAD_ARG; pending stays false |
| 3 | `UnknownType_ReturnsErrUnknownTypeWithLedgerCitation` | Unknown name → ERR_UNKNOWN_TYPE; **error message must cite `rva_victory_type_enum_init`** (operator audit trail) |
| 4 | `ValidType_StagesPendingAndReturnsPhase2Pending` | Galactic_Conquer → PHASE2_PENDING; pending = true; type = arg |
| 5 | `SubTacticalStory_StagesAcrossEnumPrefixFamily` | Sub_Tactical_* family also accepted (3-family coverage) |
| 6 | `SkirmishControl_RoundTripsCorrectly` | Skirmish_* family + response references "iter-450a" (operator knows resolution iter) |
| 7 | `SecondCall_OverwritesFirstStaging` | Most-recent valid call wins (semantic contract) |
| 8 | `InvalidAfterValid_LeavesPriorStagingIntact` | Invalid call after valid does NOT clear prior staging (atomicity contract) |

**All 8 PASSED in 26ms.**

## Verification gates (all GREEN)

| Gate | Result | Notes |
|---|---|---|
| Editor build (test project) | ✅ PASS | All 12 projects compiled clean (after 2 quick syntax fixes; see toolchain notes below) |
| Filtered test run | ✅ 8/0/0 in 26ms | `--filter "FullyQualifiedName~Iter451_TriggerVictory"` |
| Bridge harness | ✅ 1100/0 (inherited from iter-450) | 223+ consecutive iters zero-regression |
| Ledger lint | ✅ 0/0 (inherited from iter-450) | 339 entries |

## Toolchain notes — 2 quick syntax fixes mid-iter

The first test-run attempt failed with 2 categories of compile errors:

1. **`NamedPipeLuaBridgeClient` namespace** — wrote test file with only `using SwfocTrainer.App.V2.Infrastructure;` but `NamedPipeLuaBridgeClient` lives in `SwfocTrainer.Core.Services` (per the Grep find at `src\SwfocTrainer.Core\Services\NamedPipeLuaBridgeClient.cs:194`). Added the missing `using`.
2. **`BridgeRoundTripResult.RawResponse` field name** — the record has `Response`, not `RawResponse` (per `record struct BridgeRoundTripResult(bool Succeeded, string Response, string ErrorMessage)`). Used `Edit replace_all` for `round.RawResponse` → `round.Response`, but missed `round2.RawResponse` (used by 2 tests for second-call semantics). Second `replace_all` for `round2.RawResponse` caught those.

Net debugging cost: ~2 minutes. The iter-172 hardened toolchain (tee + line-buffered + blame-hang-timeout + filtered) made both errors visible in the first failed run.

**NEW codification candidate at 1-instance trigger**: "When `replace_all` a field-rename across test files, scan for sibling identifiers (round / round2 / roundTrip / rt) BEFORE running tests, OR run a follow-up grep for the old name to confirm zero matches before claiming the rename is complete." Will track for codification at 2-3 instance trigger.

## Net iter-451 outcome

| Aspect | Value |
|---|---|
| LoC shipped | ~25 C# (FakeGameState fields) + ~50 C# (simulator handler + known-types array + Reg) + ~190 C# (8-test pin file) = ~265 LoC |
| New tools | 0 |
| Doc shipped | 1 close-out (this file) + 1 ralph_loop_state.md entry |
| Pattern observations | 1 NEW (replace_all sibling-identifier scan; 1/3 codification trigger) |
| Cycle time | ~12 min (3 file edits + 2 build/test cycles + 2 syntax fixes + close-out) |

120th post-iter-323 arc iter; 12th A1.x arc iter (iter-440 to iter-451).

## SWFOC_TriggerVictory arc state at iter-451 close

**Arc shipped 12 of estimated 12-13 iters**:
- ✅ iter-440 to iter-449 = 10 iters of progressive RE
- ✅ iter-450 = scaffolding (RVA pins + wrapper + DORMANT detour)
- ✅ iter-451 = simulator handler + 8 pin tests (this iter)
- ⏸️ iter-450a (next-session): RE AwaitingVictoryTest 48-byte struct layout + add capture-on-CTOR hook at 0x341850 + populate inject branch + flip MH_EnableHook
- ⏸️ iter-452 (next-session): Editor UX wire (Camera & Debug or NEW Victory tab; SWFOC_TriggerVictory button + ComboBox for VictoryType)
- ⏸️ iter-453 (next-session): Live verify + close-out + operator changelog supplement13

## Cumulative this conversation continuation (29 iters: 423-451)

- 2 NEW codified rules (#21 event-driven defer + #22 rationale-extension-application)
- 29 close-out docs + 19 new tools
- iter-368 + iter-426 + iter-373 rules MATURE
- 5 cheap-insurance republishes
- 4-of-4 doc surfaces COHERENT (iter-435 closure)
- SWFOC_TriggerVictory A1.x arc 12/12-13 iters complete (scaffolding LIVE + simulator/tests LIVE; injection queued for iter-450a)
- Bridge harness 1100/0 sustained for **223 consecutive iters**
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE
- 23rd codified rule candidate at 5-instance trigger ("body inspection beyond RTTI")
- 24th codification candidate at 1/3 trigger: DORMANT MinHook scaffolding sub-pattern (iter-450)
- 25th codification candidate at 1/3 trigger: replace_all sibling-identifier scan (iter-451 toolchain note)
- **iter-450 ledger growth**: 336 → 339 entries (sustained at iter-451)
- **iter-451 test growth**: +8 simulator pin tests for Lua_TriggerVictory wrapper input validation contract

## Next iter (450a; NEXT SESSION) — Active injection

iter-450a still has the same scope per the iter-450 close-out:

1. Decompile-walk VictoryMonitorClass CTOR @ 0x341850 → extract AwaitingVictoryTest 48-byte struct layout
2. Add capture-on-CTOR hook at 0x341850 in lua_bridge.cpp (stores `this` as `g_capturedVictoryMonitor`)
3. Populate Hook_VictoryMonitorCounter inject branch with conditional injection
4. Flip MH_EnableHook on for both new hooks (CTOR + counter_inc)
5. Add bridge harness pin tests for round-trip + capture path

After iter-450a lands, iter-451's `ValidType_StagesPendingAndReturnsPhase2Pending` test will fail (PHASE2_PENDING → "ok") — that's the trigger to flip the simulator's return string + extend tests to assert injection semantics.

iter-451 closes the SIMULATOR phase. iter-450a closes the INJECTION phase.
