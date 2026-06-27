#!/usr/bin/env python3
"""Smoke test for Task-101 unit/hardpoint/behavior helpers.

Exercises the byte-flip vs hardpoint-behavior-attach distinction end-to-end
through the replay pipe, which is the critical test surface for Tasks 99
and 100. Each step asserts the expected observable state.

This binds the 2026-04-23 live finding (writing +0x3A7 is a no-op for
gameplay; attaching INVULNERABLE to every hardpoint is the real path)
into a fixture-free regression guard. If a future change re-couples
SWFOC_ReplayApplyDamage to the display flag, the test fails loudly.
"""

from __future__ import annotations

import sys
from smoke_test_replay import send  # reuse pipe client

OBJ = 2141623079680  # kept under 2^53 so strtod round-trips cleanly

SCENARIO = [
    # (cmd, expected-substring) — None means "any reply".
    (f'return SWFOC_ReplayMockUnit({OBJ}, "Aggressor_Destroyer", 6, 5000, 6000, 3)', "1"),
    (f'return SWFOC_ReplayUnitCount()',                                              "1"),
    (f'return SWFOC_ReplayUnitHull({OBJ})',                                           "5000"),
    (f'return SWFOC_ReplayUnitMaxHull({OBJ})',                                        "6000"),
    (f'return SWFOC_ReplayHardpointCount({OBJ})',                                     "3"),
    (f'return SWFOC_ReplayUnitOwnerSlot({OBJ})',                                      "6"),

    # ---- Flag-flip path: byte lands, damage still applies. ----
    (f'return SWFOC_ReplaySetUnitInvulnFlag({OBJ}, 1)',                               "1"),
    (f'return SWFOC_ReplayUnitInvulnFlag({OBJ})',                                     "1"),
    (f'return SWFOC_ReplayUnitIsInvulnerable({OBJ})',                                 "0"),
    (f'return SWFOC_ReplayApplyDamage({OBJ}, 500)',                                   "4500"),
    (f'return SWFOC_ReplayUnitHull({OBJ})',                                           "4500"),

    # ---- prevent_death bit-flip: same no-effect semantics. ----
    (f'return SWFOC_ReplaySetPreventDeathBit({OBJ}, 1)',                              "1"),
    (f'return SWFOC_ReplayUnitPreventDeath({OBJ})',                                   "1"),
    (f'return SWFOC_ReplayUnitIsInvulnerable({OBJ})',                                 "0"),

    # ---- Make_Invulnerable path: attaches behavior to every hardpoint. ----
    (f'return SWFOC_ReplayMakeInvulnerable({OBJ}, 1)',                                "1"),
    (f'return SWFOC_ReplayUnitIsInvulnerable({OBJ})',                                 "1"),
    (f'return SWFOC_ReplayHardpointHasBehavior({OBJ}, 0, "INVULNERABLE")',            "1"),
    (f'return SWFOC_ReplayHardpointHasBehavior({OBJ}, 1, "INVULNERABLE")',            "1"),
    (f'return SWFOC_ReplayHardpointHasBehavior({OBJ}, 2, "INVULNERABLE")',            "1"),

    # Damage is a no-op while invulnerable.
    (f'return SWFOC_ReplayApplyDamage({OBJ}, 1000)',                                  "4500"),
    (f'return SWFOC_ReplayUnitHull({OBJ})',                                           "4500"),

    # Detach behaviors; damage resumes.
    (f'return SWFOC_ReplayMakeInvulnerable({OBJ}, 0)',                                "1"),
    (f'return SWFOC_ReplayHardpointHasBehavior({OBJ}, 0, "INVULNERABLE")',            "0"),
    (f'return SWFOC_ReplayApplyDamage({OBJ}, 1000)',                                  "3500"),

    # ---- Hull clamp: writes above max_hull clamp to max. ----
    (f'return SWFOC_ReplaySetUnitHull({OBJ}, 99999)',                                 "1"),
    (f'return SWFOC_ReplayUnitHull({OBJ})',                                           "6000"),

    # ---- Selection vector. ----
    (f'return SWFOC_ReplaySetSelected({OBJ})',                                        "1"),
    (f'return SWFOC_ReplayGetSelectedUnit()',                                         str(OBJ)),
    (f'return SWFOC_ReplaySelectedCount()',                                           "1"),
    (f'return SWFOC_ReplayClearSelected()',                                           "1"),
    (f'return SWFOC_ReplaySelectedCount()',                                           "0"),

    # ---- ListTacticalUnits CSV contract (Task 104). ----
    # State at this point: OBJ mocked with max_hull=6000, hull clamped to
    # 6000 by the prior SetUnitHull(99999) step; invuln_flag still 1 because
    # SetUnitInvulnFlag(OBJ, 1) was called earlier and never cleared (the
    # Make_Invulnerable toggles only touch hardpoint behaviors, not the
    # display byte — that's the central Task 99 contract); prevent_death
    # bit 0x80 still set from SetPreventDeathBit(1); local_slot was never
    # set so is_local must be 0; selection was just cleared so is_selected=0.
    (f'return SWFOC_ReplayListTacticalUnits()',                                       f"count=1|{OBJ};6;6000.000;1;1;0;0"),

    # After re-selecting the unit the is_selected column must flip to 1.
    (f'return SWFOC_ReplaySetSelected({OBJ})',                                        "1"),
    (f'return SWFOC_ReplayListTacticalUnits()',                                       f"count=1|{OBJ};6;6000.000;1;1;0;1"),
]


def main() -> int:
    failures = 0
    for i, (cmd, expected) in enumerate(SCENARIO, 1):
        try:
            reply = send(cmd)
        except Exception as exc:
            print(f"  [{i:02d}] CMD: {cmd}")
            print(f"       FAIL (exception): {exc}")
            failures += 1
            continue
        if expected is not None and expected not in reply:
            status = f"FAIL (expected '{expected}', got '{reply}')"
            failures += 1
        else:
            status = "PASS"
        print(f"  [{i:02d}] CMD: {cmd}")
        print(f"       RES: {reply}   [{status}]")

    print()
    print(f"=== {len(SCENARIO) - failures}/{len(SCENARIO)} tests passed ===")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
