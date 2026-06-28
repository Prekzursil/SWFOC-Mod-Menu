# Iter 436 — Apply iter-426 to combat-related P2HP entries (6th-instance application)

**Date:** 2026-05-07
**Arc class:** Codified-rule forward application (mirrors iter-433 pattern; combat subsystem extension)
**Predecessor:** iter-435 (STATUS.md surgical mini-refresh)
**Successor (queued):** iter-437 (verify republish results OR continue with NEW catalog entries OR codify 2-audit-pair pattern)

## What this iter does

Continues the iter-433 forward-application pattern (EXTEND-existing P2HP entries with iter-426 rationale) for 3 more combat-related entries that match the event-driven Observer-pattern signature.

## Catalog entries extended

| Entry | Original rationale | iter-436 extension | *BehaviorClass match |
|---|---|---|---|
| `SWFOC_SetAreaDamage` | BLOCKED-NO-RVA | + Event-driven; per iter-426; multi-iter A1.x; iter-96 SetDamageMultiplierGlobal LIVE alternative | BarrageAreaBehaviorClass + AsteroidFieldDamageBehaviorClass (per-frame area damage) |
| `SWFOC_SetTargetFilter` | BLOCKED-NO-RVA | + Event-driven; per iter-426; multi-iter A1.x | UnitAIBehaviorClass (per-tick target-selection) |
| `SWFOC_ToggleOHKAttackPower` | BLOCKED-NO-RVA | + Event-driven; per iter-426; multi-iter A1.x; iter-96 LIVE alternative | CombatantBehaviorClass + DamageTrackingBehaviorClass (per-tick damage application) |

Each extension follows the iter-433 template: cite iter-426 + identify *BehaviorClass + state multi-iter A1.x cost + (where applicable) cite LIVE alternative.

## iter-426 rule maturity post-iter-436

Forward applications since iter-426 codification:
1. **iter-416** Play_Animation — codification trigger #1
2. **iter-422** SWFOC_GetUnitLocomotorState — codification trigger #2
3. **iter-423** SWFOC_TriggerVictory — codification trigger #3 (3/3 fired)
4. **iter-427** Forward callgraph scan — 119+1 RTTI candidates pre-classified
5. **iter-433** Catalog rationale integration — 4 entries extended (lifecycle + AI + story)
6. **iter-436 (this)** Catalog rationale integration #2 — 3 entries extended (combat-specific)

**6-instance forward-application track**: iter-426 rule has empirical evidence at 5 distinct application contexts (codification + scan + 2× catalog integration). The 5-context pattern from iter-433 close-out is now reinforced — iter-436 is the SECOND catalog-integration iter, validating that the EXTEND-existing-entries pattern is a repeatable workflow.

## Why 3 more this iter

iter-433 picked the 4 P2HP entries with CLEAREST event-driven architecture (StoryEvent / event-queue / AI-tick / Death-tick). iter-436 picks 3 more with COMBAT-SPECIFIC event-driven architecture (area-damage tick / target-filter tick / OHK damage-multiplier tick). The 3 combat entries share a unifying theme — all touch `CombatantBehaviorClass` directly or its damage-tracking sibling.

After iter-436, the P2HP entries WITHOUT iter-426 rationale are mostly:
- BLOCKED-NO-RVA economy entries (SetIncomeMultiplier / SetBuildSpeed / SetBuildCost / InstantBuild / FreeBuild) — NOT clearly event-driven; would need RE to confirm
- Vestigial mirrors (ChangePlanetOwner / ChangePlanetOwnerWithMode) — already cite LIVE alternatives
- Specific-RVA defers (SetUnitCapOverride iter-249 / SetGameSpeed iter-131) — different defer reason
- LIVE-alternative entries (FreezeCredits iter-251 / SpawnUnit iter-266 / SetHeroRespawnTimer iter-130/380)

So iter-433 + iter-436 together cover the full "obviously event-driven" subset of P2HP entries. Future iter-437+ would need RE to expand further (or shift to NEW catalog entries per iter-435 close-out option 2).

## What shipped

1. **`src/SwfocTrainer.Core/Diagnostics/CapabilityStatusCatalog.cs`** — 3 P2HP entry rationale extensions (~18 LoC added)
2. **`TestResults/iter436_publish_and_test.ps1`** (NEW; mirrors iter-434 PS-script-file pattern) — verify republish wrapper
3. **iter-436 close-out doc** (this file)

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-435 chain (catalog edits are pure rationale-string changes)
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 210 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- 🔄 Editor binary republish in progress (background task `bz1g1i40f`); expected to advance from May 7 14:50 → ~current time
- 🔄 Filtered tests pending; expected 26/0/0 per iter-434 precedent

## Net iter-436 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | ~18 LoC catalog rationale (string-content only) |
| New tools | 1 (iter436_publish_and_test.ps1) |
| Doc shipped | 1 close-out doc |
| Pattern observations | 1 (iter-433 + iter-436 together cover the full "obviously event-driven" P2HP subset; ~7 entries total now have explicit iter-426 references) |
| Catalog entries enhanced | 3 (SetAreaDamage + SetTargetFilter + ToggleOHKAttackPower) |
| Cycle time | ~10 min (3 surgical edits + script + close-out) |

**iter-436 closes the "obviously event-driven P2HP" application set** — between iter-433 (4 entries: lifecycle + AI + story) and iter-436 (3 entries: combat), all P2HP entries with CLEAR `*BehaviorClass` matches now have explicit iter-426 rule rationale. Future application would either need RE-validation of less-obvious entries OR shift to NEW catalog entries.

105th post-iter-323 arc iter (15th post-survey-completion iter); 166th consecutive NON-A1.x iter per iter-269 lesson #2.

## Codification candidate strengthens

iter-432 flagged "2-audit pair when both predicted CLEAN" at 1/3 trigger. Per the iter-433 + iter-436 pattern observation, a NEW codification candidate emerges:

**"Codified-rule application via existing-entry rationale extension"** — ~7 instances now (4 iter-433 + 3 iter-436 = 7 instances of the SAME workflow shape):
1. Identify P2HP entry matching codified rule
2. Extend rationale with rule reference + architectural identification + cost statement + (optional) LIVE alternative
3. ~5-6 LoC per entry

This pattern ALONE has 7 instances within 4 iters (iter-433 batch + iter-436 batch). Per iter-380/iter-388 high-instance-count threshold (7 + 88 instance triggers respectively), this is at codification-trigger threshold for a Tier-1 production rule. Could be iter-437 or future codification target.

## Next iter (iter-437)

Options:

1. **Verify iter-436 republish + tests pass** (cheap; ~3-5 min cycle; mirrors iter-434 pattern)
2. **Codify "Codified-rule application via existing-entry rationale extension"** as 22nd codified rule (~7 instances at iter-433 + iter-436)
3. **NEW arc-class kickoff: SWFOC_TriggerVictory multi-iter A1.x** (~5 iters)
4. **Continue with NEW catalog entries** for less-obvious event-driven candidates (DeathBehaviorClass / CapturePointBehaviorClass / etc.)
5. **Codify "2-audit pair when both predicted CLEAN"** at 2nd instance (would need 2 more for 3-instance trigger)

Recommended: option 1 (verify republish; closes iter-436 cleanly) THEN option 2 (codify the rationale-extension pattern at 7-instance trigger; matches iter-380's 7-instance precedent for Tier-1 codification).
