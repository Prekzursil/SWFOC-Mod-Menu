// =============================================================================
// swfoc_overlay/overlay_inspector_test.cpp — unit test for overlay_inspector.h
// (Phase 5 cont., iter 539 / spec iter-300).
//
// iter-300 is the click-to-inspect link of Phase 5: once iter-299's raycast
// names the unit under the cursor, this iter presents it — an "Inspector"
// overlay panel showing hull / shield / owner / type / position (read-only;
// the write-side action buttons are iter-301). overlay_inspector.h holds the
// pure panel logic — clamped health math, faction naming, the exact row text,
// handle resolution, and the open / update / refresh controller. This test
// pins all of it so the iter-300 ImGui::Begin("Inspector") glue can depend on
// it build-only.
//
// The integration section wires the kernel to its real upstream: it builds a
// visible-unit AABB set, runs overlay_hit_test.h's NearestUnitHit to pick a
// unit, feeds that UnitHit into OpenInspectorFor against a parallel UnitInfo
// set, and confirms the panel shows the picked unit — then kills the unit and
// confirms RefreshInspectorPanel closes the panel.
//
// overlay_inspector.h is header-only and std-only (<cstdint>/<cstdio>, plus
// <cmath> via the include chain). Build + run via build_inspector_test.bat —
// no game, no pipe, no ImGui.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - CLICK OPENS INSPECTOR : a valid UnitHit opens the panel on the unit.
//   - MISS LEAVES PANEL     : UpdateInspectorPanel on a miss returns the prior
//                             panel unchanged — no flicker-shut.
//   - HEALTH PERCENT CLAMPS : over-heal -> 100%, negative -> 0%, max<=0 ->
//                             "n/a" — a bare current/max overflows past 100.
//   - DEAD UNIT AUTO-CLOSES : RefreshInspectorPanel against a snapshot missing
//                             the inspected handle closes the panel.
//   - FACTION NAME CORRECT  : FactionName(0) is "Rebel", not "Unknown".
//   - RESOLVE BY HANDLE     : OpenInspectorFor finds the unit by handle, so a
//                             reordered list still inspects the right unit.
// =============================================================================

#include "overlay_inspector.h"

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

    void ExpectInt(const char* name, int got, int want)
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
                        name, got, want);
        }
    }

    void ExpectNearEps(const char* name, float got, float want, float eps)
    {
        ++g_checks;
        const float diff = got - want;
        const float absdiff = diff < 0.0f ? -diff : diff;
        if (absdiff <= eps)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %.5f\n    want: %.5f\n",
                        name, static_cast<double>(got),
                        static_cast<double>(want));
        }
    }

    void ExpectNear(const char* name, float got, float want)
    {
        ExpectNearEps(name, got, want, 0.001f);
    }

    void ExpectStr(const char* name, const char* got, const char* want)
    {
        ++g_checks;
        if (got != nullptr && want != nullptr && std::strcmp(got, want) == 0)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : \"%s\"\n    want: \"%s\"\n",
                        name, got != nullptr ? got : "(null)",
                        want != nullptr ? want : "(null)");
        }
    }

    void ExpectU64(const char* name, std::uint64_t got, std::uint64_t want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : 0x%llX\n    want: 0x%llX\n",
                        name, static_cast<unsigned long long>(got),
                        static_cast<unsigned long long>(want));
        }
    }

    using swfoc_overlay::Aabb;
    using swfoc_overlay::InspectorPanel;
    using swfoc_overlay::UnitAabb;
    using swfoc_overlay::UnitHit;
    using swfoc_overlay::UnitInfo;
    using swfoc_overlay::Vec3;
    using swfoc_overlay::WorldRay;

    // Build a UnitInfo in one expression — the parallel of iter-299's UnitAabb,
    // carrying the fields the inspector renders.
    UnitInfo MakeUnit(std::uint64_t handle, float hull, float hullMax,
                      float shield, float shieldMax, int owner,
                      const Vec3& pos, const char* type)
    {
        UnitInfo u{};
        u.handle    = handle;
        u.hull      = hull;
        u.hullMax   = hullMax;
        u.shield    = shield;
        u.shieldMax = shieldMax;
        u.owner     = owner;
        u.position  = pos;
        swfoc_overlay::SetUnitType(u, type);
        return u;
    }

    // Build a UnitHit directly — for the panel-controller sections that pin
    // behaviour without running the whole raycast.
    UnitHit MakeHit(bool hit, int index, std::uint64_t handle, float t)
    {
        UnitHit h{};
        h.hit    = hit;
        h.index  = index;
        h.handle = handle;
        h.t      = t;
        return h;
    }

    // A valid pick ray with a unit-length direction, so a returned `t` reads
    // directly as a world-space distance (mirrors overlay_hit_test_test.cpp).
    WorldRay MakeRay(const Vec3& origin, const Vec3& dir)
    {
        WorldRay r{};
        r.origin    = origin;
        r.direction = swfoc_overlay::Vec3Normalize(dir);
        r.valid     = true;
        return r;
    }
}

int main()
{
    using swfoc_overlay::FactionName;
    using swfoc_overlay::FindUnitByHandle;
    using swfoc_overlay::FormatHealthLabel;
    using swfoc_overlay::FormatPositionLabel;
    using swfoc_overlay::HealthFraction;
    using swfoc_overlay::HealthPercent;
    using swfoc_overlay::OpenInspectorFor;
    using swfoc_overlay::RefreshInspectorPanel;
    using swfoc_overlay::SetUnitType;
    using swfoc_overlay::UpdateInspectorPanel;
    using swfoc_overlay::kMaxRaycastUnits;
    using swfoc_overlay::kUnitTypeNameMax;

    std::printf("=== overlay_inspector.h unit test (iter 539 / spec 300) ===\n\n");

    // ---- 1. HealthFraction / HealthPercent --------------------------------
    std::printf("[health math]\n");
    ExpectNear("HealthFraction(50,100) is 0.5", HealthFraction(50.0f, 100.0f),
               0.5f);
    ExpectNear("HealthFraction(0,100) is 0.0", HealthFraction(0.0f, 100.0f),
               0.0f);
    ExpectNear("HealthFraction(100,100) is 1.0", HealthFraction(100.0f, 100.0f),
               1.0f);
    ExpectNear("HealthFraction over-heal (150,100) clamps to 1.0",
               HealthFraction(150.0f, 100.0f), 1.0f);
    ExpectNear("HealthFraction negative (-20,100) clamps to 0.0",
               HealthFraction(-20.0f, 100.0f), 0.0f);
    ExpectNear("HealthFraction zero max (50,0) is 0.0",
               HealthFraction(50.0f, 0.0f), 0.0f);
    ExpectNear("HealthFraction negative max (50,-100) is 0.0",
               HealthFraction(50.0f, -100.0f), 0.0f);
    ExpectInt("HealthPercent(3600,5000) is 72", HealthPercent(3600.0f, 5000.0f),
              72);
    ExpectInt("HealthPercent(2500,5000) is 50", HealthPercent(2500.0f, 5000.0f),
              50);
    ExpectInt("HealthPercent(5000,5000) is 100",
              HealthPercent(5000.0f, 5000.0f), 100);
    // RED-GREEN: HEALTH PERCENT CLAMPS — a bare current/max gives 140 here.
    ExpectInt("HealthPercent over-heal (7000,5000) clamps to 100",
              HealthPercent(7000.0f, 5000.0f), 100);
    ExpectInt("HealthPercent negative (-10,5000) clamps to 0",
              HealthPercent(-10.0f, 5000.0f), 0);
    ExpectInt("HealthPercent zero max (10,0) is 0",
              HealthPercent(10.0f, 0.0f), 0);
    ExpectInt("HealthPercent(0,5000) is 0", HealthPercent(0.0f, 5000.0f), 0);
    std::printf("\n");

    // ---- 2. FactionName ---------------------------------------------------
    std::printf("[faction name]\n");
    // RED-GREEN: FACTION NAME CORRECT — slot 0 is Rebel, not Unknown.
    ExpectStr("FactionName(0) is Rebel", FactionName(0), "Rebel");
    ExpectStr("FactionName(1) is Empire", FactionName(1), "Empire");
    ExpectStr("FactionName(2) is Underworld", FactionName(2), "Underworld");
    ExpectStr("FactionName(3) is Unknown", FactionName(3), "Unknown");
    ExpectStr("FactionName(-1) is Unknown", FactionName(-1), "Unknown");
    std::printf("\n");

    // ---- 3. FormatHealthLabel ---------------------------------------------
    std::printf("[health label]\n");
    {
        char buf[64];
        FormatHealthLabel(buf, sizeof(buf), 3600.0f, 5000.0f);
        ExpectStr("FormatHealthLabel(3600,5000)", buf, "3600 / 5000 (72%)");
        FormatHealthLabel(buf, sizeof(buf), 5000.0f, 5000.0f);
        ExpectStr("FormatHealthLabel(5000,5000)", buf, "5000 / 5000 (100%)");
        // RED-GREEN: max<=0 reads "n/a" rather than dividing by zero.
        FormatHealthLabel(buf, sizeof(buf), 10.0f, 0.0f);
        ExpectStr("FormatHealthLabel zero max is n/a", buf, "n/a");
        FormatHealthLabel(buf, sizeof(buf), 10.0f, -5.0f);
        ExpectStr("FormatHealthLabel negative max is n/a", buf, "n/a");
        FormatHealthLabel(buf, sizeof(buf), 7000.0f, 5000.0f);
        ExpectStr("FormatHealthLabel over-heal clamps shown current", buf,
                  "5000 / 5000 (100%)");
        FormatHealthLabel(buf, sizeof(buf), -50.0f, 5000.0f);
        ExpectStr("FormatHealthLabel negative current shows 0", buf,
                  "0 / 5000 (0%)");
        // Null-buffer / zero-capacity guard: a sentinel must survive untouched.
        char sentinel[16];
        std::strcpy(sentinel, "UNTOUCHED");
        FormatHealthLabel(sentinel, 0, 1.0f, 2.0f);
        ExpectStr("FormatHealthLabel cap 0 is a no-op", sentinel, "UNTOUCHED");
    }
    std::printf("\n");

    // ---- 4. FormatPositionLabel -------------------------------------------
    std::printf("[position label]\n");
    {
        char buf[64];
        FormatPositionLabel(buf, sizeof(buf), Vec3{ 128.0f, -64.0f, 0.0f });
        ExpectStr("FormatPositionLabel(128,-64,0)", buf, "(128.0, -64.0, 0.0)");
        FormatPositionLabel(buf, sizeof(buf), Vec3{ 0.0f, 0.0f, 0.0f });
        ExpectStr("FormatPositionLabel origin", buf, "(0.0, 0.0, 0.0)");
        char sentinel[16];
        std::strcpy(sentinel, "UNTOUCHED");
        FormatPositionLabel(sentinel, 0, Vec3{ 1.0f, 2.0f, 3.0f });
        ExpectStr("FormatPositionLabel cap 0 is a no-op", sentinel,
                  "UNTOUCHED");
    }
    std::printf("\n");

    // ---- 5. SetUnitType ---------------------------------------------------
    std::printf("[set unit type]\n");
    {
        UnitInfo u{};
        SetUnitType(u, "Empire_AT_AT");
        ExpectStr("SetUnitType copies a normal name", u.type, "Empire_AT_AT");
        ExpectInt("SetUnitType normal-name length",
                  static_cast<int>(std::strlen(u.type)), 12);

        // Truncation: a name longer than the buffer is cut, still nul-term.
        char longName[128];
        for (int i = 0; i < 127; ++i)
        {
            longName[i] = 'X';
        }
        longName[127] = '\0';
        SetUnitType(u, longName);
        ExpectInt("SetUnitType truncates to kUnitTypeNameMax-1",
                  static_cast<int>(std::strlen(u.type)),
                  kUnitTypeNameMax - 1);
        ExpectTrue("SetUnitType truncated string stays nul-terminated",
                   u.type[kUnitTypeNameMax - 1] == '\0');

        SetUnitType(u, nullptr);
        ExpectStr("SetUnitType(null) yields an empty string", u.type, "");
    }
    std::printf("\n");

    // ---- 6. FindUnitByHandle ----------------------------------------------
    std::printf("[find unit by handle]\n");
    {
        const UnitInfo trio[3] = {
            MakeUnit(0xAAull, 100, 100, 0, 0, 0, Vec3{}, "A"),
            MakeUnit(0xBBull, 100, 100, 0, 0, 1, Vec3{}, "B"),
            MakeUnit(0xCCull, 100, 100, 0, 0, 2, Vec3{}, "C"),
        };
        ExpectInt("FindUnitByHandle finds the middle unit",
                  FindUnitByHandle(trio, 3, 0xBBull), 1);
        ExpectInt("FindUnitByHandle finds the first unit",
                  FindUnitByHandle(trio, 3, 0xAAull), 0);
        ExpectInt("FindUnitByHandle finds the last unit",
                  FindUnitByHandle(trio, 3, 0xCCull), 2);
        ExpectInt("FindUnitByHandle returns -1 for an absent handle",
                  FindUnitByHandle(trio, 3, 0xFFFFull), -1);
        ExpectInt("FindUnitByHandle returns -1 for handle 0 (miss sentinel)",
                  FindUnitByHandle(trio, 3, 0ull), -1);
        ExpectInt("FindUnitByHandle returns -1 for a null list",
                  FindUnitByHandle(nullptr, 3, 0xAAull), -1);
        ExpectInt("FindUnitByHandle returns -1 for count 0",
                  FindUnitByHandle(trio, 0, 0xAAull), -1);

        // Budget clamp: a hit within kMaxRaycastUnits is seen; one past it is
        // not — the walk stops at the snapshot's per-unit cap.
        const int big = 80;
        UnitInfo many[80] = {};
        for (int i = 0; i < big; ++i)
        {
            many[i] = MakeUnit(0x1000ull + static_cast<std::uint64_t>(i),
                               100, 100, 0, 0, 0, Vec3{}, "U");
        }
        ExpectInt("FindUnitByHandle sees a hit at index 63 (within budget)",
                  FindUnitByHandle(many, big, 0x1000ull + 63), 63);
        ExpectInt("FindUnitByHandle does not see a hit past kMaxRaycastUnits",
                  FindUnitByHandle(many, big, 0x1000ull + 70), -1);
        ExpectInt("kMaxRaycastUnits is 64 (the snapshot per-unit cap)",
                  kMaxRaycastUnits, 64);
    }
    std::printf("\n");

    // ---- 7. OpenInspectorFor ----------------------------------------------
    std::printf("[open inspector for]\n");
    {
        const UnitInfo units[3] = {
            MakeUnit(0xA1ull, 3600, 5000, 1200, 2000, 0,
                     Vec3{ 10.0f, 20.0f, 0.0f }, "Rebel_Trooper"),
            MakeUnit(0xB2ull, 8000, 8000, 0, 0, 1,
                     Vec3{ -5.0f, 7.0f, 0.0f }, "Empire_Star_Destroyer"),
            MakeUnit(0xC3ull, 500, 4000, 800, 800, 2,
                     Vec3{ 0.0f, 0.0f, 0.0f }, "Underworld_Frigate"),
        };

        const InspectorPanel p =
            OpenInspectorFor(MakeHit(true, 1, 0xB2ull, 12.0f), units, 3);
        // RED-GREEN: CLICK OPENS INSPECTOR — a hit must open the panel.
        ExpectTrue("OpenInspectorFor opens the panel on a valid pick",
                   p.visible);
        ExpectU64("OpenInspectorFor carries the picked handle", p.unit.handle,
                  0xB2ull);
        ExpectStr("OpenInspectorFor carries the picked type", p.unit.type,
                  "Empire_Star_Destroyer");
        ExpectInt("OpenInspectorFor carries the picked owner", p.unit.owner, 1);
        ExpectNear("OpenInspectorFor carries the picked hull", p.unit.hull,
                   8000.0f);
        ExpectNear("OpenInspectorFor carries the picked position X",
                   p.unit.position.x, -5.0f);

        // A miss pick never opens the panel.
        const InspectorPanel miss =
            OpenInspectorFor(MakeHit(false, -1, 0ull, 0.0f), units, 3);
        ExpectTrue("OpenInspectorFor stays closed on a miss pick",
                   !miss.visible);

        // A pick whose handle is no longer in the list — a stale pick — does
        // not open a phantom panel.
        const InspectorPanel stale =
            OpenInspectorFor(MakeHit(true, 0, 0xDEADull, 5.0f), units, 3);
        ExpectTrue("OpenInspectorFor stays closed on a stale handle",
                   !stale.visible);

        // A null info list keeps the panel closed.
        const InspectorPanel nolist =
            OpenInspectorFor(MakeHit(true, 0, 0xA1ull, 5.0f), nullptr, 3);
        ExpectTrue("OpenInspectorFor stays closed on a null list",
                   !nolist.visible);

        // RED-GREEN: RESOLVE BY HANDLE — units listed in a different order
        // than the UnitHit's index. The hit says index 0 but handle 0xC3;
        // resolving by handle must inspect 0xC3 (at array index 2), not the
        // unit sitting at index 0.
        const UnitInfo reordered[3] = { units[0], units[1], units[2] };
        const InspectorPanel byHandle =
            OpenInspectorFor(MakeHit(true, /*index=*/0, /*handle=*/0xC3ull,
                                     9.0f),
                             reordered, 3);
        ExpectTrue("OpenInspectorFor opens despite a mismatched index",
                   byHandle.visible);
        ExpectU64("OpenInspectorFor resolves by handle, not by index",
                  byHandle.unit.handle, 0xC3ull);
        ExpectStr("OpenInspectorFor handle-resolved type is correct",
                  byHandle.unit.type, "Underworld_Frigate");
    }
    std::printf("\n");

    // ---- 8. UpdateInspectorPanel ------------------------------------------
    std::printf("[update inspector panel]\n");
    {
        const UnitInfo units[2] = {
            MakeUnit(0xB2ull, 8000, 8000, 0, 0, 1, Vec3{}, "Empire_SD"),
            MakeUnit(0xC3ull, 500, 4000, 0, 0, 2, Vec3{}, "Underworld_Frigate"),
        };
        const InspectorPanel closed{};   // default — not visible

        // From a closed panel, a valid pick opens it.
        const InspectorPanel opened =
            UpdateInspectorPanel(closed, MakeHit(true, 0, 0xB2ull, 1.0f),
                                 units, 2);
        ExpectTrue("UpdateInspectorPanel opens from a closed panel",
                   opened.visible);
        ExpectU64("UpdateInspectorPanel opened on the picked unit",
                  opened.unit.handle, 0xB2ull);

        // A second valid pick replaces the inspected unit.
        const InspectorPanel replaced =
            UpdateInspectorPanel(opened, MakeHit(true, 1, 0xC3ull, 1.0f),
                                 units, 2);
        ExpectU64("UpdateInspectorPanel replaces on a new pick",
                  replaced.unit.handle, 0xC3ull);

        // RED-GREEN: MISS LEAVES PANEL — a miss-click returns the prior panel
        // untouched; a "close on every click" old form would hide it.
        const InspectorPanel afterMiss =
            UpdateInspectorPanel(opened, MakeHit(false, -1, 0ull, 0.0f),
                                 units, 2);
        ExpectTrue("UpdateInspectorPanel keeps the panel open on a miss",
                   afterMiss.visible);
        ExpectU64("UpdateInspectorPanel keeps the same unit on a miss",
                  afterMiss.unit.handle, 0xB2ull);

        // A pick that hits but no longer resolves also leaves the panel as-is.
        const InspectorPanel afterStale =
            UpdateInspectorPanel(opened, MakeHit(true, 0, 0xDEADull, 1.0f),
                                 units, 2);
        ExpectTrue("UpdateInspectorPanel keeps the panel on a stale pick",
                   afterStale.visible);
        ExpectU64("UpdateInspectorPanel keeps the same unit on a stale pick",
                  afterStale.unit.handle, 0xB2ull);

        // A miss from a closed panel stays closed.
        const InspectorPanel stillClosed =
            UpdateInspectorPanel(closed, MakeHit(false, -1, 0ull, 0.0f),
                                 units, 2);
        ExpectTrue("UpdateInspectorPanel stays closed on a miss from closed",
                   !stillClosed.visible);
    }
    std::printf("\n");

    // ---- 9. RefreshInspectorPanel -----------------------------------------
    std::printf("[refresh inspector panel]\n");
    {
        // Panel open on unit 0xB2 from an earlier snapshot.
        InspectorPanel panel{};
        panel.visible = true;
        panel.unit = MakeUnit(0xB2ull, 8000, 8000, 2000, 2000, 1,
                              Vec3{ 0.0f, 0.0f, 0.0f }, "Empire_SD");

        // Fresh snapshot: 0xB2 took damage and moved; 0xB2 is now at a
        // different array index than before to prove handle re-resolution.
        const UnitInfo damaged[3] = {
            MakeUnit(0xA1ull, 100, 100, 0, 0, 0, Vec3{}, "Rebel_Trooper"),
            MakeUnit(0xC3ull, 500, 4000, 0, 0, 2, Vec3{}, "Underworld_Frigate"),
            MakeUnit(0xB2ull, 3200, 8000, 0, 2000, 1,
                     Vec3{ 50.0f, -10.0f, 0.0f }, "Empire_SD"),
        };
        const InspectorPanel refreshed =
            RefreshInspectorPanel(panel, damaged, 3);
        ExpectTrue("RefreshInspectorPanel keeps a still-present unit open",
                   refreshed.visible);
        ExpectNear("RefreshInspectorPanel pulls fresh hull",
                   refreshed.unit.hull, 3200.0f);
        ExpectNear("RefreshInspectorPanel pulls fresh shield",
                   refreshed.unit.shield, 0.0f);
        ExpectNear("RefreshInspectorPanel pulls fresh position X",
                   refreshed.unit.position.x, 50.0f);
        ExpectU64("RefreshInspectorPanel re-resolves by handle across reorder",
                  refreshed.unit.handle, 0xB2ull);

        // RED-GREEN: DEAD UNIT AUTO-CLOSES — a snapshot without 0xB2 closes
        // the panel rather than freeze a phantom dead unit.
        const UnitInfo dead[2] = {
            MakeUnit(0xA1ull, 100, 100, 0, 0, 0, Vec3{}, "Rebel_Trooper"),
            MakeUnit(0xC3ull, 500, 4000, 0, 0, 2, Vec3{}, "Underworld_Frigate"),
        };
        const InspectorPanel closedByDeath =
            RefreshInspectorPanel(panel, dead, 2);
        ExpectTrue("RefreshInspectorPanel closes when the unit is gone",
                   !closedByDeath.visible);

        // A closed panel passes through untouched.
        const InspectorPanel wasClosed{};
        const InspectorPanel stillClosed =
            RefreshInspectorPanel(wasClosed, damaged, 3);
        ExpectTrue("RefreshInspectorPanel leaves a closed panel closed",
                   !stillClosed.visible);
    }
    std::printf("\n");

    // ---- 10. Integration: the iter-300 click pipeline ----------------------
    // Run the exact sequence the inspector glue runs on a click: raycast the
    // visible-unit AABB set (iter-299), then resolve the hit into a panel.
    std::printf("[integration: click -> raycast -> inspector]\n");
    {
        using swfoc_overlay::AabbFromCenterExtents;
        using swfoc_overlay::NearestUnitHit;

        // Three units on the z=0 ground, 5-unit half-extent cubes.
        const UnitAabb boxes[3] = {
            { 0xA0ull, AabbFromCenterExtents(Vec3{ -30.0f, 0.0f, 0.0f },
                                             5.0f, 5.0f, 5.0f) },
            { 0xB0ull, AabbFromCenterExtents(Vec3{ 0.0f, 0.0f, 0.0f },
                                             5.0f, 5.0f, 5.0f) },
            { 0xC0ull, AabbFromCenterExtents(Vec3{ 30.0f, 0.0f, 0.0f },
                                             5.0f, 5.0f, 5.0f) },
        };
        // Parallel UnitInfo set, same handles in the same order.
        const UnitInfo infos[3] = {
            MakeUnit(0xA0ull, 100, 100, 50, 50, 0,
                     Vec3{ -30.0f, 0.0f, 0.0f }, "Rebel_Trooper"),
            MakeUnit(0xB0ull, 6400, 8000, 1500, 2000, 1,
                     Vec3{ 0.0f, 0.0f, 0.0f }, "Empire_Star_Destroyer"),
            MakeUnit(0xC0ull, 4000, 4000, 0, 0, 2,
                     Vec3{ 30.0f, 0.0f, 0.0f }, "Underworld_Frigate"),
        };

        // A click straight down onto the centre unit (B0).
        const WorldRay onB =
            MakeRay(Vec3{ 0.0f, 0.0f, 50.0f }, Vec3{ 0.0f, 0.0f, -1.0f });
        const UnitHit hitB = NearestUnitHit(onB, boxes, 3);
        ExpectTrue("integration: the raycast hits the centre unit", hitB.hit);
        ExpectU64("integration: the raycast names unit B0", hitB.handle,
                  0xB0ull);

        const InspectorPanel panelB = OpenInspectorFor(hitB, infos, 3);
        ExpectTrue("integration: the click opens the inspector", panelB.visible);
        ExpectU64("integration: the inspector shows the clicked unit",
                  panelB.unit.handle, 0xB0ull);
        ExpectStr("integration: the inspector shows the unit type",
                  panelB.unit.type, "Empire_Star_Destroyer");
        char hbuf[64];
        FormatHealthLabel(hbuf, sizeof(hbuf), panelB.unit.hull,
                          panelB.unit.hullMax);
        ExpectStr("integration: the inspector hull row reads correctly", hbuf,
                  "6400 / 8000 (80%)");
        ExpectStr("integration: the inspector owner row reads correctly",
                  FactionName(panelB.unit.owner), "Empire");

        // A click on empty ground hits nothing and leaves the panel open.
        const WorldRay onEmpty =
            MakeRay(Vec3{ 100.0f, 100.0f, 50.0f }, Vec3{ 0.0f, 0.0f, -1.0f });
        const UnitHit miss = NearestUnitHit(onEmpty, boxes, 3);
        ExpectTrue("integration: a click on empty ground hits nothing",
                   !miss.hit);
        const InspectorPanel afterEmpty =
            UpdateInspectorPanel(panelB, miss, infos, 3);
        ExpectTrue("integration: the empty click leaves the inspector open",
                   afterEmpty.visible);
        ExpectU64("integration: the empty click keeps the same unit",
                  afterEmpty.unit.handle, 0xB0ull);

        // B0 takes damage — a fresh snapshot keeps the panel and updates it.
        const UnitInfo afterDamage[3] = {
            infos[0],
            MakeUnit(0xB0ull, 2400, 8000, 0, 2000, 1,
                     Vec3{ 12.0f, 3.0f, 0.0f }, "Empire_Star_Destroyer"),
            infos[2],
        };
        const InspectorPanel live =
            RefreshInspectorPanel(panelB, afterDamage, 3);
        ExpectTrue("integration: the inspector tracks the damaged unit",
                   live.visible);
        ExpectNear("integration: the inspector hull falls with the unit",
                   live.unit.hull, 2400.0f);

        // B0 is destroyed — the next snapshot drops it; the panel auto-closes.
        const UnitInfo afterDeath[2] = { infos[0], infos[2] };
        const InspectorPanel gone =
            RefreshInspectorPanel(panelB, afterDeath, 2);
        ExpectTrue("integration: the inspector closes when the unit dies",
                   !gone.visible);
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
