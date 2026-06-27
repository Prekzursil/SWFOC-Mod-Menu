"""
Struct definitions and field offsets for reverse-engineered game structures.

All offsets are relative to the start of the object they belong to.
RVAs are relative to the base of StarWarsG.exe.
"""

from dataclasses import dataclass, field
from typing import Dict


# ---------------------------------------------------------------------------
# GameObjectClass  (vtable RVA 0x8661B8, RTTI "GameObjectClass")
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class GameObjectClassLayout:
    """Field offsets within a GameObjectClass instance."""

    VTABLE:              int = 0x00    # Pointer to vtable
    OBJECT_ID:           int = 0x50    # int32  - unique object ID
    OWNER_PLAYER_ID:     int = 0x58    # int32  - index into PlayerListClass
    HP:                  int = 0x5C    # float  - current hit-points
    HEALTH_SUB_OBJECT:   int = 0x118   # pointer to health sub-object
    COMPONENT_ARRAY:     int = 0x278   # pointer to component pointer array
    GAME_OBJECT_TYPE:    int = 0x298   # pointer to GameObjectType
    COMPONENT_LOOKUP:    int = 0x332   # byte array indexed by query type
    PARENT_COMPONENT_IDX:int = 0x335   # byte - 0xFF means top-level (no parent)
    HP_PATH_FLAG:        int = 0x348   # byte - HP routing flag
    DIRTY_FLAG:          int = 0x3A0   # byte - dirty/changed flag
    INVULNERABILITY:     int = 0x3A7   # byte - invulnerability flag (1 = invuln)

    # Query types for the component lookup table at +0x332
    QUERY_SELF:              int = 0x00
    QUERY_PARENT_CONTAINER:  int = 0x03
    QUERY_HARDPOINT_MGR:     int = 0x16
    QUERY_ABILITY:           int = 0x19
    QUERY_TRANSFORM:         int = 0x3D
    QUERY_PROPERTY:          int = 0x46

    # Known vtable RVA (for identification)
    VTABLE_RVA: int = 0x8661B8

    # RTTI class name
    RTTI_NAME: str = "GameObjectClass"


@dataclass(frozen=True)
class GameObjectTypeLayout:
    """Field offsets within a GameObjectType instance (pointed to by GameObjectClass+0x298)."""

    # The type name is stored as an MSVC SSO (Small String Optimization) string.
    # Layout: 16 bytes inline buffer + 8 bytes (size_or_capacity)
    # If [type+0xF8+0x18] >= 16 -> heap-allocated: char* at [type+0xF8]
    # If [type+0xF8+0x18] <  16 -> inline string starting at type+0xF8
    TYPE_NAME_SSO:     int = 0xF8   # Start of MSVC SSO string
    SSO_CAPACITY_OFF:  int = 0x18   # Offset from SSO start to capacity field


@dataclass(frozen=True)
class PlayerListClassLayout:
    """Field offsets within the global PlayerListClass."""

    PLAYER_ARRAY:  int = 0x20   # void** - array of PlayerObject pointers
    PLAYER_COUNT:  int = 0x28   # int32  - number of players

    # Global RVA of the PlayerListClass pointer
    GLOBAL_RVA: int = 0xA16FD0


# ---------------------------------------------------------------------------
# Key function RVAs
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class FunctionRVAs:
    """Known function addresses as RVAs from StarWarsG.exe base."""

    HP_SETTER:            int = 0x3A89D0   # SetHP(obj, new_hp)
    GET_HULL_PCT:         int = 0x396DF0
    GET_HULL_OUTER:       int = 0x396470
    GET_MAX_HEALTH:       int = 0x3727A0
    TAKE_DAMAGE:          int = 0x38A350   # Main damage dispatch
    TAKE_DAMAGE_DISPATCH: int = 0x3A97E0   # Take_Damage property dispatch
    INVULN_CLEANUP:       int = 0x3A56B0
    INVULN_SETTER:        int = 0x3ABB80
    QUERY_INTERFACE:      int = 0x395AC0
    FIND_PLAYER_BY_ID:    int = 0x294BC0
    RESOLVE_PARENT_OWNER: int = 0x3956C0


# ---------------------------------------------------------------------------
# AOB signatures for version-independent address resolution
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class AOBSignatures:
    """Array-of-bytes patterns for locating functions across game versions.

    Patterns use ?? for wildcard bytes.
    """

    # SetHP prologue:  push rbx; sub rsp, 60h
    HP_SETTER: str = "40 53 48 83 EC 60"

    # PlayerListClass access pattern (mov rax, [rip+...] with the global)
    # This needs to be refined per-version; the RVA-based lookup is the fallback.
    PLAYER_LIST_ACCESS: str = ""


# Singleton instances for convenience
GOC = GameObjectClassLayout()
GOT = GameObjectTypeLayout()
PLC = PlayerListClassLayout()
RVAS = FunctionRVAs()
AOBS = AOBSignatures()
