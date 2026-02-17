# SWFOC-Mod-Menu

Profile-driven trainer/editor for **Star Wars: Empire at War / Forces of Corruption** with support for base game and major FoC mod stacks.

## Supported Profiles

- `base_sweaw` (base Empire at War)
- `base_swfoc` (base Forces of Corruption)
- `aotr_1397421866_swfoc` (Awakening of the Rebellion)
- `roe_3447786229_swfoc` (ROE submod)

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

Deterministic test suite:

```powershell
dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj `
  -c Release --no-build `
  --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"
```

## CI

GitHub Actions workflows:
- `.github/workflows/ci.yml`: restore, build, deterministic tests, launch-context fixture smoke checks
- `.github/workflows/release-portable.yml`: manual/tag-triggered portable package artifact

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

## Roadmap and Execution

- Execution board: `TODO.md`
- Roadmap workflow: `docs/ROADMAP_WORKFLOW.md`
- Plan archive: `(new)codex(plans)/`
- Profile format contract: `docs/PROFILE_FORMAT.md`

## Security and Reporting

Please use `SECURITY.md` for vulnerability disclosure workflow.
