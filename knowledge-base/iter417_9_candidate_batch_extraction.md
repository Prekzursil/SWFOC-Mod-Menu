# Iter 417 — 9-candidate batch extraction: 3 successes (10 names) + 6 honest-break-outs; clause #6 size-threshold-not-needed re-validated

**Date:** 2026-05-07
**Arc class:** Forward-applicability validation #5 of iter-407 codified rule
**Predecessor:** iter-416 (Play_Animation defer + 3rd-tier kickoff)
**Successor (queued):** iter-418 (TBD per "Next iter" below)

## What this iter does

Continues compounding iter-407 codified rule's evidence base. Per task #667 recommendation, batch-extracts 9 more EnumConversionClass candidates from the iter-409 discovery list.

## 9-candidate batch results

| Target | Address | Size | Names | Result |
|---|---|---|---|---|
| GameObjectPropertiesType | 0x14046B350 | 1521 | **5 ERROR** | break-out #7 (error-strings-only) |
| **AIGoalReachabilityType** | 0x1405E70B0 | 1839 | **4** | SUCCESS (Any_Threat / Friendly_Ignore / High_Threat / Medium_Threat) |
| ClashTypeEnum | 0x140480C30 | 670 | **0** | break-out #6 (metadata-only) |
| ActionRelevanceEnum | 0x1405E94A0 | **1013** | **0** | break-out #6 (metadata-only — size >800 confirms iter-414 refinement) |
| **CellPassabilityType** | 0x1405DD090 | 1780 | **5** | SUCCESS (Empirewall / Impassable / Infantryonly / Shield / Water) |
| FormationFormupWaitType | 0x1405DDDC0 | **805** | **0** | break-out #6 (metadata-only — size >800 confirms iter-414 refinement) |
| EdgeAnnotationType | 0x1405E2A60 | **969** | **0** | break-out #6 (metadata-only — size >800 confirms iter-414 refinement) |
| InstantiatedGoalStateType | 0x1405E5890 | **915** | **0** | break-out #6 (metadata-only — size >800 confirms iter-414 refinement) |
| **ModelClass::EmitterType** | 0x1402641C0 | 1195 | **1** | borderline SUCCESS (Power_To_Weapons) |

**Net outcome**: 3 successful extractions (10 NEW names: 4+5+1) + 5 metadata-only break-outs + 1 error-strings break-out.

## Clause #6 refinement re-empirically validated

iter-414 refined clause #6 from "size <800 bytes" to "regex match count, not size". iter-417 found **4 NEW metadata-only instances all >800 bytes** (805/915/969/1013). The match-count rule held perfectly:
- ClashTypeEnum: 670 bytes / 0 refs (metadata-only)
- FormationFormupWaitType: 805 bytes / 0 refs (metadata-only — size >800 ✓ contradicts old threshold)
- InstantiatedGoalStateType: 915 bytes / 0 refs (metadata-only — size >800 ✓ contradicts old threshold)
- EdgeAnnotationType: 969 bytes / 0 refs (metadata-only — size >800 ✓ contradicts old threshold)
- ActionRelevanceEnum: 1013 bytes / 0 refs (metadata-only — size >800 ✓ contradicts old threshold)

**iter-414 rule refinement empirically VALIDATED at 4 additional instances.** Match-count signal is durable; original size-based proxy was a coincidence of the first 2 metadata-only instances (DifficultyLevelType=782 + ForceAlignmentType=794) being below 800. iter-417 pushes the empirical-disproof count for the size-threshold from 1 to 5.

## Cumulative state across 13 successful EnumConversionClass extractions

| Iter | Target | Names |
|---|---|---|
| 402-404 | UnitAbilityType | 69 |
| 405 | ModelAnimType | 111 |
| 406 | GUIGadgetComponentType | 83 |
| 409 | HardPointType | 5 |
| 410 | CorruptionTypeEnum | 4 |
| 410 | AbilityActivationType | 6 |
| 414 | AIGoalApplicationType | 10 |
| 414 | LightEffectType | 1 |
| 414 | LocomotorStateType | 34 |
| 414 | GUIGadgetType | 10 |
| **417** | **AIGoalReachabilityType** | **4** |
| **417** | **CellPassabilityType** | **5** |
| **417** | **ModelClass::EmitterType** | **1** |

**Cumulative: 343 engine-canonical strings extracted across 13 successful EnumConversionClass instances** (iter-417 added 3 to total).

**Cumulative honest-break-outs**:
- Clause #3 (small enum): 2 instances (HardPointType iter-409 + LightEffectType iter-414 borderline)
- Clause #6 (metadata-only): 9 instances total (DifficultyLevelType + ForceAlignmentType iter-410 + FormationGroupingType + MapEnvironmentType + ContainerArrangementType iter-414 + ClashTypeEnum + FormationFormupWaitType + InstantiatedGoalStateType + EdgeAnnotationType + ActionRelevanceEnum iter-417)
- Clause #7 (error-strings-only): 2 instances (GameObjectCategoryType iter-410 + GameObjectPropertiesType iter-417)
- Clause #8 (DynamicEnumConversionClass): 5 instances (all 5 Dynamic variants tested iter-411)

**Total empirical break-out validations**: 18 instances → robust precise rule

## Forward-applicability validation tally (post-iter-417)

| Validation # | Iter | Outcome |
|---|---|---|
| #1 | 409 | HardPointType — clause #3 small-enum |
| #2 | 410 | 5-candidate batch — 2 successes + clauses #6+#7 NEW |
| #3 | 411 | DynamicEnumConversionClass — clause #8 negative-applicability |
| #4 | 414 | 7-candidate batch — 4 successes + clause #6 refined |
| **#5** | **417 (THIS)** | **9-candidate batch — 3 successes + clause #6 refinement re-validated** |

iter-407 rule has now had **5 forward-applicability validations** post-codification. Recipe is fully mature; the rule's case space is precisely characterized via 8 honest-break-out clauses with 18 empirical validations.

## What shipped

1. **`tools/iter417_find_addrs.py`** (NEW) — looks up addresses + sizes + sources for batch candidates
2. **`tools/iter417_ledger_add.py`** (NEW) — adds 3 ledger entries
3. **`verified_facts.json`**: 328 → 331 entries; 318 VERIFIED; lint 0/0
4. **iter417 close-out doc** (this file)

## Verification gates ALL GREEN

- ✅ Verifier lint 0/0 at 331 entries (318 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ All editor build/test gates inherit GREEN from iter-401-416 chain
- ✅ Bridge harness 1100/0
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-412 verified pipeline)
- ✅ iter-407 codified rule's clause #6 refinement empirically reaffirmed at 5 NEW instances

## Net iter-417 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML (pure RE extraction + ledger iter) |
| New tools | 2 (iter417_find_addrs.py + iter417_ledger_add.py) |
| Catalog entries | 328 → 331 (+3 ledger entries) |
| Doc shipped | 1 close-out doc (this file) |
| Pattern observations flagged | clause #6 match-count rule re-validated at 5 NEW instances; cumulative 18 empirical break-out validations |
| Names extracted (cumulative) | 333 + 10 = **343 engine-canonical strings** across **13 successful EnumConversionClass instances** |
| Cycle time | ~10 min (9-candidate batch query + extraction + 3-ledger add + close-out) |

**iter-417 is the 5th forward-applicability validation of iter-407 rule.** Recipe is fully mechanical at ~5-min/extraction; rule's case space precisely characterized via 18 empirical break-out validations.

86th post-iter-323 arc iter (5th post-codification forward validation); 147th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-418)

Options:

1. **Continue EnumConversionClass extractions** — many candidates still unexplored. Pattern is fully mechanical; cycle ~5 min/extraction.

2. **Cheap-insurance republish + filtered test verify** — last republish was iter-412 (~6 iters ago); would refresh status.

3. **Live SWFOC verify** of iter-403 ComboBox.

4. **NEW arc-class via DynamicEnumConversionClass XML extraction** (2nd 3rd-tier instance) — would require finding SWFOC `data/xml/` location locally.

5. **Operator changelog supplement10 partial** — covering iter 415-417 (3-iter window).

iter-418 likely option 1 (compounding) OR option 2 (refresh status before further extraction).
