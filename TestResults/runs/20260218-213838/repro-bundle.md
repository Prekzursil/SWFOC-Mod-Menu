# Repro Bundle Summary

- runId: 20260218-213838
- scope: ROE
- classification: passed
- launch profile: 
- launch reason: python_not_found
- confidence: 0
- launch kind: Unknown
- runtime mode effective: Tactical
- runtime mode reason: tactical_test_passed
- selected host: StarWarsG.exe (pid=32952, role=game_host, score=2223.37)
- backend route: Extender (CAPABILITY_PROBE_PASS)
- capability probe: extender (CAPABILITY_PROBE_PASS, required=)
- overlay: available=False visible=False (CAPABILITY_BACKEND_UNAVAILABLE)

## Process Snapshot

| PID | Name | Role | Score | SteamModIds | Command Line |
|---|---|---|---:|---|---|
| 45908 | swfoc.exe | unknown | 2011.81 | 3447786229,1397421866 | "[REDACTED_GAME_PATH]\swfoc.exe" STEAMMOD=3447786229 STEAMMOD=1397421866 .Replace('|','/') |
| 32952 | StarWarsG.exe | game_host | 2223.37 | 3447786229,1397421866 | StarWarsG.exe STEAMMOD=3447786229 STEAMMOD=1397421866  NOARTPROCESS IGNOREASSERTS.Replace('|','/') |

## Live Tests

| Test | Outcome | Pass/Fail/Skip | TRX | Message |
|---|---|---|---|---|
| LiveTacticalToggleWorkflowTests | Passed | 1/0/0 | TestResults/runs/20260218-213838/20260218-213838-live-tactical.trx |  |
| LiveHeroHelperWorkflowTests | Passed | 1/0/0 | TestResults/runs/20260218-213838/20260218-213838-live-hero-helper.trx |  |
| LiveRoeRuntimeHealthTests | Passed | 1/0/0 | TestResults/runs/20260218-213838/20260218-213838-live-roe-health.trx |  |
| LiveCreditsTests | Passed | 1/0/0 | TestResults/runs/20260218-213838/20260218-213838-live-credits.trx |  |

## Next Action

Attach bundle to issue and continue with fix or closure workflow.
