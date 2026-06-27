using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Tests for the internal static Build*LuaCommand methods added in Group A:
/// PlanetManagerService, FleetManagerService, CooldownManagerService, DamageLogService.
/// </summary>
public sealed class V5ServiceLuaCommandBuilderGroupATests
{
    // ========== PlanetManagerService.BuildSetPlanetOwnerLuaCommand ==========

    [Theory]
    [InlineData("CORUSCANT", "EMPIRE", "FindPlanet(\"CORUSCANT\"):Change_Owner(Find_Player(\"EMPIRE\"))")]
    [InlineData("KUAT", "REBEL", "FindPlanet(\"KUAT\"):Change_Owner(Find_Player(\"REBEL\"))")]
    [InlineData("MON_CALAMARI", "UNDERWORLD", "FindPlanet(\"MON_CALAMARI\"):Change_Owner(Find_Player(\"UNDERWORLD\"))")]
    public void BuildSetPlanetOwnerLuaCommand_ProducesCorrectLua(
        string planetId, string newOwner, string expected)
    {
        var lua = PlanetManagerService.BuildSetPlanetOwnerLuaCommand(planetId, newOwner);

        lua.Should().Be(expected);
    }

    [Fact]
    public void BuildSetPlanetOwnerLuaCommand_ContainsFindPlanetAndChangeOwner()
    {
        var lua = PlanetManagerService.BuildSetPlanetOwnerLuaCommand("KASHYYYK", "REBEL");

        lua.Should().Contain("FindPlanet(\"KASHYYYK\")");
        lua.Should().Contain("):Change_Owner(", "Lua 5.0 colon method call syntax is required");
        lua.Should().Contain("Find_Player(\"REBEL\")");
    }

    [Fact]
    public void BuildSetPlanetOwnerLuaCommand_NullPlanetId_ThrowsArgumentNull()
    {
        var act = () => PlanetManagerService.BuildSetPlanetOwnerLuaCommand(null!, "EMPIRE");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildSetPlanetOwnerLuaCommand_NullNewOwner_ThrowsArgumentNull()
    {
        var act = () => PlanetManagerService.BuildSetPlanetOwnerLuaCommand("CORUSCANT", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== FleetManagerService.BuildAssembleFleetLuaCommand ==========

    [Theory]
    [InlineData("EMPIRE", "CORUSCANT", "Assemble_Fleet(Find_Player(\"EMPIRE\"), FindPlanet(\"CORUSCANT\"))")]
    [InlineData("REBEL", "MON_CALAMARI", "Assemble_Fleet(Find_Player(\"REBEL\"), FindPlanet(\"MON_CALAMARI\"))")]
    [InlineData("UNDERWORLD", "KUAT", "Assemble_Fleet(Find_Player(\"UNDERWORLD\"), FindPlanet(\"KUAT\"))")]
    public void BuildAssembleFleetLuaCommand_ProducesCorrectLua(
        string faction, string planet, string expected)
    {
        var lua = FleetManagerService.BuildAssembleFleetLuaCommand(faction, planet);

        lua.Should().Be(expected);
    }

    [Fact]
    public void BuildAssembleFleetLuaCommand_ContainsAssembleFleetAndFindPlayer()
    {
        var lua = FleetManagerService.BuildAssembleFleetLuaCommand("EMPIRE", "FONDOR");

        lua.Should().StartWith("Assemble_Fleet(");
        lua.Should().Contain("Find_Player(\"EMPIRE\")");
        lua.Should().Contain("FindPlanet(\"FONDOR\")");
    }

    [Fact]
    public void BuildAssembleFleetLuaCommand_NullFaction_ThrowsArgumentNull()
    {
        var act = () => FleetManagerService.BuildAssembleFleetLuaCommand(null!, "CORUSCANT");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildAssembleFleetLuaCommand_NullPlanet_ThrowsArgumentNull()
    {
        var act = () => FleetManagerService.BuildAssembleFleetLuaCommand("EMPIRE", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== CooldownManagerService.BuildCooldownResetLuaCommand ==========

    [Fact]
    public void BuildCooldownResetLuaCommand_SelectedUnit_ProducesResetAbilityCounter()
    {
        var request = new CooldownResetRequest(CooldownResetScope.SelectedUnit, "AT_AT");

        var lua = CooldownManagerService.BuildCooldownResetLuaCommand(request);

        lua.Should().Be("Find_First_Object(\"AT_AT\"):Reset_Ability_Counter()");
    }

    [Fact]
    public void BuildCooldownResetLuaCommand_SelectedUnit_ContainsFindFirstObject()
    {
        var request = new CooldownResetRequest(CooldownResetScope.SelectedUnit, "STAR_DESTROYER");

        var lua = CooldownManagerService.BuildCooldownResetLuaCommand(request);

        lua.Should().Contain("Find_First_Object(\"STAR_DESTROYER\")");
        lua.Should().Contain("):Reset_Ability_Counter()", "Lua 5.0 colon method call syntax is required");
    }

    [Fact]
    public void BuildCooldownResetLuaCommand_AllPlayerUnits_ProducesComment()
    {
        var request = new CooldownResetRequest(CooldownResetScope.AllPlayerUnits, null);

        var lua = CooldownManagerService.BuildCooldownResetLuaCommand(request);

        lua.Should().Be("-- Reset all player unit cooldowns (requires iteration)");
    }

    [Fact]
    public void BuildCooldownResetLuaCommand_AllPlayerUnits_StartsWithComment()
    {
        var request = new CooldownResetRequest(CooldownResetScope.AllPlayerUnits, null);

        var lua = CooldownManagerService.BuildCooldownResetLuaCommand(request);

        lua.Should().StartWith("--");
    }

    [Fact]
    public void BuildCooldownResetLuaCommand_UnknownScope_ThrowsArgumentOutOfRange()
    {
        var request = new CooldownResetRequest((CooldownResetScope)999, null);

        var act = () => CooldownManagerService.BuildCooldownResetLuaCommand(request);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildCooldownResetLuaCommand_NullRequest_ThrowsArgumentNull()
    {
        var act = () => CooldownManagerService.BuildCooldownResetLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== DamageLogService.BuildEventControlLuaCommand ==========

    [Fact]
    public void BuildEventControlLuaCommand_Enable_ProducesEventControl1()
    {
        var lua = DamageLogService.BuildEventControlLuaCommand(true);

        lua.Should().Be("SWFOC_EventControl(1)");
    }

    [Fact]
    public void BuildEventControlLuaCommand_Disable_ProducesEventControl0()
    {
        var lua = DamageLogService.BuildEventControlLuaCommand(false);

        lua.Should().Be("SWFOC_EventControl(0)");
    }

    [Fact]
    public void BuildEventControlLuaCommand_Enable_ContainsFunctionName()
    {
        var lua = DamageLogService.BuildEventControlLuaCommand(true);

        lua.Should().Contain("SWFOC_EventControl");
    }

    [Fact]
    public void BuildEventControlLuaCommand_Disable_ContainsFunctionName()
    {
        var lua = DamageLogService.BuildEventControlLuaCommand(false);

        lua.Should().Contain("SWFOC_EventControl");
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void BuildEventControlLuaCommand_BoolToArgument_ProducesCorrectValue(
        bool enable, string expectedArg)
    {
        var lua = DamageLogService.BuildEventControlLuaCommand(enable);

        lua.Should().Contain($"SWFOC_EventControl({expectedArg})");
    }
}
