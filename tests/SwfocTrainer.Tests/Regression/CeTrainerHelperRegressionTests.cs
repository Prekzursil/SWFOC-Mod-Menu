using FluentAssertions;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// Guards the GodModeService and OneHitKillService Lua command shapes against
/// a revert to the old Lua-only approach used by the legacy Cheat Engine
/// trainer ports.
/// </summary>
/// <remarks>
/// <para>
/// The CE trainer historically called <c>Make_Invulnerable</c> and
/// <c>Set_Cannot_Be_Killed</c> directly from Lua to implement god mode. Per
/// Q9 (<c>knowledge-base/verified_facts.json::fact_make_invulnerable_hardpoint_propagation</c>),
/// hardpoint propagation through the pure-Lua path is UNVERIFIED -- the Lua
/// metatable does not expose hardpoint state, so capital ships kept firing
/// from individual hardpoints even with the global invuln behavior attached.
/// </para>
/// <para>
/// The new approach (session 2026-04-07): the C++ bridge ships native
/// <c>SWFOC_GodMode</c> / <c>SWFOC_OneHitKill</c> helpers backed by MinHook
/// detours on the engine's damage / SetHP path. These run inside the game
/// process and reliably block damage at the C++ layer regardless of the Lua
/// metatable shape. <c>GodModeService</c> and <c>OneHitKillService</c> emit
/// <c>return SWFOC_GodMode(0|1)</c> and <c>return SWFOC_OneHitKill(0|1)</c>
/// respectively to invoke those helpers.
/// </para>
/// <para>
/// Similarly the CE trainer used Lua's <c>Take_Damage</c> for one-hit kill,
/// but Q9 confirmed <c>Take_Damage</c> is a no-op at the Lua layer (the
/// engine accepts the call and discards it). The C++ helper hooks the actual
/// damage application path, which is why it works.
/// </para>
/// <para>
/// If any assertion in this file fires, the bridge-helper rewrite has been
/// reverted to a pure-Lua call form -- god mode will be unreliable on capital
/// ships and one-hit kill will silently no-op.
/// </para>
/// </remarks>
public sealed class CeTrainerHelperRegressionTests
{
    // ----- GodMode -----

    [Fact]
    public void Regression_GodMode_UsesSwfocHelperNotOldLuaCall()
    {
        var lua = GodModeService.BuildGodModeLuaCommand(true);

        // The old approach called Make_Invulnerable via Lua which propagates
        // hardpoints unreliably. The new approach uses the native SWFOC_GodMode
        // helper which installs a MinHook detour on SetHP.
        lua.Should().Contain("SWFOC_GodMode(1)");
        lua.Should().NotContain("Make_Invulnerable");
        lua.Should().NotContain("Set_Cannot_Be_Killed");
    }

    [Fact]
    public void Regression_GodMode_DisableUsesSwfocHelperNotOldLuaCall()
    {
        var lua = GodModeService.BuildGodModeLuaCommand(false);

        lua.Should().Contain("SWFOC_GodMode(0)");
        lua.Should().NotContain("Make_Invulnerable");
        lua.Should().NotContain("Set_Cannot_Be_Killed");
    }

    [Fact]
    public void Regression_GodMode_AlwaysUsesReturnPrefix()
    {
        // The bridge intercept catalog only short-circuits commands that
        // start with "return SWFOC_*". Dropping the return prefix would
        // route the call through the generic Lua execution path and lose
        // the helper's status string.
        GodModeService.BuildGodModeLuaCommand(true).Should().StartWith("return SWFOC_");
        GodModeService.BuildGodModeLuaCommand(false).Should().StartWith("return SWFOC_");
    }

    // ----- OneHitKill -----

    [Fact]
    public void Regression_OneHitKill_UsesSwfocHelperNotOldLuaCall()
    {
        var lua = OneHitKillService.BuildOneHitKillLuaCommand(true);

        // Per Q9, Lua Take_Damage is a no-op -- the new helper hooks the
        // actual damage application path inside the engine.
        lua.Should().Contain("SWFOC_OneHitKill(1)");
        lua.Should().NotContain("Take_Damage");
        lua.Should().NotContain("Apply_Damage");
        lua.Should().NotContain("Kill_Object");
    }

    [Fact]
    public void Regression_OneHitKill_DisableUsesSwfocHelperNotOldLuaCall()
    {
        var lua = OneHitKillService.BuildOneHitKillLuaCommand(false);

        lua.Should().Contain("SWFOC_OneHitKill(0)");
        lua.Should().NotContain("Take_Damage");
        lua.Should().NotContain("Apply_Damage");
        lua.Should().NotContain("Kill_Object");
    }

    [Fact]
    public void Regression_OneHitKill_AlwaysUsesReturnPrefix()
    {
        OneHitKillService.BuildOneHitKillLuaCommand(true).Should().StartWith("return SWFOC_");
        OneHitKillService.BuildOneHitKillLuaCommand(false).Should().StartWith("return SWFOC_");
    }
}
