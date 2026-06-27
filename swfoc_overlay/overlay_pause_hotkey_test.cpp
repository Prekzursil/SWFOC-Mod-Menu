// =============================================================================
// swfoc_overlay/overlay_pause_hotkey_test.cpp — unit test for
// overlay_pause_hotkey.h (Phase 6 cont., iter 546 / spec iter-306).
//
// iter-306 is the F4 pause/resume hotkey: one press freezes the simulation,
// the next un-freezes it back to the running speed the operator was at.
// overlay_pause_hotkey.h holds the pure kernel — the PauseToggle state
// machine, the SWFOC_SetGameSpeed command builder, and the PHASE 2 PENDING
// wire-status surface. This test pins all of it so the deferred overlay.cpp
// F4 glue can depend on it build-only.
//
// The integration section runs the full operator cycle: open the overlay,
// pick a 4x speed preset, hit F4 to freeze a battle, bump the preset while
// frozen, hit F4 again to un-freeze — and confirms the footer always shows
// the honest current speed plus the PHASE 2 PENDING status.
//
// overlay_pause_hotkey.h is header-only and std-only (<cmath> / <string>,
// plus <cstdio> via overlay_actions.h and <deque> / <functional> / <mutex>
// via overlay_action_queue.h — -pthread is load-bearing for the threading
// runtime that include chain pulls in). No game, no pipe, no ImGui. Build +
// run via build_pause_hotkey_test.bat.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - TOGGLE PAUSES THEN RESUMES    : F4 alternates SetGameSpeed(0)/(running).
//   - RESUME RESTORES RUNNING SPEED : a 3x run speed survives a pause cycle.
//   - SET-SPEED-WHILE-PAUSED        : SetRunningSpeed while paused keeps the
//     DOESN'T UNPAUSE                 game paused.
//   - REJECT NON-POSITIVE SPEED     : 0 / negative / NaN / inf are refused.
//   - CURRENT SPEED REFLECTS PAUSE  : CurrentSpeed() is 0 iff paused.
//   - HOTKEY IS F4                  : the labels name F4.
//   - PLAIN NUMBER ARG              : the speed arg is a bare number literal.
//   - WIRE STATUS IS PHASE-2-PENDING: kGameSpeedWireStatus is Phase2Pending.
//   - BUILDAPPLY MIRRORS THE TOGGLE : BuildApply() == the last Toggle() out.
// =============================================================================

#include "overlay_pause_hotkey.h"

#include <cmath>
#include <cstddef>
#include <cstdio>
#include <limits>
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

    void ExpectStr(const char* name, const std::string& got, const char* want)
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

    using swfoc_overlay::ActionRequest;
    using swfoc_overlay::BuildSetGameSpeedCommand;
    using swfoc_overlay::GameSpeedWireStatus;
    using swfoc_overlay::GameSpeedWireStatusText;
    using swfoc_overlay::kGameSpeedWireStatus;
    using swfoc_overlay::kNormalGameSpeed;
    using swfoc_overlay::kPauseHotkeyFKey;
    using swfoc_overlay::kPausedGameSpeed;
    using swfoc_overlay::PauseToggle;
}

int main()
{
    std::printf("=== overlay_pause_hotkey.h — Phase 6 pause-hotkey kernel ===\n\n");

    // -------------------------------------------------------------------------
    // 1. TOGGLE PAUSES THEN RESUMES — F4 alternates freeze and un-freeze.
    // -------------------------------------------------------------------------
    std::printf("[1] TOGGLE PAUSES THEN RESUMES\n");
    {
        PauseToggle t;
        ExpectTrue("a fresh toggle starts un-paused", !t.IsPaused());
        ExpectTrue("a fresh toggle runs at the normal speed",
                   t.ResumeSpeed() == kNormalGameSpeed);

        const ActionRequest r1 = t.Toggle();
        ExpectTrue("the first F4 press pauses the game", t.IsPaused());
        ExpectStr("...and emits a SetGameSpeed(0) freeze", r1.lua,
                  "return SWFOC_SetGameSpeed(0)");
        ExpectContains("...with a 'Pause game' label", r1.label, "Pause game");

        const ActionRequest r2 = t.Toggle();
        ExpectTrue("the second F4 press resumes the game", !t.IsPaused());
        ExpectStr("...and emits a SetGameSpeed(1) resume", r2.lua,
                  "return SWFOC_SetGameSpeed(1)");
        ExpectContains("...with a 'Resume game' label", r2.label, "Resume game");
    }

    // -------------------------------------------------------------------------
    // 2. RESUME RESTORES THE RUNNING SPEED — not a hardcoded 1x.
    // -------------------------------------------------------------------------
    std::printf("\n[2] RESUME RESTORES THE RUNNING SPEED\n");
    {
        PauseToggle t;
        ExpectTrue("a 3x running speed is accepted", t.SetRunningSpeed(3.0f));
        ExpectTrue("...3 is now the resume speed", t.ResumeSpeed() == 3.0f);

        const ActionRequest paused = t.Toggle();
        ExpectStr("pausing from 3x still freezes the sim", paused.lua,
                  "return SWFOC_SetGameSpeed(0)");

        const ActionRequest resumed = t.Toggle();
        ExpectStr("resuming restores 3x — not 1x", resumed.lua,
                  "return SWFOC_SetGameSpeed(3)");
        ExpectContains("...and the resume label names the restored speed",
                       resumed.label, "3x");
        ExpectTrue("...the toggle is running again", !t.IsPaused());
    }

    // -------------------------------------------------------------------------
    // 3. SET-SPEED-WHILE-PAUSED DOESN'T UNPAUSE — a preset change while
    //    frozen updates the resume target but keeps the game paused.
    // -------------------------------------------------------------------------
    std::printf("\n[3] SET-SPEED-WHILE-PAUSED DOESN'T UNPAUSE\n");
    {
        PauseToggle t;
        t.Toggle();  // -> paused
        ExpectTrue("the toggle is paused", t.IsPaused());
        ExpectTrue("CurrentSpeed is 0 while paused",
                   t.CurrentSpeed() == kPausedGameSpeed);

        ExpectTrue("a preset change while paused is accepted",
                   t.SetRunningSpeed(2.0f));
        ExpectTrue("...the toggle is STILL paused", t.IsPaused());
        ExpectTrue("...CurrentSpeed stays 0 while paused",
                   t.CurrentSpeed() == kPausedGameSpeed);
        ExpectTrue("...but ResumeSpeed now remembers 2", t.ResumeSpeed() == 2.0f);

        const ActionRequest resumed = t.Toggle();
        ExpectStr("...the next resume uses the speed set while paused",
                  resumed.lua, "return SWFOC_SetGameSpeed(2)");
    }

    // -------------------------------------------------------------------------
    // 4. REJECT NON-POSITIVE RUNNING SPEED — 0 / negative / NaN / inf are
    //    refused; a resume can never silently restore a 0-or-garbage speed.
    // -------------------------------------------------------------------------
    std::printf("\n[4] REJECT NON-POSITIVE RUNNING SPEED\n");
    {
        PauseToggle t;
        ExpectTrue("a valid 4x speed is accepted first", t.SetRunningSpeed(4.0f));

        ExpectTrue("SetRunningSpeed(0) is refused", !t.SetRunningSpeed(0.0f));
        ExpectTrue("...the running speed is left at 4", t.ResumeSpeed() == 4.0f);

        ExpectTrue("SetRunningSpeed(-1) is refused", !t.SetRunningSpeed(-1.0f));
        ExpectTrue("...the running speed is STILL 4", t.ResumeSpeed() == 4.0f);

        ExpectTrue("SetRunningSpeed(NaN) is refused",
                   !t.SetRunningSpeed(std::nanf("")));
        ExpectTrue("SetRunningSpeed(+inf) is refused",
                   !t.SetRunningSpeed(std::numeric_limits<float>::infinity()));
        ExpectTrue("...the running speed survives every bad input",
                   t.ResumeSpeed() == 4.0f);

        ExpectTrue("a valid fractional speed is accepted",
                   t.SetRunningSpeed(0.5f));
        ExpectTrue("...the running speed is now 0.5", t.ResumeSpeed() == 0.5f);
    }

    // -------------------------------------------------------------------------
    // 5. CURRENT SPEED REFLECTS PAUSE STATE — drives the overlay footer.
    // -------------------------------------------------------------------------
    std::printf("\n[5] CURRENT SPEED REFLECTS PAUSE STATE\n");
    {
        PauseToggle t;
        t.SetRunningSpeed(2.0f);
        ExpectTrue("running: CurrentSpeed equals the running speed",
                   t.CurrentSpeed() == 2.0f);

        t.Toggle();  // -> paused
        ExpectTrue("paused: CurrentSpeed drops to 0",
                   t.CurrentSpeed() == kPausedGameSpeed);
        ExpectTrue("paused: ResumeSpeed still remembers 2",
                   t.ResumeSpeed() == 2.0f);

        t.Toggle();  // -> running
        ExpectTrue("resumed: CurrentSpeed is 2 again", t.CurrentSpeed() == 2.0f);
        ExpectTrue("resumed: the toggle reports un-paused", !t.IsPaused());
    }

    // -------------------------------------------------------------------------
    // 6. HOTKEY IS F4 — the constant and every label name F4.
    // -------------------------------------------------------------------------
    std::printf("\n[6] HOTKEY IS F4\n");
    {
        ExpectTrue("the pause-hotkey constant is 4", kPauseHotkeyFKey == 4);

        PauseToggle t;
        const ActionRequest paused = t.Toggle();
        ExpectContains("the pause label names F4", paused.label, "F4");
        ExpectAbsent("the pause label does NOT name F3", paused.label, "F3");

        const ActionRequest resumed = t.Toggle();
        ExpectContains("the resume label names F4", resumed.label, "F4");
        ExpectAbsent("the resume label does NOT name F5", resumed.label, "F5");
    }

    // -------------------------------------------------------------------------
    // 7. PLAIN NUMBER ARG — SetGameSpeed takes a bare number, never a quoted
    //    string and never a Find_* wrapper (contrast the SWFOC_*Lua wires).
    // -------------------------------------------------------------------------
    std::printf("\n[7] PLAIN NUMBER ARG\n");
    {
        ExpectStr("SetGameSpeed(0) is a bare 0", BuildSetGameSpeedCommand(0.0f),
                  "return SWFOC_SetGameSpeed(0)");
        ExpectStr("SetGameSpeed(1) is a bare 1", BuildSetGameSpeedCommand(1.0f),
                  "return SWFOC_SetGameSpeed(1)");
        ExpectStr("SetGameSpeed(2) is a bare 2", BuildSetGameSpeedCommand(2.0f),
                  "return SWFOC_SetGameSpeed(2)");
        ExpectStr("SetGameSpeed(0.5) keeps the fraction",
                  BuildSetGameSpeedCommand(0.5f),
                  "return SWFOC_SetGameSpeed(0.5)");
        ExpectAbsent("no double-quote wraps the speed arg",
                     BuildSetGameSpeedCommand(2.0f), "\"");
        ExpectAbsent("no Find_* expression wraps the speed arg",
                     BuildSetGameSpeedCommand(2.0f), "Find_");
    }

    // -------------------------------------------------------------------------
    // 8. WIRE STATUS IS PHASE-2-PENDING — the honest-defer surface.
    // -------------------------------------------------------------------------
    std::printf("\n[8] WIRE STATUS IS PHASE-2-PENDING\n");
    {
        ExpectTrue("SWFOC_SetGameSpeed is still PHASE 2 PENDING",
                   kGameSpeedWireStatus == GameSpeedWireStatus::Phase2Pending);
        ExpectStr("the footer status text reads PHASE 2 PENDING",
                  std::string(GameSpeedWireStatusText()), "PHASE 2 PENDING");
        ExpectTrue("Live and Phase2Pending are distinct statuses",
                   GameSpeedWireStatus::Live !=
                       GameSpeedWireStatus::Phase2Pending);
    }

    // -------------------------------------------------------------------------
    // 9. BUILDAPPLY MIRRORS THE TOGGLE STATE — the shared core of Toggle().
    // -------------------------------------------------------------------------
    std::printf("\n[9] BUILDAPPLY MIRRORS THE TOGGLE STATE\n");
    {
        PauseToggle t;
        t.SetRunningSpeed(2.0f);
        ExpectStr("running: BuildApply re-asserts the running speed",
                  t.BuildApply().lua, "return SWFOC_SetGameSpeed(2)");

        const ActionRequest toggled = t.Toggle();   // -> paused
        const ActionRequest applied = t.BuildApply();
        ExpectStr("Toggle and a following BuildApply emit the same lua",
                  applied.lua, toggled.lua.c_str());
        ExpectStr("Toggle and a following BuildApply emit the same label",
                  applied.label, toggled.label.c_str());
        ExpectStr("...and that lua is the pause freeze", applied.lua,
                  "return SWFOC_SetGameSpeed(0)");
    }

    // -------------------------------------------------------------------------
    // INTEGRATION — the full F4 operator cycle.
    //   Operator opens the overlay, picks a 4x speed preset, hits F4 to freeze
    //   a battle, bumps the preset to 2x while frozen, hits F4 to un-freeze.
    // -------------------------------------------------------------------------
    std::printf("\n[*] INTEGRATION — the full F4 operator cycle\n");
    {
        PauseToggle session;

        // Operator picks the "4x" speed preset.
        ExpectTrue("integration: the operator selects a 4x running speed",
                   session.SetRunningSpeed(4.0f));
        ExpectTrue("integration: the footer shows 4x while running",
                   session.CurrentSpeed() == 4.0f);

        // Operator hits F4 to freeze the battle and line up a maneuver.
        const ActionRequest freeze = session.Toggle();
        ExpectTrue("integration: F4 pauses the game", session.IsPaused());
        ExpectStr("integration: the dispatched command freezes the sim",
                  freeze.lua, "return SWFOC_SetGameSpeed(0)");
        ExpectTrue("integration: the footer shows 0 while paused",
                   session.CurrentSpeed() == kPausedGameSpeed);

        // While frozen, the operator bumps the preset down to 2x.
        ExpectTrue("integration: a preset change while paused is accepted",
                   session.SetRunningSpeed(2.0f));
        ExpectTrue("integration: ...but the game stays paused",
                   session.IsPaused());

        // Operator hits F4 again to un-freeze.
        const ActionRequest unfreeze = session.Toggle();
        ExpectTrue("integration: the second F4 resumes the game",
                   !session.IsPaused());
        ExpectStr("integration: resume honors the 2x set while paused",
                  unfreeze.lua, "return SWFOC_SetGameSpeed(2)");
        ExpectContains("integration: the resume label shows 2x",
                       unfreeze.label, "2x");
        ExpectTrue("integration: the footer shows 2x again",
                   session.CurrentSpeed() == 2.0f);

        // The footer also surfaces the honest SetGameSpeed wire status.
        ExpectStr("integration: the footer warns PHASE 2 PENDING",
                  std::string(GameSpeedWireStatusText()), "PHASE 2 PENDING");

        // A standalone re-assert (no toggle) emits the current running speed.
        const ActionRequest reassert = session.BuildApply();
        ExpectStr("integration: BuildApply re-asserts the running speed",
                  reassert.lua, "return SWFOC_SetGameSpeed(2)");
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    if (g_failures == 0)
    {
        std::printf("=== PAUSE-HOTKEY TEST: ALL PASS ===\n");
        return 0;
    }
    std::printf("=== PAUSE-HOTKEY TEST: %d FAILURE(S) ===\n", g_failures);
    return 1;
}
