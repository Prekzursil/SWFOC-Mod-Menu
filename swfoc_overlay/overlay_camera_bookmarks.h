// =============================================================================
// swfoc_overlay/overlay_camera_bookmarks.h — Phase 6 camera-bookmark kernel.
//
// Phase 6 (iter 304-307) expands the overlay's hotkey surface into power-user
// features. iter-304 (spec line 62) is the "top quick win" the agent #2 UX
// research flagged: BOOKMARKABLE CAMERA POSITIONS bound to F6 / F7 / F8.
//
//   - SAVE  : press (e.g.) Shift+F6 -> the overlay reads the engine-current
//             camera position via the SWFOC_GetCameraPos LIVE wire (iter-237)
//             and stores it in bookmark slot 0.
//   - RECALL: press F6 -> the overlay jumps the camera back to the stored
//             viewpoint via the SWFOC_SetCameraPos LIVE wire (iter-237).
//
// Both wires already exist and are LIVE — iter-304 ships NO new bridge wire.
// This header is the pure kernel: a 3-slot bookmark store, the wire-result
// parser that turns SWFOC_GetCameraPos's "x,y,z" string into a stored
// position, and the recall ActionRequest builder. The overlay.cpp glue
// (deferred, build-only verifiable — exactly like every Phase 5 kernel iter)
// only polls the F-keys and hands BuildRecall()'s ActionRequest to the
// iter-513 ActionQueue; the deciding logic is here so a unit test pins it
// before the input path depends on it.
//
// WIRE FACTS (swfoc_lua_bridge/lua_bridge.cpp, re-read 2026-05-21)
// ---------------------------------------------------------------
//   SWFOC_SetCameraPos(x, y, z)  -> 3 plain Lua NUMBER args. Unlike the
//       SWFOC_*Lua write wires there is NO nested-expression quoting — the
//       bridge reads the args with fn_tonumber. (registered lua_bridge.cpp
//       :8244; Lua_SetCameraPos :3893)
//   SWFOC_GetCameraPos()         -> no args; returns the engine-current
//       camera position as a 3-decimal "x.xxx,y.yyy,z.zzz" string. When no
//       tactical camera is active it returns the literal "0.000,0.000,0.000"
//       (NOT an ERR: string). (registered :8245; Lua_GetCameraPos :6011)
//
// ParseCameraPos cannot tell "camera genuinely at the origin" from "no
// active camera" — both wire-read as "0.000,0.000,0.000". That is the
// bridge's ambiguity, not the kernel's: a bookmark saved as (0,0,0) is a
// valid bookmark at the origin. The deferred render glue may choose to warn
// when it saves an exact-origin bookmark; the kernel stays honest and stores
// what the wire gave it.
//
// RED-GREEN REGRESSION PINS (overlay_camera_bookmarks_test.cpp)
// ------------------------------------------------------------
//   - SAVE THEN RECALL ROUND-TRIPS : Save(slot, p) then BuildRecall(slot)
//                                    emits SWFOC_SetCameraPos(p) — a store
//                                    that drops the position fails.
//   - EMPTY SLOT YIELDS EMPTY LUA  : BuildRecall on an UNSET slot yields an
//                                    empty `lua` + an "empty" label — an old
//                                    form that fires SetCameraPos(0,0,0) for
//                                    an unset key is an accidental camera
//                                    jump and fails.
//   - PARSE REJECTS MALFORMED WIRE : ParseCameraPos rejects anything that is
//                                    not exactly three numeric fields — a
//                                    partial-parse old form fails.
//   - MALFORMED WIRE DOESN'T CLOBBER: SaveFromWire with a bad wire string
//                                    leaves an already-good slot untouched —
//                                    an old form that wipes the slot fails.
//   - SLOTS ARE INDEPENDENT        : the three slots store distinct
//                                    positions — a single-shared-slot old
//                                    form fails.
//   - HOTKEY LABELS THE F-KEY      : the recall label names F6 / F7 / F8 for
//                                    slots 0 / 1 / 2.
//   - PLAIN NUMBER ARGS            : BuildSetCameraPosCommand emits bare
//                                    number literals — a copy-from-SWFOC_*Lua
//                                    old form that LuaQuotes the coords fails.
//
// THREADING: CameraBookmarks is touched ONLY by the render / input thread —
// Save() / SaveFromWire() at the hotkey site, BuildRecall() / Get() while
// drawing the toolbar. The background action worker drains the ActionQueue
// but never touches this store. Render-thread-confined, so — like
// overlay_recent_actions.h's RecentActions — it needs no mutex.
//
// Pure, header-only, std-only. Reuses FormatCoord from overlay_actions.h and
// ActionRequest from overlay_action_queue.h — no new type, no ImGui, no
// Windows, no bridge. Unit-tested with a plain g++
// (build_camera_bookmarks_test.bat).
// =============================================================================

#pragma once

#include "overlay_actions.h"       // FormatCoord
#include "overlay_action_queue.h"  // ActionRequest

#include <cerrno>
#include <cmath>
#include <cstddef>
#include <cstdlib>
#include <string>

namespace swfoc_overlay
{
    // Number of camera bookmark slots — three, bound to F6 / F7 / F8 per
    // overlay-interactive.md line 30 / line 62.
    inline constexpr std::size_t kCameraBookmarkSlots = 3;

    // The function-key number bound to bookmark slot `slot`:
    // slot 0 -> 6 (F6), slot 1 -> 7 (F7), slot 2 -> 8 (F8). An out-of-range
    // slot yields 0 so a label can never read a garbage key — callers index
    // by a validated slot, but a defensive 0 beats undefined behaviour.
    inline int CameraBookmarkHotkey(std::size_t slot)
    {
        switch (slot)
        {
            case 0:  return 6;
            case 1:  return 7;
            case 2:  return 8;
            default: return 0;
        }
    }

    // A single stored camera viewpoint. `set` is false until a position is
    // recorded; the (x, y, z) are the engine world-space camera translation.
    struct CameraBookmark
    {
        bool  set = false;
        float x   = 0.0f;
        float y   = 0.0f;
        float z   = 0.0f;
    };

    // The canonical empty bookmark — returned by CameraBookmarks::Get for an
    // out-of-range slot so a render bug reads clean zeros, never garbage.
    inline const CameraBookmark kEmptyCameraBookmark{};

    // Build the Lua line that jumps the active tactical camera to (x, y, z)
    // via the SWFOC_SetCameraPos LIVE wire (iter-237). SetCameraPos takes
    // three plain Lua NUMBER arguments — there is NO nested-expression
    // quoting (contrast the SWFOC_*Lua write wires in overlay_actions.h).
    // Coordinates are formatted by overlay_actions.h::FormatCoord so a whole
    // number renders "0" not "0.000" and the preview stays readable.
    inline std::string BuildSetCameraPosCommand(float x, float y, float z)
    {
        return "return SWFOC_SetCameraPos(" + FormatCoord(x) + ", " +
               FormatCoord(y) + ", " + FormatCoord(z) + ")";
    }

    // Parse one comma-delimited field of a SWFOC_GetCameraPos wire result
    // into a float. Returns false (and leaves `out` untouched) unless the
    // ENTIRE token is a single finite number — a token with trailing junk
    // ("12abc"), an empty token, or inf/nan is rejected. strtod returns
    // +/-HUGE_VAL on overflow, which the isfinite check catches.
    inline bool ParseCameraPosField(const std::string& token, float& out)
    {
        if (token.empty()) return false;
        const char* begin = token.c_str();
        char* end = nullptr;
        const double value = std::strtod(begin, &end);
        // The whole token must be consumed: end short of the terminator means
        // trailing junk; end == begin means nothing numeric was parsed.
        if (end != begin + token.size()) return false;
        if (!std::isfinite(value)) return false;
        out = static_cast<float>(value);
        return true;
    }

    // Parse a SWFOC_GetCameraPos() wire result ("x.xxx,y.yyy,z.zzz") into a
    // CameraBookmark. The result has `set == true` only when the string is
    // EXACTLY three comma-separated finite numbers — any other shape (too few
    // / too many fields, a non-numeric field, an empty string, an ERR:
    // payload) yields an unset bookmark so a caller never stores junk.
    inline CameraBookmark ParseCameraPos(const std::string& wireResult)
    {
        CameraBookmark bm;  // set == false by default.

        // Exactly two commas == exactly three fields.
        std::size_t commaCount = 0;
        for (const char c : wireResult)
        {
            if (c == ',') ++commaCount;
        }
        if (commaCount != 2) return bm;

        // Split into the three fields.
        std::string fields[3];
        std::size_t fieldIdx = 0;
        for (const char c : wireResult)
        {
            if (c == ',') ++fieldIdx;
            else          fields[fieldIdx].push_back(c);
        }

        float xyz[3] = { 0.0f, 0.0f, 0.0f };
        for (std::size_t i = 0; i < 3; ++i)
        {
            if (!ParseCameraPosField(fields[i], xyz[i])) return bm;
        }

        bm.set = true;
        bm.x = xyz[0];
        bm.y = xyz[1];
        bm.z = xyz[2];
        return bm;
    }

    // The Phase 6 camera-bookmark store: three slots (F6 / F7 / F8), each
    // holding one CameraBookmark. See the file header for the full SAVE /
    // RECALL contract and the render-thread-confined threading note.
    class CameraBookmarks
    {
    public:
        // Slot count — three, exposed for the render loop's slot iteration.
        static constexpr std::size_t kSlots = kCameraBookmarkSlots;

        // Save an explicit camera position into slot `slot`. An out-of-range
        // slot is a no-op (defensive — callers index by a validated F-key).
        void Save(std::size_t slot, float x, float y, float z)
        {
            if (slot >= kSlots) return;
            slots_[slot].set = true;
            slots_[slot].x   = x;
            slots_[slot].y   = y;
            slots_[slot].z   = z;
        }

        // Save by parsing a SWFOC_GetCameraPos() wire result. Returns true
        // when the wire string was a well-formed "x,y,z" triple AND the slot
        // is valid; otherwise returns false and leaves the slot UNTOUCHED —
        // so a malformed or stale wire read can never clobber a good
        // bookmark (the MALFORMED WIRE DOESN'T CLOBBER pin).
        bool SaveFromWire(std::size_t slot, const std::string& wireResult)
        {
            if (slot >= kSlots) return false;
            const CameraBookmark parsed = ParseCameraPos(wireResult);
            if (!parsed.set) return false;
            slots_[slot] = parsed;
            return true;
        }

        // True when slot `slot` holds a saved position (and is in range).
        bool IsSet(std::size_t slot) const
        {
            return slot < kSlots && slots_[slot].set;
        }

        // The bookmark in slot `slot`. An out-of-range slot yields the shared
        // empty bookmark so a render bug reads zeros, not garbage.
        const CameraBookmark& Get(std::size_t slot) const
        {
            return slot < kSlots ? slots_[slot] : kEmptyCameraBookmark;
        }

        // Forget the bookmark in slot `slot` (an operator "clear" control).
        // Out-of-range slot is a no-op.
        void Clear(std::size_t slot)
        {
            if (slot < kSlots) slots_[slot] = CameraBookmark{};
        }

        // Build the recall ActionRequest for slot `slot`: a dispatch-ready
        // SWFOC_SetCameraPos line that jumps the camera to the stored
        // viewpoint, plus a label naming the F-key and the destination.
        //
        // An UNSET slot yields an ActionRequest with an EMPTY `lua` and an
        // "<Fn> camera bookmark empty" label — the render glue MUST check
        // IsSet() (or req.lua.empty()) before enqueueing, so an F-key bound
        // to an unset slot toasts "empty" instead of firing a destructive
        // jump to the origin. Pinning the empty-lua contract here keeps that
        // decision testable (the EMPTY SLOT YIELDS EMPTY LUA pin).
        ActionRequest BuildRecall(std::size_t slot) const
        {
            ActionRequest req;
            const std::string fkey =
                std::string("F") +
                static_cast<char>('0' + CameraBookmarkHotkey(slot));

            if (!IsSet(slot))
            {
                req.label = fkey + " camera bookmark empty";
                req.lua.clear();
                return req;
            }

            const CameraBookmark& bm = slots_[slot];
            req.label = std::string("Recall camera ") + fkey + " (" +
                        FormatCoord(bm.x) + ", " + FormatCoord(bm.y) + ", " +
                        FormatCoord(bm.z) + ")";
            req.lua = BuildSetCameraPosCommand(bm.x, bm.y, bm.z);
            return req;
        }

    private:
        // Slot 0 -> F6, slot 1 -> F7, slot 2 -> F8.
        CameraBookmark slots_[kSlots];
    };
}
