using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterExecuteCoverageTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnBlockedResult_WhenContextSpawnModeIsUnknown()
    {
        var harness = new AdapterHarness();
        var profile = BuildProfile("spawn_tactical_entity");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Unknown);

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn_context_entity", RuntimeMode.Unknown),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.MODE_STRICT_TACTICAL_UNSPECIFIED.ToString());
        result.Diagnostics.Should().ContainKey("contextActionId");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnDependencySoftBlock_WhenActionIsDisabled()
    {
        var harness = new AdapterHarness();
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        SetPrivateField(adapter, "_dependencySoftDisabledActions", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "set_hero_state_helper" });
        SetPrivateField(adapter, "_dependencyValidationStatus", DependencyValidationStatus.SoftFail);
        SetPrivateField(adapter, "_dependencyValidationMessage", "missing parent");

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("dependencyValidation");
        result.Diagnostics!["dependencyValidation"]!.ToString().Should().Be(DependencyValidationStatus.SoftFail.ToString());
        result.Diagnostics["disabledActionId"]!.ToString().Should().Be("set_hero_state_helper");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnMechanicBlocked_WhenDetectorReportsUnsupported()
    {
        var harness = new AdapterHarness
        {
            MechanicDetectionService = new StubMechanicDetectionService(
                supported: false,
                actionId: "set_hero_state_helper",
                reasonCode: RuntimeReasonCode.MECHANIC_NOT_SUPPORTED_FOR_CHAIN,
                message: "unsupported for chain")
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        SetPrivateField(adapter, "_extenderBackend", null);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("mechanicGating");
        result.Diagnostics!["mechanicGating"]!.ToString().Should().Be("blocked");
        result.Diagnostics["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.MECHANIC_NOT_SUPPORTED_FOR_CHAIN.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnBlockedRoute_WhenRouterRejectsAction()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(
                new BackendRouteDecision(
                    Allowed: false,
                    Backend: ExecutionBackendKind.Helper,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "route blocked",
                    Diagnostics: new Dictionary<string, object?> { ["route"] = "blocked" }))
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("route blocked");
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccess_WhenHelperBackendApplies()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(
                new BackendRouteDecision(
                    Allowed: true,
                    Backend: ExecutionBackendKind.Helper,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                    Message: "ok")),
            HelperBridgeBackend = new StubHelperBridgeBackend
            {
                ExecuteResult = new HelperBridgeExecutionResult(
                    Succeeded: true,
                    ReasonCode: RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
                    Message: "applied",
                    Diagnostics: new Dictionary<string, object?> { ["helperVerifyState"] = "applied" })
            }
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("backendRoute");
        result.Diagnostics!["backendRoute"]!.ToString().Should().Be(ExecutionBackendKind.Helper.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnActionException_WhenHelperBackendThrows()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(
                new BackendRouteDecision(
                    Allowed: true,
                    Backend: ExecutionBackendKind.Helper,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                    Message: "ok")),
            HelperBridgeBackend = new StubHelperBridgeBackend
            {
                ExecuteException = new InvalidOperationException("helper crash")
            }
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("failureReasonCode");
        result.Diagnostics!["failureReasonCode"]!.ToString().Should().Be("action_exception");
        result.Diagnostics["exceptionType"]!.ToString().Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRouteContextAllegianceAction_ToTacticalTarget()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(
                new BackendRouteDecision(
                    Allowed: false,
                    Backend: ExecutionBackendKind.Extender,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "blocked"))
        };
        var profile = BuildProfile("set_context_allegiance", "set_selected_owner_faction");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.TacticalLand);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_context_allegiance", RuntimeMode.TacticalLand),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("contextActionId");
        result.Diagnostics!["contextActionId"]!.ToString().Should().Be("set_context_allegiance");
        result.Diagnostics["contextRoutedAction"]!.ToString().Should().Be("set_selected_owner_faction");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRouteContextFactionAction_ToGalacticTarget()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(
                new BackendRouteDecision(
                    Allowed: false,
                    Backend: ExecutionBackendKind.Extender,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "blocked"))
        };
        var profile = BuildProfile("set_context_faction", "set_planet_owner");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_context_faction", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("contextActionId");
        result.Diagnostics!["contextActionId"]!.ToString().Should().Be("set_context_faction");
        result.Diagnostics["contextRoutedAction"]!.ToString().Should().Be("set_planet_owner");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnMissingRoutedAction_WhenContextActionTargetIsUnavailable()
    {
        var harness = new AdapterHarness();
        var profile = BuildProfile("set_context_allegiance");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_context_allegiance", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING.ToString());
        result.Diagnostics["routedActionId"]!.ToString().Should().Be("set_planet_owner");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIgnoreMechanicDetectorExceptions()
    {
        var harness = new AdapterHarness
        {
            MechanicDetectionService = new ThrowingMechanicDetectionService()
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBypassMechanicGate_WhenActionSupportIsExplicitlySupported()
    {
        var harness = new AdapterHarness
        {
            MechanicDetectionService = new StubMechanicDetectionService(
                supported: true,
                actionId: "set_hero_state_helper",
                reasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                message: "supported")
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldApplyExpertOverride_WhenPromotedActionIsBlockedByRoute()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            var harness = new AdapterHarness
            {
                Router = new StubBackendRouter(
                    new BackendRouteDecision(
                        Allowed: false,
                        Backend: ExecutionBackendKind.Extender,
                        ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                        Message: "blocked extender"))
            };
            var profile = BuildProfile("set_unit_cap");
            var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

            var result = await adapter.ExecuteAsync(
                BuildRequest("set_unit_cap", RuntimeMode.Galactic),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.Diagnostics.Should().ContainKey("expertOverrideEnabled");
            result.Diagnostics!["expertOverrideEnabled"].Should().Be(true);
            result.Diagnostics.Should().NotContainKey("contextActionId");
            result.Diagnostics.Should().NotContainKey("contextRoutedAction");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnBackendUnavailable_WhenExtenderRouteSelectedWithoutBackend()
    {
        var harness = new AdapterHarness
        {
            IncludeExecutionBackend = false,
            Router = new StubBackendRouter(
                new BackendRouteDecision(
                    Allowed: true,
                    Backend: ExecutionBackendKind.Extender,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                    Message: "extender selected"))
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        SetPrivateField(adapter, "_extenderBackend", null);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE.ToString());
    }

    [Fact]
    public async Task AttachAsync_ShouldPopulateCurrentSession_WithHelperProbeMetadata()
    {
        var helperBackend = new StubHelperBridgeBackend
        {
            ProbeResult = new HelperBridgeProbeResult(
                Available: true,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok",
                Diagnostics: new Dictionary<string, object?> { ["availableFeatures"] = "set_hero_state_helper" })
        };
        var process = BuildSession(RuntimeMode.Galactic).Process;
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = new RuntimeAdapter(
            new StubProcessLocator(process),
            new StubProfileRepository(profile),
            new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance,
            new MapServiceProvider(
                new Dictionary<Type, object>
                {
                    [typeof(IHelperBridgeBackend)] = helperBackend,
                    [typeof(IModDependencyValidator)] = new StubDependencyValidator(
                        new DependencyValidationResult(
                            DependencyValidationStatus.Pass,
                            string.Empty,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase))),
                    [typeof(ITelemetryLogTailService)] = new StubTelemetryLogTailService()
                }));

        var attached = await adapter.AttachAsync(profile.Id, CancellationToken.None);

        attached.Process.Metadata.Should().ContainKey("helperBridgeState");
        attached.Process.Metadata!["helperBridgeState"].Should().Be("ready");
        adapter.CurrentSession.Should().NotBeNull();
        adapter.CurrentSession!.Process.Metadata!["helperBridgeReasonCode"]
            .Should()
            .Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS.ToString());
    }

    [Fact]
    public async Task ApplyHelperBridgeProbeMetadataAsync_ShouldAttachBridgeDiagnosticsToSession()
    {
        var helperBackend = new StubHelperBridgeBackend
        {
            ProbeResult = new HelperBridgeProbeResult(
                Available: true,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok",
                Diagnostics: new Dictionary<string, object?>
                {
                    ["availableFeatures"] = "spawn_tactical_entity,set_context_allegiance"
                })
        };
        var harness = new AdapterHarness { HelperBridgeBackend = helperBackend };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyHelperBridgeProbeMetadataAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var session = BuildSession(RuntimeMode.Galactic);
        var task = (Task<AttachSession>)method!.Invoke(adapter, new object?[] { session, profile, CancellationToken.None })!;
        var enriched = await task;

        enriched.Process.Metadata.Should().ContainKey("helperBridgeState");
        enriched.Process.Metadata!["helperBridgeState"].Should().Be("ready");
        enriched.Process.Metadata["helperBridgeReasonCode"].Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS.ToString());
        enriched.Process.Metadata["helperBridgeFeatures"].Should().Contain("spawn_tactical_entity");
    }

    [Fact]
    public void ApplyDependencyValidation_ShouldThrowOnHardFail_AndPopulateSoftFailState()
    {
        var hardHarness = new AdapterHarness
        {
            DependencyValidator = new StubDependencyValidator(
                new DependencyValidationResult(
                    DependencyValidationStatus.HardFail,
                    "hard mismatch",
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
        };
        var hardProfile = BuildProfile("set_hero_state_helper");
        var hardAdapter = hardHarness.CreateAdapter(hardProfile, RuntimeMode.Galactic);
        var method = typeof(RuntimeAdapter).GetMethod("ApplyDependencyValidation", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = BuildSession(RuntimeMode.Galactic).Process;
        Action hardCall = () => method!.Invoke(hardAdapter, new object?[] { hardProfile, process });
        hardCall.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .Where(x => x.Message.Contains(RuntimeReasonCode.ATTACH_PROFILE_MISMATCH.ToString(), StringComparison.Ordinal));

        var softHarness = new AdapterHarness
        {
            DependencyValidator = new StubDependencyValidator(
                new DependencyValidationResult(
                    DependencyValidationStatus.SoftFail,
                    "soft mismatch",
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "set_hero_state_helper" }))
        };
        var softProfile = BuildProfile("set_hero_state_helper");
        var softAdapter = softHarness.CreateAdapter(softProfile, RuntimeMode.Galactic);
        var processed = (ProcessMetadata)method!.Invoke(softAdapter, new object?[] { softProfile, process })!;
        processed.Metadata.Should().ContainKey("dependencyValidation");
        processed.Metadata!["dependencyValidation"].Should().Be(DependencyValidationStatus.SoftFail.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAnnotateContextRoute_WhenSpawnContextRedirectsAndRouteBlocks()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(
                new BackendRouteDecision(
                    Allowed: false,
                    Backend: ExecutionBackendKind.Helper,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "blocked"))
        };
        var profile = BuildProfile("spawn_context_entity", "spawn_tactical_entity");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.TacticalLand);

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn_context_entity", RuntimeMode.TacticalLand),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("contextActionId");
        result.Diagnostics!["contextActionId"]!.ToString().Should().Be("spawn_context_entity");
        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics["contextRoutedAction"]!.ToString().Should().Be("spawn_tactical_entity");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBlockContextRoute_WhenTargetActionIsMissing()
    {
        var harness = new AdapterHarness();
        var profile = BuildProfile("spawn_context_entity");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.TacticalLand);

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn_context_entity", RuntimeMode.TacticalLand),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING.ToString());
        result.Diagnostics.Should().ContainKey("routedActionId");
        result.Diagnostics["routedActionId"]!.ToString().Should().Be("spawn_tactical_entity");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldContinue_WhenMechanicDetectionThrows()
    {
        var harness = new AdapterHarness
        {
            MechanicDetectionService = new ThrowingMechanicDetectionService()
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteHelperActionAsync_ShouldFailClosed_WhenSessionIsMissing()
    {
        var harness = new AdapterHarness();
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        typeof(RuntimeAdapter)
            .GetProperty("CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(adapter, null);
        SetPrivateField(adapter, "_memory", null);

        var method = typeof(RuntimeAdapter).GetMethod("ExecuteHelperActionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = (Task<ActionExecutionResult>)method!.Invoke(adapter, new object?[]
        {
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None
        })!;
        var result = await task;

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE.ToString());
    }

    [Fact]
    public async Task ExecuteHelperActionAsync_ShouldFail_WhenHookIsMissing()
    {
        var harness = new AdapterHarness();
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        var request = new ActionExecutionRequest(
            Action: new ActionSpec(
                "set_hero_state_helper",
                ActionCategory.Hero,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: new JsonObject
            {
                ["helperHookId"] = "missing_hook"
            },
            ProfileId: "profile",
            RuntimeMode: RuntimeMode.Galactic,
            Context: null);

        var method = typeof(RuntimeAdapter).GetMethod("ExecuteHelperActionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var task = (Task<ActionExecutionResult>)method!.Invoke(adapter, new object?[] { request, CancellationToken.None })!;
        var result = await task;

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.HELPER_ENTRYPOINT_NOT_FOUND.ToString());
        result.Diagnostics["helperHookId"]!.ToString().Should().Be("missing_hook");
    }

    [Fact]
    public async Task ExecuteHelperActionAsync_ShouldFail_WhenProbeIsUnavailable()
    {
        var harness = new AdapterHarness
        {
            HelperBridgeBackend = new StubHelperBridgeBackend
            {
                ProbeResult = new HelperBridgeProbeResult(
                    Available: false,
                    ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                    Message: "probe unavailable",
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["helperBridgeState"] = "unavailable"
                    })
            }
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod("ExecuteHelperActionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var task = (Task<ActionExecutionResult>)method!.Invoke(adapter, new object?[]
        {
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None
        })!;
        var result = await task;

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("probe unavailable");
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE.ToString());
    }

    [Fact]
    public void ResolveMemoryActionSymbol_ShouldThrow_WhenPayloadSymbolMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveMemoryActionSymbol", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var payload = new JsonObject();

        Action act = () => method!.Invoke(null, new object?[] { payload });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>();
    }

    [Fact]
    public void TryReadCodePatchSymbol_ShouldReturnFailure_WhenSymbolMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadCodePatchSymbol", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var args = new object?[] { new JsonObject(), null, null };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeFalse();
        args[1].Should().BeNull();
        args[2].Should().BeOfType<ActionExecutionResult>();
        ((ActionExecutionResult)args[2]!).Message.Should().Contain("requires 'symbol'");
    }

    [Fact]
    public async Task ExecuteExtenderBackendActionAsync_ShouldReturnResult_WhenBackendIsConfigured()
    {
        var harness = new AdapterHarness();
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        var method = typeof(RuntimeAdapter).GetMethod("ExecuteExtenderBackendActionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var capability = new CapabilityReport(
            profile.Id,
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase),
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        var task = (Task<ActionExecutionResult>)method!.Invoke(adapter, new object?[]
        {
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            capability,
            CancellationToken.None
        })!;
        var result = await task;

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void CreateFallbackDisabledResult_ShouldEmitFallbackDisabledReasonCode()
    {
        var method = typeof(RuntimeAdapter).GetMethod("CreateFallbackDisabledResult", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ActionExecutionResult)method!.Invoke(null, new object?[] { "set_unit_cap_patch_fallback", "allow_unit_cap_patch_fallback" })!;
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be("fallback_disabled");
    }

    [Fact]
    public async Task ExecuteUnitCapPatchFallbackAsync_ShouldEmitReasonCodeDiagnostic()
    {
        var profile = BuildProfileWithFeatureFlags(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow_unit_cap_patch_fallback"] = true
            },
            "set_unit_cap_patch_fallback");
        var harness = new AdapterHarness();
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        var method = typeof(RuntimeAdapter).GetMethod("ExecuteUnitCapPatchFallbackAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = new ActionExecutionRequest(
            Action: new ActionSpec(
                "set_unit_cap_patch_fallback",
                ActionCategory.Global,
                RuntimeMode.Unknown,
                ExecutionKind.CodePatch,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: new JsonObject
            {
                ["enable"] = false
            },
            ProfileId: profile.Id,
            RuntimeMode: RuntimeMode.Galactic,
            Context: null);

        var task = (Task<ActionExecutionResult>)method!.Invoke(adapter, new object?[] { request })!;
        var result = await task;

        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.Should().NotBeNull();
        result.Diagnostics.Should().ContainKey("fallbackAction");
        result.Diagnostics["fallbackAction"]!.ToString().Should().Be("set_unit_cap_patch_fallback");
    }

    [Fact]
    public async Task ExecuteFogPatchFallbackAsync_ShouldReturnResolutionFailure_WhenMemoryReadFails()
    {
        var profile = BuildProfileWithFeatureFlags(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow_fog_patch_fallback"] = true
            },
            "toggle_fog_reveal_patch_fallback");
        var harness = new AdapterHarness();
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        var method = typeof(RuntimeAdapter).GetMethod("ExecuteFogPatchFallbackAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var request = new ActionExecutionRequest(
            Action: new ActionSpec(
                "toggle_fog_reveal_patch_fallback",
                ActionCategory.Global,
                RuntimeMode.Unknown,
                ExecutionKind.CodePatch,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: new JsonObject
            {
                ["enable"] = true
            },
            ProfileId: profile.Id,
            RuntimeMode: RuntimeMode.Galactic,
            Context: null);

        var task = (Task<ActionExecutionResult>)method!.Invoke(adapter, new object?[] { request })!;
        var result = await task;

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("fallbackAction");
        result.Diagnostics!["fallbackAction"]!.ToString().Should().Be("toggle_fog_reveal_patch_fallback");
        result.Diagnostics.Should().ContainKey("reasonCode");
    }

    [Fact]
    public void EnableFogPatchFallback_ShouldFailClosed_WhenMemoryAccessorIsMissing()
    {
        var profile = BuildProfileWithFeatureFlags(new Dictionary<string, bool>(), "toggle_fog_reveal_patch_fallback");
        var adapter = new AdapterHarness().CreateAdapter(profile, RuntimeMode.Galactic);
        SetPrivateField(adapter, "_memory", null);

        var resolution = CreateFogFallbackResolution((nint)0x1000, original: 0x74, patched: 0xEB, pattern: "74 0A");
        var result = (ActionExecutionResult)InvokePrivateInstance(adapter, "EnableFogPatchFallback", resolution)!;

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be("safety_fail_closed");
    }

    [Fact]
    public void DisableFogPatchFallback_ShouldFailClosed_WhenMemoryAccessorIsMissing()
    {
        var profile = BuildProfileWithFeatureFlags(new Dictionary<string, bool>(), "toggle_fog_reveal_patch_fallback");
        var adapter = new AdapterHarness().CreateAdapter(profile, RuntimeMode.Galactic);
        SetPrivateField(adapter, "_memory", null);

        var resolution = CreateFogFallbackResolution((nint)0x1000, original: 0x74, patched: 0xEB, pattern: "74 0A");
        var result = (ActionExecutionResult)InvokePrivateInstance(adapter, "DisableFogPatchFallback", resolution)!;

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be("safety_fail_closed");
    }

    [Fact]
    public void EnableFogPatchFallback_ShouldHandleAlreadyPatchedUnexpectedAndPatchedOutcomes()
    {
        var profile = BuildProfileWithFeatureFlags(new Dictionary<string, bool>(), "toggle_fog_reveal_patch_fallback");
        var adapter = new AdapterHarness().CreateAdapter(profile, RuntimeMode.Galactic);
        using var memoryAccessor = CreateProcessMemoryAccessor();
        SetPrivateField(adapter, "_memory", memoryAccessor);

        var address = Marshal.AllocHGlobal(1);
        try
        {
            var resolution = CreateFogFallbackResolution(address, original: 0x74, patched: 0xEB, pattern: "74 0A");

            Marshal.WriteByte(address, 0xEB);
            var alreadyPatched = (ActionExecutionResult)InvokePrivateInstance(adapter, "EnableFogPatchFallback", resolution)!;
            alreadyPatched.Succeeded.Should().BeTrue();
            alreadyPatched.Diagnostics!["state"]!.ToString().Should().Be("already_patched");
            alreadyPatched.Diagnostics["reasonCode"]!.ToString().Should().Be("fallback_applied");

            Marshal.WriteByte(address, 0x74);
            var patched = (ActionExecutionResult)InvokePrivateInstance(adapter, "EnableFogPatchFallback", resolution)!;
            patched.Succeeded.Should().BeTrue();
            patched.Diagnostics!["state"]!.ToString().Should().Be("patched");
            patched.Diagnostics["reasonCode"]!.ToString().Should().Be("fallback_applied");

            Marshal.WriteByte(address, 0x90);
            var unexpected = (ActionExecutionResult)InvokePrivateInstance(adapter, "EnableFogPatchFallback", resolution)!;
            unexpected.Succeeded.Should().BeFalse();
            unexpected.Diagnostics!["reasonCode"]!.ToString().Should().Be("safety_fail_closed");
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }

    [Fact]
    public void DisableFogPatchFallback_ShouldHandleAlreadyRestoredUnexpectedAndRestoredOutcomes()
    {
        var profile = BuildProfileWithFeatureFlags(new Dictionary<string, bool>(), "toggle_fog_reveal_patch_fallback");
        var adapter = new AdapterHarness().CreateAdapter(profile, RuntimeMode.Galactic);
        using var memoryAccessor = CreateProcessMemoryAccessor();
        SetPrivateField(adapter, "_memory", memoryAccessor);

        var address = Marshal.AllocHGlobal(1);
        try
        {
            var resolution = CreateFogFallbackResolution(address, original: 0x74, patched: 0xEB, pattern: "74 0A");

            Marshal.WriteByte(address, 0x74);
            var alreadyRestored = (ActionExecutionResult)InvokePrivateInstance(adapter, "DisableFogPatchFallback", resolution)!;
            alreadyRestored.Succeeded.Should().BeTrue();
            alreadyRestored.Diagnostics!["state"]!.ToString().Should().Be("already_restored");
            alreadyRestored.Diagnostics["reasonCode"]!.ToString().Should().Be("fallback_restored");

            Marshal.WriteByte(address, 0x90);
            var unexpected = (ActionExecutionResult)InvokePrivateInstance(adapter, "DisableFogPatchFallback", resolution)!;
            unexpected.Succeeded.Should().BeFalse();
            unexpected.Diagnostics!["reasonCode"]!.ToString().Should().Be("safety_fail_closed");

            Marshal.WriteByte(address, 0xEB);
            var restored = (ActionExecutionResult)InvokePrivateInstance(adapter, "DisableFogPatchFallback", resolution)!;
            restored.Succeeded.Should().BeTrue();
            restored.Diagnostics!["state"]!.ToString().Should().Be("restored");
            restored.Diagnostics["reasonCode"]!.ToString().Should().Be("fallback_restored");
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotBlock_WhenMechanicSupportIsMarkedSupported()
    {
        var harness = new AdapterHarness
        {
            MechanicDetectionService = new StubMechanicDetectionService(
                supported: true,
                actionId: "set_hero_state_helper",
                reasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                message: "supported")
        };
        var profile = BuildProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
    }

    private static ActionExecutionRequest BuildRequest(string actionId, RuntimeMode runtimeMode)
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
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: payload,
            ProfileId: "profile",
            RuntimeMode: runtimeMode);
    }

    private static TrainerProfile BuildProfile(params string[] actionIds)
    {
        var actions = actionIds.ToDictionary(
            id => id,
            id => new ActionSpec(
                id,
                ActionCategory.Hero,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            StringComparer.OrdinalIgnoreCase);

        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets:
            [
                new SignatureSet(
                    Name: "test",
                    GameBuild: "build",
                    Signatures:
                    [
                        new SignatureSpec("credits", "AA BB", 0)
                    ])
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

    private static TrainerProfile BuildProfileWithFeatureFlags(IReadOnlyDictionary<string, bool> featureFlags, params string[] actionIds)
    {
        var actions = actionIds.ToDictionary(
            id => id,
            id => new ActionSpec(
                id,
                ActionCategory.Global,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            StringComparer.OrdinalIgnoreCase);

        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(featureFlags, StringComparer.OrdinalIgnoreCase),
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

    private static AttachSession BuildSession(RuntimeMode runtimeMode)
    {
        return new AttachSession(
            "profile",
            new ProcessMetadata(
                ProcessId: Environment.ProcessId,
                ProcessName: "swfoc",
                ProcessPath: @"C:\Games\swfoc.exe",
                CommandLine: null,
                ExeTarget: ExeTarget.Swfoc,
                Mode: runtimeMode,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new ProfileBuild("profile", "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            DateTimeOffset.UtcNow);
    }

    private static object CreateUninitializedMemoryAccessor()
    {
        var memoryType = typeof(RuntimeAdapter).Assembly.GetType("SwfocTrainer.Runtime.Interop.ProcessMemoryAccessor");
        memoryType.Should().NotBeNull();
        return RuntimeHelpers.GetUninitializedObject(memoryType!);
    }

    private static IDisposable CreateProcessMemoryAccessor()
    {
        var memoryType = typeof(RuntimeAdapter).Assembly.GetType("SwfocTrainer.Runtime.Interop.ProcessMemoryAccessor");
        memoryType.Should().NotBeNull();
        var accessor = Activator.CreateInstance(memoryType!, Environment.ProcessId);
        accessor.Should().NotBeNull();
        accessor.Should().BeAssignableTo<IDisposable>();
        return (IDisposable)accessor!;
    }

    private static object CreateFogFallbackResolution(nint address, byte original, byte patched, string pattern)
    {
        var resolutionType = typeof(RuntimeAdapter).GetNestedType("FogPatchFallbackResolution", BindingFlags.NonPublic);
        resolutionType.Should().NotBeNull();
        var method = resolutionType!.GetMethod("Ok", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.Invoke(null, new object?[] { address, original, patched, pattern })!;
    }

    private static object? InvokePrivateInstance(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull($"private method '{methodName}' should exist.");
        return method!.Invoke(instance, arguments);
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field {fieldName} should exist.");
        field!.SetValue(instance, value);
    }

    private sealed class AdapterHarness
    {
        public IProcessLocator ProcessLocator { get; set; } = new StubProcessLocator();
        public bool IncludeExecutionBackend { get; set; } = true;

        public IBackendRouter Router { get; set; } = new StubBackendRouter(
            new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Helper,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"));

        public IExecutionBackend ExecutionBackend { get; set; } = new StubExecutionBackend();

        public IHelperBridgeBackend HelperBridgeBackend { get; set; } = new StubHelperBridgeBackend();

        public IModDependencyValidator DependencyValidator { get; set; } = new StubDependencyValidator(
            new DependencyValidationResult(
                DependencyValidationStatus.Pass,
                string.Empty,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        public IModMechanicDetectionService? MechanicDetectionService { get; set; }

        public RuntimeAdapter CreateAdapter(TrainerProfile profile, RuntimeMode mode)
        {
            var services = new Dictionary<Type, object>
            {
                [typeof(IBackendRouter)] = Router,
                [typeof(IHelperBridgeBackend)] = HelperBridgeBackend,
                [typeof(IModDependencyValidator)] = DependencyValidator,
                [typeof(ITelemetryLogTailService)] = new StubTelemetryLogTailService()
            };
            if (IncludeExecutionBackend)
            {
                services[typeof(IExecutionBackend)] = ExecutionBackend;
            }
            if (MechanicDetectionService is not null)
            {
                services[typeof(IModMechanicDetectionService)] = MechanicDetectionService;
            }

            var adapter = new RuntimeAdapter(
                ProcessLocator,
                new StubProfileRepository(profile),
                new StubSignatureResolver(),
                NullLogger<RuntimeAdapter>.Instance,
                new MapServiceProvider(services));

            var session = BuildSession(mode);
            typeof(RuntimeAdapter)
                .GetProperty("CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .SetValue(adapter, session);
            SetPrivateField(adapter, "_attachedProfile", profile);
            SetPrivateField(adapter, "_memory", CreateUninitializedMemoryAccessor());
            return adapter;
        }
    }

    private sealed class StubBackendRouter(BackendRouteDecision decision) : IBackendRouter
    {
        public BackendRouteDecision Decision { get; set; } = decision;

        public BackendRouteDecision Resolve(
            ActionExecutionRequest request,
            TrainerProfile profile,
            ProcessMetadata process,
            CapabilityReport capabilityReport)
        {
            _ = request;
            _ = profile;
            _ = process;
            _ = capabilityReport;
            return Decision;
        }
    }

    private sealed class StubExecutionBackend : IExecutionBackend
    {
        public ExecutionBackendKind BackendKind { get; set; } = ExecutionBackendKind.Extender;

        public CapabilityReport ProbeReport { get; set; } = new(
            "profile",
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase),
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);

        public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext)
        {
            _ = profileId;
            _ = processContext;
            return Task.FromResult(ProbeReport);
        }

        public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = processContext;
            _ = cancellationToken;
            return Task.FromResult(ProbeReport);
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport)
        {
            _ = command;
            _ = capabilityReport;
            return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.None));
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport, CancellationToken cancellationToken)
        {
            _ = command;
            _ = capabilityReport;
            _ = cancellationToken;
            return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.None));
        }

        public Task<BackendHealth> GetHealthAsync()
            => Task.FromResult(new BackendHealth("stub", BackendKind, true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok"));

        public Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return GetHealthAsync();
        }
    }

    private sealed class StubHelperBridgeBackend : IHelperBridgeBackend
    {
        public HelperBridgeProbeResult ProbeResult { get; set; } = new(
            Available: true,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "ok");

        public HelperBridgeExecutionResult ExecuteResult { get; set; } = new(
            Succeeded: true,
            ReasonCode: RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
            Message: "applied");

        public Exception? ExecuteException { get; set; }

        public Task<HelperBridgeProbeResult> ProbeAsync(HelperBridgeProbeRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(ProbeResult);
        }

        public Task<HelperBridgeExecutionResult> ExecuteAsync(HelperBridgeRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            if (ExecuteException is not null)
            {
                throw ExecuteException;
            }

            return Task.FromResult(ExecuteResult);
        }
    }

    private sealed class StubDependencyValidator(DependencyValidationResult result) : IModDependencyValidator
    {
        public DependencyValidationResult Result { get; set; } = result;

        public DependencyValidationResult Validate(TrainerProfile profile, ProcessMetadata process)
        {
            _ = profile;
            _ = process;
            return Result;
        }
    }

    private sealed class StubMechanicDetectionService(
        bool supported,
        string actionId,
        RuntimeReasonCode reasonCode,
        string message) : IModMechanicDetectionService
    {
        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken cancellationToken)
        {
            _ = session;
            _ = catalog;
            _ = cancellationToken;
            return Task.FromResult(new ModMechanicReport(
                ProfileId: profile.Id,
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                DependenciesSatisfied: true,
                HelperBridgeReady: true,
                ActionSupport:
                [
                    new ModMechanicSupport(
                        ActionId: actionId,
                        Supported: supported,
                        ReasonCode: reasonCode,
                        Message: message,
                        Confidence: 0.9d)
                ],
                Diagnostics: new Dictionary<string, object?> { ["supportSource"] = "stub" }));
        }
    }

    private sealed class StubTelemetryLogTailService : ITelemetryLogTailService
    {
        public TelemetryModeResolution ResolveLatestMode(string? processPath, DateTimeOffset nowUtc, TimeSpan freshnessWindow)
        {
            _ = processPath;
            _ = nowUtc;
            _ = freshnessWindow;
            return TelemetryModeResolution.Unavailable("stub");
        }
    }

    private sealed class StubProcessLocator : IProcessLocator
    {
        private readonly ProcessMetadata? _bestMatch;

        public StubProcessLocator()
        {
        }

        public StubProcessLocator(ProcessMetadata bestMatch)
        {
            _bestMatch = bestMatch;
        }

        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<ProcessMetadata>>(
                _bestMatch is null ? Array.Empty<ProcessMetadata>() : [_bestMatch]);
        }

        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken)
        {
            _ = target;
            _ = cancellationToken;
            return Task.FromResult(_bestMatch);
        }
    }

    private sealed class ThrowingMechanicDetectionService : IModMechanicDetectionService
    {
        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken cancellationToken)
        {
            _ = profile;
            _ = session;
            _ = catalog;
            _ = cancellationToken;
            throw new InvalidOperationException("detector failed");
        }
    }

    private sealed class StubProfileRepository(TrainerProfile profile) : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(profile);
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(profile);
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            _ = profile;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }

    private sealed class StubSignatureResolver : ISignatureResolver
    {
        public Task<SymbolMap> ResolveAsync(
            ProfileBuild build,
            IReadOnlyList<SignatureSet> signatureSets,
            IReadOnlyDictionary<string, long> fallbackOffsets,
            CancellationToken cancellationToken)
        {
            _ = build;
            _ = signatureSets;
            _ = fallbackOffsets;
            _ = cancellationToken;
            return Task.FromResult(new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)));
        }
    }

    private sealed class MapServiceProvider(IReadOnlyDictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => services.TryGetValue(serviceType, out var service) ? service : null;
    }
}


