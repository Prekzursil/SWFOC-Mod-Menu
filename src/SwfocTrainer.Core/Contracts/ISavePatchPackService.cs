using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Builds, loads, and validates schema-path save patch packs.
/// </summary>
public interface ISavePatchPackService
{
    /// <summary>
    /// Creates a patch pack by diffing an original and edited save document for a profile.
    /// </summary>
    Task<SavePatchPack> ExportAsync(
        SaveDocument originalDoc,
        SaveDocument editedDoc,
        string profileId,
        CancellationToken cancellationToken);

    Task<SavePatchPack> ExportAsync(
        SaveDocument originalDoc,
        SaveDocument editedDoc,
        string profileId)
    {
        return ExportAsync(originalDoc, editedDoc, profileId, CancellationToken.None);
    }

    /// <summary>
    /// Loads and validates a patch pack contract from disk.
    /// </summary>
    Task<SavePatchPack> LoadPackAsync(string path, CancellationToken cancellationToken);

    Task<SavePatchPack> LoadPackAsync(string path)
    {
        return LoadPackAsync(path, CancellationToken.None);
    }

    /// <summary>
    /// Checks whether a patch pack can be applied to a target save/profile.
    /// </summary>
    Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(
        SavePatchPack pack,
        SaveDocument targetDoc,
        string targetProfileId,
        CancellationToken cancellationToken);

    Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(
        SavePatchPack pack,
        SaveDocument targetDoc,
        string targetProfileId)
    {
        return ValidateCompatibilityAsync(pack, targetDoc, targetProfileId, CancellationToken.None);
    }

    /// <summary>
    /// Produces a non-mutating preview of operations that would be applied.
    /// </summary>
    Task<SavePatchPreview> PreviewApplyAsync(
        SavePatchPack pack,
        SaveDocument targetDoc,
        string targetProfileId,
        CancellationToken cancellationToken);

    Task<SavePatchPreview> PreviewApplyAsync(
        SavePatchPack pack,
        SaveDocument targetDoc,
        string targetProfileId)
    {
        return PreviewApplyAsync(pack, targetDoc, targetProfileId, CancellationToken.None);
    }
}
