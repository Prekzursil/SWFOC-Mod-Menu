# Iter 221 — Phase2HookPending re-audit pass (2026-05-06)

Re-audit of all `CapabilityStatus.Phase2HookPending` entries in
`SWFOC editor/src/SwfocTrainer.Core/Diagnostics/CapabilityStatusCatalog.cs`,
89 days after the iter-132 audit. Catalog has grown from ~92 entries
(iter 132) to **~213 entries** at iter 220 (+121 entries since iter 132).
**+88 LIVE flips** were shipped between iter 132 and iter 220, but ALL of
them were NEW catalog entries (SWFOC_*Lua wires), NOT silent flips of
existing iter-132 PHASE 2 PENDING entries.

## Headline tally

| Verdict | Count | Comment |
|---|---|---|
| Drift catches (silent flips since iter 132) | **0** | Negative result — catalog has held up |
| Strong drift candidates (need deeper RE) | **0** | None identified |
| Confirmed defers (carryover from iter 132) | **23** | All iter-132 verdicts still apply |
| New PHASE 2 entries since iter 132 (need classification) | **1** | GetPlanetTechAndBuildings — confirmed defer |
| Vestigial / cleanup | **0** | iter 137 cleaned all known vestigial entries |
| Carryover partial / per-field | **2** | HeroStatEdit (3/4 LIVE) + SetUnitField (now LIVE main, 3/13 sub-fields LIVE) |
| **Total PHASE 2 PENDING entries (iter 220)** | **26** | (24 from iter 132 + 1 new + 1 promoted-from-partial) |

## Per-entry classification

Each entry below is checked against:
1. Bridge harness LIVE list (`swfoc_lua_bridge/registrations.cpp`)
2. Verified ledger entries (`knowledge-base/verified_facts.json`)
3. Iter 132-220 changelog tracking (iter 187 main + iter 196/216/220 supplements)

### Galactic-mode group (4 entries, all confirmed defer per iter 134)

| Catalog entry | Iter 221 verdict | Rationale |
|---|---|---|
| `SWFOC_ChangePlanetOwnerWithMode` | **CONFIRMED DEFER (iter 134/137)** | Engine writers PlanetFactionChange_FullTransfer @ 0x3FB040 + PlanetFactionChange_InitialSet @ 0x3FA160 too complex for single-iter; iter 137 added Phase-1 mirror. Operator surface uses overlay Feature 3 (iter 33-34). |
| `SWFOC_SpawnAsStoryArrival` | **CONFIRMED DEFER (iter 134/137)** | StoryEvent_Factory_Create requires multi-arg state setup; iter 137 added Phase-1 mirror. Operator surface uses overlay Feature 2 (iter 34). |
| `SWFOC_GetPlanets` | **CONFIRMED DEFER (iter 134)** | Phase-1 mirror; engine has no `Galactic.GetAllPlanets` Lua API. Bridge would need a galactic-mode walk. |
| `SWFOC_ChangePlanetOwner` | **CONFIRMED DEFER (iter 134)** | Same 0x3FB040 / 0x3FA160 writers too complex. No `Planet:Change_Owner` Lua wrapper to use via DoString. |

### XML-attribute family (8 entries, all confirmed defer)

These entries were "set XYZ multiplier" wires that turned out to be XML-attribute-only
(read at game startup from MOD's XML files; no engine setter exposed). Confirmed defer
at iter 101 framing.

| Catalog entry | Iter 221 verdict |
|---|---|
| `SWFOC_SetIncomeMultiplier` | **CONFIRMED DEFER** — XML-attribute family |
| `SWFOC_SetBuildSpeed` | **CONFIRMED DEFER** — XML-attribute family |
| `SWFOC_SetAreaDamage` | **CONFIRMED DEFER** — XML-attribute family |
| `SWFOC_SetBuildCost` | **CONFIRMED DEFER** — XML-attribute family |
| `SWFOC_SetTargetFilter` | **CONFIRMED DEFER** — XML-attribute / targeting-system reverse |
| `SWFOC_ToggleOHKAttackPower` | **CONFIRMED DEFER** — XML-attribute family |
| `SWFOC_SetFireRate` | **CONFIRMED DEFER (iter 130)** — global form blocked; **per-unit form shipped iter 154 as separate `SWFOC_SetRateOfFireModifierLua` wire (NOT a flip of this entry)** |
| `SWFOC_SetUnitCapOverride` | **CONFIRMED DEFER** — only validator pinned, no setter |

### Engine-state-machine group (5 entries, all confirmed defer)

These need engine-internal state-machine reverse work; not addressable via DoString.

| Catalog entry | Iter 221 verdict |
|---|---|
| `SWFOC_FreezeAI` | **CONFIRMED DEFER** — AI scheduler reverse needed |
| `SWFOC_FreezeCredits` | **CONFIRMED DEFER** — needs Take_Credits hook (similar to iter-96 Take_Damage_Outer pattern) |
| `SWFOC_EventControl` | **CONFIRMED DEFER** — engine event-loop pause has no exposed primitive |
| `SWFOC_InstantBuild` | **CONFIRMED DEFER** — no `instant_complete` ledger entry |
| `SWFOC_FreeBuild` | **CONFIRMED DEFER** — needs build-cost-validator detour |
| `SWFOC_SetGameSpeed` | **CONFIRMED DEFER (iter 131)** — ledger has zero entries for game-speed/time-scale |

### Camera path (2 entries, partial defer)

| Catalog entry | Iter 221 verdict |
|---|---|
| `SWFOC_FreeCam` | **CONFIRMED DEFER (iter 106)** — engine has no `Free_Cam(enable)` Lua API; would need Lua-side scripted-behaviour mimic |
| `SWFOC_SetCameraPos` | **PARTIAL DEFER (iter 132)** — engine Lua API takes userdata not raw floats; **iter-107 ScrollCameraToTarget LIVE covers most operator use cases**; per-coord setters need matrix construction |

### Spawn / build group (1 entry — Phase-1 mirror with LIVE alternative shipped)

| Catalog entry | Iter 221 verdict |
|---|---|
| `SWFOC_SpawnUnit` | **DOCUMENTED — iter 119 LIVE alternative shipped** (`SWFOC_SpawnUnitLua` is the LIVE pair via Spawn_Unit Lua API). The Phase-1 mirror still exists for compatibility; Note text in catalog updated iter 119. |

### Hero group (3 entries, all confirmed defer)

| Catalog entry | Iter 221 verdict |
|---|---|
| `SWFOC_ListHeroes` | **CONFIRMED DEFER** — no is_hero flag in ledger; needs hero RTTI walk |
| `SWFOC_SetHeroRespawnTimer` | **CONFIRMED DEFER (iter 130)** — per-hero respawn timer table RVA not in ledger; distinct from iter-130 GLOBAL `SWFOC_SetHeroRespawn` (LIVE drift catch at iter 130) |
| `SWFOC_SetPermadeath` | **CONFIRMED DEFER (iter 104/132)** — no `permadeath` / `is_permanent` flag pin |

### NEW since iter 132 (1 entry)

| Catalog entry | Iter 221 verdict |
|---|---|
| `SWFOC_GetPlanetTechAndBuildings` | **CONFIRMED DEFER** — "Phase 1 mirror — pending galactic state API"; same blocker as iter-134 GetPlanets/ChangePlanetOwner. Galactic-mode state API not exposed via Lua wrappers in ledger. |

### Damage group (1 entry, confirmed defer)

| Catalog entry | Iter 221 verdict |
|---|---|
| `SWFOC_SetDamageMultiplier` | **CONFIRMED DEFER (iter 95)** — per-slot multiplier needs higher-layer detour; Take_Damage carries no attacker-slot context at the detour layer. **GLOBAL form is LIVE iter 96 as separate `SWFOC_SetDamageMultiplierGlobal` entry; per-unit form is LIVE iter 154 as separate `SWFOC_SetDamageModifierLua` entry — neither is a flip of THIS entry.** |

### Promoted from partial (iter 136 flipped)

| Catalog entry | Iter 221 verdict |
|---|---|
| `SWFOC_SetUnitField` | **PROMOTED TO LIVE iter 136** (line 216 Status=Live, 3/13 sub-fields LIVE — hull/shield/speed). NO LONGER PHASE 2 PENDING. |

### Carryover partial (iter 135)

| Catalog entry | Iter 221 verdict |
|---|---|
| `SWFOC_HeroStatEdit` | **PARTIAL — 3/4 sub-fields LIVE iter 135**. Catalog already documents this; not a separate PHASE 2 entry anymore. |

## Audit-triangulated verdicts

Cross-checking the iter-132 verdict count:
- **iter-132 said**: 24 PHASE 2 PENDING entries triaged
- **iter-220 has**: 26 PHASE 2 PENDING entries (24 carryover + 1 new + 1 still-pending-from-partial-resolution)
- **Net change since iter 132**: +2 entries (GetPlanetTechAndBuildings new at iter ?? + SetHeroRespawnTimer kept separate from iter-130 GLOBAL flip)
- **iter-132 prediction held**: zero silent drift catches in the iter 132-220 window. The catalog discipline is working.

## Comparison to iter 132 drift rate

| Metric | Iter 132 | Iter 221 |
|---|---|---|
| Drift catches discovered | 3 (iter 105/130/131 — pre-iter-132) | 0 |
| Strong drift candidates | 1 (SetDiplomacy → iter 133 LIVE) | 0 |
| Confirmed defers | 12 | 23 |
| Drift rate within candidates with ledger surface | 60% | 0% (none had any new ledger surface to check) |

**Drift rate dropped to 0%** because the iter-132 audit triaged ALL ledger-surfaced candidates. Subsequent iters (133-186) shipped wires for NEW APIs (the iter 100-186 LIVE-wire goldmine) — these were always NEW catalog entries, not flips of pre-existing PHASE 2 PENDING entries.

## High-leverage iter-222+ direction (recommendations)

Since the audit found zero drift catches, iter-222+ direction must come from outside the catalog. Per the iter-220 supplement's "What's left after the queue closure" section:

### Option A: Multi-iter A1.x dedicated arc (highest engineering value)

Pick one Phase-2-blocked candidate and do the multi-iter RTTI dissection arc to unblock it:

1. **A1.3 SetFireRate at GLOBAL level** — iter 154 closed the per-unit form; the GLOBAL form needs WeaponClass RTTI dissection or per-tick MinHook detour. Estimate: **5-8 iters** (RTTI walk + detour design + validation).

2. **SetUnitField's 10 remaining Phase-1 fields** — iter 136 flipped 3/13 to LIVE; the remaining 10 (max_hull, max_shield, max_speed, attack_power, respawn_ms, invuln_flag, prevent_death, is_hero, respawn_enabled, owner_slot) need per-field RVA pins. Estimate: **8-15 iters** (one to a few fields per iter depending on RTTI complexity).

3. **SetCameraPos per-coord** — iter-107 LIVE for vec3-targeted camera; per-coord setters require matrix construction. Estimate: **3-5 iters**.

4. **SetUnitCapOverride** — only validator pinned; setter unknown. Estimate: **5-10 iters** (needs pop-cap field offset hunt).

### Option B: Multi-iter project from STATUS.md backlog

1. **Thread B — Overlay Phase 2-full ImGui vendoring** (~500 LoC, ~15 files). Kicks off at iter-222 with vendoring sources; multi-iter integration follows.

2. **Thread C — Save-game RE** (not started). Multi-iter; iter-222 would be the format-dissection kickoff (binary identification + struct mapping + first-pass section-table walk).

### Option C: Operator-facing polish (lowest engineering risk, fastest delivery)

1. **README capstone update** covering iter 100-220 master loop (~250 lines). Pure docs iter; serves as the public-facing summary of the surfacing arc.

2. **Bridge harness expansion** — current 1100 tests cover iter 100-131; iter 132-186 LIVE wires don't have harness tests. Multi-iter to backfill (10-20 tests per iter, ~5 iters total).

3. **Replay harness expansion** — iter 141 audit confirmed by-design narrower (24 commands vs 100). Multi-iter to add SimulatorSmokeRun parallel suite.

### Recommended iter 222

**Option C polish path: README capstone update + iter-220 reference**. Pure docs iter (single-iter scope, low risk, immediate operator-facing value). Sets up iter-223 to start a multi-iter project (Option A or B) without losing momentum on the docs trail.

Alternative: **iter-222 = Option A1.3 SetFireRate GLOBAL-level kickoff** if the user wants to pivot back to RE work. Multi-iter; first iter would be WeaponClass RTTI walk + setter-RVA hunt.

## Pattern lesson

The iter-132 audit's framing held up perfectly across 88 days and 88 LIVE flips. **Catalog discipline pays off**: explicit Phase2HookPending markers + iter-N rationale stamps + cross-iter audit passes produce a steady, drift-free catalog state. The iter-128/130/131/132 audit pattern is reusable for any future "did the catalog drift?" check.

**The 0% drift rate is a NEGATIVE RESULT, but a useful one** — it tells us where engineering effort needs to go (RTTI dissection arcs for new RVA pins, NOT silent-flip catalog hunts). Future iters won't find free drift catches; every Phase-1 → LIVE promotion will require new RE work.
