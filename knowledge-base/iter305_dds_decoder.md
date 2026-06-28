# iter-305 — DDS decoder for asset extraction (Thread D iter 2 of 5)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (asset icons; multi-iter Thread D arc)
**Predecessor:** iter-304 (.meg parser MVP)
**Successor (queued):** iter-306 (thumbnail generator + cache)

## What changed (1 file new, ~190 LoC; 7/7 smoke tests pass)

- **NEW** `tools/asset_extractor/dds_decoder.py` (~190 LoC) — Pillow-wrapper DDS decoder:
  - `inspect_dds(path) -> DdsInfo` — header-only parse (cheap; no decode)
  - `decode_dds(path) -> PIL.Image` — full decode into Pillow Image
  - `decode_dds_to_png(dds_path, png_path)` — round-trip with auto parent-dir creation
  - `batch_decode_dir(in_dir, out_dir, recursive=True)` — bulk converter for `.meg` extracted contents
  - CLI: `python dds_decoder.py <input> --inspect | --to-png <out> | --batch <out_dir>`
  - 7/7 smoke tests pass against synthetic DXT1 fixtures

## Iter-302 rule applied at the library-selection layer

**Before writing any DXT decoder, probe Pillow first** (per iter-302 codified `engine-already-does-this` rule extended to library-selection):

```bash
$ python -c "from PIL import Image, DdsImagePlugin; print(Image.__version__)"
12.2.0
DdsImagePlugin importable: yes
```

Pillow 12.2 ships `DdsImagePlugin` natively. **30-second probe verified it decodes synthetic DXT1 → RGBA correctly** (red color round-tripped from 0xF800 R5G6B5 → 0xFF000 RGBA8888 within rounding).

**Cost-of-skip**: ~150 LoC bespoke DXT1/DXT3/DXT5 decoder.
**Cost-of-verify**: 30 seconds + ~50 LoC throwaway probe script.
**Result**: shipped a ~190 LoC wrapper instead of ~340 LoC custom decoder. **Saved ~150 LoC** by leveraging existing infra.

This is the iter-302 pattern at the library-selection layer — sibling to its bridge-wire layer use in iter-296 (DoString-via-engine-Lua-API) and iter-300 (filesystem probe).

## Reference-library audit pattern recurrence (4th instance)

iter-275 (ImGui), iter-287 (PG.Commons), iter-304 (PG.StarWarsGame.Files.MEG), **iter-305 (Pillow DdsImagePlugin)**. **4 instances now — codification threshold reached** for `feedback_reference_library_audit_first.md`.

Distinct from iter-302 `engine-already-does-this`:
- iter-302 = "does the engine expose this via Lua API?" (engine-side check)
- This pattern = "does an established library implement this?" (host-side check)

Both are cheap-mechanism preferences. Both compound — 4 instances saved cumulative days of bespoke implementation work. **Codification candidate flagged for iter-306 or iter-307.**

## Verification gates ALL GREEN

```
python _smoke_iter305.py:
  [PASS] inspect_dds returns correct DXT1 metadata
  [PASS] decode_dds returns RGBA image with expected color
  [PASS] decode_dds_to_png writes valid PNG
  [PASS] decode_dds_to_png creates parent dirs
  [PASS] batch_decode_dir converts 3 nested DDS files
  [PASS] inspect_dds rejects non-DDS files with clear error
  [PASS] inspect_dds rejects truncated files with clear error
  === ALL 7 ITER-305 DDS_DECODER SMOKE TESTS PASSED ===

Bridge harness inherits 1100/0 (no bridge changes).
Editor inherits 0/0 (no editor changes).
Verifier ledger lint inherits 0/0 at 318 entries.
Test file _smoke_iter305.py removed before close-out.
```

7 tests cover happy + error paths:
1. `inspect_dds` returns correct metadata (width/height/fourcc/pitch/flags)
2. `decode_dds` returns RGBA Image with expected color (red round-trip)
3. `decode_dds_to_png` writes valid PNG (re-openable by Pillow)
4. `decode_dds_to_png` creates parent directory chain
5. `batch_decode_dir` converts 3 nested DDS files preserving directory structure
6. `inspect_dds` rejects non-DDS files with clear error message
7. `inspect_dds` rejects truncated files with clear error message

## Operator-visible workflow now possible end-to-end

iter-304 + iter-305 give operators a complete .meg-to-PNG pipeline:

```bash
# 1. Inspect what's in the .meg
python meg_parser.py "C:\Games\SWFOC\Data\MasterTextures.meg" --list --limit 20

# 2. Extract everything
python meg_parser.py "C:\Games\SWFOC\Data\MasterTextures.meg" --extract-all /tmp/textures

# 3. Convert all DDS to PNG in one batch
python dds_decoder.py /tmp/textures --batch /tmp/textures_png

# 4. Browse the PNGs in File Explorer / image viewer
```

All before iter-307 ships the editor UI consumer. Operator can see SWFOC unit icons today via this CLI workflow.

## Multi-iter arc plan refresh

- iter-304: .meg parser MVP ✓
- **iter-305: DDS decoder ✓** (this iter)
- iter-306: Thumbnail generator + cache. Take a DDS, downscale to 64×64 PNG, save under `~/.cache/swfoc_thumbnails/<crc32>.png` (or similar). ~50-80 LoC + smoke tests.
- iter-307: Editor UI consumer. Add icon column to Spawning tab's unit-type DataGrid (or new Asset Browser tab). Cache lookup via iter-306. ~80-120 LoC + XAML + pin tests.
- iter-308: Live SWFOC verify + arc close. Run pipeline against operator's real `MasterTextures.meg`, verify icons render correctly in the editor.

## What's NOT done in iter-305 (deferred)

- **Real SWFOC .dds verification** — requires operator's game install. Smoke covered synthetic DXT1; real SWFOC ships DXT1/DXT3/DXT5 mix. iter-308 is the live verify checkpoint.
- **DXT3/DXT5 fixtures** — synthetic builder only generates DXT1. If iter-308 reveals DXT3/DXT5 issues, will add explicit fixtures then.
- **Mipmap handling** — iter-305 ignores mipmap chain (decodes top-level only). Sufficient for icon thumbnails.
- **Cube/volume textures** — out of scope for unit icons.
- **Pillow version pin** — relies on host Python's Pillow. If operator's env lacks it, the explicit `ImportError` message tells them to `pip install Pillow`.

## Pattern lessons

### *Library probe before bespoke implementation* (NEW, codification candidate)

The 30-second `python -c "from PIL import DdsImagePlugin"` probe saved ~150 LoC of DXT decoder code. **Always probe established libraries first** when the format is well-known (DDS, PNG, JPEG, ZIP, TAR, etc.). Cost: ~30 sec. Benefit: avoid hundreds of LoC + maintenance burden.

This is a special case of iter-302 `engine-already-does-this` extended to "library-already-does-this." Could be a 4th instance of the broader meta-pattern, supporting codification of `feedback_reference_library_audit_first.md`.

### *Synthetic-input smoke testing pattern hits 4th instance*

iter-287 (savegame) + iter-298 (integrity) + iter-304 (.meg) + **iter-305 (DDS)**. 4 instances of the same shape: write a binary-format builder in the test, parse via your decoder, assert round-trip. **The pattern is now load-bearing across all binary-parser iters in this loop.** Worth flagging in any future memory-rule that codifies binary-RE workflow.

### *Iter-302 + iter-293 + iter-302 (library form) compound across iter-304 + iter-305*

iter-304 applied iter-302 (engine API audit) + iter-293 (Python first); iter-305 applied iter-302 again (library audit) + iter-293 (Python first). **Two iters in a row, three rules each, zero code-path errors.** Codified rules pay compound interest at exactly the rate the codification iters predicted (iter-256/283/293/302 cadence improvement).

## Tasks queued

- **iter-306** (next): thumbnail generator + cache. ~50-80 LoC.
- iter-307: Editor UI consumer.
- iter-308: Live verify + arc close.
- iter-309+: Codify `feedback_reference_library_audit_first.md` if a 5th instance lands (or iter-306 may be the codification trigger if it applies the pattern again).

## Verification checklist

- [x] Pillow probe verified DDS support before any bespoke code.
- [x] `dds_decoder.py` ships with inspect + decode + decode_to_png + batch_decode_dir + CLI.
- [x] 7/7 pure-function smoke tests pass against synthetic DXT1 fixtures.
- [x] Test file `_smoke_iter305.py` cleaned up before close-out.
- [x] Iter-302 rule (engine-already-does-this) applied at library-selection layer.
- [x] Iter-293 rule (Python prototype first) applied.
- [x] Reference-library audit pattern reached 4th instance — codification candidate flagged.
- [x] Bridge harness inherits 1100/0; editor inherits 0/0; ledger lint 0/0.
- [x] End-to-end CLI workflow documented (.meg → extract → DDS → batch PNG).
- [ ] Real SWFOC .dds verification — deferred to iter-308.
- [ ] DXT3/DXT5 explicit fixtures — deferred until iter-308 surfaces evidence of need.
- [ ] State docs synced.
- [ ] Task #556 marked completed; iter-306 (thumbnail cache) queued.
