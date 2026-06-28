# StarWarsG.exe Complete RVA Reference v3
## Star Wars: Empire at War -- Forces of Corruption (64-bit Steam)
### Binary: StarWarsG.exe v1.121.13.7360, x86_64, MSVC, 13,369,344 bytes
### Ghidra Image Base: 0x140000000

**Document Version:** 3.0 (Phase 3 Consolidation)
**Total RVAs Cataloged:** 280+
**Analysis Date:** 2026-04-04

---

## Verification Method Legend

| Tag | Meaning |
|-----|---------|
| CONFIRMED-RUNTIME | Verified via live runtime testing (CE, DLL bridge, hook) |
| CONFIRMED-RE | Verified via binary RE (Ghidra decompilation, multiple xrefs) |
| CONFIRMED-FOCAPI | Matches FoCAPI community reference (independently verified) |
| CONFIRMED-AOB | AOB signature verified unique within module |
| CONFIRMED-RTTI | Class identified via RTTI recovery |
| DISCOVERED-GHIDRA | Found via Ghidra static analysis in Phase 3 (single source) |
| DISCOVERED-CONTEXT | Inferred from decompiler context and cross-references |
| ESTIMATED | Educated guess based on source ordering or patterns |

---

## 1. Lua 5.0.2 C API Functions (48 Confirmed)

### Confirmed and Tested

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x7B8930` | lua_open | CONFIRMED-RUNTIME | MinHook hooked, 400+ states captured |
| `0x7B8BC0` | lua_checkstack | CONFIRMED-RE | Ghidra: checks (top-base>>4)+extra > 0x800 |
| `0x7B8C40` | lua_concat | CONFIRMED-RE | Ghidra: if n>1 calls concat, if n==0 pushes empty |
| `0x7B8CD0` | lua_cpcall | CONFIRMED-RE | Ghidra: checks top-1 is function, calls luaD_pcall |
| `0x7B8D80` | lua_error | CONFIRMED-RE | Ghidra: calls luaG_errormsg |
| `0x7B8D90` | lua_getfenv | CONFIRMED-RE | Ghidra: checks tag==6+C, reads env from closure+0x20 |
| `0x7B8DF0` | lua_getgccount | CONFIRMED-RE | Ghidra: returns global_State+0x74 >> 10 |
| `0x7B8E00` | lua_getgcthreshold | CONFIRMED-RE | Ghidra: returns global_State+0x70 >> 10 |
| `0x7B8E10` | lua_getmetatable | CONFIRMED-RE | Ghidra: checks tag==5||7, reads metatable at gc+0x10 |
| `0x7B8E90` | lua_gettable | CONFIRMED-RE | Ghidra: resolves table, calls luaV_gettable |
| `0x7B8EF0` | lua_gettop | CONFIRMED-RUNTIME | Bytes: 48 8B 41 10 48 2B 41 18 48 C1 F8 04 C3 |
| `0x7B8F00` | lua_insert | CONFIRMED-RE | Ghidra: shifts values above idx, copies top |
| `0x7B8F60` | lua_iscfunction | CONFIRMED-RE | Ghidra: checks tag==6 && gc+0x0a != 0 |
| `0x7B8FB0` | lua_isnumber | CONFIRMED-RE | Ghidra: checks tag==3 or calls tonumber |
| `0x7B9010` | lua_isstring | CONFIRMED-RE | Ghidra: checks tag-3 < 2 |
| `0x7B9060` | lua_equal | CONFIRMED-RE | Ghidra: calls luaV_equalobj |
| `0x7B90F0` | lua_load | CONFIRMED-RE | Ghidra: calls parser/compiler chain |
| `0x7B9140` | lua_newtable | CONFIRMED-RUNTIME | FoCAPI match + DLL self-test PASSED |
| `0x7B9190` | lua_newthread | CONFIRMED-RE | Ghidra: creates thread, pushes type=8 |
| `0x7B91D0` | lua_newuserdata | CONFIRMED-RE | Ghidra: allocates Udata, returns gc+0x20 |
| `0x7B9220` | lua_next | CONFIRMED-RE | Ghidra: hash traversal on table |
| `0x7B9280` | lua_pcall | CONFIRMED-RE | Ghidra: 4 params, calls luaD_pcall at 0x7BBBE0 |
| `0x7B9320` | lua_pushboolean | CONFIRMED-RUNTIME | Byte pattern: writes type=1 based on EDX |
| `0x7B9340` | lua_pushcclosure | CONFIRMED-RUNTIME | FoCAPI match + DLL registration works |
| `0x7B9480` | lua_pushlightuserdata | CONFIRMED-RUNTIME | Byte pattern: writes type=2, value from RDX |
| `0x7B94A0` | lua_pushlstring | CONFIRMED-RE | Ghidra: pushes type=4, calls string intern |
| `0x7B9510` | lua_pushnil | CONFIRMED-RUNTIME | Byte pattern: writes type=0 |
| `0x7B9520` | lua_pushnumber | CONFIRMED-RUNTIME | Byte-verified: movsd [rax+8],xmm1 + type=3 |
| `0x7B9540` | lua_pushstring | CONFIRMED-RUNTIME | FoCAPI RVA, DLL self-test PASSED |
| `0x7B9600` | lua_pushvalue | CONFIRMED-RE | Ghidra: copies TValue at idx to top |
| `0x7B9640` | lua_pushfstring | CONFIRMED-RE | Ghidra: wraps luaO_pushfstring |
| `0x7B9690` | lua_lessthan | CONFIRMED-RE | Ghidra: calls luaV_lessthan |
| `0x7B9820` | lua_rawseti | CONFIRMED-RUNTIME | FoCAPI RVA, DLL self-test PASSED |
| `0x7B99D0` | lua_settop | CONFIRMED-RUNTIME | DLL self-test PASSED (pop operations) |
| `0x7B9A60` | lua_settable | CONFIRMED-RUNTIME | FoCAPI RVA, DLL registration PASSED |
| `0x7B9BC0` | lua_tonumber | CONFIRMED-RUNTIME | DLL self-test: returned 12345.0 correctly |
| `0x7B9E00` | lua_type | CONFIRMED-RUNTIME | DLL self-test: returned 4 for string, 3 for number |

### Estimated / Needs Verification

| RVA | Function | Status | Notes |
|-----|----------|--------|-------|
| `0x7B8A70` | lua_close | ESTIMATED | Between lua_open and lua_checkstack; needs Ghidra confirm |
| `0x7B9B10` | lua_tostring candidate 1 | ESTIMATED | Has cmp-with-4 at +0x3A |
| `0x7B9CC0` | lua_tostring candidate 2 | ESTIMATED | Standard prologue, cmp-with-4 at +0x45 |

### Known Wrong RVAs (DO NOT USE)

| RVA | Was Labeled | Actual | Evidence |
|-----|-----------|--------|----------|
| `0x7B94A0` | lua_pushstring | lua_pushlstring | FoCAPI 0x7B9540 works, this has 3 params |
| `0x7B97C0` | lua_settable | Unknown | Crashes; real is 0x7B9A60 |
| `0x7B9500` | lua_pushnumber | Unknown | Game crashed; real is 0x7B9520 |
| `0x7B8E10` | lua_gettable | lua_getmetatable | Self-test FAILED; real gettable is 0x7B8E90 |

---

## 2. Player System Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x27ED40` | PlayerClass::~PlayerClass | CONFIRMED-RE | Ghidra destructor analysis, RTTI confirmed |
| `0x27F370` | AddCredits | CONFIRMED-AOB | Phase 2 RE, AOB verified unique, CE tested |
| `0x27F7C0` | Lock_Tech_Add | CONFIRMED-RE | Ghidra decompilation of Lock_Tech flow |
| `0x27F860` | Unlock_Tech | CONFIRMED-RE | Writes to +0x1B0/0x1B8 confirmed |
| `0x282190` | Auto_Upgrade_Tech | DISCOVERED-GHIDRA | Increments tech_level at +0x84 |
| `0x2823E0` | Is_Ally | CONFIRMED-RE | Returns diplomacy_table[id] == 0 |
| `0x2824F0` | Is_Enemy | CONFIRMED-RE | Returns diplomacy_table[id] == 1 |
| `0x282550` | IsLocked | CONFIRMED-RE | Reads +0x1C8/0x1D0 |
| `0x282580` | IsUnlocked | CONFIRMED-RE | Reads +0x1B0/0x1B8 |
| `0x286100` | Unlock_Tech_Remove | CONFIRMED-RE | Removes from locked list |
| `0x286150` | Lock_Tech | CONFIRMED-RE | Writes to locked_types DVec |
| `0x288800` | Make_Ally / Make_Enemy | CONFIRMED-RE | Writes diplomacy_table[target_id] |
| `0x288980` | SetTechLevel | CONFIRMED-AOB | Phase 2 RE, AOB verified unique |
| `0x294BC0` | PlayerList_FindByID | CONFIRMED-FOCAPI | Phase 1 RE + FoCAPI exact match |
| `0x2BD2F0` | Select_Object_Engine | CONFIRMED-RE | Called by Lua Select_Object wrapper |
| `0x340920` | Retreat_Engine | CONFIRMED-RE | Called by Lua Retreat wrapper |

### Player Lua Wrapper Functions

| RVA | Lua Method | Status |
|-----|-----------|--------|
| `0x601AA0` | Disable_Bombing_Run | DISCOVERED-GHIDRA |
| `0x601BE0` | Disable_Orbital_Bombardment | DISCOVERED-GHIDRA |
| `0x601E80` | Enable_Advisor_Hints | DISCOVERED-GHIDRA |
| `0x602060` | Get_Name (AI type) | DISCOVERED-GHIDRA |
| `0x602640` | Enable_As_Actor | CONFIRMED-RE |
| `0x602690` | Get_Space_Station_Level | DISCOVERED-GHIDRA |
| `0x6027F0` | Get_Credits | DISCOVERED-GHIDRA |
| `0x602A00` | Get_Faction_Name | DISCOVERED-GHIDRA |
| `0x602AE0` | Get_Difficulty | DISCOVERED-GHIDRA |
| `0x602C40` | Get_ID | DISCOVERED-GHIDRA |
| `0x602EE0` | Get_Team_ID | DISCOVERED-GHIDRA |
| `0x603040` | Get_Tech_Level | DISCOVERED-GHIDRA |
| `0x603130` | Give_Money | CONFIRMED-RE |
| `0x603560` | Is_Ally | CONFIRMED-RE |
| `0x603760` | Is_Enemy | CONFIRMED-RE |
| `0x603960` | Is_Local_Player | DISCOVERED-GHIDRA |
| `0x603A40` | Is_Human | DISCOVERED-GHIDRA |
| `0x603B20` | Lock_Tech | CONFIRMED-RE |
| `0x603C70` | Release_Credits_For_Tactical | CONFIRMED-RE |
| `0x603DE0` | Retreat | CONFIRMED-RE |
| `0x603F60` | Select_Object | CONFIRMED-RE |
| `0x604300` | Set_Black_Market_Tutorial | DISCOVERED-GHIDRA |
| `0x6043C0` | Set_Sabotage_Tutorial | DISCOVERED-GHIDRA |
| `0x604480` | Set_Tech_Level | CONFIRMED-RE |
| `0x604540` | Unlock_Tech | CONFIRMED-RE |
| `0x6046A0` | Make_Ally | CONFIRMED-RE |
| `0x604780` | Make_Enemy | CONFIRMED-RE |
| `0x6019F0` | PlayerWrapper::Create | CONFIRMED-FOCAPI |

---

## 3. Game Object System Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x388720` | GameObjectClass::ctor (default) | CONFIRMED-RE | Ghidra, writes vtable 0x8661B8 |
| `0x388B60` | GameObjectClass::ctor (full) | CONFIRMED-RE | Ghidra, 5+ params |
| `0x395AC0` | QueryInterface | CONFIRMED-RUNTIME | Phase 1 RE, pseudocode available |
| `0x3956C0` | ResolveParentOwner | CONFIRMED-RE | Phase 1 RE |
| `0x395920` | SubObject_OwnerResolve | DISCOVERED-GHIDRA | Reads +0xB8, +0xD8 |
| `0x395C70` | BuffModifier_Read | DISCOVERED-GHIDRA | Reads +0xF0 |
| `0x3989A0` | Spawn_Init | CONFIRMED-RE | Allocates combatant (0x3B8), sets initial HP |
| `0x3AC530` | Transform_Update | DISCOVERED-GHIDRA | Signal listener notify, position update |
| `0x574D0E` | Change_Owner | CONFIRMED-RE | Phase 2 RE |
| `0x5792E0` | Get_Owner_Lua | CONFIRMED-RE | Phase 1 RE, pseudocode available |
| `0x57D550` | Make_Invulnerable_LuaWrapper | CONFIRMED-RE | IDA verified — full Lua binding with hardpoint propagation (Q3/Q6/Q8) |
| `0x5819E0` | Make_Invulnerable_Lua | DISCOVERED-GHIDRA | Lua binding for invulnerability |
| `0x769C58` | operator new | CONFIRMED-FOCAPI | Phase 2 RE + FoCAPI match |

---

## 4. Combat System Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x3727A0` | GetMaxHealth | DISCOVERED-GHIDRA | Reads type+0xDCC, applies multipliers |
| `0x372320` | GetMaxFrontShield | DISCOVERED-GHIDRA | Reads type+0xDD0 |
| `0x3725F0` | GetMaxRearShield | DISCOVERED-GHIDRA | Reads type+0xDD4 |
| `0x3711C0` | GetAdjustedCost (variant) | DISCOVERED-GHIDRA | Cost modifier chain |
| `0x374DA0` | HasCombatBehavior | DISCOVERED-GHIDRA | Checked during spawn init |
| `0x3751A0` | FactionAffiliation_Check | DISCOVERED-GHIDRA | Used by SetTechLevel, CanProduce |
| `0x387010` | WeaponTick | DISCOVERED-GHIDRA | Per-frame weapon update, cooldown via delta-time |
| `0x387F50` | HardpointFire | DISCOVERED-GHIDRA | Per-hardpoint damage, station level loss |
| `0x38A350` | Take_Damage_Outer | DISCOVERED-GHIDRA | 8 invulnerability checks, top-level gate |
| `0x38D730` | FireControl_Dispatch | DISCOVERED-GHIDRA | Master fire control, enemy/invuln/LOS checks |
| `0x38EB10` | Death_Signal_Behavior | DISCOVERED-GHIDRA | Notifies behavior system of death |
| `0x38F8B0` | ClearSpeedOverride | CONFIRMED-AOB | Phase 2 RE, AOB verified unique |
| `0x396DF0` | GetHullPercentage | DISCOVERED-GHIDRA | Per-hardpoint hull ratio |
| `0x3963C0` | FrontShield_Read | DISCOVERED-GHIDRA | Reads front shield current value |
| `0x39BDB0` | Death_Handler | DISCOVERED-GHIDRA | Full death sequence (17 steps) |
| `0x3A06A0` | Property_Init_HP | DISCOVERED-GHIDRA | Initial HP from XML data system |
| `0x3A56B0` | Invulnerability_Cleanup | DISCOVERED-GHIDRA | Cleans up invulnerability state |
| `0x3A8630` | SetFrontShield | DISCOVERED-GHIDRA | Front shield write |
| `0x3A89D0` | **SetHP** | CONFIRMED-RUNTIME | Phase 1 RE + CT hook + AOB unique |
| `0x3A8C90` | SetSpeedOverride | CONFIRMED-AOB | Phase 2 RE, AOB verified unique |
| `0x3A91E0` | SetRearShield | DISCOVERED-GHIDRA | Rear shield write |
| `0x3A92F0` | AnimationDispatch | DISCOVERED-GHIDRA | Damage type routing, Berserker override |
| `0x3A97E0` | Take_Damage_PropertyDispatch | DISCOVERED-GHIDRA | Shield/hardpoint routing |
| `0x3AB890` | Take_Damage_Impl | DISCOVERED-GHIDRA | Core: old_hp - damage, prevent-death |
| `0x38C570` | BehaviorAttach | CONFIRMED-RE | IDA verified — attaches behavior object to unit (used by Make_Invulnerable) |
| `0x3A54C0` | BehaviorRemoveDispatch | CONFIRMED-RE | IDA verified — removes behavior from unit (used by Make_Invulnerable) |
| `0x3ABB80` | ~~Make_Invulnerable_Setter~~ **SetPosition** | CONFIRMED-RE | **IDA CORRECTION:** Phase 2 label was wrong — this is SetPosition/Teleport with QI(0x16) hardpoint propagation, NOT invulnerability |
| `0x3AC290` | DamageVisualLevel | DISCOVERED-GHIDRA | Damage model swap thresholds |
| `0x405230` | HullRatio_ViaHardpoints | DISCOVERED-GHIDRA | Aggregate health display |
| `0x4052D0` | GetHardpoint | CONFIRMED-RE | IDA verified via Make_Invulnerable call chain — returns hardpoint by index |
| `0x405300` | HardpointCount | CONFIRMED-RE | IDA verified via Make_Invulnerable call chain — returns number of hardpoints |
| `0x42DD63` | Lua_Set_Hull (caller) | DISCOVERED-GHIDRA | SetHP caller from Lua |
| `0x48EB10` | ScheduleHeroRespawn | CONFIRMED-RE | Phase 2 RE |
| `0x56BFB0` | FrontShield_Read_Impl | DISCOVERED-GHIDRA | Shield behavior read |
| `0x56C1B0` | FrontShield_Write_Impl | DISCOVERED-GHIDRA | Shield behavior write |
| `0x549490` | RearShield_Read_Impl | DISCOVERED-GHIDRA | Rear shield read |
| `0x549810` | RearShield_Write_Impl | DISCOVERED-GHIDRA | Rear shield write |
| `0x5D70F0` | HealthRegen_Periodic | DISCOVERED-GHIDRA | Natural HP regen |

### Combat Ability Constructors

| RVA | Ability Class | Status |
|-----|--------------|--------|
| `0x6EEAE0` | ArcSweepAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x6EE840` | AbsorbBlasterAbilityClass | DISCOVERED-GHIDRA |
| `0x6F22F0` | CableAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x6F42D0` | CombatBonusAbilityClass | DISCOVERED-GHIDRA |
| `0x6F52C0` | ConcentrateFireAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x6F7170` | EarthquakeAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x6F7A30` | EatAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x6F80D0` | PeriodicDamage (DoT) | DISCOVERED-GHIDRA |
| `0x6F9980` | EnergyWeaponAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x6FFFF0` | GenericAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x706280` | LuckyShotAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x706B10` | MaximumFirepowerAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x7048E0` | IonCannonShotAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x70A280` | ReduceProductionPriceAbilityClass | DISCOVERED-GHIDRA |
| `0x70B040` | ReduceProductionTimeAbilityClass | DISCOVERED-GHIDRA |
| `0x70EB50` | StarbaseUpgradeAbilityClass | DISCOVERED-GHIDRA |
| `0x710080` | TractorBeamAttackAbilityClass | DISCOVERED-GHIDRA |
| `0x712D20` | BerserkerAbilityClass | DISCOVERED-GHIDRA |
| `0x717530` | LeechShieldsAbilityClass | DISCOVERED-GHIDRA |
| `0x71B560` | DrainLifeAbilityClass | DISCOVERED-GHIDRA |
| `0x71C820` | ShieldFlareAbilityClass | DISCOVERED-GHIDRA |

### Combatant Behavior Classes

| RVA | Class | Status |
|-----|-------|--------|
| `0x6383E0` | CombatantBehaviorClass::ctor | DISCOVERED-GHIDRA |
| `0x6CB700` | BaseCombatantClass::ctor | DISCOVERED-GHIDRA |
| `0x6CBEE0` | CompanyCombatantClass::ctor | DISCOVERED-GHIDRA |
| `0x6CBA40` | SquadronCombatantClass::ctor | DISCOVERED-GHIDRA |

---

## 5. AI System Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x4747E0` | TheAIDataManagerClass::ctor | DISCOVERED-GHIDRA | Singleton |
| `0x4AF810` | AIPlayerClass::ctor | DISCOVERED-GHIDRA | Per-player AI controller |
| `0x4B0250` | Enable_As_Actor | CONFIRMED-RE | Lua binding for AI enable |
| `0x4B06D0` | Release_Credits_For_Tactical | CONFIRMED-RE | AI credits release |
| `0x4D9C80` | TheAIClass::ctor | DISCOVERED-GHIDRA | Top-level singleton |
| `0x4DAD80` | AIPerceptionSystemClass::ctor | DISCOVERED-GHIDRA | Base perception |
| `0x4E1880` | GalacticPerceptionSystemClass::ctor | DISCOVERED-GHIDRA | Galactic mode |
| `0x4E8920` | TheAITemplateManagerClass::ctor | DISCOVERED-GHIDRA | Singleton |
| `0x4E9940` | TheAIPlayerTypeManagerClass::ctor | DISCOVERED-GHIDRA | Singleton |
| `0x524CE0` | AIExecutionSystemClass::ctor | DISCOVERED-GHIDRA | |
| `0x585D00` | AILearningSystemClass::ctor | DISCOVERED-GHIDRA | 5 hash maps |
| `0x5E5C30` | TheAIGoalProposalFunctionSetManagerClass::ctor | DISCOVERED-GHIDRA | |
| `0x5E6690` | TheAIGoalTypeManagerClass::ctor | DISCOVERED-GHIDRA | |
| `0x6109C0` | AIBudgetClass::ctor | DISCOVERED-GHIDRA | |
| `0x610A70` | BudgetedCategoryStruct::ctor | DISCOVERED-GHIDRA | |
| `0x6478C0` | AIBuildTaskClass::ctor | DISCOVERED-GHIDRA | FSM, 25 states |
| `0x64AEC0` | AIBuildTaskClass::StateMachineHandler | DISCOVERED-GHIDRA | |
| `0x64C250` | ServicedAISystemClass::ctor | DISCOVERED-GHIDRA | Base subsystem |
| `0x653340` | TacticalPerceptionGridClass::ctor | DISCOVERED-GHIDRA | |
| `0x6954B0` | AIPlanetBuildTaskClass::ctor | DISCOVERED-GHIDRA | |
| `0x6B8480` | GalacticGoalSystemClass::ctor | DISCOVERED-GHIDRA | |
| `0x6B86E0` | LandGoalSystemClass::ctor | DISCOVERED-GHIDRA | |
| `0x6B88D0` | SpaceGoalSystemClass::ctor | DISCOVERED-GHIDRA | |
| `0x6B8980` | LandPerceptionSystemClass::ctor | DISCOVERED-GHIDRA | |
| `0x6B9A20` | SpacePerceptionSystemClass::ctor | DISCOVERED-GHIDRA | |
| `0x6BAC00` | AIPlanningSystemClass::ctor | DISCOVERED-GHIDRA | 4.0s interval |
| `0x6BB9E0` | AITemplateSystemClass::ctor | DISCOVERED-GHIDRA | |
| `0x6C6500` | ProduceForceBlockStatus::ctor | DISCOVERED-GHIDRA | |
| `0x6C7970` | AIGoalSystemClass::ctor | DISCOVERED-GHIDRA | |

---

## 6. Galactic Map Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x3F3340` | PlanetaryBehaviorClass::dtor | DISCOVERED-GHIDRA | Links planet to data pack |
| `0x3F6AF0` | Planet_ComputeIncomeValue | DISCOVERED-GHIDRA | Called during ownership changes |
| `0x3F8AA0` | BuildPad_CheckSlots | DISCOVERED-GHIDRA | Validates build pad availability |
| `0x3F8B30` | BuildPad_CheckSecondary | DISCOVERED-GHIDRA | Secondary slot check |
| `0x3FA160` | PlanetFactionChange_InitialSet | DISCOVERED-GHIDRA | Initial ownership assignment |
| `0x3FB040` | PlanetFactionChange_FullTransfer | DISCOVERED-GHIDRA | Full ownership transfer with UI |
| `0x3FE810` | CaptureTimer_Update | DISCOVERED-GHIDRA | Writes capture timer fields |
| `0x4B1270` | GalacticModeClass::ctor | DISCOVERED-GHIDRA | |
| `0x4B5DF0` | LineLinkStruct::dtor | DISCOVERED-GHIDRA | |
| `0x4B5E60` | PersistentTacticalBuiltObjectStruct::dtor | DISCOVERED-GHIDRA | |
| `0x4B5ED0` | PersistentUpgradeObjectStruct::dtor | DISCOVERED-GHIDRA | |
| `0x4B5F40` | TradeRouteLinkEntryClass::dtor | DISCOVERED-GHIDRA | |
| `0x4AE5E0` | DVec<TradeRouteClass const*>::dtor | DISCOVERED-GHIDRA | |
| `0x663530` | PlanetReachabilityClass::ctor | DISCOVERED-GHIDRA | 11 slots |
| `0x6A53B0` | FOW_Undo_Reveal_All | DISCOVERED-GHIDRA | Sets global toggle |
| `0x331CC0` | FindPlanetType | DISCOVERED-GHIDRA | Checks is_planet flag |

---

## 7. Production System Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x2804D0` | CanProduce | DISCOVERED-GHIDRA | 18 prerequisite gates |
| `0x2AC320` | CheckPopCap | DISCOVERED-GHIDRA | Unit cap validation |
| `0x3FFF10` | ProductionBehaviorClass::dtor | DISCOVERED-GHIDRA | |
| `0x3FFF70` | ObjectUnderConstructionClass::ctor | DISCOVERED-GHIDRA | 0x38 bytes |
| `0x400240` | GetAdjustedCost | DISCOVERED-GHIDRA | Modified cost calculation |
| `0x400370` | ComputeBuildTime | DISCOVERED-GHIDRA | 5-factor formula |
| `0x42D850` | TacticalBuildObjectsBehaviorClass::dtor | DISCOVERED-GHIDRA | |
| `0x42E890` | Production_CompletionHandler | DISCOVERED-GHIDRA | Spawns finished unit |
| `0x497900` | ObjectUnderConstruction_Serialize | DISCOVERED-GHIDRA | Save/load |
| `0x523F50` | ProductionEventClass::ctor | DISCOVERED-GHIDRA | Event ID=7 |
| `0x5242C0` | ObjectUnderConstructionClass::dtor | DISCOVERED-GHIDRA | |
| `0x559680` | ProductionDataPackClass::ctor | DISCOVERED-GHIDRA | 2 queues |
| `0x561270` | TacticalBuildObjectsDataPackClass::ctor | DISCOVERED-GHIDRA | |
| `0x5D7740` | TacticalBuildEventClass::ctor | DISCOVERED-GHIDRA | Event ID=33 |
| `0x532690` | GalacticSellEventClass::ctor | DISCOVERED-GHIDRA | Event ID=46 |

---

## 8. Story System Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x215A30` | CRC32_Hash | CONFIRMED-RE | Standard CRC32 with table at 0xA14D20 |
| `0x242570` | LuaScriptClass::ctor | DISCOVERED-GHIDRA | |
| `0x2567B0` | LuaScriptClass::RegisterGlobals | CONFIRMED-RE | 14 globals including GetEvent |
| `0x4501D0` | StoryEventClass::ctor | CONFIRMED-RE | Size >= 0x360 |
| `0x450600` | StoryEventClass::dtor | CONFIRMED-RE | |
| `0x4504E0` | StoryEventCommandUnitClass::ctor | DISCOVERED-GHIDRA | |
| `0x450540` | StoryEventHeroMoveClass::ctor | DISCOVERED-GHIDRA | |
| `0x452D70` | StorySubPlot_ComputeDependencies | CONFIRMED-RE | Builds dependency graph |
| `0x453310` | StoryEvent_Factory_Create | CONFIRMED-RE | 61 type IDs, switch/case |
| `0x4562A0` | StoryEvent_BuildFromParsed | DISCOVERED-GHIDRA | |
| `0x45C3F0` | ResolveRewardTypeFromString | CONFIRMED-RE | CRC32 tree lookup |
| `0x45C5D0` | StoryEvent_ParseXMLBlock | DISCOVERED-GHIDRA | |
| `0x467540` | StoryEvent_DebugLog | DISCOVERED-GHIDRA | |
| `0x47D6B0` | DVec<StoryDialogGoal>::dtor | DISCOVERED-GHIDRA | |
| `0x52D400` | StorySubPlotClass::ctor | CONFIRMED-RE | Size >= 0x650 |
| `0x52E220` | StorySubPlotClass::dtor | CONFIRMED-RE | |
| `0x52EF90` | StorySubPlot_FindEventByName | CONFIRMED-RE | CRC32 XOR 0xDEADBEEF + LCG |
| `0x52FC10` | StorySubPlot_Reset | CONFIRMED-RE | Iterates events, clears +0x4C |
| `0x530180` | StorySubPlot_BuildTypeArrays | DISCOVERED-GHIDRA | 61-slot indexed arrays |
| `0x5DC4B0` | StoryEventSelectPlanetClass::ctor | DISCOVERED-GHIDRA | |
| `0x724120` | StoryPlotWrapper::LuaConstructor | CONFIRMED-RE | 5 methods |
| `0x724480` | StoryPlotWrapper::Get_Event | CONFIRMED-RE | |
| `0x7246E0` | StoryPlotWrapper::Reset | CONFIRMED-RE | |
| `0x73DC80` | StoryEventWrapper::LuaConstructor | CONFIRMED-RE | 7 methods |
| `0x73DF60` | StoryEventWrapper::Add_Dialog_Text | CONFIRMED-RE | |
| `0x73E4F0` | StoryEventWrapper::Clear_Dialog_Text | CONFIRMED-RE | |
| `0x73E590` | WrapEventAsLuaObject | DISCOVERED-GHIDRA | |
| `0x73E9A0` | StoryEventWrapper::Set_Dialog | CONFIRMED-RE | |
| `0x73ECA0` | StoryEventWrapper::Set_Reward_Parameter | CONFIRMED-RE | |
| `0x73EEF0` | StoryEventWrapper::Set_Reward_Type | CONFIRMED-RE | |

---

## 9. Camera and Selection Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x14EE30` | GetScreenAspectRatio | DISCOVERED-GHIDRA | Returns float at 0xA12550 |
| `0x141260` | RebuildProjectionMatrix | DISCOVERED-GHIDRA | Perspective/ortho dispatch |
| `0x1417C0` | ProjectionMatrix_Dispatch | DISCOVERED-GHIDRA | |
| `0x22D390` | ComputeInverseMatrix | DISCOVERED-GHIDRA | |
| `0x232C50` | BuildPerspectiveViewProjectionMatrix | CONFIRMED-RE | |
| `0x232DF0` | BuildOrthoViewProjectionMatrix | CONFIRMED-RE | |
| `0x261470` | CameraClass::ctor | CONFIRMED-RE | Sub-object 0x308 bytes |
| `0x261590` | CameraClass::CopyFrom | CONFIRMED-RE | |
| `0x261690` | CameraClass::GetForwardDirection | CONFIRMED-RE | Negated +0x08,+0x18 |
| `0x261870` | CameraClass::GetViewport | DISCOVERED-GHIDRA | |
| `0x2618E0` | CameraClass::GetFovAspect | CONFIRMED-RE | Reads +0x48, +0x4C |
| `0x261900` | CameraClass::GetFrustumBounds | CONFIRMED-RE | |
| `0x2619F0` | CameraClass::GetTransformMatrix | CONFIRMED-RE | 12 floats |
| `0x261A40` | CameraClass::GetPosition | CONFIRMED-RE | +0x0C, +0x1C, +0x2C |
| `0x261A80` | CameraClass::SetFovAspect | CONFIRMED-RE | |
| `0x261AB0` | CameraClass::SetPerspectiveProjection | CONFIRMED-RE | |
| `0x261B50` | CameraClass::SetOrthoProjection | CONFIRMED-RE | |
| `0x261BD0` | CameraClass::SetTransformMatrix | CONFIRMED-RE | |
| `0x261C90` | CameraClass::TranslateLocal | CONFIRMED-RE | |
| `0x261E00` | CameraClass::SetViewport | CONFIRMED-RE | |
| `0x2611C0` | CameraClass::Render | DISCOVERED-GHIDRA | |
| `0x28A950` | GameModeManagerClass::GetModeByType | CONFIRMED-RE | Iterates modes |
| `0x28D930` | Selection_WriteState | DISCOVERED-GHIDRA | Writes to GMM+0xB4 |
| `0x2CF8D0` | MatrixRotation | DISCOVERED-GHIDRA | Camera orbit rotation |
| `0x3AC9D0` | SelectEventClass::ctor | CONFIRMED-RE | Event ID=5 |
| `0x3C2B20` | GalacticCameraClass::ctor | CONFIRMED-RE | |
| `0x3C2C00` | GalacticCameraClass::Update | CONFIRMED-RE | Main frame update |
| `0x3C1960` | SelectBehaviorClass::dtor | CONFIRMED-RE | 0x58 bytes |
| `0x436060` | ControlGroupEventClass::ctor | CONFIRMED-RE | Event ID=30 |
| `0x437F40` | SelectAllEventClass::ctor | CONFIRMED-RE | Event ID=37 |
| `0x55BE30` | SelectionDataPackClass::ctor | CONFIRMED-RE | Pool size 20 |
| `0x689670` | LookEventClass::ctor | CONFIRMED-RE | Event ID=4 |

---

## 10. Save System Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x2043B0` | Read_Int | DISCOVERED-GHIDRA | Serialization primitive |
| `0x2046F0` | Write_Int | DISCOVERED-GHIDRA | Serialization primitive |
| `0x204AD0` | Read_String | DISCOVERED-GHIDRA | wstring deserialization |
| `0x204FB0` | Write_String | DISCOVERED-GHIDRA | wstring serialization |
| `0x213010` | FileClass::ctor (default) | DISCOVERED-GHIDRA | |
| `0x2130D0` | FileClass::ctor (cstring) | DISCOVERED-GHIDRA | |
| `0x2131E0` | FileClass::ctor (stdstring) | DISCOVERED-GHIDRA | |
| `0x2132F0` | FileClass::dtor | DISCOVERED-GHIDRA | |
| `0x213600` | FileClass::Open | DISCOVERED-GHIDRA | CreateFileA wrapper |
| `0x21FCC0` | ChunkWriterClass::ctor | DISCOVERED-GHIDRA | |
| `0x21FDC0` | ChunkWriterClass::dtor | DISCOVERED-GHIDRA | |
| `0x21FE20` | ChunkWriterClass::Open_Chunk | DISCOVERED-GHIDRA | |
| `0x21FEB0` | ChunkWriterClass::Close_Chunk | DISCOVERED-GHIDRA | |
| `0x21FFA0` | ChunkWriterClass::Open_Micro_Chunk | DISCOVERED-GHIDRA | |
| `0x220030` | ChunkWriterClass::Close_Micro_Chunk | DISCOVERED-GHIDRA | |
| `0x2200B0` | ChunkWriterClass::Write | DISCOVERED-GHIDRA | |
| `0x220140` | ChunkWriterClass::Write_CString | DISCOVERED-GHIDRA | strlen+1 bytes |
| `0x220280` | ChunkReaderClass::ctor | DISCOVERED-GHIDRA | |
| `0x220370` | ChunkReaderClass::dtor | DISCOVERED-GHIDRA | |
| `0x2203F0` | ChunkReaderClass::Get_File_Size | DISCOVERED-GHIDRA | |
| `0x220460` | ChunkReaderClass::Tell | DISCOVERED-GHIDRA | |
| `0x2204A0` | ChunkReaderClass::Open_Chunk | DISCOVERED-GHIDRA | |
| `0x220520` | ChunkReaderClass::Close_Chunk | DISCOVERED-GHIDRA | |
| `0x2227E0` | RAMFileClass::ctor1 | DISCOVERED-GHIDRA | |
| `0x222830` | RAMFileClass::ctor2 | DISCOVERED-GHIDRA | |
| `0x222880` | RAMFileClass::ctor3 | DISCOVERED-GHIDRA | |
| `0x2228D0` | RAMFileClass::dtor | DISCOVERED-GHIDRA | |
| `0x056360` | SaveGameStruct_Vector::ctor | DISCOVERED-GHIDRA | |
| `0x0581C0` | SaveGameStruct_Vector::dtor | DISCOVERED-GHIDRA | |
| `0x48FA80` | SaveGameEventClass::ctor | DISCOVERED-GHIDRA | Event ID=36 |
| `0x48FAD0` | SaveGameEventClass::dtor | DISCOVERED-GHIDRA | |
| `0x48FB20` | SaveGameEventClass::Deserialize | DISCOVERED-GHIDRA | |
| `0x48FB90` | SaveGameEventClass::Serialize | DISCOVERED-GHIDRA | |
| `0x48FC00` | SaveGameEventClass::Execute | DISCOVERED-GHIDRA | |
| `0x4B5FB0` | PlanetaryDataPackClass::dtor | DISCOVERED-GHIDRA | |
| `0x4F2D80` | tPersistentUnit_Vector::ctor | DISCOVERED-GHIDRA | |
| `0x4F2FF0` | tPersistentUnit_Vector::iterate | DISCOVERED-GHIDRA | |

### Compression (zlib, statically linked)

| RVA | Function | Status |
|-----|----------|--------|
| `0x7A1470` | compress2 | DISCOVERED-GHIDRA |
| `0x7A1590` | uncompress2 | DISCOVERED-GHIDRA |
| `0x7A3080` | deflateInit_ | DISCOVERED-GHIDRA |
| `0x7A2DF0` | deflateInit2_ | DISCOVERED-GHIDRA |
| `0x7A54F0` | inflateInit_ | DISCOVERED-GHIDRA |
| `0x7A60A0` | compress_block | DISCOVERED-GHIDRA |

---

## 11. Network System Functions

| RVA | Function | Status | Verification |
|-----|----------|--------|-------------|
| `0x6A000` | SteamClass::ctor | DISCOVERED-GHIDRA | Singleton |
| `0x6B370` | SteamAsyncSocketImpl::ctor | DISCOVERED-GHIDRA | Steam P2P |
| `0x6CA10` | SteamPeerLobbyClass::ctor | DISCOVERED-GHIDRA | 6 callbacks + 3 call results |
| `0x72540` | Steam_LobbyInvite_Handler | DISCOVERED-GHIDRA | Callback ID 503 |
| `0x72550` | Steam_LobbyKicked_Handler | DISCOVERED-GHIDRA | Callback ID 512 |
| `0x72570` | Steam_LobbyChatMsg_Handler | DISCOVERED-GHIDRA | Callback ID 507 |
| `0x726C0` | Steam_LobbyDataUpdate_Handler | DISCOVERED-GHIDRA | Callback ID 505 |
| `0x726D0` | Steam_LobbyChatUpdate_Handler | DISCOVERED-GHIDRA | Callback ID 506 |
| `0x72730` | Steam_ItemInstalled_Handler | DISCOVERED-GHIDRA | Callback ID 3405 |
| `0x2054C0` | PacketHandlerClass::dtor | DISCOVERED-GHIDRA | Thread-based |
| `0x227110` | WinsockAsyncSocketImpl::ctor | DISCOVERED-GHIDRA | LAN/direct-IP |
| `0x23BC40` | PacketClass::ctor | DISCOVERED-GHIDRA | BitStreamClass-based |
| `0x5126C0` | EventClass::ctor | DISCOVERED-GHIDRA | Base event |
| `0x512C50` | ScheduledEventClass::ctor | DISCOVERED-GHIDRA | Frame-scheduled |
| `0x4B39E0` | EventQueueClass::ctor | DISCOVERED-GHIDRA | |
| `0x5960F0` | BaseEventFactoryClass::ctor | DISCOVERED-GHIDRA | Type registry |

### Network Event Constructors (58 Events)

| RVA | Event Class | ID | Status |
|-----|-----------|-----|--------|
| `0x4C1B00` | FrameInfoEventClass | 0 | DISCOVERED-GHIDRA |
| `0x3ACEE0` | MoveToPositionEventClass | 1 | DISCOVERED-GHIDRA |
| `0x3AEE50` | MoveToObjectEventClass | 2 | DISCOVERED-GHIDRA |
| `0x3AF290` | MoveObjectToObjectEventClass | 3 | DISCOVERED-GHIDRA |
| `0x689670` | LookEventClass | 4 | CONFIRMED-RE |
| `0x3AC9D0` | SelectEventClass | 5 | CONFIRMED-RE |
| `0x3AF4C0` | AttackEventClass | 6 | DISCOVERED-GHIDRA |
| `0x523F50` | ProductionEventClass | 7 | DISCOVERED-GHIDRA |
| `0x5AF020` | FleetManagementEventClass | 8 | DISCOVERED-GHIDRA |
| `0x48FCE0` | InvadeEventClass | 9 | DISCOVERED-GHIDRA |
| `0x3C41C0` | MoveThroughObjectsEventClass | 10 | DISCOVERED-GHIDRA |
| `0x4AD0F0` | DebugEventClass | 11 | DISCOVERED-GHIDRA |
| `0x403AB0` | ReinforceEventClass | 12 | DISCOVERED-GHIDRA |
| `0x4B4210` | SpecialAbilityEventClass | 13 | DISCOVERED-GHIDRA |
| `0x3AE4A0` | MoveToRayEventClass | 14 | DISCOVERED-GHIDRA |
| `0x4993F0` | QuitGameEventClass | 15 | DISCOVERED-GHIDRA |
| `0x44D1D0` | ChatEventClass | 16 | DISCOVERED-GHIDRA |
| `0x4C1D30` | FrameSyncEventClass | 17 | DISCOVERED-GHIDRA |
| `0x4C1EB0` | PerformanceMetricsEventClass | 18 | DISCOVERED-GHIDRA |
| `0x3AE950` | MoveToRayFacingEventClass | 19 | DISCOVERED-GHIDRA |
| `0x409B40` | FacingEventClass | 20 | DISCOVERED-GHIDRA |
| `0x3B0010` | EscortEventClass | 21 | DISCOVERED-GHIDRA |
| `0x6899C0` | CinematicAnimationEventClass | 26 | DISCOVERED-GHIDRA |
| `0x524560` | BombingRunEventClass | 28 | DISCOVERED-GHIDRA |
| `0x436060` | ControlGroupEventClass | 30 | CONFIRMED-RE |
| `0x5AEA90` | SetupPhaseMoveEventClass | 31 | DISCOVERED-GHIDRA |
| `0x5AEF30` | SetupPhaseTriggerEndEventClass | 32 | DISCOVERED-GHIDRA |
| `0x5D7740` | TacticalBuildEventClass | 33 | DISCOVERED-GHIDRA |
| `0x689810` | TacticalSellEventClass | 34 | DISCOVERED-GHIDRA |
| `0x689AD0` | AllyEventClass | 35 | DISCOVERED-GHIDRA |
| `0x48FA80` | SaveGameEventClass | 36 | DISCOVERED-GHIDRA |
| `0x437F40` | SelectAllEventClass | 37 | CONFIRMED-RE |
| `0x44D530` | GameOptionsEventClass | 39 | DISCOVERED-GHIDRA |
| `0x4292E0` | TacticalSpecialAbilityEventClass | 40 | DISCOVERED-GHIDRA |
| `0x498CA0` | DistributeMoneyEventClass | 42 | DISCOVERED-GHIDRA |
| `0x532690` | GalacticSellEventClass | 46 | DISCOVERED-GHIDRA |
| `0x3ADD90` | MoveToPositionFacingEventClass | 47 | DISCOVERED-GHIDRA |
| `0x4ADED0` | WithdrawlEventClass | 49 | DISCOVERED-GHIDRA |
| `0x4D6A60` | TauntEventClass | 50 | DISCOVERED-GHIDRA |
| `0x4ADFD0` | ResumeGameEventClass | 51 | DISCOVERED-GHIDRA |
| `0x440AD0` | GarrisonEventClass | 52 | DISCOVERED-GHIDRA |
| `0x52AFC0` | PlanetaryBombardEventClass | 54 | DISCOVERED-GHIDRA |
| `0x3AD3B0` | MoveToGarrisonEventClass | 55 | DISCOVERED-GHIDRA |

---

## 12. Game Mode Functions

| RVA | Function | Status |
|-----|----------|--------|
| `0x35A5E0` | GameModeClass::ctor | DISCOVERED-GHIDRA |
| `0x35AD70` | GameModeClass::dtor | DISCOVERED-GHIDRA |
| `0x3B5210` | LandModeClass::ctor | DISCOVERED-GHIDRA |
| `0x4B1270` | GalacticModeClass::ctor | DISCOVERED-GHIDRA |
| `0x4D6BA0` | SpaceModeClass::ctor | DISCOVERED-GHIDRA |

---

## 13. Signal System Functions

| RVA | Function | Status |
|-----|----------|--------|
| `0x220ED0` | Signal_Fire | DISCOVERED-GHIDRA |
| `0x240610` | SignalGeneratorClass::ctor | DISCOVERED-GHIDRA |
| `0x2406C0` | SignalListClass::ctor | DISCOVERED-GHIDRA |
| `0x2215B0` | SignalDispatcherClass::ctor1 | DISCOVERED-GHIDRA |
| `0x2218C0` | SignalDispatcherClass::ctor2 | DISCOVERED-GHIDRA |

---

## 14. Miscellaneous / Utility Functions

| RVA | Function | Status | Source |
|-----|----------|--------|--------|
| `0x245790` | LuaScriptClass::Get_Script_From_State | CONFIRMED-FOCAPI | |
| `0x247700` | LuaScriptClass::Map_Var_To_Lua | CONFIRMED-FOCAPI | |
| `0x24BE40` | LuaUserVar::Register_Member | CONFIRMED-FOCAPI | |
| `0x256D40` | LuaUserVar::Return_Variable | CONFIRMED-FOCAPI | |
| `0x1FA680` | GameTextClass::Get | CONFIRMED-FOCAPI | |
| `0x604A10` | GameObjectTypeWrapper::ctor | CONFIRMED-FOCAPI | |
| `0x025760` | Log_Printf | DISCOVERED-GHIDRA | Engine logging |
| `0x17D070` | Debug_Should_Issue_Event_Alert | DISCOVERED-GHIDRA | |
| `0x29F810` | General_Object_Spawn | DISCOVERED-GHIDRA | |
| `0x265AE0` | Destroy_Visual_Representation | DISCOVERED-GHIDRA | |
| `0x264A40` | QueryAnimationCount | DISCOVERED-GHIDRA | |
| `0x1FFB40` | RandomSelection_WithinCount | DISCOVERED-GHIDRA | |
| `0x4D07E0` | SpawnWreckage | DISCOVERED-GHIDRA | Death debris |
| `0x33FB70` | ComputeAbilityBonus | DISCOVERED-GHIDRA | |
| `0x38C850` | CombatBonus_ApplyStats | DISCOVERED-GHIDRA | 8 combat stats |
| `0x55A010` | AbilityModifier_Accumulate | DISCOVERED-GHIDRA | Tree-based accumulator |

---

## 15. Vtable Addresses

| RVA | Class | Status |
|-----|-------|--------|
| `0x8661B8` | GameObjectClass (primary) | CONFIRMED-RUNTIME |
| `0x8661D8` | MultiLinkedListMember (in GOC) | CONFIRMED-RE |
| `0x866200` | CullObjectClass (in GOC) | CONFIRMED-RE |
| `0x866210` | SignalGeneratorClass (in GOC) | CONFIRMED-RE |
| `0x866228` | Unknown base 5 (in GOC) | CONFIRMED-RE |
| `0x8762C0` | GalacticModeClass | DISCOVERED-GHIDRA |
| `0x878D58` | ProjectileBehaviorClass | DISCOVERED-GHIDRA |
| `0x899458` | ShieldBehaviorClass (rear) | DISCOVERED-GHIDRA |
| `0x8BF6C0` | BaseCombatantClass | DISCOVERED-GHIDRA |

---

## 16. Global Data Addresses

| RVA | Name | Type | Status |
|-----|------|------|--------|
| `0xA12550` | screen_aspect_ratio | float32 | CONFIRMED-RE |
| `0xA14D20` | CRC32_lookup_table | uint32[256] | CONFIRMED-RE |
| `0xA15738` | script_id_counter | int32 | DISCOVERED-GHIDRA |
| `0xA157C0` | NumberTypeMarker | marker | DISCOVERED-GHIDRA |
| `0xA157D0` | StringTypeMarker | marker | DISCOVERED-GHIDRA |
| `0xA16FB0` | PlayerCount_static | uint32 | DISCOVERED-GHIDRA |
| `0xA16FD0` | PlayerListClass* | pointer | CONFIRMED-FOCAPI |
| `0xA16FD8` | PlayerListClass_end | pointer | DISCOVERED-GHIDRA |
| `0xA16FF0` | PlayerArray | pointer | CONFIRMED-RUNTIME |
| `0xA16FF8` | PlayerCount | int32 | CONFIRMED-RUNTIME |
| `0xA172D0` | GameObjectTypeList* | pointer | DISCOVERED-GHIDRA |
| `0xA284C4` | FOW_GlobalToggle | bool | DISCOVERED-GHIDRA |
| `0xA43B18` | GameObjectTypeMarker | marker | DISCOVERED-GHIDRA |
| `0xA44270` | PlayerTypeMarker | marker | DISCOVERED-GHIDRA |
| `0xA573D0` | FOW_data | pointer | CONFIRMED-AOB |
| `0xA7BC58` | TheGameText | pointer | CONFIRMED-FOCAPI |
| `0xB0A320` | game_speed_numerator | int/float | DISCOVERED-GHIDRA |
| `0xB0A340` | game_speed_denominator | int/float | DISCOVERED-GHIDRA |
| `0xB153E0` | GameModeManagerClass* | pointer | CONFIRMED-RE |
| `0xB15418` | GameModeManager_active_mode | pointer | CONFIRMED-RE |
| `0xB15920` | build_time_global_scalar | float | DISCOVERED-GHIDRA |
| `0xB1599C` | galactic_camera_zoom_angle | float32 | CONFIRMED-RE |
| `0xB159A0` | galactic_camera_target_zoom | float32 | CONFIRMED-RE |
| `0xB159A4` | galactic_camera_distance | float32 | CONFIRMED-RE |
| `0xB159A8` | galactic_camera_distance_scale | float32 | CONFIRMED-RE |
| `0xB159AC` | galactic_camera_look_dir_x | float32 | CONFIRMED-RE |
| `0xB159B0` | galactic_camera_look_dir_y | float32 | CONFIRMED-RE |
| `0xB159B4` | galactic_camera_look_dir_z | float32 | CONFIRMED-RE |
| `0xB159C8` | galactic_camera_mode_value | float32 | CONFIRMED-RE |
| `0xB16DC8` | hard_hp_multiplier | float | DISCOVERED-GHIDRA |
| `0xB16DCC` | easy_hp_multiplier | float | DISCOVERED-GHIDRA |
| `0xB169F0` | Default_Hero_Respawn_Time | float | CONFIRMED-RE |
| `0xB27F60` | TheCommandBar | pointer | CONFIRMED-FOCAPI |
| `0xB30728` | EventTypeTree (story/reward) | pointer | CONFIRMED-RE |
| `0xB30738` | XML_NestingDepthCounter | int | DISCOVERED-GHIDRA |
| `0xB313D8` | SaveGame_dispatch_fptr | pointer | DISCOVERED-GHIDRA |
| `0xB313E0` | SaveGameEventFactory_singleton | pointer | DISCOVERED-GHIDRA |
| `0xB36BC1` | EventFactory_registry | DVec | DISCOVERED-GHIDRA |
| `0xB3B450` | RewardParam_static_buffer | pointer | DISCOVERED-GHIDRA |
| `0xB3B458` | RewardParam_buffer_end | pointer | DISCOVERED-GHIDRA |
| `0xB3B468` | Dialog_init_guard | int | DISCOVERED-GHIDRA |
| `0x803514` | damage_threshold_1 | float | DISCOVERED-GHIDRA |
| `0x8007C0` | damage_threshold_2 | float | DISCOVERED-GHIDRA |
| `0xA13DB0` | ObjectPointerPairClass_Vector | DVec | DISCOVERED-GHIDRA |

---

## 17. From Community Sources (Not Independently Verified)

### Apocalypticx CE Tables

| RVA | Description | Status |
|-----|-------------|--------|
| `0x451974` | Fog of war visibility check | CONFIRMED-AOB |
| `0x333E73` | Build time/cost (Skirmish) | CONFIRMED-AOB |
| `0x3A1B4C` | Build time/cost (Conquest) | CONFIRMED-AOB |
| `0x28DF6F` | Unit cap calculation (GC) | CONFIRMED-AOB |

---

## Summary Statistics

| Category | Count |
|----------|-------|
| CONFIRMED-RUNTIME | 18 |
| CONFIRMED-RE | 68 |
| CONFIRMED-FOCAPI | 12 |
| CONFIRMED-AOB | 9 |
| CONFIRMED-RTTI | 1703 classes |
| DISCOVERED-GHIDRA | 190+ |
| ESTIMATED | 3 |
| **Total unique RVAs** | **280+** |

---

*End of Verified RVAs v3.0*
*Generated by Agent 5A -- Master Knowledge Base Consolidation*
*All RVAs relative to module base. Add 0x140000000 for Ghidra absolute addresses.*
