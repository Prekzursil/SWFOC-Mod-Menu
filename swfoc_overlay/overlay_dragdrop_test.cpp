// =============================================================================
// swfoc_overlay/overlay_dragdrop_test.cpp — unit test for overlay_dragdrop.h
// (Phase 4 kickoff, iter 529 / spec iter-292).
//
// Phase 4 adds drag-drop tactical spawning: drag the "Unit type" combo onto a
// fixed square spawn pad, drop, and the unit spawns at the mapped world point.
// overlay_dragdrop.h holds the two pure pieces of that feature — payload
// packing and the pad-pixel -> world-coordinate map. This test pins both so
// the ImGui render glue in overlay.cpp can depend on them build-only.
//
// overlay_dragdrop.h is header-only and std-only; this test also #includes
// overlay_actions.h (also header-only) to prove a DropPadToWorld() result
// composes correctly into the BuildSpawnUnitCommand Lua line — the same
// integration-pin idea as overlay_phase3_catalog_test.cpp's wire-matches-
// builder pins. Build + run via build_dragdrop_test.bat — no game, no pipe.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - PAD CENTER -> ORIGIN  : the pad center must map to world (0,0,0). An
//                             off-by-one in the normalize would shift every
//                             spawn; this pin fails on the old broken form.
//   - SCREEN-UP IS +Y       : the pad TOP edge must map to +Y, the BOTTOM to
//                             -Y. A flipped Y axis (a common drag-drop bug)
//                             fails this pin while still passing a naive
//                             "edges map to +/-halfExtent" check.
//   - NO SILENT TRUNCATION  : PackUnitTypePayload must return false — not a
//                             truncated name — when the name does not fit.
//   - TYPE-ID WITHIN LIMIT  : kUnitTypePayloadId must be <= 32 chars (the
//                             ImGuiPayload::DataType cap) and not start with
//                             '_' (ImGui reserves '_'-prefixed type-ids).
// =============================================================================

#include "overlay_dragdrop.h"
#include "overlay_actions.h"

#include <cstddef>
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

    // Float compare with a small absolute epsilon. Every value the kernel
    // produces in this test is exactly representable (0, +/-250, +/-500,
    // +/-1000), but the epsilon keeps the test robust to a future non-integer
    // halfExtent without rewriting every check.
    void ExpectNear(const char* name, float got, float want)
    {
        ++g_checks;
        const float diff = got - want;
        const float absdiff = diff < 0.0f ? -diff : diff;
        if (absdiff <= 0.001f)
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

    void ExpectEqStr(const char* name, const std::string& got,
                     const std::string& want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %s\n    want: %s\n",
                        name, got.c_str(), want.c_str());
        }
    }

    // Assert a SpawnDrop equals (wantX, wantY, wantZ). One named sub-check per
    // axis so a failure report says which axis drifted.
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

    std::printf("== overlay_dragdrop unit test ==\n");

    // ---- Constants: ImGui drag-drop type-id is well-formed -----------------
    ExpectTrue("id: kUnitTypePayloadId non-null and non-empty",
               kUnitTypePayloadId != nullptr && kUnitTypePayloadId[0] != '\0');
    // PIN type-id within limit: ImGuiPayload::DataType is char[32+1].
    ExpectTrue("PIN id-within-limit: kUnitTypePayloadId <= 32 chars",
               std::strlen(kUnitTypePayloadId) <= 32);
    // ImGui reserves '_'-prefixed type-ids for its own internal payloads.
    ExpectTrue("id: kUnitTypePayloadId does not start with '_'",
               kUnitTypePayloadId[0] != '_');
    // Capacity must hold the longest current unit-type name + terminator
    // ("Empire_Stormtrooper_Squad" = 25 chars).
    ExpectTrue("cap: kUnitTypePayloadCapacity >= 26",
               kUnitTypePayloadCapacity >= 26);
    ExpectTrue("pad: kSpawnPadSizePx is positive", kSpawnPadSizePx > 0.0f);
    ExpectTrue("pad: kSpawnPadHalfExtent is positive",
               kSpawnPadHalfExtent > 0.0f);

    // ---- DropPadToWorld: a 200px pad mapping a 1000x1000 world -------------
    const float kPad = 200.0f;
    const float kHalf = 500.0f;

    // PIN pad-center -> origin: an off-by-one in the normalize shifts this.
    ExpectDrop("PIN center-to-origin: (100,100)",
               DropPadToWorld(100.0f, 100.0f, kPad, kHalf), 0.0f, 0.0f, 0.0f);

    // Four corners. PIN screen-up-is-+Y is embedded here: the top corners
    // (py=0) must yield +Y, the bottom corners (py=200) must yield -Y.
    ExpectDrop("PIN screen-up: top-left (0,0)",
               DropPadToWorld(0.0f, 0.0f, kPad, kHalf), -500.0f, 500.0f, 0.0f);
    ExpectDrop("PIN screen-up: top-right (200,0)",
               DropPadToWorld(200.0f, 0.0f, kPad, kHalf), 500.0f, 500.0f, 0.0f);
    ExpectDrop("corner: bottom-left (0,200)",
               DropPadToWorld(0.0f, 200.0f, kPad, kHalf),
               -500.0f, -500.0f, 0.0f);
    ExpectDrop("corner: bottom-right (200,200)",
               DropPadToWorld(200.0f, 200.0f, kPad, kHalf),
               500.0f, -500.0f, 0.0f);

    // Interior quarter points — linearity of the map.
    ExpectDrop("interior: quarter (50,50)",
               DropPadToWorld(50.0f, 50.0f, kPad, kHalf),
               -250.0f, 250.0f, 0.0f);
    ExpectDrop("interior: three-quarter (150,150)",
               DropPadToWorld(150.0f, 150.0f, kPad, kHalf),
               250.0f, -250.0f, 0.0f);

    // Clamping: a drop a few pixels off the pad resolves to the nearest edge.
    ExpectDrop("clamp: below-left (-30,-30) -> top-left",
               DropPadToWorld(-30.0f, -30.0f, kPad, kHalf),
               -500.0f, 500.0f, 0.0f);
    ExpectDrop("clamp: beyond bottom-right (260,999) -> bottom-right",
               DropPadToWorld(260.0f, 999.0f, kPad, kHalf),
               500.0f, -500.0f, 0.0f);

    // Degenerate pad sizes yield the world origin (no divide-by-zero).
    ExpectDrop("degenerate: padSizePx 0 -> origin",
               DropPadToWorld(40.0f, 40.0f, 0.0f, kHalf), 0.0f, 0.0f, 0.0f);
    ExpectDrop("degenerate: padSizePx -5 -> origin",
               DropPadToWorld(40.0f, 40.0f, -5.0f, kHalf), 0.0f, 0.0f, 0.0f);

    // halfExtent is honoured — a wider world scales the same pad linearly.
    ExpectDrop("halfExtent: center stays origin at half=1000",
               DropPadToWorld(100.0f, 100.0f, kPad, 1000.0f),
               0.0f, 0.0f, 0.0f);
    ExpectDrop("halfExtent: top-left scales to +/-1000 at half=1000",
               DropPadToWorld(0.0f, 0.0f, kPad, 1000.0f),
               -1000.0f, 1000.0f, 0.0f);

    // Monotonic X: dragging rightwards always increases world X.
    ExpectTrue("monotonic: X(px=50) < X(px=150)",
               DropPadToWorld(50.0f, 100.0f, kPad, kHalf).x <
               DropPadToWorld(150.0f, 100.0f, kPad, kHalf).x);

    // ---- PackUnitTypePayload ----------------------------------------------
    {
        char buf[kUnitTypePayloadCapacity];
        ExpectTrue("pack: normal name fits",
                   PackUnitTypePayload("Empire_AT_AT", buf, sizeof(buf)));
        ExpectEqStr("pack: normal name copied verbatim", buf, "Empire_AT_AT");

        // The longest real unit-type name must fit the declared capacity.
        ExpectTrue("pack: longest unit name fits kUnitTypePayloadCapacity",
                   PackUnitTypePayload("Empire_Stormtrooper_Squad", buf,
                                       sizeof(buf)));

        // Empty name is valid — fits trivially, copies the terminator.
        ExpectTrue("pack: empty name fits",
                   PackUnitTypePayload("", buf, sizeof(buf)));
        ExpectTrue("pack: empty name leaves empty buffer", buf[0] == '\0');
    }
    {
        // Exact fit: a name of length cap-1 fits with its terminator.
        char tiny[13];
        ExpectTrue("pack: exact fit (12 chars into cap 13)",
                   PackUnitTypePayload("123456789012", tiny, sizeof(tiny)));
        ExpectEqStr("pack: exact-fit name copied verbatim", tiny,
                    "123456789012");
    }
    {
        // PIN no-silent-truncation: a name that needs cap+1 bytes (name +
        // terminator) must fail, NOT copy a truncated name.
        char tiny[12];
        std::memset(tiny, 'Z', sizeof(tiny));  // poison: prove it gets cleared
        ExpectTrue("PIN no-truncation: 12-char name into cap 12 fails",
                   !PackUnitTypePayload("123456789012", tiny, sizeof(tiny)));
        ExpectTrue("PIN no-truncation: failed pack leaves buffer empty",
                   tiny[0] == '\0');
    }
    {
        char small[5];
        std::memset(small, 'Z', sizeof(small));
        ExpectTrue("pack: far-too-long name fails",
                   !PackUnitTypePayload("ABCDEFGHIJ", small, sizeof(small)));
        ExpectTrue("pack: far-too-long failure leaves buffer empty",
                   small[0] == '\0');
    }
    {
        char buf[kUnitTypePayloadCapacity];
        std::memset(buf, 'Z', sizeof(buf));
        ExpectTrue("pack: null name fails",
                   !PackUnitTypePayload(nullptr, buf, sizeof(buf)));
        ExpectTrue("pack: null-name failure leaves buffer empty",
                   buf[0] == '\0');
        ExpectTrue("pack: null buffer fails",
                   !PackUnitTypePayload("x", nullptr, sizeof(buf)));
        ExpectTrue("pack: zero capacity fails",
                   !PackUnitTypePayload("x", buf, 0));
    }

    // ---- Integration: DropPadToWorld result feeds BuildSpawnUnitCommand ----
    // Proves the kernel composes with the existing overlay_actions.h builder —
    // the exact path the spawn-pad drop handler in overlay.cpp takes.
    {
        const SpawnDrop center = DropPadToWorld(100.0f, 100.0f, kPad, kHalf);
        const std::string cmd = BuildSpawnUnitCommand(
            "REBEL", "Empire_AT_AT", center.x, center.y, center.z);
        ExpectTrue("integration: center spawn calls SWFOC_SpawnUnitLua",
                   Contains(cmd, "SWFOC_SpawnUnitLua"));
        ExpectTrue("integration: center spawn maps to Create_Position(0, 0, 0)",
                   Contains(cmd, "Create_Position(0, 0, 0)"));
    }
    {
        const SpawnDrop tl = DropPadToWorld(0.0f, 0.0f, kPad, kHalf);
        const std::string cmd = BuildSpawnUnitCommand(
            "EMPIRE", "Empire_AT_AT", tl.x, tl.y, tl.z);
        ExpectTrue("integration: top-left spawn maps to "
                   "Create_Position(-500, 500, 0)",
                   Contains(cmd, "Create_Position(-500, 500, 0)"));
    }
    {
        const SpawnDrop br = DropPadToWorld(200.0f, 200.0f, kPad, kHalf);
        const std::string cmd = BuildSpawnUnitCommand(
            "EMPIRE", "Empire_AT_AT", br.x, br.y, br.z);
        ExpectTrue("integration: bottom-right spawn maps to "
                   "Create_Position(500, -500, 0)",
                   Contains(cmd, "Create_Position(500, -500, 0)"));
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
