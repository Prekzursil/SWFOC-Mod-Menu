@echo off
REM ============================================================================
REM build_phase4_close_test.bat - compile + run the Phase 4 close-out
REM integration test (Phase 4 close-out, iter 533 / spec iter-296).
REM
REM overlay_phase4_close_test.cpp is a pure INTEGRATION test: it wires the four
REM Phase 4 kernels (overlay_dragdrop.h + overlay_minimap.h +
REM overlay_preview_ring.h + overlay_spawn_gate.h) together with the spawn
REM builder overlay_actions.h and exercises the complete drag-drop spawn
REM pipeline. All five headers are header-only and std-only
REM (<string>/<cstdio>/<cstring>/<cstddef>). No Windows, no ImGui, no bridge,
REM no <thread>. Needs no game and no pipe. Reuses the MinGW g++ that build.bat
REM uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 533): mirrors build_spawn_gate_test.bat (iter 532) -
REM full compiler path via `where`, cwd pinned to this script's folder, test
REM exe run by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay Phase 4 close-out integration test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === PHASE4-CLOSE TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_phase4_close_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_phase4_close_test.cpp -o overlay_phase4_close_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_phase4_close_test.exe...
echo.
".\overlay_phase4_close_test.exe"
if errorlevel 1 goto testfail

echo.
echo === PHASE4-CLOSE TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === PHASE4-CLOSE TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === PHASE4-CLOSE TEST: FAILURES ===
exit /b 1

:end
