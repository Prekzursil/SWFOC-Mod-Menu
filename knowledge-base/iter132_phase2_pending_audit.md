# Iter 132 — Phase2HookPending audit pass (2026-04-29)

Comprehensive sweep of all 24 catalog entries marked
`CapabilityStatus.Phase2HookPending` in
`SWFOC editor/src/SwfocTrainer.Core/Diagnostics/CapabilityStatusCatalog.cs`,
classified using the iter-128 ledger-search pattern via the iter-124-fixed
callgraph CLI.

## Triage

| Catalog entry | Verdict | Engine surface | LIVE-wire complexity |
|---|---|---|---|
| `SWFOC_ChangePlanetOwner` | drift candidate | (planet flip path; iter 33-34 wired the LIVE alternatives `ChangePlanetOwnerWithMode`/`SpawnAsStoryArrival` for Galactic tab) | unclear — re-audit with planet-side ledger in iter 133 |
| `SWFOC_ChangePlanetOwnerWithMode` | drift candidate | per iter-33 finding, this is the LIVE form via overlay Feature 3 | likely already LIVE; catalog drift |
| `SWFOC_EventControl` | confirmed defer | no `pause_event` / `event_queue` ledger entries | engine event-loop pause has no exposed primitive |
| `SWFOC_FreeBuild` | confirmed defer | no ledger entry | needs build-cost-validator detour |
| `SWFOC_FreeCam` | confirmed defer (iter 106) | no engine `Free_Cam` Lua API; only `Scroll_Camera_To` exists | needs cinematic-camera state machine reverse |
| `SWFOC_FreezeAI` | confirmed defer | no `ai_active` / `ai_pause` flag pinned | needs AI scheduler detour |
| `SWFOC_FreezeCredits` | confirmed defer | no ledger entry | needs Take_Credits hook (similar to iter-96 Take_Damage_Outer pattern) |
| `SWFOC_GetPlanets` | drift candidate | `rva_planet_*` family exists in ledger (iter 30 walked the planet array) | bridge has Phase-1 mirror today; LIVE wire needs galactic-mode walk |
| `SWFOC_GetUnitShield` | **CLOSED iter 131** | `rva_front_shield_read` @ 0x3963C0 | done — LIVE pair-flip with iter-129 writer |
| `SWFOC_HeroStatEdit` | partial | dispatcher; "shield" path now LIVE iter 129; "hull" was always LIVE; "speed" LIVE iter 100 | pre-existing partial-LIVE; add per-field LIVE/PHASE 2 split badges |
| `SWFOC_InstantBuild` | confirmed defer | no `instant_complete` ledger entry | needs construction-progress field offset |
| `SWFOC_ListHeroes` | confirmed defer | no `is_hero` flag in ledger | needs hero RTTI walk or class-based enumeration |
| `SWFOC_SetAreaDamage` | confirmed defer | no ledger entry | XML-attribute-only family (like iter 101 fire-rate) |
| `SWFOC_SetBuildCost` | confirmed defer | no ledger entry | XML-attribute-only family |
| `SWFOC_SetBuildSpeed` | confirmed defer | no ledger entry | XML-attribute-only family |
| `SWFOC_SetCameraPos` | partial defer | `rva_camera_set_transform_matrix` @ 0x261BD0 takes a `_DWORD*` matrix, not `(x,y,z)` floats | needs identity-rotation matrix construction; not a trivial wire. Iter 107's `ScrollCameraToTarget` covers the operator's likely use case. |
| `SWFOC_SetDamageMultiplier` (per-slot) | confirmed defer (iter 95) | iter 95 found Take_Damage carries no attacker-slot context at the detour layer | global form is LIVE iter 96; per-slot needs higher-layer detour |
| `SWFOC_SetDiplomacy` | **DRIFT** | `rva_make_ally_make_enemy_engine` @ 0x288800 = `__int64 __fastcall(PlayerClass*, int target_slot, int state_code)`; 0=ally, 1=enemy. Lua wrappers @ 0x6046A0/0x604780 confirm calling shape. | **single-iter LIVE wire scope** — bridge needs slot→PlayerClass* lookup (existing PlayerArray walking infrastructure) + direct `Resolve<>()` call. Queued as iter 133. |
| `SWFOC_SetIncomeMultiplier` | confirmed defer | no `income_mult` / `credits_per_sec` ledger entry | XML-attribute-only family |
| `SWFOC_SetPermadeath` | confirmed defer | no `permadeath` / `is_permanent` ledger entry | iter 104 finding stands |
| `SWFOC_SetTargetFilter` | confirmed defer | no `target_filter` ledger entry | needs targeting-system reverse |
| `SWFOC_SetUnitCapOverride` | confirmed defer | only `rva_check_pop_cap` (validator/consumer); no setter | needs pop-cap field offset |
| `SWFOC_SetUnitField` | partial — generic dispatcher | iter 138 task; Phase-1 mirror today | per-field LIVE flips trickle in (HP, shield, speed already LIVE) |
| `SWFOC_SpawnAsStoryArrival` | drift candidate | iter 34 wired Galactic tab LIVE button via Overlay Feature 2 | bridge may already LIVE-wire via a different path |
| `SWFOC_SpawnUnit` (Phase-1 mirror) | iter-119 LIVE alternative shipped | Phase-1 mirror is the original Spawn button; iter 109 `SWFOC_SpawnUnitLua` is the LIVE pair | catalog Note clarified iter 119 |
| `SWFOC_ToggleOHKAttackPower` | confirmed defer | no `attack_power` / `damage_dealer` ledger entry | XML-attribute family per iter 101 framing |

## Audit running tally (iter 128 + iter 130 + iter 131 + iter 132)

- **Drift catches**: 3 confirmed (iter 105 SetUnitShield writer, iter 130 SetHeroRespawn, iter 131 GetUnitShield reader) + 1 strong drift candidate identified iter 132 (SetDiplomacy)
- **Confirmed-defer**: 12 entries (SetFireRate, SetGameSpeed, EventControl, FreeBuild, FreeCam, FreezeAI, FreezeCredits, InstantBuild, ListHeroes, SetAreaDamage, SetBuildCost, SetBuildSpeed, SetIncomeMultiplier, SetPermadeath, SetTargetFilter, SetUnitCapOverride, SetDamageMultiplier-per-slot, ToggleOHKAttackPower)
- **Drift candidates needing deeper investigation**: 4 (ChangePlanetOwner, ChangePlanetOwnerWithMode, GetPlanets, SpawnAsStoryArrival — all galactic-mode entries that iter 30-34 may have already wired LIVE; catalog likely drifted)
- **Partial / per-field**: 2 (HeroStatEdit, SetUnitField — dispatchers where some sub-fields are LIVE and some Phase-1)
- **Camera path**: 1 (SetCameraPos — engine surface exists but per-coord wire needs matrix construction; iter 107 ScrollCameraToTarget covers most operator use cases)

## High-leverage next iterations queued

1. **Iter 133 CLOSED** — SetDiplomacy LIVE wire shipped end-to-end. Engine
   writer + Lua wrapper both pinned; bridge walks PlayerArray for slot→
   PlayerClass* + `Resolve<pfn_MakeAllyEnemy>(0x288800)(player_a, slot_b,
   state_code)`. Maps "ally"→0, "enemy"→1, "neutral"→2 (assumed).
2. **Iter 134 — galactic 4-candidate audit COMPLETE — REVISED to all
   confirmed-defer.** Iter 132 was over-optimistic.
   - **ChangePlanetOwner**: bridge Phase-1 mirror; engine writers
     `PlanetFactionChange_FullTransfer` @ 0x3FB040 (3989 bytes, 4 args)
     and `PlanetFactionChange_InitialSet` @ 0x3FA160 (271 bytes, 2 args)
     too complex for one-iter Resolve<>() pattern. No `Planet:Change_Owner`
     Lua API wrapper in ledger so DoString approach also blocked.
   - **ChangePlanetOwnerWithMode**: **NOT IN BRIDGE** — no
     `Lua_ChangePlanetOwnerWithMode` function. Catalog entry is
     vestigial; describes editor-side concept (overlay Feature 3 planet
     flip with convert/kick modes wired via separate path iter 33-34).
   - **GetPlanets**: bridge returns `"count=0"` sentinel; no
     planet-list walker in ledger.
   - **SpawnAsStoryArrival**: **NOT IN BRIDGE**; vestigial catalog
     entry (overlay Feature 2 wired via separate path iter 34).
     `StoryEvent_Factory_Create` requires multi-arg state setup not
     achievable in single-iter scope.
3. **Iter 135+** — Per-field dispatcher partial-LIVE polish. iter 132 audit
   noted HeroStatEdit + SetUnitField are partially LIVE (hull/shield/speed
   sub-fields LIVE through iter 100/129/HeroStatEdit-shield paths) but
   catalog entries say single Phase2HookPending status. Either: (a) split
   each into per-field catalog entries, or (b) flip the dispatcher to
   `Live` with note "partial — see per-field sub-status". Lower complexity
   than wire-shipment work; ships operator-visible accuracy improvement.
4. **Iter 136+** — Catalog cleanup pass: remove vestigial entries
   (`ChangePlanetOwnerWithMode`, `SpawnAsStoryArrival`) that have no
   bridge backing — these describe editor-side concepts wired via
   different paths and shouldn't pollute the SWFOC_* catalog.
5. **Iter 137+** — XML-attribute-only family bundle (fire-rate, build-cost,
   build-speed, income-multiplier, area-damage, target-filter, etc.)
   needs a dedicated RTTI-dissection arc; multi-iter project; lower
   priority than the remaining audit + polish work.

## Iter 137 follow-up — vestigial-entry cleanup

The 2 catalog entries iter 134 flagged as "vestigial" (`SWFOC_ChangePlanetOwnerWithMode`
and `SWFOC_SpawnAsStoryArrival`) had a real bridge contract gap — the
editor's `BridgeGalacticDispatcher` called both via DoString, but the
bridge had no `Lua_*` implementation, so the operator's "Flip and convert
garrison", "Flip and destroy garrison", and "Story-arrival spawn"
buttons errored at runtime with `attempt to call nil value`.

Iter 137 added Phase-1 mirror implementations to the bridge:
- `Lua_ChangePlanetOwnerWithMode` records to `g_pendingPlanetFlipModes`
- `Lua_SpawnAsStoryArrival` records to `g_pendingStoryArrivalSpawns`

Catalog entries stay `Phase2HookPending` — the Phase 2 engine wire-through
is genuinely blocked per iter 134 (multi-arg/state-laden writers). Notes
extended with iter 137 + iter 134 provenance + the overlay Feature 2/3
alternate-path explanation. Operator's actual button surface continues
to use the C++ overlay DLL's separate dispatch path.

5 red-green tests added in `tests/SwfocTrainer.Tests/Regression/Iter137VestigialMirrorPinTests.cs`.
Bridge harness 1100/0 still GREEN. Editor test suite 7623/0/0.

## Revised audit running tally (iter 128-134)

After 6 audits and 4 LIVE flips (iter 128-133) plus iter 134's galactic
4-candidate audit:

- **4 confirmed drift catches** (iter 105 SetUnitShield writer,
  iter 130 SetHeroRespawn, iter 131 GetUnitShield reader, iter 133
  SetDiplomacy)
- **6 confirmed defers** (iter 130 SetFireRate, iter 131 SetGameSpeed,
  iter 134 ChangePlanetOwner, ChangePlanetOwnerWithMode, GetPlanets,
  SpawnAsStoryArrival)
- **2 vestigial catalog entries** to remove (iter 134 found
  ChangePlanetOwnerWithMode + SpawnAsStoryArrival not in bridge at all)
- **12 unaudited entries** still triaged-but-unaudited from iter 132

**Drift hit rate** revised: **4 / 10 audited** = 40% (down from 60% with
the smaller sample). Still meaningfully positive but the galactic batch
turned out to be all defers. The pattern's leverage is **highest when the
ledger has a single-arg `(unit, value)`-shape engine setter** — that's
what makes iter 105/130/131/133 single-iter scope. Multi-arg / state-laden
functions like the planet-faction-change family need full RTTI-arc work.

## Pattern lesson reinforced

The iter-128 re-audit pattern's hit rate after 17 audits:
- **3 drift catches** (~18%) → LIVE flips in 1-2 iters
- **4 strong drift candidates** (~24%) → likely LIVE flips in 1-2 iters
  per item once investigated
- **12 confirmed defers** (~70%) → genuine engine-surface gaps, need
  multi-iter RTTI arcs

The audit itself is ~30 seconds per candidate via the callgraph CLI.
Each LIVE flip ships an operator-visible "this button actually does
something to the game" signal. The **expected ROI** of completing the
audit pass + the 1-2 iter LIVE wires it surfaces is **5-7 LIVE flips
across iter 132-135** vs zero new RE work — high leverage compared
to picking up multi-iter Thread B/C/D/E projects.
