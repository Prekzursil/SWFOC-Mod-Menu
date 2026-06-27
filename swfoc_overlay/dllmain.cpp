// =============================================================================
// swfoc_overlay — in-game overlay DLL for SWFOC
// 2026-04-27 (iter 31, Phase 1) — skeleton: load, detour D3D9::Present, toggle
// visibility on F1. ImGui rendering deferred to Phase 2.
//
// Loaded next to StarWarsG.exe under a shim DLL name the OS picks up via the
// standard search order. We piggyback on the same trick the existing
// powrprof.dll bridge uses, but with a different exe-imported DLL name so
// both DLLs can coexist. Candidates: dwmapi.dll, version.dll, dxva2.dll.
// Final choice deferred to Phase 1 install testing.
// =============================================================================

#include <windows.h>
#include "overlay.h"

namespace
{
    HMODULE g_self = nullptr;
    DWORD WINAPI BootstrapThread(LPVOID)
    {
        // Allow the host process to fully initialize D3D9 before we hook.
        // 1500 ms is a conservative cushion; the bridge DLL uses similar.
        Sleep(1500);
        swfoc_overlay::Install();
        return 0;
    }
}

extern "C" __declspec(dllexport) BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        g_self = inst;
        DisableThreadLibraryCalls(inst);
        // Spin up the bootstrap on a worker thread so we don't block
        // LoadLibrary inside the host's loader lock.
        if (HANDLE h = CreateThread(nullptr, 0, BootstrapThread, nullptr, 0, nullptr))
        {
            CloseHandle(h);
        }
        break;
    case DLL_PROCESS_DETACH:
        swfoc_overlay::Uninstall();
        break;
    }
    return TRUE;
}
