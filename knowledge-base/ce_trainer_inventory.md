# SWFOC Cheat Engine Trainer Inventory

> **Purpose:** Authoritative inventory of every working trainer feature, hook strategy, and assembly cave in the SWFOC Cheat Engine trainer (`trainer/`), prepared for the C++ DLL bridge porting phase.
>
> **Source files inspected:**
> - `trainer/SWFOC_GUI_Trainer_v3.lua` (10-tab GUI, full direct-RVA + named-pipe trainer)
> - `trainer/SWFOC_GUI_Trainer_v3.CT` (Cheat Engine table -- contains the same v3 Lua script embedded inside `<AssemblerScript>{$lua}...{$asm}</AssemblerScript>`; no extra `aobscan`/`assemble` content beyond what is in the .lua)
> - `trainer/SWFOC_GUI_Trainer_v4.lua` (9-tab v4-alpha rewrite that delegates everything to the bridge / shared-memory ring buffer)
> - `trainer/god_mode.lua`, `trainer/fow_toggle.lua`, `trainer/dps_log.lua`, `trainer/triggers.lua`, `trainer/blueprints.lua`, `trainer/type_discovery.lua`, `trainer/lua_playground.lua`, `trainer/shared_cmd.lua`
>
> **CRITICAL FINDING:** This trainer family contains **NO `aobscan` calls anywhere**. Every CE feature is one of:
> 1. RVA-based memory read/write at `StarWarsG.exe + 0x...` offsets (resolved at runtime via `getAddress("StarWarsG.exe")`)
> 2. RVA-based `autoAssemble` injection (the SetHP cave is patched at a known RVA, **not** scanned)
> 3. A Lua bridge call routed through the named pipe `\\.\pipe\swfoc_bridge` (v3) or shared-memory command buffer `Local\SWFOC_Bridge_Cmd` (v4) into the in-game Lua VM
>
> The DLL bridge porting phase will therefore not need to translate AOB scans -- it needs to **port the same RVA-based hooks and the SetHP injection cave** into MinHook detours, and expose the Lua-bridge actions as native pipe commands so the editor can call them without going through CE.

---

## Section 1: Per-feature table

Columns:
- **Feature** -- user-visible action
- **Source** -- file:line where the implementation lives
- **Trigger / Hook** -- mechanism (memory write, AA injection, Lua bridge call, ring buffer poll, etc.)
- **AOB signature** -- AOB pattern if any (always `n/a` -- this trainer never AOB-scans)
- **Injection / RVA** -- absolute injection site (RVA from image base)
- **Replacement / Cave / Lua payload** -- exact bytes patched, full asm cave, or Lua snippet sent to the bridge
- **Side state** -- persistent state the feature relies on (timers, counters, original-bytes save)
- **Status** -- `working` | `experimental` | `disabled` | `placeholder`

### 1.1 Combat / Damage hooks (CE Auto-Assembler)

| Feature | Source | Trigger / Hook | AOB | RVA | Replacement / Cave | Side state | Status |
|---|---|---|---|---|---|---|---|
| **God Mode (SetHP cave)** | `SWFOC_GUI_Trainer_v3.lua:684-769` (`buildGodModeScript`) | `autoAssemble` injection at `StarWarsG.exe+0x3A89D0` (SetHP entry) | n/a (RVA-based, no scan) | `0x3A89D0` (`SetHP`) | full cave below (cave label `godcave`) | `gd_pa` qword = absolute address of `PlayerArray` global = `base + 0xA16FF0`; `state.godModeActive` flag | working |
| **One-Hit Kill (OHK)** | `SWFOC_GUI_Trainer_v3.lua:771-871` (`buildOHKScript`) | `autoAssemble` injection at `StarWarsG.exe+0x3A89D0` | n/a | `0x3A89D0` | full cave below (`ohkcave`) | `ohk_pa` qword `= base + 0xA16FF0`; `ohk_fz` float `= 0.0`; `state.ohkActive` flag | working |
| **God Mode + OHK Combined** | `SWFOC_GUI_Trainer_v3.lua:873-973` (`buildCombinedScript`) | `autoAssemble` injection at `StarWarsG.exe+0x3A89D0` | n/a | `0x3A89D0` | full cave below (`combocave`) | `cb_pa` qword + `cb_fz` float; `state.combinedActive` flag | working |
| **Disable any combat hook** | `SWFOC_GUI_Trainer_v3.lua:975-1004` (`disableAllCombat` / `getDisableScript`) | `autoAssemble` restore | n/a | `0x3A89D0` | restores original bytes `db 40 53 48 83 EC 60` (push rbx; sub rsp,60) and `dealloc`s the cave + globals | none | working |

#### God Mode cave (full assembly, verbatim from `SWFOC_GUI_Trainer_v3.lua:689-766`)

```asm
[ENABLE]
alloc(godcave,512)
alloc(gd_pa,8)

label(gd_walk)
label(gd_check)
label(gd_noprot)
label(gd_protect)
label(gd_orig)
label(gd_ret)

gd_pa:
  dq <PlayerArray_global_addr>     ; = base + 0xA16FF0 (writeable global, holds PlayerArray*)

godcave:
  push rax
  push rdx
  push r8
  mov rdx, rcx                      ; rcx = SetHP first arg = GameObject* (the unit being damaged)

gd_walk:                              ; walk parent chain via ParentIdx + Components
  movzx eax, byte ptr [rdx+335]       ; obj.ParentIdx (1 byte)
  cmp al, FF
  je gd_check                         ; 0xFF = no parent, this is the root unit
  mov rax, [rdx+278]                  ; obj.Components (qword pointer)
  test rax, rax
  jz gd_check
  movzx edx, byte ptr [rdx+335]
  mov rdx, [rax+rdx*8]                ; rdx = Components[ParentIdx]
  test rdx, rdx
  jz gd_check
  jmp gd_walk

gd_check:                             ; rdx now points to root unit; read OwnerID
  mov eax, [rdx+58]                   ; obj.OwnerID (int32 at +0x58)
  mov r8, [gd_pa]                     ; r8 = address of PlayerArray ptr global
  mov r8, [r8]                        ; r8 = actual PlayerArray base
  test r8, r8
  jz gd_noprot
  movsxd rdx, eax
  mov r8, [r8+rdx*8]                  ; r8 = PlayerArray[OwnerID]
  test r8, r8
  jz gd_noprot
  cmp byte ptr [r8+62], 01            ; PlayerObject.LocalPlayer (+0x62) == 1 ?
  pop r8
  pop rdx
  pop rax
  jne gd_orig                         ; not local -> let damage through

gd_protect:                           ; LOCAL player owns this unit -> block hull reduction
  movss xmm0, [rcx+5C]                ; current HP (obj+0x5C)
  ucomiss xmm0, xmm1                  ; xmm1 = new HP requested by SetHP
  jna gd_orig                         ; if new HP >= current, allow (heal/identity)
  ret                                 ; otherwise: bail out of SetHP entirely (block damage)

gd_noprot:
  pop r8
  pop rdx
  pop rax

gd_orig:
  push rbx
  sub rsp,60                          ; original SetHP prologue bytes
  jmp gd_ret

StarWarsG.exe+3A89D0:                 ; <-- the hook site
  jmp godcave
  nop
gd_ret:

[DISABLE]
StarWarsG.exe+3A89D0:
  db 40 53 48 83 EC 60                ; restore original 6-byte SetHP prologue

dealloc(godcave)
dealloc(gd_pa)
```

#### OHK cave (full assembly, verbatim from `SWFOC_GUI_Trainer_v3.lua:777-868`)

```asm
[ENABLE]
alloc(ohkcave,512)
alloc(ohk_pa,8)
alloc(ohk_fz,4)

label(ohk_walk) label(ohk_check) label(ohk_noown)
label(ohk_enemy) label(ohk_mine) label(ohk_orig) label(ohk_ret)

ohk_pa:
  dq <PlayerArray_global_addr>       ; = base + 0xA16FF0

ohk_fz:
  dd (float)0.0                      ; the "kill" value forced into xmm1 for enemies

ohkcave:
  push rax
  push rdx
  push r8
  mov rdx, rcx

ohk_walk:                            ; same parent walk as God Mode
  movzx eax, byte ptr [rdx+335]
  cmp al, FF
  je ohk_check
  mov rax, [rdx+278]
  test rax, rax
  jz ohk_check
  movzx edx, byte ptr [rdx+335]
  mov rdx, [rax+rdx*8]
  test rdx, rdx
  jz ohk_check
  jmp ohk_walk

ohk_check:
  mov eax, [rdx+58]                  ; OwnerID
  mov r8, [ohk_pa]
  mov r8, [r8]
  test r8, r8
  jz ohk_noown
  movsxd rdx, eax
  mov r8, [r8+rdx*8]
  test r8, r8
  jz ohk_noown
  cmp byte ptr [r8+62], 01           ; LocalPlayer flag
  pop r8
  pop rdx
  pop rax
  je ohk_mine                        ; local -> protect (NOP)
  jmp ohk_enemy                      ; not local -> kill instantly

ohk_noown:
  pop r8
  pop rdx
  pop rax

ohk_enemy:                           ; force xmm1 to 0.0 so SetHP writes 0 -> instant kill
  movss xmm0, [rcx+5C]
  ucomiss xmm0, xmm1
  jna ohk_orig
  movss xmm1, [ohk_fz]               ; xmm1 := 0.0
  jmp ohk_orig

ohk_mine:                            ; protect (same as God Mode)
  movss xmm0, [rcx+5C]
  ucomiss xmm0, xmm1
  jna ohk_orig
  ret                                ; bail SetHP -- our unit is invulnerable

ohk_orig:
  push rbx
  sub rsp,60
  jmp ohk_ret

StarWarsG.exe+3A89D0:
  jmp ohkcave
  nop
ohk_ret:

[DISABLE]
StarWarsG.exe+3A89D0:
  db 40 53 48 83 EC 60

dealloc(ohkcave)
dealloc(ohk_pa)
dealloc(ohk_fz)
```

#### Combined God+OHK cave (full assembly, verbatim from `SWFOC_GUI_Trainer_v3.lua:879-970`)

```asm
[ENABLE]
alloc(combocave,512)
alloc(cb_pa,8)
alloc(cb_fz,4)

label(cb_walk) label(cb_check) label(cb_noown)
label(cb_enemy) label(cb_mine) label(cb_orig) label(cb_ret)

cb_pa:
  dq <PlayerArray_global_addr>

cb_fz:
  dd (float)0.0

combocave:
  push rax
  push rdx
  push r8
  mov rdx, rcx

cb_walk:
  movzx eax, byte ptr [rdx+335]
  cmp al, FF
  je cb_check
  mov rax, [rdx+278]
  test rax, rax
  jz cb_check
  movzx edx, byte ptr [rdx+335]
  mov rdx, [rax+rdx*8]
  test rdx, rdx
  jz cb_check
  jmp cb_walk

cb_check:
  mov eax, [rdx+58]
  mov r8, [cb_pa]
  mov r8, [r8]
  test r8, r8
  jz cb_noown
  movsxd rdx, eax
  mov r8, [r8+rdx*8]
  test r8, r8
  jz cb_noown
  cmp byte ptr [r8+62], 01
  pop r8
  pop rdx
  pop rax
  je cb_mine
  jmp cb_enemy

cb_noown:
  pop r8
  pop rdx
  pop rax

cb_enemy:
  movss xmm0, [rcx+5C]
  ucomiss xmm0, xmm1
  jna cb_orig
  movss xmm1, [cb_fz]
  jmp cb_orig

cb_mine:
  movss xmm0, [rcx+5C]
  ucomiss xmm0, xmm1
  jna cb_orig
  ret

cb_orig:
  push rbx
  sub rsp,60
  jmp cb_ret

StarWarsG.exe+3A89D0:
  jmp combocave
  nop
cb_ret:

[DISABLE]
StarWarsG.exe+3A89D0:
  db 40 53 48 83 EC 60

dealloc(combocave)
dealloc(cb_pa)
dealloc(cb_fz)
```

> **Key insight for the C++ port:** all three caves are minor variations on the same hook -- they intercept `SetHP(GameObjectClass*, float)` at its 6-byte prologue (`40 53 48 83 EC 60` = `push rbx; sub rsp, 60h`), walk the parent-component chain (`obj+0x335` ParentIdx + `obj+0x278` Components ptr) up to the root unit, look up the unit's owner via `obj+0x58` OwnerID -> `PlayerArray[OwnerID]`, and check `PlayerObject+0x62 == 1` (`LocalPlayer` flag). Local-owned units are protected (early-return); for OHK, enemy-owned units have `xmm1` overwritten with `0.0`. The C++ port should implement this with a single MinHook detour on `SetHP` that branches on a mode flag (`MODE_NONE | MODE_GOD | MODE_OHK | MODE_GOD_AND_OHK`).

---

### 1.2 Direct memory write features (no hook -- just `writeFloat`/`writeInteger`)

| Feature | Source | Trigger | RVA / Offset | Operation | Side state | Status |
|---|---|---|---|---|---|---|
| **Set Credits** | `SWFOC_GUI_Trainer_v3.lua:506-517` | Button click | `PlayerObject + 0x70` (per-faction `PlayerObject*` resolved via `PlayerArray[slot]`) | `writeFloat(ptr+0x70, value)` | none | working |
| **Credit presets (10K..1M)** | `v3.lua:519-540` | Button | `PlayerObject + 0x70` | `writeFloat` | none | working |
| **Freeze Credits** | `v3.lua:543-565` + timer at `v3.lua:2134-2136` | Checkbox + 500ms timer rewrites stored value | `PlayerObject + 0x70` | rewrite stored `freezeCreditsVal` every 500ms | `state.freezeCredits`, `state.freezeCreditsVal`, `state.freezePlayerPtr` | working |
| **Uncap Max Credits** | `v3.lua:567-586` | Checkbox | `PlayerObject + 0x74` | save original, `writeFloat(ptr+0x74, 999999999.0)` | `state.origMaxCredits` | working |
| **Set Tech Level** | `v3.lua:603-614` | Button | `PlayerObject + 0x84` | `writeInteger(ptr+0x84, level)` (range 1..5) | none | working |
| **Drain Enemy Credits** | `v3.lua:622-636` | Button | iterates `PlayerArray`, writes `ptr+0x70` to 0 for each non-local playable | direct write loop | none | working |
| **Max Tech All** | `v3.lua:638-652` | Button | iterates `PlayerArray`, writes `ptr+0x84` to 5 | direct write loop | none | working |
| **Hero Respawn -> 0 (instant)** | `v3.lua:1169-1189` (Tab 3) and `v3.lua:1717-1722` (Tab 7) | Checkbox / button | `StarWarsG.exe + 0xB169F0` | save original, `writeFloat(base+0xB169F0, 0.0)` | `state.origRespawn`, `state.heroRespawnActive` | working |
| **Hero Respawn -> custom (slider 0..300)** | `v3.lua:1194-1215`, `v3.lua:1736-1760` | TrackBar + Apply | `StarWarsG.exe + 0xB169F0` | `writeFloat(base+0xB169F0, slider*1.0)` | none | working |
| **Hero Respawn -> 96 (default)** | `v3.lua:1724-1731` | Button | `StarWarsG.exe + 0xB169F0` | `writeFloat(base+0xB169F0, 96.0)` | none | working |
| **Drain All AI Credits (galactic)** | `v3.lua:1673-1687` | Button | iterates `PlayerArray`, writes `ptr+0x70` to 0 for each non-local | loop | none | working |
| **Give Credits to Faction (galactic)** | `v3.lua:1614-1626` | Button | `PlayerObject + 0x70` of selected slot | `safeReadFloat + value`, `safeWriteFloat` | `state.galacticFactionMap` | working |
| **Set Tech for Faction (galactic)** | `v3.lua:1656-1668` | Button | `PlayerObject + 0x84` of selected slot | `writeInteger` | `state.galacticFactionMap` | working |

#### Per-faction `PlayerObject` field offsets used by the trainer

| Field | Offset | Type | Verified note |
|---|---|---|---|
| `Playable` | `+0x37` | byte | Trainer checks `== 2`, but per `CLAUDE.md` this field does **NOT** behave as expected at runtime — see Open Questions |
| `SlotIndex` | `+0x48` | int32 | |
| `PlayerID` | `+0x4C` | int32 | |
| `TeamID` | `+0x54` | int32 | |
| `LocalPlayer` | `+0x62` | byte | `== 1` means human/local player (used by all combat caves) |
| `FactionName` | `+0x68` | qword (string ptr) | |
| `Credits` | `+0x70` | float | |
| `MaxCredits` | `+0x74` | float | |
| `TechLevel` | `+0x84` | int32 | |
| `MaxTechLevel` | `+0x88` | int32 | |
| `IsHuman` | `+0x108` | byte | |
| `AIPlayer` | `+0x360` | qword | |

#### `GameObject` field offsets used by the trainer

| Field | Offset | Type | Notes |
|---|---|---|---|
| `VTable` | `+0x00` | qword | matches `GOC_VTable` global at RVA `0x8661B8` for GameObjectClass instances |
| `ObjectID` | `+0x50` | int32 | |
| `OwnerID` | `+0x58` | int32 | |
| `HP` | `+0x5C` | float | the cave reads/writes this directly |
| `Components` | `+0x278` | qword | array of child component pointers |
| `ParentIdx` | `+0x335` | byte | `0xFF` = root |
| `StatusFlags` | `+0x3A0` | byte | |
| `PreventDeath` | `+0x3A1` | byte | bit `0x80` set by `Set_Cannot_Be_Killed(true)` |
| `InvulnFlag` | `+0x3A7` | byte | the inspector checkbox toggles this directly |
| `HardpointFlag` | `+0x348` | byte | |

---

### 1.3 Lua-bridge features (named pipe + Lua VM proxy)

These features do not touch the binary directly. They send a Lua snippet down `\\.\pipe\swfoc_bridge` (v3) or write it into the `Local\SWFOC_Bridge_Cmd` shared-memory command buffer (v4); the in-game Lua VM (hooked via `luaD_call`) executes the snippet on the game thread and writes a reply back. **The C++ port can shortcut these by exposing a native pipe verb that performs the same effect via direct memory access where possible.**

| Feature | Source | Lua snippet sent over the bridge | Status |
|---|---|---|---|
| **God Mode (hardpoint propagation)** | `god_mode.lua:23-52` (called by v4) | iterates 15 unit categories via `Find_Object_Type`, then `Find_All_Objects_Of_Type`, then for each unit owned by `Find_Player("local")` calls `unit:Make_Invulnerable(true)` and `unit:Set_Cannot_Be_Killed(true)`. Re-applies every 2 seconds (CE Lua timer in `god_mode_enable`). | working in v4 |
| **God Mode disable** | `god_mode.lua:74-103` | same iteration, calls `Make_Invulnerable(false)` + `Set_Cannot_Be_Killed(false)` | working |
| **FOW Reveal All** | `fow_toggle.lua:9-19` | `FOWManager.Reveal_All(Find_Player("local"))`, re-applied every 5s | working |
| **FOW Undo Reveal** | `fow_toggle.lua:34-46` | `FOWManager.Undo_Reveal_All(Find_Player("local"))` | working |
| **Give Credits via Lua** | `v4.lua:194-203` | `Find_Player("local"):Give_Money(N)` | working in v4 |
| **Get Credits via Lua** | `v4.lua:205-211` | `Find_Player("local"):Get_Credits()` | working in v4 |
| **Set Tech Level via Lua** | `v4.lua:240-249` | `Find_Player("local"):Set_Tech_Level(N)` | working in v4 |
| **Heal All My Units** | `v4.lua:308-336` | iterates units, `u:Set_Hull(u:Get_Max_Hull())` for owned units | working |
| **Kill All Enemies** | `v4.lua:338-364` | iterates units, `u:Take_Damage(99999)` for non-owned units | partially working — see Open Questions; `Take_Damage` from Lua is a no-op per `verified_facts.json:fact_lua_take_damage_noop` |
| **Suspend AI** | `v4.lua:366-372` | `Suspend_AI(1)` | working |
| **Resume AI** | `v4.lua:373-378` | `Suspend_AI(0)` | working |
| **Max Speed All** | `v4.lua:380-406` | iterates units, `u:Override_Max_Speed(500)` | working |
| **Reset Speed All** | `v4.lua:408-434` | iterates units, `u:Override_Max_Speed(0)` | working |
| **Spawn Unit (tactical)** | `v3.lua:1486-1518` and `v4.lua:586-616` | `Spawn_Unit(FindPlayer(slot), Find_Object_Type("name"), pos, count)` | working when bridge connected |
| **Spawn Unit (galactic)** | `v4.lua:618-640` | requires planet target -- not yet implemented; placeholder | placeholder |
| **Story Event Fire** | `v3.lua:1908-1937` | `Story_Event("EVENT_NAME")` | working |
| **Story preset: Give Credits** | `v3.lua:1947-1981` | `AddCredits(FindPlayer(0), N)` | working |
| **Story preset: Reveal All Fog** | `v3.lua:1948` | `Reveal_All_Fog_Of_War()` | working |
| **Type Discovery** | `type_discovery.lua:8-55` | iterates 15 categories + `Find_Object_Type` -> `Find_All_Objects_Of_Type` -> `obj:Get_Type():Get_Name()`, returns delimited list | working |
| **Triggers: credits_above / credits_below** | `triggers.lua:104-115` | `Find_Player("local"):Get_Credits()` polled every 500ms | working |
| **Triggers: unit_count_below** | `triggers.lua:116-141` | iterates owned units, returns count, polled every 500ms | working |
| **Triggers: pause_game** | `triggers.lua:153-154` | `Game_Speed(0)` | working |
| **Triggers: alert** | `triggers.lua:155-163` | `form_ref:BringToFront(); FlashWindow()` | working |
| **Triggers: execute_lua** | `triggers.lua:164-171` | arbitrary user-supplied Lua | working |
| **DPS Log: enable event capture** | `dps_log.lua:181-198` | sets bit 0 of `Local\SWFOC_Bridge_Events` shared-memory `+12` flags field; also sends `SWFOC_EventControl(true)` over bridge | working |
| **DPS Log: read events** | `dps_log.lua:57-108` | polls ring buffer at `Local\SWFOC_Bridge_Events` (64 KB, 16-byte header at offsets `WRITE_POS=0`, `READ_POS=4`, `EVENT_COUNT=8`, `FLAGS=12`, `RING=16`) -- DLL writes `EVT_HP_CHANGE (0x01)` and `EVT_UNIT_DIED (0x02)` events from a hook on `Take_Damage_Outer at 0x38A350` | working |
| **DPS Log: per-unit accumulator** | `dps_log.lua:113-131` | accumulates `dealt`/`taken`/`kills` per `unit_id` in CE-side dict | working |
| **DPS Log: CSV export** | `dps_log.lua:268-295` | dumps `event_log` + `unit_damage` to CSV | working |
| **Blueprints: save** | `blueprints.lua:15-113` | iterates units, captures `type_name`, `owner_faction`, `Get_Position()`, `Get_Hull()`, writes JSON | working |
| **Blueprints: load + spawn** | `blueprints.lua:118-167` | parses JSON, `Spawn_Unit(local_player, type, Create_Position(x,y,z))` per entry -- *spawns all under local player, original ownership lost* | working |
| **Lua Playground: arbitrary execute** | `lua_playground.lua:110-127` | sends arbitrary Lua text down the bridge, displays result | working |
| **Playground recipes** | `lua_playground.lua:11-108` | 9 canned recipes (give credits, kill enemies, reveal map, max tech, list units, heal all, suspend/resume AI) | working |

---

### 1.4 Inspector / read-only features

| Feature | Source | Reads | Status |
|---|---|---|---|
| **Live credits / max / tech display** | `v3.lua:2114-2131` (timer) | reads `+0x70 (float)`, `+0x74 (float)`, `+0x84/+0x88 (int32)` from selected `PlayerObject` every 500ms | working |
| **Live hero-respawn display** | `v3.lua:2138-2142` | `readFloat(base + 0xB169F0)` every 500ms | working |
| **Manual GameObject inspector** | `v3.lua:1308-1366` | reads `+0x00 vtable, +0x5C HP, +0x58 OwnerID, +0x3A7 InvulnFlag, +0x50 ObjID, +0x335 ParentIdx, +0x278 Components, +0x3A0 StatusFlags, +0x348 HardpointFlag` and resolves owner faction | working |
| **VTable match check** | `v3.lua:1321-1327` | compares loaded vtable to `base + 0x8661B8` (`GOC_VTable`) and reports MATCH/MISMATCH | working |
| **Capture Selected Unit** | `v3.lua:1298-1305` | placeholder -- needs selected-unit pointer (not yet hooked) | **placeholder** |
| **Set HP on inspected object** | `v3.lua:1384-1394` | `writeFloat(addr + 0x5C, value)` | working |
| **Toggle Invuln flag on inspected object** | `v3.lua:1396-1402` | `writeBytes(addr + 0x3A7, val)` | working |
| **Faction overview (8 slots, all factions credits/tech)** | `v3.lua:1574-1595` | iterates `PlayerArray`, displays per slot | working |
| **Debug tab: live game state** | `v3.lua:2207-2259` | base, `PlayerArray`, `PlayerCount`, `GameModeManager` | working |
| **Raw bridge command** | `v3.lua:2065-2092` | sends arbitrary Lua to the pipe | working |

### 1.5 Placeholders / disabled features

| Feature | Source | Status / reason |
|---|---|---|
| Tab 2 "Heal All Your Units" button | `v3.lua:1131-1135` | **placeholder** -- v3 only; "requires unit list enumeration" (v4 implements this via the bridge in `v4.lua:308-336`) |
| Tab 2 "Kill All Enemies" button | `v3.lua:1137-1141` | **placeholder** -- v3 only |
| Tab 3 "Unit Speed Controls" | `v3.lua:1221-1224` | **placeholder** -- "requires selected unit pointer" |
| Tab 4 "Capture Selected Unit" | `v3.lua:1298-1305` | **placeholder** -- "selected unit pointer not yet found" |
| Tab 7 per-hero respawn | `v3.lua:1763-1781` | **placeholder** -- "per-hero respawn timers and ability cooldowns require the pipe bridge" |
| Tab 8 "Game Speed" note | `v3.lua:1857-1860` | **disabled** -- "controlled via in-game options menu, not memory" |
| Tab 6 "Planet ownership changes" | `v3.lua:1689-1691, 2244-2250` | **placeholder** -- bridge required, not yet implemented |
| `blueprint_load` JSON parser | `blueprints.lua:124-141` | **partially working** -- the `gmatch` pattern is brittle (commented "this pattern won't work cleanly"); a fallback iteration follows but unit ownership is lost on load |
| Camera zoom / aspect ratio RVAs | `v3.lua:34-37` (`CameraZoomAngle = 0xB1599C`, `CameraDistance = 0xB159A4`, `ScreenAspect = 0xA12550`) | **declared but unused** -- defined as constants in the RVA table but no UI binds to them in v3 |

---

## Section 2: Cross-reference to `verified_facts.json`

### 2.1 RVAs the trainer uses that ARE in the ledger

| Trainer constant | Value | `verified_facts.json` entry | Confidence |
|---|---|---|---|
| `RVA.PlayerArray` | `0xA16FF0` | `fact_global_player_array` | VERIFIED (CE + test_harness) |
| `RVA.PlayerCount` | `0xA16FF8` | `fact_global_player_count` | VERIFIED (CONFIRMED-RUNTIME) |
| `RVA.HeroRespawn` | `0xB169F0` | `fact_global_default_hero_respawn_time` | VERIFIED (Ghidra + IDA cross-check) |
| `RVA.SetHP` | `0x3A89D0` | `rva_set_hp` | VERIFIED (CE AOB unique + CT hook live + test harness) |
| `RVA.GameModeManager` | `0xB153E0` | `fact_global_game_mode_manager` | VERIFIED (Ghidra + IDA) |
| `RVA.ScreenAspect` | `0xA12550` | `fact_global_screen_aspect_ratio` | VERIFIED (Ghidra) |
| Active game mode pointer | `0xB15418` | `fact_global_active_game_mode` | VERIFIED (Ghidra + IDA) |
| FOW data global | `0xA573D0` | `fact_global_fow_data` | VERIFIED (CE CONFIRMED-AOB) |
| `Take_Damage_Outer` (used by `dps_log.lua` event source -- referenced in trainer notes at `v3.lua:1874`) | `0x38A350` | `fact_lua_take_damage_noop` notes | LIVE_OBSERVED (real C++ entry, but Lua wrapper is no-op) |
| `Make_Invulnerable` Lua wrapper | `0x57D550` | `rva_make_invulnerable_lua` | VERIFIED (IDA) |
| `CheckPopCap` (referenced in v3.lua:1865) | `0x2AC320` | `rva_check_pop_cap` | VERIFIED (Ghidra) |

### 2.2 Trainer-referenced RVAs / globals that are **NOT yet in the ledger** (gaps for the porting phase)

| Constant | Value | Used by | Why it's a gap |
|---|---|---|---|
| `RVA.GOC_VTable` | `0x8661B8` | `v3.lua:1321` (Inspector vtable match) | No `verified_facts.json` entry. Must be added so the C++ bridge can validate `GameObjectClass*` arguments. |
| `RVA.CameraZoomAngle` | `0xB1599C` | `v3.lua:35` (declared, unused) | Not in ledger; if camera-control features ever ship, needs verification. |
| `RVA.CameraDistance` | `0xB159A4` | `v3.lua:36` (declared, unused) | Not in ledger; same as above. |
| `PlayerObject` field offsets | `+0x37`, `+0x48`, `+0x4C`, `+0x54`, `+0x62`, `+0x68`, `+0x70`, `+0x74`, `+0x84`, `+0x88`, `+0x108`, `+0x360` | direct memory ops in Tab 1, Tab 6, Tab 8 | Not currently captured in `verified_facts.json` as a struct schema. The ledger should grow a `struct_player_object_layout` fact with all of these fields and their verified status. **`+0x37 Playable` is documented in `CLAUDE.md` as "does NOT contain 0 or 2 as expected" -- this is a known runtime bug that needs cross-validation before the C++ port relies on it.** |
| `GameObject` field offsets | `+0x50`, `+0x58`, `+0x5C`, `+0x278`, `+0x335`, `+0x3A0`, `+0x3A1`, `+0x3A7`, `+0x348` | the SetHP cave + Inspector tab | The InvulnFlag (`+0x3A7`) and HardpointFlag (`+0x348`) are not in the ledger as confirmed offsets. Make_Invulnerable's actual setter location (per `fact_invuln_dispatch_is_embedded_in_lua_wrapper`) is `0x57D550` -- but the byte offsets it writes are not separately recorded. |
| `Story_Event` factory at `0x453310` | `0x453310` | trainer note `v3.lua:1990-1994` ("61 event types in factory at RVA 0x453310. CRC32+LCG hash lookup.") | The `verified_facts.json` has `rva_story_event_ctor 0x4501D0`, `rva_story_event_command_unit_ctor 0x4504E0`, `rva_story_event_build_from_parsed 0x4562A0`, but **not** `0x453310`. Either the trainer note is wrong or the factory hash table needs to be added to the ledger. |
| Default hero respawn original value | (runtime read) | `state.origRespawn` in `v3.lua:1178` | The ledger says `0xB169F0` is a float, but does not record the default value (`96.0` per the "Restore Default" button at `v3.lua:1729`). Worth noting in the fact entry. |
| `SetHP` original prologue bytes | `40 53 48 83 EC 60` | both god/ohk caves, restored in `getDisableScript` | The `rva_set_hp` fact should record the canonical prologue bytes so the C++ port can verify them at startup before installing a MinHook detour. |

### 2.3 Other trainer notes the ledger should capture

- `v3.lua:1874-1877`: "SetHP at 0x3A89D0 | Take_Damage_Outer at 0x38A350" and "Invuln flag: obj+0x3A7 | Prevent-death: obj+0x3A1 bit 7" -- these are the trainer-author's authoritative reference and are not all redundant with the ledger.
- `v3.lua:1990`: "61 event types in factory at RVA 0x453310. CRC32+LCG hash lookup." -- if accurate, this is a missing engine_function fact.

---

## Section 3: Hook strategies summary

### 3.1 AOB scan + NOP patch
**Not used.** This trainer never AOB-scans. The C++ port does not need to translate any AOB patterns from the CE side.

### 3.2 AOB scan + injection cave
**Not used as a "scan".** The trainer hardcodes the SetHP RVA (`0x3A89D0`) and patches the 6-byte prologue with a `jmp <cave>` via `autoAssemble`. This is functionally equivalent to a MinHook trampoline detour.

- **CE Lua API used:** `autoAssemble(scriptString)` (returns `ok, err`); cave is allocated via `alloc(name, size)`; the disable script restores the original bytes via `db 40 53 48 83 EC 60` and `dealloc`s the cave + the global storage.
- **Globals threaded into the cave:** the absolute address of `PlayerArray` is computed Lua-side (`base + 0xA16FF0`) and emitted as a `dq` literal inside the cave (`gd_pa: dq <addr>`). For OHK/Combined a `(float)0.0` is also written into a `dd` slot.
- **C++ equivalent:** install a MinHook detour on `SetHP` (`base + 0x3A89D0`). The detour reproduces the parent walk + owner check inline in C++, then either tail-calls the original `SetHP`, returns early without calling it (God Mode), or replaces the new-HP `float` argument with `0.0f` before tail-calling (OHK). Since SetHP's prototype is `void SetHP(GameObject* obj /*rcx*/, float hp /*xmm1*/)`, the detour is just `void __fastcall SetHP_hook(GameObject* obj, float hp)`.
- **Gotchas:**
  - `xmm1` carries the new-HP arg in fastcall; the C++ detour must replicate that calling convention (or use a naked function).
  - The cave reads `obj+0x335` as `byte` (`movzx eax, byte ptr [rdx+335]`) -- the C++ port must use the same width (`uint8_t`).
  - The parent walk has no termination guard other than `ParentIdx == 0xFF` or `Components[ParentIdx] == nullptr` -- in pathological cases this could loop forever. The C++ port should add a hard cap (e.g., 8 iterations) for safety.
  - **Cannot run two combat caves at once.** v3 enforces this by calling `disableAllCombat()` before installing any new cave. The C++ port should expose a single mode flag instead.
  - The cave restoration assumes the original 6 bytes are exactly `40 53 48 83 EC 60`. The C++ port should verify this at hook-install time and refuse to install if the bytes differ (game version mismatch).

### 3.3 AOB scan + branch flip
**Not used.** No branch flips in the trainer.

### 3.4 Pointer chain walk
- **PlayerArray walk:** `base + 0xA16FF0 -> qword (PlayerArray base) -> [slot * 8] -> PlayerObject*` (used by every Tab-1 / Tab-6 feature and inside the SetHP cave).
- **Parent-component chain walk** (inside the SetHP cave only): `obj+0x335 (ParentIdx) -> obj+0x278 (Components qword) -> Components[ParentIdx] -> repeat until ParentIdx == 0xFF`.
- **CE Lua API used:** `readQword`, `readInteger`, `readFloat`, `readBytes`, `readString`, `getAddress("StarWarsG.exe")`.
- **C++ equivalent:** straight pointer arithmetic on memory the bridge has already mapped. No `ReadProcessMemory` needed since the bridge runs in-process.
- **Gotchas:**
  - All reads must be null-checked. The trainer's `safeReadQword` returns `nil` for `0`, which is a defensive idiom -- the C++ equivalent should treat `0` as "not yet initialized" and back off, not crash.

### 3.5 MinHook on known RVA (event interception)
- **Used by:** `dps_log.lua` consumes events that the **DLL bridge** (powrprof.dll, not CE) generates by hooking `Take_Damage_Outer` at `0x38A350`. The CE side just polls the shared-memory ring buffer at `Local\SWFOC_Bridge_Events`.
- **Ring buffer schema:** `[uint32 write_pos][uint32 read_pos][uint32 event_count][uint32 flags][ring 65520 bytes]`. Each event is `[uint16 type][uint16 payload_size][payload]`. `EVT_HP_CHANGE (0x01)` payload = `[u32 unit_id][f32 old_hp][f32 damage][u32 damage_type]` (16 bytes). `EVT_UNIT_DIED (0x02)` payload = `[u32 unit_id][u32 death_cause]` (8 bytes). `flags` bit 0 = capture enabled.
- **CE Lua API used:** `openFileMapping`, `mapViewOfFile`, `readInteger`, `readSmallInteger`, `readFloat`, `writeInteger`, `bAnd/bOr/bNot`.
- **C++ equivalent:** the C++ bridge already owns this side. The porting phase should expose a native pipe verb (e.g., `EVENT_DRAIN`) that reads-and-resets the ring buffer in one call, so the editor doesn't need to reimplement the polling loop.

### 3.6 QueryInterface walking (hardpoint enumeration)
- **Not implemented in the trainer.** Per `fact_make_invulnerable_hardpoint_propagation`, the trainer relies on `Make_Invulnerable(true)` propagating to hardpoints **circumstantially** -- it does not enumerate hardpoints itself. Per `fact_invuln_dispatch_is_embedded_in_lua_wrapper`, the actual invuln dispatch lives in the Lua wrapper at `0x57D550` and uses `QueryInterface(22)` for hardpoint iteration internally.
- **For the C++ port:** to provide reliable per-hardpoint god-mode (one of the stated goals), the bridge must replicate the `QueryInterface(22)` hardpoint walk that lives inside `0x57D550`. This is unbuilt territory and is its own subtask.

---

## Section 4: Open questions for the porting phase

1. **`PlayerObject.Playable` field at `+0x37` does not contain 0 or 2 as expected** (per `CLAUDE.md` and `getPlayerInfo` at `v3.lua:178` checking `== 2`). The trainer's "playable" filter is therefore unreliable. The C++ port should switch to a stronger predicate (e.g., `LocalPlayer == 1 || IsHuman == 1`) and the `+0x37` field should be re-RE'd to discover its real meaning.

2. **`Take_Damage_Outer` location is `0x38A350`** (per `verified_facts.json:fact_lua_take_damage_noop` notes and `v3.lua:1874`), but it has **no first-class `verified_facts.json` entry**. Add an `rva_take_damage_outer` fact before the C++ port hooks it for the DPS event source.

3. **`Take_Damage` Lua wrapper is a no-op** (per `fact_lua_take_damage_noop`). The "Kill All Enemies" features in `v4.lua:338-364` and `lua_playground.lua:14-34` rely on `u:Take_Damage(99999)` and will silently do nothing. The C++ port should replace these with a direct `SetHP(unit, 0.0f)` call (which DOES work, since the trainer's OHK cave proves it).

4. **The `gd_pa: dq <addr>` literal must be re-emitted whenever the game restarts** because `getAddress("StarWarsG.exe")` can change between runs (ASLR). The C++ port doesn't have this problem (it's in-process), but the editor service must remember to re-resolve `PlayerArray` after every `LOADED_GAME` event.

5. **The SetHP cave's parent walk has no termination guard other than `ParentIdx == 0xFF`.** A corrupt component graph would loop forever and freeze the game. Add a hard iteration cap (8 levels) in the C++ port.

6. **The `0x453310` "61 story event types factory" claim** in `v3.lua:1990-1994` is not in `verified_facts.json`. Either the note is wrong (the closest documented fact is `rva_story_event_build_from_parsed 0x4562A0`) or this is a missing engine_function fact. **Verify before relying on it.**

7. **`GOC_VTable 0x8661B8` is used as the Inspector's "is this really a GameObjectClass?" check** but is not in `verified_facts.json`. Add it. (The fact `rva_set_hp` already implies SetHP operates on GameObjectClass instances, so the vtable address is verifiable.)

8. **`Spawn_Unit` calling convention disagrees between v3 and v4.**
   - v3 (`v3.lua:1508-1511`): `Spawn_Unit(FindPlayer(slot), Find_Object_Type("name"), nil, count)` (4 args, position is `nil`)
   - v4 (`v4.lua:601-614`): `Spawn_Unit(player, t, pos)` in a `for i=1,count do pcall(Spawn_Unit, ...) end` loop (3 args, with explicit position from `Create_Position`)
   - The Lua-side signature should be confirmed against the engine binding. The blueprints loader (`blueprints.lua:154`) uses `Spawn_Unit(player, t, pos)` (3 args). For the C++ port: pick one signature, document it.

9. **CE 7.6 `connectToPipe` works (per `feedback_ce_pipe_discovery`) but `writeToPipe` / `readFromPipe` as **globals** do NOT exist.** The trainer's v3 (`v3.lua:294, 300`) uses the global functions and **will fail on CE 7.6**; v3 was probably authored against an older CE build. `shared_cmd.lua` already does the right thing by calling handle methods (`h:writeByte`, `h:readByte`). **The v3 named-pipe path is broken on CE 7.6** and v4's shared-memory path is what actually works in production. The C++ port should expose **both** transports (named pipe AND shared memory) so existing CE 7.x trainers don't need to be updated.

10. **The `freezeCredits` timer fights any in-game writer at 500ms cadence.** This is racy -- if the game's credit-update tick is faster than 500ms, the user will see flicker. The C++ port should instead hook the credit-write site (or set up a write-watch breakpoint) for atomic enforcement.

11. **`Find_All_Objects_Of_Type(nil)` is broken.** Documented in `god_mode.lua:7` and `type_discovery.lua:31`. Every iteration in this trainer uses the **15-category fallback list** (`Capital, Fighter, Corvette, Frigate, Bomber, Cruiser, Destroyer, Transport, StarBase, Infantry, Vehicle, Air, Hero, Structure, Special`). The C++ port should expose a native "iterate all units" verb that walks the engine's actual object table directly (no Lua, no category fallback).

12. **`autoAssemble` failure mode is silent on cave-allocation collision.** If two cave names overlap (e.g., enabling God Mode while OHK is still allocated), CE returns an error string but the trainer's `pcall` only logs it. The C++ port should simply not use string-named caves -- detours are atomic.

13. **The DPS event capture flag in shared memory is set with `bOr(current_flags, 1)` and cleared with `bAnd(current_flags, bNot(1))`**, which is non-atomic. If the DLL writes the flag concurrently the value can race. The C++ port should expose `EVENT_ENABLE` / `EVENT_DISABLE` verbs that use `_InterlockedOr` / `_InterlockedAnd`.

14. **`Make_Invulnerable + Set_Cannot_Be_Killed` is the only invuln strategy that actually freezes hull at runtime** (per `fact_make_invulnerable_hull_freeze`). The Inspector tab's "Invulnerable (flag at +0x3A7)" checkbox writes `+0x3A7` directly, but **per the same fact, this byte alone may not be sufficient** -- the engine reads `obj+0x3A1 bit 7` (PreventDeath) for the at-zero check. The C++ port's "god mode" should set BOTH `+0x3A7` and `+0x3A1 |= 0x80`, or call the Lua wrapper at `0x57D550`, not just toggle one flag.

15. **Hardpoint propagation is unverified** (per `fact_make_invulnerable_hardpoint_propagation`). One of the stated goals of the C++ port is "god mode hardpoint propagation". Until the `QueryInterface(22)` hardpoint walk inside `Make_Invulnerable` (`0x57D550`) is reverse-engineered and ported, the C++ god mode will be no better than the Lua bridge version.

16. **Blueprints loader loses original ownership** (`blueprints.lua:350-352`: "Original owners are not preserved (all spawn as local player)"). The C++ port should resolve `entry.owner` (faction name string) to the corresponding `PlayerObject*` via `PlayerArray` so loaded blueprints reproduce the original faction layout.

17. **The trainer assumes `getAddress("StarWarsG.exe")` is non-zero on every call.** If CE is attached to a different process (e.g., `swgalacticbattles.exe` for the Steam launcher) every read silently returns `nil` and the GUI shows `---`. The C++ port doesn't have this failure mode but the editor's pipe layer must handle the "bridge not loaded" case explicitly.

18. **No `aobscan` calls means no AOB validation against game updates.** If `StarWarsG.exe` ever ships a patch that moves `SetHP` away from `0x3A89D0`, the trainer (and the C++ port if it uses the same hardcoded RVA) will hook the wrong code. The C++ port should AOB-verify the SetHP prologue bytes (`40 53 48 83 EC 60`) at hook-install time and refuse to install on mismatch.

---

## Section 5: Working / experimental / disabled summary

| Category | Count |
|---|---|
| **Working features (CE-side, validated live)** | 51 |
| **Placeholder / "coming soon" features** | 8 |
| **Disabled / informational-only** | 3 |
| **Experimental (works but partially)** | 2 (`Kill All Enemies` via Lua `Take_Damage` -- no-op; blueprint loader -- ownership lost) |
| **Total inventoried features** | **64** |

| Hook style | Count |
|---|---|
| Direct memory write (no hook) | 13 |
| `autoAssemble` injection caves | 3 (God / OHK / Combined -- all share the same hook site) |
| Lua bridge (named pipe v3 / shared memory v4) | 32 |
| Shared-memory ring buffer poll | 1 (DPS event log) |
| Inspector / read-only display | 6 |
| Total | 55 distinct mechanisms |

| AOB signatures extracted | **0** -- the trainer never uses `aobscan`. |
| Unique cave assemblies | **3** (`godcave`, `ohkcave`, `combocave` -- all variations on one SetHP detour) |
| Unique RVA hook sites | **1** (`SetHP at 0x3A89D0`) |
| Unique RVAs read directly | **6** (`PlayerArray 0xA16FF0`, `PlayerCount 0xA16FF8`, `HeroRespawn 0xB169F0`, `GOC_VTable 0x8661B8`, `GameModeManager 0xB153E0`, `ScreenAspect 0xA12550`) |
