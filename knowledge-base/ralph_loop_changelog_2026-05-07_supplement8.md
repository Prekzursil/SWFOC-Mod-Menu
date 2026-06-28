# Ralph Loop Changelog 2026-05-07 — Supplement 8 (iter 401-407)

**Window:** iter 401-407 (7 iters)
**Predecessor:** supplement7 (iter 393-398 UX Pattern 2 finale + headline-doc + iter-400 milestone prep)
**Successor (queued):** supplement9 (iter 408+; covers next callgraph-mining arc)

## What this supplement covers

Closes the 14-iter doc gap since supplement7 (which covered iter 393-398). Covers the post-iter-400 milestone callgraph-mining arc that delivered the user's explicit "highest-leverage next step": **3 EnumConversionClass extractions yielding 263 engine-canonical strings, the iter-403 UnitControl ComboBox UX shipment, and the iter-407 codification of the static-data RE extraction pattern as the 20th codified rule.**

## Phase 1 — Post-milestone deep verification (iter 401)

iter-401 CLOSED — 5-tier verification + deep callgraph CLI exercise per user explicit directive ("queryable callgraph + subsystem index... cluster functions by call neighborhood").

| Tier | Verification |
|---|---|
| T1 Editor binary | 157.88 MB at May 7 12:20:02 (iter-397 republish; sustained) |
| T2 Bridge DLL | 412 KB at May 7 02:01:49 (iter-282 build) |
| T3a Verifier lint | 0/0 at 318 entries |
| T3b Callgraph SQLite index | 22,728 funcs / 152,032 xrefs / 3,737 RTTI refs / 282 verified facts (FULLY OPERATIONAL) |
| T3c Replay binary | 937.8 KB at May 7 02:02:08 |

**4 callgraph CLI query types exercised** end-to-end (info / fn / cluster / untouched). 9-function 2-hop cluster around SetHP demonstrated the user's "cluster functions by call neighborhood" use case empirically. 20,854 / 22,728 functions = 91.7% of binary unmined.

## Phase 2 — Callgraph-mining-driven feature shipment (iter 402-404)

**3-iter mini-arc**: First end-to-end callgraph-mining → implementation → ledger-pin chain.

### iter-402 — RE kickoff
- Identified `EnumConversionClass<UnitAbilityType>` @ 0x1405DEA20 as high-leverage target (untouched_subsystems.md largest non-template cluster)
- Extracted 69 ability name fragments via `tools/extract_unit_ability_strings.py` (NEW)
- Designed 3-iter mini-arc

### iter-403 — Implementation
- **NEW**: `src/SwfocTrainer.Core/Diagnostics/KnownUnitAbilityNames.cs` (75 LoC; IReadOnlyList<string> All sorted alphabetically)
- **MODIFIED**: `UnitControlTabViewModel.cs` — added `AbilityNamePresetSelection` property (auto-quotes Lua expression on ComboBox selection)
- **MODIFIED**: `MainWindowV2.xaml` — replaced TextBox with Grid containing TextBox + ComboBox; ItemsSource via `x:Static diag:KnownUnitAbilityNames.All`
- **NEW**: `Iter403KnownUnitAbilityNamesTests.cs` (4 pin tests; 21/21 GREEN in 206 ms)
- Operator-visible payoff: UnitControl tab Activate_Ability button now has 69-name dropdown

### iter-404 — Mini-arc finale
- Editor republished: 157.89 MB at May 7 12:58:37 (+0.01 MB for ComboBox)
- Ledger entry added: `rva_unit_ability_type_enum_init` @ 0x5DEA20 (3-tool consensus via binary-fingerprint identity per iter-258 precedent)
- Verifier lint: 0/0 at 319 entries (306 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)

**Total cycle time iter-402-404**: ~50 min (RE + impl + verify + republish)

## Phase 3 — Pattern compounding (iter 405-406)

### iter-405 — 2nd EnumConversionClass extraction
- `EnumConversionClass<ModelAnimType>` @ 0x140279010 (size 9313 bytes)
- 111 animation name fragments extracted (42% MORE than UnitAbilityType)
- Tooling generalized: `tools/extract_enum_conversion_strings.py` (parametrized over `<addr> <corpus_file>`)
- Ledger 319 → 320 (`rva_model_anim_type_enum_init`)
- UX consumer DEFERRED (no Play_Animation Lua wire yet)
- Cycle time: ~10 min (5× speedup vs iter-402-404 mini-arc)

### iter-406 — 3rd EnumConversionClass extraction (3/3 trigger reached)
- `EnumConversionClass<GUIGadgetComponentType>` @ 0x1401D98B0 (size 7743 bytes)
- 83 GUI component-name fragments extracted
- Ledger 320 → 321 (`rva_gui_gadget_component_type_enum_init`)
- UX consumer DEFERRED (engine-internal UI rendering pipeline)
- Cycle time: ~8 min (further speedup)

**3/3 codification trigger reached** for static-data RE extraction pattern.

| Iter | Target | Names | Function size | UX consumer | Cycle time |
|---|---|---|---|---|---|
| 402-404 | UnitAbilityType | **69** | 7012 bytes | SHIPPED (UnitControl ComboBox) | ~50 min |
| 405 | ModelAnimType | **111** | 9313 bytes | DEFERRED | ~10 min |
| 406 | GUIGadgetComponentType | **83** | 7743 bytes | DEFERRED | ~8 min |
| **Cumulative** | 3 enum types | **263 strings** | 24KB total | 1 consumer + 2 deferred | **~70 min total** |

## Phase 4 — Codification (iter 407)

iter-407 CLOSED — codified `feedback_static_data_re_extraction.md` at 3/3 trigger.

- **20th codified rule** in project total
- **5th Tier-1 production rule** (after iter-302/334/345/380/388)
- 3-instance trigger justified by **mechanical pattern shape** (vs iter-302/388 6-instance precedent for heuristic patterns)
- 11-section template per iter-388 latest production-rule shape:
  - Rule + Why + How (7-step recipe) + Examples (3 instances) + Honest break-out (5 cases) + Cost-benefit + Cross-references to iter-302/256 + Codification trigger + 4th+ candidates
- MEMORY.md index updated: codified rules 19 → 20

**Cross-reference taxonomy** completed:
- **iter-302** (`feedback_engine_already_does_this.md`): engine has Lua API → DoString roundtrip
- **iter-407** (`feedback_static_data_re_extraction.md`): engine has STATIC DATA → extract once + embed
- Together they form the 2-tier "engine-already-does-this" pattern family

## Cumulative state at end-of-arc (post-iter-407)

| Metric | iter-400 | iter-407 | Delta |
|---|---|---|---|
| Codified rules | 19 | **20** | +1 (iter-407 static-data-re-extraction) |
| Tier-1 production rules | 4 | **5** | +1 (iter-407 5th Tier-1) |
| Engine-canonical strings extracted | 0 | **263** | +263 (iter-402+iter-405+iter-406) |
| Ledger entries | 318 | **321** | +3 (iter-404/iter-405/iter-406 ledger adds) |
| Editor V2 ComboBox UX shipments | 0 (in this window) | **1** | +1 (iter-403 UnitControl Activate_Ability dropdown) |
| Editor binary size | 157.88 MB | **157.89 MB** | +0.01 MB (KnownUnitAbilityNames.cs + ComboBox) |
| Tools shipped | 0 | **5** | +5 (extract_unit_ability_strings + extract_enum_conversion_strings + iter404/405/406 ledger_add scripts) |

## NEW patterns observed iter 401-407

1. **Callgraph-mining as feature-extraction methodology** (iter-401 demonstrated; iter-402-406 applied 3×): user's "cluster functions by call neighborhood" directive is mechanically actionable via the callgraph SQLite index. 91.7% of binary surface unmined gives a near-inexhaustible feature frontier.

2. **Static-data RE extraction is 5-10× cheaper than RVA-pin alternative** (per iter-407 cost-benefit analysis): when constants are program-lifetime, a one-time extraction at RE time + C# const list beats per-call DoString roundtripping by orders of magnitude in both LoC and runtime cost.

3. **Tooling generalization compounds the codification ROI** (iter-405 demonstrated): the iter-402 UnitAbilityType-specific tool was generalized in iter-405 to `extract_enum_conversion_strings.py <addr> <corpus_file>`. Marginal cost dropped 50 min → 10 min → 8 min across the 3 instances. Future extractions ship at ~5 min via the codified recipe.

4. **Honest UX defer enables compounding**: iter-405 + iter-406 had no operator-visible payoff (no Lua wire for ModelAnimType / GUIGadgetComponentType), but the codification trigger compounded REGARDLESS of per-instance UX consumer. Pattern recipe value is independent of per-instance shipment value.

5. **Mechanical pattern shape justifies lower codification trigger** (iter-407 codified at 3/3 vs iter-302/388 6-instance baseline): per iter-345 evidence-quality precedent, when the recipe has no judgment calls, fewer instances prove validity.

## Verification gates GREEN throughout (post-iter-407)

- ✅ Bridge harness 1100/0 (continuous since iter-225 = 182 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 321 entries (308 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ Editor build 0 errors / 0 warnings (iter-356 zero-warnings standard sustained)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; sustained iter 405/406/407)
- ✅ Filtered tests 21/21 PASS in 206 ms (iter-403 confirmed; sustained)
- ✅ Callgraph SQLite index FULLY OPERATIONAL (iter-401 confirmed; sustained)
- ✅ Reverse-orphan tests 1/1 PASSED <1 ms (iter-395 baseline; sustained)
- ✅ ENTIRE 4910-line MainWindowV2.xaml zero iter-N drift (iter-397 closure; sustained)

## Source attribution

- 7 close-out docs (iter401_through_iter407) in `knowledge-base/`
- ralph_loop_state.md iteration log (iter 401-407 detailed entries)
- 5 NEW tools in `tools/` (extract_unit_ability_strings + extract_enum_conversion_strings + 3 ledger-add scripts)
- 1 NEW codified rule (`feedback_static_data_re_extraction.md`)
- MEMORY.md index updated with NEW entry

## Next supplement

Supplement 9 will cover iter 408+ including next callgraph-mining cycle + any forward-applicability validations of iter-407 rule.
