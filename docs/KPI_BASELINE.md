# KPI Baseline (Phase 3/4)

This document defines the weekly KPI baseline for AI-assisted engineering operations.

## Generating a KPI digest

### Automated workflow

A GitHub Actions workflow runs every Monday at 06:15 UTC to generate a KPI digest issue automatically:

- Workflow: `.github/workflows/kpi-weekly-digest.yml`
- Trigger: Weekly schedule (Monday 06:15 UTC) or manual dispatch

### Manual generation

To generate a KPI digest manually:

```powershell
pwsh ./tools/generate-kpi-digest.ps1
```

To save the digest to a file:

```powershell
pwsh ./tools/generate-kpi-digest.ps1 -OutputPath TestResults/kpi-digest.md
```

To analyze a different time period:

```powershell
pwsh ./tools/generate-kpi-digest.ps1 -DaysBack 14
```

**Requirements:**

- PowerShell Core 7.x+
- GitHub CLI (`gh`) for automated metric collection (optional but recommended)

## Core metrics

1. Intake-to-PR lead time
2. PR cycle time
3. Queue failure rate
4. Agent rework rate
5. Evidence completeness rate
6. Regression incident count

## Collection cadence

- Weekly digest issue generated automatically.
- Human reviewer validates outliers and annotates root causes.
- Metrics are compared week-over-week to detect drift.

## Branch protection policy

The following branch protection requirements are validated weekly via `.github/workflows/branch-protection-audit.yml`:

### Required settings for `main` branch

1. **Pull request reviews**
   - Minimum required approving reviews: 1
   - Dismiss stale pull request approvals when new commits are pushed: Enabled
   - Require review from code owners: Recommended

2. **Status checks**
   - Require status checks to pass before merging: Enabled
   - Required checks:
     - `build-test` (CI workflow)
     - `validate-policy-contracts` (policy-contract workflow)
   - Require branches to be up to date before merging: Enabled

3. **Restrictions**
   - Require a pull request before merging: Enabled
   - Require conversation resolution before merging: Enabled
   - Do not allow bypassing the above settings: Enabled
   - Restrict force pushes: Enabled
   - Restrict deletions: Enabled

### Validation

The branch protection audit workflow runs every Monday at 06:30 UTC and creates an issue if any requirements are not met.

Manual validation command (requires GitHub CLI with appropriate permissions):

```bash
gh api repos/:owner/:repo/branches/main/protection
```

## Operating notes

- Keep merges human-reviewed.
- Treat failed deterministic verification as a hard stop.
- Use risk labels consistently (`risk:low`, `risk:medium`, `risk:high`).

## Escaped-regression signal

An escaped regression is detected when:

1. **Runtime-sensitive change merged** - Changes to `src/SwfocTrainer.Runtime/` or profile metadata
2. **Post-merge issue opened** - Issue references merged PR and reports runtime behavior regression
3. **Classification criteria met**:
   - Issue includes `repro-bundle.json` with `classification: failed`
   - Issue includes launch reason code showing profile/signature mismatch
   - Issue demonstrates behavior worked before the referenced PR

### Response protocol

1. Apply `regression:escaped` label to the issue
2. Link to the PR that introduced the regression
3. Create hotfix branch from main
4. Implement minimal fix with deterministic test coverage
5. Include rollback steps in hotfix PR
6. Fast-track review with `risk:high` label
7. Document root cause in weekly KPI digest

### Prevention measures

- Required deterministic test suite pass before merge
- Required evidence completeness in PR (repro bundle or justified skip)
- Branch protection enforcement (human review + status checks)
- Post-merge monitoring of runtime-tagged issues
