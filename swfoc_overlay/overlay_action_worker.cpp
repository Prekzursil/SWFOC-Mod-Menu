// =============================================================================
// swfoc_overlay/overlay_action_worker.cpp — Phase 3 action-worker lifecycle.
//
// overlay_action_worker.h ships two pure, unit-tested pieces: the ActionQueue
// type (overlay_action_queue.h) and RunActionWorkerLoop(), the drain loop a
// background thread runs. This file is the DLL-side glue that the header-only
// unit test deliberately cannot exercise — it spawns the real thread, binds
// the real blocking bridge send, and owns the one process-wide queue:
//
//   ActionQueueInstance() — the single ActionQueue. The render thread enqueues
//       onto it (Phase 3 button onClick, next overlay iter) and reads
//       LatestResult() once per frame; the worker thread drains it.
//   StartActionWorker()   — spawn the background std::thread running
//       RunActionWorkerLoop() with the real BridgeProbe send (hud_state.h),
//       a sliced Sleep, and an std::atomic<bool> stop signal. Idempotent.
//   StopActionWorker()    — set the stop signal and join the thread.
//       Idempotent.
//
// Install() calls StartActionWorker() after StartHudWorker(); Uninstall()
// calls StopActionWorker() before the D3D9 hooks come down, so an in-flight
// bridge round-trip drains cleanly. The shape mirrors hud_state.cpp's
// StartHudWorker / StopHudWorker exactly: idempotent joinable() guard, sliced
// Sleep so a shutdown requested mid-pause is honoured within one slice.
//
// Why a worker thread at all: BridgeProbe does blocking named-pipe I/O. The
// Phase 3 buttons must never run it on the render thread (inside the D3D9
// Present detour) or a slow bridge would freeze the host game's frame loop.
// See overlay_action_queue.h for the full rationale.
// =============================================================================

#include "overlay_action_worker.h"

#include "hud_state.h"  // swfoc_overlay::BridgeProbe — the real blocking send.

#include <windows.h>    // Sleep

#include <atomic>
#include <string>
#include <thread>

namespace
{
    // The single process-wide action queue. A function-local static so its
    // construction is thread-safe (C++11 magic statics) and it outlives any
    // StartActionWorker / StopActionWorker restart cycle.
    swfoc_overlay::ActionQueue& Queue()
    {
        static swfoc_overlay::ActionQueue instance;
        return instance;
    }

    // The single background drain thread + its stop signal.
    std::thread g_actionWorker;
    std::atomic<bool> g_actionShutdown{false};

    // Inter-drain pause. Sliced into 50 ms chunks (mirrors hud_state.cpp's
    // WorkerLoop) so a shutdown requested mid-pause is honoured within one
    // slice instead of after a full interval.
    constexpr int kActionDrainIntervalMs = 200;
    constexpr int kActionSleepSliceMs = 50;

    void SlicedSleep()
    {
        for (int i = 0;
             i < kActionDrainIntervalMs / kActionSleepSliceMs
                 && !g_actionShutdown.load(std::memory_order_relaxed);
             ++i)
        {
            Sleep(kActionSleepSliceMs);
        }
    }
}

namespace swfoc_overlay
{
    ActionQueue& ActionQueueInstance()
    {
        return Queue();
    }

    void StartActionWorker()
    {
        if (g_actionWorker.joinable()) return;  // Idempotent.
        g_actionShutdown.store(false);
        g_actionWorker = std::thread([]
        {
            RunActionWorkerLoop(
                Queue(),
                // Real bridge send: a blocking named-pipe round-trip. Runs on
                // THIS worker thread, never the render thread.
                [](const std::string& lua, std::string& response)
                {
                    return BridgeProbe(lua, response);
                },
                // Stop signal: the worker exits when StopActionWorker() sets it.
                []
                {
                    return g_actionShutdown.load(std::memory_order_relaxed);
                },
                // Inter-drain pause, shutdown-responsive.
                SlicedSleep);
        });
    }

    void StopActionWorker()
    {
        g_actionShutdown.store(true);
        if (g_actionWorker.joinable()) g_actionWorker.join();
    }
}
