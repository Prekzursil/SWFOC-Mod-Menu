# Iter 402 — NEW arc-class RE kickoff: EnumConversionClass<UnitAbilityType> ability-name extraction (callgraph mining)

**Date:** 2026-05-07
**Arc class:** RE kickoff (1st iter of ~3-iter mini-arc)
**Predecessor:** iter-401 (post-milestone deep feature-health verify)
**Successor (queued):** iter-403 (C# const list + UnitControl dropdown wiring)

## What this iter does

User explicit directive (iter-401 stop-hook): "highest-leverage next step is building a queryable callgraph + subsystem index... cluster functions by call neighborhood +"

iter-402 is the FIRST application of the callgraph index for NEW feature mining (post-iter-400 milestone NEW frontier). Per iter-401 discovery: 91.7% of binary unmined.

**Target identified**: `EnumConversionClass<enum UnitAbilityType>` static initializer at `0x1405DEA20` (single 7012-byte function in the untouched_subsystems.md catalog).

**Operator value**: Extracts the FULL list of unit ability names (e.g. `Ion_Cannon_Shot`, `Saber_Throw`, `Force_Lightning`). Pairs with iter-156 `SWFOC_ActivateAbilityLua` (writer LIVE) + iter-173 `SWFOC_IsAbilityActiveLua` (reader LIVE) — currently the operator must KNOW ability names by memory; iter-403 will add a dropdown.

## RE design + extraction

### Callgraph metadata
```
addr:        0x1405DEA20
name:        sub_1405DEA20
prototype:   __int64(void)
size:        7012
end_addr:    0x1405E0584
source:      full_b150-151.json
verified:    -- (no ledger entry; iter-402 will add one)
rtti_refs:   EnumConversionClass<enum UnitAbilityType>
n_callers:   1  (caller @ 0x14046EA70 — likely program-init driver)
n_callees:   10
```

### Callee analysis
- **75 calls to `0x140341CE0`**: likely `EnumConversionClass::RegisterMapping(enum_value, name)` core helper
- **44 calls to `j_j_free`**: per-entry C-string lifecycle
- **44 calls to `_invalid_parameter_noinfo_noreturn`**: defensive bounds
- **31 calls to `0x14001EAA0`**: likely `EnumConversionClass::AppendEntry`
- **31 calls to `0x14008C6E0`**: likely string allocator
- **3 calls to `operator new`**: 3 internal vector reallocations (typical for 31-entry growth)

**Inference**: Function constructs a static `EnumConversionClass<UnitAbilityType>` and registers ~31 distinct ability types. The 75/44 ratio suggests each entry has ~2 string forms (name + display-name OR primary + alias).

### String reference extraction (69 unique `a`-prefixed symbol labels)

IDA dumps string-references with `a<CamelCase>` symbol labels. Extracted from `extract_unit_ability_strings.py` (run iter-402):

| IDA label | Inferred SWFOC ability name |
|---|---|
| aAfterburner | `Afterburner` |
| aAreaEffectConv | `Area_Effect_Conversion` (truncated) |
| aAreaEffectHeal | `Area_Effect_Heal` |
| aAreaEffectStun | `Area_Effect_Stun` |
| aAvoidDanger | `Avoid_Danger` |
| aBerserker | `Berserker` |
| aBlast | `Blast` |
| aBuzzDroids | `Buzz_Droids` |
| aCableAttack | `Cable_Attack` |
| aCaptureVehicle | `Capture_Vehicle` |
| aClusterBomb | `Cluster_Bomb` |
| aConcentrateFir | `Concentrate_Fire` (truncated) |
| aCorruptSystems | `Corrupt_Systems` |
| aDeploy | `Deploy` |
| aDeploySquad | `Deploy_Squad` |
| aDeployTroopers_0 | `Deploy_Troopers` (IDA dedupe suffix) |
| aDetonateRemote | `Detonate_Remote` |
| aDistract | `Distract` |
| aDrainLife | `Drain_Life` |
| aEjectVehicleTh | `Eject_Vehicle_Thieves` (truncated) |
| aEnergyWeapon | `Energy_Weapon` |
| aFireLobbingSup | `Fire_Lobbing_Support` (truncated) |
| aFlameThrower | `Flame_Thrower` |
| aForceCloak | `Force_Cloak` |
| aForceConfuse | `Force_Confuse` |
| aForceLightning | `Force_Lightning` |
| aForceSight | `Force_Sight` |
| aForceTelekines | `Force_Telekinesis` (truncated) |
| aForceWhirlwind | `Force_Whirlwind` |
| aFowRevealPing | `FOW_Reveal_Ping` |
| aFullSalvo | `Full_Salvo` |
| aHarmonicBomb | `Harmonic_Bomb` |
| aInvulnerabilit | `Invulnerability` (truncated) |
| aIonCannonShot | `Ion_Cannon_Shot` |
| aJetPack | `Jet_Pack` |
| aLaserDefense | `Laser_Defense` |
| aLeechShields | `Leech_Shields` |
| aLuckyShot | `Lucky_Shot` |
| aLure | `Lure` |
| aMaximumFirepow | `Maximum_Firepower` (truncated) |
| aMissileShield | `Missile_Shield` |
| aPlaceRemoteBom | `Place_Remote_Bomb` (truncated) |
| aPowerToWeapons_0 | `Power_To_Weapons` |
| aProximityMines | `Proximity_Mines` |
| aRadioactiveCon | `Radioactive_Contamination` (truncated) |
| aReplenishWingm | `Replenish_Wingmen` (truncated) |
| aRocketAttack | `Rocket_Attack` |
| aSaberThrow_0 | `Saber_Throw` |
| aSelfDestruct | `Self_Destruct` |
| aSensorJamming | `Sensor_Jamming` |
| aShieldFlare | `Shield_Flare` |
| aSpoilerLock | `Spoiler_Lock` |
| aSpreadOut | `Spread_Out` |
| aSprint | `Sprint` |
| aStealth | `Stealth` |
| aStickyBomb | `Sticky_Bomb` |
| aStimPack | `Stim_Pack` |
| aStun | `Stun` |
| aSummon | `Summon` |
| aSuperLaser | `Super_Laser` |
| aSwapWeapons | `Swap_Weapons` |
| aTacticalBribe | `Tactical_Bribe` |
| aTargetedHack | `Targeted_Hack` |
| aTargetedInvuln | `Targeted_Invulnerability` (truncated) |
| aTargetedRepair | `Targeted_Repair` |
| aTractorBeam | `Tractor_Beam` |
| aTurbo | `Turbo` |
| aUntargetedStic | `Untargeted_Sticky_Bomb` (truncated) |
| aWeakenEnemy | `Weaken_Enemy` |

**69 ability names recovered.** Some labels are IDA-truncated at ~14 chars; full names will be confirmed against `docs/lua-api.md` reference (community-documented ability names) before C# embed.

### Architectural finding
The `EnumConversionClass` pattern is a static lookup table that does NOT need MinHook intervention. The names are **program-lifetime constants** — extracting at iter-402 RE time gives the operator a static C# list with no game-attached requirement.

This is a stronger application of iter-302 codified rule (`feedback_engine_already_does_this`): instead of "engine has Lua API for this", here it's "engine has STATIC DATA — extract at RE time, embed in C# const list".

## Arc plan (3-iter mini-arc)

| Iter | Action | Output |
|---|---|---|
| **402** (THIS) | RE design + 69-name extraction | This doc + extraction tool + ledger entry |
| 403 | C# const `KnownUnitAbilityNames` array + UnitControl tab Activate_Ability button → dropdown | ~120 LoC + 3-4 pin tests |
| 404 | Editor republish + verifier ledger lint + filtered test verify + close-out | Republished editor + iter-404 close-out doc |

**Marginal cost vs full A1.x 5-iter arc**: NO bridge work, NO simulator handler, NO MinHook detour. Pure editor-side change consuming RE-extracted static data. ~3-iter shape.

## Verification gates ALL GREEN at iter-402

- 0 source/test/catalog edits in `SWFOC editor/` (pure RE iter)
- All editor build/test gates inherit GREEN from iter-401 chain
- Bridge harness inherits 1100/0; verifier ledger lint 0/0 at 318 entries (iter-402 will queue ledger add for iter-403/404)
- Editor binary 157.88 MB at May 7 12:20:02 (iter-397 republish; inherited)
- Callgraph CLI confirmed FULLY OPERATIONAL at iter-401 (4 query types exercised)

## Net iter-402 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 1 NEW Python tool (`tools/extract_unit_ability_strings.py` ~50 LoC) |
| Doc shipped | 1 close-out doc with 69-name extraction table |
| Pattern observations flagged | NEW pattern: "EnumConversionClass static-initializer mining" — extends iter-302 `feedback_engine_already_does_this` to "engine has STATIC DATA via RE-extracted bitops" tier |
| Cycle time | ~15 min (callgraph queries + Python extraction tool + close-out doc) |
| Cluster mining demonstrated | YES (untouched_subsystems.md → high-leverage cluster picked → 2-hop callgraph query → IDA decompile body extraction → operator-facing C# const list designed) |
| User-directive alignment | DIRECT (callgraph + subsystem index used as the planning surface; cluster mining drove the target selection) |

**iter-402 is the first concrete application of the user-directed callgraph + subsystem index for NEW feature mining.** Demonstrates the index's full usage chain from untouched-cluster identification → 2-hop neighborhood query → corpus extraction → operator-facing feature design.

71st post-iter-323 arc iter (1st post-milestone NEW-arc iter); 132nd consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter options (iter-403)

In priority order:

1. **iter-403 implement C# `KnownUnitAbilityNames`** — primary path; ~120 LoC source + 3-4 pin tests; UnitControl tab dropdown wiring
2. **iter-403 cross-validate ability names against `docs/lua-api.md`** — disambiguate IDA-truncated names; low-risk
3. **iter-403 ledger entry add** — `rva_unit_ability_type_enum_init` ledger entry pinning `0x5DEA20` with 1-tool consensus initially

iter-403 likely combines all three (option 1 primary + 2/3 as side effects).
