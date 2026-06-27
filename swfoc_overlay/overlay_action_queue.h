// =============================================================================
// swfoc_overlay/overlay_action_queue.h — Phase 3 non-blocking action dispatch.
//
// Phase 3's interactive widgets (Spawn / Make-Invuln / Kill, iter 512) must
// issue bridge calls WITHOUT blocking the D3D9 Present detour. BridgeProbe
// (hud_state.cpp) does blocking named-pipe I/O — CreateFile + WriteFile +
// ReadFile. Calling it straight from a button's onClick handler would run
// that I/O on the render thread; a slow or stalled bridge would then freeze
// the host game's frame loop. The overlay deliberately avoided exactly this
// class of hazard in Phase 1 (it polls F1 from a worker thread, never the
// host's window message pump).
//
// ActionQueue keeps that guarantee. The render thread only ever ENQUEUES a
// request and READS the latest result snapshot — both are short lock-guarded
// in-memory operations. A single background worker drains the queue and
// performs the actual (blocking) pipe I/O off the render thread.
//
// The send-function is injected (BridgeSendFn) so the queue logic is fully
// unit-testable with a fake send — no bridge, no game, no DLL. See
// overlay_action_queue_test.cpp (build_action_queue_test.bat).
//
// NOTE: this header is not yet #included by any DLL translation unit. The
// next overlay iter wires the Phase 3 button onClick handlers to Enqueue()
// and starts the drain worker — see knowledge-base/overlay_phase3_*_iter513.md.
// =============================================================================

#pragma once

#include <cstddef>
#include <deque>
#include <functional>
#include <mutex>
#include <string>

namespace swfoc_overlay
{
    // Outcome of the most recently dispatched action. Mirrors the editor's
    // operator-trust badge vocabulary so the in-overlay toast reads the same
    // as the editor's LIVE / FAILED surfaces (guardrail 1007 — never let
    // "bridge call issued" be confused with "engine state changed").
    enum class ActionStatus
    {
        Idle,     // No action dispatched yet this session.
        Pending,  // Enqueued; the drain worker has not run it yet.
        Live,     // Bridge round-trip succeeded.
        Failed,   // Bridge round-trip failed (pipe error or send rejected).
    };

    // A queued action: a human-readable label for the toast plus the exact
    // Lua line to send through the bridge (built by overlay_actions.h).
    struct ActionRequest
    {
        std::string label;  // e.g. "Spawn Rebel_Trooper_Squad"
        std::string lua;    // e.g. "return SWFOC_SpawnUnitLua(...)"
    };

    // Snapshot of the most-recent action outcome, copied by the render
    // thread once per frame for the footer toast.
    struct ActionResult
    {
        ActionStatus status = ActionStatus::Idle;
        std::string label;     // label of the action this result describes
        std::string response;  // bridge response text, or failure reason
    };

    // The send-function contract: run `lua` through the bridge, write the
    // response into `response`, return true on success. BridgeProbe in
    // hud_state.cpp has exactly this shape; the test injects a fake.
    using BridgeSendFn =
        std::function<bool(const std::string& lua, std::string& response)>;

    // Thread-safe FIFO action queue + latest-result store.
    //
    //   Render thread : Enqueue() on button click, LatestResult() per frame.
    //   Worker thread : Drain(send) in a loop.
    //
    // Every public method takes the internal mutex, so the object is safe to
    // share between the render thread and the worker thread.
    class ActionQueue
    {
    public:
        // Render-thread side. Append a request to the FIFO and mark the
        // latest result Pending, so the toast updates the instant the
        // operator clicks — before the worker has run anything.
        void Enqueue(const ActionRequest& req)
        {
            std::lock_guard<std::mutex> lg(mutex_);
            pending_.push_back(req);
            latest_.status = ActionStatus::Pending;
            latest_.label = req.label;
            latest_.response.clear();
        }

        // Render-thread side. Copy the latest result for the toast.
        ActionResult LatestResult() const
        {
            std::lock_guard<std::mutex> lg(mutex_);
            return latest_;
        }

        // Number of requests still waiting to be drained.
        std::size_t PendingCount() const
        {
            std::lock_guard<std::mutex> lg(mutex_);
            return pending_.size();
        }

        // Worker-thread side. Pop and dispatch every pending request in
        // FIFO order, sending each through `send`. After each dispatch the
        // latest result is updated to Live or Failed. Returns the number of
        // requests processed.
        //
        // Each request is popped under the lock; the lock is then RELEASED
        // across the (blocking) send call, so a slow bridge never holds the
        // mutex — Enqueue() / LatestResult() on the render thread stay
        // non-blocking even mid-dispatch.
        int Drain(const BridgeSendFn& send)
        {
            int processed = 0;
            for (;;)
            {
                ActionRequest req;
                {
                    std::lock_guard<std::mutex> lg(mutex_);
                    if (pending_.empty()) break;
                    req = pending_.front();
                    pending_.pop_front();
                }

                ActionResult result;
                result.label = req.label;
                if (send)
                {
                    std::string response;
                    const bool ok = send(req.lua, response);
                    result.status = ok ? ActionStatus::Live
                                       : ActionStatus::Failed;
                    result.response = response;
                }
                else
                {
                    result.status = ActionStatus::Failed;
                    result.response = "(no send function)";
                }

                {
                    std::lock_guard<std::mutex> lg(mutex_);
                    latest_ = result;
                }
                ++processed;
            }
            return processed;
        }

    private:
        mutable std::mutex mutex_;
        std::deque<ActionRequest> pending_;
        ActionResult latest_;
    };
}
