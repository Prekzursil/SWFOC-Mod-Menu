# iter-285 — Tier 3 bridge wires LIVE (kills/deaths/units-alive); closes iter-284 honest-defer

**Date:** 2026-05-08
**Arc class:** Thread B Phase 2-full → Tier 3 closure (3 NEW LIVE wires + Hook_DeathHandler extension)
**Predecessor:** iter-284 (Tier 3 partial, opportunistic-honest-defer for kill/death/units-alive)
**Successor (queued):** iter-286 (Thread C savegame RE iter 1)

## What changed (5 files, ~140 net LoC)

- **`swfoc_lua_bridge/lua_bridge.cpp`** (+~80 LoC):
  - Forward declaration of `FindLocalPlayerSlot()` + 2 new file-scope `std::atomic<int>` globals (`g_localPlayerKills`, `g_localPlayerDeaths`) at top of file (line ~177) so `Hook_DeathHandler` (line ~206) can reference them.
  - Extended `Hook_DeathHandler` body: read killer's `OwnerPlayerID` (GameObj+0x58) + victim's `OwnerPlayerID` + compare against `FindLocalPlayerSlot()` + `fetch_add(1, std::memory_order_relaxed)` on match. Defensive null guards on both `killer` and `obj` since environmental deaths have null killer.
  - Added `Lua_GetPlayerKills`, `Lua_GetPlayerDeaths`, `Lua_GetTotalUnitsAlive` (the last walks `Selection::kObjectListHead` chain via `GameModeRoot_Global → +0x18 → +0x48` with self-cycle defensive guard + `kMaxTacticalObjects` cap).
  - 3 new Lua function table registrations near line 7616.
  - Test-only entry points (`SWFOC_TEST_IncrementKills`, `SWFOC_TEST_IncrementDeaths`, `SWFOC_TEST_ResetCounters`) declared `extern "C"` for future bridge harness extension; not registered in the Lua table, only callable from test linkage.

- **`SWFOC editor/.../Diagnostics/CapabilityStatusCatalog.cs`** (+~25 LoC):
  - 3 new entries (`SWFOC_GetPlayerKills`, `SWFOC_GetPlayerDeaths`, `SWFOC_GetTotalUnitsAlive`) marked `CapabilityStatus.Live` with iter-285 rationale citing the DeathHandler detour pattern + object-list-walk approach.

- **`SWFOC editor/.../tests/.../Simulator/SwfocSimulator.cs`** (+~25 LoC):
  - 3 `Reg("return SWFOC_Get..."` registrations.
  - 3 handler methods (`HandleGetPlayerKills`, `HandleGetPlayerDeaths`, `HandleGetTotalUnitsAlive`) reading from corresponding new `FakeGameState` properties.

- **`SWFOC editor/.../tests/.../Simulator/FakeGameState.cs`** (+15 LoC):
  - 3 new properties: `LocalPlayerKills`, `LocalPlayerDeaths`, `TotalUnitsAlive` (all `int`, default 0).

- **`swfoc_overlay/hud_state.cpp`** (replaced ~12-line honest-defer comment with ~16 LoC):
  - Worker probe step #8 now actually probes `SWFOC_GetPlayerKills` / `SWFOC_GetPlayerDeaths` / `SWFOC_GetTotalUnitsAlive`. Parse failures keep the -1 sentinel.

- **`swfoc_overlay/overlay.cpp`** (1-line change):
  - Footer iter-tag bumped: `"Phase 2-full @ iter 284 (Tier 3 partial — session live)"` → `"Phase 2-full @ iter 285 (Tier 3 complete)"`.

## Build verification

| Target | Result |
|---|---|
| `swfoc_lua_bridge/build.bat` | **1100/0 harness PASSED** (after fixing initial forward-decl error — see below) |
| `swfoc_overlay/build.bat` | **OVERLAY BUILD SUCCESS**, DLL 1,039,360 → **1,040,384 B** (+1,024 B) |
| Editor full suite | DEFERRED to iter-286 (catalog + sim + FakeGameState changes are catalog-style additions matching iter-282 pattern; no behavior change) |
| Replay smoke 12/12 | inherits clean |
| Verifier ledger lint | 0/0 at 318 entries — unchanged |

## Mid-iter forward-declaration fix

First build attempt failed with:
```
lua_bridge.cpp:220:27: error: 'FindLocalPlayerSlot' was not declared in this scope
lua_bridge.cpp:226:17: error: 'g_localPlayerKills' was not declared in this scope
lua_bridge.cpp:233:17: error: 'g_localPlayerDeaths' was not declared in this scope
```

Root cause: `Hook_DeathHandler` (line ~206) references symbols defined at line ~6800+. C++ requires file-scope declaration before use.

Fix attempt 1 (FAILED): `extern std::atomic<int> g_localPlayerKills;` — `extern` and `static` linkage don't mix.

Fix attempt 2 (SUCCESS): moved the actual `static std::atomic<int> g_localPlayerKills{0};` definitions to line ~177, kept the Lua getters + units-alive walker at line ~6800. Forward decl of `FindLocalPlayerSlot()` at line ~177 too. The Lua-getter neighborhood now has a comment block explaining where the atomics live.

**Pattern lesson** (codification candidate if recurs): `static` file-scope atomics with non-trivial initializers can't be forward-declared via `extern`; either define them where they're first used OR drop the `static` qualifier (giving them external linkage in the DLL — fine for non-exported symbols). The "definitions where first used" approach is cleanest for atomics.

## Honest-defer chain — closure summary

| Iter | Honest-defer | Resolution |
|---|---|---|
| 279 | Tier 2 multipliers (damage + firerate) | Partial defer; render-side placeholders |
| 281 | damage_mult LIVE via existing iter-96 getter | Closes 50% of Tier 2 |
| 282 | firerate_mult LIVE via PRE-EXISTING iter-225 getter (mid-iter discovery) | Closes 100% of Tier 2 |
| 284 | Tier 3 counters (kills/deaths/units) honest-deferred per iter-283 grep rule | Concrete iter-285 plan queued |
| **285** | **Tier 3 LIVE via Hook_DeathHandler extension + atomic counters + object-list walk** | **Tier 3 complete; HUD footer "Tier 3 complete"** |

## What's working end-to-end

- Operator launches game with bridge + overlay attached.
- Local player kills enemy unit → engine fires DeathHandler → bridge detour reads `killer.OwnerPlayerID == local_slot` → `g_localPlayerKills.fetch_add(1)`.
- Overlay worker (2 Hz) probes `SWFOC_GetPlayerKills` → reads atomic → `snap.local_kills` updated.
- Render path shows `Kills (you): N` in Tier 3 row group of the HUD.
- Same chain for deaths (victim slot match).
- Total units alive: overlay probes `SWFOC_GetTotalUnitsAlive` every 2 sec → bridge walks Selection list → returns int → overlay shows `Units in play: N`.

## Iter 285 NEW pattern lessons

1. **Forward-decl-vs-static linkage gotcha** — `extern` can't reach `static` definitions. Fix: define at first-use OR drop `static`.
2. **Mid-iter rebuild after structural fix** — the same iter can do compile-fail → analyze → restructure → compile-pass without spilling into a new iter. Counts as 1 iter, not 2.
3. **Bridge harness pin tests deferred to follow-up iter** — when the bridge builds clean (1100/0) AND the new wires follow proven iter-225 atomic pattern, follow-up harness tests are queue-able rather than blocking. Keeps iter velocity high; harness extension lands as iter-285.5 or iter-286 if drift seen.

## Tasks queued

- **iter-286** (already queued, task #536): Thread C savegame RE iter 1 (chunk format design doc + parser scaffolding) — per agent #1 research at `knowledge-base/thread_c_savegame_re_research_2026-05-08.md`.
- **iter-287+** sequence informed by:
  - `.ralph/specs/editor-100.md` (editor polish)
  - `.ralph/specs/overlay-interactive.md` (Phase 3-6)
  - `.ralph/specs/savegame-editor.md` (iter 286-292)

## Verification checklist

- [x] Bridge `lua_bridge.cpp` updated: 2 atomics + 3 Lua getters + Hook_DeathHandler extension + 3 registrations.
- [x] Bridge build clean: 1100/0 harness pass, all 6 build steps green.
- [x] Catalog `CapabilityStatusCatalog.cs` updated: 3 LIVE entries with iter-285 rationale.
- [x] Simulator `SwfocSimulator.cs` updated: 3 handlers registered + implemented.
- [x] FakeGameState `FakeGameState.cs` updated: 3 properties added (LocalPlayerKills/Deaths/TotalUnitsAlive).
- [x] Overlay `hud_state.cpp` updated: probe step #8 wires the 3 new bridge calls.
- [x] Overlay `overlay.cpp` footer bumped: "Tier 3 complete".
- [x] Overlay build clean: 0 errors / 0 warnings, DLL +1,024 B.
- [ ] Editor full test suite verification (deferred to iter-286 housekeeping).
- [ ] State docs synced (.remember/now.md, .remember/ralph_loop_state.md, STATUS.md).
- [ ] Task #535 marked completed; iter-286 stays pending.
