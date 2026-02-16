using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ISdkRuntimeAdapter
{
    Task<SdkCommandResult> ExecuteAsync(
        SdkCommandRequest request,
        AttachSession session,
        SdkOperationCapability capability,
        CancellationToken cancellationToken = default);
}
