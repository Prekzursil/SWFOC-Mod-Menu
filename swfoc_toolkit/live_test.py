#!/usr/bin/env python3
"""
SWFOC Trainer v4 — Autonomous Live Test Harness

Deploys the DLL, launches the game, waits for it to load,
runs pipe tests, and reports pass/fail. No manual intervention needed.

Usage:
    python live_test.py                    # Full deploy + test cycle
    python live_test.py --test-only        # Skip deploy, just test running game
    python live_test.py --deploy-only      # Deploy DLL without testing
    python live_test.py --kill             # Kill game process
"""

import argparse
import ctypes
import ctypes.wintypes
import os
import struct
import subprocess
import sys
import time
from pathlib import Path

# === Configuration ===
GAME_DIR = Path(r"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption")
BUILD_DIR = Path(r"C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge")
TRAINER_DIR = Path(r"C:\Users\Prekzursil\Downloads\swfoc_memory\trainer")
STEAM_APP_ID = "32470"  # EaW: FoC Steam app ID
PIPE_NAME = rb"\\.\pipe\swfoc_bridge"
GAME_EXE = "StarWarsG.exe"
DLL_NAME = "powrprof.dll"
LOG_NAME = "swfoc_bridge.log"

# Trainer Lua modules to deploy
TRAINER_MODULES = [
    "shared_cmd.lua", "god_mode.lua", "type_discovery.lua",
    "fow_toggle.lua", "lua_playground.lua", "dps_log.lua",
    "triggers.lua", "blueprints.lua",
]

k32 = ctypes.windll.kernel32


def find_process(name: str) -> int | None:
    """Find a process by name, return PID or None."""
    import subprocess
    result = subprocess.run(
        ["tasklist", "/FI", f"IMAGENAME eq {name}", "/FO", "CSV", "/NH"],
        capture_output=True, text=True
    )
    for line in result.stdout.strip().split("\n"):
        if name.lower() in line.lower():
            parts = line.strip('"').split('","')
            if len(parts) >= 2:
                return int(parts[1])
    return None


def kill_game() -> bool:
    """Kill the game process if running."""
    pid = find_process(GAME_EXE)
    if pid:
        print(f"[*] Killing {GAME_EXE} (PID {pid})...")
        subprocess.run(["taskkill", "/F", "/PID", str(pid)], capture_output=True)
        time.sleep(2)
        return True
    print("[*] Game not running")
    return False


def deploy_dll() -> bool:
    """Copy the built DLL and trainer modules to the game directory."""
    src_dll = BUILD_DIR / DLL_NAME
    dst_dll = GAME_DIR / DLL_NAME

    if not src_dll.exists():
        print(f"[!] DLL not found: {src_dll}")
        return False

    # Check if game is running (file would be locked)
    if find_process(GAME_EXE):
        print("[!] Game is running — killing it first...")
        kill_game()
        time.sleep(3)

    # Copy DLL
    print(f"[*] Deploying {DLL_NAME}...")
    import shutil
    shutil.copy2(str(src_dll), str(dst_dll))
    print(f"    {src_dll} -> {dst_dll} ({dst_dll.stat().st_size} bytes)")

    # Copy trainer modules
    for mod in TRAINER_MODULES:
        src = TRAINER_DIR / mod
        dst = GAME_DIR / mod
        if src.exists():
            shutil.copy2(str(src), str(dst))
            print(f"    Deployed {mod}")
        else:
            print(f"    [!] Missing: {mod}")

    return True


def launch_game() -> bool:
    """Launch the game via Steam."""
    print(f"[*] Launching game via Steam (AppID {STEAM_APP_ID})...")
    subprocess.Popen(
        ["cmd", "/c", f"start steam://rungameid/{STEAM_APP_ID}"],
        shell=True
    )
    return True


def wait_for_game(timeout: int = 120) -> int | None:
    """Wait for the game process to appear and stabilize."""
    print(f"[*] Waiting for {GAME_EXE} to start (timeout {timeout}s)...")
    start = time.time()
    while time.time() - start < timeout:
        pid = find_process(GAME_EXE)
        if pid:
            print(f"[*] {GAME_EXE} found (PID {pid}), waiting for stabilization...")
            time.sleep(10)  # Let the game load
            return pid
        time.sleep(2)
        sys.stdout.write(".")
        sys.stdout.flush()
    print("\n[!] Timeout waiting for game")
    return None


def wait_for_bridge_log(timeout: int = 60) -> bool:
    """Wait for bridge log to show successful initialization."""
    log_path = GAME_DIR / LOG_NAME
    print(f"[*] Waiting for bridge log ({timeout}s)...")
    start = time.time()
    while time.time() - start < timeout:
        if log_path.exists():
            content = log_path.read_text(errors="replace")
            if "Pipe listener thread started" in content:
                print("[+] Bridge initialized successfully")
                if "SHM] Command buffer created" in content:
                    print("[+] Shared memory command buffer active")
                if "Take_Damage_Outer hooked" in content:
                    print("[+] Event stream hooks active")
                if "lua_close hooked" in content:
                    print("[+] lua_close hook active")
                if "Crash handler installed" in content:
                    print("[+] Crash handler active")
                # Check for game state
                if "localSlot=" in content and "FAILED" not in content.split("localSlot=")[-1][:20]:
                    print("[+] Game state cached (in a match)")
                else:
                    print("[!] Game at menu — need to start a match for pipe commands to work")
                return True
        time.sleep(2)
    print("[!] Bridge log not found or incomplete")
    return False


def pipe_connect() -> int | None:
    """Connect to the bridge named pipe using Win32 API. Returns handle or None."""
    GENERIC_RW = 0xC0000000
    OPEN_EXISTING = 3

    # Wait for pipe to be available
    k32.WaitNamedPipeA(PIPE_NAME, 5000)

    h = k32.CreateFileA(PIPE_NAME, GENERIC_RW, 0, None, OPEN_EXISTING, 0, None)
    if h == -1 or h == 0xFFFFFFFFFFFFFFFF:
        return None
    return h


def pipe_send(handle: int, cmd: str, timeout: float = 10.0) -> str | None:
    """Send a command through the pipe and read the response."""
    # Write command + null terminator
    data = cmd.encode("utf-8") + b"\x00"
    written = ctypes.c_ulong(0)
    ok = k32.WriteFile(handle, data, len(data), ctypes.byref(written), None)
    if not ok:
        return None

    # Read response (byte-by-byte until newline)
    buf = ctypes.create_string_buffer(4096)
    read_bytes = ctypes.c_ulong(0)

    # Wait for response
    start = time.time()
    while time.time() - start < timeout:
        ok = k32.ReadFile(handle, buf, 4096, ctypes.byref(read_bytes), None)
        if ok and read_bytes.value > 0:
            return buf.value.decode("utf-8", errors="replace").strip()
        time.sleep(0.1)
    return None


def run_tests() -> tuple[int, int]:
    """Run all pipe tests. Returns (passed, failed)."""
    passed = 0
    failed = 0

    tests = [
        ("GetCredits", "return SWFOC_GetCredits()", None),
        ("GetVersion", "return SWFOC_GetVersion()", "SWFOC Lua Bridge"),
        ("GetLocalPlayer", "return SWFOC_GetLocalPlayer()", None),
        ("ListFactions", "local t = SWFOC_ListFactions(); return tostring(table.getn(t))", None),  # Lua 5.0: table.getn not #
        ("DoString", "return SWFOC_DoString('return 42')", None),
        ("StateInfo", "return SWFOC_StateInfo()", "Game states"),
        ("Math", "return tostring(1 + 1)", "2"),
        ("EventControl", "return SWFOC_EventControl(1)", None),
    ]

    for name, cmd, expected in tests:
        # Retry up to 3 times with delay (pipe is single-instance, may be recycling)
        resp = None
        for attempt in range(3):
            h = pipe_connect()
            if h is None:
                time.sleep(1)
                continue
            resp = pipe_send(h, cmd)
            k32.CloseHandle(h)
            if resp is not None:
                break
            time.sleep(1)

        if resp is None:
            # Try one more time after longer wait
            time.sleep(3)
            h = pipe_connect()
            if h:
                resp = pipe_send(h, cmd)
                k32.CloseHandle(h)

        if resp is None:
            print(f"  [{name}] FAIL — timeout")
            failed += 1
        elif resp.startswith("ERR:"):
            print(f"  [{name}] FAIL — {resp}")
            failed += 1
        elif expected and expected not in resp:
            print(f"  [{name}] FAIL — expected '{expected}' in '{resp}'")
            failed += 1
        else:
            print(f"  [{name}] PASS — {resp[:80]}")
            passed += 1

    return passed, failed


def main():
    parser = argparse.ArgumentParser(description="SWFOC Trainer v4 Live Test Harness")
    parser.add_argument("--test-only", action="store_true", help="Skip deploy, just test")
    parser.add_argument("--deploy-only", action="store_true", help="Deploy without testing")
    parser.add_argument("--kill", action="store_true", help="Kill game process")
    parser.add_argument("--no-launch", action="store_true", help="Deploy but don't launch game")
    args = parser.parse_args()

    if args.kill:
        kill_game()
        return

    print("=" * 60)
    print("SWFOC Trainer v4 — Autonomous Live Test Harness")
    print("=" * 60)

    if not args.test_only:
        # Deploy
        if not deploy_dll():
            print("[!] Deploy failed")
            sys.exit(1)

        if args.deploy_only:
            print("[+] Deploy complete")
            return

        if not args.no_launch:
            # Launch game
            launch_game()

            # Wait for game to start
            pid = wait_for_game(timeout=120)
            if not pid:
                print("[!] Game did not start")
                sys.exit(1)

    # Wait for bridge initialization
    if not wait_for_bridge_log(timeout=60):
        print("[!] Bridge did not initialize")
        sys.exit(1)

    # Check if game is in a match (needed for pipe commands)
    log_content = (GAME_DIR / LOG_NAME).read_text(errors="replace")
    if "localSlot=-1" in log_content.split("SELF-TESTS COMPLETE")[-1]:
        print("\n[!] Game is at the menu. Pipe commands need a match running.")
        print("[!] Please start a skirmish/GC battle, then run:")
        print("    python live_test.py --test-only")
        sys.exit(0)

    # Run tests
    print("\n" + "=" * 60)
    print("Running pipe command tests...")
    print("=" * 60)

    passed, failed = run_tests()

    print("\n" + "=" * 60)
    print(f"Results: {passed} passed, {failed} failed")
    print("=" * 60)

    sys.exit(1 if failed > 0 else 0)


if __name__ == "__main__":
    main()
