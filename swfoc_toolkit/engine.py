"""
CE Bridge connection layer.

Wraps the Cheat Engine MCP Bridge named pipe protocol into a clean Python API
that the rest of the toolkit uses for all memory access.
"""

from __future__ import annotations

import json
import struct
import time
from typing import Any, Dict, List, Optional, Union


# Default pipe name matching the CE MCP Bridge v11/v99
PIPE_PATH = r"\\.\pipe\CE_MCP_Bridge_v99"


class CEBridge:
    """Client for the Cheat Engine MCP Bridge named pipe.

    Usage::

        bridge = CEBridge()
        bridge.connect()
        val = bridge.read_integer("StarWarsG.exe+5C", "float")
        bridge.close()
    """

    def __init__(self, pipe_path: str = PIPE_PATH):
        self.pipe_path = pipe_path
        self._handle = None
        self._module_base: Optional[int] = None
        self._module_name: Optional[str] = None

    # ------------------------------------------------------------------
    # Connection management
    # ------------------------------------------------------------------

    def connect(self) -> None:
        """Open the named pipe to Cheat Engine.  Raises ConnectionError on failure."""
        import win32file
        import pywintypes

        try:
            self._handle = win32file.CreateFile(
                self.pipe_path,
                win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                0,
                None,
                win32file.OPEN_EXISTING,
                0,
                None,
            )
        except pywintypes.error as exc:
            raise ConnectionError(
                f"CE Bridge pipe not found at {self.pipe_path}. "
                f"Is Cheat Engine running with the MCP Bridge script? ({exc})"
            ) from exc

        # Probe the process to cache the module base
        info = self.get_process_info()
        if isinstance(info, dict) and info.get("success") is not False:
            self._module_name = info.get("name", "StarWarsG.exe")

    def close(self) -> None:
        """Close the pipe handle."""
        if self._handle is not None:
            import win32file
            try:
                win32file.CloseHandle(self._handle)
            except Exception:
                pass
            self._handle = None

    @property
    def connected(self) -> bool:
        return self._handle is not None

    # ------------------------------------------------------------------
    # Low-level RPC
    # ------------------------------------------------------------------

    def _send(self, method: str, params: Optional[Dict[str, Any]] = None) -> Any:
        """Send a JSON-RPC request over the pipe and return the result."""
        import win32file

        if self._handle is None:
            raise ConnectionError("Not connected. Call connect() first.")

        request = {
            "jsonrpc": "2.0",
            "method": method,
            "params": params or {},
            "id": int(time.time() * 1000),
        }

        req_bytes = json.dumps(request).encode("utf-8")
        header = struct.pack("<I", len(req_bytes))

        try:
            win32file.WriteFile(self._handle, header)
            win32file.WriteFile(self._handle, req_bytes)

            resp_hdr = win32file.ReadFile(self._handle, 4)[1]
            if len(resp_hdr) < 4:
                self.close()
                raise ConnectionError("Incomplete response header from CE Bridge.")
            resp_len = struct.unpack("<I", resp_hdr)[0]
            if resp_len > 16 * 1024 * 1024:
                self.close()
                raise ConnectionError(f"Response too large ({resp_len} bytes).")

            resp_body = win32file.ReadFile(self._handle, resp_len)[1]
            response = json.loads(resp_body.decode("utf-8"))

            if "error" in response:
                return {"success": False, "error": str(response["error"])}
            return response.get("result", response)

        except Exception as exc:
            # If the pipe broke, reset so a future connect() can retry
            self.close()
            raise ConnectionError(f"Pipe communication failed: {exc}") from exc

    # ------------------------------------------------------------------
    # Reconnecting wrapper
    # ------------------------------------------------------------------

    def send(self, method: str, params: Optional[Dict[str, Any]] = None) -> Any:
        """Send with one automatic reconnect attempt on pipe failure."""
        try:
            return self._send(method, params)
        except ConnectionError:
            # Try reconnecting once
            try:
                self.connect()
                return self._send(method, params)
            except ConnectionError:
                raise

    # ------------------------------------------------------------------
    # Process introspection
    # ------------------------------------------------------------------

    def get_process_info(self) -> Dict[str, Any]:
        return self._send("get_process_info")

    def get_module_base(self) -> int:
        """Return the base address of StarWarsG.exe (cached)."""
        if self._module_base is None:
            result = self.send("get_symbol_address", {"symbol": "StarWarsG.exe"})
            if isinstance(result, dict) and "address" in result:
                self._module_base = int(result["address"], 16) if isinstance(result["address"], str) else result["address"]
            else:
                raise RuntimeError(f"Could not resolve module base: {result}")
        return self._module_base

    # ------------------------------------------------------------------
    # Memory reading helpers
    # ------------------------------------------------------------------

    def read_u8(self, address: str) -> int:
        """Read an unsigned byte."""
        r = self.send("read_integer", {"address": address, "type": "byte"})
        if isinstance(r, dict) and "value" in r:
            return r["value"]
        return int(r) if not isinstance(r, dict) else 0

    def read_i32(self, address: str) -> int:
        """Read a signed 32-bit integer."""
        r = self.send("read_integer", {"address": address, "type": "dword"})
        if isinstance(r, dict) and "value" in r:
            v = r["value"]
            # Convert unsigned dword to signed int32
            if v >= 0x80000000:
                v -= 0x100000000
            return v
        return int(r) if not isinstance(r, dict) else 0

    def read_u32(self, address: str) -> int:
        """Read an unsigned 32-bit integer."""
        r = self.send("read_integer", {"address": address, "type": "dword"})
        if isinstance(r, dict) and "value" in r:
            return r["value"]
        return int(r) if not isinstance(r, dict) else 0

    def read_u64(self, address: str) -> int:
        """Read an unsigned 64-bit integer (pointer)."""
        r = self.send("read_integer", {"address": address, "type": "qword"})
        if isinstance(r, dict) and "value" in r:
            return r["value"]
        return int(r) if not isinstance(r, dict) else 0

    def read_float(self, address: str) -> float:
        """Read a 32-bit float."""
        r = self.send("read_integer", {"address": address, "type": "float"})
        if isinstance(r, dict) and "value" in r:
            return float(r["value"])
        return float(r) if not isinstance(r, dict) else 0.0

    def read_ptr(self, address: str) -> int:
        """Read a 64-bit pointer value."""
        return self.read_u64(address)

    def read_string(self, address: str, max_length: int = 256, wide: bool = False) -> str:
        """Read a null-terminated string from memory."""
        r = self.send("read_string", {
            "address": address,
            "max_length": max_length,
            "wide": wide,
        })
        if isinstance(r, dict) and "value" in r:
            return r["value"]
        return str(r) if not isinstance(r, dict) else ""

    def read_bytes(self, address: str, size: int) -> bytes:
        """Read raw bytes from memory."""
        r = self.send("read_memory", {"address": address, "size": size})
        if isinstance(r, dict) and "data" in r:
            data = r["data"]
            if isinstance(data, str):
                return bytes.fromhex(data.replace(" ", ""))
            if isinstance(data, list):
                return bytes(data)
        return b""

    # ------------------------------------------------------------------
    # Memory writing helpers
    # ------------------------------------------------------------------

    def write_u8(self, address: str, value: int) -> Any:
        return self.send("write_integer", {"address": address, "value": value, "type": "byte"})

    def write_i32(self, address: str, value: int) -> Any:
        return self.send("write_integer", {"address": address, "value": value, "type": "dword"})

    def write_float(self, address: str, value: float) -> Any:
        return self.send("write_integer", {"address": address, "value": value, "type": "float"})

    def write_bytes(self, address: str, data: List[int]) -> Any:
        return self.send("write_memory", {"address": address, "bytes": data})

    # ------------------------------------------------------------------
    # Pointer chain
    # ------------------------------------------------------------------

    def read_pointer_chain(self, base: str, offsets: List[int]) -> Dict[str, Any]:
        """Follow a multi-level pointer chain.  Returns the bridge response dict."""
        return self.send("read_pointer_chain", {"base": base, "offsets": offsets})

    # ------------------------------------------------------------------
    # Scanning
    # ------------------------------------------------------------------

    def aob_scan(self, pattern: str, protection: str = "+X", limit: int = 100) -> List[str]:
        """Scan for an array-of-bytes pattern.  Returns list of hex address strings."""
        r = self.send("aob_scan", {"pattern": pattern, "protection": protection, "limit": limit})
        if isinstance(r, dict):
            if "addresses" in r:
                return r["addresses"]
            if "results" in r:
                return r["results"]
        if isinstance(r, list):
            return r
        return []

    def scan_value(self, value: str, scan_type: str = "exact", protection: str = "+W-C") -> Any:
        """Run a first-scan for a value."""
        return self.send("scan_all", {"value": value, "type": scan_type, "protection": protection})

    def get_scan_results(self, max_results: int = 100) -> List[str]:
        """Retrieve addresses from the last scan."""
        r = self.send("get_scan_results", {"max": max_results})
        if isinstance(r, dict) and "addresses" in r:
            return r["addresses"]
        if isinstance(r, list):
            return r
        return []

    # ------------------------------------------------------------------
    # Auto Assembler
    # ------------------------------------------------------------------

    def auto_assemble(self, script: str) -> Any:
        """Execute an Auto Assembler script (code caves, hooks, etc.)."""
        return self.send("auto_assemble", {"script": script})

    # ------------------------------------------------------------------
    # Lua execution
    # ------------------------------------------------------------------

    def evaluate_lua(self, code: str) -> Any:
        """Execute Lua code inside Cheat Engine and return the result."""
        return self.send("evaluate_lua", {"code": code})

    # ------------------------------------------------------------------
    # RTTI
    # ------------------------------------------------------------------

    def get_rtti_classname(self, address: str) -> str:
        """Get the RTTI class name for the object at the given address."""
        r = self.send("get_rtti_classname", {"address": address})
        if isinstance(r, dict) and "classname" in r:
            return r["classname"]
        if isinstance(r, str):
            return r
        return ""

    # ------------------------------------------------------------------
    # Symbol resolution
    # ------------------------------------------------------------------

    def get_symbol_address(self, symbol: str) -> Optional[int]:
        """Resolve a symbol like 'StarWarsG.exe+0x5C' to an absolute address."""
        r = self.send("get_symbol_address", {"symbol": symbol})
        if isinstance(r, dict) and "address" in r:
            addr = r["address"]
            return int(addr, 16) if isinstance(addr, str) else int(addr)
        return None

    # ------------------------------------------------------------------
    # Address formatting
    # ------------------------------------------------------------------

    def addr(self, rva_or_abs: int) -> str:
        """Format an absolute address as a hex string for CE commands."""
        return hex(rva_or_abs)

    def rva_to_va(self, rva: int) -> int:
        """Convert a module-relative RVA to an absolute virtual address."""
        return self.get_module_base() + rva

    def va(self, rva: int) -> str:
        """Shorthand: RVA -> absolute address as hex string."""
        return hex(self.rva_to_va(rva))

    # ------------------------------------------------------------------
    # Context manager
    # ------------------------------------------------------------------

    def __enter__(self):
        self.connect()
        return self

    def __exit__(self, *exc):
        self.close()
        return False
