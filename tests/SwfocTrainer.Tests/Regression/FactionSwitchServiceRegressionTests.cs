using FluentAssertions;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// Guards the FactionSwitchService Lua command shape against reverts.
/// </summary>
/// <remarks>
/// <para>
/// Timeline:
/// <list type="number">
/// <item>Pre-2026-04-07: generated <c>set_context_allegiance(Find_Player(...))</c>,
/// which nil-called a non-existent API.</item>
/// <item>2026-04-07: replaced with <c>error("FactionSwitch BLOCKED-NEEDS-MEMORY ...")</c>
/// marker after IDA confirmed no Lua API exists for human-player switching.</item>
/// <item>2026-04-10 (B4 resolution): replaced the error marker with
/// <c>return SWFOC_SetHumanPlayer(slot)</c> after IDA verified
/// <c>PlayerListClass::Switch_Sides</c> at RVA 0x297E80 as the canonical
/// engine setter (see ledger entry <c>rva_player_list_switch_sides</c>).
/// The bridge helper wraps Switch_Sides in a bounded rotation loop.</item>
/// <item>2026-04-11 (galactic-mode fix): replaced v1 with
/// <c>SWFOC_SetHumanPlayer_v3(slot)</c> after a live-game galactic test
/// exposed that Switch_Sides is silently guarded out in mode 3.
/// v2 is mode-agnostic — it does a manual +0x62 sweep and calls
/// the subsystem refresh path directly. See
/// <c>knowledge-base/faction_switch_full_anatomy_2026-04-11.md</c>.</item>
/// </list>
/// </para>
/// <para>
/// The regression pair below is red-on-old-shape / green-on-new-shape for
/// BOTH the pre-2026-04-07 broken form AND the 2026-04-07 blocked marker.
/// A future "simplification" that re-introduces either shape must fail here.
/// </para>
/// </remarks>
public sealed class FactionSwitchServiceRegressionTests
{
    [Fact]
    public void Regression_OldSetContextAllegianceCall_NotGenerated()
    {
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand("REBEL");

        // None of the original broken-API names should appear.
        lua.Should().NotContain("set_context_allegiance(");
        lua.Should().NotContain("Set_Affiliation(");
        lua.Should().NotContain("Set_Player(");
        lua.Should().NotContain("SetLocalPlayer(");
        lua.Should().NotContain("Take_Control(");
        lua.Should().NotContain("SwitchFaction(");
    }

    [Fact]
    public void Regression_BlockedMemoryMarker_NotGenerated()
    {
        // The 2026-04-10 B4 resolution replaced the BLOCKED-NEEDS-MEMORY
        // error marker with a real SWFOC_SetHumanPlayer call. If this test
        // fails, the service has regressed to the interim blocked form.
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand("REBEL");

        lua.Should().NotContain("BLOCKED-NEEDS-MEMORY");
        lua.Should().NotStartWith("error(\"FactionSwitch BLOCKED");
    }

    [Fact]
    public void Regression_SwfocSetHumanPlayerCall_IsGenerated()
    {
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand("REBEL");

        // 2026-04-11: must be the v2 shape. The v1 shape was found to be a
        // silent no-op in galactic mode because Switch_Sides is guarded out
        // by sub_14028AF60 when game-mode type == 3. Any regression to v1
        // reintroduces the split-brain the live test exposed on 2026-04-10.
        lua.Should().Contain("SWFOC_SetHumanPlayer_v3(");
        lua.Should().StartWith("return SWFOC_SetHumanPlayer_v3(");
    }

    [Fact]
    public void Regression_VersionOneShape_NotGenerated()
    {
        // 2026-04-11 regression guard. The v1 helper (without the _v2 suffix)
        // is still registered in the bridge as a diagnostic fallback, but
        // FactionSwitchService MUST emit the v2 call. The old v1 return
        // `return SWFOC_SetHumanPlayer(N)` was always followed by a `(`, so
        // scanning for that exact prefix catches any revert.
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand("REBEL");

        // v1 had no `_v2` segment before the `(`, so the old shape
        // `return SWFOC_SetHumanPlayer(0)` would match a NegatedStartWith
        // that demands the v2 suffix.
        lua.Should().NotStartWith("return SWFOC_SetHumanPlayer(");
        // Double-check: the literal v1 argument wrapper should not appear.
        lua.Should().NotContain("SWFOC_SetHumanPlayer(0)");
    }

    [Theory]
    [InlineData("REBEL", 0)]
    [InlineData("REBELS", 0)]
    [InlineData("EMPIRE", 1)]
    [InlineData("GALACTIC_EMPIRE", 1)]
    [InlineData("UNDERWORLD", 2)]
    [InlineData("ZANN", 2)]
    public void FactionNameToSlot_MapsCanonicalFactions(string faction, int expectedSlot)
    {
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand(faction);
        lua.Should().Contain($"SWFOC_SetHumanPlayer_v3({expectedSlot})");
    }

    [Fact]
    public void UnknownFaction_ProducesClearError()
    {
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand("MANDALORIAN");

        // Unknown faction names fall through to a clear error rather than
        // silently mapping to slot -1 (which would hit the bridge's
        // out-of-range guard without the faction-name context).
        lua.Should().StartWith("error(\"FactionSwitch: unknown faction");
        lua.Should().Contain("MANDALORIAN");
        lua.Should().NotContain("SWFOC_SetHumanPlayer_v3(");
    }
}
