@echo off
REM ============================================================================
REM build_recent_actions_test.bat — compile + run the overlay_recent_actions.h
REM test.
REM
REM overlay_recent_actions.h is header-only; it pulls in overlay_action_queue.h
REM (for the ActionRequest struct) and <vector>/<cstddef> — no Windows, no
REM ImGui, no bridge, no <thread>. The test needs no game and no pipe. Reuses
REM the MinGW g++ that build.bat uses for the DLL.
REM
REM -static -pthread: overlay_action_queue.h's ActionQueue carries a std::mutex
REM member, so the include chain pulls libstdc++'s threading runtime even
REM though RecentActions itself is render-thread-confined and lock-free.
REM -pthread satisfies the threading model; -static links libstdc++ /
REM libwinpthread in so the test exe runs with no DLL on PATH (build.bat links
REM the overlay DLL the same way, with -static).
REM
REM 2026-05-21 (iter 521): mirrors build_action_worker_test.bat (iter 515) —
REM full compiler path via `where`, cwd pinned to this script's folder, test
REM exe run by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay RecentActions unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === RECENT ACTIONS TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_recent_actions_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_recent_actions_test.cpp -o overlay_recent_actions_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_recent_actions_test.exe...
echo.
".\overlay_recent_actions_test.exe"
if errorlevel 1 goto testfail

echo.
echo === RECENT ACTIONS TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === RECENT ACTIONS TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === RECENT ACTIONS TEST: FAILURES ===
exit /b 1

:end
