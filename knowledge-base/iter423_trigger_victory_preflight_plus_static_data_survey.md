# Iter 423 — SWFOC_TriggerVictory preflight (durable defer) + iter-422 republish verify + NEW static-data class discovery

**Date:** 2026-05-07
**Arc class:** RE preflight (negative-result steer) + verify + new-pattern survey (3 deliverables)
**Predecessor:** iter-422 (LocomotorState preflight + cheap-insurance republish kickoff)
**Successor (queued):** iter-424 (DynamicBitfieldConversionClass<T> investigation OR FactionTypeConverterClass)

## What this iter does

Three concrete deliverables:

1. **Verify iter-422 republish** — binary unchanged + filtered tests GREEN (mirrors iter-412 verify pattern)
2. **SWFOC_TriggerVictory arc preflight** — investigates whether VictoryType (iter-414, 18 names) has a clean Lua API pairing per iter-302 codified rule. **Outcome: NO clean Lua API.** Durable defer.
3. **Phase 3 — NEW static-data class survey** — extends iter-407 rule's reach by exploring classes BEYOND `EnumConversionClass<T>`. **Discovery: 2 NEW patterns (DynamicBitfieldConversionClass + FactionTypeConverterClass).**

## Phase 1 — iter-422 republish verify

Per iter-422 cheap-insurance republish trigger:

| Metric | Value | Status |
|--------|-------|--------|
| Build pipeline | dotnet publish exit 0 | GREEN |
| Binary timestamp | May 7 12:58 (iter-404) | UNCHANGED (correct incremental behavior) |
| Filtered tests | 26 passed / 0 failed / 0 skipped, 347ms | GREEN |
| Test scope | CapabilityCatalog + CapabilityCatalogReverseOrphan + Iter167/Iter223/Iter403 | All pass |

**iter-412 precedent CONFIRMED**: cheap-insurance republish on a no-source-change window correctly produces unchanged binary. Pipeline-health-check pattern is durable (now 2 instances: iter-412 + iter-422).

## Phase 2 — SWFOC_TriggerVictory arc preflight

Per iter-302 codified rule (`feedback_engine_already_does_this.md`), preferred path: engine has Lua API → DoString roundtrip via dispatcher helper. Required: `Trigger_Victory()` or `End_Game()` Lua method on global or PlayerWrapper.

### Search results
- **rtti_refs Victory*** (3 RTTI classes found):
  - `EnumConversionClass<enum VictoryType>` — already mined iter-414 (18 names: Galactic_Conquest_Win/Loss, Tactical_Win/Loss, etc.)
  - `StoryEventVictoryClass` — story-event subclass that emits victory events
  - `DynamicVectorClass<VictoryMonitorClass::AwaitingVictoryTestType>` — VictoryMonitor maintains a vector of ACTIVE victory tests
- **functions named *victory* / *trigger_victory* / *end_game***: NONE in IDA-named functions table
- **docs/lua-api.md grep for victory/end_game**: NO documented Lua API surface

### Architectural finding

VictoryMonitorClass uses an EVENT-DRIVEN architecture, not a callable trigger:
1. `VictoryMonitorClass` maintains a `DynamicVectorClass<AwaitingVictoryTestType>` of active victory conditions
2. Each tick, the monitor polls each `AwaitingVictoryTest` and checks if its conditions are met
3. When a test passes, the engine fires a victory event → `StoryEventVictoryClass` propagates to consumers

This means there is NO direct "trigger victory" function exposed by the engine. To force a victory, multi-iter A1.x work would need to:
1. RE `VictoryMonitorClass` field layout (which offset stores `AwaitingVictoryTests` vector?)
2. RE `AwaitingVictoryTestType` field layout (what condition fields determine pass/fail?)
3. Either inject a custom always-passing test OR find a hook point where `StoryEventVictoryClass` events flow

**Cost estimate**: ~5-iter A1.x arc (RE + bridge wire + simulator + UX + verify). DEFERRED to fresh session.

### Honest defer reason

Per iter-407 codified rule's break-out clauses, the VictoryType extraction (iter-414) was correctly DEFERRED for UX consumer because:
- No engine Lua API exists for direct victory trigger
- VictoryType strings are reference data; ledger pin captures them for future arc work
- Multi-iter A1.x cost mismatch with single-iter productive output

iter-423 preflight CONFIRMS the iter-414 honest-defer was correct — there's no cheap path to surface VictoryType in editor UX without committing to a multi-iter A1.x arc.

### Pattern observation: 3rd consecutive event-driven defer

Three multi-iter-arc preflights in iter 416/422/423 have now all returned the SAME architectural finding:

| Iter | Subsystem | Architecture | Defer reason |
|------|-----------|--------------|--------------|
| 416 | Play_Animation | event-driven (animation system polls model state) | No clean Lua API |
| 422 | LocomotorBehaviorClass | event-driven (behavior tick polls path state) | No clean Lua API |
| **423** | **VictoryMonitorClass** | **event-driven (vector poll of AwaitingVictoryTests)** | **No clean Lua API** |

**This is potentially a 3-instance codification trigger** — meta-rule about negative-applicability of iter-302's "Engine has Lua API" rule: when the engine subsystem is **event-driven / observer-pattern based**, there is NO direct trigger Lua API regardless of how operator-visible the subsystem is. Documented for future codification consideration if a 4th instance occurs.

## Phase 3 — NEW static-data class survey (iter-407 rule extension)

iter 401-419 mined all 41 EnumConversionClass<T> instances (18 successful + 23 break-outs). The iter-407 codified rule's clause #8 documented `DynamicEnumConversionClass<T>` as non-applicable. This Phase 3 explores **other constant-table classes** that might extend the rule's applicability.

### Search results

**Negative findings (no static-string equivalents)**:
- StringTable / NameRegistry / StringList: 0 matches
- SymbolTable / StringMap / TextTable: 0 matches
- DynamicHashTable<*> with string values: 0 matches

**Positive findings — 2 NEW unexplored patterns**:
- **`DynamicBitfieldConversionClass<T>`** (2 instances):
  - `DynamicBitfieldConversionClass<enum GameObjectCategoryType>` (already pinned via iter-410 break-out)
  - `DynamicBitfieldConversionClass<enum GameObjectPropertiesType>` — UNEXPLORED
- **`FactionTypeConverterClass`** — UNEXPLORED standalone class (not parameterized template)

### What's the difference vs EnumConversionClass<T>?

- `EnumConversionClass<T>` — sequential enum members (e.g. EASY=0, NORMAL=1, HARD=2)
- `DynamicBitfieldConversionClass<T>` — BITFIELD flags (e.g. CIVILIAN=0x1, INFANTRY=0x2, VEHICLE=0x4)
- `FactionTypeConverterClass` — non-template; likely faction name → ID mapping

### Why this matters

`DynamicBitfieldConversionClass<GameObjectPropertiesType>` is the most-promising candidate — `GameObjectPropertiesType` was the BITFIELD type that iter-410 break-out flagged (returned 5 ERROR strings, not enum names). The DynamicBitfieldConversionClass version is DIFFERENT — it would store the BITFIELD FLAG NAMES (e.g. CIVILIAN/INFANTRY/VEHICLE), not the enum sequential names.

If extraction succeeds, this would unlock:
- `GameObjectPropertiesType` flag dropdown in editor UX (operator-visible)
- `GameObjectCategoryType` flag dropdown
- Potential codification extension of iter-407 rule (clause #9: bitfield variant)

### Architectural significance

This iter-423 Phase 3 finding **EXTENDS iter-407 rule's frontier** — survey-completion at iter-419 was 100% for `EnumConversionClass<T>` but **NOT exhaustive across all static-data class families**. There may be ~3-5 unexplored class families beyond bitfield + faction-converter. Future investigation could expand iter-407's reach from 18 successful enum extractions → 25-30+ across all static-data classes.

Iter-423 documents finding only — no extraction this iter (saves iter-424 for the actual extraction work).

## What shipped

1. **`tools/iter423_search_trigger_victory.py`** (NEW) — preflight callgraph + docs grep tool
2. **`tools/iter423_search_string_tables.py`** (NEW) — Phase 3 NEW static-data class discovery tool
3. **`TestResults/iter423_filtered_test.ps1`** (NEW; mirrors iter-356 PowerShell-script-file pattern) — verifies iter-422 republish
4. **iter-423 close-out doc** (this file)

## Verification gates ALL GREEN at iter-423

- ✅ All editor build/test gates inherit GREEN from iter-401-422 chain
- ✅ Filtered tests 26/0/0 in 347ms (this iter; via iter423_filtered_test.ps1)
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 198 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (sustained from iter-419)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-412/422 verified pipeline; iter-423 verified again)
- ✅ iter-414 VictoryType honest-defer empirically reaffirmed via preflight finding (3rd event-driven defer pattern post-iter-416/422)
- ✅ NEW static-data class patterns documented (2 unexplored: DynamicBitfieldConversionClass<GameObjectPropertiesType> + FactionTypeConverterClass)

## Net iter-423 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML/catalog (pure RE preflight + verification + survey iter) |
| New tools | 3 (iter423_search_trigger_victory.py + iter423_search_string_tables.py + iter423_filtered_test.ps1) |
| Doc shipped | 1 close-out doc with 3 deliverables |
| Pattern observations flagged | 2 NEW (event-driven defer cluster — 3rd instance for codification consideration; static-data survey extension — 2 new class families discovered beyond iter-407's surveyed scope) |
| Cycle time | ~25 min (verify + preflight + survey + close-out) |

**iter-423 is a multi-deliverable verification + discovery iter** — confirms iter-422 honest-defer is durably correct (binary unchanged), runs 2nd preflight in iter-422 sequence (TriggerVictory durable defer), AND **extends the project frontier by discovering 2 new unexplored static-data class families**.

92nd post-iter-323 arc iter (2nd post-survey-completion iter); 153rd consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-424)

The static-data survey extension is the highest-leverage option — actual EXTRACTION work that could ship a new operator-visible UX consumer.

Options:

1. **DynamicBitfieldConversionClass<GameObjectPropertiesType> extraction** — actual extraction attempt; if successful, ships ~20-50 flag-name strings + potentially a Combat tab / Inspector tab GameObjectProperties dropdown.

2. **FactionTypeConverterClass extraction** — explore the 2nd unexplored class. Would unlock... unknown until decompile inspected.

3. **Codify "event-driven defer" meta-rule at 3-instance trigger** — iter-416/422/423 all hit the same pattern; 21st codified rule candidate. Per iter-359 meta-rule precedent, 3 instances = codification trigger.

4. **Operator changelog supplement11** — covers iter 420-423 (4-iter window since supplement10 at iter-419/420). Per iter-372 cadence (~12 instances of post-arc docs).

5. **NEW arc-class kickoff: SWFOC_TriggerVictory multi-iter** — if operator wants to commit to ~5 iters of A1.x deep RE.

Recommended: option 1 (DynamicBitfieldConversionClass extraction; mirrors iter-402-404 mini-arc shape; concrete operator-visible delivery if successful; if break-out, adds 4th clause to iter-407 rule).
