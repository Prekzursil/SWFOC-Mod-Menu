@echo off
setlocal

set "REPO_ROOT=%~dp0"
if "%REPO_ROOT:~-1%"=="\" set "REPO_ROOT=%REPO_ROOT:~0,-1%"
set "PROJECT_PATH=%REPO_ROOT%\tests\SwfocTrainer.Tests\SwfocTrainer.Tests.csproj"
set "DOTNET_NOLOGO=1"

echo [run-live-tests] Running live profile tests...
echo [run-live-tests] Make sure SWFOC is running if you expect non-skip results.

cd /d "%REPO_ROOT%"
dotnet test "%PROJECT_PATH%" -c Release --nologo ^
  --filter "FullyQualifiedName~SwfocTrainer.Tests.Profiles.Live"

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :failed

echo [run-live-tests] Completed (passes and/or expected skips).
exit /b 0

:failed
echo [run-live-tests] FAILED with exit code %EXIT_CODE%.
echo [run-live-tests] Ensure the .NET SDK required by global.json is installed.
exit /b %EXIT_CODE%
