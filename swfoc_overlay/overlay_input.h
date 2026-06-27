// =============================================================================
// swfoc_overlay/overlay_input.h — Phase 3 host-window input routing decision.
//
// Phase 3's interactive widgets need mouse + keyboard input, but the overlay
// shares ONE window with the host game. Phase 1-2 deliberately never detoured
// the host WndProc (it polls F1 from a worker thread) so it could not steal
// input. Phase 3 changes that: overlay.cpp now subclasses the host WndProc so
// ImGui sees mouse/keyboard messages.
//
// The hazard a WndProc detour introduces is INPUT BLEED, in both directions:
//   - forward too much   -> a click on an overlay button ALSO commands the
//                           game (e.g. orders the selected unit to move).
//   - forward too little -> the game stops responding to the mouse whenever
//                           the overlay is on screen, even over empty space.
//
// ShouldSwallowMessage() is the pure decision at the heart of that detour:
// given a window message and ImGui's per-frame WantCapture* flags, it answers
// "does the overlay consume this, or does it pass through to the game?". It is
// dependency-free (no <windows.h>, no ImGui) so the routing rule is unit-
// tested with a plain g++ — see overlay_input_test.cpp (build_input_test.bat),
// mirroring overlay_action_queue.h / overlay_actions.h.
// =============================================================================

#pragma once

namespace swfoc_overlay
{
    // Win32 window-message identifiers this classifier needs. These mirror the
    // <windows.h> WM_* constants, which are a frozen ABI — Microsoft cannot
    // renumber them without breaking every Windows program ever compiled.
    // Mirroring the two contiguous ranges here (rather than #include
    // <windows.h>) keeps the header dependency-free and the swallow rule
    // unit-testable with a plain g++, exactly like overlay_action_queue.h.
    // overlay.cpp passes the real WM_* message id; it equals these by ABI.
    namespace wm
    {
        // Mouse block: contiguous 0x0200..0x020E
        // (WM_MOUSEMOVE / WM_MOUSEFIRST .. WM_MOUSEHWHEEL / WM_MOUSELAST).
        constexpr unsigned int kMouseFirst = 0x0200u;
        constexpr unsigned int kMouseLast  = 0x020Eu;

        // Keyboard block: contiguous 0x0100..0x0107
        // (WM_KEYDOWN / WM_KEYFIRST .. WM_SYSDEADCHAR). Covers KEYDOWN / UP,
        // CHAR / DEADCHAR and their SYS* (Alt-modified) counterparts.
        constexpr unsigned int kKeyFirst   = 0x0100u;
        constexpr unsigned int kKeyLast    = 0x0107u;
    }

    // True when `msg` is a mouse input message (move / any button / wheel).
    inline bool IsMouseMessage(unsigned int msg)
    {
        return msg >= wm::kMouseFirst && msg <= wm::kMouseLast;
    }

    // True when `msg` is a keyboard input message (key up/down, char, and
    // their Alt-modified SYS* variants).
    inline bool IsKeyboardMessage(unsigned int msg)
    {
        return msg >= wm::kKeyFirst && msg <= wm::kKeyLast;
    }

    // The core WndProc-detour decision. Return true to SWALLOW the message
    // (the overlay consumes it; do NOT forward to the game's WndProc), false
    // to pass it through.
    //
    // The rule has three independent gates, ALL of which must hold to swallow:
    //   1. overlayVisible      — a hidden overlay never steals input; the game
    //                            plays normally with F1 toggled off.
    //   2. imguiWants{Mouse,Keyboard} — ImGui only wants the input when the
    //                            cursor is over a widget / a widget is being
    //                            typed into. Off a widget, the click belongs
    //                            to the game even with the overlay visible.
    //   3. message-class match — a mouse-capture want only swallows mouse
    //                            messages; a keyboard-capture want only
    //                            swallows keyboard messages. Non-input
    //                            messages (WM_PAINT, WM_SIZE, ...) always
    //                            pass through so the game keeps functioning.
    inline bool ShouldSwallowMessage(unsigned int msg,
                                     bool overlayVisible,
                                     bool imguiWantsMouse,
                                     bool imguiWantsKeyboard)
    {
        if (!overlayVisible) return false;
        if (imguiWantsMouse && IsMouseMessage(msg)) return true;
        if (imguiWantsKeyboard && IsKeyboardMessage(msg)) return true;
        return false;
    }
}
