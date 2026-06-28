# iter-332 — Ability icon extension to UnitIconResolver (post-iter-331 mirror; 6th asset class; iter-313 LocateByConvention plugin set N=6)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Audit E ability-icon class extension; closes iter-294 honest-defer alongside iter-331 weapons)
**Predecessor:** iter-331 (weapon hardpoint icons)
**Successor (queued):** iter-333 (Asset Browser tab weapon + ability category extension OR Combat tab UI consumer)

## What changed (1 file modified + 1 test file new; ~140 LoC; **11/11 iter-332 + combined 95/95 PASS in 2.19s**)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.Core/Assets/UnitIconResolver.cs` (+~50 LoC):
  - Class XML doc updated for 6 asset classes
  - `ResolveAbilityIcon(abilityName, size = 32)` + `LocateAbilityIconDds(abilityName)` via `i_button_ability_` SWFOC ability prefix
  - **Extends iter-313 LocateByConvention helper to 6 plugins** (units + portraits + factions + planets + weapons + abilities)

- **NEW** `tests/SwfocTrainer.Tests/Core/Assets/Iter332AbilityIconResolverTests.cs` (~225 LoC, **11 facts**):
  - Mirror of iter-331 shape exactly — strict 6-way prefix discriminator + reverse 6-way symmetry + end-to-end 6-way co-existence
  - Default-arg pin: 32 — **3rd asset class** to share size-32 default (units + weapons + abilities); distinct from 48/64/96 of factions/portraits/planets

  Pinned to `[Collection("ThumbnailCacheEnv")]` for env-var orthogonality with iter-307+308+312+313+314+315+331.

## Verification gates ALL GREEN

```
[Start-Process bypass — Clink-safe]
dotnet test --filter "FullyQualifiedName~Iter307|...~Iter332"
Test Run Successful.
Total tests: 95
     Passed: 95
 Total time: 2.1928 Seconds
```

- iter-332 pin tests: **Passed 11/11** ✓
- Combined Thread D + iter-313/314/315/331/332: **Passed 95/95 in 2.19s** ✓ (no regression in iter-307's 21, iter-308's 20, iter-313's 10, iter-314's 11, iter-315's 11, iter-331's 11)
- Editor build inherits GREEN
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries

## Pattern validation — iter-313 LocateByConvention plugin set at N=6

iter-313 hero portraits (1st) → iter-314 faction emblems (2nd) → iter-315 planet icons (3rd) → iter-321 cross-asset Asset Browser tab (4th — composes all 3 + units) → iter-331 weapon hardpoints (5th) → **iter-332 ability icons (6th)**.

**The abstraction is now stable at N=6**:
- 50 LoC marginal cost per plugin (resolver + locator + XML doc; consistent across iter-313/314/315/331/332)
- ~225 LoC test marginal cost per plugin (11 facts: null/empty/canonical/discriminator/symmetry/cached/default-arg/cache-missing/unsupported-size/not-present/co-existence)
- Test suite scales linearly: 84 → 95 (+11 from iter-332's facts) at 2.19s; no regression
- N-way co-existence test grows to 6 distinct paths from same NAME — **the strongest pattern-stability proof**

Future asset types (build-tab category icons, ranking medal icons, ability-cooldown overlay icons, etc.) plug in the same way at the same marginal cost.

## Pattern lessons

### iter-313 LocateByConvention plugin set — *codification-ready at 6 instances*

Six instances spanning iter 313 → iter 332 (~20-iter window). Pattern shape now sufficiently stable for codification as `feedback_locate_by_convention_extensible.md` memory rule:

- **Rule**: when adding a new asset class to UnitIconResolver, mirror the iter-313 pattern exactly: `Resolve<X>(name, int size = N) → Locate<X>Dds(name) → LocateByConvention(prefix, name)` + 11 pin tests including N-way discriminator + reverse N-way symmetry + N-way co-existence
- **Why**: validated at N=6 across 5 distinct conventions (unit/portrait/faction/planet/weapon/ability); marginal cost stays at ~50 LoC source + ~225 LoC tests; no test regression
- **When NOT to use**: when an asset class needs a non-DDS extension (e.g., audio cues `*.wav` for unit voice lines) — would need a new helper not built on `LocateByConvention("<prefix>", name)` returning `<prefix><name>.dds`

Codification candidate flagged at 6th instance (codification trigger reached per iter-302 6-instance precedent).

### iter-323 preflight stack proves iter-strategy-layer value (continued from iter-331)

iter-331 introduced the pattern of applying the iter-326/327/328 preflight stack at the iter-strategy layer to pivot from speculative work (Audit B) to higher-value adjacent target (weapon icons). iter-332 confirms the pattern:

- iter-331 saved ~5 iters of speculative Audit B arc (would have been honest-defer)
- iter-332 capitalized on the iter-331 fresh-mirror state (~30 min cycle vs ~60 min for a cold-start asset class)
- **Total saved**: ~5 iters of speculative work + ~30 min cycle-time on the actually-shipped iter

The preflight stack is now load-bearing infrastructure for both **what to build** AND **what NOT to build**.

## What's NOT done in iter-332 (deferred)

- **UI consumer for ability icons**: deferred to iter-334+ (UnitControl tab "abilities" GroupBox would need engine-side ability enumeration via `(unit):Get_Active_Abilities()` — Lua API may not exist; needs RE)
- **Asset Browser ability category extension**: iter-321 AssetBrowserTabViewModel has 4 categories (units + portraits + factions + planets); extending to 6 (weapons + abilities) is a 2-line VM change but deferred to iter-333 to keep iter-332 surgical-scope
- **Combat tab weapon-icon column** (iter-331 deferred): mirrors iter-308 unit-icon column pattern; ~80 LoC VM + ~50 LoC XAML; deferred to iter-334+
- **Live SWFOC verify** against operator's real MasterTextures.meg: requires running game; defer to operator session

## Verification checklist

- [x] `ResolveAbilityIcon` ships with `i_button_ability_` prefix + default size 32 + ThumbnailCache lookup
- [x] `LocateAbilityIconDds` ships with same prefix
- [x] Class XML doc updated to document 6 asset classes
- [x] 11/11 pin tests pass (iter-332)
- [x] 95/95 combined thumbnail-cache + asset-resolver suite pass (iter 307→332)
- [x] Editor build inherits GREEN
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [x] iter-313 LocateByConvention plugin set now at 6 plugins — **codification trigger reached**
- [ ] UI consumer (UnitControl tab abilities GroupBox) — deferred to iter-334+
- [ ] Asset Browser ability category extension — deferred to iter-333
- [ ] Live SWFOC verify — deferred to operator session
