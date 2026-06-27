@echo off
REM ============================================================================
REM build_unit_aabb_test.bat — compile + run the overlay_unit_aabb.h test
REM (Phase 5 cont., iter 302 / spec line 60).
REM
REM overlay_unit_aabb.h is header-only and std-only — it pulls in <cstdint>, and
REM (via overlay_hit_test.h / overlay_cursor_ray.h) <cmath> and <limits>. The
REM test adds <cstdio>. No Windows, no ImGui, no bridge, no <thread>. Needs no
REM game and no pipe. Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 302): mirrors build_hit_test_test.bat (iter 538) — full
REM compiler path via `where`, cwd pinned to this script's folder, test exe run
REM by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay unit-AABB kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === UNIT-AABB TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_unit_aabb_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_unit_aabb_test.cpp -o overlay_unit_aabb_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_unit_aabb_test.exe...
echo.
".\overlay_unit_aabb_test.exe"
if errorlevel 1 goto testfail

echo.
echo === UNIT-AABB TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === UNIT-AABB TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === UNIT-AABB TEST: FAILURES ===
exit /b 1

:end
