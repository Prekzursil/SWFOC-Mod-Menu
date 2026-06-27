// =============================================================================
// swfoc_overlay/overlay_inspector_actions_test.cpp — unit test for
// overlay_inspector_actions.h (Phase 5 cont., iter 540 / spec iter-301).
//
// iter-301 is the write-side of click-to-inspect: once iter-300's panel shows
// the clicked unit, five action buttons act on it — Kill / Heal / Teleport /
// SwapOwner / MakeInvuln. overlay_inspector_actions.h holds the pure kernel
// that turns the inspected UnitInfo into a dispatch-ready ActionRequest for
// each button (label + the exact bridge Lua line). This test pins all of it
// so the deferred ImGui::Begin("Inspector") glue can depend on it build-only.
//
// The integration section wires the kernel to its real upstream: it builds a
// visible-unit AABB set, runs overlay_hit_test.h's NearestUnitHit to pick a
// unit, feeds that UnitHit into overlay_inspector.h's OpenInspectorFor, and
// then builds all five inspector actions from the panel's unit — confirming
// the full Phase 5 chain (raycast -> inspect -> act) holds end to end.
//
// overlay_inspector_actions.h is header-only and std-only (<string> plus the
// include chain). Build + run via build_inspector_actions_test.bat — no game,
// no pipe, no ImGui.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - KILL TARGETS EXACT HANDLE : the kill Lua embeds the picked unit's exact
//                                 handle as the obj_addr.
//   - HEAL WIRE NAME CORRECT    : the heal Lua names SWFOC_HealUnitLua.
//   - HEAL IS SINGLE-ARG        : the heal Lua is the no-arg :Heal() shape.
//   - SWAPOWNER USES NEW OWNER  : SwapOwner re-owns to the requested slot,
//                                 not the unit's current owner.
//   - INVULN BOOL TOGGLES       : true -> "true" + "Make Invuln"; false ->
//                                 "false" + "Clear Invuln".
//   - EXPR ESCAPES TYPE NAME    : a quote in the unit type is escaped in the
//                                 Find_First_Object literal.
//   - LABEL NAMES THE UNIT      : every ActionRequest label embeds the type.
// =============================================================================

#include "overlay_inspector_actions.h"

#include <cstdio>
#include <cstring>
#include <string>

namespace
{
    int g_checks = 0;
    int g_failures = 0;

    void ExpectTrue(const char* name, bool cond)
    {
        ++g_checks;
        if (cond)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    expected true\n", name);
        }
    }

    void ExpectStr(const char* name, const std::string& got,
                   const char* want)
    {
        ++g_checks;
        if (want != nullptr && got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : \"%s\"\n    want: \"%s\"\n",
                        name, got.c_str(), want != nullptr ? want : "(null)");
        }
    }

    // Pin that `haystack` contains `needle` — used for the longer Lua lines
    // where one distinctive fragment carries the regression signal.
    void ExpectContains(const char* name, const std::string& haystack,
                        const char* needle)
    {
        ++g_checks;
        if (needle != nullptr &&
            haystack.find(needle) != std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\"\n    must contain: \"%s\"\n",
                        name, haystack.c_str(),
                        needle != nullptr ? needle : "(null)");
        }
    }

    // Pin that `haystack` does NOT contain `needle` — the negative half of a
    // red-green pin (e.g. SwapOwner must not name the current owner).
    void ExpectAbsent(const char* name, const std::string& haystack,
                      const char* needle)
    {
        ++g_checks;
        if (needle != nullptr &&
            haystack.find(needle) == std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\"\n    must NOT contain: \"%s\"\n",
                        name, haystack.c_str(),
                        needle != nullptr ? needle : "(null)");
        }
    }

    using swfoc_overlay::AabbFromCenterExtents;
    using swfoc_overlay::ActionRequest;
    using swfoc_overlay::BuildHealUnitCommand;
    using swfoc_overlay::BuildInspectorHeal;
    using swfoc_overlay::BuildInspectorKill;
    using swfoc_overlay::BuildInspectorMakeInvuln;
    using swfoc_overlay::BuildInspectorSwapOwner;
    using swfoc_overlay::BuildInspectorTeleport;
    using swfoc_overlay::FactionPlayerName;
    using swfoc_overlay::InspectorPanel;
    using swfoc_overlay::InspectorUnitLuaExpr;
    using swfoc_overlay::NearestUnitHit;
    using swfoc_overlay::NextFactionSlot;
    using swfoc_overlay::OpenInspectorFor;
    using swfoc_overlay::UnitAabb;
    using swfoc_overlay::UnitHit;
    using swfoc_overlay::UnitInfo;
    using swfoc_overlay::Vec3;
    using swfoc_overlay::WorldRay;

    // Build a UnitInfo in one expression — mirrors overlay_inspector_test.cpp.
    UnitInfo MakeUnit(std::uint64_t handle, int owner, const char* type)
    {
        UnitInfo u{};
        u.handle    = handle;
        u.hull      = 5000.0f;
        u.hullMax   = 8000.0f;
        u.shield    = 1000.0f;
        u.shieldMax = 2000.0f;
        u.owner     = owner;
        u.position  = Vec3{ 0.0f, 0.0f, 0.0f };
        swfoc_overlay::SetUnitType(u, type);
        return u;
    }
}

int main()
{
    std::printf("=== overlay_inspector_actions.h kernel test ===\n\n");

    // -----------------------------------------------------------------------
    // 1. BuildHealUnitCommand — the new overlay_actions.h builder (iter-540).
    // -----------------------------------------------------------------------
    std::printf("[BuildHealUnitCommand]\n");
    {
        // Single, no-arg unit method: SWFOC_HealUnitLua("<expr>"), nothing
        // more. The HEAL WIRE NAME + HEAL IS SINGLE-ARG pins live here.
        ExpectStr("plain expr -> return SWFOC_HealUnitLua(\"UnitX\")",
                  BuildHealUnitCommand("UnitX"),
                  "return SWFOC_HealUnitLua(\"UnitX\")");
        ExpectContains("HEAL WIRE NAME CORRECT: names SWFOC_HealUnitLua",
                       BuildHealUnitCommand("UnitX"), "SWFOC_HealUnitLua(");
        ExpectAbsent("HEAL IS SINGLE-ARG: no second argument separator",
                     BuildHealUnitCommand("UnitX"), "\", \"");
        // A nested expression carrying its own quotes survives — LuaQuote
        // escapes them so the outer literal stays well-formed.
        ExpectStr("nested expr is escaped",
                  BuildHealUnitCommand("Find_First_Object(\"Z\")"),
                  "return SWFOC_HealUnitLua(\"Find_First_Object(\\\"Z\\\")\")");
    }

    // -----------------------------------------------------------------------
    // 2. FactionPlayerName — owner slot -> engine Find_Player() id.
    // -----------------------------------------------------------------------
    std::printf("\n[FactionPlayerName]\n");
    {
        ExpectStr("slot 0 -> REBEL",      FactionPlayerName(0), "REBEL");
        ExpectStr("slot 1 -> EMPIRE",     FactionPlayerName(1), "EMPIRE");
        ExpectStr("slot 2 -> UNDERWORLD", FactionPlayerName(2), "UNDERWORLD");
        // An invalid slot yields "" so a bad request is detectable, never a
        // silent re-own to the wrong side.
        ExpectStr("slot 3 -> empty",  FactionPlayerName(3),  "");
        ExpectStr("slot -1 -> empty", FactionPlayerName(-1), "");
        ExpectStr("slot 99 -> empty", FactionPlayerName(99), "");
    }

    // -----------------------------------------------------------------------
    // 3. NextFactionSlot — the 0 -> 1 -> 2 -> 0 ownership cycle.
    // -----------------------------------------------------------------------
    std::printf("\n[NextFactionSlot]\n");
    {
        ExpectTrue("0 -> 1", NextFactionSlot(0) == 1);
        ExpectTrue("1 -> 2", NextFactionSlot(1) == 2);
        ExpectTrue("2 -> 0 (wraps)", NextFactionSlot(2) == 0);
        ExpectTrue("invalid 7 -> 0 (cycle is total)", NextFactionSlot(7) == 0);
        ExpectTrue("invalid -1 -> 0", NextFactionSlot(-1) == 0);
    }

    // -----------------------------------------------------------------------
    // 4. InspectorUnitLuaExpr — the first-of-type fallback seam.
    // -----------------------------------------------------------------------
    std::printf("\n[InspectorUnitLuaExpr]\n");
    {
        const UnitInfo atat = MakeUnit(0x1000ull, 1, "Empire_AT_AT");
        ExpectStr("type -> Find_First_Object(\"<type>\")",
                  InspectorUnitLuaExpr(atat),
                  "Find_First_Object(\"Empire_AT_AT\")");

        // EXPR ESCAPES TYPE NAME pin — a quote in the type is escaped so the
        // Lua string literal stays valid; a raw-concat old form breaks it.
        const UnitInfo weird = MakeUnit(0x2000ull, 0, "Bad\"Type");
        ExpectStr("EXPR ESCAPES TYPE NAME: embedded quote is escaped",
                  InspectorUnitLuaExpr(weird),
                  "Find_First_Object(\"Bad\\\"Type\")");

        const UnitInfo empty = MakeUnit(0x3000ull, 2, "");
        ExpectStr("empty type -> Find_First_Object(\"\")",
                  InspectorUnitLuaExpr(empty),
                  "Find_First_Object(\"\")");
    }

    // -----------------------------------------------------------------------
    // 5. BuildInspectorKill — exact-unit, address-based.
    // -----------------------------------------------------------------------
    std::printf("\n[BuildInspectorKill]\n");
    {
        const UnitInfo unit = MakeUnit(1000000ull, 1, "Empire_AT_AT");
        const ActionRequest req = BuildInspectorKill(unit);
        ExpectStr("label names the unit", req.label, "Kill Empire_AT_AT");

        // KILL TARGETS EXACT HANDLE pin — the kill Lua carries the picked
        // unit's exact handle as the obj_addr, so it never hits a sibling.
        ExpectStr("KILL TARGETS EXACT HANDLE: Lua embeds the handle",
                  req.lua, "return SWFOC_KillUnit(1000000)");

        // A different inspected unit kills a different address — proves the
        // handle is read from the unit, not hard-wired.
        const UnitInfo sibling = MakeUnit(2000000ull, 1, "Empire_AT_AT");
        const ActionRequest reqB = BuildInspectorKill(sibling);
        ExpectStr("a sibling of the same type kills a different address",
                  reqB.lua, "return SWFOC_KillUnit(2000000)");
        ExpectAbsent("the sibling kill never carries the first handle",
                     reqB.lua, "1000000");
    }

    // -----------------------------------------------------------------------
    // 6. BuildInspectorHeal — engine :Heal() via the first-of-type expr.
    // -----------------------------------------------------------------------
    std::printf("\n[BuildInspectorHeal]\n");
    {
        const UnitInfo unit = MakeUnit(0x4242ull, 1, "Empire_AT_AT");
        const ActionRequest req = BuildInspectorHeal(unit);
        ExpectStr("label names the unit", req.label, "Heal Empire_AT_AT");
        ExpectStr("Lua resolves the unit then calls :Heal()",
                  req.lua,
                  "return SWFOC_HealUnitLua("
                  "\"Find_First_Object(\\\"Empire_AT_AT\\\")\")");
        ExpectContains("HEAL WIRE NAME CORRECT", req.lua, "SWFOC_HealUnitLua(");
    }

    // -----------------------------------------------------------------------
    // 7. BuildInspectorTeleport — :Teleport() to a caller-supplied position.
    // -----------------------------------------------------------------------
    std::printf("\n[BuildInspectorTeleport]\n");
    {
        const UnitInfo unit = MakeUnit(0x5555ull, 0, "Rebel_Trooper_Squad");
        const ActionRequest req = BuildInspectorTeleport(unit, 100.0f,
                                                         200.0f, 0.0f);
        ExpectStr("label names the unit", req.label,
                  "Teleport Rebel_Trooper_Squad");
        ExpectContains("Lua names the teleport wire", req.lua,
                       "SWFOC_TeleportUnitLua(");
        ExpectContains("Lua resolves the unit by type", req.lua,
                       "Find_First_Object(\\\"Rebel_Trooper_Squad\\\")");
        ExpectContains("Lua carries the destination position", req.lua,
                       "Create_Position(100, 200, 0)");
    }

    // -----------------------------------------------------------------------
    // 8. BuildInspectorSwapOwner — engine Change_Owner to a requested slot.
    // -----------------------------------------------------------------------
    std::printf("\n[BuildInspectorSwapOwner]\n");
    {
        // The unit is currently Empire (slot 1); the operator re-owns it to
        // Rebel (slot 0).
        const UnitInfo unit = MakeUnit(0x6666ull, 1, "Empire_AT_AT");
        const ActionRequest req = BuildInspectorSwapOwner(unit, 0);
        ExpectStr("label shows the destination faction", req.label,
                  "Swap Owner Empire_AT_AT -> Rebel");
        ExpectContains("Lua names the change-owner wire", req.lua,
                       "SWFOC_ChangeUnitOwner(");

        // SWAPOWNER USES NEW OWNER pin — the Lua re-owns to REBEL (the
        // requested slot 0), never to EMPIRE (the unit's current owner). A
        // "swap to current owner" old form would be a silent no-op.
        ExpectContains("SWAPOWNER USES NEW OWNER: re-owns to REBEL", req.lua,
                       "Find_Player(\\\"REBEL\\\")");
        ExpectAbsent("SWAPOWNER USES NEW OWNER: never names the old owner",
                     req.lua, "EMPIRE");

        // Slot 2 routes to the Underworld player + display name.
        const ActionRequest reqU = BuildInspectorSwapOwner(unit, 2);
        ExpectStr("slot 2 label reads Underworld", reqU.label,
                  "Swap Owner Empire_AT_AT -> Underworld");
        ExpectContains("slot 2 Lua re-owns to UNDERWORLD", reqU.lua,
                       "Find_Player(\\\"UNDERWORLD\\\")");

        // The single-click cycle: NextFactionSlot(current) advances ownership.
        const ActionRequest reqCycle =
            BuildInspectorSwapOwner(unit, NextFactionSlot(unit.owner));
        ExpectStr("cycle from Empire advances to Underworld", reqCycle.label,
                  "Swap Owner Empire_AT_AT -> Underworld");
    }

    // -----------------------------------------------------------------------
    // 9. BuildInspectorMakeInvuln — engine :Make_Invulnerable() toggle.
    // -----------------------------------------------------------------------
    std::printf("\n[BuildInspectorMakeInvuln]\n");
    {
        const UnitInfo unit = MakeUnit(0x7777ull, 1, "Empire_AT_AT");

        // INVULN BOOL TOGGLES pin — true sets, false clears, and the label
        // and the Lua bool literal agree.
        const ActionRequest on = BuildInspectorMakeInvuln(unit, true);
        ExpectStr("invuln on: label reads Make Invuln", on.label,
                  "Make Invuln Empire_AT_AT");
        ExpectContains("invuln on: Lua bool literal is \"true\"", on.lua,
                       ", \"true\")");
        ExpectContains("invuln on: names the invuln wire", on.lua,
                       "SWFOC_MakeUnitInvulnLua(");

        const ActionRequest off = BuildInspectorMakeInvuln(unit, false);
        ExpectStr("invuln off: label reads Clear Invuln", off.label,
                  "Clear Invuln Empire_AT_AT");
        ExpectContains("invuln off: Lua bool literal is \"false\"", off.lua,
                       ", \"false\")");
    }

    // -----------------------------------------------------------------------
    // 10. LABEL NAMES THE UNIT — every builder embeds the type in the label.
    // -----------------------------------------------------------------------
    std::printf("\n[LABEL NAMES THE UNIT]\n");
    {
        const UnitInfo unit = MakeUnit(0x8888ull, 0, "Underworld_Vengeance");
        ExpectContains("kill label names the unit",
                       BuildInspectorKill(unit).label, "Underworld_Vengeance");
        ExpectContains("heal label names the unit",
                       BuildInspectorHeal(unit).label, "Underworld_Vengeance");
        ExpectContains("teleport label names the unit",
                       BuildInspectorTeleport(unit, 0, 0, 0).label,
                       "Underworld_Vengeance");
        ExpectContains("swap-owner label names the unit",
                       BuildInspectorSwapOwner(unit, 1).label,
                       "Underworld_Vengeance");
        ExpectContains("make-invuln label names the unit",
                       BuildInspectorMakeInvuln(unit, true).label,
                       "Underworld_Vengeance");
    }

    // -----------------------------------------------------------------------
    // 11. Integration — the full Phase 5 chain: raycast -> inspect -> act.
    // -----------------------------------------------------------------------
    std::printf("\n[integration: raycast -> inspect -> act]\n");
    {
        // Two visible units; the cursor ray (straight down -Z at x=0) crosses
        // only the unit at the origin.
        const UnitAabb boxes[2] = {
            { 0x4242ull, AabbFromCenterExtents(Vec3{ 0.0f, 0.0f, 0.0f },
                                               5.0f, 5.0f, 5.0f) },
            { 0x9999ull, AabbFromCenterExtents(Vec3{ 50.0f, 0.0f, 0.0f },
                                               5.0f, 5.0f, 5.0f) },
        };
        // The parallel UnitInfo set the inspector resolves the pick against.
        const UnitInfo infos[2] = {
            MakeUnit(0x4242ull, 1, "Empire_AT_AT"),
            MakeUnit(0x9999ull, 0, "Rebel_Trooper_Squad"),
        };

        WorldRay ray{};
        ray.origin    = Vec3{ 0.0f, 0.0f, 100.0f };
        ray.direction = Vec3{ 0.0f, 0.0f, -1.0f };
        ray.valid     = true;

        const UnitHit pick = NearestUnitHit(ray, boxes, 2);
        ExpectTrue("integration: the raycast picks a unit", pick.hit);
        ExpectTrue("integration: it picks the unit at the origin",
                   pick.handle == 0x4242ull);

        const InspectorPanel panel = OpenInspectorFor(pick, infos, 2);
        ExpectTrue("integration: the inspector opens on the picked unit",
                   panel.visible);
        ExpectTrue("integration: the panel holds the picked handle",
                   panel.unit.handle == 0x4242ull);

        // Build all five actions from the inspected unit. Each must be
        // dispatch-ready: a non-empty label that names the unit and a Lua
        // line that is a real bridge call.
        const ActionRequest acts[5] = {
            BuildInspectorKill(panel.unit),
            BuildInspectorHeal(panel.unit),
            BuildInspectorTeleport(panel.unit, 10.0f, 20.0f, 0.0f),
            BuildInspectorSwapOwner(panel.unit,
                                    NextFactionSlot(panel.unit.owner)),
            BuildInspectorMakeInvuln(panel.unit, true),
        };
        for (int i = 0; i < 5; ++i)
        {
            ExpectTrue("integration: the action has a label",
                       !acts[i].label.empty());
            ExpectContains("integration: the label names the picked unit",
                           acts[i].label, "Empire_AT_AT");
            ExpectContains("integration: the Lua is a bridge call",
                           acts[i].lua, "return SWFOC_");
        }

        // The kill action targets the EXACT picked handle (0x4242 = 16962),
        // never the unpicked sibling (0x9999 = 39321).
        ExpectContains("integration: the kill targets the picked handle",
                       acts[0].lua, "16962");
        ExpectAbsent("integration: the kill never targets the sibling",
                     acts[0].lua, "39321");

        // A miss-pick yields no panel — and so the inspector simply has no
        // unit to build actions from (the glue gates the buttons on
        // panel.visible).
        UnitHit miss{};
        miss.hit = false;
        const InspectorPanel none = OpenInspectorFor(miss, infos, 2);
        ExpectTrue("integration: an empty-ground click opens no inspector",
                   !none.visible);
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
