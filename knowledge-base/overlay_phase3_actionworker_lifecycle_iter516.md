# Overlay Phase 3 — action-worker lifecycle wired into the DLL (iter 516)

**Date**: 2026-05-21
**Hat**: `overlay-interactive`
**Spec**: `.ralph/specs/overlay-interactive.md` — Phase 3 (interactive widgets)
**Predecessors**: iter-512 (widget skeleton) → iter-513 (`overlay_action_queue.h`) →
iter-514 (host WndProc detour) → iter-515 (`overlay_action_worker.h` drain loop) →
**iter-516 (this doc — backend integration)**

## What shipped

iter-515 shipped the last *pure, unit-tested* layer of the Phase 3 action
pipeline: `overlay_action_worker.h::RunActionWorkerLoop`, the drain loop a
background thread runs. iter-516 is the **DLL-side glue** that the header-only
unit test deliberately cannot exercise — it spawns the real thread, binds the
real blocking bridge send, owns the one process-wide queue, and hooks all of
that into the overlay's `Install` / `Uninstall` lifecycle.

This iter was a **phantom-partial recovery**: an interrupted earlier iteration
had already written `overlay_action_worker.cpp` and extracted `BridgeProbe`
into `namespace swfoc_overlay` (`hud_state.cpp` / `hud_state.h`), but never
wired the new TU into `build.bat`, never called the lifecycle from
`overlay.cpp`, and never rebuilt the DLL. iter-516 finished that wiring.

### Files

- **`overlay_action_worker.cpp`** (pre-existing on disk, unchanged this iter):
  `ActionQueueInstance()` (the single process-wide `ActionQueue`, a magic-static),
  `StartActionWorker()` / `StopActionWorker()` (spawn / join one background
  `std::thread` running `RunActionWorkerLoop`). Idempotent `joinable()` guards;
  sliced `Sleep` (200 ms interval / 50 ms slices) so a mid-pause shutdown is
  honoured within one slice — shape mirrors `hud_state.cpp`'s
  `StartHudWorker` / `StopHudWorker` exactly.
- **`hud_state.cpp` / `hud_state.h`** (pre-existing on disk, unchanged this iter):
  `BridgeProbe` promoted from a file-local `namespace {}` helper to a public
  `namespace swfoc_overlay` symbol declared in `hud_state.h`. The HUD read-probe
  worker (`BuildSnapshot`) and the Phase 3 action worker now share **one**
  blocking named-pipe round-trip implementation — no duplicated pipe code. A
  `using swfoc_overlay::BridgeProbe;` in the anonymous namespace keeps
  `BuildSnapshot`'s ~10 call sites unqualified and untouched.
- **`build.bat`** (EDIT this iter): added `overlay_action_worker.cpp` →
  `overlay_action_worker.o` to step `[2/4]`, and `overlay_action_worker.o` to
  the `[4/4]` link object list. This is the first time `overlay_action_queue.h`
  + `overlay_action_worker.h` are compiled into a DLL TU.
- **`overlay.cpp`** (EDIT this iter): `#include "overlay_action_worker.h"`;
  `Install()` calls `StartActionWorker()` after `StartHudWorker()`; `Uninstall()`
  calls `StopActionWorker()` before `StopHudWorker()` (reverse of the start
  order) and before the D3D9 hooks are torn down, so an in-flight bridge
  round-trip drains cleanly.

## Lifecycle order

| Phase | Order |
|---|---|
| `Install()` | MinHook init → vtable harvest → create/enable hooks → hotkey thread → `StartHudWorker()` → **`StartActionWorker()`** |
| `Uninstall()` | **`StopActionWorker()`** → `StopHudWorker()` → `ShutdownImGui()` → hotkey thread join → `MH_DisableHook` → `MH_Uninitialize` |

Both worker threads are stopped (and joined) **before** the hooks come down —
neither thread can be mid-`BridgeProbe` against a freed handle, and the action
worker's `RunActionWorkerLoop` CHECK-FIRST contract (iter-515) means a stop
requested before its first tick issues zero pipe I/O into a dying process.

## Verification

- `build.bat` → **OVERLAY BUILD SUCCESS**, exit 0, no compiler diagnostics.
- DLL **1,063,936 B** — up from iter-515's 1,051,136 B (**+12,800 B**). Sane
  delta: `overlay_action_worker.cpp` is the first DLL TU to instantiate
  `std::thread` / `std::atomic<bool>` / `std::deque<ActionRequest>` /
  `std::function` / `std::mutex` for the action pipeline.
- `build_action_worker_test.bat` → **20/20 pass**, exit 0 — the pure layer
  (`RunActionWorkerLoop` / `ActionQueue`) is untouched, confirmed no regression.
- Bridge harness / ledger lint / editor suite — not touched, gates unaffected.

This iter is **build-only verifiable** for the lifecycle itself: the
`std::thread` spawn/join + cross-thread `BridgeProbe` runtime behaviour cannot
be unit-asserted without the live game. The drain loop's correctness is already
pinned by iter-515's 20-check red-green suite; iter-516 only connects it.

## Next

iter-517 = UI wiring: `RenderActionsWindow` un-disables the 3 Phase 3 action
buttons, wires each `onClick` → `ActionQueueInstance().Enqueue(...)` with the
Lua line built by `overlay_actions.h`, and adds a footer toast driven by
`ActionQueueInstance().LatestResult()` (LIVE / FAILED / Pending badge per the
operator-trust pattern, guardrail 1007).
