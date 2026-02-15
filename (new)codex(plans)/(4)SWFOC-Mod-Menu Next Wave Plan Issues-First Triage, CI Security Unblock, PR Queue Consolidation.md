# SWFOC-Mod-Menu Next Wave Plan: Issues-First Triage, CI/Security Unblock, PR Queue Consolidation

## Summary
This execution plan starts with issue/project hygiene, then unblocks CI/security, then consolidates the 14 Dependabot PRs, and defers Pages/deployment work for now.

Locked preferences applied:
- Execution order: **CI + Security first**
- PR handling: **Consolidate then prune**
- Code scanning: **Fix runtime/app alerts, triage tests/tools**
- Automation: **Minimal guardrails**
- Pages/deployment: **Skip for this phase**

Current observed baseline:
- Open PRs: **14** (all Dependabot), all failing required `build-test` and all `BEHIND`.
- Open code scanning alerts: **29**
- CI failure root cause: `.github/workflows/ci.yml` uses Python heredoc under `pwsh` (`python - <<'PY'`), which fails in PowerShell.
- Security workflow warnings: `actions/missing-workflow-permissions` in `ci.yml` and `release-portable.yml`.
- Existing workflows: `ci.yml`, `release-portable.yml`, plus dynamic GitHub-managed `CodeQL` and dependency submission.

---

## Phase 0: Start With Issues/Project Board (requested)
1. Update issue statuses before code changes:
- Close completed items with evidence links:
- Candidate closures: `#12` repo hygiene and first push (already done), plus any now-complete runtime hardening issues after quick verification comments.
2. Keep active `Now` lane focused:
- `#13` CI deterministic filtering hardening
- `#14` launch-context parity
- `#15` symbol telemetry
- `#16` re-resolve on failure
- `#17` calibration artifact capture
- `#18` docs guardrails
3. Add a comment on `#13` documenting the exact CI parser failure and intended workflow fix.
4. Ensure project board `Roadmap Lane` remains:
- `Now`: M0 epic + operational blockers
- `Next`: M1 epic + live checklist
- `Later`: M2/M3/M4 epics

Completion gate:
- All `Now` issues reflect current reality with explicit next action and owner/assignee.

---

## Phase 1: Continuous Integration Unblock
Files:
- `.github/workflows/ci.yml`
- `.github/workflows/release-portable.yml`

1. Fix CI launch-context smoke step in `ci.yml`:
- Replace `python - <<'PY'` heredoc with a PowerShell-native JSON assertion block.
- Keep detector invocation as `python tools/detect-launch-context.py ... > launch_context_results.json`.
- Parse and validate JSON using `Get-Content | ConvertFrom-Json` in `pwsh`.
2. Add explicit least-privilege workflow permissions:
- `ci.yml`: `permissions: contents: read`
- `release-portable.yml`: `permissions: contents: read`
3. Keep deterministic test filter as-is unless failing suite indicates regression.
4. Keep artifact uploads, but ensure `launch_context_results.json` is always generated before upload step.

Completion gate:
- Main branch `build-test` check passes end-to-end.
- No workflow-permissions warnings remain for CI/release workflows.

---

## Phase 2: Security Workflow Hardening (CodeQL + dependency guardrail)
Files:
- `.github/workflows/codeql.yml` (new)
- `.github/workflows/dependency-review.yml` (new)

1. Add repo-managed CodeQL workflow:
- Trigger on `push` to `main`, `pull_request` to `main`, and weekly schedule.
- Analyze languages: `csharp`, `python`, `actions`.
- Permissions:
- `security-events: write`
- `actions: read`
- `contents: read`
2. Add dependency review workflow:
- Trigger on `pull_request` to `main`.
- Uses `actions/dependency-review-action`.
- Permissions:
- `contents: read`
- `pull-requests: write` only if posting comments is enabled, otherwise omit.
3. Keep dynamic default CodeQL temporarily until new workflow is green, then disable default setup in repository Code Security settings to avoid duplicate scans.

Completion gate:
- New CodeQL workflow runs from repo YAML (not only dynamic default).
- Dependency review runs on new dependency PRs.

---

## Phase 3: Address 29 Code Scanning Alerts (fix + triage policy)
Target state:
- **No open alerts** in production/runtime/workflow paths.
- Tests/tools alerts either fixed if trivial or dismissed with explicit rationale.

Alert groups observed:
- Workflow permissions warnings: 2 (`.github/workflows/*`)
- C# path-injection in app/runtime/saves/core: 10
- Test path-injection: 6
- Tooling Python path-injection: 11

1. Workflow warnings:
- Resolved by Phase 1 permissions blocks.
2. Runtime/app fixes first:
- `src/SwfocTrainer.Saves/Services/BinarySaveCodec.cs`:
- Add canonicalization and path validation before write/delete operations.
- Reject unsafe path forms and non-save-file extensions where applicable.
- `src/SwfocTrainer.App/ViewModels/MainViewModel.cs`, `src/SwfocTrainer.App/App.xaml.cs`, `src/SwfocTrainer.Core/Logging/FileAuditLogger.cs`:
- Normalize and explicitly constrain app-owned paths to LocalAppData-derived roots.
- Centralize trusted app paths to remove ambiguous dataflow.
3. Test/tools triage:
- For alerts in `tests/...` and `tools/...`:
- Dismiss false positives where user-controlled local file paths are expected behavior.
- Use dismissal reason and standardized comment referencing threat model boundaries.
4. Add triage record:
- `docs/SECURITY_ALERT_TRIAGE.md` with alert IDs, disposition (`fixed` or `dismissed`), and rationale.

Completion gate:
- Code scanning alert dashboard reduced from 29 to 0 open, or all remaining alerts explicitly accepted and documented with rationale.

---

## Phase 4: Handle 14 Open PRs (Consolidate then prune)
Files:
- `.github/dependabot.yml`
- Optional `.github/labeler.yml` + workflow (Phase 5)

1. Stabilize first:
- Do not merge dependency PRs before CI/security fixes are green on `main`.
2. Update Dependabot strategy:
- Group updates by ecosystem to reduce PR flood.
- Keep `open-pull-requests-limit` low (e.g., 3).
- Separate groups:
- `github-actions` grouped
- `nuget` patch/minor grouped
- Optional ignore major updates in this wave.
3. Prune current queue:
- Close all 14 stale/behind Dependabot PRs with a standard superseded comment.
- Allow Dependabot to reopen grouped PRs under new config.
4. Process grouped PRs with deterministic checklist:
- CI green
- dependency review green
- no new high-severity code scanning alert introduced.

Completion gate:
- Open Dependabot PR count reduced from 14 to <=3 grouped PRs.
- No stale behind dependency PR backlog.

---

## Phase 5: Minimal Automation Bundle (selected scope)
Files:
- `.github/workflows/pr-labeler.yml` (new)
- `.github/labeler.yml` (new)

1. Add PR labeling automation:
- Auto-label by path and dependency source:
- `area:ci`, `area:runtime`, `area:profiles`, etc.
- `type:chore` and `dependencies` for Dependabot PRs.
2. Keep merge decisions manual (no auto-merge in this phase).

Completion gate:
- New PRs receive deterministic labels without manual triage overhead.

---

## Phase 6: Deployment and Pages (deferred in this phase)
Decision:
- No Pages deployment now.
- No new deployment workflow now.
- Keep `release-portable.yml` artifact packaging only.

Tracking:
- Add/keep a `Later` issue under M4 for Pages/deployment strategy after CI/security baseline is stable.

---

## Important changes or additions to public APIs/interfaces/types
1. No external/breaking public API changes are planned for runtime features in this phase.
2. Internal safety additions are expected:
- Path validation/canonicalization helper(s) for file operations (app-owned and save write paths).
3. Workflow contract changes:
- Required checks remain `build-test`.
- New repo-managed security workflows (`codeql.yml`, `dependency-review.yml`) become part of operational CI/security surface.

---

## Test cases and scenarios
1. CI regression validation:
- Push to `main` runs `build-test` successfully.
- `Launch-context fixture smoke` step passes under `pwsh` on `windows-latest`.
2. Workflow security validation:
- Code scanning no longer reports `actions/missing-workflow-permissions` for CI/release workflows.
3. Code scanning triage validation:
- Production path alerts are fixed (code change) or explicitly justified and dismissed with documented rationale.
4. Dependabot queue validation:
- Existing 14 PRs are pruned; grouped replacements appear under updated dependabot config.
- Grouped PRs pass required checks and dependency review.
5. Board/issue flow validation:
- `Now/Next/Later` lanes reflect active scope and issue comments include concrete evidence links.

---

## Assumptions and defaults
1. Branch protection on `main` remains with required `build-test`.
2. Admin bypass currently exists; this plan does not change that policy.
3. Repo is public and CodeQL is available via GitHub Code Scanning.
4. Security triage in tests/tools is acceptable when rationale is explicit and documented.
5. Deployment/Pages remain out of scope for this pass and will be revisited under M4.
