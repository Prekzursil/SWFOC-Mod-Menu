# Iter 275 — Thread B Overlay Phase 2-full ImGui vendoring RE kickoff (NEW arc-class; HIGH operator value)

**Date:** 2026-05-08 01:00 UTC
**Iter:** 275 (NEW arc-class kickoff per iter-269 lesson #2 NON-A1.x pivot continuation; iter-274 recommendation)
**Arc:** Thread B Phase 2-full; **estimated 5-iter arc** (iter 275-279) mirroring iter-224-228 / 230-234 / 236-240 / 242-246 / 248-249 / 257-261 / 267-268 / 269-270 cadence.
**Predecessor:** Phase 1 (iter 31 in overlay README; iter 92 in master loop) + Phase 2-lite (iter 43 / iter 103). Both DONE.
**Successor:** Phase 3 (HUD reading live state from in-process powrprof.dll bridge; Phase 4 = drag-to-spawn; Phase 7 = aesthetic polish).

## Headline

Pure RE/design iter. **No code changes.** Closes the Phase 2-full design
gap by codifying the vendoring shopping list, build.bat integration
pattern, and Tier 1/2/3 content roadmap. Iter 276+ ships ImGui vendoring;
iter 277-279 ships Tier 1 + 2 content.

| Metric | Value |
|---|---|
| Overlay source LoC | 819 lines (dllmain 48 + overlay 445 + hud_state 229 + headers 97) |
| Compiled DLL size | 270,848 bytes (existing Phase 2-lite amber rectangle) |
| Phase 1 status | DONE (D3D9 vtable harvest + MinHook detours of Present/Reset; F1 hotkey via worker-thread polling) |
| Phase 2-lite status | DONE (160×32 amber rectangle bottom-right at ~70% alpha; saves+restores all touched render state) |
| Phase 2-full target | Vendor Dear ImGui + DX9 backend + Win32 backend; replace amber rectangle with 4-row vertical HUD strip |
| HUD model already in place | `HudSnapshot` struct (bridge-reachable / local-player-slot / credits / alive-units / scene-name / last-error / generated-tick) — iter-275 design preserves this surface unchanged |
| Vendoring shopping list | **12 files** (imgui core 5 + internal headers 3 + stb 3 + backends DX9 + Win32 = ~10K LoC) |
| Build.bat integration | already has [1/3] MinHook + [2/3] sources + [3/3] link pattern; iter-276 adds [2.5/3] ImGui compile step |
| Estimated arc length | **5 iters** (iter 275-279) |
| Iter 275 deliverable | This document (~250 lines) |

## Phase 1 + Phase 2-lite recap (already shipped)

### Phase 1 (DONE)

`overlay.cpp` lines 1-90 + remainder = full D3D9 detour skeleton:

- **D3D9 vtable harvest**: throwaway `IDirect3D9` + `IDirect3DDevice9`
  pair created with minimum-viable `D3DPRESENT_PARAMETERS` to read the
  vtable pointers for `Present` (slot 17) + `Reset` (slot 16).
- **MinHook detours** at the harvested addresses, redirecting both
  vtable methods through our `Present_Detour` / `Reset_Detour` and
  saving the trampolines for forwarding.
- **F1 hotkey poller** in a dedicated worker thread (100ms tick,
  `GetAsyncKeyState(VK_F1)` edge-detection). Avoids WndProc detour ⇒
  no host message-pump deadlock risk.
- **Frame counter** (`g_frameCount`) + DebugView log every 600 frames
  as proof-of-life.
- Public surface: `Install / Uninstall / IsVisible / SetVisible / ToggleVisible`.

### Phase 2-lite (DONE)

`overlay.cpp` lines 70-145 (approx):

- **Pre-transformed-vertex format** (`D3DFVF_XYZRHW | D3DFVF_DIFFUSE`) —
  no projection matrix setup needed.
- **160×32 amber rectangle** at bottom-right with 12px margin, ~70%
  alpha, matches editor's `WarningForeground` brand color.
- **Render state save/restore** for alpha-blend, Z-enable, cull-mode,
  lighting, FVF — host's subsequent draws stay clean.
- **+60 LoC over Phase 1**, no new dependencies.

### `HudSnapshot` model (Phase 2-lite + Phase 2-full bridge)

`hud_state.h` already defines the model that Phase 2-full will render:

```cpp
struct HudSnapshot {
    bool bridge_reachable;
    int local_player_slot;
    int64_t credits;
    int alive_units;
    std::string scene_name;
    std::string last_error;
    uint64_t generated_tick;
};
```

Lock-free `std::atomic<HudSnapshot*>` rotation pattern is already
implemented; Phase 2-full only changes the *rendering* of this snapshot,
not its production. **No worker-thread changes needed in iter 276+.**

## Phase 2-full design

### Vendoring shopping list (12 files; ~10K LoC vendored)

Per overlay README + Dear ImGui v1.91+ canonical structure, vendor
into `swfoc_overlay/imgui/`:

**Core (5 files)**:
- `imgui.cpp` (~6.5 KB)
- `imgui.h` (~3 KB)
- `imgui_draw.cpp` (~3 KB)
- `imgui_widgets.cpp` (~5 KB)
- `imgui_tables.cpp` (~1.5 KB)

**Internal headers (3 files)**:
- `imgui_internal.h` (~2 KB)
- `imconfig.h` (~0.5 KB)
- `imstb_textedit.h`, `imstb_rectpack.h`, `imstb_truetype.h` (~3 KB total)

**Backends (2 files in `backends/`)**:
- `imgui_impl_dx9.cpp` + `imgui_impl_dx9.h` (~25 KB combined)
- `imgui_impl_win32.cpp` + `imgui_impl_win32.h` (~30 KB combined)

**Total**: 12 files; ~10K LoC; ~75 KB source on disk; bundled DLL
expected to grow from 270,848 B → ~600-800 KB after ImGui static link.

### Build.bat integration (iter 276 scope)

Add `[2.5/3] Compiling ImGui...` step between current [2/3] and [3/3]:

```bat
echo [2.5/3] Compiling ImGui (vendored)...
%GPP% -c %CPPFLAGS% -Iimgui imgui/imgui.cpp -o imgui_imgui.o
%GPP% -c %CPPFLAGS% -Iimgui imgui/imgui_draw.cpp -o imgui_draw.o
%GPP% -c %CPPFLAGS% -Iimgui imgui/imgui_widgets.cpp -o imgui_widgets.o
%GPP% -c %CPPFLAGS% -Iimgui imgui/imgui_tables.cpp -o imgui_tables.o
%GPP% -c %CPPFLAGS% -Iimgui imgui/backends/imgui_impl_dx9.cpp -o imgui_impl_dx9.o
%GPP% -c %CPPFLAGS% -Iimgui imgui/backends/imgui_impl_win32.cpp -o imgui_impl_win32.o
if errorlevel 1 goto fail
```

Update [3/3] linker to include the 6 new `.o` files; no extra `-l<lib>`
flags needed (ImGui DX9 backend uses already-linked `d3d9.dll` API).

### Tier 1 content (iter 277-278 scope)

**Required for "Phase 2-full DONE" line** per overlay README:
> "A panel saying 'Hello, operator' with the current frame counter,
> visibility toggle indicator, and a 'Phase 2-full landed at iter X'
> footer."

Iter 277-278 implementation plan:

1. **Iter 277**: ImGui::CreateContext + DX9 Init + Win32 Init; `Present_Detour`
   wraps each frame in `ImGui_ImplDX9_NewFrame` + `ImGui_ImplWin32_NewFrame`
   + `ImGui::NewFrame` / `ImGui::Render` / `ImGui_ImplDX9_RenderDrawData`.
   Render an empty `ImGui::Begin("SWFOC Overlay") / ImGui::Text("Hello, operator") / ImGui::End()`.
   This is the "ImGui plumbing works" milestone; equivalent in spirit to
   iter-258's "first sub-field LIVE branch" milestone.

2. **Iter 278**: Replace empty panel with the 4-row HUD strip rendered
   via ImGui:
   - Row 0: bridge LED via colored `ImGui::Text("●")` + label.
   - Row 1: credits via `ImGui::Text("Credits: %lld", snap.credits)`.
   - Row 2: alive_units via `ImGui::Text("Units: %d / 200", snap.alive_units)` +
     ImGui::ProgressBar.
   - Row 3: scene_name + last_error via 2 `ImGui::Text` lines.
   - Footer: `ImGui::Text("Phase 2-full @ iter 278")`.

### Tier 2 content (iter 279 scope)

**Operator-value enhancements** beyond the README "DONE line":

- **PHASE 2 PENDING badge counts** — surface the 25-entry catalog state
  (mirrors editor bottom status bar's "Capability: 53 LIVE / 37 PHASE 2
  / 3 LIVE ONLY" rollup). Renders as `ImGui::Text("Catalog: %d LIVE / %d
  PHASE 2", liveCount, phase2Count)` at the top of the panel.
- **Active SetDamageMultiplierGlobal / SetFireRateMultiplierGlobal
  multiplier values** — surface iter-96/iter-225 LIVE wire effects in-game
  so operators see when their multipliers are active. Render as `ImGui::Text("Damage: %.2fx | FireRate: %.2fx", damageMult, fireRateMult)`.
- **Faction tinting** — when `local_player_slot` is known, tint the panel
  background using the same REBEL amber / EMPIRE chrome / UNDERWORLD sand+rust
  palette the editor already uses (extracted from `WarningForeground`-family
  brand colors). Reuses iter-103 Phase 2-lite faction-tint logic.

### Tier 3 content (deferred to iter 281+)

**Out of scope for this 5-iter arc**:

- **Per-unit Inspector overlay** (hover unit → show hull/shield/speed):
  needs camera projection matrix RE pin (per overlay README's
  "Phase 4 → drag-to-spawn (camera projection matrix RE pin required first)"
  comment). Defer to dedicated Phase 4 arc.
- **Hotkey-driven cinematic mode** (iter-150 letterbox + iter-145 cinematic
  camera quad): needs hotkey-binding UI in ImGui + DoString-from-overlay
  routing (overlay would call powrprof.dll bridge in-process). Defer to
  Phase 5.

## Estimated arc length: 5 iters

Mirroring iter-224-228 SetFireRate / iter-236-240 SetCameraPos / iter-257-261
SetUnitField max_* canonical 5-iter arc cadence:

| Iter | Scope | Estimated wall-clock | Risk |
|---|---|---|---|
| **275** | RE kickoff + design doc (this iter) | ~30 min | LOW (pure docs) |
| **276** | Vendor 12 ImGui files + build.bat update + verify compile chain (no rendering yet) | ~60-90 min | MEDIUM (build path could surface MinGW-w64 toolchain incompatibilities) |
| **277** | ImGui::CreateContext + DX9/Win32 Init + minimal `ImGui::Begin/End` panel | ~60 min | MEDIUM (DX9 backend stateful — may surface render-state-restore conflicts with Phase 2-lite) |
| **278** | 4-row HUD strip via ImGui (replaces Phase 2-lite amber rectangle) | ~60 min | LOW (HudSnapshot model already complete; just translate render call sites) |
| **279** | Tier 2 content: catalog rollup + multipliers + faction tinting + live verify + close-out | ~60 min | LOW (operator-value extensions, all data already in HudSnapshot) |

**Total estimated wall-clock**: ~5 hours across 5 iters.

## What's deferred (Phase 6 + Phase 4 + Phase 5)

- **Editor↔overlay IPC** (Phase 6): second named pipe so editor reflects
  overlay actions in real-time. Not needed yet — overlay calls powrprof.dll
  bridge directly in-process.
- **Camera projection matrix RE pin** (prerequisite for Phase 4
  drag-to-spawn): would unlock per-unit Inspector overlay.
- **Aesthetic polish + faction-tinted chrome** (Phase 7): post-functional
  polish iter (think iter-265-style README capstone but for overlay).

## Pattern lessons

### Lesson #1 — NEW arc-class kickoff is multi-iter even when each iter is small

Iter-275 is a pure design doc — but the arc it kicks off is 5 iters. This
is because Thread B is a NEW arc-class (not an A1.x sub-field), so the
"5-iter canonical shape" applies even though no LIVE wires ship in
iter-275. **Pattern**: NEW arc-class kickoffs follow 5-iter shape regardless
of whether they ship LIVE wires; the mode is *foundational* not
*incremental*.

### Lesson #2 — Phase staging mirrors A1.x staging at the architecture level

The overlay's Phase 1 → Phase 2-lite → Phase 2-full → Phase 3 → Phase 4
→ Phase 5/6/7 progression mirrors A1.x's RE kickoff → bridge LIVE wire
→ simulator → native UX → live verify cadence at the *project* level
rather than the *sub-field* level. **Pattern**: phase staging is fractal
— each phase has its own RE/implement/verify cycle.

### Lesson #3 — HudSnapshot model is the contract; Phase 2-full only changes the renderer

`hud_state.h`'s `HudSnapshot` struct is unchanged from Phase 2-lite to
Phase 2-full. The contract between worker (producer) and render path
(consumer) is stable; only the rendering changes. **Pattern**: model
stability across phases is a sign of healthy architecture — Phase 2-full
shipping shouldn't cascade rework into Phase 1 plumbing.

## Verification gates (ALL GREEN UNCHANGED)

Pure RE/design iter — no code changes; all gates inherit iter-274 state:

| Gate | Result |
|---|---|
| Editor test build | inherits iter-274 0 errors |
| Bridge harness | inherits iter-273 1100/0 |
| Verifier ledger lint | inherits iter-274 0/0 at 318 entries |
| Capability surface | inherits iter-274 unchanged |
| Overlay DLL | inherits Phase 2-lite 270,848 B |

## What's next

- **Iter 276**: Vendor 12 ImGui files into `swfoc_overlay/imgui/` + update
  `build.bat` with [2.5/3] ImGui compile step + verify compile chain. No
  rendering changes; just confirms the toolchain accepts ImGui sources
  and the DLL still links.
- **Iter 277-279**: progressively replace Phase 2-lite amber rectangle
  with full ImGui-rendered HUD per Tier 1+2 plan above.
- **Post iter-279**: queue Phase 4 RE kickoff (camera projection matrix)
  OR pivot back to A1.x if live-game CheatEngine tracing surfaces fresh
  ledger entries.

## Iter 275 close-out summary

- This document is the iter 275 deliverable.
- **Code changes**: 0 (pure RE/design).
- All gates GREEN inherit from iter-274.
- **NEW arc-class kickoff** — 1st 5-iter arc since iter-257-261 SetUnitField max_*
  (also a 5-iter arc, but A1.x sub-field arc; Thread B is a different
  class).
- **5th consecutive NON-A1.x iter** per iter-269 lesson #2 (iter-271 +
  iter-272 + iter-273 + iter-274 + **iter-275**).
- 109 → 109 buttons UNCHANGED. 106 → 106 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25 unchanged.
- **Pattern lesson capstone**: NEW arc-class kickoffs follow 5-iter shape
  regardless of whether they ship LIVE wires; foundational vs incremental
  mode distinction matters.
- **Session-cumulative this conversation (iter 159-275)**: +99 LIVE
  wire/sub-field flips + 10 helpers + 34 operator-facing improvements + 12
  docs iters + 7 audit/audit-followup iters + 1 memory codification iter +
  3 preset-menu refresh iters + **9 RE kickoff iters** (was 8; iter 275
  NEW Thread B kickoff) + 5 RE-implementation iters + 5 simulator iters +
  3 native UX iters + 2 staging-UI verification iters + 11 close-out iters
  + 4 ledger updates + 9 stale-count drift fixes + 1
  wire-format-canonical alignment + 3 honest-defer arc closures + 3
  audit-iter rationale drift catches + 1 cross-reference pin test + 3
  README capstone updates + 3 reverse-orphan audit clean passes + 1 memory
  rule codification + 6 surface report regens + 1 multi-iter arc finale
  capstone + 1 mid-iter dual-drift catch across **117 iters**.
