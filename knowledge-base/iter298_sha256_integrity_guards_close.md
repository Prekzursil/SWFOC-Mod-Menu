# iter-298 — SHA256 integrity guards in repair workflows

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (savegame integrity verification per iter-294 audit)
**Predecessor:** iter-297 (L3 stub-XML injection)
**Successor (queued):** iter-299 (`SWFOC_GetFactionRoster` + `SWFOC_GetCurrentMod` bridge wires)

## What changed (1 file new, 2 files extended; ~300 LoC total)

- **`tools/savegame_parser/integrity.py`** (NEW, ~210 LoC) — utility module for SHA256 integrity verification:
  - `hash_file(path)` — streaming SHA256 of file content (1 MiB buffer for large saves)
  - `hash_directory_recursive(path) -> (hex_digest, file_count)` — merkle-style aggregate; deterministic via sorted walk; mixes relative paths into hash so renames are detected
  - `IntegritySnapshot.capture(save_path, mod_dir, extra_files)` — frozen dataclass snapshot of save + optional mod folder + optional extra files
  - `format_integrity_report(before, after, op_name)` — operator-facing verification block
  - `integrity_dict(before, after, op_name)` — JSON-serializable variant
- **`tools/savegame_parser/stub_xml_generator.py`** (extended, +30 LoC) — `--verify-integrity` + `--active-mod-dir` flags. Captures pre-snapshot before any I/O, post-snapshot after, emits report block (or nests integrity dict inside JSON manifest).
- **`tools/savegame_parser/fixer.py`** (extended, +50 LoC) — same flags via shared `_add_integrity_flags(parser)` helper applied to 3 write-side subcommands (`strip-bad-chunk`, `truncate-at-corruption`, `strip-missing-types`). Read-only `diff` and `inspect-mod-hash` skipped — they never write.

## Why integrity guards matter

iter-292 (strip-fix v1) and iter-297 (stub injection v2) both **claim** "save untouched" + "active mod untouched". Without verification, those claims are operator faith. iter-298 makes the claims structurally auditable:

```
=== Integrity verification ===
Operation: stub_xml_generator

Save file:
  Before: SHA256=ab12cd34...  size=2,048,392  mtime=...
  After:  SHA256=ab12cd34...  size=2,048,392  mtime=...
  Verdict: UNCHANGED [OK]

Active mod folder:
  Before: 47 files, recursive SHA256=def456...
  After:  47 files, recursive SHA256=def456...
  Verdict: UNCHANGED [OK]
```

If a future change to the repair tools accidentally introduces a write to either path, the verdict flips to `MODIFIED [FAIL]` and the operator sees the breakage immediately.

## Threat model (in scope vs out)

**In scope** (what iter-298 catches):
- Did THIS TOOL accidentally modify the save file? → no (verdict UNCHANGED)
- Did THIS TOOL accidentally modify the active mod folder? → no (verdict UNCHANGED)
- Did the tool report success but actually corrupt the save? → caught (verdict MODIFIED)

**Out of scope** (what iter-298 does NOT catch):
- External tampering between pre-hash and post-hash (operator concern, not tool concern)
- Cryptographic authentication / signing (operator-side trust suffices for local repairs)
- Mod files matching some "known good" baseline (we don't ship fingerprints; that's a future feature)

The guards measure **pre-vs-post within ONE invocation**, not historical state. This is the right semantic: operators verify their own tool runs, not anti-tamper protection.

## Verification

### Pure-function smoke (10/10 PASS)

```
[PASS] hash_file deterministic + correct sha256        (verifies vs. NIST b94d27b9... fixture)
[PASS] hash_file detects modification                  (b'hello world' -> b'hello world!' differs)
[PASS] hash_directory_recursive deterministic          (3 files, identical hash twice)
[PASS] IntegritySnapshot.capture                       (size + file_count fields populated)
[PASS] no-op produces UNCHANGED verdict                (capture-no-op-capture)
[PASS] save modification detected                      (mod folder still UNCHANGED)
[PASS] mod folder modification detected                (changing one XML breaks aggregate)
[PASS] rename detected (path is mixed into hash)       (same content, different path -> different hash)
[PASS] format_integrity_report contains expected verdicts
[PASS] integrity_dict serializes to JSON
=== ALL 10 INTEGRITY MODULE SMOKE TESTS PASSED ===
```

### E2E integration smoke (4/4 PASS)

```
[PASS] generator produced 2 stubs successfully
[PASS] integrity block present + mod UNCHANGED verdict: UNCHANGED [OK]
[PASS] sidecar mod folder created (3 files)
[PASS] within-run integrity correctly reports UNCHANGED even after external pre-edit
=== ITER-298 E2E INTEGRATION TESTS PASSED (4/4) ===
```

### Pattern bug caught + fixed mid-iter

Initial implementation used Unicode checkmark `✓` and cross `✗` in verdict text. This crashed on Windows CP1252 console with `UnicodeEncodeError`. Fixed by replacing with ASCII `[OK]` / `[FAIL]` markers. **Lesson**: when generating operator-facing text on Windows, default to ASCII-safe characters. Any non-ASCII Unicode requires `PYTHONIOENCODING=utf-8` set globally — not a constraint to push onto operators.

This is a recurrence of the **iter-282 wire-format-canonical alignment** principle: when adding a new format/encoding, check the consumption-side constraints first. The "consumption side" here is Windows console default encoding.

## Inherited gates GREEN

- Bridge harness inherits iter-297 1100/0 (no bridge changes)
- Verifier ledger lint inherits 0/0 at 318 entries
- Editor build inherits iter-296 0 warnings / 0 errors (no editor changes)
- iter-297 stub generator + iter-292 strip-fix continue to function (extended additively, no breaking changes to existing CLI surface)

## Pattern lessons

### *Sidecar-additive* recurrence (3rd instance — close to codification)

iter-292 strip-fix mutates SAVE side via copy. iter-297 stub injection mutates MOD side via addition. iter-298 doesn't mutate at all (it's a verification layer that wraps existing operations). All three preserve operator data through additive output.

This is now load-bearing across 3 iters of Thread C. **iter-299 or later: codify as `feedback_sidecar_additive_pattern.md` memory rule** if a 4th instance arrives — current threshold is ready, just need one more recurrence to confirm the abstraction is general.

### *Within-run-snapshot* semantics

iter-298 measures pre-vs-post **within one invocation**. This is the right semantic for verifying tool behavior, but it's a different threat model from anti-tamper. Worth noting because operators may misunderstand: running the tool twice with no changes between runs is NOT verifying historical integrity, it's verifying *that this new run didn't change things*. Anti-tamper would require persisted snapshots + a baseline.

### *Encoding-defaults trap* (NEW pattern)

Windows console default encoding (`cp1252`) silently breaks any non-ASCII output unless `PYTHONIOENCODING=utf-8` is set. For operator-facing CLI tools targeting Windows, **default to ASCII-safe markers in stdout**. Use Unicode/emoji only when:
- Running in a terminal we control (where we can set encoding)
- Writing to a file that we explicitly open with `encoding="utf-8"`

This wasn't a memory-rule-worthy lesson (well-known Python-on-Windows gotcha) but worth surfacing here.

## Tasks queued

- **iter-299** (next): `SWFOC_GetFactionRoster` + `SWFOC_GetCurrentMod` bridge wires. Per iter-294 Audit B: these are 2 of the 6 missing enumeration wires. Pattern matches iter-296 `SWFOC_GetPlanets` (DoString-driven enumeration via existing engine Lua API). ~50-80 LoC bridge additions + catalog flips.
- iter-300: `SWFOC_ListMods` + Settings UI mod-picker.
- iter-301+: Asset/icon extraction kickoff (.meg parser + DDS decoder).
- iter-302+ (when 4th sidecar-additive instance lands): codify `feedback_sidecar_additive_pattern.md`.

## Verification checklist

- [x] `integrity.py` ships with full type annotations + frozen dataclass + 10/10 smoke pass.
- [x] `stub_xml_generator.py --verify-integrity` works end-to-end (tested with synthetic mod folder + missing-types list).
- [x] `fixer.py --verify-integrity` flag wired into 3 write-side subcommands via shared helper.
- [x] Read-only subcommands (`diff`, `inspect-mod-hash`) correctly skip integrity flags (no false integrity claims for read paths).
- [x] Windows CP1252 console encoding bug caught + fixed mid-iter.
- [x] E2E test confirms: 2-stub generation + integrity verdict UNCHANGED + sidecar mod created (3 files).
- [x] Within-run-snapshot semantics validated (external mod edit before tool run does NOT cause MODIFIED verdict — tool's job is to verify *its own* run).
- [x] Test files cleaned up (no `_smoke_*.py` / `_iter298_e2e.py` left in tools dir).
- [ ] Live SWFOC verify — deferred to operator session (no live game changes; build clean, all smoke pass).
- [ ] State docs synced (next).
- [ ] Task #549 marked completed; iter-299 queued.
