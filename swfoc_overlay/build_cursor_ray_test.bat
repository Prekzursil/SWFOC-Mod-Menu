@echo off
REM ============================================================================
REM build_cursor_ray_test.bat — compile + run the overlay_cursor_ray.h test
REM (Phase 5 cont., iter 537 / spec iter-298).
REM
REM overlay_cursor_ray.h is header-only and std-only — it pulls in <cmath> for
REM sqrt / tan / fabs. The test adds <cstdio>. No Windows, no ImGui, no bridge,
REM no <thread>. Needs no game and no pipe. Reuses the MinGW g++ that build.bat
REM uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 537): mirrors build_spawn_gate_test.bat (iter 532) — full
REM compiler path via `where`, cwd pinned to this script's folder, test exe run
REM by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay cursor-ray kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === CURSOR-RAY TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_cursor_ray_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_cursor_ray_test.cpp -o overlay_cursor_ray_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_cursor_ray_test.exe...
echo.
".\overlay_cursor_ray_test.exe"
if errorlevel 1 goto testfail

echo.
echo === CURSOR-RAY TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === CURSOR-RAY TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === CURSOR-RAY TEST: FAILURES ===
exit /b 1

:end
