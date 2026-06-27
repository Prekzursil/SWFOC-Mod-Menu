// =============================================================================
// swfoc_overlay/overlay_phase4_close_test.cpp — Phase 4 close-out integration
// test (Phase 4 close-out, iter 533 / spec iter-296).
//
// Phase 4 (iter 292-296) gave the overlay a drag-drop tactical-spawn surface,
// shipped one kernel per iteration, each with its own dedicated unit test:
//
//   iter-292/529  overlay_dragdrop.h     drag payload + pad->world map   65/0
//   iter-293/530  overlay_minimap.h      256x256 minimap + marker ring  135/0
//   iter-294/531  overlay_preview_ring.h faction-tinted drop preview     74/0
//   iter-295/532  overlay_spawn_gate.h   multi-player-safety gate        53/0
//
// Those four tests pin each kernel IN ISOLATION (327 checks total). This file
// is the Phase 4 CLOSE-OUT test: it wires the four kernels — plus the spawn
// builder overlay_actions.h::BuildSpawnUnitCommand — TOGETHER and exercises
// the complete end-to-end drag-drop spawn PIPELINE exactly as overlay.cpp's
// RenderActionsWindow / RenderSpawnPad / RenderMinimap / DispatchSpawnDrop
// chain them at runtime. Its value is the SEAMS between kernels, which no
// isolation test can see.
//
// Naming note (carried from iter-527's Phase 3 close-out): the spec iter-296
// row writes "Iter296Phase4DragDropTests.cs". The `.cs` name predates the
// overlay's all-C++ native-exe test infra — a C# test cannot exercise a C++
// header. This file IS the spec iter-296 close-out test in the established
// pattern (overlay_dragdrop_test.cpp, overlay_minimap_test.cpp, ...).
//
// SPEC iter-296 RED-GREEN PINS (overlay-interactive.md line 54)
// ------------------------------------------------------------
//   [1] DROP WITHOUT SELECTION BLOCKS : a drop fired with no valid tactical
//       local player (gate closed) — or with no draggable unit-type payload —
//       produces NO spawn command and records NO minimap marker. The "no gate
//       at all" old form, where a drop always spawned, fails this pin.
//   [2] DROP WITH SELECTION FIRES     : a drop fired with a valid local-player
//       slot and a valid dragged unit type produces a real
//       `return SWFOC_SpawnUnitLua(...)` Lua line at the resolved world point.
//   [3] PREVIEW RING RENDERS          : the preview-ring kernel yields a
//       positive, in-band radius and a visible faction-tinted color for every
//       frame of the breathe cycle — ImGui is never handed a degenerate ring.
//   [4] MINIMAP SHOWS UNITS           : every spawn that fires records a
//       marker, and each retained marker projects onto an on-map minimap
//       pixel — the drop becomes a dot the operator can see.
//
// All five headers are header-only and std-only. Build + run via
// build_phase4_close_test.bat — no game, no pipe, no ImGui, no bridge.
// =============================================================================

#include "overlay_actions.h"
#include "overlay_dragdrop.h"
#include "overlay_minimap.h"
#include "overlay_preview_ring.h"
#include "overlay_spawn_gate.h"

#include <cstdio>
#include <cstring>
#include <string>

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

    // Compare two floats within a tolerance. World coordinates in this test
    // are all exactly representable (0, 250, 500, 1000, 2000), so a tight eps
    // catches any real drift without flagging benign float rounding.
    void ExpectNear(const char* name, float got, float want, float eps)
    {
        ++g_checks;
        const float diff = got > want ? got - want : want - got;
        if (diff <= eps)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got %.4f  want %.4f\n",
                        name, static_cast<double>(got),
                        static_cast<double>(want));
        }
    }

    // Compare two integer-valued quantities. `long long` so std::size_t ring
    // counts and unsigned-char color channels both feed it without a sign
    // warning under -Wextra.
    void ExpectIntEq(const char* name, long long got, long long want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got %lld  want %lld\n",
                        name, got, want);
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
            std::printf("  FAIL %s\n    got \"%s\"  want \"%s\"\n", name,
                        got != nullptr ? got : "(null)",
                        want != nullptr ? want : "(null)");
        }
    }

    // True when `hay` contains `needle` as a substring.
    void ExpectContains(const char* name, const std::string& hay,
                        const char* needle)
    {
        ++g_checks;
        if (hay.find(needle) != std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\" does not contain \"%s\"\n",
                        name, hay.c_str(), needle);
        }
    }

    // True when `hay` begins with `prefix`.
    void ExpectStartsWith(const char* name, const std::string& hay,
                          const char* prefix)
    {
        ++g_checks;
        if (hay.rfind(prefix, 0) == 0)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\" does not start with \"%s\"\n",
                        name, hay.c_str(), prefix);
        }
    }

    // -------------------------------------------------------------------------
    // Phase4DropResult / SimulatePhase4Drop — a faithful in-memory model of the
    // overlay.cpp Phase 4 drag-drop flow, so the close-out test can drive the
    // four kernels TOGETHER through the exact decision sequence the ImGui
    // render glue runs. The glue itself (BeginDragDropSource/Target, the draw
    // lists) is build-only; this models its DECISION sequence, which is the
    // part that has a right answer.
    //
    // The chain reproduced here, in order:
    //   RenderActionsWindow : EvaluateSpawnGate(slot) -> SpawnGateAllowsSpawn.
    //                         A closed gate disarms the drag source AND
    //                         withholds both drop targets — nothing spawns.
    //   drag source         : PackUnitTypePayload(name) — a failed pack means
    //                         "do not start the drag" (overlay_dragdrop.h).
    //   drop delivery frame : DropPadToWorld / MinimapToWorld — resolve the
    //                         drop pixel to a Z=0 world point.
    //   DispatchSpawnDrop   : BuildSpawnUnitCommand(...) + SpawnMarkerRing.Push
    //                         — emit the Lua line and record the minimap dot.
    // -------------------------------------------------------------------------
    struct Phase4DropResult
    {
        bool        gate_open       = false;  // gate opened for this slot
        bool        drag_started    = false;  // gate_open && payload packed
        bool        spawn_fired     = false;  // a spawn command was produced
        bool        marker_recorded = false;  // SpawnMarkerRing.Push happened
        std::string command;                  // BuildSpawnUnitCommand output
        swfoc_overlay::SpawnDrop world{ 0.0f, 0.0f, 0.0f };  // resolved point
    };

    Phase4DropResult SimulatePhase4Drop(int local_player_slot,
                                        const char* dragged_unit_name,
                                        const char* faction_name,
                                        float drop_px, float drop_py,
                                        bool target_is_minimap,
                                        swfoc_overlay::SpawnMarkerRing& ring)
    {
        using namespace swfoc_overlay;
        Phase4DropResult r;

        // RenderActionsWindow evaluates the multi-player-safety gate ONCE.
        // A closed gate disarms the drag source (spawnAllowed && BeginDrag-
        // DropSource) and makes RenderSpawnPad / RenderMinimap early-return
        // before BeginDragDropTarget — so nothing can spawn.
        r.gate_open = SpawnGateAllowsSpawn(EvaluateSpawnGate(local_player_slot));
        if (!r.gate_open)
        {
            return r;
        }

        // Drag source: pack the dragged unit-type name into the fixed ImGui
        // payload buffer. A failed pack (null name, or a name too long) means
        // "do not start the drag" — drag_started stays false.
        char payload[kUnitTypePayloadCapacity];
        if (!PackUnitTypePayload(dragged_unit_name, payload, sizeof(payload)))
        {
            return r;
        }
        r.drag_started = true;

        // Drop delivery frame: resolve the drop pixel to a Z=0 world point.
        // The pad and the minimap share ONE coordinate convention — Minimap-
        // ToWorld delegates to DropPadToWorld — at two sizes/extents.
        r.world = target_is_minimap
            ? MinimapToWorld(drop_px, drop_py,
                             kMinimapSizePx, kMinimapHalfExtent)
            : DropPadToWorld(drop_px, drop_py,
                             kSpawnPadSizePx, kSpawnPadHalfExtent);

        // DispatchSpawnDrop: build the spawn Lua line and record the minimap
        // marker so the drop shows as a dot.
        r.command = BuildSpawnUnitCommand(faction_name, payload,
                                          r.world.x, r.world.y, r.world.z);
        ring.Push(r.world);
        r.marker_recorded = true;
        r.spawn_fired = true;
        return r;
    }
}

int main()
{
    using namespace swfoc_overlay;
    std::printf("=== Phase 4 close-out integration test (spec iter-296) ===\n\n");

    // =========================================================================
    // [1] SPEC PIN: drop without selection blocks.
    //
    // "Selection" in the overlay is two preconditions: a valid TACTICAL local
    // player (the iter-295 gate) AND a draggable unit-type payload. Missing
    // either one must block the drop — no command, no marker.
    // =========================================================================
    std::printf("[1] Pin: drop without selection blocks\n");
    {
        // RED-GREEN core: gate closed (cold start / galactic transition,
        // slot -1). The "no gate at all" old form spawned anyway — it fails
        // every assertion below.
        SpawnMarkerRing ring;
        const Phase4DropResult r =
            SimulatePhase4Drop(kNoLocalPlayerSlot, "Rebel_Trooper_Squad",
                               "REBEL", 100.0f, 100.0f, false, ring);
        ExpectFalse("gate closed (slot -1): gate_open false", r.gate_open);
        ExpectFalse("gate closed (slot -1): drag never started",
                    r.drag_started);
        ExpectFalse("gate closed (slot -1): spawn did NOT fire",
                    r.spawn_fired);
        ExpectFalse("gate closed (slot -1): no marker recorded",
                    r.marker_recorded);
        ExpectTrue("gate closed (slot -1): command string empty",
                   r.command.empty());
        ExpectIntEq("gate closed (slot -1): marker ring stays empty",
                    static_cast<long long>(ring.Count()), 0);
    }
    {
        // Gate closed defensively: an out-of-range slot is InvalidSlot — a
        // bogus slot must never resolve to a spawn owner.
        SpawnMarkerRing ring;
        const Phase4DropResult r =
            SimulatePhase4Drop(99, "Empire_Stormtrooper_Squad", "EMPIRE",
                               50.0f, 50.0f, true, ring);
        ExpectFalse("gate closed (slot 99 invalid): spawn did NOT fire",
                    r.spawn_fired);
        ExpectIntEq("gate closed (slot 99 invalid): ring empty",
                    static_cast<long long>(ring.Count()), 0);
    }
    {
        // Gate OPEN but no draggable unit type — a null payload name. The
        // drag must not start, so the drop cannot fire a spawn.
        SpawnMarkerRing ring;
        const Phase4DropResult r =
            SimulatePhase4Drop(0, nullptr, "REBEL",
                               100.0f, 100.0f, false, ring);
        ExpectTrue("no payload (null unit name): gate WAS open", r.gate_open);
        ExpectFalse("no payload (null unit name): drag never started",
                    r.drag_started);
        ExpectFalse("no payload (null unit name): spawn did NOT fire",
                    r.spawn_fired);
        ExpectIntEq("no payload (null unit name): ring empty",
                    static_cast<long long>(ring.Count()), 0);
    }
    {
        // Every slot outside the tactical [0,7] range blocks the drop. One
        // shared ring proves NONE of them ever pushes a marker.
        SpawnMarkerRing ring;
        bool all_blocked = true;
        const int blocked_slots[] = { -5, -4, -3, -2, 8, 9, 12, 64, 255 };
        for (const int slot : blocked_slots)
        {
            const Phase4DropResult r =
                SimulatePhase4Drop(slot, "Rebel_Trooper_Squad", "REBEL",
                                   100.0f, 100.0f, false, ring);
            if (r.spawn_fired)
            {
                all_blocked = false;
            }
        }
        ExpectTrue("every non-tactical slot blocks the drop", all_blocked);
        ExpectIntEq("9 blocked drops pushed 0 markers",
                    static_cast<long long>(ring.Count()), 0);
    }

    // =========================================================================
    // [2] SPEC PIN: drop with selection fires.
    //
    // A valid tactical slot + a valid dragged unit type produces a real
    // `return SWFOC_SpawnUnitLua(...)` Lua line at the resolved world point.
    // =========================================================================
    std::printf("\n[2] Pin: drop with selection fires\n");
    {
        // Slot 0, pad CENTER (100,100 on a 200px pad) -> world origin.
        SpawnMarkerRing ring;
        const Phase4DropResult r =
            SimulatePhase4Drop(0, "Rebel_Trooper_Squad", "REBEL",
                               100.0f, 100.0f, false, ring);
        ExpectTrue("slot 0 pad-center: gate open", r.gate_open);
        ExpectTrue("slot 0 pad-center: drag started", r.drag_started);
        ExpectTrue("slot 0 pad-center: spawn fired", r.spawn_fired);
        ExpectTrue("slot 0 pad-center: marker recorded", r.marker_recorded);
        ExpectStartsWith("slot 0 pad-center: command is a SWFOC_SpawnUnitLua "
                         "call", r.command, "return SWFOC_SpawnUnitLua(");
        ExpectContains("slot 0 pad-center: command names the faction",
                       r.command, "REBEL");
        ExpectContains("slot 0 pad-center: command names the unit type",
                       r.command, "Rebel_Trooper_Squad");
        ExpectNear("slot 0 pad-center: world X = 0", r.world.x, 0.0f, 0.01f);
        ExpectNear("slot 0 pad-center: world Y = 0", r.world.y, 0.0f, 0.01f);
        ExpectNear("slot 0 pad-center: world Z = 0 (interim plane)",
                   r.world.z, 0.0f, 0.01f);
        ExpectIntEq("slot 0 pad-center: ring has 1 marker",
                    static_cast<long long>(ring.Count()), 1);
    }
    {
        // Slot 7 (last tactical slot), minimap CENTER (128,128 on 256px) ->
        // world origin — the minimap is just a larger drop pad.
        SpawnMarkerRing ring;
        const Phase4DropResult r =
            SimulatePhase4Drop(7, "Empire_Stormtrooper_Squad", "EMPIRE",
                               128.0f, 128.0f, true, ring);
        ExpectTrue("slot 7 minimap-center: spawn fired", r.spawn_fired);
        ExpectContains("slot 7 minimap-center: command names EMPIRE",
                       r.command, "EMPIRE");
        ExpectNear("slot 7 minimap-center: world X = 0", r.world.x,
                   0.0f, 0.01f);
        ExpectNear("slot 7 minimap-center: world Y = 0", r.world.y,
                   0.0f, 0.01f);
        ExpectIntEq("slot 7 minimap-center: ring has 1 marker",
                    static_cast<long long>(ring.Count()), 1);
    }
    {
        // Slot 3, pad OFF-CENTER (150,50) -> world (+250,+250,0). The command
        // preview must carry the resolved coordinate, not the raw pixel.
        SpawnMarkerRing ring;
        const Phase4DropResult r =
            SimulatePhase4Drop(3, "Rebel_Trooper_Squad", "REBEL",
                               150.0f, 50.0f, false, ring);
        ExpectTrue("slot 3 pad-off-center: spawn fired", r.spawn_fired);
        ExpectNear("slot 3 pad-off-center: world X = +250", r.world.x,
                   250.0f, 0.01f);
        ExpectNear("slot 3 pad-off-center: world Y = +250 (screen-up=north)",
                   r.world.y, 250.0f, 0.01f);
        ExpectContains("slot 3 pad-off-center: command carries the 250 coord",
                       r.command, "250");
    }
    {
        // Explicit RED-GREEN pair on ONE ring: the SAME drop, blocked at
        // slot -1 then fired at slot 0. Count goes 0 -> 0 -> 1.
        SpawnMarkerRing ring;
        const Phase4DropResult blocked =
            SimulatePhase4Drop(kNoLocalPlayerSlot, "Rebel_Trooper_Squad",
                               "REBEL", 100.0f, 100.0f, false, ring);
        ExpectFalse("red-green: slot -1 leg does NOT fire", blocked.spawn_fired);
        ExpectIntEq("red-green: ring still empty after blocked leg",
                    static_cast<long long>(ring.Count()), 0);
        const Phase4DropResult fired =
            SimulatePhase4Drop(0, "Rebel_Trooper_Squad", "REBEL",
                               100.0f, 100.0f, false, ring);
        ExpectTrue("red-green: slot 0 leg fires", fired.spawn_fired);
        ExpectIntEq("red-green: ring has 1 marker after fired leg",
                    static_cast<long long>(ring.Count()), 1);
    }

    // =========================================================================
    // [3] SPEC PIN: preview ring renders.
    //
    // While the operator drags over a spawn target the preview-ring kernel
    // must yield a positive, in-band radius and a visible faction-tinted color
    // for EVERY frame of the breathe cycle — ImGui is never handed a
    // degenerate (non-positive radius / transparent) ring.
    // =========================================================================
    std::printf("\n[3] Pin: preview ring renders\n");
    {
        // Radius at the cardinal phases — base at the ends, peak at mid-cycle.
        ExpectNear("preview radius phase 0.0 = base 14",
                   PreviewRingRadius(kPreviewRingBaseRadius,
                                     kPreviewRingPulseAmplitude, 0.0f),
                   14.0f, 0.01f);
        ExpectNear("preview radius phase 0.5 = base+amp 20 (peak)",
                   PreviewRingRadius(kPreviewRingBaseRadius,
                                     kPreviewRingPulseAmplitude, 0.5f),
                   20.0f, 0.01f);
        ExpectNear("preview radius phase 1.0 = base 14",
                   PreviewRingRadius(kPreviewRingBaseRadius,
                                     kPreviewRingPulseAmplitude, 1.0f),
                   14.0f, 0.01f);
        ExpectNear("preview radius is triangle-symmetric (0.3 == 0.7)",
                   PreviewRingRadius(kPreviewRingBaseRadius,
                                     kPreviewRingPulseAmplitude, 0.3f),
                   PreviewRingRadius(kPreviewRingBaseRadius,
                                     kPreviewRingPulseAmplitude, 0.7f),
                   0.01f);
    }
    {
        // Across one full breathe cycle (frame 0..period inclusive) the
        // radius is ALWAYS positive AND inside [base, base+amp]. A negative
        // or out-of-band frame would mean ImGui gets a degenerate ring.
        bool all_positive = true;
        bool all_in_band = true;
        for (unsigned long long f = 0; f <= kPreviewRingPulseFrames; ++f)
        {
            const float phase = FramePhase01(f, kPreviewRingPulseFrames);
            const float radius = PreviewRingRadius(kPreviewRingBaseRadius,
                                                   kPreviewRingPulseAmplitude,
                                                   phase);
            if (radius <= 0.0f)
            {
                all_positive = false;
            }
            if (radius < kPreviewRingBaseRadius - 0.01f ||
                radius > kPreviewRingBaseRadius +
                             kPreviewRingPulseAmplitude + 0.01f)
            {
                all_in_band = false;
            }
        }
        ExpectTrue("preview radius positive for every frame of the cycle",
                   all_positive);
        ExpectTrue("preview radius in [base, base+amp] for every frame",
                   all_in_band);
    }
    {
        // FramePhase01 wraps — a missing modulo would grow the ring unbounded.
        ExpectNear("preview phase: frame 0 -> 0.0",
                   FramePhase01(0, kPreviewRingPulseFrames), 0.0f, 0.001f);
        ExpectNear("preview phase: frame period/2 -> 0.5",
                   FramePhase01(kPreviewRingPulseFrames / 2,
                                kPreviewRingPulseFrames),
                   0.5f, 0.001f);
        ExpectNear("preview phase: frame == period wraps back to 0.0",
                   FramePhase01(kPreviewRingPulseFrames,
                                kPreviewRingPulseFrames),
                   0.0f, 0.001f);
    }
    {
        // Faction tint: each Phase 4 faction index maps to its iter-92 LED
        // color, and every result carries a VISIBLE (alpha > 0) tint.
        const RingColor rebel = PreviewRingColor(0);
        const RingColor empire = PreviewRingColor(1);
        const RingColor under = PreviewRingColor(2);
        const RingColor other = PreviewRingColor(3);
        ExpectIntEq("preview tint REBEL(0) red channel = 0xFF",
                    static_cast<long long>(rebel.r), 0xFF);
        ExpectIntEq("preview tint REBEL(0) green channel = 0xB4",
                    static_cast<long long>(rebel.g), 0xB4);
        ExpectIntEq("preview tint EMPIRE(1) is chrome 0xDC",
                    static_cast<long long>(empire.r), 0xDC);
        ExpectIntEq("preview tint UNDERWORLD(2) red channel = 0xC8",
                    static_cast<long long>(under.r), 0xC8);
        ExpectIntEq("preview tint unknown faction(3) -> generic green 0xAA",
                    static_cast<long long>(other.g), 0xAA);
        ExpectIntEq("preview tint alpha is the iter-92 0xCC (visible)",
                    static_cast<long long>(rebel.a), kPreviewRingAlpha);
        ExpectTrue("preview tint alpha > 0 -> ring is never transparent",
                   rebel.a > 0 && empire.a > 0 &&
                       under.a > 0 && other.a > 0);
        ExpectTrue("preview tint REBEL != EMPIRE (factions are distinct)",
                   rebel.r != empire.r || rebel.g != empire.g ||
                       rebel.b != empire.b);
    }

    // =========================================================================
    // [4] SPEC PIN: minimap shows units.
    //
    // Every spawn that fires records a marker, and each retained marker
    // projects onto an on-map minimap pixel inside [0, kMinimapSizePx] — the
    // drop becomes a dot the operator can see.
    // =========================================================================
    std::printf("\n[4] Pin: minimap shows units\n");
    {
        // Three drag-drop spawns at three distinct world points. The ring
        // must retain all three, and each must project on-map.
        SpawnMarkerRing ring;
        SimulatePhase4Drop(2, "Rebel_Trooper_Squad", "REBEL",
                           128.0f, 128.0f, true, ring);   // -> world (0,0)
        SimulatePhase4Drop(2, "Rebel_Trooper_Squad", "REBEL",
                           192.0f, 64.0f, true, ring);    // -> world (+1000,+1000)
        SimulatePhase4Drop(2, "Rebel_Trooper_Squad", "REBEL",
                           64.0f, 192.0f, true, ring);    // -> world (-1000,-1000)
        ExpectIntEq("minimap: 3 spawns recorded 3 markers",
                    static_cast<long long>(ring.Count()), 3);

        bool all_on_map = true;
        bool all_in_pixels = true;
        for (std::size_t i = 0; i < ring.Count(); ++i)
        {
            const SpawnDrop& m = ring.At(i);
            const MinimapPoint p = WorldToMinimap(m.x, m.y,
                                                  kMinimapSizePx,
                                                  kMinimapHalfExtent);
            if (!p.onMap)
            {
                all_on_map = false;
            }
            if (p.px < 0.0f || p.px > kMinimapSizePx ||
                p.py < 0.0f || p.py > kMinimapSizePx)
            {
                all_in_pixels = false;
            }
        }
        ExpectTrue("minimap: every recorded marker projects on-map",
                   all_on_map);
        ExpectTrue("minimap: every dot lands inside [0, 256] pixels",
                   all_in_pixels);

        // The oldest marker (At(0)) is the first drop -> world origin ->
        // minimap CENTER. Stable draw order: At(0) oldest, At(Count-1) newest.
        const MinimapPoint first =
            WorldToMinimap(ring.At(0).x, ring.At(0).y,
                           kMinimapSizePx, kMinimapHalfExtent);
        ExpectNear("minimap: oldest marker (origin drop) dots at center X",
                   first.px, 128.0f, 0.01f);
        ExpectNear("minimap: oldest marker (origin drop) dots at center Y",
                   first.py, 128.0f, 0.01f);
    }
    {
        // A drop resolved by MinimapToWorld then projected back by
        // WorldToMinimap must round-trip to the same pixel — the two halves
        // of the minimap coordinate map are exact inverses.
        const SpawnDrop w = MinimapToWorld(192.0f, 64.0f,
                                           kMinimapSizePx, kMinimapHalfExtent);
        const MinimapPoint back = WorldToMinimap(w.x, w.y,
                                                 kMinimapSizePx,
                                                 kMinimapHalfExtent);
        ExpectTrue("minimap: round-trip drop is on-map", back.onMap);
        ExpectNear("minimap: round-trip pixel X identity", back.px,
                   192.0f, 0.05f);
        ExpectNear("minimap: round-trip pixel Y identity", back.py,
                   64.0f, 0.05f);
    }
    {
        // A world point beyond the mapped extent still draws — clamped to the
        // edge — but onMap reports false so the render glue can tint it as an
        // off-extent edge dot.
        const MinimapPoint far_pt = WorldToMinimap(9000.0f, 9000.0f,
                                                   kMinimapSizePx,
                                                   kMinimapHalfExtent);
        ExpectFalse("minimap: off-extent point reports onMap=false",
                    far_pt.onMap);
        ExpectTrue("minimap: off-extent point still clamps inside the widget",
                   far_pt.px >= 0.0f && far_pt.px <= kMinimapSizePx &&
                       far_pt.py >= 0.0f && far_pt.py <= kMinimapSizePx);
    }

    // =========================================================================
    // [5] End-to-end operator session — the four pins chained as one timeline,
    // plus the cross-kernel invariants that hold the pipeline together.
    // =========================================================================
    std::printf("\n[5] End-to-end operator session\n");
    {
        SpawnMarkerRing ring;

        // Cold start: overlay attached, no game in a tactical battle yet
        // (HudSnapshot::local_player_slot defaults to -1). Drag-drop blocked.
        const Phase4DropResult cold =
            SimulatePhase4Drop(kNoLocalPlayerSlot, "Rebel_Trooper_Squad",
                               "REBEL", 100.0f, 100.0f, false, ring);
        ExpectFalse("session: cold-start drop blocked", cold.spawn_fired);
        ExpectIntEq("session: cold-start leaves the minimap empty",
                    static_cast<long long>(ring.Count()), 0);

        // Battle starts — the local player is slot 2. Four drag-drop spawns,
        // a mix of the pad and the minimap, all fire.
        bool all_fired = true;
        const Phase4DropResult drops[] = {
            SimulatePhase4Drop(2, "Rebel_Trooper_Squad", "REBEL",
                               100.0f, 100.0f, false, ring),
            SimulatePhase4Drop(2, "Rebel_Trooper_Squad", "REBEL",
                               0.0f, 0.0f, false, ring),
            SimulatePhase4Drop(2, "Empire_Stormtrooper_Squad", "EMPIRE",
                               128.0f, 128.0f, true, ring),
            SimulatePhase4Drop(2, "Empire_Stormtrooper_Squad", "EMPIRE",
                               256.0f, 0.0f, true, ring),
        };
        for (const Phase4DropResult& d : drops)
        {
            if (!d.spawn_fired)
            {
                all_fired = false;
            }
        }
        ExpectTrue("session: all 4 in-battle drops fired", all_fired);
        ExpectIntEq("session: minimap now shows 4 dots",
                    static_cast<long long>(ring.Count()), 4);

        bool all_on_map = true;
        for (std::size_t i = 0; i < ring.Count(); ++i)
        {
            const MinimapPoint p = WorldToMinimap(ring.At(i).x, ring.At(i).y,
                                                  kMinimapSizePx,
                                                  kMinimapHalfExtent);
            if (!p.onMap)
            {
                all_on_map = false;
            }
        }
        ExpectTrue("session: all 4 session dots project on-map", all_on_map);

        // During the drag the preview ring is tinted for the active faction
        // (REBEL, index 0) and rendered at a positive radius.
        const RingColor tint = PreviewRingColor(0);
        const float live_radius =
            PreviewRingRadius(kPreviewRingBaseRadius,
                              kPreviewRingPulseAmplitude,
                              FramePhase01(30, kPreviewRingPulseFrames));
        ExpectTrue("session: drag preview ring tinted for active faction",
                   tint.a == kPreviewRingAlpha && tint.r == 0xFF);
        ExpectTrue("session: drag preview ring renders at a positive radius",
                   live_radius > 0.0f);
    }
    {
        // Cross-kernel invariants — the seams that keep the four kernels
        // consistent with each other.

        // The minimap is the battlefield-wide view; the pad is the fine
        // placement grid near the origin. The minimap MUST span wider world.
        ExpectTrue("invariant: minimap half-extent wider than the spawn pad",
                   kMinimapHalfExtent > kSpawnPadHalfExtent);

        // Pad center and minimap center BOTH resolve to the world origin —
        // the two drop targets agree on where the middle of the map is.
        const SpawnDrop pad_mid =
            DropPadToWorld(kSpawnPadSizePx * 0.5f, kSpawnPadSizePx * 0.5f,
                           kSpawnPadSizePx, kSpawnPadHalfExtent);
        const SpawnDrop map_mid =
            MinimapToWorld(kMinimapSizePx * 0.5f, kMinimapSizePx * 0.5f,
                           kMinimapSizePx, kMinimapHalfExtent);
        ExpectNear("invariant: pad center -> world origin X", pad_mid.x,
                   0.0f, 0.01f);
        ExpectNear("invariant: minimap center -> world origin X", map_mid.x,
                   0.0f, 0.01f);
        ExpectTrue("invariant: pad and minimap agree on the map center",
                   pad_mid.x == map_mid.x && pad_mid.y == map_mid.y);

        // The drag payload buffer holds the longest current unit-type name
        // with room to spare — a real spawn name must never silently truncate.
        char payload[kUnitTypePayloadCapacity];
        const bool packed = PackUnitTypePayload("Empire_Stormtrooper_Squad",
                                                payload, sizeof(payload));
        ExpectTrue("invariant: longest unit-type name packs without truncation",
                   packed);
        ExpectStrEq("invariant: packed payload round-trips intact",
                    payload, "Empire_Stormtrooper_Squad");
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
