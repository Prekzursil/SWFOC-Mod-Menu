# SWFOC Trainer Editor — v1.0.0

**For Star Wars: Empire at War — Forces of Corruption (Steam, 64-bit).**

This is the first public release of a desktop tool that attaches to a running
SWFOC instance and lets you mutate the game in real time — credits, units,
heroes, planets, AI behaviour, cameras — over a named-pipe Lua bridge.

It also opens `.PetroglyphFoC64Save` files directly so you can inspect chunks,
monitor save folders for the canonical soft-lock signature, and extract a
planet roster + faction tokens from a save without launching the game.

---

## Two modes in one app

The top bar has a mode toggle. Tabs swap when you flip it.

### 🎮 Live Trainer (~22 tabs)

Attach to `StarWarsG.exe` and modify the game while it runs.

- **Player State** — per-slot credits, tech level, hero respawn, faction switch
- **Economy** — credit multipliers, freeze credits, drain enemy treasuries
- **Unit Control** — selected-unit heal/damage, invuln, fire-rate, shield, speed, teleport, owner change
- **Combat** — damage/fire-rate/speed scalars (Easy/Normal/Hard/Hardcore), hardpoint inspector
- **Galactic** — per-planet owner change, diplomacy, FOW reveal, TaskForce write
- **Hero Lab** — mass revive, respawn-timer presets, permadeath toggles
- **Camera & Debug** — set/read XYZ, follow, rotate, zoom, cinematic, letterbox, free-cam
- **Director Mode** — multi-step cinematic flow editor with waypoint save/load
- **Lua Playground** — free-form Lua dispatch with named recipes and preset menus
- **Quick Actions** — composite workflows: Battle / Filming / Tournament / Sandbox / Streaming
- **Asset Browser** — cross-asset thumbnail browser sourced from extracted `.meg` icons
- Plus: Story Events, World State, Inspector, Event Stream, Tactical Units, Probes, CrossFaction Recruitment, UnitStatEditor, Settings

Every operator button surfaces its **capability badge** so "bridge call succeeded" is never confused with "engine state actually changed":

- 🟢 **LIVE** — fully wired and verified end-to-end
- 🟢 **LIVE ONLY** — works only while attached to a running scene
- 🟡 **MIXED (N/M LIVE)** — composite where some primitives are still Phase 2
- 🟠 **PHASE 2 PENDING** — RVA pinned but the hook is dormant; tooltip cites the LIVE alternative

### 💾 Savegame Editor (4 tabs)

Open `.PetroglyphFoC64Save` files directly. No game needed.

- **Savegame Rescue** — parse / chunk-scan / strip / splice / diff via the bundled Python toolkit
- **Save Monitor** — watch the SWFOC save folder for the canonical `>5 MB` growth anomaly (the soft-lock signature)
- **Save Auto-Tools** — auto-snapshot on mutation OR interval; rotating prefix-named archive
- **Galaxy Visualizer** — per-save health cards + chunk-id distribution + **real planet roster + faction tokens** mined from the 0x3EA / 0x3E9 / 0x3EB chunks

---

## Why "Trainer Editor"?

The two modes are intentional. Live mutation is the trainer half. Save inspection is the editor half. A campaign that soft-locks isn't necessarily lost — open it in the Savegame Editor, find the runaway chunk, snapshot a known-good copy from the Auto-Tools archive.

---

## Bridge architecture (the gory details)

The Live Trainer talks to the game over `\\.\pipe\swfoc_bridge`, served by an in-process DLL stub (`powrprof.dll`) that the game loads on startup. Lua 5.0.2 is the engine's embedded scripting language — every helper you see in the editor is a Lua function the bridge registered into the game's primary Lua state. The bridge auto-detects which of the game's ~400 Lua states is the "real" one (the one with global functions like `Find_Object_Type` registered) and routes all calls there.

The optional **SwfocExtender.Host sidecar** loads plugins for surfaces that need direct memory mutation without the Lua round-trip (some Galactic-mode TaskForce mutations, for example). The editor defaults to the Lua bridge and only reaches for the sidecar when a tab is specifically wired through it.

---

## What's NOT in this release

Three engine surfaces are Phase 2 pending. They surface in the editor with orange badges and a tooltip pointing to the LIVE workaround. The badges exist so the operator is never misled.

| Surface | Workaround |
|---|---|
| `SWFOC_TriggerVictory` (force a victory condition) | Galactic tab planet-owner change + AI suspend |
| Per-slot attacker damage multiplier | Global form `SWFOC_SetDamageMultiplierGlobal` (LIVE) |
| Per-hero respawn-timer table | Global form `SWFOC_SetHeroRespawn` (LIVE) |

The reverse-engineering work to flip these to LIVE is documented in the knowledge base (`knowledge-base/iter450a_*.md` etc.) and tracked for future releases.

---

## Quality bar at release

- **Build**: 0 warnings / 0 errors (`dotnet build -c Release --no-incremental`)
- **Unit tests**: 8395 / 0 failed / 5 skipped / 8400 total
- **Stress test (3× consecutive)**: 0 / 3 failed runs
- **Bridge harness** (C++ in-process tests, no game required): 1100 / 0
- **Replay binary smoke** (synthetic snapshot round-trip): 12 / 12
- **RVA verifier ledger lint**: 0 errors / 0 warnings @ 341 ledger entries
- **Semgrep `p/csharp` security ruleset**: 0 findings
- **Cyclomatic complexity** (lizard): 0 functions over CCN 15 / length 50
- **Self-contained binary**: 158 MB single-file, no .NET install required

---

## Installation

1. Download `SwfocTrainerEditor_2026-05-20.zip` from the release assets below.
2. Verify the bundled SHA256:
   ```powershell
   Get-FileHash SwfocTrainer.App.exe -Algorithm SHA256
   # Should match the value in SHA256SUMS.txt
   ```
3. Deploy the bridge DLL alongside `StarWarsG.exe` (see `USER_GUIDE.md` for detail).
4. Launch SWFOC.
5. Run `SwfocTrainer.App.exe`. Connection & Diagnostics tab → Connect.

**Windows SmartScreen warning** is expected on first launch — the binary is unsigned. Click "More info" → "Run anyway".

---

## Reporting bugs

Open an issue on this GitHub repo. Please include:

1. The output of the **Connection & Diagnostics → Diagnostics → Open surface report** button (markdown table of which wires are LIVE on your install).
2. Your `swfoc_bridge.log` (next to `StarWarsG.exe`).
3. The mod loader / mod you're playing under (vanilla, AOTR, FoTR, RoE, etc.).

---

## Credits

Built on top of [PetroglyphTools](https://github.com/AnakinSklavenwalker/PetroglyphTools) for save-file structure references. Engine RE done in IDA Pro + Ghidra + Binary Ninja cross-validated. Stress-test discipline borrowed from the broader `superpowers:test-driven-development` skill family.

Lua 5.0.2 is the engine's embedded scripting language — bridge API documentation is in `knowledge-base/alamo_engine_reference.md` if you're modding alongside.
