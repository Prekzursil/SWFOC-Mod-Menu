@echo off
setlocal

set "REPO_ROOT=%~dp0"
if "%REPO_ROOT:~-1%"=="\" set "REPO_ROOT=%REPO_ROOT:~0,-1%"
set "PROJECT_PATH=%REPO_ROOT%\tests\SwfocTrainer.Tests\SwfocTrainer.Tests.csproj"
set "DOTNET_NOLOGO=1"

echo [run-deterministic-tests] Running deterministic test suite...
cd /d "%REPO_ROOT%"
dotnet test "%PROJECT_PATH%" -c Release --nologo ^
  --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live^&FullyQualifiedName!~RuntimeAttachSmokeTests"

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :failed

echo [run-deterministic-tests] PASSED.
exit /b 0

:failed
echo [run-deterministic-tests] FAILED with exit code %EXIT_CODE%.
echo [run-deterministic-tests] Ensure the .NET SDK required by global.json is installed.
exit /b %EXIT_CODE%
