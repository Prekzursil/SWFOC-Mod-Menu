#pragma once
#include <cstdint>

// Lua 5.0.2 type definitions for StarWarsG.exe (x64)
// We don't need the full Lua headers — just the types for our function pointers.

typedef struct lua_State lua_State;

// Lua type constants (Lua 5.0.2)
#define LUA_TNONE          (-1)
#define LUA_TNIL           0
#define LUA_TBOOLEAN       1
#define LUA_TLIGHTUSERDATA 2
#define LUA_TNUMBER        3
#define LUA_TSTRING        4
#define LUA_TTABLE         5
#define LUA_TFUNCTION      6
#define LUA_TUSERDATA      7
#define LUA_TTHREAD        8

// Lua pseudo-indices (Lua 5.0.2)
#define LUA_REGISTRYINDEX  (-10000)
#define LUA_GLOBALSINDEX   (-10001)

// Lua C function type
typedef int (*lua_CFunction)(lua_State* L);

// Function pointer typedefs for the Lua C API
typedef lua_State* (*pfn_lua_open)();
typedef void       (*pfn_lua_close)(lua_State* L);
typedef void       (*pfn_lua_newtable)(lua_State* L);
typedef void       (*pfn_lua_pushcclosure)(lua_State* L, lua_CFunction fn, int n);
typedef const char*(*pfn_lua_pushstring)(lua_State* L, const char* s);
typedef void       (*pfn_lua_settable)(lua_State* L, int index);
typedef void       (*pfn_lua_rawseti)(lua_State* L, int index, int n);
typedef void       (*pfn_lua_settop)(lua_State* L, int index);
typedef const char*(*pfn_lua_tostring)(lua_State* L, int index);
typedef double     (*pfn_lua_tonumber)(lua_State* L, int index);
typedef int        (*pfn_lua_type)(lua_State* L, int index);
typedef void       (*pfn_lua_gettable)(lua_State* L, int index);
typedef void       (*pfn_lua_setglobal)(lua_State* L, const char* name);
typedef void       (*pfn_lua_pushnumber)(lua_State* L, double n);
typedef int        (*pfn_lua_pcall)(lua_State* L, int nargs, int nresults, int errfunc);
typedef void       (*pfn_lua_getglobal)(lua_State* L, const char* name);
typedef int        (*pfn_lua_toboolean)(lua_State* L, int index);
typedef void       (*pfn_lua_pushboolean)(lua_State* L, int b);
typedef void       (*pfn_lua_pushnil)(lua_State* L);
typedef int        (*pfn_lua_isstring)(lua_State* L, int index);
typedef int        (*pfn_lua_isnumber)(lua_State* L, int index);
typedef int        (*pfn_lua_gettop)(lua_State* L);

// lua_load reader callback (Lua 5.0.2)
typedef const char* (*lua_Chunkreader)(lua_State* L, void* ud, size_t* sz);

// lua_load function pointer
typedef int (*pfn_lua_load)(lua_State* L, lua_Chunkreader reader, void* data, const char* chunkname);

// Convenience: push a C function (pushcclosure with 0 upvalues)
#define lua_pushcfunction(L, f) lua_pushcclosure(L, f, 0)
// Convenience: pop n values
#define lua_pop(L, n) lua_settop(L, -(n)-1)
