# Iter 363 — Codify `feedback_codify_then_apply_then_verify_quad.md` at 2/3 trigger (13th codified rule; meta-rule + forward-applicability per iter-359 precedent)

**Date:** 2026-05-07
**Arc class:** Codification (mirrors iter-359 2-instance trigger codification at meta-rule + forward-applicability layer)
**Predecessor:** iter-362 (10th operator changelog supplement)
**Successor (queued):** iter-364 (TBD — see "Next iter options" below)

## What changed (1 NEW memory file + 1 MEMORY.md index entry; ~175 LoC)

- **NEW** `~/.claude/projects/.../memory/feedback_codify_then_apply_then_verify_quad.md` (~175 LoC):
  - Frontmatter: `name`, `description`, `type=feedback`, `originSessionId`
  - 11-section body following iter-345/iter-359 codification template:
    - **Rule** (4-step quad: audit → codify → apply → verify)
    - **Why** (table of 2 instances iter 354-357 + iter 358-361 with cycle time + value documented)
    - **How to apply** (4 numbered steps with min estimates)
    - **Honest break-out clause** (4 NOT-applicable cases: audit clean, 1/3 trigger candidate, trivial application, non-adjacent quad)
    - **Edge cases worth flagging** (5 sub-rules: retroactive recognition, smaller-scope apply, fast verify, monotonic confidence, 11-iter scale variant via iter-308-321)
    - **Cost-benefit ratio** (~70 min total cycle / 1 codified rule + N applied consumers + N verified gates per quad)
    - **Memory-write triggers** (2-instance evidence + iter-359 meta-rule precedent justification)
    - **Prospective uses** (iter-368 reverse-orphan audit + next memory-rule codification iter + NEW arc-class kickoff)
    - **Pattern reinforcement** (cross-link to iter-359 audit-compounds + iter-337 preflight + iter-338 codification-value-by-next-iter)
    - **Cross-link to related codified rules** (5 links to iter-359/337/345/302/172)
- **EXTEND** `~/.claude/projects/.../memory/MEMORY.md` (+1 line + 1 update):
  - New entry: `[Codify-Then-Apply-Then-Verify Quad](feedback_codify_then_apply_then_verify_quad.md)` with 1-line description
  - Updated Project Status entry: bumped from `iter 100-359 (149 LIVE wires + 12 codified rules)` → `iter 100-363 (149 LIVE wires + 13 codified rules)`
  - Index now contains 37 entries (was 36)

## Verification gates ALL GREEN

- 0 source/test/catalog edits in `SWFOC editor/` — pure docs iter
- All editor build/test gates inherit GREEN from iter-356 build re-run + iter-357 test verify + iter-358 P2HP audit + iter-359-362 chain
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- New memory file frontmatter contract complete (name/description/type=feedback/originSessionId)

## Codification trigger justification — 2nd 2-instance trigger codification (meta-rule + forward-applicability)

iter-359 established the 4-tier codification threshold; iter-363 applies the 4th tier (meta-rules with forward applicability ≥2 instances) for the 2nd time:

| Tier | Threshold | First instance | Second instance |
|---|---|---|---|
| 1. New production patterns | ≥6 instances | iter-302 (engine-already-does-this) | iter-334 (locate-by-convention) |
| 2. Production patterns with high evidence | 6-8+ flexible | iter-345 (resolver-injection at 8 instances) | — |
| 3. Meta-rules at higher abstraction | ≥3 instances | iter-337 (iter-strategy preflight stack) | — |
| 4. Meta-rules with forward applicability | ≥2 instances | iter-359 (audit-compounds) | **iter-363 (codify-apply-verify-quad)** |

Justification for iter-363's tier-4 codification:

1. **Meta-pattern**: about how to organize codification arcs (audit + codify + apply + verify), not a production code pattern
2. **Forward applicability**: iter-368 reverse-orphan audit (5 iters away) and any future codification iter can apply this rule directly
3. **High evidence per instance**: both iter 354-357 and iter 358-361 ran in ~70 min total / produced 1 codified rule + N applied consumers / ended with empirically-verified GREEN gates
4. **iter-361 was the codification trigger**: 2nd instance recognized retroactively when comparing iter 354-357 + iter 358-361 shapes

## Pattern lessons surfaced

### Codification cadence acceleration confirmed (4th time)

iter-302 (6 instances) → iter-334 (6) → iter-337 (3 — meta-rule) → iter-345 (8 — production) → iter-359 (2 — meta-rule + forward-applicability) → **iter-363 (2 — meta-rule + forward-applicability)**.

Pattern observation: **the 4-tier codification threshold system is now stable**:
- Tier 4 (meta-rules with forward applicability ≥2) has 2 instances; pattern itself codified by precedent
- Future codifications can defer to this taxonomy without re-deriving thresholds

### Codified-rules tally NOW at 13

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
| 10 | `feedback_iter_strategy_preflight_stack.md` | iter-337 | 3 instances (meta-rule tier-3) |
| 11 | `feedback_resolver_injection_at_composition_root.md` | iter-345 | 8 instances |
| 12 | `feedback_audit_compounds_via_rationale_extensions.md` | iter-359 | 2 instances (meta-rule + forward-applicability tier-4) |
| 13 | **`feedback_codify_then_apply_then_verify_quad.md`** | **iter-363** | **2 instances (meta-rule + forward-applicability tier-4)** |

13 codified rules across iter 100-363 = 1 rule per ~22 iters (cadence stable; iter-363 maintains the trend; 2 codifications in 4 iters reflects the meta-rule tier-4 trigger making meta-patterns more accessible to codification).

## What's NOT done in iter-363 (deferred)

- **`feedback_p2hp_clean_when_no_new_wires.md`** at 1/3 (iter-358): need 2 more instances; defer to iter-375+
- **`feedback_codification_value_proven_by_next_iter.md`** at 1/3 (iter-338): need 2 more instances
- **`feedback_research_first_implementation_second.md`** at 2/3: need 1 more instance
- **`feedback_vm_first_xaml_second_iter_split.md`** at 2/3: need 1 more instance
- **Live SWFOC verify** of iter-343 chain: requires operator session
- **iter-368 reverse-orphan audit**: 5 iters away — apply iter-363 quad rule forward when it lands

## Verification checklist

- [x] `feedback_codify_then_apply_then_verify_quad.md` shipped with 11-section template
- [x] Frontmatter contract complete (name/description/type=feedback/originSessionId)
- [x] 2-instance table validates pattern across iter 354-357 + iter 358-361 with cycle time + value documented
- [x] iter-359 meta-rule + forward-applicability precedent + 4-tier codification threshold table documented
- [x] Honest break-out clause covers 4 NOT-applicable cases
- [x] Edge cases section covers 5 sub-rules
- [x] Cost-benefit ratio quantified (~70 min cycle / high-value-per-iter)
- [x] Cross-link to 5 related codified rules
- [x] MEMORY.md index entry added (37 entries; under 200-line truncation threshold)
- [x] Project Status entry bumped from `12 codified rules` → `13 codified rules`
- [x] All editor build/test gates inherit GREEN

## Next iter options (iter-364)

In priority order:

1. **Wait for natural codification recurrence** — 4 candidates at 2/3 trigger remain (vm_first_xaml_second + research_first_implementation_second + p2hp_clean_when_no_new_wires + 1 from iter-360). Next 3rd-instance trigger likely iter-368 reverse-orphan OR iter-375 P2HP.
2. **Live SWFOC verify of iter-343 chain** — requires operator session
3. **NEW arc-class kickoff** — multi-iter; deferred per iter-271
4. **Quiet-loop iter** — pure verification (low utility)
5. **Apply iter-363 quad pattern forward** — there's no immediate consumer (next codification opportunity is iter-368+); could ship a "quad pattern checklist" doc for future codification iters to reference, but premature.

Recommended for **iter 364**: option 1 (wait for natural recurrence). Codification queue is steady at 19 candidates; iter-368 audit is 4 iters away. Iters 364-367 are filler iters before iter-368 cadence-driven trigger. Opportunistic small-improvement iters welcome.

## Net iter-363 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure docs iter) |
| Doc shipped | 1 NEW memory rule (~175 LoC) + 1 MEMORY.md update + 1 close-out doc (~140 lines) |
| Pattern observations flagged | 0 NEW (codification iter; consolidates existing pattern) |
| Cycle time | ~25 min |
| Codified rules tally | 12 → **13** (+1; cadence ~1 per ~22 iters maintained) |
| MEMORY.md entries | 36 → 37 |

**iter-363 is the 2nd 2-instance trigger codification in the project**, validating iter-359's tier-4 codification threshold (meta-rules with forward applicability ≥2). Pattern about codification arc organization is now formally codified; future audit-driven codification arcs inherit a 4-step recipe.

33rd post-iter-323 arc iter (6 LIVE + 5 codification + 2 republish + 1 XAML + 14 docs/audit/inventory/promote/verification + 1 warning-cleanup + 1 build-verify + 1 test-verify + 1 P2HP audit + 1 pre-compound + 1 pre-compound-verify); 94th consecutive NON-A1.x iter per iter-269 lesson #2.
