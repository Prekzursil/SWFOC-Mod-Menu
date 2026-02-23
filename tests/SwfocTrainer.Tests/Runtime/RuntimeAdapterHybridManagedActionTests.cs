using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterHybridManagedActionTests
{
    [Theory]
    [InlineData("freeze_timer", ExecutionKind.Memory)]
    [InlineData("toggle_fog_reveal", ExecutionKind.Memory)]
    [InlineData("toggle_ai", ExecutionKind.Memory)]
    [InlineData("set_unit_cap", ExecutionKind.CodePatch)]
    [InlineData("toggle_instant_build_patch", ExecutionKind.CodePatch)]
    public void ResolveHybridManagedExecutionKind_ShouldMapPromotedActionsToManagedKinds(
        string actionId,
        ExecutionKind expectedExecutionKind)
    {
        var resolved = InvokeResolveHybridManagedExecutionKind(actionId, ExecutionKind.Sdk);
        resolved.Should().Be(expectedExecutionKind);
    }

    [Fact]
    public void ResolveHybridManagedExecutionKind_ShouldKeepOriginalKind_ForNonPromotedActions()
    {
        var resolved = InvokeResolveHybridManagedExecutionKind("set_credits", ExecutionKind.Sdk);
        resolved.Should().Be(ExecutionKind.Sdk);
    }

    [Fact]
    public void ShouldExecuteHybridManagedAction_ShouldRequireHybridRouteFlag()
    {
        var request = BuildRequest("freeze_timer", ExecutionKind.Sdk);
        var extenderWithoutHybrid = BuildRouteDecision(hybridExecution: false);
        var extenderWithHybrid = BuildRouteDecision(hybridExecution: true);

        InvokeShouldExecuteHybridManagedAction(request, extenderWithoutHybrid).Should().BeFalse();
        InvokeShouldExecuteHybridManagedAction(request, extenderWithHybrid).Should().BeTrue();
    }

    private static ActionExecutionRequest BuildRequest(string actionId, ExecutionKind executionKind)
    {
        var action = new ActionSpec(
            Id: actionId,
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Galactic,
            ExecutionKind: executionKind,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0,
            Description: "test");

        return new ActionExecutionRequest(
            Action: action,
            Payload: new JsonObject(),
            ProfileId: "roe_3447786229_swfoc",
            RuntimeMode: RuntimeMode.Galactic);
    }

    private static BackendRouteDecision BuildRouteDecision(bool hybridExecution)
    {
        return new BackendRouteDecision(
            Allowed: true,
            Backend: ExecutionBackendKind.Extender,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "ok",
            Diagnostics: new Dictionary<string, object?>
            {
                ["hybridExecution"] = hybridExecution
            });
    }

    private static ExecutionKind InvokeResolveHybridManagedExecutionKind(string actionId, ExecutionKind requestedKind)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHybridManagedExecutionKind",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("RuntimeAdapter should normalize promoted hybrid action ids to managed execution kinds.");
        var invoked = method!.Invoke(null, new object?[] { actionId, requestedKind });
        invoked.Should().BeOfType<ExecutionKind>();
        return (ExecutionKind)invoked!;
    }

    private static bool InvokeShouldExecuteHybridManagedAction(ActionExecutionRequest request, BackendRouteDecision decision)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ShouldExecuteHybridManagedAction",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("RuntimeAdapter should honor route diagnostics before dispatching hybrid managed actions.");
        var invoked = method!.Invoke(null, new object?[] { request, decision });
        invoked.Should().BeOfType<bool>();
        return (bool)invoked!;
    }
}
