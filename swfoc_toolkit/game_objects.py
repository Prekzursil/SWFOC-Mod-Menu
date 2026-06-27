"""
GameObjectClass wrapper -- Pythonic access to live game object fields.

Each wrapper holds the absolute address of a GameObjectClass instance in the
running process and reads/writes fields through the CE Bridge on demand.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, List, Optional

from .structs import GOC, GOT

if TYPE_CHECKING:
    from .engine import CEBridge


class GameObject:
    """Lazy wrapper around a live GameObjectClass instance.

    Field reads go over the pipe each time -- no caching -- so the object
    always reflects the current game state.
    """

    def __init__(self, bridge: "CEBridge", address: int):
        self._b = bridge
        self._addr = address

    # ------------------------------------------------------------------
    # Address helpers
    # ------------------------------------------------------------------

    @property
    def address(self) -> int:
        """Absolute virtual address of this GameObjectClass."""
        return self._addr

    def _field(self, offset: int) -> str:
        """Return a hex address string for a field at *offset*."""
        return hex(self._addr + offset)

    # ------------------------------------------------------------------
    # Identity
    # ------------------------------------------------------------------

    @property
    def object_id(self) -> int:
        """Unique per-session object ID (int32 at +0x50)."""
        return self._b.read_i32(self._field(GOC.OBJECT_ID))

    @property
    def owner_id(self) -> int:
        """Player slot index that owns this object (int32 at +0x58)."""
        return self._b.read_i32(self._field(GOC.OWNER_PLAYER_ID))

    # ------------------------------------------------------------------
    # Health
    # ------------------------------------------------------------------

    @property
    def hp(self) -> float:
        """Current hit-points (float at +0x5C)."""
        return self._b.read_float(self._field(GOC.HP))

    @hp.setter
    def hp(self, value: float) -> None:
        self._b.write_float(self._field(GOC.HP), value)

    # ------------------------------------------------------------------
    # Invulnerability
    # ------------------------------------------------------------------

    @property
    def invulnerable(self) -> bool:
        """True if the invulnerability flag (+0x3A7) is set."""
        return self._b.read_u8(self._field(GOC.INVULNERABILITY)) != 0

    @invulnerable.setter
    def invulnerable(self, value: bool) -> None:
        self._b.write_u8(self._field(GOC.INVULNERABILITY), 1 if value else 0)

    # ------------------------------------------------------------------
    # Type name (MSVC SSO string resolution)
    # ------------------------------------------------------------------

    @property
    def type_name(self) -> str:
        """Resolve the unit/building type name string from GameObjectType."""
        type_ptr = self._b.read_ptr(self._field(GOC.GAME_OBJECT_TYPE))
        if not type_ptr:
            return "<no type>"
        return self._read_sso_string(type_ptr + GOT.TYPE_NAME_SSO)

    def _read_sso_string(self, sso_addr: int) -> str:
        """Read an MSVC SSO (Small String Optimization) std::string.

        Layout at *sso_addr*:
          - 16 bytes inline buffer  (or a char* if heap-allocated)
          - +0x10: size (8 bytes)
          - +0x18: capacity (8 bytes)

        If capacity >= 16 the string is heap-allocated and the first 8 bytes
        at *sso_addr* are a pointer to the char data.  Otherwise the inline
        buffer starting at *sso_addr* contains the characters directly.
        """
        capacity = self._b.read_u64(hex(sso_addr + GOT.SSO_CAPACITY_OFF))
        if capacity >= 16:
            # Heap-allocated: first qword is a pointer to the char array
            heap_ptr = self._b.read_ptr(hex(sso_addr))
            if not heap_ptr:
                return "<bad ptr>"
            return self._b.read_string(hex(heap_ptr), max_length=256)
        else:
            # Inline: chars sit directly at sso_addr
            return self._b.read_string(hex(sso_addr), max_length=16)

    # ------------------------------------------------------------------
    # Component / parent traversal
    # ------------------------------------------------------------------

    @property
    def is_subobject(self) -> bool:
        """True if this object is a hardpoint / sub-component (has a parent)."""
        idx = self._b.read_u8(self._field(GOC.PARENT_COMPONENT_IDX))
        return idx != 0xFF

    @property
    def parent_component_index(self) -> int:
        """Raw byte from +0x335.  0xFF means top-level."""
        return self._b.read_u8(self._field(GOC.PARENT_COMPONENT_IDX))

    def get_parent(self) -> Optional["GameObject"]:
        """Traverse to the parent GameObjectClass via QueryInterface(3).

        Uses the component lookup table at +0x332 and the component array
        at +0x278 to resolve the parent container.
        """
        # Read the component index for query type 3 (parent/container)
        idx = self._b.read_u8(hex(self._addr + GOC.COMPONENT_LOOKUP + GOC.QUERY_PARENT_CONTAINER))
        if idx == 0xFF:
            return None  # No parent

        comp_array_ptr = self._b.read_ptr(self._field(GOC.COMPONENT_ARRAY))
        if not comp_array_ptr:
            return None

        parent_ptr = self._b.read_ptr(hex(comp_array_ptr + idx * 8))
        if not parent_ptr:
            return None

        return GameObject(self._b, parent_ptr)

    def resolve_owner_id(self) -> int:
        """Walk up parent chain to find the true owner ID.

        Sub-objects (hardpoints) may have a stale or inherited owner_id.
        The real owner is on the top-level parent.
        """
        parent = self.get_parent()
        if parent is not None:
            return parent.resolve_owner_id()
        return self.owner_id

    def get_component(self, query_type: int) -> Optional["GameObject"]:
        """Generic component lookup via the table at +0x332."""
        idx = self._b.read_u8(hex(self._addr + GOC.COMPONENT_LOOKUP + query_type))
        if idx == 0xFF:
            return None

        comp_array_ptr = self._b.read_ptr(self._field(GOC.COMPONENT_ARRAY))
        if not comp_array_ptr:
            return None

        comp_ptr = self._b.read_ptr(hex(comp_array_ptr + idx * 8))
        if not comp_ptr:
            return None

        return GameObject(self._b, comp_ptr)

    # ------------------------------------------------------------------
    # RTTI validation
    # ------------------------------------------------------------------

    def validate_rtti(self) -> bool:
        """Check that the RTTI class name matches 'GameObjectClass'."""
        name = self._b.get_rtti_classname(hex(self._addr))
        return name == GOC.RTTI_NAME

    # ------------------------------------------------------------------
    # Repr
    # ------------------------------------------------------------------

    def __repr__(self) -> str:
        return (
            f"<GameObject 0x{self._addr:X} "
            f"id={self.object_id} owner={self.owner_id} "
            f"hp={self.hp:.1f} type={self.type_name!r}>"
        )

    def __eq__(self, other):
        if isinstance(other, GameObject):
            return self._addr == other._addr
        return NotImplemented

    def __hash__(self):
        return hash(self._addr)
