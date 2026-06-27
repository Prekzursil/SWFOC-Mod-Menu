// =============================================================================
// swfoc_overlay/overlay_unit_aabb_test.cpp — unit test for overlay_unit_aabb.h
// (Phase 5 cont., iter 302 / spec line 60).
//
// iter-302 is the unit-AABB storage link of Phase 5: the UnitAabbSet that
// HudSnapshot carries so the iter-299 client-side raycast has a visible-unit
// list to walk. overlay_unit_aabb.h holds the pure data-model logic — a
// fixed-capacity append-only container, a 64-cap raycast budget, an inverted-
// box screen, a handle lookup, and the PickUnitInSet seam onto NearestUnitHit.
// This test pins all of it so the HudSnapshot field and the iter-300/301
// inspector glue can depend on it build-only.
//
// The integration section runs the exact worker -> raycast pipeline: append a
// visible-unit set the way the HUD worker will, fire a world pick ray, and
// confirm PickUnitInSet resolves the nearest unit and FindUnitAabb recovers it
// by handle (the iter-300 inspector resolves by handle, not by array index).
//
// overlay_unit_aabb.h is header-only and std-only (<cstdint>, plus
// <cmath>/<limits> via overlay_hit_test.h). Build + run via
// build_unit_aabb_test.bat — no game, no pipe, no ImGui.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - CAP AT 64            : the 65th append returns false; count stays 64.
//   - APPEND-ONLY ORDER    : entries[i] is the i-th unit appended.
//   - INVALID AABB REJECTED: an inverted box is not stored, count unchanged.
//   - HANDLE 0 IS A MISS   : FindUnitAabb(set, 0) is always nullptr.
//   - CLEAR RESETS         : ClearUnitAabbSet zeroes count; next append at 0.
//   - EMPTY SET CLEAN MISS : PickUnitInSet on the default set hits nothing.
//   - SET FEEDS RAYCAST    : PickUnitInSet picks the same unit a hand-built
//                            NearestUnitHit does — the iter-299/302 seam.
// =============================================================================

#include "overlay_unit_aabb.h"

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
            std::printf("  FAIL %s\n    got : %llu\n    want: %llu\n",
                        name, static_cast<unsigned long long>(got),
                        static_cast<unsigned long long>(want));
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

    void Section(const char* title)
    {
        std::printf("\n[ %s ]\n", title);
    }

    using swfoc_overlay::Aabb;
    using swfoc_overlay::UnitAabb;
    using swfoc_overlay::UnitAabbSet;
    using swfoc_overlay::UnitHit;
    using swfoc_overlay::Vec3;
    using swfoc_overlay::WorldRay;
    using swfoc_overlay::kMaxRaycastUnits;

    // A unit-cube-ish box centred on (cx, cy, cz) with half-extent h on every
    // axis. AabbFromCenterExtents guarantees min <= max.
    Aabb BoxAt(float cx, float cy, float cz, float h)
    {
        return swfoc_overlay::AabbFromCenterExtents(Vec3{ cx, cy, cz },
                                                    h, h, h);
    }

    // A valid pick ray with a unit-length direction so the returned ray
    // parameter `t` reads directly as a world-space distance.
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
    using swfoc_overlay::AppendUnitAabb;
    using swfoc_overlay::ClearUnitAabbSet;
    using swfoc_overlay::FindUnitAabb;
    using swfoc_overlay::NearestUnitHit;
    using swfoc_overlay::PickUnitInSet;

    std::printf("=== overlay_unit_aabb.h unit test (iter-302) ===\n");

    // ---- Default-construct state: the honest-defer initial state ----------
    {
        Section("default-construct state");

        UnitAabbSet set{};
        ExpectInt("a default UnitAabbSet has count 0", set.count, 0);
        ExpectInt("the capacity is the raycast budget (64)",
                  kMaxRaycastUnits, 64);
        // Every entry is zero-initialised — no stale handle, no stale box.
        bool allZero = true;
        for (int i = 0; i < kMaxRaycastUnits; ++i)
        {
            if (set.entries[i].handle != 0)
            {
                allZero = false;
            }
        }
        ExpectTrue("every default entry has a 0 (miss-sentinel) handle",
                   allZero);
        // PIN (EMPTY SET CLEAN MISS): the honest-defer state never phantom-hits.
        const WorldRay ray = MakeRay(Vec3{ 0.0f, 0.0f, -100.0f },
                                     Vec3{ 0.0f, 0.0f, 1.0f });
        const UnitHit miss = PickUnitInSet(ray, set);
        ExpectTrue("PIN EMPTY SET CLEAN MISS: empty set -> hit=false",
                   !miss.hit);
        ExpectInt("the empty-set miss reports index -1", miss.index, -1);
        ExpectU64("the empty-set miss reports handle 0", miss.handle, 0);
    }

    // ---- AppendUnitAabb: basic store + append-only order ------------------
    {
        Section("append + append-only order");

        UnitAabbSet set{};
        ExpectTrue("appending the first unit succeeds",
                   AppendUnitAabb(set, 0x1111u, BoxAt(0, 0, 0, 5)));
        ExpectInt("count is 1 after one append", set.count, 1);
        ExpectTrue("appending the second unit succeeds",
                   AppendUnitAabb(set, 0x2222u, BoxAt(10, 0, 0, 5)));
        ExpectTrue("appending the third unit succeeds",
                   AppendUnitAabb(set, 0x3333u, BoxAt(20, 0, 0, 5)));
        ExpectInt("count is 3 after three appends", set.count, 3);

        // PIN (APPEND-ONLY ORDER): entries[i] is the i-th unit appended — the
        // set never sorts or reorders. A "sort by handle" old form would still
        // pass here (handles already ascend), so append in DESCENDING handle
        // order below and confirm the array still follows append order.
        ExpectU64("PIN APPEND-ONLY ORDER: entry[0] is the 1st appended",
                  set.entries[0].handle, 0x1111u);
        ExpectU64("entry[1] is the 2nd appended", set.entries[1].handle,
                  0x2222u);
        ExpectU64("entry[2] is the 3rd appended", set.entries[2].handle,
                  0x3333u);

        UnitAabbSet desc{};
        AppendUnitAabb(desc, 0x9999u, BoxAt(0, 0, 0, 5));
        AppendUnitAabb(desc, 0x4444u, BoxAt(0, 0, 0, 5));
        AppendUnitAabb(desc, 0x1111u, BoxAt(0, 0, 0, 5));
        ExpectU64("descending-handle append keeps entry[0] = first appended",
                  desc.entries[0].handle, 0x9999u);
        ExpectU64("descending-handle append keeps entry[2] = last appended",
                  desc.entries[2].handle, 0x1111u);

        // The stored box round-trips intact.
        ExpectNearEps("the stored box min.x round-trips",
                      set.entries[1].box.min.x, 5.0f, 0.001f);
        ExpectNearEps("the stored box max.x round-trips",
                      set.entries[1].box.max.x, 15.0f, 0.001f);
    }

    // ---- CAP AT 64: the raycast budget -----------------------------------
    {
        Section("cap at kMaxRaycastUnits (64)");

        UnitAabbSet set{};
        // Append exactly the budget — every one succeeds.
        bool all64Stored = true;
        for (int i = 0; i < kMaxRaycastUnits; ++i)
        {
            if (!AppendUnitAabb(set, static_cast<std::uint64_t>(i + 1),
                                BoxAt(static_cast<float>(i), 0, 0, 1)))
            {
                all64Stored = false;
            }
        }
        ExpectTrue("the first 64 appends all succeed", all64Stored);
        ExpectInt("count saturates at the budget (64)", set.count,
                  kMaxRaycastUnits);

        // PIN (CAP AT 64): the 65th append is refused, count is unchanged, the
        // last in-budget entry is untouched — a "no cap" old form overruns the
        // 64-entry array.
        const std::uint64_t lastBefore = set.entries[kMaxRaycastUnits - 1]
                                             .handle;
        ExpectTrue("PIN CAP AT 64: the 65th append returns false",
                   !AppendUnitAabb(set, 0xDEADu, BoxAt(999, 0, 0, 1)));
        ExpectInt("count stays at 64 after the refused append", set.count,
                  kMaxRaycastUnits);
        ExpectU64("the refused append did not overwrite entry[63]",
                  set.entries[kMaxRaycastUnits - 1].handle, lastBefore);
        // Many more refused appends never corrupt the set.
        for (int i = 0; i < 40; ++i)
        {
            AppendUnitAabb(set, 0xBADu, BoxAt(0, 0, 0, 1));
        }
        ExpectInt("count stays at 64 after 40 more refused appends",
                  set.count, kMaxRaycastUnits);
    }

    // ---- INVALID AABB REJECTED -------------------------------------------
    {
        Section("invalid AABB rejected");

        UnitAabbSet set{};
        AppendUnitAabb(set, 0x1111u, BoxAt(0, 0, 0, 5));
        ExpectInt("count is 1 before the invalid append", set.count, 1);

        // An inverted box: min strictly greater than max on the X axis.
        Aabb inverted;
        inverted.min = Vec3{ 10.0f, 0.0f, 0.0f };
        inverted.max = Vec3{ -10.0f, 5.0f, 5.0f };
        // PIN (INVALID AABB REJECTED): the inverted box is not stored.
        ExpectTrue("PIN INVALID AABB REJECTED: inverted box append returns "
                   "false",
                   !AppendUnitAabb(set, 0x2222u, inverted));
        ExpectInt("count is unchanged after the rejected invalid box",
                  set.count, 1);
        ExpectU64("the rejected handle did not land in the set",
                  FindUnitAabb(set, 0x2222u) == nullptr ? 0u : 1u, 0u);

        // A zero-volume box (min == max, a single point) is still valid.
        Aabb point;
        point.min = Vec3{ 3.0f, 3.0f, 3.0f };
        point.max = Vec3{ 3.0f, 3.0f, 3.0f };
        ExpectTrue("a zero-volume point box is still a valid append",
                   AppendUnitAabb(set, 0x3333u, point));
        ExpectInt("count is 2 after the valid point box", set.count, 2);
    }

    // ---- center + half-extents overload ----------------------------------
    {
        Section("center + half-extents overload");

        UnitAabbSet a{};
        UnitAabbSet b{};
        // The overload must build the same box AabbFromCenterExtents does.
        AppendUnitAabb(a, 0x55u, Vec3{ 2.0f, 4.0f, 6.0f }, 1.0f, 2.0f, 3.0f);
        AppendUnitAabb(b, 0x55u,
                       swfoc_overlay::AabbFromCenterExtents(
                           Vec3{ 2.0f, 4.0f, 6.0f }, 1.0f, 2.0f, 3.0f));
        ExpectInt("the overload stored one entry", a.count, 1);
        ExpectNearEps("overload min.x matches the explicit-box form",
                      a.entries[0].box.min.x, b.entries[0].box.min.x, 0.001f);
        ExpectNearEps("overload max.z matches the explicit-box form",
                      a.entries[0].box.max.z, b.entries[0].box.max.z, 0.001f);
        ExpectNearEps("overload min.x is center.x - |hx|",
                      a.entries[0].box.min.x, 1.0f, 0.001f);
        ExpectNearEps("overload max.y is center.y + |hy|",
                      a.entries[0].box.max.y, 6.0f, 0.001f);

        // Negative half-extents take their magnitude — never an inverted box.
        UnitAabbSet neg{};
        ExpectTrue("negative half-extents still produce a valid append",
                   AppendUnitAabb(neg, 0x66u, Vec3{ 0.0f, 0.0f, 0.0f },
                                  -4.0f, -4.0f, -4.0f));
        ExpectNearEps("negative half-extent box min is -|hx|",
                      neg.entries[0].box.min.x, -4.0f, 0.001f);
    }

    // ---- FindUnitAabb: handle lookup -------------------------------------
    {
        Section("FindUnitAabb handle lookup");

        UnitAabbSet set{};
        AppendUnitAabb(set, 0xAAAAu, BoxAt(0, 0, 0, 5));
        AppendUnitAabb(set, 0xBBBBu, BoxAt(10, 0, 0, 5));
        AppendUnitAabb(set, 0xCCCCu, BoxAt(20, 0, 0, 5));

        const UnitAabb* found = FindUnitAabb(set, 0xBBBBu);
        ExpectTrue("FindUnitAabb locates an appended handle",
                   found != nullptr);
        if (found != nullptr)
        {
            ExpectU64("the found entry carries the queried handle",
                      found->handle, 0xBBBBu);
            ExpectNearEps("the found entry carries that unit's box",
                          found->box.min.x, 5.0f, 0.001f);
        }
        ExpectTrue("FindUnitAabb returns nullptr for an absent handle",
                   FindUnitAabb(set, 0xFFFFu) == nullptr);

        // PIN (HANDLE 0 IS A MISS): handle 0 is the miss sentinel.
        ExpectTrue("PIN HANDLE 0 IS A MISS: FindUnitAabb(set, 0) is nullptr",
                   FindUnitAabb(set, 0u) == nullptr);
        // Even if a 0-handle entry somehow exists, the query for 0 still misses.
        UnitAabbSet withZero{};
        AppendUnitAabb(withZero, 0u, BoxAt(0, 0, 0, 5));
        ExpectTrue("a query for handle 0 misses even a stored 0-handle entry",
                   FindUnitAabb(withZero, 0u) == nullptr);
    }

    // ---- ClearUnitAabbSet ------------------------------------------------
    {
        Section("ClearUnitAabbSet");

        UnitAabbSet set{};
        AppendUnitAabb(set, 0x1u, BoxAt(0, 0, 0, 5));
        AppendUnitAabb(set, 0x2u, BoxAt(10, 0, 0, 5));
        ExpectInt("count is 2 before clear", set.count, 2);

        // PIN (CLEAR RESETS): clear zeroes count; the next append lands at 0.
        ClearUnitAabbSet(set);
        ExpectInt("PIN CLEAR RESETS: count is 0 after ClearUnitAabbSet",
                  set.count, 0);
        ExpectTrue("FindUnitAabb finds nothing in a cleared set",
                   FindUnitAabb(set, 0x1u) == nullptr);
        ExpectTrue("appending into a cleared set succeeds",
                   AppendUnitAabb(set, 0x7u, BoxAt(0, 0, 0, 5)));
        ExpectInt("the cleared set's next append lands at index 0",
                  set.count, 1);
        ExpectU64("the post-clear append is at entry[0]",
                  set.entries[0].handle, 0x7u);

        // Clear is idempotent and safe on an already-empty set.
        UnitAabbSet empty{};
        ClearUnitAabbSet(empty);
        ExpectInt("clearing an already-empty set keeps count 0",
                  empty.count, 0);
    }

    // ---- SET FEEDS RAYCAST: PickUnitInSet -> NearestUnitHit --------------
    {
        Section("PickUnitInSet feeds the raycast");

        // Three units laid along the +Z axis. The ray enters the near one
        // (0x1111 at z=0) first, then the far one (0x2222 at z=50). 0x3333 is
        // off the ray (x=100) entirely.
        UnitAabbSet set{};
        AppendUnitAabb(set, 0x3333u, BoxAt(100, 0, 0, 5));   // off-axis
        AppendUnitAabb(set, 0x1111u, BoxAt(0, 0, 0, 5));     // near
        AppendUnitAabb(set, 0x2222u, BoxAt(0, 0, 50, 5));    // far

        const WorldRay ray = MakeRay(Vec3{ 0.0f, 0.0f, -100.0f },
                                     Vec3{ 0.0f, 0.0f, 1.0f });

        // PIN (SET FEEDS RAYCAST): PickUnitInSet returns exactly what a
        // hand-built NearestUnitHit over the set's range returns.
        const UnitHit viaSet  = PickUnitInSet(ray, set);
        const UnitHit viaHand = NearestUnitHit(ray, set.entries, set.count);
        ExpectTrue("PIN SET FEEDS RAYCAST: PickUnitInSet hit matches "
                   "NearestUnitHit",
                   viaSet.hit == viaHand.hit
                       && viaSet.handle == viaHand.handle
                       && viaSet.index == viaHand.index);
        ExpectTrue("PickUnitInSet reports a hit on the populated set",
                   viaSet.hit);
        // The NEAR unit wins even though it was appended SECOND — the raycast
        // picks by entry distance, not by array index.
        ExpectU64("PickUnitInSet picks the nearest unit (0x1111)",
                  viaSet.handle, 0x1111u);
        ExpectInt("the nearest unit is at array index 1 (append order)",
                  viaSet.index, 1);

        // The picked handle round-trips through FindUnitAabb — exactly the
        // iter-300 inspector's resolve-by-handle step.
        const UnitAabb* picked = FindUnitAabb(set, viaSet.handle);
        ExpectTrue("the picked handle resolves back via FindUnitAabb",
                   picked != nullptr);

        // A ray well off every box is a clean miss.
        const WorldRay astray = MakeRay(Vec3{ 0.0f, 1000.0f, -100.0f },
                                        Vec3{ 0.0f, 0.0f, 1.0f });
        ExpectTrue("a ray past every box -> PickUnitInSet miss",
                   !PickUnitInSet(astray, set).hit);

        // An invalid ray is a miss regardless of the set.
        WorldRay bad{};
        bad.valid = false;
        ExpectTrue("an invalid ray -> PickUnitInSet miss",
                   !PickUnitInSet(bad, set).hit);
    }

    // ---- Integration: worker-style fill -> raycast -> resolve ------------
    {
        Section("integration: worker fill -> pick -> resolve");

        // Build a visible-unit set the way the HUD worker will once a per-unit
        // AABB bridge wire lands: clear, then append each enumerated unit.
        UnitAabbSet snap{};
        ClearUnitAabbSet(snap);
        struct Enum { std::uint64_t handle; float x, y, z; };
        const Enum visible[] = {
            { 0xA001u,  -30.0f, 0.0f,   0.0f },
            { 0xA002u,    0.0f, 0.0f,   0.0f },
            { 0xA003u,   30.0f, 0.0f,   0.0f },
            { 0xA004u,    0.0f, 0.0f,  60.0f },
        };
        for (const Enum& e : visible)
        {
            AppendUnitAabb(snap, e.handle, Vec3{ e.x, e.y, e.z },
                           6.0f, 6.0f, 6.0f);
        }
        ExpectInt("the worker fill stored all 4 visible units", snap.count, 4);

        // Operator clicks straight down the +Z axis at the origin column —
        // 0xA002 (x=0,z=0) is nearest, 0xA004 (x=0,z=60) is behind it.
        const WorldRay click = MakeRay(Vec3{ 0.0f, 0.0f, -200.0f },
                                       Vec3{ 0.0f, 0.0f, 1.0f });
        const UnitHit hit = PickUnitInSet(click, snap);
        ExpectTrue("the click resolves to a unit", hit.hit);
        ExpectU64("the click picks the origin-column near unit (0xA002)",
                  hit.handle, 0xA002u);

        // The iter-300 inspector resolves the clicked unit by HANDLE — the
        // box it gets back is that exact unit's, not a sibling's.
        const UnitAabb* inspected = FindUnitAabb(snap, hit.handle);
        ExpectTrue("the inspector resolves the clicked unit by handle",
                   inspected != nullptr);
        if (inspected != nullptr)
        {
            ExpectNearEps("the resolved box is centred on 0xA002's X (0)",
                          (inspected->box.min.x + inspected->box.max.x)
                              * 0.5f,
                          0.0f, 0.001f);
        }

        // A click down a column with no unit (x=+100) inspects nothing.
        const WorldRay empty = MakeRay(Vec3{ 100.0f, 0.0f, -200.0f },
                                       Vec3{ 0.0f, 0.0f, 1.0f });
        ExpectTrue("a click on an empty column resolves no unit",
                   !PickUnitInSet(empty, snap).hit);

        // Re-fill (next refresh tick): clear drops the stale set cleanly.
        ClearUnitAabbSet(snap);
        ExpectInt("the next refresh tick starts from an empty set",
                  snap.count, 0);
        ExpectTrue("after the re-fill clear the old click resolves nothing",
                   !PickUnitInSet(click, snap).hit);
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
