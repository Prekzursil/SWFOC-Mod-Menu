# Iter 414 — 7-candidate batch extraction: 4 successes (55 names) + 3 metadata-only break-outs; clause #6 refined

**Date:** 2026-05-07
**Arc class:** Forward-applicability validation #3 of iter-407 codified rule + clause #6 refinement
**Predecessor:** iter-413 (headline-doc quad refresh)
**Successor (queued):** iter-415 (TBD per "Next iter" below)

## What this iter does

Per task #664 recommendation, batch-extracts 7 more EnumConversionClass candidates from the iter-409 discovery list. Pushes cumulative engine-canonical strings count past 330 + tightens iter-407 rule's clause #6 diagnostic test.

## 7-candidate batch results

| Target | Address | Size | Names | Result |
|---|---|---|---|---|
| **AIGoalApplicationType** | 0x1405E4F20 | 2403 | **10** | SUCCESS — Enemy/Friendly Build_Pad/Cash_Point/Reinforcements/Structure/Unit + Tactical_Location |
| FormationGroupingType | 0x1405DE0F0 | 782 | **0** | break-out #6 (metadata-only) — size <800 ✓ |
| **LightEffectType** | 0x1405E4A80 | 1181 | **1** | borderline — only "Continuous_Smooth"; SUCCESS but tiny |
| **LocomotorStateType** | 0x1405E2E30 | **5283** | **34** | LARGEST batch yet — Bike/Fighter/Fly/Hover/Land/Move state machine |
| MapEnvironmentType | 0x1405E8770 | **1409** | **0** | break-out #6 (metadata-only) — **size 1409 contradicts <800 threshold** |
| **GUIGadgetType** | 0x1401DB6F0 | 2508 | **10** | SUCCESS — ComboBox / EditBox / HScrollBar / Image / ImeEditBox / ListBox / OverlayCaption / ProgressBar / VScrollBar / VSlider |
| ContainerArrangementType | 0x1405DD790 | 773 | **0** | break-out #6 (metadata-only) — size <800 ✓ |

**Net outcome**: 4 successful extractions (55 NEW names: 10+1+34+10) + 3 metadata-only break-outs.

## Clause #6 refinement (CRITICAL FINDING)

**Original codified diagnostic test (iter-410)**:
> Function size <800 bytes + zero `aXxx` refs = metadata-only EnumConversionClass

**Empirical contradiction (iter-414)**:
- `EnumConversionClass<MapEnvironmentType>` @ 0x1405E8770: **size 1409 bytes**, **zero refs** = metadata-only
- This violates the 800-byte threshold

**Refined diagnostic test (iter-414 codified update)**:
> **zero `aXxx` refs in asm** = metadata-only EnumConversionClass, REGARDLESS of function size. The reliable signal is the regex match count, not function size.

The iter-410 size threshold was based on only 2 metadata-only instances (DifficultyLevelType=782 + ForceAlignmentType=794). iter-414's MapEnvironmentType (1409 bytes / zero refs) and FormationGroupingType (782) and ContainerArrangementType (773) prove the size signal is unreliable. The match-count signal is the durable test.

This is a **rule maturation finding** — clause #6's diagnostic test refined post-codification based on empirical evidence. iter-373 codified rule (`feedback_codified_rule_self_validates_via_forward_application.md`) explicitly anticipates this maturation; the iter-407 rule is now more precise post-iter-414 than at iter-410 codification.

## LocomotorStateType: largest single extraction (34 names from one function)

`EnumConversionClass<LocomotorStateType>` @ 0x1405E2E30 returned 34 unique names from a 5283-byte function — largest single-instance extraction since iter-405 ModelAnimType (111 names from 9313 bytes; ratio of 34/5283 = 0.64% bytes-per-name vs ModelAnimType's 0.84%).

Sample of extracted names (full list in extract_enum_conversion_strings.py output):
- Bike states: Bike_End_Move, Bike_Moving, Bike_PreStopped, Bike_Start_Move, Bike_Stopped, Bike_Turning_In_Place
- Fighter states: Fighter_Dead_Stopped, Fighter_Directed, Fighter_Idle, Fighter_Idle_Combat, Fighter_Moving
- Hyperspace: Fly_Hyperspace_Approach, Fly_Start_Hyperspace, Fly_Stopped
- Hover: Hover_Moving, Hover_Stopped, Hover_Stopping
- Land: Land_Fly_Circling, Land_Fly_NoPath, Move_Follow_Leader, PreStopped
- Generic: Deployed, Deploying

**Operator value**: pairs operationally with iter-178 Get_Game_Mode + tactical state queries. Future arc could ship `SWFOC_GetUnitLocomotorState` wire that returns the canonical name string; LocomotorStateType embed enables operator-friendly state filtering.

## Cumulative state across 9 successful EnumConversionClass extractions

| Iter | Target | Names |
|---|---|---|
| 402-404 | UnitAbilityType | 69 |
| 405 | ModelAnimType | 111 |
| 406 | GUIGadgetComponentType | 83 |
| 409 | HardPointType | 5 |
| 410 | CorruptionTypeEnum | 4 |
| 410 | AbilityActivationType | 6 |
| **414** | **AIGoalApplicationType** | **10** |
| **414** | **LightEffectType** | **1** |
| **414** | **LocomotorStateType** | **34** |
| **414** | **GUIGadgetType** | **10** |

**Cumulative: 333 engine-canonical strings extracted** across **10 successful EnumConversionClass instances** (iter-414 added 4 to bring count from 5 → 9 successful + 4 = 9? Let me recount: iter-402+iter-405+iter-406+iter-409+iter-410-Corruption+iter-410-Ability+iter-414×4 = 10 total).

## Forward-applicability validation tally

| Validation # | Iter | Outcome |
|---|---|---|
| #1 | 409 | HardPointType — clause #3 small-enum break-out |
| #2 | 410 | 5-candidate batch — 2 successes + clauses #6+#7 NEW |
| #3 | 411 | DynamicEnumConversionClass — clause #8 negative-applicability |
| **#4** | **414 (THIS)** | **7-candidate batch — 4 successes + clause #6 refined** |

iter-407 rule has now had **4 forward-applicability validations** post-codification. Rule has accumulated **8 honest-break-out clauses** (5 original + 3 NEW iter-410/411 + 1 refined iter-414).

## What shipped

1. **`tools/iter414_ledger_add.py`** (NEW) — adds 4 ledger entries
2. **`verified_facts.json`**: 324 → 328 entries; 315 VERIFIED; lint 0/0
3. **`feedback_static_data_re_extraction.md`** — clause #6 diagnostic test refined (size threshold removed; match-count is the durable signal)
4. **iter414 close-out doc** (this file)

## Verification gates ALL GREEN

- ✅ Verifier lint 0/0 at 328 entries (315 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ All editor build/test gates inherit GREEN from iter-401-413 chain
- ✅ Bridge harness 1100/0
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-414 ships 0 source/test/XAML)
- ✅ iter-407 codified rule strengthened with empirically-validated clause #6 refinement

## Net iter-414 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML (pure RE extraction + ledger + rule refinement) |
| New tools | 1 (iter414_ledger_add.py) |
| Catalog entries | 324 → 328 (+4 ledger entries) |
| Doc shipped | 1 close-out doc + 1 rule refinement (clause #6 diagnostic test updated) |
| Pattern observations flagged | clause #6 size-threshold-unreliable empirically validated |
| Names extracted (cumulative across 9 successful EnumConversionClass instances iter 402+405+406+409+410+410+414+414+414+414 = 10 instances) | 69+111+83+5+4+6+10+1+34+10 = **333 engine-canonical strings** |
| Cycle time | ~20 min (7-candidate batch query + extraction + 4-ledger add + clause refinement + close-out) |

**iter-414 strengthens iter-407 rule by refining clause #6's diagnostic test** based on empirical evidence. Cumulative 333 strings across 10 instances is the most extracted in any single arc post-iter-407 codification.

83rd post-iter-323 arc iter (4th post-codification forward-applicability validation; LARGEST single-iter extraction count for cumulative); 144th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-415)

Options:

1. **Continue EnumConversionClass extractions** — many candidates still unexplored; pattern is now mature (~5 min/extraction). Each new validation strengthens iter-407 rule's evidence base further.

2. **3rd-tier "XML config extraction" codification kickoff** — design doc for the implied 3rd-tier candidate (iter-411 finding).

3. **NEW arc-class kickoff** — RE Play_Animation engine helper for ModelAnimType UX consumer (deferred since iter-405).

4. **Operator changelog supplement10** — covering iter 408-414 (6-iter window).

5. **Live SWFOC verify** of iter-403 ComboBox.

iter-415 likely option 1 (continue compounding extractions; cycle time bottoms out at ~5 min) OR option 4 (close 6-iter doc gap).
