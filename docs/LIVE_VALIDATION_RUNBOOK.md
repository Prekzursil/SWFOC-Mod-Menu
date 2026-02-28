# Live Validation Runbook (Evidence-First)

Use this runbook to gather real-machine evidence for runtime/mod issues and milestone closure tasks.

## 1. Preconditions

- Launch the target game session first (`swfoc.exe` / `StarWarsG.exe`).
- Ensure extender bridge host is running for extender-routed credits checks:
  - `SwfocExtender.Host` on pipe `SwfocExtenderBridge`.
- Promoted actions are extender-authoritative and fail-closed:
  - no managed/memory fallback is accepted for promoted matrix evidence.
  - missing or unverified promoted capability must surface fail-closed diagnostics.
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
pwsh ./tools/run-live-validation.ps1 -NoBuild -Scope ROE -EmitReproBundle $true -TopModsPath TestResults/mod-discovery/<runId>/top-mods.json
```

Forced-context closure run (for hosts that expose only `StarWarsG.exe NOARTPROCESS IGNOREASSERTS`):

```powershell
pwsh ./tools/run-live-validation.ps1 `
  -Configuration Release `
  -NoBuild `
  -Scope ROE `
  -EmitReproBundle $true `
  -FailOnMissingArtifacts `
  -Strict `
  -ForceWorkshopIds 1397421866,3447786229 `
  -ForceProfileId roe_3447786229_swfoc
```

Expected in bundle diagnostics for forced-context runs:

- `launchContext.source=forced`
- `launchContext.forcedWorkshopIds` contains supplied IDs
- `launchContext.forcedProfileId` reflects explicit profile override (when provided)

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
- `live-promoted-action-matrix.json`
- `artifact-index.json` (headless ghidra metadata index when emitted in CI/tooling runs)
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
- `actionStatusDiagnostics` (promoted action matrix diagnostics from `live-promoted-action-matrix.json`)

## 3a. Universal Compatibility Boundary

- Universal recommendation is metadata/discovery-driven across known defaults and generated custom profiles.
- Runtime mutation remains fail-closed by default for unknown/ambiguous patch states.
- New fallback actions are intentionally **not** part of the promoted extender matrix closure for issue `#7`.

## 3b. Patch Fallback Rollback Notes (`risk:high`)

- Risk label for PRs that change fallback patch behavior: `risk:high`.
- Rollback actions:
  - Execute `toggle_fog_reveal_patch_fallback` with `enable=false` when enabled profiles were patched.
  - Execute `set_unit_cap_patch_fallback` with `enable=false` when enabled profiles were patched.
  - Detach runtime session to force restore of tracked fallback bytes/hook state.
- Required evidence:
  - include reason-code diagnostics for apply/restore paths in `repro-bundle.json`
  - include affected profile IDs and fallback feature-flag state in PR notes

## 4. Promoted Action Matrix Evidence (Issue #7)

Promoted matrix evidence must cover 3 profiles x 5 actions (15 total checks):

| Action ID | `base_swfoc` | `aotr_1397421866_swfoc` | `roe_3447786229_swfoc` |
|---|---|---|---|
| `freeze_timer` | required | required | required |
| `toggle_fog_reveal` | required | required | required |
| `toggle_ai` | required | required | required |
| `set_unit_cap` | required | required | required |
| `toggle_instant_build_patch` | required | required | required |

`actionStatusDiagnostics` expected keys in `repro-bundle.json`:

- top-level: `status`, `source`, `summary`, `entries`
- summary: `total`, `passed`, `failed`, `skipped`
- entry keys: `profileId`, `actionId`, `outcome`, `backendRoute`, `routeReasonCode`, `capabilityProbeReasonCode`, `hybridExecution`, `hasFallbackMarker`, `message`, `skipReasonCode`
- route diagnostics should also preserve map-source metadata when available:
  - `capabilityMapReasonCode`
  - `capabilityMapState`
  - `capabilityDeclaredAvailable`

Expected evidence behavior for promoted actions:

- `backendRoute=Extender`
- `routeReasonCode=CAPABILITY_PROBE_PASS`
- `capabilityProbeReasonCode=CAPABILITY_PROBE_PASS`
- `hybridExecution=false`
- `hasFallbackMarker=false`
- fail-closed outcomes use explicit route diagnostics (`SAFETY_FAIL_CLOSED`) and block issue `#7` closure.

## 5. Bundle Validation

```powershell
pwsh ./tools/validate-repro-bundle.ps1 `
  -BundlePath TestResults/runs/<runId>/repro-bundle.json `
  -SchemaPath tools/schemas/repro-bundle.schema.json `
  -Strict
```

## 6. Post Evidence to GitHub Issues

```powershell
gh issue comment 34 --body-file TestResults/runs/<runId>/issue-34-evidence-template.md
gh issue comment 19 --body-file TestResults/runs/<runId>/issue-19-evidence-template.md
```

Before posting, replace placeholder fields with actual attach/mode/profile details.
Do not close issues from placeholder-only or skip-only runs.

## 7. Closure Criteria

Close issues only when all required evidence is present:

- At least one successful tactical toggle + revert in tactical mode.
- Helper workflow evidence for both AOTR and ROE.
- Launch recommendation reason code + confidence captured.
- Selected host process includes deterministic host ranking diagnostics (`hostRole`, `selectionScore`).
- Backend route and capability probe sections are present with explicit reason codes.
- Extender credits path evidence includes `backendRoute=Extender` and hook state tag (`HOOK_LOCK` / `HOOK_ONESHOT`) in runtime diagnostics.
- Captured action status diagnostics include `backendRoute`, `routeReasonCode`, `capabilityProbeReasonCode`, `capabilityMapReasonCode`, `capabilityMapState`, `capabilityDeclaredAvailable`, and for promoted matrix entries `hybridExecution` + `hasFallbackMarker`.
- Valid `repro-bundle.json` linked in issue evidence.
- If `top-mods.json` is present in the run directory (or passed via `-TopModsPath`), bundle assembly appends deterministic top-mod rows to `actionStatusDiagnostics` with explicit skip reason codes when live context is absent or not executed.

Issue `#7` evidence decision gate:

- `actionStatusDiagnostics.status` is `captured`.
- `actionStatusDiagnostics.summary.total=15`, `passed=15`, `failed=0`, `skipped=0`.
- every matrix entry for the five promoted actions across `base_swfoc`, `aotr_1397421866_swfoc`, and `roe_3447786229_swfoc` is present.
- no matrix entry includes fallback markers or blocked diagnostics (`hasFallbackMarker=true`, `backendRoute` not `Extender`, or non-pass route/probe reason codes).
- top-mod rows (when present) must never be silent omissions; each row must be explicit `Passed`, `Failed`, or `Skipped` with `skipReasonCode` when skipped.

Then run:

```powershell
gh issue close 34 --comment "Live validation evidence posted; acceptance criteria met."
gh issue close 19 --comment "AOTR/ROE live calibration checklist complete with evidence."
gh issue close 7 --comment "All M1 acceptance criteria completed and evidenced across slices and live validation."
```
