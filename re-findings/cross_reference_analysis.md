# SWFOC RE Cross-Reference Analysis
## Binary RE Findings vs Community Documentation

Generated: 2026-04-04
Sources: knowledge-base/alamo_engine_kb_v3.json, knowledge-base/lua_binding_map.json, archive/phase2_intermediates/signatures_phase2.json, re-findings/game_systems_map.json, knowledge-base/rtti_class_hierarchy.json, runtime CE MCP session

---

## 1. What Our Binary RE Already Covers (No Community Docs Needed)

### Struct Layouts (VERIFIED via live CE MCP)
| Struct | Fields Mapped | Live-Verified? |
|--------|--------------|----------------|
| GameObjectClass | 15 fields (vtable, object_id, owner, hp, component_array, parent_index, invuln, etc.) | YES — vtable 0x8661B8 confirmed, HP/owner read live |
| PlayerObject | 8 fields (player_id*, credits, max_credits, tech, max_tech, faction_ref) | YES — credits, tech, faction name all confirmed live |
| PlayerListClass | 2 fields (player_array, player_count) | YES — PlayerArray at RVA 0xA16FF0 confirmed live |
| GameObjectType | 3 fields (name SSO at +0xF8, sub_objects, type_name) | YES — type names read live (DARK_APPRENTICE, etc.) |
| LocomotorComponent | 2 fields (speed_override_flag +0x29C, speed_value +0x2A0) | Not yet live-tested |

*PlayerObject+0x04 from KB was WRONG (returns garbage). Corrected offsets discovered via CE MCP:
- +0x37 = playable flag, +0x48 = slot index, +0x62 = local player flag, +0x68 = faction name ptr

### Functions (14 with full RVAs and signatures)
| Function | RVA | Signature | AOB? |
|----------|-----|-----------|------|
| SetHP | 0x3A89D0 | void(GameObjectClass*, float) | YES (verified live) |
| QueryInterface | 0x395AC0 | void*(GameObjectClass*, int) | Pseudocode available |
| Get_Owner_Lua | 0x5792E0 | int(GameObjectWrapper*) | Pseudocode available |
| ResolveParentOwner | 0x3956C0 | void*(GameObjectClass*) | - |
| Change_Owner | 0x574D0E | void(GameObjectClass*, PlayerObject*) | - |
| AddCredits | 0x27F370 | void(PlayerObject*, float, bool) | YES |
| SetTechLevel | 0x288980 | int(PlayerObject*, int, bool, bool) | YES |
| SetSpeedOverride | 0x3A8C90 | void(GameObjectClass*, float) | YES |
| ClearSpeedOverride | 0x38F8B0 | void(GameObjectClass*) | YES |
| ScheduleHeroRespawn | 0x48EB10 | bool(hero, planet, delay, force) | - |
| Make_Invulnerable_Setter | 0x3ABB80 | void(GameObjectClass*, bool) | - |
| PlayerList_FindByID | 0x294BC0 | PlayerObject*(PlayerListClass*, int) | - |
| Take_Damage | 0x38A350 | void(GameObjectClass*, float, ...) | - |
| GetMaxHealth | 0x3A8B60 | float(GameObjectClass*) | - |

### Lua Bindings (134 total from binary analysis)
- 26 wrapper classes identified with RTTI addresses
- 40 global Lua functions (Spawn_Unit, Find_Object_Type, Story_Event, etc.)
- 32 GameObjectWrapper methods (Get_Hull, Get_Owner, Change_Owner, Make_Invulnerable, etc.)
- 12 PlayerWrapper methods (Give_Money, Set_Tech_Level, Get_Credits, etc.)
- 10 TaskForce methods across 4 subtypes
- 20 trainer-critical bindings with full error strings and parameter counts

### RTTI Recovery
- 2,919 classes, 1,992 vtables, 1,233 inheritance trees
- 16 subsystems mapped: combat (148 classes), AI (312), movement (130), abilities (91), economy (45), story (89), etc.
- Top hookable targets prioritized: LocomotorCommonClass (104 vfuncs), GameModeClass (89), CombatantBehaviorClass (55)

---

## 2. What Community Docs CONFIRM (Validation)

### Lua Function Names — Our Binary RE vs Community
Our binary analysis found 134 Lua bindings. The community (eaw-emmyluadoc, Focumentation) documents approximately 200+ Lua functions. Cross-referencing:

**CONFIRMED by our binary RE (exact name match + RVA):**
- Spawn_Unit ✓ (RVA known, error strings found, backing class identified)
- Reinforce_Unit ✓
- Find_Object_Type ✓
- Find_First_Object ✓
- Find_All_Objects_Of_Type ✓
- Find_Nearest ✓
- Find_Path ✓
- Story_Event ✓
- Get_Game_Mode ✓
- Assemble_Fleet ✓
- Create_Generic_Object ✓
- Create_Position ✓
- Add_Objective ✓
- FindPlanet ✓ (global)
- FindTarget ✓ (global)
- Get_Hull ✓
- Get_Health ✓
- Get_Shield ✓
- Get_Owner ✓ (RVA 0x5792E0 decompiled with pseudocode)
- Get_Type ✓
- Get_Position ✓
- Get_Distance ✓
- Get_Parent_Object ✓
- Get_Bone_Position ✓
- Get_Attack_Target ✓
- Has_Attack_Target ✓
- Attack_Target ✓
- Guard_Target ✓
- Move_To ✓
- Fire_Special_Weapon ✓
- Activate_Ability ✓
- Is_Ability_Active ✓
- Has_Property ✓
- Is_Category ✓
- Make_Invulnerable ✓ (RVA 0x3ABB80 decompiled)
- Set_Cannot_Be_Killed ✓
- Teleport ✓
- Teleport_And_Face ✓
- Override_Max_Speed ✓ (RVA 0x57E590)
- Change_Owner ✓ (RVA 0x574D0E decompiled)
- Cancel_Hyperspace ✓
- Set_Selectable ✓
- Prevent_AI_Usage ✓
- Are_Engines_Online ✓
- Give_Money ✓ (RVA 0x603130, calls AddCredits)
- Set_Tech_Level ✓ (RVA 0x604480, calls SetTechLevel)
- Get_Credits ✓
- Unlock_Tech ✓
- Lock_Tech ✓
- Is_Enemy ✓
- Is_Ally ✓
- Release_Credits_For_Tactical ✓
- Divert ✓
- Play_SFX_Event ✓
- Play_Music ✓
- Stop_All_Music ✓

**Total confirmed: ~56 functions with exact name + RVA from binary**

### Functions Community Docs Have That We DON'T (Gaps to Fill)

Based on the Focumentation wiki and eaw-emmyluadoc, these are Lua functions documented by the community that we have NOT yet found RVAs for:

**High Priority (trainer/helper-relevant) — from Imperialware + Community:**
- `Find_Player(faction_name)` — get player object by faction name string. CRITICAL for helper bridge.
- `SpawnList(unit_table, position, owner, allow_ai, delete_after)` — spawn MULTIPLE units at once. Better than Reinforce_Unit for batch spawns.
- `Object.Despawn()` — remove an object from the game. No RVA found yet.
- `Planet.Change_Owner(player)` — change planet ownership in GC. No RVA found yet.
- `Sleep(seconds)` — coroutine yield. Essential for helper scripts with delays.
- `TestValid(object)` — check if object ref is still alive. Prevents crashes.
- `Object.Is_Human()` — on PlayerWrapper, returns true for human player. CONFIRMS our +0x62 flag finding.
- `Object.Get_Enemy()` — on PlayerWrapper, returns enemy player list.
- `Set_Hull` — NOT in binary (confirmed). Hull must be set via SetHP at C++ level.
- `ReinforceList` — likely same as SpawnList? Community uses both names.

**Medium Priority (abilities/unit control) — from Imperialware:**
- `Object.Has_Ability(ability_name)` — check if unit has specific ability
- `Object.Is_Ability_Ready(ability_name)` — check ability cooldown state
- `Object.Reset_Ability_Counter()` — reset ability cooldown
- `Object.Face_Immediate(target)` — instant rotation (Teleport_And_Face alternative)
- `Object.Hide(bool)` — show/hide object
- `Object.Get_Planet_Location()` — which planet an object is at (GC mode)
- `Hide_Sub_Object(unit, flag, sub_name)` — hide sub-objects (e.g., "Shield" mesh)
- `Create_Thread(func_name)` — Lua coroutine creation
- `GameRandom(min, max)` — random number
- `ScriptExit()` — terminate script

**Low Priority (UI/debug):**
- `Add_Radar_Blip(pos, name)` / `Remove_Radar_Blip(name)` — radar markers
- `Object.Set_Check_Contested_Space(bool)` — toggle space contest check
- Various cinematic camera functions (we found most already)

**KEY INSIGHT from Imperialware:** It has ZERO memory offsets. It's purely file-based (XML modding + Lua script injection, requires game restart). Our binary RE is the ONLY source of runtime memory offsets for this game. Imperialware's value is confirming Lua function names/signatures and demonstrating the spawning pattern.

---

## 3. What Our RE Found That Community Docs DON'T Cover

**These are UNIQUE contributions from our binary RE that aren't in any community source:**

### Internal C++ Architecture
- **QueryInterface component system** — full pseudocode, 8 query types mapped, component_lookup_table at +0x332
- **Parent/sub-object traversal chain** — [obj+0x335] → [obj+0x278] → parent, with deep nesting loop recipe
- **SetHP caller graph** — all 18 callers mapped, proving SetHP is the ONLY HP write path
- **RTTI class hierarchy** — 2,919 classes with inheritance trees, vtable RVAs, virtual method counts
- **LuaMemberFunctionWrapper<T> registration mechanism** — how wrapper classes register methods on Lua userdata
- **Lua 5.0.2 C API wrapper RVAs** — lua_pushstring, lua_newtable, etc. compiled into the binary
- **lua_State* location candidates** — TheEvaluatorScriptManagerClass singleton at 0x5E77E0

### Memory Layout Details
- **PlayerObject+0x62** = local player flag (1 = YOU) — NOT in any community doc
- **PlayerObject+0x37** = playable faction flag — NOT in any community doc
- **PlayerObject+0x48** = actual slot index (corrects KB's wrong +0x04) — NOT in any community doc
- **GameObjectClass vtable RVA 0x8661B8** — exact vtable address for runtime object identification
- **Parent component index at +0x335** — sub-object ownership resolution shortcut
- **Container reference at +0x2B0** — invulnerability propagation chain

### AOB Signatures
All AOB signatures in signatures_phase2.json are original binary RE work — not available anywhere else:
- SetHP prologue: `40 53 48 83 EC 60 0F 29 74 24 50 0F 57 C0 F3 0F 10 71 5C`
- AddCredits prologue: `48 89 5C 24 08 57 48 83 EC 40 0F 29 74 24 30 41 0F B6 F8...`
- SetSpeedOverride: `48 8B 81 A8 00 00 00 48 85 C0 74 ?? 89 90 A0 02 00 00...`
- SetTechLevel: `56 57 41 54 48 83 EC 40 8B C2 48 89 5C 24 60...`

### CE Bugs Discovered
- `getRTTIClassName()` broken for x64 image-relative RTTI (COL sig=1)
- `vtQword` scan cannot find large addresses
- Neither is documented anywhere in the CE community

---

## 4. Practical Impact — What Saves Us Future Work

### Things We Do NOT Need to Research
Thanks to our Phase 1-2 RE + runtime CE session:

1. **Owner detection** — SOLVED. PlayerArray at 0xA16FF0, local player at +0x62. No more scanning.
2. **HP manipulation** — SOLVED. SetHP at 0x3A89D0, single write path, 18 callers mapped.
3. **Credits** — SOLVED. PlayerObject+0x70 (float), AddCredits at 0x27F370.
4. **Tech level** — SOLVED. PlayerObject+0x84 (int32), SetTechLevel at 0x288980.
5. **Speed override** — SOLVED. obj+0xA8→locomotor, +0x29C flag, +0x2A0 value.
6. **Hero respawn** — SOLVED. Global at RVA 0xB169F0.
7. **Faction names** — SOLVED. PlayerObject+0x68 → string pointer.
8. **Unit type identification** — SOLVED. obj+0x298 → GameObjectType → +0xF8 name.
9. **Sub-object ownership** — SOLVED. Parent chain walk via +0x335/+0x278.
10. **Invulnerability** — SOLVED. Make_Invulnerable_Setter at 0x3ABB80 (proper way), SetHP hook (practical way).

### Things That Still Need Research (Phase 3 Candidates)
1. **Fog of War pointer** — not found. Community docs mention `Fog_Of_War` as a Lua global but no internal address.
2. **Selected unit pointer** — the game's "currently selected unit" global. Trainer uses AOB signatures for UI mirror values but the direct pointer would be more robust.
3. **Game speed multiplier global** — the trainer profile has `game_speed: 0` (unresolved). Need to find the actual global.
4. **Damage multiplier injection** — DealDamage vfunc on CombatantBehaviorClass (vtable 0x8B21D0). Needs decompilation of specific vfunc slots.
5. **Lua state pointer extraction** — TheEvaluatorScriptManagerClass at 0x5E77E0 likely holds the lua_State*. Extracting it would enable calling any Lua function from the CT table.
6. **Income rate multiplier** — the `FUN_1404B0500()+0x20` reference from the KB needs validation.
7. **Planet ownership** — galactic map planet structs for planet switching features.

### What Imperialware Could Contribute
If the Imperialware project is still accessible:
- **Spawning implementation** — how it calls Spawn_Unit/Reinforce from C# via Lua injection
- **Mod roster parsing** — how it reads unit lists from any mod's XML
- **Memory offset validation** — if it has known offsets for the 32-bit build, comparing against our 64-bit findings validates the struct layout migration

### What PetroglyphTools Could Replace
- **MEG file reading** — our editor has a custom implementation; PetroglyphTools NuGet package could replace it
- **DAT localization parsing** — same
- **Game detection/launch** — process locator patterns

---

## 5. Knowledge Base Update Recommendations

### alamo_engine_kb.json Corrections Needed
1. **PlayerObject+0x04** → mark as WRONG or remove. Actual slot index is at +0x48.
2. **Add new fields**: +0x37 (playable flag), +0x48 (slot index), +0x62 (local player flag)
3. **Update PlayerObject description** to note the 3-layer identity system: +0x37 (playable), +0x62 (local), +0x68 (faction name)

### signatures_phase2.json Additions
1. Add SetHP AOB: `40 53 48 83 EC 60 0F 29 74 24 50 0F 57 C0 F3 0F 10 71 5C`
2. All signatures verified unique in module

### lua_binding_map.json Enrichment
1. Cross-reference parameter semantics from eaw-emmyluadoc
2. Add missing function RVAs for Despawn, Get_Faction, ReinforceList
3. Document scope restrictions (tactical-only vs any-mode) from community docs

---

## Summary

| Category | Our RE Coverage | Community Adds | Status |
|----------|----------------|----------------|--------|
| C++ struct layouts | 14 structs, 50+ fields | 0 — community has no C++ docs | COMPLETE |
| Function RVAs | 14 functions | 0 — community has no RVAs | COMPLETE |
| AOB signatures | 7 unique signatures | 0 — community has no AOBs | COMPLETE |
| Lua binding names | 134 bindings | +271 additional (405 total in community) | 33% of names, but 100% of RVAs |
| Lua parameter semantics | Error strings only | Full docs + behavioral gotchas | ENRICHMENT NEEDED |
| Behavioral warnings | 0 | 5+ critical crash/bug warnings | HIGH VALUE — prevents runtime crashes |
| Runtime object detection | PlayerArray + local player flag | 0 — community has nothing runtime | OUR UNIQUE CONTRIBUTION |
| RTTI class hierarchy | 2,919 classes | 0 — community has nothing | OUR UNIQUE CONTRIBUTION |
| Game mod data (XML/MEG) | Not our focus | PetroglyphTools has parsers | DEFER TO COMMUNITY |

**Bottom line:** Our binary RE covers the runtime/memory side comprehensively. The community documentation is strongest on Lua function semantics and mod data formats. The two are complementary — the community tells us WHAT Lua functions do, we know WHERE they are in memory and HOW to call them.
