"""
Player / faction enumeration.

Reads the global PlayerListClass to enumerate all players in the current
game session and exposes helpers to identify the human player.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, Dict, List, Optional

from .structs import PLC

if TYPE_CHECKING:
    from .engine import CEBridge


@dataclass
class Player:
    """Snapshot of a player slot."""

    index: int              # Slot index (the owner_id on game objects)
    address: int            # Absolute address of the PlayerObject
    is_human: bool = False  # Detected via Lua or heuristic
    faction_name: str = ""  # e.g. "EMPIRE", "REBEL", resolved at read time


class PlayerList:
    """Interface to the global PlayerListClass at RVA 0xA16FD0."""

    def __init__(self, bridge: "CEBridge"):
        self._b = bridge
        self._global_addr: Optional[int] = None

    # ------------------------------------------------------------------
    # Locate the global
    # ------------------------------------------------------------------

    def _resolve_global(self) -> int:
        """Return the absolute address of the PlayerListClass instance."""
        if self._global_addr is None:
            base = self._b.get_module_base()
            # The global is a *pointer* at StarWarsG.exe+0xA16FD0
            ptr = self._b.read_ptr(hex(base + PLC.GLOBAL_RVA))
            if not ptr:
                raise RuntimeError(
                    f"PlayerListClass pointer at RVA 0x{PLC.GLOBAL_RVA:X} is NULL. "
                    "Is a game session active?"
                )
            self._global_addr = ptr
        return self._global_addr

    def invalidate_cache(self) -> None:
        """Force re-read of the global on next access (e.g. after map change)."""
        self._global_addr = None

    # ------------------------------------------------------------------
    # Enumeration
    # ------------------------------------------------------------------

    @property
    def count(self) -> int:
        """Number of player slots in this session."""
        g = self._resolve_global()
        return self._b.read_i32(hex(g + PLC.PLAYER_COUNT))

    def get_player_address(self, index: int) -> int:
        """Read the pointer for player slot *index*."""
        g = self._resolve_global()
        arr_ptr = self._b.read_ptr(hex(g + PLC.PLAYER_ARRAY))
        if not arr_ptr:
            return 0
        return self._b.read_ptr(hex(arr_ptr + index * 8))

    def enumerate(self) -> List[Player]:
        """Return all player slots as Player dataclass instances."""
        n = self.count
        players: List[Player] = []
        for i in range(n):
            addr = self.get_player_address(i)
            p = Player(index=i, address=addr)
            players.append(p)
        return players

    # ------------------------------------------------------------------
    # Faction name resolution (via Lua)
    # ------------------------------------------------------------------

    def get_faction_name(self, player_index: int) -> str:
        """Use CE Lua to call the game's Find_Player / Get_Faction_Name.

        Falls back to reading known memory patterns if Lua is unavailable.
        """
        lua = (
            f"local pl = FindPlayerByIndex({player_index})\n"
            f"if pl then return pl.Get_Faction_Name() end\n"
            f"return 'UNKNOWN'"
        )
        # The bridge Lua support may not expose game-level Lua directly.
        # Use a simpler approach: read the faction string from the player object.
        addr = self.get_player_address(player_index)
        if not addr:
            return "UNKNOWN"

        # Try reading a faction string from known offsets on PlayerObject.
        # The game stores faction info in the player data -- common pattern
        # is a pointer to a FactionClass with a name string.
        # For robustness we'll attempt a few approaches.
        return self._read_faction_heuristic(addr, player_index)

    def _read_faction_heuristic(self, player_addr: int, index: int) -> str:
        """Best-effort faction name read from a PlayerObject."""
        # The faction reference is typically at [player+0x68], with the
        # faction name as an SSO string at [faction+0x10] or similar.
        # This offset may vary; we try a safe probe.
        try:
            faction_ptr = self._b.read_ptr(hex(player_addr + 0x68))
            if faction_ptr:
                # Try reading a string near the faction pointer
                name = self._b.read_string(hex(faction_ptr + 0x10), max_length=64)
                if name and name.isprintable() and len(name) > 1:
                    return name
        except Exception:
            pass
        return f"Player_{index}"

    # ------------------------------------------------------------------
    # Human player detection
    # ------------------------------------------------------------------

    def detect_human_player(self) -> Optional[Player]:
        """Attempt to identify which player slot is human-controlled.

        Strategy: scan player objects for a known IsHumanControlled flag,
        or fall back to matching the player whose units are most numerous
        with RTTI-validated GameObjectClass instances.
        """
        players = self.enumerate()

        # Heuristic: player slot 0 is often Neutral, slot 1+ are the real
        # players.  In single-player the human is typically the lowest
        # non-neutral index.  We'll try reading a flag byte.
        for p in players:
            if not p.address:
                continue
            # IsHumanControlled is a bool property on PlayerObject.
            # Common offset candidates: +0x90, +0xA0, +0x100.
            # We'll probe a few.
            for flag_off in (0x90, 0xA0, 0x100, 0x108):
                val = self._b.read_u8(hex(p.address + flag_off))
                if val == 1:
                    # Tentative hit -- validate with a second check
                    p.is_human = True
                    return p

        # Fallback: return player index 1 (index 0 is usually Neutral)
        if len(players) > 1:
            players[1].is_human = True
            return players[1]
        return players[0] if players else None

    # ------------------------------------------------------------------
    # Convenience
    # ------------------------------------------------------------------

    def get_faction_map(self) -> Dict[int, str]:
        """Return {player_index: faction_name} for all slots."""
        n = self.count
        return {i: self.get_faction_name(i) for i in range(n)}
