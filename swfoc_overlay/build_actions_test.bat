@echo off
REM ============================================================================
REM build_actions_test.bat — compile + run the overlay_actions.h unit test.
REM
REM overlay_actions.h is pure / header-only, so the test needs nothing but a
REM C++17 compiler. Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM 2026-05-21 (iter 512): resolve the FULL compiler path — a bare
REM `x86_64-w64-mingw32-g++` invocation fails sub-tool spawning when the
REM toolchain install dir contains a space (see build.bat for the detail).
REM Also pin the working directory to this script's folder and run the test
REM exe by explicit relative path so it works regardless of caller cwd or a
REM NoDefaultCurrentDirectoryInExePath environment policy.
REM ============================================================================
cd /d "%~dp0"
echo === Overlay Actions builder unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === ACTIONS TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_actions_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror overlay_actions_test.cpp -o overlay_actions_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_actions_test.exe...
echo.
".\overlay_actions_test.exe"
if errorlevel 1 goto testfail

echo.
echo === ACTIONS TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === ACTIONS TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === ACTIONS TEST: FAILURES ===
exit /b 1

:end
