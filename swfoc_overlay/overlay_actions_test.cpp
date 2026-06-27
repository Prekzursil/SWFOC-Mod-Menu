// =============================================================================
// swfoc_overlay/overlay_actions_test.cpp — unit test for overlay_actions.h.
//
// Standalone test executable. overlay_actions.h is pure / header-only
// (no Windows, no ImGui, no D3D9) so this compiles with a plain g++ and
// needs no game, no bridge, no DLL. Build + run via build_actions_test.bat.
//
// RED-GREEN REGRESSION PINS
// ------------------------
// The SWFOC_*Lua wires require two-level Lua quoting: each call argument
// is a Lua string literal whose contents are a Lua expression, so inner
// double-quotes must become \" inside the literal. The most likely future
// regression is a "simplification" of LuaQuote that drops inner-quote
// escaping. The ExpectContains / ExpectNotContains pair below pins that:
//   - the spawn command MUST contain the escaped  \"REBEL\"  sequence
//     (passes only on the correct/new form),
//   - the spawn command MUST NOT contain the bare  Player("REBEL")
//     sequence (fails on the old/broken un-escaped form).
// Both live in this file so a silent revert fires immediately.
// =============================================================================

#include "overlay_actions.h"

#include <cstdio>
#include <string>

namespace
{
    int g_checks = 0;
    int g_failures = 0;

    void ExpectEq(const char* name, const std::string& got, const std::string& want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %s\n    want: %s\n",
                        name, got.c_str(), want.c_str());
        }
    }

    void ExpectContains(const char* name, const std::string& hay,
                        const std::string& needle)
    {
        ++g_checks;
        if (hay.find(needle) != std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    string  : %s\n    missing : %s\n",
                        name, hay.c_str(), needle.c_str());
        }
    }

    void ExpectNotContains(const char* name, const std::string& hay,
                           const std::string& needle)
    {
        ++g_checks;
        if (hay.find(needle) == std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    string       : %s\n    must NOT have: %s\n",
                        name, hay.c_str(), needle.c_str());
        }
    }
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_actions builder unit test ==\n");

    // ---- LuaQuote -----------------------------------------------------------
    ExpectEq("LuaQuote plain", LuaQuote("REBEL"), "\"REBEL\"");
    ExpectEq("LuaQuote nested-quotes",
             LuaQuote("Find_Player(\"REBEL\")"),
             "\"Find_Player(\\\"REBEL\\\")\"");
    ExpectEq("LuaQuote backslash", LuaQuote("a\\b"), "\"a\\\\b\"");
    ExpectEq("LuaQuote newline", LuaQuote("a\nb"), "\"a\\nb\"");
    ExpectEq("LuaQuote carriage-return", LuaQuote("a\rb"), "\"a\\rb\"");

    // ---- FormatCoord --------------------------------------------------------
    ExpectEq("FormatCoord zero", FormatCoord(0.0f), "0");
    ExpectEq("FormatCoord whole", FormatCoord(250.0f), "250");
    ExpectEq("FormatCoord fractional", FormatCoord(1.5f), "1.5");
    ExpectEq("FormatCoord negative", FormatCoord(-12.25f), "-12.25");
    ExpectEq("FormatCoord negative-zero", FormatCoord(-0.0f), "0");

    // ---- BuildSpawnUnitCommand ---------------------------------------------
    ExpectEq("Spawn whole-coords",
             BuildSpawnUnitCommand("REBEL", "Rebel_Trooper_Squad", 0, 0, 0),
             "return SWFOC_SpawnUnitLua("
             "\"Find_Player(\\\"REBEL\\\")\", "
             "\"Find_Object_Type(\\\"Rebel_Trooper_Squad\\\")\", "
             "\"Create_Position(0, 0, 0)\")");
    ExpectEq("Spawn fractional-coords",
             BuildSpawnUnitCommand("EMPIRE", "Empire_AT_AT", 100.5f, -50.25f, 0),
             "return SWFOC_SpawnUnitLua("
             "\"Find_Player(\\\"EMPIRE\\\")\", "
             "\"Find_Object_Type(\\\"Empire_AT_AT\\\")\", "
             "\"Create_Position(100.5, -50.25, 0)\")");

    // ---- BuildMakeUnitInvulnCommand ----------------------------------------
    ExpectEq("MakeInvuln true",
             BuildMakeUnitInvulnCommand("Find_First_Object(\"Empire_AT_AT\")", true),
             "return SWFOC_MakeUnitInvulnLua("
             "\"Find_First_Object(\\\"Empire_AT_AT\\\")\", \"true\")");
    ExpectEq("MakeInvuln false",
             BuildMakeUnitInvulnCommand("Find_First_Object(\"Empire_AT_AT\")", false),
             "return SWFOC_MakeUnitInvulnLua("
             "\"Find_First_Object(\\\"Empire_AT_AT\\\")\", \"false\")");

    // ---- BuildKillUnitCommand ----------------------------------------------
    ExpectEq("Kill decimal-address",
             BuildKillUnitCommand(140737488355328ULL),
             "return SWFOC_KillUnit(140737488355328)");
    ExpectEq("Kill zero-address",
             BuildKillUnitCommand(0ULL),
             "return SWFOC_KillUnit(0)");

    // ---- BuildTeleportUnitCommand ------------------------------------------
    ExpectEq("Teleport whole-coords",
             BuildTeleportUnitCommand("Find_First_Object(\"Empire_AT_AT\")",
                                      0, 0, 0),
             "return SWFOC_TeleportUnitLua("
             "\"Find_First_Object(\\\"Empire_AT_AT\\\")\", "
             "\"Create_Position(0, 0, 0)\")");
    ExpectEq("Teleport fractional-coords",
             BuildTeleportUnitCommand("Find_First_Object(\"Rebel_Trooper_Squad\")",
                                      100.5f, -50.25f, 0),
             "return SWFOC_TeleportUnitLua("
             "\"Find_First_Object(\\\"Rebel_Trooper_Squad\\\")\", "
             "\"Create_Position(100.5, -50.25, 0)\")");

    // ---- BuildChangeUnitOwnerCommand ---------------------------------------
    ExpectEq("ChangeOwner rebel",
             BuildChangeUnitOwnerCommand(
                 "Find_First_Object(\"Empire_AT_AT\")", "REBEL"),
             "return SWFOC_ChangeUnitOwner("
             "\"Find_First_Object(\\\"Empire_AT_AT\\\")\", "
             "\"Find_Player(\\\"REBEL\\\")\")");
    ExpectEq("ChangeOwner underworld",
             BuildChangeUnitOwnerCommand(
                 "Find_First_Object(\"Rebel_Trooper_Squad\")", "UNDERWORLD"),
             "return SWFOC_ChangeUnitOwner("
             "\"Find_First_Object(\\\"Rebel_Trooper_Squad\\\")\", "
             "\"Find_Player(\\\"UNDERWORLD\\\")\")");

    // ---- Red-green regression pins -----------------------------------------
    // These two checks form the pair: the first passes ONLY on the correct
    // escaped form, the second fails ONLY on the un-escaped (broken) form.
    const std::string spawn =
        BuildSpawnUnitCommand("REBEL", "Rebel_Trooper_Squad", 0, 0, 0);
    ExpectContains("pin: spawn keeps escaped inner quotes",
                   spawn, "\\\"REBEL\\\"");
    ExpectNotContains("pin: spawn never emits bare inner quotes",
                      spawn, "Player(\"REBEL\")");

    // The teleport and faction-switch commands carry the same two-level
    // quoting: the unit handle expression's inner quotes MUST stay escaped,
    // and the bare un-escaped form MUST NOT appear. Same red-green shape as
    // the spawn pins above — a LuaQuote simplification that drops inner-quote
    // escaping fires here too.
    const std::string teleport =
        BuildTeleportUnitCommand("Find_First_Object(\"Empire_AT_AT\")", 0, 0, 0);
    ExpectContains("pin: teleport keeps escaped inner quotes",
                   teleport, "\\\"Empire_AT_AT\\\"");
    ExpectNotContains("pin: teleport never emits bare inner quotes",
                      teleport, "Object(\"Empire_AT_AT\")");

    const std::string changeOwner =
        BuildChangeUnitOwnerCommand("Find_First_Object(\"Empire_AT_AT\")", "REBEL");
    ExpectContains("pin: change-owner keeps escaped faction quotes",
                   changeOwner, "Find_Player(\\\"REBEL\\\")");
    ExpectNotContains("pin: change-owner never emits bare faction quotes",
                      changeOwner, "Find_Player(\"REBEL\")");

    // ---- Boundary: garbled input must not split the pipe line --------------
    // BridgeProbe terminates the wire line with '\n'; a unit name carrying a
    // raw newline must be neutralised by LuaQuote, never passed through.
    const std::string garbled =
        BuildSpawnUnitCommand("REBEL", "Bad\nName", 0, 0, 0);
    ExpectNotContains("boundary: no raw newline in spawn command",
                      garbled, std::string(1, '\n'));

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
