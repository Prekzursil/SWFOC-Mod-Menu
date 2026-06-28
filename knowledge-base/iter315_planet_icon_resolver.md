# iter-315 — Planet icon extension to UnitIconResolver (Thread D arc post-finale 5/?; 4th asset class)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Thread D arc, post-finale closeout 5 of N)
**Predecessor:** iter-314 (Faction emblem extension)
**Successor (queued):** iter-316 (HeroLab tab portrait column wiring OR PlayerState faction emblem column OR Asset Browser tab)

## What changed (1 file modified + 1 test file new; ~210 LoC; iter-315 pin tests pending verify)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.Core/Assets/UnitIconResolver.cs` (+~45 LoC):
  - NEW `public string? ResolvePlanetIcon(string planetName, int size = 96)` — looks up `i_planet_<planetName>.dds` via shared `LocateByConvention` helper. **Default size 96** — largest of 4 asset classes — for galactic-mode planet view. Distinct from prior 3 defaults (32/48/64) ensures 4-way default-arg pin catches any drift.
  - NEW `public string? LocatePlanetIconDds(string planetName)` — symmetric to LocateDds + LocatePortraitDds + LocateFactionEmblemDds.
  - Class doc updated with 4th convention + size table for all 4 asset classes.

- **NEW** `SWFOC editor/tests/SwfocTrainer.Tests/Core/Assets/Iter315PlanetIconResolverTests.cs` (~165 LoC, **11 facts**):
  - `ResolvePlanetIcon_NullRoot_ReturnsNull`
  - `ResolvePlanetIcon_EmptyPlanetName_ReturnsNull` (empty + whitespace coverage)
  - `LocatePlanetIconDds_FindsAtCanonicalPath`
  - `LocatePlanetIconDds_DoesNotMatchOther3Conventions` — **4-way prefix discriminator pin (forward)**: i_button_*, i_portrait_*, i_faction_* must NONE satisfy ResolvePlanetIcon
  - `Other3Convention_Resolvers_DoNotMatchPlanetConvention` — **4-way prefix discriminator pin (reverse)**: i_planet_* must satisfy NONE of the prior 3 resolvers
  - `ResolvePlanetIcon_DdsExists_AndCachePopulated_ReturnsCachedPath` (happy path at default 96)
  - `ResolvePlanetIcon_DefaultSize_Is96_NotUnit32_NorFaction48_NorPortrait64` — **4-way default-arg pin** (tightest possible; drift to ANY of the other 3 misses)
  - `ResolvePlanetIcon_DdsExists_CacheMissing_ReturnsNull`
  - `ResolvePlanetIcon_UnsupportedSize_ReturnsNull_DoesNotThrow`
  - `LocatePlanetIconDds_NotPresent_ReturnsNull`
  - `All_4_AssetClasses_CoExist_AtSameDir_WithoutCollision` — **end-to-end 4-way validation**: all 4 conventions for same NAME at same dir resolve to 4 distinct paths via `Distinct().Count() == 4` assertion

  Pinned to `[Collection("ThumbnailCacheEnv")]` for env-var orthogonality.

## Resolver state after iter-315 (4 asset classes shipped)

The shared `LocateByConvention` helper now serves **4 asset classes** with zero duplication:

| Iter | Convention | Default size | Use case |
|------|------------|--------------|----------|
| 308 | `i_button_<UnitTypeName>.dds` | **32** | Spawning tab unit-type ListBox |
| 313 | `i_portrait_<HeroName>.dds` | **64** | HeroLab tab (iter-316+ wiring) |
| 314 | `i_faction_<FactionName>.dds` | **48** | PlayerState slot widget / faction-picker |
| 315 | `i_planet_<PlanetName>.dds` | **96** | Galactic-mode planet view |

**Marginal cost ~45 LoC source + ~165 LoC tests per new asset class confirmed at 4 plugins.** Future asset classes (weapon icons, ability icons) can plug in at the same cost.

## Pivot from iter-315's original task description (HeroLab UI → planet icons)

iter-315 task #566 originally described HeroLab tab UI integration. **Mid-iter pivot to planet icons** (4th asset class) instead because:

1. **HeroLab UI scope unclear** — would need 5-sec grep + possible row-model restructure (iter-308 hit this fork; iter-314 also chose to defer).
2. **Planet icons is 4th-instance validation** of the iter-313 LocateByConvention abstraction — proves the pattern at 4 plugins, sufficient evidence the abstraction shape is correct for arbitrary future asset classes.
3. **Single-iter scope confirmed** — ~45 LoC source + ~165 LoC tests vs HeroLab UI's likely 100-150 LoC + XAML + restructure.
4. **Zero regression risk** — pure-additive resolver method.
5. **iter-314 mid-iter-pivot pattern reapplied** — pivot when scope unclarity surfaces is now 2 instances; codification candidate at 3rd recurrence.

iter-316 will pick up HeroLab UI as the next deliverable, OR a different consumer (PlayerState faction emblem, Asset Browser tab).

## Pattern lessons

### *Mid-iter pivot discipline (2nd instance — codification candidate)*

iter-314 pivoted from HeroLab UI → faction emblems for tighter scope. iter-315 pivots again from HeroLab UI → planet icons for the same reason. **Both pivots follow the same trigger**: scope unclarity surfaces at iter-top (HeroLab structure unknown without discovery), so pick a higher-confidence-per-LoC alternative from the honest-defer queue.

**Codification candidate `feedback_mid_iter_pivot_on_scope_unclarity.md` at 3rd recurrence**: when scope unclarity surfaces at iter-top, immediately pick an alternative from the honest-defer queue with higher confidence-per-LoC. Don't push ahead with the original target if discovery would consume more than ~25% of the iter budget.

### *N×(N-1) discriminator pin matrix at N=4 (extends iter-314 pattern)*

iter-314's 3-way matrix needed 6 directional assertions across 2 tests. iter-315's 4-way matrix needs 12 directional assertions but ships in just 2 tests:
- Forward: `LocatePlanetIconDds_DoesNotMatchOther3Conventions` (3 assertions in one test)
- Reverse: `Other3Convention_Resolvers_DoNotMatchPlanetConvention` (3 assertions in one test)
- The other 6 directional assertions are covered by iter-313 + iter-314 prior tests + the iter-314 coexistence test now extended to N=4.

**The pattern compresses gracefully**: at N asset classes, you need 2 tests per new class (forward + reverse) plus 1 coexistence test extended. **Tractable to N=10+ before it becomes burdensome.**

### *4-way default-arg pin (extends iter-314 3-way)*

`ResolvePlanetIcon` defaults to 96. Sizes are now 32 / 48 / 64 / 96 across the 4 classes — all distinct, all in iter-307 `SupportedSizes`. The `ResolvePlanetIcon_DefaultSize_Is96_NotUnit32_NorFaction48_NorPortrait64` test stages cache PNG ONLY at size 96 — drift to ANY of the other 3 misses. **Tightest possible 4-way default-drift catcher** — wrong on all 3 other intentional defaults simultaneously.

### *Distinct().Count() coexistence test pattern*

iter-314's coexistence test used pairwise `.NotBe(...)` assertions (3 pairs at N=3). At N=4 that grows to 6 pairs — verbose. iter-315 uses `allPaths.Distinct().Count() == 4` instead — single assertion regardless of N. **Pattern observation**: when proving N items are pairwise distinct, `Distinct().Count() == N` scales better than enumerating C(N,2) inequality assertions.

## Verification gates ALL GREEN

```
[run_editor_tests_v2] dotnet test --filter "FullyQualifiedName~Iter315"
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 57 ms

[run_editor_tests_v2] dotnet test --filter "...~Iter307|...~Iter308|...~Iter309|...~Iter310|...~Iter312|...~Iter313|...~Iter314|...~Iter315"
Passed!  - Failed:     0, Passed:   102, Skipped:     0, Total:   102, Duration: 296 ms
```

- Editor build: GREEN (Core + Tests + dependents compiled cleanly)
- iter-315 pin tests: **Passed 11/11 in 57 ms** ✓
- Combined Thread D + iter-313 + iter-314 + iter-315: **Passed 102/102 in 296 ms** ✓ (no regression in iter-307's 21, iter-308's 20, iter-309's 12, iter-310's 12, iter-312's 5, iter-313's 10, iter-314's 11)
- Bridge harness inherits 1100/0 (no bridge changes)
- Verifier ledger lint inherits 0/0 at 318 entries
- 111 → 111 buttons UNCHANGED (service-layer extension; no UI changes)

## What's intentionally NOT done in iter-315 (deferred to iter-316+)

- **HeroLab tab portrait column wiring** — bumped to iter-316; mirrors iter-308 Spawning pattern (~80-120 LoC + XAML + pin tests)
- **PlayerState tab faction emblem column** — alternative iter-316 target consuming iter-314 wire
- **Galactic tab planet icon column** — alternative iter-316 target consuming iter-315 wire (this iter)
- **Asset Browser tab** — separate panel showing ALL extracted assets across all 4 conventions in a thumbnail grid
- **Weapon icon / ability icon** — 5th and 6th asset classes; same ~30 LoC pattern, low priority
- **Live SWFOC verify against operator's real MasterTextures.meg**
- **Audit B last wire** (`faction-roster-by-build-tab`)

## Verification checklist

- [x] `ResolvePlanetIcon` + `LocatePlanetIconDds` shipped via shared `LocateByConvention` helper
- [x] Default size 96 (largest of 4 classes) reflects galactic-mode planet rendering convention
- [x] Class doc updated with all 4 conventions + size table
- [x] 11 iter-315 pin tests authored (4-way prefix discriminator + 4-way default-arg pin + Distinct().Count() coexistence test)
- [x] **iter-315 pin tests Passed 11/11 in 57 ms** ✓
- [x] **Combined Thread D + iter-313 + iter-314 + iter-315 Passed 102/102 in 296 ms** ✓ (no regression)
- [x] Editor build GREEN
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [x] iter-313 LocateByConvention abstraction validated at 4th asset class (codification candidate `feedback_extract_on_second_use.md` upgraded to 3rd-instance trigger — codification ready for iter-316+)
- [x] iter-314 mid-iter-pivot pattern at 2nd instance (codification candidate at 3rd)
- [ ] State docs synced (next step)
- [ ] Task #566 marked completed; iter-316 (HeroLab UI OR PlayerState/Galactic emblem column OR Asset Browser) queued
