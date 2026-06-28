# iter-304 — Asset/icon extraction kickoff (.meg parser MVP)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (asset icons per user mandate; multi-iter arc start)
**Predecessor:** iter-303 (Settings tab XAML mod-picker)
**Successor (queued):** iter-305 (DDS decoder + thumbnail generation)

## What changed (1 file new, ~200 LoC; 7/7 smoke tests pass)

- **NEW** `tools/asset_extractor/meg_parser.py` (~200 LoC) — V1 .meg archive parser/extractor:
  - `parse_meg(path) -> list[MegEntry]` — read metadata only (header + filename table + file table)
  - `extract_one(path, name, out_dir)` — extract a single named file (case-insensitive)
  - `extract_all(path, out_dir)` — extract every file preserving the archive's directory layout
  - CLI entry: `python meg_parser.py <megfile> --list | --extract <name> <out_dir> | --extract-all <out_dir>`
  - V1 detection: relies on `flags == id` invariant (V2/V3 raise clear ValueError with iter-304 deferral note)
  - 7/7 smoke tests pass against synthetic V1 .meg files

## Iter-302 rule applied (engine-already-does-this audit)

**30-second grep audit performed first** per iter-302 codified rule:
- `grep -E '[Ii]con|[Tt]exture|[Ss]prite|[Tt]humbnail|[Ii]mage|DDS|GUI_Texture' docs/lua-api.md` → **0 hits**
- `grep -E '[Ii]con|[Tt]exture|[Ss]prite|[Tt]humbnail|MEG|MasterText' knowledge-base/alamo_engine_reference.md` → **0 hits**

Engine has no Lua API for icon/texture/.meg loading. **Honest break-out clause activates** → filesystem path is justified. Filed under iter-302's "filesystem wins when no engine Lua API exists" branch (same lineage as iter-299 GetCurrentMod + iter-300 ListMods).

## Iter-293 rule applied (iterative-deferral)

Per iter-293 codified rule (`feedback_iterative_deferral_keeps_velocity`): **ship Python prototype FIRST**, defer C# port.

- iter-304: Python prototype ✓ (this iter; ~200 LoC; 7/7 smoke pass)
- iter-305+: C# port deferred until algorithm proven against real SWFOC .meg files

The Python prototype already gives operators a complete CLI workflow:
```
python meg_parser.py "C:\Games\SWFOC\Data\MasterTextures.meg" --list --limit 20
python meg_parser.py MasterTextures.meg --extract "data\textures\icons\icon_atat.dds" /tmp/out
python meg_parser.py MasterTextures.meg --extract-all /tmp/extracted
```

## Format spec captured (V1 little-endian)

Extracted from `PetroglyphTools/PG.StarWarsGame.Files.MEG/.../V1/` reference C# library:

```
Header (8 bytes):
  uint32 NumFileNames   (must equal NumFiles for V1)
  uint32 NumFiles

Filename table (variable):
  for each file:
    uint16 NameLength
    char[NameLength] Name (ASCII)

File table (NumFiles * 20 bytes):
  for each file:
    uint32 Crc32
    uint32 Index
    uint32 FileSize
    uint32 FileOffset
    uint32 NameIndex

File data (variable):
  Concatenation of every file's bytes, referenced by FileOffset+FileSize.
```

V1 vs V2/V3 detection: V1 has `flags == id` (NumFileNames == NumFiles); V2/V3 have a magic number `0x3F7D70A4` at offset 4. SWFOC ships V1 only. iter-304 raises `ValueError` with clear message if V2/V3 is encountered, deferring V2/V3 support until evidence shows operators need it.

## Verification gates ALL GREEN

```
python _smoke_iter304.py:
  [PASS] parse_meg returns 3 entries with correct names
  [PASS] file sizes match seeded content
  [PASS] extract_one round-trips bytes correctly
  [PASS] extract_one is case-insensitive
  [PASS] extract_all preserves directory structure + content
  [PASS] V2/V3 detection raises clear ValueError
  [PASS] truncated-header detection raises ValueError
  === ALL 7 ITER-304 MEG_PARSER SMOKE TESTS PASSED ===

Bridge harness inherits 1100/0 from iter-300 (no bridge changes).
Editor build inherits 0/0 from iter-303 (no editor changes).
Verifier ledger lint inherits 0/0 at 318 entries.
Test file _smoke_iter304.py removed before close-out (no stray artifacts).
```

7 tests cover the full happy + error paths:
1. parse_meg returns correct count
2. parse_meg returns correct sizes
3. extract_one round-trips bytes
4. extract_one is case-insensitive (engine convention)
5. extract_all preserves directory structure (data/textures/file.dds)
6. V2/V3 detection raises clear ValueError
7. Truncated-header detection raises ValueError

## Multi-iter arc plan (iter-304 → iter-308)

- **iter-304** (this iter): Python parser MVP + smoke tests ✓
- **iter-305**: DDS decoder. SWFOC .meg files contain DDS image data. Need to decode DXT1/DXT3/DXT5 compressed textures into RGBA so we can produce PNG thumbnails. Two paths: `Pillow` (PIL) might handle DDS natively; if not, write a minimal DXT decoder (~150 LoC).
- **iter-306**: Thumbnail generator. Take a DDS, downscale to 64×64, save as PNG to a cache dir. Operator can browse via File Explorer or via a future editor tab.
- **iter-307**: Editor consumer. Decide between (a) a new "Asset Browser" tab or (b) inline icon column in Spawning tab's unit-type DataGrid. Probably (b) for highest operator value.
- **iter-308**: Live verify + close-out. Run against operator's actual `MasterTextures.meg` + extract a real unit icon + verify it displays in the editor.

## Honest scope acknowledgment

Per iter-302 break-out clause + iter-293 iterative-deferral:
- **iter-304 is a SCAFFOLDING iter**, not a feature-complete shipping iter. The .meg parser works on synthetic files; haven't yet verified against operator's real `MasterTextures.meg` (deferred to operator session because it requires their game install).
- **DDS decoding is the hard part** (iter-305). DXT decompression is well-known but ~150 LoC of bit-twiddling. If `Pillow` handles it, iter-305 is trivial; if not, iter-305 ships a minimal decoder.
- **Asset browser UI is a nice-to-have**, not a must-have. The CLI tool already provides operator value (extract icons → browse in File Explorer).

## What's NOT done in iter-304 (deferred)

- **Real SWFOC .meg verification** — needs operator's game install. CLI works on synthetic files; live verify deferred to operator session.
- **DDS decoder** — iter-305.
- **Thumbnail cache** — iter-306.
- **Editor UI consumer** — iter-307.
- **C# port** — iter-308+ if needed (Python CLI may be sufficient).
- **V2/V3 .meg support** — deferred indefinitely until operator demand emerges (SWFOC is V1).

## Pattern lessons

### *Reference-library audit + 30-sec format extraction* (NEW pattern)

The PetroglyphTools C# library was already cloned in `PetroglyphTools/`. A 5-minute read of `MegHeader.cs` + `MegFileTableRecord.cs` + `MegVersionIdentifier.cs` extracted the entire V1 binary spec. **Reference libraries dramatically accelerate format RE work** — the cost is 5 minutes of reading vs hours of binary inspection.

This pattern recurs across the loop: iter-287 used the savegame parser's reference docs; iter-275 used ImGui's reference impl; iter-304 used PetroglyphTools' reference impl. **Codification candidate** if it recurs once more (formal "reference-library-first" rule). For now, mental note.

### *Synthetic-input smoke testing for binary parsers*

iter-287 (savegame), iter-298 (integrity), iter-304 (.meg) all ship pure-function smoke tests against synthetically constructed binary inputs (build the input bytes in the test, parse them, assert the round-trip matches). **This pattern works because the parser doesn't need the live runtime to validate — it just needs bytes that match the format spec.**

Cost: ~80 LoC of smoke test setup.
Benefit: 7/7 confidence the parser works without needing a real SWFOC .meg file.
**Real-file verification still needed** (operator session) — synthetic tests prove the format implementation; real files prove the format spec is right. Both are needed; synthetic is the cheaper first step.

## Tasks queued

- **iter-305** (next): DDS decoder. Try `Pillow` first (likely supports DXT). If not, ship minimal DXT1/DXT3/DXT5 decoder. ~50-150 LoC depending on Pillow support.
- iter-306: Thumbnail generator + cache.
- iter-307: Editor UI consumer (Spawning tab unit-icon column).
- iter-308: Live SWFOC verify + arc close.

## Verification checklist

- [x] Iter-302 rule applied: 30-sec engine Lua API audit performed (0 hits → filesystem path justified).
- [x] Iter-293 rule applied: Python prototype FIRST, C# port deferred.
- [x] V1 format spec captured from PetroglyphTools reference.
- [x] `meg_parser.py` ships with parse + extract_one + extract_all + CLI.
- [x] 7/7 pure-function smoke tests pass (happy paths + error paths + V2/V3 detection).
- [x] Test file `_smoke_iter304.py` cleaned up before close-out.
- [x] Bridge harness inherits 1100/0; editor inherits 0/0; ledger lint 0/0.
- [x] Multi-iter arc plan documented (iter-305 → iter-308).
- [x] Honest scope acknowledgment (scaffolding iter, not feature-complete).
- [ ] Real SWFOC .meg verification — deferred to operator session.
- [ ] State docs synced.
- [ ] Task #555 marked completed; iter-305 (DDS decoder) queued.
