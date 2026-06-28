# A1.3 SetFireRate Global — Multi-Iter Arc Operator Changelog (iter 224-228)

**Date range:** 2026-05-06 11:30 UTC to 13:30 UTC (single session)
**Status at end of arc:** **CLOSED at offline verification level**, live-game smoke `[LIVE-PENDING]` for next live-attached session
**LIVE wire count delta:** +1 (143rd LIVE flip in master loop)
**Native UX delta:** +2 buttons (Combat tab — total 109 → 111)

---

## What this arc closed

A1.3 SetFireRate had been **DEFERRED** since iter 101 (2026-04-23) — a 124-day deferral. The ledger had three verified consumer functions (`weapon_tick`, `hardpoint_fire`, `fire_control_dispatch`) but no setter — every prior iter that touched the topic walked away because the consumer-side surface didn't expose a clean injection point.

The iter 221 PHASE 2 PENDING re-audit re-confirmed the defer (no drift since iter 132's audit) but flagged it as "needs RTTI dissection or per-tick MinHook detour." **Iter 224 picked up that thread and the arc shipped over 5 iterations in a single session.**

---

## Per-iter walk-through

### Iter 224 — RE design kickoff (research-only, no code)

- Created `knowledge-base/iter224_setfirerate_global_re_kickoff.md` (156 lines).
- Decompiled `WeaponTick` @ `0x140387010` from the IDA full-corpus.
- Identified field offsets: `a1+40` (current cooldown timer), `a1+96` (last-tick timestamp), `a1+32 → WeaponClass*+72` (weapon-state enum).
- Found cooldown decrement centralized in `sub_140387400` (1904-byte function, takes `dt` as 2nd arg).
- **Design decision:** per-tick MinHook detour (mirrors iter-96 Take_Damage_Outer pattern) — scale `dt` by a `g_fireRateMult_global` atomic before forwarding. Rejected RTTI-driven WeaponClass setter (would have needed 5-10 iter RTTI walk).

### Iter 225 — Bridge LIVE wire shipped (143rd LIVE flip)

- `swfoc_lua_bridge/rvas.h`: `RVA::Weapon_Tick = 0x387010`.
- `swfoc_lua_bridge/lua_bridge.cpp`:
  - `static std::atomic<float> g_fireRateMult_global{1.0f};`
  - `Hook_WeaponTick(__int64 a1, int a2)` — fast-pathed for mult=1.0; otherwise scales the `dt` arg by the global before forwarding to `real_WeaponTick`.
  - `Lua_SetFireRateMultiplierGlobal(L)` — sanity clamp `[0.0, 100.0]` (lower bound prevents reverse cooldown via negative; upper bound prevents int overflow in dt math).
  - `Lua_GetFireRateMultiplierGlobal(L)` — round-trip read-back.
  - 2 Lua function registrations + MinHook installer block.
- `CapabilityStatusCatalog`: 2 NEW Live entries (`SWFOC_SetFireRateMultiplierGlobal` + `SWFOC_GetFireRateMultiplierGlobal`). Phase-1 mirror Note for `SWFOC_SetFireRate` updated to reference the iter-225 LIVE alternative.
- Bridge harness 1100/0 GREEN. DLL + replay rebuilt clean.

### Iter 226 — Simulator handler + 6 pin tests + reverse-orphan rebalance

- `tests/SwfocTrainer.Tests/Simulator/SwfocSimulator.cs`: 2 `Reg()` handlers + `HandleSetFireRateMultiplierGlobal` (mirrors bridge clamp) + `HandleGetFireRateMultiplierGlobal`.
- `tests/SwfocTrainer.Tests/Simulator/FakeGameState.cs`: `public float GlobalFireRateMultiplier { get; set; } = 1.0f;` (engine identity default).
- 6-test pin file `Iter226SetFireRateGlobalSimulatorTests.cs` (catalog Live + iter-225/iter-224 cross-references + FakeGameState reflection check + simulator round-trip Set→Get + clamp lower-bound + clamp upper-bound). Used iter-97's `V2BridgeAdapter` wrapping pattern for pipe transport.
- **Mid-iter drift caught:** reverse-orphan snapshot count -4 (added 2 iter-225 entries pending Combat-tab UX, dropped 6 entries that became regex-visible from iter-185/186/195/203/217/218/219 dispatchers' interpolated form). Updated `KnownUnwiredEntries` in `CapabilityCatalogReverseOrphanTests`.
- Final filtered run: **40/40 GREEN** in 84 ms.

### Iter 227 — Combat tab native UX (Apply GLOBAL + Read GLOBAL)

- `BridgeCombatDispatcher.cs`: 2 new methods using regex-visible interpolated form (`string.Format(Inv, "return SWFOC_SetFireRateMultiplierGlobal({0})", mult)`). Pattern matches iter-96 `SetDamageMultiplierGlobal` exactly.
- `ICombatDispatcher`: 2 default-impl methods so older mocks still compile.
- `CombatTabState.cs`: `SetFireRateMultiplierGlobalAsync` + `GetFireRateMultiplierGlobalAsync` wrappers binding to existing `FireRateMultiplier` slider (zero new field — pure reuse).
- `CombatTabViewModel.cs`: 2 `ICommand`s + 2 `CapabilityAwareAction`s + 2 handlers + `AllActions` extended (14 → 16) + `CapabilityBadge` updated.
- `MainWindowV2.xaml`: 2 buttons added to fire-rate row (Grid.Row=2) — Apply (GLOBAL) + Read (GLOBAL) — alongside the existing Apply (per-slot). Tooltip captures iter-224 engine semantic caveat (mult=0 freezes weapons → use Suspend_AI for proper pause) + clamp `[0.0, 100.0]` + `WeaponTick @ 0x387010` RVA reference for traceability.
- `Iter227FireRateGlobalNativeUxTests.cs` (6 tests): catalog Live + VM exposes 4 properties + XAML row contains Apply+Read buttons + tooltip references WeaponTick/0x387010/freeze + AllActions includes pair (HelperNames check) + dispatcher uses regex-visible interpolated form + iter-226 sim still pins.
- **Reverse-orphan rebalance:** dropped iter-225 entries from `KnownUnwiredEntries` (now regex-visible via `BridgeCombatDispatcher`). Net change to count: -2.
- **Mid-iter drift caught × 2:** (1) `CapabilityAwareAction.CommandName` doesn't exist (used `HelperNames`). (2) `CombatTabViewModelCapabilityTests.AllActions_EnumeratesEveryActionInDeclaredOrder` count 14→16 — caught and fixed in same iter (vs iter-208's 14-iter delay).
- Final filtered run: **103/103 GREEN** in 423 ms. Editor republished.

### Iter 228 — Offline verify + close (multi-iter arc finale)

- Pure verify + close-out, no code changes.
- `bridge_test_harness.exe` → **1100 passed, 0 failed**.
- `python -m verifier lint` → **0 errors / 0 warnings** (315 entries: 303 VERIFIED + 2 LIVE_OBSERVED + 10 DEPRECATED).
- HISTORY.md updated with the 5-iter arc summary entry.
- STATUS.md master-loop A1.3 row updated: `DEFERRED iter 101/130 → CLOSED iter 225 (bridge LIVE) → VERIFIED OFFLINE iter 228, [LIVE-PENDING] for next live-attached session`.
- Live-game smoke verify queued — current environment has no SWFOC process attached. Offline verification (bridge harness + simulator + 103/103 editor tests) is the GREEN-light gate to ship; live engine-effect verify (operator clicks "Apply GLOBAL" with FireRateMultiplier=2.0 and observes 2x faster weapon firing) waits on next live attach but blocks no further iter.

---

## Operator workflow (post-iter-228)

**To globally scale weapon fire rates engine-wide:**

1. Open editor (`publish/SwfocTrainer.App.exe`).
2. Switch to the **Combat** tab.
3. Type a multiplier into the `Fire rate mult` text box (e.g. `2.0` for 2x faster fire rate).
4. Click **Apply (GLOBAL)** in the same row.
5. Optionally click **Read (GLOBAL)** to confirm the bridge has the value stored.

**Engine semantic caveats** (per iter-224 design doc — surfaced in tooltip):

- `mult=2.0` → weapon cooldown advances 2x faster (2x fire rate)
- `mult=0.5` → halved fire rate
- `mult=0.0` → effective freeze (no time passes — weapons never fire). **Use the Suspend_AI button (iter-219 sibling on this tab) for a proper AI pause** rather than mult=0; freeze blocks ALL weapons including yours.
- `mult > 100` → clamped to 100 (int overflow guard in dt math)
- `mult < 0` → clamped to 0 (prevents reverse-cooldown engine breakage)

**To restore normal fire rate**, set the multiplier back to `1.0` and click Apply (GLOBAL). The bridge fast-paths `mult=1.0` (no scaling overhead), so leaving it at 1.0 has zero perf cost.

**Combined with iter-96 SetDamageMultiplierGlobal** (also on Combat tab), the operator now has full dual-axis combat scaling: damage × fire rate. Useful for streaming/cinematic recording where the operator wants slower-but-deadlier fights or vice versa.

---

## Pattern lessons captured

1. **Multi-iter RE arc cadence**: the canonical shape is `~1 RE-design iter + ~1 bridge-LIVE iter + ~1 simulator + tests iter + ~1 editor UX iter + ~1 live-verify iter`. iter 224-228 nailed this exactly.
2. **"Bridge LIVE first, editor button next iter"**: standard pattern — iter-96 SetDamageMultiplierGlobal followed identical shape (bridge in iter 96, button in iter 100). Operators get the wire callable via Lua Playground first, then the native button comes next iter.
3. **Immediate stale-count drift catch**: iter 227's `CombatTabViewModelCapabilityTests.AllActions` count drift (14→16) was caught in the same iter as introduction. Compare iter-208 which caught its drift 14 iters after introduction. The fix is **run full-suite tests every iter close-out**, not just filtered-by-iter-name tests.
4. **Reverse-orphan as two-way drift catcher**: when a wire ships LIVE on the bridge but isn't yet wired in the editor (e.g. iter 225's entries pre-iter-227), the snapshot lists them as "catalogued but unwired." When the editor button ships, the regex-visible interpolated form drops them from the snapshot. iter 227 had to update the snapshot in both directions (+2 iter-225 wires, -6 iter-185/186/195/203/217/218/219 wires that became regex-visible from interpolated dispatcher forms shipped in those iters). The test catches both kinds of drift.
5. **`CapabilityAwareAction.HelperNames`, not `CommandName`**: the action class wraps a list of helper names (because composite actions can fan out to multiple bridge calls) — there's no singular `CommandName` field. Future test code should `SelectMany(a => a.HelperNames)` for individual SWFOC_X probes.

---

## Cross-references

- `knowledge-base/iter224_setfirerate_global_re_kickoff.md` — RE design doc with full WeaponTick decompile + design decision matrix.
- `knowledge-base/HISTORY.md` (top entry) — 5-iter arc summary.
- `STATUS.md` (master-loop table A1.3 row) — closed-state summary.
- `knowledge-base/master_loop_capstone_iter_100-221.md` — predecessor capstone (iter 222 docs iter); iter 229 supplements it for the iter 224-228 arc.

---

## What's next (iter 230+)

Per the iter 221 audit's direction-setting recommendations:

- **Option A (multi-iter)**: pick up another A1.x deferred sub-task — `SetCameraPos` / `SetUnitField-10-fields` / `SetUnitCapOverride` would each be 5-iter arcs of the same shape.
- **Option B (multi-iter)**: Thread B Overlay Phase 2-full ImGui vendoring or Thread C save-game RE.
- **Option C (single-iter polish)**: bridge harness expansion / replay harness expansion / capability surface report polish.

Given the iter 221 audit found 0% drift across the existing PHASE 2 PENDING set, **all future LIVE flips require new RE work** (no free promotions remaining). Iter 230 should commit to one of A/B/C as a multi-iter project rather than chase low-effort surfacing.
