// =============================================================================
// swfoc_overlay/overlay_phase3_catalog_test.cpp — unit test for
// overlay_phase3_catalog.h (Phase 3 close-out, iter 527 / spec iter-291).
//
// The Phase 3 close-out spec (overlay-interactive.md iter-291) calls for a
// "capability surface badge per Phase 3 widget" plus a regression test. The
// badge table RenderPhase3CapabilityTable() draws is fed by the kPhase3Widgets
// catalog; this test pins that catalog so the operator-trust badges (guardrail
// 1007) cannot drift from reality.
//
// overlay_phase3_catalog.h is header-only and std-only; this test also
// #includes overlay_actions.h (also header-only) to cross-check each
// catalogued wire against the builder that actually sends it. Build + run via
// build_phase3_catalog_test.bat — no game, no pipe.
//
// NAMING NOTE: the spec named the close-out test "Phase3WidgetsTests.cs". That
// C# name predates the overlay's test infrastructure: every overlay component
// is unit-tested as a native g++ exe (overlay_actions_test.cpp,
// overlay_recent_actions_test.cpp, overlay_action_queue_test.cpp, ...), and a
// C# test cannot exercise a C++ catalog header. This file IS that close-out
// test, in the established overlay C++ test pattern.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - WIRE-MATCHES-BUILDER : each widget's catalogued `wire` must appear in the
//                            Lua line its overlay_actions.h builder emits. A
//                            badge that names a wire the button does not call
//                            would lie to the operator. ("PIN wire-matches-
//                            builder".)
//   - ALL-LIVE             : every Phase 3 widget is WidgetStatus::Live. If a
//                            future iter demotes a wire, the catalog must be
//                            edited and this pin documents the expectation.
//   - COUNT / ORDER        : exactly 5 widgets, in the documented button order
//                            (Spawn, Make Invuln, Kill, Teleport, Faction
//                            Switch) — a reorder or an un-catalogued button
//                            fires the test.
// =============================================================================

#include "overlay_phase3_catalog.h"
#include "overlay_actions.h"

#include <cstddef>
#include <cstdio>
#include <cstring>
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

    // True when `haystack` contains `needle` as a substring.
    bool Contains(const std::string& haystack, const char* needle)
    {
        return haystack.find(needle) != std::string::npos;
    }

    // Assert the builder output for `widgetLabel` contains that widget's
    // catalogued wire — proves the operator-trust badge names the wire the
    // button really calls. Short-circuits on a catalog miss so a broken
    // catalog fails the check instead of dereferencing nullptr.
    void ExpectWireInBuilder(const char* widgetLabel, const std::string& built)
    {
        const swfoc_overlay::Phase3Widget* w =
            swfoc_overlay::FindPhase3Widget(widgetLabel);
        char nm[96];
        std::snprintf(nm, sizeof(nm),
                      "PIN wire-matches-builder: %s builder sends its wire",
                      widgetLabel);
        ExpectTrue(nm, w != nullptr && Contains(built, w->wire));
    }
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_phase3_catalog unit test ==\n");

    // ---- Catalog size -----------------------------------------------------
    ExpectEqInt("count: kPhase3WidgetCount is 5",
                static_cast<long long>(kPhase3WidgetCount), 5);

    // ---- Draw order: catalog matches RenderActionsWindow's button order ---
    // Spawn, Make Invuln, Kill, then the Teleport / Faction Switch row.
    const char* const kExpectLabels[] = {
        "Spawn", "Make Invuln", "Kill", "Teleport", "Faction Switch",
    };
    const char* const kExpectWires[] = {
        "SWFOC_SpawnUnitLua", "SWFOC_MakeUnitInvulnLua", "SWFOC_KillUnit",
        "SWFOC_TeleportUnitLua", "SWFOC_ChangeUnitOwner",
    };
    for (std::size_t i = 0; i < kPhase3WidgetCount; ++i)
    {
        char nm[64];
        std::snprintf(nm, sizeof(nm), "order: widget %zu label", i);
        ExpectEqStr(nm, kPhase3Widgets[i].label, kExpectLabels[i]);
        std::snprintf(nm, sizeof(nm), "order: widget %zu wire", i);
        ExpectEqStr(nm, kPhase3Widgets[i].wire, kExpectWires[i]);
    }

    // ---- PIN all-live: every Phase 3 widget is WidgetStatus::Live ---------
    for (std::size_t i = 0; i < kPhase3WidgetCount; ++i)
    {
        char nm[80];
        std::snprintf(nm, sizeof(nm), "PIN all-live: %s is WidgetStatus::Live",
                      kPhase3Widgets[i].label);
        ExpectTrue(nm, kPhase3Widgets[i].status == WidgetStatus::Live);
    }

    // ---- No widget has a null or empty label / wire -----------------------
    for (std::size_t i = 0; i < kPhase3WidgetCount; ++i)
    {
        char nm[64];
        std::snprintf(nm, sizeof(nm), "fields: widget %zu label non-empty", i);
        ExpectTrue(nm, kPhase3Widgets[i].label != nullptr &&
                       kPhase3Widgets[i].label[0] != '\0');
        std::snprintf(nm, sizeof(nm), "fields: widget %zu wire non-empty", i);
        ExpectTrue(nm, kPhase3Widgets[i].wire != nullptr &&
                       kPhase3Widgets[i].wire[0] != '\0');
    }

    // ---- Labels + wires are unique (distinct ImGui IDs / capabilities) ----
    for (std::size_t i = 0; i < kPhase3WidgetCount; ++i)
    {
        for (std::size_t j = i + 1; j < kPhase3WidgetCount; ++j)
        {
            char nm[96];
            std::snprintf(nm, sizeof(nm), "unique: labels %zu/%zu differ", i, j);
            ExpectTrue(nm, std::strcmp(kPhase3Widgets[i].label,
                                       kPhase3Widgets[j].label) != 0);
            std::snprintf(nm, sizeof(nm), "unique: wires %zu/%zu differ", i, j);
            ExpectTrue(nm, std::strcmp(kPhase3Widgets[i].wire,
                                       kPhase3Widgets[j].wire) != 0);
        }
    }

    // ---- Badge text strings are stable ------------------------------------
    ExpectEqStr("badge: Live text", WidgetStatusBadge(WidgetStatus::Live),
                "[LIVE]");
    ExpectEqStr("badge: Phase2 text", WidgetStatusBadge(WidgetStatus::Phase2),
                "[PHASE 2 PENDING]");
    ExpectEqStr("badge: LiveOnly text",
                WidgetStatusBadge(WidgetStatus::LiveOnly), "[LIVE ONLY]");

    // ---- FindPhase3Widget: hit + miss + null + exact-match-only -----------
    {
        const Phase3Widget* spawn = FindPhase3Widget("Spawn");
        ExpectTrue("find: 'Spawn' is catalogued", spawn != nullptr);
        if (spawn != nullptr)
        {
            ExpectEqStr("find: 'Spawn' maps to its wire", spawn->wire,
                        "SWFOC_SpawnUnitLua");
        }
        ExpectTrue("find: 'Faction Switch' is catalogued",
                   FindPhase3Widget("Faction Switch") != nullptr);
        ExpectTrue("find: unknown label 'Heal' returns nullptr",
                   FindPhase3Widget("Heal") == nullptr);
        ExpectTrue("find: nullptr label returns nullptr",
                   FindPhase3Widget(nullptr) == nullptr);
        // Lookup is exact-match — a label prefix must not resolve.
        ExpectTrue("find: prefix 'Spaw' does not match 'Spawn'",
                   FindPhase3Widget("Spaw") == nullptr);
    }

    // ---- PIN wire-matches-builder: the catalogued wire is the wire the ----
    //      overlay_actions.h builder actually sends. A badge cannot lie.
    ExpectWireInBuilder("Spawn",
        BuildSpawnUnitCommand("REBEL", "Rebel_Trooper_Squad",
                              1.0f, 2.0f, 3.0f));
    ExpectWireInBuilder("Make Invuln",
        BuildMakeUnitInvulnCommand("Find_First_Object(\"Empire_AT_AT\")", true));
    ExpectWireInBuilder("Kill",
        BuildKillUnitCommand(0x140000000ull));
    ExpectWireInBuilder("Teleport",
        BuildTeleportUnitCommand("Find_First_Object(\"Empire_AT_AT\")",
                                 1.0f, 2.0f, 3.0f));
    ExpectWireInBuilder("Faction Switch",
        BuildChangeUnitOwnerCommand("Find_First_Object(\"Empire_AT_AT\")",
                                    "REBEL"));

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
