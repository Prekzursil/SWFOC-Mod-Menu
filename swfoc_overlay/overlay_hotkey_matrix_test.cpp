// =============================================================================
// swfoc_overlay/overlay_hotkey_matrix_test.cpp — unit test for
// overlay_hotkey_matrix.h (Phase 6 close-out part 1/2, iter 547 / spec iter-307).
//
// iter-307 is the Phase 6 close-out; its headline deliverable is the HOTKEY
// CONFLICT MATRIX — a per-binding intercept-or-passthrough decision for every
// F-key the overlay touches (spec acceptance line 39). overlay_hotkey_matrix.h
// holds the pure kernel: the 12-row matrix, the VK<->F-key helpers, and
// OverlayInterceptsKey() — the WndProc-detour decision the deferred overlay.cpp
// keyboard handler will call on every F-key WM_KEYDOWN.
//
// This test pins the matrix AND cross-checks the F4 / F6 / F7 / F8 rows against
// the Phase 6 kernels they describe (overlay_pause_hotkey.h,
// overlay_camera_bookmarks.h) so the matrix can never drift from them. The
// integration section drives OverlayInterceptsKey through a full operator
// session — overlay hidden -> F1 un-hides -> overlay hotkeys go live -> F1
// re-hides -> the keyboard reverts to the game.
//
// overlay_hotkey_matrix.h is header-only and std-only (<cstddef> only). The
// two Phase 6 kernels this test cross-checks pull in <cstdio> / <string> /
// <deque> / <functional> / <mutex> through their include chains — so -pthread
// is load-bearing for the threading runtime. No game, no pipe, no ImGui, no
// Windows. Build + run via build_hotkey_matrix_test.bat.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - MATRIX COVERS F1..F12          : exactly 12 rows, VK codes contiguous
//                                      0x70..0x7B, F-keys 1..12 in order.
//   - F1 INTERCEPTS EVEN WHEN HIDDEN : OverlayInterceptsKey(VK_F1, false) is
//                                      true — a hidden overlay still claims F1.
//   - HOTKEYS YIELD THE KEYBOARD     : F3/F4/F6/F7/F8 are NOT intercepted with
//     WHEN HIDDEN                      overlayVisible=false.
//   - UNBOUND KEYS ALWAYS PASS       : F5/F9/F10/F11/F12 are never intercepted.
//     THROUGH
//   - F10 IS NEVER INTERCEPTED       : the Win32 system-menu key always passes.
//   - INTERCEPT IMPLIES A BOUND      : disposition Intercept only ever pairs
//     LIVE/PHASE2 STATUS               with status Live / Phase2Pending.
//   - F4 STATUS TRACKS THE WIRE      : the F4 row mirrors
//                                      overlay_pause_hotkey.h::kGameSpeed-
//                                      WireStatus and flips LIVE in lockstep.
//   - CAMERA KEYS ARE F6/F7/F8       : the camera rows match
//                                      overlay_camera_bookmarks.h::CameraBook-
//                                      markHotkey(0/1/2).
// =============================================================================

#include "overlay_hotkey_matrix.h"

#include "overlay_camera_bookmarks.h"  // CameraBookmarkHotkey, kCameraBookmarkSlots
#include "overlay_pause_hotkey.h"      // kPauseHotkeyFKey, kGameSpeedWireStatus

#include <cstddef>
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

    void ExpectFalse(const char* name, bool cond)
    {
        ExpectTrue(name, !cond);
    }

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
            std::printf("  FAIL %s\n    got %lld  want %lld\n", name, got, want);
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

    void ExpectContains(const char* name, const char* hay, const char* needle)
    {
        ++g_checks;
        if (hay != nullptr && needle != nullptr &&
            std::strstr(hay, needle) != nullptr)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\" does not contain \"%s\"\n", name,
                        hay != nullptr ? hay : "(null)",
                        needle != nullptr ? needle : "(null)");
        }
    }
}

int main()
{
    using namespace swfoc_overlay;
    std::printf("=== Phase 6 hotkey conflict matrix test (spec iter-307) ===\n\n");

    // =========================================================================
    // [1] PIN: matrix covers F1..F12.
    //
    // Exactly 12 rows, VK codes contiguous 0x70..0x7B, F-key numbers 1..12 in
    // order. An old form that drops a key — or duplicates one — fails here.
    // =========================================================================
    std::printf("[1] Pin: matrix covers F1..F12\n");
    {
        ExpectIntEq("matrix has exactly 12 rows",
                    static_cast<long long>(kHotkeyMatrixCount), 12);
        ExpectIntEq("VK_F1 constant is 0x70", kVkF1, 0x70);
        ExpectIntEq("VK_F12 constant is 0x7B", kVkF12, 0x7B);

        bool vk_contiguous = true;
        bool fkey_in_order = true;
        char nm[96];
        for (std::size_t i = 0; i < kHotkeyMatrixCount; ++i)
        {
            const HotkeyBinding& row = kHotkeyMatrix[i];
            const int want_vk   = kVkF1 + static_cast<int>(i);
            const int want_fkey = static_cast<int>(i) + 1;
            std::snprintf(nm, sizeof(nm),
                          "row %zu: vk == 0x%02X (F%d)", i, want_vk, want_fkey);
            ExpectIntEq(nm, row.vk, want_vk);
            std::snprintf(nm, sizeof(nm), "row %zu: fkey == %d", i, want_fkey);
            ExpectIntEq(nm, row.fkey, want_fkey);
            if (row.vk != want_vk)     vk_contiguous = false;
            if (row.fkey != want_fkey) fkey_in_order = false;
        }
        ExpectTrue("VK codes are contiguous 0x70..0x7B", vk_contiguous);
        ExpectTrue("F-key numbers run 1..12 in row order", fkey_in_order);
    }

    // =========================================================================
    // [2] PIN: VK <-> F-key round-trip.
    // =========================================================================
    std::printf("\n[2] Pin: VK <-> F-key round-trip\n");
    {
        bool round_trips = true;
        char nm[96];
        for (int fkey = 1; fkey <= 12; ++fkey)
        {
            const int vk = VkForFKey(fkey);
            std::snprintf(nm, sizeof(nm), "VkForFKey(%d) == 0x%02X",
                          fkey, kVkF1 + (fkey - 1));
            ExpectIntEq(nm, vk, kVkF1 + (fkey - 1));
            std::snprintf(nm, sizeof(nm), "FKeyForVk(0x%02X) == %d", vk, fkey);
            ExpectIntEq(nm, FKeyForVk(vk), fkey);
            if (FKeyForVk(VkForFKey(fkey)) != fkey) round_trips = false;
        }
        ExpectTrue("VkForFKey/FKeyForVk round-trip for every F1..F12",
                   round_trips);

        // Out-of-range inputs yield 0, never a garbage key code.
        ExpectIntEq("VkForFKey(0) is 0 (out of range)", VkForFKey(0), 0);
        ExpectIntEq("VkForFKey(13) is 0 (out of range)", VkForFKey(13), 0);
        ExpectIntEq("FKeyForVk(0x6F) is 0 (below F1)", FKeyForVk(0x6F), 0);
        ExpectIntEq("FKeyForVk(0x7C) is 0 (above F12)", FKeyForVk(0x7C), 0);
        ExpectIntEq("FKeyForVk(0x41 'A') is 0 (not an F-key)",
                    FKeyForVk(0x41), 0);
    }

    // =========================================================================
    // [3] PIN: the intercept-or-passthrough decision, per key, visible.
    //
    // With the overlay on screen, every overlay-bound F-key is intercepted and
    // every key the overlay does not bind passes through.
    // =========================================================================
    std::printf("\n[3] Pin: intercept-or-passthrough decision (overlay visible)\n");
    {
        // F1/F3/F4/F6/F7/F8 bound -> intercepted. F2/F5/F9/F10/F11/F12 not.
        const bool want_visible[12] = {
            true,   // F1  visibility toggle
            false,  // F2  spawn-mode toggle (deferred)
            true,   // F3  faction switch
            true,   // F4  pause/resume
            false,  // F5  quick-save (deferred)
            true,   // F6  camera bookmark 0
            true,   // F7  camera bookmark 1
            true,   // F8  camera bookmark 2
            false,  // F9  quick-load (deferred)
            false,  // F10 unbound (Win32 system key)
            false,  // F11 unbound
            false,  // F12 unbound
        };
        char nm[96];
        for (int fkey = 1; fkey <= 12; ++fkey)
        {
            const int vk = VkForFKey(fkey);
            std::snprintf(nm, sizeof(nm),
                          "F%d visible: %s", fkey,
                          want_visible[fkey - 1] ? "intercepted"
                                                 : "passthrough");
            ExpectIntEq(nm, OverlayInterceptsKey(vk, /*overlayVisible=*/true),
                        want_visible[fkey - 1]);
        }
    }

    // =========================================================================
    // [4] PIN: hotkeys yield the keyboard when the overlay is hidden — and
    //          F1 alone intercepts unconditionally.
    //
    // Toggle the overlay off and every overlay hotkey EXCEPT F1 reverts to
    // passthrough: the operator gets the game's full keyboard back. F1 must
    // still be intercepted so a hidden overlay can be brought back.
    // =========================================================================
    std::printf("\n[4] Pin: hidden overlay yields the keyboard (F1 excepted)\n");
    {
        ExpectTrue("F1 INTERCEPTS EVEN WHEN HIDDEN",
                   OverlayInterceptsKey(kVkF1, /*overlayVisible=*/false));

        char nm[96];
        bool all_yield = true;
        for (int fkey = 2; fkey <= 12; ++fkey)
        {
            const int vk = VkForFKey(fkey);
            const bool intercepted =
                OverlayInterceptsKey(vk, /*overlayVisible=*/false);
            std::snprintf(nm, sizeof(nm),
                          "F%d hidden: passes through to the game", fkey);
            ExpectFalse(nm, intercepted);
            if (intercepted) all_yield = false;
        }
        ExpectTrue("every F2..F12 hotkey yields the keyboard when hidden",
                   all_yield);

        // The bound-when-visible hotkeys specifically flip with visibility.
        const int flipping[5] = { 3, 4, 6, 7, 8 };
        for (int i = 0; i < 5; ++i)
        {
            const int vk = VkForFKey(flipping[i]);
            std::snprintf(nm, sizeof(nm),
                          "F%d intercepted only while visible", flipping[i]);
            ExpectTrue(nm,
                       OverlayInterceptsKey(vk, true) &&
                       !OverlayInterceptsKey(vk, false));
        }
    }

    // =========================================================================
    // [5] PIN: F10 is never intercepted (Win32 system-menu key).
    // =========================================================================
    std::printf("\n[5] Pin: F10 is never intercepted\n");
    {
        const int vkF10 = VkForFKey(10);
        ExpectFalse("F10 not intercepted while visible",
                    OverlayInterceptsKey(vkF10, true));
        ExpectFalse("F10 not intercepted while hidden",
                    OverlayInterceptsKey(vkF10, false));
        const HotkeyBinding* f10 = FindHotkeyBinding(vkF10);
        ExpectTrue("F10 row exists", f10 != nullptr);
        if (f10 != nullptr)
        {
            ExpectTrue("F10 disposition is Passthrough",
                       f10->disposition == HotkeyDisposition::Passthrough);
            ExpectTrue("F10 status is Unbound",
                       f10->status == HotkeyStatus::Unbound);
            ExpectContains("F10 conflict note flags the Win32 system menu",
                           f10->game_conflict, "window menu");
        }
    }

    // =========================================================================
    // [6] PIN: matrix invariants hold for every row.
    //
    //   - disposition Intercept  => status Live or Phase2Pending
    //   - status Deferred/Unbound => disposition Passthrough
    //   - intercept_when_hidden   => disposition Intercept
    //   - exactly one row (F1) is intercept_when_hidden
    // =========================================================================
    std::printf("\n[6] Pin: matrix invariants\n");
    {
        bool intercept_implies_bound   = true;
        bool deferred_implies_pass     = true;
        bool hidden_implies_intercept  = true;
        int  hidden_rows               = 0;
        char nm[112];
        for (std::size_t i = 0; i < kHotkeyMatrixCount; ++i)
        {
            const HotkeyBinding& row = kHotkeyMatrix[i];

            if (row.disposition == HotkeyDisposition::Intercept)
            {
                const bool bound = row.status == HotkeyStatus::Live ||
                                   row.status == HotkeyStatus::Phase2Pending;
                if (!bound) intercept_implies_bound = false;
            }
            if (row.status == HotkeyStatus::Deferred ||
                row.status == HotkeyStatus::Unbound)
            {
                if (row.disposition != HotkeyDisposition::Passthrough)
                {
                    deferred_implies_pass = false;
                }
            }
            if (row.intercept_when_hidden)
            {
                ++hidden_rows;
                if (row.disposition != HotkeyDisposition::Intercept)
                {
                    hidden_implies_intercept = false;
                }
                std::snprintf(nm, sizeof(nm),
                              "intercept_when_hidden row is F%d", row.fkey);
                ExpectIntEq(nm, row.fkey, 1);
            }
        }
        ExpectTrue("INTERCEPT IMPLIES A BOUND (Live/Phase2) STATUS",
                   intercept_implies_bound);
        ExpectTrue("Deferred / Unbound status implies Passthrough",
                   deferred_implies_pass);
        ExpectTrue("intercept_when_hidden implies disposition Intercept",
                   hidden_implies_intercept);
        ExpectIntEq("exactly one row is intercept_when_hidden", hidden_rows, 1);
    }

    // =========================================================================
    // [7] PIN: status badge + disposition text are stable strings.
    // =========================================================================
    std::printf("\n[7] Pin: status badge + disposition text\n");
    {
        ExpectStrEq("Live badge", HotkeyStatusBadge(HotkeyStatus::Live),
                    "[LIVE]");
        ExpectStrEq("Phase2Pending badge",
                    HotkeyStatusBadge(HotkeyStatus::Phase2Pending),
                    "[PHASE 2 PENDING]");
        ExpectStrEq("Deferred badge", HotkeyStatusBadge(HotkeyStatus::Deferred),
                    "[DEFERRED]");
        ExpectStrEq("Unbound badge", HotkeyStatusBadge(HotkeyStatus::Unbound),
                    "[UNBOUND]");
        ExpectStrEq("Intercept text",
                    HotkeyDispositionText(HotkeyDisposition::Intercept),
                    "Intercept");
        ExpectStrEq("Passthrough text",
                    HotkeyDispositionText(HotkeyDisposition::Passthrough),
                    "Passthrough");
    }

    // =========================================================================
    // [8] PIN: cross-kernel consistency — the matrix matches the Phase 6
    //          kernels it describes, so it can never silently drift from them.
    // =========================================================================
    std::printf("\n[8] Pin: cross-kernel consistency\n");
    {
        // --- F4 vs overlay_pause_hotkey.h ------------------------------------
        ExpectIntEq("overlay_pause_hotkey.h binds the pause toggle to F4",
                    kPauseHotkeyFKey, 4);
        const HotkeyBinding* f4 = FindHotkeyBindingByFKey(kPauseHotkeyFKey);
        ExpectTrue("matrix has an F4 row", f4 != nullptr);
        if (f4 != nullptr)
        {
            ExpectContains("F4 row names the pause/resume action",
                           f4->overlay_action, "ause");
            ExpectTrue("F4 disposition is Intercept",
                       f4->disposition == HotkeyDisposition::Intercept);

            // F4 STATUS TRACKS THE WIRE: the F4 row's status is derived from
            // overlay_pause_hotkey.h::kGameSpeedWireStatus — flip that to Live
            // and this pin fires to remind a maintainer to flip the F4 row.
            const HotkeyStatus want_f4 =
                kGameSpeedWireStatus == GameSpeedWireStatus::Live
                    ? HotkeyStatus::Live
                    : HotkeyStatus::Phase2Pending;
            ExpectTrue("F4 row status tracks kGameSpeedWireStatus",
                       f4->status == want_f4);
            ExpectTrue("F4 is PHASE 2 PENDING today (SetGameSpeed not hooked)",
                       f4->status == HotkeyStatus::Phase2Pending);
        }

        // --- F6/F7/F8 vs overlay_camera_bookmarks.h --------------------------
        ExpectIntEq("overlay_camera_bookmarks.h has 3 bookmark slots",
                    static_cast<long long>(kCameraBookmarkSlots), 3);
        bool camera_rows_ok = true;
        char nm[112];
        for (std::size_t slot = 0; slot < kCameraBookmarkSlots; ++slot)
        {
            const int fkey = CameraBookmarkHotkey(slot);
            const HotkeyBinding* row = FindHotkeyBindingByFKey(fkey);
            std::snprintf(nm, sizeof(nm),
                          "camera slot %zu maps to matrix F%d", slot, fkey);
            ExpectTrue(nm, row != nullptr);
            if (row == nullptr ||
                row->disposition != HotkeyDisposition::Intercept ||
                row->status != HotkeyStatus::Live)
            {
                camera_rows_ok = false;
            }
            if (row != nullptr)
            {
                std::snprintf(nm, sizeof(nm),
                              "F%d row names a camera bookmark", fkey);
                ExpectContains(nm, row->overlay_action, "camera bookmark");
            }
        }
        ExpectTrue("CAMERA KEYS ARE F6/F7/F8 — all Intercept + LIVE",
                   camera_rows_ok);
        ExpectIntEq("camera slot 0 -> F6", CameraBookmarkHotkey(0), 6);
        ExpectIntEq("camera slot 1 -> F7", CameraBookmarkHotkey(1), 7);
        ExpectIntEq("camera slot 2 -> F8", CameraBookmarkHotkey(2), 8);

        // --- F3 the faction-switch key ---------------------------------------
        const HotkeyBinding* f3 = FindHotkeyBindingByFKey(3);
        ExpectTrue("matrix has an F3 row", f3 != nullptr);
        if (f3 != nullptr)
        {
            ExpectContains("F3 row names the faction-switch action",
                           f3->overlay_action, "Faction switch");
            ExpectTrue("F3 disposition is Intercept",
                       f3->disposition == HotkeyDisposition::Intercept);
            ExpectTrue("F3 status is Live (iter-305)",
                       f3->status == HotkeyStatus::Live);
        }
    }

    // =========================================================================
    // [9] PIN: lookup miss paths.
    // =========================================================================
    std::printf("\n[9] Pin: lookup miss paths\n");
    {
        ExpectTrue("FindHotkeyBinding(0x41 'A') is nullptr",
                   FindHotkeyBinding(0x41) == nullptr);
        ExpectTrue("FindHotkeyBinding(0x6F) is nullptr (below F1)",
                   FindHotkeyBinding(0x6F) == nullptr);
        ExpectTrue("FindHotkeyBinding(0x7C) is nullptr (above F12)",
                   FindHotkeyBinding(0x7C) == nullptr);
        ExpectTrue("FindHotkeyBindingByFKey(0) is nullptr",
                   FindHotkeyBindingByFKey(0) == nullptr);
        ExpectTrue("FindHotkeyBindingByFKey(13) is nullptr",
                   FindHotkeyBindingByFKey(13) == nullptr);
        ExpectFalse("OverlayInterceptsKey on a non-F-key never intercepts",
                    OverlayInterceptsKey(0x41, true));
        // A hit path resolves cleanly.
        const HotkeyBinding* f1 = FindHotkeyBinding(kVkF1);
        ExpectTrue("FindHotkeyBinding(VK_F1) resolves the F1 row",
                   f1 != nullptr && f1->fkey == 1);
    }

    // =========================================================================
    // [10] End-to-end operator session — the WndProc-detour decision driven
    // through a realistic timeline. `overlayVisible` is the host-side toggle;
    // each "key" line is exactly what overlay.cpp's deferred keyboard handler
    // computes when it sees a WM_KEYDOWN.
    // =========================================================================
    std::printf("\n[10] End-to-end operator session\n");
    {
        bool overlayVisible = false;  // overlay starts hidden (F1 toggled off).

        // While hidden, an F6 press belongs entirely to the game.
        ExpectFalse("session: F6 while hidden passes through to the game",
                    OverlayInterceptsKey(VkForFKey(6), overlayVisible));

        // F1 is intercepted even hidden — and the handler flips visibility.
        const bool f1a = OverlayInterceptsKey(kVkF1, overlayVisible);
        ExpectTrue("session: F1 while hidden is intercepted", f1a);
        if (f1a) overlayVisible = !overlayVisible;
        ExpectTrue("session: F1 un-hid the overlay", overlayVisible);

        // Now visible: F6 recalls a camera bookmark (intercepted, swallowed).
        ExpectTrue("session: F6 while visible is intercepted (camera recall)",
                   OverlayInterceptsKey(VkForFKey(6), overlayVisible));
        // F4 pause toggle — intercepted (PHASE 2 PENDING, but still claimed).
        ExpectTrue("session: F4 while visible is intercepted (pause toggle)",
                   OverlayInterceptsKey(VkForFKey(4), overlayVisible));
        // F5 is unclaimed (quick-save deferred) — the game keeps it.
        ExpectFalse("session: F5 while visible still passes through (deferred)",
                    OverlayInterceptsKey(VkForFKey(5), overlayVisible));
        // F10 is never claimed — the host window menu still works.
        ExpectFalse("session: F10 while visible passes through (system key)",
                    OverlayInterceptsKey(VkForFKey(10), overlayVisible));

        // F1 again — re-hide the overlay.
        const bool f1b = OverlayInterceptsKey(kVkF1, overlayVisible);
        ExpectTrue("session: F1 while visible is intercepted", f1b);
        if (f1b) overlayVisible = !overlayVisible;
        ExpectFalse("session: F1 re-hid the overlay", overlayVisible);

        // Hidden again: F6 reverts to the game — the keyboard is fully its.
        ExpectFalse("session: F6 after re-hide passes through to the game",
                    OverlayInterceptsKey(VkForFKey(6), overlayVisible));
        ExpectFalse("session: F3 after re-hide passes through to the game",
                    OverlayInterceptsKey(VkForFKey(3), overlayVisible));
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
