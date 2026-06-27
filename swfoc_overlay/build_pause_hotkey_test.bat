@echo off
REM ============================================================================
REM build_pause_hotkey_test.bat — compile + run the
REM overlay_pause_hotkey.h test (Phase 6 cont., iter 546 / spec iter-306).
REM
REM overlay_pause_hotkey.h is header-only and std-only — it pulls in
REM <cmath> / <string>, plus <cstdio> via overlay_actions.h and <deque> /
REM <functional> / <mutex> via overlay_action_queue.h. That last include
REM carries the threading runtime, so -pthread is load-bearing here, not
REM parity. No Windows, no ImGui, no bridge. Needs no game and no pipe.
REM Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH.
REM
REM 2026-05-21 (iter 546): mirrors build_faction_switch_test.bat (iter 545) —
REM full compiler path via `where`, cwd pinned to this script's folder, test exe
REM run by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay pause-hotkey kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === PAUSE-HOTKEY TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_pause_hotkey_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_pause_hotkey_test.cpp -o overlay_pause_hotkey_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_pause_hotkey_test.exe...
echo.
".\overlay_pause_hotkey_test.exe"
if errorlevel 1 goto testfail

echo.
echo === PAUSE-HOTKEY TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === PAUSE-HOTKEY TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === PAUSE-HOTKEY TEST: FAILURES ===
exit /b 1

:end
