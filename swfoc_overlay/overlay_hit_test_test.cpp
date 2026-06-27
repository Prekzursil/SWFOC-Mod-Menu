// =============================================================================
// swfoc_overlay/overlay_hit_test_test.cpp — unit test for overlay_hit_test.h
// (Phase 5 cont., iter 538 / spec iter-299).
//
// iter-299 is the cursor-hit-unit detection link of Phase 5: given the pick ray
// iter-298 builds and the visible-unit AABB set iter-302 appends to
// HudSnapshot, decide which unit the operator clicked. overlay_hit_test.h holds
// the pure math — an AABB validity screen, a slab ray-vs-box intersector, the
// nearest-hit walk, and the iter-300 PickUnitAtCursor convenience seam. This
// test pins all of it so the iter-300 inspector glue can depend on it
// build-only.
//
// The integration section runs the exact click pipeline iter-300 will run:
// build a real right-handed view-projection (the engine's own render path),
// place unit AABBs on the ground, project a unit's center to a screen pixel,
// and confirm PickUnitAtCursor recovers that unit — and that a click on empty
// ground recovers nothing.
//
// overlay_hit_test.h is header-only and std-only (<cmath>/<cstdint>/<limits>).
// Build + run via build_hit_test_test.bat — no game, no pipe, no ImGui.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - NEAREST HIT WINS         : with two boxes on the ray, the one entered
//                                first is returned even when listed second.
//   - MISS REPORTS NO HIT      : a ray beside every box -> hit=false,
//                                index=-1, handle=0.
//   - BOX BEHIND ORIGIN IGNORED: a box entirely behind the origin is no hit.
//   - PARALLEL-AXIS RAY HANDLED: a ray parallel to a slab axis hits within the
//                                other slabs, misses outside, never /0.
//   - INVALID AABB SKIPPED     : an inverted box (min > max) yields no hit.
//   - ORIGIN INSIDE BOX HITS   : a ray starting inside a box hits it at t=0.
//   - BUDGET CLAMP HOLDS       : NearestUnitHit walks at most kMaxRaycastUnits
//                                entries — a hit past the budget is unseen.
// =============================================================================

#include "overlay_hit_test.h"

#include <cmath>
#include <cstdio>

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

    using swfoc_overlay::Aabb;
    using swfoc_overlay::Mat4;
    using swfoc_overlay::UnitAabb;
    using swfoc_overlay::Vec3;
    using swfoc_overlay::Vec4;
    using swfoc_overlay::WorldRay;

    // Build a valid pick ray with a UNIT-length direction, so a returned
    // ray parameter `t` reads directly as a world-space distance.
    WorldRay MakeRay(const Vec3& origin, const Vec3& dir)
    {
        WorldRay r{};
        r.origin    = origin;
        r.direction = swfoc_overlay::Vec3Normalize(dir);
        r.valid     = true;
        return r;
    }

    // ---- Test-only fixtures: the matrices the engine builds ----------------
    // Mirror D3DX's row-major constructors so the integration section runs the
    // kernel against the exact transform conventions the engine's render path
    // uses (identical to overlay_cursor_ray_test.cpp).

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

    // The screen pixel a world point projects to — the exact inverse of what
    // CursorRay does. Used to aim a synthetic "cursor" at a known unit.
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
        p.sy = (1.0f - ndcy) * 0.5f * vh;
        return p;
    }
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_hit_test unit test ==\n");

    // ---- Section A: AabbIsValid -------------------------------------------
    ExpectTrue("aabb-valid: ordered box (min < max) is valid",
               AabbIsValid(Aabb{ Vec3{ -1, -2, -3 }, Vec3{ 4, 5, 6 } }));
    ExpectTrue("aabb-valid: zero-volume box (min == max) is valid",
               AabbIsValid(Aabb{ Vec3{ 7, 7, 7 }, Vec3{ 7, 7, 7 } }));
    ExpectTrue("aabb-valid: box with one flat axis is valid",
               AabbIsValid(Aabb{ Vec3{ 0, 0, 5 }, Vec3{ 10, 10, 5 } }));
    ExpectTrue("aabb-valid: X-inverted box is rejected",
               !AabbIsValid(Aabb{ Vec3{ 5, 0, 0 }, Vec3{ -5, 1, 1 } }));
    ExpectTrue("aabb-valid: Y-inverted box is rejected",
               !AabbIsValid(Aabb{ Vec3{ 0, 9, 0 }, Vec3{ 1, -9, 1 } }));
    ExpectTrue("aabb-valid: Z-inverted box is rejected",
               !AabbIsValid(Aabb{ Vec3{ 0, 0, 3 }, Vec3{ 1, 1, -3 } }));
    ExpectTrue("aabb-valid: fully inverted box is rejected",
               !AabbIsValid(Aabb{ Vec3{ 9, 9, 9 }, Vec3{ -9, -9, -9 } }));

    // ---- Section B: AabbFromCenterExtents ---------------------------------
    {
        const Aabb b = AabbFromCenterExtents(Vec3{ 10, 20, 30 }, 1, 2, 3);
        ExpectNear("aabb-make: min.x = center - hx", b.min.x, 9.0f);
        ExpectNear("aabb-make: min.y = center - hy", b.min.y, 18.0f);
        ExpectNear("aabb-make: min.z = center - hz", b.min.z, 27.0f);
        ExpectNear("aabb-make: max.x = center + hx", b.max.x, 11.0f);
        ExpectNear("aabb-make: max.y = center + hy", b.max.y, 22.0f);
        ExpectNear("aabb-make: max.z = center + hz", b.max.z, 33.0f);
        ExpectTrue("aabb-make: built box is valid", AabbIsValid(b));
    }
    {
        // Negative half-extents are taken by magnitude — the box never inverts.
        const Aabb b = AabbFromCenterExtents(Vec3{ 0, 0, 0 }, -4, -4, -4);
        ExpectTrue("aabb-make: negative extents still build a valid box",
                   AabbIsValid(b));
        ExpectNear("aabb-make: negative extent magnitude -> min -4", b.min.x,
                   -4.0f);
        ExpectNear("aabb-make: negative extent magnitude -> max +4", b.max.x,
                   4.0f);
    }

    // ---- Section C: RayAabbIntersect basic hits ---------------------------
    {
        const Aabb cube{ Vec3{ -1, -1, -1 }, Vec3{ 1, 1, 1 } };
        float t = -1.0f;
        ExpectTrue("aabb-hit: ray down the +Z column hits the unit cube",
                   RayAabbIntersect(MakeRay(Vec3{ 0, 0, 10 },
                                            Vec3{ 0, 0, -1 }), cube, t));
        ExpectNear("aabb-hit: enters at the near (z=1) face, t=9", t, 9.0f);
    }
    {
        const Aabb cube{ Vec3{ -1, -1, -1 }, Vec3{ 1, 1, 1 } };
        float t = -1.0f;
        ExpectTrue("aabb-hit: ray along -X hits the cube",
                   RayAabbIntersect(MakeRay(Vec3{ 5, 0, 0 },
                                            Vec3{ -1, 0, 0 }), cube, t));
        ExpectNear("aabb-hit: enters at the x=1 face, t=4", t, 4.0f);
    }
    {
        const Aabb cube{ Vec3{ -1, -1, -1 }, Vec3{ 1, 1, 1 } };
        float t = -1.0f;
        ExpectTrue("aabb-hit: ray along +Y hits the cube",
                   RayAabbIntersect(MakeRay(Vec3{ 0, -8, 0 },
                                            Vec3{ 0, 1, 0 }), cube, t));
        ExpectNear("aabb-hit: enters at the y=-1 face, t=7", t, 7.0f);
    }
    {
        // An off-origin box: corner at (10,10,0), 10 units wide on X and Y,
        // 16 tall. A ray dropped down through its top face.
        const Aabb box{ Vec3{ 10, 10, 0 }, Vec3{ 20, 20, 16 } };
        float t = -1.0f;
        ExpectTrue("aabb-hit: ray dropped onto an off-origin box hits",
                   RayAabbIntersect(MakeRay(Vec3{ 15, 15, 100 },
                                            Vec3{ 0, 0, -1 }), box, t));
        ExpectNear("aabb-hit: enters at the z=16 top face, t=84", t, 84.0f);
    }
    {
        // A 45-degree descent toward the origin: from (-20,0,20) along
        // (1,0,-1). The ray runs straight through the cube center; it enters
        // at the corner where the x=-1 and z=1 faces coincide, after
        // travelling 19 units in X and 19 in Z — world distance 19*sqrt(2).
        const Aabb cube{ Vec3{ -1, -1, -1 }, Vec3{ 1, 1, 1 } };
        float t = -1.0f;
        ExpectTrue("aabb-hit: diagonal ray through the cube center hits",
                   RayAabbIntersect(MakeRay(Vec3{ -20, 0, 20 },
                                            Vec3{ 1, 0, -1 }), cube, t));
        ExpectNearEps("aabb-hit: diagonal entry t = sqrt(2)*19", t,
                      std::sqrt(2.0f) * 19.0f, 0.01f);
    }

    // ---- Section D: RayAabbIntersect misses / behind / parallel -----------
    {
        const Aabb cube{ Vec3{ -1, -1, -1 }, Vec3{ 1, 1, 1 } };
        float t = 99.0f;
        // PIN miss: a ray offset in X passes beside the cube entirely.
        ExpectTrue("PIN aabb-miss: ray offset past the cube reports no hit",
                   !RayAabbIntersect(MakeRay(Vec3{ 50, 0, 10 },
                                             Vec3{ 0, 0, -1 }), cube, t));
        ExpectNear("aabb-miss: a miss leaves t at 0", t, 0.0f);
    }
    {
        // PIN box-behind-origin: the box sits behind a downward ray. Its
        // crossings are at negative t -> not a hit.
        const Aabb behind{ Vec3{ -2, -2, 198 }, Vec3{ 2, 2, 202 } };
        float t = 99.0f;
        ExpectTrue("PIN aabb-behind: box behind the ray origin is no hit",
                   !RayAabbIntersect(MakeRay(Vec3{ 0, 0, 100 },
                                             Vec3{ 0, 0, -1 }), behind, t));
        ExpectNear("aabb-behind: a behind-miss leaves t at 0", t, 0.0f);
    }
    {
        // A box just ahead is a hit; the same box nudged fully behind is not —
        // the t>=0 clamp is the only difference.
        float t = -1.0f;
        const Aabb ahead{ Vec3{ -2, -2, 40 }, Vec3{ 2, 2, 60 } };
        ExpectTrue("aabb-behind: box ahead of the origin still hits",
                   RayAabbIntersect(MakeRay(Vec3{ 0, 0, 100 },
                                            Vec3{ 0, 0, -1 }), ahead, t));
        ExpectNear("aabb-behind: ahead box enters at z=60, t=40", t, 40.0f);
    }
    {
        // PIN parallel-axis: a ray running straight down is parallel to both
        // the X and Y slabs. Inside both -> the Z slab alone decides the hit.
        const Aabb box{ Vec3{ -5, -5, -5 }, Vec3{ 5, 5, 5 } };
        float t = -1.0f;
        ExpectTrue("PIN aabb-parallel: ray parallel to X/Y, inside both, hits",
                   RayAabbIntersect(MakeRay(Vec3{ 0, 0, 100 },
                                            Vec3{ 0, 0, -1 }), box, t));
        ExpectNear("aabb-parallel: parallel-axis hit enters at z=5, t=95",
                   t, 95.0f);
    }
    {
        // PIN parallel-axis: the same downward ray, now offset out of the X
        // slab. Parallel to X AND outside it -> the ray can never enter.
        const Aabb box{ Vec3{ -5, -5, -5 }, Vec3{ 5, 5, 5 } };
        float t = 99.0f;
        ExpectTrue("PIN aabb-parallel: ray parallel to X, outside it, misses",
                   !RayAabbIntersect(MakeRay(Vec3{ 100, 0, 200 },
                                             Vec3{ 0, 0, -1 }), box, t));
    }
    {
        // Parallel to Y and outside the Y slab also misses.
        const Aabb box{ Vec3{ -5, -5, -5 }, Vec3{ 5, 5, 5 } };
        float t = 99.0f;
        ExpectTrue("aabb-parallel: ray parallel to Y, outside it, misses",
                   !RayAabbIntersect(MakeRay(Vec3{ 0, 999, 200 },
                                             Vec3{ 0, 0, -1 }), box, t));
    }
    {
        // A ray that grazes along a face plane (parallel, exactly on the
        // boundary) is treated as inside the slab -> the other axes decide.
        const Aabb box{ Vec3{ -5, -5, 0 }, Vec3{ 5, 5, 10 } };
        float t = -1.0f;
        ExpectTrue("aabb-parallel: ray on the x=5 boundary still enters",
                   RayAabbIntersect(MakeRay(Vec3{ 5, 0, 100 },
                                            Vec3{ 0, 0, -1 }), box, t));
    }
    {
        // PIN invalid-aabb: an inverted box is screened out — no phantom hit
        // even for a ray that would geometrically pass through its corners.
        const Aabb inverted{ Vec3{ 1, 1, 1 }, Vec3{ -1, -1, -1 } };
        float t = 99.0f;
        ExpectTrue("PIN aabb-invalid: inverted box yields no hit",
                   !RayAabbIntersect(MakeRay(Vec3{ 0, 0, 10 },
                                             Vec3{ 0, 0, -1 }), inverted, t));
    }
    {
        // An invalid ray never hits, whatever box it is given.
        WorldRay bad{};
        bad.origin    = Vec3{ 0, 0, 10 };
        bad.direction = Vec3{ 0, 0, -1 };
        bad.valid     = false;
        const Aabb cube{ Vec3{ -1, -1, -1 }, Vec3{ 1, 1, 1 } };
        float t = 99.0f;
        ExpectTrue("aabb-invalid: invalid ray yields no hit",
                   !RayAabbIntersect(bad, cube, t));
    }

    // ---- Section E: RayAabbIntersect origin inside the box ----------------
    {
        // PIN origin-inside: a ray whose origin is inside the box hits it at
        // t=0 — the camera being "in" a unit is a hit, not a miss.
        const Aabb big{ Vec3{ -10, -10, -10 }, Vec3{ 10, 10, 10 } };
        float t = -1.0f;
        ExpectTrue("PIN aabb-inside: ray starting inside the box hits",
                   RayAabbIntersect(MakeRay(Vec3{ 0, 0, 0 },
                                            Vec3{ 0, 0, -1 }), big, t));
        ExpectNear("PIN aabb-inside: inside-box hit reports t=0", t, 0.0f);
    }
    {
        // Origin inside, ray pointing any which way — still t=0.
        const Aabb big{ Vec3{ -10, -10, -10 }, Vec3{ 10, 10, 10 } };
        float t = -1.0f;
        ExpectTrue("aabb-inside: inside-box hit holds for a diagonal ray",
                   RayAabbIntersect(MakeRay(Vec3{ 1, 2, 3 },
                                            Vec3{ 1, 1, 1 }), big, t));
        ExpectNear("aabb-inside: diagonal inside-box hit also reports t=0",
                   t, 0.0f);
    }

    // ---- Section F: NearestUnitHit ----------------------------------------
    {
        // Two boxes on one downward ray. The array lists the FAR box first;
        // NearestUnitHit must still return the near box.
        UnitAabb units[2];
        units[0].handle = 0xFAAAull;  // far: centered z=10
        units[0].box    = Aabb{ Vec3{ -2, -2, 8 }, Vec3{ 2, 2, 12 } };
        units[1].handle = 0x4EE7ull;  // near: centered z=50
        units[1].box    = Aabb{ Vec3{ -2, -2, 48 }, Vec3{ 2, 2, 52 } };

        const WorldRay ray = MakeRay(Vec3{ 0, 0, 100 }, Vec3{ 0, 0, -1 });
        const UnitHit hit  = NearestUnitHit(ray, units, 2);
        ExpectTrue("PIN nearest: a ray crossing two boxes reports a hit",
                   hit.hit);
        ExpectInt("PIN nearest: the NEAR box wins (index 1, listed second)",
                  hit.index, 1);
        ExpectTrue("PIN nearest: nearest hit carries the near box handle",
                   hit.handle == 0x4EE7ull);
        ExpectNear("PIN nearest: nearest hit enters at z=52, t=48", hit.t,
                   48.0f);
    }
    {
        // The same two boxes, array order reversed — the near box is now
        // listed first. The result must be identical (handle + t), proving
        // the pick is geometry-driven, not array-order-driven.
        UnitAabb units[2];
        units[0].handle = 0x4EE7ull;  // near
        units[0].box    = Aabb{ Vec3{ -2, -2, 48 }, Vec3{ 2, 2, 52 } };
        units[1].handle = 0xFAAAull;  // far
        units[1].box    = Aabb{ Vec3{ -2, -2, 8 }, Vec3{ 2, 2, 12 } };

        const UnitHit hit = NearestUnitHit(
            MakeRay(Vec3{ 0, 0, 100 }, Vec3{ 0, 0, -1 }), units, 2);
        ExpectInt("nearest: near box wins again when listed first", hit.index,
                  0);
        ExpectTrue("nearest: handle is geometry-driven, not order-driven",
                   hit.handle == 0x4EE7ull);
        ExpectNear("nearest: t is geometry-driven (48) regardless of order",
                   hit.t, 48.0f);
    }
    {
        // A single box straight ahead.
        UnitAabb units[1];
        units[0].handle = 0x1234ull;
        units[0].box    = Aabb{ Vec3{ -3, -3, -3 }, Vec3{ 3, 3, 3 } };
        const UnitHit hit = NearestUnitHit(
            MakeRay(Vec3{ 0, 0, 40 }, Vec3{ 0, 0, -1 }), units, 1);
        ExpectTrue("nearest: a single box ahead is hit", hit.hit);
        ExpectInt("nearest: single-box hit reports index 0", hit.index, 0);
        ExpectTrue("nearest: single-box hit carries its handle",
                   hit.handle == 0x1234ull);
        ExpectNear("nearest: single-box hit enters at z=3, t=37", hit.t,
                   37.0f);
    }
    {
        // Tie: two boxes whose entry faces are at the exact same distance.
        // The lower array index must win.
        UnitAabb units[2];
        units[0].handle = 0xA0A0ull;
        units[0].box    = Aabb{ Vec3{ -2, -2, 48 }, Vec3{ 2, 2, 52 } };
        units[1].handle = 0xB1B1ull;
        units[1].box    = Aabb{ Vec3{ -2, -2, 48 }, Vec3{ 2, 2, 52 } };
        const UnitHit hit = NearestUnitHit(
            MakeRay(Vec3{ 0, 0, 100 }, Vec3{ 0, 0, -1 }), units, 2);
        ExpectInt("PIN nearest: an entry-distance tie picks the lower index",
                  hit.index, 0);
        ExpectTrue("nearest: tie winner carries the index-0 handle",
                   hit.handle == 0xA0A0ull);
    }
    {
        // Three boxes; only the middle one lies on the ray.
        UnitAabb units[3];
        units[0].handle = 0x01ull;
        units[0].box    = Aabb{ Vec3{ 100, 100, 0 }, Vec3{ 110, 110, 10 } };
        units[1].handle = 0x02ull;
        units[1].box    = Aabb{ Vec3{ -4, -4, 20 }, Vec3{ 4, 4, 30 } };
        units[2].handle = 0x03ull;
        units[2].box    = Aabb{ Vec3{ -200, -200, 0 }, Vec3{ -190, -190, 5 } };
        const UnitHit hit = NearestUnitHit(
            MakeRay(Vec3{ 0, 0, 100 }, Vec3{ 0, 0, -1 }), units, 3);
        ExpectTrue("nearest: ray crossing only the middle box hits it",
                   hit.hit);
        ExpectInt("nearest: middle-box-only hit reports index 1", hit.index,
                  1);
        ExpectTrue("nearest: middle-box-only hit carries handle 0x02",
                   hit.handle == 0x02ull);
    }

    // ---- Section G: NearestUnitHit edge cases -----------------------------
    {
        // PIN miss: a ray that passes beside every box.
        UnitAabb units[2];
        units[0].handle = 0xDEADull;
        units[0].box    = Aabb{ Vec3{ -2, -2, 8 }, Vec3{ 2, 2, 12 } };
        units[1].handle = 0xBEEFull;
        units[1].box    = Aabb{ Vec3{ -2, -2, 48 }, Vec3{ 2, 2, 52 } };
        const UnitHit hit = NearestUnitHit(
            MakeRay(Vec3{ 500, 0, 100 }, Vec3{ 0, 0, -1 }), units, 2);
        ExpectTrue("PIN nearest-miss: a ray beside every box reports no hit",
                   !hit.hit);
        ExpectInt("PIN nearest-miss: a miss reports index -1", hit.index, -1);
        ExpectTrue("PIN nearest-miss: a miss reports handle 0",
                   hit.handle == 0ull);
        ExpectNear("nearest-miss: a miss reports t 0", hit.t, 0.0f);
    }
    {
        // A null unit list is a miss, not a crash.
        const UnitHit hit = NearestUnitHit(
            MakeRay(Vec3{ 0, 0, 100 }, Vec3{ 0, 0, -1 }), nullptr, 10);
        ExpectTrue("nearest-edge: null unit list -> no hit", !hit.hit);
        ExpectInt("nearest-edge: null unit list -> index -1", hit.index, -1);
    }
    {
        // A non-positive count is a miss.
        UnitAabb units[1];
        units[0].handle = 0x1ull;
        units[0].box    = Aabb{ Vec3{ -3, -3, -3 }, Vec3{ 3, 3, 3 } };
        const WorldRay ray = MakeRay(Vec3{ 0, 0, 40 }, Vec3{ 0, 0, -1 });
        ExpectTrue("nearest-edge: count 0 -> no hit",
                   !NearestUnitHit(ray, units, 0).hit);
        ExpectTrue("nearest-edge: negative count -> no hit",
                   !NearestUnitHit(ray, units, -5).hit);
    }
    {
        // An invalid ray is a miss.
        UnitAabb units[1];
        units[0].handle = 0x1ull;
        units[0].box    = Aabb{ Vec3{ -3, -3, -3 }, Vec3{ 3, 3, 3 } };
        WorldRay bad{};
        bad.origin    = Vec3{ 0, 0, 40 };
        bad.direction = Vec3{ 0, 0, -1 };
        bad.valid     = false;
        ExpectTrue("nearest-edge: invalid ray -> no hit",
                   !NearestUnitHit(bad, units, 1).hit);
    }
    {
        // PIN invalid-aabb: an inverted box in the list never produces a hit,
        // while a valid box alongside it still does.
        UnitAabb units[2];
        units[0].handle = 0xBADull;  // inverted — must be skipped
        units[0].box    = Aabb{ Vec3{ 2, 2, 52 }, Vec3{ -2, -2, 48 } };
        units[1].handle = 0x600Dull;  // valid
        units[1].box    = Aabb{ Vec3{ -2, -2, 8 }, Vec3{ 2, 2, 12 } };
        const UnitHit hit = NearestUnitHit(
            MakeRay(Vec3{ 0, 0, 100 }, Vec3{ 0, 0, -1 }), units, 2);
        ExpectTrue("PIN nearest-invalid: an inverted box is skipped", hit.hit);
        ExpectInt("PIN nearest-invalid: the valid box (index 1) is the hit",
                  hit.index, 1);
        ExpectTrue("PIN nearest-invalid: hit carries the valid box handle",
                   hit.handle == 0x600Dull);
    }
    {
        // A list of only inverted boxes is a clean miss.
        UnitAabb units[2];
        units[0].handle = 0x1ull;
        units[0].box    = Aabb{ Vec3{ 1, 1, 1 }, Vec3{ -1, -1, -1 } };
        units[1].handle = 0x2ull;
        units[1].box    = Aabb{ Vec3{ 9, 9, 9 }, Vec3{ -9, -9, -9 } };
        ExpectTrue("nearest-invalid: an all-inverted list is a clean miss",
                   !NearestUnitHit(MakeRay(Vec3{ 0, 0, 100 },
                                           Vec3{ 0, 0, -1 }), units, 2).hit);
    }
    {
        // PIN budget-clamp: an 80-entry list with a hittable box at index 63
        // (inside the kMaxRaycastUnits=64 budget) AND a NEARER hittable box at
        // index 70 (past the budget). NearestUnitHit walks only the first 64,
        // so index 63 wins — a broken clamp would return the nearer index 70.
        static UnitAabb many[80];
        for (int i = 0; i < 80; ++i)
        {
            many[i].handle = static_cast<std::uint64_t>(0x1000 + i);
            // Far off the ray's X column -> a clean miss for every filler box.
            many[i].box = Aabb{ Vec3{ 10000, 10000, 0 },
                                Vec3{ 10010, 10010, 10 } };
        }
        // Index 63: inside the budget, FAR down the ray (enters at z=12).
        many[63].box = Aabb{ Vec3{ -2, -2, 8 }, Vec3{ 2, 2, 12 } };
        // Index 70: past the budget, NEARER (enters at z=82).
        many[70].box = Aabb{ Vec3{ -2, -2, 78 }, Vec3{ 2, 2, 82 } };

        const UnitHit hit = NearestUnitHit(
            MakeRay(Vec3{ 0, 0, 100 }, Vec3{ 0, 0, -1 }), many, 80);
        ExpectTrue("PIN budget: a hit inside the raycast budget is found",
                   hit.hit);
        ExpectInt("PIN budget: walk stops at kMaxRaycastUnits — index 63 wins",
                  hit.index, 63);
        ExpectTrue("PIN budget: the past-budget index-70 box is never seen",
                   hit.index != 70);
        ExpectInt("budget: kMaxRaycastUnits is the documented 64",
                  kMaxRaycastUnits, 64);
    }

    // ---- Section H: PickUnitAtCursor convenience seam ---------------------
    {
        const float vw = 1280.0f;
        const float vh = 720.0f;
        const Mat4 view = MakeLookAtRH(Vec3{ 0, -100, 120 }, Vec3{ 0, 0, 0 },
                                       Vec3{ 0, 0, 1 });
        const Mat4 proj = MakePerspectiveFovRH(1.0f, vw / vh, 1.0f, 800.0f);
        const Mat4 viewProj = Mat4Multiply(view, proj);

        // One unit straddling the world origin on the ground.
        UnitAabb units[1];
        units[0].handle = 0xC0FFEEull;
        units[0].box    = AabbFromCenterExtents(Vec3{ 0, 0, 8 }, 12, 12, 8);

        // A center-screen click looks straight at the origin -> hits the unit.
        const UnitHit hit = PickUnitAtCursor(vw * 0.5f, vh * 0.5f, vw, vh,
                                             viewProj, units, 1);
        ExpectTrue("pick: center click on a unit at the origin hits it",
                   hit.hit);
        ExpectTrue("pick: the hit carries the unit handle",
                   hit.handle == 0xC0FFEEull);

        // The view+projection overload must agree with the pre-multiplied one.
        const UnitHit hit2 = PickUnitAtCursor(vw * 0.5f, vh * 0.5f, vw, vh,
                                              view, proj, units, 1);
        ExpectTrue("pick: view+proj overload agrees on the hit", hit2.hit);
        ExpectTrue("pick: view+proj overload agrees on the handle",
                   hit2.handle == hit.handle);
        ExpectInt("pick: view+proj overload agrees on the index", hit2.index,
                  hit.index);

        // A click in the far corner of the screen aims off the lone unit.
        const UnitHit corner = PickUnitAtCursor(2.0f, 2.0f, vw, vh,
                                                viewProj, units, 1);
        ExpectTrue("pick: a corner click misses the centered unit",
                   !corner.hit);

        // A degenerate viewport yields an invalid ray -> a clean miss.
        const UnitHit deg = PickUnitAtCursor(vw * 0.5f, vh * 0.5f, 0.0f, vh,
                                             viewProj, units, 1);
        ExpectTrue("pick: a zero-width viewport misses cleanly", !deg.hit);
        ExpectInt("pick: a degenerate-viewport miss reports index -1",
                  deg.index, -1);
    }

    // ---- Section I: integration — the iter-300 click pipeline -------------
    // The exact sequence the iter-300 click-to-inspect glue runs: a cursor
    // pixel -> CursorRay -> NearestUnitHit -> the unit under the cursor. Two
    // units are placed on the battlefield; clicking each one's projected
    // center must recover that unit, and a click on empty ground recovers
    // nothing.
    {
        const float vw = 1280.0f;
        const float vh = 720.0f;
        const Mat4 view = MakeLookAtRH(Vec3{ 40, -150, 110 }, Vec3{ 0, 0, 0 },
                                       Vec3{ 0, 0, 1 });
        const Mat4 proj = MakePerspectiveFovRH(0.9f, vw / vh, 1.0f, 900.0f);
        const Mat4 viewProj = Mat4Multiply(view, proj);

        // Two units standing on the z=0 ground, well apart in the world.
        const Vec3 footA{ -60.0f, 40.0f, 0.0f };
        const Vec3 footB{ 70.0f, -30.0f, 0.0f };
        UnitAabb units[2];
        units[0].handle = 0xA11CE0ull;
        units[0].box    = AabbFromCenterExtents(
            Vec3{ footA.x, footA.y, 10.0f }, 14, 14, 10);
        units[1].handle = 0xB0B0B0ull;
        units[1].box    = AabbFromCenterExtents(
            Vec3{ footB.x, footB.y, 10.0f }, 14, 14, 10);

        // Click unit A: project its z=0 FOOTPRINT to a pixel and pick there.
        // The footprint lies on box A's bottom face, so the cursor ray still
        // crosses the whole box (entering at the top face) — and that same
        // ray's z=0 ground crossing lands back on the footprint (the iter-298
        // round trip), which the ground cross-check below relies on.
        const Projected pa = ProjectWorld(footA, viewProj, vw, vh);
        ExpectTrue("integration: unit A projects in front of the camera",
                   pa.inFront);
        const UnitHit hitA = PickUnitAtCursor(pa.sx, pa.sy, vw, vh, viewProj,
                                              units, 2);
        ExpectTrue("integration: clicking unit A's center hits a unit",
                   hitA.hit);
        ExpectInt("integration: the click on unit A resolves to index 0",
                  hitA.index, 0);
        ExpectTrue("integration: unit A click carries handle 0xA11CE0",
                   hitA.handle == 0xA11CE0ull);
        ExpectTrue("integration: unit A entry distance is ahead (t>0)",
                   hitA.t > 0.0f);

        // Click unit B the same way.
        const Vec3 centerB{ footB.x, footB.y, 10.0f };
        const Projected pb = ProjectWorld(centerB, viewProj, vw, vh);
        ExpectTrue("integration: unit B projects in front of the camera",
                   pb.inFront);
        const UnitHit hitB = PickUnitAtCursor(pb.sx, pb.sy, vw, vh, viewProj,
                                              units, 2);
        ExpectTrue("integration: clicking unit B's center hits a unit",
                   hitB.hit);
        ExpectInt("integration: the click on unit B resolves to index 1",
                  hitB.index, 1);
        ExpectTrue("integration: unit B click carries handle 0xB0B0B0",
                   hitB.handle == 0xB0B0B0ull);

        // Click empty ground far from both units — projected from a bare
        // ground point with no unit on it.
        const Projected pe =
            ProjectWorld(Vec3{ 5.0f, 5.0f, 0.0f }, viewProj, vw, vh);
        ExpectTrue("integration: the empty-ground point projects in front",
                   pe.inFront);
        const UnitHit miss = PickUnitAtCursor(pe.sx, pe.sy, vw, vh, viewProj,
                                              units, 2);
        ExpectTrue("integration: a click on empty ground hits no unit",
                   !miss.hit);
        ExpectInt("integration: the empty-ground click reports index -1",
                  miss.index, -1);

        // Cross-check: the picked unit's ray also crosses the z=0 ground —
        // a cursor pick and a Phase 4 drag-drop drop share one ground plane.
        const WorldRay rayA = CursorRay(pa.sx, pa.sy, vw, vh, viewProj);
        const PlaneHit groundA = RayPlaneZ0(rayA);
        ExpectTrue("integration: the unit-A pick ray also reaches the ground",
                   groundA.hit);
        ExpectNearEps("integration: unit-A ground pick recovers footprint X",
                      groundA.x, footA.x, 1.0f);
        ExpectNearEps("integration: unit-A ground pick recovers footprint Y",
                      groundA.y, footA.y, 1.0f);

        // With unit B removed from the list, a click on B's pixel misses.
        const UnitHit bGone = PickUnitAtCursor(pb.sx, pb.sy, vw, vh, viewProj,
                                               units, 1);
        ExpectTrue("integration: clicking B's pixel with only A listed misses",
                   !bGone.hit);
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
