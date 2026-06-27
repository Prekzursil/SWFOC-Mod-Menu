// =============================================================================
// swfoc_overlay/overlay_input_test.cpp — unit test for overlay_input.h.
//
// overlay_input.h is dependency-free (no <windows.h>, no ImGui, no D3D9, no
// bridge). This test compiles with a plain g++ and needs no game and no
// window — it exercises the pure WndProc-detour swallow decision. Build + run
// via build_input_test.bat.
//
// RED-GREEN REGRESSION PINS
// ------------------------
// The WndProc detour is dangerous to get wrong: it shares one window with the
// host game. The three checks tagged "PIN" below fail on the two most likely
// regressions:
//   - dropping the visibility gate  -> the overlay steals input even when
//                                      hidden (F1 toggled off freezes the game).
//   - "swallow everything while visible" -> in-game mouse control freezes
//                                      whenever the overlay is on screen, even
//                                      over empty space away from any widget.
// They pass ONLY on the correct three-gate form in ShouldSwallowMessage().
// =============================================================================

#include "overlay_input.h"

#include <cstdio>

namespace
{
    int g_checks = 0;
    int g_failures = 0;

    void ExpectBool(const char* name, bool got, bool want)
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
                        name, got ? "true" : "false", want ? "true" : "false");
        }
    }

    // Win32 WM_* values used as test inputs. Spelled out here so the test
    // reads against real message names; equal to overlay_input.h's wm:: by ABI.
    constexpr unsigned int WM_MOUSEMOVE_   = 0x0200;
    constexpr unsigned int WM_LBUTTONDOWN_ = 0x0201;
    constexpr unsigned int WM_RBUTTONDOWN_ = 0x0204;
    constexpr unsigned int WM_MBUTTONDOWN_ = 0x0207;
    constexpr unsigned int WM_MOUSEWHEEL_  = 0x020A;
    constexpr unsigned int WM_XBUTTONDOWN_ = 0x020B;
    constexpr unsigned int WM_MOUSEHWHEEL_ = 0x020E;
    constexpr unsigned int WM_KEYDOWN_     = 0x0100;
    constexpr unsigned int WM_KEYUP_       = 0x0101;
    constexpr unsigned int WM_CHAR_        = 0x0102;
    constexpr unsigned int WM_SYSKEYDOWN_  = 0x0104;
    constexpr unsigned int WM_SYSDEADCHAR_ = 0x0107;
    constexpr unsigned int WM_PAINT_       = 0x000F;
    constexpr unsigned int WM_SIZE_        = 0x0005;
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_input unit test ==\n");

    // ---- IsMouseMessage -----------------------------------------------------
    ExpectBool("mouse: WM_MOUSEMOVE is a mouse message",
               IsMouseMessage(WM_MOUSEMOVE_), true);
    ExpectBool("mouse: WM_LBUTTONDOWN is a mouse message",
               IsMouseMessage(WM_LBUTTONDOWN_), true);
    ExpectBool("mouse: WM_RBUTTONDOWN is a mouse message",
               IsMouseMessage(WM_RBUTTONDOWN_), true);
    ExpectBool("mouse: WM_MBUTTONDOWN is a mouse message",
               IsMouseMessage(WM_MBUTTONDOWN_), true);
    ExpectBool("mouse: WM_MOUSEWHEEL is a mouse message",
               IsMouseMessage(WM_MOUSEWHEEL_), true);
    ExpectBool("mouse: WM_XBUTTONDOWN is a mouse message",
               IsMouseMessage(WM_XBUTTONDOWN_), true);
    ExpectBool("mouse: WM_MOUSEHWHEEL (range upper bound) is a mouse message",
               IsMouseMessage(WM_MOUSEHWHEEL_), true);
    ExpectBool("mouse: WM_KEYDOWN is NOT a mouse message",
               IsMouseMessage(WM_KEYDOWN_), false);
    ExpectBool("mouse: 0x01FF (just below range) is NOT a mouse message",
               IsMouseMessage(0x01FF), false);
    ExpectBool("mouse: 0x020F (just above range) is NOT a mouse message",
               IsMouseMessage(0x020F), false);

    // ---- IsKeyboardMessage --------------------------------------------------
    ExpectBool("key: WM_KEYDOWN is a keyboard message",
               IsKeyboardMessage(WM_KEYDOWN_), true);
    ExpectBool("key: WM_KEYUP is a keyboard message",
               IsKeyboardMessage(WM_KEYUP_), true);
    ExpectBool("key: WM_CHAR is a keyboard message",
               IsKeyboardMessage(WM_CHAR_), true);
    ExpectBool("key: WM_SYSKEYDOWN is a keyboard message",
               IsKeyboardMessage(WM_SYSKEYDOWN_), true);
    ExpectBool("key: WM_SYSDEADCHAR (range upper bound) is a keyboard message",
               IsKeyboardMessage(WM_SYSDEADCHAR_), true);
    ExpectBool("key: WM_MOUSEMOVE is NOT a keyboard message",
               IsKeyboardMessage(WM_MOUSEMOVE_), false);
    ExpectBool("key: 0x00FF (just below range) is NOT a keyboard message",
               IsKeyboardMessage(0x00FF), false);
    ExpectBool("key: 0x0108 (just above range) is NOT a keyboard message",
               IsKeyboardMessage(0x0108), false);

    // ---- ShouldSwallowMessage ----------------------------------------------
    // PIN: a hidden overlay never steals input — the game must play normally
    // with F1 toggled off even though ImGui can still report WantCapture flags
    // from the last visible frame. A regression dropping the visibility gate
    // flips these to true and freezes the game whenever the overlay is hidden.
    ExpectBool("PIN swallow: hidden overlay never swallows (mouse)",
               ShouldSwallowMessage(WM_LBUTTONDOWN_, false, true, true), false);
    ExpectBool("PIN swallow: hidden overlay never swallows (keyboard)",
               ShouldSwallowMessage(WM_KEYDOWN_, false, true, true), false);

    ExpectBool("swallow: visible + wantMouse + mouse msg -> swallow",
               ShouldSwallowMessage(WM_LBUTTONDOWN_, true, true, false), true);

    // PIN: visible but ImGui does NOT want the mouse (cursor over empty space,
    // not a widget) — the click belongs to the game. A "swallow everything
    // while visible" regression flips this to true and freezes in-game mouse
    // control whenever the overlay is up.
    ExpectBool("PIN swallow: visible + NOT wantMouse + mouse msg -> pass through",
               ShouldSwallowMessage(WM_LBUTTONDOWN_, true, false, false), false);

    ExpectBool("swallow: visible + wantKeyboard + key msg -> swallow",
               ShouldSwallowMessage(WM_KEYDOWN_, true, false, true), true);
    ExpectBool("swallow: visible + NOT wantKeyboard + key msg -> pass through",
               ShouldSwallowMessage(WM_KEYDOWN_, true, false, false), false);

    // Cross-class: a mouse-capture want must not swallow keyboard messages,
    // and a keyboard-capture want must not swallow mouse messages.
    ExpectBool("swallow: wantMouse does NOT swallow a keyboard msg",
               ShouldSwallowMessage(WM_KEYDOWN_, true, true, false), false);
    ExpectBool("swallow: wantKeyboard does NOT swallow a mouse msg",
               ShouldSwallowMessage(WM_LBUTTONDOWN_, true, false, true), false);

    // PIN: non-input messages always pass through, even with the overlay
    // visible and ImGui wanting all input — the game must still receive
    // WM_PAINT / WM_SIZE or it stops drawing / resizing.
    ExpectBool("PIN swallow: WM_PAINT passes through (non-input msg)",
               ShouldSwallowMessage(WM_PAINT_, true, true, true), false);
    ExpectBool("PIN swallow: WM_SIZE passes through (non-input msg)",
               ShouldSwallowMessage(WM_SIZE_, true, true, true), false);

    // Wheel is a mouse message and is swallowed when ImGui wants the mouse,
    // so scrolling an overlay list does not also zoom the game camera.
    ExpectBool("swallow: visible + wantMouse + wheel -> swallow",
               ShouldSwallowMessage(WM_MOUSEWHEEL_, true, true, false), true);

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
