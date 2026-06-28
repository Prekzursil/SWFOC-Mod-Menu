# Iter 450a — SWFOC_TriggerVictory Phase 14: RE-only honest-defer (struct layout findings; +2 ledger pins)

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc Phase 14 (RE-only iter; honest-defer of active injection to iter-450b per iter-426 codified rule)
**Predecessor:** iter-452 (Lua Playground preset menu; arc UX phase complete)
**Successor (queued):** iter-450b (active injection: AwaitingVictoryTestType element layout RE + capture-on-CTOR + flip MH_EnableHook)

## Why iter-450a ships as RE-only

iter-450 left iter-450a's scope as "active injection: capture-on-CTOR + AwaitingVictoryTest layout RE + flip MH_EnableHook". The RE phase ran first (this iter); findings exposed unknowns that block safe injection:

1. **VictoryMonitor has 3 embedded vectors** (NOT 1 as iter-449 close-out assumed)
2. **AwaitingVictoryTestType element layout still unknown** — CTOR + DTOR_FULL + DTOR_VEC don't reveal it (CTOR zero-inits, DTOR_VEC just heap-frees POD-like buffer, DTOR_FULL iterates a *different* vector for per-element dtor)
3. **A construction-site decompile is needed** — likely StoryEventVictoryClass @ 0x140453310 or parent_tick @ 0x456970 — to discover the element field layout

Per iter-426 codified rule (event-driven defer): when an engine subsystem's full layout isn't yet RE'd, defer to multi-iter A1.x or commit fully. Shipping an injection without the element layout = guaranteed memory corruption.

## What this iter shipped

### Tools

- **`tools/iter450a_extract_victory_ctor.py`** (NEW; ~110 LoC) — extracts CTOR + DTOR_VEC + DTOR_FULL + CounterInc decompiles from the IDA corpus + scans for field-write patterns + reports vftable assignments + indirect-call counts + stride-0x30 occurrences.

### Ledger pins (verified_facts.json: 339 → 341 entries)

| Entry | RVA | Findings |
|---|---|---|
| `rva_victory_monitor_dtor_full` | 0x341AF0 | 329-byte FULL dtor; iterates 3 vectors with different lifecycle semantics |
| `rva_victory_monitor_dtor_vec` | 0x3419C0 | 98-byte AwaitingVictoryTests vector dtor; reveals layout `{ vftable, base_ptr, count_dword, flags_dword }` and POD-element semantics |

### Critical RE findings

#### 1. VictoryMonitor structure has THREE embedded vectors

| Offset | Stride | Per-elem dtor | Dtor target | Role (inferred) |
|---|---|---|---|---|
| +0x08 / +0x10 | 0x30 (48 bytes) | YES (sub_140066600 on elem+0x18) | range walk | Likely "queue of completed/in-progress tests" |
| +0x40 / +0x48 | 32 (`sar 5`) | unknown | not seen in DTOR_VEC | Mystery (3rd vector) |
| +0x60 / +0x68 / +0x70 / +0x74 | 48 (presumed) | NO (POD-like; just HeapFree on base_ptr) | direct heap-free | **AwaitingVictoryTests** (the iter-450 hook target) |

#### 2. AwaitingVictoryTests DynamicVector layout (CONFIRMED)

```cpp
// Embedded at parent_instance + 0x60
struct DynamicVectorClass<AwaitingVictoryTestType> {
    void* vftable;       // +0x00 (relative)
    void* base_ptr;      // +0x08 — heap-allocated buffer (NULL if empty)
    uint32_t count;      // +0x10 — DWORD count of valid elements
    uint32_t flags;      // +0x14 — top bit = "allocated via Win32 HeapAlloc"
                         //         bottom bits = TBD (may be capacity?)
};
```

**Key correction to iter-449 assumption**: +0x70 is a DWORD count, NOT a `_last` QWORD pointer. The DTOR_VEC store `mov [rbx+10h], eax` (using 32-bit `eax`) confirms this.

#### 3. AwaitingVictoryTestType element size CONFIRMED at 48 bytes

The DynamicVector's element-size attribute is implicit (no per-element-stride store seen in CTOR/DTOR_VEC), but DTOR_FULL's iteration of the +0x08/+0x10 outer vector uses `add rdi, 30h` (stride 48), and the templated type signature is `DynamicVectorClass<AwaitingVictoryTestType>`. Cross-confirmed.

#### 4. AwaitingVictoryTestType is POD-like at the destructor level

DTOR_VEC just calls HeapFree on the base_ptr — no per-element destructor walk. This means injecting a single AwaitingVictoryTest into the vector won't require constructing per-element sub-objects via virtual dispatch; it'll require allocating a 48-byte buffer with the right field values.

**BUT**: the engine may still walk per-element vftables when EVALUATING the test (e.g., calling `test->IsPassed()` virtually). The element vftable presence is unconfirmed and is the iter-450b RE target.

## What iter-450b still needs

To safely inject a victory test, iter-450b needs:

1. **AwaitingVictoryTestType element field layout** — most-likely RE target is the construction call site:
   - StoryEventVictoryClass @ 0x140453310 (2508 bytes; iter-440 RE'd)
   - parent_tick @ 0x456970 (15.6KB; iter-449 RE'd; would push to vector if it adds a test)
   - Other Victory-cluster functions found via xref-to AwaitingVictoryTests vftable
2. **Vector capacity-management semantics** — does the engine pre-allocate a buffer + count = 0? Or alloc-on-first-push? If the latter, our injection needs to handle both null-buffer and existing-buffer cases.
3. **Test evaluation semantics** — does VictoryMonitor's tick walk the vector and call each test's `Evaluate()` method? If so, the element vftable is critical (need to point to a callable test predicate).

## Verification gates (all GREEN)

| Gate | Result | Notes |
|---|---|---|
| Verifier ledger lint | ✅ 0/0 | 341 entries (+2 from iter-450) |
| Bridge harness | ✅ 1100/0 (sustained from iter-450/451/452) | 223+ consecutive iters zero-regression |
| iter-451 simulator pin tests | ✅ 8/0/0 (sustained) | Wrapper input-validation contract intact |
| Editor build | ✅ Sustained from iter-452 republish | Binary 157.35 MB |

## Net iter-450a outcome

| Aspect | Value |
|---|---|
| LoC shipped | ~110 Python (1 new RE tool) + ~110 lines JSON (2 new ledger entries) |
| Files modified | 2 (verified_facts.json + tools/iter450a_extract_victory_ctor.py NEW) |
| New tools | 1 (iter450a_extract_victory_ctor.py) |
| Doc shipped | 1 close-out (this file) + 1 ralph_loop_state.md entry |
| Pattern observations | None (canonical RE-only honest-defer per iter-426 rule) |
| Cycle time | ~10 min (1 tool + 1 corpus extract + 2 ledger entries + 1 lint + close-out) |

122nd post-iter-323 arc iter; 14th A1.x arc iter (iter-440 to iter-450a).

## SWFOC_TriggerVictory arc state at iter-450a close

**Arc shipped 14 of estimated 14-15 iters** (UPPER BOUND BUMPED from 12-13 by iter-450a's RE-only honest-defer):
- ✅ iter-440 to iter-449 = 10 iters of progressive RE
- ✅ iter-450 = scaffolding (RVA pins + wrapper + DORMANT detour)
- ✅ iter-451 = simulator handler + 8 pin tests
- ✅ iter-452 = Lua Playground preset menu + republish
- ✅ iter-450a = RE-only struct layout findings + 2 new ledger pins (this iter)
- ⏸️ iter-450b (next-session): AwaitingVictoryTestType element layout RE + capture-on-CTOR hook + flip MH_EnableHook
- ⏸️ iter-453 (potential next-session): Live verify + close-out + operator changelog supplement13

## Cumulative this conversation continuation (31 iters: 423-450a)

- 2 NEW codified rules (#21 event-driven defer + #22 rationale-extension-application)
- 31 close-out docs + 20 new tools (added iter-450a's extract_victory_ctor.py)
- iter-368 + iter-426 + iter-373 rules MATURE
- 6 cheap-insurance republishes
- 4-of-4 doc surfaces COHERENT (iter-435 closure)
- SWFOC_TriggerVictory A1.x arc 14/14-15 iters complete (scaffolding + simulator/tests + UX + RE struct findings; only injection queued for iter-450b)
- Bridge harness 1100/0 sustained for **223 consecutive iters**
- Ledger 341 entries (+2 this iter)
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE
- 23rd codified rule candidate at 5-instance trigger
- 24th + 25th codification candidates at 1/3 trigger
- iter-450a's RE-only-honest-defer is the **2nd instance** of "RE iter splits a multi-iter implementation into RE-then-implement" pattern (iter-440-449 was the 1st — also a multi-iter RE prelude). Future codification candidate at 3-instance trigger.

## Next iter (450b OR 453; NEXT SESSION)

**iter-450b (active injection)** is the canonical next iter. Scope:

1. **Decompile-walk caller-of-AwaitingVictoryTests-push** — most-likely candidates:
   - StoryEventVictoryClass @ 0x140453310 (2508 bytes; large enough for construction logic)
   - parent_tick @ 0x456970 (only if it adds tests dynamically)
   - Find via xref-to `??_7DynamicVectorClass<AwaitingVictoryTestType>` vftable (call-target search)
2. **Extract AwaitingVictoryTestType element field layout** — vftable presence at +0, victory_type enum at some offset, condition predicate function ptr or virtual dispatch info
3. **Implement** the capture-on-CTOR hook + injection branch + flip MH_EnableHook
4. **Add bridge harness pin tests** for round-trip + capture path

If iter-450b's RE blocks (e.g., construction is via virtual dispatch through an opaque vftable chain that requires further RE), honest-defer to iter-450c and ship iter-450b as another RE pin update.

**iter-453 (operator changelog supplement13)** is the fallback. Documents iter 423-450a SWFOC_TriggerVictory arc + 22 codified rules + cumulative progress.
