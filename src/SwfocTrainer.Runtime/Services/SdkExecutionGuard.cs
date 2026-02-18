using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class SdkExecutionGuard : ISdkExecutionGuard
{
    public SdkExecutionDecision CanExecute(CapabilityResolutionResult resolution, bool isMutation)
    {
        if (resolution.State == SdkCapabilityStatus.Available)
        {
            return new SdkExecutionDecision(true, resolution.ReasonCode, "Capability available.");
        }

        if (!isMutation && resolution.State == SdkCapabilityStatus.Degraded)
        {
            return new SdkExecutionDecision(true, resolution.ReasonCode, "Read-only operation allowed in degraded capability state.");
        }

        var reason = isMutation && resolution.State != SdkCapabilityStatus.Available
            ? CapabilityReasonCode.MutationBlockedByCapabilityState
            : resolution.ReasonCode;

        return new SdkExecutionDecision(false, reason, "Operation blocked by fail-closed SDK execution guard.");
    }
}
