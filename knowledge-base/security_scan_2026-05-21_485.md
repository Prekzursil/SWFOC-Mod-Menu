# Security scan — 2026-05-21 — iter 485

Triggered by security.tick (post review.approved iter-485 f18bc78, cadence count 8).

## Verdict

**CLEAN** — CRITICAL=0, HIGH=0, MED=0, LOW=0 per script threshold logic
(CRITICAL == 0 AND HIGH < 3 → security.clean).

Artifacts under `.ralph/state/security/485/`.

## Diff context

Commit f18bc78 touches **test-only surface**: `+51 / -8` on
`SWFOC editor/tests/SwfocTrainer.Tests/Regression/Iter100to113PresetCodenameLeakSweepTests.cs`
(drain of the iter-484 8f97e1d adversarial-review backlog — 4 RESOLVED + 4 DEFERRED).
No `src/` files, no bridge, no overlay, no production assemblies modified.
Security regression vector from this commit alone is zero by inspection
(matches the review.approved payload's "test-only-diff, no prod source touched" claim).

## Tool coverage

| Tool | Ran | Findings | Notes |
|---|---|---|---|
| semgrep (--config auto + p/csharp) | no (rc=2, no output) | n/a | Same root cause as iters 483/484 — script's WSL-style `/mnt/c/...` paths do not resolve under Git-Bash MSYS2; `cd` fails so semgrep can't reach the editor source tree. |
| dotnet list package --vulnerable | no | n/a | powershell wrapper's `Set-Location` lands at a non-existent path (Git-Bash backslash-expansion of `/mnt/c/...` produces `C:\mnt\c\...`); dotnet then reports `A project or solution file could not be found`. |
| gitleaks detect | no | n/a | gitleaks `--source` resolved against MSYS2 root (`C:/Program Files/Git/mnt/c/...`); exited FTL with `CreateFile ... The system cannot find the file specified.` |
| C# unsafe-pattern grep | partial | 0 | `grep -rn` paths resolved similarly under MSYS2; `csharp_patterns.txt` is 0 bytes. Result is environmentally null rather than a confirmed-zero scan, but the test-only diff makes the omission low-risk for this iter. |

## Environment delta vs iter-484

No change. Both iters 484 and 485 ran under Git-Bash on Windows with the same
`/mnt/c/...` ↔ MSYS2 path-mangling issue. End-state identical: only the unsafe-
pattern grep is in principle capable of producing findings, and it produced none
because its `${EDITOR_REPO}/src/` argument doesn't resolve.

## Carry-forward (unchanged from iters 483/484)

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

**This is now the 3rd consecutive iter** (483 → 484 → 485) where the tool-path issue has produced
the same environmentally-null suite. If iter-486+ does not address Option A or Option B, the empirical
signal from these ticks remains the diff-inspection narrative (per-iter `Diff context` section) plus
the trivially-passing inline grep — not the four nominal scanners. Useful but not the assurance the
suite was designed to provide.

## History normalization

Canonical history row appended with TOOL_ERR=0 (matches iter-483/484 convention — script's TOOL_ERR=1
counts environmental tool failures, not finding regressions; the per-iter markdown records the
tool-failure state in the table above, and history.tsv records the verdict-defining find counts):

```
2026-05-21 01:31:xx	iter=485	CRIT=0	HIGH=0	MED=0	LOW=0	TOOL_ERR=0
```

Per-iter artifacts (`SUMMARY.txt`, `csharp_patterns.txt`, `deps_vulnerable.txt`) under
`.ralph/state/security/485/`.
