# Overlay Phase 3 — action-worker drain loop (iter-515)

**Spec**: `.ralph/specs/overlay-interactive.md` — Phase 3 (interactive widgets).
**Hat**: `overlay-interactive`.
**Date**: 2026-05-21.

## What shipped

The pure, unit-tested **drain-loop layer** for the Phase 3 action pipeline:

- NEW `swfoc_overlay/overlay_action_worker.h` — header-only
  `RunActionWorkerLoop(ActionQueue&, BridgeSendFn, ShouldStopFn, WorkerSleepFn)`.
  Dependency-free beyond `overlay_action_queue.h` + `<functional>` (no
  `<windows.h>`, no `<thread>`, no ImGui, no bridge).
- NEW `swfoc_overlay/overlay_action_worker_test.cpp` — 20 checks, 2 red-green
  PINs (CHECK-FIRST, NO-SLEEP-ON-STOP).
- NEW `swfoc_overlay/build_action_worker_test.bat` — `-Wall -Wextra -Werror
  -static -pthread`; mirrors `build_action_queue_test.bat`.

## Why a separate, tested loop layer

The Phase 3 pipeline now has all three of its pure layers shipped and tested:

| Iter | Pure header | What it pins |
|---|---|---|
| 512 | `overlay_actions.h` | Lua-command escaping (`overlay_actions_test.cpp`) |
| 513 | `overlay_action_queue.h` | thread-safe FIFO + latest-result (`..._queue_test.cpp`) |
| 514 | `overlay_input.h` | WndProc swallow decision (`overlay_input_test.cpp`) |
| **515** | **`overlay_action_worker.h`** | **drain-loop ordering** (`..._worker_test.cpp`) |

The drain loop has two ordering behaviours that are subtle and easy to
"simplify" into bugs, so they were extracted into a pure function rather than
left as untested glue in the (build-only) `.cpp`:

1. **CHECK-FIRST** — `shouldStop()` is polled BEFORE the first `Drain()`. A
   `do/while` (drain-then-check) loop would fire one last bridge call into a
   possibly-dead pipe during DLL teardown. PIN: enqueue a request, stop the
   loop before its first tick, assert the send was never called.
2. **NO-SLEEP-ON-STOP** — after each drain the loop re-checks `shouldStop()`
   before sleeping, so a shutdown requested mid-tick is honoured immediately
   instead of after a full sleep interval. PIN: stop the loop right after a
   drain, assert zero sleeps.

Defensive on the injected callables: empty `shouldStop` → the loop refuses to
run (a missing stop signal would otherwise spin forever); empty `send` →
`ActionQueue::Drain` already marks each request `Failed`; empty `sleep` → the
inter-drain pause is skipped.

## Verification

- `build_action_worker_test.bat` → **20/20 pass, exit 0**, clean
  `-Wall -Wextra -Werror`.
- `build.bat` → OVERLAY BUILD SUCCESS, DLL **1,051,136 B** — byte-identical to
  iter-514; no DLL translation unit was touched (`overlay_action_worker.h` is
  not yet `#include`d by any DLL TU, exactly like `overlay_action_queue.h` was
  after iter-513).
- Bridge harness / ledger lint / editor test suite — untouched, gates
  unaffected.

## Next overlay iter (iter-516)

Integrate the backend, build-only surface:

1. Expose `BridgeProbe` from `hud_state.cpp` as a public
   `swfoc_overlay` send function (the real `BridgeSendFn`).
2. NEW `overlay_action_worker.cpp` — `ActionQueueInstance()` accessor +
   `StartActionWorker()` / `StopActionWorker()` spawning an `std::thread` that
   runs `RunActionWorkerLoop` with the real send, a sliced `Sleep`, and the
   shutdown atomic.
3. Add `overlay_action_worker.cpp` to `build.bat` (compile + link list).
4. Wire `StartActionWorker` / `StopActionWorker` into `Install()` /
   `Uninstall()` (order: HUD worker → action worker → ImGui → hooks).

Then iter-517 = UI wiring: the 3 Phase 3 buttons' `onClick` → `Enqueue`,
un-disable them, and a footer toast reading `ActionQueue::LatestResult()`.
