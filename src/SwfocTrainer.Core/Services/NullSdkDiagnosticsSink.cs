using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class NullSdkDiagnosticsSink : ISdkDiagnosticsSink
{
    public Task WriteAsync(SdkOperationRequest request, SdkOperationResult result)
    {
        return Task.CompletedTask;
    }

    public Task WriteAsync(SdkOperationRequest request, SdkOperationResult result, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
