# Iter 406 — 3rd callgraph-mining extraction reaches **3/3 codification trigger**: GUIGadgetComponentType (83 component names)

**Date:** 2026-05-07
**Arc class:** RE extraction + ledger pin (3rd instance compounding iter-402-405 pattern to **3/3 codification trigger**)
**Predecessor:** iter-405 (ModelAnimType extraction; pattern at 2/3)
**Successor (queued):** iter-407 (CODIFICATION at 3/3 trigger — `feedback_static_data_re_extraction.md`)

## What this iter does

Final extraction in the 3-instance series that confirms the iter-302 rule extension pattern. Picks the 3rd EnumConversionClass cluster from `untouched_subsystems.md`:

- `EnumConversionClass<GUIGadgetComponentType>` @ `0x1401D98B0` (size 7743 bytes)

**Outcome**: 83 unique GUI-component name fragments extracted. Pattern shape stable across **3** distinct EnumConversionClass instances. **3/3 codification trigger reached.**

## RE extraction

### Callgraph metadata
```
addr:        0x1401D98B0
name:        sub_1401D98B0
size:        7743
end_addr:    0x1401DB6EF
source:      full_b48-49.json
verified:    -- (no ledger entry; iter-406 add)
rtti_refs:   EnumConversionClass<enum GUIGadgetComponentType>
n_callers:   1
n_callees:   10  (same callee shape as iter-402 + iter-405)
```

### Extracted GUI component name samples (83 total)

| Category | Sample names |
|---|---|
| Buttons | `Button_Left_Disabled`, `Button_Left_Pressed`, `Button_Middle_Disabled`, `Button_Middle_Pressed`, `Button_Right_Disabled`, `Button_Right_Pressed`, `Button_Right_MouseOver` |
| ComboBox | `ComboBox_Left_Cap`, `ComboBox_Popdown` (×3 variants), `ComboBox_TextBox` |
| Dial | `Dial_Left`, `Dial_Middle`, `Dial_Minus`, `Dial_Minus_MouseOver`, `Dial_Minus_Pressed`, `Dial_Plus`, `Dial_Plus_MouseOver`, `Dial_Plus_Pressed`, `Dial_Right`, `Dial_Tab` |
| Frame | `Frame_Background`, `Frame_Bottom_Left`, `Frame_Bottom_Right`, `Frame_Bottom_Transition`, `Frame_Left_Transition`, `Frame_Right_Transition`, `Frame_Top_Left`, `Frame_Top_Right`, `Frame_Top_Transition` |
| ProgressBar | `ProgressBar_Left`, `ProgressBar_Middle` (×2), `ProgressBar_Right` |
| Radio | `Radio_Disabled`, `Radio_MouseOver`, `Radio_Off`, `Radio_On` |
| Scrollbar | `Scroll_Down_Button` (×4 variants), `Scroll_Middle`, `Scroll_Middle_Disabled`, `Scroll_Tab`, `Scroll_Tab_Disabled`, `Scroll_Up_Button` (×4 variants) |
| SmallFrame | `Small_Frame_Background`, `Small_Frame_Bottom` (×3 variants), `Small_Frame_Left`, `Small_Frame_Right`, `Small_Frame_Top`, `Small_Frame_Top_Left`, `Small_Frame_Top_Right` |
| Trackbar | `Trackbar_Scroller` (×11 variants — `_0` through `_10`) |
| Misc | `Check_Off`, `Check_On`, `Scanlines` |

(Full list with IDA truncation forms: see `tools/extract_enum_conversion_strings.py 0x1401D98B0 full_b48-49.json` output)

## Pattern fully proven across 3 instances (codification trigger reached)

| Iter | Target | Names | Function size | Callee shape | UX consumer |
|---|---|---|---|---|---|
| **402-404** | UnitAbilityType @ 0x1405DEA20 | 69 | 7012 bytes | std (operator_new + free + RegisterMapping × 31-75) | UnitControl ComboBox (iter-403; 21/21 GREEN) |
| **405** | ModelAnimType @ 0x140279010 | 111 | 9313 bytes | std (same callee shape) | DEFERRED (no Play_Animation Lua wire) |
| **406 (THIS)** | GUIGadgetComponentType @ 0x1401D98B0 | 83 | 7743 bytes | std (same callee shape) | DEFERRED (engine-internal UI rendering) |

**Cumulative engine-canonical strings extracted: 263** (69 + 111 + 83) across 3 distinct enum types.

**Pattern is fully mechanical**:
1. Identify EnumConversionClass<T> in `untouched_subsystems.md`
2. Run `python tools/callgraph_query.py fn <addr>` for metadata
3. Run `python tools/extract_enum_conversion_strings.py <addr> <corpus_file>` for string list
4. Run `python tools/iter<N>_ledger_add.py` template for ledger entry (binary-fingerprint 3-tool consensus)
5. Verify lint 0/0
6. Optional: ship C# const + ComboBox UX consumer if a Lua wire exists for the enum

**Cycle time per extraction**: ~5-10 min (largely tooling-bound, not RE-bound).

## 3/3 codification trigger reached

Per iter-337 codified `feedback_iter_strategy_preflight_stack.md` rule (3-instance trigger for meta-rules), iter-407 should codify the rule:

- **Rule name**: `feedback_static_data_re_extraction.md` (working title)
- **Tier**: meta-rule (Tier 3) per iter-337/iter-368/iter-371/iter-373/iter-374 precedents OR Tier 1 production rule at 3/6 if treated as a production pattern
- **Decision pending iter-407**: framing as Tier-3 meta-rule (codify-pattern-pattern) vs Tier-1 production (engine-data-extraction-pattern)

**Recommended framing**: Tier 1 production at 3/6 trigger. Reasons:
1. Pattern is concrete + mechanical (not a meta-pattern about other patterns)
2. Unlocks ~5-min cycle time per future EnumConversionClass extraction (huge ROI)
3. Future operators reading the rule get an exact recipe (callgraph → IDA decompile body → string label regex → ledger add template)
4. Honest break-out cases: when no UX consumer exists, defer to "captured for future feature work"

iter-407 will codify the rule with full template (per iter-345/iter-388/iter-380 production rule shape).

## Honest-defer on operator UX

| Enum type | UX value | Status |
|---|---|---|
| UnitAbilityType | HIGH (operators activate abilities by name) | SHIPPED iter-403 ComboBox |
| ModelAnimType | MEDIUM (would unlock Play_Animation arc) | DEFERRED (no Play_Animation wire yet) |
| GUIGadgetComponentType | LOW (engine-internal UI rendering pipeline) | DEFERRED (no operator-actionable wire) |

Pattern: **3rd extraction had even less UX value than 2nd** — but **codification trigger compounds regardless of UX consumer**. The pattern's value is the recipe, not any single extraction outcome.

## What shipped

1. **`tools/iter406_ledger_add.py`** — adds `rva_gui_gadget_component_type_enum_init` @ 0x1D98B0
2. **`verified_facts.json`**: 320 → 321 entries; 308 VERIFIED; lint 0/0
3. **iter406 close-out doc** (this file)

## Verification gates ALL GREEN at iter-406

- ✅ Verifier lint 0/0 at 321 entries (308 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ Ledger entry uses 3-tool consensus pattern from iter-404/405
- ✅ All editor build/test gates inherit GREEN from iter-401-405 chain
- ✅ Bridge harness 1100/0
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-406 ships 0 source/test/XAML changes)

## Net iter-406 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML (pure RE extraction + ledger iter) |
| New tools | 1 (iter406_ledger_add.py — derivative of iter405 template) |
| Catalog entries | 320 → 321 (+1 ledger entry) |
| Doc shipped | 1 close-out doc (this file) |
| Pattern observations flagged | **3/3 codification trigger reached** for static-data RE extraction pattern |
| Names extracted (cumulative across 3 EnumConversionClass instances) | 69 + 111 + 83 = **263 engine-canonical strings** |
| Cycle time | ~8 min (callgraph query + extraction + ledger add + close-out) |

**iter-406 closes the 3-instance pattern proof series.** Pattern is mechanical at ~5-10 min per future extraction. Cumulative 263 engine-canonical strings extracted in ~70 min total (iter-402 through iter-406).

75th post-iter-323 arc iter (3rd callgraph-mining extraction); 136th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-407)

**Recommended**: codify `feedback_static_data_re_extraction.md` at 3/3 trigger (Tier-1 production rule with 6-instance precedent allowance OR Tier-3 meta-rule per iter-337/368/371/373/374).

Codification template per iter-388 latest production-rule shape:
- 11-section structure (Why / How / Examples / Honest break-out / Cost-benefit / etc.)
- 3 confirmed instances + ~4 future candidates (other RTTI clusters with similar shape — see `untouched_subsystems.md`)
- Pattern stability proof (same callee shape across 3 distinct enum types)
- Marginal cost ~5-10 min per future application

Alternative options:
- **iter-407 NEW arc-class kickoff** — RE Play_Animation engine helper to unlock deferred ModelAnimType UX consumer (3-iter mini-arc parallel to iter-402-404)
- **iter-407 operator changelog supplement** — covering iter 401-406 (closes 13-iter doc gap since iter-393)
- **iter-407 4th EnumConversionClass extraction** if untouched_subsystems.md has more candidates (overshoots codification trigger but compounds further)
