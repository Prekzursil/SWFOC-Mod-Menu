# SWFOC Trainer Editor — Comprehensive Improvement & Testing Plan

**Date**: 2026-05-20
**Status**: synthesized from 4-agent parallel brainstorm (B1-B4) + live-game empirical findings
**Scope**: UI/UX improvements + broken-feature triage + autodetection opportunities + comprehensive test plan

---

## Executive Summary

Four independent audits (UI/UX, feature brokenness, autodetection, test-plan) converged on the same insight: **the editor's biggest pain point isn't broken bridge wires — it's the manual data entry that the bridge already has helpers for**. The bridge ships 225 helpers including comprehensive read-side coverage of game state; the editor exposes ~213 of them but only ~150 with native UX, and those 150 require typed object addresses, typed slot numbers, typed planet names, typed faction names — when every one of those is reachable via a bridge READ call.

**Highest-leverage interventions (ranked by operator-hours-saved-per-LoC):**

1. **Hoist `TryParseObjAddr` from `UnitControlTabViewModel.cs:1364` to `SwfocTrainer.Core.Validation`** and route all 7+ obj_addr-consuming tabs through it. Standardize the tooltip text to `"Obj addr (hex 0x... or decimal)"` everywhere. — eliminates the #1 every-session frustration.
2. **Extract `SlotPickerComboBox` UserControl** mirroring Player State's `SelectedSlotEntry` (`xaml:849`). Replace every typed-integer slot field across 8 tabs. — removes the `-1 = local; 0..N = specific` magic-number cognitive overhead.
3. **Bind `Button.IsEnabled` to `<Action>.Badge != "PHASE 2 PENDING"`** via a converter for the 5+ tabs where PHASE 2 buttons currently fire empty bridge calls. — eliminates "I clicked but nothing happened" confusion.
4. **Build `bridge/full_editor_test_matrix.ps1`** orchestrator (10-day investment) — verifies 95% coverage in 30 min wall-time, gives operators a "is the editor still working after this change?" answer.
5. **Bridge banner string `"37 live helpers"` → `"174 live helpers"`** — 1-LoC fix that stops the cognitive dissonance.

**Cumulative deferred bridge work** (multi-iter RE arcs, not blocking operator UX):
- `SWFOC_TriggerVictory` LIVE flip: 3-4 iters remaining (iter-450b kickoff doc ready)
- `SWFOC_SetGameSpeed` LIVE flip: 1-2 iters (reader exists, find writer)
- `SWFOC_SetIncomeMultiplier` per-slot LIVE: 1 iter (extend existing `Hook_AddCredits`)
- `SWFOC_FreeCam` LIVE flip: 1 iter (compose existing `Lock_Controls` + `SetCameraPos`)
- `SWFOC_FreezeAI`: deprecate (use existing `SWFOC_SuspendAiLua` LIVE)
- `SWFOC_GetLocalPlayerLua` fallback path: ~15 LoC bridge edit

**Test plan validates everything**: the JSONL-based harness at `bridge/full_editor_test_matrix.ps1` runs 6 phases (preflight / baseline / per-tab matrix / composite workflows / failure-mode probes / restore+report) in ~30 min wall-time, emits structured logs ingestible by CI, and self-corrects via the baseline-snapshot revert chain. Without this harness, each UI/UX change risks silent regressions in 22 tabs × 335 buttons.

---

## Part 1 — UI/UX Improvements

### CRITICAL (every-session pain)

1. **`obj_addr` format is inconsistent across 6+ tabs** — Unit Control says "hex" (`xaml:1062`), Inspector says "decimal or hex without 0x" (`:2325`), Combat says "decimal" (`:2624`), Speed says "decimal" (`:3089`), Hero Lab says "decimal" (`:3875`), Hardpoint says "hex e.g. 0x... or decimal" (`:2929`), Cross-Faction says "dec or hex w/o 0x" (`:4628`). Operators paste from Inspector "Copy obj_addr (hex)" (emits `0x...`) into Combat/Speed/HeroLab and the call silently fails because those parsers expect decimal. **Fix**: hoist `TryParseObjAddr` (already in `UnitControlTabViewModel.cs:1364`) to shared `SwfocTrainer.Core.Validation`; standardize all labels to `"Obj addr (hex 0x... or decimal)"`.
2. **Mode switcher has no state banner** — `xaml:282-317`. Operators flip Live↔Savegame and tabs vanish/appear with nothing in the title bar saying which mode is active. **Fix**: bind window title to `IsLiveMode` and append ` — LIVE TRAINER` / ` — SAVEGAME EDITOR`.
3. **`Slot: -1` magic-number exposed on 4+ tabs** — Combat `:2622`, Economy `:2144`, Battle Control `:3987`, Spawning `:3260`. Tooltip says "-1 = local; 0..N = specific slot" — operators must memorize. **Fix**: replace with ComboBox of `{Local player (auto), Slot 0 — REBEL, ...}` driven by the existing `Slots` collection in Player State (`xaml:849`).
4. **PHASE 2 PENDING buttons remain clickable** — Speed Apply/Pause/0.5x/1x/2x/4x (`:3005-3036`), Economy Income/Build mults (`:2194-2205`), Combat damage scalar Apply (`:2708`), Director SetTimeScale (`:4591`), Galactic owner-change (`:3566+`). Tooltip says "Disabled" but `IsEnabled` isn't bound to badge state — clicking fires a bridge call that succeeds-with-no-effect, polluting activity log. **Fix**: bind `Button.IsEnabled` to `<Action>.Badge != "PHASE 2 PENDING"` via a converter; keep tooltip explanation.

### HIGH (recurring annoyances)

1. **Tooltips expose internal class names** — `xaml:1476` `"ICorruptionService → ..."`, `:1490` `"IDiplomacyService → ..."`, `:1511` `"IStoryEventService → ..."`, `:1708` `"ICrashAnalyzerService → ..."`, `:2936` `"Walks Components array via lua_bridge.cpp:2228"`, `:4848` `"env SWFOC_RESCUE_TOOLS"`, body label `:1928` still mentions `v2_settings.json`. **Fix**: drop the `IFoo →` prefix from operator tooltips; reserve internal names for Diagnostics activity log.
2. **Stale `iter N` references in OPERATOR-VISIBLE strings** — `xaml:2925-2926` Hardpoint header + tooltip reference `iter-281`, `lua_bridge.cpp:2228`, `iter-340+`. `xaml:4372` ComboBox GroupBox header `"Iter 100-300 LIVE wires (+2 honest-defer notes)"` is jargon. **Fix**: rename `:4372` → `"LIVE wire examples"`; drop iter-N + filepath refs from operator-facing tooltips (keep in code comments).
3. **Unit Control "Spawn unit" row (`:1117-1127`) duplicates Spawning tab** but with NO capability badge, NO position fields, NO tooltip. **Fix**: either delete the row + add `"Open Spawning tab →"` link, or mirror Spawning's `:3289` badge + tooltips.
4. **Combat / Speed / Hero Lab "Selected obj_addr" TextBoxes have no `Use selected` button** — only Unit Control `:1064` has it. **Fix**: add `Use selected` button next to every `SelectedObjAddr`; auto-bind to a shared `MainViewModelV2.SelectedObjAddr` so a single click on a Tactical Units row fills all tabs.
5. **World State PlanetId / Faction-A/B use raw TextBox** — `xaml:1470` `PlanetIdInput`. Planet names are case-sensitive engine ids (`CORUSCANT` not `Coruscant`) and validation lives in the bridge. **Fix**: editable ComboBox pre-populated from `Galactic.Planets` (already loaded) — mirror Story Event Id pattern at `:1505-1509`.
6. **"VO toggle (typo: Reponse)" is operator-visible** — `xaml:1616` exposes the engine misspelling explicitly to operators. **Fix**: button label `VO on/off`; remove the visible `(typo: Reponse)` — keep rationale in the tooltip at `:1619`.
7. **`Auto-refresh (2s)` state isn't persisted across sessions** — `InspectorTabViewModel.cs:32 _isAutoRefreshEnabled` is private with no `_state` write-back (compare `EconomyTabState.Slot` at `EconomyTabViewModel.cs:189`). Same for Event Stream `Auto-drain (1s)` (`:4452`), Diagnostics auto-poll. **Fix**: write to `_state.IsAutoRefreshEnabled`; default true is fine.
8. **Spawning's "Spawn" button at `:3288` is replay-only but visually IDENTICAL** to the legitimate "Spawn (Lua, LIVE) →" at `:3319-3322`. Same green chrome, same `MinWidth=420`. Operators scrolling fast click the wrong one. **Fix**: visually demote the PHASE 2 button — gray background, italic font, or move it BELOW the LIVE block.

### MEDIUM (polish)

1. **Hardpoint Inspector + Hero Lab DataGrid `:3825` clip the "respawn" column** at default width. **Fix**: `MinColumnWidth` or `Width="*"` for variable rows.
2. **Combat "OHK Attack Power" + "Area Damage" tooltips are blunt** — `:2658` `"PHASE 2 PENDING — replay-only"`. Other P2 buttons get a verbose "why" + LIVE-alternative pointer. **Fix**: match the verbose Speed-Apply tooltip pattern (`:3006`).
3. **`Iter 100-300 LIVE wires (+2 honest-defer notes)` GroupBox header (`:4372`)** — internal pipeline jargon. **Fix**: rename to `LIVE wire examples (300+)`.
4. **Settings "Browse..." next to LogPath (`:1871`)** opens OpenFileDialog for a file that may not exist. **Fix**: `SaveFileDialog` or `OpenFileDialog.CheckFileExists=false`.
5. **Tactical Units' `Faction slot` filter (`:2059-2060`) is a plain Width=80 TextBox** while Player State uses the wide Slot ComboBox at `:849`. **Fix**: same Slots ComboBox.
6. **Quick Actions composite button labels are giant** — `:5630` `"Operator god mode (GodMode + Heal me + Uncap credits)"` consumes a full row. **Fix**: short label "Operator god mode" + caption row listing the 3 primitives (iter-194 pattern).
7. **`Search obj_addr (hex)` filter on Tactical Units (`:2061-2062`)** is a TextBox doing substring match — operators try regex/decimal and silently get zero rows. **Fix**: rename header `Filter (hex substring)` or accept both forms.
8. **Inspector Read-side WrapPanel (`:2376-2467`) has 14 buttons in one WrapPanel** — visual wall. **Fix**: 3 sub-groups (Identity / State / Combat) matching the catalog organization.

### LOW (visual nits)

1. Capitalization drift — `Player slot:` (`:835`) vs `Owner slot:` (`:2584`) vs `owner` (`:4473`) vs `Slot:` (`:2622`). Standardize on `Slot:`.
2. Mixed em-dash vs hyphen — `"Slot 6 — UNDERWORLD"` (`:837`) vs `"Operator god mode (GodMode + Heal me + ...)"` (`:5630`). Pick one.
3. Diagnostics `Reset filters` (`:596`) vs `Clear log` (`:629`). Standardize verbs: `Reset` for defaults, `Clear` for drop.
4. Tactical Units status `Border` uses `TabBackground` (`:2128`) while everywhere else uses `StatusBackground`. Unify.

### CROSS-CUTTING (architectural)

1. **Capability-badge surface inconsistency** — Battle Control, Cross-Faction, Tactical Units, Lua Playground all show `CapabilityBadge + LastStatus` consistently. Spawning's status row is at the bottom yet the WARNING banner with same info is mid-tab (`:3279`). Settings has no capability area. **Fix**: extract `<TabFooterStatus>` UserControl, use everywhere, remove duplicate banners.
2. **TextBox validation is opt-in per VM** — UnitControl has `TryParseObjAddr` (hex+decimal+0x-prefix); Inspector/Combat/Speed/Cross-Faction parse independently with different `NumberStyles`. **Fix**: hoist `TryParseObjAddr` to shared `SwfocTrainer.Core.Validation` static; route every obj_addr-consuming VM through it. Add inline red border `Validation.ErrorTemplate` for pre-click feedback.
3. **No "address picker" dialog** — every obj_addr input requires typing/pasting. Tactical Units tab has the data but no `Pick obj_addr from list...` button on consuming tabs. **Fix**: small `...` button next to every obj_addr TextBox opens a modal with the Tactical Units DataGrid + double-click to commit.
4. **`PHASE 2 PENDING` shown 4 different ways** — yellow Warning banner (Combat `:2615`), inline `(PHASE 2 PENDING)` in GroupBox header (`:1684`), per-button `Badge` TextBlock (`:2659`), warning-style red border (Hero Lab `:3866`). **Fix**: pick one — keep per-button badge (most granular); remove duplicate banners.
5. **Tab order vs workflow order mismatch** — Player State (Tab 2) and Unit Control (Tab 3) make sense, but Tactical Units (Tab 7) is where obj_addrs live and Inspector (Tab 9) is where you read them. Operators land on Combat (Tab 10) expecting "Slot=-1" to just work without knowing they need to fill SelectedObjAddr from two tabs back. **Fix**: reorder to Diagnostics → Player State → Tactical Units → Inspector → Unit Control → Combat → ... (selection-driven first, mutation-driven next).
6. **Only Inspector tooltip says "Paste from Tactical Units tab"** — Combat, Speed, Hero Lab, Cross-Faction don't repeat it. **Fix**: standardize "Paste from Tactical Units, or click `...` to pick" everywhere.

---

## Part 2 — Feature Brokenness Triage

Bridge ships 225 SWFOC_* helpers; editor consumes ~213. Catalog: **174 LIVE / 18 PHASE 2 PENDING / 2 LIVE-ONLY**. Autonomous probe: 43 PASS / 3 honest P2 / 1 TACTICAL-ONLY / 1 ENGINE-ERR / 0 unexpected on 48-helper smoke.

### Category 1 — Engine-Side Quirks (bridge can work around)

1. **`SWFOC_GetLocalPlayerLua` raises `Get_Local_Player rc=1`** in galactic when no fleet selected. `DiagSelfTest` already reads `local_slot=6 OK` via `PlayerListClass+0x30` direct memory. **Fix** (~15 LoC bridge): fallback path in `Lua_GetLocalPlayerLua` calls the direct-memory probe on engine-error and returns synthetic `Find_Player(<faction_name>)`. Saves operators from a confusing red error.
2. **`SWFOC_GetGameModeLua` returns "Space" when space-unit selected in galactic** — engine's `Get_Game_Mode()` is context-dependent. **Fix** (~10 LoC bridge wrap): prefer `BatchTypeExists`-style probe; compose `"Galactic (Space-selected)"` when Get_Game_Mode says Space but BatchTypeExists rejects.
3. **`SWFOC_PlayerGiveMoneyLua` raises rc=1** in galactic — `Give_Money` engine API needs context the bridge doesn't supply. **Fix** (~20 LoC): detect rc=1, resolve `player_lua_expr` → slot via roster walk, fallback to `SetCreditsForSlot(slot, current+amount)`. OR simpler: deprecate the dispatcher, document `SWFOC_SetCreditsForSlot` as canonical write path.
4. **`SWFOC_GetPlanets` returns `(no_planets)`** in galactic Day 1 despite 8 populated factions. Implementation at `lua_bridge.cpp:2949-2982` tries 3 category strings (`Planet`/`GalacticPlanet`/`Planetary`); all 3 empty in vanilla Day 1. **Fix**: add 4th fallback walking `Find_Object_Type("Yavin")` etc. via hardcoded planet name list, dedup (~30 LoC). Alt: callgraph-RE the `GalacticPlanetVector` global and walk directly (~50 LoC).

### Category 2 — Phase-2-Pending Quick Wins (≤ 2 iters each)

1. **`SWFOC_SetGameSpeed`** — `SWFOC_GetSecondsPerGameMinuteLua` is LIVE (iter-178). The engine has a *reader* for the same data the *writer* needs. **Proposed**: callgraph-RE writer via `xrefs_to(Get_Seconds_Per_Game_Minute)` to find float global, add direct memory write. ~1-2 iter arc.
2. **`SWFOC_FreezeAI`** — records per-slot `g_pendingFreezeAi`. `SWFOC_SuspendAiLua` (iter-162) is LIVE alternative. **Proposed UX-only**: deprecate `FreezeAI` as legacy stub, route catalog rationale → `SuspendAiLua`. Zero RE needed.
3. **`SWFOC_FreeCam`** — no engine `Free_Cam(enable)` API. iter-208 `Lock_Controls` + iter-237 `SetCameraPos` are LIVE. **Proposed** (~30 LoC): emulate free-cam via tick-loop calling `Lock_Controls(true)` + leaving camera at SetCameraPos.
4. **`SWFOC_SetIncomeMultiplier`** (per-slot) — iter-231 shipped global form via `AddCredits` MinHook detour. **Proposed** (~10 LoC): add per-slot map check inside `Hook_AddCredits` before scaling.
5. **`SWFOC_SetBuildSpeed`** — needs build-progress-tick hook; unknown RVA. Per `feedback_event_driven_defer_pattern.md` multi-iter A1.x. Route operators to `SetCreditsMultiplierGlobal` as cheap-build alternative.

### Category 3 — Phase-2-Pending Multi-Iter Arcs

1. **`SWFOC_TriggerVictory`** — iter-450 scaffolding LIVE (DORMANT MinHook at `0x140341FE0`). iter-450a kickoff doc has 5-step path: dump `kAwaitingVictoryTestDefaultTemplate[16]` from `.rdata 0x804FC0`, decompile `0x140344710`, write discriminator branch, bridge harness tests, flip `MH_EnableHook`. ~3-4 iters remaining.
2. **`SWFOC_SetAreaDamage` / `SWFOC_SetTargetFilter` / `SWFOC_ToggleOHKAttackPower`** — all event-driven per iter-436. Need A1.x RE on `BarrageAreaBehaviorClass` / `UnitAIBehaviorClass` / `CombatantBehaviorClass`. `SetDamageMultiplierGlobal` LIVE covers global damage scaling meanwhile.
3. **`SWFOC_ListHeroes`** — iter-325 documented 3 gaps blocking LIVE flip. Needs hero-detection-table RVA pin OR Lua-handle→engine-addr surface. ~2-3 iter arc.
4. **`SWFOC_SetHeroRespawnTimer`** (per-hero) — per-hero RVA not in ledger. Global form `SWFOC_SetHeroRespawn` covers 80% of operator use cases.
5. **`SWFOC_SetUnitCapOverride`** — iter-249 found AOB drift on community CE entry. Needs live-tracing or fresh IDA xref walk. ~3-5 iter arc.

### Category 4 — Confusing Signatures (UX-only fix)

1. **`SWFOC_FOWRevealAllLua`** — operator runs without arg, gets `"expected arg_lua_expr"`. **Fix** (3 LoC bridge): add `usage:` line to the error message. Existing iter-200 Galactic-tab native UX already pre-fills `Find_Player("REBEL")`.
2. **`SWFOC_GetSpaceStationLevelLua`** — requires `(unit_lua_expr, arg_lua_expr)`. The receiver is actually a **PLAYER** handle (not unit); arg is a planet handle. **Fix** (XAML+VM only): rename Inspector tab fields to `player_lua_expr` + `planet_lua_expr`, tooltip `"Find_Player(...) + FindPlanet(...)"`.
3. **`SWFOC_TriggerVictory` error codes** — bridge returns 4 different `ERR_*` codes (`ERR_NO_ARG`, `ERR_BAD_ARG`, `ERR_UNKNOWN_TYPE`, `PHASE2_PENDING`). WorldState tab button doesn't differentiate. **Fix**: dispatch parses error prefix and surfaces "Enter one of: Galactic_Conquer / Skirmish_Control / ..." inline.
4. **`SWFOC_SetCreditsMultiplierGlobal` mult=0.0 is soft-freeze** (calls real_AddCredits with 0 delta) vs `SWFOC_SetCreditsFreezeGlobal` (blocks entirely). Editor surface doesn't disambiguate. **Fix**: Economy tab status badge surfaces this when `mult==0.0`.

### Category 5 — Duplicate Helpers (deprecate)

1. **`MakeAllyLua` / `MakeEnemyLua` (obj-receiver) + `GlobalMakeAllyLua/EnemyLua`** → all 4 deprecate; route UX through `SetDiplomacy` (LIVE, bypasses game-mode-change-reset caveat).
2. **`SpawnUnit` (P2)** vs **`SpawnUnitLua` (LIVE iter-109)**. iter-266 audit caught the drift. Remove legacy registration.
3. **`SetHeroRespawn` (global)** vs **`SetHeroRespawnTimer` (per-hero P2)**. Default UX to global; expose per-hero as advanced toggle only.
4. **`FreezeCredits` (slot, P2)** vs **`SetCreditsFreezeGlobal` (LIVE)**. Catalog deprecates already; remove from bridge registration.
5. **`SetDamageMultiplier` (per-slot P2)** vs **`SetDamageMultiplierGlobal` (LIVE)** vs **`SetDamageModifierLua` (per-unit LIVE)**. Triple coverage; per-slot variant has ZERO C# consumers per iter-328. Remove orphan.
6. **`SetFireRate` (P2)** vs **`SetFireRateMultiplierGlobal` (LIVE iter-225)**. Catalog deprecates already; remove legacy.

### Category 6 — Unused Bridge Helpers (expose in editor)

Of 225 helpers, ~12 unreferenced by editor source. Notable opportunities:

| Helper | Recommendation |
|---|---|
| `SWFOC_StateInfo` | Expose as Diagnostics read button (~5 LoC VM) — useful for "is bridge attached?" diagnostics |
| `SWFOC_TriggerAbility` + `SWFOC_ListAbilities` | Together form an **Ability Lab** surface on UnitControl tab (~50 LoC VM + 3 buttons) |
| `SWFOC_*Cinematic*` (Start/End/SetKey/TransitionKey + Letterbox + Fade + PointCameraAt + RotateCameraBy) | Consolidate into a dedicated **Cinematic** tab; ~150 LoC pure UX, zero bridge changes |
| `SWFOC_DiagPipeStats` / `SWFOC_DiagSelection` | Verify DiagnosticsTabViewModel surfaces these — if not, add ~10 LoC VM |
| `SWFOC_SetFreezeCredits` (legacy slot form) | Remove (superseded by global) |
| `SWFOC_GetDamageMultiplier` (legacy slot read) | Remove (superseded by `GetDamageMultiplierGlobal`) |

**Bonus 1-LoC fix**: `SWFOC_GetVersion` banner says "37 live helpers" but 174 are catalogued LIVE. Update `Lua_BridgeVersionString` constant.

---

## Part 3 — Autodetection Opportunities

### Tier 1 — Operator pain points (manual where unambiguous)

1. **Hero Lab → Selected addr** (`MainWindowV2.xaml:3875-3876`) — TextBox `SelectedHeroAddr`. Heroes DataGrid above (line 3822, `Binding HeroRows`) has no `SelectedItem` binding. **Fix**: add `SelectedItem="{Binding SelectedHeroRow}"` + sync writes `SelectedHeroAddr = row.ObjAddr`. Same pattern as Tactical Units' `SelectedRow` (line 2087).
2. **Galactic → Planet ID** (`xaml:3567-3568`) — TextBox `SelectedPlanetId`. PlanetRows DataGrid (line 3527) has no SelectedItem binding. **Fix**: add `SelectedItem="{Binding SelectedPlanetRow}"`. Also `NewOwnerFaction` TextBox at line 3570 → ComboBox bound to `{Binding Factions}` (mirror Spawning at line 1122).
3. **World State → Corruption Planet id** (`xaml:1469-1470`) — TextBox `PlanetIdInput`. **Fix**: editable ComboBox bound to `SWFOC_GetPlanets` (mirror `StoryEventSuggestions` at 1505-1509). Add "Refresh planets" button reusing existing `RefreshPlanetsCommand`. Share a `IPlanetCatalogProvider` across tabs.
4. **Galactic → Story-arrival Planet + Faction** (`xaml:3651-3653`) — Both TextBoxes. Planet → ComboBox from `SWFOC_GetPlanets`; Faction → ComboBox bound to existing `{Binding Factions}`.
5. **World State → Diplomacy Slot A/B** (`xaml:3611-3614`) — Two integer TextBoxes. **Fix**: replace with `ComboBox` showing `"Slot 0 — REBEL"`, `"Slot 6 — UNDERWORLD"` (pattern at line 850 for `SelectedSlotEntry`). Same `RefreshSlotMapCommand` (line 869).
6. **Inspector → Unit obj_addr** (`xaml:2325-2326`) — TextBox with tooltip "Paste from Tactical Units tab" (smell). **Fix**: add "Use selected" button calling `SWFOC_GetSelectedUnit` (pattern at Unit Control line 1064). Add "Pull from Tactical Units row" when a row is selected.
7. **Combat → Selected obj_addr** (`xaml:2624-2625`) — Same gap. Same fix (UseSelectedCommand/AutoUseSelected from Unit Control 1064-1065).
8. **Speed → Per-unit obj_addr** (`xaml:3089-3090`) — Same.
9. **Cross-Faction → Target slot** (`xaml:4651-4652`) — Source auto-fills (line 4642) but destination `TargetSlot` is hand-typed integer. Same slot ComboBox fix as #5.
10. **Battle Control → Target Slot** (`xaml:3987`) — Integer TextBox. Same slot ComboBox.
11. **Director Mode → Waypoint X/Y/Z/Rot/Zoom** (`xaml:4549-4557`) — 5 TextBoxes operator types. **Fix**: "Add waypoint from current camera" button reads `SWFOC_GetCameraPos` and pre-fills. Pairs with Save/Load path at 4578-4585.
12. **Camera Debug → X/Y/Z TextBoxes** (`xaml:4171-4179`) — `GetCameraPosCommand` exists (line 266) but result lands in status row only. **Fix**: bind read-back to populate `CamX/CamY/CamZ/CamZoom` for read→tweak-one-axis→write workflow.

### Tier 2 — "Auto-fill from game" buttons

1. **Inspector tab** — "Use selected" button calling `SWFOC_GetSelectedUnit`.
2. **Combat tab Scope group** (`xaml:2620-2627`) — "Use selected" + "Use local player slot" buttons. Local slot via `SWFOC_GetLocalPlayer` (existing `DetectLocalPlayerCommand` line 871).
3. **Speed tab Per-unit group** (`xaml:3088-3093`) — "Use selected" button.
4. **Economy tab Slot field** (`xaml:2143-2145`) — tooltip says "-1 = local player". **Fix**: "Local" button calls `SWFOC_GetLocalPlayer` and sets real slot integer.
5. **Battle Control Target group** (`xaml:3984-3990`) — "Use enemy of local" composite via `SWFOC_GetAllPlayers` + `Is_Enemy(local)`.
6. **Galactic Story-arrival Type ID** (`xaml:3647-3649`) — editable ComboBox sourced from `SWFOC_ListTacticalUnits` type ids.
7. **Spawning Slot field** (`xaml:3259-3260`) — slot ComboBox.
8. **Speed Faction Slot** (`xaml:3051-3052`) — slot ComboBox.

### Tier 3 — Polling / periodic refresh

1. **Camera Debug tab** — couple to `SWFOC_DiagGameTick`; X/Y/Z boxes auto-track as operator moves camera in-game. Reuse Inspector's pattern (lines 2349-2353, `IsAutoRefreshEnabled` @ 2s). Add `Auto-track camera (1s)` checkbox.
2. **Player State Credits Amount** (`xaml:882-883`) — when slot changes, prefill `CreditsInput` via `SWFOC_GetCredits` (reuse `GetCreditsCommand`). Makes "Get → tweak → Set" a 1-click round-trip.
3. **Player State Tech Level** (`xaml:897-898`) — same pattern: prefill `TechInput` from `SWFOC_GetTechForSlot(slot)` on slot change.
4. **Hero Lab DataGrid** — periodic auto-refresh of `SWFOC_ListHeroes` so respawn timers tick live. Same 2s timer.
5. **Galactic PlanetRows** — auto-refresh on tick so AI owner-changes show without manual Refresh.

### Tier 4 — Validation hints

1. **Unit Control Obj addr** (`xaml:1063`) — accept `0x...` AND decimal; validate against `SWFOC_ListTacticalUnits` known-addresses set; warn if not found before sending bridge command.
2. **Inspector obj_addr** (`xaml:2326`) — same validation. The "decimal or hex without 0x" tooltip is doing validation work the VM should do.
3. **Player State PlayerLuaExpr** (`xaml:946`) — validate Lua expression resolves to a player handle via `SWFOC_DoString` probe before action fires (avoids confusing engine errors).
4. **World State Corruption Level** (`xaml:1473-1474`) — numeric clamp + range hint (vanilla 0-3); add `UnitCapHint`-style live-validation strip (pattern at 4046-4050).
5. **Spawning Count** (`xaml:3261-3262`) — clamp 1-1000; warn on 0 (no-op) and >100 (perf cliff).
6. **Spawning PosX/Y/Z** (`xaml:3263-3268`) — fill from `SWFOC_GetCameraPos` ("Spawn at camera target") or `SWFOC_GetSelectedUnit` position. Add two prefill buttons.
7. **Hero Lab Edit Value** (`xaml:3948`) — type/range validation depends on `EditField` ComboBox selection; surface hint in muted text alongside.
8. **Unit Stat Editor TargetObjAddrs textarea** (`xaml:4717-4720`) — validate each parsed address against `SWFOC_ListTacticalUnits` and surface per-address "OK/unknown/wrong-owner" hint list. `SelectedUnitCount` (line 4720) counts but doesn't validate.

### Cross-cutting — common patterns to extract

1. **Reusable `SlotPickerComboBox`** — Player State's `SelectedSlotEntry` ComboBox (line 850, `DisplayLabel = "Slot 6 — UNDERWORLD"` via `RefreshSlotMapCommand` line 869) is the canonical UX. Extract a `SlotPickerControl` UserControl. Reuse on: World State Diplomacy, Galactic Diplomacy, Battle Control Target, Cross-Faction Target, Spawning Slot, Speed Faction Slot, Economy Slot. Shared `Factions` + slot-to-faction map already in `MainViewModelV2.cs`.
2. **Reusable `UnitPickerControl`** — bundles `ObjAddrInput` TextBox + `Use selected` button + `Auto-use selected` checkbox (pattern at Unit Control 1062-1066). Reuse on Inspector, Combat, Speed, Cross-Faction, Unit Stat Editor.
3. **Reusable `PlanetPickerComboBox`** — `IsEditable=True` ComboBox with `ItemsSource={Binding PlanetSuggestions}` sourced from `SWFOC_GetPlanets`. Mirror existing `StoryEventSuggestions` pattern (1505-1509). Use on World State Corruption, Galactic Owner/Story-arrival, Probes.
4. **Reusable `ReadFromGameButton`** — small button + status badge that runs an arbitrary read-side `SWFOC_*` command and writes the result back into a target binding. Backbone for Tier 3 prefill flows.
5. **Reusable `PeriodicAutoRefreshDriver`** — Inspector auto-refresh + Event Stream auto-drain (2349-2353, 4452-4456) both reimplement the same `PeriodicTimer` checkbox pattern. Extract `AutoRefreshControl`; apply to Hero Lab, Galactic PlanetRows, Camera Debug, Player State credits/tech.
6. **Standardise `ObjAddrInput` parsing helper** — across 7 tabs the tooltip text varies ("decimal or hex without 0x" / "paste from Tactical Units" / "hex 0x..."). Extract `ObjAddrParser.TryParse(text, out ulong addr, out string error)` so the same accept/reject/warn logic runs everywhere; tooltip text identical.
7. **Selection-bridge bus** — when a row is selected in Tactical Units, Hero Lab, or Galactic Planets DataGrid, publish an event that other tabs subscribe to so `ObjAddrInput`/`SelectedPlanetId`/`SelectedHeroAddr` autofill cross-tab. The "copy snapshot/copy addr" buttons at Inspector 2335-2343 are manually doing this today.

**Files referenced**: `MainWindowV2.xaml`, `CameraDebugTabViewModel.cs`, `HeroLabTabViewModel.cs`, `PlayerStateTabViewModel.cs`, `GalacticTabViewModel.cs`.

---

## Part 4 — Comprehensive Test Plan

### Test Matrix Overview

- **Total tabs**: 27 (22 Live Trainer + 4 Savegame + Galaxy Visualizer / Quick Actions cluster)
- **Total interactive elements**: 335 `<Button>` declarations in `MainWindowV2.xaml`; 413 `RelayCommand` bindings across 29 ViewModels
- **Total bridge helpers**: ~225 `SWFOC_*` entries in `CapabilityStatusCatalog.cs`
- **Currently covered by autonomous harness**: 79 / ~335 buttons (24%) via `autonomous_live_test.ps1` (48) + `extended_live_test.ps1` (30) + `focused_multiplier_test.ps1` (1)
- **Coverage target**: 95% of LIVE entries; honest P2-PENDING / TACTICAL-ONLY explicitly skipped with reason logged

### Per-tab matrix (27 tabs, ~335 buttons)

| # | Tab | Buttons | VM commands | Phase | Pre-condition | Test method |
|---|---|---|---|---|---|---|
| 1 | Connection & Diagnostics | ~15 | 41 | mode-agnostic | bridge connected | read-only probes + healthcheck snapshot |
| 2 | Player State | ~22 | 31 | galactic-preferred | LocalPlayer != 0 | write+revert per slot |
| 3 | Unit Control | ~38 | 55 | mixed | selection or live unit | hp_before/after, alive_before/after |
| 4 | World State | ~18 | 21 | galactic-only | running game | snapshot + revert |
| 5 | Probes & Scripts | ~8 | 14 | mode-agnostic | bridge connected | DoString round-trips |
| 6 | Settings | ~16 | 20 | offline-OK | none | config persistence + ConnectAttempt |
| 7 | Tactical Units (filter) | ~10 | 13 | tactical-only | in-battle | mode-skip in galactic |
| 8 | Economy | ~22 | 29 | galactic-only | running game | multiplier cycle |
| 9 | Inspector | ~16 | 22 | mode-agnostic | selection | field-read consistency |
| 10 | Combat | ~16 | 21 | galactic-OK | running | damage/firerate cycles |
| 11 | Speed | ~12 | 18 | galactic-only | running | per-faction speed cycle |
| 12 | Spawning | ~9 | 11 | tactical-only | in-battle | mode-skip in galactic |
| 13 | Galactic | ~18 | 22 | galactic-only | galactic running | planet-owner cycle with revert |
| 14 | Hero Lab | ~10 | 11 | tactical-only | hero alive | mode-skip in galactic |
| 15 | Battle Control | ~6 | 6 | tactical-only | in-battle | mode-skip in galactic |
| 16 | Story Events | ~4 | 2 | mode-agnostic | running | read-only |
| 17 | Camera & Debug | ~14 | 16 | mode-agnostic | running | get_camera_pos + letterbox toggle |
| 18 | Lua Playground | ~6 | 6 | mode-agnostic | bridge connected | preset eval round-trip |
| 19 | Event Stream | ~3 | 2 | mode-agnostic | bridge connected | drain + count |
| 20 | Director Mode | ~10 | 12 | mode-agnostic | bridge connected | letterbox/freecam P2-honest check |
| 21 | Cross-Faction Recruit | ~3 | 2 | galactic-only | running | snapshot only (P2-PENDING) |
| 22 | Unit Stat Editor | ~5 | 3 | offline-OK | bridge connected | dat-edit roundtrip |
| 23 | Asset Browser | ~3 | 1 | offline-OK | data files present | UnitIconResolver locate |
| 24 | Savegame Rescue | ~6 | 7 | offline-OK | save file selected | python-replay smoke |
| 25 | Save Monitor | ~3 | 4 | offline-OK | save dir | watcher delta detect |
| 26 | Save Auto-Tools | ~5 | 7 | offline-OK | save dir | rewrite + checksum |
| 27 | Galaxy Visualizer / Quick Actions | ~30 | 11 | galactic-only | running | end-to-end orbital ops |

Per-button row format: `{tab, button_tooltip_text, bound_command, expected_mode, expected_status_from_catalog, test_method, pre_value, post_value, revert_value, verdict}`

### Composite workflows (10 scenarios)

1. **Boost-income → mass-spawn drain**: SetCreditsMultiplierGlobal(10) → wait 10 ticks → snapshot credit delta per slot → SetTechForSlot(6,5) → spawn 100 units → snapshot credits drain. Validates iter-100 sign-gate fix end-to-end.
2. **Force conquest**: Galactic → ChangePlanetOwner(tatooine, 6) → ChangePlanetOwner(coruscant, 6) → ChangePlanetOwner(kuat, 6) → snapshot GetPlanets ownership counts → revert.
3. **Cinematic mode**: Camera & Debug → FreeCam(true) → LetterBoxOn → ZoomCameraLua(0.3) → GetCameraPos → revert all. (FreeCam is P2-PENDING — log verdict accordingly.)
4. **God-mode survivability sweep**: GodMode(true) for all local units → take damage cycle via SetDamageMultiplierGlobal(0.0)→(10.0) → verify hp unchanged → GodMode(false) revert.
5. **Hero resurrection loop**: Hero Lab → SetHeroRespawn(0.5s) → kill local hero → observe revive → revert respawn=60s. Galactic-mode = skip.
6. **Cross-faction diplomacy spin**: Galactic → SetDiplomacy(6,0,Ally) → SetDiplomacy(6,1,Ally) → MakeAllyLua(0,6) → snapshot all 8-slot diplomacy → revert.
7. **Mod-load lifecycle**: World State → GetCurrentMod (vanilla) → manual mod switch → re-probe → ListMods enumeration check.
8. **Stress write/read pipe**: 200× SWFOC_GetCredits() loop; expect 200 in `received` counter delta, no CONNECT_FAIL, p95 latency < 50ms.
9. **Build-info regression guard**: Quick Actions → GetBuildInfo → compare hash to known good → fail if DLL older than expected.
10. **Savegame round-trip**: Rescue → load known save → write → reload → checksum compare. Validates swfoc_replay.exe parity.

### Failure-mode tests

1. **Game paused mid-test**: SWFOC_DiagGameTick before/after each probe; if tick_after == tick_before for state-change tests → `verdict=PAUSED_GAME`, skip downstream dependents.
2. **Wrong game mode**: Pre-flight SWFOC_GetGameModeLua(); if expected != observed → `verdict=MODE_MISMATCH (need=X got=Y)` and skip. Documents the space-unit-selected-returns-"Space" gotcha.
3. **Bridge pipe drops**: Wrap every Send-LuaCmd with try/catch; CONNECT_FAIL after retry → `verdict=BRIDGE_DEAD`, abort with rescue footer.
4. **Stale DLL**: First probe = SWFOC_GetBuildInfo(); check against expected_dll_built ≥ 2026-05-20 06:00:25; older → `verdict=DLL_TOO_OLD`, abort.
5. **Helper raised engine-error rc=N**: classify as `ENGINE_ERR` (existing harness already does this).
6. **Args-missing helper**: `NEEDS_ARGS` classification (already in harness).

### Log format spec — JSONL

One JSON object per line. Easy to grep, parse, aggregate; survives PowerShell newline quirks.

```json
{"ts":"2026-05-20T13:00:00.123Z","tab":"UnitControl","feature":"Heal","capability_status":"LIVE","mode_expected":"GALACTIC","mode_observed":"Galactic","verdict":"PASS","evidence":{"hp_before":8000,"hp_after":8000,"alive_before":true,"alive_after":true},"diagnostic":null,"duration_ms":42,"bridge_pipe_stats_received_delta":1}
```

**Required fields**: `ts` (ISO-8601 ms UTC), `tab`+`feature` (locator), `capability_status` (catalog value), `mode_expected`/`mode_observed`, `verdict` (closed enum: PASS / P2-PENDING / MODE_MISMATCH / ENGINE_ERR / NEEDS_ARGS / CONNECT_FAIL / DLL_TOO_OLD / PAUSED_GAME / UNEXPECTED / FAIL), `evidence` (before/after/reverted/delta), `diagnostic` (only on non-PASS), `duration_ms`, `bridge_pipe_stats_received_delta`.

**Companion summary**: markdown auto-generated at end with pass counts by category, all FAIL rows expanded with diagnostic, p95/p99 latency, environment snapshot.

### Orchestration script — `bridge/full_editor_test_matrix.ps1`

Six phases (each gated on prior success):

```
Phase 0 — PREFLIGHT (~15s)
  - Check StarWarsG.exe PID
  - SWFOC_GetVersion + GetBuildInfo; verify DLL build >= expected
  - DiagPipeStats baseline (received counter)
  - GetGameModeLua; record observed mode
  - Load CapabilityStatusCatalog into hashtable

Phase 1 — BASELINE SNAPSHOT (~5s)
  Capture engine-state-snapshot to log header + baseline_$timestamp.json for revert

Phase 2 — PER-TAB MATRIX (~15 min)
  For each of 27 tabs (Diagnostics first, Savegame last):
    For each button (from VM RelayCommand list):
      - Look up helper in catalog
      - Pre-flight mode-check; skip if mismatch
      - Read baseline (write-cycle)
      - Send bridge command
      - Classify response
      - Read post-value
      - Revert + verify
      - Emit JSONL row

Phase 3 — COMPOSITE WORKFLOWS (~10 min)
  Run scenarios 1-10 sequentially. Each emits scenario_id + per-step rows.

Phase 4 — FAILURE-MODE PROBES (~3 min)
  Inject: stale-DLL, paused-game, mode-mismatch, pipe-drop simulation.

Phase 5 — RESTORE + REPORT (~30s)
  - Replay revert chain from baseline_$timestamp.json
  - Verify engine state matches baseline
  - Generate markdown summary (pass counts by tab/status/verdict)
  - Exit 0 if ≥95% pass on LIVE rows, else 1
```

**Invocation**: `powershell -File bridge/full_editor_test_matrix.ps1 [-Filter Tab=Economy] [-NoComposite] [-DryRun]`

**Logs**:
- `bridge/test_runs/full_matrix_YYYY-MM-DD_HHMMSS.jsonl` — primary structured log
- `bridge/test_runs/full_matrix_YYYY-MM-DD_HHMMSS.md` — human summary
- `bridge/test_runs/baseline_YYYY-MM-DD_HHMMSS.json` — pre-test state for revert + diff
- Stable copy: `bridge/test_runs/latest.{jsonl,md}` — for CI/HUD ingestion

**Summary section interpretation**:
- Top: total PASS / total LIVE-eligible (target ≥95%)
- "Honest non-PASS": P2-PENDING / MODE_MISMATCH / NEEDS_ARGS — expected, DON'T fail the run
- "Investigate": UNEXPECTED / ENGINE_ERR / FAIL — block CI green
- Latency p50/p95/p99; flag any call > 500ms
- DLL hash + mod + game mode pinned in header

### Effort estimate

| Work item | Effort |
|---|---|
| JSONL logger + Classify extension + catalog ingestion | 1 day |
| Per-tab matrix (256 new rows beyond existing 79) | 4 days (~64 rows/day) |
| 10 composite workflows | 1.5 days |
| Failure-mode harness (6 modes) | 1 day |
| Markdown summary generator | 0.5 day |
| Baseline-snapshot + revert chain | 0.5 day |
| End-to-end dry-run debug + first green | 1 day |
| Documentation + operator handoff | 0.5 day |
| **Total** | **~10 dev-days (2 calendar weeks)** |

**Key dependencies**: stable bridge DLL (shipped via v1.0.1), reproducible savegame baseline (operator-saved at Day 1 vanilla galactic Underworld slot 6), `CapabilityStatusCatalog.cs` exported as JSON via small `dotnet run` helper (1 hour, blocker for catalog status lookup).

**Risk**: ~30% of buttons are mode-restricted and will SKIP cleanly in galactic; need a separate tactical-mode pass to hit 95%. Build harness with `-Mode Tactical` filter from day 1.

---

## Cross-Cutting Synthesis

### Theme A — `obj_addr` and `slot` are the universal manual-input headache

B1 flagged inconsistent obj_addr formats across 7 tabs as the #1 CRITICAL. B3 flagged 12 manual obj_addr/slot/planet TextBoxes as Tier-1 autodetection opportunities. B4's test plan assumed obj_addrs would be selectable via a picker. **Combined fix**: build 3 reusable UserControls — `SlotPickerComboBox`, `ObjAddrPickerControl`, `PlanetPickerComboBox` — and route ~25 fields through them. Removes manual entry from ~75% of operator-typed inputs. Shared `TryParseObjAddr` + `Validation.ErrorTemplate` provide the safety net for power users who still want to type.

### Theme B — PHASE 2 PENDING surfacing has 4 inconsistent representations

B1 found 4 ways P2 status is shown (banner / GroupBox prefix / per-button badge / red border) and 5+ tabs where P2 buttons remain clickable. B2 documented several P2 helpers that have LIVE alternatives (FreezeAI → SuspendAiLua, MakeAllyLua → SetDiplomacy, etc.). **Combined fix**: (1) unify on per-button `Badge` TextBlock + `IsEnabled` converter to disable; (2) catalog rationale strings point operators to the LIVE alternative inline; (3) drop the duplicate yellow Warning banners. Result: operators never wonder "why did this button do nothing?"

### Theme C — Reusable controls are the architectural lever

B1 + B3 independently identified the SAME 5-7 reusable controls (`SlotPicker`, `UnitPicker`, `PlanetPicker`, `ReadFromGameButton`, `AutoRefreshControl`, `ObjAddrParser` helper, `TabFooterStatus`). **Combined fix**: extracting these takes ~3-5 dev-days. Once extracted, ~75% of the editor's typed-field tabs become 1-line replacements. The mod-authoring v1.1 tab (already in flight) can use them from day 1.

### Theme D — Test harness is the safety net for everything else

B4's full_editor_test_matrix.ps1 is the prerequisite for every UI/UX change in B1 and every deprecation in B2. Without the harness, refactoring the obj_addr parser risks silent breakage on tabs B1 didn't explicitly enumerate. **Sequence**: ship the harness (10 dev-days) BEFORE the major UI refactors so each refactor commit can be validated end-to-end.

### Theme E — Bridge stale-version-string is the trust-killing micro-bug

B2 found `SWFOC_GetVersion` banner says "37 live helpers" but 174 are catalogued LIVE. B4 noted this misleads operators reading the version banner. B1 didn't flag it specifically but related: tooltips citing iter-281 etc. are similar trust-erosion. **Combined fix**: 1-LoC bridge edit + tooltip cleanup pass. Cheapest single fix in the entire plan.

---

## Recommended Roadmap

| Release | Scope | Effort | Outcome |
|---|---|---|---|
| **v1.0.2 hotfix** (1-2 days) | • Bridge banner string fix (`"174 live helpers"`)<br>• Tooltip cleanup (drop iter-N + file:line refs from operator-visible strings)<br>• `FreezeAI` catalog rationale → cite `SuspendAiLua` LIVE alternative<br>• Mode-switcher window title binding (` — LIVE TRAINER` / ` — SAVEGAME EDITOR`) | Quick trust-restoration; no behavioral changes |
| **v1.1.0 minor** (2-3 weeks) | • Mod authoring tooling cluster A (already in flight)<br>• Hoist `TryParseObjAddr` to `SwfocTrainer.Core.Validation`<br>• Extract `SlotPickerComboBox`, `UnitPicker`, `PlanetPicker`, `AutoRefreshControl`, `TabFooterStatus`<br>• Replace ~25 typed-input fields with the new controls<br>• Bind PHASE 2 button `IsEnabled` to badge state<br>• Persist `IsAutoRefreshEnabled` to `_state` across sessions | ~75% reduction in operator-typed input; consistent PHASE 2 behavior; deprecate 4-6 legacy helpers from bridge registration |
| **v1.2.0 minor** (3-4 weeks) | • Build `bridge/full_editor_test_matrix.ps1` (10 dev-days)<br>• Add "Use selected" buttons to all SelectedObjAddr fields<br>• Add `address picker dialog` (modal Tactical Units DataGrid)<br>• Selection-bridge event bus for cross-tab autofill<br>• Cinematic tab consolidation (12 LIVE camera/director helpers)<br>• Ability Lab on UnitControl (`TriggerAbility` + `ListAbilities`) | 95% LIVE-row test coverage; cross-tab workflows feel cohesive |
| **v1.3.0 minor** (4-6 weeks) | • `SWFOC_TriggerVictory` LIVE flip (iter-450b + 450c)<br>• `SWFOC_SetGameSpeed` LIVE (1-2 iters)<br>• `SWFOC_SetIncomeMultiplier` per-slot LIVE (1 iter inside existing AddCredits hook)<br>• `SWFOC_FreeCam` LIVE via compose (1 iter)<br>• `SWFOC_GetPlanets` Day-1 fallback (4th category + hardcoded list) | 5 P2 entries flip to LIVE; PHASE 2 PENDING count drops from 18 → 13 |
| **v2.0.0 major** (6-8 weeks) | • Multi-iter RE arcs: `SetAreaDamage`, `SetTargetFilter`, `ToggleOHKAttackPower`, `SetHeroRespawnTimer` per-hero, `SetUnitCapOverride`<br>• `ListHeroes` LIVE via hero-detection-table RVA pin<br>• Tab reordering for workflow-first (Diagnostics → Player State → Tactical Units → Inspector → Unit Control → Combat → ...)<br>• Asset Browser → full Lua API browser with cataloged help | Operator-visible PHASE 2 count drops to ≤ 5; the remaining 5 are explicitly multi-session RE work with documented LIVE alternatives |

---

## Where to start

**Today (1-day investment, 80% trust restoration)**:
1. Bridge banner string fix → rebuild → redeploy → autonomous test verifies
2. Pass through `MainWindowV2.xaml` removing iter-N + file:line refs from operator-visible tooltips (~30 edits)
3. Bind window title to `IsLiveMode`
4. Mark FreezeAI catalog rationale to cite SuspendAiLua LIVE alternative

**This week (5-day investment, foundational lift)**:
5. Hoist `TryParseObjAddr` to shared validation
6. Extract `SlotPickerComboBox` + apply to 8 tabs
7. Bind PHASE 2 buttons' IsEnabled to badge state

**Next sprint (10-day investment, verification spine)**:
8. Build `bridge/full_editor_test_matrix.ps1` per B4's design
9. Run it against v1.1.0 candidate; gate release on 95% pass on LIVE rows
