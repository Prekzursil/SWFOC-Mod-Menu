// =============================================================================
// swfoc_overlay/hud_state.cpp — Phase 2 HUD model + bridge-poll worker.
//
// The worker pattern is deliberately simple:
//   while (!shutdown):
//     snap = build_snapshot_via_bridge()
//     atomic_swap(snap_ptr, snap)
//     sleep(refresh_ms)
//
// We use a heap-allocated HudSnapshot and an std::atomic<HudSnapshot*>
// for the swap. Reader copies the pointed-to value while holding a
// mutex (HudSnapshot has std::string members so atomic struct-swap
// isn't viable). The mutex is held only for the copy duration —
// microseconds on the render thread.
//
// The bridge probe is named-pipe-based: open \\.\pipe\swfoc_bridge,
// send a Lua line, read a response. Phase 2 ships with the connection
// + send-receive but the actual Lua queries that build the snapshot
// are stubbed; a future iter (or live testing) wires them up.
// =============================================================================

#include "hud_state.h"

#include <windows.h>

#include <atomic>
#include <memory>
#include <mutex>
#include <string>
#include <thread>

namespace
{
    // Bridge pipe name shared with powrprof.dll's named-pipe server.
    constexpr const char* kBridgePipeName = R"(\\.\pipe\swfoc_bridge)";

    // Refresh cadence — operator-perceptible at ~2 Hz, doesn't bombard
    // the bridge during high-frequency game state.
    constexpr DWORD kRefreshIntervalMs = 500;

    // Per-call timeout — generous because the bridge is in-process
    // (powrprof.dll is loaded by StarWarsG.exe along with us).
    constexpr DWORD kProbeTimeoutMs = 250;

    // ---- Snapshot storage ----------------------------------------------------
    // std::shared_ptr swapped under a mutex. Render side copies the
    // shared_ptr (refcount bump only) then dereferences without holding
    // the mutex.
    std::mutex g_snap_mutex;
    std::shared_ptr<swfoc_overlay::HudSnapshot> g_current_snap;

    // ---- Worker thread -------------------------------------------------------
    std::thread g_worker;
    std::atomic<bool> g_shutdown{false};

    // 2026-05-08 (iter 284): Tier 3 — session-start tick captured at
    // first-successful-bridge-probe. Used to compute
    // session_elapsed_seconds without a bridge wire (the engine has no
    // SWFOC_GetMissionElapsedMs equivalent — iter-285 honest-defer).
    // 0 means "not yet seeded"; a non-zero seed survives until DLL
    // unload, so reattach without reload preserves the session clock.
    std::atomic<uint64_t> g_session_start_tick{0};

    void PublishSnapshot(swfoc_overlay::HudSnapshot snap)
    {
        snap.generated_tick = GetTickCount64();
        auto sp = std::make_shared<swfoc_overlay::HudSnapshot>(std::move(snap));
        std::lock_guard<std::mutex> lg(g_snap_mutex);
        g_current_snap = std::move(sp);
    }

    // Bridge probe — round-trip a single Lua line. The DEFINITION now lives in
    // `namespace swfoc_overlay` below (declared in hud_state.h) so Phase 3's
    // action worker (overlay_action_worker.cpp) reuses the exact same blocking
    // pipe round-trip as its write-command BridgeSendFn — one implementation,
    // no duplicated pipe code. This using-declaration keeps BuildSnapshot's
    // ~10 call sites unqualified and untouched.
    using swfoc_overlay::BridgeProbe;

    swfoc_overlay::HudSnapshot BuildSnapshot()
    {
        swfoc_overlay::HudSnapshot snap;

        std::string resp;
        // 1) Reachability + local player slot via SWFOC_GetLocalPlayer.
        if (BridgeProbe("return SWFOC_GetLocalPlayer()", resp))
        {
            snap.bridge_reachable = true;
            // Response is integer slot number; parse permissively.
            try { snap.local_player_slot = std::stoi(resp); }
            catch (...) { /* leave as -1 */ }
        }
        else
        {
            snap.bridge_reachable = false;
            snap.last_error = resp;
            return snap;  // Skip remaining probes if pipe is dead.
        }

        // 2) Credits — SWFOC_GetCredits(slot).
        if (snap.local_player_slot >= 0)
        {
            char lua[64];
            std::snprintf(lua, sizeof(lua),
                "return SWFOC_GetCredits(%d)", snap.local_player_slot);
            if (BridgeProbe(lua, resp))
            {
                try { snap.credits = std::stoll(resp); }
                catch (...) { /* leave as -1 */ }
            }
        }

        // 3) Alive units — SWFOC_CountUnits(slot) (placeholder helper name;
        //    Phase 2 wiring will replace with the real catalog name).
        if (snap.local_player_slot >= 0)
        {
            char lua[64];
            std::snprintf(lua, sizeof(lua),
                "return SWFOC_CountUnits(%d)", snap.local_player_slot);
            if (BridgeProbe(lua, resp))
            {
                try { snap.alive_units = std::stoi(resp); }
                catch (...) { /* leave as -1 */ }
            }
        }

        // 4) Scene name — SWFOC_GetCurrentScene() returns either planet
        //    name (galactic) or map name (tactical).
        if (BridgeProbe("return SWFOC_GetCurrentScene()", resp))
        {
            // Strip surrounding quotes if the bridge returned a quoted
            // string literal.
            if (resp.size() >= 2 && resp.front() == '"' && resp.back() == '"')
            {
                snap.scene_name = resp.substr(1, resp.size() - 2);
            }
            else
            {
                snap.scene_name = resp;
            }
        }

        // 5) 2026-05-08 (iter 281): Tier 2 damage-multiplier probe via
        //    iter-96 SWFOC_GetDamageMultiplierGlobal (LIVE getter pair).
        //    Resolves iter-279 honest-defer for the HUD's damage row.
        //    Bridge returns a stringified float (e.g. "2.0" or "1.0"); on
        //    parse failure the field stays at -1.0f sentinel and the
        //    render side falls back to TextDisabled placeholder.
        if (BridgeProbe("return SWFOC_GetDamageMultiplierGlobal()", resp))
        {
            try { snap.damage_mult = std::stof(resp); }
            catch (...) { /* leave at -1.0f sentinel */ }
        }

        // 6) 2026-05-08 (iter 282): Tier 2 fire-rate-multiplier probe via
        //    SWFOC_GetFireRateMultiplierGlobal — DISCOVERED to already be
        //    LIVE in the bridge (lua_bridge.cpp:6794, registered in the
        //    Lua table at line 7616). iter-281's honest-defer was based on
        //    incomplete investigation — it checked iter-225's setter doc
        //    but didn't grep for `Lua_GetFireRateMultiplierGlobal` in the
        //    bridge, which would have shown the getter was already there.
        //    iter-282 resolves the second Tier 2 honest-defer entirely by
        //    just adding this probe call (no bridge work needed).
        if (BridgeProbe("return SWFOC_GetFireRateMultiplierGlobal()", resp))
        {
            try { snap.firerate_mult = std::stof(resp); }
            catch (...) { /* leave at -1.0f sentinel */ }
        }

        // 7) 2026-05-08 (iter 284): Tier 3 — session elapsed seconds.
        //    Local clock; no bridge wire needed. Seeded at first
        //    successful bridge probe (iter-281 step #1 sets
        //    bridge_reachable=true). Persistent across snapshot
        //    rotations — survives until DLL unload.
        if (snap.bridge_reachable)
        {
            uint64_t expected = 0;
            const uint64_t now = GetTickCount64();
            // CAS to seed only once.
            g_session_start_tick.compare_exchange_strong(expected, now);
            const uint64_t seed = g_session_start_tick.load(std::memory_order_relaxed);
            if (seed > 0 && now >= seed)
            {
                snap.session_elapsed_seconds = (now - seed) / 1000;
            }
        }

        // 8) 2026-05-08 (iter 285): Tier 3 HUD counters NOW LIVE.
        //    iter-284 honest-deferred kill/death/units-alive; iter-285
        //    closed the defer by extending Hook_DeathHandler in the
        //    bridge to maintain std::atomic<int> counters + adding a
        //    SWFOC_GetTotalUnitsAlive Lua getter that walks
        //    Selection::kObjectListHead. This worker just probes the 3
        //    new wires; parse failures fall back to -1 sentinel which
        //    the render side displays as "—" or "n/a".
        if (BridgeProbe("return SWFOC_GetPlayerKills()", resp))
        {
            try { snap.local_kills = std::stoi(resp); }
            catch (...) { /* leave at -1 sentinel */ }
        }
        if (BridgeProbe("return SWFOC_GetPlayerDeaths()", resp))
        {
            try { snap.local_deaths = std::stoi(resp); }
            catch (...) { /* leave at -1 sentinel */ }
        }
        if (BridgeProbe("return SWFOC_GetTotalUnitsAlive()", resp))
        {
            try { snap.total_units_in_play = std::stoi(resp); }
            catch (...) { /* leave at -1 sentinel */ }
        }

        // 9) 2026-05-21 (iter 302): Phase 5 unit-AABB set — HONEST DEFER.
        //    snap.unit_aabbs default-constructs EMPTY (count == 0) and the
        //    worker leaves it so — there is deliberately no probe here. The
        //    bridge exposes no per-unit world-AABB read wire: SWFOC_EnumerateUnits
        //    returns handles only, and no SWFOC_GetUnitAabb-class getter exists
        //    in lua_bridge.cpp (see the HudSnapshot::unit_aabbs comment in
        //    hud_state.h). The iter 298-302 client-side-raycast kernel chain
        //    (overlay_cursor_ray.h / overlay_hit_test.h / overlay_unit_aabb.h)
        //    is built and test-pinned; this step starts appending the moment a
        //    SWFOC_GetUnitAabb-class wire lands. An empty set is a safe
        //    clean-miss for the raycast — never a phantom inspector hit.

        return snap;
    }

    void WorkerLoop()
    {
        while (!g_shutdown.load(std::memory_order_relaxed))
        {
            // Build + publish.
            PublishSnapshot(BuildSnapshot());
            // Sleep in 50 ms slices so shutdown is responsive.
            for (int i = 0; i < kRefreshIntervalMs / 50
                && !g_shutdown.load(std::memory_order_relaxed); ++i)
            {
                Sleep(50);
            }
        }
    }
}

namespace swfoc_overlay
{
    // Bridge primitive — round-trip a single Lua line through the
    // \\.\pipe\swfoc_bridge named pipe. Shared by the HUD read-probe worker
    // (BuildSnapshot, above) and the Phase 3 action worker
    // (overlay_action_worker.cpp). BLOCKING: synchronous
    // CreateFile / WriteFile / ReadFile — must run on a background worker
    // thread, never the D3D9 render thread. Declared in hud_state.h.
    bool BridgeProbe(const std::string& lua, std::string& response)
    {
        response.clear();
        HANDLE pipe = CreateFileA(
            kBridgePipeName,
            GENERIC_READ | GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (pipe == INVALID_HANDLE_VALUE)
        {
            response = "(pipe open failed)";
            return false;
        }
        // Make sure we don't sit on a stalled pipe forever.
        DWORD mode = PIPE_READMODE_BYTE;
        SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr);

        const std::string line = lua + "\n";
        DWORD written = 0;
        if (!WriteFile(pipe, line.data(),
                static_cast<DWORD>(line.size()), &written, nullptr))
        {
            CloseHandle(pipe);
            response = "(pipe write failed)";
            return false;
        }
        // Best-effort read with a small buffer.
        char buf[1024];
        DWORD readBytes = 0;
        const BOOL ok = ReadFile(pipe, buf, sizeof(buf) - 1, &readBytes, nullptr);
        CloseHandle(pipe);
        if (!ok)
        {
            response = "(pipe read failed)";
            return false;
        }
        buf[readBytes] = '\0';
        response.assign(buf, readBytes);
        // Strip trailing newline if present.
        while (!response.empty()
            && (response.back() == '\n' || response.back() == '\r'))
        {
            response.pop_back();
        }
        return true;
    }

    void StartHudWorker()
    {
        if (g_worker.joinable()) return;  // Idempotent.
        g_shutdown.store(false);
        g_worker = std::thread(WorkerLoop);
    }

    void StopHudWorker()
    {
        g_shutdown.store(true);
        if (g_worker.joinable()) g_worker.join();
    }

    HudSnapshot GetHudSnapshot()
    {
        std::shared_ptr<HudSnapshot> sp;
        {
            std::lock_guard<std::mutex> lg(g_snap_mutex);
            sp = g_current_snap;
        }
        return sp ? *sp : HudSnapshot{};
    }

    void SetHudSnapshotForTest(const HudSnapshot& snap)
    {
        auto sp = std::make_shared<HudSnapshot>(snap);
        std::lock_guard<std::mutex> lg(g_snap_mutex);
        g_current_snap = std::move(sp);
    }
}
