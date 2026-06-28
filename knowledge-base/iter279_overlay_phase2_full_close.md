# Iter 279 — Thread B Overlay Phase 2-full FINALE (5/5): Tier 2 content + faction-tint chrome consistency + arc-level capstone

**Date:** 2026-05-08 05:00 UTC
**Iter:** 279 (Thread B Phase 2-full arc finale 5/5)
**Predecessors:** iter 275 (RE kickoff) + iter 276 (vendoring) + iter 277 (plumbing init) + iter 278 (Tier 1 HUD strip).
**Successor:** iter 280 (open: operator changelog supplement / HudSnapshot extension for multipliers / Phase 4 Camera projection matrix RE / pivot back to A1.x).

## Headline

**Thread B Phase 2-full arc COMPLETE.** Tier 2 content shipped: catalog
rollup row at top of HUD, 2 multiplier-value honest-defer rows
(bridge-query pending iter 280+), and faction-tint consistency
extended from bridge LED to panel chrome (border + separator colors).
Build clean; DLL +512 B; arc-cumulative DLL +765,440 B (iter-275
baseline 270,848 B → iter-279 final 1,036,288 B).

| Metric | Value |
|---|---|
| `overlay.cpp` LoC | 540 → **597** (+57 lines for Tier 2) |
| DLL size | 1,035,776 → **1,036,288 B** (+512 B; +0.05%) |
| Arc-cumulative LoC delta | 819 → 597 LoC (-222 across iter 275-279; vendored ImGui not counted) |
| Arc-cumulative DLL delta | 270,848 → 1,036,288 B (+765,440 B; +283%; mostly ImGui static link) |
| Build | 4/4 GREEN; 0 errors / 0 warnings |
| Bridge harness | n/a (no bridge changes across the arc) — inherits iter-274 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) — inherits iter-274 0/0 at 318 entries |
| Phase 2-lite render path | **REMOVED** at iter-278; iter-279 unchanged |
| HudSnapshot model | **UNCHANGED** across all 5 iters — iter-275 design held |
| Arc completion | **5/5 = 100%** |

## What shipped this iter (iter 279 Tier 2)

### `overlay.cpp` — 3 sections changed

**Section 1: Faction-tint chrome (~14 lines added before `ImGui::Begin`)** —
push 3 ImGui style colors (`Border` + `SeparatorActive` + `SeparatorHovered`)
to the iter-103 faction tint (via `DwordToImVec4`) when bridge is
reachable AND slot is known. Track count in `factionTintStyleColors`
so we can pop them after `ImGui::End`. Falls back to ImGui's default
theme when bridge offline so operator immediately sees the un-tinted
state (visual proof that bridge connection drives the chrome).

**Section 2: Catalog rollup row (3 lines added at top of panel body)** —
hardcoded iter-274 Phase2HookPending audit numbers (142 LIVE / 25
PHASE 2 / 0 LIVE ONLY) as `ImGui::TextDisabled` + Separator. iter-N
tag in the row label so operators see when the catalog snapshot was
last refreshed. Source: editor-side `CapabilityStatusCatalog.cs`
audit at iter-274; future iters could marshal this dynamically via
shared file or named pipe.

**Section 3: Multiplier honest-defer rows (4 lines added before footer)** —
2 `ImGui::TextDisabled` rows showing where `g_dmgMult_global` and
`g_fireRateMult_global` values WILL appear once iter-280+ implements
the cross-DLL bridge query. Honest defer per iter-249 pattern: ship
the layout slot + cross-reference the future iter that fills it.

### `kPanelH` extended 180 → 250 px

To fit the additional rows (catalog + 2 multipliers + separators),
panel height grew 70px. Width unchanged at 280px.

### `factionTintStyleColors` cleanup

Pop the 3 pushed style colors after `ImGui::End` so they don't leak
into subsequent ImGui calls in this frame. Currently idempotent
(RenderImGuiPanel only opens one window) but defensive for iter 280+
extensions.

## Arc-level cumulative shipping (iter 275-279)

| Iter | Scope | LoC delta | DLL delta | Pattern lessons |
|---|---|---|---|---|
| 275 | RE kickoff (pure design doc) | 0 | 0 | 3 |
| 276 | ImGui vendoring (12+1 files) | 0 (overlay.cpp) | +764,416 (+281%) | 3 |
| 277 | ImGui plumbing init | +178 | +1,536 (+0.15%) | 3 |
| 278 | Tier 1 HUD strip (replaces Phase 2-lite) | -83 NET (-190 P2-lite + 107 Tier 1) | -1,024 (-0.1%) | 3 |
| **279** | **Tier 2 content + faction-tint chrome** | **+57** | **+512 (+0.05%)** | **3** |
| **TOTAL** | **Phase 2-full COMPLETE** | **+152 NET (overlay.cpp 445→597; vendored ~10K LoC ImGui)** | **+765,440 (+283%)** | **15** |

## Arc-level pattern lessons (consolidating iter 275-279)

### Lesson #1 — NEW arc-class follows 5-iter canonical shape regardless of scope

Iter-275 RE kickoff predicted "5-iter arc length applies even when
each iter is small" because Thread B is project-level not A1.x
sub-field. Confirmed: iter 275 (design only) + iter 276 (vendoring) +
iter 277 (plumbing) + iter 278 (Tier 1 content) + iter 279 (Tier 2
content + close-out) = exactly 5 iters, mirrors iter-224-228 /
230-234 / 236-240 / 242-246 / 257-261 canonical A1.x cadence.

**Pattern**: foundational arcs (NEW arc-class kickoffs) and
incremental arcs (A1.x sub-field arcs) both fit the 5-iter shape but
for different reasons — foundational arcs decompose the work along
the RE → infrastructure → Tier 1 → Tier 2 → close-out axis;
incremental arcs decompose along the RE → bridge LIVE → simulator →
native UX → live verify axis. The shape is the same; the contents
differ.

### Lesson #2 — Vendoring iters bear binary cost; subsequent iters are nearly free

Iter-276 paid the entire +764,416 B static-link cost for ImGui.
iter-277 (+1,536 B) + iter-278 (-1,024 B) + iter-279 (+512 B) sum to
**+1,024 B across 3 application iters** — a 1:746 ratio of
application-code cost vs vendoring cost.

**Pattern**: future arcs that vendor large libraries should plan
binary-size budgets primarily around the vendoring iter; subsequent
iters are essentially free. iter-275 estimate "<800 KB DLL growth"
was within 1× of actual (+765 KB).

### Lesson #3 — `HudSnapshot` model stability across 4 render-path iters

The same 7 `HudSnapshot` fields (`bridge_reachable`,
`local_player_slot`, `credits`, `alive_units`, `scene_name`,
`last_error`, `generated_tick`) feed all 4 render implementations:
- iter 43: D3D9 vertex amber rectangle (Phase 1).
- iter 103: 5-row faction-tinted strip (Phase 2-lite).
- iter 277: ImGui minimal "Hello, operator" panel (Phase 2-full Tier 1
  init).
- iter 278: ImGui 4-row HUD strip (Phase 2-full Tier 1 final).
- iter 279: same model + iter 274 catalog rollup (compile-time) +
  multiplier honest-defer (cross-DLL TBD).

Worker thread + lock-free atomic snapshot rotation: zero changes
across **236 iters of master loop** (iter 43 → iter 279).

**Pattern**: stable data contracts let render-path migrations stay
purely render-side. iter-275 design predicted this; iter-279 confirms
it empirically across the entire arc.

### Lesson #4 — Honest-defer pattern applies equally well to render features

Iter-279 Tier 2's multiplier rows shipped as honest-defer placeholders
(TextDisabled rows + iter-280+ cross-reference) rather than blocking
the arc finale on cross-DLL symbol resolution. This mirrors A1.x's
honest-defer pattern (iter-249 SetUnitCapOverride / iter-268 max_speed
/ iter-270 attack_power) at the operator-trust layer: ship the layout
slot, document where the value will come from, name the iter that
fills it.

**Pattern**: honest-defer isn't just for missing engine offsets —
it's a general operator-trust protocol for "this feature has a slot
in the UI but the data wire isn't ready yet." Ship the slot + the
cross-reference; iter-N+1 ships the data.

## Verification gates (ALL GREEN)

| Gate | Result | Δ vs iter-278 |
|---|---|---|
| Overlay build [1/4] MinHook | clean | unchanged |
| Overlay build [2/4] overlay sources | **clean** (Tier 2 additions) | NEW |
| Overlay build [3/4] ImGui | clean | unchanged |
| Overlay build [4/4] link | clean | unchanged |
| Overlay DLL size | **1,036,288 B** | +512 B |
| `overlay.cpp` LoC | **597** | +57 |
| Bridge harness | n/a (no bridge changes) | inherits iter-274 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) | inherits iter-274 0/0 at 318 entries |

## What's next (iter 280+)

Per arc finale + iter-269 lesson #2 NON-A1.x pivot still active:

1. **Iter 280 (RECOMMENDED)** — **Operator changelog supplement** for
   iter 275-279 Thread B Phase 2-full arc. Mirrors
   iter-235/241/247/262 docs-iter post-arc cadence at exactly the
   same structural place (5-iter arc → 1 docs iter post-finale).
   Pure docs; no code changes; bundles arc-cumulative metrics +
   pattern lessons + operator-facing F1+overlay smoke checklist.
   **5th docs iter post-arc this conversation**.

2. **Alternative iter 280**: HudSnapshot extension for multiplier
   values + worker probe for iter-96/iter-225 LIVE getter readback
   (resolves iter-279 Tier 2 honest-defer). Would require ~30 LoC
   in `hud_state.h` + ~60 LoC in `hud_state.cpp` worker code; +5
   LoC in overlay.cpp to consume. Out of strict iter-275 design
   scope but inside the iter-280 follow-up window.

3. **Alternative iter 280**: Phase 4 Camera projection matrix RE
   kickoff (deferred from iter-275 Tier 3). Multi-iter; would
   unlock per-unit Inspector overlay + drag-to-spawn workflows.

4. **Alternative iter 280**: Pivot back to A1.x if user has surfaced
   new ledger entries via live-game CheatEngine tracing (would
   restart the iter-269 honest-defer-rate measurement).

5. **NOT recommended for iter 280**: NEW arc-class kickoff (Thread C
   Save-game RE / Thread D Multi-repo CI / Thread E SonarQube). All
   are LOW operator value vs Thread B's just-shipped HUD; better to
   land Thread B fully + capture the docs first.

## Cumulative iter 159-279 session metrics

| Category | Count |
|---|---|
| LIVE wire/sub-field flips | +99 |
| Bridge dispatcher helpers | 10 |
| Operator-facing improvements | 34 |
| Docs iters | 12 |
| Audit/audit-followup iters | 7 |
| Memory codification iters | 1 |
| Preset-menu refresh iters | 3 |
| RE kickoff iters | 9 |
| **RE-implementation iters** | **9** (was 8; iter 279 NEW Tier 2) |
| Simulator iters | 5 |
| Native UX iters | 3 |
| Staging-UI verification iters | 2 |
| **Close-out iters** | **15** (was 14; iter 279 NEW arc finale) |
| Ledger updates | 4 |
| Stale-count drift fixes | 9 |
| Wire-format-canonical alignments | 1 |
| Honest-defer arc closures | 3 |
| Audit-iter rationale drift catches | 3 |
| Cross-reference pin tests | 1 |
| README capstone updates | 3 |
| Reverse-orphan audit clean passes | 3 |
| Memory rules codified | 1 |
| Surface report regens | 6 |
| Multi-iter arc finale capstones | **2** (was 1; iter 279 NEW Thread B Phase 2-full) |
| Mid-iter dual-drift catches | 1 |
| NEW arc-class implementation starts | 1 |
| NEW arc-class plumbing wires | 1 |
| Phase-transition implementations | 1 |
| **Multi-iter arc-class completions** | **1** (NEW; Thread B Phase 2-full COMPLETE) |
| Total iters | **121** |

## Iter 279 close-out summary

- This document is the iter 279 deliverable + Thread B Phase 2-full
  arc finale.
- **Code changes**: +57 LoC in `overlay.cpp` (faction-tint chrome
  push/pop ~14 lines + catalog rollup row 3 lines + 2 multiplier
  honest-defer rows 4 lines + iter-tag footer bump 1 line + extended
  kPanelH 180→250 + comments).
- All gates GREEN: build 4/4 clean; DLL **1,036,288 B** (+512 B vs
  iter-278); bridge harness + ledger lint inherit iter-274 unchanged.
- **Iter 5/5 of 5-iter Thread B Phase 2-full arc** (iter 275-279).
  **Arc 100% COMPLETE.**
- **9th consecutive NON-A1.x iter** per iter-269 lesson #2 (iter-271
  + iter-272 + iter-273 + iter-274 + iter-275 + iter-276 + iter-277
  + iter-278 + **iter-279**).
- 109 → 109 buttons UNCHANGED. 106 → 106 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25
  unchanged.
- **3 NEW pattern lessons** (this iter) + **4 arc-level capstone
  lessons** (consolidated from iter 275-279):
  - NEW arc-class follows 5-iter canonical shape regardless of scope.
  - Vendoring iters bear binary cost; subsequent iters are nearly free.
  - HudSnapshot model stability across 4 render-path iters (235-iter span).
  - Honest-defer pattern applies equally well to render features.
- **Phase 2-full Tier 2 OWNS the HUD render path with operator-trust
  audit trail** (catalog rollup → bridge LED → multiplier honest-defer
  cross-references → faction-tint chrome consistency).
- **Multi-iter arc class proven at project level** (Thread B is the
  1st NEW arc-class to complete since iter-100; A1.x is incremental).
