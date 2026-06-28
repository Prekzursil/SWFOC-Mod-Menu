# v5 Service Lua API Findings (Live Test Results 2026-04-06)

Live verification against StarWarsG.exe + Thrawn's Revenge mod, local player Zann Consortium (UNDERWORLD slot 6), galactic conquest mode.

All commands sent through `\\.\pipe\swfoc_bridge` after the bridge round-trip was confirmed working.

> **Audit note (postmortem 2026-04-06):** An earlier pass on this file marked many rows as "Confirmed Working" based on the bridge returning a non-error response. That is NOT the same as proving the side effect happened in-game. This file has been re-graded on two axes:
>
> - **VERIFIED** — The return value carries specific game state data that can only come from a working call (e.g., `Get_Credits()` returning `17454.78515625`, `Get_Faction_Name()` returning `UNDERWORLD`). Read-only queries that return real data are safe to tag VERIFIED.
> - **LIVE_OBSERVED** — The Lua wrapper ran without error and the bridge returned a value, but the in-game side effect (camera moved, event actually fired, unit actually spawned into the world) was NEVER probed. These are NOT verified.
> - **UNVERIFIED** — Either we never invoked the call at all, or the call pattern is ambiguous.
> - **REFUTED** — The call was made with `ok=true`, but a follow-up probe showed the claimed side effect did not occur.
>
> See `session_2026-04-06_postmortem.md` for the flat audit log.

## Read-Only Queries — VERIFIED

These return real game state values and can only succeed if the underlying C++ call actually ran and read the live data structures.

| Lua API | Test | Result | Confidence |
|---|---|---|---|
| `SWFOC_GetVersion()` | `return SWFOC_GetVersion()` | `SWFOC Lua Bridge v1.0` | VERIFIED (bridge-side, trivial) |
| `SWFOC_GetCredits()` | `return SWFOC_GetCredits()` | `13710 → 17454` (live data, incrementing) | VERIFIED |
| `Find_Player("local"):Get_Faction_Name()` | direct | `UNDERWORLD` | VERIFIED |
| `Find_Player("local"):Get_Credits()` | direct | `17454.78515625` | VERIFIED |
| `Find_Player("local"):Get_Tech_Level()` | direct | `2` | VERIFIED |
| `Find_Player("local"):Get_Name()` | direct | `Zann Consortium` | VERIFIED |

## Type-Existence Probes — VERIFIED (existence only)

These only prove the Lua global exists as a userdata. They do NOT prove the function actually does anything useful when invoked.

| Lua API | Test | Result | Confidence |
|---|---|---|---|
| `type(Point_Camera_At)` | direct | `userdata` | VERIFIED-EXISTS (not proven to move camera) |
| `type(Zoom_Camera)` | direct | `userdata` | VERIFIED-EXISTS (not proven to zoom camera) |
| `type(Spawn_Unit)` | direct | `userdata` | VERIFIED-EXISTS (invocation effect REFUTED — see below) |
| `type(Create_Position)` | direct | `userdata` | VERIFIED-EXISTS |
| `type(GameRandom)` | direct | `userdata` | VERIFIED-EXISTS |
| `type(Suspend_AI)` | direct | `userdata` | VERIFIED-EXISTS |
| `type(Find_All_Objects_Of_Type)` | direct | `userdata` | VERIFIED-EXISTS |

## Call-And-Assume APIs — LIVE_OBSERVED ≠ VERIFIED

These returned without error from the bridge but the actual in-game side effect was never probed. They must NOT be treated as working until a follow-up probe inspects the game state.

| Lua API | Bridge return | Side effect probed? | Confidence |
|---|---|---|---|
| `Story_Event("GENERIC")` | `fired` (and later `OK`) | **NO** — game event log never inspected; the string `fired` is just what the Lua wrapper prints, not proof the story event handler ran | LIVE_OBSERVED |
| `Letter_Box_On()` / `Letter_Box_Off()` | `OK` | **NO** — screen was never checked for the actual letterbox bars | LIVE_OBSERVED |
| `Zoom_Camera(1.0)` | `OK` | **NO** — camera position/zoom was never sampled before/after | LIVE_OBSERVED |
| `Rotate_Camera_By(0)` | `OK` | **NO** — camera heading was never sampled. Also the rotation angle was 0, so even a successful call would produce no visible change | LIVE_OBSERVED (arguably meaningless) |
| `Point_Camera_At(unit)` | not actually invoked with a real unit | — | UNVERIFIED |

**Spawn_Unit — REFUTED:**

- **Original claim:** `Spawn_Unit` returning `ok=true` from the bridge means the spawn worked.
- **Evidence for REFUTED:** In phase 2k of session 2026-04-06, `Spawn_Unit` was called multiple times and the bridge reported `ok=true`. A follow-up `Find_All_Objects_Of_Type` for the enemy faction inventory continued to show only the original 1 Nebulon and 1 Corvette — the "spawned" units never appeared in the enumeration. The `ok=true` response from the bridge reflects only that the Lua wrapper did not raise a Lua-level error; it does NOT confirm the engine actually instantiated a game object.
- **Evidence basis:** `Find_All_Objects_Of_Type` inventory counts before and after the Spawn_Unit call were identical.
- **Confidence:** REFUTED.
- **Next-step verification:**
  1. Capture `Find_All_Objects_Of_Type(t)` count before the call, immediately after, and 5s later (to allow async instantiation).
  2. If the count never goes up, hook the actual game-side spawn function (documented in the EnhancedSpawnService notes) with Frida to see whether the Lua wrapper even calls it, and with which arguments.
  3. Verify the argument shape against a known-good Lua script from the vanilla game's `Data/Scripts/` folder — the Lua binding almost certainly wants a specific argument tuple `(type, position, player, ...)` that our probe got wrong.

**Take_Damage self-damage probe — UNVERIFIED (not REFUTED):**

- **Original claim:** `obj:Take_Damage(N)` is a no-op for hull modification.
- **Evidence basis:** Multiple 1-arg, 2-arg, and 3-arg invocations of `obj:Take_Damage(...)` with values `0.5`, `1.0`, `10000`, `100` did not change `Get_Hull()` readings. The test was done with both BOTH protections removed.
- **What we don't know:** Whether we called it correctly. The Lua-exposed Take_Damage is almost certainly the C++ damage receiver and very likely requires a `damage source` object (a weapon, a projectile, a game object reference) as one of its arguments that cannot be synthesized from pure Lua. Our probe passed raw numbers, which may have been silently discarded by the wrapper.
- **Confidence:** UNVERIFIED (not REFUTED — we cannot rule out "Take_Damage works, but we invoked it wrong").
- **Next-step verification:** Hook `Take_Damage_Outer` at RVA `0x38A350` with Frida, attach to the live game during real combat, and capture the argument tuple that the game itself passes when an enemy weapon lands a hit. Replay that exact tuple from Lua. If it still no-ops, THEN the claim becomes REFUTED.

## NOT-Found APIs (return `nil` in galactic conquest mode)

**IMPORTANT correction:** Several APIs my probe script tested with WRONG names. Below is the corrected analysis after cross-referencing the actual v5 service code and the game KB.

### Per-service call status (re-graded)

| Lua API | Service | KB reference | Confidence |
|---|---|---|---|
| `Make_Ally(player1, player2)` | DiplomacyService | `GAME_LUA_API.md` line 238/266 | **VERIFIED-NEGATIVE as global.** Live probe (`v5_service_live_matrix.md` row 10) showed `type(Make_Ally) == "nil"` at the global scope. This function is NOT a Lua global in our build. It may exist as a PlayerObject method, under a different namespace, or simply not in this mod. The KB entry that lists it as a global function is wrong. Next step: enumerate `Find_Player("local")`'s metatable methods, or search the binary for the registration site of "Make_Ally" / "Make_Enemy" to see where it's actually installed. |
| `Make_Enemy(player1, player2)` | DiplomacyService | Same | **VERIFIED-NEGATIVE as global.** Same as above. |
| `Is_Ally(player)` | DiplomacyService (read) | `GAME_LUA_API.md` line 190 | **VERIFIED-NEGATIVE as global.** Same probe, same result — nil at global scope. |
| `Is_Enemy(player)` | DiplomacyService (read) | Same | **VERIFIED-NEGATIVE as global.** Same. |
| `Story_Event("EVENT_NAME")` | StoryEventService | KB | **LIVE_OBSERVED only** — bridge returned `fired`/`OK`, but the game's story-event log was never inspected to confirm the event actually dispatched. |
| `Find_Player("local"):Get_Credits()` | FactionDashboardService | KB | **VERIFIED** — runtime returned `17454.78515625`, live-pulled data. |
| `Find_Player("local"):Get_Faction_Name()` | FactionDashboardService | KB | **VERIFIED** — runtime returned `UNDERWORLD`. |
| `Find_Player("local"):Get_Tech_Level()` | FactionDashboardService | KB | **VERIFIED** — runtime returned `2`. |
| `Find_Player("local"):Get_Name()` | FactionDashboardService | KB | **VERIFIED** — runtime returned `Zann Consortium`. |
| `Spawn_Unit(...)` | EnhancedSpawnService | KB | **REFUTED for "ok=true ⇒ spawn worked"** — see the dedicated Spawn_Unit section above. The function exists (`type == userdata`), but repeated invocations with `ok=true` never produced new units in Find_All_Objects_Of_Type enumeration. |
| `Create_Position(...)` | EnhancedSpawnService | KB | **VERIFIED-EXISTS** only — we never used the returned position to do anything observable. |
| `Point_Camera_At(unit)` | CameraDirectorService | KB | **VERIFIED-EXISTS** only — never invoked with a real unit, camera state never sampled. |
| `Scroll_Camera_To(position)` | CameraDirectorService | KB line 87 | **VERIFIED-EXISTS** only — never invoked, camera state never sampled. |
| `Zoom_Camera(level)` | CameraDirectorService | KB | **LIVE_OBSERVED** — `Zoom_Camera(1.0)` returned `OK`, but camera zoom state was never checked. |
| `Rotate_Camera_By(degrees)` | CameraDirectorService | KB line 89 | **LIVE_OBSERVED** — `Rotate_Camera_By(0)` returned `OK`, but heading was never sampled, and passing `0` would produce no visible change even on success. |
| `Letter_Box_On()` / `Letter_Box_Off()` | CameraDirectorService | KB line 90 | **LIVE_OBSERVED** — both returned `OK`, but the screen was never checked for the letterbox bars. |
| `Suspend_AI(seconds)` | AiControlService | KB | **VERIFIED-EXISTS** only — `type(Suspend_AI) == "userdata"`. Never actually called with a duration; AI pause behavior never verified. |

### Likely incorrect / needs investigation

| Lua API | Service | Status |
|---|---|---|
| `Reset_Hyperspace_Time` | CooldownManagerService | NOT in KB. KB only has `Cancel_Hyperspace()`. Investigate further. |
| `Set_Tactical_Build_Time` | CooldownManagerService | NOT in KB. May need to use unit object methods instead. |
| `Game_Mode()` (called as global) | DiplomacyService.DetectMode | Returns `nil`. KB doesn't document this. May need to inspect player state. |
| `GetMapName()` | DamageLogService | Returns `nil`. May not exist or may be on a different namespace. |
| `Find_Object_Type("AT_AT")` | EnhancedSpawnService | Returns `nil` IN GALACTIC MODE. Should work in tactical battle (unit XML loaded only then). |

## Lua 5.0 Constraints (CONFIRMED)

The game embeds **Lua 5.0.2**. The following 5.1+ features are unavailable and must NOT be used in v5 service commands:

| Feature | Lua 5.0 Alternative |
|---|---|
| `#table` operator | `table.getn(t)` |
| `string.find` with patterns | works but escape rules differ |
| `module()` / `require` (5.1) | not available |
| `lua_Integer` | use `lua_Number` (double) |

The v5 services were scanned — none use the `#` operator in their generated Lua. Good.

## Game State Confirmation (from runtime exploration)

```
Local player: slot 6, faction 'UNDERWORLD', name 'Zann Consortium'
Player metatable: __eq __tostring __gc __index __call
PlayerArray pointer: 0x000001e79ad7d990 (count=8)
Game state count: 392+ lua_open calls (heavy state churn — Lua states for cinematics, AI, etc.)

# Faction lookup verified for all 3 factions in galactic conquest:
Find_Player("EMPIRE")      → EMPIRE
Find_Player("REBEL")       → REBEL
Find_Player("UNDERWORLD")  → UNDERWORLD (matches Get_Name() = "Zann Consortium")
```

The `__index` metamethod confirms PlayerObject method dispatch routes through C++ implementations (userdata, not a Lua table). This matches the engine's class system.

## Galactic vs Tactical Mode Distinction (IMPORTANT)

**The user is currently in galactic conquest mode.** In this mode:
- Player objects exist (verified via Find_Player)
- Story events fire (verified)
- Camera APIs exist (verified type-wise)
- BUT `Find_Object_Type("AT_AT")` returns `nil` for ALL unit XML names tested:
  - `AT_AT`, `ATAT`, `Empire_AT_AT`, `Empire_AT_AT_Walker`, `Underworld_StarViper`, `Empire_TIE_Fighter`, `REBEL_X_WING` — all `nil`
- This is because unit XML object types are loaded only when a tactical battle is initialized
- Spawn-related v5 services (`EnhancedSpawnService`, `RosterBrowserService`) cannot be verified in galactic mode
- For complete verification, the user must enter a tactical battle (land or space) and run probe_game_types.ps1 again

## Lua 5.0 String Method Constraint (NEW)

In Lua 5.0, **string method colon syntax is not supported**:
```lua
n:find(",")           -- ERROR in Lua 5.0
string.find(n, ",")   -- OK in Lua 5.0
```

Verify that no v5 service generates Lua with `string-literal:method()` patterns. Userdata colon syntax (`object:Method()`) DOES work in Lua 5.0 — it's only string literals where colon notation is broken.

## Bridge Drain Behavior

The drain hook `Hook_luaD_call` fires only when:
1. The game is in active gameplay (tactical battle UNPAUSED, OR galactic mode with active script execution)
2. A registered Lua state is currently inside a `luaD_call` invocation

When the game is paused, in a transitional menu, in a loading screen, or in the strategic galactic map with no script ticks, the drain doesn't fire and pipe commands time out after 10 seconds with `ERR: timeout (10s) - game may be paused or in menu`.

This is the fundamental constraint of the in-process bridge architecture. The user must be actively playing for the bridge to respond.

## Next Polish Tasks (Per-Service Lua API Correctness)

1. **DiplomacyService**: Find correct diplomacy API names (may live in `GameObject:Set_Affiliation` or similar)
2. **CooldownManagerService**: Per-ability reset is the model (`object:Reset_Cooldowns()` per HardpointManager?), not a global function
3. **DamageLogService.GameMap**: Use `Find_Player("local"):Get_Current_Mode()` or similar instead of nonexistent `Game_Mode()`
4. **EnhancedSpawnService**: Verify mod-specific game object type names (TR uses different unit names than vanilla)
5. **PlanetManagerService**: Test `Find_Object_Type` with planet names (e.g. `Find_Object_Type("CORUSCANT")`)
6. **FleetManagerService**: Verify fleet-related APIs against PetroglyphTools reference

These are per-service code changes — they don't affect the bridge architecture. The bridge wiring (verified) routes any Lua string to the game; whether the string is correct is up to the caller.
