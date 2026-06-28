# iter-318 — Hero Lab tab portrait column (second UI consumer of iter-313 ResolvePortrait) + 1 iter-295 drift catch

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Thread D arc, post-finale 8 of N) + 2nd UI-consumer ship after iter-317 + 1 audit-by-fail drift catch
**Predecessor:** iter-317 (Galactic planet icon column)
**Successor (queued):** iter-319 (PlayerState faction emblem column OR Asset Browser tab)

## What changed (4 source files modified + 1 new test file + 1 drift fix; ~250 LoC source + tests)

### NEW source files (1)
- **NEW** `src/SwfocTrainer.Core/V2Vm/HeroRowWithPortrait.cs` (~70 LoC) — record `(ObjAddr, TypeName, OwnerSlot, Alive, RespawnRemainingMs, RespawnEnabled, IconPath?)` + computed `RespawnRemainingDisplay` mirror of HeroRow's existing property (em-dash for disabled / "0 ms" / "X ms" / "X.X sec" / "N min M sec" — exact same operator-visible string, pinned by test).

### MODIFIED source files (3)
- **MODIFY** `src/SwfocTrainer.App/V2/ViewModels/HeroLabTabViewModel.cs` (+~50 LoC) — `using SwfocTrainer.Core.Assets;` + `_iconResolver` mutable field + `_heroRows` ObservableCollection + ctor optional `UnitIconResolver? iconResolver = null` + public `HeroRows` + public `SetIconResolver` hot-swap + private `RebuildHeroRows()` with **defensive `_heroes.ToList()` snapshot** (carry-forward iter-317 race-condition fix).
- **MODIFY** `src/SwfocTrainer.App/V2/ViewModels/MainViewModelV2.cs` (~5 LoC) — pass iconResolver to HeroLab ctor + hot-swap HeroLab alongside Spawning + Galactic in OnSettingsPropertyChanged. **All 3 tabs now hot-swap together** when operator changes Settings.IconsRoot.
- **MODIFY** `src/SwfocTrainer.App/V2/MainWindowV2.xaml` (+~20 LoC) — HeroLab DataGrid `ItemsSource` flipped from `{Binding Heroes}` to `{Binding HeroRows}` + `RowHeight="72"` (larger than iter-317 Galactic 40 because portraits render at 64px, larger than Galactic 32px planet icons) + NEW `DataGridTemplateColumn` with `<Image Width="64" Height="64" Source="{Binding IconPath}"/>` as first column.

### NEW test file (1)
- **NEW** `tests/SwfocTrainer.Tests/Regression/Iter318HeroLabPortraitColumnTests.cs` (~310 LoC, 12 facts) — `[Collection("ThumbnailCacheEnv")]`. Covers: record-shape pin (7 fields) + RespawnRemainingDisplay mirror across 6 canonical inputs + optional ctor + ObservableCollection type pin + SetIconResolver public method pin + 4 timing-independent hot-swap behaviors (sentinel hero name `TestHero9999` + `await Task.Delay(200)`) + metadata-preservation pin + no-resolver-null-IconPaths pin + 4 source-level XAML/MainViewModelV2 wire guards (including count-pin on Image columns ≥ 2 to catch future XAML refactors that drop either tab's icon column).

### Drift fix (1) — iter-295 auto-refresh count-pin drift
**iter-295** added `_ = RefreshHeroesCore()` to HeroLabTabViewModel ctor (auto-refresh-on-tab-activate) but did NOT update `Iter78HeroLabRespawnPresetTests.ApplyRespawnPreset_FiresSetHeroRespawnTimer_RecordsSingleEntry`. The test asserted `RecentCalls.Should().HaveCount(1)` expecting only the SetHeroRespawnTimer call, but auto-refresh adds an extra ListHeroes call → expected 1, found 2.

**Fix**: filter by command name (`Where(c => c.LuaCommand.Contains("SetHeroRespawnTimer"))`) so the test pins what it actually means: ApplyRespawnPreset fires exactly one SetHeroRespawnTimer call regardless of unrelated auto-refresh activity. This is the iter-317 lesson restated: **count-by-name is more robust than count-total when async setup contributes unrelated entries**.

## Verification

| Gate | Result |
|------|--------|
| Build (Debug, full solution) | ✅ green (0 errors, pre-existing CS1570/CS8602 warnings unchanged from iter-317) |
| iter-318 + HeroLab + iter-317 + Iter78 filtered tests | ✅ **60/60 PASS in 2 s** (after Iter78 drift fix) |
| Bridge harness | inherited 1100/0 (no bridge changes) |
| Verifier ledger lint | inherited 0/0 at 318 entries (no ledger changes) |

## Pattern lessons

### iter-317 pattern repeats with negligible marginal cost

iter-318 mirrored iter-317 verbatim across the same 5 modification sites (record, VM, MainVM, XAML, tests). Marginal cost: ~50 LoC source + 70 LoC record + 310 LoC tests + ~20 LoC XAML = **~450 LoC for a complete UI consumer**. Iter-317's marginal cost was similar; the resolver-extension iters (iter-313/314/315) were ~45 LoC each with no UI consumer attached. **Total cost of "ship asset class as a fully operator-visible feature" ≈ ~500 LoC** when summing the resolver extension + UI consumer iters.

### "Delay commitment" trio applied 3 iters in a row

iter-318 applied:
- **iter-302 engine-already-does-this**: HeroLab portrait icons reuse iter-313 `ResolvePortrait` (already shipped); no duplicate resolver.
- **iter-311 optional-default-null-constructor-extension**: HeroLabTabViewModel ctor extends with `UnitIconResolver? iconResolver = null`; existing callers (and iter-78 test) keep working unchanged.
- **iter-316 extract-on-second-use**: Originally `LocateByConvention` extracted at iter-313 (1st extract); iter-314/315/318 all reuse it without modification. iter-318 is the 5th plugin into that abstraction.

### Audit-by-fail catches yesterday's silent drifts

Same lesson as iter-317: the iter-295 count-pin drift had been silently red-or-flaky (depending on async timing) for ~23 iters. iter-318's broader regression filter caught it via the timing-shift my synchronous `RebuildHeroRows()` introduced. Per `feedback_allactions_count_pin_drift.md` (iter-195/iter-208 codified): full-suite runs every 5 iters of an arc would have caught these earlier.

### NEW pattern observation: count-by-name > count-total for async assertions (1st instance, codification candidate at 3rd)

When a test asserts on a side-effect counter (like `RecentCalls.Count`), filtering by the action's own name (`.Where(LuaCommand.Contains("X"))`) is more robust than total count when async setup or auto-refresh contributes orthogonal entries. iter-317 fixed Iter221's ChangePlanetOwnerWithMode test similarly. Codification candidate `feedback_count_by_name_for_async_assertions.md` flagged at 3rd recurrence.

## Operator workflow (now end-to-end, 3 tabs)

1. Operator pre-extracts MasterTextures.meg via Python CLI
2. Operator runs `python tools/asset_extractor/thumbnail_cache.py warm <root>` to populate the cache
3. Operator launches editor, goes to Settings tab, types or browses to extracted-DDS root
4. **Spawning tab** — unit-type ListBox renders unit icons (iter-308) — LIVE
5. **Galactic tab** — planet DataGrid renders planet icons (iter-317) — LIVE
6. **Hero Lab tab** — hero DataGrid renders hero portraits (iter-318) — LIVE
7. Settings.IconsRoot can be edited mid-session — all 3 tabs hot-swap immediately (no editor restart)

Honest defer to iter-319+:
- PlayerState faction emblem column (iter-314 ResolveFactionEmblem extension shipped but no UI consumer)
- Asset Browser tab (iter-313 deferred, ~150-250 LoC + new XAML tab)
- Audit B last wire (`faction-roster-by-build-tab` from iter-299 honest-defer)
- Live SWFOC verify against operator's real MasterTextures.meg
- Weapon/ability icon classes

## Verification checklist

- [x] HeroRowWithPortrait record created (7 fields + RespawnRemainingDisplay mirror)
- [x] HeroLabTabViewModel ctor accepts optional iconResolver (back-compat preserved)
- [x] HeroRows ObservableCollection populated on RefreshHeroes + SetIconResolver
- [x] SetIconResolver hot-swap rebuilds rows immediately
- [x] MainViewModelV2 wires resolver to HeroLab + hot-swaps all 3 tabs on Settings.IconsRoot change
- [x] HeroLab XAML DataGrid binds to HeroRows + has Image column on IconPath
- [x] iter-318 12 tests pass (filtered run)
- [x] HeroLab + iter-317 + iter-78 60 tests pass (regression run after drift fix)
- [x] 1 iter-295 drift catch fixed inline
- [ ] State docs synced (in progress)
- [ ] Task #569 marked completed; iter-319 queued
