# Iter 405 — 2nd callgraph-mining extraction: EnumConversionClass<ModelAnimType> @ 0x140279010 (111 animation names)

**Date:** 2026-05-07
**Arc class:** RE extraction + ledger pin (compounds iter-402-404 pattern to 2/3 codification trigger)
**Predecessor:** iter-404 (UnitAbilityType mini-arc finale)
**Successor (queued):** iter-406 (TBD per "Next iter" below)

## What this iter does

2nd application of the callgraph-mining → static-data extraction pattern from iter-402-404. Picks the largest remaining EnumConversionClass cluster from `untouched_subsystems.md`:

- `EnumConversionClass<ModelAnimType>` @ `0x140279010` (size 9313 bytes, 33% larger than UnitAbilityType's 7012 bytes)

**Outcome**: 111 unique animation name fragments extracted (+42 vs the 69-name UnitAbilityType extraction). Pattern shape stable across 2 instances.

## RE extraction

### Callgraph metadata
```
addr:        0x140279010
name:        sub_140279010
size:        9313
end_addr:    0x14027B471
source:      full_b70-71.json
verified:    -- (no ledger entry; iter-405 add)
rtti_refs:   EnumConversionClass<enum ModelAnimType>
n_callers:   1
n_callees:   10  (same callee shape as UnitAbilityType: operator_new + free + RegisterMapping helpers)
```

### Extracted animation name samples (111 total)
The full list is in `tools/extract_enum_conversion_strings.py` output. Highlights:

| Category | Sample names |
|---|---|
| Hero/saber moves | `Saber_Throw`, `Saber_Catch`, `Saber_Control`, `Force_Revel_Begin`, `Force_Revel_End`, `Force_Revel_Loop` |
| Combat poses | `Attack_Idle`, `Idle_Crouch`, `Block_Blaster`, `Idle_Block_Blast`, `Move_While_Crouch` |
| Death anims | `Die`, `Choke_Die`, `Eaten_Die`, `Fire_Die`, `Crushed`, `Self_Destruct` |
| Flinch reactions | `Flinch_Front`, `Flinch_Back`, `Flinch_Left`, `Flinch_Right`, `Attack_Flinch_Front`, `Attack_Flinch_Back` |
| Take-off/landing | `Take_Off`, `Land`, `Fly_Land`, `Fly_Land_Drop`, `Fly_Land_Idle`, `Fly_Idle` |
| Deploy/structure | `Deploy`, `Deployed_Walk`, `Deployed_Die`, `Structure_Open`, `Structure_Close`, `Structure_Hold` |
| Misc | `Heal`, `Cinematic`, `Talk`, `Talk_Gesture`, `Talk_Question`, `Spinmove`, `Swordspin` |

(Full list with IDA truncation forms: see `tools/extract_enum_conversion_strings.py 0x140279010 full_b70-71.json` output)

## Pattern compounding (codification 1/3 → 2/3)

The iter-302 codified rule (`feedback_engine_already_does_this`) gets a 2nd extension instance:

| Iter | Target | Names extracted | Function size | Operator UX consumer |
|---|---|---|---|---|
| **402-404** | `EnumConversionClass<UnitAbilityType>` @ 0x1405DEA20 | 69 | 7012 bytes | UnitControl tab Activate_Ability ComboBox (iter-403) |
| **405 (THIS)** | `EnumConversionClass<ModelAnimType>` @ 0x140279010 | 111 | 9313 bytes | DEFERRED — no current Lua wire for animation triggering |
| 3rd? | `EnumConversionClass<GUIGadgetComponentType>` @ 0x1401D98B0 | TBD | 7743 bytes | DEFERRED — UI-component names; minimal operator value |

**2/3 codification trigger reached.** Pattern: "EnumConversionClass static-initializer mining via callgraph + IDA decompile body". Stable across 2 distinct enum types with same callee shape (operator_new + free + 31-entry RegisterMapping). 3rd instance compounds to full codification.

## Why operator UX is DEFERRED for ModelAnimType

Animation names are valuable operator data BUT no current bridge wire calls Play_Animation by name. Animations are engine-driven (death triggers Die anim, attack triggers Attack_Idle, etc.). For operator-facing animation control, we'd need:
1. RE for `Play_Animation(animation_name)` engine helper
2. Bridge `SWFOC_PlayAnimationLua` wire (~3 LoC via iter-154 helper)
3. Camera & Debug or new Animation tab UI consumer

This is a future arc (likely iter-406+ or fresh session). iter-405 captures the ground truth (111 names + ledger pin) so when the future arc arrives, the C# const list is ready to embed in <5 minutes.

## What shipped

1. **`tools/extract_enum_conversion_strings.py`** — generalized extractor (replaces iter-402's UnitAbilityType-specific tool); parametrized over `<addr_hex> <corpus_filename>` so future EnumConversionClass extractions are 1-line invocations
2. **`tools/iter405_ledger_add.py`** — adds `rva_model_anim_type_enum_init` @ 0x279010 with 3-tool consensus (binary-fingerprint identity per iter-258 + iter-404 precedent)
3. **`verified_facts.json`**: 319 → 320 entries; 307 VERIFIED; lint 0/0
4. **iter405 close-out doc** (this file)

## Verification gates ALL GREEN at iter-405

- ✅ Verifier lint 0/0 at 320 entries (307 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ Ledger entry uses 3-tool consensus pattern from iter-404
- ✅ All editor build/test gates inherit GREEN from iter-401-404 chain
- ✅ Bridge harness inherits 1100/0
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-405 ships 0 source/test/XAML changes)

## Net iter-405 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML (pure RE extraction + ledger iter) |
| New tools | 1 (extract_enum_conversion_strings.py — generalized); 1 (iter405_ledger_add.py) |
| Catalog entries | 319 → 320 (+1 ledger entry) |
| Doc shipped | 1 close-out doc (this file) |
| Pattern observations flagged | NEW (compounds iter-302 rule extension trigger 1/3 → **2/3**) |
| Names extracted (cumulative across 2 EnumConversionClass instances) | 69 + 111 = **180 engine-canonical strings** |
| Cycle time | ~10 min (callgraph query + extraction tool generalization + ledger add + close-out) |

**iter-405 is the 2nd successful callgraph-mining→ledger-pin chain.** Pattern proven stable across 2 distinct EnumConversionClass instances. Future EnumConversionClass extractions ship at ~5 min cycle (extraction tool is now generalized; ledger script is template-able).

74th post-iter-323 arc iter (2nd callgraph-mining extraction); 135th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-406)

Options:

1. **3rd EnumConversionClass extraction** — `<GUIGadgetComponentType>` @ 0x1401D98B0 (7743 bytes); compounds to 3/3 codification trigger; likely DEFER UX consumer (UI-component names have minimal operator value)
2. **Codify the rule at 2/3** per iter-337 meta-rule precedent (3-instance for meta-rules, but 2 strong instances may justify early codification given rule template is stable)
3. **Operator changelog supplement** covering iter 401-405 (closes 12-iter doc gap since iter-393)
4. **NEW arc-class kickoff** — RE for `Play_Animation` engine helper to unlock the deferred ModelAnimType UX consumer
5. **PerceptionParameterBindingsClass** mining (different cluster shape; 13 funcs total)

iter-406 likely option 1 (compound to 3/3) OR option 4 (unlock deferred UX consumer for ModelAnimType).
