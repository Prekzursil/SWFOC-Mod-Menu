# iter-326 — SWFOC_GetPlanetTechAndBuildings resolution: ORPHAN BRIDGE WIRE (DEPRECATE candidate)

**Date:** 2026-05-07
**Predecessor:** iter-325 (SWFOC_ListHeroes non-drift verified — 3 engine-surface gaps)
**Successor (queued):** iter-327 (SWFOC_SpawnUnit DEPRECATE-or-LIVE — likely DEPRECATE per iter-109/152/185 coverage)

## Verification finding

Investigation surfaced an **orphan bridge wire**:

| Layer | State |
|-------|-------|
| Bridge (`lua_bridge.cpp:6046`) | `Lua_GetPlanetTechAndBuildings` exists; returns `""` sentinel |
| Catalog (`CapabilityStatusCatalog.cs:448`) | Phase2HookPending with rationale "pending galactic state API" |
| C# dispatcher | **ZERO callers** — `grep -rn "GetPlanetTechAndBuildings\|GetPlanetTechAsync"` returns only the catalog entry itself |
| Operator workflow coverage | **Subsumed by iter-296 SWFOC_GetPlanets** — that wire already returns `name;faction;tech` rows with per-planet tech embedded |

This is **direction-A drift** per `feedback_infra_claim_drift_bidirectional.md` (iter-283 codified): catalog claims a feature exists; in practice it's an orphan with no callers and the operator value is already shipped via iter-296.

## Why it's an orphan, not a defer

iter-323 audit flagged this as "REVIEW — could be LIVE via iter-296 + iter-169 composition". Investigation reveals:

1. **No C# code calls `SWFOC_GetPlanetTechAndBuildings`**. The audit assumed there was a consumer; there isn't.
2. **iter-296 GetPlanets already returns tech**: the `name;faction;tech` row format from `Lua_GetPlanets` covers the per-planet tech use case end-to-end.
3. **Buildings enumeration**: would need `Find_All_Objects_Of_Type("Building")` filtered by parent-planet handle. **No documented Lua API for parent-planet filtering.** Would require either:
   - New engine surface (parent-handle-as-arg filter)
   - Iterating ALL buildings + reading parent via `:Get_Parent_Object()` then matching against planet handle (O(N×M) cost, fragile)

iter-296 GetPlanets implementation chose to ship tech-only because buildings aggregation is the harder gap.

## Decision: pure-verification iter (3rd in a row)

Following iter-324 + iter-325 precedent: ship close-out doc; **do not** flip catalog or remove orphan bridge wire in this iter. The catalog rationale extension + bridge wire deprecation queue for **iter-329 docs cleanup** which will address all 4 drift-resolution iters' findings together.

Rationale for not removing the orphan now:
- Removing the bridge wire is a contract change — must coordinate with any future consumers + simulator handlers + tests
- Catalog DEPRECATE flag would be premature without first checking iter-282 direction-B drift (does the orphan have any external Lua callers via DoString? — unlikely but should verify via grep)
- iter-329 batch cleanup is cheaper than 1-iter-per-orphan removal

## Catalog rationale extension (queued for iter-329)

Current:
```
SWFOC_GetPlanetTechAndBuildings — Phase 1 mirror — pending galactic state API
```

Proposed (iter-329):
```
SWFOC_GetPlanetTechAndBuildings — DEPRECATED ORPHAN —
  Bridge wire registered at lua_bridge.cpp:6046 but ZERO C# consumers as of iter-326.
  Operator value subsumed by iter-296 SWFOC_GetPlanets which embeds per-planet tech in
  row format (`name;faction;tech`). Buildings enumeration genuinely deferred — no engine-
  side parent-planet filter for Find_All_Objects_Of_Type("Building"). Catalog status stays
  Phase2HookPending solely because the bridge wire still exists; consider removing the
  orphan in iter-330+ alongside any other orphans surfaced by iter-272 reverse-orphan audit
  pattern.
```

## iter-323 audit re-correction (cumulative)

iter-324 + iter-325 + iter-326 corrections:
- Drift-review candidates: **2** (was 5; FreezeCredits + ListHeroes + GetPlanetTechAndBuildings all confirmed-defer or orphan-deprecate)
- Confirmed defers: **22** (was 19 + 1 orphan)
- Drift rate: 2/24 = **8%** (was 21%)

Remaining 2-iter arc:
- iter-327: SWFOC_SpawnUnit DEPRECATE-or-LIVE-flip (covered by iter-109/152/185 — likely DEPRECATE; same orphan pattern as #21)
- iter-328: SWFOC_SetDamageMultiplier per-slot resolution (gap between iter-96 global + iter-154 per-unit; possibly genuine LIVE-flip candidate)

## Pattern lesson — orphan bridge wires can pass as drift candidates in audits

**NEW pattern observation (1st instance; codification candidate at 3rd recurrence):** the iter-272 reverse-orphan audit pattern (catalog → bridge mapping check) is asymmetric — it catches "catalog says X but bridge doesn't have it" but doesn't catch "bridge has X but no C# code calls it". P2HP audits inherit the same gap: a Phase2HookPending entry might be a true defer OR an orphan with no consumers.

iter-326 surfaces the orphan-detection complement: **a P2HP entry is only a real defer if (a) the bridge wire exists AND (b) at least one C# dispatcher calls it**. Either side missing = not a real defer.

Codification candidate `feedback_orphan_bridge_wire_in_p2hp_audit.md` flagged at 1st instance.

## Verification gates

- Editor build inherits GREEN
- Bridge harness inherits 1100/0 (Lua_GetPlanetTechAndBuildings is registered but never called from harness either — no test coverage)
- Verifier ledger lint inherits 0/0 at 318 entries
- iter-221 / iter-274 / iter-323 P2HP count tests still expect 24 (no flip happened) — no test changes needed

## Honest scope discipline

3 consecutive pure-verification iters (iter-324 + iter-325 + iter-326). All 3 surfaced different non-op outcomes:
- iter-324: catalog rationale ALREADY documented the LIVE alternative
- iter-325: 3 engine-surface gaps (parser format + addr extraction + iter-130 deferred RVA)
- iter-326: orphan bridge wire (registered + cataloged but ZERO C# consumers)

The iter-323 audit format (REVIEW-flag → per-iter investigation) keeps producing load-bearing findings even when the answer is "no flip needed". 0 catalog flips across 3 iters but 3 different pattern lessons surfaced + 3 catalog rationale extensions queued for iter-329.

## Cumulative pattern observations from iter-324/325/326 trio

3 NEW observations in 3 iters, each codification-candidate at 3rd recurrence:
1. iter-324: `feedback_catalog_rationale_cross_references_obviate.md`
2. iter-325: `feedback_audit_drift_candidates_have_deeper_gaps.md`
3. iter-326: `feedback_orphan_bridge_wire_in_p2hp_audit.md`

All 3 are "audit format finds a real question; investigation finds a different answer than the audit predicted". Together they suggest the iter-323 audit format should add a 4-step pre-flight check for each REVIEW candidate: (1) does catalog rationale already explain? (2) is there an engine-surface gap? (3) is the bridge wire an orphan? (4) is composition-via-existing-LIVE genuinely sufficient?

This 4-step shape is itself a meta-pattern; codification candidate `feedback_p2hp_audit_4_step_preflight.md` at 1st instance.
