using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ISaveCodec
{
    Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken);

    Task<SaveDocument> LoadAsync(string path, string schemaId)
    {
        return LoadAsync(path, schemaId, CancellationToken.None);
    }

    Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken);

    Task EditAsync(SaveDocument document, string nodePath, object? value)
    {
        return EditAsync(document, nodePath, value, CancellationToken.None);
    }

    Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken);

    Task<SaveValidationResult> ValidateAsync(SaveDocument document)
    {
        return ValidateAsync(document, CancellationToken.None);
    }

    Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken);

    Task WriteAsync(SaveDocument document, string outputPath)
    {
        return WriteAsync(document, outputPath, CancellationToken.None);
    }

    Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken);

    Task<bool> RoundTripCheckAsync(SaveDocument document)
    {
        return RoundTripCheckAsync(document, CancellationToken.None);
    }
}
