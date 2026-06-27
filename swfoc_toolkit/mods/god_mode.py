"""
God Mode mod -- prevents HP reduction for the human player's units.

Injects a code cave at the HP setter entry (RVA 0x3A89D0) that:
  1. Checks if the target object belongs to the protected player
  2. Walks up the parent chain for sub-objects (hardpoints)
  3. Blocks any HP decrease (allows HP increases like healing)
  4. Lets all other players' objects take damage normally

This is a direct Python port of the proven SWFOC_GodMode.CT Auto Assembler
script, parameterized so the owner ID can be set at runtime.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Optional

from .base import ModBase
from ..structs import RVAS

if TYPE_CHECKING:
    from ..engine import CEBridge


class GodModeMod(ModBase):
    """Faction-aware god mode with sub-object parent traversal."""

    name = "God Mode"
    description = (
        "Prevents HP reduction for the protected player's units. "
        "Handles hardpoint sub-objects by walking up to the parent."
    )

    def __init__(self, bridge: "CEBridge", owner_id: Optional[int] = None):
        super().__init__(bridge)
        self._owner_id = owner_id  # Will be auto-detected if None

    @property
    def owner_id(self) -> int:
        """The player slot ID being protected."""
        return self._owner_id if self._owner_id is not None else -1

    @owner_id.setter
    def owner_id(self, value: int) -> None:
        """Change the protected player.  If currently enabled, re-injects."""
        was_enabled = self._enabled
        if was_enabled:
            self.disable()
        self._owner_id = value
        if was_enabled:
            self.enable()

    # ------------------------------------------------------------------
    # Auto-detect owner
    # ------------------------------------------------------------------

    def _detect_owner(self) -> int:
        """Try to auto-detect the human player's owner ID.

        Uses a Lua scan in CE to find a GameObjectClass instance and
        read its owner_id field.  Falls back to a conservative default.
        """
        lua = """
local base = getAddress("StarWarsG.exe")
if not base then return "10" end

-- Try reading the PlayerListClass global
local plPtr = readQword(base + 0xA16FD0)
if not plPtr or plPtr == 0 then return "10" end
local count = readInteger(plPtr + 0x28)
if not count or count < 2 then return "10" end

-- Scan for a GameObjectClass with non-trivial HP to find a real unit
local ms = createMemScan()
ms.firstScan(soMoreThan, vtSingle, rtRounded, "10", nil,
    0, 0x7FFFFFFFFFFFFFFF, "+W-C", fsmNotAligned, "1", false, false, false, false)
ms.waitTillDone()
local fl = createFoundList(ms)
fl.initialize()

for i = 0, math.min(fl.getCount()-1, 5000) do
    local addr = tonumber("0x" .. fl.getAddress(i))
    if addr then
        local ob = addr - 0x5C
        local vt = readQword(ob)
        if vt and vt > base and vt < base + 0xCC0000 then
            local cls = getRTTIClassName(ob)
            if cls == "GameObjectClass" then
                local owner = readInteger(ob + 0x58)
                if owner and owner > 0 and owner < count then
                    fl.destroy()
                    ms.destroy()
                    return tostring(owner)
                end
            end
        end
    end
end

fl.destroy()
ms.destroy()
return "10"
"""
        result = self._bridge.evaluate_lua(lua)
        if isinstance(result, dict):
            raw = result.get("value", result.get("result", "10"))
        else:
            raw = str(result)
        try:
            return int(raw)
        except (ValueError, TypeError):
            return 10

    # ------------------------------------------------------------------
    # Enable / Disable
    # ------------------------------------------------------------------

    def _do_enable(self) -> None:
        if self._owner_id is None:
            self._owner_id = self._detect_owner()

        enable_script = self._build_enable_script(self._owner_id)
        self._auto_assemble(enable_script)

    def _do_disable(self) -> None:
        self._auto_assemble(self._build_disable_script())

    # ------------------------------------------------------------------
    # Auto Assembler generation
    # ------------------------------------------------------------------

    @staticmethod
    def _build_enable_script(owner_id: int) -> str:
        """Generate the [ENABLE] Auto Assembler script.

        This creates a code cave at the HP setter (RVA 0x3A89D0) that:
        - Resolves sub-object -> parent via +0x335 / +0x278
        - Compares owner_id against the protected player
        - Blocks HP decreases for the protected player
        - Falls through to the original for everyone else
        """
        return f"""\
[ENABLE]
alloc(godcave,512,StarWarsG.exe+3A89D0)
alloc(ownerCfg,4)
registersymbol(ownerCfg)
label(resolve_parent)
label(check_owner)
label(doprotect)
label(original)
label(ret_orig)

ownerCfg:
  dd {owner_id}

godcave:
  push rax
  push rdx
  // Check if this is a sub-object (parent index != 0xFF)
  movzx eax,byte ptr [rcx+335]
  cmp al,FF
  je check_owner
resolve_parent:
  // Walk to parent: components[parent_index] -> read owner from parent
  mov rdx,[rcx+278]
  test rdx,rdx
  jz check_owner
  movzx eax,al
  mov rdx,[rdx+rax*8]
  test rdx,rdx
  jz check_owner
  // Read owner_id from parent object
  mov eax,[rdx+58]
  jmp short @f
check_owner:
  // Read owner_id from this object directly
  mov eax,[rcx+58]
@@:
  cmp eax,[ownerCfg]
  pop rdx
  pop rax
  jne original
doprotect:
  // Protected player: only allow HP increases, block decreases
  // xmm1 = proposed new HP, [rcx+5C] = current HP
  movss xmm0,[rcx+5C]
  comiss xmm0,xmm1
  // If current >= proposed (i.e. damage), skip the setter entirely
  jna original
  ret
original:
  // Original prologue bytes: push rbx; sub rsp,60h
  push rbx
  sub rsp,60
  jmp ret_orig

StarWarsG.exe+3A89D0:
  jmp godcave
  nop
ret_orig:

[DISABLE]
"""

    @staticmethod
    def _build_disable_script() -> str:
        """Generate the [DISABLE] Auto Assembler script.

        Restores the original 6 bytes at the HP setter entry and frees
        the code cave.
        """
        return """\
[ENABLE]
// Restore original bytes: push rbx (40 53); sub rsp,60h (48 83 EC 60)
StarWarsG.exe+3A89D0:
  db 40 53 48 83 EC 60

dealloc(godcave)
dealloc(ownerCfg)
unregistersymbol(ownerCfg)

[DISABLE]
"""
