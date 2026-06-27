#pragma once
// fake_lua.h -- Stub Lua 5.0.2 API for offline testing of the SWFOC bridge.
// Provides a FakeLuaState with stack simulation and call logging so that
// every bridge function can be exercised without the game process.

#include <vector>
#include <map>
#include <set>
#include <string>
#include <cstdint>
#include <cstddef>

// Forward-declare so the bridge code's lua_CFunction typedef resolves.
struct FakeLuaState;

// Stack entry: every push/pop manipulates these.
struct StackEntry {
    int type;           // LUA_TNIL=0, LUA_TBOOLEAN=1, LUA_TNUMBER=3, LUA_TSTRING=4, etc.
    double numval;
    std::string strval;
    int boolval;
    void* funcptr;      // for TFUNCTION entries (closure pointer)

    StackEntry() : type(0), numval(0), strval(), boolval(0), funcptr(nullptr) {}
};

struct FakeLuaState {
    std::vector<StackEntry> stack;
    std::map<std::string, StackEntry> globals;   // Global table (GLOBALSINDEX)
    std::vector<std::string> call_log;           // Log of all API calls made

    bool has_game_globals = false;    // If true, Find_Object_Type exists as a global function
    int pcall_error = 0;             // If non-zero, pcall returns this error code
    std::string pcall_error_msg;     // Error message for failed pcall
    int load_error = 0;              // If non-zero, lua_load returns this error
    std::string load_error_msg;      // Error message for failed load

    // 2026-04-27: type-existence simulation for Lua_BatchTypeExists tests.
    // If non-empty, fake_pcall inspects the arg (top-1) of a Find_Object_Type
    // call and pushes a fake userdata if the typename is in this set, nil
    // otherwise. Lets the harness verify both the "exists" and "missing"
    // paths without injecting a real engine pointer.
    std::set<std::string> known_object_types;
};

// ---- Stub function declarations ----
// These match the signatures the bridge's function pointers expect.

int         fake_gettop(FakeLuaState* L);
void        fake_settop(FakeLuaState* L, int idx);
const char* fake_pushstring(FakeLuaState* L, const char* s);
void        fake_pushnumber(FakeLuaState* L, double n);
void        fake_pushboolean(FakeLuaState* L, int b);
void        fake_pushnil(FakeLuaState* L);
void        fake_pushcclosure(FakeLuaState* L, void* fn, int n);
void        fake_newtable(FakeLuaState* L);
void        fake_settable(FakeLuaState* L, int idx);
void        fake_gettable(FakeLuaState* L, int idx);
void        fake_rawseti(FakeLuaState* L, int idx, int n);
int         fake_type(FakeLuaState* L, int idx);
double      fake_tonumber(FakeLuaState* L, int idx);
const char* fake_tostring(FakeLuaState* L, int idx);
int         fake_pcall(FakeLuaState* L, int nargs, int nresults, int errfunc);
int         fake_load(FakeLuaState* L, void* reader, void* data, const char* chunkname);

// Helpers
void fake_reset(FakeLuaState* L);   // Clear stack, globals, call_log
