@echo off
REM ============================================================================
REM build_dragdrop_test.bat — compile + run the overlay_dragdrop.h test
REM (Phase 4 kickoff, iter 529 / spec iter-292).
REM
REM overlay_dragdrop.h is header-only and std-only (<cstddef>/<cstring>). The
REM test also #includes overlay_actions.h (header-only, <cstdio>/<string>) to
REM prove a DropPadToWorld() result composes into BuildSpawnUnitCommand. No
REM Windows, no ImGui, no bridge, no <thread>. Needs no game and no pipe.
REM Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 529): mirrors build_phase3_catalog_test.bat (iter 527) —
REM full compiler path via `where`, cwd pinned to this script's folder, test
REM exe run by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay drag-drop spawn kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === DRAGDROP TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_dragdrop_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_dragdrop_test.cpp -o overlay_dragdrop_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_dragdrop_test.exe...
echo.
".\overlay_dragdrop_test.exe"
if errorlevel 1 goto testfail

echo.
echo === DRAGDROP TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === DRAGDROP TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === DRAGDROP TEST: FAILURES ===
exit /b 1

:end
