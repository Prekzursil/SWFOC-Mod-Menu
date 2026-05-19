# SWFOC Trainer Editor — Operator User Guide

**Final version.** This guide replaces every prior "v2 / v3 / draft" guide. If you read
something here that contradicts an older doc, this doc wins.

---

## 1. What this tool is

The **SWFOC Trainer Editor** is a Windows desktop app for Star Wars: Empire at War —
Forces of Corruption (Steam, 64-bit). It runs in two modes:

1. **Live Trainer** — attach to a running `StarWarsG.exe` via the `powrprof.dll` Lua
   bridge and modify the world while the game runs (set credits, unlock units, freeze
   the AI, etc.).
2. **Savegame Editor** — open a `.PetroglyphFoC64Save` file, inspect its chunk tree,
   monitor live save folders for corruption signals, and run the rescue toolkit.

The top bar has a mode toggle (`LIVE TRAINER` / `SAVEGAME EDITOR`). Switching mode
flips which tabs are visible and tints the title bar so you always know which mode
you are in.

---

## 2. First-time setup

### Required

- Windows 10/11 64-bit
- Star Wars: Empire at War — Forces of Corruption (Steam install)
- `StarWarsG.exe` reachable from the launcher (mod-loader or vanilla, both work)

### Optional but recommended

- **Python 3.11+** on PATH (for the Savegame Editor's chunk inspection + galaxy
  state extraction tools)
- A copy of this repo's `tools/savegame_rescue/` directory (the Savegame Editor
  shells out to it; set `SWFOC_RESCUE_TOOLS` to the parent dir, or edit Settings →
  Tools root)

### Run

1. Double-click `artifacts/publish/SwfocTrainer.App.exe` (self-contained — no .NET
   install required; ~158 MB single-file).
2. The first launch creates `%LOCALAPPDATA%\SwfocTrainer\v2_settings.json` with
   defaults.
3. Open **Connection & Diagnostics** tab → click **Connect** → status line should
   read "Connected to pipe `\\.\pipe\swfoc_bridge`" once SWFOC is running with the
   bridge DLL deployed (see `bridge/README.md` for DLL deployment).

---

## 3. Tab inventory

### Live Trainer mode

| Tab | What it does |
|---|---|
| **Connection & Diagnostics** | Attach/detach, probe bridge version, list registered SWFOC_* helpers, save activity log to file, generate capability surface reports. |
| **Player State** | Per-slot credits / tech level / hero respawn / faction switch. The Player slot dropdown shows live faction labels (`Slot 6 — UNDERWORLD`). |
| **Economy** | Credit multipliers, freeze credits globally, drain enemy treasuries. |
| **Unit Control** | Selected-unit actions: heal/damage, invuln, hide, retreat, despawn, teleport, change owner, set fire-rate / shield / speed. |
| **Combat** | Per-faction or global damage / fire-rate / speed scalars with Easy/Normal/Hard/Hardcore presets, area damage / OHK toggles, hardpoint inspector. |
| **Speed** | Per-faction movement-speed multipliers with presets (vanilla / fast / blitz). |
| **Hero Lab** | Per-hero revive / respawn-timer / permadeath toggles, mass revive, respawn-time presets (Quick/Normal/Slow/Glacial). |
| **Galactic** | Per-planet owner change, diplomacy (lock tech / make ally / make enemy), FOW reveal (per-planet or galaxy-wide), TaskForce write-side (move/reinforce/launch/land). |
| **Spawning** | Spawn unit by type/owner/location, story-arrival spawn, reinforce / generic-object create. Faction filter + grouping. |
| **Camera & Debug** | Set/read camera position (X/Y/Z), camera-follow target, rotate-to / zoom, cinematic mode toggle, letterbox on/off, free-cam toggle. |
| **Director Mode** | Multi-step cinematic flow editor with save/load waypoint paths. |
| **Story Events** | Story-flag toggle, story-event trigger by name with autocomplete, audio (PlayMusic, PlaySfxEvent, VO toggle). |
| **World State** | Lock/unlock controls, story-event trigger by name, disable orbital bombardment, SFX VO toggle. |
| **Inspector** | Address inspector with auto-refresh, read hull / shield / health / position / garrison / behavior id / ability state / has-property / distance for the selected unit. |
| **Lua Playground** | Free-form Lua dispatch with named recipes, paste/run history, 10+ preset menus (iter 100-113 LIVE wires, camera arc, etc.). |
| **Event Stream** | Live event tail from the bridge with substring filter and auto-drain. |
| **Tactical Units** | Walks current tactical battle's unit roster with filters, CSV export, context menu. |
| **Probes** | Free-form parameterized probes (faction roster, type-manager iteration, mod-CRC32, etc.). |
| **CrossFaction Recruitment** | Cross-faction unit recruitment with per-faction rule preview. |
| **UnitStatEditor** | Stage per-unit field overrides (max_hull / max_shield / max_speed / attack_power / etc.) and apply them in a batch. |
| **Quick Actions** | Operator workflow composites: Battle setup, Filming setup, Tournament / Sandbox / Streaming presets. Each composite fires 2-7 primitive bridge calls in sequence; tab amber-banner flags Mixed (LIVE + Phase 2 pending) composites. |
| **Asset Browser** | Cross-asset thumbnail browser (unit / hero / faction / ability / weapon / planet icons) sourced from extracted `.meg` assets. |
| **Settings** | Pipe name, Tools root, Icons root with browse buttons, mod picker, reset to defaults, open settings file, open log file. |

### Savegame Editor mode

| Tab | What it does |
|---|---|
| **Savegame Rescue** | Wraps `tools/savegame_rescue/` Python toolkit: parse a save, scan for runaway chunks, strip by chunk_id or path, splice subtree, identity diff against a known-good save, integrity hash. |
| **Save Monitor** | FileSystemWatcher over the SWFOC save folder. Logs every save with file-size delta vs. prior; flags `> 5 MB` jumps as anomalies (canonical soft-lock signature). DataGrid row coloured red on warning rows. |
| **Save Auto-Tools** | Two automation modes: (a) auto-copy every save mutation to a rotating snapshot archive with a custom prefix; (b) interval-trigger snapshots every N minutes regardless of game saves. Keeps last K rotated copies (configurable). |
| **Galaxy Visualizer** | Per-save dashboard with health cards (size delta + growth rate scored into a 0-1 health metric, anomalous badges), file-size growth curve (real chronological metadata), chunk-ID distribution from the Python parser, and **real extracted galactic state** (planet roster + co-located faction tokens from `extract_galaxy_state.py` string-mining of 0x3EA / 0x3E9 / 0x3EB chunks). |

---

## 4. Capability badges

Every operator button surfaces its capability status so "bridge call succeeded" is
never confused with "engine state actually changed":

| Badge | Meaning |
|---|---|
| **LIVE** | Wire is fully implemented and verified end-to-end. The bridge call modifies engine state and the change is observable. |
| **LIVE ONLY** | Bridge wire works only when SWFOC is attached and in the right scene mode. Stays badged so operators know it won't replay. |
| **MIXED (N/M LIVE)** | Composite action where N of M primitives are LIVE. Tooltip lists which sub-primitives are still Phase-2-pending. |
| **PHASE 2 PENDING** | The wire's RVA / RE is incomplete. The button is intentionally surfaced (so the catalog stays honest) but does not actually mutate engine state. Tooltip explains what's blocking and what the LIVE alternative is (if any). |

The bottom status bar shows the editor-wide rollup ("87% engine-effective; 148 LIVE,
29 PHASE 2 PENDING, 3 LIVE ONLY"). Hover over it for the top-3 PHASE 2 PENDING tabs.

The **Diagnostics → Open surface report** button opens the auto-generated markdown
report (one row per button across every tab) so you can audit the whole surface.

---

## 5. Bridge architecture (System A vs System B)

There are **two** distinct in-process injection paths:

1. **System A — `powrprof.dll` Lua bridge** (primary). The editor talks to the game
   over a named pipe (`\\.\pipe\swfoc_bridge`) that's served by the in-game DLL.
   Every `SWFOC_*` Lua helper lives here. This is the path 90%+ of buttons go
   through.
2. **System B — `SwfocExtender.Host` sidecar** (secondary). A separate executable
   that loads `SwfocExtender.*` plugins for surfaces that need direct memory mutation
   without the Lua round-trip (e.g. some galactic-mode mutations). Look for the
   sidecar process when troubleshooting `ATTACH_NO_PROCESS` errors.

If both processes are running, the editor uses System A by default; System B
plugins are opt-in per-tab.

---

## 6. Save corruption playbook

**Soft-lock symptom**: SWFOC loads the save, the world renders, but the engine
thread spins forever in some monitor/behavior queue. Common in long AOTR / FOTR
campaigns.

### Triage

1. **Open Save Monitor tab.** It logs every save in the SWFOC folder. Look for an
   anomaly badge (red row) with a size delta `> 5 MB` vs the prior save — this is
   the canonical signature of a runaway monitor/behavior queue.
2. **Open Savegame Rescue tab → Parse** the broken save. If the parser bails before
   reaching the chunk tree, the file is structurally corrupt (rare). If it parses
   cleanly, the corruption is at the entry level inside one of the high-volume
   chunks (`0x3E9` = AI/scripting, `0x3EA` = primary state, `0x4B5` = AI TaskForce).
3. **Open Galaxy Visualizer → Inspect selected**. The chunk-ID distribution panel
   shows where bytes went. The Extracted galactic state panel confirms which planets
   the save references. If you see a planet co-located with all 6 factions and a
   suspiciously large chunk_size, that leaf is suspect.
4. **Roll back instead of repair.** Saves are cheap. Load the most-recent save that
   the Monitor tab did NOT flag as anomalous. Per Thread A's byte-delta scan
   (`.remember/save_diagnosis/THREAD_A_BYTE_DELTA_REPORT.md`), stripping AI
   TaskForce entries in a broken save breaks cross-references and crashes the
   engine — strip-based repair is **not** a viable recovery path.
5. **If you must keep the broken save:** use `tools/savegame_rescue/strip_by_path.py`
   with a path-positional argument scoped to the specific runaway sub-tree. See the
   tool's docstring for the path syntax (`0x3E9:0/0x1:0/0x3:4` etc.). Always verify
   the patched save re-parses cleanly before in-game testing.

### D3D9 flicker is NOT corruption

If a clean save loads but the game flickers / shows black frames / refreshes hard,
that's a Windows DWM + D3D9 fullscreen-exclusive interaction, NOT save corruption.
Fix:

- Disable Fullscreen Optimization on `StarWarsG.exe` (Properties → Compatibility)
- Run as Administrator
- Optional: switch to windowed mode (bypasses the D3D9 fullscreen path entirely)

Don't run `RedrawWindow` / `WM_DISPLAYCHANGE` against a wounded D3D9 device —
you'll promote a soft-failure into a hard crash. See
`~/.claude/projects/.../memory/feedback_d3d9_redraw_intervention.md`.

---

## 7. Settings

`%LOCALAPPDATA%\SwfocTrainer\v2_settings.json` (file kept on this name for
backwards-compatibility with prior installs; the UI just calls it "the settings
file").

| Field | Default | Purpose |
|---|---|---|
| `PipeName` | `\\.\pipe\swfoc_bridge` | Lua bridge pipe. Change only if you renamed the bridge DLL's pipe. |
| `Theme` | `Auto` | `Auto`, `Light`, or `Dark`. Auto follows the system theme. |
| `ToolsRoot` | `%USERPROFILE%\Downloads\swfoc_memory` | Folder containing the `tools/` directory (Python rescue toolkit lives in `tools/savegame_rescue/`). |
| `IconsRoot` | `(empty)` | If set, points to a folder with extracted asset icons (`.dds`). Used by Spawning / HeroLab / Asset Browser. |
| `SwfocGameRoot` | `(empty)` | Override the SWFOC game install root. Useful for non-Steam installs or when running with custom mod loaders. |
| `AutoConnect` | `false` | If true, the editor connects to the bridge on startup. |
| `Mod` | `(none)` | The active mod for catalog source-of-truth. Picks "Catalog source = active mod" on Spawning. |
| `AnomalyGrowthBytes` | `5_000_000` | Save Monitor / Galaxy Visualizer "anomalous growth" threshold. |

**Reset to defaults** is gated by a Yes/No confirmation. **Open settings file** opens
the JSON in your default editor. **Open log file** opens the bridge log
(`%LOCALAPPDATA%\SwfocTrainer\bridge.log`).

---

## 8. Troubleshooting

### "Could not connect to bridge"

1. Is `StarWarsG.exe` running?
2. Is `powrprof.dll` deployed to the game directory? Run `bridge/bridge_test_quick.py`
   to verify the named pipe is up.
3. Check Settings → PipeName matches the bridge's actual pipe name.
4. Open the log file (Settings → Open log file) for connect-side errors.

### "ATTACH_NO_PROCESS" errors in live tests

The `SwfocExtender.Host` sidecar process probably exited. Either:
- Launch SWFOC with the mod-loader that auto-spawns the sidecar
- Or run `native/runtime/SwfocExtender.Host.exe` manually before launching SWFOC

### "Failed to launch Python"

Galaxy Visualizer's Inspect button shells out to `python -m tools.savegame_rescue`.
Make sure:
- Python 3.11+ is on PATH
- Settings → ToolsRoot points to the parent of `tools/`
- The Python module at `tools/savegame_rescue/` exists in that path

### Tests fail with FlaUI "Process not found"

The UI tests in `SwfocTrainer.UiTests` launch the editor and probe it via FlaUI. In
headless / non-interactive sessions this fails because no desktop session is
available. Run the test wrapper with `-Filter "FullyQualifiedName!~UiTests"` to skip
the UI suite if you only want unit coverage.

---

## 9. Build from source

```powershell
# Restore + compile
dotnet build SwfocTrainer.sln --no-incremental --verbosity normal -c Release

# Unit tests (Clink-bypass wrapper avoids cmd.exe injection noise)
powershell -File C:\Users\Prekzursil\Downloads\swfoc_memory\tools\run_editor_tests_v2.ps1 -NoBuild

# Self-contained single-file publish
dotnet publish src\SwfocTrainer.App\SwfocTrainer.App.csproj `
  -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:SelfContained=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --output artifacts\publish
```

Build target: **0 warnings / 0 errors**. Tests target: **0 failures** in
`SwfocTrainer.Tests`; UI tests can be skipped in non-interactive sessions.

---

## 10. What's NOT in this build

Honest list of operator-visible deferred items. None of these affect the core
trainer functionality.

- **Iter 450c — SWFOC_TriggerVictory active injection.** Cannot be wired without
  multi-iter binary RE (per the event-driven-defer rule —
  `~/.claude/projects/.../memory/feedback_event_driven_defer_pattern.md`). Use the
  Galactic tab's planet-owner-change wires + AI suspend instead to force a
  conquest end.
- **Per-slot ATTACKER damage multiplier.** Phase 2 pending. The global form
  (`SWFOC_SetDamageMultiplierGlobal`) is LIVE. The per-slot form requires detours
  at ~58 caller sites where the attacker slot is in scope. Use the global form for
  global damage scaling and `SWFOC_SetDamageModifierLua` for per-unit damage
  RECEIVED scaling.
- **Per-hero respawn-timer table.** Phase 2 pending. The global default-respawn-time
  wire (`SWFOC_SetHeroRespawn`) is LIVE and covers ~80% of operator workflows.
- **AOB drift in third-party CE tables.** Community Cheat Engine tables drift
  across binary versions. Always verify semantic consistency
  (decompile + spot-check) before trusting any AOB-resolved address in this build.
  Codified in `~/.claude/projects/.../memory/feedback_aob_drift_across_binary_versions.md`.

---

## 11. Where to look next

- **Bridge / DLL deployment:** `bridge/README.md` in the docs repo.
- **Engine reference:** `knowledge-base/alamo_engine_reference.md` (RVA tables,
  struct layouts, Lua 5.0.2 quirks).
- **RVA ledger:** `knowledge-base/verified_facts.json` + auto-generated
  `knowledge-base/VERIFIED_RVAS.md`.
- **Per-iter operator changelog:** `knowledge-base/operator_changelog_*.md` files.
- **Recent session diagnosis:** `.remember/save_diagnosis/` — has the May 8 Frida
  capture (`LIVE_DIAGNOSIS_2026-05-08.md`) + the Thread A byte-delta report
  (`THREAD_A_BYTE_DELTA_REPORT.md`).
