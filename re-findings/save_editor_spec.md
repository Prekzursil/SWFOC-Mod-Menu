# SWFOC Save File Editor Specification

## Status: FIRST PASS -- Binary Analysis Complete, No Sample File Validated

This document describes what can be edited in Star Wars: Empire at War - Forces of Corruption
save files (.sav), based on reverse engineering of StarWarsG.exe (x86_64 Steam build) via
Ghidra. **No save editor exists in the modding community -- this is uncharted territory.**

**Critical caveat:** No actual .sav sample file was available on disk during this analysis.
The chunk IDs and exact serialization order are derived from class structure and code flow,
not from hexdump validation. The first step before building an editor is obtaining and
hex-dumping an actual save file.

---

## File Format Summary

The save file uses the **Westwood/Petroglyph chunk format**, the same binary container used
for .alo, .ala, and other Alamo engine files. There is no file-level magic number or version
header -- the file begins directly with the first chunk.

### Chunk Header (8 bytes)
```
Offset  Type    Name
0x00    uint32  chunk_id        -- identifies chunk type
0x04    uint32  chunk_size      -- data size in bytes (bit 31 = has sub-chunks)
```

- If `chunk_size & 0x80000000`, the chunk contains nested sub-chunks, not raw data
- Actual data size = `chunk_size & 0x7FFFFFFF`
- Chunks nest to arbitrary depth (up to 256 levels)

### Micro-Chunk Header (2 bytes)
```
Offset  Type    Name
0x00    uint8   micro_chunk_id  -- identifies field within parent chunk
0x01    uint8   micro_chunk_size -- data size (max 255 bytes)
```

Micro-chunks are leaf-level data containers inside a chunk. They hold individual fields
(an int, a float, a short string). They cannot nest.

### String Encoding
- **In-file strings**: null-terminated ASCII/ANSI (strlen+1 bytes written including null)
- **Save names**: UTF-16LE wide strings (wchar_t, std::wstring with MSVC SSO)

### Compression
zlib is statically linked (compress2 at RVA 0x7A1470, uncompress2 at RVA 0x7A1590).
Whether saves are zlib-wrapped or raw chunk trees is **TBD until a sample file is examined**.
If compressed, the signature bytes 0x78 0x9C or 0x78 0x01 will appear near the file start.

### Checksum
No CRC or checksum calculation was found in the save path. The engine trusts the chunk
structure. This is good news for editing -- no integrity check to bypass.

---

## Editable Fields

### SAFE TO EDIT (high confidence)

These are scalar values in micro-chunks. As long as the micro-chunk size byte is preserved
(the value fits in the same number of bytes), editing is safe.

| Field | Type | Location | Notes |
|-------|------|----------|-------|
| Player credits | int32 | Player data chunk, micro-chunk | Per-player currency |
| Planet owner | int32 | Planet chunk, micro-chunk | Player index (0-based) |
| Tech level | int32 | Player data chunk | Per-player technology level |
| Unit HP | float32 | Per-unit persistent state | Current hitpoints |

### MODERATELY SAFE (medium confidence)

These require understanding the surrounding data but are still scalar edits.

| Field | Type | Location | Notes |
|-------|------|----------|-------|
| Corruption level | int32/float32 | Planet chunk | Underworld corruption state |
| Ability cooldowns | float32 | AbilityCountdownDataPackClass | Timer values, set to 0 to reset |
| Superweapon timers | float32 | TacticalSuperWeaponDataPackClass | Cooldown remaining |
| Shield state | float32 | BaseShieldDataPackClass | Shield HP/recharge state |
| Visibility/fog | bitmask | PlanetaryDataPackClass | Per-player fog of war |

### DANGEROUS -- DO NOT EDIT WITHOUT FULL PARSER

| Field | Risk | Why |
|-------|------|-----|
| Save name (wstring) | HIGH | Changing length corrupts all parent chunk sizes |
| Object ID mappings | CRITICAL | SaveLoadClass::ObjectPointerPairClass -- pointer resolution table. Corrupting this breaks ALL object references on load |
| Adding/removing units | CRITICAL | Requires updating: PlanetaryDataPackClass vectors, ObjectPersistenceClass entries, player unit lists, linked list pointers |
| Faction names | HIGH | Referenced by multiple systems, length-prefixed in context |
| Chunk structure itself | CRITICAL | Moving, reordering, or resizing chunks without updating all parent sizes will corrupt the file |

---

## Architecture for a Save Editor

### Phase 1: Parser (read-only)

Build a chunk tree parser first. This is straightforward:

```
function parse_chunk_tree(stream):
    while stream.has_data():
        chunk_id = read_uint32(stream)
        raw_size = read_uint32(stream)
        has_children = (raw_size & 0x80000000) != 0
        data_size = raw_size & 0x7FFFFFFF
        
        if has_children:
            children = parse_chunk_tree(stream.slice(data_size))
            yield Chunk(id=chunk_id, children=children)
        else:
            data = stream.read(data_size)
            micro_chunks = parse_micro_chunks(data)
            yield Chunk(id=chunk_id, data=data, micro_chunks=micro_chunks)

function parse_micro_chunks(data):
    offset = 0
    while offset < len(data):
        mc_id = data[offset]
        mc_size = data[offset + 1]
        mc_data = data[offset + 2 : offset + 2 + mc_size]
        yield MicroChunk(id=mc_id, data=mc_data)
        offset += 2 + mc_size
```

### Phase 2: Chunk ID Catalog

With a parsed tree, catalog chunk IDs by examining multiple save files:
1. Save at different campaign points
2. Diff the chunk trees to identify which chunks change
3. Map chunk IDs to game concepts (players, planets, units)
4. Build a lookup table: chunk_id -> human-readable name

### Phase 3: Field Editor

For scalar fields within micro-chunks:
1. Navigate to the target micro-chunk
2. Read the current value (interpret bytes as int32/float32/etc.)
3. Write the new value (same byte count)
4. No chunk size updates needed if the value size is unchanged

For variable-length fields (strings):
1. Calculate the size delta
2. Update the micro-chunk size byte
3. Update ALL parent chunk sizes up to the root
4. Rewrite the file from the modified chunk tree

### Phase 4: Structural Editor

For adding/removing objects:
1. Full round-trip: parse entire file -> modify tree -> serialize entire file
2. Must update ObjectPointerPairClass mappings
3. Must update all DynamicVector counts and contents
4. This is the hardest part and requires understanding every chunk type

---

## Key RVAs for Hooking/Debugging

These addresses (relative to module base 0x140000000) are useful for runtime
debugging with x64dbg or Frida to capture actual save data.

### Save Flow
| Function | RVA | Purpose |
|----------|-----|---------|
| SaveGameEventClass::Execute | 0x48FC00 | Entry point when save is triggered |
| Save dispatch function ptr | global at 0xB313D8 | Actual save execution |
| SaveGameEventClass::ctor | 0x48FA80 | Save event creation |

### Chunk I/O
| Function | RVA | Purpose |
|----------|-----|---------|
| ChunkWriter::Open_Chunk | 0x21FE20 | Begin writing a chunk (logs chunk_id) |
| ChunkWriter::Close_Chunk | 0x21FEB0 | End chunk (patches size) |
| ChunkWriter::Open_Micro_Chunk | 0x21FFA0 | Begin micro-chunk (logs mc_id) |
| ChunkWriter::Close_Micro_Chunk | 0x220030 | End micro-chunk (patches size) |
| ChunkWriter::Write | 0x2200B0 | Write raw bytes |
| ChunkWriter::Write_CString | 0x220140 | Write null-terminated string |
| ChunkReader::Open_Chunk | 0x2204A0 | Read chunk header |
| ChunkReader::Close_Chunk | 0x220520 | Skip remaining data, pop level |

### File I/O
| Function | RVA | Purpose |
|----------|-----|---------|
| FileClass::Open | 0x213600 | CreateFileA wrapper |
| FileClass::dtor | 0x2132F0 | CloseHandle |

### Serialization Primitives
| Function | RVA | Purpose |
|----------|-----|---------|
| Write_Int | 0x2046F0 | Write integer to stream |
| Read_Int | 0x2043B0 | Read integer from stream |
| Write_String | 0x204FB0 | Write wstring to stream |
| Read_String | 0x204AD0 | Read wstring from stream |

---

## Recommended Next Steps

### 1. Obtain a Sample Save File
Play a galactic conquest game, save it, and locate the .sav file at:
`%APPDATA%\Petroglyph\Empire at War - Forces of Corruption\Save\`

### 2. Hexdump and Validate Format
```bash
xxd -l 512 savefile.sav
```
Look for:
- Does it start with a chunk header (8 bytes: id + size)?
- Or does it start with zlib magic (0x78 0x9C)?
- Are there recognizable strings (planet names, faction names)?

### 3. Runtime Trace with Frida
Hook ChunkWriter::Open_Chunk to log all chunk_ids in write order:
```javascript
Interceptor.attach(base.add(0x21FE20), {
    onEnter(args) {
        console.log("Open_Chunk: id=" + args[1].toInt32().toString(16) + 
                     " depth=" + Memory.readS32(args[0].add(0x10)));
    }
});
```
This will produce the complete chunk ID catalog with nesting depth.

### 4. Hook Open_Micro_Chunk Similarly
```javascript
Interceptor.attach(base.add(0x21FFA0), {
    onEnter(args) {
        console.log("Open_MicroChunk: id=" + args[1].toInt32().toString(16));
    }
});
```

### 5. Build the Parser
With the chunk ID catalog from step 3, build a Python parser that reads
the chunk tree and displays it in human-readable form. This is the
foundation for all editing.

---

## Known Persistent Data Structures

These classes are serialized into the save file. Each has virtual serialize/deserialize
methods that emit micro-chunks.

### Per-Planet (PlanetaryDataPackClass)
- Built structures (PersistentTacticalBuiltObjectStruct)
- Applied upgrades (PersistentUpgradeObjectStruct)
- Hyperspace lane connections (LineLinkStruct)
- Trade route state (TradeRouteLinkEntryClass)
- Ground unit garrison (DynamicVector of GameObjectClass*)
- Space unit garrison (DynamicVector of GameObjectClass*)
- Type references (DynamicVector of GameObjectTypeClass*)
- Fog of war (visibility modifiers per player)
- Planet name (SSO string)

### Per-Object (ObjectPersistenceClass::tPersistentUnit)
- Object type reference
- DataPack state for each attached behavior/component
- Position, rotation, health state

### Per-Player
- Credits, tech level, faction name
- AI state (AIDataPackClass) for computer players
- Diplomatic relations, team assignments
- Build queues, production state

### Global
- Game mode (galactic conquest, campaign)
- Turn counter / game time
- Story flag state (campaign progress)
- Random seed state

---

## Comparison with Other Alamo Engine Formats

The .sav format shares the exact same chunk infrastructure as:
- **.alo** (3D model files) -- same ChunkReader/ChunkWriter
- **.ala** (animation files) -- same chunk headers
- **.meg** (archive files) -- different container but same engine

Existing tools like Mike.nl's AloViewer or the Petroglyph modding tools parse
the chunk format for .alo files. Their chunk parsing code can be adapted for
.sav files -- only the chunk IDs and data interpretation differ.
