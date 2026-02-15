using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ISaveCodec
{
    Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken = default);

    Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken = default);

    Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken = default);

    Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken = default);

    Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken = default);
}
