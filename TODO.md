# SWFOC Editor Execution Board

Rule for completion: every `[x]` item must include evidence in-line as either:

- `evidence: test <path>` for automated coverage
- `evidence: manual <date> <note>` for live/manual validation

## Now (M0: Runtime Hardening + Detector + Tests)

- [x] Add shared launch-context model (`LaunchKind`, `LaunchContext`, `ProfileRecommendation`) and attach to `ProcessMetadata`.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/LaunchContextResolverTests.cs`
- [x] Implement `ILaunchContextResolver` + `LaunchContextResolver` with STEAMMOD/MODPATH/StarWarsG parsing and reason-code confidence output.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/LaunchContextResolverTests.cs`
- [x] Wire resolver into `ProcessLocator` and populate normalized metadata bridge keys.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/LaunchContextResolverTests.cs`
- [x] Update profile recommendation flow in app to prefer normalized launch-context recommendation.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/LaunchContextResolverTests.cs`
- [x] Introduce `IModDependencyValidator` and move dependency checks out of `RuntimeAdapter`.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/ModDependencyValidatorTests.cs`
- [x] Make dependency resolution MODPATH-aware and include Steam `libraryfolders.vdf` discovery.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/ModDependencyValidatorTests.cs`
- [x] Add `tools/detect-launch-context.py` with stable JSON contract and batch fixture mode.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/LaunchContextScriptSmokeTests.cs`
- [x] Add detector fixtures for STEAMMOD/MODPATH/base fallback cases.
  evidence: fixture `tools/fixtures/launch_context_cases.json`
- [x] Normalize profile metadata contract in shipped profiles (`requiredWorkshopIds`, hints, aliases).
  evidence: profile `profiles/default/profiles/aotr_1397421866_swfoc.json`
- [x] Update live tests to use normalized recommendation semantics instead of raw command-line substring checks.
  evidence: test `tests/SwfocTrainer.Tests/Profiles/LiveCreditsTests.cs`
- [x] Refresh roadmap docs (`PLAN.md`, `TODO.md`, `docs/PROFILE_FORMAT.md`) for ambitious safe-method track.
  evidence: docs `PLAN.md`

## Next (M1: Live Action Command Surface Validation)

- [ ] Validate spawn presets + batch operations across `base_swfoc`, `aotr`, and `roe`.
- [ ] Add selected-unit apply/revert transaction path and verify rollback behavior.
- [ ] Enforce tactical/campaign mode-aware gating for all high-impact action bundles.
- [ ] Publish action reliability flags (`stable`, `experimental`, `unavailable`) in UI diagnostics.
- [ ] Add live smoke coverage for tactical toggles and hero-state helper workflows.

## Later (M2 + M3 + M4 Ambition Tracks)

- [ ] Save Lab patch-pack export/import with deterministic compatibility checks.
- [ ] Extend save schema validation coverage and corpus round-trip checks.
- [ ] Build custom-mod onboarding wizard (bootstrap profile, hints, dependency contract scaffolding).
- [ ] Add signature calibration flow and compatibility report card for newly onboarded mods.
- [ ] Implement profile-pack operational hardening (rollback-safe updates + diagnostics bundle export).
