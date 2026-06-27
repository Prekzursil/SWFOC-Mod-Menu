@echo off
REM ============================================================================
REM build_hit_test_test.bat — compile + run the overlay_hit_test.h test
REM (Phase 5 cont., iter 538 / spec iter-299).
REM
REM overlay_hit_test.h is header-only and std-only — it pulls in <cmath> (via
REM overlay_cursor_ray.h), <cstdint>, and <limits>. The test adds <cstdio>. No
REM Windows, no ImGui, no bridge, no <thread>. Needs no game and no pipe.
REM Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 538): mirrors build_cursor_ray_test.bat (iter 537) — full
REM compiler path via `where`, cwd pinned to this script's folder, test exe run
REM by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay hit-test kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === HIT-TEST TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_hit_test_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_hit_test_test.cpp -o overlay_hit_test_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_hit_test_test.exe...
echo.
".\overlay_hit_test_test.exe"
if errorlevel 1 goto testfail

echo.
echo === HIT-TEST TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === HIT-TEST TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === HIT-TEST TEST: FAILURES ===
exit /b 1

:end
