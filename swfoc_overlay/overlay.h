// =============================================================================
// swfoc_overlay — public surface for the overlay's bootstrap.
// =============================================================================

#pragma once

namespace swfoc_overlay
{
    // Detours IDirect3DDevice9::Present (and friends) so we can paint over the
    // game on every frame. Idempotent — calling twice is a no-op.
    void Install();

    // Reverses Install's hooks. Called from DLL_PROCESS_DETACH.
    void Uninstall();

    // Hotkey state — toggled on F1 by the host's WndProc detour. Phase 1
    // exposes this for diagnostic logging; Phase 2 ImGui paths read it
    // each frame to decide whether to render.
    bool IsVisible();
    void SetVisible(bool visible);
    void ToggleVisible();
}
