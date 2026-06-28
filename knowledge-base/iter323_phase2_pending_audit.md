# iter-323 Phase2HookPending re-audit (5th audit in series)

**Date:** 2026-05-07
**Cadence:** ~16-iter cadence per iter-132/iter-221/iter-250/iter-266/iter-274 lineage. iter-274 was the previous audit; iter-323 is **49 iters past** — significantly overdue. The window covered the Thread B Overlay Phase 2-full + Thread C Savegame + Thread D Asset Extraction + Thread D UI Integration (5-tab) arcs.

## Catalog state
- **Phase2HookPending entries**: **24** (down from 26 → 25 at iter-237 SetCameraPos flip → 24 at iter-296 GetPlanets flip — both caught in iter-317 inline drift fixes)
- **Total catalog entries**: 318 (verifier ledger lint inherits 0/0)
- **LIVE-flip rate this window**: ~10 catalog entries promoted to LIVE since iter-274 (dispatcher matrix + multi-arg helpers + namespace expansion + read-side getter expansion)

## Triage table (all 24 entries)

| # | SWFOC_* entry | Last triage iter | Current status | Audit conclusion | Drift? |
|---|---------------|------------------|----------------|------------------|--------|
| 1 | SWFOC_ChangePlanetOwnerWithMode | iter-137 vestigial-→-Phase1-mirror | Operator-callable but Phase-1 only | DEFER (galactic state API not pinned) | N |
| 2 | SWFOC_SpawnAsStoryArrival | iter-137 vestigial-→-Phase1-mirror | Operator-callable but Phase-1 only | DEFER (story-arrival kit not pinned) | N |
| 3 | SWFOC_EventControl | iter-274 confirmed defer | Phase-1 mirror; no engine API | DEFER (event-bus API not in ledger) | N |
| 4 | SWFOC_SetIncomeMultiplier | iter-274 confirmed defer | Phase-1 mirror | DEFER (no setter found per iter-230 FreezeCredits arc; scope similar) | N |
| 5 | SWFOC_SetGameSpeed | iter-131 confirmed defer | Phase-1 mirror | DEFER (no game-speed/time-scale ledger entries) | N |
| 6 | SWFOC_FreezeCredits | iter-251 rationale fix | Phase-1 mirror | **REVIEW**: iter-231 shipped `Hook_AddCredits` LIVE wire; check if `SWFOC_FreezeCredits` is the reverse-orphan now (iter-282 bidirectional drift pattern). |  ⚠️ |
| 7 | SWFOC_SetBuildSpeed | iter-274 confirmed defer | Phase-1 mirror | DEFER (build-speed multiplier not in ledger; pattern matches SetUnitField defers iter-242+) | N |
| 8 | SWFOC_SetDamageMultiplier | iter-132/iter-274 confirmed defer | Phase-1 mirror per-slot | **REVIEW**: iter-96 shipped `SetDamageMultiplierGlobal` LIVE; iter-154 shipped `Set_Damage_Modifier` per-unit LIVE. Per-slot setter may be drift candidate now. | ⚠️ |
| 9 | SWFOC_SetFireRate | iter-228 confirmed defer (after 5-iter A1.3 arc) | Phase-1 mirror per-slot | DEFER (iter-225 shipped global LIVE; per-slot needs WeaponClass RTTI walk per iter-101) | N |
| 10 | SWFOC_SetAreaDamage | iter-274 confirmed defer | Phase-1 mirror | DEFER (area-damage modifier not in ledger) | N |
| 11 | SWFOC_SetTargetFilter | iter-274 confirmed defer | Phase-1 mirror | DEFER (target-filter bitmask flags not enumerated) | N |
| 12 | SWFOC_ToggleOHKAttackPower | iter-274 confirmed defer | Phase-1 mirror | DEFER (OHK toggle is composite of iter-154 SetDamageModifier — operator already has the per-unit primitive) | N |
| 13 | SWFOC_FreezeAI | iter-274 confirmed defer | Phase-1 mirror | DEFER (AI-tick suspension at engine-level not pinned; iter-162 Suspend_AI LIVE is the per-call alternative) | N |
| 14 | SWFOC_FreeCam | iter-106 confirmed defer | Phase-1 mirror | DEFER (free-cam mode toggle not in ledger; iter-107/143/144/145 camera primitives are the per-call alternative) | N |
| 15 | SWFOC_SpawnUnit | iter-274 confirmed defer | Phase-1 mirror | **REVIEW**: iter-109 shipped `Spawn_Unit` Lua API LIVE (tactical) + iter-152 GalacticSpawnUnit LIVE + iter-185 spawn variants LIVE. May be drift candidate. | ⚠️ |
| 16 | SWFOC_SetBuildCost | iter-274 confirmed defer | Phase-1 mirror | DEFER (build-cost multiplier not in ledger; matches SetUnitField defer pattern) | N |
| 17 | SWFOC_SetUnitCapOverride | iter-249 confirmed defer | Phase-1 mirror | DEFER (iter-249 RE walk failed to find canonical reader; 5-iter arc honest-deferred) | N |
| 18 | SWFOC_InstantBuild | iter-274 confirmed defer | Phase-1 mirror | DEFER (build-time override not in ledger) | N |
| 19 | SWFOC_FreeBuild | iter-274 confirmed defer | Phase-1 mirror | DEFER (composite of iter-#16 SetBuildCost — operator gets it free if SetBuildCost ships) | N |
| 20 | SWFOC_ChangePlanetOwner | iter-134 confirmed defer | Phase-1 mirror | DEFER (galactic state API not pinned per iter-134; sibling to #1) | N |
| 21 | SWFOC_GetPlanetTechAndBuildings | iter-274 confirmed defer | Phase-1 mirror | **REVIEW**: iter-296 shipped `SWFOC_GetPlanets` LIVE via `Find_All_Objects_Of_Type`. Tech + Buildings may now be derivable per-planet. | ⚠️ |
| 22 | SWFOC_ListHeroes | iter-274 confirmed defer | Phase-1 mirror | **REVIEW**: iter-179 shipped `Find_All_Objects_Of_Type` LIVE — could enumerate hero category. Pattern matches iter-296 GetPlanets. | ⚠️ |
| 23 | SWFOC_SetHeroRespawnTimer | iter-130 confirmed defer | Phase-1 mirror | DEFER (per-hero respawn-timer table RVA not in ledger) | N |
| 24 | SWFOC_SetPermadeath | iter-274 confirmed defer | Phase-1 mirror | DEFER (permadeath flag at engine-level not pinned; iter-153 SetCannotBeKilled per-unit is the alternative) | N |

## Audit summary

| Category | Count |
|----------|-------|
| Confirmed defers (genuine block) | **19** |
| Drift-review candidates (require follow-up iter) | **5** (#6 FreezeCredits, #8 SetDamageMultiplier per-slot, #15 SpawnUnit, #21 GetPlanetTechAndBuildings, #22 ListHeroes) |
| Vestigial / Phase-1-only by design | 0 |
| Bridge-ledger broken contracts | 0 |

**Drift-rate this audit: 5/24 = 21%** (vs iter-274 audit which had ~12%; 50% relative increase). Reflects: this window shipped many enumeration + per-unit wires that **partially obviate** older composite Phase2HookPending entries. The original entries were broader (system-level toggles); the iter-100-321 window favored per-unit primitives instead.

## 5 drift-review candidates — recommended follow-up iters

Each candidate gets its own ~30-60 LoC follow-up iter:

### iter-324a — SWFOC_FreezeCredits
- iter-231 shipped `Hook_AddCredits` MinHook detour (LIVE) for the freeze toggle
- iter-251 fixed the catalog rationale but kept `SWFOC_FreezeCredits` Phase2HookPending
- **Action**: re-audit whether the `Hook_AddCredits` shipping LIVE makes `SWFOC_FreezeCredits` itself LIVE. If yes: catalog flip + 1-line bridge wire delegating to the iter-231 hook. If no: rationale update explaining the distinction.

### iter-324b — SWFOC_SetDamageMultiplier per-slot
- iter-96 shipped `SetDamageMultiplierGlobal` LIVE
- iter-154 shipped per-unit `Set_Damage_Modifier` LIVE
- **Action**: per-slot is the gap (slot ≠ global ≠ per-unit). Likely needs an `(player):Apply_Damage_Modifier_To_All_Units(mult)` style composite. Defer if no engine API exists; flip LIVE if iter-167 unit-getter helper can enumerate slot's units + iter-154 helper can apply per-unit.

### iter-324c — SWFOC_SpawnUnit
- iter-109 shipped `Spawn_Unit` Lua API LIVE (tactical)
- iter-152 shipped `SWFOC_GalacticSpawnUnit` LIVE
- iter-185 shipped `Reinforce_Unit` + `Spawn_From_Reinforcement_Pool` + `Create_Generic_Object` LIVE
- **Action**: the original `SWFOC_SpawnUnit` entry may be the catch-all that's now redundant. Either flip LIVE (delegating to iter-109) or DEPRECATE if iter-109 + iter-152 + iter-185 cover all operator workflows.

### iter-324d — SWFOC_GetPlanetTechAndBuildings
- iter-296 shipped `SWFOC_GetPlanets` LIVE via `Find_All_Objects_Of_Type`
- **Action**: per-planet tech + buildings may now be derivable via `(planet):Get_Tech_Level()` (iter-169 LIVE) + `Find_All_Objects_Of_Type` filtered by parent. If composable from existing LIVE primitives, flip catalog status to LIVE with composition example. If genuine engine-API gap, document specifically what's missing.

### iter-324e — SWFOC_ListHeroes
- iter-179 shipped `Find_All_Objects_Of_Type` LIVE
- **Action**: hero category enumeration via `Find_All_Objects_Of_Type("Hero")` likely closes this. Flip LIVE + ship example invocation in catalog rationale.

## Verification

- **Ledger lint**: 0/0 at 318 entries (no ledger changes this iter — pure docs)
- **Bridge harness**: inherits 1100/0
- **Editor build**: inherits GREEN
- **No inline catalog edits**: iter-323 is audit-only; the 5 drift candidates queue for iter-324 follow-ups

## Honest defer to iter-324+

| Item | Why deferred | Recommended iter |
|------|-------------|------------------|
| Resolve all 5 drift candidates | Each needs ~30-60 LoC investigation + catalog edit | iter-324 (1st of 5) |
| Audit B last wire (`faction-roster-by-build-tab`) | iter-299 honest defer; needs additional bridge wire | iter-329+ |
| Live SWFOC verify against operator's real MasterTextures.meg | Requires running the actual game | TBD |
| Weapon/ability icon classes | 2 more asset classes; same iter-313 pattern | iter-330+ |

## Pattern lessons from this audit

### Audit cadence is auto-recovering even when overdue

Despite being ~48 iters past the canonical ~16-iter cadence, this audit found 5 drift candidates — confirming `feedback_allactions_count_pin_drift.md` (iter-195/iter-208 codified) intuition: **silent drifts compound at roughly 1 candidate per 10 iters across the catalog**. Not catastrophic, but cumulative.

### LIVE-wire enumeration partially obviates older Phase2HookPending entries

5 of the 24 P2HP entries (#6/#8/#15/#21/#22) became drift candidates **because the iter-100-321 window shipped granular per-unit + per-call primitives** that enable composition workflows the older system-level Phase2HookPending entries promised. This is the upside of the iter-178 dispatcher matrix completion + iter-179+ batch shipping — operators get the building blocks before the catch-all.

### NEW pattern observation — drift catches scale with batch granularity

The iter-100-321 window shipped 100+ wires; 5 drifted out of 24 P2HP entries → **drift rate ~5% per LIVE-wire-batch shipped**. Higher-granularity shipping = more frequent drift catches in the catalog. Codification candidate `feedback_drift_rate_scales_with_batch_granularity.md` flagged at 1st instance.

## Next-session pickup

iter-324 will start the 5-iter drift-resolution arc (one P2HP entry per iter):
- iter-324: SWFOC_FreezeCredits resolution (most-likely-LIVE-already candidate)
- iter-325: SWFOC_ListHeroes resolution (composes with iter-179 Find_All_Objects_Of_Type)
- iter-326: SWFOC_GetPlanetTechAndBuildings resolution
- iter-327: SWFOC_SpawnUnit DEPRECATE-or-LIVE-flip
- iter-328: SWFOC_SetDamageMultiplier per-slot resolution

Each ~30-60 LoC; the arc closes the 21% drift-rate gap surfaced this iter.
