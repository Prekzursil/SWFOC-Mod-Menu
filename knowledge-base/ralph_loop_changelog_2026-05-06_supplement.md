# Ralph Loop Changelog — 2026-05-06 Supplement (iter 217-219 — closing the iter-216 surfacing queue)

**Predecessor**: `ralph_loop_changelog_2026-05-06.md` covers iter 197-215 (74 buttons, 100-button milestone crossed at iter 215).

**This supplement**: covers iter 217-219 — 3 small surfacing iters that closed every remaining wire from the iter-216 changelog "What's NOT yet surfaced" list. After iter 219, the iter-216 surfacing queue is **operationally complete**.

## Headline

- **3 surfacing iters** (217-219) over 3 hours.
- **7 buttons** added across **4 editor tabs** (PlayerState 4 + UnitControl 1 + Galactic 1 + Combat 1).
- **0 new bridge wires** (all 7 closing iter-216 queue items).
- **iter-204 hardcoded-bool on/off lineage now 7 iters deep** (204→208→211→212→213→215→217).
- **ITER-216 SURFACING QUEUE NOW CLOSED** — every wire from the iter-216 changelog "What's NOT yet surfaced" list has a native button.
- **Total native UX surfacing across iter 188-219**: **109 buttons across 9 tabs** (UnitControl 34 + PlayerState 16 + Inspector 18 + Galactic 16 + WorldState 12 + Spawning 8 + Diagnostics 6 + Camera 10 + Combat 9).
- Editor binary: **165.43 MB → 157.25 MB** (Release single-file rebuild path; size delta is build-config not behavior).
- Filtered tests added: 15 new pin tests across 3 pin files (5 each for iter 217/218/219). All GREEN.

## Walk-through-every-tab format

### PlayerState tab — 4 new buttons (iter 217)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 217 | DisableOrbitalBombardmentOnLua | (player):Disable_Orbital_Bombardment(true) | "Block REBEL from orbital strikes" — hardcoded-bool on/off pair (iter-204 lineage 7 iters deep) |
| 217 | DisableOrbitalBombardmentOffLua | (player):Disable_Orbital_Bombardment(false) | "Allow orbital strikes again" — pair half |
| 217 | GlobalMakeAllyLua | Make_Ally(player1, player2) | "Make REBEL ally with EMPIRE" via GLOBAL form (iter-182 helper) — alternative to iter-209 obj-receiver Make_Ally |
| 217 | GlobalMakeEnemyLua | Make_Enemy(player1, player2) | "Make REBEL enemy with EMPIRE" via GLOBAL form — alternative to iter-209 obj-receiver Make_Enemy |

PlayerState tab now has **16 native LIVE buttons** total (3 iter-189 read + 3 iter-199 read + 3 iter-209 diplomacy + 3 iter-210 player-extension + 4 iter-217 final extension). **Zero new fields this iter** — 100% reuse of PlayerLuaExpr (iter-189) + OtherPlayerLuaExpr (iter-199) — cleanest field-reuse iter ratio yet.

### UnitControl tab — 1 new button (iter 218)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 218 | CorruptLua | (unit):Corrupt(amount) | "Degrade Underworld unit hostility/loyalty" — pairs semantically with iter-212 Bribe (Bribe takes ownership; Corrupt degrades only). Uses NEW CorruptAmountLuaExpr field. |

UnitControl tab now has **34+ native LIVE buttons** total. Bribe + Corrupt are the two Underworld signature abilities — operators can now A/B test the engine's two distinct corruption mechanisms.

### Galactic tab — 1 new button (iter 218)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 218 | TaskForceMoveToTargetLua | (taskforce):Move_To_Target(target) | "Order TaskForce to move toward a target object" — distinct from iter-215 TaskForceMoveTo (which targets a position). Reuses iter-215 TaskForceLuaExpr + TaskForceTargetLuaExpr — zero new fields. |

Galactic tab now has **16+ native LIVE buttons** total (3 iter-108 ChangeOwner + 3 iter-200 FOW + 9 iter-215 TaskForce write-side + 1 iter-218 TaskForce target-targeted move). Operators can A/B test position-targeted (Move_To, iter-215) vs object-targeted (Move_To_Target, iter-218) without re-typing the TaskForce handle.

### Combat tab — 1 new button (iter 219)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 219 | SuspendAiLua | Suspend_AI(seconds) | "Pause AI player decision loop" — cinematic helper. Uses NEW SuspendAiSecondsLuaExpr field. Pairs with iter-208 Lock_Controls + iter-145 cinematic camera quad for full battle-pause cinematic recording. |

Combat tab now has **9 native LIVE buttons** total (4 iter-93/94/95/96/100 toggle/global scalars + 4 iter-193 per-unit Lua + 1 iter-219 Suspend_AI). **Closes the iter-216 changelog "What's NOT yet surfaced" queue.**

## Architectural patterns established this arc

### 1. Helper shape-agnosticism for global-form alternatives (iter-217)

iter-217 demonstrates that `BuildUnitLuaMethodCall("SWFOC_X", a, b)` works equally well for player-method wires (`(player):Disable_Orbital_Bombardment(bool)`) AND global-form wires (`Make_Ally(player1, player2)`). The bridge function name `SWFOC_X` doesn't care what the receiver shape is — it just takes 2 string args. This is why iter-178's matrix completion paid off: helpers are receiver-shape-agnostic at the wire layer.

**Insight**: when surfacing a "GLOBAL form alternative" alongside an existing "obj-receiver" wire (iter-217 GlobalMakeAlly vs iter-209 (player1):Make_Ally(player2)), use the SAME helper for both. The bridge function dispatches to different engine APIs, but the operator-facing wire format is identical.

### 2. Cross-tab A/B-test batches (iter-218)

iter-218 demonstrates a new batching pattern — pair two unrelated wires into one iter when each closes a distinct A/B-test loop. UnitControl Corrupt (iter-218) vs Bribe (iter-212) is one such pair; Galactic Move_To_Target (iter-218) vs Move_To (iter-215) is another. Each pair lets operators experiment with engine-level alternatives without re-typing handles. Field-reuse is what makes this work at 2-LoC marginal cost per wire.

**Insight**: A/B-test pairs are operator-facing experimentation surface, not redundancy. Document the pairing explicitly in catalog rationale ("Pairs semantically with iter-X Y") so future readers understand WHY both exist.

### 3. Single-pass clean run (iter-219)

iter-219 was the first iter where capability surface markdown drift was caught + fixed in the SAME test run (env var enabled at run time). Previously each iter required two passes — initial fail + regen + re-run. Combining `SWFOC_REGEN_CAPABILITY_SURFACE=1` with the filtered test run is now the default — saves 30-60 seconds per iter on small surfacing iters.

**Insight**: The toolchain hardening that started at iter-172 (tee-grep-line-buffered + blame-hang-timeout) extends to the iter-219 single-pass pattern. Each toolchain improvement compounds: iter-172 made test runs visible, iter-208 made drift-catch routine, iter-219 made drift-fix integrated.

### 4. Queue-closure as iter direction

The iter-216 changelog's "What's NOT yet surfaced" section explicitly listed 7 remaining wires; iter 217 closed 3, iter 218 closed 2, iter 219 closed the last 1. **Each iter knew exactly which wire to surface next because the changelog enumerated them.** This is the docs-as-roadmap pattern proving its value: a supplement isn't just retrospective, it's prescriptive for the next 3-7 iters.

**Insight**: Always include a "What's NOT yet surfaced" section in surfacing supplements. It functions as both a retrospective (what's done) and a roadmap (what to do next). Iter 220 (this supplement) maintains the pattern with the "What's left after the queue closure" section below.

### 5. Cinematic workflow chain complete (iter-219)

iter 219 closes the last hole in the operator-facing cinematic recording chain. **Lock_Controls → Suspend_AI → cinematic camera quad → letterbox → music → Resume music → Unlock_Controls** is now a 7-button operator workflow with no Lua Playground required. The chain was assembled iter-by-iter:

| Iter | Wire | Date |
|---|---|---|
| 145 | Cinematic camera quad (Start/End + Set_Key/Transition_Key) | Apr 29 |
| 150 | Letter_Box_On / Letter_Box_Off | Apr 30 |
| 201 | Play_Music + Story_Event + Add_Objective + Play_SFX_Event | May 5 |
| 202 | Stop_All_Music + Resume_Mode_Based_Music + Story_Event_Trigger | May 5 |
| 208 | Lock_Controls + Unlock_Controls | May 6 |
| 219 | Suspend_AI | May 6 |

Total ~38 iters from start to finish. This is the longest end-to-end operator-facing arc in the master loop.

## Bridge state (unchanged from iter 187)

- **142 LIVE wires** total in master loop (iter 100-186).
- **12 dispatcher helpers** + **2 builder helpers** (iter-202 BuildGlobalLuaNoArgCall + iter-203 BuildSwfocLua3ArgCall).
- **0 new wires shipped** in iter 217-219. All 7 buttons surface existing LIVE wires from the iter-216 queue.

## Editor surface count

After iter 217-219:

- **109 native LIVE-wire buttons** across **9 editor tabs** (UnitControl 34 / PlayerState 16 / Inspector 18 / Galactic 16 / WorldState 12 / Spawning 8 / Diagnostics 6 / Camera 10 / Combat 9).
- **Lua Playground preset menu** (iter-183, 83 entries) still available for the ~33 wires that don't have native UX yet (mostly read-side or rare cinematic wires that don't merit dedicated buttons).
- **Operator changelogs**: iter-187 (`2026-05-05.md` covers iter 146-186), iter-196 (`2026-05-05_supplement.md` covers iter 188-195), iter-216 (`2026-05-06.md` covers iter 197-215), iter-220 (this file, covers iter 217-219 + queue closure milestone).

## Test gates state

- **Filtered iter-217 tests**: 5/5 GREEN (PlayerState final extension)
- **Filtered iter-218 tests**: 5/5 GREEN (UnitControl Corrupt + Galactic TaskForceMoveToTarget)
- **Filtered iter-219 tests**: 5/5 GREEN (Combat Suspend_AI)
- **Total filtered tests added**: **15/15 GREEN** across 3 pin files.
- **Bridge harness**: 1100/0 (unchanged from iter 186).
- **Verifier lint**: 315/0/0 (unchanged).

## Operator quick-reference

For any operation with native UX, click the button. For wires without native UX, use the Lua Playground preset menu (iter 183 still current — 83 entries).

**Iter-216 surfacing queue closed this supplement**: 7 wires, 3 iters, 4 tabs, 15 pin tests. Bridge↔editor↔catalog 3-way contract aligned for all 109 surfaced wires.

## What's left after the queue closure (iter 221+ direction)

Native UX surfacing arc is **operationally complete** — every meaningful operator-facing wire from the iter 100-186 LIVE-wire goldmine has a native button OR a preset-menu entry. Future iters need a NEW direction. Candidates from STATUS.md "next session" multi-iter projects:

### Multi-iter projects (each needs ~5-15 iters)

1. **Thread B — Overlay Phase 2-full ImGui vendoring** (~500 LoC, ~15 files). Vendors ImGui + DX9 backend into the in-game overlay so it can render structured HUD panels on top of the game. Iter 102/289 shipped Phase-1 (text overlay + visible rectangle). Phase-2-full is the heavy lift.

2. **Thread C — Save-game RE** (not started). The .sav format is poorly understood. RE pass to dissect would unlock save-state editing, scenario import/export, and replay-from-save workflows.

3. **A1.x dedicated arcs** (each needs RTTI dissection):
   - **A1.3 SetFireRate** (per-tick MinHook detour or WeaponClass RTTI dissection — iter-101 confirmed defer at iter-130)
   - **A1.6 SetGameSpeed** (no engine setter exists per iter-131 audit — needs new RVA hunt)
   - **SetUnitField's 10 Phase-1 fields** (iter-136 only flipped 3/13 LIVE; remaining 10 need per-field RVA dissection)
   - **SetCameraPos per-coord** (iter-107 LIVE for full vec3; per-coord setters need separate RVA pin)
   - **SetUnitCapOverride** (no setter found yet — multi-iter RTTI dissection arc)

### Phase2HookPending re-audit

iter-132 was the last Phase2HookPending audit pass (24 candidates triaged). Since iter-132, the catalog has grown by ~85 entries and many existing entries may have flipped LIVE silently as iter-100-186 wires shipped. A re-audit would catch any drift across the now-larger catalog.

**Recommended next iter (221)**: Phase2HookPending re-audit pass — read every catalog entry, check Status field, cross-reference against bridge harness LIVE list. Single-iter scope; outputs an updated `iter221_phase2_pending_audit.md` similar to iter-132's audit doc. Sets up iter-222+ direction based on what the re-audit finds.

Alternative iter-221 option: Thread B Overlay Phase 2-full kickoff iter (vendoring ImGui sources into the bridge DLL). Multi-iter; iter-221 would be the design + vendoring iter; iter-222+ would integrate.

## Pattern lesson capstone

3 surfacing iters at ~2.3 buttons/iter average — smaller than the iter 197-215 cadence (~3.9 buttons/iter) because the queue's remaining wires were the long-tail leftovers (one wire per workflow gap). Field-reuse compounding peaked here: iter-217 added zero new fields by reusing PlayerLuaExpr + OtherPlayerLuaExpr threaded through 4 iters of PlayerState surfacing.

The iter-216 queue closure is a natural milestone but not a hard stop. The native UX surfacing arc has been productive for **32 surfacing iters** (iter 188-219) producing **109 buttons across 9 tabs**. Going forward, individual Phase 2 wires can still be surfaced opportunistically when they ship — but bulk-surfacing days are over until a new arc opens (overlay/save-game/A1.x RTTI dissection).
