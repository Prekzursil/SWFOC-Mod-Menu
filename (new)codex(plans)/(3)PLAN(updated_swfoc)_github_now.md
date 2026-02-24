# SWFOC-Mod-Menu Bootstrap + Realtime Reliability Plan

## Summary

This plan bootstraps `https://github.com/Prekzursil/SWFOC-Mod-Menu` from the current workspace as a clean, code-focused repo, then configures GitHub operations end-to-end (description, governance, CI, backlog, project board, issue/PR flow), and finally defines an implementation track to make runtime value resolution reliable across build/mod variance.

Current facts discovered from environment:

1. Local workspace is **not** a git repo yet.
2. Remote repo exists and is empty (no branches, no contents, no issues/PRs).
3. Workspace contains ~25 GB due to local mod mirrors; many files exceed GitHub normal file limits.
4. `dotnet` is not available in this shell, so compile/test verification must run in Windows/GitHub Actions.

## Locked decisions (confirmed)

1. First push scope: **Code + profiles only**.
2. License: **MIT**.
3. Bootstrap depth: **Full PM setup**.
4. Migration mode: **Initialize this workspace and push**.
5. CI scope: **Build + deterministic tests**.
6. Realtime reliability strategy: **Hybrid stability-first**.
7. SDK baseline: **.NET 8 LTS pin**.
8. Project tracking: **Create GitHub Project board**.
9. Issue seeding: **Epics + actionable first-sprint tasks**.

## Phase 1 — Repository bootstrap (safe first push)

1. Initialize git in current workspace with `main` as default branch.
2. Harden `.gitignore` to permanently exclude local heavyweight mod mirrors and generated outputs.
3. Exclude these paths explicitly: `1397421866(original mod)/`, `3447786229(submod)/`, `3661482670(cheat_mode_example)/`, `artifacts/`, all `bin/` and `obj/`, plus local-path helper files as needed.
4. Add/refresh `.gitattributes` for consistent line endings and binary handling.
5. Pin SDK to .NET 8 in `global.json` with an 8.x-compatible roll-forward policy.
6. Perform first commit with curated source/docs/profiles/tests/tooling only.
7. Add remote `origin` to `Prekzursil/SWFOC-Mod-Menu` and push `main` as first branch.

## Phase 2 — GitHub repo metadata and settings

1. Set repository description to: `Profile-driven SWFOC trainer/editor for base game, AOTR, and ROE with launch-context detection, save tooling, and calibration-first runtime reliability.`
2. Set homepage URL initially blank; keep public visibility.
3. Keep Issues and Projects enabled; disable Wiki unless actively used.
4. Add repository topics: `swfoc`, `empire-at-war`, `trainer`, `modding`, `dotnet`, `wpf`, `game-hacking`, `save-editor`, `aotr`, `roe`.
5. Configure default branch `main`.
6. Apply branch protection/ruleset for `main`: PR required, at least 1 review, stale review dismissal, conversation resolution required, force-push/delete blocked, required status checks enabled.

## Phase 3 — Governance files and contributor UX

1. Add `LICENSE` (MIT).
2. Add/refresh `README.md` for this new repo context; include architecture, supported profiles, setup, CI, and calibration workflow.
3. Add `CONTRIBUTING.md` with branch naming, commit/PR conventions, test commands, and evidence requirements.
4. Add `SECURITY.md` with disclosure process and scope.
5. Add `CODEOWNERS` with initial owner mapping.
6. Add `.github/pull_request_template.md` with checklist for runtime safety, profile metadata updates, and test evidence.
7. Add issue forms under `.github/ISSUE_TEMPLATE/`: `bug.yml`, `feature.yml`, `calibration.yml`, plus `config.yml`.
8. Add `CODE_OF_CONDUCT.md` (Contributor Covenant).

## Phase 4 — CI/CD and automation baseline

1. Replace/split workflows into:
   1. `ci.yml`: restore, build, deterministic tests, launch-context fixture smoke checks.
   2. `release-portable.yml`: manual/tag-triggered portable packaging artifact.
2. CI runtime:
   1. `runs-on: windows-latest`.
   2. `actions/setup-dotnet` pinned to `8.0.x`.
   3. Optional `actions/setup-python` for tooling smoke tests.
3. Deterministic test execution:
   1. Use test filter to exclude live/process-dependent classes by name pattern.
   2. Include runtime/parser/validation/save/core tests.
4. Artifacts:
   1. Upload test results (`trx`).
   2. Upload packaged portable ZIP on release workflow.
5. Add `dependabot.yml` for NuGet and GitHub Actions updates.

## Phase 5 — Backlog and project management seeding

1. Create milestones aligned to roadmap with concrete names:
   1. `M0 Runtime Fidelity + Context Normalization`
   2. `M1 Live Action Command Surface`
   3. `M2 Save Lab`
   4. `M3 Mod Compatibility Studio`
   5. `M4 Distribution + Ops Hardening`
2. Create label taxonomy:
   1. Type labels: `type:bug`, `type:feature`, `type:chore`, `type:docs`, `type:test`, `type:calibration`.
   2. Area labels: `area:runtime`, `area:app`, `area:profiles`, `area:saves`, `area:ci`, `area:tooling`, `area:docs`.
   3. Priority labels: `priority:p0` to `priority:p3`.
   4. Mod labels: `mod:base`, `mod:aotr`, `mod:roe`, `mod:custom`.
3. Seed epic issues (one per milestone) with acceptance criteria and linked roadmap sections.
4. Seed first-sprint actionable issues from current `TODO.md` and `PLAN.md`:
   1. Repo hygiene and first push.
   2. CI deterministic filtering.
   3. Launch-context parity checks.
   4. Symbol health telemetry.
   5. Re-resolve-on-failure write path.
   6. Calibration artifact capture tool.
   7. Docs and contributor guardrails.
   8. Live calibration checklist for AOTR/ROE.
5. Create one GitHub Project board `SWFOC-Mod-Menu Roadmap` with columns `Now`, `Next`, `Later`, auto-linked to seeded issues and milestones.

## Phase 6 — Realtime value reliability program (hybrid stability-first)

1. Keep current hierarchy but formalize it:
   1. Signature-first resolution.
   2. Validated fallback offsets.
   3. Hook/patch path for specific unstable symbols when justified.
2. Add symbol health model:
   1. Per-symbol health status (`Healthy`, `Degraded`, `Unresolved`).
   2. Source confidence and last validation result.
   3. Diagnostics surfaced to UI and logs.
3. Add runtime validation and retry:
   1. Symbol read sanity checks by value type/range.
   2. Re-resolve-on-failure before write for critical actions.
   3. Controlled retry path with explicit failure reason codes.
4. Add calibration artifact flow:
   1. Capture module fingerprint and candidate signature data.
   2. Emit calibration report artifact for issue attachment.
   3. Document and template this in `calibration.yml` issue form.
5. Add profile evolution rules:
   1. Signature changes must include fixture/live evidence.
   2. Fallback-only additions must include risk note and follow-up calibration issue.
6. Add observability:
   1. Action-level success/failure counters by profile and symbol source.
   2. Track fallback-hit rate and unresolved symbol rate for drift detection.

## Important changes or additions to public APIs/interfaces/types

1. Add `SymbolHealthStatus` enum in core models: `Healthy`, `Degraded`, `Unresolved`.
2. Extend `SymbolInfo` to include health fields: `HealthStatus`, `HealthReason`, `LastValidatedAt`.
3. Add `SymbolValidationRule` model (range/type/mode constraints) loaded from profile metadata.
4. Add `SymbolValidationResult` model used by runtime execution diagnostics.
5. Add `ISymbolHealthService` contract:
   1. `Evaluate(SymbolInfo symbol, TrainerProfile profile, RuntimeMode mode) -> SymbolValidationResult`.
6. Extend `ISignatureResolver` diagnostics contract to emit confidence/validation metadata in addition to address source.
7. Extend profile metadata contract keys:
   1. `symbolValidationRules` for per-symbol sanity constraints.
   2. `criticalSymbols` for actions requiring strict resolution quality.

## Test cases and scenarios

1. Repository bootstrap validation:
   1. `git push` succeeds on first attempt.
   2. No large mod mirrors tracked in history.
   3. `main` branch protection active after initial push.
2. CI validation:
   1. `ci.yml` passes restore/build/deterministic tests on `windows-latest`.
   2. Live/process-dependent tests are excluded in CI by filter.
   3. Launch-context fixture smoke is executed and passing.
3. GitHub operations validation:
   1. Description, topics, templates, labels, milestones, and project board exist.
   2. Seeded epics and first-sprint issues created and correctly labeled.
   3. PR template appears for new pull requests.
4. Realtime reliability validation:
   1. Signature miss + valid fallback yields `Degraded` health and action gating rules apply.
   2. Failed write readback triggers re-resolve path once before final failure.
   3. Calibration report generation produces attachable artifact with fingerprint + candidates.
   4. UI displays symbol source and health for active profile.
5. Regression safety:
   1. Existing non-live deterministic tests continue passing.
   2. Live tests still skip/return gracefully when no target process exists.
   3. Profile inheritance behavior remains unchanged for shipped profiles.

## Assumptions and defaults

1. This repo will track the trainer/editor codebase only, not full mod asset mirrors.
2. Full local mod trees remain local reference data and are not published to GitHub.
3. First commit goes directly to `main`; branch protection is applied immediately after.
4. CI is authoritative for compile/test in this environment because local `dotnet` is unavailable here.
5. Windows remains the runtime target; WSL/Linux are tooling/diagnostics-only.
6. Existing four profiles remain first-class and custom-mod onboarding is additive.
7. Reliability improvements must stay within the current safe-method architecture and avoid new invasive injection models.
