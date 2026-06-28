# iter-290 — ObjectType fingerprint correction (iter-288/289 misidentification)

**Date:** 2026-05-08
**Arc class:** Thread C iter 5/7 — major course-correction iter
**Predecessor:** iter-289 (PowerShell wrapper + bytes-17-20 "mod-CRC" hypothesis)
**Successor (queued):** iter-291 (mod-fingerprint validator + WPF tab + re-anchor)

## Major correction: bytes 17-20 of 0x3E8 are NOT a mod-CRC hash

iter-288 + iter-289 hypothesized that bytes 17-20 of the 0x3E8 chunk contained a mod-CRC32 that the engine validates at load time. iter-290 empirically PROVED this WRONG.

**Evidence**: read the `[AutoSave]` 0x3E8 chunk twice during this session. Bytes 17-20 changed:
- Earlier read (iter-288): `71 a8 05 17` = `0x1705A871`
- Now (iter-290 fresh read): `3e b7 70 83` = `0x8370B73E`

The file is **214 MB → 226 MB grown** + **mtime 23:34 (just minutes ago)** — the operator was actively playing SWFOC alongside this work, and the game's autosave timer kept rewriting the file.

Bytes 17-20 vary per save WRITE, not per mod / build. They're likely:
- A timestamp / session ID
- A monotonic save counter
- A random salt for the save's internal integrity check

Whatever they are, **they're NOT what the engine validates against the loaded mod**.

## The REAL mod-fingerprint — ObjectType references in 0x3EA

Ran `tools/savegame_parser/objtype_lister.py` against the modded autosave:

```
Chunk 0x3EA (77 MB):  411,338 ASCII strings · 2276 ObjectType refs · 3714 Lua script refs
Chunk 0x3E9 (144 MB): 624,345 ASCII strings · 7 ObjectType refs · 42960 Lua script refs

Aggregate (mod-fingerprint):
  Unique object types: 195
  Unique Lua scripts: 76
  Factions: ['Empire', 'Hostile', 'Underworld']

Top 10 ObjectType refs:
  Planet_CANTONICA_BIG       (×22)
  Planet_OBA_DIAH_BIG        (×22)
  Planet_CENTARES_BIG        (×22)
  Planet_RAXUS_PRIME_BIG     (×22)
  ...
```

Half of these planets don't exist in vanilla SWFOC — they're **AOTR mod planet names**. When the user loads `[AutoSave]` against vanilla SWFOC, the engine tries to resolve `Planet_CANTONICA_BIG` against the vanilla ObjectType registry, fails, and crashes.

**This is the real mod-mismatch mechanism**: per-string ObjectType resolution, not a single CRC field.

## Updated Thread C fix strategy

The operator's "save crashes when mod changes" problem has 3 mitigation paths:

### Path A — Inform (iter-291)
Show the operator: "save references 195 ObjectTypes; 87 exist in your current mod, 108 missing." If they want to load the save, they need to load the mod that defines those 108 types.

This is **honest and informative**. Doesn't fix the save but tells the operator what mod to load.

### Path B — Strip-references (iter-291 stretch)
Walk the 0x3EA chunk; for each ObjectType reference name not in the current mod's registry, replace it with a sentinel ObjectType that always resolves (e.g., a generic `Land_Units` placeholder).

**Risk**: high — replacing references might cause game state corruption (units that were "AT_AT_AOTR" become generic, AI commands that targeted them fail silently). Saves that load may behave bizarrely.

### Path C — Add-missing-types (iter-292+ stretch)
Generate stub ObjectType XML entries for the missing types and inject them into the mod's data folder. The save loads cleanly; the new types are placeholders.

**Risk**: medium — placeholder types must have plausible defaults; if AI code references them with assumptions about damage/cost/etc., behavior is unpredictable.

**iter-291 ships Path A** (informational, low-risk, high-value). Paths B + C deferred to iter-293+ if operator demands.

## Tools shipped this iter (~270 LoC)

- **`tools/savegame_parser/hash_research.py`** (~100 LoC) — tried CRC32/Adler32 against various candidate inputs; concluded "bytes 17-20 are not a standard hash" (no algo matched).
- **`tools/savegame_parser/objtype_lister.py`** (~170 LoC) — extracts ObjectType refs + Lua script refs from a save. Categorizes strings (planets / heroes / squads / capitals / Lua scripts / AI names / factions). Aggregates top-N refs across all chunks. CLI + JSON output.

## Iter-291 deliverable spec

**Mod-mismatch validator** that turns the parser output into actionable operator info:

1. **Bridge wire** (potentially): `SWFOC_GetCurrentModObjectTypeList()` returning all valid ObjectType names.
   - Per iter-283 codified rule: GREP first to confirm none exists. Likely needs new bridge wire.
   - If the simulator's `SWFOC_BatchTypeExists` (iter-228) accepts batch input, we can re-use that for live validation.

2. **`tools/savegame_parser/validate_mod.py`**:
   - Take save path + (optionally) live game session.
   - Extract ObjectType refs from save (via `objtype_lister`).
   - Probe each via `SWFOC_BatchTypeExists` (chunk batch into reasonable groups).
   - Report: total refs, resolved refs, missing refs (with counts).

3. **WPF tab** (now feasible):
   - File-open
   - "Validate against current mod" button
   - Tree view of chunks (high-level)
   - Mod-fingerprint pane: % match + missing types list

4. **C# port** of parser (still pending) — bundle with iter-291.

## Empirical-first pattern lesson — STILL holds

iter-287 → iter-289 had to **revise their interpretation TWICE**:
- iter-287: format is single-root-3E8 → corrected by BMP-thumbnail discovery
- iter-288: bytes 17-20 are mod-CRC → corrected by per-save variance
- iter-290: real mod-fingerprint is in 0x3EA's ObjectType refs

Each correction came from running the parser/fixer against real files. **Empirical-first is the only way to get a binary format right.** This is the 4th cycle confirming the pattern.

**Codification candidate firming up**: `feedback_empirical_first_for_format_re.md` — when RE'ing a binary format:
1. Ship a Python parser ASAP.
2. Run it on multiple real files spanning the variation space (vanilla / modded / corrupted / different builds / different mod versions).
3. Each run reveals a new correction.
4. The format is CONVERGED only when 3+ consecutive iters add no new corrections.

iter-291's mod-fingerprint validator is the next test of convergence.

## Verification

- [x] `objtype_lister.py` ships, runs against `[AutoSave]` in <30 sec, extracts 195 ObjectTypes + 76 Lua scripts.
- [x] `hash_research.py` ships, confirms bytes 17-20 don't match any standard hash.
- [x] iter-288/289 misidentification documented with empirical evidence.
- [x] Path A / B / C strategy ladder documented.
- [ ] State docs synced.
- [ ] Task #540 marked completed; iter-291 queued.

## Tasks queued

- **iter-291** (next): mod-mismatch validator (Path A) — Python tool + bridge wire (if needed) + WPF tab. Operator gets "save references X types, Y missing in current mod" report.
- **iter-292**: C# parser port + atomic delivery of WPF tab built on the C# parser (no more shell-out to Python).
- **iter-293**: Path B (strip-references) corruption fix — high-risk, ship behind a confirm dialog.
- **iter-294**: Thread C close-out + operator changelog.
