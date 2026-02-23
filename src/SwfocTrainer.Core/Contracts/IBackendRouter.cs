using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IBackendRouter
{
    BackendRouteDecision Resolve(
        ActionExecutionRequest request,
        TrainerProfile profile,
        ProcessMetadata process,
        CapabilityReport capabilityReport);
}
