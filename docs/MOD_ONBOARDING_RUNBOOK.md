# Mod Onboarding Runbook (M3)

This runbook covers the Mod Compatibility Studio flow for onboarding a custom mod profile.

## Goal
Generate a draft custom profile, export calibration evidence, and compute a promotion readiness report.

## UI Flow (Profiles & Updates tab)
1. Set `Base Profile` (default: `base_swfoc`).
2. Enter `Draft Profile Id` and `Display Name`.
3. Paste launch sample lines in `Launch Samples` (supports `STEAMMOD` and/or `MODPATH`).
4. Click `Scaffold Draft`.
5. Click `Export Calibration Artifact`.
6. Click `Build Compatibility Report`.

## Draft Profile Output
Draft profile files are emitted under:
- `profiles/custom/profiles/<draftProfileId>.json` by default.

The scaffold process infers:
- `requiredWorkshopIds` from `STEAMMOD=...`
- `localPathHints` from `MODPATH=...` and process/path tokens
- `profileAliases` from draft id + display name (+ optional aliases)

## Calibration Artifacts
Artifacts are written to:
- `%LOCALAPPDATA%\\SwfocTrainer\\support\\calibration` (default app path)

Schema contract:
- `tools/schemas/calibration-artifact.schema.json`

Validate artifact:

```powershell
pwsh ./tools/validate-calibration-artifact.ps1 -ArtifactPath <artifact.json> -SchemaPath tools/schemas/calibration-artifact.schema.json -Strict
```

## Compatibility Report Interpretation
Promotion is blocked when any of these are true:
- dependency status is `HardFail`
- unresolved critical symbols > 0
- one or more actions are `Unavailable`

Use the report output list to prioritize calibration and dependency fixes.

## Evidence Checklist
For issue/PR evidence, include:
- draft profile path
- calibration artifact path
- compatibility summary (`promotionReady`, dependency status, unresolved critical count)
- deterministic test command/results
