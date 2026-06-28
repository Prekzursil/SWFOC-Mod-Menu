# iter-344 — MainViewModelV2 wire-up to pass iconResolver to CombatTabViewModel + republish (closes iter-343 deferred composition root wiring; 6th consumer of iter-337 preflight rule)

**Date:** 2026-05-07
**Arc class:** Composition root wiring (mirrors iter-309/iter-312/iter-321 resolver injection pattern)
**Predecessor:** iter-343 (Hardpoint icon-resolution chain Phase 1 Approach A)
**Successor (queued):** iter-345 (live SWFOC verify OR pivot to Approach B if tostring returns userdata)

## What changed (1 file modified + 1 binary republished; ~10 LoC)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/ViewModels/MainViewModelV2.cs` (+~8 LoC):
  - Line 86: `Combat = new CombatTabViewModel(bridge, unitMutator)` → `Combat = new CombatTabViewModel(bridge, unitMutator, iconResolver)` (passes iter-309 injected `iconResolver`)
  - Line 449+: Added `Combat.SetIconResolver(newResolver)` to existing OnSettingsPropertyChanged hot-swap chain (mirrors iter-312 + iter-321 pattern; **6th consumer** in the chain)
- **REPUBLISH** `SWFOC editor/publish/SwfocTrainer.App.exe`: 157.34 MB at May 7 08:09 (incremental from iter-343 republish at 08:05).

## Verification gates ALL GREEN

- Editor build: GREEN (no compile errors)
- Editor binary republished: 157.34 MB at May 7 08:09
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries

## iter-337 preflight rule application — 6th consumer

**Step 1 — Rationale-grep**: iter-309/iter-312/iter-321 documented the resolver-injection-at-composition-root pattern. **PASS** (5+ pattern instances).

**Step 2 — Bridge-source-grep**: N/A (composition root only).

**Step 3 — 4-step composition preflight**: trivial — pattern-precedented at iter-309/iter-312/iter-321/iter-317/iter-318/iter-319. **PASS** (composition-layer pattern stable).

**Decision tree application**: continue with original plan → ~10 LoC ship → republish. Outcome matched.

**6th consumer milestone**: iter-337 codified at 3-instance trigger; iter-344 is the 6th consumer (iter-338, iter-339, iter-341, iter-342, iter-343, iter-344). 6 consumers across 7-iter window = exceptional consumption rate.

## Operator-facing impact (END-TO-END flow now wired)

**Pre-iter-344**: iter-343 shipped the icon-resolution chain in `CombatTabViewModel` ctor with `(UnitIconResolver? iconResolver = null)` param BUT MainViewModelV2 didn't pass the resolver. Operator clicking Refresh in Hardpoint Inspector got `IconPath = null` for all rows even if tostring(handle) would have returned valid name strings.

**Post-iter-344**: MainViewModelV2 passes the same `iconResolver` instance to CombatTabViewModel that's already wired to Spawning/Galactic/HeroLab/PlayerState/AssetBrowser tabs (iter-309/312/321). Hot-swap on Settings.IconsRoot change propagates to Combat tab too.

**End-to-end workflow operator now sees**:
1. Operator launches editor (`publish/SwfocTrainer.App.exe` 157.34 MB)
2. Operator opens Settings tab → "Unit icons" GroupBox → Browse... → picks `C:/swfoc_extracted_dds/`
3. Operator opens Combat tab → scrolls to Hardpoint Inspector GroupBox
4. Operator types/pastes unit `obj_addr` (hex) → clicks Refresh
5. **If tostring(GameObjectType_handle) returns NAME**: ListBox shows hardpoint icons next to each row → mid-battle weapon-type visual identification
6. **If tostring returns "userdata: 0x..."**: ListBox shows text-only rows (graceful fallback) → iter-345 pivots to Approach B

**Live SWFOC session needed** to know which path operator gets.

## Resolver-injection chain at end-of-arc

| Tab | iter | Consumer pattern |
|-----|------|------------------|
| Spawning | iter-308 | Inject in ctor; SetIconResolver hot-swap |
| PlayerState | iter-319 | Inject in ctor; SetIconResolver hot-swap |
| Galactic | iter-317 | Inject in ctor; SetIconResolver hot-swap |
| HeroLab | iter-318 | Inject in ctor; SetIconResolver hot-swap |
| AssetBrowser | iter-321 | Inject in ctor; SetIconResolver hot-swap |
| **Combat** | **iter-344** | **Inject in ctor; SetIconResolver hot-swap (6th consumer)** |

All 6 tabs share the same `UnitIconResolver` instance + hot-swap chain. iter-313 LocateByConvention plugin set fully consumed at the composition-root layer.

## Pattern lessons

### Recurrence — *resolver-injection-at-composition-root pattern* (6th instance; codification trigger reached)

iter-308 + iter-309 + iter-312 + iter-317 + iter-318 + iter-319 + iter-321 + **iter-344** = 8 instances of the resolver-injection-at-composition-root pattern. Pattern shape:

1. Inject `UnitIconResolver?` in tab VM ctor (per iter-311 codified `feedback_optional_default_null_constructor_extension`)
2. Add `SetIconResolver(UnitIconResolver?)` hot-swap method (mirror iter-312 pattern)
3. Wire MainViewModelV2 to pass + hot-swap

**Codification trigger reached at 8 instances** — well beyond the iter-302 6-instance precedent. Codification candidate `feedback_resolver_injection_at_composition_root.md` flagged at 8/3 (trivially over-trigger).

Future codification iter (iter-346+?) could capture this rule at very high evidence base. iter-345 priority is live verify; codification can wait.

### Pattern observation — *iter-337 preflight rule consumed at 6 instances in 7-iter window*

iter-338, iter-339, iter-341, iter-342, iter-343, iter-344 = 6 consumers. Average consumption rate: ~0.86 per iter. **iter-337 is the highest-utilization codified rule in the project**. Validates the iter-337 lesson that "codifying at higher abstraction layers compounds value faster" empirically — iter-strategy preflight rule applies to nearly every iter that involves feature work.

## What's NOT done in iter-344 (deferred)

- **Live SWFOC verify** of `tostring(GameObjectType_handle)` semantics: requires operator session
- **iter-345 contingency** (Approach B if tostring returns userdata): pending empirical evidence
- **Pin tests for MainViewModelV2 → Combat resolver wiring**: deferred — composition root wiring is hard to unit-test without spinning up the full ViewModel graph; existing iter-343 tests cover the contract
- **Operator changelog supplement** for iter-340-344 arc: ~10 iters away from canonical changelog cadence; defer to iter-350+

## Verification checklist

- [x] MainViewModelV2 line 86 passes iconResolver to CombatTabViewModel ctor
- [x] MainViewModelV2 line 449+ adds Combat.SetIconResolver to hot-swap chain
- [x] Editor build GREEN
- [x] Editor binary republished: 157.34 MB at May 7 08:09
- [x] iter-337 preflight rule consumed for 6th time
- [x] resolver-injection-at-composition-root pattern at 8/3 trigger (codification candidate flagged)
- [ ] Live SWFOC verify — deferred to operator session
- [ ] iter-345 contingency — pending empirical evidence
- [ ] Pin tests for composition root wiring — deferred (hard to unit-test)

## Net iter-344 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | ~8 LoC (composition root wiring) |
| Files modified | 1 (MainViewModelV2.cs) |
| Files new | 1 (this close-out doc) |
| Editor binary | 157.34 MB at May 7 08:09 |
| iter-337 preflight consumers | 6 (this iter is 6th) |
| Pattern observations flagged | 1 (`feedback_resolver_injection_at_composition_root.md` at 8/3 trigger) |
| Cycle time | ~10 min (smallest iter this conversation) |

**iter-344 closes the iter-343 deferred composition root wiring**: operator now has end-to-end Hardpoint Inspector with optimistic icon resolution. Live verify in operator session reveals whether Approach A's tostring assumption holds.

## Iter-345 decision tree

| Outcome | iter-345 pivot |
|---------|----------------|
| Operator session: icons render correctly | **Close mandate** at per-hardpoint scope; pivot to other arcs (e.g., README capstone, Phase B icon class extension) |
| Operator session: icons stay null (tostring returned userdata) | **Pivot to Approach B**: NEW Lua_GetUnitTypeNameLua bridge wire (~50 LoC C++ + ~30 LoC simulator + ~30 LoC catalog + ~30 LoC test) |
| Operator session: SWFOC_GetTypeLua errors on hardpoint child addrs | **Pivot to Approach C**: engine-side investigation (multi-iter; hardpoint child addresses may not be valid GameObjectClass instances) |
| No operator session yet | iter-345 ships an alternative iter (e.g., codify resolver-injection rule at 8/3 trigger; ship operator changelog supplement; etc.) |
