# Live Validation Runbook (M1 Closure)

Use this runbook to gather real-machine evidence for issues `#34` and `#19`.

## 1. Preconditions

- Launch the target game session first (`swfoc.exe` / `StarWarsG.exe`).
- For AOTR: ensure workshop/mod launch context resolves to `aotr_1397421866_swfoc`.
- For ROE: ensure launch context resolves to `roe_3447786229_swfoc`.
- From repo root, run on Windows PowerShell.

## 2. Run Pack Command

```powershell
pwsh ./tools/run-live-validation.ps1 -Configuration Release -NoBuild
```

Generated artifacts (under `TestResults/`):
- `live-tactical.trx`
- `live-hero-helper.trx`
- `live-roe-health.trx`
- `live-credits.trx`
- `launch-context-fixture.json`
- `live-validation-summary.json`
- `issue-34-evidence-template.md`
- `issue-19-evidence-template.md`

`launch-context-fixture.json` behavior:
- success path: full detector output JSON
- fallback path: status payload (`failed`/`skipped`) with reason code when Python is unavailable in the current shell

## 3. Post Evidence to GitHub Issues

```powershell
gh issue comment 34 --body-file TestResults/issue-34-evidence-template.md
gh issue comment 19 --body-file TestResults/issue-19-evidence-template.md
```

Before posting, replace placeholder fields in each template with real attach/mode/profile details.
Do not close issues from placeholder-only or skip-only runs.

## 4. Closure Criteria

Close issues only when all required evidence is present:
- At least one successful tactical toggle + revert in tactical mode.
- Helper workflow evidence for both AOTR and ROE.
- Launch recommendation reason code + confidence captured.

Then run:

```powershell
gh issue close 34 --comment "Live validation evidence posted; acceptance criteria met."
gh issue close 19 --comment "AOTR/ROE live calibration checklist complete with evidence."
gh issue close 7 --comment "All M1 acceptance criteria completed and evidenced across slices and live validation."
```
