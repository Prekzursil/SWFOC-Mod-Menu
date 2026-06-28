# Wave 0 RVA Verification Results

Verified via Ghidra MCP decompilation on 2026-04-05.
Binary: StarWarsG.exe (x86_64, image base 0x140000000, Steam build).

---

## 1. lua_close (0x7B8A70)

**Status:** DENIED -- wrong address
**Actual function at 0x7B8A70:** Mid-function instruction inside `f_luaopen` (FUN_1407b8a20, range 0x7B8A20-0x7B8ADB). This is the Lua state initialization callback that allocates the stack (0x2D0 bytes = 45 TValues), call info array (0x180 bytes), sets `top`, `base`, `ci`, and initializes the main thread type tag to 8. Hooking at 0x7B8A70 crashes because it's not a function entry point.

**Real lua_close:** RVA **0x7B8890** (FUN_1407b8890, range 0x7B8890-0x7B8906)
- Signature: `void lua_close(lua_State* L)` -- single parameter
- Calls `luaF_close` (FUN_1407b9f90) to close open upvalues
- Calls `luaC_collectall` (FUN_1407bca50) to run full GC sweep
- Resets stack to base in a loop, calling finalizers via `luaD_rawrunprotected`
- Finally calls `close_state` (FUN_1407b8350) which frees:
  - Stack array at L+0x68 (size = L+0x70 << 4)
  - CallInfo array at L+0x80
  - String table via FUN_1407c3030
  - Global state at L+0x20 (size 0x188)
  - lua_State itself (size 0xD0)

**Key observations:**
- The ESTIMATED RVA was 0x50 bytes off -- it landed inside the state init function, not the close function
- lua_close is a small function (0x76 bytes) immediately before lua_open (0x7B8930)
- The crash when hooking was caused by hooking a non-function-boundary address

---

## 2. Take_Damage_Outer (0x38A350)

**Status:** CONFIRMED-RE
**Actual function:** `FUN_14038a350` -- Master damage routing function
**Signature:** `char Take_Damage_Outer(GameObj* obj, int damageType, byte applyDamage, float* damageParams, int sourceInfo, uint flags)`
**Returns:** char (bool) -- '\0' on failure, '\x01' on success

**Key observations:**
- Massive function (~58K chars decompiled, 1300+ lines) with a 56-case switch on damage type IDs
- Calls `FUN_1403986b0` (damage-type capability checker) **15 times** throughout the function
  - FUN_1403986b0 checks `obj[((damageType + 0x3d) * 2)]` -- a per-damage-type capability/vulnerability flag array
  - It also iterates hardpoints via QueryInterface(0x16) recursively
- Does NOT directly check offset 0x3A7 (InvulnFlag) -- instead uses the capability checker which is a higher-level abstraction
- Recursively calls itself on hardpoints: `FUN_14038a350(hardpoint, damageType, param_3, &local_68, ...)` with flag `0x3FFFFF`
- Early-exit guard: `if (damageType - 1 > 0x4B) return '\0'` -- max 75 damage types
- Routes through specialized handlers per damage type:
  - Cases 1,9,0x1D,0x2B,0x3A -> FUN_1403a4250 (generic damage)
  - Case 2 -> FUN_1403a1ce0 (specific damage type)
  - Case 3 -> destructor-based handler
  - Case 5 -> FUN_1403a1930 (shield damage with sub-check)
  - Cases 6,10,0xC,0x26,0x27 -> FUN_1403a4070 (energy/special damage)
  - Case 7 -> FUN_1403a3500
  - Case 0xB -> FUN_1403a3260
  - Case 0xE -> FUN_1403a1fb0
  - Case 0x11 -> reference list destructor
  - Many more cases for damage types up to 0x4B

**Correction to RE findings:** The "8 invulnerability checks" is more accurately "15 capability/vulnerability checks via FUN_1403986b0", which checks whether the object (and its hardpoints) can receive a given damage type. This is not a simple invuln flag check at +0x3A7 but a per-damage-type array lookup.

---

## 3. Death Handler (0x39BDB0)

**Status:** CONFIRMED-RE
**Actual function:** `FUN_14039bdb0` -- Object death/destruction handler
**Signature:** `void DeathHandler(GameObj* obj, int deathCause, GameObj* killer, void* deathEvent, int deathAnim, int ownerTransfer)`

**Key observations:**
- Sets death flag: `obj[0x26].byte |= 0x40` (marks object as dying/dead)
- Zeros out current damage counter: `obj[3].data = 0`
- Calls `FUN_140392160()` -- likely a game event notification
- Handles owner/killer tracking: reads killer's owner ID from `killer+0x58` and stores it
- Propagates death to associated structures at `obj+0x1C` (offset 0x348, 0x330)
- Calls `FUN_14038eb10` -- likely removes from combat/targeting lists
- Calls `FUN_140392600` if a global flag `DAT_140b305e9` is set (debug/logging)
- Calls `FUN_1405031a0` if ParentIndex != -1 (handles child object death)
- Iterates over hardpoint types (loop up to 2), calling `FUN_140374b50` with type ID 0x20
- Calls `FUN_1404d07e0` -- likely explosion/debris VFX
- Fires event via `FUN_140220ed0(eventSystem, obj+2, 0x25)` -- event type 0x25 = "unit destroyed"
- QueryInterface calls:
  - `QueryInterface(obj, 5)` -> calls `FUN_1404f3a20` (AI/behavior component cleanup)
  - `QueryInterface(obj, 4)` -> `FUN_140432780` (physics component)
  - `QueryInterface(obj, 5)` -> `FUN_1404f3bb0` (alternative behavior path)
  - `QueryInterface(obj, 0x16)` -> `FUN_140404c20` (hardpoint component)
- Recursively calls itself on child hardpoints: `FUN_14039bdb0(child, ...)`
- Pushes death record to a global vector at `DAT_140b15418` (position + death cause)
- Handles debris spawning: reads debris table at `obj_type+0xd48`, spawns debris objects via `FUN_14029f810`
- Copies owner ID to debris: `debris+0x58 = obj.ownerPlayerID`
- Calls destructor: `~DynamicVectorClass<GameObjectClass*>(obj, '\x01')` -- removes from global object list
- Hero respawn logic: checks `obj_type+0xd1` (is_hero flag), `obj+0x22+3` (respawn enabled), then calls `FUN_14029f270` (ScheduleRespawn)
- Final cleanup: calls `FUN_1402cc940` (add to respawn queue) and `FUN_1402d5290` for special unit death events

---

## 4. SetHP (0x3A89D0)

**Status:** CONFIRMED-RE
**Actual function:** `FUN_1403a89d0` -- HP setter with clamping and validation
**Signature:** `float SetHP(GameObj* obj, float newHP)`

**Key observations:**
- Reads current HP from `obj+0x5C` (matches GameObj.HP offset)
- Clamps input: `if (newHP < 0.0) newHP = 0.0` (no negative HP)
- Reads max HP from `FUN_1403727a0(obj+0x298, obj)` -- gets max HP from GameObjectType
- Clamps to max: `if (currentHP > maxHP) currentHP = maxHP`
- Writes final value to `obj+0x5C`
- Dirty flag: if HP changed AND object is the local player's unit (`obj+0x50 == DAT_140a286f0`), sets `obj+0x3A0 |= 1` (UI refresh flag)
- Debug warning: if final HP < 0.0 (should be impossible after clamp), prints:
  `"!!! %s(ID: %d, Owner: %S) Health == %f after call to Set_Health(%f) !!!\n"`
  - Reads object name from `obj+0x298+0xF8` (GameObjectType name string)
  - Reads owner name via `FUN_140294bc0(&PlayerListGlobal, obj+0x58)` then `player+0x28` (wide string)
- Returns final HP value

**Verdict:** This is indeed a simple HP setter. It does NOT propagate to hardpoints, does NOT check invulnerability, and does NOT trigger death. It's a low-level field setter with clamping.

---

## 5. Make_Invulnerable_Setter (0x3ABB80)

**Status:** DENIED -- this is NOT Make_Invulnerable_Setter
**Actual function:** `FUN_1403abb80` -- **Set_Position / Teleport function**
**Signature:** `void SetPosition(GameObj* obj, float position[3])`

**Key observations:**
- Takes a float[3] parameter (x, y, z coordinates)
- QueryInterface(obj, 1) -> gets physics/movement component
- Calls `FUN_140426d50(battleEngine, obj)` -- likely removes from spatial hash
- Fires event 0xB via `FUN_140220ed0` -- "object moved" event
- Gets current game mode via `DAT_140b15418` vtable call (+0xE0)
- If mode == 2 (space battle): adjusts Z coordinate by `FUN_1403973b0(obj)+0xDFC` (height offset)
- If `obj+0x348` byte != -1 (has hardpoints): iterates via QueryInterface(0x16), recursively calls itself on each hardpoint with offset-adjusted positions:
  - `child_pos.x = parent_pos.x + (child.x - parent.x)`
  - `child_pos.y = parent_pos.y + (child.y - parent.y)`
  - `child_pos.z = parent_pos.z + (child.z - parent.z)`
- Writes final position to `obj+0x78` (x), `obj+0x7C` (y), `obj+0x80` (z)
- Updates visual transform at `obj+0x110` (two copies: +0xA4/+0xA8/+0xAC and +0xB0/+0xB4/+0xB8)
- If physics component exists: calls vtable+0x2F8 (SetPhysicsPosition), updates pathfinding data
- Calls `FUN_1403ac530(obj, 1)` -- likely marks position dirty
- Calls `FUN_14039bcb0` -- likely updates collision/spatial index

**Verdict:** RVA 0x3ABB80 is a position/teleport setter, NOT the invulnerability setter. The hardpoint propagation via QueryInterface(0x16) is for position updates, not invulnerability propagation.

---

## Summary Table

| Function | RVA | Prior Status | New Status | Notes |
|----------|-----|-------------|------------|-------|
| lua_close | 0x7B8A70 | ESTIMATED | **DENIED** | Mid-function in f_luaopen. Real lua_close at **0x7B8890** |
| Take_Damage_Outer | 0x38A350 | (unverified) | **CONFIRMED-RE** | Massive damage router, 56 damage types, 15 capability checks |
| Death Handler | 0x39BDB0 | (unverified) | **CONFIRMED-RE** | Full death pipeline: flags, events, debris, hero respawn |
| SetHP | 0x3A89D0 | CONFIRMED | **CONFIRMED-RE** | Simple HP setter with clamping, no hardpoint propagation |
| Make_Invulnerable_Setter | 0x3ABB80 | CONFIRMED | **DENIED** | Actually SetPosition/Teleport, not invulnerability |

## Action Items

1. **lua_close**: Update rvas.h from 0x7B8A70 to **0x7B8890** with CONFIRMED-RE status
2. **Make_Invulnerable_Setter**: RVA 0x3ABB80 must be relabeled as `SetPosition`. The real invulnerability setter needs to be found separately (likely writes to `obj+0x3A7` or manipulates the per-damage-type capability array at `obj+((type+0x3D)*0x10)`)
3. **FUN_1403986b0** at RVA 0x3986B0 should be documented as `CanReceiveDamageType(obj, damageType)` -- the real invulnerability/vulnerability check function
