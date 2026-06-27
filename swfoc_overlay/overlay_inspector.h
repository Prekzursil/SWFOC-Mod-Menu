// =============================================================================
// swfoc_overlay/overlay_inspector.h — Phase 5 click-to-inspect panel kernel.
//
// Phase 5 (iter 297-303) makes the overlay click-aware. iter-297 pinned the
// engine's D3D transform matrices, iter-298 (overlay_cursor_ray.h) turns a
// cursor pixel into a world-space pick ray, and iter-299 (overlay_hit_test.h)
// walks the visible-unit AABB set to decide WHICH unit the operator clicked.
// iter-300 (spec line 58) is the next link: take that picked unit and present
// it — an "Inspector" overlay panel showing hull / shield / owner / type /
// position. The write-side action buttons (kill / heal / teleport / swap-owner
// / make-invuln) are iter-301; this iter is READ-ONLY display.
//
// This header is the pure decision kernel of that panel — the logic that has a
// right and a wrong answer and so must be pinned by a unit test before the
// ImGui::Begin("Inspector") glue in overlay.cpp depends on it:
//
//   1. HealthFraction() / HealthPercent() — turn a (current, max) pair into a
//      clamped [0,1] bar fraction and a 0..100 percent. Over-heal (current >
//      max) and a zero / negative max are real engine states the display must
//      survive without overflowing the bar.
//   2. FactionName()         — map an owner faction slot to a readable name.
//   3. FormatHealthLabel() / FormatPositionLabel() — the exact text the panel
//      rows render; deterministic so a test can pin it character-for-character.
//   4. FindUnitByHandle()    — resolve an engine GameObject token to its entry
//      in the visible-unit info set (the parallel of iter-299's UnitAabb set).
//   5. OpenInspectorFor()    — turn an iter-299 UnitHit into a populated panel,
//      resolving the unit by its engine HANDLE (drift-resistant) rather than by
//      the raw array index.
//   6. UpdateInspectorPanel() — the click controller: a valid pick opens /
//      replaces the panel; a miss-click leaves the panel exactly as it was, so
//      clicking empty ground never yanks the inspector shut.
//   7. RefreshInspectorPanel() — re-resolve the open panel against a fresh
//      snapshot each frame so hull / shield / position stay live, and auto-
//      close the panel when the inspected unit dies or leaves the field.
//
// WHERE THE DATA COMES FROM
// -------------------------
// UnitInfo is a self-contained POD — the kernel never owns engine memory. The
// overlay.cpp glue (deferred; lands after iter-302 extends HudSnapshot with the
// per-unit set) populates it: handle + position + AABB from the iter-104 /
// iter-179 visible-unit enumeration, hull / shield / owner / type from the
// iter-167+ getters. The kernel only decides how that data is shown.
//
// RED-GREEN REGRESSION PINS (overlay_inspector_test.cpp)
// -----------------------------------------------------
//   - CLICK OPENS INSPECTOR  : a valid UnitHit opens the panel on the picked
//                              unit — a "no inspector" old form never opens.
//   - MISS LEAVES PANEL      : UpdateInspectorPanel on a miss-pick returns the
//                              prior panel unchanged — a "close on every
//                              click" old form would flicker the panel shut.
//   - HEALTH PERCENT CLAMPS  : over-heal saturates at 100%, negative hull at
//                              0%, max <= 0 reads "n/a" — a bare current / max
//                              old form overflows past 100%.
//   - DEAD UNIT AUTO-CLOSES  : RefreshInspectorPanel against a snapshot missing
//                              the inspected handle closes the panel — a
//                              "never refresh" old form renders a phantom.
//   - FACTION NAME CORRECT   : FactionName(0) is "Rebel", not "Unknown" — a
//                              swapped / missing switch arm fails.
//   - RESOLVE BY HANDLE      : OpenInspectorFor finds the unit by handle, so a
//                              reordered unit list still inspects the right
//                              unit — a raw-index old form picks the wrong one.
//
// Pure, header-only, std-only (<cstdint>, <cstdio>; <cmath> via the include
// chain) — no ImGui, no Windows, no bridge. Unit-tested with a plain g++
// (build_inspector_test.bat).
// =============================================================================

#pragma once

#include "overlay_hit_test.h"   // UnitHit, UnitAabb, kMaxRaycastUnits, Vec3

#include <cstdint>
#include <cstdio>

namespace swfoc_overlay
{
    // Capacity of UnitInfo::type, in bytes (including the nul terminator). The
    // longest SWFOC engine unit-type names ("Underworld_Vengeance_Frigate" and
    // the like) sit well under this; SetUnitType truncates anything longer
    // rather than overrun the buffer.
    inline constexpr int kUnitTypeNameMax = 64;

    // Read-only description of one visible unit, as the Inspector panel needs
    // it. A self-contained POD: `type` is an inline fixed array (not a borrowed
    // pointer), so a UnitInfo copied into an InspectorPanel stays valid even
    // after the source HudSnapshot is recycled. The overlay.cpp glue fills one
    // of these per visible unit; the kernel only reads them.
    struct UnitInfo
    {
        std::uint64_t handle;                 // engine GameObject token
        float         hull;                   // current hull points
        float         hullMax;                // maximum hull points
        float         shield;                 // current shield points
        float         shieldMax;              // maximum shield points
        int           owner;                  // faction slot (see FactionName)
        Vec3          position;               // world-space position
        char          type[kUnitTypeNameMax]; // engine unit-type name, nul-term
    };

    // The Inspector panel's display state. `visible` drives ImGui::Begin; when
    // it is true `unit` holds the currently-inspected unit's data. A default-
    // constructed panel (InspectorPanel{}) is closed with a zeroed unit.
    struct InspectorPanel
    {
        bool     visible;
        UnitInfo unit;
    };

    // ---- Unit-type string helper ------------------------------------------

    // Copy a nul-terminated unit-type name into `info.type`, truncating to
    // kUnitTypeNameMax - 1 characters and always nul-terminating. A null
    // `name` yields an empty string. Safe by construction — never overruns the
    // fixed buffer (no strcpy / strcat).
    inline void SetUnitType(UnitInfo& info, const char* name)
    {
        int i = 0;
        if (name != nullptr)
        {
            for (; name[i] != '\0' && i < kUnitTypeNameMax - 1; ++i)
            {
                info.type[i] = name[i];
            }
        }
        info.type[i] = '\0';
    }

    // ---- Health math ------------------------------------------------------

    // The fraction of `max` that `current` represents, clamped to [0, 1]. A
    // zero or negative `max` (a unit with no shield, say) yields 0 and never
    // divides by zero. Over-heal (current > max) saturates at 1 so an ImGui
    // progress bar is never asked to draw past full.
    inline float HealthFraction(float current, float max)
    {
        if (max <= 0.0f)
        {
            return 0.0f;
        }
        const float f = current / max;
        if (f < 0.0f)
        {
            return 0.0f;
        }
        if (f > 1.0f)
        {
            return 1.0f;
        }
        return f;
    }

    // HealthFraction expressed as a rounded 0..100 percent — the integer the
    // panel prints in "(NN%)". Built on the clamped fraction, so it is itself
    // bounded to [0, 100].
    inline int HealthPercent(float current, float max)
    {
        return static_cast<int>(HealthFraction(current, max) * 100.0f + 0.5f);
    }

    // ---- Field formatting -------------------------------------------------

    // Map an owner faction slot to a readable name. Slots 0 / 1 / 2 are the
    // three SWFOC factions in the same order the Phase 4 faction combo and the
    // iter-92 LED palette use them; any other slot is "Unknown" (defensive — a
    // unit whose owner getter failed must not be mislabelled).
    inline const char* FactionName(int slot)
    {
        switch (slot)
        {
            case 0:  return "Rebel";
            case 1:  return "Empire";
            case 2:  return "Underworld";
            default: return "Unknown";
        }
    }

    // Write a "<current> / <max> (<pct>%)" health label into `buf`. When `max`
    // is zero or negative the unit has no such bar (no shield, typically) and
    // the label is "n/a". The displayed `current` is clamped to [0, max] so an
    // over-healed or negative raw value still reads sensibly; the percent comes
    // from HealthPercent and so is itself clamped. A null buffer or non-
    // positive capacity is a no-op. snprintf, never sprintf — no overrun.
    inline void FormatHealthLabel(char* buf, int cap, float current, float max)
    {
        if (buf == nullptr || cap <= 0)
        {
            return;
        }
        if (max <= 0.0f)
        {
            std::snprintf(buf, static_cast<std::size_t>(cap), "n/a");
            return;
        }
        const float shown = current < 0.0f ? 0.0f
                                           : (current > max ? max : current);
        std::snprintf(buf, static_cast<std::size_t>(cap), "%.0f / %.0f (%d%%)",
                      static_cast<double>(shown), static_cast<double>(max),
                      HealthPercent(current, max));
    }

    // Write a "(x, y, z)" world-position label into `buf`, one decimal place
    // per axis. A null buffer or non-positive capacity is a no-op.
    inline void FormatPositionLabel(char* buf, int cap, const Vec3& p)
    {
        if (buf == nullptr || cap <= 0)
        {
            return;
        }
        std::snprintf(buf, static_cast<std::size_t>(cap), "(%.1f, %.1f, %.1f)",
                      static_cast<double>(p.x), static_cast<double>(p.y),
                      static_cast<double>(p.z));
    }

    // ---- Panel resolution -------------------------------------------------

    // Find the index of the UnitInfo whose `handle` equals `handle`, or -1 if
    // none does. handle 0 is the miss sentinel and never matches. A null list
    // or non-positive count returns -1. The walk is clamped to kMaxRaycastUnits
    // — the same budget iter-302 caps the snapshot's per-unit set at — so a
    // stale count cannot read past the array.
    inline int FindUnitByHandle(const UnitInfo* infos, int count,
                                std::uint64_t handle)
    {
        if (infos == nullptr || count <= 0 || handle == 0)
        {
            return -1;
        }
        int walk = count;
        if (walk > kMaxRaycastUnits)
        {
            walk = kMaxRaycastUnits;
        }
        for (int i = 0; i < walk; ++i)
        {
            if (infos[i].handle == handle)
            {
                return i;
            }
        }
        return -1;
    }

    // Turn an iter-299 cursor pick into a populated Inspector panel. The unit
    // is resolved by its engine HANDLE, not by `pick.index`: the raycast and
    // this call may see the unit list at slightly different moments, and the
    // handle is the stable key. A miss pick, a null / empty info list, or a
    // handle that no longer resolves all yield a closed panel (visible=false).
    inline InspectorPanel OpenInspectorFor(const UnitHit& pick,
                                           const UnitInfo* infos, int count)
    {
        InspectorPanel panel{};
        panel.visible = false;
        if (!pick.hit)
        {
            return panel;
        }
        const int idx = FindUnitByHandle(infos, count, pick.handle);
        if (idx < 0)
        {
            return panel;
        }
        panel.visible = true;
        panel.unit    = infos[idx];
        return panel;
    }

    // The click controller. A valid pick that resolves to a unit opens (or
    // replaces) the panel on that unit. A miss-click — or a pick whose unit no
    // longer resolves — returns `current` UNCHANGED: clicking empty ground,
    // panning the camera, or issuing an order must never yank the inspector
    // shut. The panel is dismissed only by the ImGui window close box or by
    // RefreshInspectorPanel when the inspected unit dies.
    inline InspectorPanel UpdateInspectorPanel(const InspectorPanel& current,
                                               const UnitHit& pick,
                                               const UnitInfo* infos,
                                               int count)
    {
        if (pick.hit)
        {
            const InspectorPanel opened = OpenInspectorFor(pick, infos, count);
            if (opened.visible)
            {
                return opened;
            }
        }
        return current;
    }

    // Re-resolve an open panel against a fresh visible-unit snapshot. While the
    // panel is visible its unit is re-found by handle and its hull / shield /
    // position pulled fresh, so the inspector tracks the unit live. When the
    // handle is no longer present — the unit was destroyed or left the visible
    // set — the panel auto-closes (visible=false) rather than freeze a phantom
    // dead unit on screen. A closed panel passes through untouched.
    inline InspectorPanel RefreshInspectorPanel(const InspectorPanel& current,
                                                const UnitInfo* infos,
                                                int count)
    {
        if (!current.visible)
        {
            return current;
        }
        const int idx = FindUnitByHandle(infos, count, current.unit.handle);
        InspectorPanel refreshed = current;
        if (idx < 0)
        {
            refreshed.visible = false;   // inspected unit died or left the field
            return refreshed;
        }
        refreshed.unit = infos[idx];     // pull fresh hull / shield / position
        return refreshed;
    }
}
