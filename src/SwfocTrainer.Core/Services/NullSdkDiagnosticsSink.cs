using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class NullSdkDiagnosticsSink : ISdkDiagnosticsSink
{
    public Task WriteAsync(SdkOperationRequest request, SdkOperationResult result, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
