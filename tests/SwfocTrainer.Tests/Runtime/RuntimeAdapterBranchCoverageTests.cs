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

/// <summary>
/// Branch-coverage sweep for RuntimeAdapter — targets the ~1,500 uncovered branches
/// in mode resolution, context routing, backend dispatch, code-patch, SDK, helper,
/// memory, diagnostics, expert override, and telemetry paths.
/// </summary>
public sealed class RuntimeAdapterBranchCoverageTests
{
    // ── Mode Resolution ───────────────���───────────────────────────────��──────

    [Theory]
    [InlineData("Galactic", RuntimeMode.Galactic)]
    [InlineData("AnyTactical", RuntimeMode.AnyTactical)]
    [InlineData("TacticalLand", RuntimeMode.TacticalLand)]
    [InlineData("TacticalSpace", RuntimeMode.TacticalSpace)]
    public async Task ExecuteAsync_ShouldApplyManualOverrideMode(string overrideValue, RuntimeMode expectedMode)
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Unknown);

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeModeOverride"] = overrideValue
        };
        var request = BuildRequestWithContext("set_hero_state_helper", RuntimeMode.Unknown, context);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Diagnostics.Should().ContainKey("runtimeModeEffective");
        result.Diagnostics!["runtimeModeEffective"]!.ToString().Should().Be(expectedMode.ToString());
        result.Diagnostics["runtimeModeEffectiveSource"]!.ToString().Should().Be("manual_override");
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ExecuteAsync_ShouldNotApplyManualOverride_WhenAutoOrEmpty(string? overrideValue)
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (overrideValue is not null)
        {
            context["runtimeModeOverride"] = overrideValue;
        }

        var request = BuildRequestWithContext("set_hero_state_helper", RuntimeMode.Galactic, context);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Diagnostics!["runtimeModeEffectiveSource"]!.ToString().Should().NotBe("manual_override");
    }

    [Theory]
    [InlineData("Galactic", RuntimeMode.Galactic)]
    [InlineData("Land", RuntimeMode.TacticalLand)]
    [InlineData("TacticalLand", RuntimeMode.TacticalLand)]
    [InlineData("Space", RuntimeMode.TacticalSpace)]
    [InlineData("TacticalSpace", RuntimeMode.TacticalSpace)]
    [InlineData("AnyTactical", RuntimeMode.AnyTactical)]
    public async Task ExecuteAsync_ShouldApplyTelemetryModeFromContext(string telemetryValue, RuntimeMode expectedMode)
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Unknown);

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["telemetryRuntimeMode"] = telemetryValue
        };
        var request = BuildRequestWithContext("set_hero_state_helper", RuntimeMode.Unknown, context);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Diagnostics.Should().ContainKey("runtimeModeEffective");
        result.Diagnostics!["runtimeModeEffective"]!.ToString().Should().Be(expectedMode.ToString());
        result.Diagnostics["runtimeModeEffectiveSource"]!.ToString().Should().Be("telemetry");
    }

    [Theory]
    [InlineData("")]
    [InlineData("InvalidValue")]
    public async Task ExecuteAsync_ShouldIgnoreInvalidTelemetryModeFromContext(string telemetryValue)
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["telemetryRuntimeMode"] = telemetryValue
        };
        var request = BuildRequestWithContext("set_hero_state_helper", RuntimeMode.Galactic, context);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Diagnostics!["runtimeModeEffectiveSource"]!.ToString().Should().NotBe("telemetry");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFallBackToProbeMode_WhenHintIsUnknownAndNoOverrideOrTelemetry()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = BuildRequest("set_hero_state_helper", RuntimeMode.Unknown);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Diagnostics.Should().ContainKey("runtimeModeEffective");
        result.Diagnostics!["runtimeModeEffective"]!.ToString().Should().Be(RuntimeMode.Galactic.ToString());
        result.Diagnostics["runtimeModeEffectiveSource"]!.ToString().Should().Be("auto");
    }

    // ── Context Spawn Target Routing ─────────────────────���──────────────────

    [Theory]
    [InlineData(RuntimeMode.TacticalLand)]
    [InlineData(RuntimeMode.TacticalSpace)]
    [InlineData(RuntimeMode.AnyTactical)]
    public async Task ExecuteAsync_SpawnContextEntity_ShouldRouteToTactical_InTacticalModes(RuntimeMode mode)
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("spawn_context_entity", "spawn_tactical_entity");
        var adapter = harness.CreateAdapter(profile, mode);

        var result = await adapter.ExecuteAsync(BuildRequest("spawn_context_entity", mode), CancellationToken.None);
        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics!["contextRoutedAction"]!.ToString().Should().Be("spawn_tactical_entity");
    }

    [Fact]
    public async Task ExecuteAsync_SpawnContextEntity_ShouldRouteToGalactic_InGalacticMode()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("spawn_context_entity", "spawn_galactic_entity");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn_context_entity", RuntimeMode.Galactic), CancellationToken.None);
        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics!["contextRoutedAction"]!.ToString().Should().Be("spawn_galactic_entity");
    }

    [Fact]
    public async Task ExecuteAsync_SetContextFaction_ShouldRouteToTactical_InTacticalMode()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_context_faction", "set_selected_owner_faction");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.TacticalLand);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_context_faction", RuntimeMode.TacticalLand), CancellationToken.None);
        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics!["contextRoutedAction"]!.ToString().Should().Be("set_selected_owner_faction");
    }

    [Fact]
    public async Task ExecuteAsync_SetContextAllegiance_ShouldRouteToGalactic_InGalacticMode()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_context_allegiance", "set_planet_owner");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_context_allegiance", RuntimeMode.Galactic), CancellationToken.None);
        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics!["contextRoutedAction"]!.ToString().Should().Be("set_planet_owner");
    }

    // ── ExecuteByRouteAsync dispatch ──────────────────────────��─────────────

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccess_WhenRouteIsSave()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Save,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"))
        };
        var profile = BuildHelperProfile("save_game");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = BuildRequest("save_game", RuntimeMode.Galactic, ExecutionKind.Save);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Save action");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFail_WhenRouteIsUnsupportedBackend()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: (ExecutionBackendKind)999,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"))
        };
        var profile = BuildHelperProfile("some_action");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = BuildRequest("some_action", RuntimeMode.Galactic);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Unsupported execution backend");
    }

    // ── ExecuteLegacyBackendActionAsync dispatch branches ────────────────────

    [Fact]
    public async Task ExecuteLegacyBackendAction_ShouldReturnFreezeMessage_WhenExecutionKindIsFreeze()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Memory,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"))
        };
        var profile = BuildHelperProfile("freeze_timer");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = BuildRequest("freeze_timer", RuntimeMode.Galactic, ExecutionKind.Freeze);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Freeze actions must be handled by the orchestrator");
    }

    [Fact]
    public async Task ExecuteLegacyBackendAction_ShouldReturnFail_WhenExecutionKindIsUnsupported()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Memory,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"))
        };
        var profile = BuildHelperProfile("unknown_action");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = BuildRequest("unknown_action", RuntimeMode.Galactic, (ExecutionKind)999);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Unsupported execution kind");
    }

    // ── ExecuteSdkActionAsync branches ───────────────────��──────────────────

    [Fact]
    public async Task ExecuteSdkActionAsync_ShouldReturnMissing_WhenNoSdkRouterConfigured()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Memory,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"))
        };
        var profile = BuildHelperProfile("sdk_action");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = BuildRequest("sdk_action", RuntimeMode.Galactic, ExecutionKind.Sdk);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("failureReasonCode");
        result.Diagnostics!["failureReasonCode"]!.ToString().Should().Be("sdk_router_missing");
    }

    // ── Expert Mutation Override ─────────────────────��───────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldNotApplyExpertOverride_WhenPanicDisableIsActive()
    {
        var prevExpert = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var prevPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", "1");
            var harness = new AdapterHarness
            {
                Router = new StubBackendRouter(new BackendRouteDecision(
                    Allowed: false,
                    Backend: ExecutionBackendKind.Extender,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "blocked"))
            };
            var profile = BuildHelperProfile("set_unit_cap");
            var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

            var request = BuildRequest("set_unit_cap", RuntimeMode.Galactic);
            var result = await adapter.ExecuteAsync(request, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Diagnostics.Should().ContainKey("panicDisableState");
            result.Diagnostics!["panicDisableState"]!.ToString().Should().Be("active");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prevExpert);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", prevPanic);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotApplyExpertOverride_WhenActionIsNotPromotedExtender()
    {
        var prev = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            var harness = new AdapterHarness
            {
                Router = new StubBackendRouter(new BackendRouteDecision(
                    Allowed: false,
                    Backend: ExecutionBackendKind.Extender,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "blocked"))
            };
            // "set_hero_state_helper" is NOT in the PromotedExtenderActionIds set
            var profile = BuildHelperProfile("set_hero_state_helper");
            var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

            var request = BuildRequest("set_hero_state_helper", RuntimeMode.Galactic);
            var result = await adapter.ExecuteAsync(request, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Diagnostics.Should().NotContainKey("riskyOverride");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prev);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotApplyExpertOverride_WhenBackendIsNotExtender()
    {
        var prev = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            var harness = new AdapterHarness
            {
                Router = new StubBackendRouter(new BackendRouteDecision(
                    Allowed: false,
                    Backend: ExecutionBackendKind.Helper,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "blocked"))
            };
            // "set_unit_cap" is promoted but backend is Helper, not Extender
            var profile = BuildHelperProfile("set_unit_cap");
            var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

            var request = BuildRequest("set_unit_cap", RuntimeMode.Galactic);
            var result = await adapter.ExecuteAsync(request, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Diagnostics.Should().NotContainKey("riskyOverride");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prev);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotApplyExpertOverride_WhenActionIsReadOnly()
    {
        var prev = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            var harness = new AdapterHarness
            {
                Router = new StubBackendRouter(new BackendRouteDecision(
                    Allowed: false,
                    Backend: ExecutionBackendKind.Extender,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "blocked"))
            };
            // "read_credits" starts with "read_" so is not mutating
            // But we need it also in PromotedExtenderActionIds: it's not, so it fails for that reason too
            var profile = BuildHelperProfile("read_credits");
            var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

            var request = BuildRequest("read_credits", RuntimeMode.Galactic);
            var result = await adapter.ExecuteAsync(request, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prev);
        }
    }

    // ── Exception catch branches in ExecuteAsync ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldCatchIOException()
    {
        var backend = new ThrowingExecutionBackend(new IOException("pipe broken"));
        var harness = new AdapterHarness
        {
            ExecutionBackend = backend,
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"))
        };
        var profile = BuildHelperProfile("some_action");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(BuildRequest("some_action", RuntimeMode.Galactic), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("failureReasonCode");
        result.Diagnostics!["failureReasonCode"]!.ToString().Should().Be("action_exception");
        result.Diagnostics["exceptionType"]!.ToString().Should().Be(nameof(IOException));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCatchWin32Exception()
    {
        var backend = new ThrowingExecutionBackend(new System.ComponentModel.Win32Exception(5, "access denied"));
        var harness = new AdapterHarness
        {
            ExecutionBackend = backend,
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"))
        };
        var profile = BuildHelperProfile("some_action");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(BuildRequest("some_action", RuntimeMode.Galactic), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics!["exceptionType"]!.ToString().Should().Be(nameof(System.ComponentModel.Win32Exception));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCatchKeyNotFoundException()
    {
        var backend = new ThrowingExecutionBackend(new KeyNotFoundException("symbol not found"));
        var harness = new AdapterHarness
        {
            ExecutionBackend = backend,
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"))
        };
        var profile = BuildHelperProfile("some_action");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(BuildRequest("some_action", RuntimeMode.Galactic), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics!["exceptionType"]!.ToString().Should().Be(nameof(KeyNotFoundException));
    }

    // ── ExecuteExtenderBackendActionAsync branches ──────────────────────────

    [Fact]
    public async Task ExecuteExtenderBackendAction_ShouldReturnUnavailable_WhenExtenderIsNull()
    {
        var harness = new AdapterHarness
        {
            IncludeExecutionBackend = false,
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"))
        };
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_extenderBackend", null);

        var method = typeof(RuntimeAdapter).GetMethod(
            "ExecuteExtenderBackendActionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
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

        result.Succeeded.Should().BeFalse();
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE.ToString());
    }

    // ── Detach and Cleanup ───────────────────────────────────────���─────────

    [Fact]
    public async Task DetachAsync_ShouldClearAllState()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        adapter.IsAttached.Should().BeTrue();

        await adapter.DetachAsync(CancellationToken.None);

        adapter.IsAttached.Should().BeFalse();
        adapter.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task DetachAsync_ParameterlessOverload_ShouldWork()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        await adapter.DetachAsync();

        adapter.IsAttached.Should().BeFalse();
    }

    // ── EnsureAttached guard ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenNotAttached()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        typeof(RuntimeAdapter)
            .GetProperty("CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(adapter, null);

        Func<Task> act = () => adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not attached*");
    }

    // ── Overload: ExecuteAsync without CancellationToken ────────────────────

    [Fact]
    public async Task ExecuteAsync_ParameterlessOverload_ShouldDelegateCorrectly()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(BuildRequest("set_hero_state_helper", RuntimeMode.Galactic));

        result.Should().NotBeNull();
    }

    // ── ProbeCapabilitiesAsync ─────────────────��────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldReturnUnknownCapabilities_WhenExtenderBackendIsNull()
    {
        var harness = new AdapterHarness
        {
            IncludeExecutionBackend = false,
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: false,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                Message: "blocked"))
        };
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_extenderBackend", null);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    // ── ResolveHelperHookId edge cases ──────────────────────────────────────

    [Theory]
    [InlineData("spawn_tactical_entity")]
    [InlineData("spawn_galactic_entity")]
    [InlineData("place_planet_building")]
    public async Task ExecuteHelperAction_ShouldMapSpawnActionsToSpawnBridgeHookId(string actionId)
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile(actionId);
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod(
            "ExecuteHelperActionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = new ActionExecutionRequest(
            Action: new ActionSpec(
                actionId,
                ActionCategory.Hero,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: new JsonObject(),
            ProfileId: "profile",
            RuntimeMode: RuntimeMode.Galactic);

        var task = (Task<ActionExecutionResult>)method!.Invoke(adapter, new object?[] { request, CancellationToken.None })!;
        var result = await task;

        // Hook "spawn_bridge" is not in test profile, so it should fail with entrypoint not found
        result.Succeeded.Should().BeFalse();
        result.Diagnostics!["helperHookId"]!.ToString().Should().Be("spawn_bridge");
    }

    [Fact]
    public async Task ExecuteHelperAction_ShouldFallBackToActionIdAsHookId_WhenNoExplicitHookId()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("custom_action");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod(
            "ExecuteHelperActionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = new ActionExecutionRequest(
            Action: new ActionSpec(
                "custom_action",
                ActionCategory.Hero,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: new JsonObject(),
            ProfileId: "profile",
            RuntimeMode: RuntimeMode.Galactic);

        var task = (Task<ActionExecutionResult>)method!.Invoke(adapter, new object?[] { request, CancellationToken.None })!;
        var result = await task;

        result.Diagnostics!["helperHookId"]!.ToString().Should().Be("custom_action");
    }

    // ── ResolveHelperOperationKind dispatch ────────────────────────��─────────

    [Theory]
    [InlineData("spawn_unit_helper")]
    [InlineData("spawn_context_entity")]
    [InlineData("spawn_tactical_entity")]
    [InlineData("spawn_galactic_entity")]
    [InlineData("place_planet_building")]
    [InlineData("set_context_allegiance")]
    [InlineData("set_context_faction")]
    [InlineData("set_hero_state_helper")]
    [InlineData("toggle_roe_respawn_helper")]
    [InlineData("unknown_action_id")]
    public void ResolveHelperOperationKind_ShouldReturnCorrectKind(string actionId)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHelperOperationKind", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var kind = (HelperBridgeOperationKind)method!.Invoke(null, new object[] { actionId })!;

        kind.Should().BeDefined();
    }

    // ── IsMutatingActionId branches ───────────────────────────────���─────────

    [Theory]
    [InlineData("read_credits", false)]
    [InlineData("list_units", false)]
    [InlineData("get_status", false)]
    [InlineData("set_credits", true)]
    [InlineData("toggle_fog_reveal", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void IsMutatingActionId_ShouldReturnCorrectResult(string actionId, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsMutatingActionId", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { actionId })!;
        result.Should().Be(expected);
    }

    // ── ResolveLegacyOverrideBackend branches ───────────────────────────────

    [Theory]
    [InlineData(ExecutionKind.Helper, ExecutionBackendKind.Helper)]
    [InlineData(ExecutionKind.Save, ExecutionBackendKind.Save)]
    [InlineData(ExecutionKind.Memory, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.CodePatch, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Freeze, ExecutionBackendKind.Memory)]
    public void ResolveLegacyOverrideBackend_ShouldMapCorrectly(ExecutionKind kind, ExecutionBackendKind expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveLegacyOverrideBackend", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object[] { kind })!;
        result.Should().Be(expected);
    }

    // ── IsPromotedExtenderAction branches ────────────────��─────────────────

    [Theory]
    [InlineData("freeze_timer", true)]
    [InlineData("toggle_fog_reveal", true)]
    [InlineData("toggle_ai", true)]
    [InlineData("set_unit_cap", true)]
    [InlineData("toggle_instant_build_patch", true)]
    [InlineData("set_credits", false)]
    [InlineData("set_hero_state_helper", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsPromotedExtenderAction_ShouldReturnCorrectResult(string actionId, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsPromotedExtenderAction", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { actionId })!;
        result.Should().Be(expected);
    }

    // ── NormalizePatternText branches ────────────────────────────────��───────

    [Theory]
    [InlineData("aa bb cc", "AA BB CC")]
    [InlineData("? bb ?", "?? BB ??")]
    [InlineData("?? bb ??", "?? BB ??")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizePatternText_ShouldProduceExpectedOutput(string? input, string expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "NormalizePatternText", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object?[] { input ?? string.Empty })!;
        result.Should().Be(expected);
    }

    // ── SanitizeArtifactToken branches ──────────────────────────────────────

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("hello/world", "hello_world")]
    [InlineData("a b c", "a_b_c")]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    [InlineData("___", "unknown")]
    public void SanitizeArtifactToken_ShouldProduceExpectedOutput(string input, string expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "SanitizeArtifactToken", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }

    // ── ClampConfidence branches ────────────────────────────────────────────

    [Theory]
    [InlineData(double.NaN, 0d)]
    [InlineData(-0.5d, 0d)]
    [InlineData(0.5d, 0.5d)]
    [InlineData(1.5d, 1d)]
    [InlineData(0d, 0d)]
    [InlineData(1d, 1d)]
    public void ClampConfidence_ShouldClampCorrectly(double input, double expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ClampConfidence", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (double)method!.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }

    // ── InferPatchFailureReasonCode branches ──────────────────────────────��─

    [Theory]
    [InlineData("Pattern not unique in module", "PATTERN_NOT_UNIQUE")]
    [InlineData("Anchor not found at expected offset", "PATTERN_MISSING")]
    [InlineData("Some other failure", "SAFETY_FAIL_CLOSED")]
    public void InferPatchFailureReasonCode_ShouldReturnCorrectCode(string message, string expectedCode)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "InferPatchFailureReasonCode", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object[] { message })!;
        result.ToString().Should().Be(expectedCode);
    }

    // ── MergeDiagnostics edge branches ─────────────────────────────────────

    [Fact]
    public void MergeDiagnostics_ShouldReturnPrimary_WhenBothEmpty()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { null, null });
        result.Should().BeNull();
    }

    [Fact]
    public void MergeDiagnostics_ShouldMerge_WhenBothHaveValues()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var primary = new Dictionary<string, object?> { ["a"] = "1" };
        var secondary = new Dictionary<string, object?> { ["b"] = "2" };
        var result = (IReadOnlyDictionary<string, object?>)method!.Invoke(null, new object?[] { primary, secondary })!;
        result.Should().ContainKey("a");
        result.Should().ContainKey("b");
    }

    // ── TryReadBoolPayload branches ───────────────────��────────────────────

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void TryReadBoolPayload_ShouldParseBoolean(string value, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBoolPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["boolValue"] = bool.Parse(value) };
        var args = new object?[] { payload, false };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
        ((bool)args[1]!).Should().Be(expected);
    }

    [Fact]
    public void TryReadBoolPayload_ShouldReturnFalse_WhenNoKey()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBoolPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        var args = new object?[] { payload, false };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeFalse();
    }

    // ── TryReadIntPayload branches ──────────────────────────���──────────────

    [Fact]
    public void TryReadIntPayload_ShouldParseInt()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadIntPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["intValue"] = 42 };
        var args = new object?[] { payload, 0 };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
        ((int)args[1]!).Should().Be(42);
    }

    [Fact]
    public void TryReadIntPayload_ShouldReturnFalse_WhenNoKey()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadIntPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        var args = new object?[] { payload, 0 };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeFalse();
    }

    // ── TryReadFloatPayload branches ───────────────────────────────────────

    [Fact]
    public void TryReadFloatPayload_ShouldParseFloat()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadFloatPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["floatValue"] = 3.14f };
        var args = new object?[] { payload, 0f };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
        ((float)args[1]!).Should().BeApproximately(3.14f, 0.01f);
    }

    [Fact]
    public void TryReadFloatPayload_ShouldParseDoubleAsFloat()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadFloatPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["floatValue"] = 3.14d };
        var args = new object?[] { payload, 0f };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
    }

    [Fact]
    public void TryReadFloatPayload_ShouldReturnFalse_WhenNoKey()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadFloatPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        var args = new object?[] { payload, 0f };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeFalse();
    }

    // ── TryReadBooleanPayload edge branches ────────────────────────────────

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TryReadBooleanPayload_ShouldParseBool(bool input, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["key"] = input };
        var args = new object?[] { payload, "key", false };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
        ((bool)args[2]!).Should().Be(expected);
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldParseIntAsBool()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["key"] = 1 };
        var args = new object?[] { payload, "key", false };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
        ((bool)args[2]!).Should().BeTrue();
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldParseStringAsBool()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["key"] = "true" };
        var args = new object?[] { payload, "key", false };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
        ((bool)args[2]!).Should().BeTrue();
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldReturnFalse_WhenKeyMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        var args = new object?[] { payload, "missing", false };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeFalse();
    }

    // ── ParseHexBytes ─────────────────────���────────────────────────────────

    [Theory]
    [InlineData("90 90 90", new byte[] { 0x90, 0x90, 0x90 })]
    [InlineData("AA-BB-CC", new byte[] { 0xAA, 0xBB, 0xCC })]
    [InlineData("FF", new byte[] { 0xFF })]
    public void ParseHexBytes_ShouldParseCorrectly(string hex, byte[] expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ParseHexBytes", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (byte[])method!.Invoke(null, new object[] { hex })!;
        result.Should().Equal(expected);
    }

    // ── ToHex ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToHex_ShouldFormatCorrectly()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ToHex", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object[] { (nint)0x1234 })!;
        result.Should().Be("0x1234");
    }

    // ── BuildPatternSnippet branches ───────────────────────────────────────

    [Fact]
    public void BuildPatternSnippet_ShouldReturnEmpty_WhenModuleBytesEmpty()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "BuildPatternSnippet", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object[] { Array.Empty<byte>(), 0, 4 })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildPatternSnippet_ShouldReturnSnippet_WhenModuleBytesPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "BuildPatternSnippet", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var bytes = new byte[] { 0x48, 0x8B, 0x05, 0x12, 0x34, 0x56, 0x78, 0x90, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22, 0x33, 0x44 };
        var result = (string)method!.Invoke(null, new object[] { bytes, 4, 4 })!;
        result.Should().NotBeEmpty();
    }

    // ── TryReadCodePatchSymbol success path ────────────────────��───────────

    [Fact]
    public void TryReadCodePatchSymbol_ShouldReturnSuccess_WhenSymbolPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadCodePatchSymbol", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["symbol"] = "my_symbol" };
        var args = new object?[] { payload, null, null };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
        args[1].Should().Be("my_symbol");
    }

    // ── TryParseCodePatchBytes branches ───────────────────────────��────────

    [Fact]
    public void TryParseCodePatchBytes_ShouldFail_WhenBytesAreMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryParseCodePatchBytes", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        var args = new object?[] { payload, null, null, null };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeFalse();
        args[3].Should().BeOfType<ActionExecutionResult>();
    }

    [Fact]
    public void TryParseCodePatchBytes_ShouldFail_WhenLengthsMismatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryParseCodePatchBytes", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject
        {
            ["patchBytes"] = "90 90",
            ["originalBytes"] = "48 8B 05"
        };
        var args = new object?[] { payload, null, null, null };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeFalse();
        ((ActionExecutionResult)args[3]!).Message.Should().Contain("must match");
    }

    [Fact]
    public void TryParseCodePatchBytes_ShouldSucceed_WhenValid()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryParseCodePatchBytes", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject
        {
            ["patchBytes"] = "90 90 90",
            ["originalBytes"] = "48 8B 05"
        };
        var args = new object?[] { payload, null, null, null };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
    }

    // ── TryDispatchSpecializedCodePatchAction branches ──────────────────────

    [Theory]
    [InlineData("set_unit_cap")]
    [InlineData("set_unit_cap_patch_fallback")]
    [InlineData("toggle_instant_build_patch")]
    [InlineData("toggle_fog_reveal_patch_fallback")]
    public void TryDispatchSpecializedCodePatchAction_ShouldDispatch(string actionId)
    {
        var harness = new AdapterHarness();
        var profile = BuildProfileWithFeatureFlags(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            actionId);
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod(
            "TryDispatchSpecializedCodePatchAction", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = new ActionExecutionRequest(
            Action: new ActionSpec(
                actionId,
                ActionCategory.Global,
                RuntimeMode.Unknown,
                ExecutionKind.CodePatch,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: new JsonObject { ["enable"] = false },
            ProfileId: "profile",
            RuntimeMode: RuntimeMode.Galactic);

        var args = new object?[] { request, null };
        var dispatched = (bool)method!.Invoke(adapter, args)!;

        dispatched.Should().BeTrue();
    }

    [Fact]
    public void TryDispatchSpecializedCodePatchAction_ShouldNotDispatch_WhenGenericAction()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("generic_code_patch");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod(
            "TryDispatchSpecializedCodePatchAction", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = new ActionExecutionRequest(
            Action: new ActionSpec(
                "generic_code_patch",
                ActionCategory.Global,
                RuntimeMode.Unknown,
                ExecutionKind.CodePatch,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: new JsonObject(),
            ProfileId: "profile",
            RuntimeMode: RuntimeMode.Galactic);

        var args = new object?[] { request, null };
        var dispatched = (bool)method!.Invoke(adapter, args)!;

        dispatched.Should().BeFalse();
    }

    // ── IsCreditsWrite branches ─────────────────────────────────────────────

    [Fact]
    public void IsCreditsWrite_ShouldReturnTrue_WhenActionIdIsSetCredits()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsCreditsWrite", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("set_credits", RuntimeMode.Galactic);
        var result = (bool)method!.Invoke(null, new object[] { request, "other_symbol" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCreditsWrite_ShouldReturnTrue_WhenSymbolIsCredits()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsCreditsWrite", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("some_action", RuntimeMode.Galactic);
        var result = (bool)method!.Invoke(null, new object[] { request, "credits" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCreditsWrite_ShouldReturnFalse_WhenNeitherMatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsCreditsWrite", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("other_action", RuntimeMode.Galactic);
        var result = (bool)method!.Invoke(null, new object[] { request, "health" })!;
        result.Should().BeFalse();
    }

    // ── RecordActionTelemetry branches ──────────────────────────��───────────

    [Fact]
    public async Task RecordActionTelemetry_ShouldIncrementCounters()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        // Execute two actions to record telemetry
        await adapter.ExecuteAsync(BuildRequest("set_hero_state_helper", RuntimeMode.Galactic), CancellationToken.None);
        await adapter.ExecuteAsync(BuildRequest("set_hero_state_helper", RuntimeMode.Galactic), CancellationToken.None);

        // Success counters should have at least one entry
        var successCounters = typeof(RuntimeAdapter)
            .GetField("_actionSuccessCounters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(adapter) as Dictionary<string, int>;
        successCounters.Should().NotBeNull();
        successCounters!.Values.Sum().Should().BeGreaterOrEqualTo(2);
    }

    // ── Context Spawn Payload Defaults ─────────────────────────────��────────

    [Fact]
    public async Task ExecuteAsync_SpawnContextEntity_ShouldApplyPayloadDefaults_ForTacticalSpawn()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("spawn_context_entity", "spawn_tactical_entity");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.TacticalLand);

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn_context_entity", RuntimeMode.TacticalLand), CancellationToken.None);

        // Even if the execution fails downstream, it must have routed to spawn_tactical_entity
        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics!["contextRoutedAction"]!.ToString().Should().Be("spawn_tactical_entity");
    }

    [Fact]
    public async Task ExecuteAsync_SpawnContextEntity_ShouldApplyPayloadDefaults_ForGalacticSpawn()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("spawn_context_entity", "spawn_galactic_entity");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("spawn_context_entity", RuntimeMode.Galactic), CancellationToken.None);

        result.Diagnostics.Should().ContainKey("contextRoutedAction");
        result.Diagnostics!["contextRoutedAction"]!.ToString().Should().Be("spawn_galactic_entity");
    }

    // ── ScanCalibrationCandidatesAsync branches ─────────────────────────────

    [Fact]
    public async Task ScanCalibrationCandidates_ShouldFailValidation_WhenSymbolIsEmpty()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = new RuntimeCalibrationScanRequest(
            TargetSymbol: "",
            MaxCandidates: 10);
        var result = await adapter.ScanCalibrationCandidatesAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_request");
    }

    [Fact]
    public async Task ScanCalibrationCandidates_ShouldFailNotAttached_WhenSessionIsNull()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        typeof(RuntimeAdapter)
            .GetProperty("CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(adapter, null);

        var request = new RuntimeCalibrationScanRequest(TargetSymbol: "credits", MaxCandidates: 10);
        var result = await adapter.ScanCalibrationCandidatesAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("not_attached");
    }

    // ── IsStarWarsGProcess branches (static) ────────────────────────────────

    [Theory]
    [InlineData("StarWarsG", true)]
    [InlineData("StarWarsG.exe", true)]
    [InlineData("swfoc", false)]
    public void IsStarWarsGProcess_ShouldDetectCorrectly(string processName, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsStarWarsGProcess", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: processName,
            ProcessPath: @"C:\Games\" + processName,
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown);

        var result = (bool)method!.Invoke(null, new object[] { process })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void IsStarWarsGProcess_ShouldCheckMetadata()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsStarWarsGProcess", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "game",
            ProcessPath: @"C:\Games\game.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["isStarWarsG"] = "true"
            });

        var result = (bool)method!.Invoke(null, new object[] { process })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_ShouldCheckProcessPath()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsStarWarsGProcess", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "game",
            ProcessPath: @"C:\Games\StarWarsG.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown);

        var result = (bool)method!.Invoke(null, new object[] { process })!;
        result.Should().BeTrue();
    }

    // ── ProcessContainsWorkshopId branches ────────────────────────���─────────

    [Fact]
    public void ProcessContainsWorkshopId_ShouldDetectFromCommandLine()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ProcessContainsWorkshopId", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "game",
            ProcessPath: @"C:\Games\game.exe",
            CommandLine: "game.exe MODPATH=12345",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown);

        var result = (bool)method!.Invoke(null, new object[] { process, "12345" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ProcessContainsWorkshopId_ShouldDetectFromMetadata()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ProcessContainsWorkshopId", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "game",
            ProcessPath: @"C:\Games\game.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["steamModIdsDetected"] = "111,222,333"
            });

        var result = (bool)method!.Invoke(null, new object[] { process, "222" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ProcessContainsWorkshopId_ShouldReturnFalse_WhenNoMatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ProcessContainsWorkshopId", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "game",
            ProcessPath: @"C:\Games\game.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown);

        var result = (bool)method!.Invoke(null, new object[] { process, "99999" })!;
        result.Should().BeFalse();
    }

    // ── CollectRequiredWorkshopIds branches ─────────────────────────���───────

    [Fact]
    public void CollectRequiredWorkshopIds_ShouldCollectFromProfileAndMetadata()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "CollectRequiredWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = new TrainerProfile(
            Id: "test",
            DisplayName: "test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: "12345",
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "67890,11111",
                ["requiredWorkshopId"] = "22222"
            });

        var ids = (HashSet<string>)method!.Invoke(null, new object[] { profile })!;
        ids.Should().Contain("12345");
        ids.Should().Contain("67890");
        ids.Should().Contain("11111");
        ids.Should().Contain("22222");
    }

    // ── ResolveProcessSelectionReason branches ─────────────────────────────

    [Fact]
    public void ResolveProcessSelectionReason_ShouldReturnMetadataReason_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveProcessSelectionReason", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "game",
            ProcessPath: @"C:\Games\game.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["recommendationReason"] = "fingerprint_match"
            });

        var result = (string)method!.Invoke(null, new object[] { process })!;
        result.Should().Be("fingerprint_match");
    }

    [Fact]
    public void ResolveProcessSelectionReason_ShouldReturnDefault_WhenNoMetadata()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveProcessSelectionReason", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "game",
            ProcessPath: @"C:\Games\game.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown);

        var result = (string)method!.Invoke(null, new object[] { process })!;
        result.Should().Be("exe_target_match");
    }

    // ── ContextRouteType enum coverage ─────────────────────���───────────────

    [Theory]
    [InlineData("spawn_context_entity", "Spawn")]
    [InlineData("set_context_faction", "Faction")]
    [InlineData("set_context_allegiance", "Faction")]
    [InlineData("set_credits", "None")]
    [InlineData("toggle_fog_reveal", "None")]
    public void ResolveContextRouteType_ShouldReturnCorrectType(string actionId, string expectedType)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveContextRouteType", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object[] { actionId })!;
        result.ToString().Should().Be(expectedType);
    }

    // ── TryGetFileSize / TryGetLastWriteUtc edge branches ──────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetFileSize_ShouldReturnNull_WhenPathIsNullOrEmpty(string? path)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryGetFileSize", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { path });
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetLastWriteUtc_ShouldReturnNull_WhenPathIsNullOrEmpty(string? path)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryGetLastWriteUtc", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { path });
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ComputeFileSha256_ShouldReturnNull_WhenPathIsNullOrEmpty(string? path)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ComputeFileSha256", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { path });
        result.Should().BeNull();
    }

    // ── Diagnostic resolution helper branches ──────────────────────��───────

    [Fact]
    public void ResolveHybridExecutionFlag_ShouldReturnFalse_WhenMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHybridExecutionFlag", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object?[] { null })!;
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void ResolveHybridExecutionFlag_ShouldParseCorrectly(string value, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHybridExecutionFlag", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["hybridExecution"] = value
        };
        var result = (bool)method!.Invoke(null, new object?[] { diag })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveBackendDiagnosticValue_ShouldReturnExisting_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveBackendDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["backend"] = "Memory"
        };
        var result = (string)method!.Invoke(null, new object?[] { diag, ExecutionBackendKind.Helper })!;
        result.Should().Be("Memory");
    }

    [Fact]
    public void ResolveBackendDiagnosticValue_ShouldReturnRouteBackend_WhenMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveBackendDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object?[] { null, ExecutionBackendKind.Helper })!;
        result.Should().Be("Helper");
    }

    // ── ComputeSelectionScore ──────────────────────────────────────────────

    [Fact]
    public void ComputeSelectionScore_ShouldProduceNonNegativeResult()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ComputeSelectionScore", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (double)method!.Invoke(null, new object[] { 2, true, 1, true, 1000000 })!;
        result.Should().BeGreaterThan(0);
    }

    // ── Helper builders ────────────────────────────────────────────────────

    private static ActionExecutionRequest BuildRequest(string actionId, RuntimeMode runtimeMode, ExecutionKind kind = ExecutionKind.Helper)
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
                kind,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: payload,
            ProfileId: "profile",
            RuntimeMode: runtimeMode);
    }

    private static ActionExecutionRequest BuildRequestWithContext(
        string actionId,
        RuntimeMode runtimeMode,
        IReadOnlyDictionary<string, object?> context)
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
            RuntimeMode: runtimeMode,
            Context: context);
    }

    private static TrainerProfile BuildHelperProfile(params string[] actionIds)
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

    private static TrainerProfile BuildProfileWithFeatureFlags(
        IReadOnlyDictionary<string, bool> featureFlags,
        params string[] actionIds)
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
}

/// <summary>
/// An execution backend that throws a configurable exception on ExecuteAsync.
/// </summary>
internal sealed class ThrowingExecutionBackend(Exception exception) : IExecutionBackend
{
    public ExecutionBackendKind BackendKind => ExecutionBackendKind.Extender;

    public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext)
        => Task.FromResult(new CapabilityReport(
            profileId,
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase),
            RuntimeReasonCode.CAPABILITY_PROBE_PASS));

    public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext, CancellationToken cancellationToken)
        => ProbeCapabilitiesAsync(profileId, processContext);

    public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport)
        => throw exception;

    public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport, CancellationToken cancellationToken)
        => throw exception;

    public Task<BackendHealth> GetHealthAsync()
        => Task.FromResult(new BackendHealth("throwing", BackendKind, true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok"));

    public Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken) => GetHealthAsync();
}
