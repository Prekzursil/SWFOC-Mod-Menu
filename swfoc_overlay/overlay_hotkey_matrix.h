// =============================================================================
// swfoc_overlay/overlay_hotkey_matrix.h — Phase 6 hotkey conflict matrix.
//
// Phase 6 (iter 304-307) expanded the overlay's hotkey surface into power-user
// features. iter-307 (spec line 65) is the Phase 6 CLOSE-OUT, whose headline
// deliverable is the HOTKEY CONFLICT MATRIX: a per-binding "intercept-or-
// passthrough decision" for every F-key the overlay touches, so the overlay's
// claim on the keyboard can never silently collide with the host game's own
// F-key handling (spec acceptance line 39).
//
// THE CONFLICT
// ------------
// The overlay subclasses the host game's WndProc (iter-514) so ImGui sees
// mouse / keyboard messages. overlay_input.h::ShouldSwallowMessage already
// routes the GENERAL case — a click / keystroke over an ImGui widget. The
// F-key HOTKEYS are different: they fire whether or not a widget has focus,
// so when the WndProc detour sees a WM_KEYDOWN for an F-key it needs a
// SEPARATE, explicit decision:
//
//   - INTERCEPT  : the overlay acts on the key AND swallows the message — the
//                  game never sees it. Used for every F-key the overlay binds,
//                  so one press never both toggles an overlay feature AND
//                  triggers whatever the game maps that key to.
//   - PASSTHROUGH: the overlay does not claim the key; the message flows to
//                  the game untouched. Used for every F-key the overlay does
//                  NOT bind, so the game keeps full use of its own keyboard.
//
// THE VISIBILITY GATE — how the conflict is "worked around"
// --------------------------------------------------------
// An overlay-bound F-key is intercepted ONLY while the overlay is on screen.
// Toggle the overlay off (F1) and every other overlay hotkey reverts to
// passthrough — the operator gets the game's full keyboard back the instant
// the tool is hidden. F1 itself is the sole exception: it is intercepted
// UNCONDITIONALLY (the `intercept_when_hidden` flag), because the master
// visibility toggle must stay reachable to bring a hidden overlay back.
//
// This header is the AUTHORITATIVE matrix: one HotkeyBinding row per F1..F12,
// carrying the disposition, the operator-trust capability status (guardrail
// 1007), the game-side conflict note, and the rationale. Two consumers share
// this single source of truth:
//   - the deferred overlay.cpp WndProc detour calls OverlayInterceptsKey() to
//     decide whether to swallow an F-key WM_KEYDOWN;
//   - overlay_hotkey_matrix_test.cpp pins the matrix AND cross-checks the F4 /
//     F6 / F7 / F8 rows against the Phase 6 kernels (overlay_pause_hotkey.h,
//     overlay_camera_bookmarks.h) so the matrix can never drift from them.
//
// RED-GREEN REGRESSION PINS (overlay_hotkey_matrix_test.cpp)
// ---------------------------------------------------------
//   - MATRIX COVERS F1..F12         : exactly 12 rows, VK codes contiguous
//                                     0x70..0x7B — a missing key fails.
//   - F1 INTERCEPTS EVEN WHEN HIDDEN: OverlayInterceptsKey(VK_F1, false) is
//                                     true — an old form gating F1 on
//                                     visibility could never un-hide the
//                                     overlay and fails.
//   - HOTKEYS YIELD THE KEYBOARD    : F3 / F4 / F6 / F7 / F8 are NOT
//     WHEN HIDDEN                     intercepted with overlayVisible=false —
//                                     an old form swallowing them while hidden
//                                     starves the game and fails.
//   - UNBOUND KEYS ALWAYS PASS      : F5 / F9 / F10 / F11 / F12 are never
//     THROUGH                         intercepted, visible or hidden.
//   - F10 IS NEVER INTERCEPTED      : F10 is a Win32 system key — swallowing it
//                                     would break the host window menu.
//   - INTERCEPT IMPLIES A BOUND     : disposition Intercept only ever pairs
//     LIVE/PHASE2 STATUS              with status Live / Phase2Pending;
//                                     Deferred / Unbound always pair with
//                                     Passthrough — a row violating that fails.
//   - F4 STATUS TRACKS THE WIRE     : the F4 row's status mirrors
//                                     overlay_pause_hotkey.h::kGameSpeedWire-
//                                     Status — it flips to LIVE in lockstep.
//   - CAMERA KEYS ARE F6/F7/F8      : the camera-bookmark rows match
//                                     overlay_camera_bookmarks.h::CameraBook-
//                                     markHotkey(0/1/2).
//
// THREADING: the matrix is immutable constexpr data — every consumer only
// READS it. OverlayInterceptsKey() is a pure function of its arguments. No
// state, no mutex; safe to call from the WndProc detour on the host UI thread.
//
// Pure, header-only, std-only (<cstddef> only — NO ImGui, NO Windows, NO
// bridge). The Win32 VK_F* values are mirrored as plain constants exactly as
// overlay_input.h mirrors the WM_* range; they are a frozen Windows ABI.
// Unit-tested with a plain g++ (build_hotkey_matrix_test.bat).
// =============================================================================

#pragma once

#include <cstddef>

namespace swfoc_overlay
{
    // Win32 virtual-key codes for the function keys. VK_F1 == 0x70 and F1..F24
    // are contiguous — a frozen Windows ABI, mirrored here (rather than
    // #include <windows.h>) so the matrix stays dependency-free and unit-
    // testable with a plain g++, exactly like overlay_input.h's WM_* mirror.
    // overlay.cpp passes the real VK_F* code; it equals these by ABI.
    inline constexpr int kVkF1  = 0x70;
    inline constexpr int kVkF12 = 0x7B;

    // The VK code for F-key number `fkey` (1..12). Out of range yields 0 so a
    // caller never builds a garbage key code.
    inline int VkForFKey(int fkey)
    {
        if (fkey < 1 || fkey > 12) return 0;
        return kVkF1 + (fkey - 1);
    }

    // The F-key number (1..12) for VK code `vk`. Returns 0 when `vk` is not an
    // F1..F12 key — the caller's "this is not a function key" signal.
    inline int FKeyForVk(int vk)
    {
        if (vk < kVkF1 || vk > kVkF12) return 0;
        return (vk - kVkF1) + 1;
    }

    // The WndProc-detour disposition for one F-key. The spec's two-value
    // vocabulary (acceptance line 39: "intercept-or-passthrough"); the
    // overlay-hidden case is the orthogonal `intercept_when_hidden` modifier.
    enum class HotkeyDisposition
    {
        Intercept,    // overlay swallows the key (the game never sees it)
        Passthrough,  // overlay never claims the key; the game receives it
    };

    // Operator-trust capability status of an overlay F-key binding (guardrail
    // 1007) — the same honesty contract as overlay_phase3_catalog.h's
    // WidgetStatus, widened with the two states a hotkey row can be in that a
    // shipped widget cannot.
    enum class HotkeyStatus
    {
        Live,           // the overlay action is LIVE today
        Phase2Pending,  // bound + wired, but the bridge wire is PHASE 2 PENDING
        Deferred,       // spec reserves the key; the feature is a later phase
        Unbound,        // the overlay binds nothing to this key at all
    };

    // Short bracketed badge text for a hotkey status — drawn next to the key in
    // the deferred overlay.cpp hotkey-help panel. Stable strings: the test
    // pins each so a UI tweak cannot quietly reword an operator-trust badge.
    inline const char* HotkeyStatusBadge(HotkeyStatus status)
    {
        switch (status)
        {
            case HotkeyStatus::Live:          return "[LIVE]";
            case HotkeyStatus::Phase2Pending: return "[PHASE 2 PENDING]";
            case HotkeyStatus::Deferred:      return "[DEFERRED]";
            case HotkeyStatus::Unbound:       return "[UNBOUND]";
        }
        return "[?]";  // unreachable — every enumerator is handled above.
    }

    // Human-readable disposition text for the hotkey-help panel.
    inline const char* HotkeyDispositionText(HotkeyDisposition disposition)
    {
        switch (disposition)
        {
            case HotkeyDisposition::Intercept:   return "Intercept";
            case HotkeyDisposition::Passthrough: return "Passthrough";
        }
        return "?";  // unreachable — every enumerator is handled above.
    }

    // One row of the Phase 6 hotkey conflict matrix.
    struct HotkeyBinding
    {
        int               vk;             // VK_F1..VK_F12 (0x70..0x7B)
        int               fkey;           // 1..12
        const char*       overlay_action; // what the overlay does, or "(unbound)"
        HotkeyDisposition disposition;    // intercept-or-passthrough when active
        HotkeyStatus      status;         // operator-trust capability
        bool intercept_when_hidden;       // still intercept with overlay hidden
        const char*       game_conflict;  // the game's own use / conflict note
        const char*       rationale;      // why this disposition was chosen
    };

    // The AUTHORITATIVE Phase 6 hotkey conflict matrix — one row per F1..F12,
    // in F-key order. Adding an overlay F-key binding without updating its row
    // here — or changing a disposition — fires overlay_hotkey_matrix_test.cpp.
    inline constexpr HotkeyBinding kHotkeyMatrix[] = {
        { kVkF1 + 0, 1, "Toggle overlay visibility",
          HotkeyDisposition::Intercept, HotkeyStatus::Live, true,
          "F1 opens the in-game help screen on some builds",
          "The overlay master toggle must be reachable whether the overlay is "
          "shown or hidden, so F1 is the one binding intercepted "
          "unconditionally — a hidden overlay still claims F1 so the operator "
          "can always bring it back." },

        { kVkF1 + 1, 2, "Spawn-mode toggle (deferred)",
          HotkeyDisposition::Passthrough, HotkeyStatus::Deferred, false,
          "no overlay claim — the game keeps F2",
          "Spec line 26 assigns F2 to a drag-drop ON/OFF toggle, but Phase 4 "
          "shipped always-available drag-drop with no discrete F2 control. "
          "Until a spawn-mode-toggle kernel lands F2 is unclaimed and passes "
          "through; this row flips to Intercept the moment that kernel ships." },

        { kVkF1 + 2, 3, "Faction switch (arm bulk re-own)",
          HotkeyDisposition::Intercept, HotkeyStatus::Live, false,
          "no default tactical binding observed",
          "F3 arms the iter-305 faction-switch confirm prompt. Intercepted "
          "only while the overlay is visible — a hidden overlay yields F3 back "
          "to the game so the operator's keyboard is unaffected off-screen." },

        { kVkF1 + 3, 4, "Pause / resume game",
          HotkeyDisposition::Intercept, HotkeyStatus::Phase2Pending, false,
          "no default tactical binding observed",
          "F4 drives the iter-306 pause toggle. SWFOC_SetGameSpeed is PHASE 2 "
          "PENDING (it records intent only), so the key is bound and "
          "intercepted but the freeze is not yet effective — surfaced via the "
          "operator-trust badge. Intercepted only while the overlay is "
          "visible." },

        { kVkF1 + 4, 5, "Quick-save (deferred to Phase 7)",
          HotkeyDisposition::Passthrough, HotkeyStatus::Deferred, false,
          "no overlay claim — the game keeps F5",
          "Spec line 29 defers quick-save to Phase 7 (state capture is too "
          "complex for Phase 6). The overlay binds nothing to F5; it passes "
          "through untouched." },

        { kVkF1 + 5, 6, "Recall camera bookmark slot 0",
          HotkeyDisposition::Intercept, HotkeyStatus::Live, false,
          "no default tactical binding observed",
          "F6 recalls iter-304 camera-bookmark slot 0 (Shift+F6 saves). "
          "Intercepted only while the overlay is visible." },

        { kVkF1 + 6, 7, "Recall camera bookmark slot 1",
          HotkeyDisposition::Intercept, HotkeyStatus::Live, false,
          "no default tactical binding observed",
          "F7 recalls iter-304 camera-bookmark slot 1 (Shift+F7 saves). "
          "Intercepted only while the overlay is visible." },

        { kVkF1 + 7, 8, "Recall camera bookmark slot 2",
          HotkeyDisposition::Intercept, HotkeyStatus::Live, false,
          "no default tactical binding observed",
          "F8 recalls iter-304 camera-bookmark slot 2 (Shift+F8 saves). "
          "Intercepted only while the overlay is visible." },

        { kVkF1 + 8, 9, "Quick-load (deferred to Phase 7)",
          HotkeyDisposition::Passthrough, HotkeyStatus::Deferred, false,
          "no overlay claim — the game keeps F9",
          "Spec line 31 defers quick-load to Phase 7 (it pairs with the F5 "
          "quick-save). The overlay binds nothing to F9; it passes through "
          "untouched." },

        { kVkF1 + 9, 10, "(unbound)",
          HotkeyDisposition::Passthrough, HotkeyStatus::Unbound, false,
          "Win32 reserved — F10 alone activates the window menu (WM_SYSKEYDOWN)",
          "F10 is a Win32 system key. The overlay never intercepts it — "
          "swallowing F10 would break the host window's menu and Alt-key "
          "handling. Always passthrough." },

        { kVkF1 + 10, 11, "(unbound)",
          HotkeyDisposition::Passthrough, HotkeyStatus::Unbound, false,
          "no overlay claim",
          "The overlay binds no Phase 6 feature to F11; it passes through so "
          "the game (or a future phase) keeps it." },

        { kVkF1 + 11, 12, "(unbound)",
          HotkeyDisposition::Passthrough, HotkeyStatus::Unbound, false,
          "Win32 reserved — F12 is the conventional debugger-break key",
          "F12 is the default debugger-break key. The overlay never intercepts "
          "it; always passthrough." },
    };

    // Number of catalogued F-key rows (12). sizeof-derived so the count can
    // never drift from the array — exactly as overlay_phase3_catalog.h does.
    inline constexpr std::size_t kHotkeyMatrixCount =
        sizeof(kHotkeyMatrix) / sizeof(kHotkeyMatrix[0]);

    // Look up the matrix row for VK code `vk`. Returns nullptr when `vk` is not
    // an F1..F12 key — the test exercises both the hit and the miss path.
    inline const HotkeyBinding* FindHotkeyBinding(int vk)
    {
        for (std::size_t i = 0; i < kHotkeyMatrixCount; ++i)
        {
            if (kHotkeyMatrix[i].vk == vk) return &kHotkeyMatrix[i];
        }
        return nullptr;
    }

    // Look up the matrix row by F-key number `fkey` (1..12). Returns nullptr
    // when `fkey` is out of range.
    inline const HotkeyBinding* FindHotkeyBindingByFKey(int fkey)
    {
        return FindHotkeyBinding(VkForFKey(fkey));
    }

    // THE CORE DECISION — the deferred overlay.cpp WndProc detour calls this on
    // every F-key WM_KEYDOWN. Returns true to SWALLOW the key (the overlay
    // consumes it; do NOT forward it to the game), false to pass it through.
    //
    //   - `vk` is not a catalogued F-key  -> false (passthrough)
    //   - disposition == Passthrough       -> false (the overlay never claims it)
    //   - disposition == Intercept         -> swallow when the overlay is
    //                                         visible, OR when the row is
    //                                         intercept_when_hidden (only F1).
    inline bool OverlayInterceptsKey(int vk, bool overlayVisible)
    {
        const HotkeyBinding* binding = FindHotkeyBinding(vk);
        if (binding == nullptr) return false;
        if (binding->disposition != HotkeyDisposition::Intercept) return false;
        return overlayVisible || binding->intercept_when_hidden;
    }
}
