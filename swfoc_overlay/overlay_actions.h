// =============================================================================
// swfoc_overlay/overlay_actions.h — Phase 3 bridge-command builders.
//
// The overlay's Phase 3 "Actions" widgets (Spawn / Make-Invuln / Kill /
// Teleport / Faction-Switch) issue engine mutations by sending a single Lua
// line through the existing bridge pipe (\\.\pipe\swfoc_bridge, see
// hud_state.cpp::BridgeProbe). The bridge runs that line via DoString.
//
// The SWFOC_*Lua write wires take their arguments as **Lua expression
// strings** — each argument is a Lua string literal whose CONTENTS are
// themselves a Lua expression. That means two levels of quoting:
//
//   inner expression : Find_Player("REBEL")
//   as a Lua literal : "Find_Player(\"REBEL\")"
//
// Getting that escaping wrong silently breaks the call (the bridge sees a
// truncated or malformed expression). These builders are pure, deterministic
// functions so the escaping can be pinned by overlay_actions_test.cpp before
// the render path ever depends on it.
//
// Verified wire signatures (swfoc_lua_bridge/lua_bridge.cpp, 2026-05-21):
//   SWFOC_SpawnUnitLua(player_expr, type_expr, position_expr)  -> Spawn_Unit
//   SWFOC_MakeUnitInvulnLua(unit_lua_expr, bool_lua_expr)      -> :Make_Invulnerable
//   SWFOC_KillUnit(obj_addr)                                   -> hull = 0
//   SWFOC_TeleportUnitLua(unit_lua_expr, position_lua_expr)    -> :Teleport
//   SWFOC_ChangeUnitOwner(unit_lua_expr, player_lua_expr)      -> :Change_Owner
//   SWFOC_HealUnitLua(unit_lua_expr)                           -> :Heal
//
// NOTE: the overlay-interactive spec referred to the invuln wire as
// "SWFOC_MakeInvulnerableLua" — that name does not exist. The real
// registered wire is "SWFOC_MakeUnitInvulnLua" (lua_bridge.cpp:8372).
// =============================================================================

#pragma once

#include <cstdio>
#include <string>

namespace swfoc_overlay
{
    // Wrap `inner` in a Lua double-quoted string literal, escaping the
    // characters that would otherwise break the literal or split the
    // pipe line. Backslash and double-quote are escaped so nested
    // expressions survive; newline/carriage-return are escaped because
    // BridgeProbe terminates the wire line with '\n' and an embedded
    // newline would split the command.
    inline std::string LuaQuote(const std::string& inner)
    {
        std::string out;
        out.reserve(inner.size() + 2);
        out.push_back('"');
        for (const char c : inner)
        {
            switch (c)
            {
                case '\\': out += "\\\\"; break;
                case '"':  out += "\\\""; break;
                case '\n': out += "\\n";  break;
                case '\r': out += "\\r";  break;
                default:   out.push_back(c); break;
            }
        }
        out.push_back('"');
        return out;
    }

    // Format a world coordinate as a Lua number literal. Uses fixed 3-dp
    // formatting then trims trailing zeros so whole numbers render as
    // "0" not "0.000" — keeps the command preview readable and the test
    // output deterministic. Never emits scientific notation (which Lua
    // 5.0's number lexer tolerates inconsistently across CRTs).
    inline std::string FormatCoord(float v)
    {
        char buf[64];
        std::snprintf(buf, sizeof(buf), "%.3f", static_cast<double>(v));
        std::string s(buf);
        if (s.find('.') != std::string::npos)
        {
            while (!s.empty() && s.back() == '0') s.pop_back();
            if (!s.empty() && s.back() == '.') s.pop_back();
        }
        if (s == "-0") s = "0";
        return s;
    }

    // Build the Lua line that spawns a unit via SWFOC_SpawnUnitLua.
    // `factionName`  -> Find_Player("<faction>")        e.g. "REBEL"
    // `unitTypeName` -> Find_Object_Type("<type>")      e.g. "Rebel_Trooper_Squad"
    // (x, y, z)      -> Create_Position(x, y, z)
    inline std::string BuildSpawnUnitCommand(const std::string& factionName,
                                             const std::string& unitTypeName,
                                             float x, float y, float z)
    {
        const std::string playerExpr =
            "Find_Player(" + LuaQuote(factionName) + ")";
        const std::string typeExpr =
            "Find_Object_Type(" + LuaQuote(unitTypeName) + ")";
        const std::string posExpr =
            "Create_Position(" + FormatCoord(x) + ", " +
            FormatCoord(y) + ", " + FormatCoord(z) + ")";
        return "return SWFOC_SpawnUnitLua(" + LuaQuote(playerExpr) + ", " +
               LuaQuote(typeExpr) + ", " + LuaQuote(posExpr) + ")";
    }

    // Build the Lua line that toggles unit invulnerability via
    // SWFOC_MakeUnitInvulnLua. `unitLuaExpr` is the inner Lua expression
    // that resolves to a unit handle (e.g. Find_First_Object("Empire_AT_AT"));
    // it is quoted as a Lua string literal. The bool argument is itself a
    // Lua string literal "true"/"false" — the bridge splices it into
    // (<unit>):Make_Invulnerable(<bool>).
    inline std::string BuildMakeUnitInvulnCommand(const std::string& unitLuaExpr,
                                                  bool invulnerable)
    {
        return "return SWFOC_MakeUnitInvulnLua(" + LuaQuote(unitLuaExpr) +
               ", " + LuaQuote(invulnerable ? "true" : "false") + ")";
    }

    // Build the Lua line that kills a unit via SWFOC_KillUnit. The wire
    // takes a numeric object address (Lua number); a 48-bit user-space
    // pointer round-trips exactly through Lua 5.0's double. Emitted as a
    // plain decimal literal — universally parsed by Lua's tonumber.
    inline std::string BuildKillUnitCommand(unsigned long long objAddr)
    {
        char buf[32];
        std::snprintf(buf, sizeof(buf), "%llu", objAddr);
        return std::string("return SWFOC_KillUnit(") + buf + ")";
    }

    // Build the Lua line that teleports a unit via SWFOC_TeleportUnitLua.
    // Mirrors the iter-108 ChangeUnitOwner two-arg shape: `unitLuaExpr` is
    // the inner Lua expression resolving to a unit handle (e.g.
    // Find_First_Object("Empire_AT_AT")), quoted as a Lua string literal;
    // (x, y, z) become Create_Position(x, y, z) — the same coordinate
    // expression BuildSpawnUnitCommand emits. The bridge composes
    // (<unit>):Teleport(<pos>).
    inline std::string BuildTeleportUnitCommand(const std::string& unitLuaExpr,
                                                float x, float y, float z)
    {
        const std::string posExpr =
            "Create_Position(" + FormatCoord(x) + ", " +
            FormatCoord(y) + ", " + FormatCoord(z) + ")";
        return "return SWFOC_TeleportUnitLua(" + LuaQuote(unitLuaExpr) +
               ", " + LuaQuote(posExpr) + ")";
    }

    // Build the Lua line that re-owns a unit via SWFOC_ChangeUnitOwner —
    // the engine's full "swap sides" behaviour (iter-108). `unitLuaExpr`
    // resolves to a unit handle; `factionName` -> Find_Player("<faction>")
    // resolves to the new owning player. Both arguments are quoted as Lua
    // string literals; the bridge composes (<unit>):Change_Owner(<player>).
    inline std::string BuildChangeUnitOwnerCommand(const std::string& unitLuaExpr,
                                                   const std::string& factionName)
    {
        const std::string playerExpr =
            "Find_Player(" + LuaQuote(factionName) + ")";
        return "return SWFOC_ChangeUnitOwner(" + LuaQuote(unitLuaExpr) +
               ", " + LuaQuote(playerExpr) + ")";
    }

    // Build the Lua line that heals a unit via SWFOC_HealUnitLua — the
    // engine's GameObject :Heal() method (iter-154 LIVE wire). A no-arg unit
    // method, so this mirrors the Despawn / Stop / Retreat shape, NOT the
    // two-arg Teleport / ChangeOwner shape: the only argument is `unitLuaExpr`,
    // the inner Lua expression resolving to a unit handle (e.g.
    // Find_First_Object("Empire_AT_AT")), quoted as a Lua string literal. The
    // bridge composes (<unit>):Heal().
    inline std::string BuildHealUnitCommand(const std::string& unitLuaExpr)
    {
        return "return SWFOC_HealUnitLua(" + LuaQuote(unitLuaExpr) + ")";
    }
}
