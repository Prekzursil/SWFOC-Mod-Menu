// fake_lua.cpp -- Stub Lua 5.0.2 C API implementations for test harness.
// Every function logs its call to L->call_log and manipulates L->stack.

#include "fake_lua.h"
#include <cstring>
#include <cstdio>
#include <algorithm>

// Lua type constants (must match lua_types.h / bridge code)
#define FL_TNIL        0
#define FL_TBOOLEAN    1
#define FL_TNUMBER     3
#define FL_TSTRING     4
#define FL_TTABLE      5
#define FL_TFUNCTION   6

// GLOBALSINDEX pseudo-index
#define FL_GLOBALSINDEX (-10001)

// ---- Internal helpers ----

// Resolve a Lua-style index (1-based positive, negative from top) to a
// zero-based vector index. Returns -1 if out of range.
static int resolve_index(FakeLuaState* L, int idx) {
    if (idx > 0) return idx - 1;                              // 1-based -> 0-based
    if (idx < 0 && idx != FL_GLOBALSINDEX) return (int)L->stack.size() + idx;
    return -1;  // GLOBALSINDEX or other pseudo-index
}

static void log_call(FakeLuaState* L, const std::string& entry) {
    L->call_log.push_back(entry);
}

// ---- API stubs ----

int fake_gettop(FakeLuaState* L) {
    log_call(L, "gettop");
    return (int)L->stack.size();
}

void fake_settop(FakeLuaState* L, int idx) {
    log_call(L, "settop(" + std::to_string(idx) + ")");
    if (idx >= 0) {
        L->stack.resize((size_t)idx);
    } else {
        // settop(-n-1) pops n elements: e.g. settop(-2) pops 1
        int newSize = (int)L->stack.size() + idx + 1;
        if (newSize < 0) newSize = 0;
        L->stack.resize((size_t)newSize);
    }
}

const char* fake_pushstring(FakeLuaState* L, const char* s) {
    log_call(L, std::string("pushstring(\"") + (s ? s : "null") + "\")");
    StackEntry e;
    e.type = FL_TSTRING;
    e.strval = s ? s : "";
    L->stack.push_back(e);
    // Return pointer to the string stored in the stack entry.
    // This is stable until the entry is popped/overwritten.
    return L->stack.back().strval.c_str();
}

void fake_pushnumber(FakeLuaState* L, double n) {
    char buf[64];
    snprintf(buf, sizeof(buf), "pushnumber(%.6g)", n);
    log_call(L, buf);
    StackEntry e;
    e.type = FL_TNUMBER;
    e.numval = n;
    L->stack.push_back(e);
}

void fake_pushboolean(FakeLuaState* L, int b) {
    log_call(L, std::string("pushboolean(") + std::to_string(b) + ")");
    StackEntry e;
    e.type = FL_TBOOLEAN;
    e.boolval = b;
    L->stack.push_back(e);
}

void fake_pushnil(FakeLuaState* L) {
    log_call(L, "pushnil");
    StackEntry e;
    e.type = FL_TNIL;
    L->stack.push_back(e);
}

void fake_pushcclosure(FakeLuaState* L, void* fn, int n) {
    log_call(L, "pushcclosure");
    // Pop n upvalues from the stack (Lua 5.0 semantics)
    for (int i = 0; i < n && !L->stack.empty(); i++)
        L->stack.pop_back();
    StackEntry e;
    e.type = FL_TFUNCTION;
    e.funcptr = fn;
    L->stack.push_back(e);
}

void fake_newtable(FakeLuaState* L) {
    log_call(L, "newtable");
    StackEntry e;
    e.type = FL_TTABLE;
    L->stack.push_back(e);
}

void fake_settable(FakeLuaState* L, int idx) {
    log_call(L, "settable(" + std::to_string(idx) + ")");
    // Stack: ... table ... key value  (top = value, top-1 = key)
    if (L->stack.size() < 2) return;

    StackEntry value = L->stack.back(); L->stack.pop_back();
    StackEntry key   = L->stack.back(); L->stack.pop_back();

    if (idx == FL_GLOBALSINDEX) {
        // Set global
        L->globals[key.strval] = value;
    }
    // For regular table indices we just pop -- we don't simulate real table storage
    // beyond globals, which is sufficient for testing the bridge.
}

void fake_gettable(FakeLuaState* L, int idx) {
    log_call(L, "gettable(" + std::to_string(idx) + ")");
    if (L->stack.empty()) return;

    StackEntry key = L->stack.back(); L->stack.pop_back();

    if (idx == FL_GLOBALSINDEX) {
        auto it = L->globals.find(key.strval);
        if (it != L->globals.end()) {
            L->stack.push_back(it->second);
        } else if (key.strval == "Find_Object_Type" && L->has_game_globals) {
            StackEntry e;
            e.type = FL_TFUNCTION;
            e.strval = "Find_Object_Type";
            L->stack.push_back(e);
        } else {
            StackEntry e;
            e.type = FL_TNIL;
            L->stack.push_back(e);
        }
    } else {
        // Non-global gettable: push nil
        StackEntry e;
        e.type = FL_TNIL;
        L->stack.push_back(e);
    }
}

void fake_rawseti(FakeLuaState* L, int idx, int n) {
    char buf[64];
    snprintf(buf, sizeof(buf), "rawseti(%d, %d)", idx, n);
    log_call(L, buf);
    // Pop the value from the top; the table at idx remains
    if (!L->stack.empty()) {
        L->stack.pop_back();
    }
}

int fake_type(FakeLuaState* L, int idx) {
    log_call(L, "type(" + std::to_string(idx) + ")");
    int ri = resolve_index(L, idx);
    if (ri < 0 || ri >= (int)L->stack.size()) return FL_TNIL;
    return L->stack[ri].type;
}

double fake_tonumber(FakeLuaState* L, int idx) {
    log_call(L, "tonumber(" + std::to_string(idx) + ")");
    int ri = resolve_index(L, idx);
    if (ri < 0 || ri >= (int)L->stack.size()) return 0.0;
    const auto& e = L->stack[ri];
    if (e.type == FL_TNUMBER) return e.numval;
    if (e.type == FL_TSTRING) {
        // Lua 5.0 coerces numeric strings
        try { return std::stod(e.strval); } catch (...) { return 0.0; }
    }
    return 0.0;
}

// We store returned c_str pointers in a static buffer so they survive the call.
// This is safe for single-threaded test usage.
static thread_local std::string s_tostring_buf;

const char* fake_tostring(FakeLuaState* L, int idx) {
    log_call(L, "tostring(" + std::to_string(idx) + ")");
    int ri = resolve_index(L, idx);
    if (ri < 0 || ri >= (int)L->stack.size()) return nullptr;
    const auto& e = L->stack[ri];
    if (e.type == FL_TSTRING) return e.strval.c_str();
    if (e.type == FL_TNUMBER) {
        char buf[64];
        snprintf(buf, sizeof(buf), "%.14g", e.numval);
        s_tostring_buf = buf;
        return s_tostring_buf.c_str();
    }
    return nullptr;
}

int fake_pcall(FakeLuaState* L, int nargs, int nresults, int errfunc) {
    char buf[128];
    snprintf(buf, sizeof(buf), "pcall(nargs=%d, nresults=%d)", nargs, nresults);
    log_call(L, buf);

    if (L->pcall_error != 0) {
        // Simulate error: pop the function + args, push error message
        int toPop = 1 + nargs;
        for (int i = 0; i < toPop && !L->stack.empty(); i++)
            L->stack.pop_back();
        StackEntry e;
        e.type = FL_TSTRING;
        e.strval = L->pcall_error_msg.empty() ? "pcall error" : L->pcall_error_msg;
        L->stack.push_back(e);
        return L->pcall_error;
    }

    // 2026-04-27: type-existence simulation. If the call shape is
    // Find_Object_Type(typename) and the typename is in known_object_types,
    // pop everything and push a fake userdata so the bridge code's
    // "rty != LUA_TNIL" branch fires. Used by Lua_BatchTypeExists tests.
    bool simulateTypeExists = false;
    if (nargs == 1 && nresults == 1 && L->stack.size() >= 2) {
        const auto& fn  = L->stack[L->stack.size() - 2];
        const auto& arg = L->stack[L->stack.size() - 1];
        bool isFindObjType =
            fn.type == FL_TFUNCTION && fn.strval == "Find_Object_Type";
        if (isFindObjType && arg.type == FL_TSTRING &&
            L->known_object_types.count(arg.strval) > 0) {
            simulateTypeExists = true;
        }
    }

    // Simulate success: pop the function + args
    int toPop = 1 + nargs;
    for (int i = 0; i < toPop && !L->stack.empty(); i++)
        L->stack.pop_back();

    // Push nresults result values. For Find_Object_Type with a known
    // typename we push a TUSERDATA-shaped entry; everything else gets nil.
    for (int i = 0; i < nresults; i++) {
        StackEntry e;
        if (simulateTypeExists && i == 0) {
            // LUA_TUSERDATA = 7 in Lua 5.0; the bridge code only checks
            // "is non-nil", so the exact tag doesn't matter, but we use
            // TFUNCTION (already defined) as a non-nil sentinel to keep
            // dependencies on lua_types.h tag values out of fake_lua.
            e.type   = FL_TFUNCTION;
            e.strval = "<type_wrapper>";
        } else {
            e.type = FL_TNIL;
        }
        L->stack.push_back(e);
    }
    return 0;
}

int fake_load(FakeLuaState* L, void* reader, void* data, const char* chunkname) {
    log_call(L, std::string("load(\"") + (chunkname ? chunkname : "") + "\")");

    if (L->load_error != 0) {
        StackEntry e;
        e.type = FL_TSTRING;
        e.strval = L->load_error_msg.empty() ? "load error" : L->load_error_msg;
        L->stack.push_back(e);
        return L->load_error;
    }

    // Success: push a fake compiled chunk (function) onto the stack
    StackEntry e;
    e.type = FL_TFUNCTION;
    e.strval = chunkname ? chunkname : "chunk";
    L->stack.push_back(e);
    return 0;
}

void fake_reset(FakeLuaState* L) {
    L->stack.clear();
    L->globals.clear();
    L->call_log.clear();
    L->has_game_globals = false;
    L->pcall_error = 0;
    L->pcall_error_msg.clear();
    L->load_error = 0;
    L->load_error_msg.clear();
}
