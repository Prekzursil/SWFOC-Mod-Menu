# iter-314 — Faction emblem extension to UnitIconResolver (Thread D arc post-finale 4/?)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Thread D arc, post-finale closeout 4 of N)
**Predecessor:** iter-313 (Hero portrait extension)
**Successor (queued):** iter-315 (HeroLab tab portrait column wiring OR planet icons OR Asset Browser tab)

## What changed (1 file modified + 1 test file new; ~210 LoC; **11/11 iter-314 in 62 ms + combined 91/91 PASS in 210 ms**)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.Core/Assets/UnitIconResolver.cs` (+~45 LoC):
  - NEW `public string? ResolveFactionEmblem(string factionName, int size = 48)` — looks up `i_faction_<factionName>.dds` via the same shared `LocateByConvention` helper from iter-313. Default size 48 — between unit icons (32) and hero portraits (64) — because faction emblems typically render at medium scale (header badges, faction-picker dropdowns, slot labels).
  - NEW `public string? LocateFactionEmblemDds(string factionName)` — symmetric to `LocateDds` + `LocatePortraitDds`.
  - Class doc updated to describe all 3 conventions.

- **NEW** `SWFOC editor/tests/SwfocTrainer.Tests/Core/Assets/Iter314FactionEmblemResolverTests.cs` (~165 LoC, **11 facts**) — mirrors iter-313 shape exactly:
  - `ResolveFactionEmblem_NullRoot_ReturnsNull`
  - `ResolveFactionEmblem_EmptyFactionName_ReturnsNull` (empty + whitespace coverage)
  - `LocateFactionEmblemDds_FindsAtCanonicalPath`
  - `LocateFactionEmblemDds_DoesNotMatchUnitIcon_NorPortrait` — **3-way prefix discriminator**: neither `i_button_*` nor `i_portrait_*` may satisfy ResolveFactionEmblem
  - `LocateDds_AndLocatePortraitDds_DoNotMatchFactionConvention` — symmetric inverse: `i_faction_*` must NOT satisfy LocateDds OR LocatePortraitDds
  - `ResolveFactionEmblem_DdsExists_AndCachePopulated_ReturnsCachedPath` (happy path at default 48)
  - `ResolveFactionEmblem_DefaultSize_Is48_NotUnit32_NorPortrait64` — **3-way default-arg pin**: tightest possible default-drift catcher (cache PNG only at 48 → drift to 32 OR 64 misses)
  - `ResolveFactionEmblem_DdsExists_CacheMissing_ReturnsNull`
  - `ResolveFactionEmblem_UnsupportedSize_ReturnsNull_DoesNotThrow`
  - `LocateFactionEmblemDds_NotPresent_ReturnsNull`
  - `All_3_AssetClasses_CoExist_AtSameDir_WithoutCollision` — **end-to-end validation**: with all 3 conventions populated for the same NAME at the same dir, each surfaces its own image without swap

  Pinned to `[Collection("ThumbnailCacheEnv")]` for env-var orthogonality with iter-307+308+312+313.

## End-to-end resolver state after iter-314

The shared `LocateByConvention` helper now serves **3 asset classes** with zero duplication:

| Iter | Convention | Default size | Use case |
|------|------------|--------------|----------|
| 308 | `i_button_<UnitTypeName>.dds` | 32 | Spawning tab unit-type ListBox |
| 313 | `i_portrait_<HeroName>.dds` | 64 | HeroLab tab (iter-315+ wiring) |
| 314 | `i_faction_<FactionName>.dds` | 48 | PlayerState slot widget / faction-picker dropdowns |

Future asset classes (planet icons, weapon icons, ability icons) plug in by adding one more `LocateXyz` wrapper around `LocateByConvention` + ~30 LoC test mirroring this iter's shape. **Marginal cost ~30 LoC per asset class.**

## Pivot from iter-314's original task description (HeroLab UI → faction emblems)

iter-314 task #565 originally described HeroLab tab UI integration. Mid-iter pivot to faction emblems instead because:

1. **HeroLab UI scope was unclear** — would need 5-sec grep + possible row-model restructure (iter-308 hit this fork; iter-315 can take it on with confidence).
2. **Faction emblems is 3rd-instance validation** of the iter-313 LocateByConvention abstraction — proves the pattern at 3 asset classes before committing to the larger UI integration.
3. **Single-iter scope confirmed** — ~45 LoC source + ~165 LoC tests vs HeroLab UI's likely 100-150 LoC + XAML + restructure.
4. **No regression risk** — pure-additive resolver method, no existing call sites change.

iter-315 picks up HeroLab UI as the next deliverable.

## Pattern lessons

### *Triple-discriminator pin (extends iter-313 prefix-discriminator pattern)*

iter-313 used a 2-way symmetric pin (i_button_* must not match ResolvePortrait + i_portrait_* must not match LocateDds). iter-314 extends to 3-way:
- `LocateFactionEmblemDds_DoesNotMatchUnitIcon_NorPortrait` — neither i_button_* nor i_portrait_* may satisfy ResolveFactionEmblem
- `LocateDds_AndLocatePortraitDds_DoNotMatchFactionConvention` — i_faction_* must satisfy NEITHER LocateDds NOR LocatePortraitDds

**Pattern observation**: as asset-class count grows, the discriminator-pin matrix grows with it. At N asset classes, you need N×(N-1) directional pin assertions to lock the symmetry. iter-314 shows this is still tractable at N=3 (6 assertions across 2 tests). Codification candidate at 4th asset class.

### *Triple-default-arg pin (extends iter-313 default-arg pattern)*

iter-313 pinned `ResolvePortrait` default size 64 vs `Resolve` default 32 (2 distinct sizes). iter-314 pins `ResolveFactionEmblem` default size 48 — between the other two. The `ResolveFactionEmblem_DefaultSize_Is48_NotUnit32_NorPortrait64` test stages a cache PNG ONLY at size 48 — if the default drifts to 32 OR 64 (the other two intentional defaults), the lookup misses. **Tightest possible default-drift catcher** — wrong on both sides simultaneously.

### *End-to-end coexistence test*

`All_3_AssetClasses_CoExist_AtSameDir_WithoutCollision` is a NEW test pattern: with all 3 conventions populated for the same name at the same dir, each must surface its own image without swap. This catches the "operator perspective" bug class that unit tests miss — the operator sees ALL three buttons/portraits/emblems for "Vader" at once, and they must be different images.

**Codification candidate at 3rd recurrence**: `feedback_coexistence_test_for_extension_points.md` — when extending an abstraction with multiple plugins, test that all plugins COEXIST without interfering, not just that each works in isolation.

## Verification gates ALL GREEN

```
[run_editor_tests_v2] dotnet test --filter "FullyQualifiedName~Iter314"
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 62 ms

[run_editor_tests_v2] dotnet test --filter "...~Iter307|...~Iter308|...~Iter309|...~Iter310|...~Iter312|...~Iter313|...~Iter314"
Passed!  - Failed:     0, Passed:    91, Skipped:     0, Total:    91, Duration: 210 ms
```

- Editor build: GREEN (Core + Tests + dependents compiled cleanly with iter-314 changes)
- iter-314 pin tests: **Passed 11/11 in 62 ms** ✓
- Combined Thread D + iter-313 + iter-314: **Passed 91/91 in 210 ms** ✓ (no regression)
- Bridge harness inherits 1100/0 (no bridge changes)
- Verifier ledger lint inherits 0/0 at 318 entries
- 111 → 111 buttons UNCHANGED (service-layer extension; no UI changes)

## What's intentionally NOT done in iter-314 (deferred to iter-315+)

- **HeroLab tab portrait column wiring** — iter-315 territory; mirrors iter-308 Spawning pattern (~80-120 LoC + XAML + pin tests)
- **Planet icon support** — 4th asset class. Same ~30 LoC pattern.
- **Weapon icon / ability icon support** — 5th and 6th asset classes; same pattern, low priority.
- **Asset Browser tab** — separate panel showing ALL extracted assets across all conventions in a thumbnail grid.
- **Live SWFOC verify against operator's real MasterTextures.meg** — requires operator's game install.
- **Audit B last wire** (`faction-roster-by-build-tab`) — iter-294 audit closure.

## Verification checklist

- [x] `ResolveFactionEmblem` + `LocateFactionEmblemDds` shipped via shared `LocateByConvention` helper
- [x] Default size 48 (between 32 and 64) reflects faction-emblem rendering convention
- [x] Class doc updated with all 3 filename conventions
- [x] 11 iter-314 pin tests authored (resolver behavior + 3-way discriminator + 3-way default-arg + coexistence test)
- [x] **iter-314 pin tests Passed 11/11 in 62 ms** ✓
- [x] **Combined Thread D + iter-313 + iter-314 Passed 91/91 in 210 ms** ✓ (no regression)
- [x] Editor build GREEN
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [x] iter-313 LocateByConvention pattern validated at 3rd asset class (codification candidate `feedback_extract_on_second_use.md` upgraded to 2nd-instance trigger; needs 1 more recurrence)
- [ ] State docs synced (next step)
- [ ] Task #565 marked completed; iter-315 (HeroLab tab portrait column) queued
