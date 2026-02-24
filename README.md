# SWFOC-Mod-Menu

Profile-driven trainer/editor for **Star Wars: Empire at War / Forces of Corruption** with support for base game and major FoC mod stacks.

## Supported Profiles

- `base_sweaw` (base Empire at War)
- `base_swfoc` (base Forces of Corruption)
- `aotr_1397421866_swfoc` (Awakening of the Rebellion)
- `roe_3447786229_swfoc` (ROE submod)
- `universal_auto` (user-facing auto resolver that maps to concrete internal profile variants)

## What This Repository Contains

This repository is **code-first** and intentionally excludes full local mod mirrors and large game assets.

Included:

- .NET solution and runtime/editor code
- profile packs, schemas, catalogs, and helper hooks
- tests and CI workflows
- diagnostics and launch-context tooling

Excluded:

- full Workshop/local mod content trees
- large media/model assets
- generated build artifacts

## Core Capabilities

- Profile inheritance + metadata-driven routing
- Attach/process selection for SWFOC launch realities (`STEAMMOD`, `MODPATH`, `StarWarsG`)
- Signature-first symbol resolution with validated fallback offsets
- Runtime memory actions, code patches, helper actions, and freeze orchestration
- Live Ops workflows:
  - selected-unit transaction lab (apply/revert/baseline restore)
  - action reliability surface (`stable`/`experimental`/`unavailable`)
  - spawn preset studio with batch operations
- Save decode/edit/validate/write workflow
- Save Lab patch-pack workflow:
  - schema-path typed patch export/import
  - compatibility preview (profile/schema/hash)
  - strict/non-strict apply toggle (strict default ON)
  - atomic apply with backup + receipt
  - rollback from latest backup
- Mod Compatibility Studio (M3):
  - draft profile onboarding from launch samples (`STEAMMOD`/`MODPATH`)
  - calibration artifact export
  - compatibility report with promotion gate verdict
- Ops Hardening (M4):
  - transactional profile update install + rollback
  - support bundle export (logs/runtime/repro bundles/telemetry)
  - telemetry snapshot export for drift diagnostics
- Dependency-aware action gating for mod/submod contexts
- Launch-context detector script for reproducible diagnostics

## Prerequisites

- Windows 10/11
- .NET SDK 8.x (repo pinned through `global.json`)
- Python 3.10+ (for tooling scripts)

## Build and Test

```powershell

# from repo root

dotnet restore SwfocTrainer.sln
dotnet build SwfocTrainer.sln -c Release
```

Quick verification using Makefile:

```bash
make verify    # Run deterministic test suite
make build     # Build the solution
make clean     # Clean build artifacts
```

Quick Windows launchers in repo root (double-click):

- `launch-app-release.cmd` builds Release if needed, then starts `SwfocTrainer.App.exe`.
- `launch-app-debug.cmd` builds Debug if needed, then starts `SwfocTrainer.App.exe`.
- `run-deterministic-tests.cmd` runs the non-live deterministic test suite.
- `run-live-tests.cmd` runs live profile tests (expected to skip when no live SWFOC process is available).

Deterministic test suite (direct command):

```powershell
dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj `
  -c Release --no-build `
  --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"
```

For troubleshooting test failures, build issues, or environment setup problems, see `docs/TROUBLESHOOTING.md`.

## CI

GitHub Actions workflows:

- `.github/workflows/ci.yml`: restore, build, deterministic tests, launch-context fixture smoke checks
- `.github/workflows/release-portable.yml`: portable package + checksum + GitHub Release publish on tags

## Reviewer Automation

Reviewer assignment is handled by a REST-only workflow to avoid GraphQL fragility:

- Workflow: `.github/workflows/reviewer-automation.yml`
- Roster contract: `config/reviewer-roster.json`
- Script: `tools/request-pr-reviewers.ps1`

Behavior:

- Requests non-author reviewers from the configured roster.
- If no eligible reviewer exists, applies fallback label/comment (`needs-reviewer`) and keeps workflow green.

Runbook: `docs/REVIEWER_AUTOMATION.md`

## Launch Context Tooling

```powershell
python tools/detect-launch-context.py --from-process-json tools/fixtures/launch_context_cases.json --profile-root profiles/default --pretty
```

This emits normalized launch context + profile recommendation JSON for runtime parity validation.

## Repro Bundle Tooling

```powershell
pwsh ./tools/run-live-validation.ps1 -Configuration Release -NoBuild -Scope FULL -EmitReproBundle $true
pwsh ./tools/validate-repro-bundle.ps1 -BundlePath TestResults/runs/<runId>/repro-bundle.json -SchemaPath tools/schemas/repro-bundle.schema.json -Strict
```

Runtime/mod triage should attach `repro-bundle.json` + `repro-bundle.md` from `TestResults/runs/<runId>/`.

## R&D Research Tooling (Deep RE Track)

```powershell
pwsh ./tools/research/run-capability-intel.ps1 -ModulePath "C:\\Games\\Star Wars Empire at War\\corruption\\swfoc.exe" -ProfilePath profiles/default/profiles/base_swfoc.json
pwsh ./tools/research/capture-binary-fingerprint.ps1 -ModulePath "C:\\Games\\Star Wars Empire at War\\corruption\\swfoc.exe"
pwsh ./tools/research/generate-signature-candidates.ps1 -FingerprintPath TestResults/research/<runId>/fingerprint.json
pwsh ./tools/research/normalize-signature-pack.ps1 -InputPath TestResults/research/<runId>/signature-pack.json
pwsh ./tools/validate-binary-fingerprint.ps1 -FingerprintPath tools/fixtures/binary_fingerprint_sample.json -SchemaPath tools/schemas/binary-fingerprint.schema.json -Strict
pwsh ./tools/validate-signature-pack.ps1 -SignaturePackPath tools/fixtures/signature_pack_sample.json -SchemaPath tools/schemas/signature-pack.schema.json -Strict
```

Contracts:

- `docs/RESEARCH_GAME_WORKFLOW.md`
- `tools/research/build-fingerprint.md`
- `tools/schemas/binary-fingerprint.schema.json`
- `tools/schemas/signature-pack.schema.json`

## Save Patch-Pack Tooling

```powershell
pwsh ./tools/validate-save-patch-pack.ps1 -PatchPackPath tools/fixtures/save_patch_pack_sample.json -SchemaPath tools/schemas/save-patch-pack.schema.json -Strict
pwsh ./tools/export-save-patch-pack.ps1 -OriginalSavePath <original.sav> -EditedSavePath <edited.sav> -ProfileId base_swfoc -SchemaId base_swfoc_steam_v1 -OutputPath TestResults/patches/example.patch.json -BuildIfNeeded
pwsh ./tools/apply-save-patch-pack.ps1 -TargetSavePath <target.sav> -PatchPackPath TestResults/patches/example.patch.json -TargetProfileId base_swfoc -Strict $true -BuildIfNeeded
```

## Calibration Workflow (Realtime Reliability)

1. Run live attach against target profile.
2. Capture diagnostics (`reasonCode`, symbol source, dependency state).
3. Run launch-context tool against captured process command line/path.
4. If symbol drift is observed, open a **Calibration** issue using the template.
5. Update signature/metadata with evidence and keep fallback-only changes explicitly marked.

## Packaging

```powershell
pwsh ./tools/package-portable.ps1 -Configuration Release
```

Output: `artifacts/SwfocTrainer-portable.zip`

GitHub Releases are the primary distribution channel. See `docs/RELEASE_RUNBOOK.md` for tag policy, checksum verification, and rollback steps.

## Roadmap and Execution

- Execution board: `TODO.md`
- Roadmap workflow: `docs/ROADMAP_WORKFLOW.md`
- Save Lab operator workflow: `docs/SAVE_LAB_RUNBOOK.md`
- Mod onboarding workflow: `docs/MOD_ONBOARDING_RUNBOOK.md`
- Release operations: `docs/RELEASE_RUNBOOK.md`
- KPI baseline and governance: `docs/KPI_BASELINE.md`
- Fleet baseline lite (for repo rollout): `docs/FLEET_BASELINE_LITE.md`
- Plan archive: `(new)codex(plans)/`
- Profile format contract: `docs/PROFILE_FORMAT.md`

## Security and Reporting

Please use `SECURITY.md` for vulnerability disclosure workflow.
