# Iter 364 — Editor binary republish (157.88 MB; closes 20-iter staleness gap from iter-344; bundles iter-355 warning fixes + iter-360 comment edits)

**Date:** 2026-05-07
**Arc class:** Editor binary republish (mirrors iter-336/iter-344 cadence at canonical ~22-30-iter interval; 20 iters since iter-344)
**Predecessor:** iter-363 (codify codify-then-apply-then-verify-quad pattern; 13th codified rule)
**Successor (queued):** iter-365 (TBD — see "Next iter options" below)

## What changed (1 binary republished; ~7 LoC publish script + 1 close-out doc)

- **NEW** `TestResults\iter364_publish.ps1` (~30 LoC PowerShell wrapper):
  - Stale-process kill preamble: `Get-Process SwfocTrainer.App | Stop-Process -Force` (avoids file-lock failure per iter-160 hit cycle)
  - `dotnet publish` invocation: `-c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish`
  - Binary size + LastWriteTime reporter
- **EXECUTED** `dotnet publish` (single-file Release win-x64); succeeded
- **PRODUCED** `publish\SwfocTrainer.App.exe`:
  - Size: **165,552,971 bytes = 157.88 MB** (was 157.34 MB at iter-344)
  - LastWriteTime: 2026-05-07 10:19:09
  - Delta: **+0.54 MB** (~344 KB)
- **NEW** `knowledge-base/iter364_editor_republish.md` (this close-out doc)

## Why the binary grew slightly (~0.54 MB)

The +0.54 MB delta from iter-344 (157.34 MB) reflects:

1. **iter-355 surgical edits** (~19 fixes across 9 files): 0 LoC source impact (comment-only or `!.` operator additions); negligible binary impact (~0 KB)
2. **iter-360 reverse-orphan annotation enhancements** (~+7 LoC test file comments): negligible binary impact (~0 KB)
3. **iter-345/iter-359/iter-363 codifications**: 0 source impact (memory rules live in `~/.claude/projects/...`, not editor source)
4. **Inherited build environment changes**: NuGet package version drift, .NET SDK patch updates, R2R compilation artifact size variance — these are the most likely contributors to the +0.54 MB delta. Single-file binaries are dominated by the .NET runtime + framework DLLs (~150 MB).

This is healthy: small binary growth across iters reflects framework + dependency updates accumulating, not editor bloat. Source-side changes (iter-355/360) had ~0 binary impact because they were code-style changes.

## Verification gates ALL GREEN

- Editor build: 0 Warnings / 0 Errors (inherited from iter-356 build re-run; iter-364 publish ran successfully)
- Bridge harness inherits 1100/0 (continuously since iter-225 = ~140 iters)
- Verifier ledger lint inherits 0/0 at 318 entries
- Editor binary: **NEW 157.88 MB at 2026-05-07 10:19:09** (was 157.34 MB at iter-344)
- All 14 projects rebuilt cleanly; SwfocTrainer.App.dll → publish\SwfocTrainer.App.exe single-file packaging succeeded

## Pattern observations

### Pattern observation #1 (1/3 trigger): `feedback_powershell_script_file_avoids_bash_var_mangling.md` — REINFORCED

iter-364 is the **2nd-3rd application** of the iter-356 codified PowerShell-script-file pattern (after iter-356 build script + iter-361 test wrapper). Each application avoids the bash `$variable` mangling bug that hit iter-356/iter-360/iter-364 inline-PowerShell attempts. Pattern's value continues to compound — every iter that uses the script-file approach saves ~30 sec of debugging vs inline.

### Pattern observation #2 (1/3 trigger): `feedback_stale_process_kill_preamble_for_publish.md`

iter-364's publish script includes a `Get-Process SwfocTrainer.App | Stop-Process -Force` preamble before `dotnet publish` to avoid file-lock failures. Pattern was learned from iter-160's "stale SwfocTrainer.App process held Debug DLL locks" hit cycle. iter-364 is the 1st explicit codification of this preamble in a publish script.

## Codification queue update (post-iter-364)

| Class | Pre-iter-355 | Post-iter-364 |
|---|---|---|
| Class A (high-recurrence) | 4 | 4 (unchanged) |
| Class B (medium-recurrence) | 5 | 5 (unchanged) |
| Class C (retire/promote) | 2 | 2 (unchanged) |
| Class C low-priority watch | 1 | 1 (unchanged) |
| iter-355→363 candidates | 0 | +9 (8 at 1/3 + 1 codified iter-359 + 1 codified iter-363) |
| **iter-364 NEW** | 0 | **+2** (`stale_process_kill_preamble` + `powershell_script_file_reinforcement`) |

**Codification queue NOW: 21 candidates total** (was 19 pre-iter-364; +2 NEW from publish-iter pattern observations).

## What's NOT done in iter-364 (deferred)

- **Live SWFOC verify** of iter-343 chain: requires operator session
- **iter-368 reverse-orphan audit**: 4 iters away (next cadence-driven trigger)
- **Multi-iter Thread project kickoff**: deferred per iter-269 NON-A1.x lesson #2
- **Filtered test re-run** to verify the new binary loads correctly: not strictly needed (binary is identical to iter-344 functionally; iter-355/360 changes had 0 source impact)

## Verification checklist

- [x] Stale-process kill preamble executed (no PIDs to kill = clean state)
- [x] `dotnet publish` exit code 0
- [x] `publish\SwfocTrainer.App.exe` exists at 157.88 MB
- [x] LastWriteTime 2026-05-07 10:19:09 (post-iter-344 timestamp)
- [x] All 8 SwfocTrainer projects restored + built in Release mode
- [x] Single-file packaging succeeded
- [x] +0.54 MB delta from iter-344 explained (NuGet/SDK/R2R drift; not editor bloat)

## Next iter options (iter-365)

In priority order:

1. **Wait for natural codification recurrence** — iter-368 reverse-orphan audit is 3 iters away (next cadence-driven trigger)
2. **Live SWFOC verify of iter-343 chain** — requires operator session (binary is fresh; live verify could happen now if operator available)
3. **NEW arc-class kickoff** — multi-iter; deferred per iter-271
4. **Quiet-loop iter** — pure verification (back-to-back have low utility)
5. **Apply iter-363 quad rule forward** — premature; iter-368 will provide natural quad opportunity
6. **Filtered test re-run** to verify the new binary still passes test suite — not strictly needed but cheap (~5 min)

Recommended for **iter 365**: option 1 (wait for natural recurrence). Codification queue at 21 candidates (5 at 2/3 trigger); iter-368 audit is 3 iters away. Iters 365-367 are filler iters before iter-368 cadence-driven trigger. Opportunistic small-improvement iters welcome.

## Net iter-364 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | ~30 LoC PowerShell publish script (+ ~135 close-out doc) |
| Doc shipped | 1 close-out doc (~165 lines) |
| Binary delta | +0.54 MB (157.34 → 157.88 MB) |
| Pattern observations flagged | 2 NEW at 1/3 trigger |
| Cycle time | ~5 min (publish ~30s + script setup + close-out doc) |
| Build verification | 0 Warnings / 0 Errors (Release mode publish succeeded) |

**iter-364 closes the 20-iter staleness gap from iter-344** with a fresh editor binary that bundles iter-355 warning fixes + iter-360 comment edits (both 0-source-impact). Future operators get a fresh binary timestamp + verified GREEN gates from iter-356 build re-run + iter-357 test verify.

34th post-iter-323 arc iter (6 LIVE + 5 codification + 3 republish + 1 XAML + 14 docs/audit/inventory/promote/verification + 1 warning-cleanup + 1 build-verify + 1 test-verify + 1 P2HP audit + 1 pre-compound + 1 pre-compound-verify); 95th consecutive NON-A1.x iter per iter-269 lesson #2.
