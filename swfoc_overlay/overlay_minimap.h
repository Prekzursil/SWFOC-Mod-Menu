// =============================================================================
// swfoc_overlay/overlay_minimap.h — Phase 4 tactical-minimap kernel.
//
// Phase 4 (iter 292-296) lets the operator place units by drag-drop. iter-292
// shipped a fixed 200x200 spawn pad (overlay_dragdrop.h). iter-293 adds a
// 256x256 top-down MINIMAP: a richer drag-drop target that also plots a dot
// for every spawn the operator has dropped, so they can see at a glance where
// their units landed across the battlefield.
//
// This header is the PURE kernel of the minimap — the parts that have a right
// and a wrong answer and so must be pinned by a unit test before the ImGui
// render glue in overlay.cpp depends on them:
//
//   1. WorldToMinimap() — project a world (X, Y) onto a minimap pixel, so a
//      spawn marker draws where it actually is. A flipped Y axis would draw
//      every dot upside-down.
//   2. MinimapToWorld() — map a minimap drop pixel back to a world (X, Y, 0).
//      It is the EXACT inverse of WorldToMinimap; the test pins the round
//      trip. It delegates to overlay_dragdrop.h::DropPadToWorld — the minimap
//      is just a larger drop pad — so the two coordinate maps cannot diverge.
//   3. SpawnMarkerRing — a fixed-capacity ring of recent spawn-drop points.
//      The eviction order (oldest out when full) has a right answer worth
//      pinning so the minimap's dot history stays stable.
//
// HONEST DEFER (overlay-interactive.md line 51): a true minimap needs the
// engine heightmap + ortho-camera RVAs to know the real tactical-map extent
// and to draw live engine-unit dots from SWFOC_EnumerateUnits. Neither is
// pinned. iter-293 therefore ships the interim: a flat 2D grid over a fixed
// coarse world extent (kMinimapHalfExtent), and the dots plot the operator's
// OWN recent spawn drops — data the overlay already has — rather than the
// live engine unit list. Live per-unit dots arrive in Phase 5 once HudSnapshot
// carries per-unit positions (spec iter-302).
//
// Pure, header-only, std-only — no ImGui, no Windows, no bridge. Unit-tested
// with a plain g++ (build_minimap_test.bat).
// =============================================================================

#pragma once

#include <cstddef>

#include "overlay_dragdrop.h"  // SpawnDrop, DropPadToWorld

namespace swfoc_overlay
{
    // Side length, in pixels, of the square Phase 4 minimap child window.
    // Shared by the BeginChild() size and every WorldToMinimap/MinimapToWorld
    // call so the drawn rect and the coordinate map can never disagree.
    inline constexpr float kMinimapSizePx = 256.0f;

    // World half-extent the minimap spans. The minimap covers
    // [-kMinimapHalfExtent, +kMinimapHalfExtent] on both world axes; its full
    // edge-to-edge span is 2x this value. Interim coarse value for the flat
    // Z=0 plane (see the HONEST DEFER note) — wider than the spawn pad's
    // kSpawnPadHalfExtent (500) because the minimap is the battlefield-wide
    // view while the pad is a fine placement grid near the origin. The real
    // per-map extent needs the engine heightmap RVA, an honest defer.
    inline constexpr float kMinimapHalfExtent = 2000.0f;

    // Capacity of the minimap spawn-marker ring — how many recent spawn drops
    // the minimap plots as dots. Once full, the oldest marker is evicted.
    inline constexpr std::size_t kMinimapMarkerCapacity = 16;

    // A world position projected onto the minimap.
    struct MinimapPoint
    {
        float px;     // pixel X from the minimap's top-left corner, in [0,size]
        float py;     // pixel Y from the minimap's top-left corner, in [0,size]
        bool  onMap;  // true when the world point lay within the mapped extent
    };

    // Project a world (worldX, worldY) position onto a square minimap of side
    // `sizePx` covering [-halfExtent, +halfExtent] on each world axis.
    //
    //   - World origin -> minimap center.
    //   - World X: -halfExtent -> px 0 (left edge), +halfExtent -> px sizePx.
    //   - World Y: +halfExtent -> py 0 (TOP edge), -halfExtent -> py sizePx
    //     (screen-up is world-north — the same top-down convention as
    //     overlay_dragdrop.h::DropPadToWorld).
    //
    // The returned px/py are CLAMPED to [0, sizePx], so a unit outside the
    // mapped extent still draws as an edge dot rather than off the widget;
    // `onMap` reports whether the world point was within the extent (false
    // means the pixel was clamped). A non-positive sizePx or halfExtent is a
    // degenerate minimap and yields { 0, 0, false }.
    inline MinimapPoint WorldToMinimap(float worldX, float worldY,
                                       float sizePx, float halfExtent)
    {
        if (sizePx <= 0.0f || halfExtent <= 0.0f)
        {
            return MinimapPoint{ 0.0f, 0.0f, false };
        }
        const float span = 2.0f * halfExtent;
        // Normalize each world axis to [0, 1] across the mapped extent.
        const float nx = (worldX + halfExtent) / span;  // 0 = left, 1 = right
        const float ny = (halfExtent - worldY) / span;  // 0 = top,  1 = bottom
        // onMap is decided BEFORE clamping — it reports whether the world
        // point lay within the extent, not where the clamped pixel landed.
        const bool onMap = nx >= 0.0f && nx <= 1.0f &&
                           ny >= 0.0f && ny <= 1.0f;
        const float cnx = nx < 0.0f ? 0.0f : (nx > 1.0f ? 1.0f : nx);
        const float cny = ny < 0.0f ? 0.0f : (ny > 1.0f ? 1.0f : ny);
        return MinimapPoint{ cnx * sizePx, cny * sizePx, onMap };
    }

    // Map a minimap drop pixel (px, py) — measured from the minimap's top-left
    // corner — back to a world position on the Z=0 plane. This is the EXACT
    // inverse of WorldToMinimap for any on-map point.
    //
    // The minimap is simply a larger drop pad, so this delegates to
    // overlay_dragdrop.h::DropPadToWorld — one coordinate map, used at two
    // sizes, can never drift between the pad and the minimap. DropPadToWorld
    // already clamps off-pad pixels, maps center->origin, treats screen-up as
    // world-north, and pins Z to 0.
    inline SpawnDrop MinimapToWorld(float px, float py,
                                    float sizePx, float halfExtent)
    {
        return DropPadToWorld(px, py, sizePx, halfExtent);
    }

    // A fixed-capacity ring of recent spawn-drop world points. The minimap
    // draws one dot per retained marker (via WorldToMinimap). Push() appends;
    // once kMinimapMarkerCapacity markers are held the OLDEST is evicted so
    // the ring always shows the most recent drops. At(0) is the oldest
    // retained marker and At(Count()-1) the newest — a stable draw order.
    //
    // This is a RAW FIFO — unlike RecentActions it does NOT dedup. Two spawns
    // dropped at the same world point are two units there, so they are two
    // distinct dots.
    //
    // Pure and heap-free (a plain array member). Render-thread-confined in
    // overlay.cpp exactly like RecentActions, so it needs no locking; the
    // unit test exercises it single-threaded.
    class SpawnMarkerRing
    {
    public:
        // Append `drop`. When the ring is already full the oldest marker is
        // evicted (the rest shift down one slot) before the new one lands.
        void Push(const SpawnDrop& drop)
        {
            if (count_ < kMinimapMarkerCapacity)
            {
                slots_[count_++] = drop;
                return;
            }
            // Full: drop slot 0, shift the rest down, append at the end. The
            // shift is O(capacity) but capacity is small and Push() fires only
            // on a human drag-drop action — clarity beats a modular index.
            for (std::size_t i = 1; i < kMinimapMarkerCapacity; ++i)
            {
                slots_[i - 1] = slots_[i];
            }
            slots_[kMinimapMarkerCapacity - 1] = drop;
        }

        // Number of markers currently retained, in [0, kMinimapMarkerCapacity].
        std::size_t Count() const { return count_; }

        // True when no markers are retained.
        bool Empty() const { return count_ == 0; }

        // Marker at index `i`, valid for i in [0, Count()). At(0) is the
        // oldest retained marker; At(Count()-1) is the newest. The caller
        // (the render loop, the test) guards with i < Count().
        const SpawnDrop& At(std::size_t i) const { return slots_[i]; }

        // Forget every marker.
        void Clear() { count_ = 0; }

    private:
        SpawnDrop   slots_[kMinimapMarkerCapacity] = {};
        std::size_t count_ = 0;
    };
}
