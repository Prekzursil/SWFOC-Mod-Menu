@echo off
REM ============================================================================
REM build_input_test.bat — compile + run the overlay_input.h test.
REM
REM overlay_input.h is dependency-free (no <windows.h>, no ImGui, no std
REM threading) — the WndProc-detour swallow rule is pure. The test needs no
REM game and no window. Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM 2026-05-21 (iter 514): mirrors build_action_queue_test.bat (iter 513) —
REM full compiler path via `where`, cwd pinned to this script's folder, test
REM exe run by explicit relative path. No -pthread (overlay_input.h uses no
REM std::mutex). CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay input-routing unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === INPUT TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_input_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static overlay_input_test.cpp -o overlay_input_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_input_test.exe...
echo.
".\overlay_input_test.exe"
if errorlevel 1 goto testfail

echo.
echo === INPUT TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === INPUT TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === INPUT TEST: FAILURES ===
exit /b 1

:end
