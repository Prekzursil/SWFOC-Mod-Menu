# StarWarsG.exe Verified RVA Reference

**Auto-generated** from `knowledge-base/verified_facts.json` — do not hand-edit.
Re-run via `python tools/generate_verified_rvas_md.py` after any ledger change.

Image base: `0x140000000` (Ghidra convention). At runtime, add the live module base.
**Total VERIFIED entries with RVA: 309**
(skipped 36 non-VERIFIED or rva-less entries)

## engine_function (204)

| RVA | fact_id | claim |
|---|---|---|
| `0x17F1D0` | `rva_d3d_build_camera_matrices` | D3D camera-matrix build routine at RVA 0x17F1D0 (sub_14017F1D0, size 0x32A). Rebuilds the global view matrix (0xA6EEE4) and global projection matrix (0xA6EF24) each camera update. Branches on the p... |
| `0x1D98B0` | `rva_gui_gadget_component_type_enum_init` | Static initializer for EnumConversionClass<GUIGadgetComponentType>. Constructs the program-lifetime singleton mapping engine GUI-gadget component-type enum values to their canonical UI-element-name... |
| `0x1DB6F0` | `rva_gui_gadget_type_enum_init` | Static initializer for EnumConversionClass<GUIGadgetType>. Maps GUIGadgetType to canonical UI-widget-type strings: ComboBox, EditBox, HScrollBar, Image, ImeEditBox, ListBox, OverlayCaption, Progres... |
| `0x215A30` | `rva_crc32_hash` | CRC32_Hash -- standard CRC32 with table at 0xA14D20. |
| `0x242570` | `rva_lua_script_ctor` | LuaScriptClass::ctor (DISCOVERED-GHIDRA). |
| `0x2567B0` | `rva_lua_script_register_globals` | LuaScriptClass::RegisterGlobals -- 14 globals including GetEvent. |
| `0x261470` | `rva_camera_class_ctor` | CameraClass::ctor -- sub-object 0x308 bytes. |
| `0x261A40` | `rva_camera_get_position` | CameraClass::GetPosition -- +0x0C, +0x1C, +0x2C. |
| `0x261BD0` | `rva_camera_set_transform_matrix` | CameraClass::SetTransformMatrix (CONFIRMED-RE). |
| `0x2641C0` | `rva_model_emitter_type_enum_init` | Static initializer for EnumConversionClass<ModelClass::EmitterType>. Maps ModelClass::EmitterType. **1 entry extracted** (Power_To_Weapons) — borderline result that may indicate a partial-mapping p... |
| `0x279010` | `rva_model_anim_type_enum_init` | Static initializer for EnumConversionClass<ModelAnimType>. Constructs the program-lifetime singleton mapping engine ModelAnimType enum values to their canonical animation-name strings ("Saber_Throw... |
| `0x27ED40` | `rva_player_destructor` | PlayerClass::~PlayerClass destructor -- Ghidra destructor analysis, RTTI confirmed. |
| `0x27F370` | `rva_add_credits` | PlayerClass::AddCredits(player, amount, track) -- AOB signature verified unique, CE tested live. |
| `0x27F7C0` | `rva_lock_tech_add` | Lock_Tech_Add -- Ghidra decompilation of Lock_Tech flow. |
| `0x27F860` | `rva_unlock_tech_engine` | Unlock_Tech engine function -- writes to +0x1B0/0x1B8 confirmed. |
| `0x2804D0` | `rva_can_produce` | CanProduce -- 18 prerequisite gates. |
| `0x282190` | `rva_auto_upgrade_tech` | Auto_Upgrade_Tech -- increments tech_level at +0x84 (DISCOVERED-GHIDRA). |
| `0x2823E0` | `rva_is_ally_engine` | PlayerClass::Is_Ally -- returns diplomacy_table[id] == 0. |
| `0x2824F0` | `rva_is_enemy_engine` | PlayerClass::Is_Enemy -- returns diplomacy_table[id] == 1. |
| `0x282550` | `rva_is_locked` | PlayerClass::IsLocked -- reads +0x1C8/0x1D0. |
| `0x282580` | `rva_is_unlocked` | PlayerClass::IsUnlocked -- reads +0x1B0/0x1B8. |
| `0x286100` | `rva_unlock_tech_remove` | Unlock_Tech_Remove -- removes from locked list. |
| `0x286150` | `rva_lock_tech_engine` | Lock_Tech engine function -- writes to locked_types DVec. |
| `0x288800` | `rva_make_ally_make_enemy_engine` | Make_Ally / Make_Enemy engine function -- writes diplomacy_table[target_id]. |
| `0x288980` | `rva_set_tech_level` | PlayerClass::SetTechLevel -- AOB verified unique, Phase 2 RE. |
| `0x28A950` | `rva_game_mode_manager_get_mode_by_type` | GameModeManagerClass::GetModeByType -- iterates modes. |
| `0x2924D0` | `rva_player_list_set_current_by_faction` | PlayerListClass::Set_Current_Player_By_Faction at RVA 0x2924D0. Galactic-mode equivalent of Switch_Sides. Takes (playerList, modeType, factionPtr, playerObjOrNull). Walks the PlayerListClass intern... |
| `0x294A40` | `rva_player_list_get_current_player` | PlayerListClass::GetCurrentPlayer at RVA 0x294A40. Companion reader to Switch_Sides. Returns vec[currentSlot] where vec is the vector<PlayerObject*> at PlayerListClass+0x00 and currentSlot is the i... |
| `0x294BC0` | `rva_player_list_find_by_id` | PlayerList_FindByID -- Phase 1 RE + FoCAPI exact match. |
| `0x297E80` | `rva_player_list_switch_sides` | PlayerListClass::Switch_Sides at RVA 0x297E80. Canonical method for rotating the human-controlled player slot. Takes a PlayerListClass* (the global at 0xA16FD0). Increments *(int*)(this + 0x30), wr... |
| `0x2AC320` | `rva_check_pop_cap` | CheckPopCap -- unit cap validation. |
| `0x2B59B0` | `rva_gamemode_refresh_local_player_subsystems` | GameModeClass::Refresh_Local_Player_Subsystems at RVA 0x2B59B0. Walks the linked list at *(activeGameMode + 72) using +8 next pointers and terminates at the sentinel (activeGameMode + 64). For each... |
| `0x2BD2F0` | `rva_select_object_engine` | Select_Object_Engine -- called by Lua Select_Object wrapper. |
| `0x331CC0` | `rva_find_planet_type` | FindPlanetType -- checks is_planet flag. |
| `0x333E73` | `rva_apocalypticx_build_skirmish` | Apocalypticx community CE table: Build time/cost (Skirmish) at 0x333E73. AOB verified. |
| `0x340920` | `rva_retreat_engine` | Retreat_Engine -- called by Lua Retreat wrapper. |
| `0x341850` | `rva_victory_monitor_ctor` | VictoryMonitorClass constructor. 358-byte function. Initializes 48-byte AwaitingVictoryTest DynamicVector at instance+0x68 (length, capacity, base pointer triple at +0x68/+0x70/+0x78), parent struc... |
| `0x3419C0` | `rva_victory_monitor_dtor_vec` | VictoryMonitorClass::DynamicVectorClass<AwaitingVictoryTestType> destructor. 98-byte function. Member-function dtor on the AwaitingVictoryTests vector subobject (parent+0x60). Operations: (1) write... |
| `0x341AF0` | `rva_victory_monitor_dtor_full` | VictoryMonitorClass full destructor. 329-byte function. Cleans up TWO embedded vectors: (a) outer vector at instance+0x08/+0x10 with stride 0x30 (48 bytes) — walks per-element calling sub_140066600... |
| `0x341FE0` | `rva_victory_monitor_counter_inc` | VictoryMonitorClass per-tick frame-counter increment helper. 16-byte utility: ++[rcx+0x5C], clamp to >=0. Called by VictoryMonitor parent tick (rva_victory_monitor_parent_tick @ 0x456970) and many ... |
| `0x341FF0` | `rva_victory_type_enum_init` | Static initializer for EnumConversionClass<VictoryType>. Maps VictoryType to canonical win-condition strings across all game modes: Galactic_Conquer, Galactic_Control, Galactic_Cycles, Galactic_Kil... |
| `0x35D4F0` | `rva_fow_sub_14035d4f0` | FOW reveal underlying function -- validates game mode, bounds-checks player index, calls sub_1404C1560 for full reveal. |
| `0x3711C0` | `rva_get_adjusted_cost_variant` | GetAdjustedCost variant -- cost modifier chain. |
| `0x372320` | `rva_get_max_front_shield` | GetMaxFrontShield -- reads type+0xDD0 (DISCOVERED-GHIDRA). |
| `0x3725F0` | `rva_get_max_rear_shield` | GetMaxRearShield -- reads type+0xDD4 (DISCOVERED-GHIDRA). |
| `0x3727A0` | `rva_get_max_health` | GetMaxHealth -- reads type+0xDCC, applies multipliers. |
| `0x374DA0` | `rva_has_combat_behavior` | HasCombatBehavior -- checked during spawn init. |
| `0x3751A0` | `rva_faction_affiliation_check` | FactionAffiliation_Check -- used by SetTechLevel, CanProduce. |
| `0x387010` | `rva_weapon_tick` | WeaponTick -- per-frame weapon update, cooldown via delta-time. |
| `0x387F50` | `rva_hardpoint_fire` | HardpointFire -- per-hardpoint damage, station level loss. |
| `0x388720` | `rva_game_object_ctor_default` | GameObjectClass::ctor (default) -- Ghidra, writes vtable 0x8661B8. |
| `0x388B60` | `rva_game_object_ctor_full` | GameObjectClass::ctor (full) -- Ghidra, 5+ params. |
| `0x38A350` | `rva_take_damage_outer` | Take_Damage_Outer -- damage routing function with 56 damage type cases and 15 calls to CanReceiveDamageType; does NOT directly check obj+0x3A7 invuln flag. |
| `0x38C570` | `rva_behavior_attach` | BehaviorAttach -- attaches behavior object to unit. Called by Make_Invulnerable Lua wrapper. |
| `0x38D730` | `rva_fire_control_dispatch` | FireControl_Dispatch -- master fire control, enemy/invuln/LOS checks. |
| `0x38EB10` | `rva_death_signal_behavior` | Death_Signal_Behavior -- notifies behavior system of death. |
| `0x38F8B0` | `rva_clear_speed_override` | ClearSpeedOverride -- AOB verified unique. |
| `0x3956C0` | `rva_resolve_parent_owner` | ResolveParentOwner -- Phase 1 RE. |
| `0x395920` | `rva_sub_object_owner_resolve` | SubObject_OwnerResolve -- reads +0xB8, +0xD8 (DISCOVERED-GHIDRA). |
| `0x395AC0` | `rva_query_interface` | GameObjectClass::QueryInterface(obj, type) -- confirmed at runtime via Phase 1 RE, pseudocode available. |
| `0x395C70` | `rva_buff_modifier_read` | BuffModifier_Read -- reads +0xF0 (DISCOVERED-GHIDRA). |
| `0x3963C0` | `rva_front_shield_read` | FrontShield_Read -- reads front shield current value. |
| `0x396DF0` | `rva_get_hull_percentage` | GetHullPercentage -- per-hardpoint hull ratio. |
| `0x3989A0` | `rva_spawn_init` | Spawn_Init -- allocates combatant (0x3B8), sets initial HP. |
| `0x39BDB0` | `rva_death_handler` | Death_Handler -- full death sequence (17 steps). |
| `0x3A06A0` | `rva_property_init_hp` | Property_Init_HP -- initial HP from XML data system. |
| `0x3A1B4C` | `rva_apocalypticx_build_conquest` | Apocalypticx community CE table: Build time/cost (Conquest) at 0x3A1B4C. AOB verified. |
| `0x3A54C0` | `rva_behavior_remove_dispatch` | BehaviorRemoveDispatch -- removes behavior from unit. Called by Make_Invulnerable via QI(75). |
| `0x3A56B0` | `rva_invulnerability_cleanup` | Invulnerability_Cleanup -- cleans up invulnerability state. |
| `0x3A8630` | `rva_set_front_shield` | SetFrontShield -- front shield write. |
| `0x3A89D0` | `rva_set_hp` | SetHP(obj, float) -- Phase 1 RE + CT hook + AOB unique. Writes obj+0x5C. |
| `0x3A8C90` | `rva_set_speed_override` | SetSpeedOverride -- AOB verified unique. |
| `0x3A91E0` | `rva_set_rear_shield` | SetRearShield -- rear shield write. |
| `0x3A92F0` | `rva_animation_dispatch` | AnimationDispatch -- damage type routing, Berserker override. |
| `0x3A97E0` | `rva_take_damage_property_dispatch` | Take_Damage_PropertyDispatch -- shield/hardpoint routing. |
| `0x3A9E30` | `rva_take_damage_function` | GameObjectClass::Take_Damage entry at RVA 0x3A9E30 -- the central damage-application function (single chokepoint that ~58 callers reach to mutate a target's hull/shield). |
| `0x3AB890` | `rva_take_damage_impl` | Take_Damage_Impl -- core old_hp - damage, prevent-death. |
| `0x3ABB80` | `rva_set_position` | SetPosition/Teleport -- propagates position to hardpoints via QI(0x16). Previously MISLABELED Make_Invulnerable_Setter. |
| `0x3AC290` | `rva_damage_visual_level` | DamageVisualLevel -- damage model swap thresholds. |
| `0x3AC530` | `rva_transform_update` | Transform_Update -- signal listener notify, position update (DISCOVERED-GHIDRA). |
| `0x3AC9D0` | `rva_select_event_ctor` | SelectEventClass::ctor -- Event ID=5. |
| `0x3C2B20` | `rva_galactic_camera_ctor` | GalacticCameraClass::ctor (CONFIRMED-RE). |
| `0x3C2C00` | `rva_galactic_camera_update` | GalacticCameraClass::Update -- main frame update. |
| `0x3F3340` | `rva_planetary_behavior_dtor` | PlanetaryBehaviorClass::dtor -- links planet to data pack. |
| `0x3F6AF0` | `rva_planet_compute_income` | Planet_ComputeIncomeValue -- called during ownership changes. |
| `0x3F8AA0` | `rva_buildpad_check_slots` | BuildPad_CheckSlots -- validates build pad availability. |
| `0x3F8B30` | `rva_buildpad_check_secondary` | BuildPad_CheckSecondary -- secondary slot check. |
| `0x3FA160` | `rva_planet_faction_change_initial` | PlanetFactionChange_InitialSet -- initial ownership assignment. |
| `0x3FB040` | `rva_planet_faction_change_full` | PlanetFactionChange_FullTransfer -- full ownership transfer with UI. |
| `0x3FE810` | `rva_capture_timer_update` | CaptureTimer_Update -- writes capture timer fields. |
| `0x3FFF10` | `rva_production_behavior_dtor` | ProductionBehaviorClass::dtor (DISCOVERED-GHIDRA). |
| `0x3FFF70` | `rva_object_under_construction_ctor` | ObjectUnderConstructionClass::ctor -- 0x38 bytes (DISCOVERED-GHIDRA). |
| `0x400240` | `rva_get_adjusted_cost` | GetAdjustedCost -- modified cost calculation. |
| `0x400370` | `rva_compute_build_time` | ComputeBuildTime -- 5-factor formula. |
| `0x405230` | `rva_hull_ratio_via_hardpoints` | HullRatio_ViaHardpoints -- aggregate health display. |
| `0x4052D0` | `rva_get_hardpoint` | GetHardpoint -- returns hardpoint by index from hardpoint manager. |
| `0x405300` | `rva_hardpoint_count` | HardpointCount -- returns number of hardpoints from manager. |
| `0x42D850` | `rva_tactical_build_behavior_dtor` | TacticalBuildObjectsBehaviorClass::dtor (DISCOVERED-GHIDRA). |
| `0x42DBD0` | `rva_lua_set_hull_caller` | Lua_Set_Hull caller -- SetHP caller from Lua. |
| `0x42E890` | `rva_production_completion_handler` | Production_CompletionHandler -- spawns finished unit. |
| `0x4501D0` | `rva_story_event_ctor` | StoryEventClass::ctor -- size >= 0x360. |
| `0x4504E0` | `rva_story_event_command_unit_ctor` | StoryEventCommandUnitClass::ctor (DISCOVERED-GHIDRA). |
| `0x450540` | `rva_story_event_hero_move_ctor` | StoryEventHeroMoveClass::ctor (DISCOVERED-GHIDRA). |
| `0x450600` | `rva_story_event_dtor` | StoryEventClass::dtor (CONFIRMED-RE). |
| `0x451974` | `rva_apocalypticx_fow_visibility` | Apocalypticx community CE table: Fog of war visibility check at 0x451974. AOB verified. |
| `0x452D70` | `rva_story_subplot_compute_dependencies` | StorySubPlot_ComputeDependencies -- builds dependency graph. |
| `0x453310` | `rva_story_event_factory_create` | StoryEvent_Factory_Create -- 61 type IDs, switch/case. |
| `0x4562A0` | `rva_story_event_build_from_parsed` | StoryEvent_BuildFromParsed (DISCOVERED-GHIDRA). |
| `0x456970` | `rva_victory_monitor_parent_tick` | Parent-class TICK / per-frame orchestrator that owns VictoryMonitor as a subsystem. 15,632-byte function with 62 reads at [r+0x68] (likely AwaitingVictoryTest vector iteration), 85 reads at [r+0x60... |
| `0x45C3F0` | `rva_resolve_reward_type_from_string` | ResolveRewardTypeFromString -- CRC32 tree lookup. |
| `0x45C5D0` | `rva_story_event_parse_xml_block` | StoryEvent_ParseXMLBlock (DISCOVERED-GHIDRA). |
| `0x4747E0` | `rva_ai_the_ai_data_mgr_ctor` | TheAIDataManagerClass::ctor -- singleton (DISCOVERED-GHIDRA). |
| `0x48EB10` | `rva_schedule_hero_respawn` | ScheduleHeroRespawn -- Phase 2 RE. |
| `0x497900` | `rva_object_under_construction_serialize` | ObjectUnderConstruction_Serialize -- save/load. |
| `0x4AF810` | `rva_ai_player_class_ctor` | AIPlayerClass::ctor -- per-player AI controller. AIPlayerClass inherits from AIDiagnosticsClass (per Binary Ninja vftable demangling). Three-tool VERIFIED. |
| `0x4AFF50` | `rva_ai_player_class_simple_factory` | AIPlayerClass simple factory: (PlayerObject*) -> new AIPlayerClass*. Allocates 0x60 bytes, calls AIPlayerClass::ctor with (this, PlayerObject, 0). Multi-tool VERIFIED. |
| `0x4AFF90` | `rva_ai_player_class_typed_factory` | AIPlayerClass typed factory: (PlayerObject*, type_name_cstr) -> new AIPlayerClass*. Looks up the AI type by name, allocates+constructs, then iterates AI components for initialisation. Three-tool VE... |
| `0x4B0250` | `rva_ai_enable_as_actor` | Enable_As_Actor -- AI enable Lua binding (CONFIRMED-RE, single tool). |
| `0x4B06D0` | `rva_ai_release_credits_tactical` | Release_Credits_For_Tactical -- AI credits release (CONFIRMED-RE, single tool). |
| `0x4B1270` | `rva_galactic_mode_ctor` | GalacticModeClass::ctor (DISCOVERED-GHIDRA). |
| `0x4D9C80` | `rva_ai_the_ai_class_ctor` | TheAIClass::ctor -- top-level singleton (DISCOVERED-GHIDRA). |
| `0x4DAD80` | `rva_ai_perception_system_ctor` | AIPerceptionSystemClass::ctor -- base perception (DISCOVERED-GHIDRA). |
| `0x4E1880` | `rva_ai_galactic_perception_ctor` | GalacticPerceptionSystemClass::ctor -- galactic mode (DISCOVERED-GHIDRA). |
| `0x4E8920` | `rva_ai_template_mgr_ctor` | TheAITemplateManagerClass::ctor (DISCOVERED-GHIDRA). |
| `0x4E9940` | `rva_ai_player_type_mgr_ctor` | TheAIPlayerTypeManagerClass::ctor (DISCOVERED-GHIDRA). |
| `0x510120` | `rva_visibility_level_type_enum_init` | Static initializer for EnumConversionClass<tVisibilityLevelType>. Maps tVisibilityLevelType to canonical visibility-level strings: Credit_Income, Enemy_Major_Stealth, Enemy_Minor_Stealth, Evil_Defa... |
| `0x523F50` | `rva_production_event_ctor` | ProductionEventClass::ctor -- Event ID=7. |
| `0x5242C0` | `rva_object_under_construction_dtor` | ObjectUnderConstructionClass::dtor (DISCOVERED-GHIDRA). |
| `0x524CE0` | `rva_ai_execution_system_ctor` | AIExecutionSystemClass::ctor (DISCOVERED-GHIDRA). |
| `0x52D400` | `rva_story_subplot_ctor` | StorySubPlotClass::ctor -- size >= 0x650. |
| `0x52E220` | `rva_story_subplot_dtor` | StorySubPlotClass::dtor (CONFIRMED-RE). |
| `0x52EF90` | `rva_story_subplot_find_event_by_name` | StorySubPlot_FindEventByName -- CRC32 XOR 0xDEADBEEF + LCG. |
| `0x52FC10` | `rva_story_subplot_reset` | StorySubPlot_Reset -- iterates events, clears +0x4C. |
| `0x532690` | `rva_galactic_sell_event_ctor` | GalacticSellEventClass::ctor -- Event ID=46. |
| `0x53F7B0` | `rva_hard_point_type_enum_init` | Static initializer for EnumConversionClass<HardPointType>. Constructs the program-lifetime singleton mapping engine HardPointType enum values to their canonical hardpoint-category strings: Dummy_Ar... |
| `0x549490` | `rva_rear_shield_read_impl` | RearShield_Read_Impl -- rear shield read. |
| `0x549810` | `rva_rear_shield_write_impl` | RearShield_Write_Impl -- rear shield write. |
| `0x559680` | `rva_production_data_pack_ctor` | ProductionDataPackClass::ctor -- 2 queues (DISCOVERED-GHIDRA). |
| `0x561270` | `rva_tactical_build_datapack_ctor` | TacticalBuildObjectsDataPackClass::ctor (DISCOVERED-GHIDRA). |
| `0x56BFB0` | `rva_front_shield_read_impl` | FrontShield_Read_Impl -- shield behavior read. |
| `0x56C1B0` | `rva_front_shield_write_impl` | FrontShield_Write_Impl -- shield behavior write. |
| `0x574BF0` | `rva_change_owner` | Change_Owner -- Phase 2 RE. |
| `0x5792E0` | `rva_get_owner_lua` | Get_Owner_Lua -- Phase 1 RE, pseudocode available. |
| `0x57E590` | `rva_gameobjectwrapper_override_max_speed` | GameObjectWrapper::Override_Max_Speed Lua-binding wrapper at RVA 0x57E590 -- engine-native Lua API for setting per-unit max speed. Operator-controllable via SWFOC_DoString. |
| `0x585D00` | `rva_ai_learning_system_ctor` | AILearningSystemClass::ctor -- 5 hash maps (DISCOVERED-GHIDRA). |
| `0x5D70F0` | `rva_health_regen_periodic` | HealthRegen_Periodic -- natural HP regen. |
| `0x5D7740` | `rva_tactical_build_event_ctor` | TacticalBuildEventClass::ctor -- Event ID=33. |
| `0x5DD090` | `rva_cell_passability_type_enum_init` | Static initializer for EnumConversionClass<CellPassabilityType>. Maps CellPassabilityType to canonical pathfinding-cell strings: Empirewall, Impassable, Infantryonly, Shield, Water. **5 entries** —... |
| `0x5DEA20` | `rva_unit_ability_type_enum_init` | Static initializer for EnumConversionClass<UnitAbilityType>. Constructs the program-lifetime singleton that maps the engine's UnitAbilityType enum values to their canonical string forms ("Tractor_B... |
| `0x5E0590` | `rva_corruption_type_enum_init` | Static initializer for EnumConversionClass<CorruptionTypeEnum>. Constructs the program-lifetime singleton mapping engine CorruptionTypeEnum values to canonical Underworld corruption-type strings: C... |
| `0x5E0C00` | `rva_space_layer_type_enum_init` | Static initializer for EnumConversionClass<SpaceLayerType>. Maps SpaceLayerType to canonical space-mode unit-tier strings: Capital, Frigate, Supercapital. **3 entries** — describes the engine's spa... |
| `0x5E1110` | `rva_unit_collision_class_type_enum_init` | Static initializer for EnumConversionClass<UnitCollisionClassType>. Maps UnitCollisionClassType to canonical collision-category strings: Bike, Giant_Vehicle, Large_Vehicle, Vehicle. **4 entries** —... |
| `0x5E1AC0` | `rva_damage_type_enum_init` | Static initializer for EnumConversionClass<tDamageType>. Maps tDamageType to canonical damage-category strings: Damage_Asteroid, Damage_Cable_Attack, Damage_Crush, Damage_Drain_Life, Damage_Eat, Da... |
| `0x5E2E30` | `rva_locomotor_state_type_enum_init` | Static initializer for EnumConversionClass<LocomotorStateType>. Maps LocomotorStateType to canonical movement-state strings: Bike_End_Move, Bike_Moving, Bike_PreStopped, Bike_Start_Move, Bike_Stopp... |
| `0x5E42E0` | `rva_ability_activation_type_enum_init` | Static initializer for EnumConversionClass<AbilityActivationType>. Constructs the program-lifetime singleton mapping engine AbilityActivationType values to canonical activation-trigger strings: Com... |
| `0x5E4A80` | `rva_light_effect_type_enum_init` | Static initializer for EnumConversionClass<LightEffectType>. Maps LightEffectType to canonical light-effect strings. **1 entry** extracted: Continuous_Smooth — describes light-pulse animation style. |
| `0x5E4F20` | `rva_ai_goal_application_type_enum_init` | Static initializer for EnumConversionClass<AIGoalApplicationType>. Maps AIGoalApplicationType to canonical AI-target-category strings: Enemy_Build_Pad, Enemy_Cash_Point, Enemy_Reinforcements, Enemy... |
| `0x5E5C30` | `rva_ai_goal_proposal_fn_mgr_ctor` | TheAIGoalProposalFunctionSetManagerClass::ctor (DISCOVERED-GHIDRA). |
| `0x5E6690` | `rva_ai_goal_type_mgr_ctor` | TheAIGoalTypeManagerClass::ctor (DISCOVERED-GHIDRA). |
| `0x5E70B0` | `rva_ai_goal_reachability_type_enum_init` | Static initializer for EnumConversionClass<AIGoalReachabilityType>. Maps AIGoalReachabilityType to canonical AI threat-evaluation strings: Any_Threat, Friendly_Ignore, High_Threat, Medium_Threat. *... |
| `0x6109C0` | `rva_ai_budget_class_ctor` | AIBudgetClass::ctor (DISCOVERED-GHIDRA, single tool). |
| `0x610A70` | `rva_ai_budgeted_category_struct_ctor` | BudgetedCategoryStruct::ctor (DISCOVERED-GHIDRA). |
| `0x6383E0` | `rva_combatant_behavior_ctor` | CombatantBehaviorClass::ctor (DISCOVERED-GHIDRA). |
| `0x6478C0` | `rva_ai_build_task_class_ctor` | AIBuildTaskClass::ctor -- FSM with 25 states (DISCOVERED-GHIDRA). |
| `0x64AEC0` | `rva_ai_build_task_state_machine_handler` | AIBuildTaskClass::StateMachineHandler (DISCOVERED-GHIDRA). |
| `0x64C250` | `rva_ai_serviced_system_ctor` | ServicedAISystemClass::ctor -- base subsystem (DISCOVERED-GHIDRA). |
| `0x653340` | `rva_ai_tactical_perception_grid_ctor` | TacticalPerceptionGridClass::ctor (DISCOVERED-GHIDRA). |
| `0x663530` | `rva_planet_reachability_ctor` | PlanetReachabilityClass::ctor -- 11 slots (DISCOVERED-GHIDRA). |
| `0x6954B0` | `rva_ai_planet_build_task_ctor` | AIPlanetBuildTaskClass::ctor (DISCOVERED-GHIDRA). |
| `0x6A53B0` | `rva_fow_undo_reveal_all` | FOW_Undo_Reveal_All -- sets global toggle. |
| `0x6B8480` | `rva_ai_galactic_goal_system_ctor` | GalacticGoalSystemClass::ctor (DISCOVERED-GHIDRA). |
| `0x6B86E0` | `rva_ai_land_goal_system_ctor` | LandGoalSystemClass::ctor (DISCOVERED-GHIDRA). |
| `0x6B88D0` | `rva_ai_space_goal_system_ctor` | SpaceGoalSystemClass::ctor (DISCOVERED-GHIDRA). |
| `0x6B8980` | `rva_ai_land_perception_ctor` | LandPerceptionSystemClass::ctor (DISCOVERED-GHIDRA). |
| `0x6B9A20` | `rva_ai_space_perception_ctor` | SpacePerceptionSystemClass::ctor (DISCOVERED-GHIDRA). |
| `0x6BAC00` | `rva_ai_planning_system_ctor` | AIPlanningSystemClass::ctor -- 4.0s interval (DISCOVERED-GHIDRA). |
| `0x6BB9E0` | `rva_ai_template_system_ctor` | AITemplateSystemClass::ctor (DISCOVERED-GHIDRA). |
| `0x6C6500` | `rva_ai_produce_force_block_status_ctor` | ProduceForceBlockStatus::ctor (DISCOVERED-GHIDRA). |
| `0x6C7970` | `rva_ai_goal_system_ctor` | AIGoalSystemClass::ctor (DISCOVERED-GHIDRA). |
| `0x6CB700` | `rva_base_combatant_ctor` | BaseCombatantClass::ctor (DISCOVERED-GHIDRA). |
| `0x6CBA40` | `rva_squadron_combatant_ctor` | SquadronCombatantClass::ctor (DISCOVERED-GHIDRA). |
| `0x6CBEE0` | `rva_company_combatant_ctor` | CompanyCombatantClass::ctor (DISCOVERED-GHIDRA). |
| `0x6EE840` | `rva_ability_absorb_blaster` | AbsorbBlasterAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x6EEAE0` | `rva_ability_arc_sweep` | ArcSweepAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x6F22F0` | `rva_ability_cable_attack` | CableAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x6F42D0` | `rva_ability_combat_bonus` | CombatBonusAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x6F52C0` | `rva_ability_concentrate_fire` | ConcentrateFireAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x6F7170` | `rva_ability_earthquake` | EarthquakeAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x6F7A30` | `rva_ability_eat_attack` | EatAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x6F80D0` | `rva_ability_periodic_damage` | PeriodicDamage (DoT) ability ctor (DISCOVERED-GHIDRA). |
| `0x6F9980` | `rva_ability_energy_weapon` | EnergyWeaponAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x6FFFF0` | `rva_ability_generic_attack` | GenericAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x7048E0` | `rva_ability_ion_cannon_shot` | IonCannonShotAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x706280` | `rva_ability_lucky_shot` | LuckyShotAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x706B10` | `rva_ability_maximum_firepower` | MaximumFirepowerAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x70A280` | `rva_ability_reduce_production_price` | ReduceProductionPriceAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x70B040` | `rva_ability_reduce_production_time` | ReduceProductionTimeAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x70EB50` | `rva_ability_starbase_upgrade` | StarbaseUpgradeAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x710080` | `rva_ability_tractor_beam` | TractorBeamAttackAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x712D20` | `rva_ability_berserker` | BerserkerAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x717530` | `rva_ability_leech_shields` | LeechShieldsAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x71B560` | `rva_ability_drain_life` | DrainLifeAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x71C820` | `rva_ability_shield_flare` | ShieldFlareAbilityClass ctor (DISCOVERED-GHIDRA). |
| `0x769C58` | `rva_operator_new` | operator new -- CONFIRMED-FOCAPI Phase 2 RE + FoCAPI match. |

## lua_api (40)

| RVA | fact_id | claim |
|---|---|---|
| `0x7B8890` | `api_lua_close` | lua_close Lua 5.0.2 C API entry. Corrected RVA from previously ESTIMATED 0x7B8A70. |
| `0x7B8930` | `api_lua_open` | lua_open Lua 5.0.2 C API entry -- MinHook hooked, 400+ states captured at runtime. |
| `0x7B8BC0` | `api_lua_checkstack` | lua_checkstack Lua 5.0.2 C API entry -- Ghidra shows (top-base>>4)+extra > 0x800 check. |
| `0x7B8C40` | `api_lua_concat` | lua_concat Lua 5.0.2 C API entry -- Ghidra: if n>1 calls concat, if n==0 pushes empty. |
| `0x7B8CD0` | `api_lua_cpcall` | lua_cpcall Lua 5.0.2 C API entry -- Ghidra: checks top-1 is function, calls luaD_pcall. |
| `0x7B8D80` | `api_lua_error` | lua_error Lua 5.0.2 C API entry -- Ghidra: calls luaG_errormsg. |
| `0x7B8D90` | `api_lua_getfenv` | lua_getfenv Lua 5.0.2 C API entry -- Ghidra: checks tag==6+C, reads env from closure+0x20. |
| `0x7B8DF0` | `api_lua_getgccount` | lua_getgccount Lua 5.0.2 C API entry -- Ghidra: returns global_State+0x74 >> 10. |
| `0x7B8E00` | `api_lua_getgcthreshold` | lua_getgcthreshold Lua 5.0.2 C API entry -- Ghidra: returns global_State+0x70 >> 10. |
| `0x7B8E10` | `api_lua_getmetatable` | lua_getmetatable Lua 5.0.2 C API entry -- Ghidra: checks tag==5\|\|7, reads metatable at gc+0x10. Was previously mislabeled lua_gettable at this RVA. |
| `0x7B8E90` | `api_lua_gettable` | lua_gettable Lua 5.0.2 C API entry -- Ghidra: resolves table, calls luaV_gettable. |
| `0x7B8EF0` | `api_lua_gettop` | lua_gettop Lua 5.0.2 C API entry -- bytes 48 8B 41 10 48 2B 41 18 48 C1 F8 04 C3. |
| `0x7B8F00` | `api_lua_insert` | lua_insert Lua 5.0.2 C API entry -- Ghidra: shifts values above idx, copies top. |
| `0x7B8F60` | `api_lua_iscfunction` | lua_iscfunction Lua 5.0.2 C API entry -- Ghidra: checks tag==6 && gc+0x0a != 0. |
| `0x7B8FB0` | `api_lua_isnumber` | lua_isnumber Lua 5.0.2 C API entry -- Ghidra: checks tag==3 or calls tonumber. |
| `0x7B9010` | `api_lua_isstring` | lua_isstring Lua 5.0.2 C API entry -- Ghidra: checks tag-3 < 2. |
| `0x7B9060` | `api_lua_equal` | lua_equal Lua 5.0.2 C API entry -- Ghidra: calls luaV_equalobj. |
| `0x7B90F0` | `api_lua_load` | lua_load Lua 5.0.2 C API entry -- Ghidra: calls parser/compiler chain. |
| `0x7B9140` | `api_lua_newtable` | lua_newtable Lua 5.0.2 C API entry -- FoCAPI RVA match + DLL self-test PASSED. |
| `0x7B9190` | `api_lua_newthread` | lua_newthread Lua 5.0.2 C API entry -- Ghidra: creates thread, pushes type=8. |
| `0x7B91D0` | `api_lua_newuserdata` | lua_newuserdata Lua 5.0.2 C API entry -- Ghidra: allocates Udata, returns gc+0x20. |
| `0x7B9220` | `api_lua_next` | lua_next Lua 5.0.2 C API entry -- Ghidra: hash traversal on table. |
| `0x7B9280` | `api_lua_pcall` | lua_pcall Lua 5.0.2 C API entry -- Ghidra: 4 params, calls luaD_pcall at 0x7BBBE0. |
| `0x7B9320` | `api_lua_pushboolean` | lua_pushboolean Lua 5.0.2 C API entry -- byte pattern writes type=1 based on EDX. |
| `0x7B9340` | `api_lua_pushcclosure` | lua_pushcclosure Lua 5.0.2 C API entry -- FoCAPI match + DLL registration works. |
| `0x7B9480` | `api_lua_pushlightuserdata` | lua_pushlightuserdata Lua 5.0.2 C API entry -- byte pattern writes type=2 from RDX. |
| `0x7B94A0` | `api_lua_pushlstring` | lua_pushlstring Lua 5.0.2 C API entry -- Ghidra: pushes type=4, calls string intern. Was previously mislabeled lua_pushstring at this RVA. |
| `0x7B9510` | `api_lua_pushnil` | lua_pushnil Lua 5.0.2 C API entry -- byte pattern writes type=0. |
| `0x7B9520` | `api_lua_pushnumber` | lua_pushnumber Lua 5.0.2 C API entry -- byte-verified movsd [rax+8],xmm1 + type=3. |
| `0x7B9540` | `api_lua_pushstring` | lua_pushstring Lua 5.0.2 C API entry -- FoCAPI RVA + DLL self-test PASSED. |
| `0x7B9600` | `api_lua_pushvalue` | lua_pushvalue Lua 5.0.2 C API entry -- Ghidra: copies TValue at idx to top. |
| `0x7B9640` | `api_lua_pushfstring` | lua_pushfstring Lua 5.0.2 C API entry -- Ghidra: wraps luaO_pushfstring. |
| `0x7B9690` | `api_lua_lessthan` | lua_lessthan Lua 5.0.2 C API entry -- Ghidra: calls luaV_lessthan. |
| `0x7B9820` | `api_lua_rawseti` | lua_rawseti Lua 5.0.2 C API entry -- FoCAPI RVA + DLL self-test PASSED. |
| `0x7B99D0` | `api_lua_settop` | lua_settop Lua 5.0.2 C API entry -- DLL self-test PASSED (pop operations). |
| `0x7B9A60` | `api_lua_settable` | lua_settable Lua 5.0.2 C API entry -- FoCAPI RVA + DLL registration PASSED. |
| `0x7B9B10` | `api_lua_tocfunction` | lua_tocfunction at 0x7B9B10 -- IDA decompile session 2026-04-07: returns *(value_ptr+16) which is closure->c.f (the C function pointer at offset 16 of a closure). The conditional sub_1407C83C0 call... |
| `0x7B9BC0` | `api_lua_tonumber` | lua_tonumber Lua 5.0.2 C API entry -- DLL self-test returned 12345.0 correctly. |
| `0x7B9CC0` | `api_lua_tostring` | lua_tostring at 0x7B9CC0 -- IDA decompile session 2026-04-07: two paths (string fast-path returning value_ptr+24 chars, conversion path via sub_1407C83C0 = luaV_tostring), then GC threshold check (... |
| `0x7B9E00` | `api_lua_type` | lua_type Lua 5.0.2 C API entry -- DLL self-test returned 4 for string, 3 for number. |

## lua_binding (41)

| RVA | fact_id | claim |
|---|---|---|
| `0x540B20` | `rva_find_all_objects_of_type_wrapper` | Find_All_Objects_Of_Type Lua wrapper -- size 0x558, registered via sub_140546C70 at 0x140547B4E. First check rejects zero-arg (nil) calls. |
| `0x547B0C` | `rva_lua_binding_suspend_ai` | Suspend_AI Lua function binding -- registered via sub_140546C70 at 0x140547B0C with C++ wrapper class LuaSuspendAI (vtable derived from LuaMemberFunctionWrapper<LuaUserVar>). The registration call ... |
| `0x57D550` | `rva_make_invulnerable_lua_wrapper` | GameObjectWrapper::Make_Invulnerable Lua binding -- full Lua wrapper with tactical-mode gate, creates/removes INVULNERABLE behavior, iterates hardpoints via QI(22). Size 0x316. Per-object method, N... |
| `0x580010` | `rva_set_cannot_be_killed_wrapper` | Set_Cannot_Be_Killed Lua wrapper -- size 0x164. Writes bit 7 (0x80) of byte at obj+929 (0x3A1) and propagates to group members via vtable QI(22). |
| `0x5819E0` | `rva_teleport_lua_wrapper` | GameObjectWrapper::Teleport Lua binding â€” discovered by IDA Pro decompile on 2026-04-07. Explicit class::method strings in decompile. Takes 1 parameter (target position-producing object). Uses su... |
| `0x6019F0` | `rva_lua_player_wrapper_create` | PlayerWrapper::Create -- CONFIRMED-FOCAPI (community reference match). |
| `0x601AA0` | `rva_lua_disable_bombing_run` | Player Lua wrapper Disable_Bombing_Run (DISCOVERED-GHIDRA). |
| `0x601BE0` | `rva_lua_disable_orbital_bombardment` | Player Lua wrapper Disable_Orbital_Bombardment (DISCOVERED-GHIDRA). |
| `0x601E80` | `rva_lua_enable_advisor_hints` | Player Lua wrapper Enable_Advisor_Hints (DISCOVERED-GHIDRA). |
| `0x602060` | `rva_lua_get_name_ai_type` | Player Lua wrapper Get_Name (AI type) (DISCOVERED-GHIDRA). |
| `0x602640` | `rva_lua_enable_as_actor_wrapper` | Player Lua wrapper Enable_As_Actor -- delegates to engine 0x4B0250. |
| `0x602690` | `rva_lua_get_space_station_level` | Player Lua wrapper Get_Space_Station_Level (DISCOVERED-GHIDRA). |
| `0x6027F0` | `rva_lua_get_credits` | Player Lua wrapper Get_Credits (DISCOVERED-GHIDRA). |
| `0x602A00` | `rva_lua_get_faction_name` | Player Lua wrapper Get_Faction_Name (DISCOVERED-GHIDRA). |
| `0x602AE0` | `rva_lua_get_difficulty` | Player Lua wrapper Get_Difficulty (DISCOVERED-GHIDRA). |
| `0x602C40` | `rva_lua_get_id` | Player Lua wrapper Get_ID (DISCOVERED-GHIDRA). |
| `0x602EE0` | `rva_lua_get_team_id` | Player Lua wrapper Get_Team_ID (DISCOVERED-GHIDRA). |
| `0x603040` | `rva_lua_get_tech_level` | Player Lua wrapper Get_Tech_Level (DISCOVERED-GHIDRA). |
| `0x603130` | `rva_lua_give_money` | Player Lua wrapper Give_Money -- delegates to AddCredits 0x27F370. |
| `0x603560` | `rva_lua_is_ally_wrapper` | Player Lua wrapper Is_Ally -- delegates to engine 0x2823E0. |
| `0x603760` | `rva_lua_is_enemy_wrapper` | Player Lua wrapper Is_Enemy -- delegates to engine 0x2824F0. |
| `0x603960` | `rva_lua_is_local_player` | Player Lua wrapper Is_Local_Player (DISCOVERED-GHIDRA). |
| `0x603A40` | `rva_lua_is_human` | Player Lua wrapper Is_Human (DISCOVERED-GHIDRA). |
| `0x603B20` | `rva_lua_lock_tech_wrapper` | Player Lua wrapper Lock_Tech -- delegates to engine 0x286150. |
| `0x603C70` | `rva_lua_release_credits_tactical_wrapper` | Player Lua wrapper Release_Credits_For_Tactical -- delegates to 0x4B06D0. |
| `0x603DE0` | `rva_lua_retreat_wrapper` | Player Lua wrapper Retreat -- delegates to engine 0x340920. |
| `0x603F60` | `rva_lua_select_object_wrapper` | Player Lua wrapper Select_Object -- delegates to engine 0x2BD2F0. |
| `0x604300` | `rva_lua_set_black_market_tutorial` | Player Lua wrapper Set_Black_Market_Tutorial (DISCOVERED-GHIDRA). |
| `0x6043C0` | `rva_lua_set_sabotage_tutorial` | Player Lua wrapper Set_Sabotage_Tutorial (DISCOVERED-GHIDRA). |
| `0x604480` | `rva_lua_set_tech_level_wrapper` | Player Lua wrapper Set_Tech_Level -- delegates to engine 0x288980. |
| `0x604540` | `rva_lua_unlock_tech_wrapper` | Player Lua wrapper Unlock_Tech -- delegates to engine 0x286100. |
| `0x6046A0` | `rva_lua_make_ally_wrapper` | Player Lua wrapper Make_Ally -- delegates to engine 0x288800. |
| `0x604780` | `rva_lua_make_enemy_wrapper` | Player Lua wrapper Make_Enemy -- delegates to engine 0x288800. |
| `0x6A4820` | `rva_find_player_wrapper` | Find_Player Lua wrapper -- size 0x159. Accepts string only; 'local' (case-insensitive) returns local player, anything else is faction lookup via qword_140B310B8. |
| `0x6A51B0` | `rva_fow_reveal_command_class_ctor` | LuaFOWRevealCommandClass constructor -- size 0x16C. Registers Reveal, Reveal_All, Disable_Rendering, Temporary_Reveal. |
| `0x6A53B0` | `rva_fow_disable_rendering_impl` | FogOfWar.Disable_Rendering Lua method. |
| `0x6A5700` | `rva_fow_reveal_impl` | FogOfWar.Reveal Lua method -- (player, object [, radius]). |
| `0x6A5B00` | `rva_fow_reveal_all_impl` | FogOfWar.Reveal_All Lua method -- size 0xE9. Tactical-only, 1 arg (player), extracts player index from +76, calls sub_14035D4F0. |
| `0x6A5CF0` | `rva_fow_temporary_reveal_impl` | FogOfWar.Temporary_Reveal Lua method. |
| `0x724120` | `rva_story_plot_wrapper_lua_ctor` | StoryPlotWrapper::LuaConstructor -- 5 methods. |
| `0x73DC80` | `rva_story_event_wrapper_lua_ctor` | StoryEventWrapper::LuaConstructor -- 7 methods. |

## memory_layout (14)

| RVA | fact_id | claim |
|---|---|---|
| `0xA12550` | `fact_global_screen_aspect_ratio` | screen_aspect_ratio float32 at RVA 0xA12550. |
| `0xA14D20` | `fact_global_crc32_table` | CRC32 lookup table (uint32[256]) at RVA 0xA14D20. |
| `0xA16FD0` | `fact_global_player_list_class_ptr` | PlayerListClass* singleton at RVA 0xA16FD0 (Ghidra convention). |
| `0xA16FF0` | `fact_global_player_array` | PlayerArray pointer at RVA 0xA16FF0. |
| `0xA16FF8` | `fact_global_player_count` | PlayerCount int32 at RVA 0xA16FF8. CONFIRMED-RUNTIME. |
| `0xA573D0` | `fact_global_fow_data` | FOW_data pointer at RVA 0xA573D0. CONFIRMED-AOB. |
| `0xA6EEE4` | `fact_global_d3d_view_matrix` | Global D3D view matrix (D3DXMATRIX, 16 floats / 64 bytes, row-major) at RVA 0xA6EEE4. Rebuilt every camera update by the matrix-build routine at RVA 0x17F1D0 -- rotation/translation elements copied... |
| `0xA6EF24` | `fact_global_d3d_projection_matrix` | Global D3D projection matrix (D3DXMATRIX, 16 floats / 64 bytes) at RVA 0xA6EF24. Rebuilt every camera update by the matrix-build routine at RVA 0x17F1D0: D3DXMatrixPerspectiveFovRH(&0xA6EF24,...) w... |
| `0xA6F49C` | `fact_global_d3d_view_projection_matrix` | Global D3D view*projection matrix (D3DXMATRIX, 64 bytes) at RVA 0xA6F49C. Lazily recomputed by accessor sub_14017F810 via D3DXMatrixMultiply(&0xA6F49C, &0xA6EEE4 [view], &0xA6EF24 [projection]) whe... |
| `0xA7BC58` | `fact_global_the_game_text` | TheGameText pointer at RVA 0xA7BC58. CONFIRMED-FOCAPI. |
| `0xB153E0` | `fact_global_game_mode_manager` | GameModeManagerClass* pointer at RVA 0xB153E0. |
| `0xB15418` | `fact_global_active_game_mode` | Active game mode pointer at RVA 0xB15418 -- current tactical session. |
| `0xB169F0` | `fact_global_default_hero_respawn_time` | Default_Hero_Respawn_Time float at RVA 0xB169F0. |
| `0xB27F60` | `fact_global_the_command_bar` | TheCommandBar pointer at RVA 0xB27F60. CONFIRMED-FOCAPI. |

## rtti_class (10)

| RVA | fact_id | claim |
|---|---|---|
| `0x7FFA38` | `rva_switch_sides_command_vtable` | SwitchSidesCommand::`vftable' at RVA 0x7FFA38. The vtable has 5 entries: [0] destructor 0x1F760, [1] D1 destructor 0x1FA90, [2] D0 destructor 0x1FA90 (same as D1 per MSVC convention), [3] execute()... |
| `0x8661B8` | `rva_vtable_game_object_class` | GameObjectClass primary vtable at RVA 0x8661B8. CONFIRMED-RUNTIME. |
| `0x8661D8` | `rva_vtable_multi_linked_list_member` | MultiLinkedListMember vtable (in GOC) at RVA 0x8661D8. |
| `0x866200` | `rva_vtable_cull_object_class` | CullObjectClass vtable (in GOC) at RVA 0x866200. |
| `0x866210` | `rva_vtable_signal_generator_class` | SignalGeneratorClass vtable (in GOC) at RVA 0x866210. |
| `0x866228` | `rva_vtable_base5` | Unknown base 5 vtable (in GOC) at RVA 0x866228. |
| `0x8762C0` | `rva_vtable_galactic_mode_class` | GalacticModeClass vtable at RVA 0x8762C0. |
| `0x878D58` | `rva_vtable_projectile_behavior_class` | ProjectileBehaviorClass vtable at RVA 0x878D58. |
| `0x899458` | `rva_vtable_shield_behavior_class_rear` | ShieldBehaviorClass (rear) vtable at RVA 0x899458. |
| `0x8BF6C0` | `rva_vtable_base_combatant_class` | BaseCombatantClass vtable at RVA 0x8BF6C0. |
