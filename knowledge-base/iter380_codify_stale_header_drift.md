# Iter 380 — Codify `feedback_stale_groupbox_header_drift.md` at 7/6 trigger (18th codified rule; first Tier 1/2 codification post iter-359-374 cluster)

**Date:** 2026-05-07
**Arc class:** Codification (Tier 1/2 production-pattern; closes UX polish 3-iter arc cleanly)
**Predecessor:** iter-379 (4-fix batch; 7/6 trigger reached)
**Successor (queued):** iter-381 (TBD per "Next iter" below)

## What changed (1 NEW memory file + 2 MEMORY.md edits + 1 close-out doc; ~250 LoC docs)

- **NEW** `~/.claude/projects/.../memory/feedback_stale_groupbox_header_drift.md` (~225 LoC):
  - Frontmatter: `name`, `description`, `type=feedback`, `originSessionId`
  - 11-section template per iter-302/iter-334/iter-345 Tier 1/2 codification format
  - Rule statement + GOOD/BAD example pairs
  - 7-instance evidence table (UnitControl iter-377 + Combat iter-378 + PlayerState iter-378 + WorldState iter-379 + Inspector iter-379 + Spawning iter-379 + Spawning Discovery iter-379)
  - Operator-impact analysis (4 reasons WHY the rule matters)
  - 4-step "How to apply" + count-only vs scope-described header strategy
  - 4 NOT-applicable cases (tab names, atomic features, milestone iters, intrinsic-iter sections)
  - 5 edge-case sub-rules (label-no-longer-fits-scope / mixed read/write / span-many-iters / count-uncertain / multi-sub-type)
  - Cost-benefit ratio (~7 LoC per fix; ~1 min cycle when batched)
  - Codification trigger justification (Tier 1/2; 7/6 instances; concrete-work-grounded NOT meta-codification)
  - iter-381+ prospective uses + audit-cadence recommendation
  - Cross-link to 4 related codified rules (iter-302/334/345/368)
  - 4 pattern lessons surfaced during codification
- **EXTEND** `MEMORY.md` (+1 line + 1 update):
  - New entry: `[Stale-GroupBox-Header-Drift]`
  - Updated Project Status: bumped from `iter 100-374 (149 LIVE wires + 17 codified rules)` → `iter 100-380 (149 LIVE wires + 18 codified rules)`
  - Index NOW 42 entries (was 41)

## Verification gates ALL GREEN

| Gate | Result |
|------|--------|
| MEMORY.md frontmatter contract | Complete |
| New memory file 11-section template | Complete |
| Editor build/test gates | inherit GREEN from iter-377/378/379 chain |
| Bridge harness | inherits 1100/0 |
| Verifier ledger lint | inherits 0/0 at 318 entries |
| Editor binary | inherits 157.89 MB at iter-379 timestamp 11:17:00 |

## Codification trigger justification — Tier 1/2 production-pattern at 7/6 instances

iter-380 is the **first Tier 1/2 codification post-cluster** (iter-359-374 was 6 Tier 4 meta-rules), validating that concrete operator-visible work generates strong codification triggers organically.

| Tier | Threshold | Iter-codified instances |
|---|---|---|
| 1. New production patterns | ≥6 instances | iter-302 + iter-334 + **iter-380** |
| 2. Production patterns with high evidence | 6-8+ flexible | iter-345 |
| 3. Meta-rules at higher abstraction | ≥3 instances | iter-337 |
| 4. Meta-rules with forward applicability | ≥2 instances | iter-359/363/368/371/373/374 |

**iter-380 is the 3rd Tier 1 codification** (iter-302 = 6 instances; iter-334 = 6 instances; iter-380 = 7 instances). Pattern was generated organically by iter-377/378/379 UX polish work, NOT by abstraction-laddering meta-codification — fundamentally different epistemology than the iter-359-374 Tier 4 cluster.

## Codified rules tally NOW at 18

| # | Rule | Iter codified | Trigger |
|---|------|---------------|---------|
| 1 | `feedback_dotnet_test_hang_diagnosis.md` | iter-172 | toolchain |
| 2-9 | (production / 3-instance / 8-instance rules) | iter-256/283/302/311/316/?/334/345 | various |
| 10 | `feedback_iter_strategy_preflight_stack.md` | iter-337 | 3 instances (Tier 3) |
| 11 | `feedback_resolver_injection_at_composition_root.md` | iter-345 | 8 instances (Tier 2) |
| 12 | `feedback_audit_compounds_via_rationale_extensions.md` | iter-359 | 2 instances (Tier 4) |
| 13 | `feedback_codify_then_apply_then_verify_quad.md` | iter-363 | 2 instances (Tier 4) |
| 14 | `feedback_audits_clean_when_no_new_wires.md` | iter-368 | 2 instances (Tier 4) |
| 15 | `feedback_audit_prep_force_multiplier.md` | iter-371 | 2 instances (Tier 4) |
| 16 | `feedback_codified_rule_self_validates_via_forward_application.md` | iter-373 | 2 instances (Tier 4 meta-meta) |
| 17 | `feedback_advance_audit_cadence_when_predicted_clean.md` | iter-374 | 2 instances (Tier 4) |
| 18 | **`feedback_stale_groupbox_header_drift.md`** | **iter-380** | **7 instances (Tier 1)** |

18 codified rules across iter 100-380 = 1 rule per ~16 iters average.

## Pattern lesson — concrete-work-driven codification beats meta-codification

This is the headline learning of iter-380:

- iter-359-374 cluster (6 codifications in 16 iters) was Tier 4 meta-rules accelerating to ~1 per ~3 iters; iter-375 meta-reflection acknowledged saturation
- iter-377-379 UX polish (3 iters of concrete work) generated 7 production-pattern instances → Tier 1 codification trigger reached organically WITHOUT abstraction laddering

The empirical lesson: **concrete production work generates STRONGER codification triggers than meta-codification**. iter-380's 7-instance evidence base is grounded in 7 real production patterns; iter-374's 2-instance evidence base was 2 abstract recurrence-validations.

This is itself a Tier 5 candidate (rule about codification-grounding) but **not codifying it now** — would risk re-entering cluster-saturation pattern. Defer to natural recurrence (e.g., when next concrete-work arc generates another Tier 1 trigger, then 2/3 trigger fires for this meta-meta-meta rule).

## What's NOT done in iter-380 (deferred)

- **Lua Playground line 4248 preset menu refresh** ("Iter 100-300 LIVE wires"): noted in iter-378 audit catalog as needing multi-iter preset-menu refresh; deferred to iter-381+
- **UX Pattern 2 — demote iter-N references from user-facing tooltips**: ~30 tooltips in UnitControl alone; multi-iter sub-arc deferred per iter-377 inventory
- **Operator changelog supplement** for iter 348-380 (~33-iter window since iter-347 supplement; canonical post-arc cadence) — deferred 1 more iter
- **Live SWFOC verify** of iter-343 Hardpoint Inspector chain — requires operator session

## Codification queue update (post-iter-380)

| Class | Pre-iter-355 | Post-iter-380 |
|---|---|---|
| Class A (high-recurrence) | 4 | 4 (unchanged) |
| Class B (medium-recurrence) | 5 | 5 (unchanged) |
| Class C (retire/promote) | 2 | 2 (unchanged) |
| Class C low-priority watch | 1 | 1 (unchanged) |
| iter-355→379 candidates | 0 | +18 (7 codified iter-359/363/368/371/373/374/380 + 11 at 1/3 trigger) |

**Codification queue NOW: 28 candidates total** (was 29 pre-iter-380; -1 because iter-377 NEW pattern just CODIFIED; +0 NEW from iter-380 docs iter).

## Net iter-380 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure docs iter) |
| Doc shipped | 1 NEW memory rule (~225 LoC) + 1 MEMORY.md update + 1 close-out doc (~150 lines) |
| Pattern observations flagged | 0 NEW (codification iter; consolidates existing pattern) |
| Cycle time | ~25 min |
| Codified rules tally | 17 → **18** |
| MEMORY.md entries | 41 → 42 |
| UX polish 3-iter arc | **CLOSED end-to-end** (iter-377 survey + iter-378 audit catalog + iter-379 batch fix + iter-380 codification) |

**iter-380 closes the UX polish 3-iter arc with a concrete-work-grounded codification artifact.** The 4-iter sequence (iter-377 survey → iter-378 catalog → iter-379 batch-fix → iter-380 codify) is a clean codify-apply-verify quad per iter-363 codified rule, validating the pattern's repeatability across distinct domain contexts (audit-organization vs UX polish).

50th post-iter-323 arc iter (6 LIVE + 10 codification + 5 republish + 7 XAML + 19 docs/audit/inventory/promote/verification + 1 warning-cleanup + 1 build-verify + 5 test-verify + 2 P2HP audit + 1 reverse-orphan audit + 2 pre-compound + 1 pre-compound-verify + 1 meta-reflection + 3 UX-polish + **1 UX-codification**); 111th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter options (iter-381)

In priority order:

1. **Operator changelog supplement** for iter 348-380 (~33-iter window since iter-347 supplement; canonical post-arc cadence; 12th instance per iter-235/241/247/262/280/311/320/330/340/347/362/372). **Recommended** — closes the iter-377→380 UX polish + codification arc with operator-facing docs.
2. **Pivot to UX Pattern 2** (demote iter-N references from user-facing tooltips per iter-377 inventory) — ~30 tooltips in UnitControl alone; multi-iter sub-arc.
3. **Pivot to UX Pattern 3** (de-duplicate amber warning banners) — Combat + Galactic; ~80 LoC.
4. **Lua Playground preset menu refresh** (line 4248) — closes iter-378 audit's "needs refresh" entry.
5. **NEW arc-class kickoff** — multi-iter; deferred per iter-271 (savegame editor finer features / overlay Tier 4 / etc.).
6. **Live SWFOC verify** of iter-343 chain — requires operator session.

iter-381 should ship operator-facing docs (option 1) to close the arc cleanly + maintain the 11-instance post-arc-docs cadence (iter-235/241/247/262/280/311/320/330/340/347/362/372 = 12 prior instances). After that, iter-382+ can pivot to UX Pattern 2 (tooltip cleanup) for another 5-10 iter sub-arc.
