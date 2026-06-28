# iter-334 — Codify `feedback_locate_by_convention_extensible.md` memory rule (6-instance trigger; mirrors iter-302 codification cadence)

**Date:** 2026-05-07
**Arc class:** Codification (mirrors iter-302/iter-256/iter-283 codification iters at 6-instance trigger)
**Predecessor:** iter-333 (Asset Browser tab category extension 4 → 6 + iter-321 prefix-overlap bug fix)
**Successor (queued):** iter-335 (Combat tab weapon-icon column UI consumer OR Lua Playground preset menu refresh)

## What changed (1 NEW memory file + 1 MEMORY.md index entry; ~150 LoC)

- **NEW** `~/.claude/projects/C--Users-Prekzursil-Downloads-swfoc-memory/memory/feedback_locate_by_convention_extensible.md` (~150 LoC):
  - Frontmatter: `name`, `description`, `type=feedback`, `originSessionId`
  - 11-section body following iter-302 codification template:
    - **Rule** (3-step adoption: 2 public methods + class doc + 11 pin tests + env-var collection)
    - **Why** (table of 6 instances iter-308/313/314/315/331/332 with marginal LoC validated)
    - **How to apply** (numbered steps with code snippet + 11-fact test template + filtered-test-suite command)
    - **Honest break-out clause** (4 cases when this rule does NOT apply: non-DDS, multi-file, dynamic, prefix-overlap)
    - **Edge cases worth flagging** (5 sub-rules: prefix-overlap from iter-333, default-size sharing, N-way scaling, hot-swap, Asset-Browser composability)
    - **Cost-benefit ratio** (~50 LoC source + ~225 LoC tests + ~30 min cycle vs ~65-135 min without rule = 2-4× faster after first 2 instances)
    - **Memory-write triggers** (6-instance precedent matches iter-302; instance #7+ should EXTEND not duplicate)
    - **Prospective uses** (4 candidate future asset classes: build-tabs, ranks, cooldowns, cursors)
    - **Pattern reinforcement** (cross-link to iter-333 consumer-layer companion patterns)
    - **Cross-link to related codified rules** (iter-302/iter-256/iter-283/iter-172)
- **EXTEND** `~/.claude/projects/.../memory/MEMORY.md` (+1 line):
  - New entry `[LocateByConvention Plugin Set Extension](feedback_locate_by_convention_extensible.md)` with 1-line description
  - Index now contains 33 entries (was 32)

## Verification gates ALL GREEN

- 0 source/test/catalog edits in `SWFOC editor/` — pure docs iter
- All editor build/test gates inherit GREEN from iter-333 (no edits this iter to invalidate)
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- New memory file linted via the frontmatter contract (name/description/type/originSessionId all present)
- MEMORY.md index entry under 200 lines (33 entries, well under truncation threshold)

## Codification trigger validation — 6 instances precedent

iter-302 codified `feedback_engine_already_does_this.md` at 6 instances (iter-100/107/179/296/299/300). iter-334 mirrors the same precedent at 6 instances (iter-308/313/314/315/331/332). The 6-instance threshold is chosen because:

1. **Pattern stability proof**: 5 instances could be coincidence; 6 instances across distinct contexts (5 distinct prefix conventions + 5 distinct default sizes spanning 32-96 + producer-side AND consumer-side validation via iter-321/333) demonstrate the abstraction holds across variance.
2. **Test-suite-scaling proof**: 11 facts × 6 instances = 66 facts at consistent ~2.2s suite runtime with no test regression — empirical proof the pattern doesn't degrade as N grows.
3. **Recall economy**: codified rules carry forward to future sessions; 6 instances is "enough" recurrence that future agents are likely to encounter another instance and benefit from the rule.

If a 7th instance surfaces, it should EXTEND the rule's table rather than create a new rule. If a 7th instance surfaces a NEW shape constraint (e.g., compound multi-file assets), document it under the rule's "Honest break-out clause" rather than creating a new rule.

## Pattern lessons surfaced

### Recurrence — *codification cadence at 6 instances* (3rd codification iter at this trigger)

iter-302 (engine-already-does-this) → iter-256 (aob-drift-across-binary-versions) → iter-283 (infra-claim-drift-bidirectional) → **iter-334 (locate-by-convention-extensible)**.

4 codification iters now precedent at the 6-instance trigger. Pattern itself is becoming meta-stable: every ~30-50 iters, an opportunity to codify a 6-instance pattern surfaces. This is consistent with the iter-323 5-iter drift-resolution arc's finding that "delay commitment until you have evidence" produces measurable downstream quality at the iter-strategy layer.

### Pattern lesson — *codification iters are cheap (~30 min) and produce compounding value*

iter-334 cycle-time: ~30 min (read iter-302 template + write 150-LoC memory file + add 1-line MEMORY.md entry + write close-out doc + sync state). Compare to:

- iter-329 (5 catalog rationale extensions): ~45 min cycle
- iter-330 (10-iter operator changelog supplement): ~30 min cycle
- iter-331 (5th asset class ResolveWeaponIcon shipped end-to-end): ~30 min cycle
- iter-332 (6th asset class ResolveAbilityIcon shipped end-to-end): ~25 min cycle (ride iter-331 mirror)
- iter-333 (Asset Browser 4 → 6 categories + bug fix + regression test): ~45 min cycle

Codification iters are at the bottom of the cycle-time spectrum AND compound forward to future sessions/agents. **Highest cost-benefit-ratio of any docs-class iter type.**

## What's NOT done in iter-334 (deferred)

- **`feedback_glob_walker_prefix_overlap_audit.md` codification** (iter-333 1/3 trigger): premature; need 2 more instances to validate
- **`feedback_consumer_extensibility_audit.md` codification** (iter-333 1/3 trigger): premature; need 2 more instances to validate
- **`feedback_codification_cadence_at_6_instances` meta-codification** (4-instance trigger now: iter-302/256/283/334): meta-rule about WHEN to codify; defer to 6-instance trigger of meta-trigger
- **Combat tab weapon-icon column UI consumer**: deferred to iter-335+ (needs iter-167 Get_Hardpoints engine API preflight discovery first)
- **Lua Playground preset menu refresh**: stale since iter-264 (29 iters ago); could be next docs iter

## Verification checklist

- [x] `feedback_locate_by_convention_extensible.md` shipped with 11-section iter-302-mirror format
- [x] Frontmatter contract complete (name/description/type=feedback/originSessionId)
- [x] 6-instance table validates pattern stability across iter-308/313/314/315/331/332
- [x] Honest break-out clause covers 4 NOT-applicable cases
- [x] Edge cases section covers 5 sub-rules (including iter-333 prefix-overlap finding)
- [x] Cost-benefit ratio quantified: ~50 LoC source + ~225 LoC tests + ~30 min cycle = 2-4× faster after first 2 instances
- [x] Cross-link to 4 related codified rules (iter-302/256/283/172)
- [x] MEMORY.md index entry added (33 entries; under 200-line truncation threshold)
- [x] All editor build/test gates inherit GREEN from iter-333 (no edits this iter)
- [ ] Live SWFOC verify — N/A (pure docs iter)

## Codified-rules tally (cumulative)

After iter-334, the SWFOC project has **9 codified `feedback_*.md` memory rules**:

1. `feedback_dotnet_test_hang_diagnosis.md` (iter-172) — toolchain
2. `feedback_aob_drift_across_binary_versions.md` (iter-256) — RE
3. `feedback_infra_claim_drift_bidirectional.md` (iter-283) — pre-design grep
4. `feedback_engine_already_does_this.md` (iter-302) — bridge wire prioritization
5. `feedback_optional_default_null_constructor_extension.md` (iter-311) — VM extension
6. `feedback_status_badge_as_inline_docs.md` (iter-311) — operator-trust UX
7. `feedback_extract_on_second_use.md` (iter-316) — abstraction discipline
8. `feedback_empirical_first_for_format_re.md` (iter-?) — Thread C savegame RE
9. **`feedback_locate_by_convention_extensible.md` (iter-334)** — asset class extension

9 rules across iter 100-334 (~234-iter window) = 1 codified rule per ~26 iters. Sustainable recurrence.
