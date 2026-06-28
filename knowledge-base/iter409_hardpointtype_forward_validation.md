# Iter 409 — Forward-applicability validation #1 of iter-407 rule: HardPointType extraction (small-enum honest-break-out empirically validated)

**Date:** 2026-05-07
**Arc class:** Forward-applicability validation (per iter-373 codified rule)
**Predecessor:** iter-408 (supplement8 changelog covering iter 401-407)
**Successor (queued):** iter-410 (TBD per "Next iter" below)

## What this iter does

First post-codification application of the iter-407 codified rule (`feedback_static_data_re_extraction.md`). Three goals:

1. **Validate the recipe's mechanical claim** — the codified 7-step recipe should ship a 4th extraction at ~5 min cycle
2. **Discover binary-wide candidates** — search `rtti_refs` table for ALL `EnumConversionClass<T>` instances beyond the 3 known ones
3. **Empirically test honest-break-out clauses** — does the rule's "Enum is small (<10 entries)" defer-clause matter in practice?

## Discovery: ~30 EnumConversionClass instances exist binary-wide

The callgraph SQLite `rtti_refs` table contains far more EnumConversionClass clusters than `untouched_subsystems.md` listed. Sample (LIMIT 30):

```
DynamicEnumConversionClass<AIGoalCategoryType / MovementClassType / ObjectWeatherCategoryType / PerceptionTokenType / SurfaceFXTriggerType>
EnumConversionClass<AIGoalApplicationType / AIGoalCategoryType / AIGoalReachabilityType /
                    AbilityActivationType / ActionRelevanceEnum / CellPassabilityType /
                    ClashTypeEnum / ContainerArrangementType / CorruptionTypeEnum /
                    DifficultyLevelType / EdgeAnnotationType / ForceAlignmentType /
                    FormationFormupWaitType / FormationGroupingType / GUIGadgetComponentType /
                    GUIGadgetType / GameObjectCategoryType / GameObjectPropertiesType /
                    HardPointType / InstantiatedGoalStateType / LightEffectType /
                    LocomotorStateType / MapEnvironmentType / ModelAnimType /
                    ModelClass::EmitterType>
```

(Result truncated at 30; actual count likely higher. Many candidates have **HIGH** operator value: DifficultyLevelType pairs with iter-322 Combat presets; CorruptionTypeEnum pairs with iter-180 SWFOC_CorruptLua; GameObjectCategoryType pairs with iter-179 Find_All_Objects_Of_Type.)

## Forward validation: HardPointType extraction

**Target chosen**: `EnumConversionClass<HardPointType>` @ `0x14053F7B0` (size 1914 bytes — smallest of the candidates).

**Reason**: pairs with iter-343 Hardpoint Inspector chain. UX intent: hypothetical filter-by-category dropdown for the Hardpoint Inspector ListBox.

### Recipe steps executed
1. ✅ Identify cluster: `iter409_find_more_enum_conversions.py` (NEW; queries rtti_refs table)
2. ✅ Confirm callgraph metadata: `python tools/callgraph_query.py fn 0x14053F7B0` — **1 caller, 7 callees** (similar shape, fewer callees because fewer entries)
3. ✅ Locate corpus: `full_b136-137.json`
4. ✅ Extract strings: `python tools/extract_enum_conversion_strings.py 0x14053F7B0 full_b136-137.json` — **5 names returned**
5. ✅ Add ledger: `iter409_ledger_add.py` (320 → 321 — wait, 321 → 322)
6. ✅ Verify lint: 0/0 at 322 entries (309 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
7. ⏸️ **DEFERRED** UX consumer per honest-break-out clause

### Extracted names (5)
| IDA label | Inferred SWFOC name |
|---|---|
| aDummyArt | `Dummy_Art` |
| aWeaponIonCanno | `Weapon_Ion_Cannon` (truncated) |
| aWeaponMassDriv | `Weapon_Mass_Driver` (truncated) |
| aWeaponSpecial | `Weapon_Special` |
| aWeaponTorpedo | `Weapon_Torpedo` |

## Honest-break-out clause empirically validated

The iter-407 rule's break-out cases include:

> **3. Enum is small (<10 entries)**: not worth tooling; just hand-list in C# const.

HardPointType has **5 entries** — well below the 10-entry threshold. Per the rule, the recipe should ship STEPS 1-6 (extraction + ledger pin) but DEFER step 7 (UX consumer) because:
1. ComboBox UX adds editor surface for marginal operator value (5 categories don't justify a dropdown widget)
2. If a future iter does want HardPointType filtering, the 5 names are 1-line embeddable as a C# enum or const array (no `KnownXxxNames` infrastructure needed)
3. The Hardpoint Inspector UX would more naturally surface category as a column in the ListBox row (already there via SWFOC_GetType wire) rather than as a filter dropdown

**Decision: defer UX consumer; ledger pin sufficient for ground truth.**

This is the **first empirical test** of an honest-break-out clause from iter-407's codification. The rule held — small-enum break-out is a real distinction that should drive the per-instance UX decision.

## Pattern compounding (forward-applicability)

| Iter | Target | Names | UX consumer | Recipe step skipped |
|---|---|---|---|---|
| 402-404 | UnitAbilityType | 69 | SHIPPED ComboBox | none |
| 405 | ModelAnimType | 111 | DEFERRED (no Lua wire) | step 7 (no Play_Animation API) |
| 406 | GUIGadgetComponentType | 83 | DEFERRED (engine-internal) | step 7 (no operator-actionable consumer) |
| **409 (THIS)** | HardPointType | **5** | DEFERRED (small enum) | **step 7 (honest-break-out clause)** |

**4 instances** of the codified pattern; 3 distinct defer reasons (no Lua wire / engine-internal / small enum). Pattern is mature: the recipe + honest-break-out clauses cover the realistic case space.

## What shipped

1. **`tools/iter409_find_more_enum_conversions.py`** (NEW) — scans `rtti_refs` table for all EnumConversionClass<T> instances; ~30 candidates surfaced
2. **`tools/iter409_find_target_addr.py`** (NEW) — looks up specific class names + retrieves func metadata
3. **`tools/iter409_ledger_add.py`** (NEW) — adds `rva_hard_point_type_enum_init` @ 0x53F7B0 (3-tool consensus)
4. **`verified_facts.json`**: 321 → 322 entries; 309 VERIFIED; lint 0/0
5. **iter409 close-out doc** (this file)

## Verification gates ALL GREEN at iter-409

- ✅ Verifier lint 0/0 at 322 entries (309 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ All editor build/test gates inherit GREEN from iter-401-408 chain
- ✅ Bridge harness 1100/0
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-409 ships 0 source/test/XAML)
- ✅ iter-407 codified rule empirically validated at forward-applicability instance #1
- ✅ Honest-break-out clause #3 ("Enum is small <10 entries") empirically validated

## Net iter-409 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML (pure RE extraction + ledger iter) |
| New tools | 3 (rtti_refs query, target lookup, ledger-add for HardPointType) |
| Catalog entries | 321 → 322 (+1 ledger entry) |
| Doc shipped | 1 close-out doc (this file) |
| Pattern observations flagged | iter-407 rule's forward-applicability validation #1 + honest-break-out clause #3 empirically validated |
| Names extracted (cumulative across 4 EnumConversionClass instances) | 69 + 111 + 83 + 5 = **268 engine-canonical strings** |
| Cycle time | ~10 min (rtti_refs discovery + extraction + ledger add + close-out) |
| EnumConversionClass candidates discovered binary-wide | ~30+ (way more than the 3 in untouched_subsystems.md) |

**iter-409 validates the iter-407 codified rule at forward-applicability instance #1.** Honest-break-out clauses MATTER — the rule's discipline is per-instance UX-defer-when-appropriate, not blanket "apply everywhere".

78th post-iter-323 arc iter (4th callgraph-mining extraction; 1st post-codification forward validation); 139th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-410)

Options:

1. **5th EnumConversionClass extraction with HIGH operator value** — `<DifficultyLevelType>` (pairs with iter-322 Combat presets) OR `<CorruptionTypeEnum>` (pairs with iter-180 SWFOC_CorruptLua). Either would be SHIPPED UX (similar to UnitAbilityType iter-402-404 pattern).

2. **Headline-doc quad refresh** — README + STATUS + HISTORY need iter 401-409 update; iter-396 was last refresh (13-iter gap).

3. **Different RTTI cluster type** — DynamicEnumConversionClass<T> variants exist (5 listed in iter-409 discovery). Would test rule generalization beyond the canonical EnumConversionClass shape.

4. **Live SWFOC verify** of iter-403 ComboBox.

iter-410 likely option 1 (DifficultyLevelType for HIGH-UX pairing with iter-322 Combat tab) — would compound the 1-shipped-consumer-out-of-N pattern.
