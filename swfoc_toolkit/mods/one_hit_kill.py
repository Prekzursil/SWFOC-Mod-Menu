"""
One-Hit Kill mod -- makes the protected player's attacks instantly lethal.

Injects a code cave at the HP setter (RVA 0x3A89D0) that sets HP to 0
for any object NOT owned by the protected player when HP is being decreased.

This can coexist with God Mode since both hook the same entry point,
but they should not be enabled simultaneously.  Use one or the other,
or combine them into a single code cave (see the combo note below).

Implementation note: Because both mods hook the same address, this mod
uses a separate approach -- it hooks the Take_Damage dispatch at
RVA 0x3A97E0 instead, so it can run alongside God Mode.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Optional

from .base import ModBase
from ..structs import RVAS

if TYPE_CHECKING:
    from ..engine import CEBridge


class OneHitKillMod(ModBase):
    """One-hit kill for the protected player's attacks against enemies."""

    name = "One-Hit Kill"
    description = (
        "Any damage dealt to enemy units (not owned by the protected player) "
        "is amplified to instantly kill them."
    )

    def __init__(self, bridge: "CEBridge", owner_id: Optional[int] = None):
        super().__init__(bridge)
        self._owner_id = owner_id

    @property
    def owner_id(self) -> int:
        return self._owner_id if self._owner_id is not None else -1

    @owner_id.setter
    def owner_id(self, value: int) -> None:
        was_enabled = self._enabled
        if was_enabled:
            self.disable()
        self._owner_id = value
        if was_enabled:
            self.enable()

    def _detect_owner(self) -> int:
        """Reuse the same detection logic as GodMode."""
        from .god_mode import GodModeMod
        detector = GodModeMod(self._bridge)
        return detector._detect_owner()

    # ------------------------------------------------------------------
    # Enable / Disable
    # ------------------------------------------------------------------

    def _do_enable(self) -> None:
        if self._owner_id is None:
            self._owner_id = self._detect_owner()

        self._auto_assemble(self._build_enable_script(self._owner_id))

    def _do_disable(self) -> None:
        self._auto_assemble(self._build_disable_script())

    # ------------------------------------------------------------------
    # Auto Assembler scripts
    # ------------------------------------------------------------------

    @staticmethod
    def _build_enable_script(owner_id: int) -> str:
        """Hook the Take_Damage property dispatch (RVA 0x3A97E0).

        Before the dispatch function processes damage, we check:
        - If the TARGET object is owned by the protected player -> do nothing
          (don't amplify damage to our own units)
        - If the TARGET is owned by anyone else -> set HP to 0 directly
          and skip the normal damage path

        The function at 0x3A97E0 receives the target GameObjectClass in RCX.
        Its prologue is: mov [rsp+10h],rbx (48 89 5C 24 10)
        That gives us 5 bytes to overwrite with a JMP.
        """
        return f"""\
[ENABLE]
alloc(ohkcave,512,StarWarsG.exe+3A97E0)
alloc(ohkOwner,4)
registersymbol(ohkOwner)
label(ohk_check)
label(ohk_kill)
label(ohk_original)
label(ohk_ret)

ohkOwner:
  dd {owner_id}

ohkcave:
  push rax
  push rdx
  // Resolve owner through parent chain (same logic as god mode)
  movzx eax,byte ptr [rcx+335]
  cmp al,FF
  je ohk_check
  // Sub-object: walk to parent
  mov rdx,[rcx+278]
  test rdx,rdx
  jz ohk_check
  movzx eax,al
  mov rdx,[rdx+rax*8]
  test rdx,rdx
  jz ohk_check
  mov eax,[rdx+58]
  jmp short @f
ohk_check:
  mov eax,[rcx+58]
@@:
  // If this object belongs to the protected player, don't kill it
  cmp eax,[ohkOwner]
  pop rdx
  pop rax
  je ohk_original
ohk_kill:
  // Enemy unit: set HP to 0 directly
  push rax
  xor eax,eax
  mov [rcx+5C],eax
  pop rax
  // Still run the original function so death events fire properly
ohk_original:
  // Restore original prologue: mov [rsp+10h],rbx
  mov [rsp+10],rbx
  jmp ohk_ret

StarWarsG.exe+3A97E0:
  jmp ohkcave
ohk_ret:

[DISABLE]
"""

    @staticmethod
    def _build_disable_script() -> str:
        """Restore original bytes at the Take_Damage dispatch entry."""
        return """\
[ENABLE]
// Restore: mov [rsp+10h],rbx = 48 89 5C 24 10
StarWarsG.exe+3A97E0:
  db 48 89 5C 24 10

dealloc(ohkcave)
dealloc(ohkOwner)
unregistersymbol(ohkOwner)

[DISABLE]
"""
