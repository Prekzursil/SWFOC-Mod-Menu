# SWFOC Snapshot Format (v1 + v2)

> **Version history**
>
> - **v1** — original format. Magic `SWFOCSNAPv1`. No explicit local-player
>   slot in section 1; the reader infers it as the first player.
> - **v2** — added 2026-04-08. Magic `SWFOCSNAPv2`. Adds an explicit
>   `local_slot: uint32` immediately after `player_count` in section 1.
>   `UINT32_MAX` (`0xFFFFFFFF`) means "no local player".
>
> Both `swfoc_lua_bridge/replay_harness.cpp` and the synthetic generator
> `swfoc_lua_bridge/make_test_snapshot.py` accept either version. The
> writer in `lua_bridge.cpp::Lua_DumpState` emits v2 by default. Pass
> `--v1` to `make_test_snapshot.py` to emit a v1 snapshot for back-compat
> regression tests. Both readers cross-check the magic against the
> `format_version` field and reject mismatched headers.

## Purpose

The `.swfocsnap` file format captures a single point-in-time view of SWFOC game state from a running `StarWarsG.exe` so it can be replayed offline by editor tests and analysis tools. The capture side lives inside `powrprof.dll` (the SWFOC Lua bridge) as the Lua helper `SWFOC_DumpState(path)`. The replay side lives outside this document.

A snapshot is **not** a full memory dump. It stores only the fields the replay harness needs to mock out player slots, Lua state counts, object-type populations, and whitelisted global bindings. Everything is little-endian, tightly packed, and fixed-width where possible so a C, C++, C#, Python, or Rust reader can parse it with `memcpy`-style reads.

## Byte Order and Padding Rules

- All multi-byte integers are **little-endian**.
- All floats are **IEEE 754 64-bit** (no 32-bit floats at the file format level; the capture widens player credits from `float` to `double`).
- All ASCII strings in fixed-width fields are **null-padded, not null-terminated** — a 64-byte faction field containing `"REBEL"` has bytes `52 45 42 45 4C 00 00 ... 00`.
- The header is aligned to 8 bytes. Sections are **not** padded between each other; each section is immediately followed by the next section header.

## File Layout

```
+----------------------------+  offset 0
|        FILE HEADER         |  68 bytes total
+----------------------------+  offset 0x44
|        SECTION 1           |  variable
+----------------------------+
|        SECTION 2           |  variable
+----------------------------+
|           ...              |
+----------------------------+
|        SECTION N           |  variable
+----------------------------+
|       END MARKER           |  12 bytes (section_id=0xFFFFFFFF, length=4, crc32)
+----------------------------+  end of file
```

## File Header (68 bytes)

| Offset | Size | Type                | Field                | Description                                                                 |
|--------|------|---------------------|----------------------|-----------------------------------------------------------------------------|
| 0x00   | 16   | char[16]            | magic                | ASCII `"SWFOCSNAPv1"` (legacy) or `"SWFOCSNAPv2"` (current) followed by 5 null bytes. v2 exact bytes: `53 57 46 4F 43 53 4E 41 50 76 32 00 00 00 00 00` |
| 0x10   | 4    | uint32 LE           | format_version       | `1` for legacy snapshots, `2` for current. Reader cross-checks against magic and rejects mismatch. |
| 0x14   | 8    | uint64 LE           | capture_timestamp_ms | Unix epoch milliseconds at the instant the capture started                  |
| 0x1C   | 32   | uint8[32]           | engine_build_hash    | SHA-256 digest of `StarWarsG.exe`, or all zeros if unavailable              |
| 0x3C   | 1    | uint8               | game_mode            | `0=unknown`, `1=galactic`, `2=tactical_space`, `3=tactical_land`, `4=menu`  |
| 0x3D   | 7    | uint8[7]            | reserved             | Zero-filled padding; section 1 starts at absolute offset `0x44`             |

Total header size: **68 bytes**. The first section header begins at file offset `0x44`. The header is not 8-aligned at the end, but every fixed-size field inside it is naturally aligned for its type, and each section's payload does its own alignment internally (see Section 4 where the `uint64` raw value is explicitly 8-aligned via its `pad[7]` field).

Magic is checked byte-for-byte. A reader that does not see exactly these 16 bytes must reject the file.

## Section Framing

Every section after the header is preceded by an 8-byte section header:

| Offset | Size | Type       | Field          | Description                                                |
|--------|------|------------|----------------|------------------------------------------------------------|
| 0x00   | 4    | uint32 LE  | section_id     | Section type (see table below)                             |
| 0x04   | 4    | uint32 LE  | section_length | Number of payload bytes that follow, **not** including this header |

Defined section IDs:

| ID          | Name             | Description                                               |
|-------------|------------------|-----------------------------------------------------------|
| `0x00000001`| player_array     | Player slot snapshots                                     |
| `0x00000002`| lua_state_registry| Count and pointer list of registered Lua states          |
| `0x00000003`| object_catalog   | Summary of named game object types and their populations  |
| `0x00000004`| global_registry  | Whitelisted Lua globals                                    |
| `0x00000005`| metadata         | Key/value metadata strings                                |
| `0xFFFFFFFF`| end              | End-of-file marker; payload is a 4-byte CRC32             |

Sections must appear in ascending ID order in a v1 snapshot. The end marker must be last. Readers must skip unknown section IDs by advancing `section_length` bytes.

## Section 1 — `player_array` (ID 0x00000001)

Payload (v2 — current):

```
uint32 player_count
uint32 local_slot            // v2 only — UINT32_MAX = no local player
for i in 0..player_count:
    uint32  slot             // 0-based slot index
    char    faction[64]      // null-padded ASCII
    float64 credits          // widened from engine float32
    int32   tech_level
    char    player_name[64]  // null-padded ASCII
```

Payload (v1 — legacy, no `local_slot`):

```
uint32 player_count
for i in 0..player_count:
    uint32  slot             // 0-based slot index
    char    faction[64]      // null-padded ASCII
    float64 credits          // widened from engine float32
    int32   tech_level
    char    player_name[64]  // null-padded ASCII
```

Per-player record size: `4 + 64 + 8 + 4 + 64 = 144 bytes`.

Section length:
- v1: `4 + 144 * player_count`
- v2: `4 + 4 + 144 * player_count` (extra `local_slot: uint32`)

Notes:
- `faction` comes from `PlayerObj.FactionName` (engine offset `+0x68`, `char*`).
- `credits` is read from `PlayerObj.Credits` (engine offset `+0x70`, `float`) and widened to `double`.
- `tech_level` is read from `PlayerObj.TechLevel` (engine offset `+0x84`, `int32`).
- `player_name` is reserved for future display-name data. v1 capture writes an empty string (all zero bytes) because the engine does not expose a player display name the bridge can safely dereference. Readers must not assume this field is populated.
- The capture bounds-checks `player_count` to `[0, 8]`. If the engine reports more than 8 players, only the first 8 are written and the count is clamped.

## Section 2 — `lua_state_registry` (ID 0x00000002)

Payload:

```
uint32 state_count
for i in 0..state_count:
    uint64 state_pointer
```

Section length: `4 + 8 * state_count`.

Notes:
- `state_pointer` values are the raw `lua_State*` addresses captured inside the running game. They are useless for replay (they point into a foreign address space) but they tell downstream tooling how many Lua states the engine spawned and whether the count matches what we expect from the mod under test.
- The capture bounds-checks `state_count` to `[0, 1024]` to defend against corrupted vectors.

## Section 3 — `object_catalog` (ID 0x00000003)

Payload:

```
uint32 type_count
for i in 0..type_count:
    char   type_name[64]       // null-padded ASCII
    uint32 instance_count
```

Per-type record size: `64 + 4 = 68 bytes`.

Section length: `4 + 68 * type_count`.

Notes:
- This is a **summary**, not the full object dump. The replay harness uses it to size its mock object lists — e.g., "the replay needs to simulate 12 TIE_Fighter instances so filter queries return the right count".
- `type_name` matches the SWFOC `GameObject_Type` name strings (e.g. `Nebulon_B_Frigate`, `TIE_Fighter`).
- `instance_count` is the number of live instances of that type at capture time. Zero is valid and means the type is known but currently has no instances.
- v1 capture walks a fixed whitelist of well-known names. Future versions may add discovery; the format supports up to `UINT32_MAX` entries.

## Section 4 — `global_registry` (ID 0x00000004)

Payload:

```
uint32 global_count
for i in 0..global_count:
    char   name[64]         // null-padded ASCII
    uint8  lua_type          // 0=nil, 1=boolean, 3=number, 4=string, 6=function, 7=userdata
    uint8  pad[7]            // zero padding so raw_value is 8-aligned
    uint64 raw_value_or_ptr  // number = IEEE 754 bit pattern, string = pointer, function = C closure addr
```

Per-global record size: `64 + 1 + 7 + 8 = 80 bytes`.

Section length: `4 + 80 * global_count`.

Allowed `lua_type` values follow the Lua 5.0.2 type tags from `lua_types.h`:

| Value | Lua type           |
|-------|--------------------|
| 0     | `LUA_TNIL`         |
| 1     | `LUA_TBOOLEAN`     |
| 3     | `LUA_TNUMBER`      |
| 4     | `LUA_TSTRING`      |
| 6     | `LUA_TFUNCTION`    |
| 7     | `LUA_TUSERDATA`    |

Values 2 (`LUA_TLIGHTUSERDATA`), 5 (`LUA_TTABLE`), and 8 (`LUA_TTHREAD`) are reserved and not written by v1 capture.

Whitelisted global names (the capture writes one record per name that resolves to any non-nil value, plus the SWFOC_* helpers, and one record per name that is nil so the replay can verify absence):

- `Find_Player`
- `Find_Object_Type`
- `Find_All_Objects_Of_Type`
- `Spawn_Unit`
- `Story_Event`
- `Letter_Box_On`
- `Suspend_AI`
- `GameRandom`
- `Create_Position`
- `SWFOC_GetVersion`
- `SWFOC_GetLocalPlayer`
- `SWFOC_SetCredits`
- `SWFOC_GetCredits`
- `SWFOC_SetTechLevel`
- `SWFOC_UncapCredits`
- `SWFOC_HeroInstantRespawn`
- `SWFOC_ListFactions`
- `SWFOC_Log`
- `SWFOC_DoString`
- `SWFOC_DrainPipe`
- `SWFOC_StateInfo`
- `SWFOC_EventControl`
- `SWFOC_DumpState`

For `LUA_TNUMBER` globals, `raw_value_or_ptr` holds the raw IEEE 754 bit pattern of the number (read via `memcpy` from a `double`). For `LUA_TSTRING` globals, it holds the captured string's engine pointer (useless for replay but diagnostic). For `LUA_TFUNCTION` globals, it holds the C function pointer if the binding is a C closure, otherwise zero. For `LUA_TNIL`, it is zero.

## Section 5 — `metadata` (ID 0x00000005)

Payload:

```
uint32 entry_count
for i in 0..entry_count:
    uint16 key_length
    char   key[key_length]
    uint16 value_length
    char   value[value_length]
```

Neither `key` nor `value` is null-terminated. There is no padding between entries.

Section length: sum of the field sizes above.

Required keys in v1 (capture must emit all of them, in any order):

| Key                    | Example value                |
|------------------------|------------------------------|
| `capture_method`       | `powrprof_dll`               |
| `mod_name`             | `unknown`                    |
| `mod_version`          | `unknown`                    |
| `swfoc_bridge_version` | `1.0`                        |

Readers must tolerate additional unknown keys.

## Section 11 — `selected_units` (ID 0x0000000B, added 2026-04-23)

OPTIONAL. Emitted by `SWFOC_DumpState` only when the selection reader
resolves a non-empty selection (i.e. tactical battle with units selected).
Enables replay-side tests to observe what the user had selected at capture
time without re-walking the live selection pointer chain.

Payload:

```
uint32 count
for i in 0..count:
    uint64 obj_addr   // absolute GameObject pointer captured live
```

Section length: `4 + 8 * count`. `count` is bounded to `64` by the writer
(mirrors `RVA::Selection::kMaxSelectionCount`).

## Section 12 — `unit_detail` (ID 0x0000000C, added 2026-04-23)

OPTIONAL. Emitted alongside section 11. One record per selected obj_addr;
carries the state reads Task 99 (hardpoint-behavior invulnerability) and
Task 100 (damage-path hunt) need for autonomous offline iteration.

Payload:

```
uint32 unit_count
for i in 0..unit_count:
    uint64  obj_addr
    char    type_name[64]       // reserved (writer currently emits "")
    int32   owner_slot          // GameObj+0x58
    float32 hull                // GameObj+0x5C
    float32 max_hull            // reserved; writer emits 0.0 (engine layout unstable)
    uint8   invuln_flag         // GameObj+0x3A7 (display flag, no gameplay effect)
    uint8   prevent_death       // GameObj+0x3A1 (bit 0x80 is the capture of interest)
    uint8   reserved[6]         // zero padding, 8-byte alignment buffer
    uint32  hardpoint_count
    uint32  hardpoint_index[hardpoint_count]  // indices derived from GameObj.ComponentArray walk
```

Per-unit fixed portion: 92 bytes. `hardpoint_count` is bounded to 32 by
the writer. Behavior-object lists are NOT in this section — see section 13.

## Section 13 — `behavior_attach` (ID 0x0000000D, added 2026-04-23)

OPTIONAL. Flat list of (obj_addr, hp_index, behavior_name) triples that
the replay harness applies on top of section 12's unit records. Current
writer emits `entry_count=0` because the engine does not expose a
"list behaviors on hardpoint" read path yet — once Task 100's IDA
investigation uncovers it, the writer populates this section.

Payload:

```
uint32 entry_count
for i in 0..entry_count:
    uint64 obj_addr
    uint32 hp_index
    char   behavior_name[32]    // e.g. "INVULNERABLE"
```

Per-entry size: 44 bytes. Readers silently skip triples that reference
obj_addrs not present in section 12 (forward-compat: a capture that emits
behaviors for units outside the selection does not fail loading).

## End Marker

After the last content section, the file ends with a 12-byte record:

| Offset | Size | Type       | Field          | Description                                               |
|--------|------|------------|----------------|-----------------------------------------------------------|
| 0x00   | 4    | uint32 LE  | section_id     | Always `0xFFFFFFFF`                                       |
| 0x04   | 4    | uint32 LE  | section_length | Always `4`                                                |
| 0x08   | 4    | uint32 LE  | crc32          | CRC32 of every byte from offset 0 up to and including `section_length` (i.e., the 8 bytes `FF FF FF FF 04 00 00 00` are covered by the CRC; the 4-byte CRC itself is not) |

CRC32 is computed with the standard polynomial `0xEDB88320`, initial value `0xFFFFFFFF`, final XOR `0xFFFFFFFF`, and byte-reflected input/output (the same variant used by zlib, PKZIP, and `crc32()` in Python's `zlib` module).

## Example Hex Dump (Minimal Snapshot)

The smallest meaningful v1 snapshot has the header, an empty `player_array`, an empty `lua_state_registry`, an empty `object_catalog`, an empty `global_registry`, a `metadata` section with only the four required keys, and the end marker. With `capture_timestamp_ms = 0x1234567890ABCDEF`, `engine_build_hash = all zeros`, `game_mode = 4` (menu), and the metadata set to the example values above, the file looks like this (offsets on the left, bytes in the middle, annotation on the right):

```
0000: 53 57 46 4F 43 53 4E 41 50 76 31 00 00 00 00 00  magic "SWFOCSNAPv1" + 5 nulls
0010: 01 00 00 00                                       format_version = 1
0014: EF CD AB 90 78 56 34 12                           capture_timestamp_ms = 0x1234567890ABCDEF
001C: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00   engine_build_hash bytes 0..15
002C: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00   engine_build_hash bytes 16..31
003C: 04                                                game_mode = 4 (menu)
003D: 00 00 00 00 00 00 00                              reserved padding

0044: 01 00 00 00                                       section 1 id = player_array
0048: 04 00 00 00                                       section 1 length = 4
004C: 00 00 00 00                                       player_count = 0

0050: 02 00 00 00                                       section 2 id = lua_state_registry
0054: 04 00 00 00                                       section 2 length = 4
0058: 00 00 00 00                                       state_count = 0

005C: 03 00 00 00                                       section 3 id = object_catalog
0060: 04 00 00 00                                       section 3 length = 4
0064: 00 00 00 00                                       type_count = 0

0068: 04 00 00 00                                       section 4 id = global_registry
006C: 04 00 00 00                                       section 4 length = 4
0070: 00 00 00 00                                       global_count = 0

0074: 05 00 00 00                                       section 5 id = metadata
0078: 58 00 00 00                                       section 5 length = 0x58 (= 88)
007C: 04 00 00 00                                       entry_count = 4

0080: 0E 00                                             key_length = 14
0082: 63 61 70 74 75 72 65 5F 6D 65 74 68 6F 64         "capture_method"
0090: 0C 00                                             value_length = 12
0092: 70 6F 77 72 70 72 6F 66 5F 64 6C 6C               "powrprof_dll"

009E: 08 00                                             key_length = 8
00A0: 6D 6F 64 5F 6E 61 6D 65                           "mod_name"
00A8: 07 00                                             value_length = 7
00AA: 75 6E 6B 6E 6F 77 6E                               "unknown"

00B1: 0B 00                                             key_length = 11
00B3: 6D 6F 64 5F 76 65 72 73 69 6F 6E                   "mod_version"
00BE: 07 00                                             value_length = 7
00C0: 75 6E 6B 6E 6F 77 6E                               "unknown"

00C7: 14 00                                             key_length = 20
00C9: 73 77 66 6F 63 5F 62 72 69 64 67 65 5F 76 65 72
      73 69 6F 6E                                       "swfoc_bridge_version"
00DD: 03 00                                             value_length = 3
00DF: 31 2E 30                                           "1.0"

00E2: FF FF FF FF                                       end marker section_id
00E6: 04 00 00 00                                       end marker section_length = 4
00EA: XX XX XX XX                                       crc32 (LE) of bytes 0x00 .. 0x00E9 inclusive
```

Total file size: `0x00EE = 238 bytes` for a fully-empty snapshot with the required metadata.

The actual CRC32 value depends on the concrete bytes produced by the capture, so it is shown as `XX XX XX XX` above.

## Reader Checklist

A compliant reader MUST:

1. Verify the magic matches one of `SWFOCSNAPv1` or `SWFOCSNAPv2` exactly.
2. Verify `format_version` is `1` (legacy) or `2` (current). Reject the file if the magic and the version disagree (e.g. `SWFOCSNAPv1` header with `format_version=2`).
3. When `format_version >= 2`, read the additional `local_slot: uint32` field in section 1 immediately after `player_count`. Treat `UINT32_MAX` (`0xFFFFFFFF`) as "no local player".
4. When `format_version == 1`, derive the local slot from `players[0].slot` after the per-player loop completes (legacy convention).
5. Skip unknown section IDs by consuming `section_length` bytes.
6. Recompute the CRC32 over all bytes before the end-marker CRC field and compare. Reject the file on mismatch.
7. Treat all fixed-width strings as null-padded and stop interpreting at the first null byte.
8. Treat `capture_timestamp_ms = 0` as "unknown" but still accept the file.

## Writer Guarantees

The capture writer (`SWFOC_DumpState`) guarantees:

1. The header is written exactly once at offset 0.
2. Sections appear in ascending ID order, no duplicates.
3. `player_count` is clamped to at most 8.
4. `state_count` is clamped to at most 1024.
5. The `capture_timestamp_ms` field is populated via `GetSystemTimeAsFileTime` converted to Unix milliseconds.
6. The end marker and CRC32 are written last. If the writer is interrupted before the end marker is emitted, the file is truncated and invalid — readers will detect this via the CRC mismatch and/or missing end marker.
