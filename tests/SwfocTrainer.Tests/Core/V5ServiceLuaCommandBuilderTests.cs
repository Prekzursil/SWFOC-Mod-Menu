using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Tests for the internal static Build*LuaCommand methods on each v5 service.
/// These verify that the correct Lua command string is produced for the bridge protocol.
/// </summary>
public sealed class V5ServiceLuaCommandBuilderTests
{
    // ========== EnhancedSpawnService.BuildSpawnLuaCommand ==========

    [Theory]
    [InlineData(SpawnMode.Tactical, "EMPIRE", "AT_AT")]
    [InlineData(SpawnMode.Tactical, "REBEL", "X_WING")]
    [InlineData(SpawnMode.Tactical, "UNDERWORLD", "RANCOR")]
    public void BuildSpawnLuaCommand_Tactical_ProducesSpawnUnit(
        SpawnMode mode, string faction, string unitId)
    {
        var request = new EnhancedSpawnRequest(
            unitId, faction, mode, 1, SpawnPositionKind.AtCamera, null, false, false);

        var lua = EnhancedSpawnService.BuildSpawnLuaCommand(request);

        lua.Should().StartWith("Spawn_Unit(");
        lua.Should().Contain($"Find_Player(\"{faction}\")");
        lua.Should().Contain($"Find_Object_Type(\"{unitId}\")");
        lua.Should().Contain("Create_Position(0,0,0)");
    }

    [Fact]
    public void BuildSpawnLuaCommand_Reinforcement_ProducesReinforceUnit()
    {
        var request = new EnhancedSpawnRequest(
            "AT_ST", "EMPIRE", SpawnMode.Reinforcement, 2,
            SpawnPositionKind.AtCamera, null, false, false);

        var lua = EnhancedSpawnService.BuildSpawnLuaCommand(request);

        lua.Should().StartWith("Reinforce_Unit(");
        lua.Should().Contain("Find_Player(\"EMPIRE\")");
        lua.Should().Contain("Find_Object_Type(\"AT_ST\")");
        lua.Should().Contain("Create_Position(0,0,0)");
    }

    [Theory]
    [InlineData("CORUSCANT")]
    [InlineData("KUAT")]
    [InlineData("MON_CALAMARI")]
    public void BuildSpawnLuaCommand_Galactic_WithPlanet_ProducesGalacticSpawn(string planet)
    {
        var request = new EnhancedSpawnRequest(
            "STAR_DESTROYER", "EMPIRE", SpawnMode.GalacticPersistent, 1,
            SpawnPositionKind.AtCamera, planet, false, false);

        var lua = EnhancedSpawnService.BuildSpawnLuaCommand(request);

        lua.Should().StartWith("Galactic_Spawn_Unit(");
        lua.Should().Contain("Find_Player(\"EMPIRE\")");
        lua.Should().Contain("Find_Object_Type(\"STAR_DESTROYER\")");
        lua.Should().Contain($"FindPlanet(\"{planet}\")");
    }

    [Fact]
    public void BuildSpawnLuaCommand_Galactic_NullPlanet_DefaultsToCoruscant()
    {
        var request = new EnhancedSpawnRequest(
            "STAR_DESTROYER", "EMPIRE", SpawnMode.GalacticPersistent, 1,
            SpawnPositionKind.AtCamera, null, false, false);

        var lua = EnhancedSpawnService.BuildSpawnLuaCommand(request);

        lua.Should().Contain("FindPlanet(\"CORUSCANT\")");
    }

    [Fact]
    public void BuildSpawnLuaCommand_UnknownMode_ThrowsArgumentOutOfRange()
    {
        var request = new EnhancedSpawnRequest(
            "AT_AT", "EMPIRE", (SpawnMode)999, 1,
            SpawnPositionKind.AtCamera, null, false, false);

        var act = () => EnhancedSpawnService.BuildSpawnLuaCommand(request);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildSpawnLuaCommand_NullRequest_ThrowsArgumentNull()
    {
        var act = () => EnhancedSpawnService.BuildSpawnLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== CameraDirectorService.BuildCameraLuaCommand ==========

    [Theory]
    [InlineData("zoom", null, "Zoom_Camera(1.0)")]
    [InlineData("zoom", "3.5", "Zoom_Camera(3.5)")]
    [InlineData("rotate", null, "Rotate_Camera_By(0)")]
    [InlineData("rotate", "90", "Rotate_Camera_By(90)")]
    [InlineData("point_at", null, "Point_Camera_At(selectedUnit)")]
    [InlineData("scroll_to", null, "Scroll_Camera_To(0,0,0)")]
    [InlineData("scroll_to", "100,200,0", "Scroll_Camera_To(100,200,0)")]
    [InlineData("letterbox_on", null, "Letter_Box_On()")]
    [InlineData("letterbox_off", null, "Letter_Box_Off()")]
    [InlineData("freeze", null, "Game_Set_Speed(0)")]
    [InlineData("unfreeze", null, "Game_Set_Speed(1)")]
    public void BuildCameraLuaCommand_KnownCommand_ProducesExpectedLua(
        string command, string? parameter, string expected)
    {
        var lua = CameraDirectorService.BuildCameraLuaCommand(command, parameter);

        lua.Should().Be(expected);
    }

    [Fact]
    public void BuildCameraLuaCommand_UnknownCommand_ReturnsNull()
    {
        var lua = CameraDirectorService.BuildCameraLuaCommand("explode", null);

        lua.Should().BeNull();
    }

    [Fact]
    public void BuildCameraLuaCommand_CaseInsensitive_ReturnsLua()
    {
        var lua = CameraDirectorService.BuildCameraLuaCommand("ZOOM", "2.0");

        lua.Should().Be("Zoom_Camera(2.0)");
    }

    [Fact]
    public void BuildCameraLuaCommand_NullCommand_ThrowsArgumentNull()
    {
        var act = () => CameraDirectorService.BuildCameraLuaCommand(null!, null);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== StoryEventService.BuildStoryEventLuaCommand ==========

    [Theory]
    [InlineData("DEATH_STAR_DESTROYED", "Story_Event(\"DEATH_STAR_DESTROYED\")")]
    [InlineData("ENDOR_SHIELD_DOWN", "Story_Event(\"ENDOR_SHIELD_DOWN\")")]
    [InlineData("REBEL_FLEET_ARRIVES", "Story_Event(\"REBEL_FLEET_ARRIVES\")")]
    public void BuildStoryEventLuaCommand_ProducesCorrectLua(
        string eventId, string expected)
    {
        var lua = StoryEventService.BuildStoryEventLuaCommand(eventId);

        lua.Should().Be(expected);
    }

    [Fact]
    public void BuildStoryEventLuaCommand_NullEventId_ThrowsArgumentNull()
    {
        var act = () => StoryEventService.BuildStoryEventLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== DiplomacyService.BuildDiplomacyLuaCommand ==========

    [Fact]
    public void BuildDiplomacyLuaCommand_Allied_ProducesMakeAlly()
    {
        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied);

        var lua = DiplomacyService.BuildDiplomacyLuaCommand(state);

        // Phase 3 IDA correction (2026-04-07): Make_Ally is a PlayerWrapper instance method
        // (RVA 0x6046A0). Previous global form would error at runtime.
        lua.Should().Be(
            "local p1 = Find_Player(\"EMPIRE\"); local p2 = Find_Player(\"REBEL\"); " +
            "if p1 and p2 then p1:Make_Ally(p2) end");
    }

    [Fact]
    public void BuildDiplomacyLuaCommand_Hostile_ProducesMakeEnemy()
    {
        var state = new DiplomacyState("EMPIRE", "UNDERWORLD", DiplomacyRelation.Hostile);

        var lua = DiplomacyService.BuildDiplomacyLuaCommand(state);

        // Phase 3 IDA correction (2026-04-07): Make_Enemy is a PlayerWrapper instance method
        // (RVA 0x604780). Same engine function as Make_Ally with ally_flag=1.
        lua.Should().Be(
            "local p1 = Find_Player(\"EMPIRE\"); local p2 = Find_Player(\"UNDERWORLD\"); " +
            "if p1 and p2 then p1:Make_Enemy(p2) end");
    }

    [Fact]
    public void BuildDiplomacyLuaCommand_Neutral_ReturnsNull()
    {
        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Neutral);

        var lua = DiplomacyService.BuildDiplomacyLuaCommand(state);

        lua.Should().BeNull();
    }

    [Fact]
    public void BuildDiplomacyLuaCommand_UnknownRelation_ReturnsNull()
    {
        var state = new DiplomacyState("EMPIRE", "REBEL", (DiplomacyRelation)99);

        var lua = DiplomacyService.BuildDiplomacyLuaCommand(state);

        lua.Should().BeNull();
    }

    [Fact]
    public void BuildDiplomacyLuaCommand_NullState_ThrowsArgumentNull()
    {
        var act = () => DiplomacyService.BuildDiplomacyLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("REBEL", "UNDERWORLD", DiplomacyRelation.Allied)]
    [InlineData("UNDERWORLD", "EMPIRE", DiplomacyRelation.Hostile)]
    public void BuildDiplomacyLuaCommand_VariousFactions_ContainsBothFactions(
        string faction1, string faction2, DiplomacyRelation relation)
    {
        var state = new DiplomacyState(faction1, faction2, relation);

        var lua = DiplomacyService.BuildDiplomacyLuaCommand(state);

        lua.Should().NotBeNull();
        lua.Should().Contain($"Find_Player(\"{faction1}\")");
        lua.Should().Contain($"Find_Player(\"{faction2}\")");
    }

    // ========== CorruptionService.BuildCorruptionLuaCommand ==========

    [Theory]
    [InlineData(CorruptionType.Racketeering, "CORUSCANT", "Story_Event(\"CORRUPTION_RACKETEERING_CORUSCANT\")")]
    [InlineData(CorruptionType.Bribery, "KUAT", "Story_Event(\"CORRUPTION_BRIBERY_KUAT\")")]
    [InlineData(CorruptionType.Piracy, "MON_CALAMARI", "Story_Event(\"CORRUPTION_PIRACY_MON_CALAMARI\")")]
    [InlineData(CorruptionType.Kidnapping, "KASHYYYK", "Story_Event(\"CORRUPTION_KIDNAPPING_KASHYYYK\")")]
    [InlineData(CorruptionType.Sabotage, "FONDOR", "Story_Event(\"CORRUPTION_SABOTAGE_FONDOR\")")]
    public void BuildCorruptionLuaCommand_ValidType_ProducesStoryEvent(
        CorruptionType type, string planetId, string expected)
    {
        var entry = new CorruptionEntry(planetId, type, 1);

        var lua = CorruptionService.BuildCorruptionLuaCommand(entry);

        lua.Should().Be(expected);
    }

    [Fact]
    public void BuildCorruptionLuaCommand_NullEntry_ThrowsArgumentNull()
    {
        var act = () => CorruptionService.BuildCorruptionLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== CorruptionService.BuildRemoveCorruptionLuaCommand ==========

    [Theory]
    [InlineData("CORUSCANT", "Story_Event(\"REMOVE_CORRUPTION_CORUSCANT\")")]
    [InlineData("KUAT", "Story_Event(\"REMOVE_CORRUPTION_KUAT\")")]
    [InlineData("Mon_Calamari", "Story_Event(\"REMOVE_CORRUPTION_MON_CALAMARI\")")]
    public void BuildRemoveCorruptionLuaCommand_ProducesCorrectLua(
        string planetId, string expected)
    {
        var lua = CorruptionService.BuildRemoveCorruptionLuaCommand(planetId);

        lua.Should().Be(expected);
    }

    [Fact]
    public void BuildRemoveCorruptionLuaCommand_NullPlanetId_ThrowsArgumentNull()
    {
        var act = () => CorruptionService.BuildRemoveCorruptionLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== OwnershipTransferService.BuildOwnershipLuaCommand ==========

    [Theory]
    [InlineData("unit_42", "REBEL", "Find_First_Object(\"unit_42\"):Change_Owner(Find_Player(\"REBEL\"))")]
    [InlineData("AT_AT", "EMPIRE", "Find_First_Object(\"AT_AT\"):Change_Owner(Find_Player(\"EMPIRE\"))")]
    [InlineData("CORUSCANT", "UNDERWORLD", "Find_First_Object(\"CORUSCANT\"):Change_Owner(Find_Player(\"UNDERWORLD\"))")]
    public void BuildOwnershipLuaCommand_ProducesCorrectLua(
        string targetId, string newOwner, string expected)
    {
        var lua = OwnershipTransferService.BuildOwnershipLuaCommand(targetId, newOwner);

        lua.Should().Be(expected);
    }

    [Fact]
    public void BuildOwnershipLuaCommand_ContainsColonSyntax()
    {
        var lua = OwnershipTransferService.BuildOwnershipLuaCommand("unit_1", "REBEL");

        lua.Should().Contain("):Change_Owner(", "Lua 5.0 colon method call syntax is required");
    }

    [Fact]
    public void BuildOwnershipLuaCommand_NullTargetId_ThrowsArgumentNull()
    {
        var act = () => OwnershipTransferService.BuildOwnershipLuaCommand(null!, "REBEL");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildOwnershipLuaCommand_NullNewOwner_ThrowsArgumentNull()
    {
        var act = () => OwnershipTransferService.BuildOwnershipLuaCommand("unit_1", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== AiControlService.BuildAiLuaCommand ==========

    [Fact]
    public void BuildAiLuaCommand_SuspendAll_ProducesSuspendAI()
    {
        var request = new AiControlRequest(
            AiControlAction.SuspendAll, 60, null, null, null);

        var lua = AiControlService.BuildAiLuaCommand(request);

        lua.Should().Be("Suspend_AI(60)");
    }

    [Fact]
    public void BuildAiLuaCommand_SuspendAll_NullSeconds_UsesDefault9999()
    {
        var request = new AiControlRequest(
            AiControlAction.SuspendAll, null, null, null, null);

        var lua = AiControlService.BuildAiLuaCommand(request);

        lua.Should().Be("Suspend_AI(9999)");
    }

    [Fact]
    public void BuildAiLuaCommand_ResumeAll_ProducesSuspendAIZero()
    {
        var request = new AiControlRequest(
            AiControlAction.ResumeAll, null, null, null, null);

        var lua = AiControlService.BuildAiLuaCommand(request);

        lua.Should().Be("Suspend_AI(0)");
    }

    [Fact]
    public void BuildAiLuaCommand_PreventUsage_ProducesCommentedWarning()
    {
        var request = new AiControlRequest(
            AiControlAction.PreventUsage, null, "UNIT_42", null, null);

        var lua = AiControlService.BuildAiLuaCommand(request);

        lua.Should().Contain("WARNING");
        lua.Should().Contain("Prevent_AI_Usage(true)");
    }

    [Fact]
    public void BuildAiLuaCommand_SetDifficulty_ProducesComment()
    {
        var request = new AiControlRequest(
            AiControlAction.SetDifficulty, null, null, "EMPIRE", 3);

        var lua = AiControlService.BuildAiLuaCommand(request);

        lua.Should().Contain("EMPIRE");
        lua.Should().StartWith("--");
    }

    [Fact]
    public void BuildAiLuaCommand_SetDifficulty_NullFaction_UsesUnknown()
    {
        var request = new AiControlRequest(
            AiControlAction.SetDifficulty, null, null, null, null);

        var lua = AiControlService.BuildAiLuaCommand(request);

        lua.Should().Contain("unknown");
    }

    [Fact]
    public void BuildAiLuaCommand_UnknownAction_ThrowsArgumentOutOfRange()
    {
        var request = new AiControlRequest(
            (AiControlAction)999, null, null, null, null);

        var act = () => AiControlService.BuildAiLuaCommand(request);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildAiLuaCommand_NullRequest_ThrowsArgumentNull()
    {
        var act = () => AiControlService.BuildAiLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== FactionSwitchService.BuildFactionSwitchLuaCommand ==========

    // Phase 3 IDA investigation (2026-04-07): the previous implementation generated
    // set_context_allegiance(Find_Player(...)) but no such Lua API exists in the binary.
    // Searched for set_context_allegiance, Set_Player, SetLocalPlayer, Set_Faction,
    // Set_Affiliation, Take_Control, SwitchFaction, Switch_Player, etc. — all nil.
    // Status 2026-04-10: RESOLVED. IDA B4 investigation found PlayerListClass::Switch_Sides
    // at RVA 0x297E80 as the canonical setter. The bridge helper SWFOC_SetHumanPlayer
    // wraps Switch_Sides in a bounded rotation loop. FactionSwitchService now emits
    // `return SWFOC_SetHumanPlayer_v3(slot)` with faction-name-to-slot mapping.
    // Status 2026-04-11: REVISED. Live-game galactic test exposed that Switch_Sides
    // is silently guarded out in game mode 3. The bridge now exposes
    // SWFOC_SetHumanPlayer_v3 which does a manual +0x62 sweep + subsystem refresh
    // unconditionally. FactionSwitchService now emits the v2 call.

    [Theory]
    [InlineData("REBEL", 0)]
    [InlineData("EMPIRE", 1)]
    [InlineData("UNDERWORLD", 2)]
    public void BuildFactionSwitchLuaCommand_InvokesSwfocSetHumanPlayer(string targetFaction, int expectedSlot)
    {
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand(targetFaction);

        lua.Should().StartWith("return SWFOC_SetHumanPlayer_v3(");
        lua.Should().Contain($"SWFOC_SetHumanPlayer_v3({expectedSlot})");
        lua.Should().NotContain("BLOCKED-NEEDS-MEMORY");
    }

    [Fact]
    public void BuildFactionSwitchLuaCommand_DoesNotReferenceNonexistentLuaApi()
    {
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand("EMPIRE");

        // Regression guard: the previous form attempted to call a Lua function
        // that doesn't exist. Both forms below are forbidden.
        lua.Should().NotContain("set_context_allegiance(");
        lua.Should().NotContain("Set_Affiliation(");
        lua.Should().NotContain("Set_Player(");
    }

    [Fact]
    public void BuildFactionSwitchLuaCommand_NullFaction_ThrowsArgumentNull()
    {
        var act = () => FactionSwitchService.BuildFactionSwitchLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildFactionSwitchLuaCommand_EmptyFaction_ThrowsArgumentException()
    {
        var act = () => FactionSwitchService.BuildFactionSwitchLuaCommand("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildFactionSwitchLuaCommand_WhitespaceFaction_ThrowsArgumentException()
    {
        var act = () => FactionSwitchService.BuildFactionSwitchLuaCommand("   ");

        act.Should().Throw<ArgumentException>();
    }
}
