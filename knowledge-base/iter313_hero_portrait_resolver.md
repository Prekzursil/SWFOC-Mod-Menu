# iter-313 — Hero portrait extension to UnitIconResolver (Thread D arc post-finale 3/3)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Thread D arc, post-finale closeout 3/3)
**Predecessor:** iter-312 (Live VM rebuild on Settings.IconsRoot change)
**Successor (queued):** iter-314 (HeroLab tab portrait wiring + faction emblems OR Asset Browser tab OR Audit B last wire)

## What changed (1 file modified + 1 test file new; ~220 LoC; **10/10 iter-313 + combined 80/80 PASS**)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.Core/Assets/UnitIconResolver.cs` (+~55 LoC):
  - Refactored `LocateDds` to delegate to NEW `private string? LocateByConvention(string filenamePrefix, string assetName)` helper that walks the 5-candidate-relpath list with a configurable filename prefix.
  - NEW `public string? ResolvePortrait(string heroName, int size = 64)` — looks up `i_portrait_<heroName>.dds` via the same 5-relpath walk + iter-307 ThumbnailCache lookup. Default size 64 (vs unit icons' 32) because portraits typically render larger.
  - NEW `public string? LocatePortraitDds(string heroName)` — symmetric to `LocateDds` for the portrait convention.
  - Class doc updated to describe both conventions.
  - Eliminates duplicated-walk drift risk inside the resolver: future asset types plug in by adding another `LocateXyz` wrapper around `LocateByConvention`.

- **NEW** `SWFOC editor/tests/SwfocTrainer.Tests/Core/Assets/Iter313HeroPortraitResolverTests.cs` (~165 LoC, **10 facts**):
  - `ResolvePortrait_NullRoot_ReturnsNull`
  - `ResolvePortrait_EmptyHeroName_ReturnsNull` (empty + whitespace coverage)
  - `LocatePortraitDds_FindsAtCanonicalPath`
  - `LocatePortraitDds_DoesNotMatchUnitIconConvention` — **prefix discriminator pin**: `i_button_*` MUST NOT satisfy ResolvePortrait
  - `LocateDds_DoesNotMatchPortraitConvention` — symmetric inverse pin
  - `ResolvePortrait_DdsExists_AndCachePopulated_ReturnsCachedPath` (happy path at default size 64)
  - `ResolvePortrait_DefaultSize_Is64_NotUnit32` — **default-arg pin**: prevents future refactor from silently matching the unit-icon default
  - `ResolvePortrait_DdsExists_CacheMissing_ReturnsNull` (matches iter-308 graceful-null contract)
  - `ResolvePortrait_UnsupportedSize_ReturnsNull_DoesNotThrow`
  - `LocatePortraitDds_NotPresent_ReturnsNull`

  Pinned to `[Collection("ThumbnailCacheEnv")]` for env-var orthogonality with iter-307+308+312.

## Verification gates ALL GREEN

```
[run_editor_tests_v2] dotnet test --filter "FullyQualifiedName~Iter313"
Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 43 ms

[run_editor_tests_v2] dotnet test --filter "...~Iter307|...~Iter308|...~Iter309|...~Iter310|...~Iter312|...~Iter313"
Passed!  - Failed:     0, Passed:    80, Skipped:     0, Total:    80, Duration: 210 ms
```

- Editor build: GREEN (Core + Tests + dependents compiled cleanly)
- iter-313 pin tests: **Passed 10/10 in 43 ms** ✓
- Combined Thread D + iter-313: **Passed 80/80 in 210 ms** ✓ (no regression in iter-307's 21, iter-308's 20, iter-309's 12, iter-310's 12, iter-312's 5)
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- 111 → 111 buttons UNCHANGED (iter-313 ships service-layer extension; HeroLab tab wiring deferred to iter-314)

## Iter-310 duplicated-walk discipline lesson applied immediately (2nd instance — codification trigger reached)

iter-310's `SettingsTabViewModel.CountIconsAtRoot` mirrored iter-308 `UnitIconResolver.LocateDds` 5-relpath walk by hand-copying the candidate-relpath list. Marked as 1/3 instances toward `feedback_duplicated_walk_discipline.md` codification.

iter-313 takes the OPPOSITE approach inside the resolver — extracts `LocateByConvention` as a private helper that BOTH `LocateDds` AND `LocatePortraitDds` delegate to. Zero duplication.

**Codification candidate `feedback_duplicated_walk_discipline.md` reaches 2nd instance**:
- iter-310 case: cross-VM-boundary (Core resolver → App-side counter); cross-reference comment as discipline anchor
- iter-313 case: same-class (LocateDds + LocatePortraitDds); private helper as discipline anchor

Two cases now show the same principle expressed two ways. **One more recurrence triggers codification.**

## Pattern lessons

### *Prefix-discriminator pinning prevents asset-class confusion*

Operators expect "Han Solo's portrait" and "Han Solo's vehicle button" to be DIFFERENT images. Without the prefix discriminator, ResolvePortrait could match `i_button_Han_Solo.dds` and silently surface a unit icon as a hero portrait — operators would see the wrong picture without realizing. Two pin tests (`LocatePortraitDds_DoesNotMatchUnitIconConvention` + `LocateDds_DoesNotMatchPortraitConvention`) lock the discriminator on both sides of the symmetry.

**Codification candidate at 3rd recurrence** — the asymmetric-pair-pinning pattern would generalize beyond asset-class discriminators (e.g. read-vs-write method pairs that share argument shapes).

### *Default-arg pin prevents silent semantic drift*

`ResolvePortrait` defaults to `size: 64` while `Resolve` (unit icons) defaults to `size: 32`. The semantic difference is intentional — portraits render larger. A future refactor that "consolidates" both methods to use a shared default of 32 would silently downscale portraits. The `ResolvePortrait_DefaultSize_Is64_NotUnit32` test stages a cache PNG ONLY at size 64, then calls `ResolvePortrait` without a size arg — if the default drifts to 32, the lookup misses and the test fails.

**Codification candidate at 3rd recurrence** — applies wherever default args carry semantic meaning that consolidation could erase.

### *Extract-on-second-use is the right time to abstract*

When iter-308 shipped `LocateDds`, the 5-relpath walk was inline. When iter-313 needed the same walk for portraits, extracting `LocateByConvention` was correct because:
- Two concrete uses now exist (unit icons + hero portraits)
- Future asset types are imminently expected (faction emblems, planet icons)
- Both uses share the EXACT same walk shape (only filename varies)

Extracting at first use would have been premature abstraction; extracting at second use is when the pattern is clear. **Codification candidate `feedback_extract_on_second_use.md` at 3rd instance.**

## End-to-end operator workflow (after iter-314 wires HeroLab)

```bash
# 1. One-time per game install: extract MasterTextures.meg + cache thumbnails for BOTH conventions
python tools/asset_extractor/meg_parser.py "C:\Games\SWFOC\Data\MasterTextures.meg" --extract-all C:\Games\SWFOC\extracted
for dds in C:\Games\SWFOC\extracted\Data\Art\Textures\Units\i_*.dds; do
    python tools/asset_extractor/thumbnail_cache.py "$dds" --size 32  # for unit icons
    python tools/asset_extractor/thumbnail_cache.py "$dds" --size 64  # for hero portraits
done

# 2. Configure once via Settings tab (already shipped iter-310)
# 3. Spawning tab → unit icons render (iter-308)
# 4. HeroLab tab → hero portraits render (iter-314+ when wired)
```

## What's intentionally NOT done in iter-313 (deferred to iter-314+)

- **HeroLab tab portrait column** — UI integration. Mirrors iter-308 Spawning tab pattern; ~80-120 LoC + XAML + pin tests.
- **Faction emblem support** — third asset class. Add `ResolveFactionEmblem(factionName, size=48)` via `LocateByConvention("i_faction_", factionName)`. ~30 LoC.
- **Planet icon support** — fourth asset class. Same pattern.
- **Asset Browser tab** — separate panel showing ALL extracted assets across all conventions in a thumbnail grid.
- **Live SWFOC verify against operator's real MasterTextures.meg** — requires operator's game install.
- **Audit B last wire** (`faction-roster-by-build-tab`) — last of 6 from iter-294 audit.

## Verification checklist

- [x] `LocateByConvention` private helper extracted (eliminates duplication inside the resolver)
- [x] `ResolvePortrait` + `LocatePortraitDds` shipped via the new helper
- [x] Default size 64 (vs unit-icon 32) reflects portrait rendering convention
- [x] Class doc updated with both filename conventions
- [x] 10 iter-313 pin tests authored (resolver behavior + prefix discriminator + default-arg pin)
- [x] **iter-313 pin tests Passed 10/10 in 43 ms** ✓
- [x] **Combined Thread D + iter-313 Passed 80/80 in 210 ms** ✓ (no regression)
- [x] Editor build GREEN
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [x] iter-310 duplicated-walk discipline lesson applied immediately at 2nd instance
- [x] State docs synced (now.md + ralph_loop_state.md + STATUS.md updated in same iter)
- [x] Task #564 marked completed; iter-314 (HeroLab tab portrait column OR faction emblems OR Asset Browser OR Audit B) queued
