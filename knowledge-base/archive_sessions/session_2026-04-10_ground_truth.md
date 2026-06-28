# Session 2026-04-10 — Ground Truth Pass

## Headline

**SmokeCase discrepancy was a false alarm caused by the wrong grep pattern in this session's Part-2 state snapshot.** The previous session's "14 SmokeCases" claim is accurate. The handoff brief was honest. No over-claim to correct.

## SmokeCase audit — FullFeatureSmokeTest.cs

- File: `SWFOC editor/tests/SwfocTrainer.Tests/SmokeRun/FullFeatureSmokeTest.cs`
- Lines: 238
- Test methods: **1** (`ExerciseEveryReadyService_AllReturnNonErrorResponses`, `[SkippableFact]`)
- SmokeCase entries: **14**, structured as a `IReadOnlyList<(string, Func<string>, Func<BridgeAssertionResult, bool>)>` named `SmokeCases` (lines 130–231). Each entry is an anonymous tuple, not a `new SmokeCase(...)` constructor call — which is why an earlier grep for `new SmokeCase|SmokeCase\(` returned only 3 false matches on other tokens.

### The 14 cases

| # | Feature name | Lua command shape | Status group |
|---|---|---|---|
| 1 | BridgeVersion | `SWFOC_GetVersion()` | liveness |
| 2 | LocalPlayer | `SWFOC_GetLocalPlayer()` | liveness |
| 3 | EconomyService.GetCredits | `EconomyService.BuildGetCreditsLuaCommand(-1)` | bridge-helper |
| 4 | EconomyService.GetMaxCredits | `EconomyService.BuildGetMaxCreditsLuaCommand()` | bridge-helper |
| 5 | CrashAnalyzer.CaptureSnapshot | `CrashAnalyzerService.BuildCaptureSnapshotLuaCommand(...)` | bridge-helper (replay short-circuit) |
| 6 | GodMode.Enable | `GodModeService.BuildGodModeLuaCommand(true)` | bridge-helper |
| 7 | GodMode.Disable | `GodModeService.BuildGodModeLuaCommand(false)` | bridge-helper |
| 8 | OneHitKill.Enable | `OneHitKillService.BuildOneHitKillLuaCommand(true)` | bridge-helper |
| 9 | OneHitKill.Disable | `OneHitKillService.BuildOneHitKillLuaCommand(false)` | bridge-helper |
| 10 | HeroRespawn.SetInstant | `HeroRespawnService.BuildSetInstantRespawnLuaCommand(true)` | bridge-helper |
| 11 | HeroRespawn.SetCustom | `HeroRespawnService.BuildSetCustomRespawnLuaCommand(30.0)` | bridge-helper |
| 12 | Diplomacy.Ally | `DiplomacyService.BuildDiplomacyLuaCommand(...)` | v5 stub (NEEDS-REPLAY-HELPER) |
| 13 | FactionSwitch.Rebel | `FactionSwitchService.BuildFactionSwitchLuaCommand("REBEL")` | BLOCKED-MEMORY today; will become READY after Phase 3 wiring |
| 14 | Maphack.Reveal | `MaphackService.BuildRevealAllLuaCommand()` | v5 stub |

### Accept-predicate rigor — honest limits

Most of the 14 accept predicates are **shape checks, not semantic checks**. They verify `!r.PostState.Contains("ERR:")` or `!string.IsNullOrEmpty(r.PostState)`. The predicate does not verify the mutation actually occurred against a real game state — that is exactly why the replay harness's short-circuit responses are acceptable for the cases marked "replay short-circuit" / "v5 stub" above. This is not a lie or an over-claim; the cases at lines 152–156 and 186–191 explicitly document that the replay binary returns canned OK responses and the real write is only exercised by the bridge harness.

**What this means**: SmokeRun green proves "the Lua command shape is well-formed and the bridge accepts it without throwing," not "the mutation lands in memory." The mutation-land proof requires either (a) section 6–10 observer helpers with a matching C# fixture builder, or (b) a live game.

### TODO block at lines 208–230

The file already documents the 4 reverted observer cases (PlayerCredits, PlayerTechLevel, DiplomaticState, PlanetCorruption) and the unblock recipe: extend `ReplaySnapshotBuilder.cs` with `WithPlanets`, `WithDiplomacy`, `WithCooldowns`, `WithTaskForces`, `WithObjectOwners` methods that write sections 6–10 in the same byte layout as `make_test_snapshot.py`. This is Phase 2 of the 2026-04-10 prompt. The TODO is honest and specific.

## Correction to this session's state snapshot

The Part-2 entry "grep -c 'new SmokeCase|SmokeCase(' → 3" in the mid-session state snapshot was **wrong** — it misled the user into believing there was an over-claim. The pattern should have been `grep -cE '^        \\("'` or a count of the tuple entries in the `SmokeCases` list body. Amending: the correct count is **14**. No handoff brief correction is needed; only this session's snapshot was mistaken.

## Other ground-truth checks (not regressed)

| Check | Result |
|---|---|
| `python -m verifier lint` (from `tools/`) | 308 entries, 296 VERIFIED, 2 LIVE_OBSERVED, 10 DEPRECATED, 0 errors, 0 warnings |
| `swfoc_lua_bridge/bridge_test_harness.exe` | 295 passed, 0 failed |
| Deployed `powrprof.dll` | SHA `fc2c104b…`, 286,720 bytes, mtime 2026-04-06 20:17 (STALE — 3 days) |
| Source `powrprof.dll` | SHA `e46b8091…`, 301,056 bytes, mtime 2026-04-09 06:01 |
| `SWFOC_*` helpers in `lua_bridge.cpp` | 27 |
| `SWFOC_Replay*` helpers in `replay_harness.cpp` | 19 distinct |
| `docs/USER_WORKFLOW.md` | exists, 332 lines |
| `knowledge-base/handoff_2026-04-09.md` | exists, 11 sections filled |
| `knowledge-base/feature_readiness_matrix_2026-04-09.md` or `_04-10` | **does not exist** — latest is `_2026-04-08.md`, 2 days stale |
| Editor build / test suite / warning audit | **not run this turn** (context budget) |

## Verdict

Phase 0 is clean. The previous session's deliverables survive disk audit. The real story is still the stale deployment and the missing live-game validation — everything else is offline infrastructure that is working as intended.
