# Iter 450a kickoff — VictoryMonitor instance layout decoded from CTOR ASM

**Date:** 2026-05-20
**Class:** Active-injection prerequisite RE
**Predecessor:** iter-450 (DORMANT MinHook scaffolding LIVE)
**Successor (planned):** iter-450b (capture-on-CTOR hook + flip MH_EnableHook → LIVE)

## What this kickoff adds

iter-450 shipped a DORMANT MinHook detour on `0x140341FE0` (counter-increment
helper). Active injection was deferred per `feedback_event_driven_defer_pattern.md`
because two prerequisites were missing:

1. **Discriminator problem**: `0x140341FE0` fires for many engine subsystems
   beyond `VictoryMonitor`. Need a way to identify which `rcx` belongs to a
   `VictoryMonitorClass` instance vs other callers.
2. **AwaitingVictoryTest struct layout unknown**: size pinned at 48 bytes, the
   vector container at instance+0x68, but field offsets within the type body
   never decoded.

This doc opens iter-450a by extracting the VictoryMonitor CTOR's instance
layout from the IDA decompile corpus (`full_b84-85.json` → `0x140341850`).

## Discriminator: VictoryMonitor instance signature

The CTOR at `0x140341850` writes the AwaitingVictoryTestType vtable to the
instance at offset `+0x60`:

```asm
140341879  lea rax, ??_7?$DynamicVectorClass@UAwaitingVictoryTestType@VictoryMonitorClass@@@@6B@
1403418a0  mov [rcx+60h], rax
```

**This is the discriminator.** Hook strategy for iter-450b:

- At every call of `0x140341FE0`, read `*(uint64_t*)(rcx + 0x60)` and compare
  to the captured RTTI vtable address of `??_7?$DynamicVectorClass@U...`.
- If they match, this `rcx` is a `VictoryMonitor` instance → run our inject
  branch.
- If not, call the original (counter-inc for some other subsystem) unchanged.

No need to capture-on-CTOR; the vtable identity at +0x60 is the discriminator.
This is **simpler than the iter-449 capture-on-CTOR plan** because we read the
vtable directly from `rcx` at every detour call.

## VictoryMonitor instance layout (decoded from CTOR ASM)

Confirmed offsets within a `VictoryMonitor` instance (`rdi = rcx` in CTOR):

| Offset | Type | Initial value | Role |
|--------|------|---------------|------|
| +0x00 | qword | `rdx` (CTOR arg 2) | Owner / parent pointer |
| +0x08 | qword | 0 | (unknown) |
| +0x10 | qword | 0 | (unknown) |
| +0x18 | qword | 0 | (unknown) |
| +0x20 | byte | 0 | (unknown flag) |
| +0x24 | dword | 0 | (unknown counter) |
| +0x28 | qword | `0xFFFFFFFFFFFFFFFF` | Sentinel (commonly "last index = -1") |
| +0x30 | qword | 0 | (unknown) |
| +0x38 | dword | 0 | (unknown) |
| +0x3C | word | 0 | (unknown) |
| +0x40 | qword | 0 | (unknown) |
| +0x48 | qword | 0 | (unknown) |
| +0x50 | qword | 0 | (unknown) |
| +0x58 | dword | `r8d` (CTOR arg 3) | Win-condition mode / type (32-bit) |
| +0x5C | dword | 0 | (unknown) |
| **+0x60** | **qword (vtable ptr)** | **`AwaitingVictoryTestType` vtable** | **DynamicVectorClass header** |
| +0x68 | qword | 0 | Vector data pointer (`vec.data`) |
| +0x70 | dword | 0 | Vector count (`vec.count`) |
| +0x74 | dword | 0 (high bit = heap-allocated flag) | Vector capacity + heap-allocated flag (`0x80000000`) |
| +0x78 | word | 0 | (unknown) |
| +0x7A | byte | 0 | (unknown) |

**Vector pre-existing teardown path** (CTOR's clear-existing-vec branch starts
at `0x1403418c4`):

```c
if (this->vec_data_ptr) {  // +0x68 non-null = vector has data
    if (this->vec_capacity_and_flag & 0x80000000) {  // +0x74 high bit set
        HeapFree(GetProcessHeap(), 0, this->vec_data_ptr);
    } else {
        j_j_free(this->vec_data_ptr);  // CRT free
    }
    this->vec_capacity_and_flag &= 0x80000000;   // keep just the flag bit
    this->vec_data_ptr = 0;
    this->vec_count = 0;
}
```

This tells us that **for the inject path we want to use HeapAlloc** (so the
`0x80000000` heap-allocated flag is set and the engine cleans it up via
`HeapFree` rather than mismatched-allocator `j_j_free`).

## Next step for iter-450a

1. Cross-reference the AwaitingVictoryTestType vtable to find its CTOR(s) and
   one example instantiation site in the corpus to decode the type's body
   layout. The vector is `DynamicVectorClass<AwaitingVictoryTestType>` so the
   element size (48 bytes per iter-440-449) plus how each instance is
   initialized will reveal what field offsets exist within the awaiting-test
   record.
2. Write the discriminator-aware detour in `swfoc_lua_bridge/lua_bridge.cpp`:
   ```cpp
   // inside Hook_VictoryMonitorCounter
   uint64_t* rcx_inst = reinterpret_cast<uint64_t*>(rcx);
   if (rcx_inst[0x60 / 8] != g_VictoryMonitorVectorVtable) {
       return p_Original_VictoryMonitorCounter(rcx);   // not us
   }
   if (g_PendingVictoryTrigger) {
       g_PendingVictoryTrigger = false;
       InjectAlwaysPassAwaitingTest(rcx_inst);  // implemented after step 1
   }
   return p_Original_VictoryMonitorCounter(rcx);
   ```
3. Pin the AwaitingVictoryTestType vtable RVA. The CTOR ASM shows it's loaded
   via `lea rax, ??_7?$DynamicVectorClass@U...` — extract the absolute address
   from the loaded image and add to `rvas.h`.

## Open questions

- Does the engine call `0x140341FE0` once per VictoryMonitor instance per tick,
  or once globally per tick? If global, the discriminator pattern is correct.
  If per-instance, we may catch the wrong instance on the first hit and need
  to maintain a tracked-set instead.

## Iter-450b corpus dig findings (2026-05-20)

A subagent re-decoded the AwaitingVictoryTestType layout from the IDA + Ghidra
corpus. **Major corrections to the layout assumptions above**:

1. **AwaitingVictoryTestType is POD, not polymorphic.** No element vtable
   exists. No `??0AwaitingVictoryTestType@...` constructor symbol. No function
   pointers in the element body.
2. **0x140341850 is the inner `DynamicVectorClass<AwaitingVictoryTestType>`
   CTOR, NOT the VictoryMonitor CTOR.** The earlier "VictoryMonitor instance
   layout" table in this doc reflects the DV<T> sub-object, not VictoryMonitor.
3. **The element vector lives at VictoryMonitor+0x08 / +0x10 / +0x18**
   (begin / end / cap_end), NOT at +0x60-0x78 as previously assumed.
4. **Element field layout (48 bytes confirmed)**:
   - `+0x00..+0x0F` — 16-byte opaque header, default-copied from `.rdata`
     template at `0x140804FC0`
   - `+0x10` — `uint32 TypeId` (the discriminator chosen by `kKnownVictoryTypes[]`)
   - `+0x14` — `uint8 Flag_A`
   - `+0x15` — `uint8 Flag_B`
   - `+0x16..+0x17` — padding
   - `+0x18..+0x2F` — `DynamicVectorClass<basic_string>` (begin/end/cap_end);
     each string is 32-byte stride
5. **Construction recipe** (programmatic inject of "always pass" element):
   ```cpp
   uint8_t elem[48] = {};
   memcpy(elem + 0x00, kDefaultTemplate_0x140804FC0, 16);
   *(uint32_t*)(elem + 0x10) = TYPE_ID_ALWAYS_PASS; // from kKnownVictoryTypes
   // +0x14/+0x15 stay 0 (no flag)
   // +0x18..+0x2F stays 0 (empty string vector — engine treats as no-args)
   // then append by:
   //   - if capacity sufficient: memcpy to (*(uint8_t**)(vm+0x10))
   //                             ; *(uint8_t**)(vm+0x10) += 0x30
   //   - else: call the engine's append helper (FUN_140340f30 candidate)
   ```
6. **Behavior dispatch** happens in `VictoryMonitorClass::Update` (large
   function at `sub_14035F970`), which switches on TypeId at `+0x10`. We do
   not need to know what `Update` does — we just need the engine's own
   "always_pass" type ID, which is one of the 14 names in `kKnownVictoryTypes[]`.

## Iter-450b implementation order (next session)

1. Dump 16 bytes from `.rdata` at RVA `0x804FC0` → embed in `lua_bridge.cpp`
   as `static const uint8_t kAwaitingVictoryTestDefaultTemplate[16] = { ... };`
2. Decompile `0x140344710` to find the **append/grow** helper signature.
   Likely takes `(DV<T>*, const Element*)` → returns void. Alternative is to
   call `0x140340f30` if that's the placement-new variant.
3. In `Hook_VictoryMonitorCounter` discriminator branch (vtable at +0x60):
   - Read TypeId chosen by the Lua-level pending state
   - Build the 48-byte element
   - Call the append helper
   - Set `MH_EnableHook` to enabled at module load
4. Add bridge harness tests: append → verify VM+0x10 advanced by 0x30;
   verify TypeId at element+0x10 == requested type
5. Flip catalog entry from `Phase2HookPending` → `Live`

## Why this is iter-450a kickoff not iter-450b complete

Active injection (`MH_EnableHook` flipped to enabled) needs:
- Discriminator pattern decoded ✅ (this doc)
- AwaitingVictoryTestType field layout ⏸ (needs IDA Pro live session for type cross-ref)
- HeapAlloc-side AwaitingVictoryTest builder ⏸ (depends on field layout)
- Detour body written + tested ⏸
- Build green + bridge harness pin tests ⏸

This kickoff gets us through the first of those 5 in one session and writes up
the path for the next ~3-4 iters. Per `feedback_event_driven_defer_pattern.md`,
the multi-iter arc cost is honest: ~10 RE iters + ~3 implementation iters.

## Status update for catalog

`SWFOC_TriggerVictory` remains `CapabilityStatus.Phase2HookPending`. The
operator-visible WorldState tab Trigger button (shipped iter-461) stays
PHASE 2 PENDING badged until iter-450b/c flips `MH_EnableHook` and the
discriminator-aware detour activates.

LIVE alternative for operator workflow: force conquest via Galactic tab
planet-owner-change + AI suspend (already LIVE).
