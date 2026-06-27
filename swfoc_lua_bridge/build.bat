@echo off
echo === SWFOC Lua Bridge Build ===
echo.

set GCC=x86_64-w64-mingw32-gcc
set GPP=x86_64-w64-mingw32-g++
set CFLAGS=-O2 -DWIN32_LEAN_AND_MEAN
set CPPFLAGS=-O2 -std=c++17 -DWIN32_LEAN_AND_MEAN -I. -Iminhook/include

echo [1/4] Compiling MinHook...
%GCC% -c %CFLAGS% -Iminhook/include -Iminhook/src minhook/src/hook.c -o hook.o
%GCC% -c %CFLAGS% minhook/src/buffer.c -o buffer.o
%GCC% -c %CFLAGS% minhook/src/trampoline.c -o trampoline.o
%GCC% -c %CFLAGS% minhook/src/hde/hde64.c -o hde64.o
if errorlevel 1 goto fail

echo [2/4] Compiling proxy...
%GPP% -c %CPPFLAGS% proxy.cpp -o proxy.o
if errorlevel 1 goto fail

echo [3/4] Compiling bridge...
%GPP% -c %CPPFLAGS% lua_bridge.cpp -o lua_bridge.o
%GPP% -c %CPPFLAGS% dllmain.cpp -o dllmain.o
if errorlevel 1 goto fail

echo [4/4] Linking powrprof.dll...
%GPP% -shared -o powrprof.dll dllmain.o proxy.o lua_bridge.o hook.o buffer.o trampoline.o hde64.o exports.def -lkernel32 -luser32 -static -s
if errorlevel 1 goto fail

echo.
echo === DLL BUILD SUCCESS ===
dir powrprof.dll
echo.
echo Install: copy powrprof.dll to your game's corruption\ folder
echo.

echo [5/6] Building test harness...
%GPP% -o bridge_test_harness.exe test_harness.cpp fake_lua.cpp fake_memory.cpp -std=c++17 -DTEST_MODE -DWIN32_LEAN_AND_MEAN -I. -static
if errorlevel 1 goto test_fail

echo.
echo Running tests...
.\bridge_test_harness.exe
if errorlevel 1 goto test_fail

echo [6/6] Building replay harness...
%GPP% -O2 -std=c++17 -static -o swfoc_replay.exe replay_harness.cpp fake_lua.cpp fake_memory.cpp -lws2_32
if errorlevel 1 goto replay_fail

echo.
echo === ALL BUILDS AND TESTS PASSED ===
dir swfoc_replay.exe
goto end

:replay_fail
echo.
echo === REPLAY HARNESS BUILD FAILED ===
goto end

:test_fail
echo.
echo === TEST HARNESS BUILD OR RUN FAILED ===
goto end

:fail
echo.
echo === DLL BUILD FAILED ===

:end
