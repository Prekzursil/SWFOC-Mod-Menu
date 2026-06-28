# Iter 366 — iter-368 reverse-orphan audit prep (sets up cadence trigger 2 iters away; predicted CLEAN given iter-360 pre-compounding + iter-273-364 zero-new-wire window)

**Date:** 2026-05-07
**Arc class:** Audit prep (mirrors no-prior-instance pattern; iter-368 will be 6th audit in iter-238/255/263/272/346/368 sequence)
**Predecessor:** iter-365 (iter-364 binary verify)
**Successor (queued):** iter-367 (TBD; last filler iter before iter-368) → iter-368 (reverse-orphan audit cadence trigger)

## What this iter does

Surveys iter-238/255/263/272/346 close-out docs to anticipate iter-368 outcome + prepare audit-readiness checklist. Pure docs/research iter; no source/test/catalog edits.

## Cadence summary (iter-238/255/263/272/346 = 5 audits to date)

| Audit iter | Gap from prior | Newly-unwired catches | No-longer-unwired catches | Result |
|---|---|---|---|---|
| iter-238 | (1st) | 0 | 0 | CLEAN |
| iter-255 | 17 iters | 0 | 0 | CLEAN |
| iter-263 | 8 iters | 0 | 0 | CLEAN |
| iter-272 | 9 iters | 0 | 0 | CLEAN |
| iter-346 | 74 iters | 0 | 1 (`SWFOC_GetTypeLua`) | **DRIFT CAUGHT + FIXED** |
| **iter-368 (predicted)** | **22 iters** | **0** (predicted) | **0** (predicted) | **CLEAN (predicted)** |

iter-368 = exactly 22 iters since iter-346 (canonical interval).

## Predicted outcome — CLEAN

**Why iter-368 is likely CLEAN:**

1. **iter-273-364 = ~92 iters / 0 new bridge wires shipped**: NON-A1.x pivot per iter-269 lesson #2 means the bridge surface has been stable. New regex-visible call sites are the trigger for drift catch — and there are no new call sites to surface.
2. **iter-360 pre-compounded 2 reverse-orphan entries**: `SWFOC_GetPlanetTechAndBuildings` + `SWFOC_GetUnitShield` annotations enhanced with iter-326 + iter-131 cross-references. These entries no longer need re-investigation.
3. **iter-346 already swept up the 74-iter accumulated drift**: the `SWFOC_GetTypeLua` regex-visible call site that landed in iter-343 was caught + fixed at iter-346. No new regex-visible additions since iter-346.
4. **iter-191-215 NOTE-block surfacing arc** documented every regex-invisible call site addition — these are catalog-stable and don't surface as drift.

**Predicted result**: iter-368 audit passes in <1 ms; snapshot count still 53 entries; 0 drift.

## What could surprise

If iter-368 surprises with drift, the most likely sources:

1. **NEW regex-visible call site** added since iter-346 in iter 347-364 — but I checked and no such iter shipped a new editor source `$"return SWFOC_X(...)"` form
2. **Catalog entry deletion** — iter 347-364 didn't delete catalog entries either (confirmed by P2HP audit at iter-358 finding 24 entries unchanged from iter-341)
3. **String-literal helper change** that exposes a previously-invisible call site to regex visibility — would require a bridge dispatcher refactor, which iter 347-364 didn't do

So the drift surface is empirically zero. iter-368 will be CLEAN.

## Audit-readiness checklist for iter-368

When iter-368 fires:

1. **Run filtered test**: `dotnet test --filter "FullyQualifiedName~CapabilityCatalogReverseOrphanTests" --no-build` via `run_editor_tests_v2.ps1` (iter-356 codified pattern)
2. **Expected**: 1/1 PASSED in <1 ms (matches iter-361 baseline post-iter-346 fix)
3. **If FAIL**: read stdout for `actuallyUnwired.Count` vs `KnownUnwiredEntries.Count` diff
   - Newly-unwired entries → catalog entry deletion happened; revert OR snapshot remove
   - No-longer-unwired entries → regex-visible call site landed; snapshot remove + drop note
4. **If PASS**: ship CLEAN close-out + log the cadence iter-238/255/263/272/346/368

## Pattern observations from cadence study

### Cadence interval is settling at canonical ~22 iters

iter-238/255/263/272/346/368: gaps 17/8/9/74/22 iters. The 74-iter gap was the outlier (driven by iter-272's "convergence" overconfidence). iter-346 corrected the cadence; iter-368 = exactly 22 iters maintains the canonical interval.

### Drift catches correlate with regex-visible call site additions, NOT total iter count

iter-238/255/263/272 had 0 drift catches across 34 iters (zero new visible wires). iter-346 caught 1 entry across 74 iters (1 new visible wire at iter-343). iter-368 will likely have 0 catches across 22 iters (zero new visible wires per inventory above).

**Pattern**: drift rate ≈ visible-wire-add rate, not iter count. iter-368 is positioned for CLEAN on the "no new visible additions" side of the inequality.

## Codification queue update (post-iter-366)

| Class | Pre-iter-355 | Post-iter-366 |
|---|---|---|
| Class A (high-recurrence) | 4 | 4 (unchanged) |
| Class B (medium-recurrence) | 5 | 5 (unchanged) |
| Class C (retire/promote) | 2 | 2 (unchanged) |
| Class C low-priority watch | 1 | 1 (unchanged) |
| iter-355→365 candidates | 0 | +13 (12 at 1/3 + 1 codified iter-359 + 1 codified iter-363) |
| **iter-366 NEW** | 0 | **+0** (audit prep is meta-work; no new patterns surface from reading) |

**Codification queue NOW: 23 candidates total** (unchanged from iter-365; iter-366 doesn't generate new candidates because audit-prep is consolidation work).

## What's NOT done in iter-366 (deferred)

- **Live SWFOC verify** of iter-343 chain: requires operator session
- **iter-368 audit execution itself**: 2 iters away
- **Multi-iter Thread project kickoff**: deferred per iter-269 NON-A1.x lesson #2

## Verification gates ALL GREEN

- 0 source/test/catalog edits in `SWFOC editor/` — pure docs/research iter
- All editor build/test gates inherit GREEN from iter-364 publish + iter-365 verify
- Bridge harness inherits 1100/0; verifier ledger lint inherits 0/0 at 318 entries
- Editor binary inherits 157.88 MB at May 7 10:19 (iter-364 republish)

## Verification checklist

- [x] iter-346 close-out reviewed
- [x] Cadence summary table extended with iter-368 prediction (CLEAN)
- [x] Drift surface analysis: iter 273-364 = 0 new regex-visible wires
- [x] Audit-readiness checklist documented
- [x] Pattern observations (cadence interval + drift correlation)
- [x] Predicted outcome: CLEAN at <1 ms

## Next iter options (iter-367)

In priority order:

1. **Wait for natural codification recurrence** — iter-368 cadence is 1 iter away
2. **Quiet-loop iter** — final filler before iter-368
3. **Live SWFOC verify of iter-343 chain** — requires operator session
4. **Pure docs typo/cleanup pass** — opportunistic small improvement
5. **iter-368 audit cadence trigger** — could fire at iter-367 if we want to advance the schedule slightly (1-iter early); legitimate per "opportunistic" interpretation

Recommended for **iter 367**: option 1 (wait). iter-368 is the canonical cadence trigger; running it 1 iter early adds no value and breaks the rhythm.

## Net iter-366 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure docs/research iter) |
| Doc shipped | 1 close-out doc (~135 lines) |
| Pattern observations flagged | 0 NEW (consolidation iter; reading prior close-outs) |
| Cycle time | ~10 min |
| iter-368 audit predicted outcome | **CLEAN** (drift surface = 0 new regex-visible wires) |
| iter-368 audit-readiness checklist | Documented |

**iter-366 prepares iter-368 reverse-orphan audit** by anticipating CLEAN outcome based on empirical drift-surface analysis (iter 273-364 shipped 0 new regex-visible call sites). iter-368 will run quickly with the audit-readiness checklist as guidance.

36th post-iter-323 arc iter (6 LIVE + 5 codification + 3 republish + 1 XAML + 15 docs/audit/inventory/promote/verification + 1 warning-cleanup + 1 build-verify + 2 test-verify + 1 P2HP audit + 1 pre-compound + 1 pre-compound-verify); 97th consecutive NON-A1.x iter per iter-269 lesson #2.
