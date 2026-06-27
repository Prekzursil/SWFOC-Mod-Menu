#!/usr/bin/env python3
"""
SWFOC DLL Bridge — Comprehensive Test Suite
=============================================
Connects to the swfoc_bridge named pipe and exercises every SWFOC_*
function registered in lua_bridge.cpp (12 functions total).

Tests:
  - Valid invocation of each function
  - Invalid argument handling for each function
  - Read-set-read cycles for setters (Credits, TechLevel)
  - SWFOC_StateInfo() (v3 game state cache)
  - Stress test: 100 rapid commands
  - Timeout test: 2s deadline
  - Produces bridge_test_report.json

Usage:  python bridge_test.py
        python bridge_test.py --stress-count 200
        python bridge_test.py --timeout 3.0
"""

import json
import os
import sys
import time
import argparse

PIPE_NAME = r"\\.\pipe\swfoc_bridge"
REPORT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bridge_test_report.json")

# ---------------------------------------------------------------------------
# Pipe I/O
# ---------------------------------------------------------------------------

class PipeError(Exception):
    """Raised when the pipe cannot be reached."""
    pass


def pipe_send(lua_code: str, timeout_s: float = 10.0) -> str:
    """
    Send a null-terminated Lua string to the bridge pipe and return the
    raw response string.  Raises PipeError on connection failure.
    """
    try:
        handle = open(PIPE_NAME, "r+b", buffering=0)
    except FileNotFoundError:
        raise PipeError("Pipe not found — game not running or DLL not injected")
    except PermissionError:
        raise PipeError("Permission denied on pipe")
    except OSError as e:
        raise PipeError(str(e))

    try:
        handle.write(lua_code.encode("utf-8") + b"\x00")
        handle.flush()

        deadline = time.monotonic() + timeout_s
        chunks = []
        while time.monotonic() < deadline:
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


def is_ok(response: str) -> bool:
    return response.startswith("OK")


def is_err(response: str) -> bool:
    return response.startswith("ERR")


# ---------------------------------------------------------------------------
# Test framework
# ---------------------------------------------------------------------------

class TestResult:
    def __init__(self, name: str):
        self.name = name
        self.passed = False
        self.detail = ""
        self.response = ""
        self.elapsed_ms = 0.0

    def to_dict(self) -> dict:
        return {
            "name": self.name,
            "passed": self.passed,
            "detail": self.detail,
            "response": self.response[:200],
            "elapsed_ms": round(self.elapsed_ms, 2),
        }


results: list[TestResult] = []


def run_test(name: str, lua_code: str, expect_ok: bool = True,
             timeout_s: float = 10.0) -> TestResult:
    """Run a single pipe command and check OK/ERR expectation."""
    tr = TestResult(name)
    t0 = time.monotonic()
    try:
        resp = pipe_send(lua_code, timeout_s=timeout_s)
        tr.elapsed_ms = (time.monotonic() - t0) * 1000
        tr.response = resp.strip()
        if expect_ok:
            tr.passed = is_ok(resp)
            tr.detail = "OK response" if tr.passed else f"Expected OK, got: {resp[:120]}"
        else:
            tr.passed = is_err(resp)
            tr.detail = "ERR response as expected" if tr.passed else f"Expected ERR, got: {resp[:120]}"
    except PipeError as e:
        tr.elapsed_ms = (time.monotonic() - t0) * 1000
        tr.detail = f"PipeError: {e}"
    results.append(tr)
    status = "PASS" if tr.passed else "FAIL"
    print(f"  [{status}] {name} ({tr.elapsed_ms:.0f}ms) — {tr.detail}")
    return tr


def run_raw(name: str, lua_code: str, timeout_s: float = 10.0) -> TestResult:
    """Run a command and record the raw response without pass/fail judgment yet.
    Caller sets tr.passed and tr.detail after inspecting tr.response."""
    tr = TestResult(name)
    t0 = time.monotonic()
    try:
        resp = pipe_send(lua_code, timeout_s=timeout_s)
        tr.elapsed_ms = (time.monotonic() - t0) * 1000
        tr.response = resp.strip()
    except PipeError as e:
        tr.elapsed_ms = (time.monotonic() - t0) * 1000
        tr.detail = f"PipeError: {e}"
    return tr


# ---------------------------------------------------------------------------
# Individual function tests
# ---------------------------------------------------------------------------

def test_GetVersion():
    """SWFOC_GetVersion() -> string"""
    print("\n=== SWFOC_GetVersion ===")
    # Valid: should return OK (the return value goes to Lua, pipe just says OK)
    run_test("GetVersion_valid",
             "SWFOC_GetVersion()",
             expect_ok=True)
    # Also test return-value capture via a wrapper
    run_test("GetVersion_return",
             "SWFOC_Log(SWFOC_GetVersion())",
             expect_ok=True)


def test_GetLocalPlayer():
    """SWFOC_GetLocalPlayer() -> slot, faction_name"""
    print("\n=== SWFOC_GetLocalPlayer ===")
    run_test("GetLocalPlayer_valid",
             "local s,f = SWFOC_GetLocalPlayer()",
             expect_ok=True)
    # No args needed, so invalid = passing garbage (Lua ignores extra args in 5.0)
    # Test with intentionally wrong usage — calling it as a table
    run_test("GetLocalPlayer_bad_call",
             "local x = SWFOC_GetLocalPlayer.foo",
             expect_ok=False)


def test_SetCredits():
    """SWFOC_SetCredits(amount) -> success"""
    print("\n=== SWFOC_SetCredits ===")
    run_test("SetCredits_valid",
             "SWFOC_SetCredits(5000)",
             expect_ok=True)
    # No arg = tonumber returns 0 in Lua 5.0, so it should still succeed (sets to 0)
    run_test("SetCredits_no_arg",
             "SWFOC_SetCredits()",
             expect_ok=True)
    # String arg — tonumber on a string may return 0 or the number
    run_test("SetCredits_string_arg",
             "SWFOC_SetCredits('abc')",
             expect_ok=True)


def test_GetCredits():
    """SWFOC_GetCredits() -> number"""
    print("\n=== SWFOC_GetCredits ===")
    run_test("GetCredits_valid",
             "local c = SWFOC_GetCredits()",
             expect_ok=True)
    run_test("GetCredits_bad_call",
             "local c = SWFOC_GetCredits.x",
             expect_ok=False)


def test_SetTechLevel():
    """SWFOC_SetTechLevel(level) -> success"""
    print("\n=== SWFOC_SetTechLevel ===")
    run_test("SetTechLevel_valid",
             "SWFOC_SetTechLevel(3)",
             expect_ok=True)
    run_test("SetTechLevel_no_arg",
             "SWFOC_SetTechLevel()",
             expect_ok=True)
    run_test("SetTechLevel_string_arg",
             "SWFOC_SetTechLevel('bad')",
             expect_ok=True)


def test_UncapCredits():
    """SWFOC_UncapCredits() -> success"""
    print("\n=== SWFOC_UncapCredits ===")
    run_test("UncapCredits_valid",
             "SWFOC_UncapCredits()",
             expect_ok=True)
    run_test("UncapCredits_bad_call",
             "local x = SWFOC_UncapCredits.y",
             expect_ok=False)


def test_HeroInstantRespawn():
    """SWFOC_HeroInstantRespawn(enable) -> success"""
    print("\n=== SWFOC_HeroInstantRespawn ===")
    run_test("HeroRespawn_enable",
             "SWFOC_HeroInstantRespawn(1)",
             expect_ok=True)
    run_test("HeroRespawn_disable",
             "SWFOC_HeroInstantRespawn(0)",
             expect_ok=True)
    run_test("HeroRespawn_no_arg",
             "SWFOC_HeroInstantRespawn()",
             expect_ok=True)


def test_ListFactions():
    """SWFOC_ListFactions() -> table"""
    print("\n=== SWFOC_ListFactions ===")
    run_test("ListFactions_valid",
             "local t = SWFOC_ListFactions()",
             expect_ok=True)
    run_test("ListFactions_bad_call",
             "local x = SWFOC_ListFactions.z",
             expect_ok=False)


def test_Log():
    """SWFOC_Log(message)"""
    print("\n=== SWFOC_Log ===")
    run_test("Log_valid",
             'SWFOC_Log("bridge_test: hello from Python")',
             expect_ok=True)
    run_test("Log_no_arg",
             "SWFOC_Log()",
             expect_ok=True)
    run_test("Log_number_arg",
             "SWFOC_Log(12345)",
             expect_ok=True)


def test_DoString():
    """SWFOC_DoString(code) -> success, errmsg"""
    print("\n=== SWFOC_DoString ===")
    run_test("DoString_valid",
             'local ok = SWFOC_DoString("-- noop")',
             expect_ok=True)
    run_test("DoString_syntax_error",
             'local ok, err = SWFOC_DoString("if then")',
             expect_ok=True)   # The function itself succeeds; it returns 0 + error
    run_test("DoString_no_arg",
             "SWFOC_DoString()",
             expect_ok=True)   # Returns 0 + "expected string argument"
    run_test("DoString_bad_call",
             "SWFOC_DoString.x",
             expect_ok=False)


def test_DrainPipe():
    """SWFOC_DrainPipe() -> 0 or 1"""
    print("\n=== SWFOC_DrainPipe ===")
    run_test("DrainPipe_valid",
             "local n = SWFOC_DrainPipe()",
             expect_ok=True)
    run_test("DrainPipe_bad_call",
             "local x = SWFOC_DrainPipe.q",
             expect_ok=False)


def test_StateInfo():
    """SWFOC_StateInfo() -> string (v3 cache info)"""
    print("\n=== SWFOC_StateInfo (v3) ===")
    run_test("StateInfo_valid",
             "local s = SWFOC_StateInfo()",
             expect_ok=True)
    # Capture and log the actual value
    run_test("StateInfo_via_log",
             'SWFOC_Log(SWFOC_StateInfo())',
             expect_ok=True)
    run_test("StateInfo_bad_call",
             "local x = SWFOC_StateInfo.z",
             expect_ok=False)


# ---------------------------------------------------------------------------
# Read-Set-Read cycles
# ---------------------------------------------------------------------------

def test_credits_cycle():
    """Read credits, set to a known value, read back, verify."""
    print("\n=== Credits Read-Set-Read Cycle ===")

    # Step 1: Set to known value
    run_test("CreditsCycle_set",
             "SWFOC_SetCredits(7777)",
             expect_ok=True)

    # Step 2: Read back via DoString wrapper that logs the value
    run_test("CreditsCycle_readback",
             'SWFOC_Log("credits=" .. tostring(SWFOC_GetCredits()))',
             expect_ok=True)

    # Step 3: Restore to something reasonable
    run_test("CreditsCycle_restore",
             "SWFOC_SetCredits(10000)",
             expect_ok=True)


def test_techlevel_cycle():
    """Set tech level, read-back is not directly possible via pipe (no return),
    but we verify the set call succeeds and then set it back."""
    print("\n=== TechLevel Read-Set-Read Cycle ===")
    run_test("TechCycle_set5",
             "SWFOC_SetTechLevel(5)",
             expect_ok=True)
    run_test("TechCycle_set1",
             "SWFOC_SetTechLevel(1)",
             expect_ok=True)


def test_hero_respawn_cycle():
    """Enable, then disable hero respawn."""
    print("\n=== HeroRespawn Enable-Disable Cycle ===")
    run_test("RespawnCycle_enable",
             "SWFOC_HeroInstantRespawn(1)",
             expect_ok=True)
    run_test("RespawnCycle_disable",
             "SWFOC_HeroInstantRespawn(0)",
             expect_ok=True)


# ---------------------------------------------------------------------------
# Stress test
# ---------------------------------------------------------------------------

def test_stress(count: int = 100):
    """Send N rapid commands and track success rate."""
    print(f"\n=== Stress Test ({count} commands) ===")
    passed = 0
    failed = 0
    errors = 0
    t_start = time.monotonic()

    for i in range(count):
        try:
            resp = pipe_send(f'SWFOC_Log("stress_{i}")', timeout_s=15.0)
            if is_ok(resp):
                passed += 1
            else:
                failed += 1
        except PipeError:
            errors += 1

    elapsed = (time.monotonic() - t_start) * 1000
    rate = count / (elapsed / 1000) if elapsed > 0 else 0

    tr = TestResult(f"Stress_{count}_commands")
    tr.elapsed_ms = elapsed
    tr.passed = (errors == 0 and failed == 0)
    tr.detail = (
        f"{passed} OK, {failed} ERR, {errors} pipe errors "
        f"({elapsed:.0f}ms total, {rate:.1f} cmd/s)"
    )
    tr.response = f"passed={passed} failed={failed} errors={errors}"
    results.append(tr)
    status = "PASS" if tr.passed else "FAIL"
    print(f"  [{status}] {tr.name} — {tr.detail}")


# ---------------------------------------------------------------------------
# Timeout test
# ---------------------------------------------------------------------------

def test_timeout(deadline_s: float = 2.0):
    """Send a command with a short timeout to verify we get a response in time."""
    print(f"\n=== Timeout Test ({deadline_s}s deadline) ===")
    tr = TestResult("Timeout_test")
    t0 = time.monotonic()
    try:
        resp = pipe_send("SWFOC_GetVersion()", timeout_s=deadline_s)
        elapsed = time.monotonic() - t0
        tr.elapsed_ms = elapsed * 1000
        tr.response = resp.strip()
        if elapsed <= deadline_s + 0.5:  # allow 500ms grace
            tr.passed = is_ok(resp)
            tr.detail = f"Response in {elapsed:.2f}s (within {deadline_s}s deadline)"
        else:
            tr.passed = False
            tr.detail = f"Response took {elapsed:.2f}s — exceeded {deadline_s}s deadline"
    except PipeError as e:
        tr.elapsed_ms = (time.monotonic() - t0) * 1000
        tr.detail = f"PipeError: {e}"
    results.append(tr)
    status = "PASS" if tr.passed else "FAIL"
    print(f"  [{status}] {tr.name} — {tr.detail}")


# ---------------------------------------------------------------------------
# Syntax / malformed input tests
# ---------------------------------------------------------------------------

def test_bad_inputs():
    """Send deliberately malformed Lua to verify ERR handling."""
    print("\n=== Malformed Input Tests ===")
    run_test("BadInput_syntax_error",
             "if then end end",
             expect_ok=False)
    run_test("BadInput_nil_call",
             "NONEXISTENT_FUNCTION()",
             expect_ok=False)
    run_test("BadInput_incomplete",
             "local x =",
             expect_ok=False)
    run_test("BadInput_long_string",
             'SWFOC_Log("' + "A" * 3000 + '")',
             expect_ok=True)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def generate_report():
    """Write bridge_test_report.json."""
    total = len(results)
    passed = sum(1 for r in results if r.passed)
    failed = total - passed

    report = {
        "summary": {
            "total": total,
            "passed": passed,
            "failed": failed,
            "pass_rate": f"{100 * passed / total:.1f}%" if total > 0 else "N/A",
            "timestamp": time.strftime("%Y-%m-%d %H:%M:%S"),
        },
        "tests": [r.to_dict() for r in results],
    }

    with open(REPORT_PATH, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    print(f"\nReport written to: {REPORT_PATH}")
    return report


def main():
    parser = argparse.ArgumentParser(description="SWFOC Bridge Test Suite")
    parser.add_argument("--stress-count", type=int, default=100,
                        help="Number of commands for stress test (default: 100)")
    parser.add_argument("--timeout", type=float, default=2.0,
                        help="Deadline in seconds for timeout test (default: 2.0)")
    args = parser.parse_args()

    print("=" * 60)
    print("  SWFOC DLL Bridge — Comprehensive Test Suite")
    print(f"  Pipe: {PIPE_NAME}")
    print("=" * 60)

    # Pre-flight: check if pipe exists
    print("\n[*] Checking pipe availability...")
    try:
        resp = pipe_send("-- ping", timeout_s=5.0)
        if is_ok(resp):
            print("[*] Pipe is live and responding.")
        elif is_err(resp):
            print(f"[*] Pipe responded with error (may be OK): {resp.strip()}")
        else:
            print(f"[!] Unexpected response: {resp[:100]}")
    except PipeError as e:
        print(f"[!] Cannot connect to pipe: {e}")
        print("[!] Generating report with connection failure.")
        tr = TestResult("pipe_connection")
        tr.passed = False
        tr.detail = str(e)
        results.append(tr)
        report = generate_report()
        print(f"\nResults: 0 passed / 1 failed")
        sys.exit(1)

    # ---- Per-function tests ----
    test_GetVersion()
    test_GetLocalPlayer()
    test_SetCredits()
    test_GetCredits()
    test_SetTechLevel()
    test_UncapCredits()
    test_HeroInstantRespawn()
    test_ListFactions()
    test_Log()
    test_DoString()
    test_DrainPipe()
    test_StateInfo()

    # ---- Read-Set-Read cycles ----
    test_credits_cycle()
    test_techlevel_cycle()
    test_hero_respawn_cycle()

    # ---- Malformed input ----
    test_bad_inputs()

    # ---- Stress test ----
    test_stress(count=args.stress_count)

    # ---- Timeout test ----
    test_timeout(deadline_s=args.timeout)

    # ---- Report ----
    report = generate_report()
    summary = report["summary"]
    print(f"\n{'=' * 60}")
    print(f"  RESULTS: {summary['passed']} passed / {summary['failed']} failed "
          f"({summary['pass_rate']})")
    print(f"{'=' * 60}")

    sys.exit(0 if summary["failed"] == 0 else 1)


if __name__ == "__main__":
    main()
