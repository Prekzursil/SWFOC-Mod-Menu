@echo off
REM ============================================================================
REM build_spawn_gate_test.bat — compile + run the overlay_spawn_gate.h test
REM (Phase 4 cont., iter 532 / spec iter-295).
REM
REM overlay_spawn_gate.h is header-only and std-only (no includes at all —
REM plain int / enum / const char* logic). The test pulls in only <cstdio> and
REM <cstring>. No Windows, no ImGui, no bridge, no <thread>. Needs no game and
REM no pipe. Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 532): mirrors build_preview_ring_test.bat (iter 531) —
REM full compiler path via `where`, cwd pinned to this script's folder, test
REM exe run by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay spawn-gate kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === SPAWN-GATE TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_spawn_gate_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_spawn_gate_test.cpp -o overlay_spawn_gate_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_spawn_gate_test.exe...
echo.
".\overlay_spawn_gate_test.exe"
if errorlevel 1 goto testfail

echo.
echo === SPAWN-GATE TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === SPAWN-GATE TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === SPAWN-GATE TEST: FAILURES ===
exit /b 1

:end
