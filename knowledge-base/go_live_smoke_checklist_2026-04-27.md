# Go-Live Smoke Checklist — 2026-04-27

Deferred to the end of the implementation loop per user instruction
("defer live testing until you the very end"). When the operator is ready
to attach the editor to a running game, walk this list top-to-bottom.

## 0. Pre-flight (once per session)

- [ ] Verify deployed bridge DLL: copy
  `swfoc_lua_bridge/powrprof.dll` (≈ 365 KB, dated 2026-04-27 or later)
  to `<game install>/Star Wars Empire at War Forces of Corruption/` so
  it loads ahead of `C:\Windows\System32\powrprof.dll`.
- [ ] Verify deployed editor: launch from
  `C:\Users\Prekzursil\Downloads\SWFOC editor\publish\SwfocTrainer.App.exe`
  (single-file, 3.5 MB+, dated 2026-04-27 or later).
- [ ] Settings tab → confirm `autoConnect = true` and theme = Dark (or Light).
- [ ] Launch the game in **vanilla** skirmish first — galactic comes later.

## 1. Connection & Diagnostics tab — vanilla skirmish

- [ ] Status banner is **green** (Bridge ready).
- [ ] `SWFOC_GetVersion` returns the current bridge build string
  (look for `v1.5-dev+a (2026-04-11, 37 live helpers, snapshot v2)` or
  newer).
- [ ] `SWFOC_GetBuildInfo` shows a non-`(empty)` string.
- [ ] Registered helpers count is **>= 60** (today's bridge registers 86).
  If you see "(probe not available on this bridge build — update
  powrprof.dll)" you are running an older DLL.
- [ ] Self-test reports `failed=0`.
- [ ] Bridge log tail is updating live as you click Refresh.

## 2. Player State tab — slot map auto-detect

- [ ] **Player slot dropdown** shows "Slot N — FACTION" for every active
  slot (e.g. "Slot 0 — REBEL", "Slot 1 — EMPIRE"). If labels are bare
  "Slot 0/1/2" you are on an older `OnWindowLoadedAsync` build.
- [ ] **Faction dropdown** contains EMPIRE, REBEL, UNDERWORLD plus any
  faction the live game reports (vanilla returns 3-4; mods return more).
- [ ] **No "Switch faction" button** in the Heroes group — the slot-based
  "Switch to selected slot (v3 + AI swap)" is the authoritative path.
- [ ] Click "Detect local" → status shows your current slot + faction.
- [ ] Set credits → operator should see the value reflected in the HUD
  within 1 second.
- [ ] Set tech level (5) → operator should see tech progression unlock
  in the build menu.

## 3. Unit Control tab

- [ ] God mode toggle on a selected unit → unit takes damage but HP
  doesn't drop.
- [ ] OHK toggle → enemies die in one hit.
- [ ] Set Unit Hull (12345) → HUD shows the value, no Lua parse error
  in the bridge log.
- [ ] Set Unit Invuln on / off — unit blinks the invuln visual.
- [ ] PreventUnitDeath on a low-HP unit → unit can't be killed.
- [ ] Spawn unit (default `NEB_B_FRIGATE`) → frigate appears at camera.
- [ ] **Faction dropdown** matches the Player-State tab's dropdown
  (live-merged via `V2FactionRegistry`).

## 4. Spawning tab — live filter

- [ ] Click "Refresh from live game" → status line shows
  "Refreshed from live game: kept N of M types …" with N < M (the
  bridge filtered out catalog entries the running game doesn't have).
- [ ] Faction filter dropdown auto-populated with the prefixes from
  the live catalog (e.g. `EMPIRE`, `REBEL`, `UNDERWORLD` for vanilla;
  `AOTR`, `EMPIRE`, `REBEL` etc. for AOTR).
- [ ] Domain filter narrows correctly (`Space` → only space units;
  `Ground` → only ground units).
- [ ] Selecting a type and clicking Spawn places the unit at the camera.

## 5. Combat tab

- [ ] God / OHK toggles work as in tab 3.
- [ ] **PHASE 2 PENDING — REPLAY MIRROR ONLY** amber banner is visible
  inside the Scalars GroupBox.
- [ ] Damage / shield / fire-rate values can be entered but the
  operator understands they only apply via the replay harness, not live.
- [ ] **Target filter checkboxes**: ENEMY / FRIENDLY / NEUTRAL toggle
  the bitmask (0x1 / 0x2 / 0x4); Apply sends `SWFOC_SetTargetFilter`.

## 6. Speed tab

- [ ] Global game speed: type 0.5, click Apply → game runs at half speed.
- [ ] Click "Pause" preset → game freezes.
- [ ] Click "1.0x" preset → game resumes.
- [ ] Click "2.0x", "4.0x" presets → game accelerates.
- [ ] Per-faction / per-unit groups show **PHASE 2 PENDING** amber banner.

## 7. World State tab

- [ ] Set diplomacy (faction A=EMPIRE, B=REBEL, relation=Allied) →
  factions stop attacking each other.
- [ ] **Story event ID dropdown** shows STORY_TEST_EVENT, EVENT_GAME_WON,
  EVENT_GAME_LOST, EVENT_TUTORIAL_COMPLETE, EVENT_RESEARCH_COMPLETE.
- [ ] Toggle maphack → fog-of-war reveals.
- [ ] Faction dropdowns are the same shared list as elsewhere.

## 8. Galactic mode (re-launch the game in galactic)

- [ ] Re-run sections 1, 2, 3 in galactic. Most behave identically.
- [ ] Galactic tab → Refresh planets shows the planet DataGrid.
- [ ] DiplomacyRelations dropdown shows ALL enum values
  (Hostile / Neutral / Allied — pre-fix only Allied + Hostile).
- [ ] Change planet owner → planet flag updates on the galactic map.

## 9. Cross-Faction tab

- [ ] Paste a unit obj_addr from Tactical Units, set source slot,
  target slot. Click Recruit. Unit changes ownership.
- [ ] Set source.OwnerSlot == target → live preview banner shows
  "⚠ source.OwnerSlot (3) == target (3); recruitment will be a no-op."
  in amber. Click anyway → State rejects with the same message.

## 10. Hero Lab

- [ ] Refresh heroes → DataGrid populates.
- [ ] **Respawn column** shows human-readable values
  ("5.0 sec", "1 min 30 sec", "—" for permadeath).
- [ ] Set custom respawn (10000) → `SWFOC_SetHeroRespawnTimer` fires.
- [ ] Toggle permadeath on alive hero → kill the hero → does not respawn.
- [ ] Edit stat (max_hull, 99999) → hero reports new max.

## 11. Mod-isolation cross-check (the big one)

- [ ] Quit vanilla. Launch the game with a different mod
  (AOTR / ROE / Thrawn's Revenge / etc.).
- [ ] Editor reconnects automatically (auto-connect on startup).
- [ ] **Player slot labels** show the mod's faction names
  (e.g. "Slot 0 — REPUBLIC" in Thrawn's Revenge).
- [ ] **Faction dropdown** reflects the mod's factions (no leakage
  from the previous vanilla session).
- [ ] **Spawn tab** "Refresh from live game" filters the catalog —
  mod-specific units appear, vanilla-only units drop out.
- [ ] Quit and switch back to vanilla. Repeat — vanilla types now
  appear, mod types drop out.

## 12. Theme + Dark-mode contrast

- [ ] Settings → Theme = Dark → all ComboBoxes (Player slot, Faction,
  Spawn faction filter, Spawn domain filter, Story event id) render
  with **dark backgrounds + light readable text**. Do NOT see white
  ComboBox fields with invisible text.
- [ ] Settings → Theme = Light → all ComboBoxes render light with
  dark text.
- [ ] Theme switch is live — no need to relaunch.

## 13. Stress / freeze guard

- [ ] Toggle "Freeze credits" with a target value → credits hold for
  > 60 seconds without the host PC slowing down. (The 2026-04-27
  ValueFreezeService rewrite removed `timeBeginPeriod(1)` +
  `AboveNormal` priority + sync-over-async; this is the regression
  guard.)

## Pass criteria

A go-live PASS requires:
- All checkboxes in sections 1-12 marked complete.
- No bridge crashes / pipe errors during the walk.
- Bridge log shows no `[err]` lines outside of operator-induced ones
  (e.g. "set tech with no value" → expected error).

If ANY checkbox fails, file a bug with:
- Section + step number,
- Expected vs actual,
- Bridge log tail (Diagnostics tab → log viewer).
