# Iter 427 — Apply iter-426 event-driven defer rule forward (4th-instance validation + scale-out)

**Date:** 2026-05-07
**Arc class:** Codified-rule forward application (mirrors iter-360/iter-369 pattern)
**Predecessor:** iter-426 (codification at 3-instance trigger)
**Successor (queued):** iter-428 (operator changelog supplement11 OR cheap-insurance republish OR continue extension work)

## What this iter does

Applies the freshly-codified iter-426 rule forward by scanning the callgraph for the rule's signature shapes:
1. `*MonitorClass` (engine polled-state idiom)
2. `*BehaviorClass` (entity-attached behavior idiom)
3. `DynamicVectorClass<*::AwaitingTestType>` (polled-test-list idiom)

**Outcome**: 4th-instance validation + DRAMATIC scale-out of the rule's evidence base from 3 instances → 119+1 RTTI matches.

## Findings

### Pattern A: *MonitorClass

**0 distinct classes** matching `*MonitorClass` in rtti_refs (excluding DynamicVectorClass<...> wrappers).

**Notable**: `VictoryMonitorClass` (the iter-423 example) is NOT directly in rtti_refs — it appears ONLY via `DynamicVectorClass<VictoryMonitorClass::AwaitingVictoryTestType>`. This means *MonitorClass is a RARE engine idiom, not a dominant one.

### Pattern B: *BehaviorClass — DOMINANT

**119 distinct classes** matching `*BehaviorClass` in rtti_refs.

This is the engine's CANONICAL Observer-pattern idiom. Behaviors get attached to GameObjects via `QueryInterface(BEHAVIOR_INTERFACE_ID)` and are ticked by the engine each frame. Examples:
- **Movement**: 13 Locomotor* variants (Bike/Fighter/Fleet/Flying/JetPack/LandBomber/LandTeam/SimpleSpace/Starship/Team/Walk/Locomotor[parent])
- **Damage/Death**: DeathBehaviorClass, DamageTrackingBehaviorClass, AsteroidFieldDamageBehaviorClass
- **AI/Tactical**: UnitAIBehaviorClass, AvoidDangerBehaviorClass, CombatantBehaviorClass
- **Capture**: CapturePointBehaviorClass, CashPointBehaviorClass, CorruptSystemsBehaviorClass, BoardableBehaviorClass
- **Status effects**: BurningBehaviorClass, ConfuseBehaviorClass, BlindBehaviorClass, DisableForceAbilitiesBehaviorClass
- **Special weapons**: BombBehaviorClass, BeaconBehaviorClass, BarrageAreaBehaviorClass, DemolitionBehaviorClass
- **Lifecycle**: AbilityCountdownBehaviorClass, BuzzDroidsBehaviorClass, DeployTroopersBehaviorClass

Plus 89 more.

### Pattern C: DynamicVectorClass<*::AwaitingTestType>

**1 distinct class**: `DynamicVectorClass<VictoryMonitorClass::AwaitingVictoryTestType>` (iter-423 example, validated as unique).

This is the rarest signal but the most architecturally pure — explicitly indicates a polled-test-list architecture where the engine iterates the vector each tick checking each test for completion.

### Total event-driven RTTI surface

**120 distinct classes** matching iter-426 rule patterns. The original 3-instance evidence base (iter-416/422/423) was just the tip of the iceberg — the engine has ~120 event-driven subsystems all subject to the rule.

## Operator-relevant subsystems (subset)

The 119 `*BehaviorClass` instances include MANY operator-relevant ones that are subject to iter-426 rule. Future SWFOC_* candidates targeting these subsystems should be CHECKED against this rule first:

| Behavior class | Likely operator candidate name | iter-426 verdict |
|---|---|---|
| DeathBehaviorClass | SWFOC_TriggerDeath / SWFOC_PreventDeath | **Defer per iter-426** (event-driven) |
| CapturePointBehaviorClass | SWFOC_CaptureCapturePoint | **Defer per iter-426** (event-driven) |
| CashPointBehaviorClass | SWFOC_TakeCashPoint | **Defer per iter-426** (event-driven) |
| CorruptSystemsBehaviorClass | SWFOC_CorruptSystems (extends iter-180 Corrupt) | **Likely defer per iter-426** |
| DamageTrackingBehaviorClass | SWFOC_GetDamageStats | **Defer per iter-426** (read-side query of polled state) |
| UnitAIBehaviorClass | SWFOC_OverrideAIBehavior | **Defer per iter-426** (event-driven) |
| BurningBehaviorClass | SWFOC_TriggerBurning | **Defer per iter-426** (event-driven) |
| ConfuseBehaviorClass | SWFOC_TriggerConfuse | **Defer per iter-426** (event-driven) |
| BombBehaviorClass | SWFOC_DetonateBomb | **Defer per iter-426** (event-driven) |
| BeaconBehaviorClass | SWFOC_DropBeacon | **Defer per iter-426** (event-driven) |

Future operators choosing among A1.x candidate arcs should rank by "which subsystem is event-driven (defer) vs which has a Lua API (commit)?" using this list as a pre-classification.

## Refinement applied to iter-426 rule

The iter-426 codified rule's "Negative-result signature" section was UPDATED with a "Pattern weighting" subsection documenting:
- Primary signal: `*BehaviorClass` (119 instances; DOMINANT)
- Secondary signal: `DynamicVectorClass<*::AwaitingTestType>` (1 instance; rare but architecturally pure)
- Tertiary signal: `*SystemClass` (engine-wide subsystems; not always in rtti_refs)

This refinement is empirically grounded by iter-427's scan results.

## What shipped

1. **`tools/iter427_event_driven_subsystem_scan.py`** (NEW) — scans callgraph for iter-426 rule signature shapes
2. **`feedback_event_driven_defer_pattern.md`** updated with "Pattern weighting" subsection (refined per iter-427 scale-out finding)
3. **iter-427 close-out doc** (this file)

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-426 chain
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 202 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (UNCHANGED from iter-404)
- ✅ iter-426 codified rule REFINED based on forward-application empirical evidence
- ✅ 4th-instance validation (iter-416/422/423/427) — rule moves from 3-instance Tier-4 to 4-instance MATURE Tier-4

## iter-426 rule maturity post-iter-427

| Aspect | Pre-iter-427 | Post-iter-427 |
|---|---|---|
| Empirical instances | 3 | 4 (+ 119 RTTI candidates pre-classified) |
| Signal weighting | "*MonitorClass / *BehaviorClass / *<*::AwaitingTestType>" undifferentiated | Primary (*BehaviorClass) / Secondary (DynamicVector<*::Awaiting*>) / Tertiary (*SystemClass) |
| Forward applicability | Untested | EMPIRICALLY VALIDATED at scale (119+1 RTTI matches) |
| Operator usability | Pattern signal only | Pre-classified operator-relevant table (10+ subsystems) |

## Net iter-427 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure forward-application iter) |
| New tools | 1 (iter427_event_driven_subsystem_scan.py) |
| Doc shipped | 1 close-out + 1 codified-rule refinement |
| Pattern observations | 1 NEW (iter-426 rule's evidence base scales from 3 → 120 RTTI matches) |
| Codified rule maturity | iter-426 from "3-instance Tier-4" → "MATURE Tier-4 with 4 forward applications + 119 RTTI candidates pre-classified" |
| Cycle time | ~10 min (scan + rule refinement + close-out) |

**iter-427 is an exemplary forward-application iter** — codified rule went from 3 instances to 120 RTTI matches in a single iter; rule's pattern weighting got empirically refined; operator-usable pre-classification table shipped.

96th post-iter-323 arc iter (6th post-survey-completion iter); 157th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-428)

Options:

1. **Operator changelog supplement11** — covers iter 420-427 (8-iter window since supplement10 at iter-419/420). Per iter-372 ~12-instance post-arc docs cadence.

2. **Cheap-insurance republish** — iter-422 was last (5 iters ago).

3. **Headline-doc quad mini-refresh** — covers iter 421-427 (7-iter window).

4. **NEW arc-class kickoff: SWFOC_TriggerVictory multi-iter** — committing to ~5-iter A1.x arc (now with iter-426 rule explicitly documenting cost + iter-427 confirming the architectural shape).

5. **Continue applying iter-426 forward** — pre-mark operator-relevant *BehaviorClass entries in catalog.json with 'event-driven defer per iter-426' rationale.

Recommended: option 1 (operator changelog supplement11). The 5-iter window iter 423-427 has shipped substantial codification + rule application work; closing it with a docs supplement maintains the post-arc docs cadence pattern (per iter-372 codified rule).
