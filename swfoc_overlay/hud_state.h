// =============================================================================
// swfoc_overlay/hud_state.h — Phase 2 read-only HUD model.
//
// The overlay's render path reads a snapshot of HudState every frame; a
// background worker polls the existing powrprof.dll bridge pipe (the
// editor's ground truth) and refreshes HudState atomically. Render and
// poll never block each other — the worker swaps a pre-built snapshot
// into an std::atomic<HudSnapshot> pointer once per refresh tick.
//
// Phase 2 fields are intentionally narrow: credits, unit count, current
// planet/map name, and bridge-reachable flag. Phases 3-5 will extend
// this struct as new RE pins land.
// =============================================================================

#pragma once

#include "overlay_unit_aabb.h"  // UnitAabbSet — the Phase 5 unit-AABB section

#include <atomic>
#include <cstdint>
#include <string>

namespace swfoc_overlay
{
    // ---- Pure data snapshot --------------------------------------------------
    // Lock-free swap target. Worker constructs a fresh HudSnapshot every
    // refresh tick and atomically rotates it under the read pointer.
    struct HudSnapshot
    {
        // True when the worker successfully round-tripped a probe through
        // the bridge pipe within the last refresh window.
        bool bridge_reachable = false;

        // Local-player slot (0-7 in SWFOC). -1 when unknown / no game.
        int local_player_slot = -1;

        // Local-player credits. Negative = unknown.
        int64_t credits = -1;

        // Tactical-mode alive-unit count for the local player. -1 when
        // not in tactical mode or unknown.
        int alive_units = -1;

        // Current planet name (galactic) or map name (tactical). Empty
        // when unknown. UTF-8.
        std::string scene_name;

        // Last-error line from the most recent probe failure. Empty when
        // the last probe succeeded. UTF-8.
        std::string last_error;

        // 2026-05-08 (iter 281): Tier 2 multiplier values; resolves
        // iter-279 honest-defer for the in-game HUD's damage / fire-rate
        // rows. Worker probes via iter-96 SWFOC_GetDamageMultiplierGlobal
        // (LIVE getter pair to iter-96 SWFOC_SetDamageMultiplierGlobal
        // Take_Damage_Outer detour). Sentinel -1.0f means "not yet probed
        // or probe failed"; render side shows TextDisabled placeholder
        // in that case. firerate_mult stays -1.0f because iter-225
        // SetFireRateMultiplierGlobal does NOT have a paired getter yet
        // — see iter-281 close-out + iter-282 follow-up plan. Append-only
        // field additions preserve the iter-275 design's binary layout
        // stability across phases.
        float damage_mult = -1.0f;
        float firerate_mult = -1.0f;

        // 2026-05-08 (iter 284): Tier 3 state-rate counters.
        // session_elapsed_seconds is purely local — worker tracks the
        // tick at first-successful-bridge-probe and subtracts current
        // tick. NOT mission-time (engine has no SWFOC_GetMissionElapsedMs
        // wire yet — iter-285 candidate). Useful as a "how long has the
        // overlay been attached" cue for stream operators.
        // local_kills + local_deaths + total_units_in_play stay -1 sentinel
        // until iter-285 ships SWFOC_GetPlayerKills/Deaths/UnitsAlive
        // bridge wires (HONEST DEFER per iter-284 grep — none exist in
        // lua_bridge.cpp; only SWFOC_KillUnit which is a write-side
        // mutation, not a counter read).
        uint64_t session_elapsed_seconds = 0;
        int local_kills = -1;
        int local_deaths = -1;
        int total_units_in_play = -1;

        // Frame-relative timestamp of the snapshot: tick count from
        // GetTickCount64() when the worker built it. The render side
        // shows "x sec ago" using delta against current GetTickCount64.
        uint64_t generated_tick = 0;

        // 2026-05-21 (iter 302): Phase 5 client-side-raycast unit-AABB set.
        // overlay_hit_test.h (iter-299) walks this list to resolve which unit
        // the operator clicked; overlay_cursor_ray.h (iter-298) turns the
        // cursor pixel into the pick ray. Capped at kMaxRaycastUnits (64)
        // visible units — the raycast budget. Appended to the struct tail so
        // the addition is binary-layout append-only, preserving the iter-275
        // stability commitment exactly as the iter-281 / iter-284 fields above
        // did — old readers walking the leading fields are untouched.
        //
        // HONEST DEFER: the worker leaves this set EMPTY (count == 0). The
        // bridge exposes no per-unit world-AABB read wire — SWFOC_EnumerateUnits
        // (LIVE iter-104) returns handles, not bounding boxes, and no
        // SWFOC_GetUnitAabb-class getter exists in lua_bridge.cpp. Populating
        // it needs either a new bridge wire reading each GameObject's
        // CollisionClass bounds or the engine's selection-raycast RVA —
        // overlay-interactive.md honest-defer #2. The iter 298-302 client-side
        // raycast kernel chain is fully built and unit-pinned; it goes live the
        // moment that wire lands. An empty set makes PickUnitInSet /
        // NearestUnitHit a clean miss — never a phantom inspector hit.
        UnitAabbSet unit_aabbs;
    };

    // ---- Phase 2 worker control ----------------------------------------------
    // Idempotent. Spawns the bridge-poll worker if not already running.
    // Safe to call from Install() — uses its own thread, doesn't block
    // the D3D9 detour.
    void StartHudWorker();

    // Signals the worker to exit and joins. Called from Uninstall().
    void StopHudWorker();

    // ---- Render-side accessors -----------------------------------------------
    // Returns a copy of the most recent snapshot. Render path calls
    // this once per Present detour. Returns a default-constructed
    // snapshot when the worker hasn't produced one yet.
    HudSnapshot GetHudSnapshot();

    // Test-only: synthesize a snapshot directly. Used by the harness so
    // we can verify the render path produces sensible output without
    // running the full bridge worker thread.
    void SetHudSnapshotForTest(const HudSnapshot& snap);

    // ---- Bridge primitive (shared) -------------------------------------------
    // Round-trip a single Lua line through the powrprof.dll bridge pipe
    // (\\.\pipe\swfoc_bridge): open, write `lua` + '\n', read the response.
    // Returns true and fills `response` on success; returns false and writes a
    // parenthesised failure reason into `response` on any pipe error.
    //
    // This is the read-probe primitive the HUD worker uses to build snapshots
    // (BuildSnapshot, hud_state.cpp). Phase 3's action worker
    // (overlay_action_worker.cpp) reuses it as the BridgeSendFn for write
    // commands — both are the same blocking named-pipe round-trip, so they
    // share one implementation rather than duplicating the pipe code.
    //
    // BLOCKING: performs synchronous CreateFile / WriteFile / ReadFile. Must be
    // called only from a background worker thread, never the D3D9 render thread
    // (that is the entire reason the action queue exists).
    bool BridgeProbe(const std::string& lua, std::string& response);
}
