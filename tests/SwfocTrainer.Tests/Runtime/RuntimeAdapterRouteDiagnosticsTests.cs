using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterRouteDiagnosticsTests
{
    [Fact]
    public void ApplyBackendRouteDiagnostics_ShouldEmitBackendRouteAndHybridKeys()
    {
        var result = new ActionExecutionResult(
            Succeeded: true,
            Message: "ok",
            AddressSource: AddressSource.Signature,
            Diagnostics: new Dictionary<string, object?>
            {
                ["state"] = "installed"
            });

        var routeDecision = new BackendRouteDecision(
            Allowed: true,
            Backend: ExecutionBackendKind.Extender,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "routed",
            Diagnostics: new Dictionary<string, object?>
            {
                ["hybridExecution"] = true,
                ["capabilityMapReasonCode"] = "CAPABILITY_PROBE_PASS",
                ["capabilityMapState"] = "Verified",
                ["capabilityDeclaredAvailable"] = true
            });

        var capabilityReport = new CapabilityReport(
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

        var applied = InvokeApplyBackendRouteDiagnostics(result, routeDecision, capabilityReport);

        applied.Diagnostics.Should().NotBeNull();
        applied.Diagnostics.Should().ContainKey("backend");
        applied.Diagnostics.Should().ContainKey("routeReasonCode");
        applied.Diagnostics.Should().ContainKey("capabilityProbeReasonCode");
        applied.Diagnostics.Should().ContainKey("hookState");
        applied.Diagnostics.Should().ContainKey("hybridExecution");
        applied.Diagnostics.Should().ContainKey("capabilityMapReasonCode");
        applied.Diagnostics.Should().ContainKey("capabilityMapState");
        applied.Diagnostics.Should().ContainKey("capabilityDeclaredAvailable");
        applied.Diagnostics.Should().ContainKey("expertOverrideEnabled");
        applied.Diagnostics.Should().ContainKey("overrideReason");
        applied.Diagnostics.Should().ContainKey("panicDisableState");
    }

    [Fact]
    public void ApplyBackendRouteDiagnostics_ShouldReadExpertOverrideEnvFlags_WhenDiagnosticsDoNotProvideValues()
    {
        const string overrideEnv = "SWFOC_EXPERT_MUTATION_OVERRIDES";
        const string panicEnv = "SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC";
        var previousOverride = Environment.GetEnvironmentVariable(overrideEnv);
        var previousPanic = Environment.GetEnvironmentVariable(panicEnv);

        try
        {
            Environment.SetEnvironmentVariable(overrideEnv, "true");
            Environment.SetEnvironmentVariable(panicEnv, "1");

            var result = new ActionExecutionResult(
                Succeeded: false,
                Message: "blocked",
                AddressSource: AddressSource.None,
                Diagnostics: new Dictionary<string, object?>());

            var routeDecision = new BackendRouteDecision(
                Allowed: false,
                Backend: ExecutionBackendKind.Extender,
                ReasonCode: RuntimeReasonCode.SAFETY_FAIL_CLOSED,
                Message: "blocked",
                Diagnostics: new Dictionary<string, object?>());

            var capabilityReport = CapabilityReport.Unknown("roe_3447786229_swfoc");
            var applied = InvokeApplyBackendRouteDiagnostics(result, routeDecision, capabilityReport);

            applied.Diagnostics.Should().NotBeNull();
            applied.Diagnostics!["expertOverrideEnabled"].Should().Be(true);
            applied.Diagnostics["panicDisableState"].Should().Be("active");
            applied.Diagnostics["overrideReason"].Should().Be("none");
        }
        finally
        {
            Environment.SetEnvironmentVariable(overrideEnv, previousOverride);
            Environment.SetEnvironmentVariable(panicEnv, previousPanic);
        }
    }

    private static ActionExecutionResult InvokeApplyBackendRouteDiagnostics(
        ActionExecutionResult result,
        BackendRouteDecision routeDecision,
        CapabilityReport capabilityReport)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyBackendRouteDiagnostics",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("RuntimeAdapter should normalize backend routing diagnostics.");
        var invoked = method!.Invoke(null, new object?[] { result, routeDecision, capabilityReport });
        invoked.Should().BeOfType<ActionExecutionResult>();
        return (ActionExecutionResult)invoked!;
    }
}
