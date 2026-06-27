# Bridge v3 Changes: Game State Caching

## Problem

The DLL bridge hooks `luaD_call`, which fires on every Lua function invocation across 400+ Lua states. To execute pipe commands on the correct state (one with game globals like `Find_Object_Type`), the v2 bridge probed the Lua stack during every `luaD_call` invocation when a pipe command was pending. This was unreliable because:

1. The stack may be in a transient state during `luaD_call` (mid-call setup), making the probe unsafe.
2. Probing involves `pushstring` + `gettable` + `type` + `settop` on a potentially unstable stack, risking heap corruption.
3. The probe ran on every `luaD_call` while a command was pending, adding overhead to a hot path.

## Solution: Cache game states at creation time

Instead of probing the stack on every `luaD_call`, we identify game states once during `lua_open` and cache their pointers. The `luaD_call` hook then does a simple pointer lookup (O(n) on a tiny vector, typically 1-3 entries) instead of touching the Lua stack.

## Files Changed

### `lua_bridge.cpp`

**A. New includes and globals (top of file):**
- Added `<string>`, `<vector>`, `<algorithm>` includes.
- Added `CRITICAL_SECTION csGameStates` and `std::vector<void*> cached_game_states` for the cache.

**B. New hook types:**
- Added `lua_close_t` typedef and `orig_lua_close` trampoline pointer for the lua_close hook.

**C. New function: `SWFOC_StateInfo()` (line ~465):**
- Lua-callable function returning a string listing all cached game state pointers.
- Useful for debugging: call `print(SWFOC_StateInfo())` from the game console or pipe.

**D. New function: `Hook_lua_close()` (line ~484):**
- Intercepts `lua_close` to remove destroyed states from `cached_game_states`.
- Prevents stale pointers from accumulating in the cache.
- Thread-safe via `csGameStates` critical section.

**E. Modified: `Hook_luaD_call()` (line ~537):**
- **REMOVED**: Stack probing logic (`pushstring("Find_Object_Type")` + `gettable` + `type` check).
- **REPLACED WITH**: `std::find` lookup in `cached_game_states` under `csGameStates` lock.
- No Lua stack manipulation during `luaD_call` anymore -- just a pointer comparison.

**F. Modified: `Hook_lua_open()` (line ~749):**
- After registering SWFOC_* functions and running self-tests, probes for `Find_Object_Type` in the global table.
- If found (meaning this is a gameplay state, not a menu/config state), adds `L` to `cached_game_states`.
- This is safe because `lua_open` returns a fully initialized state with a stable stack.

**G. Modified: `Hook_lua_open()` function registration (line ~630):**
- Added `SWFOC_StateInfo` registration alongside other SWFOC_* functions.
- Function count bumped from 11 to 12.

**H. Modified: `RegisterAll()` (line ~499):**
- Added `SWFOC_StateInfo` to the registration table.

**I. Modified: `LuaBridge_Init()` (line ~828):**
- `InitializeCriticalSection(&csGameStates)` called before MinHook initialization.
- New `lua_close` hook installed after `lua_open` hook, before `luaD_call` hook.
- `lua_close` hook failure is non-fatal (logged as WARNING) since the RVA is estimated.

**J. Modified: `LuaBridge_Shutdown()` (line ~902):**
- Clears `cached_game_states` vector and destroys `csGameStates` critical section.

### `rvas.h`

- Added `lua_close = 0x7B8A70` with `ESTIMATED` / `NEEDS_VERIFICATION` marker.
- Address is between `lua_open` (0x7B8930) and `lua_checkstack` (0x7B8BC0), consistent with Lua 5.0.2 source layout.

## Action Required

The `lua_close` RVA (0x7B8A70) is an estimate based on source ordering. It needs Ghidra verification before the hook can be considered reliable. If the hook fails at runtime, the bridge logs a warning but continues functioning -- the only consequence is that stale cache entries may accumulate (harmless since the game typically doesn't destroy gameplay states during a session).

To verify: open StarWarsG.exe in Ghidra, navigate to 0x1407B8A70 (image base + RVA), and confirm it matches the `lua_close` signature: single parameter (lua_State*), calls `luaC_callGCTM` and `luai_close`, frees the state.

## Performance Impact

- **Hot path (`luaD_call`)**: Replaced 5 Lua C API calls (pushstring, gettable, type, settop, gettop) with one `EnterCriticalSection` + `std::find` on a 1-3 element vector + `LeaveCriticalSection`. Significantly cheaper and does not touch the Lua stack.
- **Cold path (`lua_open`)**: Added one probe per state creation (same 5 API calls that were removed from the hot path). This runs once per state, not per function call.
- **Memory**: One `void*` per game state in the cache vector (typically 1-3 entries, 8-24 bytes total).
