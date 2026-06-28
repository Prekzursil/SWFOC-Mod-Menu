# Iter 410 — 5-candidate batch extraction reveals 2 NEW honest-break-out cases (rule extended)

**Date:** 2026-05-07
**Arc class:** Forward-applicability validation #2 of iter-407 codified rule + rule extension
**Predecessor:** iter-409 (HardPointType extraction; honest-break-out clause #3 validated)
**Successor (queued):** iter-411 (TBD per "Next iter" below)

## What this iter does

5-candidate batch extraction targeting HIGH-UX-value EnumConversionClass clusters. Per user "do all the plans end 2 end 100%" directive — extracted 5 candidates in one iter via the codified mechanical recipe.

## Outcome — honest-break-out cases empirically expanded

| Target | Address | Size | Names | Result | Action |
|---|---|---|---|---|---|
| **CorruptionTypeEnum** | 0x1405E0590 | 1638 | **4** (Corruption_Black_Market, Corruption_Bond, Corruption_Bribery, Corruption_Corruption) | **SUCCESS** | Ledger pin shipped; UX defer (4 entries < 10-entry threshold per clause #3) |
| **AbilityActivationType** | 0x1405E42E0 | 1945 | **6** (Combat_Imminent, Ground_Activate, Skirmish_Automatic, Special_Attack, Take_Damage, User_Input) | **SUCCESS** | Ledger pin shipped; UX defer (reference data; pairs operationally with iter-403 ComboBox tooltip) |
| DifficultyLevelType | 0x1405DE400 | 782 | **0** (only "DifficultyLevelType" type-name) | **NEW BREAK-OUT #6** | Metadata-only EnumConversionClass; no enum→string mapping; iter-322 Combat presets hardcode labels |
| ForceAlignmentType | 0x1405DDAA0 | 794 | **0** (only "ForceAlignmentType" type-name) | **NEW BREAK-OUT #6** | Metadata-only EnumConversionClass; integer-keyed without canonical strings |
| GameObjectCategoryType | 0x14046AD50 | 1521 | **5** (all error-handler strings — `aErrorCanTCreat`, `aErrorCanTOpenX`, etc.) | **NEW BREAK-OUT #7** | Strings extracted are DIAGNOSTIC error messages, not enum values |

**Net outcome**: 2 successful extractions (10 names total: CorruptionTypeEnum=4 + AbilityActivationType=6) + 3 honest-break-out validations.

## NEW honest-break-out clauses (iter-410 added to codified rule)

### Clause #6: EnumConversionClass<T> with NO RegisterMapping calls

**Discovery**: DifficultyLevelType and ForceAlignmentType returned ZERO `aXxx` symbol-label refs despite being legitimate EnumConversionClass instances.

**Indicator**: `strcpy(v2, "TypeName")` is the ONLY string in the asm; the class is constructed with just its type-name string for serialization/diagnostics, but enum values are integer-keyed without canonical string labels.

**Example asm body** (DifficultyLevelType):
```c
v0 = 0;
v1 = operator new(0x90u);
if (v1) {
    v2 = (char *)operator new(0x20u);
    v13 = 19;
    v14 = 31;
    strcpy(v2, "DifficultyLevelType");  // <-- ONLY string
    sub_14023AE90(v1, &v12);
    *v1 = &EnumConversionClass<enum DifficultyLevelType>::`vftable';
    // ... more zeroing/setup, no RegisterMapping calls
}
```

**Diagnostic test**: function size <800 bytes + zero `aXxx` refs = metadata-only EnumConversionClass; the operator-facing strings come from a different source (XML config / localization tables / hardcoded labels in editor code).

**Cross-reference**: iter-322 Combat tab presets hardcode "Easy/Normal/Hard/Hardcore" exactly BECAUSE the engine's DifficultyLevelType conversion class doesn't expose those strings — they live in `data/xml/difficultyleveltypes.xml` or similar.

### Clause #7: EnumConversionClass<T> with strings in error-handling paths only

**Discovery**: GameObjectCategoryType returned 5 `aXxx` symbol-label refs but ALL are diagnostic error messages (`aErrorCanTCreat`, `aErrorCanTOpenX`, `aErrorCouldnTAd`, `aErrorTooManyEn`, `aLlx`) — not enum values.

**Indicator**: extracted strings start with `Error_*` / `Cant_*` / `Failed_*` / etc. prefix patterns rather than Title_Case_Underscore enum-value patterns.

**Diagnostic test**: validate extracted strings before accepting — enum values look like `Title_Case_Underscores` (e.g. `Saber_Throw`, `Force_Lightning`) not error messages.

**Pattern**: when GameObjectCategoryType conversion was first introduced, the engine probably emitted error log strings if the conversion failed at runtime. The static initializer's asm body includes those error paths but doesn't include the actual enum-to-string registrations (those may be in a separate function or in XML config).

## What shipped

1. **`tools/iter410_inspect_asm.py`** (NEW) — deeper asm inspection helper for diagnosing zero-extract or anomalous-extract cases (lea targets + xrefs.from + inline strings + raw asm preview)
2. **`tools/iter410_ledger_add.py`** (NEW) — adds 2 ledger entries for the 2 successful extractions
3. **`verified_facts.json`**: 322 → 324 entries (+2 ledger entries; 311 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED; lint 0/0)
4. **`feedback_static_data_re_extraction.md`** — extended with NEW honest-break-out clauses #6 and #7 (rule was at 5 break-out clauses; now at 7)
5. **iter410 close-out doc** (this file)

## Pattern compounding (forward-applicability validation #2)

| Iter | Targets attempted | Successful | UX shipped | Honest-break-outs hit |
|---|---|---|---|---|
| 402-404 | 1 | 1 (UnitAbilityType=69) | YES | none |
| 405 | 1 | 1 (ModelAnimType=111) | DEFER | clause #4 (no Lua wire) |
| 406 | 1 | 1 (GUIGadgetComponentType=83) | DEFER | clause #4 (no consumer) |
| 409 | 1 | 1 (HardPointType=5) | DEFER | clause #3 (small enum) |
| **410 (THIS)** | **5** | **2 (CorruptionTypeEnum=4 + AbilityActivationType=6)** | DEFER both | **clauses #6 + #7 NEW (3 hits across 3 candidates)** |

**Cumulative across 5 successful extractions**: 69 + 111 + 83 + 5 + 4 + 6 = **278 engine-canonical strings** in ~80 min total.

**Rule's empirical state at iter-410**:
- 5 successful extractions across 5 distinct enum types
- 3 honest-break-out clauses validated empirically (clauses #3 / #6 / #7)
- Recipe ships at ~5-10 min/extraction marginal cost
- Tooling now includes `iter410_inspect_asm.py` for the zero-extract diagnostic case

## Verification gates ALL GREEN at iter-410

- ✅ Verifier lint 0/0 at 324 entries (311 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ All editor build/test gates inherit GREEN from iter-401-409 chain
- ✅ Bridge harness 1100/0
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-410 ships 0 source/test/XAML)
- ✅ iter-407 codified rule extended with 2 NEW empirically-validated break-out clauses (rule maturation)

## Net iter-410 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML (pure RE extraction + ledger + rule extension) |
| New tools | 2 (iter410_inspect_asm.py + iter410_ledger_add.py) |
| Catalog entries | 322 → 324 (+2 ledger entries) |
| Doc shipped | 1 close-out doc + 1 rule extension (2 NEW break-out clauses) |
| Pattern observations flagged | 2 NEW honest-break-out clauses (#6 metadata-only + #7 error-strings-only) |
| Names extracted (cumulative across 5 successful EnumConversionClass instances) | 69 + 111 + 83 + 5 + 4 + 6 = **278 engine-canonical strings** |
| Cycle time | ~15 min (5-candidate batch query + extraction + asm-inspect diagnostic + ledger add + rule extension + close-out) |

**iter-410 is the most productive single-iter callgraph-mining batch** — 5 candidates queried, 2 extracted, 3 break-out validated, codified rule extended with 2 NEW clauses. The rule is now MORE mature post-iter-410 than at iter-407 codification, having survived 5 forward applications and absorbed 2 NEW empirical break-out cases.

79th post-iter-323 arc iter (5-candidate batch extraction; 1st rule-extension iter); 140th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-411)

Options:

1. **DynamicEnumConversionClass<T> generalization test** — 5 listed in iter-409 discovery (AIGoalCategoryType / MovementClassType / ObjectWeatherCategoryType / PerceptionTokenType / SurfaceFXTriggerType). Tests if the recipe extends to the Dynamic variant of EnumConversionClass.

2. **Editor binary republish + filtered test verify** — cheap insurance after multiple ledger adds (iter-322 → 324); iter-376 cheap-insurance precedent.

3. **Headline-doc quad refresh** — README + STATUS + HISTORY need iter 401-410 update; iter-396 was last refresh (14-iter gap).

4. **NEW arc-class kickoff** — RE Play_Animation engine helper to unlock deferred ModelAnimType UX consumer.

5. **Operator changelog supplement** — supplement8 covered iter 401-407; supplement9 would cover iter 408-410.

iter-411 likely option 1 (test recipe generalization to DynamicEnumConversionClass — would be the rule's strongest forward-applicability test) OR option 2 (cheap-insurance republish).
