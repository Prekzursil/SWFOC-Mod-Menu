// =============================================================================
// swfoc_overlay/overlay_spawn_gate.h — Phase 4 multi-player-safety spawn gate.
//
// Phase 4 (iter 292-296) lets the operator place units by drag-drop. iter-292
// shipped the spawn pad, iter-293 the minimap, iter-294 the preview ring.
// iter-295 (spec line 53) adds the MULTI-PLAYER SAFETY GATE: a drag-drop spawn
// must only fire when the overlay is looking at a valid TACTICAL local player.
//
// SWFOC_GetLocalPlayer reports the local player's slot. In a tactical battle
// the slot is 0..7; during a galactic-mode transition — or with no game
// attached — the bridge cannot resolve a tactical local player and the slot is
// -1. Spawning a unit "under" a -1 owner has no well-defined meaning (the
// engine's Create_Generic_Object path needs a real player object), so every
// drag-drop spawn is gated on this slot.
//
// The HUD worker already polls the local-player slot into
// HudSnapshot::local_player_slot (hud_state.h). This header is the PURE
// decision kernel layered on top of that field — the part that has a right and
// a wrong answer and so must be pinned by a unit test before the ImGui render
// glue in overlay.cpp (the Unit-type drag source + RenderSpawnPad +
// RenderMinimap) depends on it:
//
//   1. EvaluateSpawnGate()    — classify a raw slot int into a 3-state status.
//   2. SpawnGateAllowsSpawn() — the one boolean the drag source and the drop
//      targets gate on.
//   3. SpawnGateBadgeText()   — the operator-trust badge string per status;
//      the spec requires the exact phrase "Tactical mode only" for slot -1.
//
// Mirrors the iter-120 LiveSkip pattern (a live-only capability is SKIPped, not
// FAILed, when its precondition is absent) — here a drag-drop spawn is gated,
// not errored, when there is no tactical local player.
//
// RED-GREEN REGRESSION PINS (overlay_spawn_gate_test.cpp)
// ------------------------------------------------------
//   - SLOT -1 IS GATED   : EvaluateSpawnGate(-1) is NotInTactical and
//                          SpawnGateAllowsSpawn(-1) is false. A gate that
//                          allowed -1 (the "no gate at all" old form, where a
//                          drop always spawned) fails this pin.
//   - VALID SLOT ALLOWED : slots 0 and 7 are Allowed — the gate must not
//                          block legitimate tactical spawning.
//   - BADGE PHRASE EXACT : SpawnGateBadgeText(NotInTactical) is exactly
//                          "Tactical mode only" — the spec's required string;
//                          a UI reword (the iter-380 / iter-388 drift family)
//                          fails this pin.
//   - OUT-OF-RANGE GATED : slot 8 / 99 / -2 are InvalidSlot, not allowed — a
//                          bogus slot must never resolve to a spawn owner.
//
// Pure, header-only, std-only (no includes at all — plain int / enum / const
// char* logic) — no ImGui, no Windows, no bridge. Unit-tested with a plain
// g++ (build_spawn_gate_test.bat).
// =============================================================================

#pragma once

namespace swfoc_overlay
{
    // Lowest / highest valid SWFOC tactical player slot. A tactical battle has
    // 8 player slots numbered 0..7; SWFOC_GetLocalPlayer returns the local
    // player's slot in this inclusive range when a tactical local player
    // exists.
    inline constexpr int kMinLocalPlayerSlot = 0;
    inline constexpr int kMaxLocalPlayerSlot = 7;

    // Sentinel SWFOC_GetLocalPlayer / HudSnapshot::local_player_slot value for
    // "no tactical local player" — a galactic-mode transition, or no game
    // attached. Matches hud_state.h's documented default for the field.
    inline constexpr int kNoLocalPlayerSlot = -1;

    // The result of classifying a local-player slot for drag-drop spawning.
    enum class SpawnGateStatus
    {
        Allowed,        // slot in [0,7] — a valid tactical local player; spawn OK
        NotInTactical,  // slot == -1 — galactic transition / no game; spawn gated
        InvalidSlot,    // slot outside [-1,7] — unexpected; spawn gated defensively
    };

    // Classify a raw local-player slot (HudSnapshot::local_player_slot) into a
    // SpawnGateStatus. Order matters: the valid [0,7] range is tested first,
    // then the -1 sentinel, and everything else falls through to the defensive
    // InvalidSlot bucket.
    inline SpawnGateStatus EvaluateSpawnGate(int localPlayerSlot)
    {
        if (localPlayerSlot >= kMinLocalPlayerSlot &&
            localPlayerSlot <= kMaxLocalPlayerSlot)
        {
            return SpawnGateStatus::Allowed;
        }
        if (localPlayerSlot == kNoLocalPlayerSlot)
        {
            return SpawnGateStatus::NotInTactical;
        }
        return SpawnGateStatus::InvalidSlot;
    }

    // True only when `status` is Allowed — the single boolean the Unit-type
    // drag source, RenderSpawnPad and RenderMinimap gate on. NotInTactical and
    // InvalidSlot both yield false: a drag-drop spawn must not fire.
    inline bool SpawnGateAllowsSpawn(SpawnGateStatus status)
    {
        return status == SpawnGateStatus::Allowed;
    }

    // Convenience overload: classify the raw slot and return the gate boolean
    // in one call. Equivalent to SpawnGateAllowsSpawn(EvaluateSpawnGate(slot)).
    inline bool SpawnGateAllowsSpawn(int localPlayerSlot)
    {
        return SpawnGateAllowsSpawn(EvaluateSpawnGate(localPlayerSlot));
    }

    // Operator-trust badge text for a spawn-gate status (guardrail 1007). The
    // overlay draws this next to the Phase 4 spawn widgets so the operator
    // always knows whether — and why — a drop will or will not spawn.
    //   - Allowed       -> a green LIVE confirmation.
    //   - NotInTactical -> exactly "Tactical mode only" — the spec's required
    //                      phrase (overlay-interactive.md iter-295).
    //   - InvalidSlot   -> a distinct defensive message; an unexpected slot is
    //                      not the same operator situation as a clean galactic
    //                      transition, so it must not borrow the -1 phrasing.
    // Stable strings: the test pins each one so a UI tweak cannot quietly
    // reword an operator-trust badge (the iter-380 / iter-388 drift family).
    inline const char* SpawnGateBadgeText(SpawnGateStatus status)
    {
        switch (status)
        {
            case SpawnGateStatus::Allowed:
                return "Drag-drop spawn LIVE";
            case SpawnGateStatus::NotInTactical:
                return "Tactical mode only";
            case SpawnGateStatus::InvalidSlot:
                return "Unknown local player";
        }
        return "Unknown local player";  // unreachable — every enum handled above
    }
}
