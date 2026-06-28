# Iter 368 — Codify `feedback_audits_clean_when_no_new_wires.md` at 2/3 trigger (14th codified rule; Tier 4 meta-rule generalizes across P2HP + reverse-orphan audits)

**Date:** 2026-05-07
**Arc class:** Codification (Tier 4 meta-rule + forward applicability + cross-category generalization)
**Predecessor:** iter-367 (6th reverse-orphan audit CLEAN; pattern generalized from P2HP)
**Successor (queued):** iter-369 (TBD — see "Next iter options" below)

## What changed (1 NEW memory file + 1 MEMORY.md index entry; ~180 LoC)

- **NEW** `~/.claude/projects/.../memory/feedback_audits_clean_when_no_new_wires.md` (~180 LoC):
  - Frontmatter: `name`, `description`, `type=feedback`, `originSessionId`
  - 11-section body following iter-345/iter-359/iter-363 codification template:
    - **Rule** (audits stay CLEAN when iter range covered shipped 0 new visible wires; drift correlates with wire-shipping rate, not iter count)
    - **Why** (table of 2 instances iter-358 P2HP + iter-367 reverse-orphan; both during NON-A1.x quiet wire period)
    - **Empirical support across all audits** (table of 7 P2HP + 6 reverse-orphan audits; drift catches always correlate with new visible wires)
    - **How to apply** (4 numbered steps: survey → predict → run → adjust)
    - **Honest break-out clause** (4 NOT-applicable cases: string-literal additions, deletions, refactor, deferred drift)
    - **Edge cases** (5 sub-rules: lag time, cadence overrun, NON-A1.x extended windows, quiet periods aren't permanent, future categories)
    - **Cost-benefit ratio** (asymmetric upside: prediction CLEAN saves ~5-10 min when correct, costs ~0 min when wrong)
    - **Memory-write triggers** (2-instance evidence + iter-359/363 Tier 4 precedent)
    - **Prospective uses** (iter-375 P2HP / iter-389+ reverse-orphan / Thread B/C/D/E kickoff)
    - **Pattern reinforcement** (cross-link to iter-359 audit-compounds + iter-363 quad + iter-337 preflight)
    - **Cross-link to related codified rules** (5 links to iter-359/363/337/172/345)
- **EXTEND** `~/.claude/projects/.../memory/MEMORY.md` (+1 line + 1 update):
  - New entry: `[Audits-Clean-When-No-New-Wires](feedback_audits_clean_when_no_new_wires.md)`
  - Updated Project Status: bumped from `iter 100-363 (149 LIVE wires + 13 codified rules)` → `iter 100-368 (149 LIVE wires + 14 codified rules)`
  - Index NOW 38 entries (was 37)

## Verification gates ALL GREEN

- 0 source/test/catalog edits in `SWFOC editor/` — pure docs iter
- All editor build/test gates inherit GREEN from iter-364 publish + iter-365 verify + iter-367 audit chain
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- New memory file frontmatter contract complete

## Codification trigger justification — 3rd 2-instance trigger codification (Tier 4 meta-rule + cross-category generalization)

iter-368 is the 3rd Tier 4 codification, validating that the threshold framework continues working at 2-instance scale when the rule is meta-level + forward-applicable:

| Tier | Threshold | Iter-codified instances |
|---|---|---|
| 1. New production patterns | ≥6 instances | iter-302 (engine-already-does-this) + iter-334 (locate-by-convention) |
| 2. Production patterns with high evidence | 6-8+ flexible | iter-345 (resolver-injection at 8 instances) |
| 3. Meta-rules at higher abstraction | ≥3 instances | iter-337 (iter-strategy preflight stack) |
| 4. Meta-rules with forward applicability | ≥2 instances | iter-359 (audit-compounds) + iter-363 (codify-apply-verify-quad) + **iter-368 (audits-clean-when-no-new-wires)** |

**Cross-category generalization is the strongest evidence shape for Tier 4**:
- iter-358 (P2HP audit) and iter-367 (reverse-orphan audit) are DISTINCT audit categories with DIFFERENT mechanisms but SAME outcome correlation with wire-shipping rate
- This isn't 2 instances of the same audit category — it's 2 instances of the same META-PATTERN across 2 different audit categories
- Stronger than iter-359 (P2HP CLEAN repeat) or iter-363 (audit-codify pair repeat)

Tier 4 codification at 2 instances is now solidly justified by 3 codified rules at this threshold.

## Pattern lessons surfaced

### Codification cadence acceleration confirmed (5th time)

iter-302 (6) → iter-334 (6) → iter-337 (3 meta) → iter-345 (8 production-high) → iter-359 (2 meta + forward) → iter-363 (2 meta + forward) → **iter-368 (2 meta + forward + cross-category)**.

Pattern observation: **Tier 4 (meta-rules with forward applicability ≥2) has now codified 3 rules**. Future codifications should defer to this taxonomy without re-deriving thresholds:

- New production patterns: ≥6 instances
- Production patterns with high evidence: 6-8+ flexible
- Meta-rules: ≥3 instances
- Meta-rules with forward applicability: ≥2 instances
- Meta-rules with cross-category generalization: ≥2 instances (strongest evidence shape; iter-368 NEW)

### Codified-rules tally NOW at 14

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
| 10 | `feedback_iter_strategy_preflight_stack.md` | iter-337 | 3 instances (Tier 3) |
| 11 | `feedback_resolver_injection_at_composition_root.md` | iter-345 | 8 instances (Tier 2) |
| 12 | `feedback_audit_compounds_via_rationale_extensions.md` | iter-359 | 2 instances (Tier 4) |
| 13 | `feedback_codify_then_apply_then_verify_quad.md` | iter-363 | 2 instances (Tier 4) |
| 14 | **`feedback_audits_clean_when_no_new_wires.md`** | **iter-368** | **2 instances (Tier 4 + cross-category)** |

14 codified rules across iter 100-368 = 1 rule per ~19 iters (cadence accelerating slightly; 4 codifications in 35 iters reflects Tier 4 making meta-rules more accessible).

## What's NOT done in iter-368 (deferred)

- **`feedback_audit_prep_force_multiplier.md`** at 2/3 (iter-366 → iter-367): need 1 more instance; defer to next audit prep iter
- **`feedback_research_first_implementation_second.md`** at 2/3: need 1 more instance
- **`feedback_vm_first_xaml_second_iter_split.md`** at 2/3: need 1 more instance
- **Live SWFOC verify** of iter-343 chain: requires operator session
- **iter-375 P2HP audit**: 7 iters away (next cadence trigger; will validate iter-368 rule's forward applicability)

## Verification checklist

- [x] `feedback_audits_clean_when_no_new_wires.md` shipped with 11-section template
- [x] Frontmatter contract complete (name/description/type=feedback/originSessionId)
- [x] 2-instance table validates pattern across iter-358 P2HP + iter-367 reverse-orphan
- [x] Empirical-support table includes all 7 P2HP + 6 reverse-orphan audits with drift correlation
- [x] iter-359/iter-363 Tier 4 precedent + cross-category generalization documented
- [x] Honest break-out clause covers 4 NOT-applicable cases
- [x] Edge cases section covers 5 sub-rules
- [x] Cost-benefit ratio quantified (asymmetric upside)
- [x] Cross-link to 5 related codified rules
- [x] MEMORY.md index entry added (38 entries; under 200-line truncation threshold)
- [x] Project Status entry bumped from `13 codified rules` → `14 codified rules`
- [x] All editor build/test gates inherit GREEN

## Next iter options (iter-369)

In priority order:

1. **Wait for natural codification recurrence** — 4 candidates remaining at 2/3 trigger
2. **Live SWFOC verify of iter-343 chain** — requires operator session
3. **NEW arc-class kickoff** — multi-iter; deferred per iter-271
4. **Quiet-loop iter** — pure verification (low utility)
5. **Apply iter-368 rule forward** — predict iter-375 P2HP audit outcome (CLEAN per generalization); ship pre-audit prep doc

Recommended for **iter 369**: option 1 (wait for natural recurrence). Codification queue at 24 candidates; iter-375 P2HP audit is 7 iters away (next cadence trigger). Iters 369-374 are filler iters before iter-375. Opportunistic small-improvement iters welcome.

OR **option 5 (apply iter-368 forward)**: pre-predict iter-375 outcome via the codified rule. Mirrors iter-360's pre-compounding approach. Cost: ~5 min. Benefit: iter-375 audit prep is already done; iter-375 can run + close out in ~3 min.

## Net iter-368 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure docs iter) |
| Doc shipped | 1 NEW memory rule (~180 LoC) + 1 MEMORY.md update + 1 close-out doc (~150 lines) |
| Pattern observations flagged | 0 NEW (codification iter; consolidates existing pattern) |
| Cycle time | ~25 min |
| Codified rules tally | 13 → **14** (+1; cadence ~1 per ~19 iters) |
| MEMORY.md entries | 37 → 38 |

**iter-368 is the 3rd Tier 4 codification in the project**, demonstrating cross-category generalization (P2HP + reverse-orphan audits sharing same meta-pattern). The 4-tier codification threshold framework is now solidly validated at 14 codified rules total. Future audit cadence triggers can apply this rule to predict outcome ahead of time (per iter-366 prep pattern at iter-367).

38th post-iter-323 arc iter (6 LIVE + 6 codification + 3 republish + 1 XAML + 16 docs/audit/inventory/promote/verification + 1 warning-cleanup + 1 build-verify + 2 test-verify + 1 P2HP audit + 1 reverse-orphan audit + 1 pre-compound + 1 pre-compound-verify); 99th consecutive NON-A1.x iter per iter-269 lesson #2.
