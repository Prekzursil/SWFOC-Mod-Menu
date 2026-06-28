# Iter 277 — Thread B Overlay Phase 2-full iter 3/5: ImGui::CreateContext + DX9/Win32 Init + minimal "Hello, operator" panel

**Date:** 2026-05-08 03:00 UTC
**Iter:** 277 (Thread B Phase 2-full arc iter 3/5)
**Predecessor:** iter 276 vendoring (12+1 ImGui files; build chain GREEN; DLL 1,035,264 B).
**Successor:** iter 278 (4-row HUD strip via ImGui Tables + ProgressBar; replaces Phase 2-lite amber rectangle).

## Headline

**ImGui plumbing wired into Present_Detour.** Lazy-init pattern captures
HWND on first Present, calls `IMGUI_CHECKVERSION` + `ImGui::CreateContext`
+ `ImGui_ImplWin32_Init` + `ImGui_ImplDX9_Init`. Per-frame: 3 NewFrames
+ minimal `ImGui::Begin("SWFOC Overlay") / ImGui::Text("Hello, operator")
/ Frame: %llu / Phase 2-full @ iter 277 / End` panel rendered when
`g_visible` is true. Reset_Detour invalidates/recreates DX9 device
objects around the host's Reset. Uninstall calls Shutdown.

| Metric | Value |
|---|---|
| `overlay.cpp` LoC | 445 → **623** (+178 lines for ImGui plumbing) |
| DLL size | 1,035,264 → **1,036,800** B (+1,536 B for new code) |
| Build steps | 4/4 all GREEN; 0 errors / 0 warnings (after forward-decl fix) |
| Phase 2-lite amber rectangle | preserved (Option A defensive per iter-275 plan) |
| ImGui panel position | bottom-right above amber strip (180px above, with 12px margin) |
| ImGui Init mode | lazy (first Present_Detour call); HWND fallback via `GetSwapChain(0)->GetPresentParameters()` |
| ImGui shutdown order | HUD worker → ImGui → MinHook (ensures device-bound resources release while device valid) |
| Bridge harness | n/a (no bridge changes) |
| Verifier ledger lint | n/a (no ledger changes) |

## What shipped

### `swfoc_overlay/overlay.cpp` — 4 sections added/modified

**Section 1: Includes (lines 14-31)** — added 3 ImGui headers:
```cpp
#include "imgui/imgui.h"
#include "imgui/backends/imgui_impl_dx9.h"
#include "imgui/backends/imgui_impl_win32.h"
```

**Section 2: Forward declarations (lines 67-77)** — anon-namespace forward
declarations because `EnsureImGuiInit` / `RenderImGuiPanel` /
`g_imguiInitialized` are defined AFTER `HookedPresent` / `HookedReset`
which reference them. Used `extern std::atomic<bool>` to allow the
brace-initialized definition to coexist with the forward declaration in
the same anonymous namespace.

**Section 3: HookedPresent + HookedReset modifications (lines 285-340)**:
- `HookedPresent`: lazy `EnsureImGuiInit(dev, hwnd)` call before
  Phase 2-lite render; `RenderImGuiPanel()` call after Phase 2-lite so
  ImGui panel appears on top.
- `HookedReset`: `ImGui_ImplDX9_InvalidateDeviceObjects()` before
  forwarding; `ImGui_ImplDX9_CreateDeviceObjects()` after successful
  forward. Both guarded on `g_imguiInitialized` so pre-init Reset is
  a no-op for ImGui.

**Section 4: ImGui plumbing helpers (lines 380-503)** — 3 new functions
in anonymous namespace:
- `EnsureImGuiInit(dev, hwnd)` — lazy first-call init with HWND
  fallback through `GetSwapChain(0)->GetPresentParameters()` if
  `hwnd` from Present is null. Calls `ImGui::CreateContext` +
  `StyleColorsDark` + `ImGui_ImplWin32_Init` + `ImGui_ImplDX9_Init`.
  Disables `IniFilename` + `LogFilename` (no operator-side files).
  Cleanup-on-failure paths for both backend Init failures.
- `ShutdownImGui()` — calls `ImGui_ImplDX9_Shutdown` +
  `ImGui_ImplWin32_Shutdown` + `ImGui::DestroyContext`. Idempotent
  via `g_imguiInitialized` flag.
- `RenderImGuiPanel()` — wraps each frame in
  `ImGui_ImplDX9_NewFrame` + `ImGui_ImplWin32_NewFrame` +
  `ImGui::NewFrame`. Renders panel only when `g_visible` is true.
  Panel positioned bottom-right above Phase 2-lite amber strip
  (220×90 px with 12 px right margin + 50 px above strip + 12 px
  bottom margin). Window flags disable title bar, resize, move, save,
  focus, nav for "operator HUD" (non-interactive) presentation.

### `swfoc_overlay.dll` size delta

| Build | DLL bytes | Delta |
|---|---|---|
| iter 275 (Phase 2-lite) | 270,848 | baseline |
| iter 276 (ImGui vendored, no rendering) | 1,035,264 | +764,416 (+281%) |
| **iter 277 (ImGui plumbing wired)** | **1,036,800** | **+1,536 (+0.15%)** |

The +1,536 B delta from iter-276 is solely the new code in `overlay.o`
(forward decls + 3 helpers + 8 lines edited in HookedPresent/Reset/
Uninstall). Confirms the iter-276 vendoring already included all
ImGui code in the static link; iter-277 just adds call sites.

## What broke + what fixed it

### Mid-iter forward-declaration order issue

First build attempt failed at step [2/4] with 4 "not declared in this
scope" errors:

```
overlay.cpp:290:9: error: 'EnsureImGuiInit' was not declared in this scope
overlay.cpp:306:9: error: 'RenderImGuiPanel' was not declared in this scope
overlay.cpp:319:13: error: 'g_imguiInitialized' was not declared in this scope
overlay.cpp:324:30: error: 'g_imguiInitialized' was not declared in this scope
```

Root cause: I added the helpers + variable AFTER the existing
`constexpr int kSlotPresent = 17;` (the last element in the anonymous
namespace before iter-277). But `HookedPresent` (line 261) and
`HookedReset` (line 286) — which now reference these helpers — are
defined EARLIER in the same anonymous namespace. C++ requires
forward declaration for forward references.

Fix: added 4 forward declarations after `g_origPresent` / `g_origReset`
declarations (lines 67-77). Used `extern std::atomic<bool>
g_imguiInitialized;` so the brace-initialized definition below
coexists.

**Fix latency**: ~30 sec (read error, identify root cause, add 5 lines
of forward declarations, re-run build).

### Stale build trap (build.bat error-checking)

First build attempt also revealed a build.bat issue:
- `[2/4]` failed with the 4 errors above.
- `if errorlevel 1 goto fail` correctly triggered after step [2/4]'s
  3 g++ calls (overlay.cpp / dllmain.cpp / hud_state.cpp).
- BUT — the `if errorlevel 1` after step [3/4]'s ImGui compiles
  reset errorlevel to 0 (because all 6 ImGui sources compiled fine).
- So step [4/4] linker ran with stale `.o` files from iter-276
  (overlay.o was unchanged on disk because compile failed).
- Linker SUCCEEDED with stale objects → produced a 1,035,264 B DLL
  identical to iter-276.

**Pattern**: build.bat's `if errorlevel 1` is per-step, not cumulative.
A step-2 failure doesn't propagate to step-4. The mitigation is
running the actual build via `grep -E 'error|warning'` on the build
log rather than trusting the final `=== OVERLAY BUILD SUCCESS ===`
header. Iter 277 used this pattern; iter 276 didn't surface the
issue because compile was clean.

**Pattern lesson**: trust grep on build logs, not the success banner.

## Verification gates (ALL GREEN after fix)

| Gate | Result | Δ vs iter-276 |
|---|---|---|
| Overlay build [1/4] MinHook | clean | unchanged |
| Overlay build [2/4] overlay sources | **clean** (after forward-decl fix) | NEW ImGui plumbing additions |
| Overlay build [3/4] ImGui | clean | unchanged |
| Overlay build [4/4] link | clean | unchanged |
| Overlay DLL size | **1,036,800 B** | +1,536 B (call-site additions) |
| `overlay.cpp` LoC | **623** | +178 lines |
| Bridge harness | n/a (no bridge changes) | inherits iter-276 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) | inherits iter-276 0/0 at 318 entries |

## Pattern lessons

### Lesson #1 — Forward declarations are non-optional in anonymous namespaces

I instinctively expected anonymous-namespace symbols to be forward-
visible within the same TU, similar to extern symbols within a C++
translation unit. But anonymous namespace just controls *linkage*
(internal); it doesn't change name lookup ordering. **Pattern**:
adding helpers to existing anonymous namespaces still requires
forward declarations if call sites precede definitions.

### Lesson #2 — `build.bat` error-checking is per-step, not cumulative

The build.bat's `if errorlevel 1 goto fail` only catches step-N
failures, not step-(N-2) cascades. In a 4-step build, a step-2 fail
+ step-3 success + step-4 success produces a "SUCCESS" banner with
a stale binary. **Pattern**: always grep build logs for `error`
explicitly when the iter touches earlier compile steps; don't trust
the success banner alone.

### Lesson #3 — DLL size delta validates iter-276 vendoring claim

Iter-276 close-out claimed: "DLL grew 270,848 B → 1,035,264 B
(+764,416 B = +281%) after static link of all ImGui code."
Iter-277 added 178 LoC of plumbing + 4 helper functions, and the
DLL grew only +1,536 B (+0.15%). This validates that iter-276's
+281% was indeed the cost of *all* ImGui code; iter-277's call-site
additions are a rounding error in comparison.

**Pattern**: vendoring iters bear the static-link cost; call-site
iters are essentially free in binary-size terms.

## What's next (iter 278-279)

Per iter-275 design:

1. **Iter 278**: Replace minimal "Hello, operator" panel with the
   4-row HUD strip rendered via ImGui Tables + ProgressBar widgets,
   consuming the existing `HudSnapshot` model unchanged. This
   replaces (or supersedes) the Phase 2-lite `DrawVisibleBadge`
   render path. Decision: REMOVE Phase 2-lite amber rectangle code
   in iter 278 once the ImGui-rendered strip is confirmed working
   via DebugView frame log + visual inspection (deferred to live
   verify in iter 279).

2. **Iter 279**: Tier 2 content (catalog rollup + multipliers +
   faction tinting) + live verify + close-out (multi-iter arc
   finale, 5/5 mirroring iter-228/234/240/246/261 cadence).

**Risk for iter 278**: LOW — `HudSnapshot` model is already complete;
the work is translating the existing `DrawVisibleBadge`'s 4-row D3D9
vertex math into ImGui Table widgets. No new RE, no new toolchain
unknowns.

## Iter 277 close-out summary

- This document is the iter 277 deliverable.
- **Code changes**: +178 LoC in `overlay.cpp` (3 ImGui includes + 4
  forward decls + 3 helper functions ~120 LoC + 8 lines edited
  across HookedPresent/HookedReset/Uninstall).
- All gates GREEN: build 4/4 clean (after forward-decl fix); DLL
  1,036,800 B (+1,536 B vs iter-276); bridge harness + ledger lint
  inherit iter-276 unchanged.
- **Iter 3 of 5-iter Thread B Phase 2-full arc** (iter 275-279). Arc
  60% complete.
- **7th consecutive NON-A1.x iter** per iter-269 lesson #2 (iter-271
  + iter-272 + iter-273 + iter-274 + iter-275 + iter-276 + iter-277).
- 109 → 109 buttons UNCHANGED. 106 → 106 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25
  unchanged.
- **3 NEW pattern lessons**: forward decls non-optional in anonymous
  namespaces; build.bat error-checking is per-step not cumulative;
  DLL size delta validates iter-276 vendoring claim (call-site iters
  are binary-free in comparison).
- **Mid-iter forward-decl fix latency**: ~30 sec. Stale-build trap
  caught by grep on build log rather than success banner trust.
- **Phase 2-lite amber rectangle preserved** (Option A defensive
  per iter-275 plan); iter-278 will REMOVE in favor of ImGui-rendered
  4-row strip once verified.
- **Session-cumulative this conversation (iter 159-277)**: +99 LIVE
  wire/sub-field flips + 10 helpers + 34 operator-facing improvements
  + 12 docs iters + 7 audit/audit-followup iters + 1 memory
  codification iter + 3 preset-menu refresh iters + 9 RE kickoff
  iters + **7 RE-implementation iters** (was 6; iter 277 NEW Thread
  B plumbing) + 5 simulator iters + 3 native UX iters + 2 staging-UI
  verification iters + **13 close-out iters** (was 12; iter 277 NEW)
  + 4 ledger updates + 9 stale-count drift fixes + 1
  wire-format-canonical alignment + 3 honest-defer arc closures + 3
  audit-iter rationale drift catches + 1 cross-reference pin test +
  3 README capstone updates + 3 reverse-orphan audit clean passes +
  1 memory rule codification + 6 surface report regens + 1
  multi-iter arc finale capstone + 1 mid-iter dual-drift catch + 1
  NEW arc-class implementation start + **1 NEW arc-class plumbing
  wire** (Thread B iter 277 ImGui Init + Render + Reset/Shutdown
  hooks) across **119 iters**.
