using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IHelperBridgeBackend
{
    Task<HelperBridgeProbeResult> ProbeAsync(HelperBridgeProbeRequest request, CancellationToken cancellationToken);

    Task<HelperBridgeExecutionResult> ExecuteAsync(HelperBridgeRequest request, CancellationToken cancellationToken);

    Task<HelperBridgeProbeResult> ProbeAsync(HelperBridgeProbeRequest request)
    {
        return ProbeAsync(request, CancellationToken.None);
    }

    Task<HelperBridgeExecutionResult> ExecuteAsync(HelperBridgeRequest request)
    {
        return ExecuteAsync(request, CancellationToken.None);
    }
}
