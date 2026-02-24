# SWFOC Knowledge Base for This Project

Last updated: 2026-02-15

This document captures source-backed facts that directly affect `SWFOC editor` process detection, profile routing, and runtime debugging.

## 1. Launch Modes You Must Support

Sources:

- https://modtools.petrolution.net/articles/Creating_A_Mod
- https://steamcommunity.com/discussions/forum/1/4841930637303525852/
- https://www.moddb.com/mods/republic-at-war/downloads/republic-at-war-v18

Key forms:

- Local/dev mod:
  - `swfoc.exe MODPATH=Mods\\MyMod`
- Local/dev mod with spaces in path:
  - `swfoc.exe MODPATH="C:\\...\\My Mod"`
- Workshop mod:
  - `swfoc.exe STEAMMOD=<workshop_id>`

Project impact:

- Process locator should parse both `MODPATH` and `STEAMMOD` when command line is available.
- Attach heuristics should still work when command line is unavailable.
- Submod workflows are often local `MODPATH` even when parent mod is workshop-based.

## 2. Patch Reality (Do Not Assume 2017-era Runtime)

Sources:

- https://www.petroglyphgames.com/eawmodtool/
- https://steamcommunity.com/games/32470/announcements/detail/5455609499987684897
- https://steamcommunity.com/games/32470/announcements/detail/4551542220607036511

Key facts:

- Official mod support page says last updated on February 3, 2026.
- Steam patch from November 20, 2023 converted both games to 64-bit applications.
- October 22, 2024 maintenance patch reports major FoC galactic performance improvements and additional bug fixes.

Project impact:

- Signature offsets and runtime assumptions are version-sensitive.
- Keep profile/source indicators explicit (`Signature` vs `Fallback`) and avoid hardcoding one build epoch.

## 3. Official Debug Kit Exists and Was Updated in 2026

Source:

- https://www.petroglyphgames.com/eawmodtool/EAW_FOC_Debug_Kit_64.zip

Observed metadata on 2026-02-15:

- Last-Modified: 2026-02-04
- Archive includes updated `corruption` debug binaries/PDBs dated 2026-02-03.

Project impact:

- Debug-kit sessions can alter executable/module signatures and symbol expectations.
- Consider detecting/flagging debug-kit environments explicitly to reduce false-negative signature failures.

## 4. Logs and Fast Triage

Practical source:

- https://steamcommunity.com/sharedfiles/filedetails/?id=2726462029

Common logs used by community troubleshooting:

- `...\\Star Wars Empire at War\\corruption\\LogFile.txt`
- `...\\<mod>\\Data\\Scripts\\LuaScriptLog.txt` (when script logging is produced)

Project impact:

- Keep runtime diagnostics copy-pastable and include expected log locations in error/status UI.

## 5. File and Asset Pipeline References (for Future Editor Features)

Sources:

- https://modtools.petrolution.net/docs/Formats
- https://modtools.petrolution.net/docs/MegFileFormat
- https://modtools.petrolution.net/docs/AloFileFormat
- https://modtools.petrolution.net/docs/AlaFileFormat
- https://modtools.petrolution.net/docs/MtdFileFormat
- https://modtools.petrolution.net/docs/DatFileFormat
- https://modtools.petrolution.net/docs/LuaFileFormat

Project impact:

- Any future save/content editing helper should treat MEG and loose files as different deployment modes.
- Text/localization, model, animation, and script workflows all have format-specific caveats and tooling.

## 6. Local Workspace Mod Reality (Ground Truth)

Observed in this repo:

- `1397421866(original mod)` is a full-scale payload (hundreds of XML/Lua/TED files).
- `3447786229(submod)` is a targeted override payload (smaller but still broad).
- `3661482670(cheat_mode_example)` is script-only plugin style.

Project impact:

- Do not assume one canonical mod shape.
- Dependency checks and feature gating should handle partial payload submods cleanly.

## 7. Linked Skill for Ongoing Use

A dedicated skill was created at:

- `/home/prekzursil/.codex/skills/swfoc-modding`

It contains:

- `SKILL.md` with workflow routing
- `references/` with official + community source synthesis
- `scripts/check_sources.py` for quick source freshness checks
