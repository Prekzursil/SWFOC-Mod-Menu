# Iter 440 — SWFOC_TriggerVictory A1.x arc kickoff (RE Phase 1: VictoryMonitorClass dissection)

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc kickoff (mirrors iter-224-228 SetFireRate arc structure)
**Predecessor:** iter-439 (conversation session-close pause point)
**Successor (queued):** iter-441 (Phase 2: bridge wire via direct field write OR MinHook detour)

## What this iter does

iter-440 is **iter 1 of 5 in the SWFOC_TriggerVictory A1.x arc**. RE callgraph dissection of VictoryMonitorClass + StoryEventVictoryClass to map the engine's victory-trigger surface.

Per iter-423 preflight (durable defer); iter-426 codification documented the cost; iter-439 close-out RECOMMENDED this arc as iter-440. Now committed.

## RE findings

### VictoryMonitorClass cluster (3 functions in 360-byte address span)

| Addr | Size | Likely role |
|---|---|---|
| 0x140341850 | 358 bytes | **Tick handler** — iterates AwaitingVictoryTests vector each frame |
| 0x1403419C0 | 98 bytes | **Helper** — likely Add() / Remove() / count getter |
| 0x140341AF0 | 329 bytes | **Test evaluator** — likely runs each AwaitingVictoryTest's pass/fail check |

This cluster is **MUCH smaller than typical multi-iter A1.x targets** (e.g. SetFireRate spanned ~3000 bytes across WeaponClass + WeaponTick at 0x387010 + dispatchers). VictoryMonitorClass's tight ~360-byte footprint suggests:
- **3-iter arc may be feasible** instead of 5-iter (if iter-441 finds the "victory triggered" flag offset early)
- **Direct field write may suffice** vs MinHook detour (smaller surface = simpler hook)

### StoryEventVictoryClass at 0x140453310 (2508 bytes)

LARGE function — likely the "fire victory event" implementation. Three approaches:
- **Approach A**: Read this function to find Lua-callable "Get_Player_Wins" or similar getter; if exists, cheap Lua-API path opens up.
- **Approach B**: Hook entry point of 0x140453310 with MinHook detour to inject custom victory call.
- **Approach C**: Hook a CALLER of 0x140453310 (e.g. 0x14035E560 / 0x14035F970 game-loop callers) to inject victory test that always passes.

Approach A is iter-302 (engine-already-does-this) shape — would invalidate iter-423 preflight finding if Lua API exists. Worth checking iter-441 Phase 2 BEFORE committing to MinHook approach.

### Callers of VictoryMonitor tick (0x140341850)

- 0x14035E560 — likely the game-loop tick dispatcher
- 0x14035F970 — likely an alternate entry (mode-specific?)

These are the hook points if **Approach C** (caller-side injection) is chosen. iter-441 will RE 0x14035E560 to confirm.

### EnumConversionClass<VictoryType> @ 0x140341FF0

Already mined iter-414 (18 victory type names: Galactic_Conquest_Win/Loss, Tactical_Win/Loss, Galactic_Conquest_Player_1_Win, etc.). The 18 names are the operator-visible options for the future SWFOC_TriggerVictory(victory_type) Lua wire.

## Architectural cost confirmation

iter-426 codified rule predicted ~5-iter A1.x arc. iter-440 dissection refines:
- **3 iters minimum** (iter-440 RE + iter-441 wire + iter-442 verify) IF Approach A finds Lua API
- **5 iters typical** (iter-440 RE + iter-441 hook + iter-442 simulator + iter-443 UX + iter-444 verify) IF Approach B/C
- **6+ iters worst case** if hook fails first attempt

iter-440 confirms the arc IS feasible at the standard SWFOC A1.x cost range — not the multi-multi-iter horror that iter-423 preflight feared.

## What shipped

1. **`tools/iter440_victory_monitor_dissection.py`** (NEW; ~80 LoC) — callgraph dissection tool
2. **iter-440 close-out doc** (this file) — RE findings + arc strategy

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-439 chain (this iter is pure RE; no source changes)
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 213 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 165561163 bytes at May 7 14:58 (iter-436 baseline; UNCHANGED this iter)
- ✅ Callgraph SQLite query produced clean dissection findings (3 VictoryMonitor + 1 StoryEvent + 18 VictoryType + 2 callers identified)

## Net iter-440 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure RE iter) |
| New tools | 1 (iter440_victory_monitor_dissection.py) |
| Doc shipped | 1 close-out doc with RE findings + 3 approach options + arc strategy |
| Pattern observations | 1 (VictoryMonitorClass cluster is unexpectedly tight; arc may be 3-iter not 5-iter; iter-302 Approach A worth pursuing first) |
| Cycle time | ~10 min (dissection script + run + close-out) |

**iter-440 successfully kicks off the SWFOC_TriggerVictory A1.x arc** — RE confirms cluster is MUCH smaller than feared; 3 distinct hook approaches identified; Approach A (search for Lua API in StoryEventVictoryClass) recommended for iter-441 BEFORE committing to MinHook.

109th post-iter-323 arc iter (19th post-survey-completion iter); 170th consecutive NON-A1.x iter per iter-269 lesson #2 — **NOTE: this iter starts an A1.x arc, breaking the 169-iter NON-A1.x streak**. The streak counter resets at iter-441 (first hook iter); iter-440 is the RE preflight.

## Next iter (iter-441) — Phase 2: bridge wire

Per iter-440 dissection findings, iter-441 strategy:

**Step 1**: Decompile 0x140453310 (StoryEventVictoryClass; 2508 bytes) — search for `Get_Player_Wins` / `Trigger_Victory` / `Set_Victorious` Lua-callable getter. If found → Approach A (~30-50 LoC bridge wire via DoString). If NOT found → continue to Step 2.

**Step 2**: Decompile 0x140341AF0 (test evaluator; 329 bytes) — find offset to "victory triggered" flag inside AwaitingVictoryTestType struct. If clear → Approach B (MinHook detour at 0x140341850 tick handler that injects always-true test once).

**Step 3**: Fallback Approach C — hook caller 0x14035E560 / 0x14035F970 (game-loop dispatcher) with conditional injection that triggers victory when SWFOC_TriggerVictory(victory_type) is called.

Recommended start: Step 1 (Approach A search). 30-60 min RE; cheap if successful, worth it before committing to ~2-hour MinHook implementation.

iter-441 close-out should include: chosen approach + bridge wire LoC + simulator handler scaffolding (deferred to iter-442 per iter-224-228 arc shape).
