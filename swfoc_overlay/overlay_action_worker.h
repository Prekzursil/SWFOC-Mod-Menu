// =============================================================================
// swfoc_overlay/overlay_action_worker.h — Phase 3 action-worker drain loop.
//
// Phase 3's interactive widgets enqueue bridge calls into the ActionQueue
// (overlay_action_queue.h). Something must DRAIN that queue off the render
// thread — the render thread runs inside the D3D9 Present detour and must
// never block on the bridge's named-pipe I/O. That something is a single
// background worker thread whose body is RunActionWorkerLoop().
//
// The loop body is a pure, dependency-free function — no <windows.h>, no
// <thread>, no bridge. The two ingredients a real worker needs but a test
// cannot supply (a blocking bridge send, a real Sleep) and the one ingredient
// the test must control (the shutdown signal) are all INJECTED as callables.
// That keeps the loop's two subtle behaviours unit-testable with a plain g++:
//
//   1. CHECK-FIRST — shouldStop() is consulted BEFORE the first Drain(), so a
//      worker asked to stop before it ever ticks issues zero bridge I/O. A
//      drain-then-check (do/while) loop would fire one last bridge call into a
//      possibly-dead pipe during DLL teardown.
//   2. NO-SLEEP-ON-STOP — after a drain the loop re-checks shouldStop() before
//      sleeping, so a shutdown requested mid-tick is honoured immediately
//      instead of after a full sleep interval.
//
// See overlay_action_worker_test.cpp (build_action_worker_test.bat) for the
// red-green pins on both behaviours.
//
// The DLL-side lifecycle that drives this loop lives in
// overlay_action_worker.cpp (added iter 516): ActionQueueInstance() exposes the
// one process-wide ActionQueue, and StartActionWorker() / StopActionWorker()
// spawn / join the single background std::thread that runs RunActionWorkerLoop
// with the real BridgeProbe send (hud_state.h), a sliced Sleep, and the
// shutdown atomic. overlay.cpp's Install() / Uninstall() own that lifecycle.
// Phase 3's RenderActionsWindow wires the buttons to ActionQueueInstance()
// next overlay iter. See knowledge-base/overlay_phase3_actionworker_iter515.md
// (the loop) + knowledge-base/overlay_phase3_actionworker_lifecycle_iter516.md
// (the lifecycle).
// =============================================================================

#pragma once

#include <functional>

#include "overlay_action_queue.h"

namespace swfoc_overlay
{
    // Predicate the loop polls to learn when to stop. The real worker binds
    // this to an std::atomic<bool> load; the test binds a deterministic
    // call-count fake.
    using ShouldStopFn = std::function<bool()>;

    // Inter-drain pause. The real worker binds this to a sliced Sleep (so a
    // shutdown requested during the pause stays responsive); the test binds a
    // no-op counter so the loop runs instantly.
    using WorkerSleepFn = std::function<void()>;

    // Drain `queue` through `send` repeatedly until `shouldStop()` is true,
    // pausing via `sleep()` between drains. Pure: every external effect is one
    // of the four injected callables, so the whole loop runs in a unit test.
    //
    // Ordering contract (both pinned by overlay_action_worker_test.cpp):
    //   while shouldStop() is false:
    //       Drain(send)              -- process every pending request, FIFO
    //       if shouldStop():  break  -- stop now; do NOT sleep
    //       sleep()
    // shouldStop() is therefore checked before the first Drain (CHECK-FIRST)
    // and again before every sleep (NO-SLEEP-ON-STOP).
    //
    // Defensive on the injected callables:
    //   - empty shouldStop -> the loop refuses to run at all (a missing stop
    //     signal would otherwise spin forever); it returns immediately.
    //   - empty send       -> ActionQueue::Drain already marks each request
    //     Failed with "(no send function)" — the loop just keeps ticking.
    //   - empty sleep      -> the inter-drain pause is skipped.
    inline void RunActionWorkerLoop(ActionQueue& queue,
                                    const BridgeSendFn& send,
                                    const ShouldStopFn& shouldStop,
                                    const WorkerSleepFn& sleep)
    {
        if (!shouldStop) return;  // No stop signal — refuse to loop forever.
        while (!shouldStop())
        {
            queue.Drain(send);
            if (shouldStop()) break;  // Stop requested mid-tick — skip sleep.
            if (sleep) sleep();
        }
    }

    // ---- DLL-side lifecycle (overlay_action_worker.cpp, iter 516) -----------
    // These three are DEFINED in overlay_action_worker.cpp — the DLL glue the
    // header-only unit test deliberately does not link. Declared here so
    // overlay.cpp can drive the worker lifecycle (Install / Uninstall) and,
    // next overlay iter, the Phase 3 RenderActionsWindow can enqueue onto the
    // shared queue + read its latest result for the footer toast.

    // The single process-wide ActionQueue. The render thread enqueues onto it
    // (button onClick) and reads LatestResult() once per frame; the background
    // worker thread drains it. Same instance for the whole life of the DLL.
    ActionQueue& ActionQueueInstance();

    // Spawn the background drain worker if it is not already running.
    // Idempotent. Called from Install() after StartHudWorker(); the worker
    // runs RunActionWorkerLoop() with the real blocking BridgeProbe send.
    void StartActionWorker();

    // Signal the drain worker to stop and join it. Idempotent. Called from
    // Uninstall() before the D3D9 hooks are torn down so an in-flight bridge
    // round-trip finishes cleanly.
    void StopActionWorker();
}
