# Iter 443 — SWFOC_TriggerVictory hunt Phase 4: tick handler has NO RTTI ref (deeper arc deferral)

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc Phase 4 (negative-result hunt; arc cost extended further)
**Predecessor:** iter-442 (Phase 3 RE; cluster reidentified as lifecycle methods)
**Successor (queued):** iter-444 (next-session: deeper RE on 0x140365300 DTOR caller OR pivot to operator-blocked live verify)

## What this iter does

iter-443 ran the 4-method tick-handler hunt strategy from iter-442. **CRITICAL FINDING: ALL 5 Victory-related RTTI functions are now mapped (3 lifecycle + EnumConversionClass + StoryEventVictoryClass) — there is NO 6th Victory-related RTTI function.** The actual tick handler must be calling VictoryMonitorClass via NON-virtual / NON-RTTI'd dispatch path.

## Hunt findings

### Method 1: All callers of CTOR + DTORs

| Function | Callers | Size | Likely role |
|---|---|---|---|
| 0x140341850 CTOR | 0x14035E560 (641 bytes) + 0x14035F970 (4088 bytes) | — | Game-init paths |
| 0x1403419C0 DTOR_VEC | 0x140341CA0 (52 bytes) | NEW | Small wrapper helper |
| 0x140341AF0 DTOR_FULL | 0x140365300 (564 bytes) | NEW | Engine-side VictoryMonitor manager |

**NEW**: 0x140341CA0 (52 bytes) and 0x140365300 (564 bytes) are previously unidentified VictoryMonitorClass-adjacent functions.

### Method 2: Examine known callers (0x14035E560 + 0x14035F970)

- 0x14035E560 (641 bytes): 0 RTTI refs; 14 callees including CTOR. Likely game-init wirer.
- 0x14035F970 (4088 bytes): 1 RTTI ref (`DynamicVectorClass<GameObjectClass *>`); 30+ callees. Likely a LARGE game-init function (game-mode-startup orchestrator).

### Method 3: ALL Victory-related RTTI functions

Total: **5 functions** (no others found):
1. 0x140341850 (CTOR; 358 bytes)
2. 0x1403419C0 (DTOR_VEC; 98 bytes)
3. 0x140341AF0 (DTOR_FULL; 329 bytes)
4. 0x140341FF0 (EnumConversionClass<VictoryType>; 3352 bytes; iter-414)
5. 0x140453310 (StoryEventVictoryClass; 2508 bytes; iter-441)

**The tick handler is NOT in this list.**

### Method 4: AwaitingVictoryTestType vftable references

Exactly 3 (matches Method 1 lifecycle methods). No additional functions reference the vector vftable.

## Critical architectural insight

VictoryMonitorClass tick handler must be called via:
- **Path A**: Non-virtual free function that takes `VictoryMonitorClass*` as parameter (no vftable lookup → no RTTI dependency)
- **Path B**: Virtual function on a NON-RTTI'd base class (e.g. abstract `SubsystemBase::Tick()` that doesn't emit RTTI)
- **Path C**: Function pointer stored in a global/static dispatch table (game-loop calls via callback ptr, not virtual)

Per the iter-442 constructor body inspection, VictoryMonitorClass appears to NOT have its own vftable (the only vftable assigned in the visible portion is for the AwaitingVictoryTests vector at +0x60). This supports Path A (non-virtual free function) being most likely.

**Implication**: Finding the tick handler requires WIDER RE — examine 0x140365300 (564 bytes; DTOR_FULL caller; probably engine-side manager) and 0x140341CA0 (52 bytes; small wrapper). One of these or their callers may contain the actual tick logic OR a function pointer to it.

## Arc cost re-extended: 6 → 7+ iters

iter-440 estimated 5 iters. iter-442 revised to 6. iter-443 reveals the tick handler hunt requires DEEPER RE than expected:
- iter-444 (next-session): RE 0x140365300 (564 bytes; engine-side manager) + 0x140341CA0 (52 bytes; wrapper) — find tick dispatch
- iter-445 (next-session): MinHook implementation at the FOUND tick handler
- iter-446 (next-session): Simulator + UX
- iter-447 (next-session): Verify + close-out
- iter-448 (next-session): Operator changelog supplement

Total: **7-iter arc** if iter-444 finds the dispatch path; potentially 8-9 if RE goes wider.

This is the EXACT outcome iter-426 codified rule predicted ("event-driven subsystems require multi-iter A1.x"). The arc IS feasible, just costlier than initial estimates.

## What shipped

1. **`tools/iter443_hunt_victory_tick_handler.py`** (NEW; ~110 LoC) — 4-method hunt tool
2. **iter-443 close-out doc** (this file) — hunt findings + Path A/B/C hypothesis + arc cost re-extension

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-442 chain (this iter is pure RE; no source changes)
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 216 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 165561163 bytes at May 7 14:58 (iter-436 baseline; UNCHANGED this iter)
- ✅ Tick handler hunt empirically ruled out RTTI-based discovery; redirects to non-virtual hunt

## Net iter-443 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure RE iter) |
| New tools | 1 (iter443_hunt_victory_tick_handler.py) |
| Doc shipped | 1 close-out doc with negative-result + Path A/B/C hypothesis + arc extension |
| Pattern observations | 1 NEW (tick handler invisible to RTTI search; must hunt via non-virtual / non-RTTI path) |
| Cycle time | ~10 min (hunt script + run + close-out) |

**iter-443 is a productive negative-result iter** — confirms the RTTI-search exhaustively covered all Victory-related classes; the tick handler MUST be reached via non-virtual dispatch. Identifies 2 NEW investigation targets (0x140365300 + 0x140341CA0) for iter-444 deeper RE.

112th post-iter-323 arc iter (22nd post-survey-completion iter); 4th A1.x arc iter (iter-440 + iter-441 + iter-442 + iter-443).

## SWFOC_TriggerVictory arc state at iter-443 close

**Arc shipped 4 of ~7 iters** (cost re-extended from 5 → 6 → 7):
- ✅ iter-440 RE Phase 1 (cluster mapped — partial; lifecycle only)
- ✅ iter-441 Approach A confirmed-failed (no Lua API)
- ✅ iter-442 Struct layout extracted + cluster reidentified
- ✅ iter-443 Tick handler hunt — NO RTTI match; needs non-virtual investigation
- ⏸️ iter-444 (next-session): RE 0x140365300 + 0x140341CA0 to find tick dispatch path
- ⏸️ iter-445 (next-session): MinHook implementation
- ⏸️ iter-446 (next-session): Simulator + UX
- ⏸️ iter-447 (next-session): Verify + close-out + changelog

## Next iter (iter-444; NEXT SESSION)

Step 1: Decompile 0x140365300 (564 bytes; DTOR_FULL caller). Look for:
- Function pointer stored in field offset (suggests Path C dispatch table)
- Direct call to VictoryMonitor tick at known offset (suggests Path A free-function)
- Inheritance pattern from a NON-RTTI'd base class (suggests Path B)

Step 2: Decompile 0x140341CA0 (52 bytes; tiny DTOR_VEC wrapper). Inspect for clues — too small to be tick but might be a "Reset()" or "Clear()" called from tick.

Step 3: If Step 1+2 find dispatch path → ship MinHook in iter-445.

Step 4: If Step 1+2 fail → consider broader corpus search for `add rdi, 30h` (AwaitingVictoryTestType iteration stride) — the tick handler MUST iterate the vector with this stride.

## Cumulative this conversation continuation (21 iters: 423-443)

Updated stats from iter-442:
- 2 NEW codified rules (#21 event-driven defer + #22 rationale-extension-application)
- 21 close-out docs + 13 new tools
- iter-368 + iter-426 + iter-373 rules MATURE
- 5 cheap-insurance republishes
- 4-of-4 doc surfaces COHERENT (iter-435 closure)
- SWFOC_TriggerVictory A1.x arc 4/7 iters complete (queued for next session)
- Bridge harness 1100/0 sustained for **216 consecutive iters**
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE

iter-443 advances the SWFOC_TriggerVictory arc with negative-result confirmation that RTTI-search exhausted; tick handler hunt needs non-virtual investigation in next session; arc cost revised 5 → 6 → 7 iters.
