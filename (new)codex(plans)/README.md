# Codex Plan Archive

This folder is intentionally tracked as a versioned archive of long-form plans produced during Codex planning sessions.

## Conventions

- Keep one plan per file.
- Prefix filenames with a sortable sequence when useful (for example: `(5)M1-live-ops-implementation.md`).
- Avoid storing secrets or machine-specific credentials in plan files.
- When a plan is executed, link the implementation evidence (issue, PR, commit, or test output) inside the plan file.

## Evidence Link Format

Use one or more of:

- `evidence: issue <url>`
- `evidence: pr <url>`
- `evidence: commit <sha>`
- `evidence: test <path>`

This archive is additive and does not replace `TODO.md` or milestone issues.
