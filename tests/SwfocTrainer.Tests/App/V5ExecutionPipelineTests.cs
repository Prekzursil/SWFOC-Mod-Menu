using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Tests that v5 mutation services route Lua commands through the ILuaBridgeExecutor
/// when one is provided, and fall back to stub behavior when one is not.
/// </summary>
public sealed class V5ExecutionPipelineTests
{
    // ===== Test infrastructure =====

    /// <summary>
    /// Records every call to ExecuteLuaAsync so tests can verify routing.
    /// </summary>
    private sealed class RecordingBridgeExecutor : ILuaBridgeExecutor
    {
        public List<(string ProfileId, string LuaCommand, string FeatureId)> Calls { get; } = new();

        public ActionExecutionResult NextResult { get; set; } = new(
            Succeeded: true,
            Message: "Bridge executed",
            AddressSource: AddressSource.None);

        public Task<ActionExecutionResult> ExecuteLuaAsync(
            string profileId, string luaCommand, string featureId, CancellationToken cancellationToken)
        {
            Calls.Add((profileId, luaCommand, featureId));
            return Task.FromResult(NextResult);
        }
    }

    private const string TestProfileId = "test_profile";

    // ===== StoryEventService =====

    [Fact]
    public async Task FireStoryEvent_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<StoryEventService>();
        var catalog = new StubCatalogService();
        var service = new StoryEventService(catalog, bridge, logger);

        var result = await ((IStoryEventService)service).FireEventAsync(TestProfileId, "DEATH_STAR_DESTROYED", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        bridge.Calls[0].LuaCommand.Should().Be("Story_Event(\"DEATH_STAR_DESTROYED\")");
        bridge.Calls[0].FeatureId.Should().Be(StoryEventService.FeatureId);
        bridge.Calls[0].ProfileId.Should().Be(TestProfileId);
    }

    [Fact]
    public async Task FireStoryEvent_WithoutBridge_ReturnsPreparedResult()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<StoryEventService>();
        var catalog = new StubCatalogService();
        var service = new StoryEventService(catalog, logger);

        var result = await ((IStoryEventService)service).FireEventAsync(TestProfileId, "ENDOR_SHIELD_DOWN");

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("prepared");
        result.Diagnostics.Should().ContainKey("lua_call");
    }

    [Fact]
    public async Task FireStoryEvent_EmptyEventId_ReturnsFailure()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<StoryEventService>();
        var catalog = new StubCatalogService();
        var service = new StoryEventService(catalog, bridge, logger);

        var result = await ((IStoryEventService)service).FireEventAsync(TestProfileId, "  ");

        result.Succeeded.Should().BeFalse();
        bridge.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task FireStoryEvent_BridgeReturnsFailure_PropagatesFailure()
    {
        var bridge = new RecordingBridgeExecutor
        {
            NextResult = new ActionExecutionResult(false, "Bridge error", AddressSource.None)
        };
        var logger = NullLoggerFactory.Instance.CreateLogger<StoryEventService>();
        var catalog = new StubCatalogService();
        var service = new StoryEventService(catalog, bridge, logger);

        var result = await ((IStoryEventService)service).FireEventAsync(TestProfileId, "REBEL_FLEET_ARRIVES");

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("Bridge error");
    }

    // ===== CameraDirectorService =====

    [Fact]
    public async Task ExecuteCameraCommand_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<CameraDirectorService>();
        var service = new CameraDirectorService(bridge, logger);

        var result = await ((ICameraDirectorService)service).ExecuteCameraCommandAsync(TestProfileId, "zoom");

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        bridge.Calls[0].LuaCommand.Should().Be("Zoom_Camera(1.0)");
        bridge.Calls[0].FeatureId.Should().Be(CameraDirectorService.FeatureId);
    }

    [Fact]
    public async Task ExecuteCameraCommand_UnknownCommand_ReturnsFailureWithoutBridgeCall()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<CameraDirectorService>();
        var service = new CameraDirectorService(bridge, logger);

        var result = await ((ICameraDirectorService)service).ExecuteCameraCommandAsync(TestProfileId, "explode");

        result.Succeeded.Should().BeFalse();
        bridge.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteCameraCommand_WithoutBridge_ReturnsPreparedResult()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<CameraDirectorService>();
        var service = new CameraDirectorService(logger);

        var result = await ((ICameraDirectorService)service).ExecuteCameraCommandAsync(TestProfileId, "letterbox_on");

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("prepared");
    }

    // ===== EnhancedSpawnService =====

    [Fact]
    public async Task ExecuteSpawn_WithBridge_RoutesEachUnitThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<EnhancedSpawnService>();
        var service = new EnhancedSpawnService(bridge, logger);

        var request = new EnhancedSpawnRequest(
            "AT_AT", "EMPIRE", SpawnMode.Tactical, 3,
            SpawnPositionKind.AtCamera, null, false, false);

        var result = await ((IEnhancedSpawnService)service).ExecuteSpawnAsync(TestProfileId, request);

        result.Attempted.Should().Be(3);
        result.Succeeded.Should().Be(3);
        result.Failed.Should().Be(0);
        bridge.Calls.Should().HaveCount(3);
        bridge.Calls[0].LuaCommand.Should().Contain("Spawn_Unit(");
        bridge.Calls[0].FeatureId.Should().Be("spawn_tactical_entity");
    }

    [Fact]
    public async Task ExecuteSpawn_BridgeFails_StopOnFailure_StopsBatch()
    {
        var bridge = new RecordingBridgeExecutor
        {
            NextResult = new ActionExecutionResult(false, "Spawn denied", AddressSource.None)
        };
        var logger = NullLoggerFactory.Instance.CreateLogger<EnhancedSpawnService>();
        var service = new EnhancedSpawnService(bridge, logger);

        var request = new EnhancedSpawnRequest(
            "AT_AT", "EMPIRE", SpawnMode.Tactical, 5,
            SpawnPositionKind.AtCamera, null, false, StopOnFailure: true);

        var result = await ((IEnhancedSpawnService)service).ExecuteSpawnAsync(TestProfileId, request);

        result.Failed.Should().Be(1);
        result.Succeeded.Should().Be(0);
        bridge.Calls.Should().HaveCount(1, "batch should stop after first failure");
    }

    [Fact]
    public async Task ExecuteSpawn_WithoutBridge_ReturnsAllSucceededStub()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<EnhancedSpawnService>();
        var service = new EnhancedSpawnService(logger);

        var request = new EnhancedSpawnRequest(
            "X_WING", "REBEL", SpawnMode.Tactical, 2,
            SpawnPositionKind.AtCamera, null, false, false);

        var result = await ((IEnhancedSpawnService)service).ExecuteSpawnAsync(TestProfileId, request);

        result.Attempted.Should().Be(2);
        result.Succeeded.Should().Be(2);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteSpawn_GalacticMode_UsesCorrectActionId()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<EnhancedSpawnService>();
        var service = new EnhancedSpawnService(bridge, logger);

        var request = new EnhancedSpawnRequest(
            "STAR_DESTROYER", "EMPIRE", SpawnMode.GalacticPersistent, 1,
            SpawnPositionKind.AtCamera, "KUAT", false, false);

        await ((IEnhancedSpawnService)service).ExecuteSpawnAsync(TestProfileId, request);

        bridge.Calls.Should().HaveCount(1);
        bridge.Calls[0].FeatureId.Should().Be("spawn_galactic_entity");
        bridge.Calls[0].LuaCommand.Should().Contain("Galactic_Spawn_Unit(");
    }

    // ===== FactionSwitchService =====

    [Fact]
    public async Task SwitchFaction_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<FactionSwitchService>();
        var service = new FactionSwitchService(bridge, logger);

        var result = await ((IFactionSwitchService)service).SwitchFactionAsync(
            TestProfileId, new FactionSwitchRequest("REBEL"));

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        // 2026-04-11 galactic-mode fix: SWFOC_SetHumanPlayer_v3 replaces the
        // v1 Switch_Sides rotation because v1 is silently guarded out in
        // galactic mode. v2 does a manual +0x62 sweep + subsystem refresh.
        // See knowledge-base/faction_switch_full_anatomy_2026-04-11.md.
        bridge.Calls[0].LuaCommand.Should().StartWith("return SWFOC_SetHumanPlayer_v3(");
        bridge.Calls[0].LuaCommand.Should().Contain("SWFOC_SetHumanPlayer_v3(0)");
        bridge.Calls[0].FeatureId.Should().Be(FactionSwitchService.FeatureId);
    }

    [Fact]
    public async Task SwitchFaction_EmptyTarget_ReturnsFailure()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<FactionSwitchService>();
        var service = new FactionSwitchService(bridge, logger);

        var result = await ((IFactionSwitchService)service).SwitchFactionAsync(
            TestProfileId, new FactionSwitchRequest("  "));

        result.Succeeded.Should().BeFalse();
        bridge.Calls.Should().BeEmpty();
    }

    // ===== OwnershipTransferService =====

    [Fact]
    public async Task TransferOwnership_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<OwnershipTransferService>();
        var service = new OwnershipTransferService(bridge, logger);

        var request = new OwnershipTransferRequest("AT_AT", "REBEL", OwnershipTransferScope.SelectedUnit);
        var result = await ((IOwnershipTransferService)service).TransferOwnershipAsync(TestProfileId, request);

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        bridge.Calls[0].LuaCommand.Should().Contain("Change_Owner(Find_Player(\"REBEL\"))");
    }

    // ===== AiControlService =====

    [Fact]
    public async Task ExecuteAiControl_SuspendAll_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<AiControlService>();
        var service = new AiControlService(bridge, logger);

        var request = new AiControlRequest(AiControlAction.SuspendAll, 120, null, null, null);
        var result = await ((IAiControlService)service).ExecuteAiControlAsync(TestProfileId, request);

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        bridge.Calls[0].LuaCommand.Should().Be("Suspend_AI(120)");
    }

    [Fact]
    public async Task ExecuteAiControl_PreventUsage_CommentOnly_DoesNotCallBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<AiControlService>();
        var service = new AiControlService(bridge, logger);

        var request = new AiControlRequest(AiControlAction.PreventUsage, null, "UNIT_42", null, null);
        var result = await ((IAiControlService)service).ExecuteAiControlAsync(TestProfileId, request);

        // PreventUsage produces a comment-only Lua string (starts with --),
        // so the bridge should NOT be called.
        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().BeEmpty();
        result.Diagnostics.Should().ContainKey("lua_call");
    }

    // ===== CooldownManagerService =====

    [Fact]
    public async Task ResetCooldowns_SelectedUnit_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<CooldownManagerService>();
        var service = new CooldownManagerService(bridge, logger);

        var request = new CooldownResetRequest(CooldownResetScope.SelectedUnit, "AT_AT");
        var result = await ((ICooldownManagerService)service).ResetCooldownsAsync(TestProfileId, request);

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        bridge.Calls[0].LuaCommand.Should().Contain("Reset_Ability_Counter()");
    }

    [Fact]
    public async Task ResetCooldowns_SelectedUnit_NoUnitId_ReturnsFailure()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<CooldownManagerService>();
        var service = new CooldownManagerService(bridge, logger);

        var request = new CooldownResetRequest(CooldownResetScope.SelectedUnit, null);
        var result = await ((ICooldownManagerService)service).ResetCooldownsAsync(TestProfileId, request);

        result.Succeeded.Should().BeFalse();
        bridge.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetCooldowns_AllPlayerUnits_CommentOnly_DoesNotCallBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<CooldownManagerService>();
        var service = new CooldownManagerService(bridge, logger);

        var request = new CooldownResetRequest(CooldownResetScope.AllPlayerUnits, null);
        var result = await ((ICooldownManagerService)service).ResetCooldownsAsync(TestProfileId, request);

        // AllPlayerUnits produces a comment-only Lua string.
        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().BeEmpty();
    }

    // ===== PlanetManagerService =====

    [Fact]
    public async Task SetPlanetOwner_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<PlanetManagerService>();
        var catalog = new StubCatalogService();
        var service = new PlanetManagerService(catalog, bridge, logger);

        var result = await ((IPlanetManagerService)service).SetPlanetOwnerAsync(TestProfileId, "CORUSCANT", "REBEL");

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        bridge.Calls[0].LuaCommand.Should().Be("FindPlanet(\"CORUSCANT\"):Change_Owner(Find_Player(\"REBEL\"))");
        bridge.Calls[0].FeatureId.Should().Be(PlanetManagerService.SetOwnerFeatureId);
    }

    // ===== DiplomacyService =====

    [Fact]
    public async Task SetDiplomacyRelation_Allied_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<DiplomacyService>();
        var service = new DiplomacyService(bridge, logger);

        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied);
        var result = await ((IDiplomacyService)service).SetRelationAsync(TestProfileId, state);

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        // Phase 3 IDA correction (2026-04-07): Make_Ally is a PlayerWrapper instance method
        // (RVA 0x6046A0). Previous global form would error at runtime ("attempt to call nil").
        bridge.Calls[0].LuaCommand.Should().Be(
            "local p1 = Find_Player(\"EMPIRE\"); local p2 = Find_Player(\"REBEL\"); " +
            "if p1 and p2 then p1:Make_Ally(p2) end");
    }

    [Fact]
    public async Task SetDiplomacyRelation_Neutral_ReturnsFailure()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<DiplomacyService>();
        var service = new DiplomacyService(bridge, logger);

        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Neutral);
        var result = await ((IDiplomacyService)service).SetRelationAsync(TestProfileId, state);

        result.Succeeded.Should().BeFalse();
        bridge.Calls.Should().BeEmpty();
    }

    // ===== CorruptionService =====

    [Fact]
    public async Task SetCorruption_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<CorruptionService>();
        var service = new CorruptionService(bridge, logger);

        var entry = new CorruptionEntry("CORUSCANT", CorruptionType.Racketeering, 1);
        var result = await ((ICorruptionService)service).SetCorruptionAsync(TestProfileId, entry);

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        bridge.Calls[0].LuaCommand.Should().Be("Story_Event(\"CORRUPTION_RACKETEERING_CORUSCANT\")");
    }

    [Fact]
    public async Task SetCorruption_NoneType_ReturnsFailure()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<CorruptionService>();
        var service = new CorruptionService(bridge, logger);

        var entry = new CorruptionEntry("CORUSCANT", CorruptionType.None, 1);
        var result = await ((ICorruptionService)service).SetCorruptionAsync(TestProfileId, entry);

        result.Succeeded.Should().BeFalse();
        bridge.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveCorruption_WithBridge_RoutesLuaThroughBridge()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<CorruptionService>();
        var service = new CorruptionService(bridge, logger);

        var result = await ((ICorruptionService)service).RemoveCorruptionAsync(TestProfileId, "KUAT");

        result.Succeeded.Should().BeTrue();
        bridge.Calls.Should().HaveCount(1);
        bridge.Calls[0].LuaCommand.Should().Be("Story_Event(\"REMOVE_CORRUPTION_KUAT\")");
    }

    [Fact]
    public async Task RemoveCorruption_EmptyPlanetId_ReturnsFailure()
    {
        var bridge = new RecordingBridgeExecutor();
        var logger = NullLoggerFactory.Instance.CreateLogger<CorruptionService>();
        var service = new CorruptionService(bridge, logger);

        var result = await ((ICorruptionService)service).RemoveCorruptionAsync(TestProfileId, " ");

        result.Succeeded.Should().BeFalse();
        bridge.Calls.Should().BeEmpty();
    }

    // ===== Stub catalog service =====

    private sealed class StubCatalogService : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(
            string profileId, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> empty =
                new Dictionary<string, IReadOnlyList<string>>();
            return Task.FromResult(empty);
        }
    }
}
