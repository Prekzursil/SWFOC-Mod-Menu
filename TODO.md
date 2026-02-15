# SWFOC Editor Execution Board

Rule for completion: every `[x]` item includes evidence as one of:

- `evidence: test <path>`
- `evidence: manual <date> <note>`
- `evidence: issue <url>`

## Now (Bootstrap + M0 Reliability)

- [x] Initialize git repo in workspace, apply hardened ignore rules, and push first `main` branch to `Prekzursil/SWFOC-Mod-Menu`.
  evidence: manual `2026-02-15` first push commit `4bea67a`
- [x] Configure GitHub governance and automation baseline (MIT license, README/CONTRIBUTING/SECURITY/CODE_OF_CONDUCT, issue templates, PR template, dependabot, CI/release workflows).
  evidence: manual `2026-02-15` files under `.github/` + root governance docs
- [x] Apply repository metadata/settings (description, topics, issues/projects enabled, wiki disabled) and protect `main` (PR required, 1 review, stale-dismissal, conversation resolution, force-push/delete blocked, required check).
  evidence: manual `2026-02-15` `gh repo view` + branch protection API response
- [x] Seed roadmap milestones, label taxonomy, epic issues, and first-sprint actionable issues.
  evidence: issue `https://github.com/Prekzursil/SWFOC-Mod-Menu/issues/6`
- [ ] Create GitHub Project board `SWFOC-Mod-Menu Roadmap` with `Now/Next/Later` lanes and link seeded issues.
  evidence: blocked `2026-02-15` token lacks `project` scope (`Resource not accessible by personal access token`)
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
  evidence: manual `2026-02-15` `dotnet test ... --filter \"FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests\"` passed 51 tests

## Next (M1: Live Action Command Surface)

- [ ] Validate spawn presets + batch operations across `base_swfoc`, `aotr`, and `roe`.
- [ ] Add selected-unit apply/revert transaction path and verify rollback behavior.
- [ ] Enforce tactical/campaign mode-aware gating for high-impact action bundles.
- [ ] Publish action reliability flags (`stable`, `experimental`, `unavailable`) in UI diagnostics.
- [ ] Add live smoke coverage for tactical toggles and hero-state helper workflows.

## Later (M2 + M3 + M4)

- [ ] Save Lab patch-pack export/import with deterministic compatibility checks.
- [ ] Extend save schema validation coverage and corpus round-trip checks.
- [ ] Build custom-mod onboarding wizard (bootstrap profile + hint/dependency scaffolding).
- [ ] Add signature calibration flow and compatibility report card for newly onboarded mods.
- [ ] Implement profile-pack operational hardening (rollback-safe updates + diagnostics bundle export).
