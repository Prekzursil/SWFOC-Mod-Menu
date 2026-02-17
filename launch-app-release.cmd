@echo off
setlocal

set "REPO_ROOT=%~dp0"
if "%REPO_ROOT:~-1%"=="\" set "REPO_ROOT=%REPO_ROOT:~0,-1%"
set "DOTNET_NOLOGO=1"
set "SOLUTION_PATH=%REPO_ROOT%\SwfocTrainer.sln"

set "APP_EXE=src\SwfocTrainer.App\bin\Release\net8.0-windows\SwfocTrainer.App.exe"

cd /d "%REPO_ROOT%"
if exist "%APP_EXE%" goto :start_app

echo [launch-app-release] Release EXE not found. Building Release...
dotnet build "%SOLUTION_PATH%" -c Release --nologo
set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" goto :build_failed
if not exist "%APP_EXE%" goto :missing_exe

:start_app
echo [launch-app-release] Starting %APP_EXE%
start "" "%APP_EXE%"
exit /b 0

:build_failed
echo [launch-app-release] Build failed with exit code %BUILD_EXIT%.
echo [launch-app-release] Ensure the .NET SDK required by global.json is installed.
exit /b %BUILD_EXIT%

:missing_exe
echo [launch-app-release] Could not locate: %APP_EXE%
exit /b 1
