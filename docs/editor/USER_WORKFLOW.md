# SWFOC Editor + Bridge — User Workflow

**Audience:** Someone who hasn't touched the stack in a week.
**Goal:** Go from "game is not running, editor is not open" to "I can
click buttons in the editor and observe changes in my live SWFOC game."

This doc is deliberately concrete — every command, every path, every
status indicator. If something in here doesn't match what you see on
screen, the stack has drifted and you should stop and diagnose before
pushing further.

---

## Cold start (first session after a break)

### 0. Prerequisites (one-time, verify once per machine)

- **Game installed** at `D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\` with `StarWarsG.exe` present. If Steam put it somewhere else, update `.claude\ida-pro-mcp.local.md`'s `expected_idb_path` first.
- **MinGW-w64** on `PATH` (`x86_64-w64-mingw32-g++ --version` should work) — used only if you need to rebuild the bridge C++ code.
- **.NET 8 SDK** on `PATH` (`dotnet --version` should show `8.x`) — used to build and run the editor.
- **Python 3** on `PATH` (`python --version` should show `3.x`) — used to lint the ledger and generate synthetic snapshots.

### 1. Verify the bridge is ready (before starting the game)

The bridge ships as `swfoc_lua_bridge\powrprof.dll`. It replaces the
Windows stock `powrprof.dll` via DLL hijack, so it must be pre-deployed
into the game's directory BEFORE the game launches.

```powershell
# Compare source vs deployed SHA256
certutil -hashfile "C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge\powrprof.dll" SHA256
certutil -hashfile "D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\powrprof.dll" SHA256
```

- **If the two SHAs match** → the deployed DLL is current; skip to Step 2.
- **If they differ** → the deployed DLL is stale. Back up the deployed copy, then copy the source version over:

```powershell
cd "D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption"
Copy-Item powrprof.dll powrprof.dll.backup-$(Get-Date -Format yyyy-MM-dd)
Copy-Item "C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge\powrprof.dll" .
```

> ⚠️ **Current state as of 2026-04-09**: the source DLL is 3 days newer
> than the deployed copy. You WILL need to deploy before the first live
> test — see `knowledge-base/handoff_2026-04-09.md` Section 7 for the
> full context.

### 2. Rebuild the bridge (only if source has changed)

```powershell
cd "C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge"
.\build.bat
```

Expected final output:
```
=== ALL BUILDS AND TESTS PASSED ===
```
plus `bridge_test_harness.exe` reports `=== Results: 295 passed, 0 failed ===`
and `powrprof.dll` is regenerated in this directory.

### 3. Launch the editor

```powershell
cd "C:\Users\Prekzursil\Downloads\SWFOC editor"
dotnet build                                       # incremental; must show "0 Warning(s), 0 Error(s)"
# Then either run from Visual Studio, or:
.\src\SwfocTrainer.App\bin\Debug\net8.0-windows\SwfocTrainer.App.exe
```

The editor is a WPF app. On first launch it will:
- Scan for installed SWFOC profiles
- NOT attach to the game (deliberate — it waits until you attach manually)

### 4. Launch the game

Start SWFOC from Steam like normal. Wait until you're on the main menu.

### 5. Attach

In the editor, click the **Attach** button (top of Runtime tab). Status
should change from "Detached" to "Attached to StarWarsG.exe (PID #####)".

### 6. Verify the bridge is live

Click the **Ping Bridge** or equivalent diagnostic button. The status
indicator should show:
- **Green / "Bridge v1.0 (or newer) connected"** — you're good to go.
- **Red / "Pipe not found"** — the DLL didn't load. The game is running the stock `powrprof.dll` from `C:\Windows\System32\` instead of our proxy. Diagnose:
  1. Check that `D:\...\corruption\powrprof.dll` is our version (SHA compare from Step 1)
  2. Check Windows Event Viewer for DLL load errors
  3. Restart the game

- **Red / "Bridge version mismatch"** — the deployed DLL is older than the editor expects. Redeploy and restart the game.

---

## The happy path

*"I want to god-mode my Vengeance Frigate in a tactical battle."*

### Step 1 — Enter a tactical battle

Start a skirmish (or load a save) and get into tactical combat. Make
sure your Vengeance Frigate is present and selectable.

### Step 2 — Find its object address

The editor's **Diagnostics** tab has a "Unit Inspector" section with an
"Address" text box and an "Inspect" button. But you need to know the
object address first. Two options:

- **Option A (manual)**: open Cheat Engine, attach to `StarWarsG.exe`, find your frigate via unit list scan, copy the `this` pointer hex value.
- **Option B (future)**: `SWFOC_FindSelectedUnit()` helper (doesn't exist yet — TODO item for next session).

### Step 3 — Inspect to confirm

Paste the hex address (without `0x`, as decimal or prefixed hex) into
the Unit Inspector box and click **Inspect**. Expected response:

```
hull=<current>/<max>  owner=<slot>  obj_id=<id>  parent_idx=0xFF  status_flags=0x00  prevent_death=0x00  invuln_flag=0x00  hardpoint_flag=0xFF  components_ptr=0x<address>
```

If `parent_idx` is `0xFF`, you've selected the root unit (good — frigates
are roots, hardpoints are children). If it's anything else, you selected
a hardpoint and need to walk up.

### Step 4 — Enable god mode

Click the **God Mode** toggle button (Combat tab, or Bridge Helpers tab
depending on the editor version). Expected editor status: `"god mode
enabled"`. In-game verification:

- Let an enemy shoot your frigate
- HP should NOT decrease (enemy damage is blocked)
- If you order the frigate to take damage (repair hack, friendly fire), HP
  also doesn't decrease

The underlying mechanism is the `SetHP` MinHook detour — it intercepts
every call to the HP-write function, walks the parent chain via
`obj+0x335 ParentIdx` and `obj+0x278 Components`, checks `owner == local
player`, and if yes, bails out of the write entirely.

**Hardpoints propagate automatically** — the hook walks the parent chain
so damage to a hardpoint also fails the check and gets blocked. You do
NOT need to individually enable god mode per hardpoint.

### Step 5 — Turn it off

Click the **God Mode** toggle again (or its "Disable" pair if the UI has
separate buttons). Expected status: `"god mode disabled"`. The MinHook
detour is removed, and subsequent damage calls pass through unchanged.

### Step 6 — Detach cleanly before quitting

Before closing the game, click **Detach** in the editor. This releases
the named pipe and lets the bridge's pipe thread exit gracefully. If you
close the game without detaching, the editor will show
"Pipe disconnected" and may need a restart before the next attach.

---

## Troubleshooting

### 1. Editor shows "bridge not connected"

**Symptom:** Status indicator red, all bridge calls fail with "pipe
not found" or "ConnectNamedPipe failed (error 2)".

**Root cause:** One of:
- The deployed `powrprof.dll` is the stock Windows one, not ours
- Our DLL is deployed but the game hasn't loaded it yet (check whether
  the game is actually running and fully initialized)
- A previous session left the pipe in a weird state

**Fix:**
```powershell
# Verify deployment
certutil -hashfile "D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\powrprof.dll" SHA256

# Compare with source
certutil -hashfile "C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge\powrprof.dll" SHA256

# Kill any stray replay binaries that might be holding the pipe name
Stop-Process -Name swfoc_replay -Force -ErrorAction SilentlyContinue

# Restart the game from scratch (Steam -> exit game -> relaunch)
```

### 2. Bridge connects but commands silently no-op

**Symptom:** Status says "Bridge v1.0 connected", commands return `"OK"` or `"1"`, but the expected in-game effect doesn't happen.

**Root cause:** The deployed DLL is older than the helper the editor is
calling. For example, the editor calls `SWFOC_SetCreditsForSlot(0, 50000)`
but the old deployed DLL only knows `SWFOC_SetCredits(amount)`, so the
Lua interpreter returns `nil` for the unknown function name and the
wrapper silently no-ops.

**Fix:** Compare SHA256 as in #1. If they differ, redeploy the bridge.
Watch for the version string: a stale bridge will still return
`"SWFOC Lua Bridge v1.0"` so the version string alone isn't enough — the
SHA is the authoritative drift check. After redeploy, restart the game.

**Preventive:** Bump the version string in `lua_bridge.cpp::Lua_GetVersion`
every time you add a SWFOC_* helper so drift becomes visible from the
editor's status bar.

### 3. Stray `swfoc_replay.exe` blocks the pipe with error 231

**Symptom:** Log shows `[Replay] CreateNamedPipe failed: 231` or
`ERROR_PIPE_BUSY`. The editor can connect via the LIVE bridge pipe
(`\\.\pipe\swfoc_bridge`) but the REPLAY mirror pipe
(`\\.\pipe\swfoc_bridge_replay`) is stuck.

**Root cause:** A previous test run left `swfoc_replay.exe` alive with
an open pipe. Windows doesn't release the pipe name until the process
dies.

**Fix:**
```powershell
Stop-Process -Name swfoc_replay -Force -ErrorAction SilentlyContinue
# Verify it's gone:
Get-Process swfoc_replay -ErrorAction SilentlyContinue
```

### 4. Feature works in replay tests but not in the live game

**Symptom:** `FullFeatureSmokeTest` passes 14/14 in the replay harness,
but when you click the button in the live editor the in-game effect
doesn't happen.

**Root cause:** The replay binary is a PARALLEL UNIVERSE — it accepts
the same pipe protocol as the live bridge but its underlying state
manipulation is done against `fake_memory.cpp` stubs, not the real game.
Specifically, the replay's `fake_lua` cannot execute arbitrary Lua
method calls like `p1:Make_Ally(p2)` — it short-circuits a small table
of known `SWFOC_*` helpers and returns canned responses for everything
else. So a service that EMITS non-SWFOC_* Lua (DiplomacyService,
CooldownManagerService, etc.) will "pass" the replay smoke test
(command accepted) but do nothing in the live game unless the live
game's Lua interpreter actually has the `Make_Ally` wrapper registered.

**Diagnosis:**
- Open `lua_bridge.cpp` and check whether the service's `BuildLuaCommand`
  output contains a `SWFOC_*` helper from the registration table.
- If yes → the live bridge will execute it. Problem is elsewhere
  (memory offset, argument type, etc.).
- If no → the service is calling a game-side Lua function. Check the
  verified_facts.json ledger to confirm it actually exists as a Lua
  binding in the real game. If it's documented but still fails, the
  binding may be scoped to a specific game mode (tactical vs galactic).

**Fix:** depends on the specific service. See
`knowledge-base/v5_service_fixes_applied.md` for the canonical list of
service fixes and their Lua command shapes. If a service needs a fix,
add a row to that file AND add a regression guard pair in
`tests/SwfocTrainer.Tests/Regression/`.

### 5. Game crashes when a feature is toggled

**Symptom:** Clicking a button (especially God Mode or Inspect) causes
the game to immediately CTD (crash-to-desktop) or hang.

**Root cause:** The bridge DLL installed a MinHook detour that is
reading or writing an invalid memory address. This is almost always
because:
- A struct field offset is wrong (e.g. `GameObject+0x5C HP` is
  actually different in this build of SWFOC)
- The MinHook target RVA is wrong (the detour installed at
  `base + 0x3A89D0` intercepts a different function than expected)
- The hook is installed at the right RVA but the surrounding bytes were
  patched by a game update

**Fix:**
1. IMMEDIATELY disable the bridge: close the editor (pipe disconnects),
   then close the game. Restart the game WITHOUT attaching.
2. If the game still crashes on startup, swap the deployed DLL back to
   the pre-deploy backup:
   ```powershell
   cd "D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption"
   Copy-Item powrprof.dll.backup-* powrprof.dll -Force
   ```
3. Open an issue with:
   - Which feature was toggled when the crash happened
   - The SHA256 of the DLL that was running
   - The last few lines of `swfoc_bridge.log` in the game directory (if any)
   - The game version (Steam build number)

**Preventive:** Before installing a new `SetHP` detour, the bridge
verifies the 6-byte prologue `40 53 48 83 EC 60` (push rbx; sub rsp,
60h) still matches. If SWFOC shipped a patch that moved SetHP's address
or changed the prologue, the hook install refuses and logs a warning.
Check `swfoc_bridge.log` for this message BEFORE enabling any combat
feature after a game update.

---

## Quick reference

| Path | What it is |
|---|---|
| `C:\Users\Prekzursil\Downloads\swfoc_memory\CLAUDE.md` | Project-level agent instructions (read first) |
| `C:\Users\Prekzursil\Downloads\swfoc_memory\.remember\now.md` | Last-session handoff state |
| `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\handoff_2026-04-09.md` | This session's handoff brief (11 sections) |
| `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\feature_readiness_matrix_2026-04-08.md` | Which features are READY |
| `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\verified_facts.json` | RVA ledger (304 entries, source of truth) |
| `C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge\powrprof.dll` | Source bridge DLL (must be deployed before game launch) |
| `D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\powrprof.dll` | Deployed bridge DLL (this is what the game actually loads) |
| `C:\Users\Prekzursil\Downloads\swfoc_memory\tools\run_editor_tests_v2.ps1` | Editor test runner (Clink-bypass wrapper) |

| Command | What it does |
|---|---|
| `.\build.bat` in `swfoc_lua_bridge\` | Rebuild bridge DLL + harness + replay binary (requires MinGW-w64) |
| `.\bridge_test_harness.exe` | Run 295 offline bridge tests |
| `python make_test_snapshot.py <path>` | Generate a synthetic v2 snapshot |
| `python make_test_snapshot.py <path> --v1` | Legacy v1 snapshot (back-compat test) |
| `python make_test_snapshot.py <path> --v2-early` | v2 header but no extended sections (back-compat test) |
| `.\swfoc_replay.exe <snap.swfocsnap>` | Start the replay binary on the synthetic snapshot |
| `python smoke_test_replay.py` | 12-command smoke test against the replay pipe |
| `python -m verifier lint` from `tools\` | Lint the ledger (must be 0 errors, 0 warnings) |
| `dotnet build --verbosity minimal` in editor dir | Incremental editor build |
| `dotnet build --no-incremental --verbosity normal` | Clean rebuild — reveals all warnings |
| `powershell -File tools\run_editor_tests_v2.ps1` | Run full editor test suite |
| `powershell -File tools\run_editor_tests_v2.ps1 -Filter "Category=Replay"` | Replay-tagged tests only (fast) |

---

**Last updated:** 2026-04-09 (Phase E of the functional-readiness session).
See `knowledge-base/handoff_2026-04-09.md` for the full session context.
