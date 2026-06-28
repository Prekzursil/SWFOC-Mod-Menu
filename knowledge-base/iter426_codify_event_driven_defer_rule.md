# Iter 426 — Codify event-driven defer rule (21st codified rule)

**Date:** 2026-05-07
**Arc class:** Codification at 3-instance trigger (mirrors iter-407/iter-359/iter-368/iter-371/iter-373/iter-374/iter-380/iter-388 pattern)
**Predecessor:** iter-425 (FactionTypeConverterClass extraction + clause #6 generalization)
**Successor (queued):** iter-427 (apply iter-426 rule forward by pre-marking deferred candidates OR operator changelog supplement11)

## What this iter does

Codifies the iter-416/422/423 architectural pattern as the **21st codified rule** in the project's master-loop codification track:

**`feedback_event_driven_defer_pattern.md`** — When SWFOC engine subsystem is event-driven (Observer pattern, polled state machine, behavior-attached components, monitor-class polling lists), there is NO direct trigger Lua API. Defer to multi-iter A1.x offset RE — never a 3-iter mini-arc.

## Codification trigger evidence (3/3)

Three instances at the same architectural pattern:

| Iter | Subsystem | Architectural shape | RTTI signal |
|---|---|---|---|
| 416 | Play_Animation | event-driven (animation system polls model state) | ModelAnimationClass + AnimationSystemClass |
| 422 | SWFOC_GetUnitLocomotorState | event-driven (LocomotorBehaviorClass polls path state) | 16 Locomotor* classes inheriting LocomotorBehaviorClass |
| 423 | SWFOC_TriggerVictory | event-driven (VictoryMonitorClass polls AwaitingVictoryTests) | VictoryMonitorClass + StoryEventVictoryClass |

**Pattern**: ALL 3 instances had the same RTTI shape — `*MonitorClass` / `*BehaviorClass` / `*<*::AwaitingTestType>` — engine's idiom for Observer-pattern subsystems.

## Why this rule matters

Three architectural insights captured:

1. **Negative-applicability companion to iter-302**: iter-302 covers "engine HAS Lua API → DoString roundtrip"; iter-426 covers "engine LACKS Lua API because event-driven → defer or A1.x commit". Together they form the complete decision-making framework.

2. **Lookup table for iter-337 preflight**: Without iter-426, every iter-337 preflight returning "no clean Lua API" required reasoning from scratch about what the negative signal meant. With iter-426, the negative signal maps to a known architectural pattern.

3. **Project's "what does the engine provide" taxonomy is now COMPLETE**:
   - **iter-302** — engine has command-class Lua API (camera/spawning/diplomacy/audio/UI)
   - **iter-407** — engine has STATIC DATA (EnumConversionClass<T> populators)
   - **iter-426** — engine has EVENT-DRIVEN STATE (Observer-pattern subsystems; no direct trigger Lua API)

Future iter-strategy decisions can now cleanly map to one of these 3 categories using the iter-337 preflight stack.

## What shipped

1. **`~/.claude/projects/.../memory/feedback_event_driven_defer_pattern.md`** (NEW; ~120 LoC) — 21st codified rule
2. **`~/.claude/projects/.../memory/MEMORY.md`** — added 1-line entry pointing to the new rule (44th index entry)
3. **iter-426 close-out doc** (this file)

## Verification gates

- ✅ All editor build/test gates inherit GREEN from iter-401-425 chain
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 201 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (UNCHANGED from iter-404)
- ✅ Memory rule file written + MEMORY.md index entry added cleanly
- ✅ NEW rule file follows the codification template (rule + why + how to apply + examples + cost-benefit + cross-reference + codification-trigger)

## 21-rule codification track summary

| Rule # | Tier | Iter | Title | Trigger basis |
|---|---|---|---|---|
| 1 | 2 | 256 | AOB drift across binary versions | 1 instance |
| 2 | 1 | 283 | Bidirectional infra-claim drift | 2 instances |
| 3 | 1 | 293 | Iterative deferral keeps velocity | 6-iter pattern |
| 4 | 1 | 302 | Engine-already-does-this primitive shortcut | 6 instances |
| 5 | 1 | 311 | Optional-default-null ctor extension | 3 instances |
| 6 | 1 | 311 | Status badge as inline operator docs | 3 instances |
| 7 | 1 | 316 | Extract on second use | 3 instances |
| 8 | 1 | 334 | LocateByConvention plugin set extension | 6 instances |
| 9 | 1 | 337 | Iter-strategy preflight stack | 3 instances |
| 10 | 1 | 345 | Resolver-injection-at-composition-root | 8 instances |
| 11 | 1 | 380 | Stale-groupbox-header-drift | 7 instances |
| 12 | 1 | 388 | Internal-codename-in-tooltips-drift | 88 instances |
| 13 | 1 | 407 | Static-data RE extraction | 3 instances + 100% survey closure |
| 14 | 4 | 359 | Audit-compounds-via-rationale-extensions | 2 instances Tier 4 |
| 15 | 4 | 363 | Codify-then-apply-then-verify quad | 2 instances Tier 4 |
| 16 | 4 | 368 | Audits-clean-when-no-new-wires | 2 instances Tier 4 |
| 17 | 4 | 371 | Audit-prep force multiplier | 2 instances Tier 4 |
| 18 | 4 | 373 | Codified rule self-validates via forward application | 2 instances Tier 4 |
| 19 | 4 | 374 | Advance audit cadence when predicted CLEAN | 2 instances Tier 4 |
| 20 | 4 | (composite) | (audits-related cluster ends here) | — |
| **21** | **4** | **426** | **Event-driven subsystem defer pattern** | **3 instances Tier 4** |

**iter-426 closes the "what does the engine provide" taxonomy + extends the Tier-4 meta-rule track to 7 rules** (was 6 at iter-374; iter-426 is 7th Tier-4).

## Net iter-426 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure codification iter) |
| New tools | 0 |
| Doc shipped | 1 NEW codified rule (~120 LoC) + 1 MEMORY.md index entry + 1 close-out doc |
| Pattern observations | 1 NEW rule capturing 3-instance pattern; "what does the engine provide" taxonomy now complete (3 categories) |
| Codified rules total | 20 → **21** |
| Cycle time | ~15 min (rule writing + index update + close-out) |

**iter-426 is a high-meta-value codification iter** — captures a durable architectural insight that future iter-strategy decisions will reference for years. Closes the iter-302 → iter-407 → iter-426 taxonomy at 3 categories matching SWFOC's actual engine architecture.

95th post-iter-323 arc iter (5th post-survey-completion iter); 156th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-427)

Per iter-373 codified rule (codified rule self-validates via forward application), the natural next move is to APPLY iter-426 forward by:

Options:

1. **Apply iter-426 rule forward by pre-marking deferred candidates** — scan codebase for *MonitorClass / *BehaviorClass / DynamicVector<*::AwaitingTestType> RTTI references; pre-classify which deferred candidates fit the pattern; mark them in catalog/STATUS for future operators. Validates rule applicability at 4th instance.

2. **Operator changelog supplement11** — covers iter 420-426 (7-iter window since supplement10 at iter-419/420). Per iter-372 ~12-instance post-arc docs cadence.

3. **Cheap-insurance republish** — iter-422 was last (5 iters ago).

4. **Headline-doc quad mini-refresh** — covers iter 421-426 (6-iter window).

5. **NEW arc-class kickoff: SWFOC_TriggerVictory multi-iter** — committing to ~5-iter A1.x arc (NOW with the iter-426 rule explicitly documenting the cost).

Recommended: option 1 (apply iter-426 forward) — mirrors iter-360/iter-369/iter-373 pattern of applying a freshly-codified rule the very next iter. Validates that the rule is actionable, not just descriptive.
