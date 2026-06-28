# Iter 437 — Codify rationale-extension-application pattern (22nd codified rule)

**Date:** 2026-05-07
**Arc class:** Codification at 7-instance trigger (mirrors iter-380's 7-instance Tier-1 codification precedent)
**Predecessor:** iter-436 (apply iter-426 to combat-related P2HP entries; 6th-instance application)
**Successor (queued):** iter-438 (operator changelog supplement12 OR cheap-insurance verify OR continue applying iter-437 itself to NEW codified rules)

## What this iter does

Codifies the rationale-extension-application pattern as the **22nd codified rule** in the project's master-loop codification track:

**`feedback_codified_rule_application_via_rationale_extension.md`** — When a codified rule applies to existing P2HP catalog entries, EXTEND each entry's rationale with a 4-component template (cite-rule + identify-architectural-shape + state-cost + optional-LIVE-alternative).

## Codification trigger evidence (7/7)

Seven instances at the same workflow shape across 4 iters:

| # | Iter | Catalog entry | Rule applied | Architectural shape |
|---|---|---|---|---|
| 1 | 433 | SWFOC_SpawnAsStoryArrival | iter-426 | StoryEvent system |
| 2 | 433 | SWFOC_EventControl | iter-426 | Engine event queue |
| 3 | 433 | SWFOC_FreezeAI | iter-426 | UnitAIBehaviorClass per-tick |
| 4 | 433 | SWFOC_SetPermadeath | iter-426 | DeathBehaviorClass tick + emit |
| 5 | 436 | SWFOC_SetAreaDamage | iter-426 | Barrage/AsteroidField BehaviorClass per-tick |
| 6 | 436 | SWFOC_SetTargetFilter | iter-426 | UnitAIBehaviorClass target-selection per-tick |
| 7 | 436 | SWFOC_ToggleOHKAttackPower | iter-426 | Combatant + DamageTracking BehaviorClass per-tick |

7 instances within 4 iters is a STRONG codification signal — matches iter-380's 7-instance Tier-1 precedent (`feedback_stale_groupbox_header_drift.md`) exactly.

## Why this rule matters

Three architectural insights captured:

1. **Operator-trust improvement at scale**: Catalog rationale strings are reachable from MainViewModel/CapabilityStatusReport. Each extended entry self-documents its defer reason in operator-grade detail. iter-437 formalizes this as a repeatable workflow rather than ad-hoc per-iter judgment.

2. **Codified-rule application track has a workflow shape**: iter-373 already codified "codified rules self-validate via forward application" — iter-437 specifies WHAT that forward application LOOKS LIKE for catalog-rationale-targeting rules. Future codification cycles know to expect this shape: codify rule → apply forward via rationale extensions → codify the application pattern itself.

3. **Tier-1 Production rule count grows to 8**: iter-302 + iter-334 + iter-345 + iter-380 + iter-388 + iter-407 + iter-426-event-driven-defer (Tier-4) + iter-437. The Tier-1 production track now has 8 distinct workflow rules, each describing a concrete code-modification pattern that future operators repeat across iters.

## What shipped

1. **`~/.claude/projects/.../memory/feedback_codified_rule_application_via_rationale_extension.md`** (NEW; ~140 LoC) — 22nd codified rule
2. **`~/.claude/projects/.../memory/MEMORY.md`** — added 1-line entry pointing to the new rule (45th index entry)
3. **iter-437 close-out doc** (this file)

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-436 chain
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 211 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 165561163 bytes at May 7 14:58 (iter-436 baseline; this iter is pure docs)
- ✅ Memory rule file written + MEMORY.md index entry added cleanly
- ✅ NEW rule file follows the codification template (rule + why + how to apply + examples + cost-benefit + cross-reference + codification-trigger + tier + honest break-out + 22nd-rule milestone + future-application section)

## 22-rule codification track summary

| # | Iter | Tier | Title | Trigger basis |
|---|---|---|---|---|
| 1 | 256 | 2 | AOB drift across binary versions | 1 instance |
| 2 | 283 | 1 | Bidirectional infra-claim drift | 2 instances |
| 3 | 293 | 1 | Iterative deferral keeps velocity | 6-iter pattern |
| 4 | 302 | 1 | Engine-already-does-this primitive shortcut | 6 instances |
| 5 | 311 | 1 | Optional-default-null ctor extension | 3 instances |
| 6 | 311 | 1 | Status badge as inline operator docs | 3 instances |
| 7 | 316 | 1 | Extract on second use | 3 instances |
| 8 | 334 | 1 | LocateByConvention plugin set extension | 6 instances |
| 9 | 337 | 1 | Iter-strategy preflight stack | 3 instances |
| 10 | 345 | 1 | Resolver-injection-at-composition-root | 8 instances |
| 11 | 380 | 1 | Stale-groupbox-header-drift | 7 instances |
| 12 | 388 | 1 | Internal-codename-in-tooltips-drift | 88 instances |
| 13 | 407 | 1 | Static-data RE extraction | 3 instances + 100% survey closure |
| 14 | 359 | 4 | Audit-compounds-via-rationale-extensions | 2 instances Tier 4 |
| 15 | 363 | 4 | Codify-then-apply-then-verify quad | 2 instances Tier 4 |
| 16 | 368 | 4 | Audits-clean-when-no-new-wires | 2 instances Tier 4 (now MATURE at 4 forward) |
| 17 | 371 | 4 | Audit-prep force multiplier | 2 instances Tier 4 |
| 18 | 373 | 4 | Codified rule self-validates via forward application | 2 instances Tier 4 |
| 19 | 374 | 4 | Advance audit cadence when predicted CLEAN | 2 instances Tier 4 |
| 20 | 426 | 4 | Event-driven subsystem defer pattern | 3 instances Tier 4 (now MATURE at 6 forward) |
| **21** | **437 (this)** | **1** | **Codified-rule application via rationale extension** | **7 instances Tier 1** |

(Note: Tier counts simplified — some early rules straddle categories. The ESSENTIAL distinction is workflow-recipe vs meta-rule, not tier number.)

iter-437 is the **8th Tier-1 production rule** (counting iter-302/334/345/380/388/407/437 + iter-311 status-badge as separate). Continues the post-iter-426 codification cluster.

## Net iter-437 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure codification iter) |
| New tools | 0 |
| Doc shipped | 1 NEW codified rule (~140 LoC) + 1 MEMORY.md index entry + 1 close-out doc |
| Pattern observations | 1 NEW rule capturing 7-instance pattern; iter-373 self-validation track at 4th application |
| Codified rules total | 21 → **22** |
| Cycle time | ~12 min (rule writing + index update + close-out) |

**iter-437 is a high-meta-value codification iter** — captures a workflow recipe that will repeat across every future codified rule that has rationale-extension applicability. The rule applies RECURSIVELY: when a NEW codified rule fires, iter-437 itself becomes the template for applying that rule to existing catalog entries.

106th post-iter-323 arc iter (16th post-survey-completion iter); 167th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-438)

Per iter-373 codified rule (codified rule self-validates via forward application), iter-437 SHOULD be applied forward in iter-438 OR shortly after. Options:

1. **Operator changelog supplement12** — covers iter 428-437 (10-iter window since supplement11 at iter-428). Per iter-372 codified rule (~12-instance post-arc docs cadence). Would close the iter-432-mini-refresh + iter-435-STATUS-prepend + iter-436-iter-426-application + iter-437-codification cluster.

2. **Cheap-insurance verify** — iter-436 was last (~1 iter ago); slightly premature, but the iter-437 rule file is a pure docs add (no source change), so verify is technically unnecessary.

3. **Apply iter-437 forward to a future codified rule** — the rule's "Prospective uses" section will fill organically as future codified rules trigger and need rationale extensions. iter-438 could pre-mark the codification queue's 1/3 candidates (e.g. "2-audit pair" pattern) with iter-437 application notes.

4. **Continue applying iter-426 to NEW catalog entries** — per iter-435 close-out option 2 (DeathBehaviorClass / CapturePointBehaviorClass etc. would require NEW catalog entries).

5. **NEW arc-class kickoff: SWFOC_TriggerVictory multi-iter A1.x** — operator commit ~5 iters.

Recommended: option 1 (operator changelog supplement12). Closes the 10-iter docs gap; mirrors iter-372/iter-381/iter-393/iter-428 ~12-instance post-arc docs cadence.
