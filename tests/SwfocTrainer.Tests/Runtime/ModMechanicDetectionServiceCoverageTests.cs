using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ModMechanicDetectionServiceCoverageTests
{
    [Fact]
    public async Task DetectAsync_ShouldReportHelperSupport_AndCatalogDiagnostics_WhenHelperBridgeReady()
    {
        var profile = BuildProfile(
            actions: new[]
            {
                Action("spawn_unit_helper", ExecutionKind.Helper, "helperHookId", "unitId", "faction")
            },
            steamWorkshopId: "7777777777",
            helperHooks: new[]
            {
                new HelperHookSpec("spawn_bridge", "spawn_bridge.lua", "1.0", EntryPoint: "SpawnBridge_Invoke")
            });
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = " ready ",
                ["dependencyValidation"] = " strict_valid ",
                ["forcedWorkshopIds"] = "3447786229",
                ["steamModIdsDetected"] = " 1397421866 , 3447786229 "
            },
            launchContextSteamModIds: new[] { " 1125571106 " });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "AOTR_AT_AT", "RAW_TIE_SCOUT" },
            ["faction_catalog"] = new[] { "Empire", "Rebel" },
            ["building_catalog"] = new[] { "RAW_MINING_FACILITY" }
        };
        var service = new ModMechanicDetectionService();

        var report = await service.DetectAsync(profile, session, catalog, CancellationToken.None);

        report.DependenciesSatisfied.Should().BeTrue();
        report.HelperBridgeReady.Should().BeTrue();

        var support = report.ActionSupport.Single();
        support.ActionId.Should().Be("spawn_unit_helper");
        support.Supported.Should().BeTrue();
        support.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_EXECUTION_APPLIED);

        report.Diagnostics.Should().ContainKey("dependencyValidation");
        report.Diagnostics["dependencyValidation"].Should().Be("strict_valid");
        report.Diagnostics["helperBridgeState"].Should().Be("ready");
        report.Diagnostics["unitCatalogCount"].Should().Be(2);
        report.Diagnostics["factionCatalogCount"].Should().Be(2);
        report.Diagnostics["buildingCatalogCount"].Should().Be(1);
        report.Diagnostics["transplantEnabled"].Should().Be(false);
        report.Diagnostics["transplantAllResolved"].Should().Be(true);
        report.Diagnostics["transplantBlockingEntityCount"].Should().Be(0);
        report.Diagnostics["activeWorkshopIds"].Should().BeAssignableTo<IReadOnlyList<string>>();
        ((IReadOnlyList<string>)report.Diagnostics["activeWorkshopIds"]!)
            .Should()
            .Equal("1125571106", "1397421866", "3447786229");
    }

    [Fact]
    public async Task DetectAsync_ShouldNormalizeDependencyDisabledActions_AndMarkDependenciesUnsatisfied()
    {
        var profile = BuildProfile(
            actions: new[] { Action("set_credits", ExecutionKind.Memory, "symbol", "intValue") });
        var session = BuildSession(
            RuntimeMode.Galactic,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyDisabledActions"] = " spawn_unit_helper , set_credits , spawn_unit_helper "
            },
            symbols: new[]
            {
                new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature)
            });
        var service = new ModMechanicDetectionService();

        var report = await service.DetectAsync(profile, session, catalog: null, CancellationToken.None);

        report.DependenciesSatisfied.Should().BeFalse();
        report.Diagnostics.Should().ContainKey("dependencyDisabledActions");
        report.Diagnostics["dependencyDisabledActions"].Should().BeAssignableTo<IReadOnlyList<string>>();
        ((IReadOnlyList<string>)report.Diagnostics["dependencyDisabledActions"]!)
            .Should()
            .Equal("set_credits", "spawn_unit_helper");

        var support = report.ActionSupport.Single();
        support.Supported.Should().BeFalse();
        support.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
    }

    [Fact]
    public async Task DetectAsync_ShouldSupportContextAllegiance_WhenSelectedOwnerSymbolIsHealthy()
    {
        var profile = BuildProfile(
            actions: new[] { Action("set_context_allegiance", ExecutionKind.Memory, "faction") });
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            symbols: new[]
            {
                new SymbolInfo("selected_owner_faction", (nint)0x2200, SymbolValueType.Int32, AddressSource.Signature)
            });
        var service = new ModMechanicDetectionService();

        var report = await service.DetectAsync(profile, session, catalog: null, CancellationToken.None);

        var support = report.ActionSupport.Single();
        support.ActionId.Should().Be("set_context_allegiance");
        support.Supported.Should().BeTrue();
        support.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        support.Message.Should().Contain("routing symbols are available");
    }

    [Fact]
    public void BuildRosterEntities_ShouldUseEmpireFallback_AndPopulateOptionalEntityMetadata()
    {
        var profile = BuildProfile(
            actions: Array.Empty<ActionSpec>(),
            steamWorkshopId: "7777777777");
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity_catalog"] = new[]
            {
                "Building|RAW_BUILD_PAD|||Data/Art/Models/raw_build_pad.alo|dep_b;dep_a;dep_b",
                "Hero|   ",
                "Unit|RAW_TIE_SCOUT"
            }
        };

        var records = InvokePrivateStatic<IReadOnlyList<RosterEntityRecord>>("BuildRosterEntities", profile, catalog);

        records.Should().HaveCount(2);

        records[0].EntityId.Should().Be("RAW_BUILD_PAD");
        records[0].EntityKind.Should().Be(RosterEntityKind.Building);
        records[0].SourceProfileId.Should().Be(profile.Id);
        records[0].SourceWorkshopId.Should().Be("7777777777");
        records[0].DefaultFaction.Should().Be("Empire");
        records[0].VisualRef.Should().Be("Data/Art/Models/raw_build_pad.alo");
        records[0].DependencyRefs.Should().Equal("dep_b", "dep_a");
        records[0].AllowedModes.Should().Equal(RuntimeMode.Galactic);

        records[1].EntityId.Should().Be("RAW_TIE_SCOUT");
        records[1].EntityKind.Should().Be(RosterEntityKind.Unit);
        records[1].SourceProfileId.Should().Be(profile.Id);
        records[1].SourceWorkshopId.Should().Be("7777777777");
        records[1].DefaultFaction.Should().Be("Empire");
        records[1].VisualRef.Should().BeNull();
        records[1].DependencyRefs.Should().BeEmpty();
        records[1].AllowedModes.Should().Equal(RuntimeMode.AnyTactical, RuntimeMode.Galactic);
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
                ["required"] = new JsonArray(required.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray())
            },
            VerifyReadback: false,
            CooldownMs: 0);
    }

    private static TrainerProfile BuildProfile(
        IReadOnlyList<ActionSpec> actions,
        string? steamWorkshopId = null,
        IReadOnlyList<HelperHookSpec>? helperHooks = null)
    {
        return new TrainerProfile(
            Id: "coverage_profile",
            DisplayName: "coverage profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: steamWorkshopId,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions.ToDictionary(action => action.Id, action => action, StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test",
            HelperModHooks: helperHooks ?? Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());
    }

    private static AttachSession BuildSession(
        RuntimeMode mode,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<SymbolInfo>? symbols = null,
        IReadOnlyList<string>? launchContextSteamModIds = null)
    {
        var symbolMap = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols ?? Array.Empty<SymbolInfo>())
        {
            symbolMap[symbol.Name] = symbol;
        }

        var launchContext = launchContextSteamModIds is null
            ? null
            : new LaunchContext(
                LaunchKind.Workshop,
                CommandLineAvailable: true,
                SteamModIds: launchContextSteamModIds,
                ModPathRaw: null,
                ModPathNormalized: null,
                DetectedVia: "tests",
                Recommendation: new ProfileRecommendation("coverage_profile", "tests", 1.0));

        return new AttachSession(
            ProfileId: "coverage_profile",
            Process: new ProcessMetadata(
                ProcessId: 4242,
                ProcessName: "StarWarsG.exe",
                ProcessPath: @"C:\Games\StarWarsG.exe",
                CommandLine: "STEAMMOD=1397421866",
                ExeTarget: ExeTarget.Swfoc,
                Mode: mode,
                Metadata: metadata,
                LaunchContext: launchContext),
            Build: new ProfileBuild("coverage_profile", "test", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(symbolMap),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    private static T InvokePrivateStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(ModMechanicDetectionService).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"private static method '{methodName}' should exist.");
        return (T)method!.Invoke(null, args)!;
    }
}
