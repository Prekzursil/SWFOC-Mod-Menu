# External Tools Setup

This repository can augment internal reliability tooling with external quality signals.

## Secrets

Configure these repository secrets in GitHub:

- `SONAR_TOKEN`: token for SonarCloud scanning.
- `APPLITOOLS_API_KEY`: optional key for visual audit uploads.

## SonarCloud

1. Create SonarCloud project for this repository.
2. Ensure project key in `sonar-project.properties` matches SonarCloud configuration.
3. Push/PR runs trigger `.github/workflows/sonarcloud.yml`.

## Applitools (optional)

1. Capture visual pack artifacts per `docs/VISUAL_AUDIT_RUNBOOK.md`.
2. Use workflow `.github/workflows/visual-audit.yml` to produce compare artifacts.
3. If key exists, publish selected captures to Applitools for team review.

## jscpd duplication detection

- Config: `.jscpd.json`
- Workflow: `.github/workflows/duplication-check.yml`
- Report artifact: `jscpd-report`
