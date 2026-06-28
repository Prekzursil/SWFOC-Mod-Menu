# iter-312 — Live VM rebuild on Settings.IconsRoot change (closes iter-310 honest-defer)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Thread D arc, post-finale closeout 2/2)
**Predecessor:** iter-311 (Operator changelog + 2 codifications)
**Successor (queued):** iter-313 (Asset Browser tab OR hero portraits OR Audit B last wire)

## What changed (3 files modified + 1 test file new + 1 test edit; ~150 LoC; iter-312 pin tests pending verify)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/ViewModels/SpawningTabViewModel.cs` (~10 LoC):
  - Dropped `readonly` from `_iconResolver` field (now mutable for hot-swap)
  - NEW `public void SetIconResolver(UnitIconResolver? iconResolver)` method — replaces `_iconResolver` + immediately calls `RefreshFilteredTypes()` to rebuild rows with new IconPaths

- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/ViewModels/MainViewModelV2.cs` (~25 LoC):
  - NEW `Settings.PropertyChanged += OnSettingsPropertyChanged;` subscription right after Spawning tab construction
  - NEW `private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)` handler — filters on `nameof(SettingsTabViewModel.IconsRoot)` then calls `Spawning.SetIconResolver(new UnitIconResolver(ResolveIconsRoot(_settings)))`

- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/ViewModels/SettingsTabViewModel.cs` (~3 LoC):
  - `IconsRootStatus` happy-path string: `"Found {N} icons (restart editor for changes to take effect)"` → `"Found {N} icons (Spawning tab updates live on edit)"` — accurately reflects iter-312 hot-swap behavior

- **MODIFY** `SWFOC editor/tests/SwfocTrainer.Tests/App/V2/ViewModels/Iter310SettingsIconsRootUiTests.cs` (~3 LoC):
  - Updated `IconsRootStatus_WhenIconsPresent_ShowsCount` assertion: `Should().Contain("restart editor")` → `Should().Contain("updates live")` — tracks the iter-312 string change

- **NEW** `SWFOC editor/tests/SwfocTrainer.Tests/App/V2/ViewModels/Iter312SpawningResolverHotSwapTests.cs` (~145 LoC, **5 facts**):
  - `SetIconResolver_FromNullToValid_PopulatesIconPaths` — null → valid resolver hot-swap rebuilds rows with new IconPaths
  - `SetIconResolver_FromValidToNull_ClearsIconPaths` — operator-clear scenario (settings.IconsRoot → null)
  - `SetIconResolver_NewRoot_RebuildsWithNewIconPaths` — old-root vs new-root behavior; new content with no cache → graceful null
  - `SetIconResolver_PreservesRowCount_AndOrder` — row count unchanged on hot-swap; same TypeIds in same order
  - `SetIconResolver_RespectsCurrentFilter` — hot-swap rebuilds from FILTERED rows, not raw available types

- Pinned to `[Collection("ThumbnailCacheEnv")]` — orthogonal collection serialization with iter-307+308.

## End-to-end operator workflow (Thread D arc COMPLETE + WIRED + UI-DISCOVERABLE + LIVE)

After iter-312, the operator workflow loses the editor restart:

```bash
# 1-2. (One-time per game install — same as iter-310)
python tools/asset_extractor/meg_parser.py "C:\Games\SWFOC\Data\MasterTextures.meg" --extract-all C:\Games\SWFOC\extracted
for dds in C:\Games\SWFOC\extracted\Data\Art\Textures\Units\*.dds; do
    python tools/asset_extractor/thumbnail_cache.py "$dds" --size 32
done

# 3. Launch editor → Settings tab → Unit icons → Browse → pick C:\Games\SWFOC\extracted
# 4. Spawning tab → unit types render with their in-game icons IMMEDIATELY (no restart)
# 5. Operator can re-Browse to a different root and Spawning rows update in-place
```

## Verification gates ALL GREEN

```
[run_editor_tests_v2] dotnet test --filter "FullyQualifiedName~Iter312"
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 31 ms

[run_editor_tests_v2] dotnet test --filter "...~Iter307|...~Iter308|...~Iter309|...~Iter310|...~Iter312"
Passed!  - Failed:     0, Passed:    70, Skipped:     0, Total:    70, Duration: 209 ms
```

- Editor build: GREEN (App + Tests + dependents compiled cleanly with iter-312 changes)
- iter-312 pin tests: **Passed 5/5 in 31 ms** ✓
- Combined Thread D (iter-307+308+309+310+312): **Passed 70/70 in 209 ms** ✓ (no regression in iter-307's 21, iter-308's 20, iter-309's 12, iter-310's 12)
- Bridge harness inherits 1100/0 (no bridge changes)
- Verifier ledger lint inherits 0/0 at 318 entries (no ledger changes)
- 111 → 111 buttons UNCHANGED (hot-swap reuses existing Browse + Save + TextBox edit triggers)

## Pattern lessons

### *INPC-based hot-swap is the cleanest cross-VM-boundary signal*

The composition root (`MainViewModelV2`) doesn't need to inspect `V2Settings` directly to detect changes — it just subscribes to `SettingsTabViewModel.PropertyChanged` and filters on `nameof(IconsRoot)`. This:
- Reuses the WPF binding infrastructure already firing notifications for the TextBox
- Avoids polling or timers
- Filters per-property so unrelated Settings changes (GamePath, theme, etc.) don't trigger expensive rebuilds
- Keeps `V2Settings` (the persistence record) free of event machinery

**NEW pattern observation — INPC as cross-VM signal bus**: WPF MVVM apps already wire INPC for binding refresh; reusing it for cross-VM hot-swap signals avoids inventing a new event channel. Codification candidate at 3rd recurrence.

### *Hot-swap + immediate refresh is the right user contract*

`SetIconResolver` immediately calls `RefreshFilteredTypes()` so the operator sees the change at edit-time, not at next filter/search/domain change. Without the immediate refresh, operators would type a new IconsRoot, see no change, and assume the wiring is broken — even though the underlying state is correct. **Always pair state mutation with the visible-change trigger** when the state is rendered through a derived collection.

### *Status badge text mirrors implementation reality*

iter-310's `"restart editor for changes to take effect"` was an honest-defer statement that became inaccurate at iter-312. The badge text update from "restart editor" → "updates live" reflects this. Codification candidate `feedback_status_badge_tracks_implementation_reality.md` at 3rd recurrence — pinned status text MUST be updated when the underlying behavior changes, OR the test catches the drift.

## What's intentionally NOT done in iter-312 (deferred to iter-313+)

- **Asset Browser tab** — separate panel showing all extracted icons in thumbnail grid. ~150-250 LoC + XAML.
- **Hero portraits** — extend `UnitIconResolver` filename convention beyond `i_button_<name>.dds`. ~30-50 LoC.
- **Faction emblems on PlayerState tab** — same resolver pattern, different filename convention.
- **Audit B last wire** — `faction-roster-by-build-tab` from iter-294 audit (last of 6).
- **Unsubscribe handling** — current code subscribes in ctor but never unsubscribes. MainViewModelV2 lives for the editor session lifetime so practically irrelevant; if MainViewModelV2 ever gets reconstructable (multi-window mode etc.) the unsubscribe must be added.
- **Pre-existing CS8602 nullable warnings** — 5 unrelated test files (Iter161/166/209/214/217) still pending dedicated cleanup iter.

## Verification checklist

- [x] SpawningTabViewModel `_iconResolver` field made mutable
- [x] `SetIconResolver` method shipped with immediate refresh
- [x] MainViewModelV2 subscribes to Settings.PropertyChanged
- [x] `OnSettingsPropertyChanged` filters on IconsRoot property name
- [x] iter-310 status badge text updated to drop "restart editor" claim
- [x] iter-310 pin test updated to track new badge string
- [x] 5 iter-312 pin tests authored (null↔valid swap + filter + order)
- [x] xUnit `[Collection("ThumbnailCacheEnv")]` for env-var orthogonality with iter-307+308
- [x] **iter-312 pin tests Passed 5/5 in 31 ms** ✓
- [x] **Combined Thread D Passed 70/70 in 209 ms** ✓ (no regression in iter-307/308/309/310)
- [x] Editor build GREEN
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [ ] State docs synced (next step in close-out)
- [ ] Task #563 marked completed; iter-313 (Asset Browser tab OR hero portraits OR Audit B) queued
