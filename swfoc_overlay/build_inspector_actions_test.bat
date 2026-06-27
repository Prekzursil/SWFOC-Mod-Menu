@echo off
REM ============================================================================
REM build_inspector_actions_test.bat — compile + run the
REM overlay_inspector_actions.h test (Phase 5 cont., iter 540 / spec iter-301).
REM
REM overlay_inspector_actions.h is header-only and std-only — it pulls in
REM <string>, plus <cstdint> / <cstdio> / <cmath> via the overlay_inspector.h,
REM overlay_actions.h and overlay_action_queue.h include chain. overlay_action_
REM queue.h carries <deque> / <functional> / <mutex>, so this test genuinely
REM needs the threading runtime — -pthread is load-bearing here, not parity.
REM The test adds <cstring>. No Windows, no ImGui, no bridge. Needs no game and
REM no pipe. Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH.
REM
REM 2026-05-21 (iter 540): mirrors build_inspector_test.bat (iter 539) — full
REM compiler path via `where`, cwd pinned to this script's folder, test exe run
REM by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay inspector-actions kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === INSPECTOR-ACTIONS TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_inspector_actions_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_inspector_actions_test.cpp -o overlay_inspector_actions_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_inspector_actions_test.exe...
echo.
".\overlay_inspector_actions_test.exe"
if errorlevel 1 goto testfail

echo.
echo === INSPECTOR-ACTIONS TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === INSPECTOR-ACTIONS TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === INSPECTOR-ACTIONS TEST: FAILURES ===
exit /b 1

:end
