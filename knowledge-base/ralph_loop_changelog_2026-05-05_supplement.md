# Ralph Loop Changelog — 2026-05-05 Supplement (iter 188-195 native UX surfacing arc)

**Predecessor**: `ralph_loop_changelog_2026-05-05.md` covers iter 146-186 (bridge wires + dispatcher matrix completion).
**This supplement**: covers iter 188-195 — 8 iters of native UX surfacing across 7 editor tabs. Pure operator-facing work; zero new bridge wires shipped. The 142 LIVE wires from iter 100-186 stay LIVE; this arc made them clickable instead of paste-Lua-in-Playground accessible.

## Headline tally

- **8 surfacing iters** (188-195) over a single conversation.
- **28 native buttons** added across **7 editor tabs**.
- **0 new bridge wires** (all wires were already LIVE; only editor surfacing changed).
- **+0 LIVE flips, +0 dispatcher helpers** — pure operator-facing improvement.
- **Editor binary**: 156.99 MB (iter 188) → **165.25 MB** (iter 195). Size delta is Release/single-file build path differing from prior Debug builds, plus the 8 iters of code.
- **Filtered tests added**: 33 new pin tests across 8 pin files (3+3+3+4+4+5+4+6 + a CreateGenericObject param-order extra).

## Walk-through-every-tab format

Operators can now run extensive workflows entirely natively — no Lua Playground needed for these surfaced wires.

### Tab 1: UnitControl (4 read + 3 combat-order = 7 new buttons across iter 188 + 194)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 188 | GetHullLua | (unit):Get_Hull() | "How damaged is this unit?" |
| 188 | GetShieldLua | (unit):Get_Shield() | "What's the shield percentage?" |
| 188 | GetPositionLua | (unit):Get_Position() | "Where is this unit?" (returns position handle for chained queries) |
| 188 | GetGarrisonUnitsLua | (unit):Get_Garrison_Units() | "What's inside this transport?" |
| 194 | AttackTargetLua | (unit):Attack_Target(target) | "Make my unit attack THAT unit" |
| 194 | GuardTargetLua | (unit):Guard_Target(target) | "Defensive escort — guard this VIP" |
| 194 | DivertLua | (unit):Divert(position) | "Reroute the path through this position" |

UnitControl tab now has **22 native unit-method buttons** total (iter 117 + 118 + 188 + 194).

### Tab 2: PlayerState (3 read = 3 new buttons in iter 189)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 189 | GetCreditsLua | (player):Get_Credits() | "How many credits does REBEL have right now?" (read-after-write with iter-155 PlayerGiveMoney) |
| 189 | GetTechLevelLua | (player):Get_Tech_Level() | "What's REBEL's tech level?" (read-after-write with iter-155 PlayerSetTechLevel) |
| 189 | GetFactionLua | (player):Get_Faction() | "What faction is this player?" |

### Tab 3: Diagnostics (3 global-state = 3 new buttons in iter 190)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 190 | GetGameModeLua | Get_Game_Mode() | "Are we in Land/Space/Galactic mode?" — gates tactical-only commands |
| 190 | GetLocalPlayerLua | Get_Local_Player() | "Who is THIS slot?" — pairs with iter-155 (Get_Local_Player()):Give_Money() workflow |
| 190 | GetSecondsPerGameMinuteLua | Get_Seconds_Per_Game_Minute() | "What's the current time scale?" |

0-arg globals so no input field. Result lands in the diagnostic log as `[engine_state] Game mode -> Land`.

### Tab 4: Inspector (4 unit-receiver read = 4 new buttons in iter 191)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 191 | GetTypeLua | (unit):Get_Type() | "What type is this unit?" — returns GameObjectType handle |
| 191 | GetOwnerLua | (unit):Get_Owner() | "Who owns this unit?" — returns PlayerWrapper handle |
| 191 | HasAttackTargetLua | (unit):Has_Attack_Target() | "Is this unit currently engaged?" |
| 191 | AreEnginesOnlineLua | (unit):Are_Engines_Online() | "Are this ship's engines operational?" |

### Tab 5: Camera & Debug (4 write-side primitives = 4 new buttons in iter 192)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 192 | ZoomCameraLua | Zoom_Camera(time) | "Zoom over 2 seconds" |
| 192 | FadeScreenOutLua | Fade_Screen_Out(time) | "Fade to black for cinematic" |
| 192 | RotateCameraByLua | Rotate_Camera_By(degrees) | "Rotate camera by 45° (relative — vs iter-144 Rotate_Camera_To absolute)" |
| 192 | PointCameraAtLua | Point_Camera_At(target) | "Point camera at this object" — 8th camera primitive in the arc |

Camera & Debug tab now has **10 camera primitive native buttons** (iter 107/143/144/145×4/162/165×3) — full cinematic workflow without typing Lua.

### Tab 6: Combat (4 per-unit damage/heal = 4 new buttons in iter 193)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 193 | HealUnitLua | (unit):Heal() | "Restore this unit to full hull" — no-arg |
| 193 | TakeDamageLua | (unit):Take_Damage(amount) | "Deal N damage to this unit" — routes through iter-96 Take_Damage_Outer chokepoint, so the GLOBAL multiplier applies |
| 193 | SetDamageModifierLua | (unit):Set_Damage_Modifier(mult) | "Boost this unit's outgoing damage" — per-unit (different from iter-96 GLOBAL) |
| 193 | SetRateOfFireModifierLua | (unit):Set_Rate_Of_Fire_Modifier(mult) | "Speed up this unit's fire rate" — closes iter-101 SetFireRate gap at per-unit level (no global setter exists) |

Combat tab now has THREE damage scopes in one place: per-slot scalar (existing) + GLOBAL multiplier (iter 100) + per-unit (iter 193). Catalog rationale documents this hierarchy.

### Tab 7: Spawning (3 spawn variants = 3 new buttons in iter 195)

| Iter | Wire (SWFOC_*) | Engine Lua | Operator workflow |
|---|---|---|---|
| 195 | ReinforceUnitLua | Reinforce_Unit(player, type, position) | Alt to iter-109 Spawn_Unit — uses engine's reinforcement-pool path |
| 195 | SpawnFromReinforcementPoolLua | Spawn_From_Reinforcement_Pool(player, type, position) | Alt entrypoint to same pool — engine exposes both names |
| 195 | CreateGenericObjectLua | Create_Generic_Object(**type, position, player**) | **PARAM ORDER GOTCHA** — engine takes (type, position, player), NOT (player, type, position). UI stays player-first; dispatcher reorders internally. |

iter-119 GroupBox now offers **4 spawn entrypoint choices** in one place (iter 119 SpawnUnit + iter 195 trio).

## Patterns established this arc

### 1. Constructor-injection cascade

Adding `V2UnitMutationDispatcher` as a 2nd constructor arg (iter 191 Inspector, iter 193 Combat) cascades to ~5 test files each. The C# compiler catches all CS7036 errors immediately — no need for grep searching. Strong argument for changing constructor signatures over silently extending optional arg lists: the type system is the regression guard.

### 2. Reverse-orphan snapshot regex limitation (3-time recurrence)

The reverse-orphan test uses regex `\bSWFOC_X\s*\(` to detect call sites. Two dispatcher styles trip the regex differently:

- `BuildUnitLuaMethodCall("SWFOC_X", ...)` — string-literal form, regex-INVISIBLE. Entries STAY in `KnownUnwiredEntries` snapshot with iter-N NOTE comment.
- `$"return SWFOC_X('{...}')"` — interpolated literal form, regex MATCHES (SWFOC name immediately followed by `(`). Entries DROP from snapshot.

Hit 3 times: iter 191 (3 entries kept), iter 194 (2 entries kept), iter 195 (2 kept + 1 dropped). Future regex extension would unify this — but the NOTE-comment workaround is fine for now.

### 3. Param-order GOTCHA reorder pattern (iter 195)

`Create_Generic_Object(type, position, player)` has DIFFERENT param order from `Spawn_Unit(player, type, position)`. Three layers of defense:

1. **Catalog rationale**: explicit "GOTCHA: param order differs" warning + reference to iter-185 pin tests.
2. **Dispatcher signature**: `CreateGenericObjectLuaAsync(string typeLuaExpr, string positionLuaExpr, string playerLuaExpr)` — mirrors engine API order exactly. iter-195 pin test asserts parameter NAMES match expected order.
3. **VM reorders before calling**: UI fields stay player-first (matching iter-119 SpawnUnit); the VM's `CreateGenericObjectLuaCore()` swaps args before passing to dispatcher. Operator never has to manually reorder. Button label flags the gotcha so the operator knows _why_ it's different.

This is the cleanest GOTCHA-handling pattern in the codebase. Documented for future divergent-API wires.

### 4. Builder-helper extraction (iter 195)

When 2+ wires share the same Lua format pattern, extract a helper (`BuildSpawnVariantPlayerTypePosCommand`) — adding a 4th similar wire becomes ~3 LoC. When 1 wire diverges (CreateGenericObject's param order), extract a SEPARATE named helper (`BuildCreateGenericObjectCommand`) — divergence visible in the helper name. "Factor common shapes, but keep divergent shapes visible by separation."

### 5. Tab-extension vs new-tab pattern

iter 188-193 each added a NEW GroupBox to a different tab (UnitControl/PlayerState/Diagnostics/Inspector/Camera/Combat). iter 194 EXTENDS an existing GroupBox (UnitControl iter-117/118) with 3 more buttons. iter 195 EXTENDS another existing GroupBox (Spawning iter-119). This is the natural endgame: each tab grows organically.

## Bridge state (unchanged from iter 187)

- **142 LIVE wires** total in master loop (iter 100-186).
- **12 dispatcher helpers** (matrix complete + 4 multi-arg expansions): iter-111/112/154/158/166/167/173/177/178 + iter-182/184/186 multi-arg.
- **0 new wires shipped** in iter 188-195. All 28 buttons surface existing LIVE wires.

## Editor surface count

After iter 188-195:

- **~25 LIVE-wire native buttons surfaced** across the 7 tabs touched in this arc.
- Combined with prior arcs (iter 117/118/119 UnitControl + iter 148/149 Camera + iter 102/107 Speed/Camera + ...), the editor now has **substantial native UX coverage** for the master-loop's 142 LIVE wires.
- **Lua Playground preset menu** still available for the ~70 wires that don't have native UX yet (iter-159/160/161/164/166/170/171/172/175/176/179/180/181/186 batches — all surfaced in the iter-183 preset menu).

## Test gates state

- **Filtered iter-188 tests**: 3/3 GREEN.
- **Filtered iter-189 tests**: 3/3 GREEN.
- **Filtered iter-190 tests**: 3/3 GREEN.
- **Filtered iter-191 tests**: 4/4 GREEN.
- **Filtered iter-192 tests**: 4/4 GREEN.
- **Filtered iter-193 tests**: 5/5 GREEN.
- **Filtered iter-194 tests**: 4/4 GREEN.
- **Filtered iter-195 tests**: 6/6 GREEN.
- **Total filtered tests added**: **32/32 GREEN**.
- **Bridge harness**: 1100/0 (unchanged from iter 186).

## Operator quick-reference

For any operation with native UX, click the button. For wires without native UX, use the Lua Playground preset menu (iter 183 still current — 83 entries).

**Surfacing arc complete this conversation**: 8 iters, 28 buttons, 7 tabs, 32 pin tests. Bridge↔editor ↔catalog 3-way contract aligned for all surfaced wires.

## What's NOT yet surfaced (future iters)

These wires remain in the `KnownUnwiredEntries` snapshot — Lua Playground preset menu is still the operator path:

- iter-159 PlaySfxEvent (audio)
- iter-160 DisableOrbitalBombardment / StoryEventTrigger (worldstate)
- iter-161 LockTech / MakeEnemy (PlayerState — Lock_Tech is queued, MakeEnemy player-receiver form is queued; iter-182 GLOBAL forms also queued)
- iter-162 SuspendAi (Diagnostics or Combat)
- iter-164 EnableAsActor / SelectObject (PlayerState extension)
- iter-166 ShowGuiObject (Diagnostics or WorldState)
- iter-170 GetName (read-side — PlayerState extension)
- iter-171 GetParentObject / GetAttackTarget / GetDamageModifier (Inspector extension)
- iter-172 GetContainedObjectCount / GetBehaviorId / GetRateOfFireModifier (Inspector extension)
- iter-173 HasProperty / IsCategory / GetDistance (Inspector extension)
- iter-174 ContainsObjectType / GetSpaceStationLevel (Inspector or TaskForce-tab if added)
- iter-175 TaskForceLaunchUnits / Reinforce / ReleaseReinforcements (TaskForce-tab if added)
- iter-176 TaskForceAttackTarget / TaskForceGuardTarget / TaskForceLandUnits / TaskForceSetAsGoalSystemRemovable (TaskForce-tab)
- iter-177 FindFirstObject (Galactic — Find_Object_Type/FindPlanet already queued)
- iter-178 GetSecondsPerGameMinute (Diagnostics — already covered)
- iter-179 IsAlly / TaskForceMoveToTarget (PlayerState/TaskForce)
- iter-180 FOWUndoRevealAll (Galactic)
- iter-184 FOWReveal (Galactic — partial reveal)
- iter-186 FindNearest (Camera or Tactical)

Estimated future surfacing: 5-8 more iters at the same ~3-4 buttons per iter cadence. After that, the editor will have full native UX coverage of the 142 LIVE wires.
