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
  - extended edit types (`float`, `double`, `ascii`) via deterministic temp schema
- `SavePatchPackServiceTests`
  - deterministic patch-pack export from typed save edits
  - schema/profile compatibility checks (includes all four shipped profiles)
  - invalid contract rejection for import path
  - JSON disk roundtrip load/preview regression coverage
  - field path/id drift preview warnings
- `SavePatchApplyServiceTests`
  - atomic apply success path with backup + receipt artifacts
  - compatibility-block behavior for profile mismatch
  - validation-failure rollback behavior with hash preservation
  - stale `fieldPath` fallback to `fieldId`
  - strict-off source-hash mismatch apply behavior
- `ActionReliabilityServiceTests`
  - verifies `stable`/`experimental`/`unavailable` scoring
  - verifies unknown-mode strict gating and dependency soft-block handling
- `SelectedUnitTransactionServiceTests`
  - verifies apply/rollback semantics for selected-unit edits
  - verifies `RevertLast` and baseline restore behavior
- `SpawnPresetServiceTests`
  - verifies preset loading, batch expansion, stop-on-failure and continue-on-failure execution
- `SaveCorpusRoundTripTests`
  - schema round-trip checks driven from tracked corpus fixtures under `tools/fixtures/save-corpus`
  - covers all shipped schemas (`base_sweaw`, `base_swfoc`, `aotr`, `roe`)
- `ModOnboardingServiceTests`
  - scaffolds deterministic custom profile draft from launch sample hints
  - validates workshop/path hint inference and profile output contract
- `ModCalibrationServiceTests`
  - validates calibration artifact generation and compatibility gate behavior
- `ProfileUpdateServiceTransactionalTests`
  - verifies transactional install receipt/backup behavior and rollback success path
- `TelemetrySnapshotServiceTests`
  - verifies counter aggregation and deterministic snapshot export
- `SupportBundleServiceTests`
  - verifies support-bundle zip + manifest generation and expected payload presence
- `BinaryFingerprintServiceTests`
  - verifies deterministic fingerprint capture and stable hash/id derivation
- `CapabilityMapResolverTests`
  - verifies fail-closed behavior for missing/partial capability anchors
- `ProfileVariantResolverTests`
  - verifies universal profile resolution for ROE workshop, sweaw fallback, and no-process fallback
- `RuntimeModeProbeResolverTests`
  - verifies runtime-effective tactical/galactic inference from symbol-health probes
- `SdkExecutionGuardTests`
  - verifies degraded-read allowance and mutating fail-closed behavior
- `SdkOperationRouterTests`
  - verifies feature-flag gate, missing runtime context gate, and mode mismatch blocking
- `BackendRouterTests`
  - verifies fail-closed mutating behavior for hard-extender profiles
  - verifies extender route promotion only when capability proof is present
  - verifies capability contract blocking and legacy memory fallback behavior
- `NamedPipeExtenderBackendTests`
  - verifies deterministic unhealthy state when extender bridge is unavailable
- `ProfileValidatorTests`
  - verifies `backendPreference` and `hostPreference` contract enforcement

## Tooling contract tests

- Launch-context fixture parity:

```powershell
python tools/detect-launch-context.py --from-process-json tools/fixtures/launch_context_cases.json --profile-root profiles/default --pretty
```

- Repro bundle schema + semantic validation:

```powershell
pwsh ./tools/validate-repro-bundle.ps1 -BundlePath tools/fixtures/repro_bundle_sample.json -SchemaPath tools/schemas/repro-bundle.schema.json -Strict
```

- Save patch-pack schema validation:

```powershell
pwsh ./tools/validate-save-patch-pack.ps1 -PatchPackPath tools/fixtures/save_patch_pack_sample.json -SchemaPath tools/schemas/save-patch-pack.schema.json -Strict
```

- Calibration artifact schema validation:

```powershell
pwsh ./tools/validate-calibration-artifact.ps1 -ArtifactPath tools/fixtures/calibration_artifact_sample.json -SchemaPath tools/schemas/calibration-artifact.schema.json -Strict
```

- Support bundle manifest schema validation:

```powershell
pwsh ./tools/validate-support-bundle-manifest.ps1 -ManifestPath tools/fixtures/support_bundle_manifest_sample.json -SchemaPath tools/schemas/support-bundle-manifest.schema.json -Strict
```

- Binary fingerprint schema validation:

```powershell
pwsh ./tools/validate-binary-fingerprint.ps1 -FingerprintPath tools/fixtures/binary_fingerprint_sample.json -SchemaPath tools/schemas/binary-fingerprint.schema.json -Strict
```

- Signature pack schema validation:

```powershell
pwsh ./tools/validate-signature-pack.ps1 -SignaturePackPath tools/fixtures/signature_pack_sample.json -SchemaPath tools/schemas/signature-pack.schema.json -Strict
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
   - capture status diagnostics showing `backend`, `routeReasonCode`, `capabilityProbeReasonCode`, plus `hookState`/`hybridExecution` when present
4. Verify attach diagnostics include host ranking fields:
   - `hostRole`
   - `mainModuleSize`
   - `workshopMatchCount`
   - `selectionScore`
5. Save editor pass:
   - load save
   - edit credits + hero respawn fields
   - optionally edit `ascii`/floating fields where schema supports them
   - validate + write edited save
   - export patch pack, reload patch pack, preview/apply, restore backup
   - load in-game to confirm integrity.

## Phase 2 quick action and hotkey checks

1. Attach to a SWFOC profile where `set_credits` executes as `Sdk`.
2. Run quick actions:
   - `Set Credits` (SDK-backed route)
   - `Freeze Timer` (symbol-backed memory route)
3. Trigger equivalent bindings from `Hotkeys` (default `Ctrl+Shift+1/2`).
4. Verify status lines include route diagnostics when available:
   - `backend`
   - `routeReasonCode`
   - `capabilityProbeReasonCode`
   - `hookState` (when backend emits it)
   - `hybridExecution` (when backend emits it)
5. Verify symbol/dependency gating behavior:
   - actions requiring unresolved symbols remain blocked/hidden for `Memory`, `CodePatch`, and `Freeze` execution kinds
   - SDK actions remain executable when symbol gating is not required by execution kind

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
Include at least one captured status line in issue evidence that demonstrates Phase 2 route diagnostics for a mutating action.
