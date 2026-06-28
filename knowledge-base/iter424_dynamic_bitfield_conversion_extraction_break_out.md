# Iter 424 — DynamicBitfieldConversionClass<T> extraction (break-out finding) + iter-407 rule clause #7 extension

**Date:** 2026-05-07
**Arc class:** Static-data extraction attempt + codified-rule clarification (mirrors iter-411 negative-result extension pattern)
**Predecessor:** iter-423 (preflight + survey discovery + verify)
**Successor (queued):** iter-425 (FactionTypeConverterClass extraction OR codify event-driven defer rule OR operator changelog supplement11)

## What this iter does

Extends iter-423 Phase 3 finding (NEW unexplored static-data class family — `DynamicBitfieldConversionClass<T>`) into actual extraction attempt:

1. **Located both `DynamicBitfieldConversionClass<T>` populator addresses** via callgraph SQLite query:
   - `<GameObjectPropertiesType>` @ 0x14046B350 (size 1521 bytes)
   - `<GameObjectCategoryType>` @ 0x14046AD50 (size 1521 bytes)
2. **Ran iter-405 generalized extraction tool** on both addresses — same break-out result on both (5 ERROR strings; clause #7 of iter-407 rule).
3. **Extended iter-407 codified rule's clause #7** to cover the bitfield template family explicitly (architectural generalization).

## Findings

### Both populators return identical 5 ERROR strings

Extraction output for BOTH addresses (0x14046B350 + 0x14046AD50):
- `aErrorCanTCreat` (Error_Cant_Create...)
- `aErrorCanTOpenX` (Error_Cant_Open_X...)
- `aErrorCouldnTAd` (Error_Couldn't_Add...)
- `aErrorTooManyEn` (Error_Too_Many_Entries...)
- `aLlx` (printf format spec %llx)

**This is identical to iter-410's `EnumConversionClass<GameObjectCategoryType>` @ 0x14046AD50 break-out finding** — same 5 error strings.

### Architectural insight

The engine has THREE conversion-class template families:
1. **`EnumConversionClass<T>`** — STATIC registration via RegisterMapping at program init (iter-407 rule applies; 18 successful extractions)
2. **`DynamicEnumConversionClass<T>`** — RUNTIME XML loader (clause #8 break-out; identical 4-error template across all 5 instantiations)
3. **`DynamicBitfieldConversionClass<T>`** — **NEW iter-424 finding**: shares the parser-error template with `EnumConversionClass<T>` for the GameObjectCategoryType/PropertiesType cases. **Both template variants of the SAME underlying type** (GameObjectCategoryType has both an enum form and a bitfield form) hit the same break-out reason.

This is **clause #7 generalization**, not a new clause. The architectural pattern is "actual flag/enum names live in XML data files; the populator only contains generic error-handling strings." Whether the parameterized template is `Enum*` or `Bitfield*` is irrelevant — both follow the same XML-loader architecture.

### iter-407 rule clause #7 EXTENDED

The codified rule's clause #7 now lists 3 examples (was 1):
- `EnumConversionClass<GameObjectCategoryType>` (iter-410 finding)
- `DynamicBitfieldConversionClass<GameObjectCategoryType>` (iter-424)
- `DynamicBitfieldConversionClass<GameObjectPropertiesType>` (iter-424)

Plus an architectural note that "the bitfield template family shares the same parser-error template as the enum family for these types — actual flag names live in XML data files."

This extension is durable across both bitfield template variants AND aligns with clause #8 (DynamicEnumConversionClass XML-loader) — together they document the engine's "config-driven" type registry pattern.

## What did NOT advance

**Codification trigger NOT hit**: while iter-424 added 2 new instances (DynamicBitfieldConversionClass<GameObjectPropertiesType> + DynamicBitfieldConversionClass<GameObjectCategoryType>), they are **the same break-out pattern** (clause #7) — not a NEW pattern. Per iter-407's 3-instance codification precedent, only NEW patterns trigger codification, not generalizations of existing clauses.

The 21st codified rule track (3-tier "engine FILESYSTEM/XML" pattern) is still at 1/3 trigger (iter-300 SWFOC_ListMods only).

## What shipped

1. **`tools/iter424_find_bitfield_address.py`** (NEW) — locates DynamicBitfieldConversionClass populator addresses via callgraph
2. **`feedback_static_data_re_extraction.md`** clause #7 extended (3 examples + bitfield architectural note)
3. **iter-424 close-out doc** (this file)

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-423 chain
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 199 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (UNCHANGED from iter-404; correct incremental behavior)
- ✅ iter-407 codified rule extended without breaking existing 7 clauses
- ✅ NO ledger pin needed (clause #7 break-out — not new actual data)

## Net iter-424 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure RE + codified-rule extension iter) |
| New tools | 1 (iter424_find_bitfield_address.py) |
| Doc shipped | 1 close-out doc + clause #7 extension |
| Pattern observations flagged | 1 (DynamicBitfieldConversionClass<T> shares parser-error template with EnumConversionClass<T> for XML-loader types) |
| Codified rule extension | iter-407 clause #7 now explicitly covers bitfield variants |
| Cycle time | ~15 min (address lookup + 2 extractions + rule edit + close-out) |

**iter-424 is a productive negative-result extension iter** — confirms that the iter-423 Phase 3 NEW-pattern discovery (`DynamicBitfieldConversionClass<T>`) collapses into the existing iter-407 clause #7 break-out, not a new codification track.

93rd post-iter-323 arc iter (3rd post-survey-completion iter); 154th consecutive NON-A1.x iter per iter-269 lesson #2.

## Reasons this is a useful negative result

1. **Saves future operators ~30 min** — without iter-424, a future operator might re-attempt extraction on `DynamicBitfieldConversionClass<*>` populators expecting flag names, only to discover they hit the same clause #7 break-out
2. **Closes the iter-423 Phase 3 question definitively** — the NEW-pattern discovery is now mapped to existing rule architecture, not a new codification track
3. **Architectural confirmation** — engine's "config-driven" type registry pattern is now documented across BOTH `Dynamic*` template variants (Enum + Bitfield) AND the static `Enum*` variant when the type is XML-loaded

## Next iter (iter-425)

Options:

1. **FactionTypeConverterClass extraction** — explore the iter-423 Phase 3's 2nd unexplored class. Standalone non-template class; unknown structure until decompiled. Could be a NEW pattern or another break-out.

2. **Codify "event-driven defer" meta-rule at 3-instance trigger** (iter-416/422/423) — would be 21st codified rule. Per iter-359 meta-rule precedent, 3-instance trigger fires.

3. **Operator changelog supplement11** — covers iter 420-424 (5-iter window since supplement10 at iter-419/420). Per iter-372 ~12-instance post-arc docs cadence.

4. **NEW arc-class kickoff: SWFOC_TriggerVictory multi-iter** — committing to ~5-iter A1.x arc.

5. **Cheap-insurance republish** — last was iter-422 (3 iters ago).

Recommended: option 1 (FactionTypeConverterClass extraction) — concrete extraction work mirroring iter-424's pattern; either ships NEW data OR adds another break-out clause. Highest information-yield from existing tooling.
