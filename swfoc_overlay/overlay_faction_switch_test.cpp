// =============================================================================
// swfoc_overlay/overlay_faction_switch_test.cpp — unit test for
// overlay_faction_switch.h (Phase 6 cont., iter 545 / spec iter-305).
//
// iter-305 is the F3 faction-switch hotkey: one press re-owns the local
// player's whole visible army to the next faction, gated by a confirm-prompt
// so a mass-defection can never happen by accident. overlay_faction_switch.h
// holds the pure kernel — the Idle/Armed confirm state machine, the bulk
// SWFOC_ChangeUnitOwner batch builder, and the prompt-sizing count. This test
// pins all of it so the deferred overlay.cpp F3 glue can depend on it
// build-only.
//
// The integration section runs the full operator cycle: a visible-unit set ->
// CountUnitsOwnedBy -> FactionSwitchPrompt.Arm -> Confirm ->
// BuildFactionSwitchBatch -> a list of dispatch-ready ActionRequests whose Lua
// are real SWFOC_ChangeUnitOwner bridge calls.
//
// overlay_faction_switch.h is header-only and std-only (<cstddef> / <string> /
// <vector>, plus <cstdio> via overlay_actions.h and <deque> / <functional> /
// <mutex> via overlay_action_queue.h — -pthread is load-bearing for the
// threading runtime that include chain pulls in). No game, no pipe, no ImGui.
// Build + run via build_faction_switch_test.bat.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - ARM REQUIRES UNITS       : Arm(slot, 0) is refused.
//   - DOUBLE-TAP DOESN'T SKIP  : Arm while Armed is a no-op.
//   - CONFIRM GATES THE BATCH  : Confirm() returns false unless Armed.
//   - CANCEL ABORTS THE SWITCH : after Cancel() a Confirm() returns false.
//   - BATCH SKIPS NON-OWNED    : only units owned by fromSlot are re-owned.
//   - BATCH RE-OWNS TO NEXT    : each request re-owns to the NEXT faction.
//   - COUNT MATCHES BATCH SIZE : CountUnitsOwnedBy == batch size.
//   - EXPR ESCAPES TYPE NAME   : a quote in a unit type is escaped.
// =============================================================================

#include "overlay_faction_switch.h"

#include <cstddef>
#include <cstdio>
#include <string>
#include <vector>

namespace
{
    int g_checks = 0;
    int g_failures = 0;

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

    void ExpectStr(const char* name, const std::string& got, const char* want)
    {
        ++g_checks;
        if (want != nullptr && got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : \"%s\"\n    want: \"%s\"\n",
                        name, got.c_str(), want != nullptr ? want : "(null)");
        }
    }

    void ExpectContains(const char* name, const std::string& haystack,
                        const char* needle)
    {
        ++g_checks;
        if (needle != nullptr &&
            haystack.find(needle) != std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\"\n    must contain: \"%s\"\n",
                        name, haystack.c_str(),
                        needle != nullptr ? needle : "(null)");
        }
    }

    void ExpectAbsent(const char* name, const std::string& haystack,
                      const char* needle)
    {
        ++g_checks;
        if (needle != nullptr &&
            haystack.find(needle) == std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\"\n    must NOT contain: \"%s\"\n",
                        name, haystack.c_str(),
                        needle != nullptr ? needle : "(null)");
        }
    }

    void ExpectSize(const char* name, std::size_t got, std::size_t want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %zu\n    want: %zu\n",
                        name, got, want);
        }
    }

    using swfoc_overlay::ActionRequest;
    using swfoc_overlay::BuildFactionSwitchBatch;
    using swfoc_overlay::CountUnitsOwnedBy;
    using swfoc_overlay::FactionSwitchPrompt;
    using swfoc_overlay::NextFactionSlot;
    using swfoc_overlay::SetUnitType;
    using swfoc_overlay::UnitInfo;
    using swfoc_overlay::kFactionSlotCount;

    // Build a visible-unit record with just the two fields the faction-switch
    // kernel reads — owner faction slot and type name. Everything else is
    // value-initialised to zero (UnitInfo is a POD).
    UnitInfo MakeUnit(int owner, const char* type)
    {
        UnitInfo u{};
        u.owner = owner;
        SetUnitType(u, type);
        return u;
    }
}

int main()
{
    std::printf("=== overlay_faction_switch.h kernel test ===\n\n");

    // -----------------------------------------------------------------------
    // 1. Faction cycle sanity — the kernel re-uses NextFactionSlot.
    // -----------------------------------------------------------------------
    std::printf("[faction cycle sanity]\n");
    {
        ExpectTrue("kFactionSlotCount is 3", kFactionSlotCount == 3);
        ExpectTrue("cycle Rebel -> Empire", NextFactionSlot(0) == 1);
        ExpectTrue("cycle Empire -> Underworld", NextFactionSlot(1) == 2);
        ExpectTrue("cycle Underworld -> Rebel", NextFactionSlot(2) == 0);
        // An out-of-range slot resets to 0 so the cycle is total.
        ExpectTrue("cycle out-of-range -> Rebel", NextFactionSlot(9) == 0);
    }

    // -----------------------------------------------------------------------
    // 2. CountUnitsOwnedBy — sizes the confirm prompt.
    // -----------------------------------------------------------------------
    std::printf("\n[CountUnitsOwnedBy]\n");
    {
        const std::vector<UnitInfo> empty;
        ExpectSize("an empty field counts 0", CountUnitsOwnedBy(empty, 0), 0u);

        std::vector<UnitInfo> mixed;
        mixed.push_back(MakeUnit(0, "Heavy_Tank"));     // Rebel
        mixed.push_back(MakeUnit(0, "Air_Speeder"));    // Rebel
        mixed.push_back(MakeUnit(1, "Ion_Cannon"));     // Empire
        mixed.push_back(MakeUnit(2, "Skiff"));          // Underworld
        ExpectSize("counts the 2 Rebel units", CountUnitsOwnedBy(mixed, 0), 2u);
        ExpectSize("counts the 1 Empire unit", CountUnitsOwnedBy(mixed, 1), 1u);
        ExpectSize("counts the 1 Underworld unit",
                   CountUnitsOwnedBy(mixed, 2), 1u);
        // A faction with no visible units counts 0, not garbage.
        ExpectSize("a faction with no units counts 0",
                   CountUnitsOwnedBy(mixed, 5), 0u);

        std::vector<UnitInfo> allRebel;
        allRebel.push_back(MakeUnit(0, "A"));
        allRebel.push_back(MakeUnit(0, "B"));
        allRebel.push_back(MakeUnit(0, "C"));
        ExpectSize("an all-Rebel field counts every unit",
                   CountUnitsOwnedBy(allRebel, 0), 3u);
        ExpectSize("the same field has 0 Empire units",
                   CountUnitsOwnedBy(allRebel, 1), 0u);
    }

    // -----------------------------------------------------------------------
    // 3. BuildFactionSwitchBatch — the bulk SWFOC_ChangeUnitOwner builder.
    // -----------------------------------------------------------------------
    std::printf("\n[BuildFactionSwitchBatch]\n");
    {
        std::vector<UnitInfo> field;
        field.push_back(MakeUnit(0, "Heavy_Tank"));   // Rebel  — switched
        field.push_back(MakeUnit(1, "Ion_Cannon"));   // Empire — left alone
        field.push_back(MakeUnit(0, "Air_Speeder"));  // Rebel  — switched

        // Rebel (0) -> Empire (1): only the 2 Rebel units are in the batch.
        const std::vector<ActionRequest> batch =
            BuildFactionSwitchBatch(field, 0, 1);
        ExpectSize("BATCH SKIPS NON-OWNED: 2 Rebel units -> 2 requests",
                   batch.size(), 2u);

        // Every request is a real SWFOC_ChangeUnitOwner bridge call.
        for (const ActionRequest& req : batch)
        {
            ExpectContains("request lua names the ChangeUnitOwner wire",
                           req.lua, "SWFOC_ChangeUnitOwner(");
            ExpectContains("request lua resolves the unit by type",
                           req.lua, "Find_First_Object(");
            // BATCH RE-OWNS TO NEXT pin — re-owns to EMPIRE (the next
            // faction), never back to REBEL (the current one).
            ExpectContains("BATCH RE-OWNS TO NEXT: new owner is EMPIRE",
                           req.lua, "EMPIRE");
            ExpectAbsent("BATCH RE-OWNS TO NEXT: never re-owns to REBEL",
                         req.lua, "REBEL");
        }

        // The labels carry a 1-based ordinal + the unit type + the direction,
        // so the recent-actions toast distinguishes the requests.
        ExpectContains("request 1 label has ordinal #1", batch[0].label, "#1");
        ExpectContains("request 1 label names the unit type",
                       batch[0].label, "Heavy_Tank");
        ExpectContains("request 1 label names the direction",
                       batch[0].label, "Rebel -> Empire");
        ExpectContains("request 2 label has ordinal #2", batch[1].label, "#2");
        ExpectContains("request 2 label names its own unit type",
                       batch[1].label, "Air_Speeder");

        // BATCH SKIPS NON-OWNED pin — the Empire unit's type appears in NO
        // request; an old form flipping every visible unit would leak it.
        for (const ActionRequest& req : batch)
        {
            ExpectAbsent("BATCH SKIPS NON-OWNED: the Empire unit is untouched",
                         req.lua, "Ion_Cannon");
        }

        // A no-op cycle (fromSlot == toSlot) re-owns nothing.
        ExpectSize("no-op cycle (from == to) yields an empty batch",
                   BuildFactionSwitchBatch(field, 0, 0).size(), 0u);

        // An out-of-range slot yields an empty batch — never a malformed call.
        ExpectSize("out-of-range fromSlot yields an empty batch",
                   BuildFactionSwitchBatch(field, -1, 1).size(), 0u);
        ExpectSize("out-of-range toSlot yields an empty batch",
                   BuildFactionSwitchBatch(field, 0, 9).size(), 0u);

        // A field with no friendly units yields an empty batch.
        std::vector<UnitInfo> noFriendly;
        noFriendly.push_back(MakeUnit(1, "Ion_Cannon"));
        noFriendly.push_back(MakeUnit(2, "Skiff"));
        ExpectSize("no Rebel units -> empty batch",
                   BuildFactionSwitchBatch(noFriendly, 0, 1).size(), 0u);

        // EXPR ESCAPES TYPE NAME pin — a unit type carrying a double-quote is
        // escaped in the Find_First_Object literal; a raw-concat old form
        // would break the Lua string.
        std::vector<UnitInfo> quirky;
        quirky.push_back(MakeUnit(0, "Tank\"X"));
        const std::vector<ActionRequest> quirkyBatch =
            BuildFactionSwitchBatch(quirky, 0, 1);
        ExpectSize("the quirky-type unit still builds a request",
                   quirkyBatch.size(), 1u);
        ExpectContains("EXPR ESCAPES TYPE NAME: the quote is backslash-escaped",
                       quirkyBatch[0].lua, "\\\"");
    }

    // -----------------------------------------------------------------------
    // 4. FactionSwitchPrompt::Arm — confirm-prompt arming.
    // -----------------------------------------------------------------------
    std::printf("\n[FactionSwitchPrompt::Arm]\n");
    {
        FactionSwitchPrompt prompt;
        ExpectTrue("a fresh prompt is Idle",
                   prompt.state() == FactionSwitchPrompt::State::Idle);
        ExpectTrue("a fresh prompt is not armed", !prompt.IsArmed());

        // ARM REQUIRES UNITS pin — arming with 0 affected units is refused.
        ExpectTrue("ARM REQUIRES UNITS: Arm(0, 0) returns false",
                   !prompt.Arm(0, 0));
        ExpectTrue("ARM REQUIRES UNITS: the prompt stays Idle",
                   !prompt.IsArmed());

        // A bad local-player slot is refused.
        ExpectTrue("Arm with a negative slot returns false",
                   !prompt.Arm(-1, 5));
        ExpectTrue("Arm with an over-range slot returns false",
                   !prompt.Arm(3, 5));
        ExpectTrue("the prompt is still Idle after the bad arms",
                   !prompt.IsArmed());

        // A valid arm stages the switch.
        ExpectTrue("Arm(0, 5) returns true", prompt.Arm(0, 5));
        ExpectTrue("the prompt is now Armed", prompt.IsArmed());
        ExpectTrue("fromSlot is the local player's faction",
                   prompt.fromSlot() == 0);
        ExpectTrue("toSlot is the next faction in the cycle",
                   prompt.toSlot() == 1);
        ExpectSize("affectedCount is the snapshot passed to Arm",
                   prompt.affectedCount(), 5u);

        // DOUBLE-TAP DOESN'T SKIP pin — a second F3 while Armed is a no-op; it
        // must not advance the cycle or re-snapshot the count.
        ExpectTrue("DOUBLE-TAP DOESN'T SKIP: a second Arm returns false",
                   !prompt.Arm(0, 99));
        ExpectTrue("DOUBLE-TAP DOESN'T SKIP: toSlot did not advance",
                   prompt.toSlot() == 1);
        ExpectSize("DOUBLE-TAP DOESN'T SKIP: affectedCount unchanged",
                   prompt.affectedCount(), 5u);
    }

    // -----------------------------------------------------------------------
    // 5. FactionSwitchPrompt::Confirm / Cancel — commit + abort.
    // -----------------------------------------------------------------------
    std::printf("\n[FactionSwitchPrompt::Confirm / Cancel]\n");
    {
        // CONFIRM GATES THE BATCH pin — a Confirm with nothing armed is a
        // false no-op, so a stray confirm never dispatches a batch.
        FactionSwitchPrompt idle;
        ExpectTrue("CONFIRM GATES THE BATCH: Confirm() on an Idle prompt "
                   "returns false", !idle.Confirm());
        ExpectTrue("the Idle prompt is unchanged by the stray Confirm",
                   !idle.IsArmed());

        // A confirmed switch returns true and resets to Idle.
        FactionSwitchPrompt prompt;
        ExpectTrue("Arm stages the switch", prompt.Arm(1, 8));
        ExpectTrue("Confirm() of an armed switch returns true",
                   prompt.Confirm());
        ExpectTrue("the prompt is Idle again after Confirm",
                   !prompt.IsArmed());
        // Confirm resets only the State — the slots stay readable so the
        // caller may build the batch before or after the Confirm() call.
        ExpectTrue("fromSlot stays readable after Confirm",
                   prompt.fromSlot() == 1);
        ExpectTrue("toSlot stays readable after Confirm",
                   prompt.toSlot() == 2);
        // A second Confirm has nothing to commit.
        ExpectTrue("a second Confirm returns false", !prompt.Confirm());

        // CANCEL ABORTS THE SWITCH pin — after Cancel a Confirm dispatches
        // nothing; an old form where cancel left the prompt armed would let
        // the batch fire anyway.
        FactionSwitchPrompt cancelled;
        ExpectTrue("Arm stages a switch", cancelled.Arm(0, 12));
        cancelled.Cancel();
        ExpectTrue("Cancel returns the prompt to Idle", !cancelled.IsArmed());
        ExpectTrue("CANCEL ABORTS THE SWITCH: a post-Cancel Confirm "
                   "returns false", !cancelled.Confirm());

        // Cancel on an already-Idle prompt is inert.
        FactionSwitchPrompt inert;
        inert.Cancel();
        ExpectTrue("Cancel on an Idle prompt is inert", !inert.IsArmed());

        // The prompt re-arms after a Confirm — the F3 cycle continues.
        FactionSwitchPrompt cycle;
        ExpectTrue("first arm", cycle.Arm(0, 3));
        ExpectTrue("first confirm", cycle.Confirm());
        ExpectTrue("the prompt re-arms after a completed cycle",
                   cycle.Arm(2, 4));
        ExpectTrue("the re-armed prompt staged the new switch",
                   cycle.toSlot() == 0);
    }

    // -----------------------------------------------------------------------
    // 6. PromptText — the confirm-prompt question.
    // -----------------------------------------------------------------------
    std::printf("\n[PromptText]\n");
    {
        FactionSwitchPrompt prompt;
        ExpectStr("an Idle prompt has no question text",
                  prompt.PromptText(), "");

        prompt.Arm(0, 47);
        const std::string text = prompt.PromptText();
        ExpectContains("the armed prompt names the action",
                       text, "Faction switch");
        ExpectContains("the armed prompt states the affected count",
                       text, "47 unit(s)");
        ExpectContains("the armed prompt names the source faction",
                       text, "from Rebel");
        ExpectContains("the armed prompt names the destination faction",
                       text, "to Empire");

        // A different starting faction yields a different question.
        FactionSwitchPrompt empirePrompt;
        empirePrompt.Arm(1, 3);
        ExpectContains("an Empire-side prompt cycles Empire -> Underworld",
                       empirePrompt.PromptText(),
                       "from Empire to Underworld");

        // A confirmed prompt drops back to no question text.
        prompt.Confirm();
        ExpectStr("a confirmed prompt has no question text again",
                  prompt.PromptText(), "");
    }

    // -----------------------------------------------------------------------
    // 7. Arm cycles toSlot through every faction.
    // -----------------------------------------------------------------------
    std::printf("\n[Arm cycles toSlot]\n");
    {
        FactionSwitchPrompt prompt;
        prompt.Arm(0, 1);
        ExpectTrue("F3 from Rebel stages Empire", prompt.toSlot() == 1);
        prompt.Confirm();
        prompt.Arm(1, 1);
        ExpectTrue("F3 from Empire stages Underworld", prompt.toSlot() == 2);
        prompt.Confirm();
        prompt.Arm(2, 1);
        ExpectTrue("F3 from Underworld stages Rebel", prompt.toSlot() == 0);
    }

    // -----------------------------------------------------------------------
    // 8. Integration — the full F3 -> arm -> confirm -> batch operator cycle.
    // -----------------------------------------------------------------------
    std::printf("\n[integration: F3 -> arm -> confirm -> batch]\n");
    {
        // A tactical field: the local player (Rebel, slot 0) commands three
        // units — two of them share a type — and faces two Empire units.
        std::vector<UnitInfo> field;
        field.push_back(MakeUnit(0, "Rebel_Trooper_Squad"));
        field.push_back(MakeUnit(0, "Rebel_Trooper_Squad"));
        field.push_back(MakeUnit(0, "T2B_Tank"));
        field.push_back(MakeUnit(1, "AT_AT"));
        field.push_back(MakeUnit(1, "TIE_Fighter_Squadron"));

        const int localSlot = 0;  // the operator is Rebel

        // F3 pressed: the glue sizes the prompt from the live unit list.
        const std::size_t affected = CountUnitsOwnedBy(field, localSlot);
        ExpectSize("integration: 3 friendly units are affected", affected, 3u);

        FactionSwitchPrompt prompt;
        ExpectTrue("integration: F3 arms the confirm prompt",
                   prompt.Arm(localSlot, affected));
        ExpectContains("integration: the prompt warns about the 3 units",
                       prompt.PromptText(), "3 unit(s)");

        // COUNT MATCHES BATCH SIZE pin — the number the prompt shows is
        // exactly the number of bridge calls the confirm will dispatch.
        const std::vector<ActionRequest> preview =
            BuildFactionSwitchBatch(field, prompt.fromSlot(),
                                    prompt.toSlot());
        ExpectSize("COUNT MATCHES BATCH SIZE: prompt count == batch size",
                   prompt.affectedCount(), preview.size());

        // The operator confirms; the glue rebuilds the batch from the live
        // unit list and dispatches it.
        ExpectTrue("integration: the operator confirms the switch",
                   prompt.Confirm());
        const std::vector<ActionRequest> batch =
            BuildFactionSwitchBatch(field, prompt.fromSlot(),
                                    prompt.toSlot());
        ExpectSize("integration: the batch has one request per friendly unit",
                   batch.size(), 3u);

        // Every dispatched request re-owns a friendly unit to the Empire.
        for (const ActionRequest& req : batch)
        {
            ExpectContains("integration: every request is a ChangeUnitOwner "
                           "call", req.lua, "return SWFOC_ChangeUnitOwner(");
            ExpectContains("integration: every request re-owns to EMPIRE",
                           req.lua, "EMPIRE");
        }

        // The two Empire units were never touched by the batch.
        for (const ActionRequest& req : batch)
        {
            ExpectAbsent("integration: the AT_AT was left alone",
                         req.lua, "AT_AT");
            ExpectAbsent("integration: the TIE squadron was left alone",
                         req.lua, "TIE_Fighter_Squadron");
        }

        // The prompt is Idle again, ready for the next F3.
        ExpectTrue("integration: the prompt is Idle after the commit",
                   !prompt.IsArmed());
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
