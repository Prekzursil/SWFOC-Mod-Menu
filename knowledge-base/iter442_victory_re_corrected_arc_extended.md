# Iter 442 — SWFOC_TriggerVictory arc Phase 3 RE: cluster reidentified (lifecycle, not tick); arc extended

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc Phase 3 (RE refinement; iter-440 cluster misidentification corrected)
**Predecessor:** iter-441 (Approach A confirmed-failed)
**Successor (queued):** iter-443 (next-session: hunt actual tick handler via xref walk on VictoryMonitorClass instance)

## What this iter does

iter-442 RE'd the 3 VictoryMonitorClass cluster functions (0x140341850 / 0x1403419C0 / 0x140341AF0) to extract AwaitingVictoryTestType struct layout. **CRITICAL FINDING: iter-440 misidentified the cluster.** The 3 functions are LIFECYCLE methods (constructor + 2 destructor variants), NOT the tick handler.

## RE findings (corrected)

### 0x140341850 (358 bytes) = VictoryMonitorClass CONSTRUCTOR

Asm pattern: sets vftable + initializes ~15 fields at offsets 0x8/0x10/0x14/0x18/0x28/0x30/0x38/0x3C/0x40/0x48/0x50/0x60/0x68/0x70/0x78/0x7A. Includes `mov qword ptr [rcx+28h], 0FFFFFFFFFFFFFFFFh` (typical "uninitialized handle" sentinel).

**NOT the tick handler.** This is the class init function called by the 2 callers iter-440 identified (0x14035E560 + 0x14035F970 = game-init paths, not per-tick).

### 0x1403419C0 (98 bytes) = AwaitingVictoryTests DynamicVectorClass DESTRUCTOR

Asm pattern: HeapFree/free decision based on [rcx+0x14] high bit; clears [rcx+8]/[rcx+0x10]/[rcx+0x14] (data/count/flags). Standard DynamicVectorClass<T> destructor.

### 0x140341AF0 (329 bytes) = VictoryMonitorClass FULL DESTRUCTOR

Asm pattern: iterates [rcx+0x18] entries with **stride 0x30 = 48 bytes** (`add rdi, 30h`), calling `sub_140066600` on each `[rdi+0x18]` (likely string destructor for inner field). Then frees the vector wrapper at offset +0x68.

**KEY ARCHITECTURAL FINDING**: `AwaitingVictoryTestType struct size = 0x30 bytes (48 bytes)`. This is essential metadata for any future hook implementation (entry size for iteration logic).

## Why iter-440 misidentified the cluster

iter-440 inferred "tick handler" from the largest function (0x140341850 = 358 bytes). But:
- All 3 functions are in the same 360-byte address span
- 358 bytes is consistent with a CONSTRUCTOR initializing ~15 fields
- The actual tick handler would have a polling loop with conditional branches calling test-evaluation logic
- iter-440 didn't decompile the bodies; only counted RTTI refs and sizes

Lesson: RTTI-ref count + function size are insufficient signals to identify FUNCTION ROLE. Need decompile body inspection.

## Implications for arc cost

iter-440 estimated **3-iter MIN** if Approach A succeeded. iter-441 confirmed Approach A failed. iter-442 reveals:

- iter-440's "VictoryMonitorClass cluster mapped" was incomplete — only LIFECYCLE methods mapped; the actual tick/poll handler is missing.
- **Arc requires additional iter (iter-443) to HUNT the tick handler** via xref walk on VictoryMonitorClass instance offsets that the constructor initialized.
- Updated arc estimate: **6-iter A1.x arc** (was 5):
  - iter-440 RE constructor cluster ✓
  - iter-441 Approach A confirmed-failed ✓
  - iter-442 RE struct layout + corrected cluster identification ✓ (this iter)
  - iter-443 Hunt actual tick handler (next-session)
  - iter-444 Implement MinHook detour
  - iter-445 Simulator + UX
  - iter-446 Verify + close-out + changelog

## Tick handler hunt strategy (iter-443)

Methods to find the tick handler:

**Method 1 — Xref walk on VictoryMonitorClass instance**:
- The constructor at 0x140341850 takes `rcx` = VictoryMonitorClass instance ptr.
- Find ALL functions that READ from this instance (not just construct/destroy).
- Filter by: read access to [instance+0x68] (the vector ptr) AND iteration loop pattern.

**Method 2 — Game-loop dispatcher trace**:
- Find the SWFOC main game loop (likely contains GameMode dispatcher).
- Trace VictoryMonitor.Tick() call chain from game-mode-tick.
- Likely path: GameMode::Tick → SubsystemList::Tick → VictoryMonitor::Tick → Iterate(AwaitingVictoryTests).

**Method 3 — String search for "victory" / "Win" in nearby asm**:
- Tick handler likely emits debug strings or victory-event-name strings during evaluation.
- Search asm corpus near 0x140341XXXX address space.

**Method 4 — Examine 0x14035E560 / 0x14035F970 callers**:
- iter-440 identified these as VictoryMonitorClass constructor callers.
- One of them might ALSO call the tick handler (game-init might wire the tick callback).

iter-443 should start with Method 1 (xref walk; cheapest).

## What shipped

1. **`tools/iter442_re_victory_test_evaluator.py`** (NEW; ~80 LoC) — RE tool that decompiles the 3 cluster functions
2. **iter-442 close-out doc** (this file) — RE corrections + arc cost extension + tick-handler-hunt strategy

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-441 chain (this iter is pure RE; no source changes)
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 215 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 165561163 bytes at May 7 14:58 (iter-436 baseline; UNCHANGED this iter)
- ✅ AwaitingVictoryTestType struct size confirmed at 0x30 bytes via destructor iteration stride

## Net iter-442 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure RE iter) |
| New tools | 1 (iter442_re_victory_test_evaluator.py) |
| Doc shipped | 1 close-out doc with RE corrections + arc extension + hunt strategy |
| Pattern observations | 2 NEW (RTTI-ref count insufficient for role-ID; struct-size from dtor-stride is reliable signal) |
| Architectural finding | AwaitingVictoryTestType = 48 bytes; VictoryMonitorClass.AwaitingVictoryTests vector at +0x68 |
| Cycle time | ~10 min (decompile script + run + close-out) |

**iter-442 is a productive RE-correction iter** — caught iter-440's role misidentification before committing to wrong hook target; extracted critical struct-size metadata for future hook implementation; identified 4 hunt strategies for tick handler.

111th post-iter-323 arc iter (21st post-survey-completion iter); 3rd A1.x arc iter (iter-440 + iter-441 + iter-442).

## SWFOC_TriggerVictory arc state at iter-442 close

**Arc shipped 3 of ~6 iters** (estimate revised from 5 to 6 due to iter-442 cluster misidentification):
- ✅ iter-440 RE Phase 1 (VictoryMonitorClass cluster mapped — partial; lifecycle only)
- ✅ iter-441 Approach A confirmed-failed (no Lua API in StoryEventVictoryClass)
- ✅ iter-442 Struct layout extracted + cluster correctly identified (lifecycle, not tick)
- ⏸️ iter-443 (next-session): Hunt actual tick handler via xref walk
- ⏸️ iter-444 (next-session): Implement MinHook detour at tick handler
- ⏸️ iter-445 (next-session): Simulator + UX
- ⏸️ iter-446 (next-session): Verify + close-out + changelog

## Next iter (iter-443; NEXT SESSION)

Hunt the actual VictoryMonitorClass tick handler:

**Step 1 (Method 1)**: Run callgraph SQLite query for all functions that read [instance+0x68] / [instance+0x18] / similar offsets where AwaitingVictoryTests vector lives. Filter by xref to VictoryMonitorClass instance.

**Step 2 (fallback Method 2)**: Trace game-loop call chain. Find SWFOC main game loop (GameMode dispatcher). Trace tick path.

**Step 3 (fallback Method 3)**: String search asm corpus near 0x140341XXX for "victory" / "win" / "test" debug strings.

**Step 4 (fallback Method 4)**: Examine 0x14035E560 + 0x14035F970 (constructor callers) — they may ALSO wire the tick callback.

Total cost estimate for iter-443: ~30-60 min RE (depending on which method succeeds first).

## Cumulative this conversation continuation (20 iters: 423-442)

Updated stats from iter-441:
- 2 NEW codified rules (#21 event-driven defer + #22 rationale-extension-application)
- 20 close-out docs + 12 new tools
- iter-368 + iter-426 + iter-373 rules MATURE
- 5 cheap-insurance republishes
- 4-of-4 doc surfaces COHERENT (iter-435 closure)
- SWFOC_TriggerVictory A1.x arc 3/6 iters complete (queued for next session)
- Bridge harness 1100/0 sustained for **215 consecutive iters**
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE

iter-442 advances the SWFOC_TriggerVictory arc with corrected cluster identification + extracted struct metadata; arc cost revised from 5 to 6 iters; clean stopping point for this conversation.
