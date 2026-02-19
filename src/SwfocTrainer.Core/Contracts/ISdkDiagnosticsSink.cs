using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Sink for structured SDK diagnostics records.
/// </summary>
public interface ISdkDiagnosticsSink
{
    Task WriteAsync(SdkOperationRequest request, SdkOperationResult result, CancellationToken cancellationToken = default);
}
