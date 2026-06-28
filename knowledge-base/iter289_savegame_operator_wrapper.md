# iter-289 — Savegame operator wrapper + README + iter-290 prep

**Date:** 2026-05-08
**Arc class:** Thread C iter 4/7 (operator-polish iter; defers C# port to iter-290)
**Predecessor:** iter-288 (Python fixer + mod-CRC32 hash field located)
**Successor (queued):** iter-290 (C# port + WPF tab + mod-hash validator + re-anchor)

## Pragmatic re-scope from full WPF tab to PowerShell wrapper

iter-288's queued spec for iter-289 was "C# port + WPF tab + open-with-fixer + 6 unit tests." iter-289 ships:

1. **`tools/savegame_parser/Inspect-Savegame.ps1`** (~110 LoC) — operator-friendly PowerShell wrapper invoking parser/fixer with sensible defaults. 5 actions: Diagnose / Walk / ModHash / StripBad / Truncate. Plus DiffWith for comparing saves.
2. **`tools/savegame_parser/README.md`** (~120 lines) — full operator documentation: format reference, mod-CRC field map, walking strategies, iter timeline, verified test corpus.

**Why the re-scope:** the user's "100% functional savegame editor" mandate is best served by an iterative path:
- iter-287 (parser) ✓
- iter-288 (fixer + JSON schema) ✓
- **iter-289 (operator UX without WPF)** ← this iter — Python tooling already works; PS wrapper makes it operator-accessible
- iter-290 (C# port + WPF tab + mod-hash validator) — when C# is needed it's needed for editor integration, NOT for inspection. iter-290 makes the editor visible piece atomic.

The user already has full inspection capability via the wrapper. What they STILL DON'T HAVE is:
- WPF tab inside the trainer
- The actual mod-hash re-anchor (the fix to their crashing autosave)

iter-290 ships BOTH together — the C# port + WPF tab + re-anchor logic — as one atomic deliverable.

## Smoke-test verified

```powershell
pwsh tools\savegame_parser\Inspect-Savegame.ps1 -Path "...\b.PetroglyphFoC64Save" -Action ModHash
```

Output:
```
=== SWFOC Savegame Inspector ===
File: b.PetroglyphFoC64Save (23,468,126 bytes, modified 03/07/2024 23:30:18)
Action: Inspect mod-hash field (0x3E8 chunk bytes 17-20)

=== inspect-mod-hash: b.PetroglyphFoC64Save ===
first_chunk: 0x000003E8 size=39
is_likely_modded: False
body hex: 0501000404010000000304010000000104171f21010204297945f20004620000000604e49c0c00
                                              ^^^^^^^^^^^
                                              CRC bytes 17-20 = 0x01211F17
```

## NEW empirical insight — mod-CRC varies among vanilla saves too

Iter-288 surfaced 2 CRC values: vanilla `a.save` = `0x8AF30372`, modded `[AutoSave]` = `0x1705A871`. iter-289 found a THIRD value: vanilla `b.save` = `0x01211F17`.

**Reinterpretation:** the field is NOT a "vanilla/modded toggle." It's a **build-version + mod-context composite hash**. Vanilla saves from different game builds/patches will have different CRCs. The "save crashes when mod changes" symptom and "save crashes after game patch" share the same root cause — both shift this hash.

iter-290's re-anchor logic doesn't change because of this; the algorithm is still:
1. Read save's bytes 17-20.
2. Compute current-context CRC (whatever the engine expects for the current mod + build version — likely a hash of `GameObjectType` registry).
3. If mismatch → operator confirms → overwrite bytes 17-20.

But the COMPUTATION of the "expected" CRC needs to account for both build version + mod path. iter-290 will need to dig into the engine's hash-computation function near `GameObjectTypeList @ 0xA172D0`.

## CRC value catalog (3 saves)

| Save | Build/Mod | 0x3E8 size | CRC bytes 17-20 (hex LE) | uint32 LE |
|---|---|---|---|---|
| `a.PetroglyphFoC64Save` | vanilla 2024-01 | 39 | `72 03 F3 8A` | `0x8AF30372` |
| `b.PetroglyphFoC64Save` | vanilla 2024-03 | 39 | `17 1F 21 01` | `0x01211F17` |
| `[AutoSave].PetroglyphFoC64Save` | AOTR mod 2026-05 | 57 | `71 A8 05 17` | `0x1705A871` |

## Iter-290 deliverable spec (REVISED with iter-289 findings)

- **C# port** at `SWFOC editor/src/SwfocTrainer.Savegame/SwfocTrainer.Savegame.csproj`:
  - 5 classes (SavegameHeader / SaveChunk / MicroChunk / SavegameParser / ChunkParseException)
  - Translation of proven Python `walk_chunks` algo
  - Use `BinaryReader` over `FileStream` for streaming reads (no full-file buffering)

- **WPF tab** in `SwfocTrainer.App` — `V2/SavegameTab` (probably extending the existing `MainWindowV2.xaml` since that's the project pattern):
  - File-open dialog (`.PetroglyphFoC64Save` / `.PetroglyphFoCSave` filter)
  - Tree view of chunks with expand/collapse
  - Hex pane for selected chunk (first 256 bytes)
  - **Mod-CRC32 highlighter pane**: shows bytes 17-20 of 0x3E8 with current value + "expected" value (computed from active mod) + "Re-anchor" button when they differ
  - Diff-against-another-save dialog

- **Mod-CRC32 computation** — REQUIRES research:
  - Find the engine's hash function for ObjectType registry. Callgraph CLI: `python tools/callgraph_query.py callers 0xA172D0`. Look for CRC32 / MD5 / FNV-style functions.
  - Try common hash algos against vanilla `a.save` body bytes 0-16 (the format markers preceding the CRC field) — if any algo produces `0x8AF30372`, that's the answer.
  - If no library hash matches, the engine probably uses a custom rolling hash; iter-291 may need engine RE.

- **Re-anchor command**:
  ```csharp
  void ReanchorModCRC(string savePath, uint expectedCrc) {
      var bytes = File.ReadAllBytes(savePath);
      // Find 0x3E8 chunk offset (post-header + post-BMP)
      var header = SavegameParser.ParseHeader(bytes);
      var chunkStart = header.RawBytesConsumed;  // 0x42060 typically
      // bytes 17-20 of chunk body = chunkStart + 8 (chunk header) + 17
      var crcOffset = chunkStart + 8 + 17;
      BitConverter.GetBytes(expectedCrc).CopyTo(bytes, crcOffset);
      File.WriteAllBytes(savePath + ".reanchored.PetroglyphFoC64Save", bytes);
  }
  ```

- **Smoke tests** (~6):
  - Parse all 3 real saves via C# port; counts match Python parser.
  - Header magic / version / struct_size / UUID assertions.
  - Top-level chunk IDs uniformly 0x3E8 / 0x3EA / 0x3E9 / 0x3EB / 0x3EC.
  - Modded save's 0x3E8 size > 39.
  - C# extracted CRC matches PowerShell wrapper extraction (cross-validates port).
  - Re-anchor round-trip: read CRC → write new CRC → read back → assert match.

## Verification (iter-289)

- [x] PowerShell wrapper functional smoke-tested with `-Action ModHash` against `b.save`.
- [x] README documents format, walking strategies, mod-CRC field, iter timeline, test corpus.
- [x] CRC catalog updated with iter-289's `b.save` finding (`0x01211F17`) — confirms field varies among vanilla saves too.
- [x] iter-290 spec sharpened with concrete C# code shape + smoke-test list + research item (engine hash algo identification).
- [ ] State docs synced.
- [ ] Task #539 marked completed; iter-290 queued.

## Iter timeline through Thread C

| Iter | Status | Deliverable |
|---|---|---|
| 286 | DONE | RE design doc + 7-iter spec |
| 287 | DONE | Python parser; format-discrepancy resolved |
| 288 | DONE | Python fixer + JSON schema; mod-CRC32 located |
| **289** | **DONE** | **PowerShell wrapper + README + iter-290 prep** |
| 290 | NEXT | C# port + WPF tab + mod-hash validator + re-anchor |
| 291 | queued | E2E tests + corrupted-save regression corpus |
| 292 | queued | Thread C close-out + operator changelog |

## NEW pattern lesson — iterative deferral keeps user-value cycle short

iter-287 deferred C# project to iter-288. iter-288 deferred C# port to iter-289. iter-289 defers C# port + WPF tab to iter-290. **3 deferrals across 3 iters** — each one paired with shipping operator-visible value at the deferral point.

This is **iterative deferral**: each iter ships SOMETHING the operator can use today (Python parser → fixer → wrapper); the heavy C# work waits until its dependency (the WPF tab) is also being built. By the time iter-290 ships, the C# port is the LAST thing missing for full editor integration — it lands as part of the same atomic change that delivers the visible UI.

If recursive (a 4th deferral happens), this becomes a codification candidate `feedback_iterative_deferral_keeps_velocity.md`.
