using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class NoopSdkRuntimeAdapter : ISdkRuntimeAdapter
{
    public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request)
    {
        return ExecuteAsync(request, CancellationToken.None);
    }

    public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken)
    {
        var result = new SdkOperationResult(
            false,
            "SDK runtime adapter is not implemented for this build.",
            CapabilityReasonCode.OperationNotMapped,
            SdkCapabilityStatus.Unavailable,
            new Dictionary<string, object?>
            {
                ["operationId"] = request.OperationId,
                ["profileId"] = request.ProfileId
            });
        return Task.FromResult(result);
    }
}
