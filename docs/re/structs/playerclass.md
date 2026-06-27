# PlayerClass

**RTTI**: `.?AVPlayerClass@@`

**Inherits**: `RefCountClass`, `SignalGeneratorClass`

**Size**: 1232 (0x4D0) bytes


## Notes

- Actual RTTI name is PlayerClass (not PlayerObject)
- AIPlayerClass at +0x360 is a separate heap object, NOT inlined
- PlayerWrapper Lua binding stores pointer at wrapper+0x28
- 15+ DynamicVectorClass instances inlined


## Fields

| Offset | Type | Name | Status / Confidence | Notes |
|--------|------|------|---------------------|-------|
| `0x00` | `pointer` | vtable_ptr | CONFIRMED |  |
| `0x08` | `struct_inline` | refcount_data | DISCOVERED |  |
| `0x10` | `struct_inline` | signal_generator_base | DISCOVERED |  |
| `0x37` | `uint8` | playable | CONFIRMED |  |
| `0x38` | `unknown` | padding_38 | DISCOVERED |  |
| `0x48` | `int32` | slot_index | CONFIRMED |  |
| `0x4C` | `int32` | player_id | CONFIRMED |  |
| `0x54` | `int32` | team_id | DISCOVERED |  |
| `0x58` | `int32` | ai_player_type_id | DISCOVERED |  |
| `0x62` | `uint8` | local_player | CONFIRMED |  |
| `0x68` | `pointer` | faction_ref | CONFIRMED |  |
| `0x70` | `float32` | credits | CONFIRMED |  |
| `0x74` | `float32` | max_credits | CONFIRMED |  |
| `0x84` | `int32` | tech_level | CONFIRMED |  |
| `0x88` | `int32` | max_tech_level | CONFIRMED |  |
| `0x8C` | `DynamicVectorClass<int>` | dvec_int_array_0 | DISCOVERED |  |
| `0xE0` | `DynamicVectorClass<PlayerClass::HistoricallyBuiltObjectType>` | historically_built_types | CONFIRMED |  |
| `0xF8` | `DynamicVectorClass<GameObjectTypeClass const*>` | buildable_types_1 | DISCOVERED |  |
| `0x108` | `uint8` | is_human_controlled | CONFIRMED |  |
| `0x110` | `DynamicVectorClass<GameObjectTypeClass const*>` | buildable_types_2 | DISCOVERED |  |
| `0x128` | `DynamicVectorClass<GameObjectTypeClass const*>` | buildable_types_3 | DISCOVERED |  |
| `0x140` | `struct_unknown` | player_data_block_1 | DISCOVERED |  |
| `0x180` | `struct_unknown` | player_data_block_2 | DISCOVERED |  |
| `0x1A8` | `DynamicVectorClass_header` | unlockable_types_dvec_header | DISCOVERED |  |
| `0x1B0` | `pointer` | unlocked_types_array | CONFIRMED |  |
| `0x1B8` | `int32` | unlocked_types_count | CONFIRMED |  |
| `0x1BC` | `int32` | unlocked_types_capacity | CONFIRMED |  |
| `0x1C0` | `DynamicVectorClass<GameObjectTypeClass const*>` | locked_types_dvec | CONFIRMED |  |
| `0x1C8` | `pointer` | locked_types_array | CONFIRMED |  |
| `0x1D0` | `int32` | locked_types_count | CONFIRMED |  |
| `0x1D4` | `int32` | locked_types_capacity | CONFIRMED |  |
| `0x1E0` | `DynamicVectorClass<GameObjectTypeClass const*>` | type_list_4 | DISCOVERED |  |
| `0x1F8` | `DynamicVectorClass<GameObjectTypeClass const*>` | type_list_5 | DISCOVERED |  |
| `0x210` | `DynamicVectorClass<GameObjectTypeClass const*>` | type_list_6 | DISCOVERED |  |
| `0x228` | `DynamicVectorClass<GameObjectTypeClass const*>` | type_list_7 | DISCOVERED |  |
| `0x250` | `DynamicVectorClass<GameObjectTypeClass const*>` | type_list_8_vtable | DISCOVERED |  |
| `0x258` | `pointer` | type_list_8_data | DISCOVERED |  |
| `0x268` | `DynamicVectorClass<GameObjectTypeClass const*>` | type_list_9_vtable | DISCOVERED |  |
| `0x270` | `pointer` | type_list_9_data | DISCOVERED |  |
| `0x280` | `DynamicVectorClass<int>` | int_vector | DISCOVERED |  |
| `0x298` | `DynamicVectorClass<GameObjectTypeClass const*>` | type_list_10 | DISCOVERED |  |
| `0x2B0` | `pointer` | sub_object_ptr | DISCOVERED |  |
| `0x2C0` | `pointer` | heap_buffer_1 | DISCOVERED |  |
| `0x2C8` | `struct_unknown` | sub_struct_2c8 | DISCOVERED |  |
| `0x2F0` | `pointer` | large_sub_object | DISCOVERED |  |
| `0x318` | `struct_unknown` | data_block_318 | DISCOVERED |  |
| `0x360` | `pointer (AIPlayerClass*)` | ai_player_ptr | CONFIRMED |  |
| `0x370` | `pointer (int32[])` | diplomacy_table | CONFIRMED |  |
| `0x378` | `pointer` | profile_data_ptr | DISCOVERED |  |
| `0x380` | `DynamicVectorClass<BlackMarketItemClass const*>` | black_market_items | CONFIRMED |  |
| `0x398` | `int32` | difficulty_level | DISCOVERED |  |
| `0x3F8` | `uint8[]` | advisor_hints_base | CONFIRMED |  |
| `0x448` | `uint8` | black_market_tutorial_flag | CONFIRMED |  |
| `0x449` | `uint8` | sabotage_tutorial_flag | CONFIRMED |  |
| `0x484` | `int32` | space_station_level | DISCOVERED |  |

## Lua Methods

| Method | Wrapper RVA | Engine RVA |
|--------|-------------|------------|
| `Give_Money` | `0x603130` | `0x27F370` |
| `Get_Faction_Name` | `0x602A00` | `inline` |
| `Get_Credits` | `0x6027F0` | `inline` |
| `Get_ID` | `0x602C40` | `inline` |
| `Get_Tech_Level` | `0x603040` | `inline` |
| `Set_Tech_Level` | `0x604480` | `0x288980` |
| `Unlock_Tech` | `0x604540` | `0x286100` |
| `Lock_Tech` | `0x603B20` | `0x286150` |
| `Is_Enemy` | `0x603760` | `0x2824F0` |
| `Is_Ally` | `0x603560` | `0x2823E0` |
| `Is_Human` | `0x603A40` | `inline` |
| `Is_Local_Player` | `0x603960` | `inline` |
| `Make_Ally` | `0x6046A0` | `0x288800` |
| `Make_Enemy` | `0x604780` | `0x288800` |
| `Enable_As_Actor` | `0x602640` | `0x4B0250` |
| `Select_Object` | `0x603F60` | `0x2BD2F0` |
| `Retreat` | `0x603DE0` | `0x340920` |
| `Release_Credits_For_Tactical` | `0x603C70` | `0x4B06D0` |
