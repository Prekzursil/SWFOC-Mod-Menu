using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Internal;

namespace SwfocTrainer.Saves.Services;

/// <summary>
/// Atomic apply + backup/receipt + restore pipeline for save patch packs.
/// </summary>
public sealed class SavePatchApplyService : ISavePatchApplyService
{
    internal static readonly JsonSerializerOptions ReceiptJsonOptions = new()
    {
        WriteIndented = true
    };

    private const string RunIdFormat = "yyyyMMddHHmmssfff";
    private const string SaveExtension = ".sav";
    private const string ReasonTargetLoadFailed = "target_load_failed";
    private const string ReasonCompatibilityFailed = "compatibility_failed";
    private const string ReasonSourceHashMismatch = "source_hash_mismatch";
    private const string ReasonUnsupportedOperationKind = "unsupported_operation_kind";
    private const string ReasonNewValueMissing = "new_value_missing";
    private const string ReasonValueNormalizationFailed = "value_normalization_failed";
    private const string ReasonFieldApplyFailed = "field_apply_failed_all_selectors";
    private const string ReasonValidationFailed = "validation_failed";
    private const string ReasonWriteFailedRolledBack = "write_failed_rolled_back";
    private const string ReasonWriteFailed = "write_failed";
    private const string SelectorNotFoundInSchemaText = "not found in schema";
    private const string SelectorUnknownFieldText = "unknown save field selector";

    private readonly ISaveCodec _saveCodec;
    private readonly ISavePatchPackService _patchPackService;
    private readonly ILogger<SavePatchApplyService> _logger;
    private readonly SavePatchApplyServiceHelper _helper;

    public SavePatchApplyService(
        ISaveCodec saveCodec,
        ISavePatchPackService patchPackService,
        ILogger<SavePatchApplyService> logger)
    {
        _saveCodec = saveCodec;
        _patchPackService = patchPackService;
        _logger = logger;
        _helper = new SavePatchApplyServiceHelper(
            saveCodec,
            logger,
            SelectorNotFoundInSchemaText,
            SelectorUnknownFieldText);
    }

    public async Task<SavePatchApplyResult> ApplyAsync(
        string targetSavePath,
        SavePatchPack pack,
        string targetProfileId,
        bool strict,
        CancellationToken cancellationToken)
    {
        var paths = BuildApplyFilePaths(targetSavePath);
        var targetLoad = await TryLoadTargetDocumentAsync(paths.TargetPath, pack.Metadata.SchemaId, cancellationToken);
        if (targetLoad.Failure is not null)
        {
            return targetLoad.Failure;
        }

        var targetDoc = targetLoad.Document!;
        var compatibility = await _patchPackService.ValidateCompatibilityAsync(pack, targetDoc, targetProfileId, cancellationToken);
        var compatibilityFailure = ValidateCompatibility(targetProfileId, strict, compatibility);
        if (compatibilityFailure is not null)
        {
            return compatibilityFailure;
        }

        var preApplyBytes = targetDoc.Raw.ToArray();
        var operationFailure = await ApplyOperationsAsync(targetDoc, pack.Operations, preApplyBytes, cancellationToken);
        if (operationFailure is not null)
        {
            return operationFailure;
        }

        var validationFailure = await ValidatePatchedDocumentAsync(targetDoc, preApplyBytes, cancellationToken);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        return await PersistPatchedSaveAsync(
            targetDoc,
            pack,
            compatibility,
            targetProfileId,
            paths,
            preApplyBytes,
            cancellationToken);
    }

    public Task<SavePatchApplyResult> ApplyAsync(
        string targetSavePath,
        SavePatchPack pack,
        string targetProfileId)
    {
        return ApplyAsync(targetSavePath, pack, targetProfileId, strict: true, CancellationToken.None);
    }

    public Task<SavePatchApplyResult> ApplyAsync(
        string targetSavePath,
        SavePatchPack pack,
        string targetProfileId,
        bool strict)
    {
        return ApplyAsync(targetSavePath, pack, targetProfileId, strict, CancellationToken.None);
    }

    public async Task<SaveRollbackResult> RestoreLastBackupAsync(string targetSavePath, CancellationToken cancellationToken)
    {
        var normalizedTargetPath = NormalizeTargetPath(targetSavePath);
        var backupPath = await _helper.ResolveLatestBackupPathAsync(normalizedTargetPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            return new SaveRollbackResult(
                Restored: false,
                Message: "No backup file was found for the selected save path.",
                TargetPath: normalizedTargetPath);
        }

        try
        {
            var backupBytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
            await File.WriteAllBytesAsync(normalizedTargetPath, backupBytes, cancellationToken);
            var restoredHash = SavePatchFieldCodec.ComputeSha256Hex(backupBytes);

            return new SaveRollbackResult(
                Restored: true,
                Message: "Backup restore completed.",
                TargetPath: normalizedTargetPath,
                BackupPath: backupPath,
                RestoredHash: restoredHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup restore failed. Target={TargetPath} Backup={BackupPath}", normalizedTargetPath, backupPath);
            return new SaveRollbackResult(
                Restored: false,
                Message: "Backup restore failed.",
                TargetPath: normalizedTargetPath,
                BackupPath: backupPath);
        }
    }

    public Task<SaveRollbackResult> RestoreLastBackupAsync(string targetSavePath)
    {
        return RestoreLastBackupAsync(targetSavePath, CancellationToken.None);
    }

    private async Task<(SaveDocument? Document, SavePatchApplyResult? Failure)> TryLoadTargetDocumentAsync(
        string normalizedTargetPath,
        string schemaId,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetDoc = await _saveCodec.LoadAsync(normalizedTargetPath, schemaId, cancellationToken);
            return (targetDoc, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Target save load failed for patch apply. Target={TargetPath} Schema={SchemaId}", normalizedTargetPath, schemaId);
            return (
                null,
                BuildFailure(
                    SavePatchApplyClassification.CompatibilityFailed,
                    ReasonTargetLoadFailed,
                    "Target save could not be loaded."));
        }
    }

    private SavePatchApplyResult? ValidateCompatibility(
        string targetProfileId,
        bool strict,
        SavePatchCompatibilityResult compatibility)
    {
        if (!compatibility.IsCompatible)
        {
            _logger.LogWarning(
                "Patch compatibility check failed for profile {ProfileId}. Errors={Errors}",
                targetProfileId,
                string.Join(" | ", compatibility.Errors));
            return BuildFailure(
                SavePatchApplyClassification.CompatibilityFailed,
                ReasonCompatibilityFailed,
                "Compatibility checks failed for this patch pack.");
        }

        if (strict && !compatibility.SourceHashMatches)
        {
            return BuildFailure(
                SavePatchApplyClassification.CompatibilityFailed,
                ReasonSourceHashMismatch,
                "Strict apply blocked this patch because source hash does not match target save.");
        }

        return null;
    }

    private async Task<SavePatchApplyResult?> ApplyOperationsAsync(
        SaveDocument targetDoc,
        IReadOnlyList<SavePatchOperation> operations,
        byte[] preApplyBytes,
        CancellationToken cancellationToken)
    {
        foreach (var operation in operations.OrderBy(x => x.Offset))
        {
            var failure = await ApplyOperationAsync(targetDoc, operation, cancellationToken);
            if (failure is null)
            {
                continue;
            }

            RestoreRawSnapshot(targetDoc.Raw, preApplyBytes);
            return failure;
        }

        return null;
    }

    private async Task<SavePatchApplyResult?> ApplyOperationAsync(
        SaveDocument targetDoc,
        SavePatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.Kind != SavePatchOperationKind.SetValue)
        {
            return BuildFailure(
                SavePatchApplyClassification.ValidationFailed,
                ReasonUnsupportedOperationKind,
                "Patch operation kind is not supported.",
                operation.FieldId,
                operation.FieldPath);
        }

        if (operation.NewValue is null)
        {
            return BuildFailure(
                SavePatchApplyClassification.ValidationFailed,
                ReasonNewValueMissing,
                "Patch operation is missing required newValue.",
                operation.FieldId,
                operation.FieldPath);
        }

        var normalization = _helper.TryNormalizePatchValue(operation, ReasonValueNormalizationFailed);
        if (normalization.Failure is not null)
        {
            return normalization.Failure;
        }

        return await _helper.TryApplyOperationValueAsync(
            targetDoc,
            operation,
            normalization.Value,
            ReasonFieldApplyFailed,
            cancellationToken);
    }

    private async Task<SavePatchApplyResult?> ValidatePatchedDocumentAsync(
        SaveDocument targetDoc,
        byte[] preApplyBytes,
        CancellationToken cancellationToken)
    {
        var validation = await _saveCodec.ValidateAsync(targetDoc, cancellationToken);
        if (validation.IsValid)
        {
            return null;
        }

        _logger.LogWarning(
            "Patched save failed validation. Errors={Errors}",
            string.Join(" | ", validation.Errors));
        RestoreRawSnapshot(targetDoc.Raw, preApplyBytes);
        return BuildFailure(
            SavePatchApplyClassification.ValidationFailed,
            ReasonValidationFailed,
            "Patched save failed validation checks.");
    }

    private async Task<SavePatchApplyResult> PersistPatchedSaveAsync(
        SaveDocument targetDoc,
        SavePatchPack pack,
        SavePatchCompatibilityResult compatibility,
        string targetProfileId,
        ApplyFilePaths paths,
        byte[] preApplyBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllBytesAsync(paths.BackupPath, preApplyBytes, cancellationToken);

            await _saveCodec.WriteAsync(targetDoc, paths.TempOutputPath, cancellationToken);
            File.Move(paths.TempOutputPath, paths.TargetPath, overwrite: true);

            var appliedHash = SavePatchFieldCodec.ComputeSha256Hex(await File.ReadAllBytesAsync(paths.TargetPath, cancellationToken));
            await WriteReceiptAsync(paths.ReceiptPath, new SavePatchApplyReceipt(
                RunId: paths.RunId,
                AppliedAtUtc: DateTimeOffset.UtcNow,
                TargetPath: paths.TargetPath,
                BackupPath: paths.BackupPath,
                ReceiptPath: paths.ReceiptPath,
                ProfileId: targetProfileId,
                SchemaId: pack.Metadata.SchemaId,
                Classification: SavePatchApplyClassification.Applied.ToString(),
                SourceHash: pack.Metadata.SourceHash,
                TargetHash: compatibility.TargetHash,
                AppliedHash: appliedHash,
                OperationsApplied: pack.Operations.Count), cancellationToken);

            return new SavePatchApplyResult(
                SavePatchApplyClassification.Applied,
                Applied: true,
                Message: $"Applied {pack.Operations.Count} operation(s).",
                OutputPath: paths.TargetPath,
                BackupPath: paths.BackupPath,
                ReceiptPath: paths.ReceiptPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Patch apply write path failed for {TargetSavePath}", paths.TargetPath);
            _helper.TryDeleteTempOutput(paths.TempOutputPath);
            return await RestoreAfterWriteFailureAsync(paths.TargetPath, preApplyBytes, paths.BackupPath, paths.ReceiptPath, cancellationToken);
        }
    }

    private async Task<SavePatchApplyResult> RestoreAfterWriteFailureAsync(
        string normalizedTargetPath,
        byte[] preApplyBytes,
        string backupPath,
        string receiptPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllBytesAsync(normalizedTargetPath, preApplyBytes, cancellationToken);
            return BuildFailure(
                SavePatchApplyClassification.RolledBack,
                ReasonWriteFailedRolledBack,
                "Save write failed and original bytes were restored.",
                backupPath: File.Exists(backupPath) ? backupPath : null,
                receiptPath: File.Exists(receiptPath) ? receiptPath : null);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx, "Rollback failed after write failure for {TargetSavePath}", normalizedTargetPath);
            return BuildFailure(
                SavePatchApplyClassification.WriteFailed,
                ReasonWriteFailed,
                "Save write failed and automatic rollback did not complete.",
                backupPath: File.Exists(backupPath) ? backupPath : null,
                receiptPath: File.Exists(receiptPath) ? receiptPath : null);
        }
    }

    private static string NormalizeTargetPath(string path)
    {
        var normalized = TrustedPathPolicy.NormalizeAbsolute(path);
        TrustedPathPolicy.EnsureAllowedExtension(normalized, SaveExtension);
        if (!File.Exists(normalized))
        {
            throw new FileNotFoundException($"Save file not found: {normalized}", normalized);
        }

        return normalized;
    }

    private static void RestoreRawSnapshot(byte[] targetRaw, byte[] snapshot)
    {
        if (targetRaw.Length != snapshot.Length)
        {
            throw new InvalidOperationException("Save buffer length changed unexpectedly during patch apply.");
        }

        Buffer.BlockCopy(snapshot, 0, targetRaw, 0, snapshot.Length);
    }

    private static async Task WriteReceiptAsync(string receiptPath, SavePatchApplyReceipt receipt, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(receipt, ReceiptJsonOptions);
        await File.WriteAllTextAsync(receiptPath, json, cancellationToken);
    }

    private static ApplyFilePaths BuildApplyFilePaths(string targetSavePath)
    {
        var targetPath = NormalizeTargetPath(targetSavePath);
        var runId = DateTimeOffset.UtcNow.ToString(RunIdFormat);
        return new ApplyFilePaths(
            TargetPath: targetPath,
            BackupPath: $"{targetPath}.bak.{runId}.sav",
            ReceiptPath: $"{targetPath}.apply-receipt.{runId}.json",
            TempOutputPath: $"{targetPath}.tmp.{runId}.sav",
            RunId: runId);
    }

    private static SavePatchApplyResult BuildFailure(
        SavePatchApplyClassification classification,
        string reasonCode,
        string message,
        string? fieldId = null,
        string? fieldPath = null,
        string? backupPath = null,
        string? receiptPath = null)
    {
        return new SavePatchApplyResult(
            classification,
            Applied: false,
            Message: message,
            BackupPath: backupPath,
            ReceiptPath: receiptPath,
            Failure: new SavePatchApplyFailure(reasonCode, message, fieldId, fieldPath));
    }

    private sealed record ApplyFilePaths(
        string TargetPath,
        string BackupPath,
        string ReceiptPath,
        string TempOutputPath,
        string RunId);
}
