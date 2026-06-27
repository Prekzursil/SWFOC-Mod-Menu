# GameObjectClass

**RTTI**: `.?AVGameObjectClass@@`

**VTable RVA**: `0x8661B8`

**Inherits**: `RootClass`, `MultiLinkedListMember`, `PooledObjectClass`, `CullObjectClass`, `SignalGeneratorClass`

**Size**: 0x3C8 bytes


## Fields

| Offset | Type | Name | Status / Confidence | Notes |
|--------|------|------|---------------------|-------|
| `0x00` | `pointer` | vtable_ptr | high | Pointer to the primary virtual function table. RVA 0x8661B8 identifies GameObjectClass. Written first in both constructors. |
| `0x08` | `pointer` | vtable_MultiLinkedListMember | high | Secondary vtable for MultiLinkedListMember base class. |
| `0x10` | `uint64` | linked_list_next | medium | MultiLinkedListMember next pointer. Initialized to 0 in constructor. |
| `0x18` | `pointer` | vtable_CullObjectClass | high | Secondary vtable for CullObjectClass base class. |
| `0x20` | `pointer` | vtable_SignalGeneratorClass | high | Secondary vtable for SignalGeneratorClass base class. |
| `0x28` | `undefined` | signal_generator_data | medium | SignalGeneratorClass internal data. Initialized by SignalGeneratorClass::SignalGeneratorClass(). |
| `0x30` | `uint64` | signal_data_2 | medium | Additional signal/cull object data. Initialized to 0. |
| `0x38` | `pointer` | vtable_5 | high | Fifth vtable pointer (for unknown base class at offset 56 in RTTI). Present in additional_vtables array. |
| `0x40` | `uint32` | visibility_mask | high | Cull/visibility bitmask. Initialized to 0x3FFFFF (all bits set). Used by the rendering/culling system to determine which cameras/viewports can see this object. |
| `0x44` | `uint32` | visibility_mask_2 | high | Second visibility mask. Also initialized to 0x3FFFFF. |
| `0x48` | `int32` | unique_session_id | high | Session-unique allocation ID. Initialized to -1 (0xFFFFFFFF) in default constructor; set to param_3 in the full constructor. Used by FUN_1402AC980() for session tracking. |
| `0x4C` | `int32` | session_id_high | medium | Upper 32 bits / secondary ID. Initialized to 0. |
| `0x50` | `pointer` | signal_listener_list | high | Linked list of signal listeners. Initialized to NULL. Iterated in FUN_1403AC530 (transform update) to notify observers. |
| `0x58` | `int32` | owner_player_id | high | Index into the global PlayerListClass array identifying the owning player. SESSION-DEPENDENT. For sub-objects, use QueryInterface(3) to resolve the true owner via parent chain. Read by SetHP, Change_Owner, ScheduleHeroRespawn, and many others. |
| `0x5C` | `float32` | hp | high | Current hitpoints. Written exclusively by SetHP (RVA 0x3A89D0). Clamped to [0.0, max_hp]. When HP reaches 0.0, the engine logs a death event. All known callers funnel through SetHP. Also set during spawn init in FUN_1403989A0 via GetMaxHealth. |
| `0x60` | `uint64` | unknown_0x60 | medium | Initialized to 0 in constructor. Referenced in Change_Owner via *(param_1 + 0x60) as the GameObjectClass pointer of the wrapper's underlying object. |
| `0x68` | `float32` | spawn_position_x | high | Initial spawn X coordinate. Set from param_4 in the full constructor. Used as the base position during object initialization. |
| `0x6C` | `float32` | spawn_position_y | high | Initial spawn Y coordinate (vertical/height). Set from param_4[1] in the full constructor. |
| `0x70` | `float32` | spawn_position_z | high | Initial spawn Z coordinate (depth). Set from param_4[2] in the full constructor. May have height offset (DFC) added from [GameObjectType](../structs/gameobjecttype.md). |
| `0x74` | `float32` | facing_x | high | Initial facing direction X component. Set from param_5[0] in the full constructor. |
| `0x78` | `float32` | current_position_x | high | Current world position X. Written by the Make_Invulnerable/Teleport path. Read by FUN_1403A8710 alongside +0x7C and +0x80. Also set from spawn_position in constructor. |
| `0x7C` | `float32` | current_position_y | high | Current world position Y (height). Written by Make_Invulnerable/Teleport path. The 'y' in the engine coordinate system. |
| `0x80` | `float32` | current_position_z | high | Current world position Z (depth). Written by Make_Invulnerable/Teleport path. |
| `0x84` | `float32` | render_position_x | high | Rendering/interpolated position X. Updated by FUN_1403A8710 which compares with param and sets dirty flag at +0x3A1. Distinct from current_position for interpolation. |
| `0x88` | `float32` | render_position_y | high | Rendering/interpolated position Y. |
| `0x8C` | `float32` | render_position_z | high | Rendering/interpolated position Z. Also used as the facing/rotation Z in FUN_1403AC530 transform update. |
| `0x90` | `float32` | previous_position_x | high | Previous frame position X. Updated by FUN_1403A8710 alongside render_position. |
| `0x94` | `float32` | previous_position_y | high | Previous frame position Y. Used by FUN_1403AC530 for rotation calculation. |
| `0x98` | `float32` | previous_position_z | high | Previous frame position Z. |
| `0xA8` | `pointer` | locomotor_component_ptr | high | Pointer to the locomotor/movement behavior component. When non-null, contains speed override fields: flag at locomotor+0x29C (byte), value at locomotor+0x2A0 (float32). SetSpeedOverride writes both; ClearSpeedOverride zeros both. |
| `0xB0` | `pointer` | component_slot_0xB0 | medium | Component pointer slot. Part of the component pointer block from 0xA8 to 0x240. Cleared during reset in FUN_1403989A0. |
| `0xB8` | `pointer` | parent_container_component_ptr | high | Pointer to parent container component. FUN_1403956C0 returns (obj+0xB8)+0x68 as the player/faction identity when the object has a parent. Used for resolving ownership of sub-objects. Also read in FUN_140395920 for sub-object owner resolution. |
| `0xC0` | `pointer` | component_slot_0xC0 | medium | Component pointer slot. Part of the component pointer block. Cleared during reset. |
| `0xC8` | `pointer` | component_slot_0xC8 | medium | Component pointer slot. |
| `0xD0` | `pointer` | component_slot_0xD0 | medium | Component pointer slot. |
| `0xD8` | `pointer` | targeting_data_ptr | medium | Pointer to targeting/AI data structure. FUN_140395920 reads *(obj+0xD8)+0x18 as a linked list for target priority evaluation. |
| `0xE0` | `pointer` | component_slot_0xE0 | medium | Component pointer slot. |
| `0xE8` | `pointer` | component_slot_0xE8 | medium | Component pointer slot. |
| `0xF0` | `pointer` | buff_modifier_component_ptr | medium | Pointer to combat buff/modifier component. FUN_140395C70 reads *(obj+0xF0)+0x138 for buff data. Used in health modifier calculations. |
| `0xF8` | `pointer` | game_object_type_wrapper_ptr | medium | Pointer to the [GameObjectType](../structs/gameobjecttype.md) struct wrapper for this instance. Contains the type definition, refcounted. Written from param_2 in the full constructor. |
| `0x100` | `pointer` | combatant_behavior_ptr | high | Pointer to CombatantBehaviorClass. Allocated (size 0x3B8) and initialized in FUN_1403989A0 when FUN_140374DA0 returns true. Contains combat-related state including weapon data at +0x2CC and nested structures. |
| `0x108` | `pointer` | component_slot_0x108 | medium | Component pointer slot. |
| `0x110` | `pointer` | component_slot_0x110 | medium | Component pointer slot. |
| `0x118` | `pointer` | health_sub_object_ptr | high | Pointer to a health-related sub-object. Used by the Take_Damage property dispatch path. Contains additional health state fields at deep offsets. |
| `0x120` | `pointer` | component_slot_0x120 | medium | Component pointer slot. |
| `0x128` | `pointer` | component_slot_0x128 | medium | Component pointer slot. |
| `0x130` | `pointer` | component_slot_0x130 | medium | Component pointer slot. |
| `0x138` | `pointer` | component_slot_0x138 | medium | Component pointer slot. |
| `0x140` | `pointer` | component_slot_0x140 | medium | Component pointer slot. |
| `0x148` | `pointer` | component_slot_0x148 | medium | Component pointer slot. |
| `0x150` | `pointer` | component_slot_0x150 | medium | Component pointer slot. |
| `0x158` | `pointer` | component_slot_0x158 | medium | Component pointer slot. |
| `0x160` | `pointer` | component_slot_0x160 | medium | Component pointer slot. Explicitly set to NULL in constructor. |
| `0x168` | `pointer` | component_slot_0x168 | medium | Component pointer slot. Cleared during reset. |
| `0x170` | `pointer` | component_slot_0x170 | medium | Component pointer slot. |
| `0x178` | `pointer` | component_slot_0x178 | medium | Component pointer slot. |
| `0x180` | `pointer` | component_slot_0x180 | medium | Component pointer slot. |
| `0x188` | `pointer` | component_slot_0x188 | medium | Component pointer slot. |
| `0x190` | `pointer` | component_slot_0x190 | medium | Component pointer slot. |
| `0x198` | `pointer` | component_slot_0x198 | medium | Component pointer slot. |
| `0x1A0` | `pointer` | component_slot_0x1A0 | medium | Component pointer slot. |
| `0x1A8` | `pointer` | component_slot_0x1A8 | medium | Component pointer slot. |
| `0x1B0` | `pointer` | component_slot_0x1B0 | medium | Component pointer slot. |
| `0x1B8` | `pointer` | component_slot_0x1B8 | medium | Component pointer slot. |
| `0x1C0` | `pointer` | component_slot_0x1C0 | medium | Component pointer slot. |
| `0x1C8` | `pointer` | component_slot_0x1C8 | medium | Component pointer slot. |
| `0x1D0` | `pointer` | component_slot_0x1D0 | medium | Component pointer slot. |
| `0x1D8` | `pointer` | component_slot_0x1D8 | medium | Component pointer slot. |
| `0x1E0` | `pointer` | component_slot_0x1E0 | medium | Component pointer slot. |
| `0x1E8` | `pointer` | component_slot_0x1E8 | medium | Component pointer slot. |
| `0x1F0` | `pointer` | component_slot_0x1F0 | medium | Component pointer slot. |
| `0x1F8` | `pointer` | component_slot_0x1F8 | medium | Component pointer slot. |
| `0x200` | `pointer` | radar_map_data_ptr | high | Pointer to RadarMapDataPackClass (size 0x20). Allocated in FUN_1403989A0 if NULL. Used for minimap representation of this object. |
| `0x208` | `uint64` | unknown_0x208 | low | Unknown. Cleared during init. |
| `0x210` | `uint64` | unknown_0x210 | low | Unknown. Cleared during init. |
| `0x218` | `pointer` | component_slot_0x218 | medium | Component pointer slot. |
| `0x220` | `pointer` | component_slot_0x220 | medium | Component pointer slot. |
| `0x228` | `pointer` | component_slot_0x228 | medium | Component pointer slot. |
| `0x230` | `pointer` | component_slot_0x230 | medium | Component pointer slot. Explicitly set to NULL. |
| `0x240` | `pointer` | component_slot_0x240 | medium | Component pointer slot. Referenced in FUN_1403A5840 for map node cleanup. Used in FUN_1403A4820 for hero/garrison logic -- contains sub-structure with +0x2A0 map manager ref and +0x328 counter. |
| `0x248` | `float32[3]` | orientation_matrix_row0 | high | First row of 3x3 orientation/rotation matrix. Initialized to identity matrix (1,0,0,0,1,0,0,0,1) in constructor. The RootClass_data offsets 0x1A8-0x1D0 map to raw struct offsets 0x248-0x270. |
| `0x254` | `float32[3]` | orientation_matrix_row1 | high | Second row of orientation matrix. Identity: (0, 1, 0). |
| `0x260` | `float32[3]` | orientation_matrix_row2 | high | Third row of orientation matrix. Identity: (0, 0, 1). |
| `0x268` | `uint8` | death_check_flag | medium | Flag related to death state processing. Mapped from constructor's RootClass_data offset_0x1E8. |
| `0x269` | `uint8` | spawn_initialized | medium | Set to 1 during construction. Indicates the object has been initialized post-spawn. |
| `0x278` | `pointer` | component_array_ptr | high | Pointer to an array of component (behavior) pointers. Each element is 8 bytes. Indexed by values in the component_lookup_table at +0x332. Used by QueryInterface: result = components[obj[0x332 + query_type]]. Components are added via FUN_14038CB30. |
| `0x288` | `int8` | component_count | high | Current number of components in the component_array. Incremented in FUN_14038CB30 when a new component is added. |
| `0x289` | `int8` | component_capacity | high | Capacity of the component_array. When component_count >= component_capacity, the array is grown via FUN_1403A59F0. |
| `0x290` | `int32` | priority_component_count | high | Count of priority/front-inserted components. Incremented in FUN_14038CB30 when a component is inserted at index 0 (priority behavior). Reset to 0 in FUN_1403989A0. |
| `0x298` | `pointer` | game_object_type_ptr | high | Pointer to the GameObjectTypeClass struct that defines this object's type (unit definition). The type name string is at [GameObjectType+0xF8] as an MSVC SSO string. Max health is obtained via FUN_1403727A0 which reads [GameObjectType](../structs/gameobjecttype.md)+0xDCC. Used extensively: SetHP, spawn init, hero respawn, combat, etc. |
| `0x2A0` | `pointer` | scene_node_ptr | high | Pointer to the 3D scene/render node for this object. Contains the visual representation. Has sub-fields: +0x88 (animation state), +0xA4/+0xA8/+0xAC (render position XYZ), +0xB0/+0xB4/+0xB8 (render position copy). Checked extensively -- when non-null, the object has a visible representation. Cleared in FUN_1403989A0. |
| `0x2A8` | `pointer` | ai_label_display_ptr | high | Pointer to the AI label/name display object. Allocated and managed in FUN_1403A4510. Contains a DrawTextClass used for debug/AI overhead labels. Cleared in FUN_1403989A0. |
| `0x2B0` | `pointer` | container_ref | high | Reference to the parent container object. Used by the invulnerability system and FUN_1403963F0 which walks the container chain: while (obj+0x335 != -1) { obj = *(obj+0x2B0) }. When non-null, the object is contained within another object. Cleared in FUN_1403989A0. |
| `0x2B8` | `pointer` | game_session_context_ptr | high | Pointer to the game session/world context. Dereferenced as *(obj+0x2B8)+0x20 to reach the game mode manager which provides vtable calls for GetGameMode, GetPlayerManager, etc. Used by FUN_1403989A0, FUN_14039BCB0, FUN_1403A4510, FUN_1403A4820. |
| `0x2D0` | `pointer` | hardpoint_array_ptr | high | Pointer to DynamicVectorClass<HardPointClass*>. Allocated (size 0x18) and populated in FUN_1403989A0 when combatant_behavior_ptr exists. Each hardpoint is size 0xD8, created by FUN_140381A90. Contains weapon/turret sub-objects. |
| `0x2E0` | `pointer` | map_garrison_node_ptr | medium | Pointer to map/garrison node. Referenced in FUN_1403A5840 for cleanup. When non-null and *(+0x2E0)+0x2A0 is valid, indicates the object is garrisoned on a map structure. Contains a counter at +0x328. |
| `0x2F8` | `pointer` | garrison_map_ref_2 | medium | Secondary garrison/map reference. Cleared alongside map_garrison_node_ptr in FUN_1403A5840. |
| `0x300` | `uint8` | object_flags_0x300 | high | Object state flags byte. Initialized from global DAT_140A28648 in constructor. Bit 6 (0x40) and bit 7 (0x80) are set when param_7 == 1 in the full constructor (0xC0 mask). Checked in FUN_1403A4820 as (obj+0x3A1) bit 3. |
| `0x301` | `uint8` | object_flags_0x301 | medium | Initialized from global DAT_140B2C379. |
| `0x302` | `uint8` | object_flags_0x302 | medium | Initialized from global DAT_140A28649. |
| `0x303` | `uint8` | object_flags_0x303 | medium | Initialized from global DAT_140B2C37A. |
| `0x304` | `uint8` | object_flags_0x304 | medium | Initialized from global DAT_140A2864A. |
| `0x305` | `uint8` | active_flag | medium | Initialized to 1 in constructor. Likely indicates the object is active/alive. |
| `0x307` | `uint8` | state_flag_0x307 | low | Initialized to 0. |
| `0x308` | `uint32` | state_0x308 | low | Initialized to 0. |
| `0x30C` | `uint8` | state_0x30C | low | Initialized to 0xFF. |
| `0x310` | `int32` | state_0x310 | low | Initialized to -1 (0xFFFFFFFF). |
| `0x314` | `uint64` | state_0x314 | low | Initialized to 0. |
| `0x318` | `uint64` | state_0x318 | low | Initialized to 0. |
| `0x320` | `uint64` | state_0x320 | low | Initialized to 0. |
| `0x328` | `uint32` | state_0x328 | low | Initialized to 1. |
| `0x32C` | `uint32` | state_0x32C | low | Initialized to 0. |
| `0x330` | `uint8` | hero_respawn_slot | medium | Used in ScheduleHeroRespawn as the last parameter to FUN_1403FE0A0. Read as (char)(obj+0x330). Likely an index into a respawn slot table. |
| `0x332` | `uint8[0x6A]` | component_lookup_table | high | Byte array indexed by component query type ID (0 through 0x69). Each byte is either an index into the component_array (at +0x278) or 0xFF meaning 'no component of this type'. Used by QueryInterface: result = components[obj[0x332 + query_type]]. Known query types: 0=self, 1=behavior, 3=parent/container, 4=land_locomotor, 5=space_locomotor, 0x16=hardpoint_mgr, 0x19=ability, 0x3D=transform, 0x46=property_handler. Initialized from global template at 0x140866880. Range is 0x332 to 0x39B inclusive (106 bytes). |
| `0x333` | `uint8` | behavior_component_index | high | Shorthand for component_lookup_table[1]. Index into component array for the behavior manager. When != -1, the object has an active behavior system. Checked in FUN_14039BCB0. |
| `0x335` | `uint8` | parent_component_index | high | Shorthand for component_lookup_table[3] (query type 3 = parent/container). 0xFF means this object has no parent (it is a top-level object). Any other value is an index into the component array pointing to the parent component. Used in Change_Owner, FUN_1403963F0, FUN_14039BCB0, FUN_1403AC530. |
| `0x33E` | `uint8` | unknown_component_index_0x33E | medium | Component lookup table entry. Checked in FUN_1403A4820: when != -1 and FUN_1403751F0 returns false, the function returns early. Likely relates to a specific behavior type. |
| `0x348` | `uint8` | hardpoint_manager_index | high | Shorthand for component_lookup_table[0x16]. When 0xFF, the object uses the direct HP path (no hardpoint indirection). When any other value, damage routing goes through the hardpoint manager. Checked in Make_Invulnerable and Take_Damage paths. |
| `0x34B` | `uint8` | ability_manager_index | medium | Shorthand for component_lookup_table[0x19]. When != -1, the object has an ability system. Checked in FUN_1403A4820 to look up ability component and check +0x54 state. |
| `0x36F` | `uint8` | transform_component_index | medium | Shorthand for component_lookup_table[0x3D]. When != -1, the object has a transform component. Checked in Change_Owner for position updates. |
| `0x39E` | `uint8` | registered_with_session_flag | high | Set to 1 when FUN_14029BFE0(session_context, this) is called, indicating the object has been registered with the game session manager. Checked before calling FUN_14029BFE0 to avoid double-registration. Set in FUN_14038CB30, FUN_1403989A0, FUN_1403A4820. |
| `0x39F` | `uint8` | spawn_complete_flag | high | Set to 1 in FUN_1403989A0 during the spawn initialization sequence. When == 1 in FUN_1403A4820, enables visual model attachment and hero clash node setup. When == 0 in FUN_1403A4820, skips animation setup. |
| `0x3A0` | `uint8` | state_flags_bitfield | high | Bitfield of state/dirty flags. Bit 0 (0x01): HP change notification dirty flag (set by SetHP when object_id matches tracked ID). Bit 1 (0x02): used in ScheduleHeroRespawn -- if set, hero respawn is blocked. Bit 3 (0x08): animation/combat state flag (cleared/set in FUN_1403A92F0). Bit 4 (0x10): position lock flag (blocks render position updates in FUN_1403A8710). Bit 6 (0x40): used in ScheduleHeroRespawn -- if set alongside 0x02, respawn is blocked. Bit 7 (0x80): sign bit -- checked in FUN_1403989A0 (if < 0, skips hardpoint allocation). |
| `0x3A1` | `uint8` | dirty_flags_bitfield | high | Dirty/state flags bitfield. Bit 0 (0x01): transform dirty -- set in FUN_1403A8710 when position changes, cleared in FUN_1403AC530 at the end of transform update. Bit 3 (0x08): checked in FUN_1403A4820 for animation state. Bit 7 (0x80): prevent-death flag. When set, Take_Damage_Impl clamps HP to max(1.0, current_hp). |
| `0x3A6` | `uint16` | state_0x3A6 | low | Cleared to 0 in FUN_1403989A0 during spawn init. |
| `0x3A7` | `uint8` | invulnerability_flag | high | Invulnerability state. Checked by Take_Damage_Outer at multiple points. WARNING: Setting this byte manually does NOT work -- you must call Make_Invulnerable_Setter (RVA 0x3ABB80) which propagates to hardpoints and updates the behavior system. Located within the 0x3A6 cleared region. |
| `0x3A8` | `uint32` | state_0x3A8 | low | Cleared to 0 in FUN_1403989A0 during spawn init. |
| `0x3B5` | `uint32` | state_0x3B5 | low | Cleared to 0 in FUN_1403989A0. |
| `0x3B9` | `uint8` | state_0x3B9 | low | Cleared to 0 in FUN_1403989A0. |
| `0x3C0` | `uint64` | state_0x3C0 | medium | Cleared to 0 in FUN_1403989A0. Likely the last field -- places minimum struct size at 0x3C8. |

## QueryInterface Types

| ID | Class |
|----|-------|
| `0x00` | Self |
| `0x01` | BehaviorClass |
| `0x03` | Parent/Container |
| `0x0F` | BaseShieldBehaviorClass (front) |
| `0x10` | ShieldBehaviorClass (rear) |
| `0x16` | HardpointManager |
| `0x19` | AbilityComponent |
| `0x3D` | TransformComponent |
| `0x46` | PropertyHandler |
