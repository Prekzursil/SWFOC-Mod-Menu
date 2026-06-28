# Static Analysis Q&A -- IDA Hex-Rays Decompiler

Binary: StarWarsG.exe (x86_64, image base 0x140000000)
Date: 2026-04-05
Tool: IDA Pro with Hex-Rays decompiler via MCP

---

## Q1: Does Find_All_Objects_Of_Type(nil) accept nil?

**Answer:** NO. Nil/no-argument will produce an error. The function requires at least one filter parameter and validates it strictly.

**Wrapper RVA:** `0x140540B20` (size: 0x558)

**Registration:** String `"Find_All_Objects_Of_Type"` at `0x140898DE8`, registered in `sub_140546C70` at `0x140547B4E` with vtable `FindAllObjectsOfType`.

**Evidence:**

The very first check after entering the function validates argument count:

```c
// a3 is the Lua argument vector; a3+16 = argv start, a3+24 = argv end
v5 = v3[2];  // argv start pointer
if ( !((v3[3] - v5) >> 3) )  // argc == 0?
{
    sub_140248190(a2, "FindAllObjectsOfType -- Expected a filter parameter.");
    return 0;
}
```

After that, each argument is processed in a loop. For each argument, the function attempts three type checks (in order):
1. **String type** (checks against `unk_140A157D0`) -- interpreted as a game object type name, looked up via `qword_140B31068` (GameObjectType registry) and `qword_140B31020` (category registry)
2. **Player object type** (checks against `unk_140A44270`) -- filters by player/faction owner
3. **PlayerWrapper type** (checks against `unk_140A44260`) -- also filters by player

If an argument matches none of these types:
```c
sub_140248190(v50, "FindAllObjectsOfType -- unknown filter parameter %d.", v10);
return 0;
```

After all filters are parsed, there is a final safety check:
```c
if ( !v9 && !v8 && v45 == -1 && v52 == -1 )
{
    sub_140248190(v50, "FindAllObjectsOfType -- Invalid parameters.  You must pass a filter.");
    return 0;
}
```

**Practical implication:** You MUST pass a valid type name string (e.g., `"Star_Destroyer"`) or a player object. Passing nil, no arguments, or an invalid type will error. There is no "get all objects" shortcut.

**Filter system detail:** The function supports two separate bitmask filters stored at object offsets +5712 and +5704, corresponding to the GameObjectType registry (`qword_140B31068`) and the category registry (`qword_140B31020`). Multiple filters can be combined by passing multiple arguments.

---

## Q2: What does Set_Cannot_Be_Killed(true) write?

**Answer:** Writes bit 7 (0x80) of the byte at offset +929 (`0x3A1`) from the game object. `true` sets the high bit; `false` clears it. Also propagates to all objects in the same group (fleet/garrison).

**Wrapper RVA:** `0x140580010` (size: 0x164)

**Registration:** String `"Set_Cannot_Be_Killed"` at `0x14089AF18`, registered in `sub_14056D4C0`.

**Evidence:**

Core write logic after argument validation:

```c
// a1+96 = the wrapped C++ game object pointer
v10 = *(_BYTE **)(a1 + 96);

// Check if object byte at +840 (0x348) is 0xFF -- meaning "standalone" object
if ( v10[840] == 0xFF )
    goto LABEL_16;  // skip group iteration, write directly

// Otherwise iterate over group members via vtable call (type 22)
v11 = (*(__int64 (__fastcall **)(ptr, int))(vtable + 16))(v10, 22);
v12 = v11;  // group/fleet object
if ( v11 )
{
    v13 = 0;
    if ( (int)sub_140405300(v11) > 0 )  // get group member count
    {
        do
        {
            v14 = sub_1404052D0(v12, v13);  // get member at index
            if ( v14 )
                // THE KEY WRITE: offset +929 (0x3A1), bit 7 (0x80)
                *(_BYTE *)(v14 + 929) = *(_BYTE *)(v14 + 929) & 0x7F
                                      | (boolParam != 0 ? 0x80 : 0);
            ++v13;
        }
        while ( v13 < (int)sub_140405300(v12) );
    }
}

// LABEL_16: Also write to the object itself
*(_BYTE *)(*(_QWORD *)(a1 + 96) + 929LL) =
    *(_BYTE *)(*(_QWORD *)(a1 + 96) + 929LL) & 0x7F
    | (boolParam != 0 ? 0x80 : 0);
```

**Summary:**
| Detail | Value |
|--------|-------|
| Object field offset | +929 (0x3A1) |
| Bit position | Bit 7 (mask 0x80) |
| true value | Sets bit 7 (OR 0x80) |
| false value | Clears bit 7 (AND 0x7F) |
| Group propagation | Yes -- iterates fleet/garrison via vtable slot 2 (type 22), writes to each member |
| Byte 840 check | If `obj[840] == 0xFF`, skips group iteration (standalone unit) |

**For trainer/bridge use:** To replicate `Set_Cannot_Be_Killed(true)`, write `obj_ptr[0x3A1] |= 0x80`. To undo, write `obj_ptr[0x3A1] &= 0x7F`.

---

## Q3: Does Make_Invulnerable(true) set a timer?

**Answer:** NO timer. Make_Invulnerable creates (or removes) an "INVULNERABLE" behavior object that persists indefinitely until explicitly disabled by calling `Make_Invulnerable(false)`. However, it is TACTICAL-ONLY.

**Wrapper RVA:** `0x14057D550` (size: 0x316)

**Registration:** String `"Make_Invulnerable"` at `0x14089ADE0`, registered in `sub_14056D4C0`.

**Evidence:**

**Tactical mode gate:**
```c
// First check: must be in tactical mode
if ( qword_140B15418
  && (*(unsigned __int8 (__fastcall **)(ptr))(vtable + 240))(qword_140B15418) )
{
    // proceed...
}
else
{
    sub_140248190(a2, "GameObjectWrapper::Make_Invulnerable -- "
                      "this function may only be used in tactical modes.");
    return 0;
}
```

**Setting invulnerable (true path):**

When `boolParam` is true and the object's byte at +893 (`0x37D`) equals -1 (0xFF, meaning no existing INVULNERABLE behavior):

```c
if ( v8[16] )  // boolParam == true
{
    if ( v21 == -1 )  // obj[893] == 0xFF -- no INVULNERABLE behavior yet
    {
        // Create the behavior by name lookup
        strcpy(v25, "INVULNERABLE");
        v22 = sub_1404C3520(v25);       // Look up behavior by name in global table
        sub_14038C570(obj, v22, 0);      // Attach behavior to object (third param = 0)
    }
    return 0;
}
```

The behavior creation function `sub_1404C3520` does:
1. Copies the string "INVULNERABLE", converts to uppercase via `strupr()`
2. Searches a global behavior registry table at `off_140A2AC90` (linear scan)
3. Calls the matched factory function to create the behavior object
4. The behavior is then attached to the game object via `sub_14038C570`

**Key: The third parameter to `sub_14038C570` is `0` (not a duration).** There is no timer, no countdown, no frame-limited duration. The behavior persists until removed.

**Removing invulnerable (false path):**

When `boolParam` is false and a behavior IS present (byte 893 != -1):

```c
// false path
if ( v21 != -1 )  // behavior exists
{
    v24 = (*(__int64 (__fastcall **)(ptr, int))(vtable + 16))(v10, 75);  // get behavior #75
    sub_1403A54C0(obj, v24);  // Remove behavior from object
}
```

`sub_1403A54C0` removes the behavior from the object's behavior array at offset +632, cleans up tracking indices, and releases the behavior object.

**Group propagation:** Like Set_Cannot_Be_Killed, this function checks `obj[840]`:
- If `obj[840] != 0xFF` (has a group), iterates all group members via vtable slot 2 (type 22) and applies the same invulnerable behavior to each
- If `obj[840] == 0xFF` (standalone), applies to the object only

**Summary:**
| Detail | Value |
|--------|-------|
| Timer/duration | NONE -- permanent until explicitly disabled |
| Behavior name | "INVULNERABLE" |
| Behavior tracking | Byte at offset +893 (0x37D); 0xFF = no behavior, other = behavior slot index |
| Behavior array | Object offset +632 (0x278) |
| Mode restriction | Tactical only (space/land battles) |
| Group propagation | Yes -- same as Set_Cannot_Be_Killed |
| Enable mechanism | Creates behavior object via name lookup + attaches |
| Disable mechanism | Looks up behavior slot 75, removes from array |

---

## Q4: What does Find_Player("local") accept?

**Answer:** Accepts a STRING parameter. The special value `"local"` returns the local human player. Any other string is treated as a faction name (e.g., `"Empire"`, `"Rebel"`). Does NOT accept numeric indices. Case-insensitive for `"local"`.

**Wrapper RVA:** `0x1406A4820` (size: 0x159)

**Evidence:**

**Argument validation -- string only:**
```c
// Type check: walks inheritance chain looking for unk_140A157D0 (string type)
// If argument is nil or not a string:
sub_140248190(a2, "Find_Player -- invalid type for parameter 1.  Expected string.");
return 0;
```

**"local" special case (case-insensitive):**
```c
v12 = v8 + 16;  // raw string pointer from Lua argument
if ( *((_QWORD *)v8 + 5) >= 0x10u )
    v12 = *(const char **)v11;  // SSO: if length >= 16, dereference pointer

if ( !stricmp(v12, "local") )  // CASE-INSENSITIVE comparison
{
    v13 = sub_140294A40(&qword_140A16FD0);  // Get local player from PlayerList
    // -> wraps in Lua object and returns
    goto LABEL_15;
}
```

**Faction name path:**
```c
// Not "local" -- look up as faction name
v15 = sub_140331C40(qword_140B310B8, v8 + 16);  // Faction registry lookup
if ( v15 )
{
    v13 = sub_140294D30(&qword_140A16FD0, v15);  // Get player by faction
    if ( !v13 )
    {
        v10 = 0;  // faction exists but no player owns it
        goto LABEL_9;
    }
    goto LABEL_15;  // wrap and return
}

// Faction not found
sub_140248190(a2, "Find_Player -- unknown faction %s.", v11);
return 0;
```

**Summary:**
| Input | Behavior |
|-------|----------|
| `"local"` | Returns local human player (case-insensitive via `stricmp`) |
| `"Empire"` | Looks up faction name in `qword_140B310B8`, returns owning player |
| `"Rebel"` | Same faction lookup |
| Any other valid faction string | Same faction lookup |
| Unknown faction string | Error: `"Find_Player -- unknown faction %s."` |
| nil or non-string | Error: `"Expected string"` |
| Number/integer | Error: `"Expected string"` (no numeric index support) |

**Key globals:**
- `qword_140A16FD0` -- PlayerList/PlayerManager object
- `sub_140294A40` -- Get local player from PlayerList
- `sub_140294D30` -- Get player by faction from PlayerList
- `qword_140B310B8` -- Faction name registry

---

## Q5: How does FogOfWar.Reveal_All() work?

**Answer:** `FogOfWar.Reveal_All` is a method on a `LuaFOWRevealCommandClass` Lua table object (accessed as `FogOfWar.Reveal_All(player)`). It takes exactly ONE parameter: a player object. It reveals the entire fog-of-war map for that player. Tactical mode only.

**Class constructor RVA:** `0x1406A51B0` (size: 0x16C)
**Reveal_All implementation RVA:** `0x1406A5B00` (size: 0xE9)

**Registration:** The `FogOfWar` string at `0x140898E68` is registered in `sub_140546C70` at `0x140547CB3`. The constructor at `0x1406A51B0` creates a `LuaFOWRevealCommandClass` object with these methods:

```c
*a1 = &LuaFOWRevealCommandClass::`vftable';

// Register 4 methods on this Lua table:
sub_14024BE40(a1, "Reveal",             sub_1406A5700);  // Reveal(player, object [, radius])
sub_14024BE40(a1, "Reveal_All",         sub_1406A5B00);  // Reveal_All(player)
sub_14024BE40(a1, "Disable_Rendering",  sub_1406A53B0);  // Disable_Rendering(...)
sub_14024BE40(a1, "Temporary_Reveal",   sub_1406A5CF0);  // Temporary_Reveal(...)
```

**Reveal_All implementation (`sub_1406A5B00`):**

```c
__int64 __fastcall sub_1406A5B00(__int64 a1, __int64 a2, __int64 a3)
{
    // Gate: tactical mode only
    if ( !(*(uint8 (__fastcall **)(ptr))(vtable + 240))(qword_140B15418) )
    {
        sub_140248190(a2, "LuaFOWRevealCommandClass -- "
                          "Command only valid in a tactical game.");
        return 0;
    }

    // Argument check: requires at least 1 parameter
    v6 = *(ptr**)(a3 + 16);  // argv
    if ( !((__int64)(*(_QWORD *)(a3 + 24) - (_QWORD)v6) >> 3) )
    {
        sub_140248190(a2, "LuaFOWRevealCommandClass -- "
                          "Requires at least 1 parameters: (player).");
        return 0;
    }

    // Type check: first arg must be a PlayerWrapper (unk_140A44260)
    v7 = *v6;
    // ... walks type hierarchy checking against unk_140A44260 ...
    // On type mismatch:
    sub_140248190(a2, "LuaFOWRevealCommandClass -- "
                      "Expected player object as first parameter.");
    return 0;

    // SUCCESS path:
    (**(void (***)(...))v7)(v7);  // AddRef on player object
    v9 = std::ios_base::width(v7);  // Extract raw C++ player pointer
    // THE KEY CALL: reveal all FOW for this player
    sub_14035D4F0(qword_140B15418, *(unsigned int *)(v9 + 76));
    (*(void (**)(...))(vtable + 8))(v7);  // Release player object
    return 0;
}
```

**The reveal function `sub_14035D4F0`:**
```c
__int64 __fastcall sub_14035D4F0(__int64 gameMode, int playerIndex)
{
    // Call vtable[72] (offset 576) to validate game mode
    result = (*(vtable + 576))(gameMode);
    if ( (uint8)result )
    {
        v5 = *(ptr*)(gameMode + 408);  // FOW player array
        if ( v5 )
        {
            if ( playerIndex >= 0 && playerIndex < *(int*)(gameMode + 400) )  // bounds check
            {
                if ( *(ptr*)(v5 + 8 * playerIndex) )  // player FOW slot exists
                    return sub_1404C1560();  // Execute the full FOW reveal
            }
        }
    }
    return result;
}
```

**Lua usage pattern:**
```lua
-- Get the local player
local player = Find_Player("local")
-- Reveal all fog of war for that player
FogOfWar.Reveal_All(player)
```

**Comparison with `FogOfWar.Reveal(player, object [, radius])`:**
The `Reveal` method (`sub_1406A5700`) takes 2-3+ parameters: `(player, gameObject [, radius])` and reveals FOW around a specific object's position. `Reveal_All` is the simpler global reveal.

**Summary:**
| Detail | Value |
|--------|-------|
| Lua access pattern | `FogOfWar.Reveal_All(player)` |
| Parameters | 1: player object (from `Find_Player`) |
| Mode restriction | Tactical only (space/land battles) |
| Player index extraction | Offset +76 from raw player pointer |
| FOW manager location | gameMode + 408 (array), gameMode + 400 (count) |
| Underlying call | `sub_1404C1560()` via `sub_14035D4F0` |
| Global game mode | `qword_140B15418` |

**All FogOfWar methods:**
| Method | RVA | Parameters |
|--------|-----|------------|
| `Reveal` | `0x1406A5700` | (player, object [, radius]) |
| `Reveal_All` | `0x1406A5B00` | (player) |
| `Disable_Rendering` | `0x1406A53B0` | (unknown) |
| `Temporary_Reveal` | `0x1406A5CF0` | (unknown) |

---

## Q9: Does `Make_Invulnerable(true) + Set_Cannot_Be_Killed(true)` actually protect the hull from incoming damage?

**Answer (HULL only):** YES — empirically confirmed via live runtime test on 2026-04-06. **Confidence: LIVE_OBSERVED (hull freeze)** — reproducibly observed via Get_Hull() samples over ~60s of active combat.

**Answer (HARDPOINTS):** UNVERIFIED. The earlier claim that "hardpoint propagation is confirmed YES" is over-claimed and has been corrected in the hardpoint section below. The Lua API does not expose any hardpoint enumeration on the userdata, so direct hardpoint-state inspection was not possible; the only evidence cited (the ship kept firing its weapons) is circumstantial, not a direct observation of per-hardpoint invuln flags.

### Test setup
- Game: SWFOC + Thrawn's Revenge mod, tactical space battle
- Local player: Underworld (Zann Consortium)
- Test target: `Vengeance_Frigate` (UNDERWORLD, the user's ship)
- Aggressors: `Nebulon_B_Frigate` (REBEL) and additional spawned units
- Bridge protocol: `\\.\pipe\swfoc_bridge` (powrprof.dll)

### Calling pattern (CORRECTED — these are NOT global functions)

```lua
local t = Find_Object_Type("Vengeance_Frigate")
local objs = Find_All_Objects_Of_Type(t)
for _,obj in pairs(objs) do
    obj:Make_Invulnerable(true)
    obj:Set_Cannot_Be_Killed(true)
end
```

**Critical correction to earlier docs:** `type(Make_Invulnerable)` returned `nil` as a global. Both methods are exposed ONLY as instance methods on game objects (dispatched via `__index` metamethod on userdata). The Lua wrapper at `0x57D550` documented in Q8 IS the per-object method binding, not a global.

### Empirical evidence

| Sample | Time | Our Vengeance (INVULN) | Enemy Nebulon (no protection) |
|---|---|---|---|
| 0 | 23:08:03 | hull=0.998000 | 0.5520 |
| 1 | 23:08:10 | hull=0.998000 | 0.3910 |
| 2 | 23:08:16 | hull=0.998000 | 0.2124 |
| 3 | 23:08:22 | hull=0.998000 | 0.1121 |
| 4 | 23:08:36 | hull=0.998000 | 0.0256 |
| 5 | 23:09:02 | hull=0.998000 | **DESTROYED** |

- Our hull was **frozen at 0.998000** for the entire ~60 seconds of active combat
- The Nebulon dropped from `0.5520 → 0` in the same window and was destroyed
- The Vengeance was firing on the Nebulon (Underworld AI engaged), proving its weapons (and thus hardpoints) were still functional
- Our hull NEVER decreased even by a fractional amount

### Hardpoint propagation status — UNVERIFIED

**Confidence: UNVERIFIED.** This is a correction of the original session's over-claim.

**Evidence basis (what was actually observed):**
- The Vengeance was still firing weapons at the Nebulon over the 60s window.
- That was treated as proof that hardpoints were "intact or protected," and the trailing section ("Hardpoint propagation: YES — confirmed") was written accordingly.
- No per-hardpoint data was ever read. The Lua API does NOT expose any per-hardpoint enumeration on the userdata: none of `Get_All_Hardpoints`, `Get_Hardpoints`, `Get_Sub_Object_List`, `Get_Hardpoint`, `Has_Damaged_Hardpoints`, `Get_Damaged_Hardpoints`, `Get_Hardpoint_Count`, `Find_Hardpoint`, `Get_Sub_Objects`, `Repair_All_Hardpoints` exist on the Vengeance_Frigate userdata.
- "Still firing" is circumstantial. A ship can continue firing while a subset of hardpoints are damaged or destroyed — weapons come from whichever hardpoints are still alive. Continuing fire does not prove each individual hardpoint has the invuln flag set.

**What was NOT observed:**
- The per-hardpoint `+0x3A7` invuln flag was never read directly.
- The hardpoint manager handle at `+0x348` was never walked.
- No hardpoint count or per-hardpoint hull was ever captured.
- No hardpoint was intentionally damaged by an enemy focus-fire test to see whether it degraded or stayed frozen.

**Next-step verification (how to actually prove the claim):**
1. Use Cheat Engine to resolve the game object pointer for the test ship (via the trainer's existing unit lookup), then:
   - Read byte at `obj + 0x3A7` to confirm the top-level invuln flag is set.
   - Dereference the hardpoint manager at `obj + 0x348`, walk its hardpoint array, and read byte `+0x3A7` on each hardpoint to confirm the INVULNERABLE behavior slot is present on every child.
2. Alternatively, hook `Take_Damage_Outer` at RVA `0x38A350` with Frida, filter events to the hardpoint objects of the test ship during focus fire, and confirm every incoming damage event is rejected / zeroed.
3. Alternatively, stage a focus-fire test against a single hardpoint (e.g., front shield generator) and sample whether the ship ever loses that hardpoint visually or through any side channel (ship roster, explosion sfx counter, etc).

Until any of those produces direct evidence, the hardpoint-propagation claim stays at UNVERIFIED.

### Failed Take_Damage probe (lessons learned)

`obj:Take_Damage(N)` from Lua is a **no-op** for hull modification. Tested values: `0.5`, `1.0`, `10000`, `100`. With BOTH protections removed and various 1-arg/2-arg/3-arg invocations, hull never changed. Hypothesis: the Lua-exposed `Take_Damage` is the C++ damage receiver entry point that requires a damage source object (a weapon/projectile reference) which Lua cannot synthesize. It is hooked at `0x38A350` (Take_Damage_Outer) per Q1 work; that hook fires on real combat damage events, not synthetic Lua calls.

### Methods present on Vengeance_Frigate userdata (verified runtime)

```
Make_Invulnerable    Set_Cannot_Be_Killed    Get_Hull            Get_Shield
Despawn              Get_Type                Get_Owner           Get_Position
Teleport             Set_Selectable          Take_Damage         (and more)
```

### Bridge log evidence

```
[Pipe] Received 142 bytes: ...obj:Make_Invulnerable(true)...obj:Set_Cannot_Be_Killed(true)...
[Pipe] Executing: ...
[Pipe] Execution OK: applied true

[Pipe] Received 89 bytes: ...obj:Get_Hull()...
[Pipe] Executing: ...
[Pipe] Execution OK: hull=0.998000 shield=0.000000
```

### Conclusion

`Make_Invulnerable(true) + Set_Cannot_Be_Killed(true)` is the canonical Lua-side incantation for **hull** invulnerability — LIVE_OBSERVED. It:
- Accepts the call without error
- Holds hull frozen across active combat
- Does NOT prevent the unit from continuing to fight back
- Survives multiple aggressors firing simultaneously

For trainer purposes (hull god mode), this is sufficient as a v1 implementation.

**Hardpoint-level behavior is UNVERIFIED** (see the "Hardpoint propagation status" section above). Do NOT publish trainer documentation that claims the Lua-level Make_Invulnerable call gives you per-hardpoint invulnerability until the CE / Frida next-step verification has been run.

---

## Cross-Reference: Key Offsets and Globals

### Game Object Offsets
| Offset | Size | Purpose | Used By |
|--------|------|---------|---------|
| +840 (0x348) | byte | Group flag (0xFF = standalone) | Set_Cannot_Be_Killed, Make_Invulnerable |
| +893 (0x37D) | byte | Behavior slot index for INVULNERABLE (0xFF = none) | Make_Invulnerable |
| +929 (0x3A1) | byte | Flags byte -- bit 7 = cannot_be_killed | Set_Cannot_Be_Killed |
| +632 (0x278) | ptr | Behavior array base | Make_Invulnerable (add/remove) |

### Key Globals
| Address | Purpose |
|---------|---------|
| `qword_140B15418` | Current game mode / tactical session pointer |
| `qword_140A16FD0` | PlayerList / PlayerManager |
| `qword_140B310B8` | Faction name registry |
| `qword_140B31068` | GameObjectType registry |
| `qword_140B31020` | Category bitmask registry |
| `off_140A2AC90` | Behavior name-to-factory table (INVULNERABLE etc.) |

### Trainer Implementation Notes

1. **God mode (Set_Cannot_Be_Killed):** Write `obj[0x3A1] |= 0x80` for each unit. Simplest to implement -- single byte write.
2. **God mode (Make_Invulnerable):** More complex -- requires creating a behavior object and attaching it. Only works in tactical. Prefer Set_Cannot_Be_Killed for galactic map god mode.
3. **Type discovery:** `Find_All_Objects_Of_Type` requires a valid type string. To enumerate all objects, iterate the game mode's object list at `gameMode+24 -> +72/+64` (linked list) directly.
4. **FOW reveal:** Call the underlying `sub_14035D4F0(gameMode, playerIndex)` directly via hook, or use Lua `FogOfWar.Reveal_All(Find_Player("local"))`.
5. **Find_Player("local"):** Safe to call, returns nil-wrapped 0 if no local player. Use `stricmp` semantics (case-insensitive).

---

## Q6: What is the REAL invulnerability setter?

**Answer:** There is no single C++ "invulnerability setter" function. The Lua wrapper `Make_Invulnerable` at RVA `0x14057D550` handles the full logic:
1. Validates argument (must be boolean, tactical mode only)
2. Checks if object has hardpoints (`obj+840 == 0xFF`)
3. If NO hardpoints: creates "INVULNERABLE" behavior via `sub_14038C570` (behavior system attachment)
4. If HAS hardpoints: iterates each hardpoint via `QueryInterface(22)` → `sub_1404052D0`, applies invulnerability behavior to each

The previously labeled `Make_Invulnerable_Setter` at `0x3ABB80` is actually `SetPosition/Teleport` — it propagates POSITION (not invulnerability) to hardpoints via the same QI(0x16) mechanism.

**Wrapper RVA:** `0x14057D550`
**Behavior creation:** `sub_14038C570`
**Hardpoint iteration:** `QueryInterface(22)` + `sub_1404052D0`

---

## Q7: Take_Damage_Outer confirmation

**Answer:** CONFIRMED at RVA `0x38A350`. IDA Hex-Rays decompilation shows a massive damage routing function with 56 damage type cases and 15 calls to `CanReceiveDamageType` (`0x3986B0`). It does NOT directly check the invulnerability flag at `obj+0x3A7` — instead it uses a per-type capability array. The invulnerability check happens earlier in the call chain or through the `CanReceiveDamageType` capability system.

---

## Q8: Make_Invulnerable complete call chain (IDA + Ghidra verified)

**Answer:** The Make_Invulnerable Lua binding at `0x14057D550` handles invulnerability fully, including hardpoint propagation. There is no single "setter" function — the logic is embedded in the Lua wrapper itself.

**Call chain:**
```
0x14057D550 (GameObjectWrapper::Make_Invulnerable Lua binding)
  |-- Validates: 1 boolean arg, tactical mode only
  |-- Gets wrapped C++ object from a1+96
  |-- Checks obj+840 (0x348) for hardpoint flag
  |
  |-- IF obj+840 == 0xFF (no hardpoints -- direct unit):
  |   |-- IF enabling (arg is true) AND obj+893 (0x37D) == -1 (no existing behavior):
  |   |   +-- Creates "INVULNERABLE" behavior string
  |   |       +-- Calls sub_14038C570(obj, behaviorObj, 0) -- attaches behavior
  |   +-- IF disabling (arg is false) AND obj+893 != -1:
  |       +-- Calls QueryInterface(obj, 75) then sub_1403A54C0 -- removes behavior
  |
  +-- IF obj+840 != 0xFF (HAS hardpoints -- capital ship/station):
      |-- Gets hardpoint manager via QueryInterface(obj, 22) [0x16]
      |-- Gets hardpoint count via sub_140405300(manager)
      +-- FOR each hardpoint (0 to count-1):
          |-- Gets hardpoint object via sub_1404052D0(manager, index)
          +-- Applies same enable/disable logic to each hardpoint:
              |-- Check hardpoint+893 for existing behavior
              |-- Create/remove INVULNERABLE behavior on hardpoint
              +-- Calls vtable method to refresh behavior state
```

**Conclusion (static analysis only):** The decompiled `Make_Invulnerable` wrapper at `0x14057D550` contains a loop that walks the group/hardpoint manager via `QueryInterface(obj, 22)` and `sub_1404052D0`, and calls the same behavior-attach path on each child. **What the code is supposed to do, per the decompile, is propagate the INVULNERABLE behavior to each hardpoint.**

**Hardpoint propagation confidence: STATIC-ANALYSIS only (UNVERIFIED at runtime).**

- Source: IDA Hex-Rays decompile of the wrapper.
- Evidence basis: the decompile shows the loop and the attach call. This is a code-path claim.
- What was NOT observed: no live test read `obj + 0x3A7` on any child hardpoint to confirm the flag was actually set after the Lua call, and no live test verified that an incoming damage event to a single hardpoint was actually rejected.
- Next-step verification: see the "Hardpoint propagation status — UNVERIFIED" section in Q9. Until the CE read or the Frida hook confirms the flag flip on each hardpoint, this sits at static-only.

**Key RVAs:**
- Lua wrapper: `0x57D550` (CONFIRMED-RE via IDA)
- Behavior attach: `0x38C570`
- Hardpoint manager QI: `QueryInterface(obj, 22)` at vtable+16
- Hardpoint get: `0x4052D0`
- Hardpoint count: `0x405300`
- Behavior remove dispatch: `0x3A54C0`
