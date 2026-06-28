# Overlay Phase 3 — Recent-actions toolbar (iter 521)

**Hat**: `overlay-interactive` · **Spec**: `.ralph/specs/overlay-interactive.md` (line 35,
iter-289 row) · **Date**: 2026-05-21

## What shipped

The Phase 3 **recent-actions toolbar** — a "re-execute the last 5 SWFOC calls"
quick win flagged by the agent #2 overlay UX research
(`overlay_ux_research_2026-05-08.md`; spec line 35). The operator can re-fire a
recent Spawn / Make-Invuln / Kill without re-typing the unit type or hex
address.

### Component (authored earlier; verified this iter)

`swfoc_overlay/overlay_recent_actions.h` — pure, header-only, std-only. Class
`RecentActions`: a bounded most-recent-first history of dispatched
`ActionRequest`s (reuses the struct from `overlay_action_queue.h` — no new
type). Recent-FILES-list semantics, not a raw FIFO ring:

- `Record()` front-inserts; index 0 is always newest.
- Recording an `ActionRequest` whose **`lua` line** already exists **promotes**
  that slot to the front instead of duplicating it (`lua`, not `label`, is the
  identity — newer label wins). A raw ring would show "Spawn X" five times if
  the operator spammed it; dedup-promote keeps the 5 slots showing 5 *distinct*
  actions.
- A genuinely new action at full capacity (`kCapacity = 5`) evicts the oldest;
  a promote never evicts (size unchanged).
- `Record()` copies its argument first, because the toolbar's re-fire path is
  `Record(At(i))` — `req` aliases an internal element and the promote-erase
  would dangle it.

Render-thread-confined (touched only inside the D3D9 Present detour), so —
unlike `ActionQueue` — it needs no mutex.

Unit test `overlay_recent_actions_test.cpp` (`build_recent_actions_test.bat`)
has red-green pins on the three behaviours a naive "push and cap" rewrite would
silently break: **dedup-promote**, **no-refire-evict**, **alias-safe**.

### Wiring into the DLL (this iter)

`overlay.cpp`:

- `#include "overlay_recent_actions.h"`.
- `RecentActionsInstance()` — process-wide `RecentActions` as a function-local
  static. No synchronization: render-thread-confined, and C++11's
  thread-safe-static-init covers the lazy construction.
- `DispatchAction(const ActionRequest&)` — the single dispatch path. Enqueues
  the request onto `ActionQueueInstance()` (drained off the render thread by
  the iter-515/516 worker) **and** `Record()`s it into the history. The three
  Phase 3 buttons (Spawn / Make-Invuln / Kill, iter 520) now route through it
  instead of calling `ActionQueueInstance().Enqueue()` directly.
- `RenderRecentActionsToolbar()` — drawn inside `RenderActionsWindow()` between
  the command preview and the footer toast. One clickable button per history
  slot, **vertical** list (long labels like "Make Invuln Empire_AT_AT" would
  balloon an `AlwaysAutoResize` window past the Tier strip on a `SameLine()`
  row — vertical keeps it ~320 px and honours the "uncluttered HUD" acceptance
  criterion). Each button ID is suffixed `##recent<i>` so two slots sharing a
  label stay distinct ImGui IDs. A click copies the slot out and, **after** the
  draw loop, calls `DispatchAction()` — `Record()` erases+re-inserts inside the
  very vector `At(i)` indexes into, so dispatching mid-loop would invalidate
  the iteration.

The re-fire is therefore a true round-trip: a clicked slot is re-enqueued onto
the bridge worker (the footer toast shows PENDING → LIVE/FAILED, guardrail
1007) and re-recorded, which promotes it to slot 0.

## Verification

- `build_recent_actions_test.bat` → **37 checks, 0 failures** (all three
  red-green pins green).
- `swfoc_overlay/build.bat` → **OVERLAY BUILD SUCCESS**, exit 0, zero compiler
  diagnostics. DLL **1,080,832 B**, up from iter-520's 1,072,128 B
  (+8,704 B — sane: new `RenderRecentActionsToolbar` + `DispatchAction` +
  `RecentActionsInstance` glue plus the `std::vector<ActionRequest>`
  erase/insert template instantiation).
- Build-only scope: `RenderRecentActionsToolbar` is ImGui render glue; the
  pure `RecentActions` logic is fully unit-tested. The click → re-fire →
  bridge round-trip path needs the live game — build-only verifiable, same as
  iter-512/514/516/519/520.
- Bridge harness / ledger lint / editor suite **untouched** by this
  overlay-only change — those gates unaffected.

## Next

iter-522 (per the iter-520 plan): teleport-selected + faction-switch buttons
(iter-108 `SWFOC_ChangeUnitOwner`), then the Phase 3 close-out (per-widget
capability badge + `Iter*Phase3WidgetsTests` + close-out doc).
