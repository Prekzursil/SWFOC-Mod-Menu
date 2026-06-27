// =============================================================================
// swfoc_overlay/overlay_phase6_close_test.cpp — Phase 6 close-out integration
// test (Phase 6 close-out part 2/2, iter 548 / spec iter-307).
//
// Phase 6 (iter 304-307) expanded the overlay's hotkey surface into power-user
// features. It shipped one kernel per iteration, each with its own dedicated
// unit test:
//
//   iter-304  overlay_camera_bookmarks.h  F6/F7/F8 bookmark store + recall  82/0
//   iter-305  overlay_faction_switch.h    F3 bulk faction-switch + confirm  94/0
//   iter-306  overlay_pause_hotkey.h      F4 pause/resume toggle            67/0
//   iter-307  overlay_hotkey_matrix.h     F1..F12 intercept-or-passthrough 145/0
//          part 1/2                       conflict matrix (the router)
//
// Those four tests pin each kernel — and the matrix — IN ISOLATION (388 checks
// total). This file is the Phase 6 CLOSE-OUT test: it wires the three feature
// kernels and the conflict matrix TOGETHER and exercises the complete
// hotkey-routing pipeline exactly as overlay.cpp's deferred WndProc detour
// runs it — a WM_KEYDOWN arrives, the matrix decides intercept-or-passthrough
// (OverlayInterceptsKey), and on intercept the F-key is routed to its kernel.
// Its value is the SEAM between the matrix-as-router and the three kernels —
// the part no isolation test can see.
//
// Naming note (carried from iter-527's Phase 3, iter-533's Phase 4 and
// iter-542's Phase 5 close-outs): the spec iter-307 row writes
// "Iter307Phase6HotkeysTests.cs". The `.cs` name predates the overlay's
// all-C++ native-exe test infra — a C# test cannot exercise a C++ header.
// This file IS the spec iter-307 close-out test in the established pattern
// (overlay_phase4_close_test.cpp, overlay_phase5_close_test.cpp, ...).
//
// SPEC iter-307 RED-GREEN PINS (overlay-interactive.md acceptance line 39)
// -----------------------------------------------------------------------
//   [1] EVERY OVERLAY HOTKEY ROUTES TO ITS KERNEL : the matrix's F3 / F4 /
//       F6 / F7 / F8 rows agree, key-for-key, with each kernel's own hotkey
//       constant (kPauseHotkeyFKey, CameraBookmarkHotkey). A matrix that
//       drifted from a kernel — or a kernel that bound a key the matrix does
//       not catalogue — fails.
//   [2] THE MATRIX GATES WHETHER A HOTKEY FIRES : a key the matrix passes
//       through never reaches a kernel. With the overlay VISIBLE F3 / F4 /
//       F6 / F7 / F8 fire; with it HIDDEN they all revert to passthrough and
//       the kernels stay untouched — only F1 fires while hidden. An old form
//       that swallowed an overlay hotkey off-screen starves the game and
//       fails.
//   [3] EACH KERNEL'S HOTKEY ACTION IS DISPATCH-READY : a routed F4 builds a
//       real `return SWFOC_SetGameSpeed` line; a routed F6 a real
//       `return SWFOC_SetCameraPos` line; a routed-then-confirmed F3 a batch
//       of real `return SWFOC_ChangeUnitOwner` lines. An F6 on an UNSET slot
//       builds an empty-Lua request the router must NOT enqueue.
//   [4] OPERATOR-TRUST STATUS IS CONSISTENT ACROSS THE SEAM : the matrix's
//       F4 row status mirrors overlay_pause_hotkey.h::kGameSpeedWireStatus
//       (Phase2Pending today) — the matrix never claims LIVE for a
//       PHASE-2-PENDING wire; every Intercept row carries a bound
//       Live/Phase2Pending status.
//
// All five headers are header-only and std-only. Build + run via
// build_phase6_close_test.bat — no game, no pipe, no ImGui, no bridge.
// =============================================================================

#include "overlay_hotkey_matrix.h"
#include "overlay_camera_bookmarks.h"
#include "overlay_faction_switch.h"
#include "overlay_pause_hotkey.h"
#include "overlay_action_queue.h"

#include <cstddef>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

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

    void ExpectFalse(const char* name, bool cond)
    {
        ExpectTrue(name, !cond);
    }

    // Compare two integer-valued quantities. `long long` so engine handles,
    // array counts, faction slots and VK codes all feed it without a
    // signed/unsigned warning under -Wextra.
    void ExpectIntEq(const char* name, long long got, long long want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got %lld  want %lld\n",
                        name, got, want);
        }
    }

    void ExpectStrEq(const char* name, const char* got, const char* want)
    {
        ++g_checks;
        if (got != nullptr && want != nullptr && std::strcmp(got, want) == 0)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got \"%s\"  want \"%s\"\n", name,
                        got != nullptr ? got : "(null)",
                        want != nullptr ? want : "(null)");
        }
    }

    // True when `hay` contains `needle` as a substring.
    void ExpectContains(const char* name, const std::string& hay,
                        const char* needle)
    {
        ++g_checks;
        if (hay.find(needle) != std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\" does not contain \"%s\"\n",
                        name, hay.c_str(), needle);
        }
    }

    // True when `hay` begins with `prefix`.
    void ExpectStartsWith(const char* name, const std::string& hay,
                          const char* prefix)
    {
        ++g_checks;
        if (hay.rfind(prefix, 0) == 0)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\" does not start with \"%s\"\n",
                        name, hay.c_str(), prefix);
        }
    }

    using swfoc_overlay::ActionRequest;
    using swfoc_overlay::CameraBookmarks;
    using swfoc_overlay::FactionSwitchPrompt;
    using swfoc_overlay::PauseToggle;
    using swfoc_overlay::UnitInfo;

    // Build a visible-unit record with just the two fields the Phase 6
    // faction-switch kernel reads — owner faction slot and type name. Mirrors
    // overlay_faction_switch_test.cpp's MakeUnit. UnitInfo is a POD; the rest
    // is value-initialised to zero.
    UnitInfo MakeUnit(int owner, const char* type)
    {
        UnitInfo u{};
        u.owner = owner;
        swfoc_overlay::SetUnitType(u, type);
        return u;
    }

    // -------------------------------------------------------------------------
    // OverlayHotkeyModel / PressHotkey — a faithful in-memory model of the
    // overlay.cpp Phase 6 hotkey input flow, so the close-out test can drive
    // the matrix and the three kernels TOGETHER through the exact decision
    // sequence the (deferred) WndProc detour runs on a WM_KEYDOWN. The glue
    // itself (the message hook, the ImGui prompt, the footer) is build-only;
    // this models its DECISION sequence, which is the part with a right answer.
    //
    // The chain reproduced here, in order:
    //   iter-307  OverlayInterceptsKey : the matrix is the SINGLE gate — a key
    //                                    it passes through never reaches a
    //                                    kernel, so the game keeps it.
    //   iter-304  CameraBookmarks      : an intercepted F6/F7/F8 recalls a
    //                                    bookmark via BuildRecall.
    //   iter-305  FactionSwitchPrompt  : an intercepted F3 ARMS the confirm
    //                                    prompt (the commit is a later step).
    //   iter-306  PauseToggle          : an intercepted F4 toggles pause.
    //   iter-513  ActionQueue          : every built ActionRequest is enqueued
    //                                    for the off-thread bridge drain.
    // -------------------------------------------------------------------------
    struct OverlayHotkeyModel
    {
        bool                overlayVisible = true;
        PauseToggle         pause;
        CameraBookmarks     bookmarks;
        FactionSwitchPrompt faction;
        swfoc_overlay::ActionQueue queue;
    };

    // What one hotkey press produced — the close-out test asserts against
    // these fields instead of reaching into the kernels directly.
    struct HotkeyOutcome
    {
        bool        intercepted = false;  // the matrix swallowed the key
        bool        handled     = false;  // a kernel acted on it
        bool        enqueued    = false;  // an ActionRequest reached the queue
        const char* kernel      = "(passthrough)";
        std::string label;                // the built request's label
        std::string lua;                  // the built request's Lua line
    };

    // Drive one F-key press through the Phase 6 hotkey pipeline. `vk` is the
    // Win32 virtual-key code (VK_F1..VK_F12). `fromSlot` / `affectedCount` are
    // consumed only by the F3 branch — the local player's current faction and
    // the count of units a faction switch would affect.
    //
    // STEP 1 the matrix decides intercept-or-passthrough; a passthrough key
    // returns immediately with handled == false (the game gets the key).
    // STEP 2 an intercepted key is routed to its Phase 6 kernel.
    HotkeyOutcome PressHotkey(OverlayHotkeyModel& m, int vk,
                              int fromSlot, std::size_t affectedCount)
    {
        using namespace swfoc_overlay;
        HotkeyOutcome out;

        // STEP 1 — the matrix is the SINGLE gate. A key it does not intercept
        // never reaches a kernel; the game keeps full use of it.
        out.intercepted = OverlayInterceptsKey(vk, m.overlayVisible);
        if (!out.intercepted) return out;

        // STEP 2 — intercepted: route the F-key to its Phase 6 kernel.
        const int fkey = FKeyForVk(vk);
        switch (fkey)
        {
            case 1:  // F1 master visibility toggle (matrix row 0).
                m.overlayVisible = !m.overlayVisible;
                out.handled = true;
                out.kernel  = "visibility";
                break;

            case 3:  // F3 faction switch — ARMS the confirm prompt (iter-305).
                out.handled = m.faction.Arm(fromSlot, affectedCount);
                out.kernel  = "faction-switch";
                break;

            case 4:  // F4 pause/resume toggle (iter-306).
            {
                const ActionRequest req = m.pause.Toggle();
                m.queue.Enqueue(req);
                out.handled  = true;
                out.enqueued = true;
                out.kernel   = "pause-toggle";
                out.label    = req.label;
                out.lua      = req.lua;
                break;
            }

            case 6:  // F6 / F7 / F8 camera-bookmark RECALL (iter-304).
            case 7:
            case 8:
            {
                const std::size_t slot =
                    static_cast<std::size_t>(fkey - 6);
                const ActionRequest req = m.bookmarks.BuildRecall(slot);
                out.handled = true;
                out.kernel  = "camera-bookmark";
                out.label   = req.label;
                out.lua     = req.lua;
                // An UNSET slot yields an empty-Lua request the router must
                // NOT enqueue (overlay_camera_bookmarks.h's EMPTY SLOT pin) —
                // an F-key on an unset slot toasts "empty", never an
                // accidental camera jump to the origin.
                if (!req.lua.empty())
                {
                    m.queue.Enqueue(req);
                    out.enqueued = true;
                }
                break;
            }

            default:
                // The matrix only ever intercepts F1/F3/F4/F6/F7/F8 (the
                // INTERCEPT IMPLIES A BOUND STATUS pin). An intercepted key
                // with no kernel branch is matrix/kernel drift — leave
                // handled == false so section [1] catches it.
                out.kernel = "(intercepted, unrouted)";
                break;
        }
        return out;
    }
}

int main()
{
    using namespace swfoc_overlay;
    std::printf("=== Phase 6 close-out integration test (spec iter-307) ===\n\n");

    // =========================================================================
    // [1] SPEC PIN: every overlay hotkey routes to its kernel.
    //
    // The matrix's F3 / F4 / F6 / F7 / F8 rows must agree, key-for-key, with
    // each Phase 6 kernel's own hotkey constant. The matrix is the router; a
    // router pointing at the wrong door is worse than no router.
    // =========================================================================
    std::printf("[1] Pin: every overlay hotkey routes to its kernel\n");
    {
        // The matrix catalogues exactly F1..F12 — twelve contiguous rows.
        ExpectIntEq("matrix catalogues all twelve function keys",
                    static_cast<long long>(kHotkeyMatrixCount), 12);

        // F4 — the pause kernel. overlay_pause_hotkey.h::kPauseHotkeyFKey is
        // the kernel's own statement of which key it owns; the matrix row must
        // match it both by F-key number and by VK code.
        const HotkeyBinding* f4 = FindHotkeyBindingByFKey(kPauseHotkeyFKey);
        ExpectTrue("F4: pause kernel's hotkey has a matrix row", f4 != nullptr);
        if (f4 != nullptr)
        {
            ExpectIntEq("F4: matrix row fkey == kPauseHotkeyFKey",
                        f4->fkey, kPauseHotkeyFKey);
            ExpectIntEq("F4: matrix row vk == VkForFKey(kPauseHotkeyFKey)",
                        f4->vk, VkForFKey(kPauseHotkeyFKey));
            ExpectContains("F4: matrix row names the pause action",
                           std::string(f4->overlay_action), "Pause");
        }

        // F6 / F7 / F8 — the camera-bookmark kernel. CameraBookmarkHotkey(slot)
        // is the kernel's own slot->F-key map; every slot must land on a
        // matrix row whose fkey matches.
        for (std::size_t slot = 0; slot < kCameraBookmarkSlots; ++slot)
        {
            const int hk = CameraBookmarkHotkey(slot);
            const HotkeyBinding* row = FindHotkeyBindingByFKey(hk);
            char nm[160];
            std::snprintf(nm, sizeof(nm),
                          "camera slot %zu (F%d): kernel hotkey has a matrix "
                          "row", slot, hk);
            ExpectTrue(nm, row != nullptr);
            if (row != nullptr)
            {
                std::snprintf(nm, sizeof(nm),
                              "camera slot %zu: matrix row fkey == "
                              "CameraBookmarkHotkey(slot)", slot);
                ExpectIntEq(nm, row->fkey, hk);
                std::snprintf(nm, sizeof(nm),
                              "camera slot %zu: matrix row names the camera "
                              "action", slot);
                ExpectContains(nm, std::string(row->overlay_action), "camera");
            }
        }

        // F3 — the faction-switch kernel. The faction kernel has no numeric
        // hotkey constant of its own (spec line 27 assigns F3 directly); the
        // matrix row IS the binding record, and it must name the action.
        const HotkeyBinding* f3 = FindHotkeyBindingByFKey(3);
        ExpectTrue("F3: faction-switch has a matrix row", f3 != nullptr);
        if (f3 != nullptr)
        {
            ExpectContains("F3: matrix row names the faction-switch action",
                           std::string(f3->overlay_action), "Faction switch");
            ExpectTrue("F3: matrix row disposition is Intercept",
                       f3->disposition == HotkeyDisposition::Intercept);
        }

        // F1 — the master toggle. The one row intercepted unconditionally.
        const HotkeyBinding* f1 = FindHotkeyBindingByFKey(1);
        ExpectTrue("F1: master-toggle has a matrix row", f1 != nullptr);
        if (f1 != nullptr)
        {
            ExpectTrue("F1: matrix row is intercept_when_hidden",
                       f1->intercept_when_hidden);
        }

        // PressHotkey routes each intercepted F-key to the named kernel — the
        // run-time confirmation that the static rows above wire to real code.
        OverlayHotkeyModel m;
        ExpectStrEq("route: F4 reaches the pause-toggle kernel",
                    PressHotkey(m, VkForFKey(4), 0, 0).kernel, "pause-toggle");
        ExpectStrEq("route: F6 reaches the camera-bookmark kernel",
                    PressHotkey(m, VkForFKey(6), 0, 0).kernel,
                    "camera-bookmark");
        ExpectStrEq("route: F3 reaches the faction-switch kernel",
                    PressHotkey(m, VkForFKey(3), 0, 3).kernel,
                    "faction-switch");
        ExpectStrEq("route: F1 reaches the visibility kernel",
                    PressHotkey(m, VkForFKey(1), 0, 0).kernel, "visibility");
    }

    // =========================================================================
    // [2] SPEC PIN: the matrix gates whether a hotkey fires.
    //
    // The conflict is "worked around" by the visibility gate: an overlay
    // hotkey is intercepted ONLY while the overlay is on screen. Hide the
    // overlay (F1) and every other overlay hotkey reverts to passthrough — the
    // game keeps its full keyboard. F1 alone stays intercepted.
    // =========================================================================
    std::printf("\n[2] Pin: the matrix gates whether a hotkey fires\n");
    {
        // Visible: every overlay-bound F-key is intercepted.
        const int boundKeys[] = { 1, 3, 4, 6, 7, 8 };
        for (const int fk : boundKeys)
        {
            char nm[128];
            std::snprintf(nm, sizeof(nm),
                          "visible: F%d is intercepted", fk);
            ExpectTrue(nm, OverlayInterceptsKey(VkForFKey(fk), true));
        }

        // Visible: every UNBOUND F-key passes through even on screen.
        const int unboundKeys[] = { 2, 5, 9, 10, 11, 12 };
        for (const int fk : unboundKeys)
        {
            char nm[128];
            std::snprintf(nm, sizeof(nm),
                          "visible: F%d (unbound) passes through", fk);
            ExpectFalse(nm, OverlayInterceptsKey(VkForFKey(fk), true));
        }

        // Hidden: every overlay hotkey EXCEPT F1 reverts to passthrough.
        ExpectTrue("hidden: F1 is still intercepted (master toggle)",
                   OverlayInterceptsKey(VkForFKey(1), false));
        const int gatedKeys[] = { 3, 4, 6, 7, 8 };
        for (const int fk : gatedKeys)
        {
            char nm[128];
            std::snprintf(nm, sizeof(nm),
                          "hidden: F%d yields the keyboard back to the game",
                          fk);
            ExpectFalse(nm, OverlayInterceptsKey(VkForFKey(fk), false));
        }

        // The gate's effect end-to-end: F4 on a visible overlay toggles pause
        // and enqueues; F4 on a hidden overlay does NEITHER.
        {
            OverlayHotkeyModel vis;            // visible by default
            const HotkeyOutcome o = PressHotkey(vis, VkForFKey(4), 0, 0);
            ExpectTrue("visible: F4 is intercepted", o.intercepted);
            ExpectTrue("visible: F4 toggles pause", vis.pause.IsPaused());
            ExpectIntEq("visible: F4 enqueues one action",
                        static_cast<long long>(vis.queue.PendingCount()), 1);
        }
        {
            OverlayHotkeyModel hidden;
            hidden.overlayVisible = false;
            const HotkeyOutcome o = PressHotkey(hidden, VkForFKey(4), 0, 0);
            ExpectFalse("hidden: F4 is NOT intercepted", o.intercepted);
            ExpectFalse("hidden: F4 does not reach the pause kernel",
                        o.handled);
            ExpectFalse("hidden: F4 leaves the game unpaused",
                        hidden.pause.IsPaused());
            ExpectIntEq("hidden: F4 enqueues nothing",
                        static_cast<long long>(hidden.queue.PendingCount()),
                        0);
        }

        // Hidden: pressing EVERY gated overlay hotkey enqueues nothing — the
        // game keeps the full keyboard off-screen.
        {
            OverlayHotkeyModel hidden;
            hidden.overlayVisible = false;
            hidden.bookmarks.Save(0, 1.0f, 2.0f, 3.0f);  // a saved bookmark
            for (const int fk : gatedKeys)
            {
                PressHotkey(hidden, VkForFKey(fk), 0, 3);
            }
            ExpectIntEq("hidden: F3/F4/F6/F7/F8 together enqueue nothing",
                        static_cast<long long>(hidden.queue.PendingCount()),
                        0);
            ExpectFalse("hidden: the faction prompt never armed",
                        hidden.faction.IsArmed());
        }
    }

    // =========================================================================
    // [3] SPEC PIN: each kernel's hotkey action is dispatch-ready.
    //
    // A routed F-key must build a real `return SWFOC_*` bridge line — the same
    // dispatch-ready shape the iter-513 ActionQueue drains.
    // =========================================================================
    std::printf("\n[3] Pin: each kernel's hotkey action is dispatch-ready\n");
    {
        // F4 — the pause toggle. First press freezes (SetGameSpeed 0).
        OverlayHotkeyModel m;
        const HotkeyOutcome pause = PressHotkey(m, VkForFKey(4), 0, 0);
        ExpectTrue("F4: the pause action was enqueued", pause.enqueued);
        ExpectStartsWith("F4: builds a SWFOC_SetGameSpeed call",
                         pause.lua, "return SWFOC_SetGameSpeed(");
        ExpectContains("F4: the freeze command sets speed 0",
                       pause.lua, "(0)");
        ExpectContains("F4: the label names the bound key",
                       pause.label, "F4");
    }
    {
        // F6 — camera recall on a SAVED slot builds a SWFOC_SetCameraPos call.
        OverlayHotkeyModel m;
        m.bookmarks.Save(0, 120.0f, 240.0f, 90.0f);
        const HotkeyOutcome recall = PressHotkey(m, VkForFKey(6), 0, 0);
        ExpectTrue("F6: a saved bookmark enqueues the recall", recall.enqueued);
        ExpectStartsWith("F6: builds a SWFOC_SetCameraPos call",
                         recall.lua, "return SWFOC_SetCameraPos(");
        ExpectContains("F6: the recall carries the stored X", recall.lua,
                       "120");
        ExpectContains("F6: the recall carries the stored Y", recall.lua,
                       "240");
        ExpectContains("F6: the label names the bound key", recall.label,
                       "F6");
    }
    {
        // F6 on an UNSET slot — the empty-Lua contract crosses the seam: the
        // kernel hands back an empty request and the router must NOT enqueue
        // it (no accidental camera jump to the origin).
        OverlayHotkeyModel m;
        const HotkeyOutcome empty = PressHotkey(m, VkForFKey(6), 0, 0);
        ExpectTrue("F6 unset: the key was still handled", empty.handled);
        ExpectFalse("F6 unset: an empty bookmark enqueues NOTHING",
                    empty.enqueued);
        ExpectTrue("F6 unset: the request Lua is empty", empty.lua.empty());
        ExpectContains("F6 unset: the label reads 'empty'", empty.label,
                       "empty");
    }
    {
        // F3 — arm the prompt, confirm it, build the batch. Each request is a
        // real SWFOC_ChangeUnitOwner call.
        OverlayHotkeyModel m;
        std::vector<UnitInfo> army;
        army.push_back(MakeUnit(0, "Rebel_Trooper_Squad"));   // Rebel
        army.push_back(MakeUnit(0, "T2B_Tank"));              // Rebel
        army.push_back(MakeUnit(1, "AT_AT"));                 // Empire — left
        const std::size_t affected = CountUnitsOwnedBy(army, 0);
        const HotkeyOutcome f3 = PressHotkey(m, VkForFKey(3), 0, affected);
        ExpectTrue("F3: the faction prompt armed", f3.handled);
        ExpectTrue("F3: the kernel is in the Armed state", m.faction.IsArmed());
        ExpectTrue("F3: Confirm() commits the armed switch",
                   m.faction.Confirm());

        const std::vector<ActionRequest> batch = BuildFactionSwitchBatch(
            army, m.faction.fromSlot(), m.faction.toSlot());
        ExpectIntEq("F3: the batch re-owns both Rebel units only",
                    static_cast<long long>(batch.size()), 2);
        ExpectIntEq("F3: the batch size equals the prompt's count",
                    static_cast<long long>(batch.size()),
                    static_cast<long long>(affected));
        if (!batch.empty())
        {
            ExpectStartsWith("F3: each request is a SWFOC_ChangeUnitOwner call",
                             batch[0].lua, "return SWFOC_ChangeUnitOwner(");
        }
    }

    // =========================================================================
    // [4] SPEC PIN: operator-trust status is consistent across the seam.
    //
    // The matrix's status column is operator-facing (guardrail 1007). It must
    // never claim a key is LIVE when the kernel behind it is PHASE 2 PENDING.
    // =========================================================================
    std::printf("\n[4] Pin: operator-trust status consistent across the seam\n");
    {
        // F4: SWFOC_SetGameSpeed is PHASE 2 PENDING. The matrix's F4 row must
        // mirror overlay_pause_hotkey.h::kGameSpeedWireStatus exactly — flip
        // one without the other and this pin fires.
        const HotkeyBinding* f4 = FindHotkeyBindingByFKey(4);
        ExpectTrue("F4: the matrix has a row to status-check", f4 != nullptr);
        if (f4 != nullptr)
        {
            const bool kernelPending =
                (kGameSpeedWireStatus == GameSpeedWireStatus::Phase2Pending);
            const bool matrixPending =
                (f4->status == HotkeyStatus::Phase2Pending);
            ExpectTrue("F4: matrix status mirrors the pause kernel's wire "
                       "status", kernelPending == matrixPending);
            ExpectTrue("F4: today both agree the wire is PHASE 2 PENDING",
                       kernelPending && matrixPending);
            ExpectStrEq("F4: the matrix badge reads PHASE 2 PENDING",
                        HotkeyStatusBadge(f4->status), "[PHASE 2 PENDING]");
        }

        // F3 / F6 / F7 / F8: their wires (ChangeUnitOwner iter-108,
        // SetCameraPos iter-237) are LIVE — the matrix rows must say so.
        const int liveKeys[] = { 3, 6, 7, 8 };
        for (const int fk : liveKeys)
        {
            const HotkeyBinding* row = FindHotkeyBindingByFKey(fk);
            char nm[128];
            std::snprintf(nm, sizeof(nm),
                          "F%d: matrix row status is LIVE", fk);
            ExpectTrue(nm, row != nullptr &&
                               row->status == HotkeyStatus::Live);
            std::snprintf(nm, sizeof(nm),
                          "F%d: the LIVE badge reads [LIVE]", fk);
            if (row != nullptr)
            {
                ExpectStrEq(nm, HotkeyStatusBadge(row->status), "[LIVE]");
            }
        }

        // INTERCEPT IMPLIES A BOUND STATUS: walk all twelve rows — a row the
        // matrix intercepts must carry a Live or Phase2Pending status (a
        // Deferred / Unbound key is never intercepted).
        bool allInterceptsBound = true;
        for (std::size_t i = 0; i < kHotkeyMatrixCount; ++i)
        {
            const HotkeyBinding& row = kHotkeyMatrix[i];
            if (row.disposition == HotkeyDisposition::Intercept)
            {
                if (row.status != HotkeyStatus::Live &&
                    row.status != HotkeyStatus::Phase2Pending)
                {
                    allInterceptsBound = false;
                }
            }
        }
        ExpectTrue("every intercepted row carries a bound Live/Phase2 status",
                   allInterceptsBound);
    }

    // =========================================================================
    // [5] End-to-end operator session — the four pins chained as one timeline:
    // attach, save a camera viewpoint, pause the battle, defect an army,
    // recall the camera, then hide the overlay and confirm the keyboard frees.
    // =========================================================================
    std::printf("\n[5] End-to-end operator session\n");
    {
        OverlayHotkeyModel m;  // overlay attached and visible

        // A fake bridge send for the ActionQueue drains — echoes the Lua back.
        const BridgeSendFn fakeSend =
            [](const std::string& lua, std::string& response) -> bool
            {
                response = "ok:" + lua;
                return true;
            };

        // The local player's army: three Rebel units + two Empire units.
        std::vector<UnitInfo> army;
        army.push_back(MakeUnit(0, "Rebel_Trooper_Squad"));
        army.push_back(MakeUnit(0, "Rebel_Trooper_Squad"));
        army.push_back(MakeUnit(0, "T2B_Tank"));
        army.push_back(MakeUnit(1, "AT_AT"));
        army.push_back(MakeUnit(1, "TIE_Fighter_Squadron"));

        // --- 1. Save the current camera viewpoint to bookmark slot 0. -------
        // The deferred glue checks Shift+F6: the matrix gates the F6 vk, the
        // Shift modifier is the glue's save-vs-recall discriminator. The save
        // sends SWFOC_GetCameraPos and stores the wire result.
        ExpectTrue("session: Shift+F6 vk is gated by the matrix (visible)",
                   OverlayInterceptsKey(VkForFKey(6), true));
        ExpectTrue("session: a GetCameraPos wire result saves into slot 0",
                   m.bookmarks.SaveFromWire(0, "300.000,150.000,75.000"));
        ExpectTrue("session: camera bookmark slot 0 is now set",
                   m.bookmarks.IsSet(0));

        // --- 2. Pause the battle with F4. ----------------------------------
        const HotkeyOutcome pause = PressHotkey(m, VkForFKey(4), 0, 0);
        ExpectTrue("session: F4 paused the battle", m.pause.IsPaused());
        ExpectTrue("session: the pause action enqueued", pause.enqueued);
        ExpectIntEq("session: the pause drains as one Live action",
                    m.queue.Drain(fakeSend), 1);
        ExpectTrue("session: the drained pause reports Live",
                   m.queue.LatestResult().status == ActionStatus::Live);

        // --- 3. Defect the army with F3, then confirm. ---------------------
        const std::size_t affected = CountUnitsOwnedBy(army, 0);
        ExpectIntEq("session: three Rebel units are eligible to defect",
                    static_cast<long long>(affected), 3);
        const HotkeyOutcome arm = PressHotkey(m, VkForFKey(3), 0, affected);
        ExpectTrue("session: F3 armed the faction-switch prompt", arm.handled);
        ExpectContains("session: the confirm prompt names the unit count",
                       m.faction.PromptText(), "3 unit(s)");

        // A second F3 before confirming must not re-arm (DOUBLE-TAP pin).
        const HotkeyOutcome doubleTap =
            PressHotkey(m, VkForFKey(3), 0, affected);
        ExpectFalse("session: a double-tap of F3 does not re-arm",
                    doubleTap.handled);

        // Operator confirms — build the batch and enqueue every request.
        ExpectTrue("session: Confirm() commits the armed switch",
                   m.faction.Confirm());
        const std::vector<ActionRequest> batch = BuildFactionSwitchBatch(
            army, m.faction.fromSlot(), m.faction.toSlot());
        ExpectIntEq("session: the batch holds one request per Rebel unit",
                    static_cast<long long>(batch.size()), 3);
        for (const ActionRequest& req : batch)
        {
            m.queue.Enqueue(req);
        }
        ExpectIntEq("session: the faction batch drains as three Live actions",
                    m.queue.Drain(fakeSend), 3);

        // --- 4. Recall the camera viewpoint with F6. -----------------------
        const HotkeyOutcome recall = PressHotkey(m, VkForFKey(6), 0, 0);
        ExpectTrue("session: F6 enqueued the camera recall", recall.enqueued);
        ExpectContains("session: the recall jumps to the saved X",
                       recall.lua, "300");
        ExpectIntEq("session: the camera recall drains as one Live action",
                    m.queue.Drain(fakeSend), 1);

        // --- 5. Hide the overlay with F1 — the keyboard frees. -------------
        const HotkeyOutcome hide = PressHotkey(m, VkForFKey(1), 0, 0);
        ExpectTrue("session: F1 was intercepted", hide.intercepted);
        ExpectFalse("session: the overlay is now hidden", m.overlayVisible);

        // With the overlay hidden, F4 no longer reaches the pause kernel: the
        // game stays paused (F4 hidden cannot un-pause it) and nothing queues.
        const HotkeyOutcome hiddenF4 = PressHotkey(m, VkForFKey(4), 0, 0);
        ExpectFalse("session: a hidden F4 is not intercepted",
                    hiddenF4.intercepted);
        ExpectTrue("session: a hidden F4 cannot un-pause the game",
                   m.pause.IsPaused());
        ExpectIntEq("session: a hidden F4 enqueues nothing",
                    static_cast<long long>(m.queue.PendingCount()), 0);

        // F1 still works while hidden — it brings the overlay back.
        const HotkeyOutcome show = PressHotkey(m, VkForFKey(1), 0, 0);
        ExpectTrue("session: F1 still fires while hidden", show.intercepted);
        ExpectTrue("session: F1 brought the overlay back", m.overlayVisible);
    }

    // =========================================================================
    // [6] Cross-kernel invariants — the seams that keep the matrix-as-router
    // consistent with the three kernels it routes to.
    // =========================================================================
    std::printf("\n[6] Cross-kernel invariants\n");
    {
        // Every F-key a Phase 6 kernel binds has a matrix row, and the row's
        // disposition is Intercept (a kernel-bound key the matrix passed
        // through could never fire).
        ExpectTrue("invariant: the pause kernel's F-key has an Intercept row",
                   FindHotkeyBindingByFKey(kPauseHotkeyFKey) != nullptr &&
                   FindHotkeyBindingByFKey(kPauseHotkeyFKey)->disposition ==
                       HotkeyDisposition::Intercept);
        for (std::size_t slot = 0; slot < kCameraBookmarkSlots; ++slot)
        {
            const HotkeyBinding* row =
                FindHotkeyBindingByFKey(CameraBookmarkHotkey(slot));
            char nm[160];
            std::snprintf(nm, sizeof(nm),
                          "invariant: camera slot %zu's F-key has an "
                          "Intercept row", slot);
            ExpectTrue(nm, row != nullptr &&
                               row->disposition ==
                                   HotkeyDisposition::Intercept);
        }

        // OverlayInterceptsKey (the router's gate) agrees with each row's
        // disposition: for a VISIBLE overlay, a key is intercepted IFF its
        // matrix row's disposition is Intercept. The gate never contradicts
        // the catalogue it reads from.
        bool gateMatchesMatrix = true;
        for (std::size_t i = 0; i < kHotkeyMatrixCount; ++i)
        {
            const HotkeyBinding& row = kHotkeyMatrix[i];
            const bool gated = OverlayInterceptsKey(row.vk, true);
            const bool isIntercept =
                (row.disposition == HotkeyDisposition::Intercept);
            if (gated != isIntercept) gateMatchesMatrix = false;
        }
        ExpectTrue("invariant: the visible-overlay gate matches every row's "
                   "disposition", gateMatchesMatrix);

        // The single-gate invariant: PressHotkey never lets a kernel act on a
        // key the matrix did not intercept. Press every UNBOUND F-key and
        // confirm not one of them was handled or enqueued.
        {
            OverlayHotkeyModel m;
            bool anyHandled = false;
            const int unboundKeys[] = { 2, 5, 9, 10, 11, 12 };
            for (const int fk : unboundKeys)
            {
                const HotkeyOutcome o = PressHotkey(m, VkForFKey(fk), 0, 3);
                if (o.handled || o.enqueued) anyHandled = true;
            }
            ExpectFalse("invariant: no unbound F-key ever reaches a kernel",
                        anyHandled);
            ExpectIntEq("invariant: pressing every unbound key queues nothing",
                        static_cast<long long>(m.queue.PendingCount()), 0);
        }

        // A non-function key (a letter) is not an F-key at all: FKeyForVk
        // returns 0 and the matrix never intercepts it.
        ExpectIntEq("invariant: a non-F-key VK has FKeyForVk == 0",
                    FKeyForVk(0x41 /* 'A' */), 0);
        ExpectFalse("invariant: the matrix never intercepts a non-F-key",
                    OverlayInterceptsKey(0x41 /* 'A' */, true));

        // The three Phase 6 kernels are independent: F4 (pause) leaves the
        // camera-bookmark and faction-switch kernels untouched.
        {
            OverlayHotkeyModel m;
            m.bookmarks.Save(1, 5.0f, 6.0f, 7.0f);
            PressHotkey(m, VkForFKey(4), 0, 0);  // pause
            ExpectTrue("invariant: F4 leaves a camera bookmark intact",
                       m.bookmarks.IsSet(1));
            ExpectFalse("invariant: F4 does not arm the faction prompt",
                        m.faction.IsArmed());
        }
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
