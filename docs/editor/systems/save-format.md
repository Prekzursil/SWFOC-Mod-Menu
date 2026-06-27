# Save Format

**File Extension**: .sav

**Encoding**: binary little-endian

**Chunk Format**: Westwood W3D-derived (8-byte header: id+size, bit31=sub-chunks)

**Micro Chunk Header**: 2 bytes (id+size, max 255 data bytes)

**Compression**: zlib linked but usage unconfirmed for saves

**Chunk Writer Ctor**: 0x21FCC0

**Chunk Reader Ctor**: 0x220280


## RE Findings Detail

**Analysis**: SWFOC Save File Format Specification

Reverse-engineered save file format for Star Wars: Empire at War - Forces of Corruption (Alamo Engine, x86_64 Steam build). Derived from static analysis of StarWarsG.exe via Ghidra MCP.

- **Date**: 2026-04-04
- **Analyst**: Agent 3F (Claude)


### File Format

- **extension**: .sav
- **default_directory**: %APPDATA%\Petroglyph\Empire at War - Forces of Corruption\Save\
- **encoding**: binary, little-endian
- **byte_order**: little-endian (x86)
- **overall_structure**: Hierarchical chunk tree (Westwood/Petroglyph chunk format). No top-level file magic or version header -- the file begins directly with the root chunk. zlib compression (compress2/uncompress2) is linked but usage context is TBD.
- **compression**:
  - **library**: zlib (statically linked)
  - **functions**:
    - **compress2**: 0x1407A1470
    - **uncompress2**: 0x1407A1590
    - **deflateInit_**: 0x1407A3080
    - **deflateInit2_**: 0x1407A2DF0
    - **inflateInit_**: 0x1407A54F0
    - **compress_block**: 0x1407A60A0
  - **notes**: zlib is present and may wrap the chunk payload or the entire file. Alamo engine .meg/.alo files use the same chunk format without compression. Save files may be uncompressed chunk trees or have a thin zlib wrapper. A sample file is needed to confirm.
- **checksum**:
  - **method**: none found
  - **notes**: No CRC or checksum functions were identified in the save path. The engine does not appear to verify file integrity on load -- it relies on the chunk structure being well-formed.


### Chunk Format

- **description**: The Alamo engine uses a two-tier chunk system inherited from Westwood Studios W3D format. 'Chunks' are large containers that nest, 'micro-chunks' are leaf data units within a chunk.
- **chunk_header**:
  - **size**: 8
  - **layout**: {'offset': 0, 'type': 'uint32', 'name': 'chunk_id', 'description': 'Identifies the chunk type. Unique within context.'}, {'offset': 4, 'type': 'uint32', 'name': 'chunk_size', 'description': 'Size of the chunk data in bytes (excludes this 8-byte header). Bit 31 (0x80000000) is set if the chunk contains sub-chunks rather than raw data.'}
  - **sub_chunk_flag**: chunk_size & 0x80000000 != 0 means this chunk contains nested sub-chunks. data_size = chunk_size & 0x7FFFFFFF.
  - **nesting**: Chunks nest to arbitrary depth. The ChunkWriter/ChunkReader support up to 256 nesting levels (0x800 byte stack / 8 bytes per level).
  - **rvas**:
    - **ChunkWriterClass_ctor**: 0x14021FCC0
    - **ChunkWriterClass_dtor**: 0x14021FDC0
    - **ChunkWriterClass_Open_Chunk**: 0x14021FE20
    - **ChunkWriterClass_Close_Chunk**: 0x14021FEB0
    - **ChunkWriterClass_Write**: 0x1402200B0
    - **ChunkWriterClass_Write_CString**: 0x140220140
    - **ChunkReaderClass_ctor**: 0x140220280
    - **ChunkReaderClass_dtor**: 0x140220370
    - **ChunkReaderClass_Get_File_Size**: 0x1402203F0
    - **ChunkReaderClass_Tell**: 0x140220460
    - **ChunkReaderClass_Open_Chunk**: 0x1402204A0
    - **ChunkReaderClass_Close_Chunk**: 0x140220520
- **micro_chunk_header**:
  - **size**: 2
  - **layout**: {'offset': 0, 'type': 'uint8', 'name': 'micro_chunk_id', 'description': 'Identifies the micro-chunk type within its parent chunk.'}, {'offset': 1, 'type': 'uint8', 'name': 'micro_chunk_size', 'description': 'Size of the micro-chunk data in bytes (max 255). Excludes this 2-byte header.'}
  - **max_data_size**: 255
  - **notes**: Micro-chunks are leaf-level data containers. They cannot nest. The size byte is patched after all data is written (seek-back pattern).
  - **rvas**:
    - **ChunkWriterClass_Open_Micro_Chunk**: 0x14021FFA0
    - **ChunkWriterClass_Close_Micro_Chunk**: 0x140220030


### String Encoding

- **save_names**:
  - **type**: wchar_t (UTF-16LE)
  - **notes**: Save game names are wide strings. The SaveGameStruct stores names as std::wstring (MSVC SSO, inline threshold 7 wchars / 14 bytes). The [AutoSave] sentinel is a 10-wchar wide string.
- **in_file_strings**:
  - **type**: null-terminated char (ASCII/ANSI)
  - **notes**: ChunkWriterClass::Write_CString writes strlen+1 bytes (includes null terminator). Strings in save data (object type names, faction names, etc.) are single-byte null-terminated.


### Io Layer

- **FileClass**:
  - **description**: Core I/O wrapper around Win32 HANDLE (CreateFileA/CloseHandle). Both ChunkWriter and ChunkReader take a FileClass* (or RAMFileClass*) as their stream parameter.
  - **handle_offset**: 0x08 (HANDLE, initialized to INVALID_HANDLE_VALUE)
  - **filename_offset**: 0x18 (std::string, SSO threshold 15 chars)
  - **open_mode_offset**: 0x00 (int: -1=closed, 0=read, 1=write)
  - **vtable_methods**:
    - **Read**: vtable[0x28]  -- read(buf, size) -> bytes_read
    - **Write**: vtable[0x30] -- write(buf, size) -> bytes_written
    - **Seek**: vtable[0x38]  -- seek(offset, whence) whence: 0=SET, 1=CUR, 2=END
    - **Tell**: vtable[0x40]  -- tell() -> position
  - **rvas**:
    - **FileClass_ctor_default**: 0x140213010
    - **FileClass_ctor_cstring**: 0x1402130D0
    - **FileClass_ctor_stdstring**: 0x1402131E0
    - **FileClass_dtor**: 0x1402132F0
    - **FileClass_Open**: 0x140213600
- **RAMFileClass**:
  - **description**: In-memory file implementation. Used for serialization buffers. Same vtable interface as FileClass.
  - **rvas**:
    - **RAMFileClass_ctor1**: 0x1402227E0
    - **RAMFileClass_ctor2**: 0x140222830
    - **RAMFileClass_ctor3**: 0x140222880
    - **RAMFileClass_dtor**: 0x1402228D0


### Save Load System

- **SaveLoadManagerClass**:
  - **description**: Singleton manager for save/load operations. Maintains a DynamicVector of SaveGameStruct entries representing available save slots.
  - **SaveGameStruct**:
    - **stride**: 0x70 (112 bytes per entry)
    - **fields**: {'offset': '0x00', 'type': 'std::wstring', 'name': 'save_name', 'description': 'Wide string save name (SSO: ptr/inline at +0, length at +0x10, capacity at +0x18)'}, {'offset': '0x10', 'type': 'uint64', 'name': 'name_length', 'description': 'wchar_t count of the save name'}, {'offset': '0x18', 'type': 'uint64', 'name': 'name_capacity', 'description': 'Capacity for SSO string (inline threshold: 7 wchars)'}
    - **sentinel**: [AutoSave] (10 wchars, L"[AutoSave]")
  - **rvas**:
    - **SaveGameStruct_Vector_ctor**: 0x140056360
    - **SaveGameStruct_Vector_dtor**: 0x1400581C0
- **SaveLoadClass**:
  - **description**: Core serialization class. Uses ObjectPointerPairClass to track object-to-ID mappings during save/load for pointer resolution.
  - **ObjectPointerPairClass_Vector**:
    - **global_address**: 0x140A13DB0
    - **layout**:
      - **data_ptr**: offset 0x00
      - **count**: offset 0x08
      - **capacity_flags**: offset 0x0C (bit 31 = heap flag)
  - **rvas**:
    - **ObjectPointerPairClass_Vector_ctors**: 0x1407EC700, 0x1407EC770, 0x1407EC900, 0x1407ECA90
    - **ObjectPointerPairClass_Vector_dtor**: 0x1401FF850
- **SaveGameEventClass**:
  - **description**: Scheduled event that triggers a save. Event type ID: 36 (0x24). Created via EventFactoryClass.
  - **size**: 0x70 bytes
  - **fields**: {'offset': 'EventClass+0x00', 'type': 'int32', 'name': 'save_slot_index', 'description': 'Index into SaveGameStruct vector. 0xFFFFFFFF = new save.'}, {'offset': 'EventClass+0x08', 'type': 'std::wstring', 'name': 'save_name', 'description': 'Display name of the save.'}
  - **event_type_id**: 36
  - **factory_singleton**: 0x140B313E0
  - **execute_handler_rva**: 0x14048FC00
  - **save_dispatch_fptr**: 0x140B313D8 (function pointer to actual save execution)
  - **serialize_rva**: 0x14048FB90
  - **deserialize_rva**: 0x14048FB20
  - **rvas**:
    - **ctor**: 0x14048FA80
    - **dtor**: 0x14048FAD0
    - **execute**: 0x14048FC00
- **ObjectPersistenceClass**:
  - **description**: Manages persistent object units across save/load. tPersistentUnit is the per-object save record.
  - **tPersistentUnit**:
    - **notes**: Contains a DynamicVector and vtable. Each unit tracks an object's persistent state.
  - **rvas**:
    - **tPersistentUnit_Vector_ctor**: 0x1404F2D80
    - **tPersistentUnit_Vector_iterate**: 0x1404F2FF0
    - **tPersistentUnit_Vector_dtor**: 0x140046570


### Persistent Data Classes

- **description**: These DataPack classes contain the per-object state that gets serialized. Each has serialize/deserialize virtual methods that write/read micro-chunks.
- **PlanetaryDataPackClass**:
  - **description**: Per-planet galactic map data. The largest and most important persistent structure for save editing.
  - **dtor_rva**: 0x1404B5FB0
  - **sub_structures**:
    - **PersistentTacticalBuiltObjectStruct**:
      - **description**: Records of structures built on the planet surface in tactical mode.
      - **vector_dtor_rva**: 0x1404B5E60
    - **PersistentUpgradeObjectStruct**:
      - **description**: Records of upgrades applied to structures on this planet.
      - **vector_dtor_rva**: 0x1404B5ED0
    - **LineLinkStruct**:
      - **description**: Hyperspace lane connections between planets.
      - **vector_dtor_rva**: 0x1404B5DF0
    - **TradeRouteLinkEntryClass**:
      - **description**: Trade route connections and state.
      - **vector_dtor_rva**: 0x1404B5F40
  - **contains**: DynamicVector<PersistentTacticalBuiltObjectStruct>, DynamicVector<PersistentUpgradeObjectStruct>, DynamicVector<LineLinkStruct>, DynamicVector<TradeRouteLinkEntryClass>, DynamicVector<GameObjectClass*> (ground units), DynamicVector<GameObjectClass*> (space units), DynamicVector<GameObjectTypeClass*> (type references), DynamicVector<SimpleModifierListClass<tVisibilityLevelType>*> (fog of war), Linked list structures for unit tracking, SSO string for planet name
- **other_datapacks**:
  - **description**: Each of these DataPack classes has serialize/deserialize methods. They represent per-object component state in the save file.
  - **list**: {'name': 'AIDataPackClass', 'notes': 'AI player state (budgets, goals, perception)'}, {'name': 'AbilityCountdownDataPackClass', 'notes': 'Active ability cooldown timers'}, {'name': 'CombatantBehaviorClass', 'notes': 'Combat statistics and targeting'}, {'name': 'TacticalBuildObjectsDataPackClass', 'notes': 'Build queue and construction state'}, {'name': 'TacticalSuperWeaponDataPackClass', 'notes': 'Superweapon cooldown and targeting'}, {'name': 'LocomotorDataPackClass', 'notes': 'Unit movement state and pathfinding'}, {'name': 'ProjectileDataPackClass', 'notes': 'Active projectile state'}, {'name': 'GarrisonDataPackClass', 'notes': 'Garrisoned unit information'}, {'name': 'InfectionDataPackClass', 'notes': 'Corruption/infection spread state'}, {'name': 'DamageTrackingDataPackClass', 'notes': 'Damage history for combat bonuses'}, {'name': 'BaseShieldDataPackClass', 'notes': 'Shield generator state'}, {'name': 'DeathDataPackClass', 'notes': 'Death animation and cleanup state'}, {'name': 'GUIDataPackClass', 'notes': 'UI-related persistent state'}, {'name': 'SelectionDataPackClass', 'notes': 'Selection group persistence'}, {'name': 'HintDataPackClass', 'notes': 'Tutorial/hint completion state'}, {'name': 'TeamDataPackClass', 'notes': 'Team alliance and diplomacy state'}


### Serialization Primitives

- **description**: Low-level read/write operations used by serialize/deserialize methods.
- **write_int**:
  - **rva**: 0x1402046F0
  - **signature**: Write_Int(stream, value, size_type)
  - **size_type_7**: 4 bytes (int32)
- **read_int**:
  - **rva**: 0x1402043B0
  - **signature**: Read_Int(stream, size_type) -> value
- **write_string**:
  - **rva**: 0x140204FB0
  - **signature**: Write_String(stream, std_wstring*)
- **read_string**:
  - **rva**: 0x140204AD0
  - **signature**: Read_String(stream, std_wstring*) -> success


### Game Mode Classes

- **GalacticModeClass**:
  - **description**: The galactic conquest game mode. Save/load is triggered from this context.
  - **ctor_rva**: 0x1404B1270
  - **inherits**: GameModeClass
  - **init_fields**:
    - **offset_0x338**: 1 (default value)
    - **offset_0x358**: 0x0F
    - **offset_0x368**: 1
    - **offset_0x36c**: 0x3FFFFF (bitmask, likely faction/player visibility)
- **GameModeClass**:
  - **ctor_rva**: 0x14035A5E0
  - **dtor_rva**: 0x14035AD70


### Predicted Save Structure

- **description**: Based on the class hierarchy and serialization system, the save file is predicted to have this top-level structure. Chunk IDs are UNKNOWN without a sample file -- these are placeholders based on common Alamo engine patterns.
- **tree**: {'chunk': 'ROOT (ID TBD)', 'contains_sub_chunks': True, 'children': [{'chunk': 'SAVE_HEADER (ID TBD)', 'data': ['save_name (wstring)', 'save_timestamp', 'mod_name', 'version_info', 'game_mode_id']}, {'chunk': 'PLAYER_DATA (ID TBD)', 'contains_sub_chunks': True, 'repeats': 'per player', 'children': [{'chunk': 'PLAYER_INFO', 'data': ['player_id', 'faction_name (cstring)', 'credits', 'tech_level', 'is_human', 'team_id']}, {'chunk': 'AI_STATE', 'data': ['AIDataPackClass serialized state']}]}, {'chunk': 'GALACTIC_MAP (ID TBD)', 'contains_sub_chunks': True, 'children': [{'chunk': 'PLANET (ID TBD)', 'repeats': 'per planet', 'contains_sub_chunks': True, 'children': [{'chunk': 'PLANET_INFO', 'data': ['planet_name (cstring)', 'owner_player_id', 'corruption_level']}, {'chunk': 'GROUND_UNITS', 'data': ['PersistentTacticalBuiltObjectStruct array']}, {'chunk': 'SPACE_UNITS', 'data': ['unit type references, counts']}, {'chunk': 'UPGRADES', 'data': ['PersistentUpgradeObjectStruct array']}, {'chunk': 'TRADE_ROUTES', 'data': ['TradeRouteLinkEntryClass array']}, {'chunk': 'VISIBILITY', 'data': ['fog of war modifiers per player']}]}]}, {'chunk': 'OBJECT_PERSISTENCE (ID TBD)', 'contains_sub_chunks': True, 'data': ['ObjectPersistenceClass::tPersistentUnit array -- per-object save state']}, {'chunk': 'STORY_STATE (ID TBD)', 'data': ['story flag completion, campaign progress']}]}
- **confidence**: SPECULATIVE -- based on class hierarchy only. Actual chunk IDs and exact ordering require a sample .sav file hexdump.


### Safely Editable Fields

- **description**: Fields that can be edited with low risk if the chunk structure is preserved. All require maintaining correct chunk sizes.
- **high_confidence**: credits (per player) -- int32, within a micro-chunk, planet_owner (per planet) -- int32, player index, tech_level (per player) -- int32
- **medium_confidence**: unit_hp (per unit) -- float32, within PersistentTacticalBuiltObjectStruct, corruption_level (per planet) -- likely int32 or float32, ability_cooldown_timers -- float32 values in AbilityCountdownDataPackClass, superweapon_cooldowns -- float32 values in TacticalSuperWeaponDataPackClass
- **dangerous**: save_name -- wstring, changing length will corrupt chunk sizes, object_id mappings -- SaveLoadClass::ObjectPointerPairClass references; corrupting these breaks all pointer resolution on load, Adding/removing units -- requires updating multiple linked structures (PlanetaryDataPackClass vectors, ObjectPersistenceClass, player unit lists), faction_name changes -- referenced by multiple systems, length changes corrupt chunks

