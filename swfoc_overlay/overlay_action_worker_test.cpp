// =============================================================================
// swfoc_overlay/overlay_action_worker_test.cpp — unit test for
// overlay_action_worker.h.
//
// RunActionWorkerLoop is header-only and dependency-free (it pulls in only
// overlay_action_queue.h + <functional> — no Windows, no <thread>, no ImGui,
// no bridge). The blocking bridge send, the real Sleep and the shutdown
// signal are all injected as callables, so this test drives the entire worker
// loop deterministically with a plain g++ — no game, no pipe, no thread.
// Build + run via build_action_worker_test.bat.
//
// RED-GREEN REGRESSION PINS
// ------------------------
// The loop has two ordering behaviours that are easy to "simplify" away:
//   - CHECK-FIRST  : a do/while (drain-then-check) loop would fire one last
//                    bridge call into a possibly-dead pipe during DLL
//                    teardown. The "PIN check-first" checks enqueue a request,
//                    stop the loop before its first tick, and assert the send
//                    was never called — they pass ONLY on the while-form.
//   - NO-SLEEP-ON-STOP : a loop that sleeps unconditionally before re-checking
//                    shutdown would delay DLL teardown by a full sleep
//                    interval. The "PIN no-sleep-on-stop" check stops the loop
//                    immediately after a drain and asserts zero sleeps.
// =============================================================================

#include "overlay_action_worker.h"

#include <cstddef>
#include <cstdio>
#include <string>
#include <vector>

namespace
{
    int g_checks = 0;
    int g_failures = 0;

    void ExpectEqInt(const char* name, long long got, long long want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %lld\n    want: %lld\n",
                        name, got, want);
        }
    }

    void ExpectEqStr(const char* name, const std::string& got,
                     const std::string& want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %s\n    want: %s\n",
                        name, got.c_str(), want.c_str());
        }
    }

    long long StatusInt(swfoc_overlay::ActionStatus s)
    {
        return static_cast<long long>(s);
    }

    std::string Join(const std::vector<std::string>& v)
    {
        std::string s;
        for (std::size_t i = 0; i < v.size(); ++i)
        {
            if (i != 0) s += ",";
            s += v[i];
        }
        return s;
    }

    // A ShouldStopFn fake: returns false for the first `falseCalls` calls,
    // then true forever. `calls` (captured by reference) records exactly how
    // many times the loop polled the predicate, which the tests assert on to
    // verify the loop's check ordering. The referenced int outlives the
    // synchronous RunActionWorkerLoop call, so the reference stays valid.
    swfoc_overlay::ShouldStopFn StopAfter(int& calls, int falseCalls)
    {
        return [&calls, falseCalls]() -> bool
        {
            const bool stop = calls >= falseCalls;
            ++calls;
            return stop;
        };
    }
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_action_worker unit test ==\n");

    // ---- Empty shouldStop refuses to loop (would otherwise spin forever) ----
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "Spawn A", "L0" });
        int sends = 0;
        BridgeSendFn countSend =
            [&sends](const std::string&, std::string& r) -> bool
        {
            ++sends;
            r = "ok";
            return true;
        };
        int sleeps = 0;
        RunActionWorkerLoop(q, countSend, ShouldStopFn{},
                            [&sleeps]() { ++sleeps; });
        ExpectEqInt("empty shouldStop: send never called", sends, 0);
        ExpectEqInt("empty shouldStop: sleep never called", sleeps, 0);
        ExpectEqInt("empty shouldStop: request still pending",
                    static_cast<long long>(q.PendingCount()), 1);
    }

    // ---- PIN CHECK-FIRST: stopped before the first tick -> zero bridge I/O -
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "Spawn A", "L0" });
        int sends = 0;
        BridgeSendFn countSend =
            [&sends](const std::string&, std::string& r) -> bool
        {
            ++sends;
            r = "ok";
            return true;
        };
        int stopCalls = 0;
        int sleeps = 0;
        // falseCalls = 0: shouldStop is true on its very first call.
        RunActionWorkerLoop(q, countSend, StopAfter(stopCalls, 0),
                            [&sleeps]() { ++sleeps; });
        // PIN: a do/while (drain-then-check) loop would drain the request once.
        ExpectEqInt("PIN check-first: stopped pre-tick -> zero sends",
                    sends, 0);
        ExpectEqInt("PIN check-first: stopped pre-tick -> request still pending",
                    static_cast<long long>(q.PendingCount()), 1);
        ExpectEqInt("check-first: stopped pre-tick -> zero sleeps", sleeps, 0);
        ExpectEqInt("check-first: shouldStop polled exactly once",
                    stopCalls, 1);
    }

    // ---- A single tick drains every pending request, FIFO ------------------
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "a", "L0" });
        q.Enqueue(ActionRequest{ "b", "L1" });
        q.Enqueue(ActionRequest{ "c", "L2" });
        std::vector<std::string> seen;
        BridgeSendFn okSend =
            [&seen](const std::string& lua, std::string& r) -> bool
        {
            seen.push_back(lua);
            r = "ok";
            return true;
        };
        int stopCalls = 0;
        int sleeps = 0;
        // falseCalls = 2: false on the while-check and the post-drain check
        // of tick 1, then true on the while-check of tick 2.
        RunActionWorkerLoop(q, okSend, StopAfter(stopCalls, 2),
                            [&sleeps]() { ++sleeps; });
        ExpectEqInt("single tick: all three requests drained",
                    static_cast<long long>(seen.size()), 3);
        ExpectEqStr("single tick: drained in FIFO order", Join(seen),
                    "L0,L1,L2");
        ExpectEqInt("single tick: queue emptied",
                    static_cast<long long>(q.PendingCount()), 0);
        ExpectEqInt("single tick: one inter-drain sleep before exit",
                    sleeps, 1);
    }

    // ---- PIN NO-SLEEP-ON-STOP: shutdown right after a drain skips the sleep -
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "a", "L0" });
        int sends = 0;
        BridgeSendFn countSend =
            [&sends](const std::string&, std::string& r) -> bool
        {
            ++sends;
            r = "ok";
            return true;
        };
        int stopCalls = 0;
        int sleeps = 0;
        // falseCalls = 1: false on the while-check of tick 1, true on the
        // post-drain check — i.e. shutdown lands the instant the drain ends.
        RunActionWorkerLoop(q, countSend, StopAfter(stopCalls, 1),
                            [&sleeps]() { ++sleeps; });
        ExpectEqInt("no-sleep-on-stop: the one request was drained", sends, 1);
        // PIN: a loop that sleeps unconditionally before re-checking shutdown
        // would sleep once here, delaying DLL teardown by a full interval.
        ExpectEqInt("PIN no-sleep-on-stop: stop after drain -> zero sleeps",
                    sleeps, 0);
    }

    // ---- Multi-tick: the loop keeps draining across several ticks ----------
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "a", "L0" });
        q.Enqueue(ActionRequest{ "b", "L1" });
        int sends = 0;
        BridgeSendFn countSend =
            [&sends](const std::string&, std::string& r) -> bool
        {
            ++sends;
            r = "ok";
            return true;
        };
        int stopCalls = 0;
        int sleeps = 0;
        // falseCalls = 4: two full ticks (each consumes 2 shouldStop polls),
        // then the 5th poll stops the loop.
        RunActionWorkerLoop(q, countSend, StopAfter(stopCalls, 4),
                            [&sleeps]() { ++sleeps; });
        ExpectEqInt("multi-tick: both requests drained on tick 1", sends, 2);
        ExpectEqInt("multi-tick: two inter-drain sleeps", sleeps, 2);
        ExpectEqInt("multi-tick: shouldStop polled five times", stopCalls, 5);
    }

    // ---- A failing send does not break the loop; the queue still empties ---
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "Kill X", "LK" });
        BridgeSendFn failSend =
            [](const std::string&, std::string& r) -> bool
        {
            r = "(pipe open failed)";
            return false;
        };
        int stopCalls = 0;
        // Empty WorkerSleepFn — also exercises the loop's `if (sleep)` guard.
        RunActionWorkerLoop(q, failSend, StopAfter(stopCalls, 1),
                            WorkerSleepFn{});
        ExpectEqInt("failing send: latest status is Failed",
                    StatusInt(q.LatestResult().status),
                    StatusInt(ActionStatus::Failed));
        ExpectEqInt("failing send: queue still emptied",
                    static_cast<long long>(q.PendingCount()), 0);
    }

    // ---- Empty sleep fn is tolerated across multiple ticks -----------------
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "a", "L0" });
        std::vector<std::string> seen;
        BridgeSendFn okSend =
            [&seen](const std::string& lua, std::string& r) -> bool
        {
            seen.push_back(lua);
            r = "ok";
            return true;
        };
        int stopCalls = 0;
        // falseCalls = 3: the loop ticks twice with no sleep callable bound.
        RunActionWorkerLoop(q, okSend, StopAfter(stopCalls, 3),
                            WorkerSleepFn{});
        ExpectEqInt("empty sleep: loop still drains its request",
                    static_cast<long long>(seen.size()), 1);
        ExpectEqInt("empty sleep: loop still terminates cleanly",
                    stopCalls, 4);
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
