@echo off
REM ============================================================================
REM build_minimap_test.bat — compile + run the overlay_minimap.h test
REM (Phase 4 cont., iter 530 / spec iter-293).
REM
REM overlay_minimap.h is header-only and std-only (<cstddef>); it #includes
REM overlay_dragdrop.h (SpawnDrop, DropPadToWorld). The test also #includes
REM overlay_actions.h (header-only, <cstdio>/<string>) to prove a
REM MinimapToWorld() result composes into BuildSpawnUnitCommand. No Windows,
REM no ImGui, no bridge, no <thread>. Needs no game and no pipe. Reuses the
REM MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 530): mirrors build_dragdrop_test.bat (iter 529) — full
REM compiler path via `where`, cwd pinned to this script's folder, test exe
REM run by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay tactical-minimap kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === MINIMAP TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_minimap_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_minimap_test.cpp -o overlay_minimap_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_minimap_test.exe...
echo.
".\overlay_minimap_test.exe"
if errorlevel 1 goto testfail

echo.
echo === MINIMAP TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === MINIMAP TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === MINIMAP TEST: FAILURES ===
exit /b 1

:end
