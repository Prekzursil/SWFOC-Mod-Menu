using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IExecutionBackend
{
    ExecutionBackendKind BackendKind { get; }

    Task<CapabilityReport> ProbeCapabilitiesAsync(
        string profileId,
        ProcessMetadata processContext);

    Task<CapabilityReport> ProbeCapabilitiesAsync(
        string profileId,
        ProcessMetadata processContext,
        CancellationToken cancellationToken);

    Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionRequest command,
        CapabilityReport capabilityReport);

    Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionRequest command,
        CapabilityReport capabilityReport,
        CancellationToken cancellationToken);

    Task<BackendHealth> GetHealthAsync();

    Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken);
}
