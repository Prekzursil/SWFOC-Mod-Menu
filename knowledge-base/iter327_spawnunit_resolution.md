# iter-327 — SWFOC_SpawnUnit resolution: NON-DRIFT (catalog rationale already cross-references iter-109 LIVE)

**Date:** 2026-05-07
**Predecessor:** iter-326 (SWFOC_GetPlanetTechAndBuildings — orphan bridge wire)
**Successor (queued):** iter-328 (SWFOC_SetDamageMultiplier per-slot — last drift candidate)

## Verification finding

The iter-323 audit flagged SWFOC_SpawnUnit as a drift candidate because iter-109 + iter-152 + iter-185 shipped 3 LIVE spawn-variant wires. Investigation reveals the catalog rationale **already cross-references the LIVE alternative** (mirror of iter-324 FreezeCredits finding):

```cs
["SWFOC_SpawnUnit"] = new("SWFOC_SpawnUnit", CapabilityStatus.Phase2HookPending,
    "BLOCKED-NO-RVA — superseded by iter-109 SWFOC_SpawnUnitLua "
  + "(engine Spawn_Unit Lua API via DoString; 3-arg form (player, type, position)). "
  + "This entry stays PHASE 2 PENDING as a Phase-1 mirror legacy wire shape; "
  + "iter-266 audit caught the operator-trust drift (rationale didn't cite the LIVE alternative). "
  + "Operator should use the iter-109 SWFOC_SpawnUnitLua LIVE wire.")
```

**Conclusion**: catalog rationale already correct (iter-266 audit fix per the rationale text). Same shape as iter-324 FreezeCredits — legacy wire shape with documented LIVE alternative.

## 2nd instance of `feedback_catalog_rationale_cross_references_obviate` pattern

iter-324 surfaced the pattern at 1st instance; iter-327 is the 2nd. **One more recurrence triggers codification** of:

> When a Phase2HookPending catalog entry's rationale **explicitly names its LIVE alternative**, future P2HP audits should classify it as confirmed-defer (intentional legacy wire shape with documented migration path), not drift-candidate. Meta-version of `feedback_status_badge_as_inline_docs.md` (iter-311 codified) applied to the catalog rationale field instead of operator-facing UI badges.

Both iter-324 and iter-327 entries also share a meta-pattern: **iter-266 audit was the previous fix** that added the LIVE-alternative cross-reference to both rationales. iter-266 was a P2HP re-audit; the cross-reference pattern was applied prophylactically. iter-323 audit didn't catch the pattern because it was scanning catalog STATUS only, not rationale CONTENT.

## Pattern lesson — P2HP audits should grep rationale for "iter-N" cross-references

**NEW pattern observation (1st instance; codification candidate at 3rd recurrence):** P2HP audits should add a pre-flight grep step: for each Phase2HookPending entry, search the rationale for `iter-\d+ SWFOC_\w+(Lua|Global)` patterns indicating a documented LIVE alternative. If present, classify as confirmed-defer-with-migration-path before doing deeper investigation.

This would have skipped iter-324 + iter-327 entirely (both rationales explicitly cite their LIVE alternatives), saving 2 verification iters. iter-323 audit format upgrades to:

1. **Pre-flight grep** for "superseded by iter-N" / "use the iter-N" / "migration to iter-N" rationale tokens
2. If pre-flight matches → confirmed-defer-with-migration-path; skip deeper investigation
3. If no match → REVIEW flag for per-iter investigation
4. Per-iter investigation per the iter-326 4-step preflight (rationale already explains? engine-surface gap? bridge wire orphan? composition genuinely sufficient?)

Codification candidate `feedback_p2hp_audit_rationale_grep_preflight.md` flagged at 1st instance.

## iter-323 audit re-correction (cumulative)

iter-324 + iter-325 + iter-326 + iter-327 corrections:
- Drift-review candidates: **1** (was 5; FreezeCredits + ListHeroes + GetPlanetTechAndBuildings + SpawnUnit all confirmed-defer or orphan)
- Confirmed defers: **23** (was 19; +3 from non-op verifications + 1 orphan)
- Drift rate: 1/24 = **4%** (was 21%)

Remaining 1-iter arc:
- iter-328: SWFOC_SetDamageMultiplier per-slot (gap between iter-96 global + iter-154 per-unit)

## Pattern: iter-323 audit's "drift candidates" decompose into 4 categories

Cumulative breakdown of the 5 iter-323 candidates after iter-324/325/326/327 investigations:
1. **catalog-rationale-cross-references**: iter-324 FreezeCredits + iter-327 SpawnUnit (2 instances)
2. **engine-surface-gap-deeper-than-predicted**: iter-325 ListHeroes (1 instance)
3. **orphan-bridge-wire**: iter-326 GetPlanetTechAndBuildings (1 instance)
4. **genuine-LIVE-flip-candidate**: iter-328 SetDamageMultiplier per-slot (last; pending verification)

If iter-328 confirms category 4, the 4-category taxonomy holds. If iter-328 surfaces a 5th distinct shape, the iter-326 4-step preflight needs extension.

## Verification gates

- Editor build inherits GREEN
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- iter-221 / iter-274 / iter-323 P2HP count tests still expect 24 (no flip happened) — no test changes needed

## Honest scope discipline

**4 consecutive pure-verification iters** (iter-324 + iter-325 + iter-326 + iter-327). Net catalog flips: 0. Net pattern lessons surfaced: 5 (3 non-op variants + 1 meta + 1 audit-format upgrade). Net rationale extensions queued for iter-329: 4.

This is exactly what a periodic audit is supposed to do — **systematically discover the topology of "why entries aren't ready" without breaking anything**. The audit format itself is now load-bearing infrastructure.

## Cumulative pattern observations from iter-324/325/326/327 quartet

5 NEW observations across 4 iters:
1. iter-324: `feedback_catalog_rationale_cross_references_obviate.md` (now at 2/3)
2. iter-325: `feedback_audit_drift_candidates_have_deeper_gaps.md` (1/3)
3. iter-326: `feedback_orphan_bridge_wire_in_p2hp_audit.md` (1/3)
4. iter-326 META: `feedback_p2hp_audit_4_step_preflight.md` (1/3)
5. iter-327 NEW: `feedback_p2hp_audit_rationale_grep_preflight.md` (1/3)

The "delay commitment" trio of memory rules (iter-302 + iter-311 + iter-316) keeps proving itself: 4 iters of investigation, 0 catalog flips, 5 pattern lessons. The next iter-323-style audit gets 5 iters of free quality improvement just by applying these patterns at audit-time instead of investigation-time.
