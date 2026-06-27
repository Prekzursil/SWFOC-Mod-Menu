// =============================================================================
// swfoc_overlay/overlay_pause_hotkey.h — Phase 6 pause/resume-hotkey kernel.
//
// Phase 6 (iter 304-307) expands the overlay's hotkey surface into power-user
// features. iter-306 (spec line 64) is the PAUSE / RESUME hotkey bound to F4:
//
//   - One F4 press freezes the simulation (game speed -> 0).
//   - The next F4 press un-freezes it, restoring the running speed the
//     operator was at before the pause.
//   - The overlay footer shows the current game speed so the operator
//     always knows whether time is moving.
//
// This header is the pure kernel: the F4 toggle state machine (PauseToggle),
// the SWFOC_SetGameSpeed command builder, and the SetGameSpeed delivery
// status. The overlay.cpp glue (deferred, build-only verifiable — exactly
// like every Phase 5/6 kernel iter) only polls F4 and hands Toggle()'s
// ActionRequest to the iter-513 ActionQueue; the deciding logic is here so a
// unit test pins it before the input path depends on it.
//
// WIRE FACTS + HONEST DEFER (swfoc_lua_bridge/lua_bridge.cpp, re-read 2026-05-21)
// -----------------------------------------------------------------------------
//   SWFOC_SetGameSpeed(speed) IS a registered, callable bridge wire
//   (Lua_SetGameSpeed, registered :8217). But its Phase-1 body only RECORDS
//   the requested speed into g_pendingGameSpeed and returns
//       "OK: game speed recorded (Phase 2 hook pending)"
//   — the engine's SimulationRate global is NOT yet patched. lua_bridge.cpp
//   :3292 documents the Phase 2 RE blocker (the SimulationRate global was
//   searched 2026-04-23, no direct string hit). So a bridge call that
//   "succeeds" does NOT freeze or slow the game today.
//
//   The editor mirrors this: BridgeDirectorDispatcher.cs and
//   BridgeSpeedDispatcher.cs both label SWFOC_SetGameSpeed
//   "PHASE 2 PENDING — UI command disabled".
//
//   Per overlay-interactive.md line 64, iter-306 ships the F4 hotkey wire-up
//   ANYWAY: the toggle, the command builder, and the footer speed display
//   are all real and tested. The kernel surfaces the SetGameSpeed status
//   (operator-trust pattern, guardrail 1007) via GameSpeedWireStatusText()
//   so the overlay footer warns "PHASE 2 PENDING" instead of pretending the
//   game froze. The MOMENT the editor-100 spec flips SetGameSpeed LIVE, a
//   maintainer flips kGameSpeedWireStatus to Live — the WIRE STATUS pin in
//   the test fires to remind them — and the F4 hotkey goes LIVE with zero
//   further kernel change.
//
// RED-GREEN REGRESSION PINS (overlay_pause_hotkey_test.cpp)
// --------------------------------------------------------
//   - TOGGLE PAUSES THEN RESUMES   : the first F4 emits SetGameSpeed(0), the
//                                    second emits SetGameSpeed(running) — an
//                                    old form stuck on SetGameSpeed(0) fails.
//   - RESUME RESTORES RUNNING SPEED: a 3x running speed is restored on
//                                    resume — an old form hardcoding 1x fails.
//   - SET-SPEED-WHILE-PAUSED       : SetRunningSpeed while paused updates the
//     DOESN'T UNPAUSE                resume target WITHOUT un-pausing — an old
//                                    form that un-pauses on SetRunningSpeed
//                                    fails.
//   - REJECT NON-POSITIVE SPEED    : SetRunningSpeed rejects 0 / negative /
//                                    NaN / inf and leaves the speed intact —
//                                    an old form that stores 0 (making resume
//                                    a silent no-op) fails.
//   - CURRENT SPEED REFLECTS PAUSE : CurrentSpeed() is 0 while paused, the
//                                    running speed while running — drives the
//                                    footer; an old form ignoring pause fails.
//   - HOTKEY IS F4                 : the labels name F4 — an old form labeling
//                                    F3 / F5 fails.
//   - PLAIN NUMBER ARG             : BuildSetGameSpeedCommand emits a bare
//                                    number literal — a copy-from-SWFOC_*Lua
//                                    old form that LuaQuotes the speed fails.
//   - WIRE STATUS IS PHASE-2-PENDING: kGameSpeedWireStatus is Phase2Pending —
//                                    flips (and the test reminds a maintainer)
//                                    only when SetGameSpeed goes LIVE.
//
// THREADING: PauseToggle is touched ONLY by the render / input thread —
// Toggle() at the F4 hotkey site, CurrentSpeed() / IsPaused() while drawing
// the footer. The background action worker drains the ActionQueue but never
// touches this state. Render-thread-confined, so — like
// overlay_camera_bookmarks.h's CameraBookmarks — it needs no mutex.
//
// Pure, header-only, std-only. Reuses FormatCoord from overlay_actions.h and
// ActionRequest from overlay_action_queue.h — no new type, no ImGui, no
// Windows, no bridge. Unit-tested with a plain g++ (build_pause_hotkey_test.bat).
// =============================================================================

#pragma once

#include "overlay_actions.h"       // FormatCoord
#include "overlay_action_queue.h"  // ActionRequest

#include <cmath>
#include <string>

namespace swfoc_overlay
{
    // The function-key bound to the pause/resume toggle — F4, per
    // overlay-interactive.md line 64.
    inline constexpr int kPauseHotkeyFKey = 4;

    // The two well-known game-speed values the F4 toggle moves between.
    // 0 freezes the simulation (no time passes); 1 is the engine's
    // normalized real-time pace and the default running speed.
    inline constexpr float kPausedGameSpeed = 0.0f;
    inline constexpr float kNormalGameSpeed = 1.0f;

    // SWFOC_SetGameSpeed delivery status — see the file header's WIRE FACTS
    // block for the full Phase-1-records / Phase-2-hook-pending story.
    enum class GameSpeedWireStatus
    {
        Live,           // the SimulationRate hook is patched; F4 freezes time.
        Phase2Pending,  // the wire records the speed only; the game keeps running.
    };

    // Current SWFOC_SetGameSpeed status. Flip to GameSpeedWireStatus::Live
    // when the Phase 2 SimulationRate hook lands — the WIRE STATUS pin in
    // overlay_pause_hotkey_test.cpp fires to remind whoever lands it.
    inline constexpr GameSpeedWireStatus kGameSpeedWireStatus =
        GameSpeedWireStatus::Phase2Pending;

    // A short human-readable status tag for the overlay footer. "PHASE 2
    // PENDING" deliberately matches the editor's CapabilityAwareAction
    // wording (BridgeSpeedDispatcher.cs) so the two surfaces read identically.
    inline const char* GameSpeedWireStatusText()
    {
        return kGameSpeedWireStatus == GameSpeedWireStatus::Live
                   ? "LIVE"
                   : "PHASE 2 PENDING";
    }

    // Build the Lua line that sets the game speed via the SWFOC_SetGameSpeed
    // wire. SetGameSpeed takes ONE plain Lua NUMBER argument — like
    // SWFOC_SetCameraPos (overlay_camera_bookmarks.h) and unlike the
    // SWFOC_*Lua write wires, there is NO nested-expression quoting. The
    // speed is formatted by overlay_actions.h::FormatCoord so a whole number
    // renders "0" / "1" not "0.000" and the preview stays readable.
    inline std::string BuildSetGameSpeedCommand(float speed)
    {
        return "return SWFOC_SetGameSpeed(" + FormatCoord(speed) + ")";
    }

    // The Phase 6 pause/resume toggle. Tracks whether the overlay believes
    // the game is paused and what running speed a resume restores. See the
    // file header for the full contract and the render-thread-confined
    // threading note.
    class PauseToggle
    {
    public:
        // Is the game paused, as far as this toggle tracks?
        bool IsPaused() const { return paused_; }

        // The running speed a resume restores — always > 0 and finite
        // (SetRunningSpeed rejects every other input).
        float ResumeSpeed() const { return resumeSpeed_; }

        // The speed the toggle currently represents: 0 while paused, else
        // the running speed. Drives the overlay footer's speed display.
        float CurrentSpeed() const
        {
            return paused_ ? kPausedGameSpeed : resumeSpeed_;
        }

        // Record the running speed the operator selected (e.g. a speed
        // preset). A running speed MUST be a positive finite number — 0
        // means "pause", which is Toggle()'s job, not this method's; a
        // negative / NaN / inf is invalid input. SetRunningSpeed rejects all
        // of those (returns false, leaves resumeSpeed_ untouched) so a
        // resume can never silently restore a 0-or-garbage speed.
        //
        // While running, the new speed takes effect on the next BuildApply()
        // / Toggle(); while paused, it only updates what the next resume
        // restores — it does NOT un-pause (the SET-SPEED-WHILE-PAUSED
        // DOESN'T UNPAUSE pin).
        bool SetRunningSpeed(float speed)
        {
            if (!(speed > 0.0f) || !std::isfinite(speed)) return false;
            resumeSpeed_ = speed;
            return true;
        }

        // Toggle pause <-> resume and return the ActionRequest to dispatch:
        //   running -> paused : SetGameSpeed(0),           "Pause game (F4)"
        //   paused  -> running: SetGameSpeed(running),     "Resume game (F4, Nx)"
        ActionRequest Toggle()
        {
            paused_ = !paused_;
            return BuildApply();
        }

        // Build the dispatch request for the CURRENT toggle state WITHOUT
        // changing it — the shared core of Toggle(), also usable to re-assert
        // the speed after an engine event. The label describes the command:
        // a paused state freezes ("Pause game"), a running state asserts the
        // running speed ("Resume game").
        ActionRequest BuildApply() const
        {
            ActionRequest req;
            req.lua   = BuildSetGameSpeedCommand(CurrentSpeed());
            req.label = paused_ ? PauseLabel() : ResumeLabel(resumeSpeed_);
            return req;
        }

    private:
        // "Pause game (F4)" — names the bound key so the recent-actions
        // toolbar reads naturally.
        static std::string PauseLabel()
        {
            return std::string("Pause game (F") +
                   static_cast<char>('0' + kPauseHotkeyFKey) + ")";
        }

        // "Resume game (F4, 2x)" — includes the running speed being asserted.
        static std::string ResumeLabel(float speed)
        {
            return std::string("Resume game (F") +
                   static_cast<char>('0' + kPauseHotkeyFKey) + ", " +
                   FormatCoord(speed) + "x)";
        }

        bool  paused_      = false;
        float resumeSpeed_ = kNormalGameSpeed;
    };
}
