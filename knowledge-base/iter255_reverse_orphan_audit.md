# Iter 255 — Reverse-Orphan Snapshot Audit

**Date:** 2026-05-07
**Predecessor:** iter-238 (caught 1 reverse-orphan +1 SWFOC_GetCameraPos newly unwired); iter-239 (dropped after wiring).
**Window audited:** iter 244-254 (11 iters since last reverse-orphan check).
**Result:** **CLEAN PASS** — 1/1 GREEN, no drift.

---

## What this audit checks

The `CapabilityCatalogReverseOrphanTests` test (`tests/SwfocTrainer.Tests/Diagnostics/CapabilityCatalogReverseOrphanTests.cs`) walks the editor source for regex-visible `return SWFOC_*` and `SWFOC_DispatchAsync("SWFOC_*"` call sites, then compares against the `CapabilityStatusCatalog.Entries` keys. Catalog entries with no regex-visible call site are flagged as "actually unwired."

The `KnownUnwiredEntries` HashSet tracks intentional cases (catalog entries shipped without call sites yet — e.g. a NEW catalog entry queued for future native UX surfacing). The test fires when:

- **Newly unwired** (added to catalog but no call site shipped yet): operator-trust drift; need to either ship the call site OR add to `KnownUnwiredEntries` with a follow-up tag.
- **No longer unwired** (call site shipped but `KnownUnwiredEntries` not updated): housekeeping drift; drop from the set.

---

## Iter 244-254 activity summary (no new catalog entries)

The 11 iters since iter-243's bridge LIVE branches:

| Iter | Activity | New catalog entries | Reverse-orphan impact |
|---|---|---|---|
| 244 | Simulator handler extension + 6 pin tests | 0 (extends iter-243 LIVE branches) | None |
| 245 | UnitStatEditor staging-UI verification + 6 pin tests | 0 (verifies iter-242 design) | None |
| 246 | A1.x SetUnitField extras live verify + close | 0 | None |
| 247 | Operator changelog (~440 lines) | 0 (docs) | None |
| 248 | A1.x SetUnitCapOverride RE kickoff | 0 (RE design only) | None |
| 249 | RE walk + correction + DEFERRED CONFIRMED + ledger DEPRECATION | 0 (catalog status unchanged for SWFOC_SetUnitCapOverride; ledger entry DEPRECATED but ledger ≠ catalog) | None |
| 250 | Phase2HookPending re-audit (3rd audit) | 0 (audit-only) | None |
| 251 | SWFOC_FreezeCredits rationale fix + new pin test | 0 (rationale only) | None |
| 252 | Lua Playground preset menu refresh (+12 presets) | 0 (preset menu adds Lua scripts but presets reference EXISTING wires) | None |
| 253 | Operator changelog (~430 lines) | 0 (docs) | None |
| 254 | README capstone update | 0 (docs) | None |

**Conclusion**: 11 iters of activity that strengthened the catalog-discipline framework, audit cycle, operator UX, and documentation — but **shipped zero new catalog entries**. Reverse-orphan check validates that catalog-rationale-only changes / docs iters / audits don't introduce wiring graph drift.

---

## Verification

```
$ powershell -File tools/run_editor_tests_v2.ps1 -Filter "FullyQualifiedName~CapabilityCatalogReverseOrphan" -NoBuild

A total of 1 test files matched the specified pattern.
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: < 1 ms
```

**Single test in the file** (`KnownUnwiredEntries_MatchesActualUnwiredSet`) — 1/1 GREEN.

The test asserts `actuallyUnwired.Count == KnownUnwiredEntries.Count`. Since iter-238 last established the snapshot (with iter-239 dropping SWFOC_GetCameraPos after wiring), no entries have been added to or removed from either side of the comparison.

---

## Pattern lessons

1. **Reverse-orphan is a wiring-graph invariant**: it's only affected by NEW catalog entries (which start unwired until a call site ships) or NEW call sites (which transition entries from unwired to wired). Catalog-rationale changes / docs iters / audits / preset menu refreshes don't move entries across the wired/unwired boundary.

2. **The 11-iter clean-pass window validates "polish without surface"** — iter-247 changelog used the phrase "audit + polish window strengthens framework, not surface." Iter-255 verifies this empirically: 11 iters of framework-strengthening activity produced zero wiring-graph drift.

3. **Reverse-orphan check is the cheapest audit** (single test, < 1 ms execution time). Run it whenever a NEW catalog entry ships without same-iter call site addition. The 11-iter cadence between iter-238 and iter-255 is steady-state — each NEW catalog entry (none in this window) would have triggered an immediate same-iter check rather than waiting for a periodic audit.

4. **Catalog entries can have transient states**: NEW entry → KnownUnwiredEntries (with iter-N follow-up tag) → call site shipped → drop from KnownUnwiredEntries. Iter-238 caught this transition for SWFOC_GetCameraPos (Phase: added to KnownUnwiredEntries with iter-239-queued comment). Iter-239 closed the loop. **The KnownUnwiredEntries set is a queue, not a permanent placeholder.**

---

## What's next (iter 256+)

Pull from iter-253's "What's next" alternatives:
- **Option B** — A1.x SetUnitField max_* batch RTTI walk arc (iter-242 deferred 7 sub-fields including max_hull/max_shield/max_speed/attack_power/respawn_ms/is_hero/respawn_enabled). Estimated 4-5 iter arc.
- **Option E** (NEW) — codify `feedback_aob_drift_across_binary_versions` memory rule (iter-249 finding); single-iter operator-trust polish.
- **Option F** (NEW) — verify the 5 NEW pattern lessons from iter-253 changelog get propagated to memory via `feedback_*` files (4 closure modes / AOB drift / Catalog-rationale drift / discipline ROI / verification iters lock exclusions).

**Recommended: Option E** (memory rule codification) — single-iter, closes the loop on iter-249's NEW pattern lesson. Future RE that uses community CE tables will benefit from the documented verification step.

---

## Iter 255 close-out

- This document is the iter 255 deliverable.
- No bridge / dispatcher / VM / XAML / test changes. Pure verification iter + design doc.
- 109 → 109 buttons UNCHANGED. 102 → 102 preset entries UNCHANGED.
- Verifier ledger lint untouched (no new entries).
- Bridge harness 1100/0 unchanged.
- Editor focused tests pass: reverse-orphan 1/1 GREEN.

**Pattern lesson capstone — "Polish without surface" is testable**: iter-247 changelog asserted this; iter-255 verified it empirically via the reverse-orphan invariant. Future framework-strengthening windows can validate "no surface drift" via the same mechanism. **Wiring-graph invariants are operator-trust artifacts** — they pin the boundary between catalog and editor source code at a level where rationale changes, audits, and docs can't accidentally break the boundary.
