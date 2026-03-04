using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ModMechanicDetectionServiceTests
{
    [Fact]
    public async Task DetectAsync_ShouldBlockEntityOperations_WhenTransplantReportHasBlockers()
    {
        var profile = BuildProfile(
            actions: new[]
            {
                Action("spawn_tactical_entity", ExecutionKind.Helper, "helperHookId", "entityId", "faction"),
                Action("set_credits", ExecutionKind.Memory, "symbol", "intValue")
            },
            helperHooks: new[]
            {
                new HelperHookSpec("spawn_bridge", "spawn_bridge.lua", "1.0", EntryPoint: "SWFOC_Trainer_Spawn_Context")
            });
        var session = BuildSessionWithHelperReady(
            mode: RuntimeMode.TacticalLand,
            steamModIds: "1397421866",
            symbols: new[]
            {
                new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature)
            });
        var catalog = CreateCatalog("Unit|RAW_MACE_WINDU|raw_1125571106_swfoc|1125571106");
        var transplantService = new StubTransplantCompatibilityService(CreateBlockingTransplantReport(profile.Id));
        var service = new ModMechanicDetectionService(transplantService);

        var report = await service.DetectAsync(profile, session, catalog, CancellationToken.None);

        var spawnSupport = report.ActionSupport.Single(x => x.ActionId == "spawn_tactical_entity");
        spawnSupport.Supported.Should().BeFalse();
        spawnSupport.ReasonCode.Should().Be(RuntimeReasonCode.CROSS_MOD_TRANSPLANT_REQUIRED);
        spawnSupport.Message.Should().Contain("RAW_MACE_WINDU");

        var creditsSupport = report.ActionSupport.Single(x => x.ActionId == "set_credits");
        creditsSupport.Supported.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_ShouldAllowEntityOperations_WhenTransplantReportIsResolved()
    {
        var profile = BuildProfile(
            actions: new[] { Action("spawn_tactical_entity", ExecutionKind.Helper, "helperHookId", "entityId", "faction") },
            helperHooks: new[] { new HelperHookSpec("spawn_bridge", "spawn_bridge.lua", "1.0", EntryPoint: "SWFOC_Trainer_Spawn_Context") });
        var session = BuildSessionWithHelperReady(RuntimeMode.TacticalLand, "1397421866");
        var catalog = CreateCatalog("Unit|AOTR_AT_AT|aotr_1397421866_swfoc|1397421866");
        var transplantService = new StubTransplantCompatibilityService(new TransplantValidationReport(
            TargetProfileId: profile.Id,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            AllResolved: true,
            TotalEntities: 1,
            BlockingEntityCount: 0,
            Entities: new[]
            {
                new TransplantEntityValidation(
                    EntityId: "AOTR_AT_AT",
                    SourceProfileId: "aotr_1397421866_swfoc",
                    SourceWorkshopId: "1397421866",
                    RequiresTransplant: false,
                    Resolved: true,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                    Message: "Entity belongs to active workshop chain.",
                    VisualRef: "Data/Art/Models/aotr_atat.alo",
                    MissingDependencies: Array.Empty<string>())
            },
            Diagnostics: new Dictionary<string, object?>()));
        var service = new ModMechanicDetectionService(transplantService);

        var report = await service.DetectAsync(profile, session, catalog, CancellationToken.None);

        var spawnSupport = report.ActionSupport.Single(x => x.ActionId == "spawn_tactical_entity");
        spawnSupport.Supported.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_ShouldBlockHelperActions_WhenHelperBridgeUnavailable()
    {
        var profile = BuildProfile(
            actions: new[] { Action("spawn_unit_helper", ExecutionKind.Helper, "helperHookId", "unitId") },
            helperHooks: new[] { new HelperHookSpec("spawn_bridge", "spawn_bridge.lua", "1.0", EntryPoint: "SpawnBridge_Invoke") });
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "unavailable"
            });

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog: null, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "spawn_unit_helper");
        support.Supported.Should().BeFalse();
        support.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE);
    }

    [Fact]
    public async Task DetectAsync_ShouldBlockSymbolDrivenActions_WhenSymbolIsUnresolved()
    {
        var profile = BuildProfile(
            actions: new[] { Action("set_selected_owner_faction", ExecutionKind.Memory, "symbol", "intValue") });
        var session = BuildSession(RuntimeMode.TacticalLand);

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog: null, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "set_selected_owner_faction");
        support.Supported.Should().BeFalse();
        support.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
    }

    [Fact]
    public async Task DetectAsync_ShouldMarkDependencyDisabledActions_AsUnsupported()
    {
        var profile = BuildProfile(
            actions: new[] { Action("set_credits", ExecutionKind.Memory, "symbol", "intValue") });
        var session = BuildSession(
            RuntimeMode.Galactic,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyDisabledActions"] = "set_credits"
            },
            symbols: new[]
            {
                new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature)
            });

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog: null, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "set_credits");
        support.Supported.Should().BeFalse();
        support.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
    }

    [Fact]
    public async Task DetectAsync_ShouldBlockSpawnContextEntity_WhenUnitRosterCatalogMissing()
    {
        var profile = BuildProfile(
            actions: new[] { Action("spawn_context_entity", ExecutionKind.Memory, "entityId", "faction") });
        var session = BuildSession(RuntimeMode.TacticalLand);
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["faction_catalog"] = new[] { "Empire" }
        };

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "spawn_context_entity");
        support.Supported.Should().BeFalse();
        support.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
        support.Message.Should().Contain("Spawn roster catalog is unavailable");
    }

    [Fact]
    public async Task DetectAsync_ShouldBlockPlacePlanetBuilding_WhenBuildingRosterCatalogMissing()
    {
        var profile = BuildProfile(
            actions: new[] { Action("place_planet_building", ExecutionKind.Memory, "entityId", "faction") });
        var session = BuildSession(RuntimeMode.Galactic);
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["faction_catalog"] = new[] { "Empire" }
        };

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "place_planet_building");
        support.Supported.Should().BeFalse();
        support.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
        support.Message.Should().Contain("Building roster catalog is unavailable");
    }

    [Fact]
    public async Task DetectAsync_ShouldAllowSetContextFaction_WhenPlanetOwnerSymbolHealthy()
    {
        var profile = BuildProfile(
            actions: new[] { Action("set_context_faction", ExecutionKind.Memory, "faction") });
        var session = BuildSession(
            RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("planet_owner", (nint)0x2000, SymbolValueType.Int32, AddressSource.Signature)
            });

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog: null, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "set_context_faction");
        support.Supported.Should().BeTrue();
        support.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
    }

    [Fact]
    public async Task DetectAsync_ShouldBlockHelperAction_WhenHookMetadataIsMissingEvenIfBridgeReady()
    {
        var profile = BuildProfile(
            actions: new[] { Action("set_hero_state_helper", ExecutionKind.Helper, "helperHookId", "globalKey", "intValue") },
            helperHooks: Array.Empty<HelperHookSpec>());
        var session = BuildSession(
            RuntimeMode.Galactic,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "ready"
            });

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog: null, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "set_hero_state_helper");
        support.Supported.Should().BeFalse();
        support.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_ENTRYPOINT_NOT_FOUND);
    }

    [Fact]
    public async Task DetectAsync_ShouldTreatTransplantResolverExceptionsAsNonBlocking()
    {
        var profile = BuildProfile(
            actions: new[] { Action("spawn_tactical_entity", ExecutionKind.Helper, "helperHookId", "entityId", "faction") },
            helperHooks: new[] { new HelperHookSpec("spawn_bridge", "spawn_bridge.lua", "1.0", EntryPoint: "SWFOC_Trainer_Spawn_Context") });
        var session = BuildSessionWithHelperReady(RuntimeMode.TacticalLand, "1397421866");
        var catalog = CreateCatalog("Unit|AOTR_AT_AT|aotr_1397421866_swfoc|1397421866");
        var service = new ModMechanicDetectionService(new ThrowingTransplantCompatibilityService());

        var report = await service.DetectAsync(profile, session, catalog, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "spawn_tactical_entity");
        support.Supported.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_ShouldBlockContextFaction_WhenNoOwnerSymbolsAreResolved()
    {
        var profile = BuildProfile(
            actions: new[] { Action("set_context_allegiance", ExecutionKind.Memory, "faction") });
        var session = BuildSession(RuntimeMode.Galactic);

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog: null, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "set_context_allegiance");
        support.Supported.Should().BeFalse();
        support.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
        support.Message.Should().Contain("Neither selected-unit owner nor planet owner");
    }

    [Fact]
    public async Task DetectAsync_ShouldRethrowOperationCanceled_WhenTransplantResolverCancels()
    {
        var profile = BuildProfile(
            actions: new[] { Action("spawn_tactical_entity", ExecutionKind.Helper, "helperHookId", "entityId", "faction") },
            helperHooks: new[] { new HelperHookSpec("spawn_bridge", "spawn_bridge.lua", "1.0", EntryPoint: "SWFOC_Trainer_Spawn_Context") });
        var session = BuildSessionWithHelperReady(RuntimeMode.TacticalLand, "1397421866");
        var catalog = CreateCatalog("Unit|AOTR_AT_AT|aotr_1397421866_swfoc|1397421866");
        var service = new ModMechanicDetectionService(new CancelingTransplantCompatibilityService());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.DetectAsync(profile, session, catalog, CancellationToken.None));
    }

    [Fact]
    public async Task DetectAsync_ShouldTreatUnknownSymbolAction_AsSupported()
    {
        var profile = BuildProfile(
            actions: new[] { Action("unknown_runtime_action", ExecutionKind.Memory, "symbol") });
        var session = BuildSession(RuntimeMode.Galactic);

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog: null, CancellationToken.None);

        var support = report.ActionSupport.Single(x => x.ActionId == "unknown_runtime_action");
        support.Supported.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_ShouldEmitHeroMechanicsSummary_FromProfileMetadataAndActions()
    {
        var profile = BuildProfile(
            actions: new[]
            {
                Action("set_hero_state_helper", ExecutionKind.Helper, "helperHookId", "globalKey", "intValue"),
                Action("edit_hero_state", ExecutionKind.Helper, "helperHookId", "entityId", "desiredState")
            },
            helperHooks: new[]
            {
                new HelperHookSpec("hero_hook", "hero_bridge.lua", "1.0", EntryPoint: "SWFOC_Trainer_Edit_Hero_State")
            },
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["supports_hero_rescue"] = "true",
                ["supports_hero_permadeath"] = "true",
                ["defaultHeroRespawnTime"] = "14",
                ["respawnExceptionSources"] = "GameConstants.xml, RespawnExceptions.lua",
                ["duplicateHeroPolicy"] = "allow_with_warning"
            });
        var session = BuildSession(
            RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("hero_respawn_timer", (nint)0x3000, SymbolValueType.Int32, AddressSource.Signature)
            });

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog: null, CancellationToken.None);

        report.Diagnostics.Should().ContainKey("heroMechanicsSummary");
        var summary = report.Diagnostics!["heroMechanicsSummary"] as IReadOnlyDictionary<string, object?>;
        summary.Should().NotBeNull();
        summary!["supportsRespawn"].Should().Be(true);
        summary["supportsPermadeath"].Should().Be(true);
        summary["supportsRescue"].Should().Be(true);
        summary["defaultRespawnTime"].Should().Be(14);
        summary["duplicateHeroPolicy"]!.ToString().Should().Be("allow_with_warning");

        var exceptionSources = summary["respawnExceptionSources"] as IReadOnlyList<string>;
        exceptionSources.Should().NotBeNull();
        exceptionSources!.Should().Contain("GameConstants.xml");
        exceptionSources.Should().Contain("RespawnExceptions.lua");
    }

    [Fact]
    public void ParseActiveWorkshopIds_ShouldMergeLaunchContextAndMetadata()
    {
        var process = new ProcessMetadata(
            ProcessId: 10,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["forcedWorkshopIds"] = " 1397421866 ",
                ["steamModIdsDetected"] = " 1125571106 , 1976399102 "
            },
            LaunchContext: new LaunchContext(
                LaunchKind.Workshop,
                CommandLineAvailable: true,
                SteamModIds: new[] { " 3447786229 " },
                ModPathRaw: null,
                ModPathNormalized: null,
                DetectedVia: "tests",
                Recommendation: new ProfileRecommendation("profile", "test", 1.0)));

        var activeIds = InvokePrivateStatic<IReadOnlyList<string>>("ParseActiveWorkshopIds", process);

        activeIds.Should().Equal("1125571106", "1397421866", "1976399102", "3447786229");
    }

    [Fact]
    public void TryParseEntityCatalogEntry_ShouldRejectInvalidEntries_AndMapKnownKinds()
    {
        var profile = BuildProfile(actions: Array.Empty<ActionSpec>());
        const string defaultFaction = "Empire";

        var invalidBlankArgs = new object?[] { "", profile, defaultFaction, null };
        var invalidBlank = InvokePrivateStaticMethod("TryParseEntityCatalogEntry").Invoke(null, invalidBlankArgs);
        ((bool)invalidBlank!).Should().BeFalse();

        var invalidSegmentsArgs = new object?[] { "UnitOnly", profile, defaultFaction, null };
        var invalidSegments = InvokePrivateStaticMethod("TryParseEntityCatalogEntry").Invoke(null, invalidSegmentsArgs);
        ((bool)invalidSegments!).Should().BeFalse();

        VerifyKind("Hero|RAW_HERO", RosterEntityKind.Hero);
        VerifyKind("Building|RAW_BUILDING", RosterEntityKind.Building);
        VerifyKind("SpaceStructure|RAW_STATION", RosterEntityKind.SpaceStructure);
        VerifyKind("AbilityCarrier|RAW_CARRIER", RosterEntityKind.AbilityCarrier);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CreateCatalog(string entityEntry)
    {
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "AOTR_AT_AT" },
            ["faction_catalog"] = new[] { "Empire" },
            ["entity_catalog"] = new[] { entityEntry }
        };
    }

    private static AttachSession BuildSessionWithHelperReady(
        RuntimeMode mode,
        string steamModIds,
        IReadOnlyList<SymbolInfo>? symbols = null)
    {
        return BuildSession(
            mode,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "ready",
                ["steamModIdsDetected"] = steamModIds
            },
            symbols: symbols);
    }

    private static TransplantValidationReport CreateBlockingTransplantReport(string profileId)
    {
        return new TransplantValidationReport(
            TargetProfileId: profileId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            AllResolved: false,
            TotalEntities: 1,
            BlockingEntityCount: 1,
            Entities: new[]
            {
                new TransplantEntityValidation(
                    EntityId: "RAW_MACE_WINDU",
                    SourceProfileId: "raw_1125571106_swfoc",
                    SourceWorkshopId: "1125571106",
                    RequiresTransplant: true,
                    Resolved: false,
                    ReasonCode: RuntimeReasonCode.ROSTER_VISUAL_MISSING,
                    Message: "Missing visual reference for transplanted hero.",
                    VisualRef: null,
                    MissingDependencies: new[] { "Data/Art/Models/raw_mace_windu.alo" })
            },
            Diagnostics: new Dictionary<string, object?>());
    }

    private static ActionSpec Action(string id, ExecutionKind kind, params string[] required)
    {
        return new ActionSpec(
            id,
            ActionCategory.Global,
            RuntimeMode.Unknown,
            kind,
            new JsonObject
            {
                ["required"] = new JsonArray(required.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray())
            },
            VerifyReadback: false,
            CooldownMs: 0);
    }

    private static TrainerProfile BuildProfile(
        IReadOnlyList<ActionSpec> actions,
        IReadOnlyList<HelperHookSpec>? helperHooks = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "test profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions.ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test",
            HelperModHooks: helperHooks ?? Array.Empty<HelperHookSpec>(),
            Metadata: metadata is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase));
    }

    private static AttachSession BuildSession(
        RuntimeMode mode,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<SymbolInfo>? symbols = null)
    {
        var symbolMap = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols ?? Array.Empty<SymbolInfo>())
        {
            symbolMap[symbol.Name] = symbol;
        }

        return new AttachSession(
            ProfileId: "test_profile",
            Process: new ProcessMetadata(
                ProcessId: 4242,
                ProcessName: "StarWarsG.exe",
                ProcessPath: @"C:\Games\StarWarsG.exe",
                CommandLine: "STEAMMOD=1397421866",
                ExeTarget: ExeTarget.Swfoc,
                Mode: mode,
                Metadata: metadata,
                LaunchContext: null),
            Build: new ProfileBuild("test_profile", "test", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(symbolMap),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    private sealed class StubTransplantCompatibilityService : ITransplantCompatibilityService
    {
        private readonly TransplantValidationReport _report;

        public StubTransplantCompatibilityService(TransplantValidationReport report)
        {
            _report = report;
        }

        public Task<TransplantValidationReport> ValidateAsync(
            string targetProfileId,
            IReadOnlyList<string> activeWorkshopIds,
            IReadOnlyList<RosterEntityRecord> entities,
            CancellationToken cancellationToken)
        {
            _ = targetProfileId;
            _ = activeWorkshopIds;
            _ = entities;
            _ = cancellationToken;
            return Task.FromResult(_report);
        }
    }

    private sealed class ThrowingTransplantCompatibilityService : ITransplantCompatibilityService
    {
        public Task<TransplantValidationReport> ValidateAsync(
            string targetProfileId,
            IReadOnlyList<string> activeWorkshopIds,
            IReadOnlyList<RosterEntityRecord> entities,
            CancellationToken cancellationToken)
        {
            _ = targetProfileId;
            _ = activeWorkshopIds;
            _ = entities;
            _ = cancellationToken;
            throw new InvalidOperationException("transplant service unavailable");
        }
    }

    private sealed class CancelingTransplantCompatibilityService : ITransplantCompatibilityService
    {
        public Task<TransplantValidationReport> ValidateAsync(
            string targetProfileId,
            IReadOnlyList<string> activeWorkshopIds,
            IReadOnlyList<RosterEntityRecord> entities,
            CancellationToken cancellationToken)
        {
            _ = targetProfileId;
            _ = activeWorkshopIds;
            _ = entities;
            _ = cancellationToken;
            throw new OperationCanceledException("transplant canceled");
        }
    }

    private static void VerifyKind(string entry, RosterEntityKind expectedKind)
    {
        var profile = BuildProfile(actions: Array.Empty<ActionSpec>());
        var args = new object?[]
        {
            entry,
            profile,
            "Empire",
            null
        };
        var method = InvokePrivateStaticMethod("TryParseEntityCatalogEntry");
        var parsed = (bool)method.Invoke(null, args)!;
        parsed.Should().BeTrue();

        args[3].Should().NotBeNull();
        var record = (RosterEntityRecord)args[3]!;
        record.EntityKind.Should().Be(expectedKind);
    }

    private static T InvokePrivateStatic<T>(string methodName, params object?[] args)
    {
        var method = InvokePrivateStaticMethod(methodName);
        return (T)method.Invoke(null, args)!;
    }

    private static System.Reflection.MethodInfo InvokePrivateStaticMethod(string methodName)
    {
        var method = typeof(ModMechanicDetectionService).GetMethod(
            methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull($"private static method '{methodName}' should exist.");
        return method!;
    }
}
