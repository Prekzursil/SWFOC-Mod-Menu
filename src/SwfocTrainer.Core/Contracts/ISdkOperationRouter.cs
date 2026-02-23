using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Routes SDK operation requests through capability resolution and execution guards.
/// </summary>
public interface ISdkOperationRouter
{
    Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request);

    Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken);
}
