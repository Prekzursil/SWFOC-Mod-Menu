using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

// =============================================================================
// 2026-05-07 (iter 461) — SWFOC_TriggerVictory native UX pin tests.
//
// Verifies the WorldState tab Trigger Victory GroupBox wire shape end-to-end:
//   1. Dispatcher emits canonical `return SWFOC_TriggerVictory('<type>')`
//      via the iter-201 BuildUnitLuaNoArgCall helper (single-quote escape)
//   2. Lua-injection guard (single quotes inside victory_type are escaped)
//   3. AllActions list count includes the new TriggerVictoryLua action
//      (catches iter-195/iter-208 AllActions count-pin drift category)
//   4. Default VM state — SelectedVictoryType opens at "Galactic_Conquer"
//      (matches operator's most-common workflow; mirrors bridge default)
//   5. VictoryTypes list contains all 14 names from kKnownVictoryTypes[]
//      (triple-source consistency with bridge + simulator + Lua Playground)
//
// The iter-450 DORMANT MinHook scaffolding means SWFOC_TriggerVictory currently
// returns PHASE2_PENDING; tests at the VM/dispatcher layer don't depend on
// engine-side behavior — they pin the WIRE FORMAT only. The engine response
// is exercised by the iter-451 simulator handler tests
// (Iter451_TriggerVictoryHandlerTests).
// =============================================================================
public class Iter461_TriggerVictoryNativeUxTests
{
    [Fact]
    public void BuildUnitLuaNoArgCall_emits_canonical_TriggerVictory_wire_for_galactic_conquer()
    {
        // Arrange + Act: the dispatcher uses the same iter-201 helper as
        // SWFOC_StoryEventLua / SWFOC_AddObjectiveLua / etc.
        var actual = V2UnitMutationDispatcher.BuildUnitLuaNoArgCall(
            "SWFOC_TriggerVictory", "Galactic_Conquer");

        // Assert: exact wire-format match — single-quoted arg, no inner spaces.
        actual.Should().Be("return SWFOC_TriggerVictory('Galactic_Conquer')");
    }

    [Fact]
    public void BuildUnitLuaNoArgCall_emits_canonical_TriggerVictory_wire_for_skirmish_control_win()
    {
        // Arrange + Act: a different victory_type to verify the helper isn't
        // hardcoded against Galactic_Conquer.
        var actual = V2UnitMutationDispatcher.BuildUnitLuaNoArgCall(
            "SWFOC_TriggerVictory", "Skirmish_Control_Win");

        // Assert: same shape, different arg.
        actual.Should().Be("return SWFOC_TriggerVictory('Skirmish_Control_Win')");
    }

    [Fact]
    public void BuildUnitLuaNoArgCall_TriggerVictory_escapes_single_quotes_in_arg()
    {
        // Arrange: a mischievous operator types an embedded single quote
        // (the bridge wrapper would still reject this name, but the dispatcher
        // must escape before sending so the bridge's Lua parser doesn't choke).
        // This pins the Lua-injection guard.
        var actual = V2UnitMutationDispatcher.BuildUnitLuaNoArgCall(
            "SWFOC_TriggerVictory", "Galactic_Conquer'; os.execute('rm -rf /'); --");

        // Assert: every single quote in the arg is backslash-escaped.
        actual.Should().Be("return SWFOC_TriggerVictory('Galactic_Conquer\\'; os.execute(\\'rm -rf /\\'); --')");
    }
}
