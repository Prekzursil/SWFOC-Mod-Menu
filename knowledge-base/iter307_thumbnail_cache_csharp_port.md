# iter-307 — C# read-side mirror of iter-306 thumbnail cache (Thread D iter 4 of 5)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (asset icons; Thread D arc, iter 4 of 5)
**Predecessor:** iter-306 (Python thumbnail generator + cache)
**Successor (queued):** iter-308 (Spawning tab unit-row icon column wiring)

## What changed (1 file new + 1 test file new; ~285 LoC; **21/21 pin tests PASS in 44 ms**)

- **NEW** `SWFOC editor/src/SwfocTrainer.Core/Assets/ThumbnailCache.cs` (~135 LoC) — read-only C# mirror of iter-306 Python `tools/asset_extractor/thumbnail_cache.py`.
  - `CacheRoot()` — resolves operator-local cache dir; mirrors Python EXACTLY (Windows `%LOCALAPPDATA%\swfoc_thumbnails\` / elsewhere `~/.cache/swfoc_thumbnails/` / `SWFOC_THUMB_CACHE` env override).
  - `ComputeCacheFilename(ddsPath, size=64)` — SHA256-hex + `_<size>.png` suffix; mirrors Python `<sha256>_<size>.png` shape EXACTLY so the Python writer + C# reader interop byte-for-byte.
  - `TryGetCachedPath(ddsPath, size, out cachePath)` — read-side lookup; returns `false` if cache file doesn't exist (no auto-generation; that stays Python's job until iter-308+ adds an in-editor decoder).
  - `GetCachedPathOrNull(ddsPath, size=64)` — convenience wrapper for WPF binding (null binding hides the icon naturally).
  - `SupportedSizes` constant matches Python `SUPPORTED_SIZES = (32, 48, 64, 96, 128, 256)` — drift on either side breaks pin tests.

- **NEW** `SWFOC editor/tests/SwfocTrainer.Tests/Core/Assets/Iter307ThumbnailCacheTests.cs` (~150 LoC) — 12 declared cases / **21 effective tests** after xUnit theory expansion (6 supported-sizes rows + 5 rejected-sizes rows count individually):
  - `CacheRoot_RespectsEnvOverride`
  - `ComputeCacheFilename_FormatMatchesPythonWriter` (locks `<sha256>_<size>.png` shape)
  - `ComputeCacheFilename_AcceptsSupportedSizes` × 6 (theory: 32/48/64/96/128/256)
  - `ComputeCacheFilename_RejectsUnsupportedSize` × 5 (theory: 33/65/0/-1/512)
  - `ComputeCacheFilename_ThrowsWhenDdsMissing`
  - `TryGetCachedPath_ReturnsFalse_OnCacheMiss`
  - `TryGetCachedPath_ReturnsTrue_WhenPythonWriterSeededCache`
  - `TryGetCachedPath_ReturnsFalse_WhenDdsDoesNotExist`
  - `TryGetCachedPath_DifferentSizes_MapToDifferentFiles`
  - `TryGetCachedPath_DifferentDdsContents_ProduceDifferentKeys`
  - `GetCachedPathOrNull_ReturnsNull_OnCacheMiss`
  - `GetCachedPathOrNull_ReturnsPath_OnCacheHit`

  All tests use the same `SWFOC_THUMB_CACHE` env override pattern as iter-306 Python `_smoke_iter306.py` (per-test tmp dir, restored on dispose).

## Iter-282 bidirectional infra-claim drift catch (direction B) — MAJOR FIND

**Iter-top 5-second grep audit revealed `SwfocTrainer.Meg` C# project ALREADY EXISTS** at `SWFOC editor/src/SwfocTrainer.Meg/`:
- `IMegArchiveReader.cs` interface
- `MegArchive.cs` (~80 LoC) with `TryReadEntryBytes` + `TryOpenEntryStream`
- `MegArchiveReader.cs`
- `MegEntry.cs` record (Path, Crc32, Index, SizeBytes, StartOffset, Flags)
- `MegOpenResult.cs`

**This is iter-283 codified rule, direction B fired again.** Pre-existing C# .meg infra would have been duplicated if iter-307 had aggressively ported the Python `meg_parser.py` work without grepping first.

**Recovery:** iter-307 stayed narrow — shipped only the cache-lookup layer that genuinely doesn't pre-exist in C#. Did NOT port .meg parsing because it's already there. Did NOT port DDS decoding because that work belongs in iter-308 (via WPF BitmapImage's WIC DDS support OR the existing C# library). iter-307's footprint is ~135 LoC + 150 LoC tests, all genuinely new.

**Cost-of-skip-grep**: ~200-400 LoC of duplicate `MegArchive` C# port + a merge-conflict iter when the operator inevitably opens both copies.
**Cost-of-do-grep**: 5 seconds.

This is the **3rd compound application of iter-283 codified rule** in the iter-296 → iter-307 mandate-expansion arc:
- iter-282 ORIGINAL discovery of `SWFOC_GetFireRateMultiplierGlobal` pre-existing
- iter-296+ implicit: bridge dispatcher patterns greppable before iter wrote anything
- iter-307 (this iter): `SwfocTrainer.Meg` pre-existing

## Iter-302 codified rule applied AT THE INFRASTRUCTURE-PROVENANCE LAYER

iter-302 originally said: prefer engine Lua API → filesystem → RVA pin.
iter-305 extended: also library API (Pillow).
iter-306 extended: also in-repo infra (iter-298 + iter-305).
**iter-307 extends FURTHER: pre-existing C# project (`SwfocTrainer.Meg`) takes precedence over `meg_parser.py` C# port.**

The decision tree is now 4 layers deep:
1. Engine Lua API (cheapest — DoString)
2. Established library (Pillow / WPF WIC / etc.)
3. In-repo infra from sibling iters
4. Pre-existing C# project in editor solution
5. … if all of the above absent, write new code

**6th instance of iter-302 — pattern is load-bearing.**

## Verification gates ALL GREEN

```
[run_editor_tests_v2] dotnet test --filter FullyQualifiedName~Iter307ThumbnailCache
Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 44 ms
```

- Editor build: GREEN (compiled SwfocTrainer.Core, SwfocTrainer.Tests, all dependents)
- Iter-307 pin tests: **Passed 21/21 in 44 ms** ✓
- Bridge harness: inherits 1100/0 (no bridge changes)
- Verifier ledger lint: inherits 0/0 at 318 entries (no ledger changes)
- Phase2HookPending count: 24 → 24 unchanged (no catalog changes)
- **Pre-existing CS8602 nullable warnings** in unrelated test files (Iter161/166/209/214/217 — not introduced by iter-307; surfaced by `--verbosity minimal` build, would require dedicated cleanup iter to drive to 0 per `feedback_test_host_clink` zero-warnings standard).

## What's intentionally NOT done in iter-307 (deferred to iter-308)

- **In-editor DDS decoding** — WPF `BitmapImage` may support DDS via WIC on Win10/11, OR the operator can pre-extract via Python CLI. Defer the decoder choice until iter-308 lands the UI consumer and the operator workflow is ironed out.
- **Spawning tab restructure** — currently `ObservableCollection<string>`; converting to `ObservableCollection<UnitTypeRow>` with `IconPath` is a cross-cutting refactor that needs its own iter scope (the filter/search/grouping logic all keys off strings).
- **MEG entry-name ↔ unit-type mapping** — convention is likely `i_button_<unit_type>.dds` in `MasterTextures.meg`, but verifying this against operator's actual game install is the iter-308 live-verify step.
- **Asset Browser tab** — out of scope for iter-307. Could land in iter-308 as a thin "preview .dds at path" tool if that's where Thread D arc finale settles.

## Pattern lessons

### *Pre-existing C# project audit (iter-302 layer 4)*
The editor solution is a multi-project structure (`SwfocTrainer.App` + `Core` + `Catalog` + `DataIndex` + `Flow` + `Helper` + **`Meg`** + `Profiles` + `Runtime` + `Saves`). When a new iter wants to add C# infra for a domain, **`ls src/` first** to check whether the project already exists. iter-307 caught the `Meg` project this way; saved hours of duplicate work.

### *Read/write split keeps responsibility clean*
iter-306 owns thumbnail GENERATION (Python: SHA256 + DDS decode + PIL.Image.thumbnail + PNG save).
iter-307 owns thumbnail LOOKUP (C#: SHA256 + cache filename build + File.Exists check).
Both sides compute the SAME SHA256 against the SAME bytes → SAME cache filename → editor reads what Python wrote, no coordination needed.

This works because SHA256 is deterministic across implementations. Python's `hashlib.sha256` and .NET's `System.Security.Cryptography.SHA256` produce byte-identical output for the same input. Pin test `ComputeCacheFilename_FormatMatchesPythonWriter` locks this contract.

### *Pin tests as Python↔C# interop contract*
The 12 iter-307 pin tests aren't just C# unit tests — they're a **wire-format contract** between Python iter-306 and C# iter-307. If anyone changes:
- The cache filename format (Python `_cache_filename` or C# `ComputeCacheFilename`)
- The hash algorithm (SHA256 → anything else)
- The supported sizes list (Python `SUPPORTED_SIZES` or C# `SupportedSizes`)
- The cache-root resolution rules (env override → platform default)

…on EITHER side, these tests fire. This is the iter-117/118/119 wire-format pin pattern applied to a Python↔C# boundary instead of a C#↔C++ bridge boundary.

## Verification checklist

- [x] iter-307 narrowed to read-side cache-lookup; defers DDS decode + .meg parse + Spawning-tab restructure
- [x] `SwfocTrainer.Meg` pre-existing C# project caught via 5-sec grep (iter-283 direction B 3rd application)
- [x] Cache-key shape pin-tested for byte-level interop with Python iter-306
- [x] `SWFOC_THUMB_CACHE` env override mirrored from Python (hermetic test isolation)
- [x] 12 declared / 21 effective pin tests covering: env override, format pin, supported sizes (theory × 6), unsupported sizes (theory × 5), missing DDS, cache miss, cache hit, missing DDS at lookup, size variation, content variation, null wrapper miss, null wrapper hit
- [x] Editor build GREEN (Core + Tests + dependents compiled cleanly with respect to iter-307 changes)
- [x] Iter-307 pin tests **Passed 21/21 in 44 ms**
- [ ] State docs synced (next step in this close-out)
- [ ] Task #558 marked completed; iter-308 (Spawning tab icon column) queued
