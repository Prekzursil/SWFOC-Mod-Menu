# Iter 407 — Codify `feedback_static_data_re_extraction.md` at 3/3 trigger (20th codified rule, 5th Tier-1 production)

**Date:** 2026-05-07
**Arc class:** Codification (Tier-1 production rule at 3-instance trigger)
**Predecessor:** iter-406 (3rd EnumConversionClass extraction; 3/3 trigger reached)
**Successor (queued):** iter-408 (TBD per "Next iter" below)

## What this iter does

Codifies the static-data RE extraction pattern that empirically proved itself across **3 instances** in 5 iters (iter-402-406):

| Iter | Target | Result | Cycle time |
|---|---|---|---|
| 402-404 | UnitAbilityType | 69 names + UnitControl ComboBox (UX SHIPPED) | ~50 min mini-arc |
| 405 | ModelAnimType | 111 names + ledger pin (UX DEFERRED) | ~10 min |
| 406 | GUIGadgetComponentType | 83 names + ledger pin (UX DEFERRED) | ~8 min |

**Cumulative**: 263 engine-canonical strings extracted in ~70 min total. Marginal cost trends to ~5-10 min per future extraction.

## Codification details

### Rule file
`~/.claude/projects/C--Users-Prekzursil-Downloads-swfoc-memory/memory/feedback_static_data_re_extraction.md`

### Tier classification
**Tier-1 production rule** at 3-instance trigger (vs the 6-instance precedent of iter-302/334/345/380/388 production rules). Justification:
1. Pattern is **mechanical** (not heuristic) — 4-step recipe with no judgment calls
2. Tooling generalized at iter-405 (`extract_enum_conversion_strings.py` template)
3. Marginal cost dramatically dropped (50 min → 10 min → 8 min)
4. 3 instances span the breadth: 1 with full UX consumer + 2 with deferred UX (proves codification value is recipe-not-instance)
5. Per iter-337 meta-rule precedent: "thresholds dynamic by evidence quality" — when pattern is mechanical, fewer instances needed

### 11-section template (per iter-388 latest production-rule shape)
1. **Rule** (one-line statement)
2. **Why** (rationale for preferring extraction over roundtripping)
3. **How to apply** (7-step mechanical recipe)
4. **Examples** (3 confirmed instances with sizes + UX status)
5. **Honest break-out cases** (5 scenarios where rule does NOT apply)
6. **Cost-benefit** (50/10/8 min progression + comparison vs RVA-pin alternative)
7. **Cross-reference to iter-302** (this rule is a Tier-2 extension; together they form the engine-already-does-this taxonomy)
8. **Cross-reference to iter-256** (different pattern, similar callgraph workflow)
9. **Codification trigger** (3/3 + reasoning)
10. **4th+ candidates** (untouched_subsystems.md has more enum candidates for opportunistic extraction)
11. **Open questions** (none flagged; pattern is mature)

## What shipped

1. **`~/.claude/projects/.../memory/feedback_static_data_re_extraction.md`** — NEW codified rule (~120 lines)
2. **MEMORY.md** — index updated:
   - Project Status entry: iter range 100-388 → 100-407; codified rules 19 → 20
   - NEW entry: `[Static-Data RE Extraction]` with one-line summary
3. **iter407 close-out doc** (this file)

## Codified rules tally (post-iter-407)

| # | Iter | Rule | Tier | Trigger |
|---|---|---|---|---|
| 1-11 | (carried) | (5 from iter-272 era + 6 production iter-256/283/302/311/316/334) | 1 | various |
| 12 | iter-359 | audit-compounds-via-rationale-extensions | 4 (meta) | 2/3 |
| 13 | iter-363 | codify-then-apply-then-verify-quad | 4 (meta) | 2/3 |
| 14 | iter-368 | p2hp-clean-when-no-new-wires | 4 (meta) | 2/3 |
| 15 | iter-371 | audit-prep-force-multiplier | 4 (meta) | 2/3 |
| 16 | iter-373 | codified-rule-self-validates-via-forward-application | 4 (meta) | 2/3 |
| 17 | iter-374 | advance-audit-cadence-when-predicted-clean | 4 (meta) | 2/3 |
| 18 | iter-380 | stale-groupbox-header-drift | 1 (production) | 7/6 |
| 19 | iter-388 | internal-codename-in-tooltips-drift | 1 (production) | 88/6 |
| **20** | **iter-407 (THIS)** | **static-data-re-extraction** | **1 (production)** | **3/3** (mechanical pattern; lower trigger justified) |

**5 Tier-1 production rules total**: iter-302/334/345/380/388/**407** (counting both iter-345 8-instance and iter-407 3-instance trigger as Tier-1; iter-345 set the precedent for "evidence quality > evidence count").

## Verification gates ALL GREEN at iter-407

- ✅ Memory file written; MEMORY.md index updated
- ✅ All editor build/test gates inherit GREEN from iter-401-406 chain
- ✅ Verifier lint 0/0 at 321 entries (sustained from iter-406)
- ✅ Bridge harness 1100/0 (continuous)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-407 ships 0 source/test/XAML)

## Net iter-407 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure docs iter) |
| Doc shipped | 1 NEW codified rule (~120 lines) + MEMORY.md update + iter407 close-out (~150 lines) |
| Pattern observations flagged | 0 NEW (this iter codifies the iter-402-406 pattern) |
| Codified rules count | 19 → **20** |
| Tier-1 production rules | 4 → **5** (iter-302/334/345/380/388/407) |
| Cycle time | ~10 min (write rule + index update + close-out) |

**iter-407 codifies the iter-402-406 pattern at 3/3 trigger.** 20th codified rule, 5th Tier-1 production rule. Future EnumConversionClass extractions ship at ~5-10 min via the codified recipe.

76th post-iter-323 arc iter (1st codification iter for the new pattern); 137th consecutive NON-A1.x iter per iter-269 lesson #2.

## Forward-applicability validation queue

Per iter-373 codified rule (`feedback_codified_rule_self_validates_via_forward_application.md`), iter-407's rule should accumulate forward-applicability validations:

- **Validation #1 (queued)**: Next EnumConversionClass extraction at iter-408+ would be the rule's 1st post-codification application; cycle should be ~5 min (recipe stable)
- **Validation #2 (queued)**: A different RTTI cluster type (e.g. RegistryClass<T>) should also fit the recipe with minor adaptation; would prove generalization beyond EnumConversionClass

## Next iter (iter-408)

Options:

1. **4th EnumConversionClass extraction** — opportunistic; per "4th+ candidates" section of the codified rule. Would compound forward-applicability to validation #1.
2. **Operator changelog supplement** covering iter 401-407 (closes 14-iter doc gap since iter-393).
3. **Different cluster type extraction** — pick a non-EnumConversionClass cluster (PerceptionParameterBindingsClass, SignalListenerClass) to test generalization.
4. **NEW arc-class kickoff** — RE Play_Animation engine helper to unlock deferred ModelAnimType UX consumer.
5. **Live SWFOC verify** of iter-403 ComboBox.

iter-408 likely option 1 or 2 (lowest-risk, fast-cycle).
