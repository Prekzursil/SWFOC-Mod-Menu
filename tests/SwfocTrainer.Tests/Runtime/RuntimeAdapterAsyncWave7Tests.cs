using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 7 coverage tests targeting async execution paths in RuntimeAdapter:
/// ExecuteByRouteAsync, ExecuteExtenderBackendActionAsync, ExecuteMemoryActionAsync,
/// ExecuteSdkActionAsync, ExecuteLegacyBackendActionAsync, ProbeCapabilitiesAsync,
/// AttachAsync/DetachAsync lifecycle, CalibrationScanAsync, TryCreateMechanicBlockedResultAsync,
/// TryExecuteExpertMutationOverrideAsync, and exception catch branches.
/// </summary>
public sealed class RuntimeAdapterAsyncWave7Tests
{
    // ────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field '{name}' should exist");
        field!.SetValue(target, value);
    }

    private static RuntimeAdapter CreateAttachedAdapter(
        RuntimeMode mode = RuntimeMode.Galactic,
        TrainerProfile? profile = null,
        IBackendRouter? router = null,
        IHelperBridgeBackend? helperBackend = null,
        IModMechanicDetectionService? mechanicService = null,
        ISdkOperationRouter? sdkRouter = null,
        IExecutionBackend? executionBackend = null,
        bool includeExecutionBackend = true)
    {
        profile ??= BuildProfile("set_credits");
        var services = new Dictionary<Type, object>
        {
            [typeof(IBackendRouter)] = router ?? new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            [typeof(IHelperBridgeBackend)] = helperBackend ?? new StubHelperBridgeBackend(),
            [typeof(IModDependencyValidator)] = new StubDependencyValidator(
                new DependencyValidationResult(DependencyValidationStatus.Pass, "", new HashSet<string>(StringComparer.OrdinalIgnoreCase))),
            [typeof(ITelemetryLogTailService)] = new StubTelemetryLogTailService()
        };

        if (includeExecutionBackend)
        {
            services[typeof(IExecutionBackend)] = executionBackend ?? new StubExecutionBackend();
        }

        if (mechanicService is not null)
        {
            services[typeof(IModMechanicDetectionService)] = mechanicService;
        }

        if (sdkRouter is not null)
        {
            services[typeof(ISdkOperationRouter)] = sdkRouter;
        }

        var adapter = new RuntimeAdapter(
            new StubProcessLocator(),
            new StubProfileRepository(profile),
            new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance,
            new MapServiceProvider(services));

        var symbolMap = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.95),
            ["unit_cap"] = new SymbolInfo("unit_cap", (nint)0x2000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.95),
            ["fog_reveal"] = new SymbolInfo("fog_reveal", (nint)0x3000, SymbolValueType.Byte, AddressSource.Signature, Confidence: 0.95),
            ["test_float"] = new SymbolInfo("test_float", (nint)0x7000, SymbolValueType.Float, AddressSource.Signature, Confidence: 0.95),
            ["test_bool"] = new SymbolInfo("test_bool", (nint)0x9000, SymbolValueType.Bool, AddressSource.Signature, Confidence: 0.95)
        });

        var session = new AttachSession(
            "profile",
            new ProcessMetadata(
                ProcessId: Environment.ProcessId,
                ProcessName: "swfoc",
                ProcessPath: @"C:\Games\swfoc.exe",
                CommandLine: null,
                ExeTarget: ExeTarget.Swfoc,
                Mode: mode,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new ProfileBuild("profile", "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            symbolMap,
            DateTimeOffset.UtcNow);

        typeof(RuntimeAdapter)
            .GetProperty("CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(adapter, session);
        SetField(adapter, "_attachedProfile", profile);

        var memType = typeof(RuntimeAdapter).Assembly.GetType("SwfocTrainer.Runtime.Interop.ProcessMemoryAccessor")!;
        var accessor = RuntimeHelpers.GetUninitializedObject(memType);
        SetField(adapter, "_memory", accessor);

        return adapter;
    }

    private static TrainerProfile BuildProfile(params string[] actionIds)
    {
        return BuildProfileWithExecution(ExecutionKind.Helper, actionIds);
    }

    private static TrainerProfile BuildProfileWithExecution(ExecutionKind executionKind, params string[] actionIds)
    {
        var actions = actionIds.ToDictionary(
            id => id,
            id => new ActionSpec(id, ActionCategory.Hero, RuntimeMode.Unknown, executionKind, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            StringComparer.OrdinalIgnoreCase);

        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets:
            [
                new SignatureSet(Name: "test", GameBuild: "build", Signatures: [new SignatureSpec("credits", "AA BB", 0)])
            ],
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks:
            [
                new HelperHookSpec(
                    Id: "hero_hook",
                    Script: "scripts/aotr/hero_state_bridge.lua",
                    Version: "1.0.0",
                    EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn")
            ],
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static TrainerProfile BuildProfileWithCapabilities(string[] actionIds, string[]? requiredCapabilities = null)
    {
        var actions = actionIds.ToDictionary(
            id => id,
            id => new ActionSpec(id, ActionCategory.Hero, RuntimeMode.Unknown, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            StringComparer.OrdinalIgnoreCase);

        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets:
            [
                new SignatureSet(Name: "test", GameBuild: "build", Signatures: [new SignatureSpec("credits", "AA BB", 0)])
            ],
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks:
            [
                new HelperHookSpec(
                    Id: "hero_hook",
                    Script: "scripts/aotr/hero_state_bridge.lua",
                    Version: "1.0.0",
                    EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn")
            ],
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            RequiredCapabilities: requiredCapabilities);
    }

    private static ActionExecutionRequest BuildRequest(string actionId, RuntimeMode runtimeMode, ExecutionKind executionKind = ExecutionKind.Helper)
    {
        var payload = new JsonObject
        {
            ["helperHookId"] = "hero_hook"
        };
        return new ActionExecutionRequest(
            Action: new ActionSpec(
                actionId,
                ActionCategory.Hero,
                RuntimeMode.Unknown,
                executionKind,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: payload,
            ProfileId: "profile",
            RuntimeMode: runtimeMode);
    }

    private static ActionExecutionRequest BuildMemoryRequest(string symbol, int? intValue = null, float? floatValue = null, bool? boolValue = null)
    {
        var payload = new JsonObject
        {
            ["symbol"] = symbol
        };
        if (intValue.HasValue)
        {
            payload["intValue"] = intValue.Value;
        }

        if (floatValue.HasValue)
        {
            payload["floatValue"] = floatValue.Value;
        }

        if (boolValue.HasValue)
        {
            payload["boolValue"] = boolValue.Value;
        }

        return new ActionExecutionRequest(
            Action: new ActionSpec(
                "memory_action",
                ActionCategory.Economy,
                RuntimeMode.Unknown,
                ExecutionKind.Memory,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: payload,
            ProfileId: "profile",
            RuntimeMode: RuntimeMode.Galactic);
    }

    private static ActionExecutionRequest BuildSdkRequest(string operationId)
    {
        return new ActionExecutionRequest(
            Action: new ActionSpec(
                operationId,
                ActionCategory.Unit,
                RuntimeMode.Unknown,
                ExecutionKind.Sdk,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: new JsonObject(),
            ProfileId: "profile",
            RuntimeMode: RuntimeMode.TacticalLand);
    }

    // ────────────────────────────────────────────────────────────────
    // Stub SDK Operation Router
    // ────────────────────────────────────────────────────────────────

    private sealed class StubSdkOperationRouter : ISdkOperationRouter
    {
        public SdkOperationResult Result { get; set; } = new(
            Succeeded: true,
            Message: "sdk ok",
            ReasonCode: CapabilityReasonCode.AllRequiredAnchorsPresent,
            CapabilityState: SdkCapabilityStatus.Available,
            Diagnostics: new Dictionary<string, object?> { ["sdkStub"] = "yes" });

        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request)
            => Task.FromResult(Result);

        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Result);
    }

    private sealed class CancellationThrowingMechanicService : IModMechanicDetectionService
    {
        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException("cancelled");
        }
    }

    // ────────────────────────────────────────────────────────────────
    // ExecuteByRouteAsync — Extender backend route
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ExtenderRoute_Success_ReturnsSucceededResult()
    {
        var extenderBackend = new StubExecutionBackend();
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            executionBackend: extenderBackend);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("backendRoute");
        result.Diagnostics!["backendRoute"]!.ToString().Should().Be(ExecutionBackendKind.Extender.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ExtenderRoute_NullBackend_ReturnsFailure()
    {
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            includeExecutionBackend: false);
        // Null out the extender backend
        SetField(adapter, "_extenderBackend", null);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("backendRoute");
    }

    // ────────────────────────────────────────────────────────────────
    // ExecuteByRouteAsync — Save backend route
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SaveRoute_ReturnsResult()
    {
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Save, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("save_action", RuntimeMode.Galactic, ExecutionKind.Save),
            CancellationToken.None);

        // Save action returns stub result
        result.Diagnostics.Should().ContainKey("backendRoute");
        result.Diagnostics!["backendRoute"]!.ToString().Should().Be(ExecutionBackendKind.Save.ToString());
    }

    // ────────────────────────────────────────────────────────────────
    // ExecuteByRouteAsync — Memory (legacy) backend route
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MemoryRoute_ReturnsResult()
    {
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var request = BuildRequest("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory);

        // Memory route dispatches to ExecuteLegacyBackendActionAsync -> ExecuteMemoryActionAsync
        // which will throw because _memory is uninitialized — caught by the exception handler
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Diagnostics.Should().ContainKey("backendRoute");
        result.Diagnostics!["backendRoute"]!.ToString().Should().Be(ExecutionBackendKind.Memory.ToString());
    }

    // ────────────────────────────────────────────────────────────────
    // ExecuteByRouteAsync — Unknown backend (default case)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UnknownBackendRoute_ReturnsUnsupportedMessage()
    {
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Unknown, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("some_action", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Unsupported execution backend");
    }

    // ────────────────────────────────────────────────────────────────
    // ExecuteLegacyBackendActionAsync — all ExecutionKind branches
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_LegacyRoute_FreezeKind_ReturnsOrchestratorMessage()
    {
        var profile = BuildProfileWithExecution(ExecutionKind.Freeze, "freeze_timer");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("freeze_timer", RuntimeMode.Galactic, ExecutionKind.Freeze),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("orchestrator");
    }

    [Fact]
    public async Task ExecuteAsync_LegacyRoute_SdkKind_RoutesToSdkAction()
    {
        var sdkRouter = new StubSdkOperationRouter
        {
            Result = new SdkOperationResult(true, "sdk success", CapabilityReasonCode.AllRequiredAnchorsPresent, SdkCapabilityStatus.Available)
        };
        var profile = BuildProfileWithExecution(ExecutionKind.Sdk, "spawn");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            sdkRouter: sdkRouter);

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn", RuntimeMode.Galactic, ExecutionKind.Sdk),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("sdkReasonCode");
    }

    [Fact]
    public async Task ExecuteAsync_LegacyRoute_UnknownKind_ReturnsUnsupported()
    {
        var profile = BuildProfileWithExecution((ExecutionKind)99, "weird_action");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("weird_action", RuntimeMode.Galactic, (ExecutionKind)99),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Unsupported execution kind");
    }

    // ────────────────────────────────────────────────────────────────
    // ExecuteSdkActionAsync — null router
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SdkAction_NullRouter_ReturnsFailure()
    {
        var profile = BuildProfileWithExecution(ExecutionKind.Sdk, "spawn");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));
        // sdkRouter not set => _sdkOperationRouter is null

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn", RuntimeMode.Galactic, ExecutionKind.Sdk),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("failureReasonCode");
        result.Diagnostics!["failureReasonCode"]!.ToString().Should().Be("sdk_router_missing");
    }

    [Fact]
    public async Task ExecuteAsync_SdkAction_WithDiagnostics_MergesDiagnostics()
    {
        var sdkRouter = new StubSdkOperationRouter
        {
            Result = new SdkOperationResult(
                true,
                "ok",
                CapabilityReasonCode.AllRequiredAnchorsPresent,
                SdkCapabilityStatus.Available,
                new Dictionary<string, object?> { ["extra"] = "info" })
        };
        var profile = BuildProfileWithExecution(ExecutionKind.Sdk, "list_selected");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            sdkRouter: sdkRouter);

        var result = await adapter.ExecuteAsync(
            BuildRequest("list_selected", RuntimeMode.TacticalLand, ExecutionKind.Sdk),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("sdkReasonCode");
        result.Diagnostics.Should().ContainKey("sdkCapabilityState");
        result.Diagnostics.Should().ContainKey("extra");
    }

    [Fact]
    public async Task ExecuteAsync_SdkAction_NullDiagnostics_DoesNotThrow()
    {
        var sdkRouter = new StubSdkOperationRouter
        {
            Result = new SdkOperationResult(
                false,
                "failed",
                CapabilityReasonCode.RequiredAnchorsMissing,
                SdkCapabilityStatus.Unavailable,
                Diagnostics: null)
        };
        var profile = BuildProfileWithExecution(ExecutionKind.Sdk, "kill");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            sdkRouter: sdkRouter);

        var result = await adapter.ExecuteAsync(
            BuildRequest("kill", RuntimeMode.TacticalLand, ExecutionKind.Sdk),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("sdkReasonCode");
    }

    // ────────────────────────────────────────────────────────────────
    // ProbeCapabilitiesAsync — null extender / null session
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NullExtenderBackend_ProbeReturnsUnknownCapabilities()
    {
        var adapter = CreateAttachedAdapter(
            includeExecutionBackend: false,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));
        SetField(adapter, "_extenderBackend", null);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        // Should still succeed via helper route
        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("capabilityProbeReasonCode");
        result.Diagnostics!["capabilityProbeReasonCode"]!.ToString()
            .Should().Be(RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ProbeReturnsEmptyCapabilities_InfersFromRequiredCapabilities()
    {
        var emptyProbeBackend = new StubExecutionBackend
        {
            ProbeReport = new CapabilityReport(
                "profile",
                DateTimeOffset.UtcNow,
                new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase),
                RuntimeReasonCode.CAPABILITY_PROBE_PASS)
        };
        var profile = BuildProfileWithCapabilities(["set_credits"], ["freeze_timer", "toggle_fog_reveal"]);
        var adapter = CreateAttachedAdapter(
            profile: profile,
            executionBackend: emptyProbeBackend,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Diagnostics.Should().ContainKey("capabilityCount");
    }

    [Fact]
    public async Task ExecuteAsync_ProbeReturnsPopulatedCapabilities_UsesThemDirectly()
    {
        var populatedProbeBackend = new StubExecutionBackend
        {
            ProbeReport = new CapabilityReport(
                "profile",
                DateTimeOffset.UtcNow,
                new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
                {
                    ["freeze_timer"] = new BackendCapability("freeze_timer", true, CapabilityConfidenceState.Verified, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")
                },
                RuntimeReasonCode.CAPABILITY_PROBE_PASS)
        };
        var adapter = CreateAttachedAdapter(
            executionBackend: populatedProbeBackend,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Diagnostics.Should().ContainKey("capabilityCount");
        ((int)result.Diagnostics!["capabilityCount"]!).Should().Be(1);
    }

    // ────────────────────────────────────────────────────────────────
    // TryCreateMechanicBlockedResultAsync — various branches
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MechanicDetection_NullService_SkipsGating()
    {
        var adapter = CreateAttachedAdapter(mechanicService: null);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        // No mechanic gating with null service — should proceed normally
        result.Diagnostics.Should().NotContainKey("mechanicGating");
    }

    [Fact]
    public async Task ExecuteAsync_MechanicDetection_OperationCanceledException_ReturnsNull()
    {
        var adapter = CreateAttachedAdapter(
            mechanicService: new CancellationThrowingMechanicService());

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        // OperationCanceledException should be swallowed, mechanic gate returns null
        result.Diagnostics.Should().NotContainKey("mechanicGating");
    }

    [Fact]
    public async Task ExecuteAsync_MechanicDetection_InvalidOperationException_ReturnsNull()
    {
        var adapter = CreateAttachedAdapter(
            mechanicService: new ThrowingMechanicDetectionService());

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        // InvalidOperationException should be swallowed
        result.Diagnostics.Should().NotContainKey("mechanicGating");
    }

    [Fact]
    public async Task ExecuteAsync_MechanicDetection_SupportedAction_SkipsBlock()
    {
        var adapter = CreateAttachedAdapter(
            mechanicService: new StubMechanicDetectionService(
                supported: true,
                actionId: "set_credits",
                reasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                message: "supported"));

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Diagnostics.Should().NotContainKey("mechanicGating");
    }

    [Fact]
    public async Task ExecuteAsync_MechanicDetection_UnsupportedAction_ReturnsBlocked()
    {
        var adapter = CreateAttachedAdapter(
            mechanicService: new StubMechanicDetectionService(
                supported: false,
                actionId: "set_credits",
                reasonCode: RuntimeReasonCode.MECHANIC_NOT_SUPPORTED_FOR_CHAIN,
                message: "blocked by mechanic"));

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("mechanicGating");
        result.Diagnostics!["mechanicGating"]!.ToString().Should().Be("blocked");
    }

    [Fact]
    public async Task ExecuteAsync_MechanicDetection_UnknownAction_SkipsBlock()
    {
        // Mechanic detection service reports for a different action ID
        var adapter = CreateAttachedAdapter(
            mechanicService: new StubMechanicDetectionService(
                supported: false,
                actionId: "different_action",
                reasonCode: RuntimeReasonCode.MECHANIC_NOT_SUPPORTED_FOR_CHAIN,
                message: "blocked"));

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        // Should not be blocked because action ID doesn't match
        result.Diagnostics.Should().NotContainKey("mechanicGating");
    }

    // ────────────────────────────────────────────────────────────────
    // TryExecuteExpertMutationOverrideAsync branches
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ExpertOverride_NonPromotedAction_NoOverride()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            var adapter = CreateAttachedAdapter(
                router: new StubBackendRouter(
                    new BackendRouteDecision(false, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "blocked")));

            // Non-promoted action should NOT get expert override
            var result = await adapter.ExecuteAsync(
                BuildRequest("set_credits", RuntimeMode.Galactic),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpertOverride_PanicDisableActive_NoOverride()
    {
        var previousOverride = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var previousPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", "1");
            var profile = BuildProfile("set_unit_cap");
            var adapter = CreateAttachedAdapter(
                profile: profile,
                router: new StubBackendRouter(
                    new BackendRouteDecision(false, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "blocked")));

            var result = await adapter.ExecuteAsync(
                BuildRequest("set_unit_cap", RuntimeMode.Galactic),
                CancellationToken.None);

            // Panic disable overrides the expert override — should be blocked
            result.Succeeded.Should().BeFalse();
            result.Diagnostics.Should().ContainKey("panicDisableState");
            result.Diagnostics!["panicDisableState"]!.ToString().Should().Be("active");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previousOverride);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", previousPanic);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpertOverride_Disabled_NoOverride()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", null);
            var profile = BuildProfile("set_unit_cap");
            var adapter = CreateAttachedAdapter(
                profile: profile,
                router: new StubBackendRouter(
                    new BackendRouteDecision(false, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "blocked")));

            var result = await adapter.ExecuteAsync(
                BuildRequest("set_unit_cap", RuntimeMode.Galactic),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpertOverride_AllowedRoute_NoOverride()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            var profile = BuildProfile("set_unit_cap");
            // Route is allowed, so expert override should not trigger
            var adapter = CreateAttachedAdapter(
                profile: profile,
                router: new StubBackendRouter(
                    new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

            var result = await adapter.ExecuteAsync(
                BuildRequest("set_unit_cap", RuntimeMode.Galactic),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpertOverride_NonExtenderBackend_NoOverride()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            var profile = BuildProfile("set_unit_cap");
            // Route blocked but backend is Helper, not Extender
            var adapter = CreateAttachedAdapter(
                profile: profile,
                router: new StubBackendRouter(
                    new BackendRouteDecision(false, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "blocked")));

            var result = await adapter.ExecuteAsync(
                BuildRequest("set_unit_cap", RuntimeMode.Galactic),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpertOverride_ReadOnlyAction_NoOverride()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            // read_ prefix makes IsMutatingActionId return false
            var profile = BuildProfile("read_unit_cap");
            var adapter = CreateAttachedAdapter(
                profile: profile,
                router: new StubBackendRouter(
                    new BackendRouteDecision(false, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "blocked")));

            var result = await adapter.ExecuteAsync(
                BuildRequest("read_unit_cap", RuntimeMode.Galactic),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previous);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Exception catch branches in ExecuteAsync
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Win32Exception_CaughtAndReturnsFailure()
    {
        var throwingHelper = new StubHelperBridgeBackend
        {
            ExecuteException = new System.ComponentModel.Win32Exception("access denied")
        };
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            helperBackend: throwingHelper);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("failureReasonCode");
        result.Diagnostics!["exceptionType"]!.ToString().Should().Be(nameof(System.ComponentModel.Win32Exception));
    }

    [Fact]
    public async Task ExecuteAsync_IOException_CaughtAndReturnsFailure()
    {
        var throwingHelper = new StubHelperBridgeBackend
        {
            ExecuteException = new System.IO.IOException("pipe broken")
        };
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            helperBackend: throwingHelper);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics!["exceptionType"]!.ToString().Should().Be(nameof(System.IO.IOException));
    }

    [Fact]
    public async Task ExecuteAsync_KeyNotFoundException_CaughtAndReturnsFailure()
    {
        var throwingHelper = new StubHelperBridgeBackend
        {
            ExecuteException = new KeyNotFoundException("symbol not found")
        };
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            helperBackend: throwingHelper);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics!["exceptionType"]!.ToString().Should().Be(nameof(KeyNotFoundException));
    }

    // ────────────────────────────────────────────────────────────────
    // DetachAsync lifecycle
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetachAsync_ClearsSessionAndMemory()
    {
        var adapter = CreateAttachedAdapter();
        adapter.IsAttached.Should().BeTrue();

        await adapter.DetachAsync(CancellationToken.None);

        adapter.IsAttached.Should().BeFalse();
        adapter.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task DetachAsync_ParameterlessOverload_Works()
    {
        var adapter = CreateAttachedAdapter();
        adapter.IsAttached.Should().BeTrue();

        await adapter.DetachAsync();

        adapter.IsAttached.Should().BeFalse();
    }

    [Fact]
    public async Task DetachAsync_CalledTwice_DoesNotThrow()
    {
        var adapter = CreateAttachedAdapter();
        await adapter.DetachAsync();
        await adapter.DetachAsync();

        adapter.IsAttached.Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────────
    // ExecuteAsync parameterless overload
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ParameterlessOverload_DelegatesToCancellationVersion()
    {
        var adapter = CreateAttachedAdapter();
        var result = await adapter.ExecuteAsync(BuildRequest("set_credits", RuntimeMode.Galactic));
        result.Should().NotBeNull();
    }

    [Fact]
    public void ExecuteAsync_NullRequest_Throws()
    {
        var adapter = CreateAttachedAdapter();
        var act = () => adapter.ExecuteAsync(null!, CancellationToken.None);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void ExecuteAsync_ParameterlessNullRequest_Throws()
    {
        var adapter = CreateAttachedAdapter();
        var act = () => adapter.ExecuteAsync(null!);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ────────────────────────────────────────────────────────────────
    // EnsureAttached — not-attached guard
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NotAttached_Throws()
    {
        var profile = BuildProfile("set_credits");
        var adapter = new RuntimeAdapter(
            new StubProcessLocator(),
            new StubProfileRepository(profile),
            new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance);

        var act = () => adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ────────────────────────────────────────────────────────────────
    // ScanCalibrationCandidatesAsync — null/empty/not attached
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScanCalibrationAsync_NullRequest_Throws()
    {
        var adapter = CreateAttachedAdapter();
        var act = () => adapter.ScanCalibrationCandidatesAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ScanCalibrationAsync_EmptyTargetSymbol_ReturnsInvalidRequest()
    {
        var adapter = CreateAttachedAdapter();
        var result = await adapter.ScanCalibrationCandidatesAsync(
            new RuntimeCalibrationScanRequest("  "), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_request");
    }

    [Fact]
    public async Task ScanCalibrationAsync_NotAttached_ReturnsNotAttached()
    {
        var profile = BuildProfile("set_credits");
        var adapter = new RuntimeAdapter(
            new StubProcessLocator(),
            new StubProfileRepository(profile),
            new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance);

        var result = await adapter.ScanCalibrationCandidatesAsync(
            new RuntimeCalibrationScanRequest("credits"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("not_attached");
    }

    // ────────────────────────────────────────────────────────────────
    // ExecuteHelperActionAsync — null session / missing hook / probe unavailable
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HelperRoute_MissingHook_ReturnsFailure()
    {
        // Create profile WITHOUT the hook ID the request references
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["test_action"] = new ActionSpec("test_action", ActionCategory.Hero, RuntimeMode.Unknown, ExecutionKind.Helper, new JsonObject(), false, 0)
        };
        var profileNoHook = new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: [new SignatureSet(Name: "test", GameBuild: "build", Signatures: [new SignatureSpec("credits", "AA BB", 0)])],
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var adapter = CreateAttachedAdapter(
            profile: profileNoHook,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("test_action", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("helperBridgeState");
        result.Diagnostics!["helperBridgeState"]!.ToString().Should().Be("denied");
    }

    [Fact]
    public async Task ExecuteAsync_HelperRoute_ProbeUnavailable_ReturnsFailure()
    {
        var helperBackend = new StubHelperBridgeBackend
        {
            ProbeResult = new HelperBridgeProbeResult(
                Available: false,
                ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                Message: "bridge down")
        };
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            helperBackend: helperBackend);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("bridge down");
    }

    [Fact]
    public async Task ExecuteAsync_HelperRoute_ProbeAvailable_ExecuteSucceeds()
    {
        var helperBackend = new StubHelperBridgeBackend
        {
            ProbeResult = new HelperBridgeProbeResult(true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok"),
            ExecuteResult = new HelperBridgeExecutionResult(true, RuntimeReasonCode.HELPER_EXECUTION_APPLIED, "applied",
                new Dictionary<string, object?> { ["helperVerifyState"] = "applied" })
        };
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            helperBackend: helperBackend);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("helperEntryPoint");
    }

    [Fact]
    public async Task ExecuteAsync_HelperRoute_ExecuteFails_ReturnsFailure()
    {
        var helperBackend = new StubHelperBridgeBackend
        {
            ProbeResult = new HelperBridgeProbeResult(true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok"),
            ExecuteResult = new HelperBridgeExecutionResult(false, RuntimeReasonCode.HELPER_EXECUTION_APPLIED, "apply failed",
                new Dictionary<string, object?> { ["helperVerifyState"] = "failed" })
        };
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            helperBackend: helperBackend);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("apply failed");
    }

    // ────────────────────────────────────────────────────────────────
    // Dependency soft-disable branch
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DependencyDisabledAction_ReturnsBlocked()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_dependencySoftDisabledActions", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "set_credits" });
        SetField(adapter, "_dependencyValidationStatus", DependencyValidationStatus.SoftFail);
        SetField(adapter, "_dependencyValidationMessage", "mod missing");

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("dependencyValidation");
    }

    // ────────────────────────────────────────────────────────────────
    // Context faction routing — spawn variants
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ContextSpawn_TacticalLand_RoutesToTacticalAction()
    {
        var profile = BuildProfile("spawn_context_entity", "spawn_tactical_entity");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            mode: RuntimeMode.TacticalLand,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn_context_entity", RuntimeMode.TacticalLand),
            CancellationToken.None);

        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics!["contextRoutedAction"]!.ToString().Should().Be("spawn_tactical_entity");
    }

    [Fact]
    public async Task ExecuteAsync_ContextSpawn_Galactic_RoutesToGalacticAction()
    {
        var profile = BuildProfile("spawn_context_entity", "spawn_galactic_entity");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            mode: RuntimeMode.Galactic,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn_context_entity", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics!["contextRoutedAction"]!.ToString().Should().Be("spawn_galactic_entity");
    }

    [Fact]
    public async Task ExecuteAsync_ContextSpawn_UnknownMode_ReturnsBlocked()
    {
        var profile = BuildProfile("spawn_context_entity", "spawn_tactical_entity");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            mode: RuntimeMode.Unknown,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn_context_entity", RuntimeMode.Unknown),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("contextActionId");
    }

    [Fact]
    public async Task ExecuteAsync_ContextFaction_TacticalSpace_RoutesToSelected()
    {
        var profile = BuildProfile("set_context_faction", "set_selected_owner_faction");
        var adapter = CreateAttachedAdapter(
            profile: profile,
            mode: RuntimeMode.TacticalSpace,
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Helper, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")));

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_context_faction", RuntimeMode.TacticalSpace),
            CancellationToken.None);

        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics!["contextRoutedAction"]!.ToString().Should().Be("set_selected_owner_faction");
    }

    // ────────────────────────────────────────────────────────────────
    // ExecuteExtenderBackendActionAsync — context building
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ExtenderRoute_BuildsContext_WithProcessInfo()
    {
        var capturedBackend = new CapturingExecutionBackend();
        var adapter = CreateAttachedAdapter(
            router: new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            executionBackend: capturedBackend);

        await adapter.ExecuteAsync(
            BuildRequest("set_credits", RuntimeMode.Galactic),
            CancellationToken.None);

        capturedBackend.LastRequest.Should().NotBeNull();
        capturedBackend.LastRequest!.Context.Should().ContainKey("processId");
        capturedBackend.LastRequest!.Context.Should().ContainKey("processName");
    }

    // ────────────────────────────────────────────────────────────────
    // AttachAsync null guard
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void AttachAsync_NullProfileId_Throws()
    {
        var adapter = CreateAttachedAdapter();
        var act = () => adapter.AttachAsync(null!);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void AttachAsync_WithCancellation_NullProfileId_Throws()
    {
        var adapter = CreateAttachedAdapter();
        var act = () => adapter.AttachAsync(null!, CancellationToken.None);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AttachAsync_AlreadyAttached_ReturnsCurrent()
    {
        var adapter = CreateAttachedAdapter();
        var session = adapter.CurrentSession;

        var result = await adapter.AttachAsync("profile", CancellationToken.None);

        result.Should().Be(session);
    }

    // ────────────────────────────────────────────────────────────────
    // Promoted extender action identification
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("freeze_timer", true)]
    [InlineData("toggle_fog_reveal", true)]
    [InlineData("toggle_ai", true)]
    [InlineData("set_unit_cap", true)]
    [InlineData("toggle_instant_build_patch", true)]
    [InlineData("random_action", false)]
    [InlineData("", false)]
    public void IsPromotedExtenderAction_CorrectlyIdentifiesPromotedActions(string actionId, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod("IsPromotedExtenderAction", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(null, [actionId])!;
        result.Should().Be(expected);
    }

    // ────────────────────────────────────────────────────────────────
    // IsMutatingActionId branches
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("set_credits", true)]
    [InlineData("read_unit_data", false)]
    [InlineData("list_units", false)]
    [InlineData("get_status", false)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    public void IsMutatingActionId_CorrectlyIdentifiesMutations(string actionId, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod("IsMutatingActionId", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(null, [actionId])!;
        result.Should().Be(expected);
    }

    // ────────────────────────────────────────────────────────────────
    // MergeDiagnostics static method
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void MergeDiagnostics_BothNull_ReturnsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod("MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = method!.Invoke(null, [null, null]);
        result.Should().BeNull();
    }

    [Fact]
    public void MergeDiagnostics_BothEmpty_ReturnsPrimary()
    {
        var method = typeof(RuntimeAdapter).GetMethod("MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var primary = new Dictionary<string, object?>();
        var secondary = new Dictionary<string, object?>();
        var result = method!.Invoke(null, [primary, secondary]);
        result.Should().BeSameAs(primary);
    }

    [Fact]
    public void MergeDiagnostics_PrimaryOnly_MergesAll()
    {
        var method = typeof(RuntimeAdapter).GetMethod("MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var primary = new Dictionary<string, object?> { ["a"] = "1" };
        var result = (IReadOnlyDictionary<string, object?>?)method!.Invoke(null, [primary, null]);
        result.Should().ContainKey("a");
    }

    [Fact]
    public void MergeDiagnostics_SecondaryOnly_MergesAll()
    {
        var method = typeof(RuntimeAdapter).GetMethod("MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var secondary = new Dictionary<string, object?> { ["b"] = "2" };
        var result = (IReadOnlyDictionary<string, object?>?)method!.Invoke(null, [null, secondary]);
        result.Should().ContainKey("b");
    }

    [Fact]
    public void MergeDiagnostics_BothPresent_SecondaryOverridesPrimary()
    {
        var method = typeof(RuntimeAdapter).GetMethod("MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var primary = new Dictionary<string, object?> { ["key"] = "primary" };
        var secondary = new Dictionary<string, object?> { ["key"] = "secondary" };
        var result = (IReadOnlyDictionary<string, object?>?)method!.Invoke(null, [primary, secondary]);
        result!["key"]!.ToString().Should().Be("secondary");
    }

    // ────────────────────────────────────────────────────────────────
    // ResolveExpertMutationOverrideState
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveExpertMutationOverrideState_Default_DisabledFailClosed()
    {
        var previousOverride = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var previousPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", null);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", null);

            var method = typeof(RuntimeAdapter).GetMethod("ResolveExpertMutationOverrideState", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            var result = method!.Invoke(null, []);
            result.Should().NotBeNull();
            // It's a readonly record struct — test via ToString or reflection
            var enabledProp = result!.GetType().GetProperty("Enabled");
            enabledProp.Should().NotBeNull();
            ((bool)enabledProp!.GetValue(result)!).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previousOverride);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", previousPanic);
        }
    }

    [Fact]
    public void ResolveExpertMutationOverrideState_OverrideEnabled_ReturnsEnabled()
    {
        var previousOverride = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var previousPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "true");
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", null);

            var method = typeof(RuntimeAdapter).GetMethod("ResolveExpertMutationOverrideState", BindingFlags.Static | BindingFlags.NonPublic);
            var result = method!.Invoke(null, []);
            var enabledProp = result!.GetType().GetProperty("Enabled");
            ((bool)enabledProp!.GetValue(result)!).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previousOverride);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", previousPanic);
        }
    }

    [Fact]
    public void ResolveExpertMutationOverrideState_PanicActive_DisablesOverride()
    {
        var previousOverride = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var previousPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", "true");

            var method = typeof(RuntimeAdapter).GetMethod("ResolveExpertMutationOverrideState", BindingFlags.Static | BindingFlags.NonPublic);
            var result = method!.Invoke(null, []);
            var enabledProp = result!.GetType().GetProperty("Enabled");
            ((bool)enabledProp!.GetValue(result)!).Should().BeFalse();

            var panicProp = result!.GetType().GetProperty("PanicDisableState");
            panicProp!.GetValue(result)!.ToString().Should().Be("active");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previousOverride);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", previousPanic);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // IsEnabledEnvironmentFlag
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsEnabledEnvironmentFlag_VariousValues(string? envValue, bool expected)
    {
        var envName = $"SWFOC_TEST_FLAG_{Guid.NewGuid():N}";
        try
        {
            Environment.SetEnvironmentVariable(envName, envValue);
            var method = typeof(RuntimeAdapter).GetMethod("IsEnabledEnvironmentFlag", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            var result = (bool)method!.Invoke(null, [envName])!;
            result.Should().Be(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // ResolveHybridExecutionFlag
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveHybridExecutionFlag_NullDiagnostics_ReturnsFalse()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveHybridExecutionFlag", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(null, [null])!;
        result.Should().BeFalse();
    }

    [Fact]
    public void ResolveHybridExecutionFlag_TrueValue_ReturnsTrue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveHybridExecutionFlag", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var diag = new Dictionary<string, object?> { ["hybridExecution"] = "true" };
        var result = (bool)method!.Invoke(null, [diag])!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ResolveHybridExecutionFlag_FalseValue_ReturnsFalse()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveHybridExecutionFlag", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["hybridExecution"] = "false" };
        var result = (bool)method!.Invoke(null, [diag])!;
        result.Should().BeFalse();
    }

    [Fact]
    public void ResolveHybridExecutionFlag_NonBoolValue_ReturnsFalse()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveHybridExecutionFlag", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["hybridExecution"] = "notaboolean" };
        var result = (bool)method!.Invoke(null, [diag])!;
        result.Should().BeFalse();
    }

    [Fact]
    public void ResolveHybridExecutionFlag_EmptyValue_ReturnsFalse()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveHybridExecutionFlag", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["hybridExecution"] = "" };
        var result = (bool)method!.Invoke(null, [diag])!;
        result.Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────────
    // ResolveBackendDiagnosticValue
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveBackendDiagnosticValue_WithExistingValue_ReturnsExisting()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveBackendDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var diag = new Dictionary<string, object?> { ["backend"] = "custom" };
        var result = (string)method!.Invoke(null, [diag, ExecutionBackendKind.Memory])!;
        result.Should().Be("custom");
    }

    [Fact]
    public void ResolveBackendDiagnosticValue_NullDiagnostics_ReturnsRouteBackend()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveBackendDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        var result = (string)method!.Invoke(null, [null, ExecutionBackendKind.Extender])!;
        result.Should().Be(ExecutionBackendKind.Extender.ToString());
    }

    // ────────────────────────────────────────────────────────────────
    // TryReadDiagnosticString
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void TryReadDiagnosticString_NullDiagnostics_ReturnsFalse()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var args = new object?[] { null, "key", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadDiagnosticString_NullValue_ReturnsFalse()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["key"] = null };
        var args = new object?[] { diag, "key", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadDiagnosticString_EmptyValue_ReturnsFalse()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["key"] = "" };
        var args = new object?[] { diag, "key", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadDiagnosticString_MissingKey_ReturnsFalse()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["other"] = "value" };
        var args = new object?[] { diag, "key", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadDiagnosticString_ValidValue_ReturnsTrue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["key"] = "hello" };
        var args = new object?[] { diag, "key", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        ((string?)args[2]).Should().Be("hello");
    }

    // ────────────────────────────────────────────────────────────────
    // TryResolveFirstDiagnosticValue
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void TryResolveFirstDiagnosticValue_NoMatch_ReturnsFalse()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryResolveFirstDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var diag = new Dictionary<string, object?> { ["other"] = "value" };
        string[] keys = ["hookState", "creditsStateTag"];
        var args = new object?[] { diag, keys, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryResolveFirstDiagnosticValue_MatchesSecondKey_ReturnsTrue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryResolveFirstDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["creditsStateTag"] = "found" };
        string[] keys = ["hookState", "creditsStateTag"];
        var args = new object?[] { diag, keys, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        ((string?)args[2]).Should().Be("found");
    }

    // ────────────────────────────────────────────────────────────────
    // ResolveLegacyOverrideBackend
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ExecutionKind.Helper, ExecutionBackendKind.Helper)]
    [InlineData(ExecutionKind.Save, ExecutionBackendKind.Save)]
    [InlineData(ExecutionKind.Memory, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.CodePatch, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Freeze, ExecutionBackendKind.Memory)]
    public void ResolveLegacyOverrideBackend_MapsCorrectly(ExecutionKind executionKind, ExecutionBackendKind expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveLegacyOverrideBackend", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (ExecutionBackendKind)method!.Invoke(null, [executionKind])!;
        result.Should().Be(expected);
    }

    // ────────────────────────────────────────────────────────────────
    // IsMutatingSdkOperation
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("list_selected", false)]
    [InlineData("read_unit_data", false)]
    [InlineData("spawn", true)]
    [InlineData("kill", true)]
    [InlineData("unknown_op", true)]
    public void IsMutatingSdkOperation_CorrectlyClassifies(string operationId, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod("IsMutatingSdkOperation", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(null, [operationId])!;
        result.Should().Be(expected);
    }

    // ────────────────────────────────────────────────────────────────
    // ResolveOverrideReasonDiagnosticValue
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveOverrideReasonDiagnosticValue_NullDiag_ReturnsDefault()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveOverrideReasonDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (string)method!.Invoke(null, [null, "default_reason"])!;
        result.Should().Be("default_reason");
    }

    [Fact]
    public void ResolveOverrideReasonDiagnosticValue_WithValue_ReturnsValue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveOverrideReasonDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["overrideReason"] = "custom" };
        var result = (string)method!.Invoke(null, [diag, "default"])!;
        result.Should().Be("custom");
    }

    // ────────────────────────────────────────────────────────────────
    // ResolvePanicDisableStateDiagnosticValue
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolvePanicDisableStateDiagnosticValue_NullDiag_ReturnsDefault()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolvePanicDisableStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (string)method!.Invoke(null, [null, "inactive"])!;
        result.Should().Be("inactive");
    }

    [Fact]
    public void ResolvePanicDisableStateDiagnosticValue_WithValue_ReturnsValue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolvePanicDisableStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["panicDisableState"] = "active" };
        var result = (string)method!.Invoke(null, [diag, "inactive"])!;
        result.Should().Be("active");
    }

    // ────────────────────────────────────────────────────────────────
    // ResolveExpertOverrideEnabledDiagnosticValue
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveExpertOverrideEnabledDiagnosticValue_NullDiag_ReturnsDefault()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveExpertOverrideEnabledDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(null, [null, true])!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ResolveExpertOverrideEnabledDiagnosticValue_TrueString_ReturnsTrue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveExpertOverrideEnabledDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["expertOverrideEnabled"] = "true" };
        var result = (bool)method!.Invoke(null, [diag, false])!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ResolveExpertOverrideEnabledDiagnosticValue_InvalidString_ReturnsDefault()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveExpertOverrideEnabledDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["expertOverrideEnabled"] = "maybe" };
        var result = (bool)method!.Invoke(null, [diag, true])!;
        result.Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────────
    // ResolveHookStateDiagnosticValue
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveHookStateDiagnosticValue_NoBothDiag_ReturnsUnknown()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveHookStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (string)method!.Invoke(null, [null, null])!;
        result.Should().Be("unknown");
    }

    [Fact]
    public void ResolveHookStateDiagnosticValue_FromResultDiag_ReturnsValue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveHookStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        var resultDiag = new Dictionary<string, object?> { ["hookState"] = "installed" };
        var result = (string)method!.Invoke(null, [resultDiag, null])!;
        result.Should().Be("installed");
    }

    [Fact]
    public void ResolveHookStateDiagnosticValue_FromCapabilityDiag_ReturnsValue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveHookStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        var capDiag = new Dictionary<string, object?> { ["hookState"] = "ready" };
        var result = (string)method!.Invoke(null, [null, capDiag])!;
        result.Should().Be("ready");
    }

    // ────────────────────────────────────────────────────────────────
    // ResolveProcessSelectionReason
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveProcessSelectionReason_WithRecommendation_ReturnsReason()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveProcessSelectionReason", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var process = new ProcessMetadata(1, "sw", @"C:\sw.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["recommendationReason"] = "workshop_match" });
        var result = (string)method!.Invoke(null, [process])!;
        result.Should().Be("workshop_match");
    }

    [Fact]
    public void ResolveProcessSelectionReason_NoRecommendation_ReturnsDefault()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveProcessSelectionReason", BindingFlags.Static | BindingFlags.NonPublic);
        var process = new ProcessMetadata(1, "sw", @"C:\sw.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic);
        var result = (string)method!.Invoke(null, [process])!;
        result.Should().Be("exe_target_match");
    }

    // ────────────────────────────────────────────────────────────────
    // Helper stub: CapturingExecutionBackend
    // ────────────────────────────────────────────────────────────────

    private sealed class CapturingExecutionBackend : IExecutionBackend
    {
        public ExecutionBackendKind BackendKind => ExecutionBackendKind.Extender;
        public ActionExecutionRequest? LastRequest { get; private set; }

        public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext)
            => Task.FromResult(new CapabilityReport(profileId, DateTimeOffset.UtcNow,
                new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
                {
                    ["probe"] = new BackendCapability("probe", true, CapabilityConfidenceState.Verified, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")
                },
                RuntimeReasonCode.CAPABILITY_PROBE_PASS));

        public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext, CancellationToken cancellationToken)
            => ProbeCapabilitiesAsync(profileId, processContext);

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport)
        {
            LastRequest = command;
            return Task.FromResult(new ActionExecutionResult(true, "extender ok", AddressSource.None));
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport, CancellationToken cancellationToken)
            => ExecuteAsync(command, capabilityReport);

        public Task<BackendHealth> GetHealthAsync()
            => Task.FromResult(new BackendHealth("capture", ExecutionBackendKind.Extender, true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok"));

        public Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken)
            => GetHealthAsync();
    }
}
