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
  - validates enriched generated-seed metadata ingestion and safe fallback feature defaults
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
  - verifies capability-hint source metadata (`reasonCode`/`state`/`available`) propagation into resolution output
- `SignatureResolverPackSelectionTests`
  - verifies deterministic symbol-pack selection precedence:
    - exact `<fingerprintId>.json`
    - artifact index mapping
    - fallback scan (`generatedAtUtc` desc, normalized path asc)
  - verifies embedded fingerprint mismatch rejection
- `ProfileVariantResolverTests`
  - verifies universal profile resolution for ROE workshop, sweaw fallback, and no-process fallback
- `LaunchContextResolverTests`
  - verifies metadata-driven workshop/modpath precedence and generic profile reason-code routing
- `RuntimeModeProbeResolverTests`
  - verifies runtime-effective strict mode inference (`TacticalLand` / `TacticalSpace` / `AnyTactical` / `Galactic`) from symbol-health probes
- `SdkExecutionGuardTests`
  - verifies degraded-read allowance and mutating fail-closed behavior
- `SdkOperationRouterTests`
  - verifies feature-flag gate, missing runtime context gate, and mode mismatch blocking
- `BackendRouterTests`
  - verifies fail-closed mutating behavior for hard-extender profiles
  - verifies promoted extender routing is opt-in via `SWFOC_FORCE_PROMOTED_EXTENDER`
  - verifies extender route promotion only when capability proof is present under override mode
  - verifies capability contract blocking and legacy memory fallback behavior
  - verifies fallback patch actions stay off promoted extender matrix and preserve managed-memory routing
- `MainViewModelSessionGatingTests`
  - verifies unresolved-symbol action gating and fallback feature-flag gating reasons
- `NamedPipeExtenderBackendTests`
  - verifies deterministic unhealthy state when extender bridge is unavailable
  - verifies probe-seed anchor parity and explicit anchor-invalid/anchor-unreadable reason-code handling
- `ProfileValidatorTests`
  - verifies `backendPreference` and `hostPreference` contract enforcement
- `MegArchiveReaderTests`
  - verifies deterministic MEG entry listing/open behavior for fixture archives
  - verifies corrupt-header fail-closed diagnostics
- `EffectiveGameDataIndexServiceTests`
  - verifies precedence ordering (`MODPATH` > game loose > enabled MEGs)
  - verifies provenance and shadow metadata (`sourceType`, `sourcePath`, `overrideRank`, `shadowedBy`)
- `TelemetryLogTailServiceTests`
  - verifies telemetry marker parsing with strict LAND/SPACE mapping, freshness gating, and stale-ignore behavior
- `RuntimeAdapterModeOverrideTests`
  - verifies mode precedence with telemetry feed (manual override still highest priority) across strict tactical mode values
- `NamedPipeHelperBridgeBackendTests`
  - verifies helper bridge fail-closed behavior and verification-contract enforcement before helper success is reported
- `StoryFlowGraphExporterTests`
  - verifies deterministic node/edge graph output and tactical/galactic event linkage
- `LuaHarnessRunnerTests`
  - verifies offline telemetry script execution contract with pinned vendor metadata

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

- Effective data index export smoke:

```powershell
pwsh ./tools/research/export-effective-data-index.ps1 -ProfileId base_swfoc -OutPath TestResults/index/base_swfoc_effective_index.json -Strict
```

- Story flow graph export smoke:

```powershell
pwsh ./tools/research/export-story-flow-graph.ps1 -ProfileId roe_3447786229_swfoc -OutPath TestResults/flow/roe_flow_graph.json -Strict
```

- Offline Lua harness smoke:

```powershell
pwsh ./tools/lua-harness/run-lua-harness.ps1 -Strict
```

- Ghidra artifact index schema validation:

```powershell
pwsh ./tools/validate-ghidra-artifact-index.ps1 -Path tools/fixtures/ghidra_artifact_index_sample.json -SchemaPath tools/schemas/ghidra-artifact-index.schema.json -Strict
```

## Manual runtime checks

For each profile (`base_sweaw`, `base_swfoc`, `aotr_1397421866_swfoc`, `roe_3447786229_swfoc`):

1. Launch target session (prefer app `Launch + Attach` or `tools/run-live-validation.ps1 -AutoLaunch`) + target mode.
2. Load profile and attach.
3. Execute:

   - credits change
   - timer freeze toggle
   - fog reveal toggle
   - selected unit HP/shield/speed edit (`AnyTactical` / `TacticalLand` / `TacticalSpace`)
   - helper spawn action
   - capture status diagnostics showing `backendRoute`, `routeReasonCode`, `capabilityProbeReasonCode`, `capabilityMapReasonCode`, `capabilityMapState`, `capabilityDeclaredAvailable`, plus `hookState`/`hybridExecution` when present

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

## Phase 2 promoted action and hotkey checks

Promoted FoC actions are not forced to extender by default.
For extender-authoritative promoted matrix evidence, explicitly enable:

```powershell
$env:SWFOC_FORCE_PROMOTED_EXTENDER = "1"
```

Evidence must come from `actionStatusDiagnostics` in `repro-bundle.json` (source `live-promoted-action-matrix.json`).

Required profile/action matrix:

| Profile | `freeze_timer` | `toggle_fog_reveal` | `toggle_ai` | `set_unit_cap` | `toggle_instant_build_patch` |
|---|---|---|---|---|---|
| `base_swfoc` | required | required | required | required | required |
| `aotr_1397421866_swfoc` | required | required | required | required | required |
| `roe_3447786229_swfoc` | required | required | required | required | required |

Verification checklist:

1. Attach to each target FoC profile with extender bridge available.
2. Trigger quick actions/hotkeys for at least `Set Credits` and `Freeze Timer` in UI; run full promoted matrix via live validation pack.
3. Verify `actionStatusDiagnostics` contains required keys:
   - top-level: `status`, `source`, `summary`, `entries`
   - summary: `total`, `passed`, `failed`, `skipped`
   - each entry: `profileId`, `actionId`, `outcome`, `backendRoute`, `routeReasonCode`, `capabilityProbeReasonCode`, `hybridExecution`, `hasFallbackMarker`, `message`, `skipReasonCode`
4. Verify promoted matrix entry outcomes for issue `#7` closure gate:
   - `summary.failed=0`
   - all entries report `backendRoute=Extender`
   - all entries report `hybridExecution=false`
   - all entries report `hasFallbackMarker=false`
   - normal pass entries report `routeReasonCode=CAPABILITY_PROBE_PASS` and `capabilityProbeReasonCode=CAPABILITY_PROBE_PASS`
   - capability-gated entries use explicit skip semantics (`skipReasonCode=promoted_capability_unavailable`) instead of synthetic success.
5. Verify fail-closed behavior remains explicit when environment is unhealthy:
   - promoted actions must not silently route to fallback backend
   - blocked runs surface explicit reason codes and must not be used for issue `#7` closure
6. Verify top-mod evidence extension:
   - when `top-mods.json` is present, `actionStatusDiagnostics.entries` must include explicit rows for discovered workshop ids (up to 10 mods x 5 promoted actions)
   - top-mod rows may be `Skipped` during deterministic/live runs that only execute shipped profile matrix, but each skipped row must include `skipReasonCode`

After matrix runs:

```powershell
Remove-Item Env:SWFOC_FORCE_PROMOTED_EXTENDER -ErrorAction SilentlyContinue
```

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

Optional top-mod context extension:

```powershell
pwsh ./tools/run-live-validation.ps1 -Configuration Release -NoBuild -Scope FULL -EmitReproBundle $true -TopModsPath TestResults/mod-discovery/<runId>/top-mods.json
```

This writes TRX + launch context outputs + repro bundle + prefilled issue templates to `TestResults/runs/<runId>/`.
If Python is unavailable in the running shell, the run pack still emits `launch-context-fixture.json` with a machine-readable failure status.
Include captured status diagnostics for promoted matrix evidence in issue reports (`actionStatusDiagnostics` summary + representative entries).
