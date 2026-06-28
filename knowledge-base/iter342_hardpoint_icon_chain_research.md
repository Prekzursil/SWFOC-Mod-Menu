# iter-342 — Hardpoint icon-resolution chain RESEARCH + design (4th consumer of iter-337 preflight rule; pivot to smaller scope per decision tree)

**Date:** 2026-05-07
**Arc class:** Research + design (foundation for iter-343 implementation)
**Predecessor:** iter-341 (P2HP audit clean)
**Successor (queued):** iter-343 (Hardpoint icon-resolution chain Phase 1: bridge wire OR optimistic chain in VM)

## Executive summary

iter-336 preflight identified the Hardpoint Inspector icon-resolution as a 2-bridge-call chain. iter-342 RESEARCH digs deeper:

| Aspect | Finding |
|--------|---------|
| Existing wire 1 | `SWFOC_GetHardpoints(unit_addr)` LIVE (iter-281) → returns `count=N child0=0x... hp0=...` |
| Existing wire 2 | `SWFOC_GetTypeLua(unit_addr)` LIVE (iter-169) → composes `(unit):Get_Type()` via DoString; returns "GameObjectType handle" |
| Existing wire 3 | `SWFOC_GetTypeOfUnitLua` LIVE (iter-174) → composes `(taskforce):Get_Type_Of_Unit(typeName)` returning count; **WRONG for hardpoint use case** |
| Resolver | `UnitIconResolver.ResolveWeaponIcon(weaponName, size = 32)` LIVE (iter-331) → expects `weaponName` STRING |
| **Critical unknown** | Does `tostring(GameObjectType_handle)` return the type NAME string (e.g. "TIE_Laser") or the userdata pointer (e.g. "userdata: 0x140012340")? |

**iter-337 preflight decision tree application**:
- **Step 1 rationale-grep**: iter-169 catalog says "GameObjectType handle" — ambiguous about tostring behavior
- **Step 2 bridge-source-grep**: `Lua_GetTypeOfUnitLua` confirmed at lua_bridge.cpp:4848 but it's WRONG receiver (TaskForce, not unit child); `SWFOC_GetTypeLua` at iter-167 uses generic unit-getter helper with `tostring(method())` codegen
- **Step 3 4-step composition preflight**: composition path uncertain; `tostring(handle)` semantics undocumented for SWFOC engine
- **Decision: pivot to smaller-scope RESEARCH iter** (per iter-337 decision tree row 3 — "preflight surfaces unforeseen complexity")

## Composition path analysis — 3 candidate approaches for iter-343

### Approach A: Optimistic chain (assume tostring returns name)

```
Operator obj_addr → SWFOC_GetHardpoints(addr) → list of child addresses
For each child_addr:
  → SWFOC_GetTypeLua(child_addr) → (assumed) returns weapon type name string
  → ResolveWeaponIcon(typeName) → DDS path → cached PNG
  → bind to ListBox ItemTemplate Image
```

**LoC estimate**: ~80 LoC VM extension + ~30 LoC XAML DataGridTemplateColumn + 8-10 pin tests

**Risk**: if `tostring(GameObjectType_handle)` returns "userdata: 0x..." instead of name, all icons show null/blank. Pivot to Approach B in iter-344.

### Approach B: Explicit name-extraction bridge wire (NEW LIVE wire)

```
NEW Lua_GetUnitTypeNameLua(L) {
    obj_addr = ...
    DoString: "return tostring((Find_Object_Addr(<addr>)):Get_Type():Get_Name())"
    Return name string
}
```

**LoC estimate**: ~50 LoC new bridge wire + ~30 LoC simulator handler + ~30 LoC catalog + ~80 LoC VM extension + ~30 LoC XAML + 12-15 pin tests

**Risk**: lower (explicit name extraction); higher LoC cost; needs bridge rebuild + DLL redeploy (operator workflow disruption)

### Approach C: Engine-already-does-this lookup via existing iter-296 GetPlanets style

```
Use SWFOC_GetUnitsAlive (iter-285) or SWFOC_GetFactionRoster (iter-299) which return name strings directly,
then cross-reference hardpoint child addresses against the roster. May not work if hardpoints aren't in the
unit roster (they're sub-objects of units, not standalone units).
```

**LoC estimate**: 0 (uses existing wires) + ~50 LoC VM cross-reference logic + ~30 LoC XAML + 6-8 pin tests

**Risk**: hardpoints may not be in unit-roster outputs (semantic mismatch); fallback needed

## Recommendation for iter-343

**Ship Approach A (optimistic chain)** as iter-343 implementation. Rationale:

1. **Cheapest path**: lowest LoC + uses 100% existing infrastructure
2. **iter-302 codified rule "engine-already-does-this"** applies: prefer existing engine API (SWFOC_GetTypeLua) over new wire
3. **Failure mode is graceful**: if tostring returns userdata, icons render as null (transparent), not as broken images
4. **Provides empirical evidence**: iter-343 close-out documents what tostring actually returns; informs iter-344 decision (refine vs pivot to Approach B)
5. **Live SWFOC verify needed regardless**: all 3 approaches require operator session to confirm; Approach A's failure mode is detectable without breaking the editor

**Decision tree at iter-344**:
- If Approach A works: ship Hardpoint icon column END-TO-END at minimal cost
- If Approach A returns userdata: pivot to Approach B (explicit name-extraction wire) at higher cost
- If Approach C is needed (semantic mismatch): pivot to engine-side investigation (multi-iter)

## Pattern lessons

### Recurrence — *iter-337 preflight rule consumed at 4 instances*

iter-338 (continue with original plan) → iter-339 (continue) → iter-341 (audit clean → pivot from follow-up arc) → **iter-342 (pivot to smaller-scope research)**.

**4th consumer of iter-337 preflight rule** validates the codification stability. The 4 outcomes span the full decision tree:
1. Continue with original plan (iter-338, iter-339)
2. Pivot from speculative work to higher-value adjacent (iter-336)
3. Pivot from follow-up arc to LIVE delivery (iter-341)
4. Pivot to smaller-scope research (iter-342)

The 5-pivot decision tree from iter-337 codification is now validated at 4/5 distinct shapes. If iter-343 pivots due to tostring(handle) returning userdata, the 5th pivot shape ("pivot to alternative approach mid-iter due to empirical findings") gets validated.

### NEW pattern — *RESEARCH-iter-followed-by-implementation-iter sub-pattern*

iter-336 RESEARCH (preflight pivot) → iter-338/339 implementation. iter-342 RESEARCH → iter-343 implementation. **2 instances of research-first 2-iter shape**.

This is a SUBSET of the iter-148→149 + iter-338→339 VM-first/XAML-second pattern. The sub-shape: when complexity is unknown, ship research-iter FIRST + implementation-iter SECOND. Codification candidate `feedback_research_first_implementation_second.md` flagged at 1/3 trigger.

## Verification gates ALL GREEN

- 0 source/test/catalog edits in `SWFOC editor/` — pure docs/research iter
- All editor build/test gates inherit GREEN from iter-339 republish
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries

## What's NOT done in iter-342 (deferred)

- **Implementation**: deferred to iter-343 per Approach A (optimistic chain)
- **Live SWFOC verify** of `tostring(GameObjectType_handle)` semantics: requires operator session
- **NEW bridge wire** if Approach A fails: deferred to iter-344+ contingency

## Verification checklist

- [x] `SWFOC_GetTypeLua` LIVE wire confirmed at iter-169 + catalog line 919
- [x] `SWFOC_GetTypeOfUnitLua` confirmed wrong-receiver (TaskForce, not unit child)
- [x] `tostring(GameObjectType_handle)` semantics flagged as UNKNOWN (needs empirical verification)
- [x] 3 candidate approaches analyzed with LoC estimates + risk profiles
- [x] Approach A (optimistic chain) recommended for iter-343
- [x] iter-337 preflight rule consumed for 4th time; decision tree validated at 4/5 shapes
- [x] NEW pattern observation: research-first + implementation-second sub-pattern at 2 instances
- [ ] iter-343 implementation — queued
- [ ] Live SWFOC verify — deferred

## Net iter-342 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 (research-only) |
| Doc shipped | 1 file (~120 lines) |
| Bridge wires investigated | 3 (SWFOC_GetHardpoints + SWFOC_GetTypeLua + SWFOC_GetTypeOfUnitLua) |
| Approaches analyzed | 3 (A/B/C with LoC estimates + risks) |
| Recommendation | Approach A for iter-343 |
| iter-337 preflight consumers | 4 (this iter is 4th) |
| Pattern observations flagged | 1 (`feedback_research_first_implementation_second.md` at 1/3) |
| Cycle time | ~25 min (research + design only) |

**iter-342 IS the foundation for iter-343**: empirical verification + Approach A implementation can ship in single-iter scope because the design work is complete.
