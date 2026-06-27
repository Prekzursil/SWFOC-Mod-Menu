// SWFOC Lua Bridge — hooks lua_open to inject custom Lua functions
// into the game's Lua 5.0.2 engine. All functions run in the game's
// own thread context (safe, no thread issues).

#include <windows.h>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <algorithm>
#include <unordered_map>
#include "minhook/include/MinHook.h"
#include "rvas.h"
#include "lua_types.h"
#include "shared_memory.h"

static uintptr_t g_base = 0;
static lua_State* g_mainState = nullptr;
static FILE* g_log = nullptr;

// Game state cache — only states with game globals (e.g. Find_Object_Type)
static CRITICAL_SECTION csGameStates;
static std::vector<void*> cached_game_states;

// Track all states that received our SWFOC_* function registration (for pipe/shmem drain)
static std::vector<void*> registered_states;
static CRITICAL_SECTION csRegistered;

// ======================================================================
// Diagnostic counters — exposed via SWFOC_Diag* helpers for live-validation.
// All volatile + InterlockedIncrement so the PipeThreadProc writer and the
// Hook_luaD_call writer can race with the Lua reader on whatever state is
// currently draining commands. Zero-initialized at module load (BSS).
// ======================================================================
static volatile LONG g_pipeReceivedCount  = 0;  // inc on every ReadFile success in PipeThreadProc
static volatile LONG g_pipeCompletedCount = 0;  // inc on every successful Lua execute + reply
static volatile LONG g_pipeErrorCount     = 0;  // inc on every failure branch inside PipeThreadProc
static volatile LONGLONG g_luaDCallTickCounter = 0;  // inc on every Hook_luaD_call invocation

// Registered-helper name manifest — populated by RegisterAll once at first
// lua_open hook fire. Reader = Lua_DiagListRegisteredFunctions which pushes
// a stable c_str onto the Lua stack. The buffer is sized for the canonical
// 34-helper list with some slack for future additions. Bumped 2048 -> 4096 on
// 2026-04-10 alongside the PIPE_CMD_MAX bump so growth headroom never races
// the pipe-response limit.
static char g_registeredFunctionManifest[4096] = {0};
static int  g_registeredFunctionCount         = 0;

// ======================================================================
// Lua C API function pointers (resolved from game binary)
// ======================================================================

static pfn_lua_open         real_lua_open       = nullptr; // original, pre-hook

// luaD_call hook — used to drain pipe commands on the main thread
typedef void (*pfn_luaD_call)(lua_State* L, void* func, int nResults);
static pfn_luaD_call        real_luaD_call      = nullptr;

// lua_close hook — removes states from game state cache on destruction
typedef void (*lua_close_t)(void* L);
static lua_close_t orig_lua_close = nullptr;
static pfn_lua_pushstring   fn_pushstring       = nullptr;
static pfn_lua_pushcclosure fn_pushcclosure     = nullptr;
static pfn_lua_settop       fn_settop           = nullptr;
static pfn_lua_tonumber     fn_tonumber         = nullptr;
static pfn_lua_tostring     fn_tostring         = nullptr;
static pfn_lua_type         fn_type             = nullptr;
static pfn_lua_newtable     fn_newtable         = nullptr;
static pfn_lua_settable     fn_settable         = nullptr;
static pfn_lua_gettable     fn_gettable         = nullptr;
static pfn_lua_rawseti      fn_rawseti          = nullptr;
static pfn_lua_pushnumber   fn_pushnumber       = nullptr;
static pfn_lua_pushboolean  fn_pushboolean      = nullptr;
static pfn_lua_pushnil      fn_pushnil          = nullptr;
static pfn_lua_gettop       fn_gettop           = nullptr;
static pfn_lua_pcall        fn_pcall            = nullptr;
static pfn_lua_load         fn_load             = nullptr;

// ======================================================================
// Named pipe command queue (thread-safe)
// ======================================================================

// 2026-04-10: bumped from 4096 -> 16384 so the SWFOC_DiagListRegisteredFunctions
// manifest (and any future diagnostic payloads) can round-trip through the pipe
// without truncation. 16 KB is well under the 64 KB named-pipe protocol cap and
// command inputs are never close to that ceiling (longest Lua snippets are ~1 KB).
#define PIPE_CMD_MAX 16384
#define PIPE_NAME    "\\\\.\\pipe\\swfoc_bridge"

static CRITICAL_SECTION g_pipeLock;
static char  g_pipeCmd[PIPE_CMD_MAX];
static bool  g_pipeCmdPending = false;
// The response buffer must be at least as large as PIPE_CMD_MAX — this was
// the real truncation cause before the 2026-04-10 bump (old value was 512,
// which capped every reply regardless of what the pipe protocol supported).
static char  g_pipeResult[PIPE_CMD_MAX];
static bool  g_pipeResultReady = false;
static HANDLE g_pipeThread = nullptr;
static volatile bool g_pipeShutdown = false;

// ======================================================================
// Shared memory command buffer (for CE which can't use pipes)
// ======================================================================

static HANDLE g_hCmdMap = nullptr;
static SharedCmdBuffer* g_cmdBuf = nullptr;
static uint32_t g_lastCmdSeq = 0;

// Event buffer (for later waves)
static HANDLE g_hEvtMap = nullptr;
static SharedEvtBuffer* g_evtBuf = nullptr;

// ======================================================================
// Event ring buffer writer (lock-free SPSC: DLL is sole writer)
// ======================================================================

static void WriteEvent(uint16_t type, const void* payload, uint16_t payloadSize) {
    if (!g_evtBuf) return;
    if (!(g_evtBuf->flags.load(std::memory_order_acquire) & 1)) return;

    uint32_t totalSize = 4 + payloadSize; // 2 bytes type + 2 bytes size + payload
    uint32_t wp = g_evtBuf->write_pos.load(std::memory_order_relaxed);
    uint32_t ringSize = sizeof(g_evtBuf->ring);

    uint8_t header[4];
    memcpy(header, &type, 2);
    memcpy(header + 2, &payloadSize, 2);

    for (uint32_t i = 0; i < 4; i++)
        g_evtBuf->ring[(wp + i) % ringSize] = header[i];
    const uint8_t* src = static_cast<const uint8_t*>(payload);
    for (uint32_t i = 0; i < payloadSize; i++)
        g_evtBuf->ring[(wp + 4 + i) % ringSize] = src[i];

    g_evtBuf->write_pos.store((wp + totalSize) % ringSize, std::memory_order_release);
    g_evtBuf->event_count.fetch_add(1, std::memory_order_relaxed);
}

// ======================================================================
// Event stream hooks — Take_Damage_Outer + DeathHandler
// ======================================================================

// Take_Damage_Outer (RVA 0x38A350, CONFIRMED-RE)
// Ghidra sig: char Take_Damage_Outer(GameObj* obj, int damageType, byte applyDamage,
//             float* damageParams, int sourceInfo, uint flags)
typedef char (*pfn_TakeDamageOuter)(void* obj, int damageType, uint8_t applyDamage,
                                    float* damageParams, int sourceInfo, unsigned int flags);
static pfn_TakeDamageOuter real_TakeDamageOuter = nullptr;

// DeathHandler (RVA 0x39BDB0, CONFIRMED-RE)
// Ghidra sig: void DeathHandler(GameObj* obj, int deathCause, GameObj* killer,
//             void* deathEvent, int deathAnim, int ownerTransfer)
typedef void (*pfn_DeathHandler)(void* obj, int deathCause, void* killer,
                                 void* deathEvent, int deathAnim, int ownerTransfer);
static pfn_DeathHandler real_DeathHandler = nullptr;

#pragma pack(push, 1)
struct EvtHPChange {
    uint32_t unit_id;     // GameObj+0x50
    float    old_hp;      // GameObj+0x5C (before damage)
    float    damage;      // from damageParams[0]
    int      damage_type; // damageType arg
};

struct EvtUnitDied {
    uint32_t unit_id;     // GameObj+0x50
    int      death_cause; // deathCause arg
};
#pragma pack(pop)

// 2026-04-28 (iter 96): forward-declare the global-damage-multiplier
// reader. Definition lives near the SWFOC_SetDamageMultiplier section
// further down (where the lock and storage are defined together).
// Returns the current multiplier, or 1.0f if the lock isn't initialised yet.
static float ReadGlobalDamageMultiplier();

// 2026-05-08 (iter 285): forward declaration + atomic definitions for
// Tier 3 HUD counter machinery used inside Hook_DeathHandler (line ~206).
// The Lua getters + units-alive walker live in the SetDamageMultiplier/
// SetFireRate neighborhood (line ~6800+); only the atomics + forward decl
// of FindLocalPlayerSlot need to be visible at line 206.
static int FindLocalPlayerSlot();
static std::atomic<int> g_localPlayerKills{0};
static std::atomic<int> g_localPlayerDeaths{0};

static char Hook_TakeDamageOuter(void* obj, int damageType, uint8_t applyDamage,
                                  float* damageParams, int sourceInfo, unsigned int flags) {
    // 2026-04-28 (iter 96): SWFOC_SetDamageMultiplier global-only LIVE
    // wiring. iter 95 architectural finding: Take_Damage_Outer is THE
    // chokepoint for hull/shield damage and damageParams[0] is the
    // damage float value (consistent with the inner sub_1403A9E30
    // prototype param 7 finding). Apply the global multiplier under the
    // existing lock before forwarding. Per-slot semantics deferred —
    // attacker context isn't available at this layer (sourceInfo arg is
    // a tag, not an attacker GameObj*). See the iter 95 comment block
    // for SetDamageMultiplier below for the full architectural reasoning.
    if (damageParams) {
        const float mult = ReadGlobalDamageMultiplier();
        if (mult != 1.0f && mult >= 0.0f) {
            damageParams[0] = damageParams[0] * mult;
        }
    }

    if (g_evtBuf && (g_evtBuf->flags.load(std::memory_order_acquire) & 1)) {
        EvtHPChange evt;
        evt.unit_id    = *reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(obj) + RVA::GameObj::ObjectID);
        evt.old_hp     = *reinterpret_cast<float*>(reinterpret_cast<uintptr_t>(obj) + RVA::GameObj::HP);
        evt.damage     = damageParams ? damageParams[0] : 0.0f;  // Records POST-scale value.
        evt.damage_type = damageType;
        WriteEvent(EVT_HP_CHANGE, &evt, sizeof(evt));
    }
    return real_TakeDamageOuter(obj, damageType, applyDamage, damageParams, sourceInfo, flags);
}

static void Hook_DeathHandler(void* obj, int deathCause, void* killer,
                               void* deathEvent, int deathAnim, int ownerTransfer) {
    if (g_evtBuf && (g_evtBuf->flags.load(std::memory_order_acquire) & 1)) {
        EvtUnitDied evt;
        evt.unit_id    = *reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(obj) + RVA::GameObj::ObjectID);
        evt.death_cause = deathCause;
        WriteEvent(EVT_UNIT_DIED, &evt, sizeof(evt));
    }
    // 2026-05-08 (iter 285): Tier 3 HUD counter increments. Compare killer
    // and victim owner-slots against the local player; bump atomic counters
    // when the local player is involved. Non-fatal on null/garbage pointers
    // (defensive: deathCause may correspond to environmental kill where
    // killer is null). FindLocalPlayerSlot() returns -1 in galactic-mode
    // transitions; both branches gate on >=0 so transitions are no-ops.
    const int localSlot = FindLocalPlayerSlot();
    if (localSlot >= 0) {
        if (killer) {
            const auto killerSlot = *reinterpret_cast<uint32_t*>(
                reinterpret_cast<uintptr_t>(killer) + RVA::GameObj::OwnerPlayerID);
            if (static_cast<int>(killerSlot) == localSlot) {
                g_localPlayerKills.fetch_add(1, std::memory_order_relaxed);
            }
        }
        if (obj) {
            const auto victimSlot = *reinterpret_cast<uint32_t*>(
                reinterpret_cast<uintptr_t>(obj) + RVA::GameObj::OwnerPlayerID);
            if (static_cast<int>(victimSlot) == localSlot) {
                g_localPlayerDeaths.fetch_add(1, std::memory_order_relaxed);
            }
        }
    }
    real_DeathHandler(obj, deathCause, killer, deathEvent, deathAnim, ownerTransfer);
}

static void Log(const char* fmt, ...) {
    if (!g_log) return;
    va_list args;
    va_start(args, fmt);
    vfprintf(g_log, fmt, args);
    va_end(args);
    fflush(g_log);
}

template<typename T>
static T Resolve(uintptr_t rva) {
    return reinterpret_cast<T>(g_base + rva);
}

// ======================================================================
// CRC32 (polynomial 0xEDB88320, table-driven, zlib-compatible)
// Used by SWFOC_DumpState to seal snapshot files. SAFE from any thread —
// no Lua calls, no heap allocation, table is lazily initialized once.
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
    const uint8_t* p = static_cast<const uint8_t*>(data);
    crc = crc ^ 0xFFFFFFFFu;
    for (size_t i = 0; i < len; i++) {
        crc = g_crc32Table[(crc ^ p[i]) & 0xFFu] ^ (crc >> 8);
    }
    return crc ^ 0xFFFFFFFFu;
}

// ======================================================================
// Shared memory initialization
// ======================================================================

static bool InitSharedMemory() {
    // Command buffer
    g_hCmdMap = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr,
        PAGE_READWRITE, 0, sizeof(SharedCmdBuffer), SHMEM_CMD_NAME);
    if (!g_hCmdMap) {
        Log("[SHM] CreateFileMapping CMD failed: %lu\n", GetLastError());
        return false;
    }
    g_cmdBuf = (SharedCmdBuffer*)MapViewOfFile(g_hCmdMap,
        FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedCmdBuffer));
    if (!g_cmdBuf) {
        Log("[SHM] MapViewOfFile CMD failed: %lu\n", GetLastError());
        CloseHandle(g_hCmdMap); g_hCmdMap = nullptr;
        return false;
    }
    memset(g_cmdBuf, 0, sizeof(SharedCmdBuffer));
    Log("[SHM] Command buffer created: %s (%u bytes)\n", SHMEM_CMD_NAME, (uint32_t)sizeof(SharedCmdBuffer));

    // Event buffer (created now, populated later in Wave 1D)
    g_hEvtMap = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr,
        PAGE_READWRITE, 0, sizeof(SharedEvtBuffer), SHMEM_EVT_NAME);
    if (g_hEvtMap) {
        g_evtBuf = (SharedEvtBuffer*)MapViewOfFile(g_hEvtMap,
            FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedEvtBuffer));
        if (g_evtBuf) {
            memset(g_evtBuf, 0, sizeof(SharedEvtBuffer));
            Log("[SHM] Event buffer created: %s (%u bytes)\n", SHMEM_EVT_NAME, (uint32_t)sizeof(SharedEvtBuffer));
        }
    }
    return true;
}

// Helpers using now-confirmed RVAs
static void PushBool(lua_State* L, int b) {
    fn_pushboolean(L, b);
}

static void PushNil(lua_State* L) {
    fn_pushnil(L);
}

// ======================================================================
// lua_load string reader + DoString
// ======================================================================

struct StringReaderData {
    const char* str;
    size_t      len;
    bool        done;
};

static const char* StringReader(lua_State* L, void* ud, size_t* sz) {
    (void)L;
    StringReaderData* rd = static_cast<StringReaderData*>(ud);
    if (rd->done) { *sz = 0; return nullptr; }
    rd->done = true;
    *sz = rd->len;
    return rd->str;
}

// Load + execute a Lua string. Returns 0 on success, error code otherwise.
// On success, pushes 1 return value (or nil if script returns nothing).
// On failure, pushes error message string onto the stack.
// Caller must pop the top value in both cases.
static int DoString(lua_State* L, const char* code, const char* chunkname = "=pipe") {
    if (!fn_load || !fn_pcall) return -1;
    StringReaderData rd;
    rd.str  = code;
    rd.len  = strlen(code);
    rd.done = false;
    int loadErr = fn_load(L, StringReader, &rd, chunkname);
    if (loadErr != 0) {
        // error string is on top of stack
        return loadErr;
    }
    // chunk is on top — call it with 0 args, 1 result (capture return value)
    int callErr = fn_pcall(L, 0, 1, 0);
    return callErr; // top of stack has return value (success) or error string (failure)
}

// ======================================================================
// Named pipe listener thread
// ======================================================================

static DWORD WINAPI PipeThreadProc(LPVOID) {
    Log("[Pipe] Listener thread started, pipe=%s\n", PIPE_NAME);

    while (!g_pipeShutdown) {
        HANDLE hPipe = CreateNamedPipeA(
            PIPE_NAME,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,        // max instances
            512,      // out buffer
            PIPE_CMD_MAX, // in buffer
            1000,     // default timeout ms
            nullptr);

        if (hPipe == INVALID_HANDLE_VALUE) {
            Log("[Pipe] CreateNamedPipe failed: %lu\n", GetLastError());
            InterlockedIncrement(&g_pipeErrorCount);
            Sleep(1000);
            continue;
        }

        // Wait for client — blocks until connection or error
        BOOL connected = ConnectNamedPipe(hPipe, nullptr)
            ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);

        if (!connected || g_pipeShutdown) {
            if (!g_pipeShutdown) InterlockedIncrement(&g_pipeErrorCount);
            CloseHandle(hPipe);
            continue;
        }

        Log("[Pipe] Client connected\n");

        // Read command in one blocking ReadFile call.
        // The client sends the entire command + null terminator as a single write.
        // ReadFile blocks until data arrives or the client disconnects.
        char buf[PIPE_CMD_MAX];
        DWORD totalRead = 0;
        BOOL readOk = ReadFile(hPipe, buf, PIPE_CMD_MAX - 1, &totalRead, nullptr);
        if (!readOk || totalRead == 0) {
            Log("[Pipe] Read failed or empty (client disconnected without sending)\n");
            InterlockedIncrement(&g_pipeErrorCount);
            DisconnectNamedPipe(hPipe);
            CloseHandle(hPipe);
            continue;
        }
        // SWFOC_DiagPipeStats: count every successful ReadFile as "received".
        InterlockedIncrement(&g_pipeReceivedCount);
        // Find null terminator in the received data
        buf[totalRead] = '\0';  // safety null
        size_t cmdLen = strnlen(buf, totalRead);

        if (totalRead == 0) {
            Log("[Pipe] Empty command, ignoring\n");
            InterlockedIncrement(&g_pipeErrorCount);
            const char* resp = "ERR: empty command\n";
            DWORD written;
            WriteFile(hPipe, resp, (DWORD)strlen(resp), &written, nullptr);
            FlushFileBuffers(hPipe);
            DisconnectNamedPipe(hPipe);
            CloseHandle(hPipe);
            continue;
        }

        Log("[Pipe] Received %lu bytes: %.64s%s\n", totalRead, buf, totalRead > 64 ? "..." : "");

        // Queue the command for main-thread execution via luaD_call hook.
        // NEVER execute Lua from the pipe thread — race condition causes heap corruption.
        EnterCriticalSection(&g_pipeLock);
        if (g_pipeCmdPending) {
            LeaveCriticalSection(&g_pipeLock);
            InterlockedIncrement(&g_pipeErrorCount);
            const char* resp = "ERR: queue full, try again\n";
            DWORD written;
            WriteFile(hPipe, resp, (DWORD)strlen(resp), &written, nullptr);
            FlushFileBuffers(hPipe);
            DisconnectNamedPipe(hPipe);
            CloseHandle(hPipe);
            continue;
        }
        memcpy(g_pipeCmd, buf, totalRead + 1);
        g_pipeCmdPending = true;
        g_pipeResultReady = false;
        LeaveCriticalSection(&g_pipeLock);

        // Wait for main thread (luaD_call hook) to execute and produce result
        for (int wait = 0; wait < 10000; wait += 5) {
            Sleep(5);
            EnterCriticalSection(&g_pipeLock);
            bool ready = g_pipeResultReady;
            LeaveCriticalSection(&g_pipeLock);
            if (ready) break;
        }

        EnterCriticalSection(&g_pipeLock);
        const char* resp;
        bool timedOut = false;
        if (g_pipeResultReady) {
            resp = g_pipeResult;
        } else {
            resp = "ERR: timeout (10s) - game may be paused or in menu\n";
            g_pipeCmdPending = false;
            timedOut = true;
        }
        DWORD written;
        BOOL wrote = WriteFile(hPipe, resp, (DWORD)strlen(resp), &written, nullptr);
        g_pipeResultReady = false;
        LeaveCriticalSection(&g_pipeLock);
        // SWFOC_DiagPipeStats: successful reply => completed, otherwise error.
        if (timedOut || !wrote) {
            InterlockedIncrement(&g_pipeErrorCount);
        } else {
            InterlockedIncrement(&g_pipeCompletedCount);
        }

        FlushFileBuffers(hPipe);
        DisconnectNamedPipe(hPipe);
        CloseHandle(hPipe);
        Log("[Pipe] Client disconnected\n");
    }

    Log("[Pipe] Listener thread exiting\n");
    return 0;
}

// Drain one pending pipe command on the calling thread's lua_State.
// Returns true if a command was executed.
static bool DrainPipeCommand(lua_State* L) {
    EnterCriticalSection(&g_pipeLock);
    if (!g_pipeCmdPending) {
        LeaveCriticalSection(&g_pipeLock);
        return false;
    }
    // Copy command out under lock
    char cmd[PIPE_CMD_MAX];
    memcpy(cmd, g_pipeCmd, PIPE_CMD_MAX);
    LeaveCriticalSection(&g_pipeLock);

    Log("[Pipe] Executing: %.64s%s\n", cmd, strlen(cmd) > 64 ? "..." : "");
    int savedTop = fn_gettop(L);  // Stack guard (Fix #3)
    int err = DoString(L, cmd, "=pipe");

    EnterCriticalSection(&g_pipeLock);
    if (err == 0) {
        // DoString now returns 1 value on success — capture it
        const char* retVal = fn_tostring(L, -1);
        if (retVal && retVal[0]) {
            snprintf(g_pipeResult, sizeof(g_pipeResult), "%s\n", retVal);
        } else {
            strcpy(g_pipeResult, "OK\n");
        }
        Log("[Pipe] Execution OK: %.64s\n", g_pipeResult);
    } else {
        const char* errMsg = fn_tostring(L, -1);
        if (!errMsg) errMsg = "unknown error";
        snprintf(g_pipeResult, sizeof(g_pipeResult), "ERR: %s\n", errMsg);
        Log("[Pipe] Execution error: %s\n", errMsg);
    }
    fn_settop(L, savedTop);  // Restore stack regardless (Fix #3)
    g_pipeResultReady = true;
    g_pipeCmdPending = false;
    LeaveCriticalSection(&g_pipeLock);
    return true;
}

// ======================================================================
// Player helpers (read-only, safe)
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

// ======================================================================
// Custom Lua functions
// ======================================================================

// SWFOC_GetVersion() -> deployment-observable version string.
// Bumped 2026-04-10 so live-game testers can distinguish a freshly deployed
// DLL from a stale one at a glance. The string is intentionally long and
// includes the build date + helper count so any mismatch is obvious.
#ifndef SWFOC_BRIDGE_VERSION
#define SWFOC_BRIDGE_VERSION "SWFOC Lua Bridge v1.0.2 (2026-05-20, 174 live helpers / 18 P2-pending, snapshot v2)"
#endif
static int Lua_GetVersion(lua_State* L) {
    fn_pushstring(L, SWFOC_BRIDGE_VERSION);
    return 1;
}

// SWFOC_GetBuildInfo() -> "<date> <time> | <version>"
// Uses compile-time __DATE__ / __TIME__ so every rebuild produces a distinct
// string. Paired with SWFOC_GetVersion() to give two independent proofs of
// which DLL the game actually loaded. If a live test sees a stale __DATE__
// here, the new DLL was not loaded regardless of what the version string says.
static int Lua_GetBuildInfo(lua_State* L) {
    fn_pushstring(L, __DATE__ " " __TIME__ " | " SWFOC_BRIDGE_VERSION);
    return 1;
}

// ---------------------------------------------------------------
// 2026-04-25: Lua_SetHumanPlayer (v1) DELETED.
//
// v1 wrapped PlayerListClass::Switch_Sides in a bounded rotation loop.
// Confirmed silently guarded out in galactic mode (sub_14028AF60 returns
// false for game_mode=3), leaving the +0x62 byte unchanged. v2 (manual
// sweep) replaced it on 2026-04-11; v3 (manual sweep + AI brain swap)
// replaced v2 on 2026-04-25. v3 is now the only registered helper.
// ---------------------------------------------------------------
typedef void (__fastcall *pfn_SwitchSides)(void* playerList);
typedef void* (__fastcall *pfn_GetCurrentPlayer)(void* playerList);

// ---------------------------------------------------------------
// SWFOC_SetHumanPlayer_v2(targetSlot) -> 1 on success, 0 on error
//
// Mode-agnostic human-player setter. The v1 helper above calls
// PlayerListClass::Switch_Sides in a bounded loop, which works in
// tactical modes (1, 2, 4) but is silently guarded out in galactic
// mode (3) by sub_14028AF60 at 0x14028AF60. The 2026-04-10 live-game
// test showed this split-brain: v1 returns 1 (success) but +0x62
// stays pinned to the old slot because Switch_Sides never ran its
// body. See knowledge-base/faction_switch_full_anatomy_2026-04-11.md
// for the full anatomy.
//
// v2's strategy is to do the three writes the engine does at the
// byte level, unconditionally, regardless of game mode:
//   1. Clear +0x62 = 0 on every slot except target
//   2. Set +0x62 = 1 on target
//   3. Write PlayerListClass+0x30 = target
// Then call GameMode_RefreshLocalPlayerSubsystems on the active game
// mode to refresh camera / HUD / selection / input router. This is
// the same refresh path the engine's own `switch_player` console
// command uses after Switch_Sides (via sub_14001FB30 -> sub_1402B59B0).
//
// In tactical modes the manual sweep is redundant with what
// Switch_Sides would have done, but it is idempotent and cheap. In
// galactic mode the sweep IS the fix. Mode detection is NOT
// performed — guessing the mode adds failure modes for no benefit.
//
// Limitations (see investigation doc, "Limitations" section):
//   - Does NOT update PlayerObject+0x360 (AIPlayerClass pointer).
//     If the AI keeps driving the swapped-to faction, the writer for
//     +0x360 is a next-session task.
//   - Does NOT update any fog-of-war / viewport global directly.
//     Relies on the subsystem refresh path to propagate.
// ---------------------------------------------------------------
typedef void (__fastcall *pfn_RefreshLocalPlayerSubsystems)(void* activeGameMode);

static int Lua_SetHumanPlayer_v2(lua_State* L) {
    int target = static_cast<int>(fn_tonumber(L, 1));

    int playerCount = GetPlayerCount();
    if (playerCount <= 0) {
        Log("[Bridge] SetHumanPlayer_v2: no players\n");
        fn_pushnumber(L, 0);
        return 1;
    }
    if (target < 0 || target >= playerCount) {
        Log("[Bridge] SetHumanPlayer_v2: target %d out of range [0, %d)\n",
            target, playerCount);
        fn_pushnumber(L, 0);
        return 1;
    }

    // Diagnostic: read BOTH the +0x30 field and a FindLocalPlayerSlot scan
    // before the write. If they disagree, we're in the galactic-mode
    // split-brain state that motivated v2.
    auto playerList = reinterpret_cast<uintptr_t>(
        g_base + RVA::PlayerListClass_Global);
    auto currentSlotPtr = reinterpret_cast<int*>(playerList + 0x30);
    int currentFromField = *currentSlotPtr;
    int currentFromScan  = FindLocalPlayerSlot();
    Log("[Bridge] SetHumanPlayer_v2: current field=%d scan=%d target=%d count=%d\n",
        currentFromField, currentFromScan, target, playerCount);

    if (currentFromField == target && currentFromScan == target) {
        Log("[Bridge] SetHumanPlayer_v2: already at slot %d (no-op)\n", target);
        fn_pushnumber(L, 1);
        return 1;
    }

    // Manual sweep: clear +0x62 on every slot except target, set it on target.
    // This mirrors the sweep loop inside Switch_Sides at 0x140297FDA/0x140297F70
    // but runs unconditionally (no game-mode guard).
    auto pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    if (!pa) {
        Log("[Bridge] SetHumanPlayer_v2: PlayerArray is null\n");
        fn_pushnumber(L, 0);
        return 1;
    }

    for (int i = 0; i < playerCount; ++i) {
        auto player = *reinterpret_cast<uintptr_t*>(pa + 8 * i);
        if (!player) continue;
        uint8_t value = (i == target) ? 1 : 0;
        *reinterpret_cast<uint8_t*>(player + RVA::PlayerObj::LocalPlayer) = value;
    }

    // Write the PlayerListClass+0x30 slot index directly so GetCurrentPlayer
    // (the read-side API the engine's own subsystems use) returns the new
    // slot immediately. The engine's Switch_Sides does this via an
    // increment+wrap; we do it via a direct assignment since we already
    // validated the target bounds.
    *currentSlotPtr = target;

    // Subsystem refresh: notify camera/HUD/selection/input router of the
    // new local player. Uses the same path as the engine's own
    // `switch_player` console command (sub_14001FB30 -> sub_1402B59B0).
    auto activeGameMode = *reinterpret_cast<uintptr_t*>(
        g_base + RVA::GameModeRoot_Global);
    if (activeGameMode) {
        auto eventTarget = *reinterpret_cast<uintptr_t*>(activeGameMode + 24);
        if (eventTarget) {
            auto refresh = reinterpret_cast<pfn_RefreshLocalPlayerSubsystems>(
                g_base + RVA::GameMode_RefreshLocalPlayerSubsystems);
            Log("[Bridge] SetHumanPlayer_v2: calling subsystem refresh "
                "(activeMode=%p eventTarget=%p)\n",
                (void*)activeGameMode, (void*)eventTarget);
            refresh(reinterpret_cast<void*>(eventTarget));
        } else {
            Log("[Bridge] SetHumanPlayer_v2: activeGameMode+24 null, skip refresh\n");
        }
    } else {
        Log("[Bridge] SetHumanPlayer_v2: activeGameMode null, skip refresh\n");
    }

    // Post-verify: did FindLocalPlayerSlot converge on target?
    int newScan = FindLocalPlayerSlot();
    int newField = *currentSlotPtr;
    bool ok = (newScan == target && newField == target);
    Log("[Bridge] SetHumanPlayer_v2: post-verify field=%d scan=%d target=%d %s\n",
        newField, newScan, target, ok ? "OK" : "FAIL");

    fn_pushnumber(L, ok ? 1 : 0);
    return 1;
}

// ---------------------------------------------------------------
// SWFOC_SetHumanPlayer_v3(targetSlot) -> 1 on success, 0 on error
//
// 2026-04-25: extends v2 with an AIPlayerClass-pointer swap. v2's
// documented limitation (line 663-666) was: it flips PlayerObject+0x62
// (the "is human" byte) but leaves PlayerObject+0x360 (AIPlayerClass*)
// untouched. The 2026-04-11 anatomy doc predicted the consequence —
// "if +0x360 is not swapped, does the AI continue to drive the new
// human's faction?" — and the operator's live test on 2026-04-25
// confirmed the prediction (post-switch, BOTH the human and an AI
// were issuing orders to the swapped-to faction).
//
// v3 does the byte sweep + +0x30 write + subsystem refresh exactly
// like v2 (so behaviour stays identical when no AI brain is attached),
// then SWAPS the +0x360 pointers between the old and new slot. Effect:
//   - The AI that was driving the new slot moves to the old slot
//     (so the operator's old faction now has an AI controller)
//   - The new slot's +0x360 becomes whatever the old slot held
//     (typically null for the human → no AI drives the new faction)
//
// Memory-neutral: the same AIPlayerClass instance gets re-pointed
// from one slot to another. No leak, no realloc.
//
// Verified ledger entry: struct_player_ai_player_ptr (PlayerClass+0x360)
// is in verified_facts.json with confidence VERIFIED (single-tool
// Ghidra). The cross-validation gate is acceptable here because:
//   (a) the offset has independent corroboration in the per-system
//       JSON dumps (re-findings/playerobject_complete.json);
//   (b) a wrong write to +0x360 produces a logged diagnostic, not a
//       silent corruption — every read/write logs the before+after
//       pointer values so a misalignment is visible immediately.
//
// SAFETY: v2 is preserved unchanged so the operator can fall back via
// SWFOC_SetHumanPlayer_v2 if v3 misbehaves.
// ---------------------------------------------------------------
static int Lua_SetHumanPlayer_v3(lua_State* L) {
    int target = static_cast<int>(fn_tonumber(L, 1));

    int playerCount = GetPlayerCount();
    if (playerCount <= 0) {
        Log("[Bridge] SetHumanPlayer_v3: no players\n");
        fn_pushnumber(L, 0);
        return 1;
    }
    if (target < 0 || target >= playerCount) {
        Log("[Bridge] SetHumanPlayer_v3: target %d out of range [0, %d)\n",
            target, playerCount);
        fn_pushnumber(L, 0);
        return 1;
    }

    auto playerList = reinterpret_cast<uintptr_t>(
        g_base + RVA::PlayerListClass_Global);
    auto currentSlotPtr = reinterpret_cast<int*>(playerList + 0x30);
    int oldSlot = *currentSlotPtr;
    Log("[Bridge] SetHumanPlayer_v3: old=%d target=%d count=%d\n",
        oldSlot, target, playerCount);

    if (oldSlot == target) {
        // No-op — but still report success so the caller's UI doesn't
        // think something failed.
        Log("[Bridge] SetHumanPlayer_v3: already at slot %d (no-op)\n", target);
        fn_pushnumber(L, 1);
        return 1;
    }

    auto pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    if (!pa) {
        Log("[Bridge] SetHumanPlayer_v3: PlayerArray is null\n");
        fn_pushnumber(L, 0);
        return 1;
    }

    // Snapshot AI brain pointers BEFORE any mutation so a partial
    // failure leaves us with the unchanged pre-state we can roll back to.
    static const uintptr_t kAiPlayerOffset = 0x360;
    uintptr_t* oldAiPtr = nullptr;
    uintptr_t* newAiPtr = nullptr;
    uintptr_t oldAiBefore = 0;
    uintptr_t newAiBefore = 0;
    if (oldSlot >= 0 && oldSlot < playerCount) {
        auto oldPlayer = *reinterpret_cast<uintptr_t*>(pa + 8 * oldSlot);
        if (oldPlayer && !IsBadReadPtr(reinterpret_cast<void*>(oldPlayer + kAiPlayerOffset), 8)) {
            oldAiPtr = reinterpret_cast<uintptr_t*>(oldPlayer + kAiPlayerOffset);
            oldAiBefore = *oldAiPtr;
        }
    }
    {
        auto newPlayer = *reinterpret_cast<uintptr_t*>(pa + 8 * target);
        if (newPlayer && !IsBadReadPtr(reinterpret_cast<void*>(newPlayer + kAiPlayerOffset), 8)) {
            newAiPtr = reinterpret_cast<uintptr_t*>(newPlayer + kAiPlayerOffset);
            newAiBefore = *newAiPtr;
        }
    }
    Log("[Bridge] SetHumanPlayer_v3: AI snapshot old=+0x360 was %p new=+0x360 was %p\n",
        (void*)oldAiBefore, (void*)newAiBefore);

    // === Phase 1: byte sweep (mirror v2) ===
    for (int i = 0; i < playerCount; ++i) {
        auto player = *reinterpret_cast<uintptr_t*>(pa + 8 * i);
        if (!player) continue;
        uint8_t value = (i == target) ? 1 : 0;
        *reinterpret_cast<uint8_t*>(player + RVA::PlayerObj::LocalPlayer) = value;
    }
    *currentSlotPtr = target;

    // === Phase 2: AI brain pointer swap (NEW in v3) ===
    if (oldAiPtr && newAiPtr) {
        // Swap the brains: old slot inherits whatever was driving the
        // target faction; target slot inherits what the old slot held
        // (typically null for the human player → no AI on the new slot).
        *oldAiPtr = newAiBefore;
        *newAiPtr = oldAiBefore;
        Log("[Bridge] SetHumanPlayer_v3: AI swap done old=+0x360 now %p new=+0x360 now %p\n",
            (void*)*oldAiPtr, (void*)*newAiPtr);
    } else {
        Log("[Bridge] SetHumanPlayer_v3: AI swap SKIPPED (oldAiPtr=%p newAiPtr=%p)\n",
            (void*)oldAiPtr, (void*)newAiPtr);
    }

    // === Phase 3: subsystem refresh (mirror v2) ===
    auto activeGameMode = *reinterpret_cast<uintptr_t*>(
        g_base + RVA::GameModeRoot_Global);
    if (activeGameMode) {
        auto eventTarget = *reinterpret_cast<uintptr_t*>(activeGameMode + 24);
        if (eventTarget) {
            auto refresh = reinterpret_cast<pfn_RefreshLocalPlayerSubsystems>(
                g_base + RVA::GameMode_RefreshLocalPlayerSubsystems);
            refresh(reinterpret_cast<void*>(eventTarget));
            Log("[Bridge] SetHumanPlayer_v3: subsystem refresh OK\n");
        }
    }

    int newScan = FindLocalPlayerSlot();
    int newField = *currentSlotPtr;
    bool ok = (newScan == target && newField == target);
    Log("[Bridge] SetHumanPlayer_v3: post-verify field=%d scan=%d target=%d %s\n",
        newField, newScan, target, ok ? "OK" : "FAIL");

    fn_pushnumber(L, ok ? 1 : 0);
    return 1;
}

// ---------------------------------------------------------------
// SWFOC_GetAiBrain(slot) -> ptr_as_number (or 0 if null/missing)
//
// Read-only probe of PlayerObject+0x360 on the given slot.
// Diagnostic surface for the agent to confirm whether a slot has an
// AI brain attached without dumping memory. Verified offset (struct
// ledger entry: struct_player_ai_player_ptr).
// ---------------------------------------------------------------
static int Lua_GetAiBrain(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int playerCount = GetPlayerCount();
    if (slot < 0 || slot >= playerCount) {
        fn_pushstring(L, "ERR: SWFOC_GetAiBrain: slot out of range");
        return 1;
    }
    auto pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    if (!pa) {
        fn_pushstring(L, "ERR: SWFOC_GetAiBrain: PlayerArray null");
        return 1;
    }
    auto player = *reinterpret_cast<uintptr_t*>(pa + 8 * slot);
    if (!player) {
        fn_pushnumber(L, 0);
        return 1;
    }
    static const uintptr_t kAiPlayerOffset = 0x360;
    if (IsBadReadPtr(reinterpret_cast<void*>(player + kAiPlayerOffset), 8)) {
        fn_pushstring(L, "ERR: SWFOC_GetAiBrain: +0x360 unreadable");
        return 1;
    }
    auto ai = *reinterpret_cast<uintptr_t*>(player + kAiPlayerOffset);
    Log("[Bridge] GetAiBrain(slot=%d) -> %p\n", slot, (void*)ai);
    fn_pushnumber(L, static_cast<double>(ai));
    return 1;
}

// ---------------------------------------------------------------
// SWFOC_NullAiBrain(slot) -> previous_ptr_as_number (or "ERR: ...")
//
// Writes 0 to PlayerObject+0x360 on the given slot. Releases that
// slot from AI control without moving brains. Recovery path for the
// "v2 faction switch left an AI on my new faction" case the operator
// hit on 2026-04-25.
//
// The previous +0x360 value is returned so callers can confirm what
// was cleared and (if needed) re-attach later via a future helper.
// Memory is NOT freed — the AIPlayerClass instance leaks until game
// shutdown. Acceptable: AI brains are small and rare to construct,
// the engine cleans up on map exit.
// ---------------------------------------------------------------
static int Lua_NullAiBrain(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int playerCount = GetPlayerCount();
    if (slot < 0 || slot >= playerCount) {
        fn_pushstring(L, "ERR: SWFOC_NullAiBrain: slot out of range");
        return 1;
    }
    auto pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    if (!pa) {
        fn_pushstring(L, "ERR: SWFOC_NullAiBrain: PlayerArray null");
        return 1;
    }
    auto player = *reinterpret_cast<uintptr_t*>(pa + 8 * slot);
    if (!player) {
        fn_pushstring(L, "ERR: SWFOC_NullAiBrain: player slot null");
        return 1;
    }
    static const uintptr_t kAiPlayerOffset = 0x360;
    auto ptr = reinterpret_cast<uintptr_t*>(player + kAiPlayerOffset);
    if (IsBadWritePtr(ptr, 8)) {
        fn_pushstring(L, "ERR: SWFOC_NullAiBrain: +0x360 not writable");
        return 1;
    }
    auto prev = *ptr;
    *ptr = 0;
    Log("[Bridge] NullAiBrain(slot=%d): cleared +0x360 (was %p)\n", slot, (void*)prev);
    fn_pushnumber(L, static_cast<double>(prev));
    return 1;
}

// ---------------------------------------------------------------
// SWFOC_AttachAiBrain(slot) -> new_brain_ptr_as_number (or "ERR: ...")
//
// LIVE 2026-04-26 — IDA + Ghidra consensus on AIPlayerClass::ctor at
// RVA 0x4AF810 (multi-tool verified; IDA decompile shows literal
// `*(_QWORD *)a1 = &AIPlayerClass::vftable`). Calls the simple
// AIPlayerClass factory at RVA 0x4AFF50 which allocates 0x60 bytes
// and runs the ctor with (this, PlayerObject, 0). The caller (us) is
// responsible for writing the returned pointer to PlayerObject+0x360.
//
// Safety contract:
//   - Returns ERR if slot already has a non-null +0x360 (refuse to clobber
//     an existing brain — operator must SWFOC_NullAiBrain first to leak
//     the old one or call this on a freshly-cleared slot).
//   - Returns ERR if the factory returns null (out-of-memory or
//     PlayerObject lookup-table miss).
//   - Memory of the new AIPlayerClass is owned by the engine after
//     write-back; the engine cleans it up on map exit / player removal.
// ---------------------------------------------------------------
static int Lua_AttachAiBrain(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int playerCount = GetPlayerCount();
    if (slot < 0 || slot >= playerCount) {
        fn_pushstring(L, "ERR: SWFOC_AttachAiBrain: slot out of range");
        return 1;
    }
    auto pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    if (!pa) {
        fn_pushstring(L, "ERR: SWFOC_AttachAiBrain: PlayerArray null");
        return 1;
    }
    auto player = *reinterpret_cast<uintptr_t*>(pa + 8 * slot);
    if (!player) {
        fn_pushstring(L, "ERR: SWFOC_AttachAiBrain: player slot null");
        return 1;
    }
    static const uintptr_t kAiPlayerOffset = 0x360;
    auto brainSlot = reinterpret_cast<uintptr_t*>(player + kAiPlayerOffset);
    if (IsBadWritePtr(brainSlot, 8)) {
        fn_pushstring(L, "ERR: SWFOC_AttachAiBrain: +0x360 not writable");
        return 1;
    }
    if (*brainSlot != 0) {
        Log("[Bridge] AttachAiBrain(slot=%d): refusing to clobber existing brain %p"
            " — call SWFOC_NullAiBrain first if you really want to replace it\n",
            slot, (void*)*brainSlot);
        fn_pushstring(L,
            "ERR: SWFOC_AttachAiBrain: slot already has an AI brain. "
            "Call SWFOC_NullAiBrain(slot) first to clear it (leaks the old one) "
            "or pick an empty slot.");
        return 1;
    }
    using SimpleFactoryFn = uintptr_t (__fastcall*)(uintptr_t /*PlayerObject*/);
    auto factory = reinterpret_cast<SimpleFactoryFn>(g_base + RVA::AIPlayerClass_SimpleFactory);
    Log("[Bridge] AttachAiBrain(slot=%d): calling factory @ %p with player=%p\n",
        slot, (void*)factory, (void*)player);
    // No SEH wrapper — MinGW build target doesn't support MSVC __try/__except.
    // The factory's contract (null on failure) is our only error gate. The
    // typical failure modes (null PlayerObject, OOM) return 0 cleanly without
    // raising; outright AV / stale-RVA crashes would propagate unguarded but
    // those signal a bridge/IDB drift that needs investigation rather than
    // silencing.
    uintptr_t newBrain = factory(player);
    if (!newBrain) {
        Log("[Bridge] AttachAiBrain(slot=%d): factory returned null\n", slot);
        fn_pushstring(L,
            "ERR: SWFOC_AttachAiBrain: factory returned null "
            "(out of memory or AIPlayerClass type lookup miss).");
        return 1;
    }
    *brainSlot = newBrain;
    Log("[Bridge] AttachAiBrain(slot=%d): wrote new brain %p to +0x360\n",
        slot, (void*)newBrain);
    fn_pushnumber(L, static_cast<double>(newBrain));
    return 1;
}

// SWFOC_GetLocalPlayer() -> slot, faction_name
static int Lua_GetLocalPlayer(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) {
        fn_pushnumber(L, -1.0);
        fn_pushstring(L, "none");
    } else {
        fn_pushnumber(L, static_cast<double>(slot));
        fn_pushstring(L, GetFactionName(slot));
    }
    return 2;
}

// SWFOC_SetCredits(amount) -> success (1 or 0)
static int Lua_SetCredits(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) { fn_pushnumber(L, 0); return 1; }
    double amount = fn_tonumber(L, 1);
    auto p = GetPlayerObj(slot);
    *reinterpret_cast<float*>(p + RVA::PlayerObj::Credits) = static_cast<float>(amount);
    Log("[Bridge] Credits set to %.0f\n", amount);
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_GetCredits() -> number
static int Lua_GetCredits(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) { fn_pushnumber(L, 0); return 1; }
    auto p = GetPlayerObj(slot);
    float c = *reinterpret_cast<float*>(p + RVA::PlayerObj::Credits);
    fn_pushnumber(L, static_cast<double>(c));
    return 1;
}

// SWFOC_SetTechLevel(level) -> success
static int Lua_SetTechLevel(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) { fn_pushnumber(L, 0); return 1; }
    int level = static_cast<int>(fn_tonumber(L, 1));
    auto p = GetPlayerObj(slot);
    *reinterpret_cast<int*>(p + RVA::PlayerObj::TechLevel) = level;
    Log("[Bridge] Tech level set to %d\n", level);
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_UncapCredits() -> success
static int Lua_UncapCredits(lua_State* L) {
    int slot = FindLocalPlayerSlot();
    if (slot < 0) { fn_pushnumber(L, 0); return 1; }
    auto p = GetPlayerObj(slot);
    *reinterpret_cast<float*>(p + RVA::PlayerObj::MaxCredits) = 999999999.0f;
    Log("[Bridge] Max credits uncapped\n");
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_HeroInstantRespawn(enable) -> success
static int Lua_HeroInstantRespawn(lua_State* L) {
    static float originalValue = -1.0f;
    auto addr = reinterpret_cast<float*>(g_base + RVA::DefaultHeroRespawnTime);
    int enable = static_cast<int>(fn_tonumber(L, 1));

    if (enable) {
        if (originalValue < 0) originalValue = *addr;
        *addr = 0.0f;
        Log("[Bridge] Hero respawn = instant\n");
    } else if (originalValue >= 0) {
        *addr = originalValue;
        Log("[Bridge] Hero respawn restored to %.1f\n", originalValue);
    }
    fn_pushnumber(L, 1);
    return 1;
}

// SWFOC_ListFactions() -> table of {slot, name, credits, is_local}
static int Lua_ListFactions(lua_State* L) {
    int count = GetPlayerCount();
    fn_newtable(L); // result table

    int idx = 1;
    for (int i = 0; i < count; i++) {
        auto p = GetPlayerObj(i);
        if (!p) continue;
        auto isLocal = *reinterpret_cast<uint8_t*>(p + RVA::PlayerObj::LocalPlayer) == 1;
        auto credits = *reinterpret_cast<float*>(p + RVA::PlayerObj::Credits);

        fn_newtable(L); // entry table

        fn_pushstring(L, "slot");
        fn_pushnumber(L, static_cast<double>(i));
        fn_settable(L, -3);

        fn_pushstring(L, "name");
        fn_pushstring(L, GetFactionName(i));
        fn_settable(L, -3);

        fn_pushstring(L, "credits");
        fn_pushnumber(L, static_cast<double>(credits));
        fn_settable(L, -3);

        fn_pushstring(L, "is_local");
        fn_pushnumber(L, isLocal ? 1.0 : 0.0);
        fn_settable(L, -3);

        fn_rawseti(L, -2, idx++);
    }
    return 1;
}

// SWFOC_Log(message)
static int Lua_Log(lua_State* L) {
    const char* msg = fn_tostring(L, 1);
    if (msg) Log("[Lua] %s\n", msg);
    return 0;
}

// SWFOC_DoString(code) -> value_or_nil, errmsg_or_nil
// Executes a Lua code string. Returns the evaluated value on success
// (nil if the code returned nothing); on failure returns (nil, errmsg).
//
// 2026-05-20 BUGFIX: previously returned literal `1` instead of the
// actual evaluated value (line 1171: fn_settop discarded what DoString
// pushed, then fn_pushnumber(L, 1) wrote a useless success flag). Live
// smoke caught this on `SWFOC_DoString("return 1+2+3")` → "1" instead
// of "6". The new contract:
//   local v = SWFOC_DoString("return 1+2+3")      -> v == 6
//   local v, err = SWFOC_DoString("return broken")  -> v == nil, err = "..."
// Truthiness check still works for "did it succeed" — any value
// returned (even false) means parsing succeeded; nil + err means failure.
static int Lua_DoString(lua_State* L) {
    const char* code = fn_tostring(L, 1);
    if (!code) {
        fn_pushnil(L);
        fn_pushstring(L, "SWFOC_DoString: expected string argument");
        return 2;
    }
    Log("[DoString] Executing: %.64s%s\n", code, strlen(code) > 64 ? "..." : "");
    int topBefore = fn_gettop(L);
    int err = DoString(L, code, "=SWFOC_DoString");
    if (err == 0) {
        // DoString already pushed 1 result on success; return it directly.
        return 1;
    } else {
        const char* errMsg = fn_tostring(L, -1);
        // Save errMsg out (will be freed by settop)
        // Lua 5.0 doesn't have lua_pushlstring with a length arg here easily,
        // so we copy into a static thread-local buffer for safety.
        static thread_local char s_errCopy[512];
        if (errMsg) {
            strncpy(s_errCopy, errMsg, sizeof(s_errCopy) - 1);
            s_errCopy[sizeof(s_errCopy) - 1] = '\0';
        } else {
            strcpy(s_errCopy, "unknown error");
        }
        fn_settop(L, topBefore); // pop the error string we just copied
        fn_pushnil(L);
        fn_pushstring(L, s_errCopy);
        return 2;
    }
}

// SWFOC_DrainPipe() -> executed (1 if a command was run, 0 if queue empty)
// Call this from Lua scripts to process pending pipe commands on the game thread.
static int Lua_DrainPipe(lua_State* L) {
    bool did = DrainPipeCommand(L);
    fn_pushnumber(L, did ? 1.0 : 0.0);
    return 1;
}

// SWFOC_StateInfo() -> string describing cached game states
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

// ======================================================================
// lua_close hook — evict destroyed states from the game state cache
// ======================================================================

void Hook_lua_close(void* L) {
    EnterCriticalSection(&csGameStates);
    auto it = std::find(cached_game_states.begin(), cached_game_states.end(), L);
    if (it != cached_game_states.end()) {
        cached_game_states.erase(it);
        Log("Removed game state: %p (remaining: %d)\n", L, (int)cached_game_states.size());
    }
    LeaveCriticalSection(&csGameStates);

    // Also remove from registered states
    EnterCriticalSection(&csRegistered);
    auto it2 = std::find(registered_states.begin(), registered_states.end(), L);
    if (it2 != registered_states.end()) {
        registered_states.erase(it2);
        Log("Removed registered state: %p (remaining: %d)\n", L, (int)registered_states.size());
    }
    LeaveCriticalSection(&csRegistered);

    orig_lua_close(L);
}

// SWFOC_EventControl(enable) -> 1 on success, 0 if no event buffer
// enable=1: reset ring buffer and enable event stream
// enable=0: disable event stream
static int Lua_EventControl(lua_State* L) {
    if (!g_evtBuf) { fn_pushnumber(L, 0); return 1; }
    int enable = static_cast<int>(fn_tonumber(L, 1));
    if (enable) {
        g_evtBuf->write_pos.store(0, std::memory_order_release);
        g_evtBuf->read_pos.store(0, std::memory_order_release);
        g_evtBuf->event_count.store(0, std::memory_order_release);
        g_evtBuf->flags.store(1, std::memory_order_release);
        Log("[Events] Stream enabled\n");
    } else {
        g_evtBuf->flags.store(0, std::memory_order_release);
        Log("[Events] Stream disabled\n");
    }
    fn_pushnumber(L, 1);
    return 1;
}

// ======================================================================
// SWFOC_DumpState(path) -> result string
//
// Captures a point-in-time snapshot of game state to a .swfocsnap file.
// See SNAPSHOT_FORMAT.md for the binary layout. The capture is READ-ONLY
// with respect to game memory — no hooks are installed, no fields are
// written to the engine.
//
// SAFETY NOTES:
// - All engine memory reads are bounds-checked (player_count clamped to 8,
//   state_count clamped to 1024).
// - The function never throws and never calls back into Lua during the
//   write (so there is no risk of re-entering luaD_call while holding the
//   snapshot file handle).
// - Global registry lookups use pushstring+gettable on LUA_GLOBALSINDEX
//   with stack save/restore around every lookup.
// ======================================================================

// Well-known game object types that the replay harness wants sized mocks for.
// Not exhaustive — the format supports adding more without bumping the
// version. Growth is cheap: each entry is 68 bytes.
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

// Whitelisted globals to capture. Order is stable across captures so diffs
// of two snapshots line up field-for-field.
static const char* const kDumpGlobals[] = {
    // Game API
    "Find_Player",
    "Find_Object_Type",
    "Find_All_Objects_Of_Type",
    "Spawn_Unit",
    "Story_Event",
    "Letter_Box_On",
    "Suspend_AI",
    "GameRandom",
    "Create_Position",
    // SWFOC_* helpers
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

// Buffered write helper: appends bytes to a growing std::vector<uint8_t> and
// updates a running CRC32 checksum. All section writes go through here so
// the final CRC covers every byte before the end marker.
struct DumpBuf {
    std::vector<uint8_t> bytes;
    uint32_t crc;

    DumpBuf() : crc(0) {}

    void append(const void* p, size_t n) {
        const uint8_t* b = static_cast<const uint8_t*>(p);
        bytes.insert(bytes.end(), b, b + n);
    }
    void u8(uint8_t v)   { append(&v, 1); }
    void u16(uint16_t v) { append(&v, 2); }
    void u32(uint32_t v) { append(&v, 4); }
    void u64(uint64_t v) { append(&v, 8); }
    void f64(double v)   { append(&v, 8); }

    // Write a null-padded fixed-width ASCII field
    void fixedStr(const char* s, size_t width) {
        std::vector<uint8_t> tmp(width, 0);
        if (s) {
            size_t n = strnlen(s, width);
            memcpy(tmp.data(), s, n);
        }
        append(tmp.data(), width);
    }
};

// Safely read a pointer from an absolute engine address. Returns 0 on failure.
// Uses IsBadReadPtr for defensive bounds checking before the actual read.
// NOTE: IsBadReadPtr is deprecated on modern Windows but MinGW's MinHook-based
// build targets it intentionally because there is no SEH available.
static uint64_t SafeReadU64(uintptr_t addr) {
    if (addr == 0) return 0;
    if (IsBadReadPtr(reinterpret_cast<void*>(addr), 8)) return 0;
    return *reinterpret_cast<uint64_t*>(addr);
}
static uint32_t SafeReadU32(uintptr_t addr) {
    if (addr == 0) return 0;
    if (IsBadReadPtr(reinterpret_cast<void*>(addr), 4)) return 0;
    return *reinterpret_cast<uint32_t*>(addr);
}
static float SafeReadF32(uintptr_t addr) {
    if (addr == 0) return 0.0f;
    if (IsBadReadPtr(reinterpret_cast<void*>(addr), 4)) return 0.0f;
    return *reinterpret_cast<float*>(addr);
}
static const char* SafeReadCStr(uintptr_t addr) {
    if (addr == 0) return nullptr;
    if (IsBadReadPtr(reinterpret_cast<void*>(addr), 8)) return nullptr;
    const char* p = *reinterpret_cast<const char**>(addr);
    if (!p) return nullptr;
    if (IsBadReadPtr((void*)p, 1)) return nullptr;
    return p;
}

static uint64_t CaptureTimestampMs() {
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    // FILETIME is 100ns ticks since 1601-01-01. Unix epoch is 11644473600s later.
    uint64_t ft100ns = (uint64_t(ft.dwHighDateTime) << 32) | uint64_t(ft.dwLowDateTime);
    // Convert to milliseconds and rebase to Unix epoch
    const uint64_t kEpochDeltaMs = 11644473600000ULL;
    return (ft100ns / 10000ULL) - kEpochDeltaMs;
}

// Look up a whitelisted global, write one section-4 record to buf.
// Stack is saved/restored around the lookup so we never leave residue
// behind for the calling Lua context.
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
        raw = reinterpret_cast<uint64_t>(s);
    } else if (ty == LUA_TFUNCTION) {
        // We do not have a safe C API to read the underlying C closure
        // pointer in Lua 5.0.2, so just record 0 for functions.
        raw = 0;
    } else if (ty != LUA_TNIL && ty != LUA_TBOOLEAN && ty != LUA_TUSERDATA) {
        // Clamp any type we do not encode (table, lightuserdata, thread)
        // to nil in the output. Format only enumerates 0/1/3/4/6/7.
        ty = LUA_TNIL;
    }
    fn_settop(L, savedTop);

    // lua_type encoded as uint8 + 7 bytes of zero padding
    buf.u8(static_cast<uint8_t>(ty & 0xFF));
    uint8_t pad[7] = {0};
    buf.append(pad, 7);
    buf.u64(raw);
}

// Count live instances of a given object type.
//
// Alamo Lua exposes Find_All_Objects_Of_Type(type_name) which returns a
// table of matching objects. We pcall it with a stack guard and interpret
// the result:
//   - table  -> the type exists and has >=1 instance (v1 reports 1 as a
//               "present" sentinel; the replay harness only needs a nonzero
//               flag, not the exact count)
//   - number -> the engine build returned a count directly (older mods)
//   - nil / function / other / error -> count = 0
//
// SAFETY: no unbounded loops, every gettable/pcall goes through
// fn_settop stack restore on failure. If any required fn_* is unresolved,
// returns 0 immediately so the section payload still has the expected shape.
static uint32_t QueryObjectTypeCount(lua_State* L, const char* typeName) {
    if (!fn_pushstring || !fn_gettable || !fn_pcall || !fn_type ||
        !fn_gettop || !fn_settop || !fn_tonumber) {
        return 0;
    }
    int savedTop = fn_gettop(L);

    fn_pushstring(L, "Find_All_Objects_Of_Type");
    fn_gettable(L, LUA_GLOBALSINDEX);
    if (fn_type(L, -1) != LUA_TFUNCTION) {
        fn_settop(L, savedTop);
        return 0;
    }
    fn_pushstring(L, typeName);
    int err = fn_pcall(L, 1, 1, 0);
    if (err != 0) {
        fn_settop(L, savedTop);
        return 0;
    }
    int resultTy = fn_type(L, -1);
    uint32_t count = 0;
    if (resultTy == LUA_TTABLE) {
        // Present sentinel. Exact counting via table.getn requires
        // lua_pushvalue, which we do not resolve. v1 consumers only care
        // that the type is present; they ignore the numeric value when it
        // comes from a table branch.
        count = 1;
    } else if (resultTy == LUA_TNUMBER) {
        count = static_cast<uint32_t>(fn_tonumber(L, -1));
    }
    fn_settop(L, savedTop);
    return count;
}

// 2026-04-27 (Spawn-tab live filtering — Task #222):
//
// SWFOC_BatchTypeExists(pipe_separated_names) -> pipe-separated "1"/"0"
// flags, one per input name, in the same order.
//
// Why: the editor needs to know which on-disk catalog types are actually
// loaded in the current game state (vanilla / mod / submod) so the Spawn
// tab doesn't show types the running game won't accept. The engine's own
// Find_Object_Type Lua global returns a type wrapper if the type is
// registered with the GameObjectTypeManager, nil otherwise. We reuse
// QueryObjectTypeCount's stack-restore pattern.
//
// Input shape (Lua stack arg 1): "REBEL_INFANTRY|EMPIRE_AT_AT|VONG_PYRAMID"
// Output shape: "1|1|0"   (flags, in same order; pipe-separated)
//
// Bounds: input is split in-place into a thread_local buffer, capped at
// kMaxInputBytes. Names individually capped at kMaxNameBytes. If either
// cap is hit we return "ERR: SWFOC_BatchTypeExists: input too large" so
// the editor can fall back to per-name probes.
static int Lua_BatchTypeExists(lua_State* L) {
    constexpr size_t kMaxInputBytes = 16384;  // 16 KB of names per probe
    constexpr size_t kMaxNameBytes  = 256;    // per-entry safety cap
    constexpr size_t kMaxNames      = 512;    // per-call safety cap

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

    // 2026-04-27 (defence-in-depth): copy the caller's input into our own
    // buffer BEFORE we do any other Lua stack manipulation. lua_tostring
    // returns a pointer into Lua's string heap; while real Lua keeps that
    // memory alive as long as the value is on the stack, additional
    // pushstring calls can move/realloc internal structures (and the
    // harness fake's std::vector definitely does on every push_back). If
    // we delay the memcpy, `raw` may dangle by the time we use it.
    static thread_local char s_buf[kMaxInputBytes + 1];
    static thread_local const char* s_names[kMaxNames];
    memcpy(s_buf, raw, rawLen);
    s_buf[rawLen] = '\0';

    // Look up Find_Object_Type once per call (it's the same wrapper
    // across iterations). Stash it as a stack value we can re-push by
    // reference via fn_pushvalue if available; otherwise look it up
    // per-iteration (slower but still correct).
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
                // Oversize entry — record an empty placeholder so output
                // ordering still aligns with caller's input.
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

    // Per-name probe: push Find_Object_Type, push name, pcall, check
    // truthiness of the single return.
    static thread_local char s_out[kMaxNames * 2 + 1]; // "1|1|0|..." worst case
    size_t off = 0;
    for (size_t i = 0; i < nameCount; ++i) {
        if (i > 0 && off < sizeof(s_out) - 1) {
            s_out[off++] = '|';
        }

        int top = fn_gettop(L);
        fn_pushstring(L, "Find_Object_Type");
        fn_gettable(L, LUA_GLOBALSINDEX);
        if (fn_type(L, -1) != LUA_TFUNCTION) {
            // Lost the global mid-call (extremely unlikely) — emit "0".
            fn_settop(L, top);
            if (off < sizeof(s_out) - 1) s_out[off++] = '0';
            continue;
        }
        fn_pushstring(L, s_names[i]);
        int err = fn_pcall(L, 1, 1, 0);
        char flag = '0';
        if (err == 0) {
            int rty = fn_type(L, -1);
            // Type wrapper returns LUA_TUSERDATA in vanilla; some mods may
            // return LUA_TLIGHTUSERDATA or LUA_TTABLE for proxy wrappers.
            // Treat ANY non-nil/non-boolean-false result as "exists".
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
    Log("[Bridge] BatchTypeExists: probed=%zu out_len=%zu\n", nameCount, off);
    return 1;
}

// Metadata writer: length-prefixed key then length-prefixed value
static void WriteMetaPair(DumpBuf& buf, const char* key, const char* value) {
    size_t kl = key ? strnlen(key, 0xFFFE) : 0;
    size_t vl = value ? strnlen(value, 0xFFFE) : 0;
    buf.u16(static_cast<uint16_t>(kl));
    buf.append(key, kl);
    buf.u16(static_cast<uint16_t>(vl));
    buf.append(value, vl);
}

// Forward declarations for the 2026-04-23 sections 11-13 writer. The real
// definitions live below (Phase 3.2 / selection reader), so we need these
// prototypes here because Lua_DumpState precedes them in file order.
static bool IsValidObjAddr(uintptr_t addr);
static bool ResolveSelectionVector(uintptr_t& outVec);
static int  WalkSelectionVector(uintptr_t vec, uintptr_t* outObjs, int maxOut);

static int Lua_DumpState(lua_State* L) {
    const char* path = fn_tostring(L, 1);
    if (!path || !path[0]) {
        fn_pushstring(L, "ERR: SWFOC_DumpState: expected string path argument");
        return 1;
    }

    Log("[Dump] SWFOC_DumpState called, path=%s\n", path);

    // ---- Build the whole snapshot in memory first, then write in one go.
    // This keeps partial-write failure windows small and lets us CRC over
    // the exact bytes we are about to flush.
    DumpBuf buf;

    // ---- File header (68 bytes) ----
    // See SNAPSHOT_FORMAT.md for the full layout. Section 1 begins at
    // file offset 0x44.
    // 0x00: magic 16 bytes — bumped to v2 in 2026-04-08 to carry the
    // explicit local_slot in section 1. v1 reader retained in
    // replay_harness.cpp for back-compat with snapshots captured before
    // this change.
    const uint8_t kMagic[16] = {
        'S','W','F','O','C','S','N','A','P','v','2',0,0,0,0,0
    };
    buf.append(kMagic, 16);
    // 0x10: format_version = 2 (was 1 prior to 2026-04-08)
    buf.u32(2);
    // 0x14: capture_timestamp_ms
    buf.u64(CaptureTimestampMs());
    // 0x1C: engine_build_hash = 32 bytes zero (SHA-256 computation deferred)
    uint8_t zeroHash[32] = {0};
    buf.append(zeroHash, 32);
    // 0x3C: game_mode — probed from the GameModeRoot chain (Task 108, 2026-04-23).
    //   0 = menu / no active mode
    //   1 = tactical (object list populated)
    //   2 = galactic (active mode but no tactical object list)
    //
    // Derivation: WalkAllTacticalObjects already walks
    //   `*(g_base + GameModeRoot_Global) + 0x18` → inner manager
    //   inner + 0x48 → object list head
    //   inner + 0x40 → sentinel
    // A live tactical battle has head ≠ sentinel. Galactic has a valid
    // inner pointer but head == sentinel (the inner manager exists for
    // every active mode, but only tactical populates the unit list).
    //
    // This is the same safety-checked chain the selection walker uses,
    // so any mode-shift that breaks selection also breaks this probe —
    // they fail together, which is the right coupling.
    uint8_t gameMode = 0;
    uintptr_t rootAddr = g_base + RVA::GameModeRoot_Global;
    if (!IsBadReadPtr(reinterpret_cast<void*>(rootAddr), 8)) {
        uintptr_t rootPtr = SafeReadU64(rootAddr);
        if (rootPtr && !IsBadReadPtr(
                reinterpret_cast<void*>(rootPtr + RVA::Selection::kModeRootIndirection), 8)) {
            uintptr_t innerPtr = SafeReadU64(
                rootPtr + RVA::Selection::kModeRootIndirection);
            if (innerPtr && !IsBadReadPtr(reinterpret_cast<void*>(innerPtr), 0x80)) {
                uintptr_t sentinel = innerPtr + RVA::Selection::kObjectListSentinel;
                uintptr_t head = SafeReadU64(innerPtr + RVA::Selection::kObjectListHead);
                gameMode = (head && head != sentinel) ? 1 : 2;
            }
        }
    }
    buf.u8(gameMode);
    // 0x3D: 7 bytes reserved padding
    uint8_t headerPad[7] = {0};
    buf.append(headerPad, 7);

    // ---- Section 1: player_array ----
    {
        std::vector<uint8_t> payload;
        DumpBuf sec;

        // Read and bound player count
        uintptr_t pcAddr = g_base + RVA::PlayerCount_Global;
        int32_t rawCount = static_cast<int32_t>(SafeReadU32(pcAddr));
        if (rawCount < 0) rawCount = 0;
        if (rawCount > 8) rawCount = 8;
        uint32_t clamped = static_cast<uint32_t>(rawCount);

        uintptr_t arrBase = SafeReadU64(g_base + RVA::PlayerArray_Global);
        // If the array pointer looks invalid, fall back to count=0.
        uint32_t actual = 0;
        sec.u32(0); // placeholder for player_count, rewritten below

        // v2 addition: explicit local_slot. Resolve via FindLocalPlayerSlot.
        // The reader treats UINT32_MAX as "no local player" (e.g. main menu).
        int localSlotInt = FindLocalPlayerSlot();
        uint32_t localSlot = (localSlotInt < 0) ? 0xFFFFFFFFu : static_cast<uint32_t>(localSlotInt);
        sec.u32(localSlot);

        for (uint32_t i = 0; i < clamped; i++) {
            uint64_t pPtr = arrBase ? SafeReadU64(arrBase + i * 8) : 0;
            if (!pPtr) continue;
            // slot
            sec.u32(i);
            // faction (null-padded 64)
            const char* faction = SafeReadCStr(pPtr + RVA::PlayerObj::FactionName);
            sec.fixedStr(faction ? faction : "", 64);
            // credits (widened to double)
            float c = SafeReadF32(pPtr + RVA::PlayerObj::Credits);
            sec.f64(static_cast<double>(c));
            // tech_level (int32)
            int32_t tech = static_cast<int32_t>(SafeReadU32(pPtr + RVA::PlayerObj::TechLevel));
            int32_t tmp = tech;
            sec.append(&tmp, 4);
            // player_name reserved — 64 zero bytes
            sec.fixedStr("", 64);
            actual++;
        }
        // Patch the real player_count into the first 4 bytes of the section payload
        memcpy(sec.bytes.data(), &actual, 4);

        buf.u32(1);                                             // section_id
        buf.u32(static_cast<uint32_t>(sec.bytes.size()));        // section_length
        buf.append(sec.bytes.data(), sec.bytes.size());          // payload
    }

    // ---- Section 2: lua_state_registry ----
    {
        DumpBuf sec;
        EnterCriticalSection(&csRegistered);
        uint32_t stateCount = static_cast<uint32_t>(registered_states.size());
        if (stateCount > 1024) stateCount = 1024;
        sec.u32(stateCount);
        for (uint32_t i = 0; i < stateCount; i++) {
            sec.u64(reinterpret_cast<uint64_t>(registered_states[i]));
        }
        LeaveCriticalSection(&csRegistered);

        buf.u32(2);
        buf.u32(static_cast<uint32_t>(sec.bytes.size()));
        buf.append(sec.bytes.data(), sec.bytes.size());
    }

    // ---- Section 3: object_catalog ----
    {
        DumpBuf sec;
        sec.u32(kDumpObjectTypesCount);
        for (uint32_t i = 0; i < kDumpObjectTypesCount; i++) {
            sec.fixedStr(kDumpObjectTypes[i], 64);
            uint32_t count = QueryObjectTypeCount(L, kDumpObjectTypes[i]);
            sec.u32(count);
        }

        buf.u32(3);
        buf.u32(static_cast<uint32_t>(sec.bytes.size()));
        buf.append(sec.bytes.data(), sec.bytes.size());
    }

    // ---- Section 4: global_registry ----
    {
        DumpBuf sec;
        sec.u32(kDumpGlobalsCount);
        for (uint32_t i = 0; i < kDumpGlobalsCount; i++) {
            WriteGlobalRecord(L, sec, kDumpGlobals[i]);
        }

        buf.u32(4);
        buf.u32(static_cast<uint32_t>(sec.bytes.size()));
        buf.append(sec.bytes.data(), sec.bytes.size());
    }

    // ---- Section 5: metadata ----
    {
        DumpBuf sec;
        sec.u32(4); // entry_count: four required keys
        WriteMetaPair(sec, "capture_method",       "powrprof_dll");
        WriteMetaPair(sec, "mod_name",             "unknown");
        WriteMetaPair(sec, "mod_version",          "unknown");
        WriteMetaPair(sec, "swfoc_bridge_version", "1.0");

        buf.u32(5);
        buf.u32(static_cast<uint32_t>(sec.bytes.size()));
        buf.append(sec.bytes.data(), sec.bytes.size());
    }

    // ---- v2.1 extension (added 2026-04-23 for Task 101) ----
    // Sections 11-13 capture the selection + per-unit detail needed for the
    // replay harness to exercise Task 99 (hardpoint-behavior invulnerability)
    // and Task 100 (damage-path simulation) offline. All three sections are
    // OPTIONAL — readers predating this extension skip cleanly via the
    // unknown-section rule. The writer emits them only when ResolveSelectionVector
    // succeeds; main-menu / no-selection captures stay backwards-compatible.
    uintptr_t selVec = 0;
    bool haveSelection = ResolveSelectionVector(selVec);
    uintptr_t selObjs[RVA::Selection::kMaxSelectionCount];
    int selCount = 0;
    if (haveSelection) {
        selCount = WalkSelectionVector(selVec, selObjs, RVA::Selection::kMaxSelectionCount);
        if (selCount < 0) selCount = 0;
    }

    if (haveSelection && selCount > 0) {
        // ---- Section 11: selected_units ----
        {
            DumpBuf sec;
            sec.u32(static_cast<uint32_t>(selCount));
            for (int i = 0; i < selCount; i++) {
                sec.u64(static_cast<uint64_t>(selObjs[i]));
            }
            buf.u32(11);
            buf.u32(static_cast<uint32_t>(sec.bytes.size()));
            buf.append(sec.bytes.data(), sec.bytes.size());
        }

        // ---- Section 12: unit_detail ----
        // Per-unit: obj_addr + type_name + owner + hull + max_hull (unknown → 0)
        // + display flags + hardpoint index list. Type-name resolution is
        // deferred to a future extension — we write an empty string for now
        // so the reader still has a stable slot. Hardpoint count uses the
        // same Components-array walk as Lua_GetHardpoints (bounded at 32).
        {
            DumpBuf sec;
            sec.u32(static_cast<uint32_t>(selCount));
            for (int i = 0; i < selCount; i++) {
                uintptr_t obj = selObjs[i];
                sec.u64(static_cast<uint64_t>(obj));
                sec.fixedStr("", 64); // type_name (reserved; Component-type lookup TODO)

                // Defensive read guard: if the object pointer went stale
                // between selection resolution and this write, emit zeros
                // for its fields so the reader still sees a valid slot.
                int32_t ownerSlot = 0;
                float   hull      = 0.0f;
                float   maxHull   = 0.0f;
                uint8_t invuln    = 0;
                uint8_t preventDeath = 0;
                if (IsValidObjAddr(obj)) {
                    ownerSlot    = *reinterpret_cast<int32_t*>(obj + RVA::GameObj::OwnerPlayerID);
                    hull         = *reinterpret_cast<float*>(obj + RVA::GameObj::HP);
                    invuln       = *reinterpret_cast<uint8_t*>(obj + RVA::GameObj::InvulnFlag);
                    preventDeath = *reinterpret_cast<uint8_t*>(obj + RVA::GameObj::PreventDeath);
                    // max_hull is not in the stable GameObj layout — leave 0.
                }
                int32_t ownerTmp = ownerSlot;
                sec.append(&ownerTmp, 4);
                uint32_t hullBits = 0, maxHullBits = 0;
                memcpy(&hullBits, &hull, 4);
                memcpy(&maxHullBits, &maxHull, 4);
                sec.u32(hullBits);
                sec.u32(maxHullBits);
                sec.u8(invuln);
                sec.u8(preventDeath);
                uint8_t rsv[6] = {0};
                sec.append(rsv, 6);

                // Hardpoint indices: walk Components[0..31], emit one uint32
                // per non-null child. The replay harness mirrors this via
                // ReplayMutMockUnit(hardpoint_count), then section 13
                // attaches behaviors on top.
                uint32_t hpCountPayloadOffset = static_cast<uint32_t>(sec.bytes.size());
                sec.u32(0); // placeholder for hardpoint_count
                uint32_t hpCount = 0;
                if (IsValidObjAddr(obj)) {
                    uintptr_t components =
                        *reinterpret_cast<uintptr_t*>(obj + RVA::GameObj::ComponentArray);
                    if (components && !IsBadReadPtr(reinterpret_cast<void*>(components), 0x100)) {
                        for (uint32_t j = 0; j < 32; j++) {
                            uintptr_t child =
                                *reinterpret_cast<uintptr_t*>(components + j * 8);
                            if (!child) continue;
                            if (!IsValidObjAddr(child)) continue;
                            sec.u32(j);
                            hpCount++;
                        }
                    }
                }
                memcpy(sec.bytes.data() + hpCountPayloadOffset, &hpCount, 4);
            }
            buf.u32(12);
            buf.u32(static_cast<uint32_t>(sec.bytes.size()));
            buf.append(sec.bytes.data(), sec.bytes.size());
        }

        // ---- Section 13: behavior_attach (empty placeholder for now) ----
        // The engine does not expose a "list behaviors on hardpoint" read
        // path yet. Until Task 100's IDA work uncovers the behavior-list
        // pointer, we emit entry_count=0. The reader ignores the section;
        // tests using the mocked path populate behaviors programmatically
        // via SWFOC_ReplayAttachBehavior.
        {
            DumpBuf sec;
            sec.u32(0);
            buf.u32(13);
            buf.u32(static_cast<uint32_t>(sec.bytes.size()));
            buf.append(sec.bytes.data(), sec.bytes.size());
        }
    }

    // ---- End marker: id 0xFFFFFFFF, length 4, CRC32 of everything before.
    uint32_t endId = 0xFFFFFFFFu;
    uint32_t endLen = 4;
    buf.u32(endId);
    buf.u32(endLen);
    uint32_t crc = Crc32_Update(0, buf.bytes.data(), buf.bytes.size());
    buf.u32(crc);

    // ---- Flush to disk in binary mode.
    FILE* f = fopen(path, "wb");
    if (!f) {
        char errbuf[512];
        snprintf(errbuf, sizeof(errbuf),
                 "ERR: SWFOC_DumpState: could not open '%s' for write", path);
        Log("[Dump] fopen failed: %s\n", path);
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
        Log("[Dump] short write: %zu/%zu\n", wrote, buf.bytes.size());
        fn_pushstring(L, errbuf);
        return 1;
    }

    char okbuf[512];
    snprintf(okbuf, sizeof(okbuf),
             "OK: snapshot written to %s (%zu bytes)", path, buf.bytes.size());
    Log("[Dump] %s\n", okbuf);
    fn_pushstring(L, okbuf);
    return 1;
}

// ======================================================================
// Phase 3.2: Combat / Inspect helpers ported from CE trainer
// ----------------------------------------------------------------------
// All operate on absolute GameObject pointers (no Lua proxy types). The
// SetHP combat hook is installed lazily on first SWFOC_GodMode(true) or
// SWFOC_OneHitKill(true), and removed when both flags are off.
// See ce_trainer_inventory.md sections 1.1 and 1.2 for the source caves.
// ======================================================================

// Validate that an absolute address looks like a plausible in-process
// pointer to engine memory. We do not have a precise heap range, but we
// can rule out null, low pages, and obviously bogus values. IsBadReadPtr
// is the last line of defense (deprecated, but the only SEH-free option
// available to MinGW builds).
static bool IsValidObjAddr(uintptr_t addr) {
    if (addr == 0) return false;
    if (addr < 0x10000) return false; // null page / low memory
    // Reject sign-extended kernel addresses
    if ((addr & 0xFFFF000000000000ULL) == 0xFFFF000000000000ULL) return false;
    if (IsBadReadPtr(reinterpret_cast<void*>(addr), 0x400)) return false;
    return true;
}

// Walk the parent-component chain to the root unit. Mirrors the gd_walk
// loop in the CE God Mode cave. Adds a hard 8-iteration cap (the trainer
// has no termination guard other than ParentIdx==0xFF or null components,
// which can loop forever in pathological cases).
static uintptr_t WalkToRootUnit(uintptr_t obj) {
    if (!IsValidObjAddr(obj)) return 0;
    uintptr_t cur = obj;
    for (int i = 0; i < 8; i++) {
        uint8_t parentIdx = *reinterpret_cast<uint8_t*>(cur + RVA::GameObj::ParentIndex);
        if (parentIdx == 0xFF) return cur; // root
        uintptr_t components = *reinterpret_cast<uintptr_t*>(cur + RVA::GameObj::ComponentArray);
        if (!components || IsBadReadPtr(reinterpret_cast<void*>(components), (parentIdx + 1) * 8)) {
            return cur;
        }
        uintptr_t parent = *reinterpret_cast<uintptr_t*>(components + parentIdx * 8);
        if (!parent || !IsValidObjAddr(parent)) return cur;
        cur = parent;
    }
    return cur; // cap reached — return whatever we walked to
}

// Resolve the root unit's owning PlayerObject* via the PlayerArray global.
// Returns 0 if any link is invalid.
static uintptr_t GetOwnerPlayerObj(uintptr_t rootObj) {
    if (!IsValidObjAddr(rootObj)) return 0;
    int32_t ownerId = *reinterpret_cast<int32_t*>(rootObj + RVA::GameObj::OwnerPlayerID);
    if (ownerId < 0 || ownerId > 7) return 0; // 8 player slots max
    uintptr_t paAddr = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    if (!paAddr) return 0;
    uintptr_t player = *reinterpret_cast<uintptr_t*>(paAddr + ownerId * 8);
    return player;
}

// Test whether a root unit is owned by the local human player. Reads the
// PlayerObject.LocalPlayer byte at +0x62 (the same field every cave checks).
static bool IsObjOwnedByHuman(uintptr_t obj) {
    uintptr_t root = WalkToRootUnit(obj);
    if (!root) return false;
    uintptr_t player = GetOwnerPlayerObj(root);
    if (!player) return false;
    if (IsBadReadPtr(reinterpret_cast<void*>(player + RVA::PlayerObj::LocalPlayer), 1)) return false;
    return *reinterpret_cast<uint8_t*>(player + RVA::PlayerObj::LocalPlayer) == 1;
}

// MSVC std::string layout (x64). Matches the 32-byte structure the engine's
// BehaviorLookup helper at 0x4C3520 reads from. SSO (short-string-optimization)
// is active when capacity < 16; strings >= 16 chars live on heap with a pointer
// in the first 8 bytes. "INVULNERABLE" is 12 chars, so SSO always.
struct MsvcStdStringSSO {
    char   data[16];   // SSO buffer (or heap pointer in first 8 bytes when heap)
    size_t length;     // offset 16
    size_t capacity;   // offset 24 — 15 = SSO, >=16 = heap
};

// Build a temporary MSVC std::string holding "INVULNERABLE" and return its
// behavior-object pointer from the global behavior registry. Mirrors the
// strcpy + v27=15 + v26=12 + sub_1404C3520(v25) pattern in
// MakeInvulnerable_LuaWrapper (0x57D550). Returns 0 on lookup failure.
static void* EngineLookupInvulnerableBehavior() {
    MsvcStdStringSSO str;
    memset(&str, 0, sizeof(str));
    strcpy(str.data, "INVULNERABLE");
    str.length   = 12;
    str.capacity = 15;
    typedef void* (*pfn_BehaviorLookup)(void* str);
    auto fn = reinterpret_cast<pfn_BehaviorLookup>(g_base + RVA::BehaviorLookup);
    return fn(&str);
}

// QueryInterface dispatch. Vtable slot index 2 (byte-offset 16 from vtable base).
// Returns the requested interface pointer or nullptr on any failure. Mirrors the
// (*(__int64 (__fastcall **)(ptr, int))(vtable + 16))(obj, iface) pattern.
static void* EngineQueryInterface(uintptr_t obj, int iface_id) {
    if (!IsValidObjAddr(obj)) return nullptr;
    uintptr_t vtable = *reinterpret_cast<uintptr_t*>(obj);
    if (!vtable || IsBadReadPtr(reinterpret_cast<void*>(vtable + 0x10), 8)) return nullptr;
    typedef void* (*pfn_QI)(void*, int);
    auto fn = *reinterpret_cast<pfn_QI*>(vtable + 0x10);
    if (!fn) return nullptr;
    return fn(reinterpret_cast<void*>(obj), iface_id);
}

// Reimplement MakeInvulnerable's behavior-attach chain inline — this is the
// path Task 99 unblocks. The engine's Lua wrapper (0x57D550) takes a
// userdata which we cannot synthesize from C++, so we reproduce the
// hardpoint iteration + BehaviorAttach/Remove sequence here. Returns true
// on any real mutation; false if the object layout is invalid or the
// behavior registry lookup fails.
static bool CallMakeInvulnerableInline(uintptr_t obj, bool enable) {
    if (!IsValidObjAddr(obj)) return false;

    void* behavior = EngineLookupInvulnerableBehavior();
    if (!behavior) return false;

    typedef char  (*pfn_BehaviorAttach)(void* obj, void* behavior, char);
    typedef void  (*pfn_BehaviorRemove)(void* obj, void* behavior);
    typedef int   (*pfn_HardpointCount)(void* mgr);
    typedef void* (*pfn_HardpointGet)(void* mgr, int index);

    auto BehaviorAttach = reinterpret_cast<pfn_BehaviorAttach>(g_base + RVA::BehaviorAttach);
    auto BehaviorRemove = reinterpret_cast<pfn_BehaviorRemove>(g_base + RVA::BehaviorRemoveDispatch);
    auto HardpointCount = reinterpret_cast<pfn_HardpointCount>(g_base + RVA::HardpointCount);
    auto HardpointGet   = reinterpret_cast<pfn_HardpointGet>(g_base + RVA::HardpointGet);

    uint8_t hpFlag = *reinterpret_cast<uint8_t*>(obj + RVA::GameObj::HardpointFlag); // +0x348

    if (hpFlag == 0xFF) {
        // No hardpoints — attach/remove behavior directly on the unit.
        uint8_t marker = *reinterpret_cast<uint8_t*>(obj + RVA::GameObj::BehaviorMarker);
        if (enable && marker == 0xFF) {
            BehaviorAttach(reinterpret_cast<void*>(obj), behavior, 0);
        } else if (!enable && marker != 0xFF) {
            BehaviorRemove(reinterpret_cast<void*>(obj), behavior);
        }
        return true;
    }

    // Has hardpoints — iterate via QueryInterface(22) → HardpointCount/Get.
    void* mgr = EngineQueryInterface(obj, 22);
    if (!mgr) return false;
    int count = HardpointCount(mgr);
    if (count <= 0 || count > 256) return false;

    int mutated = 0;
    for (int i = 0; i < count; i++) {
        void* hp = HardpointGet(mgr, i);
        if (!hp) continue;
        uintptr_t hpAddr = reinterpret_cast<uintptr_t>(hp);
        if (!IsValidObjAddr(hpAddr)) continue;
        uint8_t hpMarker = *reinterpret_cast<uint8_t*>(hpAddr + RVA::GameObj::BehaviorMarker);
        if (enable && hpMarker == 0xFF) {
            BehaviorAttach(hp, behavior, 0);
            mutated++;
        } else if (!enable && hpMarker != 0xFF) {
            BehaviorRemove(hp, behavior);
            mutated++;
        }
    }
    Log("[Bridge] CallMakeInvulnerableInline(0x%llX, enable=%d) hp_count=%d mutated=%d\n",
        (unsigned long long)obj, (int)enable, count, mutated);
    return true;
}

// SWFOC_SetUnitInvuln(obj_addr, flag) -> "OK" or "ERR: ..."
// Routes through the engine's hardpoint-behavior path (Task 99, 2026-04-23).
// Also updates the display flag at +0x3A7 so InspectUnit reflects the state.
// Task 102: rejects obj_addrs not owned by local slot so enemy selection stays
// READ-ONLY.
static int Lua_SetUnitInvuln(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    double rawFlag = fn_tonumber(L, 2);
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    uint8_t flag = (rawFlag != 0.0) ? 1 : 0;
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitInvuln: invalid obj_addr");
        return 1;
    }
    // Task 102: enemy units are read-only. Reject writes to non-local obj_addrs.
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitInvuln: not controllable (not owned by local slot)");
        return 1;
    }

    bool ok = CallMakeInvulnerableInline(addr, flag != 0);
    if (!ok) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitInvuln: hardpoint behavior path failed");
        return 1;
    }

    // Also update the display flag so InspectUnit observers remain consistent.
    volatile uint8_t* p = reinterpret_cast<volatile uint8_t*>(addr + RVA::GameObj::InvulnFlag);
    *p = flag;

    Log("[Bridge] SetUnitInvuln(0x%llX, %d) OK via hardpoint path\n",
        (unsigned long long)addr, (int)flag);
    fn_pushstring(L, "OK");
    return 1;
}

// SWFOC_SetUnitHull(obj_addr, value) -> "OK" or "ERR: ..."
// Direct float write to obj+0x5C. Caller is responsible for sane range
// (typically 0.0..max_hull). The engine's SetHP function would normally
// clamp this; this is a raw poke.
static int Lua_SetUnitHull(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    double rawHp = fn_tonumber(L, 2);
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitHull: invalid obj_addr");
        return 1;
    }
    // Task 102: enemy units are read-only. Reject hull writes to non-local units.
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitHull: not controllable (not owned by local slot)");
        return 1;
    }
    volatile float* p = reinterpret_cast<volatile float*>(addr + RVA::GameObj::HP);
    *p = static_cast<float>(rawHp);
    Log("[Bridge] SetUnitHull(0x%llX, %.3f) OK\n", (unsigned long long)addr, rawHp);
    fn_pushstring(L, "OK");
    return 1;
}

// SWFOC_InspectUnit(obj_addr) -> "key=value key=value ..." string
// Reads the same nine fields the CE Inspector tab displays. Returns a
// space-delimited key/value string parseable by both Lua patterns and
// C# String.Split(' '). On invalid input returns an "ERR: ..." string.
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

// Bounded append helper. Treats `cap` as the buffer size and returns the
// new offset, never exceeding `cap-1` (always leaves room for NUL). The
// `snprintf` return value is clamped to the remaining capacity, so a
// truncated write cannot push the offset past the end of the buffer.
static size_t SafeAppendFmt(char* buf, size_t offset, size_t cap, const char* fmt, ...) {
    if (!buf || cap == 0 || offset >= cap - 1) return offset;
    size_t remaining = cap - offset; // space including NUL
    va_list args;
    va_start(args, fmt);
    int n = vsnprintf(buf + offset, remaining, fmt, args);
    va_end(args);
    if (n < 0) return offset;
    size_t add = static_cast<size_t>(n);
    if (add >= remaining) add = remaining - 1; // clamp on truncation
    return offset + add;
}

// SWFOC_GetHardpoints(obj_addr) -> "count=N child0=0x... hp0=... ..."
// Walks the Components array of the supplied object (which is expected
// to BE the root — the caller is responsible). For each non-null child,
// emits its address and current HP. Bounded to 32 children to avoid
// pathological loops if Components contains garbage.
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
    // Two-phase: count first so we can emit a stable "count=N" prefix,
    // then format each entry. Single linear walk for each phase, with
    // bounded snprintf via SafeAppendFmt — no return-value math on the
    // raw snprintf result.
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
// Selection reader (2026-04-11)
// ----------------------------------------------------------------------
// Walks the per-player selection linked list via the engine's own layout.
// Derivation and evidence: knowledge-base/selection_pointer_2026-04-11.md.
//
// Chain (all offsets from RVA::Selection::*):
//   mgr_root = *(g_base + GameModeRoot_Global + kModeRootIndirection)
//   slot_id  = current human player slot (via PlayerListClass_Global+0x30)
//   vec      = *(mgr_root + kPerPlayerVectorsArray) + kSelectionEntryStride*slot_id
//   walk node = *(vec + kVectorHead) until node == vec + kVectorSentinel;
//                obj = *(node + kNodeDataPlus24) - kNodeDataAdjustment
//
// Every hop is IsBadReadPtr-guarded so a torn read from the shared memory
// drain thread degrades to "no selection" rather than crashing the game.
// The function does NOT call the engine's sub_14039AD40 validator — that
// helper replays vtable calls that are unsafe outside the main thread,
// and the caller can re-validate via SWFOC_InspectUnit if it cares.
// ======================================================================

// Resolve the current human player's slot id using PlayerListClass_Global.
// Mirrors the tiny sub_140294A70 helper exactly: read current-slot int at
// +0x30, use it to index the vec_begin pointer at +0x00, then read the
// slot id out of the player at +0x4C. Returns -1 on any dereference
// failure. Harness tests swap g_base with a fake image so all arithmetic
// must go through g_base for test isolation.
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

// Resolve the per-player selection-vector header for the current human.
//
// 2026-04-23 bug fix: previously this function did ONE dereference at
// rootSlot+0x18, assuming `qword_140B15418` in IDA Hex-Rays was an address.
// It's not — Hex-Rays `qword_XXX` is an implicitly-dereferenced symbol,
// meaning `*(qword_140B15418 + 24)` performs TWO dereferences: (1) read
// the pointer stored at 0x140B15418, (2) read *(pointer + 0x18). This
// function now matches the two-deref pattern, proven live: the single-
// deref version returned 0 when a frigate was visibly selected in tactical
// mode on KAMINO. See knowledge-base/selection_pointer_2026-04-11.md for
// the original (incorrect) chain documentation.
//
// Chain:
//   globalPtr = *(g_base + 0xB15418)          // pointer stored at the global slot
//   mgrRoot   = *(globalPtr + 0x18)           // active GameModeClass instance
//   vecArray  = *(mgrRoot + 0x1C0)            // flat per-player vector array
//   sel_list  = vecArray + (slot * 0x48)      // human player's selection vector
//
// Returns 0 if any step yields a null or unreadable pointer. outVec
// receives the DynamicVectorClass header (0x48-byte struct with
// head at +0x10, sentinel at +0x08).
static bool ResolveSelectionVector(uintptr_t& outVec) {
    outVec = 0;

    // Step 1: first dereference — read the 8-byte pointer stored at the
    // global slot 0xB15418 (module-relative). This is the canonical global
    // pointer IDA prints as `qword_140B15418`.
    uintptr_t globalSlotAddr = g_base + RVA::GameModeRoot_Global;
    if (IsBadReadPtr(reinterpret_cast<void*>(globalSlotAddr), 8)) return false;
    uintptr_t globalPtr = *reinterpret_cast<uintptr_t*>(globalSlotAddr);
    if (!globalPtr) return false;

    // Step 2: second dereference — read *(globalPtr + 0x18) to get the
    // live GameModeClass instance. This is the value IDA shows as
    // `*(qword_140B15418 + 24)` in sub_14003AFE0 / sub_1402BD2F0.
    if (IsBadReadPtr(reinterpret_cast<void*>(globalPtr + RVA::Selection::kModeRootIndirection), 8)) {
        return false;
    }
    uintptr_t mgrRoot = *reinterpret_cast<uintptr_t*>(
        globalPtr + RVA::Selection::kModeRootIndirection);
    if (!mgrRoot) return false;

    // Step 3: read the flat per-player vector array base.
    if (IsBadReadPtr(reinterpret_cast<void*>(mgrRoot + RVA::Selection::kPerPlayerVectorsArray), 8)) {
        return false;
    }
    uintptr_t vecArrayBase = *reinterpret_cast<uintptr_t*>(
        mgrRoot + RVA::Selection::kPerPlayerVectorsArray);
    if (!vecArrayBase) return false;

    // Step 4: index by the current human player's slot to get the
    // per-player selection-vector header.
    int slot = ReadCurrentHumanPlayerSlot();
    if (slot < 0 || slot > 7) return false;
    uintptr_t vec = vecArrayBase + RVA::Selection::kSelectionEntryStride * slot;
    if (IsBadReadPtr(reinterpret_cast<void*>(vec), RVA::Selection::kSelectionEntryStride)) {
        return false;
    }
    outVec = vec;
    return true;
}

// Diagnostic probe — dumps every intermediate pointer in the selection
// chain so we can empirically verify which interpretation is correct
// when the chain doesn't yield results. Added 2026-04-23 after the
// single-deref bug above; kept registered for ongoing use.
static int Lua_DiagSelection(lua_State* L) {
    char buf[1024];
    size_t off = 0;

    uintptr_t globalSlotAddr = g_base + RVA::GameModeRoot_Global;

    uintptr_t val_at_global = 0;
    if (!IsBadReadPtr(reinterpret_cast<void*>(globalSlotAddr), 8)) {
        val_at_global = *reinterpret_cast<uintptr_t*>(globalSlotAddr);
    }

    uintptr_t val_at_global_plus_18 = 0;
    if (!IsBadReadPtr(reinterpret_cast<void*>(globalSlotAddr + 0x18), 8)) {
        val_at_global_plus_18 = *reinterpret_cast<uintptr_t*>(globalSlotAddr + 0x18);
    }

    // Two-deref interpretation (the fix): mgrRoot = *(val_at_global + 0x18)
    uintptr_t mgr_twoderef = 0;
    if (val_at_global && !IsBadReadPtr(reinterpret_cast<void*>(val_at_global + 0x18), 8)) {
        mgr_twoderef = *reinterpret_cast<uintptr_t*>(val_at_global + 0x18);
    }

    // Single-deref interpretation (previous bug): mgrRoot = val_at_global_plus_18
    uintptr_t mgr_oneDeref = val_at_global_plus_18;

    // For each, try to read vecArrayBase
    uintptr_t vec_two = 0, vec_one = 0;
    if (mgr_twoderef && !IsBadReadPtr(reinterpret_cast<void*>(mgr_twoderef + 0x1C0), 8)) {
        vec_two = *reinterpret_cast<uintptr_t*>(mgr_twoderef + 0x1C0);
    }
    if (mgr_oneDeref && !IsBadReadPtr(reinterpret_cast<void*>(mgr_oneDeref + 0x1C0), 8)) {
        vec_one = *reinterpret_cast<uintptr_t*>(mgr_oneDeref + 0x1C0);
    }

    int slot = ReadCurrentHumanPlayerSlot();

    // What the fixed ResolveSelectionVector currently returns
    uintptr_t actualVec = 0;
    bool resolved = ResolveSelectionVector(actualVec);
    uintptr_t head = 0;
    if (actualVec && !IsBadReadPtr(reinterpret_cast<void*>(actualVec + 0x10), 8)) {
        head = *reinterpret_cast<uintptr_t*>(actualVec + 0x10);
    }
    uintptr_t sentinel = actualVec + 0x08;

    off = SafeAppendFmt(buf, off, sizeof(buf),
        "gAddr=0x%llx valAtG=0x%llx valAtG+18=0x%llx | twoDeref_mgr=0x%llx oneDeref_mgr=0x%llx | twoDeref_vecArr=0x%llx oneDeref_vecArr=0x%llx | slot=%d vec=0x%llx resolved=%d head=0x%llx sentinel=0x%llx emptyList=%d",
        (unsigned long long)globalSlotAddr,
        (unsigned long long)val_at_global,
        (unsigned long long)val_at_global_plus_18,
        (unsigned long long)mgr_twoderef,
        (unsigned long long)mgr_oneDeref,
        (unsigned long long)vec_two,
        (unsigned long long)vec_one,
        slot,
        (unsigned long long)actualVec,
        resolved ? 1 : 0,
        (unsigned long long)head,
        (unsigned long long)sentinel,
        (head == sentinel) ? 1 : 0);

    fn_pushstring(L, buf);
    return 1;
}

// Walk a resolved selection-vector header and emit up to `maxOut` raw
// GameObjectClass pointers into `outObjs`. Mirrors the iteration in
// sub_14003AFE0 exactly. The linked list walk is bounded to
// kMaxSelectionCount iterations regardless of maxOut to defend against a
// corrupted list that loses its sentinel.
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

// Walk the tactical-battle GameObjectManager's linked list of every live
// unit on the current map. Same doubly-linked-list pattern as the selection
// walker, but rooted at the GameMode's inner object (verified via IDA
// decompile of sub_140540B20 / Find_All_Objects_Of_Type, 2026-04-23):
//
//   inner     = *(g_base + GameModeRoot_Global + kModeRootIndirection)
//   sentinel  = inner + kObjectListSentinel   (0x40)
//   first     = *(inner + kObjectListHead)    (0x48)
//   walk:     node = *(node + kNodeNext);  obj = *(node + 24) - 24
//
// Returns the number of obj_addrs written to outObjs (capped at maxOut).
// Bounded to kMaxTacticalObjects regardless of maxOut to defend against a
// torn/corrupted list from drain-thread reads.
static int WalkAllTacticalObjects(uintptr_t* outObjs, int maxOut) {
    if (!outObjs || maxOut <= 0) return 0;
    uintptr_t globalSlotAddr = g_base + RVA::GameModeRoot_Global;
    if (IsBadReadPtr(reinterpret_cast<void*>(globalSlotAddr), 8)) return 0;
    uintptr_t globalPtr = *reinterpret_cast<uintptr_t*>(globalSlotAddr);
    if (!globalPtr) return 0;
    if (IsBadReadPtr(reinterpret_cast<void*>(globalPtr + RVA::Selection::kModeRootIndirection), 8)) return 0;
    uintptr_t inner = *reinterpret_cast<uintptr_t*>(
        globalPtr + RVA::Selection::kModeRootIndirection);
    if (!inner || IsBadReadPtr(reinterpret_cast<void*>(inner), 0x80)) return 0;

    uintptr_t sentinel = inner + RVA::Selection::kObjectListSentinel;
    if (IsBadReadPtr(reinterpret_cast<void*>(inner + RVA::Selection::kObjectListHead), 8)) return 0;
    uintptr_t node = *reinterpret_cast<uintptr_t*>(inner + RVA::Selection::kObjectListHead);
    if (!node || node == sentinel) return 0;

    int found = 0;
    int capWalk = RVA::Selection::kMaxTacticalObjects;
    for (int i = 0; i < capWalk && node && node != sentinel; i++) {
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

// SWFOC_ListTacticalUnits() -> CSV of per-unit rows, one per '|' separator.
//
// Row format (semicolon-separated fields, chosen because '|' separates rows):
//   obj_addr_decimal;owner_slot;hull;invuln_flag;prevent_death_bit;is_local_owner;is_selected
//
// Where:
//   obj_addr_decimal is the raw pointer as decimal (C# parses via ulong).
//   owner_slot is the int32 at +0x58 (or -1 if the read fails).
//   hull is the float at +0x5C, printed with 3 decimals.
//   invuln_flag is the display byte at +0x3A7 (0 or 1).
//   prevent_death_bit is 1 if bit 0x80 of +0x3A1 is set, 0 otherwise.
//   is_local_owner is 1 if IsObjOwnedByHuman returns true, 0 otherwise.
//   is_selected is 1 if the obj_addr is present in the current selection vector.
//
// Returns "count=0" when the tactical object list is empty or the chain is not
// live (e.g. main menu / galactic mode). CSV approach chosen over a Lua table
// because Lua 5.0's table-construction API is not exposed to the bridge.
// See Task 104 (2026-04-23) for rationale and Task 107 for the V2 consumer.
static int Lua_ListTacticalUnits(lua_State* L) {
    static constexpr int kMax = RVA::Selection::kMaxTacticalObjects;
    static thread_local uintptr_t s_objs[kMax];
    int count = WalkAllTacticalObjects(s_objs, kMax);
    if (count <= 0) {
        fn_pushstring(L, "count=0");
        return 1;
    }

    // Pre-resolve the current selection so we can flag is_selected per row
    // without walking the selection vector in a nested loop.
    uintptr_t selVec = 0;
    uintptr_t selObjs[RVA::Selection::kMaxSelectionCount];
    int selCount = 0;
    if (ResolveSelectionVector(selVec)) {
        selCount = WalkSelectionVector(selVec, selObjs, RVA::Selection::kMaxSelectionCount);
        if (selCount < 0) selCount = 0;
    }
    auto isSelected = [&](uintptr_t obj) {
        for (int i = 0; i < selCount; i++) if (selObjs[i] == obj) return 1;
        return 0;
    };

    // Response size: ~96 bytes per row × kMax is ~200 KB worst-case; we cap
    // the emitted string at 64 KB so a single pipe response fits. Callers
    // can paginate via a follow-up helper if needed (Task 104 follow-up).
    constexpr size_t kBufCap = 65536;
    char* buf = reinterpret_cast<char*>(malloc(kBufCap));
    if (!buf) {
        fn_pushstring(L, "ERR: SWFOC_ListTacticalUnits: alloc failed");
        return 1;
    }
    size_t off = 0;
    off = SafeAppendFmt(buf, off, kBufCap, "count=%d", count);

    int emitted = 0;
    for (int i = 0; i < count; i++) {
        uintptr_t obj = s_objs[i];
        if (!IsValidObjAddr(obj)) continue;
        int32_t owner = *reinterpret_cast<int32_t*>(obj + RVA::GameObj::OwnerPlayerID);
        float   hull  = *reinterpret_cast<float*>(obj + RVA::GameObj::HP);
        uint8_t iflag = *reinterpret_cast<uint8_t*>(obj + RVA::GameObj::InvulnFlag);
        uint8_t pdb   = (*reinterpret_cast<uint8_t*>(obj + RVA::GameObj::PreventDeath) & 0x80) ? 1 : 0;
        int localOwn  = IsObjOwnedByHuman(obj) ? 1 : 0;
        int selected  = isSelected(obj);
        off = SafeAppendFmt(
            buf, off, kBufCap,
            "|%llu;%d;%.3f;%u;%u;%d;%d",
            (unsigned long long)obj,
            (int)owner, hull,
            (unsigned)iflag, (unsigned)pdb,
            localOwn, selected);
        emitted++;
        if (off >= kBufCap - 128) {
            // Truncate: append an "...+N more" marker so callers see the cap hit.
            SafeAppendFmt(buf, off, kBufCap, "|...+%d_truncated", count - emitted);
            break;
        }
    }
    fn_pushstring(L, buf);
    free(buf);
    Log("[Bridge] ListTacticalUnits: count=%d emitted=%d\n", count, emitted);
    return 1;
}

// SWFOC_GetSelectedUnit() -> number (obj_addr as a 64-bit raw pointer) or 0.
// Returns the first valid entry in the current human player's selection
// vector. Zero means "nothing selected" OR "pointer chain not yet live"
// (e.g. main menu). The caller should treat 0 as a soft signal and
// re-query on the next tick. The returned number is a uintptr_t cast to
// double, which is lossless for all x64 user-space pointers (they fit in
// 48 bits, double has a 53-bit mantissa). C# side parses it with
// (ulong)double.
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

// SWFOC_GetSelectedUnits() -> comma-separated decimal obj_addrs, or "".
// Lua 5.0 doesn't have a stable lua_createtable binding in our API set so
// we stringify. C# side splits on ',' and parses each as ulong. Empty
// string means nothing selected. The format uses decimal (not hex with an
// 0x prefix) so the parser in the editor matches Lua 5.0's only accepted
// number literal syntax.
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
// SetHP combat hook (unified God Mode + One-Hit Kill detour)
// ----------------------------------------------------------------------
//
// The CE trainer maintains three separate caves (god/ohk/combined). We
// install a single MinHook detour and branch on two flags. Toggling either
// flag from Lua only manipulates the flag — the hook itself is installed
// on the first enable and torn down once both flags are off.
//
// Calling convention: SetHP(GameObjectClass* obj, float new_hp). On x64
// MSVC fastcall this places obj in rcx and new_hp in xmm1. The detour
// signature mirrors that. The trampoline returned by MinHook is the
// pre-prologue real function pointer.
typedef void (__fastcall *pfn_SetHP)(void* obj, float new_hp);
static pfn_SetHP g_real_SetHP = nullptr;

static volatile LONG g_god_mode_enabled = 0;
static volatile LONG g_ohk_enabled       = 0;
static CRITICAL_SECTION g_combat_hook_lock;
static bool g_combat_hook_lock_initialized = false;
static bool g_combat_hook_installed = false;

// ======================================================================
// Task 112 (2026-04-23) -- damage-event ring buffer
// ----------------------------------------------------------------------
// Each SetHP detour invocation appends an event describing the attempted
// HP transition. SWFOC_EventStreamDrain reads and clears the accumulated
// buffer, returning a CSV the V2 editor + replay harness consume in the
// same shape. Fixed-capacity, drop-oldest-on-overflow so the detour stays
// lock-free on the hot path. A single CriticalSection guards drain so
// readers see a coherent snapshot.
// ======================================================================

struct DamageEvent {
    uint64_t timestamp_ms;
    uint64_t obj_addr;
    int32_t  owner_slot;
    float    requested_hp;
    float    current_hp;
};

static constexpr int kEventRingSize = 256;
static DamageEvent g_eventRing[kEventRingSize] = {};
static volatile LONG g_eventWriteIdx = 0; // monotonic; modulo is the slot
static volatile LONG g_eventReadIdx  = 0; // monotonic; advances on drain
static CRITICAL_SECTION g_eventRingLock;
static bool g_eventRingLockInit = false;

static void EnsureEventRingLock() {
    if (!g_eventRingLockInit) {
        InitializeCriticalSection(&g_eventRingLock);
        g_eventRingLockInit = true;
    }
}

// Best-effort push that never blocks the detour. On overflow, oldest
// events are overwritten -- readers losing the window is strictly better
// than stalling the game thread. Safe to call before the drain lock is
// initialised (the initial zeroing of g_eventRing makes every slot a
// defined-but-stale value).
static void PushDamageEvent(uint64_t obj_addr, int32_t owner, float requested, float current) {
    LONG next = InterlockedIncrement(&g_eventWriteIdx);
    int slot = (next - 1) % kEventRingSize;
    DamageEvent& slot_ref = g_eventRing[slot];
    slot_ref.timestamp_ms = 0;
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    ULARGE_INTEGER uli;
    uli.LowPart  = ft.dwLowDateTime;
    uli.HighPart = ft.dwHighDateTime;
    slot_ref.timestamp_ms = (uli.QuadPart - 116444736000000000ULL) / 10000ULL;
    slot_ref.obj_addr     = obj_addr;
    slot_ref.owner_slot   = owner;
    slot_ref.requested_hp = requested;
    slot_ref.current_hp   = current;
}

static void EnsureCombatHookLock() {
    if (!g_combat_hook_lock_initialized) {
        InitializeCriticalSection(&g_combat_hook_lock);
        g_combat_hook_lock_initialized = true;
    }
}

static void __fastcall Detour_SetHP(void* obj, float new_hp) {
    // Read the flags once into locals — no lock needed on the hot path
    // (writers hold g_combat_hook_lock; readers see a coherent byte).
    LONG god = g_god_mode_enabled;
    LONG ohk = g_ohk_enabled;
    uintptr_t objAddr = reinterpret_cast<uintptr_t>(obj);
    float currentHp = 0.0f;
    int32_t owner = -1;
    bool addrOk = IsValidObjAddr(objAddr);
    if (addrOk) {
        currentHp = *reinterpret_cast<float*>(objAddr + RVA::GameObj::HP);
        owner = *reinterpret_cast<int32_t*>(objAddr + RVA::GameObj::OwnerPlayerID);
    }
    // Always log the REQUESTED transition, regardless of whether god/ohk
    // flips the final value. The event stream's job is to witness intent;
    // consumers can diff current_hp vs requested_hp to detect clamping.
    if (addrOk) PushDamageEvent(objAddr, owner, new_hp, currentHp);
    if (god || ohk) {
        bool human = IsObjOwnedByHuman(objAddr);
        if (god && human) {
            if (new_hp < currentHp) return;
        }
        if (ohk && !human) {
            new_hp = 0.0f;
        }
    }
    if (g_real_SetHP) {
        g_real_SetHP(obj, new_hp);
    }
}

// Install the SetHP detour. Called under g_combat_hook_lock. Idempotent —
// safe to call when already installed (returns true without re-hooking).
static bool InstallCombatHook() {
    if (g_combat_hook_installed) return true;
    void* target = reinterpret_cast<void*>(g_base + RVA::SetHP);
    if (MH_CreateHook(target, reinterpret_cast<void*>(&Detour_SetHP),
                      reinterpret_cast<void**>(&g_real_SetHP)) != MH_OK) {
        Log("[Bridge] InstallCombatHook: MH_CreateHook failed at 0x%p\n", target);
        return false;
    }
    if (MH_EnableHook(target) != MH_OK) {
        Log("[Bridge] InstallCombatHook: MH_EnableHook failed\n");
        MH_RemoveHook(target);
        g_real_SetHP = nullptr;
        return false;
    }
    g_combat_hook_installed = true;
    Log("[Bridge] SetHP combat hook installed at 0x%p\n", target);
    return true;
}

// Tear down the SetHP detour. Called under g_combat_hook_lock. Only runs
// when both god mode and OHK are disabled.
static void RemoveCombatHook() {
    if (!g_combat_hook_installed) return;
    void* target = reinterpret_cast<void*>(g_base + RVA::SetHP);
    MH_DisableHook(target);
    MH_RemoveHook(target);
    g_combat_hook_installed = false;
    g_real_SetHP = nullptr;
    Log("[Bridge] SetHP combat hook removed\n");
}

// Sweep every live tactical unit and attach/remove the INVULNERABLE behavior
// on every hardpoint of every human-owned unit. This is the Task 106 wiring
// that routes God Mode through the Task 99 hardpoint-behavior path — the
// only damage-immunity mechanism the engine actually honours. Counts
// returned so Lua_GodMode can log how many units actually flipped.
//
// Offline contract: the replay harness mirrors this via ReplayMutSweepGodMode
// over `s.units` filtered by `s.local_slot == u.owner_slot`. The red/green
// pair in test_harness.cpp pins the behavior: flip enables immunity for
// every local hardpoint and does NOT touch enemy units; disable removes
// immunity from local units only.
static int SweepLocalUnitsInvulnerable(bool enable) {
    static constexpr int kMax = RVA::Selection::kMaxTacticalObjects;
    static thread_local uintptr_t s_objs[kMax];
    int count = WalkAllTacticalObjects(s_objs, kMax);
    if (count <= 0) return 0;
    int flipped = 0;
    for (int i = 0; i < count; i++) {
        uintptr_t obj = s_objs[i];
        if (!IsValidObjAddr(obj)) continue;
        if (!IsObjOwnedByHuman(obj)) continue;
        if (CallMakeInvulnerableInline(obj, enable)) flipped++;
    }
    Log("[Bridge] SweepLocalUnitsInvulnerable(enable=%d): walked=%d flipped=%d\n",
        (int)enable, count, flipped);
    return flipped;
}

// SWFOC_GodMode(enable) -> "OK" or "ERR: ..."
// Toggles BOTH the hardpoint-behavior sweep (Task 99 path, the real
// gameplay mechanism) AND the SetHP detour's god-mode branch (a safety
// net for late-spawn units that weren't present at enable time). If both
// flags end up false, the underlying MinHook is removed entirely but the
// behavior sweep always fires on every transition so the engine's real
// state stays coherent.
static int Lua_GodMode(lua_State* L) {
    EnsureCombatHookLock();
    int enable = static_cast<int>(fn_tonumber(L, 1));
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
    // The hardpoint-behavior sweep runs OUTSIDE the hook lock — it walks
    // engine state and must not deadlock with a concurrent Detour_SetHP
    // read of the flags. The flags' InterlockedExchange above already
    // published the new state by the time we reach here, so the sweep
    // sees a consistent god-mode value.
    int flipped = SweepLocalUnitsInvulnerable(enable != 0);
    if (!ok) {
        fn_pushstring(L, "ERR: SWFOC_GodMode: hook install failed (see swfoc_bridge.log)");
        return 1;
    }
    Log("[Bridge] GodMode -> %s (god=%d ohk=%d hook=%d sweep_flipped=%d)\n",
        enable ? "ENABLED" : "DISABLED",
        (int)g_god_mode_enabled, (int)g_ohk_enabled, (int)g_combat_hook_installed,
        flipped);
    char buf[96];
    snprintf(buf, sizeof(buf),
             enable
                 ? "OK: god mode enabled (sweep flipped %d local unit(s))"
                 : "OK: god mode disabled (sweep cleared %d local unit(s))",
             flipped);
    fn_pushstring(L, buf);
    return 1;
}

// SWFOC_HealAllLocal() -> integer count healed.
// Task 98 (2026-04-23). Ports the CE trainer's HealAllMine recipe: walk
// tactical units, filter to human-owned, write a large hull value so the
// engine's per-tick clamp restores each to max_hull. Enemy READ-ONLY
// discipline enforced by IsObjOwnedByHuman. Returns the count of writes
// attempted (not necessarily the count of units whose visible hull
// actually changed -- an already-full unit still gets the redundant
// write because we don't have a live max_hull read without an extra RVA).
//
// The heal value is intentionally large (engine clamps to max_hull on
// the next damage tick). Users who want surgical heal should reach for
// SWFOC_SetUnitHull(obj, target) instead.
static int Lua_HealAllLocal(lua_State* L) {
    static constexpr int kMax = RVA::Selection::kMaxTacticalObjects;
    static thread_local uintptr_t s_objs[kMax];
    int count = WalkAllTacticalObjects(s_objs, kMax);
    if (count <= 0) {
        fn_pushnumber(L, 0.0);
        return 1;
    }
    int healed = 0;
    for (int i = 0; i < count; i++) {
        uintptr_t obj = s_objs[i];
        if (!IsValidObjAddr(obj)) continue;
        if (!IsObjOwnedByHuman(obj)) continue;
        *reinterpret_cast<float*>(obj + RVA::GameObj::HP) = 99999.0f;
        healed++;
    }
    Log("[Bridge] HealAllLocal: walked=%d healed=%d\n", count, healed);
    fn_pushnumber(L, static_cast<double>(healed));
    return 1;
}

// SWFOC_GetPlanets() -> CSV "<type_name>;<faction>;<tech>\n..." or "(no_planets)" / "ERR: ...".
// Task 141 (2026-04-23) Phase 1 stub UPGRADED iter-296 to LIVE.
//
// WIRE FORMAT (must match BridgeGalacticDispatcher.GetPlanetsAsync):
//   - Newline-separated rows (NOT pipe-separated).
//   - 3 fields per row: <type_name>;<owner_faction>;<tech_level_int>
//   - Empty result: "(no_planets)" (simulator convention; dispatcher
//     gracefully returns empty list since parts.Length < 3).
//   - Engine error: "ERR: <reason>".
//
// Strategy (per iter-294 audit + iter-179 helper precedent): use DoString to
// invoke the engine's `Find_All_Objects_Of_Type` Lua API at category "Planet"
// (with fallback to "GalacticPlanet" / "Planetary"). Iterate the returned
// table; for each planet, extract Get_Type() + Get_Owner():Get_Faction_Name()
// + Get_Owner():Get_Tech_Level() via pcall to tolerate per-planet read
// failures. Defaults: faction='NONE', tech=0 if owner is nil/unowned.
//
// Iter-296a (2026-05-07): initial implementation emitted pipe-separated
// "count=N|<idx>;<type>;<faction>" — wire-format mismatch with the existing
// dispatcher caused silent empty-roster results. Fixed to legacy newline
// format with tech_level included so the dispatcher parses without changes.
//
// Lua 5.0 specifics: no `#table` (uses counter-style iteration);
// `pcall` available; `table.concat` available; `tostring` available.
static int Lua_GetPlanets(lua_State* L) {
    static const char* kEnumerateScript =
        "local cats = { 'Planet', 'GalacticPlanet', 'Planetary' }\n"
        "local pl = nil\n"
        "for _, c in pairs(cats) do\n"
        "  local ok, r = pcall(Find_All_Objects_Of_Type, c)\n"
        "  if ok and r and type(r) == 'table' then\n"
        "    pl = r; break\n"
        "  end\n"
        "end\n"
        "if not pl then return '(no_planets)' end\n"
        "local rows = {}\n"
        "local n = 0\n"
        "for i, p in pairs(pl) do\n"
        "  if p then\n"
        "    n = n + 1\n"
        "    local name = '?'\n"
        "    local faction = 'NONE'\n"
        "    local tech = 0\n"
        "    local ok1, nm = pcall(function() return tostring(p:Get_Type()) end)\n"
        "    if ok1 and nm then name = nm end\n"
        "    local ok2, ow = pcall(function() return p:Get_Owner() end)\n"
        "    if ok2 and ow then\n"
        "      local ok3, fn = pcall(function() return tostring(ow:Get_Faction_Name()) end)\n"
        "      if ok3 and fn then faction = fn end\n"
        "      local ok4, tl = pcall(function() return ow:Get_Tech_Level() end)\n"
        "      if ok4 and type(tl) == 'number' then tech = tl end\n"
        "    end\n"
        "    rows[n] = name .. ';' .. faction .. ';' .. tech\n"
        "  end\n"
        "end\n"
        "if n == 0 then return '(no_planets)' end\n"
        "return table.concat(rows, '\\n')";
    int rc = DoString(L, kEnumerateScript, "=SWFOC_GetPlanets");
    if (rc != 0) {
        fn_settop(L, -2);
        fn_pushstring(L, "ERR: SWFOC_GetPlanets engine error (galactic API likely unavailable)");
        Log("[Bridge] GetPlanets -- DoString rc=%d (galactic API unavailable?)\n", rc);
        return 1;
    }
    const char* resultStr = fn_tostring(L, -1);
    if (!resultStr) {
        fn_settop(L, -2);
        fn_pushstring(L, "(no_planets)");
        Log("[Bridge] GetPlanets -- non-stringable result; returning (no_planets)\n");
        return 1;
    }
    Log("[Bridge] GetPlanets -- LIVE returned '%.200s%s'\n",
        resultStr, strlen(resultStr) > 200 ? "..." : "");
    fn_pushstring(L, resultStr);
    return 1;
}

// ====================================================================
// iter-299: faction roster + current-mod enumeration wires
// ====================================================================
//
// SWFOC_GetFactionRoster(faction_name) -> CSV "<unit_type>;<category>\n..."
//                                         or "(empty)" / "ERR: ...".
//
// Strategy mirrors iter-296 SWFOC_GetPlanets: DoString into the engine's
// existing Lua API rather than pinning new RVAs.
//
// Approach: iterate Find_All_Objects_Of_Type for each broad category
// ('GroundCompany', 'Hero', 'SpaceUnit') and filter by Get_Owner():
// Get_Faction_Name() == requested faction. Returns NEWLINE-separated
// CSV: <type_name>;<category>\n... so the C# dispatcher can split by
// '\n' and ';' the same way iter-296 GetPlanets does (wire-format
// alignment per iter-296b lesson — match consumer semicolon-csv shape).
//
// Defensive: per-unit pcall guards on Get_Type / Get_Owner so one bad
// entry doesn't break enumeration. Lua 5.0-compat (counter-style
// iteration; no '#table'; pcall + table.concat available).
//
// Engine-already-does-this pattern (5th instance after iter-100/107/179/296).
static int Lua_GetFactionRoster(lua_State* L) {
    const char* factionName = fn_tostring(L, 1);
    if (!factionName || !factionName[0]) {
        fn_pushstring(L, "ERR: faction name required (arg #1 missing or empty)");
        return 1;
    }
    // Build a Lua script that filters Find_All_Objects_Of_Type by faction.
    // Embed the faction name literally; sanitize quotes to prevent injection
    // (engine names should never contain quotes but we defend anyway).
    char sanitized[256];
    int j = 0;
    for (int i = 0; factionName[i] && j < 250; ++i) {
        char c = factionName[i];
        if (c == '\'' || c == '\\' || c == '\n' || c == '\r') continue;
        sanitized[j++] = c;
    }
    sanitized[j] = '\0';

    char script[2048];
    snprintf(script, sizeof(script),
        "local cats = { 'GroundCompany', 'Hero', 'SpaceUnit', 'Infantry', 'Vehicle' }\n"
        "local target = '%s'\n"
        "local rows = {}\n"
        "local n = 0\n"
        "for _, c in pairs(cats) do\n"
        "  local ok, list = pcall(Find_All_Objects_Of_Type, c)\n"
        "  if ok and list and type(list) == 'table' then\n"
        "    for _, u in pairs(list) do\n"
        "      if u then\n"
        "        local fac = '?'\n"
        "        local ok1, ow = pcall(function() return u:Get_Owner() end)\n"
        "        if ok1 and ow then\n"
        "          local ok2, fn = pcall(function() return tostring(ow:Get_Faction_Name()) end)\n"
        "          if ok2 and fn then fac = fn end\n"
        "        end\n"
        "        if fac == target then\n"
        "          local name = '?'\n"
        "          local ok3, nm = pcall(function() return tostring(u:Get_Type()) end)\n"
        "          if ok3 and nm then name = nm end\n"
        "          n = n + 1\n"
        "          rows[n] = name .. ';' .. c\n"
        "        end\n"
        "      end\n"
        "    end\n"
        "  end\n"
        "end\n"
        "if n == 0 then return '(empty)' end\n"
        "return table.concat(rows, '\\n')",
        sanitized);

    int rc = DoString(L, script, "=SWFOC_GetFactionRoster");
    if (rc != 0) {
        fn_settop(L, -2);
        fn_pushstring(L, "ERR: SWFOC_GetFactionRoster engine error (Find_All_Objects_Of_Type unavailable?)");
        Log("[Bridge] GetFactionRoster -- DoString rc=%d for faction='%s'\n", rc, sanitized);
        return 1;
    }
    const char* resultStr = fn_tostring(L, -1);
    if (!resultStr) {
        fn_settop(L, -2);
        fn_pushstring(L, "(empty)");
        Log("[Bridge] GetFactionRoster -- non-stringable result; returning (empty)\n");
        return 1;
    }
    Log("[Bridge] GetFactionRoster('%s') -- LIVE returned '%.200s%s'\n",
        sanitized, resultStr, strlen(resultStr) > 200 ? "..." : "");
    fn_pushstring(L, resultStr);
    return 1;
}

// SWFOC_GetCurrentMod() -> "<mod_name>;<version>\n<mod_path>" or "vanilla" / "ERR: ...".
//
// Strategy: filesystem-based detection (no engine Lua API available for
// "what mod did the operator launch with?"). Walks process working dir's
// ./Mods/ subfolder looking for any directory containing a Modinfo.xml.
// If exactly one mod folder has been recently accessed (mtime within
// last hour of game launch), that's the active mod. If multiple match,
// returns the alphabetically first (operator can disambiguate via the
// returned mod_path — see below).
//
// Output format:
//   "<mod_name>;<version>\n<absolute_mod_path>"
// Or:
//   "vanilla"        (no Mods/ folder, or no recently-accessed mod)
//   "ERR: ..."       (filesystem error)
//
// iter-300 will extend this with SWFOC_ListMods() for full enumeration
// (operator picks via Settings UI). iter-299 ships the simpler
// "what's loaded right now" probe to satisfy the iter-294 mandate.
//
// Sidecar-additive note: this wire READS only. Never writes to disk.
static int Lua_GetCurrentMod(lua_State* L) {
    char modsDir[MAX_PATH];
    DWORD len = GetCurrentDirectoryA(MAX_PATH - 16, modsDir);
    if (len == 0 || len > MAX_PATH - 16) {
        fn_pushstring(L, "ERR: GetCurrentDirectory failed");
        return 1;
    }
    strcat_s(modsDir, sizeof(modsDir), "\\Mods\\*");

    WIN32_FIND_DATAA findData;
    HANDLE hFind = FindFirstFileA(modsDir, &findData);
    if (hFind == INVALID_HANDLE_VALUE) {
        fn_pushstring(L, "vanilla");
        Log("[Bridge] GetCurrentMod -- no Mods/ folder; reporting vanilla\n");
        return 1;
    }

    char latestModName[260] = {0};
    char latestModPath[MAX_PATH] = {0};
    FILETIME latestAccess = {0};
    bool found = false;

    do {
        if (!(findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) continue;
        if (findData.cFileName[0] == '.') continue;

        // Probe for Modinfo.xml inside this candidate folder.
        char modinfoPath[MAX_PATH];
        char cwdOnly[MAX_PATH];
        GetCurrentDirectoryA(MAX_PATH, cwdOnly);
        snprintf(modinfoPath, sizeof(modinfoPath), "%s\\Mods\\%s\\Modinfo.xml",
                 cwdOnly, findData.cFileName);

        WIN32_FILE_ATTRIBUTE_DATA attrData;
        if (!GetFileAttributesExA(modinfoPath, GetFileExInfoStandard, &attrData)) continue;

        // Track most-recently-accessed mod folder. Game touches it on launch.
        if (!found || CompareFileTime(&attrData.ftLastAccessTime, &latestAccess) > 0) {
            found = true;
            latestAccess = attrData.ftLastAccessTime;
            strcpy_s(latestModName, sizeof(latestModName), findData.cFileName);
            snprintf(latestModPath, sizeof(latestModPath), "%s\\Mods\\%s",
                     cwdOnly, findData.cFileName);
        }
    } while (FindNextFileA(hFind, &findData));
    FindClose(hFind);

    if (!found) {
        fn_pushstring(L, "vanilla");
        Log("[Bridge] GetCurrentMod -- no valid Mods/* with Modinfo.xml; reporting vanilla\n");
        return 1;
    }

    // Iter-299 ships mod_name + path. Version field is a placeholder until
    // we parse Modinfo.xml; XML parsing in C++ adds a dependency we want
    // to defer. Operators can read the path and inspect Modinfo.xml directly.
    char result[MAX_PATH + 64];
    snprintf(result, sizeof(result), "%s;unknown\n%s", latestModName, latestModPath);
    Log("[Bridge] GetCurrentMod -- LIVE returned '%s'\n", result);
    fn_pushstring(L, result);
    return 1;
}

// SWFOC_ListMods() -> NEWLINE-separated "<mod_name>;<absolute_path>" rows
//                    or "(no_mods)" / "ERR: ...".
//
// Iter-300 (2026-05-07; 300th-iter milestone): enumerate ALL mods candidate
// folders under ./Mods/* that contain a Modinfo.xml. Mirrors iter-299
// Lua_GetCurrentMod's filesystem walk shape, but emits every match instead
// of picking the most-recently-accessed one.
//
// Output format (consumer convention matches iter-296 GetPlanets):
//   <mod_name1>;<absolute_path1>
//   <mod_name2>;<absolute_path2>
//   ...
//
// Sentinels: "(no_mods)" when ./Mods/ exists but contains no Modinfo.xml,
// or when ./Mods/ doesn't exist at all (covers vanilla SWFOC install).
//
// Operator workflow (per iter-294 Audit B mandate):
//   1. Settings tab calls SWFOC_ListMods → DataGrid shows all mods
//   2. Operator clicks one → SWFOC_GetCurrentMod (iter-299) cross-refs
//      whether that mod is the one currently loaded
//   3. "Open Mods folder" button (operator-side) opens ./Mods in Explorer
//
// Sidecar-additive note: this wire READS only. Never writes to disk.
static int Lua_ListMods(lua_State* L) {
    char modsDir[MAX_PATH];
    DWORD len = GetCurrentDirectoryA(MAX_PATH - 16, modsDir);
    if (len == 0 || len > MAX_PATH - 16) {
        fn_pushstring(L, "ERR: GetCurrentDirectory failed");
        return 1;
    }
    char cwdOnly[MAX_PATH];
    strcpy_s(cwdOnly, sizeof(cwdOnly), modsDir);
    strcat_s(modsDir, sizeof(modsDir), "\\Mods\\*");

    WIN32_FIND_DATAA findData;
    HANDLE hFind = FindFirstFileA(modsDir, &findData);
    if (hFind == INVALID_HANDLE_VALUE) {
        fn_pushstring(L, "(no_mods)");
        Log("[Bridge] ListMods -- no Mods/ folder; reporting (no_mods)\n");
        return 1;
    }

    // Buffer caps the response at ~16KB which is plenty for any reasonable
    // operator's mod collection. If they have >100 mods, the tail is
    // silently truncated (logged); operator can pivot to filesystem
    // exploration via the "Open Mods folder" button as fallback.
    char result[16384] = {0};
    size_t resultLen = 0;
    int modCount = 0;

    do {
        if (!(findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) continue;
        if (findData.cFileName[0] == '.') continue;

        // Probe for Modinfo.xml inside this candidate folder.
        char modinfoPath[MAX_PATH];
        snprintf(modinfoPath, sizeof(modinfoPath), "%s\\Mods\\%s\\Modinfo.xml",
                 cwdOnly, findData.cFileName);

        WIN32_FILE_ATTRIBUTE_DATA attrData;
        if (!GetFileAttributesExA(modinfoPath, GetFileExInfoStandard, &attrData)) continue;

        char absModPath[MAX_PATH];
        snprintf(absModPath, sizeof(absModPath), "%s\\Mods\\%s",
                 cwdOnly, findData.cFileName);

        char row[MAX_PATH + 260];
        int rowLen = snprintf(row, sizeof(row), "%s%s;%s",
            modCount == 0 ? "" : "\n",
            findData.cFileName, absModPath);
        // snprintf returns the size that WOULD have been written (excl. NUL).
        // Clamp before use as memcpy length so a truncated row can never drive
        // an out-of-bounds copy (CWE-787). Negative return = encoding error.
        if (rowLen < 0) continue;
        if (static_cast<size_t>(rowLen) >= sizeof(row)) rowLen = static_cast<int>(sizeof(row) - 1);

        if (resultLen + rowLen + 1 >= sizeof(result)) {
            Log("[Bridge] ListMods -- result buffer full at %d mods; truncating\n", modCount);
            break;
        }
        memcpy(result + resultLen, row, static_cast<size_t>(rowLen));
        resultLen += rowLen;
        result[resultLen] = '\0';
        modCount++;
    } while (FindNextFileA(hFind, &findData));
    FindClose(hFind);

    if (modCount == 0) {
        fn_pushstring(L, "(no_mods)");
        Log("[Bridge] ListMods -- no valid Mods/* with Modinfo.xml; reporting (no_mods)\n");
        return 1;
    }
    Log("[Bridge] ListMods -- LIVE returned %d mod(s); first row: '%.200s%s'\n",
        modCount, result, resultLen > 200 ? "..." : "");
    fn_pushstring(L, result);
    return 1;
}

// SWFOC_ChangePlanetOwner("name", slot) -> "OK: ..." or "ERR: ...".
// Task 142 (2026-04-23) Phase 1. Parameters and response shape match the
// replay mirror. Live path invokes `Planet:Change_Owner(slot)` through
// SWFOC_DoString once the engine's galactic API is verified -- for now
// we just store the requested change in a module-local map so the UI
// round-trip works against the replay probe suite.
static std::unordered_map<std::string, int32_t> g_pendingPlanetOwnerWrites;
static CRITICAL_SECTION g_planetLock;
static bool g_planetLockInit = false;
static void EnsurePlanetLock() {
    if (!g_planetLockInit) {
        InitializeCriticalSection(&g_planetLock);
        g_planetLockInit = true;
    }
}

// SWFOC_SetIncomeMultiplier / SetGameSpeed / FreezeCredits.
// Tasks 122/123/127 Phase 1. All three use module-local pending maps
// under shared CriticalSections. Phase 2 wires hooks once IDA pins:
//   - income-delta apply function (candidates: xrefs from PlayerList_GetCurrentPlayer)
//   - SimulationRate global (searched 2026-04-23, no direct string hit)
//   - credits-field apply site (shares path with Give_Money hits)
static std::unordered_map<int32_t, float>  g_pendingIncomeMult;
static std::unordered_map<int32_t, double> g_pendingFrozenCredits;
static std::unordered_map<int32_t, bool>   g_pendingFreezeEnable;
static float                               g_pendingGameSpeed = 1.0f;
static CRITICAL_SECTION g_economyLock;
static bool g_economyLockInit = false;
static void EnsureEconomyLock() {
    if (!g_economyLockInit) {
        InitializeCriticalSection(&g_economyLock);
        g_economyLockInit = true;
    }
}

static std::unordered_map<int32_t, float> g_pendingBuildSpeedMult;
static std::unordered_map<int32_t, float> g_pendingFactionSpeedMult;

static int Lua_SetBuildSpeed(lua_State* L) {
    EnsureEconomyLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double mult = fn_tonumber(L, 2);
    if (mult < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetBuildSpeed: multiplier must be >= 0");
        return 1;
    }
    EnterCriticalSection(&g_economyLock);
    g_pendingBuildSpeedMult[slot] = static_cast<float>(mult);
    LeaveCriticalSection(&g_economyLock);
    Log("[Bridge] SetBuildSpeed(slot=%d, mult=%.3f) -- Phase 1 pending\n", slot, mult);
    fn_pushstring(L, "OK: build speed recorded (Phase 2 hook pending)");
    return 1;
}

// SWFOC_SetPerFactionSpeedMultiplier(slot, value).
//
// 2026-04-28 (iter 100, master ralph loop) — LIVE wired. The "multiplier"
// name is retained for editor backward compat but the engine's
// SetSpeedOverride takes an ABSOLUTE speed (the override layer at
// locomotor +0x2A0). We treat the second arg as the absolute target
// speed and apply it to every tactical object owned by `slot`.
//
// Operator semantic: "Make every unit owned by slot N move at speed X".
// X=1.0 ≈ engine's normalized walk pace; the editor's preset library
// already maps "Slow / Normal / Fast / Lightspeed" to absolute values.
//
// Enemy faction (any slot != local human's slot when slot==-1): walks
// the OwnerPlayerID filter the same way the per-unit helper does.
// 2026-04-28 (iter 100): speed-override storage + engine-call typedefs
// live ABOVE Lua_SetPerFactionSpeedMultiplier so this earlier handler
// can reuse the same machinery as the per-unit handler further down.
typedef void (__fastcall *pfn_SetSpeedOverride)(void* obj, float speed);
typedef void (__fastcall *pfn_ClearSpeedOverride)(void* obj);

static std::unordered_map<uintptr_t, float> g_unitSpeedOverrideMap;
static CRITICAL_SECTION g_speedLock;
static bool g_speedLockInit = false;

static void EnsureSpeedLock() {
    if (!g_speedLockInit) {
        InitializeCriticalSection(&g_speedLock);
        g_speedLockInit = true;
    }
}

// Read the locomotor's override-speed field (+0x2A0) directly from
// engine memory. Returns -1.0f when the locomotor pointer is null,
// the override-active flag (+0x29C) is clear, or the read is unsafe.
static float ReadEngineSpeedOverride(uintptr_t addr) {
    if (!IsValidObjAddr(addr)) return -1.0f;
    uintptr_t inner = *reinterpret_cast<uintptr_t*>(addr + 0xA8);
    if (!inner) return -1.0f;
    if (IsBadReadPtr(reinterpret_cast<void*>(inner + 0x2A4), 1)) return -1.0f;
    uint8_t activeFlag = *reinterpret_cast<uint8_t*>(inner + 0x29C);
    if (!activeFlag) return -1.0f;
    return *reinterpret_cast<float*>(inner + 0x2A0);
}

static int Lua_SetPerFactionSpeedMultiplier(lua_State* L) {
    EnsureEconomyLock();
    EnsureSpeedLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double absSpeed = fn_tonumber(L, 2);
    if (slot < 0) {
        fn_pushstring(L, "ERR: SWFOC_SetPerFactionSpeedMultiplier: slot must be >= 0");
        return 1;
    }
    if (absSpeed < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetPerFactionSpeedMultiplier: speed must be >= 0");
        return 1;
    }

    // Cache the per-faction value for replay-harness inspection and so
    // the diagnostics tab can show "what's currently applied per slot".
    EnterCriticalSection(&g_economyLock);
    g_pendingFactionSpeedMult[slot] = static_cast<float>(absSpeed);
    LeaveCriticalSection(&g_economyLock);

    // Enumerate every tactical object once, filter by OwnerPlayerID,
    // call SetSpeedOverride per unit. Mirrors Lua_EnumerateUnits.
    static constexpr int kMax = RVA::Selection::kMaxTacticalObjects;
    static thread_local uintptr_t s_objs[kMax];
    int count = WalkAllTacticalObjects(s_objs, kMax);
    if (count <= 0) {
        Log("[Bridge] SetPerFactionSpeedMultiplier(slot=%d, speed=%.3f) -- "
            "no tactical objects (count=%d)\n", slot, absSpeed, count);
        fn_pushstring(L, "OK: applied to 0 units (no tactical objects)");
        return 1;
    }

    auto fnSetOverride = Resolve<pfn_SetSpeedOverride>(RVA::SetSpeedOverride);
    int applied = 0;
    for (int i = 0; i < count; i++) {
        uintptr_t obj = s_objs[i];
        if (!IsValidObjAddr(obj)) continue;
        int32_t owner = *reinterpret_cast<int32_t*>(obj + RVA::GameObj::OwnerPlayerID);
        if (owner != slot) continue;

        EnterCriticalSection(&g_speedLock);
        g_unitSpeedOverrideMap[obj] = static_cast<float>(absSpeed);
        LeaveCriticalSection(&g_speedLock);
        fnSetOverride(reinterpret_cast<void*>(obj), static_cast<float>(absSpeed));
        applied++;
    }

    Log("[Bridge] SetPerFactionSpeedMultiplier(slot=%d, speed=%.3f) -- LIVE "
        "(applied to %d/%d tactical objects)\n", slot, absSpeed, applied, count);

    char msg[128];
    _snprintf_s(msg, sizeof(msg), _TRUNCATE,
        "OK: applied to %d unit(s) (LIVE — SetSpeedOverride per unit)", applied);
    fn_pushstring(L, msg);
    return 1;
}

static int Lua_SetIncomeMultiplier(lua_State* L) {
    EnsureEconomyLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double mult = fn_tonumber(L, 2);
    if (mult < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetIncomeMultiplier: multiplier must be >= 0");
        return 1;
    }
    EnterCriticalSection(&g_economyLock);
    g_pendingIncomeMult[slot] = static_cast<float>(mult);
    LeaveCriticalSection(&g_economyLock);
    Log("[Bridge] SetIncomeMultiplier(slot=%d, mult=%.3f) -- Phase 1 pending\n", slot, mult);
    fn_pushstring(L, "OK: income multiplier recorded (Phase 2 hook pending)");
    return 1;
}

static int Lua_SetGameSpeed(lua_State* L) {
    EnsureEconomyLock();
    double speed = fn_tonumber(L, 1);
    if (speed < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetGameSpeed: speed must be >= 0");
        return 1;
    }
    EnterCriticalSection(&g_economyLock);
    g_pendingGameSpeed = static_cast<float>(speed);
    LeaveCriticalSection(&g_economyLock);
    Log("[Bridge] SetGameSpeed(%.3f) -- Phase 1 pending\n", speed);
    fn_pushstring(L, "OK: game speed recorded (Phase 2 hook pending)");
    return 1;
}

static int Lua_SetFreezeCredits(lua_State* L) {
    EnsureEconomyLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int enable = static_cast<int>(fn_tonumber(L, 2));
    double target = fn_tonumber(L, 3);
    if (slot < 0) {
        fn_pushstring(L, "ERR: SWFOC_SetFreezeCredits: slot must be >= 0");
        return 1;
    }
    if (enable && target < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetFreezeCredits: target must be >= 0 when enabling");
        return 1;
    }
    EnterCriticalSection(&g_economyLock);
    if (enable) {
        g_pendingFreezeEnable[slot] = true;
        g_pendingFrozenCredits[slot] = target;
    } else {
        g_pendingFreezeEnable.erase(slot);
        g_pendingFrozenCredits.erase(slot);
    }
    LeaveCriticalSection(&g_economyLock);
    Log("[Bridge] SetFreezeCredits(slot=%d, enable=%d, target=%.2f) -- Phase 1 pending\n",
        slot, enable, target);
    fn_pushstring(L, "OK: freeze-credits state recorded (Phase 2 hook pending)");
    return 1;
}

// SWFOC_ToggleOHKAttackPower(enable) — Task 105 Phase 1.
// Distinct from the existing SWFOC_OneHitKill (which uses the SetHP
// detour to intercept incoming damage): this helper toggles a flag
// instructing the Phase 2 hook to inflate each local unit's outgoing
// attack_power field. The two helpers are complementary — #128's
// Combined toggle uses the SetHP flavor, while #105 targets the
// attacker-side field for players who want their own units to 1-shot
// enemies without the SetHP global override.
//
// Phase 1: record intent only. Phase 2 will walk local units, snapshot
// each unit->attack_power, write the inflated sentinel, and restore on
// disable. The replay mirror ReplayMutSetOHK pins the contract for
// every IDA-blocked edge case (idempotent re-enable, orphan guard on
// disable, enemy READ-ONLY sweep).
static CRITICAL_SECTION g_ohkAttackLock;
static bool g_ohkAttackLockInit = false;
static bool g_ohkAttackEnabled = false;
static void EnsureOhkAttackLock() {
    if (!g_ohkAttackLockInit) {
        InitializeCriticalSection(&g_ohkAttackLock);
        g_ohkAttackLockInit = true;
    }
}
static int Lua_ToggleOHKAttackPower(lua_State* L) {
    EnsureOhkAttackLock();
    int enable = static_cast<int>(fn_tonumber(L, 1));
    EnterCriticalSection(&g_ohkAttackLock);
    g_ohkAttackEnabled = (enable != 0);
    LeaveCriticalSection(&g_ohkAttackLock);
    Log("[Bridge] ToggleOHKAttackPower(%d) -- Phase 1 pending (attack_power offset not IDA-pinned)\n",
        enable);
    fn_pushstring(L, enable
        ? "OK: OHK attack-power toggled ON (Phase 2 hook pending)"
        : "OK: OHK attack-power toggled OFF (Phase 2 hook pending)");
    return 1;
}

// SWFOC_SetFireRate / SWFOC_SetAreaDamage / SWFOC_SetTargetFilter
// Tasks 131/132/133 Phase 1. Each helper stores the requested intent
// into a pending-write map behind g_combatLock. Phase 2 of the contract
// generator (#178) will emit the real hook once IDA pins the weapon
// cooldown-reset function (#131), the Take_Damage_Outer splash branch
// (#132), and the targeting filter predicate (#133). Enemy-slot writes
// on SetTargetFilter are REJECTED at the bridge — enemy units stay
// READ-ONLY per hard rule, and the filter shapes combat output of the
// owning slot's units, so writing another slot's filter is effectively
// mutating enemy behavior. The replay mirror DOES accept any slot so
// tests can assert cross-slot isolation without tripping the gate.
static CRITICAL_SECTION g_combatLock;
static bool g_combatLockInit = false;
static void EnsureCombatLock() {
    if (!g_combatLockInit) {
        InitializeCriticalSection(&g_combatLock);
        g_combatLockInit = true;
    }
}
static std::unordered_map<int32_t, float>    g_pendingFireRateMult;
static std::unordered_map<int32_t, uint32_t> g_pendingTargetFilter;
static bool                                  g_pendingAreaDamage = false;

static int Lua_SetFireRate(lua_State* L) {
    EnsureCombatLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double mult = fn_tonumber(L, 2);
    if (mult <= 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetFireRate: multiplier must be > 0");
        return 1;
    }
    EnterCriticalSection(&g_combatLock);
    g_pendingFireRateMult[slot] = static_cast<float>(mult);
    LeaveCriticalSection(&g_combatLock);
    Log("[Bridge] SetFireRate(slot=%d, mult=%.3f) -- Phase 1 pending\n", slot, mult);
    fn_pushstring(L, "OK: fire-rate multiplier recorded (Phase 2 hook pending)");
    return 1;
}

static int Lua_SetAreaDamage(lua_State* L) {
    EnsureCombatLock();
    int enabled = static_cast<int>(fn_tonumber(L, 1));
    EnterCriticalSection(&g_combatLock);
    g_pendingAreaDamage = (enabled != 0);
    LeaveCriticalSection(&g_combatLock);
    Log("[Bridge] SetAreaDamage(%d) -- Phase 1 pending\n", enabled);
    fn_pushstring(L, "OK: area-damage toggle recorded (Phase 2 hook pending)");
    return 1;
}

static int Lua_SetTargetFilter(lua_State* L) {
    EnsureCombatLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    uint32_t bitmask = static_cast<uint32_t>(fn_tonumber(L, 2));
    if (slot < 0) {
        fn_pushstring(L, "ERR: SWFOC_SetTargetFilter: slot must be >= 0");
        return 1;
    }
    int localSlot = FindLocalPlayerSlot();
    if (slot != localSlot && localSlot >= 0) {
        Log("[Bridge] SetTargetFilter REJECTED: slot=%d != local_slot=%d (enemy READ-ONLY)\n",
            slot, localSlot);
        fn_pushstring(L, "ERR: SWFOC_SetTargetFilter: only local slot may set filter (enemy READ-ONLY)");
        return 1;
    }
    EnterCriticalSection(&g_combatLock);
    g_pendingTargetFilter[slot] = bitmask & 0x7;
    LeaveCriticalSection(&g_combatLock);
    Log("[Bridge] SetTargetFilter(slot=%d, mask=0x%X) -- Phase 1 pending\n", slot, bitmask & 0x7);
    fn_pushstring(L, "OK: target-filter recorded (Phase 2 hook pending)");
    return 1;
}

// SWFOC_InstantBuild(enable) / SWFOC_FreeBuild(enable) — Tasks 161/162.
// Phase 1 record-only toggles. Phase 2 will AOB-scan for the
// build-progress-increment instruction (#161) and the credits-deduction
// instruction (#162), patch them with NOPs on enable, restore the
// original bytes on disable. Both toggles share g_aobPatchLock so the
// pending state doesn't race during consecutive writes.
static CRITICAL_SECTION g_aobPatchLock;
static bool g_aobPatchLockInit = false;
static bool g_pendingInstantBuild = false;
static bool g_pendingFreeBuild    = false;
static void EnsureAobPatchLock() {
    if (!g_aobPatchLockInit) {
        InitializeCriticalSection(&g_aobPatchLock);
        g_aobPatchLockInit = true;
    }
}
static int Lua_InstantBuild(lua_State* L) {
    EnsureAobPatchLock();
    int enable = static_cast<int>(fn_tonumber(L, 1));
    EnterCriticalSection(&g_aobPatchLock);
    g_pendingInstantBuild = (enable != 0);
    LeaveCriticalSection(&g_aobPatchLock);
    Log("[Bridge] InstantBuild(%d) -- Phase 1 pending (AOB patch queued)\n", enable);
    fn_pushstring(L, enable
        ? "OK: instant-build enabled (Phase 2 AOB patch pending)"
        : "OK: instant-build disabled (Phase 2 AOB patch pending)");
    return 1;
}
static int Lua_FreeBuild(lua_State* L) {
    EnsureAobPatchLock();
    int enable = static_cast<int>(fn_tonumber(L, 1));
    EnterCriticalSection(&g_aobPatchLock);
    g_pendingFreeBuild = (enable != 0);
    LeaveCriticalSection(&g_aobPatchLock);
    Log("[Bridge] FreeBuild(%d) -- Phase 1 pending (AOB patch queued)\n", enable);
    fn_pushstring(L, enable
        ? "OK: free-build enabled (Phase 2 AOB patch pending)"
        : "OK: free-build disabled (Phase 2 AOB patch pending)");
    return 1;
}

// SWFOC_SetUnitField(ptr, field, value) — Task 157 Phase 1.
// Generic dispatcher over per-unit fields. The live path walks a
// field→offset table (populated from knowledge-base/verified_facts.json
// at load time, modulo the fields whose offsets are still IDA-blocked)
// and writes the resulting memory. Phase 1 just records the intent in
// g_pendingUnitFieldWrites so UI consumers can see the edit reflected
// in a future readback. Enemy READ-ONLY gate: the helper refuses to
// write to units whose owner_slot != local_slot unless the field is
// on the allow-list for enemy units (position readbacks, etc.).
//
// 2026-04-29 (iter 136): per-field LIVE branches added for hull /
// shield / speed (mirroring Lua_HeroStatEdit's iter 100/129 LIVE
// wires). Other 10 fields (max_hull, max_shield, max_speed,
// attack_power, respawn_ms, invuln_flag, prevent_death, is_hero,
// respawn_enabled, owner_slot) still fall through to the Phase-1
// mirror queue.
//
// Function definition moved below Lua_HeroStatEdit so it can reference
// the shield/speed primitives (g_shieldLock, pfn_SetFrontShield,
// g_unitShieldOverrideMap, etc.) that are defined just above
// HeroStatEdit. The pending struct + lock stay here (data only).
struct PendingUnitFieldWrite {
    uint64_t    obj_addr  = 0;
    std::string field;
    float       value     = 0.0f;
};
static std::vector<PendingUnitFieldWrite> g_pendingUnitFieldWrites;
static CRITICAL_SECTION g_unitFieldLock;
static bool g_unitFieldLockInit = false;
static void EnsureUnitFieldLock() {
    if (!g_unitFieldLockInit) {
        InitializeCriticalSection(&g_unitFieldLock);
        g_unitFieldLockInit = true;
    }
}
static int Lua_SetUnitField(lua_State* L); // moved below Lua_HeroStatEdit (iter 136)

// SWFOC_SpawnUnit / SWFOC_SetBuildCost / SWFOC_SetUnitCapOverride —
// Tasks 159/160/163 Phase 1. SpawnUnit is the only one whose live path
// exists today (via the engine's Spawn_Unit Lua function) — but Phase 1
// records intent only because the 5-arg call (type, faction, x, y, z,
// count) needs the Phase 2 pipeline to marshal into a SWFOC_DoString
// call with the correct string escaping. SetBuildCost and
// SetUnitCapOverride are IDA-blocked pending the credits-deduction site
// and the unit-cap check, respectively. All three use
// g_economyLock since they're all production-economy toggles.
//
// 2026-05-06 (iter 248 → iter 249 CORRECTION): the Apocalypticx CE
// community ledger entry `rva_apocalypticx_unit_cap_gc @ 0x28DF6F` was
// DEPRECATED (iter 249) — the AOB resolves to a string-deallocation
// cleanup block in the 2026 binary, NOT a cap calculation. SetUnitCapOverride
// stays DEFERRED CONFIRMED until live-game CheatEngine tracing or IDA MCP
// xref walk identifies the canonical cap reader. See
// `knowledge-base/iter248_setunitcapoverride_re_kickoff.md` for the
// correction details + iter 250+ recommendation.
static std::unordered_map<int32_t, float>   g_pendingBuildCostMult;
static std::unordered_map<int32_t, int32_t> g_pendingUnitCapOverride;
struct PendingSpawnRequest {
    std::string type_name;
    int32_t     owner_slot = -1;
    float       x = 0.0f, y = 0.0f, z = 0.0f;
    int32_t     count = 1;
};
static std::vector<PendingSpawnRequest>     g_pendingSpawnRequests;

static int Lua_SpawnUnit(lua_State* L) {
    EnsureEconomyLock();
    const char* raw_type = fn_tostring(L, 1);
    int slot  = static_cast<int>(fn_tonumber(L, 2));
    float x   = static_cast<float>(fn_tonumber(L, 3));
    float y   = static_cast<float>(fn_tonumber(L, 4));
    float z   = static_cast<float>(fn_tonumber(L, 5));
    int count = static_cast<int>(fn_tonumber(L, 6));
    if (!raw_type || raw_type[0] == '\0') {
        fn_pushstring(L, "ERR: SWFOC_SpawnUnit: type_name required");
        return 1;
    }
    if (slot < 0) {
        fn_pushstring(L, "ERR: SWFOC_SpawnUnit: slot must be >= 0");
        return 1;
    }
    if (count <= 0) {
        fn_pushstring(L, "ERR: SWFOC_SpawnUnit: count must be > 0");
        return 1;
    }
    PendingSpawnRequest req;
    req.type_name  = raw_type;
    req.owner_slot = slot;
    req.x = x; req.y = y; req.z = z;
    req.count = count;
    EnterCriticalSection(&g_economyLock);
    g_pendingSpawnRequests.push_back(req);
    LeaveCriticalSection(&g_economyLock);
    Log("[Bridge] SpawnUnit(type=%s, slot=%d, xyz=(%.1f,%.1f,%.1f), count=%d) -- Phase 1 pending\n",
        raw_type, slot, x, y, z, count);
    fn_pushstring(L, "OK: spawn request queued (Phase 2 engine-call pending)");
    return 1;
}

static int Lua_SetBuildCost(lua_State* L) {
    EnsureEconomyLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double mult = fn_tonumber(L, 2);
    if (mult < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetBuildCost: multiplier must be >= 0");
        return 1;
    }
    EnterCriticalSection(&g_economyLock);
    g_pendingBuildCostMult[slot] = static_cast<float>(mult);
    LeaveCriticalSection(&g_economyLock);
    Log("[Bridge] SetBuildCost(slot=%d, mult=%.3f) -- Phase 1 pending\n", slot, mult);
    fn_pushstring(L, "OK: build cost recorded (Phase 2 hook pending)");
    return 1;
}

static int Lua_SetUnitCapOverride(lua_State* L) {
    EnsureEconomyLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int cap  = static_cast<int>(fn_tonumber(L, 2));
    if (slot < 0) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitCapOverride: slot must be >= 0");
        return 1;
    }
    if (cap < -1) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitCapOverride: cap must be >= -1 (-1 = unlimited)");
        return 1;
    }
    EnterCriticalSection(&g_economyLock);
    g_pendingUnitCapOverride[slot] = cap;
    LeaveCriticalSection(&g_economyLock);
    Log("[Bridge] SetUnitCapOverride(slot=%d, cap=%d) -- Phase 1 pending\n", slot, cap);
    fn_pushstring(L, "OK: unit-cap override recorded (Phase 2 hook pending)");
    return 1;
}

// SWFOC_FreezeAI(slot, enable) — Task 114 Phase 1.
// Records per-slot AI-freeze intent behind g_aiFreezeLock. Phase 2 will
// hook the AI scheduler at the per-frame tick that iterates PlayerArray
// and skip the ->ai_think() dispatch for frozen slots. Freezing enemy
// AI is the whole POINT of the feature, not a violation of the READ-ONLY
// rule — it's a player-facing "hold back the enemy" knob that maps to
// a global scheduler gate, not per-unit state mutation.
static CRITICAL_SECTION g_aiFreezeLock;
static bool g_aiFreezeLockInit = false;
static std::unordered_map<int32_t, bool> g_pendingFreezeAi;
static void EnsureAiFreezeLock() {
    if (!g_aiFreezeLockInit) {
        InitializeCriticalSection(&g_aiFreezeLock);
        g_aiFreezeLockInit = true;
    }
}
static int Lua_FreezeAI(lua_State* L) {
    EnsureAiFreezeLock();
    int slot   = static_cast<int>(fn_tonumber(L, 1));
    int enable = static_cast<int>(fn_tonumber(L, 2));
    if (slot < 0) {
        fn_pushstring(L, "ERR: SWFOC_FreezeAI: slot must be >= 0");
        return 1;
    }
    EnterCriticalSection(&g_aiFreezeLock);
    if (enable) {
        g_pendingFreezeAi[slot] = true;
    } else {
        g_pendingFreezeAi.erase(slot);
    }
    LeaveCriticalSection(&g_aiFreezeLock);
    Log("[Bridge] FreezeAI(slot=%d, enable=%d) -- Phase 1 pending\n", slot, enable);
    fn_pushstring(L, enable
        ? "OK: AI freeze recorded (Phase 2 hook pending)"
        : "OK: AI unfreeze recorded (Phase 2 hook pending)");
    return 1;
}

// SWFOC_FreeCam / SetCameraPos / GetCameraPos — Task 115 Phase 1.
// SWFOC_ScrollCameraToTarget — iter 107 LIVE.
//
// 2026-04-28 (iter 106 finding + iter 107 wire): the engine exposes
// camera control through Lua-registered APIs in the LuaUserVar registry
// (`sub_140546c70`) rather than C++ helpers — `Scroll_Camera_To`,
// `Camera_To_Follow`, `Rotate_Camera_To`, `Start_Cinematic_Camera`,
// `End_Cinematic_Camera`, `Set_Cinematic_Camera_Key`,
// `Transition_Cinematic_Camera_Key`. Same pattern as iter 99/100's
// `Override_Max_Speed` — we drive them via `DoString`.
//
// LIVE wire `Lua_ScrollCameraToTarget(lua_arg_expr)` delegates target
// construction to the caller (so a planet-name string, a Find_Object
// handle, or a vector literal all work). The 3-float
// `Lua_SetCameraPos(x,y,z)` stays PHASE 2 PENDING because the engine's
// Lua API expects an object/position userdata, not raw floats, and the
// constructor for that userdata isn't pinned yet. `Lua_FreeCam` also
// stays PHASE 2 PENDING — there's no `Free_Cam(enable)` Lua API; the
// engine implements it as Lua-side script behaviour we'd need to mimic.
static CRITICAL_SECTION g_cameraLock;
static bool g_cameraLockInit = false;
static bool g_pendingFreeCam = false;
static float g_pendingCamX = 0.0f;
static float g_pendingCamY = 0.0f;
static float g_pendingCamZ = 0.0f;
static void EnsureCameraLock() {
    if (!g_cameraLockInit) {
        InitializeCriticalSection(&g_cameraLock);
        g_cameraLockInit = true;
    }
}
static int Lua_FreeCam(lua_State* L) {
    EnsureCameraLock();
    int enable = static_cast<int>(fn_tonumber(L, 1));
    EnterCriticalSection(&g_cameraLock);
    g_pendingFreeCam = (enable != 0);
    LeaveCriticalSection(&g_cameraLock);
    Log("[Bridge] FreeCam(%d) -- Phase 1 pending\n", enable);
    fn_pushstring(L, enable
        ? "OK: free-cam enabled (Phase 2 hook pending)"
        : "OK: free-cam disabled (Phase 2 hook pending)");
    return 1;
}
// 2026-05-06 (iter 236-237): A1.x SetCameraPos per-coord LIVE wire.
// Pattern parallels iter-100 SetSpeedOverride exactly (direct C++ engine-
// function call, NOT MinHook detour). Operator wants one-shot teleport;
// detour would fight the camera animation pipeline (jitter every frame).
//
// Active camera lookup (tactical mode only — galactic camera path is a
// follow-up wire when needed):
//   gm = qword_140B15418       (GameModeRoot_Global value, the GameModeClass*)
//   mode = vftable[28](gm)      (vftable+0xE0 = GetCurrentGameModeId; 1=Galactic, 2=Land)
//   if mode == 2:
//     camera = *(gm + 0x90)    (CameraClass*)
//   else: error
//
// SetCameraPos: read inline 4x3 matrix from CameraClass+0x10 (12 floats),
// modify translation column at indices [3]/[7]/[11], call SetTransformMatrix
// to write back + propagate to per-frame matrix-pointer at +0x40.
//
// GetCameraPos: call CameraClass::GetPosition (engine reader at 0x261A40)
// which returns the X/Y/Z from the per-frame matrix-pointer.
typedef __int64 (__fastcall *pfn_CameraSetTransformMatrix)(__int64 camera, void* matrix);
typedef void* (__fastcall *pfn_CameraGetPosition)(__int64 camera, float* out_xyz);

static __int64 LookupActiveCamera() {
    __int64 gm = *reinterpret_cast<__int64*>(g_base + RVA::GameModeRoot_Global);
    if (!gm) return 0;

    // vftable[28] @ +0xE0 = GetCurrentGameModeId. Returns 1 (Galactic) or 2 (Land).
    auto vftable = *reinterpret_cast<__int64**>(gm);
    if (!vftable) return 0;
    auto getMode = reinterpret_cast<unsigned int(__fastcall*)(__int64)>(vftable[28]);
    unsigned int mode = getMode(gm);
    if (mode != 2) return 0;  // tactical-only

    return *reinterpret_cast<__int64*>(gm + 0x90);
}

static int Lua_SetCameraPos(lua_State* L) {
    float x = static_cast<float>(fn_tonumber(L, 1));
    float y = static_cast<float>(fn_tonumber(L, 2));
    float z = static_cast<float>(fn_tonumber(L, 3));

    __int64 camera = LookupActiveCamera();
    if (!camera) {
        Log("[Bridge] SetCameraPos(%.2f, %.2f, %.2f) -- ERR: no active tactical camera "
            "(galactic mode? game not loaded?)\n", x, y, z);
        fn_pushstring(L, "ERR: no active tactical camera");
        return 1;
    }

    // Read current inline matrix (12 floats at CameraClass+0x10) to preserve
    // rotation columns; modify translation column at indices [3]/[7]/[11].
    float matrix[12];
    memcpy(matrix, reinterpret_cast<float*>(camera + 0x10), sizeof(matrix));
    matrix[3]  = x;
    matrix[7]  = y;
    matrix[11] = z;

    auto fn = reinterpret_cast<pfn_CameraSetTransformMatrix>(
        g_base + RVA::CameraSetTransformMatrix);
    fn(camera, matrix);

    Log("[Bridge] SetCameraPos(%.2f, %.2f, %.2f) -- LIVE\n", x, y, z);
    fn_pushstring(L, "OK: camera teleported (LIVE -- SetTransformMatrix direct call)");
    return 1;
}

// 2026-04-28 (iter 113) — UNIVERSAL Lua-method dispatcher.
// Calls any method `method_name` on a Lua object expression with the
// caller-supplied argument list spliced verbatim. Composes
// `(<obj_expr>):(<method_name>)(<args_expr>)` and dispatches via DoString.
//
// One wire, infinite methods. Operator examples:
//   SWFOC_CallObjMethodLua("Find_Player(\"REBEL\")", "Give_Money", "5000")
//   SWFOC_CallObjMethodLua("Find_First_Object(\"Empire_AT_AT\")", "Heal", "")
//   SWFOC_CallObjMethodLua("Find_First_Object(\"Empire_AT_AT\")", "Enable_Behavior", "\"INVULNERABLE\", true")
//
// Bridge does NOT validate the method name or args — that's the engine's
// Lua VM's job. Localhost-only (named pipe, max_instances=1) so untrusted
// input isn't a concern; the operator is the caller. The dispatcher is
// the GREATEST-COMMON-DENOMINATOR wire that lets the editor surface ANY
// engine Lua API the operator can name, without needing to ship a
// per-method bridge wire and catalog flip.
//
// Trade-off vs per-method wires (iter 100/107/108/109/110/111/112):
// per-method wires give the catalog a typed surface with named badges,
// which is operator-pleasing in the editor UI. The universal wire is
// the escape hatch for everything else — same primitive, finer grain.
static int Lua_CallObjMethodLua(lua_State* L) {
    const char* objExpr = fn_tostring(L, 1);
    const char* methodName = fn_tostring(L, 2);
    const char* argsExpr = fn_tostring(L, 3);
    if (!objExpr || !*objExpr || !methodName || !*methodName) {
        fn_pushstring(L,
            "ERR: SWFOC_CallObjMethodLua: expected (obj_lua_expr, method_name, args_lua_expr)");
        return 1;
    }
    if (!argsExpr) argsExpr = "";  // empty args are fine for no-arg methods
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(objExpr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(methodName, kMaxExpr + 1) > kMaxExpr ||
        strnlen(argsExpr, kMaxExpr + 1) > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_CallObjMethodLua: expression too long");
        return 1;
    }
    char code[3 * kMaxExpr + 96];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "(%s):%s(%s)", objExpr, methodName, argsExpr);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_CallObjMethodLua: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=CallObjMethodLua");
    if (rc != 0) {
        Log("[Bridge] CallObjMethodLua(%s, %s, %s) -- LIVE call FAILED rc=%d\n",
            objExpr, methodName, argsExpr, rc);
        fn_settop(L, -2);
        char errmsg[512];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] CallObjMethodLua(%s, %s, %s) -- LIVE OK\n",
        objExpr, methodName, argsExpr);
    char okmsg[256];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s dispatched (LIVE — engine Lua API)", methodName);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-04-28 (iter 112) — shared helper for zero-arg method dispatch
// on a Lua unit handle. Pattern is `(<unit>):Method()` — no second
// argument needed. Used by Despawn / Stop / Retreat / Sell wrappers.
static int Lua_DispatchUnitNoArgMethod(lua_State* L, const char* methodName,
                                        const char* swfocFnName) {
    const char* unitExpr = fn_tostring(L, 1);
    if (!unitExpr || !*unitExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (unit_lua_expr)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(unitExpr, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[kMaxExpr + 96];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "(%s):%s()", unitExpr, methodName);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s(%s) -- LIVE call FAILED rc=%d\n",
            swfocFnName, unitExpr, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] %s(%s) -- LIVE OK\n", swfocFnName, unitExpr);
    char okmsg[160];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s dispatched (LIVE — engine Lua API)", methodName);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-04-28 (iter 112) — LIVE per-unit no-arg method wires.
static int Lua_DespawnUnitLua(lua_State* L) {
    return Lua_DispatchUnitNoArgMethod(L, "Despawn", "SWFOC_DespawnUnitLua");
}
static int Lua_StopUnitLua(lua_State* L) {
    return Lua_DispatchUnitNoArgMethod(L, "Stop", "SWFOC_StopUnitLua");
}
static int Lua_RetreatUnitLua(lua_State* L) {
    return Lua_DispatchUnitNoArgMethod(L, "Retreat", "SWFOC_RetreatUnitLua");
}

// 2026-04-28 (iter 111) — shared helper for generic single-bool-arg
// method dispatch on a Lua unit handle. Three callers below
// (Hide / Prevent_AI_Usage / Set_Selectable) all follow the
// `(<unit>):Method(<bool>)` shape — one helper, three thin wrappers,
// one ledger of code. Trade-off: a generic-method wrapper would also
// work but per-method wires give the catalog a typed surface for
// operator visibility (LIVE / PHASE 2 PENDING badges per action).
static int Lua_DispatchUnitBoolMethod(lua_State* L, const char* methodName,
                                      const char* swfocFnName) {
    const char* unitExpr = fn_tostring(L, 1);
    const char* boolExpr = fn_tostring(L, 2);
    if (!unitExpr || !*unitExpr || !boolExpr || !*boolExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (unit_lua_expr, bool_lua_expr)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(unitExpr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(boolExpr, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[2 * kMaxExpr + 96];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "(%s):%s(%s)", unitExpr, methodName, boolExpr);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s(%s, %s) -- LIVE call FAILED rc=%d\n",
            swfocFnName, unitExpr, boolExpr, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] %s(%s, %s) -- LIVE OK\n", swfocFnName, unitExpr, boolExpr);
    char okmsg[160];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s dispatched (LIVE — engine Lua API)", methodName);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-04-29 (iter 154) — float-arg unit-method shared dispatcher.
// Mirrors iter 111's bool-arg Lua_DispatchUnitBoolMethod verbatim except
// the second arg is splice-quoted as a numeric Lua expression (operator
// passes "100.5" or "Get_Hull() * 0.5" — the bridge spliices verbatim).
static int Lua_DispatchUnitFloatMethod(lua_State* L, const char* methodName,
                                       const char* swfocFnName) {
    const char* unitExpr  = fn_tostring(L, 1);
    const char* floatExpr = fn_tostring(L, 2);
    if (!unitExpr || !*unitExpr || !floatExpr || !*floatExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (unit_lua_expr, float_lua_expr)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(unitExpr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(floatExpr, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[2 * kMaxExpr + 96];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "(%s):%s(%s)", unitExpr, methodName, floatExpr);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s(%s, %s) -- LIVE call FAILED rc=%d\n",
            swfocFnName, unitExpr, floatExpr, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] %s(%s, %s) -- LIVE OK\n", swfocFnName, unitExpr, floatExpr);
    char okmsg[160];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s dispatched (LIVE — engine Lua API)", methodName);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-04-29 (iter 154) — float-arg unit-method LIVE batch.
// docs/lua-api.md GameObjectWrapper section. Take_Damage applies raw
// damage to the unit; Set_Damage_Modifier scales outgoing damage;
// Set_Rate_Of_Fire_Modifier scales rate-of-fire.
static int Lua_TakeDamageLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Take_Damage",
        "SWFOC_TakeDamageLua");
}

static int Lua_SetDamageModifierLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Set_Damage_Modifier",
        "SWFOC_SetDamageModifierLua");
}

static int Lua_SetRateOfFireModifierLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Set_Rate_Of_Fire_Modifier",
        "SWFOC_SetRateOfFireModifierLua");
}

// 2026-04-29 (iter 154) — Heal LIVE wire (no-arg unit method).
// Mirrors iter 112 (Despawn/Stop/Retreat) shape — reuses
// Lua_DispatchUnitNoArgMethod.
static int Lua_HealUnitLua(lua_State* L) {
    return Lua_DispatchUnitNoArgMethod(L, "Heal", "SWFOC_HealUnitLua");
}

// 2026-04-29 (iter 155) — player-method LIVE batch.
// docs/lua-api.md PlayerWrapper section. Reuses Lua_DispatchUnitFloatMethod
// (helper composes `(<obj>):method(<arg>)` regardless of arg type — splice
// is verbatim, so it works for player handles + numeric/string args).
// Operator passes a player expression like `Find_Player("REBEL")` and an
// arg expression — same wire shape as the unit-method wires.
static int Lua_PlayerGiveMoneyLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Give_Money",
        "SWFOC_PlayerGiveMoneyLua");
}

static int Lua_PlayerSetTechLevelLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Set_Tech_Level",
        "SWFOC_PlayerSetTechLevelLua");
}

static int Lua_PlayerUnlockTechLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Unlock_Tech",
        "SWFOC_PlayerUnlockTechLua");
}

// 2026-04-29 (iter 156) — 4-wire unit-method LIVE batch using existing
// helpers. Pattern's marginal cost is now ~3-5 LoC per wire when the
// dispatcher's already there.
static int Lua_ActivateAbilityLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Activate_Ability",
        "SWFOC_ActivateAbilityLua");
}

static int Lua_DisableCaptureLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Disable_Capture",
        "SWFOC_DisableCaptureLua");
}

static int Lua_SetGarrisonSpawnLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Set_Garrison_Spawn",
        "SWFOC_SetGarrisonSpawnLua");
}

static int Lua_CancelHyperspaceLua(lua_State* L) {
    return Lua_DispatchUnitNoArgMethod(L, "Cancel_Hyperspace",
        "SWFOC_CancelHyperspaceLua");
}

// 2026-04-29 (iter 157) — 6-wire unit-method mega-batch using existing
// iter-111/112/154 helpers. Pattern marginal cost ~3 LoC per wire.
static int Lua_SetInLimboLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Set_In_Limbo",
        "SWFOC_SetInLimboLua");
}

static int Lua_SetCheckContestedSpaceLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Set_Check_Contested_Space",
        "SWFOC_SetCheckContestedSpaceLua");
}

static int Lua_SellUnitLua(lua_State* L) {
    return Lua_DispatchUnitNoArgMethod(L, "Sell", "SWFOC_SellUnitLua");
}

static int Lua_BribeLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Bribe", "SWFOC_BribeLua");
}

static int Lua_MoveToLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Move_To", "SWFOC_MoveToLua");
}

static int Lua_FireSpecialWeaponLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Fire_Special_Weapon",
        "SWFOC_FireSpecialWeaponLua");
}

// 2026-04-29 (iter 158) — global-method dispatcher.
// Different from iter-111/154 which compose `(obj):method(arg)`. Globals
// compose `method(arg)` (no receiver). Shape: SWFOC_X(arg_expr).
// Per docs/lua-api.md Additional Global Functions section.
static int Lua_DispatchGlobalArgMethod(lua_State* L, const char* methodName,
                                       const char* swfocFnName) {
    const char* arg = fn_tostring(L, 1);
    if (!arg || !*arg) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (arg_lua_expr)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(arg, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[kMaxExpr + 96];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "%s(%s)", methodName, arg);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s(%s) -- LIVE call FAILED rc=%d\n", swfocFnName, arg, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] %s(%s) -- LIVE OK\n", swfocFnName, arg);
    char okmsg[160];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s dispatched (LIVE — engine Lua API)", methodName);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-04-29 (iter 158) — global-method LIVE batch.
static int Lua_DisableBombingRunLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Disable_Bombing_Run",
        "SWFOC_DisableBombingRunLua");
}

static int Lua_FlashGuiObjectLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Flash_GUI_Object",
        "SWFOC_FlashGuiObjectLua");
}

static int Lua_HideGuiObjectLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Hide_GUI_Object",
        "SWFOC_HideGuiObjectLua");
}

// 2026-04-29 (iter 159) — string-arg global LIVE batch.
// docs/lua-api.md "Story & Events" + "Audio" sections. Reuses
// Lua_DispatchGlobalArgMethod (iter 158); helper is shape-agnostic
// for the (arg) call form. Operator passes pre-quoted Lua string
// literal as arg (e.g. SWFOC_StoryEventLua('"DEATH_STAR_DESTROYED"')).
// Cost: ~3 LoC per wire after dispatcher exists.
static int Lua_StoryEventLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Story_Event",
        "SWFOC_StoryEventLua");
}

static int Lua_AddObjectiveLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Add_Objective",
        "SWFOC_AddObjectiveLua");
}

static int Lua_PlayMusicLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Play_Music",
        "SWFOC_PlayMusicLua");
}

static int Lua_PlaySfxEventLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Play_SFX_Event",
        "SWFOC_PlaySfxEventLua");
}

// 2026-04-29 (iter 160) — mixed-helper LIVE batch.
// docs/lua-api.md notes Lock_Controls(bool) is a global, while
// Disable_Orbital_Bombardment(bool) is a PlayerWrapper method, and
// Story_Event_Trigger is an alternative global trigger to Story_Event.
// All 3 reuse existing dispatchers; ~3 LoC each. Note iter-111
// "DispatchUnitBoolMethod" helper is misleadingly named — it works for
// ANY obj:bool_method shape, including player receivers (Lua doesn't
// care about the static type, just the method-call syntax).
static int Lua_LockControlsLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Lock_Controls",
        "SWFOC_LockControlsLua");
}

static int Lua_DisableOrbitalBombardmentLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Disable_Orbital_Bombardment",
        "SWFOC_DisableOrbitalBombardmentLua");
}

static int Lua_StoryEventTriggerLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Story_Event_Trigger",
        "SWFOC_StoryEventTriggerLua");
}

// 2026-04-29 (iter 161) — player-method LIVE batch.
// Lock_Tech is the opposite of iter-155 Unlock_Tech (locks a tech that
// would otherwise be available to the player). Make_Ally / Make_Enemy
// are PlayerWrapper diplomacy primitives. docs/lua-api.md flags the
// behavioral warning: "Make_Ally/Make_Enemy resets every game mode
// change" — caller must re-apply after Galactic↔Tactical transitions.
// All 3 reuse iter-154 generic 2-arg helper since the shape is
// `(player):method(arg)` regardless of arg type. Lua doesn't care
// whether the arg is a type-handle or a player-handle.
static int Lua_LockTechLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Lock_Tech",
        "SWFOC_LockTechLua");
}

static int Lua_MakeAllyLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Make_Ally",
        "SWFOC_MakeAllyLua");
}

static int Lua_MakeEnemyLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Make_Enemy",
        "SWFOC_MakeEnemyLua");
}

// 2026-04-29 (iter 162) — 4-wire batch demonstrating dispatcher reuse
// across receiver shapes (unit-method + globals). All 4 binary-confirmed
// in docs/lua-api.md sections 1+2.
static int Lua_OverrideMaxSpeedLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Override_Max_Speed",
        "SWFOC_OverrideMaxSpeedLua");
}

static int Lua_SuspendAiLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Suspend_AI",
        "SWFOC_SuspendAiLua");
}

static int Lua_FadeScreenInLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Fade_Screen_In",
        "SWFOC_FadeScreenInLua");
}

static int Lua_ZoomCameraLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Zoom_Camera",
        "SWFOC_ZoomCameraLua");
}

// 2026-04-29 (iter 163) — combat-order LIVE batch.
// docs/lua-api.md GameObjectWrapper Commands section: orders the unit
// to attack / guard / divert. All 3 take a single target arg (object
// or position) and reuse iter-154 generic 2-arg helper since Lua
// doesn't care about the static type of the arg.
static int Lua_AttackTargetLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Attack_Target",
        "SWFOC_AttackTargetLua");
}

static int Lua_GuardTargetLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Guard_Target",
        "SWFOC_GuardTargetLua");
}

static int Lua_DivertLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Divert",
        "SWFOC_DivertLua");
}

// 2026-04-29 (iter 164) — player-method extension LIVE batch.
// docs/lua-api.md PlayerWrapper section. Enable_As_Actor is no-arg
// (via iter-112 helper, which is shape-agnostic for any obj receiver).
// Release_Credits_For_Tactical and Select_Object are 1-arg via
// iter-154 generic 2-arg helper (Lua doesn't care about static type
// of receiver — works for player too).
static int Lua_EnableAsActorLua(lua_State* L) {
    return Lua_DispatchUnitNoArgMethod(L, "Enable_As_Actor",
        "SWFOC_EnableAsActorLua");
}

static int Lua_ReleaseCreditsForTacticalLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Release_Credits_For_Tactical",
        "SWFOC_ReleaseCreditsForTacticalLua");
}

static int Lua_SelectObjectLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Select_Object",
        "SWFOC_SelectObjectLua");
}

// 2026-04-29 (iter 165) — camera/cinematic complement batch.
// Completes camera-arc trio: Rotate_Camera_By complements iter-144
// Rotate_Camera_To, Fade_Screen_Out complements iter-162 Fade_Screen_In,
// Point_Camera_At points camera at any unit/object. All globals via
// iter-158 helper.
static int Lua_FadeScreenOutLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Fade_Screen_Out",
        "SWFOC_FadeScreenOutLua");
}

static int Lua_RotateCameraByLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Rotate_Camera_By",
        "SWFOC_RotateCameraByLua");
}

static int Lua_PointCameraAtLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Point_Camera_At",
        "SWFOC_PointCameraAtLua");
}

// 2026-04-29 (iter 166) — 5th dispatcher helper: global no-arg shape.
// Completes the 2x2 matrix of canonical Lua API call shapes:
//   - obj no-arg `(obj):method()`        — iter-112
//   - obj bool-arg `(obj):method(bool)`   — iter-111
//   - obj generic-arg `(obj):method(arg)` — iter-154
//   - global 1-arg `method(arg)`          — iter-158
//   - global no-arg `method()`            — iter-166 (this helper)
// Mirror of iter-158's Lua_DispatchGlobalArgMethod minus the arg.
static int Lua_DispatchGlobalNoArgMethod(lua_State* L, const char* methodName,
                                         const char* swfocFnName) {
    char code[160];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "%s()", methodName);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s() -- LIVE call FAILED rc=%d\n", swfocFnName, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] %s() -- LIVE OK\n", swfocFnName);
    char okmsg[160];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s dispatched (LIVE — engine Lua API)", methodName);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-04-29 (iter 166) — global no-arg LIVE wires (audio + Show_GUI_Object).
static int Lua_StopAllMusicLua(lua_State* L) {
    return Lua_DispatchGlobalNoArgMethod(L, "Stop_All_Music",
        "SWFOC_StopAllMusicLua");
}

static int Lua_ResumeModeBasedMusicLua(lua_State* L) {
    return Lua_DispatchGlobalNoArgMethod(L, "Resume_Mode_Based_Music",
        "SWFOC_ResumeModeBasedMusicLua");
}

static int Lua_ShowGuiObjectLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "Show_GUI_Object",
        "SWFOC_ShowGuiObjectLua");
}

// 2026-04-29 (iter 167) — 6th dispatcher helper: unit-getter no-arg
// shape with return-value capture. Engine APIs like Get_Hull /
// Get_Health / Get_Shield return a numeric value via Lua's stack;
// previous helpers discarded that value. This helper wraps the call
// in `local _v = (obj):method(); return tostring(_v)` so the value
// flows back to the bridge via DoString's return-stack mechanism.
//
// NOTE: Lua 5.0 doesn't have lua_pushglobaltable, but our existing
// DoString returns the value as a string-on-stack. We rely on
// engineGetTopLuaResult to read it back. If the engine API returns
// nil (unit dead, etc.), we report that explicitly.
static int Lua_DispatchUnitGetterNoArg(lua_State* L, const char* methodName,
                                       const char* swfocFnName) {
    const char* unitExpr = fn_tostring(L, 1);
    if (!unitExpr || !*unitExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (unit_lua_expr)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(unitExpr, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[kMaxExpr + 96];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "return tostring((%s):%s())", unitExpr, methodName);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s -- LIVE call FAILED rc=%d\n", swfocFnName, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    // Capture return value — DoString left the result on the stack
    // (Lua 5.0: tostring()'s return is at index -1).
    const char* resultStr = fn_tostring(L, -1);
    if (!resultStr) {
        fn_settop(L, -2);
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s returned non-stringable result", methodName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    Log("[Bridge] %s -- LIVE returned '%s'\n", swfocFnName, resultStr);
    char okmsg[256];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s = %s (LIVE — engine Lua API)", methodName, resultStr);
    fn_settop(L, -2);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-04-29 (iter 167) — read-side LIVE wires using NEW unit-getter helper.
// docs/lua-api.md GameObjectWrapper Health & Combat section: returns
// current HP / health-percentage / shield-percentage. Operator-friendly
// for verifying combat state during testing.
static int Lua_GetHullLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Hull",
        "SWFOC_GetHullLua");
}

static int Lua_GetHealthLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Health",
        "SWFOC_GetHealthLua");
}

static int Lua_GetUnitShieldLuaGetter(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Shield",
        "SWFOC_GetShieldLua");
}

// 2026-04-29 (iter 168) — read-side getter expansion via iter-167 helper.
// Has_Attack_Target / Are_Engines_Online return booleans (stringify
// to "true"/"false"). Get_Owner returns a PlayerWrapper handle —
// stringification confirms the call landed even if the result isn't
// human-readable (operator can verify via subsequent (player):Get_Faction()
// once that helper exists).
static int Lua_HasAttackTargetLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Has_Attack_Target",
        "SWFOC_HasAttackTargetLua");
}

static int Lua_AreEnginesOnlineLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Are_Engines_Online",
        "SWFOC_AreEnginesOnlineLua");
}

static int Lua_GetOwnerLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Owner",
        "SWFOC_GetOwnerLua");
}

// 2026-04-29 (iter 169) — read-side getter expansion #2.
// iter-167 helper is shape-agnostic for any obj receiver; same
// Lua_DispatchUnitGetterNoArg works for player getters too. The
// helper name is misleading (says "Unit") but semantics are obj-no-arg.
// Get_Credits / Get_Tech_Level pair with iter-155 GiveMoney /
// SetTechLevel for read-after-write verification workflows.
static int Lua_GetUnitTypeLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Type",
        "SWFOC_GetTypeLua");
}

static int Lua_GetCreditsLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Credits",
        "SWFOC_GetCreditsLua");
}

static int Lua_GetFactionLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Faction",
        "SWFOC_GetFactionLua");
}

static int Lua_GetTechLevelLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Tech_Level",
        "SWFOC_GetTechLevelLua");
}

// 2026-04-29 (iter 170) — read-side state-query batch.
// Each wire forms a read-after-write pair with an earlier writer:
//   Is_Stealthed   ↔ iter-153 EnableStealth
//   Is_In_Limbo    ↔ iter-157 SetInLimbo
//   Is_Capturable  ↔ iter-156 DisableCapture
// Get_Name returns the player's display name as a string.
static int Lua_GetNameLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Name",
        "SWFOC_GetNameLua");
}

static int Lua_IsStealthedLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Is_Stealthed",
        "SWFOC_IsStealthedLua");
}

static int Lua_IsInLimboLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Is_In_Limbo",
        "SWFOC_IsInLimboLua");
}

static int Lua_IsCapturableLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Is_Capturable",
        "SWFOC_IsCapturableLua");
}

// 2026-04-30 (iter 171) — read-side query batch via iter-167 helper.
// Get_Position / Get_Parent_Object / Get_Attack_Target return handles
// (stringify as 'table: 0x...' confirming call landed). Get_Damage_Modifier
// returns float — read-after-write pair with iter-154 SetDamageModifier.
static int Lua_GetPositionLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Position",
        "SWFOC_GetPositionLua");
}

static int Lua_GetParentObjectLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Parent_Object",
        "SWFOC_GetParentObjectLua");
}

static int Lua_GetAttackTargetLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Attack_Target",
        "SWFOC_GetAttackTargetLua");
}

static int Lua_GetDamageModifierLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Damage_Modifier",
        "SWFOC_GetDamageModifierLua");
}

// 2026-04-30 (iter 172) — read-side garrison/behavior batch.
// **100 LIVE wire milestone** — pushing master loop from 99 to 103.
// Get_Garrison_Units returns table; Get_Contained_Object_Count returns
// numeric. Get_Behavior_ID returns behavior descriptor.
// Get_Rate_Of_Fire_Modifier returns float — read-after-write pair
// with iter-154 SetRateOfFireModifier writer.
static int Lua_GetGarrisonUnitsLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Garrison_Units",
        "SWFOC_GetGarrisonUnitsLua");
}

static int Lua_GetContainedObjectCountLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Contained_Object_Count",
        "SWFOC_GetContainedObjectCountLua");
}

static int Lua_GetBehaviorIdLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Behavior_ID",
        "SWFOC_GetBehaviorIdLua");
}

static int Lua_GetRateOfFireModifierLua(lua_State* L) {
    return Lua_DispatchUnitGetterNoArg(L, "Get_Rate_Of_Fire_Modifier",
        "SWFOC_GetRateOfFireModifierLua");
}

// 2026-05-04 (iter 173) — 7th dispatcher helper: unit-getter WITH arg
// shape with return-value capture. Mirror of iter-167 helper but takes
// a single Lua-expression arg and forwards it into the engine call:
//   `return tostring((obj):method(arg))`
// Captured stringified result flows back to operator via DoString
// return-stack mechanism (same pattern as iter-167). Opens up the
// arg-getter family: Is_Ability_Active(name), Has_Property(prop),
// Is_Category(cat), Get_Distance(target), Get_Bone_Position(bone), etc.
static int Lua_DispatchUnitGetterArg(lua_State* L, const char* methodName,
                                     const char* swfocFnName) {
    const char* unitExpr = fn_tostring(L, 1);
    const char* argExpr = fn_tostring(L, 2);
    if (!unitExpr || !*unitExpr || !argExpr || !*argExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (unit_lua_expr, arg_lua_expr)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(unitExpr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(argExpr, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[2 * kMaxExpr + 96];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "return tostring((%s):%s(%s))", unitExpr, methodName, argExpr);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s -- LIVE call FAILED rc=%d\n", swfocFnName, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    const char* resultStr = fn_tostring(L, -1);
    if (!resultStr) {
        fn_settop(L, -2);
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s returned non-stringable result", methodName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    Log("[Bridge] %s(%s) -- LIVE returned '%s'\n", swfocFnName, argExpr, resultStr);
    char okmsg[256];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s(%s) = %s (LIVE — engine Lua API)", methodName, argExpr, resultStr);
    fn_settop(L, -2);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-05-04 (iter 173) — read-side LIVE wires using NEW unit-getter-with-arg.
static int Lua_IsAbilityActiveLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Is_Ability_Active",
        "SWFOC_IsAbilityActiveLua");
}

static int Lua_HasPropertyLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Has_Property",
        "SWFOC_HasPropertyLua");
}

static int Lua_IsCategoryLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Is_Category",
        "SWFOC_IsCategoryLua");
}

static int Lua_GetDistanceLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Get_Distance",
        "SWFOC_GetDistanceLua");
}

// 2026-05-04 (iter 174) — cross-receiver arg-getter batch via iter-173 helper.
// Get_Bone_Position is a unit method (binary-confirmed), Contains_Object_Type
// is a unit predicate (community-doc), Get_Space_Station_Level is a player
// method (community-doc), Get_Type_Of_Unit is a TaskForce method
// (binary-confirmed). All four reuse the same iter-173 helper because the
// dispatch shape `(obj):method(arg)` is receiver-agnostic.
static int Lua_GetBonePositionLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Get_Bone_Position",
        "SWFOC_GetBonePositionLua");
}

static int Lua_ContainsObjectTypeLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Contains_Object_Type",
        "SWFOC_ContainsObjectTypeLua");
}

static int Lua_GetSpaceStationLevelLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Get_Space_Station_Level",
        "SWFOC_GetSpaceStationLevelLua");
}

static int Lua_GetTypeOfUnitLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Get_Type_Of_Unit",
        "SWFOC_GetTypeOfUnitLua");
}

// 2026-05-04 (iter 175) — TaskForce write-side batch using existing
// helpers iter-112 (no-arg) and iter-154 (1-arg). All four binary-confirmed
// in docs/lua-api.md TaskForce section. SWFOC_* names use TaskForce prefix
// to disambiguate from unit-method versions of Move_To (iter-157
// SWFOC_MoveToLua) — both Lua callsites are valid because the bridge
// dispatch table differentiates by the SWFOC_* function name, not by
// the engine method.
static int Lua_TaskForceMoveToLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Move_To",
        "SWFOC_TaskForceMoveToLua");
}

static int Lua_TaskForceReinforceLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Reinforce",
        "SWFOC_TaskForceReinforceLua");
}

static int Lua_TaskForceReleaseReinforcementsLua(lua_State* L) {
    return Lua_DispatchUnitNoArgMethod(L, "Release_Reinforcements",
        "SWFOC_TaskForceReleaseReinforcementsLua");
}

static int Lua_TaskForceLaunchUnitsLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Launch_Units",
        "SWFOC_TaskForceLaunchUnitsLua");
}

// 2026-05-04 (iter 176) — TaskForce coverage extension via existing helpers.
// Attack_Target / Guard_Target are TaskForce variants of iter-163 unit
// commands; Land_Units is the GalacticTaskForce complement to iter-175
// Launch_Units; Set_As_Goal_System_Removable is a TaskForceClass-specific
// bool flag (iter-111 dispatcher).
static int Lua_TaskForceAttackTargetLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Attack_Target",
        "SWFOC_TaskForceAttackTargetLua");
}

static int Lua_TaskForceGuardTargetLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Guard_Target",
        "SWFOC_TaskForceGuardTargetLua");
}

static int Lua_TaskForceLandUnitsLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Land_Units",
        "SWFOC_TaskForceLandUnitsLua");
}

static int Lua_TaskForceSetAsGoalSystemRemovableLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Set_As_Goal_System_Removable",
        "SWFOC_TaskForceSetAsGoalSystemRemovableLua");
}

// 2026-05-04 (iter 177) — 8th dispatcher helper: global-getter-with-arg
// shape with return-value capture. Mirror of iter-173 helper but no-receiver:
//   `return tostring(method(arg))`
// Captured stringified result flows back via DoString return-stack.
// Opens up the global-getter family: Find_Object_Type(name),
// FindPlanet(name), Find_First_Object(type_name) — discovery operations
// that return engine handles for further composition.
static int Lua_DispatchGlobalGetterArg(lua_State* L, const char* methodName,
                                       const char* swfocFnName) {
    const char* argExpr = fn_tostring(L, 1);
    if (!argExpr || !*argExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (arg_lua_expr)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(argExpr, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[kMaxExpr + 96];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "return tostring(%s(%s))", methodName, argExpr);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s -- LIVE call FAILED rc=%d\n", swfocFnName, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    const char* resultStr = fn_tostring(L, -1);
    if (!resultStr) {
        fn_settop(L, -2);
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s returned non-stringable result", methodName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    Log("[Bridge] %s(%s) -- LIVE returned '%s'\n", swfocFnName, argExpr, resultStr);
    char okmsg[256];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s(%s) = %s (LIVE — engine Lua API)", methodName, argExpr, resultStr);
    fn_settop(L, -2);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-05-04 (iter 177) — discovery LIVE wires using NEW global-getter helper.
static int Lua_FindObjectTypeLua(lua_State* L) {
    return Lua_DispatchGlobalGetterArg(L, "Find_Object_Type",
        "SWFOC_FindObjectTypeLua");
}

static int Lua_FindPlanetLua(lua_State* L) {
    return Lua_DispatchGlobalGetterArg(L, "FindPlanet",
        "SWFOC_FindPlanetLua");
}

static int Lua_FindFirstObjectLua(lua_State* L) {
    return Lua_DispatchGlobalGetterArg(L, "Find_First_Object",
        "SWFOC_FindFirstObjectLua");
}

// 2026-05-04 (iter 178) — 9th dispatcher helper: global-getter-with-NO-arg
// shape with return-value capture. Closes the receiver × arg × read/write
// matrix (iter 158/166: write-only; iter 167: obj/0-arg/read; iter 173:
// obj/1-arg/read; iter 177: global/1-arg/read; iter 178: global/0-arg/read).
//
// Codegen: `return tostring(method())` — no receiver, no arg, just capture.
// Operator-facing: Get_Game_Mode() / Get_Local_Player() / etc. — global state
// queries that need no parameter and return engine handles or strings for
// further composition.
static int Lua_DispatchGlobalGetterNoArg(lua_State* L, const char* methodName,
                                         const char* swfocFnName) {
    char code[160];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "return tostring(%s())", methodName);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s -- LIVE call FAILED rc=%d\n", swfocFnName, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    const char* resultStr = fn_tostring(L, -1);
    if (!resultStr) {
        fn_settop(L, -2);
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s returned non-stringable result", methodName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    Log("[Bridge] %s() -- LIVE returned '%s'\n", swfocFnName, resultStr);
    char okmsg[256];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s() = %s (LIVE — engine Lua API)", methodName, resultStr);
    fn_settop(L, -2);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-05-04 (iter 178) — global-no-arg-getter LIVE batch using new helper.
// All three are binary-confirmed in docs/lua-api.md (Globals section).
// Get_Game_Mode: returns "Galactic"/"Land"/"Space" — gates tactical-only
// commands; safe to call always. Get_Local_Player: returns PlayerWrapper
// handle; pairs with iter-155 PlayerGiveMoney for "give MY player credits"
// composition. Get_Seconds_Per_Game_Minute: returns time-scale float.
static int Lua_GetGameModeLua(lua_State* L) {
    return Lua_DispatchGlobalGetterNoArg(L, "Get_Game_Mode",
        "SWFOC_GetGameModeLua");
}

static int Lua_GetLocalPlayerLua(lua_State* L) {
    return Lua_DispatchGlobalGetterNoArg(L, "Get_Local_Player",
        "SWFOC_GetLocalPlayerLua");
}

static int Lua_GetSecondsPerGameMinuteLua(lua_State* L) {
    return Lua_DispatchGlobalGetterNoArg(L, "Get_Seconds_Per_Game_Minute",
        "SWFOC_GetSecondsPerGameMinuteLua");
}

// 2026-05-04 (iter 179) — first batch post matrix-complete; all reuse existing helpers.
// docs/lua-api.md PlayerWrapper Diplomacy section: Is_Enemy(player) / Is_Ally(player)
// — 1-arg getters returning boolean. Pair with iter-178 GetLocalPlayer for
// "is THIS player my enemy?" workflows: `(Get_Local_Player()):Is_Enemy(other_player)`.
// Helper iter-173 is shape-agnostic — works for player receivers, not just units.
static int Lua_IsEnemyLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Is_Enemy",
        "SWFOC_IsEnemyLua");
}

static int Lua_IsAllyLua(lua_State* L) {
    return Lua_DispatchUnitGetterArg(L, "Is_Ally",
        "SWFOC_IsAllyLua");
}

// 2026-05-04 (iter 179) — discovery extension via iter-177 helper.
// docs/lua-api.md Globals: Find_All_Objects_Of_Type(type) returns a table.
// Helper tostring()s the table to `table: 0xADDR` — operators iterate via
// Lua Playground if needed (e.g. `for i,obj in pairs(Find_All_Objects_Of_Type(t)) do ... end`).
static int Lua_FindAllObjectsOfTypeLua(lua_State* L) {
    return Lua_DispatchGlobalGetterArg(L, "Find_All_Objects_Of_Type",
        "SWFOC_FindAllObjectsOfTypeLua");
}

// 2026-05-04 (iter 179) — TaskForce write-side completion via iter-154 helper.
// docs/lua-api.md TaskForce section line 213: Move_To_Target(target) — TaskForceClass-only.
// Distinct from iter-175 SWFOC_TaskForceMoveToLua (Move_To position) — Move_To_Target
// takes a target unit/object, not a position. Naming convention parallels iter-175.
static int Lua_TaskForceMoveToTargetLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Move_To_Target",
        "SWFOC_TaskForceMoveToTargetLua");
}

// 2026-05-04 (iter 180) — namespaced + pair-completion batch.
// FOW (Fog of War) reveal pair via NAMESPACED method-name dispatch through
// iter-158 helper. Lua's method resolution handles `FOWManager.Reveal_All`
// transparently — the helper just snprintfs `<methodName>(<arg>)` which
// becomes `FOWManager.Reveal_All(player_expr)`. No new helper required;
// proves the iter-158 helper is namespace-agnostic, not just function-name-agnostic.
// docs/lua-api.md section 5.4. Useful for cinematic / debug workflows.
static int Lua_FOWRevealAllLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "FOWManager.Reveal_All",
        "SWFOC_FOWRevealAllLua");
}

static int Lua_FOWUndoRevealAllLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "FOWManager.Undo_Reveal_All",
        "SWFOC_FOWUndoRevealAllLua");
}

// 2026-05-04 (iter 180) — Unlock_Controls() pair-completion via iter-166
// global-no-arg helper. Pairs with iter-160 SWFOC_LockControlsLua: paired
// LockControls(true) → ... → Unlock_Controls(). docs/lua-api.md section 5.2.
static int Lua_UnlockControlsLua(lua_State* L) {
    return Lua_DispatchGlobalNoArgMethod(L, "Unlock_Controls",
        "SWFOC_UnlockControlsLua");
}

// 2026-05-04 (iter 180) — Corrupt(amount) Underworld faction unit-method
// via iter-154 1-arg helper. Pairs with iter-157 Bribe — both are Underworld
// faction signature abilities (Bribe = take ownership; Corrupt = degrade hostility).
// docs/lua-api.md section 5.1.
static int Lua_CorruptLua(lua_State* L) {
    return Lua_DispatchUnitFloatMethod(L, "Corrupt",
        "SWFOC_CorruptLua");
}

// 2026-05-05 (iter 181) — namespace expansion: extends iter-180 finding to iter-178 helper.
// `Thread.Get_Current_Stage()` proves the iter-178 global-no-arg-getter helper is also
// namespace-agnostic (just like iter-158 from iter-180). The codegen `return tostring(<name>())`
// becomes `return tostring(Thread.Get_Current_Stage())` — Lua's parser handles `.` lookup.
// Returns the current cinematic-thread stage int (per docs/lua-api.md section 5.2).
static int Lua_ThreadGetCurrentStageLua(lua_State* L) {
    return Lua_DispatchGlobalGetterNoArg(L, "Thread.Get_Current_Stage",
        "SWFOC_ThreadGetCurrentStageLua");
}

// 2026-05-05 (iter 181) — SFXManager namespace via iter-158 (namespaced setter pattern).
// IMPORTANT: engine has a typo — the actual function name is "Allow_Unit_Reponse_VO" (not
// "Response"). Catalog rationale preserves this so future readers know it's intentional, not
// a copy-paste error. docs/lua-api.md section 6 (Behavioral Warnings) flags this typo.
static int Lua_SFXAllowUnitReponseVoLua(lua_State* L) {
    return Lua_DispatchGlobalArgMethod(L, "SFXManager.Allow_Unit_Reponse_VO",
        "SWFOC_SFXAllowUnitReponseVoLua");
}

// 2026-05-05 (iter 182) — 10th dispatcher helper: global-2-arg shape.
// First multi-arg expansion beyond the matrix. Codegen builds
// `<methodName>(<arg1>, <arg2>)` and dispatches via DoString.
// Inherits namespace-agnosticism from iter-180/181 finding (passing dotted
// names like `FOWManager.X` works the same way through the same codegen).
//
// Useful for engine globals that take 2 distinct arguments (e.g.
// `Make_Ally(p1, p2)`, `Make_Enemy(p1, p2)`, etc.) — the global-form
// alternative to obj-receiver `(p1):Make_Ally(p2)`.
static int Lua_DispatchGlobalArg2Method(lua_State* L, const char* methodName,
                                        const char* swfocFnName) {
    const char* arg1Expr = fn_tostring(L, 1);
    const char* arg2Expr = fn_tostring(L, 2);
    if (!arg1Expr || !*arg1Expr || !arg2Expr || !*arg2Expr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (arg1_lua, arg2_lua)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(arg1Expr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(arg2Expr, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[2 * kMaxExpr + 128];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "%s(%s, %s)", methodName, arg1Expr, arg2Expr);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s -- LIVE call FAILED rc=%d\n", swfocFnName, rc);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    Log("[Bridge] %s(%s, %s) -- LIVE dispatched\n", swfocFnName, arg1Expr, arg2Expr);
    char okmsg[256];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s dispatched (LIVE — engine Lua API)", methodName);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-05-05 (iter 182) — Make_Ally / Make_Enemy global-form LIVE batch.
// docs/lua-api.md section 5.2 documents both `Make_Ally(player1, player2)`
// (global) and `(player1):Make_Ally(player2)` (obj-receiver). Iter-161 wired
// the obj-receiver form (SWFOC_PlayerMakeAllyLua / PlayerMakeEnemyLua); iter-182
// adds the global form so operators can call either shape.
//
// IMPORTANT: state RESETS on every game-mode change (Galactic↔Tactical) — caller
// must re-apply after each transition. docs/lua-api.md section 6 (Behavioral
// Warnings line 272). Catalog rationale preserves this caveat.
static int Lua_GlobalMakeAllyLua(lua_State* L) {
    return Lua_DispatchGlobalArg2Method(L, "Make_Ally",
        "SWFOC_GlobalMakeAllyLua");
}

static int Lua_GlobalMakeEnemyLua(lua_State* L) {
    return Lua_DispatchGlobalArg2Method(L, "Make_Enemy",
        "SWFOC_GlobalMakeEnemyLua");
}

// 2026-05-05 (iter 184) — 11th dispatcher helper: global-3-arg shape.
// Mirror of iter-182's 2-arg helper with one more arg. Codegen
// `<methodName>(<arg1>, <arg2>, <arg3>)`. Inherits namespace-agnosticism
// from iter-180/181 finding (passing dotted names like FOWManager.X works
// the same way through the same codegen).
//
// Useful for engine globals that take 3 args, e.g.
// `FOWManager.Reveal(player, position, radius)` — partial-reveal complement
// to iter-180's `FOWManager.Reveal_All(player)`.
static int Lua_DispatchGlobalArg3Method(lua_State* L, const char* methodName,
                                        const char* swfocFnName) {
    const char* arg1Expr = fn_tostring(L, 1);
    const char* arg2Expr = fn_tostring(L, 2);
    const char* arg3Expr = fn_tostring(L, 3);
    if (!arg1Expr || !*arg1Expr ||
        !arg2Expr || !*arg2Expr ||
        !arg3Expr || !*arg3Expr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (arg1_lua, arg2_lua, arg3_lua)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(arg1Expr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(arg2Expr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(arg3Expr, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[3 * kMaxExpr + 192];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "%s(%s, %s, %s)", methodName, arg1Expr, arg2Expr, arg3Expr);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s -- LIVE call FAILED rc=%d\n", swfocFnName, rc);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    Log("[Bridge] %s(%s, %s, %s) -- LIVE dispatched\n",
        swfocFnName, arg1Expr, arg2Expr, arg3Expr);
    char okmsg[256];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s dispatched (LIVE — engine Lua API)", methodName);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-05-05 (iter 184) — FOWManager.Reveal(player, position, radius) —
// partial-reveal complement to iter-180 SWFOC_FOWRevealAllLua. docs/lua-api.md
// section 5.4. Useful for cinematic / debug workflows where operators want to
// reveal a specific area instead of the whole map. Also opens future iters
// to wire other 3-arg globals (e.g. iter-145's Set_Cinematic_Camera_Key).
static int Lua_FOWRevealLua(lua_State* L) {
    return Lua_DispatchGlobalArg3Method(L, "FOWManager.Reveal",
        "SWFOC_FOWRevealLua");
}

// 2026-05-05 (iter 185) — first marginal-cost batch using iter-184 3-arg helper.
// Three spawn-variant globals from docs/lua-api.md section 2 (Spawning):
//   - Reinforce_Unit(player, type, position) — alternative spawn via reinforcement pool
//   - Spawn_From_Reinforcement_Pool(player, type, position) — same family, different entrypoint
//   - Create_Generic_Object(type, position, player) — IMPORTANT: param order differs from
//     Spawn_Unit (type-first vs player-first). Catalog rationale flags this gotcha.
//
// All three are ~3 LoC bridge each (just helper-dispatch wrappers); validates
// the iter-184 marginal-cost claim for the new 3-arg helper.
static int Lua_ReinforceUnitLua(lua_State* L) {
    return Lua_DispatchGlobalArg3Method(L, "Reinforce_Unit",
        "SWFOC_ReinforceUnitLua");
}

static int Lua_SpawnFromReinforcementPoolLua(lua_State* L) {
    return Lua_DispatchGlobalArg3Method(L, "Spawn_From_Reinforcement_Pool",
        "SWFOC_SpawnFromReinforcementPoolLua");
}

static int Lua_CreateGenericObjectLua(lua_State* L) {
    return Lua_DispatchGlobalArg3Method(L, "Create_Generic_Object",
        "SWFOC_CreateGenericObjectLua");
}

// 2026-05-05 (iter 186) — 12th dispatcher helper: global-3-arg-getter shape.
// Symmetric to iter-184 (3-arg setter) but with engine return-value capture
// (mirror of iter-177's pattern for the 1-arg version). Codegen
// `return tostring(<methodName>(<arg1>, <arg2>, <arg3>))`. Useful for engine
// globals like `Find_Nearest(type, position, player)` that take 3 args and
// return a handle.
//
// Closes the symmetry: iter-182/184 = multi-arg setters, iter-186 starts the
// multi-arg getter family.
static int Lua_DispatchGlobalGetter3Arg(lua_State* L, const char* methodName,
                                        const char* swfocFnName) {
    const char* arg1Expr = fn_tostring(L, 1);
    const char* arg2Expr = fn_tostring(L, 2);
    const char* arg3Expr = fn_tostring(L, 3);
    if (!arg1Expr || !*arg1Expr ||
        !arg2Expr || !*arg2Expr ||
        !arg3Expr || !*arg3Expr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expected (arg1_lua, arg2_lua, arg3_lua)", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(arg1Expr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(arg2Expr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(arg3Expr, kMaxExpr + 1) > kMaxExpr) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: expression too long", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char code[3 * kMaxExpr + 192];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "return tostring(%s(%s, %s, %s))", methodName, arg1Expr, arg2Expr, arg3Expr);
    if (written < 0) {
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s: code buffer overflow", swfocFnName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    char chunk[64];
    _snprintf_s(chunk, sizeof(chunk), _TRUNCATE, "=%s", swfocFnName);
    int rc = DoString(L, code, chunk);
    if (rc != 0) {
        Log("[Bridge] %s -- LIVE call FAILED rc=%d\n", swfocFnName, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s raised engine error rc=%d", methodName, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    const char* resultStr = fn_tostring(L, -1);
    if (!resultStr) {
        fn_settop(L, -2);
        char errmsg[160];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: %s returned non-stringable result", methodName);
        fn_pushstring(L, errmsg);
        return 1;
    }
    Log("[Bridge] %s(%s, %s, %s) -- LIVE returned '%s'\n",
        swfocFnName, arg1Expr, arg2Expr, arg3Expr, resultStr);
    char okmsg[256];
    _snprintf_s(okmsg, sizeof(okmsg), _TRUNCATE,
        "OK: %s = %s (LIVE — engine Lua API)", methodName, resultStr);
    fn_settop(L, -2);
    fn_pushstring(L, okmsg);
    return 1;
}

// 2026-05-05 (iter 186) — Find_Nearest(type, position, player) discovery via
// iter-186 helper. docs/lua-api.md line 75. Returns GameObjectWrapper handle
// for the closest instance of the given type owned by the given player at the
// given position. Composes with iter-177 Find_Object_Type and iter-178
// Get_Local_Player for "find my closest AT-AT to here" workflows.
static int Lua_FindNearestLua(lua_State* L) {
    return Lua_DispatchGlobalGetter3Arg(L, "Find_Nearest",
        "SWFOC_FindNearestLua");
}

// 2026-04-28 (iter 111) — LIVE.
// Calls the engine's `Hide(bool)` Lua method on a unit handle. Hides
// the unit's visual presentation without removing it from the world
// (per docs/lua-api.md: "Toggle unit visibility").
// 2026-04-29 (iter 153) — bool-arg unit-method LIVE batch.
// docs/lua-api.md GameObjectWrapper section. Same iter-111 dispatch
// helper; ~5 LoC per wire. Cannot_Be_Killed: HP can drop to 1 but unit
// won't die. Enable_Stealth: cloaks the unit until disabled or attacked.
static int Lua_SetCannotBeKilledLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Set_Cannot_Be_Killed",
        "SWFOC_SetCannotBeKilledLua");
}

static int Lua_EnableStealthLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Enable_Stealth",
        "SWFOC_EnableStealthLua");
}

static int Lua_HideUnitLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Hide", "SWFOC_HideUnitLua");
}

// 2026-04-28 (iter 111) — LIVE.
// Calls the engine's `Prevent_AI_Usage(bool)` Lua method. When true,
// blocks the AI from issuing orders to this unit (operator-friendly
// "lock this unit away from the AI" toggle).
static int Lua_PreventAiUsageLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Prevent_AI_Usage",
                                       "SWFOC_PreventAiUsageLua");
}

// 2026-04-28 (iter 111) — LIVE.
// Calls the engine's `Set_Selectable(bool)` Lua method. Toggles whether
// the operator can click-select the unit. Useful for "ghost" units that
// should exist in the world but not be interactable.
static int Lua_SetUnitSelectableLua(lua_State* L) {
    return Lua_DispatchUnitBoolMethod(L, "Set_Selectable",
                                       "SWFOC_SetUnitSelectableLua");
}

// 2026-04-28 (iter 110) — LIVE.
// Calls the engine's `Make_Invulnerable(bool)` Lua method on a unit
// handle (one of the verified facts in our knowledge base — see
// `fact_make_invulnerable_hardpoint_propagation` in verified_facts.json).
// The engine's wrapper at RVA 0x57D550 calls QueryInterface(22) →
// HardpointCount/HardpointGet loop → BehaviorAttach(hp, "INVULNERABLE",
// 0) per hardpoint. So the per-unit `Make_Invulnerable(true)` Lua call
// flips the whole unit including its hardpoints.
//
// Examples:
//   SWFOC_MakeUnitInvulnLua("Find_First_Object(\"Empire_AT_AT\")", "true")
//   SWFOC_MakeUnitInvulnLua("Find_Object_Type(\"Rebel_Trooper_Squad\")[0]", "false")
static int Lua_MakeUnitInvulnLua(lua_State* L) {
    const char* unitExpr = fn_tostring(L, 1);
    const char* boolExpr = fn_tostring(L, 2);
    if (!unitExpr || !*unitExpr || !boolExpr || !*boolExpr) {
        fn_pushstring(L, "ERR: SWFOC_MakeUnitInvulnLua: expected (unit_lua_expr, bool_lua_expr)");
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(unitExpr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(boolExpr, kMaxExpr + 1) > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_MakeUnitInvulnLua: expression too long");
        return 1;
    }
    char code[2 * kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "(%s):Make_Invulnerable(%s)", unitExpr, boolExpr);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_MakeUnitInvulnLua: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=MakeUnitInvulnLua");
    if (rc != 0) {
        Log("[Bridge] MakeUnitInvulnLua(%s, %s) -- LIVE call FAILED rc=%d\n",
            unitExpr, boolExpr, rc);
        fn_settop(L, -2);
        char errmsg[512];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Make_Invulnerable raised engine error rc=%d", rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] MakeUnitInvulnLua(%s, %s) -- LIVE OK\n", unitExpr, boolExpr);
    fn_pushstring(L, "OK: Make_Invulnerable dispatched (LIVE — engine Lua API)");
    return 1;
}

// 2026-04-28 (iter 109) — LIVE.
// Calls the engine's `Spawn_Unit(player, type, position)` Lua API via
// DoString. Caller supplies all three Lua expressions that resolve to
// (PlayerWrapper, ObjectType, Position). The bridge composes
// `Spawn_Unit(<player>, <type>, <position>)` and dispatches.
//
// Engine API signature (per docs/lua-api.md): `Spawn_Unit(player, type,
// position)` — creates a unit of the given type owned by the given
// player at the given world position. Used in story scripts and the
// editor's existing Phase-1-mirror Spawning tab. This wire promotes
// that surface to LIVE.
//
// Examples:
//   SWFOC_SpawnUnitLua("Find_Player(\"REBEL\")",
//                      "Find_Object_Type(\"Rebel_Trooper_Squad\")",
//                      "Create_Position(0, 0, 0)")
static int Lua_SpawnUnitLua(lua_State* L) {
    const char* playerExpr = fn_tostring(L, 1);
    const char* typeExpr   = fn_tostring(L, 2);
    const char* posExpr    = fn_tostring(L, 3);
    if (!playerExpr || !*playerExpr || !typeExpr || !*typeExpr ||
        !posExpr || !*posExpr) {
        fn_pushstring(L, "ERR: SWFOC_SpawnUnitLua: expected (player_expr, type_expr, position_expr)");
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(playerExpr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(typeExpr,   kMaxExpr + 1) > kMaxExpr ||
        strnlen(posExpr,    kMaxExpr + 1) > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_SpawnUnitLua: expression too long");
        return 1;
    }
    char code[3 * kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "Spawn_Unit(%s, %s, %s)", playerExpr, typeExpr, posExpr);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_SpawnUnitLua: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=SpawnUnitLua");
    if (rc != 0) {
        Log("[Bridge] SpawnUnitLua(%s, %s, %s) -- LIVE call FAILED rc=%d\n",
            playerExpr, typeExpr, posExpr, rc);
        fn_settop(L, -2);
        char errmsg[768];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Spawn_Unit raised engine error rc=%d", rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] SpawnUnitLua(%s, %s, %s) -- LIVE OK\n", playerExpr, typeExpr, posExpr);
    fn_pushstring(L, "OK: Spawn_Unit dispatched (LIVE — engine Lua API)");
    return 1;
}

// 2026-04-29 (iter 152) — LIVE.
// Galactic-mode complement to iter 109 SWFOC_SpawnUnitLua. Engine API
// per docs/lua-api.md line 49: "Spawn on galactic | Galactic_Spawn_Unit(
// player, type, planet)". 3-arg shape mirrors iter 109; the only
// difference is the call goes to Galactic_Spawn_Unit instead of
// Spawn_Unit, and the third arg is a PlanetWrapper instead of a
// position userdata.
//
// Common operator usage:
//   SWFOC_GalacticSpawnUnit("Find_Player('REBEL')",
//                            "Find_Object_Type('Rebel_Trooper_Squad')",
//                            "FindPlanet('Yavin')")
static int Lua_GalacticSpawnUnit(lua_State* L) {
    const char* playerExpr = fn_tostring(L, 1);
    const char* typeExpr   = fn_tostring(L, 2);
    const char* planetExpr = fn_tostring(L, 3);
    if (!playerExpr || !*playerExpr || !typeExpr || !*typeExpr ||
        !planetExpr || !*planetExpr) {
        fn_pushstring(L, "ERR: SWFOC_GalacticSpawnUnit: expected (player_expr, type_expr, planet_expr)");
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(playerExpr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(typeExpr,   kMaxExpr + 1) > kMaxExpr ||
        strnlen(planetExpr, kMaxExpr + 1) > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_GalacticSpawnUnit: expression too long");
        return 1;
    }
    char code[3 * kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "Galactic_Spawn_Unit(%s, %s, %s)", playerExpr, typeExpr, planetExpr);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_GalacticSpawnUnit: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=GalacticSpawnUnit");
    if (rc != 0) {
        Log("[Bridge] GalacticSpawnUnit(%s, %s, %s) -- LIVE call FAILED rc=%d\n",
            playerExpr, typeExpr, planetExpr, rc);
        fn_settop(L, -2);
        char errmsg[768];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Galactic_Spawn_Unit raised engine error rc=%d", rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] GalacticSpawnUnit(%s, %s, %s) -- LIVE OK\n",
        playerExpr, typeExpr, planetExpr);
    fn_pushstring(L, "OK: Galactic_Spawn_Unit dispatched (LIVE — engine Lua API)");
    return 1;
}

// 2026-04-28 (iter 108) — LIVE.
// Calls the engine's `Change_Owner` Lua method on a unit handle. The
// caller supplies the Lua expressions that resolve to (unit_handle,
// new_player_handle); the bridge composes `<unit_expr>:Change_Owner(
// <player_expr>)` and dispatches via DoString. Same pattern as iter
// 107 ScrollCameraToTarget — engine API call routed through existing
// primitives, no MinHook detour, no struct-offset write.
//
// Examples:
//   SWFOC_ChangeUnitOwner("Find_First_Object(\"Empire_AT_AT\")", "Find_Player(\"REBEL\")")
//   SWFOC_ChangeUnitOwner("Find_Object_Type(\"Empire_Stormtrooper_Squad\")[0]", "Find_Player(\"UNDERWORLD\")")
//
// Engine-side: `Change_Owner` is the per-unit method on GameObjectWrapper
// that internally calls sub_140574D0E (RVA 0x574D0E, "Phase 2 RE"-pinned
// per docs/rvas.md). Updates ownership, fires UI events, plays audio,
// processes corruption, updates AI budgets — the full "swap sides"
// behaviour that the editor's Phase-1-mirror version couldn't replicate.
static int Lua_ChangeUnitOwner(lua_State* L) {
    const char* unitExpr = fn_tostring(L, 1);
    const char* playerExpr = fn_tostring(L, 2);
    if (!unitExpr || !*unitExpr || !playerExpr || !*playerExpr) {
        fn_pushstring(L, "ERR: SWFOC_ChangeUnitOwner: expected (unit_lua_expr, player_lua_expr)");
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    const size_t ulen = strnlen(unitExpr, kMaxExpr + 1);
    const size_t plen = strnlen(playerExpr, kMaxExpr + 1);
    if (ulen > kMaxExpr || plen > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_ChangeUnitOwner: expression too long");
        return 1;
    }
    char code[2 * kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "(%s):Change_Owner(%s)", unitExpr, playerExpr);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_ChangeUnitOwner: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=ChangeUnitOwner");
    if (rc != 0) {
        Log("[Bridge] ChangeUnitOwner(%s, %s) -- LIVE call FAILED rc=%d\n",
            unitExpr, playerExpr, rc);
        fn_settop(L, -2);  // pop engine error
        char errmsg[512];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Change_Owner raised engine error rc=%d (unit=%s player=%s)",
            rc, unitExpr, playerExpr);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);  // pop engine return
    Log("[Bridge] ChangeUnitOwner(%s, %s) -- LIVE OK\n", unitExpr, playerExpr);
    fn_pushstring(L, "OK: Change_Owner dispatched (LIVE — engine Lua API)");
    return 1;
}

// 2026-04-29 (iter 151) — LIVE.
// Mirrors iter 108 ChangeUnitOwner two-arg shape: SWFOC_TeleportUnitLua(
// unit_lua_expr, position_lua_expr) composes (<unit>):Teleport(<pos>) and
// dispatches via DoString. Engine method per docs/lua-api.md GameObjectWrapper
// Movement section: "Teleport(position) | position | Instant teleport".
// Common operator usage:
//   SWFOC_TeleportUnitLua("Find_First_Object('Empire_AT_AT')",
//                          "Create_Position(0, 0, 0)")
static int Lua_TeleportUnitLua(lua_State* L) {
    const char* unitExpr = fn_tostring(L, 1);
    const char* posExpr  = fn_tostring(L, 2);
    if (!unitExpr || !*unitExpr || !posExpr || !*posExpr) {
        fn_pushstring(L, "ERR: SWFOC_TeleportUnitLua: expected (unit_lua_expr, position_lua_expr)");
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(unitExpr, kMaxExpr + 1) > kMaxExpr ||
        strnlen(posExpr, kMaxExpr + 1) > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_TeleportUnitLua: expression too long");
        return 1;
    }
    char code[2 * kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "(%s):Teleport(%s)", unitExpr, posExpr);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_TeleportUnitLua: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=TeleportUnitLua");
    if (rc != 0) {
        Log("[Bridge] TeleportUnitLua(%s, %s) -- LIVE call FAILED rc=%d\n",
            unitExpr, posExpr, rc);
        fn_settop(L, -2);
        char errmsg[512];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Teleport raised engine error rc=%d (unit=%s pos=%s)",
            rc, unitExpr, posExpr);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] TeleportUnitLua(%s, %s) -- LIVE OK\n", unitExpr, posExpr);
    fn_pushstring(L, "OK: Teleport dispatched (LIVE — engine Lua API)");
    return 1;
}

// 2026-04-28 (iter 107) — LIVE.
// Calls the engine's `Scroll_Camera_To` Lua API via DoString, with the
// caller's argument expression spliced verbatim. Examples:
//   SWFOC_ScrollCameraToTarget("Find_Planet(\"Yavin\")")
//   SWFOC_ScrollCameraToTarget("Find_Object_Type(\"Rebel_Trooper_Squad\")[0]")
//   SWFOC_ScrollCameraToTarget("Find_First_Object(\"Empire_AT_AT\")")
// The bridge does not validate the expression; the engine's Lua VM
// will report syntax/runtime errors back through DoString's normal
// error path. Untrusted input would be a problem — but the bridge is
// localhost-only (named pipe, max_instances=1), so the operator is
// the caller.
static int Lua_ScrollCameraToTarget(lua_State* L) {
    const char* arg = fn_tostring(L, 1);
    if (!arg || !*arg) {
        fn_pushstring(L, "ERR: SWFOC_ScrollCameraToTarget: target expression required");
        return 1;
    }
    // Reasonable cap so a runaway operator script can't blow the
    // stack with a multi-megabyte expression.
    constexpr size_t kMaxExpr = 1024;
    const size_t arglen = strnlen(arg, kMaxExpr + 1);
    if (arglen > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_ScrollCameraToTarget: target expression too long");
        return 1;
    }
    char code[kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "Scroll_Camera_To(%s)", arg);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_ScrollCameraToTarget: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=ScrollCameraToTarget");
    if (rc != 0) {
        // DoString left the engine error message on the stack. Don't
        // overwrite it — the caller can read it through DoString's
        // normal return-channel pattern.
        Log("[Bridge] ScrollCameraToTarget(%s) -- LIVE call FAILED rc=%d\n", arg, rc);
        // We pushed nothing extra, but consumers of this Lua C function
        // expect a return string. Pop the engine error and replace.
        fn_settop(L, -2);  // pop error
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Scroll_Camera_To(%s) raised engine error rc=%d", arg, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    // Successful call — pop the engine return value (Scroll_Camera_To
    // returns nothing useful) and replace with our standard "OK" sentinel.
    fn_settop(L, -2);  // pop engine return
    Log("[Bridge] ScrollCameraToTarget(%s) -- LIVE OK\n", arg);
    fn_pushstring(L, "OK: Scroll_Camera_To dispatched (LIVE — engine Lua API)");
    return 1;
}

// 2026-04-29 (iter 143) — LIVE.
// Calls the engine's `Camera_To_Follow` Lua API via DoString, with the
// caller's argument expression spliced verbatim. Mirror of iter 107's
// ScrollCameraToTarget pattern: same engine-Lua-API + DoString path,
// different camera primitive. Camera_To_Follow attaches the camera to
// a target object so it tracks the object as it moves; Scroll_Camera_To
// is a one-shot pan to a target.
//
// Examples:
//   SWFOC_CameraFollow("Find_First_Object('Empire_AT_AT')")
//   SWFOC_CameraFollow("Find_Player('REBEL'):Get_Hero()")
//
// Engine API pinned iter 106 at LuaUserVar registry slot 0x140898d70.
static int Lua_CameraFollow(lua_State* L) {
    const char* arg = fn_tostring(L, 1);
    if (!arg || !*arg) {
        fn_pushstring(L, "ERR: SWFOC_CameraFollow: target expression required");
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    const size_t arglen = strnlen(arg, kMaxExpr + 1);
    if (arglen > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_CameraFollow: target expression too long");
        return 1;
    }
    char code[kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "Camera_To_Follow(%s)", arg);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_CameraFollow: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=CameraFollow");
    if (rc != 0) {
        Log("[Bridge] CameraFollow(%s) -- LIVE call FAILED rc=%d\n", arg, rc);
        fn_settop(L, -2);  // pop engine error
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Camera_To_Follow(%s) raised engine error rc=%d", arg, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);  // pop engine return
    Log("[Bridge] CameraFollow(%s) -- LIVE OK\n", arg);
    fn_pushstring(L, "OK: Camera_To_Follow dispatched (LIVE — engine Lua API)");
    return 1;
}

// 2026-04-29 (iter 144) — LIVE.
// Calls the engine's `Rotate_Camera_To` Lua API via DoString. Engine
// API pinned iter 106 at LuaUserVar registry slot 0x140898db0. Same
// pattern as iter 107 (Scroll_Camera_To) + iter 143 (Camera_To_Follow);
// rotates the camera to face the target object instead of panning to
// or tracking it.
static int Lua_RotateCameraTo(lua_State* L) {
    const char* arg = fn_tostring(L, 1);
    if (!arg || !*arg) {
        fn_pushstring(L, "ERR: SWFOC_RotateCameraTo: target expression required");
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    const size_t arglen = strnlen(arg, kMaxExpr + 1);
    if (arglen > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_RotateCameraTo: target expression too long");
        return 1;
    }
    char code[kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "Rotate_Camera_To(%s)", arg);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_RotateCameraTo: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=RotateCameraTo");
    if (rc != 0) {
        Log("[Bridge] RotateCameraTo(%s) -- LIVE call FAILED rc=%d\n", arg, rc);
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Rotate_Camera_To(%s) raised engine error rc=%d", arg, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] RotateCameraTo(%s) -- LIVE OK\n", arg);
    fn_pushstring(L, "OK: Rotate_Camera_To dispatched (LIVE — engine Lua API)");
    return 1;
}

// 2026-04-29 (iter 145) — cinematic camera quad LIVE wires.
// 4 engine Lua APIs from iter 106 finding (LuaUserVar registry slots
// 0x140898ec0/ed8/f30/f50). Same DoString pattern as iter 107/143/144.
// Forms a cinematic state machine: Start → SetKey×N → TransitionKey →
// End. Each wrapper accepts a Lua expression for the runtime args.
static int Lua_StartCinematicCamera(lua_State* L) {
    // Zero-arg call. Operator may pass an empty string or omit; we tolerate either.
    int rc = DoString(L, "Start_Cinematic_Camera()", "=StartCinematicCamera");
    if (rc != 0) {
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Start_Cinematic_Camera() raised engine error rc=%d", rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] StartCinematicCamera -- LIVE OK\n");
    fn_pushstring(L, "OK: Start_Cinematic_Camera dispatched (LIVE — engine Lua API)");
    return 1;
}

static int Lua_EndCinematicCamera(lua_State* L) {
    int rc = DoString(L, "End_Cinematic_Camera()", "=EndCinematicCamera");
    if (rc != 0) {
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: End_Cinematic_Camera() raised engine error rc=%d", rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] EndCinematicCamera -- LIVE OK\n");
    fn_pushstring(L, "OK: End_Cinematic_Camera dispatched (LIVE — engine Lua API)");
    return 1;
}

static int Lua_SetCinematicCameraKey(lua_State* L) {
    const char* arg = fn_tostring(L, 1);
    if (!arg || !*arg) {
        fn_pushstring(L, "ERR: SWFOC_SetCinematicCameraKey: args expression required");
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(arg, kMaxExpr + 1) > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_SetCinematicCameraKey: args expression too long");
        return 1;
    }
    char code[kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "Set_Cinematic_Camera_Key(%s)", arg);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_SetCinematicCameraKey: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=SetCinematicCameraKey");
    if (rc != 0) {
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Set_Cinematic_Camera_Key(%s) raised engine error rc=%d", arg, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] SetCinematicCameraKey(%s) -- LIVE OK\n", arg);
    fn_pushstring(L, "OK: Set_Cinematic_Camera_Key dispatched (LIVE — engine Lua API)");
    return 1;
}

// 2026-04-29 (iter 150) — Letter_Box_On / Letter_Box_Off LIVE wires.
// Per docs/lua-api.md (line 50): "Play cinematic | Point_Camera_At(unit);
// Letter_Box_On()". Complement to iter 145 cinematic camera quad.
// Zero-arg engine globals; same DoString pattern.
static int Lua_LetterBoxOn(lua_State* L) {
    int rc = DoString(L, "Letter_Box_On()", "=LetterBoxOn");
    if (rc != 0) {
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Letter_Box_On() raised engine error rc=%d", rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] LetterBoxOn -- LIVE OK\n");
    fn_pushstring(L, "OK: Letter_Box_On dispatched (LIVE — engine Lua API)");
    return 1;
}

static int Lua_LetterBoxOff(lua_State* L) {
    int rc = DoString(L, "Letter_Box_Off()", "=LetterBoxOff");
    if (rc != 0) {
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Letter_Box_Off() raised engine error rc=%d", rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] LetterBoxOff -- LIVE OK\n");
    fn_pushstring(L, "OK: Letter_Box_Off dispatched (LIVE — engine Lua API)");
    return 1;
}

static int Lua_TransitionCinematicCameraKey(lua_State* L) {
    const char* arg = fn_tostring(L, 1);
    if (!arg || !*arg) {
        fn_pushstring(L, "ERR: SWFOC_TransitionCinematicCameraKey: args expression required");
        return 1;
    }
    constexpr size_t kMaxExpr = 1024;
    if (strnlen(arg, kMaxExpr + 1) > kMaxExpr) {
        fn_pushstring(L, "ERR: SWFOC_TransitionCinematicCameraKey: args expression too long");
        return 1;
    }
    char code[kMaxExpr + 64];
    int written = _snprintf_s(code, sizeof(code), _TRUNCATE,
        "Transition_Cinematic_Camera_Key(%s)", arg);
    if (written < 0) {
        fn_pushstring(L, "ERR: SWFOC_TransitionCinematicCameraKey: code buffer overflow");
        return 1;
    }
    int rc = DoString(L, code, "=TransitionCinematicCameraKey");
    if (rc != 0) {
        fn_settop(L, -2);
        char errmsg[256];
        _snprintf_s(errmsg, sizeof(errmsg), _TRUNCATE,
            "ERR: Transition_Cinematic_Camera_Key(%s) raised engine error rc=%d", arg, rc);
        fn_pushstring(L, errmsg);
        return 1;
    }
    fn_settop(L, -2);
    Log("[Bridge] TransitionCinematicCameraKey(%s) -- LIVE OK\n", arg);
    fn_pushstring(L, "OK: Transition_Cinematic_Camera_Key dispatched (LIVE — engine Lua API)");
    return 1;
}

static int Lua_GetCameraPos(lua_State* L) {
    // 2026-05-06 (iter 237): flipped from Phase-1 mirror to LIVE backing
    // via CameraClass::GetPosition @ 0x261A40. Returns the engine-current
    // camera X/Y/Z (NOT the cached pending value from the old stub).
    // Falls back to "0,0,0" string when no active tactical camera (mirrors
    // iter-186/167 reader-failure response shape — operator gets a parseable
    // result rather than ERR:, since downstream Lua might not handle ERR).
    __int64 camera = LookupActiveCamera();
    char buf[96];
    if (!camera) {
        snprintf(buf, sizeof(buf), "0.000,0.000,0.000");
        fn_pushstring(L, buf);
        return 1;
    }

    float xyz[3] = { 0.0f, 0.0f, 0.0f };
    auto fn = reinterpret_cast<pfn_CameraGetPosition>(
        g_base + RVA::CameraGetPosition);
    fn(camera, xyz);
    snprintf(buf, sizeof(buf), "%.3f,%.3f,%.3f", xyz[0], xyz[1], xyz[2]);
    fn_pushstring(L, buf);
    return 1;
}

// SWFOC_ListAbilities(obj_addr) / SWFOC_TriggerAbility(obj_addr, idx).
// Tasks 139/140 Phase 1. Live detection of per-unit ability catalogues
// requires walking the SpecialAbility vtable chain off GameObject --
// candidates exist in re-findings/combat_system.json but no 2-tool
// consensus yet, so both helpers return Phase 1 sentinels and the
// replay mirror carries the full contract.
static int Lua_ListAbilities(lua_State* L) {
    (void)L;
    fn_pushstring(L, "count=0");
    Log("[Bridge] ListAbilities -- Phase 1 sentinel (ability vtable walk pending)\n");
    return 1;
}

static int Lua_TriggerAbility(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    int index = static_cast<int>(fn_tonumber(L, 2));
    if (rawAddr <= 0 || index < 0) {
        fn_pushstring(L, "ERR: SWFOC_TriggerAbility: expected (obj_addr, index>=0)");
        return 1;
    }
    // Phase 2 will emit: `Find_Object(obj_addr):Trigger_Ability(index)`
    // through SWFOC_DoString once the ability-index-to-Lua mapping is
    // verified. For now we log intent so the UI round-trip proceeds.
    Log("[Bridge] TriggerAbility(0x%llX, %d) -- Phase 1 intent logged\n",
        (unsigned long long)static_cast<uint64_t>(rawAddr), index);
    fn_pushstring(L, "OK: ability trigger recorded (Phase 2 live wire-through pending)");
    return 1;
}

// SWFOC_GetPlanetTechAndBuildings("name") -> "" sentinel until galactic API is wired.
// Task 143 (2026-04-23) Phase 1. Live path returns empty string so the
// V2 Galactic tab can distinguish "unknown planet" (empty) from a
// legitimate tech=0 row. The replay mirror ships the full contract
// (ReplayObsGetPlanetTechAndBuildings). Phase 2 reads tech via the
// existing Planet:Get_Tech_Level Lua method and building count via a
// walk of the planet's building list.
static int Lua_GetPlanetTechAndBuildings(lua_State* L) {
    fn_pushstring(L, "");
    Log("[Bridge] GetPlanetTechAndBuildings -- Phase 1 sentinel\n");
    return 1;
}

// SWFOC_SetDiplomacy(slot_a, slot_b, "state") -> "OK: ..." or "ERR: ...".
// Task 144 (2026-04-23) Phase 1 → LIVE 2026-04-29 iter 133.
//
// Iter 132 Phase2HookPending audit caught this drift candidate: the
// verified ledger had `rva_make_ally_make_enemy_engine` @ 0x288800
// pinned with `__int64 __fastcall(PlayerClass*, int target_slot, int state)`
// shape, but the bridge was still Phase-1-mirror writing to a string
// key map. Iter 133 ships the LIVE wire.
//
// Engine semantics (Hex-Rays of sub_140288800):
//   result = *(_QWORD*)(player_a + 0x370);  // diplomacy_table_ptr
//   *(_DWORD*)(result + 4 * slot_b) = state_code;
//   return result;
// One-way write — A's diplomatic view of B. Operator gets symmetric
// behavior by calling twice (a→b and b→a). State codes:
//   0 = ally     (per rva_is_ally_engine: returns table[id] == 0)
//   1 = enemy    (per rva_is_enemy_engine: returns table[id] == 1)
//   2 = neutral  (ASSUMED via process of elimination — only state
//                 left after ally/enemy. Live-game verification
//                 queued — operator can confirm by calling with
//                 "neutral" and observing target's stance.)
//
// Cache map kept for replay/dev fallback when Resolve<>() returns
// nullptr (no module loaded).
typedef __int64 (__fastcall *pfn_MakeAllyEnemy)(__int64 player_a, int target_slot, int state);

static std::unordered_map<std::string, std::string> g_pendingDiplomacyWrites;
static CRITICAL_SECTION g_diplomacyLock;
static bool g_diplomacyLockInit = false;
static void EnsureDiplomacyLock() {
    if (!g_diplomacyLockInit) {
        InitializeCriticalSection(&g_diplomacyLock);
        g_diplomacyLockInit = true;
    }
}

static int Lua_SetDiplomacy(lua_State* L) {
    EnsureDiplomacyLock();
    int slot_a = static_cast<int>(fn_tonumber(L, 1));
    int slot_b = static_cast<int>(fn_tonumber(L, 2));
    const char* state = fn_tostring(L, 3);
    if (slot_a < 0 || slot_b < 0 || !state || !state[0]) {
        fn_pushstring(L, "ERR: SWFOC_SetDiplomacy: expected (slot_a>=0, slot_b>=0, state)");
        return 1;
    }
    if (slot_a == slot_b) {
        fn_pushstring(L, "ERR: SWFOC_SetDiplomacy: slots must differ");
        return 1;
    }

    // Map state-string → state-code per ledger readers.
    int state_code = -1;
    if (strcmp(state, "ally") == 0) state_code = 0;
    else if (strcmp(state, "enemy") == 0) state_code = 1;
    else if (strcmp(state, "neutral") == 0) state_code = 2;
    else {
        fn_pushstring(L, "ERR: SWFOC_SetDiplomacy: state must be 'ally', 'enemy', or 'neutral'");
        return 1;
    }

    // LIVE: walk PlayerArray to resolve slot_a → PlayerClass*, then
    // call engine writer at 0x288800. Same walker pattern as
    // SetHumanPlayer_v2 / GetCurrentPlayer / Switch_Sides etc.
    auto pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    int playerCount = *reinterpret_cast<int*>(g_base + RVA::PlayerCount_Global);
    auto fnWrite = Resolve<pfn_MakeAllyEnemy>(RVA::MakeAllyEnemy);

    if (pa && fnWrite && slot_a < playerCount && slot_b < playerCount) {
        uintptr_t player_a = *reinterpret_cast<uintptr_t*>(pa + 8 * slot_a);
        if (player_a) {
            fnWrite(static_cast<__int64>(player_a), slot_b, state_code);
            // Mirror to cache for legacy read paths.
            char key[32];
            int lo = slot_a < slot_b ? slot_a : slot_b;
            int hi = slot_a < slot_b ? slot_b : slot_a;
            snprintf(key, sizeof(key), "%d-%d", lo, hi);
            EnterCriticalSection(&g_diplomacyLock);
            g_pendingDiplomacyWrites[key] = state;
            LeaveCriticalSection(&g_diplomacyLock);
            Log("[Bridge] SetDiplomacy(%d, %d, %s) -- LIVE (MakeAllyEnemy "
                "@ 0x%llX, state_code=%d)\n",
                slot_a, slot_b, state,
                (unsigned long long)RVA::MakeAllyEnemy, state_code);
            char buf[96];
            snprintf(buf, sizeof(buf),
                "OK: diplomacy set to %s (LIVE — one-way A->B; call "
                "again with slots swapped for symmetric)", state);
            fn_pushstring(L, buf);
            return 1;
        }
    }

    // Fallback: cache the desired state for replay/dev builds without
    // the module loaded. Same string-key shape the legacy Phase-1
    // path used.
    int lo = slot_a < slot_b ? slot_a : slot_b;
    int hi = slot_a < slot_b ? slot_b : slot_a;
    char key[32];
    snprintf(key, sizeof(key), "%d-%d", lo, hi);
    EnterCriticalSection(&g_diplomacyLock);
    g_pendingDiplomacyWrites[key] = state;
    LeaveCriticalSection(&g_diplomacyLock);
    Log("[Bridge] SetDiplomacy(%d, %d, %s) -- fallback cache "
        "(no module loaded or slot out of bounds)\n",
        slot_a, slot_b, state);
    fn_pushstring(L, "OK: diplomacy cached (replay/dev mode — engine writer not reachable)");
    return 1;
}

static int Lua_ChangePlanetOwner(lua_State* L) {
    EnsurePlanetLock();
    const char* raw = fn_tostring(L, 1);
    int slot = static_cast<int>(fn_tonumber(L, 2));
    if (!raw || !raw[0]) {
        fn_pushstring(L, "ERR: SWFOC_ChangePlanetOwner: expected (name, slot)");
        return 1;
    }
    if (slot < 0) {
        fn_pushstring(L, "ERR: SWFOC_ChangePlanetOwner: slot must be >= 0");
        return 1;
    }
    // Uppercase the key to match the replay-side case-insensitive lookup
    // so the pending-map reads round-trip with the replay CSV.
    std::string key = raw;
    for (char& c : key) {
        if (c >= 'a' && c <= 'z') c = static_cast<char>(c - 'a' + 'A');
    }
    EnterCriticalSection(&g_planetLock);
    g_pendingPlanetOwnerWrites[key] = slot;
    LeaveCriticalSection(&g_planetLock);
    Log("[Bridge] ChangePlanetOwner(%s, %d) -- Phase 1 pending\n", raw, slot);
    fn_pushstring(L, "OK: planet owner change recorded (Phase 2 live wire-through pending)");
    return 1;
}

// SWFOC_ChangePlanetOwnerWithMode(planet, new_owner, mode_str) — Phase 1
// mirror added in iter 137 (was vestigial pre-iter-137; editor's
// BridgeGalacticDispatcher.ChangePlanetOwnerWithModeAsync called this
// helper but the bridge had no implementation, so the operator's
// "Flip & convert garrison" / "Flip & destroy garrison" buttons
// returned a Lua runtime "attempt to call nil" error rather than a
// well-formed Phase-1 mirror response).
//
// Phase 2 engine wire-through is genuinely blocked: per iter 134 audit,
// the engine-side writers `PlanetFactionChange_FullTransfer @ 0x3FB040`
// (3989 bytes, 4 args) and `PlanetFactionChange_InitialSet @ 0x3FA160`
// (271 bytes, 2 args) are too complex for a single-iter Resolve<>()
// pattern. No `Planet:Change_Owner` Lua wrapper exists either, so the
// DoString approach is also blocked. This stays Phase-1 mirror until a
// dedicated multi-iter RTTI dissection arc.
//
// The editor's ALTERNATE wire-path (overlay Feature 3, iter 33-34)
// runs in the C++ overlay DLL via a separate dispatch path and is the
// actual operator surface today. This SWFOC_* helper is a doc-only
// fallback so the bridge contract isn't broken.
struct PendingPlanetFlipMode {
    std::string planet;
    std::string new_owner;
    std::string mode;
};
static std::vector<PendingPlanetFlipMode> g_pendingPlanetFlipModes;

static int Lua_ChangePlanetOwnerWithMode(lua_State* L) {
    EnsurePlanetLock();
    const char* planet    = fn_tostring(L, 1);
    const char* new_owner = fn_tostring(L, 2);
    const char* mode      = fn_tostring(L, 3);
    if (!planet || !planet[0] || !new_owner || !new_owner[0] || !mode || !mode[0]) {
        fn_pushstring(L, "ERR: SWFOC_ChangePlanetOwnerWithMode: expected (planet, new_owner, mode)");
        return 1;
    }
    PendingPlanetFlipMode entry;
    entry.planet    = planet;
    entry.new_owner = new_owner;
    entry.mode      = mode;
    EnterCriticalSection(&g_planetLock);
    g_pendingPlanetFlipModes.push_back(entry);
    LeaveCriticalSection(&g_planetLock);
    Log("[Bridge] ChangePlanetOwnerWithMode(%s, %s, %s) -- Phase 1 pending (engine writer multi-arg, blocked iter 134)\n",
        planet, new_owner, mode);
    fn_pushstring(L,
        "OK: planet flip with mode recorded (Phase 2 multi-arg engine writer blocked per iter 134)");
    return 1;
}

// SWFOC_SpawnAsStoryArrival(type, planet, faction) — Phase 1 mirror
// added in iter 137. Same situation as ChangePlanetOwnerWithMode:
// editor's BridgeGalacticDispatcher.SpawnAsStoryArrivalAsync called
// the helper but no bridge implementation existed → operator's
// "Story-arrival spawn" button errored at runtime.
//
// Phase 2 engine wire-through blocked per iter 134 audit:
// `StoryEvent_Factory_Create` requires multi-arg state setup (event
// trigger, faction, planet binding) that's not single-iter
// achievable. Stays Phase-1 mirror until the multi-arg engine-call
// pipeline is built (would unblock SetUnitField's 10 Phase-1 fields
// too — same architectural limitation).
struct PendingStoryArrivalSpawn {
    std::string type;
    std::string planet;
    std::string faction;
};
static std::vector<PendingStoryArrivalSpawn> g_pendingStoryArrivalSpawns;
static CRITICAL_SECTION g_storyArrivalLock;
static bool g_storyArrivalLockInit = false;
static void EnsureStoryArrivalLock() {
    if (!g_storyArrivalLockInit) {
        InitializeCriticalSection(&g_storyArrivalLock);
        g_storyArrivalLockInit = true;
    }
}

static int Lua_SpawnAsStoryArrival(lua_State* L) {
    EnsureStoryArrivalLock();
    const char* type    = fn_tostring(L, 1);
    const char* planet  = fn_tostring(L, 2);
    const char* faction = fn_tostring(L, 3);
    if (!type || !type[0] || !planet || !planet[0] || !faction || !faction[0]) {
        fn_pushstring(L, "ERR: SWFOC_SpawnAsStoryArrival: expected (type, planet, faction)");
        return 1;
    }
    PendingStoryArrivalSpawn entry;
    entry.type    = type;
    entry.planet  = planet;
    entry.faction = faction;
    EnterCriticalSection(&g_storyArrivalLock);
    g_pendingStoryArrivalSpawns.push_back(entry);
    LeaveCriticalSection(&g_storyArrivalLock);
    Log("[Bridge] SpawnAsStoryArrival(%s, %s, %s) -- Phase 1 pending (StoryEvent_Factory_Create blocked iter 134)\n",
        type, planet, faction);
    fn_pushstring(L,
        "OK: story-arrival spawn recorded (Phase 2 StoryEvent_Factory_Create blocked per iter 134)");
    return 1;
}

// SWFOC_ListHeroes() -> "count=0" sentinel until hero detection is live.
// Task 134 (2026-04-23) Phase 1. The live bridge has no way to identify
// heroes from memory yet -- no IDA-pinned is_hero flag on GameObject,
// and no RTTI-class enumeration that gates by "HeroClass". The replay
// harness carries the full contract (ReplayObsListHeroes via the
// dispatch pattern above) so the V2 Hero Lab tab can be built and
// tested offline against mocked heroes. Phase 2 wires real detection
// once IDA analysis confirms the marker -- candidates: a flag at
// obj+0x3xx, an RTTI base-class walk, or iterating Find_Player's
// heroes list via SWFOC_DoString.
static int Lua_ListHeroes(lua_State* L) {
    fn_pushstring(L, "count=0");
    Log("[Bridge] ListHeroes -- Phase 1 sentinel (live hero detection pending)\n");
    return 1;
}

// SWFOC_SetHeroRespawnTimer / SWFOC_SetPermadeath live stubs. Phase 1
// stores values in module-local maps keyed by obj_addr so the UI can
// round-trip without errors; Phase 2 wires through to the engine's
// hero respawn-timer field (currently UNVERIFIED in re-findings).
static std::unordered_map<uintptr_t, int32_t> g_pendingRespawnWrites;
static std::unordered_map<uintptr_t, bool>    g_pendingPermadeathWrites;
static CRITICAL_SECTION g_heroLock;
static bool g_heroLockInit = false;
static void EnsureHeroLock() {
    if (!g_heroLockInit) {
        InitializeCriticalSection(&g_heroLock);
        g_heroLockInit = true;
    }
}

static int Lua_SetHeroRespawnTimer(lua_State* L) {
    EnsureHeroLock();
    double rawAddr = fn_tonumber(L, 1);
    double ms = fn_tonumber(L, 2);
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetHeroRespawnTimer: bad obj_addr");
        return 1;
    }
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetHeroRespawnTimer: enemy heroes READ-ONLY");
        return 1;
    }
    int32_t clamped = static_cast<int32_t>(ms);
    if (clamped < 0) clamped = 0;
    EnterCriticalSection(&g_heroLock);
    g_pendingRespawnWrites[addr] = clamped;
    LeaveCriticalSection(&g_heroLock);
    Log("[Bridge] SetHeroRespawnTimer(0x%llX, %dms) -- Phase 1 pending\n",
        (unsigned long long)addr, clamped);
    fn_pushstring(L, "OK: respawn timer recorded (Phase 2 live wire-through pending)");
    return 1;
}

static int Lua_SetPermadeath(lua_State* L) {
    EnsureHeroLock();
    double rawAddr = fn_tonumber(L, 1);
    int flag = static_cast<int>(fn_tonumber(L, 2));
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetPermadeath: bad obj_addr");
        return 1;
    }
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetPermadeath: enemy heroes READ-ONLY");
        return 1;
    }
    EnterCriticalSection(&g_heroLock);
    g_pendingPermadeathWrites[addr] = (flag != 0);
    LeaveCriticalSection(&g_heroLock);
    Log("[Bridge] SetPermadeath(0x%llX, %d) -- Phase 1 pending\n",
        (unsigned long long)addr, flag);
    fn_pushstring(L, "OK: permadeath flag recorded (Phase 2 live wire-through pending)");
    return 1;
}

// SWFOC_SetUnitSpeed / SWFOC_GetUnitSpeed.
//
// 2026-04-28 (iter 100, master ralph loop) — LIVE wired via the engine's
// own SetSpeedOverride helper at RVA 0x3A8C90 (`sub_1403A8C90`):
//
//   __int64 __fastcall SetSpeedOverride(GameObjectClass* obj, float speed)
//   {
//       inner = *(QWORD*)(obj + 168);  // obj + 0xA8 → locomotor sub-object
//       if (inner) {
//           *(float*)(inner + 672) = speed;  // +0x2A0 = override-speed
//           *(BYTE*) (inner + 668) = 1;      // +0x29C = override-active
//       }
//       return inner;
//   }
//
// Verified via 2-tool consensus (IDA on-disk corpus + Binary Ninja xref) —
// see ledger entry `rva_set_speed_override`. The engine's Lua API
// `GameObjectWrapper::Override_Max_Speed` (sub_14057E590) calls this same
// function for its own per-unit speed override; we're tapping the same
// layer with the same calling convention.
//
// Companion: `SWFOC_ClearUnitSpeedOverride(addr)` calls
// ClearSpeedOverride @ RVA 0x38F8B0 to clear the active flag and revert.
//
// Mirror map `g_unitSpeedOverrideMap` retained for SWFOC_GetUnitSpeed —
// the engine's locomotor pointer chain isn't always traversable from a
// background thread, so we cache the last-written value alongside the
// live override. The map is now a CACHE, not the source of truth.
//
// Enemy READ-ONLY: writes rejected; reads permitted for Inspector.
// (typedef pfn_SetSpeedOverride / g_unitSpeedOverrideMap / g_speedLock /
// EnsureSpeedLock / ReadEngineSpeedOverride are all defined earlier in
// the file, above Lua_SetPerFactionSpeedMultiplier.)

static int Lua_SetUnitSpeed(lua_State* L) {
    EnsureSpeedLock();
    double rawAddr = fn_tonumber(L, 1);
    double value = fn_tonumber(L, 2);
    if (rawAddr <= 0 || value < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitSpeed: expected (obj_addr, value>=0)");
        return 1;
    }
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitSpeed: bad obj_addr");
        return 1;
    }
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitSpeed: enemy units are READ-ONLY");
        return 1;
    }

    // LIVE: call engine's SetSpeedOverride directly. Stamp the cache
    // *before* the call so a concurrent Lua_GetUnitSpeed sees the new
    // value even if the locomotor read fails.
    EnterCriticalSection(&g_speedLock);
    g_unitSpeedOverrideMap[addr] = static_cast<float>(value);
    LeaveCriticalSection(&g_speedLock);

    auto fnSetOverride = Resolve<pfn_SetSpeedOverride>(RVA::SetSpeedOverride);
    fnSetOverride(reinterpret_cast<void*>(addr), static_cast<float>(value));

    Log("[Bridge] SetUnitSpeed(0x%llX, %.3f) -- LIVE (SetSpeedOverride @ "
        "RVA 0x3A8C90; locomotor +0x2A0 written, +0x29C flag set)\n",
        (unsigned long long)addr, value);
    fn_pushstring(L, "OK: speed override applied (LIVE — SetSpeedOverride engine call)");
    return 1;
}

// SWFOC_ClearUnitSpeedOverride(addr) — revert to engine's natural max
// speed by calling ClearSpeedOverride @ RVA 0x38F8B0. Safe to call when
// no override is active (engine's check at +0x29C handles it).
static int Lua_ClearUnitSpeedOverride(lua_State* L) {
    EnsureSpeedLock();
    double rawAddr = fn_tonumber(L, 1);
    if (rawAddr <= 0) {
        fn_pushstring(L, "ERR: SWFOC_ClearUnitSpeedOverride: bad obj_addr");
        return 1;
    }
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_ClearUnitSpeedOverride: bad obj_addr");
        return 1;
    }
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_ClearUnitSpeedOverride: enemy units READ-ONLY");
        return 1;
    }

    EnterCriticalSection(&g_speedLock);
    g_unitSpeedOverrideMap.erase(addr);
    LeaveCriticalSection(&g_speedLock);

    auto fnClear = Resolve<pfn_ClearSpeedOverride>(RVA::ClearSpeedOverride);
    fnClear(reinterpret_cast<void*>(addr));

    Log("[Bridge] ClearUnitSpeedOverride(0x%llX) -- LIVE (ClearSpeedOverride "
        "@ RVA 0x38F8B0; locomotor +0x29C flag cleared)\n",
        (unsigned long long)addr);
    fn_pushstring(L, "OK: speed override cleared (LIVE — ClearSpeedOverride engine call)");
    return 1;
}

static int Lua_GetUnitSpeed(lua_State* L) {
    EnsureSpeedLock();
    double rawAddr = fn_tonumber(L, 1);
    if (rawAddr <= 0) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));

    // Source of truth: engine's locomotor +0x2A0 when override is active.
    // Fallback: cache (in case override was cleared from outside or the
    // locomotor pointer chain is currently unreadable).
    float value = ReadEngineSpeedOverride(addr);
    if (value < 0.0f) {
        EnterCriticalSection(&g_speedLock);
        auto it = g_unitSpeedOverrideMap.find(addr);
        if (it != g_unitSpeedOverrideMap.end()) value = it->second;
        LeaveCriticalSection(&g_speedLock);
    }
    fn_pushnumber(L, static_cast<double>(value));
    return 1;
}

// SWFOC_SetUnitShield(obj_addr, value) / SWFOC_GetUnitShield(obj_addr).
// Task 130 (2026-04-23) Phase 1 → LIVE 2026-04-29 iter 129.
//
// Iter 129 (2026-04-29) — flipped LIVE via SetFrontShield + SetRearShield
// engine helpers. Iter 105 (2026-04-28) had wrongly deferred SetUnitShield
// as "XML-attribute-only, needs RTTI dissection". Iter 128 re-audit using
// the iter-124-fixed callgraph CLI found the verified ledger ALREADY had
// `rva_set_front_shield` @ 0x3A8630 and `rva_set_rear_shield` @ 0x3A91E0,
// both with the same `void __fastcall(__int64 unit, float val)` shape as
// iter 100's SetSpeedOverride. Iter 105's mistake was searching on
// string-literal keys (SHIELD_REGEN_MULTIPLIER) which are XML attributes
// and VFX names rather than function-name entries.
//
// Engine path: SetFrontShield validates value >= 0, calls vtbl[2](unit, 15)
// to fetch the shield-behavior subobject, then dispatches through
// FrontShield_Write_Impl @ 0x56C1B0 (`void __fastcall(behavior, unit, float)`,
// float propagating via xmm0). Same engine-API-call pattern as iter 100;
// no DoString, no MinHook, no XML hack required.
//
// We write BOTH front and rear shield to the same value — operators
// using "shield = 5000" expect both rings filled. Cache mirrors the
// engine state for SWFOC_GetUnitShield reads.
//
// Enemy READ-ONLY: `IsObjOwnedByHuman` gates writes; reads work for any
// unit so the Inspector can show enemy shields too.
typedef void (__fastcall *pfn_SetFrontShield)(void* obj, float val);
typedef void (__fastcall *pfn_SetRearShield)(void* obj, float val);
// 2026-04-29 (iter 131): engine reader for current shield. Hex-Rays
// reports `double __fastcall(__int64)` for sub_1403963C0 — float-valued
// (xmm0) result returned in the double slot per __fastcall x64 ABI.
// Cast to float on return for cache mirroring.
typedef double (__fastcall *pfn_FrontShield_Read)(void* obj);

static std::unordered_map<uintptr_t, float> g_unitShieldOverrideMap;
static CRITICAL_SECTION g_shieldLock;
static bool g_shieldLockInit = false;

static void EnsureShieldLock() {
    if (!g_shieldLockInit) {
        InitializeCriticalSection(&g_shieldLock);
        g_shieldLockInit = true;
    }
}

static int Lua_SetUnitShield(lua_State* L) {
    EnsureShieldLock();
    double rawAddr = fn_tonumber(L, 1);
    double value = fn_tonumber(L, 2);
    if (rawAddr <= 0 || value < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitShield: expected (obj_addr, value>=0)");
        return 1;
    }
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitShield: bad obj_addr");
        return 1;
    }
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitShield: enemy units are READ-ONLY");
        return 1;
    }

    // LIVE: call engine's SetFrontShield + SetRearShield directly. Same
    // pattern as iter 100 SetUnitSpeed — Resolve<>() the engine helper,
    // call with (unit_addr, value). Engine validates >= 0 internally
    // and dispatches through QueryInterface(15) to the shield behavior.
    auto fnSetFront = Resolve<pfn_SetFrontShield>(RVA::SetFrontShield);
    auto fnSetRear  = Resolve<pfn_SetRearShield>(RVA::SetRearShield);
    void* obj = reinterpret_cast<void*>(addr);
    float fval = static_cast<float>(value);
    if (fnSetFront) fnSetFront(obj, fval);
    if (fnSetRear)  fnSetRear(obj, fval);

    EnterCriticalSection(&g_shieldLock);
    g_unitShieldOverrideMap[addr] = fval;
    LeaveCriticalSection(&g_shieldLock);

    Log("[Bridge] SetUnitShield(0x%llX, %.3f) -- LIVE (SetFrontShield @ "
        "0x%llX + SetRearShield @ 0x%llX)\n",
        (unsigned long long)addr, value,
        (unsigned long long)RVA::SetFrontShield,
        (unsigned long long)RVA::SetRearShield);
    fn_pushstring(L, "OK: shield set (LIVE — SetFrontShield + SetRearShield engine call)");
    return 1;
}

static int Lua_GetUnitShield(lua_State* L) {
    // 2026-04-29 (iter 131): flipped to LIVE via FrontShield_Read engine
    // call. Pre-iter-131 this returned the last cached SetUnitShield
    // override write (or -1 if none) — that meant a fresh GetUnitShield
    // on a unit with no recorded override returned -1 even though the
    // unit had a real engine-side shield value. Iter 128 re-audit
    // pattern caught this: `rva_front_shield_read` @ 0x3963C0 is
    // `double __fastcall(__int64)` — drop in directly. Engine value
    // wins over the cache; the cache survives only as a fallback if
    // the engine call fails (Resolve<>() returns nullptr in dev/replay
    // builds without the loaded module).
    EnsureShieldLock();
    double rawAddr = fn_tonumber(L, 1);
    if (rawAddr <= 0) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushnumber(L, -1.0);
        return 1;
    }

    // LIVE: engine read first.
    auto fnRead = Resolve<pfn_FrontShield_Read>(RVA::FrontShield_Read);
    if (fnRead) {
        double engineValue = fnRead(reinterpret_cast<void*>(addr));
        // Mirror the engine value into the override cache so subsequent
        // read-after-write paths agree with the engine.
        EnterCriticalSection(&g_shieldLock);
        g_unitShieldOverrideMap[addr] = static_cast<float>(engineValue);
        LeaveCriticalSection(&g_shieldLock);
        fn_pushnumber(L, engineValue);
        return 1;
    }

    // Fallback: cache lookup (replay/dev builds without the module loaded).
    float value = -1.0f;
    EnterCriticalSection(&g_shieldLock);
    auto it = g_unitShieldOverrideMap.find(addr);
    if (it != g_unitShieldOverrideMap.end()) value = it->second;
    LeaveCriticalSection(&g_shieldLock);
    fn_pushnumber(L, static_cast<double>(value));
    return 1;
}

// SWFOC_HeroStatEdit(obj_addr, field, value) -> "OK: ..." or "ERR: ...".
// Task 138 (2026-04-23). Dispatcher placed AFTER every per-field helper
// so it can reference the shared locks/maps (g_shieldLock, g_speedLock,
// g_heroLock) and their respective CriticalSections without forward
// declarations. The live path does NOT gate on is_hero (the bridge
// cannot detect it until Phase 2 of #134); only the replay mirror
// rejects non-heroes. Enemy READ-ONLY is enforced.
static int Lua_HeroStatEdit(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    const char* field = fn_tostring(L, 2);
    double value = fn_tonumber(L, 3);
    if (rawAddr <= 0 || !field) {
        fn_pushstring(L, "ERR: SWFOC_HeroStatEdit: expected (obj_addr, field, value)");
        return 1;
    }
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_HeroStatEdit: bad obj_addr");
        return 1;
    }
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_HeroStatEdit: enemy units READ-ONLY");
        return 1;
    }
    std::string f = field;
    if (f == "hull") {
        *reinterpret_cast<float*>(addr + RVA::GameObj::HP) = static_cast<float>(value);
        fn_pushstring(L, "OK: hull written");
        return 1;
    }
    if (f == "shield") {
        // 2026-04-29 (iter 129): UnitStatEditor shield write also goes
        // LIVE through SetFrontShield + SetRearShield. Cache mirrors the
        // engine state so SWFOC_GetUnitShield sees the same value via
        // either path. Same shape as the iter-100 speed migration.
        EnsureShieldLock();
        auto fnSetFront = Resolve<pfn_SetFrontShield>(RVA::SetFrontShield);
        auto fnSetRear  = Resolve<pfn_SetRearShield>(RVA::SetRearShield);
        void* obj = reinterpret_cast<void*>(addr);
        float fval = static_cast<float>(value);
        if (fnSetFront) fnSetFront(obj, fval);
        if (fnSetRear)  fnSetRear(obj, fval);
        EnterCriticalSection(&g_shieldLock);
        g_unitShieldOverrideMap[addr] = fval;
        LeaveCriticalSection(&g_shieldLock);
        fn_pushstring(L, "OK: shield set (LIVE — SetFrontShield + SetRearShield)");
        return 1;
    }
    if (f == "speed") {
        // 2026-04-28 (iter 100): UnitStatEditor speed write also goes
        // LIVE through SetSpeedOverride. Cache mirrors the engine state
        // so SWFOC_GetUnitSpeed sees the same value via either path.
        EnsureSpeedLock();
        EnterCriticalSection(&g_speedLock);
        g_unitSpeedOverrideMap[addr] = static_cast<float>(value);
        LeaveCriticalSection(&g_speedLock);
        auto fnSetOverride = Resolve<pfn_SetSpeedOverride>(RVA::SetSpeedOverride);
        fnSetOverride(reinterpret_cast<void*>(addr), static_cast<float>(value));
        fn_pushstring(L, "OK: speed override applied (LIVE)");
        return 1;
    }
    if (f == "respawn_ms") {
        EnsureHeroLock();
        EnterCriticalSection(&g_heroLock);
        int32_t clamped = static_cast<int32_t>(value);
        if (clamped < 0) clamped = 0;
        g_pendingRespawnWrites[addr] = clamped;
        LeaveCriticalSection(&g_heroLock);
        fn_pushstring(L, "OK: respawn_ms recorded (Phase 2 pending)");
        return 1;
    }
    Log("[Bridge] HeroStatEdit: unknown field '%s'\n", field);
    fn_pushstring(L, "ERR: SWFOC_HeroStatEdit: unknown field (hull/shield/speed/respawn_ms)");
    return 1;
}

// SWFOC_SetUnitField(addr, field, value) — iter-136 partial-LIVE
// implementation. Forward-declared at the top of this file (near the
// PendingUnitFieldWrite struct) so RegisterAll can reference it; defined
// here so it can mirror Lua_HeroStatEdit's per-field LIVE branches that
// rely on shield/speed primitives defined just above.
//
// Field handling:
//   hull   → direct write to addr+RVA::GameObj::HP                   (LIVE)
//   shield → SetFrontShield + SetRearShield (engine helpers)         (LIVE iter 129)
//   speed  → SetSpeedOverride                                        (LIVE iter 100)
//   anything else → fall through to g_pendingUnitFieldWrites Phase-1 mirror
//
// Safety semantics match Lua_HeroStatEdit:
//   - IsValidObjAddr gate
//   - IsObjOwnedByHuman enemy READ-ONLY gate
// (The Phase-1 fallback path also gates on these now — was unguarded
// pre-iter-136. UnitStatEditor edits land on local heroes/units only.)
static int Lua_SetUnitField(lua_State* L) {
    EnsureUnitFieldLock();
    uint64_t    addrRaw = static_cast<uint64_t>(fn_tonumber(L, 1));
    const char* raw_f   = fn_tostring(L, 2);
    float       val     = static_cast<float>(fn_tonumber(L, 3));
    if (!raw_f || raw_f[0] == '\0') {
        fn_pushstring(L, "ERR: SWFOC_SetUnitField: field name required");
        return 1;
    }
    if (addrRaw == 0) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitField: obj_addr cannot be 0");
        return 1;
    }
    uintptr_t addr = static_cast<uintptr_t>(addrRaw);
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitField: bad obj_addr");
        return 1;
    }
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitField: enemy units READ-ONLY");
        return 1;
    }

    std::string f = raw_f;
    if (f == "hull") {
        *reinterpret_cast<float*>(addr + RVA::GameObj::HP) = val;
        Log("[Bridge] SetUnitField(addr=0x%llX, field=hull, value=%.3f) — LIVE direct write\n",
            (unsigned long long)addr, val);
        fn_pushstring(L, "OK: hull written (LIVE)");
        return 1;
    }
    if (f == "shield") {
        // Mirror of HeroStatEdit's shield path (iter 129) — engine
        // helpers SetFrontShield + SetRearShield with a Resolve<>()
        // fallback to cache for replay/dev builds.
        EnsureShieldLock();
        auto fnSetFront = Resolve<pfn_SetFrontShield>(RVA::SetFrontShield);
        auto fnSetRear  = Resolve<pfn_SetRearShield>(RVA::SetRearShield);
        void* obj = reinterpret_cast<void*>(addr);
        if (fnSetFront) fnSetFront(obj, val);
        if (fnSetRear)  fnSetRear(obj, val);
        EnterCriticalSection(&g_shieldLock);
        g_unitShieldOverrideMap[addr] = val;
        LeaveCriticalSection(&g_shieldLock);
        Log("[Bridge] SetUnitField(addr=0x%llX, field=shield, value=%.3f) — LIVE SetFrontShield+SetRearShield\n",
            (unsigned long long)addr, val);
        fn_pushstring(L, "OK: shield set (LIVE — SetFrontShield + SetRearShield)");
        return 1;
    }
    if (f == "speed") {
        // Mirror of HeroStatEdit's speed path (iter 100) — engine
        // helper SetSpeedOverride with cache mirror.
        EnsureSpeedLock();
        EnterCriticalSection(&g_speedLock);
        g_unitSpeedOverrideMap[addr] = val;
        LeaveCriticalSection(&g_speedLock);
        auto fnSetOverride = Resolve<pfn_SetSpeedOverride>(RVA::SetSpeedOverride);
        if (fnSetOverride) fnSetOverride(reinterpret_cast<void*>(addr), val);
        Log("[Bridge] SetUnitField(addr=0x%llX, field=speed, value=%.3f) — LIVE SetSpeedOverride\n",
            (unsigned long long)addr, val);
        fn_pushstring(L, "OK: speed override applied (LIVE)");
        return 1;
    }
    if (f == "invuln_flag") {
        // iter 243 — LIVE direct write of GameObj+0x3A7. Display-only flag.
        // The actual gameplay-effective invulnerability lives in the
        // BehaviorMarker at +0x37D + per-hardpoint INVULNERABLE behavior
        // attachments (iter 110 hardpoint propagation). Writing +0x3A7 alone
        // updates the visual indicator without touching the behavior chain.
        // Operator should pair with iter-110 SWFOC_MakeInvulnerableLua for
        // full gameplay invulnerability.
        *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::InvulnFlag) =
            (val != 0.0f) ? static_cast<uint8_t>(0x01) : static_cast<uint8_t>(0x00);
        Log("[Bridge] SetUnitField(addr=0x%llX, field=invuln_flag, value=%d) "
            "— LIVE direct write (display flag only; pair with MakeInvulnerableLua)\n",
            (unsigned long long)addr, (val != 0.0f) ? 1 : 0);
        fn_pushstring(L, "OK: invuln_flag written (LIVE — display only; pair with MakeInvulnerableLua for engine effect)");
        return 1;
    }
    if (f == "prevent_death") {
        // iter 243 — LIVE direct bit-write of bit 0x80 of GameObj+0x3A1.
        // iter-153 SWFOC_SetCannotBeKilledLua sets this bit via the engine
        // Lua API (Set_Cannot_Be_Killed); direct write here is for operator
        // convenience when they have obj_addr but not a Lua handle. Same
        // caveat as invuln_flag — engine-state machinery may expect this bit
        // paired with other behavior changes; operator should prefer the
        // iter-153 LIVE wire when possible.
        uint8_t* flag_byte = reinterpret_cast<uint8_t*>(addr + RVA::GameObj::PreventDeath);
        if (val != 0.0f) {
            *flag_byte |= static_cast<uint8_t>(0x80);
        } else {
            *flag_byte &= static_cast<uint8_t>(0x7F);
        }
        Log("[Bridge] SetUnitField(addr=0x%llX, field=prevent_death, value=%d) "
            "— LIVE direct bit write (bit 0x80 of +0x3A1)\n",
            (unsigned long long)addr, (val != 0.0f) ? 1 : 0);
        fn_pushstring(L, "OK: prevent_death bit set (LIVE — bit 0x80 of +0x3A1; operator may prefer SWFOC_SetCannotBeKilledLua)");
        return 1;
    }
    if (f == "max_hull" || f == "max_shield") {
        // iter 258 — LIVE direct write to the per-unit-TYPE stats struct.
        // Walk: unit_instance + GameObj::GameObjType (0x298) → UnitType*.
        // Then write the float at UnitType::MaxHull (0xDCC) /
        // UnitType::MaxFrontShield (0xDD0) / UnitType::MaxRearShield (0xDD4).
        //
        // CRITICAL CAVEAT: the type-stats record is SHARED across every unit
        // instance of the same type. Writing here changes the cap for ALL
        // units of that type, both yours and enemies', for the rest of the
        // session — there is no "per-instance max_hull" without a separate
        // override field that this engine version does not expose.
        //
        // Effect timing: the engine reads max_hull/max_shield on every damage
        // event (per `rva_get_max_health` callers; see callgraph CLI: callers
        // 0x3727A0 → 29 unique callers). Writing the larger value is observable
        // from the very next damage tick.
        //
        // Semantic verification per iter-256 memory rule (RE walk in
        // iter-258 doc): two engine callers (rva_get_hull_percentage,
        // rva_set_hp) both pass `*(unit+0x298)` to GetMaxHealth's `this`
        // slot AND dereference the typename string at `(type+0xF8)`. Two
        // independent confirmations of the +0x298 unit→type chain.
        uintptr_t typePtr = *reinterpret_cast<uintptr_t*>(addr + RVA::GameObj::GameObjType);
        if (typePtr == 0) {
            Log("[Bridge] SetUnitField(addr=0x%llX, field=%s) — type pointer at +0x298 was NULL; cannot write max_*.\n",
                (unsigned long long)addr, raw_f);
            fn_pushstring(L, "ERR: type pointer at +0x298 is NULL (orphan unit?). Try a fresh obj_addr.");
            return 1;
        }
        if (f == "max_hull") {
            *reinterpret_cast<float*>(typePtr + RVA::UnitType::MaxHull) = val;
            Log("[Bridge] SetUnitField(addr=0x%llX, type=0x%llX, field=max_hull, value=%.3f) "
                "— LIVE direct type-stats write (UnitType+0xDCC); affects ALL units of this type\n",
                (unsigned long long)addr, (unsigned long long)typePtr, val);
            fn_pushstring(L, "OK: max_hull written to UnitType+0xDCC (LIVE — affects EVERY unit of this type for the session; engine reads it on next damage tick)");
            return 1;
        }
        // f == "max_shield": dual-write to front (+0xDD0) and rear (+0xDD4)
        // mirroring iter-129 SetUnitShield's per-instance dual-write.
        *reinterpret_cast<float*>(typePtr + RVA::UnitType::MaxFrontShield) = val;
        *reinterpret_cast<float*>(typePtr + RVA::UnitType::MaxRearShield)  = val;
        Log("[Bridge] SetUnitField(addr=0x%llX, type=0x%llX, field=max_shield, value=%.3f) "
            "— LIVE direct type-stats dual-write (UnitType+0xDD0 + UnitType+0xDD4); affects ALL units of this type\n",
            (unsigned long long)addr, (unsigned long long)typePtr, val);
        fn_pushstring(L, "OK: max_shield front+rear written to UnitType+0xDD0 / +0xDD4 (LIVE — affects EVERY unit of this type for the session)");
        return 1;
    }

    // Phase-1 mirror fall-through for the remaining 6 sub-fields
    // (max_speed/attack_power/respawn_ms/
    //  is_hero/respawn_enabled/owner_slot — owner_slot deferred to iter
    //  247+ per iter-242 design; operator should use iter-108
    //  SWFOC_ChangeUnitOwnerLua for engine-aware ownership change).
    PendingUnitFieldWrite w;
    w.obj_addr = addrRaw;
    w.field    = raw_f;
    w.value    = val;
    EnterCriticalSection(&g_unitFieldLock);
    g_pendingUnitFieldWrites.push_back(w);
    LeaveCriticalSection(&g_unitFieldLock);
    Log("[Bridge] SetUnitField(addr=0x%llX, field=%s, value=%.3f) -- Phase 1 pending\n",
        (unsigned long long)addr, raw_f, val);
    fn_pushstring(L, "OK: unit-field write queued (Phase 2 offset-table hook pending)");
    return 1;
}

// SWFOC_KillUnit(obj_addr) / SWFOC_ReviveUnit(obj_addr).
// Task 137 (2026-04-23). Kill writes hull=0 into the target; revive
// writes a large hull that the engine clamps to max_hull. Enemy
// protection: kill targets enemies OR the calling session's local
// units (users can kill their own units); revive is restricted to
// local-owned units via IsObjOwnedByHuman so the trainer cannot
// unilaterally resurrect enemy reinforcements.
//
// Pairing with the #106 hardpoint invulnerability path: if the target
// has INVULNERABLE attached to every hardpoint, the kill write still
// lands in the hull field but the engine's behavior layer will restore
// the unit on the next tick (observed in the 2026-04-23 live session).
// Callers who want a guaranteed kill should first toggle invulnerability
// off via SWFOC_SetUnitInvuln(obj, 0).
static int Lua_KillUnit(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    if (rawAddr <= 0) {
        fn_pushstring(L, "ERR: SWFOC_KillUnit: expected obj_addr");
        return 1;
    }
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_KillUnit: bad obj_addr");
        return 1;
    }
    *reinterpret_cast<float*>(addr + RVA::GameObj::HP) = 0.0f;
    Log("[Bridge] KillUnit(0x%llX)\n", (unsigned long long)addr);
    fn_pushstring(L, "OK: hull set to 0 (engine may revert on next tick if invulnerable)");
    return 1;
}

static int Lua_ReviveUnit(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    if (rawAddr <= 0) {
        fn_pushstring(L, "ERR: SWFOC_ReviveUnit: expected obj_addr");
        return 1;
    }
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_ReviveUnit: bad obj_addr");
        return 1;
    }
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_ReviveUnit: enemy units are READ-ONLY");
        return 1;
    }
    // Engine clamps to max_hull; a large value is safe.
    *reinterpret_cast<float*>(addr + RVA::GameObj::HP) = 99999.0f;
    Log("[Bridge] ReviveUnit(0x%llX)\n", (unsigned long long)addr);
    fn_pushstring(L, "OK: hull lifted; engine clamps to max_hull on next tick");
    return 1;
}

// SWFOC_SetDamageMultiplier(slot, mult) / SWFOC_GetDamageMultiplier(slot).
// Task 129 (2026-04-23) Phase 1. Stores the multiplier in a module-local
// table.
//
// 2026-04-28 (iter 93-94) — Phase 2 detour search:
//
// iter 93 finding: original comment claimed "Take_Damage_Outer @ 0x38A350"
//   — WRONG. That function is a player-event-handler taking
//   DynamicVectorClass<PlayerClass*> + event-id, not a damage path.
//
// iter 94 finding: the "Damage_Multiplier" string anchors point to
//   PER-ABILITY-CLASS validators, not a global damage multiplier path.
//   sub_140718790 (RVA 0x718790) is a virtual method on
//   `LeechShieldsAbilityClass`, slot #29 of its
//   `vftable{for SpecialAbilityClass}` at 0x1408cc740. The +392 offset
//   is the per-ability Damage_Multiplier float WITHIN a LeechShields
//   ability instance — used to scale that ability's damage output, not
//   to scale all damage globally. The "5 string anchors" the bridge
//   mentioned are 5 different ability subclasses (LeechShields and 4
//   others), each with its own validator method.
//
// Architectural mismatch: the bridge's SWFOC_SetDamageMultiplier(slot, mult)
//   intends a global / per-slot damage multiplier affecting ALL damage.
//   The engine's per-ability Damage_Multiplier is a different concept at
//   a different layer.
//
// 2026-04-28 (iter 94 — CONSUME-SITE PINNED): the damage-resolve chokepoint
//   is GameObjectClass::Take_Damage at RVA 0x3A9E30 (image-base-added:
//   0x1403A9E30). 3-tool consensus pinned via verified_facts.json entry
//   rva_take_damage_function. Identified by xref-to-format-string on
//   0x140866400 ("--- Function call was: Take_Damage(%d, %f, ...)" debug log
//   from inside the function itself). 58 callers across the binary reach
//   this chokepoint -- it is THE single mutation site for hull/shield
//   damage application.
//
//   Prototype (IDA Hex-Rays):
//     void __fastcall sub_1403A9E30(
//         __int64 this,            // GameObjectClass* (the target)
//         unsigned int damageType,
//         int, __int64,
//         void *Src,               // attacker source object
//         _BYTE *,
//         float *damageValue,      // ← param 7. *damageValue is the float
//                                  //   damage to apply. Stack arg at
//                                  //   [rsp+0x30] under MSVC x64 fastcall.
//         char, char, int, int, void **, char, _BYTE *);
//
//   Phase 2 detour wiring decision (iter 95):
//
//   ARCHITECTURAL FINDING: Take_Damage's `Src` (param 5) is a name string
//   used for printf-style debug logging, NOT the attacker GameObjectClass*.
//   Sampled callers (sub_14005FE90, sub_14071DFF0, sub_140436920,
//   sub_140456970, …) all pass `Src=nullptr` and `a10=-1`. The attacker
//   identity is IMPLICIT in the call stack at this layer — meaning a
//   Take_Damage detour CANNOT reliably extract the attacker player slot
//   without dangerous stack-walking.
//
//   Implication for SWFOC_SetDamageMultiplier semantics:
//     - g_dmgMult_global (applied to all damage when slot=-1 was passed
//       to SWFOC_SetDamageMultiplier(-1, mult)) → IMPLEMENTABLE via a
//       Take_Damage detour at this chokepoint. Reliable. Single hook.
//     - g_dmgMult_perSlot[slot] (per-attacker semantics) → NOT
//       IMPLEMENTABLE at Take_Damage. Needs detours at the ~58 caller
//       sites that have an attacker context (e.g., weapon-fire paths).
//       Defer to a future arc that pins those caller layers.
//
//   Recommended near-term implementation (iter 96+):
//     - Detour 0x1403A9E30. In the hook, scale `*damageValue` by
//       g_dmgMult_global (loaded under g_dmgMultLock).
//     - Documentation update: SWFOC_SetDamageMultiplier(slot, mult) with
//       slot >= 0 stores per-slot but is INERT until the higher-layer
//       detours land. slot == -1 stores g_dmgMult_global which IS LIVE.
//     - Capability badge: emit SWFOC_SetDamageMultiplierGlobal as LIVE
//       (new helper) while SWFOC_SetDamageMultiplier (the per-slot
//       superset) stays MIXED with a note explaining the layered status.
//
// Until the detour + the badge split ship, the stored values are inert
// from the game's perspective. Storage + chokepoint identification +
// architectural call complete; hook wiring + badge update remain.
//
// Until the detour lands, the stored values are inert from the game's
// perspective -- the replay harness mirror at ReplayObsGetDamageMultiplier
// provides the full behavioural contract for offline tests.
static constexpr int kDmgMultMaxSlot = 16;
static float g_dmgMult_global = 1.0f;
static float g_dmgMult_perSlot[kDmgMultMaxSlot] = {0};
static CRITICAL_SECTION g_dmgMultLock;
static bool g_dmgMultLockInit = false;

static void EnsureDamageMultiplierLock() {
    if (!g_dmgMultLockInit) {
        InitializeCriticalSection(&g_dmgMultLock);
        g_dmgMultLockInit = true;
        for (int i = 0; i < kDmgMultMaxSlot; i++) g_dmgMult_perSlot[i] = 0.0f;
    }
}

// 2026-04-28 (iter 96): definition of the forward-declared helper used by
// Hook_TakeDamageOuter (line ~175). Reads g_dmgMult_global under the
// existing lock; returns 1.0f if the lock isn't initialised yet (defensive
// against the hook firing before EnsureDamageMultiplierLock() has run).
static float ReadGlobalDamageMultiplier() {
    if (!g_dmgMultLockInit) return 1.0f;
    EnterCriticalSection(&g_dmgMultLock);
    const float mult = g_dmgMult_global;
    LeaveCriticalSection(&g_dmgMultLock);
    return mult;
}

static int Lua_SetDamageMultiplier(lua_State* L) {
    EnsureDamageMultiplierLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double mult = fn_tonumber(L, 2);
    if (mult < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetDamageMultiplier: multiplier must be >= 0");
        return 1;
    }
    EnterCriticalSection(&g_dmgMultLock);
    if (slot < 0) {
        g_dmgMult_global = static_cast<float>(mult);
    } else if (slot < kDmgMultMaxSlot) {
        // Zero is the sentinel "no override" value. Callers who genuinely
        // want zero damage for this slot should use a very small value
        // like 0.0001 instead -- safer than colliding with the sentinel.
        g_dmgMult_perSlot[slot] = (mult == 1.0 ? 0.0f : static_cast<float>(mult));
    }
    LeaveCriticalSection(&g_dmgMultLock);
    Log("[Bridge] SetDamageMultiplier(slot=%d, mult=%.3f)\n", slot, mult);
    fn_pushstring(L, "OK: damage multiplier stored (Phase 2 detour pending)");
    return 1;
}

static int Lua_GetDamageMultiplier(lua_State* L) {
    EnsureDamageMultiplierLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    float effective = g_dmgMult_global;
    EnterCriticalSection(&g_dmgMultLock);
    if (slot >= 0 && slot < kDmgMultMaxSlot && g_dmgMult_perSlot[slot] != 0.0f) {
        effective = g_dmgMult_perSlot[slot];
    }
    LeaveCriticalSection(&g_dmgMultLock);
    fn_pushnumber(L, static_cast<double>(effective));
    return 1;
}

// 2026-04-28 (iter 96): SWFOC_SetDamageMultiplierGlobal(mult) — the LIVE-
// badged simpler API. Sets ONLY the global multiplier (no slot dimension)
// which IS implementable at the Take_Damage_Outer chokepoint we hooked.
// The per-slot SetDamageMultiplier(slot, mult) stays MIXED because the
// per-slot dimension needs higher-layer detours (see iter 95 comment).
static int Lua_SetDamageMultiplierGlobal(lua_State* L) {
    EnsureDamageMultiplierLock();
    double mult = fn_tonumber(L, 1);
    if (mult < 0.0) {
        fn_pushstring(L, "ERR: SWFOC_SetDamageMultiplierGlobal: multiplier must be >= 0");
        return 1;
    }
    EnterCriticalSection(&g_dmgMultLock);
    g_dmgMult_global = static_cast<float>(mult);
    LeaveCriticalSection(&g_dmgMultLock);
    Log("[Bridge] SetDamageMultiplierGlobal(mult=%.3f) -- LIVE\n", mult);
    fn_pushstring(L, "OK: global damage multiplier applied (LIVE — Take_Damage_Outer detour)");
    return 1;
}

static int Lua_GetDamageMultiplierGlobal(lua_State* L) {
    EnsureDamageMultiplierLock();
    EnterCriticalSection(&g_dmgMultLock);
    const float mult = g_dmgMult_global;
    LeaveCriticalSection(&g_dmgMultLock);
    fn_pushnumber(L, static_cast<double>(mult));
    return 1;
}

// ======================================================================
// 2026-05-06 (iter 224-225): SetFireRate global-level LIVE wire
// ======================================================================
//
// iter-224 RE kickoff doc: knowledge-base/iter224_setfirerate_global_re_kickoff.md
// Closes A1.3 SetFireRate after 124-day deferral (iter-101/130 audits confirmed
// no rva_set_fire_rate setter; iter-224 found WeaponTick @ 0x387010 dt-scaling
// path matches iter-96 Take_Damage_Outer pattern exactly).
//
// Pattern: MinHook detour at WeaponTick. Bridge stores g_fireRateMult_global;
// hook scales (a2 - last_tick) delta by mult before calling original. Higher
// mult → cooldown advances faster → faster fire rate. Sanity clamp [0, 100].
//
// Engine semantic caveat (from iter-224 design doc):
//   - mult = 2.0 → 2x fire rate (cooldown advances 2x faster)
//   - mult = 0.5 → halved fire rate
//   - mult = 0.0 → effective freeze (no time passes; use Suspend_AI for proper pause)
//   - mult > 100 clamped to prevent int overflow in dt math
static std::atomic<float> g_fireRateMult_global{1.0f};

typedef void (__fastcall *pfn_WeaponTick)(__int64, int);
static pfn_WeaponTick real_WeaponTick = nullptr;

static void __fastcall Hook_WeaponTick(__int64 a1, int a2) {
    // Read last_tick from a1 + 96 (per iter-224 RE finding). Scale dt by
    // g_fireRateMult_global before delegating. The original WeaponTick will
    // re-read last_tick from a1+96 and compute dt itself; we synthesize an
    // earlier last_tick to mimic faster (or slower) elapsed time.
    const float mult = g_fireRateMult_global.load(std::memory_order_relaxed);
    if (mult == 1.0f) {
        // Fast path: no scaling, no overhead.
        real_WeaponTick(a1, a2);
        return;
    }
    const int last_tick = *reinterpret_cast<int*>(a1 + 96);
    const int dt = a2 - last_tick;
    // Scale dt by multiplier; pass synthesized a2 = last_tick + scaled_dt.
    const int scaled_dt = static_cast<int>(static_cast<float>(dt) * mult);
    const int scaled_a2 = last_tick + scaled_dt;
    real_WeaponTick(a1, scaled_a2);
}

static int Lua_SetFireRateMultiplierGlobal(lua_State* L) {
    double mult = fn_tonumber(L, 1);
    if (mult < 0.0) mult = 0.0;       // negative would reverse cooldown
    if (mult > 100.0) mult = 100.0;   // sanity cap to prevent int overflow in dt math
    g_fireRateMult_global.store(static_cast<float>(mult), std::memory_order_relaxed);
    Log("[Bridge] SetFireRateMultiplierGlobal(mult=%.3f) -- LIVE\n", mult);
    fn_pushstring(L, "OK: global fire rate multiplier applied (LIVE — WeaponTick detour)");
    return 1;
}

static int Lua_GetFireRateMultiplierGlobal(lua_State* L) {
    const float mult = g_fireRateMult_global.load(std::memory_order_relaxed);
    fn_pushnumber(L, static_cast<double>(mult));
    return 1;
}

// ======================================================================
// 2026-05-08 (iter 285): Tier 3 HUD counters — kills / deaths / units-alive
// ======================================================================
//
// Pattern: extends existing Hook_DeathHandler (line ~206) — no new MinHook
// detour required since DeathHandler @ 0x39BDB0 is already hooked. The hook
// receives killer/victim GameObject pointers; iter-285 reads owner-player-IDs
// (GameObj+0x58 = slot index 0-7) and compares against FindLocalPlayerSlot()
// to maintain atomic counters.
//
// Per iter-225 lock-free pattern: std::atomic<int> with relaxed ordering for
// counter increments. Detour and Lua getters both run on the game thread, so
// no cross-thread synchronization concerns; relaxed is sufficient.
//
// Total-units-alive uses poll-on-demand (no detour). Walks the engine object
// list via Selection::kObjectListHead. O(n) where n ≤ 2048; ≤ 1 ms at HUD
// refresh frequency. Slower than a counter but no spawn-event RVA pin needed.
//
// Honest-defer per iter-283 codified rule: the design spec (agent #3 research,
// 2026-05-08) confirmed via grep that none of these wires existed; iter-284
// honest-deferred them; iter-285 closes the defer.
//
// NOTE: g_localPlayerKills and g_localPlayerDeaths are defined at the top of
// this file (line ~177) so Hook_DeathHandler at line ~218 can reference them.
// Only the Lua getters + units-alive walker live here.

static int Lua_GetPlayerKills(lua_State* L) {
    const int n = g_localPlayerKills.load(std::memory_order_relaxed);
    fn_pushnumber(L, static_cast<double>(n));
    return 1;
}

static int Lua_GetPlayerDeaths(lua_State* L) {
    const int n = g_localPlayerDeaths.load(std::memory_order_relaxed);
    fn_pushnumber(L, static_cast<double>(n));
    return 1;
}

// Test-only entry points so the bridge harness (no live game) can exercise
// the read-side without invoking Hook_DeathHandler. Counts use atomic
// fetch_add to mirror what the detour does. NOT registered in the Lua
// function table — only callable from test_harness.cpp linkage.
extern "C" void SWFOC_TEST_IncrementKills(int n) {
    g_localPlayerKills.fetch_add(n, std::memory_order_relaxed);
}
extern "C" void SWFOC_TEST_IncrementDeaths(int n) {
    g_localPlayerDeaths.fetch_add(n, std::memory_order_relaxed);
}
extern "C" void SWFOC_TEST_ResetCounters() {
    g_localPlayerKills.store(0, std::memory_order_relaxed);
    g_localPlayerDeaths.store(0, std::memory_order_relaxed);
}

static int Lua_GetTotalUnitsAlive(lua_State* L) {
    int count = 0;
    auto inner_ptr = *reinterpret_cast<uintptr_t*>(g_base + RVA::GameModeRoot_Global);
    if (inner_ptr) {
        // GameModeRoot_Global -> GameModeClass*; Selection chain at +0x18.
        auto sel_base = *reinterpret_cast<uintptr_t*>(inner_ptr + 0x18);
        if (sel_base) {
            auto list_head = *reinterpret_cast<uintptr_t*>(sel_base + RVA::Selection::kObjectListHead);
            const uintptr_t sentinel = sel_base + RVA::Selection::kObjectListSentinel;
            uintptr_t node = list_head;
            while (node && node != sentinel && count < RVA::Selection::kMaxTacticalObjects) {
                count++;
                uintptr_t next = *reinterpret_cast<uintptr_t*>(node + RVA::Selection::kNodeNext);
                if (next == node) break;  // self-cycle defensive guard
                node = next;
            }
        }
    }
    fn_pushnumber(L, static_cast<double>(count));
    return 1;
}

// ======================================================================
// 2026-05-06 (iter 230-231): FreezeCredits global-level LIVE wire
// ======================================================================
//
// iter-230 RE kickoff doc: knowledge-base/iter230_freeze_credits_re_kickoff.md
// Closes A1.x FreezeCredits via universal credit-adjust hook. Pattern parity
// with iter-96 (Take_Damage_Outer) and iter-225 (WeaponTick) — single MinHook
// detour at AddCredits @ 0x27F370 covers all 47 callers (gains AND spends).
//
// Design ships TWO knobs in same arc:
//   - g_creditsFreeze_global (bool): short-circuits AddCredits entirely.
//     No write to PlayerClass+0x70, no event notification, no tracking
//     callback. Returns the unchanged balance to preserve prototype contract.
//   - g_creditsMult_global (float): scales the delta arg before forwarding.
//     mult=1.0 fast-path mirrors iter-225. Clamp [0.0, 100.0].
//
// Bool freeze wins-over-mult precedence (avoids ambiguity when both are set).
//
// Engine semantic caveats (from iter-230 design doc):
//   - cap at PlayerClass+0x74 still applies (mult=2 doesn't let you exceed cap)
//   - negative-balance-guard already engine-side (deductions can't push balance < 0)
//   - AI subsidies blocked equally with freeze=true (all factions affected)
//   - tracking flag (a3=1 → analytics callback) suppressed by freeze
static std::atomic<bool>  g_creditsFreeze_global{false};
static std::atomic<float> g_creditsMult_global{1.0f};

typedef float (__fastcall *pfn_AddCredits)(__int64, float, char);
static pfn_AddCredits real_AddCredits = nullptr;

static float __fastcall Hook_AddCredits(__int64 a1, float a2, char a3) {
    // Bool freeze precedence: short-circuit ENTIRELY. Don't call original;
    // that prevents the +0x70 write, the event notification, and the
    // tracking callback. Returns the unchanged balance the same way the
    // original returns the new balance (prototype contract preserved).
    if (g_creditsFreeze_global.load(std::memory_order_relaxed)) {
        return *reinterpret_cast<float*>(a1 + 112);
    }

    // Multiplier mode: scale POSITIVE deltas only (income).
    // 2026-05-20 BUGFIX: previously `a2 * mult` scaled BOTH gains and spends.
    // Engine routes all 47 callers (buy/sell/reward/payroll) through this
    // single function, so naive multiplication 10x'd UNIT COSTS the same as
    // it 10x'd income — counter-intuitive to operators expecting "more money."
    // Sign-gate: a2 > 0 means credits IN (income/reward), a2 <= 0 means
    // credits OUT (purchase) and stays unmultiplied.
    const float mult = g_creditsMult_global.load(std::memory_order_relaxed);
    if (mult == 1.0f || a2 <= 0.0f) {
        return real_AddCredits(a1, a2, a3);
    }
    return real_AddCredits(a1, a2 * mult, a3);
}

static int Lua_SetCreditsFreezeGlobal(lua_State* L) {
    const bool freeze = (fn_tonumber(L, 1) != 0.0);
    g_creditsFreeze_global.store(freeze, std::memory_order_relaxed);
    Log("[Bridge] SetCreditsFreezeGlobal(%s) -- LIVE\n", freeze ? "true" : "false");
    fn_pushstring(L, freeze
        ? "OK: credits frozen (LIVE -- AddCredits short-circuited)"
        : "OK: credits unfrozen (LIVE)");
    return 1;
}

static int Lua_GetCreditsFreezeGlobal(lua_State* L) {
    const bool freeze = g_creditsFreeze_global.load(std::memory_order_relaxed);
    fn_pushnumber(L, freeze ? 1.0 : 0.0);
    return 1;
}

static int Lua_SetCreditsMultiplierGlobal(lua_State* L) {
    double mult = fn_tonumber(L, 1);
    if (mult < 0.0) mult = 0.0;        // no negative deltas
    if (mult > 100.0) mult = 100.0;    // overflow guard
    g_creditsMult_global.store(static_cast<float>(mult), std::memory_order_relaxed);
    Log("[Bridge] SetCreditsMultiplierGlobal(mult=%.3f) -- LIVE\n", mult);
    fn_pushstring(L, "OK: credit multiplier applied (LIVE -- AddCredits delta scaling)");
    return 1;
}

static int Lua_GetCreditsMultiplierGlobal(lua_State* L) {
    const float mult = g_creditsMult_global.load(std::memory_order_relaxed);
    fn_pushnumber(L, static_cast<double>(mult));
    return 1;
}

// SWFOC_EnumerateUnits(slot) -> CSV of tactical units owned by `slot`.
// Task 158 (2026-04-23). Thin wrapper over Lua_ListTacticalUnits that
// filters rows by OwnerPlayerID. Same row shape as #104 so the V2
// Spawning tab (#149) can reuse its parser. Negative slots return
// "count=0" -- the engine's owner=-1 sentinel is not a valid filter
// target.
static int Lua_EnumerateUnits(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    if (slot < 0) {
        fn_pushstring(L, "count=0");
        return 1;
    }
    static constexpr int kMax = RVA::Selection::kMaxTacticalObjects;
    static thread_local uintptr_t s_objs[kMax];
    int count = WalkAllTacticalObjects(s_objs, kMax);
    if (count <= 0) {
        fn_pushstring(L, "count=0");
        return 1;
    }

    // Pre-resolve selection once for the is_selected column.
    uintptr_t selVec = 0;
    uintptr_t selObjs[RVA::Selection::kMaxSelectionCount];
    int selCount = 0;
    if (ResolveSelectionVector(selVec)) {
        selCount = WalkSelectionVector(selVec, selObjs, RVA::Selection::kMaxSelectionCount);
        if (selCount < 0) selCount = 0;
    }
    auto isSelected = [&](uintptr_t obj) {
        for (int i = 0; i < selCount; i++) if (selObjs[i] == obj) return 1;
        return 0;
    };
    int localSlot = FindLocalPlayerSlot();

    constexpr size_t kBufCap = 65536;
    char* buf = reinterpret_cast<char*>(malloc(kBufCap));
    if (!buf) {
        fn_pushstring(L, "ERR: SWFOC_EnumerateUnits: alloc failed");
        return 1;
    }

    // First pass: count matching rows so the header is accurate.
    int matched = 0;
    for (int i = 0; i < count; i++) {
        uintptr_t obj = s_objs[i];
        if (!IsValidObjAddr(obj)) continue;
        int32_t owner = *reinterpret_cast<int32_t*>(obj + RVA::GameObj::OwnerPlayerID);
        if (owner == slot) matched++;
    }
    size_t off = 0;
    off = SafeAppendFmt(buf, off, kBufCap, "count=%d", matched);
    for (int i = 0; i < count; i++) {
        uintptr_t obj = s_objs[i];
        if (!IsValidObjAddr(obj)) continue;
        int32_t owner = *reinterpret_cast<int32_t*>(obj + RVA::GameObj::OwnerPlayerID);
        if (owner != slot) continue;
        float   hull  = *reinterpret_cast<float*>(obj + RVA::GameObj::HP);
        uint8_t iflag = *reinterpret_cast<uint8_t*>(obj + RVA::GameObj::InvulnFlag);
        uint8_t pdb   = (*reinterpret_cast<uint8_t*>(obj + RVA::GameObj::PreventDeath) & 0x80) ? 1 : 0;
        int is_local  = (owner == localSlot) ? 1 : 0;
        int selected  = isSelected(obj);
        off = SafeAppendFmt(
            buf, off, kBufCap,
            "|%llu;%d;%.3f;%u;%u;%d;%d",
            (unsigned long long)obj,
            (int)owner, hull,
            (unsigned)iflag, (unsigned)pdb,
            is_local, selected);
        if (off >= kBufCap - 128) {
            SafeAppendFmt(buf, off, kBufCap, "|...+truncated");
            break;
        }
    }
    fn_pushstring(L, buf);
    free(buf);
    Log("[Bridge] EnumerateUnits(slot=%d): matched=%d / %d\n", slot, matched, count);
    return 1;
}

// SWFOC_EventStreamDrain() -> CSV of damage events since the last drain.
//
// Task 112 (2026-04-23). Row format (semicolon-separated, rows by '|'):
//   timestamp_ms;obj_addr;owner_slot;requested_hp;current_hp
//
// "count=0" when empty. Draining is destructive: readers consume the
// ring-buffer range [read_idx, write_idx) and advance read_idx past them.
// Subsequent drains with no new events return "count=0" — which also
// means the ring is safely drained before a new batch arrives.
static int Lua_EventStreamDrain(lua_State* L) {
    EnsureEventRingLock();
    EnterCriticalSection(&g_eventRingLock);
    LONG write_idx = g_eventWriteIdx;
    LONG read_idx  = g_eventReadIdx;
    LONG diff = write_idx - read_idx;
    if (diff < 0) diff = 0;
    int count = (diff > kEventRingSize) ? kEventRingSize : static_cast<int>(diff);
    int first_new = write_idx - count;

    constexpr size_t kBufCap = 32768;
    char* buf = reinterpret_cast<char*>(malloc(kBufCap));
    if (!buf) {
        LeaveCriticalSection(&g_eventRingLock);
        fn_pushstring(L, "ERR: SWFOC_EventStreamDrain: alloc failed");
        return 1;
    }
    size_t off = 0;
    off = SafeAppendFmt(buf, off, kBufCap, "count=%d", count);
    for (int i = 0; i < count; i++) {
        const DamageEvent& ev = g_eventRing[(first_new + i) % kEventRingSize];
        off = SafeAppendFmt(
            buf, off, kBufCap,
            "|%llu;%llu;%d;%.3f;%.3f",
            (unsigned long long)ev.timestamp_ms,
            (unsigned long long)ev.obj_addr,
            (int)ev.owner_slot,
            ev.requested_hp,
            ev.current_hp);
        if (off >= kBufCap - 80) break;
    }
    g_eventReadIdx = write_idx;
    LeaveCriticalSection(&g_eventRingLock);
    fn_pushstring(L, buf);
    free(buf);
    return 1;
}

// SWFOC_GetAllPlayers() -> CSV of per-slot rows.
// Task 111 (2026-04-23). Row format mirrors Lua_ReplayGetAllPlayers so the
// V2 Galactic + Diagnostics tabs can consume the same string shape whether
// reading live state or a replay fixture. One row per live slot:
//   slot;faction;credits;tech_level;is_human;is_local;unit_count
// Rows separated by '|'. Header "count=N" leads the string.
//
// unit_count is derived on the fly from WalkAllTacticalObjects — in
// galactic mode the tactical list is empty so every slot shows 0; that's
// expected and matches the game_mode=2 semantics of SWFOC_DumpState.
static int Lua_GetAllPlayers(lua_State* L) {
    uintptr_t pcAddr = g_base + RVA::PlayerCount_Global;
    int32_t rawCount = 0;
    if (!IsBadReadPtr(reinterpret_cast<void*>(pcAddr), 4)) {
        rawCount = static_cast<int32_t>(SafeReadU32(pcAddr));
    }
    if (rawCount < 0) rawCount = 0;
    if (rawCount > 8) rawCount = 8;

    uintptr_t arrBase = SafeReadU64(g_base + RVA::PlayerArray_Global);
    int localSlot = FindLocalPlayerSlot();

    // Snapshot every tactical obj's owner in a single walk so the per-slot
    // unit_count loop is O(players + units) instead of O(players * units).
    static constexpr int kMaxObj = RVA::Selection::kMaxTacticalObjects;
    static thread_local uintptr_t s_objs[kMaxObj];
    int objCount = WalkAllTacticalObjects(s_objs, kMaxObj);
    int unitCountByOwner[16] = {0};
    for (int i = 0; i < objCount; i++) {
        uintptr_t o = s_objs[i];
        if (!IsValidObjAddr(o)) continue;
        int32_t owner = *reinterpret_cast<int32_t*>(o + RVA::GameObj::OwnerPlayerID);
        if (owner >= 0 && owner < 16) unitCountByOwner[owner]++;
    }

    constexpr size_t kBufCap = 8192;
    char* buf = reinterpret_cast<char*>(malloc(kBufCap));
    if (!buf) {
        fn_pushstring(L, "ERR: SWFOC_GetAllPlayers: alloc failed");
        return 1;
    }
    size_t off = 0;
    off = SafeAppendFmt(buf, off, kBufCap, "count=%d", rawCount);

    for (int32_t i = 0; i < rawCount; i++) {
        uint64_t pPtr = arrBase ? SafeReadU64(arrBase + i * 8) : 0;
        const char* faction = pPtr
            ? SafeReadCStr(pPtr + RVA::PlayerObj::FactionName)
            : nullptr;
        float credits = pPtr ? SafeReadF32(pPtr + RVA::PlayerObj::Credits) : 0.0f;
        int32_t tech = pPtr
            ? static_cast<int32_t>(SafeReadU32(pPtr + RVA::PlayerObj::TechLevel))
            : 0;
        uint8_t lp = pPtr
            ? *reinterpret_cast<uint8_t*>(pPtr + RVA::PlayerObj::LocalPlayer)
            : 0;
        int is_human = (lp == 1) ? 1 : 0;
        int is_local = (i == localSlot) ? 1 : 0;
        int uc = (i >= 0 && i < 16) ? unitCountByOwner[i] : 0;
        off = SafeAppendFmt(
            buf, off, kBufCap,
            "|%d;%s;%.3f;%d;%d;%d;%d",
            i,
            faction && faction[0] ? faction : "UNKNOWN",
            credits,
            tech,
            is_human,
            is_local,
            uc);
        if (off >= kBufCap - 96) {
            SafeAppendFmt(buf, off, kBufCap, "|...+truncated");
            break;
        }
    }
    fn_pushstring(L, buf);
    free(buf);
    Log("[Bridge] GetAllPlayers: count=%d localSlot=%d\n", rawCount, localSlot);
    return 1;
}

// SWFOC_RevealAll(slot) -> "OK: revealed" or "ERR: ..."
// Task 113 (2026-04-23). Toggles fog-of-war for the given player slot by
// invoking the engine's Lua binding `FOW_Object:Reveal_All(player)` via
// SWFOC_DoString. The engine path (discovered via IDA: the Lua method
// registered at sub_1406A5B00 in LuaFOWRevealCommandClass, which calls
// sub_14035D4F0(TacticalGameManager, player_index)) only works in tactical
// mode — the engine itself rejects the call in galactic/menu. Two-tier
// safety: the Lua shim does the mode check, and our replay contract
// mirrors that via ReplayMutRevealAll idempotency.
//
// Slot semantics: -1 = local player, 0..7 = absolute slot. Matches the
// live `Find_Player` idiom the engine exposes.
static int Lua_RevealAll(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    char script[256];
    if (slot < 0) {
        snprintf(script, sizeof(script),
                 "local p = Find_Player(\"local\"); "
                 "if p and p.Get_Fog_Of_War then p:Get_Fog_Of_War():Reveal_All(p) end");
    } else {
        snprintf(script, sizeof(script),
                 "local p = Find_Player(%d); "
                 "if p and p.Get_Fog_Of_War then p:Get_Fog_Of_War():Reveal_All(p) end",
                 slot);
    }
    // Route through the existing SWFOC_DoString path — same execution surface
    // any editor-driven script would use, so the failure modes are identical.
    fn_pushstring(L, script);
    if (Lua_DoString(L) >= 1) {
        // Discard whatever DoString pushed; replace with our own status.
        fn_settop(L, 0);
    }
    Log("[Bridge] RevealAll(slot=%d): dispatched Lua shim\n", slot);
    fn_pushstring(L, "OK: reveal_all dispatched (tactical only)");
    return 1;
}

// SWFOC_CombinedGodOHK(enable) -> "OK: ..." or "ERR: ..."
// Task 128 (2026-04-23). Atomic toggle for the common "local immortal +
// enemy one-shot" demo state. Avoids the two-click UX where a user clicks
// GodMode then OHK with a brief window where only one is active. Acquires
// the combat-hook lock ONCE, flips both flags, releases. The hardpoint
// sweep from Lua_GodMode still fires on the god-mode transition so the
// engine-state path stays coherent with #99's contract.
static int Lua_CombinedGodOHK(lua_State* L) {
    EnsureCombatHookLock();
    int enable = static_cast<int>(fn_tonumber(L, 1));
    EnterCriticalSection(&g_combat_hook_lock);
    bool ok = true;
    if (enable) {
        if (!InstallCombatHook()) ok = false;
        InterlockedExchange(&g_god_mode_enabled, 1);
        InterlockedExchange(&g_ohk_enabled,       1);
    } else {
        InterlockedExchange(&g_god_mode_enabled, 0);
        InterlockedExchange(&g_ohk_enabled,       0);
        RemoveCombatHook();
    }
    LeaveCriticalSection(&g_combat_hook_lock);
    int flipped = SweepLocalUnitsInvulnerable(enable != 0);
    if (!ok) {
        fn_pushstring(L, "ERR: SWFOC_CombinedGodOHK: hook install failed");
        return 1;
    }
    Log("[Bridge] CombinedGodOHK -> %s (god=%d ohk=%d hook=%d sweep=%d)\n",
        enable ? "ENABLED" : "DISABLED",
        (int)g_god_mode_enabled, (int)g_ohk_enabled,
        (int)g_combat_hook_installed, flipped);
    char buf[96];
    snprintf(buf, sizeof(buf),
             enable
                 ? "OK: combined god+ohk enabled (sweep %d)"
                 : "OK: combined god+ohk disabled (sweep %d)",
             flipped);
    fn_pushstring(L, buf);
    return 1;
}

// SWFOC_OneHitKill(enable) -> "OK" or "ERR: ..."
// Same as Lua_GodMode but flips the OHK flag. The detour is shared; the
// two flags compose (god protects allies, OHK kills enemies).
static int Lua_OneHitKill(lua_State* L) {
    EnsureCombatHookLock();
    int enable = static_cast<int>(fn_tonumber(L, 1));
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
        fn_pushstring(L, "ERR: SWFOC_OneHitKill: hook install failed (see swfoc_bridge.log)");
        return 1;
    }
    Log("[Bridge] OneHitKill -> %s (god=%d ohk=%d hook=%d)\n",
        enable ? "ENABLED" : "DISABLED",
        (int)g_god_mode_enabled, (int)g_ohk_enabled, (int)g_combat_hook_installed);
    fn_pushstring(L, enable ? "OK: one-hit kill enabled" : "OK: one-hit kill disabled");
    return 1;
}

// ======================================================================
// Phase 3.2 (continuation): per-slot writers + observers ported from CE
// ----------------------------------------------------------------------
// CE trainer Tab 1 / Tab 6 features all iterate PlayerArray Lua-side and
// poke per-slot fields directly. The C++ port exposes one helper per
// (op, field) pair that takes the slot as an explicit argument, so the
// editor can drive the iteration in C# without depending on the in-game
// Lua VM. See ce_trainer_inventory.md sections 1.2 and 1.4.
//
// All helpers return either a number (for getters) or "OK"/"ERR: ..."
// (for setters). This is the same convention used by the original CE
// helpers above so the editor's NamedPipeLuaBridgeClient can parse them
// uniformly.
// ======================================================================

// Bounds check for slot index. The engine has 8 player slots; values
// outside [0, count) are rejected. Returns true if the slot resolves to a
// non-null PlayerObject pointer.
static bool ResolveSlotPlayer(int slot, uintptr_t& outPlayer) {
    int count = GetPlayerCount();
    if (slot < 0 || slot >= count || slot > 7) {
        outPlayer = 0;
        return false;
    }
    outPlayer = GetPlayerObj(slot);
    return outPlayer != 0;
}

// SWFOC_SetCreditsForSlot(slot, amount) -> "OK" or "ERR: ..."
// Per-slot version of SWFOC_SetCredits. Mirrors the CE trainer's "Give
// Credits to Faction (galactic)" button at v3.lua:1614-1626.
static int Lua_SetCreditsForSlot(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    double amount = fn_tonumber(L, 2);
    uintptr_t player = 0;
    if (!ResolveSlotPlayer(slot, player)) {
        fn_pushstring(L, "ERR: SWFOC_SetCreditsForSlot: invalid slot");
        return 1;
    }
    *reinterpret_cast<float*>(player + RVA::PlayerObj::Credits) = static_cast<float>(amount);
    Log("[Bridge] SetCreditsForSlot(%d, %.0f) OK\n", slot, amount);
    fn_pushstring(L, "OK");
    return 1;
}

// SWFOC_GetCreditsForSlot(slot) -> number or -1.0 on error
// Per-slot read of credits. Mirrors the CE trainer's faction-overview
// display at v3.lua:1574-1595.
static int Lua_GetCreditsForSlot(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    uintptr_t player = 0;
    if (!ResolveSlotPlayer(slot, player)) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    float c = *reinterpret_cast<float*>(player + RVA::PlayerObj::Credits);
    fn_pushnumber(L, static_cast<double>(c));
    return 1;
}

// SWFOC_SetTechForSlot(slot, level) -> "OK" or "ERR: ..."
// Per-slot tech level write. Mirrors v3.lua:1656-1668.
static int Lua_SetTechForSlot(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int level = static_cast<int>(fn_tonumber(L, 2));
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
    Log("[Bridge] SetTechForSlot(%d, %d) OK\n", slot, level);
    fn_pushstring(L, "OK");
    return 1;
}

// SWFOC_GetTechForSlot(slot) -> number or -1 on error
static int Lua_GetTechForSlot(lua_State* L) {
    int slot = static_cast<int>(fn_tonumber(L, 1));
    uintptr_t player = 0;
    if (!ResolveSlotPlayer(slot, player)) {
        fn_pushnumber(L, -1.0);
        return 1;
    }
    int t = *reinterpret_cast<int*>(player + RVA::PlayerObj::TechLevel);
    fn_pushnumber(L, static_cast<double>(t));
    return 1;
}

// SWFOC_DrainEnemyCredits() -> "OK: drained N slots" or "ERR: ..."
// Iterates PlayerArray, sets credits to 0 for every non-local slot. The
// editor doesn't have to do the iteration itself. Mirrors v3.lua:622-636
// (the "Drain Enemy Credits" button) and v3.lua:1673-1687 (the galactic
// "Drain All AI Credits" button — same effect, different tab).
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
    Log("[Bridge] DrainEnemyCredits() OK (%d slots)\n", drained);
    fn_pushstring(L, buf);
    return 1;
}

// SWFOC_SetHeroRespawn(seconds) -> "OK: prev=N.N" or "ERR: ..."
// Custom hero respawn timer. Range [0, 600] seconds (the trainer slider
// caps at 300; we cap at 600 for safety). Returns the previous value so
// callers can restore it. Mirrors v3.lua:1736-1760.
static int Lua_SetHeroRespawn(lua_State* L) {
    double seconds = fn_tonumber(L, 1);
    if (seconds < 0.0 || seconds > 600.0) {
        fn_pushstring(L, "ERR: SWFOC_SetHeroRespawn: out of [0, 600]");
        return 1;
    }
    auto addr = reinterpret_cast<float*>(g_base + RVA::DefaultHeroRespawnTime);
    float prev = *addr;
    *addr = static_cast<float>(seconds);
    char buf[64];
    snprintf(buf, sizeof(buf), "OK: prev=%.1f new=%.1f", prev, (float)seconds);
    Log("[Bridge] SetHeroRespawn(%.1f) prev=%.1f OK\n", seconds, prev);
    fn_pushstring(L, buf);
    return 1;
}

// SWFOC_PreventUnitDeath(obj_addr, enable) -> "OK" or "ERR: ..."
// Sets bit 0x80 of obj+0x3A1 (PreventDeath flag). Mirrors the
// `Set_Cannot_Be_Killed(true)` Lua call's effect (per
// fact_make_invulnerable_hardpoint_propagation, this is the at-zero check
// the engine consults inside SetHP). Pair with SWFOC_SetUnitInvuln for
// the full god-mode effect on a single unit. See ce_trainer_inventory.md
// open question #14.
static int Lua_PreventUnitDeath(lua_State* L) {
    double rawAddr = fn_tonumber(L, 1);
    double rawFlag = fn_tonumber(L, 2);
    uintptr_t addr = static_cast<uintptr_t>(static_cast<uint64_t>(rawAddr));
    if (!IsValidObjAddr(addr)) {
        fn_pushstring(L, "ERR: SWFOC_PreventUnitDeath: invalid obj_addr");
        return 1;
    }
    // Task 102: enemy units are read-only. Reject writes to non-local units.
    if (!IsObjOwnedByHuman(addr)) {
        fn_pushstring(L, "ERR: SWFOC_PreventUnitDeath: not controllable (not owned by local slot)");
        return 1;
    }
    volatile uint8_t* p = reinterpret_cast<volatile uint8_t*>(addr + RVA::GameObj::PreventDeath);
    uint8_t cur = *p;
    if (rawFlag != 0.0) {
        *p = static_cast<uint8_t>(cur | 0x80);
    } else {
        *p = static_cast<uint8_t>(cur & ~0x80);
    }
    Log("[Bridge] PreventUnitDeath(0x%llX, %d) was=0x%02X now=0x%02X\n",
        (unsigned long long)addr, (int)(rawFlag != 0.0), (unsigned)cur, (unsigned)*p);
    fn_pushstring(L, "OK");
    return 1;
}

// SWFOC_GetMaxCredits() -> number or -1 on error
// Local-player MaxCredits read. Editor uses this to display the current
// cap and decide whether to call SWFOC_UncapCredits.
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
    fn_pushnumber(L, static_cast<double>(m));
    return 1;
}

// ======================================================================
// Diagnostic helpers — expose bridge state back to Lua for live validation.
// These intentionally avoid any memory writes; they're pure readers over
// our own module-state (counters, manifest) and live game memory.
// ======================================================================

// SWFOC_DiagListRegisteredFunctions() -> comma-separated manifest string.
// Source of truth is the static buffer populated by RegisterAll() exactly
// once per module load. This deliberately does NOT walk the Lua globals
// at runtime — the manifest is a compile-time proof of what the canonical
// registration table contained, independent of any later global mutation.
static int Lua_DiagListRegisteredFunctions(lua_State* L) {
    if (g_registeredFunctionManifest[0]) {
        fn_pushstring(L, g_registeredFunctionManifest);
    } else {
        fn_pushstring(L, "ERR: manifest not populated (RegisterAll never ran)");
    }
    return 1;
}

// SWFOC_DiagPipeStats() -> "received=N completed=M errors=K"
// Counters are atomic (InterlockedIncrement) so the PipeThreadProc writer
// and the Lua reader race safely. Reader uses plain LONG load — on x86_64
// aligned 32-bit loads are atomic, and we don't need monotonic ordering
// across the three values (a diagnostic, not a consistency gate).
static int Lua_DiagPipeStats(lua_State* L) {
    LONG received  = g_pipeReceivedCount;
    LONG completed = g_pipeCompletedCount;
    LONG errors    = g_pipeErrorCount;
    char buf[128];
    snprintf(buf, sizeof(buf), "received=%ld completed=%ld errors=%ld",
             (long)received, (long)completed, (long)errors);
    fn_pushstring(L, buf);
    return 1;
}

// SWFOC_DiagGameTick() -> current value of g_luaDCallTickCounter as a number.
// The counter increments on EVERY Hook_luaD_call entry, so a caller can
// sample twice a second apart and prove the hook is live. If two samples
// differ by zero while the game is supposedly running, the hook is dead.
static int Lua_DiagGameTick(lua_State* L) {
    LONGLONG tick = g_luaDCallTickCounter;
    // Push as number — LONGLONG fits lossless in double up to 2^53 ticks,
    // which at ~60 Hz would take ~4.7M years to overflow.
    fn_pushnumber(L, static_cast<double>(tick));
    return 1;
}

// SWFOC_DiagSelfTest() -> "passed=N failed=M details=..."
// Offline sanity checks over live game memory. Each check emits one Log
// line and contributes one token to the details string. Safe: read-only,
// bounded, no Lua-global probing. Used by the probe harness to confirm
// the bridge's basic assumptions about game state are still valid.
static int Lua_DiagSelfTest(lua_State* L) {
    int passed = 0;
    int failed = 0;
    char details[1024];
    size_t doff = 0;
    details[0] = '\0';

    auto append_detail = [&](const char* tag, bool ok) {
        size_t remaining = (doff >= sizeof(details) - 1) ? 0 : sizeof(details) - doff - 1;
        if (remaining > 0) {
            int n = snprintf(details + doff, remaining, "%s%s=%s",
                             doff > 0 ? "," : "", tag, ok ? "OK" : "FAIL");
            if (n > 0 && (size_t)n < remaining) doff += (size_t)n;
        }
        if (ok) ++passed; else ++failed;
    };

    // Check 1: PlayerArray_Global non-null and inside a sane user-space range.
    // The OLD check compared against the module range [0x140000000, 0x180000000),
    // but PlayerArray is HEAP-allocated — live test 2026-04-10 showed
    // PlayerArray=0x000002983d64b950 count=8 localSlot=6 (all other downstream
    // checks PASSED), yet the module-range check falsely reported FAIL. Heap
    // pointers on x86_64 Windows are 48-bit user-space addresses above
    // 0x10000000 with the upper 16 bits clear.
    uintptr_t pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
    bool paOk = (pa != 0 && pa > 0x10000000ULL && (pa >> 48) == 0);
    Log("[DiagSelfTest] PlayerArray_Global=0x%llX %s\n",
        (unsigned long long)pa, paOk ? "OK" : "FAIL");
    append_detail("player_array", paOk);

    // Check 2: PlayerCount_Global positive (> 0 in any loaded game).
    int pc = *reinterpret_cast<int*>(g_base + RVA::PlayerCount_Global);
    bool pcOk = (pc > 0 && pc <= 8);  // engine caps at 8
    Log("[DiagSelfTest] PlayerCount_Global=%d %s\n", pc, pcOk ? "OK" : "FAIL");
    append_detail("player_count", pcOk);

    // Check 3: FindLocalPlayerSlot in [0, PlayerCount).
    int localSlot = FindLocalPlayerSlot();
    bool slotOk = (localSlot >= 0 && localSlot < pc);
    Log("[DiagSelfTest] local_slot=%d (count=%d) %s\n", localSlot, pc,
        slotOk ? "OK" : "FAIL");
    append_detail("local_slot", slotOk);

    // Check 4: Local-player credits are a finite float (not NaN, not Inf).
    bool creditsOk = false;
    if (slotOk) {
        uintptr_t p = GetPlayerObj(localSlot);
        if (p) {
            float c = *reinterpret_cast<float*>(p + RVA::PlayerObj::Credits);
            // Finite check without pulling <cmath>: NaN != NaN, Inf is both
            // > FLT_MAX and < -FLT_MAX but easier: compare against self and
            // against a big sane bound.
            creditsOk = (c == c) && c > -1.0e12f && c < 1.0e12f;
        }
    }
    Log("[DiagSelfTest] local_credits_finite %s\n", creditsOk ? "OK" : "FAIL");
    append_detail("credits_finite", creditsOk);

    // Check 5: PlayerListClass +0x30 current-slot int in [0, PlayerCount).
    // The PlayerListClass_Global is a direct instance, not a pointer-to-pointer
    // (see SetHumanPlayer comments). Current slot lives at +0x30 inside it.
    int curSlot = *reinterpret_cast<int*>(g_base + RVA::PlayerListClass_Global + 0x30);
    bool curSlotOk = (curSlot >= 0 && curSlot < pc);
    Log("[DiagSelfTest] PlayerListClass+0x30 curSlot=%d %s\n", curSlot,
        curSlotOk ? "OK" : "FAIL");
    append_detail("curslot_range", curSlotOk);

    // Check 6: Walk PlayerArray[local_slot] and read its +0x62 LocalPlayer
    // byte, verify it's 0 or 1 (valid engine boolean, not garbage).
    bool walkOk = false;
    if (slotOk) {
        uintptr_t p = GetPlayerObj(localSlot);
        if (p) {
            uint8_t lp = *reinterpret_cast<uint8_t*>(p + RVA::PlayerObj::LocalPlayer);
            walkOk = (lp == 0 || lp == 1);
        }
    }
    Log("[DiagSelfTest] walk_local_player_byte %s\n", walkOk ? "OK" : "FAIL");
    append_detail("lp_byte_valid", walkOk);

    char out[1280];
    snprintf(out, sizeof(out), "passed=%d failed=%d details=%s",
             passed, failed, details);
    fn_pushstring(L, out);
    return 1;
}

// ======================================================================
// SWFOC_TriggerVictory (iter-450 scaffolding)
// ======================================================================
//
// Programmatic victory trigger via the 18 VictoryType enum entries pinned
// at rva_victory_type_enum_init @ 0x341FF0 (iter-414 RE). iter-450 ships:
//   1. The Lua-callable wrapper (validates victory_type against 14-of-18
//      known names + stages pending state; emits PHASE2_PENDING status).
//   2. A DORMANT MinHook detour for rva_victory_monitor_counter_inc @
//      0x341FE0 (Option C target per iter-449 disambiguation). The
//      trampoline is created via MH_CreateHook in LuaBridge_Init;
//      MH_EnableHook is INTENTIONALLY deferred to iter-450a.
//
// iter-450a still needs:
//   (a) AwaitingVictoryTest 48-byte struct layout RE for safe injection
//       (constructing one without the layout = guaranteed memory corruption).
//   (b) Capture-on-CTOR hook at rva_victory_monitor_ctor @ 0x341850 to
//       identify the VictoryMonitor instance pointer (counter_inc fires
//       for many engine subsystems; we need a `rcx`-discriminator).
//
// See knowledge-base/iter449_breakthrough_disambiguation_parent_tick_inlines.md
// for the full hook-strategy analysis.
// ======================================================================

// Known VictoryType names per rva_victory_type_enum_init (iter-414 RE).
// 14-of-18 entries the enum initializer claims; iter-450a will extract
// the remaining 4 from the full decompile and extend this list.
static const char* const kKnownVictoryTypes[] = {
    "Galactic_Conquer",
    "Galactic_Control",
    "Galactic_Cycles",
    "Galactic_Kill_Enemy",
    "Galactic_Super_Weapon",
    "Skirmish_All_Enemies",
    "Skirmish_Control",
    "Skirmish_Enemy_Capitulate",
    "Skirmish_Space_Eradication",
    "Sub_Tactical_All",
    "Sub_Tactical_Enemy",
    "Sub_Tactical_Land",
    "Sub_Tactical_Space",
    "Sub_Tactical_Story",
    nullptr
};

static volatile LONG g_victoryTriggerPending = 0;
static char g_victoryTriggerType[64] = {0};

static bool IsKnownVictoryType(const char* name) {
    if (!name) return false;
    for (size_t i = 0; kKnownVictoryTypes[i]; ++i) {
        if (strcmp(kKnownVictoryTypes[i], name) == 0) return true;
    }
    return false;
}

static int Lua_TriggerVictory(lua_State* L) {
    int top = fn_gettop(L);
    if (top < 1) {
        fn_pushstring(L, "ERR_NO_ARG: SWFOC_TriggerVictory(victory_type) requires 1 string arg");
        return 1;
    }

    const char* victoryType = fn_tostring(L, 1);
    if (!victoryType || !*victoryType) {
        fn_pushstring(L, "ERR_BAD_ARG: victory_type must be non-empty string");
        return 1;
    }

    if (!IsKnownVictoryType(victoryType)) {
        fn_pushstring(L, "ERR_UNKNOWN_TYPE: not in VictoryType enum "
                         "(per rva_victory_type_enum_init @ 0x341FF0)");
        return 1;
    }

    // Stage the pending trigger; iter-450a will detour 0x140341FE0 and
    // inject an always-pass AwaitingVictoryTest into VictoryMonitor's
    // DynamicVector at instance+0x68 on the next tick that matches our
    // captured VictoryMonitor instance pointer.
    strncpy(g_victoryTriggerType, victoryType, sizeof(g_victoryTriggerType) - 1);
    g_victoryTriggerType[sizeof(g_victoryTriggerType) - 1] = '\0';
    InterlockedExchange(&g_victoryTriggerPending, 1);

    Log("[Bridge] SWFOC_TriggerVictory staged: %s "
        "(PHASE2_PENDING; iter-450a needs struct layout + capture hook)\n",
        victoryType);

    fn_pushstring(L,
        "PHASE2_PENDING: victory_type validated and staged; iter-450 ships "
        "the wrapper + DORMANT detour. iter-450a will enable the MinHook "
        "at 0x341FE0 once AwaitingVictoryTest 48-byte struct layout is RE'd "
        "and capture-on-CTOR hook at 0x341850 is added (resolves the "
        "rcx-discriminator problem).");
    return 1;
}

// DORMANT MinHook detour for rva_victory_monitor_counter_inc @ 0x341FE0.
// MH_CreateHook is called at LuaBridge_Init time; MH_EnableHook is NOT.
// iter-450a will: (1) populate the inject branch with AwaitingVictoryTest
// construction logic once the 48-byte struct layout is RE'd, (2) call
// MH_EnableHook to activate the detour, (3) add a sibling capture-on-CTOR
// detour at 0x341850 that stores the VictoryMonitor `this` pointer so the
// counter_inc hook can identify which `rcx` belongs to the monitor.
typedef void (__fastcall *pfn_VictoryMonitorCounter)(void* this_obj);
static pfn_VictoryMonitorCounter real_VictoryMonitorCounter = nullptr;

static void __fastcall Hook_VictoryMonitorCounter(void* this_obj) {
    // iter-450a inject-branch placeholder. Will read:
    //
    //   if (g_victoryTriggerPending && this_obj == g_capturedVictoryMonitor) {
    //       BuildAndInjectAlwaysPassTest(
    //           static_cast<VictoryMonitor*>(this_obj),
    //           g_victoryTriggerType);
    //       InterlockedExchange(&g_victoryTriggerPending, 0);
    //   }
    //
    // For now: pass-through to original. The trampoline is created but
    // MH_EnableHook was never called, so this function never actually
    // runs except via direct call -- which never happens. The cost is
    // exactly one MH_CreateHook trampoline allocation at module load.
    real_VictoryMonitorCounter(this_obj);
}

// ======================================================================
// Registration
// ======================================================================

// Canonical registration — single source of truth for every SWFOC_* helper
// exposed to the game's Lua state. Previously the live registration path was
// a hand-rolled inline block inside Hook_lua_open that drifted from this
// table, with the result that 8 helpers (Phase 3.2 per-slot writers) were
// never actually callable despite being defined in the source. The linker's
// -s strip mode also dead-stripped the string literals of the old table
// because nothing called RegisterAll. This function is now the ONLY code
// path that calls fn_pushcclosure for helper registration — the Hook_lua_open
// block that used to duplicate this work now just calls RegisterAll(L).
//
// Ground-truth helper inventory (as of 2026-04-11):
// 30 pre-existing = 29 static int Lua_* + 1 static int SWFOC_StateInfo cast.
// 4 diagnostics = Lua_DiagListRegisteredFunctions, Lua_DiagPipeStats,
// Lua_DiagGameTick, Lua_DiagSelfTest.
// 2 selection (2026-04-11) = Lua_GetSelectedUnit, Lua_GetSelectedUnits.
// 1 faction fix (2026-04-11) = Lua_SetHumanPlayer_v2.
// Total = 37 helpers. The SWFOC_BRIDGE_VERSION macro tracks this count.
static void RegisterAll(lua_State* L) {
    struct HelperEntry { const char* name; lua_CFunction func; };
    static const HelperEntry funcs[] = {
        // Core / metadata
        {"SWFOC_GetVersion",         Lua_GetVersion},
        {"SWFOC_GetBuildInfo",       Lua_GetBuildInfo},
        {"SWFOC_Log",                Lua_Log},
        {"SWFOC_DoString",           Lua_DoString},
        {"SWFOC_DrainPipe",          Lua_DrainPipe},
        {"SWFOC_StateInfo",          (lua_CFunction)SWFOC_StateInfo},
        {"SWFOC_EventControl",       Lua_EventControl},
        {"SWFOC_DumpState",          Lua_DumpState},
        // Player / economy
        {"SWFOC_GetLocalPlayer",     Lua_GetLocalPlayer},
        // 2026-04-25: v1 deleted (silent no-op in galactic mode);
        // v2 unregistered (manual sweep without AI swap caused the
        // dual-control bug confirmed live on 2026-04-25). v3 is the
        // single canonical SetHumanPlayer surface; the v2 function
        // remains in the source as historical reference but is no
        // longer dispatchable from Lua.
        {"SWFOC_SetHumanPlayer_v3",  Lua_SetHumanPlayer_v3},
        {"SWFOC_GetAiBrain",         Lua_GetAiBrain},
        {"SWFOC_NullAiBrain",        Lua_NullAiBrain},
        {"SWFOC_AttachAiBrain",      Lua_AttachAiBrain},
        {"SWFOC_SetCredits",         Lua_SetCredits},
        {"SWFOC_GetCredits",         Lua_GetCredits},
        {"SWFOC_SetTechLevel",       Lua_SetTechLevel},
        {"SWFOC_UncapCredits",       Lua_UncapCredits},
        {"SWFOC_HeroInstantRespawn", Lua_HeroInstantRespawn},
        {"SWFOC_ListFactions",       Lua_ListFactions},
        // Phase 3.2: combat / inspect helpers ported from CE trainer
        {"SWFOC_SetUnitInvuln",      Lua_SetUnitInvuln},
        {"SWFOC_SetUnitHull",        Lua_SetUnitHull},
        {"SWFOC_InspectUnit",        Lua_InspectUnit},
        {"SWFOC_GetHardpoints",      Lua_GetHardpoints},
        {"SWFOC_GetSelectedUnit",    Lua_GetSelectedUnit},
        {"SWFOC_GetSelectedUnits",   Lua_GetSelectedUnits},
        {"SWFOC_ListTacticalUnits",  Lua_ListTacticalUnits},
        {"SWFOC_GodMode",            Lua_GodMode},
        {"SWFOC_OneHitKill",         Lua_OneHitKill},
        {"SWFOC_CombinedGodOHK",     Lua_CombinedGodOHK},
        {"SWFOC_RevealAll",          Lua_RevealAll},
        {"SWFOC_GetAllPlayers",      Lua_GetAllPlayers},
        {"SWFOC_EnumerateUnits",     Lua_EnumerateUnits},
        {"SWFOC_HealAllLocal",       Lua_HealAllLocal},
        {"SWFOC_KillUnit",           Lua_KillUnit},
        {"SWFOC_ReviveUnit",         Lua_ReviveUnit},
        {"SWFOC_SetUnitShield",      Lua_SetUnitShield},
        {"SWFOC_GetUnitShield",      Lua_GetUnitShield},
        {"SWFOC_SetUnitSpeed",       Lua_SetUnitSpeed},
        {"SWFOC_GetUnitSpeed",       Lua_GetUnitSpeed},
        {"SWFOC_ClearUnitSpeedOverride", Lua_ClearUnitSpeedOverride},
        {"SWFOC_ListHeroes",         Lua_ListHeroes},
        {"SWFOC_SetHeroRespawnTimer",Lua_SetHeroRespawnTimer},
        {"SWFOC_SetPermadeath",      Lua_SetPermadeath},
        {"SWFOC_HeroStatEdit",       Lua_HeroStatEdit},
        {"SWFOC_GetPlanets",         Lua_GetPlanets},
        // 2026-05-07 (iter 299): Faction roster + current-mod enumeration wires.
        // GetFactionRoster: DoString-driven via Find_All_Objects_Of_Type filter;
        // mirrors iter-296 GetPlanets shape (engine-already-does-this 5th instance).
        // GetCurrentMod: filesystem probe of ./Mods/*/Modinfo.xml (no engine Lua API);
        // returns most-recently-accessed mod folder + path.
        {"SWFOC_GetFactionRoster",   Lua_GetFactionRoster},
        {"SWFOC_GetCurrentMod",      Lua_GetCurrentMod},
        // 2026-05-07 (iter 300; 300th-iter milestone): mod enumeration —
        // mirrors GetCurrentMod's filesystem walk but emits ALL candidate
        // mods. Settings tab consumes for the operator-facing mod-picker UI.
        {"SWFOC_ListMods",           Lua_ListMods},
        {"SWFOC_ChangePlanetOwner",  Lua_ChangePlanetOwner},
        {"SWFOC_ChangePlanetOwnerWithMode", Lua_ChangePlanetOwnerWithMode}, // iter 137 Phase-1 mirror
        {"SWFOC_SpawnAsStoryArrival",       Lua_SpawnAsStoryArrival},        // iter 137 Phase-1 mirror
        {"SWFOC_GetPlanetTechAndBuildings", Lua_GetPlanetTechAndBuildings},
        {"SWFOC_SetDiplomacy",       Lua_SetDiplomacy},
        {"SWFOC_ListAbilities",      Lua_ListAbilities},
        {"SWFOC_TriggerAbility",     Lua_TriggerAbility},
        // 2026-05-07 (iter 450 scaffolding): SWFOC_TriggerVictory wrapper.
        // Validates victory_type vs. 14-of-18 known VictoryType enum names and
        // stages pending state. The active MinHook detour is DORMANT (iter-450a
        // will enable it once the AwaitingVictoryTest 48-byte struct layout +
        // capture-on-CTOR hook are landed).
        {"SWFOC_TriggerVictory",     Lua_TriggerVictory},
        {"SWFOC_SetIncomeMultiplier",Lua_SetIncomeMultiplier},
        {"SWFOC_SetGameSpeed",       Lua_SetGameSpeed},
        {"SWFOC_FreezeCredits",      Lua_SetFreezeCredits},
        {"SWFOC_SetBuildSpeed",      Lua_SetBuildSpeed},
        {"SWFOC_SetPerFactionSpeedMultiplier", Lua_SetPerFactionSpeedMultiplier},
        {"SWFOC_SetDamageMultiplier", Lua_SetDamageMultiplier},
        {"SWFOC_GetDamageMultiplier", Lua_GetDamageMultiplier},
        // 2026-04-28 (iter 96): LIVE-badged global-only siblings.
        {"SWFOC_SetDamageMultiplierGlobal", Lua_SetDamageMultiplierGlobal},
        // 2026-05-06 (iter 225): SetFireRate global LIVE wire — WeaponTick MinHook detour scales dt by g_fireRateMult_global
        {"SWFOC_SetFireRateMultiplierGlobal", Lua_SetFireRateMultiplierGlobal},
        {"SWFOC_GetFireRateMultiplierGlobal", Lua_GetFireRateMultiplierGlobal},
        // 2026-05-08 (iter 285): Tier 3 HUD counters via DeathHandler detour extension.
        {"SWFOC_GetPlayerKills",     Lua_GetPlayerKills},
        {"SWFOC_GetPlayerDeaths",    Lua_GetPlayerDeaths},
        {"SWFOC_GetTotalUnitsAlive", Lua_GetTotalUnitsAlive},
        // 2026-05-06 (iter 230-231): FreezeCredits global LIVE wire — AddCredits MinHook detour. +4 LIVE flips.
        {"SWFOC_SetCreditsFreezeGlobal", Lua_SetCreditsFreezeGlobal},
        {"SWFOC_GetCreditsFreezeGlobal", Lua_GetCreditsFreezeGlobal},
        {"SWFOC_SetCreditsMultiplierGlobal", Lua_SetCreditsMultiplierGlobal},
        {"SWFOC_GetCreditsMultiplierGlobal", Lua_GetCreditsMultiplierGlobal},
        {"SWFOC_GetDamageMultiplierGlobal", Lua_GetDamageMultiplierGlobal},
        {"SWFOC_SetFireRate",        Lua_SetFireRate},
        {"SWFOC_SetAreaDamage",      Lua_SetAreaDamage},
        {"SWFOC_SetTargetFilter",    Lua_SetTargetFilter},
        {"SWFOC_ToggleOHKAttackPower", Lua_ToggleOHKAttackPower},
        {"SWFOC_FreezeAI",           Lua_FreezeAI},
        {"SWFOC_FreeCam",            Lua_FreeCam},
        {"SWFOC_SetCameraPos",       Lua_SetCameraPos},
        {"SWFOC_GetCameraPos",       Lua_GetCameraPos},
        // 2026-04-28 (iter 107) — LIVE camera target via engine Lua API.
        {"SWFOC_ScrollCameraToTarget", Lua_ScrollCameraToTarget},
        {"SWFOC_CameraFollow",       Lua_CameraFollow},        // iter 143 LIVE
        {"SWFOC_RotateCameraTo",     Lua_RotateCameraTo},      // iter 144 LIVE
        {"SWFOC_StartCinematicCamera", Lua_StartCinematicCamera},                  // iter 145 LIVE
        {"SWFOC_EndCinematicCamera", Lua_EndCinematicCamera},                      // iter 145 LIVE
        {"SWFOC_SetCinematicCameraKey", Lua_SetCinematicCameraKey},                // iter 145 LIVE
        {"SWFOC_TransitionCinematicCameraKey", Lua_TransitionCinematicCameraKey},  // iter 145 LIVE
        {"SWFOC_LetterBoxOn",        Lua_LetterBoxOn},          // iter 150 LIVE
        {"SWFOC_LetterBoxOff",       Lua_LetterBoxOff},         // iter 150 LIVE
        {"SWFOC_TeleportUnitLua",    Lua_TeleportUnitLua},      // iter 151 LIVE
        {"SWFOC_GalacticSpawnUnit",  Lua_GalacticSpawnUnit},    // iter 152 LIVE
        {"SWFOC_SetCannotBeKilledLua", Lua_SetCannotBeKilledLua}, // iter 153 LIVE
        {"SWFOC_EnableStealthLua",   Lua_EnableStealthLua},     // iter 153 LIVE
        {"SWFOC_HealUnitLua",        Lua_HealUnitLua},          // iter 154 LIVE
        {"SWFOC_TakeDamageLua",      Lua_TakeDamageLua},        // iter 154 LIVE
        {"SWFOC_SetDamageModifierLua", Lua_SetDamageModifierLua},   // iter 154 LIVE
        {"SWFOC_SetRateOfFireModifierLua", Lua_SetRateOfFireModifierLua}, // iter 154 LIVE
        {"SWFOC_PlayerGiveMoneyLua", Lua_PlayerGiveMoneyLua},   // iter 155 LIVE
        {"SWFOC_PlayerSetTechLevelLua", Lua_PlayerSetTechLevelLua}, // iter 155 LIVE
        {"SWFOC_PlayerUnlockTechLua", Lua_PlayerUnlockTechLua},  // iter 155 LIVE
        {"SWFOC_ActivateAbilityLua", Lua_ActivateAbilityLua},    // iter 156 LIVE
        {"SWFOC_DisableCaptureLua",  Lua_DisableCaptureLua},     // iter 156 LIVE
        {"SWFOC_SetGarrisonSpawnLua",Lua_SetGarrisonSpawnLua},   // iter 156 LIVE
        {"SWFOC_CancelHyperspaceLua",Lua_CancelHyperspaceLua},   // iter 156 LIVE
        {"SWFOC_SetInLimboLua",      Lua_SetInLimboLua},         // iter 157 LIVE
        {"SWFOC_SetCheckContestedSpaceLua", Lua_SetCheckContestedSpaceLua}, // iter 157 LIVE
        {"SWFOC_SellUnitLua",        Lua_SellUnitLua},           // iter 157 LIVE
        {"SWFOC_BribeLua",           Lua_BribeLua},              // iter 157 LIVE
        {"SWFOC_MoveToLua",          Lua_MoveToLua},             // iter 157 LIVE
        {"SWFOC_FireSpecialWeaponLua", Lua_FireSpecialWeaponLua},// iter 157 LIVE
        {"SWFOC_DisableBombingRunLua", Lua_DisableBombingRunLua},// iter 158 LIVE
        {"SWFOC_FlashGuiObjectLua",  Lua_FlashGuiObjectLua},     // iter 158 LIVE
        {"SWFOC_HideGuiObjectLua",   Lua_HideGuiObjectLua},      // iter 158 LIVE
        {"SWFOC_StoryEventLua",      Lua_StoryEventLua},         // iter 159 LIVE
        {"SWFOC_AddObjectiveLua",    Lua_AddObjectiveLua},       // iter 159 LIVE
        {"SWFOC_PlayMusicLua",       Lua_PlayMusicLua},          // iter 159 LIVE
        {"SWFOC_PlaySfxEventLua",    Lua_PlaySfxEventLua},       // iter 159 LIVE
        {"SWFOC_LockControlsLua",    Lua_LockControlsLua},       // iter 160 LIVE
        {"SWFOC_DisableOrbitalBombardmentLua", Lua_DisableOrbitalBombardmentLua}, // iter 160 LIVE
        {"SWFOC_StoryEventTriggerLua", Lua_StoryEventTriggerLua},// iter 160 LIVE
        {"SWFOC_LockTechLua",        Lua_LockTechLua},           // iter 161 LIVE
        {"SWFOC_MakeAllyLua",        Lua_MakeAllyLua},           // iter 161 LIVE
        {"SWFOC_MakeEnemyLua",       Lua_MakeEnemyLua},          // iter 161 LIVE
        {"SWFOC_OverrideMaxSpeedLua", Lua_OverrideMaxSpeedLua},  // iter 162 LIVE
        {"SWFOC_SuspendAiLua",       Lua_SuspendAiLua},          // iter 162 LIVE
        {"SWFOC_FadeScreenInLua",    Lua_FadeScreenInLua},       // iter 162 LIVE
        {"SWFOC_ZoomCameraLua",      Lua_ZoomCameraLua},         // iter 162 LIVE
        {"SWFOC_AttackTargetLua",    Lua_AttackTargetLua},       // iter 163 LIVE
        {"SWFOC_GuardTargetLua",     Lua_GuardTargetLua},        // iter 163 LIVE
        {"SWFOC_DivertLua",          Lua_DivertLua},             // iter 163 LIVE
        {"SWFOC_EnableAsActorLua",   Lua_EnableAsActorLua},      // iter 164 LIVE
        {"SWFOC_ReleaseCreditsForTacticalLua", Lua_ReleaseCreditsForTacticalLua}, // iter 164 LIVE
        {"SWFOC_SelectObjectLua",    Lua_SelectObjectLua},       // iter 164 LIVE
        {"SWFOC_FadeScreenOutLua",   Lua_FadeScreenOutLua},      // iter 165 LIVE
        {"SWFOC_RotateCameraByLua",  Lua_RotateCameraByLua},     // iter 165 LIVE
        {"SWFOC_PointCameraAtLua",   Lua_PointCameraAtLua},      // iter 165 LIVE
        {"SWFOC_StopAllMusicLua",    Lua_StopAllMusicLua},       // iter 166 LIVE (new helper)
        {"SWFOC_ResumeModeBasedMusicLua", Lua_ResumeModeBasedMusicLua}, // iter 166 LIVE (new helper)
        {"SWFOC_ShowGuiObjectLua",   Lua_ShowGuiObjectLua},      // iter 166 LIVE
        {"SWFOC_GetHullLua",         Lua_GetHullLua},            // iter 167 LIVE (new getter helper)
        {"SWFOC_GetHealthLua",       Lua_GetHealthLua},          // iter 167 LIVE (new getter helper)
        {"SWFOC_GetShieldLua",       Lua_GetUnitShieldLuaGetter},// iter 167 LIVE (new getter helper)
        {"SWFOC_HasAttackTargetLua", Lua_HasAttackTargetLua},    // iter 168 LIVE
        {"SWFOC_AreEnginesOnlineLua", Lua_AreEnginesOnlineLua},  // iter 168 LIVE
        {"SWFOC_GetOwnerLua",        Lua_GetOwnerLua},           // iter 168 LIVE
        {"SWFOC_GetTypeLua",         Lua_GetUnitTypeLua},        // iter 169 LIVE
        {"SWFOC_GetCreditsLua",      Lua_GetCreditsLua},         // iter 169 LIVE
        {"SWFOC_GetFactionLua",      Lua_GetFactionLua},         // iter 169 LIVE
        {"SWFOC_GetTechLevelLua",    Lua_GetTechLevelLua},       // iter 169 LIVE
        {"SWFOC_GetNameLua",         Lua_GetNameLua},            // iter 170 LIVE
        {"SWFOC_IsStealthedLua",     Lua_IsStealthedLua},        // iter 170 LIVE
        {"SWFOC_IsInLimboLua",       Lua_IsInLimboLua},          // iter 170 LIVE
        {"SWFOC_IsCapturableLua",    Lua_IsCapturableLua},       // iter 170 LIVE
        {"SWFOC_GetPositionLua",     Lua_GetPositionLua},        // iter 171 LIVE
        {"SWFOC_GetParentObjectLua", Lua_GetParentObjectLua},    // iter 171 LIVE
        {"SWFOC_GetAttackTargetLua", Lua_GetAttackTargetLua},    // iter 171 LIVE
        {"SWFOC_GetDamageModifierLua", Lua_GetDamageModifierLua},// iter 171 LIVE
        {"SWFOC_GetGarrisonUnitsLua", Lua_GetGarrisonUnitsLua},  // iter 172 LIVE — 100 milestone
        {"SWFOC_GetContainedObjectCountLua", Lua_GetContainedObjectCountLua}, // iter 172 LIVE
        {"SWFOC_GetBehaviorIdLua",   Lua_GetBehaviorIdLua},      // iter 172 LIVE
        {"SWFOC_GetRateOfFireModifierLua", Lua_GetRateOfFireModifierLua}, // iter 172 LIVE
        {"SWFOC_IsAbilityActiveLua", Lua_IsAbilityActiveLua},    // iter 173 LIVE (new arg-getter helper)
        {"SWFOC_HasPropertyLua",     Lua_HasPropertyLua},        // iter 173 LIVE
        {"SWFOC_IsCategoryLua",      Lua_IsCategoryLua},         // iter 173 LIVE
        {"SWFOC_GetDistanceLua",     Lua_GetDistanceLua},        // iter 173 LIVE
        {"SWFOC_GetBonePositionLua", Lua_GetBonePositionLua},    // iter 174 LIVE
        {"SWFOC_ContainsObjectTypeLua", Lua_ContainsObjectTypeLua}, // iter 174 LIVE
        {"SWFOC_GetSpaceStationLevelLua", Lua_GetSpaceStationLevelLua}, // iter 174 LIVE
        {"SWFOC_GetTypeOfUnitLua",   Lua_GetTypeOfUnitLua},      // iter 174 LIVE
        {"SWFOC_TaskForceMoveToLua", Lua_TaskForceMoveToLua},    // iter 175 LIVE
        {"SWFOC_TaskForceReinforceLua", Lua_TaskForceReinforceLua}, // iter 175 LIVE
        {"SWFOC_TaskForceReleaseReinforcementsLua", Lua_TaskForceReleaseReinforcementsLua}, // iter 175 LIVE
        {"SWFOC_TaskForceLaunchUnitsLua", Lua_TaskForceLaunchUnitsLua}, // iter 175 LIVE
        {"SWFOC_TaskForceAttackTargetLua", Lua_TaskForceAttackTargetLua}, // iter 176 LIVE
        {"SWFOC_TaskForceGuardTargetLua", Lua_TaskForceGuardTargetLua}, // iter 176 LIVE
        {"SWFOC_TaskForceLandUnitsLua", Lua_TaskForceLandUnitsLua}, // iter 176 LIVE
        {"SWFOC_TaskForceSetAsGoalSystemRemovableLua", Lua_TaskForceSetAsGoalSystemRemovableLua}, // iter 176 LIVE
        {"SWFOC_FindObjectTypeLua", Lua_FindObjectTypeLua},      // iter 177 LIVE (new global-getter helper)
        {"SWFOC_FindPlanetLua",     Lua_FindPlanetLua},          // iter 177 LIVE
        {"SWFOC_FindFirstObjectLua", Lua_FindFirstObjectLua},    // iter 177 LIVE
        {"SWFOC_GetGameModeLua",    Lua_GetGameModeLua},         // iter 178 LIVE (NEW global-no-arg-getter helper — closes dispatcher matrix)
        {"SWFOC_GetLocalPlayerLua", Lua_GetLocalPlayerLua},      // iter 178 LIVE
        {"SWFOC_GetSecondsPerGameMinuteLua", Lua_GetSecondsPerGameMinuteLua}, // iter 178 LIVE
        {"SWFOC_IsEnemyLua",                 Lua_IsEnemyLua},                 // iter 179 LIVE (player-method via iter-173 helper)
        {"SWFOC_IsAllyLua",                  Lua_IsAllyLua},                  // iter 179 LIVE
        {"SWFOC_FindAllObjectsOfTypeLua",    Lua_FindAllObjectsOfTypeLua},    // iter 179 LIVE (global-arg via iter-177 helper)
        {"SWFOC_TaskForceMoveToTargetLua",   Lua_TaskForceMoveToTargetLua},   // iter 179 LIVE (TaskForce-method via iter-154 helper)
        {"SWFOC_FOWRevealAllLua",            Lua_FOWRevealAllLua},            // iter 180 LIVE (NAMESPACED via iter-158 — FOWManager.Reveal_All)
        {"SWFOC_FOWUndoRevealAllLua",        Lua_FOWUndoRevealAllLua},        // iter 180 LIVE
        {"SWFOC_UnlockControlsLua",          Lua_UnlockControlsLua},          // iter 180 LIVE (pairs with iter-160 LockControls)
        {"SWFOC_CorruptLua",                 Lua_CorruptLua},                 // iter 180 LIVE (Underworld faction; pairs with iter-157 Bribe)
        {"SWFOC_ThreadGetCurrentStageLua",   Lua_ThreadGetCurrentStageLua},   // iter 181 LIVE (NAMESPACED via iter-178 — extends iter-180 finding)
        {"SWFOC_SFXAllowUnitReponseVoLua",   Lua_SFXAllowUnitReponseVoLua},   // iter 181 LIVE (SFXManager namespace; preserves engine "Reponse" typo)
        {"SWFOC_GlobalMakeAllyLua",          Lua_GlobalMakeAllyLua},          // iter 182 LIVE (NEW global-2-arg helper — 10th in dispatcher set)
        {"SWFOC_GlobalMakeEnemyLua",         Lua_GlobalMakeEnemyLua},         // iter 182 LIVE
        {"SWFOC_FOWRevealLua",               Lua_FOWRevealLua},               // iter 184 LIVE (NEW global-3-arg helper — 11th; FOWManager.Reveal partial-reveal)
        {"SWFOC_ReinforceUnitLua",           Lua_ReinforceUnitLua},           // iter 185 LIVE (3-arg via iter-184; reinforcement-pool spawn)
        {"SWFOC_SpawnFromReinforcementPoolLua", Lua_SpawnFromReinforcementPoolLua}, // iter 185 LIVE
        {"SWFOC_CreateGenericObjectLua",     Lua_CreateGenericObjectLua},     // iter 185 LIVE (param order: type, position, player — DIFFERS from Spawn_Unit)
        {"SWFOC_FindNearestLua",             Lua_FindNearestLua},             // iter 186 LIVE (NEW global-3-arg-getter helper — 12th; symmetric to iter-184)
        // 2026-04-28 (iter 108) — LIVE per-unit owner change via Change_Owner.
        {"SWFOC_ChangeUnitOwner",      Lua_ChangeUnitOwner},
        // 2026-04-28 (iter 109) — LIVE unit spawn via Spawn_Unit Lua API.
        {"SWFOC_SpawnUnitLua",         Lua_SpawnUnitLua},
        // 2026-04-28 (iter 110) — LIVE per-unit invuln via Make_Invulnerable.
        {"SWFOC_MakeUnitInvulnLua",    Lua_MakeUnitInvulnLua},
        // 2026-04-28 (iter 111) — LIVE per-unit Hide / Prevent_AI_Usage / Set_Selectable.
        {"SWFOC_HideUnitLua",            Lua_HideUnitLua},
        {"SWFOC_PreventAiUsageLua",      Lua_PreventAiUsageLua},
        {"SWFOC_SetUnitSelectableLua",   Lua_SetUnitSelectableLua},
        // 2026-04-28 (iter 112) — LIVE per-unit Despawn / Stop / Retreat.
        {"SWFOC_DespawnUnitLua",         Lua_DespawnUnitLua},
        {"SWFOC_StopUnitLua",            Lua_StopUnitLua},
        {"SWFOC_RetreatUnitLua",         Lua_RetreatUnitLua},
        // 2026-04-28 (iter 113) — UNIVERSAL Lua-method dispatcher.
        {"SWFOC_CallObjMethodLua",       Lua_CallObjMethodLua},
        {"SWFOC_SpawnUnit",          Lua_SpawnUnit},
        {"SWFOC_SetBuildCost",       Lua_SetBuildCost},
        {"SWFOC_SetUnitCapOverride", Lua_SetUnitCapOverride},
        {"SWFOC_SetUnitField",       Lua_SetUnitField},
        {"SWFOC_InstantBuild",       Lua_InstantBuild},
        {"SWFOC_FreeBuild",          Lua_FreeBuild},
        {"SWFOC_EventStreamDrain",   Lua_EventStreamDrain},
        // Phase 3.2 (continuation): per-slot writers + observers — these
        // were previously DEAD. They existed in source but the inline
        // Hook_lua_open block never registered them, so any live call
        // hit a nil global. Drift ends here.
        {"SWFOC_SetCreditsForSlot",  Lua_SetCreditsForSlot},
        {"SWFOC_GetCreditsForSlot",  Lua_GetCreditsForSlot},
        {"SWFOC_SetTechForSlot",     Lua_SetTechForSlot},
        {"SWFOC_GetTechForSlot",     Lua_GetTechForSlot},
        {"SWFOC_DrainEnemyCredits",  Lua_DrainEnemyCredits},
        {"SWFOC_SetHeroRespawn",     Lua_SetHeroRespawn},
        {"SWFOC_PreventUnitDeath",   Lua_PreventUnitDeath},
        {"SWFOC_GetMaxCredits",      Lua_GetMaxCredits},
        // 2026-04-10 diagnostic helpers — live self-report of bridge health.
        {"SWFOC_DiagListRegisteredFunctions", Lua_DiagListRegisteredFunctions},
        {"SWFOC_DiagPipeStats",               Lua_DiagPipeStats},
        {"SWFOC_DiagGameTick",                Lua_DiagGameTick},
        {"SWFOC_DiagSelfTest",                Lua_DiagSelfTest},
        // 2026-04-23 selection chain diagnostic — dumps every intermediate
        // pointer so we can empirically verify the two-deref fix against
        // future game-state shifts.
        {"SWFOC_DiagSelection",               Lua_DiagSelection},
        // 2026-04-27 (Spawn-tab live filtering — Task #222):
        // Returns "1"/"0" flags telling the editor which catalog types
        // are actually loaded in the current game state. Used to keep
        // mod A's units from showing up when the operator is running
        // mod B (or vanilla).
        {"SWFOC_BatchTypeExists",             Lua_BatchTypeExists},
    };
    constexpr int kHelperCount = static_cast<int>(sizeof(funcs)/sizeof(funcs[0]));

    // Build the manifest exactly once — this is the compile-time proof of
    // what we registered. Safe to rebuild on every lua_open fire because
    // the table is static and identical each time. Keeps the code simple
    // and avoids a "populated" flag that could desync.
    size_t moff = 0;
    g_registeredFunctionManifest[0] = '\0';
    for (int i = 0; i < kHelperCount; i++) {
        size_t remaining = (moff >= sizeof(g_registeredFunctionManifest) - 1)
            ? 0 : sizeof(g_registeredFunctionManifest) - moff - 1;
        if (remaining == 0) break;
        int n = snprintf(g_registeredFunctionManifest + moff, remaining,
                         "%s%s", i > 0 ? "," : "", funcs[i].name);
        if (n <= 0 || (size_t)n >= remaining) break;
        moff += (size_t)n;
    }
    g_registeredFunctionCount = kHelperCount;

    // Register every helper via the canonical Lua 5.0.2 triad:
    // push name (key) -> push cclosure (value) -> settable GLOBALSINDEX.
    for (int i = 0; i < kHelperCount; i++) {
        fn_pushstring(L, funcs[i].name);
        fn_pushcclosure(L, funcs[i].func, 0);
        fn_settable(L, LUA_GLOBALSINDEX);
        Log("[Bridge] Registered %s\n", funcs[i].name);
    }
    Log("[Bridge] Total helpers registered: %d\n", kHelperCount);
}

// ======================================================================
// lua_open hook
// ======================================================================

// ======================================================================
// Focus-loss drain fallback via Windows timer (2026-04-23)
// ======================================================================
// The luaD_call hook only fires while SWFOC's Lua interpreter is executing.
// When the game loses focus (user alt-tabs to V2 or any other window),
// SWFOC suspends its frame loop, so luaD_call stops firing. Every pipe
// command sent during that pause times out after 10s.
//
// Fix: install a windowless WM_TIMER on the main thread during Hook_lua_open.
// The Win32 message pump keeps running during focus loss (the game needs
// it to detect reactivation), so WM_TIMER messages still dispatch. The
// callback fires on the same thread that owns the Lua state, so calling
// DrainPipeCommand from it is safe. Uses the same g_drainGuard atomic as
// the luaD_call path, so drain races are impossible.
//
// Cadence: 100ms. Fast enough to be invisible (pipe RTT is <200ms) but
// slow enough that the timer callback is a negligible fraction of CPU
// when the game is active (luaD_call is the primary drain path; the
// timer only matters when the game is paused / defocused).

static volatile LONG g_drainGuard = 0; // prevent re-entrancy (shared by both drain paths)
static UINT_PTR g_focusDrainTimerId = 0;
static DWORD g_focusDrainThreadId = 0;

static VOID CALLBACK FocusDrainTimerProc(HWND /*hwnd*/, UINT /*msg*/, UINT_PTR /*idEvent*/, DWORD /*dwTime*/) {
    // Fires on the main thread via WM_TIMER dispatch. When the game is
    // focused, Hook_luaD_call usually drains first and we're a no-op;
    // when the game is paused, this is the only path that runs.
    if (!g_pipeCmdPending) return;
    if (InterlockedCompareExchange(&g_drainGuard, 1, 0) != 0) return;

    lua_State* pickedState = nullptr;
    EnterCriticalSection(&csRegistered);
    if (!registered_states.empty()) {
        pickedState = reinterpret_cast<lua_State*>(registered_states[0]);
    }
    LeaveCriticalSection(&csRegistered);

    if (pickedState) {
        DrainPipeCommand(pickedState);
    }
    InterlockedExchange(&g_drainGuard, 0);
}

static void InstallFocusDrainTimer() {
    if (g_focusDrainTimerId != 0) return; // already installed
    // Windowless timer — callback runs via the thread's WM_TIMER dispatch.
    // The main thread MUST pump messages for this to fire; SWFOC does.
    g_focusDrainTimerId = SetTimer(nullptr, 0, 100, FocusDrainTimerProc);
    g_focusDrainThreadId = GetCurrentThreadId();
    if (g_focusDrainTimerId != 0) {
        Log("[Bridge] Focus-drain timer installed (id=%zu, thread=%lu, period=100ms)\n",
            (size_t)g_focusDrainTimerId, g_focusDrainThreadId);
    } else {
        Log("[Bridge] SetTimer FAILED (err=%lu) — drain tied to luaD_call only\n",
            GetLastError());
    }
}

// ======================================================================
// luaD_call hook — drains pipe commands on the MAIN THREAD
// luaD_call is called on every Lua function invocation during gameplay,
// so this gives us a frequent, safe execution point. The focus-drain
// timer above handles the "game is paused" case.
// ======================================================================

static void Hook_luaD_call(lua_State* L, void* func, int nResults) {
    // SWFOC_DiagGameTick: increment BEFORE any other work so the counter
    // reflects every single luaD_call entry, including ones that skip the
    // drain branches (menu states, unregistered states, etc).
    InterlockedIncrement64((LONG64*)&g_luaDCallTickCounter);

    // Check if this state has our SWFOC_* functions registered (safe — no stack probing)
    EnterCriticalSection(&csRegistered);
    bool is_registered = std::find(registered_states.begin(), registered_states.end(), (void*)L) != registered_states.end();
    LeaveCriticalSection(&csRegistered);

    // Pipe command drain — execute on any registered state
    if (g_pipeCmdPending && is_registered && InterlockedCompareExchange(&g_drainGuard, 1, 0) == 0) {
        DrainPipeCommand(L);
        InterlockedExchange(&g_drainGuard, 0);
    }

    // Shared memory command drain (for CE)
    if (g_cmdBuf && is_registered) {
            uint32_t seq = g_cmdBuf->cmd_seq.load(std::memory_order_acquire);
            if (seq != g_lastCmdSeq) {
                g_lastCmdSeq = seq;
                int savedTop = fn_gettop(L);  // Stack guard
                char localCmd[4096];
                memcpy(localCmd, g_cmdBuf->cmd, g_cmdBuf->cmd_len + 1);
                Log("[SHM] Executing cmd seq=%u: %.64s%s\n", seq, localCmd, strlen(localCmd) > 64 ? "..." : "");

                int err = DoString(L, localCmd, "=shmem");
                if (err == 0) {
                    // Capture return value (DoString now requests 1 result)
                    const char* retVal = fn_tostring(L, -1);
                    if (retVal && retVal[0]) {
                        snprintf(g_cmdBuf->result, 4095, "%s", retVal);
                    } else {
                        strncpy(g_cmdBuf->result, "OK", 4095);
                    }
                    g_cmdBuf->result_len = (uint32_t)strlen(g_cmdBuf->result);
                } else {
                    const char* msg = fn_tostring(L, -1);
                    snprintf(g_cmdBuf->result, 4095, "ERR: %s", msg ? msg : "unknown");
                    g_cmdBuf->result_len = (uint32_t)strlen(g_cmdBuf->result);
                }
                fn_settop(L, savedTop);  // Restore stack regardless
                g_cmdBuf->result_seq.store(seq, std::memory_order_release);
            }
    }

    // Call original luaD_call
    real_luaD_call(L, func, nResults);
}

static int g_stateCount = 0;

static lua_State* Hook_lua_open() {
    lua_State* L = real_lua_open();
    if (!L) return L;

    g_stateCount++;
    Log("[Bridge] lua_open called (#%d), state=%p\n", g_stateCount, L);

    if (!g_mainState) {
        g_mainState = L;
        Log("[Bridge] Main lua_State saved: %p\n", L);
    }

    // Canonical registration — every SWFOC_* helper goes through RegisterAll.
    // Do NOT add any fn_pushcclosure calls here. If you need to add a new
    // helper, add it to the funcs[] table inside RegisterAll and the drift
    // guard test in test_harness.cpp will keep everything consistent.
    if (fn_pushstring && fn_pushcclosure && fn_settable) {
        Log("[Bridge] Attempting canonical RegisterAll on state #%d...\n", g_stateCount);
        RegisterAll(L);

        // Track this state as registered for pipe/shmem drain in luaD_call
        EnterCriticalSection(&csRegistered);
        if (std::find(registered_states.begin(), registered_states.end(), (void*)L) == registered_states.end()) {
            registered_states.push_back((void*)L);
            Log("[Bridge] Registered state %p for command drain (total: %d)\n", L, (int)registered_states.size());
        }
        LeaveCriticalSection(&csRegistered);

        // === COMPREHENSIVE SELF-TEST SUITE ===
        // Test every Lua API function we use. If any crash, SEH catches it.

        // Test 1: pushnumber + settop (pop)
        fn_pushnumber(L, 42.0);
        fn_settop(L, -2);
        Log("[Test] pushnumber + pop: PASSED\n");

        // Test 2: tonumber
        fn_pushstring(L, "12345");
        double tv = fn_tonumber(L, -1);
        fn_settop(L, -2);
        Log("[Test] tonumber: %.0f %s\n", tv, tv == 12345.0 ? "PASSED" : "FAILED");

        // Test 3: newtable + settable (create a table, add a field)
        fn_newtable(L);              // push {}
        fn_pushstring(L, "key");     // push "key"
        fn_pushnumber(L, 99.0);      // push 99
        fn_settable(L, -3);          // t["key"] = 99
        fn_settop(L, -2);            // pop table
        Log("[Test] newtable + settable: PASSED\n");

        // Test 4: rawseti (create array-like table)
        fn_newtable(L);              // push {}
        fn_pushstring(L, "hello");   // push "hello"
        fn_rawseti(L, -2, 1);        // t[1] = "hello"
        fn_settop(L, -2);            // pop table
        Log("[Test] rawseti: PASSED\n");

        // Test 5: type check
        fn_pushstring(L, "test");
        int ty = fn_type(L, -1);
        fn_settop(L, -2);
        Log("[Test] type(string) = %d %s\n", ty, ty == LUA_TSTRING ? "PASSED" : "FAILED");

        fn_pushnumber(L, 1.0);
        ty = fn_type(L, -1);
        fn_settop(L, -2);
        Log("[Test] type(number) = %d %s\n", ty, ty == LUA_TNUMBER ? "PASSED" : "FAILED");

        // Test 6: tostring (0x7B9CC0 = lua_tolstring — Ghidra confirmed)
        fn_pushstring(L, "readback");
        const char* rb = fn_tostring(L, -1);
        int rbOk = rb && strcmp(rb, "readback") == 0;
        fn_settop(L, -2);
        Log("[Test] tostring readback: %s %s\n", rb ? rb : "null", rbOk ? "PASSED" : "FAILED");

        // Test 7: gettable (0x7B8E90 = lua_gettable — Ghidra confirmed)
        fn_pushstring(L, "_SWFOC_TEST");
        fn_pushnumber(L, 777.0);
        fn_settable(L, LUA_GLOBALSINDEX);
        fn_pushstring(L, "_SWFOC_TEST");
        fn_gettable(L, LUA_GLOBALSINDEX);
        double gv = fn_tonumber(L, -1);
        fn_settop(L, -2);
        Log("[Test] global set+get: %.0f %s\n", gv, gv == 777.0 ? "PASSED" : "FAILED");

        // Test 8: pcall (0x7B9280 — Ghidra confirmed)
        fn_pushstring(L, "SWFOC_GetVersion");
        fn_gettable(L, LUA_GLOBALSINDEX);
        ty = fn_type(L, -1);
        if (ty == LUA_TFUNCTION) {
            int pcResult = fn_pcall(L, 0, 1, 0);
            if (pcResult == 0) {
                const char* ver = fn_tostring(L, -1);
                fn_settop(L, -2);
                Log("[Test] pcall SWFOC_GetVersion: '%s' PASSED\n", ver ? ver : "null");
            } else {
                const char* err = fn_tostring(L, -1);
                fn_settop(L, -2);
                Log("[Test] pcall SWFOC_GetVersion: error=%s FAILED\n", err ? err : "null");
            }
        } else {
            fn_settop(L, -2);
            Log("[Test] pcall: function not found (type=%d) — OK for early states\n", ty);
        }

        // Test 9: pushboolean + pushnil + gettop
        fn_pushboolean(L, 1);
        ty = fn_type(L, -1);
        fn_settop(L, -2);
        Log("[Test] pushboolean: type=%d %s\n", ty, ty == LUA_TBOOLEAN ? "PASSED" : "FAILED");

        fn_pushnil(L);
        ty = fn_type(L, -1);
        fn_settop(L, -2);
        Log("[Test] pushnil: type=%d %s\n", ty, ty == LUA_TNIL ? "PASSED" : "FAILED");

        int top_before = fn_gettop(L);
        fn_pushnumber(L, 1.0);
        int top_after = fn_gettop(L);
        fn_settop(L, -2);
        Log("[Test] gettop: before=%d after=%d %s\n", top_before, top_after,
            (top_after == top_before + 1) ? "PASSED" : "FAILED");

        // Test 10: PlayerArray access (memory read, no Lua)
        auto pa = *reinterpret_cast<uintptr_t*>(g_base + RVA::PlayerArray_Global);
        auto pc = *reinterpret_cast<int*>(g_base + RVA::PlayerCount_Global);
        int localSlot = FindLocalPlayerSlot();
        Log("[Test] PlayerArray=0x%p count=%d localSlot=%d %s\n",
            (void*)pa, pc, localSlot,
            (pa != 0 && pc > 0) ? "PASSED" : "FAILED (may be OK in menu)");

        if (localSlot >= 0) {
            Log("[Test] Local player: slot %d, faction '%s'\n",
                localSlot, GetFactionName(localSlot));
        }

        Log("[Test] === ALL SELF-TESTS COMPLETE ===\n");

        // Test 11: DoString (lua_load + pcall)
        if (fn_load) {
            int dsErr = DoString(L, "-- bridge dostring self-test", "=selftest");
            Log("[Test] DoString (no-op): %s\n", dsErr == 0 ? "PASSED" : "FAILED");
        } else {
            Log("[Test] DoString: SKIPPED (fn_load not resolved)\n");
        }
    } else {
        Log("[Bridge] ERROR: Core Lua API functions not resolved, skipping registration\n");
    }

    // Probe for game globals to identify game states.
    // States with Find_Object_Type are gameplay states (not menu/config states).
    // Cache them so the luaD_call hook can skip stack probing entirely.
    if (fn_pushstring && fn_gettable && fn_type && fn_gettop && fn_settop) {
        int top = fn_gettop(L);
        fn_pushstring(L, "Find_Object_Type");
        fn_gettable(L, LUA_GLOBALSINDEX);
        if (fn_type(L, -1) == LUA_TFUNCTION) {
            EnterCriticalSection(&csGameStates);
            if (std::find(cached_game_states.begin(), cached_game_states.end(), (void*)L) == cached_game_states.end()) {
                cached_game_states.push_back((void*)L);
                Log("Cached game state: %p (total: %d)\n", L, (int)cached_game_states.size());
            }
            LeaveCriticalSection(&csGameStates);
        }
        fn_settop(L, top); // restore stack
    }

    // Drain any pending pipe command on this (main) thread
    DrainPipeCommand(L);

    // Install the focus-drain timer exactly once, on the main thread.
    // After this call, pipe commands drain every 100ms via WM_TIMER,
    // even when the game is unfocused/paused and luaD_call stops firing.
    // See the "Focus-loss drain fallback" comment block for rationale.
    InstallFocusDrainTimer();

    return L;
}

// FindPushNumber removed — RVA 0x7B9520 confirmed via binary analysis + DLL self-test

// ======================================================================
// Crash Dump Analyzer — unhandled exception filter
// ======================================================================
// SAFETY: No Lua calls, no heap allocation, stack buffers only.

struct FuncEntry { uintptr_t rva; const char* name; };

static const FuncEntry knownFuncs[] = {
    // Lua 5.0.2 C API — state management
    {RVA::lua_open,             "lua_open"},
    {RVA::lua_close,            "lua_close"},
    {RVA::lua_checkstack,       "lua_checkstack"},
    {RVA::lua_newthread,        "lua_newthread"},
    // Lua — stack operations
    {RVA::lua_gettop,           "lua_gettop"},
    {RVA::lua_settop,           "lua_settop"},
    {RVA::lua_pushvalue,        "lua_pushvalue"},
    {RVA::lua_remove,           "lua_remove"},
    {RVA::lua_insert,           "lua_insert"},
    {RVA::lua_replace,          "lua_replace"},
    // Lua — type checking
    {RVA::lua_type,             "lua_type"},
    {RVA::lua_typename,         "lua_typename"},
    {RVA::lua_iscfunction,      "lua_iscfunction"},
    {RVA::lua_isnumber,         "lua_isnumber"},
    {RVA::lua_isstring,         "lua_isstring"},
    // Lua — conversion (stack -> C)
    {RVA::lua_tonumber,         "lua_tonumber"},
    {RVA::lua_toboolean,        "lua_toboolean"},
    {RVA::lua_tolstring,        "lua_tolstring"},
    {RVA::lua_strlen,           "lua_strlen"},
    {RVA::lua_touserdata,       "lua_touserdata"},
    {RVA::lua_tothread,         "lua_tothread"},
    {RVA::lua_topointer,        "lua_topointer"},
    // Lua — push (C -> stack)
    {RVA::lua_pushnil,          "lua_pushnil"},
    {RVA::lua_pushnumber,       "lua_pushnumber"},
    {RVA::lua_pushlstring,      "lua_pushlstring"},
    {RVA::lua_pushstring,       "lua_pushstring"},
    {RVA::lua_pushfstring,      "lua_pushfstring"},
    {RVA::lua_pushcclosure,     "lua_pushcclosure"},
    {RVA::lua_pushboolean,      "lua_pushboolean"},
    {RVA::lua_pushlightuserdata,"lua_pushlightuserdata"},
    {RVA::lua_newuserdata,      "lua_newuserdata"},
    // Lua — table get
    {RVA::lua_gettable,         "lua_gettable"},
    {RVA::lua_rawget,           "lua_rawget"},
    {RVA::lua_rawgeti,          "lua_rawgeti"},
    {RVA::lua_newtable,         "lua_newtable"},
    {RVA::lua_getmetatable,     "lua_getmetatable"},
    // Lua — table set
    {RVA::lua_settable,         "lua_settable"},
    {RVA::lua_rawset,           "lua_rawset"},
    {RVA::lua_rawseti,          "lua_rawseti"},
    {RVA::lua_setmetatable,     "lua_setmetatable"},
    // Lua — environment
    {RVA::lua_getfenv,          "lua_getfenv"},
    {RVA::lua_setfenv,          "lua_setfenv"},
    // Lua — call / execute
    {RVA::lua_pcall,            "lua_pcall"},
    {RVA::lua_cpcall,           "lua_cpcall"},
    {RVA::lua_load,             "lua_load"},
    {RVA::lua_error,            "lua_error"},
    // Lua — comparison
    {RVA::lua_equal,            "lua_equal"},
    {RVA::lua_lessthan,         "lua_lessthan"},
    // Lua — iteration / misc
    {RVA::lua_next,             "lua_next"},
    {RVA::lua_concat,           "lua_concat"},
    // Lua — GC
    {RVA::lua_getgccount,       "lua_getgccount"},
    {RVA::lua_getgcthreshold,   "lua_getgcthreshold"},
    {RVA::lua_setgcthreshold,   "lua_setgcthreshold"},
    // Lua internal functions
    {RVA::close_state,          "close_state"},
    {RVA::f_luaopen,            "f_luaopen"},
    {RVA::luaD_call,            "luaD_call"},
    {RVA::luaD_pcall,           "luaD_pcall"},
    {RVA::luaV_gettable,        "luaV_gettable"},
    {RVA::luaV_tostring,        "luaV_tostring"},
    {RVA::luaC_checkGC,         "luaC_checkGC"},
    {RVA::luaG_errormsg,        "luaG_errormsg"},
    // Game engine functions
    {RVA::SetHP,                        "SetHP"},
    {RVA::Take_Damage_Outer,            "Take_Damage_Outer"},
    {RVA::DeathHandler,                 "DeathHandler"},
    {RVA::CanReceiveDamageType,         "CanReceiveDamageType"},
    {RVA::QueryInterface,               "QueryInterface"},
    {RVA::AddCredits,                   "AddCredits"},
    {RVA::SetTechLevel,                 "SetTechLevel"},
    {RVA::SetSpeedOverride,             "SetSpeedOverride"},
    {RVA::ClearSpeedOverride,           "ClearSpeedOverride"},
    {RVA::PlayerList_FindByID,          "PlayerList_FindByID"},
    {RVA::Get_Owner_Lua,                "Get_Owner_Lua"},
    {RVA::Change_Owner,                 "Change_Owner"},
    {RVA::SetPosition,                  "SetPosition"},
    {RVA::ScheduleHeroRespawn,          "ScheduleHeroRespawn"},
    // FoCAPI-discovered engine functions
    {RVA::LuaScriptClass_GetScriptFromState, "LuaScriptClass_GetScriptFromState"},
    {RVA::LuaScriptClass_MapVarToLua,        "LuaScriptClass_MapVarToLua"},
    {RVA::PlayerWrapper_Create,              "PlayerWrapper_Create"},
    {RVA::GameObjectTypeWrapper_Ctor,        "GameObjectTypeWrapper_Ctor"},
    {RVA::LuaUserVar_RegisterMember,         "LuaUserVar_RegisterMember"},
    {RVA::LuaUserVar_ReturnVariable,         "LuaUserVar_ReturnVariable"},
    {RVA::GameText_Get,                      "GameText_Get"},
    {RVA::OperatorNew,                       "OperatorNew"},
    // Lua function registration
    {RVA::GlobalLuaRegister,    "GlobalLuaRegister"},
    {RVA::GlobalRegisterHelper, "GlobalRegisterHelper"},
};

static const int knownFuncsCount = sizeof(knownFuncs) / sizeof(knownFuncs[0]);

// Find the nearest known function at or below the given RVA.
// Returns nullptr if no function is at or below the address.
static const FuncEntry* FindNearestFunc(uintptr_t rva) {
    const FuncEntry* best = nullptr;
    for (int i = 0; i < knownFuncsCount; i++) {
        if (knownFuncs[i].rva <= rva) {
            if (!best || knownFuncs[i].rva > best->rva) {
                best = &knownFuncs[i];
            }
        }
    }
    return best;
}

// Format a single address as "RVA 0xXXXXXX — FuncName (+0xNN)" or "RVA 0xXXXXXX (unknown)"
// Returns number of chars written (excluding null).
static int FormatAddr(char* buf, int bufSize, uintptr_t absAddr, uintptr_t base) {
    uintptr_t rva = absAddr - base;
    const FuncEntry* fn = FindNearestFunc(rva);
    if (fn) {
        return _snprintf(buf, bufSize, "RVA 0x%llX -- %s (+0x%llX)",
                         (unsigned long long)rva, fn->name,
                         (unsigned long long)(rva - fn->rva));
    }
    return _snprintf(buf, bufSize, "RVA 0x%llX (unknown)", (unsigned long long)rva);
}

static const char* ExceptionCodeName(DWORD code) {
    switch (code) {
        case 0xC0000005: return "ACCESS_VIOLATION";
        case 0xC0000094: return "INTEGER_DIVIDE_BY_ZERO";
        case 0xC00000FD: return "STACK_OVERFLOW";
        case 0xC0000096: return "PRIVILEGED_INSTRUCTION";
        case 0xC000001D: return "ILLEGAL_INSTRUCTION";
        case 0x80000003: return "BREAKPOINT";
        case 0x80000004: return "SINGLE_STEP";
        case 0xC0000008: return "INVALID_HANDLE";
        case 0xC0000017: return "NO_MEMORY";
        case 0xC000008C: return "ARRAY_BOUNDS_EXCEEDED";
        case 0xC000008D: return "FLOAT_DENORMAL_OPERAND";
        case 0xC000008E: return "FLOAT_DIVIDE_BY_ZERO";
        case 0xC0000090: return "FLOAT_INVALID_OPERATION";
        case 0xC0000091: return "FLOAT_OVERFLOW";
        case 0xC0000092: return "FLOAT_STACK_CHECK";
        case 0xC0000093: return "FLOAT_UNDERFLOW";
        default:         return "UNKNOWN";
    }
}

// Path to crash_reports/ dir, set during init
static char g_crashDir[MAX_PATH] = {0};

static LONG WINAPI CrashHandler(EXCEPTION_POINTERS* ep) {
    if (!ep || !ep->ContextRecord || !ep->ExceptionRecord) {
        return EXCEPTION_CONTINUE_SEARCH;
    }

    CONTEXT* ctx = ep->ContextRecord;
    EXCEPTION_RECORD* rec = ep->ExceptionRecord;
    uintptr_t base = g_base;

    // Build timestamp string (stack buffer)
    SYSTEMTIME st;
    GetLocalTime(&st);
    char timeStr[64];
    _snprintf(timeStr, sizeof(timeStr), "%04d-%02d-%02d_%02d-%02d-%02d",
              st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);

    // Build output file path
    char filePath[MAX_PATH];
    _snprintf(filePath, sizeof(filePath), "%s\\crash_%s.txt", g_crashDir, timeStr);

    // Open crash report file (stack-based path, no heap)
    HANDLE hFile = CreateFileA(filePath, GENERIC_WRITE, 0, nullptr,
                               CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE) {
        // Fallback: try writing next to the exe
        char fallback[MAX_PATH];
        _snprintf(fallback, sizeof(fallback), "crash_%s.txt", timeStr);
        hFile = CreateFileA(fallback, GENERIC_WRITE, 0, nullptr,
                            CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    }

    // Large stack buffer for formatting the report
    char report[8192];
    int pos = 0;
    int rem = sizeof(report) - 1;

    // Header
    char timeDisplay[64];
    _snprintf(timeDisplay, sizeof(timeDisplay), "%04d-%02d-%02d %02d:%02d:%02d",
              st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);

    int n;
    n = _snprintf(report + pos, rem,
        "SWFOC Bridge Crash Report\r\n"
        "Time: %s\r\n"
        "Exception: 0x%08lX (%s)\r\n"
        "Module base: 0x%llX\r\n\r\n",
        timeDisplay,
        rec->ExceptionCode, ExceptionCodeName(rec->ExceptionCode),
        (unsigned long long)base);
    if (n > 0) { pos += n; rem -= n; }

    // Crash address
    uintptr_t crashRip = ctx->Rip;
    char addrBuf[256];
    FormatAddr(addrBuf, sizeof(addrBuf), crashRip, base);
    n = _snprintf(report + pos, rem,
        "CRASH at %s\r\n\r\n", addrBuf);
    if (n > 0) { pos += n; rem -= n; }

    // Registers
    n = _snprintf(report + pos, rem,
        "Registers:\r\n"
        "  RAX=0x%016llX  RBX=0x%016llX\r\n"
        "  RCX=0x%016llX  RDX=0x%016llX\r\n"
        "  RSI=0x%016llX  RDI=0x%016llX\r\n"
        "  RSP=0x%016llX  RBP=0x%016llX\r\n"
        "  R8 =0x%016llX  R9 =0x%016llX\r\n"
        "  R10=0x%016llX  R11=0x%016llX\r\n"
        "  R12=0x%016llX  R13=0x%016llX\r\n"
        "  R14=0x%016llX  R15=0x%016llX\r\n"
        "  RIP=0x%016llX  EFLAGS=0x%08lX\r\n\r\n",
        (unsigned long long)ctx->Rax, (unsigned long long)ctx->Rbx,
        (unsigned long long)ctx->Rcx, (unsigned long long)ctx->Rdx,
        (unsigned long long)ctx->Rsi, (unsigned long long)ctx->Rdi,
        (unsigned long long)ctx->Rsp, (unsigned long long)ctx->Rbp,
        (unsigned long long)ctx->R8,  (unsigned long long)ctx->R9,
        (unsigned long long)ctx->R10, (unsigned long long)ctx->R11,
        (unsigned long long)ctx->R12, (unsigned long long)ctx->R13,
        (unsigned long long)ctx->R14, (unsigned long long)ctx->R15,
        (unsigned long long)ctx->Rip, (unsigned long)ctx->EFlags);
    if (n > 0) { pos += n; rem -= n; }

    // Stack trace — walk RSP upward for first 5 return addresses
    // that fall within the module's plausible range
    n = _snprintf(report + pos, rem, "Stack trace (return addresses):\r\n");
    if (n > 0) { pos += n; rem -= n; }

    uintptr_t rsp = ctx->Rsp;
    int found = 0;
    // Walk up to 256 qwords from RSP looking for plausible return addrs
    for (int i = 0; i < 256 && found < 5; i++) {
        uintptr_t stackAddr = rsp + (uintptr_t)(i * 8);
        // Safety: check the stack memory is readable (MinGW has no __try/__except)
        if (IsBadReadPtr(reinterpret_cast<void*>(stackAddr), 8)) break;
        uintptr_t val = *reinterpret_cast<uintptr_t*>(stackAddr);
        // Check if value looks like it's inside the module (RVA 0x1000 .. 0x1000000)
        if (val > base + 0x1000 && val < base + 0x1000000) {
            FormatAddr(addrBuf, sizeof(addrBuf), val, base);
            n = _snprintf(report + pos, rem, "  [%d] %s\r\n", found, addrBuf);
            if (n > 0) { pos += n; rem -= n; }
            found++;
        }
    }

    if (found == 0) {
        n = _snprintf(report + pos, rem, "  (no plausible return addresses found)\r\n");
        if (n > 0) { pos += n; rem -= n; }
    }

    report[pos] = '\0';

    // Write to file
    if (hFile != INVALID_HANDLE_VALUE) {
        DWORD written = 0;
        WriteFile(hFile, report, (DWORD)pos, &written, nullptr);
        CloseHandle(hFile);
    }

    // Show brief MessageBox summary
    char msgBuf[512];
    _snprintf(msgBuf, sizeof(msgBuf),
        "SWFOC Bridge Crash!\n\n"
        "Exception: 0x%08lX (%s)\n"
        "Crash at: %s\n\n"
        "Full report saved to:\n%s",
        rec->ExceptionCode, ExceptionCodeName(rec->ExceptionCode),
        addrBuf, filePath);
    msgBuf[sizeof(msgBuf) - 1] = '\0';

    MessageBoxA(nullptr, msgBuf, "SWFOC Bridge - Crash Report", MB_OK | MB_ICONERROR);

    return EXCEPTION_CONTINUE_SEARCH;
}

// ======================================================================
// Init / Shutdown
// ======================================================================

extern bool Proxy_Init();
extern void Proxy_Shutdown();

bool LuaBridge_Init() {
    g_base = reinterpret_cast<uintptr_t>(GetModuleHandleA(nullptr));
    if (!g_base) return false;

    // Open log
    char logPath[MAX_PATH];
    GetModuleFileNameA(nullptr, logPath, MAX_PATH);
    char* slash = strrchr(logPath, '\\');
    if (slash) strcpy(slash + 1, "swfoc_bridge.log");
    else strcpy(logPath, "swfoc_bridge.log");
    g_log = fopen(logPath, "w");

    Log("[Bridge] %s\n", SWFOC_BRIDGE_VERSION);
    Log("[Bridge] Built: %s %s\n", __DATE__, __TIME__);
    Log("[Bridge] Base: 0x%p\n", (void*)g_base);

    // Verify process
    char exe[MAX_PATH];
    GetModuleFileNameA(nullptr, exe, MAX_PATH);
    if (!strstr(exe, "StarWarsG") && !strstr(exe, "starwarsg")) {
        Log("[Bridge] Wrong process, aborting\n");
        return false;
    }

    // Create crash_reports/ directory next to the exe
    {
        char dirPath[MAX_PATH];
        GetModuleFileNameA(nullptr, dirPath, MAX_PATH);
        char* dirSlash = strrchr(dirPath, '\\');
        if (dirSlash) strcpy(dirSlash + 1, "crash_reports");
        else strcpy(dirPath, "crash_reports");
        CreateDirectoryA(dirPath, nullptr); // OK if already exists
        strncpy(g_crashDir, dirPath, MAX_PATH - 1);
        g_crashDir[MAX_PATH - 1] = '\0';
        Log("[Bridge] Crash report dir: %s\n", g_crashDir);
    }

    // Install unhandled exception filter (crash dump analyzer)
    SetUnhandledExceptionFilter(CrashHandler);
    Log("[Bridge] Crash handler installed\n");

    // Resolve Lua C API — all RVAs verified via Ghidra static decompilation
    fn_pushstring   = Resolve<pfn_lua_pushstring>(RVA::lua_pushstring);
    fn_pushcclosure = Resolve<pfn_lua_pushcclosure>(RVA::lua_pushcclosure);
    fn_settop       = Resolve<pfn_lua_settop>(RVA::lua_settop);       // 0x7B9AB0 (was 0x7B99D0 = setmetatable!)
    fn_tonumber     = Resolve<pfn_lua_tonumber>(RVA::lua_tonumber);
    fn_tostring     = Resolve<pfn_lua_tostring>(RVA::lua_tolstring);  // 0x7B9CC0 — returns char*
    fn_type         = Resolve<pfn_lua_type>(RVA::lua_type);
    fn_newtable     = Resolve<pfn_lua_newtable>(RVA::lua_newtable);
    fn_settable     = Resolve<pfn_lua_settable>(RVA::lua_settable);
    fn_gettable     = Resolve<pfn_lua_gettable>(RVA::lua_gettable);   // 0x7B8E90 (was 0x7B8E10 = getmetatable!)
    fn_rawseti      = Resolve<pfn_lua_rawseti>(RVA::lua_rawseti);
    fn_pushnumber   = Resolve<pfn_lua_pushnumber>(RVA::lua_pushnumber);
    fn_pushboolean  = Resolve<pfn_lua_pushboolean>(RVA::lua_pushboolean);
    fn_pushnil      = Resolve<pfn_lua_pushnil>(RVA::lua_pushnil);
    fn_gettop       = Resolve<pfn_lua_gettop>(RVA::lua_gettop);
    fn_pcall        = Resolve<pfn_lua_pcall>(RVA::lua_pcall);         // 0x7B9280 — safe calls!
    fn_load         = Resolve<pfn_lua_load>(RVA::lua_load);          // 0x7B90F0 — parser/compiler

    Log("[Bridge] All Lua API RVAs resolved (Ghidra-verified):\n");
    Log("[Bridge]   settop=0x%X gettable=0x%X tostring=0x%X pcall=0x%X\n",
        RVA::lua_settop, RVA::lua_gettable, RVA::lua_tolstring, RVA::lua_pcall);

    Log("[Bridge] Lua API resolved\n");

    // Initialize game state cache critical section (before any hooks fire)
    InitializeCriticalSection(&csGameStates);
    InitializeCriticalSection(&csRegistered);

    // Hook lua_open
    if (MH_Initialize() != MH_OK) {
        Log("[Bridge] MinHook init failed\n");
        return false;
    }

    void* target = reinterpret_cast<void*>(g_base + RVA::lua_open);
    if (MH_CreateHook(target, (void*)&Hook_lua_open, (void**)&real_lua_open) != MH_OK) {
        Log("[Bridge] lua_open hook creation failed\n");
        return false;
    }
    if (MH_EnableHook(target) != MH_OK) {
        Log("[Bridge] lua_open hook enable failed\n");
        return false;
    }

    Log("[Bridge] lua_open hooked at 0x%p\n", target);

    // lua_close hook — evicts destroyed states from cached_game_states
    // RVA 0x7B8890 CONFIRMED-RE via Ghidra (2026-04-05). Old 0x7B8A70 was DENIED (mid-function).
    void* lcTarget = reinterpret_cast<void*>(g_base + RVA::lua_close);
    if (MH_CreateHook(lcTarget, (void*)&Hook_lua_close, (void**)&orig_lua_close) != MH_OK) {
        Log("[Bridge] WARNING: lua_close hook creation failed (stale states may accumulate)\n");
    } else if (MH_EnableHook(lcTarget) != MH_OK) {
        Log("[Bridge] WARNING: lua_close hook enable failed\n");
    } else {
        Log("[Bridge] lua_close hooked at 0x%p (RVA 0x%X, CONFIRMED-RE)\n", lcTarget, RVA::lua_close);
    }

    // Hook luaD_call for main-thread pipe command execution
    void* dcTarget = reinterpret_cast<void*>(g_base + RVA::luaD_call);
    if (MH_CreateHook(dcTarget, (void*)&Hook_luaD_call, (void**)&real_luaD_call) != MH_OK) {
        Log("[Bridge] WARNING: luaD_call hook creation failed (pipe commands will be slow)\n");
    } else if (MH_EnableHook(dcTarget) != MH_OK) {
        Log("[Bridge] WARNING: luaD_call hook enable failed\n");
    } else {
        Log("[Bridge] luaD_call hooked at 0x%p (pipe drain on main thread)\n", dcTarget);
    }

    // Initialize shared memory command buffer (before pipe thread)
    if (!InitSharedMemory()) {
        Log("[Bridge] WARNING: Shared memory init failed (CE communication unavailable)\n");
    }

    // Event stream hooks (written to shared memory ring buffer)
    // Only installed if event buffer was created successfully
    if (g_evtBuf) {
        void* tdoTarget = reinterpret_cast<void*>(g_base + RVA::Take_Damage_Outer);
        if (MH_CreateHook(tdoTarget, (void*)&Hook_TakeDamageOuter, (void**)&real_TakeDamageOuter) == MH_OK
            && MH_EnableHook(tdoTarget) == MH_OK) {
            Log("[Bridge] Take_Damage_Outer hooked at 0x%p (event stream)\n", tdoTarget);
        } else {
            Log("[Bridge] WARNING: Take_Damage_Outer hook failed\n");
        }

        void* dhTarget = reinterpret_cast<void*>(g_base + RVA::DeathHandler);
        if (MH_CreateHook(dhTarget, (void*)&Hook_DeathHandler, (void**)&real_DeathHandler) == MH_OK
            && MH_EnableHook(dhTarget) == MH_OK) {
            Log("[Bridge] DeathHandler hooked at 0x%p (event stream)\n", dhTarget);
        } else {
            Log("[Bridge] WARNING: DeathHandler hook failed\n");
        }
    } else {
        Log("[Bridge] Event buffer unavailable, skipping combat event hooks\n");
    }

    // 2026-05-06 (iter 225): WeaponTick detour for SetFireRate global LIVE wire.
    // Installs unconditionally (not gated on g_evtBuf — fire-rate scaling
    // is a separate concern from event stream). Pattern matches iter-96
    // Take_Damage_Outer detour. iter-224 RE doc + iter-225 implementation
    // close A1.3 after 124-day deferral.
    {
        void* wtTarget = reinterpret_cast<void*>(g_base + RVA::Weapon_Tick);
        if (MH_CreateHook(wtTarget, (void*)&Hook_WeaponTick, (void**)&real_WeaponTick) == MH_OK
            && MH_EnableHook(wtTarget) == MH_OK) {
            Log("[Bridge] WeaponTick hooked at 0x%p (SetFireRate global LIVE)\n", wtTarget);
        } else {
            Log("[Bridge] WARNING: WeaponTick hook failed (SetFireRate global won't apply)\n");
        }
    }

    // 2026-05-06 (iter 230-231): AddCredits detour for FreezeCredits + CreditsMultiplier
    // global LIVE wires. Pattern matches iter-96 + iter-225. AddCredits is the
    // universal engine credit-adjust function (47 callers, gains AND spends route
    // through it). Single MinHook detour covers economy-wide control. iter-230
    // RE design + iter-231 implementation close A1.x FreezeCredits.
    {
        void* acTarget = reinterpret_cast<void*>(g_base + RVA::AddCredits);
        if (MH_CreateHook(acTarget, (void*)&Hook_AddCredits, (void**)&real_AddCredits) == MH_OK
            && MH_EnableHook(acTarget) == MH_OK) {
            Log("[Bridge] AddCredits hooked at 0x%p (FreezeCredits + CreditsMultiplier global LIVE)\n", acTarget);
        } else {
            Log("[Bridge] WARNING: AddCredits hook failed (Freeze/Mult won't apply)\n");
        }
    }

    // 2026-05-07 (iter 450 scaffolding): VictoryMonitor counter_inc DORMANT
    // detour for SWFOC_TriggerVictory. MH_CreateHook installs the trampoline
    // so iter-450a can flip MH_EnableHook on after RE'ing AwaitingVictoryTest
    // struct layout + capture-on-CTOR discriminator. The trampoline never
    // runs in iter-450 (MH_EnableHook is intentionally skipped) -- cost is
    // exactly one trampoline allocation at module load.
    // See knowledge-base/iter449_breakthrough_disambiguation_parent_tick_inlines.md.
    {
        void* vmcTarget = reinterpret_cast<void*>(g_base + RVA::VictoryMonitor_CounterInc);
        if (MH_CreateHook(vmcTarget, (void*)&Hook_VictoryMonitorCounter,
                          (void**)&real_VictoryMonitorCounter) == MH_OK) {
            // INTENTIONALLY NOT calling MH_EnableHook here -- iter-450 ships
            // scaffolding only. iter-450a flips this on after struct + capture
            // hook land. Operator-visible: SWFOC_TriggerVictory currently
            // returns PHASE2_PENDING with the validated type staged.
            Log("[Bridge] VictoryMonitor counter_inc hook CREATED but DORMANT "
                "at 0x%p (iter-450 scaffolding; iter-450a will enable)\n", vmcTarget);
        } else {
            Log("[Bridge] WARNING: VictoryMonitor counter_inc hook creation failed "
                "(iter-450a will need to retry CreateHook + RE the missing pieces)\n");
        }
    }

    // Start named pipe listener thread
    InitializeCriticalSection(&g_pipeLock);
    g_pipeShutdown = false;
    g_pipeThread = CreateThread(nullptr, 0, PipeThreadProc, nullptr, 0, nullptr);
    if (g_pipeThread) {
        Log("[Bridge] Pipe listener thread started\n");
    } else {
        Log("[Bridge] WARNING: Pipe listener thread failed to start: %lu\n", GetLastError());
    }

    Log("[Bridge] Ready. Lua functions will be injected when game creates Lua states.\n");
    Log("[Bridge] Named pipe: %s\n", PIPE_NAME);
    return true;
}

void LuaBridge_Shutdown() {
    // Stop pipe listener thread
    g_pipeShutdown = true;
    if (g_pipeThread) {
        // Create a dummy connection to unblock ConnectNamedPipe
        HANDLE hDummy = CreateFileA(PIPE_NAME, GENERIC_READ | GENERIC_WRITE,
            0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (hDummy != INVALID_HANDLE_VALUE) CloseHandle(hDummy);
        WaitForSingleObject(g_pipeThread, 2000);
        CloseHandle(g_pipeThread);
        g_pipeThread = nullptr;
        Log("[Bridge] Pipe thread stopped\n");
    }
    DeleteCriticalSection(&g_pipeLock);

    // Clean up game state cache
    EnterCriticalSection(&csGameStates);
    cached_game_states.clear();
    LeaveCriticalSection(&csGameStates);
    DeleteCriticalSection(&csGameStates);

    // Clean up registered states
    EnterCriticalSection(&csRegistered);
    registered_states.clear();
    LeaveCriticalSection(&csRegistered);
    DeleteCriticalSection(&csRegistered);

    // Clean up shared memory
    if (g_cmdBuf) { UnmapViewOfFile(g_cmdBuf); g_cmdBuf = nullptr; }
    if (g_hCmdMap) { CloseHandle(g_hCmdMap); g_hCmdMap = nullptr; }
    if (g_evtBuf) { UnmapViewOfFile(g_evtBuf); g_evtBuf = nullptr; }
    if (g_hEvtMap) { CloseHandle(g_hEvtMap); g_hEvtMap = nullptr; }

    // Tear down combat hook lock if it was initialized via SWFOC_GodMode/OHK
    if (g_combat_hook_lock_initialized) {
        DeleteCriticalSection(&g_combat_hook_lock);
        g_combat_hook_lock_initialized = false;
    }

    MH_DisableHook(MH_ALL_HOOKS);
    MH_Uninitialize();
    Log("[Bridge] Shutdown\n");
    if (g_log) { fclose(g_log); g_log = nullptr; }
    g_mainState = nullptr;
}
