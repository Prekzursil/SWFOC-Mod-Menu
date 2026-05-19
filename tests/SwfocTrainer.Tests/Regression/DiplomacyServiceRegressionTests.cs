using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// Guards the DiplomacyService Lua command shape against a revert of the
/// session 2026-04-07 fix that switched from the broken global form
/// (<c>Make_Ally(Find_Player("EMPIRE"), Find_Player("REBEL"))</c>) to the
/// correct PlayerWrapper instance-method form
/// (<c>local p1 = Find_Player("EMPIRE"); local p2 = Find_Player("REBEL");
/// if p1 and p2 then p1:Make_Ally(p2) end</c>).
/// </summary>
/// <remarks>
/// <para>
/// IDA Pro evidence (session 2026-04-07): the wrappers
/// <c>PlayerWrapper::Make_Ally</c> at RVA <c>0x6046A0</c> and
/// <c>PlayerWrapper::Make_Enemy</c> at RVA <c>0x604780</c> are PlayerObject
/// INSTANCE methods. They are NOT registered as Lua globals. The previous
/// global call form would fail at runtime with
/// <c>"attempt to call global 'Make_Ally' (a nil value)"</c>.
/// </para>
/// <para>
/// See <c>knowledge-base/v5_service_fixes_applied.md</c> section
/// "DiplomacyService - FIXED" for the full IDA evidence trail. If any
/// assertion in this file fires, the diplomacy fix has been reverted and
/// the broken Lua call is being regenerated -- treat as a P0 regression.
/// </para>
/// </remarks>
public sealed class DiplomacyServiceRegressionTests
{
    // ----- Allied -----

    [Fact]
    public void Regression_OldGlobalForm_IsNotGenerated_Allied()
    {
        var lua = DiplomacyService.BuildDiplomacyLuaCommand(
            new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied));

        // The old broken form was a top-level Make_Ally(Find_Player(...), Find_Player(...)).
        // If this assertion fires, the fix has been reverted -- see
        // knowledge-base/v5_service_fixes_applied.md for the IDA evidence.
        lua.Should().NotBeNull();
        lua!.Should().NotStartWith("Make_Ally(");
        lua.Should().NotContain("Make_Ally(Find_Player");
    }

    [Fact]
    public void Regression_NewMethodForm_IsGenerated_Allied()
    {
        var lua = DiplomacyService.BuildDiplomacyLuaCommand(
            new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied));

        lua.Should().NotBeNull();
        lua!.Should().Contain(":Make_Ally(p2)");
        lua.Should().Contain("if p1 and p2 then");
        lua.Should().Contain("local p1 = Find_Player(\"EMPIRE\")");
        lua.Should().Contain("local p2 = Find_Player(\"REBEL\")");
    }

    // ----- Hostile -----

    [Fact]
    public void Regression_OldGlobalForm_IsNotGenerated_Hostile()
    {
        var lua = DiplomacyService.BuildDiplomacyLuaCommand(
            new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Hostile));

        // The old broken form was a top-level Make_Enemy(Find_Player(...), Find_Player(...)).
        lua.Should().NotBeNull();
        lua!.Should().NotStartWith("Make_Enemy(");
        lua.Should().NotContain("Make_Enemy(Find_Player");
    }

    [Fact]
    public void Regression_NewMethodForm_IsGenerated_Hostile()
    {
        var lua = DiplomacyService.BuildDiplomacyLuaCommand(
            new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Hostile));

        lua.Should().NotBeNull();
        lua!.Should().Contain(":Make_Enemy(p2)");
        lua.Should().Contain("if p1 and p2 then");
        lua.Should().Contain("local p1 = Find_Player(\"EMPIRE\")");
        lua.Should().Contain("local p2 = Find_Player(\"REBEL\")");
    }

    // ----- Neutral (intentionally null) -----

    [Fact]
    public void Regression_NeutralRelation_StillReturnsNullSentinel()
    {
        // The fix did not change the contract that Neutral has no Lua API.
        // If this assertion fires, somebody may have wired a no-op Lua command
        // to Neutral that the engine cannot honor -- treat as a contract
        // regression and revisit the design.
        var lua = DiplomacyService.BuildDiplomacyLuaCommand(
            new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Neutral));

        lua.Should().BeNull();
    }
}
