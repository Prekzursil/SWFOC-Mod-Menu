// =============================================================================
// swfoc_overlay/overlay_dragdrop.h — Phase 4 drag-drop tactical-spawn kernel.
//
// Phase 4 (iter 292-296) lets the operator DRAG a unit-type from the overlay's
// "Unit type" combo and DROP it on a fixed square spawn pad; the drop point is
// mapped to a world position and the unit is spawned there via the existing
// SWFOC_SpawnUnitLua wire (overlay_actions.h::BuildSpawnUnitCommand).
//
// This header is the PURE kernel of that feature — the two pieces that have a
// right and a wrong answer and so must be pinned by a unit test before the
// ImGui render glue depends on them:
//
//   1. PackUnitTypePayload() — copy the dragged unit-type name into the fixed
//      ImGui drag payload buffer without ever truncating it silently. A
//      truncated unit name sent to Find_Object_Type would spawn the wrong unit
//      (or nothing); the pack must fail loudly instead.
//   2. DropPadToWorld()      — map a pad-local drop pixel to a world (X, Y, 0)
//      coordinate. An off-by-one in the normalize, or a flipped Y axis, would
//      spawn units in the wrong place; the test pins center/edges/clamping.
//
// HONEST DEFER (overlay-interactive.md): the projection-matrix RVA is NOT
// pinned, so the overlay cannot map a true 3D screen ray to the world. Phase 4
// therefore uses a 2D interim — the spawn pad is a flat top-down tactical grid
// on the Z=0 ground plane. iter-293's minimap and iter-297's projection-matrix
// RE pass refine this; until then DropPadToWorld is a deliberate coarse map.
//
// Pure, header-only, std-only (<cstddef>/<cstring>) — no ImGui, no Windows, no
// bridge. Unit-tested with a plain g++ (build_dragdrop_test.bat).
// =============================================================================

#pragma once

#include <cstddef>
#include <cstring>

namespace swfoc_overlay
{
    // ImGui drag-drop payload type-id for a unit-type drag (Phase 4).
    //
    // ImGui matches a drag payload to a drop target by an EXACT type-id string
    // (ImGuiPayload::DataType, 32 chars max). The spawn pad accepts ANY unit
    // type, so the type-id MUST be fixed — a drop target cannot know in advance
    // which unit was dragged. The dragged unit's NAME therefore travels in the
    // payload DATA (see PackUnitTypePayload), not in the type-id.
    //
    // (The spec sketch wrote SetDragDropPayload("unit_type_<NAME>", ...); a
    // per-name type-id cannot be matched by AcceptDragDropPayload on a target
    // that accepts every unit. This fixed id is the working ImGui idiom and the
    // name is carried as data — a documented, deliberate deviation.)
    inline constexpr const char* kUnitTypePayloadId = "SWFOC_UNIT_TYPE";

    // Fixed byte size of the unit-type drag payload buffer. Holds the longest
    // current unit-type name (kActionUnitTypes in overlay.cpp:
    // "Empire_Stormtrooper_Squad", 25 chars) with generous headroom and a null
    // terminator. The drag source (SetDragDropPayload) and the drop target both
    // read exactly this many bytes, so the size is fixed here as the single
    // source of truth.
    inline constexpr std::size_t kUnitTypePayloadCapacity = 64;

    // Side length, in pixels, of the square Phase 4 spawn-pad child window.
    // Shared by the BeginChild() size and the DropPadToWorld() call so the
    // render rect and the coordinate map can never disagree.
    inline constexpr float kSpawnPadSizePx = 200.0f;

    // World half-extent the spawn pad maps to. The pad spans
    // [-kSpawnPadHalfExtent, +kSpawnPadHalfExtent] on both world axes, so its
    // full edge-to-edge span is 2x this value. Interim coarse value for the
    // Z=0 plane (see the HONEST DEFER note above) — the pad is a tactical grid,
    // not a pixel-accurate world map. iter-293's minimap refines the mapping.
    inline constexpr float kSpawnPadHalfExtent = 500.0f;

    // A spawn drop point resolved onto the Z=0 world plane.
    struct SpawnDrop
    {
        float x;
        float y;
        float z;
    };

    // Copy a null-terminated unit-type `name` into the fixed drag payload
    // buffer `buf` of capacity `cap`. Always null-terminates `buf` when cap>0
    // (it is set to the empty string up front), so a failed pack never leaves a
    // stale or partially-copied name behind.
    //
    // Returns false — without copying the name — on a null `name`, a null
    // `buf`, a zero `cap`, or a `name` too long to fit together with its null
    // terminator. The caller MUST treat false as "do not start the drag": a
    // truncated unit name would resolve to the wrong Find_Object_Type.
    // Returns true only when the whole name plus its terminator fit.
    inline bool PackUnitTypePayload(const char* name, char* buf, std::size_t cap)
    {
        if (buf == nullptr || cap == 0)
        {
            return false;
        }
        buf[0] = '\0';  // empty by default — no stale data on any failure path
        if (name == nullptr)
        {
            return false;
        }
        const std::size_t len = std::strlen(name);
        if (len + 1 > cap)  // the name AND its '\0' must fit
        {
            return false;
        }
        std::memcpy(buf, name, len + 1);
        return true;
    }

    // Map a drop pixel (px, py) — measured from the spawn pad's top-left
    // corner — to a world position on the Z=0 plane.
    //
    //   - The pad is a `padSizePx` square. Its CENTER maps to the world origin.
    //   - World X: pad left edge -> -halfExtent, pad right edge -> +halfExtent.
    //   - World Y: pad TOP edge -> +halfExtent, pad BOTTOM edge -> -halfExtent
    //     (screen-up is world-north — the SWFOC top-down tactical convention).
    //   - Z is always 0 (the interim ground plane; see the HONEST DEFER note).
    //
    // px/py outside [0, padSizePx] are CLAMPED to the pad edges, so a drop that
    // lands a pixel off the child window still resolves to an on-pad world
    // point rather than running off the grid. A non-positive padSizePx is a
    // degenerate pad and yields the world origin.
    inline SpawnDrop DropPadToWorld(float px, float py,
                                    float padSizePx, float halfExtent)
    {
        if (padSizePx <= 0.0f)
        {
            return SpawnDrop{ 0.0f, 0.0f, 0.0f };
        }
        // Clamp the drop pixel to the pad, then normalize each axis to [0, 1].
        const float cx = px < 0.0f ? 0.0f : (px > padSizePx ? padSizePx : px);
        const float cy = py < 0.0f ? 0.0f : (py > padSizePx ? padSizePx : py);
        const float nx = cx / padSizePx;  // 0 = left edge, 1 = right edge
        const float ny = cy / padSizePx;  // 0 = top edge,  1 = bottom edge
        // X: left -> -halfExtent, right -> +halfExtent.
        // Y: top  -> +halfExtent, bottom -> -halfExtent (screen-up = north).
        const float worldX = (nx * 2.0f - 1.0f) * halfExtent;
        const float worldY = (1.0f - ny * 2.0f) * halfExtent;
        return SpawnDrop{ worldX, worldY, 0.0f };
    }
}
