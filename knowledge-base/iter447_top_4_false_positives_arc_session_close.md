# Iter 447 — SWFOC_TriggerVictory Phase 8: top 4 candidates ALL false positives; arc session-close

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc Phase 8 (negative-result; session-close declared)
**Predecessor:** iter-446 (192 candidates from stride search; top 4 ranked)
**Successor (queued):** iter-448 (NEXT SESSION; refined hunt strategy needed — stride+offset filter was wrong)

## What this iter does

iter-447 decompiled all 4 top stride candidates from iter-446. **CRITICAL FINDING: NONE are VictoryMonitor tick handlers — all 4 are false positives.** Stride+offset filter combination is insufficient to identify the tick handler.

## RE findings — top 4 candidates analysis

| # | Address | [r+0x28]? | Victory calls? | Actual content |
|---|---|---|---|---|
| 1 | 0x140461850 | NO | NONE | StoryReward processing (`aStoryRewardErr_93`) |
| 2 | 0x14065AA20 | NO | NONE | Math/comparison routine with vector iteration |
| 3 | 0x140213A50 | NO | NONE | Different subsystem (5 reads at +0x60) |
| 4 | 0x14023C9A0 | NO | NONE | Different subsystem (6 reads at +0x60) |

**All 4 lack the parent-struct read at +0x28** that is THE DEFINING signature of a tick handler accessing VictoryMonitor (per iter-444's parent-class layout discovery).

**All 4 lack direct calls to victory cluster** (sub_140341XXX) which a tick handler should make.

## Implications for hunt strategy

Stride 0x30 + offset +0x60/+0x68 are NOT specific enough to identify the VictoryMonitor tick. False positive rate at top 4 is 4/4 = 100%, which means:

1. **The actual tick handler MAY NOT use a linear `add r, 30h` iteration**: Maybe accesses tests by index lookup (`mov rax, [base+rdi*0x30]`) without the `add r, 30h` increment pattern. iter-446 filter would miss this.

2. **OR the tick handler does NOT iterate AwaitingVictoryTests at all in the recognizable loop pattern**: Maybe checks one test per external call (game-loop polls VictoryMonitor.CheckTest() once per frame, which checks the front of the queue + advances). This would have NO stride loop.

3. **OR the tick handler is INLINED into the parent class's tick**: VictoryMonitor's "test loop" might be expanded inline in the parent class's per-frame method, with no separate VictoryMonitor::Tick() function.

## Refined hunt strategy for next session

Per iter-426 codified rule + iter-444 parent-struct finding, the ACTUAL tick handler must:
- Read from a parent struct that has VictoryMonitor at offset +0x28
- Either iterate AwaitingVictoryTests OR call sub_140341XXX functions

**Better filter for iter-448**:
- Filter A: Functions reading `[rcx+28]` AND `[rcx+28+68]` = `[rcx+90h]` (reaching INTO VictoryMonitor's vector through parent indirection)
- Filter B: Functions calling sub_140341XXX (any of the 5 Victory-cluster functions including hidden helper functions not yet identified)
- Filter C: Functions with `mov rax, [base + rdi*0x30]` patterns (indexed access to AwaitingVictoryTestType array)

Combine 2 of 3 filters for stronger signal.

## Pragmatic assessment

The SWFOC_TriggerVictory arc has now spent **8 RE iters** (iter-440 to iter-447) without locating the tick handler. The iter-426 codified rule's prediction held: this IS multi-iter A1.x heavy work. But the cost is now SIGNIFICANTLY higher than the original 5-iter estimate.

Honest assessment options:
- **A**: Continue hunt with refined filters in iter-448+ (estimated 2-4 more iters to find tick + 4 more iters to ship MinHook + UX = 14-16 total iters)
- **B**: Pause arc; document findings; pivot to other concrete work; resume when fresh session focus is available

Per iter-422 LocomotorState honest-defer pattern + iter-441 Approach A confirmed-failed pattern, **Option B is the right call** — preserve all RE artifacts, declare session-close, queue refined hunt strategy for next session.

## Session-close declaration

iter-447 declares the SWFOC_TriggerVictory arc temporarily PAUSED at the RE phase. All artifacts preserved:

✅ **Architectural map (iter-440 to iter-446)**:
- VictoryMonitorClass cluster at 0x140341850-0x140341AF0 (lifecycle: ctor + 2 dtors)
- AwaitingVictoryTestType struct = 48 bytes (stride 0x30)
- AwaitingVictoryTests vector at instance+0x68
- Parent class owns VictoryMonitor at +0x28
- Path A confirmed (non-virtual direct calls)
- 5 Victory-related RTTI functions exhaustively mapped
- 192 stride+offset candidates inventoried; top 4 ALL false positives at iter-447

❌ **Still missing**:
- Parent class's TICK method (the actual hook target)
- Mechanism by which game-loop calls the parent's tick

🔧 **Refined hunt strategy queued for iter-448**:
- Filter A: `[rcx+28]` AND `[rcx+90]` (parent indirection to VictoryMonitor vector ptr)
- Filter B: ANY victory-cluster call (broader than current filter)
- Filter C: indexed access pattern `[base+idx*0x30]`
- 2-of-3 combined filter

## What shipped

1. **`tools/iter447_decompile_top_candidates.py`** (NEW; ~70 LoC) — decompile + signal-check tool
2. **iter-447 close-out doc** (this file) — false-positive analysis + refined hunt strategy + session-close declaration

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-446 chain (this iter is pure RE; no source changes)
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 220 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 165561163 bytes at May 7 14:58 (iter-436 baseline; UNCHANGED this iter)
- ✅ False-positive rate empirically confirmed at iter-446 filter

## Net iter-447 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure RE iter) |
| New tools | 1 (iter447_decompile_top_candidates.py) |
| Doc shipped | 1 close-out doc with false-positive analysis + refined strategy + session-close |
| Pattern observations | 1 NEW (stride+offset 100% false positive at top 4; need parent-struct indirection filter) |
| Cycle time | ~10 min (decompile + analysis + close-out) |

**iter-447 is a productive negative-result iter** — empirically rules out iter-446's filter; refines the hunt strategy with 3 new filter approaches; preserves all arc RE artifacts cleanly.

116th post-iter-323 arc iter (26th post-survey-completion iter); 8th A1.x arc iter (iter-440 to iter-447).

## SWFOC_TriggerVictory arc state at iter-447 close

**Arc shipped 8 of estimated 14-16 iters** (cost re-extended significantly):
- ✅ iter-440 to iter-446 = 7 iters of progressive RE
- ✅ iter-447 = top 4 candidates ALL false positives (this iter)
- ⏸️ iter-448 (next-session): Refined hunt with 3-filter combination
- ⏸️ iter-449-450 (next-session): Decompile narrowed candidates
- ⏸️ iter-451 (next-session): MinHook implementation once tick confirmed
- ⏸️ iter-452 (next-session): Simulator + UX
- ⏸️ iter-453 (next-session): Verify + close-out + changelog

## Cumulative this conversation continuation (25 iters: 423-447)

- 2 NEW codified rules (#21 event-driven defer + #22 rationale-extension-application)
- 25 close-out docs + 17 new tools
- iter-368 + iter-426 + iter-373 rules MATURE
- 5 cheap-insurance republishes
- 4-of-4 doc surfaces COHERENT (iter-435 closure)
- SWFOC_TriggerVictory A1.x arc 8/14-16 iters complete (queued for next session)
- Bridge harness 1100/0 sustained for **220 consecutive iters**
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE
- 23rd codified rule candidate at 4-instance trigger ("body inspection beyond RTTI")

iter-447 is the natural session-close — RE phase exhausted current filter; refined strategy queued for fresh-session focus.

## Why pause now is correct

1. **25 iters in this conversation** matches/exceeds prior productive-stretch sizes (iter-100-126 = 27, iter-129-149 = 21, iter-159-190 = 32). Diminishing returns.
2. **All gates GREEN** — no broken state to clean up.
3. **All RE artifacts preserved** — close-outs + tools document every finding for fresh-session resumption.
4. **Refined hunt strategy queued** — iter-448 has clear next steps (3-filter combination); won't be wheel-spinning.
5. **Multi-iter A1.x arcs benefit from focused sessions** — iter-441 + iter-447 honest-defer pattern repeated.
6. **The codified rule predictions ARE holding** — iter-426 said multi-iter; iter-447 confirms. Honest cost-revision (5→6→7→8→9→10→14-16) reflects empirical discovery, not estimation drift.

iter-448 onwards belongs to the next session.
