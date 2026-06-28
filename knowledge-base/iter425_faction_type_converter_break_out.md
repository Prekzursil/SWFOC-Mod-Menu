# Iter 425 — FactionTypeConverterClass extraction (clause #6 generalization to non-template classes)

**Date:** 2026-05-07
**Arc class:** Static-data extraction attempt + codified-rule generalization (mirrors iter-424 negative-result pattern)
**Predecessor:** iter-424 (DynamicBitfieldConversionClass extraction + clause #7 extension)
**Successor (queued):** iter-426 (codify event-driven defer rule at 3-instance trigger OR operator changelog supplement11 OR cheap-insurance republish)

## What this iter does

Closes the iter-423 Phase 3 NEW-pattern survey by extracting the 2nd of 2 unexplored static-data classes:

1. **Located `FactionTypeConverterClass` populator** via callgraph SQLite: 0x1403301A0 (size=549 bytes, 1 caller, 12 callees)
2. **Ran iter-405 generalized extraction tool** — **0 strings extracted (zero `aXxx` references)**
3. **Extended iter-407 codified rule's clause #6** to cover non-template standalone classes

## Findings

### Zero string references = clause #6 break-out

Extraction output:
```
=== Target: 0x1403301a0 (size=0x225) ===
Found 0 unique a-prefixed string-label references:
```

The shape signature (1 caller / 12 callees / 549 bytes) initially looked promising — matches the iter-407 rule's expected shape ("1 caller + 10 callees" for successful EnumConversionClass extractions). However, **zero `aXxx` refs** is the definitive metadata-only signal per clause #6's iter-414 refinement.

### Architectural insight — pattern generalizes across template families

The engine has THREE conversion-class families now confirmed to share clause #6 break-out:
1. **Template `EnumConversionClass<T>`** — iter-410 finding (DifficultyLevelType, ForceAlignmentType, MapEnvironmentType)
2. **Template `DynamicBitfieldConversionClass<T>`** — iter-424 finding (GameObjectCategoryType, GameObjectPropertiesType — fall through to clause #7 ERROR-strings)
3. **NON-TEMPLATE `FactionTypeConverterClass`** — **iter-425 NEW finding** (zero `aXxx` refs at 549 bytes; clause #6 metadata-only)

This is **clause #6 generalization**, not a new clause. The architectural pattern is:
> "Type-converter class with an XML-loaded data backing → populator function contains zero static string registrations because the actual flag/enum names live in `data/xml/<typename>.xml` config files."

Whether the class is template-parameterized (`Enum*<T>`, `DynamicBitfield*<T>`) or standalone (`FactionTypeConverterClass`) is irrelevant — the "config-driven type registry" architecture is consistent across all 3 families.

### iter-407 rule clause #6 EXTENDED

The codified rule's clause #6 now lists 4 examples (was 3):
- `EnumConversionClass<DifficultyLevelType>` (iter-410)
- `EnumConversionClass<ForceAlignmentType>` (iter-410)
- `EnumConversionClass<MapEnvironmentType>` (iter-414 size-threshold refinement)
- **`FactionTypeConverterClass`** (iter-425 — non-template generalization)

Plus an architectural generalization note that clause #6 covers BOTH template variants AND non-template standalone classes — the pattern is "config-driven type registry with no in-binary canonical names."

## What did NOT advance

- **Codification trigger NOT hit**: 1 new clause #6 instance is a generalization, not a NEW pattern. Per iter-407's 3-instance precedent, only NEW patterns trigger codification.
- **No NEW operator-visible UX shipped**: zero strings means no ComboBox dropdown can be populated from this populator. Faction names live elsewhere (XML config; iter-217 already auto-detects them via SWFOC_GetFactionRoster).

## What shipped

1. **`tools/iter425_find_faction_type_converter.py`** (NEW) — locates FactionTypeConverterClass populator + analyzes shape
2. **`feedback_static_data_re_extraction.md`** clause #6 extended (4 examples + non-template generalization note)
3. **iter-425 close-out doc** (this file)

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-424 chain
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 200 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (UNCHANGED from iter-404; correct incremental behavior)
- ✅ iter-407 codified rule extended cleanly without breaking existing 7 clauses
- ✅ NO ledger pin needed (clause #6 metadata-only break-out)

## iter-423 Phase 3 survey closure

The iter-423 Phase 3 callgraph-mining survey discovered **2 unexplored static-data classes** beyond the EnumConversionClass<T> family:
1. `DynamicBitfieldConversionClass<GameObjectPropertiesType>` — iter-424 → clause #7 break-out
2. `FactionTypeConverterClass` — iter-425 → clause #6 break-out

**Both are now mapped to existing iter-407 rule clauses.** The Phase 3 survey is CLOSED with these findings:
- 0 NEW clauses required
- 2 existing clauses extended (clause #6 + #7)
- 0 NEW operator-visible UX shipped (both classes are XML-loaded metadata-only)
- 1 architectural generalization documented: clause #6/#7 pattern applies across both template + non-template class families

## Net iter-425 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure RE + codified-rule generalization iter) |
| New tools | 1 (iter425_find_faction_type_converter.py) |
| Doc shipped | 1 close-out doc + clause #6 extension |
| Pattern observations flagged | 1 (architectural generalization: config-driven type registry pattern spans template + non-template classes) |
| Codified rule extension | iter-407 clause #6 now covers non-template variants explicitly |
| Cycle time | ~10 min (address lookup + extraction + rule edit + close-out) |

**iter-425 is a productive closure iter** — finishes the iter-423 Phase 3 NEW-pattern survey by mapping both unexplored classes to existing rule clauses; rule architecture proves robust across template/non-template variants; closes the survey question definitively.

94th post-iter-323 arc iter (4th post-survey-completion iter); 155th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-426)

The iter-423 event-driven defer cluster (iter-416 Play_Animation + iter-422 LocomotorState + iter-423 SWFOC_TriggerVictory) is now ripe for codification:

Options:

1. **Codify "event-driven defer" meta-rule at 3-instance trigger** (iter-416/422/423) — would be 21st codified rule. Per iter-359 meta-rule precedent, 3-instance trigger fires. Documents negative-applicability of iter-302 "Engine has Lua API" rule. **HIGHEST META-VALUE option** — captures durable architectural insight about which engine subsystems are amenable to bridge wires vs which require multi-iter A1.x offset RE.

2. **Operator changelog supplement11** — covers iter 420-425 (6-iter window since supplement10 at iter-419/420). Per iter-372 ~12-instance post-arc docs cadence.

3. **Cheap-insurance republish** — last was iter-422 (3 iters ago).

4. **NEW arc-class kickoff: SWFOC_TriggerVictory multi-iter** — committing to ~5-iter A1.x arc.

5. **Headline-doc quad mini-refresh** — covers iter 421-425 (5-iter window; would mirror iter-413's mini-refresh pattern).

Recommended: option 1 (codify event-driven defer rule). 3-instance trigger has fired; per iter-359 meta-rule precedent, codification is the natural next move. Concrete deliverable mirroring iter-407/iter-407/iter-359/iter-368/iter-371/iter-373/iter-374/iter-380/iter-388 codification pattern.
