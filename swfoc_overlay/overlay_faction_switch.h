// =============================================================================
// swfoc_overlay/overlay_faction_switch.h — Phase 6 faction-switch hotkey kernel.
//
// Phase 6 (iter 304-307) expands the overlay's hotkey surface into power-user
// features. iter-305 (spec line 63) is the F3 FACTION-SWITCH HOTKEY: one press
// re-owns the local player's whole visible army to the next faction in the
// REBEL -> EMPIRE -> UNDERWORLD -> REBEL cycle.
//
// Because flipping an entire army mid-battle is irreversible, iter-305's
// headline requirement is a CONFIRM-PROMPT — "no accidental mass-defection"
// (spec line 63). F3 only ARMS a pending switch; a second, deliberate confirm
// control commits it. This header is the pure kernel of both halves:
//
//   1. FactionSwitchPrompt    — the Idle/Armed confirm state machine. Arm()
//                               on F3, Confirm() / Cancel() on the operator's
//                               decision.
//   2. BuildFactionSwitchBatch() — turns the visible-unit set into one
//                               SWFOC_ChangeUnitOwner ActionRequest per
//                               affected unit.
//   3. CountUnitsOwnedBy()    — sizes the confirm prompt; pinned equal to the
//                               batch size so the prompt never lies.
//
// The overlay.cpp glue (deferred, build-only verifiable — exactly like every
// Phase 5/6 kernel iter) polls F3, draws the prompt, and on confirm hands each
// batch ActionRequest to the iter-513 ActionQueue. The deciding logic is here
// so a unit test pins it before the input path depends on it.
//
// WIRE FACTS (swfoc_lua_bridge/lua_bridge.cpp, via overlay_actions.h)
// ------------------------------------------------------------------
//   SWFOC_ChangeUnitOwner(unit_lua_expr, player_lua_expr) -> :Change_Owner
//       iter-108 LIVE. Two Lua-expression-string args. The batch reuses
//       overlay_actions.h::BuildChangeUnitOwnerCommand, so the two-level
//       quoting is already pinned by overlay_actions_test.cpp.
//
// EXACT-UNIT TARGETING — THE HONEST LIMITATION (honest-defer #2)
// -------------------------------------------------------------
// SWFOC_ChangeUnitOwner is an engine-METHOD wire: it takes a unit Lua
// *expression*, not an address, and SWFOC exposes no "object from address"
// engine-Lua function. So the batch resolves each unit through
// overlay_inspector_actions.h::InspectorUnitLuaExpr — Find_First_Object(
// "<type>") — the SAME honest-defer #2 seam the Phase 5 inspector uses.
//
// CONSEQUENCE: when several visible units share a type, every per-unit request
// for that type resolves to the engine's FIRST instance of it. Today a bulk
// faction switch therefore flips one representative per affected type, not
// literally every unit. The batch is still built ONE-PER-UNIT (spec line 63:
// "per unit") so the moment an address->handle resolution wire lands,
// InspectorUnitLuaExpr changes in one place and the bulk switch becomes a
// genuine whole-army defection with ZERO kernel change here.
//
// RED-GREEN REGRESSION PINS (overlay_faction_switch_test.cpp)
// ----------------------------------------------------------
//   - ARM REQUIRES UNITS       : Arm(slot, 0) is refused — an empty confirm
//                                prompt is meaningless; an old form that
//                                armed with nothing to switch fails.
//   - DOUBLE-TAP DOESN'T SKIP  : Arm while already Armed is a no-op — a
//                                second F3 must not advance the cycle again
//                                before the operator confirms.
//   - CONFIRM GATES THE BATCH  : Confirm() returns false unless Armed; the
//                                batch is dispatched only on a true Confirm.
//   - CANCEL ABORTS THE SWITCH : after Cancel() a Confirm() returns false —
//                                an old form where cancel left it armed fails.
//   - BATCH SKIPS NON-OWNED    : BuildFactionSwitchBatch re-owns ONLY units
//                                owned by fromSlot — an enemy unit is not
//                                yours to defect; an old form flipping every
//                                visible unit fails.
//   - BATCH RE-OWNS TO NEXT    : each request re-owns to the NEXT faction —
//                                an old form re-owning to the current faction
//                                is a no-op and fails.
//   - COUNT MATCHES BATCH SIZE : CountUnitsOwnedBy(units, fromSlot) equals
//                                BuildFactionSwitchBatch(...).size() — the
//                                confirm prompt never lies about how many
//                                units the commit will dispatch.
//   - EXPR ESCAPES TYPE NAME   : a unit type carrying a quote is escaped in
//                                the Find_First_Object literal (inherited
//                                from InspectorUnitLuaExpr).
//
// THREADING: FactionSwitchPrompt is touched ONLY by the render / input thread
// — Arm() at the F3 site, Confirm()/Cancel() at the prompt controls, the
// getters while drawing. The background action worker drains the ActionQueue
// but never touches this state. Render-thread-confined, so — like
// overlay_camera_bookmarks.h's CameraBookmarks — it needs no mutex.
//
// Pure, header-only, std-only (<cstddef>, <string>, <vector>, plus the
// include chain). No ImGui, no Windows, no bridge. Unit-tested with a plain
// g++ (build_faction_switch_test.bat).
// =============================================================================

#pragma once

#include "overlay_inspector_actions.h"  // UnitInfo, ActionRequest,
                                        // NextFactionSlot, FactionPlayerName,
                                        // FactionName, InspectorUnitLuaExpr,
                                        // BuildChangeUnitOwnerCommand

#include <cstddef>
#include <string>
#include <vector>

namespace swfoc_overlay
{
    // The three SWFOC faction slots (0 Rebel / 1 Empire / 2 Underworld) — the
    // valid range for a local-player faction. Mirrors the slot vocabulary of
    // overlay_inspector.h::FactionName and overlay_inspector_actions.h.
    inline constexpr int kFactionSlotCount = 3;

    // Count the units in `units` currently owned by faction slot `slot`. The
    // overlay glue calls this on F3 to size the confirm prompt; a test pins it
    // equal to BuildFactionSwitchBatch(...).size() (when fromSlot != toSlot)
    // so the prompt and the dispatched batch can never disagree.
    inline std::size_t CountUnitsOwnedBy(const std::vector<UnitInfo>& units,
                                         int slot)
    {
        std::size_t n = 0;
        for (const UnitInfo& u : units)
        {
            if (u.owner == slot) ++n;
        }
        return n;
    }

    // Build the bulk faction-switch batch: one SWFOC_ChangeUnitOwner
    // ActionRequest per visible unit currently owned by `fromSlot`, each
    // re-owning that unit to `toSlot`. Units owned by any other faction are
    // skipped (an enemy unit is not the local player's to defect).
    //
    // Returns an EMPTY batch — a deliberate no-op — when fromSlot or toSlot is
    // outside [0, kFactionSlotCount), or when fromSlot == toSlot (a cycle that
    // would re-own a unit to its own faction).
    //
    // Each request resolves its unit through InspectorUnitLuaExpr (honest-defer
    // #2 — see the file header) and embeds a 1-based ordinal + the unit type in
    // its label so the recent-actions toast distinguishes same-type units.
    inline std::vector<ActionRequest> BuildFactionSwitchBatch(
        const std::vector<UnitInfo>& units, int fromSlot, int toSlot)
    {
        std::vector<ActionRequest> batch;
        if (fromSlot < 0 || fromSlot >= kFactionSlotCount) return batch;
        if (toSlot   < 0 || toSlot   >= kFactionSlotCount) return batch;
        if (fromSlot == toSlot) return batch;

        std::size_t ordinal = 0;
        for (const UnitInfo& unit : units)
        {
            if (unit.owner != fromSlot) continue;
            ++ordinal;
            ActionRequest req;
            req.label = std::string("Faction switch #") +
                        std::to_string(ordinal) + " " + unit.type + " " +
                        FactionName(fromSlot) + " -> " + FactionName(toSlot);
            req.lua = BuildChangeUnitOwnerCommand(InspectorUnitLuaExpr(unit),
                                                  FactionPlayerName(toSlot));
            batch.push_back(req);
        }
        return batch;
    }

    // The Phase 6 faction-switch confirm-prompt state machine. F3 Arm()s a
    // pending bulk switch; the operator then Confirm()s or Cancel()s it. See
    // the file header for the full no-accidental-mass-defection contract and
    // the render-thread-confined threading note.
    class FactionSwitchPrompt
    {
    public:
        // Idle  — no pending switch; F3 will Arm one.
        // Armed — a switch is staged and waiting for the operator's decision.
        enum class State { Idle, Armed };

        State state() const { return state_; }
        bool  IsArmed() const { return state_ == State::Armed; }

        // F3 pressed. Stage a bulk faction switch from `fromSlot` (the local
        // player's current faction) to NextFactionSlot(fromSlot), affecting
        // `affectedCount` visible units. Returns true when the prompt armed.
        //
        // Refused (stays Idle, returns false) when:
        //   - already Armed         : a double-tap of F3 must not advance the
        //                             cycle a second time before the operator
        //                             confirms the first (DOUBLE-TAP pin).
        //   - affectedCount == 0    : nothing to switch — no empty prompt.
        //   - fromSlot out of range : a bad local-player slot.
        //
        // `affectedCount` is a snapshot taken for the prompt text. The glue
        // rebuilds the batch from the LIVE unit list at Confirm() time, so a
        // unit that died mid-prompt is simply absent from the rebuilt batch.
        bool Arm(int fromSlot, std::size_t affectedCount)
        {
            if (state_ == State::Armed) return false;
            if (fromSlot < 0 || fromSlot >= kFactionSlotCount) return false;
            if (affectedCount == 0) return false;
            fromSlot_      = fromSlot;
            toSlot_        = NextFactionSlot(fromSlot);
            affectedCount_ = affectedCount;
            state_         = State::Armed;
            return true;
        }

        int         fromSlot() const { return fromSlot_; }
        int         toSlot() const { return toSlot_; }
        std::size_t affectedCount() const { return affectedCount_; }

        // Operator confirmed the staged switch. Returns true when a switch was
        // armed — the caller then builds the batch (BuildFactionSwitchBatch
        // with fromSlot()/toSlot()) and enqueues it — and resets to Idle. A
        // Confirm() with nothing armed returns false and is otherwise inert,
        // so the batch is dispatched ONLY on a true Confirm() (CONFIRM GATES
        // THE BATCH pin). Confirm() resets only the State; fromSlot()/toSlot()
        // stay readable so the caller may build the batch before or after it.
        bool Confirm()
        {
            if (state_ != State::Armed) return false;
            state_ = State::Idle;
            return true;
        }

        // Operator cancelled (Esc, or the visible-unit set emptied out). Drops
        // the staged switch and returns to Idle with nothing dispatched. Inert
        // when already Idle.
        void Cancel()
        {
            state_ = State::Idle;
        }

        // The confirm-prompt question the overlay draws while Armed, e.g.
        // "Faction switch: re-own 47 unit(s) from Rebel to Empire?". An Idle
        // prompt has no question and yields the empty string.
        std::string PromptText() const
        {
            if (state_ != State::Armed) return std::string();
            return std::string("Faction switch: re-own ") +
                   std::to_string(affectedCount_) + " unit(s) from " +
                   FactionName(fromSlot_) + " to " + FactionName(toSlot_) + "?";
        }

    private:
        State       state_         = State::Idle;
        int         fromSlot_      = 0;
        int         toSlot_        = 0;
        std::size_t affectedCount_ = 0;
    };
}
