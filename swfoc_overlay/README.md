# swfoc_overlay — In-Game Overlay (Phase 1)

In-game overlay DLL for SWFOC, designed to coexist with `powrprof.dll` (the
existing Lua bridge) and provide a cleaner operator-facing surface than the
desktop V2 editor. See `knowledge-base/overlay_design_2026-04-27.md` for the
full architecture rationale.

## Phase status (2026-04-27)

### Phase 1 (iter 31) — DONE

Buildable skeleton:
- D3D9 vtable harvest + MinHook detours of `Present` + `Reset`.
- F1 hotkey via worker-thread polling (no WndProc detour, no host-message-pump deadlock risk).
- `IsVisible / SetVisible / ToggleVisible` API.
- "Present frame=N visible=N" log to DebugView every 600 frames.

### Phase 2-lite (iter 43) — DONE

Minimum-viable visible render: an **amber badge** bottom-right when the
overlay is toggled on. Proves the detour can paint geometry over the game
without dependence on ImGui.

- 160×32 amber rectangle (matches editor's `WarningForeground` brand colour).
- ~70% alpha so the game stays partly visible underneath.
- Pre-transformed-vertex format (`D3DFVF_XYZRHW | D3DFVF_DIFFUSE`) — no
  projection matrix setup needed.
- Saves + restores all touched render state (alpha blend, Z-enable, cull
  mode, lighting, FVF) so we don't poison the host's subsequent draws.
- ~60 LoC added; no new dependencies.

### Phase 2-full — NEXT

Vendor ImGui (Dear ImGui core + DX9 backend + Win32 backend) and
replace the rectangle with a real panel showing operator-relevant state
(local player, alive units, current planet, hotkeys). ~10-15 vendored
files; tracked separately because of the size.

### Phase 6 — DEFERRED

Editor↔overlay IPC (second named pipe). The overlay can already call
the existing bridge directly in-process; the editor pipe is only needed
when overlay actions should reflect in the desktop UI in real-time.

## Build

Requires MinGW-w64 (`x86_64-w64-mingw32-g++`), same toolchain the bridge uses.

```cmd
cd swfoc_overlay
build.bat
```

Output: `swfoc_overlay.dll` (~80 KB, statically linked, no DLL deps beyond
`d3d9.dll` / `kernel32.dll` / `user32.dll`).

## Install

The DLL needs the OS loader to pick it up next to `StarWarsG.exe`. The bridge
already takes `powrprof.dll`, so the overlay needs a *different* shim name.
Candidates (any DLL the game's import table actually references):

- `dwmapi.dll` — Desktop Window Manager API. Common; safe.
- `dxva2.dll` — DirectX video acceleration. Safe if game doesn't use video.
- `version.dll` — version-resource API. Almost universal.

Pick one, rename `swfoc_overlay.dll` → `<chosen>.dll`, drop next to
`StarWarsG.exe`. Final pick deferred to install testing once the
`StarWarsG.exe` import table has been audited.

## Verify Phase 1 + 2-lite

1. Launch SWFOC.
2. Alt-tab to **DebugView** (Sysinternals).
3. Look for lines like:
   ```
   [swfoc_overlay] Install OK — F1 toggles visibility
   [swfoc_overlay] Present frame=0 visible=0
   [swfoc_overlay] Present frame=600 visible=0
   ```
4. Press **F1** in-game. The next frame log should show `visible=1` AND
   an amber rectangle should appear in the bottom-right of the screen.
5. Press F1 again — the rectangle disappears, log shows `visible=0`.

If both behaviours match, Phase 1 + 2-lite are green. The hook fires every
frame, visibility state toggles correctly, and the render path can paint
geometry over the host's swap chain without breaking subsequent draws.

## File map

| File | Purpose |
|---|---|
| `dllmain.cpp` | DLL entry point, spawns bootstrap worker. |
| `overlay.h` | Public surface (`Install/Uninstall/IsVisible/...`). |
| `overlay.cpp` | D3D9 vtable harvest + MinHook detours + F1 poller. |
| `build.bat` | MinGW-w64 build pipeline. |
| `README.md` | This file. |

## Next: Phase 2-full (ImGui)

Replace the Phase 2-lite rectangle with a vendored ImGui panel.

Vendoring shopping list (~15 files):
- `imgui.cpp` + `imgui.h`
- `imgui_draw.cpp` + `imgui_widgets.cpp` + `imgui_tables.cpp`
- `imgui_internal.h` + `imconfig.h`
- `imstb_rectpack.h` + `imstb_textedit.h` + `imstb_truetype.h`
- `backends/imgui_impl_dx9.cpp` + `imgui_impl_dx9.h`
- `backends/imgui_impl_win32.cpp` + `imgui_impl_win32.h`

Done line: a panel saying "Hello, operator" with the current frame counter,
visibility toggle indicator, and a "Phase 2-full landed at iter X" footer.

Phase 2-full → Phase 3: HUD reading live state from the already-loaded
`powrprof.dll` (in-process Lua, no IPC). Phase 3 → Phase 4: drag-to-spawn
(camera projection matrix RE pin required first). Phase 4 → Phase 7:
aesthetic polish + faction-tinted chrome.
