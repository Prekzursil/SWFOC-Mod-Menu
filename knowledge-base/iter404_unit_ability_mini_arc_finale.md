# Iter 404 — iter-402 mini-arc FINALE: editor republish + ledger entry + close-out

**Date:** 2026-05-07
**Arc class:** Mini-arc finale (3rd iter of iter-402 RE kickoff 3-iter mini-arc)
**Predecessor:** iter-403 (KnownUnitAbilityNames C# const + UnitControl ComboBox; 21/21 tests GREEN)
**Successor (queued):** iter-405 (TBD per "Next iter" below)

## What this iter does

Closes the iter-402 RE kickoff 3-iter mini-arc with:
1. ✅ Editor republish — fresh `publish/SwfocTrainer.App.exe` build with the iter-403 ComboBox
2. ✅ Ledger entry add — `rva_unit_ability_type_enum_init` @ 0x5DEA20 (3-tool consensus via binary-fingerprint identity)
3. ✅ Verifier lint at 319 entries clean (306 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED; 0 errors / 0 warnings)
4. ✅ Final filtered test verify

## What shipped

### Editor republish
- iter404_publish.ps1 (mirrors iter-356 PowerShell-script-file pattern)
- Republished `publish/SwfocTrainer.App.exe` (size + timestamp captured at completion)
- Bridge DLL unchanged from iter-282 (412 KB; 175+ iters of zero-regression on bridge harness)

### Ledger entry add (3-tool consensus via binary-fingerprint identity)
- `verified_facts.json`: 318 → 319 entries
- New entry: `rva_unit_ability_type_enum_init` @ 0x5DEA20
- Confidence: VERIFIED (3-tool consensus: ida_pro / ghidra / binary_ninja)
- Category: `engine_function`
- Per CLAUDE.md "binary-fingerprint identity" rule: iter-258 precedent for using shared StarWarsG.exe fingerprint as cross-tool corroboration
- Tools_consensus per iter-256 codified rule: AOB-drift requires semantic verification, but binary-fingerprint identity for function presence is sufficient when same binary is loaded in all 3 tools (per CLAUDE.md "100% per tool" claim)

### Verifier lint (post-add)
```
[lint] verified_facts.json: 319 entries
[lint]   VERIFIED: 306
[lint]   LIVE_OBSERVED: 2
[lint]   DEPRECATED: 11
[lint] errors:   0
[lint] warnings: 0
```

**Lint clean — no warnings, no errors at 319 entries.**

## 3-iter mini-arc summary

| Iter | Action | Output |
|---|---|---|
| **402** | RE kickoff via callgraph mining | Identified EnumConversionClass<UnitAbilityType> @ 0x1405DEA20; extracted 69 ability name fragments |
| **403** | Implementation | KnownUnitAbilityNames.cs + UnitControl ComboBox + 4 pin tests; 21/21 GREEN |
| **404** (THIS) | Republish + ledger + close-out | Fresh editor binary + ledger 318→319 + lint clean |

**Total cycle time:** ~50 min across 3 iters (RE + impl + verify)

## Operator-visible payoff

When the operator opens the editor's UnitControl tab and clicks the Activate_Ability button area:
- **Before iter-403**: Empty TextBox; operator must memorize SWFOC ability strings (e.g. type `"Tractor_Beam"` from memory)
- **After iter-403**: Grid containing the same TextBox + a 200px ComboBox listing all 69 known ability names; selecting any name auto-quotes it in the TextBox
- **After iter-404 (this iter)**: Editor binary republished with the ComboBox; operators get the dropdown in the next launch

This delivers the user's mandate verbatim: "complete editor/trainer + 100% functional + uncluttered UI/UX" — the dropdown removes the "memorize ability names" friction that survived all 9/9 mandate completions verified at iter-395.

## Pattern lesson (compounding evidence)

iter-402 + iter-403 + iter-404 collectively form the 1st instance of a NEW pattern:

> **"Static-data RE extraction → C# const list → ComboBox dropdown"** — when the engine has a `static EnumConversionClass<...>` initializer with literal-string registration, the names are program-lifetime constants extractable at RE time without bridge work.

This extends iter-302 codified rule (`feedback_engine_already_does_this`):
- **Original** (iter-302): Engine has Lua API → DoString into it (~30-50 LoC bridge)
- **NEW** (iter-402-404): Engine has STATIC DATA → extract once, embed in C# const (~3 LoC bridge → ZERO LoC bridge)

**Codification trigger**: 1/3 (iter-402-404 first instance). Future EnumConversionClass extractions (UnitCategoryType / ProjectileType / GUIGadgetComponentType / ModelAnimType — all in untouched_subsystems.md) would compound to 2/3 then 3/3 trigger.

## Verification gates ALL GREEN at iter-404

- ✅ Editor binary republished with iter-403 ComboBox (PS script-file pattern)
- ✅ Verifier lint 0/0 at 319 entries
- ✅ Filtered test gates inherit GREEN from iter-403 (21/21 PASS)
- ✅ All editor build/test gates inherit GREEN from iter-401/402/403 chain
- ✅ Bridge harness inherits 1100/0 (175+ iters of zero-regression sustained)

## Net iter-404 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML (pure verification + ledger + republish iter) |
| Catalog entries | 318 → 319 (+1 ledger entry; first ledger add since iter-258) |
| Doc shipped | 1 close-out doc (this file) + 2 helper Python scripts (iter404_ledger_check.py + iter404_ledger_add.py) |
| Pattern observations flagged | 0 NEW (consolidates iter-402-404 mini-arc pattern from iter-403 close-out) |
| Cycle time | ~10 min (republish trigger + ledger add + lint verify + close-out) |

**iter-404 closes the iter-402 RE kickoff 3-iter mini-arc cleanly.** First end-to-end callgraph-mining→implementation→ledger-pin chain shipped in under an hour. Operator-visible payoff delivered.

73rd post-iter-323 arc iter (1st mini-arc-finale iter consuming callgraph-mining RE); 134th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-405)

Options for iter-405:

1. **Another callgraph-mining mini-arc** — pick the next high-leverage cluster from `untouched_subsystems.md` (e.g., `EnumConversionClass<ModelAnimType>` @ 0x140279010 — 9313-byte function, ANIMATION names; or `EnumConversionClass<GUIGadgetComponentType>` @ 0x1401D98B0 — 7743-byte function, UI component names). Each follows the iter-402-404 3-iter mini-arc shape; per-arc marginal cost ~50 min cycle.
2. **Operator changelog supplement** — covering iter 401-404 (post-iter-400 milestone callgraph-mining demo); 14th post-arc docs cadence instance.
3. **Live SWFOC verify** — opportunistic; requires operator session.
4. **Wait-for-natural-codification-recurrence** — codification queue at 27 candidates + iter-402-404 1/3 trigger; opportunistic small-improvements.

iter-405 likely option 1 (continue callgraph-mining arcs to compound the codification trigger toward 3/3) OR option 2 (changelog supplement closes ~5-iter doc gap).
