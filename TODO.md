# SWFOC Editor Execution Board

Rule for completion: every `[x]` item includes evidence as one of:

- `evidence: test <path>`
- `evidence: manual <date> <note>`
- `evidence: issue <url>`
- `evidence: bundle TestResults/runs/<runId>/repro-bundle.json`

Reliability rule for runtime/mod tasks:

- issue and PR evidence must include `runId`, classification, launch `reasonCode`, and bundle link.

## Now (Bootstrap + M0 Reliability)

- [x] Initialize git repo in workspace, apply hardened ignore rules, and push first `main` branch to `Prekzursil/SWFOC-Mod-Menu`.
  evidence: manual `2026-02-15` first push commit `4bea67a`
- [x] Configure GitHub governance and automation baseline (MIT license, README/CONTRIBUTING/SECURITY/CODE_OF_CONDUCT, issue templates, PR template, dependabot, CI/release workflows).
  evidence: manual `2026-02-15` files under `.github/` + root governance docs
- [x] Apply repository metadata/settings (description, topics, issues/projects enabled, wiki disabled) and protect `main` (PR required, 1 review, stale-dismissal, conversation resolution, force-push/delete blocked, required check).
  evidence: manual `2026-02-15` `gh repo view` + branch protection API response
- [x] Seed roadmap milestones, label taxonomy, epic issues, and first-sprint actionable issues.
  evidence: issue `https://github.com/Prekzursil/SWFOC-Mod-Menu/issues/6`
- [x] Create GitHub Project board `SWFOC-Mod-Menu Roadmap` with `Now/Next/Later` lanes and link seeded issues.
  evidence: issue `https://github.com/users/Prekzursil/projects/1`
- [x] Add symbol health model (`Healthy/Degraded/Unresolved`) and runtime diagnostics enrichment (health/confidence/source per symbol).
  evidence: test `tests/SwfocTrainer.Tests/Runtime/SymbolHealthServiceTests.cs`
- [x] Add critical write reliability path (value sanity checks + single re-resolve retry + explicit failure reason codes).
  evidence: test `tests/SwfocTrainer.Tests/Runtime/SymbolHealthServiceTests.cs`
- [x] Emit attach-time calibration artifact snapshot with module fingerprint + launch context + symbol policy.
  evidence: code `src/SwfocTrainer.Runtime/Services/RuntimeAdapter.cs`
- [x] Extend profile metadata contract with `criticalSymbols` and `symbolValidationRules`; document in profile format doc.
  evidence: profile `profiles/default/profiles/base_swfoc.json`
- [x] Keep launch-context parity and dependency validation suites green.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/LaunchContextResolverTests.cs`
- [x] Deterministic test suite passes with live/process tests excluded.
  evidence: manual `2026-02-15` `dotnet test ... --filter \"FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests\"` passed 67 tests

## Next (M1: Live Action Command Surface)

- [x] Validate spawn presets + batch operations across `base_swfoc`, `aotr`, and `roe`.
  evidence: test `tests/SwfocTrainer.Tests/Core/SpawnPresetServiceTests.cs`
- [x] Add selected-unit apply/revert transaction path and verify rollback behavior.
  evidence: test `tests/SwfocTrainer.Tests/Core/SelectedUnitTransactionServiceTests.cs`
- [x] Enforce tactical/campaign mode-aware gating for high-impact action bundles.
  evidence: code `src/SwfocTrainer.Core/Services/SelectedUnitTransactionService.cs`
- [x] Publish action reliability flags (`stable`, `experimental`, `unavailable`) in UI diagnostics.
  evidence: test `tests/SwfocTrainer.Tests/Core/ActionReliabilityServiceTests.cs`
- [x] Close/reconcile M0 carryover issues (`#15`, `#16`, `#17`, `#18`) with evidence comments.
  evidence: issue `https://github.com/Prekzursil/SWFOC-Mod-Menu/issues/15`
- [x] Track plan archive under `(new)codex(plans)/` with explicit contributor convention.
  evidence: doc `(new)codex(plans)/README.md`
- [x] M2/S1 define Save Lab patch-pack contract + schema fixture (`tools/schemas/save-patch-pack.schema.json`).
  evidence: test `tests/SwfocTrainer.Tests/Saves/SavePatchPackServiceTests.cs`
- [x] M2/S2 implement patch-pack export/import + compatibility preview service.
  evidence: test `tests/SwfocTrainer.Tests/Saves/SavePatchPackServiceTests.cs`
- [x] M2/S3 implement atomic apply + backup/receipt + rollback pipeline.
  evidence: test `tests/SwfocTrainer.Tests/Saves/SavePatchApplyServiceTests.cs`
- [x] M2/S4 integrate Save Lab patch-pack UX into Save Editor tab.
  evidence: code `src/SwfocTrainer.App/MainWindow.xaml`
- [x] M2/S5 add deterministic CI/schema tooling for patch-pack contract.
  evidence: workflow `.github/workflows/ci.yml`
  evidence: tool `tools/validate-save-patch-pack.ps1`
- [x] M2 hardening wave: strict apply toggle, preview target-profile fix, field selector fallback, contract enforcement (`newValue` required), sanitized Save Lab failures, and receipt fallback recovery.
  evidence: test `tests/SwfocTrainer.Tests/Saves/SavePatchPackServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Saves/SavePatchApplyServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Saves/SaveCodecTests.cs`
  evidence: manual `2026-02-18` `dotnet build SwfocTrainer.sln -c Release --no-restore`
  evidence: manual `2026-02-18` `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --filter "FullyQualifiedName~SavePatchPackServiceTests|FullyQualifiedName~SavePatchApplyServiceTests"` => `Passed: 24`
  evidence: manual `2026-02-18` `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"` => `Passed: 92`
  evidence: manual `2026-02-18` `pwsh.exe ./tools/validate-save-patch-pack.ps1 -PatchPackPath tools/fixtures/save_patch_pack_sample.json -SchemaPath tools/schemas/save-patch-pack.schema.json -Strict` => `validation passed`
  evidence: manual `2026-02-18` `pwsh.exe ./tools/export-save-patch-pack.ps1 ...` + `pwsh.exe ./tools/apply-save-patch-pack.ps1 ... -Strict:$true` => `Classification=Applied`, backup/receipt under `TestResults/m2s6-smoke-20260218012211/`
  evidence: manual `2026-02-18` Save Lab summary persistence verified in code path (`ApplyPatchPackAsync`/`RestoreBackupAsync` assign summary after reload + re-append backup/receipt rows)
- [x] Add REST-only reviewer automation with soft fallback when no non-author reviewer is available.
  evidence: code `tools/request-pr-reviewers.ps1`
  evidence: workflow `.github/workflows/reviewer-automation.yml`
  evidence: config `config/reviewer-roster.json`
  evidence: doc `docs/REVIEWER_AUTOMATION.md`
  evidence: manual `2026-02-17` `pwsh ./tools/request-pr-reviewers.ps1 -RepoOwner Prekzursil -RepoName SWFOC-Mod-Menu -PullNumber 46 -RosterPath config/reviewer-roster.json` (fallback label/comment path)
  evidence: issue `https://github.com/Prekzursil/SWFOC-Mod-Menu/pull/46`
- [x] Queue M2/S6 hardening as draft PR stub from `feature/m2-save-lab-next-slice`.
  evidence: issue `https://github.com/Prekzursil/SWFOC-Mod-Menu/pull/47`
- [x] Universal compatibility wave: metadata-driven launch recommendation for known/generated profiles, onboarding seed enrichment, Live Ops calibration scan panel, and fail-safe fallback patch actions (`toggle_fog_reveal_patch_fallback`, `set_unit_cap_patch_fallback`).
  evidence: test `tests/SwfocTrainer.Tests/Runtime/LaunchContextResolverTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/ModOnboardingServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Core/ActionReliabilityServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/App/MainViewModelSessionGatingTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/BackendRouterTests.cs`
  evidence: tool `tools/validate-workshop-topmods.ps1`
  evidence: tool `tools/validate-generated-profile-seed.ps1`
- [x] M1 closure live matrix (non-skipped) with forced-context diagnostics + promoted-action matrix pass gate.
  evidence: bundle `TestResults/runs/20260228-064134/repro-bundle.json`
  evidence: bundle `TestResults/runs/20260228-063938/repro-bundle.json`
  evidence: manual `2026-02-28` `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"` => `Passed: 189`
- [x] M2 truthiness + flow-intelligence closure: probe hardening, patch-safe writes, managed FoC credits authority, MEG/effective-index/telemetry/story-flow/lua-harness stack, and strict tooling exports.
  evidence: doc `docs/plans/2026-02-28-m2-truthiness-flow-intelligence.md`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/NamedPipeExtenderBackendTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/RuntimeAdapterPromotedAliasTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Meg/MegArchiveReaderTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/DataIndex/EffectiveGameDataIndexServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Flow/StoryFlowGraphExporterTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Flow/LuaHarnessRunnerTests.cs`
  evidence: manual `2026-02-28` `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"` => `Passed: 213`
  evidence: manual `2026-02-28` `pwsh ./tools/research/export-effective-data-index.ps1 -ProfileId base_swfoc -OutPath TestResults/index/base_swfoc_effective_index.json -Strict`
  evidence: manual `2026-02-28` `pwsh ./tools/research/export-story-flow-graph.ps1 -ProfileId roe_3447786229_swfoc -OutPath TestResults/flow/roe_flow_graph.json -Strict`
  evidence: manual `2026-02-28` `pwsh ./tools/lua-harness/run-lua-harness.ps1 -Strict`
  evidence: bundle `TestResults/runs/20260228-171028/repro-bundle.json` (`classification=blocked_environment`)
  evidence: bundle `TestResults/runs/20260228-171159/repro-bundle.json` (`classification=blocked_environment`)
- [x] Functional closure wave: deterministic native host bootstrap, promoted `set_unit_cap` enable->disable matrix semantics, helper forced-profile fallback diagnostics, and Codex-launched live process matrix (EAW + SWFOC tactical + AOTR + ROE) with strict bundle validation.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/ProcessLocatorForcedContextTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/LivePromotedActionMatrixTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/LiveHeroHelperWorkflowTests.cs`
  evidence: manual `2026-02-28` `dotnet build SwfocTrainer.sln -c Release --no-restore` => `Build succeeded`
  evidence: manual `2026-02-28` `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"` => `Passed: 192`
  evidence: manual `2026-02-28` `powershell.exe -File tools/validate-workshop-topmods.ps1 -Path tools/fixtures/workshop_topmods_sample.json -Strict` => `validation passed`
  evidence: manual `2026-02-28` `powershell.exe -File tools/validate-generated-profile-seed.ps1 -Path tools/fixtures/generated_profile_seeds_sample.json -Strict` => `validation passed`
  evidence: manual `2026-02-28` Session A EAW snapshot `TestResults/runs/LIVE-EAW-20260228-204540/eaw-process-snapshot.json`
  evidence: bundle `TestResults/runs/LIVE-TACTICAL-20260228-211256/repro-bundle.json`
  evidence: bundle `TestResults/runs/LIVE-AOTR-20260228-211521/repro-bundle.json`
  evidence: bundle `TestResults/runs/LIVE-ROE-20260228-211757/repro-bundle.json`
- [x] Delta closure wave: default non-forced promoted FoC routing + env override (`SWFOC_FORCE_PROMOTED_EXTENDER`), `universal_auto` backend de-trap (`auto`), and profile metadata save-root portability cleanup.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/BackendRouterTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/ProfileInheritanceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/ProfileMetadataPortabilityTests.cs`
  evidence: manual `2026-03-01` `dotnet restore SwfocTrainer.sln` + `dotnet build SwfocTrainer.sln -c Release --no-restore` + `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"` => `Passed: 233`
  evidence: bundle `TestResults/runs/20260301-004145/repro-bundle.json` (`classification=blocked_environment`, tactical default routing run; no swfoc process detected)
  evidence: bundle `TestResults/runs/20260301-004232/repro-bundle.json` (`classification=blocked_environment`, tactical forced-override run; no swfoc process detected)
- [x] M3 closure wave: helper bridge fail-closed runtime path, `Launch + Attach` automation, strict tactical mode split (`TacticalLand`/`TacticalSpace`/`AnyTactical`), and codex-owned live process matrix rerun with schema-validated bundles.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/NamedPipeHelperBridgeBackendTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/NamedPipeExtenderBackendTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/LivePromotedActionMatrixTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/LiveRoeRuntimeHealthTests.cs`
  evidence: manual `2026-03-01` `dotnet restore SwfocTrainer.sln` + `dotnet build SwfocTrainer.sln -c Release --no-restore` + `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"` => `Passed: 237`
  evidence: manual `2026-03-01` `powershell.exe -File tools/validate-workshop-topmods.ps1 -Path tools/fixtures/workshop_topmods_sample.json -Strict` => `validation passed`
  evidence: manual `2026-03-01` `powershell.exe -File tools/validate-generated-profile-seed.ps1 -Path tools/fixtures/generated_profile_seeds_sample.json -Strict` => `validation passed`
  evidence: manual `2026-03-01` Session A EAW snapshot `TestResults/runs/LIVE-EAW-20260301-191639/eaw-process-snapshot.json`
  evidence: bundle `TestResults/runs/20260301-164213/repro-bundle.json` (`classification=passed`, scope `TACTICAL`)
  evidence: bundle `TestResults/runs/20260301-165502/repro-bundle.json` (`classification=passed`, scope `AOTR`)
  evidence: bundle `TestResults/runs/20260301-171325/repro-bundle.json` (`classification=skipped`, scope `ROE`, reason `set_credits precondition unmet: hook sync tick not observed`)
- [x] M4 execution wave: installed workshop/submod intelligence, chain-aware auto-launch, per-action mechanic gating, universal context faction routing, and expanded live evidence matrix (baseline + installed submod smokes).
  evidence: test `tests/SwfocTrainer.Tests/Runtime/GameLaunchServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/WorkshopInventoryServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/ModMechanicDetectionServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/RuntimeAdapterContextFactionRoutingTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Core/ActionReliabilityServiceTests.cs`
  evidence: manual `2026-03-02` `dotnet restore SwfocTrainer.sln` + `dotnet build SwfocTrainer.sln -c Release --no-restore` + `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"` => `Passed: 254`
  evidence: manual `2026-03-02` `powershell.exe -File tools/validate-workshop-topmods.ps1 -Path tools/fixtures/workshop_topmods_sample.json -Strict` => `validation passed`
  evidence: manual `2026-03-02` `powershell.exe -File tools/validate-generated-profile-seed.ps1 -Path tools/fixtures/generated_profile_seeds_sample.json -Strict` => `validation passed`
  evidence: manual `2026-03-02` installed graph `TestResults/mod-discovery/20260302-170047/installed-mod-graph.json` (`installedCount=23`, submod parent chains inferred)
  evidence: bundle `TestResults/runs/20260302-164220/repro-bundle.json` (`classification=passed`, scope `TACTICAL`)
  evidence: bundle `TestResults/runs/20260302-164500/repro-bundle.json` (`classification=passed`, scope `AOTR`, `launchContext.source=forced`)
  evidence: bundle `TestResults/runs/20260302-164838/repro-bundle.json` (`classification=skipped`, scope `ROE`, reason `set_credits precondition unmet: hook sync tick not observed`)
  evidence: bundle `TestResults/runs/M4-SUBMOD-3447786229-20260302-190617/repro-bundle.json` (`classification=passed`, chain `1397421866,3447786229`)
  evidence: bundle `TestResults/runs/M4-SUBMOD-3287776766-20260302-190708/repro-bundle.json` (`classification=blocked_environment`, transient no-process attach)
  evidence: bundle `TestResults/runs/M4-SUBMOD-3287776766-RERUN-20260302-191443/repro-bundle.json` (`classification=passed`, rerun confirmation chain `1397421866,3287776766`)
  evidence: bundle `TestResults/runs/M4-SUBMOD-2361851963-20260302-190742/repro-bundle.json` (`classification=passed`, chain `1125571106,2361851963`)
  evidence: bundle `TestResults/runs/M4-SUBMOD-2083545253-20260302-190826/repro-bundle.json` (`classification=passed`, chain `1125571106,2083545253`)
  evidence: bundle `TestResults/runs/M4-SUBMOD-2083545253-20260302-190934/repro-bundle.json` (`classification=passed`, chain `1976399102,2083545253`)
  evidence: bundle `TestResults/runs/M4-SUBMOD-2794270450-20260302-191050/repro-bundle.json` (`classification=passed`, chain `1770851727,2794270450`)
  evidence: bundle `TestResults/runs/M4-SUBMOD-3661482670-20260302-191139/repro-bundle.json` (`classification=passed`, chain `1125571106,3661482670`)
  evidence: manual `2026-03-03` installed graph delta `TestResults/mod-discovery/LIVE-NEWMOD-2361944372-20260303/installed-mod-graph.json` (`installedCount=25`, added `1780988753`, `2361944372`)
  evidence: bundle `TestResults/runs/LIVE-NEWMOD-1780988753-20260303/repro-bundle.json` (`classification=skipped`, chain `1780988753`)
  evidence: bundle `TestResults/runs/LIVE-NEWMOD-2361944372-20260303/repro-bundle.json` (`classification=skipped`, chain `2361944372`)
  evidence: manual `2026-03-03` full deep-chain matrix `TestResults/runs/LIVE-M4-DEEP-20260302/chain-matrix-summary.json` (`entries=28`, `skipped=26`, `blocked_environment=2`, failed chains `2313576303` and `1976399102>3661482670`)
  evidence: bundle `TestResults/runs/LIVE-M4-DEEP-20260302-chain16/repro-bundle.json` (`classification=blocked_environment`, reason `ATTACH_NO_PROCESS` / process drop)
  evidence: bundle `TestResults/runs/LIVE-M4-DEEP-20260302-chain27/repro-bundle.json` (`classification=blocked_environment`, process dropped during promoted matrix attach)
  evidence: bundle `TestResults/runs/LIVE-M4-RERUN-CHAIN16-20260303/repro-bundle.json` (`classification=blocked_environment`, persistent chain16 blocker)
  evidence: bundle `TestResults/runs/LIVE-M4-RERUN-CHAIN27-20260303/repro-bundle.json` (`classification=skipped`, transient chain27 blocker cleared on rerun)

## M5 (Mega PR In Progress)

- [x] Extend runtime/helper evidence bundle contract with M5 sections (`heroMechanicsSummary`, `operationPolicySummary`, `fleetTransferSafetySummary`, `planetFlipSummary`, `entityTransplantBlockers`).
  evidence: code `tools/schemas/repro-bundle.schema.json`
  evidence: code `tools/collect-mod-repro-bundle.ps1`
  evidence: code `tools/validate-repro-bundle.ps1`
  evidence: manual `2026-03-04` `pwsh -ExecutionPolicy Bypass -File ./tools/validate-repro-bundle.ps1 -BundlePath TestResults/runs/20260304-043659-chain28/repro-bundle.json -SchemaPath tools/schemas/repro-bundle.schema.json -Strict` => `validation passed`
- [x] Enforce tactical/galactic helper policy defaults and fail-closed placement/build guards in runtime adapter helper path.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/RuntimeAdapterExecuteCoverageTests.cs`
- [x] Emit hero mechanics summary diagnostics from mod-mechanic detection.
  evidence: test `tests/SwfocTrainer.Tests/Runtime/ModMechanicDetectionServiceTests.cs`
- [x] Complete full installed-chain deep live matrix run with strict bundle validation and chain16 missing-parent dependency block semantics.
  evidence: bundle `TestResults/runs/20260304-043659/chain-matrix-summary.json`
  evidence: bundle `TestResults/runs/20260304-043659/chain-matrix-summary.json` (row `chain16` => `classification=blocked_dependency_missing_parent`, `launchAttempted=false`, `missingParentIds=[2486018498]`)
  evidence: bundle `TestResults/runs/20260304-043659-chain28/repro-bundle.json`
  evidence: bundle `TestResults/runs/20260304-102055/chain-matrix-summary.json` (entries=28, blocked_dependency_missing_parent=2, blocked_environment=0)
  evidence: bundle `TestResults/runs/20260304-102055/chain-matrix-summary.json` (row `20260304-102055-chain16` / chainId `2313576303` => `classification=blocked_dependency_missing_parent`, `launchAttempted=false`, `missingParentIds=[2486018498]`)
  evidence: bundle `TestResults/runs/20260304-102055-chain28/repro-bundle.json`
  evidence: manual `2026-03-04` `pwsh -ExecutionPolicy Bypass -File ./tools/run-live-validation.ps1 -Configuration Release -NoBuild -Scope FULL -AutoLaunch -RunAllInstalledChainsDeep -EmitReproBundle $true -FailOnMissingArtifacts -Strict` => `completed: 28 chain entries, blocked_environment=0`
- [x] Add app-side chain entity roster surface and hero mechanics status panel, plus payload defaults for M5 action families.
  evidence: code `src/SwfocTrainer.App/MainWindow.xaml`
  evidence: code `src/SwfocTrainer.App/ViewModels/MainViewModelLiveOpsBase.cs`
  evidence: test `tests/SwfocTrainer.Tests/App/MainViewModelM5CoverageTests.cs`
- [x] Deterministic non-live gate remains green after M5 app/runtime updates.
  evidence: manual `2026-03-04` `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"` => `Passed: 666`
- [ ] M5 strict coverage closure to `100/100` for handwritten `src/**` scope remains open.
  evidence: manual `2026-03-04` `pwsh -ExecutionPolicy Bypass -File ./tools/quality/assert-dotnet-coverage.ps1 -CoveragePath TestResults/coverage/cobertura.xml -MinLine 100 -MinBranch 100 -Scope src` => `failed (line=72.28, branch=59.95)`
- [ ] M5 helper ingress still lacks proven in-process game mutation verification path for spawn/build/allegiance operations and remains fail-closed target for completion.
  evidence: code `native/SwfocExtender.Plugins/src/HelperLuaPlugin.cpp`

## Later (M2 + M3 + M4)

- [x] Extend save schema validation coverage and corpus round-trip checks.
  evidence: test `tests/SwfocTrainer.Tests/Saves/SaveCorpusRoundTripTests.cs`
  evidence: manual `2026-02-18` `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~SaveCorpusRoundTripTests"` => `Passed: 1`
- [x] Build custom-mod onboarding wizard (bootstrap profile + hint/dependency scaffolding).
  evidence: code `src/SwfocTrainer.Profiles/Services/ModOnboardingService.cs`
  evidence: code `src/SwfocTrainer.App/MainWindow.xaml`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/ModOnboardingServiceTests.cs`
- [x] Add signature calibration flow and compatibility report card for newly onboarded mods.
  evidence: code `src/SwfocTrainer.Core/Services/ModCalibrationService.cs`
  evidence: schema `tools/schemas/calibration-artifact.schema.json`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/ModCalibrationServiceTests.cs`
  evidence: manual `2026-02-18` `powershell.exe -File tools/validate-calibration-artifact.ps1 -ArtifactPath tools/fixtures/calibration_artifact_sample.json -SchemaPath tools/schemas/calibration-artifact.schema.json -Strict`
- [x] Implement profile-pack operational hardening (rollback-safe updates + diagnostics bundle export).
  evidence: code `src/SwfocTrainer.Profiles/Services/GitHubProfileUpdateService.cs`
  evidence: code `src/SwfocTrainer.Core/Services/SupportBundleService.cs`
  evidence: code `src/SwfocTrainer.Core/Services/TelemetrySnapshotService.cs`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/ProfileUpdateServiceTransactionalTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Core/SupportBundleServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Core/TelemetrySnapshotServiceTests.cs`
- [x] Implement deployment strategy on GitHub Releases (portable artifact + checksum + runbook).
  evidence: workflow `.github/workflows/release-portable.yml`
  evidence: doc `docs/RELEASE_RUNBOOK.md`
  evidence: doc `docs/release-notes-template.md`
- [x] R&D Deep RE-first universal profile foundation (fingerprint/capability pipeline, universal auto resolver, SDK safety gating, runtime mode probe metadata).
  evidence: code `src/SwfocTrainer.Runtime/Services/ProfileVariantResolver.cs`
  evidence: code `src/SwfocTrainer.Runtime/Services/RuntimeModeProbeResolver.cs`
  evidence: code `src/SwfocTrainer.Core/Services/SdkOperationRouter.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/BinaryFingerprintServiceTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/CapabilityMapResolverTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/ProfileVariantResolverTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/RuntimeModeProbeResolverTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Runtime/SdkExecutionGuardTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Core/SdkOperationRouterTests.cs`
  evidence: tool `tools/research/run-capability-intel.ps1`
  evidence: tool `tools/validate-binary-fingerprint.ps1`
  evidence: tool `tools/validate-signature-pack.ps1`

