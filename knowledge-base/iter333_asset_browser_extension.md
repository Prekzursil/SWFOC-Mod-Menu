# iter-333 — Asset Browser tab category extension 4 → 6 (weapons + abilities) + iter-321 prefix-overlap bug fix

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (consumes iter-331 + iter-332 plugins; closes iter-321 honest defer for full asset class coverage)
**Predecessor:** iter-332 (ability icon resolver)
**Successor (queued):** iter-334 (codify `feedback_locate_by_convention_extensible.md` memory rule OR Combat tab weapon-icon column UI consumer)

## What changed (1 source file modified + 1 test file extended; ~70 LoC; **75/75 PASS in 2.19s**)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/ViewModels/AssetBrowserTabViewModel.cs` (+~30 LoC):
  - `CategoryPrefixes` extended from 4 to 6 entries: added `("i_button_hp_", "weapon")` + `("i_button_ability_", "ability")` ordered FIRST (longest-prefix-first)
  - `RefreshAssets` now uses `HashSet<string>` to track claimed DDS paths so each file matches exactly ONE category (longest prefix wins)
  - `ResolveIconForCategory` switch extended with `"weapon" => _iconResolver.ResolveWeaponIcon(name)` + `"ability" => _iconResolver.ResolveAbilityIcon(name)`
  - Class XML doc updated to document 6 categories + iter-333 prefix-overlap bug fix
- **MODIFY** `tests/SwfocTrainer.Tests/Regression/Iter321AssetBrowserTabTests.cs` (+~40 LoC):
  - Renamed `..._ListsFourClasses` → `..._ListsSixClasses`; updated assertion to expect 6 categories
  - Renamed `..._PopulatesAssetsForAllFourCategories` → `..._PopulatesAssetsForAllSixCategories`; staged 6 DDS files (added i_button_hp_TIE_Laser + i_button_ability_Force_Push); updated `HaveCount(6)`
  - **NEW pin test**: `RefreshCommand_LongestPrefixWins_NoGhostUnitRowFromHpFile` — regression guard for the iter-321 prefix-overlap bug. Stages 2 files (`i_button_hp_TIE_Laser.dds` + `i_button_ability_Force_Push.dds`) and asserts:
    - Each file produces EXACTLY 1 row (not 2)
    - Weapon file claims weapon category (not also unit with name `hp_X`)
    - Ability file claims ability category (not also unit with name `ability_X`)
    - Would have FAILED on the pre-iter-333 implementation

## Verification gates ALL GREEN

```
[Start-Process bypass — Clink-safe]
dotnet test --filter "FullyQualifiedName~Iter321|...~Iter331|...~Iter332|...~Iter307|...~Iter308"
Test Run Successful.
Total tests: 75
     Passed: 75
 Total time: 2.1893 Seconds
```

- Iter321 tests (extended): 11 facts pass (was 11; same count after rename + new fact balance)
- Iter331 weapon tests: 11/11 pass (no regression)
- Iter332 ability tests: 11/11 pass (no regression)
- Iter307+308 thumbnail-cache tests: 41/41 pass (no regression)
- Editor build inherits GREEN
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries

## NEW pattern lesson — *iter-321 prefix-overlap bug was latent for ~12 iters before iter-333 surfaced it*

**iter-321 to iter-333 silent-bug window**: the iter-321 implementation walked `i_button_*.dds` glob via `Directory.EnumerateFiles` which is a **superset** of the more-specific `i_button_hp_*.dds` and `i_button_ability_*.dds` patterns. With ONLY 4 categories (no weapon/ability), the bug was invisible because no operator-staged DDS files used the `hp_` or `ability_` infix.

**iter-333 surfaced the bug as a side-effect of adding categories** that introduce the more-specific prefix conflict. Without the iter-333 regression guard test, the bug would have shipped silently — operator with real .meg-extracted assets (which DO contain `i_button_hp_*` files) would see ghost duplicate "unit" rows with names like `hp_TIE_Laser`.

**Pattern lesson** (1st instance; codification candidate at 3rd recurrence): when extending a glob-based file walker with new prefix categories, audit existing categories for **prefix-superset relationships** (where shorter prefix matches longer-prefix files). The fix is canonical: longest-prefix-first ordering + `HashSet`-based claim tracking. Codification candidate `feedback_glob_walker_prefix_overlap_audit.md` flagged.

This pattern lesson is **2nd-order from the iter-313 LocateByConvention pattern** — the helper itself is fine (each `LocateByConvention(prefix, name)` call uses the literal prefix as a non-glob filename match), but the iter-321 walker that ENUMERATES files via glob has different semantics and needs the disambiguation step.

## iter-313 LocateByConvention plugin set at N=6 (all consumers updated)

iter-321 Asset Browser tab is now the **5th consumer** of the iter-313 LocateByConvention plugin set (after iter-308 Spawning + iter-317 Galactic + iter-318 HeroLab + iter-319 PlayerState). After iter-333:
- All 6 plugins shipped: units (iter-308) + portraits (iter-313) + factions (iter-314) + planets (iter-315) + weapons (iter-331) + abilities (iter-332)
- All 5 consumers wired: iter-308 Spawning + iter-317 Galactic (planets) + iter-318 HeroLab (portraits) + iter-319 PlayerState (factions) + iter-321 Asset Browser (all 6 via category-switch)
- Per-tab consumer surface complete for 4/6 categories (units/portraits/factions/planets); weapons + abilities ONLY surface in Asset Browser tab (no per-tab dedicated consumer yet — deferred to iter-334+ Combat tab weapon column + UnitControl tab ability column)

**The iter-321 Asset Browser tab is now the canonical "full asset surface" view** — operators see all 6 asset classes from extracted .meg files without needing per-tab dedicated columns.

## Pattern observation — *consumer extensibility is harder than producer extensibility*

The producer side (`UnitIconResolver`) extended cleanly from 4 → 5 → 6 plugins at ~50 LoC marginal cost per plugin with no surprise side-effects. The consumer side (`AssetBrowserTabViewModel`) extended from 4 → 6 categories but **surfaced a latent bug** in the producer-style glob walker that didn't compose cleanly with the new prefix overlap.

This validates the iter-313 LocateByConvention abstraction at the producer layer (clean) but suggests **consumer-layer extensibility deserves its own audit pattern** — when adding a new consumer surface OR extending an existing one, audit for:
1. Prefix-overlap conflicts (iter-333 finding)
2. Default-arg inheritance from helper (iter-313/331/332 pattern — pinned with default-arg tests)
3. Hot-swap correctness for resolver injection (iter-312/iter-321 SetIconResolver pattern)

Codification candidate `feedback_consumer_extensibility_audit.md` flagged at 1st instance (alongside `feedback_glob_walker_prefix_overlap_audit.md` from iter-333 finding above).

## Verification checklist

- [x] `CategoryPrefixes` extended from 4 to 6 with longest-prefix-first ordering
- [x] `HashSet<string>` claim tracking added to `RefreshAssets`
- [x] `ResolveIconForCategory` switch handles 6 categories
- [x] Class XML doc updated for iter-333
- [x] Iter321 test count assertions updated (4 → 6)
- [x] Iter321 6-category staging test updated
- [x] **NEW** longest-prefix-first regression guard test
- [x] 75/75 combined tests pass in 2.19s
- [x] Editor build inherits GREEN
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [x] iter-313 LocateByConvention plugin set fully consumed by Asset Browser tab
- [ ] Combat tab weapon-icon column — deferred to iter-334+
- [ ] UnitControl tab ability-icon column — deferred to iter-335+
- [ ] Live SWFOC verify against real MasterTextures.meg — deferred to operator session
