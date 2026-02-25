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
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new()
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

    public SavePatchApplyService(
        ISaveCodec saveCodec,
        ISavePatchPackService patchPackService,
        ILogger<SavePatchApplyService> logger)
    {
        _saveCodec = saveCodec;
        _patchPackService = patchPackService;
        _logger = logger;
    }

    public async Task<SavePatchApplyResult> ApplyAsync(
        string targetSavePath,
        SavePatchPack pack,
        string targetProfileId,
        bool strict,
        CancellationToken cancellationToken)
    {
        var normalizedTargetPath = NormalizeTargetPath(targetSavePath);
        var runId = DateTimeOffset.UtcNow.ToString(RunIdFormat);
        var backupPath = $"{normalizedTargetPath}.bak.{runId}.sav";
        var receiptPath = $"{normalizedTargetPath}.apply-receipt.{runId}.json";
        var tempOutputPath = $"{normalizedTargetPath}.tmp.{runId}.sav";

        var targetLoad = await TryLoadTargetDocumentAsync(normalizedTargetPath, pack.Metadata.SchemaId, cancellationToken);
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
            normalizedTargetPath,
            backupPath,
            receiptPath,
            tempOutputPath,
            runId,
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
        var backupPath = await ResolveLatestBackupPathAsync(normalizedTargetPath, cancellationToken);
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

        object? value;
        try
        {
            value = SavePatchFieldCodec.NormalizePatchValue(operation.NewValue, operation.ValueType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Patch value normalization failed for field {FieldId}", operation.FieldId);
            return BuildFailure(
                SavePatchApplyClassification.ValidationFailed,
                ReasonValueNormalizationFailed,
                "Patch operation value could not be normalized.",
                operation.FieldId,
                operation.FieldPath);
        }

        try
        {
            await ApplyFieldWithFallbackSelectorAsync(targetDoc, operation, value, cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Patch field apply failed for {FieldId}", operation.FieldId);
            return BuildFailure(
                SavePatchApplyClassification.ValidationFailed,
                ReasonFieldApplyFailed,
                "Patch operation could not be applied to target field.",
                operation.FieldId,
                operation.FieldPath);
        }
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
        string normalizedTargetPath,
        string backupPath,
        string receiptPath,
        string tempOutputPath,
        string runId,
        byte[] preApplyBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllBytesAsync(backupPath, preApplyBytes, cancellationToken);

            await _saveCodec.WriteAsync(targetDoc, tempOutputPath, cancellationToken);
            File.Move(tempOutputPath, normalizedTargetPath, overwrite: true);

            var appliedHash = SavePatchFieldCodec.ComputeSha256Hex(await File.ReadAllBytesAsync(normalizedTargetPath, cancellationToken));
            await WriteReceiptAsync(receiptPath, new SavePatchApplyReceipt(
                RunId: runId,
                AppliedAtUtc: DateTimeOffset.UtcNow,
                TargetPath: normalizedTargetPath,
                BackupPath: backupPath,
                ReceiptPath: receiptPath,
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
                OutputPath: normalizedTargetPath,
                BackupPath: backupPath,
                ReceiptPath: receiptPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Patch apply write path failed for {TargetSavePath}", normalizedTargetPath);
            TryDeleteTempOutput(tempOutputPath);
            return await RestoreAfterWriteFailureAsync(normalizedTargetPath, preApplyBytes, backupPath, receiptPath, cancellationToken);
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

    private async Task<string?> ResolveLatestBackupPathAsync(string targetPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath);
        var fileName = Path.GetFileName(targetPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var receiptPattern = $"{fileName}.apply-receipt.*.json";
        var receiptPaths = Directory.EnumerateFiles(directory, receiptPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .ToArray();

        foreach (var receiptPath in receiptPaths)
        {
            try
            {
                var json = await File.ReadAllTextAsync(receiptPath, cancellationToken);
                var receipt = JsonSerializer.Deserialize<SavePatchApplyReceipt>(json, ReceiptJsonOptions);
                if (receipt is null)
                {
                    _logger.LogWarning("Receipt {ReceiptPath} could not be parsed into expected contract. Continuing backup lookup.", receiptPath);
                    continue;
                }

                if (TryNormalizeBackupCandidatePath(receipt.BackupPath, out var candidateBackupPath, out var invalidReason))
                {
                    return candidateBackupPath;
                }

                _logger.LogWarning(
                    "Receipt {ReceiptPath} had invalid backup path '{BackupPath}': {Reason}. Continuing backup lookup.",
                    receiptPath,
                    receipt.BackupPath,
                    invalidReason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Receipt parse failed for {ReceiptPath}. Continuing backup lookup.", receiptPath);
            }
        }

        var backupPattern = $"{fileName}.bak.*.sav";
        var backupCandidates = Directory.EnumerateFiles(directory, backupPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .ToArray();

        foreach (var candidate in backupCandidates)
        {
            if (TryNormalizeBackupCandidatePath(candidate, out var candidatePath, out _))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private async Task ApplyFieldWithFallbackSelectorAsync(
        SaveDocument targetDoc,
        SavePatchOperation operation,
        object? value,
        CancellationToken cancellationToken)
    {
        Exception? fieldIdError = null;
        Exception? fieldPathError = null;

        if (!string.IsNullOrWhiteSpace(operation.FieldId))
        {
            try
            {
                await _saveCodec.EditAsync(targetDoc, operation.FieldId, value, cancellationToken);
                return;
            }
            catch (Exception ex) when (IsSelectorMismatchError(ex))
            {
                fieldIdError = ex;
                _logger.LogDebug(ex, "FieldId selector failed for {FieldId}. Attempting fieldPath fallback.", operation.FieldId);
            }
        }

        if (!string.IsNullOrWhiteSpace(operation.FieldPath))
        {
            try
            {
                await _saveCodec.EditAsync(targetDoc, operation.FieldPath, value, cancellationToken);
                return;
            }
            catch (Exception ex) when (IsSelectorMismatchError(ex))
            {
                fieldPathError = ex;
                _logger.LogDebug(ex, "FieldPath fallback selector failed for {FieldId}.", operation.FieldId);
            }
        }

        if (fieldIdError is not null || fieldPathError is not null)
        {
            throw new InvalidOperationException(
                $"All selectors failed for field '{operation.FieldId}'.",
                fieldPathError ?? fieldIdError);
        }

        throw new InvalidOperationException($"No valid selector was provided for field '{operation.FieldId}'.");
    }

    private static bool IsSelectorMismatchError(Exception exception)
    {
        if (exception is not InvalidOperationException)
        {
            return false;
        }

        return exception.Message.Contains(SelectorNotFoundInSchemaText, StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains(SelectorUnknownFieldText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeBackupCandidatePath(string? path, out string? normalized, out string invalidReason)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            invalidReason = "path is empty";
            return false;
        }

        try
        {
            normalized = TrustedPathPolicy.NormalizeAbsolute(path);
        }
        catch (Exception ex)
        {
            invalidReason = $"path normalization failed: {ex.GetType().Name}";
            return false;
        }

        if (!normalized.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
        {
            invalidReason = "path does not use .sav extension";
            normalized = null;
            return false;
        }

        if (!File.Exists(normalized))
        {
            invalidReason = "backup file does not exist";
            normalized = null;
            return false;
        }

        invalidReason = string.Empty;
        return true;
    }

    private void TryDeleteTempOutput(string tempOutputPath)
    {
        if (!File.Exists(tempOutputPath))
        {
            return;
        }

        try
        {
            File.Delete(tempOutputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Temporary patch output cleanup failed for {TempOutputPath}", tempOutputPath);
        }
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

    private sealed record SavePatchApplyReceipt(
        string RunId,
        DateTimeOffset AppliedAtUtc,
        string TargetPath,
        string BackupPath,
        string ReceiptPath,
        string ProfileId,
        string SchemaId,
        string Classification,
        string SourceHash,
        string TargetHash,
        string AppliedHash,
        int OperationsApplied);
}
