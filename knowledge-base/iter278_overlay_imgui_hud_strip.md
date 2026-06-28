# Iter 278 — Thread B Overlay Phase 2-full iter 4/5: 4-row HUD strip via ImGui (replaces Phase 2-lite amber rectangle)

**Date:** 2026-05-08 04:00 UTC
**Iter:** 278 (Thread B Phase 2-full arc iter 4/5)
**Predecessor:** iter 277 ImGui plumbing init (`overlay.cpp` 623 LoC; DLL 1,036,800 B).
**Successor:** iter 279 (Tier 2 content: catalog rollup + multipliers + faction-tinting + live verify + close-out).

## Headline

**Phase 2-lite amber rectangle REMOVED. ImGui Tier 1 HUD owns the
render path.** RenderImGuiPanel now renders a 4-row HUD strip via
ImGui Text + ProgressBar widgets consuming the existing `HudSnapshot`
model unchanged. DrawVisibleBadge + ScreenVertex + HudBar + 5
Build*Bar helpers all deleted (~190 lines of D3D9 raw-vertex math
gone). Net binary impact: **DLL shrank 1,024 B**.

| Metric | Value |
|---|---|
| `overlay.cpp` LoC | 623 → **540** (-83 lines NET; -190 Phase 2-lite + ~110 ImGui Tier 1 + 3 helpers + comments) |
| DLL size | 1,036,800 → **1,035,776 B** (-1,024 B; -0.1%) |
| Build | 4/4 GREEN; 0 errors / 0 warnings |
| Phase 2-lite vertex render path | **REMOVED** (DrawVisibleBadge + ScreenVertex + kFvfScreen + HudBar + 5 Build*Bar helpers all deleted) |
| FactionTintForSlot | **PRESERVED** (reused by ImGui panel via DwordToImVec4 helper) |
| HUD layout | 5 ImGui rows: Bridge LED + Credits ProgressBar + Units ProgressBar + Scene + Last Error (when present) |
| Panel position | bottom-right; 280×180px; 12px margin; 78% alpha background |
| Bridge harness | n/a (no bridge changes) — inherits iter-277 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) — inherits iter-277 0/0 at 318 entries |

## What shipped

### `overlay.cpp` — 4 sections changed

**Section 1: Includes** — added `<algorithm>` for `std::min` used in
ProgressBar ratio clamps:
```cpp
#include <algorithm>  // iter 278: std::min for ProgressBar ratio clamps
```

**Section 2: HUD support helpers (~190 → 38 lines)** — deleted:
- `struct ScreenVertex` — pre-transformed-vertex format (XYZRHW + DIFFUSE).
- `constexpr DWORD kFvfScreen` — D3DFVF_XYZRHW | D3DFVF_DIFFUSE.
- `struct HudBar` — fill ratio + color_full + color_empty.
- `BuildBridgeBar` / `BuildCreditsBar` / `BuildUnitsBar` /
  `BuildSceneBar` / `BuildLastErrorBar` — 5 helpers translating
  `HudSnapshot` fields to HudBar fill/color.
- `DrawVisibleBadge(IDirect3DDevice9*)` — 90-line D3D9 render
  function with vertex-list build + render-state save/set/restore +
  `DrawPrimitiveUP` call.

**Kept**:
- `FactionTintForSlot(int)` — REBEL amber / EMPIRE chrome /
  UNDERWORLD sand+rust palette (reused by ImGui panel for bridge-LED
  row tinting).

**Added**:
- `DwordToImVec4(DWORD argb)` — converts iter-103 AARRGGBB color
  packing to ImGui ImVec4 RGBA so faction-tint colors port cleanly
  to ImGui consumption.

**Section 3: HookedPresent** — removed the `if (g_visible) DrawVisibleBadge(dev);`
block (4 lines + 8 lines of comments). `RenderImGuiPanel()` now
owns the entire HUD render path.

**Section 4: RenderImGuiPanel body** — replaced the iter-277
"Hello, operator" minimal panel with the full 4-row HUD strip:

```cpp
// Row 0: Bridge LED — faction-tinted ImGui::Text when reachable, red when not
if (snap.bridge_reachable) {
    const ImVec4 tint = DwordToImVec4(FactionTintForSlot(snap.local_player_slot));
    ImGui::PushStyleColor(ImGuiCol_Text, tint);
    ImGui::Text("Bridge: ONLINE");
    ImGui::PopStyleColor();
    ImGui::SameLine();
    ImGui::TextDisabled("(slot %d)", snap.local_player_slot);
} else {
    ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(0.85f, 0.20f, 0.13f, 1.0f));
    ImGui::Text("Bridge: OFFLINE");
    ImGui::PopStyleColor();
}
ImGui::Separator();

// Row 1: Credits — Text + ProgressBar 0..1M with numeric label
if (snap.credits >= 0) {
    const float ratio = static_cast<float>(std::min<double>(1.0,
        static_cast<double>(snap.credits) / 1'000'000.0));
    char label[64];
    std::snprintf(label, sizeof(label), "%lld cr", static_cast<long long>(snap.credits));
    ImGui::Text("Credits");
    ImGui::ProgressBar(ratio, ImVec2(-1.0f, 0.0f), label);
} else {
    ImGui::TextDisabled("Credits: unknown");
}

// Row 2: Units — same pattern, 0..200 scale
// Row 3: Scene name — Text or TextDisabled
// Row 4: Last error — Separator + colored TextWrapped (only when present)
// Footer: F1 toggles | Phase 2-full @ iter 278
```

Panel sizing: 280×180px (was 220×90px in iter-277 minimal panel) to
fit all 5 rows + footer. Position: bottom-right with 12px margin
(replaces iter-277's "above amber strip" offset since amber strip is
gone). Background alpha: 78% (was 70% in iter-277; slightly more
opaque for HUD readability).

### `swfoc_overlay.dll` size delta

| Build | DLL bytes | Delta from prior | Note |
|---|---|---|---|
| iter 275 (Phase 2-lite baseline) | 270,848 | — | pre-ImGui |
| iter 276 (ImGui vendored) | 1,035,264 | +764,416 (+281%) | static link of all ImGui |
| iter 277 (ImGui plumbing init) | 1,036,800 | +1,536 (+0.15%) | call-site additions |
| **iter 278 (Phase 2-lite removed; ImGui Tier 1 HUD)** | **1,035,776** | **-1,024 (-0.1%)** | net binary win |

The -1,024 B delta confirms the ~190 lines of Phase 2-lite vertex math
saved more bytes than the ~110 lines of ImGui Tier 1 added. **Pattern**:
ImGui's widget primitives are more compact than hand-rolled D3D9 vertex
lists when both are static-linked.

## Verification gates (ALL GREEN)

| Gate | Result | Δ vs iter-277 |
|---|---|---|
| Overlay build [1/4] MinHook | clean | unchanged |
| Overlay build [2/4] overlay sources | **clean** (added `<algorithm>` + new ImGui Tier 1 layout) | NEW Tier 1 content |
| Overlay build [3/4] ImGui | clean | unchanged |
| Overlay build [4/4] link | clean | unchanged |
| Overlay DLL size | **1,035,776 B** | -1,024 B (Phase 2-lite removal won net) |
| `overlay.cpp` LoC | **540** | -83 (deleted 190 Phase 2-lite + added 107 Tier 1) |
| Bridge harness | n/a (no bridge changes) | inherits iter-277 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) | inherits iter-277 0/0 at 318 entries |

## Pattern lessons

### Lesson #1 — ImGui widgets are denser than hand-rolled D3D9

iter-43 → iter-103 Phase 2-lite shipped 5 rows of HUD via ~190 lines
of `DrawPrimitiveUP` + 60 vertices + render-state save/restore. iter-278
ships 5 rows of HUD via ~107 lines of ImGui Text/ProgressBar/Separator
calls. Net code reduction: ~83 LoC (-44%). Net binary reduction:
-1,024 B (-0.1% of total DLL).

**Pattern**: when a vendored UI library replaces hand-rolled rendering,
expect both LoC and binary size to drop, even though the library itself
adds binary cost. The static-link cost is paid at vendoring time
(iter-276); subsequent iters that USE the library are net wins.

### Lesson #2 — Color-format helpers bridge ecosystems cleanly

iter-103's `0xCCAARRGGBB` packing convention (alpha 80% + R + G + B)
was native to D3D9's vertex color format. iter-278 added `DwordToImVec4`
to convert to ImGui's `ImVec4(r, g, b, a)` 0..1 range. This 5-line
helper preserves the iter-103 faction-tint palette without rewriting
it for ImGui — operators see the same REBEL amber / EMPIRE chrome /
UNDERWORLD sand+rust colors in Phase 2-full as they did in Phase 2-lite.

**Pattern**: when migrating render paths between graphics ecosystems,
add a single conversion helper (DwordToImVec4 in this case) rather than
duplicating the color palette in both formats. Single source of truth
for color values; format conversion at the call site.

### Lesson #3 — `HudSnapshot` model stability paid off across 4 iters

`hud_state.h`'s `HudSnapshot` struct hasn't changed across iter 43
(Phase 2-lite vertex render) → iter 103 (Phase 2-lite 5-row faction
tint) → iter 277 (ImGui plumbing minimal panel) → iter 278 (ImGui
Tier 1 HUD strip). The same 7 fields (`bridge_reachable`,
`local_player_slot`, `credits`, `alive_units`, `scene_name`,
`last_error`, `generated_tick`) feed all 4 render implementations.

**Pattern**: stable data contracts let render-path migrations stay
*purely* render-side. iter-275 design doc predicted this; iter-278
confirms it empirically. Worker thread + lock-free snapshot rotation
unchanged across the entire Phase 2-lite → Phase 2-full transition.

## What's next (iter 279)

Iter 279 is the **5/5 finale** of the Thread B Phase 2-full arc:

1. **Tier 2 content additions** to RenderImGuiPanel:
   - **PHASE 2 PENDING badge counts** at top of panel: surfaces
     editor-side capability rollup ("Catalog: 53 LIVE / 37 PHASE 2
     / 3 LIVE ONLY"). Mirrors editor bottom status bar. Source: TBD —
     either include `CapabilityStatusCatalog` data via build-time JSON
     bake-in, OR query bridge for runtime catalog state.
   - **Active SetDamageMultiplierGlobal / SetFireRateMultiplierGlobal
     multiplier values** below catalog row. Surfaces iter-96/iter-225
     LIVE wire effects in-game so operators see "x2.0 damage" /
     "x0.5 fire-rate" without alt-tabbing to editor. Source:
     `g_dmgMult_global` + `g_fireRateMult_global` in
     `swfoc_lua_bridge/lua_bridge.cpp` — overlay can read them
     in-process since both DLLs share the SWFOC process address space.
   - **Faction tinting consistency**: extend the bridge-LED faction
     tint to other panel chrome (e.g. window border, separator color).
     Pure cosmetic.

2. **Live verify** (operator-facing test):
   - Republish overlay DLL with iter-278+iter-279 changes.
   - Confirm via DebugView: `[swfoc_overlay] ImGui Init OK` log
     appears + frame counter still increments.
   - Confirm visually: F1 toggles 5-row HUD bottom-right; bridge LED
     reflects connection state; ProgressBars track credits + units;
     scene name appears when known; last_error appears in red on probe failures.

3. **Close-out doc** `iter279_overlay_phase2_full_close.md` (~280
   lines, mirrors iter-228/234/240/246/261 5/5 multi-iter arc finale
   structure):
   - Headline: Thread B Phase 2-full arc COMPLETE.
   - Cumulative arc shipping (iter 275-279 deliverables).
   - 4 arc-level pattern lessons consolidating iter-275/276/277/278
     individual lessons.
   - Iter 280+ recommendation (next NON-A1.x: README capstone? Phase
     C save-game RE? Or pivot back to A1.x if user has surfaced new
     ledger entries via live-game CheatEngine tracing?).

## Iter 278 close-out summary

- This document is the iter 278 deliverable.
- **Code changes**: -83 LoC NET in `overlay.cpp` (deleted ~190 Phase
  2-lite vertex render + added ~107 ImGui Tier 1 HUD). 1 include
  added (`<algorithm>`). 8 lines of HookedPresent comments updated.
- All gates GREEN: build 4/4 clean; DLL **1,035,776 B** (-1,024 B vs
  iter-277 net binary win); bridge harness + ledger lint inherit
  iter-277 unchanged.
- **Iter 4 of 5-iter Thread B Phase 2-full arc** (iter 275-279). Arc
  **80% complete**.
- **8th consecutive NON-A1.x iter** per iter-269 lesson #2 (iter-271
  + iter-272 + iter-273 + iter-274 + iter-275 + iter-276 + iter-277
  + **iter-278**).
- 109 → 109 buttons UNCHANGED. 106 → 106 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25
  unchanged.
- **3 NEW pattern lessons**: ImGui widgets denser than hand-rolled
  D3D9 (-83 LoC + -1,024 B); color-format helpers bridge ecosystems
  cleanly; `HudSnapshot` model stability paid off across 4 render-
  path iters.
- **Phase 2-lite REMOVED — Phase 2-full Tier 1 OWNS the render path**
  (DrawVisibleBadge gone; only ImGui-rendered output now).
- **Session-cumulative this conversation (iter 159-278)**: +99 LIVE
  wire/sub-field flips + 10 helpers + 34 operator-facing improvements
  + 12 docs iters + 7 audit/audit-followup iters + 1 memory
  codification iter + 3 preset-menu refresh iters + 9 RE kickoff
  iters + **8 RE-implementation iters** (was 7; iter 278 NEW Thread
  B Tier 1 HUD) + 5 simulator iters + 3 native UX iters + 2
  staging-UI verification iters + **14 close-out iters** (was 13;
  iter 278 NEW) + 4 ledger updates + 9 stale-count drift fixes + 1
  wire-format-canonical alignment + 3 honest-defer arc closures + 3
  audit-iter rationale drift catches + 1 cross-reference pin test +
  3 README capstone updates + 3 reverse-orphan audit clean passes +
  1 memory rule codification + 6 surface report regens + 1
  multi-iter arc finale capstone + 1 mid-iter dual-drift catch + 1
  NEW arc-class implementation start + 1 NEW arc-class plumbing
  wire + **1 phase-transition implementation** (Thread B iter 278:
  Phase 2-lite removal + Phase 2-full Tier 1 ownership) across **120
  iters**.
