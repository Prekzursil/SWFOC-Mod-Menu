# Reviewer Automation

This repository uses REST-only reviewer automation to avoid brittle GraphQL paths and to handle single-maintainer scenarios gracefully.

## Components

- Roster contract: `config/reviewer-roster.json`
- Assignment script: `tools/request-pr-reviewers.ps1`
- Workflow: `.github/workflows/reviewer-automation.yml`

## Roster Contract

`config/reviewer-roster.json` keys:

- `version` (int)
- `users` (array of GitHub logins)
- `teams` (array of GitHub team slugs)
- `fallbackLabel` (string)
- `fallbackCommentEnabled` (bool)

Example:

```json
{
  "version": 1,
  "users": ["Prekzursil"],
  "teams": [],
  "fallbackLabel": "needs-reviewer",
  "fallbackCommentEnabled": true
}
```

## Runtime Behavior

For each PR event (`opened`, `reopened`, `ready_for_review`, `synchronize`):

1. Read PR author.
2. Filter configured users/teams to exclude author.
3. Request reviewers via REST (`POST /pulls/{number}/requested_reviewers`).
4. If no eligible reviewer exists (or request fails), apply soft fallback:
   - ensure fallback label exists (create if needed)
   - apply fallback label to PR
   - post one marker-based explanatory comment (no duplicates)

This fallback does **not** fail the workflow by itself.

## Local Manual Run

```powershell
pwsh ./tools/request-pr-reviewers.ps1 \
  -RepoOwner Prekzursil \
  -RepoName SWFOC-Mod-Menu \
  -PullNumber 46 \
  -Token <github-token> \
  -RosterPath config/reviewer-roster.json
```

Dry-run mode:

```powershell
pwsh ./tools/request-pr-reviewers.ps1 \
  -RepoOwner Prekzursil \
  -RepoName SWFOC-Mod-Menu \
  -PullNumber 46 \
  -Token <github-token> \
  -RosterPath config/reviewer-roster.json \
  -DryRun
```

## Notes

- `GH_TOKEN` is provided automatically in CI (`github.token`).
- For manual local runs, pass `-Token <github-token>` or set `GH_TOKEN`.
- This automation does not alter branch protection rules.
- If only the PR author is available in the roster, fallback labeling/commenting is expected behavior.
