# iter-301 — Settings tab UI mod-picker (consumes iter-299 + iter-300)

**Date:** 2026-05-07
**Arc class:** Operator-facing surfacing (mandate-expansion polish)
**Predecessor:** iter-300 (SWFOC_ListMods bridge wire — 300th-iter milestone)
**Successor (queued):** iter-302 (codify `feedback_engine_already_does_this.md` memory rule)

## What changed (3 files extended, 1 new; ~250 LoC total)

- **`SettingsTabViewModel.cs`** (+~150 LoC) — Mod-picker added inline:
  - NEW `ModRow(Name, Path, IsCurrentlyLoaded)` record at top of file.
  - NEW `ObservableCollection<ModRow> Mods` + `string ActiveMod` + `string ModPickerStatus` properties.
  - Constructor extended with optional `V2BridgeAdapter? bridge = null` parameter. **Optional-default-null** keeps backward compat — existing tests don't break, and Refresh just no-ops if bridge is null.
  - `RefreshModsCommand` (AsyncRelayCommand) — calls iter-299 `SWFOC_GetCurrentMod` first, then iter-300 `SWFOC_ListMods`, parses both responses, populates `Mods` with cross-referenced `IsCurrentlyLoaded` flags.
  - `OpenModsFolderCommand` (RelayCommand) — derives `Mods/` from `_settings.GamePath`'s parent + `Process.Start("explorer.exe", path)` via existing `TryStartShellCommand` helper.
  - `HandleError(Exception)` — routes async errors to `ModPickerStatus` (visible UI text) instead of swallowing.

- **`MainViewModelV2.cs`** (+1 line + 2 lines comment) — passes `bridge` to `SettingsTabViewModel` constructor so the mod-picker has a live bridge.

- **`Iter301SettingsModPickerTests.cs`** (NEW, ~110 LoC) — 5 pin tests via real pipe + V2BridgeAdapter + simulator pattern.

## Verification gates ALL GREEN

```
dotnet test --filter 'FullyQualifiedName~Iter301':
  Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 39 ms

dotnet build src/SwfocTrainer.App/SwfocTrainer.App.csproj:
  Build succeeded. 0 Warning(s), 0 Error(s)
```

5 tests cover:
1. **Constructor without bridge doesn't throw** — backward-compat for legacy callers.
2. **RefreshModsCore populates + flags currently-loaded** — 3-mod fake state with AOTR active; verifies cross-iteration reference is correct.
3. **(no_mods) sentinel** → empty Mods collection + helpful "no mods" status message.
4. **Vanilla active mod** → no mod row flagged loaded (operator may have mods installed but be running vanilla).
5. **OpenModsFolderCommand with missing GamePath doesn't throw** — graceful degradation when `_settings.GamePath` is empty.

## XAML deferred to iter-302+

The C# VM is fully wired and tested; the visible WPF surface (DataGrid binding + Refresh/OpenFolder buttons + "Currently loaded:" badge) is deferred to a later iter for two reasons:

1. **Settings.xaml is large and shared** — needs careful insertion to avoid regressing existing form fields.
2. **VM-first ships value immediately** — operator can drive RefreshModsCommand programmatically (e.g. via Lua Playground or the existing diagnostic surface) before XAML lands.

iter-303+ will add the XAML surface in a focused 1-2 iter follow-up. iter-302 prioritizes the codification of the *engine-already-does-this* pattern (iter-300 trigger).

## Pattern lessons

### Optional-default-null constructor extension (NEW pattern)

Adding `V2BridgeAdapter? bridge = null` to an existing constructor preserves backward compatibility while opening a new capability surface. Three tests don't change (default null = no mod-picker behavior); two new tests exercise the bridge path. **Cost: 1 line of constructor signature change**; **benefit: incremental capability without ripple changes**.

This is the cheapest extension pattern when only a *subset* of new functionality needs the new dependency. Compare with the alternative (full constructor rewrite + every callsite update + every test update): ~30-50 LoC of churn for the same capability.

Worth adopting universally for VM constructors when adding optional features.

### Cross-iteration consumer tests (iter-300 pattern recurrence)

Test #2 verifies the same iter-299↔iter-300 relationship as iter-300's test #4, but at the **VM consumer layer** instead of the bridge wire layer. The pattern proves itself: testing relationships between wires/components catches regressions individual unit tests miss. **Adopt at every consumer layer** that touches multiple semantically-related wires.

### Zero mid-iter API drifts (3 iters in a row: iter-299→iter-300→iter-301)

Iter-299 paid the API-discovery cost (4 mid-iter drifts); iter-300 and iter-301 are riding that knowledge for free. The compound interest of pattern discipline is now load-bearing.

## Operator-visible change

**Before iter-301**: Settings tab had no way to see what mods were available or what was currently loaded. Operator had to open File Explorer, navigate to `<GameDir>/Mods/`, and remember which mod they had launched with.

**After iter-301**: Settings VM exposes:
- `Mods` collection (3-column logical: Name, Path, IsCurrentlyLoaded)
- `ActiveMod` string (e.g. "AOTR" or "vanilla")
- `ModPickerStatus` (e.g. "Found 3 mod(s). Currently loaded: AOTR")
- `RefreshModsCommand` (one click → bridge fetch → populated grid)
- `OpenModsFolderCommand` (one click → File Explorer at game's Mods/)

XAML binding is the only thing standing between this VM surface and operator-clickable UX.

## What's NOT done in iter-301 (deferred)

- **XAML surface** — iter-303+ will add the DataGrid + Refresh button + "Open Mods folder" button + "Currently loaded:" badge. ~30-50 LoC of XAML; deferred to keep iter-301 focused.
- **Modinfo.xml version parsing** — iter-302+ will add it server-side (bridge) so the version field stops showing `unknown`.
- **Multi-mod stack support** — when operator launches with `MODPATH=Mods\A;Mods\B`, only the most-recently-accessed appears as ActiveMod. iter-303+ if operator hits the limitation.
- **Live SWFOC verify** — deferred to operator session.

## Tasks queued

- **iter-302** (next, queued in iter-300 close-out): codify `feedback_engine_already_does_this.md` memory rule. 6th-instance trigger reached at iter-300; iter-301 is the 7th if you count VM-consumer surface as a separate instance. Codification ripe.
- iter-303: Settings tab XAML surface for the iter-301 VM (~30-50 LoC XAML).
- iter-304+: Asset/icon extraction kickoff (.meg parser + DDS decoder per user mandate).

## Verification checklist

- [x] `SettingsTabViewModel` constructor extended with optional bridge.
- [x] `Mods` ObservableCollection + `ActiveMod` + `ModPickerStatus` properties wired.
- [x] `RefreshModsCommand` cross-references iter-299 GetCurrentMod + iter-300 ListMods.
- [x] `OpenModsFolderCommand` reuses existing `TryStartShellCommand` helper for sandbox safety.
- [x] `HandleError` routes to visible `ModPickerStatus`.
- [x] `MainViewModelV2.cs` passes bridge to constructor.
- [x] 5/5 pin tests pass in 39ms.
- [x] Editor build 0 warnings / 0 errors.
- [x] Bridge harness inherits 1100/0 (no bridge changes).
- [ ] XAML surface — deferred to iter-303+.
- [ ] State docs synced.
- [ ] Task #552 marked completed; iter-302 (codification) queued.
