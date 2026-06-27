@echo off
REM ============================================================================
REM build_action_queue_test.bat — compile + run the overlay_action_queue.h test.
REM
REM overlay_action_queue.h is header-only (std::deque + std::mutex +
REM std::function) — no Windows, no ImGui, no bridge. The test injects a fake
REM send-function so it needs no game and no pipe. Reuses the MinGW g++ that
REM build.bat uses for the DLL.
REM
REM -static -pthread: ActionQueue uses std::mutex, so the test pulls libstdc++'s
REM threading runtime. -pthread satisfies the threading model; -static links
REM libstdc++ / libwinpthread in so the test exe runs with no DLL on PATH
REM (build.bat links the overlay DLL the same way, with -static).
REM
REM 2026-05-21 (iter 513): mirrors build_actions_test.bat (iter 512) — full
REM compiler path via `where`, cwd pinned to this script's folder, test exe run
REM by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay ActionQueue unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === ACTION QUEUE TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_action_queue_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_action_queue_test.cpp -o overlay_action_queue_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_action_queue_test.exe...
echo.
".\overlay_action_queue_test.exe"
if errorlevel 1 goto testfail

echo.
echo === ACTION QUEUE TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === ACTION QUEUE TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === ACTION QUEUE TEST: FAILURES ===
exit /b 1

:end
