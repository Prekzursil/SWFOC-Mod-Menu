#!/usr/bin/env python3
"""Build a minimal valid .swfocsnap file for exercising swfoc_replay.exe.

This mirrors the byte layout documented in SNAPSHOT_FORMAT.md. It is used by
the replay harness smoke test; it is NOT a production capture tool.

Format versions
---------------
- v1 (legacy, kept for backward-compat tests): no explicit local_slot;
  the reader derives it from the first player.
- v2 (default since 2026-04-08): adds an explicit ``local_slot: uint32``
  in section 1 immediately after ``player_count``. ``UINT32_MAX`` means
  "no local player".

  v2 also defines OPTIONAL forward-compatible sections 6-10 that earlier
  v2 readers may safely skip:

  - section 6: ``planet_state``        per-planet corruption + ownership
  - section 7: ``diplomacy``           pairwise faction relationships
  - section 8: ``cooldowns``           per-unit-type ability cooldowns
  - section 9: ``task_forces``         per-slot task force records
  - section 10: ``object_owners``      per-instance unit ownership

  These extensions added 2026-04-08 to support v5 service replay tests.
  See ``swfoc_lua_bridge/SNAPSHOT_FORMAT.md`` for the byte layouts and
  ``knowledge-base/replay_stub_gaps.md`` for the helper-by-helper map.

CLI:
    python make_test_snapshot.py <out>              # writes v2 (extended)
    python make_test_snapshot.py <out> --v2-early   # writes v2 WITHOUT sections 6-10
    python make_test_snapshot.py <out> --v1         # writes legacy v1

The ``--v2-early`` flag models a snapshot captured during the brief window
after the v2 magic was introduced but before sections 6-10 landed. It lets
back-compat tests verify that a v2 reader handling a v2 file with only
sections 1-5 still works (the "unknown section = skip" rule must catch
readers of the newer format that encounter older v2 files without the
extended sections).
"""

import struct
import sys
import zlib


def fixed_str(s: str, width: int) -> bytes:
    b = s.encode("ascii", errors="ignore")[:width]
    return b + b"\x00" * (width - len(b))


def build_snapshot(version: int = 2, include_extended_sections: bool = True) -> bytes:
    if version not in (1, 2):
        raise ValueError(f"unsupported snapshot version: {version}")
    if version == 1 and include_extended_sections:
        # v1 has no extended sections by definition; silently coerce.
        include_extended_sections = False
    parts = []

    # ---- Header (68 bytes) ----
    magic = (b"SWFOCSNAPv2" if version == 2 else b"SWFOCSNAPv1") + b"\x00" * 5
    parts.append(magic)                                  # 16 bytes magic
    parts.append(struct.pack("<I", version))             # format_version
    parts.append(struct.pack("<Q", 0x1234567890ABCDEF))  # capture_timestamp_ms
    parts.append(b"\x00" * 32)                           # engine_build_hash
    parts.append(struct.pack("<B", 1))                   # game_mode = galactic
    parts.append(b"\x00" * 7)                            # reserved padding
    assert sum(len(p) for p in parts) == 68

    # ---- Section 1: player_array ----
    sec1 = bytearray()
    players = [
        # (slot, faction, credits, tech_level, name)
        (0, "REBEL", 12345.0, 3, ""),
        (1, "EMPIRE", 99999.0, 5, ""),
        (2, "UNDERWORLD", 5000.0, 1, ""),
    ]
    local_slot = 0  # REBEL is the local player in this synthetic fixture
    sec1 += struct.pack("<I", len(players))
    if version >= 2:
        sec1 += struct.pack("<I", local_slot)
    for slot, faction, credits, tech, name in players:
        sec1 += struct.pack("<I", slot)
        sec1 += fixed_str(faction, 64)
        sec1 += struct.pack("<d", credits)
        sec1 += struct.pack("<i", tech)
        sec1 += fixed_str(name, 64)
    parts.append(struct.pack("<II", 1, len(sec1)))
    parts.append(bytes(sec1))

    # ---- Section 2: lua_state_registry ----
    sec2 = bytearray()
    states = [0x1111111100000001, 0x1111111100000002]
    sec2 += struct.pack("<I", len(states))
    for s in states:
        sec2 += struct.pack("<Q", s)
    parts.append(struct.pack("<II", 2, len(sec2)))
    parts.append(bytes(sec2))

    # ---- Section 3: object_catalog ----
    # Richer fixture (2026-04-08): three unit types spanning ownership so the
    # new SWFOC_ReplayUnitOwner observer can verify per-instance slots when
    # paired with section 10 (object_owners).
    sec3 = bytearray()
    object_types = [
        ("TIE_Fighter",       12),  # all owned by EMPIRE (slot 1)
        ("X_Wing",             8),  # all owned by REBEL (slot 0)
        ("Star_Destroyer",     2),  # all owned by EMPIRE (slot 1)
    ]
    sec3 += struct.pack("<I", len(object_types))
    for name, count in object_types:
        sec3 += fixed_str(name, 64)
        sec3 += struct.pack("<I", count)
    parts.append(struct.pack("<II", 3, len(sec3)))
    parts.append(bytes(sec3))

    # ---- Section 4: global_registry ----
    sec4 = bytearray()
    # Type tags: 0=nil, 1=bool, 3=number, 4=string, 6=function, 7=userdata
    # We list a couple of entries: SWFOC_GetVersion (function, raw=0),
    # Find_Player (nil, raw=0), GameRandom (number, raw= bit pattern of 0.42).
    import struct as _s
    game_random_raw = _s.unpack("<Q", _s.pack("<d", 0.42))[0]
    globals_entries = [
        ("SWFOC_GetVersion", 6, 0),
        ("Find_Player",      0, 0),
        ("GameRandom",       3, game_random_raw),
    ]
    sec4 += struct.pack("<I", len(globals_entries))
    for name, ty, raw in globals_entries:
        sec4 += fixed_str(name, 64)
        sec4 += struct.pack("<B", ty)
        sec4 += b"\x00" * 7
        sec4 += struct.pack("<Q", raw)
    parts.append(struct.pack("<II", 4, len(sec4)))
    parts.append(bytes(sec4))

    # ---- Section 5: metadata ----
    sec5 = bytearray()
    entries = [
        ("capture_method",       "replay_test_generator"),
        ("mod_name",             "smoke_test"),
        ("mod_version",          "0.0.1"),
        ("swfoc_bridge_version", "1.0"),
    ]
    sec5 += struct.pack("<I", len(entries))
    for k, v in entries:
        kb = k.encode("ascii")
        vb = v.encode("ascii")
        sec5 += struct.pack("<H", len(kb))
        sec5 += kb
        sec5 += struct.pack("<H", len(vb))
        sec5 += vb
    parts.append(struct.pack("<II", 5, len(sec5)))
    parts.append(bytes(sec5))

    # ---- v2-only OPTIONAL sections 6..10 (added 2026-04-08) ----
    # v1 readers MUST not see these. v2 readers that don't recognize them
    # skip cleanly via the ``unknown section -> consume section_length`` rule.
    # ``--v2-early`` skips this block to emulate a v2 snapshot from before
    # the section-6..10 extensions landed.
    if version >= 2 and include_extended_sections:
        # ---- Section 6: planet_state ----
        # Layout per planet: char[64] name + float32 corruption + int32 owner.
        planets = [
            ("TATOOINE",  0.10, 0),  # REBEL controlled, low corruption
            ("CORUSCANT", 0.00, 1),  # EMPIRE capital, clean
            ("HOTH",      0.05, 0),  # REBEL outpost
            ("KASHYYYK",  0.40, 2),  # UNDERWORLD foothold
            ("NABOO",     0.75, 2),  # heavily corrupted
        ]
        sec6 = bytearray()
        sec6 += struct.pack("<I", len(planets))
        for pname, corruption, owner in planets:
            sec6 += fixed_str(pname, 64)
            sec6 += struct.pack("<f", corruption)
            sec6 += struct.pack("<i", owner)
        parts.append(struct.pack("<II", 6, len(sec6)))
        parts.append(bytes(sec6))

        # ---- Section 7: diplomacy ----
        # Layout per pair: char[32] faction_a + char[32] faction_b + char[16] state.
        diplomacy_pairs = [
            ("REBEL",  "EMPIRE",     "hostile"),
            ("REBEL",  "UNDERWORLD", "neutral"),
            ("EMPIRE", "UNDERWORLD", "hostile"),
        ]
        sec7 = bytearray()
        sec7 += struct.pack("<I", len(diplomacy_pairs))
        for fa, fb, state in diplomacy_pairs:
            sec7 += fixed_str(fa, 32)
            sec7 += fixed_str(fb, 32)
            sec7 += fixed_str(state, 16)
        parts.append(struct.pack("<II", 7, len(sec7)))
        parts.append(bytes(sec7))

        # ---- Section 8: cooldowns ----
        # Layout per type: char[64] name + uint32 ability_count + float32[ability_count].
        cooldown_table = [
            ("TIE_Fighter", [0.0, 12.5]),                 # 2 abilities
            ("X_Wing",      [0.0, 5.0, 30.0]),            # 3 abilities
        ]
        sec8 = bytearray()
        sec8 += struct.pack("<I", len(cooldown_table))
        for type_name, abilities in cooldown_table:
            sec8 += fixed_str(type_name, 64)
            sec8 += struct.pack("<I", len(abilities))
            for v in abilities:
                sec8 += struct.pack("<f", v)
        parts.append(struct.pack("<II", 8, len(sec8)))
        parts.append(bytes(sec8))

        # ---- Section 9: task_forces ----
        # Layout per record: int32 owner_slot + char[64] name.
        task_forces = [
            (1, "Death_Squadron"),  # EMPIRE task force
            (0, "Rogue_Squadron"),  # REBEL task force
        ]
        sec9 = bytearray()
        sec9 += struct.pack("<I", len(task_forces))
        for owner, name in task_forces:
            sec9 += struct.pack("<i", owner)
            sec9 += fixed_str(name, 64)
        parts.append(struct.pack("<II", 9, len(sec9)))
        parts.append(bytes(sec9))

        # ---- Section 10: object_owners ----
        # Layout per type: char[64] name + uint32 instance_count + int32[instance_count].
        # Mirrors section 3 with per-instance owner slots so SWFOC_ReplayUnitOwner
        # can answer "who owns the i-th X_Wing?".
        owner_table = [
            ("TIE_Fighter",   [1] * 12),  # 12 EMPIRE TIE Fighters
            ("X_Wing",        [0] *  8),  # 8 REBEL X-Wings
            ("Star_Destroyer",[1, 1]),    # 2 EMPIRE Star Destroyers
        ]
        sec10 = bytearray()
        sec10 += struct.pack("<I", len(owner_table))
        for type_name, owners in owner_table:
            sec10 += fixed_str(type_name, 64)
            sec10 += struct.pack("<I", len(owners))
            for slot in owners:
                sec10 += struct.pack("<i", slot)
        parts.append(struct.pack("<II", 10, len(sec10)))
        parts.append(bytes(sec10))

    # ---- End marker ----
    parts.append(struct.pack("<II", 0xFFFFFFFF, 4))

    body = b"".join(parts)
    crc = zlib.crc32(body) & 0xFFFFFFFF
    return body + struct.pack("<I", crc)


def main() -> int:
    if len(sys.argv) < 2:
        print(
            "usage: make_test_snapshot.py <output-path> [--v1 | --v2-early]",
            file=sys.stderr,
        )
        return 2
    out_path = sys.argv[1]
    flags = sys.argv[2:]
    if "--v1" in flags:
        version = 1
        extended = False
        label = "v1"
    elif "--v2-early" in flags:
        version = 2
        extended = False
        label = "v2-early"
    else:
        version = 2
        extended = True
        label = "v2"
    blob = build_snapshot(version=version, include_extended_sections=extended)
    with open(out_path, "wb") as f:
        f.write(blob)
    print(f"wrote {len(blob)} bytes to {out_path} ({label})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
