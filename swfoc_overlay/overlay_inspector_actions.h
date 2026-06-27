// =============================================================================
// swfoc_overlay/overlay_inspector_actions.h — Phase 5 inspector action kernel.
//
// Phase 5 (iter 297-303) makes the overlay click-aware. iter-298
// (overlay_cursor_ray.h) turns a cursor pixel into a world-space pick ray,
// iter-299 (overlay_hit_test.h) walks the visible-unit AABB set to name the
// unit under the cursor, and iter-300 (overlay_inspector.h) presents that
// unit in a read-only "Inspector" panel. iter-301 (spec line 59) is the
// write-side: five action buttons on the inspector panel —
//
//   Kill        SWFOC_KillUnit(obj_addr)                    iter-110 LIVE
//   Heal        SWFOC_HealUnitLua(unit_lua_expr)            iter-154 LIVE
//   Teleport    SWFOC_TeleportUnitLua(unit_lua_expr, pos)   iter-151 LIVE
//   SwapOwner   SWFOC_ChangeUnitOwner(unit_lua_expr, plr)   iter-108 LIVE
//   MakeInvuln  SWFOC_MakeUnitInvulnLua(unit_lua_expr, b)   iter-110 LIVE
//
// All five wires are already LIVE — iter-301 ships NO new bridge wire. This
// header is the pure kernel that turns the inspected UnitInfo (the unit the
// operator clicked) into a dispatch-ready ActionRequest for each button. The
// ImGui::Begin("Inspector") glue (deferred; lands after iter-302 extends
// HudSnapshot with the per-unit set) only renders five buttons and hands the
// ActionRequest to the iter-513 ActionQueue — the deciding logic is here so a
// unit test pins it before the render path depends on it.
//
// EXACT-UNIT TARGETING — THE HONEST LIMITATION
// --------------------------------------------
// The inspector picked ONE specific unit by raycast and knows its exact
// engine handle (UnitInfo::handle == the GameObject address).
//
//   - Kill is address-based: SWFOC_KillUnit takes a numeric obj_addr, so
//     BuildInspectorKill targets the EXACT picked unit. No ambiguity.
//
//   - Heal / Teleport / SwapOwner / MakeInvuln are engine-METHOD wires: they
//     take a unit Lua *expression*, not an address, and SWFOC exposes no
//     "object from address" engine-Lua function. So these four fall back to
//     Find_First_Object("<type>") — the first live instance of the picked
//     unit's type — exactly as the Phase 3 widgets do (overlay.cpp iter-524).
//     When several units share a type the method wires may act on a sibling.
//
// This is overlay-interactive.md honest-defer #2 (cursor-hit-unit detection):
// a documented blocker with a deliberate workaround, not a failure. When a
// future SWFOC_GetUnitAtScreenCoords or address->handle resolution wire lands,
// only InspectorUnitLuaExpr() changes — the five builders below stay put.
//
// RED-GREEN REGRESSION PINS (overlay_inspector_actions_test.cpp)
// -------------------------------------------------------------
//   - KILL TARGETS EXACT HANDLE : BuildInspectorKill's Lua embeds the picked
//                                 unit's exact handle as the obj_addr — a
//                                 "kill the first of this type" old form
//                                 would drop the address.
//   - HEAL WIRE NAME CORRECT    : the heal Lua names SWFOC_HealUnitLua — a
//                                 typo'd / wrong wire name fails.
//   - HEAL IS SINGLE-ARG        : the heal Lua is the no-arg :Heal() shape —
//                                 a two-arg old form (copied from Teleport)
//                                 fails.
//   - SWAPOWNER USES NEW OWNER  : BuildInspectorSwapOwner re-owns to the
//                                 REQUESTED slot, not the unit's current
//                                 owner — a "swap to current owner" old form
//                                 is a no-op and fails.
//   - INVULN BOOL TOGGLES       : (unit,true) emits "true" + a "Make Invuln"
//                                 label; (unit,false) emits "false" + a
//                                 "Clear Invuln" label.
//   - EXPR ESCAPES TYPE NAME    : a unit type carrying a quote is escaped in
//                                 the Find_First_Object literal — a raw-
//                                 concat old form breaks the Lua literal.
//   - LABEL NAMES THE UNIT      : every ActionRequest label embeds the unit
//                                 type, so the toast / recent-actions slot
//                                 identifies the unit, not a generic verb.
//
// Pure, header-only, std-only (<string>, plus the include chain). No ImGui,
// no Windows, no bridge. Unit-tested with a plain g++
// (build_inspector_actions_test.bat).
// =============================================================================

#pragma once

#include "overlay_inspector.h"     // UnitInfo, FactionName
#include "overlay_actions.h"       // Build*Command, LuaQuote
#include "overlay_action_queue.h"  // ActionRequest

#include <string>

namespace swfoc_overlay
{
    // ---- Faction-slot helpers ---------------------------------------------

    // Map an owner faction slot to the engine Find_Player() name. The engine's
    // Find_Player expects the UPPERCASE faction id ("REBEL" / "EMPIRE" /
    // "UNDERWORLD") — the same form the Phase 3 faction combo uses
    // (overlay.cpp kActionFactions[]). Slots 0 / 1 / 2 are in the same order
    // as overlay_inspector.h's FactionName(); any other slot yields the empty
    // string so a caller (or the bridge) can detect an invalid request rather
    // than silently re-own a unit to the wrong side.
    inline const char* FactionPlayerName(int slot)
    {
        switch (slot)
        {
            case 0:  return "REBEL";
            case 1:  return "EMPIRE";
            case 2:  return "UNDERWORLD";
            default: return "";
        }
    }

    // The next faction slot in the 0 -> 1 -> 2 -> 0 cycle — the "Swap Owner"
    // button's single-click default (cf. the Phase 6 faction-switch hotkey,
    // spec iter-305, which cycles ownership). Any out-of-range slot resets to
    // slot 0 so the cycle is total.
    inline int NextFactionSlot(int slot)
    {
        switch (slot)
        {
            case 0:  return 1;
            case 1:  return 2;
            case 2:  return 0;
            default: return 0;
        }
    }

    // ---- Unit-expression seam ---------------------------------------------

    // The best-available Lua expression that resolves the inspected unit, for
    // the four engine-METHOD wires (Heal / Teleport / SwapOwner / MakeInvuln)
    // which take a unit Lua expression rather than a numeric address. Falls
    // back to Find_First_Object("<type>") — see the EXACT-UNIT TARGETING note
    // at the top of this header. The type name is run through LuaQuote so a
    // name carrying a quote or backslash cannot break the Lua string literal.
    //
    // This is the single seam: when an address->handle resolution wire lands,
    // this one function changes and the five builders below are untouched.
    inline std::string InspectorUnitLuaExpr(const UnitInfo& unit)
    {
        return std::string("Find_First_Object(") +
               LuaQuote(std::string(unit.type)) + ")";
    }

    // ---- Inspector action builders ----------------------------------------
    //
    // Each returns a dispatch-ready ActionRequest (overlay_action_queue.h):
    // `label` is the human-readable text for the footer toast / recent-actions
    // slot and always names the unit; `lua` is the exact bridge line built by
    // overlay_actions.h. The render glue only enqueues the result.

    // Kill the inspected unit. SWFOC_KillUnit is address-based, so this
    // targets the EXACT picked unit (UnitInfo::handle) — no first-of-type
    // ambiguity.
    inline ActionRequest BuildInspectorKill(const UnitInfo& unit)
    {
        ActionRequest req;
        req.label = std::string("Kill ") + unit.type;
        req.lua   = BuildKillUnitCommand(
            static_cast<unsigned long long>(unit.handle));
        return req;
    }

    // Heal the inspected unit to full via the engine :Heal() method. Resolves
    // the unit through InspectorUnitLuaExpr (first-of-type fallback).
    inline ActionRequest BuildInspectorHeal(const UnitInfo& unit)
    {
        ActionRequest req;
        req.label = std::string("Heal ") + unit.type;
        req.lua   = BuildHealUnitCommand(InspectorUnitLuaExpr(unit));
        return req;
    }

    // Teleport the inspected unit to a world position. The destination is
    // supplied by the caller — the iter-300 click pipeline can feed the
    // iter-298 pick ray's z=0 ground crossing straight in. Resolves the unit
    // through InspectorUnitLuaExpr (first-of-type fallback).
    inline ActionRequest BuildInspectorTeleport(const UnitInfo& unit,
                                                float x, float y, float z)
    {
        ActionRequest req;
        req.label = std::string("Teleport ") + unit.type;
        req.lua   = BuildTeleportUnitCommand(InspectorUnitLuaExpr(unit),
                                             x, y, z);
        return req;
    }

    // Re-own the inspected unit to faction slot `newOwnerSlot` — the engine's
    // full "swap sides" behaviour (iter-108 Change_Owner). The new owner is
    // the REQUESTED slot, never the unit's current owner; pass
    // NextFactionSlot(unit.owner) for a single-click cycle. Resolves the unit
    // through InspectorUnitLuaExpr (first-of-type fallback).
    inline ActionRequest BuildInspectorSwapOwner(const UnitInfo& unit,
                                                 int newOwnerSlot)
    {
        ActionRequest req;
        req.label = std::string("Swap Owner ") + unit.type + " -> " +
                    FactionName(newOwnerSlot);
        req.lua   = BuildChangeUnitOwnerCommand(
            InspectorUnitLuaExpr(unit), FactionPlayerName(newOwnerSlot));
        return req;
    }

    // Toggle the inspected unit's invulnerability via the engine
    // :Make_Invulnerable() method (the real hardpoint-propagating invuln, not
    // a byte-flip). `invulnerable` true sets it, false clears it; the label
    // reads "Make Invuln" / "Clear Invuln" accordingly. Resolves the unit
    // through InspectorUnitLuaExpr (first-of-type fallback).
    inline ActionRequest BuildInspectorMakeInvuln(const UnitInfo& unit,
                                                  bool invulnerable)
    {
        ActionRequest req;
        req.label = std::string(invulnerable ? "Make Invuln "
                                              : "Clear Invuln ") + unit.type;
        req.lua   = BuildMakeUnitInvulnCommand(InspectorUnitLuaExpr(unit),
                                               invulnerable);
        return req;
    }
}
