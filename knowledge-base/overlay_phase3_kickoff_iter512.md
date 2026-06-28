# Overlay Interactive — Phase 3 kickoff (iter 512)

**Date:** 2026-05-21
**Spec:** `.ralph/specs/overlay-interactive.md` (maps to spec iter-chain row "iter-287")
**Hat:** `overlay-interactive`

## What shipped

First interactive-overlay iter. Phases 1-2 (read-only HUD, iter 277-285) are
done; this iter starts Phase 3 — interactive widgets.

| File | Change |
|---|---|
| `swfoc_overlay/overlay_actions.h` | **NEW** — pure header-only bridge-command builders: `LuaQuote`, `FormatCoord`, `BuildSpawnUnitCommand`, `BuildMakeUnitInvulnCommand`, `BuildKillUnitCommand`. |
| `swfoc_overlay/overlay_actions_test.cpp` | **NEW** — standalone C++ unit test, 19 checks incl. red-green escaping pins. |
| `swfoc_overlay/build_actions_test.bat` | **NEW** — compile + run the unit test. |
| `swfoc_overlay/overlay.cpp` | `#include "overlay_actions.h"`; new `RenderActionsWindow()` — the Phase 3 "Actions" widget skeleton (faction + unit-type Combos, position InputFloat3, Spawn / Make Invuln / Kill buttons) rendered in its own window with a live Spawn-command preview. |
| `swfoc_overlay/build.bat` | Compiler resolution fix (see Toolchain note). |

## Why a command-builder layer

The `SWFOC_*Lua` write wires take **Lua expression strings** — each argument
is a Lua string literal whose contents are themselves a Lua expression, so
inner `"` must become `\"`. The overlay's `BridgeProbe` sends one Lua line
per call, so the builders also neutralise embedded newlines. This escaping is
pure and deterministic, so it is unit-tested before the render path depends
on it. Verified wire signatures (`swfoc_lua_bridge/lua_bridge.cpp`):

- `SWFOC_SpawnUnitLua(player_expr, type_expr, position_expr)`
- `SWFOC_MakeUnitInvulnLua(unit_lua_expr, bool_lua_expr)`
- `SWFOC_KillUnit(obj_addr)` — numeric address

## Infra-claim drift caught

The overlay-interactive spec named the invuln wire `SWFOC_MakeInvulnerableLua`.
That wire **does not exist**. The real registered wire is
`SWFOC_MakeUnitInvulnLua` (`lua_bridge.cpp:8372`). The builder + test use the
correct name. Grep-before-build prevented a dead-on-arrival call.

## Honest defer — input wiring

The overlay does **not** detour the host `WndProc`, so ImGui receives no
mouse/keyboard input. The Phase 3 widgets therefore render but are wrapped in
`ImGui::BeginDisabled()` this iter — a visible, reviewable skeleton. The
Spawn-command preview line IS live (it runs the real builder every frame).
The next overlay iter adds the `WndProc` detour and un-disables the widgets,
wiring each button's onClick to `BridgeProbe`.

## Toolchain note

The MinGW toolchain now lives at `C:\Program Files\CodeBlocks\MinGW\bin` — a
path with a space. A bare `x86_64-w64-mingw32-g++` invocation works for
`--version` but fails sub-tool (cc1plus/as) spawning. `build.bat` and
`build_actions_test.bat` now resolve the **full compiler path** via `where`
and invoke it quoted. Both bat files also require CRLF line endings (cmd.exe
mis-parses LF-only batch files).

## Verification

- `build_actions_test.bat` → 19 checks, 0 failures, exit 0.
- `build.bat` → exit 0; `swfoc_overlay.dll` 1,040,384 → **1,050,624 B** (+10,240).
- `overlay.cpp` compiles `-Wall -Wextra` clean.
- Bridge harness + ledger lint: untouched surface, gates unaffected.

## Next (Phase 3 continuation)

1. `WndProc` detour → ImGui input capture; un-disable the Actions widgets;
   wire button onClick → `BridgeProbe(BuildXxxCommand(...))` with a
   LIVE/FAILED result toast.
2. Recent-actions toolbar (5-slot ring buffer).
3. Teleport + faction-switch widgets; shared `SelectedUnitLuaExpr` field.
4. Phase 3 close-out doc + capability badges.
