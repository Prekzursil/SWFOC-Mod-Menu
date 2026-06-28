# Session 2026-04-09 Ground Truth

**Captured:** 2026-04-09, start of session
**Purpose:** Real disk state before any new work. Cross-checked every claim
from the previous session's handoff (`.remember/now.md` + `session_2026-04-08_ground_truth.md`).

## Delta table — previous-session claim vs actual disk state

| Metric | Claimed at end of 2026-04-08 | Actual on disk 2026-04-09 | Match? |
|---|---|---|---|
| `bridge_test_harness.exe` pass/fail | 230/0 (and later 295/0 after mid-session) | **295/0** | ✓ |
| Bridge clean rebuild warnings | 0 | **0** | ✓ |
| Replay smoke test | 6/6 | **12/12** (B1 landed — new observer helpers verified) | ✓ (exceeded) |
| v1 back-compat read | verified | **verified** (1140 bytes, reader loads OK) | ✓ |
| v2 current size | 1144 bytes (5 sections) | **2388 bytes** (extended with sections 6-10) | ✓ (extended) |
| `--v2-early` flag | "added per B1 prompt" | **NOT IMPLEMENTED** — silently falls through to v2 | ✗ |
| `dotnet build` incremental warnings | 0 | **0** | ✓ |
| `dotnet build --no-incremental --verbosity normal` | 0 | **0** | ✓ |
| Ledger entries total | 304 | **304** | ✓ |
| Ledger VERIFIED | 292 | **292** | ✓ |
| Ledger LIVE_OBSERVED | 2 | **2** | ✓ |
| Ledger UNVERIFIED | 0 | **0** | ✓ |
| Ledger DEPRECATED | 10 | **10** | ✓ |
| Ledger errors/warnings | 0/0 | **0/0** | ✓ |
| Replay-tagged editor tests | 69 (68 pass / 1 skip) | **69 (68/1)** | ✓ |
| Regression-tagged tests | 17/0 | **17/0** | ✓ |
| `FullFeatureSmokeTest` | never ran | **1 method, 14 SmokeCases, 14/14 passed in 27ms** | ✓ (newly verified) |
| Full editor test suite | not measured | **6613 total, 4 apparent failures (false alarm)** | ⚠ see below |
| CLAUDE.md line count | 165 | **165** | ✓ |
| `.claude/ida-pro-mcp.local.md` | 2741 bytes, created | **exists, expected state intact** | ✓ |
| SWFOC_* bridge helper count | 28 | **28** | ✓ |
| SWFOC_Replay* observer helpers | 9 observer + 7 mutation | **19 total `SWFOC_Replay*` names found** (including baseline count/metadata + new 9 observers + 7 mutations) | ✓ |

## The 4 "apparent failures" — diagnosed

| Test | Root cause | Resolution |
|---|---|---|
| `LiveCreditsTests.Credits_LiveDiagnostic_Should_Identify_Working_Strategy` | `ProcessLocator.FindBestMatchAsync(ExeTarget.Swfoc)` matches `Swfoc*`-prefixed processes, not just `StarWarsG.exe`. A stale `SwfocExtender.Host.exe` from a previous session was being attached, then the test timed out (~34s) trying to scan its memory. | Kill the stale extender -> tests properly `LiveSkip` when no real game is up. Killing is a workaround; the real fix is to make `ProcessLocator` match on exact exe name. Out of scope this session. |
| `LivePromotedActionMatrixTests.Promoted_Actions_Should_Route_Via_Extender_Without_Hybrid_Fallback` | Same root cause. | Same workaround. |
| `LiveTacticalToggleWorkflowTests.Tactical_Toggles_Should_Execute_And_Revert_When_Tactical_Mode` | Same root cause. | Same workaround. |
| `LiveActionSmokeTests.LiveSmoke_Attach_Read_And_OptionalToggleRevert_Should_Succeed` | Same root cause. | Same workaround. |
| `RuntimeAttachSmokeTests.RuntimeAdapter_Should_Attach_And_Detach_When_Swfoc_Process_Is_Running` | Same root cause -- test does `return;` if locator returns null but the false match returns a non-null PID so it proceeds past the null check into the attach code. | Same workaround. |

**Verification after killing `SwfocExtender.Host.exe`:** 3 Live tests skip cleanly, 1 passes, 0 fail (4/4 total). Problem is 100% diagnosed and not a regression from this session's work.

## Filtered full suite (excluding live-process tests)

| Filter | Total | Passed | Failed | Skipped | Duration | Per-test avg |
|---|---|---|---|---|---|---|
| `FullyQualifiedName!~Profiles` | 6270 | 6269 | 0 | 1 | 2m 49s | 27 ms |
| `FullyQualifiedName!~Live & FullyQualifiedName!~RuntimeAttach` | 6542 | 6541 | 0 | 1 | 4m 7s | 38 ms |
| `Category=Replay` | 69 | 68 | 0 | 1 | 111 ms | 1.6 ms |
| `FullyQualifiedName~Regression` | 17 | 17 | 0 | 0 | 22 ms | 1.3 ms |
| `FullyQualifiedName~FullFeatureSmokeTest` | 1 (14 SmokeCases) | 1 | 0 | 0 | 27 ms | 27 ms for 14 bridge round-trips |

The full suite is slow primarily due to test count (6000+), not individual slowness. Targeted filters are the fast iteration path.

## New findings from Phase A

1. **Phase B1 is DONE** — the previous session's parallel agent landed 19 `SWFOC_Replay*` names (9 observers + 7 mutations + 3 baseline) into `replay_harness.cpp`, extended `make_test_snapshot.py` to populate sections 6-10, added smoke test cases to `smoke_test_replay.py`. Verified by running the 12/12 smoke test against the rebuilt binary.

2. **`--v2-early` flag does NOT exist** — the flag is silently ignored and falls through to v2. The previous session's agent claimed it was added but the code check only recognizes `--v1`. This is a real gap that needs fixing in B1 cleanup.

3. **Phase B2 `FullFeatureSmokeTest` is substantially done** — 214 lines, 14 SmokeCases, compiles, runs, passes (14/14 in 27ms against the live replay binary via `ReplayHarnessCollection`). The remaining B2 work is to:
   - Grow the SmokeCases to match the matrix's full READY count (currently only 1 row is marked READY, which is a matrix-ordering issue, not a smoke test issue)
   - Upgrade the predicates to use the new observer helpers for side-effect verification (currently most predicates just check `!ERR:`)

4. **IDA Pro MCP is NOT connected this session** — system-reminder at session start lists all `ida-pro-mcp` tools as "no longer available." Phase B4 (SWFOC_SetHumanPlayer investigation) will need to proceed via other means: CE trainer source grep, existing decompile evidence in knowledge-base, or mark as `BLOCKED-NEEDS-LIVE-INVESTIGATION`.

5. **Claims match disk on everything substantive.** Only two gaps: the missing `--v2-early` flag (small) and the `FullFeatureSmokeTest` needing to grow its SmokeCases (medium).

## Starting state -> target state

- Start: 295 bridge tests, 12/12 smoke, 14 SmokeCases, 0 ledger warnings, 1 READY in matrix, 0 UI buttons wired, 1 BLOCKED-MEMORY feature.
- Target: same counts plus `--v2-early` flag, SmokeCases matching matrix READY count, 8 more services wired to UI (NEEDS-UI -> READY), FactionSwitch either unblocked or explicitly `BLOCKED-NEEDS-LIVE-INVESTIGATION`.
