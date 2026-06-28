# Overlay Interactive — Phase 3 host WndProc detour (iter 514)

**Date:** 2026-05-21
**Spec:** `.ralph/specs/overlay-interactive.md` (Phase 3 — interactive widgets)
**Hat:** `overlay-interactive`
**Predecessor:** `overlay_phase3_actionqueue_iter513.md`

## What shipped

iter-512 shipped the Phase 3 "Actions" widget skeleton; iter-513 shipped the
non-blocking `ActionQueue` dispatch foundation. Both left a hard gap: the
overlay never detoured the host `WndProc`, so ImGui received **no mouse or
keyboard input** — the widgets were inert (`ImGui::BeginDisabled(true)`).
iter-514 closes that gap.

| File | Change |
|---|---|
| `swfoc_overlay/overlay_input.h` | **NEW** — dependency-free header. `IsMouseMessage` / `IsKeyboardMessage` classify Win32 `WM_*` ids; `ShouldSwallowMessage` is the pure three-gate WndProc-detour forwarding decision. |
| `swfoc_overlay/overlay_input_test.cpp` | **NEW** — standalone unit test, 29 checks incl. 3 red-green regression PINs. No `<windows.h>`, no game, no window. |
| `swfoc_overlay/build_input_test.bat` | **NEW** — compile + run the unit test (`-Wall -Wextra -Werror`; no `-pthread`; CRLF endings). |
| `swfoc_overlay/overlay.cpp` | **EDIT** — `HookedWndProc` subclass; install in `EnsureImGuiInit` / restore in `ShutdownImGui`; `RenderActionsWindow` un-disables the Faction / Unit-type / Position controls (action buttons stay disabled). |

## Why a WndProc detour — and the input-bleed hazard

The overlay shares **one window** with the host game. Phase 1-2 deliberately
never touched the host `WndProc` (F1 is polled from a worker thread) so it
could not steal input. Phase 3's interactive widgets need that input, so
`overlay.cpp` now subclasses the window procedure via `SetWindowLongPtrW`.

A WndProc detour bleeds input in **both** directions if the forwarding rule
is wrong:

- **forward too much** → a click on an overlay button *also* commands the
  game (orders the selected unit to move to the click point).
- **forward too little** → the game stops responding to the mouse whenever
  the overlay is on screen, even over empty space.

`ShouldSwallowMessage` is the pure decision that prevents both. It swallows a
message (consumes it, does not forward to the game) only when **all three**
gates hold:

1. **overlay visible** — a hidden overlay never steals input; F1-off plays
   the game normally.
2. **ImGui wants that input class** — `io.WantCaptureMouse` /
   `io.WantCaptureKeyboard`. Off a widget the click belongs to the game even
   with the overlay visible.
3. **message class matches the want** — a mouse-want only swallows mouse
   messages; non-input messages (`WM_PAINT`, `WM_SIZE`, ...) always pass
   through so the game keeps drawing and resizing.

`HookedWndProc` always calls `ImGui_ImplWin32_WndProcHandler` first (so ImGui
tracks mouse position / focus / key state), then applies the rule. The
handler's return value is **intentionally ignored**: an injected overlay
decides forwarding from `WantCapture*`, not from the handler's verdict — the
opposite of the stock ImGui Win32 example, which owns its window and may
`return TRUE` freely.

`overlay_input.h` is dependency-free (no `<windows.h>`, no ImGui) so the rule
is unit-tested with a plain g++ — mirroring `overlay_action_queue.h` /
`overlay_actions.h`. The `WM_*` ids it needs are a frozen Win32 ABI; the
header mirrors the two contiguous ranges with a comment.

## Lifecycle

- **Install** — `EnsureImGuiInit` calls `SetWindowLongPtrW(GWLP_WNDPROC)`
  once, *before* publishing `g_imguiInitialized`, capturing the game's
  original procedure in `g_origWndProc`. `HookedWndProc` gates on the same
  flag, so a message arriving in the install gap forwards straight through.
- **Restore** — `ShutdownImGui` restores the original procedure **before**
  tearing down the ImGui context (`HookedWndProc` dereferences `GetIO`), so
  no message can reach a destroyed context. Defensive fallback to
  `DefWindowProcW` if `g_origWndProc` is ever null.

The F1 visibility toggle is unaffected — it is polled via `GetAsyncKeyState`
on a worker thread, independent of window-message routing.

## Verification

- `build_input_test.bat` → **29 checks, 0 failures, exit 0** (compiles clean
  under `-Wall -Wextra -Werror`).
- `build.bat` → **OVERLAY BUILD SUCCESS**, exit 0; `swfoc_overlay.dll`
  **1,051,136 B** (+512 B vs iter-513's 1,050,624 B — sane delta for the
  WndProc detour code added to `overlay.cpp`).
- Bridge harness + ledger lint + editor tests: untouched surface, gates
  unaffected.

## Honest defer / next (Phase 3 continuation — for iter-515)

The three action **buttons** stay inside `BeginDisabled` this iter: they have
no `onClick` wiring yet, and a button that looks live but issues nothing
violates the operator-trust pattern (guardrail 1007). iter-515:

1. Expose `BridgeProbe` from `hud_state.cpp` (add a declaration to
   `hud_state.h`, or a thin `RunBridgeLine(lua, resp)` wrapper) — iter-513's
   `BridgeSendFn` plugs straight into it.
2. Add an `ActionQueue` instance + a drain worker thread; wire the three
   button `onClick` handlers to `Enqueue()` and un-disable them.
3. Footer toast rendering the latest `ActionResult` (LIVE / FAILED / PENDING
   badge) so the operator sees the bridge outcome.
