# Ralph Loop Consolidated Backlog (P0–P7) — 2026-04-23

Source: user-provided P0–P7 brief, cross-referenced against the live task
queue, the 2026-04-23 handoff, and the Ghidra decompilation plan. Every row
below is either **already tracked** (existing task id cited), **newly
created** this session (new task id cited), or **deferred** with a concrete
unblock condition. No silent drops.

## Legend

| Column | Meaning |
|---|---|
| **Feature** | One-line summary of the capability |
| **Tab/Area** | Which V2 tab or service area the feature lives in |
| **Exec** | Lua / Memory / Hook-AOB / UI — which layer does the work |
| **Deps** | Services or RVAs the feature depends on |
| **Status** | TODO / IN-PROGRESS / DONE / BLOCKED / DEFERRED |
| **Blocker** | If BLOCKED — the specific unblock requirement |
| **Validate** | UnitTest / Harness / Replay / LiveGame |
| **Task #** | Task queue id |
| **Done** | Concrete signal that closes the task |

The canonical hard rules from `.claude/ralph-loop.local.md` apply to every
row: enemy units stay READ-ONLY, every mutation ships with a red/green
regression pair, the bridge harness pass count never decreases, the ledger
lints 0/0, byte-flipping is never a substitute for engine-state management,
IDA Hex-Rays `qword_XXX` is implicit-deref.

---

## P0 — Foundation / Unblockers

The architecture layer cake (Lua → Memory → Hook-AOB) must survive every
future refactor. Do NOT collapse to "Lua-only" — each layer exists because
the layers below and above do not cover its gap.

| Feature | Tab/Area | Exec | Deps | Status | Blocker | Validate | Task # | Done |
|---|---|---|---|---|---|---|---|---|
| Wire ActionExecutionRequest end-to-end (Orchestrator→IBackendRouter→NamedPipeExtenderBackend→Extender.Host) | Core pipeline | C# + pipe | `TrainerOrchestrator.ExecuteAsync`, `NamedPipeExtenderBackend` | TODO | — | UnitTest + Replay + LiveGame | **#118** | Every V2 button that previously returned `symbol:invalid` round-trips OK via the bridge |
| Audit existing BuildLuaCommand services — stub vs real | Core services | C# | `SWFOC editor/src/SwfocTrainer.Core/Services/*` | TODO | — | UnitTest table per service | **#119** | Every service has either a real command or an explicit BLOCKED row in the backlog |
| Editor consumes RE artifacts (KB JSON + signatures + bridge) without mixing RE into the UI repo | Cross-cutting | Docs + build | `knowledge-base/verified_facts.json`, `rvas.h` | TODO | — | UnitTest load-time check | **#120** | Editor imports a typed schema wrapper over `verified_facts.json`; mismatch fails the test suite |
| Restore memory + hook/AOB layer as first-class in the service registry | Core pipeline | C# + bridge C++ | `IBackendRouter`, `MemoryReaderService`, `HookAobService` | TODO | — | UnitTest routing table | **#121** | Every service declares ExecutionPath=Lua/Memory/Hook; router validates on startup |

---

## P1 — Core gameplay (must be real, not placeholders)

### Economy

| Feature | Tab/Area | Exec | Deps | Status | Blocker | Validate | Task # | Done |
|---|---|---|---|---|---|---|---|---|
| Get/Set Credits per player+faction | Economy | Lua + Memory | `SWFOC_SetCreditsForSlot`, `SWFOC_GetCreditsForSlot` | DONE | — | Harness + LiveGame | existing 37 helpers | Both helpers registered + round-trip verified 2026-04-10 |
| Freeze Credits (hold value on every tick) | Economy | Hook | New `Detour_TickApplyCreditsDelta` | TODO | IDA xref on credit-write function | Harness + LiveGame | **#122** | Hook installed, credits hold across 60 s with game running |
| Max Credits Uncap | Economy | Memory | `SWFOC_UncapCredits` | DONE | — | LiveGame | existing | User verified UncapCredits live 2026-04-10 (680k→1B) |
| Income multiplier | Economy | Hook | IDA-find income calc function | TODO | IDA xref on per-tick income | Harness + LiveGame | **#123** | `SWFOC_SetIncomeMultiplier(slot,x)` scales tick income x× in a live skirmish |
| Production / build-speed multiplier | Economy / Battle | Hook | IDA-find production tick | TODO | IDA xref on per-tick build progress | Harness + LiveGame | **#124** | Build queue progress advances x× per tick |

### Tech

| Feature | Tab/Area | Exec | Deps | Status | Blocker | Validate | Task # | Done |
|---|---|---|---|---|---|---|---|---|
| Get/Set Tech Level per player | Economy | Lua | `SWFOC_SetTechForSlot`, `SWFOC_GetTechForSlot` | DONE | — | Harness | existing | Round-trip passes in bridge harness |

### Speed

| Feature | Tab/Area | Exec | Deps | Status | Blocker | Validate | Task # | Done |
|---|---|---|---|---|---|---|---|---|
| Selected-unit speed (locomotor-based) — REAL | Speed / Inspector | Memory | Locomotor offset (`obj+0xA8 → +0x2A0`, see re-findings) | TODO | confirm locomotor+0x2A0 write-shape via IDA | Harness + Replay + LiveGame | **#125** | Editing Speed field on a selected unit changes its move speed in-game |
| Per-faction speed multiplier | Speed | Hook | IDA-find tick-rate or per-faction scalar | TODO | IDA xref | Harness + LiveGame | **#126** | Faction N units move x× speed |
| Global game-speed slider (0.1×–20×) | Speed / Battle | Lua or Memory | Existing `SimulationRate` global (re-findings) | TODO | confirm setter | Harness + LiveGame | **#127** | Slider from 0.1× to 20× scales entire engine tick rate |

### Combat

| Feature | Tab/Area | Exec | Deps | Status | Blocker | Validate | Task # | Done |
|---|---|---|---|---|---|---|---|---|
| God Mode (tactical, via engine invulnerability path) | Combat / Unit Control | Hook + Lua | #99 hardpoint path + #100 damage hook | IN-PROGRESS | — | Harness + LiveGame | **#106** | Local units survive 60 s of enemy fire with all hardpoints intact |
| One-Hit Kill (enemy HP→0 on first hit) | Combat | Hook | `Detour_SetHP` OHK branch | TODO | — | Harness + LiveGame | **#105** | Any enemy dies to one shot from a local unit |
| Combined God+OHK | Combat | Hook | #105 + #106 flags non-exclusive | TODO | — | LiveGame | **#128** | Toggling both simultaneously keeps local immortal + enemy one-shottable |
| Damage multiplier (per-faction or global) | Combat | Hook | `Take_Damage_Outer @ 0x38A350` | TODO | ready (RVA in combat_system.json) | Harness + Replay + LiveGame | **#129** | Multiplier 2× doubles observable damage-per-tick in event stream |
| Shield edit (selected unit) | Combat / Inspector | Memory | `GameObj` shield field offset | TODO | IDA-confirm shield field offset | Harness + LiveGame | **#130** | `SetUnitShield(obj, x)` round-trips through `InspectUnit` |
| Weapon fire-rate multiplier | Combat | Hook | IDA-find fire-cooldown reset | TODO | IDA xref | LiveGame | **#131** | Selected unit fires x× frequently |
| Area-damage toggle | Combat | Hook | IDA-find splash function | TODO | IDA xref | LiveGame | **#132** | Toggling on makes every shot splash; off reverts |
| Target filtering (friendly-fire, neutral, enemy-only) | Combat | Hook | IDA-find target-filter predicate | TODO | IDA xref | LiveGame | **#133** | Local units stop firing on non-selected categories |

### Hero system

| Feature | Tab/Area | Exec | Deps | Status | Blocker | Validate | Task # | Done |
|---|---|---|---|---|---|---|---|---|
| Hero list + status (alive/dead/respawning) | Hero Lab | Lua + Memory | `SWFOC_HeroInstantRespawn` exists; need ListHeroes | TODO | IDA-find hero roster iteration | Harness + Replay + LiveGame | **#134** | V2 Hero Lab tab shows heroes with live status |
| Global respawn-timer slider | Hero Lab | Memory | Hero respawn timer offset | TODO | IDA-confirm | Harness + LiveGame | **#135** | Slider 0–600 s updates timer per hero |
| Permadeath toggle | Hero Lab | Memory | Respawn-enable flag | TODO | IDA-confirm | LiveGame | **#136** | Toggle ON prevents respawn after death |
| Per-hero kill/revive | Hero Lab | Lua + Memory | existing SetHP hook | TODO | — | LiveGame | **#137** | Kill/revive buttons work per row |
| Hero stat editing (HP/damage/speed) | Hero Lab | Memory | GameObj offsets | TODO | reuse #130/#125 | Harness + LiveGame | **#138** | Editing fields persists until next tick |

### Abilities

| Feature | Tab/Area | Exec | Deps | Status | Blocker | Validate | Task # | Done |
|---|---|---|---|---|---|---|---|---|
| Ability catalog (SpecialAbility hierarchy) | Abilities | Lua | `Find_All_Objects_Of_Type` + ability RTTI | TODO | IDA-find ability vtable chain | Harness + Replay | **#139** | ListAbilities returns >20 rows in a skirmish |
| Trigger ability / activation | Abilities | Lua | `unit:Trigger_Ability(idx)` signature | TODO | confirm signature via GAME_LUA_API.md | LiveGame | **#140** | Button fires ability on selected unit |

### Galactic

| Feature | Tab/Area | Exec | Deps | Status | Blocker | Validate | Task # | Done |
|---|---|---|---|---|---|---|---|---|
| Planet list + owner display | Galactic | Lua + Memory | planet roster iteration | TODO | IDA-find planet array | Harness + Replay + LiveGame | **#141** | V2 Galactic tab shows planet→owner table |
| Change planet owner | Galactic | Lua | `Planet:Change_Owner(slot)` | TODO | confirm API | LiveGame | **#142** | Changing owner in UI flips planet color |
| Planet tech checks / building ownership | Galactic | Lua | existing ServiceAPI | TODO | — | Replay | **#143** | Querying building ownership returns consistent slot |
| Diplomacy editor (war/neutral/allied) | Galactic | Lua + Memory | `ReplaySetDiplomacy` exists as replay mutation | TODO | live helper for diplomacy | Harness + LiveGame | **#144** | Setting allied between two slots suppresses hostile fire |
| Reveal-all / fog toggle (galactic + tactical) | Galactic / Battle | Lua + Memory | FOWManager.Reveal_All | TODO | — | LiveGame | **#113** | Fog clears on tactical + galactic |
| Switch sides / faction switch (multi-field) | Galactic | Lua | `SWFOC_SetHumanPlayer_v2` | DONE | — | LiveGame | existing | User verified SwitchFaction live 2026-04-10 |

---

## P2 — Required Tabs (UI)

Every tab owes a V2 XAML+VM pair. The blocker for all tabs is the same:
Task #118 (wire ActionExecutionRequest end-to-end) must land first, otherwise
the buttons are decorative.

| Tab | Content | Status | Task # | Done |
|---|---|---|---|---|
| 1 Economy | credits set/freeze, income display, per-faction overview, tech edit, uncap, income×, build-speed× | TODO | **#145** | Every control actually mutates state, verified via V2 probe sweep |
| 2 Combat | god mode, OHK, combined, damage×, shield edit, fire-rate×, area-damage, target filter | TODO | **#146** | Controls wired to Tasks 105/106/128–133 |
| 3 Speed | global slider, per-faction×, selected-unit speed | TODO | **#147** | Wired to 125–127 |
| 4 Inspector | **no manual pointer entry** — live selected unit, 500 ms refresh, HP/shield/max, abilities, garrison, veterancy, position, invuln, hardpoints, export | TODO | **#148** | Opening Inspector on a fresh selection populates within 1 s |
| 5 Spawning | searchable mod-aware type browser, faction, count 1–100, pos (at selected / camera / coords), reinforcement mode, presets | TODO | **#149** | Spawns 10 fighters at camera in a live game |
| 6 Galactic Map | planet list, owner change, reveal-all, give money, diplomacy, switch sides | TODO | **#150** | Wired to 141–144 + existing switch sides |
| 7 Hero Lab | roster/status, respawn ctrl, permadeath, kill/revive, stat edit | TODO | **#151** | Wired to 134–138 |
| 8 Battle Control | auto-win, instant-build, free-build, unit-cap override, freeze AI, kill enemies, heal allies, no-fog, build+game speed | TODO | **#152** | Wired to 114 + 124 + 127 + 113 + new killall/healall |
| 9 Story Events | flag browser, set flag, fire event dropdown, reward buttons (credits/reveal/max-tech/spawn-hero) | TODO | **#153** | Event dropdown populated from re-findings/story_system.json |
| 10 Camera Debug | pos/zoom/rot display, free-cam, teleport, cam speed, bridge status+last result, advanced raw-command input | TODO | **#154** | Wired to 115 + existing diagnostics |

---

## P3 — UX rules (cross-cutting)

Single task enforcing these across every V2 control added this quarter.

| Feature | Status | Task # | Done |
|---|---|---|---|
| Tooltip on every control | TODO | **#155** | V2 audit script finds no tooltip-less button/checkbox/slider/dropdown |
| Visible success + failure feedback | TODO | **#155** | Every command binds both success and error templates |
| Persist toggle/slider states across sessions | TODO | **#155** | `V2Settings` serialises every ObservableProperty |
| Cleanup/restore on disable (where reversible) | TODO | **#155** | Toggling a feature off reverts the underlying state |
| No manual obj_addr entry in core flows | TODO | **#155** | Inspector/UnitControl/Hero always accept a live selection |

---

## P4 — Bridge helper surface

| Helper | Status | Task # | Notes |
|---|---|---|---|
| `SWFOC_GetAllPlayers` | TODO | **#111** | — |
| `SWFOC_GetPlayerWrapper(slot)` | TODO | **#156** | Needs `PlayerWrapper_Create @ 0x6019F0` verification |
| `SWFOC_GetSelectedUnit` | DONE | existing | — |
| `SWFOC_GetUnitInfo(ptr)` | DONE | existing `SWFOC_InspectUnit` | alias helper optional |
| `SWFOC_SetUnitField(ptr, field, value)` | TODO | **#157** | Generic setter over a field→offset table built from `gameobject_complete.json` |
| `SWFOC_EnumerateUnits(faction)` | TODO | **#158** | Wraps `SWFOC_ListTacticalUnits` + faction filter |
| `SWFOC_SpawnUnit(type, faction, x, y, z, count)` | TODO | **#159** | Needs `PlayerWrapper:Spawn_Unit` (re-findings) |
| `SWFOC_GetPlanets` / `SWFOC_ChangePlanetOwner` | TODO | **#141 / #142** | — |
| `SWFOC_RevealAll(slot, enable)` | TODO | **#113** | — |
| `SWFOC_SetDiplomacy(a,b,state)` | TODO | **#144** | — |
| `SWFOC_FreezeAI(slot, enable)` | TODO | **#114** | — |
| `SWFOC_SetBuildSpeed(slot, x)` | TODO | **#124** | — |
| `SWFOC_SetBuildCost(slot, x)` | TODO | **#160** | `x=0` = free build |
| Damage-multiplier + combat-control hooks | TODO | **#129**/131/132/133 | — |

---

## P5 — Memory / Hook / AOB features (Lua not enough)

| Feature | Status | Task # | Notes |
|---|---|---|---|
| CE-style god mode + OHK via code-cave/hook fallback | IN-PROGRESS | **#106** / **#105** | — |
| Instant build AOB patch | TODO | **#161** | — |
| Free build AOB patch | TODO | **#162** | `cost = 0` NOP or hook |
| Unit-cap override | TODO | **#163** | IDA-find unit cap check |
| Maphack / no-fog via AOB/patch | TODO | **#113** | same task as Lua path — ship whichever lands first |
| Hardpoint-aware tooling: enumerate + protect | DONE | existing `SWFOC_GetHardpoints`, Task #99 invuln | — |
| Damage/death event capture via native hooks | IN-PROGRESS | **#112** | — |

---

## P6 — Modder IDE power tools

| Feature | Status | Task # | Notes |
|---|---|---|---|
| Lua Script Playground (AvalonEdit panel) | TODO | **#110** | — |
| Engine Event Stream view | TODO | **#112** (bridge) + **#164** (V2 view) | Bridge first, then UI |
| Click-to-select inspector (property grid, editable) | TODO | **#148** | Part of Inspector tab |
| After-action report generator | TODO | **#165** | Consumes event stream |
| Save File Validator / Repair / Save Lab | TODO | **#166** | Uses `re-findings/save_format.json` |
| XML vs memory diff view | TODO | **#167** | Uses PetroglyphTools MEG parser |
| Director mode / cinematic tools | TODO | **#115** (camera) + **#168** (UI: paths/hide-UI/slow-mo/freeze) | — |
| Conditional trigger / rule engine (IFTTT) | TODO | **#169** | Runs on event stream |

---

## P7 — Deferred (do not lose)

| Feature | Status | Task # | Unblock condition |
|---|---|---|---|
| Cross-Faction Recruitment | DEFERRED | **#170** | Needs unit-ownership transfer primitive from #157 |
| Unit Stat Editor | DEFERRED | **#171** | Needs Inspector tab (#148) |
| Orbital Toggle | DEFERRED | **#172** | IDA-find orbital-phase flag |
| Save File Surgery | DEFERRED | **#166** supersedes | Validator lands first |
| Music Panel | DEFERRED | **#173** | Needs sound-system hooks |
| Lua Script Workshop (AvalonEdit) | DEFERRED / merge into **#110** | — | Part of Playground tab |
| Veterancy Manager | DEFERRED | **#174** | Needs veterancy field RE (not yet in re-findings) |
| Map Hints | DEFERRED | **#175** | Needs map-hint sprite system |

---

## Gap report (Implemented vs Partial vs Stubbed vs Blocked)

| State | Count | Notable items |
|---|---|---|
| **Implemented (green-end-to-end)** | 12 | GetLocalPlayer, SetCredits, UncapCredits, SetTechLevel, SwitchFaction v2, SetUnitInvuln via hardpoint, Make_Invulnerable replay, ListTacticalUnits replay+live, focus-drain timer, selection two-deref fix, DiagSelection, fixture library seeded |
| **Partial (bridge done, UI/LiveGame pending)** | 7 | SetUnitHull (lands + clamps), PreventUnitDeath (byte-only), GetHardpoints, GetSelectedUnit(s), DiagListRegisteredFunctions, DiagPipeStats, DiagGameTick |
| **Stubbed (UI present, helper missing)** | 6 | V2 Speed (no real service), V2 Spawning (no ListUnitTypes), V2 Inspector (no auto-select), V2 Galactic (no planet iter), V2 Hero Lab (no roster), V2 Story Events (no flag reader) |
| **Blocked (needs IDA research)** | 10 | Freeze Credits, Income multiplier, Build speed, Fire-rate, Area-damage, Target filter, Free-cam, Damage multiplier per-faction, FreezeAI, Planet owner flip |
| **Deferred (explicit unblock listed)** | 7 | Cross-faction recruitment, Unit Stat Editor, Orbital toggle, Music Panel, Veterancy Manager, Map Hints, Save File Surgery |

The one number that matters: **bridge harness must stay ≥414/0; ledger lint
must stay 0 errors / 0 warnings**. Any PR that regresses either is rejected.

---

## Recommended execution order — "fully usable in live gameplay" first

1. **#118 Wire ActionExecutionRequest end-to-end** — without this every V2 tab is decorative
2. **#119 Audit BuildLuaCommand services** — kills zombie services before they fool future sessions
3. **#121 Restore memory + hook/AOB layer** — ensures per-feature decisions can pick the right layer
4. **#106 God Mode integration via #99 hardpoint path** — the headline feature, foundation for combat demos
5. **#105 OHK attack-power toggle** — pairs with #106 to make tactical demos satisfying
6. **#125 Selected-unit speed (locomotor)** — restores the v1 CE trainer's most-used feature
7. **#148 Inspector tab (no manual pointer entry)** — makes every subsequent feature discoverable
8. **#141/#142 Planet list + change owner** — unblocks galactic-side demos
9. **#134 Hero list + status** — unblocks the Hero Lab tab
10. **#113 RevealAll + #114 FreezeAI** — enable cinematic/director workflows
11. **#112 Event Stream** — foundation for DPS log, after-action, IFTTT
12. **#110 Lua Playground** — once everything above works, give power users a scratchpad
13. **#165 After-action report + #168 Director mode UI** — content-creator facing features
14. **#169 IFTTT rules** — once event stream is live
15. P7 deferred items as individual asks

Any deviation from this order requires updating the rationale here, not
silently re-ordering in the task queue.

---

## Hard rules carried forward (from ralph-loop.local.md)

- Enemy units stay READ-ONLY (every write helper checks `IsObjOwnedByHuman`).
- No DLL redeploy while the game is running — queue deploys, one deploy per checklist.
- Q9 backup SHA `fc2c104b9d1012ac42df5fd830c2297d45e1c8bfffd42441cbe1f36ce4046e84` stays intact.
- Every mutation ships with a red/green regression pair in the replay or bridge harness.
- `python -m verifier lint` from `tools/` stays 0 errors / 0 warnings.
- Bridge harness pass count never decreases (currently **414/0**).
- `smoke_test_replay.py` stays 12/12; `smoke_test_replay_units.py` stays 34/34.
- Byte-flipping is not engine-state-management. Route writes through engine functions.
- IDA Hex-Rays `qword_XXX` is implicit-deref; `*(qword_XXX + N)` is two dereferences.

---

## Task-id map (new this turn)

The backlog above references task ids **#118–#175**. They are created via
`TaskCreate` in the same session this doc was authored. Any id mentioned
here that does not yet exist in the queue is a drift bug — log it in
`.remember/now.md` and file immediately.

---

## IDA string-search discoveries (2026-04-23 iteration 3)

Batched `ida-pro-mcp.find` with 15 string targets. Hits (reported as
image-base-added addresses, the form IDA MCP expects — subtract
`0x140000000` to get the RVA used in `rvas.h` and `verified_facts.json`):

| String | Hits | Addresses (IDA-format) | Unblocks |
|---|---|---|---|
| `Damage_Multiplier` | 5 | 0x140882f93, 0x140882fb4, 0x140886d7b, 0x14088f178, 0x1408cc90c | **#129** SetDamageMultiplier |
| `Reveal_All` | 1 | 0x1408bade0 | **#113** RevealAll |
| `FogOfWar` | 6 | 0x14081b55f, 0x14088406e, 0x140884086, 0x14088409e, 0x140898e68, 0x1408ff05d | **#113** RevealAll |
| `Give_Money` | 3 | 0x1408aa358, 0x1408aa5b3, 0x1408aa5f3 | **#122** FreezeCredits (hook-after target) |

Empty-hit queries (strings absent from binary — feature-name derivation
needs a different approach, likely: find the relevant vtable slot or a
different English string):

- `attack_power` / `Attack_Power` / `AttackPower` → #105 needs a different
  hunt: likely walk the weapon-stat struct reachable from GameObject, or
  IDA-find the weapon-damage calc function and look at what field it
  multiplies by. **Next iteration plan**: `xrefs_to` on `Take_Damage_Outer
  @ 0x38A350` and look for multiplications of GameObject field reads.
- `Fire_Cooldown` → #131 needs the same approach: find the weapon-tick
  function.
- `Build_Progress` → #124 needs the per-tick build-progress function;
  likely unnamed in the binary. Start from `Production_System` strings.
- `Orbital_Phase` / `is_orbital` → #172 is DEFERRED with a concrete unblock
  condition (find a different string anchor).
- `Veterancy` → #174 DEFERRED (same).
- `Freeze_AI` / `Disable_AI` → #114 needs AI-tick function hunt; start
  from `re-findings/ai_system.json` if those notes cite function names.
- `Apply_Credits` → #122 pivots to use `Give_Money` (found) as the anchor.

**Next iteration first action**: `xrefs_to` on each of the 4 usable
anchor addresses to map the enclosing functions; decompile the ones whose
enclosing function name looks feature-relevant; ledger the new RVAs with
2-tool consensus (`ida_pro` + the string evidence = sufficient once the
string literal is inside the function body).
