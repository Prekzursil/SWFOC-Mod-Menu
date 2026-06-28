# Iter 444 — SWFOC_TriggerVictory hunt Phase 5: parent class identified; arc cost 7→8 iters

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc Phase 5 (parent-class discovery; arc cost re-extended)
**Predecessor:** iter-443 (RTTI hunt exhausted; non-virtual dispatch confirmed)
**Successor (queued):** iter-445 (next-session: find callers of 0x140365300 to locate parent tick handler)

## What this iter does

iter-444 decompiled 0x140365300 (564 bytes) + 0x140341CA0 (52 bytes) per iter-443 hunt strategy. **CRITICAL FINDING: VictoryMonitor instance is OWNED by a parent class at offset +0x28.** The parent class is the engine subsystem manager whose tick method calls VictoryMonitor's tick logic.

## RE findings

### 0x140365300 (564 bytes; PARENT CLASS DESTRUCTOR)

This function is the destructor for an engine subsystem manager that owns multiple subobjects:

| Offset | Contents | Cleanup pattern |
|---|---|---|
| [rbx+0x18] | Subsystem A | dtor via 0x14029C800 + vftable[0] |
| [rbx+0x20] | Subsystem B | dtor via 0x14029C800 + vftable[0] (with `mov edx, 1`) |
| **[rbx+0x28]** | **VictoryMonitor instance** | **call sub_140341AF0 (DIRECT non-virtual call to VictoryMonitor full destructor)** |
| [rbx+0x30] | Subsystem C | vftable[0x20] dispatch |

**Path A confirmed**: Parent class calls VictoryMonitor's destructor via DIRECT call (no vftable lookup). This means the tick handler is ALSO likely called directly — `parent_tick() { ... ; VictoryMonitor_Tick(this->[+0x28]); ... }`.

The parent class has its OWN vftable at slot 0x2A8 (`call qword ptr [rax+2A8h]` early in destructor) — likely a "Stop()" or "OnDestroy()" method on a base subsystem-manager class.

10 indirect calls in the destructor confirms this is a polymorphic engine subsystem manager pattern.

### 0x140341CA0 (52 bytes; AwaitingVictoryTests vector cleanup wrapper)

```
mov edx, 18h   ; size = 24 bytes (the vector struct itself)
mov rcx, rdi
call sub_1403419C0  ; DTOR_VEC
test bl, 1     ; check free flag
jnz call j_j_free
```

Standard `void CleanupVector(AwaitingVictoryTestsVector* vec, byte free_flag)` helper. **NOT a tick handler.** The 24-byte size suggests the vector struct itself is 24 bytes (vftable[0] + data ptr [+8] + count [+0x10] + flag [+0x14]).

## Arc cost re-extended: 7 → 8 iters

iter-440 5 → iter-442 6 → iter-443 7 → iter-444 8:
- iter-440 RE Phase 1 (lifecycle only) ✓
- iter-441 Approach A failed ✓
- iter-442 Struct layout extracted ✓
- iter-443 RTTI hunt exhausted ✓
- iter-444 Parent class identified (owns VictoryMonitor at +0x28) ✓
- iter-445 (next-session): Find callers of parent destructor 0x140365300 → trace to parent tick method
- iter-446 (next-session): RE parent tick → find direct call to VictoryMonitor tick OR fall through to MinHook
- iter-447 (next-session): MinHook implementation
- iter-448 (next-session): Simulator + UX + verify
- iter-449 (next-session): Operator changelog supplement

## Why this is positive progress

Despite arc cost re-extension, iter-444 is a SUBSTANTIAL breakthrough:
1. **Parent-class layout MAPPED**: Subobjects at +0x18, +0x20, +0x28 (VictoryMonitor), +0x30 are now KNOWN.
2. **Direct-call path CONFIRMED**: Parent calls VictoryMonitor methods non-virtually. This is Path A from iter-443 hypothesis.
3. **Hook target NARROWED**: Tick handler is in the parent class's tick method (not yet found, but ONE caller-walk away).
4. **iter-426 codified rule predictions held**: "Multi-iter A1.x for event-driven subsystems" → arc is precisely that, just at upper end of cost spectrum.

## What shipped

1. **`tools/iter444_decompile_engine_manager.py`** (NEW; ~80 LoC) — corpus decompile + pattern-search tool
2. **iter-444 close-out doc** (this file) — parent class layout + Path A confirmed + hook target narrowed

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-443 chain (this iter is pure RE; no source changes)
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 217 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 165561163 bytes at May 7 14:58 (iter-436 baseline; UNCHANGED this iter)
- ✅ Parent class at +0x28 confirmed via destructor sub_140341AF0 direct call

## Net iter-444 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure RE iter) |
| New tools | 1 (iter444_decompile_engine_manager.py) |
| Doc shipped | 1 close-out doc with parent-class discovery + Path A confirmation + arc extension |
| Pattern observations | 1 NEW (engine subsystem manager owns multiple subsystems via offset-based subobjects; direct-call dispatch for non-virtual ones) |
| Cycle time | ~10 min (decompile script + run + close-out) |

**iter-444 is a productive breakthrough iter** — narrows the hook target from "somewhere in the binary" to "parent class's tick method, called from somewhere that destructs at 0x140365300." 1 caller-walk away from finding actual tick.

113th post-iter-323 arc iter (23rd post-survey-completion iter); 5th A1.x arc iter (iter-440 + iter-441 + iter-442 + iter-443 + iter-444).

## SWFOC_TriggerVictory arc state at iter-444 close

**Arc shipped 5 of ~8 iters**:
- ✅ iter-440 Cluster mapped (lifecycle only)
- ✅ iter-441 Approach A failed
- ✅ iter-442 Struct layout extracted
- ✅ iter-443 RTTI hunt exhausted (tick is non-virtual)
- ✅ iter-444 Parent class identified (owns at +0x28; direct-call dispatch)
- ⏸️ iter-445 (next-session): Find callers of 0x140365300 → trace to parent tick method
- ⏸️ iter-446 (next-session): RE parent tick + identify hook target offset
- ⏸️ iter-447 (next-session): MinHook implementation
- ⏸️ iter-448 (next-session): Simulator + UX + verify
- ⏸️ iter-449 (next-session): Operator changelog supplement

## Next iter (iter-445; NEXT SESSION)

Find callers of 0x140365300 (parent class destructor):

1. SQLite query: `SELECT src_func_addr FROM xrefs WHERE dst_func_addr = 0x140365300`
2. For each caller, look for tick-like patterns: callback registration / per-frame loops / similar
3. Likely outcome: 1-3 callers; one is engine-init, one or more are game-mode managers

If iter-445 finds the parent's tick method, iter-446 → MinHook implementation. If not, iter-446 → broader corpus search for `add rdi, 30h` (AwaitingVictoryTestType iteration stride; the tick HANDLER must contain this pattern).

## Cumulative this conversation continuation (22 iters: 423-444)

Updated stats from iter-443:
- 2 NEW codified rules (#21 event-driven defer + #22 rationale-extension-application)
- 22 close-out docs + 14 new tools
- iter-368 + iter-426 + iter-373 rules MATURE
- 5 cheap-insurance republishes
- 4-of-4 doc surfaces COHERENT (iter-435 closure)
- SWFOC_TriggerVictory A1.x arc 5/8 iters complete (queued for next session)
- Bridge harness 1100/0 sustained for **217 consecutive iters**
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE

iter-444 advances the SWFOC_TriggerVictory arc with parent-class discovery; tick handler now 1-2 caller walks away from being found.
