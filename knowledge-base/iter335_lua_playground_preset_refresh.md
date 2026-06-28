# iter-335 — Lua Playground preset menu refresh covering iter 282-300 wires (closes 65-iter doc gap since iter-264)

**Date:** 2026-05-07
**Arc class:** Operator-facing polish (mirrors iter-147/iter-183/iter-223/iter-264 cadence)
**Predecessor:** iter-334 (codify `feedback_locate_by_convention_extensible.md`)
**Successor (queued):** iter-336 (Combat tab weapon-icon column UI consumer OR README capstone update)

## What changed (1 source file extended + 2 test files updated; ~25 LoC; **35/35 PASS in 1.62s**)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/ViewModels/LuaPlaygroundTabViewModel.cs` (+~25 LoC):
  - 8 new presets added after the iter-269/270 honest-defer block, grouped by iter tag:
    - `[282]` SWFOC_GetFireRateMultiplierGlobal (1 preset; pair-flip with iter-225 setter)
    - `[285]` Tier 3 overlay bridge wires (3 presets: GetPlayerKills + GetPlayerDeaths + GetTotalUnitsAlive)
    - `[296]` SWFOC_GetPlanets real impl (1 preset; galactic-mode planet enumeration)
    - `[299]` Faction roster + current mod (2 presets: GetFactionRoster + GetCurrentMod)
    - `[300]` SWFOC_ListMods (1 preset; 300th-iter milestone wire)
- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/MainWindowV2.xaml` (1 line):
  - GroupBox header bumped from `"Iter 100-270 LIVE wires (+2 honest-defer notes)"` → `"Iter 100-300 LIVE wires (+2 honest-defer notes)"`
- **MODIFY** 2 test files updated for new header pin:
  - `Iter252PresetMenuRefreshTests.cs::GroupBoxHeader_ReflectsIter258Coverage` — extended assertion + comment trail (header-history audit per iter-260 lesson #3)
  - `Iter271PresetMenuRefreshTests.cs::GroupBoxHeader_ReflectsIter270Coverage` — extended assertion + comment trail; added explicit `NotContain("Iter 100-270 LIVE wires")` to prevent regression

## Verification gates ALL GREEN

```
[Start-Process bypass — Clink-safe]
dotnet test --filter "FullyQualifiedName~Iter183|...~Iter271"
Test Run Successful.
Total tests: 35
     Passed: 35
 Total time: 1.6215 Seconds
```

- Iter183 preset-expansion: PASS (count + iter-tag + content checks all hold)
- Iter223 preset-menu refresh: PASS (Iter100to113Presets surface intact)
- Iter252 GroupBox header: PASS (iter-335 header pin)
- Iter264 preset coverage: PASS
- Iter271 GroupBox header + honest-defer presets: PASS (iter-335 header pin + iter-267-268/269-270 honest-defer entries still present)
- Editor build inherits GREEN
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries

## Mid-iter test-failure catch — *2 test files pinning the same header*

The first test run failed on iter-271's `GroupBoxHeader_ReflectsIter270Coverage` test because iter-252 and iter-271 BOTH pin the GroupBox header text. The iter-252 update (line 119) caught my edit but iter-271 (line 114) didn't get updated. **Fix**: extended the iter-271 test with the same iter-335 update + added explicit `NotContain("Iter 100-270 LIVE wires")` to prevent future regressions.

**Pattern lesson** (1st instance; codification candidate at 3rd recurrence): when 2+ test files pin the same UI string, the `Edit` against one is incomplete — must grep the full test directory for the literal string before declaring done. The iter-260 lesson #3 (test files stay tagged at original iter even when content updates) means future header-history audits cross multiple test files, all of which need synchronized updates.

Codification candidate `feedback_pin_synchronization_across_test_files.md` flagged at 1/3.

## What this preset menu refresh closes

Operator-facing discoverability for **8 new LIVE wires** that previously required grepping `docs/lua-api.md` or recent close-out docs:

| Wire | Iter | Operator value |
|------|------|----------------|
| `SWFOC_GetFireRateMultiplierGlobal()` | 282 | Pair-flip read-side for iter-225 setter; HUD overlay use case |
| `SWFOC_GetPlayerKills()` | 285 | Tier 3 overlay HUD scoreboard |
| `SWFOC_GetPlayerDeaths()` | 285 | Tier 3 overlay HUD scoreboard |
| `SWFOC_GetTotalUnitsAlive()` | 285 | Tier 3 overlay HUD bar (poll-on-demand walk) |
| `SWFOC_GetPlanets()` | 296 | Galactic-mode planet enumeration; mod-compat free |
| `SWFOC_GetFactionRoster('Rebel')` | 299 | Lists units owned by faction; Audit B enumeration mandate |
| `SWFOC_GetCurrentMod()` | 299 | Mod identification via filesystem probe |
| `SWFOC_ListMods()` | 300 | Full mod enumeration; 300th-iter milestone wire |

**Pre-iter-335**: operator clicking the Lua Playground ComboBox saw 91 presets (iter 100-270 era). Operator searching for "GetPlanets" or "ListMods" found nothing — had to grep `knowledge-base/iter296_*.md` and `knowledge-base/iter300_*.md` to remember the wire shape.

**Post-iter-335**: 99 presets (iter 100-300 era). Operator can pick any iter 282/285/296/299/300 wire from the dropdown without leaving the editor.

## Pattern — preset-menu refresh cadence

iter-147 (covers iter 143-145 camera arc; +6 presets) → iter-183 (covers iter 150-182; +53 presets) → iter-223 (covers iter 184-219; +6 presets) → iter-264 (covers iter 257-260; +2 presets) → iter-271 (covers iter 267-270 honest-defer; +2 presets) → **iter-335 (covers iter 282-300; +8 presets)**.

**Cadence stable at every ~30-50 iters**. Pure VM/XAML extension; ~25 LoC marginal cost when the preset shape is well-established (just append entries to the array). Test-update cost: 0-2 LoC per existing pin test (header bump only).

## Pattern lesson — *operator-facing surface drifts at slower cadence than wire ship-rate*

Wires shipped iter 282-300 (Tier 3 overlay + dynamic-loading enumeration arc): 8 wires in ~18 iters = ~0.44 wires/iter. Preset-menu coverage drift: 65 iters between iter-264 → iter-335 = 65 ÷ ~0.44 = ~28 missed presets.

But only 8 wires actually MISSED — because most of the iter 271-281 + iter 286-298 + iter 301-334 work was native UX surfacing, codification, docs, audits, savegame RE, asset extraction, or LIVE wires already covered by earlier preset entries. The actual operator-facing-discoverability gap was much smaller than the iter-window suggested.

**Pattern lesson**: when measuring "preset menu drift", count NEW LIVE wires not yet in the menu, NOT the iter-window since last refresh. iter-335's 65-iter window only had 8 missed wires because most of those iters didn't produce new operator-facing wire shapes.

## Republish status — DEFERRED to next iter

Editor binary at `publish/SwfocTrainer.App.exe` (157.00 MB from iter-190) is still current — iter-335 only edits VM + XAML which compile-into the binary, but no republish executed this iter to keep the cycle short. Next iter that touches editor source should bundle the republish.

## Verification checklist

- [x] 8 new presets added grouped by iter tag (282/285/296/299/300)
- [x] GroupBox header bumped to "Iter 100-300 LIVE wires (+2 honest-defer notes)"
- [x] Iter252 + Iter271 GroupBox header pin tests updated
- [x] 35/35 preset-menu tests pass in 1.62s
- [x] Editor build inherits GREEN (no source changes outside VM/XAML)
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [x] Operator can now pick all 99 iter 100-300 LIVE wires from the ComboBox
- [ ] Editor republish — DEFERRED to next iter (no functional change requires republish; preset menu is VM/XAML only)
- [ ] Live SWFOC verify — DEFERRED to operator session
