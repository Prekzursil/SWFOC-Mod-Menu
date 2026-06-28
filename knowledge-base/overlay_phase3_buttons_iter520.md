# Overlay Phase 3 — action buttons wired LIVE (iter-520)

**Date**: 2026-05-21
**Hat**: overlay-interactive
**Spec**: `.ralph/specs/overlay-interactive.md` (Phase 3 — interactive widgets)
**Predecessors**: iter-512 (skeleton) → iter-513 (ActionQueue) → iter-514 (WndProc
detour + `overlay_input.h`) → iter-515 (drain loop `overlay_action_worker.h`) →
iter-516/519 (action-worker lifecycle wired into the DLL).

## What shipped

`RenderActionsWindow` in `swfoc_overlay/overlay.cpp` — the three Phase 3 action
buttons (**Spawn / Make Invuln / Kill**) are no longer wrapped in a blanket
`BeginDisabled(true)` block. Each button's `onClick` now builds a Lua line via
`overlay_actions.h` and `Enqueue()`s an `ActionRequest` onto the process-wide
`ActionQueueInstance()` (`overlay_action_worker.cpp`). A new footer toast
(`RenderActionToast`) reads `LatestResult()` once per frame.

### The action pipeline, end to end

```
button onClick (render thread)
  → BuildXxxCommand(...)            overlay_actions.h   — pure, tested
  → ActionQueueInstance().Enqueue() overlay_action_queue.h — pure, tested
        ... latest result = Pending immediately ...
  → RunActionWorkerLoop drains it   overlay_action_worker.{h,cpp} — pure loop tested
  → BridgeProbe(lua, response)      hud_state.cpp — blocking named-pipe I/O
        ... latest result = Live / Failed ...
footer toast (render thread, next frame)
  → ActionQueueInstance().LatestResult()  — short lock-guarded copy
```

The render thread only ever **enqueues** and **reads the latest result** — both
short lock-guarded in-memory ops. The blocking bridge round-trip runs on the
background action worker, never inside the D3D9 Present detour. This is the
hazard `overlay_action_queue.h` was built to prevent.

### Button gating (operator-trust pattern — guardrail 1007)

| Button | Gate | Target expression |
|---|---|---|
| **Spawn** | always enabled | `BuildSpawnUnitCommand(faction, unitType, x,y,z)` |
| **Make Invuln** | always enabled | `BuildMakeUnitInvulnCommand("Find_First_Object(\"<unitType>\")", true)` |
| **Kill** | disabled until hex field parses non-zero | `BuildKillUnitCommand(addr)` |

- **Make Invuln** targets `Find_First_Object` of the selected unit type. Phase 5
  click-to-select will promote this to an inspected unit handle.
- **Kill** is address-gated. `SWFOC_KillUnit` takes a **numeric object address**
  and the overlay has no in-game unit picker before Phase 5, so the operator
  types/pastes a hex address (from the editor Inspector tab or a Cheat Engine
  scan) into a new `ImGui::InputText` with `ImGuiInputTextFlags_CharsHexadecimal`
  (filters to `0-9a-fA-F`). The button stays disabled while the field parses to
  `0` — a Kill that targets nothing must not look dispatchable.
  - `CharsHexadecimal` strips a pasted `0x` prefix to a leading `0`, which is
    value-preserving for `strtoull(..., 16)`, so `0x1A2B3C` pasted still kills
    the right pointer.

### Footer toast — `RenderActionToast`

Renders `ActionQueueInstance().LatestResult()` with the operator-trust badge
vocabulary, color-matched to the existing overlay palette:

| `ActionStatus` | Badge | Color |
|---|---|---|
| `Idle` | `No action dispatched yet.` | TextDisabled |
| `Pending` | `PENDING: <label>` | amber `(1.0, 0.706, 0.0)` |
| `Live` | `LIVE: <label>` + bridge response | green `(0.13, 0.67, 0.13)` |
| `Failed` | `FAILED: <label>` + failure reason | red `(0.87, 0.20, 0.13)` |

`Enqueue()` flips the latest result to `Pending` synchronously on the click, so
the toast updates the instant the operator clicks — before the worker runs the
bridge call. The operator can never confuse "I clicked" with "engine changed".

## Verification

- `swfoc_overlay/build.bat` → **OVERLAY BUILD SUCCESS**, exit 0, zero compiler
  diagnostics. DLL **1,072,128 B** (+8,192 B vs iter-519's 1,063,936 B — sane
  delta for the new `RenderActionToast` + the expanded `RenderActionsWindow`
  onClick handlers + `std::string` concatenations).
- `build_actions_test.bat` → **19/19 pass** — the Lua builders the buttons call
  are confirmed green (`overlay_actions.h` untouched this iter; re-run as
  evidence).
- Bridge harness / ledger lint / editor test suite — untouched surface, those
  gates unaffected.

## Build-only scope

`RenderActionsWindow` / `RenderActionToast` are ImGui render glue — not unit-
testable without an ImGui mock. Every pure piece the wiring depends on is
already tested in isolation: the Lua builders (`overlay_actions_test.cpp`), the
FIFO `ActionQueue` (`overlay_action_queue_test.cpp`), and the drain loop
(`overlay_action_worker_test.cpp`). The runtime path (onClick → enqueue → drain
→ BridgeProbe → toast) needs the live game and is build-only verifiable, exactly
as iter-512 (skeleton) and iter-514/516/519 (WndProc + lifecycle) were.

## Next (Phase 3 remaining)

- Recent-actions toolbar (5-slot ring buffer of last SWFOC calls, click-to-
  re-fire) — `overlay_recent_actions.h` (NEW). Top quick win per agent #2.
- Teleport-selected + Faction-switch buttons (`SWFOC_ChangeUnitOwner`).
- Phase 3 close-out: per-widget capability badge + close-out doc.
