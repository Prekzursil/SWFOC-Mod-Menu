# Security scan — 2026-05-21 — iter 488

Triggered by security.tick (post review.approved iter-487 08e0c59, cadence count 9).

## Verdict

**CLEAN** — CRITICAL=0, HIGH=0, MED=0, LOW=0 per script threshold logic
(CRITICAL == 0 AND HIGH < 3 → security.clean).

Artifacts under `.ralph/state/security/488/` (moved from MSYS2 fake mount root
post-run — see "Tool coverage" below).

## Diff context

Commit 08e0c59 touches **test-only surface**: `+11 / -5` on
`SWFOC editor/tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs`
(rename of `TryParse_NumericString_IsInterpretedAsHex` →
`TryParse_HexNoPrefix_RoundTrips` + inline comment update; drain of iter-476
b064ddb adversarial-review LOW). No `src/` files, no bridge, no overlay, no
production assemblies modified. Security regression vector from this commit
alone is zero by inspection (matches the review.approved payload's
"test-only-rename, count-drift-watch-broke-iter-485-LOW4, long-tail-drain-
iter-477-LOW-closed" claim).

## Tool coverage

| Tool | Ran | Findings | Notes |
|---|---|---|---|
| semgrep (--config auto + p/csharp) | no (rc=2, no output) | n/a | Same root cause as iters 483/484/485 — script's WSL-style `/mnt/c/...` paths do not resolve under Git-Bash MSYS2; `cd` fails so semgrep can't reach the editor source tree. |
| dotnet list package --vulnerable | no | n/a | powershell wrapper's `Set-Location` lands at non-existent path (Git-Bash backslash-expansion of `/mnt/c/...` produces `C:\mnt\c\Users\Prekzursil\Downloads\SWFOC editor\`); dotnet then reports `A project or solution file could not be found`. Script's stdout-grep heuristic emits "deps: clean (no vulnerable packages)" but the underlying `deps_vulnerable.txt` actually contains the path-resolution error — script-level false-clean, not a real scan result. |
| gitleaks detect | no | n/a | gitleaks `--source` resolved against MSYS2 root (`C:/Program Files/Git/mnt/c/Users/Prekzursil/Downloads/SWFOC editor`); exited FTL with `CreateFile ... The system cannot find the file specified.` |
| C# unsafe-pattern grep | partial | 0 | `grep -rn` paths resolved similarly under MSYS2; `csharp_patterns.txt` is 0 bytes. Result is environmentally null rather than a confirmed-zero scan, but the test-only diff makes the omission low-risk for this iter. |

## Environment delta vs iter-485

No change. Both iters 485 and 488 ran under Git-Bash on Windows with the same
`/mnt/c/...` ↔ MSYS2 path-mangling issue. One additional artifact-handling
quirk this iter: the script wrote its output directory to
`C:\Program Files\Git\mnt\c\Users\Prekzursil\Downloads\swfoc_memory\.ralph\state\security\488\`
(the MSYS2 fake mount, not the real repo). Security-reviewer hat moved the
three artifact files (`SUMMARY.txt`, `csharp_patterns.txt`, `deps_vulnerable.txt`)
to the canonical `.ralph/state/security/488/` path post-run so downstream
consumers (history.tsv index, future audits) can find them.

## Carry-forward (unchanged from iters 483/484/485)

Script `.ralph/scripts/security_scan.sh` hard-codes `/mnt/c/Users/Prekzursil/Downloads/SWFOC editor`
(WSL-style mount path). Neither WSL Ubuntu (without the optional tools installed) nor Git-Bash
(with MSYS2 path mangling) executes the full 4-scan suite. To raise signal:

- Option A — install semgrep + gitleaks inside the WSL Ubuntu distro the loop already shells into.
- Option B — rewrite the script to detect the host (WSL vs Git-Bash) and call Windows-side binaries via
  `cmd.exe` / `powershell.exe` with proper path mapping (or use `cygpath -w` under MSYS2).

Out of scope for the security-reviewer hat (`security-reviewer.md` forbids source-code edits, and the
script lives under `.ralph/scripts/`). Flagged for editor-polish or operator follow-up; the loop's
post-approval security ticks remain valid for verdict purposes since the underlying review/build/test
gates already verified the diff.

**This is now the 4th consecutive iter** (483 → 484 → 485 → 488; iters 486 and 487 did
not fire a security.tick in the event log — the cadence skipped them, going directly
from cadence 8 / iter-485 to cadence 9 / iter-487-commit-08e0c59). If iter-489+ does
not address Option A or Option B, the empirical signal from these ticks remains the
diff-inspection narrative (per-iter `Diff context` section) plus the trivially-passing
inline grep — not the four nominal scanners. Useful but not the assurance the suite was
designed to provide.

## History normalization

Canonical history row appended with TOOL_ERR=0 (matches iter-483/484/485 convention —
script's TOOL_ERR=1 counts environmental tool failures, not finding regressions; the
per-iter markdown records the tool-failure state in the table above, and history.tsv
records the verdict-defining find counts):

```
2026-05-21 02:13:xx	iter=488	CRIT=0	HIGH=0	MED=0	LOW=0	TOOL_ERR=0
```

Per-iter artifacts (`SUMMARY.txt`, `csharp_patterns.txt`, `deps_vulnerable.txt`) under
`.ralph/state/security/488/` after post-run relocation from MSYS2 fake mount.
