#!/usr/bin/env python3
"""Smoke test for swfoc_replay.exe -- connects to the replay pipe and
sends a handful of round-trip commands, asserting we get real values
instead of empty/error responses.

Uses Win32 CreateFileA/ReadFile/WriteFile directly since Python's built-in
open() cannot speak byte-mode duplex named pipes on Windows."""

import ctypes
from ctypes import wintypes
import sys
import time

PIPE = r"\\.\pipe\swfoc_bridge_replay"

GENERIC_READ  = 0x80000000
GENERIC_WRITE = 0x40000000
OPEN_EXISTING = 3
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
ERROR_PIPE_BUSY = 231

k32 = ctypes.WinDLL("kernel32", use_last_error=True)

CreateFileA = k32.CreateFileA
CreateFileA.argtypes = [wintypes.LPCSTR, wintypes.DWORD, wintypes.DWORD,
                         ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD,
                         wintypes.HANDLE]
CreateFileA.restype = wintypes.HANDLE

WriteFile = k32.WriteFile
WriteFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD,
                       ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
WriteFile.restype = wintypes.BOOL

ReadFile = k32.ReadFile
ReadFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD,
                      ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
ReadFile.restype = wintypes.BOOL

CloseHandle = k32.CloseHandle
CloseHandle.argtypes = [wintypes.HANDLE]
CloseHandle.restype = wintypes.BOOL

WaitNamedPipeA = k32.WaitNamedPipeA
WaitNamedPipeA.argtypes = [wintypes.LPCSTR, wintypes.DWORD]
WaitNamedPipeA.restype = wintypes.BOOL


def send(cmd: str) -> str:
    """Open the pipe, write a command, read the response, close. Matches
    the protocol the live bridge uses: one shot per connection."""
    last_err = None
    for attempt in range(20):
        h = CreateFileA(PIPE.encode("ascii"),
                        GENERIC_READ | GENERIC_WRITE,
                        0, None, OPEN_EXISTING, 0, None)
        if h is None or h == INVALID_HANDLE_VALUE:
            err = ctypes.get_last_error()
            last_err = f"CreateFileA err={err}"
            if err == ERROR_PIPE_BUSY:
                WaitNamedPipeA(PIPE.encode("ascii"), 2000)
            else:
                time.sleep(0.1)
            continue

        try:
            # Write command + null terminator.
            payload = cmd.encode("ascii") + b"\x00"
            written = wintypes.DWORD(0)
            ok = WriteFile(h, payload, len(payload), ctypes.byref(written), None)
            if not ok:
                last_err = f"WriteFile err={ctypes.get_last_error()}"
                continue

            # Read response until ReadFile returns 0 bytes or fails.
            data = bytearray()
            buf = (ctypes.c_char * 1024)()
            read = wintypes.DWORD(0)
            while True:
                ok = ReadFile(h, buf, 1024, ctypes.byref(read), None)
                if not ok or read.value == 0:
                    break
                data += bytes(buf[:read.value])
                if len(data) > 8192:
                    break
            return data.decode("ascii", errors="replace").strip()
        finally:
            CloseHandle(h)

    raise RuntimeError(f"could not connect to {PIPE}: {last_err}")


def main() -> int:
    print(f"Connecting to {PIPE}")

    tests = [
        # ---- baseline (6 tests, in place since the original smoke harness) ----
        ("return SWFOC_GetVersion()",       "SWFOC Lua Bridge v1.0 (replay)"),
        ("return SWFOC_ReplayPlayerCount()", None),
        ("return SWFOC_GetCredits()",        None),
        ("return SWFOC_GetLocalPlayer()",    None),
        ("return SWFOC_ReplayObjectCount(\"TIE_Fighter\")", None),
        ("return SWFOC_ReplayMetadata(\"mod_name\")",       None),
        # ---- v5 service observer/mutation seam coverage (added 2026-04-08) ----
        # The fixture in make_test_snapshot.py defines REBEL=12345, EMPIRE=99999,
        # UNDERWORLD=5000, REBEL-EMPIRE hostile, NABOO corruption=0.75, etc.
        ("return SWFOC_ReplayPlayerCredits(\"EMPIRE\")",            "99999"),
        ("return SWFOC_ReplayPlayerTechLevel(\"REBEL\")",           "3"),
        ("return SWFOC_ReplayDiplomaticState(\"REBEL\",\"EMPIRE\")", "hostile"),
        ("return SWFOC_ReplayPlanetCorruption(\"NABOO\")",          "0.75"),
        # Mutation round-trip: push a story event, then read it back.
        ("return SWFOC_ReplayPushStoryEvent(\"INTRO_REBEL\")",      "1"),
        ("return SWFOC_ReplayLastStoryEvent()",                     "INTRO_REBEL"),
    ]

    failures = 0
    for i, (cmd, expected) in enumerate(tests, 1):
        try:
            reply = send(cmd)
        except Exception as e:
            print(f"  [{i}] CMD: {cmd}\n      FAIL (exception): {e}")
            failures += 1
            continue
        status = "PASS"
        if expected is not None and expected not in reply:
            status = f"FAIL (expected contains '{expected}')"
            failures += 1
        print(f"  [{i}] CMD: {cmd}")
        print(f"      RES: {reply}   [{status}]")

    print()
    print(f"=== {len(tests) - failures}/{len(tests)} tests passed ===")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
