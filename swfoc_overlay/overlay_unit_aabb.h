// =============================================================================
// swfoc_overlay/overlay_unit_aabb.h — Phase 5 unit-AABB snapshot section
// (iter-302, spec line 60).
//
// Phase 5 (iter 297-303) makes the overlay click-aware. iter-298
// (overlay_cursor_ray.h) turns a cursor pixel into a world-space pick ray;
// iter-299 (overlay_hit_test.h) intersects that ray with a list of per-unit
// world AABBs to find the clicked unit. Both kernels need that AABB list to
// exist somewhere the render path can read it every frame.
//
// iter-302 is that storage: the UnitAabbSet — a fixed-capacity, flat-POD,
// append-only container of per-unit world AABBs that HudSnapshot carries from
// this iter onward (hud_state.h embeds one at the struct tail). The HUD worker
// fills it; the render path reads it; the iter-299 raycast walks it.
//
// WHY A FIXED POD ARRAY, NOT std::vector
// --------------------------------------
// HudSnapshot is copied wholesale on every Present detour (GetHudSnapshot) and
// rebuilt every refresh tick (hud_state.cpp). A flat POD array copies in one
// memcpy, never heap-allocates on the render thread, and keeps HudSnapshot's
// binary layout fixed-size — which is what the iter-275 binary-layout-stability
// commitment wants. The capacity is kMaxRaycastUnits (64), the same
// client-side raycast budget overlay_hit_test.h::NearestUnitHit clamps to.
//
// WHY APPEND-ONLY
// ---------------
// AppendUnitAabb only ever writes at index `count` and bumps it — entries are
// never reordered, never removed in place. The i-th appended unit stays at
// entries[i] for the life of the set. NearestUnitHit picks by ray-entry
// distance, not by array index, so append order never biases the pick; but a
// stable order means a handle that resolved to entries[i] last frame still
// resolves there this frame unless the worker rebuilt the set. This is the
// iter-275 stability commitment applied at the data-model level.
//
// This header is the pure decision kernel — the cap / validity / lookup logic
// that has a right and a wrong answer and so must be unit-pinned before the
// HudSnapshot field and the iter-300/301 inspector glue depend on it.
//
// RED-GREEN REGRESSION PINS (overlay_unit_aabb_test.cpp)
// -----------------------------------------------------
//   - CAP AT 64            : the 65th AppendUnitAabb returns false and leaves
//                            count at kMaxRaycastUnits — a "no cap" old form
//                            walks off the array end.
//   - APPEND-ONLY ORDER    : entries[i] is the i-th unit appended — a "sort by
//                            handle" / "reorder" old form fails.
//   - INVALID AABB REJECTED: an inverted box (min > max) is not stored and does
//                            not bump count — AabbIsValid screens it.
//   - HANDLE 0 IS A MISS   : FindUnitAabb(set, 0) is always nullptr — the
//                            engine never hands out a 0 GameObject pointer.
//   - CLEAR RESETS         : ClearUnitAabbSet zeroes count; the next append
//                            lands at index 0.
//   - EMPTY SET CLEAN MISS : PickUnitInSet on the default empty set returns a
//                            clean miss — the honest-defer state can never
//                            produce a phantom inspector hit.
//
// Pure, header-only, std-only (<cstdint>, plus <cmath>/<limits> via
// overlay_hit_test.h). No ImGui, no Windows, no bridge. Unit-tested with a
// plain g++ (build_unit_aabb_test.bat).
// =============================================================================

#pragma once

#include "overlay_hit_test.h"  // Aabb, UnitAabb, UnitHit, WorldRay,
                               // kMaxRaycastUnits, AabbIsValid,
                               // AabbFromCenterExtents, NearestUnitHit

#include <cstdint>

namespace swfoc_overlay
{
    // The visible-unit AABB set HudSnapshot carries from iter-302 onward.
    //
    // Fixed capacity kMaxRaycastUnits (64 — the client-side raycast budget):
    // a flat POD array so the whole set copies trivially with HudSnapshot and
    // never heap-allocates on the render thread. `count` is the number of
    // populated entries and is always kept in [0, kMaxRaycastUnits] by
    // AppendUnitAabb. A default-constructed set is empty (count == 0) — that
    // is the honest-defer state HudSnapshot carries until a per-unit-AABB
    // bridge wire lands.
    struct UnitAabbSet
    {
        int      count = 0;
        UnitAabb entries[kMaxRaycastUnits] = {};
    };

    // Reset the set to empty. The next AppendUnitAabb starts at index 0. The
    // stale entries are left in place — `count` is the only validity boundary,
    // so readers walking [0, count) never see them.
    inline void ClearUnitAabbSet(UnitAabbSet& set)
    {
        set.count = 0;
    }

    // Append one unit's world AABB at the set tail. Returns true when the
    // entry was stored; returns false WITHOUT mutating the set when:
    //   - the set is already full (count >= kMaxRaycastUnits — the raycast
    //     budget; surplus visible units are dropped, never overflow the array);
    //   - `box` is inverted / malformed (AabbIsValid screens it — a bad box
    //     would otherwise produce a phantom RayAabbIntersect hit downstream).
    // A negative `count` (only reachable via a corrupt snapshot) also fails
    // the >= test's intent, so guard it explicitly: treat any out-of-range
    // count as full rather than indexing with it.
    inline bool AppendUnitAabb(UnitAabbSet& set, std::uint64_t handle,
                               const Aabb& box)
    {
        if (set.count < 0 || set.count >= kMaxRaycastUnits)
        {
            return false;
        }
        if (!AabbIsValid(box))
        {
            return false;
        }
        set.entries[set.count].handle = handle;
        set.entries[set.count].box    = box;
        ++set.count;
        return true;
    }

    // Center + half-extents overload — convenient when the engine reports a
    // unit as a center plus a bounding radius rather than as explicit corners.
    // Builds the box via AabbFromCenterExtents (which takes |extent| on each
    // axis, so the box is always valid) and delegates. Same full-set contract.
    inline bool AppendUnitAabb(UnitAabbSet& set, std::uint64_t handle,
                               const Vec3& center, float hx, float hy,
                               float hz)
    {
        return AppendUnitAabb(set, handle,
                              AabbFromCenterExtents(center, hx, hy, hz));
    }

    // Linear handle lookup over the populated range. Returns a pointer to the
    // matching entry, or nullptr when no populated entry carries `handle`.
    // Handle 0 is the miss sentinel — the engine never hands out a 0
    // GameObject pointer — and returns nullptr without scanning. The walk is
    // clamped to kMaxRaycastUnits so a corrupt count cannot read past the
    // array (mirrors NearestUnitHit's defensive clamp).
    inline const UnitAabb* FindUnitAabb(const UnitAabbSet& set,
                                        std::uint64_t handle)
    {
        if (handle == 0)
        {
            return nullptr;
        }
        int walk = set.count;
        if (walk > kMaxRaycastUnits)
        {
            walk = kMaxRaycastUnits;
        }
        for (int i = 0; i < walk; ++i)
        {
            if (set.entries[i].handle == handle)
            {
                return &set.entries[i];
            }
        }
        return nullptr;
    }

    // Cursor-pick convenience over a UnitAabbSet — the iter-299/iter-302 seam.
    // Forwards the set's populated range straight into NearestUnitHit so the
    // iter-300 inspector click path never unpacks the set by hand. An empty
    // set (the honest-defer state) yields a clean miss, never a phantom hit.
    inline UnitHit PickUnitInSet(const WorldRay& ray, const UnitAabbSet& set)
    {
        return NearestUnitHit(ray, set.entries, set.count);
    }
}
