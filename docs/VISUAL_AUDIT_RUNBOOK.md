# Visual Audit Runbook

This runbook defines visual-pack naming and comparison flow for desktop/live workflow evidence.

## Visual pack contract

Capture visual artifacts under:

- `TestResults/runs/<runId>/visual-pack/<surface>/<checkpoint>.png`

Naming guidance:
- `<surface>`: `live-ops`, `runtime-tab`, `attach-dialog`, `tactical`, `galactic`
- `<checkpoint>`: concise state marker, for example `attached-aotr`, `mode-tactical`, `spawn-panel-open`

## Baseline layout

Store baseline images under:

- `artifacts/visual-baselines/<profileId>/<surface>/<checkpoint>.png`

## Comparison flow

```powershell
pwsh ./tools/compare-visual-pack.ps1 `
  -BaselineDir artifacts/visual-baselines `
  -CandidateDir TestResults/runs/<runId>/visual-pack `
  -OutputPath TestResults/runs/<runId>/visual-pack/visual-compare.json
```

## Optional Applitools layer

If `APPLITOOLS_API_KEY` is configured, you can upload selected packs to Applitools dashboards for additional review.
This is an augmenting signal; repository artifact comparison remains the primary gate.
