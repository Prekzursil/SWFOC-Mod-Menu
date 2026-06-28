# Overlay Phase 3 — Teleport + Faction Switch buttons (iter 524)

## What shipped

The Phase 3 in-overlay **Actions** window (`swfoc_overlay/overlay.cpp::RenderActionsWindow`)
gains its final two action buttons, completing the five-button per-unit action set:

| Button | Wire | Builder | Gating |
|---|---|---|---|
| Spawn | `SWFOC_SpawnUnitLua` | `BuildSpawnUnitCommand` | always |
| Make Invuln | `SWFOC_MakeUnitInvulnLua` | `BuildMakeUnitInvulnCommand` | always |
| Kill | `SWFOC_KillUnit` | `BuildKillUnitCommand` | address-gated (non-zero hex) |
| **Teleport** | `SWFOC_TeleportUnitLua` | `BuildTeleportUnitCommand` | always |
| **Faction Switch** | `SWFOC_ChangeUnitOwner` | `BuildChangeUnitOwnerCommand` | always |

Teleport sends the selected unit to the `Position` vector (the same `g_actionPos`
field the Spawn button reads). Faction Switch re-owns the selected unit to the
`Faction` combo's player — the engine's full "swap sides" behaviour (iter 108).

Both target `selectedUnitExpr` — the single shared `Find_First_Object("<unitType>")`
Lua handle the spec's "1 SelectedUnitLuaExpr field shared across Phase 3 widgets"
(overlay-interactive.md iter-290) calls for, already consumed by Make Invuln.
Both route through `DispatchAction()`, so they appear in the recent-actions
toolbar and the footer toast like every other Phase 3 button.

## Wire verification (operator-trust, guardrail 1007)

Both wires confirmed registered LIVE in `swfoc_lua_bridge/lua_bridge.cpp`
(2026-05-21):

- `SWFOC_TeleportUnitLua` → `Lua_TeleportUnitLua`, registration line 8256
  (marked "iter 151 LIVE").
- `SWFOC_ChangeUnitOwner` → `Lua_ChangeUnitOwner`, registration line 8368.

No new bridge wires were created — both pre-exist. The footer "Wires (all LIVE)"
line now enumerates all five.

## Phantom-partial recovery

The pure builders `BuildTeleportUnitCommand` / `BuildChangeUnitOwnerCommand` and
their tests (`overlay_actions_test.cpp`: Teleport/ChangeOwner cases + red-green
quoting pins) were already on disk (timestamp 06:27) — authored by an interrupted
earlier iter but never wired into `RenderActionsWindow`. The `RenderActionsWindow`
comment block and the `selectedUnitExpr` computation were likewise pre-written.
This iter finished the wiring: the two `ImGui::Button` onClick handlers plus the
five-wire footer line. Same phantom-partial class as iter-519 / iter-523.

## Verification

- `build_actions_test.bat` → **27 checks, 0 failures** (pure builder layer green,
  Teleport + ChangeOwner cases + red-green pins included).
- `build.bat` → **OVERLAY BUILD SUCCESS**, exit 0, zero compiler diagnostics.
- DLL **1,090,048 B** (+9,216 B vs iter-523's 1,080,832 B — sane: two onClick
  handlers + `std::string` concats + two builder call instantiations + footer
  line change).
- Bridge harness / ledger lint / editor suite untouched → unaffected.

Build-only scope: `RenderActionsWindow` is ImGui render glue. The pure pieces it
depends on are all unit-tested (overlay_actions / overlay_action_queue /
overlay_action_worker / overlay_recent_actions). The onClick → enqueue → drain →
`BridgeProbe` → toast runtime path needs the live game — build-only verifiable,
same as iter-512 / 514 / 516 / 519 / 520 / 523.

## Next (Phase 3 close-out)

Per-widget capability badge + `Iter*Phase3WidgetsTests` + Phase 3 close-out doc,
then Phase 4 (drag-drop tactical spawning, iter 291-295).
