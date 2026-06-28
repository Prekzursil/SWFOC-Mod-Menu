# Iter 276 — Thread B Overlay Phase 2-full iter 2/5: vendor ImGui v1.91.5 + build.bat update + verify compile chain

**Date:** 2026-05-08 02:00 UTC
**Iter:** 276 (Thread B Phase 2-full arc iter 2/5)
**Predecessor:** iter 275 RE kickoff (vendoring shopping list + Tier 1/2/3 roadmap).
**Successor:** iter 277 (ImGui::CreateContext + DX9/Win32 Init + minimal panel).

## Headline

**ImGui v1.91.5 vendored + DLL builds clean.** 13 files vendored
(12 + LICENSE.txt) into `swfoc_overlay/imgui/`; `build.bat` extended
with `[3/4] Compiling ImGui...` step + 6 new `.o` files + 3 new
`-l<lib>` flags (`-limm32` / `-ldwmapi` / `-lgdi32`). DLL grew
**270,848 B → 1,035,264 B (+764,416 B = +281%)** — within the iter-275
predicted ~600-800 KB range, slightly over due to imgui_widgets.cpp
size.

| Metric | Value |
|---|---|
| ImGui version | **v1.91.5** (latest stable; cloned via `git clone --depth 1 --branch v1.91.5 https://github.com/ocornut/imgui.git`) |
| Files vendored | **13** (12 source/headers + LICENSE.txt for attribution) |
| Vendored source size | ~2.8 MB on disk (imgui.cpp 880 KB + imgui_widgets 519 KB + imgui.h 377 KB + imgui_internal 272 KB + imgui_tables 249 KB + imgui_draw 241 KB + imstb_truetype 205 KB + 6 smaller files) |
| Build steps | **4** (was 3): MinHook → overlay sources → ImGui (NEW) → link |
| ImGui `.o` files | **6** (imgui_imgui + imgui_draw + imgui_widgets + imgui_tables + imgui_impl_dx9 + imgui_impl_win32) |
| NEW link libs | **3** (`-limm32` / `-ldwmapi` / `-lgdi32`) |
| Compile errors | **0** (clean compile under MinGW-w64 gcc 14.2.0 + C++17) |
| DLL before | 270,848 B (Phase 2-lite) |
| DLL after | **1,035,264 B** (Phase 2-lite + ImGui static link) |
| DLL delta | **+764,416 B** (+281%) |
| Bridge harness | n/a (no bridge changes) — inherits iter-275 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) — inherits iter-275 0/0 at 318 entries |

## What shipped

### `swfoc_overlay/imgui/` directory tree (13 files)

```
swfoc_overlay/imgui/
├── LICENSE.txt                     (1,104 B; MIT — attribution requirement)
├── imconfig.h                      (11,412 B; #define gates for optional features)
├── imgui.cpp                       (879,868 B; core)
├── imgui.h                         (376,674 B; public API)
├── imgui_draw.cpp                  (241,405 B; draw command rendering)
├── imgui_internal.h                (271,906 B; internal API)
├── imgui_tables.cpp                (248,963 B; table widget)
├── imgui_widgets.cpp               (519,217 B; widget library)
├── imstb_rectpack.h                (20,971 B; nothings/stb rect packer)
├── imstb_textedit.h                (58,597 B; nothings/stb text editor)
├── imstb_truetype.h                (204,570 B; nothings/stb TrueType)
└── backends/
    ├── imgui_impl_dx9.cpp          (19,982 B; D3D9 backend)
    ├── imgui_impl_dx9.h            (1,590 B)
    ├── imgui_impl_win32.cpp        (49,118 B; Win32 backend — input + IME + DPI)
    └── imgui_impl_win32.h          (3,566 B)
```

### `swfoc_overlay/build.bat` updates (3 changes)

**1. CPPFLAGS extended with ImGui include paths**:
```diff
-set CPPFLAGS=-O2 -std=c++17 -DWIN32_LEAN_AND_MEAN -I. -I../swfoc_lua_bridge/minhook/include -I../swfoc_lua_bridge
+set CPPFLAGS=-O2 -std=c++17 -DWIN32_LEAN_AND_MEAN -I. -I../swfoc_lua_bridge/minhook/include -I../swfoc_lua_bridge -Iimgui -Iimgui/backends
```

**2. NEW `[3/4] Compiling ImGui...` step**:
```bat
echo [3/4] Compiling ImGui v1.91.5 (vendored Phase 2-full)...
%GPP% -c %CPPFLAGS% imgui/imgui.cpp -o imgui_imgui.o
%GPP% -c %CPPFLAGS% imgui/imgui_draw.cpp -o imgui_draw.o
%GPP% -c %CPPFLAGS% imgui/imgui_widgets.cpp -o imgui_widgets.o
%GPP% -c %CPPFLAGS% imgui/imgui_tables.cpp -o imgui_tables.o
%GPP% -c %CPPFLAGS% imgui/backends/imgui_impl_dx9.cpp -o imgui_impl_dx9.o
%GPP% -c %CPPFLAGS% imgui/backends/imgui_impl_win32.cpp -o imgui_impl_win32.o
```

Step labels also bumped: `[1/3]→[1/4]`, `[2/3]→[2/4]`, `[3/3]→[4/4]`.

**3. Linker line extended with 6 ImGui .o files + 3 new -l<lib> flags**:
```diff
-%GPP% -shared -o swfoc_overlay.dll dllmain.o overlay.o hud_state.o hook.o buffer.o trampoline.o hde64.o -lkernel32 -luser32 -ld3d9 -static -s
+%GPP% -shared -o swfoc_overlay.dll dllmain.o overlay.o hud_state.o hook.o buffer.o trampoline.o hde64.o imgui_imgui.o imgui_draw.o imgui_widgets.o imgui_tables.o imgui_impl_dx9.o imgui_impl_win32.o -lkernel32 -luser32 -ld3d9 -limm32 -ldwmapi -lgdi32 -static -s
```

## What broke + what fixed it

### Mid-iter bug catch: missing `-lgdi32`

First build attempt failed at link step with 3 undefined references
in `imgui_impl_win32.o`:

```
undefined reference to `__imp_GetDeviceCaps'
undefined reference to `__imp_CreateRectRgn'
undefined reference to `__imp_DeleteObject'
```

All three are GDI symbols (high-DPI scaling + IME composition region
management). iter-275 plan listed `-limm32` + `-ldwmapi` (correctly
predicted as needed for IME + DwmIsCompositionEnabled) but missed
`-lgdi32`. Added per build error; second build succeeded.

**Fix latency**: ~15 sec. Recompile from cached `.o` files (only
re-linking) was effectively instant.

**Pattern lesson**: even with comprehensive iter-275 RE kickoff
listing 2 expected `-l<lib>` flags, the 3rd surfaces only at
link-time. The mitigation is iterating quickly: if iter-275 had been
purely speculative without verifying via build, the missed flag
would have stayed undetected.

## Verification gates (ALL GREEN)

| Gate | Result | Δ vs iter-275 |
|---|---|---|
| Overlay build [1/4] MinHook | clean | unchanged |
| Overlay build [2/4] overlay sources | clean | unchanged |
| Overlay build [3/4] ImGui (NEW) | **clean** (6 .o files; 0 errors / 0 warnings) | NEW |
| Overlay build [4/4] link | **clean** after `-lgdi32` fix | NEW |
| Overlay DLL size | **1,035,264 B** | +764,416 B (+281%) |
| Bridge harness | n/a (no bridge changes) | inherits iter-275 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) | inherits iter-275 0/0 at 318 entries |
| Editor full suite | n/a (no editor changes) | inherits iter-275 8177/0/5/8182 |

## Pattern lessons

### Lesson #1 — ImGui vendoring is mostly painless under MinGW-w64

Despite Dear ImGui's documentation focusing on MSVC + Visual Studio
toolchains, vendoring under MinGW-w64 gcc 14.2.0 with C++17 produced
**zero compile errors** and only 1 link error (the `-lgdi32` miss).
The `-DWIN32_LEAN_AND_MEAN` define already in CPPFLAGS didn't conflict
with ImGui's Win32 backend.

**Pattern**: Dear ImGui is genuinely cross-toolchain. Future arcs
vendoring graphics libraries should expect similar low-friction
integration.

### Lesson #2 — Vendored size estimates compound

Iter-275 estimated ~600-800 KB DLL growth. Actual was 764,416 B
(within range, but at the upper end). The estimate was based on
"~10K LoC source × ~70 B/LoC ≈ 700 KB". Reality: imgui_widgets.cpp
alone is 519 KB source which compresses substantially in object
form, but the cumulative effect of imgui core + 2 backends + static
link of 6 `.o` files lands at +281%.

**Pattern**: vendored-library size estimates should use 1× source
size as upper bound for static-linked binaries; iter-275's "<800 KB"
was right but assumed more compression than observed.

### Lesson #3 — `git clone --depth 1 --branch <tag>` is the cleanest vendoring path

Compared to manual tarball download + extract, `git clone --depth 1
--branch v1.91.5` gives:
- Verifiable provenance (release tag).
- Minimum bandwidth (depth 1).
- No tarball staging step.
- Easy to re-vendor for ImGui upgrades (just `cd /tmp/imgui && git
  fetch --tags && git checkout v1.92.0` then re-copy).

This iter took ~2 min for clone+copy+vendor, demonstrating the path
is fast enough to use in autonomous loops without disrupting cadence.

## What's next (iter 277-279)

Per iter-275 design doc:

1. **Iter 277**: ImGui::CreateContext + DX9 Init + Win32 Init;
   `Present_Detour` wraps each frame in `ImGui_ImplDX9_NewFrame` +
   `ImGui_ImplWin32_NewFrame` + `ImGui::NewFrame` / `ImGui::Render`
   / `ImGui_ImplDX9_RenderDrawData`. Render minimal `ImGui::Begin /
   ImGui::Text / ImGui::End` panel.

2. **Iter 278**: Replace empty panel with 4-row HUD strip rendered
   via ImGui Tables + ProgressBar widgets, consuming the existing
   `HudSnapshot` model unchanged.

3. **Iter 279**: Tier 2 content (catalog rollup + multipliers +
   faction tinting) + live verify + close-out.

**Risk for iter 277**: MEDIUM — DX9 backend is stateful and could
conflict with Phase 2-lite render-state save/restore. Mitigation:
ImGui DX9 backend's `ImGui_ImplDX9_RenderDrawData` is documented as
self-contained; keep Phase 2-lite save/restore intact and verify
via DebugView log that frame counter still increments.

## Iter 276 close-out summary

- This document is the iter 276 deliverable.
- **Code changes**: 13 files vendored (12 ImGui + LICENSE.txt) +
  build.bat updated (~25 lines edited across 3 sections).
- All gates GREEN: build clean (4/4 steps); DLL size 1,035,264 B
  (+281% vs iter-275); bridge harness + ledger lint inherit iter-275
  unchanged.
- **Iter 2 of 5-iter Thread B Phase 2-full arc** (iter 275-279).
- 6th consecutive NON-A1.x iter per iter-269 lesson #2 (iter-271 +
  iter-272 + iter-273 + iter-274 + iter-275 + iter-276).
- 109 → 109 buttons UNCHANGED. 106 → 106 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25
  unchanged.
- **Pattern lesson capstone**: iter-275 RE kickoff predicted 2 of 3
  needed link-libs; the 3rd (`-lgdi32`) surfaced at iter-276 link.
  Even comprehensive design docs miss platform details that only
  emerge at build time. Mitigation: iterate quickly when build
  fails — fix latency was ~15 sec.
- **Session-cumulative this conversation (iter 159-276)**: +99 LIVE
  wire/sub-field flips + 10 helpers + 34 operator-facing improvements
  + 12 docs iters + 7 audit/audit-followup iters + 1 memory
  codification iter + 3 preset-menu refresh iters + 9 RE kickoff
  iters + **6 RE-implementation iters** (was 5; iter 276 NEW Thread B
  vendoring) + 5 simulator iters + 3 native UX iters + 2 staging-UI
  verification iters + **12 close-out iters** (was 11; iter 276 NEW)
  + 4 ledger updates + 9 stale-count drift fixes + 1
  wire-format-canonical alignment + 3 honest-defer arc closures + 3
  audit-iter rationale drift catches + 1 cross-reference pin test +
  3 README capstone updates + 3 reverse-orphan audit clean passes +
  1 memory rule codification + 6 surface report regens + 1
  multi-iter arc finale capstone + 1 mid-iter dual-drift catch + **1
  NEW arc-class implementation start** (Thread B iter 276 vendoring;
  first iter 159+ that touches `swfoc_overlay/` source) across **118
  iters**.
