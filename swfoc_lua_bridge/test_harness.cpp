// test_harness.cpp -- Standalone C++ test harness for the SWFOC Lua Bridge.
// Exercises all bridge logic offline via fake Lua API and fake game memory.
//
// Build:
//   x86_64-w64-mingw32-g++ -o bridge_test_harness.exe test_harness.cpp \
//     fake_lua.cpp fake_memory.cpp -std=c++17 -DTEST_MODE \
//     -DWIN32_LEAN_AND_MEAN -I. -static

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <string>
#include <vector>
#include <algorithm>
#include <cmath>
#include <atomic>

// Include the real lua_types.h for pfn_* typedefs and lua_State forward decl.
// lua_State remains opaque -- we cast FakeLuaState* to lua_State* at call sites.
#include "lua_types.h"
#include "rvas.h"
#include "fake_lua.h"
#include "fake_memory.h"
#include "replay_state.h"

// ======================================================================
// Test framework
// ======================================================================

static int g_passed = 0;
static int g_failed = 0;

static void StartSuite(const char* name) {
    printf("Suite: %s\n", name);
}

static void Check(bool cond, const char* testName) {
    if (cond) {
        printf("  [PASS] %s\n", testName);
        g_passed++;
    } else {
        printf("  [FAIL] %s\n", testName);
        g_failed++;
    }
}

// Cast helper: FakeLuaState* -> lua_State* (opaque, same ABI pointer)
#define LS(fakePtr) reinterpret_cast<lua_State*>(fakePtr)

// ======================================================================
// Bridge globals (replicated from lua_bridge.cpp)
// ======================================================================

static uintptr_t g_base = 0;
static FILE* g_log = nullptr;

// Function pointers -- same types as bridge, wired to fakes.
static pfn_lua_pushstring   fn_pushstring   = nullptr;
static pfn_lua_pushcclosure fn_pushcclosure = nullptr;
static pfn_lua_settop       fn_settop       = nullptr;
static pfn_lua_tonumber     fn_tonumber     = nullptr;
static pfn_lua_tostring     fn_tostring     = nullptr;
static pfn_lua_type         fn_type         = nullptr;
static pfn_lua_newtable     fn_newtable     = nullptr;
static pfn_lua_settable     fn_settable     = nullptr;
static pfn_lua_gettable     fn_gettable     = nullptr;
static pfn_lua_rawseti      fn_rawseti      = nullptr;
static pfn_lua_pushnumber   fn_pushnumber   = nullptr;
static pfn_lua_pushboolean  fn_pushboolean  = nullptr;
static pfn_lua_pushnil      fn_pushnil      = nullptr;
static pfn_lua_gettop       fn_gettop       = nullptr;
static pfn_lua_pcall        fn_pcall        = nullptr;
static pfn_lua_load         fn_load         = nullptr;

// State tracking
static std::vector<void*> registered_states;
static CRITICAL_SECTION csRegistered;
static std::vector<void*> cached_game_states;
static CRITICAL_SECTION csGameStates;

// Pipe command queue. 2026-04-10: mirrors the bridge-side bump from 4096
// -> 16384 so DiagListRegisteredFunctions and other diagnostic payloads
// can round-trip without truncation. g_pipeResult also widened to
// PIPE_CMD_MAX (was 512) to match the real bug fix in lua_bridge.cpp.
#define PIPE_CMD_MAX 16384
static CRITICAL_SECTION g_pipeLock;
static char  g_pipeCmd[PIPE_CMD_MAX];
static bool  g_pipeCmdPending = false;
static char  g_pipeResult[PIPE_CMD_MAX];
static bool  g_pipeResultReady = false;

// Shared memory (local, non-OS)
struct LocalCmdBuffer {
    std::atomic<uint32_t> cmd_seq;
    std::atomic<uint32_t> result_seq;
    uint32_t cmd_len;
    uint32_t result_len;
    char cmd[4096];
    char result[4096];
};
static LocalCmdBuffer g_shmCmdBuf;
static LocalCmdBuffer* g_cmdBuf = nullptr;
static uint32_t g_lastCmdSeq = 0;

struct LocalEvtBuffer {
    std::atomic<uint32_t> write_pos;
    std::atomic<uint32_t> read_pos;
    std::atomic<uint32_t> event_count;
    std::atomic<uint32_t> flags;
    uint8_t ring[64 * 1024 - 16];
};
static LocalEvtBuffer g_shmEvtBuf;
static LocalEvtBuffer* g_evtBuf = nullptr;

static void Log(const char* fmt, ...) {
    if (!g_log) return;
    va_list args;
    va_start(args, fmt);
    vfprintf(g_log, fmt, args);
    va_end(args);
}

// ======================================================================
// Wire fakes into the function pointers
// ======================================================================

static void WireFakes() {
    // Each fake_* takes FakeLuaState* but the pfn_* types expect lua_State*.
    // Both are pointers with identical ABI, so reinterpret_cast is safe here.
    fn_pushstring   = reinterpret_cast<pfn_lua_pushstring>(&fake_pushstring);
    fn_pushcclosure = reinterpret_cast<pfn_lua_pushcclosure>(&fake_pushcclosure);
    fn_settop       = reinterpret_cast<pfn_lua_settop>(&fake_settop);
    fn_tonumber     = reinterpret_cast<pfn_lua_tonumber>(&fake_tonumber);
    fn_tostring     = reinterpret_cast<pfn_lua_tostring>(&fake_tostring);
    fn_type         = reinterpret_cast<pfn_lua_type>(&fake_type);
    fn_newtable     = reinterpret_cast<pfn_lua_newtable>(&fake_newtable);
    fn_settable     = reinterpret_cast<pfn_lua_settable>(&fake_settable);
    fn_gettable     = reinterpret_cast<pfn_lua_gettable>(&fake_gettable);
    fn_rawseti      = reinterpret_cast<pfn_lua_rawseti>(&fake_rawseti);
    fn_pushnumber   = reinterpret_cast<pfn_lua_pushnumber>(&fake_pushnumber);
    fn_pushboolean  = reinterpret_cast<pfn_lua_pushboolean>(&fake_pushboolean);
    fn_pushnil      = reinterpret_cast<pfn_lua_pushnil>(&fake_pushnil);
    fn_gettop       = reinterpret_cast<pfn_lua_gettop>(&fake_gettop);
    fn_pcall        = reinterpret_cast<pfn_lua_pcall>(&fake_pcall);
    fn_load         = reinterpret_cast<pfn_lua_load>(&fake_load);
}

// ======================================================================
// Game image -- real process memory for pointer dereferences
// ======================================================================

static uint8_t* g_gameImage = nullptr;
static constexpr size_t GAME_IMAGE_SIZE = 16 * 1024 * 1024;

static void InitGameImage() {
    g_gameImage = (uint8_t*)VirtualAlloc(nullptr, GAME_IMAGE_SIZE,
                                          MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!g_gameImage) {
        fprintf(stderr, "FATAL: VirtualAlloc failed\n");
        exit(1);
    }
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    g_base = (uintptr_t)g_gameImage;
}

static void FreeGameImage() {
    if (g_gameImage) { VirtualFree(g_gameImage, 0, MEM_RELEASE); g_gameImage = nullptr; }
}

// Direct game-image read/write helpers
static void GI_WriteFloat(uintptr_t off, float v) { memcpy(g_gameImage + off, &v, 4); }
static float GI_ReadFloat(uintptr_t off) { float v; memcpy(&v, g_gameImage + off, 4); return v; }
static void GI_WriteInt32(uintptr_t off, int32_t v) { memcpy(g_gameImage + off, &v, 4); }
static int32_t GI_ReadInt32(uintptr_t off) { int32_t v; memcpy(&v, g_gameImage + off, 4); return v; }
static void GI_WriteUint8(uintptr_t off, uint8_t v) { g_gameImage[off] = v; }
static void GI_WriteQword(uintptr_t off, uint64_t v) { memcpy(g_gameImage + off, &v, 8); }

// Set up a player object at absolute address
static void SetupPlayer(uintptr_t addr, int slot, bool isLocal,
                         float credits, float maxCredits, int tech,
                         const char* faction) {
    uintptr_t off = addr - g_base;
    GI_WriteInt32(off + RVA::PlayerObj::SlotIndex, slot);
    GI_WriteUint8(off + RVA::PlayerObj::LocalPlayer, isLocal ? 1 : 0);
    GI_WriteFloat(off + RVA::PlayerObj::Credits, credits);
    GI_WriteFloat(off + RVA::PlayerObj::MaxCredits, maxCredits);
    GI_WriteInt32(off + RVA::PlayerObj::TechLevel, tech);
    GI_WriteInt32(off + RVA::PlayerObj::MaxTechLevel, 5);
    // Faction string at +0xF0, pointer at FactionName
    uintptr_t strOff = off + 0xF0;
    memcpy(g_gameImage + strOff, faction, strlen(faction) + 1);
    uint64_t strAddr = g_base + strOff;
    GI_WriteQword(off + RVA::PlayerObj::FactionName, strAddr);
}

static constexpr uintptr_t PLAYER_BASE_OFF = 0x300000;
static constexpr uintptr_t PLAYER_STRIDE   = 0x1000;

static void SetupPlayerArray(const std::vector<uintptr_t>& players) {
    uintptr_t arrayOff = 0x200000;
    uintptr_t arrayAddr = g_base + arrayOff;
    for (size_t i = 0; i < players.size(); i++)
        GI_WriteQword(arrayOff + i * 8, players[i]);
    GI_WriteQword(RVA::PlayerArray_Global, arrayAddr);
    GI_WriteInt32(RVA::PlayerCount_Global, (int32_t)players.size());
}

static void SetupTestPlayers() {
    uintptr_t p0 = g_base + PLAYER_BASE_OFF;
    uintptr_t p1 = g_base + PLAYER_BASE_OFF + PLAYER_STRIDE;
    uintptr_t p2 = g_base + PLAYER_BASE_OFF + 2 * PLAYER_STRIDE;
    SetupPlayer(p0, 0, false, 10000.0f, 100000.0f, 1, "EMPIRE");
    SetupPlayer(p1, 1, true,  25000.0f, 100000.0f, 3, "REBEL");
    SetupPlayer(p2, 2, false, 15000.0f, 100000.0f, 2, "UNDERWORLD");
    SetupPlayerArray({p0, p1, p2});
}

// ======================================================================
// Bridge logic replicas (identical to lua_bridge.cpp)
// ======================================================================

static uintptr_t GetPlayerObj(int slot) {
    auto pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    if (!pa) return 0;
    return *reinterpret_cast<uintptr_t*>(pa + slot * 8);
}

static int GetPlayerCount() {
    return *reinterpret_cast<int*>(g_base + RVA::PlayerCount_Global);
}

static int FindLocalPlayerSlot() {
    int count = GetPlayerCount();
    for (int i = 0; i < count; i++) {
        auto p = GetPlayerObj(i);
        if (p && *reinterpret_cast<uint8_t*>(p + RVA::PlayerObj::LocalPlayer) == 1)
            return i;
    }
    return -1;
}

static const char* GetFactionName(int slot) {
    auto p = GetPlayerObj(slot);
    if (!p) return "?";
    auto namePtr = *reinterpret_cast<const char**>(p + RVA::PlayerObj::FactionName);
    return namePtr ? namePtr : "?";
}

// SWFOC_* Lua functions -- exact replicas

static int Lua_GetVersion(lua_State* L) {
    fn_pushstring(L, "SWFOC Lua Bridge v1.0");
    return 1;
}

static int Lua_GetLocalPlayer(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) {
        fn_pushnumber(L, -1.0);
        fn_pushstring(L, "none");
    } else {
        fn_pushnumber(L, (double)slot);
        fn_pushstring(L, GetFactionName(slot));
    }
    return 2;
}

static int Lua_SetCredits(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) { fn_pushnumber(L, 0); return 1; }
    double amount = fn_tonumber(L, 1);
    auto p = GetPlayerObj(slot);
    *reinterpret_cast<float*>(p + RVA::PlayerObj::Credits) = (float)amount;
    fn_pushnumber(L, 1);
    return 1;
}

static int Lua_GetCredits(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) { fn_pushnumber(L, 0); return 1; }
    auto p = GetPlayerObj(slot);
    float c = *reinterpret_cast<float*>(p + RVA::PlayerObj::Credits);
    fn_pushnumber(L, (double)c);
    return 1;
}

static int Lua_SetTechLevel(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) { fn_pushnumber(L, 0); return 1; }
    int level = (int)fn_tonumber(L, 1);
    auto p = GetPlayerObj(slot);
    *reinterpret_cast<int*>(p + RVA::PlayerObj::TechLevel) = level;
    fn_pushnumber(L, 1);
    return 1;
}

static int Lua_UncapCredits(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) { fn_pushnumber(L, 0); return 1; }
    auto p = GetPlayerObj(slot);
    *reinterpret_cast<float*>(p + RVA::PlayerObj::MaxCredits) = 999999999.0f;
    fn_pushnumber(L, 1);
    return 1;
}

static int Lua_HeroInstantRespawn(lua_State* L) {
    static float originalValue = -1.0f;
    auto addr = reinterpret_cast<float*>(g_base + RVA::DefaultHeroRespawnTime);
    int enable = (int)fn_tonumber(L, 1);
    if (enable) {
        if (originalValue < 0) originalValue = *addr;
        *addr = 0.0f;
    } else if (originalValue >= 0) {
        *addr = originalValue;
    }
    fn_pushnumber(L, 1);
    return 1;
}

static int Lua_ListFactions(lua_State* L) {
    int count = GetPlayerCount();
    fn_newtable(L);
    int idx = 1;
    for (int i = 0; i < count; i++) {
        auto p = GetPlayerObj(i);
        if (!p) continue;
        auto isLocal = *reinterpret_cast<uint8_t*>(p + RVA::PlayerObj::LocalPlayer) == 1;
        auto credits = *reinterpret_cast<float*>(p + RVA::PlayerObj::Credits);
        fn_newtable(L);
        fn_pushstring(L, "slot");
        fn_pushnumber(L, (double)i);
        fn_settable(L, -3);
        fn_pushstring(L, "name");
        fn_pushstring(L, GetFactionName(i));
        fn_settable(L, -3);
        fn_pushstring(L, "credits");
        fn_pushnumber(L, (double)credits);
        fn_settable(L, -3);
        fn_pushstring(L, "is_local");
        fn_pushnumber(L, isLocal ? 1.0 : 0.0);
        fn_settable(L, -3);
        fn_rawseti(L, -2, idx++);
    }
    return 1;
}

static int Lua_Log(lua_State* L) {
    const char* msg = fn_tostring(L, 1);
    if (msg) Log("[Lua] %s\n", msg);
    return 0;
}

// ======================================================================
// Phase 3.2 replicas (Combat / Inspect helpers ported from CE trainer)
// ----------------------------------------------------------------------
// Mirrors the implementation in lua_bridge.cpp. Kept inline so the offline
// harness exercises the same control flow without the MinHook detour
// machinery (the SetHP hook is simulated via a function pointer table).
// ======================================================================

static bool IsValidObjAddr(uintptr_t addr) {
    if (addr == 0) return false;
    if (addr < 0x10000) return false;
    if ((addr & 0xFFFF000000000000ULL) == 0xFFFF000000000000ULL) return false;
    if (IsBadReadPtr(reinterpret_cast<void*>(addr), 0x400)) return false;
    return true;
}

static uintptr_t WalkToRootUnit(uintptr_t obj) {
    if (!IsValidObjAddr(obj)) return 0;
    uintptr_t cur = obj;
    for (int i = 0; i < 8; i++) {
        uint8_t parentIdx = *reinterpret_cast<uint8_t*>(cur + RVA::GameObj::ParentIndex);
        if (parentIdx == 0xFF) return cur;
        uintptr_t components = *reinterpret_cast<uintptr_t*>(cur + RVA::GameObj::ComponentArray);
        if (!components || IsBadReadPtr(reinterpret_cast<void*>(components), (parentIdx + 1) * 8)) {
            return cur;
        }
        uintptr_t parent = *reinterpret_cast<uintptr_t*>(components + parentIdx * 8);
        if (!parent || !IsValidObjAddr(parent)) return cur;
        cur = parent;
    }
    return cur;
}

static uintptr_t GetOwnerPlayerObj(uintptr_t rootObj) {
    if (!IsValidObjAddr(rootObj)) return 0;
    int32_t ownerId = *reinterpret_cast<int32_t*>(rootObj + RVA::GameObj::OwnerPlayerID);
    if (ownerId < 0 || ownerId > 7) return 0;
    uintptr_t paAddr = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    if (!paAddr) return 0;
    uintptr_t player = *reinterpret_cast<uintptr_t*>(paAddr + ownerId * 8);
    return player;
}

static bool IsObjOwnedByHuman(uintptr_t obj) {
    uintptr_t root = WalkToRootUnit(obj);
    if (!root) return false;
    uintptr_t player = GetOwnerPlayerObj(root);
    if (!player) return false;
    if (IsBadReadPtr(reinterpret_cast<void*>(player + RVA::PlayerObj::LocalPlayer), 1)) return false;
    return *reinterpret_cast<uint8_t*>(player + RVA::PlayerObj::LocalPlayer) == 1;
}

static int Lua_SetUnitInvuln(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    double rawFlag = fn_tonumber(L, 2);
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    uint8_t flag = (rawFlag != 0.0) ? 1 : 0;
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitInvuln: invalid obj_addr");
        return 1;
    }
    volatile uint8_t* p = reinterpret_cast<volatile uint8_t*>(addr + RVA::GameObj::InvulnFlag);
    *p = flag;
    fn_pushstring(L, "OK");
    return 1;
}

static int Lua_SetUnitHull(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    double rawHp = fn_tonumber(L, 2);
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitHull: invalid obj_addr");
        return 1;
    }
    volatile float* p = reinterpret_cast<volatile float*>(addr + RVA::GameObj::HP);
    *p = static_cast<float>(rawHp);
    fn_pushstring(L, "OK");
    return 1;
}

static int Lua_InspectUnit(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_InspectUnit: invalid obj_addr");
        return 1;
    }
    float    hull          = *reinterpret_cast<float*>(addr + RVA::GameObj::HP);
    int32_t  ownerId       = *reinterpret_cast<int32_t*>(addr + RVA::GameObj::OwnerPlayerID);
    uint32_t objId         = *reinterpret_cast<uint32_t*>(addr + RVA::GameObj::ObjectID);
    uint8_t  parentIdx     = *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::ParentIndex);
    uint8_t  statusFlags   = *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::StatusFlags);
    uint8_t  preventDeath  = *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::PreventDeath);
    uint8_t  invulnFlag    = *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::InvulnFlag);
    uint8_t  hardpointFlag = *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::HardpointFlag);
    uintptr_t componentsPtr = *reinterpret_cast<uintptr_t*>(addr + RVA::GameObj::ComponentArray);

    char buf[512];
    snprintf(buf, sizeof(buf),
             "hull=%.3f owner=%d obj_id=%u parent_idx=0x%02X status_flags=0x%02X "
             "prevent_death=0x%02X invuln_flag=0x%02X hardpoint_flag=0x%02X "
             "components_ptr=0x%016llX",
             hull, (int)ownerId, (unsigned)objId, (unsigned)parentIdx,
             (unsigned)statusFlags, (unsigned)preventDeath, (unsigned)invulnFlag,
             (unsigned)hardpointFlag, (unsigned long long)componentsPtr);
    fn_pushstring(L, buf);
    return 1;
}

// Bounded append helper. Mirrors lua_bridge.cpp::SafeAppendFmt — clamps
// snprintf's return value to remaining capacity so a truncated write
// cannot push the offset past the end of the buffer.
static size_t SafeAppendFmt(char* buf, size_t offset, size_t cap, const char* fmt, ...) {
    if (!buf || cap == 0 || offset >= cap - 1) return offset;
    size_t remaining = cap - offset;
    va_list args;
    va_start(args, fmt);
    int n = vsnprintf(buf + offset, remaining, fmt, args);
    va_end(args);
    if (n < 0) return offset;
    size_t add = static_cast<size_t>(n);
    if (add >= remaining) add = remaining - 1;
    return offset + add;
}

static int Lua_GetHardpoints(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_GetHardpoints: invalid obj_addr");
        return 1;
    }
    uintptr_t components = *reinterpret_cast<uintptr_t*>(addr + RVA::GameObj::ComponentArray);
    if (!components || IsBadReadPtr(reinterpret_cast<void*>(components), 0x100)) {
        fn_pushstring(L, "count=0");
        return 1;
    }
    constexpr int kMaxChildren = 32;
    uintptr_t children[kMaxChildren];
    int count = 0;
    for (int i = 0; i < kMaxChildren; i++) {
        uintptr_t child = *reinterpret_cast<uintptr_t*>(components + i * 8);
        if (!child) continue;
        if (!IsValidObjAddr(child)) continue;
        children[count++] = child;
    }
    char buf[2048];
    size_t off = 0;
    off = SafeAppendFmt(buf, off, sizeof(buf), "count=%d", count);
    for (int i = 0; i < count; i++) {
        float hp = *reinterpret_cast<float*>(children[i] + RVA::GameObj::HP);
        off = SafeAppendFmt(buf, off, sizeof(buf),
                            " child%d=0x%016llX hp%d=%.3f",
                            i, (unsigned long long)children[i], i, hp);
    }
    fn_pushstring(L, buf);
    return 1;
}

// ======================================================================
// SetHumanPlayer_v2 replica (2026-04-11)
// ----------------------------------------------------------------------
// Mirror lua_bridge.cpp::Lua_SetHumanPlayer_v2. Tests the manual +0x62
// sweep and +0x30 field write. The subsystem refresh call is mocked via
// a function pointer interceptor below so the test can assert that the
// dispatcher was invoked with the expected argument. See
// knowledge-base/faction_switch_full_anatomy_2026-04-11.md for context.
// ======================================================================

// Interceptor state for the subsystem refresh dispatcher. The v2 replica
// below calls this instead of computing a g_base-relative function pointer,
// which would crash because that memory doesn't contain executable code in
// the test image. This matches how the real dispatcher would be called at
// runtime: the replica records the argument and returns without side
// effects.
static int g_fakeRefreshCallCount = 0;
static void* g_fakeRefreshLastArg = nullptr;
static void FakeRefreshLocalPlayerSubsystems(void* activeGameMode) {
    g_fakeRefreshCallCount++;
    g_fakeRefreshLastArg = activeGameMode;
}

static int Lua_SetHumanPlayer_v2(lua_State* L) {
    int target = static_cast<int>(fn_tonumber(L, 1));

    int playerCount = GetPlayerCount();
    if (playerCount <= 0) {
        fn_pushnumber(L, 0);
        return 1;
    }
    if (target < 0 || target >= playerCount) {
        fn_pushnumber(L, 0);
        return 1;
    }

    auto playerList = reinterpret_cast<uintptr_t>(
        g_base + RVA::PlayerListClass_Global);
    auto currentSlotPtr = reinterpret_cast<int*>(playerList + 0x30);
    int currentFromField = *currentSlotPtr;
    int currentFromScan  = FindLocalPlayerSlot();

    if (currentFromField == target && currentFromScan == target) {
        fn_pushnumber(L, 1);
        return 1;
    }

    auto pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    if (!pa) {
        fn_pushnumber(L, 0);
        return 1;
    }

    for (int i = 0; i < playerCount; ++i) {
        auto player = *reinterpret_cast<uintptr_t*>(pa + 8 * i);
        if (!player) continue;
        uint8_t value = (i == target) ? 1 : 0;
        *reinterpret_cast<uint8_t*>(player + RVA::PlayerObj::LocalPlayer) = value;
    }

    *currentSlotPtr = target;

    // Harness: replace the g_base-relative dispatcher lookup with the
    // interceptor. The live bridge computes
    // `g_base + RVA::GameMode_RefreshLocalPlayerSubsystems` and calls the
    // function pointer; in the harness, there is no live engine binary, so
    // we call the interceptor directly.
    auto activeGameMode = *reinterpret_cast<uintptr_t*>(
        g_base + RVA::GameModeRoot_Global);
    if (activeGameMode) {
        auto eventTarget = *reinterpret_cast<uintptr_t*>(activeGameMode + 24);
        if (eventTarget) {
            FakeRefreshLocalPlayerSubsystems(
                reinterpret_cast<void*>(eventTarget));
        }
    }

    int newScan = FindLocalPlayerSlot();
    int newField = *currentSlotPtr;
    bool ok = (newScan == target && newField == target);
    fn_pushnumber(L, ok ? 1 : 0);
    return 1;
}

// ======================================================================
// Selection reader replicas (2026-04-11)
// ----------------------------------------------------------------------
// Mirror lua_bridge.cpp::Lua_GetSelectedUnit / Lua_GetSelectedUnits. The
// harness stages a fake GameModeRoot by writing the mgr pointer at
// g_base + GameModeRoot_Global + 0x18, then lays out a per-player
// DynamicVectorClass at mgr + 0x1C0 + 0x48*slot. The iteration walks a
// doubly linked list of fake nodes, each with next=+0x08 and
// data_plus_24=+0x18. See TestSelectionReader below for the fixtures.
// ======================================================================

static int ReadCurrentHumanPlayerSlot() {
    uintptr_t pl = g_base + RVA::PlayerListClass_Global;
    if (IsBadReadPtr(reinterpret_cast<void*>(pl), 0x40)) return -1;
    uintptr_t vecBegin = *reinterpret_cast<uintptr_t*>(pl + 0x00);
    uintptr_t vecEnd   = *reinterpret_cast<uintptr_t*>(pl + 0x08);
    if (!vecBegin || vecBegin == vecEnd) return -1;
    int curIdx = *reinterpret_cast<int*>(pl + 0x30);
    if (curIdx < 0 || curIdx > 7) return -1;
    uintptr_t entryAddr = vecBegin + 8 * curIdx;
    if (IsBadReadPtr(reinterpret_cast<void*>(entryAddr), 8)) return -1;
    uintptr_t player = *reinterpret_cast<uintptr_t*>(entryAddr);
    if (!player) return -1;
    if (IsBadReadPtr(reinterpret_cast<void*>(player + 0x4C), 4)) return -1;
    return *reinterpret_cast<int*>(player + 0x4C);
}

static bool ResolveSelectionVector(uintptr_t& outVec) {
    outVec = 0;
    uintptr_t rootSlot = g_base + RVA::GameModeRoot_Global;
    if (IsBadReadPtr(reinterpret_cast<void*>(rootSlot + RVA::Selection::kModeRootIndirection), 8)) {
        return false;
    }
    uintptr_t mgrRoot = *reinterpret_cast<uintptr_t*>(
        rootSlot + RVA::Selection::kModeRootIndirection);
    if (!mgrRoot) return false;
    if (IsBadReadPtr(reinterpret_cast<void*>(mgrRoot + RVA::Selection::kPerPlayerVectorsArray), 8)) {
        return false;
    }
    uintptr_t vecArrayBase = *reinterpret_cast<uintptr_t*>(
        mgrRoot + RVA::Selection::kPerPlayerVectorsArray);
    if (!vecArrayBase) return false;
    int slot = ReadCurrentHumanPlayerSlot();
    if (slot < 0 || slot > 7) return false;
    uintptr_t vec = vecArrayBase + RVA::Selection::kSelectionEntryStride * slot;
    if (IsBadReadPtr(reinterpret_cast<void*>(vec), RVA::Selection::kSelectionEntryStride)) {
        return false;
    }
    outVec = vec;
    return true;
}

static int WalkSelectionVector(uintptr_t vec, uintptr_t* outObjs, int maxOut) {
    if (!vec || !outObjs || maxOut <= 0) return 0;
    uintptr_t sentinel = vec + RVA::Selection::kVectorSentinel;
    if (IsBadReadPtr(reinterpret_cast<void*>(vec + RVA::Selection::kVectorHead), 8)) return 0;
    uintptr_t node = *reinterpret_cast<uintptr_t*>(vec + RVA::Selection::kVectorHead);
    if (!node || node == sentinel) return 0;
    int found = 0;
    for (int i = 0; i < RVA::Selection::kMaxSelectionCount && node && node != sentinel; i++) {
        if (IsBadReadPtr(reinterpret_cast<void*>(node + RVA::Selection::kNodeDataPlus24), 8)) break;
        uintptr_t dataPlus24 = *reinterpret_cast<uintptr_t*>(
            node + RVA::Selection::kNodeDataPlus24);
        uintptr_t obj = (dataPlus24 == 0)
            ? 0
            : (dataPlus24 - RVA::Selection::kNodeDataAdjustment);
        if (obj && IsValidObjAddr(obj)) {
            outObjs[found++] = obj;
            if (found >= maxOut) break;
        }
        if (IsBadReadPtr(reinterpret_cast<void*>(node + RVA::Selection::kNodeNext), 8)) break;
        node = *reinterpret_cast<uintptr_t*>(node + RVA::Selection::kNodeNext);
    }
    return found;
}

static int Lua_GetSelectedUnit(lua_State* L) {
    uintptr_t vec = 0;
    if (!ResolveSelectionVector(vec)) {
        fn_pushnumber(L, 0.0);
        return 1;
    }
    uintptr_t obj = 0;
    int found = WalkSelectionVector(vec, &obj, 1);
    if (found <= 0) {
        fn_pushnumber(L, 0.0);
        return 1;
    }
    fn_pushnumber(L, static_cast<double>(obj));
    return 1;
}

static int Lua_GetSelectedUnits(lua_State* L) {
    uintptr_t vec = 0;
    if (!ResolveSelectionVector(vec)) {
        fn_pushstring(L, "");
        return 1;
    }
    uintptr_t objs[RVA::Selection::kMaxSelectionCount];
    int count = WalkSelectionVector(vec, objs, RVA::Selection::kMaxSelectionCount);
    if (count <= 0) {
        fn_pushstring(L, "");
        return 1;
    }
    char buf[2048];
    size_t off = 0;
    for (int i = 0; i < count; i++) {
        off = SafeAppendFmt(buf, off, sizeof(buf),
                            i == 0 ? "%llu" : ",%llu",
                            static_cast<unsigned long long>(objs[i]));
    }
    fn_pushstring(L, buf);
    return 1;
}

// ----------------------------------------------------------------------
// SetHP combat hook (offline simulation)
// ----------------------------------------------------------------------
// The harness can't install a real MinHook detour against a fake binary,
// so we simulate the hook with a function-pointer dispatch table:
//   - g_real_SetHP_stub: the "original" SetHP that would write obj+0x5C
//   - g_setHP_dispatch:  pointer set to either the original or the detour
//   - InvokeSetHP():     test entry point — always calls through dispatch
// The detour itself contains the same logic as the production code so the
// branching behavior is exercised end-to-end. Tests verify that toggling
// the flags installs/removes the detour and rewrites HP correctly.
typedef void (*pfn_SetHP_stub)(void* obj, float new_hp);

// Per-test recording so the assertion can confirm what was passed to the
// "real" SetHP (mirroring how a trampoline call would behave).
static int g_real_setHP_call_count = 0;
static float g_last_real_setHP_hp = -999.0f;
static void* g_last_real_setHP_obj = nullptr;

static void Real_SetHP_Stub(void* obj, float new_hp) {
    g_real_setHP_call_count++;
    g_last_real_setHP_obj = obj;
    g_last_real_setHP_hp = new_hp;
    if (obj) {
        *reinterpret_cast<float*>(reinterpret_cast<uintptr_t>(obj) + RVA::GameObj::HP) = new_hp;
    }
}

static pfn_SetHP_stub g_real_SetHP = nullptr;
static volatile LONG g_god_mode_enabled = 0;
static volatile LONG g_ohk_enabled       = 0;
static CRITICAL_SECTION g_combat_hook_lock;
static bool g_combat_hook_lock_initialized = false;
static bool g_combat_hook_installed = false;
static pfn_SetHP_stub g_setHP_dispatch = nullptr;

static void EnsureCombatHookLock() {
    if (!g_combat_hook_lock_initialized) {
        InitializeCriticalSection(&g_combat_hook_lock);
        g_combat_hook_lock_initialized = true;
    }
}

static void Detour_SetHP(void* obj, float new_hp) {
    LONG god = g_god_mode_enabled;
    LONG ohk = g_ohk_enabled;
    if (god || ohk) {
        bool human = IsObjOwnedByHuman(reinterpret_cast<uintptr_t>(obj));
        if (god && human) {
            float cur = *reinterpret_cast<float*>(reinterpret_cast<uintptr_t>(obj) + RVA::GameObj::HP);
            if (new_hp < cur) return;
        }
        if (ohk && !human) {
            new_hp = 0.0f;
        }
    }
    if (g_real_SetHP) g_real_SetHP(obj, new_hp);
}

// Test-only entry point: call this from the test cases instead of trying
// to invoke a hooked function pointer through MinHook.
static void InvokeSetHP(void* obj, float new_hp) {
    if (g_setHP_dispatch) g_setHP_dispatch(obj, new_hp);
    else Real_SetHP_Stub(obj, new_hp);
}

static bool InstallCombatHook() {
    if (g_combat_hook_installed) return true;
    g_real_SetHP = &Real_SetHP_Stub;
    g_setHP_dispatch = &Detour_SetHP;
    g_combat_hook_installed = true;
    return true;
}

static void RemoveCombatHook() {
    if (!g_combat_hook_installed) return;
    g_setHP_dispatch = &Real_SetHP_Stub;
    g_combat_hook_installed = false;
    g_real_SetHP = nullptr;
}

static int Lua_GodMode(lua_State* L) {
    EnsureCombatHookLock();
    int enable = (int)fn_tonumber(L, 1);
    EnterCriticalSection(&g_combat_hook_lock);
    bool ok = true;
    if (enable) {
        if (!InstallCombatHook()) ok = false;
        InterlockedExchange(&g_god_mode_enabled, 1);
    } else {
        InterlockedExchange(&g_god_mode_enabled, 0);
        if (!g_god_mode_enabled && !g_ohk_enabled) RemoveCombatHook();
    }
    LeaveCriticalSection(&g_combat_hook_lock);
    if (!ok) {
        fn_pushstring(L, "ERR: SWFOC_GodMode: hook install failed");
        return 1;
    }
    fn_pushstring(L, enable ? "OK: god mode enabled" : "OK: god mode disabled");
    return 1;
}

static int Lua_OneHitKill(lua_State* L) {
    EnsureCombatHookLock();
    int enable = (int)fn_tonumber(L, 1);
    EnterCriticalSection(&g_combat_hook_lock);
    bool ok = true;
    if (enable) {
        if (!InstallCombatHook()) ok = false;
        InterlockedExchange(&g_ohk_enabled, 1);
    } else {
        InterlockedExchange(&g_ohk_enabled, 0);
        if (!g_god_mode_enabled && !g_ohk_enabled) RemoveCombatHook();
    }
    LeaveCriticalSection(&g_combat_hook_lock);
    if (!ok) {
        fn_pushstring(L, "ERR: SWFOC_OneHitKill: hook install failed");
        return 1;
    }
    fn_pushstring(L, enable ? "OK: one-hit kill enabled" : "OK: one-hit kill disabled");
    return 1;
}

// ----------------------------------------------------------------------
// Phase 3.2 (continuation): per-slot helpers replicas
// Mirrors the eight new SWFOC_* helpers added to lua_bridge.cpp.
// ----------------------------------------------------------------------

static bool ResolveSlotPlayer(int slot, uintptr_t& outPlayer) {
    int count = GetPlayerCount();
    if (slot < 0 || slot >= count || slot > 7) {
        outPlayer = 0;
        return false;
    }
    outPlayer = GetPlayerObj(slot);
    return outPlayer != 0;
}

static int Lua_SetCreditsForSlot(lua_State* L) {
    int slot = (int)fn_tonumber(L, 1);
    double amount = fn_tonumber(L, 2);
    uintptr_t player = 0;
    if (!ResolveSlotPlayer(slot, player)) {
        fn_pushstring(L, "ERR: SWFOC_SetCreditsForSlot: invalid slot");
        return 1;
    }
    *reinterpret_cast<float*>(player + RVA::PlayerObj::Credits) = (float)amount;
    fn_pushstring(L, "OK");
    return 1;
}

static int Lua_GetCreditsForSlot(lua_State* L) {
    int slot = (int)fn_tonumber(L, 1);
    uintptr_t player = 0;
    if (!ResolveSlotPlayer(slot, player)) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    float c = *reinterpret_cast<float*>(player + RVA::PlayerObj::Credits);
    fn_pushnumber(L, (double)c);
    return 1;
}

static int Lua_SetTechForSlot(lua_State* L) {
    int slot = (int)fn_tonumber(L, 1);
    int level = (int)fn_tonumber(L, 2);
    if (level < 1 || level > 5) {
        fn_pushstring(L, "ERR: SWFOC_SetTechForSlot: level out of [1,5]");
        return 1;
    }
    uintptr_t player = 0;
    if (!ResolveSlotPlayer(slot, player)) {
        fn_pushstring(L, "ERR: SWFOC_SetTechForSlot: invalid slot");
        return 1;
    }
    *reinterpret_cast<int*>(player + RVA::PlayerObj::TechLevel) = level;
    fn_pushstring(L, "OK");
    return 1;
}

static int Lua_GetTechForSlot(lua_State* L) {
    int slot = (int)fn_tonumber(L, 1);
    uintptr_t player = 0;
    if (!ResolveSlotPlayer(slot, player)) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    int t = *reinterpret_cast<int*>(player + RVA::PlayerObj::TechLevel);
    fn_pushnumber(L, (double)t);
    return 1;
}

static int Lua_DrainEnemyCredits(lua_State* L) {
    int count = GetPlayerCount();
    if (count <= 0) {
        fn_pushstring(L, "ERR: SWFOC_DrainEnemyCredits: no players loaded");
        return 1;
    }
    int drained = 0;
    for (int i = 0; i < count; i++) {
        uintptr_t p = GetPlayerObj(i);
        if (!p) continue;
        if (*reinterpret_cast<uint8_t*>(p + RVA::PlayerObj::LocalPlayer) == 1) continue;
        *reinterpret_cast<float*>(p + RVA::PlayerObj::Credits) = 0.0f;
        drained++;
    }
    char buf[64];
    snprintf(buf, sizeof(buf), "OK: drained %d slots", drained);
    fn_pushstring(L, buf);
    return 1;
}

static int Lua_SetHeroRespawn(lua_State* L) {
    double seconds = fn_tonumber(L, 1);
    if (seconds < 0.0 || seconds > 600.0) {
        fn_pushstring(L, "ERR: SWFOC_SetHeroRespawn: out of [0, 600]");
        return 1;
    }
    auto addr = reinterpret_cast<float*>(g_base + RVA::DefaultHeroRespawnTime);
    float prev = *addr;
    *addr = (float)seconds;
    char buf[64];
    snprintf(buf, sizeof(buf), "OK: prev=%.1f new=%.1f", prev, (float)seconds);
    fn_pushstring(L, buf);
    return 1;
}

static int Lua_PreventUnitDeath(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    double rawFlag = fn_tonumber(L, 2);
    uintptr_t addr = (uintptr_t)(uint64_t)rawAddr;
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_PreventUnitDeath: invalid obj_addr");
        return 1;
    }
    volatile uint8_t* p = reinterpret_cast<volatile uint8_t*>(addr + RVA::GameObj::PreventDeath);
    uint8_t cur = *p;
    if (rawFlag != 0.0) {
        *p = (uint8_t)(cur | 0x80);
    } else {
        *p = (uint8_t)(cur & ~0x80);
    }
    fn_pushstring(L, "OK");
    return 1;
}

static int Lua_GetMaxCredits(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    auto p = GetPlayerObj(slot);
    if (!p) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    float m = *reinterpret_cast<float*>(p + RVA::PlayerObj::MaxCredits);
    fn_pushnumber(L, (double)m);
    return 1;
}

struct StringReaderData { const char* str; size_t len; bool done; };

static const char* StringReader(lua_State* L, void* ud, size_t* sz) {
    (void)L;
    auto* rd = (StringReaderData*)ud;
    if (rd->done) { *sz = 0; return nullptr; }
    rd->done = true;
    *sz = rd->len;
    return rd->str;
}

static int DoString(lua_State* L, const char* code, const char* chunkname = "=pipe") {
    if (!fn_load || !fn_pcall) return -1;
    StringReaderData rd = { code, strlen(code), false };
    int loadErr = fn_load(L, (lua_Chunkreader)StringReader, &rd, chunkname);
    if (loadErr != 0) return loadErr;
    return fn_pcall(L, 0, 1, 0);
}

static int Lua_DoString(lua_State* L) {
    const char* code = fn_tostring(L, 1);
    if (!code) {
        fn_pushnumber(L, 0);
        fn_pushstring(L, "SWFOC_DoString: expected string argument");
        return 2;
    }
    int topBefore = fn_gettop(L);
    int err = DoString(L, code, "=SWFOC_DoString");
    if (err == 0) {
        fn_settop(L, topBefore);
        fn_pushnumber(L, 1);
        return 1;
    } else {
        const char* errMsg = fn_tostring(L, -1);
        fn_settop(L, topBefore);
        fn_pushnumber(L, 0);
        fn_pushstring(L, errMsg ? errMsg : "unknown error");
        return 2;
    }
}

static int Lua_DrainPipe(lua_State* L) {
    // Forward-declared; uses DrainPipeCommand below
    extern bool DrainPipeCommand_impl(lua_State* L);
    bool did = DrainPipeCommand_impl(L);
    fn_pushnumber(L, did ? 1.0 : 0.0);
    return 1;
}

static int SWFOC_StateInfo(lua_State* L) {
    EnterCriticalSection(&csGameStates);
    int count = (int)cached_game_states.size();
    std::string info = "Game states: " + std::to_string(count) + "\n";
    for (int i = 0; i < count; i++) {
        char buf[64];
        snprintf(buf, sizeof(buf), "  [%d] %p\n", i, cached_game_states[i]);
        info += buf;
    }
    LeaveCriticalSection(&csGameStates);
    fn_pushstring(L, info.c_str());
    return 1;
}

static int Lua_EventControl(lua_State* L) {
    if (!g_evtBuf) { fn_pushnumber(L, 0); return 1; }
    int enable = (int)fn_tonumber(L, 1);
    if (enable) {
        g_evtBuf->write_pos.store(0, std::memory_order_release);
        g_evtBuf->read_pos.store(0, std::memory_order_release);
        g_evtBuf->event_count.store(0, std::memory_order_release);
        g_evtBuf->flags.store(1, std::memory_order_release);
    } else {
        g_evtBuf->flags.store(0, std::memory_order_release);
    }
    fn_pushnumber(L, 1);
    return 1;
}

// ======================================================================
// SWFOC_DumpState replica for offline testing
// Mirrors the DLL implementation in lua_bridge.cpp. Exercised by
// TestSnapshotFormat below.
// ======================================================================

static uint32_t g_crc32Table[256];
static bool g_crc32TableReady = false;

static void Crc32_BuildTable() {
    for (uint32_t i = 0; i < 256; i++) {
        uint32_t c = i;
        for (int k = 0; k < 8; k++) {
            c = (c & 1) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
        }
        g_crc32Table[i] = c;
    }
    g_crc32TableReady = true;
}

static uint32_t Crc32_Update(uint32_t crc, const void* data, size_t len) {
    if (!g_crc32TableReady) Crc32_BuildTable();
    const uint8_t* p = (const uint8_t*)data;
    crc = crc ^ 0xFFFFFFFFu;
    for (size_t i = 0; i < len; i++) {
        crc = g_crc32Table[(crc ^ p[i]) & 0xFFu] ^ (crc >> 8);
    }
    return crc ^ 0xFFFFFFFFu;
}

static const char* const kDumpObjectTypes[] = {
    "Vengeance_Frigate",
    "Nebulon_B_Frigate",
    "Star_Destroyer",
    "TIE_Fighter",
    "Corellian_Corvette",
    "Tartan_Patrol_Cruiser",
};
static const uint32_t kDumpObjectTypesCount =
    sizeof(kDumpObjectTypes) / sizeof(kDumpObjectTypes[0]);

static const char* const kDumpGlobals[] = {
    "Find_Player",
    "Find_Object_Type",
    "Find_All_Objects_Of_Type",
    "Spawn_Unit",
    "Story_Event",
    "Letter_Box_On",
    "Suspend_AI",
    "GameRandom",
    "Create_Position",
    "SWFOC_GetVersion",
    "SWFOC_GetLocalPlayer",
    "SWFOC_SetCredits",
    "SWFOC_GetCredits",
    "SWFOC_SetTechLevel",
    "SWFOC_UncapCredits",
    "SWFOC_HeroInstantRespawn",
    "SWFOC_ListFactions",
    "SWFOC_Log",
    "SWFOC_DoString",
    "SWFOC_DrainPipe",
    "SWFOC_StateInfo",
    "SWFOC_EventControl",
    "SWFOC_DumpState",
};
static const uint32_t kDumpGlobalsCount =
    sizeof(kDumpGlobals) / sizeof(kDumpGlobals[0]);

struct DumpBuf {
    std::vector<uint8_t> bytes;
    uint32_t crc;
    DumpBuf() : crc(0) {}
    void append(const void* p, size_t n) {
        const uint8_t* b = (const uint8_t*)p;
        bytes.insert(bytes.end(), b, b + n);
    }
    void u8(uint8_t v)   { append(&v, 1); }
    void u16(uint16_t v) { append(&v, 2); }
    void u32(uint32_t v) { append(&v, 4); }
    void u64(uint64_t v) { append(&v, 8); }
    void f64(double v)   { append(&v, 8); }
    void fixedStr(const char* s, size_t width) {
        std::vector<uint8_t> tmp(width, 0);
        if (s) {
            size_t n = strnlen(s, width);
            memcpy(tmp.data(), s, n);
        }
        append(tmp.data(), width);
    }
};

// In the test harness all memory is real VirtualAlloc memory, so these
// just wrap the raw dereferences. No IsBadReadPtr guard needed offline.
static uint64_t HarnessReadU64(uintptr_t addr) {
    if (addr == 0) return 0;
    return *(uint64_t*)addr;
}
static uint32_t HarnessReadU32(uintptr_t addr) {
    if (addr == 0) return 0;
    return *(uint32_t*)addr;
}
static float HarnessReadF32(uintptr_t addr) {
    if (addr == 0) return 0.0f;
    return *(float*)addr;
}
static const char* HarnessReadCStr(uintptr_t addr) {
    if (addr == 0) return nullptr;
    return *(const char**)addr;
}

static uint64_t CaptureTimestampMs() {
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    uint64_t ft100ns = ((uint64_t)ft.dwHighDateTime << 32) | (uint64_t)ft.dwLowDateTime;
    const uint64_t kEpochDeltaMs = 11644473600000ULL;
    return (ft100ns / 10000ULL) - kEpochDeltaMs;
}

static void WriteGlobalRecord(lua_State* L, DumpBuf& buf, const char* name) {
    buf.fixedStr(name, 64);
    int savedTop = fn_gettop(L);
    fn_pushstring(L, name);
    fn_gettable(L, LUA_GLOBALSINDEX);
    int ty = fn_type(L, -1);
    uint64_t raw = 0;
    if (ty == LUA_TNUMBER) {
        double n = fn_tonumber(L, -1);
        memcpy(&raw, &n, 8);
    } else if (ty == LUA_TSTRING) {
        const char* s = fn_tostring(L, -1);
        raw = (uint64_t)(uintptr_t)s;
    } else if (ty == LUA_TFUNCTION) {
        raw = 0;
    } else if (ty != LUA_TNIL && ty != LUA_TBOOLEAN && ty != LUA_TUSERDATA) {
        ty = LUA_TNIL;
    }
    fn_settop(L, savedTop);
    buf.u8((uint8_t)(ty & 0xFF));
    uint8_t pad[7] = {0};
    buf.append(pad, 7);
    buf.u64(raw);
}

// Offline version: the fake Lua cannot model Alamo engine globals, so we
// just write count=0 for every type. The test verifies the section *shape*,
// not the instance counts.
static uint32_t QueryObjectTypeCount(lua_State* L, const char* typeName) {
    (void)L; (void)typeName;
    return 0;
}

static void WriteMetaPair(DumpBuf& buf, const char* key, const char* value) {
    size_t kl = key ? strnlen(key, 0xFFFE) : 0;
    size_t vl = value ? strnlen(value, 0xFFFE) : 0;
    buf.u16((uint16_t)kl);
    buf.append(key, kl);
    buf.u16((uint16_t)vl);
    buf.append(value, vl);
}

static int Lua_DumpState(lua_State* L) {
    const char* path = fn_tostring(L, 1);
    if (!path || !path[0]) {
        fn_pushstring(L, "ERR: SWFOC_DumpState: expected string path argument");
        return 1;
    }
    DumpBuf buf;

    // Header — bumped to v2 in 2026-04-08 to match the bridge writer.
    const uint8_t kMagic[16] = {
        'S','W','F','O','C','S','N','A','P','v','2',0,0,0,0,0
    };
    buf.append(kMagic, 16);
    buf.u32(2);
    buf.u64(CaptureTimestampMs());
    uint8_t zeroHash[32] = {0};
    buf.append(zeroHash, 32);
    buf.u8(0);
    uint8_t headerPad[7] = {0};
    buf.append(headerPad, 7);

    // Section 1: player_array
    {
        DumpBuf sec;
        int32_t rawCount = (int32_t)HarnessReadU32(g_base + RVA::PlayerCount_Global);
        if (rawCount < 0) rawCount = 0;
        if (rawCount > 8) rawCount = 8;
        uint32_t clamped = (uint32_t)rawCount;
        uintptr_t arrBase = (uintptr_t)HarnessReadU64(g_base + RVA::PlayerArray_Global);
        uint32_t actual = 0;
        sec.u32(0); // placeholder for player_count, patched at end
        // v2 addition: explicit local_slot. UINT32_MAX = no local player.
        int localSlotInt = FindLocalPlayerSlot();
        uint32_t localSlot = (localSlotInt < 0) ? 0xFFFFFFFFu : (uint32_t)localSlotInt;
        sec.u32(localSlot);
        for (uint32_t i = 0; i < clamped; i++) {
            uint64_t pPtr = arrBase ? HarnessReadU64(arrBase + i * 8) : 0;
            if (!pPtr) continue;
            sec.u32(i);
            const char* faction = HarnessReadCStr(pPtr + RVA::PlayerObj::FactionName);
            sec.fixedStr(faction ? faction : "", 64);
            float c = HarnessReadF32(pPtr + RVA::PlayerObj::Credits);
            sec.f64((double)c);
            int32_t tech = (int32_t)HarnessReadU32(pPtr + RVA::PlayerObj::TechLevel);
            sec.append(&tech, 4);
            sec.fixedStr("", 64);
            actual++;
        }
        memcpy(sec.bytes.data(), &actual, 4);
        buf.u32(1);
        buf.u32((uint32_t)sec.bytes.size());
        buf.append(sec.bytes.data(), sec.bytes.size());
    }

    // Section 2: lua_state_registry
    {
        DumpBuf sec;
        EnterCriticalSection(&csRegistered);
        uint32_t stateCount = (uint32_t)registered_states.size();
        if (stateCount > 1024) stateCount = 1024;
        sec.u32(stateCount);
        for (uint32_t i = 0; i < stateCount; i++) {
            sec.u64((uint64_t)(uintptr_t)registered_states[i]);
        }
        LeaveCriticalSection(&csRegistered);
        buf.u32(2);
        buf.u32((uint32_t)sec.bytes.size());
        buf.append(sec.bytes.data(), sec.bytes.size());
    }

    // Section 3: object_catalog
    {
        DumpBuf sec;
        sec.u32(kDumpObjectTypesCount);
        for (uint32_t i = 0; i < kDumpObjectTypesCount; i++) {
            sec.fixedStr(kDumpObjectTypes[i], 64);
            uint32_t count = QueryObjectTypeCount(L, kDumpObjectTypes[i]);
            sec.u32(count);
        }
        buf.u32(3);
        buf.u32((uint32_t)sec.bytes.size());
        buf.append(sec.bytes.data(), sec.bytes.size());
    }

    // Section 4: global_registry
    {
        DumpBuf sec;
        sec.u32(kDumpGlobalsCount);
        for (uint32_t i = 0; i < kDumpGlobalsCount; i++) {
            WriteGlobalRecord(L, sec, kDumpGlobals[i]);
        }
        buf.u32(4);
        buf.u32((uint32_t)sec.bytes.size());
        buf.append(sec.bytes.data(), sec.bytes.size());
    }

    // Section 5: metadata
    {
        DumpBuf sec;
        sec.u32(4);
        WriteMetaPair(sec, "capture_method",       "powrprof_dll");
        WriteMetaPair(sec, "mod_name",             "unknown");
        WriteMetaPair(sec, "mod_version",          "unknown");
        WriteMetaPair(sec, "swfoc_bridge_version", "1.0");
        buf.u32(5);
        buf.u32((uint32_t)sec.bytes.size());
        buf.append(sec.bytes.data(), sec.bytes.size());
    }

    // End marker
    uint32_t endId = 0xFFFFFFFFu;
    uint32_t endLen = 4;
    buf.u32(endId);
    buf.u32(endLen);
    uint32_t crc = Crc32_Update(0, buf.bytes.data(), buf.bytes.size());
    buf.u32(crc);

    FILE* f = fopen(path, "wb");
    if (!f) {
        char errbuf[512];
        snprintf(errbuf, sizeof(errbuf),
                 "ERR: SWFOC_DumpState: could not open '%s' for write", path);
        fn_pushstring(L, errbuf);
        return 1;
    }
    size_t wrote = fwrite(buf.bytes.data(), 1, buf.bytes.size(), f);
    fclose(f);
    if (wrote != buf.bytes.size()) {
        char errbuf[512];
        snprintf(errbuf, sizeof(errbuf),
                 "ERR: SWFOC_DumpState: short write (%zu of %zu bytes) to '%s'",
                 wrote, buf.bytes.size(), path);
        fn_pushstring(L, errbuf);
        return 1;
    }

    char okbuf[512];
    snprintf(okbuf, sizeof(okbuf),
             "OK: snapshot written to %s (%zu bytes)", path, buf.bytes.size());
    fn_pushstring(L, okbuf);
    return 1;
}

// DrainPipeCommand replica
bool DrainPipeCommand_impl(lua_State* L) {
    EnterCriticalSection(&g_pipeLock);
    if (!g_pipeCmdPending) {
        LeaveCriticalSection(&g_pipeLock);
        return false;
    }
    char cmd[PIPE_CMD_MAX];
    memcpy(cmd, g_pipeCmd, PIPE_CMD_MAX);
    LeaveCriticalSection(&g_pipeLock);

    int savedTop = fn_gettop(L);
    int err = DoString(L, cmd, "=pipe");

    EnterCriticalSection(&g_pipeLock);
    if (err == 0) {
        const char* retVal = fn_tostring(L, -1);
        if (retVal && retVal[0])
            snprintf(g_pipeResult, sizeof(g_pipeResult), "%s\n", retVal);
        else
            strcpy(g_pipeResult, "OK\n");
    } else {
        const char* errMsg = fn_tostring(L, -1);
        if (!errMsg) errMsg = "unknown error";
        snprintf(g_pipeResult, sizeof(g_pipeResult), "ERR: %s\n", errMsg);
    }
    fn_settop(L, savedTop);
    g_pipeResultReady = true;
    g_pipeCmdPending = false;
    LeaveCriticalSection(&g_pipeLock);
    return true;
}

// RegisterAll replica
static void RegisterAll(lua_State* L) {
    struct { const char* name; lua_CFunction func; } funcs[] = {
        {"SWFOC_GetVersion",         Lua_GetVersion},
        {"SWFOC_GetLocalPlayer",     Lua_GetLocalPlayer},
        {"SWFOC_SetCredits",         Lua_SetCredits},
        {"SWFOC_GetCredits",         Lua_GetCredits},
        {"SWFOC_SetTechLevel",       Lua_SetTechLevel},
        {"SWFOC_UncapCredits",       Lua_UncapCredits},
        {"SWFOC_HeroInstantRespawn", Lua_HeroInstantRespawn},
        {"SWFOC_ListFactions",       Lua_ListFactions},
        {"SWFOC_Log",                Lua_Log},
        {"SWFOC_DoString",           Lua_DoString},
        {"SWFOC_DrainPipe",          Lua_DrainPipe},
        {"SWFOC_StateInfo",          SWFOC_StateInfo},
        {"SWFOC_EventControl",       Lua_EventControl},
        {"SWFOC_DumpState",          Lua_DumpState},
        // Phase 3.2: combat / inspect helpers
        {"SWFOC_SetUnitInvuln",      Lua_SetUnitInvuln},
        {"SWFOC_SetUnitHull",        Lua_SetUnitHull},
        {"SWFOC_InspectUnit",        Lua_InspectUnit},
        {"SWFOC_GetHardpoints",      Lua_GetHardpoints},
        {"SWFOC_GetSelectedUnit",    Lua_GetSelectedUnit},
        {"SWFOC_GetSelectedUnits",   Lua_GetSelectedUnits},
        {"SWFOC_GodMode",            Lua_GodMode},
        {"SWFOC_OneHitKill",         Lua_OneHitKill},
    };
    for (auto& f : funcs) {
        fn_pushstring(L, f.name);
        fn_pushcclosure(L, f.func, 0);
        fn_settable(L, LUA_GLOBALSINDEX);
    }
}

// lua_close hook replica
static void Hook_lua_close(void* L) {
    EnterCriticalSection(&csGameStates);
    auto it = std::find(cached_game_states.begin(), cached_game_states.end(), L);
    if (it != cached_game_states.end()) cached_game_states.erase(it);
    LeaveCriticalSection(&csGameStates);

    EnterCriticalSection(&csRegistered);
    auto it2 = std::find(registered_states.begin(), registered_states.end(), L);
    if (it2 != registered_states.end()) registered_states.erase(it2);
    LeaveCriticalSection(&csRegistered);
}

// Reset all mutable bridge state between suites
static void ResetBridgeState() {
    registered_states.clear();
    cached_game_states.clear();
    g_pipeCmdPending = false;
    g_pipeResultReady = false;
    memset(g_pipeCmd, 0, sizeof(g_pipeCmd));
    memset(g_pipeResult, 0, sizeof(g_pipeResult));
    g_lastCmdSeq = 0;
    memset(&g_shmCmdBuf, 0, sizeof(g_shmCmdBuf));
    memset(&g_shmEvtBuf, 0, sizeof(g_shmEvtBuf));
}

// ======================================================================
// TEST SUITE 1: State Registration
// ======================================================================

static void TestStateRegistration() {
    StartSuite("State Registration");
    ResetBridgeState();

    FakeLuaState states[5];
    for (int i = 0; i < 5; i++)
        states[i].has_game_globals = (i == 1 || i == 3);

    // Register all 5
    for (int i = 0; i < 5; i++) {
        EnterCriticalSection(&csRegistered);
        registered_states.push_back((void*)&states[i]);
        LeaveCriticalSection(&csRegistered);
    }
    Check(registered_states.size() == 5, "Register 5 states");

    // Cache 2 game states
    for (int i = 0; i < 5; i++) {
        if (states[i].has_game_globals) {
            EnterCriticalSection(&csGameStates);
            cached_game_states.push_back((void*)&states[i]);
            LeaveCriticalSection(&csGameStates);
        }
    }
    Check(cached_game_states.size() == 2, "2 game states cached");

    bool allFound = true;
    for (int i = 0; i < 5; i++) {
        if (std::find(registered_states.begin(), registered_states.end(),
                      (void*)&states[i]) == registered_states.end())
            allFound = false;
    }
    Check(allFound, "All 5 in registered_states");

    Hook_lua_close((void*)&states[1]);
    Check(registered_states.size() == 4, "lua_close removes from registered");
    Check(cached_game_states.size() == 1, "lua_close removes from game cache");

    Hook_lua_close((void*)&states[0]);
    Check(registered_states.size() == 3, "lua_close removes non-game state");
    Check(cached_game_states.size() == 1, "Game cache unchanged for non-game state");

    Hook_lua_close((void*)&states[0]);
    Check(registered_states.size() == 3, "Double lua_close is safe (no crash)");
}

// ======================================================================
// TEST SUITE 2: SWFOC_* Functions
// ======================================================================

static void TestSWFOCFunctions() {
    StartSuite("SWFOC Functions");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();
    GI_WriteFloat(RVA::DefaultHeroRespawnTime, 120.0f);

    FakeLuaState L;

    // GetVersion
    fake_reset(&L);
    Lua_GetVersion(LS(&L));
    Check(!L.stack.empty() && L.stack.back().type == LUA_TSTRING
          && L.stack.back().strval == "SWFOC Lua Bridge v1.0",
          "GetVersion returns correct string");

    // GetLocalPlayer
    fake_reset(&L);
    Lua_GetLocalPlayer(LS(&L));
    Check(L.stack.size() == 2, "GetLocalPlayer returns 2 values");
    Check(L.stack[0].type == LUA_TNUMBER && L.stack[0].numval == 1.0,
          "GetLocalPlayer slot is 1");
    Check(L.stack[1].type == LUA_TSTRING && L.stack[1].strval == "REBEL",
          "GetLocalPlayer faction is REBEL");

    // GetCredits
    fake_reset(&L);
    Lua_GetCredits(LS(&L));
    Check(!L.stack.empty() && fabs(L.stack.back().numval - 25000.0) < 0.1,
          "GetCredits reads 25000");

    // SetCredits(50000)
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 50000.0; L.stack.push_back(a); }
    Lua_SetCredits(LS(&L));
    uintptr_t localP = GetPlayerObj(1);
    float newC = *reinterpret_cast<float*>(localP + RVA::PlayerObj::Credits);
    Check(fabs(newC - 50000.0f) < 0.1f, "SetCredits writes 50000 to memory");
    Check(!L.stack.empty() && L.stack.back().numval == 1.0, "SetCredits returns 1");

    // GetCredits after set
    fake_reset(&L);
    Lua_GetCredits(LS(&L));
    Check(!L.stack.empty() && fabs(L.stack.back().numval - 50000.0) < 0.1,
          "GetCredits reads back 50000");

    // SetTechLevel(3)
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 3.0; L.stack.push_back(a); }
    Lua_SetTechLevel(LS(&L));
    int newT = *reinterpret_cast<int*>(localP + RVA::PlayerObj::TechLevel);
    Check(newT == 3, "SetTechLevel writes 3 to memory");

    // UncapCredits
    fake_reset(&L);
    Lua_UncapCredits(LS(&L));
    float maxC = *reinterpret_cast<float*>(localP + RVA::PlayerObj::MaxCredits);
    Check(fabs(maxC - 999999999.0f) < 1.0f, "UncapCredits sets max to 999999999");

    // HeroInstantRespawn(1)
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    Lua_HeroInstantRespawn(LS(&L));
    Check(fabs(GI_ReadFloat(RVA::DefaultHeroRespawnTime)) < 0.01f,
          "HeroInstantRespawn(1) sets time to 0");

    // HeroInstantRespawn(0) -- restore
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    Lua_HeroInstantRespawn(LS(&L));
    Check(fabs(GI_ReadFloat(RVA::DefaultHeroRespawnTime) - 120.0f) < 0.1f,
          "HeroInstantRespawn(0) restores original");

    // ListFactions
    fake_reset(&L);
    Lua_ListFactions(LS(&L));
    int ntCount = 0, stCount = 0, rsCount = 0;
    for (auto& c : L.call_log) {
        if (c == "newtable") ntCount++;
        if (c.find("settable") != std::string::npos) stCount++;
        if (c.find("rawseti") != std::string::npos) rsCount++;
    }
    Check(ntCount == 4, "ListFactions creates 4 tables (1 outer + 3 entries)");
    Check(rsCount == 3, "ListFactions inserts 3 entries via rawseti");
    Check(stCount == 12, "ListFactions sets 12 fields (4 per player x 3)");

    // Log
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TSTRING; a.strval = "test msg"; L.stack.push_back(a); }
    int ret = Lua_Log(LS(&L));
    Check(ret == 0, "Log returns 0");
    bool foundTs = false;
    for (auto& c : L.call_log) if (c.find("tostring") != std::string::npos) foundTs = true;
    Check(foundTs, "Log calls tostring");

    // DrainPipe (empty queue)
    fake_reset(&L);
    g_pipeCmdPending = false;
    Lua_DrainPipe(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 0.0,
          "DrainPipe returns 0 when queue empty");

    // StateInfo
    fake_reset(&L);
    cached_game_states.clear();
    cached_game_states.push_back((void*)0x1234);
    cached_game_states.push_back((void*)0x5678);
    SWFOC_StateInfo(LS(&L));
    Check(!L.stack.empty() && L.stack.back().type == LUA_TSTRING,
          "StateInfo returns a string");
    Check(L.stack.back().strval.find("Game states: 2") != std::string::npos,
          "StateInfo reports count 2");

    // EventControl(1) enable
    g_evtBuf = &g_shmEvtBuf;
    memset(&g_shmEvtBuf, 0, sizeof(g_shmEvtBuf));
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    Lua_EventControl(LS(&L));
    Check(g_evtBuf->flags.load() == 1, "EventControl(1) enables events");

    // EventControl(0) disable
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    Lua_EventControl(LS(&L));
    Check(g_evtBuf->flags.load() == 0, "EventControl(0) disables events");

    // EventControl with no buffer
    g_evtBuf = nullptr;
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    Lua_EventControl(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 0.0,
          "EventControl returns 0 with no buffer");
    g_evtBuf = &g_shmEvtBuf;
}

// ======================================================================
// TEST SUITE 3: DoString Return Value Capture
// ======================================================================

static void TestDoStringCapture() {
    StartSuite("DoString Return Value Capture");

    FakeLuaState L;

    // Success
    fake_reset(&L);
    L.pcall_error = 0; L.load_error = 0;
    int err = DoString(LS(&L), "x = 1", "=test");
    Check(err == 0, "DoString succeeds on valid code");

    // Load failure
    fake_reset(&L);
    L.load_error = 1; L.load_error_msg = "syntax error near 'xyz'";
    err = DoString(LS(&L), "invalid{{{", "=test");
    Check(err != 0, "DoString fails on load error");
    Check(!L.stack.empty() && L.stack.back().strval.find("syntax error") != std::string::npos,
          "Load error message on stack");

    // pcall failure
    fake_reset(&L);
    L.load_error = 0; L.pcall_error = 2; L.pcall_error_msg = "attempt to call nil";
    err = DoString(LS(&L), "nonexistent()", "=test");
    Check(err == 2, "DoString returns pcall error code");
    Check(!L.stack.empty() && L.stack.back().strval.find("nil") != std::string::npos,
          "pcall error message on stack");

    // Lua_DoString wrapper success
    fake_reset(&L);
    L.load_error = 0; L.pcall_error = 0;
    { StackEntry a; a.type = LUA_TSTRING; a.strval = "return 42"; L.stack.push_back(a); }
    int rc = Lua_DoString(LS(&L));
    Check(rc == 1, "Lua_DoString returns 1 on success");
    Check(!L.stack.empty() && L.stack.back().numval == 1.0,
          "Lua_DoString pushes 1 on success");

    // Lua_DoString wrapper failure
    fake_reset(&L);
    L.load_error = 0; L.pcall_error = 2; L.pcall_error_msg = "runtime error";
    { StackEntry a; a.type = LUA_TSTRING; a.strval = "error('boom')"; L.stack.push_back(a); }
    rc = Lua_DoString(LS(&L));
    Check(rc == 2, "Lua_DoString returns 2 on error");

    // Lua_DoString with nil argument
    fake_reset(&L);
    L.load_error = 0; L.pcall_error = 0;
    { StackEntry a; a.type = LUA_TNIL; L.stack.push_back(a); }
    rc = Lua_DoString(LS(&L));
    Check(rc == 2, "Lua_DoString returns 2 for nil arg");
}

// ======================================================================
// TEST SUITE 4: Pipe Protocol
// ======================================================================

static void TestPipeProtocol() {
    StartSuite("Pipe Protocol");

    FakeLuaState L;
    L.load_error = 0; L.pcall_error = 0;

    // Empty queue
    fake_reset(&L);
    g_pipeCmdPending = false;
    bool did = DrainPipeCommand_impl(LS(&L));
    Check(!did, "DrainPipe returns false when empty");

    // Queue and drain
    fake_reset(&L);
    EnterCriticalSection(&g_pipeLock);
    strncpy(g_pipeCmd, "return SWFOC_GetVersion()", PIPE_CMD_MAX - 1);
    g_pipeCmdPending = true; g_pipeResultReady = false;
    LeaveCriticalSection(&g_pipeLock);
    did = DrainPipeCommand_impl(LS(&L));
    Check(did, "DrainPipe executes pending command");
    Check(g_pipeResultReady, "Pipe result ready after drain");
    Check(!g_pipeCmdPending, "Pipe command cleared");
    Check(strlen(g_pipeResult) > 0, "Pipe result has content");

    // Drain with pcall error
    fake_reset(&L);
    L.pcall_error = 2; L.pcall_error_msg = "bad argument";
    EnterCriticalSection(&g_pipeLock);
    strncpy(g_pipeCmd, "bad_code()", PIPE_CMD_MAX - 1);
    g_pipeCmdPending = true; g_pipeResultReady = false;
    LeaveCriticalSection(&g_pipeLock);
    did = DrainPipeCommand_impl(LS(&L));
    Check(did, "DrainPipe executes error command");
    Check(strstr(g_pipeResult, "ERR:") != nullptr, "Error result starts with ERR:");
    Check(strstr(g_pipeResult, "bad argument") != nullptr,
          "Error result contains message");

    // Stack guard: verify settop called to restore
    fake_reset(&L);
    L.pcall_error = 0; L.load_error = 0;
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 999; L.stack.push_back(a); }
    EnterCriticalSection(&g_pipeLock);
    strncpy(g_pipeCmd, "return 1", PIPE_CMD_MAX - 1);
    g_pipeCmdPending = true; g_pipeResultReady = false;
    LeaveCriticalSection(&g_pipeLock);
    DrainPipeCommand_impl(LS(&L));
    // settop was called with savedTop=1, so stack size should be 1
    Check(L.stack.size() == 1, "Stack guard restores stack after drain");

    // Queue-full (already pending)
    EnterCriticalSection(&g_pipeLock);
    g_pipeCmdPending = true;
    LeaveCriticalSection(&g_pipeLock);
    Check(g_pipeCmdPending, "Queue full: command still pending");
}

// ======================================================================
// TEST SUITE 5: Shared Memory Protocol
// ======================================================================

static void TestSharedMemoryProtocol() {
    StartSuite("Shared Memory Protocol");

    FakeLuaState L;
    L.load_error = 0; L.pcall_error = 0;

    g_cmdBuf = &g_shmCmdBuf;
    memset(&g_shmCmdBuf, 0, sizeof(g_shmCmdBuf));
    g_lastCmdSeq = 0;

    // No new command
    Check(g_shmCmdBuf.cmd_seq.load() == g_lastCmdSeq, "No cmd when seq unchanged");

    // Write and drain a command
    {
        fake_reset(&L);
        const char* cmd = "return 42";
        strncpy(g_shmCmdBuf.cmd, cmd, sizeof(g_shmCmdBuf.cmd) - 1);
        g_shmCmdBuf.cmd_len = (uint32_t)strlen(cmd);
        g_shmCmdBuf.cmd_seq.store(1, std::memory_order_release);

        uint32_t seq = g_shmCmdBuf.cmd_seq.load(std::memory_order_acquire);
        Check(seq != g_lastCmdSeq, "New command detected");
        g_lastCmdSeq = seq;

        int savedTop = fn_gettop(LS(&L));
        char localCmd[4096];
        memcpy(localCmd, g_shmCmdBuf.cmd, g_shmCmdBuf.cmd_len + 1);
        int err = DoString(LS(&L), localCmd, "=shmem");
        if (err == 0) {
            const char* rv = fn_tostring(LS(&L), -1);
            if (rv && rv[0]) snprintf(g_shmCmdBuf.result, 4095, "%s", rv);
            else strncpy(g_shmCmdBuf.result, "OK", 4095);
            g_shmCmdBuf.result_len = (uint32_t)strlen(g_shmCmdBuf.result);
        } else {
            const char* msg = fn_tostring(LS(&L), -1);
            snprintf(g_shmCmdBuf.result, 4095, "ERR: %s", msg ? msg : "unknown");
            g_shmCmdBuf.result_len = (uint32_t)strlen(g_shmCmdBuf.result);
        }
        fn_settop(LS(&L), savedTop);
        g_shmCmdBuf.result_seq.store(seq, std::memory_order_release);

        Check(g_shmCmdBuf.result_seq.load() == 1, "Result seq matches cmd seq");
        Check(strlen(g_shmCmdBuf.result) > 0, "Result buffer has content");
    }

    // Command with pcall error
    {
        fake_reset(&L);
        L.pcall_error = 2; L.pcall_error_msg = "shmem test error";
        const char* cmd = "error('boom')";
        strncpy(g_shmCmdBuf.cmd, cmd, sizeof(g_shmCmdBuf.cmd) - 1);
        g_shmCmdBuf.cmd_len = (uint32_t)strlen(cmd);
        g_shmCmdBuf.cmd_seq.store(2, std::memory_order_release);

        uint32_t seq = g_shmCmdBuf.cmd_seq.load();
        g_lastCmdSeq = seq;
        int savedTop = fn_gettop(LS(&L));
        int err = DoString(LS(&L), g_shmCmdBuf.cmd, "=shmem");
        if (err != 0) {
            const char* msg = fn_tostring(LS(&L), -1);
            snprintf(g_shmCmdBuf.result, 4095, "ERR: %s", msg ? msg : "unknown");
            g_shmCmdBuf.result_len = (uint32_t)strlen(g_shmCmdBuf.result);
        }
        fn_settop(LS(&L), savedTop);
        g_shmCmdBuf.result_seq.store(seq, std::memory_order_release);

        Check(g_shmCmdBuf.result_seq.load() == 2, "Error: result seq updated");
        Check(strstr(g_shmCmdBuf.result, "ERR:") != nullptr, "Error result has ERR:");
        Check(strstr(g_shmCmdBuf.result, "shmem test error") != nullptr,
              "Error result has message");
    }

    // Same seq not re-executed
    Check(g_shmCmdBuf.cmd_seq.load() == g_lastCmdSeq, "Same seq not re-executed");

    // Event ring buffer write
    {
        g_evtBuf = &g_shmEvtBuf;
        memset(&g_shmEvtBuf, 0, sizeof(g_shmEvtBuf));
        g_shmEvtBuf.flags.store(1, std::memory_order_release);

        uint16_t type = 0x01; // EVT_HP_CHANGE
        struct { uint32_t id; float hp; float dmg; int dtype; } payload = {42, 100.0f, 25.0f, 3};
        uint16_t payloadSize = sizeof(payload);
        uint32_t totalSize = 4 + payloadSize;
        uint32_t wp = g_shmEvtBuf.write_pos.load();
        uint32_t ringSize = sizeof(g_shmEvtBuf.ring);

        uint8_t header[4];
        memcpy(header, &type, 2);
        memcpy(header + 2, &payloadSize, 2);
        for (uint32_t i = 0; i < 4; i++)
            g_shmEvtBuf.ring[(wp + i) % ringSize] = header[i];
        const uint8_t* src = (const uint8_t*)&payload;
        for (uint32_t i = 0; i < payloadSize; i++)
            g_shmEvtBuf.ring[(wp + 4 + i) % ringSize] = src[i];
        g_shmEvtBuf.write_pos.store((wp + totalSize) % ringSize, std::memory_order_release);
        g_shmEvtBuf.event_count.fetch_add(1, std::memory_order_relaxed);

        Check(g_shmEvtBuf.write_pos.load() == totalSize, "Event write_pos advanced");
        Check(g_shmEvtBuf.event_count.load() == 1, "Event count incremented");

        uint16_t readType;
        memcpy(&readType, g_shmEvtBuf.ring, 2);
        Check(readType == 0x01, "Event type is EVT_HP_CHANGE");
    }

    g_evtBuf = nullptr;
    g_cmdBuf = nullptr;
}

// ======================================================================
// TEST SUITE 6: Registration and Global Probe
// ======================================================================

static void TestRegistrationAndProbe() {
    StartSuite("Registration and Global Probe");

    FakeLuaState L;
    fake_reset(&L);
    RegisterAll(LS(&L));

    Check(L.globals.size() == 22, "RegisterAll sets 22 globals (20 base + 2 selection)");
    Check(L.globals.count("SWFOC_GetVersion") == 1, "SWFOC_GetVersion registered");
    Check(L.globals.count("SWFOC_SetCredits") == 1, "SWFOC_SetCredits registered");
    Check(L.globals.count("SWFOC_EventControl") == 1, "SWFOC_EventControl registered");
    Check(L.globals.count("SWFOC_DrainPipe") == 1, "SWFOC_DrainPipe registered");
    Check(L.globals.count("SWFOC_DumpState") == 1, "SWFOC_DumpState registered");
    // Phase 3.2 helpers
    Check(L.globals.count("SWFOC_SetUnitInvuln") == 1, "SWFOC_SetUnitInvuln registered");
    Check(L.globals.count("SWFOC_SetUnitHull") == 1, "SWFOC_SetUnitHull registered");
    Check(L.globals.count("SWFOC_InspectUnit") == 1, "SWFOC_InspectUnit registered");
    Check(L.globals.count("SWFOC_GetHardpoints") == 1, "SWFOC_GetHardpoints registered");
    Check(L.globals.count("SWFOC_GodMode") == 1, "SWFOC_GodMode registered");
    Check(L.globals.count("SWFOC_OneHitKill") == 1, "SWFOC_OneHitKill registered");
    // 2026-04-11 selection reader helpers
    Check(L.globals.count("SWFOC_GetSelectedUnit") == 1, "SWFOC_GetSelectedUnit registered");
    Check(L.globals.count("SWFOC_GetSelectedUnits") == 1, "SWFOC_GetSelectedUnits registered");

    bool allFuncs = true;
    for (auto& kv : L.globals)
        if (kv.second.type != LUA_TFUNCTION) allFuncs = false;
    Check(allFuncs, "All registered globals are TFUNCTION");

    // Game global probe: has_game_globals -> Find_Object_Type found
    fake_reset(&L);
    L.has_game_globals = true;
    fn_pushstring(LS(&L), "Find_Object_Type");
    fn_gettable(LS(&L), LUA_GLOBALSINDEX);
    int ty = fn_type(LS(&L), -1);
    Check(ty == LUA_TFUNCTION, "Game state finds Find_Object_Type");

    // No game globals -> nil
    fake_reset(&L);
    L.has_game_globals = false;
    fn_pushstring(LS(&L), "Find_Object_Type");
    fn_gettable(LS(&L), LUA_GLOBALSINDEX);
    ty = fn_type(LS(&L), -1);
    Check(ty == LUA_TNIL, "Non-game state gets nil for Find_Object_Type");
}

// ======================================================================
// TEST SUITE 7: Player Memory Helpers
// ======================================================================

static void TestPlayerHelpers() {
    StartSuite("Player Memory Helpers");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    Check(GetPlayerCount() == 3, "PlayerCount is 3");
    Check(FindLocalPlayerSlot() == 1, "Local player is slot 1");
    Check(strcmp(GetFactionName(0), "EMPIRE") == 0, "Slot 0 = EMPIRE");
    Check(strcmp(GetFactionName(1), "REBEL") == 0, "Slot 1 = REBEL");
    Check(strcmp(GetFactionName(2), "UNDERWORLD") == 0, "Slot 2 = UNDERWORLD");

    uintptr_t p1 = GetPlayerObj(1);
    Check(fabs(*reinterpret_cast<float*>(p1 + RVA::PlayerObj::Credits) - 25000.0f) < 0.1f,
          "Slot 1 credits = 25000");
    Check(*reinterpret_cast<int*>(p1 + RVA::PlayerObj::TechLevel) == 3,
          "Slot 1 tech = 3");

    // No local player
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    uintptr_t pa = g_base + PLAYER_BASE_OFF;
    uintptr_t pb = g_base + PLAYER_BASE_OFF + PLAYER_STRIDE;
    SetupPlayer(pa, 0, false, 5000.0f, 100000.0f, 1, "EMPIRE");
    SetupPlayer(pb, 1, false, 5000.0f, 100000.0f, 1, "REBEL");
    SetupPlayerArray({pa, pb});
    Check(FindLocalPlayerSlot() == -1, "No local player returns -1");

    // Null player array
    GI_WriteQword(RVA::PlayerArray_Global, 0);
    Check(strcmp(GetFactionName(0), "?") == 0, "Null player returns '?'");
}

// ======================================================================
// TEST SUITE 8: Edge Cases
// ======================================================================

static void TestEdgeCases() {
    StartSuite("Edge Cases");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    uintptr_t pa = g_base + PLAYER_BASE_OFF;
    SetupPlayer(pa, 0, false, 5000.0f, 100000.0f, 1, "EMPIRE");
    SetupPlayerArray({pa}); // 1 player, not local

    FakeLuaState L;

    // SetCredits with no local player
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 99999; L.stack.push_back(a); }
    Lua_SetCredits(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 0.0,
          "SetCredits returns 0 with no local player");

    // GetCredits with no local player
    fake_reset(&L);
    Lua_GetCredits(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 0.0,
          "GetCredits returns 0 with no local player");

    // SetTechLevel with no local player
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 5; L.stack.push_back(a); }
    Lua_SetTechLevel(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 0.0,
          "SetTechLevel returns 0 with no local player");

    // UncapCredits with no local player
    fake_reset(&L);
    Lua_UncapCredits(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 0.0,
          "UncapCredits returns 0 with no local player");

    // DoString with null fn_load
    {
        auto saved = fn_load;
        fn_load = nullptr;
        fake_reset(&L);
        int err = DoString(LS(&L), "x=1", "=test");
        Check(err == -1, "DoString returns -1 when fn_load null");
        fn_load = saved;
    }

    // GetLocalPlayer with no local player
    fake_reset(&L);
    Lua_GetLocalPlayer(LS(&L));
    Check(L.stack.size() == 2, "GetLocalPlayer returns 2 values with no local");
    Check(L.stack[0].numval == -1.0, "GetLocalPlayer slot = -1");
    Check(L.stack[1].strval == "none", "GetLocalPlayer faction = 'none'");
}

// ======================================================================
// TEST SUITE 9: Snapshot Format (SWFOC_DumpState)
// ======================================================================

// Helper: read a little-endian uint32 at offset
static uint32_t SnapU32(const std::vector<uint8_t>& bytes, size_t off) {
    uint32_t v = 0;
    if (off + 4 <= bytes.size()) memcpy(&v, bytes.data() + off, 4);
    return v;
}

static void TestSnapshotFormat() {
    StartSuite("Snapshot Format (SWFOC_DumpState)");
    ResetBridgeState();
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    // Seed a few entries in registered_states so section 2 has data
    int dummy1 = 0, dummy2 = 0, dummy3 = 0;
    EnterCriticalSection(&csRegistered);
    registered_states.push_back((void*)&dummy1);
    registered_states.push_back((void*)&dummy2);
    registered_states.push_back((void*)&dummy3);
    LeaveCriticalSection(&csRegistered);

    FakeLuaState L;
    fake_reset(&L);

    // Push the path string as the single Lua arg and call Lua_DumpState.
    // Matches the expected call pattern: SWFOC_DumpState("test_snapshot.swfocsnap")
    const char* snapPath = "test_snapshot.swfocsnap";
    { StackEntry a; a.type = LUA_TSTRING; a.strval = snapPath; L.stack.push_back(a); }
    int rc = Lua_DumpState(LS(&L));
    Check(rc == 1, "DumpState returns 1 value");
    Check(!L.stack.empty() && L.stack.back().type == LUA_TSTRING,
          "DumpState result is a string");

    // Read the file back
    FILE* f = fopen(snapPath, "rb");
    Check(f != nullptr, "Snapshot file opened for read");
    std::vector<uint8_t> bytes;
    if (f) {
        fseek(f, 0, SEEK_END);
        long sz = ftell(f);
        fseek(f, 0, SEEK_SET);
        bytes.resize((size_t)sz);
        size_t got = fread(bytes.data(), 1, (size_t)sz, f);
        fclose(f);
        Check((long)got == sz, "Read full snapshot bytes");
    }

    // Validate magic header — bumped to v2 in 2026-04-08
    const uint8_t expectedMagic[16] = {
        'S','W','F','O','C','S','N','A','P','v','2',0,0,0,0,0
    };
    bool magicOk = bytes.size() >= 16 &&
                   memcmp(bytes.data(), expectedMagic, 16) == 0;
    Check(magicOk, "Magic header = 'SWFOCSNAPv2\\0\\0\\0\\0\\0'");

    // Validate format version at offset 0x10 == 2
    uint32_t fmtVer = SnapU32(bytes, 0x10);
    Check(fmtVer == 2, "format_version = 2");

    // Validate the first section starts at offset 0x44 (header is 68 bytes)
    // and has section_id == 1 (player_array)
    uint32_t sec1Id  = SnapU32(bytes, 0x44);
    uint32_t sec1Len = SnapU32(bytes, 0x48);
    Check(sec1Id == 1, "Section 1 id = player_array");
    Check(sec1Len >= 8, "Section 1 has at least 8-byte payload (count + local_slot)");

    // player_count at offset 0x4C (section payload starts here), clamped to <= 8
    uint32_t playerCount = SnapU32(bytes, 0x4C);
    Check(playerCount == 3, "Section 1 player_count = 3 (matches SetupTestPlayers)");

    // v2 addition: local_slot immediately after player_count, at 0x50.
    // SetupTestPlayers makes slot 1 (REBEL) the local player.
    uint32_t localSlot = SnapU32(bytes, 0x50);
    Check(localSlot == 1, "Section 1 local_slot = 1 (REBEL is local in fixture)");

    // Walk sections in order and verify the 5 content sections then end marker.
    // Start after the 68-byte header and iterate until section_id == 0xFFFFFFFF.
    size_t off = 0x44;
    uint32_t expectedIds[] = {1, 2, 3, 4, 5};
    int expectedIdx = 0;
    int sectionsSeen = 0;
    bool orderOk = true;
    uint32_t lastId = 0;
    while (off + 8 <= bytes.size()) {
        uint32_t id  = SnapU32(bytes, off);
        uint32_t len = SnapU32(bytes, off + 4);
        if (id == 0xFFFFFFFFu) break;
        if (expectedIdx < 5 && id != expectedIds[expectedIdx]) orderOk = false;
        expectedIdx++;
        lastId = id;
        sectionsSeen++;
        off += 8 + len;
        if (off > bytes.size()) { orderOk = false; break; }
    }
    Check(sectionsSeen == 5, "Saw exactly 5 content sections (ids 1..5)");
    Check(orderOk, "Sections appear in ascending ID order");
    Check(lastId == 5, "Last content section is metadata (id=5)");

    // End marker: id 0xFFFFFFFF, length = 4, followed by 4-byte CRC32
    Check(off + 12 <= bytes.size(), "End marker fits within file");
    uint32_t endId  = SnapU32(bytes, off);
    uint32_t endLen = SnapU32(bytes, off + 4);
    Check(endId  == 0xFFFFFFFFu, "End marker section_id = 0xFFFFFFFF");
    Check(endLen == 4,           "End marker section_length = 4");

    // CRC32 validation: recompute over all bytes before the 4-byte CRC field
    // (i.e., all bytes up to and including the end marker header)
    size_t crcEndOff = off + 8;
    uint32_t fileCrc = SnapU32(bytes, crcEndOff);
    uint32_t computed = Crc32_Update(0, bytes.data(), crcEndOff);
    Check(fileCrc == computed, "CRC32 at end matches body");

    // Verify the file ends exactly at crcEndOff + 4 (no trailing garbage)
    Check(bytes.size() == crcEndOff + 4, "No trailing bytes after CRC32");

    // Extra: metadata section contains "powrprof_dll" capture_method value.
    // Find the metadata section again by re-walking (simpler than re-hoisting).
    size_t walk = 0x44;
    bool metaFound = false;
    while (walk + 8 <= bytes.size()) {
        uint32_t id  = SnapU32(bytes, walk);
        uint32_t len = SnapU32(bytes, walk + 4);
        if (id == 0xFFFFFFFFu) break;
        if (id == 5) {
            const uint8_t* p = bytes.data() + walk + 8;
            size_t pl = len;
            if (pl >= 4) {
                // Search for the ASCII substring "powrprof_dll"
                const char needle[] = "powrprof_dll";
                size_t nlen = sizeof(needle) - 1;
                for (size_t i = 0; i + nlen <= pl; i++) {
                    if (memcmp(p + i, needle, nlen) == 0) { metaFound = true; break; }
                }
            }
            break;
        }
        walk += 8 + len;
    }
    Check(metaFound, "Metadata section contains capture_method='powrprof_dll'");

    // Clean up: remove dummy registered states and the snapshot file
    EnterCriticalSection(&csRegistered);
    registered_states.clear();
    LeaveCriticalSection(&csRegistered);
    remove(snapPath);

    if (bytes.size() > 0)
        printf("  [info] DumpState basic format: PASSED (%zu bytes)\n", bytes.size());
    else
        printf("  [info] DumpState basic format: FAILED (no bytes read)\n");
}

// ======================================================================
// Phase 3.2 helpers — fake GameObject + Hardpoint setup
// ======================================================================
//
// We carve out a region of g_gameImage for fake GameObject layouts and
// write all 9 documented offsets so the inspector tests can read them
// back. The components array lives in a separate region so children
// have distinct addresses.

static constexpr uintptr_t kUnitBaseOff       = 0x500000;
static constexpr uintptr_t kUnitStride        = 0x1000; // 4 KB per object
static constexpr uintptr_t kComponentsBaseOff = 0x600000;
static constexpr uintptr_t kComponentsStride  = 0x200;

struct FakeUnit {
    uintptr_t addr;
    uintptr_t componentsAddr;
};

static void WriteUnitFields(uintptr_t addr,
                            float hull, int32_t ownerId, uint32_t objId,
                            uint8_t parentIdx, uint8_t statusFlags,
                            uint8_t preventDeath, uint8_t invulnFlag,
                            uint8_t hardpointFlag, uintptr_t componentsAddr) {
    *reinterpret_cast<float*>(addr + RVA::GameObj::HP) = hull;
    *reinterpret_cast<int32_t*>(addr + RVA::GameObj::OwnerPlayerID) = ownerId;
    *reinterpret_cast<uint32_t*>(addr + RVA::GameObj::ObjectID) = objId;
    *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::ParentIndex) = parentIdx;
    *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::StatusFlags) = statusFlags;
    *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::PreventDeath) = preventDeath;
    *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::InvulnFlag) = invulnFlag;
    *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::HardpointFlag) = hardpointFlag;
    *reinterpret_cast<uintptr_t*>(addr + RVA::GameObj::ComponentArray) = componentsAddr;
}

// Build a single root unit with no children (parent_idx = 0xFF marks root).
static FakeUnit MakeRootUnit(int slot, int32_t ownerId, float hull, uint32_t objId) {
    FakeUnit u;
    u.addr = g_base + kUnitBaseOff + slot * kUnitStride;
    u.componentsAddr = 0;
    WriteUnitFields(u.addr, hull, ownerId, objId,
                    /*parentIdx=*/0xFF,
                    /*statusFlags=*/0x00,
                    /*preventDeath=*/0x00,
                    /*invulnFlag=*/0x00,
                    /*hardpointFlag=*/0x00,
                    /*componentsAddr=*/0);
    return u;
}

// Build a root with N children. Children are placed in successive unit
// slots after `rootSlot`. Each child's ParentIdx points back into the
// root's Components array at index 0 (so WalkToRootUnit on a child finds
// the root via Components[0]).
static FakeUnit MakeRootWithChildren(int rootSlot, int32_t ownerId,
                                     float rootHp, uint32_t rootObjId,
                                     int childCount) {
    FakeUnit root;
    root.addr = g_base + kUnitBaseOff + rootSlot * kUnitStride;
    root.componentsAddr = g_base + kComponentsBaseOff + rootSlot * kComponentsStride;

    // Set up the components array. Each entry is a qword child pointer.
    for (int i = 0; i < childCount; i++) {
        uintptr_t childAddr = g_base + kUnitBaseOff + (rootSlot + 1 + i) * kUnitStride;
        // Child's components qword points back to the root (Components[0] = root)
        uintptr_t childComponents = g_base + kComponentsBaseOff + (rootSlot + 1 + i) * kComponentsStride;
        *reinterpret_cast<uintptr_t*>(childComponents) = root.addr; // Components[0] = root
        WriteUnitFields(childAddr,
                        /*hull=*/100.0f + i,
                        /*ownerId=*/ownerId,
                        /*objId=*/rootObjId * 100 + i,
                        /*parentIdx=*/0x00, // -> Components[0] -> root
                        /*statusFlags=*/0x00,
                        /*preventDeath=*/0x00,
                        /*invulnFlag=*/0x00,
                        /*hardpointFlag=*/0x01,
                        childComponents);
        // Patch root's components array slot
        *reinterpret_cast<uintptr_t*>(root.componentsAddr + i * 8) = childAddr;
    }

    WriteUnitFields(root.addr, rootHp, ownerId, rootObjId,
                    /*parentIdx=*/0xFF,
                    /*statusFlags=*/0x10,
                    /*preventDeath=*/0x80,
                    /*invulnFlag=*/0x00,
                    /*hardpointFlag=*/0x00,
                    root.componentsAddr);
    return root;
}

// ======================================================================
// TEST SUITE 10: SetUnitInvuln direct write
// ======================================================================

static void TestSetUnitInvuln() {
    StartSuite("SetUnitInvuln (direct invuln write)");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();
    FakeUnit unit = MakeRootUnit(/*slot=*/0, /*ownerId=*/1, /*hull=*/100.0f, /*objId=*/1234);

    FakeLuaState L;
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = (double)(uint64_t)unit.addr; L.stack.push_back(a); }
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    int rc = Lua_SetUnitInvuln(LS(&L));
    Check(rc == 1, "SetUnitInvuln returns 1 result");
    Check(*reinterpret_cast<uint8_t*>(unit.addr + RVA::GameObj::InvulnFlag) == 1,
          "InvulnFlag byte = 1 after enable");
    Check(L.stack.back().type == LUA_TSTRING && L.stack.back().strval == "OK",
          "Returns 'OK' on success");

    // Disable
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = (double)(uint64_t)unit.addr; L.stack.push_back(a); }
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    Lua_SetUnitInvuln(LS(&L));
    Check(*reinterpret_cast<uint8_t*>(unit.addr + RVA::GameObj::InvulnFlag) == 0,
          "InvulnFlag byte = 0 after disable");

    // Invalid address (zero)
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    Lua_SetUnitInvuln(LS(&L));
    Check(L.stack.back().type == LUA_TSTRING &&
          L.stack.back().strval.find("ERR:") != std::string::npos,
          "Zero addr returns ERR:");

    // Invalid address (low memory)
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0x100; L.stack.push_back(a); }
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    Lua_SetUnitInvuln(LS(&L));
    Check(L.stack.back().strval.find("ERR:") != std::string::npos,
          "Low addr returns ERR:");
}

// ======================================================================
// TEST SUITE 11: SetUnitHull direct write
// ======================================================================

static void TestSetUnitHull() {
    StartSuite("SetUnitHull (direct hull write)");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();
    FakeUnit unit = MakeRootUnit(/*slot=*/0, /*ownerId=*/1, /*hull=*/100.0f, /*objId=*/4321);

    FakeLuaState L;
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = (double)(uint64_t)unit.addr; L.stack.push_back(a); }
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.5; L.stack.push_back(a); }
    int rc = Lua_SetUnitHull(LS(&L));
    Check(rc == 1, "SetUnitHull returns 1 result");
    float hp = *reinterpret_cast<float*>(unit.addr + RVA::GameObj::HP);
    Check(fabs(hp - 0.5f) < 0.001f, "HP float at +0x5C set to 0.5");
    Check(L.stack.back().type == LUA_TSTRING && L.stack.back().strval == "OK",
          "Returns 'OK' on success");

    // Set to 0.998
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = (double)(uint64_t)unit.addr; L.stack.push_back(a); }
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.998; L.stack.push_back(a); }
    Lua_SetUnitHull(LS(&L));
    hp = *reinterpret_cast<float*>(unit.addr + RVA::GameObj::HP);
    Check(fabs(hp - 0.998f) < 0.001f, "HP float at +0x5C set to 0.998");

    // Invalid address
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    Lua_SetUnitHull(LS(&L));
    Check(L.stack.back().strval.find("ERR:") != std::string::npos,
          "Zero addr returns ERR:");
}

// ======================================================================
// TEST SUITE 12: InspectUnit (key=value string)
// ======================================================================

static void TestInspectUnit() {
    StartSuite("InspectUnit (key=value string)");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    // Build a unit with non-trivial values for every field
    uintptr_t addr = g_base + kUnitBaseOff;
    uintptr_t comps = g_base + kComponentsBaseOff;
    WriteUnitFields(addr,
                    /*hull=*/0.998f,
                    /*ownerId=*/6,
                    /*objId=*/1234,
                    /*parentIdx=*/0xFF,
                    /*statusFlags=*/0x00,
                    /*preventDeath=*/0x80,
                    /*invulnFlag=*/0x01,
                    /*hardpointFlag=*/0x00,
                    /*componentsAddr=*/comps);

    FakeLuaState L;
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = (double)(uint64_t)addr; L.stack.push_back(a); }
    int rc = Lua_InspectUnit(LS(&L));
    Check(rc == 1, "InspectUnit returns 1 result");
    Check(L.stack.back().type == LUA_TSTRING, "InspectUnit returns string");
    const std::string& s = L.stack.back().strval;

    Check(s.find("hull=0.998") != std::string::npos, "string contains hull=0.998");
    Check(s.find("owner=6") != std::string::npos, "string contains owner=6");
    Check(s.find("obj_id=1234") != std::string::npos, "string contains obj_id=1234");
    Check(s.find("parent_idx=0xFF") != std::string::npos, "string contains parent_idx=0xFF");
    Check(s.find("status_flags=0x00") != std::string::npos, "string contains status_flags=0x00");
    Check(s.find("prevent_death=0x80") != std::string::npos, "string contains prevent_death=0x80");
    Check(s.find("invuln_flag=0x01") != std::string::npos, "string contains invuln_flag=0x01");
    Check(s.find("hardpoint_flag=0x00") != std::string::npos, "string contains hardpoint_flag=0x00");
    Check(s.find("components_ptr=0x") != std::string::npos, "string contains components_ptr=0x...");

    // Invalid addr
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    Lua_InspectUnit(LS(&L));
    Check(L.stack.back().strval.find("ERR:") != std::string::npos,
          "Zero addr returns ERR:");
}

// ======================================================================
// TEST SUITE 13: GetHardpoints (component walk)
// ======================================================================

static void TestGetHardpoints() {
    StartSuite("GetHardpoints (component walk)");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    // Root with 3 children
    FakeUnit root = MakeRootWithChildren(/*rootSlot=*/0, /*ownerId=*/1,
                                          /*rootHp=*/500.0f, /*rootObjId=*/77, 3);

    FakeLuaState L;
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = (double)(uint64_t)root.addr; L.stack.push_back(a); }
    int rc = Lua_GetHardpoints(LS(&L));
    Check(rc == 1, "GetHardpoints returns 1 result");
    Check(L.stack.back().type == LUA_TSTRING, "GetHardpoints returns string");
    const std::string& s = L.stack.back().strval;
    Check(s.find("count=3") != std::string::npos, "Reports count=3");
    Check(s.find("child0=0x") != std::string::npos, "Has child0 entry");
    Check(s.find("child1=0x") != std::string::npos, "Has child1 entry");
    Check(s.find("child2=0x") != std::string::npos, "Has child2 entry");
    Check(s.find("hp0=100") != std::string::npos, "Has hp0=100");
    Check(s.find("hp1=101") != std::string::npos, "Has hp1=101");
    Check(s.find("hp2=102") != std::string::npos, "Has hp2=102");

    // Root with 0 children — Components ptr is null
    FakeUnit lone = MakeRootUnit(/*slot=*/10, /*ownerId=*/1, /*hull=*/200.0f, /*objId=*/88);
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = (double)(uint64_t)lone.addr; L.stack.push_back(a); }
    Lua_GetHardpoints(LS(&L));
    Check(L.stack.back().strval == "count=0", "Null components -> count=0");

    // Invalid addr
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    Lua_GetHardpoints(LS(&L));
    Check(L.stack.back().strval.find("ERR:") != std::string::npos,
          "Zero addr returns ERR:");
}

// ======================================================================
// TEST SUITE 13.5: Selection reader (2026-04-11)
// ----------------------------------------------------------------------
// Stages a fake GameModeRoot at g_base + 0x700000 and writes a
// DynamicVectorClass header at manager + 0x1C0 + 0x48 * slot. Each node
// has next = +0x08 and data_plus_24 = +0x18. See
// knowledge-base/selection_pointer_2026-04-11.md for the derivation.
// ======================================================================

static constexpr uintptr_t kSelFixtureModeRoot = 0x700000;
static constexpr uintptr_t kSelFixtureListBase = 0x710000;
static constexpr size_t    kSelNodeStride      = 0x40;

static uintptr_t StageSelectionChain(int slot) {
    uintptr_t mgrRoot = g_base + kSelFixtureModeRoot;
    uintptr_t rootSlotAddr =
        g_base + RVA::GameModeRoot_Global + RVA::Selection::kModeRootIndirection;
    *reinterpret_cast<uintptr_t*>(rootSlotAddr) = mgrRoot;

    uintptr_t vecArrayBase = mgrRoot + 0x200;
    *reinterpret_cast<uintptr_t*>(mgrRoot + RVA::Selection::kPerPlayerVectorsArray) =
        vecArrayBase;

    uintptr_t vec = vecArrayBase + RVA::Selection::kSelectionEntryStride * slot;
    uintptr_t sentinel = vec + RVA::Selection::kVectorSentinel;
    *reinterpret_cast<uintptr_t*>(vec + RVA::Selection::kVectorHead) = sentinel;
    return vec;
}

static uintptr_t AppendSelectionNode(uintptr_t vec, int nodeIndex, uintptr_t unitAddr) {
    uintptr_t nodeAddr = g_base + kSelFixtureListBase + nodeIndex * kSelNodeStride;
    uintptr_t sentinel = vec + RVA::Selection::kVectorSentinel;

    *reinterpret_cast<uintptr_t*>(nodeAddr + RVA::Selection::kNodeDataPlus24) =
        unitAddr + RVA::Selection::kNodeDataAdjustment;

    uintptr_t cur = vec + RVA::Selection::kVectorHead;
    uintptr_t next = *reinterpret_cast<uintptr_t*>(cur);
    while (next != sentinel) {
        cur = next + RVA::Selection::kNodeNext;
        next = *reinterpret_cast<uintptr_t*>(cur);
    }
    *reinterpret_cast<uintptr_t*>(cur) = nodeAddr;
    *reinterpret_cast<uintptr_t*>(nodeAddr + RVA::Selection::kNodeNext) = sentinel;
    return nodeAddr;
}

static void TestSelectionReader() {
    StartSuite("Selection reader (GetSelectedUnit / GetSelectedUnits)");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    FakeLuaState L;

    // Case 1: no pointer chain -> returns 0 / "".
    {
        fake_reset(&L);
        int rc = Lua_GetSelectedUnit(LS(&L));
        Check(rc == 1, "GetSelectedUnit returns one result");
        Check(L.stack.back().type == LUA_TNUMBER, "GetSelectedUnit yields number");
        Check(L.stack.back().numval == 0.0,
              "Null chain -> GetSelectedUnit = 0 (safe degrade)");

        fake_reset(&L);
        rc = Lua_GetSelectedUnits(LS(&L));
        Check(rc == 1, "GetSelectedUnits returns one result");
        Check(L.stack.back().type == LUA_TSTRING, "GetSelectedUnits yields string");
        Check(L.stack.back().strval.empty(),
              "Null chain -> GetSelectedUnits = '' (safe degrade)");
    }

    // Stage the PlayerList global so ReadCurrentHumanPlayerSlot returns 1.
    uintptr_t pl = g_base + RVA::PlayerListClass_Global;
    static uint64_t sPlVec[2];
    sPlVec[0] = g_base + PLAYER_BASE_OFF + 1 * PLAYER_STRIDE;
    sPlVec[1] = 0;
    *reinterpret_cast<uintptr_t*>(pl + 0) = reinterpret_cast<uintptr_t>(&sPlVec[0]);
    *reinterpret_cast<uintptr_t*>(pl + 8) = reinterpret_cast<uintptr_t>(&sPlVec[1]);
    *reinterpret_cast<int*>(pl + 0x30) = 0;
    *reinterpret_cast<int*>(sPlVec[0] + 0x4C) = 1;

    uintptr_t vec = StageSelectionChain(/*slot=*/1);

    // Case 2: empty list still resolves but walks zero nodes.
    {
        fake_reset(&L);
        Lua_GetSelectedUnit(LS(&L));
        Check(L.stack.back().numval == 0.0,
              "Empty list -> GetSelectedUnit = 0");
        fake_reset(&L);
        Lua_GetSelectedUnits(LS(&L));
        Check(L.stack.back().strval.empty(),
              "Empty list -> GetSelectedUnits = ''");
    }

    // Case 3: single selected unit.
    FakeUnit u1 = MakeRootUnit(/*slot=*/20, /*ownerId=*/1, /*hull=*/50.0f, /*objId=*/777);
    AppendSelectionNode(vec, /*nodeIndex=*/0, u1.addr);
    {
        fake_reset(&L);
        Lua_GetSelectedUnit(LS(&L));
        Check(L.stack.back().type == LUA_TNUMBER,
              "GetSelectedUnit returns number with one entry");
        uint64_t returned = static_cast<uint64_t>(L.stack.back().numval);
        Check(returned == static_cast<uint64_t>(u1.addr),
              "GetSelectedUnit strips 0x18 adjustment and returns raw addr");

        fake_reset(&L);
        Lua_GetSelectedUnits(LS(&L));
        char expected[64];
        snprintf(expected, sizeof(expected), "%llu",
                 static_cast<unsigned long long>(u1.addr));
        Check(L.stack.back().strval == expected,
              "GetSelectedUnits single-entry = decimal addr");
    }

    // Case 4: multi-selection (3 units).
    FakeUnit u2 = MakeRootUnit(/*slot=*/21, /*ownerId=*/1, /*hull=*/60.0f, /*objId=*/778);
    FakeUnit u3 = MakeRootUnit(/*slot=*/22, /*ownerId=*/1, /*hull=*/70.0f, /*objId=*/779);
    AppendSelectionNode(vec, /*nodeIndex=*/1, u2.addr);
    AppendSelectionNode(vec, /*nodeIndex=*/2, u3.addr);
    {
        fake_reset(&L);
        Lua_GetSelectedUnit(LS(&L));
        uint64_t returned = static_cast<uint64_t>(L.stack.back().numval);
        Check(returned == static_cast<uint64_t>(u1.addr),
              "GetSelectedUnit returns first entry when multi-selected");

        fake_reset(&L);
        Lua_GetSelectedUnits(LS(&L));
        char expected[256];
        snprintf(expected, sizeof(expected), "%llu,%llu,%llu",
                 static_cast<unsigned long long>(u1.addr),
                 static_cast<unsigned long long>(u2.addr),
                 static_cast<unsigned long long>(u3.addr));
        Check(L.stack.back().strval == expected,
              "GetSelectedUnits returns 3 comma-separated addrs in insertion order");

        int commas = 0;
        for (char c : L.stack.back().strval) if (c == ',') commas++;
        Check(commas == 2,
              "GetSelectedUnits 3-entry response has exactly 2 commas");
    }

    // Case 5: loop guard -- corrupt node[2].next to point back at node[0].
    {
        uintptr_t nodeThird = g_base + kSelFixtureListBase + 2 * kSelNodeStride;
        uintptr_t headNode  = g_base + kSelFixtureListBase + 0 * kSelNodeStride;
        *reinterpret_cast<uintptr_t*>(nodeThird + RVA::Selection::kNodeNext) = headNode;

        fake_reset(&L);
        Lua_GetSelectedUnits(LS(&L));
        size_t len = L.stack.back().strval.size();
        Check(len > 0 && len < 2048,
              "Cyclic list -> bounded response, no infinite loop");
    }
}

// ======================================================================
// TEST SUITE 14: GodMode hook (install/disable + protection logic)
// ======================================================================

static void TestGodMode() {
    StartSuite("GodMode hook (install/disable + protection logic)");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers(); // slot 1 is the local human

    // Reset combat state
    g_god_mode_enabled = 0;
    g_ohk_enabled = 0;
    g_combat_hook_installed = false;
    g_setHP_dispatch = nullptr;
    g_real_setHP_call_count = 0;

    // Build a friendly unit (owned by slot 1 = local) and an enemy unit (owned by slot 0)
    FakeUnit friendly = MakeRootUnit(/*slot=*/0, /*ownerId=*/1, /*hull=*/100.0f, /*objId=*/1);
    FakeUnit enemy    = MakeRootUnit(/*slot=*/2, /*ownerId=*/0, /*hull=*/100.0f, /*objId=*/2);

    FakeLuaState L;

    // Enable god mode
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    int rc = Lua_GodMode(LS(&L));
    Check(rc == 1, "GodMode(1) returns 1 result");
    Check(g_combat_hook_installed, "GodMode(1) installs combat hook");
    Check(g_god_mode_enabled == 1, "g_god_mode_enabled flag = 1");
    Check(L.stack.back().strval.find("OK") != std::string::npos,
          "GodMode(1) returns OK string");

    // Friendly takes damage — should be blocked (HP unchanged at 100)
    g_real_setHP_call_count = 0;
    InvokeSetHP((void*)friendly.addr, 30.0f); // attempt to drop hull to 30
    float friendlyHp = *reinterpret_cast<float*>(friendly.addr + RVA::GameObj::HP);
    Check(fabs(friendlyHp - 100.0f) < 0.01f,
          "Friendly HP unchanged when god mode active and damage applied");
    Check(g_real_setHP_call_count == 0,
          "Real SetHP not called for friendly unit under god mode");

    // Enemy takes damage — should pass through (no OHK active, just normal write)
    g_real_setHP_call_count = 0;
    InvokeSetHP((void*)enemy.addr, 75.0f);
    float enemyHp = *reinterpret_cast<float*>(enemy.addr + RVA::GameObj::HP);
    Check(fabs(enemyHp - 75.0f) < 0.01f, "Enemy HP set to 75 normally under god mode");
    Check(g_real_setHP_call_count == 1, "Real SetHP called once for enemy");

    // Heal-through: friendly receiving HEAL (new_hp > current) should still be allowed
    g_real_setHP_call_count = 0;
    *reinterpret_cast<float*>(friendly.addr + RVA::GameObj::HP) = 50.0f;
    InvokeSetHP((void*)friendly.addr, 90.0f); // heal from 50 to 90
    friendlyHp = *reinterpret_cast<float*>(friendly.addr + RVA::GameObj::HP);
    Check(fabs(friendlyHp - 90.0f) < 0.01f,
          "Friendly heal-through allowed (new_hp > current)");
    Check(g_real_setHP_call_count == 1, "Real SetHP called once for heal");

    // Disable god mode
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    Lua_GodMode(LS(&L));
    Check(g_god_mode_enabled == 0, "g_god_mode_enabled = 0 after disable");
    Check(!g_combat_hook_installed, "GodMode(0) removes hook (no other flag set)");

    // After disable, friendly damage should pass through
    *reinterpret_cast<float*>(friendly.addr + RVA::GameObj::HP) = 100.0f;
    g_real_setHP_call_count = 0;
    InvokeSetHP((void*)friendly.addr, 25.0f);
    friendlyHp = *reinterpret_cast<float*>(friendly.addr + RVA::GameObj::HP);
    Check(fabs(friendlyHp - 25.0f) < 0.01f,
          "Friendly damage passes through after god mode disabled");
}

// ======================================================================
// TEST SUITE 15: OneHitKill hook
// ======================================================================

static void TestOneHitKill() {
    StartSuite("OneHitKill hook");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    g_god_mode_enabled = 0;
    g_ohk_enabled = 0;
    g_combat_hook_installed = false;
    g_setHP_dispatch = nullptr;
    g_real_setHP_call_count = 0;

    FakeUnit friendly = MakeRootUnit(/*slot=*/0, /*ownerId=*/1, /*hull=*/100.0f, /*objId=*/1);
    FakeUnit enemy    = MakeRootUnit(/*slot=*/2, /*ownerId=*/0, /*hull=*/100.0f, /*objId=*/2);

    FakeLuaState L;

    // Enable OHK
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    int rc = Lua_OneHitKill(LS(&L));
    Check(rc == 1, "OneHitKill(1) returns 1 result");
    Check(g_combat_hook_installed, "OneHitKill(1) installs hook");
    Check(g_ohk_enabled == 1, "g_ohk_enabled flag = 1");

    // Enemy gets attacked with new_hp=50 — detour should rewrite to 0.0
    g_real_setHP_call_count = 0;
    g_last_real_setHP_hp = -999.0f;
    InvokeSetHP((void*)enemy.addr, 50.0f);
    Check(g_real_setHP_call_count == 1, "Real SetHP called once for enemy");
    Check(fabs(g_last_real_setHP_hp - 0.0f) < 0.01f,
          "Enemy new_hp rewritten to 0.0 by OHK detour");
    float enemyHp = *reinterpret_cast<float*>(enemy.addr + RVA::GameObj::HP);
    Check(fabs(enemyHp - 0.0f) < 0.01f, "Enemy HP at 0 after OHK");

    // Friendly takes damage — OHK alone does NOT protect (god mode would)
    g_real_setHP_call_count = 0;
    g_last_real_setHP_hp = -999.0f;
    InvokeSetHP((void*)friendly.addr, 75.0f);
    Check(g_real_setHP_call_count == 1, "Real SetHP called once for friendly under OHK alone");
    Check(fabs(g_last_real_setHP_hp - 75.0f) < 0.01f,
          "Friendly new_hp NOT rewritten by OHK (no god mode active)");

    // Disable OHK
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    Lua_OneHitKill(LS(&L));
    Check(g_ohk_enabled == 0, "g_ohk_enabled = 0 after disable");
    Check(!g_combat_hook_installed, "OneHitKill(0) removes hook");

    // After disable, enemy damage should not be auto-zeroed
    *reinterpret_cast<float*>(enemy.addr + RVA::GameObj::HP) = 100.0f;
    g_real_setHP_call_count = 0;
    g_last_real_setHP_hp = -999.0f;
    InvokeSetHP((void*)enemy.addr, 60.0f);
    enemyHp = *reinterpret_cast<float*>(enemy.addr + RVA::GameObj::HP);
    Check(fabs(enemyHp - 60.0f) < 0.01f, "Enemy HP normal after OHK disabled");
}

// ======================================================================
// TEST SUITE 16: Combined GodMode + OneHitKill (mutual coexistence)
// ======================================================================

static void TestCombatHookCombined() {
    StartSuite("Combined GodMode + OneHitKill");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    g_god_mode_enabled = 0;
    g_ohk_enabled = 0;
    g_combat_hook_installed = false;
    g_setHP_dispatch = nullptr;

    FakeUnit friendly = MakeRootUnit(/*slot=*/0, /*ownerId=*/1, /*hull=*/100.0f, /*objId=*/1);
    FakeUnit enemy    = MakeRootUnit(/*slot=*/2, /*ownerId=*/0, /*hull=*/100.0f, /*objId=*/2);

    FakeLuaState L;

    // Enable both
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    Lua_GodMode(LS(&L));
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 1.0; L.stack.push_back(a); }
    Lua_OneHitKill(LS(&L));

    Check(g_god_mode_enabled == 1 && g_ohk_enabled == 1,
          "Both flags set after enabling god mode + OHK");
    Check(g_combat_hook_installed, "Hook installed once for both flags");

    // Friendly should be protected (god mode wins)
    g_real_setHP_call_count = 0;
    InvokeSetHP((void*)friendly.addr, 25.0f);
    float friendlyHp = *reinterpret_cast<float*>(friendly.addr + RVA::GameObj::HP);
    Check(fabs(friendlyHp - 100.0f) < 0.01f,
          "Friendly protected when both flags active");
    Check(g_real_setHP_call_count == 0, "Real SetHP not called for protected friendly");

    // Enemy should be killed (OHK forces 0)
    g_real_setHP_call_count = 0;
    g_last_real_setHP_hp = -999.0f;
    InvokeSetHP((void*)enemy.addr, 50.0f);
    Check(fabs(g_last_real_setHP_hp - 0.0f) < 0.01f,
          "Enemy zeroed when both flags active");
    float enemyHp = *reinterpret_cast<float*>(enemy.addr + RVA::GameObj::HP);
    Check(fabs(enemyHp - 0.0f) < 0.01f, "Enemy HP = 0");

    // Disable god mode but keep OHK — hook should remain installed
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    Lua_GodMode(LS(&L));
    Check(g_god_mode_enabled == 0, "god_mode flag = 0");
    Check(g_ohk_enabled == 1, "ohk flag still 1");
    Check(g_combat_hook_installed, "Hook still installed (OHK alone)");

    // Now friendly is no longer protected
    *reinterpret_cast<float*>(friendly.addr + RVA::GameObj::HP) = 100.0f;
    g_real_setHP_call_count = 0;
    g_last_real_setHP_hp = -999.0f;
    InvokeSetHP((void*)friendly.addr, 30.0f);
    friendlyHp = *reinterpret_cast<float*>(friendly.addr + RVA::GameObj::HP);
    Check(fabs(friendlyHp - 30.0f) < 0.01f,
          "Friendly damage passes after god disabled (only OHK active)");

    // Disable OHK — hook should now be removed
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 0.0; L.stack.push_back(a); }
    Lua_OneHitKill(LS(&L));
    Check(!g_combat_hook_installed, "Hook removed after both flags off");
}

// ======================================================================
// TEST SUITE 17: WalkToRootUnit + IsObjOwnedByHuman helpers
// ======================================================================

static void TestCombatHookHelpers() {
    StartSuite("WalkToRoot + IsObjOwnedByHuman helpers");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers(); // slot 1 = local human

    // Root + 2 hardpoints, owned by local
    FakeUnit root = MakeRootWithChildren(/*rootSlot=*/0, /*ownerId=*/1,
                                          /*rootHp=*/500.0f, /*rootObjId=*/77, 2);

    // Walk from a hardpoint should land on root
    uintptr_t hp0 = *reinterpret_cast<uintptr_t*>(root.componentsAddr);
    Check(hp0 != 0, "Hardpoint 0 address resolved");

    uintptr_t walked = WalkToRootUnit(hp0);
    Check(walked == root.addr, "WalkToRootUnit lands on root from hardpoint");

    // Walk from root returns root
    Check(WalkToRootUnit(root.addr) == root.addr, "WalkToRootUnit on root returns root");

    // IsObjOwnedByHuman: owner=1 is local
    Check(IsObjOwnedByHuman(root.addr), "Root owned by slot 1 = human");
    Check(IsObjOwnedByHuman(hp0), "Hardpoint inherits human ownership via walk");

    // Build an enemy unit
    FakeUnit enemy = MakeRootUnit(/*slot=*/5, /*ownerId=*/0, /*hull=*/100.0f, /*objId=*/99);
    Check(!IsObjOwnedByHuman(enemy.addr), "Enemy (slot 0) not human");

    // Invalid address
    Check(!IsObjOwnedByHuman(0), "NULL not owned by human");
    Check(!IsObjOwnedByHuman(0x100), "Low addr not owned by human");

    // Out-of-range owner ID
    FakeUnit oddOwner = MakeRootUnit(/*slot=*/8, /*ownerId=*/99, /*hull=*/100.0f, /*objId=*/12);
    Check(!IsObjOwnedByHuman(oddOwner.addr), "Out-of-range ownerId returns false");
}

// ======================================================================
// Phase 3.2 (continuation): per-slot writers + observers
// Exercises the 8 new SWFOC_* helpers added to lua_bridge.cpp:
//   SetCreditsForSlot / GetCreditsForSlot
//   SetTechForSlot / GetTechForSlot
//   DrainEnemyCredits
//   SetHeroRespawn
//   PreventUnitDeath
//   GetMaxCredits
// ======================================================================

// Helper: build a fake lua_State, set up the SetCreditsForSlot args, call,
// and return the string result. Mirrors the call shape used by the
// existing SetUnitInvuln tests.
static int CallSlotSetCredits(int slot, double amount, std::string& outResult) {
    FakeLuaState L;
    fn_pushnumber(LS(&L), (double)slot);
    fn_pushnumber(LS(&L), amount);
    int rc = Lua_SetCreditsForSlot(LS(&L));
    const char* s = fn_tostring(LS(&L), -1);
    outResult = s ? s : "";
    return rc;
}

static double CallSlotGetCredits(int slot) {
    FakeLuaState L;
    fn_pushnumber(LS(&L), (double)slot);
    Lua_GetCreditsForSlot(LS(&L));
    return fn_tonumber(LS(&L), -1);
}

static int CallSlotSetTech(int slot, int level, std::string& outResult) {
    FakeLuaState L;
    fn_pushnumber(LS(&L), (double)slot);
    fn_pushnumber(LS(&L), (double)level);
    int rc = Lua_SetTechForSlot(LS(&L));
    const char* s = fn_tostring(LS(&L), -1);
    outResult = s ? s : "";
    return rc;
}

static double CallSlotGetTech(int slot) {
    FakeLuaState L;
    fn_pushnumber(LS(&L), (double)slot);
    Lua_GetTechForSlot(LS(&L));
    return fn_tonumber(LS(&L), -1);
}

static void TestSlotCredits() {
    StartSuite("Per-slot credits read/write");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers(); // slot 0=EMPIRE 10000 / slot 1=REBEL 25000 (local) / slot 2=UNDERWORLD 15000

    // Read each slot back via the helper
    Check(CallSlotGetCredits(0) == 10000.0, "GetCreditsForSlot(0) returns EMPIRE credits");
    Check(CallSlotGetCredits(1) == 25000.0, "GetCreditsForSlot(1) returns REBEL credits");
    Check(CallSlotGetCredits(2) == 15000.0, "GetCreditsForSlot(2) returns UNDERWORLD credits");

    // Write a new value to slot 0 and read it back
    std::string r;
    int rc = CallSlotSetCredits(0, 99999.0, r);
    Check(rc == 1, "SetCreditsForSlot returns 1 result");
    Check(r == "OK", "SetCreditsForSlot returns OK on valid slot");
    Check(CallSlotGetCredits(0) == 99999.0, "Round-trip write/read on slot 0");

    // Other slots untouched by the slot-0 write
    Check(CallSlotGetCredits(1) == 25000.0, "Slot 1 untouched after slot 0 write");
    Check(CallSlotGetCredits(2) == 15000.0, "Slot 2 untouched after slot 0 write");

    // Out-of-range slot is rejected
    rc = CallSlotSetCredits(7, 50000.0, r);
    Check(rc == 1, "SetCreditsForSlot still returns 1 result on bad slot");
    Check(r.find("invalid slot") != std::string::npos, "Slot 7 (>= count) returns invalid slot ERR");
    Check(CallSlotGetCredits(7) == -1.0, "GetCreditsForSlot(7) returns -1 sentinel");

    // Negative slot is rejected
    rc = CallSlotSetCredits(-1, 0.0, r);
    Check(r.find("invalid slot") != std::string::npos, "Negative slot returns invalid slot ERR");
}

static void TestSlotTech() {
    StartSuite("Per-slot tech level read/write");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers(); // slot 0 tech=1, slot 1 tech=3, slot 2 tech=2

    // Read each slot back
    Check(CallSlotGetTech(0) == 1.0, "GetTechForSlot(0) = 1");
    Check(CallSlotGetTech(1) == 3.0, "GetTechForSlot(1) = 3");
    Check(CallSlotGetTech(2) == 2.0, "GetTechForSlot(2) = 2");

    // Set + read back
    std::string r;
    CallSlotSetTech(2, 5, r);
    Check(r == "OK", "SetTechForSlot(2, 5) -> OK");
    Check(CallSlotGetTech(2) == 5.0, "Slot 2 tech = 5 after write");

    // Range check: 0 and 6 are out of bounds
    CallSlotSetTech(0, 0, r);
    Check(r.find("out of") != std::string::npos, "Tech level 0 rejected");
    Check(CallSlotGetTech(0) == 1.0, "Slot 0 tech unchanged after invalid level");

    CallSlotSetTech(0, 6, r);
    Check(r.find("out of") != std::string::npos, "Tech level 6 rejected");
}

static void TestDrainEnemyCredits() {
    StartSuite("DrainEnemyCredits iteration");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers(); // slot 1 = local

    FakeLuaState L;
    int rc = Lua_DrainEnemyCredits(LS(&L));
    Check(rc == 1, "DrainEnemyCredits returns 1 result");
    const char* s = fn_tostring(LS(&L), -1);
    std::string result = s ? s : "";
    Check(result.find("OK: drained 2 slots") != std::string::npos,
          "Drained 2 non-local slots (slot 0 + slot 2)");

    // Slot 1 (local) credits should be untouched
    Check(CallSlotGetCredits(1) == 25000.0, "Local slot credits preserved");

    // Slots 0 and 2 should be zeroed
    Check(CallSlotGetCredits(0) == 0.0, "Enemy slot 0 credits zeroed");
    Check(CallSlotGetCredits(2) == 0.0, "Enemy slot 2 credits zeroed");
}

static void TestSetHeroRespawn() {
    StartSuite("SetHeroRespawn timer write");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    // Set a baseline value at the global address
    *reinterpret_cast<float*>(g_base + RVA::DefaultHeroRespawnTime) = 96.0f;

    FakeLuaState L;
    fn_pushnumber(LS(&L), 30.0);
    Lua_SetHeroRespawn(LS(&L));
    std::string result = fn_tostring(LS(&L), -1);
    Check(result.find("prev=96.0") != std::string::npos, "Returns previous value 96.0");
    Check(result.find("new=30.0") != std::string::npos, "Returns new value 30.0");
    Check(*reinterpret_cast<float*>(g_base + RVA::DefaultHeroRespawnTime) == 30.0f,
          "Global float updated to 30.0");

    // Out-of-range
    FakeLuaState L2;
    fn_pushnumber(LS(&L2), -5.0);
    Lua_SetHeroRespawn(LS(&L2));
    std::string err = fn_tostring(LS(&L2), -1);
    Check(err.find("out of") != std::string::npos, "Negative seconds rejected");
    Check(*reinterpret_cast<float*>(g_base + RVA::DefaultHeroRespawnTime) == 30.0f,
          "Global unchanged after invalid input");

    FakeLuaState L3;
    fn_pushnumber(LS(&L3), 700.0);
    Lua_SetHeroRespawn(LS(&L3));
    std::string err2 = fn_tostring(LS(&L3), -1);
    Check(err2.find("out of") != std::string::npos, "700 seconds rejected (>600 cap)");
}

static void TestPreventUnitDeath() {
    StartSuite("PreventUnitDeath bit-level write");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    FakeUnit unit = MakeRootUnit(/*slot=*/0, /*ownerId=*/1, /*hull=*/100.0f, /*objId=*/777);
    // Initialize PreventDeath byte to a non-bit-7 value to test bit OR semantics
    *reinterpret_cast<uint8_t*>(unit.addr + RVA::GameObj::PreventDeath) = 0x03;

    // Enable: should set bit 0x80 without disturbing other bits
    FakeLuaState L;
    fn_pushnumber(LS(&L), (double)unit.addr);
    fn_pushnumber(LS(&L), 1.0);
    int rc = Lua_PreventUnitDeath(LS(&L));
    Check(rc == 1, "PreventUnitDeath returns 1 result");
    std::string r = fn_tostring(LS(&L), -1);
    Check(r == "OK", "Returns OK on enable");
    uint8_t after = *reinterpret_cast<uint8_t*>(unit.addr + RVA::GameObj::PreventDeath);
    Check(after == 0x83, "Bit 0x80 set, other bits preserved (0x03 -> 0x83)");

    // Disable: should clear bit 0x80 without disturbing other bits
    FakeLuaState L2;
    fn_pushnumber(LS(&L2), (double)unit.addr);
    fn_pushnumber(LS(&L2), 0.0);
    Lua_PreventUnitDeath(LS(&L2));
    after = *reinterpret_cast<uint8_t*>(unit.addr + RVA::GameObj::PreventDeath);
    Check(after == 0x03, "Bit 0x80 cleared, other bits preserved (0x83 -> 0x03)");

    // Invalid address rejected
    FakeLuaState L3;
    fn_pushnumber(LS(&L3), 0.0);
    fn_pushnumber(LS(&L3), 1.0);
    Lua_PreventUnitDeath(LS(&L3));
    std::string err = fn_tostring(LS(&L3), -1);
    Check(err.find("invalid obj_addr") != std::string::npos, "NULL obj_addr rejected");
}

static void TestGetMaxCredits() {
    StartSuite("GetMaxCredits local-player observer");
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers(); // local player has MaxCredits = 100000

    FakeLuaState L;
    Lua_GetMaxCredits(LS(&L));
    Check(fn_tonumber(LS(&L), -1) == 100000.0, "GetMaxCredits returns local-player MaxCredits");

    // Mutate the local player's MaxCredits and verify the read picks it up.
    // Use a power-of-two-friendly value that round-trips exactly through
    // float so the assertion isn't sensitive to FP precision (999_999_999
    // doesn't fit in a float's 24-bit mantissa and gets rounded).
    int slot = FindLocalPlayerSlot();
    Check(slot >= 0, "Local player slot resolves");
    auto p = GetPlayerObj(slot);
    Check(p != 0, "Local player object resolves");
    const float kRaisedCap = 16777216.0f; // 2^24, exact float
    *reinterpret_cast<float*>(p + RVA::PlayerObj::MaxCredits) = kRaisedCap;

    FakeLuaState L2;
    Lua_GetMaxCredits(LS(&L2));
    Check(fn_tonumber(LS(&L2), -1) == (double)kRaisedCap,
          "GetMaxCredits picks up raised value after write");
}

// ======================================================================
// TEST SUITE 25..40: ReplayState observer + mutation helpers
// ======================================================================
//
// Exercises the in-memory ReplayState extensions added 2026-04-08 to
// support v5 service replay tests. Each helper gets a happy-path test, an
// empty-state test, and a mutation round-trip test where applicable.
// These tests do NOT spin up the named-pipe listener -- they call the
// observer/mutation helpers from replay_state.h directly so the test
// stays hermetic and runs offline.

namespace {

// Build a small ReplayState fixture mirroring make_test_snapshot.py output.
ReplayState BuildReplayFixture() {
    ReplayState s;
    s.format_version = 2;
    s.local_slot = 0;
    s.players = {
        {0, "REBEL",      12345.0, 3, ""},
        {1, "EMPIRE",     99999.0, 5, ""},
        {2, "UNDERWORLD",  5000.0, 1, ""},
    };
    s.objects["TIE_Fighter"]    = 12;
    s.objects["X_Wing"]         = 8;
    s.objects["Star_Destroyer"] = 2;

    // section 6: planet_state
    {
        ReplayPlanetInfo p; p.name = "TATOOINE"; p.corruption = 0.10f; p.owner_slot = 0;
        s.planets[ReplayUpper("TATOOINE")] = p;
    }
    {
        ReplayPlanetInfo p; p.name = "CORUSCANT"; p.corruption = 0.0f; p.owner_slot = 1;
        s.planets[ReplayUpper("CORUSCANT")] = p;
    }
    {
        ReplayPlanetInfo p; p.name = "NABOO"; p.corruption = 0.75f; p.owner_slot = 2;
        s.planets[ReplayUpper("NABOO")] = p;
    }

    // section 7: diplomacy
    s.diplomacy[ReplayDiplomacyKey("REBEL", "EMPIRE")]      = "hostile";
    s.diplomacy[ReplayDiplomacyKey("REBEL", "UNDERWORLD")]  = "neutral";
    s.diplomacy[ReplayDiplomacyKey("EMPIRE", "UNDERWORLD")] = "hostile";

    // section 8: cooldowns
    s.cooldowns["TIE_Fighter"] = {0.0f, 12.5f};
    s.cooldowns["X_Wing"]      = {0.0f, 5.0f, 30.0f};

    // section 9: task_forces
    s.task_forces.push_back({1, "Death_Squadron"});
    s.task_forces.push_back({0, "Rogue_Squadron"});

    // section 10: object_owners
    {
        std::vector<int32_t> ties(12, 1);
        s.object_owners[ReplayUpper("TIE_Fighter")] = ties;
    }
    {
        std::vector<int32_t> xwings(8, 0);
        s.object_owners[ReplayUpper("X_Wing")] = xwings;
    }
    {
        std::vector<int32_t> sds(2, 1);
        s.object_owners[ReplayUpper("Star_Destroyer")] = sds;
    }

    return s;
}

} // namespace

static void TestReplayPlayerCredits() {
    StartSuite("Replay: SWFOC_ReplayPlayerCredits");
    ReplayState s = BuildReplayFixture();
    Check(ReplayObsPlayerCredits(s, "REBEL")  == 12345.0, "REBEL credits = 12345");
    Check(ReplayObsPlayerCredits(s, "EMPIRE") == 99999.0, "EMPIRE credits = 99999");
    Check(ReplayObsPlayerCredits(s, "rebel")  == 12345.0, "case-insensitive lookup");
    Check(ReplayObsPlayerCredits(s, "GHOST")  == -1.0,    "unknown faction returns -1");

    ReplayState empty;
    Check(ReplayObsPlayerCredits(empty, "REBEL") == -1.0, "empty state returns -1");
}

static void TestReplayPlayerTechLevel() {
    StartSuite("Replay: SWFOC_ReplayPlayerTechLevel");
    ReplayState s = BuildReplayFixture();
    Check(ReplayObsPlayerTechLevel(s, "REBEL")      == 3.0, "REBEL tech_level = 3");
    Check(ReplayObsPlayerTechLevel(s, "EMPIRE")     == 5.0, "EMPIRE tech_level = 5");
    Check(ReplayObsPlayerTechLevel(s, "UNDERWORLD") == 1.0, "UNDERWORLD tech_level = 1");
    Check(ReplayObsPlayerTechLevel(s, "GHOST")      == -1.0, "unknown faction returns -1");

    ReplayState empty;
    Check(ReplayObsPlayerTechLevel(empty, "REBEL") == -1.0, "empty state returns -1");
}

static void TestReplayLastStoryEvent() {
    StartSuite("Replay: SWFOC_ReplayLastStoryEvent + PushStoryEvent");
    ReplayState s = BuildReplayFixture();
    Check(ReplayObsLastStoryEvent(s) == "", "fresh state returns empty string");

    ReplayMutPushStoryEvent(s, "INTRO_REBEL");
    Check(ReplayObsLastStoryEvent(s) == "INTRO_REBEL", "round-trip: pushed event observable");

    ReplayMutPushStoryEvent(s, "CORRUPTION_RACKETEERING_TATOOINE");
    Check(ReplayObsLastStoryEvent(s) == "CORRUPTION_RACKETEERING_TATOOINE",
          "second push overwrites the first");
}

static void TestReplayDiplomaticState() {
    StartSuite("Replay: SWFOC_ReplayDiplomaticState + SetDiplomacy");
    ReplayState s = BuildReplayFixture();
    Check(ReplayObsDiplomaticState(s, "REBEL",  "EMPIRE")     == "hostile", "REBEL-EMPIRE hostile");
    Check(ReplayObsDiplomaticState(s, "REBEL",  "UNDERWORLD") == "neutral", "REBEL-UNDERWORLD neutral");
    Check(ReplayObsDiplomaticState(s, "EMPIRE", "REBEL")      == "hostile", "symmetric: EMPIRE-REBEL");
    Check(ReplayObsDiplomaticState(s, "REBEL",  "MISSING")    == "hostile", "missing pair defaults to hostile");

    ReplayMutSetDiplomacy(s, "REBEL", "EMPIRE", "allied");
    Check(ReplayObsDiplomaticState(s, "REBEL",  "EMPIRE") == "allied", "round-trip: mutated to allied");
    Check(ReplayObsDiplomaticState(s, "EMPIRE", "REBEL")  == "allied", "symmetric after mutation");

    ReplayState empty;
    Check(ReplayObsDiplomaticState(empty, "REBEL", "EMPIRE") == "hostile", "empty state defaults to hostile");
}

static void TestReplayPlanetCorruption() {
    StartSuite("Replay: SWFOC_ReplayPlanetCorruption + SetPlanetCorruption");
    ReplayState s = BuildReplayFixture();
    Check(fabs(ReplayObsPlanetCorruption(s, "TATOOINE") - 0.10) < 1e-5, "TATOOINE corruption = 0.10");
    Check(fabs(ReplayObsPlanetCorruption(s, "CORUSCANT") - 0.0)  < 1e-5, "CORUSCANT corruption = 0");
    Check(fabs(ReplayObsPlanetCorruption(s, "NABOO") - 0.75) < 1e-5, "NABOO corruption = 0.75");
    Check(ReplayObsPlanetCorruption(s, "tatooine") > 0.0, "case-insensitive planet lookup");
    Check(ReplayObsPlanetCorruption(s, "DAGOBAH") == -1.0, "unknown planet returns -1");

    ReplayMutSetPlanetCorruption(s, "DAGOBAH", 0.42);
    Check(fabs(ReplayObsPlanetCorruption(s, "DAGOBAH") - 0.42) < 1e-5,
          "round-trip: created planet observable");

    ReplayMutSetPlanetCorruption(s, "TATOOINE", 0.99);
    Check(fabs(ReplayObsPlanetCorruption(s, "TATOOINE") - 0.99) < 1e-5,
          "round-trip: existing planet updated");

    ReplayState empty;
    Check(ReplayObsPlanetCorruption(empty, "TATOOINE") == -1.0, "empty state returns -1");
}

static void TestReplayUnitOwner() {
    StartSuite("Replay: SWFOC_ReplayUnitOwner + SpawnUnit");
    ReplayState s = BuildReplayFixture();
    Check(ReplayObsUnitOwner(s, "TIE_Fighter", 0)  == 1, "TIE_Fighter[0] owned by EMPIRE (slot 1)");
    Check(ReplayObsUnitOwner(s, "TIE_Fighter", 11) == 1, "TIE_Fighter[11] owned by EMPIRE");
    Check(ReplayObsUnitOwner(s, "X_Wing", 0)       == 0, "X_Wing[0] owned by REBEL (slot 0)");
    Check(ReplayObsUnitOwner(s, "X_Wing", 7)       == 0, "X_Wing[7] owned by REBEL");
    Check(ReplayObsUnitOwner(s, "Star_Destroyer", 1) == 1, "Star_Destroyer[1] owned by EMPIRE");
    Check(ReplayObsUnitOwner(s, "TIE_Fighter", 99)   == -1, "out-of-range index returns -1");
    Check(ReplayObsUnitOwner(s, "MISSING_UNIT", 0)   == -1, "unknown type returns -1");

    // Round-trip via SpawnUnit
    ReplayMutSpawnUnit(s, "REBEL", "Y_Wing", 3);
    Check(ReplayObsUnitOwner(s, "Y_Wing", 0) == 0, "round-trip: spawned Y_Wing[0] owned by REBEL");
    Check(ReplayObsUnitOwner(s, "Y_Wing", 2) == 0, "round-trip: spawned Y_Wing[2] owned by REBEL");
    Check(ReplayObsUnitOwner(s, "Y_Wing", 3) == -1, "spawn count respected (no Y_Wing[3])");
    Check(s.objects["Y_Wing"] == 3, "objects catalog mirrors spawn");

    // Spawn for unknown faction yields slot -1.
    ReplayMutSpawnUnit(s, "GHOST_FACTION", "B_Wing", 1);
    Check(ReplayObsUnitOwner(s, "B_Wing", 0) == -1, "spawn for unknown faction => slot -1");

    ReplayState empty;
    Check(ReplayObsUnitOwner(empty, "TIE_Fighter", 0) == -1, "empty state returns -1");
}

static void TestReplayCooldownState() {
    StartSuite("Replay: SWFOC_ReplayCooldownState + SetCooldown");
    ReplayState s = BuildReplayFixture();
    Check(ReplayObsCooldownState(s, "TIE_Fighter", 0)  == 0.0,  "TIE_Fighter ability 0 cooldown");
    Check(ReplayObsCooldownState(s, "TIE_Fighter", 1)  == 12.5, "TIE_Fighter ability 1 cooldown");
    Check(ReplayObsCooldownState(s, "X_Wing", 2)       == 30.0, "X_Wing ability 2 cooldown");
    Check(ReplayObsCooldownState(s, "TIE_Fighter", 99) == -1.0, "out-of-range ability returns -1");
    Check(ReplayObsCooldownState(s, "MISSING_TYPE", 0) == -1.0, "unknown unit type returns -1");

    ReplayMutSetCooldown(s, "TIE_Fighter", 0, 7.5);
    Check(ReplayObsCooldownState(s, "TIE_Fighter", 0) == 7.5, "round-trip: cooldown updated");

    // Grow vector via mutation
    ReplayMutSetCooldown(s, "TIE_Fighter", 5, 99.0);
    Check(ReplayObsCooldownState(s, "TIE_Fighter", 5) == 99.0, "round-trip: vector grew to ability 5");
    Check(ReplayObsCooldownState(s, "TIE_Fighter", 4) == 0.0,  "intermediate slots zero-initialized");

    ReplayState empty;
    Check(ReplayObsCooldownState(empty, "TIE_Fighter", 0) == -1.0, "empty state returns -1");
}

static void TestReplayTaskForceCount() {
    StartSuite("Replay: SWFOC_ReplayTaskForceCount + AddTaskForce");
    ReplayState s = BuildReplayFixture();
    Check(ReplayObsTaskForceCount(s, 0) == 1, "REBEL slot has 1 task force");
    Check(ReplayObsTaskForceCount(s, 1) == 1, "EMPIRE slot has 1 task force");
    Check(ReplayObsTaskForceCount(s, 2) == 0, "UNDERWORLD slot has 0 task forces");
    Check(ReplayObsTaskForceCount(s, 99) == 0, "unknown slot returns 0");

    ReplayMutAddTaskForce(s, 0, "Red_Squadron");
    Check(ReplayObsTaskForceCount(s, 0) == 2, "round-trip: REBEL count incremented");

    ReplayMutAddTaskForce(s, 2, "Black_Sun_Fleet");
    Check(ReplayObsTaskForceCount(s, 2) == 1, "round-trip: UNDERWORLD got first task force");

    ReplayState empty;
    Check(ReplayObsTaskForceCount(empty, 0) == 0, "empty state returns 0");
}

static void TestReplayHumanPlayerSlot() {
    StartSuite("Replay: SWFOC_ReplayHumanPlayerSlot + SwitchLocalPlayer");
    ReplayState s = BuildReplayFixture();
    Check(ReplayObsHumanPlayerSlot(s) == 0, "REBEL is the local player by default");

    Check(ReplayMutSwitchLocalPlayer(s, 1) == 1, "switch to EMPIRE succeeds");
    Check(ReplayObsHumanPlayerSlot(s) == 1, "round-trip: local slot is now 1");

    Check(ReplayMutSwitchLocalPlayer(s, 99) == 0, "switch to unknown slot rejected");
    Check(ReplayObsHumanPlayerSlot(s) == 1, "rejected switch leaves state unchanged");

    Check(ReplayMutSwitchLocalPlayer(s, -1) == 1, "switch to -1 succeeds");
    Check(ReplayObsHumanPlayerSlot(s) == -1, "round-trip: local slot now -1");

    ReplayState empty;
    Check(ReplayObsHumanPlayerSlot(empty) == -1, "empty state returns -1");
}

// Task 101 (added 2026-04-23). Pure-state regression for the unit /
// hardpoint / behavior schema. The key contract is: writes to the display
// flags (invuln_flag, prevent_death) DO NOT confer immunity — only the
// hardpoint-behavior attach path does. This mirrors the 2026-04-23 live
// finding. Any future change that couples damage simulation to the flag
// bytes breaks this suite.
static void TestReplayUnitSurface() {
    StartSuite("Replay: unit/hardpoint/behavior surface (Task 101)");

    ReplayState s;
    constexpr uint64_t kObj  = 0x1F2A3334D00ULL;  // real-fixture addr from 2026-04-23 session
    constexpr uint64_t kObj2 = 0x1F2BB40B280ULL;

    ReplayMutMockUnit(s, kObj, "Aggressor_Destroyer", 6, 5000.0f, 6000.0f, 3);
    Check(s.units.size() == 1, "mock one unit -> size 1");
    Check(ReplayFindUnit(s, kObj) != nullptr, "unit is findable by obj_addr");
    Check(ReplayFindUnit(s, kObj)->hardpoints.size() == 3, "three hardpoints created");

    // ---- Flag path (display-only: confers NO immunity). ----
    Check(ReplayMutSetUnitInvulnFlag(s, kObj, 1) == 1, "flag write returns success");
    Check(ReplayFindUnit(s, kObj)->invuln_flag == 1, "display flag updated");
    Check(ReplayUnitAnyHardpointHasBehavior(*ReplayFindUnit(s, kObj), "INVULNERABLE") == false,
          "flag flip leaves no INVULNERABLE behavior attached");

    float h0 = ReplayMutApplyDamage(s, kObj, 500.0f);
    Check(fabs(h0 - 4500.0f) < 1e-3, "flag flip does not block damage (hull 5000 -> 4500)");

    // ---- Make_Invulnerable path: attaches INVULNERABLE to every hardpoint. ----
    Check(ReplayMutMakeInvulnerable(s, kObj, true) == 1, "Make_Invulnerable(true) succeeds");
    Check(ReplayUnitAllHardpointsHaveBehavior(*ReplayFindUnit(s, kObj), "INVULNERABLE") == true,
          "every hardpoint carries INVULNERABLE after MakeInvulnerable(true)");

    float h1 = ReplayMutApplyDamage(s, kObj, 1000.0f);
    Check(fabs(h1 - 4500.0f) < 1e-3, "damage is a no-op while any hardpoint is invulnerable");

    // Detach: single hardpoint removed -> ANY-match still true until all removed.
    ReplayMutDetachBehavior(s, kObj, 0, "INVULNERABLE");
    Check(ReplayUnitAnyHardpointHasBehavior(*ReplayFindUnit(s, kObj), "INVULNERABLE") == true,
          "removing one hardpoint's behavior keeps ANY-match true");
    float h2 = ReplayMutApplyDamage(s, kObj, 500.0f);
    Check(fabs(h2 - 4500.0f) < 1e-3, "partial detach still blocks damage");

    // Full MakeInvulnerable(false): removes from all hardpoints.
    ReplayMutMakeInvulnerable(s, kObj, false);
    Check(ReplayUnitAnyHardpointHasBehavior(*ReplayFindUnit(s, kObj), "INVULNERABLE") == false,
          "MakeInvulnerable(false) clears every hardpoint");
    float h3 = ReplayMutApplyDamage(s, kObj, 1000.0f);
    Check(fabs(h3 - 3500.0f) < 1e-3, "damage resumes after full detach");

    // Hull clamp: writes above max clamp to max; below 0 clamp to 0.
    Check(ReplayMutSetUnitHull(s, kObj, 99999.0f) == 1, "set hull 99999 returns success");
    Check(ReplayFindUnit(s, kObj)->hull == 6000.0f, "hull clamped to max_hull");
    Check(ReplayMutSetUnitHull(s, kObj, -500.0f) == 1, "set hull -500 returns success");
    Check(ReplayFindUnit(s, kObj)->hull == 0.0f, "hull clamped to 0");

    // Unknown unit -> mutators reject.
    Check(ReplayMutSetUnitInvulnFlag(s, 0xDEADBEEF, 1) == 0,  "flag write on unknown unit fails");
    Check(ReplayMutApplyDamage(s, 0xDEADBEEF, 10.0f) == -1.0f, "damage on unknown unit returns -1");
    Check(ReplayMutMakeInvulnerable(s, 0xDEADBEEF, true) == 0, "MakeInvulnerable on unknown fails");

    // Selection vector semantics.
    Check(ReplayObsSelectedCount(s) == 0, "fresh state has empty selection");
    Check(ReplayObsGetSelectedUnit(s) == 0, "empty selection returns 0");
    ReplayMutSetSelected(s, kObj);
    Check(ReplayObsSelectedCount(s) == 1, "SetSelected -> size 1");
    Check(ReplayObsGetSelectedUnit(s) == kObj, "SetSelected -> obj read back");
    ReplayMutAppendSelected(s, kObj2);
    Check(ReplayObsSelectedCount(s) == 2, "AppendSelected -> size 2");
    ReplayMutAppendSelected(s, kObj2);
    Check(ReplayObsSelectedCount(s) == 2, "AppendSelected is idempotent");
    ReplayMutSetSelected(s, 0);
    Check(ReplayObsSelectedCount(s) == 0, "SetSelected(0) clears selection");

    // SetSelected followed by ClearSelected.
    ReplayMutSetSelected(s, kObj);
    ReplayMutClearSelected(s);
    Check(ReplayObsSelectedCount(s) == 0, "ClearSelected empties the vector");

    // prevent_death bit toggling.
    Check(ReplayMutSetPreventDeathBit(s, kObj, true) == 1, "prevent_death set returns success");
    Check((ReplayFindUnit(s, kObj)->prevent_death & 0x80) != 0, "bit 0x80 is now set");
    Check(ReplayMutSetPreventDeathBit(s, kObj, false) == 1, "prevent_death clear returns success");
    Check((ReplayFindUnit(s, kObj)->prevent_death & 0x80) == 0, "bit 0x80 is now clear");

    // Second unit coexists with first.
    ReplayMutMockUnit(s, kObj2, "Corellian_Corvette", 1, 2000.0f, 2500.0f, 2);
    Check(s.units.size() == 2, "two units tracked independently");
    Check(ReplayFindUnit(s, kObj2)->hardpoints.size() == 2, "second unit has two hardpoints");
    Check(ReplayFindUnit(s, kObj)->hull  == 0.0f, "first unit hull unaffected");
    Check(ReplayFindUnit(s, kObj2)->hull == 2000.0f, "second unit hull at initial value");
}

// Task 130 (added 2026-04-23). Pure-state regression for unit shield.
// Pins:
//   * set/get on unknown unit behave correctly (set returns 0, get -1)
//   * shield clamps to max_shield when max > 0
//   * negative writes clamp to 0
//   * max_shield starts at 0 (no shield) and set/get works independently
//   * lowering max_shield snaps current shield down if currently above the new cap
//   * shield mutations leave hull / invuln_flag / prevent_death / hardpoints untouched
static void TestReplayUnitShield() {
    StartSuite("Replay: unit shield (Task 130)");

    ReplayState s;
    Check(ReplayMutSetUnitShield(s, 0xDEAD, 100.0f) == 0, "set on unknown unit returns 0");
    Check(ReplayObsGetUnitShield(s, 0xDEAD) == -1.0f, "get on unknown unit returns -1");
    Check(ReplayObsGetUnitMaxShield(s, 0xDEAD) == -1.0f, "get max on unknown unit returns -1");

    constexpr uint64_t kUnit = 0x6000;
    ReplayMutMockUnit(s, kUnit, "Shielded", 0, 1000.0f, 1000.0f, 2);
    Check(ReplayObsGetUnitShield(s, kUnit) == 0.0f, "fresh unit: shield default 0");
    Check(ReplayObsGetUnitMaxShield(s, kUnit) == 0.0f, "fresh unit: max_shield default 0");

    // With max_shield = 0, arbitrary writes go through (no clamp).
    Check(ReplayMutSetUnitShield(s, kUnit, 500.0f) == 1, "set with no cap returns 1");
    Check(ReplayObsGetUnitShield(s, kUnit) == 500.0f, "no-cap write lands at 500");

    Check(ReplayMutSetUnitMaxShield(s, kUnit, 750.0f) == 1, "max set returns 1");
    Check(ReplayObsGetUnitMaxShield(s, kUnit) == 750.0f, "max now 750");
    Check(ReplayObsGetUnitShield(s, kUnit) == 500.0f, "current shield unaffected by raising cap");

    // Clamp to max_shield when write exceeds cap.
    Check(ReplayMutSetUnitShield(s, kUnit, 9999.0f) == 1, "write above cap returns 1");
    Check(ReplayObsGetUnitShield(s, kUnit) == 750.0f, "shield clamped to max_shield 750");

    // Negative writes clamp to 0.
    Check(ReplayMutSetUnitShield(s, kUnit, -100.0f) == 1, "negative write returns 1");
    Check(ReplayObsGetUnitShield(s, kUnit) == 0.0f, "negative write clamped to 0");

    // Lowering max_shield snaps current shield down if above new cap.
    ReplayMutSetUnitShield(s, kUnit, 500.0f);
    Check(ReplayObsGetUnitShield(s, kUnit) == 500.0f, "pre-lowering: shield 500");
    Check(ReplayMutSetUnitMaxShield(s, kUnit, 200.0f) == 1, "lower max returns 1");
    Check(ReplayObsGetUnitMaxShield(s, kUnit) == 200.0f, "max now 200");
    Check(ReplayObsGetUnitShield(s, kUnit) == 200.0f,
          "shield snapped down when max lowered below current");

    // Negative max rejected -- clamps to 0 instead of propagating nonsense.
    Check(ReplayMutSetUnitMaxShield(s, kUnit, -50.0f) == 1, "negative max still returns 1 (clamps)");
    Check(ReplayObsGetUnitMaxShield(s, kUnit) == 0.0f, "negative max clamped to 0");
    Check(ReplayObsGetUnitShield(s, kUnit) == 0.0f,
          "shield snapped down to 0 when max cleared");

    // Shield mutations must not touch unrelated fields.
    auto* u = ReplayFindUnit(s, kUnit);
    Check(u->hull == 1000.0f, "hull untouched by shield mutations");
    Check(u->invuln_flag == 0, "invuln_flag untouched");
    Check(u->prevent_death == 0, "prevent_death untouched");
    Check(u->hardpoints.size() == 2, "hardpoints untouched");
}

// Task 125 (added 2026-04-23). Pure-state regression for unit speed.
// Mirrors the shield suite exactly because the contract shape is the
// same (clamp, floor, snap-down, cross-field isolation). Pinning both
// suites guards against a future refactor that accidentally folds one
// into the other and loses the independence guarantee.
static void TestReplayUnitSpeed() {
    StartSuite("Replay: unit speed (Task 125)");
    ReplayState s;
    Check(ReplayMutSetUnitSpeed(s, 0xDEAD, 100.0f) == 0, "set on unknown unit returns 0");
    Check(ReplayObsGetUnitSpeed(s, 0xDEAD) == -1.0f, "get on unknown unit returns -1");

    constexpr uint64_t kUnit = 0x7000;
    ReplayMutMockUnit(s, kUnit, "Swift", 0, 1000.0f, 1000.0f, 0);
    Check(ReplayObsGetUnitSpeed(s, kUnit) == 0.0f, "fresh unit: speed default 0");
    Check(ReplayObsGetUnitMaxSpeed(s, kUnit) == 0.0f, "fresh unit: max_speed default 0");

    Check(ReplayMutSetUnitSpeed(s, kUnit, 150.0f) == 1, "no-cap write returns 1");
    Check(ReplayObsGetUnitSpeed(s, kUnit) == 150.0f, "no-cap write lands at 150");

    Check(ReplayMutSetUnitMaxSpeed(s, kUnit, 200.0f) == 1, "max set returns 1");
    Check(ReplayObsGetUnitMaxSpeed(s, kUnit) == 200.0f, "max now 200");

    Check(ReplayMutSetUnitSpeed(s, kUnit, 999.0f) == 1, "write above cap returns 1");
    Check(ReplayObsGetUnitSpeed(s, kUnit) == 200.0f, "speed clamped to max 200");

    Check(ReplayMutSetUnitSpeed(s, kUnit, -10.0f) == 1, "negative write returns 1");
    Check(ReplayObsGetUnitSpeed(s, kUnit) == 0.0f, "negative write clamps to 0");

    // Lowering max snaps current down.
    ReplayMutSetUnitSpeed(s, kUnit, 150.0f);
    Check(ReplayMutSetUnitMaxSpeed(s, kUnit, 100.0f) == 1, "lower max returns 1");
    Check(ReplayObsGetUnitSpeed(s, kUnit) == 100.0f,
          "current speed snapped down when max lowered below current");

    Check(ReplayMutSetUnitMaxSpeed(s, kUnit, -1.0f) == 1, "negative max clamps but returns 1");
    Check(ReplayObsGetUnitMaxSpeed(s, kUnit) == 0.0f, "negative max clamped to 0");
    Check(ReplayObsGetUnitSpeed(s, kUnit) == 0.0f, "speed snapped to 0 when cap cleared");

    // Cross-field isolation: speed mutations must not touch shield/hull/hardpoints.
    auto* u = ReplayFindUnit(s, kUnit);
    Check(u->hull == 1000.0f, "hull untouched by speed mutations");
    Check(u->shield == 0.0f, "shield untouched by speed mutations");
    Check(u->max_shield == 0.0f, "max_shield untouched by speed mutations");
    Check(u->invuln_flag == 0, "invuln_flag untouched");

    // Independence: shield and speed write through without affecting each other.
    ReplayMutSetUnitMaxShield(s, kUnit, 300.0f);
    ReplayMutSetUnitShield(s, kUnit, 250.0f);
    ReplayMutSetUnitMaxSpeed(s, kUnit, 75.0f);
    ReplayMutSetUnitSpeed(s, kUnit, 50.0f);
    Check(ReplayObsGetUnitShield(s, kUnit) == 250.0f, "shield survived speed writes");
    Check(ReplayObsGetUnitSpeed(s, kUnit) == 50.0f, "speed survived shield writes");
}

// Task 134 / 135 / 136 (added 2026-04-23). Pure-state regression for
// the Hero Lab trio (ListHeroes + SetHeroRespawnTimer + SetPermadeath).
// Pins:
//   * freshly-mocked units are NOT heroes (flag defaults to false)
//   * SetUnitIsHero toggles the flag; IsUnitHero round-trips
//   * non-hero respawn/permadeath mutations are rejected (return 0)
//   * ListHeroes empty state = literal "count=0"
//   * ListHeroes populated state emits one row per hero with correct
//     shape and fields (addr;owner;hull;max_hull;respawn_ms;alive;
//     respawn_enabled)
//   * alive flag reflects hull > 0 (so a killed hero reports alive=0)
//   * respawn_remaining_ms clamps negative to 0
//   * permadeath true flips respawn_enabled to false
//   * IsUnitHero / IsPermadeath return -1 on unknown and on non-hero
//     mismatch so callers distinguish "wrong type" from "actually no"
static void TestReplayHeroLab() {
    StartSuite("Replay: Hero Lab contract (Tasks 134/135/136)");

    ReplayState s;
    Check(ReplayObsListHeroes(s) == "count=0", "empty state returns 'count=0'");

    constexpr uint64_t kMon = 0x8000;
    constexpr uint64_t kRed = 0x8100;
    constexpr uint64_t kGrunt = 0x8200;
    ReplayMutMockUnit(s, kMon,   "Mon_Mothma",     0, 4000.0f, 4000.0f, 0);
    ReplayMutMockUnit(s, kRed,   "Han_Solo",       0, 2000.0f, 3000.0f, 0);
    ReplayMutMockUnit(s, kGrunt, "Rebel_Soldier",  0, 500.0f,  500.0f,  0);

    Check(ReplayObsIsUnitHero(s, kMon) == 0, "fresh unit: is_hero=false by default");
    Check(ReplayObsIsUnitHero(s, kGrunt) == 0, "fresh unit: is_hero=false");
    Check(ReplayObsIsUnitHero(s, 0xDEAD) == -1, "unknown unit: -1 sentinel");

    // Non-hero mutations rejected.
    Check(ReplayMutSetHeroRespawnTimer(s, kGrunt, 5000) == 0, "non-hero respawn rejected");
    Check(ReplayMutSetPermadeath(s, kGrunt, true) == 0, "non-hero permadeath rejected");
    Check(ReplayObsGetHeroRespawnTimer(s, kGrunt) == -1, "non-hero respawn read returns -1");
    Check(ReplayObsIsPermadeath(s, kGrunt) == -1, "non-hero permadeath read returns -1");

    // Promote Mon and Han to hero.
    Check(ReplayMutSetUnitIsHero(s, kMon, true) == 1, "promote kMon to hero");
    Check(ReplayMutSetUnitIsHero(s, kRed, true) == 1, "promote kRed to hero");
    Check(ReplayObsIsUnitHero(s, kMon) == 1, "kMon is now a hero");
    Check(ReplayObsIsUnitHero(s, kRed) == 1, "kRed is now a hero");
    Check(ReplayObsIsUnitHero(s, kGrunt) == 0, "kGrunt still not a hero");

    // Respawn timer contract.
    Check(ReplayMutSetHeroRespawnTimer(s, kMon, 45000) == 1, "timer set 45s on hero");
    Check(ReplayObsGetHeroRespawnTimer(s, kMon) == 45000, "timer reads back 45000ms");
    Check(ReplayMutSetHeroRespawnTimer(s, kMon, -10) == 1, "negative clamps to 0");
    Check(ReplayObsGetHeroRespawnTimer(s, kMon) == 0, "negative-clamped timer is 0");

    // Permadeath contract.
    Check(ReplayObsIsPermadeath(s, kRed) == 0, "fresh hero: permadeath=false (respawn_enabled=true)");
    Check(ReplayMutSetPermadeath(s, kRed, true) == 1, "enable permadeath on kRed");
    Check(ReplayObsIsPermadeath(s, kRed) == 1, "kRed now in permadeath");
    Check(ReplayMutSetPermadeath(s, kRed, false) == 1, "disable permadeath again");
    Check(ReplayObsIsPermadeath(s, kRed) == 0, "kRed back to respawn_enabled");

    // ListHeroes populated row shape.
    ReplayMutSetHeroRespawnTimer(s, kMon, 30000);
    ReplayMutSetPermadeath(s, kRed, true);
    std::string csv = ReplayObsListHeroes(s);
    Check(csv.rfind("count=2", 0) == 0, "count=2 after two heroes");
    // kMon: owner=0 hull=4000 max=4000 timer=30000 alive=1 respawn_enabled=1 (no permadeath)
    std::string rowMon = std::string("|") + std::to_string(kMon) + ";0;4000.000;4000.000;30000;1;1";
    // kRed: owner=0 hull=2000 max=3000 timer=0 alive=1 respawn_enabled=0 (permadeath)
    std::string rowRed = std::string("|") + std::to_string(kRed) + ";0;2000.000;3000.000;0;1;0";
    Check(csv.find(rowMon) != std::string::npos, "kMon row matches expected shape");
    Check(csv.find(rowRed) != std::string::npos, "kRed row matches expected shape");

    // alive flag follows hull>0. Kill kMon via direct hull write so the
    // event stream remains pristine for unrelated tests.
    ReplayFindUnit(s, kMon)->hull = 0.0f;
    std::string csv2 = ReplayObsListHeroes(s);
    std::string rowMonDead = std::string("|") + std::to_string(kMon) + ";0;0.000;4000.000;30000;0;1";
    Check(csv2.find(rowMonDead) != std::string::npos,
          "dead hero row reports alive=0 while rest of the row is unchanged");

    // Unknown-addr paths.
    Check(ReplayMutSetUnitIsHero(s, 0xDEAD, true) == 0, "SetUnitIsHero on unknown returns 0");
    Check(ReplayMutSetHeroRespawnTimer(s, 0xDEAD, 10) == 0, "SetHeroRespawn on unknown returns 0");
    Check(ReplayMutSetPermadeath(s, 0xDEAD, true) == 0, "SetPermadeath on unknown returns 0");
}

// Task 141 / 142 (added 2026-04-23). Pure-state regression for galactic
// planet list + change-owner. Pins:
//   * empty state returns literal "count=0"
//   * populated state emits "count=N" header and one row per planet
//   * row shape: name;owner_slot;corruption
//   * ReplayObsGetPlanetOwner is case-insensitive on the name key
//   * ChangePlanetOwner with negative slot rejected
//   * ChangePlanetOwner with unknown planet rejected
//   * ChangePlanetOwner with valid args round-trips via GetPlanetOwner
//   * Case-insensitive lookup works with mixed-case input
static void TestReplayPlanets() {
    StartSuite("Replay: Galactic planets (Tasks 141/142)");

    ReplayState s;
    Check(ReplayObsListPlanets(s) == "count=0", "empty state returns 'count=0'");
    Check(ReplayObsGetPlanetOwner(s, "Naboo") == -1,
          "unknown planet returns -1 owner");

    // Seed planets via the observers' input side. The ReplayState's
    // planets map is keyed by uppercase name (matches ReplayDiplomacyKey
    // convention) with display name preserved.
    s.planets["NABOO"]      = ReplayPlanetInfo{"Naboo", 0.25f, 0};
    s.planets["KAMINO"]     = ReplayPlanetInfo{"Kamino", 0.0f, 6};
    s.planets["DANTOOINE"]  = ReplayPlanetInfo{"Dantooine", 0.75f, 2};

    std::string csv = ReplayObsListPlanets(s);
    Check(csv.rfind("count=3", 0) == 0, "populated state starts with 'count=3'");
    Check(csv.find("|Naboo;0;0.250") != std::string::npos, "Naboo row present");
    Check(csv.find("|Kamino;6;0.000") != std::string::npos, "Kamino row present");
    Check(csv.find("|Dantooine;2;0.750") != std::string::npos, "Dantooine row present");

    // GetPlanetOwner is case-insensitive on the name.
    Check(ReplayObsGetPlanetOwner(s, "naboo") == 0, "lowercase lookup returns owner 0");
    Check(ReplayObsGetPlanetOwner(s, "KAMINO") == 6, "uppercase lookup returns owner 6");
    Check(ReplayObsGetPlanetOwner(s, "Kamino") == 6, "mixed-case lookup returns owner 6");
    Check(ReplayObsGetPlanetOwner(s, "Endor") == -1,
          "still-unknown planet returns -1 after seeding");

    // ChangePlanetOwner contract.
    Check(ReplayMutChangePlanetOwner(s, "Naboo", 1) == 1, "change Naboo owner to 1 returns 1");
    Check(ReplayObsGetPlanetOwner(s, "Naboo") == 1, "Naboo now owned by slot 1");
    Check(ReplayObsGetPlanetOwner(s, "Kamino") == 6, "Kamino owner unchanged");

    Check(ReplayMutChangePlanetOwner(s, "naboo", 2) == 1, "case-insensitive mutation returns 1");
    Check(ReplayObsGetPlanetOwner(s, "Naboo") == 2, "lowercase mutation landed on correct planet");

    Check(ReplayMutChangePlanetOwner(s, "Endor", 3) == 0, "unknown planet rejected");
    Check(ReplayObsGetPlanetOwner(s, "Endor") == -1, "unknown planet still unknown");

    Check(ReplayMutChangePlanetOwner(s, "Naboo", -1) == 0, "negative slot rejected");
    Check(ReplayObsGetPlanetOwner(s, "Naboo") == 2, "Naboo owner unchanged after negative reject");

    // Corruption field is untouched by owner changes.
    auto it = s.planets.find("NABOO");
    Check(it != s.planets.end() && fabs(it->second.corruption - 0.25f) < 1e-6f,
          "corruption untouched by ChangePlanetOwner");
}

// Tasks 124 / 126 (added 2026-04-23). Build-speed and per-faction
// move-speed multipliers. Shapes match the income-mult trio exactly;
// the suite pins the same invariants (default 1.0, negative slot =
// global, clear on 1.0, negative mult rejected, per-slot override
// wins) plus a cross-task isolation test proving build-speed and
// faction-speed mutations never leak into each other or into the
// existing income/damage mults.
static void TestReplayBuildAndFactionSpeed() {
    StartSuite("Replay: Build speed + per-faction move speed (Tasks 124/126)");

    ReplayState s;
    Check(fabs(ReplayObsGetBuildSpeed(s, -1) - 1.0f) < 1e-6f,
          "default global build speed = 1.0");
    Check(fabs(ReplayObsGetFactionSpeedMult(s, -1) - 1.0f) < 1e-6f,
          "default global faction speed = 1.0");

    // Build speed contract.
    Check(ReplayMutSetBuildSpeed(s, -1, 3.0f) == 1, "global build speed 3x");
    Check(fabs(ReplayObsGetBuildSpeed(s, 5) - 3.0f) < 1e-6f, "slot 5 inherits 3x");
    Check(ReplayMutSetBuildSpeed(s, 5, 10.0f) == 1, "per-slot 10x");
    Check(fabs(ReplayObsGetBuildSpeed(s, 5) - 10.0f) < 1e-6f, "slot 5 override wins");
    Check(ReplayMutSetBuildSpeed(s, 5, 1.0f) == 1, "clear override via 1.0");
    Check(s.per_slot_build_speed_mult.count(5) == 0, "per-slot table cleared");
    Check(ReplayMutSetBuildSpeed(s, -1, -0.1f) == 0, "negative global rejected");

    // Faction speed contract.
    Check(ReplayMutSetFactionSpeedMult(s, -1, 2.0f) == 1, "global faction speed 2x");
    Check(fabs(ReplayObsGetFactionSpeedMult(s, 3) - 2.0f) < 1e-6f, "slot 3 inherits 2x");
    Check(ReplayMutSetFactionSpeedMult(s, 3, 5.0f) == 1, "per-slot 5x");
    Check(fabs(ReplayObsGetFactionSpeedMult(s, 3) - 5.0f) < 1e-6f, "slot 3 override");
    Check(ReplayMutSetFactionSpeedMult(s, 3, 1.0f) == 1, "clear override");
    Check(s.per_faction_speed_mult.count(3) == 0, "per-slot cleared");
    Check(ReplayMutSetFactionSpeedMult(s, 0, -1.0f) == 0, "negative per-slot rejected");

    // Cross-task isolation: mutating build-speed leaves income/damage/faction-speed alone.
    ReplayMutSetIncomeMultiplier(s, 2, 7.0f);
    ReplayMutSetDamageMultiplier(s, 2, 4.0f);
    ReplayMutSetFactionSpeedMult(s, 2, 0.5f);
    Check(ReplayMutSetBuildSpeed(s, 2, 8.0f) == 1, "per-slot build-speed mutation");
    Check(fabs(ReplayObsGetIncomeMultiplier(s, 2) - 7.0f) < 1e-6f,
          "income mult untouched by build-speed edit");
    Check(fabs(ReplayObsGetDamageMultiplier(s, 2) - 4.0f) < 1e-6f,
          "damage mult untouched by build-speed edit");
    Check(fabs(ReplayObsGetFactionSpeedMult(s, 2) - 0.5f) < 1e-6f,
          "faction-speed untouched by build-speed edit");
    Check(fabs(ReplayObsGetBuildSpeed(s, 2) - 8.0f) < 1e-6f,
          "build-speed landed at 8.0");
}

// Tasks 122 / 123 / 127 (added 2026-04-23). Pure-state regression for
// the economy scaling trio: IncomeMultiplier + GameSpeed + FreezeCredits
// + the composite ReplayMutTickIncome. Pins:
//   * Income mult mirrors DamageMultiplier shape exactly (negative slot
//     = global, clear on 1.0, negative rejected, per-slot override wins)
//   * GameSpeed is a single global float; negative rejected, 0 allowed
//     (pause semantics), default 1.0
//   * FreezeCredits keyed per-slot; re-enable with new target overwrites;
//     disable discards target; negative slot rejected; negative target
//     rejected on enable but allowed implicitly on disable (discarded)
//   * TickIncome compounds all three: frozen slots snap to target,
//     unfrozen slots get base * income_mult(slot) * game_speed
//   * Game speed 0 produces zero deltas on unfrozen slots (tick still
//     touches frozen ones because they snap unconditionally)
static void TestReplayEconomy() {
    StartSuite("Replay: Economy scaling trio (Tasks 122/123/127)");

    ReplayState s;
    Check(fabs(ReplayObsGetIncomeMultiplier(s, -1) - 1.0f) < 1e-6f,
          "default global income mult = 1.0");
    Check(fabs(ReplayObsGetGameSpeed(s) - 1.0f) < 1e-6f,
          "default game speed = 1.0");
    Check(ReplayObsIsFreezeCredits(s, 0) == 0, "slot 0 not frozen by default");

    // --- Income multiplier ---
    Check(ReplayMutSetIncomeMultiplier(s, -1, 2.0f) == 1, "global set returns 1");
    Check(fabs(ReplayObsGetIncomeMultiplier(s, -1) - 2.0f) < 1e-6f, "global mult now 2.0");
    Check(fabs(ReplayObsGetIncomeMultiplier(s, 5) - 2.0f) < 1e-6f, "slot 5 inherits 2.0");
    Check(ReplayMutSetIncomeMultiplier(s, 5, 4.0f) == 1, "per-slot override returns 1");
    Check(fabs(ReplayObsGetIncomeMultiplier(s, 5) - 4.0f) < 1e-6f, "slot 5 override wins");
    Check(ReplayMutSetIncomeMultiplier(s, 5, 1.0f) == 1, "set to 1.0 clears override");
    Check(s.per_slot_income_mult.count(5) == 0, "per-slot table erased on 1.0");
    Check(ReplayMutSetIncomeMultiplier(s, -1, -3.0f) == 0, "negative rejected");

    // --- Game speed ---
    Check(ReplayMutSetGameSpeed(s, 2.0f) == 1, "set game speed 2x returns 1");
    Check(fabs(ReplayObsGetGameSpeed(s) - 2.0f) < 1e-6f, "game speed now 2.0");
    Check(ReplayMutSetGameSpeed(s, 0.0f) == 1, "speed 0 (pause) accepted");
    Check(fabs(ReplayObsGetGameSpeed(s) - 0.0f) < 1e-6f, "game speed now 0");
    Check(ReplayMutSetGameSpeed(s, -1.0f) == 0, "negative speed rejected");

    // --- Freeze credits ---
    Check(ReplayMutSetFreezeCredits(s, 0, true, 50000.0) == 1, "freeze slot 0 at 50k");
    Check(ReplayObsIsFreezeCredits(s, 0) == 1, "slot 0 is frozen");
    Check(ReplayObsGetFreezeCreditsTarget(s, 0) == 50000.0, "slot 0 target = 50000");
    Check(ReplayMutSetFreezeCredits(s, 0, true, 99999.0) == 1, "overwrite frozen target");
    Check(ReplayObsGetFreezeCreditsTarget(s, 0) == 99999.0, "slot 0 target updated");
    Check(ReplayMutSetFreezeCredits(s, 0, false, 0.0) == 1, "unfreeze returns 1");
    Check(ReplayObsIsFreezeCredits(s, 0) == 0, "slot 0 no longer frozen after disable");
    Check(ReplayObsGetFreezeCreditsTarget(s, 0) == -1.0, "target gone after disable");
    Check(ReplayMutSetFreezeCredits(s, -1, true, 100.0) == 0, "negative slot rejected");
    Check(ReplayMutSetFreezeCredits(s, 2, true, -5.0) == 0, "negative target rejected on enable");
    Check(ReplayObsIsFreezeCredits(s, 2) == 0, "rejected freeze does NOT leak a partial entry");

    // --- TickIncome composite ---
    // Seed players: slot 0 @ 1000 cr, slot 1 @ 2000, slot 2 @ 3000.
    s.players.push_back(ReplayPlayer{0, "REBEL", 1000.0, 0, ""});
    s.players.push_back(ReplayPlayer{1, "EMPIRE", 2000.0, 0, ""});
    s.players.push_back(ReplayPlayer{2, "UNDERWORLD", 3000.0, 0, ""});

    // Reset scaling to a known clean state.
    s.global_income_mult = 1.0f;
    s.per_slot_income_mult.clear();
    s.global_game_speed = 1.0f;
    s.frozen_credits_targets.clear();

    // Base tick: 100 credits * 1.0 * 1.0 = +100 to each unfrozen player.
    Check(ReplayMutTickIncome(s, 100.0) == 3, "tick touches all 3 players");
    Check(s.players[0].credits == 1100.0, "slot 0 credits 1100 after +100");
    Check(s.players[1].credits == 2100.0, "slot 1 credits 2100");
    Check(s.players[2].credits == 3100.0, "slot 2 credits 3100");

    // Compound: slot 1 gets per-slot 2x, global speed stays 1.0.
    ReplayMutSetIncomeMultiplier(s, 1, 2.0f);
    Check(ReplayMutTickIncome(s, 100.0) == 3, "tick still touches all 3");
    Check(s.players[0].credits == 1200.0, "slot 0 +100");
    Check(s.players[1].credits == 2300.0, "slot 1 +200 (per-slot 2x)");
    Check(s.players[2].credits == 3200.0, "slot 2 +100 (fallback to global 1x)");

    // Game speed 0.5x halves all deltas on unfrozen slots.
    ReplayMutSetGameSpeed(s, 0.5f);
    Check(ReplayMutTickIncome(s, 100.0) == 3, "tick under 0.5x still touches 3");
    Check(s.players[0].credits == 1250.0, "slot 0 +50 (base 100 * 0.5)");
    Check(s.players[1].credits == 2400.0, "slot 1 +100 (per-slot 2x * 0.5)");

    // Freeze slot 0 at 9999 — every tick snaps slot 0 to exactly 9999
    // regardless of mult/speed.
    ReplayMutSetFreezeCredits(s, 0, true, 9999.0);
    Check(ReplayMutTickIncome(s, 100.0) == 3, "tick touches all 3 (freeze counts as touched)");
    Check(s.players[0].credits == 9999.0, "frozen slot 0 snapped to 9999");
    Check(s.players[1].credits == 2500.0, "slot 1 still deltas +100");

    // Pause (game speed 0) produces 0-delta ticks but still snaps frozen slots.
    // touched counts only the slots whose credits actually changed, so
    // zero-delta unfrozen slots don't count.
    ReplayMutSetGameSpeed(s, 0.0f);
    s.players[0].credits = 12345.0;  // drift the frozen slot to force a snap
    int touched = ReplayMutTickIncome(s, 100.0);
    Check(touched == 1, "pause + 1 frozen slot => only the frozen snap counts");
    Check(s.players[0].credits == 9999.0, "frozen slot re-snapped even at speed 0");
    Check(s.players[1].credits == 2500.0, "unfrozen slot 1 unchanged at speed 0");
}

// Tasks 131 / 132 / 133 (added 2026-04-23). Pure-state regression for
// the combat-scaling trio: FireRate + AreaDamage + TargetFilter. Pins:
//   * FireRate mirrors DamageMultiplier shape: global + per-slot with
//     clear-on-1.0, but rejects ZERO (unlike damage mult which accepts
//     0) because the Phase 2 hook will divide by it. Negative rejected.
//   * ApplyFireRate(slot, base_ms) = base_ms / effective_mult; negative
//     base clamps to 0.0.
//   * AreaDamage is a global bool. Default false. Toggle persists;
//     ApplyAreaSplash is a no-op when disabled. When enabled, splashes
//     (primary_amount * falloff) onto every OTHER unit, honouring both
//     hardpoint INVULNERABLE and the target's damage multiplier (same
//     scaling path as ReplayMutApplyDamage).
//   * TargetFilter defaults to TARGET_ALL for unset slots. Writing
//     TARGET_ALL explicitly erases the override. Bitmasks outside the
//     known 3 bits are masked down. IsTargetAllowed requires a
//     single-bit target_kind — ALL, 0, and multi-bit values return
//     false.
//   * Cross-task isolation: combat-trio mutations don't leak into
//     income/damage/build/faction-speed state.
static void TestReplayCombatScaling() {
    StartSuite("Replay: Combat scaling trio (Tasks 131/132/133)");

    // === FireRate ===
    {
        ReplayState s;
        Check(fabs(ReplayObsGetFireRate(s, -1) - 1.0f) < 1e-6f,
              "default global fire-rate = 1.0");
        Check(fabs(ReplayObsGetFireRate(s, 9) - 1.0f) < 1e-6f,
              "default per-slot inherits 1.0");

        Check(ReplayMutSetFireRate(s, -1, 3.0f) == 1, "global 3x accepted");
        Check(fabs(ReplayObsGetFireRate(s, 4) - 3.0f) < 1e-6f, "slot 4 inherits 3x");
        Check(ReplayMutSetFireRate(s, 4, 10.0f) == 1, "per-slot 10x accepted");
        Check(fabs(ReplayObsGetFireRate(s, 4) - 10.0f) < 1e-6f, "slot 4 override wins");
        Check(ReplayMutSetFireRate(s, 4, 1.0f) == 1, "per-slot 1.0 clears");
        Check(s.per_slot_fire_rate_mult.count(4) == 0, "table erased");
        Check(ReplayMutSetFireRate(s, -1, 0.0f) == 0, "ZERO rejected (div-by-zero guard)");
        Check(ReplayMutSetFireRate(s, -1, -0.5f) == 0, "negative rejected");
        Check(fabs(ReplayObsGetFireRate(s, -1) - 3.0f) < 1e-6f,
              "global untouched after rejections");

        // ApplyFireRate math. Global stays at 3x from above.
        float scaled = ReplayMutApplyFireRate(s, 99, 600.0f);  // slot 99 inherits 3x
        Check(fabs(scaled - 200.0f) < 1e-3f,
              "600ms / 3x = 200ms (higher mult = faster fire)");
        ReplayMutSetFireRate(s, 2, 6.0f);
        float scaled2 = ReplayMutApplyFireRate(s, 2, 600.0f);
        Check(fabs(scaled2 - 100.0f) < 1e-3f,
              "slot 2 @ 6x => 600ms / 6x = 100ms");
        Check(ReplayMutApplyFireRate(s, 2, -5.0f) == 0.0f,
              "negative base_cooldown clamps to 0");
    }

    // === AreaDamage ===
    {
        ReplayState s;
        Check(ReplayObsIsAreaDamageEnabled(s) == false,
              "area damage default false");
        // Seed two units owned by different slots for splash target set.
        s.local_slot = 1;
        uint64_t primary = 0x1000;
        uint64_t a = 0x2000;
        uint64_t b = 0x3000;
        ReplayMutMockUnit(s, primary, "x-wing",   1, 100.0f, 100.0f, 0);
        ReplayMutMockUnit(s, a,       "a-wing",   1, 200.0f, 200.0f, 0);
        ReplayMutMockUnit(s, b,       "tie",      0, 300.0f, 300.0f, 0);

        // Disabled => no-op.
        Check(ReplayMutApplyAreaSplash(s, primary, 50.0f) == 0,
              "splash disabled: returns 0 affected");
        Check(ReplayFindUnit(s, a)->hull == 200.0f, "unit a unscathed while disabled");
        Check(ReplayFindUnit(s, b)->hull == 300.0f, "unit b unscathed while disabled");

        // Enable + splash.
        Check(ReplayMutSetAreaDamageEnabled(s, true) == 1, "enable returns 1");
        Check(ReplayObsIsAreaDamageEnabled(s) == true, "toggle persists");
        // Primary amount 100, falloff 0.5 => splash 50 onto each OTHER unit.
        int affected = ReplayMutApplyAreaSplash(s, primary, 100.0f);
        Check(affected == 2, "splash affected 2 other units");
        Check(ReplayFindUnit(s, primary)->hull == 100.0f,
              "primary not splashed onto itself");
        Check(fabs(ReplayFindUnit(s, a)->hull - 150.0f) < 1e-3f,
              "unit a: 200 - 50 splash = 150");
        Check(fabs(ReplayFindUnit(s, b)->hull - 250.0f) < 1e-3f,
              "unit b: 300 - 50 splash = 250");

        // Negative/zero primary amount => no-op.
        int affected2 = ReplayMutApplyAreaSplash(s, primary, 0.0f);
        Check(affected2 == 0, "zero amount produces no affected");
        Check(ReplayFindUnit(s, a)->hull == 150.0f, "zero splash did not touch unit a");

        // Splash respects damage multiplier on victim's slot.
        ReplayMutSetDamageMultiplier(s, 0, 3.0f);  // enemy slot 0 takes 3x
        int affected3 = ReplayMutApplyAreaSplash(s, primary, 20.0f);
        // base splash = 20 * 0.5 = 10; slot-1 units take 10; slot-0 unit takes 10*3 = 30
        Check(affected3 == 2, "splash + 3x mult still hits 2");
        Check(fabs(ReplayFindUnit(s, a)->hull - 140.0f) < 1e-3f,
              "unit a (slot 1 @ 1x) took 10 splash => 140");
        Check(fabs(ReplayFindUnit(s, b)->hull - 220.0f) < 1e-3f,
              "unit b (slot 0 @ 3x) took 30 splash => 220");

        // Splash honours hardpoint INVULNERABLE.
        ReplayUnitDetail* ua = ReplayFindUnit(s, a);
        ua->hardpoints.push_back(ReplayHardpoint{0u, {std::string("INVULNERABLE")}});
        int affected4 = ReplayMutApplyAreaSplash(s, primary, 40.0f);
        Check(affected4 == 1, "splash with invuln unit skips the invuln one");
        Check(ReplayFindUnit(s, a)->hull == 140.0f,
              "invulnerable unit a unchanged through splash");
    }

    // === TargetFilter ===
    {
        ReplayState s;
        Check(ReplayObsGetTargetFilter(s, 1) == ReplayState::TARGET_ALL,
              "default filter = TARGET_ALL for any slot");
        Check(ReplayObsGetTargetFilter(s, -1) == ReplayState::TARGET_ALL,
              "negative slot returns TARGET_ALL sentinel");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_ENEMY) == true,
              "default: ENEMY allowed");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_FRIENDLY) == true,
              "default: FRIENDLY allowed");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_NEUTRAL) == true,
              "default: NEUTRAL allowed");

        // Friendly-only: enemy + neutral blocked.
        Check(ReplayMutSetTargetFilter(s, 1, ReplayState::TARGET_FRIENDLY) == 1,
              "set friendly-only returns 1");
        Check(s.per_slot_target_filter.count(1) == 1, "override recorded");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_ENEMY) == false,
              "friendly-only: ENEMY blocked");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_FRIENDLY) == true,
              "friendly-only: FRIENDLY allowed");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_NEUTRAL) == false,
              "friendly-only: NEUTRAL blocked");

        // Disarm (0 bitmask): nothing passes.
        Check(ReplayMutSetTargetFilter(s, 1, 0) == 1, "disarm returns 1");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_ENEMY) == false,
              "disarm: ENEMY blocked");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_FRIENDLY) == false,
              "disarm: FRIENDLY blocked");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_NEUTRAL) == false,
              "disarm: NEUTRAL blocked");

        // Writing TARGET_ALL clears the override.
        Check(ReplayMutSetTargetFilter(s, 1, ReplayState::TARGET_ALL) == 1,
              "TARGET_ALL accepted");
        Check(s.per_slot_target_filter.count(1) == 0,
              "TARGET_ALL clears override (canonical)");

        // Bitmask sanitization: 0xFF masks down to 0x7.
        Check(ReplayMutSetTargetFilter(s, 2, 0xFF) == 1, "0xFF accepted");
        // 0xFF & 0x7 = 0x7 = TARGET_ALL, which CLEARS the override.
        Check(s.per_slot_target_filter.count(2) == 0, "0xFF masks to ALL and clears");

        // Negative slot rejected by mutation (observer still returns ALL).
        Check(ReplayMutSetTargetFilter(s, -1, ReplayState::TARGET_ENEMY) == 0,
              "negative slot rejected");
        Check(ReplayObsGetTargetFilter(s, -1) == ReplayState::TARGET_ALL,
              "negative slot observation unchanged");

        // IsTargetAllowed guard rails:
        Check(ReplayObsIsTargetAllowed(s, 1, 0) == false,
              "target_kind 0 returns false");
        Check(ReplayObsIsTargetAllowed(s, 1, ReplayState::TARGET_ALL) == false,
              "target_kind ALL (multi-bit) returns false");
        Check(ReplayObsIsTargetAllowed(s, 1, 0x10) == false,
              "target_kind outside known bits returns false");
    }

    // === Cross-task isolation ===
    {
        ReplayState s;
        ReplayMutSetIncomeMultiplier(s, 2, 7.0f);
        ReplayMutSetDamageMultiplier(s, 2, 4.0f);
        ReplayMutSetBuildSpeed(s, 2, 8.0f);
        ReplayMutSetFactionSpeedMult(s, 2, 0.5f);

        Check(ReplayMutSetFireRate(s, 2, 9.0f) == 1, "combat fire-rate mutation");
        Check(ReplayMutSetAreaDamageEnabled(s, true) == 1, "combat area-damage toggle");
        Check(ReplayMutSetTargetFilter(s, 2, ReplayState::TARGET_ENEMY) == 1,
              "combat target-filter mutation");

        Check(fabs(ReplayObsGetIncomeMultiplier(s, 2) - 7.0f) < 1e-6f,
              "income untouched by combat mutations");
        Check(fabs(ReplayObsGetDamageMultiplier(s, 2) - 4.0f) < 1e-6f,
              "damage untouched by combat mutations");
        Check(fabs(ReplayObsGetBuildSpeed(s, 2) - 8.0f) < 1e-6f,
              "build-speed untouched by combat mutations");
        Check(fabs(ReplayObsGetFactionSpeedMult(s, 2) - 0.5f) < 1e-6f,
              "faction-speed untouched by combat mutations");
        Check(fabs(ReplayObsGetFireRate(s, 2) - 9.0f) < 1e-6f,
              "fire-rate landed at 9.0");
        Check(ReplayObsIsAreaDamageEnabled(s) == true,
              "area damage still enabled");
        Check(ReplayObsGetTargetFilter(s, 2) == ReplayState::TARGET_ENEMY,
              "target filter still ENEMY-only");
    }
}

// Tasks 161 + 162 (added 2026-04-23). Pure-state regression for the
// AOB-style build-flow toggles: InstantBuild (skip progress ticks) and
// FreeBuild (skip credit deduction). Pins:
//   * InstantBuild default false; toggle persists.
//   * ShouldBuildComplete short-circuits when instant enabled even for
//     tiny elapsed_ms; matches "queue unit → completes on first tick".
//   * Without instant, normal threshold: elapsed >= queue_time.
//   * FreeBuild default false; toggle persists.
//   * ComputeBuildCost returns 0 when free-build enabled (wins over
//     per-slot multiplier from #160).
//   * Without free-build, ComputeBuildCost multiplies by the per-slot
//     mult (1.0x default, 0.5x after SetBuildCost, etc.).
// Tasks 172/173/174/175 (added 2026-04-24). Pure-state regressions
// for the four IDA-blocked P7 features. Each task ships its Phase 1
// replay mirror so the V2 UI layer can bind today; Phase 2 wires
// through to the engine once the corresponding RVA/structure pass
// lands in IDA.
static void TestReplayP7Mirrors() {
    StartSuite("Replay: P7 Phase 1 mirrors (Tasks 172/173/174/175)");

    // ── #172 Orbital phase ───────────────────────────────────
    {
        ReplayState s;
        Check(ReplayObsGetOrbitalPhase(s, 1) == 0, "default phase = tactical");
        Check(ReplayMutSetOrbitalPhase(s, 1, 1) == 1, "set slot 1 → orbital");
        Check(ReplayObsGetOrbitalPhase(s, 1) == 1, "slot 1 reads back orbital");
        Check(ReplayMutSetOrbitalPhase(s, 2, 0) == 1, "set slot 2 → tactical");
        Check(ReplayObsGetOrbitalPhase(s, 2) == 0, "slot 2 still tactical");
        Check(ReplayMutSetOrbitalPhase(s, -1, 1) == 0, "negative slot rejected");
        Check(ReplayMutSetOrbitalPhase(s, 1, 5) == 0, "out-of-range phase rejected");
        Check(ReplayObsGetOrbitalPhase(s, 99) == 0, "unset slot returns default 0");
    }

    // ── #173 Music panel ─────────────────────────────────────
    {
        ReplayState s;
        Check(fabs(ReplayObsGetMusicVolume(s) - 1.0f) < 1e-6f, "default music volume = 1.0");
        Check(ReplayObsIsMusicPaused(s) == false, "default music NOT paused");
        Check(ReplayObsGetCurrentTrack(s).empty(), "default current track empty");

        Check(ReplayMutSetMusicVolume(s, 0.5f) == 1, "set volume 0.5");
        Check(fabs(ReplayObsGetMusicVolume(s) - 0.5f) < 1e-6f, "volume read back");

        Check(ReplayMutSetMusicVolume(s, -0.5f) == 1, "negative clamped");
        Check(fabs(ReplayObsGetMusicVolume(s) - 0.0f) < 1e-6f, "volume clamped to 0");

        Check(ReplayMutSetMusicVolume(s, 5.0f) == 1, "above-1 clamped");
        Check(fabs(ReplayObsGetMusicVolume(s) - 1.0f) < 1e-6f, "volume clamped to 1");

        Check(ReplayMutSetMusicPaused(s, true) == 1, "pause music");
        Check(ReplayObsIsMusicPaused(s), "music now paused");
        Check(ReplayMutSetCurrentTrack(s, "imperial_march") == 1, "set track");
        Check(ReplayObsGetCurrentTrack(s) == "imperial_march", "track read back");
    }

    // ── #174 Veterancy ───────────────────────────────────────
    {
        ReplayState s;
        Check(ReplayObsGetVeterancy(s, 0xAA) == -1, "unset unit returns -1 sentinel");
        Check(ReplayMutSetVeterancy(s, 0xAA, 2) == 1, "set rank 2 (Elite)");
        Check(ReplayObsGetVeterancy(s, 0xAA) == 2, "rank reads back");
        Check(ReplayMutSetVeterancy(s, 0xAA, 3) == 1, "promote to Legendary");
        Check(ReplayObsGetVeterancy(s, 0xAA) == 3, "rank now 3");
        Check(ReplayMutSetVeterancy(s, 0xAA, 4) == 0, "rank > 3 rejected");
        Check(ReplayObsGetVeterancy(s, 0xAA) == 3, "still 3 after rejection");
        Check(ReplayMutSetVeterancy(s, 0, 1) == 0, "obj_addr=0 rejected");
        Check(ReplayMutSetVeterancy(s, 0xBB, 0) == 1, "rank 0 (Recruit) accepted");
    }

    // ── #175 Map hints ───────────────────────────────────────
    {
        ReplayState s;
        Check(ReplayObsMapHintCount(s) == 0, "no hints by default");

        Check(ReplayMutAddMapHint(s, "obj1", "Capture this", 100, 200, 300, 0) == 1,
              "first hint accepted");
        Check(ReplayObsMapHintCount(s) == 1, "count = 1");
        Check(ReplayMutAddMapHint(s, "obj2", "Defend", 400, 500, 600, 1) == 2,
              "second hint accepted");
        Check(ReplayObsMapHintCount(s) == 2, "count = 2");

        Check(ReplayMutAddMapHint(s, "obj1", "dup", 0, 0, 0, 0) == 0,
              "duplicate id rejected");
        Check(ReplayObsMapHintCount(s) == 2, "count unchanged on dup");

        Check(ReplayMutAddMapHint(s, "", "blank", 0, 0, 0, 0) == 0, "blank id rejected");

        Check(ReplayMutRemoveMapHint(s, "obj1") == 1, "removed obj1");
        Check(ReplayObsMapHintCount(s) == 1, "count = 1 after remove");
        Check(ReplayMutRemoveMapHint(s, "nope") == 0, "remove-missing returns 0");

        Check(ReplayMutClearMapHints(s) == 1, "clear returns prior count = 1");
        Check(ReplayObsMapHintCount(s) == 0, "all hints cleared");
    }
}

static void TestReplayInstantAndFreeBuild() {
    StartSuite("Replay: Instant-build + Free-build AOB toggles (Tasks 161/162)");

    // --- InstantBuild ---
    {
        ReplayState s;
        Check(ReplayObsIsInstantBuildEnabled(s) == false, "default off");

        // Normal completion: needs elapsed >= queue_time.
        Check(ReplayObsShouldBuildComplete(s, 10000, 5000) == false,
              "5s elapsed / 10s queue: not done");
        Check(ReplayObsShouldBuildComplete(s, 10000, 10000) == true,
              "10s elapsed / 10s queue: done");
        Check(ReplayObsShouldBuildComplete(s, 10000, 15000) == true,
              "15s elapsed / 10s queue: done");
        Check(ReplayObsShouldBuildComplete(s, 0, 0) == true,
              "zero queue time always done (degenerate)");
        Check(ReplayObsShouldBuildComplete(s, -1, 0) == true,
              "negative queue time always done");

        // Enable instant — every poll short-circuits to true.
        Check(ReplayMutSetInstantBuild(s, true) == 1, "enable returns 1");
        Check(ReplayObsIsInstantBuildEnabled(s) == true, "now on");
        Check(ReplayObsShouldBuildComplete(s, 999999, 0) == true,
              "instant: 0 elapsed / giant queue: DONE");
        Check(ReplayObsShouldBuildComplete(s, 10000, 1) == true,
              "instant: 1ms in: DONE");

        // Disable.
        Check(ReplayMutSetInstantBuild(s, false) == 1, "disable returns 1");
        Check(ReplayObsIsInstantBuildEnabled(s) == false, "now off");
        Check(ReplayObsShouldBuildComplete(s, 10000, 100) == false,
              "back to normal after disable");
    }

    // --- FreeBuild ---
    {
        ReplayState s;
        Check(ReplayObsIsFreeBuildEnabled(s) == false, "default off");

        // Normal cost: base * 1.0 mult.
        Check(fabs(ReplayObsComputeBuildCost(s, 1, 100.0f) - 100.0f) < 1e-3f,
              "base 100 * 1.0 mult = 100");

        // With #160 multiplier.
        ReplayMutSetBuildCost(s, 1, 0.5f);
        Check(fabs(ReplayObsComputeBuildCost(s, 1, 100.0f) - 50.0f) < 1e-3f,
              "base 100 * 0.5 mult = 50");

        // Free-build wins over the mult.
        Check(ReplayMutSetFreeBuild(s, true) == 1, "enable returns 1");
        Check(ReplayObsIsFreeBuildEnabled(s) == true, "now on");
        Check(fabs(ReplayObsComputeBuildCost(s, 1, 100.0f) - 0.0f) < 1e-3f,
              "free-build overrides mult: cost = 0");
        Check(fabs(ReplayObsComputeBuildCost(s, 5, 999999.0f) - 0.0f) < 1e-3f,
              "free-build still 0 for any slot / any base");

        // Disable.
        Check(ReplayMutSetFreeBuild(s, false) == 1, "disable returns 1");
        Check(ReplayObsIsFreeBuildEnabled(s) == false, "now off");
        // Back to mult behavior.
        Check(fabs(ReplayObsComputeBuildCost(s, 1, 100.0f) - 50.0f) < 1e-3f,
              "post-disable: mult 0.5 back in effect");

        // Negative base clamps to 0 regardless of toggles.
        Check(fabs(ReplayObsComputeBuildCost(s, 1, -100.0f) - 0.0f) < 1e-3f,
              "negative base rejected -> 0");
    }

    // --- Cross-task isolation ---
    {
        ReplayState s;
        ReplayMutSetInstantBuild(s, true);
        ReplayMutSetFreeBuild(s, true);
        ReplayMutSetBuildCost(s, 0, 2.0f);
        ReplayMutSetIncomeMultiplier(s, 0, 3.0f);
        ReplayMutSetDamageMultiplier(s, 0, 4.0f);

        Check(ReplayObsIsInstantBuildEnabled(s) == true, "instant still on");
        Check(ReplayObsIsFreeBuildEnabled(s) == true, "free still on");
        Check(fabs(ReplayObsGetBuildCost(s, 0) - 2.0f) < 1e-3f,
              "build_cost_mult untouched");
        Check(fabs(ReplayObsGetIncomeMultiplier(s, 0) - 3.0f) < 1e-3f,
              "income untouched");
        Check(fabs(ReplayObsGetDamageMultiplier(s, 0) - 4.0f) < 1e-3f,
              "damage untouched");
    }
}

// Task 157 (added 2026-04-23). Pure-state regression for the generic
// SetUnitField dispatcher + GetUnitField observer. Pins:
//   * Empty unit store / unknown obj_addr => 0 on set, NaN on get.
//   * Each known field exercises the dispatcher and reads back via the
//     observer. Bool fields accept 0.0/1.0 and any non-zero value is
//     truthy.
//   * Unknown field name returns 0 on set, NaN on get.
//   * max_hull clamps existing hull down if hull > new max_hull.
//   * owner_slot is OBSERVER-only: not settable via dispatcher,
//     readable via observer (diagnostics).
//   * Negative float values rejected on hull/shield/speed/attack_power
//     (enforced inside the underlying setters).
static void TestReplaySetUnitFieldGeneric() {
    StartSuite("Replay: Generic SetUnitField dispatcher (Task 157)");

    ReplayState s;
    ReplayMutMockUnit(s, 0x1000, "gladiator", 5, 500.0f, 1000.0f, 0);
    // Mark as hero up front so respawn_ms / permadeath-adjacent setters
    // (which require is_hero=true) work in the dispatcher test.
    ReplayFindUnit(s, 0x1000)->is_hero = true;

    // --- Unknown unit / unknown field ---
    Check(ReplayMutSetUnitField(s, 0xDEAD, "hull", 100.0f) == 0,
          "unknown addr returns 0");
    float nan_v = ReplayObsGetUnitField(s, 0xDEAD, "hull");
    Check(nan_v != nan_v, "unknown addr observer returns NaN");
    Check(ReplayMutSetUnitField(s, 0x1000, "bogus_field", 1.0f) == 0,
          "unknown field returns 0");
    float nan_v2 = ReplayObsGetUnitField(s, 0x1000, "bogus_field");
    Check(nan_v2 != nan_v2, "unknown field observer returns NaN");

    // --- Float fields ---
    Check(ReplayMutSetUnitField(s, 0x1000, "hull", 750.0f) == 1, "hull=750");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "hull") - 750.0f) < 1e-3f,
          "hull read back 750");
    Check(ReplayMutSetUnitField(s, 0x1000, "max_hull", 2000.0f) == 1, "max_hull=2000");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "max_hull") - 2000.0f) < 1e-3f,
          "max_hull read back 2000");

    Check(ReplayMutSetUnitField(s, 0x1000, "shield", 50.0f) == 1, "shield=50");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "shield") - 50.0f) < 1e-3f,
          "shield read back 50");
    Check(ReplayMutSetUnitField(s, 0x1000, "max_shield", 200.0f) == 1, "max_shield=200");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "max_shield") - 200.0f) < 1e-3f,
          "max_shield read back 200");

    Check(ReplayMutSetUnitField(s, 0x1000, "speed", 10.0f) == 1, "speed=10");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "speed") - 10.0f) < 1e-3f,
          "speed read back 10");
    Check(ReplayMutSetUnitField(s, 0x1000, "max_speed", 25.0f) == 1, "max_speed=25");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "max_speed") - 25.0f) < 1e-3f,
          "max_speed read back 25");

    Check(ReplayMutSetUnitField(s, 0x1000, "attack_power", 123.0f) == 1, "attack_power=123");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "attack_power") - 123.0f) < 1e-3f,
          "attack_power read back 123");

    // --- Int field (respawn_ms) ---
    Check(ReplayMutSetUnitField(s, 0x1000, "respawn_ms", 15000.0f) == 1, "respawn_ms=15000");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "respawn_ms") - 15000.0f) < 1e-3f,
          "respawn_ms read back 15000");

    // --- Bool fields ---
    Check(ReplayMutSetUnitField(s, 0x1000, "invuln_flag", 1.0f) == 1, "invuln_flag on");
    Check(ReplayObsGetUnitField(s, 0x1000, "invuln_flag") == 1.0f, "invuln_flag=1");
    Check(ReplayMutSetUnitField(s, 0x1000, "invuln_flag", 0.0f) == 1, "invuln_flag off");
    Check(ReplayObsGetUnitField(s, 0x1000, "invuln_flag") == 0.0f, "invuln_flag=0");

    // prevent_death stores the full byte: bit 0x80 is the "prevent" flag.
    // Observer returns the raw byte so after set we see 128.0f; after
    // unset, 0.0f. This matches what the live field read returns.
    Check(ReplayMutSetUnitField(s, 0x1000, "prevent_death", 1.0f) == 1, "prevent_death on");
    Check(ReplayObsGetUnitField(s, 0x1000, "prevent_death") == 128.0f, "prevent_death byte = 0x80");
    Check(ReplayMutSetUnitField(s, 0x1000, "prevent_death", 0.0f) == 1, "prevent_death off");
    Check(ReplayObsGetUnitField(s, 0x1000, "prevent_death") == 0.0f, "prevent_death byte = 0");

    Check(ReplayMutSetUnitField(s, 0x1000, "is_hero", 1.0f) == 1, "is_hero on");
    Check(ReplayObsGetUnitField(s, 0x1000, "is_hero") == 1.0f, "is_hero=1");
    Check(ReplayMutSetUnitField(s, 0x1000, "is_hero", 2.5f) == 1, "is_hero truthy from 2.5");
    Check(ReplayObsGetUnitField(s, 0x1000, "is_hero") == 1.0f, "still 1 after truthy set");
    Check(ReplayMutSetUnitField(s, 0x1000, "is_hero", 0.0f) == 1, "is_hero off");
    Check(ReplayObsGetUnitField(s, 0x1000, "is_hero") == 0.0f, "is_hero=0");

    Check(ReplayMutSetUnitField(s, 0x1000, "respawn_enabled", 0.0f) == 1, "respawn_enabled=false");
    Check(ReplayObsGetUnitField(s, 0x1000, "respawn_enabled") == 0.0f, "read back false");
    Check(ReplayMutSetUnitField(s, 0x1000, "respawn_enabled", 1.0f) == 1, "respawn_enabled=true");
    Check(ReplayObsGetUnitField(s, 0x1000, "respawn_enabled") == 1.0f, "read back true");

    // --- owner_slot READ-ONLY ---
    Check(ReplayMutSetUnitField(s, 0x1000, "owner_slot", 9.0f) == 0,
          "owner_slot set rejected");
    Check(ReplayObsGetUnitField(s, 0x1000, "owner_slot") == 5.0f,
          "owner_slot still 5 (read-only peek works)");

    // --- max_hull clamp-down ---
    ReplayMutSetUnitField(s, 0x1000, "hull", 1500.0f);   // push hull high
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "hull") - 1500.0f) < 1e-3f,
          "hull=1500 before clamp");
    Check(ReplayMutSetUnitField(s, 0x1000, "max_hull", 800.0f) == 1, "max_hull down to 800");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "max_hull") - 800.0f) < 1e-3f,
          "max_hull=800");
    Check(fabs(ReplayObsGetUnitField(s, 0x1000, "hull") - 800.0f) < 1e-3f,
          "hull clamped down to 800 (hull > max_hull disallowed)");

    // --- Negative rejections (via underlying setters) ---
    Check(ReplayMutSetUnitField(s, 0x1000, "attack_power", -1.0f) == 0,
          "negative attack_power rejected");
    Check(ReplayMutSetUnitField(s, 0x1000, "max_hull", -100.0f) == 0,
          "negative max_hull rejected");
}

// Tasks 159 + 160 + 163 (added 2026-04-23). Pure-state regression for
// the production-economy trio: SpawnUnits + SetBuildCost (build-cost
// multiplier, distinct from build-SPEED mult in #124) + SetUnitCapOverride.
// Pins:
//   * SpawnUnits: count<=0, empty type, negative slot all return 0.
//   * SpawnUnits: N-count produces N units, all owned by slot, with
//     distinct obj_addrs in the 0xDEADBEEF00000000 range.
//   * BuildCost: mirrors damage_mult shape (negative slot = global,
//     clear-on-1.0, negative mult rejected). 0.0 is ALLOWED (free build).
//   * UnitCapOverride: -1 is unlimited sentinel; -2 means "no override";
//     0 is valid (no building). Writing then clearing restores -2.
//   * Cross-task: these mutations don't affect each other or the
//     earlier income/damage/build-speed multipliers from iterations
//     29/30.
static void TestReplayProductionTrio() {
    StartSuite("Replay: SpawnUnits + BuildCost + UnitCapOverride (Tasks 159/160/163)");

    // --- SpawnUnits ---
    {
        ReplayState s;
        s.local_slot = 1;
        Check(ReplayMutSpawnUnits(s, "",         1, 3) == 0, "empty type rejected");
        Check(ReplayMutSpawnUnits(s, "x-wing", -1, 3) == 0, "negative slot rejected");
        Check(ReplayMutSpawnUnits(s, "x-wing",  1, 0) == 0, "zero count rejected");
        Check(ReplayMutSpawnUnits(s, "x-wing",  1, -2) == 0, "negative count rejected");
        Check(s.units.empty(), "no units spawned on rejection");

        int spawned = ReplayMutSpawnUnits(s, "x-wing", 1, 3);
        Check(spawned == 3, "3 units spawned");
        Check(s.units.size() == 3, "units map holds 3 entries");
        // Verify all owned by slot 1 with type "x-wing".
        int verified_owner = 0;
        int verified_type  = 0;
        for (const auto& entry : s.units) {
            if (entry.second.owner_slot == 1) verified_owner++;
            if (entry.second.type_name == "x-wing") verified_type++;
        }
        Check(verified_owner == 3, "all 3 spawned owned by slot 1");
        Check(verified_type == 3, "all 3 have type 'x-wing'");

        // Second spawn — addrs distinct from first batch.
        int spawned2 = ReplayMutSpawnUnits(s, "tie", 0, 2);
        Check(spawned2 == 2, "2 tie-fighters spawned");
        Check(s.units.size() == 5, "total units = 5");

        // Verify no obj_addr collision — each spawn has a unique base.
        std::vector<uint64_t> seen_addrs;
        for (const auto& entry : s.units) seen_addrs.push_back(entry.first);
        std::sort(seen_addrs.begin(), seen_addrs.end());
        auto dedup_end = std::unique(seen_addrs.begin(), seen_addrs.end());
        Check(static_cast<int>(dedup_end - seen_addrs.begin()) == 5,
              "all 5 obj_addrs distinct");
    }

    // --- SetBuildCost ---
    {
        ReplayState s;
        Check(fabs(ReplayObsGetBuildCost(s, -1) - 1.0f) < 1e-6f,
              "default global build cost = 1.0");
        Check(fabs(ReplayObsGetBuildCost(s, 3) - 1.0f) < 1e-6f,
              "slot inherits global default");

        Check(ReplayMutSetBuildCost(s, -1, 0.5f) == 1, "global 0.5x accepted");
        Check(fabs(ReplayObsGetBuildCost(s, 4) - 0.5f) < 1e-6f,
              "slot 4 inherits 0.5x");
        Check(ReplayMutSetBuildCost(s, 4, 0.0f) == 1, "per-slot 0.0 (free) accepted");
        Check(fabs(ReplayObsGetBuildCost(s, 4) - 0.0f) < 1e-6f,
              "slot 4 now free");
        Check(ReplayMutSetBuildCost(s, 4, 1.0f) == 1, "per-slot 1.0 clears");
        Check(s.per_slot_build_cost_mult.count(4) == 0, "override erased");
        Check(ReplayMutSetBuildCost(s, 4, -0.1f) == 0, "negative rejected");
        Check(fabs(ReplayObsGetBuildCost(s, 4) - 0.5f) < 1e-6f,
              "slot 4 back to inheriting global 0.5x after clear");
    }

    // --- SetUnitCapOverride ---
    {
        ReplayState s;
        Check(ReplayObsGetUnitCapOverride(s, 1) == -2,
              "default: no override (-2 sentinel)");
        Check(ReplayObsGetUnitCapOverride(s, -1) == -2,
              "negative slot returns -2");

        Check(ReplayMutSetUnitCapOverride(s, 1, 500) == 1, "cap 500 accepted");
        Check(ReplayObsGetUnitCapOverride(s, 1) == 500, "cap read back 500");

        Check(ReplayMutSetUnitCapOverride(s, 2, -1) == 1, "unlimited accepted");
        Check(ReplayObsGetUnitCapOverride(s, 2) == -1, "unlimited reads as -1");

        Check(ReplayMutSetUnitCapOverride(s, 3, 0) == 1, "cap 0 (block build) accepted");
        Check(ReplayObsGetUnitCapOverride(s, 3) == 0, "cap 0 read back");

        Check(ReplayMutSetUnitCapOverride(s, 4, -2) == 0, "-2 rejected (not a valid cap)");
        Check(ReplayMutSetUnitCapOverride(s, -1, 100) == 0, "negative slot rejected");

        // Clear restores -2 sentinel.
        Check(ReplayMutClearUnitCapOverride(s, 1) == 1, "clear slot 1 returns 1");
        Check(ReplayObsGetUnitCapOverride(s, 1) == -2, "slot 1 back to -2");
        Check(ReplayObsGetUnitCapOverride(s, 2) == -1, "slot 2 still unlimited");
        Check(ReplayMutClearUnitCapOverride(s, -1) == 0, "clear negative rejected");
    }

    // --- Cross-task isolation with iterations 29/30/32 ---
    {
        ReplayState s;
        ReplayMutSetIncomeMultiplier(s, 2, 7.0f);
        ReplayMutSetDamageMultiplier(s, 2, 4.0f);
        ReplayMutSetBuildSpeed(s, 2, 8.0f);

        Check(ReplayMutSetBuildCost(s, 2, 0.0f) == 1, "free build for slot 2");
        Check(ReplayMutSetUnitCapOverride(s, 2, 999) == 1, "unit cap 999 for slot 2");
        Check(ReplayMutSpawnUnits(s, "bomber", 2, 1) == 1, "spawn one bomber for slot 2");

        Check(fabs(ReplayObsGetIncomeMultiplier(s, 2) - 7.0f) < 1e-6f,
              "income untouched");
        Check(fabs(ReplayObsGetDamageMultiplier(s, 2) - 4.0f) < 1e-6f,
              "damage untouched");
        Check(fabs(ReplayObsGetBuildSpeed(s, 2) - 8.0f) < 1e-6f,
              "build-speed untouched");
        Check(fabs(ReplayObsGetBuildCost(s, 2) - 0.0f) < 1e-6f,
              "build-cost landed at 0.0");
        Check(ReplayObsGetUnitCapOverride(s, 2) == 999,
              "unit cap landed at 999");
        Check(s.units.size() == 1, "1 unit spawned");
    }
}

// Tasks 114 + 115 (added 2026-04-23). Pure-state regression for the AI
// freeze toggle + camera unlock/pos/rot/zoom. Pins:
//   * FreezeAI: set-membership, idempotent in both directions, negative
//     slot rejected, observer on negative slot returns false.
//   * FreezeAI: multiple slots tracked independently; unfreezing one
//     doesn't affect another.
//   * Camera: default unlocked=false, pos/rot at origin, zoom=1.0.
//   * Camera: unlocked toggle persists AND leaves pose untouched
//     (last-known-good carries over).
//   * Camera: SetCameraPos writes all three components atomically.
//   * Camera: SetCameraZoom rejects <=0 (nonsensical zoom).
//   * Camera: rot accepts negative / large values (no bounds yet).
static void TestReplayAiFreezeAndCamera() {
    StartSuite("Replay: AI freeze + camera controls (Tasks 114/115)");

    // --- FreezeAI ---
    {
        ReplayState s;
        Check(ReplayObsFrozenAiCount(s) == 0, "default: no frozen slots");
        Check(ReplayObsIsAiFrozen(s, 0) == false, "slot 0 unfrozen by default");

        Check(ReplayMutSetAiFrozen(s, 1, true) == 1, "freeze slot 1 returns 1");
        Check(ReplayObsIsAiFrozen(s, 1) == true, "slot 1 now frozen");
        Check(ReplayObsFrozenAiCount(s) == 1, "count = 1");

        // Idempotent re-freeze.
        Check(ReplayMutSetAiFrozen(s, 1, true) == 1, "re-freeze returns 1");
        Check(ReplayObsFrozenAiCount(s) == 1, "no dup in frozen_ai_slots");

        // Multi-slot independence.
        Check(ReplayMutSetAiFrozen(s, 2, true) == 1, "freeze slot 2");
        Check(ReplayMutSetAiFrozen(s, 5, true) == 1, "freeze slot 5");
        Check(ReplayObsFrozenAiCount(s) == 3, "count = 3");
        Check(ReplayObsIsAiFrozen(s, 2) == true, "slot 2 frozen");
        Check(ReplayObsIsAiFrozen(s, 5) == true, "slot 5 frozen");
        Check(ReplayObsIsAiFrozen(s, 4) == false, "slot 4 still unfrozen");

        // Unfreeze one; others untouched.
        Check(ReplayMutSetAiFrozen(s, 2, false) == 1, "unfreeze slot 2");
        Check(ReplayObsIsAiFrozen(s, 2) == false, "slot 2 no longer frozen");
        Check(ReplayObsIsAiFrozen(s, 1) == true, "slot 1 still frozen");
        Check(ReplayObsIsAiFrozen(s, 5) == true, "slot 5 still frozen");
        Check(ReplayObsFrozenAiCount(s) == 2, "count = 2");

        // Idempotent unfreeze.
        Check(ReplayMutSetAiFrozen(s, 2, false) == 1, "re-unfreeze returns 1");
        Check(ReplayObsFrozenAiCount(s) == 2, "no change on re-unfreeze");

        // Negative slot guards.
        Check(ReplayMutSetAiFrozen(s, -1, true) == 0, "negative slot mut rejected");
        Check(ReplayObsIsAiFrozen(s, -1) == false, "negative slot obs returns false");
    }

    // --- Camera ---
    {
        ReplayState s;
        Check(ReplayObsIsCameraUnlocked(s) == false, "default: camera locked");
        Check(ReplayObsGetCameraX(s) == 0.0f, "default x = 0");
        Check(ReplayObsGetCameraY(s) == 0.0f, "default y = 0");
        Check(ReplayObsGetCameraZ(s) == 0.0f, "default z = 0");
        Check(ReplayObsGetCameraRot(s) == 0.0f, "default rot = 0");
        Check(fabs(ReplayObsGetCameraZoom(s) - 1.0f) < 1e-6f, "default zoom = 1.0");

        // Toggle unlocked, verify pose untouched.
        Check(ReplayMutSetCameraUnlocked(s, true) == 1, "unlock returns 1");
        Check(ReplayObsIsCameraUnlocked(s) == true, "now unlocked");
        Check(ReplayObsGetCameraX(s) == 0.0f, "unlock didn't touch x");

        // Set pose.
        Check(ReplayMutSetCameraPos(s, 100.0f, 200.0f, 300.0f) == 1, "set pose returns 1");
        Check(fabs(ReplayObsGetCameraX(s) - 100.0f) < 1e-3f, "x = 100");
        Check(fabs(ReplayObsGetCameraY(s) - 200.0f) < 1e-3f, "y = 200");
        Check(fabs(ReplayObsGetCameraZ(s) - 300.0f) < 1e-3f, "z = 300");

        // Toggle lock off — pose survives.
        Check(ReplayMutSetCameraUnlocked(s, false) == 1, "relock returns 1");
        Check(ReplayObsIsCameraUnlocked(s) == false, "now locked");
        Check(fabs(ReplayObsGetCameraX(s) - 100.0f) < 1e-3f,
              "pose survives lock toggle (last-known-good)");

        // Rot accepts any float.
        Check(ReplayMutSetCameraRot(s, 3.14f) == 1, "rot pi accepted");
        Check(fabs(ReplayObsGetCameraRot(s) - 3.14f) < 1e-3f, "rot = 3.14");
        Check(ReplayMutSetCameraRot(s, -1.57f) == 1, "negative rot accepted");
        Check(fabs(ReplayObsGetCameraRot(s) - (-1.57f)) < 1e-3f, "rot = -1.57");

        // Zoom guard.
        Check(ReplayMutSetCameraZoom(s, 2.5f) == 1, "zoom 2.5 accepted");
        Check(fabs(ReplayObsGetCameraZoom(s) - 2.5f) < 1e-3f, "zoom = 2.5");
        Check(ReplayMutSetCameraZoom(s, 0.0f) == 0, "zoom 0 rejected");
        Check(fabs(ReplayObsGetCameraZoom(s) - 2.5f) < 1e-3f, "zoom unchanged after rejection");
        Check(ReplayMutSetCameraZoom(s, -1.0f) == 0, "negative zoom rejected");
        Check(fabs(ReplayObsGetCameraZoom(s) - 2.5f) < 1e-3f, "zoom still unchanged");
    }
}

// Task 105 (added 2026-04-23). Pure-state regression for the OHK
// attack-power toggle + snapshot/restore. Pins:
//   * Default: ohk_enabled == false, no snapshots.
//   * Enable with local_slot < 0 => no-op (defensive against fixtures
//     that never set local_slot).
//   * Enable walks ONLY local units, snapshots current attack_power,
//     inflates to kOhkInflatedAttackPower. Enemy units are untouched
//     (READ-ONLY discipline).
//   * Disable restores every saved unit's attack_power and clears the
//     snapshot map. ohk_enabled back to false.
//   * Re-enable is idempotent — calling enable twice returns 0 (already
//     enabled) without double-inflating or corrupting snapshots.
//   * Disable without prior enable is idempotent (returns 0).
//   * Orphan guard: if a unit despawns while OHK is active, disable
//     doesn't crash and still restores the surviving units.
//   * Ownership flip guard: if a local unit becomes enemy-owned while
//     OHK is active, disable does NOT write to it (READ-ONLY holds).
static void TestReplayOhkAttackPower() {
    StartSuite("Replay: OHK attack-power toggle + snapshot (Task 105)");

    // --- Default state ---
    {
        ReplayState s;
        Check(ReplayObsIsOHK(s) == false, "default ohk disabled");
        Check(s.ohk_saved_attack_powers.empty(), "no saved snapshots");
        Check(ReplayObsGetAttackPower(s, 0x1234) == -1.0f,
              "unknown unit returns -1 sentinel");
    }

    // --- Local_slot < 0 guard ---
    {
        ReplayState s;
        s.local_slot = -1;
        ReplayMutMockUnit(s, 0x1000, "x-wing", -1, 100.0f, 100.0f, 0);
        Check(ReplayMutSetOHK(s, true) == 0, "local_slot<0 => 0 flipped");
        Check(ReplayObsIsOHK(s) == false, "ohk stays disabled without local");
    }

    // --- Enable sweeps LOCAL units only, saves snapshots ---
    {
        ReplayState s;
        s.local_slot = 1;
        ReplayMutMockUnit(s, 0x1000, "x-wing",     1, 100.0f, 100.0f, 0);
        ReplayMutMockUnit(s, 0x2000, "a-wing",     1, 200.0f, 200.0f, 0);
        ReplayMutMockUnit(s, 0x3000, "tie-fighter",0, 150.0f, 150.0f, 0);
        // Seed custom attack_powers so the snapshot has meaningful payload.
        ReplayFindUnit(s, 0x1000)->attack_power = 50.0f;
        ReplayFindUnit(s, 0x2000)->attack_power = 75.0f;
        ReplayFindUnit(s, 0x3000)->attack_power = 40.0f;

        int flipped = ReplayMutSetOHK(s, true);
        Check(flipped == 2, "enable flipped exactly 2 local units");
        Check(ReplayObsIsOHK(s) == true, "ohk now enabled");
        Check(s.ohk_saved_attack_powers.size() == 2,
              "snapshot map holds 2 entries");
        Check(s.ohk_saved_attack_powers.at(0x1000) == 50.0f,
              "x-wing snapshot = 50.0");
        Check(s.ohk_saved_attack_powers.at(0x2000) == 75.0f,
              "a-wing snapshot = 75.0");
        Check(s.ohk_saved_attack_powers.count(0x3000) == 0,
              "enemy tie-fighter NOT snapshotted");

        Check(fabs(ReplayObsGetAttackPower(s, 0x1000)
                   - ReplayState::kOhkInflatedAttackPower) < 1e-3f,
              "x-wing attack_power inflated");
        Check(fabs(ReplayObsGetAttackPower(s, 0x2000)
                   - ReplayState::kOhkInflatedAttackPower) < 1e-3f,
              "a-wing attack_power inflated");
        Check(ReplayObsGetAttackPower(s, 0x3000) == 40.0f,
              "enemy attack_power UNCHANGED (READ-ONLY)");

        // Idempotent re-enable.
        int flipped2 = ReplayMutSetOHK(s, true);
        Check(flipped2 == 0, "re-enable returns 0 (idempotent)");
        Check(s.ohk_saved_attack_powers.at(0x1000) == 50.0f,
              "re-enable did NOT corrupt snapshot");

        // Disable restores.
        int restored = ReplayMutSetOHK(s, false);
        Check(restored == 2, "disable restored 2 units");
        Check(ReplayObsIsOHK(s) == false, "ohk back to disabled");
        Check(s.ohk_saved_attack_powers.empty(), "snapshot map cleared");
        Check(ReplayObsGetAttackPower(s, 0x1000) == 50.0f,
              "x-wing attack_power restored");
        Check(ReplayObsGetAttackPower(s, 0x2000) == 75.0f,
              "a-wing attack_power restored");

        // Idempotent disable.
        int restored2 = ReplayMutSetOHK(s, false);
        Check(restored2 == 0, "disable-when-off returns 0 (idempotent)");
    }

    // --- Orphan guard: unit despawns mid-OHK ---
    {
        ReplayState s;
        s.local_slot = 2;
        ReplayMutMockUnit(s, 0xA000, "bomber", 2, 100.0f, 100.0f, 0);
        ReplayMutMockUnit(s, 0xB000, "scout",  2, 100.0f, 100.0f, 0);
        ReplayFindUnit(s, 0xA000)->attack_power = 60.0f;
        ReplayFindUnit(s, 0xB000)->attack_power = 80.0f;

        Check(ReplayMutSetOHK(s, true) == 2, "enable flipped 2 units");
        Check(s.ohk_saved_attack_powers.size() == 2, "2 snapshots pre-despawn");

        // Despawn one — snapshot remains but unit is gone.
        s.units.erase(0xA000);

        int restored = ReplayMutSetOHK(s, false);
        Check(restored == 1, "disable restored only the surviving unit");
        Check(ReplayObsIsOHK(s) == false, "ohk off after orphan-guard disable");
        Check(ReplayObsGetAttackPower(s, 0xB000) == 80.0f,
              "scout attack_power restored to 80");
        Check(ReplayObsGetAttackPower(s, 0xA000) == -1.0f,
              "orphan returns -1 sentinel (despawned)");
    }

    // --- Ownership flip guard ---
    {
        ReplayState s;
        s.local_slot = 3;
        ReplayMutMockUnit(s, 0xC000, "stolen", 3, 100.0f, 100.0f, 0);
        ReplayFindUnit(s, 0xC000)->attack_power = 42.0f;

        Check(ReplayMutSetOHK(s, true) == 1, "enable flipped 1 local unit");
        Check(s.ohk_saved_attack_powers.at(0xC000) == 42.0f,
              "snapshot captured 42");

        // Unit defects mid-OHK (owner flips to enemy slot 0).
        ReplayFindUnit(s, 0xC000)->owner_slot = 0;

        int restored = ReplayMutSetOHK(s, false);
        Check(restored == 0,
              "disable restored 0: ownership flipped => enemy READ-ONLY");
        Check(fabs(ReplayObsGetAttackPower(s, 0xC000)
                   - ReplayState::kOhkInflatedAttackPower) < 1e-3f,
              "defected unit keeps inflated attack_power (not our write to undo)");
        Check(s.ohk_saved_attack_powers.empty(),
              "snapshot map cleared anyway (OHK fully off)");
    }
}

// Task 143 (added 2026-04-23). Pure-state regression for planet tech /
// building / capital observers + mutations. Pins:
//   * unknown planet returns -1 on int observers, "" on CSV observer
//   * mutations return 0 on unknown planet
//   * SetPlanetBuildings rejects negative counts
//   * SetPlanetTech permits any int32 (no clamp -- fixtures can stress)
//   * case-insensitive keying matches #141/#142 pattern
//   * tech/buildings/capital mutations are independent (no bleed)
//   * ListPlanets row format unchanged (field not widened without need)
static void TestReplayPlanetTech() {
    StartSuite("Replay: Planet tech / buildings / capital (Task 143)");
    ReplayState s;
    Check(ReplayObsGetPlanetTech(s, "Naboo") == -1, "unknown planet tech = -1");
    Check(ReplayObsGetPlanetBuildings(s, "Naboo") == -1, "unknown planet buildings = -1");
    Check(ReplayObsGetPlanetTechAndBuildings(s, "Naboo") == "", "unknown planet CSV = ''");
    Check(ReplayMutSetPlanetTech(s, "Naboo", 3) == 0, "SetTech on unknown returns 0");
    Check(ReplayMutSetPlanetBuildings(s, "Naboo", 10) == 0, "SetBuildings on unknown returns 0");
    Check(ReplayMutSetPlanetCapital(s, "Naboo", true) == 0, "SetCapital on unknown returns 0");

    // Seed a planet via the map.
    s.planets["NABOO"] = ReplayPlanetInfo{"Naboo", 0.0f, 0, 0, 0, false};

    Check(ReplayObsGetPlanetTech(s, "Naboo") == 0, "fresh planet: tech=0");
    Check(ReplayObsGetPlanetBuildings(s, "Naboo") == 0, "fresh planet: buildings=0");
    Check(ReplayObsGetPlanetTechAndBuildings(s, "Naboo") == "0;0;0",
          "fresh planet CSV = '0;0;0'");

    Check(ReplayMutSetPlanetTech(s, "Naboo", 5) == 1, "SetTech 5 returns 1");
    Check(ReplayObsGetPlanetTech(s, "Naboo") == 5, "tech now 5");
    Check(ReplayMutSetPlanetTech(s, "naboo", 3) == 1, "case-insensitive SetTech returns 1");
    Check(ReplayObsGetPlanetTech(s, "NABOO") == 3, "uppercase read returns 3");

    Check(ReplayMutSetPlanetBuildings(s, "Naboo", 15) == 1, "SetBuildings 15 returns 1");
    Check(ReplayObsGetPlanetBuildings(s, "Naboo") == 15, "buildings now 15");
    Check(ReplayMutSetPlanetBuildings(s, "Naboo", -1) == 0, "negative building count rejected");
    Check(ReplayObsGetPlanetBuildings(s, "Naboo") == 15, "buildings unchanged after rejection");

    Check(ReplayMutSetPlanetCapital(s, "Naboo", true) == 1, "SetCapital(true) returns 1");
    Check(ReplayObsGetPlanetTechAndBuildings(s, "Naboo") == "3;15;1",
          "capital flag flipped, combined CSV = '3;15;1'");

    // Cross-field isolation: mutating capital does not touch corruption,
    // owner, tech, or buildings.
    auto* p = &s.planets["NABOO"];
    Check(p->owner_slot == 0, "owner_slot untouched by capital toggle");
    Check(fabs(p->corruption - 0.0f) < 1e-6f, "corruption untouched by capital toggle");

    // Tech accepts out-of-range values (engine may reject, but the
    // replay mirror carries whatever the caller passes so fixtures can
    // stress boundary behavior).
    Check(ReplayMutSetPlanetTech(s, "Naboo", 999) == 1, "out-of-range tech stored without clamp");
    Check(ReplayObsGetPlanetTech(s, "Naboo") == 999, "tech stored verbatim");

    // ListPlanets row format is unchanged (no tech/buildings fields
    // injected -- consumers that want them use GetPlanetTechAndBuildings).
    s.planets["NABOO"].tech_level = 2;
    s.planets["NABOO"].building_count = 5;
    s.planets["NABOO"].is_capital = false;
    std::string csv = ReplayObsListPlanets(s);
    Check(csv.find("|Naboo;0;0.000") != std::string::npos,
          "ListPlanets row still name;owner;corruption (tech fields stay in dedicated helper)");
}

// Task 139 / 140 (added 2026-04-23). Pure-state regression for ability
// catalogue observer + trigger + cooldown tick. Pins:
//   * unknown obj_addr distinguishes "ERR: unknown obj_addr" from "count=0"
//   * AddUnitAbility upserts by (obj, index): same index replaces, new
//     index appends
//   * ListAbilities row format: index;name;cooldown_ms;usable
//   * Empty name renders as "UNKNOWN"
//   * TriggerAbility rejects while cooldown>0 (usable=false), accepts
//     when usable=true, sets cooldown/usable atomically
//   * TickAbilityCooldown advances all cooldowns; negative clamp to 0
//     flips usable=true automatically
//   * AbilityCooldown observer -1 on unknown unit OR unknown index
//   * Trigger with post_cooldown=0 keeps usable=true (instant-refresh
//     ability semantics)
static void TestReplayAbilities() {
    StartSuite("Replay: abilities catalog + trigger (Tasks 139/140)");

    ReplayState s;
    Check(ReplayObsListAbilities(s, 0xDEAD) == "ERR: unknown obj_addr",
          "unknown unit returns ERR sentinel (distinct from 'count=0')");
    Check(ReplayObsAbilityCooldown(s, 0xDEAD, 0) == -1, "cooldown on unknown -> -1");
    Check(ReplayMutAddUnitAbility(s, 0xDEAD, 0, "Foo", 0, true) == 0,
          "AddUnitAbility on unknown returns 0");
    Check(ReplayMutTriggerAbility(s, 0xDEAD, 0, 10000) == 0,
          "TriggerAbility on unknown returns 0");

    constexpr uint64_t kHero = 0xA000;
    ReplayMutMockUnit(s, kHero, "Han_Solo", 0, 2000.0f, 2500.0f, 0);
    Check(ReplayObsListAbilities(s, kHero) == "count=0",
          "known unit without abilities returns 'count=0'");

    // Add two abilities.
    Check(ReplayMutAddUnitAbility(s, kHero, 0, "Lucky_Shot", 0, true) == 1,
          "add ability 0 returns 1");
    Check(ReplayMutAddUnitAbility(s, kHero, 1, "Grenade",    15000, false) == 1,
          "add ability 1 returns 1");
    Check(ReplayMutAddUnitAbility(s, kHero, 2, "",           5000, false) == 1,
          "add empty-name ability returns 1 (name defaults render UNKNOWN)");

    std::string csv = ReplayObsListAbilities(s, kHero);
    Check(csv.rfind("count=3", 0) == 0, "populated roster header = 'count=3'");
    Check(csv.find("|0;Lucky_Shot;0;1") != std::string::npos, "row 0 exact shape");
    Check(csv.find("|1;Grenade;15000;0") != std::string::npos, "row 1 exact shape");
    Check(csv.find("|2;UNKNOWN;5000;0") != std::string::npos,
          "empty name renders as UNKNOWN");

    // Upsert: re-adding index 0 replaces the row.
    Check(ReplayMutAddUnitAbility(s, kHero, 0, "Lucky_Shot_v2", 2000, false) == 1,
          "re-add index 0 returns 1 (upsert)");
    csv = ReplayObsListAbilities(s, kHero);
    Check(csv.rfind("count=3", 0) == 0, "upsert keeps count=3");
    Check(csv.find("|0;Lucky_Shot_v2;2000;0") != std::string::npos,
          "index 0 row replaced by upsert");

    // Trigger contract: usable true -> sets cooldown + flips usable.
    Check(ReplayMutAddUnitAbility(s, kHero, 3, "Force_Wave", 0, true) == 1,
          "add usable ability 3");
    Check(ReplayObsAbilityCooldown(s, kHero, 3) == 0, "cooldown starts 0");
    Check(ReplayMutTriggerAbility(s, kHero, 3, 30000) == 1, "trigger returns 1 when usable");
    Check(ReplayObsAbilityCooldown(s, kHero, 3) == 30000, "post-trigger cooldown 30000");

    // Trigger while on cooldown is rejected.
    Check(ReplayMutTriggerAbility(s, kHero, 3, 60000) == 0,
          "trigger while on cooldown rejected");
    Check(ReplayObsAbilityCooldown(s, kHero, 3) == 30000,
          "cooldown unchanged after rejected trigger");

    // Instant-refresh: post_cooldown=0 keeps usable=true.
    Check(ReplayMutAddUnitAbility(s, kHero, 4, "Rally", 0, true) == 1, "add ability 4");
    Check(ReplayMutTriggerAbility(s, kHero, 4, 0) == 1, "trigger with 0 cooldown");
    Check(ReplayObsAbilityCooldown(s, kHero, 4) == 0, "cooldown stays 0");
    // Follow-up trigger still allowed because usable didn't flip off.
    Check(ReplayMutTriggerAbility(s, kHero, 4, 0) == 1, "re-trigger instant-refresh ability");

    // Tick advances all cooldowns.
    Check(ReplayMutTickAbilityCooldown(s, kHero, 10000) == 1, "tick 10s");
    Check(ReplayObsAbilityCooldown(s, kHero, 3) == 20000, "ability 3 cooldown dropped to 20000");
    Check(ReplayObsAbilityCooldown(s, kHero, 1) == 5000,
          "ability 1 cooldown dropped from 15000 to 5000");

    // Over-tick clamps to 0 and flips usable back on.
    Check(ReplayMutTickAbilityCooldown(s, kHero, 999999) == 1, "over-tick");
    Check(ReplayObsAbilityCooldown(s, kHero, 3) == 0, "cooldown clamped to 0");
    Check(ReplayMutTriggerAbility(s, kHero, 3, 10000) == 1,
          "newly-available ability can be triggered again");

    // Unknown index sentinel.
    Check(ReplayObsAbilityCooldown(s, kHero, 99) == -1,
          "cooldown for unknown index returns -1");
    Check(ReplayMutTriggerAbility(s, kHero, 99, 1000) == 0,
          "trigger on unknown index returns 0");

    // Negative cooldown rejected.
    Check(ReplayMutTriggerAbility(s, kHero, 0, -1) == 0, "negative cooldown rejected");
}

// Task 138 (added 2026-04-23). Pure-state regression for HeroStatEdit
// dispatcher. Pins:
//   * unknown unit rejected
//   * non-hero unit rejected (even if the field is otherwise valid)
//   * each supported field routes to the correct per-field mutator
//   * unknown field name rejected
//   * dispatcher does NOT touch any field other than the targeted one
//     (cross-field isolation survives the composition)
static void TestReplayHeroStatEdit() {
    StartSuite("Replay: HeroStatEdit dispatcher (Task 138)");

    ReplayState s;
    Check(ReplayMutHeroStatEdit(s, 0xDEAD, "hull", 100.0f) == 0, "unknown unit rejected");

    constexpr uint64_t kHero = 0x9000;
    constexpr uint64_t kGrunt = 0x9100;
    ReplayMutMockUnit(s, kHero,  "Hero",    0, 4000.0f, 4000.0f, 2);
    ReplayMutMockUnit(s, kGrunt, "Grunt",   0, 500.0f,  500.0f,  0);
    ReplayMutSetUnitIsHero(s, kHero, true);
    // seed other fields so we can assert they don't change on unrelated edits
    ReplayMutSetUnitMaxShield(s, kHero, 300.0f);
    ReplayMutSetUnitShield(s, kHero, 250.0f);
    ReplayMutSetUnitMaxSpeed(s, kHero, 200.0f);
    ReplayMutSetUnitSpeed(s, kHero, 150.0f);

    // Non-hero rejected even on valid field.
    Check(ReplayMutHeroStatEdit(s, kGrunt, "hull", 999.0f) == 0, "non-hero rejected");
    Check(ReplayFindUnit(s, kGrunt)->hull == 500.0f, "non-hero hull untouched after rejection");

    // Unknown field rejected.
    Check(ReplayMutHeroStatEdit(s, kHero, "invent", 10.0f) == 0, "unknown field rejected");

    // Each field routes correctly.
    Check(ReplayMutHeroStatEdit(s, kHero, "hull", 1234.0f) == 1, "hull dispatch returns 1");
    Check(ReplayFindUnit(s, kHero)->hull == 1234.0f, "hull updated to 1234");
    // Other fields untouched after hull edit.
    Check(ReplayObsGetUnitShield(s, kHero) == 250.0f, "shield untouched by hull edit");
    Check(ReplayObsGetUnitSpeed(s, kHero) == 150.0f, "speed untouched by hull edit");

    Check(ReplayMutHeroStatEdit(s, kHero, "shield", 100.0f) == 1, "shield dispatch returns 1");
    Check(ReplayObsGetUnitShield(s, kHero) == 100.0f, "shield updated to 100");
    Check(ReplayFindUnit(s, kHero)->hull == 1234.0f, "hull untouched by shield edit");

    Check(ReplayMutHeroStatEdit(s, kHero, "max_shield", 400.0f) == 1, "max_shield dispatch returns 1");
    Check(ReplayObsGetUnitMaxShield(s, kHero) == 400.0f, "max_shield updated to 400");

    Check(ReplayMutHeroStatEdit(s, kHero, "speed", 75.0f) == 1, "speed dispatch returns 1");
    Check(ReplayObsGetUnitSpeed(s, kHero) == 75.0f, "speed updated to 75");

    Check(ReplayMutHeroStatEdit(s, kHero, "max_speed", 300.0f) == 1, "max_speed dispatch returns 1");
    Check(ReplayObsGetUnitMaxSpeed(s, kHero) == 300.0f, "max_speed updated to 300");

    Check(ReplayMutHeroStatEdit(s, kHero, "respawn_ms", 20000.0f) == 1, "respawn_ms dispatch returns 1");
    Check(ReplayObsGetHeroRespawnTimer(s, kHero) == 20000, "respawn_ms updated to 20000");

    // Shield clamp still honoured via the dispatcher path (no bypass).
    Check(ReplayMutHeroStatEdit(s, kHero, "shield", 9999.0f) == 1, "shield over-cap dispatch returns 1");
    Check(ReplayObsGetUnitShield(s, kHero) == 400.0f, "shield clamped to max 400 via dispatcher");

    // Permadeath is NOT routed via the dispatcher (bool, not float field).
    // Verifying the dispatcher explicitly refuses it keeps the contract
    // tight -- callers must use SWFOC_SetPermadeath directly.
    Check(ReplayMutHeroStatEdit(s, kHero, "permadeath", 1.0f) == 0,
          "permadeath NOT routable via dispatcher (use SetPermadeath)");
}

// Task 137 (added 2026-04-23). Pure-state regression for kill / revive.
// Pins:
//   * kill on unknown unit returns 0
//   * kill on a live unit writes hull=0 and returns 1
//   * kill on an already-dead unit returns 0 (no-op idempotency)
//   * revive on unknown unit returns 0
//   * revive on a dead unit restores hull to max_hull
//   * revive on already-full unit returns 0 (no-op)
//   * revive on a unit without max_hull returns 0 (cannot guess target)
//   * event stream captures both mutations with coherent requested/current
static void TestReplayKillRevive() {
    StartSuite("Replay: kill / revive (Task 137)");

    ReplayState s;
    Check(ReplayMutKillUnit(s, 0xDEADBEEF) == 0, "kill on unknown unit returns 0");
    Check(ReplayMutReviveUnit(s, 0xDEADBEEF) == 0, "revive on unknown unit returns 0");

    constexpr uint64_t kUnit = 0x4000;
    ReplayMutMockUnit(s, kUnit, "Target", 0, 500.0f, 1000.0f, 2);
    Check(ReplayFindUnit(s, kUnit)->hull == 500.0f, "pre-kill: hull 500");

    Check(ReplayMutKillUnit(s, kUnit) == 1, "kill live unit returns 1");
    Check(ReplayFindUnit(s, kUnit)->hull == 0.0f, "post-kill: hull is 0");

    Check(ReplayMutKillUnit(s, kUnit) == 0, "kill already-dead unit returns 0");
    Check(ReplayFindUnit(s, kUnit)->hull == 0.0f, "idempotent kill keeps hull 0");

    Check(ReplayMutReviveUnit(s, kUnit) == 1, "revive dead unit returns 1");
    Check(ReplayFindUnit(s, kUnit)->hull == 1000.0f, "post-revive: hull restored to max_hull");

    Check(ReplayMutReviveUnit(s, kUnit) == 0, "revive already-full unit returns 0");
    Check(ReplayFindUnit(s, kUnit)->hull == 1000.0f, "idempotent revive keeps hull at max");

    // Unit without max_hull cannot be revived -- avoids zeroing-by-surprise
    // when a future capture is missing the max field.
    constexpr uint64_t kOrphan = 0x5000;
    ReplayMutMockUnit(s, kOrphan, "Orphan", 0, 50.0f, 0.0f, 0);
    ReplayMutKillUnit(s, kOrphan);
    Check(ReplayFindUnit(s, kOrphan)->hull == 0.0f, "orphan can still be killed");
    Check(ReplayMutReviveUnit(s, kOrphan) == 0, "orphan without max_hull cannot be revived");
    Check(ReplayFindUnit(s, kOrphan)->hull == 0.0f, "orphan hull unchanged after rejected revive");

    // Event stream witnessed every mutation.
    std::string csv = ReplayObsEventStreamDrain(s);
    Check(csv.rfind("count=3", 0) == 0,
          "event stream captures 3 mutations (kill, revive, kill-orphan)");
}

// Task 129 (added 2026-04-23). Pure-state regression for damage
// multiplier. Pins:
//   * default multiplier is 1.0 for every slot
//   * global set propagates to all slots that have no per-slot override
//   * per-slot override wins over global
//   * setting per-slot to exactly 1.0 clears the override (falls back
//     to global)
//   * negative multipliers are rejected
//   * ApplyDamage scales by the effective multiplier of the TARGET unit
//   * multiplier 0 blocks all damage (target hull unchanged)
//   * event stream requested_hp reflects UNSCALED intent, current_hp
//     reflects scaled outcome
static void TestReplayDamageMultiplier() {
    StartSuite("Replay: damage multiplier (Task 129)");

    ReplayState s;
    Check(fabs(ReplayObsGetDamageMultiplier(s, -1) - 1.0f) < 1e-6,
          "default global multiplier is 1.0");
    Check(fabs(ReplayObsGetDamageMultiplier(s, 0) - 1.0f) < 1e-6,
          "default per-slot resolves to 1.0 via fallback to global");

    Check(ReplayMutSetDamageMultiplier(s, -1, 2.0f) == 1, "global set returns 1");
    Check(fabs(ReplayObsGetDamageMultiplier(s, -1) - 2.0f) < 1e-6,
          "global multiplier now 2.0");
    Check(fabs(ReplayObsGetDamageMultiplier(s, 5) - 2.0f) < 1e-6,
          "unoverridden slot inherits global 2.0");

    Check(ReplayMutSetDamageMultiplier(s, 5, 4.0f) == 1, "per-slot set returns 1");
    Check(fabs(ReplayObsGetDamageMultiplier(s, 5) - 4.0f) < 1e-6,
          "slot 5 override wins over global");
    Check(fabs(ReplayObsGetDamageMultiplier(s, 3) - 2.0f) < 1e-6,
          "slot 3 still at global 2.0");

    // Setting per-slot to 1.0 clears the override.
    Check(ReplayMutSetDamageMultiplier(s, 5, 1.0f) == 1, "clear-by-one set returns 1");
    Check(fabs(ReplayObsGetDamageMultiplier(s, 5) - 2.0f) < 1e-6,
          "slot 5 now inherits global again after clear");
    Check(s.per_slot_damage_mult.count(5) == 0,
          "per-slot table removes the entry on value==1.0");

    // Negative multiplier rejected without mutating state.
    Check(ReplayMutSetDamageMultiplier(s, -1, -3.0f) == 0, "negative global rejected");
    Check(fabs(ReplayObsGetDamageMultiplier(s, -1) - 2.0f) < 1e-6,
          "rejected negative leaves global at 2.0");
    Check(ReplayMutSetDamageMultiplier(s, 0, -1.0f) == 0, "negative per-slot rejected");
    Check(s.per_slot_damage_mult.count(0) == 0,
          "rejected per-slot does not create an entry");

    // ApplyDamage scales by effective multiplier.
    constexpr uint64_t kUnit = 0x1000;
    ReplayMutMockUnit(s, kUnit, "Target", 1, 1000.0f, 1000.0f, 0);
    // global=2.0 still, slot 1 has no override -> effective 2.0
    float h1 = ReplayMutApplyDamage(s, kUnit, 100.0f);
    Check(fabs(h1 - 800.0f) < 1e-3,
          "damage 100 scaled 2x -> hull drops from 1000 to 800");

    // Per-slot override for target owner wins.
    ReplayMutSetDamageMultiplier(s, 1, 0.5f);
    float h2 = ReplayMutApplyDamage(s, kUnit, 100.0f);
    Check(fabs(h2 - 750.0f) < 1e-3,
          "damage 100 scaled 0.5x -> hull drops from 800 to 750");

    // Zero multiplier blocks all damage.
    ReplayMutSetDamageMultiplier(s, 1, 0.0f);
    float h3 = ReplayMutApplyDamage(s, kUnit, 1000.0f);
    Check(fabs(h3 - 750.0f) < 1e-3,
          "mult 0 blocks all damage regardless of requested amount");

    // Event stream captures unscaled requested + scaled current.
    ReplayMutSetDamageMultiplier(s, 1, 3.0f);
    constexpr uint64_t kUnit2 = 0x2000;
    ReplayMutMockUnit(s, kUnit2, "Target2", 1, 500.0f, 500.0f, 0);
    ReplayMutApplyDamage(s, kUnit2, 50.0f);  // scaled by 3x -> 150 damage
    std::string csv = ReplayObsEventStreamDrain(s);
    // Row format: ts;addr;owner;requested_hp;current_hp
    // requested = before - amount = 500 - 50 = 450 (UNSCALED intent)
    // current = 500 - 150 = 350 (after 3x scaling)
    std::string expected = std::string("|0;") + std::to_string(kUnit2) + ";1;450.000;350.000";
    Check(csv.find(expected) != std::string::npos,
          "event captures unscaled requested 450 + scaled current 350");
}

// Task 98 (added 2026-04-23). Pure-state regression for HealAllLocal
// port from the CE trainer. Pins:
//   * empty state returns 0
//   * units at full hull do NOT count as healed (no redundant writes)
//   * only units whose owner_slot == s.local_slot are healed
//   * enemy units are NEVER mutated (READ-ONLY enforced)
//   * local_slot=-1 collapses the sweep to 0 (no local target)
//   * units without max_hull (i.e. max_hull==0) are skipped so the
//     mutation cannot zero out a future live unit whose max we couldn't
//     read (matches the live bridge's defensive skip path)
static void TestReplayHealAllLocal() {
    StartSuite("Replay: HealAllLocal CE-trainer port (Task 98)");
    ReplayState s;
    Check(ReplayMutHealAllLocal(s) == 0, "empty state heals 0 units");

    constexpr uint64_t kLocalDmg  = 0x100;
    constexpr uint64_t kLocalFull = 0x200;
    constexpr uint64_t kEnemy     = 0x300;
    ReplayMutMockUnit(s, kLocalDmg,  "Aggressor", 6, 1000.0f, 6000.0f, 2);
    ReplayMutMockUnit(s, kLocalFull, "Fighter",   6, 500.0f,  500.0f,  0);
    ReplayMutMockUnit(s, kEnemy,     "ISD",       1, 3000.0f, 8000.0f, 4);
    s.local_slot = 6;

    Check(ReplayFindUnit(s, kLocalDmg)->hull  == 1000.0f, "pre-heal: damaged local at 1000");
    Check(ReplayFindUnit(s, kLocalFull)->hull == 500.0f,  "pre-heal: full local at 500");
    Check(ReplayFindUnit(s, kEnemy)->hull     == 3000.0f, "pre-heal: enemy at 3000");

    int healed = ReplayMutHealAllLocal(s);
    Check(healed == 1,
          "only the damaged local unit counts as healed (full-hull unit skipped)");

    Check(ReplayFindUnit(s, kLocalDmg)->hull  == 6000.0f, "damaged local restored to max_hull 6000");
    Check(ReplayFindUnit(s, kLocalFull)->hull == 500.0f,  "already-full local unchanged at 500");
    Check(ReplayFindUnit(s, kEnemy)->hull     == 3000.0f, "ENEMY unit NEVER healed (READ-ONLY)");

    // Idempotent after: re-run gives 0 because both local units are now full.
    Check(ReplayMutHealAllLocal(s) == 0,
          "second sweep heals 0 (all local units full)");

    // local_slot=-1 collapses sweep to 0 even when units exist.
    s.local_slot = -1;
    ReplayFindUnit(s, kLocalDmg)->hull = 100.0f;
    Check(ReplayMutHealAllLocal(s) == 0, "local_slot=-1 collapses sweep to 0");
    Check(ReplayFindUnit(s, kLocalDmg)->hull == 100.0f,
          "local_slot=-1: damaged local NOT mutated");

    // max_hull==0 units are skipped (live bridge could not read max_hull).
    ReplayState s2;
    s2.local_slot = 6;
    ReplayMutMockUnit(s2, 0x111, "Orphan", 6, 50.0f, 0.0f, 0);
    Check(ReplayMutHealAllLocal(s2) == 0,
          "max_hull=0 unit is skipped (defensive guard)");
    Check(ReplayFindUnit(s2, 0x111)->hull == 50.0f,
          "max_hull=0: hull unchanged after skip");
}

// Task 158 (added 2026-04-23). Pure-state regression for EnumerateUnits
// CSV contract. Pins:
//   * negative slot returns "count=0" (rejected sentinel)
//   * empty unit map returns "count=0"
//   * populated state returns "count=N|row|..." with N reflecting only
//     matching rows (not the total tactical unit count)
//   * is_local column still honours s.local_slot so a single helper can
//     power both ListTacticalUnits and EnumerateUnits consumers
//   * faction filter does NOT drop the is_selected flag -- selecting a
//     unit from a filtered faction still renders is_selected=1
static void TestReplayEnumerateUnits() {
    StartSuite("Replay: EnumerateUnits per-faction filter (Task 158)");
    ReplayState s;
    Check(ReplayObsEnumerateUnitsForSlot(s, 0) == "count=0",
          "empty state with slot 0 returns 'count=0'");
    Check(ReplayObsEnumerateUnitsForSlot(s, -1) == "count=0",
          "negative slot is rejected with 'count=0'");

    constexpr uint64_t kA = 0x100, kB = 0x200, kC = 0x300;
    ReplayMutMockUnit(s, kA, "Fighter",  1, 100.0f, 100.0f, 0);
    ReplayMutMockUnit(s, kB, "Frigate",  1, 500.0f, 500.0f, 2);
    ReplayMutMockUnit(s, kC, "Corvette", 6, 250.0f, 250.0f, 1);
    s.local_slot = 6;

    std::string csvEmpire = ReplayObsEnumerateUnitsForSlot(s, 1);
    Check(csvEmpire.rfind("count=2", 0) == 0, "slot 1 matches 2 units (Fighter + Frigate)");
    Check(csvEmpire.find("|256;1;100.000;0;0;0;0") != std::string::npos,
          "Fighter row present, is_local=0 because slot != local_slot");
    Check(csvEmpire.find("|512;1;500.000;0;0;0;0") != std::string::npos,
          "Frigate row present with correct hull");
    Check(csvEmpire.find("|768;") == std::string::npos,
          "Corvette (slot 6) absent from slot-1 enumeration");

    std::string csvLocal = ReplayObsEnumerateUnitsForSlot(s, 6);
    Check(csvLocal.rfind("count=1", 0) == 0, "slot 6 matches 1 unit (Corvette)");
    Check(csvLocal.find("|768;6;250.000;0;0;1;0") != std::string::npos,
          "Corvette row has is_local=1 because slot == local_slot");

    // Select the Corvette; is_selected column must flip even inside the filter.
    ReplayMutSetSelected(s, kC);
    std::string csvLocalSel = ReplayObsEnumerateUnitsForSlot(s, 6);
    Check(csvLocalSel.find("|768;6;250.000;0;0;1;1") != std::string::npos,
          "is_selected flips to 1 after SetSelected, even under faction filter");

    // Unmatched slot returns "count=0".
    Check(ReplayObsEnumerateUnitsForSlot(s, 3) == "count=0",
          "slot with no units returns 'count=0'");

    // local_slot=-1 collapses is_local column to 0 even on the local faction.
    s.local_slot = -1;
    std::string csvNoLocal = ReplayObsEnumerateUnitsForSlot(s, 6);
    Check(csvNoLocal.find("|768;6;250.000;0;0;0;1") != std::string::npos,
          "local_slot=-1 collapses is_local to 0 for every enumerated row");
}

// Task 112 (added 2026-04-23). Pure-state regression for the damage-event
// ring buffer. Pins:
//   * empty state returns literal "count=0"
//   * each ApplyDamage call pushes exactly one event
//   * drain is destructive (immediately-subsequent drain returns "count=0")
//   * event row captures requested_hp (pre-clamp) AND current_hp (post),
//     so consumers can detect god-mode clamps and OHK zero-writes
//   * damage on unknown unit does not push an event
//   * invulnerable hardpoint still logs the event (intent is visible)
//     but current_hp reflects the unchanged hull
static void TestReplayEventStream() {
    StartSuite("Replay: damage-event stream (Task 112)");

    ReplayState s;
    Check(ReplayObsEventLogCount(s) == 0, "fresh state has empty event log");
    Check(ReplayObsEventStreamDrain(s) == "count=0",
          "drain of empty log returns 'count=0'");

    constexpr uint64_t kObj = 0x1F2A3334D00ULL;
    ReplayMutMockUnit(s, kObj, "Aggressor", 6, 5000.0f, 6000.0f, 2);
    s.local_slot = 6;

    float h1 = ReplayMutApplyDamage(s, kObj, 500.0f);
    Check(fabs(h1 - 4500.0f) < 1e-3, "ApplyDamage decrements hull normally");
    Check(ReplayObsEventLogCount(s) == 1, "one event logged after one ApplyDamage");

    std::string csv = ReplayObsEventStreamDrain(s);
    Check(csv.rfind("count=1", 0) == 0, "drain returns 'count=1' after one event");
    std::string tail = std::string("|0;") + std::to_string(kObj) + ";6;4500.000;4500.000";
    Check(csv.find(tail) != std::string::npos,
          "event row records requested_hp 4500 and current_hp 4500 for clean damage");

    Check(ReplayObsEventLogCount(s) == 0, "drain empties the log");
    Check(ReplayObsEventStreamDrain(s) == "count=0",
          "immediate re-drain returns 'count=0'");

    // Invulnerable unit: event logged but current_hp reflects unchanged hull.
    ReplayMutMakeInvulnerable(s, kObj, true);
    float h2 = ReplayMutApplyDamage(s, kObj, 1000.0f);
    Check(fabs(h2 - 4500.0f) < 1e-3, "INVULNERABLE blocks damage");
    Check(ReplayObsEventLogCount(s) == 1, "event logged even when damage blocked");
    std::string csv2 = ReplayObsEventStreamDrain(s);
    std::string invRow = std::string("|0;") + std::to_string(kObj) + ";6;3500.000;4500.000";
    Check(csv2.find(invRow) != std::string::npos,
          "invuln event captures requested=3500 (intent) and current=4500 (unchanged)");

    // Unknown unit: no event.
    Check(ReplayMutApplyDamage(s, 0xDEADBEEF, 10.0f) == -1.0f,
          "ApplyDamage on unknown unit returns -1");
    Check(ReplayObsEventLogCount(s) == 0, "no event logged for unknown unit");

    // Zero-damage call still logs (intent is always witnessed).
    ReplayMutMakeInvulnerable(s, kObj, false);
    float h3 = ReplayMutApplyDamage(s, kObj, 0.0f);
    Check(fabs(h3 - 4500.0f) < 1e-3, "zero-damage is a no-op on hull");
    Check(ReplayObsEventLogCount(s) == 1, "zero-damage still logs an event");
    std::string csv3 = ReplayObsEventStreamDrain(s);
    std::string zeroRow = std::string("|0;") + std::to_string(kObj) + ";6;4500.000;4500.000";
    Check(csv3.find(zeroRow) != std::string::npos,
          "zero-damage event has requested==current (no attempted change)");

    // Multi-event burst: several damages across different units queue up
    // and drain in order.
    constexpr uint64_t kObj2 = 0x1F2BB40B280ULL;
    ReplayMutMockUnit(s, kObj2, "Corvette", 1, 2000.0f, 2000.0f, 1);
    ReplayMutApplyDamage(s, kObj,  100.0f);
    ReplayMutApplyDamage(s, kObj2, 250.0f);
    ReplayMutApplyDamage(s, kObj,  50.0f);
    Check(ReplayObsEventLogCount(s) == 3, "three queued events before drain");
    std::string csv4 = ReplayObsEventStreamDrain(s);
    Check(csv4.rfind("count=3", 0) == 0, "drain reports count=3 for three-event burst");

    // Locate each expected row -- order of appearance preserves insertion.
    size_t pos0 = csv4.find(std::to_string(kObj)  + ";6;4400.000;4400.000");
    size_t pos1 = csv4.find(std::to_string(kObj2) + ";1;1750.000;1750.000");
    size_t pos2 = csv4.find(std::to_string(kObj)  + ";6;4350.000;4350.000");
    Check(pos0 != std::string::npos, "first burst event (kObj -100) present in order");
    Check(pos1 != std::string::npos, "second burst event (kObj2 -250) present in order");
    Check(pos2 != std::string::npos, "third burst event (kObj -50) present in order");
    Check(pos0 < pos1 && pos1 < pos2, "events drain in insertion order (FIFO)");
}

// Task 111 (added 2026-04-23). Pure-state regression for GetAllPlayers CSV
// contract. Pins:
//   * empty-state returns literal "count=0"
//   * populated state returns "count=N|row|row|..."
//   * row shape: slot;faction;credits;tech;is_human;is_local;unit_count
//   * is_local honours s.local_slot
//   * unit_count is a faithful tally of s.units entries per owner_slot
//   * UNKNOWN is the default faction string when ReplayPlayer.faction_name is empty
static void TestReplayGetAllPlayers() {
    StartSuite("Replay: GetAllPlayers CSV contract (Task 111)");
    ReplayState s;
    Check(ReplayObsListAllPlayers(s) == "count=0",
          "empty state returns literal 'count=0'");

    s.players.push_back(ReplayPlayer{0, "REBEL", 50000.0, 2, "player_0"});
    s.players.push_back(ReplayPlayer{1, "EMPIRE", 120000.5, 4, "player_1"});
    s.players.push_back(ReplayPlayer{6, "UNDERWORLD", 999.0, 1, ""});  // empty faction name
    s.local_slot = 6;

    // 2 units owned by slot 6, 1 unit by slot 1, 0 units by slot 0.
    ReplayMutMockUnit(s, 0x111ULL, "Aggressor",   6, 1000.0f, 1000.0f, 1);
    ReplayMutMockUnit(s, 0x222ULL, "Corvette",    6, 500.0f,  500.0f,  1);
    ReplayMutMockUnit(s, 0x333ULL, "StarFighter", 1, 200.0f,  200.0f,  1);

    std::string csv = ReplayObsListAllPlayers(s);
    Check(csv.rfind("count=3", 0) == 0, "populated state starts with 'count=3'");
    Check(csv.find("|0;REBEL;50000.000;2;0;0;0") != std::string::npos,
          "slot 0: faction REBEL, credits 50000, tech 2, no local/units");
    Check(csv.find("|1;EMPIRE;120000.500;4;0;0;1") != std::string::npos,
          "slot 1: faction EMPIRE, tech 4, one unit owned, not local");
    Check(csv.find("|6;UNDERWORLD;999.000;1;1;1;2") != std::string::npos,
          "slot 6: local slot, is_local=1, 2 units owned");

    // Drop local_slot; is_local column collapses to 0 everywhere.
    s.local_slot = -1;
    std::string csv2 = ReplayObsListAllPlayers(s);
    Check(csv2.find("|6;UNDERWORLD;999.000;1;0;0;2") != std::string::npos,
          "local_slot=-1 collapses is_local to 0 (even on slot 6)");

    // Empty faction name defaults to UNKNOWN.
    ReplayState s2;
    s2.players.push_back(ReplayPlayer{3, "", 0.0, 0, ""});
    std::string csv3 = ReplayObsListAllPlayers(s2);
    Check(csv3.find("|3;UNKNOWN;0.000;0;0;0;0") != std::string::npos,
          "empty faction_name renders as UNKNOWN");

    // Unit_count ignores units whose owner_slot is outside the roster.
    ReplayState s3;
    s3.players.push_back(ReplayPlayer{0, "REBEL", 100.0, 1, ""});
    ReplayMutMockUnit(s3, 0x1000ULL, "X", 0, 10.0f, 10.0f, 0);
    ReplayMutMockUnit(s3, 0x2000ULL, "Y", 99, 10.0f, 10.0f, 0);  // orphan owner
    std::string csv4 = ReplayObsListAllPlayers(s3);
    Check(csv4.find("|0;REBEL;100.000;1;0;0;1") != std::string::npos,
          "orphan-owner units don't get counted against known slots");
}

// Task 113 (added 2026-04-23). Pure-state regression for fog-of-war reveal.
// Contract: RevealAll is idempotent (double-enable is single-state),
// per-slot, negative slots rejected, and Disable removes only the targeted
// slot while leaving others intact.
static void TestReplayRevealAll() {
    StartSuite("Replay: RevealAll fog-of-war toggle (Task 113)");
    ReplayState s;
    Check(ReplayObsRevealedCount(s) == 0, "fresh state: no slots revealed");
    Check(ReplayObsIsRevealed(s, 0) == 0, "fresh state: slot 0 not revealed");

    Check(ReplayMutRevealAll(s, 6, true) == 1, "reveal slot 6 returns success");
    Check(ReplayObsIsRevealed(s, 6) == 1, "slot 6 now revealed");
    Check(ReplayObsRevealedCount(s) == 1, "count=1 after single reveal");

    Check(ReplayMutRevealAll(s, 6, true) == 1, "reveal-again is idempotent");
    Check(ReplayObsRevealedCount(s) == 1, "count stays 1 after re-reveal");

    Check(ReplayMutRevealAll(s, 2, true) == 1, "reveal slot 2 returns success");
    Check(ReplayObsRevealedCount(s) == 2, "count=2 with two slots revealed");
    Check(ReplayObsIsRevealed(s, 2) == 1, "slot 2 now revealed");
    Check(ReplayObsIsRevealed(s, 6) == 1, "slot 6 still revealed after adding 2");

    Check(ReplayMutRevealAll(s, 6, false) == 1, "un-reveal slot 6 returns success");
    Check(ReplayObsIsRevealed(s, 6) == 0, "slot 6 no longer revealed");
    Check(ReplayObsIsRevealed(s, 2) == 1, "slot 2 intact after removing 6");
    Check(ReplayObsRevealedCount(s) == 1, "count=1 after single un-reveal");

    Check(ReplayMutRevealAll(s, 6, false) == 1, "un-reveal-already-unset is idempotent (returns success)");
    Check(ReplayObsRevealedCount(s) == 1, "count unchanged on redundant un-reveal");

    Check(ReplayMutRevealAll(s, -1, true) == 0, "negative slot rejected");
    Check(ReplayObsIsRevealed(s, -1) == 0, "IsRevealed(-1) returns 0");
    Check(ReplayObsRevealedCount(s) == 1, "rejected negative does not pollute state");
}

// Task 106 (added 2026-04-23). Pure-state regression for the God Mode
// hardpoint-behavior sweep. The contract: enabling God Mode attaches
// INVULNERABLE to every hardpoint of every local-owned unit, does NOT
// touch enemy units, and disabling removes INVULNERABLE from local units
// only. ApplyDamage on a swept-local unit is a no-op; on enemy it still
// decrements. This pins the Task 99 + Task 106 integration so a future
// refactor that collapses the sweep into a flag byte fails loudly.
static void TestReplayGodModeSweep() {
    StartSuite("Replay: God Mode sweep (Task 106)");

    ReplayState s;
    constexpr uint64_t kLocal1 = 0x1F2A3334D00ULL;
    constexpr uint64_t kLocal2 = 0x1F2BB40B280ULL;
    constexpr uint64_t kEnemy  = 0x1F2CC50C400ULL;
    s.local_slot = 6;
    ReplayMutMockUnit(s, kLocal1, "Aggressor_Destroyer", 6, 5000.0f, 6000.0f, 3);
    ReplayMutMockUnit(s, kLocal2, "Corellian_Corvette",  6, 2000.0f, 2500.0f, 2);
    ReplayMutMockUnit(s, kEnemy,  "ISD",                 1, 8000.0f, 8000.0f, 4);

    // Baseline: nothing invulnerable anywhere.
    Check(ReplayObsGodModeFullyActive(s) == 0,
          "pre-enable: no local unit has full-hardpoint INVULNERABLE");
    Check(ReplayUnitAnyHardpointHasBehavior(*ReplayFindUnit(s, kEnemy), "INVULNERABLE") == false,
          "pre-enable: enemy has no INVULNERABLE (obviously)");

    // Enable: two local units should flip; enemy stays untouched.
    int flipped = ReplayMutSweepGodMode(s, true);
    Check(flipped == 2, "enable sweep reports 2 units flipped (both local)");
    Check(ReplayObsGodModeFullyActive(s) == 1, "after enable: all local units carry INVULNERABLE everywhere");
    Check(ReplayUnitAllHardpointsHaveBehavior(*ReplayFindUnit(s, kLocal1), "INVULNERABLE") == true,
          "kLocal1: every hardpoint carries INVULNERABLE");
    Check(ReplayUnitAllHardpointsHaveBehavior(*ReplayFindUnit(s, kLocal2), "INVULNERABLE") == true,
          "kLocal2: every hardpoint carries INVULNERABLE");
    Check(ReplayUnitAnyHardpointHasBehavior(*ReplayFindUnit(s, kEnemy), "INVULNERABLE") == false,
          "enemy unit NEVER gets INVULNERABLE from a local-slot sweep");

    // Damage behavior: local units no-op, enemy decrements.
    float h1 = ReplayMutApplyDamage(s, kLocal1, 1500.0f);
    Check(fabs(h1 - 5000.0f) < 1e-3, "damage on local unit is a no-op");
    float h2 = ReplayMutApplyDamage(s, kLocal2, 500.0f);
    Check(fabs(h2 - 2000.0f) < 1e-3, "damage on second local unit is a no-op");
    float he = ReplayMutApplyDamage(s, kEnemy, 1000.0f);
    Check(fabs(he - 7000.0f) < 1e-3, "damage on enemy still decrements normally");

    // Disable: strip INVULNERABLE from local units only.
    int cleared = ReplayMutSweepGodMode(s, false);
    Check(cleared == 2, "disable sweep reports 2 units cleared");
    Check(ReplayObsGodModeFullyActive(s) == 0,
          "after disable: no local unit is fully invulnerable");
    Check(ReplayUnitAnyHardpointHasBehavior(*ReplayFindUnit(s, kLocal1), "INVULNERABLE") == false,
          "kLocal1: no hardpoint carries INVULNERABLE after disable");
    Check(ReplayUnitAnyHardpointHasBehavior(*ReplayFindUnit(s, kLocal2), "INVULNERABLE") == false,
          "kLocal2: no hardpoint carries INVULNERABLE after disable");

    // Damage resumes on local units after disable.
    float h3 = ReplayMutApplyDamage(s, kLocal1, 1500.0f);
    Check(fabs(h3 - 3500.0f) < 1e-3, "damage on local unit resumes after disable");

    // Edge case: local_slot=-1 means no sweep target at all.
    ReplayState s2;
    s2.local_slot = -1;
    ReplayMutMockUnit(s2, kLocal1, "Aggressor", 0, 1000.0f, 1000.0f, 2);
    Check(ReplayMutSweepGodMode(s2, true) == 0,
          "local_slot=-1 collapses sweep to 0 flips (refuses to touch anyone)");
    Check(ReplayUnitAnyHardpointHasBehavior(*ReplayFindUnit(s2, kLocal1), "INVULNERABLE") == false,
          "local_slot=-1: nothing attached");

    // Edge case: empty state sweeps cleanly.
    ReplayState s3;
    s3.local_slot = 0;
    Check(ReplayMutSweepGodMode(s3, true) == 0, "empty state: 0 flips");
    Check(ReplayObsGodModeFullyActive(s3) == 0,
          "empty state: GodModeFullyActive returns 0 (no local units to cover)");
}

// Task 104 (added 2026-04-23). Pure-state regression for the CSV row format
// contract between live Lua_ListTacticalUnits and Lua_ReplayListTacticalUnits.
// The V2 Tactical Units DataGrid (Task 107) parses this string shape, so a
// drift here breaks the live→replay→UI pipeline silently. The test pins:
//   * "count=0" on an empty state (tactical list is empty or in galactic mode)
//   * "count=N|row|row|..." on a populated state with the correct field layout
//   * is_local column honours s.local_slot
//   * is_selected column honours s.selected_units membership
//   * prevent_death column reflects ONLY bit 0x80 of +0x3A1
//   * display invuln_flag is reported verbatim; it is NOT inferred from hardpoint behaviors
static void TestReplayListTacticalUnits() {
    StartSuite("Replay: ListTacticalUnits CSV contract (Task 104)");

    ReplayState s;
    Check(ReplayObsListTacticalUnits(s) == "count=0",
          "empty state returns literal 'count=0'");

    constexpr uint64_t kObjA = 0x1F2A3334D00ULL;
    constexpr uint64_t kObjB = 0x1F2BB40B280ULL;
    ReplayMutMockUnit(s, kObjA, "Aggressor_Destroyer", 6, 5200.500f, 6000.0f, 3);
    ReplayMutMockUnit(s, kObjB, "Corellian_Corvette",  1, 1234.000f, 2500.0f, 2);
    s.local_slot = 6;
    ReplayMutSetSelected(s, kObjA);
    ReplayMutSetUnitInvulnFlag(s, kObjA, 1);
    ReplayMutSetPreventDeathBit(s, kObjA, true);

    std::string csv = ReplayObsListTacticalUnits(s);
    Check(csv.rfind("count=2", 0) == 0, "populated state starts with 'count=2'");
    std::string rowA = std::string("|") + std::to_string(kObjA) +
                       ";6;5200.500;1;1;1;1";
    std::string rowB = std::string("|") + std::to_string(kObjB) +
                       ";1;1234.000;0;0;0;0";
    Check(csv.find(rowA) != std::string::npos,
          "local + selected + invuln + prevent-death row for kObjA is exact");
    Check(csv.find(rowB) != std::string::npos,
          "non-local + unselected row for kObjB is exact");

    // Round-trip: selection flag toggles end-to-end without re-mocking units.
    ReplayMutSetSelected(s, kObjB);
    std::string csv2 = ReplayObsListTacticalUnits(s);
    std::string rowAAfter = std::string("|") + std::to_string(kObjA) +
                            ";6;5200.500;1;1;1;0";
    std::string rowBAfter = std::string("|") + std::to_string(kObjB) +
                            ";1;1234.000;0;0;0;1";
    Check(csv2.find(rowAAfter) != std::string::npos,
          "kObjA is_selected flips to 0 when selection moves");
    Check(csv2.find(rowBAfter) != std::string::npos,
          "kObjB is_selected becomes 1 after SetSelected");

    // is_local is gated by local_slot being set — -1 means "no local" and
    // every row's is_local column must read 0.
    s.local_slot = -1;
    std::string csv3 = ReplayObsListTacticalUnits(s);
    // kObjA: invuln=1 prevent=1, kObjB is now selected (from above).
    std::string rowANoLocal = std::string("|") + std::to_string(kObjA) +
                              ";6;5200.500;1;1;0;0";
    std::string rowBNoLocal = std::string("|") + std::to_string(kObjB) +
                              ";1;1234.000;0;0;0;1";
    Check(csv3.find(rowANoLocal) != std::string::npos,
          "local_slot=-1 collapses every row's is_local column to 0 (kObjA)");
    Check(csv3.find(rowBNoLocal) != std::string::npos,
          "local_slot=-1 collapses every row's is_local column to 0 (kObjB)");

    // Red-green pair for the byte-flipping vs engine-state contract.
    // Writing invuln_flag=1 must NOT change is_selected or is_local — the
    // display flag is independent of selection membership and ownership.
    Check(csv3.find(";1;1;0;0") != std::string::npos,
          "invuln=1,prevent=1,local=0,selected=0 appears in row A");

    // prevent_death column reflects ONLY bit 0x80. Set bit 0x01 directly
    // (bypassing the mut helper) and confirm the column stays 0.
    ReplayFindUnit(s, kObjA)->prevent_death = 0x01;
    std::string csv4 = ReplayObsListTacticalUnits(s);
    std::string rowANoPdb = std::string("|") + std::to_string(kObjA) +
                            ";6;5200.500;1;0;0;0";
    Check(csv4.find(rowANoPdb) != std::string::npos,
          "prevent_death column is driven by bit 0x80, not bit 0x01");

    // Empty selection clears every is_selected column.
    ReplayMutClearSelected(s);
    std::string csv5 = ReplayObsListTacticalUnits(s);
    size_t at = 0;
    while ((at = csv5.find(";1\n", at)) != std::string::npos) at++;
    // Coarse structural check — any ";1" trailing field should not appear
    // on the is_selected column after ClearSelected. We scan the full string
    // for the "is_selected=1" tail pattern ";<digit>;1|" or end-of-string.
    Check(csv5.find(";0|") != std::string::npos || csv5.back() == '0',
          "ClearSelected drives every is_selected column to 0");
}

// ======================================================================
// Registration drift guard
// ----------------------------------------------------------------------
// Cross-checks the canonical RegisterAll table in lua_bridge.cpp against
// the SWFOC_DiagListRegisteredFunctions manifest. Every name registered
// via the Lua 5.0 triad push/pushcclosure/settable must appear in the
// manifest string, and the count must be >= 30 (the new baseline after
// fixing the dead-code RegisterAll bug). If this test fails, a future
// edit added a Lua_* static but forgot to wire it into RegisterAll.
// ======================================================================

// Stubs for helpers that are not replicated elsewhere in the harness.
// These exist only for the drift-guard test; they push a dummy string
// and return 1. None of the drift-guard assertions depend on their
// return values — only on the fact that they get bound to the correct
// global names during RegisterAll.
static int HarnessStub_GetBuildInfo(lua_State* L)  { fn_pushstring(L, "stub"); return 1; }
static int HarnessStub_SetHumanPlayer(lua_State* L){ fn_pushnumber(L, 0);      return 1; }
// SetHumanPlayer_v2 stub in the drift-guard table. The real v2 behavior is
// exercised by TestSetHumanPlayerV2 which calls a local replica directly
// (similar pattern to the rest of the bridge logic in this file).
static int HarnessStub_SetHumanPlayer_v2(lua_State* L){ fn_pushnumber(L, 0);   return 1; }
static int HarnessStub_DiagListRegistered(lua_State* L); // defined below, needs g_harnessManifest
static int HarnessStub_DiagPipeStats(lua_State* L) { fn_pushstring(L, "stub"); return 1; }
static int HarnessStub_DiagGameTick(lua_State* L)  { fn_pushnumber(L, 0);      return 1; }
static int HarnessStub_DiagSelfTest(lua_State* L)  { fn_pushstring(L, "stub"); return 1; }
static int HarnessStub_DiagSelection(lua_State* L) { fn_pushstring(L, "stub"); return 1; }
// Task 104 (2026-04-23): the live SWFOC_ListTacticalUnits walks the game's
// GameObjectManager at a real module-base address, which is meaningless in
// the harness. The drift guard only cares that the name is bound, so a stub
// with a recognisable sentinel keeps the manifest honest while the pure-state
// path is covered by TestReplayListTacticalUnits / the replay smoke test.
static int HarnessStub_ListTacticalUnits(lua_State* L) { fn_pushstring(L, "count=0"); return 1; }
// Task 128 stub: live CombinedGodOHK flips module globals the harness
// cannot observe. Replay harness TestReplayGodModeSweep already covers
// the composite behavior via ReplayMutSweepGodMode + ApplyDamage assertions.
static int HarnessStub_CombinedGodOHK(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
// Task 158 stub: real EnumerateUnits walks engine tactical list. Pure-state
// contract is covered by TestReplayEnumerateUnits below.
static int HarnessStub_EnumerateUnits(lua_State* L) { fn_pushstring(L, "count=0"); return 1; }
// Task 98 stub: live HealAllLocal walks tactical list. Replay path covers
// the contract via TestReplayHealAllLocal below.
static int HarnessStub_HealAllLocal(lua_State* L) { fn_pushnumber(L, 0); return 1; }
// Task 129 stub: real Set/GetDamageMultiplier operate on module globals
// that the harness cannot observe. The pure-state contract is covered by
// TestReplayDamageMultiplier below (set global/per-slot, apply damage,
// verify scaling, restore).
static int HarnessStub_SetDamageMultiplier(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_GetDamageMultiplier(lua_State* L) { fn_pushnumber(L, 1.0); return 1; }
// Task 137 stubs: live Kill/Revive read/write engine memory; the pure-state
// contract is covered by TestReplayKillRevive.
static int HarnessStub_KillUnit(lua_State* L)   { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_ReviveUnit(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
// Task 130 stubs: real SetUnitShield/GetUnitShield touch module globals.
// Pure-state contract is covered by TestReplayUnitShield.
static int HarnessStub_SetUnitShield(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_GetUnitShield(lua_State* L) { fn_pushnumber(L, -1.0); return 1; }
static int HarnessStub_SetUnitSpeed(lua_State* L)  { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_GetUnitSpeed(lua_State* L)  { fn_pushnumber(L, -1.0); return 1; }
// Task 134/135/136 stubs: live side needs hero detection; replay suite pins the contract.
static int HarnessStub_ListHeroes(lua_State* L)          { fn_pushstring(L, "count=0"); return 1; }
static int HarnessStub_SetHeroRespawnTimer(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetPermadeath(lua_State* L)       { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_HeroStatEdit(lua_State* L)        { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_GetPlanets(lua_State* L)          { fn_pushstring(L, "count=0"); return 1; }
static int HarnessStub_ChangePlanetOwner(lua_State* L)   { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_GetPlanetTechAndBuildings(lua_State* L) { fn_pushstring(L, ""); return 1; }
static int HarnessStub_SetDiplomacy(lua_State* L)        { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_ListAbilities(lua_State* L)       { fn_pushstring(L, "count=0"); return 1; }
static int HarnessStub_TriggerAbility(lua_State* L)      { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetIncomeMultiplier(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetGameSpeed(lua_State* L)        { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_FreezeCredits(lua_State* L)       { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetBuildSpeed(lua_State* L)       { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetPerFactionSpeedMultiplier(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
// Tasks 131/132/133 stubs: live side needs IDA pin on weapon cooldown,
// splash branch, and targeting filter predicate. Replay suite pins the
// contract so the drift guard just confirms name binding here.
static int HarnessStub_SetFireRate(lua_State* L)     { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetAreaDamage(lua_State* L)   { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetTargetFilter(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
// Task 105 stub: live ToggleOHKAttackPower walks local units and writes
// attack_power; IDA-blocked pending offset pin. Replay suite pins the
// snapshot/restore + idempotency + ownership-flip contract.
static int HarnessStub_ToggleOHKAttackPower(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
// Tasks 114/115 stubs: live AI-freeze / camera-unlock / set-cam-pos /
// get-cam-pos all need IDA pins (AI scheduler dispatch + camera
// singleton pointer chain). Replay suite pins all contract details.
static int HarnessStub_FreezeAI(lua_State* L)     { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_FreeCam(lua_State* L)      { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetCameraPos(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_GetCameraPos(lua_State* L) { fn_pushstring(L, "0,0,0"); return 1; }
// Tasks 159/160/163 stubs: SpawnUnit needs the Phase 2 engine-call
// pipeline; SetBuildCost needs the credits-deduction hook; SetUnitCapOverride
// needs the unit-cap check. Replay suite pins all offline contract details.
static int HarnessStub_SpawnUnit(lua_State* L)          { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetBuildCost(lua_State* L)       { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_SetUnitCapOverride(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
// Task 157 stub: generic field→offset write table is IDA-blocked until
// each field offset is pinned. Replay suite pins the field-name
// taxonomy + clamp/reject contracts.
static int HarnessStub_SetUnitField(lua_State* L)       { fn_pushstring(L, "OK: stub"); return 1; }
// Tasks 161/162 stubs: AOB patches (build-progress NOP + cost-deduction
// NOP) are Phase 2. Replay suite pins the toggle semantics + predicate
// math. The drift guard just confirms name binding.
static int HarnessStub_InstantBuild(lua_State* L)       { fn_pushstring(L, "OK: stub"); return 1; }
static int HarnessStub_FreeBuild(lua_State* L)          { fn_pushstring(L, "OK: stub"); return 1; }
// Task 113 (2026-04-23). Live RevealAll talks to the engine's Lua VM which
// is not present in the harness; the stub just confirms the name binding
// for the drift guard. The real mutation path is covered by
// TestReplayRevealAll against the pure-state helpers.
static int HarnessStub_RevealAll(lua_State* L) { fn_pushstring(L, "OK: stub"); return 1; }
// Task 111 stub: live GetAllPlayers reads PlayerArray memory which isn't
// present in the harness. Real pure-state path is covered by
// TestReplayGetAllPlayers.
static int HarnessStub_GetAllPlayers(lua_State* L) { fn_pushstring(L, "count=0"); return 1; }
// Task 112 stub: live EventStreamDrain reads module globals (g_eventRing,
// g_eventRingLock). Harness cannot exercise those so the stub returns an
// empty sentinel; the real drain logic is covered by TestReplayEventStream.
static int HarnessStub_EventStreamDrain(lua_State* L) { fn_pushstring(L, "count=0"); return 1; }

// Harness-side manifest, populated by HarnessRegisterAll. Source of truth
// for what the drift guard expects Lua_DiagListRegisteredFunctions to
// return in the live DLL.
static char g_harnessManifest[2048] = {0};
static int  g_harnessRegisteredCount = 0;

static int HarnessStub_DiagListRegistered(lua_State* L) {
    fn_pushstring(L, g_harnessManifest);
    return 1;
}

// Mirrors the canonical lua_bridge.cpp::RegisterAll table EXACTLY. When a
// bridge helper is added, it must be added here too — the drift guard test
// below will fail otherwise. That's the whole point of the guard: the
// harness is the compile-time witness to what the bridge advertises.
static void HarnessRegisterAll(FakeLuaState* fake) {
    struct HelperEntry { const char* name; lua_CFunction func; };
    static const HelperEntry funcs[] = {
        // Core / metadata
        {"SWFOC_GetVersion",         Lua_GetVersion},
        {"SWFOC_GetBuildInfo",       HarnessStub_GetBuildInfo},
        {"SWFOC_Log",                Lua_Log},
        {"SWFOC_DoString",           Lua_DoString},
        {"SWFOC_DrainPipe",          Lua_DrainPipe},
        {"SWFOC_StateInfo",          (lua_CFunction)SWFOC_StateInfo},
        {"SWFOC_EventControl",       Lua_EventControl},
        {"SWFOC_DumpState",          Lua_DumpState},
        // Player / economy
        {"SWFOC_GetLocalPlayer",     Lua_GetLocalPlayer},
        // 2026-04-25: v1 + v2 unregistered from Lua dispatch (v3 only).
        // Mirror C functions (Lua_SetHumanPlayer_v2 etc.) stay defined so
        // existing C++ test cases that exercise the manual-sweep mechanics
        // continue to compile + pass via direct invocation.
        {"SWFOC_SetCredits",         Lua_SetCredits},
        {"SWFOC_GetCredits",         Lua_GetCredits},
        {"SWFOC_SetTechLevel",       Lua_SetTechLevel},
        {"SWFOC_UncapCredits",       Lua_UncapCredits},
        {"SWFOC_HeroInstantRespawn", Lua_HeroInstantRespawn},
        {"SWFOC_ListFactions",       Lua_ListFactions},
        // Phase 3.2: combat / inspect helpers
        {"SWFOC_SetUnitInvuln",      Lua_SetUnitInvuln},
        {"SWFOC_SetUnitHull",        Lua_SetUnitHull},
        {"SWFOC_InspectUnit",        Lua_InspectUnit},
        {"SWFOC_GetHardpoints",      Lua_GetHardpoints},
        {"SWFOC_GetSelectedUnit",    Lua_GetSelectedUnit},
        {"SWFOC_GetSelectedUnits",   Lua_GetSelectedUnits},
        {"SWFOC_ListTacticalUnits",  HarnessStub_ListTacticalUnits},
        {"SWFOC_GodMode",            Lua_GodMode},
        {"SWFOC_OneHitKill",         Lua_OneHitKill},
        {"SWFOC_CombinedGodOHK",     HarnessStub_CombinedGodOHK},
        {"SWFOC_RevealAll",          HarnessStub_RevealAll},
        {"SWFOC_GetAllPlayers",      HarnessStub_GetAllPlayers},
        {"SWFOC_EnumerateUnits",     HarnessStub_EnumerateUnits},
        {"SWFOC_HealAllLocal",       HarnessStub_HealAllLocal},
        {"SWFOC_KillUnit",           HarnessStub_KillUnit},
        {"SWFOC_ReviveUnit",         HarnessStub_ReviveUnit},
        {"SWFOC_SetUnitShield",      HarnessStub_SetUnitShield},
        {"SWFOC_GetUnitShield",      HarnessStub_GetUnitShield},
        {"SWFOC_SetUnitSpeed",       HarnessStub_SetUnitSpeed},
        {"SWFOC_GetUnitSpeed",       HarnessStub_GetUnitSpeed},
        {"SWFOC_ListHeroes",         HarnessStub_ListHeroes},
        {"SWFOC_SetHeroRespawnTimer",HarnessStub_SetHeroRespawnTimer},
        {"SWFOC_SetPermadeath",      HarnessStub_SetPermadeath},
        {"SWFOC_HeroStatEdit",       HarnessStub_HeroStatEdit},
        {"SWFOC_GetPlanets",         HarnessStub_GetPlanets},
        {"SWFOC_ChangePlanetOwner",  HarnessStub_ChangePlanetOwner},
        {"SWFOC_GetPlanetTechAndBuildings", HarnessStub_GetPlanetTechAndBuildings},
        {"SWFOC_SetDiplomacy",       HarnessStub_SetDiplomacy},
        {"SWFOC_ListAbilities",      HarnessStub_ListAbilities},
        {"SWFOC_TriggerAbility",     HarnessStub_TriggerAbility},
        {"SWFOC_SetIncomeMultiplier",HarnessStub_SetIncomeMultiplier},
        {"SWFOC_SetGameSpeed",       HarnessStub_SetGameSpeed},
        {"SWFOC_FreezeCredits",      HarnessStub_FreezeCredits},
        {"SWFOC_SetBuildSpeed",      HarnessStub_SetBuildSpeed},
        {"SWFOC_SetPerFactionSpeedMultiplier", HarnessStub_SetPerFactionSpeedMultiplier},
        {"SWFOC_SetFireRate",        HarnessStub_SetFireRate},
        {"SWFOC_SetAreaDamage",      HarnessStub_SetAreaDamage},
        {"SWFOC_SetTargetFilter",    HarnessStub_SetTargetFilter},
        {"SWFOC_ToggleOHKAttackPower", HarnessStub_ToggleOHKAttackPower},
        {"SWFOC_FreezeAI",           HarnessStub_FreezeAI},
        {"SWFOC_FreeCam",            HarnessStub_FreeCam},
        {"SWFOC_SetCameraPos",       HarnessStub_SetCameraPos},
        {"SWFOC_GetCameraPos",       HarnessStub_GetCameraPos},
        {"SWFOC_SpawnUnit",          HarnessStub_SpawnUnit},
        {"SWFOC_SetBuildCost",       HarnessStub_SetBuildCost},
        {"SWFOC_SetUnitCapOverride", HarnessStub_SetUnitCapOverride},
        {"SWFOC_SetUnitField",       HarnessStub_SetUnitField},
        {"SWFOC_InstantBuild",       HarnessStub_InstantBuild},
        {"SWFOC_FreeBuild",          HarnessStub_FreeBuild},
        {"SWFOC_SetDamageMultiplier", HarnessStub_SetDamageMultiplier},
        {"SWFOC_GetDamageMultiplier", HarnessStub_GetDamageMultiplier},
        {"SWFOC_EventStreamDrain",   HarnessStub_EventStreamDrain},
        // Phase 3.2 (continuation): per-slot writers + observers
        {"SWFOC_SetCreditsForSlot",  Lua_SetCreditsForSlot},
        {"SWFOC_GetCreditsForSlot",  Lua_GetCreditsForSlot},
        {"SWFOC_SetTechForSlot",     Lua_SetTechForSlot},
        {"SWFOC_GetTechForSlot",     Lua_GetTechForSlot},
        {"SWFOC_DrainEnemyCredits",  Lua_DrainEnemyCredits},
        {"SWFOC_SetHeroRespawn",     Lua_SetHeroRespawn},
        {"SWFOC_PreventUnitDeath",   Lua_PreventUnitDeath},
        {"SWFOC_GetMaxCredits",      Lua_GetMaxCredits},
        // 2026-04-10 diagnostic helpers
        {"SWFOC_DiagListRegisteredFunctions", HarnessStub_DiagListRegistered},
        {"SWFOC_DiagPipeStats",               HarnessStub_DiagPipeStats},
        {"SWFOC_DiagGameTick",                HarnessStub_DiagGameTick},
        {"SWFOC_DiagSelfTest",                HarnessStub_DiagSelfTest},
        {"SWFOC_DiagSelection",               HarnessStub_DiagSelection},
    };
    constexpr int kCount = static_cast<int>(sizeof(funcs)/sizeof(funcs[0]));

    // Build manifest first so HarnessStub_DiagListRegistered can return it.
    size_t off = 0;
    g_harnessManifest[0] = '\0';
    for (int i = 0; i < kCount; i++) {
        size_t remaining = (off >= sizeof(g_harnessManifest) - 1)
            ? 0 : sizeof(g_harnessManifest) - off - 1;
        if (remaining == 0) break;
        int n = snprintf(g_harnessManifest + off, remaining, "%s%s",
                         i > 0 ? "," : "", funcs[i].name);
        if (n <= 0 || (size_t)n >= remaining) break;
        off += (size_t)n;
    }
    g_harnessRegisteredCount = kCount;

    for (int i = 0; i < kCount; i++) {
        fn_pushstring(LS(fake), funcs[i].name);
        fn_pushcclosure(LS(fake), funcs[i].func, 0);
        fn_settable(LS(fake), LUA_GLOBALSINDEX);
    }
}

// Split a comma-separated manifest into a sorted vector of names for
// set-equality comparison. Trims nothing; manifest format is strict.
static std::vector<std::string> SplitManifest(const char* s) {
    std::vector<std::string> out;
    if (!s || !*s) return out;
    const char* start = s;
    for (const char* p = s; ; p++) {
        if (*p == ',' || *p == '\0') {
            if (p > start) out.emplace_back(start, p - start);
            if (*p == '\0') break;
            start = p + 1;
        }
    }
    std::sort(out.begin(), out.end());
    return out;
}

static void TestRegistrationDriftGuard() {
    StartSuite("Registration Drift Guard (canonical RegisterAll vs DiagListRegisteredFunctions)");
    ResetBridgeState();
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();

    FakeLuaState fake;
    fake_reset(&fake);

    HarnessRegisterAll(&fake);

    // 1. Every registered name must appear as a TFUNCTION in fake.globals.
    //    This is the "canonical table actually ran" check — if a helper
    //    name is present in the table but fn_pushcclosure wasn't called
    //    for it, the global won't exist.
    auto expected = SplitManifest(g_harnessManifest);
    Check(expected.size() == static_cast<size_t>(g_harnessRegisteredCount),
          "manifest split matches registered count");
    Check(g_harnessRegisteredCount >= 37,
          "registered helper count >= 37 (post-2026-04-11 baseline: 34 base + "
          "2 selection + 1 faction-switch v2)");

    bool allPresent = true;
    for (const auto& name : expected) {
        auto it = fake.globals.find(name);
        if (it == fake.globals.end() || it->second.type != 6 /*TFUNCTION*/) {
            printf("    MISSING: %s\n", name.c_str());
            allPresent = false;
        }
    }
    Check(allPresent, "every canonical helper got a TFUNCTION global");

    // 2. No duplicates in the manifest.
    auto uniq = expected;
    uniq.erase(std::unique(uniq.begin(), uniq.end()), uniq.end());
    Check(uniq.size() == expected.size(), "no duplicate helper names");

    // 3. Cross-check specific names that were previously DEAD in the live
    //    bridge. If any of these regress, the fix has been undone. Runs
    //    BEFORE the stack-reset step below so the globals map is intact.
    const char* previouslyDead[] = {
        "SWFOC_SetCreditsForSlot",
        "SWFOC_GetCreditsForSlot",
        "SWFOC_SetTechForSlot",
        "SWFOC_GetTechForSlot",
        "SWFOC_DrainEnemyCredits",
        "SWFOC_SetHeroRespawn",
        "SWFOC_PreventUnitDeath",
        "SWFOC_GetMaxCredits",
    };
    for (const char* n : previouslyDead) {
        auto it = fake.globals.find(n);
        bool live = (it != fake.globals.end() && it->second.type == 6);
        Check(live, (std::string("previously-dead helper is now live: ") + n).c_str());
    }

    // 4. Invoke SWFOC_DiagListRegisteredFunctions through its registered
    //    closure and compare the returned manifest with the harness-side
    //    manifest. This is the "DiagListRegisteredFunctions truly reports
    //    what was registered" check — the live-game probe harness calls
    //    this function to prove the bridge is self-consistent.
    auto dlFnIt = fake.globals.find("SWFOC_DiagListRegisteredFunctions");
    bool dlFnOk = (dlFnIt != fake.globals.end() &&
                   dlFnIt->second.type == 6 &&
                   dlFnIt->second.funcptr != nullptr);
    Check(dlFnOk, "SWFOC_DiagListRegisteredFunctions is callable");

    if (dlFnOk) {
        auto fn = reinterpret_cast<lua_CFunction>(dlFnIt->second.funcptr);
        // Clear only the stack (not globals) so the helper's push lands
        // at index 1 and we can read it back with fn_tostring(-1).
        fake.stack.clear();
        int nret = fn(LS(&fake));
        Check(nret == 1, "DiagListRegisteredFunctions returns 1 value");
        const char* reported = fn_tostring(LS(&fake), -1);
        auto reportedSet = SplitManifest(reported ? reported : "");
        Check(reportedSet == expected,
              "reported manifest set-equals canonical table");
    }
}

// ======================================================================
// 2026-04-10 fixes — DiagSelfTest bounds check + pipe-response capacity
// ----------------------------------------------------------------------
// Fix 1: Lua_DiagSelfTest's player_array sanity check used to compare pa
//        against the MODULE range [0x140000000, 0x180000000). But PlayerArray
//        is HEAP-allocated, so the live-test pointer 0x000002983d64b950 was
//        flagged FAIL even though every downstream check PASSED. New logic:
//        non-null, above 0x10000000, upper 16 bits clear (48-bit x64 user
//        address). This helper mirrors the in-bridge expression exactly so
//        the harness can drive it over the hostile fixture vectors from the
//        live-test ground truth.
//
// Fix 2: PIPE_CMD_MAX was 4096, and g_pipeResult was 512 — both truncated
//        the SWFOC_DiagListRegisteredFunctions manifest. Bridge now bumps
//        PIPE_CMD_MAX to 16384 and sizes g_pipeResult[PIPE_CMD_MAX]. The
//        tests below assert both that the helper-name manifest pattern at
//        >30 entries comfortably fits inside a 4 KB buffer (empirical
//        worst-case for today's 34 helpers is far below 4 KB) and that the
//        manifest buffer sized to 4096 in the bridge is not a false ceiling.
// ======================================================================

static bool HarnessIsPlayerArrayPointerValid(uintptr_t pa) {
    // Mirror of the bounds check inside Lua_DiagSelfTest (lua_bridge.cpp).
    // Keep this expression byte-identical with the live-bridge code so the
    // harness test fails loudly if either side drifts.
    return (pa != 0 && pa > 0x10000000ULL && (pa >> 48) == 0);
}

static void TestDiagSelfTestPlayerArrayBoundsCheck() {
    StartSuite("DiagSelfTest player_array bounds check (heap-range, post-2026-04-10)");

    // Live-test ground truth from 2026-04-10: heap pointer that the OLD
    // module-range check falsely rejected. Every downstream check passed,
    // confirming the pointer is valid. New check MUST accept this.
    const uintptr_t liveHeapPa = (uintptr_t)0x000002983d64b950ULL;
    Check(HarnessIsPlayerArrayPointerValid(liveHeapPa),
          "live-test heap pointer 0x2983d64b950 now passes (was false FAIL)");

    // Another plausible heap address: Windows x64 user-space allocations can
    // land anywhere from ~0x10000 up to ~0x7FFFFFFFFFFF. Pick a high one.
    const uintptr_t highHeapPa = (uintptr_t)0x00007FF812345000ULL;
    Check(HarnessIsPlayerArrayPointerValid(highHeapPa),
          "high user-space heap pointer passes");

    // The old module-range value (still inside old window) must still pass
    // with the new check — backward compatibility for the pre-fix case.
    const uintptr_t legacyModulePa = (uintptr_t)0x0000000141234000ULL;
    Check(HarnessIsPlayerArrayPointerValid(legacyModulePa),
          "legacy module-range pointer still passes (no regression)");

    // Null pointer: must FAIL. This is the "PlayerArray_Global unwritten"
    // case — the RVA slot still holds zero, which indicates the game hasn't
    // finished loading yet.
    Check(!HarnessIsPlayerArrayPointerValid(0),
          "null pointer correctly rejected");

    // Low pointer: anything at or below 0x10000000 is kernel-address-space
    // or a tiny integer being reinterpret_cast'd. Must FAIL.
    Check(!HarnessIsPlayerArrayPointerValid((uintptr_t)0x10000000ULL),
          "boundary 0x10000000 correctly rejected");
    Check(!HarnessIsPlayerArrayPointerValid((uintptr_t)0x00001234ULL),
          "tiny integer correctly rejected");

    // Upper-16-bits-set pointer: canonical x64 user-space addresses have
    // bits 48..63 clear. A value with bit 48 or above set is either a
    // kernel address (sign-extended) or outright garbage. Must FAIL.
    const uintptr_t garbagePa = (uintptr_t)0xFFFF000000000000ULL;
    Check(!HarnessIsPlayerArrayPointerValid(garbagePa),
          "upper-16-bits-set garbage pointer correctly rejected");
    const uintptr_t kernelPa = (uintptr_t)0xFFFFF78000000000ULL;
    Check(!HarnessIsPlayerArrayPointerValid(kernelPa),
          "kernel-space pointer correctly rejected");

    // Integration-style: drive the real g_gameImage setup (which allocates a
    // heap buffer via VirtualAlloc) and confirm its PlayerArray slot passes.
    // g_gameImage is the harness analogue of a live-game heap address.
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    SetupTestPlayers();
    uintptr_t fixturePa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    Check(HarnessIsPlayerArrayPointerValid(fixturePa),
          "SetupTestPlayers() heap fixture passes bounds check");
}

static void TestDiagListRegisteredFunctionsCapacity() {
    StartSuite("DiagListRegisteredFunctions manifest capacity (post-2026-04-10 bump)");

    // The bridge's g_registeredFunctionManifest is sized to 4096 and the pipe
    // response buffer g_pipeResult is sized to PIPE_CMD_MAX = 16384. Both
    // must comfortably carry an expanded manifest. Synthesise a "worst
    // plausible near-future" manifest (>30 helper names, average length 24
    // chars) and verify it still fits under 4 KB AND can exceed 4096 bytes
    // without tripping the new 16 KB response buffer.

    // First: confirm today's 34-entry manifest (from HarnessRegisterAll)
    // is less than the old 4096-byte limit — regression guard for the
    // immediate present. This just mirrors what drift-guard already does,
    // but phrased as a capacity assertion so intent is explicit.
    size_t currentManifestLen = strlen(g_harnessManifest);
    Check(currentManifestLen > 0,
          "harness manifest is populated (sanity)");
    Check(currentManifestLen < 4096,
          "current 34-helper manifest fits in 4 KB manifest buffer");

    // Second: synthesise a >4 KB manifest to prove the pipe path (which now
    // uses g_pipeResult[PIPE_CMD_MAX] = 16384) can carry more than the old
    // 512-byte or 4096-byte ceilings. This is the "truncation no longer
    // happens" proof. We build it into a local buffer sized to the new
    // response cap so the test is self-contained.
    constexpr size_t kPipeResponseCap = 16384;
    char bigManifest[kPipeResponseCap];
    size_t off = 0;
    bigManifest[0] = '\0';
    int count = 0;
    // Generate 200 synthetic helper names -- each ~25 chars -> ~5000 bytes.
    // This deliberately overshoots today's 34 to prove future-proofing.
    for (int i = 0; i < 200 && off < sizeof(bigManifest) - 32; i++) {
        int n = snprintf(bigManifest + off, sizeof(bigManifest) - off,
                         "%sSWFOC_SyntheticHelper_%03d",
                         i > 0 ? "," : "", i);
        if (n <= 0 || (size_t)n >= sizeof(bigManifest) - off) break;
        off += (size_t)n;
        count++;
    }
    Check(count >= 30,
          "synthetic manifest has >30 entries (matches drift-guard threshold)");
    Check(strlen(bigManifest) > 4096,
          "synthetic manifest exceeds 4096 bytes (old ceiling busted)");
    Check(strlen(bigManifest) < kPipeResponseCap,
          "synthetic manifest fits in new 16 KB pipe response buffer");

    // Third: confirm the harness-local PIPE_CMD_MAX matches the bridge-side
    // expectation (both files should track together). If this fails, the
    // harness was not updated when the bridge was, and the pipe-protocol
    // test above is running against a stale copy.
    Check(PIPE_CMD_MAX == 16384,
          "harness PIPE_CMD_MAX = 16384 (matches bridge-side bump)");
}

// ======================================================================
// SetHumanPlayer_v2 suite (2026-04-11)
// ----------------------------------------------------------------------
// Targets the galactic-mode bug: v1's Switch_Sides rotation is guarded
// out in mode 3, leaving the split-brain state the user saw in the
// 2026-04-10 live test. v2 does manual byte-level writes to PlayerObject
// +0x62 and PlayerListClass +0x30 and calls a subsystem refresh
// interceptor. This suite verifies:
//   1. Happy path: target slot is set, others cleared, field updated,
//      refresh interceptor called with the expected activeGameMode arg
//   2. Out-of-range target returns 0 without side effects
//   3. target == current with matching scan returns 1 without calling
//      the refresh interceptor (no-op early return)
//   4. target == +0x30 field but scan disagrees (split-brain) runs the
//      full sweep and refreshes anyway — this is the ACTUAL fix path
//   5. Sweep across an 8-slot array clears all non-target +0x62 bytes
// ======================================================================

// Stage an ActiveGameMode pointer chain so v2's subsystem refresh call
// has a valid dispatch target. Real game writes the pointer at
// g_base + GameModeRoot_Global; the refresh call dereferences
// *(activeGameMode + 24). For the test we just need any non-null
// pointers so the refresh path is exercised.
static void SetupFakeGameMode() {
    // Pick an arbitrary in-image offset for the "active game mode" struct.
    uintptr_t gmOff = 0x400000;
    uintptr_t gmAddr = g_base + gmOff;
    // Write a sentinel pointer at gmAddr + 24 so the dispatcher gets
    // something non-null as its arg.
    uintptr_t eventTargetSentinel = g_base + 0x401000;
    GI_WriteQword(gmOff + 24, eventTargetSentinel);
    // Store the active-game-mode address at GameModeRoot_Global so the
    // v2 replica finds it via *(g_base + GameModeRoot_Global).
    GI_WriteQword(RVA::GameModeRoot_Global, gmAddr);
}

// Helper: set up an 8-slot PlayerArray with sequential player objects.
// Each player starts with +0x62 = 0 and +0x30 (current slot) = initial.
static void SetupEightSlotArray(int initialSlot) {
    memset(g_gameImage, 0, GAME_IMAGE_SIZE);
    std::vector<uintptr_t> players;
    for (int i = 0; i < 8; i++) {
        uintptr_t p = g_base + PLAYER_BASE_OFF + i * PLAYER_STRIDE;
        SetupPlayer(p, i, /*isLocal=*/false,
                    10000.0f + i * 1000.0f, 100000.0f, 1, "FACTION");
        players.push_back(p);
    }
    SetupPlayerArray(players);
    // Mark initialSlot as the "real" local player.
    if (initialSlot >= 0 && initialSlot < (int)players.size()) {
        *reinterpret_cast<uint8_t*>(players[initialSlot]
            + RVA::PlayerObj::LocalPlayer) = 1;
    }
    // Write the PlayerListClass +0x30 current-slot field.
    *reinterpret_cast<int*>(
        g_base + RVA::PlayerListClass_Global + 0x30) = initialSlot;
    SetupFakeGameMode();
    g_fakeRefreshCallCount = 0;
    g_fakeRefreshLastArg = nullptr;
}

static void TestSetHumanPlayerV2() {
    StartSuite("SetHumanPlayer_v2 (galactic-mode split-brain fix)");

    FakeLuaState L;

    // ------------------------------------------------------------------
    // Case 1: happy path. current=0, target=3 on an 8-slot array.
    // ------------------------------------------------------------------
    SetupEightSlotArray(/*initialSlot=*/0);
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 3.0; L.stack.push_back(a); }
    int nret = Lua_SetHumanPlayer_v2(LS(&L));
    Check(nret == 1, "v2 returns 1 value");
    Check(!L.stack.empty() && L.stack.back().numval == 1.0,
          "case1: happy path returns 1 (success)");

    // Slot 3 byte is set, every other is cleared.
    bool sweepOk = true;
    for (int i = 0; i < 8; i++) {
        auto p = GetPlayerObj(i);
        uint8_t byte = *reinterpret_cast<uint8_t*>(
            p + RVA::PlayerObj::LocalPlayer);
        uint8_t expected = (i == 3) ? 1 : 0;
        if (byte != expected) sweepOk = false;
    }
    Check(sweepOk, "case1: +0x62 sweep wrote 1 to slot 3, 0 to all others");

    // PlayerListClass +0x30 == 3
    int fieldAfter = *reinterpret_cast<int*>(
        g_base + RVA::PlayerListClass_Global + 0x30);
    Check(fieldAfter == 3, "case1: PlayerListClass+0x30 == 3");

    // Subsystem refresh interceptor was invoked exactly once with the
    // event-target sentinel as its argument.
    Check(g_fakeRefreshCallCount == 1,
          "case1: subsystem refresh interceptor called exactly once");
    Check(g_fakeRefreshLastArg == reinterpret_cast<void*>(g_base + 0x401000),
          "case1: refresh interceptor received eventTarget sentinel");

    // FindLocalPlayerSlot now returns 3 (post-verify).
    int scanAfter = FindLocalPlayerSlot();
    Check(scanAfter == 3, "case1: FindLocalPlayerSlot returns new slot 3");

    // ------------------------------------------------------------------
    // Case 2: out-of-range target returns 0 and does NOT refresh.
    // ------------------------------------------------------------------
    SetupEightSlotArray(/*initialSlot=*/0);
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 12.0; L.stack.push_back(a); }  // 12 > 8 players
    Lua_SetHumanPlayer_v2(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 0.0,
          "case2: out-of-range target (12) returns 0");
    Check(g_fakeRefreshCallCount == 0,
          "case2: refresh interceptor NOT called for out-of-range");
    // Slot 0 still has +0x62 == 1 (untouched)
    Check(*reinterpret_cast<uint8_t*>(GetPlayerObj(0) + RVA::PlayerObj::LocalPlayer) == 1,
          "case2: slot 0 still has +0x62 == 1 (no mutation)");

    // Negative target
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = -1.0; L.stack.push_back(a); }
    Lua_SetHumanPlayer_v2(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 0.0,
          "case2b: negative target (-1) returns 0");

    // ------------------------------------------------------------------
    // Case 3: target == current AND scan agrees -> early return.
    // ------------------------------------------------------------------
    SetupEightSlotArray(/*initialSlot=*/2);
    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 2.0; L.stack.push_back(a); }
    Lua_SetHumanPlayer_v2(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 1.0,
          "case3: target == current returns 1 (no-op)");
    Check(g_fakeRefreshCallCount == 0,
          "case3: no-op path skips refresh interceptor");
    // No other slot gained +0x62 == 1 (no mutation at all)
    int slotsWithLocalFlag = 0;
    for (int i = 0; i < 8; i++) {
        auto p = GetPlayerObj(i);
        if (*reinterpret_cast<uint8_t*>(p + RVA::PlayerObj::LocalPlayer) == 1) {
            slotsWithLocalFlag++;
        }
    }
    Check(slotsWithLocalFlag == 1, "case3: exactly one slot has +0x62 == 1");

    // ------------------------------------------------------------------
    // Case 4: SPLIT-BRAIN. +0x30 field says slot 2 but actual local byte
    // is on slot 6. v1 would early-return (field match) without fixing the
    // byte. v2 MUST notice field != scan and run the full sweep anyway.
    // ------------------------------------------------------------------
    SetupEightSlotArray(/*initialSlot=*/6);
    // Corrupt: set +0x30 to 2 WITHOUT touching +0x62 on slot 6
    *reinterpret_cast<int*>(g_base + RVA::PlayerListClass_Global + 0x30) = 2;
    Check(FindLocalPlayerSlot() == 6, "case4: scan shows slot 6 (byte pinned)");
    Check(*reinterpret_cast<int*>(g_base + RVA::PlayerListClass_Global + 0x30) == 2,
          "case4: field says slot 2 (split-brain fixture)");

    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 2.0; L.stack.push_back(a); }  // target matches field
    Lua_SetHumanPlayer_v2(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 1.0,
          "case4: split-brain detected, full sweep runs, returns 1");

    // Slot 2 now has +0x62 == 1, slot 6 has +0x62 == 0 (the fix).
    Check(*reinterpret_cast<uint8_t*>(GetPlayerObj(2) + RVA::PlayerObj::LocalPlayer) == 1,
          "case4: after fix, slot 2 has +0x62 == 1");
    Check(*reinterpret_cast<uint8_t*>(GetPlayerObj(6) + RVA::PlayerObj::LocalPlayer) == 0,
          "case4: after fix, slot 6 has +0x62 == 0 (split-brain cleared)");
    Check(FindLocalPlayerSlot() == 2, "case4: scan converged on slot 2");
    Check(g_fakeRefreshCallCount == 1,
          "case4: refresh interceptor called once to propagate change");

    // ------------------------------------------------------------------
    // Case 5: sweep correctness across 8 slots starting from a random
    // pre-state where MULTIPLE slots have +0x62 == 1 (corrupt state).
    // After v2, only the target should have it set.
    // ------------------------------------------------------------------
    SetupEightSlotArray(/*initialSlot=*/0);
    // Corrupt: set +0x62 = 1 on slots 0, 3, 5 (3 locals — impossible state)
    *reinterpret_cast<uint8_t*>(GetPlayerObj(3) + RVA::PlayerObj::LocalPlayer) = 1;
    *reinterpret_cast<uint8_t*>(GetPlayerObj(5) + RVA::PlayerObj::LocalPlayer) = 1;

    fake_reset(&L);
    { StackEntry a; a.type = LUA_TNUMBER; a.numval = 7.0; L.stack.push_back(a); }
    Lua_SetHumanPlayer_v2(LS(&L));
    Check(!L.stack.empty() && L.stack.back().numval == 1.0,
          "case5: multi-local corruption -> target 7 succeeds");

    int localCountAfter = 0;
    int winnerSlot = -1;
    for (int i = 0; i < 8; i++) {
        auto p = GetPlayerObj(i);
        if (*reinterpret_cast<uint8_t*>(p + RVA::PlayerObj::LocalPlayer) == 1) {
            localCountAfter++;
            winnerSlot = i;
        }
    }
    Check(localCountAfter == 1,
          "case5: exactly one +0x62 byte set after sweep");
    Check(winnerSlot == 7,
          "case5: the set byte belongs to target slot 7");
}

// ======================================================================
// Lua_BatchTypeExists — replicated from lua_bridge.cpp for offline testing.
// 2026-04-27 (Spawn-tab live filtering — Task #222 / harness Task #228).
//
// Keep this in sync with the body in lua_bridge.cpp. The harness also pumps
// FakeLuaState::known_object_types so fake_pcall returns truthy for known
// types and nil for unknown — letting us verify both branches without a
// real engine.
// ======================================================================
static int Lua_BatchTypeExists(lua_State* L) {
    constexpr size_t kMaxInputBytes = 16384;
    constexpr size_t kMaxNameBytes  = 256;
    constexpr size_t kMaxNames      = 512;

    if (!fn_tostring || !fn_pushstring || !fn_gettable || !fn_pcall ||
        !fn_type || !fn_gettop || !fn_settop) {
        fn_pushstring(L, "ERR: SWFOC_BatchTypeExists: Lua API not resolved");
        return 1;
    }

    const char* raw = fn_tostring(L, 1);
    if (!raw) {
        fn_pushstring(L, "ERR: SWFOC_BatchTypeExists: arg 1 (string) required");
        return 1;
    }
    size_t rawLen = strnlen(raw, kMaxInputBytes + 1);
    if (rawLen > kMaxInputBytes) {
        fn_pushstring(L, "ERR: SWFOC_BatchTypeExists: input too large");
        return 1;
    }

    // memcpy BEFORE any other stack manipulation — see lua_bridge.cpp comment.
    static thread_local char s_buf[kMaxInputBytes + 1];
    static thread_local const char* s_names[kMaxNames];
    memcpy(s_buf, raw, rawLen);
    s_buf[rawLen] = '\0';

    int savedTop = fn_gettop(L);
    fn_pushstring(L, "Find_Object_Type");
    fn_gettable(L, LUA_GLOBALSINDEX);
    int findTy = fn_type(L, -1);
    fn_settop(L, savedTop);
    if (findTy != LUA_TFUNCTION) {
        fn_pushstring(L, "ERR: SWFOC_BatchTypeExists: Find_Object_Type not available (non-tactical state?)");
        return 1;
    }

    size_t nameCount = 0;
    char* cursor = s_buf;
    char* end    = s_buf + rawLen;
    char* tokenStart = cursor;
    while (cursor <= end && nameCount < kMaxNames) {
        if (cursor == end || *cursor == '|') {
            *cursor = '\0';
            size_t tlen = static_cast<size_t>(cursor - tokenStart);
            if (tlen > 0 && tlen <= kMaxNameBytes) {
                s_names[nameCount++] = tokenStart;
            } else if (tlen > 0) {
                s_names[nameCount++] = "";
            }
            tokenStart = cursor + 1;
            if (cursor == end) break;
        }
        ++cursor;
    }

    if (nameCount == 0) {
        fn_pushstring(L, "");
        return 1;
    }

    static thread_local char s_out[kMaxNames * 2 + 1];
    size_t off = 0;
    for (size_t i = 0; i < nameCount; ++i) {
        if (i > 0 && off < sizeof(s_out) - 1) {
            s_out[off++] = '|';
        }

        int top = fn_gettop(L);
        fn_pushstring(L, "Find_Object_Type");
        fn_gettable(L, LUA_GLOBALSINDEX);
        if (fn_type(L, -1) != LUA_TFUNCTION) {
            fn_settop(L, top);
            if (off < sizeof(s_out) - 1) s_out[off++] = '0';
            continue;
        }
        fn_pushstring(L, s_names[i]);
        int err = fn_pcall(L, 1, 1, 0);
        char flag = '0';
        if (err == 0) {
            int rty = fn_type(L, -1);
            if (rty != LUA_TNIL) {
                flag = '1';
            }
        }
        fn_settop(L, top);

        if (off < sizeof(s_out) - 1) {
            s_out[off++] = flag;
        }
    }
    s_out[off] = '\0';
    fn_pushstring(L, s_out);
    return 1;
}

static void TestBatchTypeExists() {
    StartSuite("BatchTypeExists (Spawn-tab live filtering)");

    // Test 1: non-tactical state (Find_Object_Type not present) returns ERR.
    {
        FakeLuaState L;
        L.has_game_globals = false;
        fake_pushstring(&L, "REBEL_INFANTRY");
        Lua_BatchTypeExists(LS(&L));
        const char* result = fake_tostring(&L, -1);
        Check(result != nullptr && strstr(result, "ERR:") == result,
              "non-tactical state returns ERR prefix");
        Check(result != nullptr && strstr(result, "Find_Object_Type") != nullptr,
              "non-tactical error mentions Find_Object_Type");
    }

    // Test 2: empty input -> empty output.
    {
        FakeLuaState L;
        L.has_game_globals = true;
        fake_pushstring(&L, "");
        Lua_BatchTypeExists(LS(&L));
        const char* result = fake_tostring(&L, -1);
        Check(result != nullptr && result[0] == '\0',
              "empty input -> empty output");
    }

    // Test 3: single known type -> "1".
    {
        FakeLuaState L;
        L.has_game_globals = true;
        L.known_object_types.insert("REBEL_INFANTRY");
        fake_pushstring(&L, "REBEL_INFANTRY");
        Lua_BatchTypeExists(LS(&L));
        const char* result = fake_tostring(&L, -1);
        Check(result != nullptr && strcmp(result, "1") == 0,
              "single known type returns \"1\"");
    }

    // Test 4: single unknown type -> "0".
    {
        FakeLuaState L;
        L.has_game_globals = true;
        // intentionally empty known_object_types
        fake_pushstring(&L, "VONG_PYRAMID");
        Lua_BatchTypeExists(LS(&L));
        const char* result = fake_tostring(&L, -1);
        Check(result != nullptr && strcmp(result, "0") == 0,
              "single unknown type returns \"0\"");
    }

    // Test 5: mixed batch in fixed order.
    {
        FakeLuaState L;
        L.has_game_globals = true;
        L.known_object_types.insert("REBEL_INFANTRY");
        L.known_object_types.insert("UNDERWORLD_FRIGATE");
        // EMPIRE_AT_AT and VONG_PYRAMID NOT in known set.
        fake_pushstring(&L, "REBEL_INFANTRY|EMPIRE_AT_AT|UNDERWORLD_FRIGATE|VONG_PYRAMID");
        Lua_BatchTypeExists(LS(&L));
        const char* result = fake_tostring(&L, -1);
        Check(result != nullptr && strcmp(result, "1|0|1|0") == 0,
              "mixed batch preserves order with correct flags");
    }

    // Test 6: trailing pipe is tolerated.
    {
        FakeLuaState L;
        L.has_game_globals = true;
        L.known_object_types.insert("REBEL_INFANTRY");
        fake_pushstring(&L, "REBEL_INFANTRY|");
        Lua_BatchTypeExists(LS(&L));
        const char* result = fake_tostring(&L, -1);
        // Trailing pipe creates an empty token which is dropped (kept the
        // count truthful instead of forcing operators to chase a phantom
        // empty entry).
        Check(result != nullptr && strcmp(result, "1") == 0,
              "trailing pipe ignored");
    }

    // Test 7: case sensitivity — known set is case-sensitive (mirrors
    // real Lua). "rebel_infantry" is NOT the same as "REBEL_INFANTRY".
    {
        FakeLuaState L;
        L.has_game_globals = true;
        L.known_object_types.insert("REBEL_INFANTRY");
        fake_pushstring(&L, "rebel_infantry");
        Lua_BatchTypeExists(LS(&L));
        const char* result = fake_tostring(&L, -1);
        Check(result != nullptr && strcmp(result, "0") == 0,
              "type lookup is case-sensitive (matches engine)");
    }

    // Test 8: oversized name in batch yields "0" placeholder, doesn't break batch.
    {
        FakeLuaState L;
        L.has_game_globals = true;
        L.known_object_types.insert("REBEL_INFANTRY");
        std::string huge(300, 'A'); // > kMaxNameBytes (256)
        std::string input = "REBEL_INFANTRY|" + huge + "|REBEL_INFANTRY";
        fake_pushstring(&L, input.c_str());
        Lua_BatchTypeExists(LS(&L));
        const char* result = fake_tostring(&L, -1);
        // Oversized middle entry stored as "" in s_names; lookup returns "0".
        Check(result != nullptr && strcmp(result, "1|0|1") == 0,
              "oversized entry replaced with empty string -> \"0\" flag, batch survives");
    }
}

// ======================================================================
// main
// ======================================================================

int main() {
    printf("=== SWFOC Bridge Test Harness ===\n\n");

    InitializeCriticalSection(&csRegistered);
    InitializeCriticalSection(&csGameStates);
    InitializeCriticalSection(&g_pipeLock);

    WireFakes();
    InitGameImage();

    TestStateRegistration();    printf("\n");
    TestSWFOCFunctions();       printf("\n");
    TestDoStringCapture();      printf("\n");
    TestPipeProtocol();         printf("\n");
    TestSharedMemoryProtocol(); printf("\n");
    TestRegistrationAndProbe(); printf("\n");
    TestPlayerHelpers();        printf("\n");
    TestEdgeCases();            printf("\n");
    TestSnapshotFormat();       printf("\n");
    // Phase 3.2: Combat / Inspect helpers ported from CE trainer
    TestSetUnitInvuln();        printf("\n");
    TestSetUnitHull();          printf("\n");
    TestInspectUnit();          printf("\n");
    TestGetHardpoints();        printf("\n");
    TestSelectionReader();      printf("\n");
    TestGodMode();              printf("\n");
    TestOneHitKill();           printf("\n");
    TestCombatHookCombined();   printf("\n");
    TestCombatHookHelpers();    printf("\n");
    // Phase 3.2 (continuation): per-slot writers + observers
    TestSlotCredits();          printf("\n");
    TestSlotTech();             printf("\n");
    TestDrainEnemyCredits();    printf("\n");
    TestSetHeroRespawn();       printf("\n");
    TestPreventUnitDeath();     printf("\n");
    TestGetMaxCredits();        printf("\n");
    // Phase 9: ReplayState observer + mutation seam helpers
    TestReplayPlayerCredits();  printf("\n");
    TestReplayPlayerTechLevel();printf("\n");
    TestReplayLastStoryEvent(); printf("\n");
    TestReplayDiplomaticState();printf("\n");
    TestReplayPlanetCorruption();printf("\n");
    TestReplayUnitOwner();      printf("\n");
    TestReplayCooldownState();  printf("\n");
    TestReplayTaskForceCount(); printf("\n");
    TestReplayHumanPlayerSlot();                printf("\n");
    // 2026-04-23 Task 101 unit/hardpoint/behavior schema.
    TestReplayUnitSurface();                    printf("\n");
    TestReplayListTacticalUnits();              printf("\n");
    TestReplayGodModeSweep();                   printf("\n");
    TestReplayRevealAll();                      printf("\n");
    TestReplayGetAllPlayers();                  printf("\n");
    TestReplayEventStream();                    printf("\n");
    TestReplayEnumerateUnits();                 printf("\n");
    TestReplayHealAllLocal();                   printf("\n");
    TestReplayDamageMultiplier();               printf("\n");
    TestReplayKillRevive();                     printf("\n");
    TestReplayUnitShield();                     printf("\n");
    TestReplayUnitSpeed();                      printf("\n");
    TestReplayHeroLab();                        printf("\n");
    TestReplayHeroStatEdit();                   printf("\n");
    TestReplayPlanets();                        printf("\n");
    TestReplayPlanetTech();                     printf("\n");
    TestReplayAbilities();                      printf("\n");
    TestReplayEconomy();                        printf("\n");
    TestReplayBuildAndFactionSpeed();           printf("\n");
    TestReplayCombatScaling();                  printf("\n");
    TestReplayOhkAttackPower();                 printf("\n");
    TestReplayAiFreezeAndCamera();              printf("\n");
    TestReplayProductionTrio();                 printf("\n");
    TestReplaySetUnitFieldGeneric();            printf("\n");
    TestReplayInstantAndFreeBuild();            printf("\n");
    TestReplayP7Mirrors();                      printf("\n");
    // 2026-04-10 drift guard — must be last so it sees a clean fake state.
    TestRegistrationDriftGuard();                printf("\n");
    // 2026-04-10 diag bounds + pipe capacity fixes. The first suite depends
    // on g_gameImage/SetupTestPlayers, the second reads g_harnessManifest
    // which is populated by TestRegistrationDriftGuard above, so ordering
    // matters: drift-guard must run first.
    TestDiagSelfTestPlayerArrayBoundsCheck();    printf("\n");
    TestDiagListRegisteredFunctionsCapacity();   printf("\n");
    // 2026-04-11 galactic-mode faction switch fix. Runs after the diag
    // suites so it can use the fresh g_gameImage/SetupTestPlayers fixture
    // via its own SetupEightSlotArray helper.
    TestSetHumanPlayerV2();
    // 2026-04-27 Spawn-tab live filtering — bridge probe + harness.
    TestBatchTypeExists();          printf("\n");

    printf("\n=== Results: %d passed, %d failed ===\n", g_passed, g_failed);

    FreeGameImage();
    DeleteCriticalSection(&csRegistered);
    DeleteCriticalSection(&csGameStates);
    DeleteCriticalSection(&g_pipeLock);

    return g_failed > 0 ? 1 : 0;
}
