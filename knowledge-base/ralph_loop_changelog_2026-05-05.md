# Ralph Loop Changelog — 2026-05-05 (covers iter 146-186)

This changelog documents the master ralph loop work shipped between
iter 146 (operator changelog through iter 145) and iter 186 (current).
**41 iters of work, +118 LIVE wires, +8 dispatcher helpers, multiple
architectural findings.** Predecessor changelog:
`ralph_loop_changelog_2026-04-29.md` (covers iter 100-145).

---

## Headline numbers

| Metric | iter 145 close | iter 186 close | Δ |
|---|---|---|---|
| LIVE wires | 29 | **142** | +113 |
| Dispatcher helpers | 4 | **12** | +8 |
| Editor tests | 7645/0/0 | **7918+/0/5** | +273 |
| Editor binary | 157.4 MB | 156.98 MB | rebuilt many times |
| Operator preset menu entries | 30 | **83** | +53 |

---

## Section 1 — Cinematic primitive complement (iter 146-150)

Closed off the camera arc started in iter 107/143-145 with operator
ergonomics: docs/preset/native UX iters that don't add new bridge wires
but make the existing wires actually clickable.

| Iter | What shipped | Operator-visible result |
|---|---|---|
| 146 | Operator changelog 2026-04-29 extended through iter 145 | docs only |
| 147 | Lua Playground preset menu — 6 iter-143-145 camera presets | "[143] Camera follow first AT-AT" / "[144] Rotate camera..." dropdown items |
| 148 | Camera & Debug tab native UX (VM/dispatcher/state) | 6 new commands wired (XAML pending iter 149) |
| 149 | Camera & Debug tab XAML buttons | Operator can click 7 camera primitives natively |
| 150 | `SWFOC_LetterBoxOn` / `SWFOC_LetterBoxOff` | Cinematic letterbox toggle pair |

**Operator checklist for cinematic mode**:
- [ ] Open Camera & Debug tab
- [ ] Click "Camera follow" with AT-AT selected — verify camera tracks unit
- [ ] Click "Rotate camera to" — verify smooth rotation
- [ ] Click "Start cinematic" / "Set key" / "Transition key" / "End cinematic"
- [ ] Use Lua Playground preset "[150] Letterbox ON" — verify black bars appear
- [ ] Use preset "[150] Letterbox OFF" — verify bars retract

---

## Section 2 — Tactical/galactic spawn variants (iter 151-152)

| Iter | LIVE flip | Engine API | Operator usage |
|---|---|---|---|
| 151 | `SWFOC_TeleportUnitLua` | `(unit):Teleport(target_planet)` | UnitControl tab → paste unit + target Lua exprs → click Teleport |
| 152 | `SWFOC_GalacticSpawnUnit` | `Galactic_Spawn_Unit(player, type, planet)` | Galactic-map spawn complement to iter-109 tactical spawn |

---

## Section 3 — Unit-method batches (iter 153-157, 22 wires)

The dispatcher helpers introduced iter 111/112/154 turned out to be
shape-agnostic across receivers. Each batch ships ~3-7 wires at ~3-25
LoC bridge total.

| Iter | Wires shipped | Helper used | Domain |
|---|---|---|---|
| 153 | Set_Cannot_Be_Killed, Enable_Stealth | iter-111 (bool-arg) | Unit invuln/stealth toggles |
| 154 | Heal, Take_Damage, Set_Damage_Modifier, Set_Rate_Of_Fire_Modifier + **NEW Lua_DispatchUnitFloatMethod** | float-arg | Combat scalars per-unit |
| 155 | PlayerGiveMoney, PlayerSetTechLevel, PlayerUnlockTech | iter-154 (helper is shape-agnostic) | Player economy/tech |
| 156 | Activate_Ability, Disable_Capture, Set_Garrison_Spawn, Cancel_Hyperspace | mixed (iter-111/112/154) | Unit ability + capture/spawn flags |
| 157 | Set_In_Limbo, Set_Check_Contested_Space, Sell, Bribe, Move_To, Fire_Special_Weapon | mixed | Unit limbo / ownership / actions (6-wire mega-batch) |

**Operator workflow examples**:
```lua
-- Make AT-AT cannot-be-killed (HP stays at 1)
SWFOC_SetCannotBeKilledLua('Find_First_Object("Empire_AT_AT")', 'true')

-- Damage AT-AT for 500
SWFOC_TakeDamageLua('Find_First_Object("Empire_AT_AT")', '500')

-- Bribe AT-AT to Rebel ownership (Underworld signature ability)
SWFOC_BribeLua('Find_First_Object("Empire_AT_AT")', 'Find_Player("REBEL")')
```

---

## Section 4 — Global-method dispatcher introduction (iter 158-166)

| Iter | LIVE flip count | Helper | Notes |
|---|---|---|---|
| 158 | 3 (Disable_Bombing_Run / Flash_GUI_Object / Hide_GUI_Object) | **NEW Lua_DispatchGlobalArgMethod** | First global-arg helper; closes 4-helper original set |
| 159 | 4 (Story_Event / Add_Objective / Play_Music / Play_SFX_Event) | iter-158 (string-arg) | String-args via existing helper |
| 160 | 3 (Lock_Controls global / Disable_Orbital_Bombardment player / Story_Event_Trigger) | iter-158 + iter-111 | Demo of helper shape-agnosticism (player receivers) |
| 161 | 3 (Lock_Tech / Make_Ally / Make_Enemy player methods) | iter-154 (player receiver) | **CAVEAT**: Make_Ally/Make_Enemy state RESETS on game-mode change |
| 162 | 4 (Override_Max_Speed / Suspend_AI / Fade_Screen_In / Zoom_Camera) | iter-154 + iter-158 | Mixed batch |
| 163 | 3 (Attack_Target / Guard_Target / Divert) | iter-154 | Combat-order batch |
| 164 | 3 (Enable_As_Actor / Release_Credits_For_Tactical / Select_Object) | iter-112 + iter-154 | Player extension batch |
| 165 | 3 (Fade_Screen_Out / Rotate_Camera_By / Point_Camera_At) | iter-158 | Cinematic complement |
| 166 | 3 (Stop_All_Music / Resume_Mode_Based_Music / Show_GUI_Object) + **NEW Lua_DispatchGlobalNoArgMethod** | new global-no-arg helper | Closes 5-helper full 2x2 matrix |

---

## Section 5 — Read-side family + matrix completion (iter 167-178, 27 wires)

The architectural arc this section establishes: **the dispatcher matrix
becomes complete at 9 helpers** covering {receiver: obj/global} ×
{args: 0/1} × {write/read} + iter-154 obj/string-arg variant.

| Iter | LIVE flip count | Helper | Notes |
|---|---|---|---|
| 167 | 3 (Get_Hull / Get_Health / Get_Shield) + **NEW Lua_DispatchUnitGetterNoArg** | first read-side helper (return-value capture via DoString) | |
| 168 | 3 (Has_Attack_Target / Are_Engines_Online / Get_Owner) | iter-167 | Self-correcting test design caught a catalog drift mid-iter |
| 169 | 4 (Get_Type / Get_Credits / Get_Faction / Get_Tech_Level) | iter-167 | Player + unit receivers; helper shape-agnostic |
| 170 | 4 (Get_Name / Is_Stealthed / Is_In_Limbo / Is_Capturable) | iter-167 | Read-after-write pairs with iter-153/156/157 |
| 171 | 4 (Get_Position / Get_Parent_Object / Get_Attack_Target / Get_Damage_Modifier) | iter-167 | 7800 test milestone |
| 172 | 4 (Get_Garrison_Units / Get_Contained_Object_Count / Get_Behavior_ID / Get_Rate_Of_Fire_Modifier) | iter-167 | **100 LIVE wire milestone** + diagnostic-toolchain hardening (tee+grep+blame-hang-timeout) |
| 173 | 4 (Is_Ability_Active / Has_Property / Is_Category / Get_Distance) + **NEW Lua_DispatchUnitGetterArg** | first arg-getter with return capture | |
| 174 | 4 (Get_Bone_Position / Contains_Object_Type / Get_Space_Station_Level / Get_Type_Of_Unit) | iter-173 | Cross-receiver batch (unit + player + TaskForce) |
| 175-176 | 8 (TaskForce arc — Move_To, Reinforce, Release_Reinforcements, Launch_Units, Attack_Target, Guard_Target, Land_Units, Set_As_Goal_System_Removable) | iter-111/112/154 | TaskForce-prefixed naming disambiguates from unit-method namesakes |
| 177 | 3 (Find_Object_Type / FindPlanet / Find_First_Object) + **NEW Lua_DispatchGlobalGetterArg** | discovery family | |
| 178 | 3 (Get_Game_Mode / Get_Local_Player / Get_Seconds_Per_Game_Minute) + **NEW Lua_DispatchGlobalGetterNoArg** | **DISPATCHER MATRIX COMPLETE** (9 helpers) | |

---

## Section 6 — Post-matrix marginal-cost + namespace-agnosticism (iter 179-181)

Once the matrix completed, future wires using known shapes ship at
**~3 LoC bridge marginal cost**. Iter 179 was the first batch validating
this; iter 180-181 then discovered an architectural finding that
expanded reach further.

**Architectural finding (iter 180/181)**: the iter-158 helper (and
iter-178 as confirmed iter 181) are NAMESPACE-AGNOSTIC — they handle
dotted method names like `FOWManager.Reveal_All` transparently because
Lua's `.` lookup is part of the parser, not the helper. This unlocked
~10 namespaced functions (FOWManager.*, SFXManager.*, Thread.*) at
~3 LoC each.

| Iter | Wires | Helper | Notes |
|---|---|---|---|
| 179 | 4 (Is_Enemy / Is_Ally / Find_All_Objects_Of_Type / TaskForce_Move_To_Target) | iter-173/177/154 | Validates marginal-cost claim |
| 180 | 4 (FOWRevealAll / FOWUndoRevealAll / Unlock_Controls / Corrupt) | iter-158/166/154 | NAMESPACE-AGNOSTIC discovery + Underworld Bribe-pair |
| 181 | 2 (Thread.Get_Current_Stage / SFXManager.Allow_Unit_Reponse_VO) | iter-178/158 | Confirms namespace-agnosticism for both helpers; **PRESERVES ENGINE TYPO "Reponse"** with regression test |

**New regression-test pattern shipped iter 181**: typo-preservation
pinning. The catalog must contain the typo'd name AND not contain the
corrected name, so future "fix typo" PRs fail loudly.

---

## Section 7 — Multi-arg expansion (iter 182, 184, 186 helpers + spawn batch iter 185)

| Iter | What shipped | Notes |
|---|---|---|
| 182 | **NEW Lua_DispatchGlobalArg2Method** (10th helper) + 2 wires (Make_Ally / Make_Enemy global-form) | First multi-arg expansion beyond the matrix; mode-change-reset caveat preserved |
| 183 | **OPERATOR-FACING PIVOT** — Lua Playground preset menu update for iter 150-182 wires | 30 → 83 entries; pure VM/XAML; mid-iter caught snapshot drift + 4 typo'd preset SWFOC_* names |
| 184 | **NEW Lua_DispatchGlobalArg3Method** (11th helper) + 1 wire (FOWManager.Reveal partial-reveal) | Second multi-arg expansion |
| 185 | 3 wires via iter-184 helper (Reinforce_Unit / Spawn_From_Reinforcement_Pool / Create_Generic_Object) | Validates iter-184 marginal-cost claim; **PARAM-ORDER GOTCHA** pinned for Create_Generic_Object |
| 186 | **NEW Lua_DispatchGlobalGetter3Arg** (12th helper) + 1 wire (Find_Nearest closest-instance discovery) | Symmetric to iter-184 setter — mirror with engine return-value capture |

**Three regression-test patterns established this section** (and prior):
1. **Cross-iter rationale pinning** (iter 168): catalog entries must reference the helper they use.
2. **Typo-preservation pinning** (iter 181): both typo'd name exists AND corrected name doesn't.
3. **Param-order gotcha pinning** (iter 185): catalog must contain "GOTCHA" + "param order differs" + reference API for non-obvious orderings.

---

## Section 8 — Architectural diagram: 12-helper dispatcher set

```
                    Setter                      Getter (with return-capture)
                  (write-only)                  (return value via DoString)
                  ────────────                  ─────────────────────────
obj × 0 args      iter-112                      iter-167
obj × 1 arg       iter-111 (bool)               iter-173
                  iter-154 (string/numeric)
global × 0 args   iter-166                      iter-178
global × 1 arg    iter-158                      iter-177
global × 2 args   iter-182 ─────────────────── (none yet — when needed)
global × 3 args   iter-184                      iter-186
```

Helper introduction cost is amortized within the same iter (3-5 wires
shipped) and pays off across subsequent iters at sub-5 LoC per wire.

The matrix is **architecturally complete** for {0,1}-arg shapes
(iter-178 closed it). Multi-arg helpers (iter-182, 184, 186) extend
into 2-arg/3-arg territory as specific engine APIs need them.

---

## Section 9 — Toolchain hardening (iter 172)

Caught a 7-minute silent hang in `dotnet test ... | tail -3`: the shell
pipe buffers everything until source exits, so when testhost crashes
the output stays at 0 bytes for the whole hang.

**Permanent fix shipped iter 172**:
- Replace `| tail -N` with `| tee log | grep --line-buffered <pattern>`
- Add `--blame-hang-timeout 5m`
- Pre-kill stale `SwfocTrainer.App` / `testhost` processes

Pinned diagnosis + recovery to memory:
`feedback_dotnet_test_hang_diagnosis.md`. Validated for **15
consecutive iters** running without hang.

---

## Section 10 — Where the documented Lua API stands (iter 186)

The bridge has wired most of the documented engine Lua API surface:
- All single-receiver × 0/1-arg setters and getters: COMPLETE
- 2-arg / 3-arg setters: COMPLETE for documented APIs (Make_Ally global-form, FOWManager.Reveal, Reinforce_Unit, Create_Generic_Object)
- 3-arg getters: 1 wired (Find_Nearest)
- Namespaced functions (FOWManager.*, SFXManager.*, Thread.*): 4 wired

**Remaining gaps requiring new architectural work**:
- Varargs (FindTarget, EvaluatePerception, GiveDesireBonus, Thread.Create)
- Table-arg (Spawn_Battle({faction='REBEL', planet='HOTH'}))
- Multi-arg getter variants beyond 3-arg

**Native UX surfacing arc**: ~75 iter-150-186 LIVE wires still only
accessible via Lua Playground (no per-tab native buttons). Future
multi-iter arc would surface the high-value ones into Combat /
Spawning / Galactic / etc tabs.

---

## Operator quick-reference

To use any iter 100-186 LIVE wire:
1. Open editor (`publish/SwfocTrainer.App.exe`)
2. Navigate to Lua Playground tab
3. Click "Iter 100-182 LIVE wires" dropdown — pick a preset
4. Replace placeholder addresses (0xABCD) and Lua expressions with real
   values from Tactical Units / Inspector / Galactic tabs
5. Click "Run script" — bridge dispatches the SWFOC_* function
6. "Last response" shows engine result (or error if game state is
   incompatible with the call)

For LIVE wires with native UX (~30 of 142):
- UnitControl tab: 7 buttons (iter 117 — invuln/hide/AI/selectable/despawn/stop/retreat) + Change owner (iter 118)
- Spawning tab: Spawn-via-Lua GroupBox (iter 119)
- Combat tab: GLOBAL damage multiplier (iter 102)
- Speed tab: per-unit revert (iter 102)
- Camera & Debug tab: 7 camera primitive buttons (iter 107/148/149)
- Galactic tab: planet flip convert/kick (iter 279/280, pre-master-loop)

---

*Generated 2026-05-05 (iter 187 close-out). Next changelog when the
master loop crosses another major milestone (200 LIVE wires? Native UX
arc complete? Bridge architectural pivot?).*
