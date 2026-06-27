// =============================================================================
// swfoc_overlay/overlay_hit_test.h — Phase 5 cursor-hit-unit detection kernel.
//
// Phase 5 (iter 297-303) makes the overlay click-aware. iter-297 pinned the
// engine's D3D transform matrices; iter-298 (overlay_cursor_ray.h) turns a
// cursor pixel into a world-space pick ray. iter-299 (spec line 57) is the next
// link: given that pick ray and the visible unit set, find which unit — if any
// — the operator clicked.
//
// SWFOC exposes no "unit under the cursor" engine wire. Rather than pin a new
// bridge RVA, Phase 5 takes Mitigation A — the CLIENT-SIDE RAYCAST: the HUD
// worker already enumerates the visible unit set (SWFOC_EnumerateUnits, LIVE
// iter-104; SWFOC_FindAllObjectsOfType per type, LIVE iter-179), and iter-302
// appends a per-unit world AABB (min/max vec3) to HudSnapshot. This kernel
// walks that AABB list and intersects each box with the pick ray entirely in
// C++ — no new bridge wire, no engine call on the click path.
//
// This header is the pure decision kernel — the math that has a right and a
// wrong answer and so must be pinned by a unit test before the iter-300
// inspector glue depends on it:
//
//   1. AabbIsValid()        — reject an inverted / malformed box before it can
//      produce a phantom hit.
//   2. RayAabbIntersect()   — the slab method: intersect a WorldRay with one
//      axis-aligned box, reporting the entry-face ray parameter. Only
//      crossings AHEAD of the ray origin (t >= 0) count — a box behind the
//      camera is never a hit.
//   3. NearestUnitHit()     — walk the visible-unit AABB list and return the
//      box the ray enters FIRST (smallest t). This is what "the unit under
//      the cursor" means when two units overlap in screen space.
//   4. PickUnitAtCursor()   — the iter-300 convenience seam: cursor pixel +
//      viewport + view-projection + unit list -> nearest hit unit. Composes
//      CursorRay (iter-298) and NearestUnitHit.
//
// The raycast budget is kMaxRaycastUnits (64) — the same cap iter-302 applies
// when it appends the AABB set to HudSnapshot. NearestUnitHit clamps to it
// defensively so a malformed count cannot walk past the snapshot's array.
//
// RED-GREEN REGRESSION PINS (overlay_hit_test_test.cpp)
// ----------------------------------------------------
//   - NEAREST HIT WINS        : with two boxes on the ray, NearestUnitHit
//                               returns the one the ray ENTERS first, even
//                               when the array lists the far box first. A
//                               "return the first hit found" old form fails.
//   - MISS REPORTS NO HIT     : a ray passing beside every box returns
//                               hit=false, index=-1, handle=0 — never a
//                               phantom unit.
//   - BOX BEHIND ORIGIN IGNORED : a box entirely behind the ray origin is not
//                               a hit; the entry parameter must be t >= 0.
//   - PARALLEL-AXIS RAY HANDLED : a ray parallel to one slab axis hits when it
//                               lies within the other slabs and misses when
//                               outside — and never divides by a zero
//                               direction component.
//   - INVALID AABB SKIPPED    : an inverted box (min > max) produces no hit;
//                               AabbIsValid screens it out of the walk.
//   - ORIGIN INSIDE BOX HITS  : a ray whose origin sits inside a box hits it
//                               at t = 0 rather than reporting a miss.
//
// Pure, header-only, std-only (<cmath> via overlay_cursor_ray.h, plus <cstdint>
// and <limits>) — no ImGui, no Windows, no bridge. Unit-tested with a plain
// g++ (build_hit_test_test.bat).
// =============================================================================

#pragma once

#include "overlay_cursor_ray.h"

#include <cmath>
#include <cstdint>
#include <limits>

namespace swfoc_overlay
{
    // The client-side raycast budget. iter-302 caps the per-unit AABB set it
    // appends to HudSnapshot at this many visible units; NearestUnitHit clamps
    // its walk to the same bound so a stale or malformed count can never read
    // past the snapshot's array.
    inline constexpr int kMaxRaycastUnits = 64;

    // An axis-aligned bounding box in world space. `min` holds the smallest
    // coordinate on each axis, `max` the largest. iter-302 appends one of
    // these per visible unit to HudSnapshot.
    struct Aabb
    {
        Vec3 min;
        Vec3 max;
    };

    // One entry in the visible-unit list the cursor raycast walks. `handle` is
    // the engine GameObject pointer (an opaque 64-bit token — the overlay
    // never dereferences it, only passes it back to the bridge); `box` is that
    // unit's world AABB.
    struct UnitAabb
    {
        std::uint64_t handle;
        Aabb          box;
    };

    // The outcome of a cursor raycast. `hit` is true when the ray entered at
    // least one valid unit box. `index` is the position of the nearest hit in
    // the walked array (-1 on a miss); `handle` is that unit's engine token
    // (0 on a miss); `t` is the ray parameter at the entry face — the distance,
    // in ray-direction units, from the ray origin to the box (0 on a miss).
    struct UnitHit
    {
        bool          hit;
        int           index;
        std::uint64_t handle;
        float         t;
    };

    // ---- AABB helpers ------------------------------------------------------

    // True when `box` is well-formed: min <= max on every axis. A zero-volume
    // box (min == max, a single point) is still valid. An inverted box, where
    // some min exceeds its max, is rejected — RayAabbIntersect would otherwise
    // report a spurious empty-interval "hit".
    inline bool AabbIsValid(const Aabb& box)
    {
        return box.min.x <= box.max.x
            && box.min.y <= box.max.y
            && box.min.z <= box.max.z;
    }

    // Build an AABB from a center point and per-axis half-extents. Convenient
    // when the engine reports a unit as a center plus a bounding radius rather
    // than as explicit corners. Negative half-extents would invert the box, so
    // their magnitude is taken.
    inline Aabb AabbFromCenterExtents(const Vec3& center,
                                      float hx, float hy, float hz)
    {
        const float ax = std::fabs(hx);
        const float ay = std::fabs(hy);
        const float az = std::fabs(hz);
        return Aabb{
            Vec3{ center.x - ax, center.y - ay, center.z - az },
            Vec3{ center.x + ax, center.y + ay, center.z + az }
        };
    }

    // ---- Ray vs AABB ------------------------------------------------------

    // Intersect a world-space pick ray with one axis-aligned box using the
    // slab method. Returns true and writes the entry-face ray parameter to
    // `tHit` when the ray crosses the box AHEAD of its origin; returns false
    // (leaving `tHit` at 0) on a miss, an invalid ray, or an invalid box.
    //
    // Only crossings with t >= 0 count: the hit interval starts clamped to the
    // ray origin, so a box entirely behind the camera is reported as a miss. A
    // ray whose origin lies inside the box hits at t = 0.
    //
    // A direction component at or below kRayParallelEpsilon means the ray runs
    // parallel to that pair of slab planes: the axis then imposes no near/far
    // bound, but if the origin lies outside the slab the ray can never enter
    // the box — and the division that would blow up on a zero direction is
    // skipped entirely.
    inline bool RayAabbIntersect(const WorldRay& ray, const Aabb& box,
                                 float& tHit)
    {
        tHit = 0.0f;

        if (!ray.valid || !AabbIsValid(box))
        {
            return false;
        }

        // The hit interval [tEntry, tExit] starts as the whole forward ray.
        float tEntry = 0.0f;
        float tExit  = std::numeric_limits<float>::infinity();

        const float o[3]  = { ray.origin.x, ray.origin.y, ray.origin.z };
        const float d[3]  = { ray.direction.x, ray.direction.y,
                              ray.direction.z };
        const float lo[3] = { box.min.x, box.min.y, box.min.z };
        const float hi[3] = { box.max.x, box.max.y, box.max.z };

        for (int axis = 0; axis < 3; ++axis)
        {
            if (std::fabs(d[axis]) <= kRayParallelEpsilon)
            {
                // Ray parallel to this slab — outside it means no entry at all.
                if (o[axis] < lo[axis] || o[axis] > hi[axis])
                {
                    return false;
                }
                continue;  // inside the slab; axis adds no near/far bound
            }

            const float inv = 1.0f / d[axis];
            float t1 = (lo[axis] - o[axis]) * inv;
            float t2 = (hi[axis] - o[axis]) * inv;
            if (t1 > t2)
            {
                const float swap = t1;
                t1 = t2;
                t2 = swap;
            }

            if (t1 > tEntry)
            {
                tEntry = t1;
            }
            if (t2 < tExit)
            {
                tExit = t2;
            }
            if (tEntry > tExit)
            {
                return false;  // slabs do not overlap — ray misses the box
            }
        }

        tHit = tEntry;
        return true;
    }

    // ---- Nearest-unit pick ------------------------------------------------

    // Walk the visible-unit AABB list and return the unit whose box the pick
    // ray enters FIRST — the one with the smallest non-negative entry
    // parameter. That is what "the unit under the cursor" means: when two
    // units overlap in screen space the nearer one is picked.
    //
    // Returns a miss (hit=false, index=-1, handle=0, t=0) when the ray is
    // invalid, the list pointer is null, the count is non-positive, or no box
    // is crossed. The walk is clamped to kMaxRaycastUnits so a malformed count
    // cannot read past the HudSnapshot array. On a tie in entry distance the
    // lower array index wins (the strict `<` keeps the first-found hit).
    inline UnitHit NearestUnitHit(const WorldRay& ray,
                                  const UnitAabb* units, int count)
    {
        UnitHit result{};
        result.hit    = false;
        result.index  = -1;
        result.handle = 0;
        result.t      = 0.0f;

        if (!ray.valid || units == nullptr || count <= 0)
        {
            return result;
        }

        int walk = count;
        if (walk > kMaxRaycastUnits)
        {
            walk = kMaxRaycastUnits;
        }

        float bestT = 0.0f;
        for (int i = 0; i < walk; ++i)
        {
            float tHit = 0.0f;
            if (!RayAabbIntersect(ray, units[i].box, tHit))
            {
                continue;
            }
            if (!result.hit || tHit < bestT)
            {
                result.hit    = true;
                result.index  = i;
                result.handle = units[i].handle;
                result.t      = tHit;
                bestT         = tHit;
            }
        }
        return result;
    }

    // The iter-300 click-to-inspect seam: turn a cursor pixel straight into
    // the unit under it. Composes CursorRay (iter-298) and NearestUnitHit —
    // the exact sequence the inspector glue runs on a click.
    inline UnitHit PickUnitAtCursor(float sx, float sy, float vw, float vh,
                                    const Mat4& viewProj,
                                    const UnitAabb* units, int count)
    {
        return NearestUnitHit(CursorRay(sx, sy, vw, vh, viewProj),
                              units, count);
    }

    // Convenience overload matching CursorRay's separate view + projection
    // form. Composes the view-projection matrix and delegates.
    inline UnitHit PickUnitAtCursor(float sx, float sy, float vw, float vh,
                                    const Mat4& view, const Mat4& proj,
                                    const UnitAabb* units, int count)
    {
        return NearestUnitHit(CursorRay(sx, sy, vw, vh, view, proj),
                              units, count);
    }
}
