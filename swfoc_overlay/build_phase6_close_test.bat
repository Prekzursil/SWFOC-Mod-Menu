@echo off
REM ============================================================================
REM build_phase6_close_test.bat - compile + run the Phase 6 close-out
REM integration test (Phase 6 close-out part 2/2, iter 548 / spec iter-307).
REM
REM overlay_phase6_close_test.cpp is a pure INTEGRATION test: it wires the
REM three Phase 6 feature kernels (overlay_camera_bookmarks.h +
REM overlay_faction_switch.h + overlay_pause_hotkey.h) together with the
REM iter-307 conflict matrix (overlay_hotkey_matrix.h) and the iter-513
REM ActionQueue (overlay_action_queue.h) and exercises the complete
REM hotkey-routing pipeline. All five headers are header-only and std-only.
REM No Windows, no ImGui, no bridge. Needs no game and no pipe. Reuses the
REM MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no
REM DLL on PATH. -pthread is carried because overlay_action_queue.h pulls in
REM <mutex>; the ActionQueue seam in section [5] needs the threading runtime.
REM
REM 2026-05-21 (iter 548): mirrors build_phase5_close_test.bat (iter 542) -
REM full compiler path via `where`, cwd pinned to this script's folder, test
REM exe run by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay Phase 6 close-out integration test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === PHASE6-CLOSE TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_phase6_close_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_phase6_close_test.cpp -o overlay_phase6_close_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_phase6_close_test.exe...
echo.
".\overlay_phase6_close_test.exe"
if errorlevel 1 goto testfail

echo.
echo === PHASE6-CLOSE TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === PHASE6-CLOSE TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === PHASE6-CLOSE TEST: FAILURES ===
exit /b 1

:end
