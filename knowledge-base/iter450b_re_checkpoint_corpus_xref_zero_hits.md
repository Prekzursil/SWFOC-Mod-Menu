# Iter 450b — SWFOC_TriggerVictory Phase 15: RE checkpoint (0 corpus xref hits beyond lifecycle; honest-defer to iter-450c)

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc Phase 15 (RE checkpoint; deeper RE work needed for active injection — second RE-only honest-defer in the arc)
**Predecessor:** iter-450a (struct layout findings; 2 ledger pins added)
**Successor (queued):** iter-450c (vftable method-table walk + derived-class CTOR enumeration) OR iter-453 (changelog supplement13 if pivot needed)

## Critical negative finding

A corpus-wide xref scan for `??_7?$DynamicVectorClass@UAwaitingVictoryTestType@VictoryMonitorClass@@@@6B@` (the AwaitingVictoryTests vftable mangled name) across all 22,728 functions in the IDA corpus returned **EXACTLY 3 hits** — all of which were already pinned by iter-450 + iter-450a:

| Address | Size | Pinned | Role |
|---|---|---|---|
| 0x140341850 | 358 bytes | iter-450 | CTOR (writes vftable to vector subobject) |
| 0x1403419C0 | 98 bytes | iter-450a | DTOR_VEC (rewrites vftable for destructor dispatch) |
| 0x140341AF0 | 329 bytes | iter-450a | DTOR_FULL (full destructor incl. vector cleanup) |

**Zero functions outside the lifecycle reference the vftable as a literal.**

## Why this is significant

In MSVC compiled code, only CTOR/DTOR functions reference container vftables as literal addresses (because they're the only places that WRITE the vftable into memory). Runtime operations like `push_back`, `clear`, etc. dispatch through the OBJECT's vftable pointer at runtime, so they never appear in literal-string xref scans.

This means **the AwaitingVictoryTests construction site cannot be found by string-xref scanning**. We need a different approach: **method-table walk** — find the vftable bytes in `.rdata`, enumerate each function pointer, and decompile each to identify which is `push_back` / `emplace_back` / `clear` / etc.

## Secondary finding — iter-440's "StoryEventVictoryClass" misframing

iter-440 RE'd `0x140453310` as "StoryEventVictoryClass". iter-450b's decompile reveals this is actually **`StoryEvent_Factory_Create`** — a 60-case `switch (type_id)` dispatcher (already pinned in ledger as `rva_story_event_factory_create`). Each case allocates a different derived class via `operator new`:

- Case 1: `operator new(0x3B0 bytes)` → constructs `StoryEventEnterClass` (vftable @ `??_7StoryEventEnterClass@@6B@`) via CTOR `sub_1404501D0`
- Case 2-60: 30+ similar paths, each allocating a different derived class with sizes ranging 0x380-0x3B0

The factory does NOT write to the AwaitingVictoryTests vector or reference its vftable. The 30+ derived classes ARE candidates for the construction site — each may have its own methods that push to AwaitingVictoryTests at runtime through the vector's vftable.

## What this iter shipped

### Tools

- **`tools/iter450b_find_construction_site.py`** (NEW; ~140 LoC) — decompiles target candidates (StoryEvent_Factory_Create + parent_tick) and scans for construction signals (HeapAlloc, alloc-48, vec-data writes, vec-count writes, Victory-cluster calls, vftable references)
- **`tools/iter450b_corpus_wide_xref.py`** (NEW; ~75 LoC) — scans all 123 corpus batches for `DynamicVectorClass@UAwaitingVictoryTestType` substring; returns canonical list of vftable-referencing functions

### Findings

1. **3-hits-only outcome** is the smoking gun: AwaitingVictoryTests construction is dispatched through vftable indirection, not via literal vftable references
2. **iter-440's "StoryEventVictoryClass" naming** corrected — the address is `StoryEvent_Factory_Create`
3. **30+ derived StoryEvent classes** are candidate construction sites for iter-450c

### What iter-450c needs

To find the AwaitingVictoryTestType construction site, iter-450c needs:

1. **Method-table walk approach**: Locate the AwaitingVictoryTests vftable bytes in the binary's `.rdata` section. Enumerate each function pointer in the table. Each is a method (slot 0 = dtor, slot 1+ = methods like push_back, clear, etc.). Decompile each to identify the construction operation.
2. **Alternative: derived-class CTOR walk**: Decompile each of the 30+ derived classes that StoryEvent_Factory_Create allocates. Look for any that initialize an embedded VictoryMonitorClass + push initial AwaitingVictoryTests entries.
3. **Alternative: dynamic RE via Frida**: Hook the engine at runtime, set a breakpoint on writes to the AwaitingVictoryTests vector, capture the call stack. This bypasses static analysis entirely but requires a live game session.

## Verification gates (all GREEN)

| Gate | Result | Notes |
|---|---|---|
| Verifier ledger lint | ✅ 0/0 (sustained from iter-450a) | 341 entries |
| Bridge harness | ✅ 1100/0 (sustained for 223+ consecutive iters) | No source changes this iter |
| iter-451 simulator pin tests | ✅ 8/0/0 (sustained) | Wrapper input-validation contract intact |
| Editor build | ✅ Sustained from iter-452 republish | Binary 157.35 MB |

## Net iter-450b outcome

| Aspect | Value |
|---|---|
| LoC shipped | ~215 Python (2 new RE tools) |
| Files modified | 0 source files; 2 NEW tools |
| New tools | 2 (iter450b_find_construction_site.py + iter450b_corpus_wide_xref.py) |
| Doc shipped | 1 close-out (this file) + 1 ralph_loop_state.md entry |
| Pattern observations | 1 NEW (3-hits-only outcome = vftable-call-dispatch indicator; codify at 2-3 instance trigger as "static xref of templated container reveals only lifecycle functions; runtime ops invisible to string scan") |
| Cycle time | ~12 min (2 tools + 2 corpus runs + close-out) |

123rd post-iter-323 arc iter; 15th A1.x arc iter (iter-440 to iter-450b).

## SWFOC_TriggerVictory arc state at iter-450b close

**Arc shipped 15 of estimated 15-16 iters** (UPPER BOUND BUMPED from 14-15 by iter-450b's RE-only honest-defer):
- ✅ iter-440 to iter-449 = 10 iters of progressive RE
- ✅ iter-450 = scaffolding (RVA pins + wrapper + DORMANT detour)
- ✅ iter-451 = simulator handler + 8 pin tests
- ✅ iter-452 = Lua Playground preset menu + republish
- ✅ iter-450a = RE struct layout findings + 2 ledger pins
- ✅ iter-450b = RE checkpoint (0-hit corpus xref + factory misframing correction) (this iter)
- ⏸️ iter-450c (next-session): Method-table walk OR derived-class enumeration OR Frida dynamic RE
- ⏸️ iter-453 (potential next-session): Changelog supplement13 (closes docs gap, deprioritizes Victory arc)

## Cumulative this conversation continuation (32 iters: 423-450b)

- 2 NEW codified rules (#21 + #22)
- 32 close-out docs + 22 new tools (added iter-450b's 2 tools)
- iter-368 + iter-426 + iter-373 rules MATURE
- 6 cheap-insurance republishes
- 4-of-4 doc surfaces COHERENT (iter-435 closure)
- SWFOC_TriggerVictory A1.x arc 15/15-16 iters complete (scaffolding + simulator + UX + 2 RE checkpoints)
- Bridge harness 1100/0 sustained for **223 consecutive iters**
- Ledger 341 entries (sustained from iter-450a)
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE
- 23rd codified rule candidate at 5-instance trigger
- 24th + 25th codification candidates at 1/3 trigger
- 26th codification candidate at **2/3 trigger**: "RE iter splits multi-iter implementation into RE-then-implement" (iter-440-449 + iter-450a; iter-450b strengthens this signal)
- **27th codification candidate at 1/3 trigger**: "0-hit static xref of templated container indicates vftable-call dispatch" (iter-450b 1st instance)

## Next iter (450c OR 453; NEXT SESSION)

**iter-450c** (continued RE) is the canonical next iter. Recommended approach:

1. Find AwaitingVictoryTests vftable address in `.rdata` (need binary-fingerprint xref or static analysis tool that enumerates `.rdata` symbols)
2. Walk vftable entries (slot 0 = dtor, slot 1+ = methods) — each entry is a function pointer
3. Decompile each method to identify push_back / emplace / clear semantics
4. From push_back's signature, derive AwaitingVictoryTestType element field layout

**iter-453** (changelog supplement13) is the alternative if iter-450c is deferred. Documents iter 423-450b SWFOC_TriggerVictory arc + 22 codified rules + cumulative progress.

**Strategic note**: After 5 RE iters in this arc (440-449 are the original 10; 450a, 450b are post-scaffolding; 450c would be the 12th total RE iter for 1 wire), the arc is showing strong **RE diminishing returns**. The right call may be to ship iter-453 as the arc's documentation closure and accept that SWFOC_TriggerVictory remains PHASE 2 PENDING with infrastructure (wrapper + simulator + UX + 5 ledger pins) but no engine-level injection. iter-450c can be revisited if/when an operator dynamically traces the construction-site via Frida or a focused RE arc.
