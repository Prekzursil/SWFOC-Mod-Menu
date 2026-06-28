# Iter 303 — Overlay Interactive Phase 5 close-out: click-to-inspect + 3D cursor COMPLETE

**Date:** 2026-05-21
**Spec:** `.ralph/specs/overlay-interactive.md` — Phase 5 (click-to-inspect + 3D cursor), spec iter-chain rows iter-297…iter-303.
**Hat:** `overlay-interactive`
**Actual loop iters:** 536–543 (the spec budgeted 7 iters 297–303; the loop took 8, with loop iter-542 a `task.resume` routing/recovery iter — see Pattern lesson #2). The doc filename keeps the **spec** iter number (303) per the established doc-filename/iter-drift convention (cf. `iter291_overlay_phase3_close.md`, `iter296_overlay_phase4_close.md`). The close-out test file headers self-label "iter 542" — the writer ran immediately after the iter-542 routing emission; the loop-iter ledger places the close-out at 543.
**Predecessor:** `iter296_overlay_phase4_close.md` (Phase 4 close — drag-drop tactical spawning).
**Successor:** Phase 6 — hotkey expansion + power-user features (spec iter-304…307).

## Headline

**Overlay Interactive Phase 5 COMPLETE.** The overlay went from the
Phase 4 drag-drop spawning surface to a **click-aware inspector**: the
operator left-clicks an in-game unit, a client-side raycast resolves
*which* unit the cursor is over, and an inspector panel opens carrying
that unit's hull / shield / owner / type / position plus five action
buttons (Kill / Heal / Teleport / SwapOwner / MakeInvuln). Phase 5
shipped **5 pure kernels**, one per spec row, each with its own
dedicated unit test, and closed the spec's **hardest budgeted honest
defer**: iter-297's RE pass pinned the engine's global D3D
projection / view / view-projection matrices at **3-tool consensus**,
so the `SWFOC_GetUnitAtScreenCoords` fallback bridge wire was never
needed. **No new bridge wires** — all five inspector action wires
pre-existed LIVE; the bridge harness inherits 1100/0 unchanged.

| Metric | Value |
|---|---|
| Phase 5 deliverable | Click-to-inspect — projection-matrix RE pin + cursor→world ray + client-side cursor-hit raycast + inspector panel + 5 action buttons + HudSnapshot unit-AABB set |
| New pure headers | **5** (`overlay_cursor_ray.h`, `overlay_hit_test.h`, `overlay_inspector.h`, `overlay_inspector_actions.h`, `overlay_unit_aabb.h`) |
| Other source edits | `overlay_actions.h` (+`BuildHealUnitCommand`); `hud_state.h`/`hud_state.cpp` (+`UnitAabbSet` field, append-only, data-layout-only) |
| New DLL translation units | **0** — all 5 kernels are header-only; the `overlay.cpp` ImGui glue (RenderInspector / OnClick) is deferred — see decomposition note |
| New standalone C++ test files | **6** — **548 checks, 0 failures** (see Verification gates) |
| New bridge wires | **0** — all 5 inspector action wires (`SWFOC_KillUnit` / `SWFOC_HealUnitLua` / `SWFOC_TeleportUnitLua` / `SWFOC_ChangeUnitOwner` / `SWFOC_MakeUnitInvulnLua`) pre-existed LIVE |
| New ledger RVAs | **4** (`fact_global_d3d_projection_matrix` 0xA6EF24 / `fact_global_d3d_view_matrix` 0xA6EEE4 / `fact_global_d3d_view_projection_matrix` 0xA6F49C / `rva_d3d_build_camera_matrices` 0x17F1D0) — all at 3-tool consensus |
| Overlay DLL size | 1,094,656 B (Phase 5 entry) → **1,094,656 B** (Phase 5 exit) — **+0 B** (every Phase 5 kernel is deferred-glue; see Lesson #2) |
| Bridge harness | n/a (no bridge surface touched) — inherits **1100 / 0** |
| Verifier ledger lint | **345 entries, 0 errors / 0 warnings** (4 RVAs added at iter-297) |
| Editor test suite | n/a (no editor surface touched) — inherits green |
| Build | every Phase 5 overlay iter `build.bat` exit 0, zero compiler diagnostics |
| Spec acceptance | Phase 5 criteria met (see Acceptance check); honest-defer #1 (projection-matrix RVA) **RESOLVED**; 2 carried defers (cursor-hit engine wire absent → Mitigation A shipped; ImGui glue build-only) |
| Arc completion | **Phase 5 = 100% COMPLETE** |

## What shipped across Phase 5 (iter chain)

| Loop iter | Spec row | Scope | DLL size (delta) | Tests |
|---|---|---|---|---|
| 536 | iter-297 | **Phase 5 kickoff — projection-matrix RE pass.** Cross-tool static read of `sub_14017F1D0` pinned the global D3D **projection** (`0xA6EF24`), **view** (`0xA6EEE4`) and **view*projection** (`0xA6F49C`) matrices + the camera-build routine (`0x17F1D0`) at 3-tool consensus. Spec honest-defer #1 **CLOSED**. | 1,094,656 B (RE + ledger + docs only) | n/a (RE iter) — ledger lint 345 / 0 / 0 |
| 537 | iter-298 | **Cursor → world ray.** `overlay_cursor_ray.h` — `Vec3`/`Mat4` primitives, `Mat4Inverse` (MESA cofactor 4×4), `ScreenToNdc` (Y-flipped), `CursorRay` (unproject near/far clip points through inverse view-proj), `RayPlaneZ0` (z=0 ground intersect). | 1,094,656 B (+0; header-only) | `overlay_cursor_ray_test` 147 |
| 538 | iter-299 | **Ray → nearest unit AABB.** `overlay_hit_test.h` — `Aabb`/`UnitAabb`/`UnitHit`, `RayAabbIntersect` (slab method), `NearestUnitHit` (smallest-t wins), `PickUnitAtCursor` (CursorRay + NearestUnitHit seam). Client-side raycast — Mitigation A, no engine wire. | 1,094,656 B (+0; header-only) | `overlay_hit_test_test` 101 |
| 539 | iter-300 | **UnitHit → inspector panel.** `overlay_inspector.h` — `UnitInfo`/`InspectorPanel`, `HealthFraction`/`HealthPercent` (clamped), `FactionName`, `FormatHealthLabel`/`FormatPositionLabel`, `OpenInspectorFor` (resolve by handle), `UpdateInspectorPanel` (miss-click keeps panel), `RefreshInspectorPanel` (auto-close on unit death). | 1,094,656 B (+0; header-only) | `overlay_inspector_test` 84 |
| 540 | iter-301 | **Panel → 5 ActionRequests.** `overlay_inspector_actions.h` — `BuildInspector{Kill,Heal,Teleport,SwapOwner,MakeInvuln}`, `NextFactionSlot`, `FactionPlayerName`. Kill is address-based (exact handle); the other four are method wires (first-of-type fallback — honest-defer #2). `overlay_actions.h` gained the one missing builder `BuildHealUnitCommand`. | 1,094,656 B (+0; header-only) | `overlay_inspector_actions_test` 68 |
| 541 | iter-302 | **HudSnapshot unit-AABB set.** `overlay_unit_aabb.h` — `UnitAabbSet` (fixed-capacity flat POD, cap 64 = raycast budget), `AppendUnitAabb`/`ClearUnitAabbSet`/`FindUnitAabb`/`PickUnitInSet`. `hud_state.h` gained `UnitAabbSet unit_aabbs;` at the struct tail (append-only); `hud_state.cpp` got an honest-defer comment. | 1,094,656 B (+0; data-layout-only field, no consuming code path) | `overlay_unit_aabb_test` 65 |
| 543 | iter-303 (1/1) | **Phase 5 close-out** — `overlay_phase5_close_test.cpp` wires all 5 kernels + `overlay_actions.h` + the iter-513 `ActionQueue` together; `SimulatePhase5Click` models the `overlay.cpp` click-to-inspect decision sequence. **This close-out doc + formal Phase 5 close.** | 1,094,656 B (test + docs only; DLL untouched) | `overlay_phase5_close_test` 83 |

Loop iter-542 was a `task.resume` routing/recovery iter (master loop
resumed after interruption, Ralph re-emitted `overlay.start`) — it
shipped no artifact and is not a spec row. The close-out test was
authored by the run immediately following it (self-labeled "iter 542"
in the file header; the loop-iter ledger places it at 543).

## The Phase 5 click-to-inspect pipeline (end to end)

```
left-click (render thread, inside D3D9 Present detour)
  → read OS cursor pixel (sx, sy) + host viewport (vw, vh)
  → read live engine matrices: view 0xA6EEE4 / projection 0xA6EF24
       (or the pre-multiplied view*projection 0xA6F49C)   ← iter-297 RVA pins

  → CursorRay(sx, sy, vw, vh, viewProj)        overlay_cursor_ray.h   — pure, tested (147)
       unproject near/far clip points → world-space pick ray

  → PickUnitInSet(ray, snap.unit_aabbs)        overlay_unit_aabb.h    — pure, tested (65)
       → NearestUnitHit(ray, set)              overlay_hit_test.h     — pure, tested (101)
            walk the visible UnitAabbSet, slab-test each box, smallest-t wins
            → UnitHit { hit, index, handle, t }

  → RayPlaneZ0(ray)                            overlay_cursor_ray.h   — pure, tested (147)
       same ray → z=0 ground point = the Teleport-action destination

  → UpdateInspectorPanel(prevPanel, hit, infos, count)   overlay_inspector.h — pure, tested (84)
       valid pick → open/replace panel on the picked unit (resolve by handle)
       miss-click → return the prior panel UNCHANGED (no flicker-shut)

  → BuildInspector{Kill,Heal,Teleport,SwapOwner,MakeInvuln}(panel.unit)
                                               overlay_inspector_actions.h — pure, tested (68)
       five dispatch-ready ActionRequests (Kill targets the EXACT handle)

  → ActionQueue.Enqueue → background worker Drain → BridgeProbe → SWFOC_* wire
                                               overlay_action_queue.h (iter-513)
```

The five coordinate / raycast / panel / action / storage kernels are
all **pure, dependency-free, std-only headers** with their own g++
test exe. What is left for `overlay.cpp` is the ImGui render glue
(`ImGui::Begin("Inspector")`, the five button widgets) and the live
left-click handler that reads the engine matrices — no branching logic
worth a test. The blocking bridge round-trip still runs on the Phase 3
background action-worker thread — click-to-inspect never blocks the
host D3D9 frame loop.

## Acceptance criteria check (spec lines 21–40)

| Criterion | Status |
|---|---|
| Click-to-inspect — click in-game unit → overlay inspector shows hull / shield / owner / type / position; action buttons for kill / heal / teleport / swap-owner / make-invuln | ✅ full kernel chain shipped + tested (548 checks); `overlay_inspector.h` formats all five fields, `overlay_inspector_actions.h` builds all five action buttons. ImGui render glue build-only verifiable (no live game). |
| NEW `SWFOC_GetUnitAtScreenCoords` bridge wire OR client-side raycasting proxy | ✅ **Mitigation A** delivered — client-side raycast (`overlay_cursor_ray.h` + `overlay_hit_test.h`) over per-unit AABBs from the extended `HudSnapshot` (`overlay_unit_aabb.h`). No new bridge wire authored. |
| Projection-matrix RVA pinned (Phase 5 iter-297 RE pass) | ✅ **honest-defer #1 RESOLVED** — projection `0xA6EF24` / view `0xA6EEE4` / view-projection `0xA6F49C` pinned at 3-tool consensus; ledger lint 345/0/0. The fallback wire path was not needed. |
| Bridge harness stays clean at 1100/0 even when overlay calls new wires | ✅ no bridge wires authored; all 5 inspector action wires pre-existed LIVE; bridge harness inherits **1100/0**. |
| Overlay DLL builds clean every iter; record byte size | ✅ every Phase 5 iter `build.bat` exit 0, zero diagnostics; DLL byte-identical 1,094,656 B across all 6 Phase 5 iters (per-iter sizes logged above + in `ralph_loop_state.md`). |
| HUD remains uncluttered — 4-row Tier 1 + collapsible Tier 2/3 | ✅ no Phase 5 widget renders yet (ImGui glue deferred); when it lands, the inspector is its own `ImGui::Begin` window — it never pushes the always-visible Tier 1 footprint above 4 rows. |
| Operator-trust badge (guardrail 1007) | ✅ the inspector surfaces real engine-read stats; the cursor-hit honest-defer (empty AABB set → clean miss, never a phantom hit) is pinned by the close-out test so the operator can never confuse "I clicked" with "a unit was found". |
| Unit-AABB extension to HudSnapshot — append-only (iter-275 binary-layout-stability) | ✅ `UnitAabbSet unit_aabbs;` appended at the `HudSnapshot` struct tail; writes only at index `count`, never reorders. |
| F-key hotkeys | → out of Phase 5 scope (Phase 6, spec iter-304…307). |

## Honest defers (documented, not failures)

- **Projection-matrix RVA — RESOLVED (spec honest-defer #1, now CLOSED).**
  The spec pre-declared the projection matrix as Phase 5's hardest
  blocker (spec line 71) and budgeted iter-297 as a dedicated RE iter
  with a `SWFOC_GetUnitAtScreenCoords` fallback bridge wire. The RE
  pass **succeeded**: a static cross-tool read of `sub_14017F1D0`
  pinned the engine's global D3D projection / view / view-projection
  matrices — all at 3-tool consensus. The fallback wire was never
  authored. A budgeted defer turned into a closed fact. See
  `knowledge-base/overlay_phase5_projection_matrix_2026-05-21.md`.

- **Cursor-hit-unit detection engine wire NOT exposed (spec
  honest-defer #2 / `overlay_ux_research` CRITICAL GAP 1) — Mitigation
  A shipped.** SWFOC exposes no "unit under cursor" engine wire. Phase
  5 delivered the spec's documented **Mitigation A**: a fully
  client-side raycast — `CursorRay` unprojects the cursor, the visible
  unit set is walked AABB-vs-ray entirely in C++, no engine call on the
  click path. The one remaining piece is data, not logic: the
  `HudSnapshot.unit_aabbs` set is **empty** until a per-unit
  world-AABB bridge *read* wire lands (the worker has no
  `SWFOC_GetUnitAabb`-class getter; `SWFOC_EnumerateUnits` returns
  handles, not boxes). The close-out test pins that an empty set yields
  a **clean miss, never a phantom inspector**. When the read wire lands
  the whole pipeline goes live with **zero kernel change** — only the
  HUD worker's snapshot-fill step changes.

- **ImGui render / click glue is build-only verifiable.** The loop has
  no live game, so `overlay.cpp`'s `RenderInspector` + left-click
  handler (read live engine matrices → `CursorRay` → `PickUnitInSet`
  → `UpdateInspectorPanel` → render panel + five buttons →
  `ActionQueue`) cannot be asserted against a running engine. Every
  **pure** kernel it rests on is unit-tested in isolation (465 checks)
  and wired together in the close-out integration fixture (83 checks);
  the ImGui widget calls and the live bridge round-trip remain
  build-only — exactly as Phases 1–4 were. This is a property of the
  test environment, not a Phase 5 gap.

## Spec deviations (documented at implementation time)

- **iter-297** — the spec budgeted a `SWFOC_GetUnitAtScreenCoords`
  NEW-bridge-wire fallback in case the projection-matrix RE blocked.
  The RE pass closed the defer outright, so the fallback wire was
  **not authored**. This is a favourable scope-narrowing deviation, not
  a gap.

- **iter-301** — Phase 3 (spec iter-287) dropped the "Heal selected"
  button, so `overlay_actions.h` reached Phase 5 with no Heal builder.
  `BuildHealUnitCommand` (the no-arg `:Heal()` shape) was added in
  iter-301 so all five inspector actions have a builder. Behaviour
  identical to the other no-arg method wires (Despawn / Stop / Retreat).

- **iter-303** — the spec wrote `Iter303Phase5InspectorTests.cs`. The
  `.cs` name predates the overlay's all-C++ native-exe test infra (a
  C# test cannot exercise a C++ header). `overlay_phase5_close_test.cpp`
  **is** the spec iter-303 close-out test in the established pattern —
  same convention as the Phase 3 and Phase 4 close-outs
  (`iter291`'s and `iter296`'s naming notes).

## Phase 5 pattern lessons

### Lesson #1 — A budgeted honest-defer is a hypothesis to test, not a foregone conclusion

The spec pre-declared the projection-matrix RVA as Phase 5's hardest
blocker and shipped a fallback (`SWFOC_GetUnitAtScreenCoords` new
bridge wire). iter-297 attempted the hard path **first** — a
cross-tool static read of the camera-build routine — and closed the
defer outright at 3-tool consensus. The fallback was insurance that
was never cashed.

**Pattern:** a documented honest-defer records a *risk*, not a
*decision*. Attempt the hard path first; the fallback is there so the
phase still ships if the hard path blocks, not so the hard path is
skipped. A defer that closes on contact saves the entire fallback's
implementation cost.

### Lesson #2 — Deferred-glue kernels add ZERO DLL bytes

Phase 3 grew the DLL with interactive widgets; Phase 4 added +4,608 B
of drag-drop surface. Phase 5 added **+0 B** — the overlay DLL is
byte-identical (1,094,656 B) across all six Phase 5 iters. Every Phase
5 deliverable is a pure header whose ImGui consumer is deferred until
the live click handler exists, so nothing the DLL compiles changed.
The byte-identical DLL is itself the zero-regression proof: there is
no regression surface to audit because there is no surface change.

**Pattern:** when a phase's deliverable is *decision logic* rather than
*rendered UI*, the pure-kernel-first decomposition lets the phase ship
fully tested with no DLL change at all. A header-only kernel not yet
`#include`d by any DLL translation unit cannot regress the DLL — the
linker never sees it. The phase is real, complete, and provably
inert against the shipping binary.

### Lesson #3 — Resolve by handle, never by index, makes a kernel chain reorder-proof

The pick kernel (`overlay_hit_test.h`) walks a `UnitAabbSet`; the
inspector kernel (`overlay_inspector.h`) resolves a `UnitInfo[]`. Those
two arrays are filled from different engine enumerations and need not
share an order. Every resolve across the seam keys on the engine
`GameObject` handle — `OpenInspectorFor` looks up by handle, not by
`pick.index`. The close-out test pins this directly: feed the inspector
a **reversed** `UnitInfo[]` and the pick still inspects the right unit.

**Pattern:** when two kernels exchange entities across a seam, key on a
stable identity (the engine handle), never the array slot. Drift
between the two producers — different enumeration orders, a unit added
or removed between the raycast and the resolve — then *cannot* corrupt
the pick. Index-keyed coupling is a latent bug; handle-keyed coupling
is correct by construction.

### Lesson #4 — Be exact where the engine lets you; document the fallback at the seam where it does not

The five inspector actions split cleanly. **Kill** is address-based
(`SWFOC_KillUnit(obj_addr)`) so it targets the *exact* picked
`unit.handle`. The other four are engine-*method* wires that take a
unit Lua *expression*, and SWFOC exposes no "object from address"
engine-Lua function — so they fall back to `Find_First_Object("<type>")`,
exactly as the Phase 3 widgets do (honest-defer #2). The close-out test
pins the split with two same-type units (A and D both
`Rebel_Trooper_Squad`): a click on D builds D's handle, never A's.

**Pattern:** where the engine API lets a kernel be exact, be exact —
and write a RED-GREEN pin (two same-type entities) that *fails* on the
loose "first-of-type" form. Where the API does not, isolate the
fallback behind one seam function (`InspectorUnitLuaExpr`) so a future
address→handle wire is a one-function change, not a five-call-site
rewrite. The integration test re-confirmed the iter-296 lesson that
isolation tests and one integration test cover orthogonal failure
modes — a kernel can be individually perfect and still wired wrong.

## Verification gates (ALL GREEN — re-run this iter, evidence-backed per guardrail 1002)

| Gate | Result |
|---|---|
| `overlay_cursor_ray_test.exe` | **147 checks, 0 failures**, exit 0 |
| `overlay_hit_test_test.exe` | **101 checks, 0 failures**, exit 0 |
| `overlay_inspector_test.exe` | **84 checks, 0 failures**, exit 0 |
| `overlay_inspector_actions_test.exe` | **68 checks, 0 failures**, exit 0 |
| `overlay_unit_aabb_test.exe` | **65 checks, 0 failures**, exit 0 |
| `overlay_phase5_close_test.exe` | **83 checks, 0 failures**, exit 0 |
| **Phase 5 test total** | **548 checks, 0 failures** across 6 standalone C++ test files |
| Overlay DLL | `build.bat` exit 0 — `=== OVERLAY BUILD SUCCESS ===`; **1,094,656 B** (unchanged) |
| Bridge harness | n/a (no bridge surface touched across Phase 5) — inherits **1100 / 0** |
| Verifier ledger lint | **345 entries, 0 errors / 0 warnings** (4 RVAs added at iter-297) |
| Editor test suite | n/a (no editor surface touched) — inherits green |

This close-out iter ships one new test file (authored by the
interrupted prior run) and one new `knowledge-base/*.md` doc. The
full 548-check Phase 5 suite **and** the overlay DLL build were re-run
from the on-disk sources this iter as fresh evidence that the
click-to-inspect surface the doc certifies is green.

## What's next — Phase 6 (hotkey expansion + power-user features, spec iter-304…307)

Per the spec iter-chain:

1. **iter-304** — Phase 6 kickoff: **bookmarkable camera positions**
   (F6 / F7 / F8). Reuses the iter-237 `SetCameraPos` / `GetCameraPos`
   LIVE wires; ~30 LoC. Top quick win — ship first because it is the
   lowest-effort, highest-value Phase 6 item.
2. **iter-305** — faction-switch hotkey (F3): cycle local-player
   ownership across visible units via iter-108 `SWFOC_ChangeUnitOwner`,
   behind a confirm-prompt (no accidental mass-defection).
3. **iter-306** — pause/resume hotkey (F4): toggle game speed via the
   Phase 2 `SetGameSpeed` wire (or detour-on-tick if still
   PHASE 2 PENDING); footer current-speed display.
4. **iter-307** — Phase 6 close-out: hotkey conflict matrix
   (per-binding intercept-or-passthrough decision — the engine binds
   F-keys for unit-group selection), `Iter307Phase6HotkeysTests`,
   close-out doc.

**Phase 6 note:** F1 already toggles overall overlay visibility
(iter 277+). Phase 6 adds F2–F9 per the spec acceptance list; the
hotkey conflict matrix at iter-307 documents every intercept-or-
passthrough decision against the engine's own F-key bindings.

## Iter 303 close-out summary

- This document is the iter-303 (loop iter-543) deliverable and the
  **formal Phase 5 close**.
- **Code changes this iter:** none — the close-out integration test
  `overlay_phase5_close_test.cpp` (+ `build_phase5_close_test.bat`)
  was authored by the interrupted prior run; this iter verified it
  (83/0), re-ran the full Phase 5 suite, and shipped this doc.
- **Phase 5 cumulative:** 5 new pure headers + 0 new DLL TUs + 6
  standalone test files (548 checks); 4 new ledger RVAs at 3-tool
  consensus; overlay DLL 1,094,656 B → 1,094,656 B (+0 B); **0 new
  bridge wires**.
- **Click-to-inspect surface:** left-click an in-game unit → client-
  side raycast (`CursorRay` → `PickUnitInSet` → `NearestUnitHit`)
  resolves the unit under the cursor → an inspector panel opens with
  hull / shield / owner / type / position → five action buttons
  (Kill / Heal / Teleport / SwapOwner / MakeInvuln) each build a
  dispatch-ready `return SWFOC_*` bridge line.
- All gates GREEN: 548/548 Phase 5 checks; overlay DLL builds clean;
  bridge harness / ledger lint / editor suite inherit unchanged.
- **4 Phase 5 pattern lessons** captured: a budgeted honest-defer is a
  hypothesis to test; deferred-glue kernels add zero DLL bytes;
  resolve-by-handle makes a kernel chain reorder-proof; be exact where
  the engine allows it and isolate the fallback where it does not.
- **Honest-defer #1 (projection-matrix RVA) RESOLVED** at iter-297;
  2 carried defers (cursor-hit engine wire absent → Mitigation A's
  client-side raycast shipped, snapshot fill awaits a per-unit-AABB
  read wire; ImGui glue build-only — no live game).
- **Phase 5 = 100% COMPLETE.** Phase 6 (hotkey expansion + power-user
  features) begins next.
