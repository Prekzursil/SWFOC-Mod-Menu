using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterContextFactionRoutingTests
{
    [Theory]
    [InlineData(RuntimeMode.Galactic, "set_planet_owner")]
    [InlineData(RuntimeMode.AnyTactical, "set_selected_owner_faction")]
    [InlineData(RuntimeMode.TacticalLand, "set_selected_owner_faction")]
    [InlineData(RuntimeMode.TacticalSpace, "set_selected_owner_faction")]
    public void ResolveContextFactionTargetAction_ShouldRouteByRuntimeMode(RuntimeMode mode, string expectedActionId)
    {
        var resolved = InvokeResolveContextFactionTargetAction(mode);

        resolved.Should().Be(expectedActionId);
    }

    [Fact]
    public void ResolveContextFactionTargetAction_ShouldReturnNull_ForUnknownMode()
    {
        var resolved = InvokeResolveContextFactionTargetAction(RuntimeMode.Unknown);

        resolved.Should().BeNull();
    }

    [Theory]
    [InlineData(RuntimeMode.Galactic, "spawn_galactic_entity")]
    [InlineData(RuntimeMode.AnyTactical, "spawn_tactical_entity")]
    [InlineData(RuntimeMode.TacticalLand, "spawn_tactical_entity")]
    [InlineData(RuntimeMode.TacticalSpace, "spawn_tactical_entity")]
    public void ResolveContextSpawnTargetAction_ShouldRouteByRuntimeMode(RuntimeMode mode, string expectedActionId)
    {
        var resolved = InvokeResolveContextSpawnTargetAction(mode);

        resolved.Should().Be(expectedActionId);
    }

    [Fact]
    public void ResolveContextSpawnTargetAction_ShouldReturnNull_ForUnknownMode()
    {
        var resolved = InvokeResolveContextSpawnTargetAction(RuntimeMode.Unknown);

        resolved.Should().BeNull();
    }

    [Fact]
    public void ApplyContextActionDiagnostics_ShouldAnnotateDiagnostics_WhenContextActionRequested()
    {
        var baseResult = new ActionExecutionResult(
            Succeeded: true,
            Message: "ok",
            AddressSource: AddressSource.Signature,
            Diagnostics: new Dictionary<string, object?>());

        var annotated = InvokeApplyContextActionDiagnostics(baseResult, "set_context_faction", "set_planet_owner");

        annotated.Diagnostics.Should().ContainKey("contextActionId");
        annotated.Diagnostics!["contextActionId"].Should().Be("set_context_faction");
        annotated.Diagnostics.Should().ContainKey("contextRoutedAction");
        annotated.Diagnostics!["contextRoutedAction"].Should().Be("set_planet_owner");
    }

    [Fact]
    public void ApplyContextActionDiagnostics_ShouldLeaveResultUntouched_WhenDifferentActionRequested()
    {
        var baseResult = new ActionExecutionResult(
            Succeeded: true,
            Message: "ok",
            AddressSource: AddressSource.Signature,
            Diagnostics: new Dictionary<string, object?>
            {
                ["existing"] = "value"
            });

        var annotated = InvokeApplyContextActionDiagnostics(baseResult, "set_planet_owner", "set_planet_owner");

        annotated.Diagnostics.Should().ContainKey("existing");
        annotated.Diagnostics.Should().NotContainKey("contextActionId");
    }

    [Fact]
    public void ApplyContextActionDiagnostics_ShouldAnnotateDiagnostics_ForContextAllegianceAlias()
    {
        var baseResult = new ActionExecutionResult(
            Succeeded: true,
            Message: "ok",
            AddressSource: AddressSource.Signature,
            Diagnostics: new Dictionary<string, object?>());

        var annotated = InvokeApplyContextActionDiagnostics(baseResult, "set_context_allegiance", "set_planet_owner");

        annotated.Diagnostics.Should().ContainKey("contextActionId");
        annotated.Diagnostics!["contextActionId"].Should().Be("set_context_allegiance");
        annotated.Diagnostics.Should().ContainKey("contextRoutedAction");
        annotated.Diagnostics!["contextRoutedAction"].Should().Be("set_planet_owner");
    }

    private static string? InvokeResolveContextFactionTargetAction(RuntimeMode mode)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveContextFactionTargetAction",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (string?)method!.Invoke(null, new object[] { mode });
    }

    private static string? InvokeResolveContextSpawnTargetAction(RuntimeMode mode)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveContextSpawnTargetAction",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (string?)method!.Invoke(null, new object[] { mode });
    }

    private static ActionExecutionResult InvokeApplyContextActionDiagnostics(
        ActionExecutionResult result,
        string requestedActionId,
        string? routedActionId)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyContextActionDiagnostics",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var invoked = method!.Invoke(null, new object?[] { result, requestedActionId, routedActionId });
        invoked.Should().BeOfType<ActionExecutionResult>();
        return (ActionExecutionResult)invoked!;
    }
}
