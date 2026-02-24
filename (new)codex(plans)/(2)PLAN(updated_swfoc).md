# SWFOC Runtime Hardening + Launch Context Detector + Ambitious Roadmap Refresh

## Summary
Implement three coordinated outcomes in order:

1. Harden attach/profile selection against real SWFOC launch realities (`STEAMMOD`, `MODPATH`, `StarWarsG`, command-line-unavailable sessions).
2. Add a deterministic `tools/` launch-context detector that emits normalized JSON usable by runtime, tests, and diagnostics.
3. Rewrite `PLAN.md` into an ambitious but safe-method roadmap (no invasive injection expansion), including custom-mod onboarding beyond the current four profiles.

This plan follows your selected defaults:

- Delivery priority: reliability first, then expansion.
- Detector delivery: standalone script + JSON contract.
- Ambition boundary: high ambition with safe methods.
- Scope: include custom-mod onboarding flow.

## Current Audit Baseline (What Must Be Fixed)

1. `MODPATH` is detected only as a boolean flag, not parsed into a structured local-mod context.  
File: `src/SwfocTrainer.Runtime/Services/ProcessLocator.cs:64`
2. Steam mod ID extraction is workshop-centric and misses many local modpath cases.  
File: `src/SwfocTrainer.Runtime/Services/ProcessLocator.cs:214`
3. UI recommendation prioritizes `STEAMMOD` IDs but does not use normalized `MODPATH` inference.  
File: `src/SwfocTrainer.App/ViewModels/MainViewModel.cs:359`
4. Runtime dependency validation checks workshop roots only, so local `MODPATH` workflows can soft-disable helper actions even when files exist locally.  
File: `src/SwfocTrainer.Runtime/Services/RuntimeAdapter.cs:315`
5. Live tests still choose profile by raw command-line substring checks, which breaks when command line is inaccessible.  
Files: `tests/SwfocTrainer.Tests/Profiles/LiveCreditsTests.cs:47`, `tests/SwfocTrainer.Tests/Profiles/LiveActionSmokeTests.cs:47`
6. Profile metadata key mismatch (`requiredWorkshopId` vs `requiredWorkshopIds`) introduces config inconsistency.  
File: `profiles/default/profiles/aotr_1397421866_swfoc.json:66`

## Important Changes to Public APIs / Interfaces / Types

1. Add `LaunchKind` enum in `src/SwfocTrainer.Core/Models/Enums.cs`.

- Values: `Unknown`, `BaseGame`, `Workshop`, `LocalModPath`, `Mixed`.

2. Add `ProfileRecommendation` record in `src/SwfocTrainer.Core/Models/RuntimeModels.cs`.

- Fields: `ProfileId`, `ReasonCode`, `Confidence`.

3. Add `LaunchContext` record in `src/SwfocTrainer.Core/Models/RuntimeModels.cs`.

- Fields: `LaunchKind`, `CommandLineAvailable`, `SteamModIds`, `ModPathRaw`, `ModPathNormalized`, `DetectedVia`, `Recommendation`.

4. Extend `ProcessMetadata` in `src/SwfocTrainer.Core/Models/RuntimeModels.cs`.

- Add optional `LaunchContext? LaunchContext` property.
- Keep existing `Metadata` dictionary for backward compatibility during migration.

5. Add `ILaunchContextResolver` in `src/SwfocTrainer.Core/Contracts`.

- Method: `LaunchContext Resolve(ProcessMetadata process, IReadOnlyList<TrainerProfile> profiles)`.

6. Add `IModDependencyValidator` in `src/SwfocTrainer.Core/Contracts`.

- Method: `DependencyValidationResult Validate(TrainerProfile profile, ProcessMetadata process)`.

7. Standardize profile metadata contract in `profiles/default/profiles/*.json`.

- Required keys for mod profiles: `requiredWorkshopIds`, `requiredMarkerFile`, `dependencySensitiveActions`.
- Optional keys: `localPathHints`, `localParentPathHints`, `profileAliases`.

## Implementation Plan

## Phase 1: Introduce Shared Launch-Context Model and Resolver

1. Create `LaunchContextResolver` in `src/SwfocTrainer.Runtime/Services/LaunchContextResolver.cs`.
2. Implement normalized parsing rules:

- Parse `STEAMMOD=<id>` from command line.
- Parse `MODPATH=` with quoted and unquoted values.
- Normalize path separators and trim surrounding quotes.
- Detect `StarWarsG` host context.

3. Implement recommendation precedence:

- `STEAMMOD` contains `3447786229` -> `roe_3447786229_swfoc`.
- Else `STEAMMOD` contains `1397421866` -> `aotr_1397421866_swfoc`.
- Else `MODPATH` matches ROE hints -> `roe_3447786229_swfoc`.
- Else `MODPATH` matches AOTR hints -> `aotr_1397421866_swfoc`.
- Else explicit `Sweaw` context -> `base_sweaw`.
- Else `Swfoc` or `StarWarsG` ambiguous -> `base_swfoc` with lower confidence.

4. Emit stable `ReasonCode` values:

- `steammod_exact_roe`, `steammod_exact_aotr`, `modpath_hint_roe`, `modpath_hint_aotr`, `exe_target_sweaw`, `foc_safe_starwarsg_fallback`, `unknown`.

5. Register resolver in DI in `src/SwfocTrainer.App/App.xaml.cs`.

## Phase 2: Wire Resolver into Process Discovery and UI Recommendation

1. Update `ProcessLocator` to populate `LaunchContext` per process.
2. Continue filling legacy metadata keys during migration:

- `steamModIdsDetected`, `commandLineAvailable`, `detectedVia`, `isStarWarsG`.

3. Update `MainViewModel.RecommendProfileIdAsync` to prefer `process.LaunchContext.Recommendation.ProfileId`.
4. Update `BuildAttachProcessHintAsync` and attach status text to include:

- `launchKind`, `modPathNormalized`, `recommendation.reasonCode`, `recommendation.confidence`.

5. Keep fallback behavior unchanged if `LaunchContext` is null.

## Phase 3: Make Dependency Validation MODPATH-Aware

1. Extract dependency logic from `RuntimeAdapter.ValidateModDependencies` to `ModDependencyValidator`.
2. Validate dependencies using both sources:

- Workshop roots discovery.
- Local mod path from `LaunchContext.ModPathNormalized`.

3. New evaluation behavior:

- `Pass` when required workshop IDs are present OR equivalent local mod path markers satisfy declared dependencies.
- `SoftFail` when dependencies are unresolved but attach can continue.
- `HardFail` only for malformed metadata contracts or unsafe marker paths (`..` traversal).

4. Add `SteamLibrary` discovery enhancement:

- Parse `libraryfolders.vdf` from detected Steam install roots before fallback to hardcoded candidates.

5. Preserve existing action gating behavior for soft-failed dependency-sensitive actions.

## Phase 4: Add Tools Script with Stable JSON Contract

1. Add `tools/detect-launch-context.py`.
2. Inputs:

- `--command-line`
- `--process-name`
- `--process-path`
- `--profile-root` (default `profiles/default`)
- `--from-process-json <path>` for batch/offline diagnostics
- `--pretty`

3. Output JSON schema:

- `schemaVersion`
- `generatedAtUtc`
- `input`
- `launchContext`
- `profileRecommendation`
- `dependencyHints`

4. `profileRecommendation` object fields:

- `profileId`
- `reasonCode`
- `confidence`

5. Script rule source:

- Load profile JSON metadata from `profiles/default/profiles/*.json` to avoid hardcoded profile mapping duplication.

6. Exit codes:

- `0` success (including low-confidence recommendations)
- `2` invalid input contract
- `3` profile-root/config parsing error

## Phase 5: Profile Metadata and Contract Cleanup

1. Update `profiles/default/profiles/aotr_1397421866_swfoc.json`:

- Replace `requiredWorkshopId` with `requiredWorkshopIds`.
- Add `localPathHints` and `profileAliases`.

2. Update `profiles/default/profiles/roe_3447786229_swfoc.json`:

- Keep `requiredWorkshopIds`.
- Add `localPathHints`, `localParentPathHints`, `profileAliases`.

3. Optionally add hints to `base_swfoc` and `base_sweaw` for explicit fallback clarity.
4. Update `docs/PROFILE_FORMAT.md` with standardized metadata keys and `LaunchContext` semantics.

## Phase 6: Test Strategy and Coverage Expansion

1. Add deterministic unit tests:

- `tests/SwfocTrainer.Tests/Runtime/LaunchContextResolverTests.cs`
- `tests/SwfocTrainer.Tests/Runtime/ModDependencyValidatorTests.cs`

2. Add test matrix for parser/recommendation:

- `STEAMMOD=3447786229` -> ROE.
- `STEAMMOD=1397421866` -> AOTR.
- `MODPATH="...\\3447786229(submod)"` -> ROE.
- `MODPATH=Mods\\AOTR` -> AOTR (when hint matches).
- `sweaw.exe` no mod markers -> `base_sweaw`.
- `StarWarsG` with no command line -> `base_swfoc` low confidence.

3. Update live tests to use normalized recommendation instead of raw command-line substring checks.
4. Add script fixture checks:

- `tools/fixtures/launch_context_cases.json`
- script smoke command validates output schema and expected recommendation for each fixture.

5. Keep existing live tests but mark recommendation-dependent assertions to use `ReasonCode` + profile output.

## Phase 7: Rewrite PLAN.md and TODO.md into Ambitious but Deliverable Program

1. Replace current `PLAN.md` structure with milestone program:

- `M0` Runtime fidelity and context normalization.
- `M1` Live Action Command Surface (spawn, unit matrix, tactical suite, campaign controls).
- `M2` Save Lab (full-schema editing, diff/patch packs, rollback).
- `M3` Mod Compatibility Studio (profile calibration wizard, custom-mod onboarding, signature calibration flow).
- `M4` Distribution and operational hardening (profile pack update pipeline, telemetry, diagnostics).

2. Add explicit “safe-method ambition” boundaries:

- Allowed: memory actions, helper hooks, save editing, code patches already in architecture.
- Disallowed in this track: new invasive injection architectures beyond current model.

3. Expand feature set in `PLAN.md` with concrete deliverables:

- Catalog-driven spawn presets and batch operations.
- Selected-unit inspector with apply/revert transactions.
- Tactical and campaign action bundles with mode-aware gating.
- Save patch-pack export/import for repeatable edits.
- Custom-mod onboarding workflow to bootstrap profiles from base FoC.

4. Rebase `TODO.md` into execution board:

- `Now` (M0 hardening + detector + tests).
- `Next` (M1 command surface validation).
- `Later` (M2+M3 ambition tracks).
- Every completed item requires evidence link to test output or manual validation note.

## Test Cases and Scenarios (Acceptance)

1. Attach/Profile Recommendation

- Given a live FoC process with `STEAMMOD=3447786229`, UI and script both recommend `roe_3447786229_swfoc`.
- Given `MODPATH` local submod path with no workshop marker, recommendation still resolves to ROE from path hints.
- Given ambiguous `StarWarsG` with no command line, recommendation falls back to `base_swfoc` with low confidence reason code.

2. Dependency Validation

- ROE profile in workshop install with both IDs present -> `Pass`.
- ROE profile launched via local mod path with required marker files present -> `Pass`.
- Missing parent dependency marker -> `SoftFail` and helper actions disabled, attach still succeeds.

3. Runtime Selection

- When both `swfoc.exe` launcher and `StarWarsG.exe` host are present, attach binds to host process for FoC profiles.

4. Tooling Contract

- `tools/detect-launch-context.py` output validates against schema for all fixture inputs.
- Runtime and script produce identical recommendation on same sample command lines.

5. Regression Safety

- Existing non-live tests pass.
- Live tests continue to skip gracefully when no target process exists.
- Profile inheritance tests remain unchanged in outcome for four shipped profiles.

## Assumptions and Defaults

1. Windows remains the runtime target; WSL/Linux use is for tooling and offline diagnostics only.
2. Existing four profiles stay first-class (`base_sweaw`, `base_swfoc`, `aotr_1397421866_swfoc`, `roe_3447786229_swfoc`).
3. Add generic custom-mod onboarding as an extension path, not a blocker for M0/M1.
4. Continue signature-first with fallback offsets; no removal of existing fallback behavior.
5. No new invasive runtime injection strategy is introduced in this roadmap.
6. Local environment here lacks `dotnet` CLI, so implementation validation must be run in your Windows dev environment/CI for compile and test execution.
