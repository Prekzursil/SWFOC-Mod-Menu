using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class SdkExecutionGuardTests
{
    [Fact]
    public void CanExecute_ShouldAllowRead_WhenCapabilityDegradedAndReadOperation()
    {
        var guard = new SdkExecutionGuard();
        var resolution = new CapabilityResolutionResult(
            ProfileId: "base_swfoc",
            OperationId: "list_selected",
            State: SdkCapabilityStatus.Degraded,
            ReasonCode: CapabilityReasonCode.OptionalAnchorsMissing,
            Confidence: 0.75d,
            FingerprintId: "fp-a",
            MatchedAnchors: new[] { "a" },
            MissingAnchors: new[] { "b" });

        var decision = guard.CanExecute(resolution, isMutation: false);

        decision.Allowed.Should().BeTrue();
        decision.ReasonCode.Should().Be(CapabilityReasonCode.OptionalAnchorsMissing);
    }

    [Fact]
    public void CanExecute_ShouldBlockWrite_WhenCapabilityNotAvailable()
    {
        var guard = new SdkExecutionGuard();
        var resolution = new CapabilityResolutionResult(
            ProfileId: "base_swfoc",
            OperationId: "set_hp",
            State: SdkCapabilityStatus.Degraded,
            ReasonCode: CapabilityReasonCode.RequiredAnchorsMissing,
            Confidence: 0.40d,
            FingerprintId: "fp-a",
            MatchedAnchors: Array.Empty<string>(),
            MissingAnchors: new[] { "selected_hp_write" });

        var decision = guard.CanExecute(resolution, isMutation: true);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be(CapabilityReasonCode.MutationBlockedByCapabilityState);
    }
}
