# Thread C — SWFOC Savegame RE Research (2026-05-08)

Research dispatched during iter-284 by parallel sub-agent. Informs Thread C iter-286+ savegame editor + corruption-fixer roadmap.

## TL;DR

The savegame format is a well-structured Petroglyph W3D-derived chunk hierarchy with clear micro-chunk type classification. The master loader at `0x140052D10` is the authoritative implementation; it has 53 callee functions and clear error handling paths. **A corruption fixer is absolutely feasible** — the parser layer can ship as a standalone utility, and editor+validator can follow incrementally. **The mod-hash validation is the highest-leverage fix** for "save crashes when mod changes" — directly addresses the user's pain point.

## Format layers

### Header (0x0–0x100)
- 4 B `RGMH` magic
- 4 B version `0x01`
- 4 B struct size `0x2028` (8232 bytes)
- 8 B reserved zeros
- 16 B fixed UUID `24a0ac6170f8564aab2394c3cd04a266`
- UTF-16LE label `"Forces of Corruption game"` zero-padded to ~0x100

### Body — chunk-based
- 8 B chunk header per chunk: `uint32 chunk_id` + `uint32 chunk_size` (bit 31 set = contains sub-chunks)
- Primary container: chunk ID `0x3E8` (1000)
- Micro-chunks within 0x3E8: 2 B header (id + size, max 255 B per micro-chunk)
- Micro-chunk types observed (switch at `0x140052FEF`):
  - `0x00` — raw serialized data
  - `0x01–0x04` — individual int32 fields (slot index, player count, difficulty, corruption level)
  - `0x05` — variable-length string/blob (1 B length prefix + data)
  - `0x06` — bulk integer array

### Footer
- Chunk loop terminates when `Open_Chunk` returns false (loader `0x140052F91`)
- File closed via `Close_Chunk` (`0x140220520`)

## Key functions

| RVA | Symbol | Role | Caller count |
|---|---|---|---|
| `0x140052D10` | `sub_140052D10` | **Master save loader** (3124 B, 53 callees) | 5 |
| `0x140220280` | `ChunkReaderClass::ctor` | Chunk reader constructor | 10 |
| `0x1402204A0` | `ChunkReaderClass::Open_Chunk` | Chunk header reader | ~20 |
| `0x140220370` | `ChunkReaderClass::Close_Chunk` | Chunk finalizer | ~15 |
| `0x140220520` | `ChunkReaderClass::Next_Chunk` | Chunk advance | ~10 |
| `0x140220610` | `Has_Micro_Chunk` | Micro-chunk detector | ~5 |
| `0x140220710` | `Read_Micro_ID` | Micro-chunk type read | within 0x140052D10 |
| `0x140220730` | `Read_Micro_Data` | Micro-chunk data read | within 0x140052D10 |
| `0x1402209A0` | `Read_Micro_String` | Micro-chunk wide-string variant | within 0x140052D10 |
| `0x140052820` | `sub_140052820` | SaveGameStruct vector handler | 0 |
| `0x1402130D0` | File open | I/O handler | called by 0x140052D10 |
| `0x1402132F0` | File close/cleanup | I/O finalizer | called by 0x140052D10 |
| `0x14048FA20` | `SaveGameEventClass` ctor | Save event factory | 0 |
| `0x140019760` | `EventFactoryClass<SaveGameEventClass,36>::Create` | Event factory dispatch | 0 |

## Crash / abort points

1. `0x140052F63` — File open failure (`sub_140213840`).
2. `0x140052F91` — Chunk read loop sentinel; `Open_Chunk` failure → truncated/corrupted stream.
3. `0x140052FBF` — Chunk ID validation (default switch arm).
4. `0x140052FEF` — Micro-chunk type validation (default switch arm).
5. `0x140052F79` — Mode-specific loader invocation; nullptr on uninitialized game mode.
6. `0x140213010–0x1402132F0` — FileClass I/O failure.

## Mod-context binding

Saves embed mod ObjectType hashes; loader validates against currently-loaded mod.

- **Save stores**: hash of each mod's ObjectType definitions (chunk ID `0x3E8`, micro-chunk type `0x05` likely).
- **Load-time check**: `sub_140052D10` reads mod hash micro-chunk → engine computes hash of loaded mod ObjectTypes → comparison.
- **Mismatch**: HIGH severity — desyncs unit definitions, building costs, tech requirements. Loader emits `"Found save game %d - %ls"` warning at `0x140053BE`.
- **Failure mode**: units spawn with old stats; production breaks; tech trees misalign.

## Corruption-fix strategies (ranked)

| Strategy | Difficulty | Risk | Notes |
|---|---|---|---|
| **Strip-bad-chunk** | Low | Low | Remove unknown-id or malformed chunks; file remains valid if structure intact. **Recommended first-pass.** |
| **Truncate at corruption** | Low | Medium | Stop at first failed chunk open; preserves preamble + valid body; loses post-corruption data. |
| **Rebuild TOC** | Medium | Medium | Re-scan file, rebuild chunk offset table; needs known chunk IDs. |
| **Re-anchor mod hash** | High | High | Recompute ObjectType hash for loaded mod; write to save. **Highest-leverage fix for "save crashes after mod change."** |
| **Selective micro-chunk drop** | Medium | Medium | Drop type-5 strings if corrupted; loses cosmetic data, preserves game state. |
| **Byte-level repair** | Very High | Very High | CRC32 / pattern reconstruction; only viable for known patterns. |

## Recommended Thread C iter sequence

| Iter | Scope | LoC | Deliverable |
|---|---|---|---|
| **iter-285** (NOTE: queued for bridge wires, see iter-285 plan) — re-number Thread C below | | | |
| iter-286 | RE design doc + chunk format deep-dive + crash points + micro-chunk taxonomy | 300-400 (docs) | `iter286_savegame_format_re_kickoff.md` |
| iter-287 | Binary parser: ChunkReader wrapper (C# layer) + chunk enumeration + micro-chunk extraction | 400-600 | `SavegameParser.cs` + unit tests |
| iter-288 | Corruption fixer CLI: strip-bad-chunk + truncate-at-failure + validation report | 300-500 | `SwfocSavegameFixer.exe` |
| iter-289 | Savegame editor UI: read save → display chunks → edit/delete micro-chunks → write back | 600-1000 | `SavegameEditorTab.cs` + WPF controls |
| iter-290 | Mod hash validator: load mod XML → compute ObjectType hash → compare + suggest re-anchor | 200-400 | `ModHashValidator.cs` + integration tests |
| iter-291 | Integration + smoke tests: corrupt test saves → fixer → verify → edit → save → load in game | 200-300 | Test suite + 5-10 regression cases |
| iter-292 | Close-out: offline verification + docs | docs | `iter292_savegame_close_audit.md` |

**Implementation strategy**: Parser-first (iter-287). CLI fixer ships before UI (iter-288 immediate utility for batch corruption recovery). UI is read-layer wrapper on proven parser (iter-289). Mod hash validation is data-driven, testable offline (iter-290).

**Success metrics**:
- Parser correctly enumerates all chunks in known-good saves (100% pass).
- Fixer recovers playable save from truncated/corrupted test corpus (>80% recovery).
- Editor read→modify→write saves load in game (100% test cases).
- Mod hash validator flags mod-mismatch saves (100% precision).

## Open questions (for iter-286 to drill into)

1. **Full chunk ID taxonomy** — observed `0x3E8`; are there others? Scan 10+ known-good saves.
2. **Micro-chunk type 5 encoding** — exact length-prefix format? Trace `sub_1402209A0`.
3. **Mod hash format** — CRC32, MD5, SHA1, custom? Find hash function near `GameObjectTypeList @ 0xA172D0`.
4. **Chunk size flag bits** — bit 31 = sub-chunks; other bits? Trace `sub_140220610`.
5. **Save version evolution** — magic version `0x01` suggests versioning; v2/v3 exist?
6. **Compression** — zlib used for chunk bodies? Scan save header for `0x789C` magic.
7. **Chunk ordering** — is order fixed/required? Trace `sub_140052D10` for ordering checks.

## Source files referenced

- `knowledge-base/decompile_corpus/ida_full/full_b*.json` (master loader decompile)
- `knowledge-base/alamo_engine_reference.md:1018-1019` (SaveGame_dispatch_fptr + SaveGameEventFactory_singleton pins)
- `knowledge-base/verified_facts.json` (RVA ledger)
- `tools/callgraph_query.py` (RTTI + caller enumeration)
- `knowledge-base/trainer_feature_candidates_2026-04-26.md:250` (SaveGameEventClass cluster #152)
