#!/usr/bin/env python3
"""
Quick manual test: connect to SWFOC bridge pipe, send SWFOC_StateInfo(), print result.
Usage: python pipe_test_quick.py
"""

import sys
import time

PIPE_NAME = r"\\.\pipe\swfoc_bridge"


def send_command(lua_code: str, timeout_s: float = 5.0) -> str:
    """Send a null-terminated Lua command and read the response."""
    try:
        # On Windows, named pipes can be opened as regular files
        handle = open(PIPE_NAME, "r+b", buffering=0)
    except FileNotFoundError:
        return "CONNECT_FAIL: Pipe not found — is the game running with the bridge DLL?"
    except PermissionError:
        return "CONNECT_FAIL: Permission denied on pipe"
    except OSError as e:
        return f"CONNECT_FAIL: {e}"

    try:
        # Send null-terminated UTF-8
        handle.write(lua_code.encode("utf-8") + b"\x00")
        handle.flush()

        # Read response (bridge writes then disconnects)
        start = time.monotonic()
        chunks = []
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


def main():
    cmd = "return SWFOC_StateInfo()"
    if len(sys.argv) > 1:
        cmd = " ".join(sys.argv[1:])

    print(f"Pipe:    {PIPE_NAME}")
    print(f"Command: {cmd}")
    print("-" * 50)

    result = send_command(cmd)
    print(result)

    if result.startswith("CONNECT_FAIL"):
        print("\nThe bridge pipe is not available.")
        print("Make sure the game is running and the bridge DLL is injected.")
        sys.exit(1)
    elif result.startswith("OK"):
        print("\n[SUCCESS]")
    elif result.startswith("ERR"):
        print("\n[ERROR from bridge]")
    else:
        print(f"\n[Unexpected response format]")


if __name__ == "__main__":
    main()
