# M1 Consolidation Wave: Fixes + Next Tasks (Decision-Complete)

## Summary
This wave will finalize and ship the currently uncommitted M1 work in a single PR, apply the selected fixes, and set up clean handoff to post-merge live validation plus the next long program (M2 Save Lab).

Locked decisions applied:
- PR strategy: one consolidated M1 PR.
- Warning strategy: zero warnings for changed surface (not repo-wide), with targeted XML docs only.
- SDK warning fix: switch WPF app project SDK to remove `NETSDK1137`.
- Live test behavior: convert “pass-on-return” to explicit skipped tests.
- Live evidence timing: after feature PR merge.
- Next long task after M1: M2 Save Lab.

Current grounded baseline:
- `dotnet` is now available in this shell (`8.0.100`), matching `global.json`.
- Local restore/build pass.
- Deterministic suite passes.
- Full test suite passes currently, but live tests use early return instead of explicit skip.
- Open PR count is `0`; open code-scanning alerts are `0`.
- M1 slice issues `#30 #31 #33 #35 #36` are still open and need evidence-backed closure.

## Implementation Plan

## Phase 1 — Scope Lock + Branching
1. Create branch `feature/m1-consolidation-live-ops` from `main`.
2. Keep plan-file migration as currently represented:
   - keep deleted root plan files (`PLAN.md` variants).
   - keep versioned archive under `(new)codex(plans)/`.
3. Include existing M1 code/docs/test/preset changes in this branch; do not split into multiple feature branches.

## Phase 2 — Fix Batch A (Warnings + SDK)
1. Remove `NETSDK1137` warning:
   - Update `src/SwfocTrainer.App/SwfocTrainer.App.csproj` SDK from `Microsoft.NET.Sdk.WindowsDesktop` to `Microsoft.NET.Sdk`.
   - Keep WPF targeting settings explicit and unchanged in behavior (`UseWPF`, Windows target framework behavior).
2. Apply targeted XML docs only on new/changed public APIs in this wave:
   - `src/SwfocTrainer.Core/Contracts/IActionReliabilityService.cs`
   - `src/SwfocTrainer.Core/Contracts/ISelectedUnitTransactionService.cs`
   - `src/SwfocTrainer.Core/Contracts/ISpawnPresetService.cs`
   - `src/SwfocTrainer.Core/Models/LiveOpsModels.cs`
   - `src/SwfocTrainer.App/Models/ActionReliabilityViewItem.cs`
   - `src/SwfocTrainer.App/Models/SelectedUnitTransactionViewItem.cs`
   - `src/SwfocTrainer.App/Models/SpawnPresetViewItem.cs`
3. Warning gate policy for this PR:
   - no `NETSDK1137`.
   - no net-new warnings introduced by newly added files.
   - legacy warnings in untouched surfaces are tracked separately (do not expand scope to repo-wide warning elimination).

## Phase 3 — Fix Batch B (Explicit Live Skip Semantics)
1. Introduce test helper for explicit skip:
   - add `tests/SwfocTrainer.Tests/Common/LiveSkip.cs` with a single helper that logs context and throws `Xunit.Sdk.SkipException`.
2. Replace early-return skip paths with explicit skip in live tests:
   - `tests/SwfocTrainer.Tests/Profiles/LiveCreditsTests.cs`
   - `tests/SwfocTrainer.Tests/Profiles/LiveRoeRuntimeHealthTests.cs`
   - `tests/SwfocTrainer.Tests/Profiles/LiveTacticalToggleWorkflowTests.cs`
   - `tests/SwfocTrainer.Tests/Profiles/LiveHeroHelperWorkflowTests.cs`
3. Keep skip reason strings explicit and operationally useful (process missing, mode mismatch, helper unavailable, profile mismatch).

## Phase 4 — Verification Gate (Local + CI Parity)
1. Run local validation commands:
   - `dotnet restore SwfocTrainer.sln`
   - `dotnet build SwfocTrainer.sln -c Release --no-restore`
   - `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"`
   - `dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj -c Release --no-build`
   - `python tools/detect-launch-context.py --from-process-json tools/fixtures/launch_context_cases.json --profile-root profiles/default`
2. Validate full-test output shows explicit `Skipped` entries for unmet live prerequisites (instead of silent pass-by-return).
3. Confirm build output has no `NETSDK1137`.

## Phase 5 — Consolidated PR + Issue Closure
1. Open one PR with title:
   - `M1 consolidation: Live Ops tab, transaction lab, spawn presets, reliability gates`
2. PR body must include:
   - `Closes #30`
   - `Closes #31`
   - `Closes #33`
   - `Closes #35`
   - `Closes #36`
   - `Refs #7`
3. Update `TODO.md` evidence lines to current command results and counts.
4. Add evidence comments to closed issues with:
   - deterministic test command/output summary
   - full test command/output summary
   - launch-context fixture parity result
   - file references for implemented slices
5. Keep `#34` and `#19` open (live evidence still pending by decision).

## Phase 6 — Post-Merge Live Validation (M1 Completion)
1. Execute live checklist on Windows machine for AOTR and ROE.
2. Record standardized evidence in `#34` and `#19`:
   - date/time
   - profile id
   - launch recommendation `reasonCode`
   - runtime mode at attach
   - tactical toggle workflow result
   - helper workflow result
   - any degraded/unavailable actions with reason codes
3. Close `#34` and `#19` when evidence is complete.
4. Close epic `#7` when all acceptance criteria are evidenced.

## Phase 7 — Next Long Task Kickoff (M2 Save Lab)
1. Open M2 sub-issues under epic `#8`:
   - Patch-pack schema and versioning contract.
   - Export/import pipeline with deterministic compatibility checks.
   - Rollback-safe apply path and validation guardrails.
2. Move board focus from M1 closure items to M2 `Now` lane after `#34/#19` are complete.

## Important Changes or Additions to Public APIs / Interfaces / Types
1. No new functional runtime public API changes are required in this wave.
2. Public API documentation additions are required on newly introduced M1 interfaces/models for changed-surface warning hygiene.
3. Test-only surface addition:
   - new live-test skip helper in `tests/SwfocTrainer.Tests/Common` (non-production API).
4. Project build contract update:
   - WPF app SDK declaration change in `src/SwfocTrainer.App/SwfocTrainer.App.csproj` to remove `NETSDK1137` while preserving WPF behavior.

## Test Cases and Scenarios
1. Build warning scenario:
   - Build succeeds with no `NETSDK1137`.
2. Deterministic regression scenario:
   - deterministic filtered suite passes.
3. Live gating semantics scenario:
   - when prerequisites are absent, live tests are reported as `Skipped` with explicit reasons.
4. Launch-context parity scenario:
   - fixture smoke recommendations and reason codes match expected matrix.
5. Issue closure scenario:
   - `#30/#31/#33/#35/#36` auto-close via merged PR.
6. Post-merge validation scenario:
   - `#34/#19` close only after real-machine evidence artifacts are posted.

## Assumptions and Defaults
1. Branch protection remains PR-required on `main`.
2. Single consolidated M1 PR is acceptable for review size and merge flow.
3. Warning cleanup scope is limited to changed surfaces in this wave; repo-wide warning eradication is deferred.
4. Live evidence is intentionally deferred until after merge and captured in issue comments.
5. Safe-method architecture boundary remains unchanged (no new invasive injection model).
6. Root plan docs remain archived under `(new)codex(plans)/`; deleted root `PLAN*` files stay deleted.
