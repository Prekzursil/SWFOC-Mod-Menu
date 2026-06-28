# Ralph Loop Changelog 2026-05-07 — Supplement 9 (iter 408-414)

**Window:** iter 408-414 (7 iters)
**Predecessor:** supplement8 (iter 401-407 callgraph-mining arc + iter-407 codification)
**Successor (queued):** supplement10 (iter 415+; covers next callgraph-mining cycle or new arc-class)

## What this supplement covers

7-iter forward-applicability arc post-iter-407 codification. Documents 4 forward applications of `feedback_static_data_re_extraction.md` (3 positive + 3 negative-applicability + 1 size-threshold-refinement = 7 distinct empirical findings) yielding **3 NEW honest-break-out clauses** (clauses #6/#7/#8) + **1 critical clause #6 diagnostic-test refinement**. Cumulative 333 engine-canonical strings extracted across 10 successful EnumConversionClass instances.

## Phase 1 — Pre-extraction docs supplement (iter 408)

iter-408 CLOSED — operator changelog supplement8 published covering iter 401-407 (15-supplement series milestone; closes 14-iter doc gap since iter-393 supplement7). 3rd opportunistic-advance application of iter-374 codified rule.

## Phase 2 — Forward-applicability validation #1 (iter 409)

iter-409 CLOSED — 1st post-codification application of iter-407 rule. Discovery query against `rtti_refs` table surfaced ~30 EnumConversionClass instances binary-wide (vs the 3 in `untouched_subsystems.md`). Picked HardPointType @ 0x14053F7B0 for forward validation. Extraction returned **5 names** (Dummy_Art / Weapon_Ion_Cannon / Weapon_Mass_Driver / Weapon_Special / Weapon_Torpedo).

**Clause #3 empirically validated**: small enum (5 < 10 entries threshold) → UX consumer DEFERRED per rule discipline. Recipe steps 1-6 executed in <2 min; step 7 (UX) skipped per honest-break-out.

## Phase 3 — Forward-applicability validation #2 + 2 NEW break-out clauses (iter 410)

iter-410 CLOSED — 5-candidate batch extraction targeting HIGH-UX-value enums. Outcome: 2 successful + 3 break-out validations.

| Target | Outcome |
|---|---|
| CorruptionTypeEnum | 4 names SUCCESS |
| AbilityActivationType | 6 names SUCCESS |
| DifficultyLevelType | 0 names — NEW break-out #6 (metadata-only) |
| ForceAlignmentType | 0 names — NEW break-out #6 (metadata-only) |
| GameObjectCategoryType | 5 ERROR strings — NEW break-out #7 (error-strings-only) |

Rule extended with **clauses #6 + #7**. Architectural finding: SWFOC's iter-322 Combat presets hardcode "Easy/Normal/Hard/Hardcore" specifically BECAUSE DifficultyLevelType is metadata-only — the engine doesn't expose those strings via the conversion class.

## Phase 4 — NEGATIVE-result validation (iter 411)

iter-411 CLOSED — first NEGATIVE forward-applicability validation. Tested if iter-407 rule extends from `EnumConversionClass<T>` → `DynamicEnumConversionClass<T>`. **Result: NO**. All 5 DynamicEnumConversionClass instances are template instantiations of an XML-loader function (identical 1998-byte size, identical 4 error-handler strings).

**Clause #8 added**: DynamicEnumConversionClass<T> does NOT generalize to this recipe; strings live in `data/xml/<typename>.xml` not the binary. Different RE methodology required.

**Architectural insight**: SWFOC has TWO conversion-class families:
- `EnumConversionClass<T>` = STATIC (engine-internal types; recipe applies)
- `DynamicEnumConversionClass<T>` = DYNAMIC (modder-customizable XML-loaded types; recipe does NOT apply)

**Implied 3rd-tier codification candidate**: "Engine has XML CONFIG DATA → walk `data/xml/`" (iter-300 SWFOC_ListMods + iter-294 mod-CRC32 = 1st implicit instance).

## Phase 5 — Cheap-insurance republish (iter 412)

iter-412 CLOSED — dotnet publish executed cleanly but binary timestamp UNCHANGED at iter-404's May 7 12:58:37 (correct incremental-build behavior; no source changes since iter-404).

**Pattern lesson refined**: cheap-insurance republish = build-pipeline-health check, NOT necessarily binary-refresh. Future iters should explicitly distinguish "binary unchanged (no source changes)" from "binary refreshed (source changes detected)".

## Phase 6 — Headline-doc quad refresh (iter 413)

iter-413 CLOSED — 7th major capstone in iter-222/254/265/322/348/396/413 sequence. Closed 17-iter gap since iter-396 by:
- HISTORY.md: NEW "iter 401-412 callgraph-mining arc" session inserted above iter-396-400
- STATUS.md: single-Edit prepend on iter-400 milestone bullet anchor
- README.md: NEW 7th capstone bullet + Key Numbers header bumped post-iter-395 → post-iter-412
- MEMORY.md: already current at iter-407

Doc-coherence at all 4 surfaces.

## Phase 7 — Forward-applicability validation #4 + clause #6 refinement (iter 414)

iter-414 CLOSED — 7-candidate batch extraction. Outcome: 4 successful (55 NEW names) + 3 metadata-only break-outs.

| Target | Outcome |
|---|---|
| **AIGoalApplicationType** | 10 names — Enemy/Friendly Build_Pad/Cash_Point/Reinforcements/Structure/Unit + Tactical_Location |
| FormationGroupingType | 0 names — break-out #6 (metadata-only) |
| **LightEffectType** | 1 name — borderline (Continuous_Smooth) |
| **LocomotorStateType** | **34 names — LARGEST single extraction in series** — Bike/Fighter/Fly/Hover/Land state machine |
| MapEnvironmentType | 0 names — break-out #6 (size 1409 bytes contradicts <800 threshold) |
| **GUIGadgetType** | 10 names — ComboBox/EditBox/HScrollBar/Image/ImeEditBox/ListBox/OverlayCaption/ProgressBar/VScrollBar/VSlider |
| ContainerArrangementType | 0 names — break-out #6 (metadata-only) |

**CRITICAL clause #6 refinement**: original "size <800 bytes" diagnostic test contradicted by MapEnvironmentType (1409 bytes / zero refs). Refined to "zero `aXxx` refs in asm = metadata-only REGARDLESS of function size".

## Cumulative state at end-of-arc (post-iter-414)

| Metric | iter-407 | iter-414 | Delta |
|---|---|---|---|
| Codified rules | 20 | 20 | 0 (rule REFINED 3× post-codification, not new rule) |
| iter-407 honest-break-out clauses | 5 (codification) | **8** | +3 (iter-410 #6/#7 + iter-411 #8) |
| iter-407 rule maturity (forward applications) | 0 | **4** | +4 (iter-409/410/411/414) |
| Engine-canonical strings extracted | 263 (iter 402-406) | **333** | +70 (iter-410 +10 + iter-414 +55 + iter-409 +5) |
| Successful EnumConversionClass instances | 3 (iter 402-406) | **10** | +7 (iter-409/410×2/iter-414×4) |
| Ledger entries | 321 | **328** | +7 (iter-409/410×2/iter-414×4) |
| Doc-coherence quad | 75% | **100%** | +25% (iter-413 closure) |
| Operator changelog supplements | 8 (supplement8) | 9 (supplement9 PUBLISHED iter-415) | (this supplement) |

## NEW patterns observed iter 408-414

1. **Codified rules MATURE post-codification via forward-applicability validation** (per iter-373 codified rule): iter-407 rule absorbed 3 NEW break-out clauses + 1 diagnostic-test refinement across 4 forward applications. The rule is more precise post-iter-414 than at iter-407 codification.

2. **Architectural insights emerge from NEGATIVE-result validations** (iter-411): iter-407 rule's DynamicEnumConversionClass non-applicability finding revealed SWFOC's 2-family conversion-class design (static vs dynamic). NEW codification candidate for 3rd tier.

3. **Empirical evidence trumps codified diagnostic tests** (iter-414 clause #6 refinement): the original "size <800 bytes" threshold was based on 2 instances; iter-414's MapEnvironmentType (1409 bytes / zero refs) proved the threshold was unreliable. Match-count-based diagnostic test is durable.

4. **Cheap-insurance republish ≠ new-binary** (iter-412): incremental dotnet publish correctly skips re-emit when no source changes; pattern is build-pipeline-health check.

5. **Headline-doc quad coherence as INVARIANT**: every ~30 iters, all 4 doc surfaces snap to same reference state. iter-413 closed 17-iter gap pre-emptively before drift widened past canonical threshold.

## Verification gates GREEN throughout (post-iter-414)

- ✅ Bridge harness 1100/0 (continuous since iter-225 = 189 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 328 entries (315 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ Editor build 0 errors / 0 warnings (iter-356 zero-warnings standard sustained)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-412 verified pipeline)
- ✅ Filtered tests 21/21 PASS (iter-403 confirmed; sustained)
- ✅ Callgraph SQLite index FULLY OPERATIONAL (iter-401 confirmed; used iter-409/411/414 for binary-wide discovery)

## Source attribution

- 7 close-out docs (iter408_through_iter414) in `knowledge-base/`
- ralph_loop_state.md iteration log (iter 408-414 detailed entries)
- 4 NEW tools in `tools/` (iter409 rtti-find + target-find + iter410 inspect-asm + iter414 ledger-add; ledger-add scripts also at iter-404/405/406/409/410)
- iter-407 codified rule extended 3× post-codification (clauses #6/#7/#8 added; clause #6 diagnostic test refined)
- 10 NEW ledger entries (iter-404/405/406/409/410×2/414×4)
- iter-413 headline-doc quad refresh (4 surfaces coherent)

## Next supplement

Supplement 10 will cover iter 415+ including continued extractions or new arc-class kickoff.
