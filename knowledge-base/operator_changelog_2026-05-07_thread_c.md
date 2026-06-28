# Operator Changelog 2026-05-07 — Thread C savegame editor (CLI complete, iter 286-292)

## TL;DR

The SWFOC trainer now has a complete CLI savegame editor toolkit: inspect, validate, diff, and fix corrupted/mod-mismatched `.PetroglyphFoC64Save` files. Use it when a save crashes on load — typically because the active mod doesn't define the ObjectTypes the save references.

Tooling lives at `tools/savegame_parser/`. WPF tab integration is queued for iter-294+ but **all functionality works today via Python + PowerShell**.

## What you can do now

### Mode A — verify a save loads cleanly under the current mod

```powershell
# 1. From a running SWFOC + trainer, dump active mod's ObjectTypes via Lua Playground:
result = SWFOC_ListUnitTypes("")
# Save dump to e.g. C:\temp\active_mod_types.txt (one type per line)

# 2. Validate
python tools\savegame_parser\validate_mod.py `
    "$env:USERPROFILE\Saved Games\Petroglyph\Empire At War - Forces of Corruption\Save\YOUR_SAVE.PetroglyphFoC64Save" `
    C:\temp\active_mod_types.txt

# 3. Output verdict: MATCH | PARTIAL | FULL MISMATCH
```

### Mode B — identify which mod a save needs

```powershell
# Run objtype_lister directly to see the mod-fingerprint
python tools\savegame_parser\objtype_lister.py path\to\save.PetroglyphFoC64Save

# Top-30 ObjectType refs reveal mod identity:
#   Planet_*_BIG / Planet_*_BIG_ALIVE → AOTR / Awakening of the Rebellion
#   Planet_<NAME> (no suffix)         → vanilla SWFOC
#   Planet_*_ROTE / *_RiseOfTheEmpire → Rise of the Empire submod
#   Hero_<custom>                     → custom-hero mods
```

### Mode C — emergency strip-fix (HIGH RISK)

When the right mod is unavailable but you need to load the save anyway:

```powershell
# Dry-run first to preview replacements
python tools\savegame_parser\fixer.py strip-missing-types `
    path\to\save.PetroglyphFoC64Save `
    C:\temp\current_mod_types.txt `
    --dry-run

# Commit (writes <save>.stripped.swfocsave next to source)
python tools\savegame_parser\fixer.py strip-missing-types `
    path\to\save.PetroglyphFoC64Save `
    C:\temp\current_mod_types.txt
```

The stripped save loads against any mod that defines `Land_Units` (vanilla + every major mod). Game state is logically inconsistent with the original save (units that were `Planet_CANTONICA_BIG` become generic), but the save loads — better than losing it entirely.

### Operator-friendly PowerShell wrapper

If you don't want to remember Python flags:

```powershell
pwsh tools\savegame_parser\Inspect-Savegame.ps1 -Path .\save.PetroglyphFoC64Save -Action Diagnose
pwsh tools\savegame_parser\Inspect-Savegame.ps1 -Path .\save.PetroglyphFoC64Save -Action ModHash
pwsh tools\savegame_parser\Inspect-Savegame.ps1 -Path .\save.PetroglyphFoC64Save -Action StripBad
pwsh tools\savegame_parser\Inspect-Savegame.ps1 -Path .\save.PetroglyphFoC64Save -DiffWith .\other.save
```

## What the format actually is (empirically proven)

```
+------------------+ 0x000000
|  RGMH header     | 0x2028 fixed bytes (RGMH magic + version + UUID + label)
+------------------+ 0x002028
|  BMP thumbnail   | variable (~262200 B vanilla; size at 0x202A-0x202D)
+------------------+ 0x042060 typical
|  Chunk stream    | 5 top-level chunks:
|                  |   0x3E8 metadata (39B vanilla / 57+B modded)
|                  |   0x3EA primary state (10-78 MB) — ObjectType refs HERE
|                  |   0x3E9 AI/scripting state (9-131 MB) — Lua paths HERE
|                  |   0x3EB auxiliary state (0.5-4 MB)
|                  |   0x3EC terminator (14 B)
+------------------+ EOF
```

Saves embed the mod-context as ObjectType reference strings inside chunks 0x3EA + 0x3E9. The engine resolves these at load time against the loaded mod's registry; missing names cause crashes. **That's the actual mod-mismatch mechanism** — NOT a CRC field as the iter-288 hypothesis briefly suggested.

## 9 tools shipped

| Tool | Purpose |
|---|---|
| `parser.py` | Walk chunks (3 strategies + diagnose). |
| `fixer.py strip-bad-chunk` | Drop malformed chunks; preserve valid ones. |
| `fixer.py truncate-at-corruption` | Cut file at first parse failure. |
| `fixer.py diff` | Chunk-level diff between two saves. |
| `fixer.py inspect-mod-hash` | Dump 0x3E8 chunk body + ASCII strings. |
| `fixer.py strip-missing-types` | Replace missing ObjectType refs with placeholder. (NEW iter-292) |
| `objtype_lister.py` | Extract ObjectType refs + Lua scripts + factions = mod-fingerprint. |
| `validate_mod.py` | Compare save's fingerprint vs valid types list → MATCH/PARTIAL/MISMATCH. |
| `Inspect-Savegame.ps1` | Operator-friendly PowerShell wrapper invoking all of the above. |

## Pattern lessons captured during the arc

### 1. Empirical-first format RE (4 corrective cycles in 5 iters)

iter-287 → iter-290 each revised the format definition based on running the parser against real files:
- iter-287: BMP thumbnail wedged between header and chunk stream — initial format guess wrong.
- iter-288: top-level chunks ARE 0x3E8 etc. (agent #1 was right after BMP-skip).
- iter-289: vanilla saves have DIFFERENT bytes at 17-20 — field is per-save, not mod-CRC.
- iter-290: real mod-fingerprint is ObjectType refs in 0x3EA, NOT bytes 17-20 of 0x3E8.

Codified as `feedback_empirical_first_for_format_re.md` — when RE'ing a binary format, ship a Python parser ASAP and run it on multiple real files. IDA decompile tells you WHY; real files tell you WHAT.

### 2. Iterative deferral keeps velocity (6 iters of partial deliveries)

iter-287→292 each shipped operator-visible value while deferring the C# port. By iter-292 the CLI toolkit is functionally COMPLETE; the C# port is now optional UX polish, NOT a functionality gate.

Codified as `feedback_iterative_deferral_keeps_velocity.md` — when a deliverable has heavy prod-lang port + lighter scripting-lang prototype, ship the prototype every iter, defer the port to AFTER the algo is proven.

## Real-save test corpus

| File | Size | Type | Test purpose |
|---|---|---|---|
| `a.PetroglyphFoC64Save` | 33 MB | Vanilla 2024-01-19 | Baseline |
| `b.PetroglyphFoC64Save` | 23 MB | Vanilla 2024-03-07 | Cross-version vanilla |
| `[AutoSave].PetroglyphFoC64Save` | 214→227 MB | AOTR mod, mid-session | Mod-mismatch + active-write |

The `[AutoSave]` was actively rewritten by the running game during this work (file grew 13 MB in ~3 hours of iteration). All tools handle this gracefully — independent invocations re-read the file.

## Iter timeline

| Iter | Deliverable |
|---|---|
| 286 | RE design doc + 7-iter spec |
| 287 | Python parser + format-discrepancy resolution |
| 288 | Python fixer (4 subcommands) + JSON schema |
| 289 | PowerShell wrapper + README |
| 290 | iter-288/289 correction + ObjectType-lister |
| 291 | `validate_mod.py` end-to-end working |
| 292 | `strip-missing-types` end-to-end working — Thread C functionally complete |
| 293 (this) | Operator changelog + Thread C close-out + 2 memory rules |
| 294+ | Optional UX polish: C# parser port + WPF tab + standalone EXE |

## What's NOT shipped (intentional)

- **WPF Savegame tab** in the trainer — functionality is fully accessible via CLI; tab integration is iter-294+ if the operator wants in-trainer UX.
- **C# parser port** — Python toolkit is functionally complete; C# port becomes a translation exercise for editor integration.
- **`SwfocSavegameValidator.exe`** — standalone .NET wrapper, queued for iter-294+ for non-Python users.
- **Mod-hash CRC mechanism** — iter-288/289 hypothesized this; iter-290 proved bytes 17-20 are per-save-instance, not mod-id. The actual mod-fingerprint is content-level (ObjectType refs).

These deferrals are CHOICES — the savegame editor functionally works today.
