// =============================================================================
// swfoc_overlay/overlay_action_queue_test.cpp — unit test for
// overlay_action_queue.h.
//
// ActionQueue is header-only and self-contained (std::deque + std::mutex +
// std::function — no Windows, no ImGui, no D3D9, no bridge). This test
// compiles with a plain g++ and needs no game and no pipe: the bridge
// send-function is injected as a fake. Build + run via
// build_action_queue_test.bat.
//
// RED-GREEN REGRESSION PIN
// -----------------------
// The single most likely future regression is a "simplification" of the
// FIFO queue into a LIFO one (e.g. swapping std::deque::pop_front for a
// std::vector::pop_back). The "PIN fifo: drain order equals enqueue order"
// check below records the order the fake send observes the requests and
// asserts it equals the enqueue order — it passes ONLY on the correct FIFO
// form and fails on a LIFO regression.
//
// The non-blocking guarantee (no pipe I/O on the render thread) is an
// architectural property a unit test cannot assert; it is documented in
// overlay_action_queue.h's header comment and enforced by Drain() releasing
// the mutex across the send call.
// =============================================================================

#include "overlay_action_queue.h"

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
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_action_queue unit test ==\n");

    // ---- Fresh queue --------------------------------------------------------
    {
        ActionQueue q;
        ExpectEqInt("fresh: latest status is Idle",
                    StatusInt(q.LatestResult().status),
                    StatusInt(ActionStatus::Idle));
        ExpectEqInt("fresh: pending count is 0",
                    static_cast<long long>(q.PendingCount()), 0);
    }

    // ---- Enqueue marks Pending ---------------------------------------------
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "Spawn A", "return SWFOC_SpawnUnitLua(1)" });
        ExpectEqInt("enqueue: pending count is 1",
                    static_cast<long long>(q.PendingCount()), 1);
        ExpectEqInt("enqueue: latest status is Pending",
                    StatusInt(q.LatestResult().status),
                    StatusInt(ActionStatus::Pending));
        ExpectEqStr("enqueue: latest label is the request label",
                    q.LatestResult().label, "Spawn A");
        ExpectEqStr("enqueue: latest response is empty pre-drain",
                    q.LatestResult().response, "");
    }

    // ---- Drain with a succeeding send --------------------------------------
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "Spawn A", "LUA_A" });
        auto okSend = [](const std::string&, std::string& resp) -> bool
        {
            resp = "spawned ok";
            return true;
        };
        const int processed = q.Drain(okSend);
        ExpectEqInt("drain ok: returns 1 processed", processed, 1);
        ExpectEqInt("drain ok: pending count back to 0",
                    static_cast<long long>(q.PendingCount()), 0);
        ExpectEqInt("drain ok: latest status is Live",
                    StatusInt(q.LatestResult().status),
                    StatusInt(ActionStatus::Live));
        ExpectEqStr("drain ok: latest response is the bridge response",
                    q.LatestResult().response, "spawned ok");
        ExpectEqStr("drain ok: latest label preserved",
                    q.LatestResult().label, "Spawn A");
    }

    // ---- Drain with a failing send -----------------------------------------
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "Kill X", "LUA_K" });
        auto failSend = [](const std::string&, std::string& resp) -> bool
        {
            resp = "(pipe open failed)";
            return false;
        };
        q.Drain(failSend);
        ExpectEqInt("drain fail: latest status is Failed",
                    StatusInt(q.LatestResult().status),
                    StatusInt(ActionStatus::Failed));
        ExpectEqStr("drain fail: latest response is the failure reason",
                    q.LatestResult().response, "(pipe open failed)");
    }

    // ---- Drain on an empty queue is a no-op --------------------------------
    {
        ActionQueue q;
        const int processed = q.Drain(
            [](const std::string&, std::string& resp) -> bool
            {
                resp = "unexpected";
                return true;
            });
        ExpectEqInt("drain empty: returns 0 processed", processed, 0);
        ExpectEqInt("drain empty: latest status stays Idle",
                    StatusInt(q.LatestResult().status),
                    StatusInt(ActionStatus::Idle));
    }

    // ---- Null send-function fails the action safely ------------------------
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "Spawn A", "LUA_A" });
        q.Drain(BridgeSendFn{});  // empty std::function
        ExpectEqInt("null send: latest status is Failed",
                    StatusInt(q.LatestResult().status),
                    StatusInt(ActionStatus::Failed));
        ExpectEqStr("null send: latest response flags the missing send fn",
                    q.LatestResult().response, "(no send function)");
    }

    // ---- The send-function receives the lua line, not the label ------------
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "LABEL_ONLY", "THE_LUA_LINE" });
        std::string seenLua = "<none>";
        q.Drain([&seenLua](const std::string& lua, std::string& resp) -> bool
        {
            seenLua = lua;
            resp = "ok";
            return true;
        });
        ExpectEqStr("send receives the request's lua line",
                    seenLua, "THE_LUA_LINE");
    }

    // ---- RED-GREEN PIN: Drain dispatches in FIFO order ---------------------
    {
        ActionQueue q;
        q.Enqueue(ActionRequest{ "a", "L0" });
        q.Enqueue(ActionRequest{ "b", "L1" });
        q.Enqueue(ActionRequest{ "c", "L2" });
        ExpectEqInt("fifo: three requests pending",
                    static_cast<long long>(q.PendingCount()), 3);
        std::vector<std::string> seen;
        const int processed = q.Drain(
            [&seen](const std::string& lua, std::string& resp) -> bool
            {
                seen.push_back(lua);
                resp = "ok";
                return true;
            });
        ExpectEqInt("fifo: drain returns 3 processed", processed, 3);
        // Passes ONLY on FIFO (L0,L1,L2); a LIFO regression yields L2,L1,L0.
        ExpectEqStr("PIN fifo: drain order equals enqueue order",
                    Join(seen), "L0,L1,L2");
        ExpectEqStr("fifo: latest label is the last-drained request",
                    q.LatestResult().label, "c");
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
