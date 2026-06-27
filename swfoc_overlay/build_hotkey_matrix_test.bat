@echo off
REM ============================================================================
REM build_hotkey_matrix_test.bat — compile + run the overlay_hotkey_matrix.h
REM test (Phase 6 close-out part 1/2, iter 547 / spec iter-307).
REM
REM overlay_hotkey_matrix.h is header-only and std-only (<cstddef> only). The
REM test cross-checks the F4 / F6 / F7 / F8 rows against the Phase 6 kernels
REM overlay_pause_hotkey.h and overlay_camera_bookmarks.h, whose include chains
REM pull in <cstdio> / <string> / <deque> / <functional> / <mutex>. That last
REM include carries the threading runtime, so -pthread is load-bearing here,
REM not parity. No Windows, no ImGui, no bridge. Needs no game and no pipe.
REM Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH.
REM
REM 2026-05-21 (iter 547): mirrors build_camera_bookmarks_test.bat (iter 544) —
REM full compiler path via `where`, cwd pinned to this script's folder, test exe
REM run by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay hotkey-conflict-matrix kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === HOTKEY-MATRIX TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_hotkey_matrix_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_hotkey_matrix_test.cpp -o overlay_hotkey_matrix_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_hotkey_matrix_test.exe...
echo.
".\overlay_hotkey_matrix_test.exe"
if errorlevel 1 goto testfail

echo.
echo === HOTKEY-MATRIX TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === HOTKEY-MATRIX TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === HOTKEY-MATRIX TEST: FAILURES ===
exit /b 1

:end
