@echo off
echo === SWFOC Overlay (Phase 1) Build ===
echo.

REM 2026-05-21 (iter 512): resolve the FULL compiler path. The toolchain
REM now lives under "C:\Program Files\CodeBlocks\MinGW\bin" — a bare
REM `x86_64-w64-mingw32-g++` invocation works for --version but fails
REM sub-tool (cc1plus/as) spawning when the install dir contains a space.
REM Invoking g++ by its full path makes gcc locate its sub-tools correctly.
set "GCC="
set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-gcc 2^>nul') do if not defined GCC set "GCC=%%i"
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GCC echo === OVERLAY BUILD FAILED: x86_64-w64-mingw32-gcc not on PATH === & exit /b 1
if not defined GPP echo === OVERLAY BUILD FAILED: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1
set CFLAGS=-O2 -DWIN32_LEAN_AND_MEAN
set CPPFLAGS=-O2 -std=c++17 -DWIN32_LEAN_AND_MEAN -I. -I../swfoc_lua_bridge/minhook/include -I../swfoc_lua_bridge -Iimgui -Iimgui/backends

REM Reuse the bridge's MinHook source. We compile our own .o files so the
REM overlay DLL is fully self-contained at link time.
set MH_SRC=../swfoc_lua_bridge/minhook/src

echo [1/4] Compiling MinHook (shared with swfoc_lua_bridge)...
"%GCC%" -c %CFLAGS% -I%MH_SRC% -I../swfoc_lua_bridge/minhook/include %MH_SRC%/hook.c -o hook.o
"%GCC%" -c %CFLAGS% %MH_SRC%/buffer.c -o buffer.o
"%GCC%" -c %CFLAGS% %MH_SRC%/trampoline.c -o trampoline.o
"%GCC%" -c %CFLAGS% %MH_SRC%/hde/hde64.c -o hde64.o
if errorlevel 1 goto fail

echo [2/4] Compiling overlay sources...
"%GPP%" -c %CPPFLAGS% overlay.cpp -o overlay.o
"%GPP%" -c %CPPFLAGS% dllmain.cpp -o dllmain.o
"%GPP%" -c %CPPFLAGS% hud_state.cpp -o hud_state.o
REM iter 516: Phase 3 action-worker lifecycle (drain thread + ActionQueue).
"%GPP%" -c %CPPFLAGS% overlay_action_worker.cpp -o overlay_action_worker.o
if errorlevel 1 goto fail

echo [3/4] Compiling ImGui v1.91.5 (vendored Phase 2-full)...
REM Iter 276: vendored Dear ImGui core + DX9 backend + Win32 backend.
REM See knowledge-base/iter276_overlay_imgui_vendoring.md for the design doc.
REM Compile units (6): core 4 + backends 2. Linked statically into swfoc_overlay.dll.
"%GPP%" -c %CPPFLAGS% imgui/imgui.cpp -o imgui_imgui.o
"%GPP%" -c %CPPFLAGS% imgui/imgui_draw.cpp -o imgui_draw.o
"%GPP%" -c %CPPFLAGS% imgui/imgui_widgets.cpp -o imgui_widgets.o
"%GPP%" -c %CPPFLAGS% imgui/imgui_tables.cpp -o imgui_tables.o
"%GPP%" -c %CPPFLAGS% imgui/backends/imgui_impl_dx9.cpp -o imgui_impl_dx9.o
"%GPP%" -c %CPPFLAGS% imgui/backends/imgui_impl_win32.cpp -o imgui_impl_win32.o
if errorlevel 1 goto fail

echo [4/4] Linking swfoc_overlay.dll...
REM -ld3d9 brings in Direct3DCreate9 + the IDirect3D9 vtable.
REM -lkernel32 -luser32 cover GetAsyncKeyState (hotkey), CreateFile/ReadFile/WriteFile (pipe).
REM -limm32 needed by imgui_impl_win32 for IME (input method editor) support.
REM -ldwmapi needed by imgui_impl_win32 for DwmIsCompositionEnabled.
REM -lgdi32 needed by imgui_impl_win32 for GetDeviceCaps / CreateRectRgn / DeleteObject (high-DPI scaling + IME composition rgn).
"%GPP%" -shared -o swfoc_overlay.dll dllmain.o overlay.o hud_state.o overlay_action_worker.o hook.o buffer.o trampoline.o hde64.o imgui_imgui.o imgui_draw.o imgui_widgets.o imgui_tables.o imgui_impl_dx9.o imgui_impl_win32.o -lkernel32 -luser32 -ld3d9 -limm32 -ldwmapi -lgdi32 -static -s
if errorlevel 1 goto fail

echo.
echo === OVERLAY BUILD SUCCESS ===
dir swfoc_overlay.dll
echo.
echo Phase 1 install: pick a DLL name the game imports + isn't already
echo claimed by powrprof.dll (the bridge). Candidates: dwmapi.dll,
echo dxva2.dll, version.dll. Rename swfoc_overlay.dll → ^<chosen-name^>.dll
echo and place next to StarWarsG.exe.
echo.
echo Phase 1 verification: launch SWFOC, alt-tab into a debug viewer
echo (DebugView, Sysinternals), look for "[swfoc_overlay] Present
echo frame=N" lines proving the D3D9 detour is firing.
echo.
echo Phase 1 ends here. Phase 2 vendors ImGui + draws an actual panel.
goto end

:fail
echo.
echo === OVERLAY BUILD FAILED ===

:end
