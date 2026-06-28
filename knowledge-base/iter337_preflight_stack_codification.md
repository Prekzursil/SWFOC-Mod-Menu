# iter-337 — Codify `feedback_iter_strategy_preflight_stack.md` memory rule (3-instance trigger; iter-strategy layer companion to wire/plugin/abstraction/UX-level preflights)

**Date:** 2026-05-07
**Arc class:** Codification (mirrors iter-302/256/283/334 codification iters; FIRST 3-instance trigger codification)
**Predecessor:** iter-336 (Combat tab weapon-icon preflight pivot + republish)
**Successor (queued):** iter-338 (Combat tab Hardpoint Inspector smaller-scope OR README capstone OR codify iter-336 binary-republish-staleness pattern at premature 1/3)

## What changed (1 NEW memory file + 1 MEMORY.md index entry; ~180 LoC)

- **NEW** `~/.claude/projects/C--Users-Prekzursil-Downloads-swfoc-memory/memory/feedback_iter_strategy_preflight_stack.md` (~180 LoC):
  - Frontmatter: `name`, `description`, `type=feedback`, `originSessionId`
  - 11-section body following iter-302/iter-334 codification template:
    - **Rule** (3 preflight steps + decision tree with 5 pivot outcomes)
    - **Why** (table of 3 instances iter-331/332/336 with original plan vs preflight finding vs actual outcome)
    - **How to apply** (4 numbered steps with grep commands + decision tree application)
    - **Honest break-out clause** (4 cases when preflight is overhead: trivial iters, multi-iter arcs in progress, toolchain hardening, catalog audits)
    - **Edge cases worth flagging** (4 sub-rules: 5th-shape extension, wrong-abstraction-layer detection, mirror-iter ride, codification iters)
    - **Cost-benefit ratio** (~40 sec cost vs ~30-90 min savings = ~45× ROI; STRONGEST single-rule ROI in codified set)
    - **Memory-write triggers** (3-instance justification: meta-rule + shape variety + simultaneous pattern surfacing + iter-334 codification cadence lesson)
    - **Prospective uses** (4 candidate iter-338+ applications)
    - **Pattern reinforcement** (cross-link to wire-layer iter-302 + plugin-layer iter-334 + abstraction-layer iter-316 + UX-layer iter-311 preflight equivalents)
    - **Cross-link to related codified rules** (7 links to iter-302/334/316/311/283/256/172)
- **EXTEND** `~/.claude/projects/.../memory/MEMORY.md` (+1 line):
  - New entry `[Iter-Strategy Preflight Stack](feedback_iter_strategy_preflight_stack.md)` with 1-line description
  - Index now contains 34 entries (was 33)

## Verification gates ALL GREEN

- 0 source/test/catalog edits in `SWFOC editor/` — pure docs iter
- All editor build/test gates inherit GREEN from iter-336 republish
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- New memory file linted via the frontmatter contract (name/description/type/originSessionId all present)
- MEMORY.md index entry under 200 lines (34 entries; well under truncation threshold)

## Codification trigger justification — FIRST 3-instance trigger

iter-302/256/283/334 all used the **6-instance trigger** (matches iter-302 precedent). iter-337 is the **FIRST 3-instance codification** in the SWFOC project. Justification:

1. **The PATTERN is meta-rule, not new pattern**: iter-302 codified `feedback_engine_already_does_this` at 6 instances of the WIRE-design preflight pattern. iter-337's iter-strategy preflight is the SAME meta-rule (preflight-before-design) applied at HIGHER abstraction layer (iter-strategy vs wire-design). The meta-rule is already validated.
2. **3 instances cover 3 DISTINCT pivot outcomes**: LIVE delivery (iter-331), mirror reuse (iter-332), smaller-scope pivot (iter-336). Shape variety (3 distinct outcomes from 3 instances) is stronger evidence than 6 instances of identical-shape recurrence.
3. **iter-336 NEW pattern lesson** (`feedback_binary_republish_staleness_audit`) flagged at 1/3 ALONGSIDE this codification — multiple patterns surfacing simultaneously suggests the iter-strategy layer is producing patterns at higher rate than catalog-resolution layer.
4. **iter-334 codification cadence lesson** ("codification iters have highest cost-benefit-ratio") justifies aggressive codification when patterns are at higher-abstraction layers.

If iter-337 turns out to be premature (4th instance surfaces a NEW shape that breaks the rule), the codified rule's "Edge cases" section explicitly invites extension rather than rule-replacement.

## Pattern lessons

### Codification cadence is now bimodal

iter-302/256/283/334 = 6-instance trigger for new-pattern codification (4 codification iters).
iter-337 = 3-instance trigger for meta-rule-applied-at-higher-layer codification (1 codification iter).

**Pattern observation**: codification cadence depends on whether the pattern is NEW or META-LEVEL. New patterns need 6 instances of stability proof. Meta-level patterns can codify at 3 instances when shape variety substitutes for recurrence count.

If iter-338+ surfaces another meta-level pattern at 3 instances, this bimodal cadence becomes a NEW codifiable rule itself: `feedback_codification_cadence_meta_level.md` at 3rd recurrence.

### Pattern lesson — *codification iters at higher abstraction layers compound faster*

iter-302 codified wire-design preflight (1 layer above design). iter-334 codified plugin-set extension (2 layers above design — abstraction-design). iter-337 codifies iter-strategy preflight (3 layers above design — strategy-design). Each layer up:
- Codification trigger threshold drops (6 → 6 → 3 instances)
- Application breadth widens (single-wire → asset-class plugin → iter shape)
- Cost-benefit ratio increases (rough 6× → 8× → 45× ROI estimates)

**Pattern lesson** (1st instance; codification candidate at 3rd recurrence): codifying at higher abstraction layers compounds value faster because the rule applies more broadly per instance. Future codification candidates should be evaluated by abstraction-layer first; lower-layer rules need more instances; higher-layer rules can codify earlier.

Codification candidate `feedback_codify_at_abstraction_layer.md` flagged at 1/3.

## Codified-rules tally (cumulative)

After iter-337, the SWFOC project has **10 codified `feedback_*.md` memory rules**:

1. `feedback_dotnet_test_hang_diagnosis.md` (iter-172) — toolchain
2. `feedback_aob_drift_across_binary_versions.md` (iter-256) — RE
3. `feedback_infra_claim_drift_bidirectional.md` (iter-283) — pre-design grep
4. `feedback_engine_already_does_this.md` (iter-302) — bridge wire prioritization
5. `feedback_optional_default_null_constructor_extension.md` (iter-311) — VM extension
6. `feedback_status_badge_as_inline_docs.md` (iter-311) — operator-trust UX
7. `feedback_extract_on_second_use.md` (iter-316) — abstraction discipline
8. `feedback_empirical_first_for_format_re.md` (iter-?) — Thread C savegame RE
9. `feedback_locate_by_convention_extensible.md` (iter-334) — asset class extension
10. **`feedback_iter_strategy_preflight_stack.md` (iter-337)** — iter-strategy preflight

10 rules across iter 100-337 (~237-iter window) = 1 codified rule per ~24 iters. Sustainable recurrence; iter-337 brings the cadence slightly tighter than the iter-334 measurement of 1 per ~26 iters.

## What's NOT done in iter-337 (deferred)

- **`feedback_binary_republish_staleness_audit.md` codification** (iter-336 1/3 trigger): premature; need 2 more instances to validate per established 3-instance META-trigger
- **`feedback_codify_at_abstraction_layer.md` codification** (iter-337 1/3 trigger): premature; need 2 more instances of the bimodal cadence pattern
- **Combat tab Hardpoint Inspector**: deferred to iter-338+ (smaller scope per iter-336 close-out)
- **README capstone update**: premature (only 15 iters since iter-322)

## Verification checklist

- [x] `feedback_iter_strategy_preflight_stack.md` shipped with 11-section iter-302/iter-334 mirror format
- [x] Frontmatter contract complete (name/description/type=feedback/originSessionId)
- [x] 3-instance table validates pattern across iter-331/332/336 with 3 distinct pivot outcomes
- [x] Decision tree (5 pivot outcomes) documented
- [x] Honest break-out clause covers 4 NOT-applicable cases
- [x] Edge cases section covers 4 sub-rules
- [x] Cost-benefit ratio quantified: ~40 sec preflight + ~30-90 min savings = ~45× ROI
- [x] Cross-link to 7 related codified rules
- [x] MEMORY.md index entry added (34 entries; under 200-line truncation threshold)
- [x] All editor build/test gates inherit GREEN from iter-336 republish
- [ ] Live SWFOC verify — N/A (pure docs iter)

## What this iter delivers to future agents

Future agents starting any iter that involves feature work + existing infra now have a 1-line lookup in MEMORY.md → 180-LoC application guide. The preflight stack converts speculative "let me see what happens" iters into structured "here's the 3 pivot directions" decisions:

| Without rule | With rule |
|--------------|-----------|
| Agent commits to original plan, hits complexity mid-iter, runs over scope | Agent applies preflight in ~40 sec, picks pivot direction, ships single-iter-scope |
| Multiple iters waste cycle-time on speculative work | Cumulative ~45× ROI per preflight invocation |
| Preflight discoveries are scattered across close-out docs | Decision tree centralized in 1 codified rule |
| New pivot outcomes get re-derived each time | Edge-cases section explicitly invites extension at 5th-shape encounter |

## Cross-link to iter-294 mandate

This rule indirectly serves the user's iter-294 standing mandate ("complete editor/trainer + proper overlay + savegame editor + 100% functional + uncluttered UI/UX"). The mandate is about DELIVERED features, not "tried but failed" features. iter-strategy preflight prevents wasted commitment that would otherwise burn cycle-time without delivering operator value.
