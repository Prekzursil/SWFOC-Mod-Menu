# Test Plan

## Automated tests

- `ProfileInheritanceTests`
  - verifies cross-profile inheritance (`roe` includes base + aotr actions)
  - verifies manifest includes all target profiles
- `SaveCodecTests`
  - load schema + synthetic save
  - edit key fields
  - validate rules
  - roundtrip write/load
- `ActionReliabilityServiceTests`
  - verifies `stable`/`experimental`/`unavailable` scoring
  - verifies unknown-mode strict gating and dependency soft-block handling
- `SelectedUnitTransactionServiceTests`
  - verifies apply/rollback semantics for selected-unit edits
  - verifies `RevertLast` and baseline restore behavior
- `SpawnPresetServiceTests`
  - verifies preset loading, batch expansion, stop-on-failure and continue-on-failure execution

## Tooling contract tests

- Launch-context fixture parity:

```powershell
python tools/detect-launch-context.py --from-process-json tools/fixtures/launch_context_cases.json --profile-root profiles/default --pretty
```

- Repro bundle schema + semantic validation:

```powershell
pwsh ./tools/validate-repro-bundle.ps1 -BundlePath tools/fixtures/repro_bundle_sample.json -SchemaPath tools/schemas/repro-bundle.schema.json -Strict
```

## Manual runtime checks

For each profile (`base_sweaw`, `base_swfoc`, `aotr_1397421866_swfoc`, `roe_3447786229_swfoc`):

1. Launch game + target mode.
2. Load profile and attach.
3. Execute:
   - credits change
   - timer freeze toggle
   - fog reveal toggle
   - selected unit HP/shield/speed edit (tactical)
   - helper spawn action
4. Save editor pass:
   - load save
   - edit credits + hero respawn fields
   - validate + write edited save
   - load in-game to confirm integrity.

## Live Ops checklist (M1)

For tactical sessions:

1. Open `Live Ops` tab.
2. `Capture Unit Baseline`, modify 2+ selected-unit fields, click `Apply Draft`.
3. Validate effects in-game.
4. Click `Revert Last` and confirm values are restored.

For spawn workflows:

1. Load profile-specific spawn presets.
2. Run a small batch (`quantity=3`) with `Stop on first failure` enabled.
3. Run a second batch with `Stop on first failure` disabled and confirm aggregated partial-failure reporting.

For reliability diagnostics:

1. Refresh reliability after attach.
2. Record at least one action in each state (`stable`, `experimental`, `unavailable`) when applicable.
3. Capture status line and action reason codes in issue evidence.

## Live evidence run pack

Use the scripted run pack when preparing issue evidence:

```powershell
pwsh ./tools/run-live-validation.ps1 -Configuration Release -NoBuild -Scope FULL -EmitReproBundle $true
```

This writes TRX + launch context outputs + repro bundle + prefilled issue templates to `TestResults/runs/<runId>/`.
If Python is unavailable in the running shell, the run pack still emits `launch-context-fixture.json` with a machine-readable failure status.
