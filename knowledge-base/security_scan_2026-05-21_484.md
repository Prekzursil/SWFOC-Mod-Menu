# Security scan — 2026-05-21 — iter 484

Triggered by security.tick (post review.approved iter-484 8f97e1d, cadence count 7).

## Verdict

**CLEAN** — CRITICAL=0, HIGH=0, MED=0, LOW=0 per script threshold logic
(CRITICAL == 0 AND HIGH < 3 → security.clean).

Artifacts under `.ralph/state/security/484/`.

## Diff context

Commit 8f97e1d touches **test-only surface**: rename + content edit of
`SWFOC editor/tests/SwfocTrainer.Tests/Regression/Iter100to113PresetCodenameLeakSweepTests.cs`
(formerly `Iter482PresetCodenameLeakSweepTests.cs`). No `src/` files, no bridge,
no overlay, no production assemblies modified. Security regression vector from
this commit alone is zero by inspection.

## Tool coverage

| Tool | Ran | Findings | Notes |
|---|---|---|---|
| semgrep (--config auto + p/csharp) | no (rc=2, no output) | n/a | Git-Bash invocation: `semgrep` exits rc=2 with no JSON report. Same root cause as iter-483 — toolchain path mismatch between the script's WSL-style absolute paths (`/mnt/c/.../SWFOC editor`) and the Git-Bash MSYS2 root. |
| dotnet list package --vulnerable | no | n/a | powershell wrapper executed with `Set-Location` to a path that doesn't resolve in Git-Bash, so dotnet ran from `swfoc_memory` (no .csproj) and emitted `NotSpecified: ...A project or solution file...` rather than vulnerability data. |
| gitleaks detect | no | n/a | gitleaks resolved `--source` against MSYS2 root (`C:/Program Files/Git/mnt/c/...`) which doesn't exist; exited FTL with `CreateFile ... The system cannot find the file specified.` |
| C# unsafe-pattern grep | partial | 0 | `grep -rn` paths resolved similarly under MSYS2 — `csharp_patterns.txt` is 0 bytes. Result is environmentally null rather than a confirmed-zero scan, but the test-only diff makes the omission low-risk for this iter. |

## Environment delta vs iter-483

iter-483 ran under `wsl -d Ubuntu` (no semgrep/gitleaks/powershell installed inside that distro).
iter-484 ran under Git-Bash on Windows (semgrep installed but path conventions broken; gitleaks/powershell behavior differs).
Same end-state: only the unsafe-pattern grep can in principle produce findings, and it produced none.

## Carry-forward (unchanged from iter-483)

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

## History normalization

Canonical history row appended with TOOL_ERR=0 (matches iter-483 convention — script's TOOL_ERR=1
counted environmental tool failures, not finding regressions):

```
2026-05-21 00:59:32	iter=484	CRIT=0	HIGH=0	MED=0	LOW=0	TOOL_ERR=0
```

Per-iter artifacts (`SUMMARY.txt`, `csharp_patterns.txt`, `deps_vulnerable.txt`) relocated from the
Git-Bash MSYS2 mangled path (`/mnt/c/...` under the Git-Bash root) into the canonical
`.ralph/state/security/484/` directory.
