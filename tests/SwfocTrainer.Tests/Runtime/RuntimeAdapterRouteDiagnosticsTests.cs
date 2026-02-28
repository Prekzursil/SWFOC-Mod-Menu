using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterRouteDiagnosticsTests
{
    [Fact]
    public void ApplyBackendRouteDiagnostics_ShouldEmitBackendRouteAndOverrideDiagnosticsKeys()
    {
        var applied = InvokeApplyBackendRouteDiagnostics();

        applied.Diagnostics.Should().NotBeNull();
        applied.Diagnostics.Should().ContainKey("backend");
        applied.Diagnostics.Should().ContainKey("routeReasonCode");
        applied.Diagnostics.Should().ContainKey("capabilityProbeReasonCode");
        applied.Diagnostics.Should().ContainKey("hookState");
        applied.Diagnostics.Should().ContainKey("hybridExecution");
        applied.Diagnostics.Should().ContainKey("expertOverrideEnabled");
        applied.Diagnostics.Should().ContainKey("overrideReason");
        applied.Diagnostics.Should().ContainKey("panicDisableState");
    }

    [Fact]
    public void ApplyBackendRouteDiagnostics_ShouldEnableExpertOverride_WhenOverrideEnvVarTrue()
    {
        var applied = WithExpertOverrideEnv("1", null, () => InvokeApplyBackendRouteDiagnostics());

        applied.Diagnostics.Should().NotBeNull();
        applied.Diagnostics!["expertOverrideEnabled"].Should().Be(true);
        applied.Diagnostics!["panicDisableState"]!.ToString().Should().Be("inactive");
        applied.Diagnostics!["overrideReason"]!.ToString().Should().Contain("enabled");
    }

    [Fact]
    public void ApplyBackendRouteDiagnostics_ShouldPanicDisableOverride_WhenPanicEnvVarTrue()
    {
        var applied = WithExpertOverrideEnv("true", "1", () => InvokeApplyBackendRouteDiagnostics());

        applied.Diagnostics.Should().NotBeNull();
        applied.Diagnostics!["expertOverrideEnabled"].Should().Be(false);
        applied.Diagnostics!["panicDisableState"]!.ToString().Should().Be("active");
        applied.Diagnostics!["overrideReason"]!.ToString().Should().Contain("panic");
    }

    private static ActionExecutionResult InvokeApplyBackendRouteDiagnostics(
        ActionExecutionResult? result = null,
        BackendRouteDecision? routeDecision = null,
        CapabilityReport? capabilityReport = null)
    {
        var resolvedResult = result ?? new ActionExecutionResult(
            Succeeded: true,
            Message: "ok",
            AddressSource: AddressSource.Signature,
            Diagnostics: new Dictionary<string, object?>
            {
                ["state"] = "installed"
            });
        var resolvedRouteDecision = routeDecision ?? new BackendRouteDecision(
            Allowed: true,
            Backend: ExecutionBackendKind.Extender,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "routed",
            Diagnostics: new Dictionary<string, object?>
            {
                ["hybridExecution"] = true
            });
        var resolvedCapabilityReport = capabilityReport ?? new CapabilityReport(
            ProfileId: "roe_3447786229_swfoc",
            ProbedAtUtc: DateTimeOffset.UtcNow,
            Capabilities: new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_unit_cap"] = new BackendCapability(
                    "set_unit_cap",
                    Available: true,
                    CapabilityConfidenceState.Verified,
                    RuntimeReasonCode.CAPABILITY_PROBE_PASS)
            },
            ProbeReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Diagnostics: new Dictionary<string, object?>
            {
                ["hookState"] = "HOOK_READY"
            });

        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyBackendRouteDiagnostics",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("RuntimeAdapter should normalize backend routing diagnostics.");
        var invoked = method!.Invoke(null, new object?[] { resolvedResult, resolvedRouteDecision, resolvedCapabilityReport });
        invoked.Should().BeOfType<ActionExecutionResult>();
        return (ActionExecutionResult)invoked!;
    }

    private static ActionExecutionResult WithExpertOverrideEnv(
        string? overrideValue,
        string? panicValue,
        Func<ActionExecutionResult> action)
    {
        var priorOverride = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var priorPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", overrideValue);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", panicValue);
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", priorOverride);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", priorPanic);
        }
    }
}
