# Iter 416 — Play_Animation defer confirmation + 3rd-tier XML codification kickoff (1/3 instance documented)

**Date:** 2026-05-07
**Arc class:** RE preflight (negative result) + new codification track kickoff
**Predecessor:** iter-415 (supplement9 changelog)
**Successor (queued):** iter-417 (TBD per "Next iter" below)

## What this iter does

Two concrete deliverables in one iter:

1. **Play_Animation arc preflight** (negative finding) — confirms iter-405 ModelAnimType honest-defer is durably correct: no clean SWFOC_PlayAnimationLua candidate exists in the binary.

2. **3rd-tier XML codification kickoff** — documents iter-300 SWFOC_ListMods + iter-294 mod-CRC32 work as 1st implicit instance of "engine has FILESYSTEM/XML data → walk directory" pattern. Queues future XML-config arcs (when 3 instances accumulate, codify as 21st rule).

## Phase 1 — Play_Animation preflight (negative result)

Searched callgraph `rtti_refs` table for animation-related classes:

```
=== rtti_refs containing 'Anim' ===
  CinematicAnimationEventClass
  UnitAnimationBlockStatus
  CameraAnimeSpiralEffectClass
  DynamicVectorClass<alAnim *>
  DynamicVectorClass<alRawAnim::alRawJointAnim>
  EnumConversionClass<enum ModelAnimType>
  GUIComponentPlayAnimClass
  GUIComponentStopAnimClass
  ModelAnimsListClass
  alQuantizedAnim
  alRawAnim
```

**Analysis**:
- `CinematicAnimationEventClass` → event-driven cinematic animation (not directly callable by name)
- `GUIComponentPlayAnimClass` / `GUIComponentStopAnimClass` → for GUI element animations, NOT unit animations
- `ModelAnimsListClass` / `alAnim` / `alRawAnim` → animation data containers, not dispatchers
- `UnitAnimationBlockStatus` → status tracker, not setter

**No clean SWFOC_PlayAnimationLua candidate** exists. The engine's animation system is event-driven (CinematicAnimationEventClass) or state-machine-driven (LocomotorStateType iter-414); operator-by-name triggering would require hooking the animation dispatch system at the C++ level (probably a multi-iter A1.x-style arc with MinHook detour, not a 3-iter mini-arc parallel to iter-402-404).

**Decision**: iter-405 ModelAnimType honest-defer is durably correct. ModelAnimType's 111 names remain captured for ground truth (ledger pin at iter-405) but UX consumer DEFERRED indefinitely OR until a future deep RE arc on the animation-dispatch system.

This is a **valid negative-result** for the Play_Animation arc preflight. Per the iter-407 codified rule's break-out clauses, "no engine Lua API exists" is a clean honest-defer; the rule's discipline holds.

## Phase 2 — 3rd-tier XML codification kickoff (1/3 trigger)

Per iter-411 implied 3rd-tier candidate, the engine-already-does-this taxonomy needs explicit 3rd-tier codification when 3 instances accumulate:

| Tier | Pattern | Recipe | Codified |
|---|---|---|---|
| 1 (iter-302) | Engine has Lua API → DoString roundtrip | ~30-50 LoC bridge wire | **YES** (`feedback_engine_already_does_this.md`) |
| 2 (iter-407) | Engine has STATIC DATA → extract once at RE time | ~5-10 min via `extract_enum_conversion_strings.py` | **YES** (`feedback_static_data_re_extraction.md`) |
| **3 (iter-416)** | **Engine has FILESYSTEM/XML data → walk directory + parse** | TBD: walk `<game-data-dir>/data/xml/` or similar | **1/3 trigger** (needs 2 more instances) |

### 1st instance of 3rd-tier pattern (documented retroactively)

iter-300 SWFOC_ListMods + iter-294 mod-CRC32 work shipped a **filesystem walker + content-hash mechanism** that exemplifies the 3rd-tier pattern:

| Element | iter-300 SWFOC_ListMods | iter-294 mod-CRC32 |
|---|---|---|
| Source | Engine's `Mods/` directory enumeration | Engine's mod XML manifest content |
| Method | Filesystem walk + DLL handle | CRC32 hash over file content |
| Output | List of mod identifiers | Content-fingerprint hash |
| UX consumer | Settings tab mod-picker (iter-301-303) | Save-game integrity validator (iter-298) |
| Operator value | HIGH (dropdown for active mod selection) | HIGH (detects savegame/mod mismatch) |
| Bridge surface | `SWFOC_ListMods` Lua wire (iter-300) | mod-CRC32 service (iter-290) |

**Pattern shape**: walk filesystem hierarchy + parse format-specific content + emit operator-meaningful list/hash. NO RVA pin needed (filesystem isn't part of binary); NO MinHook detour (read-only); NO Lua DoString (filesystem is Win32-direct).

### 2nd instance candidate (DynamicEnumConversionClass XML config)

Per iter-411 finding, all 5 DynamicEnumConversionClass instances (AIGoalCategoryType / MovementClassType / ObjectWeatherCategoryType / PerceptionTokenType / SurfaceFXTriggerType) are XML-loader template instantiations. The actual enum-name-to-value mappings live in `data/xml/<typename>.xml` config files.

If a future iter walks `data/xml/` and extracts these enum mappings, that's the **2nd instance** of the 3rd-tier pattern.

### 3rd instance candidate (game-side XML asset enumerations)

iter-313 LocateByConvention plugin set walks `data/icons/` for sprite assets. While that's image files (not XML), the pattern is similar: walk filesystem + extract operator-meaningful identifiers. Could count as 3rd instance if the rule generalizes to "filesystem walks" broadly OR could remain Tier-2-distinct if specifically about XML.

**Decision pending iter-417+**: when 2nd instance ships (e.g. DynamicEnumConversionClass XML config extraction), iter-X will codify `feedback_filesystem_data_extraction.md` as the 21st codified rule + 6th Tier-1 production.

## What shipped

1. **`tools/iter416_search_play_animation.py`** (NEW) — searches callgraph for animation-related RTTI; documents the negative-result preflight
2. **iter416 close-out doc** (this file) — documents Play_Animation defer + 3rd-tier codification design

## Verification gates ALL GREEN

- ✅ 0 source/test/catalog edits (pure RE preflight + design doc iter)
- ✅ All gates inherit GREEN from iter-401-415 chain
- ✅ Bridge harness 1100/0; verifier ledger lint 0/0 at 328 entries
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-412 verified pipeline)
- ✅ iter-405 ModelAnimType honest-defer reaffirmed empirically

## Net iter-416 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/XAML/catalog (pure design + preflight iter) |
| New tools | 1 (iter416_search_play_animation.py) |
| Doc shipped | 1 close-out doc with 3rd-tier codification design + Play_Animation preflight findings |
| Pattern observations flagged | 3rd-tier "engine has FILESYSTEM/XML data" pattern formally documented at 1/3 trigger |
| Cycle time | ~12 min (animation preflight + 3rd-tier design + close-out) |

**iter-416 is a strategic preflight + queue-future iter** — confirms the iter-405 honest-defer is durably correct AND queues the 3rd-tier codification track for when 2 more instances accumulate.

85th post-iter-323 arc iter (1st 3rd-tier codification design iter); 146th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter (iter-417)

Options:

1. **Continue EnumConversionClass extractions** — pattern is mature; remaining ~17 candidates compound iter-407 evidence base further.

2. **2nd instance of 3rd-tier pattern** — extract DynamicEnumConversionClass XML config files (e.g., walk `data/xml/aigoaltype.xml` or similar). Would compound 1/3 → 2/3 toward 21st codified rule.

3. **Live SWFOC verify** of iter-403 ComboBox.

4. **Cheap-insurance republish + filtered test verify**.

5. **NEW arc-class via DEEP animation-dispatch RE** (multi-iter; ~10 iters; high cost). Defer to fresh session.

iter-417 likely option 1 (cheap continuation) OR option 2 (compound 3rd-tier track toward codification).
