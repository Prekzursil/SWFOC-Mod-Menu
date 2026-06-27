# Galactic Map

**Planet Ownership Field**: PlanetaryDataPackClass+0x6C

**Planet Capture Progress**: PlanetaryDataPackClass+0x68

**Corruption Level**: PlanetaryDataPackClass+0x2F4

**Fow Per Player Array**: GameModeClass+0x198

**Fow Global Toggle**: 0xA284C4


### Ownership Transfer Rvas

| Key | Value |
|-----|-------|
| initial | 0x3FA160 |
| full | 0x3FB040 |

**Planet Reachability Slots**: 11


## RE Findings Detail

**Analysis**: SWFOC Galactic Map System - Reverse Engineering Results

Planet struct layout, planet list global, ownership transfer, fog of war, and trade route adjacency data from static analysis of StarWarsG.exe (x86_64, Alamo engine).

- **Date**: 2026-04-04
- **Analyst**: Agent 3E (Galactic Conquest Map System RE)


### Globals

- **PlayerListClass_ptr**:
  - **rva**: 0xA16FD0
  - **type**: pointer_to_struct
  - **struct**: PlayerListClass
  - **description**: Global pointer to PlayerListClass singleton. DAT_140a16fd0 in Ghidra. Used by every planet ownership function. Dereference once to get the struct. player_array at +0x20, player_count at +0x28.
  - **notes**: DAT_140a16fd8 is the END of the player pointer array (used as: count = (DAT_140a16fd8 - DAT_140a16fd0) >> 3).
- **PlayerListClass_end_ptr**:
  - **rva**: 0xA16FD8
  - **type**: pointer
  - **description**: End pointer of the player array. Player count = (0xA16FD8 - 0xA16FD0) >> 3 in the global-scoped usage pattern. Used in all planet ownership iteration loops.
- **GameModeManager_active_mode_ptr**:
  - **rva**: 0xB15418
  - **type**: pointer_to_struct
  - **struct**: GameModeClass
  - **description**: Global pointer to the currently active GameModeClass instance (GalacticModeClass, SpaceModeClass, or LandModeClass). DAT_140b15418 in Ghidra. Used by FOW Reveal, perception systems, and planet ownership. When in galactic mode, this points to a GalacticModeClass (vtable RVA 0x8762C0).
  - **notes**: This is THE central game state pointer. All per-player FOW objects, per-player object lists, and game-mode-specific data hang off this.
- **GameObjectTypeList_ptr**:
  - **rva**: 0xA172D0
  - **type**: pointer_to_struct
  - **description**: Global pointer to a GameObjectType list/registry. DAT_140a172d0 in Ghidra. Used by FUN_140331cc0 to find the 'is_planet' flagged type. List at +0x10 (pointer array), count at +0x18.
  - **struct_layout**:
    - **+0x10**: pointer to array of GameObjectType pointers
    - **+0x18**: int32 count
- **PerceptionManager_ptr**:
  - **rva**: 0xB153E0
  - **type**: pointer_to_struct
  - **description**: Global pointer to a perception/diplomacy manager. DAT_140b153e0 in Ghidra. Used to check faction relationships (Is_Enemy, Is_Ally) and by the planet faction change system to evaluate diplomatic state.
- **PlayerCount_static**:
  - **rva**: 0xA16FB0
  - **type**: uint32
  - **description**: Static player count. DAT_140a16fb0 in Ghidra. Used by GameModeClass constructor to allocate per-player arrays.
- **GameTickCounter_ptr**:
  - **rva**: 0xB0A340
  - **type**: float_or_int
  - **description**: DAT_140b0a340 in Ghidra. Used as a time multiplier in planet capture timer calculations.
- **FOW_GlobalToggle**:
  - **rva**: 0xA284C4
  - **type**: bool
  - **description**: DAT_140a284c4 in Ghidra. Global FOW toggle. Set by FOW 'Undo_Reveal_All' function (FUN_1406a53b0). When true, FOW is fully disabled.


### Structs

- **PlanetaryDataPackClass**:
  - **description**: Data pack attached to planet GameObjectClass instances via the behavior/component system. Contains ALL persistent galactic-map state for a planet: trade route links, ownership, garrison units, tactical built structures, upgrade objects, visibility modifiers, and capture progress. This is the planet's 'save state' that persists across tactical battles. Accessed via the PlanetaryBehaviorClass which stores a pointer to this at its +0x18 field (offset 0xB8 or 0x17*8 from behavior base).
  - **rtti_mangled_name**: .?AVPlanetaryDataPackClass@@
  - **pool_class**: PooledObjectClass<class_PlanetaryDataPackClass,20,class_EmptyLockerClass>
  - **size_known**: False
  - **size_minimum**: 0x350
  - **inherits_comment**: Uses DynamicVectorClass members extensively. The destructor reveals the member layout via inlined destructor calls.
  - **fields**: {'offset': '0x00', 'type': 'vtable_ptr', 'width': 8, 'name': 'vtable_ptr'}, {'offset_range': 'self[1]..self[2]', 'byte_offset': '0x10..0x20', 'type': 'DynamicVectorClass<PersistentTacticalBuiltObjectStruct>', 'name': 'persistent_tactical_objects', 'description': 'Stores persistent built tactical structures (land base, turrets, etc) that survive across tactical battles.'}, {'offset_range': 'self[2]..self[3]', 'byte_offset': '0x20..0x30', 'type': 'DynamicVectorClass<PersistentUpgradeObjectStruct>', 'name': 'persistent_upgrades', 'description': 'Persistent upgrade objects on this planet.'}, {'offset_range': 'self[3]..self[5]', 'byte_offset': '0x30..0x50', 'type': 'DynamicVectorClass<LineLinkStruct>', 'name': 'line_links', 'description': 'Visual/display links for the galactic map lines.'}, {'offset_range': 'self[5]..self[6]', 'byte_offset': '0x50..0x60', 'type': 'DynamicVectorClass<TradeRouteLinkEntryClass>', 'name': 'trade_route_links', 'description': "Adjacent trade routes connected to this planet. Each entry is a TradeRouteLinkEntryClass containing the connected planet reference and route data. This is the planet's adjacency list for the galactic graph."}, {'offset': '0x68', 'type': 'float32', 'width': 4, 'name': 'capture_progress', 'description': 'Current capture/ownership progress value. Written during planet faction changes. When ownership changes, this is set to the new faction change value from FUN_1403f6af0. Reset to 0 on full owner switch.', 'confidence': 'high', 'xrefs': ['function:PlanetFactionChangeClass_0x1403fa160', 'function:PlanetFactionChangeClass_0x1403fb040']}, {'offset': '0x6C', 'type': 'int32', 'width': 4, 'name': 'owning_player_id', 'description': 'Player ID (index into PlayerListClass) of the faction that owns this planet. Set to -1 (0xFFFFFFFF) during transitions. This is THE ownership field for planets. Compared and written in PlanetFactionChangeClass.', 'confidence': 'high', 'xrefs': ['function:PlanetFactionChangeClass_0x1403fa160', 'function:PlanetFactionChangeClass_0x1403fb040']}, {'offset': '0x70', 'type': 'float32', 'width': 4, 'name': 'previous_capture_progress', 'description': 'Previous capture progress value. Compared against current to detect changes.', 'confidence': 'high'}, {'offset': '0x74', 'type': 'int32', 'width': 4, 'name': 'previous_owning_player_id', 'description': 'Previous owner player ID. Used to detect ownership transitions and fire events.', 'confidence': 'high'}, {'offset': '0x78', 'type': 'pointer_or_struct', 'width': 8, 'name': 'capture_timer_data', 'description': 'Capture timer/progress tracking structure. FUN_1403fe810 writes into this + subfields.'}, {'offset': '0x98', 'type': 'int32', 'width': 4, 'name': 'capture_initiator_player_id', 'description': 'Player ID of the faction initiating the capture. Set to -1 when not being captured.', 'confidence': 'high'}, {'offset': '0x9C', 'type': 'int32', 'width': 4, 'name': 'capture_state_flags', 'description': 'Capture state flags. Set to -1 (0xFFFFFFFF) on initialization.', 'confidence': 'medium'}, {'offset': '0xA0', 'type': 'std_map', 'name': 'per_player_planet_data', 'description': 'std::map or red-black tree keyed by some per-player identifier. Iterated in FUN_1403f6af0. Contains per-player visibility/force data for this planet.'}, {'offset': '0xB0', 'type': 'int32', 'width': 4, 'name': 'income_value', 'description': 'Planet income/credit generation value. Set during faction change processing.', 'confidence': 'medium'}, {'offset': '0xB4', 'type': 'int32', 'width': 4, 'name': 'income_multiplier_or_bonus', 'description': 'Income-related secondary value.', 'confidence': 'medium'}, {'offset': '0xB8', 'type': 'uint8', 'width': 1, 'name': 'income_flag_1', 'description': 'Flag related to income processing. Set to 1 during ownership changes.', 'confidence': 'medium'}, {'offset': '0xB9', 'type': 'uint8', 'width': 1, 'name': 'income_flag_2', 'description': 'Secondary income processing flag. Set to 1 after income_flag_1.', 'confidence': 'medium'}, {'offset': '0x1C9', 'type': 'uint8', 'width': 1, 'name': 'initial_owner_set_flag', 'description': 'Set to 1 after the first ownership assignment. Guards against double-initialization in PlanetFactionChangeClass.', 'confidence': 'high'}, {'offset': '0x200', 'type': 'int32', 'width': 4, 'name': 'special_structure_owner_id', 'description': 'Owner player ID of a special structure on this planet. When matches new owner, triggers structure cleanup at +0x204 through +0x220.', 'confidence': 'medium'}, {'offset': '0x2C8', 'type': 'uint8', 'width': 1, 'name': 'planet_destroyed_flag', 'description': 'Flag indicating planet has been destroyed. Checked before AI reassignment processing.', 'confidence': 'medium'}, {'offset': '0x2E2', 'type': 'uint8', 'width': 1, 'name': 'special_state_flag_1', 'description': 'Special planet state flag. Checked in corruption processing.', 'confidence': 'medium'}, {'offset': '0x2E3', 'type': 'uint8', 'width': 1, 'name': 'capture_timer_active', 'description': 'Whether the planet capture timer is actively running.', 'confidence': 'medium'}, {'offset': '0x2E8', 'type': 'int32', 'width': 4, 'name': 'capture_start_tick', 'description': 'Game tick when capture timer started.', 'confidence': 'medium'}, {'offset': '0x2EC', 'type': 'int32', 'width': 4, 'name': 'capture_end_tick', 'description': 'Game tick when capture will complete.', 'confidence': 'medium'}, {'offset': '0x2F4', 'type': 'int32', 'width': 4, 'name': 'corruption_level', 'description': 'Planet corruption level. -1 when not corrupted. Set to 3 during ownership change by corrupting faction. Values: -1=none, 0-3=corruption tiers.', 'confidence': 'high'}, {'offset': '0x2F8', 'type': 'int32', 'width': 4, 'name': 'corruption_timer_or_state', 'description': 'Corruption timer or state field. Set to -1 on reset.', 'confidence': 'medium'}
  - **inner_structs**:
    - **TradeRouteLinkEntryClass**:
      - **description**: Entry in the trade route adjacency list. Stored in DynamicVectorClass. Connects this planet to another via a TradeRouteClass.
      - **destructor_rva**: 0x4B5F40
    - **LineLinkStruct**:
      - **description**: Visual line link data for galactic map rendering.
      - **destructor_rva**: 0x4B5DF0
    - **PersistentTacticalBuiltObjectStruct**:
      - **description**: Persistent tactical structure data (survives across battles).
      - **destructor_rva**: 0x4B5E60
    - **PersistentUpgradeObjectStruct**:
      - **description**: Persistent upgrade data.
      - **destructor_rva**: 0x4B5ED0
- **PlanetaryBehaviorClass**:
  - **description**: Behavior component attached to planet GameObjectClass instances. Manages the relationship between the planet's GameObjectClass (position, owner, etc.) and the PlanetaryDataPackClass (persistent planet state). Pool size 20. The destructor at RVA 0x3F3340 reveals the link: this->offset_0x18 is the parent GameObjectClass (the planet object), and this->offset_0x18[0x17] is the pointer to PlanetaryDataPackClass.
  - **rtti_mangled_name**: .?AVPlanetaryBehaviorClass@@
  - **pool_class**: PooledObjectClass<class_PlanetaryBehaviorClass,20,class_EmptyLockerClass>
  - **destructor_rva**: 0x3F3340
  - **size_known**: False
  - **inherits**: BehaviorClass
  - **fields**: {'offset': '0x00', 'type': 'vtable_ptr', 'width': 8, 'name': 'vtable_ptr'}, {'offset': '0x18', 'type': 'pointer', 'width': 8, 'name': 'parent_game_object_ptr', 'description': 'Pointer to the parent GameObjectClass (the planet object). Used extensively in the destructor to clean up planet state.', 'confidence': 'high'}
  - **notes**: The parent game object has: [0x17] = PlanetaryDataPackClass ptr, [0x53] = GameObjectType ptr, [0x07..0x0F] = various sub-component arrays. The PlanetaryDataPackClass at [parent+0x17*8] = [parent+0xB8] is the core data.
- **PlanetFactionChangeClass**:
  - **description**: Event/signal class that handles planet ownership transfers on the galactic map. Nested inside PlanetaryDataPackClass. Two key functions: the initial ownership set (RVA 0x3FA160) and the full transfer handler (RVA 0x3FB040). The transfer handler fires the TEXT_PLANET_CHANGED_HANDS localized string, notifies all players, updates perception, and optionally shows the TEXT_PLANET_CHANGED_HANDS_BONUS variant.
  - **destructor_rva_1**: 0x3FA160
  - **destructor_rva_2**: 0x3FB040
  - **vtable_string**: PlanetFactionChangeClass_vftable
  - **inherits**: SignalDataClass
  - **key_behaviors**:
    - **initial_ownership_set**:
      - **rva**: 0x3FA160
      - **description**: Simplified ownership setter. Writes ownership fields (planet+0x68, +0x6C, +0x70, +0x74, +0x98) and notifies all players via iteration over PlayerListClass.
      - **steps**: Read planet data ptr from param_1[0x17] (byte offset 0xB8 on parent object), Call FUN_1403f6af0 to compute new income value, Set fields at planet_data+0x68 (capture_progress), +0x6C (owner_id), +0x70 (prev_progress), +0x98 (capture_initiator) to new values, Iterate all players via FUN_140294bc0 using DAT_140a16fd0 (PlayerListClass), For each player, call FUN_1403a51e0 to update planet visibility, Fire signal via FUN_140220ed0
    - **full_ownership_transfer**:
      - **rva**: 0x3FB040
      - **description**: Full planet ownership transfer with UI notification, capture progress tracking, diplomacy checks, corruption processing, and AI budget updates.
      - **steps**: Read planet data from param_1[0x17] (PlanetaryDataPackClass), Check initial_owner_set_flag at planet_data+0x1C9, If first time: set flag, write owner to +0x6C, set capture progress to +0x68, notify all players, Compare new owner (param_2) vs current (planet_data+0x6C) -- if different, full transfer, On transfer: Reset +0x6C to new owner, +0x68 to 0, iterate players to update visibility, Look up old/new player objects via FUN_140294bc0, Read faction_affiliation from player+0x68 for both old and new owners, Format TEXT_PLANET_CHANGED_HANDS or TEXT_PLANET_CHANGED_HANDS_BONUS message, Log '%s: now controlled by %s (owner player ID %d)' via FUN_140025760, Play conquest/loss audio cues from player+0x68+0x780 / +0x790, Check corruption flag at new_player+0x68+0x10B -- if set, initiate corruption, Process capture timer at planet_data+0x2E2..+0x2F8, Update corruption level at planet_data+0x2F4 (set to 3 for corrupting faction), Notify AI subsystems for budget recomputation
- **GameModeClass_FOWFields**:
  - **description**: FOW-related fields within GameModeClass. These are at fixed offsets within the GameModeClass base.
  - **context**: GameModeClass is the base for GalacticModeClass, SpaceModeClass, LandModeClass. The active instance is pointed to by DAT_140b15418.
  - **fields**: {'offset': '0x190', 'type': 'int32', 'width': 4, 'name': 'fow_player_count', 'description': 'Number of per-player FOW objects allocated. Corresponds to self[0x32] in Ghidra notation.', 'confidence': 'high'}, {'offset': '0x198', 'type': 'pointer', 'width': 8, 'name': 'fow_per_player_array', 'description': 'Pointer to array of per-player FOW objects. Indexed by player_id. Each element is a pointer to a FOW texture/state object (0x58 bytes each, freed in destructor). Corresponds to self[0x33] in Ghidra notation.', 'confidence': 'high'}
- **FOWPlayerObject**:
  - **description**: Per-player fog of war state object. One per player, stored in GameModeClass.fow_per_player_array[player_id]. Size 0x58 bytes.
  - **size**: 0x58
  - **fields**: {'offset': '0x00', 'type': 'pointer', 'width': 8, 'name': 'visibility_grid', 'description': 'Pointer to the raw visibility grid byte array. Each byte represents visibility state for one cell: 0x00=fully fogged, 0x01-0x0F=partial visibility (fading), 0x10-0xEE=revealed (varying degrees), 0xEF=just-cleared marker, 0xFF=fully visible.', 'confidence': 'high'}, {'offset': '0x08', 'type': 'pointer', 'width': 8, 'name': 'reveal_timer_grid', 'description': 'Pointer to short[grid_size] array. Countdown timer per cell. 0=not timed (permanent reveal), -1=permanent (no countdown). Non-zero positive values decrement each tick.', 'confidence': 'high'}, {'offset': '0x10', 'type': 'pointer', 'width': 8, 'name': 'output_visibility_grid', 'description': 'Pointer to output/rendering visibility values. Written by FUN_1404c0dc0.', 'confidence': 'medium'}, {'offset': '0x20', 'type': 'int64', 'width': 8, 'name': 'grid_cell_count', 'description': 'Total number of cells in the visibility grid.', 'confidence': 'high'}, {'offset': '0x50', 'type': 'uint8', 'width': 1, 'name': 'dirty_flag', 'description': 'Set to 1 after any grid modification. Signals the renderer to re-upload the FOW texture.', 'confidence': 'high'}
- **PlanetReachabilityClass**:
  - **description**: Stores planet-to-planet reachability data for a specific player/faction. Contains 11 reachability slots (initialized in a loop of count 0xB=11). Constructor at RVA 0x663530. Uses the perception system at DAT_140b153e0 to compute pathfinding costs.
  - **constructor_rva**: 0x663530
  - **size_known**: False
  - **size_minimum**: 0x170
  - **inherits**: RefCountClass
  - **fields**: {'offset': '0x08', 'type': 'pointer', 'width': 8, 'name': 'source_planet_ptr', 'description': 'The planet this reachability data is relative to.'}, {'offset': '0x88', 'type': 'array_of_structs', 'element_size': 16, 'count': 11, 'name': 'reachability_slots', 'description': '11 slots of reachability data (each 0x10 bytes). Initialized via FUN_14020b740 and FUN_14020b930.'}, {'offset': '0x138', 'type': 'undefined8', 'width': 8, 'name': 'path_context'}, {'offset': '0x140', 'type': 'float_pair', 'width': 16, 'name': 'cost_pair_1'}, {'offset': '0x150', 'type': 'float_pair', 'width': 16, 'name': 'cost_pair_2'}, {'offset': '0x160', 'type': 'float_pair', 'width': 16, 'name': 'cost_pair_3'}, {'offset': '0x16C', 'type': 'int16', 'width': 2, 'name': 'flags_1'}, {'offset': '0x16E', 'type': 'int16', 'width': 2, 'name': 'flags_2'}
- **TradeRouteClass**:
  - **description**: Represents a trade route connecting two planets. Referenced by PlanetaryDataPackClass::TradeRouteLinkEntryClass. A DynamicVectorClass of const TradeRouteClass pointers exists (destructor at RVA 0x4AE5E0), suggesting a global or per-mode list of all trade routes.
  - **destructor_rva_list**: 0x4AE5E0
  - **size_known**: False
  - **notes**: Trade routes form the edges of the galactic map graph. Each planet has a DynamicVectorClass<TradeRouteLinkEntryClass> at its data pack offset ~0x50 that lists adjacent routes.


### Functions

- **PlayerList_FindByID**:
  - **rva**: 0x294BC0
  - **signature**: void* PlayerList_FindByID(PlayerListClass* list, int player_id)
  - **description**: Looks up a player object by ID from the player list. Bounds-checks player_id against list+0x28 (count), returns *(list+0x20)[player_id]. Returns 0 if out of bounds.
  - **params**:
    - **param_1**: longlong - pointer to PlayerListClass (or the DAT_140a16fd0 global directly)
    - **param_2**: int - player_id to look up
  - **returns**: Pointer to PlayerObject, or 0 if invalid
  - **confidence**: high
- **PlayerList_FindByFaction**:
  - **rva**: 0x294D30
  - **signature**: void* PlayerList_FindByFaction(PlayerListClass* list, void* faction_ptr)
  - **description**: Iterates all players in the list and returns the first whose faction_affiliation_ref (player+0x68) matches the given pointer. Used to look up the player owning a specific faction.
  - **params**:
    - **param_1**: longlong - pointer to PlayerListClass
    - **param_2**: longlong - faction affiliation pointer to match
  - **returns**: Pointer to matching PlayerObject, or 0
  - **confidence**: high
- **PlayerList_GetLocalPlayerID**:
  - **rva**: 0x294A70
  - **signature**: int PlayerList_GetLocalPlayerID(PlayerListClass* list)
  - **description**: Returns the player_id (field +0x4C) of the local human player. Uses list[6] as the local player index.
  - **confidence**: high
- **PlayerList_GetLocalPlayer**:
  - **rva**: 0x294A40
  - **signature**: void* PlayerList_GetLocalPlayer(PlayerListClass* list)
  - **description**: Returns the PlayerObject pointer for the local human player.
  - **confidence**: high
- **FindPlanetClass_Constructor**:
  - **rva**: 0x697010
  - **signature**: FindPlanetClass* FindPlanetClass::FindPlanetClass(FindPlanetClass* this)
  - **description**: Constructor for the Lua FindPlanet wrapper. Registers the 'Get_All_Planets' Lua method. The actual planet lookup implementation is at LAB_140254a1c (code label, not a function boundary).
  - **lua_methods_registered**: Get_All_Planets
  - **notes**: The Get_All_Planets implementation iterates whatever planet container is managed by the galactic mode and returns a Lua table of planet wrappers.
- **PlanetFactionChange_InitialSet**:
  - **rva**: 0x3FA160
  - **signature**: void PlanetFactionChange_InitialSet(PlanetFactionChangeClass* this, GameObjectClass* planet_obj)
  - **description**: Initial planet ownership assignment. Sets ownership fields on the PlanetaryDataPackClass. Iterates all players to update visibility. Called when a planet is first assigned an owner (map init or scenario load).
- **PlanetFactionChange_Transfer**:
  - **rva**: 0x3FB040
  - **signature**: void PlanetFactionChange_Transfer(PlanetFactionChangeClass* this, GameObjectClass* planet_obj, int new_owner_id, float capture_progress)
  - **description**: Full planet ownership transfer handler. Updates ownership, fires UI events, plays audio, processes corruption, updates AI budgets. The main 'Change_Owner for planets' function.
  - **log_format**: %s: now controlled by %s (owner player ID %d)
  - **ui_strings**: TEXT_PLANET_CHANGED_HANDS, TEXT_PLANET_CHANGED_HANDS_BONUS
- **Planet_UpdatePlayerVisibility**:
  - **rva**: 0x3A51E0
  - **signature**: void Planet_UpdatePlayerVisibility(GameObjectClass* planet_obj, PlayerObject* player)
  - **description**: Updates a player's visibility state for a planet. Checks if the player is the local player (via FUN_140294a40), then iterates the planet's component array (planet+0x278, count at +0x288) calling virtual method 0xA0 on each component. Sets flag at planet+0x39F.
  - **planet_fields_accessed**:
    - **+0x278**: component_array_ptr
    - **+0x288**: component_count (char)
    - **+0x2A0**: sub_object_ptr (for visibility update)
    - **+0x39F**: visibility_dirty_flag
- **PlanetOwnershipIncome_Compute**:
  - **rva**: 0x3F6AF0
  - **signature**: int PlanetOwnershipIncome_Compute(void* this, GameObjectClass* planet_obj, int* out_owner_id)
  - **description**: Computes planet income value during ownership change. Accesses planet_obj[0x53] (GameObjectType) and planet_obj[0x17] (PlanetaryDataPackClass). Uses the per-player-planet-data map at planet_data+0xA0. Writes income values to planet_data+0xB0..+0xB9.
  - **confidence**: high
- **FOW_Reveal_Tactical**:
  - **rva**: 0x6A5700
  - **signature**: void* FOW_Reveal_Tactical(GetEvent* event, lua_State* L, void* params)
  - **description**: Lua-bound FOW.Reveal function for tactical mode. Takes (player, object_or_position, radius, [decay_radius]). Gets the player's FOW object from GameModeClass+0x198[player_id], then calls FUN_14035d080 or FUN_14035d1b0 to create the reveal.
  - **lua_error_messages**: LuaFOWRevealCommandClass -- Command only valid in a tactical game., LuaFOWRevealCommandClass -- Requires at least 2 parameters: (player, object)., LuaFOWRevealCommandClass -- Expected player object as first parameter., LuaFOWRevealCommandClass -- Expected 3rd parameter for reveal radius., LuaFOWRevealCommandClass -- Undable to get a valid position from parameter 2.
- **FOW_RevealAll**:
  - **rva**: 0x6A5B00
  - **signature**: void FOW_RevealAll(void* unused, lua_State* L, void* params)
  - **description**: Lua-bound FOWManager.Reveal_All function. Takes (player). Gets player's FOW object and calls FUN_14035d4f0 to reveal all cells. Only works in tactical mode.
  - **lua_error_messages**: LuaFOWRevealCommandClass -- Command only valid in a tactical game., LuaFOWRevealCommandClass -- Requires at least 1 parameters: (player)., LuaFOWRevealCommandClass -- Expected player object as first parameter.
- **FOW_UndoRevealAll**:
  - **rva**: 0x6A53B0
  - **signature**: void FOW_UndoRevealAll(void* unused, lua_State* L, void* params)
  - **description**: Lua-bound FOWManager.Undo_Reveal_All function. Takes (bool_param). Sets DAT_140a284c4 (global FOW toggle) and calls FUN_14013ecc0.
  - **global_written**: 0xA284C4
- **FOW_TemporaryReveal**:
  - **rva**: 0x6A5CF0
  - **signature**: void FOW_TemporaryReveal(void* unused, lua_State* L, void* params)
  - **description**: Lua-bound FOWManager.Temporary_Reveal function. Takes (player, object, [radius]). Creates a time-limited reveal at the object's position. Calls FUN_140365630.
  - **lua_error_messages**: LuaFOWRevealCommandClass -- Command only valid in a tactical game., LuaFOWRevealCommandClass -- Requires 2 parameters: (player, object, [radius])., LuaFOWRevealCommandClass -- Expected player object as first parameter., LuaFOWRevealCommandClass -- Expected object as 2nd parameter., LuaFOWRevealCommandClass -- Expected number as 3rd parameter.
- **FOW_PerPlayer_Reveal**:
  - **rva**: 0x35D080
  - **signature**: void FOW_PerPlayer_Reveal(GameModeClass* mode, int player_id, position, float outer_radius, float inner_radius, void* out_handle)
  - **description**: Creates a FOW reveal circle for a specific player. Accesses mode[0x33] (fow_per_player_array) indexed by player_id. Calls FUN_1404c0ec0 on the player's FOW object to set visibility in the grid.
- **FOW_PerPlayer_RevealAll**:
  - **rva**: 0x35D4F0
  - **signature**: void FOW_PerPlayer_RevealAll(GameModeClass* mode, int player_id)
  - **description**: Reveals the entire map for a specific player. Accesses mode[0x33][player_id] and calls FUN_1404c1560 which iterates the full grid and sets all cells to fully visible.
- **FOW_UpdateGrid**:
  - **rva**: 0x4C1560
  - **signature**: void FOW_UpdateGrid(FOWPlayerObject* fow)
  - **description**: Per-frame FOW grid update. Iterates all cells (fow[4] = count). For cells with timer (fow[1][i] != 0), decrements timer. When timer expires, fades visibility. Grid byte values: 0=fogged, 0x01-0x0F=fading in, 0x10-0xEE=fading out, 0xEF=just-fogged marker, 0xFF=full visible.
  - **grid_layout**:
    - **fow[0]**: visibility_state byte array
    - **fow[1]**: reveal_timer short array
    - **fow[2]**: output_visibility byte array
    - **fow[4]**: cell_count
- **FOW_RevealCircle**:
  - **rva**: 0x4C0EC0
  - **signature**: void FOW_RevealCircle(FOWPlayerObject* fow, position, float outer_radius, float inner_radius, void* handle)
  - **description**: Reveals a circular area in the FOW grid. If outer > inner, calls FUN_1404c1000 twice (one for each radius). Otherwise calls once with combined parameters.
- **GameObjectType_FindPlanetType**:
  - **rva**: 0x331CC0
  - **signature**: void* GameObjectType_FindPlanetType(GameObjectTypeList* list)
  - **description**: Iterates the game object type list and returns the first type with flag at type+0x108 set to non-zero. This identifies the 'planet' type definition. The list is at DAT_140a172d0 (type_list+0x10 = array, type_list+0x18 = count).
  - **type_flag_offset**: 0x108
- **GalacticPathFinder_Singleton_Init**:
  - **rva**: 0x7E6920
  - **signature**: void GalacticPathFinder_Singleton_Init()
  - **description**: Initializes the GalacticPathFinderClass singleton vtable. The singleton instance pointer is at PTR_vftable_1409cf1c8.
- **PlanetaryDataPack_ResetCapture**:
  - **rva**: 0x4B6C50
  - **signature**: void PlanetaryDataPack_ResetCapture(PlanetaryDataPackClass* pack)
  - **description**: Resets capture state fields: pack+0x2E2=0, pack+0x2E3=0, pack+0x2E8=0, pack+0x2F8=-1.
  - **fields_written**:
    - **+0x2E2**: 0 (special_state_flag_1)
    - **+0x2E3**: 0 (implicitly via word write)
    - **+0x2E8**: 0 (capture_start_tick / 8 bytes)
    - **+0x2F8**: -1 (corruption_timer_or_state)
- **PlanetaryDataPack_StartCaptureTimer**:
  - **rva**: 0x4B7260
  - **signature**: void PlanetaryDataPack_StartCaptureTimer(PlanetaryDataPackClass* pack, float duration)
  - **description**: Starts the capture timer. Writes pack+0x2E3=1 (timer active), pack+0x2F8=-1, computes start/end ticks from DAT_140b15418 (game mode tick at +0x10) and DAT_140b0a340 (time scale).
  - **fields_written**:
    - **+0x2E3**: 1 (capture_timer_active)
    - **+0x2E8**: current_tick (from GameModeClass+0x10)
    - **+0x2EC**: current_tick + (time_scale * duration)
    - **+0x2F8**: -1


### Relationships

- **planet_object_to_data_pack**:
  - **description**: A planet on the galactic map is a GameObjectClass with a PlanetaryBehaviorClass component. The behavior at +0x18 points to the parent GameObjectClass. The parent's field at [0x17*8]=+0xB8 points to the PlanetaryDataPackClass. The data pack holds ownership, trade routes, income, capture state, and corruption.
  - **traversal**: planet_game_object -> [+0xB8] -> PlanetaryDataPackClass
  - **alternative**: PlanetaryBehaviorClass.offset_0x18 -> GameObjectClass -> [+0xB8] -> PlanetaryDataPackClass
- **planet_ownership**:
  - **description**: Planet ownership is stored at PlanetaryDataPackClass+0x6C as a player_id (int32). This indexes into the PlayerListClass at DAT_140a16fd0. The owning player's faction is at PlayerObject+0x68. Ownership changes go through PlanetFactionChangeClass which updates both the data pack fields and notifies all players.
  - **read_path**: planet_data+0x6C -> player_id -> PlayerList_FindByID(DAT_140a16fd0, player_id) -> PlayerObject
  - **write_path**: PlanetFactionChange_Transfer (RVA 0x3FB040) -> writes planet_data+0x6C, fires signals
- **fow_system**:
  - **description**: The FOW system is per-player and per-game-mode. The active GameModeClass (at DAT_140b15418) stores an array of per-player FOW objects at offset 0x198. Each FOW object contains three parallel arrays (visibility, timer, output) indexed by grid cell. Lua functions Reveal/Reveal_All/Undo_Reveal_All/Temporary_Reveal are bound through LuaFOWRevealCommandClass. All require tactical mode (not galactic).
  - **access_path**: DAT_140b15418 -> [+0x198] -> fow_array[player_id] -> FOWPlayerObject
  - **grid_encoding**:
    - **0x00**: fully fogged
    - **0x01-0x0F**: fading (was revealed, decreasing visibility)
    - **0x10-0xEE**: revealed (stable)
    - **0xEF**: just-cleared marker (transition state)
    - **0xFF**: fully visible (permanent or active unit vision)
  - **tactical_only**: True
  - **galactic_note**: In galactic mode, planet visibility is handled differently -- through the PlanetReachabilityClass and the per-player-planet-data map at PlanetaryDataPackClass+0xA0. The tactical FOW grid does not exist in galactic mode.
- **trade_routes_adjacency**:
  - **description**: Trade routes form the edges of the galactic map graph. Each planet's PlanetaryDataPackClass contains a DynamicVectorClass<TradeRouteLinkEntryClass> at byte offset ~0x50 that lists adjacent trade routes. The TradeRouteClass objects themselves are stored in a separate global list (DynamicVectorClass<TradeRouteClass*> destructor at RVA 0x4AE5E0). The GalacticPathFinderClass singleton uses these adjacency lists for pathfinding.
  - **per_planet**: PlanetaryDataPackClass offset ~0x50 = DynamicVectorClass<TradeRouteLinkEntryClass>
  - **global_list**: DynamicVectorClass<TradeRouteClass*> (destructor RVA 0x4AE5E0)
- **galactic_perception**:
  - **description**: The GalacticPerceptionSystemClass (constructor RVA 0x4E1880) manages what each player can see on the galactic map. This is separate from the tactical FOW system. Planet visibility in galactic mode uses FUN_1403a51e0 which iterates planet components and checks visibility per-player.
  - **constructor_rva**: 0x4E1880


### Key Data Globals Summary

- **DAT_140a16fd0**:
  - **purpose**: PlayerListClass global pointer (player_array at +0x20, count at +0x28)
  - **rva**: 0xA16FD0
- **DAT_140a16fd8**:
  - **purpose**: End of player array (count = (this - DAT_140a16fd0) >> 3)
  - **rva**: 0xA16FD8
- **DAT_140b15418**:
  - **purpose**: Active GameModeClass pointer (FOW array at +0x198, player_count at +0x190)
  - **rva**: 0xB15418
- **DAT_140a172d0**:
  - **purpose**: GameObjectType registry (array at +0x10, count at +0x18)
  - **rva**: 0xA172D0
- **DAT_140b153e0**:
  - **purpose**: Perception/diplomacy manager global
  - **rva**: 0xB153E0
- **DAT_140a284c4**:
  - **purpose**: Global FOW disable toggle (bool)
  - **rva**: 0xA284C4
- **DAT_140b15d74**:
  - **purpose**: Diplomatic capture progress multiplier 1 (float)
  - **rva**: 0xB15D74
- **DAT_140b15d78**:
  - **purpose**: Diplomatic capture progress multiplier 2 (float)
  - **rva**: 0xB15D78
- **DAT_140b0a340**:
  - **purpose**: Game time scale multiplier (for capture timer computation)
  - **rva**: 0xB0A340


### Rtti Classes Found

- PlanetaryBehaviorClass
- PlanetaryDataPackClass
- PlanetFactionChangeClass (nested in PlanetaryDataPackClass)
- PlanetReachabilityClass
- PlanetAITargetLocationClass
- PlanetDestructionAbilityClass
- PlanetIncomeBonusAbilityClass
- PlanetIncomeGamblingAbilityClass
- CorruptPlanetAbilityClass
- GalacticModeClass
- GalacticCameraClass
- GalacticGoalSystemClass
- GalacticPerceptionSystemClass
- GalacticSabotageAbilityClass
- GalacticSellEventClass
- GalacticStealthAbilityClass
- GalacticPathFinderClass (singleton)
- TradeRouteClass
- FindPlanetClass (Lua wrapper)
- LuaFOWRevealCommandClass
- RevealBehaviorClass
- StoryEventSelectPlanetClass
- PlanetaryBombardEventClass


### Lua Bindings Discovered

- **FindPlanetClass**:
  - **methods**: Get_All_Planets
  - **constructor_rva**: 0x697010
- **LuaFOWRevealCommandClass**:
  - **methods**: Reveal (tactical), Reveal_All, Undo_Reveal_All, Temporary_Reveal
  - **constructor_rva**: 0x6A51B0
  - **method_implementations**:
    - **Reveal**: 0x6A5700
    - **Reveal_All**: 0x6A5B00
    - **Undo_Reveal_All**: 0x6A53B0
    - **Temporary_Reveal**: 0x6A5CF0
- **GalacticFreeStoreClass**:
  - **constructor_rva**: 0x68EAF0
- **GalacticTaskForceClass**:
  - **constructor_rva**: 0x6CCE20


### Open Questions

- Exact planet list global: Planets are GameObjectClass instances managed by the galactic mode. The Get_All_Planets Lua function iterates them, but the actual global pointer to the 'planet list' container has not been isolated to a single RVA. It likely lives inside the GalacticModeClass instance (sub-object of GameModeClass) or is accessed via the FindPlanetClass lookup mechanism.
- PlanetaryDataPackClass full size: Destructor analysis shows fields up to at least offset 0x350+ (self[0x34] = byte offset 0x1A0+), but the total size with all DynamicVector members is larger.
- Trade route edge data: The TradeRouteLinkEntryClass internal structure (which planet it connects to, travel time, hyperlane type) needs runtime validation.
- Galactic FOW vs tactical FOW: Galactic mode visibility is handled by GalacticPerceptionSystemClass and per-player planet data, NOT by the tactical FOW grid system. The exact galactic visibility structure needs further RE.
- Planet position data: Where x/y/z galactic map coordinates are stored on the planet object. Likely inherited from GameObjectClass transform component.

