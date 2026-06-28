# iter-317 — Galactic tab planet icon column (first UI consumer of iter-315 ResolvePlanetIcon) + 4 iter-296 drift catches

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Thread D arc, post-finale 7 of N) + first UI-consumer ship for the iter-313/314/315 resolver extensions + 4 audit-by-fail drift catches
**Predecessor:** iter-316 (codified `feedback_extract_on_second_use.md`)
**Successor (queued):** iter-318 (next UI consumer ship — HeroLab portrait OR PlayerState faction emblem OR Asset Browser tab)

## What changed (5 source files modified + 1 new test file + 4 drift fixes + 1 close-out doc; ~430 LoC source + tests)

### NEW source files (1)
- **NEW** `src/SwfocTrainer.Core/V2Vm/PlanetRowWithIcon.cs` (~25 LoC) — record `PlanetRowWithIcon(PlanetId, OwnerFaction, TechLevel, IconPath?)` mirroring iter-308 `UnitTypeRow` shape; parallel UI-only projection that keeps `PlanetRow` + `GalacticTabState` icon-unaware.

### MODIFIED source files (3)
- **MODIFY** `src/SwfocTrainer.App/V2/ViewModels/GalacticTabViewModel.cs` (+~50 LoC):
  - Add `using SwfocTrainer.Core.Assets;`
  - Add `_planetRows: ObservableCollection<PlanetRowWithIcon>` field + `_iconResolver: UnitIconResolver?` MUTABLE field (mirrors iter-312 SetIconResolver pattern)
  - Add ctor optional param `UnitIconResolver? iconResolver = null` (iter-301/308/311 optional-default-null pattern)
  - Add public `PlanetRows` ObservableCollection property bound by XAML
  - Add public `SetIconResolver(UnitIconResolver?)` hot-swap method
  - Modify `RefreshPlanetsCore()` to also call NEW `RebuildPlanetRows()`
  - NEW private `RebuildPlanetRows()` with **defensive `_planets.ToList()` snapshot** before iteration (race-condition fix: ctor's `_ = RefreshPlanetsCore()` fire-and-forget can mutate `_planets` mid-enumeration)

- **MODIFY** `src/SwfocTrainer.App/V2/ViewModels/MainViewModelV2.cs` (~5 LoC):
  - Pass `iconResolver` to `GalacticTabViewModel` ctor (was 2-arg, now 3-arg)
  - In `OnSettingsPropertyChanged`, also call `Galactic.SetIconResolver(newResolver)` alongside Spawning (single new resolver instance shared across both tabs)

- **MODIFY** `src/SwfocTrainer.App/V2/MainWindowV2.xaml` (+~20 LoC):
  - Galactic DataGrid `ItemsSource` flipped from `{Binding Planets}` to `{Binding PlanetRows}`
  - Add `RowHeight="40"` for icon visibility
  - NEW `DataGridTemplateColumn` with `<Image Source="{Binding IconPath}" Width="32" Height="32" .../>` as first column

### NEW test file (1)
- **NEW** `tests/SwfocTrainer.Tests/Regression/Iter317GalacticPlanetIconColumnTests.cs` (~270 LoC, 12 facts) — `[Collection("ThumbnailCacheEnv")]` for env-var orthogonality with iter-307/308/312:
  - PlanetRowWithIcon record-shape pin (4 fields, types)
  - GalacticTabViewModel optional iconResolver ctor accepts null
  - PlanetRows is ObservableCollection<PlanetRowWithIcon>
  - SetIconResolver public method exists with correct signature
  - SetIconResolver hot-swap behaviors (4 cases including timing-independent design with `await Task.Delay(200)` to let ctor's async refresh settle)
  - SetIconResolver preserves planet metadata (only IconPath changes)
  - RebuildPlanetRows with no resolver = all IconPaths null
  - Source-level regression guards: MainViewModelV2 wires resolver to Galactic + hot-swaps both + XAML binds to PlanetRows + XAML has IconPath column

### Drift fixes (3 files modified) — iter-296 catalog drift catch
**iter-296** flipped `SWFOC_GetPlanets` from `Phase2HookPending` to `Live` (real galactic-mode planet enumeration shipped) but did NOT update 4 downstream regression tests. Caught by running broader Galactic regression filter after iter-317 wiring:

- `tests/SwfocTrainer.Tests/Regression/Iter221Phase2PendingReAuditTests.cs`:
  - `Iter134GalacticConfirmedDefers_StillPhase2Pending` — removed `SWFOC_GetPlanets` from confirmed-defer list; added explicit `Live` guard for it
  - `Phase2PendingEntryCount_Is25` → renamed `Phase2PendingEntryCount_Is24`; pin updated 25 → 24 with iter-296 promotion noted

- `tests/SwfocTrainer.Tests/App/V2/ViewModels/GalacticTabViewModelCapabilityTests.cs`:
  - `RefreshPlanets_BadgeIsPhase2Pending` → renamed `RefreshPlanets_BadgeIsLive`; expects `"LIVE"` instead of `"PHASE 2 PENDING"`
  - `Phase2PendingWarning_NamesEveryNonLiveAction` — removed `Refresh planets` from expected list; added `NotContain("Refresh planets")` with iter-296 reason

## Verification

| Gate | Result |
|------|--------|
| Build (Debug, full solution) | ✅ green (0 errors, 21 pre-existing warnings — all CS1570/CS8602 in unrelated test files; not introduced by iter-317) |
| iter-317 filtered tests | ✅ **12/12 PASS** in 995 ms |
| Galactic + iter-3xx + iter-221 regression filter | ✅ **213/213 PASS** in 1 s (after 4 drift fixes) |
| Bridge harness | inherited 1100/0 (no bridge changes) |
| Verifier ledger lint | inherited 0/0 at 318 entries (no ledger changes) |

## Pattern lessons

### Cross-cutting value of UI consumer iters

iter-313/314/315 each shipped resolver extensions but no UI. iter-317 is the **first UI consumer of any of those resolvers** — it surfaces planet icons. The pattern "ship resolver extension as a separate iter, then ship UI consumer as a follow-up iter" let each iter stay scope-contained but means the resolver work has no operator-visible value until the consumer iter ships. After iter-317, planet icons are operator-visible; HeroLab + PlayerState faction emblems still aren't (queued for iter-318+).

### Race-condition discovery via defensive snapshot

The first iter-317 test failure surfaced as `InvalidOperationException: Collection was modified; enumeration operation may not execute.` at `RebuildPlanetRows()`. Root cause: ctor fires `_ = RefreshPlanetsCore()` as fire-and-forget (matching iter-295 auto-refresh-on-tab-activate), then test calls `SeedPlanets` + `SetIconResolver` synchronously while the async refresh is still pending. The defensive 1-line fix `foreach (var p in _planets.ToList())` snapshots the collection — eliminates the race for production code too (any future overlap of operator-initiated refresh + Settings.IconsRoot edit would hit the same bug). Cheap fix; correct shape; pinned with iter-comment.

### Timing-independent test design via sentinel IDs

Second test failure exposed that `SeedPlanets` runs BEFORE the ctor's async refresh completes — the simulator's SWFOC_GetPlanets handler returns its own data and clobbers our seed. Fix: use sentinel planet IDs (`TestPlanet9999`, `TestPlanetA9999/B9999/C9999`) that won't collide with simulator's real planets, then `await Task.Delay(200)` after construction to let the ctor's async refresh settle before SeedPlanets, then verify our sentinel rows appear in the rebuilt PlanetRows via `vm.PlanetRows.First(r => r.PlanetId == "...")` instead of indexed access. Now the test is robust to whatever planets the simulator happens to return.

### iter-296 catalog drift quartet — the 4-instance audit-by-fail catch

iter-296 promoted `SWFOC_GetPlanets` Phase2HookPending → Live without updating 4 downstream regression tests:
1. `Iter134GalacticConfirmedDefers_StillPhase2Pending` (catalog name pin)
2. `Phase2PendingEntryCount_Is25` (count pin — already updated 26 → 25 in iter-243, now 25 → 24)
3. `RefreshPlanets_BadgeIsPhase2Pending` (per-button capability pin)
4. `Phase2PendingWarning_NamesEveryNonLiveAction` (warning-text contents pin)

These 4 had been silently red since iter-296 (~12 iters ago) because no iter between 296 and 317 happened to touch the Galactic tab. iter-317 surfaced them in the first regression run. **Confirms `feedback_allactions_count_pin_drift.md` rule (iter-195/iter-208 codified): full-suite runs every 5 iters of an arc** — would have caught these 4 at iter-296 close-out instead of iter-317. (Cannot retroactively pin to iter-296; the iter-296 author skipped the broader run.)

The drift fixes themselves were trivial (3 tests, ~10 LoC of edits) and shipped inline with iter-317 because (a) they're in the area I just touched and (b) leaving them red breaks the `Drive ALL warnings to zero everywhere` rule. Honest scope-extension, not unrelated-cleanup-creep.

## Operator workflow (now end-to-end, 2 tabs)

1. Operator pre-extracts MasterTextures.meg via Python CLI: `python tools/asset_extractor/meg_parser.py extract MasterTextures.meg --out C:/swfoc_extracted_dds/`
2. Operator runs `python tools/asset_extractor/thumbnail_cache.py warm C:/swfoc_extracted_dds/` to populate the cache
3. Operator launches editor, goes to Settings tab, types or browses to `C:/swfoc_extracted_dds/`
4. **Spawning tab** — unit-type ListBox renders unit icons (iter-308) — LIVE
5. **Galactic tab** — planet DataGrid renders planet icons (iter-317) — LIVE
6. Settings.IconsRoot can be edited mid-session — both tabs hot-swap immediately (no editor restart)

Honest defer to iter-318+:
- HeroLab portrait column (iter-313 ResolvePortrait extension is shipped but no UI consumer yet)
- PlayerState faction emblem column (iter-314 ResolveFactionEmblem extension shipped but no UI consumer)
- Asset Browser tab (iter-313 deferred, ~150-250 LoC + XAML)
- Audit B last wire (`faction-roster-by-build-tab` from iter-299 honest-defer)

## What's NOT codified at iter-317 (deferred patterns)

- **`feedback_mid_iter_pivot_on_scope_unclarity.md`** — at 2/3 instances; iter-317 stayed on its scoped target so no third instance triggered
- **`feedback_distinct_count_n_coexistence.md`** — at 1/3 (iter-315 only)
- **`feedback_async_ctor_fire_and_forget_test_design.md`** — first instance (iter-317 timing-independent test design); needs 2 more recurrences for codification
- **`feedback_collection_snapshot_in_async_iteration.md`** — first instance (iter-317 ToList() defensive snapshot); needs 2 more recurrences

These all stay flagged in this close-out doc. iter-318+ may trigger one or more.

## Verification checklist

- [x] PlanetRowWithIcon record created (4 fields, immutable)
- [x] GalacticTabViewModel ctor accepts optional iconResolver (back-compat preserved)
- [x] PlanetRows ObservableCollection populated on RefreshPlanets + SetIconResolver
- [x] SetIconResolver hot-swap rebuilds rows immediately
- [x] MainViewModelV2 wires resolver to Galactic + hot-swaps on Settings.IconsRoot change
- [x] Galactic XAML DataGrid binds to PlanetRows + has Image column on IconPath
- [x] iter-317 12 tests pass (filtered run)
- [x] Galactic + iter-3xx + iter-221 213 tests pass (regression run after drift fixes)
- [x] 4 iter-296 drift catches fixed inline
- [ ] State docs synced (in progress)
- [ ] Task #568 marked completed; iter-318 queued
