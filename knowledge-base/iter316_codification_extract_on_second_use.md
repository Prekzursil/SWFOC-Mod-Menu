# iter-316 — Codify `feedback_extract_on_second_use.md` memory rule (3-instance trigger)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Thread D arc, post-finale closeout 6 of N) + 3rd memory codification iter this conversation thread
**Predecessor:** iter-315 (Planet icon extension; 4th asset class)
**Successor (queued):** iter-317 (UI consumer ship — HeroLab portrait OR PlayerState faction emblem OR Galactic planet icon column)

## What changed (1 memory file new + 1 MEMORY.md index entry; ~80 lines; pure docs iter — 0 code/test changes)

- **NEW** `~/.claude/projects/C--Users-Prekzursil-Downloads-swfoc-memory/memory/feedback_extract_on_second_use.md` (~75 lines, YAML frontmatter + body) — codifies the meta-pattern with 3-instance trigger reached at iter-315:
  - Pattern shape: write logic INLINE at first concrete use; extract a helper ONLY when a SECOND consumer needs the same logic
  - Why: at first-use, the right shape isn't visible; at second-use, the abstraction can be designed around 2 real call sites instead of 1 + speculation
  - 5 how-to-apply rules (inline at 1st, copy at 2nd, extract from comparison, helper signature falls out, doc references both initial sites)
  - 5 edge cases (DRY-overzealous teams, slight-shape-difference catch, trivial-helper threshold, no pre-design, cross-file higher cost)
  - Cost-benefit table with concrete LoC numbers from iter-313→iter-315 case study
  - Cross-link to `feedback_engine_already_does_this.md` + `feedback_optional_default_null_constructor_extension.md` (the "delay commitment" trio)

- **EXTENDED** `~/.claude/projects/C--Users-Prekzursil-Downloads-swfoc-memory/memory/MEMORY.md` (+1 line) — index entry slotted directly after `feedback_status_badge_as_inline_docs` (semantic neighborhood: both are "delay commitment / wait for evidence" rules).

## 3-instance evidence

| Iter | Use | Pattern application |
|------|-----|---------------------|
| 308 | `LocateDds` 5-relpath walk | Shipped INLINE inside `UnitIconResolver.LocateDds`. 1 consumer = unit icons; no helper. |
| 313 | `LocatePortraitDds` needs same walk | Extracted `LocateByConvention(filenamePrefix, assetName)` helper. **Helper signature fell out from comparing 2 real call sites — only filename prefix differed.** |
| 314 | `LocateFactionEmblemDds` (3rd asset class) | Added with ZERO duplication via `LocateByConvention("i_faction_", factionName)`. ~30 LoC marginal. |
| 315 | `LocatePlanetIconDds` (4th asset class) | Same shape held — ~30 LoC marginal. **Abstraction validated at 4 plugins.** |

The pattern is now load-bearing: each new asset class costs ~3 LoC of plumbing because the 2nd-use extraction got the shape right.

## Pure docs iter — no code/test changes; all gates inherit GREEN

- Editor build inherits 0 errors from iter-315
- Combined Thread D + iter-313/314/315 inherits **102/102 PASS** (no regression possible — no code touched)
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- 111 → 111 buttons UNCHANGED

## Pattern lessons

### *Codification cadence: 4th memory codification iter this Thread D arc*

iter-311 codified 2 patterns simultaneously (`feedback_optional_default_null_constructor_extension.md` + `feedback_status_badge_as_inline_docs.md`). iter-316 codifies 1 pattern (`feedback_extract_on_second_use.md`). **3 codification iters across iter-311 + iter-316 — codification velocity is steady at ~1 codification per ~4 substantive iters.**

The "batch codification" pattern from iter-311 doesn't apply here because only 1 pattern reached strict 3-instance threshold this iter. Other 2 candidates (`feedback_mid_iter_pivot_on_scope_unclarity.md` + `feedback_distinct_count_n_coexistence.md`) stay at 2nd and 1st instance respectively — wait for 3rd recurrence per discipline.

### *Codifying at exactly 3rd instance is the disciplined sweet spot*

- 2 instances: pattern might be coincidence; codifying speculatively bakes in the wrong shape
- 3 instances: pattern is real; you have 3 concrete cases informing the rule wording
- 4+ instances: codification is overdue; risk that the 4th, 5th uses had to be discovered ad-hoc instead of guided by a written rule

iter-316 hits the sweet spot: 3 instances (iter-308 inline + iter-313 extract + iter-314/315 confirms shape held) and the rule writing has 4 concrete LoC numbers to anchor the cost-benefit table.

### *The "delay commitment" trio is now load-bearing*

After iter-316:
- `feedback_engine_already_does_this.md` (iter-302) — don't write what already exists in the engine
- `feedback_optional_default_null_constructor_extension.md` (iter-311) — defer dependency wiring to composition root in a separate iter
- `feedback_extract_on_second_use.md` (iter-316) — don't abstract until the 2nd consumer informs the shape

All 3 share the philosophy "delay commitment until you have evidence." Together they form a defensive design discipline that prevents the 3 most common over-engineering mistakes (rewriting existing infra, premature dependency injection, premature abstraction).

## What's intentionally NOT codified at iter-316 (deferred)

- **`feedback_mid_iter_pivot_on_scope_unclarity.md`** — at 2/3 instances (iter-314 HeroLab → faction emblems + iter-315 HeroLab → planet icons). Both pivots followed the same trigger but only 2 cases. Wait for 3rd recurrence (likely a near-future iter where another scope-unclarity surface forces a pivot).
- **`feedback_distinct_count_n_coexistence.md`** — at 1/3 instances (iter-315 introduced the form). Needs 2 more recurrences for codification.
- **`feedback_n_n_minus_1_discriminator_pin_matrix.md`** — implicit pattern from iter-313/iter-314/iter-315 prefix-discriminator pin matrices, but specific to asset-class extension; codification at 4th asset class.
- **`feedback_default_arg_pin.md`** — implicit pattern from iter-313/iter-314/iter-315 default-arg pin tests, but again asset-class-specific.

These all stay flagged in their respective close-out docs. iter-317+ may trigger one or more.

## Verification checklist

- [x] `feedback_extract_on_second_use.md` written with 3-instance evidence + cost-benefit + edge cases + cross-links
- [x] MEMORY.md index entry added in semantic neighborhood (after `feedback_status_badge_as_inline_docs`)
- [x] Pure docs iter — 0 code/test changes; all gates inherit GREEN
- [x] Cross-link to "delay commitment" trio of memory rules documented
- [ ] State docs synced
- [ ] Task #567 marked completed; iter-317 (UI consumer ship) queued
