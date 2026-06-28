# Ralph loop changelog — UI Integration Arc (iter 313-319)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Thread D arc, post-finale UI integration phase)
**Iters covered:** iter-313 → iter-319 (7 iters; 5 substantive + 2 pivot/codification)
**Status at end-of-arc:** **ALL 4 ASSET CLASSES OPERATOR-VISIBLE end-to-end across 4 tabs**

## Executive summary

The Thread D arc shipped end-to-end .meg-to-WPF-icon plumbing in iter 304-312 (Python extraction + DDS decode + thumbnail cache + C# resolver + Settings UI + live hot-swap). Iter 313-319 ships the **UI consumer surface** — every asset class that the resolver supports now has an operator-visible icon column on its native tab.

| Iter | Asset class | Resolver method | Tab | Default size | UI shape |
|------|-------------|-----------------|-----|--------------|----------|
| 308 | Unit icons | `Resolve(typeName)` | Spawning | 32px | ListBox DataTemplate |
| 313 | Hero portraits (resolver only) | `ResolvePortrait(name)` | — | 64px | (no UI yet) |
| 314 | Faction emblems (resolver only) | `ResolveFactionEmblem(name)` | — | 48px | (no UI yet) |
| 315 | Planet icons (resolver only) | `ResolvePlanetIcon(name)` | — | 96px | (no UI yet) |
| 316 | (codification) | — | — | — | — |
| 317 | Planet icons | `ResolvePlanetIcon(name)` | Galactic | 32px in 40px row | DataGrid TemplateColumn |
| 318 | Hero portraits | `ResolvePortrait(name)` | Hero Lab | 64px in 72px row | DataGrid TemplateColumn |
| 319 | Faction emblems | `ResolveFactionEmblem(name)` | Player State | 24px in ComboBox row | ComboBox ItemTemplate |

**Cumulative end state**: 4 asset classes × 4 tabs = 4 operator-visible icon surfaces. Single iter-309 `iconResolver` instance flows through MainViewModelV2 to all 4 consumer tabs and hot-swaps via single OnSettingsPropertyChanged handler.

## Operator workflow (now end-to-end)

1. Operator pre-extracts MasterTextures.meg via Python CLI:
   ```powershell
   python tools/asset_extractor/meg_parser.py extract MasterTextures.meg --out C:/swfoc_extracted_dds/
   ```
2. Operator runs the thumbnail cache warmer:
   ```powershell
   python tools/asset_extractor/thumbnail_cache.py warm C:/swfoc_extracted_dds/
   ```
3. Operator launches editor (`publish/SwfocTrainer.App.exe`).
4. Settings tab → "Unit icons (Spawning tab)" GroupBox → Browse... → pick `C:/swfoc_extracted_dds/`.
5. **All 4 tabs immediately render icons** (no editor restart):
   - **Spawning tab**: Unit-type ListBox shows in-game unit icons (32px).
   - **Galactic tab**: Planet DataGrid shows planet icons (32px, in 40px-tall rows).
   - **Hero Lab tab**: Hero DataGrid shows hero portraits (64px, in 72px-tall rows).
   - **Player State tab**: Slot ComboBox shows faction emblems (24px) next to "Slot N — FACTION".

## Per-tab operator checklist

### Spawning tab (iter-308; pre-arc)
- [ ] Open Spawning tab
- [ ] Verify the unit-type ListBox shows in-game icons next to each unit name
- [ ] Confirm icons render at 32px scale
- [ ] Type a search query → confirm filter still works
- [ ] Pick a unit → spawn it → confirm spawn flow unchanged

### Galactic tab (iter-317)
- [ ] Open Galactic tab → wait for auto-refresh (or click Refresh planets)
- [ ] Verify planet DataGrid first column shows planet icons (32px)
- [ ] Confirm planet name + owner + tech columns still populated
- [ ] Hover a planet icon → confirm tooltip shows the planet name
- [ ] Confirm row height = 40 (visually larger than the iter-200 FOW reveal section)

### Hero Lab tab (iter-318)
- [ ] Open Hero Lab tab → wait for auto-refresh (or click Refresh heroes)
- [ ] Verify hero DataGrid first column shows hero portraits (64px)
- [ ] Confirm addr/type/owner/alive/respawn/enabled columns still populated
- [ ] Confirm row height = 72 (taller than other tabs to fit the larger portrait)
- [ ] Hover a portrait → confirm tooltip shows the hero TypeName

### Player State tab (iter-319)
- [ ] Open Player State tab → click "Refresh slot map" to populate factions
- [ ] Verify Slot ComboBox dropdown shows faction emblem (24px) next to each "Slot N — FACTION" label
- [ ] Confirm ComboBox width = 280 (was 240 — bumped to fit the emblem column)
- [ ] Hover an emblem → confirm tooltip shows the faction name

### Hot-swap behavior (all 4 tabs)
- [ ] In Settings tab, change IconsRoot to a different valid path
- [ ] Switch to any of the 4 consumer tabs → confirm icons re-resolve immediately
- [ ] Clear IconsRoot in Settings → confirm icons disappear gracefully (no broken-image placeholders)
- [ ] No editor restart required

## Pattern lessons capstone

### Pattern shape per UI surface

The UI integration phase confirmed 3 distinct UI shapes for icon columns, each with its own consumer pattern:

| UI shape | Original row model | Iter-XXX pattern | Cost ratio |
|----------|-------------------|------------------|------------|
| ListBox DataTemplate | string list | iter-308 parallel collection (`UnitTypeRow` record) | baseline |
| DataGrid TemplateColumn | record collection | iter-317/318 parallel collection (`PlanetRowWithIcon` / `HeroRowWithPortrait` records) | ~1× |
| ComboBox ItemTemplate | INPC class collection | iter-319 in-place INPC class extension (`PlayerSlotEntry.IconPath`) | ~0.7× |

**Codification candidate `feedback_inpc_class_extension_vs_parallel_collection.md`** (1st instance — flagged at iter-319): when the underlying row model is an INPC class (not a record), extending it in-place with a new INPC property is cheaper than building a parallel collection. Codification at 3rd recurrence.

### Defensive `_collection.ToList()` snapshot for async-mutated collections

Hit at all 3 iter-317/318/319 iters. Production code that iterates a collection that's also touched by ctor's fire-and-forget refresh needs a defensive `.ToList()` snapshot. Pattern repeated 3 times — codification candidate at 3rd recurrence (might already be ready).

### Timing-independent test design via sentinel IDs

Iter-317 PlanetRowWithIcon tests + iter-318 HeroRowWithPortrait tests both use sentinel IDs (`TestPlanet9999`, `TestHero9999`) that won't collide with simulator output, plus `await Task.Delay(200)` after VM construction to let ctor's async refresh settle. Codification candidate at 3rd recurrence (PlayerState iter-319 didn't need this because it doesn't auto-refresh).

### Audit-by-fail catches silent drifts ~20 iters late

iter-317 caught 4 silent iter-296 drifts (catalog promotion not propagated to regression tests).
iter-318 caught 1 silent iter-295 drift (auto-refresh added to ctor, count-pin test stale).
**Total: 5 silent drifts caught in 2 iters of broader regression filtering.**

Reinforces `feedback_allactions_count_pin_drift.md` (iter-195/iter-208 codified): full-suite runs every 5 iters of an arc would have caught these at the source iter.

### "Delay commitment" trio applied 4 iters in a row

iter-316 (`feedback_extract_on_second_use.md` codified) capped the 3-rule trio:
- iter-302 `engine-already-does-this`
- iter-311 `optional-default-null-constructor-extension`
- iter-316 `extract-on-second-use`

Iter-317/318/319 all applied **all 3 rules simultaneously**:
- Reused iter-313/314/315 `Resolve*` methods (engine-already)
- Extended VM ctors with `UnitIconResolver? iconResolver = null` (optional-default-null)
- Plugged into the iter-313 `LocateByConvention` abstraction at slots 4/5/6 (extract-on-second-use)

The trio is now **load-bearing across the entire UI integration arc** — every iter inherits the pattern shape automatically.

## Cumulative tally (iter 313-319 7-iter arc)

| Category | Count |
|----------|-------|
| Source files modified across the arc | 7 (UnitIconResolver + 4 VMs + MainViewModelV2 + MainWindowV2.xaml) |
| New record types | 2 (PlanetRowWithIcon iter-317 + HeroRowWithPortrait iter-318) |
| New INPC class extensions | 1 (PlayerSlotEntry.IconPath iter-319) |
| New tests | 33 facts (12 iter-317 + 12 iter-318 + 9 iter-319) |
| Inline drift fixes | 5 (4 iter-296 catalog at iter-317 + 1 iter-295 at iter-318) |
| New pattern observations flagged for codification | 4 (async ctor test design + ToList snapshot + count-by-name + INPC vs parallel-collection) |
| Test pass rate at end-of-arc | iter-317 12/12 + iter-318 60/60 (broader filter) + iter-319 42/42 |
| Bridge harness (unchanged across arc) | 1100/0 |
| Verifier ledger lint (unchanged across arc) | 0/0 at 318 entries |
| Marginal cost per asset class N+1 | ~50-450 LoC depending on UI shape |

## Honest defer to iter-321+

| Item | Why deferred | Recommended iter |
|------|-------------|------------------|
| Asset Browser tab | Heaviest scope (~150-250 LoC + new XAML tab); orthogonal to per-tab consumer pattern | iter-321 |
| Audit B last wire (`faction-roster-by-build-tab`) | iter-299 honest defer; needs additional bridge wire | iter-322+ |
| Live SWFOC verify against operator's real MasterTextures.meg | Requires running the actual game | iter-323+ |
| Weapon/ability icon classes | 2 more asset classes; same pattern but different resolver methods | iter-324+ |
| iter-319 close-out doc (per-iter doc, not the changelog) | Consolidated into this changelog | (skipped) |

## Next-session pickup

iter-321 will pick from:
1. **Asset Browser tab kickoff** (HIGH operator value; closes the iter-313 honest defer; ~150-250 LoC)
2. **iter-308/313/314/315/317/318/319 README capstone update** (mirrors iter-222/254/265/273 cadence; ~30-iter capstone interval is overdue)
3. **Phase2HookPending re-audit pass** (mirrors iter-132/221/250/266/274 cadence; ~16-iter cadence due)

**Recommendation**: Option 1 (Asset Browser) — natural progression after the per-tab consumer surface is exhausted. iter-322+ for Asset Browser follow-up + Audit B + live verify.
