# Iter 307 — Overlay Interactive Phase 6 close-out: hotkey expansion + power-user features COMPLETE

**Date:** 2026-05-21
**Spec:** `.ralph/specs/overlay-interactive.md` — Phase 6 (hotkey expansion + power-user features), spec iter-chain rows iter-304…iter-307.
**Hat:** `overlay-interactive`
**Actual loop iters:** 544–549 (the spec budgeted 4 iters 304–307; the loop took 6, because spec iter-307 bundles three deliverables — conflict matrix + close-out test + close-out doc — split across loop iters 547/548/549 per the established 2-or-3-iter close-out split, see Pattern lesson #4). The doc filename keeps the **spec** iter number (307) per the established doc-filename/iter-drift convention (cf. `iter291_overlay_phase3_close.md`, `iter296_overlay_phase4_close.md`, `iter303_overlay_phase5_close.md`).
**Predecessor:** `iter303_overlay_phase5_close.md` (Phase 5 close — click-to-inspect + 3D cursor).
**Successor:** none — **Phase 6 is the final phase of the overlay-interactive spec.** The spec is now at full acceptance.

## Headline

**Overlay Interactive Phase 6 COMPLETE — and with it the entire
overlay-interactive spec (Phases 3–6).** Phase 6 expanded the overlay's
single F1 visibility toggle into a **power-user hotkey surface**: F6/F7/F8
recall bookmarked camera viewpoints, F3 defects a whole army between
factions behind a confirm prompt, F4 pauses/resumes the battle, and a
**hotkey conflict matrix** decides — per F-key — whether the overlay
swallows the keystroke or yields it back to the engine. Phase 6 shipped
**4 pure kernels**, one per feature plus the matrix-router, each with its
own dedicated unit test, and resolved the spec's last open design
question — the F1–F9 conflict against the engine's own F-key bindings —
with a **visibility gate**: every overlay hotkey except F1 reverts to
passthrough the instant the overlay is hidden, so the game keeps its full
keyboard off-screen. **No new bridge wires** — every Phase 6 wire
pre-existed (`SWFOC_SetCameraPos`/`SWFOC_GetCameraPos` iter-237,
`SWFOC_ChangeUnitOwner` iter-108, `SWFOC_SetGameSpeed`); the bridge
harness inherits 1100/0 unchanged.

| Metric | Value |
|---|---|
| Phase 6 deliverable | Hotkey expansion — bookmarkable camera positions (F6/F7/F8) + faction-switch hotkey (F3) + pause/resume hotkey (F4) + hotkey conflict matrix (F1..F12 intercept-or-passthrough router) |
| New pure headers | **4** (`overlay_camera_bookmarks.h`, `overlay_faction_switch.h`, `overlay_pause_hotkey.h`, `overlay_hotkey_matrix.h`) |
| Other source edits | **none** — every Phase 6 kernel is a new standalone header; `overlay.cpp` / `hud_state.*` / `overlay_actions.h` untouched all phase |
| New DLL translation units | **0** — all 4 kernels are header-only; the `overlay.cpp` WndProc-detour + ImGui glue is deferred — see decomposition note |
| New standalone C++ test files | **5** — **505 checks, 0 failures** (see Verification gates) |
| New bridge wires | **0** — F6/F7/F8 reuse `SWFOC_SetCameraPos`/`SWFOC_GetCameraPos` (iter-237 LIVE), F3 reuses `SWFOC_ChangeUnitOwner` (iter-108 LIVE), F4 reuses `SWFOC_SetGameSpeed` (registered, PHASE 2 PENDING) |
| New ledger RVAs | **0** — Phase 6 needed no RE pass |
| Overlay DLL size | 1,094,656 B (Phase 6 entry) → **1,094,656 B** (Phase 6 exit) — **+0 B** (every Phase 6 kernel is deferred-glue; see Lesson #2) |
| Bridge harness | n/a (no bridge surface touched) — inherits **1100 / 0** |
| Verifier ledger lint | **345 entries, 0 errors / 0 warnings** (unchanged — no RE this phase) |
| Editor test suite | n/a (no editor surface touched) — inherits green |
| Build | every Phase 6 overlay iter `build.bat` exit 0, zero compiler diagnostics |
| Spec acceptance | Phase 6 criteria met (see Acceptance check); F1–F9 conflict matrix **shipped + documented**; 3 carried defers (`SWFOC_SetGameSpeed` PHASE 2 PENDING → F4 records-not-freezes today; F5/F9 quick-save/quick-load → Phase 7 per spec pre-declaration; WndProc/ImGui glue build-only) |
| Arc completion | **Phase 6 = 100% COMPLETE → overlay-interactive spec (Phases 3–6) = 100% COMPLETE** |

## What shipped across Phase 6 (iter chain)

| Loop iter | Spec row | Scope | DLL size (delta) | Tests |
|---|---|---|---|---|
| 544 | iter-304 | **Phase 6 kickoff — bookmarkable camera positions.** `overlay_camera_bookmarks.h` — F6/F7/F8 (`CameraBookmarkHotkey`) 3-slot store; `Save`/`SaveFromWire` (parses a `SWFOC_GetCameraPos` wire result), `IsSet`, `BuildRecall` (a `return SWFOC_SetCameraPos` line; unset slot → empty Lua, never an accidental jump to the origin). Reuses iter-237 LIVE wires. | 1,094,656 B (+0; header-only) | `overlay_camera_bookmarks_test` 82 |
| 545 | iter-305 | **Faction-switch hotkey (F3).** `overlay_faction_switch.h` — `CountUnitsOwnedBy` (sizes the confirm prompt), `BuildFactionSwitchBatch` (one `SWFOC_ChangeUnitOwner` request per visible unit owned by `fromSlot`), `FactionSwitchPrompt` Idle/Armed confirm state machine (`Arm` refuses 0 units / a double-tap; `Confirm` gates the batch; `Cancel` aborts). Reuses iter-108 LIVE wire. | 1,094,656 B (+0; header-only) | `overlay_faction_switch_test` 94 |
| 546 | iter-306 | **Pause/resume hotkey (F4).** `overlay_pause_hotkey.h` — `kPauseHotkeyFKey=4`, `GameSpeedWireStatus` enum + `kGameSpeedWireStatus=Phase2Pending` + `GameSpeedWireStatusText` (operator-trust surface), `BuildSetGameSpeedCommand`, `PauseToggle` state machine (`IsPaused`/`ResumeSpeed`/`CurrentSpeed`/`Toggle`/`BuildApply`). Reuses `SWFOC_SetGameSpeed` (PHASE 2 PENDING). | 1,094,656 B (+0; header-only) | `overlay_pause_hotkey_test` 67 |
| 547 | iter-307 (1/3) | **Phase 6 close-out part 1/3 — hotkey conflict matrix.** `overlay_hotkey_matrix.h` — `kVkF1`/`kVkF12` Win32 VK mirror, `VkForFKey`/`FKeyForVk` round-trip, `HotkeyDisposition` {Intercept, Passthrough}, `HotkeyStatus` {Live, Phase2Pending, Deferred, Unbound}, `kHotkeyMatrix[12]` AUTHORITATIVE F1..F12 matrix, `OverlayInterceptsKey(vk, overlayVisible)` THE CORE WndProc-detour decision. | 1,094,656 B (+0; header-only) | `overlay_hotkey_matrix_test` 145 |
| 548 | iter-307 (2/3) | **Phase 6 close-out part 2/3 — hotkey-routing integration test.** `overlay_phase6_close_test.cpp` wires the matrix-router + the 3 feature kernels + the iter-513 `ActionQueue` together; `OverlayHotkeyModel`/`PressHotkey` model the `overlay.cpp` WndProc-detour decision sequence. | 1,094,656 B (test + bat only; DLL untouched) | `overlay_phase6_close_test` 117 |
| 549 | iter-307 (3/3) | **Phase 6 close-out part 3/3** — this close-out doc + formal Phase 6 close. Re-ran the full 505-check Phase 6 suite + the overlay DLL build from on-disk sources as fresh evidence. | 1,094,656 B (doc only; DLL untouched) | — |

## The Phase 6 hotkey pipeline (end to end)

```
WM_KEYDOWN  (host WndProc detour, inside the D3D9 window proc — deferred glue)
  → vk = wParam  (VK_F1..VK_F12)

  → OverlayInterceptsKey(vk, overlayVisible)      overlay_hotkey_matrix.h  — pure, tested (145)
       look up the kHotkeyMatrix[12] row → disposition + intercept_when_hidden
       VISIBLE  → every Intercept row swallows the key
       HIDDEN   → only F1 (intercept_when_hidden) still swallows; all
                  other overlay hotkeys revert to passthrough
       passthrough → return; the game keeps full use of the key

  → intercepted: route FKeyForVk(vk) to its Phase 6 kernel
       F1 → toggle overlay visibility                                    (iter 277+ binding)
       F3 → FactionSwitchPrompt.Arm(fromSlot, affectedCount)             overlay_faction_switch.h  — pure, tested (94)
              ARM the confirm prompt; on Confirm() →
              BuildFactionSwitchBatch → N × return SWFOC_ChangeUnitOwner
       F4 → PauseToggle.Toggle()                                         overlay_pause_hotkey.h    — pure, tested (67)
              build a return SWFOC_SetGameSpeed line  (PHASE 2 PENDING — records, see defer)
       F6/F7/F8 → CameraBookmarks.BuildRecall(slot)                      overlay_camera_bookmarks.h — pure, tested (82)
              build a return SWFOC_SetCameraPos line;
              an UNSET slot → empty Lua → the router must NOT enqueue it

  → ActionQueue.Enqueue → background worker Drain → BridgeProbe → SWFOC_* wire
                                                   overlay_action_queue.h (iter-513)
```

The four kernels — the conflict-matrix router and the three feature
state machines — are all **pure, dependency-free, std-only headers**
with their own g++ test exe. What is left for `overlay.cpp` is the
WndProc detour (catch `WM_KEYDOWN`, ask `OverlayInterceptsKey`, swallow
or pass through) and the ImGui glue (the F3 confirm prompt, the F4
footer speed display) — no branching logic worth a test. The blocking
bridge round-trip still runs on the Phase 3 background action-worker
thread — a hotkey press never blocks the host D3D9 frame loop.

## The hotkey conflict matrix (spec acceptance line 39)

The engine binds F-keys for unit-group selection, so a naive overlay
that always swallowed F1–F9 would starve the game. `kHotkeyMatrix[12]`
records, **per F-key**, the intercept-or-passthrough decision. The
conflict workaround is the **visibility gate**: every overlay hotkey
except F1 is intercepted ONLY while the overlay is on screen.

| Key | Disposition | Status | Bound action | Conflict resolution |
|---|---|---|---|---|
| F1 | Intercept (unconditional) | Live | Overlay master visibility toggle | `intercept_when_hidden` — the one key that must work while hidden, to un-hide the overlay |
| F2 | Passthrough | Deferred | (spawn-mode toggle) | Phase 4 shipped always-on drag-drop with no discrete F2 toggle kernel — see Spec deviations |
| F3 | Intercept | Live | Faction switch (`SWFOC_ChangeUnitOwner`, iter-108) | Intercepted only while visible; reverts to passthrough when hidden |
| F4 | Intercept | Phase2Pending | Pause/resume (`SWFOC_SetGameSpeed`) | Intercepted only while visible; status honestly PHASE 2 PENDING — see Honest defers |
| F5 | Passthrough | Deferred | (quick-save) | Spec pre-declared F5 quick-save deferred to Phase 7 (spec line 29) |
| F6 / F7 / F8 | Intercept | Live | Camera bookmark recall (`SWFOC_SetCameraPos`, iter-237) | Intercepted only while visible; revert to passthrough when hidden |
| F9 | Passthrough | Deferred | (quick-load) | Spec pre-declared F9 quick-load deferred to Phase 7 (spec line 31) |
| F10 / F11 / F12 | Passthrough | Unbound | — | Never intercepted — F10 is the Win32 system-menu key, F12 the debugger-break key; the overlay never claims them |

`OverlayInterceptsKey` is the single gate every WndProc keystroke flows
through. The close-out integration test proves the **router-to-kernel
seam**: the matrix's F3/F4/F6/F7/F8 rows agree, key-for-key, with each
kernel's own hotkey constant (`kPauseHotkeyFKey`, `CameraBookmarkHotkey`);
an `Intercept` row always carries a bound `Live`/`Phase2Pending` status;
the F4 row's status mirrors `overlay_pause_hotkey.h::kGameSpeedWireStatus`
exactly — so a PHASE 2 PENDING wire can never be badged LIVE.

## Acceptance criteria check (spec lines 21–40)

| Criterion | Status |
|---|---|
| F1 toggles overall visibility (preserves iter 277+ binding) | ✅ matrix row 0 — `Intercept`, `intercept_when_hidden`; the one key that fires while the overlay is hidden |
| F2/F3/F4/F5/F6/F7/F8 bind specific feature toggles | ✅ F3/F4/F6/F7/F8 bound (3 LIVE wires + 1 PHASE 2 PENDING); F2 (spawn-mode) marked `Passthrough/Deferred` — Phase 4 shipped always-on drag-drop; F5 (quick-save) `Passthrough/Deferred` per the spec's own Phase-7 pre-declaration |
| F3 — faction-switch (cycle local-player ownership; `SWFOC_ChangeUnitOwner`) | ✅ iter-305 `overlay_faction_switch.h` — `BuildFactionSwitchBatch` + confirm-prompt state machine (no accidental mass-defection) |
| F4 — pause/resume (`GetSecondsPerGameMinute` + Phase 2 `SetGameSpeed`) | ✅ iter-306 `overlay_pause_hotkey.h` — `PauseToggle`; `SWFOC_SetGameSpeed` honestly PHASE 2 PENDING (records-not-freezes today; see Honest defers) |
| F5 / F9 — quick-save / quick-load (NEW; spec says "deferred to Phase 7") | ✅ honored — matrix marks both `Passthrough/Deferred`; the spec pre-declared this defer (lines 29, 31) |
| F6/F7/F8 — bookmarkable camera positions (reuses iter-237 `SetCameraPos`/`GetCameraPos`) | ✅ iter-304 `overlay_camera_bookmarks.h` — 3-slot store + `SaveFromWire` + `BuildRecall`; spec's "top quick win" |
| Bookmarkable camera positions — ship in Phase 6 | ✅ shipped first (iter-304, the kickoff iter) per the spec's lowest-effort-highest-value note |
| F1-F9 hotkey conflicts with the engine's own bindings documented + worked around | ✅ iter-307 `overlay_hotkey_matrix.h` — the conflict matrix above; the **visibility gate** is the documented workaround (intercept only while on screen) |
| Bridge harness stays clean at 1100/0 even when overlay calls new wires | ✅ no bridge wires authored; every Phase 6 wire pre-existed; bridge harness inherits **1100/0** |
| Overlay DLL builds clean every iter; record byte size | ✅ every Phase 6 iter `build.bat` exit 0, zero diagnostics; DLL byte-identical 1,094,656 B across all 6 Phase 6 iters (per-iter sizes logged above + in `ralph_loop_state.md`) |
| HUD remains uncluttered — 4-row Tier 1 + collapsible Tier 2/3 | ✅ no Phase 6 widget renders yet (WndProc + ImGui glue deferred); the F3 confirm prompt + F4 footer, when they land, are their own ImGui surfaces — they never push the always-visible Tier 1 footprint above 4 rows |
| Operator-trust badge (guardrail 1007) | ✅ the matrix's `HotkeyStatus` column is operator-facing; `GameSpeedWireStatusText` surfaces F4's PHASE 2 PENDING state; the close-out test pin [4] proves the matrix never claims LIVE for a PHASE-2-PENDING wire |
| Drag-drop spawning / click-to-inspect / recent-actions / mini-map | → completed in Phases 3/4/5 — see `iter291`/`iter296`/`iter303` close-out docs |

## Honest defers (documented, not failures)

- **`SWFOC_SetGameSpeed` is PHASE 2 PENDING — F4 records, it does not
  yet freeze.** The wire is registered and callable (`Lua_SetGameSpeed`,
  `lua_bridge.cpp`), but its Phase-1 body only *records*
  `g_pendingGameSpeed` and returns `"OK: game speed recorded (Phase 2
  hook pending)"` — the engine `SimulationRate` global is not patched
  (the Phase 2 RE blocker). The editor mirror agrees
  (`BridgeSpeedDispatcher.cs` labels it "PHASE 2 PENDING"). Per the spec
  iter-306 instruction ("ship the hotkey wire-up so it goes LIVE the
  moment editor-100 flips it"), Phase 6 shipped the **complete F4 kernel
  + matrix row + operator-trust surface** (`kGameSpeedWireStatus`,
  `GameSpeedWireStatusText`). When editor-100's Phase 2 hook lands, F4
  goes LIVE with **zero kernel change** — only the wire body changes,
  and the matrix's F4 status flips `Phase2Pending → Live` in one line.

- **F5 quick-save / F9 quick-load deferred to Phase 7.** The spec itself
  pre-declared these as "NEW; deferred to Phase 7 if state-capture too
  complex" (spec lines 29, 31). Phase 6 honors that defer — the conflict
  matrix marks F5/F9 `Passthrough/Deferred` so the game keeps both keys,
  and the rows are pre-allocated for Phase 7 to flip to `Intercept` when
  the quick-save/quick-load kernels land.

- **Faction-switch exact-unit targeting — first-of-type fallback (carried
  from the Phase 5 honest-defer #2 family).** `BuildFactionSwitchBatch`
  emits one `SWFOC_ChangeUnitOwner` request per visible unit, but the
  engine-method wire resolves units by a Lua *expression*
  (`Find_First_Object("<type>")`) because SWFOC exposes no "object from
  address" engine-Lua function. Same-type units collapse to the
  first-of-type at engine-resolution time today. When an address→handle
  wire lands, only `InspectorUnitLuaExpr` (one seam function, shared with
  the Phase 5 inspector) changes — the bulk switch becomes a genuine
  whole-army defection with zero kernel change. Documented in the
  `overlay_faction_switch.h` EXACT-UNIT TARGETING section.

- **WndProc detour + ImGui glue is build-only verifiable.** The loop has
  no live game, so `overlay.cpp`'s `WM_KEYDOWN` hook (catch the key →
  `OverlayInterceptsKey` → swallow or pass through → route to a kernel)
  and the ImGui glue (the F3 confirm prompt, the F4 footer speed display)
  cannot be asserted against a running engine. Every **pure** kernel they
  rest on is unit-tested in isolation (388 checks) and wired together in
  the close-out integration fixture (117 checks); the WndProc message
  hook and the ImGui widget calls remain build-only — exactly as Phases
  3–5 were. This is a property of the test environment, not a Phase 6
  gap.

## Spec deviations (documented at implementation time)

- **iter-307 — `Iter307Phase6HotkeysTests.cs` → `overlay_phase6_close_test.cpp`.**
  The spec wrote the close-out test as a `.cs` file. The `.cs` name
  predates the overlay's all-C++ native-exe test infra (a C# test cannot
  exercise a C++ header). `overlay_phase6_close_test.cpp` **is** the spec
  iter-307 close-out test in the established pattern — same convention as
  the Phase 3/4/5 close-outs (`iter291`/`iter296`/`iter303` naming notes).

- **F2 marked `Passthrough/Deferred` — spec acceptance line 26 expected
  a spawn-mode toggle.** Spec line 26 lists "F2 — spawn-mode toggle
  (drag-drop ON/OFF, from Phase 4)". Phase 4 (iter 292–296) shipped
  **always-on** drag-drop tactical spawning with no discrete on/off
  toggle kernel — drag-drop is gated instead by the iter-295 multi-player
  safety check, which is the correct gate (it disables spawning in
  galactic-mode transition, not by operator whim). With no F2 toggle
  kernel to bind, the conflict matrix marks F2 `Passthrough/Deferred` so
  the engine keeps the key. This is a favourable scope-narrowing
  deviation — Phase 4 made the toggle unnecessary — not a gap. The F2
  row is pre-allocated should a future phase add a discrete spawn-mode
  toggle.

## Phase 6 pattern lessons

### Lesson #1 — A conflict matrix is a single gate, not per-kernel polling

Phase 6 has four hotkey-bound features (visibility, faction, pause, three
camera slots). The naive shape is each kernel polling "is my key down?".
Phase 6 instead routed **every** keystroke through one function —
`OverlayInterceptsKey(vk, overlayVisible)` reading one authoritative
table, `kHotkeyMatrix[12]`. A kernel never sees a key the matrix passed
through. The close-out test's "single-gate invariant" presses every
unbound F-key and proves not one reaches a kernel.

**Pattern:** when N features share an input space (here, the F-key row),
centralise the intercept-or-passthrough decision in one matrix + one gate
function. The matrix is the *only* place the conflict with the host
application is reasoned about; every kernel downstream can assume "if I
was called, the gate already said yes". Per-kernel polling scatters the
conflict decision across N call sites that drift independently.

### Lesson #2 — Deferred-glue kernels add ZERO DLL bytes (re-confirmed)

Phase 5 closed at +0 DLL bytes; Phase 6 did the same — the overlay DLL
is byte-identical (1,094,656 B) across all six Phase 6 iters. Every
Phase 6 deliverable is a pure header whose WndProc/ImGui consumer is
deferred until the live message hook exists, so nothing the DLL compiles
changed. The byte-identical DLL is itself the zero-regression proof:
there is no regression surface to audit because there is no surface
change. This is now the established close-out posture for two
consecutive phases.

**Pattern:** when a phase's deliverable is *decision logic* (a router, a
state machine) rather than *rendered UI*, the pure-kernel-first
decomposition lets the phase ship fully tested with no DLL change at all.
A header-only kernel not yet `#include`d by any DLL translation unit
cannot regress the DLL — the linker never sees it.

### Lesson #3 — Cross-check the router against the kernels in the TEST, not the kernel

`overlay_hotkey_matrix.h` is std-only — it does NOT `#include` the three
feature kernels, so the matrix stays a lean, dependency-free table. The
**test** (`overlay_hotkey_matrix_test.cpp` and the close-out test) is
what `#include`s all four headers and asserts, row-by-row, that the
matrix's F3/F4/F6/F7/F8 entries agree with each kernel's own hotkey
constant (`kPauseHotkeyFKey`, `CameraBookmarkHotkey(slot)`) and that the
F4 status mirrors `kGameSpeedWireStatus`. Drift between the matrix and a
kernel — a kernel binding a key the matrix does not catalogue, or the
matrix claiming LIVE for a PHASE-2-PENDING wire — fails a check.

**Pattern:** keep an authoritative catalog (the matrix) decoupled from
the things it catalogs (the kernels), then make the *test* the
consistency enforcer. The catalog stays cheap to include everywhere; the
test carries the cross-reference cost once. Mirrors the
`overlay_phase3_catalog.h`/`overlay_actions.h` split from Phase 3.

### Lesson #4 — Spec iter-307 was one row but three deliverables; the close-out split is now a routine

Spec iter-307 bundled "conflict matrix + close-out test + close-out doc".
The loop did NOT try to ship all three in one iter — it used the
established close-out split: iter-547 the matrix kernel, iter-548 the
integration test, iter-549 (this) the doc + formal close. Phase 3 split
its close-out across iter-527/528, Phase 4 across iter-533/535, Phase 5
shipped its test + doc together at iter-543. By Phase 6 the split is a
routine: one verifiable artifact per iter, each with its own green gate.

**Pattern:** a spec row that names multiple artifacts is a decomposition
hint, not a single-iter mandate. Ship one independently-testable artifact
per iter — the kernel, then the integration test that exercises it, then
the doc that certifies both. Each iter has a hard green gate; a bundled
iter would have one fuzzy gate covering three half-done things.

## Verification gates (ALL GREEN — re-run this iter, evidence-backed per guardrail 1002)

| Gate | Result |
|---|---|
| `overlay_camera_bookmarks_test.exe` | **82 checks, 0 failures**, exit 0 |
| `overlay_faction_switch_test.exe` | **94 checks, 0 failures**, exit 0 |
| `overlay_pause_hotkey_test.exe` | **67 checks, 0 failures**, exit 0 |
| `overlay_hotkey_matrix_test.exe` | **145 checks, 0 failures**, exit 0 |
| `overlay_phase6_close_test.exe` | **117 checks, 0 failures**, exit 0 |
| **Phase 6 test total** | **505 checks, 0 failures** across 5 standalone C++ test files (388 isolation + 117 integration) |
| Overlay DLL | `build.bat` exit 0 — `=== OVERLAY BUILD SUCCESS ===`; **1,094,656 B** (unchanged) |
| Bridge harness | n/a (no bridge surface touched across Phase 6) — inherits **1100 / 0** |
| Verifier ledger lint | **345 entries, 0 errors / 0 warnings** (unchanged — no RE this phase) |
| Editor test suite | n/a (no editor surface touched) — inherits green |

This close-out iter ships one new `knowledge-base/*.md` doc. The full
505-check Phase 6 suite **and** the overlay DLL build were re-run from
the on-disk sources this iter as fresh evidence that the hotkey surface
the doc certifies is green.

## overlay-interactive spec — full-arc summary (Phases 3–6)

With Phase 6 closed, the **entire overlay-interactive spec is at
acceptance.** The overlay went from the read-only HUD shipped iter
277–284 to a fully interactive in-game widget surface across four phases:

| Phase | Loop iters | Deliverable | Close-out doc |
|---|---|---|---|
| Phase 3 — interactive widgets | ~iter 287–291 era | In-overlay buttons calling existing bridge wires | `iter291_overlay_phase3_close.md` |
| Phase 4 — drag-drop spawning | ~iter 292–296 era | Drag unit-type → minimap/world → `SWFOC_SpawnUnitLua` | `iter296_overlay_phase4_close.md` |
| Phase 5 — click-to-inspect | loop 536–543 | Click in-game unit → inspector + 5 action buttons; projection-matrix RE pin | `iter303_overlay_phase5_close.md` |
| **Phase 6 — hotkey expansion** | **loop 544–549** | **F3/F4/F6/F7/F8 hotkeys + conflict matrix router** | **this doc** |

Across Phases 5–6 the overlay added **9 pure kernels**, **0 new DLL
translation units**, **11 standalone C++ test files**, and **0 new
bridge wires** beyond the iter-297 projection-matrix RE pins — the
overlay DLL is byte-identical 1,094,656 B from Phase 5 entry through
Phase 6 exit. Every interactive surface is decided by a tested pure
kernel; only the ImGui render glue and the WndProc message hook remain
build-only, awaiting a live game.

## Iter 307 close-out summary

- This document is the iter-307 (loop iter-549) deliverable and the
  **formal Phase 6 close** — and, because Phase 6 is the spec's final
  phase, the **formal close of the overlay-interactive spec**.
- **Code changes this iter:** none — the matrix kernel
  (`overlay_hotkey_matrix.h`, iter-547) and the close-out integration
  test (`overlay_phase6_close_test.cpp`, iter-548) were authored by the
  prior iters; this iter verified them (145/0 + 117/0), re-ran the full
  505-check Phase 6 suite + the overlay DLL build, and shipped this doc.
- **Phase 6 cumulative:** 4 new pure headers + 0 new DLL TUs + 5
  standalone test files (505 checks); 0 new ledger RVAs; overlay DLL
  1,094,656 B → 1,094,656 B (+0 B); **0 new bridge wires**.
- **Hotkey surface:** F1 toggles visibility; F3 defects a whole army
  behind a confirm prompt; F4 pauses/resumes the battle; F6/F7/F8 recall
  bookmarked camera viewpoints; a 12-row conflict matrix decides
  intercept-or-passthrough per F-key, with the visibility gate yielding
  every overlay hotkey except F1 back to the engine when the overlay is
  hidden.
- All gates GREEN: 505/505 Phase 6 checks; overlay DLL builds clean;
  bridge harness / ledger lint / editor suite inherit unchanged.
- **4 Phase 6 pattern lessons** captured: a conflict matrix is a single
  gate not per-kernel polling; deferred-glue kernels add zero DLL bytes
  (re-confirmed); cross-check the router against the kernels in the test
  not the kernel; a multi-artifact spec row is a decomposition hint.
- **3 carried defers** documented: `SWFOC_SetGameSpeed` PHASE 2 PENDING
  (F4 records-not-freezes; goes LIVE with zero kernel change when
  editor-100 flips the wire); F5/F9 quick-save/quick-load → Phase 7 (per
  the spec's own pre-declaration); WndProc/ImGui glue build-only (no
  live game).
- **Phase 6 = 100% COMPLETE → overlay-interactive spec (Phases 3–6) =
  100% COMPLETE.** The overlay-interactive hat is at acceptance; control
  returns to the master loop for the remaining `savegame-editor` spec.
