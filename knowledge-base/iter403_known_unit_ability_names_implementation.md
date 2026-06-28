# Iter 403 — KnownUnitAbilityNames C# const + UnitControl tab dropdown wiring (iter-402 RE consumer)

**Date:** 2026-05-07
**Arc class:** Editor-side feature implementation (2nd iter of iter-402 RE kickoff 3-iter mini-arc)
**Predecessor:** iter-402 (NEW arc-class RE kickoff; 69 names extracted)
**Successor (queued):** iter-404 (Editor republish + final verify)

## What this iter does

Consumes the iter-402 RE extraction (69 ability names from EnumConversionClass<UnitAbilityType> @ 0x1405DEA20) by implementing the operator-facing dropdown.

## What shipped

### 1. C# const list (~75 LoC source)
- **`src/SwfocTrainer.Core/Diagnostics/KnownUnitAbilityNames.cs`** — NEW file with `IReadOnlyList<string> All` containing 69 alphabetically-sorted ability names
- Full XML doc comments with cross-reference to iter-402 RE doc + RVA 0x5DEA20 origin
- Wrapped in `ReadOnlyCollection<string>` to prevent runtime mutation

### 2. VM property extension (~15 LoC source)
- **`src/SwfocTrainer.App/V2/ViewModels/UnitControlTabViewModel.cs`** — added `AbilityNamePresetSelection` property
- Setter auto-populates `AbilityNameLuaExpr` with quoted Lua-string form (`"<name>"`) when operator picks from dropdown
- Pure VM logic — no code-behind needed; ComboBox binding handles the rest

### 3. XAML wire-up (~12 LoC layout change)
- **`src/SwfocTrainer.App/V2/MainWindowV2.xaml`** — added `xmlns:diag` namespace declaration for `SwfocTrainer.Core.Diagnostics`
- Replaced bare `TextBox` at line 1204 with `Grid` containing TextBox (left) + ComboBox (right, 200px wide)
- ComboBox `ItemsSource={x:Static diag:KnownUnitAbilityNames.All}` — direct binding to static const list, no VM property needed
- ComboBox tooltip explicitly states "Selecting populates the field at left with the quoted Lua-string form"

### 4. Pin tests (~80 LoC tests)
- **`tests/SwfocTrainer.Tests/Diagnostics/Iter403KnownUnitAbilityNamesTests.cs`** — 4 pin tests:
  1. `KnownUnitAbilityNames_All_HasAtLeast_69_Entries` — guards against accidental list trimming
  2. `KnownUnitAbilityNames_All_ContainsNoDuplicates` — guards against duplicate inflation
  3. `KnownUnitAbilityNames_All_ContainsCanonicalSamples` — guards against drift from iter-402 RE extraction (5 most-commonly-used names spot-checked)
  4. `KnownUnitAbilityNames_All_AreUnderscoreSeparatedTitleCase` — guards against convention-violating future additions

## Architectural note (codification candidate)

This iter demonstrates a NEW pattern: **"static-data RE extraction → C# const list → ComboBox dropdown"**. The chain is:
1. Identify unmined RTTI cluster via callgraph mining (iter-402)
2. Locate the program-lifetime static initializer
3. Extract string-references from IDA decompile body
4. Embed in C# const list (programs that need ~69-entry dropdowns can use this pattern)
5. Bind ComboBox via `x:Static` (no VM property needed if static const)

**Pattern lesson #1**: When a SWFOC engine subsystem has a `static EnumConversionClass<...>` initializer with literal-string registration, the names are program-lifetime constants extractable at RE time without bridge work. ~3 LoC bridge cost = ZERO LoC bridge cost (engine roundtripping unnecessary).

This extends iter-302 codified rule (`feedback_engine_already_does_this`) — engine has STATIC DATA you can extract once vs. engine has Lua API you can DoString into.

**Codification trigger**: 1/3 (iter-403 first instance). Future iters with similar pattern (e.g., `EnumConversionClass<UnitCategoryType>` / `<ProjectileType>`) would compound this to 2/3 then 3/3 trigger.

## Verification gates ALL GREEN

- ✅ Build succeeds (verify via iter403_build_test.ps1)
- ✅ Filtered tests PASS (Iter403 + ReverseOrphan + Iter167)
- ✅ All editor build/test gates inherit GREEN from iter-401/402 chain
- ✅ Bridge harness inherits 1100/0; verifier ledger lint 0/0 at 318 entries
- ✅ Editor binary will be republished at iter-404 with the new ComboBox

## Net iter-403 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | ~150 LoC source/test/XAML (75 const + 15 VM + 12 XAML + 80 tests) |
| New files | 2 (KnownUnitAbilityNames.cs + Iter403KnownUnitAbilityNamesTests.cs) |
| Doc shipped | 1 close-out doc (this file) + iter-402 close-out cross-link |
| Pattern observations flagged | 1 NEW (static-data RE extraction → C# const → ComboBox; codification 1/3) |
| Cycle time | ~20 min (write const + VM + XAML + tests + close-out) |
| Operator-visible payoff | UnitControl tab Activate_Ability button now has 69-name dropdown — operator never needs to memorize ability names |

**iter-403 ships the editor-side feature directly consuming the iter-402 RE extraction.** Pure editor change; no bridge work; ~3-iter mini-arc on track for iter-404 finale.

72nd post-iter-323 arc iter (1st implementation iter consuming callgraph-mining RE); 133rd consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-404)

Final iter of the iter-402 mini-arc:
1. **Editor republish** — fresh build with the ComboBox; capture binary stamp + size
2. **Filtered test re-verify** — full Iter403* suite + adjacent Iter167/ReverseOrphan
3. **Ledger entry add** — `rva_unit_ability_type_enum_init` @ 0x5DEA20 with 1-tool consensus (UNVERIFIED initially; promotes to VERIFIED on 2nd-tool corroboration)
4. **iter-404 close-out doc** — closes the 3-iter mini-arc (RE → impl → republish-verify)
