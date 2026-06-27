"""
Unit enumeration, filtering, and bulk operations.

Finds all live GameObjectClass instances in the running game by scanning
memory for objects whose vtable matches the known GameObjectClass vtable RVA,
then wraps them in GameObject instances.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Callable, Dict, List, Optional

from .game_objects import GameObject
from .structs import GOC

if TYPE_CHECKING:
    from .engine import CEBridge


class UnitEnumerator:
    """Discovers and filters game objects in a live game session.

    The primary strategy is a Lua-driven scan inside CE that finds objects
    by RTTI class name.  This is faster than pure pipe-based memory
    scanning because the Lua runs locally inside CE.
    """

    def __init__(self, bridge: "CEBridge"):
        self._b = bridge

    # ------------------------------------------------------------------
    # Core enumeration
    # ------------------------------------------------------------------

    def enumerate_all(self, max_objects: int = 2000) -> List[GameObject]:
        """Find all GameObjectClass instances in the running game.

        Uses a CE Lua scan for float values in writable memory, then
        validates each candidate by checking its RTTI class name.

        This is the brute-force approach.  For production use, hooking
        a function like SetHP or iterating known lists is more efficient.
        """
        return self._enumerate_via_lua(max_objects)

    def _enumerate_via_lua(self, max_objects: int) -> List[GameObject]:
        """Use CE Lua to scan for GameObjectClass instances.

        Strategy: Scan for pointers whose RTTI resolves to
        "GameObjectClass", reading the vtable RVA for validation.
        """
        base = self._b.get_module_base()
        vtable_va = base + GOC.VTABLE_RVA

        # CE Lua script that scans for objects with the GameObjectClass vtable.
        # It scans a targeted memory range for qword values matching the vtable VA
        # and validates each candidate.
        lua_script = f"""
local vtable_target = {vtable_va}
local base = getAddress("StarWarsG.exe")
local results = {{}}
local count = 0
local max = {max_objects}

-- Scan writable memory for the vtable pointer value
local ms = createMemScan()
ms.firstScan(soExactValue, vtByteArray, rtRounded,
    string.format("%016X", vtable_target),
    nil, 0, 0x7FFFFFFFFFFFFFFF, "+W-C", fsmNotAligned, "8", false, false, false, false)
ms.waitTillDone()
local fl = createFoundList(ms)
fl.initialize()

for i = 0, fl.getCount() - 1 do
    if count >= max then break end
    local addr = tonumber("0x" .. fl.getAddress(i))
    if addr then
        -- Verify RTTI
        local cls = getRTTIClassName(addr)
        if cls == "GameObjectClass" then
            count = count + 1
            results[count] = string.format("0x%X", addr)
        end
    end
end

fl.destroy()
ms.destroy()

return table.concat(results, ",")
"""
        result = self._b.evaluate_lua(lua_script)

        # Parse the comma-separated hex addresses
        addresses = self._parse_address_list(result)

        return [GameObject(self._b, addr) for addr in addresses]

    def _enumerate_via_scan(self, max_objects: int) -> List[GameObject]:
        """Fallback: use the pipe-based scanner to find GameObjectClass instances.

        Scans for the vtable pointer value in writable memory.
        """
        base = self._b.get_module_base()
        vtable_va = base + GOC.VTABLE_RVA

        # Scan for the vtable pointer as a byte array
        vtable_bytes = vtable_va.to_bytes(8, "little")
        pattern = " ".join(f"{b:02X}" for b in vtable_bytes)

        self._b.scan_value(pattern, scan_type="array", protection="+W-C")
        addresses_raw = self._b.get_scan_results(max_results=max_objects)

        objects: List[GameObject] = []
        for addr_str in addresses_raw:
            addr = int(addr_str, 16) if isinstance(addr_str, str) else int(addr_str)
            # The scan found the vtable pointer, so addr IS the object base
            obj = GameObject(self._b, addr)
            if obj.validate_rtti():
                objects.append(obj)

        return objects

    # ------------------------------------------------------------------
    # Filtered enumeration
    # ------------------------------------------------------------------

    def enumerate_by_owner(self, owner_id: int, max_objects: int = 2000) -> List[GameObject]:
        """Return only objects owned by a specific player slot."""
        return [
            obj for obj in self.enumerate_all(max_objects)
            if obj.owner_id == owner_id
        ]

    def enumerate_by_type(self, type_name: str, max_objects: int = 2000) -> List[GameObject]:
        """Return only objects whose type name contains *type_name* (case-insensitive)."""
        needle = type_name.upper()
        return [
            obj for obj in self.enumerate_all(max_objects)
            if needle in obj.type_name.upper()
        ]

    def enumerate_top_level(self, max_objects: int = 2000) -> List[GameObject]:
        """Return only top-level objects (not sub-objects / hardpoints)."""
        return [
            obj for obj in self.enumerate_all(max_objects)
            if not obj.is_subobject
        ]

    def find_by_id(self, object_id: int, max_objects: int = 2000) -> Optional[GameObject]:
        """Find a single object by its unique Object ID."""
        for obj in self.enumerate_all(max_objects):
            if obj.object_id == object_id:
                return obj
        return None

    def filter(
        self,
        predicate: Callable[[GameObject], bool],
        max_objects: int = 2000,
    ) -> List[GameObject]:
        """Return objects matching an arbitrary predicate function."""
        return [obj for obj in self.enumerate_all(max_objects) if predicate(obj)]

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _parse_address_list(result) -> List[int]:
        """Parse the Lua return value into a list of integer addresses."""
        if isinstance(result, dict):
            # The bridge may wrap the Lua return in {"value": "..."} or {"result": "..."}
            raw = result.get("value", result.get("result", ""))
        elif isinstance(result, str):
            raw = result
        else:
            return []

        if not raw or not isinstance(raw, str):
            return []

        addresses: List[int] = []
        for part in raw.split(","):
            part = part.strip()
            if part:
                try:
                    addresses.append(int(part, 16))
                except ValueError:
                    continue
        return addresses

    # ------------------------------------------------------------------
    # Summary
    # ------------------------------------------------------------------

    def print_summary(self, objects: Optional[List[GameObject]] = None) -> None:
        """Print a formatted summary table of game objects."""
        if objects is None:
            objects = self.enumerate_all()

        print(f"\n{'='*80}")
        print(f"  Game Objects: {len(objects)} found")
        print(f"{'='*80}")
        print(f"  {'Address':>18s}  {'ID':>6s}  {'Owner':>5s}  {'HP':>10s}  {'Sub?':>4s}  Type")
        print(f"  {'-'*18}  {'-'*6}  {'-'*5}  {'-'*10}  {'-'*4}  {'-'*30}")

        for obj in objects:
            sub = "yes" if obj.is_subobject else ""
            print(
                f"  0x{obj.address:016X}  {obj.object_id:6d}  {obj.owner_id:5d}  "
                f"{obj.hp:10.1f}  {sub:>4s}  {obj.type_name}"
            )
        print()
