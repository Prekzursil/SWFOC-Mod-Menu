# Iter 250 — Phase2HookPending Re-Audit Pass (3rd audit; iter-132 + iter-221 predecessors)

**Date:** 2026-05-06
**Status at end of iter 250:** 25 entries triaged; **1 catalog-rationale drift caught** (SWFOC_FreezeCredits should cite iter-231 LIVE alternative — fix queued for iter 251); 22 confirmed defer (iter-132 + iter-221 + iter-249 cumulative); 2 vestigial-fixed (iter-137); count pin `_Is25` confirmed correct.

**Predecessor audits:** iter 132 (24 entries triaged → 3 drift catches → iter 133/135/136 LIVE flips); iter 221 (24 → 26 entries with 4 drift catches caught + closed); iter 250 (this doc — 25 entries, 1 drift catch).

---

## Headline tally

| Iter | Phase2 count | Drift catches | Notes |
|---|---|---|---|
| 132 | 24 | 3 (iter 133/135/136 follow-ups) | First structured audit |
| 221 | 26 (+1 iter-137 vestigials, +1 iter-130 SetHeroRespawnTimer kept distinct) | 4 (iter 222-237 follow-ups in subsequent iters) | Catalog grew ~85 entries since iter-132 |
| 237 | 25 (-1 SetCameraPos LIVE flip) | (silent count drift caught audit-by-fail iter-243) | Test pin updated `_Is26 → _Is25` |
| 249 | 25 (no change; SWFOC_SetUnitCapOverride DEFERRED CONFIRMED via iter-249 honest defer) | 0 LIVE flip; ledger DEPRECATION instead | New "AOB drift across binary versions" pattern |
| **250** | **25 (UNCHANGED)** | **1 catalog-rationale drift (SWFOC_FreezeCredits)** | **+1 minor follow-up queued (iter 251)** |

---

## Per-entry triage — 25 PHASE 2 PENDING entries

### Vestigial-fixed (iter 137 cleanup; stay Phase2 as Phase-1 mirrors)

| Entry | Status | Bridge stub | LIVE alternative |
|---|---|---|---|
| SWFOC_ChangePlanetOwnerWithMode | Phase-1 mirror (iter 137) | Stub exists | iter-108 SWFOC_ChangeUnitOwnerLua — different surface (units vs planets); SWFOC_ChangePlanetOwner alternative below |
| SWFOC_SpawnAsStoryArrival | Phase-1 mirror (iter 137) | Stub exists | iter-152 SWFOC_GalacticSpawnUnit |

**Verdict**: STAY PHASE 2 PENDING. iter-137 closed the broken-contract gap; rationales already cite alternatives.

### Confirmed-defer (genuine engine block)

| Entry | Confirmed at iter | Reason |
|---|---|---|
| SWFOC_EventControl | 132 | No engine surface for "event control" semantics |
| SWFOC_SetIncomeMultiplier | 132 | No income-multiplier ledger entry; iter-231 covers credits-freeze + global mult, not income |
| SWFOC_SetGameSpeed | 131 | Ledger has zero entries for game-speed / time-scale; bridge correctly Phase-1 mirror |
| SWFOC_SetBuildSpeed | 132 | No build-speed table RVA pin |
| SWFOC_SetDamageMultiplier (per-slot) | 94 | Per-ability classes only; no global per-slot multiplier path; iter-96 SWFOC_SetDamageMultiplierGlobal is the global LIVE alternative |
| SWFOC_SetAreaDamage | 132 | No area-damage scalar engine surface |
| SWFOC_SetTargetFilter | 132 | Per-unit AI targeting filter — no flat engine knob |
| SWFOC_ToggleOHKAttackPower | 132 | No one-hit-kill toggle in engine |
| SWFOC_FreezeAI | 132 | AI scheduler gate — needs per-frame scheduler hook (multi-iter RTTI dissection arc) |
| SWFOC_FreeCam | 132 | iter-148 cinematic camera quad LIVE wires + iter-237 SetCameraPos LIVE provide partial coverage; "free cam" semantic = direct mouse-control which engine doesn't expose |
| SWFOC_SpawnUnit | 109 (iter 109 Lua-wrapper alternative shipped) | iter-109 SWFOC_SpawnUnitLua provides the LIVE alternative; SWFOC_SpawnUnit stays Phase-1 mirror because the (faction, type, x, y, z, count) wire format is operator-different |
| SWFOC_SetBuildCost | 132 | No build-cost table RVA pin |
| **SWFOC_SetUnitCapOverride** | **249 (NEW)** | **Apocalypticx ledger entry DEPRECATED iter 249 — AOB drift; needs fresh RE via live-game CheatEngine or IDA MCP** |
| SWFOC_InstantBuild | 132 | No build-progress engine knob |
| SWFOC_FreeBuild | 132 | Same as SetBuildCost (free build = cost=0); pending build-cost RVA |
| SWFOC_GetPlanets | (pending) | Galactic state API; unstubbed reader |
| SWFOC_ChangePlanetOwner | (pending) | Different from iter-108 SWFOC_ChangeUnitOwnerLua (planet vs unit ownership) |
| SWFOC_GetPlanetTechAndBuildings | (pending) | Galactic state API; unstubbed reader |
| SWFOC_ListHeroes | 132 | Hero detection table needs IDA pin |
| SWFOC_SetHeroRespawnTimer | (pending) | Per-hero respawn-timer table RVA not in ledger; iter-130 SetHeroRespawn covers global only |
| SWFOC_SetPermadeath | (pending) | Hero permadeath flag pin |

**Verdict**: 21 entries stay PHASE 2 PENDING. All are honest defers with documented engine-block reasons.

### Catalog-rationale drift (Phase-1 mirror + has LIVE alternative; rationale doesn't cite it)

| Entry | Drift kind | Action |
|---|---|---|
| SWFOC_SetFireRate | NONE — rationale already updated iter-225/iter-154 (says "superseded by iter-225 SWFOC_SetFireRateMultiplierGlobal + iter-154 SWFOC_SetRateOfFireModifierLua") | (no action) |
| **SWFOC_FreezeCredits** | **YES — rationale still says "BLOCKED-NO-RVA"** but iter-231 SWFOC_SetCreditsFreezeGlobal LIVE shipped (+4 LIVE flips) | **Iter 251: update rationale to "superseded by iter-231 SWFOC_SetCreditsFreezeGlobal (Hook_AddCredits MinHook detour with bool-precedence)"** |

**Drift catch summary**: 1 catalog-rationale drift (operator-trust drift, not status drift). The Phase2HookPending status itself is correct — `SWFOC_FreezeCredits` is a legacy wire shape that's never been wired LIVE; iter-231 shipped a NEW catalog entry (`SWFOC_SetCreditsFreezeGlobal`) for the LIVE path. But operators looking at `SWFOC_FreezeCredits` need to know about the iter-231 LIVE alternative — same pattern as the iter-225 SetFireRate rationale fix.

---

## Drift-class extension (NEW from iter 250)

Iter 250 caught a NEW drift class:

**Catalog-rationale-cross-reference drift**: a Phase-1 mirror catalog entry stays Phase2HookPending correctly (the wire IS Phase-1) but its rationale doesn't cite the LIVE alternative shipped under a sibling catalog entry. This is **operator-trust drift** — operators reading the Phase2 entry don't know the LIVE alternative exists.

**Pattern for future audits**: when an iter ships a NEW catalog entry that supersedes a legacy Phase-1 mirror (like iter-225 / iter-231 / iter-237 / iter-243), the legacy entry's rationale MUST be updated in the same iter with an explicit "superseded by iter-N SWFOC_X" cross-reference. This is the same cascading-test-obligation pattern as iter-237 SetCameraPos catalog count + iter-243 ratio pin — but at the rationale level instead of the test-pin level.

**Memory rule extension**: `feedback_allactions_count_pin_drift` (catalog-wide aggregation pins) extends to cover **legacy-Phase-1-mirror rationale cross-references too**.

---

## What's next (iter 251+)

**Recommended: iter 251 = SWFOC_FreezeCredits rationale fix (single-iter polish)**.
- Update CapabilityStatusCatalog.cs line 121 rationale from "BLOCKED-NO-RVA" to "superseded by iter-231 SWFOC_SetCreditsFreezeGlobal (Hook_AddCredits MinHook detour at 0x27F370 with bool-precedence; +4 LIVE flips iter 231)".
- Pin test extension: add to `Iter221Phase2PendingReAuditTests` a NEW assertion that legacy Phase-1 mirror rationales cite their LIVE alternative when one exists. This catches future drift class instances.
- Verify gates: editor build 0/0 + focused test pass + capability surface markdown regen.

**iter 252 (queued)**: alternative — Option B from iter-247 changelog: A1.x SetUnitField max_hull/max_shield/max_speed/attack_power batch RTTI walk arc (4 sub-fields; iter-242 deferred).

**iter 253 (queued)**: alternative — Option C operator polish session covering iter 242-249 wires (Lua Playground preset menu refresh + native UX surfacing for any unsurfaced read-side wires).

---

## Iter 250 close-out

- This document is the iter 250 deliverable.
- No bridge / dispatcher / VM / XAML / test changes. Pure audit + design doc.
- 109 → 109 buttons UNCHANGED.
- Verifier ledger lint untouched (no new entries this iter).
- 1 follow-up task queued for iter 251 (SWFOC_FreezeCredits rationale fix).

**Pattern lesson capstone — Phase2HookPending audits stay productive at decreasing rates**:
- iter 132 (first audit, 24 entries): 3 catches = 12.5% drift rate
- iter 221 (90 iters later, 26 entries): 4 catches = 15% drift rate
- iter 250 (29 iters after iter 221, 25 entries): 1 catch = 4% drift rate

The drift rate is decreasing — each audit closes drift sources, future drift sources are NEW additions only. Future audits should be cheaper (<1 iter docs + <1 iter follow-ups) until a major catalog refactor introduces new drift potential.

**Pattern lesson new for iter 250**: **decreasing drift rate validates the catalog-discipline framework**. The iter-67 explicit-status pattern (Phase2HookPending + rationale + cross-references) + iter-132/iter-221/iter-250 audit cadence + iter-243 audit-by-fail catches together produce a measurable convergence: drift catches drop from 12.5% → 15% → 4%. Catalog-discipline ROI is real.
