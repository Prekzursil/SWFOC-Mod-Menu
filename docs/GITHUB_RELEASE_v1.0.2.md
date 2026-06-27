# v1.0.2 — Operator-Trust Polish + v1.1.0 Foundations (2026-05-20)

Cumulative release combining the 4-item v1.0.2 hotfix surface polish with the v1.1.0 reusable-control foundations that the upcoming UI refactor will sit on top of.

This release is the **first concrete execution of the comprehensive improvement plan** (`docs/IMPROVEMENT_PLAN_2026-05-20.md`) that the 4-agent brainstorm synthesised.

---

## v1.0.2 hotfix items (Part 1 CRITICAL/HIGH + Part 2 quick wins)

### I1 — Bridge banner string fix

**Before**: `SWFOC Lua Bridge v1.5-dev+a (2026-04-11, 37 live helpers, snapshot v2)` — the "37" was stale and contradicted operators reading the catalog (174 LIVE entries).

**After**: `SWFOC Lua Bridge v1.0.2 (2026-05-20, 174 live helpers / 18 P2-pending, snapshot v2)` — matches the catalog and the autonomous test report.

### I2 — Operator-tooltip cleanup

Stripped 7 operator-visible tooltips of internal jargon:

- `ICorruptionService → set ...` → `Set ...` (×2)
- `IDiplomacyService → set ...` → `Set ...`
- `IStoryEventService → call ...` → `Call ...`
- `ICrashAnalyzerService → write ...` → `Write ...`
- `Engine pointer to the unit object. Walks Components array via lua_bridge.cpp:2228.` → `Engine pointer to the unit object. Walks the unit's Components array.`
- Removed the operator-visible `(typo: Reponse)` annotation on the SFX VO toggle label.
- Renamed `Iter 100-300 LIVE wires (+2 honest-defer notes)` GroupBox header to `LIVE wire examples (300+)`.

### I3 — Window title mode binding

The window title bar now includes the active mode:

- LIVE Trainer mode: `SWFOC Trainer Editor — LIVE TRAINER — pipe swfoc_bridge`
- Savegame mode: `SWFOC Trainer Editor — SAVEGAME EDITOR — pipe swfoc_bridge`

Eliminates the "where am I again?" head-scratch when the top-bar pill is scrolled out of view.

### I4 — FreezeAI catalog rationale rewritten

The PHASE 2 PENDING entry for `SWFOC_FreezeAI` now leads with the LIVE alternative:

```
USE LIVE ALTERNATIVE: SWFOC_SuspendAiLua (LIVE) suspends per-unit AI via the
engine's Suspend_AI Lua API — that's the recommended path for freezing AI
behavior. This entry remains Phase 2 because the global AI scheduler is
event-driven ...
```

Operator-trust pattern: surface what the operator CAN do now before explaining what they can't.

---

## v1.1.0 foundations (Part 1 CROSS-CUTTING + Part 3 reusable controls)

### I5 — `SwfocTrainer.Core.Validation.ObjAddrParser`

Hoisted the obj_addr parser from `UnitControlTabViewModel.cs:1364` into a shared static class in `Core.Validation`. Pure-function `(bool Success, long Addr, string Error) TryParse(string?)` plus an `out`-param convenience overload. CLS-compliant (`long`, not `ulong`) per Core conventions. **8 new unit tests** pin canonical hex / decimal / 0x-prefix / whitespace / overflow behaviors.

`UnitControlTabViewModel.TryParseObjAddr` now delegates to the shared parser — the VM keeps the `Append` logging side-effect but the parsing is shared. Same approach will be applied to Inspector, Combat, Speed, Hero Lab, Hardpoint, Cross-Faction in v1.1.0 final.

### I6 — `SwfocTrainer.App.V2.Controls.SlotPickerControl`

WPF UserControl that replaces the typed-integer "Slot: -1" TextBox pattern across 8+ V2 tabs. DependencyProperties:

- `ItemsSource` — collection of `PlayerSlotEntry` items
- `SelectedSlot` — two-way bound to host VM
- `RefreshCommand` — optional ↻ button that re-reads slot→faction labels

Refresh button auto-hides when no command is provided. The display label format ("Slot 6 — UNDERWORLD") mirrors the canonical Player State pattern.

### I7 — `BadgeToIsEnabledConverter`

WPF `IValueConverter` that maps `CapabilityAwareAction.Badge` to `Boolean`. Buttons with `PHASE 2 PENDING` or `Phase 2 hook pending` text are disabled; everything else (LIVE / LIVE ONLY / MIXED) stays enabled. Registered as `{StaticResource BadgeToIsEnabled}` in `MainWindowV2.xaml`. **7 new unit tests** pin case-insensitive matching, non-string fallback, and ConvertBack rejection.

---

## v1.2.0 foundation (Part 4 test plan)

### I8 — `bridge/full_editor_test_matrix.ps1`

Test harness skeleton:

- **Phase 0** (preflight) — game-running check, bridge build-info check, stale-DLL guard, pipe baseline capture
- **Phase 1** (baseline snapshot) — all 8 player slots, all 4 multipliers, freeze state, current mod, game mode — written to `baseline_$timestamp.json` for revert + post-test diff
- **JSONL logger** — `Write-Verdict` function emits one structured row per probe with `ts / tab / feature / capability_status / mode_expected / mode_observed / verdict / evidence / diagnostic / duration_ms / bridge_pipe_stats_received_delta`
- **Markdown summary** auto-generated at end with pass-rate calculation + verdict breakdown
- **Meta-tab probes** (7) wired through the new logger
- **Phases 2-5** scaffolded with `TODO` markers — implementation continues via incremental commits

Exit codes: 0 = ≥95% PASS on LIVE rows; 1 = below threshold OR any FAIL; 2 = preflight refused (game not running, stale DLL).

---

## Verification

```
Build (Debug, --no-incremental)            0 warnings / 0 errors
Build (Release, self-contained publish)    0 warnings / 0 errors
Unit tests (SwfocTrainer.Tests)            8424 / 0 failed / 5 skipped / 8429 total
                                           (+29 vs v1.0.1 base, all from new ObjAddrParser
                                            + BadgeToIsEnabledConverter coverage)
Bridge harness (1100 C++ smoke)            1100 / 0 (unchanged)
Replay binary smoke                        12 / 12 (unchanged)
Verifier RVA ledger lint                   0 / 0 @ 341 entries (unchanged)
Semgrep p/csharp on changed files          0 findings
```

---

## How to upgrade

### From v1.0.1

The editor binary (`SwfocTrainer.App.exe`) is updated — replace it.

The bridge DLL (`powrprof.dll`) is updated with the new banner string but is **functionally identical**. You can keep the v1.0.1 bridge if you don't want to redeploy.

### Fresh install

1. Download `SwfocTrainerEditor_v1.0.2.zip`
2. Verify SHA256 against `SHA256SUMS.txt`
3. Copy `powrprof.dll` alongside `StarWarsG.exe`
4. Run `SwfocTrainer.App.exe`

---

## Roadmap

This release ships v1.0.2 + the v1.1.0 foundations (controls + validation). The v1.1.0 release itself (which will route every typed-integer slot TextBox through `SlotPickerControl`, every `obj_addr` field through `ObjAddrParser`, and bind every PHASE 2 button's `IsEnabled` to the new converter) is the next concrete step.

Full 5-release roadmap in `docs/IMPROVEMENT_PLAN_2026-05-20.md`.

---

## SHA256 checksums

```
A4FFD978018228BF4B8E1CD577D9D1E60F96150C5F2AE0F443F8A0F0E50EA66C  SwfocTrainer.App.exe
[bridge hash: see SHA256SUMS.txt bundled in the zip]
```
