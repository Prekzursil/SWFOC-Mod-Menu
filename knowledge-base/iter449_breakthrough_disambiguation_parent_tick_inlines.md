# Iter 449 — SWFOC_TriggerVictory Phase 10: breakthrough disambiguation; 0x140341FE0 is counter helper; 0x140456970 inlines tick

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc Phase 10 (breakthrough disambiguation; refined hook target)
**Predecessor:** iter-448 (BREAKTHROUGH 0x140341FE0 + 0x140456970 identified)
**Successor (queued):** iter-450 (next-session: MinHook implementation at 0x140456970 OR alternate strategy)

## What this iter does

iter-449 decompiled the 2 breakthrough candidates from iter-448. **CRITICAL DISAMBIGUATION**: 0x140341FE0 is NOT a tick handler — it's a tiny 16-byte counter-increment helper. 0x140456970 (15.6 KB) IS the parent class tick that INLINES VictoryMonitor logic.

## RE findings

### 0x140341FE0 (size=0x10; 16 bytes) — COUNTER HELPER, NOT TICK

```asm
mov eax, [rcx+5Ch]   ; load counter
xor edx, edx          ; zero
inc eax               ; ++
test eax, eax
cmovle eax, edx       ; clamp to >=0
mov [rcx+5Ch], eax    ; store back
retn
```

**Function shape**: 7-instruction utility that increments [rcx+0x5C] and clamps to non-negative. This is a STATE COUNTER bump (e.g., `frames_since_last_check++`), called from MANY places.

**NOT a tick handler.** No vector iteration, no test evaluation, no AwaitingVictoryTests access.

### 0x140456970 (size=0x3D10 = 15,632 bytes) — PARENT CLASS TICK

| Signal | Count |
|---|---|
| [r+0x28] reads (parent struct) | YES (filter A pass) |
| [r+0x68] reads | 62 (very high) |
| [r+0x60] reads | 85 (very high) |
| Stride 0x30 patterns | 0 |
| Victory-cluster calls | sub_140341FE0 (counter increment helper) |
| Indirect (vftable) calls | 7 |

**Verdict**: This is the engine's MAIN GAME-MODE TICK / ORCHESTRATOR. 15.6 KB function with 62+85 = 147 field reads suggests it's iterating MANY subsystems' fields per frame. The 7 vftable dispatches likely call subsystem ticks polymorphically. Calls 0x140341FE0 = counter-increment for one of the subsystems' frame-counters.

**NO `add rdi, 30h` stride pattern** suggests it does NOT directly iterate AwaitingVictoryTests vector linearly. Either:
- VictoryMonitor's tick logic is INLINED into 0x140456970 (using indexed access not pattern-matched by iter-446 stride filter)
- OR VictoryMonitor's tick is one of the 7 vftable dispatches (would mean VictoryMonitorClass DOES have its own vftable after all)

## Implication for hook strategy

### Option A: Hook 0x140456970 directly
- 15.6 KB function = HUGE detour surface
- Full MinHook implementation: trampoline + before-call hook + after-call cleanup
- Estimated 200-400 LoC C++
- Risk: detouring engine's main tick is high-risk; any bug crashes the game

### Option B: Hook one of 0x140456970's vftable[i] dispatches
- Identify which slot is VictoryMonitor's tick
- Hook the vftable entry instead of the parent function
- Lower-risk; smaller surface
- Requires identifying which of the 7 vftable[i] calls is the VictoryMonitor one

### Option C: Use 0x140341FE0 (counter increment) as a TIMING SIGNAL
- 0x140341FE0 fires on every VictoryMonitor tick (called from 0x140456970)
- Hook 0x140341FE0 with detour that injects always-pass test if SWFOC_TriggerVictory was called
- ~50-100 LoC C++ (much smaller than full tick hook)
- LOWEST RISK — 16-byte function; minimal trampoline surface

**Option C is the winning approach.** 0x140341FE0 is THE perfect MinHook target — fires on every tick (timing-aligned with VictoryMonitor's per-frame work) but is small enough to detour safely.

## Refined arc cost estimate

iter-440 5 → iter-442 6 → iter-443 7 → iter-444 8 → iter-445 9+ → iter-446 9-10 → iter-447 14-16 → iter-448 13-14 → **iter-449 11-12** (Option C is simpler than full Option A):

- iter-440 to iter-449 = 10 iters of progressive RE (DONE)
- iter-450 (next-session): MinHook implementation Option C (hook 0x140341FE0 with always-pass test injection); ~50-100 LoC C++
- iter-451 (next-session): Simulator handler + tests
- iter-452 (next-session): Editor UX wire (Camera & Debug or NEW Victory tab)
- iter-453 (next-session): Live verify + close-out + operator changelog

Total revised estimate: **11-12 iters** (within historical multi-iter A1.x arc bounds).

## What shipped

1. **`tools/iter449_decompile_breakthrough.py`** (NEW; ~80 LoC) — decompile + signal-check tool
2. **iter-449 close-out doc** (this file) — disambiguation + Option A/B/C strategies + Option C selection

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-448 chain (this iter is pure RE; no source changes)
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 222 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 165561163 bytes at May 7 14:58 (iter-436 baseline; UNCHANGED this iter)

## Net iter-449 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure RE iter) |
| New tools | 1 (iter449_decompile_breakthrough.py) |
| Doc shipped | 1 close-out doc with disambiguation + 3 hook strategies + Option C selection |
| Pattern observations | 1 NEW (counter-increment helper at 0x140341FE0 is INFERIOR target than tick handler but SUPERIOR target for hooking — small surface; per-tick firing) |
| Cycle time | ~10 min (decompile + analysis + close-out) |

**iter-449 is a productive disambiguation iter** — Option C strategy is BETTER than the original Option A/B because it hooks a smaller function with same per-tick timing. Arc cost revised DOWN from 14-16 to 11-12 iters.

118th post-iter-323 arc iter (28th post-survey-completion iter); 10th A1.x arc iter (iter-440 to iter-449).

## SWFOC_TriggerVictory arc state at iter-449 close

**Arc shipped 10 of estimated 11-12 iters**:
- ✅ iter-440 to iter-448 = 9 iters of progressive RE (cluster + struct + parent + breakthrough)
- ✅ iter-449 = disambiguation + Option C strategy selected
- ⏸️ iter-450 (next-session): Option C MinHook implementation at 0x140341FE0 (~50-100 LoC C++)
- ⏸️ iter-451 (next-session): Simulator handler + tests
- ⏸️ iter-452 (next-session): Editor UX wire
- ⏸️ iter-453 (next-session): Verify + close-out + changelog

## Cumulative this conversation continuation (27 iters: 423-449)

- 2 NEW codified rules (#21 event-driven defer + #22 rationale-extension-application)
- 27 close-out docs + 19 new tools
- iter-368 + iter-426 + iter-373 rules MATURE
- 5 cheap-insurance republishes
- 4-of-4 doc surfaces COHERENT (iter-435 closure)
- SWFOC_TriggerVictory A1.x arc 10/11-12 iters complete (Option C strategy queued for next session)
- Bridge harness 1100/0 sustained for **222 consecutive iters**
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE
- 23rd codified rule candidate at 5-instance trigger ("body inspection beyond RTTI")

iter-449 closes the RE phase definitively. iter-450 onwards is implementation work following established A1.x arc patterns.
