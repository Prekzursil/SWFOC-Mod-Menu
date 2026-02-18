# Roadmap Workflow

This project executes roadmap slices through three synchronized surfaces:

1. `TODO.md` execution board (`Now` / `Next` / `Later`)
2. Milestone epics (`M0` ... `M4`) in GitHub Issues
3. Versioned long-form plan archive in `(new)codex(plans)/`

## Execution Rules

- Start from an approved plan artifact in `(new)codex(plans)/`.
- Convert the plan into slice issues under the active milestone epic.
- Update `TODO.md` only when evidence exists.
- Every completed slice must include at least one evidence link:
  - test path, issue URL, PR URL, or commit SHA.
- Runtime/mod issues are not ready-for-fix without `repro-bundle.json` + classification + launch reason code.

## Evidence Priority

1. Deterministic tests in CI
2. Live-machine validation notes for runtime features
3. Manual UX verification screenshots/notes

## Board Lane Policy

- `Now`: only issues with explicit artifact acceptance criteria and assigned owner.
- `Next`: implementation-defined issues with dependencies resolved.
- `Later`: research or blocked work; no active implementation.
- Closure rule for runtime/mod issues: linked bundle evidence + reason code + fix validation notes.

## M1 Guidance

- Keep low-level Runtime tab behavior backward compatible.
- Deliver new high-impact workflows inside the `Live Ops` tab.
- Enforce strict bundle gating when runtime mode is unknown.
