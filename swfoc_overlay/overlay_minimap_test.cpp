// =============================================================================
// swfoc_overlay/overlay_minimap_test.cpp — unit test for overlay_minimap.h
// (Phase 4 cont., iter 530 / spec iter-293).
//
// iter-293 adds the 256x256 tactical minimap. overlay_minimap.h holds the pure
// pieces: WorldToMinimap (project a world point to a minimap pixel — draws the
// spawn-marker dots), MinimapToWorld (map a drop pixel back to the world — the
// drag-drop target), and SpawnMarkerRing (the fixed-capacity dot history).
// This test pins all three so the ImGui render glue in overlay.cpp can depend
// on them build-only.
//
// overlay_minimap.h includes overlay_dragdrop.h (SpawnDrop, DropPadToWorld);
// this test also #includes overlay_actions.h to prove a MinimapToWorld result
// composes into BuildSpawnUnitCommand — the same integration-pin idea as the
// sibling overlay tests. Build + run via build_minimap_test.bat — no game.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - ORIGIN -> CENTER     : world (0,0) must map to the minimap center. An
//                            off-by-one in the normalize shifts every dot.
//   - SCREEN-UP IS +Y      : world +Y (north) must map to the TOP of the
//                            minimap (py small). A flipped Y axis draws every
//                            spawn dot upside-down and fails this pin.
//   - ROUND-TRIP IDENTITY  : MinimapToWorld(WorldToMinimap(p)) == p for any
//                            on-map point. A flipped axis or a normalize
//                            off-by-one breaks the round trip even if each
//                            half looks plausible alone.
//   - OLDEST EVICTED FIRST : once SpawnMarkerRing is full, Push() must evict
//                            the OLDEST marker (At(0)), not the newest.
// =============================================================================

#include "overlay_minimap.h"
#include "overlay_actions.h"

#include <cstddef>
#include <cstdio>
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

    // Float compare with a small absolute epsilon. The minimap maps a 256px
    // square over a 4000-wide world; every value pinned below is chosen so
    // the normalize lands on an exactly-representable float, but the epsilon
    // keeps the round-trip checks robust to one ulp of accumulated error.
    void ExpectNear(const char* name, float got, float want)
    {
        ++g_checks;
        const float diff = got - want;
        const float absdiff = diff < 0.0f ? -diff : diff;
        if (absdiff <= 0.01f)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %.4f\n    want: %.4f\n",
                        name, static_cast<double>(got),
                        static_cast<double>(want));
        }
    }

    void ExpectEqSize(const char* name, std::size_t got, std::size_t want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %zu\n    want: %zu\n",
                        name, got, want);
        }
    }

    // Assert a MinimapPoint equals (wantPx, wantPy, wantOnMap). One named
    // sub-check per field so a failure report says which field drifted.
    void ExpectPoint(const char* name, const swfoc_overlay::MinimapPoint& got,
                     float wantPx, float wantPy, bool wantOnMap)
    {
        char nm[128];
        std::snprintf(nm, sizeof(nm), "%s [px]", name);
        ExpectNear(nm, got.px, wantPx);
        std::snprintf(nm, sizeof(nm), "%s [py]", name);
        ExpectNear(nm, got.py, wantPy);
        std::snprintf(nm, sizeof(nm), "%s [onMap]", name);
        ExpectTrue(nm, got.onMap == wantOnMap);
    }

    // Assert a SpawnDrop equals (wantX, wantY, wantZ).
    void ExpectDrop(const char* name, const swfoc_overlay::SpawnDrop& got,
                    float wantX, float wantY, float wantZ)
    {
        char nm[128];
        std::snprintf(nm, sizeof(nm), "%s [x]", name);
        ExpectNear(nm, got.x, wantX);
        std::snprintf(nm, sizeof(nm), "%s [y]", name);
        ExpectNear(nm, got.y, wantY);
        std::snprintf(nm, sizeof(nm), "%s [z]", name);
        ExpectNear(nm, got.z, wantZ);
    }

    // True when `haystack` contains `needle` as a substring.
    bool Contains(const std::string& haystack, const char* needle)
    {
        return haystack.find(needle) != std::string::npos;
    }
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_minimap unit test ==\n");

    // ---- Constants ---------------------------------------------------------
    ExpectTrue("const: kMinimapSizePx is positive", kMinimapSizePx > 0.0f);
    ExpectTrue("const: kMinimapHalfExtent is positive",
               kMinimapHalfExtent > 0.0f);
    // The minimap is the battlefield-wide view; it must cover more world than
    // the iter-292 fine-placement spawn pad.
    ExpectTrue("const: minimap extent exceeds spawn-pad extent",
               kMinimapHalfExtent > kSpawnPadHalfExtent);
    ExpectTrue("const: kMinimapMarkerCapacity is non-zero",
               kMinimapMarkerCapacity > 0);

    const float kSize = 256.0f;
    const float kHalf = 2000.0f;
    ExpectNear("const: kMinimapSizePx == 256", kMinimapSizePx, kSize);
    ExpectNear("const: kMinimapHalfExtent == 2000", kMinimapHalfExtent, kHalf);

    // ---- WorldToMinimap ----------------------------------------------------
    // PIN origin-to-center: an off-by-one in the normalize shifts every dot.
    ExpectPoint("PIN origin-to-center: world (0,0)",
                WorldToMinimap(0.0f, 0.0f, kSize, kHalf), 128.0f, 128.0f, true);

    // Four corners. PIN screen-up-is-+Y is embedded: the top corners (world
    // +Y) must yield py=0, the bottom corners (world -Y) must yield py=256.
    ExpectPoint("PIN screen-up: world (-2000,+2000) -> top-left",
                WorldToMinimap(-2000.0f, 2000.0f, kSize, kHalf),
                0.0f, 0.0f, true);
    ExpectPoint("PIN screen-up: world (+2000,+2000) -> top-right",
                WorldToMinimap(2000.0f, 2000.0f, kSize, kHalf),
                256.0f, 0.0f, true);
    ExpectPoint("corner: world (-2000,-2000) -> bottom-left",
                WorldToMinimap(-2000.0f, -2000.0f, kSize, kHalf),
                0.0f, 256.0f, true);
    ExpectPoint("corner: world (+2000,-2000) -> bottom-right",
                WorldToMinimap(2000.0f, -2000.0f, kSize, kHalf),
                256.0f, 256.0f, true);

    // Screen-up axis isolated: same X, opposite Y.
    ExpectPoint("screen-up: world (0,+2000) -> top edge center",
                WorldToMinimap(0.0f, 2000.0f, kSize, kHalf),
                128.0f, 0.0f, true);
    ExpectPoint("screen-up: world (0,-2000) -> bottom edge center",
                WorldToMinimap(0.0f, -2000.0f, kSize, kHalf),
                128.0f, 256.0f, true);

    // Interior points — linearity of the map.
    ExpectPoint("interior: world (+1000,+1000)",
                WorldToMinimap(1000.0f, 1000.0f, kSize, kHalf),
                192.0f, 64.0f, true);
    ExpectPoint("interior: world (-1000,-1000)",
                WorldToMinimap(-1000.0f, -1000.0f, kSize, kHalf),
                64.0f, 192.0f, true);

    // Off-map points: onMap is false but the pixel is CLAMPED to the edge so
    // the dot still draws on the widget.
    ExpectPoint("off-map: world (+3000,0) clamps to right edge, onMap false",
                WorldToMinimap(3000.0f, 0.0f, kSize, kHalf),
                256.0f, 128.0f, false);
    ExpectPoint("off-map: world (0,+5000) clamps to top edge, onMap false",
                WorldToMinimap(0.0f, 5000.0f, kSize, kHalf),
                128.0f, 0.0f, false);
    ExpectPoint("off-map: world (-9000,-9000) clamps to bottom-left",
                WorldToMinimap(-9000.0f, -9000.0f, kSize, kHalf),
                0.0f, 256.0f, false);

    // Boundary is ON-map: a point exactly at +halfExtent counts as in-extent.
    ExpectTrue("boundary: world (+2000,0) is onMap",
               WorldToMinimap(2000.0f, 0.0f, kSize, kHalf).onMap);

    // Clamp guarantee: even a wildly off-map input never returns px/py
    // outside [0, sizePx].
    {
        const MinimapPoint far = WorldToMinimap(1.0e9f, -1.0e9f, kSize, kHalf);
        ExpectTrue("clamp: far px within [0,size]",
                   far.px >= 0.0f && far.px <= kSize);
        ExpectTrue("clamp: far py within [0,size]",
                   far.py >= 0.0f && far.py <= kSize);
        ExpectTrue("clamp: far point is not onMap", !far.onMap);
    }

    // Degenerate minimap dimensions yield { 0, 0, false } — no divide-by-zero.
    ExpectPoint("degenerate: sizePx 0 -> {0,0,false}",
                WorldToMinimap(500.0f, 500.0f, 0.0f, kHalf),
                0.0f, 0.0f, false);
    ExpectPoint("degenerate: halfExtent 0 -> {0,0,false}",
                WorldToMinimap(500.0f, 500.0f, kSize, 0.0f),
                0.0f, 0.0f, false);
    ExpectPoint("degenerate: halfExtent negative -> {0,0,false}",
                WorldToMinimap(500.0f, 500.0f, kSize, -10.0f),
                0.0f, 0.0f, false);

    // ---- MinimapToWorld (delegates to DropPadToWorld) ----------------------
    ExpectDrop("M2W: center (128,128) -> world origin",
               MinimapToWorld(128.0f, 128.0f, kSize, kHalf),
               0.0f, 0.0f, 0.0f);
    ExpectDrop("M2W: top-left (0,0) -> world (-2000,+2000,0)",
               MinimapToWorld(0.0f, 0.0f, kSize, kHalf),
               -2000.0f, 2000.0f, 0.0f);
    ExpectDrop("M2W: bottom-right (256,256) -> world (+2000,-2000,0)",
               MinimapToWorld(256.0f, 256.0f, kSize, kHalf),
               2000.0f, -2000.0f, 0.0f);
    ExpectDrop("M2W: off-pad pixel clamps to an edge",
               MinimapToWorld(-50.0f, 9999.0f, kSize, kHalf),
               -2000.0f, -2000.0f, 0.0f);

    // ---- PIN round-trip identity ------------------------------------------
    // WorldToMinimap then MinimapToWorld must return the original world point
    // for any on-map input. A flipped axis breaks this even when each half
    // passes its own corner checks.
    {
        const float kWorlds[][2] = {
            { 0.0f, 0.0f }, { 500.0f, 1000.0f }, { -1000.0f, -500.0f },
            { 1500.0f, -1500.0f }, { -2000.0f, 2000.0f },
        };
        for (const auto& w : kWorlds)
        {
            const MinimapPoint p = WorldToMinimap(w[0], w[1], kSize, kHalf);
            const SpawnDrop back = MinimapToWorld(p.px, p.py, kSize, kHalf);
            char nm[96];
            std::snprintf(nm, sizeof(nm),
                          "PIN round-trip: world (%.0f,%.0f) survives W2M->M2W",
                          static_cast<double>(w[0]),
                          static_cast<double>(w[1]));
            ExpectDrop(nm, back, w[0], w[1], 0.0f);
        }
    }
    // Reverse round-trip: a minimap pixel survives M2W -> W2M.
    {
        const float kPixels[][2] = {
            { 128.0f, 128.0f }, { 64.0f, 192.0f }, { 192.0f, 64.0f },
            { 0.0f, 0.0f }, { 256.0f, 256.0f },
        };
        for (const auto& q : kPixels)
        {
            const SpawnDrop w = MinimapToWorld(q[0], q[1], kSize, kHalf);
            const MinimapPoint back =
                WorldToMinimap(w.x, w.y, kSize, kHalf);
            char nm[96];
            std::snprintf(nm, sizeof(nm),
                          "round-trip: pixel (%.0f,%.0f) survives M2W->W2M",
                          static_cast<double>(q[0]),
                          static_cast<double>(q[1]));
            ExpectPoint(nm, back, q[0], q[1], true);
        }
    }

    // ---- SpawnMarkerRing ---------------------------------------------------
    {
        SpawnMarkerRing ring;
        ExpectEqSize("ring: fresh ring has count 0", ring.Count(), 0);
        ExpectTrue("ring: fresh ring is Empty", ring.Empty());

        ring.Push(SpawnDrop{ 10.0f, 20.0f, 0.0f });
        ExpectEqSize("ring: one push -> count 1", ring.Count(), 1);
        ExpectTrue("ring: one push -> not Empty", !ring.Empty());
        ExpectDrop("ring: At(0) is the pushed marker",
                   ring.At(0), 10.0f, 20.0f, 0.0f);

        ring.Push(SpawnDrop{ 30.0f, 40.0f, 0.0f });
        ring.Push(SpawnDrop{ 50.0f, 60.0f, 0.0f });
        ExpectEqSize("ring: three pushes -> count 3", ring.Count(), 3);
        // At(0) oldest, At(Count()-1) newest — a stable draw order.
        ExpectDrop("ring: At(0) stays the oldest marker",
                   ring.At(0), 10.0f, 20.0f, 0.0f);
        ExpectDrop("ring: At(2) is the newest marker",
                   ring.At(2), 50.0f, 60.0f, 0.0f);

        ring.Clear();
        ExpectEqSize("ring: Clear() resets count to 0", ring.Count(), 0);
        ExpectTrue("ring: Clear() makes the ring Empty", ring.Empty());
    }
    {
        // Fill exactly to capacity, then PIN that overflow evicts the OLDEST.
        SpawnMarkerRing ring;
        for (std::size_t i = 0; i < kMinimapMarkerCapacity; ++i)
        {
            ring.Push(SpawnDrop{ static_cast<float>(i), 0.0f, 0.0f });
        }
        ExpectEqSize("ring: filled to capacity",
                     ring.Count(), kMinimapMarkerCapacity);
        ExpectDrop("ring: At(0) is the very first marker before overflow",
                   ring.At(0), 0.0f, 0.0f, 0.0f);

        // One more push: count stays at capacity, marker 0 is evicted.
        ring.Push(SpawnDrop{ 999.0f, 0.0f, 0.0f });
        ExpectEqSize("ring: count stays at capacity after overflow",
                     ring.Count(), kMinimapMarkerCapacity);
        ExpectDrop("PIN oldest-evicted: At(0) is now the SECOND marker",
                   ring.At(0), 1.0f, 0.0f, 0.0f);
        ExpectDrop("ring: newest marker landed at At(capacity-1)",
                   ring.At(kMinimapMarkerCapacity - 1), 999.0f, 0.0f, 0.0f);
    }
    {
        // Heavy overflow: push 2x capacity + 6, only the last `capacity`
        // markers survive, in order.
        SpawnMarkerRing ring;
        const std::size_t total = kMinimapMarkerCapacity * 2 + 6;
        for (std::size_t i = 0; i < total; ++i)
        {
            ring.Push(SpawnDrop{ static_cast<float>(i), 0.0f, 0.0f });
        }
        ExpectEqSize("ring: heavy overflow stays at capacity",
                     ring.Count(), kMinimapMarkerCapacity);
        ExpectDrop("ring: At(0) is total-capacity (oldest survivor)",
                   ring.At(0),
                   static_cast<float>(total - kMinimapMarkerCapacity),
                   0.0f, 0.0f);
        ExpectDrop("ring: At(capacity-1) is the last pushed",
                   ring.At(kMinimapMarkerCapacity - 1),
                   static_cast<float>(total - 1), 0.0f, 0.0f);
    }

    // ---- Integration: MinimapToWorld result feeds BuildSpawnUnitCommand ----
    // Proves the kernel composes with the existing overlay_actions.h builder —
    // the exact path the minimap drop handler in overlay.cpp takes.
    {
        const SpawnDrop center = MinimapToWorld(128.0f, 128.0f, kSize, kHalf);
        const std::string cmd = BuildSpawnUnitCommand(
            "REBEL", "Empire_AT_AT", center.x, center.y, center.z);
        ExpectTrue("integration: center drop calls SWFOC_SpawnUnitLua",
                   Contains(cmd, "SWFOC_SpawnUnitLua"));
        ExpectTrue("integration: center drop maps to Create_Position(0, 0, 0)",
                   Contains(cmd, "Create_Position(0, 0, 0)"));
    }
    {
        const SpawnDrop tl = MinimapToWorld(0.0f, 0.0f, kSize, kHalf);
        const std::string cmd = BuildSpawnUnitCommand(
            "EMPIRE", "Empire_AT_AT", tl.x, tl.y, tl.z);
        ExpectTrue("integration: top-left drop maps to "
                   "Create_Position(-2000, 2000, 0)",
                   Contains(cmd, "Create_Position(-2000, 2000, 0)"));
    }
    {
        const SpawnDrop br = MinimapToWorld(256.0f, 256.0f, kSize, kHalf);
        const std::string cmd = BuildSpawnUnitCommand(
            "EMPIRE", "Empire_AT_AT", br.x, br.y, br.z);
        ExpectTrue("integration: bottom-right drop maps to "
                   "Create_Position(2000, -2000, 0)",
                   Contains(cmd, "Create_Position(2000, -2000, 0)"));
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
