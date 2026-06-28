# Session 2026-04-08 Ground Truth

**Captured:** 2026-04-08, start of session
**Purpose:** Verified state from disk BEFORE any code changes. Pins down what the last session left behind vs what it claimed.

## Delta: last-session-end claim vs actually on disk right now

| Metric | Last-session claim | Actually on disk right now | Delta |
|---|---|---|---|
| `bridge_test_harness.exe` pass/fail | 230/0 | **230/0** | ✓ |
| Replay tests pass/fail/skip | 69 (68 pass / 1 skip) | (to verify) | pending |
| `dotnet build` warnings on `src/SwfocTrainer.App --warnaserror` | "0" | **0** | ✓ |
| `dotnet build` warnings on FULL solution (minimal verbosity, incremental) | "0" | **0** (cached) | misleading |
| `dotnet build` warnings on FULL solution (normal verbosity, `--no-incremental`) | not tested | **67 warnings** | **67 hidden behind build cache** |
| Ledger warnings | 7 | **7** | ✓ |
| Ledger UNVERIFIED | 2 | **2** (`fact_make_invulnerable_hardpoint_propagation`, `struct_player_playable_flag`) | ✓ |
| SWFOC_* functions registered in lua_bridge.cpp | 28 | **28** | ✓ |
| Editor services with ILuaBridgeExecutor wiring | 17 (new) | **18 services** + `LuaBridgeExecutor.cs` impl (8 new + 10 pre-existing) | ✓ (original count was for new services only) |
| `tools/run_editor_tests_v2.ps1` | "referenced in several places" | **MISSING** — no such file anywhere on disk | **GAP — created this session** |

## Key discrepancies

### 1. The "0 warnings" claim was hiding 67 real warnings

The previous session's agent ran `dotnet build --verbosity minimal` on an incremental build and got "0 Warning(s)". That was correct for an incremental build (nothing to recompile = nothing to warn about) but misleading. A clean rebuild with `--no-incremental` surfaces **67 warnings** across the test project.

**Warning breakdown by code (from `gt_build5.log`):**

| Code | Count | Category | Description |
|---|---|---|---|
| CS8625 | 74* | nullable | Cannot convert null literal to non-nullable reference type |
| CS3001 | 18 | CLS | Argument type is not CLS-compliant |
| CS8778 | 8 | size | Constant value may overflow `nint` at runtime |
| CS1570 | 8 | docs | XML comment has badly formed XML (including **HardpointService.cs and UnitInspectorService.cs** — last session's NEW services) |
| SYSLIB0050 | 6 | obsolete | `FormatterServices` is obsolete |
| CS8602 | 6 | nullable | Dereference of a possibly null reference (including **HardpointService.cs, UnitInspectorService.cs, IBridgeHelperServices.cs** — last session's NEW files) |
| CS3003 | 6 | CLS | Type of method is not CLS-compliant |
| CS1998 | 4 | async | Async method lacks await operators |
| CS0219 | 4 | cleanup | Variable is assigned but never used |

*74 CS8625 "line counts" vs the build's "67 Warning(s)" headline — some warnings are counted per-project they flow through (test project references core project). The unique count is 67.

**Files with warnings traceable to last session's new code:**

- `src/SwfocTrainer.Core/Services/HardpointService.cs`
- `src/SwfocTrainer.Core/Services/UnitInspectorService.cs`
- `src/SwfocTrainer.Core/Contracts/IBridgeHelperServices.cs`

These are explicitly the files the previous agent created. The "0 warnings on `--warnaserror`" claim was based on building a single project (`SwfocTrainer.Core`) incrementally, which does NOT pick up XML doc issues that the test project sees.

### 2. `tools/run_editor_tests_v2.ps1` never existed

The script is referenced by the user's prompt, by prior session notes, and by `.github/workflows/replay-tests.yml`, but no file by that name exists anywhere in the repo. This session creates it.

### 3. `Directory.Build.props` already treats warnings as non-fatal

```xml
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
```

That's why 67 warnings have been accumulating without breaking anything. The agent inherited a codebase where warnings were silently ignored.

## Other items verified true

- Bridge harness: 230 tests passing, 0 failing. ✓
- Ledger state: 303 entries, 290 VERIFIED, 2 LIVE_OBSERVED, 2 UNVERIFIED, 9 DEPRECATED, 0 errors, 7 warnings. ✓
- All 8 new editor service files and 8 new test files exist. ✓
- `IBridgeHelperServices.cs` contract file exists with 8 interfaces. ✓
- Snapshot format v2 write path in `lua_bridge.cpp::Lua_DumpState` uses magic `SWFOCSNAPv2`, format_version=2, and writes `local_slot` field. ✓
- `replay_harness.cpp` reads both v1 and v2 magics with cross-check. ✓
- `make_test_snapshot.py` defaults to v2 with `--v1` flag for legacy. ✓
- `SNAPSHOT_FORMAT.md` documents both versions. ✓
- `blocked_items_2026-04-08.md` exists listing the 7 ledger warnings with unblock recipes. ✓

## Actions this session will take (in order)

1. **Create `tools/run_editor_tests_v2.ps1`** — Start-Process bypass wrapper.
2. **Drive all 67 C# warnings to zero** — categorically, not by suppression except where documented.
3. **Rebuild `swfoc_lua_bridge` with `-Wall -Wextra -Wpedantic`** and fix every warning or document why suppressed.
4. **Close the 7 ledger warnings via IDA Pro MCP** (`decompile`, `xrefs_to`, string evidence) or document as truly-blocked with a regression test.
5. **Resolve the 2 UNVERIFIED entries** via IDA cross-check.
6. **Build `knowledge-base/feature_readiness_matrix_2026-04-08.md`** and drive all non-BLOCKED-LIVE-ONLY rows to READY.
7. **Add the 9 `SWFOC_Replay*` observer helpers** from `replay_stub_gaps.md` plus snapshot v2 section extensions.
8. **Write `tests/SwfocTrainer.Tests/SmokeRun/FullFeatureSmokeTest.cs`** — the single canonical "everything ships green" test.
9. **Add regression guards** for Diplomacy, FactionSwitch, Teleport RVA correction, and every CE-ported feature.
10. **Run the 7-gate verification** and print zero warnings across all three warning surfaces (C#, C++, verifier lint).

Each step has a corresponding task in the task list. Nothing is declared done without disk verification.
