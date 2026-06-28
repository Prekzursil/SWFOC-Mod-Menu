# Master Loop Capstone — Iter 100-221 (2026-04-23 → 2026-05-06)

Single-page operator-facing summary tying together the bridge LIVE wire goldmine + native UX surfacing arc + dispatcher matrix completion + architectural patterns + toolchain hardening + catalog discipline framework. **121 iterations across 14 days.**

## Executive summary

| Metric | Value | Notes |
|---|---|---|
| **LIVE wires shipped** | **142** | Bridge dispatchers iter 100-186 |
| **Dispatcher helpers** | **12** + **2 builders** | iter-178 matrix complete + iter-182/184/186 multi-arg + iter-202/203 builders |
| **Native UX buttons** | **109 across 9 tabs** | iter 188-219 surfacing arc |
| **Operator changelogs** | **4** | iter 187 + iter 196 + iter 216 + iter 220 |
| **Phase2HookPending audits** | **2** | iter 132 + iter 221 |
| **Catalog drift rate** | **0% iter-132 → iter-221** | 89-day window, +131% catalog growth, no silent flips |
| **Editor binary** | **157.25 MB** | Single-file Release publish, all features integrated |
| **Bridge harness** | **1100/0** | Stable since iter-186 |
| **Verifier lint** | **315/0/0** | Stable since iter-128 |

## Bridge LIVE wire goldmine (iter 100-186)

The iter 100-113 batch (17 wires) proved that engine APIs exposed via the Lua VM's userdata registry can be called via `DoString` — no MinHook detour, no RVA pin, no struct offset write. The pattern:

```cpp
// Bridge function:
int Lua_DispatchUnitMethod(const std::string& methodName, ...) {
    return doStringWrapper("return (unit_handle):" + methodName + "(args)");
}
```

This unlocked 142 LIVE wires across iter 100-186. Categories:

### Per-unit Lua-method dispatchers (~50 wires)
Hide / Prevent_AI_Usage / Set_Selectable / Despawn / Stop / Retreat (iter 111-112), Make_Invulnerable (iter 110), Teleport (iter 151), Attack_Target / Guard_Target / Divert (iter 163), Heal / Take_Damage / Set_Damage_Modifier / Set_Rate_Of_Fire_Modifier (iter 154), 6-wire mega-batch (iter 157), 4-wire extension (iter 156), bool batch (iter 153), etc.

### Per-player Lua-method dispatchers (~12 wires)
Give_Money / Set_Tech_Level / Unlock_Tech (iter 155), Lock_Tech / Make_Ally / Make_Enemy (iter 161), Enable_As_Actor / Release_Credits / Select_Object (iter 164), Disable_Orbital_Bombardment (iter 160), GLOBAL Make_Ally/Make_Enemy (iter 182).

### Camera primitives (8 wires)
Scroll_Camera_To (iter 107), Camera_To_Follow (iter 143), Rotate_Camera_To/By (iter 144/165), cinematic camera quad (iter 145), Fade_Screen_In/Out (iter 162/165), Zoom_Camera (iter 162), Point_Camera_At (iter 165).

### Cinematic helpers (10 wires)
Letter_Box_On/Off (iter 150), Lock_Controls/Unlock_Controls (iter 160/180), Suspend_AI (iter 162), Story_Event/Add_Objective/Play_Music/Play_SFX_Event (iter 159), Stop_All_Music/Resume_Mode_Based_Music/Story_Event_Trigger (iter 166), SFXManager.Allow_Unit_Reponse_VO (iter 181, typo preserved), Hide/Show_GUI_Object (iter 158/166), Disable_Bombing_Run/Flash_GUI_Object (iter 158).

### Read-side getters (~30 wires)
Get_Hull/Health/Shield (iter 167), Has_Attack_Target/Are_Engines_Online/Get_Owner (iter 168), Get_Type/Credits/Faction/Tech_Level (iter 169), Get_Name/Is_Stealthed/Is_In_Limbo/Is_Capturable (iter 170), Get_Position/Parent_Object/Attack_Target/Damage_Modifier (iter 171), Get_Garrison_Units/Contained_Object_Count/Behavior_ID/Rate_Of_Fire_Modifier (iter 172), Is_Ability_Active/Has_Property/Is_Category/Get_Distance (iter 173), Get_Bone_Position/Contains_Object_Type/Get_Space_Station_Level/Get_Type_Of_Unit (iter 174), Is_Enemy/Is_Ally (iter 179), Thread.Get_Current_Stage (iter 181), Get_Game_Mode/Get_Local_Player/Get_Seconds_Per_Game_Minute (iter 178).

### TaskForce wires (9 wires — full sub-class arc)
Get_Type_Of_Unit / Move_To / Reinforce / Release_Reinforcements / Launch_Units (iter 174-175), Attack_Target / Guard_Target / Land_Units / Set_As_Goal_System_Removable (iter 176), Move_To_Target (iter 179).

### Discovery / engine handles (~7 wires)
Find_Object_Type / FindPlanet / Find_First_Object (iter 177), Find_All_Objects_Of_Type (iter 179), Find_Nearest (iter 186), FOWManager.Reveal_All / Undo_Reveal_All / Reveal (iter 180/184).

### Spawn variants (5 wires)
Spawn_Unit (iter 109), Reinforce_Unit / Spawn_From_Reinforcement_Pool / Create_Generic_Object (iter 185 — param-order GOTCHA), Galactic_Spawn (iter 152).

### Combat scalars (4 wires LIVE — non-Lua)
Take_Damage_Outer detour for global damage multiplier (iter 96), SetSpeedOverride for per-unit speed (iter 100), SetPerFactionSpeedMultiplier (iter 100), SetUnitShield via SetFrontShield+SetRearShield (iter 129).

### Dispatcher composition (1 wire)
CallObjMethodLua — universal escape hatch dispatcher (iter 113).

## Dispatcher matrix completion (iter 178)

The 9-helper matrix covers the canonical dispatch shapes:

| Receiver | 0-arg | 1-arg | Read |
|---|---|---|---|
| **(unit/player/etc)** | iter-112 NoArgCall | iter-111 BoolMethod / iter-154 GenericMethod | iter-167 GetterNoArg / iter-173 GetterArg |
| **global** (no receiver) | iter-166 NoArgMethod | iter-158 ArgMethod | iter-178 GlobalGetterNoArg / iter-177 GlobalGetterArg |

Multi-arg expansion via iter-182 (Arg2Method, 2-arg globals), iter-184 (Arg3Method, 3-arg globals), iter-186 (Getter3Arg, 3-arg getters).

**Marginal cost after iter-178**: ~3 LoC of bridge per new wire. New wires only need new helpers if they require multi-arg variants beyond {0,1,2,3} args.

## Native UX surfacing arc (iter 188-219, 32 iters, 109 buttons)

The bridge LIVE wires were initially accessible only through the Lua Playground preset menu (iter-183, 83 entries). Iter 188-219 surfaced them as native buttons across 9 editor tabs:

| Tab | Buttons | Iter range |
|---|---|---|
| **UnitControl** | 34 | iter 117/118/188/194/211/212/213/218 |
| **PlayerState** | 16 | iter 189/199/209/210/217 |
| **Inspector** | 18 | iter 191/197/198/214 |
| **Galactic** | 16 | iter 108/200/215/218 |
| **WorldState** | 12 | iter 201/202/204/208 |
| **Camera & Debug** | 10 | iter 148/149/192 |
| **Combat** | 9 | iter 193/219 |
| **Spawning** | 8 | iter 119/195/203/206 |
| **Diagnostics** | 6 | iter 190/205/207 |

**Iter-216 surfacing queue closed at iter 219.** Every wire from the iter-216 changelog "What's NOT yet surfaced" list now has a native button. Subsequent iters need new direction (RTTI dissection arcs / multi-iter projects).

## Architectural patterns established

### 1. Helper shape-agnosticism
`BuildUnitLuaMethodCall("SWFOC_X", a, b)` works for unit-method, player-method, TaskForce-method, AND global-form 2-arg wires. The helper doesn't care about receiver shape — bridge function handles dispatch. (Proven iter 178 matrix completion + iter 214 cross-receiver + iter 217 GLOBAL-form.)

### 2. Namespace-agnostic dispatch
Dotted method names like `FOWManager.Reveal_All` work transparently through iter-158 / iter-178 helpers. The `.` is part of Lua's method-name lookup, not a separator the helper has to know about. (Discovered iter 180, extended iter 181.)

### 3. iter-204 hardcoded-bool on/off lineage (7 iters deep)
Bool-arg wires get TWO buttons (on/off) with hardcoded args ("1"/"0") instead of one button + bool TextBox. Lineage: 204 → 208 → 211 → 212 → 213 → 215 → 217. Catalog rationale references "iter-204" + "on/off" so future readers can grep the chain.

### 4. Field-reuse compounding
PlayerLuaExpr threaded through 16 PlayerState buttons (iter 189-217). SelectedUnitLuaExpr threaded through 33+ UnitControl buttons (iter 117-218). TaskForceLuaExpr threaded through 9+ Galactic buttons (iter 215-218). Each iter that reuses an existing field amplifies its semantic load.

### 5. Reverse-orphan regex-invisibility
`BuildUnitLuaMethodCall("SWFOC_X", a, b)` keeps entries in the `KnownUnwiredEntries` snapshot (with iter-N NOTE comments) — string-literal form is regex-invisible. `$"return SWFOC_X({a}, {b})"` drops entries — interpolated form is regex-visible. Conscious choice between forms is part of every surfacing iter's discipline.

### 6. Capability surface env-var regen routine
Every iter touching CapabilityStatusCatalog rationale fields generates capability surface markdown drift. Routine fix: re-run filtered tests with `SWFOC_REGEN_CAPABILITY_SURFACE=1`. Iter 219 integrated this into the filtered test run for single-pass clean runs.

### 7. A/B-test cross-tab batches
Iter 218 demonstrated pairing two unrelated wires into one iter when each closes a distinct A/B-test loop. UnitControl Corrupt vs iter-212 Bribe (Underworld signature ability comparison); Galactic Move_To_Target vs iter-215 Move_To (object-vs-position comparison). Operator-facing experimentation surface, not redundancy.

### 8. Queue-closure as iter direction
The iter-216 changelog's "What's NOT yet surfaced" section listed 7 remaining wires; iter 217-219 closed them in order (3 + 2 + 1 + 1). Docs supplement isn't just retrospective — it's prescriptive for the next 3-7 iters.

## Toolchain hardening

### Iter 172: tee-grep-line-buffered + blame-hang-timeout
Caught a 7-minute silent hang in `dotnet test ... | tail -3` (output file stayed 0 bytes; testhost never spawned). Replacement pattern:
```bash
dotnet test ... 2>&1 | tee log | grep --line-buffered -E "Passed|Failed|FAIL" | head -50
# + --blame-hang-timeout 5m on the dotnet test invocation
```
Future runs surface progress immediately. Pinned to memory: `feedback_dotnet_test_hang_diagnosis.md`.

### Iter 219: single-pass clean run integration
Combined `SWFOC_REGEN_CAPABILITY_SURFACE=1` env var with the filtered test run so capability surface markdown drift fixes happen in the SAME pass. Saves 30-60 seconds per iter on small surfacing iters.

## Catalog discipline framework

The iter 128/130/131/132 audit pattern (re-audit-via-callgraph-CLI) established a drift-detection framework. iter-132 first audit triaged 24 PHASE 2 PENDING entries with 60% drift rate. iter-221 re-audit (89 days later, +121 catalog entries) found **0 drift catches** — the framework works:

- **Explicit Phase2HookPending markers** (Status field in catalog)
- **iter-N rationale stamps** (every entry's Note text references the iter that classified it)
- **Cross-iter audit passes** (iter 132 → iter 221, regular cadence at major catalog growth checkpoints)
- **Pin tests as audit signatures** (iter-221 5-test file enforces the audit's findings as regression guards)

## Cinematic workflow chain (38-iter end-to-end arc)

The longest single operator-facing arc in the master loop. Assembled iter-by-iter:

| Iter | Date | Component |
|---|---|---|
| 145 | 2026-04-29 | Cinematic camera quad (Start/End + Set_Key/Transition_Key) |
| 150 | 2026-04-30 | Letter_Box_On/Off |
| 201 | 2026-05-05 | Play_Music + Story_Event + Add_Objective + Play_SFX_Event |
| 202 | 2026-05-05 | Stop_All_Music + Resume_Mode_Based_Music + Story_Event_Trigger |
| 208 | 2026-05-06 | Lock_Controls / Unlock_Controls |
| 219 | 2026-05-06 | Suspend_AI |

After iter 219, the 7-button operator workflow is: **Lock_Controls(true) → Suspend_AI(N seconds) → cinematic camera quad → letterbox → music → Resume_Mode_Based_Music + Unlock_Controls()**. No Lua Playground required.

## Operator quick-reference

For LIVE wires: click native button on appropriate tab (109 buttons across 9 tabs).
For unsurfaced wires (~33 remaining): Lua Playground preset menu (iter 183, 83 entries) covers everything else.
For Phase 2 PENDING wires (26 entries, audit pin tests guard against drift): use Phase-1 mirror or wait for engine-side RE work.

## Knowledge-base index

| Doc | Coverage |
|---|---|
| `verified_facts.json` | 315 RVA entries (engine ledger, source of truth) |
| `VERIFIED_RVAS.md` | Auto-generated human-readable RVA reference |
| `alamo_engine_reference.md` | Engine architecture overview |
| `HISTORY.md` | Chronological session handoff summary |
| `iter132_phase2_pending_audit.md` | First Phase2HookPending audit (60% drift rate) |
| `iter221_phase2_pending_audit.md` | Re-audit (0% drift rate, 89-day window) |
| `ralph_loop_changelog_2026-04-27.md` | Iter 1-48 changelog |
| `ralph_loop_changelog_2026-04-28.md` | Iter 100-113 LIVE flips changelog |
| `ralph_loop_changelog_2026-04-29.md` | Iter 117-145 changelog |
| `ralph_loop_changelog_2026-05-05.md` | Iter 146-186 changelog (250 lines) |
| `ralph_loop_changelog_2026-05-05_supplement.md` | Iter 188-195 native UX surfacing |
| `ralph_loop_changelog_2026-05-06.md` | Iter 197-215 + 100-button milestone (269 lines) |
| `ralph_loop_changelog_2026-05-06_supplement.md` | Iter 217-219 queue-closure |
| `master_loop_capstone_iter_100-221.md` | This document — iter 100-221 single-page summary |

## Iter 222 direction (this iter)

Pure docs iter. Created this capstone document. Sets up iter 223+ direction:
- **Option A**: Multi-iter A1.x RTTI dissection arc (SetFireRate global / SetUnitField 10 fields / SetCameraPos per-coord / SetUnitCapOverride). Estimate: 5-15 iters per arc.
- **Option B**: Multi-iter Thread B Overlay Phase 2-full ImGui vendoring (~500 LoC, ~15 files). Or Thread C save-game RE.
- **Option C**: Operator-facing polish — bridge harness expansion (backfill iter 132-186 wires, ~5 iters) or replay harness expansion (SimulatorSmokeRun parallel suite, multi-iter).
