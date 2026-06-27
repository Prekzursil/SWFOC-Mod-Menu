// =============================================================================
// swfoc_overlay/overlay_spawn_gate_test.cpp — unit test for overlay_spawn_gate.h
// (Phase 4 cont., iter 532 / spec iter-295).
//
// iter-295 adds the multi-player-safety gate: a drag-drop spawn must only fire
// when the overlay is looking at a valid TACTICAL local player. SWFOC's local-
// player slot is 0..7 in a tactical battle and -1 during a galactic-mode
// transition (or with no game attached). overlay_spawn_gate.h holds the pure
// decision kernel — slot classification, the gate boolean, the operator-trust
// badge text. This test pins all three so the ImGui render glue in overlay.cpp
// (the Unit-type drag source + RenderSpawnPad + RenderMinimap) can depend on
// them build-only.
//
// overlay_spawn_gate.h is header-only with no includes at all. Build + run via
// build_spawn_gate_test.bat — no game, no pipe, no ImGui.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - SLOT -1 IS GATED   : EvaluateSpawnGate(-1) must be NotInTactical and
//                          SpawnGateAllowsSpawn(-1) must be false. The "no
//                          gate at all" old form — where a drop always
//                          spawned, allowing -1 — fails this pin.
//   - VALID SLOT ALLOWED : slots 0 and 7 must be Allowed; the gate must not
//                          block legitimate tactical spawning.
//   - BADGE PHRASE EXACT : SpawnGateBadgeText(NotInTactical) must be exactly
//                          "Tactical mode only" — the spec's required string.
//   - OUT-OF-RANGE GATED : slots 8 / 99 / -2 must be InvalidSlot, not allowed.
// =============================================================================

#include "overlay_spawn_gate.h"

#include <cstdio>
#include <cstring>

namespace
{
    int g_checks = 0;
    int g_failures = 0;

    void ExpectTrue(const char* name, bool cond)
    {
        ++g_checks;
        if (cond)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    expected true\n", name);
        }
    }

    void ExpectFalse(const char* name, bool cond)
    {
        ExpectTrue(name, !cond);
    }

    // Compare two SpawnGateStatus values. Cast to int for the failure report
    // so a mismatch says which enumerator drifted (0 Allowed / 1 NotInTactical
    // / 2 InvalidSlot — the declaration order in overlay_spawn_gate.h).
    void ExpectStatus(const char* name, swfoc_overlay::SpawnGateStatus got,
                      swfoc_overlay::SpawnGateStatus want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %d\n    want: %d\n",
                        name, static_cast<int>(got), static_cast<int>(want));
        }
    }

    void ExpectStrEq(const char* name, const char* got, const char* want)
    {
        ++g_checks;
        if (got != nullptr && want != nullptr && std::strcmp(got, want) == 0)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %s\n    want: %s\n", name,
                        got != nullptr ? got : "(null)",
                        want != nullptr ? want : "(null)");
        }
    }
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_spawn_gate unit test ==\n");

    // ---- Constants are sane -----------------------------------------------
    ExpectTrue("const: kMinLocalPlayerSlot is 0",
               kMinLocalPlayerSlot == 0);
    ExpectTrue("const: kMaxLocalPlayerSlot is 7 (8 SWFOC tactical slots)",
               kMaxLocalPlayerSlot == 7);
    ExpectTrue("const: kNoLocalPlayerSlot is -1 (HudSnapshot sentinel)",
               kNoLocalPlayerSlot == -1);
    ExpectTrue("const: min slot is below max slot",
               kMinLocalPlayerSlot < kMaxLocalPlayerSlot);
    ExpectTrue("const: the -1 sentinel is below the valid range",
               kNoLocalPlayerSlot < kMinLocalPlayerSlot);

    // ---- EvaluateSpawnGate: classify a raw slot ---------------------------
    // Every slot in the valid [0,7] range -> Allowed.
    ExpectStatus("eval: slot 0 -> Allowed",
                 EvaluateSpawnGate(0), SpawnGateStatus::Allowed);
    ExpectStatus("eval: slot 1 -> Allowed",
                 EvaluateSpawnGate(1), SpawnGateStatus::Allowed);
    ExpectStatus("eval: slot 3 -> Allowed",
                 EvaluateSpawnGate(3), SpawnGateStatus::Allowed);
    ExpectStatus("eval: slot 4 -> Allowed",
                 EvaluateSpawnGate(4), SpawnGateStatus::Allowed);
    ExpectStatus("eval: slot 6 -> Allowed",
                 EvaluateSpawnGate(6), SpawnGateStatus::Allowed);
    ExpectStatus("eval: slot 7 -> Allowed (top of the valid range)",
                 EvaluateSpawnGate(7), SpawnGateStatus::Allowed);
    // The -1 sentinel -> NotInTactical (galactic transition / no game).
    ExpectStatus("eval: slot -1 -> NotInTactical (galactic transition)",
                 EvaluateSpawnGate(-1), SpawnGateStatus::NotInTactical);
    // Everything else -> the defensive InvalidSlot bucket.
    ExpectStatus("eval: slot 8 -> InvalidSlot (just past the valid range)",
                 EvaluateSpawnGate(8), SpawnGateStatus::InvalidSlot);
    ExpectStatus("eval: slot 99 -> InvalidSlot",
                 EvaluateSpawnGate(99), SpawnGateStatus::InvalidSlot);
    ExpectStatus("eval: slot -2 -> InvalidSlot (just past the -1 sentinel)",
                 EvaluateSpawnGate(-2), SpawnGateStatus::InvalidSlot);
    ExpectStatus("eval: slot -100 -> InvalidSlot",
                 EvaluateSpawnGate(-100), SpawnGateStatus::InvalidSlot);
    ExpectStatus("eval: slot 1000000 -> InvalidSlot",
                 EvaluateSpawnGate(1000000), SpawnGateStatus::InvalidSlot);

    // PIN slot -1 is gated: the spec's primary case — a galactic transition
    // must classify as NotInTactical, never Allowed.
    ExpectStatus("PIN gated: EvaluateSpawnGate(-1) is NotInTactical",
                 EvaluateSpawnGate(-1), SpawnGateStatus::NotInTactical);
    // PIN valid slot allowed: the two endpoints of the valid range.
    ExpectStatus("PIN allowed: slot 0 (bottom of range) is Allowed",
                 EvaluateSpawnGate(0), SpawnGateStatus::Allowed);
    ExpectStatus("PIN allowed: slot 7 (top of range) is Allowed",
                 EvaluateSpawnGate(7), SpawnGateStatus::Allowed);
    // PIN out-of-range gated: the exact upper boundary — slot 7 vs slot 8.
    ExpectTrue("PIN boundary: slot 7 Allowed AND slot 8 InvalidSlot",
               EvaluateSpawnGate(7) == SpawnGateStatus::Allowed &&
               EvaluateSpawnGate(8) == SpawnGateStatus::InvalidSlot);
    // PIN out-of-range gated: -1 is special-cased, -2 is not.
    ExpectTrue("PIN boundary: slot -1 NotInTactical AND slot -2 InvalidSlot",
               EvaluateSpawnGate(-1) == SpawnGateStatus::NotInTactical &&
               EvaluateSpawnGate(-2) == SpawnGateStatus::InvalidSlot);

    // ---- SpawnGateAllowsSpawn(status): the gate boolean -------------------
    ExpectTrue("allows(status): Allowed -> true",
               SpawnGateAllowsSpawn(SpawnGateStatus::Allowed));
    ExpectFalse("allows(status): NotInTactical -> false",
                SpawnGateAllowsSpawn(SpawnGateStatus::NotInTactical));
    ExpectFalse("allows(status): InvalidSlot -> false",
                SpawnGateAllowsSpawn(SpawnGateStatus::InvalidSlot));

    // ---- SpawnGateAllowsSpawn(int): the convenience overload -------------
    ExpectTrue("allows(int): slot 0 -> true",
               SpawnGateAllowsSpawn(0));
    ExpectTrue("allows(int): slot 3 -> true",
               SpawnGateAllowsSpawn(3));
    ExpectTrue("allows(int): slot 7 -> true",
               SpawnGateAllowsSpawn(7));
    ExpectFalse("allows(int): slot -1 -> false",
                SpawnGateAllowsSpawn(-1));
    ExpectFalse("allows(int): slot 8 -> false",
                SpawnGateAllowsSpawn(8));
    ExpectFalse("allows(int): slot 99 -> false",
                SpawnGateAllowsSpawn(99));
    ExpectFalse("allows(int): slot -5 -> false",
                SpawnGateAllowsSpawn(-5));

    // PIN no-gate-old-form: the "no gate at all" old form let every drop
    // spawn — i.e. SpawnGateAllowsSpawn(-1) would have been true. The gate
    // must return false for the -1 galactic-transition slot.
    ExpectFalse("PIN no-gate-old-form: SpawnGateAllowsSpawn(-1) is false",
                SpawnGateAllowsSpawn(-1));
    // PIN the status overload and the int overload agree on the -1 case.
    ExpectTrue("PIN consistency: allows(-1) == allows(eval(-1))",
               SpawnGateAllowsSpawn(-1) ==
               SpawnGateAllowsSpawn(EvaluateSpawnGate(-1)));
    {
        // PIN every valid slot 0..7 allows spawning.
        bool allValidAllowed = true;
        for (int slot = kMinLocalPlayerSlot; slot <= kMaxLocalPlayerSlot;
             ++slot)
        {
            if (!SpawnGateAllowsSpawn(slot))
            {
                allValidAllowed = false;
            }
        }
        ExpectTrue("PIN range: every slot 0..7 allows spawning",
                   allValidAllowed);
    }
    {
        // PIN every slot above the valid range gates spawning.
        bool allHighGated = true;
        for (int slot = kMaxLocalPlayerSlot + 1; slot <= 40; ++slot)
        {
            if (SpawnGateAllowsSpawn(slot))
            {
                allHighGated = false;
            }
        }
        ExpectTrue("PIN range: every slot 8..40 gates spawning",
                   allHighGated);
    }
    {
        // PIN every negative slot (including the -1 sentinel) gates spawning.
        bool allNegGated = true;
        for (int slot = -40; slot <= -1; ++slot)
        {
            if (SpawnGateAllowsSpawn(slot))
            {
                allNegGated = false;
            }
        }
        ExpectTrue("PIN range: every slot -40..-1 gates spawning",
                   allNegGated);
    }

    // ---- SpawnGateBadgeText: the operator-trust badge strings -------------
    ExpectStrEq("badge: Allowed -> 'Drag-drop spawn LIVE'",
                SpawnGateBadgeText(SpawnGateStatus::Allowed),
                "Drag-drop spawn LIVE");
    ExpectStrEq("badge: NotInTactical -> 'Tactical mode only'",
                SpawnGateBadgeText(SpawnGateStatus::NotInTactical),
                "Tactical mode only");
    ExpectStrEq("badge: InvalidSlot -> 'Unknown local player'",
                SpawnGateBadgeText(SpawnGateStatus::InvalidSlot),
                "Unknown local player");

    // PIN badge phrase exact: the spec (overlay-interactive.md iter-295)
    // requires the slot-(-1) badge to read EXACTLY "Tactical mode only".
    ExpectTrue("PIN badge-phrase: NotInTactical badge is exactly the spec text",
               std::strcmp(SpawnGateBadgeText(SpawnGateStatus::NotInTactical),
                           "Tactical mode only") == 0);
    // PIN badges distinct: the three statuses must read differently so the
    // operator can tell a clean galactic transition from an unexpected slot.
    ExpectTrue("PIN distinct: Allowed badge differs from NotInTactical badge",
               std::strcmp(SpawnGateBadgeText(SpawnGateStatus::Allowed),
                   SpawnGateBadgeText(SpawnGateStatus::NotInTactical)) != 0);
    ExpectTrue("PIN distinct: NotInTactical badge differs from InvalidSlot",
               std::strcmp(SpawnGateBadgeText(SpawnGateStatus::NotInTactical),
                   SpawnGateBadgeText(SpawnGateStatus::InvalidSlot)) != 0);
    ExpectTrue("PIN distinct: Allowed badge differs from InvalidSlot badge",
               std::strcmp(SpawnGateBadgeText(SpawnGateStatus::Allowed),
                   SpawnGateBadgeText(SpawnGateStatus::InvalidSlot)) != 0);
    // PIN badges non-null and non-empty for every status.
    ExpectTrue("PIN badge: Allowed badge is non-null and non-empty",
               SpawnGateBadgeText(SpawnGateStatus::Allowed) != nullptr &&
               SpawnGateBadgeText(SpawnGateStatus::Allowed)[0] != '\0');
    ExpectTrue("PIN badge: NotInTactical badge is non-null and non-empty",
               SpawnGateBadgeText(SpawnGateStatus::NotInTactical) != nullptr &&
               SpawnGateBadgeText(SpawnGateStatus::NotInTactical)[0] != '\0');
    ExpectTrue("PIN badge: InvalidSlot badge is non-null and non-empty",
               SpawnGateBadgeText(SpawnGateStatus::InvalidSlot) != nullptr &&
               SpawnGateBadgeText(SpawnGateStatus::InvalidSlot)[0] != '\0');

    // ---- Integration: the exact path overlay.cpp runs --------------------
    // RenderActionsWindow does EvaluateSpawnGate(snap.local_player_slot),
    // then SpawnGateAllowsSpawn(status) to gate the drag source + drop
    // targets, then SpawnGateBadgeText(status) for the badge.
    {
        // HudSnapshot::local_player_slot defaults to -1 (no game attached) —
        // the overlay's cold-start state. The gate must be CLOSED and show
        // the "Tactical mode only" badge.
        const int slot = -1;
        const SpawnGateStatus st = EvaluateSpawnGate(slot);
        ExpectFalse("integration: cold-start slot -1 -> gate closed",
                    SpawnGateAllowsSpawn(st));
        ExpectStrEq("integration: cold-start slot -1 -> 'Tactical mode only'",
                    SpawnGateBadgeText(st), "Tactical mode only");
    }
    {
        // A live tactical battle: the local player is slot 0. Gate OPEN.
        const int slot = 0;
        const SpawnGateStatus st = EvaluateSpawnGate(slot);
        ExpectTrue("integration: tactical slot 0 -> gate open",
                   SpawnGateAllowsSpawn(st));
        ExpectStrEq("integration: tactical slot 0 -> 'Drag-drop spawn LIVE'",
                    SpawnGateBadgeText(st), "Drag-drop spawn LIVE");
    }
    {
        // A live tactical battle, local player in the last slot. Gate OPEN.
        const int slot = 7;
        const SpawnGateStatus st = EvaluateSpawnGate(slot);
        ExpectTrue("integration: tactical slot 7 -> gate open",
                   SpawnGateAllowsSpawn(st));
        ExpectStatus("integration: tactical slot 7 -> Allowed",
                     st, SpawnGateStatus::Allowed);
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
