@echo off
REM ============================================================================
REM build_preview_ring_test.bat — compile + run the overlay_preview_ring.h test
REM (Phase 4 cont., iter 531 / spec iter-294).
REM
REM overlay_preview_ring.h is header-only and std-only (no includes at all —
REM plain float / unsigned-char arithmetic). The test pulls in only <cstdio>.
REM No Windows, no ImGui, no bridge, no <thread>. Needs no game and no pipe.
REM Reuses the MinGW g++ that build.bat uses for the DLL.
REM
REM -static links libstdc++ / libwinpthread in so the test exe runs with no DLL
REM on PATH. -pthread is carried for parity with the sibling overlay test
REM scripts even though this test pulls in no threading runtime.
REM
REM 2026-05-21 (iter 531): mirrors build_dragdrop_test.bat (iter 529) — full
REM compiler path via `where`, cwd pinned to this script's folder, test exe run
REM by explicit relative path. CRLF line endings required (cmd.exe).
REM ============================================================================
cd /d "%~dp0"
echo === Overlay preview-ring kernel unit test ===
echo.

set "GPP="
for /f "delims=" %%i in ('where x86_64-w64-mingw32-g++ 2^>nul') do if not defined GPP set "GPP=%%i"
if not defined GPP echo === PREVIEW-RING TEST: x86_64-w64-mingw32-g++ not on PATH === & exit /b 1

echo [1/2] Compiling overlay_preview_ring_test.cpp...
"%GPP%" -O2 -std=c++17 -Wall -Wextra -Werror -static -pthread overlay_preview_ring_test.cpp -o overlay_preview_ring_test.exe
if errorlevel 1 goto buildfail

echo [2/2] Running overlay_preview_ring_test.exe...
echo.
".\overlay_preview_ring_test.exe"
if errorlevel 1 goto testfail

echo.
echo === PREVIEW-RING TEST: ALL PASS ===
goto end

:buildfail
echo.
echo === PREVIEW-RING TEST: BUILD FAILED ===
exit /b 1

:testfail
echo.
echo === PREVIEW-RING TEST: FAILURES ===
exit /b 1

:end
