using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// SDK execution adapter for research-track in-process operation handling.
/// </summary>
public interface ISdkRuntimeAdapter
{
    Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request);

    Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken);
}
