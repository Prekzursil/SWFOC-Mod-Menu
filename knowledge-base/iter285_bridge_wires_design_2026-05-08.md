# Iter-285 — Tier 3 Bridge Wires Design Spec (2026-05-08)

Design dispatched during iter-284 by parallel sub-agent. Iter-285 will execute from this spec.

## Subject

`iter-285: Implement SWFOC_GetPlayerKills / GetPlayerDeaths / GetTotalUnitsAlive bridge wires for Tier 3 HUD (LIVE badges)`

## Grep verification (iter-283 rule)

| Symbol | Status |
|---|---|
| `SWFOC_GetPlayerKills` / `Lua_GetPlayerKills` | NOT present |
| `SWFOC_GetPlayerDeaths` / `Lua_GetPlayerDeaths` | NOT present |
| `SWFOC_GetTotalUnitsAlive` / `Lua_GetTotalUnitsAlive` | NOT present |
| `g_localPlayerKills` / `g_localPlayerDeaths` | NOT present |
| `g_totalUnitsAlive` / `g_aliveUnitsCount` | NOT present |

Only existing kill/death-related symbol: `SWFOC_KillUnit` (write-side mutation, unrelated). Iter-284's grep audit was correct — no conflicting stub.

## iter-96 pattern (canonical shape)

```cpp
// Global storage — std::atomic<int> for lock-free counters in detour hot path
static std::atomic<int> g_localPlayerKills{0};
static std::atomic<int> g_localPlayerDeaths{0};

// Lua getters (no locks for atomics)
static int Lua_GetPlayerKills(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(g_localPlayerKills.load(std::memory_order_acquire)));
    return 1;
}

// Detour increments atomically
g_localPlayerKills.fetch_add(1, std::memory_order_release);
```

`std::atomic` (not `CRITICAL_SECTION`) because counters are integer-only; iter-225 uses the same lock-free pattern for `g_fireRateMult_global`.

## Kill counter design — Option B (extend Hook_DeathHandler)

**Why not Option A (Take_Damage_Outer extension):** sourceInfo at that layer isn't the attacker GameObj* (line 6608-6632 comment warns). False positives from overkill, invuln flags, etc.

**Option B mechanism**: `Hook_DeathHandler` @ RVA `0x39BDB0` (CONFIRMED-RE, Ghidra 2026-04-05) is already hooked at line 8568. Extract `killer->OwnerPlayerID`, compare to local player slot.

```cpp
static void Hook_DeathHandler(void* obj, int deathCause, void* killer,
                               void* deathEvent, int deathAnim, int ownerTransfer) {
    // iter-285 kill counter
    if (killer) {
        int localSlot = FindLocalPlayerSlot();
        if (localSlot >= 0) {
            uint32_t killerOwnerId = *reinterpret_cast<uint32_t*>(
                reinterpret_cast<uintptr_t>(killer) + RVA::GameObj::OwnerPlayerID);
            auto localPlayer = GetPlayerObj(localSlot);
            uint32_t localPlayerId = *reinterpret_cast<uint32_t*>(
                reinterpret_cast<uintptr_t>(localPlayer) + RVA::PlayerObj::PlayerId);  // VERIFY offset
            if (killerOwnerId == localPlayerId) {
                g_localPlayerKills.fetch_add(1, std::memory_order_release);
            }
        }
    }
    // Always increment death counter on any death within local-player units
    // (or specifically: only count local-player unit deaths — clarify in code review)
    // ... existing event stream logic ...
    real_DeathHandler(obj, deathCause, killer, deathEvent, deathAnim, ownerTransfer);
}
```

**No new RVA pin needed** — DeathHandler already pinned + hooked.

**Caveat**: PlayerObj player-ID field offset must be verified in `verified_facts.json`. If absent, fall back to GameObject pointer comparison or slot-based comparison.

## Units-alive counter design — Option C1 (poll-on-demand)

**Why not Option A (per-type enum)**: ~30 unit types, fragile across game updates.

**Why not Option B (spawn detour)**: spawn-event RVA not pinned; new RE work needed.

**Option C1**: walk engine object list via `Selection::kObjectListHead` (already used by Find_All_Objects_Of_Type per iter-104 doc, 2026-04-23). O(n), n ≤ 2048 cap. ~0.3 ms per frame on modern HW.

```cpp
static int Lua_GetTotalUnitsAlive(lua_State* L) {
    int count = 0;
    auto inner = reinterpret_cast<uintptr_t>(g_base + RVA::GameModeRoot_Global + 0x18);
    auto listHead = *reinterpret_cast<uintptr_t*>(inner + Selection::kObjectListHead);
    auto sentinel = inner + Selection::kObjectListSentinel;

    for (uintptr_t node = listHead;
         node != sentinel && count < Selection::kMaxTacticalObjects;
         node = *reinterpret_cast<uintptr_t*>(node + Selection::kNodeNext)) {
        // Optional filter: skip non-units (buildings, structures)
        count++;
    }
    fn_pushnumber(L, static_cast<double>(count));
    return 1;
}
```

**Hybrid future**: if profiling shows >1% frame budget, add death-counter decrement + spawn-counter increment when spawn-event RVA pins.

## Local-player-slot resolution

Reuse existing `FindLocalPlayerSlot()` (lines 528-536 in lua_bridge.cpp). Iterates players, returns slot where `PlayerObj+0x62 (RVA::PlayerObj::LocalPlayer) == 1`. Returns -1 in galactic-mode transitions.

**Thread safety**: detour runs in game thread; Lua getter runs in same context. No race. Memory ordering correct: detour publishes (release), getter observes (acquire).

## Deliverables

| File | Role | LoC |
|---|---|---|
| `lua_bridge.cpp` | 4 globals + 3 Lua getters + 1 extended detour + 3 registrations | ~80 |
| `bridge_test_harness.cpp` | TEST SUITE 15: 3 round-trip tests | ~40 |
| `CapabilityStatusCatalog.cs` | 3 LIVE entries with iter-285 rationale | ~10 |
| `Simulator/SwfocSimulator.cs` | 3 stub handlers | ~15 |
| `swfoc_overlay/hud_state.cpp` | Replace iter-284 step #8 honest-defer with actual probes | ~20 |
| `swfoc_overlay/overlay.cpp` | Bump footer "Tier 3 partial — session live" → "Tier 3 complete" | ~5 |

**Total: ~170 LoC across 6 files** (target: ~150 with tight packing)

## Risks + mitigations

| Risk | Level | Mitigation |
|---|---|---|
| AOB drift on DeathHandler RVA across binary versions | MEDIUM | Inherits iter-96/iter-225 risk; document AOB regen task |
| `RVA::PlayerObj::PlayerId` offset absent | MEDIUM | Verify in `verified_facts.json`; fallback = GameObject pointer comparison |
| Atomic memory ordering | LOW | release/acquire correct; document detour-thread assumption |
| Poll latency for GetTotalUnitsAlive | LOW | <1 ms at 2048 obj cap; cache+decrement if >1% frame budget |
| FindLocalPlayerSlot returns -1 in galactic transitions | LOW | Overlay caches; document edge case |
| Death event without killer (env / suicide) | LOW | Implicit `if (killer)` guard skips |

## Acceptance criteria

- All 1100 harness tests pass (`bridge_test_harness.exe` returns 0).
- Lua getters registered + callable from game-state Lua context.
- iter-96 + iter-225 detour patterns remain unbroken (no regression in damage/fire-rate wires).
- AOB risk documented; verified_facts.json updated with attacker-ID field verification.
- Code comments explain memory-ordering assumptions, galactic-mode edge cases, poll latency trade-offs.
- Overlay footer displays "Tier 3 complete".

## Iteration chain

- **Closes**: iter-284 honest-deferred work (kills/deaths/units-alive bridge wires).
- **Enables**: iter-286+ overlay UX phases (Phase 3 interactive widgets need stable read-side counters).
- **Parallel track**: Thread C savegame RE research (iter-286 separate kickoff per `thread_c_savegame_re_research_2026-05-08.md`).
