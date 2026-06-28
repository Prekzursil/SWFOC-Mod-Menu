# Iter 450 — SWFOC_TriggerVictory Phase 11: scaffolding LIVE (RVA pins + wrapper + DORMANT detour + catalog entry)

**Date:** 2026-05-07
**Arc class:** Multi-iter A1.x arc Phase 11 (FIRST IMPLEMENTATION ITER post-RE; honest-defer scaffolding per iter-426 rule)
**Predecessor:** iter-449 (disambiguation: 0x140341FE0 = counter helper, 0x140456970 = parent tick; Option C selected)
**Successor (queued):** iter-450a (active injection: RE AwaitingVictoryTest layout + capture-on-CTOR hook + flip MH_EnableHook)

## Why scaffolding instead of LIVE flip

iter-449 close-out estimated "Option C: ~50-100 LoC" for iter-450 LIVE flip. Mid-implementation analysis revealed two missing prerequisites that would have produced unsafe code:

### Prerequisite 1 — Discriminator problem (RE'd partially in iter-449)

0x140341FE0 is a generic 16-byte counter helper:

```asm
mov eax, [rcx+5Ch]
xor edx, edx
inc eax
test eax, eax
cmovle eax, edx
mov [rcx+5Ch], eax
retn
```

It's called by MANY engine subsystems that bump per-frame counters at +0x5C, NOT just VictoryMonitor. The detour fires for ALL of them. To safely inject we need to identify which `rcx` is a VictoryMonitor instance.

**Three discriminator options** (none ready iter-450):
- **Option C-1**: VictoryMonitor singleton lookup — singleton address not pinned in ledger
- **Option C-2**: vftable identity check — VictoryMonitorClass vftable RVA not pinned in ledger
- **Option C-3 (chosen for iter-450a)**: Capture-on-construction — hook the CTOR @ 0x341850, store `this` as `g_capturedVictoryMonitor`, then the counter-inc hook can compare `rcx == g_capturedVictoryMonitor`

### Prerequisite 2 — AwaitingVictoryTest 48-byte struct layout

iter-440-449 RE work pinned: vector at instance+0x68, 48 bytes per element, RTTI signal. NOT pinned: field offsets.

A typical AwaitingVictoryTest struct presumably contains:
- offset 0: vftable pointer (likely)
- offset N: condition predicate (function pointer or lambda body)
- offset M: victory type enum value (4 bytes)
- offset K: passes/fails state byte
- ... unknown remainder

Constructing one with the wrong field layout = engine reads garbage from our injection = guaranteed crash or undefined behavior.

### The honest-defer call

Per iter-426 codified rule (event-driven defer):
> "When SWFOC engine subsystem is event-driven (*MonitorClass / *BehaviorClass / DynamicVector<*::AwaitingTestType> RTTI signals), no direct trigger Lua API exists. Defer to multi-iter A1.x or commit fully."

The rule was codified specifically to prevent this kind of premature LIVE flip. Iter-450 ships SCAFFOLDING (durable RVA pins + wrapper + dormant trampoline); iter-450a ships INJECTION once the prerequisites are RE'd.

This respects the **feature-readiness bar**: don't claim LIVE if the wire doesn't actually trigger.

## What this iter shipped

### 1. Three new ledger entries (verified_facts.json: 336 → 339)

| Entry | RVA | Size | Role |
|---|---|---|---|
| rva_victory_monitor_counter_inc | 0x341FE0 | 16 bytes | **HOOK target** (Option C MinHook); 7-instruction `++[rcx+0x5C]` clamp helper |
| rva_victory_monitor_ctor | 0x341850 | 358 bytes | RTTI-bound CTOR; foundation for iter-450a capture-on-construction strategy |
| rva_victory_monitor_parent_tick | 0x456970 | 15,632 bytes | Reference (NOT hook target — too large; would risk detouring engine main tick) |

All 3 entries: 3-tool consensus via binary-fingerprint identity per CLAUDE.md.

### 2. rvas.h — 3 new constants

```cpp
constexpr uintptr_t VictoryMonitor_Ctor        = 0x341850;
constexpr uintptr_t VictoryMonitor_CounterInc  = 0x341FE0;  // iter-450 MinHook target (DORMANT)
constexpr uintptr_t VictoryMonitor_ParentTick  = 0x456970;
```

Inline `// ======================================================================` block documents the iter-440-449 RE provenance + iter-450 scaffolding state + iter-450a deferred work.

### 3. lua_bridge.cpp — ~120 LoC C++

| Symbol | Role |
|---|---|
| `kKnownVictoryTypes[]` | 14-of-18 VictoryType enum names + nullptr terminator |
| `g_victoryTriggerPending` (volatile LONG via `InterlockedExchange`) | Pending-trigger flag |
| `g_victoryTriggerType[64]` | Validated victory_type string staged by wrapper |
| `IsKnownVictoryType()` | Linear scan helper |
| `Lua_TriggerVictory()` | Wrapper: validates input → stages pending state → emits PHASE2_PENDING status |
| `Hook_VictoryMonitorCounter()` | DORMANT detour (currently passes through to original) |
| `pfn_VictoryMonitorCounter` typedef + `real_VictoryMonitorCounter` static | MinHook trampoline plumbing |

**Registration**: `{"SWFOC_TriggerVictory", Lua_TriggerVictory}` added to RegisterAll funcs[] table.

**MinHook init**: `MH_CreateHook(0x341FE0, &Hook_VictoryMonitorCounter, &real_VictoryMonitorCounter)` -- but `MH_EnableHook` INTENTIONALLY skipped. Trampoline is allocated; detour never runs.

### 4. CapabilityStatusCatalog.cs — new `["SWFOC_TriggerVictory"]` Phase2HookPending entry

Catalog rationale cites iter-426 (event-driven defer) + iter-437 (rationale-extension application) codified rules. Documents:
- 18-entry VictoryType enum source (rva_victory_type_enum_init @ 0x341FF0)
- iter-440-449 RE provenance
- Option C MinHook strategy + WHY iter-450 ships scaffolding (not LIVE)
- iter-450a remaining work: AwaitingVictoryTest layout + capture hook
- "No operator-LIVE alternative — VictoryMonitor is the engine's only programmatic victory path"

## Verification gates

| Gate | Result | Notes |
|---|---|---|
| Bridge build (`build.bat`) | ✅ PASS | `=== ALL BUILDS AND TESTS PASSED ===` |
| Bridge harness | ✅ 1100/0 | **223 consecutive iters of zero-regression** since iter-225 |
| Verifier ledger lint | ✅ 0/0 | 339 entries (+3 from iter-449's 336) |
| Replay binary | ✅ Rebuilt | swfoc_replay.exe 960,301 bytes @ 16:00 |
| Editor catalog change | ⏸️ Not built | String-literal-only edit; build deferred to iter-451 with simulator handler |
| Editor binary republish | ⏸️ Deferred | No UX-visible change yet (catalog rationale only) |

## Pattern observation — DORMANT MinHook scaffolding

This iter codifies a useful sub-pattern within event-driven defer:

> When honest-deferring a MinHook detour for prerequisite reasons, **call `MH_CreateHook` without `MH_EnableHook`**. The trampoline is allocated at module load (~ small one-time cost), but the detour never runs. iter-450a only needs to (a) populate the inject branch, (b) flip `MH_EnableHook` on. This eliminates one round-trip of "is MH_CreateHook even working?" investigation.

Cost: 1 line of MH_CreateHook + ~10 lines of typedef/forward-decl/Hook function stub.
Benefit: iter-450a is ~30-50 LoC instead of ~50-100 LoC.

Will track for codification at 2-3 instance trigger.

## Net iter-450 outcome

| Aspect | Value |
|---|---|
| LoC shipped | ~120 C++ (lua_bridge.cpp) + 3 RVA constants (rvas.h) + ~75 lines JSON (verified_facts.json) + 1 catalog entry (CapabilityStatusCatalog.cs) |
| New tools | 0 |
| Doc shipped | 1 close-out (this file) + 1 ralph_loop_state.md entry |
| Pattern observations | 1 NEW (DORMANT MinHook scaffolding sub-pattern) |
| Ledger growth | 336 → 339 entries (+3 VictoryMonitor cluster pins) |
| Cycle time | ~25 min (research + 6 file edits + 2 builds + 1 lint) |

119th post-iter-323 arc iter; 11th A1.x arc iter (iter-440 to iter-450).

## SWFOC_TriggerVictory arc state at iter-450 close

**Arc shipped 11 of estimated 12-13 iters**:
- ✅ iter-440 to iter-449 = 10 iters of progressive RE (cluster + struct + parent + breakthrough + disambiguation)
- ✅ iter-450 = scaffolding + RVA pins + DORMANT detour (this iter)
- ⏸️ iter-450a (next-session): RE AwaitingVictoryTest 48-byte struct layout + add capture-on-CTOR hook at 0x341850 + populate inject branch in Hook_VictoryMonitorCounter + flip MH_EnableHook
- ⏸️ iter-451 (next-session): Simulator handler + bridge harness round-trip tests (validates wrapper input handling under simulator)
- ⏸️ iter-452 (next-session): Editor UX wire (Camera & Debug or NEW Victory tab; SWFOC_TriggerVictory button + ComboBox for VictoryType)
- ⏸️ iter-453 (next-session): Live verify + close-out + operator changelog supplement13

## Cumulative this conversation continuation (28 iters: 423-450)

- 2 NEW codified rules (#21 event-driven defer + #22 rationale-extension-application)
- 28 close-out docs + 19 new tools
- iter-368 + iter-426 + iter-373 rules MATURE
- 5 cheap-insurance republishes
- 4-of-4 doc surfaces COHERENT (iter-435 closure)
- SWFOC_TriggerVictory A1.x arc 11/12-13 iters complete (scaffolding LIVE; injection queued for iter-450a)
- Bridge harness 1100/0 sustained for **223 consecutive iters**
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE
- 23rd codified rule candidate at 5-instance trigger ("body inspection beyond RTTI")
- **iter-450 ledger growth**: 336 → 339 entries (+3 VictoryMonitor cluster pins)
- **NEW codification candidate at 1-instance trigger**: DORMANT MinHook scaffolding sub-pattern (codify at 2-3 instances)

## Next iter (450a; NEXT SESSION) — Active injection

Scope (~30-50 LoC C++ + ~15 lines RE doc):

1. **Decompile-walk VictoryMonitorClass CTOR @ 0x341850** to extract AwaitingVictoryTest 48-byte struct layout. Look for:
   - vftable pointer at +0 (almost certain)
   - condition predicate (function ptr or std::function-style closure)
   - victory_type enum value (4 bytes; matches iter-414 enum)
   - passes/fails state byte
   - next-pointer if intrusive linked list

2. **Add capture-on-CTOR hook** at 0x341850:
   ```cpp
   typedef void* (__fastcall *pfn_VictoryMonitorCtor)(void* this_obj, ...);
   static pfn_VictoryMonitorCtor real_VictoryMonitorCtor = nullptr;
   static void* g_capturedVictoryMonitor = nullptr;

   static void* __fastcall Hook_VictoryMonitorCtor(void* this_obj, ...) {
       g_capturedVictoryMonitor = this_obj;
       Log("[Bridge] Captured VictoryMonitor instance: 0x%p\n", this_obj);
       return real_VictoryMonitorCtor(this_obj, ...);
   }
   ```

3. **Populate Hook_VictoryMonitorCounter inject branch**:
   ```cpp
   if (g_victoryTriggerPending &&
       g_capturedVictoryMonitor &&
       this_obj == g_capturedVictoryMonitor) {
       BuildAndInjectAlwaysPassTest(
           static_cast<VictoryMonitor*>(this_obj),
           g_victoryTriggerType);
       InterlockedExchange(&g_victoryTriggerPending, 0);
   }
   real_VictoryMonitorCounter(this_obj);
   ```

4. **Flip MH_EnableHook on** for both new hooks (CTOR + counter_inc).

5. **Add bridge harness pin tests** for the wrapper (input validation already shipped iter-450; add round-trip test for the staging-path semantics under simulator).

iter-450 closes the SCAFFOLDING phase. iter-450a closes the INJECTION phase. Per iter-426 rule, this is the canonical structure for event-driven A1.x arcs.
