using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class NullSdkDiagnosticsSink : ISdkDiagnosticsSink
{
    public Task WriteAsync(SdkOperationRequest request, SdkOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        return Task.CompletedTask;
    }

    public Task WriteAsync(SdkOperationRequest request, SdkOperationResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        return Task.CompletedTask;
    }
}
