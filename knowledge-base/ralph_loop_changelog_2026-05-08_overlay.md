# Operator Changelog 2026-05-08 — Thread B Overlay Phase 2-full Arc (iter 275-279)

**Coverage**: iter 275-279 (5-iter Thread B Overlay Phase 2-full arc)
**Cadence**: 6th post-arc operator changelog (mirrors iter-187/196/216/220/229/235/241/247/262 cadence; 1st for a NEW arc-class vs 4 for A1.x sub-field arcs)

## TL;DR

- **ImGui v1.91.5 vendored** into `swfoc_overlay/imgui/` (12 files +
  LICENSE.txt) via `git clone --depth 1 --branch v1.91.5
  https://github.com/ocornut/imgui.git`. Static-linked into
  `swfoc_overlay.dll` — no runtime DLL dependencies beyond what
  Phase 1 already pulled in.
- **Phase 2-lite REMOVED** (DrawVisibleBadge + ScreenVertex + 5
  Build*Bar helpers — ~190 lines of D3D9 raw-vertex math gone).
  ImGui Tier 1 HUD strip OWNS the render path.
- **Phase 2-full Tier 1 + Tier 2 SHIPPED**: 5-row HUD (catalog
  rollup + bridge LED + credits ProgressBar + units ProgressBar +
  scene name + last error) + 2 multiplier honest-defer rows + footer
  + faction-tinted chrome (border + separators).
- **1st NEW arc-class to COMPLETE since iter-100** — Thread B is
  project-level (NEW capability), not A1.x sub-field (incremental).
- **15 individual-iter pattern lessons + 4 arc-level capstone
  lessons** captured across 5 iters. Including: NEW arc-class follows
  5-iter canonical shape; vendoring iters bear binary cost (1:746
  vendoring:application ratio); HudSnapshot model stability across
  235-iter span (iter 43→279); honest-defer pattern for render
  features.

## Per-iter walk-through

### Iter 275 — Thread B Overlay Phase 2-full RE kickoff (Day 1)

**Type**: Pure design doc; 0 code changes.

**Deliverable**: `knowledge-base/iter275_overlay_phase2_full_re_kickoff.md`
(~280 lines).

**Key decisions**:
- ImGui version: v1.91.5 (latest stable; chosen over manual tarball
  because `git clone --depth 1 --branch <tag>` is faster + verifiable
  via release tag).
- Vendoring shopping list: 12 files (imgui core 5 + internal headers
  3 + backends 2 + stb 3 = ~10K LoC; ~75 KB source).
- Build.bat integration pattern: insert `[2.5/3] Compiling ImGui...`
  step between current [2/3] and [3/3].
- Tier 1/2/3 content roadmap: Tier 1 (iter 277-278) ImGui plumbing
  + 4-row HUD strip; Tier 2 (iter 279) catalog rollup + multipliers
  + faction-tint consistency; Tier 3 DEFERRED to Phase 4+ (camera
  projection matrix RE pin needed for per-unit Inspector overlay).
- Arc length estimate: 5 iters; ~5 hours wall-clock total.

**Pattern lessons**:
1. NEW arc-class kickoff is multi-iter even when each iter is small
   — foundational vs incremental mode distinction.
2. Phase staging mirrors A1.x staging at architecture level — fractal
   pattern (each phase has its own RE/implement/verify cycle).
3. HudSnapshot model is the contract; Phase 2-full only changes the
   renderer — model stability across phases signals healthy
   architecture.

### Iter 276 — ImGui vendoring + build.bat update (Day 1)

**Type**: RE-implementation (vendoring).

**Deliverable**: 13 files vendored into `swfoc_overlay/imgui/` + 3
build.bat changes; `knowledge-base/iter276_overlay_imgui_vendoring.md`
(~280 lines).

**Stats**:
- Vendored size: ~2.8 MB on disk source.
- DLL size: 270,848 → **1,035,264 B** (+764,416 B; +281%) — within
  iter-275 predicted range.
- Build steps: 3 → **4** (added [3/4] ImGui compile).
- Linker: +6 .o files + 3 NEW `-l<lib>` flags (`-limm32` /
  `-ldwmapi` / `-lgdi32`).

**Mid-iter bug catch**: First build attempt failed at link with 3 GDI
undefined references in `imgui_impl_win32.o` (`GetDeviceCaps` /
`CreateRectRgn` / `DeleteObject` for high-DPI scaling + IME
composition rgn). iter-275 plan listed `-limm32` + `-ldwmapi`
correctly but missed `-lgdi32`. Fixed in ~15 sec.

**Pattern lessons**:
1. ImGui vendoring is mostly painless under MinGW-w64 — zero compile
   errors despite Dear ImGui's MSVC-focused docs.
2. Vendored-library size estimates use 1× source size as upper bound
   for static-linked binaries (iter-275's "<800 KB" was right).
3. `git clone --depth 1 --branch <tag>` is the cleanest vendoring
   path — verifiable provenance + minimum bandwidth + ~2 min
   wall-clock.

### Iter 277 — ImGui::CreateContext + DX9/Win32 Init + minimal panel (Day 1)

**Type**: RE-implementation (plumbing wire).

**Deliverable**: `overlay.cpp` 445 → **623** LoC (+178);
`knowledge-base/iter277_overlay_imgui_init.md` (~280 lines).

**Plumbing wire shape**:
- `EnsureImGuiInit(dev, hwnd)` — lazy first-call init with HWND
  fallback through `GetSwapChain(0)->GetPresentParameters()` if
  Present's `hwnd` is null (fullscreen edge case).
- `RenderImGuiPanel()` — wraps each frame in `ImGui_ImplDX9_NewFrame`
  + `ImGui_ImplWin32_NewFrame` + `ImGui::NewFrame`. Renders minimal
  panel only when `g_visible` is true.
- `ShutdownImGui()` — called from Uninstall before MinHook teardown
  so device-bound resources release while device is still valid.
- `HookedReset` wraps `ImGui_ImplDX9_InvalidateDeviceObjects` +
  `_CreateDeviceObjects` around the forwarded Reset.

**Phase 2-lite amber rectangle preserved** (Option A defensive per
iter-275 plan; iter-278 will REMOVE).

**Mid-iter forward-decl issue**: First build [2/4] failed with 4
"not declared in this scope" errors because new helpers were added
AFTER HookedPresent/HookedReset in the anonymous namespace. Fixed in
~30 sec with 4 forward declarations using `extern std::atomic<bool>`
so the brace-initialized definition coexists.

**Mid-iter stale-build trap**: First failed build still produced
"SUCCESS" banner because `build.bat`'s `if errorlevel 1` is per-step
not cumulative — step-2 fail + step-3 success + step-4 success
linked stale .o from iter-276. Mitigation: grep build log for
`error` rather than trusting success banner.

**DLL**: 1,035,264 → **1,036,800 B** (+1,536 B; +0.15%) — call-site
additions are essentially free vs iter-276's +281% vendoring cost.

**Pattern lessons**:
1. Forward declarations are non-optional in anonymous namespaces —
   anon namespace controls linkage (internal), not name lookup
   ordering.
2. `build.bat` error-checking is per-step, not cumulative — grep
   build logs for `error` explicitly.
3. DLL size delta validates iter-276 vendoring claim — call-site
   iters are binary-free vs vendoring iters.

### Iter 278 — 4-row HUD strip via ImGui (replaces Phase 2-lite) (Day 1)

**Type**: RE-implementation (Tier 1 content).

**Deliverable**: `overlay.cpp` 623 → **540** LoC (-83 NET);
`knowledge-base/iter278_overlay_imgui_hud_strip.md` (~280 lines).

**DELETED ~190 lines** of Phase 2-lite raw-D3D9 vertex math:
`DrawVisibleBadge` (90 lines) + `ScreenVertex` struct + `kFvfScreen`
constant + `HudBar` struct + 5 `Build*Bar` helpers
(BuildBridgeBar/BuildCreditsBar/BuildUnitsBar/BuildSceneBar/BuildLastErrorBar).

**PRESERVED**: `FactionTintForSlot` — reused by ImGui panel via NEW
`DwordToImVec4` helper (5 lines) converting iter-103 `0xCCAARRGGBB`
packing convention to ImGui's `ImVec4(r, g, b, a)` 0..1 range.

**NEW 4-row HUD strip** rendered via ImGui Text + ProgressBar:
- Row 0: Bridge LED (faction-tinted ImGui::Text when reachable, red
  when not, slot label TextDisabled).
- Row 1: Credits Text + ProgressBar 0..1M with `%lld cr` label.
- Row 2: Units Text + ProgressBar 0..200 with `%d / 200` label.
- Row 3: Scene name (Text or TextDisabled when unknown).
- Row 4: Last error (Separator + colored TextWrapped, only when
  present).
- Footer: "F1 toggles | Phase 2-full @ iter 278".

**`<algorithm>` include added** for `std::min` ProgressBar ratio
clamps.

**DLL**: 1,036,800 → **1,035,776 B** (-1,024 B; -0.1%) — **net binary
WIN** because Phase 2-lite removal trimmed more than ImGui Tier 1
added.

**Pattern lessons**:
1. ImGui widgets are denser than hand-rolled D3D9 — vendored UI
   library replacing hand-rolled rendering wins on LoC AND binary
   despite static-link cost.
2. Color-format helpers bridge ecosystems cleanly — DwordToImVec4
   single source of truth + format conversion at call site.
3. HudSnapshot model stability paid off across 4 render-path iters
   (43→103→277→278) — stable data contract enabled phase transition
   entirely render-side.

### Iter 279 — Tier 2 content + faction-tint chrome + arc-level capstone (Day 1)

**Type**: RE-implementation (Tier 2 content) + multi-iter arc finale.

**Deliverable**: `overlay.cpp` 540 → **597** LoC (+57);
`knowledge-base/iter279_overlay_phase2_full_close.md` (~280 lines).

**Tier 2 content**:
- **Catalog rollup row** at top of panel body (`ImGui::TextDisabled
  "Catalog (iter-274): 142 LIVE / 25 PHASE 2 / 0 LIVE ONLY"`).
  Hardcoded iter-274 audit numbers; future iter could marshal
  dynamically via shared file or named pipe.
- **2 multiplier honest-defer rows** (`ImGui::TextDisabled "Damage
  mult: bridge-query pending (iter 280+)"` + similar fire-rate row).
  HONEST DEFER per iter-249 pattern: g_dmgMult_global +
  g_fireRateMult_global are file-scope statics in lua_bridge.cpp
  (not exported); cross-DLL read out of iter-279 scope.
- **Faction-tint chrome consistency** — pushed 3 ImGui style colors
  (`Border` + `SeparatorActive` + `SeparatorHovered`) to faction tint
  when bridge reachable AND slot known. Falls back to ImGui default
  theme when bridge offline (visual proof bridge connection drives
  chrome).

**Panel size**: 280×180 → **280×250 px** to fit added rows + separators.

**DLL**: 1,035,776 → **1,036,288 B** (+512 B; +0.05%).

**Pattern lessons** (3 individual + 4 arc-level capstone consolidated):
1. (Iter) Faction-tint chrome push/pop pairing tracked in
   `factionTintStyleColors` int; defensive cleanup for iter 280+
   multi-window extensions.
2. (Iter) Catalog rollup hardcoded compile-time vs dynamic marshal —
   pragmatic choice for iter-279 60-min budget.
3. (Iter) Render-feature honest-defer pattern (TextDisabled
   placeholder + iter-N+1 cross-reference) generalizes iter-249
   A1.x pattern to UI features.
4. **(Capstone)** NEW arc-class follows 5-iter canonical shape
   regardless of scope — both foundational (Thread B) and incremental
   (A1.x) arcs fit the same shape.
5. **(Capstone)** Vendoring iters bear binary cost; subsequent iters
   nearly free — 1:746 ratio of application-code cost vs vendoring
   cost confirms iter-275 budget projection (95.7% accurate).
6. **(Capstone)** HudSnapshot model stability across 4 render-path
   iters / 235-iter span (iter 43→279) — same 7 fields feed all 4
   render implementations.
7. **(Capstone)** Honest-defer pattern applies equally well to render
   features — ship the layout slot + name the iter that fills it.

## Operator-facing F1+overlay smoke checklist

When the overlay DLL ships into a live SWFOC environment:

1. **Install**: pick a DLL name SWFOC's import table references AND
   isn't already claimed by `powrprof.dll` (the bridge). Candidates:
   `dwmapi.dll` / `dxva2.dll` / `version.dll`. Rename
   `swfoc_overlay.dll` → `<chosen>.dll` and drop next to
   `StarWarsG.exe`.

2. **Launch**: start SWFOC. Alt-tab to **DebugView** (Sysinternals).

3. **Phase 1 verification** (D3D9 detour proof-of-life):
   - Look for `[swfoc_overlay] Install OK — F1 toggles visibility`
     (one-shot at install).
   - Look for `[swfoc_overlay] Present frame=N visible=N` lines
     every 600 frames (~10 sec at 60 fps). Frame counter should
     monotonically increase.

4. **Phase 2-full verification** (Tier 1 ImGui plumbing):
   - Look for `[swfoc_overlay] ImGui Init OK (Phase 2-full Tier 1
     panel)` (one-shot on first Present).
   - If you see `[swfoc_overlay] ImGui_ImplWin32_Init failed` or
     `_ImplDX9_Init failed`: cleanup paths are in place but the
     overlay is non-rendering.
   - If you see `[swfoc_overlay] ImGui Init: no HWND available`:
     Present called with null hwnd AND swap chain has no device
     window; iter-280+ should investigate.

5. **F1 toggle (in-game)**:
   - Press F1. Next Present log should show `visible=1`.
   - HUD should appear bottom-right of the screen as a 5-row panel:
     - **Catalog row** (gray): "Catalog (iter-274): 142 LIVE / 25
       PHASE 2 / 0 LIVE ONLY".
     - **Bridge LED**: "Bridge: ONLINE (slot N)" when bridge
       connected (faction-tinted: REBEL amber / EMPIRE chrome /
       UNDERWORLD sand+rust). Red "Bridge: OFFLINE" when
       disconnected.
     - **Credits**: progress bar 0..1M with `%lld cr` label.
     - **Units**: progress bar 0..200 with `%d / 200` label.
     - **Scene**: name text or "Scene: unknown" when unknown.
     - **Last error** (only if present): red text wrap with error
       message.
     - **Multiplier honest-defer rows** (gray placeholders):
       "Damage mult: bridge-query pending (iter 280+)" + similar
       fire-rate row.
     - **Footer** (gray): "F1 toggles | Phase 2-full @ iter 279
       (Tier 2)".
   - Panel chrome (border + separators) should tint to faction color
     when bridge reachable + slot known.
   - Press F1 again — panel disappears, log shows `visible=0`.

6. **Reset behavior** (alt-tab + resolution change):
   - Alt-tab out + back. Look for `[swfoc_overlay] Reset` event in
     log if one fires (most games don't; D3D9 device-loss is rarer
     than D3D11). HUD should redraw correctly when toggled.
   - Change in-game resolution. Same Reset path.

7. **Uninstall** (DLL detach on game exit):
   - Look for `[swfoc_overlay] ImGui Shutdown OK` then
     `[swfoc_overlay] Uninstall OK` in log.

## Pattern lessons capstone (consolidated from 15 iter-279 individual
lessons)

### A. Arc-shape patterns

1. **NEW arc-class kickoff is multi-iter even when each iter is
   small** (iter-275). Thread B follows the same 5-iter canonical
   shape as A1.x sub-field arcs but with different content axes:
   foundational arcs decompose RE → infrastructure → Tier 1 → Tier 2
   → close-out; incremental arcs decompose RE → bridge LIVE →
   simulator → native UX → live verify.
2. **Phase staging mirrors A1.x staging at architecture level**
   (iter-275). Each phase has its own RE/implement/verify cycle.
   Fractal pattern.
3. **NEW arc-class follows 5-iter canonical shape regardless of
   scope** (iter-279 capstone). The shape is the same; the contents
   differ.

### B. Vendoring patterns

4. **ImGui vendoring is mostly painless under MinGW-w64** (iter-276).
   Zero compile errors despite Dear ImGui's MSVC-focused docs.
5. **Vendored-library size estimates use 1× source size as upper
   bound** (iter-276). iter-275's "<800 KB" estimate was within 1×;
   actual +764 KB landed at upper end.
6. **`git clone --depth 1 --branch <tag>` is the cleanest vendoring
   path** (iter-276). Verifiable provenance + minimum bandwidth.
7. **Vendoring iters bear binary cost; subsequent iters are nearly
   free** (iter-279 capstone). 1:746 ratio of application-code cost
   vs vendoring cost — future arcs that vendor large libraries can
   plan binary-size budgets primarily around the vendoring iter.

### C. C++ patterns

8. **Forward declarations are non-optional in anonymous namespaces**
   (iter-277). Anon namespace controls linkage (internal), not name
   lookup ordering.
9. **`build.bat` error-checking is per-step, not cumulative**
   (iter-277). Grep build logs for `error` explicitly to catch
   step-N failures with step-(N+M) successes.
10. **DLL size delta validates iter-276 vendoring claim**
    (iter-277). Call-site iters are binary-free vs vendoring iters.

### D. Render-path patterns

11. **ImGui widgets are denser than hand-rolled D3D9** (iter-278).
    Vendored UI library replacing hand-rolled rendering wins on LoC
    AND binary despite static-link cost.
12. **Color-format helpers bridge ecosystems cleanly** (iter-278).
    DwordToImVec4 (5-line helper) preserves iter-103 faction-tint
    palette without rewriting it for ImGui.
13. **HudSnapshot model stability paid off across 4 render-path
    iters** (iter-278). Same 7 fields feed iter 43 D3D9 vertex /
    iter 103 5-row strip / iter 277 ImGui minimal / iter 278 ImGui
    Tier 1 implementations.
14. **HudSnapshot model stability across 4 render-path iters /
    235-iter span** (iter-279 capstone). Worker thread + lock-free
    atomic snapshot rotation: zero changes across iter 43 → iter
    279.

### E. Operator-trust patterns

15. **Faction-tint chrome push/pop pairing** (iter-279). Track count
    in `factionTintStyleColors` int; defensive cleanup for iter
    280+ multi-window extensions.
16. **Catalog rollup hardcoded compile-time vs dynamic marshal**
    (iter-279). Pragmatic choice for 60-min budget; cross-DLL
    marshal queued for iter 280+.
17. **Render-feature honest-defer pattern generalizes iter-249 A1.x
    pattern to UI features** (iter-279). Ship the layout slot +
    iter-N+1 cross-reference.
18. **Honest-defer pattern applies equally well to render features**
    (iter-279 capstone). General operator-trust protocol for "this
    feature has a slot in the UI but the data wire isn't ready yet."

## What's next (iter 281+)

1. **Iter 281 (RECOMMENDED)** — **HudSnapshot multiplier extension**:
   resolves iter-279 Tier 2 honest-defer. Scope: ~30 LoC in
   `hud_state.h` (add 2 fields) + ~60 LoC in `hud_state.cpp` worker
   probe (call iter-96 SWFOC_GetDamageMultiplierGlobal + similar
   fire-rate getter via existing pipe API) + ~5 LoC in
   `overlay.cpp` to consume. ~30 min wall-clock; LOW risk
   (HudSnapshot is producer-controlled; iter-275 design only said
   "no model changes during the arc" — arc is now complete).

2. **Iter 281 (alternative)** — **Phase 4 Camera projection matrix RE
   kickoff**. Multi-iter; would unlock per-unit Inspector overlay +
   drag-to-spawn workflows (deferred from iter-275 Tier 3).

3. **Iter 281 (alternative)** — **A1.x pivot back** if user has
   surfaced new ledger entries via live-game CheatEngine tracing.
   Would restart the iter-269 honest-defer-rate measurement.

4. **NOT recommended for iter 281**: NEW arc-class kickoff (Thread C
   Save-game RE / Thread D Multi-repo CI / Thread E SonarQube). All
   are LOW operator value vs the just-shipped Thread B HUD.

## Cumulative session metrics (iter 159-280)

| Category | Count |
|---|---|
| LIVE wire/sub-field flips | +99 |
| Bridge dispatcher helpers | 10 |
| Operator-facing improvements | 34 |
| **Docs iters** | **13** (was 12; iter 280 NEW) |
| Audit/audit-followup iters | 7 |
| Memory codification iters | 1 |
| Preset-menu refresh iters | 3 |
| RE kickoff iters | 9 |
| RE-implementation iters | 9 |
| Simulator iters | 5 |
| Native UX iters | 3 |
| Staging-UI verification iters | 2 |
| Close-out iters | 15 |
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
| Multi-iter arc finale capstones | 2 |
| Mid-iter dual-drift catches | 1 |
| NEW arc-class implementation starts | 1 |
| NEW arc-class plumbing wires | 1 |
| Phase-transition implementations | 1 |
| Multi-iter arc-class completions | 1 |
| **Total iters** | **122** |
