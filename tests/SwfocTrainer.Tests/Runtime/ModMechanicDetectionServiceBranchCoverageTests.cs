using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ModMechanicDetectionServiceBranchCoverageTests
{
    [Fact]
    public async Task DetectAsync_ShouldTreatHealthyKnownSymbolAction_AsSupported()
    {
        var profile = BuildProfile(
            actions: new[]
            {
                Action("set_credits", ExecutionKind.Memory, "symbol", "intValue")
            });
        var session = BuildSession(
            RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature)
            });

        var report = await new ModMechanicDetectionService().DetectAsync(profile, session, catalog: null, CancellationToken.None);

        var support = report.ActionSupport.Single();
        support.ActionId.Should().Be("set_credits");
        support.Supported.Should().BeTrue();
        support.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        support.Message.Should().Be("Mechanic prerequisites are available.");
    }

    [Fact]
    public void BuildRosterEntities_ShouldUseCatalogFaction_AndMapSpaceAndAbilityCarrierModes()
    {
        var profile = BuildProfile(
            actions: Array.Empty<ActionSpec>(),
            steamWorkshopId: "profile_workshop");
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["faction_catalog"] = new[] { "Rebel" },
            ["entity_catalog"] = new[]
            {
                "SpaceStructure|RAW_STATION|sub_profile|1125571106|Data/Art/Models/raw_station.alo|dep_alpha;dep_beta;DEP_ALPHA",
                "AbilityCarrier|RAW_CARRIER"
            }
        };

        var records = InvokePrivateStatic<IReadOnlyList<RosterEntityRecord>>("BuildRosterEntities", profile, catalog);

        records.Should().HaveCount(2);

        records[0].EntityKind.Should().Be(RosterEntityKind.SpaceStructure);
        records[0].DefaultFaction.Should().Be("Rebel");
        records[0].SourceProfileId.Should().Be("sub_profile");
        records[0].SourceWorkshopId.Should().Be("1125571106");
        records[0].VisualRef.Should().Be("Data/Art/Models/raw_station.alo");
        records[0].DependencyRefs.Should().Equal("dep_alpha", "dep_beta");
        records[0].AllowedModes.Should().Equal(RuntimeMode.Galactic);

        records[1].EntityKind.Should().Be(RosterEntityKind.AbilityCarrier);
        records[1].DefaultFaction.Should().Be("Rebel");
        records[1].SourceProfileId.Should().Be(profile.Id);
        records[1].SourceWorkshopId.Should().Be("profile_workshop");
        records[1].VisualRef.Should().BeNull();
        records[1].DependencyRefs.Should().BeEmpty();
        records[1].AllowedModes.Should().Equal(RuntimeMode.AnyTactical, RuntimeMode.Galactic);
    }

    [Fact]
    public void ParseCsvSet_ShouldReturnDistinctCaseInsensitiveValues_AndEmptySetsForMissingInputs()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dependencyDisabledActions"] = " spawn_unit_helper , SET_CREDITS , set_credits "
        };

        var parsed = InvokePrivateStatic<IReadOnlySet<string>>("ParseCsvSet", metadata, "dependencyDisabledActions");
        var missing = InvokePrivateStatic<IReadOnlySet<string>>(
            "ParseCsvSet",
            (IReadOnlyDictionary<string, string>?)null,
            "dependencyDisabledActions");
        var whitespace = InvokePrivateStatic<IReadOnlySet<string>>(
            "ParseCsvSet",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyDisabledActions"] = "   "
            },
            "dependencyDisabledActions");

        parsed.Should().HaveCount(2);
        parsed.Should().Contain("spawn_unit_helper");
        parsed.Should().Contain("set_credits");
        missing.Should().BeEmpty();
        whitespace.Should().BeEmpty();
    }

    [Fact]
    public void ReadMetadataValue_ShouldTrimValues_AndReturnNullForMissingOrWhitespace()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["helperBridgeState"] = " ready ",
            ["dependencyValidation"] = "   "
        };

        var trimmed = InvokePrivateStatic<string?>("ReadMetadataValue", metadata, "helperBridgeState");
        var whitespace = InvokePrivateStatic<string?>("ReadMetadataValue", metadata, "dependencyValidation");
        var missing = InvokePrivateStatic<string?>("ReadMetadataValue", metadata, "missingKey");

        trimmed.Should().Be("ready");
        whitespace.Should().BeNull();
        missing.Should().BeNull();
    }

    [Fact]
    public void TryGetHealthySymbol_ShouldRequireNonZeroAddress_AndResolvedHealth()
    {
        var healthySession = BuildSession(
            RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature)
            });
        var zeroAddressSession = BuildSession(
            RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("credits", nint.Zero, SymbolValueType.Int32, AddressSource.Signature)
            });
        var unresolvedSession = BuildSession(
            RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo(
                    "credits",
                    (nint)0x1000,
                    SymbolValueType.Int32,
                    AddressSource.Signature,
                    HealthStatus: SymbolHealthStatus.Unresolved)
            });

        InvokePrivateStatic<bool>("TryGetHealthySymbol", healthySession, "credits").Should().BeTrue();
        InvokePrivateStatic<bool>("TryGetHealthySymbol", zeroAddressSession, "credits").Should().BeFalse();
        InvokePrivateStatic<bool>("TryGetHealthySymbol", unresolvedSession, "credits").Should().BeFalse();
        InvokePrivateStatic<bool>("TryGetHealthySymbol", healthySession, "missing_symbol").Should().BeFalse();
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
        string? steamWorkshopId = null)
    {
        return new TrainerProfile(
            Id: "branch_coverage_profile",
            DisplayName: "branch coverage profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: steamWorkshopId,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions.ToDictionary(action => action.Id, action => action, StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());
    }

    private static AttachSession BuildSession(
        RuntimeMode mode,
        IReadOnlyList<SymbolInfo>? symbols = null)
    {
        var symbolMap = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols ?? Array.Empty<SymbolInfo>())
        {
            symbolMap[symbol.Name] = symbol;
        }

        return new AttachSession(
            ProfileId: "branch_coverage_profile",
            Process: new ProcessMetadata(
                ProcessId: 4242,
                ProcessName: "StarWarsG.exe",
                ProcessPath: @"C:\Games\StarWarsG.exe",
                CommandLine: "STEAMMOD=1397421866",
                ExeTarget: ExeTarget.Swfoc,
                Mode: mode,
                Metadata: null,
                LaunchContext: null),
            Build: new ProfileBuild("branch_coverage_profile", "test", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc),
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
