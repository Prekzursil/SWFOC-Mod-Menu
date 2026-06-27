"""
Mod system for SWFOC runtime modifications.

All mods inherit from ModBase and implement enable/disable with
Auto Assembler code caves injected through the CE Bridge.
"""

from .base import ModBase
from .god_mode import GodModeMod
from .one_hit_kill import OneHitKillMod

__all__ = ["ModBase", "GodModeMod", "OneHitKillMod"]
