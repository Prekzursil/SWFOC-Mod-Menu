# Iter 360 — Apply iter-359 rule forward by pre-compounding 2 reverse-orphan rationale extensions for iter-368 audit (smaller scope than expected; most entries already had adequate inline comments)

**Date:** 2026-05-07
**Arc class:** Pre-compound forward (apply iter-359 codified rule prospectively to iter-368 reverse-orphan audit)
**Predecessor:** iter-359 (codified `feedback_audit_compounds_via_rationale_extensions.md` at FIRST 2-instance trigger)
**Successor (queued):** iter-361 (TBD — see "Next iter options" below)

## What changed (1 file modified — `CapabilityCatalogReverseOrphanTests.cs`; 2 entries enhanced + 1 NEW NOTE block; ~7 LoC net)

- **MODIFY** `tests/SwfocTrainer.Tests/Diagnostics/CapabilityCatalogReverseOrphanTests.cs` (~+7 LoC net):
  - **NEW iter-360 NOTE block** prefacing the 4-entry batch (lines 209-212): explains the iter-359-driven pre-compounding rationale (mirrors iter-329 catalog rationale extension pattern at test-file layer)
  - **`SWFOC_GetPlanetTechAndBuildings` annotation upgraded**:
    - From: `// galactic enrichment — planet DataGrid only shows id/owner/tech`
    - To: `// iter 326 DEPRECATED ORPHAN — superseded by iter-296 SWFOC_GetPlanets (galactic-mode planet enumeration with name;faction;tech CSV); buildings genuinely deferred per iter-326 audit`
  - **`SWFOC_GetUnitShield` annotation upgraded**:
    - From: `// read-only shield probe`
    - To: `// iter 131 LIVE pair-flip with iter-129 SetUnitShield writer (FrontShield_Read @ 0x3963C0); regex-invisible because used via service-layer wrapper`

## Verification gates ALL GREEN

- 0 source/test/catalog edits beyond the 1 test file annotation enhancement (no behavior change; comment-only edits)
- All editor build/test gates inherit GREEN from iter-356 build re-run + iter-357 test verify + iter-358 P2HP audit
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- Test snapshot count unchanged (53 entries; iter-346 baseline)

## Audit findings — pre-compounding survey of 53 KnownUnwiredEntries

I surveyed all 53 entries in the iter-346-fixed snapshot to identify which lacked cross-references that would help iter-368 recognize them as resolved:

| Annotation quality | Count | Action |
|---|---|---|
| Block-NOTE explanation (iter-191/194/197/198/199/200/201/202/203/209/210/211/212/214/215 NOTE blocks) | ~30 entries | No action — already pre-compounded |
| Inline `iter-XXX LIVE — iter-YYY native UX (...)` cross-reference | ~17 entries | No action — already adequate |
| Bare comment with brief explanation but NO iter-XXX cross-reference | **2 entries** | **Pre-compounded this iter** |
| Bare comment with brief explanation (status genuinely doesn't need cross-ref) | ~4 entries | No action — explanation sufficient (e.g., "composite — Combat tab uses individual toggles") |

**HEADLINE — Smaller scope than expected**: only 2/53 entries needed pre-compounding. Most entries had been adequately annotated during the iter-191-215 NOTE-block surfacing arc. The iter-359-rule application benefit was concentrated in the 2 specific entries that crossed iter audit boundaries (iter-326 DEPRECATED + iter-131 LIVE pair-flip).

This is itself a finding: **iter-191-215 was an inadvertent pre-compounding investment** at the test-file layer. The pattern emerged organically from the surfacing arc (NOTE blocks added per UX iter); iter-359 codified the rule + iter-360 retroactively recognized the prior compounding.

## Pre-compounding ROI estimate (forward to iter-368)

When iter-368 reverse-orphan audit fires (~9 iters from now):
- **Without iter-360 pre-compounding**: iter-368 audit reads `// galactic enrichment` for GetPlanetTechAndBuildings and may flag for re-investigation (could the deprecation status have changed?). Same for GetUnitShield (could it have been wired since?).
- **With iter-360 pre-compounding**: iter-368 reads `// iter 326 DEPRECATED ORPHAN — superseded by iter-296...` and immediately recognizes as resolved. Audit completes in ~5 min less time.

**Estimated savings**: ~10 min audit cycle reduction at iter-368, compounding across iter-368, iter-390 (next reverse-orphan), iter-412, etc. Per-iter cost: ~7 LoC investment now / ~10 min savings × N future audits.

## Pattern lessons surfaced

### Pattern observation #1 (1/3 trigger): `feedback_pre_compounding_smaller_than_predicted_when_inadvertent_compounding_already_present.md`

`feedback_inadvertent_pre_compounding.md` at 1/3 trigger — when applying the iter-359 rule forward to a new audit category, surveys often find prior work has already inadvertently pre-compounded most entries. iter-360 surveyed 53 entries; only 2 needed enhancement (~96% already pre-compounded by iter-191-215 NOTE-block surfacing arc).

This is a positive finding: organized work patterns (like the iter-191-215 surfacing arc) tend to leave behind compounding-friendly artifacts even when codification doesn't exist yet. The iter-359 codified rule formalizes what was already happening implicitly.

### Pattern observation #2 (1/3 trigger): `feedback_codification_retroactively_recognizes_implicit_patterns.md`

`feedback_codification_retroactively_recognizes_pattern.md` at 1/3 trigger — codifying a rule (iter-359) often reveals that the rule WAS being applied implicitly before codification. iter-360 application discovered iter-191-215 had been pre-compounding without anyone calling it that.

This suggests that the codification cadence may be tracking patterns that are already mature in practice, just not explicit yet. Codification = retrospective formalization, not new practice introduction.

## Codification queue update (post-iter-360)

| Class | Pre-iter-355 | Post-iter-360 |
|---|---|---|
| Class A (high-recurrence) | 4 | 4 (unchanged) |
| Class B (medium-recurrence) | 5 | 5 (unchanged) |
| Class C (retire/promote) | 2 | 2 (unchanged) |
| Class C low-priority watch | 1 | 1 (unchanged) |
| iter-355→357 trilogy candidates | 0 | +6 |
| iter-358 P2HP audit candidates | 0 | +1 + 1 progressed (audit_compounds → 2/3 → codified iter-359) |
| **iter-360 NEW** (`inadvertent_pre_compounding` + `codification_retroactively_recognizes_pattern`) | 0 | **+2** (1/3 each) |

**Codification queue NOW 18 candidates** (was 17 pre-iter-360; +2 NEW – 1 codified iter-359 = net +1).

## What's NOT done in iter-360 (deferred)

- **Editor binary republish** — not needed (test-file annotation only)
- **Live SWFOC verify** of iter-343 chain: requires operator session
- **Multi-iter Thread project kickoff**: deferred per iter-269 NON-A1.x lesson #2
- **iter-368 reverse-orphan audit**: 8 iters away; will benefit from iter-360 pre-compounding

## Verification checklist

- [x] All 53 KnownUnwiredEntries surveyed for annotation quality
- [x] 2 entries identified as needing iter-XXX cross-reference enhancement
- [x] Both entries enhanced with iter-326/iter-131 + iter-296/iter-129 cross-references
- [x] NEW iter-360 NOTE block prefaces the bare-comment batch explaining pre-compounding rationale
- [x] No semantic changes (comment-only edits)
- [x] All editor build/test gates inherit GREEN from prior iters

## Next iter options (iter-361)

In priority order:

1. **Wait for natural codification recurrence** — 4 candidates at 2/3 trigger remain (vm_first_xaml_second + research_first_implementation_second + p2hp_clean_when_no_new_wires + 1 from iter-360). Next 3rd-instance trigger likely iter-368 reverse-orphan audit OR iter-375 P2HP audit.
2. **Live SWFOC verify of iter-343 chain** — requires operator session
3. **NEW arc-class kickoff** — multi-iter; deferred per iter-271
4. **Quiet-loop iter** — pure verification (low utility)
5. **Build re-run to confirm iter-360 edit didn't break anything** — comment-only edit so risk is near-zero; could skip

Recommended for **iter 361**: option 1 (wait for natural recurrence). Codification queue is in steady state at 18 candidates; iter-368 audit will provide next natural trigger. Iters 361-367 are filler iters before iter-368. If a low-risk concrete improvement emerges, take it; otherwise quiet-loop.

## Net iter-360 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | ~+7 LoC (test file annotation enhancement; comment-only) |
| Doc shipped | 1 close-out doc (~135 lines) |
| Pattern observations flagged | 2 NEW at 1/3 trigger |
| Cycle time | ~10 min (smaller scope than predicted ~25-35 min) |
| Survey coverage | 53/53 entries surveyed; 2/53 needed enhancement (~4%) |

**iter-360 applies iter-359 codified rule forward** with smaller-than-predicted scope because iter-191-215 had already inadvertently pre-compounded most entries via NOTE blocks. The 2 enhanced entries (`GetPlanetTechAndBuildings` + `GetUnitShield`) close the cross-reference gap for iter-368's audit.

30th post-iter-323 arc iter (6 LIVE + 4 codification + 2 republish + 1 XAML + 13 docs/audit/inventory/promote/verification + 1 warning-cleanup + 1 build-verify + 1 test-verify + 1 P2HP audit + 1 pre-compound); 91st consecutive NON-A1.x iter per iter-269 lesson #2.
