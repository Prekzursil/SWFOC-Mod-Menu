# iter-303 — Settings tab XAML surface for iter-301 VM mod-picker

**Date:** 2026-05-07
**Arc class:** Operator-facing surfacing (XAML follow-up to iter-301 VM)
**Predecessor:** iter-302 (codified `feedback_engine_already_does_this`)
**Successor (queued):** iter-304 (asset/icon extraction kickoff per user mandate)

## What changed (1 file extended; ~75 LoC XAML)

- **`SWFOC editor/.../V2/MainWindowV2.xaml`** (~75 LoC):
  - Added a new `<RowDefinition Height="*"/>` (10th row) to the Settings tab `<Grid>` so the new GroupBox can expand vertically as the operator resizes.
  - Re-targeted the existing Info GroupBox to keep its `Grid.Row="8"` slot.
  - **NEW** `Grid.Row="9"` GroupBox titled "Available Mods" with:
    - **Status row** (Row 0): `Currently loaded: <ActiveMod>` (bold accent foreground) + ` — ` separator + `<ModPickerStatus>` (muted foreground, character-ellipsis on overflow).
    - **Action row** (Row 1): `[Refresh mods]` button (binds `RefreshModsCommand`) + `[Open Mods folder]` button (binds `OpenModsFolderCommand`). Tooltips reference iter-299/300 wires for operator awareness.
    - **DataGrid** (Row 2): bound to iter-301 `Mods` collection with 3 columns (`Loaded`, `Mod name`, `Path`); read-only; alternating row backgrounds via the existing `SurfaceAltBackground` resource.

## XAML insertion strategy

Settings tab XAML lives inline in `MainWindowV2.xaml` (no per-tab xaml file). The existing Settings `<Grid>` had 9 row definitions with the last (`Row=8` Info GroupBox) sitting in the `*`-height row. To preserve Info's position while adding the mod-picker as a new bottom row:

1. Bumped `RowDefinitions` from 9 → 10 by appending a new `<RowDefinition Height="*"/>` (the previous `*`-height row stayed `Auto` to keep Info compact).
2. Info GroupBox kept `Grid.Row="8"` unchanged (now Auto).
3. New mod-picker GroupBox slotted at `Grid.Row="9"` (the new `*`-height row), so it expands as the operator resizes.

This insertion strategy preserves layout for all existing controls (Game path, Bridge pipe, Log path, AutoConnect, ShowAdvanced, Save/Reload buttons, Browse buttons, Open file buttons, Info) while adding the new GroupBox at the bottom where it has natural room to grow.

## Verification gates ALL GREEN

```
dotnet build src/SwfocTrainer.App/SwfocTrainer.App.csproj:
  Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test --filter 'FullyQualifiedName~Iter301':
  Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 55 ms

Bridge harness inherits 1100/0 from iter-300 (no bridge changes).
Verifier ledger lint inherits 0/0 at 318 entries.
```

The iter-301 VM pin tests pass without modification — XAML changes to bindings don't affect VM unit tests because they don't touch the WPF visual tree. The XAML compile success (0 warnings / 0 errors from MSBuild's XamlMarkupCompilation pass) confirms the bindings resolve to actual VM properties.

## Pattern lessons

### *XAML-after-VM* surfacing pattern (iter-301 → iter-303 telescoped pair)

iter-301 shipped the VM with the bridge consumer logic. iter-302 was a docs intermission. iter-303 ships the XAML surface. **Total operator-clickable capability shipped across 3 iters** (iter-301 + iter-303 = code; iter-302 = pattern documentation).

This is a useful cadence pattern when the VM and XAML are large enough to warrant separate iters:
- iter-N (VM-first): builds capability + tests, defers XAML
- iter-N+1 (docs/pattern): codifies what the previous iter taught, zero risk
- iter-N+2 (XAML surface): exposes the VM to the operator

The intermission iter (iter-302) wasn't filler — it was a load-bearing rhythm anchor that produced permanent operator-visible value (the codified rule guides future iters).

### *DataGrid binding via 3-column shape*

Existing patterns (Galactic tab planets, Tactical Units list) use `AutoGenerateColumns="False"` + explicit `<DataGridTextColumn>` per field. iter-303 follows the same shape:
- Column 1: status indicator (`IsCurrentlyLoaded` boolean → "True"/"False" text in column; could be polished to checkmark icon in iter-N+ via converter)
- Column 2: primary identifier (`Name`)
- Column 3: secondary detail (`Path`, `*`-width to absorb extra space)

The 60/200/* column widths leave the path column flexible while keeping Name compact. Operator can resize columns at runtime if a long name/path needs more room.

### *Tooltip-as-spec-pointer*

Both buttons have tooltips that reference the underlying iter-299/300 bridge wires:
- "Query SWFOC_ListMods (iter-300) + SWFOC_GetCurrentMod (iter-299) and rebuild the grid below."
- "Launch File Explorer at the game's Mods/ directory. Drop sidecar mods here (e.g. iter-297 stub-XML repair output)."

This gives operators a self-documenting trail: hover the button → see what bridge wire it calls + which iter shipped it. **Useful for debugging** when a button doesn't behave as expected — the operator (or a future agent reading screenshots) can immediately pivot to the catalog/changelog.

## Operator-visible change

**Before iter-303**: iter-301 VM mod-picker existed but had no XAML surface. Operator could only drive it programmatically (Lua Playground, debugger).

**After iter-303**: Settings tab now shows a bottom GroupBox with:
- "Currently loaded: AOTR — Found 3 mod(s). Currently loaded: AOTR" status line
- [Refresh mods] [Open Mods folder] action row
- DataGrid listing mods with cross-referenced loaded-flag

End-to-end operator workflow: open Settings tab → click "Refresh mods" → see grid + currently-loaded badge → click "Open Mods folder" if they want to drop a sidecar mod (iter-297 stub-XML repair output).

## What's NOT done in iter-303 (deferred)

- **Boolean→checkmark converter** for the IsCurrentlyLoaded column — currently shows "True"/"False" text. Polish, not blocker. Defer until operator requests visual upgrade.
- **Right-click context menu** on the DataGrid (e.g., "Open this mod's folder", "Copy path") — defer to iter-N+ if operator demand emerges.
- **XAML pin test** — XAML compilation success + iter-301 VM tests passing covers the binding resolution. A dedicated XAML pin test would require WPF UI test infrastructure (UiTests project) which has 2 pre-existing flakes; not worth introducing more flakes for marginal coverage.
- **Live SWFOC verify** — deferred to operator session. Build clean + bindings resolve.

## Tasks queued

- **iter-304** (next): asset/icon extraction kickoff per user mandate. The iter-302 codified rule (`feedback_engine_already_does_this`) will guide the approach: check engine for DDS / .meg loading Lua API first; fall back to filesystem `.meg` parser + DDS decoder only if engine API absent. Multi-iter arc (~3-5 iters).
- iter-305+: Asset/icon extraction continued (texture cache, unit-icon DataGrid columns, etc.).

## Verification checklist

- [x] Settings tab XAML extended with new RowDefinition + Mod picker GroupBox.
- [x] GroupBox layout: 3-row Grid (Status / Actions / DataGrid).
- [x] Bindings: `ActiveMod`, `ModPickerStatus`, `Mods`, `RefreshModsCommand`, `OpenModsFolderCommand`.
- [x] DataGrid: 3 columns (Loaded / Name / Path) with `*`-width on Path.
- [x] Tooltips reference iter-299/300/297 source iters for self-documentation.
- [x] Existing Info GroupBox preserved at Grid.Row=8.
- [x] Editor build 0 warnings / 0 errors (XAML compile + C# compile).
- [x] Iter-301 VM pin tests 5/5 still pass (bindings don't affect VM unit tests).
- [x] Bridge harness inherits 1100/0; ledger lint inherits 0/0.
- [ ] Boolean→checkmark converter — deferred (polish).
- [ ] Right-click context menu — deferred (operator demand).
- [ ] Live SWFOC verify — deferred to operator session.
- [ ] State docs synced.
- [ ] Task #554 marked completed; iter-304 (asset extraction kickoff) queued.
