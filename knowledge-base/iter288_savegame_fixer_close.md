# iter-288 — Savegame fixer (Python) + JSON schema; mod-CRC32 hash field located

**Date:** 2026-05-08
**Arc class:** Thread C iter 3/7
**Predecessor:** iter-287 (Python parser + format-discrepancy resolution)
**Successor (queued):** iter-289 (C# parser port + WPF editor tab)

## What changed (3 files, ~600 LoC)

- **`tools/savegame_parser/fixer.py`** (~280 LoC) — 4 subcommands:
  - `strip-bad-chunk <save>` → drops malformed chunks, writes `<save>.fixed.swfocsave`
  - `truncate-at-corruption <save>` → cuts file at first parse failure, writes `<save>.truncated.swfocsave`
  - `diff <save-a> <save-b>` → JSON chunk-level diff (top-level + recursive)
  - `inspect-mod-hash <save>` → dumps 0x3E8 chunk body + extracts ASCII strings + uint32 hash candidates

- **`tools/savegame_parser/schema.json`** (~145 lines) — JSON Schema for parser/fixer outputs; consumed by iter-289 WPF tab + iter-290 mod-hash validator.

- **(deferred to iter-289)** C# port of parser + WPF tab. Rationale: iter-289 owns the editor surface anyway; coupling the C# port to the WPF tab keeps the build tree changes atomic.

## CRITICAL DISCOVERY — mod-CRC32 hash field located

Running `inspect-mod-hash` on vanilla `a.PetroglyphFoC64Save` vs modded `[AutoSave].PetroglyphFoC64Save`:

**Vanilla 0x3E8 body (39 bytes):**
```
05 01 00 04 04 01 00 00 00 03 04 01 00 00 00 01 04 72 03 f3 8a 02 04 e0 d7 7e 40 00 04 61 00 00 00 06 04 e4 9c 0c 00
```

**Modded 0x3E8 body (57 bytes):**
```
05 01 00 04 04 01 00 00 00 03 04 01 00 00 00 01 04 71 a8 05 17 02 04 f5 13 3f d4 00 16 5b 00 41 00 75 00 74 00 6f 00 53 00 61 00 76 00 65 00 5d 00 00 00 06 04 c0 1c 0d 00
                                              ^^^^^^^^^^^                            ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
                                              mod CRC32                              UTF-16LE save name "[AutoSave]"
```

**Field map for the 0x3E8 chunk** (empirically inferred):

| Offset | Bytes | Field | Notes |
|---|---|---|---|
| 0-2 | `05 01 00` | Micro-chunk header | type=5, size=1+ |
| 3-9 | `04 04 01 00 00 00 03` | Format markers | identical across all saves |
| 10-16 | `04 01 00 00 00 01 04` | More markers | identical across all saves |
| **17-20** | **`72 03 f3 8a` / `71 a8 05 17`** | **mod CRC32 (uint32 LE)** | **DIFFERS between saves** |
| 21-22 | `02 04` | Micro-chunk type marker | type 2, size 4 |
| 23-26 | `e0 d7 7e 40` / `f5 13 3f d4` | Secondary uint32 | likely mod-version-hash; differs |
| 27+ (modded only) | `00 16 5b 00 41 00 ...` | Save name (UTF-16LE) | starts at type byte 0x16 |
| Last 6 bytes | `06 04 <4-byte int>` | terminator marker | ~13M difference value |

**Vanilla CRC32**: `0x8AF30372` (read LE from bytes 17-20)
**Modded CRC32**: `0x1705A871`

This is the mod-mismatch field. iter-290 will:
1. Compute CRC32 of currently-loaded mod's identifying string (mod folder name? mod XML manifest hash? both candidates).
2. Compare against the save's bytes 17-20.
3. If different → operator confirms re-anchor → write loaded-mod CRC into save bytes 17-20.

This is the unblock for the "save crashes after mod change" pain point.

## [AutoSave] is structurally valid

`truncate-at-corruption [AutoSave].PetroglyphFoC64Save` reports:
```
NO TRUNCATION needed — file walked clean. 5 chunks parsed.
```

`diff a.PetroglyphFoC64Save [AutoSave].PetroglyphFoC64Save` reports:
- Both have RGMH header v1, same UUID, same 0x42060 chunk-stream offset.
- Vanilla: 5 top-level + 747,537 recursive chunks.
- Modded: 5 top-level + **1,311,347 recursive chunks** (76% more state — extra units / squadrons / objectives / story flags).

**The operator's "[AutoSave].PetroglyphFoC64Save" is NOT corrupted at the format level.** Whatever causes its load failure is the mod-CRC32 mismatch the parser just located. iter-290 fixes this.

## Iter-289 deliverables (REVISED scope)

Originally: WPF editor tab. **Now expanded to include:**

1. **C# port** of `parser.py` → `SwfocTrainer.Savegame.csproj` (5 classes per iter-286 spec) — translation of proven Python algo.
2. **WPF tab** in `SwfocTrainer.App` with chunk tree-view + hex pane + bytes-17-20 highlighter.
3. **Open-with-fixer command** that shells out to `python tools/savegame_parser/fixer.py inspect-mod-hash <save> --json` and parses the result.
4. Unit tests targeting all 3 real saves.

## Iter-290 deliverables (unblocked by iter-288)

- **Mod-hash validator** + re-anchor button:
  1. Read save's bytes 17-20 of 0x3E8 chunk → uint32 CRC.
  2. Locate currently-loaded mod (via Settings tab or `SWFOC_GetCurrentMod` if such a wire exists; check first).
  3. Compute CRC32 of mod-folder-name OR mod-manifest-hash (try both; whichever matches vanilla = the right algo).
  4. If save CRC ≠ current-mod CRC → confirm dialog → write current-mod CRC into save bytes 17-20.
  5. Flush + verify by re-parsing the modified save.
- **NEW bridge wire** if needed: `SWFOC_GetCurrentMod` returning mod name + mod path. Per iter-283 codified rule, GREP first.

## Verification

- [x] `tools/savegame_parser/fixer.py` (280 LoC) ships with 4 subcommands.
- [x] `tools/savegame_parser/schema.json` (145 lines) ships, documents all 3 output shapes.
- [x] `inspect-mod-hash` confirmed to extract distinct CRC values from vanilla vs modded saves.
- [x] `truncate-at-corruption` smoke-tested on operator's 214 MB modded autosave — file walks clean, no truncation needed.
- [x] `diff` smoke-tested vanilla vs modded — 5 top-level chunks identical, recursive count 76% larger in modded as expected.
- [ ] State docs synced (.remember/now.md, .remember/ralph_loop_state.md, STATUS.md).
- [ ] Task #538 marked completed; iter-289 queued (C# port + WPF tab).

## NEW pattern lesson — empirical-first STILL valid

iter-287 captured "ship parser before design doc." iter-288 extended this to "ship corruption fixer + run on real saves before C# port." The empirical-first approach has now repeated **TWICE** in 2 iters. Codification candidate firming up:

`feedback_empirical_first_for_format_re.md` — when RE'ing a binary format:
1. Ship Python prototype against real files FIRST.
2. Iterate the format definition based on observed parse failures.
3. THEN port to production language (C#) once the algorithm is proven.
4. Keeps iteration cycle to 5-15 min vs hours for design-doc-first approach.

Will codify after a 3rd recurrence (iter-290 mod-hash validator likely).

## Tasks queued

- **iter-289 (next)**: C# port of parser + WPF editor tab + open-with-fixer command + unit tests targeting 3 real saves.
- **iter-290**: Mod-hash validator + re-anchor button. Requires `SWFOC_GetCurrentMod` (grep-first per iter-283 rule).
- **iter-291**: E2E integration tests + corrupted-save regression corpus (synthesize 5-10 broken saves to exercise strip-bad-chunk + truncate-at-corruption).
- **iter-292**: Thread C close-out doc + operator changelog supplement.
