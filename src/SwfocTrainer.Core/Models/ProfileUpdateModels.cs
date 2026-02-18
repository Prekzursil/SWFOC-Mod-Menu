namespace SwfocTrainer.Core.Models;

/// <summary>
/// Result of transactional profile install.
/// </summary>
public sealed record ProfileInstallResult(
    bool Succeeded,
    string ProfileId,
    string InstalledPath,
    string? BackupPath,
    string? ReceiptPath,
    string Message,
    string? ReasonCode = null);

/// <summary>
/// Result of profile rollback operation.
/// </summary>
public sealed record ProfileRollbackResult(
    bool Restored,
    string ProfileId,
    string RestoredPath,
    string? BackupPath,
    string Message,
    string? ReasonCode = null);
