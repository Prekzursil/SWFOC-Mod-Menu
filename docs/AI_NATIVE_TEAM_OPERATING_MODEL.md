# AI-Native Team Operating Model

## Goal
Turn SWFOC runtime/mod debugging into an evidence-first loop where each failure becomes one reproducible run artifact with clear classification and next action.

## Operating Loop

1. Intake issue with profile/build/scope context.
2. Run live validation tooling to emit `repro-bundle.json` and `repro-bundle.md`.
3. Classify using explicit failure categories:

- `passed`
- `skipped`
- `failed`
- `blocked_environment`
- `blocked_profile_mismatch`

4. Implement fix on branch.
5. Attach evidence in PR.
6. Close issue only with linked evidence.

## Decision Rules

1. Deterministic evidence is required for all code changes.
2. Live evidence is required when behavior depends on game process or runtime mode.
3. No issue is “ready for fix” without launch-context and repro-bundle metadata.

## Artifacts

- `TestResults/runs/<runId>/repro-bundle.json`
- `TestResults/runs/<runId>/repro-bundle.md`
- TRX outputs per run
- launch context diagnostics

## Ownership

- Runtime reliability contract: `src/SwfocTrainer.Runtime/AGENTS.md`
- Tooling contract: `tools/AGENTS.md`
- Test contract: `tests/AGENTS.md`

## PR Readiness
A PR touching runtime/tooling/tests must include:

1. affected profile IDs
2. reason-code level behavior notes
3. deterministic test output summary
4. repro bundle or justified skip statement
