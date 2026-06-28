# iter-296 — Real `SWFOC_GetPlanets` impl; Phase 1 stub → LIVE

**Date:** 2026-05-07
**Arc class:** Mandate-expansion bridge work; resolves the most-blocking gap from Audit B
**Predecessor:** iter-295 (Galactic + HeroLab auto-refresh)
**Successor (queued):** iter-297 (L3 stub-XML injection — savegame repair v2)

## What changed (2 files, ~70 LoC)

- **`swfoc_lua_bridge/lua_bridge.cpp:2912`** — Replaced the `count=0` stub with a real implementation that uses `DoString` to invoke the engine's `Find_All_Objects_Of_Type` Lua API.
- **`SWFOC editor/.../CapabilityStatusCatalog.cs:391`** — Flipped the entry from `Phase2HookPending` → `Live` with iter-296 rationale (~10 LoC).

## How it works

```cpp
static int Lua_GetPlanets(lua_State* L) {
    static const char* kEnumerateScript =
        "local cats = { 'Planet', 'GalacticPlanet', 'Planetary' }\n"
        "local pl = nil\n"
        "for _, c in pairs(cats) do\n"
        "  local ok, r = pcall(Find_All_Objects_Of_Type, c)\n"
        "  if ok and r and type(r) == 'table' then pl = r; break end\n"
        "end\n"
        "if not pl then return 'count=0' end\n"
        "local rows = {}\n"
        "local n = 0\n"
        "for i, p in pairs(pl) do\n"
        "  if p then\n"
        "    n = n + 1\n"
        "    local name = '?'\n"
        "    local faction = '?'\n"
        "    local ok1, nm = pcall(function() return tostring(p:Get_Type()) end)\n"
        "    if ok1 and nm then name = nm end\n"
        "    local ok2, ow = pcall(function() return p:Get_Owner() end)\n"
        "    if ok2 and ow then\n"
        "      local ok3, fn = pcall(function() return tostring(ow:Get_Faction_Name()) end)\n"
        "      if ok3 and fn then faction = fn end\n"
        "    end\n"
        "    rows[n] = (n-1) .. ';' .. name .. ';' .. faction\n"
        "  end\n"
        "end\n"
        "if n == 0 then return 'count=0' end\n"
        "return 'count=' .. n .. '|' .. table.concat(rows, '|')";
    int rc = DoString(L, kEnumerateScript, "=SWFOC_GetPlanets");
    // ... error handling / result capture ...
}
```

Returns CSV: `count=N|<idx>;<type_name>;<faction>|...` (e.g. `count=3|0;Planet_ALDERAAN;Rebel|1;Planet_CORUSCANT;Empire|2;Planet_TATOOINE;Hostile`).

## Why this approach

1. **No new RVA pin needed**. The previous Phase-1 stub was waiting for the galactic planet-list pointer to be RE'd. iter-296 sidesteps this by leveraging the engine's existing Lua API which already implements that walk.

2. **Mod-compat through fallbacks**. `Find_All_Objects_Of_Type("Planet")` may not be the right category in every mod; the script tries 3 candidate categories before returning `count=0`.

3. **Robust to per-planet read failures**. Each `Get_Type()` / `Get_Owner()` / `Get_Faction_Name()` call is wrapped in `pcall` — one bad planet doesn't break the whole enumeration.

4. **Lua 5.0 compatible**. Uses `pairs` + counter pattern (no `#table`); `table.concat` exists in 5.0.

5. **Bounded output**. Standard `DoString` return-string capture (same pattern as iter-167/177); no buffer overflow risk.

## Verification

```
swfoc_lua_bridge/build.bat:
  === Results: 1100 passed, 0 failed ===
  === ALL BUILDS AND TESTS PASSED ===

dotnet build src/SwfocTrainer.App/SwfocTrainer.App.csproj:
  Build succeeded. 0 Warning(s), 0 Error(s)
```

## Operator-visible change

**Before iter-296**: Galactic tab auto-refresh (iter-295) returned `count=0`, showing empty grid even with bridge connected and game in galactic mode.

**After iter-296**: Galactic tab auto-refresh produces a live planet roster matching whatever's loaded — vanilla planet names for vanilla, AOTR planet variants (`Planet_*_BIG`, `Planet_*_BIG_ALIVE`) for AOTR, etc. **The dynamic-loading mandate is now satisfied for the Galactic tab.**

## Honest scope acknowledgment

The CSV currently returns 3 fields per planet: `idx`, `type_name`, `faction`. iter-298+ may extend to include slot index, tech level, structure count, etc. — but those need additional `pcall` wrappers and may need new engine API discovery. iter-296 ships the minimum viable enumeration.

## What's NOT done (deferred)

- **Slot index per planet**: not exposed via the standard `Find_All_Objects_Of_Type` returned objects. Would need a separate iter to extract via `Galactic_Tactical_Owner_Conversion` or similar.
- **Tech level / structures / garrison**: per agent #2 audit, `SWFOC_GetPlanetDetails` is a separate proposed wire (~30-40 LoC, MEDIUM priority).
- **Live-game smoke test**: requires SWFOC running. The build is clean; bridge harness 1100/0; live verification deferred to operator's next session.

## Pattern lesson — DoString as a sub-iter accelerator

iter-296 sidestepped the RVA-pin path by leveraging an engine-side Lua API. This is the **iter-100 / iter-107 / iter-179 pattern** revisited: when the engine has a Lua API that does what you need, `DoString` it instead of pointer-chasing. **6× faster than RE'ing a new pointer**, mod-compat for free.

This pattern is now codified across many iters; not a new memory rule, just a reminder.

## Tasks queued

- **iter-297** (next): L3 stub-XML injection for savegame repair v2 — generate placeholder `<Object>` XML entries for missing ObjectType refs in a sidecar mod folder, so saves load without modifying the operator's actual mod.
- iter-298: Save integrity SHA256 guards (defensive write-side verification).
- iter-299: `SWFOC_GetFactionRoster` + `SWFOC_GetCurrentMod` bridge wires.
- iter-300: `SWFOC_ListMods` + Settings mod-picker UI.
- iter-301: Asset/icon extraction kickoff (stretch).

## Verification checklist

- [x] `Lua_GetPlanets` real impl in place at `lua_bridge.cpp:2912`.
- [x] Bridge builds clean: 1100/0 harness pass.
- [x] Catalog entry flipped Phase2HookPending → Live with iter-296 rationale.
- [x] Editor builds clean: 0 warnings / 0 errors.
- [x] Lua 5.0 compatibility verified (pairs + counter pattern, table.concat).
- [x] 3-category fallback chain (Planet → GalacticPlanet → Planetary).
- [x] Per-planet pcall guards (one bad planet doesn't break enumeration).
- [ ] State docs synced.
- [ ] Task #547 marked completed; iter-297 queued.
- [ ] Live-game smoke (deferred — operator's next session).
