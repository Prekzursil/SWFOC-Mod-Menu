// powrprof.dll proxy — forwards real exports to system DLL
// Drop this DLL in the game's /corruption folder. Windows loads it
// before the system powrprof.dll due to DLL search order.

#include <windows.h>
#include <cstdio>

static HMODULE g_realDll = nullptr;

// The 6 exports that StarWarsG.exe imports from powrprof.dll
// We forward them to the real system DLL.

extern "C" {

LONG WINAPI Proxy_CallNtPowerInformation(ULONG a, PVOID b, ULONG c, PVOID d, ULONG e) {
    typedef LONG(WINAPI* fn_t)(ULONG, PVOID, ULONG, PVOID, ULONG);
    static auto real = (fn_t)GetProcAddress(g_realDll, "CallNtPowerInformation");
    return real ? real(a, b, c, d, e) : 0;
}

DWORD WINAPI Proxy_PowerReadACValueIndex(HKEY a, const GUID* b, const GUID* c, const GUID* d, DWORD* e) {
    typedef DWORD(WINAPI* fn_t)(HKEY, const GUID*, const GUID*, const GUID*, DWORD*);
    static auto real = (fn_t)GetProcAddress(g_realDll, "PowerReadACValueIndex");
    return real ? real(a, b, c, d, e) : 1;
}

DWORD WINAPI Proxy_PowerReadDCValueIndex(HKEY a, const GUID* b, const GUID* c, const GUID* d, DWORD* e) {
    typedef DWORD(WINAPI* fn_t)(HKEY, const GUID*, const GUID*, const GUID*, DWORD*);
    static auto real = (fn_t)GetProcAddress(g_realDll, "PowerReadDCValueIndex");
    return real ? real(a, b, c, d, e) : 1;
}

DWORD WINAPI Proxy_PowerGetActiveScheme(HKEY a, GUID** b) {
    typedef DWORD(WINAPI* fn_t)(HKEY, GUID**);
    static auto real = (fn_t)GetProcAddress(g_realDll, "PowerGetActiveScheme");
    return real ? real(a, b) : 1;
}

DWORD WINAPI Proxy_PowerSettingRegisterNotification(const GUID* a, DWORD b, HANDLE c, PHPOWERNOTIFY d) {
    typedef DWORD(WINAPI* fn_t)(const GUID*, DWORD, HANDLE, PHPOWERNOTIFY);
    static auto real = (fn_t)GetProcAddress(g_realDll, "PowerSettingRegisterNotification");
    return real ? real(a, b, c, d) : 1;
}

DWORD WINAPI Proxy_PowerSettingUnregisterNotification(HPOWERNOTIFY a) {
    typedef DWORD(WINAPI* fn_t)(HPOWERNOTIFY);
    static auto real = (fn_t)GetProcAddress(g_realDll, "PowerSettingUnregisterNotification");
    return real ? real(a) : 1;
}

} // extern "C"

bool Proxy_Init() {
    char systemDir[MAX_PATH];
    GetSystemDirectoryA(systemDir, MAX_PATH);
    char dllPath[MAX_PATH];
    snprintf(dllPath, MAX_PATH, "%s\\powrprof.dll", systemDir);
    g_realDll = LoadLibraryA(dllPath);
    return g_realDll != nullptr;
}

void Proxy_Shutdown() {
    if (g_realDll) {
        FreeLibrary(g_realDll);
        g_realDll = nullptr;
    }
}
