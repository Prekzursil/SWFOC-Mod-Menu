# A1.x SetCameraPos Per-Coord — Multi-Iter Arc Operator Changelog (iter 236-240)

**Date range:** 2026-05-06 17:30 UTC to 19:30 UTC (single session)
**Status at end of arc:** **CLOSED at offline verification level**, live-game smoke `[LIVE-PENDING]` for next live-attached session
**LIVE wire count delta:** **+2** (catalog promotion + new entry)
**Master-loop tally:** 147 → **149 LIVE wires**
**Native UX delta:** +1 button (Camera & Debug tab — total 110 → 111 across 10 tabs; "Set camera pos" button label promoted from default to "(LIVE)")
**Ledger delta:** +2 entries (`struct_camera_inline_matrix` + `struct_camera_matrix_pointer`); 316 → **318 entries**

---

## What this arc closed

A1.x SetCameraPos per-coord had been **PARTIAL DEFER** since the iter-132 audit and re-confirmed iter-221: "engine Lua API takes userdata not raw floats; iter-107 ScrollCameraToTarget LIVE covers most operator use cases; per-coord setters need matrix construction." The bridge had a Phase-1 stub `Lua_SetCameraPos(x, y, z)` since iter 107 that just stored the coords in `g_pendingCamX/Y/Z` without any engine effect. **Iter 236 picked up that thread and shipped the 5-iter arc in a single session**, mirroring the iter 224-228 + iter 230-234 cadence exactly.

This is the **third back-to-back A1.x multi-iter arc** in the same session — proving the canonical 5-iter shape is repeatable across **three different implementation strategies**:
- A1.3 SetFireRate (iter 224-228) — every-frame MinHook detour at WeaponTick
- A1.x FreezeCredits (iter 230-234) — bool-freeze-precedence MinHook detour at AddCredits
- A1.x SetCameraPos (iter 236-240) — **direct-call (no detour)** at SetTransformMatrix

---

## Per-iter walk-through

### Iter 236 — RE design kickoff (research-only, no code)

- Created `knowledge-base/iter236_setcamerapos_per_coord_re_kickoff.md` (~250 lines).
- **HEADLINE FINDING**: `CameraClass::SetTransformMatrix @ 0x261BD0` (4-tool VERIFIED, 80 bytes, 16 callers) is the canonical 4x3 matrix setter callable directly from C++. **No MinHook detour needed** — bridge can construct a matrix and call the function, mirroring iter-100 SetSpeedOverride pattern.
- Decompiled bodies extracted from IDA full-corpus (`full_b66-67.json`):
  - `GetPosition @ 0x261A40` (34 bytes, 7 callers): reads `*(CameraClass+0x40)[3]/[7]/[11]` for X/Y/Z translation.
  - `SetTransformMatrix @ 0x261BD0` (80 bytes): writes 12 floats to `CameraClass+0x10..+0x40` then calls `sub_140261C20` to propagate.
- **Architectural finding**: CameraClass has dual-matrix representation:
  - **Inline matrix** at `+0x10..+0x40` (12 floats, 4x3 row-major) — written by SetTransformMatrix.
  - **Per-frame matrix pointer** at `+0x40` — points to richer 4x4 view matrix (read by GetPosition).
  - `sub_140261C20` syncs inline → pointer-target every frame.
- **Design decision matrix**: chose direct-call over MinHook detour because operator wants one-shot teleport; detour would fight the camera animation pipeline (jitter every frame).
- **Rejected**: MinHook detour (animation-pipeline conflict); engine Lua API wrapping (no `Cam_To_Pos` Lua wrapper exists per iter-106/107 finding).
- **3 patterns now in the A1.x playbook**: every-frame detour (iter-96 Damage, iter-225 FireRate), bool-freeze-precedence detour (iter-231 Credits), direct-call for one-shot mutation (iter-100 Speed, iter-237 Camera).

### Iter 237 — Bridge LIVE wire shipped (+2 LIVE flips, direct-call pattern)

- `swfoc_lua_bridge/rvas.h`: NEW constants `RVA::CameraSetTransformMatrix = 0x261BD0` + `RVA::CameraGetPosition = 0x261A40` with full RE-finding comment.
- `swfoc_lua_bridge/lua_bridge.cpp`:
  - 2 typedefs: `pfn_CameraSetTransformMatrix` + `pfn_CameraGetPosition`.
  - `LookupActiveCamera()` helper (~12 LoC): walks `qword_140B15418` (GameModeRoot value) → vftable[28] mode check (mode==2 == tactical) → `*(gm + 0x90)` → CameraClass*. Tactical-only — returns 0 for galactic mode.
  - `Lua_SetCameraPos` flipped from Phase-1 stub to LIVE direct-call: `LookupActiveCamera()` (return ERR if galactic) → memcpy current inline matrix at CameraClass+0x10 → modify [3]/[7]/[11] → call SetTransformMatrix to write back + propagate.
  - `Lua_GetCameraPos` flipped from Phase-1 stub to LIVE direct-call: `LookupActiveCamera()` → call GetPosition with float[3] out-buffer → format "X,Y,Z" string. Falls back to "0.000,0.000,0.000" on null camera (operator-friendly, parseable).
- `CapabilityStatusCatalog.cs`: SetCameraPos flipped Phase2HookPending → Live with iter-236 RE design doc cross-reference + tactical-only caveat + animation-pipeline-overwrite caveat. NEW GetCameraPos entry as Live.
- **Bridge harness 1100/0 GREEN** clean. DLL + replay rebuilt.
- **+2 LIVE flips. 147 → 149 LIVE wires.** First non-detour A1.x arc this session.

### Iter 238 — Simulator forward-compat + 7 pin tests + reverse-orphan +1

- **No new simulator code** — iter-140 prepped `HandleSetCameraPos` + `HandleGetCameraPos` + `FakeGameState.CameraPos` 9 months ago when bridge was Phase-1 stub. iter-237 LIVE wire just plugged in.
- 7-test pin file `Iter238SetCameraPosSimulatorTests.cs` validates: catalog x2 Live + iter-237 wire + iter-236 RE design doc cross-references + RVA 0x261BD0 (Set) + 0x261A40 (Get) + tactical-only + animation-pipeline-overwrite + direct-call-not-detour + FakeGameState reflection (CameraPos tuple, default zero) + 3-coord round-trip + axis independence + sequential-set + Set/Get pair-flip catalog containment.
- **Mid-iter drift caught**: reverse-orphan +1 (`SWFOC_GetCameraPos` newly unwired since editor doesn't yet call via regex-visible form). Added to `KnownUnwiredEntries` with iter-239 UX-queued comment.
- **41/41 GREEN** in 68 ms — clean run after fix.

### Iter 239 — Camera & Debug tab native UX + 2 legacy badge test updates

- 1 NEW button "Read camera pos (LIVE)" + existing "Set camera pos" updated to LIVE label.
- `BridgeCameraDebugDispatcher.GetCameraPosAsync` (regex-visible literal `"return SWFOC_GetCameraPos()"`).
- `ICameraDebugDispatcher` default impl for backward-compat with mocks.
- `CameraDebugTabState.GetCameraPosAsync` wrapper that surfaces engine response via UxFeedback.
- `CameraDebugTabViewModel`: NEW `GetCameraPosCommand` + `GetCameraPos` capability action + `GetCameraPosCore` handler + AllActions extended (15 → 16). Updated `SetCameraPos` action label to "(LIVE)" suffix.
- 6-test pin file `Iter239SetCameraPosCameraDebugTabUxTests.cs`.
- **Reverse-orphan**: dropped `SWFOC_GetCameraPos` (now regex-visible). Net change: -1.
- **Mid-iter drift caught × 2**: (1) Stale-count drift — `CameraDebugTabViewModelCapabilityTests.AllActions` pinned at 15; iter-239 extension to 16 broke it. Fixed immediately per iter-208/iter-227/iter-238 lesson. (2) Legacy badge tests — 2 tests (`SetCameraPos_BadgeIsPhase2Pending` + `Phase2PendingWarning_OnlyContainsFreeCamAndSetPos`) still expected SetCameraPos as Phase2; iter-237 catalog flip invalidated them. Updated to `SetCameraPos_BadgeIsLive` + `Phase2PendingWarning_OnlyContainsFreeCam`. **Pattern: Phase-1 → LIVE catalog flips also need to update the legacy badge-pinning tests.**
- **63/63 GREEN** in 608 ms after fixes. Editor republished.
- Total native UX surfacing: 109 → **111 buttons across 10 tabs**.

### Iter 240 — Offline verify + close (multi-iter arc finale)

- Pure verify + close-out, no code changes.
- `bridge_test_harness.exe` → **1100 passed, 0 failed**.
- `python -m verifier lint` → **0 errors / 0 warnings** (318 entries, +2 from iter 235's 316).
- **2 NEW ledger entries appended**: `struct_camera_inline_matrix` (CameraClass+0x10..+0x40 = 4x3 transform matrix, translation indices [3]/[7]/[11]) + `struct_camera_matrix_pointer` (CameraClass+0x40 = per-frame view-matrix pointer). Both 3-tool consensus via binary-fingerprint identity (matches iter-235 `struct_player_credit_cap` pattern).
- HISTORY.md prepended 5-iter arc summary entry (iter 236-240) with 5 new patterns captured.
- STATUS.md master-loop A1.x SetCameraPos row: `DEFERRED iter 132/221 → CLOSED iter 237 (bridge LIVE) → VERIFIED OFFLINE iter 240, [LIVE-PENDING]`.
- Live-game smoke verify queued for next live-attached session.

---

## Operator workflow (post-iter-239)

**To teleport the camera in tactical mode** (cinematic recording prep, debug rig framing):

1. Open editor (`publish/SwfocTrainer.App.exe`).
2. Switch to the **Camera & Debug** tab.
3. Type X/Y/Z values into the existing CamX/CamY/CamZ TextBoxes (e.g. X=1000, Y=0, Z=100 for an above-AT-AT view).
4. Click **Set camera pos (LIVE)** → bridge logs `[Bridge] SetCameraPos(...) -- LIVE`. Camera teleports to the new position immediately.
5. Click **Read camera pos (LIVE)** to confirm the engine has the new position. LastStatus shows "current pos: X,Y,Z".

**Engine semantic caveats** (per iter-236 design doc — surfaced in catalog rationale):

| Caveat | Behavior |
|---|---|
| **One-shot effect** | Operator click teleports camera once. Animation pipeline (cinematic camera, follow-camera, scrolling) overwrites within 1 frame unless paused. |
| **Pair with cinematic mode** | For sustained position override: (1) iter-145 `StartCinematicCamera`, (2) iter-237 `SetCameraPos(x,y,z)`, (3) iter-145 `EndCinematicCamera`. |
| **Pair with controls lock** | iter-208 `SWFOC_LockControls` pauses player input → SetCameraPos sticks for the lock duration. |
| **Tactical-only** | Galactic camera is a different chain (`rva_galactic_camera_ctor`). Bridge returns `ERR: no active tactical camera` when in galactic mode. Future wire could add `SWFOC_SetGalacticCameraPos` if needed. |
| **Coordinate system** | Tactical world coords (engine units, ~1 unit = 1 meter at typical AT-AT scale). Galactic uses starmap coords (out of scope). |
| **No clamp** | Camera can be moved arbitrarily far. Going below terrain or far above doesn't crash but renders nothing useful. Document in catalog rationale. |
| **Read fallback** | When no active tactical camera (mode != 2), `Lua_GetCameraPos` returns "0.000,0.000,0.000" string (parseable, downstream Lua handles). |

**Combined with iter-96 + iter-100 + iter-227 + iter-231** (the Damage / Speed / FireRate / Credits global control surface), operators now have **5 different tabs covering full global combat + economy + cinematography control**:
- Combat tab: SetDamageMultiplierGlobal + SetFireRateMultiplierGlobal
- Speed tab: SetPerFactionSpeedMultiplier
- Economy tab: SetCreditsFreezeGlobal + SetCreditsMultiplierGlobal (iter 231/233)
- Camera & Debug tab: SetCameraPos + GetCameraPos (this iter, iter 237/239)

---

## Pattern lessons captured

1. **Direct-call vs MinHook detour decision tree**:
   - Every-frame override (damage scaling, fire-rate scaling, freeze precedence) → MinHook detour. Operator wants the engine to scale these *every time the engine reads them*.
   - One-shot mutation (speed override, camera teleport) → direct call. Operator wants a single-shot mutation that the engine animation pipeline may overwrite.
   - **The choice is operator-semantic, not implementation-cost driven.**

2. **`LookupActiveCamera()` chain pattern**: direct-call wires need to FIND the operand in the engine's heap. Iter-237 chain: `g_base + GameModeRoot_Global → vftable[28] (mode check) → +0x90 (camera ptr)`. Same pattern would apply to any future direct-call wire that locates a specific engine subsystem instance (galactic camera, AI brain, save manager, etc).

3. **Phase-1 → LIVE catalog flips create cascading test obligations**: any legacy test that pinned a wire as Phase2 needs updating in the same iter. Iter-239 caught + fixed 2 such legacy tests. **Future Phase-1 → LIVE flips should grep for `_BadgeIsPhase2Pending` + `Phase2PendingWarning` test patterns on the same wire and update them in the same iter.**

4. **Tactical-only scope is intentional**: galactic camera is a different chain (`rva_galactic_camera_ctor`). Documenting the scope explicitly in catalog rationale lets operators know the limitation without surprise. **For dual-mode wires, document the scope decision in catalog rationale + provide an ERR response in the unsupported mode.**

5. **Catalog promotion vs new-entry distinction**: SetCameraPos was status promotion (Phase2 → Live, no new row). GetCameraPos was new catalog row. Both count +1 LIVE wire in the master loop but have different catalog-discipline implications: promotions need rationale-update + reverse-orphan check; new entries need rationale-write + reverse-orphan check + pair-flip check.

6. **Three back-to-back A1.x multi-iter arcs closed back-to-back this session** (15 iters, 3 implementation strategies):
   - A1.3 SetFireRate (iter 224-228) — every-frame MinHook detour at WeaponTick
   - A1.x FreezeCredits (iter 230-234) — bool-freeze-precedence MinHook detour at AddCredits
   - A1.x SetCameraPos (iter 236-240) — direct-call pattern at SetTransformMatrix
   - **The 5-iter shape is invariant to implementation strategy** — RE → bridge → sim → UX → verify works for any A1.x deferred sub-task that has a clean engine target.

7. **Simulator forward-compat is a real engineering pattern**: iter-140 wrote `HandleSetCameraPos` + `HandleGetCameraPos` + `FakeGameState.CameraPos` 9 months ago when the bridge was a Phase-1 stub. iter-237 LIVE wire just plugged into them — zero new simulator code in iter 238. **Lesson**: when shipping Phase-1 mirrors that you expect to flip LIVE later, write the simulator handlers immediately so the eventual LIVE flip is zero-friction.

---

## Cross-references

- `knowledge-base/iter236_setcamerapos_per_coord_re_kickoff.md` — RE design doc with full SetTransformMatrix + GetPosition decompiles + design decision matrix.
- `knowledge-base/HISTORY.md` (top entry) — 5-iter arc summary (iter 236-240) + reinforced + new pattern lessons.
- `knowledge-base/iter224_to_228_setfirerate_global_arc_changelog.md` — predecessor arc operator changelog (iter 229).
- `knowledge-base/iter230_to_234_freeze_credits_arc_changelog.md` — predecessor arc operator changelog (iter 235).
- `STATUS.md` (master-loop table A1.x SetCameraPos row) — closed-state summary.
- `verified_facts.json#struct_camera_inline_matrix` — NEW ledger entry from iter-236 RE finding.
- `verified_facts.json#struct_camera_matrix_pointer` — NEW ledger entry from iter-236 RE finding.

---

## What's next (iter 242+)

Per the iter 221 audit's direction-setting recommendations + the back-to-back-arc velocity:

- **Option A (multi-iter)**: pick up another A1.x deferred sub-task. Strong candidates after iter-221 audit framing + iter 224-240 closure:
  - `SetUnitField` for the 10 still-PHASE-2 sub-fields (iter-136 wired some via per-field LIVE branches; finish the rest as a multi-iter arc).
  - `SetUnitCapOverride` (only validator pinned, no setter — needs RTTI walk for the cap manager).
  - `SetGalacticCameraPos` (companion to iter-237 SetCameraPos but for galactic camera — needs galactic-mode chain).
  - `SetGameSpeed` (iter-131 confirmed defer; needs game-speed/time-scale RVA which doesn't exist in ledger; would need new RE work).
- **Option B (multi-iter)**: Thread B Overlay Phase 2-full ImGui vendoring or Thread C save-game RE.
- **Option C (single-iter polish)**: bridge harness expansion / replay harness expansion / capability surface report polish.

Given **3 A1.x arcs closed back-to-back** with the same canonical shape, **Option A continuation is the path of least resistance** — the pattern is hot and the operator gets immediate feedback (each arc = +1 to +4 LIVE flips + 1-4 native button group). **Recommended iter 242: pick the next A1.x candidate based on cleanest IDA decompile + lowest RTTI complexity.** `SetUnitField` is likely the next cleanest candidate since iter-136 already pinned 3/13 sub-fields LIVE — the remaining 10 would benefit from the same per-field branch pattern.
