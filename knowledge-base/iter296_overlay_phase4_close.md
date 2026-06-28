# Iter 296 — Overlay Interactive Phase 4 close-out: drag-drop tactical spawning COMPLETE

**Date:** 2026-05-21
**Spec:** `.ralph/specs/overlay-interactive.md` — Phase 4 (drag-drop tactical spawning), spec iter-chain rows iter-292…iter-296.
**Hat:** `overlay-interactive`
**Actual loop iters:** 529–535 (the spec budgeted 5 iters 292–296; the loop took 7, with loop iter-534 a `task.resume` routing/recovery iter — see Pattern lesson #2). The doc filename keeps the **spec** iter number (296) per the established doc-filename/iter-drift convention (cf. `iter291_overlay_phase3_close.md`).
**Predecessor:** `iter291_overlay_phase3_close.md` (Phase 3 interactive-widget surface close).
**Successor:** Phase 5 — click-to-inspect + 3D cursor (spec iter-297…303).

## Headline

**Overlay Interactive Phase 4 COMPLETE.** The overlay went from the
Phase 3 interactive "Actions" window to a **drag-drop tactical
spawning surface**: the operator drags a unit-type out of the
in-overlay Combo and drops it onto a 200×200 spawn pad **or** a
256×256 top-down tactical minimap; the drop projects to 2D world
X/Y at Z=0, fires `SWFOC_SpawnUnitLua`, draws a faction-tinted
pulsing preview ring at the drop point, and records a spawn-marker
dot on the minimap. A multi-player safety gate disarms the whole
surface (drag source + both drop targets) and shows a red
**"Tactical mode only"** badge whenever the local-player slot is -1
(galactic-mode transition). **No new bridge wires** —
`SWFOC_SpawnUnitLua` pre-existed LIVE and the `local_player_slot`
gate field was already in `HudSnapshot`; the bridge harness inherits
1100/0 unchanged.

| Metric | Value |
|---|---|
| Phase 4 deliverable | Drag-drop spawning — unit-type drag source + 200×200 spawn pad + 256×256 minimap drop target + faction-tinted preview ring + multi-player safety gate |
| New pure headers | **4** (`overlay_dragdrop.h`, `overlay_minimap.h`, `overlay_preview_ring.h`, `overlay_spawn_gate.h`) |
| New DLL translation units | **0** — all four kernels are header-only; `overlay.cpp` consumed them |
| New standalone C++ test files | **5** — **405 checks, 0 failures** (see Verification gates) |
| New bridge wires | **0** — `SWFOC_SpawnUnitLua` pre-existed LIVE; `local_player_slot` already in `HudSnapshot` |
| Overlay DLL size | 1,090,048 B (Phase 4 entry) → **1,094,656 B** (+4,608 B; +0.42%) |
| Bridge harness | n/a (no bridge surface touched) — inherits **1100 / 0** |
| Verifier ledger lint | n/a (no ledger changes) — inherits **0 / 0** |
| Editor test suite | n/a (no editor surface touched) — inherits green |
| Build | every Phase 4 overlay iter `build.bat` exit 0, zero compiler diagnostics |
| Spec acceptance | Phase 4 criteria met (see Acceptance check); 3 honest defers (projection-matrix RVA, live-engine minimap dots, build-only runtime path) |
| Arc completion | **Phase 4 = 100% COMPLETE** |

## What shipped across Phase 4 (iter chain)

| Loop iter | Spec row | Scope | DLL size (delta) | Tests |
|---|---|---|---|---|
| 529 | iter-292 | **Phase 4 kickoff** — `overlay_dragdrop.h` (`PackUnitTypePayload` + `DropPadToWorld`); drag-drop SOURCE on the unit-type Combo; `RenderSpawnPad` 200×200 bordered drop-target child. | 1,090,048 → 1,092,096 B (+2,048) | `overlay_dragdrop_test` 65 |
| 530 | iter-293 | **256×256 tactical minimap** — `overlay_minimap.h` (`WorldToMinimap` / `MinimapToWorld` / `SpawnMarkerRing`); `RenderMinimap` draw-list grid + spawn-marker dots; `DispatchSpawnDrop` shared helper extracted at the second drop site. | 1,092,096 → 1,093,632 B (+1,536) | `overlay_minimap_test` 135 |
| 531 | iter-294 | **Faction-tinted preview ring** — `overlay_preview_ring.h` (`PreviewRingColor` / `FramePhase01` / `PreviewRingRadius`); `DrawPreviewRing` foreground-draw-list glue; drop handlers switched to `AcceptBeforeDelivery` + `IsDelivery()` branch. | 1,093,632 → 1,094,144 B (+512) | `overlay_preview_ring_test` 74 |
| 532 | iter-295 | **Multi-player safety gate** — `overlay_spawn_gate.h` (`EvaluateSpawnGate` / `SpawnGateAllowsSpawn` / `SpawnGateBadgeText`); `RenderSpawnGateBadge`; gate threaded through 3 layers (drag source disarmed, both drop targets withheld, red badge). | 1,094,144 → 1,094,656 B (+512) | `overlay_spawn_gate_test` 53 |
| 533 | iter-296 (1/2) | **Phase 4 close-out integration test** — `overlay_phase4_close_test.cpp` wires all 4 kernels + `BuildSpawnUnitCommand` together; `SimulatePhase4Drop` models the `overlay.cpp` decision sequence. | 1,094,656 B (test-only; DLL untouched) | `overlay_phase4_close_test` 78 |
| **535** | **iter-296 (2/2)** | **This close-out doc + formal Phase 4 close.** | 1,094,656 B (docs-only) | — |

Loop iter-534 was a `task.resume` routing/recovery iter (master loop
resumed after interruption, Ralph re-emitted `overlay.start`) — it
shipped no artifact and is not a spec row.

## The Phase 4 drag-drop spawn pipeline (end to end)

```
drag the unit-type Combo (render thread, inside D3D9 Present detour)
  → PackUnitTypePayload(name, buf)        overlay_dragdrop.h     — pure, tested (65)
  → SetDragDropPayload(kUnitTypePayloadId, buf)   overlay.cpp glue

  ... multi-player safety gate evaluated ONCE at RenderActionsWindow top ...
  → EvaluateSpawnGate(snap.local_player_slot)     overlay_spawn_gate.h  — pure, tested (53)
       slot -1  → drag source disarmed, both targets withheld, red badge
       slot 0-7 → surface armed, green badge

drop on the 200×200 pad      → DropPadToWorld(px,py)    overlay_dragdrop.h — pure, tested (65)
drop on the 256×256 minimap  → MinimapToWorld(px,py)    overlay_minimap.h  — pure, tested (135)
                                  (delegates to DropPadToWorld — single projection)
  → DispatchSpawnDrop(faction, type, SpawnDrop)   overlay.cpp glue
       → BuildSpawnUnitCommand(...)               overlay_actions.h  — pure, tested
       → DispatchAction → enqueue → worker → BridgeProbe → SWFOC_SpawnUnitLua
       → MarkerRingInstance().Push(world)         overlay_minimap.h  — pure, tested (135)

hover frame (AcceptBeforeDelivery, !IsDelivery)
  → DrawPreviewRing(cursor, factionIndex)         overlay_preview_ring.h — pure, tested (74)
       → FramePhase01 → PreviewRingRadius → PreviewRingColor → AddCircle

minimap render (every frame)
  → one AddCircleFilled per SpawnMarkerRing entry via WorldToMinimap
```

The four coordinate/animation/gate kernels are all **pure,
dependency-free headers** with their own g++ test exe. What is left in
`overlay.cpp` is ImGui drag-drop render glue with no branching logic
worth a test. The blocking bridge round-trip still runs on the Phase 3
background action-worker thread — drag-drop never blocks the host
D3D9 frame loop.

## Acceptance criteria check (spec lines 21–40)

| Criterion | Status |
|---|---|
| Drag-drop unit spawning — drag from in-overlay unit-type list → drop on minimap or world → `SWFOC_SpawnUnitLua` with converted coords; **2D Z=0 plane interim** | ✅ drag source on the unit-type Combo; **two** drop targets (200×200 pad + 256×256 minimap), both routing through `DispatchSpawnDrop` → `BuildSpawnUnitCommand` → `SWFOC_SpawnUnitLua` at Z=0. Build-only verifiable (no live game). |
| Mini-map — top-down 256×256 quad with unit dots; doubles as the drag-drop target | ✅ iter-293 — 256×256 draw-list grid + crosshair; dots plot the operator's own recent spawn drops (live-engine dots → honest defer, spec iter-302). |
| Bridge harness stays clean at 1100/0 even when overlay calls new wires | ✅ no bridge wires authored; `SWFOC_SpawnUnitLua` pre-existed LIVE; bridge harness inherits **1100/0**. |
| Overlay DLL builds clean every iter; record byte size | ✅ every Phase 4 iter `build.bat` exit 0, zero diagnostics; DLL 1,090,048 → 1,094,656 B (per-iter sizes logged above + in `ralph_loop_state.md`). |
| HUD remains uncluttered — 4-row Tier 1 + collapsible Tier 2/3 | ✅ the spawn pad + minimap render **inside** the separate Phase 3 "Actions" window — they never push the always-visible Tier 1 footprint above 4 rows. |
| Operator-trust badge surfaced (guardrail 1007) | ✅ `RenderSpawnGateBadge` shows green "Drag-drop spawn LIVE" / red "Tactical mode only" — the operator can never confuse "the surface is armed" with "spawning is safe right now". |
| Projection-matrix RVA → 2D Z=0 interim | ✅ honest defer (see below) — Phase 4 locks to a Z=0 ground plane exactly as the spec planned. |
| Click-to-inspect / F-key hotkeys | → out of Phase 4 scope (Phases 5–6). |

## Honest defers (documented, not failures)

- **Projection-matrix RVA NOT pinned** in `verified_facts.json` (per
  `overlay_ux_research_2026-05-08.md` agent #2 finding). Phase 4
  delivers its full drag-drop surface anyway by **locking to a 2D Z=0
  ground plane**: the operator drops a unit-type onto the pad or the
  minimap, the drop pixel maps to world X/Y, Z is 0 (ground level).
  This is the spec-planned interim (spec lines 32, 50, 72). Phase 5
  iter-297 is the dedicated RE iter that attempts the projection-matrix
  pin; if it blocks, Phase 5 falls back to the
  `SWFOC_GetUnitAtScreenCoords` NEW-bridge-wire path. A blocked
  dependency **narrowed scope; it did not block the phase**.

- **Live ENGINE-unit dots on the minimap NOT shown.** The 256×256
  minimap (iter-293) plots the operator's **own** recent spawn drops
  — real data the overlay already holds in `SpawnMarkerRing`. Live
  positions of all engine units from `SWFOC_EnumerateUnits` need
  per-unit positions appended to `HudSnapshot`, which is **spec
  iter-302** (Phase 5, append-only snapshot extension). Plotting the
  operator's own spawns is honest real data; faking engine-unit dots
  would not be. Deferred at iter-530, unblocked by iter-302.

- **Runtime drag-drop → spawn path is build-only verifiable.** The
  loop has no live game, so the `drag → SetDragDropPayload → drop →
  Accept → DispatchSpawnDrop → BridgeProbe → SWFOC_SpawnUnitLua`
  round-trip cannot be asserted end-to-end. Every **pure** kernel it
  rests on is unit-tested in isolation (327 checks) and wired together
  in the integration fixture (78 checks); the ImGui drag-drop render
  glue (`BeginDragDropSource`/`Target`, the `AcceptBeforeDelivery` +
  `IsDelivery()` branch) and the live bridge round-trip remain
  build-only — exactly as Phases 1–3 were. This is a property of the
  test environment, not a Phase 4 gap.

## Spec deviations (documented at implementation time)

- **iter-292** — the spec sketched a per-name drag-drop type-id
  `unit_type_<NAME>`. A drop target accepting *any* unit-type cannot
  match a per-name id, so a **fixed** type-id `SWFOC_UNIT_TYPE`
  carries the unit name in the payload **data** (the working ImGui
  drag-drop idiom). Behaviour is identical; the id is just stable.

- **iter-293** — the spec sketched an `ImGui::Image` quad for the
  minimap. With no pinned heightmap RVA there is no map texture, so a
  **draw-list grid + crosshair** is the working interim idiom.

- **iter-296** — the spec wrote `Iter296Phase4DragDropTests.cs`. The
  `.cs` name predates the overlay's all-C++ native-exe test infra (a
  C# test cannot exercise a C++ header). `overlay_phase4_close_test.cpp`
  **is** the spec iter-296 close-out test in the established pattern —
  same convention as the Phase 3 close-out (`iter291`'s naming note).

## Phase 4 pattern lessons

### Lesson #1 — Pure-kernel-first holds across a new problem domain

Phase 3 Lesson #1 (extract every subtle behaviour into a
dependency-free header before the DLL glue consumes it) was proven on
**Lua-command escaping**. Phase 4 proved it again on an entirely
different domain — **coordinate projection**:

| Header | Pins | Checks |
|---|---|---|
| `overlay_dragdrop.h` | pad pixel → world X/Y (center→origin, screen-up=north, off-pad clamp); payload pack no-silent-truncation | 65 |
| `overlay_minimap.h` | world ↔ minimap projection + W2M↔M2W round-trip identity; `SpawnMarkerRing` FIFO/evict | 135 |
| `overlay_preview_ring.h` | faction-tint mapping; frame→phase modulo-wrap; triangle-wave radius no-negative clamp | 74 |
| `overlay_spawn_gate.h` | slot → 3-state gate classification; exact "Tactical mode only" badge phrase | 53 |

**Pattern:** the pure-layer-first decomposition is **domain-agnostic**.
Whether the subtle logic is string escaping (Phase 3) or coordinate
math, FIFO ordering, animation phase, and gate classification
(Phase 4), it belongs in an `<windows.h>`-free, ImGui-free header with
a plain g++ test. The build-only ImGui surface stays thin.

### Lesson #2 — A clean loop confirms the finer-decomposition hedge is cheap

Phase 3 took ~16 loop iters for a 5-iter spec budget (recovery-heavy).
Phase 4 took **7** (529–535) for the same 5-iter budget — only one
non-spec iter (iter-534, a `task.resume` routing recovery). The
per-kernel decomposition that hedged Phase 3's chaos cost **nothing
extra** when the loop ran clean: 4 kernels + 4 isolation tests + 1
integration test is the natural shape of the work, not overhead bought
for recovery insurance.

**Pattern:** finer atomic decomposition is not a tax you pay only when
the loop is unreliable — it is the correct shape regardless. When the
loop is clean it just completes faster; when it is chaotic each
small, separately-tested artifact is independently recoverable. There
is no decomposition trade-off to manage.

### Lesson #3 — Shared-kernel delegation makes coordinate maps un-divergent

`overlay_minimap.h::MinimapToWorld` does **not** re-derive the
world projection — it delegates to
`overlay_dragdrop.h::DropPadToWorld`. So the 200×200 pad and the
256×256 minimap **cannot** mathematically disagree on where world
origin is. The close-out integration test pins this directly:
`invariant: pad and minimap agree on the map center`. This is the
extract-on-second-use rule (the editor's codified iter-316 pattern,
and `DispatchSpawnDrop` extracted at the second drop site in iter-530)
applied to **coordinate kernels**: when a second consumer needs the
same projection, delegate to the first kernel rather than copying the
math.

**Pattern:** two surfaces that must agree on a transform should share
**one** kernel, not two kernels that "should" produce the same
numbers. Divergence you have to test for is divergence you can
eliminate by construction.

### Lesson #4 — The integration test earns its keep at the seams

The four isolation tests (327 checks) each prove **one kernel** correct
in a vacuum. The close-out integration test
(`overlay_phase4_close_test.cpp`, 78 checks) wires all four kernels +
`overlay_actions.h::BuildSpawnUnitCommand` **together** and exercises
the **seams** no isolation test can see: gate → drag source → drop →
`BuildSpawnUnitCommand` → spawn → marker → ring; world↔minimap
round-trips through the *live* `SpawnMarkerRing`; the faction tint
flowing from the drag payload all the way to the preview-ring colour.
Its `SimulatePhase4Drop` fixture is a faithful in-memory model of the
`overlay.cpp` `RenderActionsWindow` decision sequence, so the
build-only ImGui glue has a tested behavioural twin.

**Pattern:** isolation tests and an integration test are not
redundant — they cover orthogonal failure modes. A kernel can be
individually perfect and still be wired wrong. The close-out
integration test is where "each piece works" becomes "the pipeline
works", and it is the right place to spend a close-out iter's test
budget.

## Verification gates (ALL GREEN — re-run this iter, evidence-backed per guardrail 1002)

| Gate | Result |
|---|---|
| `overlay_dragdrop_test.exe` | **65 checks, 0 failures**, exit 0 |
| `overlay_minimap_test.exe` | **135 checks, 0 failures**, exit 0 |
| `overlay_preview_ring_test.exe` | **74 checks, 0 failures**, exit 0 |
| `overlay_spawn_gate_test.exe` | **53 checks, 0 failures**, exit 0 |
| `overlay_phase4_close_test.exe` | **78 checks, 0 failures**, exit 0 |
| **Phase 4 test total** | **405 checks, 0 failures** across 5 standalone C++ test files |
| Overlay DLL | **1,094,656 B** (unchanged — this is a docs-only iter; `build.bat` last green at iter-532, exit 0) |
| Bridge harness | n/a (no bridge surface touched across Phase 4) — inherits **1100 / 0** |
| Verifier ledger lint | n/a (no ledger changes) — inherits **0 / 0** |
| Editor test suite | n/a (no editor surface touched) — inherits green |

This close-out iter is **pure docs** (one new `knowledge-base/*.md`
file). No source, test, build, bridge, ledger, or editor surface was
touched — the gates above are inherited, and the 405-check Phase 4
suite was re-run from the on-disk test exes this iter as fresh
evidence that the drag-drop spawning surface the doc certifies is
green.

## What's next — Phase 5 (click-to-inspect + 3D cursor, spec iter-297…303)

Per the spec iter-chain:

1. **iter-297** — Phase 5 kickoff: **projection-matrix RE pass**.
   Dedicated RE iter to pin the projection-matrix RVA via IDA
   Hex-Rays + Ghidra cross-tool; update `verified_facts.json` with
   2-tool consensus. If RE blocks, document in
   `knowledge-base/blocked_items_<date>.md` and pivot Phase 5 to the
   `SWFOC_GetUnitAtScreenCoords` NEW-bridge-wire path.
2. **iter-298** — cursor → world-space conversion (cursor + viewport
   + view matrix `CameraClass+0x40` + projection matrix → world-ray).
3. **iter-299** — cursor-hit-unit detection (client-side raycasting
   proxy: per-unit AABB-vs-ray test).
4. **iter-300** — click-to-inspect overlay panel (hull / shield /
   owner / type / position; read-only).
5. **iter-301** — inspector action buttons (Kill / Heal / Teleport /
   SwapOwner / MakeInvuln — all 5 already LIVE, no new bridge wires).
6. **iter-302** — unit-AABB extension to `HudSnapshot` (append-only;
   **also unblocks the live-engine minimap dots deferred at
   iter-293**).
7. **iter-303** — Phase 5 close-out.

**Phase 5 honest defer (carried from the spec):** cursor-hit-unit
detection has no exposed engine wire (`overlay_ux_research` CRITICAL
GAP 1). Mitigation A is the client-side raycasting proxy (iter-298 +
iter-299) over visible-unit AABBs from the extended `HudSnapshot`
(iter-302) — no new bridge wire. Mitigation B is a NEW
`SWFOC_GetUnitAtScreenCoords` wire if Mitigation A's accuracy is
unacceptable.

## Iter 296 close-out summary

- This document is the iter-296 (loop iter-535) deliverable and the
  **formal Phase 4 close**.
- **Code changes this iter:** none — pure docs.
- **Phase 4 cumulative:** 4 new pure headers + 0 new DLL TUs + 5
  standalone test files (405 checks); overlay DLL 1,090,048 B →
  1,094,656 B (+4,608 B; +0.42%); **0 new bridge wires**.
- **Drag-drop tactical spawning surface**: drag a unit-type out of
  the overlay → drop on a 200×200 spawn pad or a 256×256 minimap →
  `SWFOC_SpawnUnitLua` at 2D world X/Y (Z=0) → faction-tinted pulsing
  preview ring + minimap spawn-marker dot. A multi-player safety gate
  disarms the whole surface and shows a red "Tactical mode only"
  badge whenever the local-player slot is -1.
- All gates GREEN: 405/405 Phase 4 checks; overlay DLL builds clean;
  bridge harness / ledger lint / editor suite inherit unchanged.
- **4 Phase 4 pattern lessons** captured: pure-kernel-first holds
  across problem domains; finer decomposition is the correct shape,
  not a recovery tax; shared-kernel delegation makes coordinate maps
  un-divergent; isolation + integration tests cover orthogonal
  failure modes.
- **3 honest defers:** projection-matrix RVA (Phase 4 ships 2D Z=0
  interim, Phase 5 iter-297 attempts the pin); live-engine minimap
  dots (Phase 5 iter-302 snapshot extension); build-only runtime path
  (no live game — every pure layer is tested).
- **Phase 4 = 100% COMPLETE.** Phase 5 (click-to-inspect + 3D cursor)
  begins next.
