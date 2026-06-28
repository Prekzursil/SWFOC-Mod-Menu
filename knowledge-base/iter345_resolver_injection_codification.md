# iter-345 — Codify `feedback_resolver_injection_at_composition_root.md` memory rule (8-instance trigger; HIGHEST evidence base of any codified rule in the project)

**Date:** 2026-05-07
**Arc class:** Codification (mirrors iter-302/256/283/334/337 cadence; FIRST 8-instance trigger codification in the project)
**Predecessor:** iter-344 (MainViewModelV2 wire-up to pass iconResolver to CombatTabViewModel)
**Successor (queued):** iter-346 (TBD — see "Next iter options" below)

## What changed (1 NEW memory file + 1 MEMORY.md index entry; ~165 LoC)

- **NEW** `~/.claude/projects/C--Users-Prekzursil-Downloads-swfoc-memory/memory/feedback_resolver_injection_at_composition_root.md` (~165 LoC):
  - Frontmatter: `name`, `description`, `type=feedback`, `originSessionId`
  - 11-section body following iter-302/iter-334/iter-337 codification template:
    - **Rule** (3-step composition root injection pattern with code snippets)
    - **Why** (table of 8 instances iter-308/309/312/317/318/319/321/344 with hot-swap behavior column)
    - **How to apply** (4 numbered steps with LoC estimates)
    - **Hot-swap behavior choice** (Pattern A eager + Pattern B dormant decision tree)
    - **Honest break-out clause** (4 NOT-applicable cases: singleton, single-consumer, async-init, stateless)
    - **Edge cases worth flagging** (5 sub-rules: timing + threading + test-isolation + iter-321 prefix-overlap + composition root growth)
    - **Cost-benefit ratio** (~5-15 LoC source + ~2-4 tests + ~5-10 min cycle = 2-3× faster after first 2 instances)
    - **Memory-write triggers** (8-instance justification: highest evidence base; 2 hot-swap patterns)
    - **Prospective uses** (3 candidate future tab consumers: Inspector + WorldState + Director Mode)
    - **Pattern reinforcement** (cross-link to iter-311 + iter-302 + iter-316 + iter-334 + iter-337 codified rules)
    - **Cross-link to related codified rules** (7 links to iter-311/316/334/337/302/172)
- **EXTEND** `~/.claude/projects/.../memory/MEMORY.md` (+1 line):
  - New entry `[Resolver-Injection-At-Composition-Root](feedback_resolver_injection_at_composition_root.md)` with 1-line description
  - Index now contains 35 entries (was 34)

## Verification gates ALL GREEN

- 0 source/test/catalog edits in `SWFOC editor/` — pure docs iter
- All editor build/test gates inherit GREEN from iter-344 republish
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- New memory file linted via the frontmatter contract (name/description/type/originSessionId all present)
- MEMORY.md index entry under 200 lines (35 entries; well under truncation threshold)

## Codification trigger justification — FIRST 8-instance trigger codification in the project

iter-302 codified at 6 instances; iter-256/283 at 6 instances; iter-334 at 6 instances; iter-337 at 3 instances (meta-rule). iter-345 is the **FIRST 8-instance trigger codification** in the project. Justification:

1. **Highest evidence base in the project**: 8 instances across iter 308-344 (~36-iter window) prove the pattern stable across distinct tab shapes
2. **2 hot-swap behavior patterns surfaced**: Pattern A (eager re-resolution; iter-308/317/318/319/321) + Pattern B (dormant; iter-344). Variety of behavior shapes confirms the abstraction holds beyond mechanical recurrence
3. **iter-344 was the codification trigger**: 6th consumer in OnSettingsPropertyChanged hot-swap chain; pattern's stability is now empirically proven across all 6 icon-consumer tabs
4. **iter-337 codification cadence lesson**: codification at higher abstraction layers compounds value faster. iter-345's composition-root layer is HIGHER than iter-334's plugin layer (composition-root spans MULTIPLE plugin instances) → faster ROI

If a 9th tab needs a NEW hot-swap pattern (e.g. Pattern C async re-resolution), the rule's "Hot-swap behavior choice" section explicitly invites extension rather than rule-replacement.

## Pattern lessons

### Codification cadence acceleration confirmed

iter-302 (6 instances) → iter-256 (6) → iter-283 (6) → iter-334 (6) → iter-337 (3 — meta-rule) → **iter-345 (8 — production-pattern)**.

Pattern observation: codification thresholds are now **dynamic based on evidence quality**:
- New patterns: ≥6 instances (iter-302 precedent)
- Meta-rules at higher abstraction layers: ≥3 instances (iter-337 precedent)
- Production patterns with high evidence: ≥6 instances but flexible up to 8+ (iter-345)
- Variety of behavior shapes (e.g. 2+ patterns within a single rule) can substitute for higher count

### Codified-rules tally now at 11

| # | Rule | Iter codified | Trigger |
|---|------|---------------|---------|
| 1 | `feedback_dotnet_test_hang_diagnosis.md` | iter-172 | toolchain |
| 2 | `feedback_aob_drift_across_binary_versions.md` | iter-256 | 6 instances |
| 3 | `feedback_infra_claim_drift_bidirectional.md` | iter-283 | 6 instances |
| 4 | `feedback_engine_already_does_this.md` | iter-302 | 6 instances |
| 5 | `feedback_optional_default_null_constructor_extension.md` | iter-311 | 3 instances |
| 6 | `feedback_status_badge_as_inline_docs.md` | iter-311 | 3 instances |
| 7 | `feedback_extract_on_second_use.md` | iter-316 | 3 instances |
| 8 | `feedback_empirical_first_for_format_re.md` | iter-? | savegame RE |
| 9 | `feedback_locate_by_convention_extensible.md` | iter-334 | 6 instances |
| 10 | `feedback_iter_strategy_preflight_stack.md` | iter-337 | 3 instances (meta-rule) |
| 11 | **`feedback_resolver_injection_at_composition_root.md`** | **iter-345** | **8 instances (highest)** |

11 rules across iter 100-345 = 1 codified rule per ~22 iters. iter-345 brings cadence slightly tighter than iter-337's 1 per ~24 iters.

## What's NOT done in iter-345 (deferred)

- **`feedback_codification_value_proven_by_next_iter.md`** at 1/3 (iter-338): need 2 more instances to validate
- **`feedback_vm_first_xaml_second_iter_split.md`** at 2/3 (iter-148/149 + iter-338/339): need 1 more instance to codify
- **`feedback_audit_compounds_via_rationale_extensions.md`** at 1/3 (iter-341): need 2 more instances
- **`feedback_research_first_implementation_second.md`** at 1/3 (iter-342): need 2 more instances
- **`feedback_graceful_failure_enables_empirical_feedback.md`** at 1/3 (iter-343): need 2 more instances
- **Live SWFOC verify** of iter-343 Hardpoint Inspector chain: requires operator session
- **iter-346 contingency**: pending operator session OR alternative iter

## Verification checklist

- [x] `feedback_resolver_injection_at_composition_root.md` shipped with 11-section template
- [x] Frontmatter contract complete (name/description/type=feedback/originSessionId)
- [x] 8-instance table validates pattern across iter-308/309/312/317/318/319/321/344
- [x] 2 hot-swap behavior patterns documented (Pattern A eager + Pattern B dormant)
- [x] Honest break-out clause covers 4 NOT-applicable cases
- [x] Edge cases section covers 5 sub-rules
- [x] Cost-benefit ratio quantified
- [x] Cross-link to 7 related codified rules
- [x] MEMORY.md index entry added (35 entries; under 200-line truncation threshold)
- [x] All editor build/test gates inherit GREEN from iter-344 republish

## Next iter options (iter-346)

In priority order:

1. **Operator changelog supplement** covering iter 340-345 (~6-iter window since iter-340; well-precedented at iter-235/241/247/262/280/311/320/330/340 cadence; lowest token cost)
2. **Codify** one of the 5 codification candidates at 1/3 trigger (premature unless context budget allows; defer to iter-348+)
3. **Live SWFOC verify** of iter-343 Hardpoint Inspector chain (requires operator session)
4. **README capstone update** (only 23 iters since iter-322; canonical cadence ~30; defer to iter-352+)
5. **Reverse-orphan snapshot audit** (last ran iter-272; ~73 iters since; substantially overdue at canonical ~22-iter cadence)

Recommended: **option 5 (Reverse-orphan snapshot audit)** — substantially past canonical cadence; surfaces orphaned bridge wires + catalog drift; mirrors iter-255/263/272 cadence at canonical 22-iter interval (now 73 iters past due → high probability of surfacing drift).

## Net iter-345 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure docs iter) |
| Doc shipped | 2 files (~165 LoC memory rule + ~115 lines close-out) |
| MEMORY.md entries | 34 → 35 |
| Codified rules tally | 10 → 11 |
| Codification cadence | 1 per ~22 iters |
| Cycle time | ~25 min |
| Pattern lessons surfaced | 0 (audit-clean codification iter) |

**iter-345 codifies the highest-evidence-base pattern in the project at 8 instances.** Future tab additions consuming shared services inherit a 3-step recipe + 2 hot-swap behavior patterns + decision tree.
