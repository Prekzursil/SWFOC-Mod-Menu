---
name: ci-pipeline-fix-and-maintenance
description: Workflow command scaffold for ci-pipeline-fix-and-maintenance in SWFOC-Mod-Menu.
allowed_tools: ["Bash", "Read", "Write", "Grep", "Glob"]
---

# /ci-pipeline-fix-and-maintenance

Use this workflow when working on **ci-pipeline-fix-and-maintenance** in `SWFOC-Mod-Menu`.

## Goal

Fixes and maintains CI pipeline workflows, including coverage, secrets, and integration with external analysis tools.

## Common Files

- `.github/workflows/*.yml`
- `tests/**/Tests.csproj`
- `tools/**/package.json`
- `tools/**/package-lock.json`

## Suggested Sequence

1. Understand the current state and failure mode before editing.
2. Make the smallest coherent change that satisfies the workflow goal.
3. Run the most relevant verification for touched files.
4. Summarize what changed and what still needs review.

## Typical Commit Signals

- Identify failing or misconfigured CI pipeline (coverage, secrets, external tools).
- Update relevant .github/workflows/*.yml files to fix parameters, secrets, or steps.
- Upgrade or patch dependencies in test or tool subprojects if required.
- Commit changes with descriptive message referencing the specific CI issue.

## Notes

- Treat this as a scaffold, not a hard-coded script.
- Update the command if the workflow evolves materially.