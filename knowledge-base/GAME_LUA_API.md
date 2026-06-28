# SWFOC Game Lua API Reference
## Star Wars: Empire at War — Forces of Corruption (Thrawn's Revenge 3.5)
## All 134 Binary-Confirmed + 271 Community-Documented Functions

---

## How to Call These Functions via the DLL Bridge

With `lua_load` + `lua_pcall` + `lua_gettable`, you can now execute **any Lua code** the game supports. Two methods:

### Method 1: Named Pipe (External — from CE, .NET trainer, Python, etc.)
```
echo 'SWFOC_SetCredits(50000)' | ncat --send-only \\.\pipe\swfoc_bridge
```

### Method 2: SWFOC_DoString (From Lua scripts inside the game)
```lua
SWFOC_DoString("Spawn_Unit(player, Find_Object_Type('AT_AT'), Create_Position(0,0,0))")
```

### Method 3: Direct DLL Function (From registered SWFOC_* functions)
```lua
local slot, faction = SWFOC_GetLocalPlayer()
SWFOC_SetCredits(999999)
```

---

## Quick Reference: What Can You Do?

| Want to... | Lua Code | Scope |
|-----------|----------|-------|
| Give yourself money | `player:Give_Money(50000)` | Any |
| Max tech level | `player:Set_Tech_Level(5)` | Any |
| Spawn a unit | `Spawn_Unit(player, Find_Object_Type("AT_AT"), pos)` | Tactical |
| Make unit invincible | `unit:Make_Invulnerable(true)` | Tactical |
| Kill a unit | `unit:Take_Damage(99999)` | Tactical |
| Change ownership | `unit:Change_Owner(enemy_player)` | Any |
| Teleport a unit | `unit:Teleport(Create_Position(x, y, z))` | Tactical |
| Reveal map | `FOWManager.Reveal_All()` | Any |
| Speed up a unit | `unit:Override_Max_Speed(500)` | Tactical |
| Unlock a tech | `player:Unlock_Tech(Find_Object_Type("DEATH_STAR"))` | Any |
| Spawn on galactic | `Galactic_Spawn_Unit(player, type, planet)` | Galactic |
| Play cinematic | `Point_Camera_At(unit); Letter_Box_On()` | Tactical |
| Trigger story event | `Story_Event("YOURMOD_CUSTOM_EVENT")` | Any |
| Find all enemies | `Find_All_Objects_Of_Type(Find_Object_Type("AT_AT"))` | Tactical |
| Execute arbitrary Lua | `SWFOC_DoString("any valid lua code here")` | Any |

---

## 1. Global Functions (40 confirmed from binary)

### Spawning & Creation
| Function | Params | Scope | Description |
|----------|--------|-------|-------------|
| `Spawn_Unit` | `player, type, position` | Tactical | Spawns a unit at position. Returns the unit object. |
| `Reinforce_Unit` | `player, type, position` | Tactical | Spawns from reinforcement pool |
| `Spawn_From_Reinforcement_Pool` | `player, type, position` | Tactical | Alternative reinforcement spawn |
| `Create_Generic_Object` | `type, position, player` | Tactical | Creates any object type (note: different param order) |
| `Galactic_Spawn_Unit` | `player, type, planet` | Galactic | Spawns on galactic map at planet |
| `Create_Position` | `x, y, z` | Any | Creates a position vector for spawn/teleport |

### Queries & Finding
| Function | Params | Returns | Description |
|----------|--------|---------|-------------|
| `Find_Object_Type` | `"TYPE_NAME"` | GameObjectType | Looks up a type by XML name |
| `Find_First_Object` | `type_name` | GameObjectWrapper | Finds first instance of type |
| `Find_All_Objects_Of_Type` | `type` | table | Returns all instances |
| `Find_Nearest` | `type, position, player` | GameObjectWrapper | Finds nearest of type |
| `Find_Hint` | `"hint_name", player` | position/object | Finds a map hint marker |
| `Find_Path` | `from, to` | table | Finds path between planets |
| `FindPlanet` | `"PLANET_NAME"` | PlanetWrapper | Finds a planet by name |
| `FindTarget` | `evaluator, ...` | GameObjectWrapper | AI target finder |
| `Get_Game_Mode` | none | string | Returns "Galactic", "Space", "Land" |

### Story & Events
| Function | Params | Description |
|----------|--------|-------------|
| `Story_Event` | `"EVENT_NAME"` | Fires a story event |
| `Story_Event_Trigger` | `"EVENT_NAME"` | Alternative trigger |
| `Add_Objective` | `"OBJECTIVE_ID"` | Adds an objective to the UI |

### Camera & Cinematics
| Function | Params | Description |
|----------|--------|-------------|
| `Point_Camera_At` | `object` | Points camera at unit/location |
| `Scroll_Camera_To` | `position` | Smooth camera pan |
| `Zoom_Camera` | `level` | Sets zoom level |
| `Rotate_Camera_By` | `degrees` | Rotates camera |
| `Letter_Box_On` | none | Enables cinematic letterbox |
| `Fade_Screen_In` | `duration` | Fades screen in |
| `Suspend_AI` | `seconds` | Pauses AI for cinematic |

### Audio
| Function | Params | Description |
|----------|--------|-------------|
| `Play_Music` | `"MUSIC_EVENT"` | Plays music |
| `Stop_All_Music` | none | Stops all music |
| `Resume_Mode_Based_Music` | none | Returns to normal music |
| `Play_SFX_Event` | `"SFX_EVENT"` | Plays sound effect |

### Fleet & AI
| Function | Params | Description |
|----------|--------|-------------|
| `Assemble_Fleet` | `player, planet, types` | Assembles fleet at planet |
| `EvaluatePerception` | `...` | AI perception evaluation |
| `GiveDesireBonus` | `...` | Modifies AI desire weights |
| `_ProduceObject` | `player, type` | AI production command |

---

## 2. GameObjectWrapper Methods (32 confirmed)

Called on unit/building objects: `unit:Method_Name(args)`

### Health & Combat
| Method | Params | Description |
|--------|--------|-------------|
| `Get_Hull()` | none | Returns current HP (float) |
| `Get_Health()` | none | Returns health percentage |
| `Get_Shield()` | none | Returns shield percentage |
| `Make_Invulnerable(bool)` | boolean | Toggle invulnerability |
| `Set_Cannot_Be_Killed(bool)` | boolean | Prevents death (HP stays at 1) |
| `Fire_Special_Weapon(slot)` | int | Fires special weapon |
| `Activate_Ability(name)` | string | Activates named ability |
| `Is_Ability_Active(name)` | string | Checks if ability is active |

### Movement & Position
| Method | Params | Description |
|--------|--------|-------------|
| `Move_To(position)` | position | Orders move to location |
| `Teleport(position)` | position | Instant teleport |
| `Teleport_And_Face(pos, target)` | pos, target | Teleport and face direction |
| `Override_Max_Speed(speed)` | float | Sets custom max speed |
| `Get_Position()` | none | Returns current position |
| `Get_Distance(target)` | object | Returns distance to target |
| `Get_Bone_Position(bone)` | string | Gets position of model bone |
| `Cancel_Hyperspace()` | none | Cancels hyperspace jump |
| `Are_Engines_Online()` | none | Checks if engines work |

### Commands
| Method | Params | Description |
|--------|--------|-------------|
| `Attack_Target(target)` | object | Orders attack on target |
| `Guard_Target(target)` | object | Orders guard on target |
| `Divert(position)` | position | Diverts unit to position |

### Queries
| Method | Params | Returns | Description |
|--------|--------|---------|-------------|
| `Get_Owner()` | none | PlayerWrapper | Gets owning player |
| `Get_Type()` | none | GameObjectType | Gets unit type |
| `Get_Parent_Object()` | none | GameObjectWrapper | Gets parent (e.g., garrison) |
| `Get_Attack_Target()` | none | GameObjectWrapper | Gets current attack target |
| `Has_Attack_Target()` | none | boolean | Checks if attacking |
| `Is_Category(cat)` | string | boolean | Checks category membership |
| `Has_Property(prop)` | string | boolean | Checks for property flag |

### Control
| Method | Params | Description |
|--------|--------|-------------|
| `Change_Owner(player)` | PlayerWrapper | Transfers ownership |
| `Prevent_AI_Usage(bool)` | boolean | Blocks AI from controlling unit |
| `Set_Selectable(bool)` | boolean | Controls if player can select |

---

## 3. PlayerWrapper Methods (12 confirmed)

Called on player objects: `player:Method_Name(args)`

### Economy
| Method | Params | Description |
|--------|--------|-------------|
| `Give_Money(amount)` | number | Adds credits |
| `Get_Credits()` | none | Returns current credits |
| `Release_Credits_For_Tactical(amount)` | number | Releases credits for land battle |

### Technology
| Method | Params | Description |
|--------|--------|-------------|
| `Set_Tech_Level(level)` | number | Sets tech level (1-5) |
| `Unlock_Tech(type)` | GameObjectType | Unlocks specific tech |
| `Lock_Tech(type)` | GameObjectType | Locks specific tech |

### Diplomacy
| Method | Params | Returns | Description |
|--------|--------|---------|-------------|
| `Is_Enemy(player)` | PlayerWrapper | boolean | Checks if enemy |
| `Is_Ally(player)` | PlayerWrapper | boolean | Checks if allied |

### Other
| Method | Params | Description |
|--------|--------|-------------|
| `Select_Object(object)` | GameObjectWrapper | Selects a unit in UI |
| `Enable_As_Actor()` | none | Enables AI actor mode |

---

## 4. TaskForce Methods (10 confirmed)

Called on assembled task forces: `taskforce:Method_Name(args)`

| Method | Wrapper | Description |
|--------|---------|-------------|
| `Move_To(target)` | All subtypes | Moves task force |
| `Move_To_Target(target)` | TaskForceClass | Moves to specific target |
| `Attack_Target(target)` | Space/Land | Orders attack |
| `Guard_Target(target)` | SpaceTaskForce | Orders guard |
| `Reinforce(type)` | Space/Land | Reinforces task force |
| `Release_Reinforcements()` | SpaceTaskForce | Releases held reinforcements |
| `Launch_Units(planet)` | GalacticTaskForce | Launches fleet to planet |
| `Land_Units(planet)` | GalacticTaskForce | Lands ground forces |
| `Get_Type_Of_Unit(idx)` | TaskForceClass | Gets unit type at index |
| `Set_As_Goal_System_Removable(b)` | TaskForceClass | AI goal cleanup flag |

---

## 5. Community-Documented Functions (271 additional)

These are documented in [eaw-emmyluadoc](https://github.com/eaw-emmyluadoc) and [Focumentation](https://focumentation.github.io/) but not yet confirmed in our binary analysis. They should work via `SWFOC_DoString()`.

### Additional GameObjectWrapper Methods (~80)
- `Despawn()`, `Sell()`, `Stop()`, `Retreat()`
- `Set_Garrison_Spawn(bool)`, `Get_Garrison_Units()`
- `Enable_Behavior(name, bool)`, `Get_Behavior_ID()`
- `Set_In_Limbo(bool)`, `Is_In_Limbo()`
- `Contains_Object_Type(type)`, `Get_Contained_Object_Count()`
- `Set_Check_Contested_Space(bool)`
- `Enable_Stealth(bool)`, `Is_Stealthed()`
- `Bribe(player)`, `Corrupt(amount)`
- `Get_Rate_Of_Fire_Modifier()`, `Set_Rate_Of_Fire_Modifier(float)`
- `Get_Damage_Modifier()`, `Set_Damage_Modifier(float)`
- `Disable_Capture(bool)`, `Is_Capturable()`
- Many more — see Focumentation for complete list

### Additional Global Functions (~60)
- `Make_Ally(player1, player2)`, `Make_Enemy(player1, player2)`
- `Disable_Bombing_Run(bool)` — NOTE: parameter logic is REVERSED (pass false to disable)
- `Flash_GUI_Object(name)`, `Hide_GUI_Object(name)`
- `Lock_Controls(bool)`, `Unlock_Controls()`
- `Set_Cinematic_Camera_Key(pos, target, duration)`
- `Sleep(seconds)` — yields coroutine
- `Thread.Get_Current_Stage()`, `Thread.Create(...)`
- `SFXManager.Allow_Unit_Reponse_VO(bool)` — NOTE: engine typo "Reponse"

### Additional PlayerWrapper Methods (~20)
- `Make_Ally(player)`, `Make_Enemy(player)` — resets every game mode change!
- `Get_Faction()`, `Get_Name()`
- `Get_Space_Station_Level(planet)`
- `Get_Tech_Level()`
- `Disable_Orbital_Bombardment(bool)`

### FOW (Fog of War) Functions
- `FOWManager.Reveal_All(player)` — reveals entire map
- `FOWManager.Reveal(player, position, radius)`
- `FOWManager.Undo_Reveal_All(player)`

---

## 6. Behavioral Warnings (From Community Testing)

| Function | Warning |
|----------|---------|
| `Prevent_AI_Usage(true)` | Crashes in tactical if unit has no active AI |
| `Make_Ally` / `Make_Enemy` | Resets every game mode change |
| `Disable_Bombing_Run` | Parameter logic REVERSED (pass false to disable) |
| `Attack_Move` CommandBlock | Never finishes (engine bug) |
| `SFXManager.Allow_Unit_Reponse_VO` | Engine typo — "Reponse" not "Response" |

---

## 7. Example Pipe Commands

Once the pipe listener is running, send these from any external tool:

```bash
# Give 50000 credits
echo 'SWFOC_SetCredits(50000)' > \\.\pipe\swfoc_bridge

# Spawn an AT-AT at origin
echo 'Spawn_Unit(SWFOC_GetLocalPlayer(), Find_Object_Type("AT_AT"), Create_Position(0,0,0))' > \\.\pipe\swfoc_bridge

# Make all your units invincible
echo 'for _,u in pairs(Find_All_Objects_Of_Type(nil)) do if u:Get_Owner() == SWFOC_GetLocalPlayer() then u:Make_Invulnerable(true) end end' > \\.\pipe\swfoc_bridge

# Max tech
echo 'SWFOC_SetTechLevel(5)' > \\.\pipe\swfoc_bridge

# Execute any Lua file
echo 'dofile("my_script.lua")' > \\.\pipe\swfoc_bridge
```

---

## 8. Architecture: How It All Fits Together

```
┌─────────────────────────────────────────────────────────────────┐
│                    External Tools                                │
│  (.NET Trainer, CE, Python, CLI)                                │
│       │                                                          │
│       ▼                                                          │
│  \\.\pipe\swfoc_bridge  ──►  Named Pipe Thread (background)     │
│                                    │                             │
│                                    ▼                             │
│                              Command Queue                       │
│                                    │                             │
│                                    ▼                             │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │              Game Main Thread                            │    │
│  │  lua_open hook → drain queue → lua_load + lua_pcall     │    │
│  │                                                          │    │
│  │  17 Lua C API functions (pushstring, gettable, pcall...) │    │
│  │  10 SWFOC_* custom functions (SetCredits, DoString...)   │    │
│  │                                                          │    │
│  │  ──► Game's 405 Lua functions (Spawn_Unit, etc.)         │    │
│  │  ──► Game's engine API (SetHP, AddCredits, etc.)         │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  powrprof.dll (DLL proxy, side-loaded via search order)         │
└─────────────────────────────────────────────────────────────────┘
```

---

*Generated from binary RE (Ghidra static analysis) + community documentation (eaw-emmyluadoc, Focumentation)*
*48 Lua C API functions mapped, 134 game bindings confirmed, 271 community-documented*
*Total coverage: ~405 Lua functions accessible via the bridge*
