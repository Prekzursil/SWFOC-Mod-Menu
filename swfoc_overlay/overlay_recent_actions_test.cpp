// =============================================================================
// swfoc_overlay/overlay_recent_actions_test.cpp — unit test for
// overlay_recent_actions.h.
//
// RecentActions is header-only and dependency-free (it pulls in only
// overlay_action_queue.h for ActionRequest + <vector>/<cstddef> — no Windows,
// no ImGui, no bridge, no <thread>). This test drives the whole structure
// deterministically with a plain g++ — no game, no pipe. Build + run via
// build_recent_actions_test.bat.
//
// RED-GREEN REGRESSION PINS
// ------------------------
// RecentActions has three behaviours that a naive "just push and cap" rewrite
// would silently break:
//   - DEDUP-PROMOTE   : recording an action whose `lua` is already present must
//                       move that one slot to the front, not append a
//                       duplicate. A raw FIFO ring would show the same action
//                       in several slots. ("PIN dedup-promote".)
//   - NO-REFIRE-EVICT : promoting an already-present action at full capacity
//                       must NOT evict the oldest distinct action — the
//                       promote erases one before re-inserting, so size is
//                       unchanged. An "insert then cap" without the prior
//                       erase would drop a real action on every re-fire.
//                       ("PIN no-refire-evict".)
//   - ALIAS-SAFE      : the toolbar re-fires a slot with Record(At(i)), so
//                       `req` aliases an internal element. Record() must copy
//                       `req` before the promote-erase or the re-insert reads
//                       freed memory. ("PIN alias-safe".)
// =============================================================================

#include "overlay_recent_actions.h"

#include <cstddef>
#include <cstdio>
#include <string>

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

    void ExpectTrue(const char* name, bool cond)
    {
        ++g_checks;
        if (cond)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    expected true\n", name);
        }
    }

    // Build an ActionRequest with distinct label and lua so the tests can tell
    // promote (lua identity) apart from a plain re-insert.
    swfoc_overlay::ActionRequest Req(const std::string& label,
                                     const std::string& lua)
    {
        swfoc_overlay::ActionRequest r;
        r.label = label;
        r.lua = lua;
        return r;
    }
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_recent_actions unit test ==\n");

    // ---- A fresh history is empty -----------------------------------------
    {
        RecentActions rec;
        ExpectTrue("fresh: Empty() is true", rec.Empty());
        ExpectEqInt("fresh: Count() is 0",
                    static_cast<long long>(rec.Count()), 0);
    }

    // ---- Recording one action makes it the only, most-recent entry --------
    {
        RecentActions rec;
        rec.Record(Req("Spawn A", "L_A"));
        ExpectTrue("single: Empty() is false", !rec.Empty());
        ExpectEqInt("single: Count() is 1",
                    static_cast<long long>(rec.Count()), 1);
        ExpectEqStr("single: At(0) label", rec.At(0).label, "Spawn A");
        ExpectEqStr("single: At(0) lua", rec.At(0).lua, "L_A");
    }

    // ---- Distinct records stack most-recent-first (index 0 == newest) -----
    {
        RecentActions rec;
        rec.Record(Req("Spawn A", "L_A"));
        rec.Record(Req("Spawn B", "L_B"));
        rec.Record(Req("Spawn C", "L_C"));
        ExpectEqInt("order: Count() is 3",
                    static_cast<long long>(rec.Count()), 3);
        ExpectEqStr("order: At(0) is the newest (C)", rec.At(0).label, "Spawn C");
        ExpectEqStr("order: At(1) is the middle (B)", rec.At(1).label, "Spawn B");
        ExpectEqStr("order: At(2) is the oldest (A)", rec.At(2).label, "Spawn A");
    }

    // ---- Capacity caps at kCapacity; the oldest distinct entries evict ----
    {
        RecentActions rec;
        rec.Record(Req("a", "L1"));
        rec.Record(Req("b", "L2"));
        rec.Record(Req("c", "L3"));
        rec.Record(Req("d", "L4"));
        rec.Record(Req("e", "L5"));
        rec.Record(Req("f", "L6"));
        rec.Record(Req("g", "L7"));  // 7 distinct; kCapacity is 5.
        ExpectEqInt("cap: Count() pinned to kCapacity (5)",
                    static_cast<long long>(rec.Count()),
                    static_cast<long long>(RecentActions::kCapacity));
        ExpectEqStr("cap: At(0) is the newest (g)", rec.At(0).label, "g");
        ExpectEqStr("cap: At(4) is the oldest survivor (c)",
                    rec.At(4).label, "c");
        // L1 ("a") and L2 ("b") evicted — nothing left matches them.
        bool aGone = true;
        bool bGone = true;
        for (std::size_t i = 0; i < rec.Count(); ++i)
        {
            if (rec.At(i).lua == "L1") aGone = false;
            if (rec.At(i).lua == "L2") bGone = false;
        }
        ExpectTrue("cap: oldest entry 'a' (L1) evicted", aGone);
        ExpectTrue("cap: second-oldest entry 'b' (L2) evicted", bGone);
    }

    // ---- PIN dedup-promote: a repeat `lua` moves to front, no duplicate ----
    {
        RecentActions rec;
        rec.Record(Req("Spawn A", "L_A"));
        rec.Record(Req("Spawn B", "L_B"));
        rec.Record(Req("Spawn C", "L_C"));
        rec.Record(Req("Spawn A", "L_A"));  // repeat of the first action.
        // A raw FIFO ring would now hold {A, C, B, A} — Count 4, A twice.
        ExpectEqInt("PIN dedup-promote: Count() stays 3 (no duplicate slot)",
                    static_cast<long long>(rec.Count()), 3);
        ExpectEqStr("PIN dedup-promote: At(0) is the promoted A",
                    rec.At(0).label, "Spawn A");
        ExpectEqStr("PIN dedup-promote: At(1) is C", rec.At(1).label, "Spawn C");
        ExpectEqStr("PIN dedup-promote: At(2) is B", rec.At(2).label, "Spawn B");
    }

    // ---- PIN dedup identity is the `lua` line, not the `label` -------------
    {
        RecentActions rec;
        rec.Record(Req("old label", "SAME_LUA"));
        rec.Record(Req("new label", "SAME_LUA"));  // same command, new label.
        ExpectEqInt("PIN dedup-by-lua: Count() is 1 (same command)",
                    static_cast<long long>(rec.Count()), 1);
        ExpectEqStr("PIN dedup-by-lua: newer label wins on promote",
                    rec.At(0).label, "new label");
        ExpectEqStr("PIN dedup-by-lua: lua preserved",
                    rec.At(0).lua, "SAME_LUA");
    }

    // ---- PIN no-refire-evict: promoting at full capacity drops nothing -----
    {
        RecentActions rec;
        rec.Record(Req("a", "L1"));
        rec.Record(Req("b", "L2"));
        rec.Record(Req("c", "L3"));
        rec.Record(Req("d", "L4"));
        rec.Record(Req("e", "L5"));  // history full: e,d,c,b,a.
        rec.Record(Req("c", "L3"));  // re-fire an entry already present.
        ExpectEqInt("PIN no-refire-evict: Count() stays at kCapacity (5)",
                    static_cast<long long>(rec.Count()),
                    static_cast<long long>(RecentActions::kCapacity));
        ExpectEqStr("PIN no-refire-evict: re-fired 'c' promoted to front",
                    rec.At(0).label, "c");
        // The oldest distinct entry 'a' must survive — an "insert then cap"
        // without the promote-erase would have evicted it here.
        ExpectEqStr("PIN no-refire-evict: oldest 'a' NOT evicted",
                    rec.At(4).label, "a");
        // Full order after the re-fire: c, e, d, b, a.
        ExpectEqStr("no-refire-evict: At(1) is e", rec.At(1).label, "e");
        ExpectEqStr("no-refire-evict: At(2) is d", rec.At(2).label, "d");
        ExpectEqStr("no-refire-evict: At(3) is b", rec.At(3).label, "b");
    }

    // ---- PIN alias-safe: the click-to-re-fire path — Record(At(i)) ---------
    {
        RecentActions rec;
        rec.Record(Req("Spawn A", "L_A"));
        rec.Record(Req("Spawn B", "L_B"));
        rec.Record(Req("Spawn C", "L_C"));
        // Operator clicks recent slot 2 (the oldest, "Spawn A") to re-fire it.
        // At(2) returns a reference INTO the internal vector; passing it
        // straight back into Record() is exactly what the toolbar glue does.
        // Record() must copy before the promote-erase or this is UB.
        rec.Record(rec.At(2));
        ExpectEqInt("PIN alias-safe: Count() stays 3 after Record(At(i))",
                    static_cast<long long>(rec.Count()), 3);
        ExpectEqStr("PIN alias-safe: re-fired slot promoted intact",
                    rec.At(0).label, "Spawn A");
        ExpectEqStr("PIN alias-safe: re-fired lua intact",
                    rec.At(0).lua, "L_A");
        ExpectEqStr("PIN alias-safe: At(1) is C", rec.At(1).label, "Spawn C");
        ExpectEqStr("PIN alias-safe: At(2) is B", rec.At(2).label, "Spawn B");
    }

    // ---- Clear() empties the history --------------------------------------
    {
        RecentActions rec;
        rec.Record(Req("a", "L1"));
        rec.Record(Req("b", "L2"));
        rec.Clear();
        ExpectTrue("clear: Empty() is true", rec.Empty());
        ExpectEqInt("clear: Count() is 0",
                    static_cast<long long>(rec.Count()), 0);
        // The history is reusable after a clear.
        rec.Record(Req("fresh", "L_FRESH"));
        ExpectEqInt("clear: recordable again after Clear()",
                    static_cast<long long>(rec.Count()), 1);
        ExpectEqStr("clear: At(0) after re-record", rec.At(0).label, "fresh");
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
