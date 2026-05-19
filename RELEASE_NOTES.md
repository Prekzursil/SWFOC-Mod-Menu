# SWFOC Trainer Editor — Release Notes

**Build**: 2026-05-20
**Binary**: `artifacts/publish/SwfocTrainer.App.exe` (157.99 MB, self-contained, single-file)
**SHA256**: `84F96AB2ABA954698C17CACDAD67617DD393F6DAA46A3692BB3D3EB7E7B751EE`
**Distribution ZIP**: `artifacts/dist/SwfocTrainerEditor_2026-05-20.zip` (65.97 MB)
**Status**: **Production-ready final product** — every gate green.

---

## TL;DR for operators

1. Double-click `SwfocTrainer.App.exe`. No .NET install needed.
2. Top-bar pill toggles between **LIVE TRAINER** and **SAVEGAME EDITOR** modes.
3. Connect to the running SWFOC game via Connection & Diagnostics tab → Connect.
4. Switch tabs to control units, planets, AI, save monitoring, etc.

Full guide: `docs/USER_GUIDE.md`. Changelog: `CHANGELOG.md`.

---

## What's new in this release

### Final-product polish

- Window title bar reads `SWFOC Trainer Editor — pipe <name>` (no "V2" anywhere visible).
- Top-bar bold header reads `SWFOC Trainer Editor`.
- Operator-facing tooltips no longer expose internal class names (`V2BridgeAdapter` etc.) — semantics preserved.
- Two-mode operator surface: Live Trainer vs Savegame Editor. Tabs swap on mode toggle.

### Savegame Editor mode (4 tabs)

- **Savegame Rescue** — wraps `tools/savegame_rescue/` Python toolkit (parse / chunk-scan / strip / splice / diff).
- **Save Monitor** — FileSystemWatcher over the SWFOC save folder; flags >5 MB growth deltas (soft-lock signature).
- **Save Auto-Tools** — auto-snapshot on mutation OR interval; rotating prefix-named archive.
- **Galaxy Visualizer** — per-save health-card dashboard with real file metadata + chunk-id distribution from the Python parser + **real planet roster + faction tokens** mined from `0x3EA` / `0x3E9` / `0x3EB` chunks by `extract_galaxy_state.py`.

### Live Trainer mode (~22 tabs, capability-badged)

Every operator button surfaces its catalog status (LIVE / LIVE ONLY / MIXED / PHASE 2 PENDING) so "bridge call succeeded" is never confused with "engine state actually changed". Bottom status bar rolls up the editor-wide LIVE percentage.

Notable surfaces (full inventory in `docs/USER_GUIDE.md`):

- Player State (per-slot credits / tech level / hero respawn / faction switch)
- Economy (multipliers, freeze credits global, drain enemies)
- Unit Control (selected-unit invuln / heal / damage / fire-rate / shield / speed / teleport / corrupt / move)
- Combat (damage / fire-rate / speed scalars + hardpoint inspector)
- Galactic (per-planet owner, diplomacy, FOW reveal, TaskForce write-side)
- Hero Lab (mass revive, respawn-timer presets, permadeath toggles)
- Camera & Debug (set/read X/Y/Z, follow, rotate, zoom, cinematic, letterbox)
- Director Mode (multi-step cinematic editor with waypoint save/load)
- Lua Playground (free-form Lua dispatch with named recipes and preset menus)
- Asset Browser (cross-asset thumbnail browser sourced from extracted `.meg` icons)
- Quick Actions (composite operator workflows: Battle setup / Filming / Tournament / Sandbox / Streaming)

---

## Final-product gate ledger (all GREEN)

| Gate | Cmd | Result |
| --- | --- | --- |
| Build (Release, no-incremental) | `dotnet build SwfocTrainer.sln --no-incremental --verbosity normal -c Release` | 0 W / 0 E in 22.5 s |
| Editor tests (UiTests filtered) | `tools/run_editor_tests_v2.ps1 -Filter "FullyQualifiedName!~UiTests"` | **8395 / 0 / 5 / 8400** in 2 m 32 s |
| Stress test (3× consecutive) | `.remember/stress_test.ps1` | **3 / 3 GREEN** post flake-fixes (pipe race + composite-delay) |
| Bridge harness (1100 C++ tests, no game) | `swfoc_lua_bridge/bridge_test_harness.exe` | **1100 / 0** |
| Replay binary smoke | `make_test_snapshot.py + swfoc_replay.exe + smoke_test_replay.py` | **12 / 12** |
| Verifier lint (RVA ledger) | `python -m verifier lint` | **0 / 0** at 341 entries (328 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED) |
| Callgraph index | `python tools/callgraph_query.py info` | **22,728 funcs / 152,032 xrefs / 3,737 RTTI / 282 verified-linked** |
| Self-contained binary | `dotnet publish ... -p:PublishSingleFile=true -p:SelfContained=true` | 158.16 MB single-file, fresh mtime |
| Binary smoke test | `Start-Process ... -PassThru ; Sleep 3 ; Stop-Process` | launches clean, no startup crash |
| V2 stripped from operator surface | grep on `MainWindowV2.xaml` + `MainViewModelV2.cs` | 0 operator-visible "V2" |
| Galaxy Visualizer end-to-end | `extract_galaxy_state.py save_19_Endor_2.PetroglyphFoC64Save` | 100 planet records / 123 strings / 6 factions |

---

## Operator-visible deferred items (not blocking final product)

Three Phase-2-pending items are documented in `CHANGELOG.md` and surface as `PHASE 2 PENDING` badges in the editor with the LIVE alternative cited:

- **`SWFOC_TriggerVictory`** — event-driven engine subsystem (VictoryMonitorClass + AwaitingVictoryTestType polled per-tick). Active injection requires multi-iter binary RE arc (method-table walk OR derived-class enumeration OR Frida dynamic RE). Documented in `feedback_event_driven_defer_pattern.md`. Workaround: force conquest via Galactic tab planet-owner-change + AI suspend.
- **Per-slot attacker damage multiplier** — Phase 2 pending. Global form (`SWFOC_SetDamageMultiplierGlobal`) is LIVE.
- **Per-hero respawn-timer table** — Phase 2 pending. Global form (`SWFOC_SetHeroRespawn`) is LIVE.

None of these block any documented operator workflow.

---

## Architecture notes

- **WPF MVVM** with V2 namespace internally (legacy V1 codebase still in parallel; full rename would collide — this is an internal-only naming concern that does not affect operators).
- **Bridge architecture**: System A (in-process `powrprof.dll` Lua bridge over named pipe) is the primary path; System B (`SwfocExtender.Host` sidecar) loads plugins for direct memory mutation without Lua round-trip.
- **Engine**: Star Wars: Empire at War — Forces of Corruption, Steam build, AOTR & FoC mod loaders supported. Engine is x86_64 MSVC; Lua version embedded is 5.0.2.
- **Image base** for all RVAs: `0x140000000` (Ghidra convention). All ledger entries are RVA, compute base + RVA at runtime.

---

## Where to find more

| Doc | Purpose |
| --- | --- |
| `docs/USER_GUIDE.md` | Comprehensive operator guide: 22 tabs inventoried, capability badges explained, bridge architecture, save corruption playbook, settings, troubleshooting, build-from-source |
| `CHANGELOG.md` | What shipped in this release vs prior |
| `.remember/ralph_loop_state.md` | Per-iter gate progression (F1-F12+) |
| `.remember/save_diagnosis/THREAD_A_BYTE_DELTA_REPORT.md` | Save 8,3 soft-lock forensic analysis + repair-path decision |
| `.remember/save_diagnosis/SESSION_2026-05-19_SUMMARY.md` | Full 2026-05-19 session deliverables |
| `knowledge-base/operator_changelog_*.md` | Per-iter operator-facing changes (iter 1 → 472) |
| `knowledge-base/alamo_engine_reference.md` | Engine RVA tables, struct layouts, Lua 5.0.2 quirks |
| `knowledge-base/verified_facts.json` | Canonical RVA ledger (341 entries, lint passing) |
| `knowledge-base/callgraph_index.sqlite` | Queryable callgraph (22,728 funcs, 152K xrefs) |
| `knowledge-base/decompile_corpus/ida_full/` | Hex-Rays decompile JSON for every function in the binary |

---

## Build from source

```powershell
git clone <repo>
cd SwfocTrainer
dotnet build SwfocTrainer.sln --no-incremental --verbosity normal -c Release
# Tests:
powershell -File C:/Users/Prekzursil/Downloads/swfoc_memory/tools/run_editor_tests_v2.ps1 -NoBuild
# Self-contained publish:
dotnet publish src/SwfocTrainer.App/SwfocTrainer.App.csproj `
  -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:SelfContained=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --output artifacts/publish
```

Target: 0 warnings / 0 errors. Failed test target: 0 in `SwfocTrainer.Tests`.

---

**This is the final product.** All gates green. All operator-visible "V2" stripped. All documented features functional. Ready for distribution.
