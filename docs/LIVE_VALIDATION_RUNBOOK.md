# Live Validation Runbook (Evidence-First)

Use this runbook to gather real-machine evidence for runtime/mod issues and milestone closure tasks.

## 1. Preconditions

- Launch the target game session first (`swfoc.exe` / `StarWarsG.exe`).
- Ensure extender bridge host is running for extender-routed credits checks:
  - `SwfocExtender.Host` on pipe `SwfocExtenderBridge`.
- For AOTR: ensure launch context resolves to `aotr_1397421866_swfoc`.
- For ROE: ensure launch context resolves to `roe_3447786229_swfoc`.
- From repo root, run on Windows PowerShell.

## 2. Run Pack Command

```powershell
pwsh ./tools/run-live-validation.ps1 `
  -Configuration Release `
  -NoBuild `
  -Scope FULL `
  -EmitReproBundle $true `
  -FailOnMissingArtifacts `
  -Strict
```

Final ROE hard gate (fails if bundle classification is blocked):

```powershell
pwsh ./tools/run-live-validation.ps1 `
  -Configuration Release `
  -NoBuild `
  -Scope ROE `
  -EmitReproBundle $true `
  -Strict `
  -RequireNonBlockedClassification
```

Optional scope-specific runs:

```powershell
pwsh ./tools/run-live-validation.ps1 -NoBuild -Scope AOTR -EmitReproBundle $true
pwsh ./tools/run-live-validation.ps1 -NoBuild -Scope ROE -EmitReproBundle $true
pwsh ./tools/run-live-validation.ps1 -NoBuild -Scope TACTICAL -EmitReproBundle $true
```

## 3. Artifacts Contract

Per run, artifacts are emitted under:

- `TestResults/runs/<runId>/`

Expected outputs:

- `*-live-tactical.trx`
- `*-live-hero-helper.trx`
- `*-live-roe-health.trx`
- `*-live-credits.trx`
- `launch-context-fixture.json`
- `live-validation-summary.json`
- `live-roe-runtime-evidence.json` (when ROE runtime health test executes set_credits path)
- `repro-bundle.json`
- `repro-bundle.md`
- `issue-34-evidence-template.md`
- `issue-19-evidence-template.md`

`repro-bundle.json` classification values:

- `passed`
- `skipped`
- `failed`
- `blocked_environment`
- `blocked_profile_mismatch`

vNext bundle sections (required for runtime-affecting changes):

- `selectedHostProcess`
- `backendRouteDecision`
- `capabilityProbeSnapshot`
- `hookInstallReport`
- `overlayState`

## 4. Bundle Validation

```powershell
pwsh ./tools/validate-repro-bundle.ps1 `
  -BundlePath TestResults/runs/<runId>/repro-bundle.json `
  -SchemaPath tools/schemas/repro-bundle.schema.json `
  -Strict
```

## 5. Post Evidence to GitHub Issues

```powershell
gh issue comment 34 --body-file TestResults/runs/<runId>/issue-34-evidence-template.md
gh issue comment 19 --body-file TestResults/runs/<runId>/issue-19-evidence-template.md
```

Before posting, replace placeholder fields with actual attach/mode/profile details.
Do not close issues from placeholder-only or skip-only runs.

## 6. Closure Criteria

Close issues only when all required evidence is present:

- At least one successful tactical toggle + revert in tactical mode.
- Helper workflow evidence for both AOTR and ROE.
- Launch recommendation reason code + confidence captured.
- Selected host process includes deterministic host ranking diagnostics (`hostRole`, `selectionScore`).
- Backend route and capability probe sections are present with explicit reason codes.
- Extender credits path evidence includes `backendRoute=Extender` and hook state tag (`HOOK_LOCK` / `HOOK_ONESHOT`) in runtime diagnostics.
- Valid `repro-bundle.json` linked in issue evidence.

Then run:

```powershell
gh issue close 34 --comment "Live validation evidence posted; acceptance criteria met."
gh issue close 19 --comment "AOTR/ROE live calibration checklist complete with evidence."
gh issue close 7 --comment "All M1 acceptance criteria completed and evidenced across slices and live validation."
```
