using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 8 branch-coverage tests for ModMechanicDetectionService — targets remaining
/// uncovered branches in helper gates, roster gates, context faction gates, symbol gates,
/// dependency gates, transplant validation, and entity catalog parsing.
/// </summary>
public sealed class ModMechanicDetectionServiceWave8Tests
{
    private static readonly BindingFlags NonPublicStatic =
        BindingFlags.Static | BindingFlags.NonPublic;

    // ── Null guards ──────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_NullProfile_Throws()
    {
        var svc = new ModMechanicDetectionService();
        var act = () => svc.DetectAsync(null!, CreateSession(), null, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DetectAsync_NullSession_Throws()
    {
        var svc = new ModMechanicDetectionService();
        var act = () => svc.DetectAsync(CreateProfile(), null!, null, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DetectAsync_CancellationRequested_Throws()
    {
        var svc = new ModMechanicDetectionService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = () => svc.DetectAsync(CreateProfile(), CreateSession(), null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Helper gate ──────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_HelperAction_HelperNotReady_ReturnsUnsupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["helper_action"] = new("helper_action", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), false, 0)
        }, helperHooks: new[] { new HelperHookSpec("hook1", "script.lua", "1.0") });
        var session = CreateSession(metadata: new Dictionary<string, string> { ["helperBridgeState"] = "disconnected" });
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "helper_action" && !x.Supported && x.ReasonCode == RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE);
    }

    [Fact]
    public async Task DetectAsync_HelperAction_HelperReadyNoHooks_ReturnsUnsupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["helper_action"] = new("helper_action", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), false, 0)
        }, helperHooks: Array.Empty<HelperHookSpec>());
        var session = CreateSession(metadata: new Dictionary<string, string> { ["helperBridgeState"] = "ready" });
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "helper_action" && !x.Supported && x.ReasonCode == RuntimeReasonCode.HELPER_ENTRYPOINT_NOT_FOUND);
    }

    [Fact]
    public async Task DetectAsync_HelperAction_HelperReadyWithHooks_ReturnsSupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["helper_action"] = new("helper_action", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), false, 0)
        }, helperHooks: new[] { new HelperHookSpec("hook1", "script.lua", "1.0") });
        var session = CreateSession(metadata: new Dictionary<string, string> { ["helperBridgeState"] = "ready" });
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "helper_action" && x.Supported && x.ReasonCode == RuntimeReasonCode.HELPER_EXECUTION_APPLIED);
    }

    // ── Roster gate ──────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_SpawnAction_NoCatalog_ReturnsUnsupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["spawn_unit_helper"] = new("spawn_unit_helper", ActionCategory.Unit, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var session = CreateSession();
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "spawn_unit_helper" && !x.Supported);
    }

    [Fact]
    public async Task DetectAsync_BuildingAction_NoBuildingCatalog_ReturnsUnsupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["place_planet_building"] = new("place_planet_building", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var catalog = new Dictionary<string, IReadOnlyList<string>>
        {
            ["faction_catalog"] = new[] { "Empire" }
        };
        var session = CreateSession();
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, catalog, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "place_planet_building" && !x.Supported);
    }

    // ── Context faction gate ─────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_ContextFactionAction_NoSymbols_ReturnsUnsupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["set_context_faction"] = new("set_context_faction", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var session = CreateSession();
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "set_context_faction" && !x.Supported);
    }

    [Fact]
    public async Task DetectAsync_ContextFactionAction_WithOwnerSymbol_ReturnsSupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["set_context_faction"] = new("set_context_faction", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["selected_owner_faction"] = new("selected_owner_faction", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.95, HealthStatus: SymbolHealthStatus.Healthy)
        };
        var session = CreateSession(symbols: symbols);
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "set_context_faction" && x.Supported);
    }

    [Fact]
    public async Task DetectAsync_ContextAllegianceAction_WithPlanetOwner_ReturnsSupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["set_context_allegiance"] = new("set_context_allegiance", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["planet_owner"] = new("planet_owner", (nint)0x2000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.95, HealthStatus: SymbolHealthStatus.Healthy)
        };
        var session = CreateSession(symbols: symbols);
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "set_context_allegiance" && x.Supported);
    }

    // ── Symbol gate ──────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_SymbolAction_UnresolvedSymbol_ReturnsUnsupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["set_credits"] = new("set_credits", ActionCategory.Economy, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new("credits", nint.Zero, SymbolValueType.Int32, AddressSource.None, HealthStatus: SymbolHealthStatus.Unresolved)
        };
        var session = CreateSession(symbols: symbols);
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "set_credits" && !x.Supported);
    }

    [Fact]
    public async Task DetectAsync_SymbolAction_HealthySymbol_PassesThrough()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["set_credits"] = new("set_credits", ActionCategory.Economy, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new("credits", (nint)0x5000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.95, HealthStatus: SymbolHealthStatus.Healthy)
        };
        var session = CreateSession(symbols: symbols);
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "set_credits" && x.Supported);
    }

    // ── Dependency gate ──────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_ActionInDisabledSet_ReturnsUnsupported()
    {
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["disabled_action"] = new("disabled_action", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var session = CreateSession(metadata: new Dictionary<string, string>
        {
            ["dependencyDisabledActions"] = "disabled_action,other_action"
        });
        var svc = new ModMechanicDetectionService();
        var report = await svc.DetectAsync(profile, session, null, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "disabled_action" && !x.Supported && x.ReasonCode == RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
    }

    // ── Transplant validation ────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_TransplantIOException_ReturnsNullReport()
    {
        var svc = new ModMechanicDetectionService(new ThrowingTransplantService(new IOException("disk error")));
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["test_action"] = new("test_action", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var report = await svc.DetectAsync(profile, CreateSession(), null, CancellationToken.None);
        report.Should().NotBeNull();
        report.ActionSupport.Should().HaveCount(1);
    }

    [Fact]
    public async Task DetectAsync_TransplantInvalidOperationException_ReturnsNullReport()
    {
        var svc = new ModMechanicDetectionService(new ThrowingTransplantService(new InvalidOperationException("bad state")));
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["test_action"] = new("test_action", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var report = await svc.DetectAsync(profile, CreateSession(), null, CancellationToken.None);
        report.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectAsync_TransplantBlockingEntity_ReturnsBlocked()
    {
        var blockingEntity = new TransplantEntityValidation("entity1", "profile_a", "123", true, false, RuntimeReasonCode.TRANSPLANT_ASSET_MISSING, "Missing", null, Array.Empty<string>());
        var transplantReport = new TransplantValidationReport("test_profile", DateTimeOffset.UtcNow, false, 1, 1, new[] { blockingEntity }, new Dictionary<string, object?>());
        var svc = new ModMechanicDetectionService(new StubTransplantService(transplantReport));
        var profile = CreateProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["spawn_unit_helper"] = new("spawn_unit_helper", ActionCategory.Unit, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var catalog = new Dictionary<string, IReadOnlyList<string>>
        {
            ["unit_catalog"] = new[] { "unit1" },
            ["faction_catalog"] = new[] { "Empire" }
        };
        var report = await svc.DetectAsync(profile, CreateSession(), catalog, CancellationToken.None);
        report.ActionSupport.Should().Contain(x => x.ActionId == "spawn_unit_helper" && !x.Supported && x.ReasonCode == RuntimeReasonCode.CROSS_MOD_TRANSPLANT_REQUIRED);
    }

    // ── Entity catalog parsing ───────────────────────────────────────────

    [Fact]
    public void TryParseEntityCatalogEntry_EmptyString_ReturnsFalse()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("TryParseEntityCatalogEntry", NonPublicStatic)!;
        var args = new object?[] { "", CreateProfile(), "Empire", null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseEntityCatalogEntry_SingleSegment_ReturnsFalse()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("TryParseEntityCatalogEntry", NonPublicStatic)!;
        var args = new object?[] { "Unit", CreateProfile(), "Empire", null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseEntityCatalogEntry_EmptyEntityId_ReturnsFalse()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("TryParseEntityCatalogEntry", NonPublicStatic)!;
        var args = new object?[] { "Unit| ", CreateProfile(), "Empire", null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseEntityCatalogEntry_NullRaw_ReturnsFalse()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("TryParseEntityCatalogEntry", NonPublicStatic)!;
        var args = new object?[] { null, CreateProfile(), "Empire", null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseEntityCatalogEntry_HeroKind_ParsesCorrectly()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("TryParseEntityCatalogEntry", NonPublicStatic)!;
        var args = new object?[] { "Hero|Vader_Hero|profile_x|12345|icon.tga|dep1;dep2", CreateProfile(), "Empire", null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        var record = (RosterEntityRecord)args[3]!;
        record.EntityKind.Should().Be(RosterEntityKind.Hero);
        record.EntityId.Should().Be("Vader_Hero");
        record.SourceProfileId.Should().Be("profile_x");
        record.SourceWorkshopId.Should().Be("12345");
        record.VisualRef.Should().Be("icon.tga");
        record.DependencyRefs.Should().HaveCount(2);
    }

    [Fact]
    public void TryParseEntityCatalogEntry_MinimalValid_UsesProfileDefaults()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("TryParseEntityCatalogEntry", NonPublicStatic)!;
        var profile = CreateProfile();
        var args = new object?[] { "Unit|AT_AT", profile, "Empire", null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        var record = (RosterEntityRecord)args[3]!;
        record.SourceProfileId.Should().Be(profile.Id);
        record.EntityKind.Should().Be(RosterEntityKind.Unit);
    }

    [Fact]
    public void ParseEntityKind_Building_ReturnsBuilding()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("ParseEntityKind", NonPublicStatic)!;
        var result = (RosterEntityKind)method.Invoke(null, new object[] { "Building" })!;
        result.Should().Be(RosterEntityKind.Building);
    }

    [Fact]
    public void ParseEntityKind_SpaceStructure_ReturnsSpaceStructure()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("ParseEntityKind", NonPublicStatic)!;
        var result = (RosterEntityKind)method.Invoke(null, new object[] { "SpaceStructure" })!;
        result.Should().Be(RosterEntityKind.SpaceStructure);
    }

    [Fact]
    public void ParseEntityKind_AbilityCarrier_ReturnsAbilityCarrier()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("ParseEntityKind", NonPublicStatic)!;
        var result = (RosterEntityKind)method.Invoke(null, new object[] { "AbilityCarrier" })!;
        result.Should().Be(RosterEntityKind.AbilityCarrier);
    }

    [Fact]
    public void ParseEntityKind_Unknown_ReturnsUnit()
    {
        var method = typeof(ModMechanicDetectionService).GetMethod("ParseEntityKind", NonPublicStatic)!;
        var result = (RosterEntityKind)method.Invoke(null, new object[] { "SomethingElse" })!;
        result.Should().Be(RosterEntityKind.Unit);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static TrainerProfile CreateProfile(
        IReadOnlyDictionary<string, ActionSpec>? actions = null,
        IReadOnlyList<HelperHookSpec>? helperHooks = null)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: "12345",
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions ?? new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "",
            HelperModHooks: helperHooks ?? Array.Empty<HelperHookSpec>());
    }

    private static AttachSession CreateSession(
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyDictionary<string, SymbolInfo>? symbols = null)
    {
        var process = new ProcessMetadata(
            ProcessId: 1234,
            ProcessName: "test",
            ProcessPath: "test.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            Metadata: metadata ?? new Dictionary<string, string>(),
            LaunchContext: new LaunchContext(LaunchKind.BaseGame, false, Array.Empty<string>(), null, null, "test", new ProfileRecommendation(null, "none", 0)));
        var build = new ProfileBuild("test_profile", "1.0", "test.exe", ExeTarget.Swfoc);
        var symbolMap = new SymbolMap(symbols ?? new Dictionary<string, SymbolInfo>());
        return new AttachSession("test_profile", process, build, symbolMap, DateTimeOffset.UtcNow);
    }

    private sealed class ThrowingTransplantService : ITransplantCompatibilityService
    {
        private readonly Exception _exception;
        public ThrowingTransplantService(Exception exception) => _exception = exception;
        public Task<TransplantValidationReport> ValidateAsync(string targetProfileId, IReadOnlyList<string> activeWorkshopIds, IReadOnlyList<RosterEntityRecord> entities, CancellationToken cancellationToken)
            => throw _exception;
    }

    private sealed class StubTransplantService : ITransplantCompatibilityService
    {
        private readonly TransplantValidationReport _report;
        public StubTransplantService(TransplantValidationReport report) => _report = report;
        public Task<TransplantValidationReport> ValidateAsync(string targetProfileId, IReadOnlyList<string> activeWorkshopIds, IReadOnlyList<RosterEntityRecord> entities, CancellationToken cancellationToken)
            => Task.FromResult(_report);
    }
}
