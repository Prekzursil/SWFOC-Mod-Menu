// =============================================================================
// swfoc_overlay — D3D9 detour core (Phase 1 skeleton).
//
// Pattern is the standard "create a throwaway D3D9 device to harvest the
// vtable, then MinHook the IDirect3DDevice9::Present slot in the host
// process's vtable so every frame routes through us first." See:
// - https://github.com/rdbo/D3D9-Hook (reference implementation)
// - swfoc_lua_bridge/lua_bridge.cpp (sibling project, same MinHook usage)
//
// Phase 1 deliberately does NOT render anything. The Present detour just
// pumps a frame counter and forwards. Phase 2 drops in ImGui.
// =============================================================================

#include "overlay.h"
#include "hud_state.h"
#include "overlay_actions.h"
#include "overlay_input.h"
#include "overlay_action_worker.h"  // iter 516: Phase 3 action-worker lifecycle
#include "overlay_recent_actions.h" // iter 521: Phase 3 recent-actions toolbar
#include "overlay_phase3_catalog.h" // iter 527: Phase 3 per-widget capability badges
#include "overlay_dragdrop.h"       // iter 529: Phase 4 drag-drop spawn kernel
#include "overlay_minimap.h"        // iter 530: Phase 4 tactical minimap kernel
#include "overlay_preview_ring.h"   // iter 531: Phase 4 drop-point preview ring
#include "overlay_spawn_gate.h"     // iter 532: Phase 4 multi-player safety gate

#include <windows.h>
#include <d3d9.h>
#include "minhook/include/MinHook.h"

// 2026-05-08 (iter 276): ImGui v1.91.5 vendored under swfoc_overlay/imgui/.
// 2026-05-08 (iter 277): ImGui::CreateContext + DX9/Win32 Init wired into
// the Present_Detour for Phase 2-full Tier 1 ("Hello, operator" panel).
// Phase 2-lite amber rectangle render path (DrawVisibleBadge) stays intact
// as a defensive fallback per iter-275 design Option A. Iter 278 will
// replace the minimal panel with a 4-row HUD strip rendered via ImGui
// Tables + ProgressBar widgets consuming the HudSnapshot model unchanged.
#include "imgui/imgui.h"
#include "imgui/backends/imgui_impl_dx9.h"
#include "imgui/backends/imgui_impl_win32.h"

#include <algorithm>  // iter 278: std::min for ProgressBar ratio clamps
#include <atomic>
#include <cstdio>
#include <cstdlib>  // iter 520: std::strtoull for the Kill-button hex field

// imgui_impl_win32.h intentionally guards this declaration behind a '#if 0'
// block to avoid forcing <windows.h> on every includer; the header instructs
// you to copy the line into your own .cpp. The overlay's WndProc detour
// (iter 514) needs it so ImGui can observe the host window's mouse + keyboard
// messages. Signature must match imgui_impl_win32.cpp's definition exactly.
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(
    HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

namespace
{
    // ---- Visibility state ---------------------------------------------------
    std::atomic<bool> g_visible{false};

    // ---- Frame counter ------------------------------------------------------
    // Incremented once per Present detour. Phase 1 dumps to stdout/debug log
    // every 600 frames (~10 sec at 60 fps) as proof-of-life.
    std::atomic<uint64_t> g_frameCount{0};

    // ---- Hotkey monitor -----------------------------------------------------
    // Phase 1 polls F1 from the bootstrap worker thread instead of detouring
    // WndProc — simpler, safer, no risk of deadlocking the host's window
    // message pump. Resolution: 100 ms tick.
    std::atomic<bool> g_hotkeyShutdown{false};
    HANDLE g_hotkeyThread = nullptr;

    DWORD WINAPI HotkeyPollLoop(LPVOID)
    {
        SHORT prevState = 0;
        while (!g_hotkeyShutdown.load(std::memory_order_relaxed))
        {
            const SHORT state = GetAsyncKeyState(VK_F1);
            const bool downNow = (state & 0x8000) != 0;
            const bool downPrev = (prevState & 0x8000) != 0;
            if (downNow && !downPrev)
            {
                swfoc_overlay::ToggleVisible();
                OutputDebugStringA("[swfoc_overlay] F1 toggled visibility\n");
            }
            prevState = state;
            Sleep(100);
        }
        return 0;
    }

    // ---- D3D9 Present detour ------------------------------------------------
    typedef HRESULT (WINAPI *PresentFn)(
        IDirect3DDevice9*, const RECT*, const RECT*, HWND, const RGNDATA*);
    typedef HRESULT (WINAPI *ResetFn)(
        IDirect3DDevice9*, D3DPRESENT_PARAMETERS*);

    PresentFn g_origPresent = nullptr;
    ResetFn g_origReset = nullptr;

    // ---- Host WndProc detour (iter 514) ------------------------------------
    // Phase 3 needs ImGui to receive mouse + keyboard input. We subclass the
    // host game's window procedure (SetWindowLongPtrW) so every message routes
    // through HookedWndProc first. g_origWndProc holds the game's original
    // procedure for forwarding each message and for restore-on-uninstall.
    WNDPROC g_origWndProc = nullptr;

    // Forward declarations — Phase 2-full ImGui plumbing helpers are defined
    // after the vtable-harvest section below to keep the file's read order
    // (frame counter → hotkey → render → detours → vtable → ImGui plumbing).
    // HookedPresent + HookedReset reference these; declare them here so the
    // C++ compiler accepts the forward references. The variable is `extern`
    // to allow the actual definition (with brace-init) below to coexist.
    extern std::atomic<bool> g_imguiInitialized;
    void EnsureImGuiInit(IDirect3DDevice9* dev, HWND hwnd);
    void ShutdownImGui();
    void RenderImGuiPanel();
    void RenderActionsWindow();
    void RenderActionToast();

    // ---- HUD support helpers (faction tinting) -----------------------------
    // 2026-04-28 (iter 103, master ralph loop): faction tinting on the
    // bridge LED. Maps the local-player slot to a faction color when
    // we know who we are. Slot 0..7 fan-out matches SWFOC's standard
    // skirmish: slot 0 = local human, slots 1+ = AI factions; the most
    // common single-player layouts put REBEL in slot 0 (amber) or
    // EMPIRE in slot 0 (chrome). When unknown, falls back to neutral
    // green/red. Iter 100 unblocked SWFOC_GetLocalPlayer; the worker
    // populates `local_player_slot` whenever bridge is reachable.
    //
    // 2026-05-08 (iter 278): retained from Phase 2-lite for ImGui
    // panel reuse. Phase 2-lite vertex-render path (DrawVisibleBadge +
    // ScreenVertex / HudBar / Build*Bar helpers) was REMOVED in iter 278
    // because RenderImGuiPanel now owns the entire HUD render path via
    // ImGui Tables + ProgressBar. The amber AARRGGBB color packing
    // convention (`0xCCAARRGGBB` = ~80% alpha + faction tint) is
    // preserved so iter 278+ can convert to ImVec4 for ImGui consumption
    // via `ARGB_TO_IMVEC4` below.
    DWORD FactionTintForSlot(int slot)
    {
        switch (slot)
        {
            case 0: return 0xCCFFB400u;   // REBEL amber (Star Wars rebellion)
            case 1: return 0xCCDCDCDCu;   // EMPIRE chrome
            case 2: return 0xCCC8965Au;   // UNDERWORLD sand+rust
            default: return 0xCC22AA22u;  // generic green for unknown / 4-7
        }
    }

    // Convert a 32-bit AARRGGBB DWORD (Phase 2-lite color convention) into
    // an ImGui ImVec4 (RGBA in 0..1 range). Used by RenderImGuiPanel for
    // the bridge-LED row's faction-tint coloring.
    ImVec4 DwordToImVec4(DWORD argb)
    {
        const float a = static_cast<float>((argb >> 24) & 0xFFu) / 255.0f;
        const float r = static_cast<float>((argb >> 16) & 0xFFu) / 255.0f;
        const float g = static_cast<float>((argb >>  8) & 0xFFu) / 255.0f;
        const float b = static_cast<float>((argb >>  0) & 0xFFu) / 255.0f;
        return ImVec4(r, g, b, a);
    }

    HRESULT WINAPI HookedPresent(
        IDirect3DDevice9* dev, const RECT* src, const RECT* dst, HWND hwnd, const RGNDATA* dirty)
    {
        const auto frame = g_frameCount.fetch_add(1, std::memory_order_relaxed);
        if (frame % 600 == 0)
        {
            char msg[128];
            const bool vis = g_visible.load(std::memory_order_relaxed);
            std::snprintf(msg, sizeof(msg),
                "[swfoc_overlay] Present frame=%llu visible=%d\n",
                static_cast<unsigned long long>(frame), vis ? 1 : 0);
            OutputDebugStringA(msg);
        }

        // 2026-05-08 (iter 277, Phase 2-full Tier 1): lazy-init ImGui on the
        // first Present we see. Subsequent frames hit the early-return inside
        // EnsureImGuiInit and skip the work.
        EnsureImGuiInit(dev, hwnd);

        // 2026-05-08 (iter 278): RenderImGuiPanel now owns the entire HUD
        // render path. Phase 2-lite DrawVisibleBadge (iter-43) was removed
        // when iter-278's ImGui Table + ProgressBar layout replaced the
        // raw-vertex amber strip. RenderImGuiPanel internally checks
        // g_imguiInitialized + g_visible; safe to call unconditionally.
        RenderImGuiPanel();

        return g_origPresent(dev, src, dst, hwnd, dirty);
    }

    HRESULT WINAPI HookedReset(IDirect3DDevice9* dev, D3DPRESENT_PARAMETERS* params)
    {
        // Reset is fired when the device loses + recovers (e.g. alt-tab,
        // resolution change). ImGui needs to release device-bound resources
        // BEFORE Reset and recreate them AFTER. iter 277 wires the
        // ImGui_ImplDX9_InvalidateDeviceObjects / _CreateDeviceObjects calls
        // around the forwarded Reset. Guarded on g_imguiInitialized so a
        // pre-init Reset (very early in game startup) is a no-op for ImGui.
        if (g_imguiInitialized.load(std::memory_order_acquire))
        {
            ImGui_ImplDX9_InvalidateDeviceObjects();
        }
        const HRESULT hr = g_origReset(dev, params);
        if (SUCCEEDED(hr) && g_imguiInitialized.load(std::memory_order_acquire))
        {
            ImGui_ImplDX9_CreateDeviceObjects();
        }
        return hr;
    }

    // ---- Host WndProc detour (iter 514) ------------------------------------
    // Subclasses the game's window procedure so ImGui can observe mouse +
    // keyboard input. Installed by EnsureImGuiInit once the HWND is known;
    // restored by ShutdownImGui. The forwarding decision is the pure
    // swfoc_overlay::ShouldSwallowMessage rule (overlay_input.h, unit-tested
    // by overlay_input_test.cpp): the overlay only consumes input while it is
    // visible AND ImGui actually wants that input class — off a widget, or
    // with the overlay hidden, every message still reaches the game.
    LRESULT CALLBACK HookedWndProc(
        HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
    {
        if (g_imguiInitialized.load(std::memory_order_acquire))
        {
            // Let ImGui observe the message (mouse position, focus, key
            // state) regardless of who ultimately owns it. The return value
            // is intentionally ignored — an injected overlay decides
            // forwarding from WantCapture*, not from the handler's verdict,
            // so the game keeps receiving input the overlay does not need.
            ImGui_ImplWin32_WndProcHandler(hwnd, msg, wParam, lParam);

            const ImGuiIO& io = ImGui::GetIO();
            if (swfoc_overlay::ShouldSwallowMessage(
                    msg,
                    g_visible.load(std::memory_order_relaxed),
                    io.WantCaptureMouse,
                    io.WantCaptureKeyboard))
            {
                // ImGui consumed this input while the overlay is visible:
                // do not also hand it to the game, or a click on an overlay
                // button would issue an in-game order at the same time.
                return TRUE;
            }
        }

        // Defensive: if the subclass somehow ran without a captured original
        // (SetWindowLongPtrW returned 0), fall back to the default handler
        // rather than calling through a null procedure.
        if (!g_origWndProc)
        {
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        return CallWindowProcW(g_origWndProc, hwnd, msg, wParam, lParam);
    }

    // ---- VTable harvesting --------------------------------------------------
    // Spin up a hidden window + throwaway D3D9 device just long enough to
    // read its vtable, capture Present/Reset slots, then tear down. The
    // host's later D3D9 device calls will hit our detours.
    void* HarvestVtableSlot(int slotIndex)
    {
        IDirect3D9* d3d9 = Direct3DCreate9(D3D_SDK_VERSION);
        if (!d3d9)
        {
            OutputDebugStringA("[swfoc_overlay] Direct3DCreate9 failed\n");
            return nullptr;
        }

        WNDCLASSA wc{};
        wc.lpfnWndProc = DefWindowProcA;
        wc.hInstance = GetModuleHandleA(nullptr);
        wc.lpszClassName = "SWFOCOverlayHarvest";
        RegisterClassA(&wc);

        HWND wnd = CreateWindowA(wc.lpszClassName, "harvest", 0,
            0, 0, 1, 1, nullptr, nullptr, wc.hInstance, nullptr);
        if (!wnd)
        {
            d3d9->Release();
            return nullptr;
        }

        D3DPRESENT_PARAMETERS pp{};
        pp.Windowed = TRUE;
        pp.SwapEffect = D3DSWAPEFFECT_DISCARD;
        pp.hDeviceWindow = wnd;

        IDirect3DDevice9* dev = nullptr;
        HRESULT hr = d3d9->CreateDevice(
            D3DADAPTER_DEFAULT,
            D3DDEVTYPE_HAL,
            wnd,
            D3DCREATE_SOFTWARE_VERTEXPROCESSING,
            &pp,
            &dev);

        void* slot = nullptr;
        if (SUCCEEDED(hr) && dev)
        {
            void** vtable = *reinterpret_cast<void***>(dev);
            slot = vtable[slotIndex];
            dev->Release();
        }
        else
        {
            char err[64];
            std::snprintf(err, sizeof(err),
                "[swfoc_overlay] CreateDevice failed hr=0x%08lx\n",
                static_cast<unsigned long>(hr));
            OutputDebugStringA(err);
        }

        DestroyWindow(wnd);
        UnregisterClassA(wc.lpszClassName, wc.hInstance);
        d3d9->Release();
        return slot;
    }

    // VTable indices (D3D9 contract — fixed across all SDK versions)
    constexpr int kSlotReset = 16;
    constexpr int kSlotPresent = 17;

    // ---- Phase 2-full ImGui plumbing (iter 277) -----------------------------
    // Lazy-init pattern: first Present_Detour call captures the device + HWND,
    // initializes ImGui + DX9/Win32 backends, sets the flag. Subsequent frames
    // skip Init and just call NewFrame/Render. Reset_Detour invalidates and
    // recreates device-bound resources around the host's IDirect3DDevice9::Reset.
    // Uninstall calls Shutdown to release everything.
    //
    // Acquire/release ordering on `g_imguiInitialized` because Init/Shutdown
    // must happen-before any NewFrame/Render observation. Per Phase 1 design
    // we don't detour WndProc, so ImGui won't capture mouse/keyboard input —
    // for Phase 2-full Tier 1 ("Hello, operator" panel) that's acceptable;
    // the panel renders without interaction. iter 278+ Tier 2 can detour
    // WndProc if interactivity is needed.
    std::atomic<bool> g_imguiInitialized{false};
    HWND g_imguiHwnd = nullptr;

    void EnsureImGuiInit(IDirect3DDevice9* dev, HWND hwnd)
    {
        if (g_imguiInitialized.load(std::memory_order_acquire)) return;

        // Resolve the HWND. Prefer the Present-passed hwnd; fall back to the
        // device's swap chain present-parameters when the game passes nullptr
        // (common in fullscreen). Without a valid HWND we can't init Win32
        // backend, so skip and retry next frame.
        HWND useHwnd = hwnd;
        if (!useHwnd && dev)
        {
            IDirect3DSwapChain9* sc = nullptr;
            if (SUCCEEDED(dev->GetSwapChain(0, &sc)) && sc)
            {
                D3DPRESENT_PARAMETERS pp{};
                if (SUCCEEDED(sc->GetPresentParameters(&pp)))
                {
                    useHwnd = pp.hDeviceWindow;
                }
                sc->Release();
            }
        }
        if (!useHwnd) return;  // Retry next frame.

        IMGUI_CHECKVERSION();
        ImGui::CreateContext();
        ImGuiIO& io = ImGui::GetIO();
        io.IniFilename = nullptr;  // Don't write imgui.ini next to StarWarsG.exe.
        io.LogFilename = nullptr;  // Don't write imgui_log.txt either.

        ImGui::StyleColorsDark();

        if (!ImGui_ImplWin32_Init(useHwnd))
        {
            OutputDebugStringA("[swfoc_overlay] ImGui_ImplWin32_Init failed\n");
            ImGui::DestroyContext();
            return;
        }
        if (!ImGui_ImplDX9_Init(dev))
        {
            OutputDebugStringA("[swfoc_overlay] ImGui_ImplDX9_Init failed\n");
            ImGui_ImplWin32_Shutdown();
            ImGui::DestroyContext();
            return;
        }

        g_imguiHwnd = useHwnd;

        // iter 514: subclass the host window so ImGui receives mouse +
        // keyboard input (Phase 3 interactive widgets). Installed before the
        // initialized flag is published so that, the instant a render path
        // observes g_imguiInitialized == true, the input route already
        // exists. HookedWndProc itself gates on the same flag, so a message
        // arriving in the gap merely forwards straight to the game.
        g_origWndProc = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(
            useHwnd, GWLP_WNDPROC,
            reinterpret_cast<LONG_PTR>(&HookedWndProc)));

        g_imguiInitialized.store(true, std::memory_order_release);
        OutputDebugStringA(
            "[swfoc_overlay] ImGui Init OK (Phase 3 WndProc detour active)\n");
    }

    void ShutdownImGui()
    {
        if (!g_imguiInitialized.load(std::memory_order_acquire)) return;

        // iter 514: restore the host window procedure BEFORE tearing down
        // ImGui. HookedWndProc dereferences ImGui state (GetIO); unhooking
        // first guarantees no message can reach it once the context is gone.
        if (g_origWndProc && g_imguiHwnd)
        {
            SetWindowLongPtrW(g_imguiHwnd, GWLP_WNDPROC,
                reinterpret_cast<LONG_PTR>(g_origWndProc));
            g_origWndProc = nullptr;
        }

        ImGui_ImplDX9_Shutdown();
        ImGui_ImplWin32_Shutdown();
        ImGui::DestroyContext();
        g_imguiHwnd = nullptr;
        g_imguiInitialized.store(false, std::memory_order_release);
        OutputDebugStringA("[swfoc_overlay] ImGui Shutdown OK\n");
    }

    void RenderImGuiPanel()
    {
        if (!g_imguiInitialized.load(std::memory_order_acquire)) return;

        ImGui_ImplDX9_NewFrame();
        ImGui_ImplWin32_NewFrame();
        ImGui::NewFrame();

        // Phase 2-full Tier 1 (iter 278): 4-row HUD strip rendered via ImGui
        // Tables + ProgressBar, replacing the iter-43 Phase 2-lite raw-D3D9
        // vertex amber strip. Consumes the existing HudSnapshot model
        // unchanged (iter-103 5-row layout: bridge LED, credits, alive
        // units, scene, last-error). Phase 2-full Tier 2 (iter 279) extends
        // with catalog rollup + multipliers + faction-tint consistency.
        if (g_visible.load(std::memory_order_relaxed))
        {
            const auto snap = swfoc_overlay::GetHudSnapshot();

            // Layout: bottom-right of back-buffer with 12px margin.
            // 5 rows + headers + footer ⇒ ~180px tall, 280px wide.
            const ImGuiIO& io = ImGui::GetIO();
            constexpr float kPanelW = 280.0f;
            // iter 279 Tier 2: extended height to fit catalog row + 2
            // multiplier rows + separator gaps. Was 180 in iter-278.
            constexpr float kPanelH = 250.0f;
            constexpr float kMargin = 12.0f;
            const ImVec2 pos(
                io.DisplaySize.x - kPanelW - kMargin,
                io.DisplaySize.y - kPanelH - kMargin);
            ImGui::SetNextWindowPos(pos, ImGuiCond_Always);
            ImGui::SetNextWindowSize(ImVec2(kPanelW, kPanelH), ImGuiCond_Always);
            ImGui::SetNextWindowBgAlpha(0.78f);  // ~80% — readable over game

            // 2026-05-08 (iter 279, Phase 2-full Tier 2): faction-tint
            // consistency. Push window border + separator-active to the
            // current faction tint when bridge is reachable AND slot is
            // known (iter-103 palette via DwordToImVec4). Falls back to
            // ImGui's default theme colors when bridge is offline so the
            // operator immediately sees the un-tinted state.
            int factionTintStyleColors = 0;
            if (snap.bridge_reachable && snap.local_player_slot >= 0)
            {
                const ImVec4 tint = DwordToImVec4(
                    FactionTintForSlot(snap.local_player_slot));
                ImGui::PushStyleColor(ImGuiCol_Border, tint);
                ImGui::PushStyleColor(ImGuiCol_SeparatorActive, tint);
                ImGui::PushStyleColor(ImGuiCol_SeparatorHovered, tint);
                factionTintStyleColors = 3;
            }

            constexpr ImGuiWindowFlags kFlags =
                ImGuiWindowFlags_NoTitleBar |
                ImGuiWindowFlags_NoResize |
                ImGuiWindowFlags_NoMove |
                ImGuiWindowFlags_NoSavedSettings |
                ImGuiWindowFlags_NoFocusOnAppearing |
                ImGuiWindowFlags_NoNav;

            if (ImGui::Begin("SWFOC Overlay", nullptr, kFlags))
            {
                // ----- Tier 2 (iter 279): Catalog rollup at top -----
                // Hardcoded iter-274 audit snapshot. Source of truth lives
                // editor-side in CapabilityStatusCatalog.cs; future iter
                // could marshal this via shared file or named pipe. For
                // Tier 2 we ship the static numbers + an iter-N tag so
                // operators see the catalog state at compile time and know
                // when it was last refreshed.
                ImGui::TextDisabled(
                    "Catalog (iter-274): 142 LIVE / 25 PHASE 2 / 0 LIVE ONLY");
                ImGui::Separator();

                // ----- Row 0: Bridge LED (faction-tinted when reachable) -----
                if (snap.bridge_reachable)
                {
                    const ImVec4 tint = DwordToImVec4(
                        FactionTintForSlot(snap.local_player_slot));
                    ImGui::PushStyleColor(ImGuiCol_Text, tint);
                    ImGui::Text("Bridge: ONLINE");
                    ImGui::PopStyleColor();
                    ImGui::SameLine();
                    ImGui::TextDisabled("(slot %d)", snap.local_player_slot);
                }
                else
                {
                    ImGui::PushStyleColor(ImGuiCol_Text,
                        ImVec4(0.85f, 0.20f, 0.13f, 1.0f));  // red — no bridge
                    ImGui::Text("Bridge: OFFLINE");
                    ImGui::PopStyleColor();
                }
                ImGui::Separator();

                // ----- Row 1: Credits (numeric + ProgressBar 0..1M) -----
                if (snap.credits >= 0)
                {
                    const float ratio = static_cast<float>(
                        std::min<double>(1.0,
                            static_cast<double>(snap.credits) / 1'000'000.0));
                    char label[64];
                    std::snprintf(label, sizeof(label), "%lld cr",
                        static_cast<long long>(snap.credits));
                    ImGui::Text("Credits");
                    ImGui::ProgressBar(ratio, ImVec2(-1.0f, 0.0f), label);
                }
                else
                {
                    ImGui::TextDisabled("Credits: unknown");
                }

                // ----- Row 2: Alive units (numeric + ProgressBar 0..200) -----
                if (snap.alive_units >= 0)
                {
                    const float ratio = static_cast<float>(
                        std::min<double>(1.0,
                            static_cast<double>(snap.alive_units) / 200.0));
                    char label[32];
                    std::snprintf(label, sizeof(label), "%d / 200",
                        snap.alive_units);
                    ImGui::Text("Units");
                    ImGui::ProgressBar(ratio, ImVec2(-1.0f, 0.0f), label);
                }
                else
                {
                    ImGui::TextDisabled("Units: unknown");
                }

                // ----- Row 3: Scene name -----
                if (!snap.scene_name.empty())
                {
                    ImGui::Text("Scene: %s", snap.scene_name.c_str());
                }
                else
                {
                    ImGui::TextDisabled("Scene: unknown");
                }

                // ----- Row 4: Last error (only shown when present) -----
                if (!snap.last_error.empty())
                {
                    ImGui::Separator();
                    ImGui::PushStyleColor(ImGuiCol_Text,
                        ImVec4(0.87f, 0.20f, 0.13f, 1.0f));
                    ImGui::TextWrapped("Error: %s", snap.last_error.c_str());
                    ImGui::PopStyleColor();
                }

                // ----- Tier 2 (iter 279) + iter 281 + iter 282: active multipliers -----
                // iter 281 lifted iter-279's damage_mult honest-defer via the
                // existing iter-96 SWFOC_GetDamageMultiplierGlobal getter pair.
                // iter 282 lifted the firerate_mult honest-defer too — discovered
                // mid-iter that SWFOC_GetFireRateMultiplierGlobal was already
                // LIVE in the bridge (lua_bridge.cpp:6794, registered at line
                // 7616) and already catalogued/simulator-handled, contrary to
                // iter-281's premise that it needed a fresh bridge getter pair.
                // Pattern lesson: BOTH directions of infrastructure-claim drift
                // matter — claims of "missing" are as suspect as claims of
                // "present". A 5-second `grep` would have closed iter-281's
                // defer immediately. Tier 2 row group now fully resolved.
                ImGui::Separator();
                auto renderMultRow = [](const char* label, float mult)
                {
                    if (mult >= 0.0f)
                    {
                        // Color-code: amber when scaled (!=1.0), gray when neutral
                        // (within ±1% float epsilon). Matches editor's
                        // WarningForeground brand for "scalar is active" cue.
                        const bool scaled = (mult < 0.99f || mult > 1.01f);
                        if (scaled)
                        {
                            ImGui::PushStyleColor(ImGuiCol_Text,
                                ImVec4(1.0f, 0.706f, 0.0f, 1.0f));  // amber
                            ImGui::Text("%s: %.2fx", label, mult);
                            ImGui::PopStyleColor();
                        }
                        else
                        {
                            ImGui::TextDisabled("%s: %.2fx (neutral)", label, mult);
                        }
                    }
                    else
                    {
                        ImGui::TextDisabled("%s: probe pending", label);
                    }
                };
                renderMultRow("Damage mult", snap.damage_mult);
                renderMultRow("Fire-rate mult", snap.firerate_mult);

                // ----- Tier 3 (iter 284): session timer + counter row group -----
                // Session elapsed is local-clock-driven (no bridge wire);
                // kill/death/units-alive are HONEST DEFER pending iter-285
                // bridge additions (grep confirmed: lua_bridge.cpp has
                // SWFOC_KillUnit write-side but no read-side counters).
                ImGui::Separator();
                {
                    // Mission/session timer — always renderable since it's
                    // computed on the worker thread without a bridge wire.
                    const uint64_t s = snap.session_elapsed_seconds;
                    const uint64_t mm = s / 60;
                    const uint64_t ss = s % 60;
                    if (s > 0)
                    {
                        ImGui::Text("Session: %02llu:%02llu", mm, ss);
                    }
                    else
                    {
                        ImGui::TextDisabled("Session: --:--");
                    }
                }
                auto renderCounterRow = [](const char* label, int count)
                {
                    if (count >= 0)
                    {
                        ImGui::Text("%s: %d", label, count);
                    }
                    else
                    {
                        ImGui::TextDisabled("%s: awaits iter-285+", label);
                    }
                };
                renderCounterRow("Kills (you)", snap.local_kills);
                renderCounterRow("Deaths (you)", snap.local_deaths);
                renderCounterRow("Units in play", snap.total_units_in_play);

                // ----- Footer: F1 hint + iter tag -----
                ImGui::Separator();
                ImGui::TextDisabled(
                    "F1 toggles | Phase 2-full @ iter 285 (Tier 3 complete)");
            }
            ImGui::End();

            // 2026-05-08 (iter 279): pop the faction-tint chrome colors
            // pushed before ImGui::Begin so the styles don't leak into
            // subsequent ImGui calls in this frame (idempotent today —
            // RenderImGuiPanel only opens one window — but defensive
            // for iter 280+ extensions).
            if (factionTintStyleColors > 0)
            {
                ImGui::PopStyleColor(factionTintStyleColors);
            }

            // 2026-05-21 (iter 512, Phase 3 kickoff): interactive Actions
            // widget skeleton rendered as a separate window below the Tier
            // strip. Definition + rationale below RenderImGuiPanel.
            RenderActionsWindow();
        }

        ImGui::Render();
        ImGui_ImplDX9_RenderDrawData(ImGui::GetDrawData());
    }

    // ---- Phase 3 (iter 512 / 514 / 516 / 520 / 521 / 524): interactive Actions ---
    // Renders the Spawn / Make-Invuln / Kill widget in its own window below
    // the Tier strip. iter 512 shipped the skeleton; iter 514 added the host
    // WndProc detour (overlay_input.h) so ImGui receives mouse + keyboard
    // input; iter 516 wired the action-worker lifecycle into the DLL. iter
    // 520 wires the three action buttons' onClick handlers: each click builds
    // a Lua line (overlay_actions.h) and Enqueue()s an ActionRequest onto the
    // process-wide ActionQueueInstance() (overlay_action_worker.cpp). The
    // background drain worker performs the blocking bridge round-trip off the
    // render thread; the footer toast reads LatestResult() once per frame so
    // the operator sees PENDING -> LIVE / FAILED and never confuses a click
    // with a confirmed engine change (operator-trust pattern, guardrail 1007).
    //
    // iter 521 adds the recent-actions toolbar: every dispatch — button onClick
    // or toolbar re-fire — routes through DispatchAction(), which enqueues the
    // request AND records it in the render-thread-confined RecentActionsInstance()
    // history (overlay_recent_actions.h). RenderRecentActionsToolbar() draws the
    // last 5 distinct calls as clickable slots; a click re-fires that exact
    // ActionRequest. The history is dedup-promote, so re-firing never grows it.
    //
    // iter 524 adds the Teleport + Faction Switch buttons (spec iter-290). Both
    // target selectedUnitExpr — the single "selected unit" Lua expression the
    // spec's "1 SelectedUnitLuaExpr field shared across Phase 3 widgets" calls
    // for: Find_First_Object of the unit-type combo, computed once and reused by
    // Make Invuln / Teleport / Faction Switch. No new bridge wires — Teleport is
    // iter-151 SWFOC_TeleportUnitLua, Faction Switch is iter-108
    // SWFOC_ChangeUnitOwner, both LIVE.
    //
    // Button gating:
    //   Spawn       — always dispatchable (the faction + unit-type combos
    //                 always hold a valid value; position is free-form).
    //   Make Invuln — always dispatchable; targets Find_First_Object of the
    //                 selected unit type until Phase 5 click-to-select lands.
    //   Teleport    — always dispatchable; sends the selected unit to the
    //                 Position vector (the same field the Spawn button reads).
    //   Faction Sw. — always dispatchable; re-owns the selected unit to the
    //                 Faction combo's player (full engine "swap sides").
    //   Kill        — address-gated: SWFOC_KillUnit takes a numeric object
    //                 address and the overlay has no unit picker before Phase
    //                 5, so the operator types/pastes a hex address (from the
    //                 editor Inspector tab or a CE scan). The button stays
    //                 disabled until the field parses to a non-zero pointer —
    //                 a Kill that targets nothing must not look dispatchable.
    //
    // The pure pieces are already unit-tested elsewhere: the Lua builders in
    // overlay_actions.h (overlay_actions_test.cpp), the FIFO ActionQueue in
    // overlay_action_queue.h (overlay_action_queue_test.cpp), and the drain
    // loop in overlay_action_worker.h (overlay_action_worker_test.cpp). What
    // remains here is ImGui render glue, verified build-only.
    const char* const kActionFactions[] = { "REBEL", "EMPIRE", "UNDERWORLD" };
    const char* const kActionUnitTypes[] = {
        "Rebel_Trooper_Squad",
        "Empire_AT_AT",
        "Empire_Stormtrooper_Squad",
        "Rebel_Plex_Soldier_Squad",
    };
    int g_actionFactionIdx = 0;
    int g_actionUnitTypeIdx = 0;
    float g_actionPos[3] = { 0.0f, 0.0f, 0.0f };
    // Kill-button target address, typed by the operator as raw hex digits
    // (ImGuiInputTextFlags_CharsHexadecimal filters input to 0-9a-fA-F). 24
    // chars holds a full 16-hex-digit 64-bit pointer plus the null terminator.
    char g_actionKillAddr[24] = "";

    // ---- Phase 3 recent-actions history (iter 521) ------------------------
    // The single process-wide recent-actions history backing the toolbar.
    // RecentActions is render-thread-confined (see overlay_recent_actions.h
    // THREADING note) — Record() / At() / Count() are touched ONLY from
    // inside the D3D9 Present detour on the render thread — so a plain
    // function-local static with no synchronization is correct. C++11's
    // thread-safe-static-init guarantee covers the lazy construction even
    // though, in practice, only the render thread ever reaches here.
    swfoc_overlay::RecentActions& RecentActionsInstance()
    {
        static swfoc_overlay::RecentActions instance;
        return instance;
    }

    // ---- Phase 4 minimap spawn-marker ring (iter 530) ---------------------
    // The single process-wide ring of recent spawn-drop world points the
    // tactical minimap plots as dots (overlay_minimap.h). Render-thread-
    // confined exactly like RecentActionsInstance above — Push() / At() /
    // Count() are touched ONLY from inside the D3D9 Present detour on the
    // render thread — so a plain function-local static with no
    // synchronization is correct.
    swfoc_overlay::SpawnMarkerRing& MarkerRingInstance()
    {
        static swfoc_overlay::SpawnMarkerRing instance;
        return instance;
    }

    // Dispatch a Phase 3 action: enqueue it onto the background bridge worker
    // (ActionQueueInstance, drained off the render thread) AND record it in
    // the recent-actions history so the toolbar can re-fire it. Every Phase 3
    // button onClick and the recent-actions toolbar route through here, so the
    // history always mirrors exactly what was sent. Recording an action whose
    // Lua line is already present promotes that slot to the front instead of
    // duplicating it (dedup-promote, overlay_recent_actions.h).
    void DispatchAction(const swfoc_overlay::ActionRequest& req)
    {
        swfoc_overlay::ActionQueueInstance().Enqueue(req);
        RecentActionsInstance().Record(req);
    }

    // Dispatch a Phase 4 drag-drop spawn: enqueue the SWFOC_SpawnUnitLua
    // command for `unitType` under `faction` at the resolved world `drop`, and
    // record the drop point in the minimap marker ring so RenderMinimap plots
    // it as a dot. Shared by the iter-529 spawn pad and the iter-530 minimap —
    // both are drag-drop spawn sources, so routing both through one helper
    // means every drag-spawn (from either widget) shows on the minimap. The
    // helper was extracted at this SECOND drop site, not speculatively at the
    // first (extract-on-second-use).
    void DispatchSpawnDrop(const char* faction, const char* unitType,
                           const swfoc_overlay::SpawnDrop& drop)
    {
        DispatchAction(
            swfoc_overlay::ActionRequest{
                std::string("Drag-spawn ") + unitType,
                swfoc_overlay::BuildSpawnUnitCommand(
                    faction, unitType, drop.x, drop.y, drop.z)});
        MarkerRingInstance().Push(drop);
    }

    // Phase 4 drop-point preview ring (iter 531, spec iter-294): a faction-
    // tinted, gently pulsing ring drawn on the ImGui FOREGROUND draw list at
    // `center` (the cursor) while a unit-type is being dragged over a spawn
    // target. It is a pure overlay hint — no game-side draw — that shows the
    // operator exactly where the unit will land before they release the mouse.
    //
    // `factionIndex` is g_actionFactionIdx — the ring tint follows the current
    // Faction-combo selection (PreviewRingColor, iter-92 LED palette). The
    // pulse phase is folded from the global Present frame counter so the ring
    // breathes at a fixed wall-clock rate regardless of where it is called.
    //
    // The pure pieces are unit-tested in overlay_preview_ring_test.cpp
    // (PreviewRingColor + FramePhase01 + PreviewRingRadius); what remains here
    // is ImGui foreground-draw-list glue, verified build-only.
    void DrawPreviewRing(const ImVec2& center, int factionIndex)
    {
        const float phase = swfoc_overlay::FramePhase01(
            g_frameCount.load(std::memory_order_relaxed),
            swfoc_overlay::kPreviewRingPulseFrames);
        const float radius = swfoc_overlay::PreviewRingRadius(
            swfoc_overlay::kPreviewRingBaseRadius,
            swfoc_overlay::kPreviewRingPulseAmplitude, phase);
        const swfoc_overlay::RingColor c =
            swfoc_overlay::PreviewRingColor(factionIndex);
        const ImU32 col = IM_COL32(c.r, c.g, c.b, c.a);

        // Foreground draw list: rendered last, above every child window's clip
        // rect, so the ring is never clipped by the spawn pad / minimap edge.
        ImDrawList* const dl = ImGui::GetForegroundDrawList();
        dl->AddCircle(center, radius, col, 0, 2.5f);
        // A small filled dot marks the exact drop pixel at the ring's center.
        dl->AddCircleFilled(center, 2.5f, col);
    }

    // Footer toast for RenderActionsWindow: render the most recent ActionQueue
    // outcome with an operator-trust badge (guardrail 1007). PENDING is amber
    // (enqueued, the worker has not confirmed it), LIVE is green (bridge
    // round-trip succeeded), FAILED is red — so a click is never mistaken for
    // a confirmed engine-state change. Called once per frame from
    // RenderActionsWindow; LatestResult() is a short lock-guarded copy.
    void RenderActionToast()
    {
        const swfoc_overlay::ActionResult result =
            swfoc_overlay::ActionQueueInstance().LatestResult();
        switch (result.status)
        {
            case swfoc_overlay::ActionStatus::Idle:
                ImGui::TextDisabled("No action dispatched yet.");
                break;
            case swfoc_overlay::ActionStatus::Pending:
                ImGui::PushStyleColor(ImGuiCol_Text,
                    ImVec4(1.0f, 0.706f, 0.0f, 1.0f));  // amber
                ImGui::Text("PENDING: %s", result.label.c_str());
                ImGui::PopStyleColor();
                break;
            case swfoc_overlay::ActionStatus::Live:
                ImGui::PushStyleColor(ImGuiCol_Text,
                    ImVec4(0.13f, 0.67f, 0.13f, 1.0f));  // green
                ImGui::Text("LIVE: %s", result.label.c_str());
                ImGui::PopStyleColor();
                if (!result.response.empty())
                {
                    ImGui::TextWrapped("  -> %s", result.response.c_str());
                }
                break;
            case swfoc_overlay::ActionStatus::Failed:
                ImGui::PushStyleColor(ImGuiCol_Text,
                    ImVec4(0.87f, 0.20f, 0.13f, 1.0f));  // red
                ImGui::Text("FAILED: %s", result.label.c_str());
                ImGui::PopStyleColor();
                if (!result.response.empty())
                {
                    ImGui::TextWrapped("  -> %s", result.response.c_str());
                }
                break;
        }
    }

    // Recent-actions toolbar for RenderActionsWindow (iter 521): one clickable
    // button per entry in RecentActionsInstance() — most recent first, up to
    // RecentActions::kCapacity (5). Clicking a slot re-fires that exact
    // ActionRequest through DispatchAction(): it is re-enqueued onto the bridge
    // worker AND re-recorded, which promotes the slot to the front (dedup-
    // promote). The toolbar therefore shows up to 5 DISTINCT recent actions and
    // re-firing never grows the list.
    //
    // The click is applied AFTER the draw loop: DispatchAction -> Record()
    // erases + re-inserts inside the very vector At(i) indexes into, so calling
    // it mid-loop would invalidate the iteration. The chosen slot is copied out
    // (At() returns a reference into that vector) and dispatched once the loop
    // has finished drawing.
    void RenderRecentActionsToolbar()
    {
        swfoc_overlay::RecentActions& recent = RecentActionsInstance();
        ImGui::TextDisabled("Recent actions (click to re-fire):");
        if (recent.Empty())
        {
            ImGui::TextDisabled("  (none yet)");
            return;
        }

        bool refire = false;
        swfoc_overlay::ActionRequest chosen;
        const std::size_t count = recent.Count();
        for (std::size_t i = 0; i < count; ++i)
        {
            const swfoc_overlay::ActionRequest& slot = recent.At(i);
            // ImGui widget IDs must be unique; the "##recent<i>" suffix keeps
            // the button ID distinct even when two slots share a label, while
            // only the text before "##" is drawn.
            char btnLabel[96];
            std::snprintf(btnLabel, sizeof(btnLabel), "%s##recent%d",
                          slot.label.c_str(), static_cast<int>(i));
            // One button per line — a vertical recent-list (cf. a recent-files
            // menu). Action labels run long ("Make Invuln Empire_AT_AT"); five
            // on a SameLine() row would balloon this AlwaysAutoResize window
            // far past the Tier strip and breach the "uncluttered HUD"
            // acceptance criterion. Vertical keeps the window ~320 px wide.
            if (ImGui::Button(btnLabel))
            {
                chosen = slot;  // copy out before DispatchAction mutates items_
                refire = true;
            }
        }

        if (refire)
        {
            DispatchAction(chosen);
        }
    }

    // Per-widget capability badge table for RenderActionsWindow (iter 527,
    // Phase 3 close-out). Draws one row per catalogued Phase 3 widget — a
    // colored operator-trust badge, the button label, and the bridge wire it
    // dispatches — straight from the authoritative kPhase3Widgets catalog
    // (overlay_phase3_catalog.h). Replaces the iter-525 hand-maintained
    // "Wires (all LIVE): ..." footer: that string could drift silently from
    // the wires the buttons actually call; this table is fed by the same
    // catalog overlay_phase3_catalog_test.cpp pins, so it cannot.
    //
    // Badge colors match RenderActionToast so the operator reads one palette:
    // LIVE green, PHASE 2 PENDING amber, LIVE ONLY cyan.
    void RenderPhase3CapabilityTable()
    {
        ImGui::TextDisabled("Phase 3 widget capability (guardrail 1007):");
        for (std::size_t i = 0; i < swfoc_overlay::kPhase3WidgetCount; ++i)
        {
            const swfoc_overlay::Phase3Widget& w = swfoc_overlay::kPhase3Widgets[i];

            // Default green (LIVE); the switch overrides for the other two
            // states. Initialized so a never-taken path can't read garbage.
            ImVec4 color(0.13f, 0.67f, 0.13f, 1.0f);
            switch (w.status)
            {
                case swfoc_overlay::WidgetStatus::Live:
                    color = ImVec4(0.13f, 0.67f, 0.13f, 1.0f);  // green
                    break;
                case swfoc_overlay::WidgetStatus::Phase2:
                    color = ImVec4(1.0f, 0.706f, 0.0f, 1.0f);   // amber
                    break;
                case swfoc_overlay::WidgetStatus::LiveOnly:
                    color = ImVec4(0.13f, 0.67f, 0.87f, 1.0f);  // cyan
                    break;
            }

            ImGui::TextColored(color, "  %s",
                swfoc_overlay::WidgetStatusBadge(w.status));
            ImGui::SameLine();
            ImGui::Text("%s", w.label);
            ImGui::SameLine();
            ImGui::TextDisabled("(%s)", w.wire);
        }
    }

    // Phase 4 multi-player-safety badge (iter 532, spec iter-295): draws the
    // spawn-gate status above the Phase 4 drag-drop widgets so the operator
    // always knows whether — and why — a drop will spawn. Green when spawning
    // is LIVE, red when gated; the same operator-trust palette RenderActionToast
    // and RenderPhase3CapabilityTable use, so the operator reads one color
    // language. The decision is the pure overlay_spawn_gate.h kernel
    // (unit-tested in overlay_spawn_gate_test.cpp); this is build-only ImGui
    // glue.
    void RenderSpawnGateBadge(swfoc_overlay::SpawnGateStatus status, int slot)
    {
        const bool allowed = swfoc_overlay::SpawnGateAllowsSpawn(status);
        const ImVec4 color = allowed
            ? ImVec4(0.13f, 0.67f, 0.13f, 1.0f)   // green — spawning enabled
            : ImVec4(0.87f, 0.20f, 0.13f, 1.0f);  // red — spawning gated
        ImGui::TextColored(color, "Spawn gate: %s",
            swfoc_overlay::SpawnGateBadgeText(status));
        ImGui::SameLine();
        ImGui::TextDisabled("(local-player slot %d)", slot);
    }

    // Phase 4 spawn pad (iter 529, spec iter-292): a fixed square child window
    // that is a drag-drop TARGET for the Unit type combo. Dropping a unit-type
    // payload spawns that unit at the world position DropPadToWorld() maps the
    // drop pixel to — the pad center is the world origin, the edges are
    // +/-kSpawnPadHalfExtent (the interim Z=0 plane; the projection-matrix RVA
    // is an honest defer, see overlay_dragdrop.h). `faction` is the current
    // Faction combo selection — the owner the spawned unit is created under.
    //
    // `spawnAllowed` is the iter-532 multi-player safety gate (spec iter-295):
    // when false (no valid tactical local player) the pad still draws so the
    // operator sees the widget, but no drop target is bound — a drop silently
    // does nothing. The red "Tactical mode only" badge above explains why.
    //
    // The pure pieces are unit-tested in overlay_dragdrop_test.cpp
    // (PackUnitTypePayload + DropPadToWorld); what remains here is ImGui
    // drag-drop render glue, verified build-only.
    void RenderSpawnPad(const char* faction, bool spawnAllowed)
    {
        ImGui::TextDisabled("Phase 4 - tactical spawn pad");
        ImGui::TextDisabled("Drag the Unit type combo onto the pad; the drop");
        ImGui::TextDisabled("point maps to a Z=0 world position.");

        ImGui::BeginChild("##spawn_pad",
            ImVec2(swfoc_overlay::kSpawnPadSizePx,
                   swfoc_overlay::kSpawnPadSizePx),
            ImGuiChildFlags_Borders);
        ImGui::TextWrapped("Drop here to spawn (Z=0)");
        ImGui::TextDisabled("center = world origin");
        ImGui::TextDisabled("edge = +/-%.0f world units",
            static_cast<double>(swfoc_overlay::kSpawnPadHalfExtent));
        ImGui::EndChild();

        // iter-532, spec iter-295: multi-player safety. The drop target is
        // bound ONLY when the spawn gate is open (a valid tactical local
        // player, slot 0..7). When it is closed — galactic transition, no
        // game — the pad has drawn above but no drop target exists, so a drop
        // silently does nothing. Returning here is defense in depth: the
        // Unit-type drag SOURCE is already disarmed in RenderActionsWindow
        // when the gate is closed, so no drag can even reach this pad.
        if (!spawnAllowed)
        {
            return;
        }

        // After EndChild() the child window is the last-submitted item:
        // GetItemRectMin() gives its screen-space top-left and
        // BeginDragDropTarget() binds the drop target to it.
        const ImVec2 padMin = ImGui::GetItemRectMin();
        if (ImGui::BeginDragDropTarget())
        {
            // ImGuiDragDropFlags_AcceptBeforeDelivery: the payload is returned
            // while the operator is still HOVERING over the pad, before the
            // mouse button is released. That lets us draw a live preview ring
            // at the drop point every hover frame; the actual spawn fires only
            // on the delivery frame (payload->IsDelivery()), so a hover never
            // spawns a unit (iter 531, spec iter-294).
            const ImGuiPayload* payload = ImGui::AcceptDragDropPayload(
                swfoc_overlay::kUnitTypePayloadId,
                ImGuiDragDropFlags_AcceptBeforeDelivery);
            // Accept only a payload of the exact fixed size the drag source
            // sent (kUnitTypePayloadCapacity) — a defensive guard so a
            // malformed or foreign payload is never read as a unit name. The
            // drag source packs the name with PackUnitTypePayload, which
            // always null-terminates, so Data is a valid C-string here.
            if (payload != nullptr && payload->Data != nullptr &&
                payload->DataSize ==
                    static_cast<int>(swfoc_overlay::kUnitTypePayloadCapacity))
            {
                const ImVec2 mouse = ImGui::GetMousePos();
                if (payload->IsDelivery())
                {
                    // Delivery frame: resolve the drop pixel to a world point
                    // and spawn. Routes through DispatchSpawnDrop so the pad's
                    // drops also appear as dots on the iter-530 minimap.
                    const char* droppedType =
                        static_cast<const char*>(payload->Data);
                    const swfoc_overlay::SpawnDrop world =
                        swfoc_overlay::DropPadToWorld(
                            mouse.x - padMin.x, mouse.y - padMin.y,
                            swfoc_overlay::kSpawnPadSizePx,
                            swfoc_overlay::kSpawnPadHalfExtent);
                    DispatchSpawnDrop(faction, droppedType, world);
                }
                else
                {
                    // Hover frame: faction-tinted preview ring at the cursor.
                    DrawPreviewRing(mouse, g_actionFactionIdx);
                }
            }
            ImGui::EndDragDropTarget();
        }
    }

    // Phase 4 tactical minimap (iter 530, spec iter-293): a 256x256 top-down
    // child window that is BOTH a richer drag-drop spawn target and a live
    // plot of the operator's recent spawn drops. Dropping a unit-type payload
    // spawns that unit at the world position MinimapToWorld() maps the drop
    // pixel to; every drag-spawn (from here or the iter-529 spawn pad) is
    // recorded in MarkerRingInstance() and drawn here as a dot via
    // WorldToMinimap() — green when on-map, amber when clamped to an edge.
    //
    // HONEST DEFER (overlay_minimap.h): live ENGINE-unit dots need HudSnapshot
    // per-unit positions (spec iter-302, Phase 5) and a pinned heightmap RVA.
    // Until then the dots plot the operator's OWN spawns — real data the
    // overlay already has. The map is a flat 2D grid, not a textured quad
    // (the spec sketched ImGui::Image, but with no heightmap there is no map
    // texture to show — a draw-list grid is the working interim idiom).
    //
    // `spawnAllowed` is the iter-532 multi-player safety gate (spec iter-295):
    // when false the minimap still draws — including the operator's recent-
    // spawn dots, which are history, not a live action — but no drop target is
    // bound, so a drop fires no new spawn. See RenderSpawnPad.
    //
    // The pure pieces are unit-tested in overlay_minimap_test.cpp
    // (WorldToMinimap + MinimapToWorld + SpawnMarkerRing); what remains here
    // is ImGui draw-list + drag-drop render glue, verified build-only.
    void RenderMinimap(const char* faction, bool spawnAllowed)
    {
        ImGui::TextDisabled("Phase 4 - tactical minimap");
        ImGui::TextDisabled("Drag the Unit type combo onto the map to spawn.");
        ImGui::TextDisabled("Dots = your recent spawn drops");
        ImGui::TextDisabled("(green on-map, amber clamped to an edge).");

        ImGui::BeginChild("##minimap",
            ImVec2(swfoc_overlay::kMinimapSizePx,
                   swfoc_overlay::kMinimapSizePx),
            ImGuiChildFlags_Borders);

        // Draw into the child's own draw list. GetWindowPos() inside the child
        // is its screen-space top-left — the same point GetItemRectMin()
        // returns after EndChild() — so the drawn dots and the drop-coordinate
        // map share one origin.
        ImDrawList* const dl = ImGui::GetWindowDrawList();
        const ImVec2 mapMin = ImGui::GetWindowPos();
        const float size = swfoc_overlay::kMinimapSizePx;
        const ImVec2 mapMax(mapMin.x + size, mapMin.y + size);

        // Background fill + a crosshair through the center (world origin).
        dl->AddRectFilled(mapMin, mapMax, IM_COL32(18, 22, 28, 255));
        const float midX = mapMin.x + size * 0.5f;
        const float midY = mapMin.y + size * 0.5f;
        dl->AddLine(ImVec2(mapMin.x, midY), ImVec2(mapMax.x, midY),
                    IM_COL32(64, 72, 84, 255));
        dl->AddLine(ImVec2(midX, mapMin.y), ImVec2(midX, mapMax.y),
                    IM_COL32(64, 72, 84, 255));

        // One dot per retained spawn marker, projected through WorldToMinimap.
        swfoc_overlay::SpawnMarkerRing& ring = MarkerRingInstance();
        for (std::size_t i = 0; i < ring.Count(); ++i)
        {
            const swfoc_overlay::SpawnDrop& m = ring.At(i);
            const swfoc_overlay::MinimapPoint p =
                swfoc_overlay::WorldToMinimap(
                    m.x, m.y, size, swfoc_overlay::kMinimapHalfExtent);
            const ImU32 col = p.onMap
                ? IM_COL32(60, 200, 90, 255)    // green: within the extent
                : IM_COL32(214, 150, 48, 255);  // amber: clamped to an edge
            dl->AddCircleFilled(
                ImVec2(mapMin.x + p.px, mapMin.y + p.py), 3.5f, col);
        }

        ImGui::EndChild();

        // iter-532, spec iter-295: gate the drop target on the spawn gate —
        // see RenderSpawnPad. The recent-spawn dots drawn above still appear
        // when the gate is closed (they are history); only the drop target
        // that would fire a NEW spawn is withheld.
        if (!spawnAllowed)
        {
            return;
        }

        // After EndChild() the child window is the last-submitted item;
        // GetItemRectMin() gives its screen top-left (== mapMin above) and
        // BeginDragDropTarget() binds the drop target to it.
        const ImVec2 dropMin = ImGui::GetItemRectMin();
        if (ImGui::BeginDragDropTarget())
        {
            // AcceptBeforeDelivery: peek the payload during the hover so the
            // preview ring (iter 531) can track the cursor; the spawn fires
            // only on the delivery frame — see RenderSpawnPad for the rationale.
            const ImGuiPayload* payload = ImGui::AcceptDragDropPayload(
                swfoc_overlay::kUnitTypePayloadId,
                ImGuiDragDropFlags_AcceptBeforeDelivery);
            // Accept only a payload of the exact fixed size the drag source
            // sent — the same defensive guard RenderSpawnPad uses.
            if (payload != nullptr && payload->Data != nullptr &&
                payload->DataSize ==
                    static_cast<int>(swfoc_overlay::kUnitTypePayloadCapacity))
            {
                const ImVec2 mouse = ImGui::GetMousePos();
                if (payload->IsDelivery())
                {
                    const char* droppedType =
                        static_cast<const char*>(payload->Data);
                    const swfoc_overlay::SpawnDrop world =
                        swfoc_overlay::MinimapToWorld(
                            mouse.x - dropMin.x, mouse.y - dropMin.y,
                            size, swfoc_overlay::kMinimapHalfExtent);
                    DispatchSpawnDrop(faction, droppedType, world);
                }
                else
                {
                    // Hover frame: faction-tinted preview ring at the cursor.
                    DrawPreviewRing(mouse, g_actionFactionIdx);
                }
            }
            ImGui::EndDragDropTarget();
        }
    }

    void RenderActionsWindow()
    {
        if (!g_imguiInitialized.load(std::memory_order_acquire)) return;

        constexpr float kMargin = 12.0f;
        ImGui::SetNextWindowPos(ImVec2(kMargin, kMargin), ImGuiCond_FirstUseEver);
        ImGui::SetNextWindowSize(ImVec2(320.0f, 0.0f), ImGuiCond_FirstUseEver);
        ImGui::SetNextWindowBgAlpha(0.78f);

        constexpr ImGuiWindowFlags kFlags =
            ImGuiWindowFlags_NoSavedSettings |
            ImGuiWindowFlags_NoFocusOnAppearing |
            ImGuiWindowFlags_AlwaysAutoResize;

        if (ImGui::Begin("SWFOC Actions (Phase 3)", nullptr, kFlags))
        {
            ImGui::TextDisabled("Phase 3 - action buttons LIVE");
            ImGui::TextDisabled("Clicks enqueue onto the bridge worker; "
                                "the footer shows the result.");
            ImGui::Separator();

            // ----- Phase 4 multi-player safety gate (iter 532, spec iter-295)
            // A drag-drop spawn must only fire when the overlay is looking at
            // a valid TACTICAL local player. SWFOC_GetLocalPlayer — polled
            // into HudSnapshot::local_player_slot by the HUD worker — is 0..7
            // in a tactical battle and -1 during a galactic-mode transition
            // or with no game attached; spawning "under" a -1 owner is
            // undefined. The gate is evaluated once here and threaded through
            // the whole Phase 4 surface: when it is closed the Unit-type drag
            // SOURCE below is not armed, and neither RenderSpawnPad nor
            // RenderMinimap binds a drop target. The red badge in the Phase 4
            // section explains why. Mirrors the iter-120 LiveSkip pattern:
            // gated, not errored. (overlay_spawn_gate.h is the pure kernel.)
            const swfoc_overlay::HudSnapshot spawnGateSnap =
                swfoc_overlay::GetHudSnapshot();
            const swfoc_overlay::SpawnGateStatus spawnGate =
                swfoc_overlay::EvaluateSpawnGate(
                    spawnGateSnap.local_player_slot);
            const bool spawnAllowed =
                swfoc_overlay::SpawnGateAllowsSpawn(spawnGate);

            ImGui::Combo("Faction", &g_actionFactionIdx,
                kActionFactions, IM_ARRAYSIZE(kActionFactions));
            ImGui::Combo("Unit type", &g_actionUnitTypeIdx,
                kActionUnitTypes, IM_ARRAYSIZE(kActionUnitTypes));
            // ----- Phase 4 (iter 529): the Unit type combo is a drag-drop
            // SOURCE. Drag it onto the spawn pad below to place the selected
            // unit at the mapped Z=0 world point. The unit-type name travels
            // in the payload DATA under the fixed type-id kUnitTypePayloadId
            // (the spawn pad accepts any unit, so the type-id cannot be
            // per-name — see overlay_dragdrop.h). BeginDragDropSource() must
            // follow the combo immediately so it binds to the combo item. ----
            // iter-532 (spec iter-295): the drag source is armed ONLY when the
            // multi-player safety gate is open. && short-circuits, so when the
            // gate is closed BeginDragDropSource is never called and the
            // operator cannot even start a unit-type drag.
            if (spawnAllowed &&
                ImGui::BeginDragDropSource(ImGuiDragDropFlags_None))
            {
                char dragPayload[swfoc_overlay::kUnitTypePayloadCapacity];
                if (swfoc_overlay::PackUnitTypePayload(
                        kActionUnitTypes[g_actionUnitTypeIdx],
                        dragPayload, sizeof(dragPayload)))
                {
                    ImGui::SetDragDropPayload(
                        swfoc_overlay::kUnitTypePayloadId,
                        dragPayload, sizeof(dragPayload));
                }
                ImGui::Text("Spawn: %s",
                    kActionUnitTypes[g_actionUnitTypeIdx]);
                ImGui::EndDragDropSource();
            }
            ImGui::InputFloat3("Position", g_actionPos);
            ImGui::InputText("Kill addr (hex)", g_actionKillAddr,
                IM_ARRAYSIZE(g_actionKillAddr),
                ImGuiInputTextFlags_CharsHexadecimal);

            // ImGui::Combo clamps its index to the valid array range, so
            // every kActionFactions / kActionUnitTypes read below is
            // in-bounds.
            const char* const faction = kActionFactions[g_actionFactionIdx];
            const char* const unitType = kActionUnitTypes[g_actionUnitTypeIdx];

            // Shared "selected unit" Lua expression — the handle every
            // per-unit Phase 3 widget (Make Invuln / Teleport / Faction
            // Switch) targets. Find_First_Object resolves the first live
            // instance of the combo's unit type; Phase 5 click-to-select
            // promotes this to an inspected handle. Computed once, reused —
            // the spec's "1 SelectedUnitLuaExpr field shared across Phase 3
            // widgets" (overlay-interactive.md iter-290).
            const std::string selectedUnitExpr =
                std::string("Find_First_Object(\"") + unitType + "\")";

            // ----- Spawn: LIVE, always dispatchable -----
            if (ImGui::Button("Spawn"))
            {
                DispatchAction(
                    swfoc_overlay::ActionRequest{
                        std::string("Spawn ") + unitType,
                        swfoc_overlay::BuildSpawnUnitCommand(
                            faction, unitType,
                            g_actionPos[0], g_actionPos[1], g_actionPos[2])});
            }
            ImGui::SameLine();

            // ----- Make Invuln: LIVE, targets the first object of the
            // selected unit type. Phase 5 click-to-select promotes this to
            // an inspected unit handle. -----
            if (ImGui::Button("Make Invuln"))
            {
                DispatchAction(
                    swfoc_overlay::ActionRequest{
                        std::string("Make Invuln ") + unitType,
                        swfoc_overlay::BuildMakeUnitInvulnCommand(
                            selectedUnitExpr, true)});
            }
            ImGui::SameLine();

            // ----- Kill: LIVE but address-gated. A Kill that targets
            // nothing must not look dispatchable (operator-trust pattern,
            // guardrail 1007), so the button is disabled until the hex
            // field parses to a non-zero pointer. -----
            const unsigned long long killAddr =
                std::strtoull(g_actionKillAddr, nullptr, 16);
            ImGui::BeginDisabled(killAddr == 0ull);
            if (ImGui::Button("Kill"))
            {
                char killLabel[32];
                std::snprintf(killLabel, sizeof(killLabel),
                    "Kill 0x%llX", killAddr);
                DispatchAction(
                    swfoc_overlay::ActionRequest{
                        killLabel,
                        swfoc_overlay::BuildKillUnitCommand(killAddr)});
            }
            ImGui::EndDisabled();

            // ----- Teleport + Faction Switch: LIVE, always dispatchable.
            // Both target selectedUnitExpr; neither is gated because the
            // unit-type and faction combos always hold a valid value.
            // Drawn on their own SameLine row so the Actions window stays
            // narrow (cf. RenderRecentActionsToolbar's width note). -----
            if (ImGui::Button("Teleport"))
            {
                DispatchAction(
                    swfoc_overlay::ActionRequest{
                        std::string("Teleport ") + unitType,
                        swfoc_overlay::BuildTeleportUnitCommand(
                            selectedUnitExpr,
                            g_actionPos[0], g_actionPos[1], g_actionPos[2])});
            }
            ImGui::SameLine();

            if (ImGui::Button("Faction Switch"))
            {
                DispatchAction(
                    swfoc_overlay::ActionRequest{
                        std::string("Faction Switch ") + unitType +
                            " -> " + faction,
                        swfoc_overlay::BuildChangeUnitOwnerCommand(
                            selectedUnitExpr, faction)});
            }

            // Live command preview - exercises overlay_actions.h in the
            // real render path; mirrors the exact Lua the Spawn button
            // enqueues.
            ImGui::Separator();
            const std::string preview = swfoc_overlay::BuildSpawnUnitCommand(
                faction, unitType,
                g_actionPos[0], g_actionPos[1], g_actionPos[2]);
            ImGui::TextDisabled("Spawn button will send:");
            ImGui::TextWrapped("%s", preview.c_str());

            // ----- Recent-actions toolbar: re-fire the last 5 calls --------
            ImGui::Separator();
            RenderRecentActionsToolbar();

            // ----- Footer toast: latest action outcome (guardrail 1007) ----
            ImGui::Separator();
            RenderActionToast();

            // ----- Per-widget capability badges (iter 527, Phase 3 close-out)
            // Replaces the iter-525 static "Wires (all LIVE)" footer with a
            // catalog-driven badge row per Phase 3 widget — overlay-trust
            // pattern, single source of truth (overlay_phase3_catalog.h).
            ImGui::Separator();
            RenderPhase3CapabilityTable();

            // ----- Phase 4 multi-player safety badge (iter 532, iter-295) --
            // The spawn-gate status, drawn once above both Phase 4 widgets so
            // the operator reads it before reaching for a drag.
            ImGui::Separator();
            RenderSpawnGateBadge(spawnGate, spawnGateSnap.local_player_slot);

            // ----- Phase 4 drag-drop spawn pad (iter 529) ------------------
            // Lands below the Phase 3 content — Phase 4+ widgets go under the
            // earlier phases, not into the always-visible Tier strip. The
            // iter-532 gate (spawnAllowed) withholds the drop target when
            // there is no valid tactical local player.
            ImGui::Separator();
            RenderSpawnPad(faction, spawnAllowed);

            // ----- Phase 4 tactical minimap (iter 530) ---------------------
            // The richer drag-drop target: drop spawns here too, and every
            // drag-spawn (pad or minimap) shows as a dot on this map. Gated by
            // the iter-532 spawnAllowed flag exactly like the spawn pad.
            ImGui::Separator();
            RenderMinimap(faction, spawnAllowed);
        }
        ImGui::End();
    }
}

namespace swfoc_overlay
{
    bool IsVisible()
    {
        return g_visible.load(std::memory_order_relaxed);
    }

    void SetVisible(bool v)
    {
        g_visible.store(v, std::memory_order_relaxed);
    }

    void ToggleVisible()
    {
        bool expected = g_visible.load();
        while (!g_visible.compare_exchange_weak(expected, !expected)) { /* retry */ }
    }

    void Install()
    {
        if (MH_Initialize() != MH_OK)
        {
            OutputDebugStringA("[swfoc_overlay] MH_Initialize failed\n");
            return;
        }

        void* presentSlot = HarvestVtableSlot(kSlotPresent);
        void* resetSlot = HarvestVtableSlot(kSlotReset);
        if (!presentSlot || !resetSlot)
        {
            OutputDebugStringA("[swfoc_overlay] vtable harvest failed\n");
            return;
        }

        if (MH_CreateHook(presentSlot,
                reinterpret_cast<LPVOID>(&HookedPresent),
                reinterpret_cast<LPVOID*>(&g_origPresent)) != MH_OK)
        {
            OutputDebugStringA("[swfoc_overlay] MH_CreateHook(Present) failed\n");
            return;
        }
        if (MH_CreateHook(resetSlot,
                reinterpret_cast<LPVOID>(&HookedReset),
                reinterpret_cast<LPVOID*>(&g_origReset)) != MH_OK)
        {
            OutputDebugStringA("[swfoc_overlay] MH_CreateHook(Reset) failed\n");
            return;
        }
        if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK)
        {
            OutputDebugStringA("[swfoc_overlay] MH_EnableHook(ALL) failed\n");
            return;
        }

        g_hotkeyShutdown.store(false);
        g_hotkeyThread = CreateThread(nullptr, 0, HotkeyPollLoop, nullptr, 0, nullptr);

        // Phase 2: spawn the bridge-poll worker so HUD bars reflect
        // live bridge state rather than just the toggle.
        StartHudWorker();

        // Phase 3 (iter 516): spawn the action-worker that drains the
        // ActionQueue off the render thread. The Phase 3 buttons enqueue
        // onto it; this thread performs the blocking bridge round-trip.
        StartActionWorker();

        OutputDebugStringA("[swfoc_overlay] Install OK — F1 toggles visibility\n");
    }

    void Uninstall()
    {
        // Stop both bridge worker threads before tearing down hooks so any
        // in-flight pipe round-trip completes / fails cleanly. Reverse of
        // Install's start order (HUD then action): action then HUD.
        // iter 516: StopActionWorker joins the Phase 3 drain thread.
        StopActionWorker();
        StopHudWorker();

        // iter 277: shut down ImGui before unhooking Present/Reset so the
        // backends release their device-bound resources while the device
        // is still valid. Order is intentional: workers → ImGui → hooks.
        ShutdownImGui();

        g_hotkeyShutdown.store(true);
        if (g_hotkeyThread)
        {
            WaitForSingleObject(g_hotkeyThread, 1000);
            CloseHandle(g_hotkeyThread);
            g_hotkeyThread = nullptr;
        }
        MH_DisableHook(MH_ALL_HOOKS);
        MH_Uninitialize();
        OutputDebugStringA("[swfoc_overlay] Uninstall OK\n");
    }
}
