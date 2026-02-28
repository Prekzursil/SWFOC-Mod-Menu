using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterModeOverrideTests
{
    [Fact]
    public void ApplyRuntimeModeOverride_ShouldKeepRequestedMode_WhenOverrideIsNotProvided()
    {
        var request = BuildRequest(
            RuntimeMode.Tactical,
            new Dictionary<string, object?>());

        var applied = InvokeApplyRuntimeModeOverride(
            request,
            new Dictionary<string, string>
            {
                ["runtimeModeHint"] = RuntimeMode.Galactic.ToString(),
                ["runtimeModeReasonCode"] = "mode_probe_galactic_signals"
            });

        applied.RuntimeMode.Should().Be(RuntimeMode.Tactical);
        applied.Context!["runtimeModeHint"].Should().Be("Galactic");
        applied.Context["runtimeModeEffective"].Should().Be("Tactical");
        applied.Context["runtimeModeReasonCode"].Should().Be("mode_probe_galactic_signals");
    }

    [Fact]
    public void ApplyRuntimeModeOverride_ShouldUseOverrideMode_ForGating()
    {
        var request = BuildRequest(
            RuntimeMode.Tactical,
            new Dictionary<string, object?>
            {
                ["runtimeModeOverride"] = RuntimeMode.Galactic.ToString()
            });

        var applied = InvokeApplyRuntimeModeOverride(
            request,
            new Dictionary<string, string>
            {
                ["runtimeModeHint"] = RuntimeMode.Tactical.ToString(),
                ["runtimeModeReasonCode"] = "mode_probe_tactical_signals"
            });

        applied.RuntimeMode.Should().Be(RuntimeMode.Galactic, "operator override should drive mode-gated routing checks.");
        applied.Context!["runtimeModeReasonCode"].Should().Be("mode_override_operator");
    }

    [Fact]
    public void ApplyRuntimeModeOverride_ShouldEmitRequestedAndEffectiveDiagnostics()
    {
        var request = BuildRequest(
            RuntimeMode.Unknown,
            new Dictionary<string, object?>
            {
                ["runtimeModeOverride"] = RuntimeMode.Tactical
            });

        var applied = InvokeApplyRuntimeModeOverride(request, sessionMetadata: null);

        applied.Context.Should().NotBeNull();
        applied.Context.Should().ContainKey("runtimeModeRequested");
        applied.Context.Should().ContainKey("runtimeModeHint");
        applied.Context.Should().ContainKey("runtimeModeEffective");
        applied.Context.Should().ContainKey("runtimeModeReasonCode");
        applied.Context!["runtimeModeRequested"].Should().Be("Unknown");
        applied.Context["runtimeModeEffective"].Should().Be("Tactical");
    }

    private static ActionExecutionRequest BuildRequest(RuntimeMode runtimeMode, IReadOnlyDictionary<string, object?>? context)
    {
        var action = new ActionSpec(
            Id: "set_planet_owner",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Galactic,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0);

        return new ActionExecutionRequest(
            Action: action,
            Payload: new JsonObject(),
            ProfileId: "test_profile",
            RuntimeMode: runtimeMode,
            Context: context);
    }

    private static ActionExecutionRequest InvokeApplyRuntimeModeOverride(
        ActionExecutionRequest request,
        IReadOnlyDictionary<string, string>? sessionMetadata)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyRuntimeModeOverride",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var applied = method!.Invoke(null, new object?[] { request, sessionMetadata });
        applied.Should().BeOfType<ActionExecutionRequest>();
        return (ActionExecutionRequest)applied!;
    }
}
