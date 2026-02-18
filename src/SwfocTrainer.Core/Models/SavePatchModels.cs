namespace SwfocTrainer.Core.Models;

/// <summary>
/// Top-level schema-path patch-pack payload for Save Lab import/export.
/// </summary>
public sealed record SavePatchPack(
    SavePatchMetadata Metadata,
    SavePatchCompatibility Compatibility,
    IReadOnlyList<SavePatchOperation> Operations);

/// <summary>
/// Metadata identifying the source save and schema contract used to create a patch pack.
/// </summary>
public sealed record SavePatchMetadata(
    string SchemaVersion,
    string ProfileId,
    string SchemaId,
    string SourceHash,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// One typed mutation to apply to a specific save field.
/// </summary>
public sealed record SavePatchOperation(
    SavePatchOperationKind Kind,
    string FieldPath,
    string FieldId,
    string ValueType,
    object? OldValue,
    object? NewValue,
    int Offset);

/// <summary>
/// Compatibility constraints attached to a patch pack.
/// </summary>
public sealed record SavePatchCompatibility(
    IReadOnlyList<string> AllowedProfileIds,
    string RequiredSchemaId,
    string? SaveBuildHint = null);

/// <summary>
/// Result of evaluating whether a patch pack can be applied to a target save/profile.
/// </summary>
public sealed record SavePatchCompatibilityResult(
    bool IsCompatible,
    bool SourceHashMatches,
    string TargetHash,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Non-mutating preview of what a patch apply would change.
/// </summary>
public sealed record SavePatchPreview(
    bool IsCompatible,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SavePatchOperation> OperationsToApply);

/// <summary>
/// Structured failure payload for failed apply attempts.
/// </summary>
public sealed record SavePatchApplyFailure(
    string ReasonCode,
    string Message,
    string? FieldId = null,
    string? FieldPath = null);

/// <summary>
/// Result returned by Save Lab patch apply operations.
/// </summary>
public sealed record SavePatchApplyResult(
    SavePatchApplyClassification Classification,
    bool Applied,
    string Message,
    string? OutputPath = null,
    string? BackupPath = null,
    string? ReceiptPath = null,
    SavePatchApplyFailure? Failure = null);

/// <summary>
/// Result returned by backup restore operations.
/// </summary>
public sealed record SaveRollbackResult(
    bool Restored,
    string Message,
    string? TargetPath = null,
    string? BackupPath = null,
    string? RestoredHash = null);
