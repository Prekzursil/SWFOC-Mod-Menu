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
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "ready",
                ["steamModIdsDetected"] = "1397421866"
            },
            symbols: new[]
            {
                new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature)
            });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "AOTR_AT_AT" },
            ["faction_catalog"] = new[] { "Empire" },
            ["entity_catalog"] = new[] { "Unit|RAW_MACE_WINDU|raw_1125571106_swfoc|1125571106" }
        };
        var transplantService = new StubTransplantCompatibilityService(new TransplantValidationReport(
            TargetProfileId: profile.Id,
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
            Diagnostics: new Dictionary<string, object?>()));
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
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "ready",
                ["steamModIdsDetected"] = "1397421866"
            });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "AOTR_AT_AT" },
            ["faction_catalog"] = new[] { "Empire" },
            ["entity_catalog"] = new[] { "Unit|AOTR_AT_AT|aotr_1397421866_swfoc|1397421866" }
        };
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
        IReadOnlyList<HelperHookSpec>? helperHooks = null)
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
            Metadata: new Dictionary<string, string>());
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
}
