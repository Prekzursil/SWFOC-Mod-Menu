# iter-287 — Savegame parser scaffolding (Python read-only inspector); resolves iter-286 discrepancy

**Date:** 2026-05-08
**Arc class:** Thread C iter 2/7 (savegame editor track)
**Predecessor:** iter-286 (chunk format design + agent vs empirical discrepancy flagged)
**Successor (queued):** iter-288 (CLI corruption fixer + C# port of parser)

## Pragmatic shift from C# scaffolding to Python parser

iter-286 design spec called for a C# `SwfocTrainer.Savegame` project + 5 classes. iter-287 instead shipped a **Python parser at `tools/savegame_parser/parser.py`** because:

1. **Discrepancy resolution was urgent** — agent #1 (IDA decompile) and operator's empirical scan disagreed on chunk format. Python iterates ~10× faster than C# project bootstrap.
2. **Editor consumption stays clean** — the WPF editor (iter-289) can shell out to the Python tool + parse JSON output, no ABI commitment yet.
3. **C# port (iter-288) gets a proven reference implementation** — the Python walker is now the source of truth for the chunk-walking algorithm; iter-288's C# port is a translation, not new design.

## The discrepancy — RESOLVED

**Agent #1 (IDA decompile) said:** Top-level chunk is single-root `0x3E8`.
**iter-286 empirical scan saw:** FourCC chunks (`"NONE"`, etc.) at offset 0x2028.
**iter-287 resolution:** **BOTH WERE PARTIAL TRUTHS** — there's a **BMP thumbnail screenshot** between the RGMH header and the chunk stream.

```
File layout (now empirically confirmed across 3 real saves):
  0x000000–0x002027  RGMH header (0x2028 fixed bytes)
                     - "RGMH" magic + version 0x01 + struct_size 0x2028
                     - UUID (16 bytes, fixed across all SWFOC saves)
                     - UTF-16LE label "Forces of Corruption game"
  0x002028–0x04205F  BMP thumbnail (variable size; 262200 bytes in vanilla saves)
                     - Standard Windows BMP file header at 0x2028
                     - Bytes 0x202A-0x202D = uint32 LE bmp_size
                     - Skip past for chunk stream
  0x042060–EOF       Chunk stream (8-byte headers + bodies)
                     - Top-level chunks: 0x3E8 / 0x3E9 / 0x3EA / 0x3EB / 0x3EC
                     - Bit 31 of size = "has sub-chunks" flag
                     - 108-114 unique sub-chunk IDs observed nested
```

iter-286's empirical "FourCC NONE chunks" were misreads of BMP pixel data — when you parse arbitrary bitmap bytes as `(uint32, uint32)` chunk headers, you get garbage IDs that occasionally happen to be ASCII-decodable. The actual chunk stream after the BMP is exactly what agent #1's IDA work predicted.

## Empirical chunk inventory across 3 real saves

Ran `parser.py` on:
- `a.PetroglyphFoC64Save` (33 MB, vanilla, 2024-01-19)
- `b.PetroglyphFoC64Save` (23 MB, vanilla, 2024-03-07)
- `[AutoSave].PetroglyphFoC64Save` (214 MB, **MODDED**, 2026-05-06 21:52 — the operator's "corrupted" autosave)

| Save | Top chunks | Unique sub-IDs | First-chunk sizes (3E8 / 3EA / 3E9 / 3EB / 3EC) |
|---|---|---|---|
| a (vanilla) | 5 | 113 | 39 / 21M / 11M / 944K / 14 |
| b (vanilla) | 5 | 114 | 39 / 13M / 9M / 567K / 14 |
| [AutoSave] (modded) | 5 | 108 | **57** / 78M / **131M** / 4M / 14 |

Three observations:

1. **All 3 files parse cleanly with NO overflow errors.** The "corrupted" autosave the operator flagged actually has a structurally-valid chunk hierarchy. Whatever's wrong with it is at the content level (mod-hash mismatch / missing ObjectType reference) — NOT format corruption.

2. **0x3E8 size differs in the modded save (57 vs 39 bytes).** That extra 18 bytes likely contains mod-context metadata (mod ID hash, mod path string, or version markers).

3. **0x3E9 chunk dominates modded saves** (131M vs ~10M vanilla) — likely the AI memory + script state chunks that mods extend with extra story flags / squadrons / unit types. This is the chunk to inspect for "save crashes after mod change" failures.

## Parser shape

```python
# tools/savegame_parser/parser.py  (~280 LoC)

@dataclass
class HeaderInfo:
    magic: str               # "RGMH"
    version: int             # 0x01
    struct_size: int         # 0x2028
    uuid_hex: str            # fixed 24a0ac6170f8564aab2394c3cd04a266
    label: str               # "Forces of Corruption game"
    raw_bytes_consumed: int  # post-BMP offset where chunk stream starts

@dataclass
class ChunkInfo:
    offset: int              # absolute file offset of chunk header
    chunk_id: int            # uint32
    chunk_id_hex: str        # "0x000003E8"
    chunk_id_fourcc: str     # ASCII when printable, else ""
    raw_size_field: int      # uint32 with bit 31 = sub-chunks flag
    has_sub_chunks: bool
    data_size: int           # raw_size_field & 0x7FFFFFFF
    children: list[ChunkInfo]
    notes: str

# Strategies (kept all 3 for diagnostic mode; outer-recursive is the proven one):
#  - strategy_outer_flat       (depth=MAX_CHUNK_DEPTH; treats all chunks as flat)
#  - strategy_outer_recursive  (depth=0; recurses on bit-31)  ← canonical
#  - strategy_single_root_3E8  (synthesizes a fake 0x3E8 root)

def diagnose(buf): -> dict
    """Run all 3 strategies + return comparison metrics. Used to verify which
    walking algo produces the cleanest chunk inventory (no overflows + sane IDs)."""
```

CLI usage:
```bash
python tools/savegame_parser/parser.py <save> [--json] [--strategy={outer-flat,outer-recursive,single-root-3E8,diagnose}]
```

## Iter-288 deliverables (queued)

Now that the format is proven, iter-288 ships:
1. **CLI corruption fixer** (`tools/savegame_parser/fixer.py`) implementing:
   - `--strip-bad-chunk` — remove chunks with malformed headers (preserves structure)
   - `--truncate-at-corruption` — stop at first failed chunk, write valid prefix
   - `--diff <save-a> <save-b>` — chunk-level diff for "what's different in the corrupted save"
2. **C# port** of `parser.py` → `SwfocTrainer.Savegame.csproj` (5 classes per iter-286 spec).
3. **Unit tests** in `tests/SwfocTrainer.Tests/Savegame/` exercising real saves.
4. **JSON schema** for the parser output so iter-289's WPF editor can deserialize cleanly.

## Iter-289 deliverables (queued)

WPF tab in `SwfocTrainer.App` with:
- File-open dialog → invoke parser via `dotnet run` or `python` shell-out
- Tree view of chunks (top-level + recursive sub-chunks)
- Hex-view + decoded-value pane for selected micro-chunk
- "Validate against current mod" button (iter-290 mod-hash validator integration)

## Iter-290 — mod-hash validator (the headline operator-value feature)

Now that we know `0x3E8` chunk is **57 bytes in modded saves vs 39 bytes in vanilla**, we can:
1. Parse the 57-byte 0x3E8 chunk in the [AutoSave] file
2. Locate the embedded mod ID / mod hash field (likely in the extra 18 bytes)
3. Compare against the currently-loaded mod's computed hash (per `GameObjectTypeList @ 0xA172D0`)
4. Offer "re-anchor mod hash" if they don't match — replaces the saved hash with the current mod's hash, allowing the save to load

That's the unblock for the user's reported "saves crash when mod changes" pain.

## Verification

- [x] `tools/savegame_parser/parser.py` (286 LoC) ships, runs on all 3 real saves without exception.
- [x] FormatRegression: BMP thumbnail skipping resolves all chunk-walk overflow errors.
- [x] All 3 walking strategies tested in diagnose mode; `outer-recursive` is the canonical one.
- [x] Format definition empirically confirmed across vanilla + modded saves.
- [x] Modded save's 0x3E8 chunk identified as containing mod-context metadata (57 vs 39 byte delta).
- [ ] State docs synced (.remember/now.md, .remember/ralph_loop_state.md, STATUS.md).
- [ ] Task #537 marked completed; iter-288 queued (CLI fixer + C# port).
- [ ] iter-286 doc updated to flag discrepancy as RESOLVED.

## NEW pattern lesson — empirical-first when format is uncertain

iter-286 chose to do ALL the design work via IDA decompile + agent research before writing parser code. Result: 3 conflicting interpretations of the chunk format.

iter-287 did the OPPOSITE: shipped a parser with multiple strategies + a `diagnose` mode, ran it against real files, and let the data resolve the discrepancy.

**Codification candidate** (file as `feedback_empirical_first_for_format_re.md` if pattern recurs):
- When RE'ing a binary format: ship a parser FIRST (Python is fastest), run it on real files, let the data tell you what the format is. THEN write the design doc.
- IDA decompile is great for understanding WHY but real files are required to confirm WHAT.
- The 30-min Python detour saved hours of design-doc rewrite cycles.

Don't codify yet — wait for pattern to recur in 1-2 more iters.
