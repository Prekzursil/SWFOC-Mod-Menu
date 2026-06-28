# iter-325 — SWFOC_ListHeroes resolution: NON-DRIFT (deeper engine surface gap than iter-323 audit predicted)

**Date:** 2026-05-07
**Predecessor:** iter-324 (SWFOC_FreezeCredits non-drift verified)
**Successor (queued):** iter-326 (SWFOC_GetPlanetTechAndBuildings resolution — next drift candidate)

## Verification finding

The iter-323 audit suggested ListHeroes could LIVE-flip via composing iter-179 `Find_All_Objects_Of_Type("Hero")` (mirror of iter-296 GetPlanets). Investigation reveals the gap is **deeper** than the audit predicted.

### Gap 1: 6-field row format requires engine addr

C# parser at `BridgeHeroLabDispatcher.ListHeroesAsync` expects 6 semicolon-separated fields per row:
```
<addr>;<typeName>;<owner>;<alive>;<respawnMs>;<respawnEnabled>
```

iter-296 GetPlanets shipped a 3-field format (`name;faction;tech`) where no addr was needed because planets are referenced by name in subsequent operator calls. **Heroes are referenced by engine pointer** (operator clicks a hero row → SWFOC_KillUnit(addr) / SWFOC_ReviveUnit(addr) / SWFOC_SetHeroRespawnTimer(addr, ms)). Without a real engine addr, the enumeration is display-only — operator can SEE the list but can't ACT on it.

### Gap 2: Lua handle → engine addr extraction not exposed

`Find_All_Objects_Of_Type("Hero")` returns Lua engine handles. Standard methods are `:Get_Type()`, `:Get_Owner()`, `:Get_Position()`, `:Get_Hull()`, etc. **No documented method to extract the underlying engine pointer (addr) from a Lua handle.**

`tostring(handle)` may return an address representation but format is undocumented and non-canonical — relying on it would be brittle.

### Gap 3: Respawn fields need iter-130 deferred RVA

`respawnMs` + `respawnEnabled` map to the per-hero respawn-timer table that **iter-130 confirmed defer** (table location not callgraph-discoverable; same gap that keeps SWFOC_SetHeroRespawnTimer Phase2HookPending).

Even if Gap 1 + Gap 2 were resolvable, the iter-130 gap blocks the last 2 fields.

## Conclusion: honest non-op (mirror of iter-324)

iter-323 audit's REVIEW-flag was correct to surface the question; iter-325 answers it as **DEFER (deeper gap than per-tab composition)**:

- **Gap 1** is solvable in principle (catalog could ship a 4-field format + parser update) but requires upstream contract change
- **Gap 2** is the binding constraint — no Lua → addr extraction
- **Gap 3** requires the iter-130 deferred RVA

The catalog rationale should be extended to cite iter-179 + iter-296 GetPlanets pattern + the 3 gaps so future audits don't re-flag this.

## Catalog rationale extension (recommended)

Current:
```
SWFOC_ListHeroes — Phase 1 mirror — needs hero detection table IDA-pin
```

Proposed (extension; iter-325 honest defer):
```
SWFOC_ListHeroes — Phase 1 mirror —
  iter-323 audit: REVIEW-flagged as drift candidate via iter-179 Find_All_Objects_Of_Type composition.
  iter-325 audit: confirmed DEFER. Even with Find_All_Objects_Of_Type("Hero"), 3 gaps prevent LIVE flip:
    (1) C# parser requires engine addr (6-field format); Lua handles don't expose addr extraction
    (2) iter-130 confirmed defer on per-hero respawn-timer table RVA
    (3) Operator workflow needs addr-clickable rows; display-only enumeration would be regression
  Stays Phase2HookPending until either: (a) hero detection table RVA pinned via callgraph, or
  (b) Lua handle → engine addr extraction surface added to engine bindings.
```

## Decision: skip the catalog edit too

Given conversation context length + iter-324 precedent (pure-verification iter, no source change), iter-325 ships as **pure-verification iter** with the analysis captured here. The catalog rationale extension is queued as part of iter-329 docs cleanup (covers all 4 drift-resolution iters' findings together).

## iter-323 audit re-correction

Cumulative iter-324 + iter-325 corrections to iter-323 audit:
- Drift-review candidates: **3** (was 5; FreezeCredits + ListHeroes both confirmed-defer)
- Confirmed defers: **21** (was 19)
- Drift rate: 3/24 = **12.5%** (was 21%)

Remaining 3-iter arc:
- iter-326: SWFOC_GetPlanetTechAndBuildings (most-likely-LIVE-via-composition; iter-296 GetPlanets shipped + iter-169 Get_Tech_Level shipped → composable as `(planet):Get_Tech_Level()` per-planet)
- iter-327: SWFOC_SpawnUnit DEPRECATE-or-LIVE-flip (covered by iter-109/152/185 — likely DEPRECATE)
- iter-328: SWFOC_SetDamageMultiplier per-slot resolution (gap between iter-96 global + iter-154 per-unit)

## Pattern lesson — audit drift candidates can have deeper gaps than predicted

**NEW pattern observation (1st instance; codification candidate at 3rd recurrence):** P2HP audit drift candidates surface "could this be LIVE now via composition?" — the answer often requires DEEPER investigation than the audit's surface-level lookup. iter-323 audit was right to flag ListHeroes (the iter-179 helper IS shipped); iter-325 investigation found 3 gaps that the audit didn't surface (parser format + addr extraction + per-hero respawn).

This is the inverse of iter-324's pattern: iter-324 found the catalog rationale ALREADY explained the deferral; iter-325 finds the audit was right that the question matters but the DEFER conclusion needs more than rationale-citation — it needs **engine-side surface that doesn't exist**.

Codification candidate `feedback_audit_drift_candidates_have_deeper_gaps.md` flagged at 1st instance.

## Verification gates

- Editor build inherits GREEN
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- iter-221 / iter-274 / iter-323 P2HP count tests still expect 24 (no flip happened) — no test changes needed

## Honest scope discipline (carries forward from iter-324)

Same shape as iter-324: pure-verification iter; deeper investigation than the audit predicted; catalog rationale extension queued for iter-329 docs cleanup. Two iters in a row of "audit was right to flag, investigation reveals DEFER stays" — useful confirmation that the iter-323 audit format (REVIEW-flag → per-iter investigation) is the right shape rather than auto-flipping.
