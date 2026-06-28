# Overlay Interactive — Phase 3 action-dispatch queue (iter 513)

**Date:** 2026-05-21
**Spec:** `.ralph/specs/overlay-interactive.md` (Phase 3 — interactive widgets)
**Hat:** `overlay-interactive`
**Predecessor:** `overlay_phase3_kickoff_iter512.md`

## What shipped

iter-512 shipped the Phase 3 "Actions" widget skeleton (Spawn / Make-Invuln /
Kill) — rendered but wrapped in `ImGui::BeginDisabled()` because the overlay
does not yet detour the host `WndProc`. iter-513 ships the missing safe-dispatch
foundation: a thread-safe, non-blocking action queue.

| File | Change |
|---|---|
| `swfoc_overlay/overlay_action_queue.h` | **NEW** — header-only `ActionQueue` class: thread-safe FIFO of `ActionRequest` + latest-`ActionResult` store + `Drain()` worker entry-point. `ActionStatus` enum (Idle / Pending / Live / Failed) mirrors the editor's operator-trust badge vocabulary. |
| `swfoc_overlay/overlay_action_queue_test.cpp` | **NEW** — standalone C++ unit test, 22 checks incl. a FIFO-order red-green regression pin. Injects a fake send-function — no bridge, no game. |
| `swfoc_overlay/build_action_queue_test.bat` | **NEW** — compile + run the unit test (`-static -pthread` for the `std::mutex` runtime; CRLF endings). |

## Why a queue — the non-blocking guarantee

The Phase 3 buttons must call the bridge, but `BridgeProbe` (`hud_state.cpp`)
does **blocking** named-pipe I/O — `CreateFile` + `WriteFile` + `ReadFile`.
A button's `onClick` handler runs on the **render thread**, inside the D3D9
`Present` detour. Calling `BridgeProbe` there would freeze the host game's
frame loop whenever the bridge is slow or stalled — the exact hazard the
overlay deliberately avoided in Phase 1 (it polls F1 from a worker thread,
never the host's window message pump).

`ActionQueue` keeps that guarantee:

- **Render thread** only ever `Enqueue()`s a request and reads `LatestResult()`
  — both are short lock-guarded in-memory operations.
- **Worker thread** calls `Drain(send)`; `Drain` pops each request under the
  lock, **releases the lock across the (blocking) send**, then re-locks to
  publish the result. The render thread is never blocked by bridge I/O, even
  mid-dispatch.

The send-function is injected (`BridgeSendFn` =
`std::function<bool(const std::string&, std::string&)>`), so the queue is
fully unit-testable with a fake send and `BridgeProbe` stays decoupled.

## Verification

- `build_action_queue_test.bat` → **22 checks, 0 failures, exit 0** (compiles
  clean under `-Wall -Wextra -Werror`).
- `build.bat` → **OVERLAY BUILD SUCCESS**; `swfoc_overlay.dll` 1,050,624 B —
  unchanged (iter-513 adds only a not-yet-included header + a standalone test;
  no DLL translation unit touched).
- Bridge harness + ledger lint: untouched surface, gates unaffected.

## Next (Phase 3 continuation — for iter-514)

1. Detour the host `WndProc` (`SetWindowLongPtr` + `ImGui_ImplWin32_WndProcHandler`
   + `CallWindowProc`) so ImGui receives input; remove the `BeginDisabled(true)`
   wrapper from `RenderActionsWindow()`.
2. Expose `BridgeProbe` from `hud_state.cpp` (add a declaration to
   `hud_state.h`, or a thin `RunBridgeLine(lua, resp)` wrapper) — iter-513's
   `BridgeSendFn` plugs straight into it.
3. Start an action-drain worker thread in `Install()` (mirror
   `StartHudWorker` / `StopHudWorker`); wire each button `onClick` to
   `ActionQueue::Enqueue`; render `LatestResult()` as a footer toast badge.
