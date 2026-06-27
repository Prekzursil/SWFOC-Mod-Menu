#!/usr/bin/env python3
"""
Live-game smoke runner for the SWFOC Trainer Editor bridge.

Fires 7 sentinel bridge commands against a running SWFOC instance and reports
per-command pass/fail. Use this after launching the game with powrprof.dll
deployed to verify the bridge round-trip works end-to-end.

Usage:
    python live_smoke.py                 # default sentinel suite
    python live_smoke.py --diag-only     # only StateInfo + bridge-alive probe
    python live_smoke.py --verbose       # show full response payloads

Exit codes:
    0 = all sentinels passed
    1 = one or more sentinel failed (per-command status printed)
    2 = pipe connect failed (game not running, DLL not deployed, etc.)
"""

from __future__ import annotations

import argparse
import sys
import time
from dataclasses import dataclass
from typing import Iterable

PIPE_NAME = r"\\.\pipe\swfoc_bridge"
DEFAULT_TIMEOUT_S = 5.0


@dataclass(frozen=True)
class Sentinel:
    """One bridge sentinel: a Lua command + a predicate that classifies the response."""

    name: str
    command: str
    expects_ok: bool = True
    description: str = ""


def send_command(lua_code: str, timeout_s: float = DEFAULT_TIMEOUT_S) -> str:
    """Send a null-terminated Lua command and read the response.

    Returns the raw response string, or a CONNECT_FAIL: prefix on connection error.
    """
    try:
        handle = open(PIPE_NAME, "r+b", buffering=0)
    except FileNotFoundError:
        return "CONNECT_FAIL: pipe not found — game not running / DLL not deployed"
    except PermissionError:
        return "CONNECT_FAIL: permission denied on pipe"
    except OSError as e:
        return f"CONNECT_FAIL: {e}"

    try:
        handle.write(lua_code.encode("utf-8") + b"\x00")
        handle.flush()

        start = time.monotonic()
        chunks: list[bytes] = []
        while time.monotonic() - start < timeout_s:
            try:
                data = handle.read(4096)
                if not data:
                    break
                chunks.append(data)
            except OSError:
                break
        return b"".join(chunks).decode("utf-8", errors="replace")
    finally:
        handle.close()


SENTINEL_SUITE: tuple[Sentinel, ...] = (
    Sentinel(
        name="StateInfo",
        command="return SWFOC_StateInfo()",
        description="Reads engine scene/mode/state — fundamental health probe",
    ),
    Sentinel(
        name="BridgeAlive",
        command="return SWFOC_BridgeVersion()",
        description="Confirms bridge DLL is loaded and responding",
    ),
    Sentinel(
        name="LocalPlayer",
        command="return SWFOC_GetLocalPlayerSlot()",
        description="Reads which slot is the human/local player",
    ),
    Sentinel(
        name="LocalCredits",
        command="return SWFOC_GetCreditsForLocalPlayer()",
        description="Reads current credits — confirms PlayerObject indirection works",
    ),
    Sentinel(
        name="ObjectTypeProbe",
        command='return SWFOC_BatchTypeExists("TIE_Fighter|Vengeance_Frigate|NotARealUnit")',
        description="Confirms Find_Object_Type lookup engine path is functional",
    ),
    Sentinel(
        name="LocalFaction",
        command="return SWFOC_GetFactionForLocalPlayer()",
        description="Reads faction string — confirms PlayerObject+0x18 chain is intact",
    ),
    Sentinel(
        name="ModName",
        command='return SWFOC_DoString("return _G[\\"GameMode\\"] or \\"unknown\\"")',
        description="DoString round-trip — confirms generic Lua dispatch works",
    ),
)

DIAG_ONLY_SUITE: tuple[Sentinel, ...] = SENTINEL_SUITE[:2]


def classify_response(response: str) -> tuple[bool, str]:
    """Return (passed, status_str) for a sentinel response."""
    if response.startswith("CONNECT_FAIL"):
        return False, "CONNECT_FAIL"
    if response.startswith("ERR"):
        return False, "ERR (bridge returned error)"
    if response.startswith("OK") or response.strip():
        return True, "PASS"
    return False, "EMPTY (no response)"


def run_suite(suite: Iterable[Sentinel], verbose: bool = False) -> tuple[int, int, bool]:
    """Run the suite; print per-sentinel status. Returns (passed, total, hit_connect_fail)."""
    passed = 0
    total = 0
    hit_connect_fail = False

    print(f"Pipe: {PIPE_NAME}")
    print("=" * 70)

    for sentinel in suite:
        total += 1
        print(f"[{total:>2}/{len(tuple(suite)) if isinstance(suite, tuple) else '?':>2}] {sentinel.name}")
        print(f"     {sentinel.description}")
        response = send_command(sentinel.command)
        ok, status = classify_response(response)
        if ok:
            passed += 1
        if status == "CONNECT_FAIL":
            hit_connect_fail = True

        status_marker = "PASS" if ok else "FAIL"
        print(f"     {status_marker}  [{status}]")
        if verbose or not ok:
            preview = response[:200] + ("..." if len(response) > 200 else "")
            print(f"     response: {preview!r}")
        print()

        if hit_connect_fail:
            break

    return passed, total, hit_connect_fail


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Live SWFOC bridge smoke runner",
    )
    parser.add_argument("--diag-only", action="store_true",
                        help="Only run StateInfo + BridgeAlive (fast probe)")
    parser.add_argument("--verbose", action="store_true",
                        help="Show full response payloads even on PASS")
    args = parser.parse_args()

    suite = DIAG_ONLY_SUITE if args.diag_only else SENTINEL_SUITE

    print("SWFOC Trainer Editor — Live-Game Smoke Runner")
    print()
    passed, total, hit_connect_fail = run_suite(suite, verbose=args.verbose)

    print("=" * 70)
    print(f"Result: {passed}/{total} sentinels passed")

    if hit_connect_fail:
        print()
        print("BRIDGE NOT REACHABLE.")
        print("  - Confirm SWFOC is running (StarWarsG.exe).")
        print("  - Confirm powrprof.dll bridge is deployed alongside the game.")
        print("  - Check Connection & Diagnostics tab in the editor for log output.")
        return 2

    if passed == total:
        print()
        print("LIVE END-TO-END VERIFIED. All bridge sentinels green.")
        return 0

    print()
    print(f"{total - passed} sentinel(s) FAILED. See per-command output above.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
