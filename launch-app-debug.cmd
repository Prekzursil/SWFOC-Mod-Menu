@echo off
setlocal

set "REPO_ROOT=%~dp0"
if "%REPO_ROOT:~-1%"=="\" set "REPO_ROOT=%REPO_ROOT:~0,-1%"
set "DOTNET_NOLOGO=1"
set "SOLUTION_PATH=%REPO_ROOT%\SwfocTrainer.sln"

set "APP_EXE=src\SwfocTrainer.App\bin\Debug\net8.0-windows\SwfocTrainer.App.exe"

cd /d "%REPO_ROOT%"
if exist "%APP_EXE%" goto :start_app

echo [launch-app-debug] Debug EXE not found. Building Debug...
dotnet build "%SOLUTION_PATH%" -c Debug --nologo
set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" goto :build_failed
if not exist "%APP_EXE%" goto :missing_exe

:start_app
echo [launch-app-debug] Starting %APP_EXE%
start "" "%APP_EXE%"
exit /b 0

:build_failed
echo [launch-app-debug] Build failed with exit code %BUILD_EXIT%.
echo [launch-app-debug] Ensure the .NET SDK required by global.json is installed.
exit /b %BUILD_EXIT%

:missing_exe
echo [launch-app-debug] Could not locate: %APP_EXE%
exit /b 1
