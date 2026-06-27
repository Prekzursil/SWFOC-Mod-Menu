# Production

**Pipeline Stages**: 8

**Validation Gates**: 18

**Can Produce Rva**: 0x2804D0

**Build Time Rva**: 0x400370

**Build Time Formula**: (base_time * ability_mod * tech_factor * faction_mod * planet_mod) / concurrent_count

**Queue Entry Size**: 0x38

**Event Id**: 7


## RE Findings Detail

**Analysis**: SWFOC Alamo Engine - Production/Build System Reverse Engineering

Complete documentation of the production pipeline, build queue, unit cap system, cost deduction, and build time calculation in the Alamo engine (Star Wars: Empire at War - Forces of Corruption). Derived from Ghidra static analysis of StarWarsG.exe (x86_64).

- **Date**: 2026-04-04


### Overview

- **summary**: The Alamo engine production system is event-driven. When a player requests unit production, a ProductionEventClass (event ID 7) is created and queued. The engine validates prerequisites (tech level, cost, unit cap, build slot availability), then creates an ObjectUnderConstructionClass entry in the production queue. Build time is computed as a multi-factor formula. Credits are deducted up-front (negative delta to PlayerObject+0x70). When the countdown expires, the unit is spawned and ownership transferred.
- **pipeline_stages**: 1. VALIDATION: CanProduce check (FUN_1402804d0) - 15+ prerequisite gates, 2. COST CHECK: GetAdjustedCost (FUN_140400240/FUN_1403711c0) - calculates modified cost, 3. UNIT CAP CHECK: CheckPopCap (FUN_1402ac320) - validates population capacity, 4. QUEUE ENTRY CREATION: ObjectUnderConstructionClass allocation (0x38 bytes), 5. COST DEDUCTION: AddCredits (FUN_14027f370) with negative cost value, 6. BUILD TIME CALCULATION: ComputeBuildTime (FUN_140400370) - 5-factor formula, 7. COUNTDOWN: ScheduledEventClass timer decrements per frame, 8. COMPLETION: ObjectUnderConstructionClass destructor spawns the unit


### Classes

- **ProductionEventClass**:
  - **description**: Network event for production commands. Extends ScheduledEventClass. Event type ID = 7. Created when a player clicks to produce a unit/building in the UI or via Lua _ProduceObject.
  - **constructor_rva**: 0x523F50
  - **event_factory_rva**: 0x01A980
  - **event_type_id**: 7
  - **inherits**: ScheduledEventClass, EventClass
  - **fields**: {'offset': '0x08', 'name': 'event_type_id', 'type': 'int32', 'value': 7, 'note': 'Set in constructor: this->EventClass_data.offset_0x8 = 7'}
- **TacticalBuildEventClass**:
  - **description**: Network event for tactical-mode construction (building structures in land/space battles). Event type ID = 0x21 (33). Extends ScheduledEventClass.
  - **constructor_rva**: 0x5D7740
  - **event_factory_rva**: 0x01AF70
  - **event_type_id**: 33
  - **inherits**: ScheduledEventClass
  - **fields**: {'offset': '0x00', 'name': 'object_bitmask', 'type': 'int32', 'default': '0x3FFFFF', 'note': 'Bitmask for object/slot reference'}, {'offset': '0x04', 'name': 'target_ref', 'type': 'int64', 'default': '-1 (0xFFFFFFFFFFFFFFFF)'}
- **GalacticSellEventClass**:
  - **description**: Network event for selling units/buildings on the galactic map. Event type ID = 0x2E (46).
  - **constructor_rva**: 0x532690
  - **event_type_id**: 46
  - **inherits**: ScheduledEventClass
  - **fields**: {'offset': '0x00', 'name': 'object_bitmask', 'type': 'int32', 'default': '0x3FFFFF'}
- **ObjectUnderConstructionClass**:
  - **description**: Represents a single item in the production queue. Allocated when production begins, destroyed when the unit/building finishes construction. Size = 0x38 bytes. Contains the type being built, build countdown, queue position, and the owner reference.
  - **vtable_string**: vftable (set in constructor at 0x3FFF70)
  - **allocation_size**: 0x38
  - **constructor_rva**: 0x3FFF70
  - **destructor_rva**: 0x5242C0
  - **completion_handler_rva**: 0x42E890
  - **serialization_rva**: 0x497900
  - **fields**: {'offset': '0x00', 'name': 'vtable_ptr', 'type': 'pointer', 'width': 8, 'description': 'Virtual function table pointer'}, {'offset': '0x08', 'name': 'producing_object_type_ptr', 'type': 'pointer', 'width': 8, 'description': 'Pointer to the GameObjectType being produced. Used to resolve the type name via +0xF8 for logging. Set from param_2 (the unit type). Serialized as field ID 6 in save/load.'}, {'offset': '0x10', 'name': 'build_countdown', 'type': 'float32', 'width': 4, 'description': "Remaining build time in seconds. Computed by ComputeBuildTime (FUN_140400370) and stored as float. Logged as 'countdown is %.2f seconds'. Decremented by the scheduler each frame. Serialized as field ID 7."}, {'offset': '0x14', 'name': 'prerequisite_list', 'type': 'DynamicVectorClass', 'width': 8, 'description': 'Inline DynamicVector for prerequisite tracking. Initialized via FUN_1404ad3a0. Serialized as field ID 0x15.'}, {'offset': '0x1C', 'name': 'prerequisite_count', 'type': 'int32', 'width': 4, 'description': 'Number of prerequisites. Serialized as field ID 9.'}, {'offset': '0x20', 'name': 'player_slot_id', 'type': 'int32', 'width': 4, 'description': "Player slot that requested this production. Copied from param_4+0x4C (player's slot). Serialized as field ID 0x0B. Also stored separately at offset +0x2C."}, {'offset': '0x24', 'name': 'queue_flags', 'type': 'int32', 'width': 4, 'description': 'Production flags (e.g., queued vs immediate). Serialized as field ID 0x0B.'}, {'offset': '0x28', 'name': 'build_cost', 'type': 'float32', 'width': 4, 'description': 'The computed cost at time of queue insertion. Used for refund on cancellation. Equals the result of GetAdjustedCost.'}, {'offset': '0x2C', 'name': 'producing_object_id', 'type': 'int32', 'width': 4, 'description': 'Object ID of the production facility. Copied from param_3+0x50. Serialized as field ID 0x0C. Default 0x3FFFFF.'}, {'offset': '0x30', 'name': 'completion_callback', 'type': 'pointer', 'width': 8, 'description': 'Callback or reference used at completion. Set to 0 in constructor.'}
  - **log_message**: Object '%s' added to production queue at '%s' by player %d: countdown is %.2f seconds, once underway.
  - **log_format_notes**: arg1 = type_name of unit being built (from producing_object_type_ptr+0xF8), arg2 = type_name of production facility (from param_3->GameObjectType+0xF8), arg3 = player_slot_id, arg4 = build_countdown
- **ProductionBehaviorClass**:
  - **description**: Behavior component attached to game objects that can produce units (space stations, factories, barracks, etc.). Inherits from BehaviorClass. Size = 0x40 bytes (from scalar deleting destructor). Manages the production queue for its parent object.
  - **destructor_rva**: 0x3FFF10
  - **allocation_size**: 0x40
  - **inherits**: BehaviorClass
  - **vtable_count**: 3
  - **note**: Destructor shows 3 vtable assignments before calling BehaviorClass::~BehaviorClass, indicating triple inheritance or 3 interface implementations.
- **ProductionDataPackClass**:
  - **description**: Data pack for production state persistence. Contains the production queues (2 queues: galactic units and galactic structures/upgrades), organized as DynamicVectorClass<ObjectUnderConstructionClass*>. Discovered in the constructor at 0x559680 where it is initialized with ProductionDataPackClass::vftable.
  - **constructor_rva**: 0x559680
  - **inherits**: DynamicVectorClass<ObjectUnderConstructionClass*>
  - **structure**:
    - **queue_count**: 2
    - **queue_types**: Galactic Units (queue 0), Galactic Structures/Upgrades (queue 1)
    - **ai_task_list**: MultiLinkedListClass<AIPlanetBuildTaskClass> per queue (0x58 bytes each)
- **TacticalBuildObjectsBehaviorClass**:
  - **description**: Behavior for tactical-mode building construction. Manages structures built during land/space battles (turrets, repair stations, etc.).
  - **destructor_rva**: 0x42D850
  - **allocation_size**: 0x40
  - **inherits**: BehaviorClass
- **TacticalBuildObjectsDataPackClass**:
  - **description**: Data pack for tactical build state persistence. Tracks objects built during tactical battles.
  - **constructor_rva**: 0x561270
  - **destructor_rva**: 0x5612A0
  - **fields**: {'offset': '0x00', 'name': 'build_count', 'type': 'int32', 'default': 0}, {'offset': '0x08', 'name': 'build_list_ptr', 'type': 'pointer', 'default': 0}, {'offset': '0x10', 'name': 'slot_index', 'type': 'int32', 'default': '-1 (0xFFFFFFFF)'}, {'offset': '0x14', 'name': 'build_state', 'type': 'int32', 'default': 0}, {'offset': '0x1C', 'name': 'build_progress', 'type': 'int32', 'default': 0}
- **TacticalUnderConstructionBehaviorClass**:
  - **description**: Behavior for objects currently under construction in tactical mode. Handles the building animation, partial construction, and completion logic.
  - **destructor_rva**: 0x5D6D60
  - **data_pack_rva**: 0x5617E0
- **AIPlanetBuildTaskClass**:
  - **description**: AI build task for planet-level production. Used by AI players to queue and prioritize production decisions. Size = 0x58 (from MultiLinkedListClass container).
  - **constructor_rva**: 0x6954B0
  - **inherits**: RefCountClass, MultiLinkedListMember
  - **fields**: {'offset': '0x00', 'name': 'ref_count', 'type': 'int32'}, {'offset': '0x08', 'name': 'build_target_ptr', 'type': 'pointer', 'description': 'What to build'}, {'offset': '0x10', 'name': 'priority', 'type': 'int32'}, {'offset': '0x18', 'name': 'planet_ref', 'type': 'pointer', 'description': 'Where to build'}, {'offset': '0x28', 'name': 'magic_seed', 'type': 'int64', 'default': '0x5D5E0B6B', 'description': 'Debug/validation magic number'}, {'offset': '0x30', 'name': 'is_active', 'type': 'uint16', 'default': 1}, {'offset': '0x3C', 'name': 'failure_reason', 'type': 'int32', 'default': '-1 (0xFFFFFFFF)'}
- **ProduceForceBlockStatus**:
  - **description**: Lua blocking status for the Produce_Force AI command. When AI Lua scripts call Produce_Force(), this BlockingStatus object tracks whether the production is complete. Extends LuaMemberFunctionWrapper<BlockingStatus>.
  - **constructor_rva**: 0x6C6500
  - **inherits**: LuaMemberFunctionWrapper<BlockingStatus>
  - **fields**: {'offset': '0x38', 'name': 'target_type_ptr', 'type': 'pointer', 'default': 0}, {'offset': '0x40', 'name': 'produce_state', 'type': 'int32', 'default': 0}, {'offset': '0x48', 'name': 'completion_flag', 'type': 'int32', 'default': 0}
- **ReduceProductionPriceAbilityClass**:
  - **description**: Special ability that reduces production cost. Applied by hero units or planet bonuses. Extends SpecialAbilityClass. Ability type ID = 3.
  - **constructor_rva**: 0x70A280
  - **inherits**: SpecialAbilityClass, IXMLLoadableClass, RefCountClass
  - **ability_type_id**: 3
  - **fields**: {'offset': '0x00', 'name': 'price_reduction_factor', 'type': 'float32/pointer', 'description': 'The fractional reduction applied to production cost. Feeds into the cost modifier chain.'}, {'offset': '0x08', 'name': 'scope_filter', 'type': 'pointer', 'description': 'Filter for which unit types are affected by this reduction.'}
- **ReduceProductionTimeAbilityClass**:
  - **description**: Special ability that reduces production time. Applied by hero units or planet bonuses. Extends SpecialAbilityClass. Ability type ID = 3.
  - **constructor_rva**: 0x70B040
  - **inherits**: SpecialAbilityClass, IXMLLoadableClass, RefCountClass
  - **ability_type_id**: 3
  - **fields**: {'offset': '0x00', 'name': 'time_reduction_factor', 'type': 'float32/pointer', 'description': 'The fractional reduction applied to production time. Feeds into the build time modifier chain.'}, {'offset': '0x08', 'name': 'scope_filter', 'type': 'pointer', 'description': 'Filter for which unit types are affected.'}
- **StarbaseUpgradeAbilityClass**:
  - **description**: Special ability for upgrading space stations. Ability type ID = 0x0C (12).
  - **constructor_rva**: 0x70EB50
  - **inherits**: SpecialAbilityClass
  - **ability_type_id**: 12


### Functions

- **CanProduce**:
  - **rva**: 0x2804D0
  - **signature**: bool CanProduce(PlayerObject* player, GameObjectType* type_to_build, GameObjectClass* production_facility, bool check_credits, ...out_params...)
  - **description**: Master validation gate for production. Returns 1 if the object can be produced, 0 otherwise. Checks 15+ conditions in sequence. This is the most critical function in the production pipeline.
  - **prerequisite_checks**: {'order': 1, 'check': 'type_to_build != NULL', 'failure_code': None, 'description': 'Null type check'}, {'order': 2, 'check': 'production_facility != NULL', 'failure_code': None, 'description': 'Null facility check'}, {'order': 3, 'check': 'production_facility[0x67] != 0xFF', 'failure_code': None, 'description': 'Facility component query type check (has production component)'}, {'order': 4, 'check': 'type_to_build+0x21 != 1', 'failure_code': None, 'description': 'Type is not disabled/locked'}, {'order': 5, 'check': 'FUN_1403751a0(type, player.faction_affiliation)', 'failure_code': None, 'description': "Faction affiliation check - type belongs to player's faction"}, {'order': 6, 'check': 'GetAdjustedCost(type, facility) >= 1', 'failure_code': None, 'description': 'Cost must be positive (at least 1 credit)'}, {'order': 7, 'check': 'ComputeBuildTime(type, facility) >= 0.0', 'failure_code': None, 'description': 'Build time must be non-negative'}, {'order': 8, 'check': '!HasProperty(type, 0x5B, -1)', 'failure_code': None, 'description': "Type does not have 'unbuildable' property flag (0x5B)"}, {'order': 9, 'check': 'IsValidBuildLocation(type) OR HasGroundBuildFlag(type)', 'failure_code': None, 'description': 'Build slot availability check'}, {'order': 10, 'check': 'player.credits >= cost (if check_credits && player.is_human && !free_build_mode)', 'failure_code': None, 'description': 'Credit sufficiency check (only for human players when not in free-build mode). Reads player+0x70 (credits) and player+0x62 (is_human flag)'}, {'order': 11, 'check': 'player.tech_level(+0x84) >= type.required_tech_level(+0x89C) AND player.max_tech(+0x88) >= type.tech_level_req(+0x894)', 'failure_code': 'error_code=1 (insufficient tech)', 'description': 'Tech level prerequisite. Compares PlayerObject+0x84 against GameObjectType+0x89C (min tech level) and PlayerObject+0x88 against GameObjectType+0x894 (tech level requirement).'}, {'order': 12, 'check': 'FUN_1403F8AA0(parent, facility) >= type+0xF0C', 'failure_code': 'error_code=2', 'description': 'Build pad/slot count check. Verifies the planet/facility has enough build slots (GameObjectType+0xF0C).'}, {'order': 13, 'check': 'FUN_1403F8B30(parent, facility) >= type+0xF10', 'failure_code': 'error_code=3', 'description': 'Secondary slot requirement check (GameObjectType+0xF10)'}, {'order': 14, 'check': 'type category check (0x5F or 0x60) - space vs land unit cap', 'failure_code': None, 'description': 'Checks unit cap per build category. For category 0x5F (space): validates against GetMaxSpaceUnits. For category 0x60 (land): validates against GetMaxLandUnits.'}, {'order': 15, 'check': 'HasPrerequisites(type+0xF50, facility_type) if type+0xF60 > 0', 'failure_code': 'error_code=5', 'description': 'Build prerequisites - other structures/tech that must exist'}, {'order': 16, 'check': 'type+0x888 (build_limit_per_player) - check instance count', 'failure_code': None, 'description': 'Per-player build limit. If type+0x888 > 0, counts existing instances across all planets.'}, {'order': 17, 'check': 'type+0x880 (build_limit_global) - check total instances', 'failure_code': 'error_code=6', 'description': 'Global build limit. If type+0x880 > 0, counts instances across all allied players.'}, {'order': 18, 'check': 'HasDuplicateCheck(type, facility_type)', 'failure_code': 'error_code=8', 'description': 'Prevents duplicate unique buildings'}
- **ComputeBuildTime**:
  - **rva**: 0x400370
  - **signature**: float ComputeBuildTime(void* production_behavior, GameObjectType* type, GameObjectClass* facility)
  - **description**: Computes the actual build time in seconds for producing a unit. Returns a multi-factor product of: base time, ability modifiers, tech level scaling, concurrent build penalty, and faction modifier. This is the core build time formula.
  - **formula**: (base_time * price_ability_mod * tech_level_factor * faction_income_mod * planet_modifier) / concurrent_build_count
  - **formula_details**:
    - **base_time**:
      - **source**: GameObjectType+0x890
      - **type**: int32
      - **description**: Base build time in engine units. Multiplied by a global speed scalar at DAT_140b15920 to get seconds. Read by FUN_140371230.
      - **global_scalar_rva**: 0xB15920
    - **price_ability_mod**:
      - **source**: ReduceProductionTimeAbilityClass via modifier system
      - **description**: Computed by FUN_14055a010. Scans active abilities on the production facility and player. Uses a tree-based accumulator with GreaterThan<float> and Plus<float> functors. Returns (1.0 - max_reduction), clamped.
    - **tech_level_factor**:
      - **source**: DAT_140b16dd4..DAT_140b16de4 (5 float globals)
      - **description**: Scaling factor based on tech level difference. Computed as: station_level = facility.parent+0xB8+0x148; diff = station_level - type+0x89C. Maps diff values 1-5 to the 5 globals. Only applies if the type is a 'can_queue_from_starbase' type (category check passes).
      - **globals**:
        - **tech_diff_1**: DAT_140b16dd4
        - **tech_diff_2**: DAT_140b16dd8
        - **tech_diff_3**: DAT_140b16ddc
        - **tech_diff_4**: DAT_140b16de0
        - **tech_diff_5**: DAT_140b16de4
    - **faction_income_mod**:
      - **source**: Player faction data -> faction+0x28
      - **description**: Income modifier from faction. Read when player.is_human is true and not in free-build mode. Accessed via FUN_1404b0500(player+0x360)+0x28.
    - **planet_modifier**:
      - **source**: Planet data array at planet+0x2FE8/0x2FE0
      - **description**: Per-planet build time modifier. Indexed by game mode (space/land). Read by FUN_14033e410.
    - **concurrent_build_count**:
      - **source**: Counted from production facility's container
      - **description**: Number of concurrent build slots being used. Min 1. Each occupied slot at facility+0xB8+0x158/0x170 that shares the same GameObjectType is counted by FUN_1404b8170. Divides the total build time (more slots = faster per-unit, but total resources stay same).
  - **return**: float: build time in seconds. Used to initialize ObjectUnderConstructionClass+0x10.
- **GetAdjustedCost**:
  - **rva**: 0x400240
  - **signature**: float GetAdjustedCost(void* production_behavior, GameObjectType* type, GameObjectClass* facility)
  - **description**: Computes the adjusted credit cost for producing a unit. Applies ability modifiers (ReduceProductionPriceAbilityClass), faction modifiers, and planet-level cost adjustments.
  - **formula**: base_cost * ability_price_mod * planet_cost_mod * tech_upgrade_mod
  - **formula_details**:
    - **base_cost**:
      - **source**: GameObjectType+0x86C
      - **type**: int32
      - **description**: Base credit cost from XML data. Read by FUN_1403711C0 when no production behavior override is present.
    - **ability_price_mod**:
      - **source**: ReduceProductionPriceAbilityClass via FUN_140559C10
      - **description**: Price reduction from active abilities. Same tree-based accumulator as build time. Returns (1.0 - max_reduction).
    - **planet_cost_mod**:
      - **source**: Planet data array at planet+0x3000/0x2FF8
      - **description**: Per-planet cost modifier. Read by FUN_14033E3E0. Indexed by game mode.
    - **tech_upgrade_mod**:
      - **source**: Player faction upgrade bonus
      - **description**: Only applies when type+0x894 (tech_level_req) != 0. Reads from faction via Plus<float>::Plus<float>_Constructor_or_Destructor.
  - **return**: float: adjusted credit cost. Negative of this is passed to AddCredits.
- **GetBaseCost**:
  - **rva**: 0x3711C0
  - **signature**: int GetBaseCost(GameObjectType* type, GameObjectClass* facility)
  - **description**: Returns the base credit cost for a unit type. If the facility has a production behavior override (QueryInterface(6)), delegates to GetAdjustedCost. Otherwise returns type+0x86C * global_scalar.
  - **base_cost_field**: GameObjectType+0x86C (int32)
- **AddCredits**:
  - **rva**: 0x27F370
  - **signature**: float AddCredits(PlayerObject* player, float amount, bool track_income)
  - **description**: Adds (or subtracts) credits from a player. For production, called with negative amount (cost). Writes to player+0x70 (credits). Clamps to [0, max_credits]. If amount > 0 and player has income modifier at +0x360, multiplies by the modifier at [+0x360]->+0x20. Core function for all economic transactions.
  - **key_fields**:
    - **credits**: PlayerObject+0x70 (float32)
    - **max_credits**: PlayerObject+0x74 (float32, negative = no cap)
    - **income_modifier_ptr**: PlayerObject+0x360 (pointer to modifier object)
  - **income_modifier_offset**: +0x20 from the modifier object (float multiplier for positive adds)
- **CheckPopCap**:
  - **rva**: 0x2AC320
  - **signature**: bool CheckPopCap(void* object_list_mgr, GameObjectType* type, uint player_slot, bool include_in_queue)
  - **description**: Validates that the player has not exceeded the population/unit cap. Computes current population by iterating all owned objects and summing their population values (GameObjectType+0x2120). Compares against the max cap (GetMaxPopCap). Also counts units currently in production queues if include_in_queue=true.
  - **algorithm**:
    - **step1**: Get current population count from GetCurrentPopulation (FUN_1402AC700)
    - **step2**: Get max population cap from object list manager
    - **step3**: Get population value of the requested unit from GetPopValue (FUN_140373500)
    - **step4**: Return: pop_value <= (max_cap - current_pop)
- **GetCurrentPopulation**:
  - **rva**: 0x2AC700
  - **signature**: int GetCurrentPopulation(void* object_list_mgr, int player_slot)
  - **description**: Counts the total population value of all units owned by the specified player. Iterates the player's object list, summing GameObjectType+0x2120 for each object. Also includes ally population if in cooperative mode (FUN_14028AFB0 alliance check).
  - **population_value_field**: GameObjectType+0x2120 (int32)
  - **base_cap_field**: FactionData+0x2EE8 (int32) - base population cap from faction definition
- **GetPopValue**:
  - **rva**: 0x373500
  - **signature**: int GetPopValue(GameObjectType* type, int game_mode)
  - **description**: Returns the population value for a unit type based on game mode. Mode 2 (space) reads +0x2128, mode 1 (land) reads +0x212C, default reads +0x2124.
  - **fields**:
    - **pop_value_default**: GameObjectType+0x2124 (int32)
    - **pop_value_space**: GameObjectType+0x2128 (int32) - used when game_mode=2
    - **pop_value_land**: GameObjectType+0x212C (int32) - used when game_mode=1
- **GetMaxSpaceUnits**:
  - **rva**: 0x372740
  - **signature**: int GetMaxSpaceUnits(GameObjectType* type, GameObjectClass* facility)
  - **description**: Returns the maximum number of space units. Checks facility+0xB8+0x230 first (planet-specific override). Falls back to type+0x92C (default from XML).
  - **override_field**: FacilityParent+0xB8+0x230
  - **default_field**: GameObjectType+0x92C
- **GetMaxLandUnits**:
  - **rva**: 0x372760
  - **signature**: int GetMaxLandUnits(GameObjectType* type, GameObjectClass* facility)
  - **description**: Returns the maximum number of land units. Checks facility+0xB8+0x234 first. Falls back to type+0x928.
  - **override_field**: FacilityParent+0xB8+0x234
  - **default_field**: GameObjectType+0x928
- **GetMaxStarbaseLevel**:
  - **rva**: 0x372780
  - **signature**: int GetMaxStarbaseLevel(GameObjectType* type, GameObjectClass* facility)
  - **description**: Returns the maximum starbase upgrade level. Checks facility+0xB8+0x238 first. Falls back to type+0x668.
  - **override_field**: FacilityParent+0xB8+0x238
  - **default_field**: GameObjectType+0x668
- **IsFreeBuilding**:
  - **rva**: 0x289050
  - **signature**: bool IsFreeBuilding(PlayerObject* player)
  - **description**: Returns true if building is free (AI or sandbox mode). Checks player+0x62 (is_human). If not human, returns the value of global DAT_140b15b20 (free_build_mode flag).
  - **is_human_field**: PlayerObject+0x62
  - **free_build_global**: DAT_140b15b20
- **IsValidBuildLocation**:
  - **rva**: 0x282400
  - **signature**: bool IsValidBuildLocation(GameObjectType* type)
  - **description**: Checks if a type can be built at the current location. Validates build slot counts (type+0x86C > 0) and build pad requirements (type+0xF0C or type+0xF10). Also checks property flags for build categories (0x5C, 0x5F).
- **CountExistingInstances**:
  - **rva**: 0x281C70
  - **signature**: int CountExistingInstances(PlayerObject* player, GameObjectType* type, bool include_allies)
  - **description**: Counts how many instances of the given type the player (and optionally allies) currently own. Used for build limit enforcement (type+0x888 per-player limit, type+0x880 global limit). Includes instances at player+0x198 (owned objects list) and recursively checks sub-types at type+0x958 and type+0x978.
- **CountInProductionQueue**:
  - **rva**: 0x281DF0
  - **signature**: int CountInProductionQueue(PlayerObject* player, GameObjectType* type, bool include_allies)
  - **description**: Counts how many of the given type are currently queued for production across all production facilities. Iterates the global object list and checks each facility's production queue. Also counts from player+0x2C8 (pending build list with count at +0x2D0).
- **CountQueuedByCategory**:
  - **rva**: 0x281AC0
  - **signature**: int CountQueuedByCategory(PlayerObject* player, GameObjectType* type, bool include_allies)
  - **description**: Counts queued units matching a specific category. Uses player+0x3A0 for fast lookup if available, otherwise iterates the full object list via FUN_1402A9FF0.
- **ProductionComplete**:
  - **rva**: 0x42E890
  - **signature**: void ProductionComplete(ObjectUnderConstructionClass* this, GameObjectClass* facility, GameObjectType* type)
  - **description**: Handles completion of a production queue item. Resolves the spawn location, creates the actual unit via FUN_14029F810, transfers ownership, fires completion events, and cleans up the queue entry. Logs: '%s: Tactical construction of final %s is complete.' Reads construction position from facility+0x84 area.
  - **key_operations**: Resolve planet location via facility+0x150 -> +0x8 chain, Check galaxy map mode via DAT_140a16fd0, Create the actual game object via FUN_14029F810, Fire ProductionCompleteEvent (event type 0x22) via FUN_140220ED0, Transfer unit properties from template, Clean up ObjectUnderConstructionClass
- **QueueInsert**:
  - **rva**: 0x46FC0
  - **signature**: void QueueInsert(DynamicVector* queue, ObjectUnderConstructionClass** entry)
  - **description**: Inserts an ObjectUnderConstructionClass entry into the production queue DynamicVector.


### Gameobjecttype Production Fields

- **description**: Fields on GameObjectType (the type definition struct) that control production behavior. These are set from XML data definitions.
- **fields**: {'offset': '0x86C', 'name': 'build_cost', 'type': 'int32', 'description': 'Base credit cost to produce this unit. Read by GetBaseCost and the production validator.'}, {'offset': '0x87C', 'name': 'build_limit_per_location', 'type': 'int32', 'description': 'Max instances of this type per build location (planet). -1 = unlimited.'}, {'offset': '0x880', 'name': 'build_limit_global', 'type': 'int32', 'description': 'Max instances across all planets/players. 0 = cannot build. Positive = limit. Checked by CountExistingInstances + CountInProductionQueue.'}, {'offset': '0x888', 'name': 'build_limit_per_player', 'type': 'int32', 'description': 'Max instances per player. 0 = cannot build. Positive = limit.'}, {'offset': '0x890', 'name': 'build_time_base', 'type': 'int32', 'description': 'Base build time in engine units. Multiplied by global scalar (DAT_140b15920) to get seconds.'}, {'offset': '0x894', 'name': 'tech_level_requirement', 'type': 'int32', 'description': 'Required tech level to build. If 0, no tech requirement. Compared against PlayerObject+0x88 (max_tech).'}, {'offset': '0x89C', 'name': 'min_tech_level', 'type': 'int32', 'description': 'Minimum tech level to appear in build menu. Compared against PlayerObject+0x84 (current tech level). Also used for tech-level-based build time scaling.'}, {'offset': '0x8A0', 'name': 'station_level_requirement', 'type': 'int32', 'description': 'Required space station level. Compared against the current station level at facility parent.'}, {'offset': '0x928', 'name': 'max_land_units_default', 'type': 'int32', 'description': 'Default max land unit count (fallback if no planet override).'}, {'offset': '0x92C', 'name': 'max_space_units_default', 'type': 'int32', 'description': 'Default max space unit count (fallback if no planet override).'}, {'offset': '0xC1', 'name': 'requires_special_build_check', 'type': 'uint8', 'description': 'If 1, triggers FUN_140282580 for a special prerequisite check.'}, {'offset': '0xC4', 'name': 'is_multiqueue_type', 'type': 'uint8', 'description': 'If 1, this type supports concurrent multi-queue building (build time divided by active queues).'}, {'offset': '0xF0C', 'name': 'required_build_pads', 'type': 'int32', 'description': 'Number of build pads required on the planet. Compared against actual pad count.'}, {'offset': '0xF10', 'name': 'required_build_slots', 'type': 'int32', 'description': 'Number of build slots required. Secondary slot check.'}, {'offset': '0xF20', 'name': 'build_categories_ptr', 'type': 'pointer', 'description': 'Pointer to array of build category entries. Each entry is 0x10 bytes: [+0x00 ptr, +0x08 ref]. Used for category-based build slot checks.'}, {'offset': '0xF28', 'name': 'build_categories_count', 'type': 'int32', 'description': 'Number of build category entries.'}, {'offset': '0xF50', 'name': 'prerequisites_list', 'type': 'DynamicVectorClass', 'description': 'List of prerequisite type references that must exist before this can be built.'}, {'offset': '0xF60', 'name': 'prerequisites_count', 'type': 'int32', 'description': 'Number of prerequisites. If 0, no prerequisite check.'}, {'offset': '0x2120', 'name': 'pop_value', 'type': 'int32', 'description': 'Population cost (how many pop points this unit consumes). Added to current population when checking cap.'}, {'offset': '0x2124', 'name': 'pop_value_default', 'type': 'int32', 'description': 'Default population value (used when game_mode is not space or land).'}, {'offset': '0x2128', 'name': 'pop_value_space', 'type': 'int32', 'description': 'Population value in space battles (game_mode=2).'}, {'offset': '0x212C', 'name': 'pop_value_land', 'type': 'int32', 'description': 'Population value in land battles (game_mode=1).'}


### Player Object Production Fields

- **description**: Fields on PlayerObject relevant to production.
- **fields**: {'offset': '0x4C', 'name': 'player_slot_id', 'type': 'int32', 'description': "Player's slot index. Used throughout production for ownership."}, {'offset': '0x62', 'name': 'is_human_player', 'type': 'uint8', 'description': '1 = human player (applies credit checks), 0 = AI (may skip credit checks).'}, {'offset': '0x70', 'name': 'credits', 'type': 'float32', 'description': 'Current credits. Deducted up-front when production starts.'}, {'offset': '0x74', 'name': 'max_credits', 'type': 'float32', 'description': 'Maximum credit cap. Negative = unlimited.'}, {'offset': '0x84', 'name': 'tech_level', 'type': 'int32', 'description': 'Current tech level. Must meet type+0x89C to build.'}, {'offset': '0x88', 'name': 'max_tech_level', 'type': 'int32', 'description': 'Maximum tech level. Must meet type+0x894 to build.'}, {'offset': '0x198', 'name': 'owned_objects_list_ptr', 'type': 'pointer', 'description': 'Pointer to the list of owned object-type/count pairs. Used by CountExistingInstances.'}, {'offset': '0x1A0', 'name': 'owned_objects_list_count', 'type': 'int32', 'description': 'Count of entries in the owned objects list.'}, {'offset': '0x2C8', 'name': 'pending_build_list_ptr', 'type': 'pointer', 'description': 'Pointer to list of pending build types (already queued).'}, {'offset': '0x2D0', 'name': 'pending_build_list_count', 'type': 'int32', 'description': 'Count of pending build entries.'}, {'offset': '0x360', 'name': 'income_modifier_ptr', 'type': 'pointer', 'description': 'Pointer to income modifier object. If non-null, positive credit adds are multiplied by [this+0x20].'}, {'offset': '0x370', 'name': 'alliance_lookup_table_ptr', 'type': 'pointer', 'description': 'Pointer to int array indexed by player_slot. Value 0 = allied, non-zero = not allied. Used for shared unit cap counting.'}, {'offset': '0x3A0', 'name': 'queued_production_tracker_ptr', 'type': 'pointer', 'description': 'Pointer to the production queue tracker. Used by CountQueuedByCategory for fast lookup.'}


### Production Queue Layout

- **description**: The production queue is stored inside ProductionDataPackClass, which is accessed via the production facility's behavior component (QueryInterface(6)). Each facility has up to 2 queues.
- **queue_structure**:
  - **queue_0**:
    - **offset_from_datapack**: 0x08
    - **name**: galactic_unit_queue
    - **type**: DynamicVectorClass<ObjectUnderConstructionClass*>
    - **queue_type_code**: 0x20
    - **description**: Queue for galactic-mode units (ships, vehicles, infantry)
  - **queue_1**:
    - **offset_from_datapack**: 0x18
    - **name**: galactic_structure_queue
    - **type**: DynamicVectorClass<ObjectUnderConstructionClass*>
    - **queue_type_code**: 0x08
    - **description**: Queue for galactic-mode structures and upgrades
- **queue_selection_logic**: If type+0xF0C != 0 OR HasProperty(type, 0x5F, -1): use queue_1 (structures, offset 0x18, type_code 0x08). Otherwise: use queue_0 (units, offset 0x30, type_code 0x20). Queue is selected in the ObjectUnderConstructionClass constructor at 0x3FFF70.
- **max_queue_depth**: Stored as global at DAT_140b26f78 (int). Checked when player is_human (player+0x62 != 0). AI players bypass queue depth limit.


### Build Pipeline Pseudocode

- **description**: Complete pseudocode for the production pipeline from UI click to unit spawn.
- **pseudocode**: // === STAGE 1: UI/Lua triggers ProductionEventClass (event 7) ===, event = new ProductionEventClass();  // RVA 0x523F50, event.event_type = 7;, EventQueueClass::Enqueue(event);  // network-synchronized, , // === STAGE 2: Event processed -> ObjectUnderConstructionClass creation ===, // FUN_1403FFF70 (ObjectUnderConstructionClass constructor/factory), function CreateProductionEntry(player, facility, type_to_build, queue_flags):,   // Validate all prerequisites,   if (!CanProduce(player, type_to_build, facility, true)):,     return false;, ,   // Get the production behavior from the facility,   prod_datapack = facility->parent_object->offset_0xD0;, ,   // Select queue based on type category,   if (type.required_build_pads > 0 || HasProperty(type, 0x5F)):,     queue_offset = 0x18;  // structure queue,     queue_type = 0x08;,   else:,     queue_offset = 0x30;  // unit queue,     queue_type = 0x20;, ,   // Check queue depth for human players,   if (player.is_human && prod_datapack[queue_offset].count >= MAX_QUEUE_DEPTH):,     return false;, ,   // Check unit cap,   if (!CheckPopCap(object_list, type_to_build, player.slot, true)):,     return false;, ,   // Compute adjusted cost,   cost = GetAdjustedCost(prod_behavior, type_to_build, facility);, ,   // Compute build time,   build_time = ComputeBuildTime(prod_behavior, type_to_build, facility);, ,   // Create the queue entry (0x38 bytes),   entry = allocate(0x38);,   entry.vtable = ObjectUnderConstructionClass::vftable;,   entry.producing_object_type = type_to_build;,   entry.build_countdown = build_time;,   entry.player_slot_id = player.slot;,   entry.producing_object_id = facility.object_id;,   entry.build_cost = cost;, ,   // Deduct credits (if not free building),   if (!IsFreeBuilding(player)):,     AddCredits(player, -cost, false);  // XOR float sign bit via DAT_140800860, ,   // Insert into queue,   QueueInsert(prod_datapack + queue_offset, &entry);, ,   // Fire network event for sync,   FireEvent(facility + 0x38, EVENT_PRODUCTION_STARTED=2, entry);, ,   // Log,   printf("Object '%s' added to production queue at '%s' by player %d: countdown is %.2f seconds",,          type_to_build.type_name, facility.type_name, player.slot, build_time);, ,   return true;, , // === STAGE 3: Build timer countdown (per frame) ===, // ScheduledEventClass timer system decrements entry.build_countdown each frame, // When countdown reaches 0, triggers completion, , // === STAGE 4: Production complete ===, // FUN_14042E890 (ObjectUnderConstructionClass::~ObjectUnderConstructionClass), function OnProductionComplete(entry, facility, type):,   planet = facility.parent->offset_0x150->offset_0x8;,   spawn_location = ResolveSpawnLocation(facility);, ,   // Spawn the actual game object,   new_object = SpawnObject(player, planet, type, spawn_location);, ,   // Transfer properties,   TransferProperties(new_object, facility);, ,   // Fire completion event (0x22),   FireEvent(new_object + 0x38, EVENT_PRODUCTION_COMPLETE=0x22, new_object);, ,   // Log,   printf("%s: Tactical construction of final '%s' is complete.", facility_name, type_name);, ,   // Clean up queue entry,   DeallocateEntry(entry);


### Globals

- **MAX_QUEUE_DEPTH**:
  - **rva**: 0xB26F78
  - **type**: int32
  - **description**: Maximum number of items in a single production queue (for human players). AI bypasses this.
- **BUILD_TIME_SCALAR**:
  - **rva**: 0xB15920
  - **type**: float32
  - **description**: Global multiplier applied to base build time (type+0x890) to convert engine units to seconds.
- **FREE_BUILD_MODE**:
  - **rva**: 0xB15B20
  - **type**: uint8
  - **description**: Global flag: if set, AI players skip credit checks.
- **TECH_DIFF_SCALARS**:
  - **rva_base**: 0xB16DD4
  - **type**: float32[5]
  - **description**: 5 floats at 0xB16DD4..0xB16DE4. Build time multiplier based on tech level difference (station_level - required_tech). Index 0 = diff of 1, etc.
- **PLAYER_LIST_GLOBAL**:
  - **rva**: 0xA16FD0
  - **type**: pointer
  - **description**: Global pointer to PlayerListClass. Used by all production functions to resolve player objects.
- **GAME_STATE_GLOBAL**:
  - **rva**: 0xB15418
  - **type**: pointer
  - **description**: Global pointer to game state. Accessed for object list management and event dispatch.


### Event Types

- **description**: Network event type IDs relevant to production.
- **events**: {'id': 7, 'name': 'ProductionEventClass', 'description': 'Player requests production of a unit/building'}, {'id': 33, 'name': 'TacticalBuildEventClass', 'description': 'Player requests tactical-mode construction'}, {'id': 46, 'name': 'GalacticSellEventClass', 'description': 'Player sells a unit/building on galactic map'}, {'id': 8, 'name': 'FleetManagementEventClass', 'description': 'Fleet management (related to production output)'}, {'id': 12, 'name': 'ReinforceEventClass', 'description': 'Reinforcement spawn from reinforcement pool'}, {'id': 42, 'name': 'DistributeMoneyEventClass', 'description': 'Credit distribution (trade routes, income)'}, {'id': 9, 'name': 'InvadeEventClass', 'description': 'Planet invasion (triggers production facility capture)'}


### Modding Recipes

- **instant_build**:
  - **description**: Set build countdown to 0 immediately after queue insertion.
  - **method**: Write 0.0f to ObjectUnderConstructionClass+0x10 (build_countdown) right after it is set. OR patch the ComputeBuildTime function (RVA 0x400370) to always return 0.0f.
  - **aob_note**: Locate the ObjectUnderConstructionClass+0x10 write in the constructor at 0x3FFF70.
- **free_build**:
  - **description**: Skip the credit deduction.
  - **method**: Patch the AddCredits call in the production constructor (0x3FFF70 -> calls 0x27F370 with negative cost). NOP the call or patch the cost to 0. Alternatively, set DAT_140b15b20 (FREE_BUILD_MODE) to 1.
  - **global_toggle_rva**: 0xB15B20
- **unlimited_pop_cap**:
  - **description**: Bypass population cap checks.
  - **method**: Patch CheckPopCap (FUN_1402AC320) to always return true. OR set FactionData+0x2EE8 to a very high value.
- **unlock_all_tech**:
  - **description**: Skip tech level requirements.
  - **method**: In CanProduce (0x2804D0), NOP the tech level comparison at the check: 'if player+0x84 != type+0x89C return 0' and 'if player+0x88 < type+0x894 return 0'.


### Cross References

- **description**: Key cross-references between production system and other engine systems.
- **refs**: {'from': 'ProductionBehaviorClass', 'to': 'BehaviorClass', 'relationship': 'inherits', 'note': 'All production facilities have this behavior component attached.'}, {'from': 'ObjectUnderConstructionClass+0x08', 'to': 'GameObjectType', 'relationship': 'references', 'note': 'Points to the type being produced. Type name at +0xF8.'}, {'from': 'CanProduce', 'to': 'PlayerObject+0x70 (credits)', 'relationship': 'reads', 'note': 'Credit check: (int)player.credits >= adjusted_cost'}, {'from': 'CanProduce', 'to': 'PlayerObject+0x84 (tech_level)', 'relationship': 'reads', 'note': 'Tech level gate'}, {'from': 'CheckPopCap', 'to': 'GameObjectType+0x2120 (pop_value)', 'relationship': 'reads', 'note': 'Accumulates population across all owned objects'}, {'from': 'ComputeBuildTime', 'to': 'ReduceProductionTimeAbilityClass', 'relationship': 'queries', 'note': 'Scans active abilities for build time modifiers'}, {'from': 'GetAdjustedCost', 'to': 'ReduceProductionPriceAbilityClass', 'relationship': 'queries', 'note': 'Scans active abilities for cost modifiers'}

