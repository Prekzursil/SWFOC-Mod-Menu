# Iter 100-113 LIVE wires — operator quick reference

**One-line example invocations for every LIVE flip from the 2026-04-28 master ralph loop session.**

Copy-paste any of these into:
- The editor's **Lua Playground tab**, or
- The bridge's `\\.\pipe\swfoc_bridge` named pipe directly via `echo` / Cheat Engine, or
- `SWFOC_DoString("...")` from inside an existing Lua script

All wires return either `OK: <method> dispatched (LIVE — engine Lua API)` on success or `ERR: <reason>` on failure.

---

## Speed control (iter 100)

```lua
-- Set per-unit speed (absolute override, in engine units)
return SWFOC_SetUnitSpeed(0xABCD, 250)

-- Read the current per-unit speed (returns -1 if no override active)
return SWFOC_GetUnitSpeed(0xABCD)

-- Set ALL units owned by slot 0 to absolute speed 350
return SWFOC_SetPerFactionSpeedMultiplier(0, 350)

-- Revert per-unit speed back to engine's natural max
return SWFOC_ClearUnitSpeedOverride(0xABCD)
```

`0xABCD` = the 64-bit obj_addr from the Tactical Units tab. Replace with your real address.

---

## Damage scaling (iter 96, editor backfilled iter 102)

```lua
-- Scale every incoming damage by 2.0× globally (Take_Damage_Outer detour)
return SWFOC_SetDamageMultiplierGlobal(2.0)

-- Read the current global multiplier
return SWFOC_GetDamageMultiplierGlobal()

-- Reset to normal damage
return SWFOC_SetDamageMultiplierGlobal(1.0)
```

---

## Camera (iter 107)

```lua
-- Galactic mode: scroll to a planet by name
return SWFOC_ScrollCameraToTarget('Find_Planet("Yavin")')

-- Tactical mode: scroll to a specific unit
return SWFOC_ScrollCameraToTarget('Find_First_Object("Empire_AT_AT")')

-- Scroll to the third Rebel Trooper Squad in the world
return SWFOC_ScrollCameraToTarget('Find_Object_Type("Rebel_Trooper_Squad")[2]')
```

Note: the outer call uses single quotes so the inner `Find_*` can use double quotes for its string argument (no escape needed). Lua accepts either quote style for string literals.

---

## Galactic — change unit ownership (iter 108)

```lua
-- Convert the first Empire AT-AT to the Rebel faction
return SWFOC_ChangeUnitOwner('Find_First_Object("Empire_AT_AT")', 'Find_Player("REBEL")')

-- Convert all Empire stormtroopers to Underworld (one at a time — repeat for each)
return SWFOC_ChangeUnitOwner('Find_First_Object("Empire_Stormtrooper_Squad")', 'Find_Player("UNDERWORLD")')
```

The engine's `Change_Owner` updates ownership, fires UI events, plays audio, processes corruption, AND updates AI budgets. Full "swap sides" engine behaviour.

---

## Spawning (iter 109)

```lua
-- Spawn a Rebel trooper squad at the origin
return SWFOC_SpawnUnitLua(
    'Find_Player("REBEL")',
    'Find_Object_Type("Rebel_Trooper_Squad")',
    'Create_Position(0, 0, 0)')

-- Spawn an Empire AT-AT at coords (100, 200, 0)
return SWFOC_SpawnUnitLua(
    'Find_Player("EMPIRE")',
    'Find_Object_Type("Empire_AT_AT")',
    'Create_Position(100, 200, 0)')
```

---

## Per-unit toggles (iter 110, 111)

```lua
-- Make a unit invulnerable (propagates via BehaviorAttach to all hardpoints)
return SWFOC_MakeUnitInvulnLua('Find_First_Object("Empire_AT_AT")', 'true')

-- Revert
return SWFOC_MakeUnitInvulnLua('Find_First_Object("Empire_AT_AT")', 'false')

-- Hide a unit (visibility toggle, doesn't remove from world)
return SWFOC_HideUnitLua('Find_First_Object("Empire_AT_AT")', 'true')

-- Lock the AI away from controlling a unit
return SWFOC_PreventAiUsageLua('Find_First_Object("Empire_AT_AT")', 'true')

-- Make a unit non-selectable (operator can still see it but can't click it)
return SWFOC_SetUnitSelectableLua('Find_First_Object("Empire_AT_AT")', 'false')
```

---

## Per-unit actions (iter 112)

```lua
-- Despawn (cleanly remove)
return SWFOC_DespawnUnitLua('Find_First_Object("Empire_AT_AT")')

-- Stop current action (interrupt move/attack)
return SWFOC_StopUnitLua('Find_First_Object("Empire_AT_AT")')

-- Retreat (engine flees the unit toward safety)
return SWFOC_RetreatUnitLua('Find_First_Object("Rebel_Trooper_Squad")')
```

---

## Universal escape hatch (iter 113)

For any engine Lua method NOT explicitly catalogued — calls
`(<obj>):<method>(<args>)` via DoString.

```lua
-- Give a player money
return SWFOC_CallObjMethodLua('Find_Player("REBEL")', 'Give_Money', '5000')

-- Heal a unit (engine method — no args)
return SWFOC_CallObjMethodLua('Find_First_Object("Empire_AT_AT")', 'Heal', '')

-- Enable a behavior on a unit (multi-arg)
return SWFOC_CallObjMethodLua(
    'Find_First_Object("Empire_AT_AT")',
    'Enable_Behavior',
    '"INVULNERABLE", true')

-- Set a player's tech level
return SWFOC_CallObjMethodLua('Find_Player("REBEL")', 'Set_Tech_Level', '5')

-- Activate a faction superweapon
return SWFOC_CallObjMethodLua('Find_Player("EMPIRE")', 'Activate_Power', '"DEATH_STAR"')
```

---

## Live-test priority

If you can only test 5 things to validate the session, do these in order:

1. **`SWFOC_SetUnitSpeed`** — easiest to verify: pick any unit, set speed 250, watch it walk much faster.
2. **`SWFOC_SetDamageMultiplierGlobal(2.0)`** — take a swing at any unit, watch HP drop double.
3. **`SWFOC_ScrollCameraToTarget('Find_Planet("Yavin")')`** — galactic-mode camera pan.
4. **`SWFOC_ChangeUnitOwner(...)`** — convert an enemy unit, watch UI events fire.
5. **`SWFOC_CallObjMethodLua('Find_Player("REBEL")', 'Give_Money', '5000')`** — credits jump 5000, validates the universal escape hatch.

If any fail, capture the bridge log line — the bridge already records `ERR: <method> raised engine error rc=<n>` on failure with the full DoString source that was dispatched.

---

## Deferred — XML-attribute-only family (still PHASE 2 PENDING)

These engine fields are baked from XML at unit construction; no runtime setter exists. They need either RTTI dissection of the per-unit struct OR a MinHook on the relevant tick/update path. Documented for future arc — don't try to call them yet.

- `SWFOC_SetFireRate` (iter 101 finding)
- `SWFOC_SetHeroRespawnTimer` (iter 104)
- `SWFOC_SetPermadeath` (iter 104)
- `SWFOC_SetUnitShield` (iter 105)
- `SWFOC_SetGameSpeed` (no engine helper pinned)
- `SWFOC_FreeCam` (no engine `Free_Cam` Lua API)
- `SWFOC_SetCameraPos` (engine API takes Lua position-userdata, not raw floats)

---

## Native UX surfaces in the editor (iter 117-119, 2026-04-29)

You no longer have to paste these wires into the Lua Playground — every iter 100-113 wire now has a native button somewhere in the editor. The Playground preset menu (iter 116) is still there as a power-user shortcut, but the per-tab buttons are the recommended path for routine operator workflows.

### Unit Control tab — "Selected Unit Lua Actions (iter 117-118 LIVE)" GroupBox

Paste a unit-handle expression (e.g. `Find_First_Object("Empire_AT_AT")`) into the first TextBox once. Then click any of these buttons:

- **Make invuln ON / OFF** → iter 110 (`SWFOC_MakeUnitInvulnLua`)
- **Hide ON / OFF** → iter 111 (`SWFOC_HideUnitLua`)
- **Lock from AI / Unlock to AI** → iter 111 (`SWFOC_PreventAiUsageLua`)
- **Selectable ON / OFF** → iter 111 (`SWFOC_SetUnitSelectableLua`)
- **Despawn / Stop / Retreat** → iter 112 (`SWFOC_DespawnUnitLua` / `StopUnitLua` / `RetreatUnitLua`)
- **Change unit owner →** (separate row, takes a 2nd TextBox for `Find_Player(...)`) → iter 108 (`SWFOC_ChangeUnitOwner`)

### Spawning tab — "Spawn unit via Lua (iter 119 LIVE)" GroupBox

Three TextBoxes for player / type / position Lua expressions, plus a button. This is the **LIVE alternative** to the existing PHASE 2 PENDING `SWFOC_SpawnUnit` Phase-1-mirror Spawn button — operators can finally spawn units that genuinely appear in the running game.

- **Player Lua**: `Find_Player("REBEL")`
- **Type Lua**: `Find_Object_Type("Rebel_Trooper_Squad")`
- **Position Lua**: `Create_Position(0, 0, 0)`
- Click **Spawn (Lua, LIVE) →** → iter 109 (`SWFOC_SpawnUnitLua`)

### Wires that already had earlier UX surfaces

- **Speed (iter 100)** — Speed tab "Apply (per-unit)" / per-faction / Revert buttons (iter 100/102)
- **Damage (iter 96/97/102)** — Combat tab "Apply (GLOBAL)" / "Apply (per-slot)" buttons (iter 102)
- **Camera (iter 107)** — Camera & Debug tab "Scroll camera to target" GroupBox (iter 107)
- **Universal escape hatch (iter 113, `SWFOC_CallObjMethodLua`)** — intentionally kept Lua-Playground-only. Too generic to deserve a dedicated button surface; lives in the iter 116 preset menu and is the documented "everything else" tool.

### Wire-format pin tests

Each iter 117/118/119 dispatcher method has a wire-format regression test that pins the exact `SWFOC_X('expr1', 'expr2'...)` shape:

- `tests/SwfocTrainer.Tests/Regression/Iter117UnitLuaCallShapeTests.cs` (10 cases)
- `tests/SwfocTrainer.Tests/Regression/Iter118ChangeUnitOwnerShapeTests.cs` (5 cases)
- `tests/SwfocTrainer.Tests/Regression/Iter119SpawnUnitLuaShapeTests.cs` (4 cases)

If a future bridge-side rename or quote-style change drifts the wire format, these fire at the dispatcher boundary instead of waiting for a runtime mismatch in the live game.
