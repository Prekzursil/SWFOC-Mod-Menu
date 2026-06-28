# iter-324 — SWFOC_FreezeCredits resolution: NON-DRIFT (catalog state correct as-is)

**Date:** 2026-05-07
**Predecessor:** iter-323 (P2HP re-audit; flagged 5 drift candidates)
**Successor (queued):** iter-325 (SWFOC_ListHeroes resolution — next drift candidate from iter-323 audit)

## Verification finding

Read-only verification of the catalog state for `SWFOC_FreezeCredits`:

```cs
["SWFOC_FreezeCredits"] = new("SWFOC_FreezeCredits", CapabilityStatus.Phase2HookPending,
    "BLOCKED-NO-RVA — superseded by iter-231 SWFOC_SetCreditsFreezeGlobal "
  + "(Hook_AddCredits MinHook detour at 0x27F370 with bool-precedence; +4 LIVE flips iter 231 — "
  + "operator should use the LIVE alternative). This entry stays PHASE 2 PENDING as a Phase-1 "
  + "mirror legacy wire shape; iter-250 audit caught the operator-trust drift (rationale didn't "
  + "cite the LIVE alternative).")
```

**Conclusion**: catalog rationale is **already correct**:
- `SWFOC_FreezeCredits` (legacy wire shape) stays Phase2HookPending
- `SWFOC_SetCreditsFreezeGlobal` (iter-231 LIVE alternative) is correctly cataloged at `CapabilityStatus.Live`
- Catalog rationale on the legacy entry **explicitly cross-references** the LIVE alternative ("operator should use the LIVE alternative")
- iter-250 audit already caught and documented this distinction

## iter-324 = no-op (verification only)

No source/test/catalog changes required. The iter-323 audit's "drift candidate" flag was a **false positive** — the audit triage table correctly said "REVIEW" but the conclusion should have been "DEFER (catalog rationale already cross-references the LIVE alternative)".

## Pattern lesson — catalog rationale that cross-references LIVE alternatives obviates audit drift catches

**NEW pattern observation (1st instance; codification candidate at 3rd recurrence):** when a Phase2HookPending catalog entry's rationale **explicitly names its LIVE alternative**, future P2HP audits should classify it as confirmed-defer (intentional legacy wire shape with documented migration path), not drift-candidate. This is the meta-version of `feedback_status_badge_as_inline_docs.md` (iter-311 codified) applied to the catalog rationale field instead of operator-facing UI badges.

`SWFOC_FreezeCredits` is the only iter-323 candidate of this shape — the other 4 (#8/#15/#21/#22) are likely genuine drift candidates because their rationales pre-date the LIVE alternatives that obviate them.

## iter-323 audit correction

iter-323 drift-candidate count drops from 5 → **4** after this verification. Updated tally:
- Drift-review candidates: **4** (#8 SetDamageMultiplier per-slot / #15 SpawnUnit / #21 GetPlanetTechAndBuildings / #22 ListHeroes)
- Confirmed defers: **20** (was 19; FreezeCredits moves into the defer column with rationale-citation tag)
- Drift rate: 4/24 = **17%** (was 21%)

Remaining iter-325 → iter-328 follow-up arc:
- iter-325: SWFOC_ListHeroes resolution (composes with iter-179 Find_All_Objects_Of_Type)
- iter-326: SWFOC_GetPlanetTechAndBuildings resolution (composes with iter-296 GetPlanets + iter-169 Get_Tech_Level)
- iter-327: SWFOC_SpawnUnit DEPRECATE-or-LIVE-flip (covered by iter-109/152/185)
- iter-328: SWFOC_SetDamageMultiplier per-slot resolution (gap between iter-96 global + iter-154 per-unit)

## Verification gates

- Editor build inherits GREEN
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- iter-221 / iter-274 / iter-323 P2HP count tests still expect 24 (no flip happened) — no test changes needed

## Honest scope discipline

iter-324 stayed pure-verification because the catalog state was already correct. Forcing a code change to "complete" the iter would have been waste — the iter-323 audit's REVIEW-flagged candidates aren't all drifts; some surface that the catalog rationale was *already* doing the documentation work. The iter is honestly closed at "verified non-drift" with the false-positive insight captured for future audits.

## Pattern lesson cross-link

This iter is a concrete instance of the **iter-302 engine-already-does-this** rule applied at the catalog-rationale layer:
- iter-302: don't write code that already exists in the engine
- iter-324: don't flip catalog status that's already correctly explained in the rationale

Both are "delay commitment until you have evidence" — same philosophy, different artifact (engine source vs catalog rationale).
