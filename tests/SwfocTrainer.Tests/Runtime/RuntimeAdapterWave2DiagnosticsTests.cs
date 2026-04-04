using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 2 diagnostic resolution and context building tests for RuntimeAdapter —
/// targets ResolveHookStateDiagnosticValue, ResolveExpertOverrideEnabledDiagnosticValue,
/// ResolveOverrideReasonDiagnosticValue, ResolvePanicDisableStateDiagnosticValue,
/// TryResolveFirstDiagnosticValue, helper action dispatching, SDK context building,
/// save action routing, and process-level resolution methods.
/// </summary>
public sealed class RuntimeAdapterWave2DiagnosticsTests
{
    // ── ResolveHookStateDiagnosticValue branches ──────────────────────────

    [Fact]
    public void ResolveHookStateDiagnosticValue_ShouldReturnUnknown_WhenBothNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHookStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object?[] { null, null })!;
        result.Should().Be("unknown");
    }

    [Fact]
    public void ResolveHookStateDiagnosticValue_ShouldReturnResultHookState_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHookStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var resultDiag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["hookState"] = "installed"
        } as IReadOnlyDictionary<string, object?>;
        var result = (string)method!.Invoke(null, new object?[] { resultDiag, null })!;
        result.Should().Be("installed");
    }

    [Fact]
    public void ResolveHookStateDiagnosticValue_ShouldReturnCreditsStateTag_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHookStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var resultDiag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["creditsStateTag"] = "credits_hook_active"
        } as IReadOnlyDictionary<string, object?>;
        var result = (string)method!.Invoke(null, new object?[] { resultDiag, null })!;
        result.Should().Be("credits_hook_active");
    }

    [Fact]
    public void ResolveHookStateDiagnosticValue_ShouldReturnState_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHookStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var resultDiag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["state"] = "ready"
        } as IReadOnlyDictionary<string, object?>;
        var result = (string)method!.Invoke(null, new object?[] { resultDiag, null })!;
        result.Should().Be("ready");
    }

    [Fact]
    public void ResolveHookStateDiagnosticValue_ShouldFallToCapabilityDiagnostics()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHookStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var capDiag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["hookState"] = "probed"
        } as IReadOnlyDictionary<string, object?>;
        var result = (string)method!.Invoke(null, new object?[] { null, capDiag })!;
        result.Should().Be("probed");
    }

    // ── ResolveExpertOverrideEnabledDiagnosticValue branches ────────────

    [Fact]
    public void ResolveExpertOverrideEnabledDiagnostic_ShouldReturnDefault_WhenNoDiag()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveExpertOverrideEnabledDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object?[] { null, true })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ResolveExpertOverrideEnabledDiagnostic_ShouldReturnParsed_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveExpertOverrideEnabledDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["expertOverrideEnabled"] = "false"
        } as IReadOnlyDictionary<string, object?>;
        var result = (bool)method!.Invoke(null, new object?[] { diag, true })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void ResolveExpertOverrideEnabledDiagnostic_ShouldReturnDefault_WhenUnparseable()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveExpertOverrideEnabledDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["expertOverrideEnabled"] = "not_a_bool"
        } as IReadOnlyDictionary<string, object?>;
        var result = (bool)method!.Invoke(null, new object?[] { diag, true })!;
        result.Should().BeTrue(); // falls back to default
    }

    // ── ResolvePanicDisableStateDiagnosticValue branches ────────────────

    [Fact]
    public void ResolvePanicDisableStateDiagnostic_ShouldReturnDefault_WhenNoDiag()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolvePanicDisableStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object?[] { null, "inactive" })!;
        result.Should().Be("inactive");
    }

    [Fact]
    public void ResolvePanicDisableStateDiagnostic_ShouldReturnExisting_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolvePanicDisableStateDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["panicDisableState"] = "active"
        } as IReadOnlyDictionary<string, object?>;
        var result = (string)method!.Invoke(null, new object?[] { diag, "inactive" })!;
        result.Should().Be("active");
    }

    // ── ResolveOverrideReasonDiagnosticValue branches ────────────────────

    [Fact]
    public void ResolveOverrideReasonDiagnostic_ShouldReturnDefault_WhenNoDiag()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveOverrideReasonDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object?[] { null, "default_reason" })!;
        result.Should().Be("default_reason");
    }

    [Fact]
    public void ResolveOverrideReasonDiagnostic_ShouldReturnExisting_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveOverrideReasonDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["overrideReason"] = "custom reason"
        } as IReadOnlyDictionary<string, object?>;
        var result = (string)method!.Invoke(null, new object?[] { diag, "default_reason" })!;
        result.Should().Be("custom reason");
    }

    // ── TryResolveFirstDiagnosticValue branches ─────────────────────────

    [Fact]
    public void TryResolveFirstDiagnosticValue_ShouldReturnFalse_WhenNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryResolveFirstDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var keys = new[] { "key1", "key2" };
        var args = new object?[] { null, keys, null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolveFirstDiagnosticValue_ShouldReturnFirst_WhenMultipleKeysPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryResolveFirstDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["key2"] = "val2",
            ["key3"] = "val3"
        } as IReadOnlyDictionary<string, object?>;
        var keys = new[] { "key1", "key2", "key3" };
        var args = new object?[] { diag, keys, null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        ((string?)args[2]).Should().Be("val2");
    }

    [Fact]
    public void TryResolveFirstDiagnosticValue_ShouldReturnFalse_WhenNoKeysMatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryResolveFirstDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["other"] = "val"
        } as IReadOnlyDictionary<string, object?>;
        var keys = new[] { "key1", "key2" };
        var args = new object?[] { diag, keys, null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    // ── TryResolveTelemetryModeFromContext branches ─────────────────────

    [Theory]
    [InlineData("Galactic", true, RuntimeMode.Galactic)]
    [InlineData("TacticalLand", true, RuntimeMode.TacticalLand)]
    [InlineData("Land", true, RuntimeMode.TacticalLand)]
    [InlineData("TacticalSpace", true, RuntimeMode.TacticalSpace)]
    [InlineData("Space", true, RuntimeMode.TacticalSpace)]
    [InlineData("AnyTactical", true, RuntimeMode.AnyTactical)]
    [InlineData("", false, RuntimeMode.Unknown)]
    [InlineData("Invalid", false, RuntimeMode.Unknown)]
    public void TryResolveTelemetryModeFromContext_ShouldHandleAllValues(string value, bool expectedSuccess, RuntimeMode expectedMode)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryResolveTelemetryModeFromContext", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["telemetryRuntimeMode"] = value
        } as IReadOnlyDictionary<string, object?>;
        var args = new object?[] { context, RuntimeMode.Unknown };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().Be(expectedSuccess);
        if (expectedSuccess)
        {
            ((RuntimeMode)args[1]!).Should().Be(expectedMode);
        }
    }

    [Fact]
    public void TryResolveTelemetryModeFromContext_ShouldReturnFalse_WhenContextNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryResolveTelemetryModeFromContext", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var args = new object?[] { null, RuntimeMode.Unknown };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolveTelemetryModeFromContext_ShouldReturnFalse_WhenKeyMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryResolveTelemetryModeFromContext", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, object?>;
        var args = new object?[] { context, RuntimeMode.Unknown };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolveTelemetryModeFromContext_ShouldReturnFalse_WhenValueIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryResolveTelemetryModeFromContext", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["telemetryRuntimeMode"] = null
        } as IReadOnlyDictionary<string, object?>;
        var args = new object?[] { context, RuntimeMode.Unknown };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    // ── ResolveManualOverrideMode branches ──────────────────────────────

    [Theory]
    [InlineData("Galactic", RuntimeMode.Galactic)]
    [InlineData("AnyTactical", RuntimeMode.AnyTactical)]
    [InlineData("TacticalLand", RuntimeMode.TacticalLand)]
    [InlineData("TacticalSpace", RuntimeMode.TacticalSpace)]
    public void ResolveManualOverrideMode_ShouldReturn_ForValidValues(string value, RuntimeMode expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveManualOverrideMode", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeModeOverride"] = value
        } as IReadOnlyDictionary<string, object?>;
        var result = (RuntimeMode?)method!.Invoke(null, new object?[] { context });
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("InvalidValue")]
    public void ResolveManualOverrideMode_ShouldReturnNull_ForAutoOrInvalid(string value)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveManualOverrideMode", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeModeOverride"] = value
        } as IReadOnlyDictionary<string, object?>;
        var result = (RuntimeMode?)method!.Invoke(null, new object?[] { context });
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveManualOverrideMode_ShouldReturnNull_WhenContextNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveManualOverrideMode", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (RuntimeMode?)method!.Invoke(null, new object?[] { null });
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveManualOverrideMode_ShouldReturnNull_WhenKeyMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveManualOverrideMode", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, object?>;
        var result = (RuntimeMode?)method!.Invoke(null, new object?[] { context });
        result.Should().BeNull();
    }

    // ── ApplyContextActionDiagnostics branches ─────────────────────────

    [Fact]
    public void ApplyContextActionDiagnostics_ShouldNotMerge_ForNonContextAction()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyContextActionDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        var modified = (ActionExecutionResult)method!.Invoke(null, new object?[] { result, "other_action", "routed" })!;
        modified.Diagnostics.Should().BeNull();
    }

    [Theory]
    [InlineData("set_context_faction")]
    [InlineData("set_context_allegiance")]
    [InlineData("spawn_context_entity")]
    public void ApplyContextActionDiagnostics_ShouldMerge_ForContextActions(string actionId)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyContextActionDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        var modified = (ActionExecutionResult)method!.Invoke(null, new object?[] { result, actionId, "routed_action" })!;
        modified.Diagnostics.Should().ContainKey("contextActionId");
        modified.Diagnostics!["contextActionId"]!.ToString().Should().Be(actionId);
        modified.Diagnostics["contextRoutedAction"]!.ToString().Should().Be("routed_action");
    }

    [Fact]
    public void ApplyContextActionDiagnostics_ShouldHandleNullRoutedAction()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyContextActionDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        var modified = (ActionExecutionResult)method!.Invoke(null, new object?[] { result, "set_context_faction", null })!;
        modified.Diagnostics.Should().ContainKey("contextRoutedAction");
        modified.Diagnostics!["contextRoutedAction"]!.ToString().Should().BeEmpty();
    }

    // ── CreateContextMissingActionResult branches ───────────────────────

    [Fact]
    public void CreateContextMissingActionResult_ShouldReturnCorrectResult()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "CreateContextMissingActionResult", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ActionExecutionResult)method!.Invoke(null, new object[] { "my_profile", "target_action" })!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("my_profile");
        result.Message.Should().Contain("target_action");
        result.Diagnostics.Should().ContainKey("routedActionId");
    }

    // ── ExecuteSaveAction routing via ExecuteByRoute ────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldReturnSaveMessage_WhenRouteIsSave()
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

    // ── Helper action with unavailable probe ───────────────────────────

    [Fact]
    public async Task ExecuteHelperAction_ShouldReturnUnavailable_WhenProbeIsUnavailable()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Helper,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok")),
            HelperBridgeBackend = new StubHelperBridgeBackend
            {
                ProbeResult = new HelperBridgeProbeResult(
                    Available: false,
                    ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                    Message: "bridge not ready",
                    Diagnostics: new Dictionary<string, object?> { ["state"] = "unresponsive" })
            }
        };
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("bridge not ready");
    }

    // ── Helper action with failed bridge result ────────────────────────

    [Fact]
    public async Task ExecuteHelperAction_ShouldReturnFailure_WhenBridgeResultFails()
    {
        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Helper,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok")),
            HelperBridgeBackend = new StubHelperBridgeBackend
            {
                ExecuteResult = new HelperBridgeExecutionResult(
                    Succeeded: false,
                    ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                    Message: "rejected by mod",
                    Diagnostics: new Dictionary<string, object?> { ["rejectReason"] = "version_mismatch" })
            }
        };
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ExecuteAsync(
            BuildRequest("set_hero_state_helper", RuntimeMode.Galactic),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("rejected by mod");
    }

    // ── BuildSdkContext branches ────────────────────────────────────────

    [Fact]
    public void BuildSdkContext_ShouldMergeExistingContext()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("test");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod(
            "BuildSdkContext", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["customKey"] = "customValue"
        } as IReadOnlyDictionary<string, object?>;
        var result = (IReadOnlyDictionary<string, object?>)method!.Invoke(adapter, new object?[] { context })!;
        result.Should().ContainKey("customKey");
        result.Should().ContainKey("processId");
        result.Should().ContainKey("processPath");
    }

    [Fact]
    public void BuildSdkContext_ShouldWorkWithNullContext()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("test");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod(
            "BuildSdkContext", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (IReadOnlyDictionary<string, object?>)method!.Invoke(adapter, new object?[] { null })!;
        result.Should().ContainKey("processId");
    }

    // ── NoopSdkRuntimeAdapter coverage ─────────────────────────────────

    [Fact]
    public async Task NoopSdkRuntimeAdapter_ShouldReturnNotImplemented()
    {
        var adapter = new NoopSdkRuntimeAdapter();
        var request = new SdkOperationRequest(
            "test_op",
            new JsonObject(),
            false,
            RuntimeMode.Galactic,
            "profile");

        var result = await adapter.ExecuteAsync(request);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("not implemented");
        result.CapabilityState.Should().Be(SdkCapabilityStatus.Unavailable);
    }

    [Fact]
    public async Task NoopSdkRuntimeAdapter_WithCancellation_ShouldReturnNotImplemented()
    {
        var adapter = new NoopSdkRuntimeAdapter();
        var request = new SdkOperationRequest(
            "test_op",
            new JsonObject(),
            false,
            RuntimeMode.Galactic,
            "profile");

        var result = await adapter.ExecuteAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("operationId");
        result.Diagnostics!["operationId"]!.ToString().Should().Be("test_op");
    }

    [Fact]
    public void NoopSdkRuntimeAdapter_ShouldThrow_WhenRequestIsNull()
    {
        var adapter = new NoopSdkRuntimeAdapter();
        var act = () => adapter.ExecuteAsync(null!);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void NoopSdkRuntimeAdapter_WithCancellation_ShouldThrow_WhenRequestIsNull()
    {
        var adapter = new NoopSdkRuntimeAdapter();
        var act = () => adapter.ExecuteAsync(null!, CancellationToken.None);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── FormatValidationRuleRange branches ──────────────────────────────

    [Fact]
    public void FormatValidationRuleRange_ShouldFormatMinAndMax()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "FormatValidationRuleRange", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 10L, 100L, null, null, false);
        var result = (string)method!.Invoke(null, new object[] { rule })!;
        result.Should().Contain("10");
        result.Should().Contain("100");
    }

    [Fact]
    public void FormatValidationRuleRange_ShouldHandleNullBounds()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "FormatValidationRuleRange", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, null, null, null, null, false);
        var result = (string)method!.Invoke(null, new object[] { rule })!;
        result.Should().NotBeEmpty();
    }

    // ── ValidateObservedIntValue branches ──────────────────────────────

    [Fact]
    public void ValidateObservedIntValue_ShouldPass_WhenRuleIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", 50L, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateObservedIntValue_ShouldFail_WhenBelowMin()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 10L, 100L, null, null, false);
        var result = method!.Invoke(null, new object?[] { "test", 5L, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateObservedIntValue_ShouldFail_WhenAboveMax()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 10L, 100L, null, null, false);
        var result = method!.Invoke(null, new object?[] { "test", 200L, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    // ── ValidateObservedFloatValue branches ────────────────────────────

    [Fact]
    public void ValidateObservedFloatValue_ShouldPass_WhenRuleIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", 3.14d, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ValidateObservedFloatValue_ShouldFail_WhenNonFinite(double value)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", value, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    // ── ProcessContainsWorkshopId branches ──────────────────────────────

    [Fact]
    public void ProcessContainsWorkshopId_ShouldReturnTrue_WhenCommandLineContainsId()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ProcessContainsWorkshopId", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = RuntimeAdapterExecuteCoverageTests.BuildSession(RuntimeMode.Galactic).Process with
        {
            CommandLine = "game.exe STEAMMOD=12345"
        };
        var result = (bool)method!.Invoke(null, new object[] { process, "12345" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ProcessContainsWorkshopId_ShouldReturnFalse_WhenNotContained()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ProcessContainsWorkshopId", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = RuntimeAdapterExecuteCoverageTests.BuildSession(RuntimeMode.Galactic).Process with
        {
            CommandLine = "game.exe STEAMMOD=99999"
        };
        var result = (bool)method!.Invoke(null, new object[] { process, "12345" })!;
        result.Should().BeFalse();
    }

    // ── CreateFallbackDisabledResult branches ──────────────────────────

    [Fact]
    public void CreateFallbackDisabledResult_ShouldReturnFailed()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "CreateFallbackDisabledResult", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ActionExecutionResult)method!.Invoke(null, new object[] { "my_action", "feature_key" })!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("my_action");
        result.Message.Should().Contain("feature_key");
    }

    // ── Helper builders ────────────────────────────────────────────────

    private static ActionExecutionRequest BuildRequest(string actionId, RuntimeMode runtimeMode, ExecutionKind kind = ExecutionKind.Helper)
    {
        var payload = new JsonObject { ["helperHookId"] = "hero_hook" };
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

    private static TrainerProfile BuildHelperProfile(params string[] actionIds)
    {
        var actions = actionIds.ToDictionary(
            id => id,
            id => new ActionSpec(
                id, ActionCategory.Hero, RuntimeMode.Unknown, ExecutionKind.Helper,
                new JsonObject(), VerifyReadback: false, CooldownMs: 0),
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
                    Signatures: [new SignatureSpec("credits", "AA BB", 0)])
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
}
