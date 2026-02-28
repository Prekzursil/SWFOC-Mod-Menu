# M2 Truthiness + Flow Intelligence Closure (2026-02-28)

## Objective

Ship one consolidated PR that closes false-ready capability states, hardens native patch mutation safety, pins FoC credits ownership to managed runtime by default, and delivers the Tier-2 flow/data stack (MEG reader, effective data index, telemetry mode feed, story-flow graph export, offline Lua harness).

## Scope Delivered

1. Probe truthiness hardening:
   - capability probe now requires parseable/readable anchors before `available=true`.
   - diagnostics now include `anchorKey`, `anchorValue`, `parseOk`, `readOk`, `readError`, `probeSource`.
2. Native patch-safe writes:
   - added `WriteMutationMode` and `TryWriteBytesPatchSafe` (`VirtualProtectEx` + restore).
   - patch write diagnostics include protection/restore metadata.
3. Build patch correctness:
   - `set_unit_cap` now writes `int32` (`1..100000`) and keeps restore-state cache.
   - disable path restores original bytes or fails closed with explicit reason code.
4. Instant-build canonicalization:
   - canonical alias chain: `instant_build_patch_injection`, `instant_build_patch`, `toggle_instant_build_patch`.
   - parity covered in runtime/backend tests.
5. Credits ownership cutover:
   - FoC `set_credits` default route is managed (`Memory`).
   - native credits lane is explicit experimental action (`set_credits_extender_experimental`) behind `allow_extender_credits=false`.
6. Repro-bundle boolean normalization:
   - removed fragile direct `[bool]` casts in dynamic diagnostics parsing.
7. MEG project:
   - added `SwfocTrainer.Meg` with archive reader/result contracts and deterministic fixtures/tests.
8. Effective data index:
   - added merge/precedence service with provenance report model and export tool.
9. Telemetry mode feed:
   - added telemetry mod template and runtime log-tail service.
   - runtime mode precedence now includes telemetry feed.
10. Story-flow graph export:
    - added graph model/exporter and deterministic JSON+markdown script.
11. Offline Lua harness integration:
    - added harness runner contracts/services, tests, and pinned vendor commit.
12. Tooling/docs/CI:
    - added script path normalization for `pwsh.exe` UNC runs.
    - CI smoke now includes effective-index export, story-flow export, and lua harness strict run.

## Verification Evidence

Deterministic gates on 2026-02-28:

- `dotnet restore SwfocTrainer.sln`
- `dotnet build SwfocTrainer.sln -c Release --no-restore`
- `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"`  
  Result: `Passed: 213, Failed: 0, Skipped: 0`.
- `python3 tools/detect-launch-context.py --from-process-json tools/fixtures/launch_context_cases.json --profile-root profiles/default --pretty`
- `pwsh ./tools/validate-workshop-topmods.ps1 -Path tools/fixtures/workshop_topmods_sample.json -Strict`
- `pwsh ./tools/validate-generated-profile-seed.ps1 -Path tools/fixtures/generated_profile_seeds_sample.json -Strict`
- `pwsh ./tools/research/export-effective-data-index.ps1 -ProfileId base_swfoc -OutPath TestResults/index/base_swfoc_effective_index.json -Strict`
- `pwsh ./tools/research/export-story-flow-graph.ps1 -ProfileId roe_3447786229_swfoc -OutPath TestResults/flow/roe_flow_graph.json -Strict`
- `pwsh ./tools/lua-harness/run-lua-harness.ps1 -Strict`

Live pack executions on 2026-02-28:

- `pwsh ./tools/run-live-validation.ps1 -Configuration Release -NoBuild -Scope FULL -EmitReproBundle $true -FailOnMissingArtifacts -Strict`
  - run id: `20260228-171028`
  - bundle: `TestResults/runs/20260228-171028/repro-bundle.json`
  - classification: `blocked_environment`
- `pwsh ./tools/run-live-validation.ps1 -Configuration Release -NoBuild -Scope ROE -EmitReproBundle $true -FailOnMissingArtifacts -Strict -ForceWorkshopIds 1397421866,3447786229 -ForceProfileId roe_3447786229_swfoc`
  - run id: `20260228-171159`
  - bundle: `TestResults/runs/20260228-171159/repro-bundle.json`
  - classification: `blocked_environment`
- bundle schema validation executed for both run ids:
  - `pwsh ./tools/validate-repro-bundle.ps1 -BundlePath TestResults/runs/<runId>/repro-bundle.json -SchemaPath tools/schemas/repro-bundle.schema.json -Strict`

## Risk and Rollback (`risk:high`)

- Runtime patch-safety changes and restore-state behavior are high-risk.
- Rollback path:
  1. Disable fallback patch actions by profile feature flags.
  2. Route unit-cap/credits through managed-only paths.
  3. Revert native patch-safe writer and restore-cache changes if regressions appear in live diagnostics.
  4. Re-run deterministic + live pack commands and compare reason-code deltas in repro bundles.

## Open Closure Note

The live run pack completed end-to-end with valid bundles, but closure-grade non-skipped live matrix evidence still requires attached game processes (`sweaw.exe` / `swfoc.exe` / `StarWarsG.exe` with expected mod contexts) at execution time.
