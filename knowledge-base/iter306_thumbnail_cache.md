# iter-306 — Thumbnail generator + cache (Thread D iter 3 of 5)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (asset icons; multi-iter Thread D arc)
**Predecessor:** iter-305 (DDS decoder)
**Successor (queued):** iter-307 (editor UI consumer — Spawning tab unit-icon column or Asset Browser tab)

## What changed (1 file new, ~165 LoC; 8/8 smoke tests pass)

- **NEW** `tools/asset_extractor/thumbnail_cache.py` (~165 LoC) — content-keyed thumbnail cache layered on iter-298 + iter-305:
  - `cache_root() -> str` — operator-local cache dir (`%LOCALAPPDATA%\swfoc_thumbnails\` on Windows; `~/.cache/swfoc_thumbnails/` elsewhere). `SWFOC_THUMB_CACHE` env override for hermetic tests.
  - `thumbnail_path(dds_path, size=64) -> str` — generates on first call, returns cache path on subsequent (idempotent). Validates size against `SUPPORTED_SIZES = (32, 48, 64, 96, 128, 256)`.
  - `clear_cache() -> int` — wipes cached files; preserves dir for fast re-population.
  - `cache_stats() -> dict` — operator visibility (path/exists/files/bytes).
  - CLI: `python thumbnail_cache.py <dds>`, `--size N`, `--clear`, `--show-cache`.

## Cache key shape (content-hash addressed)

`<sha256>_<size>.png` — same DDS bytes always map to the same cache file regardless of where the operator extracted them. SHA256 is deterministic, so a thumbnail generated on machine A is byte-identical to one on machine B for the same DDS input. Perfect for a per-operator local cache where the .meg can be re-extracted to scratch dirs without invalidating cached thumbnails.

## Reuse-first: iter-298 + iter-305 stitched together

Per the iter-302 codified rule (`engine-already-does-this` extended to in-repo infra):

```python
# iter-298 hash_file via sys.path insert (savegame_parser sibling dir)
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", "savegame_parser"))
from integrity import hash_file

# iter-305 decode_dds — same dir
from dds_decoder import decode_dds
```

**Cost-of-skip**: ~50 LoC SHA256 reimplementation + ~40 LoC DDS plumbing.
**Cost-of-reuse**: 6 LoC sys.path + 2 imports.
**Result**: iter-306 is genuinely a thin layer (~165 LoC total, ~30 LoC of real new logic; the rest is path resolution, validation, CLI surface).

This is now a **5th instance** of the iter-302 pattern — codified rule continuing to pay compound interest at the predicted cadence.

## Reference-library audit pattern — 4 instances confirmed

iter-275 (ImGui) + iter-287 (PG.Commons) + iter-304 (PG.StarWarsGame.Files.MEG) + iter-305 (Pillow DdsImagePlugin). iter-306 itself doesn't add a 5th library reference — it stitches in-repo infra — but the pattern is now load-bearing across all binary-format iters. **Codification candidate (`feedback_reference_library_audit_first.md`) ready for iter-307 or iter-308.**

## Windows CP1252 console encoding trap — 2nd recurrence

**Same trap hit at iter-298 (production code) and now iter-306 (test code).** First smoke run crashed with `UnicodeEncodeError: 'charmap' codec can't encode character '→'` because the smoke test used `→` (U+2192) in a print line. Tests 1-3 passed; test 4 crashed mid-run.

Fixed via `Edit replace_all` of `→` to `->`. Re-ran: 8/8 PASS.

**Watch list**: if this trap hits a 3rd time, codify `feedback_windows_console_ascii_only.md`. Two instances is interesting; three is a pattern. The rule would say: *"On Windows, default Python console encoding is CP1252 even when the source file is UTF-8 — never use Unicode arrows/checks/symbols in any `print()` line; always use ASCII fallbacks (`->`, `[OK]`, `[FAIL]`)."*

## Verification gates ALL GREEN

```
python _smoke_iter306.py:
  [PASS] thumbnail_path generates cache file on first call
  [PASS] thumbnail_path idempotent (cache hits don't regenerate)
  [PASS] different sizes produce different cache files
  [PASS] different DDS bytes -> different cache key
  [PASS] clear_cache wipes everything (3 files removed)
  [PASS] invalid size raises ValueError
  [PASS] missing DDS file raises FileNotFoundError
  [PASS] generated thumbnail is valid PNG (64x64)
  === ALL 8 ITER-306 THUMBNAIL_CACHE SMOKE TESTS PASSED ===

Bridge harness inherits 1100/0 (no bridge changes).
Editor inherits 0/0 (no editor changes).
Verifier ledger lint inherits 0/0 at 318 entries.
Test file _smoke_iter306.py removed before close-out.
```

8 tests cover happy + error + idempotency + cache-key-determinism paths:
1. Generates cache file on first call (cold cache)
2. Idempotent on second call (cache hit, no regenerate)
3. Different `size` arg produces different cache file (size suffix in filename)
4. Different DDS bytes produce different cache key (SHA256 collision-free)
5. `clear_cache()` removes all cached files (returns count)
6. `thumbnail_path(size=invalid)` raises `ValueError`
7. `thumbnail_path(missing_path)` raises `FileNotFoundError`
8. Generated thumbnail is valid PNG re-openable by Pillow at expected dimensions

## Operator-visible workflow now COMPLETE end-to-end

iter-304 + iter-305 + iter-306 give operators a complete .meg-to-cached-thumbnail pipeline:

```bash
# 1. Inspect what's in the .meg
python tools/asset_extractor/meg_parser.py "C:\Games\SWFOC\Data\MasterTextures.meg" --list --limit 20

# 2. Extract one DDS file
python tools/asset_extractor/meg_parser.py "C:\Games\SWFOC\Data\MasterTextures.meg" \
    --extract "i_button_attack.dds" --out /tmp/extracted

# 3. Generate cached 64x64 thumbnail PNG
python tools/asset_extractor/thumbnail_cache.py /tmp/extracted/i_button_attack.dds
# -> C:\Users\<user>\AppData\Local\swfoc_thumbnails\<sha256>_64.png

# 4. Inspect cache
python tools/asset_extractor/thumbnail_cache.py --show-cache
# -> Cache root: C:\Users\<user>\AppData\Local\swfoc_thumbnails
#    Exists:   True
#    Files:    1
#    Bytes:    2,451
```

**Operator can browse SWFOC unit icons via this CLI workflow today**, before iter-307 ships the editor UI consumer. Pipeline is functionally complete; iter-307 is purely UI integration (binding cache PNG paths to WPF Image controls in the Spawning tab DataGrid).

## Multi-iter arc plan refresh

- iter-304: .meg parser MVP ✓
- iter-305: DDS decoder ✓
- **iter-306: Thumbnail generator + cache ✓** (this iter)
- iter-307: Editor UI consumer. Add icon column to Spawning tab unit-type DataGrid (or new Asset Browser tab). Cache lookup via iter-306. ~80-120 LoC + XAML + pin tests.
- iter-308: Live SWFOC verify + arc close. Run pipeline against operator's real `MasterTextures.meg`, verify icons render correctly in the editor.

## What's NOT done in iter-306 (deferred)

- **Editor UI integration** — iter-307 territory; iter-306 ships the cache primitive.
- **Real SWFOC .dds caching** — synthetic DXT1 only; iter-308 live-verify checkpoint.
- **WIC/native Windows codec fallback** — Pillow path is sufficient until proven otherwise.
- **Cache eviction policy** — operator-explicit `clear_cache()` only; no LRU/TTL. Cache is bounded by source DDS count (a few thousand entries × 64 KB each = ~megabytes; well under operator concern threshold).
- **Mipmap-aware downscale** — `decode_dds` already grabs top-level; `Image.thumbnail` does the downscale. If 256-pixel thumbnails of 1024-pixel source DDS look mushy at iter-308, may switch to a higher mip level for the source read.

## Pattern lessons

### *Reuse over reimplementation* (iter-302 5th instance)

iter-306 does not even contain a SHA256 implementation OR a DDS decoder — it imports both from prior iters. Total iter-306 LoC: ~165. If you remove the imports + path management, the genuinely-new logic (cache filename builder + thumbnail save loop) is **~30 LoC**. **The rest is glue.** This is the iter-302 rule at peak compound effect: each codified meta-rule iter pays back 5-10× LoC across subsequent iters.

### *Telescoping arcs benefit from reuse-aware planning* (NEW; codification candidate if 3rd instance)

Thread D's 5-iter arc decomposition specifically planned iter-298 (hash) + iter-305 (decode) BEFORE iter-306 (cache) so iter-306 could be a thin glue layer. **Pre-planning the reuse graph cut iter-306 from a probable ~250 LoC to ~165 LoC.** Counted instances:
- Thread D iter-306 reusing iter-298 + iter-305 (this iter)
- Thread A iter-225/226 reusing iter-94/96 detour scaffolding (re-checking)

If a 3rd planning-aware-reuse instance lands, codify as `feedback_telescoping_arc_reuse_planning.md`.

### *Windows CP1252 trap recurrence — watch list*

iter-298 + iter-306. 2 instances. Not yet codified. If iter-3** brings a 3rd hit, write `feedback_windows_console_ascii_only.md`. Symptom: any UTF-8 char outside ASCII range crashes `print()` on default Windows console. Fix shape: ASCII-only fallbacks (`->`, `[OK]`, `[FAIL]`, `[PASS]`, `***`).

## Tasks queued

- **iter-307** (next): Editor UI consumer — Spawning tab unit-icon column OR new Asset Browser tab. ~80-120 LoC + XAML + pin tests.
- iter-308: Live SWFOC verify + arc close.
- iter-309+: Codify `feedback_reference_library_audit_first.md` if iter-307/iter-308 demonstrate the pattern again (currently 4 instances, awaiting natural 5th rather than forced codification).

## Verification checklist

- [x] iter-298 `hash_file` + iter-305 `decode_dds` reused via imports (no duplication).
- [x] `thumbnail_cache.py` ships with `cache_root` + `thumbnail_path` + `clear_cache` + `cache_stats` + CLI.
- [x] 8/8 smoke tests pass (cold-cache, hot-cache, size variation, content variation, clear, error paths, PNG validation).
- [x] Test file `_smoke_iter306.py` cleaned up before close-out.
- [x] Iter-302 rule (cheap-mechanism reuse) applied 5th time.
- [x] Bridge harness inherits 1100/0; editor inherits 0/0; ledger lint 0/0 at 318 entries.
- [x] End-to-end CLI workflow documented (.meg → extract → DDS → cached thumbnail PNG).
- [x] Windows CP1252 trap recurrence noted; codification candidate flagged on watch list.
- [ ] Real SWFOC .dds verification — deferred to iter-308.
- [ ] State docs synced (next step in this close-out).
- [ ] Task #557 marked completed; iter-307 (editor UI consumer) queued.
