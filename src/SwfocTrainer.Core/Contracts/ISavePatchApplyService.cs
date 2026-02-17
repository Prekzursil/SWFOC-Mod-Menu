using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Applies save patch packs atomically and restores backups when requested.
/// </summary>
public interface ISavePatchApplyService
{
    /// <summary>
    /// Applies a patch pack to a target save file using compatibility and validation gates.
    /// </summary>
    Task<SavePatchApplyResult> ApplyAsync(
        string targetSavePath,
        SavePatchPack pack,
        string targetProfileId,
        bool strict = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the most recent backup written for a save path.
    /// </summary>
    Task<SaveRollbackResult> RestoreLastBackupAsync(string targetSavePath, CancellationToken cancellationToken = default);
}
