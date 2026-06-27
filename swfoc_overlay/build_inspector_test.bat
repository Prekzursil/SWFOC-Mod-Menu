@echo off
REM ============================================================================
REM build_inspector_test.bat — compile + run the overlay_inspector.h test
REM (Phase 5 cont., iter 539 / spec iter-300).
REM
REM overlay_inspector.h is header-only and std-only — it pulls in <cstdint> and
REM <cstdio>, plus <cmath> via overlay_hit_test.h / overlay_cursor_ray.h. The
REM test adds <cstring>. No Windows, no ImGui, no bridge, no <thread>. Needs no
REM game and no pipe. Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 539): mirrors build_hit_test_test.bat (iter 538) — full
REM compiler path via `where`, cwd pinned to this script's folder, test exe run
REM by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay inspector kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === INSPECTOR TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_inspector_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_inspector_test.cpp -o overlay_inspector_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_inspector_test.exe...
echo.
".\overlay_inspector_test.exe"
if errorlevel 1 goto testfail

echo.
echo === INSPECTOR TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === INSPECTOR TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === INSPECTOR TEST: FAILURES ===
exit /b 1

:end
