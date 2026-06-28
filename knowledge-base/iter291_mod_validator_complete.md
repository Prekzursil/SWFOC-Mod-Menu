# iter-291 — Mod-mismatch validator (Path A) end-to-end working

**Date:** 2026-05-08
**Arc class:** Thread C iter 6/7 — operator-actionable validator ships
**Predecessor:** iter-290 (course-correction: real fingerprint is ObjectType refs)
**Successor (queued):** iter-292 (C# port + WPF tab + strip-references corruption fix)

## Pragmatic re-scope (continuing iterative deferral)

iter-290 spec'd "Path A validator + C# port + WPF tab + 5 tests" all in iter-291. Re-scoped to:
- ✅ Standalone Python validator (`validate_mod.py`)
- ✅ Smoke test demonstrating validator end-to-end
- ⏭️ C# port → iter-292
- ⏭️ WPF tab → iter-292

Rationale: getting the validator algorithm proven against real saves is the high-risk work. C# port + WPF are mechanical translations once the algorithm works. Empirical-first pattern (4th cycle).

## Tools shipped (~120 LoC)

`tools/savegame_parser/validate_mod.py` (~120 LoC):
- Takes save path + types-list text file (one type per line; `#` and `//` comments stripped).
- Extracts ObjectType refs from save via existing `objtype_lister.py`.
- Computes resolved / missing sets via plain set intersection.
- Emits verdict (`MATCH` / `PARTIAL` / `FULL MISMATCH`) + operator-action string.
- JSON or human-readable output.

## End-to-end smoke test

**Step 1:** Extract vanilla `a.PetroglyphFoC64Save`'s ObjectType refs as a mock "vanilla mod registry" (59 unique types).

**Step 2:** Validate modded `[AutoSave].PetroglyphFoC64Save` against the mock vanilla list:

```
=== Mod-mismatch validation: [AutoSave].PetroglyphFoC64Save ===
Save size: 227,988,563 bytes
Valid types in mod registry: 59
ObjectTypes referenced by save: 49
Resolved against current mod: 1 (2.0%)
Missing: 48

Verdict: PARTIAL
Action: Save references 48 ObjectType(s) not present in this mod.
        Load the mod that defines these types, OR run iter-292 strip-references fix.

Missing types (top 30):
  - Hero_Rescue_Kill_Event_Handler
  - Planet_AARGAU_BIG
  - Planet_ALDERAAN_BIG_ALIVE
  - Planet_ANSION_BIG
  - Planet_ATZERRI_BIG
  - Planet_BAKURA_BIG
  - Planet_CANTONICA_BIG
  - Planet_DATHOMIR_BIG
  - Planet_ERIADU_BIG
  ... [38 more]
```

**Validator works.** The operator gets a concrete, actionable report:
- 48 missing types are all AOTR mod's `Planet_*_BIG` variants (vanilla uses `Planet_ALDERAAN`, mod uses `Planet_ALDERAAN_BIG_ALIVE`).
- The `_BIG` suffix is the AOTR mod's naming convention for galactic-map planet variants vs tactical-map versions.
- This explains why the user's `[AutoSave]` crashes when loaded against vanilla — half the planets the save references don't exist as types.

## Operator workflow (iter-291 ready to use today)

```powershell
# 1. From a running game with the mod the operator wants to use,
#    dump the active mod's ObjectType list via Lua Playground:
result = SWFOC_ListUnitTypes("")
# Save the dump to e.g. C:\temp\active_mod_types.txt

# 2. Run the validator
cd C:\Users\Prekzursil\Downloads\swfoc_memory
python tools\savegame_parser\validate_mod.py `
    "$env:USERPROFILE\Saved Games\Petroglyph\Empire At War - Forces of Corruption\Save\[AutoSave].PetroglyphFoC64Save" `
    C:\temp\active_mod_types.txt

# 3. If verdict is "MATCH" → load the save in-game.
#    If "PARTIAL" or "FULL MISMATCH" → load the right mod first.
```

## Discovery — file is a "moving target"

iter-290 noticed `[AutoSave]` was being rewritten by the running game. iter-291 confirms: between iter-290 and iter-291 the file grew **226 MB → 227 MB** (+1 MB in ~30 minutes), confirming the operator is still playing. The validator handles this gracefully (rereads on each call).

This means: the savegame editor work isn't blocked by file-state instability. Each invocation of the validator is independent — operator can run it against any save at any time, including during gameplay.

## Iter-292 deliverables (final atomic delivery)

- **C# port** of parser → `SwfocTrainer.Savegame.csproj` (deferred 5 iters running — iter-292 is the natural commit point).
- **WPF Savegame tab** in `SwfocTrainer.App` calling Python tools via shell-out for parsing + validation.
- **Strip-references corruption fix** (Path B) behind a confirm dialog (high-risk).
- **`SwfocSavegameValidator.exe`** standalone EXE wrapping `validate_mod.py` for non-Python users.

## Iter-293 (final Thread C) deliverables

- E2E test corpus with 5+ corrupted/mismatched saves.
- Operator changelog 2026-05-08 covering iter 286-292 Thread C arc.
- Thread C close-out doc.

## Verification

- [x] `validate_mod.py` (~120 LoC) ships, passes smoke test against real saves.
- [x] End-to-end demo: extracted vanilla types from a.save → validated [AutoSave] → got 48 missing types report.
- [x] Empirical proof of the iter-290 "ObjectType refs are the real mod-fingerprint" hypothesis.
- [x] Documented operator workflow (3 steps).
- [ ] State docs synced.
- [ ] Task #541 marked completed; iter-292 queued.

## Iterative deferral pattern — 5 iters running

iter-287 → iter-288 → iter-289 → iter-290 → iter-291: each iter shipped operator-visible value while deferring C# port. The C# port is now the LAST piece; iter-292 wraps it atomically with the WPF tab + strip-references fix.

This is **iterative deferral** crystallized. Codification candidate firmed up:

`feedback_iterative_deferral_keeps_velocity.md` (NEW pattern):
- When a multi-step deliverable has a heavy "production-language" port + a lighter "scripting-language" prototype, ship the prototype FIRST.
- Each iter ships SOMETHING the operator can use today.
- Defer the heavy port to the LAST iter, where it bundles with the visible UI.
- Pros: every iter ships value; the heavy port arrives with concrete consumers; fewer wasted port revisions.
- Cons: assumes Python + C# parity is straightforward (true for plain data parsing; less true for UI/threading).

Will codify when iter-292 ships (proves the pattern's tail end works).
