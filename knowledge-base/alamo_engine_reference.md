# Alamo Engine Reference Manual
## Star Wars: Empire at War -- Forces of Corruption (x86_64 Steam Build)
### Binary: StarWarsG.exe v1.121.13.7360 | MSVC | Image Size: 13,369,344 bytes

**Document Version:** 3.0 (Phase 3 Consolidation)
**Analysis Date:** 2026-04-04
**Base Address (Ghidra):** 0x140000000
**RTTI Classes Recovered:** 1,703

---

## Table of Contents

1. [Engine Architecture Overview](#1-engine-architecture-overview)
2. [PlayerClass (55 Fields, 0x4D0 Bytes)](#2-playerclass)
3. [GameObjectClass (130+ Fields, 0x3C8+ Bytes)](#3-gameobjectclass)
4. [Combat System](#4-combat-system)
5. [AI System](#5-ai-system)
6. [Galactic Map System](#6-galactic-map-system)
7. [Production System](#7-production-system)
8. [Story System](#8-story-system)
9. [Camera and Selection](#9-camera-and-selection)
10. [Network and Multiplayer](#10-network-and-multiplayer)
11. [Save File Format](#11-save-file-format)
12. [Lua Integration](#12-lua-integration)
13. [Complete Function RVA Table](#13-complete-function-rva-table)
14. [Global Data Addresses](#14-global-data-addresses)
15. [Modding Interception Points](#15-modding-interception-points)

---

## 1. Engine Architecture Overview

The Alamo engine is a Petroglyph Studios engine descended from Westwood Studios' W3D technology. The SWFOC x86_64 Steam build uses MSVC compilation, ASLR-enabled PE, and statically links Lua 5.0.2, zlib, and D3DX. The engine uses a component-based architecture where GameObjectClass instances are augmented with BehaviorClass components resolved via QueryInterface.

**Key Architectural Patterns:**
- **Singleton pattern** for managers (TheAIClass, GameModeManagerClass, PlayerListClass, SignalDispatcherClass)
- **Component/QueryInterface system** for GameObjectClass extensibility (0x46+ query types)
- **DynamicVectorClass<T>** as the universal growable array (vtable + data ptr + count + capacity)
- **MSVC SSO strings** with inline threshold of 15 chars (wchar_t: 7 chars / 14 bytes)
- **RefCountClass** base for ref-counted objects
- **SignalGeneratorClass / SignalListenerClass** publish-subscribe pattern
- **FiniteStateMachineClass<T>** for state-driven subsystems (AI build tasks)
- **PooledObjectClass<T, N>** for frequently allocated objects (planets, selection data)
- **Hierarchical chunk I/O** (W3D-derived) for save files and asset loading

---

## 2. PlayerClass

**RTTI:** `.?AVPlayerClass@@`
**Actual Class Name:** PlayerClass (previously informally called "PlayerObject")
**Inherits:** RefCountClass, SignalGeneratorClass
**Estimated Size:** 1,232 bytes (0x4D0)
**Destructor RVA:** 0x27ED40
**Access:** PlayerListClass global at RVA 0xA16FD0, direct array at 0xA16FF0

### Key Design Notes
- AIPlayerClass at +0x360 is a **separate heap object**, NOT inlined
- PlayerWrapper (Lua binding) stores the PlayerClass pointer at wrapper+0x28
- Diplomacy is stored as an int32 array at +0x370: 0=ally, 1=enemy, indexed by player_id
- 15+ DynamicVectorClass instances are inlined (buildable types, locked/unlocked tech, etc.)

### Field Table (All 55 Fields)

| Offset | Type | Name | Status | Evidence |
|--------|------|------|--------|----------|
| 0x00 | pointer | vtable_ptr | CONFIRMED | Standard vtable |
| 0x08 | struct (8B) | refcount_data | DISCOVERED | RefCountClass base |
| 0x10 | struct (32B) | signal_generator_base | DISCOVERED | SignalGeneratorClass base |
| 0x37 | uint8 | playable | CONFIRMED | 2=playable, 3=allied |
| 0x48 | int32 | slot_index | CONFIRMED | Matches GameObjectClass+0x58 |
| 0x4C | int32 | player_id | CONFIRMED | Primary player identifier, used by AddCredits, SetTechLevel, Lua_Select_Object |
| 0x54 | int32 | team_id | DISCOVERED | Getter at 0x602EE0 returns as double |
| 0x58 | int32 | ai_player_type_id | DISCOVERED | AIPlayerClass ctor stores at AI+0x58 |
| 0x62 | uint8 | local_player | CONFIRMED | 1=local human, 0=others |
| 0x68 | pointer | faction_ref | CONFIRMED | FactionClass ptr; name at *(faction+0x28) |
| 0x70 | float32 | credits | CONFIRMED | AddCredits RVA 0x27F370 reads/writes here |
| 0x74 | float32 | max_credits | CONFIRMED | -1 = uncapped |
| 0x84 | int32 | tech_level | CONFIRMED | SetTechLevel RVA 0x288980 |
| 0x88 | int32 | max_tech_level | CONFIRMED | Clamp ceiling for SetTechLevel |
| 0x8C | DVec<int> (24B) | dvec_int_array_0 | DISCOVERED | First of 10 int vectors |
| 0xE0 | DVec<HistoricallyBuiltObjectType> | historically_built_types | CONFIRMED | RTTI-confirmed type |
| 0xF8 | DVec<GameObjectTypeClass const*> | buildable_types_1 | DISCOVERED | Destructor cleanup |
| 0x108 | uint8 | is_human_controlled | CONFIRMED | Lua getter at 0x603A40 |
| 0x110 | DVec<GameObjectTypeClass const*> | buildable_types_2 | DISCOVERED | |
| 0x128 | DVec<GameObjectTypeClass const*> | buildable_types_3 | DISCOVERED | |
| 0x1A8 | DVec header | unlockable_types_header | DISCOVERED | Resize in add_to_unlocked |
| 0x1B0 | pointer | unlocked_types_array | CONFIRMED | Unlock_Tech/Lock_Tech/IsUnlocked all read |
| 0x1B8 | int32 | unlocked_types_count | CONFIRMED | IsUnlocked loop bound |
| 0x1BC | int32 | unlocked_types_capacity | CONFIRMED | Growth check |
| 0x1C0 | DVec<GameObjectTypeClass const*> | locked_types_dvec | CONFIRMED | Lock_Tech RVA 0x286150 |
| 0x1C8 | pointer | locked_types_array | CONFIRMED | IsLocked reads here |
| 0x1D0 | int32 | locked_types_count | CONFIRMED | IsLocked loop bound |
| 0x1D4 | int32 | locked_types_capacity | CONFIRMED | Lock_Tech growth |
| 0x360 | pointer | ai_player_ptr (AIPlayerClass*) | CONFIRMED | Non-null for AI players, null for humans |
| 0x370 | pointer | diplomacy_table (int32[]) | CONFIRMED | 0=ally, 1=enemy per player_id |
| 0x380 | DVec<BlackMarketItemClass const*> | black_market_items | CONFIRMED | Destructor RTTI |
| 0x398 | int32 | difficulty_level | DISCOVERED | Lua getter at 0x602AE0 |
| 0x3F8 | uint8[16] | advisor_hints_base | CONFIRMED | Indexed by game mode (0=galactic,1=land,2=space) |
| 0x448 | uint8 | black_market_tutorial_flag | CONFIRMED | Lua setter at 0x604300 |
| 0x449 | uint8 | sabotage_tutorial_flag | CONFIRMED | Lua setter at 0x6043C0 |
| 0x484 | int32 | space_station_level | DISCOVERED | Lua getter at 0x602690 |

### Lua Wrapper Methods (PlayerWrapper)

| Lua Name | Wrapper RVA | Engine RVA | Fields Accessed |
|----------|-------------|------------|-----------------|
| Give_Money | 0x603130 | 0x27F370 | 0x70, 0x74, 0x360, 0x4C |
| Get_Faction_Name | 0x602A00 | inline | 0x68 |
| Get_Credits | 0x6027F0 | inline | 0x70 |
| Get_ID | 0x602C40 | inline | 0x4C |
| Get_Tech_Level | 0x603040 | inline | 0x84 |
| Set_Tech_Level | 0x604480 | 0x288980 | 0x84, 0x88, 0x68, 0x4C |
| Unlock_Tech | 0x604540 | 0x286100 | 0x1B0, 0x1B8, 0x1C8, 0x1D0 |
| Lock_Tech | 0x603B20 | 0x286150 | 0x1B0, 0x1B8, 0x1C8, 0x1D0, 0x1D4 |
| Is_Enemy | 0x603760 | 0x2824F0 | 0x370, 0x4C |
| Is_Ally | 0x603560 | 0x2823E0 | 0x370, 0x4C |
| Is_Human | 0x603A40 | inline | 0x108 |
| Is_Local_Player | 0x603960 | inline | 0x62 |
| Make_Ally | 0x6046A0 | 0x288800 | 0x370, 0x4C |
| Make_Enemy | 0x604780 | 0x288800 | 0x370, 0x4C |
| Enable_As_Actor | 0x602640 | 0x4B0250 | 0x360 |
| Enable_Advisor_Hints | 0x601E80 | inline | 0x3F8 |
| Select_Object | 0x603F60 | 0x2BD2F0 | 0x4C |
| Retreat | 0x603DE0 | 0x340920 | 0x4C |
| Release_Credits_For_Tactical | 0x603C70 | 0x4B06D0 | 0x360 |

---

## 3. GameObjectClass

**RTTI:** `.?AVGameObjectClass@@`
**Vtable RVA:** 0x8661B8
**Minimum Size:** 0x3C8 bytes
**Constructors:** 0x388720 (default), 0x388B60 (full)
**Inherits:** RootClass, MultiLinkedListMember, PooledObjectClass, CullObjectClass, SignalGeneratorClass

### Component System (QueryInterface)

The component lookup table at +0x332 is a byte array indexed by query type ID. Each byte is an index into the component array at +0x278, or 0xFF for "not present."

| Query Type | Component | Purpose |
|-----------|-----------|---------|
| 0x00 | Self | Identity |
| 0x01 | BehaviorClass | Behavior/AI component |
| 0x03 | Parent/Container | Owner resolution for sub-objects |
| 0x0F | BaseShieldBehaviorClass | Front shield |
| 0x10 | ShieldBehaviorClass | Rear shield |
| 0x16 | HardpointManager | Hardpoint damage routing |
| 0x19 | AbilityComponent | Ability system |
| 0x3D | TransformComponent | Position/rotation |
| 0x46 | PropertyHandler | Property system |

### Core Fields

| Offset | Type | Name | Confidence |
|--------|------|------|------------|
| 0x00 | pointer | vtable_ptr (RVA 0x8661B8) | HIGH |
| 0x08 | pointer | vtable_MultiLinkedListMember | HIGH |
| 0x18 | pointer | vtable_CullObjectClass | HIGH |
| 0x20 | pointer | vtable_SignalGeneratorClass | HIGH |
| 0x38 | pointer | vtable_5 (unknown base) | HIGH |
| 0x40 | uint32 | visibility_mask (init 0x3FFFFF) | HIGH |
| 0x48 | int32 | unique_session_id | HIGH |
| 0x50 | int32 | object_id | HIGH |
| 0x58 | int32 | owner_player_id | HIGH |
| 0x5C | float32 | **hp** (current hitpoints) | HIGH |
| 0x68 | float32 | spawn_position_x | HIGH |
| 0x6C | float32 | spawn_position_y | HIGH |
| 0x70 | float32 | spawn_position_z | HIGH |
| 0x78 | float32 | current_position_x | HIGH |
| 0x7C | float32 | current_position_y | HIGH |
| 0x80 | float32 | current_position_z | HIGH |
| 0x84 | float32 | render_position_x | HIGH |
| 0x88 | float32 | render_position_y | HIGH |
| 0x8C | float32 | render_position_z | HIGH |
| 0xA8 | pointer | locomotor_component_ptr | HIGH |
| 0xB8 | pointer | parent_container_component_ptr | HIGH |
| 0xF0 | pointer | buff_modifier_component_ptr | MEDIUM |
| 0xF8 | pointer | game_object_type_wrapper_ptr | MEDIUM |
| 0x100 | pointer | combatant_behavior_ptr (size 0x3B8) | HIGH |
| 0x118 | pointer | health_sub_object_ptr | HIGH |
| 0x278 | pointer | component_array_ptr | HIGH |
| 0x298 | pointer | game_object_type_ptr (name at type+0xF8) | HIGH |
| 0x2B0 | pointer | container_ref | HIGH |
| 0x332 | uint8[] | component_lookup_table | HIGH |
| 0x335 | uint8 | parent_component_index (0xFF=top-level) | HIGH |
| 0x341 | uint8 | front_shield_present (0xFF=none) | HIGH |
| 0x342 | uint8 | rear_shield_present (0xFF=none) | HIGH |
| 0x348 | uint8 | direct_hp_path_flag (0xFF=direct, other=hardpoints) | HIGH |
| 0x3A0 | uint8 | change_notification_flag (bit0=HP changed, bits1+6=immunity) | HIGH |
| 0x3A1 | uint8 | prevent_death_flags (bit7=prevent death) | HIGH |
| 0x3A7 | uint8 | invulnerability_flag (1=invulnerable) | HIGH |

### GameObjectType Key Offsets

| Offset | Type | Name |
|--------|------|------|
| 0xF8 | SSO string | type_name (e.g., "AT_AT") |
| 0x880 | int32 | build_limit_global |
| 0x888 | int32 | build_limit_per_player |
| 0x890 | int32 | base_build_time |
| 0x894 | int32 | tech_level_requirement |
| 0x89C | int32 | min_tech_level |
| 0x904 | int32 | build_time (skirmish) |
| 0x908 | float32 | build_cost (skirmish) |
| 0xDCC | float32 | base_max_hp |
| 0xDD0 | float32 | base_max_front_shield |
| 0xDD4 | float32 | base_max_rear_shield |
| 0xEB0 | pointer | damage_threshold_array |
| 0xF0C | int32 | build_pad_requirement |
| 0x1648 | bitmask | weapon_target_type_bitmask |
| 0x1F78 | int32 | unit_cap_contribution |
| 0x1FF4 | int32 | damage_type_flags |
| 0x23E8 | int32 | death_debris_type |

---

## 4. Combat System

### Damage Pipeline (5 Stages)

```
Stage 1: Weapon Fire / Attack Command
  CombatantBehaviorClass -> FireControl_Dispatch (0x38D730)
  Checks: enemy status, invulnerability pre-check, LOS, game mode
      |
      v
Stage 2: Invulnerability Gate -- Take_Damage_Outer (0x38A350)
  8 invulnerability checks:
    - Primary: obj+0x3A7 == 1 -> DISCARD
    - Status:  obj+0x3A0 & 0x42 -> DISCARD
    - Container flags: obj+0x381, 0x382, 0x388
    - Locomotor: obj+0xA8 -> locomotor+0x2A8 == 1 -> DISCARD
      |
      v
Stage 3: Damage Routing -- Take_Damage_PropertyDispatch (0x3A97E0)
  If obj+0x348 != 0xFF -> route to HardpointManager (QI 0x16)
  If has shields -> absorb via front (QI 0x0F) / rear (QI 0x10)
      |
      v
Stage 4: HP Subtraction -- Take_Damage_Impl (0x3AB890)
  new_hp = old_hp - damage
  If prevent_death (obj+0x3A1 & 0x80) and new_hp <= 0:
      SetHP(obj, max(1.0, old_hp))
  If hp <= 0: trigger death sequence
      |
      v
Stage 5: SetHP -- THE canonical HP write (0x3A89D0)
  Clamps to [0.0, max_hp]
  Sets dirty flag if tracked object
  Logs death events if < 0
```

### SetHP -- All 18 Known Callers

| RVA | Category | Description |
|-----|----------|-------------|
| 0x29F5B5 | death_cleanup | Death and cleanup handlers |
| 0x38738B | shield_health_regen | Shield regen tick: HP * ratio |
| 0x3A0A90 | property_init | Initial HP from XML data system |
| 0x3AB0B0 | take_damage_dispatch | Property dispatch with hardpoint post-check |
| 0x3AB8C6 | take_damage_impl | Core damage: old_hp - damage |
| 0x3AB8EC | prevent_death | Prevent-death clamp: max(1.0, old_hp) |
| 0x42DD63 | lua_set_hull | Lua Set_Hull direct write |
| 0x4B0179 | spawn_hp_scaling | Spawn proportional rescale |
| 0x4B0E99 | spawn_variant | Spawn rescale variant |
| 0x4CBA8B | behavior_attach | HP set during behavior attach |
| 0x5D7129 | health_regen | Natural regen: set to 1.0 (revival) |
| 0x5D73FC | health_regen_tick | Regen tick: min(current+rate, max) |
| 0x6EE8EE | absorb_blaster | AbsorbBlaster ability heal |
| 0x6F4444 | combat_bonus | CombatBonus HP scaling after max_hp change |
| 0x6F4DA2 | combat_bonus_deact | CombatBonus deactivation HP restore |
| 0x6F8791 | periodic_dot | DoT tick: current_hp + damage_amount |
| 0x6FBAD1 | combat_event | Combat event handler |
| 0x71B7BA | periodic_heal | DrainLife heal: current_hp + heal_amount |

### Max Health Formula

```
max_hp = type->base_max_hp(+0xDCC)
       * difficulty_multiplier
       * player_multiplier (PlayerObj+0x360->sub+0x50)
       * (1.0 + ability_bonus)

GetMaxHealth RVA: 0x3727A0
Difficulty globals: Easy=0xB16DCC, Hard=0xB16DC8
```

### Shield System

| Shield | QI ID | Set RVA | Read RVA | Storage | Max From |
|--------|-------|---------|----------|---------|----------|
| Front | 0x0F | 0x3A8630 | 0x3963C0 | [obj+0xF0]+0xF8 | Type+0xDD0 |
| Rear | 0x10 | 0x3A91E0 | 0x396420 | [obj+0xF0]+0xFC | Type+0xDD4 |

Shields must deplete before hull takes damage. When shield reaches 0 and FUN_14039B950 returns true, Take_Damage_Outer is called for hull damage.

### Damage Types

| ID | Name | Notes |
|----|------|-------|
| 0x00 | Default/Normal | Standard path |
| 0x06 | Special type 2 | BerserkerAbilityClass check |
| 0x08 | Fire/Burn | Death anim variant 0x0C |
| 0x68 | Berserker/Eat forced | Override when Berserker active |
| 0x74 | Construction | Tactical building |

### Special Attack Abilities (16 Key Abilities)

| Ability | Constructor RVA | Effect |
|---------|----------------|--------|
| ConcentrateFireAttack | 0x6F52C0 | Focus-fire damage bonus |
| MaximumFirepowerAttack | 0x706B10 | Rate of fire + damage boost |
| LuckyShotAttack | 0x706280 | Random critical hit |
| CombatBonusAbility | 0x6F42D0 | 8-stat modifier (HP, damage, shields, rate, range, accuracy) |
| AbsorbBlasterAbility | 0x6EE840 | Converts blaster damage to healing |
| BerserkerAbility | 0x712D20 | Forces damage type 0x68 |
| DrainLifeAbility | 0x71B560 | Drains HP, heals caster |
| EatAttackAbility | 0x6F7A30 | Consume/destroy target |
| CableAttackAbility | 0x6F22F0 | Tow cable (AT-AT takedown) |
| TractorBeamAttack | 0x710080 | Immobilize + damage |

---

## 5. AI System

### Architecture

The AI uses a hierarchical, goal-driven architecture centered on **TheAIClass** singleton. Each AI player gets an **AIPlayerClass** instance containing 7 **ServicedAISystem** subsystems. Planning recalculates every 4.0 seconds.

### Singletons

| Singleton | Constructor RVA | Purpose |
|-----------|----------------|---------|
| TheAIClass | 0x4D9C80 | Top-level AI manager, state_flags=0x01010101 |
| TheAIDataManagerClass | 0x4747E0 | Global AI configuration |
| TheAIPlayerTypeManagerClass | 0x4E9940 | AI player type definitions (hash map) |
| TheAIGoalTypeManagerClass | 0x5E6690 | AI goal type database |
| TheAITemplateManagerClass | 0x4E8920 | Force templates (build orders) |

### AIPlayerClass (Per-Player AI Controller)

**Constructor RVA:** 0x4AF810
**Inherits:** AIDiagnosticsClass

Contains references to 7 subsystems and a mode enum (init -1).

### 7 AI Subsystems

| Subsystem | Base Class | Constructor RVA | Purpose |
|-----------|-----------|----------------|---------|
| AIPerceptionSystem | ServicedAISystemClass | 0x4DAD80 | Senses game world; feeds evaluators to planning |
| AIPlanningSystem | ServicedAISystemClass | 0x6BAC00 | Generates plans (4.0s interval, hash map buckets=7) |
| AIGoalSystem | ServicedAISystemClass | 0x6C7970 | Manages active goals + AIBudgetClass |
| AIExecutionSystem | ServicedAISystemClass | 0x524CE0 | Executes planned actions |
| AILearningSystem | ServicedAISystemClass | 0x585D00 | Tracks performance history (5 hash maps) |
| AITemplateSystem | ServicedAISystemClass | 0x6BB9E0 | Force templates (predefined compositions) |
| ServicedAISystemClass | AIDiagnosticsClass | 0x64C250 | Base class with tick/service interface |

### Perception Specializations

| Mode | Constructor RVA |
|------|----------------|
| GalacticPerceptionSystem | 0x4E1880 |
| LandPerceptionSystem | 0x6B8980 |
| SpacePerceptionSystem | 0x6B9A20 |
| TacticalPerceptionGrid | 0x653340 |

### AI Build Task State Machine

**AIBuildTaskClass** uses FiniteStateMachineClass with 25 named states from INIT_TASK to ALL_FINISHED. Multiple tasks can run simultaneously per AI player via MultiLinkedListClass.

**Constructor RVA:** 0x6478C0
**State Machine Handler:** 0x64AEC0

### AI Enable/Disable

AI is controlled through the TheAIClass singleton:
- `state_flags` at +0x00: 4 byte flags (per-mode enable: galactic, space, land, global)
- `active_flag` at +0x04: global AI-active flag
- Per-player: AIPlayerClass at PlayerClass+0x360 (null = human, non-null = AI)
- Lua binding: `Enable_As_Actor` (wrapper 0x602640, engine 0x4B0250)

---

## 6. Galactic Map System

### Global Data

| RVA | Name | Type | Description |
|-----|------|------|-------------|
| 0xA16FD0 | PlayerListClass* | pointer | Global player list singleton |
| 0xA16FD8 | PlayerListClass_end | pointer | End of player array (count = (end-start)>>3) |
| 0xA16FB0 | PlayerCount_static | uint32 | Static player count |
| 0xB15418 | GameModeManager_active_mode | pointer | Active GameModeClass instance |
| 0xA172D0 | GameObjectTypeList | pointer | Type registry (+0x10=array, +0x18=count) |
| 0xB153E0 | PerceptionManager | pointer | Diplomacy/perception manager |
| 0xB0A340 | GameTickCounter | float/int | Time multiplier for capture timers |
| 0xA284C4 | FOW_GlobalToggle | bool | Global fog of war disable |

### PlanetaryDataPackClass

**RTTI:** `.?AVPlanetaryDataPackClass@@`
**Minimum Size:** 0x350 bytes
**Accessed via:** PlanetaryBehaviorClass+0x18 -> parent[0x17] (byte offset 0xB8)

| Offset | Type | Name |
|--------|------|------|
| 0x10-0x20 | DVec<PersistentTacticalBuiltObjectStruct> | persistent_tactical_objects |
| 0x20-0x30 | DVec<PersistentUpgradeObjectStruct> | persistent_upgrades |
| 0x30-0x50 | DVec<LineLinkStruct> | line_links |
| 0x50-0x60 | DVec<TradeRouteLinkEntryClass> | trade_route_links |
| 0x68 | float32 | capture_progress |
| 0x6C | int32 | **owning_player_id** (THE ownership field) |
| 0x70 | float32 | previous_capture_progress |
| 0x74 | int32 | previous_owning_player_id |
| 0x98 | int32 | capture_initiator_player_id |
| 0x1C9 | uint8 | initial_owner_set_flag |
| 0x2C8 | uint8 | planet_destroyed_flag |
| 0x2E3 | uint8 | capture_timer_active |
| 0x2F4 | int32 | corruption_level (-1=none, 0-3=tiers) |

### Planet Ownership Transfer

Two key functions in **PlanetFactionChangeClass**:
- **Initial set** (0x3FA160): Writes 0x68, 0x6C, 0x70, 0x98; notifies all players
- **Full transfer** (0x3FB040): Full UI notification, diplomacy checks, corruption processing, AI budget updates; fires TEXT_PLANET_CHANGED_HANDS localized string

### Fog of War

Per-player FOW state objects (0x58 bytes each) stored in GameModeClass+0x198 array:

| Offset | Type | Description |
|--------|------|-------------|
| 0x00 | pointer | visibility_grid (byte per cell: 0x00=fogged, 0xFF=visible) |
| 0x08 | pointer | reveal_timer_grid (short per cell, countdown) |
| 0x20 | int64 | grid_cell_count |
| 0x50 | uint8 | dirty_flag (signals renderer to re-upload) |

### Planet Reachability

**PlanetReachabilityClass** (ctor 0x663530): 11 reachability slots per planet, uses perception system for pathfinding costs. Inherits RefCountClass.

---

## 7. Production System

### Pipeline Stages

1. **VALIDATION:** CanProduce (0x2804D0) -- 18 prerequisite gates
2. **COST CHECK:** GetAdjustedCost (0x400240 / 0x3711C0)
3. **UNIT CAP CHECK:** CheckPopCap (0x2AC320)
4. **QUEUE ENTRY:** ObjectUnderConstructionClass allocation (0x38 bytes)
5. **COST DEDUCTION:** AddCredits (0x27F370) with negative cost
6. **BUILD TIME:** ComputeBuildTime (0x400370) -- 5-factor formula
7. **COUNTDOWN:** ScheduledEventClass timer per frame
8. **COMPLETION:** Spawns unit, transfers ownership

### Build Time Formula

```
build_time = (base_time * price_ability_mod * tech_level_factor
             * faction_income_mod * planet_modifier)
             / concurrent_build_count

base_time source:     GameObjectType+0x890
global speed scalar:  DAT_140B15920
```

### CanProduce Prerequisite Checks (18 Gates)

1. type_to_build != NULL
2. production_facility != NULL
3. Facility has production component (facility[0x67] != 0xFF)
4. Type not disabled/locked (type+0x21 != 1)
5. Faction affiliation match
6. Adjusted cost >= 1 credit
7. Build time >= 0.0
8. No 'unbuildable' property (0x5B)
9. Valid build slot/location
10. Credit sufficiency (human players only)
11. Tech level meets requirement (player+0x84 >= type+0x89C)
12. Build pad availability (type+0xF0C)
13. Secondary slot check (type+0xF10)
14. Unit cap per category (space 0x5F / land 0x60)
15. Build prerequisites (type+0xF50)
16. Per-player build limit (type+0x888)
17. Global build limit (type+0x880)
18. Duplicate unique building check

### Production Classes

| Class | RVA | Size | Purpose |
|-------|-----|------|---------|
| ProductionEventClass | 0x523F50 | event | Network event (ID=7) |
| TacticalBuildEventClass | 0x5D7740 | event | Tactical build (ID=33) |
| ObjectUnderConstructionClass | 0x3FFF70 | 0x38 | Production queue entry |
| ProductionBehaviorClass | dtor 0x3FFF10 | 0x40 | Behavior on producing objects |
| ProductionDataPackClass | 0x559680 | - | 2 queues (units + structures) |
| ReduceProductionPriceAbility | 0x70A280 | - | Cost reduction ability |
| ReduceProductionTimeAbility | 0x70B040 | - | Time reduction ability |

---

## 8. Story System

### Architecture

3-layer hierarchy: **StorySubPlotClass** owns **StoryEventClass** instances in a CRC32-keyed hash table. Events dispatched via type-ID factory pattern. 61 event types, 28 unique concrete classes.

### Story Flag Storage

Flags are NOT simple booleans. Each StorySubPlotClass contains a hash table at +0x20:
- Key: CRC32(strupr(event_name)) XOR 0xDEADBEEF
- Bucket: LCG(key) & bucket_mask (LCG: a=16807, m=2^31-1)
- Active flag: event+0x4C (byte, 0=inactive)

| Function | RVA | Purpose |
|----------|-----|---------|
| CRC32_Hash | 0x215A30 | Standard CRC32 with table at 0xA14D20 |
| StorySubPlot_FindEventByName | 0x52EF90 | Name-to-event lookup |
| StoryEvent_Factory_Create | 0x453310 | Type ID to concrete class |
| ResolveRewardTypeFromString | 0x45C3F0 | Reward type string to enum |

### All 61 Event Types

| ID | Class | Group |
|----|-------|-------|
| 0x01-0x02 | StoryEventEnterClass | enter/trigger |
| 0x03-0x04 | StoryEventSingleObjectNameClass | object-name |
| 0x05 | StoryEventConstructLevelClass | construct |
| 0x06, 0x08 | StoryEventDestroyClass | destroy |
| 0x07 | StoryEventDestroyBaseClass | destroy-base |
| 0x09-0x0A | StoryEventBeginEraClass | era |
| 0x0C-0x0D | StoryEventHeroMoveClass | hero-move |
| 0x0E | StoryEventAccumulateClass | accumulate |
| 0x0F | StoryEventConquerCountClass | conquer-count |
| 0x10 | StoryEventElapsedClass | elapsed-time |
| 0x11-0x12 | StoryEventSingleObjectNameClass | object-name |
| 0x13-0x14 | StoryEventWinBattlesClass | win-battles |
| 0x15 | StoryEventRetreatClass | retreat |
| 0x16 | StoryEventSingleObjectNameClass | object-name |
| 0x17 | StoryEventEnterClass | enter/trigger |
| 0x18-0x19 | StoryEventStartTacticalClass | tactical-start |
| 0x1A-0x1C | StoryEventSelectPlanetClass | select-planet |
| 0x1D-0x1E | StoryEventStringClass | string-event |
| 0x1F-0x20 | StoryEventFogRevealClass | fog-reveal |
| 0x21 | StoryEventStringClass | string-event |
| 0x22 | StoryEventClass (base) | generic |
| 0x23 | StoryEventAINotificationClass | ai-notification |
| 0x24 | StoryEventSingleObjectNameClass | object-name |
| 0x25 | StoryEventCommandUnitClass | command-unit |
| 0x26-0x28 | StoryEventSingleObjectNameClass | object-name |
| 0x29 | StoryEventGuardUnitClass | guard-unit |
| 0x2A | StoryEventProximityClass | proximity |
| 0x2B | StoryEventDifficultyClass | difficulty |
| 0x2C | StoryEventFlagClass | flag-check (Set/Check_Story_Flag) |
| 0x2D | StoryEventLoadTacticalClass | load-tactical |
| 0x2E | StoryCheckDestroyedClass | check-destroyed |
| 0x2F | StoryEventVictoryClass | victory |
| 0x30 | StoryEventMovieDoneClass | movie-done |
| 0x31, 0x33 | StoryEventEnterClass | enter/trigger |
| 0x32 | StoryEventStringClass | string-event |
| 0x34 | StoryEventObjectiveTimeoutClass | objective-timeout |
| 0x35 | StoryEventCaptureClass | capture |
| 0x36, 0x3A-0x3B | StoryEventCorruptionLevelClass | corruption-level |
| 0x37 | StoryEventSingleObjectNameClass | object-name |
| 0x38-0x39, 0x3C | StoryEventStringClass | string-event |

### Reward Dispatch

Rewards use a string-to-int CRC32 tree at 0xB30728. Reward type stored at event+0x3C, parameters at +0x50 through +0x190 (up to 14 SSO strings at 0x20 stride). Reward types are NOT hardcoded -- parsed from XML at load time.

### Lua Wrapper Methods

**StoryEventWrapper** (7 methods): Add_Dialog_Text, Clear_Dialog_Text, Set_Dialog, Set_Reward_Parameter, Set_Reward_Type
**StoryPlotWrapper** (5 methods): Get_Event, Reset

---

## 9. Camera and Selection

### Global Camera Parameters (RVA Base: 0xB1599C)

| RVA | Name | Type |
|-----|------|------|
| 0xB1599C | galactic_camera_zoom_angle | float32 |
| 0xB159A0 | galactic_camera_target_zoom_angle | float32 |
| 0xB159A4 | galactic_camera_distance | float32 |
| 0xB159A8 | galactic_camera_distance_scale | float32 |
| 0xB159AC | galactic_camera_look_dir_x | float32 |
| 0xB159B0 | galactic_camera_look_dir_y | float32 |
| 0xB159B4 | galactic_camera_look_dir_z | float32 |
| 0xB159C8 | galactic_camera_mode_value | float32 |
| 0xA12550 | screen_aspect_ratio | float32 |

### CameraClass

**Constructor RVA:** 0x261470
**Sub-object size:** 0x308 bytes (live camera state)

Orientation matrix: 3x4 column-major at sub+0x00
- Position: offsets 0x0C (X), 0x1C (Y), 0x2C (Z)
- Forward: offsets 0x08 (X), 0x18 (Y) -- negated
- Default position: (0, 0, -60)
- Default FOV: pi/4 (45 degrees), Aspect: 4:3

| Camera Method | RVA |
|---------------|-----|
| GetTransformMatrix | 0x2619F0 |
| SetTransformMatrix | 0x261BD0 |
| GetPosition | 0x261A40 |
| GetForwardDirection | 0x261690 |
| TranslateLocal | 0x261C90 |
| GetFovAspect | 0x2618E0 |
| SetFovAspect | 0x261A80 |
| SetPerspectiveProjection | 0x261AB0 |
| SetOrthoProjection | 0x261B50 |
| SetViewport | 0x261E00 |

### GalacticCameraClass

**Constructor:** 0x3C2B20 | **Update:** 0x3C2C00

Orbital camera around look-at point with zoom, pitch, and rotation. Camera position = look_at + rotation_matrix * (look_direction * distance).

### Selection System

**GameModeManagerClass** singleton at 0xB153E0:
- +0x48: game_mode_array (pointer to GameModeClass* array)
- +0x50: game_mode_count
- +0xB4: selection_state (written by SelectEvent/SelectAllEvent)
- GetModeByType RVA: 0x28A950

**Selection Events:**
- SelectEventClass (ID=5, ctor 0x3AC9D0)
- SelectAllEventClass (ID=37, ctor 0x437F40)
- ControlGroupEventClass (ID=30, ctor 0x436060)

---

## 10. Network and Multiplayer

### Architecture: Peer-to-Peer Deterministic Lockstep

All peers execute the same simulation independently. Only player commands (events) are synchronized. No authoritative server.

### Transport Layer

| Component | RVA | Description |
|-----------|-----|-------------|
| SteamAsyncSocketImpl | 0x6B370 | Steam P2P networking |
| WinsockAsyncSocketImpl | 0x227110 | LAN/direct-IP |
| PacketHandlerClass | dtor 0x2054C0 | Thread-based packet processing |
| PacketClass | ctor 0x23BC40 | Bit-packed serialization via BitStreamClass |

### Synchronization Protocol

1. Exchange **FrameInfoEventClass** (ID=0) with frame metadata
2. Wait for **FrameSyncEventClass** (ID=17) from all peers -- lockstep barrier
3. Execute all ScheduledEvents whose scheduled_frame matches current frame
4. Exchange **PerformanceMetricsEventClass** (ID=18) for timing/checksum desync detection

### Complete Event Catalog (58 Events)

**Control:** FrameInfo(0), FrameSync(17), PerformanceMetrics(18), QuitGame(15), Chat(16), GameOptions(39), ResumeGame(51), SaveGame(36), Debug(11), Taunt(50)

**Movement:** MoveToPosition(1), MoveToObject(2), MoveObjectToObject(3), Look(4), MoveThroughObjects(10), MoveToRay(14), MoveToRayFacing(19), Facing(20), StopMovement(29), MoveToPositionFacing(47), MoveToGarrison(55)

**Combat:** Attack(6), SpecialAbility(13), SpecialWeaponFire(25), BombingRun(28), TacticalSpecialAbility(40), PlanetaryBombard(54), TacticalSpecialAbilityWithDummyTarget(57), TacticalSuperWeapon(38)

**Economy:** Production(7), FleetManagement(8), Invade(9), Reinforce(12), TacticalBuild(33), TacticalSell(34), DistributeMoney(42), GalacticSell(46), RepairHardpoint(45)

**Selection:** Select(5), ControlGroup(30), SelectAll(37)

**Galactic:** Escort(21), Retreat(22), CinematicAnimation(26), Ally(35), Withdrawal(49), Garrison(52), PlaceBeacon(48), SetMarkerID(53), SetGUIIndex(56), SetAbilityAutofire(58), SetUnitAbilityMode(43)

**Setup:** SetupPhaseMove(31), SetupPhaseTriggerEnd(32)

### Multiplayer Safety Guide

| Safe to Modify (Visual Only) | Unsafe (Will Desync) |
|-------------------------------|---------------------|
| Camera position/zoom | GameObjectClass.hp (+0x5C) |
| UI state, selection highlights | GameObjectClass.owner (+0x58) |
| Sound/music settings | Any production queue change |
| Visual effects | Any unit spawn/destroy |
| ChatEventClass content | Any credit/resource change |
| | Any RNG state modification |

---

## 11. Save File Format

### File Structure

- **Extension:** .sav
- **Location:** %APPDATA%\Petroglyph\Empire at War - Forces of Corruption\Save\
- **Encoding:** Binary, little-endian
- **Format:** Hierarchical chunk tree (Westwood/Petroglyph W3D-derived)

### Chunk Header (8 bytes)

| Offset | Type | Name |
|--------|------|------|
| 0 | uint32 | chunk_id |
| 4 | uint32 | chunk_size (bit 31 set = contains sub-chunks) |

### Micro-Chunk Header (2 bytes)

| Offset | Type | Name |
|--------|------|------|
| 0 | uint8 | micro_chunk_id |
| 1 | uint8 | micro_chunk_size (max 255) |

### Chunk I/O Classes

| Class | Constructor | Destructor |
|-------|------------|------------|
| ChunkWriterClass | 0x21FCC0 | 0x21FDC0 |
| ChunkReaderClass | 0x220280 | 0x220370 |
| FileClass | 0x213010 | 0x2132F0 |
| RAMFileClass | 0x2227E0 | 0x2228D0 |

### Compression

zlib is statically linked but usage for saves is unconfirmed:
- compress2: 0x7A1470
- uncompress2: 0x7A1590

### Safely Editable Fields

**High confidence:** credits (int32), planet_owner (int32), tech_level (int32)
**Medium confidence:** unit_hp (float32), corruption_level, ability cooldowns, superweapon cooldowns
**Dangerous:** save_name (wstring length change), object_id mappings, adding/removing units

---

## 12. Lua Integration

### Lua 5.0.2 C API (48 Confirmed Functions)

| RVA | Function | Signature |
|-----|----------|-----------|
| 0x7B8930 | lua_open | lua_State* lua_open() |
| 0x7B8BC0 | lua_checkstack | int lua_checkstack(L, extra) |
| 0x7B8C40 | lua_concat | void lua_concat(L, n) |
| 0x7B8CD0 | lua_cpcall | int lua_cpcall(L, func, ud) |
| 0x7B8D80 | lua_error | int lua_error(L) |
| 0x7B8D90 | lua_getfenv | void lua_getfenv(L, idx) |
| 0x7B8E10 | lua_getmetatable | int lua_getmetatable(L, idx) |
| 0x7B8E90 | lua_gettable | void lua_gettable(L, idx) |
| 0x7B8EF0 | lua_gettop | int lua_gettop(L) |
| 0x7B8F00 | lua_insert | void lua_insert(L, idx) |
| 0x7B8F60 | lua_iscfunction | int lua_iscfunction(L, idx) |
| 0x7B8FB0 | lua_isnumber | int lua_isnumber(L, idx) |
| 0x7B9010 | lua_isstring | int lua_isstring(L, idx) |
| 0x7B9060 | lua_equal | int lua_equal(L, idx1, idx2) |
| 0x7B90F0 | lua_load | int lua_load(L, reader, data, chunkname) |
| 0x7B9140 | lua_newtable | void lua_newtable(L) |
| 0x7B9190 | lua_newthread | lua_State* lua_newthread(L) |
| 0x7B91D0 | lua_newuserdata | void* lua_newuserdata(L, size) |
| 0x7B9220 | lua_next | int lua_next(L, idx) |
| 0x7B9280 | lua_pcall | int lua_pcall(L, nargs, nresults, errfunc) |
| 0x7B9320 | lua_pushboolean | void lua_pushboolean(L, b) |
| 0x7B9340 | lua_pushcclosure | void lua_pushcclosure(L, fn, n) |
| 0x7B9480 | lua_pushlightuserdata | void lua_pushlightuserdata(L, p) |
| 0x7B94A0 | lua_pushlstring | void lua_pushlstring(L, s, len) |
| 0x7B9510 | lua_pushnil | void lua_pushnil(L) |
| 0x7B9520 | lua_pushnumber | void lua_pushnumber(L, n) |
| 0x7B9540 | lua_pushstring | const char* lua_pushstring(L, s) |
| 0x7B9600 | lua_pushvalue | void lua_pushvalue(L, idx) |
| 0x7B9640 | lua_pushfstring | const char* lua_pushfstring(L, fmt, ...) |
| 0x7B9690 | lua_lessthan | int lua_lessthan(L, idx1, idx2) |
| 0x7B9820 | lua_rawseti | void lua_rawseti(L, idx, n) |
| 0x7B99D0 | lua_settop | void lua_settop(L, idx) |
| 0x7B9A60 | lua_settable | void lua_settable(L, idx) |
| 0x7B9BC0 | lua_tonumber | double lua_tonumber(L, idx) |
| 0x7B9E00 | lua_type | int lua_type(L, idx) |

### lua_State Struct (Partial)

| Offset | Type | Name |
|--------|------|------|
| +0x10 | pointer | top (stack top) |
| +0x18 | pointer | base (stack base) |

### Registered Lua Globals (Story/Script System)

StringCompare, _ScriptExit, _ScriptMessage, _DebugBreak, _CustomScriptMessage, ThreadValue, GlobalValue, _MessagePopup, _OuputDebug (engine typo), GetThreadID, DumpCallStack, Create_Thread, Thread, GetEvent (61 story event types)

---

## 13. Complete Function RVA Table

### Player System

| RVA | Function |
|-----|----------|
| 0x27ED40 | PlayerClass::~PlayerClass |
| 0x27F370 | AddCredits |
| 0x27F7C0 | Lock_Tech_Add |
| 0x27F860 | Unlock_Tech |
| 0x282190 | Auto_Upgrade_Tech |
| 0x2823E0 | Is_Ally |
| 0x2824F0 | Is_Enemy |
| 0x282550 | IsLocked |
| 0x282580 | IsUnlocked |
| 0x286100 | Unlock_Tech_Remove |
| 0x286150 | Lock_Tech |
| 0x288800 | Make_Ally / Make_Enemy |
| 0x288980 | SetTechLevel |
| 0x294BC0 | PlayerList_FindByID |

### Game Object System

| RVA | Function |
|-----|----------|
| 0x388720 | GameObjectClass::ctor (default) |
| 0x388B60 | GameObjectClass::ctor (full) |
| 0x395AC0 | QueryInterface |
| 0x3956C0 | ResolveParentOwner |
| 0x3989A0 | Spawn/Init |
| 0x574D0E | Change_Owner |
| 0x5792E0 | Get_Owner_Lua |

### Combat System

| RVA | Function |
|-----|----------|
| 0x3727A0 | GetMaxHealth |
| 0x372320 | GetMaxFrontShield |
| 0x3725F0 | GetMaxRearShield |
| 0x387010 | WeaponTick |
| 0x387F50 | HardpointFire |
| 0x38A350 | Take_Damage_Outer |
| 0x38D730 | FireControl_Dispatch |
| 0x38F8B0 | ClearSpeedOverride |
| 0x3A8630 | SetFrontShield |
| 0x3A89D0 | **SetHP** |
| 0x3A8C90 | SetSpeedOverride |
| 0x3A91E0 | SetRearShield |
| 0x3A92F0 | AnimationDispatch |
| 0x3A97E0 | Take_Damage_PropertyDispatch |
| 0x3AB890 | Take_Damage_Impl |
| 0x3ABB80 | Make_Invulnerable_Setter |
| 0x3AC290 | DamageVisualLevel |
| 0x39BDB0 | Death_Handler |
| 0x405230 | HullRatio_ViaHardpoints |
| 0x4052D0 | GetHardpoint |
| 0x405300 | HardpointCount |
| 0x48EB10 | ScheduleHeroRespawn |

### AI System

| RVA | Function |
|-----|----------|
| 0x4AF810 | AIPlayerClass::ctor |
| 0x4B0250 | Enable_As_Actor |
| 0x4D9C80 | TheAIClass::ctor |
| 0x4DAD80 | AIPerceptionSystemClass::ctor |
| 0x4E1880 | GalacticPerceptionSystemClass::ctor |
| 0x4E8920 | TheAITemplateManagerClass::ctor |
| 0x4E9940 | TheAIPlayerTypeManagerClass::ctor |
| 0x524CE0 | AIExecutionSystemClass::ctor |
| 0x585D00 | AILearningSystemClass::ctor |
| 0x5E5C30 | TheAIGoalProposalFunctionSetManagerClass::ctor |
| 0x5E6690 | TheAIGoalTypeManagerClass::ctor |
| 0x6109C0 | AIBudgetClass::ctor |
| 0x6383E0 | CombatantBehaviorClass::ctor |
| 0x6478C0 | AIBuildTaskClass::ctor |
| 0x64AEC0 | AIBuildTaskClass::StateMachineHandler |
| 0x64C250 | ServicedAISystemClass::ctor |
| 0x6BAC00 | AIPlanningSystemClass::ctor |
| 0x6BB9E0 | AITemplateSystemClass::ctor |
| 0x6C7970 | AIGoalSystemClass::ctor |

### Story System

| RVA | Function |
|-----|----------|
| 0x215A30 | CRC32_Hash |
| 0x2567B0 | LuaScriptClass::RegisterGlobals |
| 0x4501D0 | StoryEventClass::ctor |
| 0x450600 | StoryEventClass::dtor |
| 0x452D70 | StorySubPlot_ComputeDependencies |
| 0x453310 | StoryEvent_Factory_Create |
| 0x45C3F0 | ResolveRewardTypeFromString |
| 0x45C5D0 | StoryEvent_ParseXMLBlock |
| 0x52D400 | StorySubPlotClass::ctor |
| 0x52E220 | StorySubPlotClass::dtor |
| 0x52EF90 | StorySubPlot_FindEventByName |
| 0x52FC10 | StorySubPlot_Reset |
| 0x73DC80 | StoryEventWrapper::LuaConstructor |
| 0x73ECA0 | StoryEventWrapper::Set_Reward_Parameter |
| 0x73EEF0 | StoryEventWrapper::Set_Reward_Type |
| 0x724120 | StoryPlotWrapper::LuaConstructor |
| 0x724480 | StoryPlotWrapper::Get_Event |

### Production System

| RVA | Function |
|-----|----------|
| 0x2804D0 | CanProduce |
| 0x3FFF70 | ObjectUnderConstructionClass::ctor |
| 0x400240 | GetAdjustedCost |
| 0x400370 | ComputeBuildTime |
| 0x42E890 | Production_CompletionHandler |
| 0x523F50 | ProductionEventClass::ctor |
| 0x5242C0 | ObjectUnderConstructionClass::dtor |
| 0x559680 | ProductionDataPackClass::ctor |

### Camera System

| RVA | Function |
|-----|----------|
| 0x261470 | CameraClass::ctor |
| 0x2618E0 | CameraClass::GetFovAspect |
| 0x261A40 | CameraClass::GetPosition |
| 0x261A80 | CameraClass::SetFovAspect |
| 0x261BD0 | CameraClass::SetTransformMatrix |
| 0x261C90 | CameraClass::TranslateLocal |
| 0x261E00 | CameraClass::SetViewport |
| 0x28A950 | GameModeManagerClass::GetModeByType |
| 0x3C2B20 | GalacticCameraClass::ctor |
| 0x3C2C00 | GalacticCameraClass::Update |

### Save System

| RVA | Function |
|-----|----------|
| 0x21FCC0 | ChunkWriterClass::ctor |
| 0x21FE20 | ChunkWriterClass::Open_Chunk |
| 0x21FEB0 | ChunkWriterClass::Close_Chunk |
| 0x21FFA0 | ChunkWriterClass::Open_Micro_Chunk |
| 0x220030 | ChunkWriterClass::Close_Micro_Chunk |
| 0x2200B0 | ChunkWriterClass::Write |
| 0x220140 | ChunkWriterClass::Write_CString |
| 0x220280 | ChunkReaderClass::ctor |
| 0x2204A0 | ChunkReaderClass::Open_Chunk |
| 0x220520 | ChunkReaderClass::Close_Chunk |
| 0x2046F0 | Write_Int |
| 0x2043B0 | Read_Int |
| 0x204FB0 | Write_String |
| 0x204AD0 | Read_String |

### Network System

| RVA | Function |
|-----|----------|
| 0x6B370 | SteamAsyncSocketImpl::ctor |
| 0x6CA10 | SteamPeerLobbyClass::ctor |
| 0x6A000 | SteamClass::ctor |
| 0x227110 | WinsockAsyncSocketImpl::ctor |
| 0x5126C0 | EventClass::ctor |
| 0x512C50 | ScheduledEventClass::ctor |
| 0x4B39E0 | EventQueueClass::ctor |
| 0x5960F0 | BaseEventFactoryClass::ctor |

### Abilities (Selected)

| RVA | Ability |
|-----|---------|
| 0x6EE840 | AbsorbBlasterAbility |
| 0x6F22F0 | CableAttackAbility |
| 0x6F42D0 | CombatBonusAbility |
| 0x6F52C0 | ConcentrateFireAttack |
| 0x6F7170 | EarthquakeAttack |
| 0x6F7A30 | EatAttackAbility |
| 0x6F9980 | EnergyWeaponAttack |
| 0x706280 | LuckyShotAttack |
| 0x706B10 | MaximumFirepowerAttack |
| 0x7048E0 | IonCannonShotAttack |
| 0x70A280 | ReduceProductionPrice |
| 0x70B040 | ReduceProductionTime |
| 0x70EB50 | StarbaseUpgrade |
| 0x710080 | TractorBeamAttack |
| 0x712D20 | BerserkerAbility |
| 0x717530 | LeechShieldsAbility |
| 0x71B560 | DrainLifeAbility |
| 0x71C820 | ShieldFlareAbility |

---

## 14. Global Data Addresses

| RVA | Name | Type |
|-----|------|------|
| 0xA12550 | screen_aspect_ratio | float32 |
| 0xA14D20 | CRC32_lookup_table | uint32[256] |
| 0xA15738 | script_id_counter | int32 |
| 0xA16FB0 | player_count_static | uint32 |
| 0xA16FD0 | PlayerListClass* | pointer |
| 0xA16FF0 | PlayerArray | pointer (direct) |
| 0xA16FF8 | PlayerCount | int32 |
| 0xA172D0 | GameObjectTypeList* | pointer |
| 0xA284C4 | FOW_GlobalToggle | bool |
| 0xA573D0 | FOW_data | pointer |
| 0xA7BC58 | TheGameText | pointer |
| 0xB0A320 | game_speed_numerator | int/float |
| 0xB0A340 | game_speed_denominator | int/float |
| 0xB15418 | GameModeManager_active_mode | pointer |
| 0xB15920 | build_time_global_scalar | float |
| 0xB153E0 | GameModeManagerClass* | pointer |
| 0xB1599C | galactic_camera_zoom_angle | float32 |
| 0xB159A0 | galactic_camera_target_zoom | float32 |
| 0xB159A4 | galactic_camera_distance | float32 |
| 0xB159AC | galactic_camera_look_dir_x | float32 |
| 0xB159B0 | galactic_camera_look_dir_y | float32 |
| 0xB159B4 | galactic_camera_look_dir_z | float32 |
| 0xB16DC8 | hard_hp_multiplier | float |
| 0xB16DCC | easy_hp_multiplier | float |
| 0xB169F0 | Default_Hero_Respawn_Time | float |
| 0xB27F60 | TheCommandBar | pointer |
| 0xB30728 | EventTypeTree (story/reward) | pointer |
| 0xB313D8 | SaveGame_dispatch_fptr | pointer |
| 0xB313E0 | SaveGameEventFactory_singleton | pointer |
| 0xB36BC1 | EventFactory_registry | DVec<BaseEventFactoryClass*> |
| 0xB3B450 | RewardParam_static_buffer | pointer |
| 0x803514 | damage_threshold_1 | float |
| 0x8007C0 | damage_threshold_2 | float |

---

## 15. Modding Interception Points

### Recommended Hooks (Ordered by Specificity)

| Hook | RVA | AOB Signature | Params | Use Case |
|------|-----|---------------|--------|----------|
| SetHP | 0x3A89D0 | `40 53 48 83 EC 60 0F 29 74 24 50 0F 57 C0 F3 0F 10 71 5C` | RCX=obj, XMM1=new_hp | Universal HP intercept |
| Take_Damage_Outer | 0x38A350 | - | RCX=obj, XMM1=damage | Combat-only damage gate |
| Take_Damage_Impl | 0x3AB890 | - | RCX=obj, param2=type, XMM1=amount | Damage just before subtraction |
| GetMaxHealth | 0x3727A0 | - | RCX=type, RDX=obj; ret XMM0 | Max HP override |
| FireControl_Dispatch | 0x38D730 | - | RCX=obj | Targeting behavior mod |
| AddCredits | 0x27F370 | - | RCX=player, XMM1=amount | Economy mod |
| CanProduce | 0x2804D0 | - | RCX=player, RDX=type | Production gate override |

### DLL Bridge Architecture (v3)

The DLL bridge hooks `lua_open` to identify game states at creation time, caching their pointers. The `luaD_call` hook does a simple pointer lookup instead of stack probing. `lua_close` hook removes stale states. Commands are dispatched via named pipe (`\\.\pipe\swfoc_bridge`).

---

*End of Alamo Engine Reference Manual v3.0*
*Generated by Agent 5A -- Master Knowledge Base Consolidation*
*All RVAs relative to module base. Add 0x140000000 for Ghidra absolute addresses.*
