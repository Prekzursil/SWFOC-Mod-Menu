"""
AOB scanner and version detection.

Provides version-independent address resolution by scanning for byte patterns
rather than relying on hardcoded RVAs that break across game patches.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Dict, List, Optional

from .structs import AOBS, RVAS

if TYPE_CHECKING:
    from .engine import CEBridge


@dataclass
class ResolvedAddresses:
    """Collection of resolved absolute addresses for key game functions."""

    module_base: int = 0
    hp_setter: int = 0
    take_damage: int = 0
    player_list_global: int = 0

    def __repr__(self) -> str:
        lines = [
            f"  module_base         = 0x{self.module_base:X}",
            f"  hp_setter           = 0x{self.hp_setter:X}",
            f"  take_damage         = 0x{self.take_damage:X}",
            f"  player_list_global  = 0x{self.player_list_global:X}",
        ]
        return "ResolvedAddresses(\n" + "\n".join(lines) + "\n)"


class GameScanner:
    """AOB-based address resolver with RVA fallback.

    Usage::

        scanner = GameScanner(bridge)
        addrs = scanner.resolve_all()
        print(f"HP setter at 0x{addrs.hp_setter:X}")
    """

    def __init__(self, bridge: "CEBridge"):
        self._b = bridge
        self._resolved: Optional[ResolvedAddresses] = None

    @property
    def addresses(self) -> ResolvedAddresses:
        """Lazily resolve and cache all addresses."""
        if self._resolved is None:
            self._resolved = self.resolve_all()
        return self._resolved

    def resolve_all(self) -> ResolvedAddresses:
        """Resolve all key addresses, preferring AOB scans over hardcoded RVAs.

        Falls back to RVA-based resolution when AOB patterns are empty or
        produce no results.
        """
        base = self._b.get_module_base()
        result = ResolvedAddresses(module_base=base)

        # HP setter
        result.hp_setter = self._resolve_one(
            "HP setter",
            aob_pattern=AOBS.HP_SETTER,
            fallback_rva=RVAS.HP_SETTER,
            scan_protection="+X",
        )

        # Take_Damage -- no AOB yet, use RVA
        result.take_damage = base + RVAS.TAKE_DAMAGE

        # PlayerListClass global -- RVA only for now
        result.player_list_global = base + RVAS.HP_SETTER  # placeholder
        result.player_list_global = base + 0xA16FD0

        return result

    def _resolve_one(
        self,
        name: str,
        aob_pattern: str,
        fallback_rva: int,
        scan_protection: str = "+X",
    ) -> int:
        """Try AOB first, fall back to hardcoded RVA."""
        base = self._b.get_module_base()

        if aob_pattern:
            hits = self._b.aob_scan(aob_pattern, protection=scan_protection, limit=10)
            if hits:
                # Filter to hits within the main module
                module_end = base + 0x1000000  # ~16 MB generous bound
                for h in hits:
                    addr = int(h, 16) if isinstance(h, str) else int(h)
                    if base <= addr < module_end:
                        return addr
                # If none matched within module, take first result anyway
                first = hits[0]
                return int(first, 16) if isinstance(first, str) else int(first)

        # Fallback to hardcoded RVA
        return base + fallback_rva

    # ------------------------------------------------------------------
    # Signature generation
    # ------------------------------------------------------------------

    def generate_signature(self, address: int) -> str:
        """Ask CE to generate a unique AOB signature for an address.

        Useful for creating new version-independent patterns.
        """
        r = self._b.send("generate_signature", {"address": hex(address)})
        if isinstance(r, dict) and "signature" in r:
            return r["signature"]
        return str(r)

    # ------------------------------------------------------------------
    # Version detection
    # ------------------------------------------------------------------

    def detect_version(self) -> str:
        """Attempt to identify the game version / mod version.

        Reads known version strings or checksums from the module.
        """
        base = self._b.get_module_base()

        # Try reading the PE version info or a known string
        # FoC + Thrawn's Revenge typically has identifiable strings
        for offset in (0x100, 0x200, 0x1000):
            s = self._b.read_string(hex(base + offset), max_length=128)
            if s and ("Star Wars" in s or "Empire" in s or "Thrawn" in s):
                return s

        # Fallback: read the PE timestamp
        pe_timestamp_off = 0x88  # Offset to PE header TimeDateStamp in typical PE
        try:
            pe_sig_off = self._b.read_u32(hex(base + 0x3C))  # e_lfanew
            timestamp = self._b.read_u32(hex(base + pe_sig_off + 8))
            return f"PE timestamp 0x{timestamp:08X}"
        except Exception:
            pass

        return "unknown"
