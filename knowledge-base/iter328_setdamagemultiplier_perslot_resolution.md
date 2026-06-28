# iter-328 — SWFOC_SetDamageMultiplier per-slot resolution: ORPHAN BRIDGE WIRE WITH IN-SOURCE RATIONALE (NEW 5th shape)

**Date:** 2026-05-07
**Predecessor:** iter-327 (SWFOC_SpawnUnit non-drift verified — catalog rationale already cross-references iter-109 LIVE)
**Successor (queued):** iter-329 (5-iter docs cleanup batch — catalog rationale extensions for iter 324-328)

## Verification finding

The iter-323 audit's LAST drift candidate (predicted as "category 4 genuine-LIVE-flip" via iter-96 + iter-154 composition gap-fill) turned out to be a **NEW 5th shape** that NEITHER the iter-326 4-step preflight NOR the iter-327 rationale-grep preflight would have caught.

### Step 0 (iter-327 rationale-grep preflight): NEGATIVE

Catalog rationale at `CapabilityStatusCatalog.cs:138`:
```cs
["SWFOC_SetDamageMultiplier"] = new("SWFOC_SetDamageMultiplier", CapabilityStatus.Phase2HookPending,
    "Per-slot multiplier — needs higher-layer detours (attacker context not at Take_Damage)"),
```

Does NOT contain `iter-N SWFOC_*(Lua|Global)` tokens. Step 0 = NEGATIVE → proceed to iter-326 4-step preflight.

### iter-326 4-step preflight: ORPHAN BRIDGE WIRE (category 3, mirror of iter-326 GetPlanetTechAndBuildings)

| Step | Question | Finding |
|------|----------|---------|
| 1 | Catalog rationale already explains? | PARTIAL — explains the BLOCK ("attacker context not at Take_Damage") but doesn't cite the iter-96 LIVE alternative |
| 2 | Engine-surface gap? | YES — Take_Damage `Src` param 5 is name string for debug logging, NOT attacker GameObjectClass*; needs ~58 caller-site detours |
| 3 | Bridge wire orphan? | **YES — ZERO C# consumers** (`grep "SetDamageMultiplier(?!Global)"` in editor source returns no matches; only `SetDamageMultiplierGlobal` is consumed) |
| 4 | Genuine LIVE-flip via composition? | **NO** — iter-154 `Set_Damage_Modifier` scales damage RECEIVED by a unit, not damage DEALT. Per-slot ATTACKER scaling is genuinely deferred per bridge source rationale |

**Primary classification**: orphan bridge wire (mirror of iter-326 GetPlanetTechAndBuildings) → **category 3**. **2nd instance of category 3** (now at codification trigger if pattern recurs once more).

### NEW 5th shape: bridge-source-rationale-richer-than-catalog-rationale

`lua_bridge.cpp:6933-7007` contains **~70 lines of in-source architectural rationale** that the catalog rationale summarizes in **a single 11-word sentence**. The bridge source documents:

1. **Original RVA was WRONG (iter 93 finding)**: claimed Take_Damage_Outer @ 0x38A350 → actually a player-event-handler
2. **String anchor analysis (iter 94 finding)**: "Damage_Multiplier" anchors point to per-ability validators (LeechShieldsAbilityClass + 4 other subclasses), NOT a global damage path
3. **Chokepoint pinned (iter 94 CONSUME-SITE)**: Take_Damage @ RVA 0x3A9E30, 3-tool consensus via `verified_facts.json::rva_take_damage_function`, identified by xref-to-format-string on 0x140866400
4. **Detour decision (iter 95 ARCHITECTURAL FINDING)**: `Src` (param 5) is a name string used for printf-style debug logging; sampled callers all pass `Src=nullptr` and `a10=-1`; attacker identity is IMPLICIT in the call stack
5. **Implication (iter 95 split)**:
   - Global form (slot=-1) → IMPLEMENTABLE via single Take_Damage detour → became iter-96 SetDamageMultiplierGlobal
   - Per-slot form → NOT IMPLEMENTABLE at this layer; needs ~58 caller-site detours
6. **Capability-badge guidance (line 7005-7007)**: "emit SWFOC_SetDamageMultiplierGlobal as LIVE (new helper) while SWFOC_SetDamageMultiplier (the per-slot superset) stays MIXED with a note explaining the layered status"

**The catalog rationale is a 99% reduction of the bridge source rationale.** Anyone reading only the catalog ("needs higher-layer detours") misses the 70 lines of WHY.

## Decision: pure-verification iter (5th in a row)

Following iter-324/325/326/327 precedent: ship close-out doc; **do not** flip catalog or remove orphan bridge wire in this iter. The catalog rationale extension + bridge wire deprecation queue for **iter-329 docs cleanup batch** which will address all 5 drift-resolution iters' findings together.

## NEW pattern lesson — P2HP audits should grep BRIDGE SOURCE comments, not just catalog rationale

**NEW pattern observation (1st instance; codification candidate at 3rd recurrence):** when a Phase2HookPending entry has a thin catalog rationale (<20 words), the audit should ALSO grep the bridge source file for the entry's `SWFOC_*` name + adjacent comment block. If the bridge source contains substantial in-source rationale (>50 lines of `//` comments), classify as "bridge-source-rich-catalog-thin" and queue a catalog rationale extension iter to lift the bridge source rationale into the catalog.

This complements the iter-326 4-step preflight + iter-327 rationale-grep preflight. The new step would be:

> **Step -1 (iter-328 NEW preflight)**: For each Phase2HookPending entry, grep `lua_bridge.cpp` for the entry's `SWFOC_*` name. If the bridge source has >50 lines of `//` rationale comments adjacent to the registration, the catalog rationale is likely too thin. Queue a rationale extension iter regardless of category.

This would have caught iter-328's classification gap before investigation: 70 lines of bridge source rationale vs. 11-word catalog rationale = obvious mismatch.

Codification candidate `feedback_p2hp_audit_bridge_source_grep_preflight.md` flagged at 1st instance.

## iter-323 audit re-correction (FINAL)

iter-324 + iter-325 + iter-326 + iter-327 + iter-328 corrections (FINAL tally):
- **Drift-review candidates: 0** (was 5; ALL confirmed-defer or orphan)
- Confirmed defers: **24** (was 19; all 5 iter-323 candidates moved into the defer column)
- **Drift rate: 0/24 = 0%** (was 21%)
- **Net catalog flips across 5-iter quartet: 0**
- Net pattern lessons surfaced: **6** (3 non-op variants + 1 meta + 2 audit-format upgrades)
- Net rationale extensions queued for iter-329 docs cleanup: **5** (FreezeCredits + ListHeroes + GetPlanetTechAndBuildings + SpawnUnit + SetDamageMultiplier per-slot)

## Pattern: iter-323 audit's "drift candidates" 4-category taxonomy CORRECTED

The iter-326 4-category taxonomy predicted iter-328 as category 4 (genuine-LIVE-flip-candidate). **This prediction was WRONG.** The corrected taxonomy after all 5 iters:

1. **catalog-rationale-cross-references**: iter-324 FreezeCredits + iter-327 SpawnUnit (×2)
2. **engine-surface-gap-deeper-than-predicted**: iter-325 ListHeroes (×1)
3. **orphan-bridge-wire**: iter-326 GetPlanetTechAndBuildings + **iter-328 SetDamageMultiplier per-slot** (×2)
4. **genuine-LIVE-flip-candidate**: **NONE** (predicted iter-328 turned out to be category 3 + new 5th shape)
5. **NEW 5th shape — bridge-source-rationale-richer-than-catalog-rationale**: iter-328 (1st instance)

**META-FINDING**: iter-323 audit's drift candidates ALL turned out to be non-op resolutions. **Net catalog flips: 0** across the entire 5-iter drift-resolution arc. This validates the "delay commitment" trio of memory rules (iter-302 + iter-311 + iter-316): 5 iters of investigation, 0 catalog flips, 6 pattern lessons, 5 rationale extensions queued.

The iter-323 audit format itself is now **load-bearing infrastructure** that converts 5 minutes of audit-time into 5 iters of free quality improvement when applied at audit-time instead of investigation-time.

## Verification gates

- Editor build inherits GREEN
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- iter-221 / iter-274 / iter-323 P2HP count tests still expect 24 (no flip happened) — no test changes needed

## Honest scope discipline

**5 consecutive pure-verification iters** (iter-324 + iter-325 + iter-326 + iter-327 + iter-328). Net catalog flips: 0. Net pattern lessons: 6 (3 non-op variants + 1 meta + 2 audit-format upgrades). Net rationale extensions queued for iter-329: 5.

This is the iter-323 audit format doing its real work — **systematically discovering the topology of "why entries aren't ready" without breaking anything**. The next iter-323-style audit gets 6 iters of free quality improvement just by applying these patterns at audit-time:

1. iter-324 pattern: catalog rationale cross-references obviate investigation
2. iter-325 pattern: composition-via-existing-LIVE has deeper gaps than predicted
3. iter-326 pattern: orphan bridge wires (no C# consumers) masquerade as defers
4. iter-326 META: 4-step preflight (rationale → engine surface → orphan → composition)
5. iter-327 pattern: rationale-grep step 0 skips obvious confirmed-defers
6. **iter-328 NEW pattern: bridge-source-grep step -1 catches catalog-rationale-thin entries**

## Cumulative pattern observations from iter 324-328 quintet

6 NEW observations across 5 iters, each codification-candidate at 3rd recurrence:

1. iter-324: `feedback_catalog_rationale_cross_references_obviate.md` (now at **2/3** — 1 more recurrence triggers codification)
2. iter-325: `feedback_audit_drift_candidates_have_deeper_gaps.md` (1/3)
3. iter-326: `feedback_orphan_bridge_wire_in_p2hp_audit.md` (now at **2/3** — iter-328 = 2nd instance — 1 more recurrence triggers codification)
4. iter-326 META: `feedback_p2hp_audit_4_step_preflight.md` (1/3)
5. iter-327 NEW: `feedback_p2hp_audit_rationale_grep_preflight.md` (1/3)
6. **iter-328 NEW: `feedback_p2hp_audit_bridge_source_grep_preflight.md` (1/3)**

Two patterns are now at **2/3 codification trigger**:
- catalog-rationale-cross-references (iter-324 + iter-327)
- orphan-bridge-wire (iter-326 + iter-328)

If the next P2HP audit hits either pattern, codification iter follows. The pattern-detection cadence is improving: it took iter-296→iter-308 (12 iters) to surface the iter-311 status-badge-as-inline-docs codification; the iter-323 arc surfaced 2 patterns at 2/3 in 5 iters.

## Cross-link to iter-302 + iter-311 + iter-316 trio

This iter is the strongest single instance of the **delay-commitment trio**:

- **iter-302 (engine-already-does-this)**: don't write code that already exists in the engine
- **iter-311 (status-badge-as-inline-docs)**: don't add operator-facing docs that the badge could surface
- **iter-316 (extract-on-second-use)**: don't extract abstractions until 2nd use validates the shape
- **iter-328 (this iter)**: **don't flip catalog status that's already correctly explained — even if the explanation is in the bridge SOURCE, not the catalog**

All 4 are "delay commitment until you have evidence". The iter-323 arc validates that systematic audit + investigation + 5 iters of pure-verification close-outs produces MORE long-term quality than a single iter of "flip everything that looks wrong".

## Iter-329 docs cleanup batch scope

Queued for iter-329 (single iter; pure docs):

1. **FreezeCredits** (iter-324): catalog rationale already correct; verify no further changes needed
2. **ListHeroes** (iter-325): extend catalog rationale to cite the 3 gaps (parser format + Lua handle → addr extraction + iter-130 deferred RVA)
3. **GetPlanetTechAndBuildings** (iter-326): mark catalog as DEPRECATED ORPHAN; cite iter-296 GetPlanets + iter-272 reverse-orphan audit
4. **SpawnUnit** (iter-327): catalog rationale already correct; verify no further changes needed
5. **SetDamageMultiplier per-slot** (iter-328): extend catalog rationale to lift bridge source rationale (cite iter-94 string-anchor analysis + iter-95 architectural finding + iter-96 split decision + ~58 caller-site detour gap)

Estimated scope: ~50-80 LoC catalog rationale extensions + 0 source/test changes + 0 catalog status flips.
