// =============================================================================
// swfoc_overlay/overlay_phase3_catalog.h — Phase 3 widget capability catalog.
//
// Phase 3 (iter 287-291) gave the overlay five interactive "Actions" widgets:
// Spawn / Make Invuln / Kill / Teleport / Faction Switch. Each issues an engine
// mutation through one SWFOC_* bridge wire (overlay_actions.h builders). The
// operator-trust pattern (guardrail 1007) requires every operator-facing button
// to surface its capability status so a click is never confused with a
// confirmed engine-state change.
//
// This header is the AUTHORITATIVE Phase 3 widget catalog: one Phase3Widget
// entry per button — its label, the bridge wire it dispatches, and that wire's
// capability status. Two consumers share this single source of truth:
//   - overlay.cpp RenderPhase3CapabilityTable() draws a per-widget badge row
//     straight from kPhase3Widgets — the badge can never drift from the catalog.
//   - overlay_phase3_catalog_test.cpp pins the catalog (count, labels, wires,
//     all-LIVE status, badge text) AND cross-checks each declared wire against
//     the matching overlay_actions.h builder so a badge can never lie.
//
// It replaces the iter-525 static "Wires (all LIVE): ..." footer string, which
// was hand-maintained and could silently drift from the wires the buttons
// actually call (the iter-380 / iter-388 stale-string-drift family of bugs).
//
// Pure, header-only, std-only — no ImGui, no Windows, no bridge. Unit-tested
// with a plain g++ (build_phase3_catalog_test.bat).
//
// All five wires are LIVE — verified registered in swfoc_lua_bridge/
// lua_bridge.cpp (2026-05-21): SWFOC_SpawnUnitLua, SWFOC_MakeUnitInvulnLua,
// SWFOC_KillUnit, SWFOC_TeleportUnitLua, SWFOC_ChangeUnitOwner. If a future
// iter demotes one (e.g. to PHASE 2 PENDING), flip its WidgetStatus here and
// the test's all-LIVE pin documents the change.
// =============================================================================

#pragma once

#include <cstddef>
#include <cstring>

namespace swfoc_overlay
{
    // Operator-trust capability status for a Phase 3 widget (guardrail 1007).
    enum class WidgetStatus
    {
        Live,      // bridge wire confirmed LIVE in swfoc_lua_bridge/lua_bridge.cpp
        Phase2,    // wire registered but PHASE 2 PENDING (no engine effect yet)
        LiveOnly,  // wire works only against a live game (no replay-harness path)
    };

    // Short bracketed badge text for a widget status — drawn inline next to the
    // button by RenderPhase3CapabilityTable(). Stable strings: the catalog test
    // pins each one so a UI tweak cannot quietly reword an operator-trust badge.
    inline const char* WidgetStatusBadge(WidgetStatus status)
    {
        switch (status)
        {
            case WidgetStatus::Live:     return "[LIVE]";
            case WidgetStatus::Phase2:   return "[PHASE 2 PENDING]";
            case WidgetStatus::LiveOnly: return "[LIVE ONLY]";
        }
        return "[?]";  // unreachable — every enumerator is handled above.
    }

    // One Phase 3 interactive widget: the ImGui button label, the SWFOC_* bridge
    // wire it dispatches, and that wire's operator-trust capability status.
    struct Phase3Widget
    {
        const char*  label;   // ImGui button label, e.g. "Spawn"
        const char*  wire;    // bridge wire name, e.g. "SWFOC_SpawnUnitLua"
        WidgetStatus status;  // capability badge status
    };

    // The authoritative Phase 3 widget catalog. Order matches the buttons'
    // draw order in RenderActionsWindow (Spawn, Make Invuln, Kill, then the
    // Teleport / Faction Switch row). Adding a Phase 3 button without a catalog
    // entry — or changing a wire — fires overlay_phase3_catalog_test.cpp.
    inline constexpr Phase3Widget kPhase3Widgets[] = {
        { "Spawn",          "SWFOC_SpawnUnitLua",      WidgetStatus::Live },
        { "Make Invuln",    "SWFOC_MakeUnitInvulnLua", WidgetStatus::Live },
        { "Kill",           "SWFOC_KillUnit",          WidgetStatus::Live },
        { "Teleport",       "SWFOC_TeleportUnitLua",   WidgetStatus::Live },
        { "Faction Switch", "SWFOC_ChangeUnitOwner",   WidgetStatus::Live },
    };

    // Number of catalogued Phase 3 widgets (5).
    inline constexpr std::size_t kPhase3WidgetCount =
        sizeof(kPhase3Widgets) / sizeof(kPhase3Widgets[0]);

    // Look up a Phase 3 widget by its exact button label. Returns nullptr when
    // `label` is null or not catalogued — the test exercises both the hit and
    // the miss path, and the wire-matches-builder pins resolve wires through it.
    inline const Phase3Widget* FindPhase3Widget(const char* label)
    {
        if (label == nullptr) return nullptr;
        for (std::size_t i = 0; i < kPhase3WidgetCount; ++i)
        {
            if (std::strcmp(kPhase3Widgets[i].label, label) == 0)
            {
                return &kPhase3Widgets[i];
            }
        }
        return nullptr;
    }
}
