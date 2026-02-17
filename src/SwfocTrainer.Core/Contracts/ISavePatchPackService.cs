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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads and validates a patch pack contract from disk.
    /// </summary>
    Task<SavePatchPack> LoadPackAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a patch pack can be applied to a target save/profile.
    /// </summary>
    Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(
        SavePatchPack pack,
        SaveDocument targetDoc,
        string targetProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces a non-mutating preview of operations that would be applied.
    /// </summary>
    Task<SavePatchPreview> PreviewApplyAsync(
        SavePatchPack pack,
        SaveDocument targetDoc,
        string targetProfileId,
        CancellationToken cancellationToken = default);
}
