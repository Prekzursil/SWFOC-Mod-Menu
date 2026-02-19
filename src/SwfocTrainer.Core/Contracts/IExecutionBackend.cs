using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IExecutionBackend
{
    ExecutionBackendKind BackendKind { get; }

    Task<CapabilityReport> ProbeCapabilitiesAsync(
        string profileId,
        ProcessMetadata processContext,
        CancellationToken cancellationToken = default);

    Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionRequest command,
        CapabilityReport capabilityReport,
        CancellationToken cancellationToken = default);

    Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}
