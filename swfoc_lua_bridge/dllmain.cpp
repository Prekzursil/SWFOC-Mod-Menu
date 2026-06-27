// DLL entry point — initializes proxy forwarding + Lua bridge
// Includes crash handler to log exceptions before game terminates.

#include <windows.h>
#include <cstdio>

extern bool Proxy_Init();
extern void Proxy_Shutdown();
extern bool LuaBridge_Init();
extern void LuaBridge_Shutdown();

static LONG WINAPI CrashHandler(EXCEPTION_POINTERS* ex) {
    FILE* f = fopen("swfoc_bridge_crash.log", "a");
    if (f) {
        DWORD code = ex->ExceptionRecord->ExceptionCode;
        auto addr = (uintptr_t)ex->ExceptionRecord->ExceptionAddress;
        auto base = (uintptr_t)GetModuleHandleA(nullptr);
        auto rip = ex->ContextRecord->Rip;
        auto rsp = ex->ContextRecord->Rsp;
        auto rcx = ex->ContextRecord->Rcx;
        auto rdx = ex->ContextRecord->Rdx;
        auto r8  = ex->ContextRecord->R8;

        fprintf(f, "=== SWFOC BRIDGE CRASH ===\n");
        fprintf(f, "Exception: 0x%08lX at 0x%llX (RVA 0x%llX)\n", code, addr, addr - base);
        fprintf(f, "RIP=0x%llX (RVA 0x%llX)\n", rip, rip - base);
        fprintf(f, "RSP=0x%llX RCX=0x%llX RDX=0x%llX R8=0x%llX\n", rsp, rcx, rdx, r8);
        fprintf(f, "Module base=0x%llX\n", base);
        fprintf(f, "=============================\n\n");
        fclose(f);
    }
    return EXCEPTION_CONTINUE_SEARCH; // let the game's own handler run too
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID reserved) {
    switch (reason) {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        SetUnhandledExceptionFilter(CrashHandler);
        if (!Proxy_Init()) return FALSE;
        LuaBridge_Init();
        break;
    case DLL_PROCESS_DETACH:
        LuaBridge_Shutdown();
        Proxy_Shutdown();
        break;
    }
    return TRUE;
}
