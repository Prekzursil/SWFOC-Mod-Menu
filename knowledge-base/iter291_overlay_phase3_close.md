# Iter 291 — Overlay Interactive Phase 3 close-out: interactive widget surface COMPLETE

**Date:** 2026-05-21
**Spec:** `.ralph/specs/overlay-interactive.md` — Phase 3 (interactive widgets), spec iter-chain rows iter-287…iter-291.
**Hat:** `overlay-interactive`
**Actual loop iters:** 512–528 (the spec budgeted 5 iters 287–291; the recovery-heavy loop took ~16 — see Pattern lesson #2). The doc filename keeps the **spec** iter number (291) per the established doc-filename/iter-drift convention (cf. `overlay_phase3_recentactions_iter521.md`, `overlay_phase3_teleport_faction_iter524.md`).
**Predecessor:** `iter285_tier3_bridge_wires_close.md` (Phases 1–2 read-only HUD close).
**Successor:** Phase 4 — drag-drop tactical spawning (spec iter-292…296).

## Headline

**Overlay Interactive Phase 3 COMPLETE.** The overlay went from the
read-only HUD shipped iter 277–285 to a **fully interactive in-game
widget surface**: a separate "Actions" window with five operator
buttons (Spawn / Make Invuln / Kill / Teleport / Faction Switch), a
5-slot recent-actions toolbar, a per-widget capability-badge table, a
non-blocking render-thread→worker action pipeline, and a host WndProc
detour so ImGui receives input — without ever blocking the game's
D3D9 frame loop. **No new bridge wires** — all five action wires
pre-existed LIVE; the bridge harness inherits 1100/0 unchanged.

| Metric | Value |
|---|---|
| Phase 3 deliverable | Interactive "Actions" window — 5 buttons + recent-actions toolbar + capability-badge table |
| New pure headers | **6** (`overlay_actions.h`, `overlay_action_queue.h`, `overlay_input.h`, `overlay_action_worker.h`, `overlay_recent_actions.h`, `overlay_phase3_catalog.h`) |
| New DLL translation unit | **1** (`overlay_action_worker.cpp`) |
| New standalone C++ test files | **6** — **195 checks, 0 failures** (see Verification gates) |
| New bridge wires | **0** — all 5 action wires pre-existed LIVE |
| Overlay DLL size | 1,040,384 B (Phase 3 entry) → **1,090,048 B** (+49,664 B; +4.78%) |
| Bridge harness | n/a (no bridge surface touched) — inherits **1100 / 0** |
| Verifier ledger lint | n/a (no ledger changes) — inherits **0 / 0** |
| Editor test suite | n/a (no editor surface touched) — inherits green |
| Build | every overlay iter `build.bat` exit 0, zero compiler diagnostics |
| Spec acceptance | Phase 3 criteria met (see Acceptance check); 1 honest defer (Pause → Phase 6) |
| Arc completion | **Phase 3 = 100% COMPLETE** |

## What shipped across Phase 3 (iter chain)

| Loop iter | Spec row | Scope | DLL size (delta) | Tests |
|---|---|---|---|---|
| 512 | iter-287 | **Phase 3 kickoff** — `overlay_actions.h` Lua-command builders + `RenderActionsWindow` skeleton (widgets rendered, `BeginDisabled`). | 1,040,384 → 1,050,624 B (+10,240) | `overlay_actions_test` 19 |
| 513 | iter-288† | `overlay_action_queue.h` — thread-safe non-blocking FIFO + latest-result store. | 1,050,624 B (header only) | `overlay_action_queue_test` 22 |
| 514 | iter-288† | Host **WndProc detour** — `overlay_input.h` 3-gate swallow decision; un-disables non-button controls. | 1,051,136 B (+512) | `overlay_input_test` 29 |
| 515 | iter-288† | `overlay_action_worker.h` — pure drain-loop (`CHECK-FIRST` / `NO-SLEEP-ON-STOP`). | 1,051,136 B (header only) | `overlay_action_worker_test` 20 |
| 516 / 519 | iter-288† | Action-worker **lifecycle** wired into the DLL (`overlay_action_worker.cpp`; `BridgeProbe` promoted to a shared `swfoc_overlay` symbol; `Install`/`Uninstall` start/stop). | 1,063,936 B (+12,800) | (pure layer 20/20 re-run) |
| 520 | iter-288 | **Action buttons LIVE** — Spawn / Make Invuln / Kill `onClick` → `Enqueue` + footer toast (PENDING/LIVE/FAILED). | 1,072,128 B (+8,192) | `overlay_actions_test` 19 re-run |
| 523 | iter-289 | **Recent-actions toolbar** — `overlay_recent_actions.h` 5-slot dedup-promote history; `DispatchAction()` single dispatch path. | 1,080,832 B (+8,704) | `overlay_recent_actions_test` 37 |
| 525 / 526 | iter-290 | **Teleport + Faction Switch** buttons — completes the 5-button per-unit action set; shared `selectedUnitExpr`. | 1,090,048 B (+9,216) | `overlay_actions_test` 27 (8 new) |
| 527 | iter-291 (1/2) | **Per-widget capability-badge catalog** — `overlay_phase3_catalog.h` single source of truth; `RenderPhase3CapabilityTable()` replaces the hand-maintained footer string. | 1,090,048 B (PE-aligned, see note) | `overlay_phase3_catalog_test` 60 |
| **528** | **iter-291 (2/2)** | **This close-out doc + formal Phase 3 close.** | 1,090,048 B (docs-only) | — |

† Spec row iter-288 ("God-mode + Pause buttons") was reshaped at
implementation time. The recovery-heavy loop decomposed the
button-enabling work into four finer atomic units (queue → WndProc →
worker → lifecycle, loop iters 513–519) before the buttons could go
LIVE at iter-520. "God Mode" is delivered by the **Make Invuln**
button. **Pause** was NOT shipped — see Honest defers.

**DLL zero-delta note (iter-527):** `overlay_phase3_catalog.h` was
freshly compiled+linked (timestamp chain `overlay.o` rebuilt →
DLL relinked) but the size stayed 1,090,048 B. This is benign: the PE
file is exactly 512-byte aligned (`1090048 mod 512 = 0`) and the net
code delta (added `RenderPhase3CapabilityTable`, removed two long
footer string literals) is sub-512 B, absorbed inside one alignment
bucket.

## The Phase 3 action pipeline (end to end)

```
button onClick (render thread, inside D3D9 Present detour)
  → BuildXxxCommand(...)              overlay_actions.h        — pure, tested (27)
  → DispatchAction(req)               overlay.cpp glue
       → ActionQueueInstance().Enqueue()  overlay_action_queue.h  — pure, tested (22)
              ... latest result = Pending synchronously — toast updates on the click ...
       → RecentActionsInstance().Record() overlay_recent_actions.h — pure, tested (37)
  → RunActionWorkerLoop drains it     overlay_action_worker.h  — pure, tested (20)
       → BridgeProbe(lua, response)   hud_state.cpp            — blocking named-pipe I/O
              ... latest result = Live / Failed ...
footer toast (render thread, next frame)
  → ActionQueueInstance().LatestResult()  — short lock-guarded copy
capability badge table (render thread, every frame)
  → kPhase3Widgets[]                  overlay_phase3_catalog.h — pure, tested (60)
```

The render thread only ever **enqueues** and **reads the latest
result** — both short lock-guarded in-memory ops. The blocking bridge
round-trip runs on a dedicated background action-worker thread, never
inside the D3D9 Present detour. This is the exact hazard the queue +
worker layers were built to prevent: a slow or stalled bridge can
never freeze the host game's frame loop.

## Acceptance criteria check (spec lines 21–40)

| Criterion | Status |
|---|---|
| HUD remains uncluttered — 4-row Tier 1 + collapsible Tier 2/3 | ✅ Phase 3 widgets render in a **separate** `AlwaysAutoResize` "Actions" window — they never push the always-visible Tier 1 footprint above 4 rows. |
| Recent-actions toolbar — re-execute last 5 SWFOC calls | ✅ iter-523 — 5-slot dedup-promote history, click-to-re-fire. |
| Bridge harness stays clean at 1100/0 | ✅ no bridge wires authored; all 5 action wires pre-existed LIVE. |
| Overlay DLL builds clean every iter | ✅ every overlay iter `build.bat` exit 0, zero diagnostics. |
| Operator-trust badge on every button (guardrail 1007) | ✅ `RenderPhase3CapabilityTable` + footer toast; PENDING flips synchronously on click. |
| God Mode | ✅ delivered as the **Make Invuln** button (`SWFOC_MakeUnitInvulnLua`). |
| Pause button | ⚠️ **honest defer** — see below. |
| F2–F9 hotkeys / drag-drop / click-to-inspect | → out of Phase 3 scope (Phases 4–6). |

## Honest defers (documented, not failures)

- **Pause button NOT shipped.** Spec row iter-288 paired God-mode with
  a Pause button. Pause needs Phase 2 `SetGameSpeed` to be **LIVE** —
  it is currently PHASE 2 PENDING and its promotion is an `editor-100`
  spec concern, not an overlay one. Shipping a Pause button now would
  surface an inert control and violate the operator-trust pattern
  (guardrail 1007). The spec already plans the Pause **hotkey** at
  Phase 6 iter-306 ("if `SetGameSpeed` still PHASE 2 PENDING, document
  defer + ship the hotkey wire-up so it goes LIVE the moment
  editor-100 flips it"). Phase 3 follows that plan: no Pause widget
  until the wire is LIVE.

- **Runtime onClick→bridge→toast path is build-only verifiable.** The
  loop has no live game, so the `onClick → enqueue → drain →
  BridgeProbe → toast` round-trip cannot be asserted end-to-end. Every
  **pure** layer it rests on is unit-tested in isolation (195 checks,
  see below); the ImGui render glue, the `std::thread` spawn/join, and
  the live bridge round-trip remain build-only — exactly as Phases
  1–2 were. This is a property of the test environment, not a Phase 3
  gap.

## Phase 3 pattern lessons

### Lesson #1 — Pure-layer-first: extract every subtle behaviour into a dependency-free header before the DLL glue consumes it

Phase 3 shipped **6 pure headers**, each with its own standalone g++
test exe, *before* the build-only DLL glue (ImGui render, `std::thread`
lifecycle, blocking bridge I/O) consumed it:

| Header | Pins | Checks |
|---|---|---|
| `overlay_actions.h` | Lua-command escaping (inner `"`→`\"`, newline neutralisation) | 27 |
| `overlay_action_queue.h` | thread-safe FIFO order + lock-released-across-send | 22 |
| `overlay_input.h` | 3-gate WndProc swallow decision (no input bleed) | 29 |
| `overlay_action_worker.h` | drain-loop ordering (`CHECK-FIRST`, `NO-SLEEP-ON-STOP`) | 20 |
| `overlay_recent_actions.h` | dedup-promote / no-refire-evict / alias-safe | 37 |
| `overlay_phase3_catalog.h` | widget catalog + wire-matches-builder | 60 |

**Pattern:** the build-only surface (ImGui glue, thread lifecycle,
named-pipe I/O) is unavoidable in an injected DLL with no live-game
test path — but it can be made *thin*. Push every decision with a
subtle correctness rule (escaping, FIFO order, swallow rule, drain
order, dedup) down into a `<windows.h>`-free, ImGui-free header and
test it with a plain g++. What's left in the `.cpp` is glue with no
branching logic worth a test.

### Lesson #2 — A recovery-heavy loop rewards finer atomic decomposition

The spec budgeted Phase 3 at 5 iters (287–291). The actual loop took
~16 (512–528) because interrupted-before-emission recovery consumed
roughly 10 of them (phantom iters 519/525, routing recoveries
521/522/524, etc.). The fine-grained pure-header decomposition turned
out to be a **hedge against exactly that failure mode**: every
phantom-partial recovery (iter-516, iter-519, iter-523, iter-525/526)
could *independently re-verify the on-disk artifact* because each
layer carried its own test exe. A coarser "ship all the plumbing in
one iter" decomposition would have left a half-built DLL with no
granular way to confirm which layers were sound.

**Pattern:** when the execution environment is unreliable (frequent
interruption, fresh-context iterations), decompose into units small
enough that each one's *artifact on disk* is independently
verifiable. The recovery cost of a too-large unit is re-doing it; the
recovery cost of a small, separately-tested unit is just re-running
its test exe.

### Lesson #3 — Catalog-as-single-source-of-truth kills stale-string drift

iter-525 shipped a hand-maintained `"Wires (all LIVE): ..."`
`TextDisabled` footer. iter-527 deleted it and replaced it with
`overlay_phase3_catalog.h` — one `Phase3Widget` entry per button,
feeding **both** the render badge table (`RenderPhase3CapabilityTable`)
**and** the test (`overlay_phase3_catalog_test.cpp`). The test's
`PIN wire-matches-builder` checks even cross-reference each catalogued
wire against the Lua its `overlay_actions.h` builder actually emits —
so a badge cannot lie, and the footer cannot drift.

**Pattern:** this is the iter-380 / iter-388 stale-string-drift rule
family (codified for the editor's XAML headers and tooltips) applied
to **overlay render glue**. Any operator-facing text that enumerates
a set (wires, widgets, statuses) should be generated from a catalog
that also feeds a test — never hand-maintained as a literal.

### Lesson #4 — Operator-trust pattern holds at the overlay layer

`Enqueue()` flips the latest result to `Pending` **synchronously on
the click**, before the worker runs the bridge call — so the footer
toast updates the instant the operator clicks. The Kill button stays
`BeginDisabled` while its hex-address field parses to `0`. Every
button carries a capability badge from the catalog. The operator can
never confuse "I clicked" with "the engine changed state" (guardrail
1007), the same discipline the editor's `CapabilityAwareAction`
enforces tab-side.

**Pattern:** the operator-trust pattern is render-surface-agnostic —
it codified for WPF editor tabs (iters 54–63) and transfers
unchanged to an ImGui in-game overlay. The vocabulary (PENDING / LIVE
/ FAILED, LIVE / PHASE 2 PENDING / LIVE ONLY) is shared.

## Verification gates (ALL GREEN — re-run this iter, evidence-backed per guardrail 1002)

| Gate | Result |
|---|---|
| `overlay_actions_test.exe` | **27 checks, 0 failures**, exit 0 |
| `overlay_action_queue_test.exe` | **22 checks, 0 failures**, exit 0 |
| `overlay_input_test.exe` | **29 checks, 0 failures**, exit 0 |
| `overlay_action_worker_test.exe` | **20 checks, 0 failures**, exit 0 |
| `overlay_recent_actions_test.exe` | **37 checks, 0 failures**, exit 0 |
| `overlay_phase3_catalog_test.exe` | **60 checks, 0 failures**, exit 0 |
| **Phase 3 test total** | **195 checks, 0 failures** across 6 standalone C++ test files |
| Overlay DLL | **1,090,048 B** (unchanged — this is a docs-only iter; `build.bat` last green at iter-527, exit 0) |
| Bridge harness | n/a (no bridge surface touched across Phase 3) — inherits **1100 / 0** |
| Verifier ledger lint | n/a (no ledger changes) — inherits **0 / 0** |
| Editor test suite | n/a (no editor surface touched) — inherits green |

This close-out iter is **pure docs** (one new `knowledge-base/*.md`
file). No source, test, build, bridge, ledger, or editor surface was
touched — the gates above are inherited, and the 195-check Phase 3
suite was re-run from the on-disk test exes this iter as fresh
evidence that the interactive surface the doc certifies is green.

## What's next — Phase 4 (drag-drop tactical spawning, spec iter-292…296)

Per the spec iter-chain:

1. **iter-292** — Phase 4 kickoff: ImGui drag-drop source on the
   unit-type Combo + a 200×200 drop-target child window. On drop →
   `SWFOC_SpawnUnitLua(localPlayer, type, vec3(x, y, 0))`.
2. **iter-293** — Mini-map widget (256×256 top-down quad with unit
   dots; doubles as the drag-drop target).
3. **iter-294** — D3D9 line/circle preview ring at the drop point.
4. **iter-295** — Multi-player safety gate (`SWFOC_GetLocalPlayer`
   slot check; disable drag-drop in galactic-mode transitions).
5. **iter-296** — Phase 4 close-out.

**Phase 4 honest defer (carried from the spec):** the projection
matrix RVA is NOT pinned in `verified_facts.json`. Phase 4 locks to a
**2D Z=0 ground plane** — operator drops a unit-type onto a minimap
region, the click maps to world X/Y, Z is 0 (or an operator slider).
Phase 5 iter-297 is the dedicated RE iter that attempts the
projection-matrix pin; if it blocks, Phase 5 falls back to the
`SWFOC_GetUnitAtScreenCoords` NEW-bridge-wire path.

## Iter 291 close-out summary

- This document is the iter-291 (loop iter-528) deliverable and the
  **formal Phase 3 close**.
- **Code changes this iter:** none — pure docs.
- **Phase 3 cumulative:** 6 new pure headers + 1 new DLL TU + 6
  standalone test files (195 checks); overlay DLL 1,040,384 B →
  1,090,048 B (+49,664 B; +4.78%); **0 new bridge wires**.
- **Interactive "Actions" window** with 5 operator buttons (Spawn /
  Make Invuln / Kill / Teleport / Faction Switch), a 5-slot
  recent-actions toolbar, and a catalog-driven capability-badge
  table — all dispatching through a non-blocking render-thread→worker
  pipeline that never stalls the host D3D9 frame loop.
- All gates GREEN: 195/195 Phase 3 checks; overlay DLL builds clean;
  bridge harness / ledger lint / editor suite inherit unchanged.
- **4 Phase 3 pattern lessons** captured: pure-layer-first
  decomposition; finer atomic units hedge a recovery-heavy loop;
  catalog-as-single-source-of-truth kills stale-string drift;
  operator-trust pattern is render-surface-agnostic.
- **1 honest defer:** the Pause button waits on `SetGameSpeed` going
  LIVE (editor-100 concern) — wired at Phase 6 iter-306, not before.
- **Phase 3 = 100% COMPLETE.** Phase 4 (drag-drop tactical spawning)
  begins next.
