"""
Base class for runtime mods.

Every mod follows the same lifecycle:
  1. enable()   -- inject code / patch memory
  2. disable()  -- restore original bytes / remove hooks
  3. toggle()   -- flip between enabled / disabled
  4. is_enabled -- query current state
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import TYPE_CHECKING, Optional

if TYPE_CHECKING:
    from ..engine import CEBridge


class ModBase(ABC):
    """Abstract base for all runtime mods."""

    # Human-readable name shown in status output
    name: str = "Unnamed Mod"
    description: str = ""

    def __init__(self, bridge: "CEBridge"):
        self._bridge = bridge
        self._enabled = False

    # ------------------------------------------------------------------
    # Public interface
    # ------------------------------------------------------------------

    @property
    def is_enabled(self) -> bool:
        """True if this mod is currently active."""
        return self._enabled

    def enable(self) -> None:
        """Activate the mod.  Idempotent -- calling when already enabled is safe."""
        if self._enabled:
            return
        self._do_enable()
        self._enabled = True

    def disable(self) -> None:
        """Deactivate the mod.  Idempotent."""
        if not self._enabled:
            return
        self._do_disable()
        self._enabled = False

    def toggle(self) -> bool:
        """Flip the mod state.  Returns the new is_enabled value."""
        if self._enabled:
            self.disable()
        else:
            self.enable()
        return self._enabled

    # ------------------------------------------------------------------
    # Subclass hooks
    # ------------------------------------------------------------------

    @abstractmethod
    def _do_enable(self) -> None:
        """Subclasses implement the actual injection / patching here."""
        ...

    @abstractmethod
    def _do_disable(self) -> None:
        """Subclasses implement the cleanup / restoration here."""
        ...

    # ------------------------------------------------------------------
    # Helpers available to subclasses
    # ------------------------------------------------------------------

    def _auto_assemble(self, script: str) -> None:
        """Run an Auto Assembler script through the bridge.

        Raises RuntimeError if the script fails.
        """
        result = self._bridge.auto_assemble(script)
        if isinstance(result, dict) and result.get("success") is False:
            raise RuntimeError(
                f"Auto Assembler script failed for {self.name}: "
                f"{result.get('error', result)}"
            )

    def _read_original_bytes(self, address: str, count: int) -> bytes:
        """Read the original bytes at an address before patching."""
        return self._bridge.read_bytes(address, count)

    # ------------------------------------------------------------------
    # Repr
    # ------------------------------------------------------------------

    def __repr__(self) -> str:
        state = "ON" if self._enabled else "OFF"
        return f"<{self.__class__.__name__} [{state}] {self.name!r}>"
