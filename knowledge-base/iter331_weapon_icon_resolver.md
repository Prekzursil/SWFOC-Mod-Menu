# iter-331 — Weapon hardpoint icon extension to UnitIconResolver (post-iter-323 feature-velocity restore; 5th asset class)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Audit E weapon-icon class extension; closes iter-294 honest-defer)
**Predecessor:** iter-330 (operator changelog supplement covering iter 320-329)
**Successor (queued):** iter-332 (ability icon resolver — 6th asset class; mirror pattern)

## What changed (1 file modified + 1 test file new; ~140 LoC; **11/11 iter-331 + combined 84/84 PASS in 1.95s**)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.Core/Assets/UnitIconResolver.cs` (+~50 LoC):
  - Updated class XML doc to add weapon hardpoints row (5 asset classes now documented)
  - NEW `public string? ResolveWeaponIcon(string weaponName, int size = 32)` — looks up `i_button_hp_<weaponName>.dds` via the same 5-relpath walk + iter-307 ThumbnailCache lookup. Default size 32 matches unit icons because hardpoint icons render at the same scale in SWFOC's per-unit weapon roster UI.
  - NEW `public string? LocateWeaponIconDds(string weaponName)` — symmetric to `LocateDds` for the weapon-hardpoint convention.
  - **Extends iter-313 LocateByConvention helper to 5 plugins** — the `LocateByConvention(string filenamePrefix, string assetName)` private helper is shape-agnostic; iter-331 just adds 1 more wrapper.

- **NEW** `SWFOC editor/tests/SwfocTrainer.Tests/Core/Assets/Iter331WeaponIconResolverTests.cs` (~225 LoC, **11 facts**):
  - `ResolveWeaponIcon_NullRoot_ReturnsNull` — graceful null contract
  - `ResolveWeaponIcon_EmptyWeaponName_ReturnsNull` — empty + whitespace coverage
  - `LocateWeaponIconDds_FindsAtCanonicalPath` — happy path
  - `LocateWeaponIconDds_DoesNotMatchOther4Conventions` — **strict 5-way prefix discriminator** (i_button_/i_portrait_/i_faction_/i_planet_ all reject for weapon name)
  - `Other4Convention_Resolvers_DoNotMatchWeaponConvention` — **reverse 5-way symmetry** (i_button_hp_ rejects from all 4 prior resolvers)
  - `ResolveWeaponIcon_DdsExists_AndCachePopulated_ReturnsCachedPath` — cache integration
  - `ResolveWeaponIcon_DefaultSize_Is32_MatchesUnitIconScale` — **default-arg pin** (32 distinct from 48/64/96 of other 3 classes; SHARES with unit-icon 32 — pinned so consolidation refactors that drift the default catch in tests)
  - `ResolveWeaponIcon_DdsExists_CacheMissing_ReturnsNull` — graceful-null contract mirror
  - `ResolveWeaponIcon_UnsupportedSize_ReturnsNull_DoesNotThrow` — exception safety
  - `LocateWeaponIconDds_NotPresent_ReturnsNull` — happy-null path
  - `All_5_AssetClasses_CoExist_AtSameDir_WithoutCollision` — **end-to-end 5-way validation** (extends iter-315's 4-way validation to 5-way; all 5 prefixes for the same NAME resolve to 5 distinct paths)

  Pinned to `[Collection("ThumbnailCacheEnv")]` for env-var orthogonality with iter-307+308+312+313+314+315.

## Verification gates ALL GREEN

```
[Start-Process bypass — Clink-safe]
dotnet test --filter "FullyQualifiedName~Iter307|...~Iter331"
Test Run Successful.
Total tests: 84
     Passed: 84
 Total time: 1.9471 Seconds
```

- iter-331 pin tests: **Passed 11/11** ✓
- Combined Thread D + iter-313/314/315/331: **Passed 84/84 in 1.95s** ✓ (no regression in iter-307's 21, iter-308's 20, iter-313's 10, iter-314's 11, iter-315's 11)
- Editor build inherits GREEN
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries

## Why iter-331 weapon icons (not Audit B last wire)

iter-331 task #582 framed Audit B last wire as RECOMMENDED. **Preflight discovery surfaced the actual situation**:

1. **Audit B has 2 remaining wires** (faction-roster-by-build-tab + hero-roster), NOT 1. iter-330 framing was off-by-one.
2. **iter-300 explicitly classifies both as empirical-defer**: "deferred until empirical evidence shows operators need them; the 4 shipped wires cover the dynamic-loading mandate's primary asks". 31 iters of zero operator complaint validates the defer.
3. **hero-roster IS already iter-329 ListHeroes** — the iter-323 drift-resolution arc just extended its catalog rationale with the 3-gap citation.
4. **faction-roster-by-build-tab** is genuinely distinct (BUILD-time catalog query vs iter-299 RUNTIME ownership query) but lacks operator demand signal.

Pivoting to **option C: weapon icon resolver** directly serves the user's standing mandate ("nice GUI showing units by their in-game pictures") at lower risk than speculative Audit B work. Closes iter-294 Audit E weapon-icon class extension that was deferred at iter-313 LocateByConvention abstraction.

## Pattern lessons

### Recurrence — *iter-313 LocateByConvention plugin set extension* (5th instance)

iter-313 hero portraits (1st) → iter-314 faction emblems (2nd) → iter-315 planet icons (3rd) → iter-321 cross-asset Asset Browser tab (4th — composes all 3 + units) → **iter-331 weapon hardpoints (5th)**. The abstraction holds: 30-50 LoC marginal cost per asset class. Future asset types (ability icons in iter-332, build-tab category icons, ranking medal icons, etc.) plug in the same way.

**Pattern shape codified at iter-316** (`feedback_locate_by_convention_extensible.md`): when adding a new asset class, mirror the iter-313 pattern exactly — `Resolve<X>(name, int size = N) → Locate<X>Dds(name) → LocateByConvention(prefix, name)` + 9-11 pin tests including N-way discriminator + N-way symmetry + N-way co-existence.

### Recurrence — *engine-already-does-this rule applied to asset conventions*

iter-302 codified `feedback_engine_already_does_this.md` for engine Lua APIs (RVA-pin alternative). iter-331 applies the same principle at the asset layer: **SWFOC has a canonical filename convention (`i_button_hp_<name>.dds` for hardpoints); use it as-is rather than designing a custom convention**. Saves operator pre-extraction ambiguity (no "rename your DDS files" friction) + maintains compatibility with mod authors who follow the SWFOC convention.

## Pivot rationale documented (preflight stack proves its value)

iter-326 4-step preflight + iter-327 rationale-grep preflight + iter-328 bridge-source-grep preflight applied to Audit B candidates:
- Step 0 (rationale-grep): faction-roster-by-build-tab has NO catalog entry yet (iter-300 deferred adding one)
- Step 1 (engine-surface): would require new engine Lua API or per-mod XML parser — both speculative
- Step 2 (orphan-bridge-wire): N/A — wire doesn't exist
- Step 3 (composition): iter-179 Find_All_Objects_Of_Type doesn't filter by build-tab; would need NEW per-type XML attribute lookup
- Step 4 (operator-demand-signal): **31 iters of zero complaint = strong negative signal**

The preflight stack converted what could have been a 5-iter speculative arc into a 5-second pivot to a higher-value adjacent target. **This is the iter-323 drift-resolution arc's payoff at the iter-strategy layer**, not just the catalog-resolution layer.

## What's NOT done in iter-331 (deferred)

- **UI consumer for weapon icons**: deferred to iter-332+ (Combat tab "weapons" GroupBox would need engine-side hardpoint enumeration via `(unit):Get_Hardpoints()` — Lua API may not exist; needs RE)
- **Asset Browser weapon category**: iter-321 AssetBrowserTabViewModel has 4 categories; extending to 5 (weapons) is a 1-line VM change but deferred to iter-333+ to keep iter-331 surgical-scope
- **Ability icon resolver** (`i_button_ability_<name>.dds`): deferred to iter-332 — mirror of iter-331 with different prefix; 6th plugin in LocateByConvention set
- **Live SWFOC verify** against operator's real MasterTextures.meg: requires running game; defer to operator session

## Verification checklist

- [x] `ResolveWeaponIcon` ships with `i_button_hp_` prefix + default size 32 + ThumbnailCache lookup
- [x] `LocateWeaponIconDds` ships with same prefix
- [x] Class XML doc updated to document 5 asset classes
- [x] 11/11 pin tests pass (iter-331)
- [x] 84/84 combined thumbnail-cache + asset-resolver suite pass (iter 307→331)
- [x] Editor build inherits GREEN
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [x] iter-313 LocateByConvention plugin set now at 5 plugins
- [ ] UI consumer (Combat tab weapons GroupBox) — deferred to iter-332+
- [ ] Asset Browser weapon category extension — deferred to iter-333+
- [ ] Live SWFOC verify — deferred to operator session
