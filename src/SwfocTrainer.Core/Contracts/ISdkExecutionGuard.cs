using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Enforces fail-closed execution policy for SDK operations.
/// </summary>
public interface ISdkExecutionGuard
{
    SdkExecutionDecision CanExecute(CapabilityResolutionResult resolution, bool isMutation);
}
