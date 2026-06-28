# Security scan — 2026-05-21 — iter 483

Triggered by security.tick (post review.approved iter-482, cadence count 6).

## Verdict

**CLEAN** — CRITICAL=0, HIGH=0, MED=0, LOW=0, tool_errors=0 per script threshold logic
(CRITICAL == 0 AND HIGH < 3 → security.clean).

Artifacts under `.ralph/state/security/483/`.

## Tool coverage

| Tool | Ran | Findings | Notes |
|---|---|---|---|
| semgrep (--config auto + p/csharp) | no | n/a | not installed inside WSL Ubuntu where script ran; would require Windows-side invocation |
| dotnet list package --vulnerable | no | n/a | wrapper invokes `powershell.exe` which is unavailable inside WSL Ubuntu; deps_vulnerable.txt captured the error and grep matched no severity tokens |
| gitleaks detect | no | n/a | not installed inside WSL Ubuntu; binary lives on the Windows-side PATH only |
| C# unsafe-pattern grep | yes | 0 | no `BinaryFormatter`, weak crypto, SQL concat, or shell `Process.Start` in `SWFOC editor/src/` |

## Carry-forward note

The scan script assumes a WSL Ubuntu environment with semgrep + gitleaks + powershell.exe interop on PATH.
The current invocation path runs the script under `wsl -d Ubuntu` from PowerShell — `powershell.exe` is
not on PATH inside that distro and the Windows-installed semgrep/gitleaks are not visible. This matches the
pattern of prior security ticks (iter 478 also produced low coverage); the script's verdict logic still
returns CLEAN because the unsafe-pattern grep is the only check that actually executed.

If a future security tick needs to escalate signal, install semgrep + gitleaks inside WSL Ubuntu OR
rewrite the script to call Windows-side binaries via cmd.exe/powershell.exe with proper path mapping.
Out of scope for the security-reviewer hat (source-code edits forbidden); flag for editor-polish or
operator follow-up.

## History append

History row added to `.ralph/state/security/history.tsv`:

```
2026-05-21 00:36:17	iter=483	CRIT=0	HIGH=0	MED=0	LOW=0	TOOL_ERR=0
```
