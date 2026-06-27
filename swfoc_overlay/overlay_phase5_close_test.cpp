// =============================================================================
// swfoc_overlay/overlay_phase5_close_test.cpp — Phase 5 close-out integration
// test (Phase 5 close-out part 1/2, iter 542 / spec iter-303).
//
// Phase 5 (iter 297-303) made the overlay CLICK-AWARE: the operator clicks an
// in-game unit and an inspector panel opens with the unit's stats and five
// action buttons. It shipped one kernel per iteration, each with its own
// dedicated unit test:
//
//   iter-297       (RE pass)               projection-matrix RVA pins   (doc)
//   iter-298       overlay_cursor_ray.h    cursor pixel -> world ray    147/0
//   iter-299       overlay_hit_test.h      ray -> nearest unit AABB     101/0
//   iter-300       overlay_inspector.h     UnitHit -> inspector panel    84/0
//   iter-301       overlay_inspector_actions.h  panel -> 5 ActionRequests 68/0
//   iter-302       overlay_unit_aabb.h     HudSnapshot unit-AABB set     65/0
//
// Those five tests pin each kernel IN ISOLATION (465 checks total). This file
// is the Phase 5 CLOSE-OUT test: it wires the five kernels — plus the bridge
// command builders overlay_actions.h and the iter-513 ActionQueue — TOGETHER
// and exercises the complete end-to-end CLICK-TO-INSPECT pipeline exactly as
// overlay.cpp's deferred RenderInspector / OnClick chain runs it at runtime.
// Its value is the SEAMS between kernels, which no isolation test can see.
//
// Naming note (carried from iter-527's Phase 3 and iter-533's Phase 4
// close-outs): the spec iter-303 row writes "Iter303Phase5InspectorTests.cs".
// The `.cs` name predates the overlay's all-C++ native-exe test infra — a C#
// test cannot exercise a C++ header. This file IS the spec iter-303 close-out
// test in the established pattern (overlay_phase4_close_test.cpp, ...).
//
// SPEC iter-303 RED-GREEN PINS (overlay-interactive.md acceptance line 33)
// -----------------------------------------------------------------------
//   [1] CLICK PICKS THE UNIT     : a cursor click whose screen pixel is the
//       projection of a unit's world centre picks THAT unit through the full
//       pipeline (CursorRay -> PickUnitInSet -> UnitHit). The honest-defer
//       empty AABB set produces a clean miss, never a phantom hit. An "always
//       miss" or "pick array slot 0" old form fails.
//   [2] INSPECTOR SHOWS THE UNIT : a hit opens an inspector panel carrying the
//       clicked unit's hull / shield / owner / type / position, formatted by
//       the iter-300 label helpers. A "never open" old form fails.
//   [3] ACTION BUTTONS DISPATCH-READY : the five inspector actions (Kill /
//       Heal / Teleport / SwapOwner / MakeInvuln) each build a real
//       `return SWFOC_*` bridge line; Kill targets the EXACT picked handle
//       even when two units share a type. A "kill first-of-type" old form
//       drops the address and fails.
//   [4] MISS-CLICK KEEPS THE PANEL : clicking empty ground leaves the open
//       inspector exactly as it was. A "close on every click" old form
//       flickers the panel shut and fails.
//
// All seven headers are header-only and std-only. Build + run via
// build_phase5_close_test.bat — no game, no pipe, no ImGui, no bridge.
// =============================================================================

#include "overlay_actions.h"
#include "overlay_action_queue.h"
#include "overlay_cursor_ray.h"
#include "overlay_hit_test.h"
#include "overlay_inspector.h"
#include "overlay_inspector_actions.h"
#include "overlay_unit_aabb.h"

#include <cmath>
#include <cstdint>
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

    // Compare two floats within a tolerance. The world coordinates picked back
    // out of a projection round-trip carry a few hundredths of a unit of float
    // error, so a 0.5-unit eps catches real drift without flagging that.
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

    // Compare two integer-valued quantities. `long long` so engine GameObject
    // handles (uint64), array counts, and faction slots all feed it without a
    // sign warning under -Wextra.
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

    using swfoc_overlay::Mat4;
    using swfoc_overlay::Vec3;
    using swfoc_overlay::Vec4;

    // ---- Test-only fixtures: build the matrices the engine builds ----------
    // These mirror D3DX's matrix constructors so the close-out test exercises
    // the kernel chain against the exact transform conventions the engine's
    // render path uses — the same fixtures overlay_cursor_ray_test.cpp pins
    // the unprojection round-trip with (iter-298).

    // Row-major D3DXMatrixPerspectiveFovRH equivalent (D3D depth range [0,1]).
    Mat4 MakePerspectiveFovRH(float fovY, float aspect, float zn, float zf)
    {
        const float yScale = 1.0f / std::tan(fovY * 0.5f);
        const float xScale = yScale / aspect;
        Mat4 r{};
        r.m[0]  = xScale;
        r.m[5]  = yScale;
        r.m[10] = zf / (zn - zf);
        r.m[11] = -1.0f;
        r.m[14] = zn * zf / (zn - zf);
        r.m[15] = 0.0f;
        return r;
    }

    // Row-major D3DXMatrixLookAtRH equivalent.
    Mat4 MakeLookAtRH(const Vec3& eye, const Vec3& at, const Vec3& up)
    {
        const Vec3 zaxis =
            swfoc_overlay::Vec3Normalize(swfoc_overlay::Vec3Sub(eye, at));
        const Vec3 xaxis =
            swfoc_overlay::Vec3Normalize(swfoc_overlay::Vec3Cross(up, zaxis));
        const Vec3 yaxis = swfoc_overlay::Vec3Cross(zaxis, xaxis);
        Mat4 r{};
        r.m[0]  = xaxis.x; r.m[1]  = yaxis.x; r.m[2]  = zaxis.x; r.m[3]  = 0.0f;
        r.m[4]  = xaxis.y; r.m[5]  = yaxis.y; r.m[6]  = zaxis.y; r.m[7]  = 0.0f;
        r.m[8]  = xaxis.z; r.m[9]  = yaxis.z; r.m[10] = zaxis.z; r.m[11] = 0.0f;
        r.m[12] = -swfoc_overlay::Vec3Dot(xaxis, eye);
        r.m[13] = -swfoc_overlay::Vec3Dot(yaxis, eye);
        r.m[14] = -swfoc_overlay::Vec3Dot(zaxis, eye);
        r.m[15] = 1.0f;
        return r;
    }

    // The screen pixel a world point projects to, plus whether it is in front
    // of the camera. Mirrors the engine's world->clip->screen path — the exact
    // inverse of what CursorRay does. The close-out test uses it to turn a
    // chosen "the operator clicked this unit" into the cursor pixel the OS
    // would have handed overlay.cpp.
    struct Projected
    {
        float sx;
        float sy;
        bool  inFront;
    };

    Projected ProjectWorld(const Vec3& w, const Mat4& viewProj,
                           float vw, float vh)
    {
        const Vec4 clip = swfoc_overlay::Mat4TransformRow(
            Vec4{ w.x, w.y, w.z, 1.0f }, viewProj);
        Projected p{};
        p.inFront = clip.w > 0.0f;
        if (clip.w == 0.0f)
        {
            p.sx = 0.0f;
            p.sy = 0.0f;
            return p;
        }
        const float ndcx = clip.x / clip.w;
        const float ndcy = clip.y / clip.w;
        p.sx = (ndcx + 1.0f) * 0.5f * vw;
        p.sy = (1.0f - ndcy) * 0.5f * vh;  // screen Y grows down
        return p;
    }

    // -------------------------------------------------------------------------
    // TestUnit / BuildVisibleUnits — a visible-unit fixture. The HUD worker
    // (deferred; honest-defer #2) would fill a UnitAabbSet and a parallel
    // UnitInfo[] from the engine's enumeration; the test builds them directly,
    // modelling the post-wire-landing state. The two arrays are keyed by the
    // engine GameObject handle, NOT by array index — OpenInspectorFor resolves
    // by handle, so the order of the two arrays need not agree.
    // -------------------------------------------------------------------------
    struct TestUnit
    {
        std::uint64_t handle;
        const char*   type;
        Vec3          pos;        // world-space ground position (z = 0)
        float         hull;
        float         hullMax;
        float         shield;
        float         shieldMax;
        int           owner;      // faction slot
        float         halfExtent; // per-axis AABB half-extent
    };

    void BuildVisibleUnits(const TestUnit* units, int n,
                           swfoc_overlay::UnitAabbSet& set,
                           swfoc_overlay::UnitInfo* infos)
    {
        using namespace swfoc_overlay;
        ClearUnitAabbSet(set);
        for (int i = 0; i < n; ++i)
        {
            AppendUnitAabb(set, units[i].handle, units[i].pos,
                           units[i].halfExtent, units[i].halfExtent,
                           units[i].halfExtent);
            UnitInfo& info  = infos[i];
            info            = UnitInfo{};
            info.handle     = units[i].handle;
            info.hull       = units[i].hull;
            info.hullMax    = units[i].hullMax;
            info.shield     = units[i].shield;
            info.shieldMax  = units[i].shieldMax;
            info.owner      = units[i].owner;
            info.position   = units[i].pos;
            SetUnitType(info, units[i].type);
        }
    }

    // -------------------------------------------------------------------------
    // Phase5ClickResult / SimulatePhase5Click — a faithful in-memory model of
    // the overlay.cpp Phase 5 click-to-inspect flow, so the close-out test can
    // drive the five kernels TOGETHER through the exact decision sequence the
    // (deferred) ImGui glue runs on a left-click. The glue itself (the OS
    // cursor read, ImGui::Begin("Inspector"), the button widgets) is build-only;
    // this models its DECISION sequence, which is the part with a right answer.
    //
    // The chain reproduced here, in order:
    //   iter-298  CursorRay        : OS cursor pixel + viewport + view*proj
    //                                -> a world-space pick ray.
    //   iter-302  PickUnitInSet    : forward the HudSnapshot UnitAabbSet's
    //   -> 299    -> NearestUnitHit  populated range into the client-side
    //                                raycast -> the unit under the cursor.
    //   iter-298  RayPlaneZ0       : where the same ray meets the z=0 ground —
    //                                the teleport-action destination.
    //   iter-300  UpdateInspectorPanel : a hit opens / replaces the inspector
    //                                on the picked unit; a miss leaves the
    //                                prior panel exactly as it was.
    //   iter-301  BuildInspector*  : turn the inspected UnitInfo into five
    //                                dispatch-ready ActionRequests.
    // -------------------------------------------------------------------------
    struct Phase5ClickResult
    {
        bool                          ray_valid     = false;
        bool                          unit_hit      = false;
        std::uint64_t                 hit_handle    = 0;
        swfoc_overlay::PlaneHit       ground{};      // teleport destination
        swfoc_overlay::InspectorPanel panel{};       // UpdateInspectorPanel out
        bool                          actions_built = false;
        swfoc_overlay::ActionRequest  kill;
        swfoc_overlay::ActionRequest  heal;
        swfoc_overlay::ActionRequest  teleport;
        swfoc_overlay::ActionRequest  swap_owner;
        swfoc_overlay::ActionRequest  make_invuln;
    };

    Phase5ClickResult SimulatePhase5Click(
        float cursor_sx, float cursor_sy, float vw, float vh,
        const swfoc_overlay::Mat4& viewProj,
        const swfoc_overlay::UnitAabbSet& aabbs,
        const swfoc_overlay::UnitInfo* infos, int count,
        const swfoc_overlay::InspectorPanel& prevPanel)
    {
        using namespace swfoc_overlay;
        Phase5ClickResult r;

        // iter-298: the OS cursor pixel becomes a world-space pick ray.
        const WorldRay ray = CursorRay(cursor_sx, cursor_sy, vw, vh, viewProj);
        r.ray_valid = ray.valid;

        // iter-302 -> iter-299: walk the HudSnapshot's visible-unit AABB set.
        const UnitHit hit = PickUnitInSet(ray, aabbs);
        r.unit_hit   = hit.hit;
        r.hit_handle = hit.handle;

        // iter-298: where the pick ray crosses the z=0 ground — the world
        // point the Teleport action sends the inspected unit to.
        r.ground = RayPlaneZ0(ray);

        // iter-300: a hit opens / replaces the inspector on the picked unit;
        // a miss returns the prior panel unchanged.
        r.panel = UpdateInspectorPanel(prevPanel, hit, infos, count);

        // iter-301: build the five action buttons from the inspected unit.
        if (r.panel.visible)
        {
            r.kill        = BuildInspectorKill(r.panel.unit);
            r.heal        = BuildInspectorHeal(r.panel.unit);
            r.teleport    = BuildInspectorTeleport(r.panel.unit,
                                                   r.ground.x, r.ground.y,
                                                   0.0f);
            r.swap_owner  = BuildInspectorSwapOwner(
                r.panel.unit, NextFactionSlot(r.panel.unit.owner));
            r.make_invuln = BuildInspectorMakeInvuln(r.panel.unit, true);
            r.actions_built = true;
        }
        return r;
    }
}

int main()
{
    using namespace swfoc_overlay;
    std::printf("=== Phase 5 close-out integration test (spec iter-303) ===\n\n");

    // ---- Shared fixtures: a tactical camera + four visible ground units -----
    //
    // A right-handed, Z-up world (the z=0 ground plane RayPlaneZ0 resolves
    // onto). The camera sits above and to the side, looking down at the
    // origin — a typical SWFOC tactical view. The four units sit on the
    // ground; A and D deliberately share a unit type so the exact-handle
    // targeting pins have something to bite on.
    const float kVw = 1280.0f;
    const float kVh = 720.0f;
    const Mat4 view = MakeLookAtRH(Vec3{ 40.0f, -120.0f, 90.0f },
                                   Vec3{ 0.0f, 0.0f, 0.0f },
                                   Vec3{ 0.0f, 0.0f, 1.0f });
    const Mat4 proj = MakePerspectiveFovRH(0.9f, kVw / kVh, 1.0f, 600.0f);
    const Mat4 viewProj = Mat4Multiply(view, proj);

    // handle, type, pos, hull, hullMax, shield, shieldMax, owner, halfExtent
    const TestUnit kUnits[4] = {
        { 0x1000ULL, "Rebel_Trooper_Squad",
          Vec3{ 0.0f, 0.0f, 0.0f },   1200.0f, 1200.0f,    0.0f,    0.0f, 0,
          18.0f },
        { 0x2000ULL, "Empire_Stormtrooper_Squad",
          Vec3{ 60.0f, 0.0f, 0.0f },  6400.0f, 8000.0f, 1500.0f, 3000.0f, 1,
          18.0f },
        { 0x3000ULL, "Underworld_Vengeance_Frigate",
          Vec3{ -60.0f, 0.0f, 0.0f }, 9000.0f, 9000.0f, 4000.0f, 4000.0f, 2,
          18.0f },
        { 0x4000ULL, "Rebel_Trooper_Squad",
          Vec3{ 0.0f, 60.0f, 0.0f },   800.0f, 1200.0f,    0.0f,    0.0f, 0,
          18.0f },
    };

    UnitAabbSet visible;
    UnitInfo    infos[4];
    BuildVisibleUnits(kUnits, 4, visible, infos);

    // =========================================================================
    // [1] SPEC PIN: click picks the unit.
    //
    // A cursor click whose screen pixel is the projection of a unit's world
    // centre must pick THAT unit through the whole CursorRay -> PickUnitInSet
    // pipeline — and the honest-defer empty set must produce a clean miss.
    // =========================================================================
    std::printf("[1] Pin: click picks the unit\n");
    {
        // Every unit, in turn: project its centre to a cursor pixel, click
        // there, confirm the pipeline picks that unit's exact handle.
        char nm[192];
        bool all_hit = true;
        for (int i = 0; i < 4; ++i)
        {
            const Projected p =
                ProjectWorld(kUnits[i].pos, viewProj, kVw, kVh);
            std::snprintf(nm, sizeof(nm),
                          "unit %d (%s): centre projects in front of camera",
                          i, kUnits[i].type);
            ExpectTrue(nm, p.inFront);

            const Phase5ClickResult r =
                SimulatePhase5Click(p.sx, p.sy, kVw, kVh, viewProj,
                                    visible, infos, 4, InspectorPanel{});
            std::snprintf(nm, sizeof(nm),
                          "unit %d: click yields a valid pick ray", i);
            ExpectTrue(nm, r.ray_valid);
            std::snprintf(nm, sizeof(nm), "unit %d: click registers a hit", i);
            ExpectTrue(nm, r.unit_hit);
            std::snprintf(nm, sizeof(nm),
                          "unit %d: pick resolves the exact handle", i);
            ExpectIntEq(nm, static_cast<long long>(r.hit_handle),
                        static_cast<long long>(kUnits[i].handle));
            if (!r.unit_hit ||
                r.hit_handle != kUnits[i].handle)
            {
                all_hit = false;
            }
        }
        ExpectTrue("all four units pick cleanly through the pipeline", all_hit);
    }
    {
        // RED-GREEN: A and D share the type "Rebel_Trooper_Squad". A click on
        // A picks A's handle by GEOMETRY — a "first object of this type" old
        // form could not tell A from D.
        const Projected pa = ProjectWorld(kUnits[0].pos, viewProj, kVw, kVh);
        const Phase5ClickResult ra =
            SimulatePhase5Click(pa.sx, pa.sy, kVw, kVh, viewProj,
                                visible, infos, 4, InspectorPanel{});
        ExpectIntEq("click on A picks A's handle (0x1000)",
                    static_cast<long long>(ra.hit_handle), 0x1000);
        ExpectTrue("click on A does NOT pick D (same type, other handle)",
                   ra.hit_handle != 0x4000);
    }
    {
        // Honest-defer (overlay-interactive.md #2): until a per-unit-AABB
        // bridge wire lands the HudSnapshot carries an EMPTY UnitAabbSet. A
        // click against it must miss cleanly — never a phantom inspector.
        UnitAabbSet empty;
        const Projected p = ProjectWorld(kUnits[0].pos, viewProj, kVw, kVh);
        const Phase5ClickResult r =
            SimulatePhase5Click(p.sx, p.sy, kVw, kVh, viewProj,
                                empty, infos, 4, InspectorPanel{});
        ExpectIntEq("honest-defer: empty AABB set has count 0",
                    static_cast<long long>(empty.count), 0);
        ExpectTrue("honest-defer: the ray is still valid", r.ray_valid);
        ExpectFalse("honest-defer: empty set yields no hit", r.unit_hit);
        ExpectFalse("honest-defer: empty set opens no inspector",
                    r.panel.visible);
    }
    {
        // Nearest-wins seam: two units stacked along ONE screen ray (both on
        // the camera->origin line, so both project to the origin pixel). The
        // pick must return the unit the ray ENTERS first — the one nearer the
        // camera — exactly as NearestUnitHit decides.
        UnitAabbSet stacked;
        AppendUnitAabb(stacked, 0x1000ULL, Vec3{ 0.0f, 0.0f, 0.0f },
                       18.0f, 18.0f, 18.0f);                 // far  (at origin)
        AppendUnitAabb(stacked, 0x5000ULL, Vec3{ 16.0f, -48.0f, 36.0f },
                       18.0f, 18.0f, 18.0f);                 // near (toward eye)
        const Projected p = ProjectWorld(Vec3{ 0.0f, 0.0f, 0.0f },
                                         viewProj, kVw, kVh);
        const Phase5ClickResult r =
            SimulatePhase5Click(p.sx, p.sy, kVw, kVh, viewProj,
                                stacked, infos, 4, InspectorPanel{});
        ExpectTrue("nearest-wins: stacked units register a hit", r.unit_hit);
        ExpectIntEq("nearest-wins: the unit nearer the camera is picked",
                    static_cast<long long>(r.hit_handle), 0x5000);
    }

    // =========================================================================
    // [2] SPEC PIN: inspector shows the unit.
    //
    // A hit opens an inspector panel carrying the clicked unit's hull / shield
    // / owner / type / position, formatted by the iter-300 label helpers.
    // =========================================================================
    std::printf("\n[2] Pin: inspector shows the unit\n");
    {
        // Click unit B (Empire_Stormtrooper_Squad, slot 1, 6400/8000 hull,
        // 1500/3000 shield, at world (60,0,0)).
        const Projected p = ProjectWorld(kUnits[1].pos, viewProj, kVw, kVh);
        const Phase5ClickResult r =
            SimulatePhase5Click(p.sx, p.sy, kVw, kVh, viewProj,
                                visible, infos, 4, InspectorPanel{});
        ExpectTrue("B: inspector panel is visible", r.panel.visible);
        ExpectIntEq("B: panel carries B's handle",
                    static_cast<long long>(r.panel.unit.handle), 0x2000);
        ExpectStrEq("B: panel shows the unit type",
                    r.panel.unit.type, "Empire_Stormtrooper_Squad");
        ExpectIntEq("B: panel shows owner slot 1",
                    r.panel.unit.owner, 1);
        ExpectStrEq("B: owner slot resolves to a faction name",
                    FactionName(r.panel.unit.owner), "Empire");
        ExpectNear("B: panel shows hull 6400", r.panel.unit.hull,
                   6400.0f, 0.01f);
        ExpectNear("B: panel shows hullMax 8000", r.panel.unit.hullMax,
                   8000.0f, 0.01f);

        char hull[64];
        FormatHealthLabel(hull, static_cast<int>(sizeof(hull)),
                          r.panel.unit.hull, r.panel.unit.hullMax);
        ExpectStrEq("B: hull row formats as current / max (pct%)",
                    hull, "6400 / 8000 (80%)");

        char shield[64];
        FormatHealthLabel(shield, static_cast<int>(sizeof(shield)),
                          r.panel.unit.shield, r.panel.unit.shieldMax);
        ExpectStrEq("B: shield row formats as current / max (pct%)",
                    shield, "1500 / 3000 (50%)");

        char posbuf[64];
        FormatPositionLabel(posbuf, static_cast<int>(sizeof(posbuf)),
                            r.panel.unit.position);
        ExpectStrEq("B: position row formats as (x, y, z)",
                    posbuf, "(60.0, 0.0, 0.0)");
    }
    {
        // Click unit A (Rebel_Trooper_Squad, slot 0, full 1200/1200 hull, NO
        // shield). The shield row must read "n/a" — a shieldless unit's
        // 0/0 max never overflows the bar.
        const Projected p = ProjectWorld(kUnits[0].pos, viewProj, kVw, kVh);
        const Phase5ClickResult r =
            SimulatePhase5Click(p.sx, p.sy, kVw, kVh, viewProj,
                                visible, infos, 4, InspectorPanel{});
        ExpectTrue("A: inspector panel is visible", r.panel.visible);
        ExpectStrEq("A: owner slot 0 resolves to Rebel",
                    FactionName(r.panel.unit.owner), "Rebel");
        ExpectIntEq("A: full hull reads 100 percent",
                    HealthPercent(r.panel.unit.hull, r.panel.unit.hullMax),
                    100);

        char hull[64];
        FormatHealthLabel(hull, static_cast<int>(sizeof(hull)),
                          r.panel.unit.hull, r.panel.unit.hullMax);
        ExpectStrEq("A: full hull row formats at 100%",
                    hull, "1200 / 1200 (100%)");

        char shield[64];
        FormatHealthLabel(shield, static_cast<int>(sizeof(shield)),
                          r.panel.unit.shield, r.panel.unit.shieldMax);
        ExpectStrEq("A: shieldless unit shows n/a, not a divide-by-zero",
                    shield, "n/a");
    }

    // =========================================================================
    // [3] SPEC PIN: action buttons dispatch-ready.
    //
    // The five inspector actions each build a real `return SWFOC_*` bridge
    // line, every label names the unit, and Kill targets the EXACT picked
    // handle even when two units share a type.
    // =========================================================================
    std::printf("\n[3] Pin: action buttons dispatch-ready\n");
    {
        // Click unit B and build all five actions from the panel.
        const Projected p = ProjectWorld(kUnits[1].pos, viewProj, kVw, kVh);
        const Phase5ClickResult r =
            SimulatePhase5Click(p.sx, p.sy, kVw, kVh, viewProj,
                                visible, infos, 4, InspectorPanel{});
        ExpectTrue("B: all five actions were built", r.actions_built);

        // Kill — address-based, targets the exact picked handle (0x2000 =
        // 8192). SWFOC_KillUnit takes a numeric obj_addr.
        ExpectStartsWith("B Kill: is a SWFOC_KillUnit call",
                         r.kill.lua, "return SWFOC_KillUnit(");
        ExpectContains("B Kill: embeds B's exact handle (8192)",
                       r.kill.lua, "8192");
        ExpectContains("B Kill: label names the unit",
                       r.kill.label, "Empire_Stormtrooper_Squad");

        // Heal — the no-arg :Heal() shape; resolves the unit by expression.
        ExpectStartsWith("B Heal: is a SWFOC_HealUnitLua call",
                         r.heal.lua, "return SWFOC_HealUnitLua(");
        ExpectContains("B Heal: resolves the unit by type expression",
                       r.heal.lua, "Empire_Stormtrooper_Squad");
        ExpectContains("B Heal: label names the unit",
                       r.heal.label, "Empire_Stormtrooper_Squad");

        // Teleport — two-arg shape, destination from the iter-298 ground pick.
        ExpectStartsWith("B Teleport: is a SWFOC_TeleportUnitLua call",
                         r.teleport.lua, "return SWFOC_TeleportUnitLua(");
        ExpectContains("B Teleport: carries a Create_Position destination",
                       r.teleport.lua, "Create_Position(");
        ExpectContains("B Teleport: label names the unit",
                       r.teleport.label, "Empire_Stormtrooper_Squad");

        // SwapOwner — re-owns to the NEXT slot. B is slot 1 (Empire) ->
        // NextFactionSlot(1) = 2 (Underworld). The new owner is the
        // REQUESTED slot, never the unit's current owner.
        ExpectStartsWith("B SwapOwner: is a SWFOC_ChangeUnitOwner call",
                         r.swap_owner.lua, "return SWFOC_ChangeUnitOwner(");
        ExpectContains("B SwapOwner: re-owns to the NEW faction (UNDERWORLD)",
                       r.swap_owner.lua, "UNDERWORLD");
        ExpectContains("B SwapOwner: label names the new owner",
                       r.swap_owner.label, "-> Underworld");

        // MakeInvuln — the model always builds the `true` (set) variant.
        ExpectStartsWith("B MakeInvuln: is a SWFOC_MakeUnitInvulnLua call",
                         r.make_invuln.lua, "return SWFOC_MakeUnitInvulnLua(");
        ExpectContains("B MakeInvuln(true): emits the true bool literal",
                       r.make_invuln.lua, "true");
        ExpectContains("B MakeInvuln(true): label reads Make Invuln",
                       r.make_invuln.label, "Make Invuln");

        // The toggle's other half — built directly off the same panel unit.
        const ActionRequest clear =
            BuildInspectorMakeInvuln(r.panel.unit, false);
        ExpectContains("B MakeInvuln(false): emits the false bool literal",
                       clear.lua, "false");
        ExpectContains("B MakeInvuln(false): label reads Clear Invuln",
                       clear.label, "Clear Invuln");
    }
    {
        // RED-GREEN exact-handle: click unit D — which shares A's type
        // "Rebel_Trooper_Squad". D's Kill must embed D's handle (0x4000 =
        // 16384), NOT A's (0x1000 = 4096). A "kill the first object of this
        // type" old form would drop the address and could kill A instead.
        const Projected p = ProjectWorld(kUnits[3].pos, viewProj, kVw, kVh);
        const Phase5ClickResult r =
            SimulatePhase5Click(p.sx, p.sy, kVw, kVh, viewProj,
                                visible, infos, 4, InspectorPanel{});
        ExpectIntEq("D: pick resolves D's handle, not A's",
                    static_cast<long long>(r.hit_handle), 0x4000);
        ExpectContains("D Kill: embeds D's exact handle (16384)",
                       r.kill.lua, "16384");
        ExpectFalse("D Kill: does NOT embed sibling A's handle (4096)",
                    r.kill.lua.find("4096") != std::string::npos);
    }

    // =========================================================================
    // [4] SPEC PIN: miss-click keeps the panel.
    //
    // Clicking empty ground — a pixel whose ray crosses no unit AABB — must
    // leave the open inspector exactly as it was. A "close on every click" old
    // form would flicker the panel shut.
    // =========================================================================
    std::printf("\n[4] Pin: miss-click keeps the panel\n");
    {
        // Open the inspector on unit A.
        const Projected pa = ProjectWorld(kUnits[0].pos, viewProj, kVw, kVh);
        const Phase5ClickResult opened =
            SimulatePhase5Click(pa.sx, pa.sy, kVw, kVh, viewProj,
                                visible, infos, 4, InspectorPanel{});
        ExpectTrue("setup: inspector is open on A", opened.panel.visible);

        // Now click empty ground — world (0,-65,0), well clear of every unit.
        const Projected pe = ProjectWorld(Vec3{ 0.0f, -65.0f, 0.0f },
                                          viewProj, kVw, kVh);
        const Phase5ClickResult miss =
            SimulatePhase5Click(pe.sx, pe.sy, kVw, kVh, viewProj,
                                visible, infos, 4, opened.panel);
        ExpectFalse("empty-ground click registers no unit hit",
                    miss.unit_hit);
        ExpectTrue("empty-ground click leaves the inspector OPEN",
                   miss.panel.visible);
        ExpectIntEq("empty-ground click keeps the inspector ON unit A",
                    static_cast<long long>(miss.panel.unit.handle), 0x1000);
    }
    {
        // A miss-click with NO prior panel leaves the inspector closed —
        // clicking empty ground never spuriously opens an empty inspector.
        const Projected pe = ProjectWorld(Vec3{ 0.0f, -65.0f, 0.0f },
                                          viewProj, kVw, kVh);
        const Phase5ClickResult miss =
            SimulatePhase5Click(pe.sx, pe.sy, kVw, kVh, viewProj,
                                visible, infos, 4, InspectorPanel{});
        ExpectFalse("empty-ground click with no prior panel stays closed",
                    miss.panel.visible);
    }

    // =========================================================================
    // [5] End-to-end operator session — the four pins chained as one timeline,
    // plus the cross-kernel invariants that hold the pipeline together.
    // =========================================================================
    std::printf("\n[5] End-to-end operator session\n");
    {
        // Cold start: overlay attached, the HUD worker has not yet populated
        // the per-unit AABB set (honest-defer). A click does nothing.
        UnitAabbSet cold;
        const Projected p0 = ProjectWorld(kUnits[0].pos, viewProj, kVw, kVh);
        const Phase5ClickResult cold_click =
            SimulatePhase5Click(p0.sx, p0.sy, kVw, kVh, viewProj,
                                cold, infos, 4, InspectorPanel{});
        ExpectFalse("session: cold-start click opens no inspector",
                    cold_click.panel.visible);

        // Battle joined: the worker fills the set. Click unit A — inspect it.
        const Phase5ClickResult clickA =
            SimulatePhase5Click(p0.sx, p0.sy, kVw, kVh, viewProj,
                                visible, infos, 4, InspectorPanel{});
        ExpectTrue("session: in-battle click on A opens the inspector",
                   clickA.panel.visible);
        ExpectIntEq("session: inspector is on A",
                    static_cast<long long>(clickA.panel.unit.handle), 0x1000);

        // Click unit B — the inspector REPLACES A with B (a valid pick always
        // re-targets the panel).
        const Projected pB = ProjectWorld(kUnits[1].pos, viewProj, kVw, kVh);
        const Phase5ClickResult clickB =
            SimulatePhase5Click(pB.sx, pB.sy, kVw, kVh, viewProj,
                                visible, infos, 4, clickA.panel);
        ExpectIntEq("session: inspector switched from A to B",
                    static_cast<long long>(clickB.panel.unit.handle), 0x2000);

        // Click empty ground — the inspector stays on B.
        const Projected pe = ProjectWorld(Vec3{ 0.0f, -65.0f, 0.0f },
                                          viewProj, kVw, kVh);
        const Phase5ClickResult clickGround =
            SimulatePhase5Click(pe.sx, pe.sy, kVw, kVh, viewProj,
                                visible, infos, 4, clickB.panel);
        ExpectTrue("session: empty-ground click keeps the inspector open",
                   clickGround.panel.visible);
        ExpectIntEq("session: inspector still on B after the empty click",
                    static_cast<long long>(clickGround.panel.unit.handle),
                    0x2000);

        // Operator presses B's "Kill" button: the iter-301 ActionRequest is
        // handed to the iter-513 ActionQueue, drained off-thread by a (faked)
        // bridge send. The whole Phase 5 write path: click -> inspector ->
        // BuildInspectorKill -> Enqueue -> Drain -> Live.
        ActionQueue queue;
        queue.Enqueue(clickGround.kill);
        ExpectIntEq("session: kill action queued (1 pending)",
                    static_cast<long long>(queue.PendingCount()), 1);
        const BridgeSendFn fakeSend =
            [](const std::string& lua, std::string& response) -> bool
            {
                response = "ok:" + lua;
                return true;
            };
        const int drained = queue.Drain(fakeSend);
        ExpectIntEq("session: the worker drained the 1 queued action",
                    drained, 1);
        const ActionResult res = queue.LatestResult();
        ExpectTrue("session: drained kill action reports Live",
                   res.status == ActionStatus::Live);
        ExpectContains("session: the dispatched action is B's kill",
                       res.label, "Empire_Stormtrooper_Squad");
    }
    {
        // Cross-kernel invariants — the seams that keep the five kernels
        // consistent with each other.

        // The raycast budget is ONE shared constant: overlay_hit_test.h's
        // NearestUnitHit clamp and overlay_unit_aabb.h's UnitAabbSet capacity
        // are the same kMaxRaycastUnits.
        ExpectIntEq("invariant: shared raycast budget kMaxRaycastUnits == 64",
                    kMaxRaycastUnits, 64);
        ExpectIntEq("invariant: the visible set holds all four units",
                    static_cast<long long>(visible.count), 4);

        // RESOLVE BY HANDLE across the close-out seam: the UnitAabbSet and the
        // UnitInfo[] need not share an order. Feed the inspector a REVERSED
        // info array and the pick still inspects the right unit, because
        // OpenInspectorFor keys on the engine handle, not the array index.
        UnitInfo reversed[4];
        for (int i = 0; i < 4; ++i)
        {
            reversed[i] = infos[3 - i];
        }
        const Projected pB = ProjectWorld(kUnits[1].pos, viewProj, kVw, kVh);
        const Phase5ClickResult r =
            SimulatePhase5Click(pB.sx, pB.sy, kVw, kVh, viewProj,
                                visible, reversed, 4, InspectorPanel{});
        ExpectIntEq("invariant: handle-keyed resolve survives a reordered "
                    "info array",
                    static_cast<long long>(r.panel.unit.handle), 0x2000);
        ExpectStrEq("invariant: the reordered resolve still names the unit",
                    r.panel.unit.type, "Empire_Stormtrooper_Squad");

        // The Teleport destination is the iter-298 ground pick of the SAME
        // ray that picked the unit — so teleporting B "to itself" lands on
        // B's own ground position.
        ExpectTrue("invariant: the pick ray meets the z=0 ground plane",
                   r.ground.hit);
        ExpectNear("invariant: teleport ground X tracks B's position",
                   r.ground.x, 60.0f, 0.5f);
        ExpectNear("invariant: teleport ground Y tracks B's position",
                   r.ground.y, 0.0f, 0.5f);
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
