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
- [x] Add live smoke coverage for tactical toggles and hero-state helper workflows.
  evidence: test `tests/SwfocTrainer.Tests/Profiles/LiveTacticalToggleWorkflowTests.cs`
  evidence: test `tests/SwfocTrainer.Tests/Profiles/LiveHeroHelperWorkflowTests.cs`
  evidence: manual `2026-02-15` `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build` passed 69 tests, skipped 4 live-gated tests
- [ ] Execute live-machine M1 validation run and attach evidence to issues `#34` and `#19`.
  note: local attempt `2026-02-16` produced explicit skips because no live SWFOC process was running.
  note: use `pwsh ./tools/run-live-validation.ps1 -Configuration Release -NoBuild -Scope FULL -EmitReproBundle $true` and post templates from `TestResults/runs/<runId>/` to `#34` and `#19`.
  evidence-progress: `#34` comment `https://github.com/Prekzursil/SWFOC-Mod-Menu/issues/34#issuecomment-3905270711`
  evidence-progress: `#19` comment `https://github.com/Prekzursil/SWFOC-Mod-Menu/issues/19#issuecomment-3905270874`
- [x] Close/reconcile M0 carryover issues (`#15`, `#16`, `#17`, `#18`) with evidence comments.
  evidence: issue `https://github.com/Prekzursil/SWFOC-Mod-Menu/issues/15`
- [x] Track plan archive under `(new)codex(plans)/` with explicit contributor convention.
  evidence: doc `(new)codex(plans)/README.md`

## Later (M2 + M3 + M4)

- [ ] Save Lab patch-pack export/import with deterministic compatibility checks.
- [ ] Extend save schema validation coverage and corpus round-trip checks.
- [ ] Build custom-mod onboarding wizard (bootstrap profile + hint/dependency scaffolding).
- [ ] Add signature calibration flow and compatibility report card for newly onboarded mods.
- [ ] Implement profile-pack operational hardening (rollback-safe updates + diagnostics bundle export).
