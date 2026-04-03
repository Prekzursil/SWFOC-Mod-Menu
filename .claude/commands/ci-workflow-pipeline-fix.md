---
name: ci-workflow-pipeline-fix
description: Workflow command scaffold for ci-workflow-pipeline-fix in SWFOC-Mod-Menu.
allowed_tools: ["Bash", "Read", "Write", "Grep", "Glob"]
---

# /ci-workflow-pipeline-fix

Use this workflow when working on **ci-workflow-pipeline-fix** in `SWFOC-Mod-Menu`.

## Goal

Fixes or updates to CI/CD workflows to resolve build, test, or analysis issues (coverage, secrets, runner, PR analysis).

## Common Files

- `.github/workflows/sonarcloud.yml`
- `.github/workflows/quality-zero-platform.yml`
- `.github/workflows/codecov-analytics.yml`

## Suggested Sequence

1. Understand the current state and failure mode before editing.
2. Make the smallest coherent change that satisfies the workflow goal.
3. Run the most relevant verification for touched files.
4. Summarize what changed and what still needs review.

## Typical Commit Signals

- Identify failing CI/CD workflow or analysis step.
- Edit the relevant .github/workflows/*.yml file(s) to fix configuration (e.g., secrets, runner OS, PR parameters).
- Commit and push changes.
- Verify that the pipeline passes with the new configuration.

## Notes

- Treat this as a scaffold, not a hard-coded script.
- Update the command if the workflow evolves materially.