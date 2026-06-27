#pragma once
#include <cstdint>

// StarWarsG.exe RVAs — verified via Ghidra static decompilation (2026-04-04)
// All addresses are module-relative (add to GetModuleHandle(nullptr))
// Source: lua_api_map.json — 48 functions identified in Lua 5.0.2 cluster

namespace RVA {

// ======================================================================
// Lua 5.0.2 C API functions (compiled into StarWarsG.exe)
// ======================================================================

// State management
constexpr uintptr_t lua_open          = 0x7B8930; // CONFIRMED: MinHook hooked, 400+ states
constexpr uintptr_t lua_close         = 0x7B8890; // CONFIRMED-RE: Ghidra verified 2026-04-05 — was ESTIMATED at 0x7B8A70 (DENIED: mid-function in f_luaopen)
constexpr uintptr_t lua_checkstack    = 0x7B8BC0; // CONFIRMED: Ghidra — stack limit 0x800
constexpr uintptr_t lua_newthread     = 0x7B9190; // CONFIRMED: Ghidra — pushes type=8

// Stack operations
constexpr uintptr_t lua_gettop        = 0x7B8EF0; // CONFIRMED: (top-base)>>4
constexpr uintptr_t lua_settop        = 0x7B9AB0; // CONFIRMED: Ghidra — handles +/- indices, fills nil
constexpr uintptr_t lua_pushvalue     = 0x7B9600; // CONFIRMED: Ghidra — copies value at idx to top
constexpr uintptr_t lua_remove        = 0x7B9880; // CONFIRMED: Ghidra — shifts down, pops
constexpr uintptr_t lua_insert        = 0x7B8F00; // CONFIRMED: Ghidra — shifts up, inserts
constexpr uintptr_t lua_replace       = 0x7B98E0; // CONFIRMED: Ghidra — copies top to idx, pops

// Type checking
constexpr uintptr_t lua_type          = 0x7B9E00; // CONFIRMED: DLL self-test
constexpr uintptr_t lua_typename      = 0x7B9E40; // CONFIRMED: Ghidra — returns type name string
constexpr uintptr_t lua_iscfunction   = 0x7B8F60; // CONFIRMED: Ghidra — tag==6 && isC flag
constexpr uintptr_t lua_isnumber      = 0x7B8FB0; // CONFIRMED: Ghidra — tag==3 or tonumber conversion
constexpr uintptr_t lua_isstring      = 0x7B9010; // CONFIRMED: Ghidra — tag 3 or 4 (number coerces)

// Conversion (stack → C)
constexpr uintptr_t lua_tonumber      = 0x7B9BC0; // CONFIRMED: DLL self-test (was mislabeled as tostring)
constexpr uintptr_t lua_toboolean     = 0x7B9B70; // CONFIRMED: Ghidra — nil/false→0, else→1
constexpr uintptr_t lua_tolstring     = 0x7B9CC0; // CONFIRMED: Ghidra — returns gc+0x18 (char* past TString header)
constexpr uintptr_t lua_strlen        = 0x7B9B10; // CONFIRMED: Ghidra — returns gc+0x10 (TString.len)
constexpr uintptr_t lua_touserdata    = 0x7B9DA0; // CONFIRMED: Ghidra — lightuserdata=value, full=gc+0x20
constexpr uintptr_t lua_tothread      = 0x7B9D60; // CONFIRMED: Ghidra — checks tag==8
constexpr uintptr_t lua_topointer     = 0x7B9C20; // CONFIRMED: Ghidra — generic pointer for any GC type

// Push (C → stack)
constexpr uintptr_t lua_pushnil       = 0x7B9510; // CONFIRMED: writes type=0
constexpr uintptr_t lua_pushnumber    = 0x7B9520; // CONFIRMED: movsd + type=3
constexpr uintptr_t lua_pushlstring   = 0x7B94A0; // CONFIRMED: Ghidra — sized string push (was mislabeled as pushstring)
constexpr uintptr_t lua_pushstring    = 0x7B9540; // CONFIRMED: FoCAPI + DLL — null-terminated string
constexpr uintptr_t lua_pushfstring   = 0x7B9640; // CONFIRMED: Ghidra — wraps luaO_pushfstring
constexpr uintptr_t lua_pushcclosure  = 0x7B9340; // CONFIRMED: FoCAPI exact match
constexpr uintptr_t lua_pushboolean   = 0x7B9320; // CONFIRMED: writes type=1
constexpr uintptr_t lua_pushlightuserdata = 0x7B9480; // CONFIRMED: writes type=2
constexpr uintptr_t lua_newuserdata   = 0x7B91D0; // CONFIRMED: Ghidra — allocates, returns data ptr

// Table get
constexpr uintptr_t lua_gettable      = 0x7B8E90; // CONFIRMED: Ghidra — calls luaV_gettable(L,t,key,0)
constexpr uintptr_t lua_rawget        = 0x7B9720; // CONFIRMED: Ghidra — direct luaH_get, no metamethods
constexpr uintptr_t lua_rawgeti       = 0x7B9770; // CONFIRMED: Ghidra — luaH_getnum by integer key
constexpr uintptr_t lua_newtable      = 0x7B9140; // CONFIRMED: FoCAPI + DLL — pushes type=5
constexpr uintptr_t lua_getmetatable  = 0x7B8E10; // CONFIRMED: Ghidra — reads gc+0x10 metatable

// Table set
constexpr uintptr_t lua_settable      = 0x7B9A60; // CONFIRMED: FoCAPI + DLL
constexpr uintptr_t lua_rawset        = 0x7B97C0; // CONFIRMED: Ghidra — direct luaH_set, pops 2
constexpr uintptr_t lua_rawseti       = 0x7B9820; // CONFIRMED: FoCAPI + DLL
constexpr uintptr_t lua_setmetatable  = 0x7B99D0; // CONFIRMED: Ghidra — sets gc+0x10 (was mislabeled as settop)

// Environment
constexpr uintptr_t lua_getfenv       = 0x7B8D90; // CONFIRMED: Ghidra — reads closure env
constexpr uintptr_t lua_setfenv       = 0x7B9930; // CONFIRMED: Ghidra — stores into closure+0x20

// Call / Execute
constexpr uintptr_t lua_pcall         = 0x7B9280; // CONFIRMED: Ghidra — 4 params, calls luaD_pcall
constexpr uintptr_t lua_cpcall        = 0x7B8CD0; // CONFIRMED: Ghidra — calls C func in protected mode
constexpr uintptr_t lua_load          = 0x7B90F0; // CONFIRMED: Ghidra — parser/compiler chain
constexpr uintptr_t lua_error         = 0x7B8D80; // CONFIRMED: Ghidra — calls luaG_errormsg

// Comparison
constexpr uintptr_t lua_equal         = 0x7B9060; // CONFIRMED: Ghidra — luaV_equalobj
constexpr uintptr_t lua_lessthan      = 0x7B9690; // CONFIRMED: Ghidra — luaV_lessthan

// Iteration / Misc
constexpr uintptr_t lua_next          = 0x7B9220; // CONFIRMED: Ghidra — hash traversal
constexpr uintptr_t lua_concat        = 0x7B8C40; // CONFIRMED: Ghidra — concatenates n values

// GC
constexpr uintptr_t lua_getgccount    = 0x7B8DF0; // CONFIRMED: Ghidra — total bytes >> 10
constexpr uintptr_t lua_getgcthreshold = 0x7B8E00; // CONFIRMED: Ghidra — threshold >> 10
constexpr uintptr_t lua_setgcthreshold = 0x7B9990; // CONFIRMED: Ghidra — sets threshold << 10

// ======================================================================
// Lua internal functions (not public API — for reference)
// ======================================================================
constexpr uintptr_t close_state       = 0x7B8350; // CONFIRMED-RE: Ghidra — frees stack, CI, global_State, lua_State
constexpr uintptr_t f_luaopen         = 0x7B8A20; // CONFIRMED-RE: Ghidra — state init callback (0x7B8A70 is mid-function here!)
constexpr uintptr_t luaD_call         = 0x7BB9E0; // "C stack overflow" at nCcalls=200
constexpr uintptr_t luaD_pcall        = 0x7BBBE0; // Protected call wrapper
constexpr uintptr_t luaV_gettable     = 0x7C7F80; // VM table lookup with metamethods
constexpr uintptr_t luaV_tostring     = 0x7C83C0; // Value-to-string conversion
constexpr uintptr_t luaC_checkGC      = 0x7BC850; // GC threshold check
constexpr uintptr_t luaG_errormsg     = 0x7BABE0; // Error message handler

// ======================================================================
// Game engine functions
// ======================================================================
constexpr uintptr_t SetHP                    = 0x3A89D0; // CONFIRMED-RE: Ghidra verified 2026-04-05 — clamps [0, maxHP], writes obj+0x5C, debug prints on negative
constexpr uintptr_t Take_Damage_Outer        = 0x38A350; // CONFIRMED-RE: Ghidra verified 2026-04-05 — master damage router, 56 damage types, 15 capability checks via CanReceiveDamageType
constexpr uintptr_t Weapon_Tick              = 0x387010; // CONFIRMED-RE iter-224: 3-tool consensus (ida + ghidra + binary_ninja); per-frame weapon update; cooldown via delta-time arg passed to sub_140387400. Detour scales dt by g_fireRateMult_global for iter-225 SetFireRate global LIVE wire (iter-96 pattern parallel)
constexpr uintptr_t DeathHandler             = 0x39BDB0; // CONFIRMED-RE: Ghidra verified 2026-04-05 — full death pipeline: flags, debris, hero respawn, event 0x25
constexpr uintptr_t CanReceiveDamageType     = 0x3986B0; // CONFIRMED-RE: Ghidra verified 2026-04-05 — checks per-damage-type capability array at obj+((type+0x3D)*0x10), recurses hardpoints
constexpr uintptr_t QueryInterface           = 0x395AC0;
constexpr uintptr_t AddCredits               = 0x27F370;
constexpr uintptr_t SetTechLevel             = 0x288980;
constexpr uintptr_t SetSpeedOverride         = 0x3A8C90;
constexpr uintptr_t ClearSpeedOverride       = 0x38F8B0;
// Camera setters — verified 2026-05-06 iter 236 via callgraph re-audit
// (rva_camera_set_transform_matrix + rva_camera_get_position, both 4-tool
// VERIFIED in verified_facts.json). SetTransformMatrix takes (CameraClass*,
// float[12]) and writes the inline 4x3 matrix at CameraClass+0x10..+0x40,
// then propagates to the per-frame matrix-pointer at CameraClass+0x40 via
// sub_140261C20. GetPosition reads X/Y/Z from the matrix-pointer's translation
// column at indices [3]/[7]/[11]. Active camera lookup chain (tactical mode
// only, per iter-236 RE doc): GameModeClass = qword_140B15418 (the global at
// GameModeRoot_Global = 0xB15418); vftable[28] @ +0xE0 returns mode (2=Land);
// camera = *(GameModeClass + 0x90) when mode==2.
constexpr uintptr_t CameraSetTransformMatrix = 0x261BD0;
constexpr uintptr_t CameraGetPosition        = 0x261A40;
// Shield writers — verified 2026-04-29 iter 128 via callgraph re-audit
// (rva_set_front_shield, rva_set_rear_shield in verified_facts.json).
// Same `void __fastcall(__int64 unit, float val)` shape as SetSpeedOverride.
// Engine validates value >= 0, then dispatches through QueryInterface(15)
// to fetch the shield-behavior subobject before writing through. Iter 105
// missed these because it searched string-literal keys instead of
// function-name entries; see iter 128 RE finding for full Hex-Rays trace.
constexpr uintptr_t SetFrontShield           = 0x3A8630;
constexpr uintptr_t SetRearShield            = 0x3A91E0;
// Shield reader — verified 2026-04-29 iter 131. `double __fastcall(__int64)`
// returns the front-shield current value. Same iter-128 catalog-drift
// pattern as iter 129's writer flip: `Lua_GetUnitShield` was reading
// from a stale cache map, even though the engine reader was already
// pinned in the verified ledger as `rva_front_shield_read`.
constexpr uintptr_t FrontShield_Read         = 0x3963C0;
// Diplomacy writer — verified 2026-04-29 iter 133 via callgraph re-audit
// (rva_make_ally_make_enemy_engine in verified_facts.json). One-line
// engine writer:
//   void __fastcall MakeAllyEnemy(PlayerClass* p, int target_slot, int state)
//   { *(_DWORD*)(p[+0x370] + 4*target_slot) = state; }
// State codes: 0=ally, 1=enemy (per rva_is_ally_engine /
// rva_is_enemy_engine readers), 2=neutral (assumed — only remaining
// state; verify with live game). Iter 132 audit caught the catalog
// drift; iter 133 shipped the LIVE wire.
constexpr uintptr_t MakeAllyEnemy            = 0x288800;
constexpr uintptr_t PlayerList_FindByID         = 0x294BC0;
// PlayerListClass methods — verified 2026-04-10 via IDA (see
// knowledge-base/verified_facts.json :: rva_player_list_switch_sides).
// Switch_Sides rotates the current human-player slot forward through
// playable entries; GetCurrentPlayer reads vec[currentSlot] with bounds
// check. __fastcall this-call convention (rcx = PlayerListClass*).
constexpr uintptr_t PlayerList_SwitchSides       = 0x297E80;
constexpr uintptr_t PlayerList_GetCurrentPlayer  = 0x294A40;
// PlayerListClass::Set_Current_Player_By_Faction — galactic-mode equivalent
// of Switch_Sides. Takes (playerList, modeType, factionPtr, playerObjOrNull).
// Sets PlayerListClass+0x30 = target slot and +0x62 = 1 on target, but does
// NOT sweep the other slots' +0x62 bytes. Used internally by
// GameModeManagerClass::Start_Game inside the inverted mode-type guard
// (mode not in {1,2,4} || mode == 3). Bridge's SWFOC_SetHumanPlayer_v2 does
// the manual sweep itself and calls GameMode_RefreshLocalPlayerSubsystems
// (0x2B59B0) instead of going through this function. Kept in rvas.h for
// reference and in case a future helper wants the engine's own path.
// See knowledge-base/faction_switch_full_anatomy_2026-04-11.md section 3.
constexpr uintptr_t PlayerList_SetCurrentByFaction = 0x2924D0;
// GameModeClass::Refresh_Local_Player_Subsystems — walks the active game
// mode's subsystem linked list at *(ActiveGameMode + 64/72) and notifies
// each subsystem (camera, HUD, selection, input router) of the new local
// player. Called by the engine's own `switch_player` console command
// (sub_14001FB30) immediately after Switch_Sides; also invoked by
// SkirmishSetup at 0x140095BFE. Signature: (activeGameMode).
// The v2 helper calls this directly to get camera/HUD/input router to
// follow the faction change. See
// knowledge-base/faction_switch_full_anatomy_2026-04-11.md section 5.
constexpr uintptr_t GameMode_RefreshLocalPlayerSubsystems = 0x2B59B0;
// GetCurrentPlayerSlot -- returns the *slot id* of the current human player
// by chasing PlayerListClass_Global. Used by the selection reader to pick
// the right per-player selection vector. Entry reads *(pl+0x30) for the
// current index, then returns *(vec_begin[idx] + 0x4C). IDA-verified
// 2026-04-11 (see knowledge-base/selection_pointer_2026-04-11.md).
constexpr uintptr_t GetCurrentPlayerSlot         = 0x294A70;
// GetPerPlayerSelectionList(mgr_root, slot) -> DynamicVector<GameObj*>*.
// Two-instruction helper: return *(mgr + 0x1C0) + 0x48 * slot. IDA-verified
// 2026-04-11 against sub_1402AD080. The returned pointer is a linked-list
// header (head = +0x10, sentinel = +0x08, node.next = +0x08, node.data = *(+0x18) - 0x18).
constexpr uintptr_t GetPerPlayerSelectionList    = 0x2AD080;
constexpr uintptr_t Get_Owner_Lua            = 0x5792E0;
constexpr uintptr_t Change_Owner             = 0x574D0E;
constexpr uintptr_t SetPosition               = 0x3ABB80; // CONFIRMED-RE: Ghidra verified 2026-04-05 — was mislabeled Make_Invulnerable_Setter; actually SetPosition/Teleport with hardpoint propagation via QI(0x16)
// constexpr uintptr_t Make_Invulnerable_Setter = 0x3ABB80; // DENIED: this is SetPosition, not invuln setter. Real invuln logic is in the Lua wrapper + behavior system.
constexpr uintptr_t ScheduleHeroRespawn      = 0x48EB10;

// Invulnerability system (IDA-verified call chain from Make_Invulnerable Lua wrapper)
constexpr uintptr_t MakeInvulnerable_LuaWrapper = 0x57D550; // CONFIRMED-RE: IDA verified — full Lua binding with hardpoint propagation
constexpr uintptr_t BehaviorAttach               = 0x38C570; // CONFIRMED-RE: attaches behavior object to unit (signature: char(obj, behavior, char))
constexpr uintptr_t BehaviorRemoveDispatch        = 0x3A54C0; // CONFIRMED-RE: removes behavior from unit
constexpr uintptr_t HardpointGet                  = 0x4052D0; // CONFIRMED-RE: gets hardpoint by index from manager
constexpr uintptr_t HardpointCount                = 0x405300; // CONFIRMED-RE: returns hardpoint count from manager
// BehaviorLookup: maps "INVULNERABLE" (MSVC std::string by pointer) to a behavior object.
// Call pattern from MakeInvulnerable_LuaWrapper: build 32-byte SSO std::string, pass &str.
// Verified via IDA MCP on 2026-04-23: decompile of sub_1404C3520 shows linear scan of
// off_140A2AC90 registry table after strupr.
constexpr uintptr_t BehaviorLookup                = 0x4C3520;

// FoCAPI-discovered engine functions
constexpr uintptr_t LuaScriptClass_GetScriptFromState = 0x245790;
constexpr uintptr_t LuaScriptClass_MapVarToLua        = 0x247700;
constexpr uintptr_t PlayerWrapper_Create               = 0x6019F0;
constexpr uintptr_t GameObjectTypeWrapper_Ctor         = 0x604A10;
constexpr uintptr_t LuaUserVar_RegisterMember          = 0x24BE40;
constexpr uintptr_t LuaUserVar_ReturnVariable          = 0x256D40;
constexpr uintptr_t GameText_Get                       = 0x1FA680;
constexpr uintptr_t OperatorNew                        = 0x769C58;

// ======================================================================
// VictoryMonitorClass cluster -- iter 440-449 RE; ledger-pinned 2026-05-07
// (rva_victory_monitor_ctor / counter_inc / parent_tick).
// Used by iter-450 SWFOC_TriggerVictory scaffolding. The counter_inc helper
// (16 bytes) is the Option C MinHook target; iter-450 installs MH_CreateHook
// but DEFERS MH_EnableHook to iter-450a, which still needs:
//   (a) AwaitingVictoryTest 48-byte struct layout RE for safe injection,
//   (b) capture-on-CTOR hook at VictoryMonitor_Ctor to resolve the rcx
//       discriminator problem (counter_inc fires for many subsystems, so
//       we need a way to identify VictoryMonitor instances).
// See knowledge-base/iter449_breakthrough_disambiguation_parent_tick_inlines.md
// for the full disambiguation analysis.
// ======================================================================
constexpr uintptr_t VictoryMonitor_Ctor        = 0x341850; // 358-byte CTOR; foundation for iter-450a capture-on-construction hook
constexpr uintptr_t VictoryMonitor_CounterInc  = 0x341FE0; // 16-byte ++[rcx+0x5C] clamp helper; iter-450 MinHook target (DORMANT)
constexpr uintptr_t VictoryMonitor_ParentTick  = 0x456970; // 15.6KB parent-class tick (REFERENCE only -- too large to detour safely)

// ======================================================================
// Global data pointers
// ======================================================================
constexpr uintptr_t PlayerListClass_Global = 0xA16FD0;
constexpr uintptr_t PlayerArray_Global     = 0xA16FF0;
constexpr uintptr_t PlayerCount_Global     = 0xA16FF8;
// GameModeRoot_Global — base for the runtime selection pointer chain. The
// selection reader dereferences (g_base + 0xB15418 + 0x18) to get the live
// GameModeClass instance that owns the per-player selection vectors. Verified
// via IDA Pro decompile of sub_140603F60 and sub_14003AFE0 (2026-04-11).
constexpr uintptr_t GameModeRoot_Global    = 0xB15418;
constexpr uintptr_t TheCommandBar          = 0xB27F60;
constexpr uintptr_t TheGameText            = 0xA7BC58;
constexpr uintptr_t DefaultHeroRespawnTime = 0xB169F0;

// Lua function registration
constexpr uintptr_t GlobalLuaRegister    = 0x546C70;
constexpr uintptr_t GlobalRegisterHelper = 0x247000;

// ======================================================================
// AIPlayerClass — verified 2026-04-26 via IDA Pro MCP + Ghidra
// ======================================================================
// AIPlayerClass::ctor (this, PlayerObject*, third_param) — stores the
// vftable to *this and writes member fields at offsets 40, 48, 56-80, 88.
// Decompile at 0x1404AF810 shows literal `*(_QWORD *)a1 = &AIPlayerClass::vftable`
// — IDA names the vftable explicitly. Multi-tool consensus: IDA + Ghidra.
constexpr uintptr_t AIPlayerClass_ctor          = 0x4AF810;
// Simple AIPlayerClass factory: takes PlayerObject*, returns new AIPlayerClass*.
// Allocates 0x60 bytes via operator new, calls AIPlayerClass::ctor with
// (this, PlayerObject, 0). Caller is responsible for writing the returned
// pointer to PlayerObject+0x360. Verified via IDA decompile at 0x1404AFF50
// — the function body is a textbook factory shell.
constexpr uintptr_t AIPlayerClass_SimpleFactory = 0x4AFF50;
// Typed AIPlayerClass factory: takes (PlayerObject*, type_name_cstr) →
// AIPlayerClass*. Looks up the AI type by name (e.g. "Aggressive",
// "Defensive"), allocates+constructs, then iterates AI components for
// initialisation. Use the simple factory unless a specific type is needed.
constexpr uintptr_t AIPlayerClass_TypedFactory  = 0x4AFF90;

// ======================================================================
// Struct field offsets
// ======================================================================

namespace PlayerObj {
    constexpr int Playable     = 0x37;
    constexpr int SlotIndex    = 0x48;
    constexpr int LocalPlayer  = 0x62;
    constexpr int FactionName  = 0x68;
    constexpr int Credits      = 0x70;
    constexpr int MaxCredits   = 0x74;
    constexpr int TechLevel    = 0x84;
    constexpr int MaxTechLevel = 0x88;
}

namespace GameObj {
    constexpr int VTable         = 0x00;
    constexpr int ObjectID       = 0x50;
    constexpr int OwnerPlayerID  = 0x58;
    constexpr int HP             = 0x5C;
    constexpr int ComponentArray = 0x278;
    constexpr int GameObjType    = 0x298;
    constexpr int ParentIndex    = 0x335;
    constexpr int HardpointFlag  = 0x348; // CONFIRMED: trainer Inspector tab + ce_trainer_inventory.md section 1.2
    constexpr int StatusFlags    = 0x3A0; // CONFIRMED: trainer Inspector tab + ce_trainer_inventory.md section 1.2
    constexpr int PreventDeath   = 0x3A1; // CONFIRMED: bit 0x80 set by Set_Cannot_Be_Killed(true)
    constexpr int InvulnFlag     = 0x3A7;
    // BehaviorMarker: 0xFF means "no INVULNERABLE behavior attached yet". Anything
    // else means behavior is present. Set by BehaviorAttach, cleared by
    // BehaviorRemoveDispatch. Verified via IDA MCP on 2026-04-23 against
    // sub_14057D550 (offset 893 decimal = 0x37D).
    constexpr int BehaviorMarker = 0x37D;
}

namespace UnitType {
    // Per-unit-type stats struct offsets — verified iter 258 (2026-05-06).
    // The struct lives behind the GameObjType pointer (GameObj+0x298). Multiple
    // unit-instances share one type-stats record, so writing here changes the
    // CAP for EVERY unit of that type.
    //
    // Semantic verification per iter-256 memory rule:
    //   * GetMaxHealth (rva_get_max_health @ 0x3727A0) reads
    //     `*(float*)(this + 0xDCC)` as the very first instruction.
    //   * Two callers (rva_get_hull_percentage @ 0x396DF0, rva_set_hp @ 0x3A89D0)
    //     pass `*(unit + 0x298)` as the `this` arg AND dereference the typename
    //     string at `(*(unit+0x298)) + 0xF8` — both consistent with "+0x298 holds
    //     the unit-type pointer".
    //   * Existing ledger entries `rva_get_max_front_shield` @ 0x372320 and
    //     `rva_get_max_rear_shield` @ 0x3725F0 (3-tool VERIFIED, 2026-04-04 first
    //     documented) pin the +0xDD0 / +0xDD4 sibling offsets.
    constexpr int MaxHull        = 0xDCC; // float — base max-hull, before damage/diff multipliers
    constexpr int MaxFrontShield = 0xDD0; // float — base max-front-shield
    constexpr int MaxRearShield  = 0xDD4; // float — base max-rear-shield
}

// ======================================================================
// Selection system (IDA-verified 2026-04-11 via re-findings/camera_selection_system.json
// + live IDA Pro decompile of sub_140603F60, sub_1402AD080, sub_14003AFE0).
// The GameModeRoot pointer chain + per-player selection vector layout live
// here so Lua_GetSelectedUnit / Lua_GetSelectedUnits stay centralized. See
// knowledge-base/selection_pointer_2026-04-11.md for the full derivation.
// ======================================================================
namespace Selection {
    // Offset inside GameModeRoot_Global at which the live GameModeClass
    // pointer is stored. Walk is: *(g_base + 0xB15418 + kModeRootIndirection).
    constexpr int kModeRootIndirection   = 0x18;
    // Offset inside the GameModeClass at which the flat array of per-player
    // selection vectors begins. Each entry is kSelectionEntryStride bytes.
    constexpr int kPerPlayerVectorsArray = 0x1C0;
    // Stride between consecutive per-player DynamicVectorClass headers.
    constexpr int kSelectionEntryStride  = 0x48;
    // Linked-list head/sentinel offsets inside one header.
    constexpr int kVectorSentinel        = 0x08; // sentinel node
    constexpr int kVectorHead            = 0x10; // first live node
    // Per-node offsets (doubly linked list).
    constexpr int kNodeNext              = 0x08;
    constexpr int kNodeDataPlus24        = 0x18; // points at obj + 0x18
    constexpr int kNodeDataAdjustment    = 0x18; // subtract to get obj
    // Task 104 (2026-04-23): same GameModeRoot chain also exposes the
    // tactical-battle's full object list. Verified via IDA decompile of
    // sub_140540B20 (Find_All_Objects_Of_Type implementation):
    //   inner        = *(g_base + GameModeRoot_Global + 0x18)
    //   list_sentinel = inner + kObjectListSentinel  (0x40)
    //   list_head    = *(inner + kObjectListHead)    (0x48)
    //   walk: node = *(node + kNodeNext); obj = *(node + kNodeDataPlus24) - kNodeDataAdjustment
    //   (shares the kNode* node-offset constants below with the selection walker)
    constexpr int kObjectListSentinel    = 0x40; // &inner->tail_sentinel
    constexpr int kObjectListHead        = 0x48; // *(inner+0x48) → first live node
    constexpr int kMaxTacticalObjects    = 2048; // hard cap for safety
    // Hard cap on per-player selection iteration. The engine uses ring-buffer
    // growth so anything beyond this is either corruption or a test rig.
    constexpr int kMaxSelectionCount     = 64;
}

// SetHP entry RVA — alias for clarity at the SetHP combat hook site.
// Same value as RVA::SetHP above; named separately so search-and-replace
// in the porting phase is unambiguous.
constexpr uintptr_t SetHP_Entry = 0x3A89D0; // CONFIRMED-RE: cave hook site, prologue = 40 53 48 83 EC 60

constexpr uintptr_t GameObjectClass_VTable_RVA = 0x8661B8;

} // namespace RVA
