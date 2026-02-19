# SWFOC Agent Operating Contract

## Purpose
This repository uses an evidence-first AI engineering workflow for SWFOC runtime/mod reliability.
Every change must produce verifiable artifacts that explain what was tested, what failed, and why.

## Scope
Applies to all contributors and all automation agents working in this repository.
Scoped contracts in subdirectories can add stricter rules but cannot weaken this contract.

## Required Evidence
1. Runtime/tooling/test changes must include deterministic test evidence or an explicit justified skip.
2. Mod/runtime bugfixes must include a reproducible bundle:
- `TestResults/runs/<runId>/repro-bundle.json`
- `TestResults/runs/<runId>/repro-bundle.md`
3. PRs must include affected profile IDs and reason-code-level diagnostics when runtime behavior changes.

## Risk Policy
- Default merge policy: human-reviewed only.
- Use explicit risk labels: `risk:low`, `risk:medium`, `risk:high`.
- High-risk runtime changes require explicit rollback notes.

## Reliability Loop
1. Intake issue with reproduction details.
2. Run live-validation tooling to collect a reproducible bundle.
3. Classify failure by explicit reason code.
4. Implement fix on branch.
5. Attach evidence in PR.
6. Close issue only when linked evidence confirms acceptance criteria.

## Safety Rules
1. No blind fixed-address runtime actions.
2. No silent success when artifacts are missing.
3. Keep profile compatibility explicit (`base`, `aotr`, `roe`, `custom`).
4. Prefer additive, reversible changes.

## Canonical Verification Command
Run this command before completion claims:

```bash
dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"
```

## Agent Queue Contract
- Intake work via `.github/ISSUE_TEMPLATE/agent_task.yml`.
- Queue with label `agent:ready`.
- Queue workflow posts execution packet and notifies `@copilot`.

## Queue Trigger Warning
Applying label `agent:ready` triggers the queue workflow immediately.
