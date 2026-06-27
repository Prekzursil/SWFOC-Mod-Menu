@echo off
REM ============================================================================
REM build_phase3_catalog_test.bat — compile + run the overlay_phase3_catalog.h
REM test (Phase 3 close-out, iter 527 / spec iter-291).
REM
REM overlay_phase3_catalog.h is header-only and std-only (<cstddef>/<cstring>).
REM The test also #includes overlay_actions.h (header-only, <cstdio>/<string>)
REM to cross-check each catalogued wire against the builder that sends it. No
REM Windows, no ImGui, no bridge, no <thread>. Needs no game and no pipe.
REM Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 527): mirrors build_recent_actions_test.bat (iter 521) —
REM full compiler path via `where`, cwd pinned to this script's folder, test
REM exe run by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay Phase3 capability catalog unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === PHASE3 CATALOG TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_phase3_catalog_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_phase3_catalog_test.cpp -o overlay_phase3_catalog_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_phase3_catalog_test.exe...
echo.
".\overlay_phase3_catalog_test.exe"
if errorlevel 1 goto testfail

echo.
echo === PHASE3 CATALOG TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === PHASE3 CATALOG TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === PHASE3 CATALOG TEST: FAILURES ===
exit /b 1

:end
