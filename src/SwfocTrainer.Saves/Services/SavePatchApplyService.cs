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
        bool strict = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedTargetPath = NormalizeTargetPath(targetSavePath);
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var backupPath = $"{normalizedTargetPath}.bak.{runId}.sav";
        var receiptPath = $"{normalizedTargetPath}.apply-receipt.{runId}.json";
        var tempOutputPath = $"{normalizedTargetPath}.tmp.{runId}.sav";

        SaveDocument targetDoc;
        try
        {
            targetDoc = await _saveCodec.LoadAsync(normalizedTargetPath, pack.Metadata.SchemaId, cancellationToken);
        }
        catch (Exception ex)
        {
            return new SavePatchApplyResult(
                SavePatchApplyClassification.CompatibilityFailed,
                Applied: false,
                Message: $"Target load failed: {ex.Message}",
                Failure: new SavePatchApplyFailure("target_load_failed", ex.Message));
        }

        var compatibility = await _patchPackService.ValidateCompatibilityAsync(pack, targetDoc, targetProfileId, cancellationToken);
        if (!compatibility.IsCompatible)
        {
            return new SavePatchApplyResult(
                SavePatchApplyClassification.CompatibilityFailed,
                Applied: false,
                Message: $"Compatibility failed: {string.Join(" | ", compatibility.Errors)}",
                Failure: new SavePatchApplyFailure("compatibility_failed", string.Join(" | ", compatibility.Errors)));
        }

        if (strict && !compatibility.SourceHashMatches)
        {
            return new SavePatchApplyResult(
                SavePatchApplyClassification.CompatibilityFailed,
                Applied: false,
                Message: "Compatibility failed: source hash mismatch in strict mode.",
                Failure: new SavePatchApplyFailure("source_hash_mismatch", "Source hash mismatch in strict mode."));
        }

        var preApplyBytes = targetDoc.Raw.ToArray();
        foreach (var operation in pack.Operations.OrderBy(x => x.Offset))
        {
            if (operation.Kind != SavePatchOperationKind.SetValue)
            {
                RestoreRawSnapshot(targetDoc.Raw, preApplyBytes);
                return new SavePatchApplyResult(
                    SavePatchApplyClassification.ValidationFailed,
                    Applied: false,
                    Message: $"Unsupported operation kind '{operation.Kind}'.",
                    Failure: new SavePatchApplyFailure("unsupported_operation_kind", $"Unsupported operation kind '{operation.Kind}'.", operation.FieldId, operation.FieldPath));
            }

            object? value;
            try
            {
                value = SavePatchFieldCodec.NormalizePatchValue(operation.NewValue, operation.ValueType);
            }
            catch (Exception ex)
            {
                RestoreRawSnapshot(targetDoc.Raw, preApplyBytes);
                return new SavePatchApplyResult(
                    SavePatchApplyClassification.ValidationFailed,
                    Applied: false,
                    Message: $"Value normalization failed for '{operation.FieldId}': {ex.Message}",
                    Failure: new SavePatchApplyFailure("value_normalization_failed", ex.Message, operation.FieldId, operation.FieldPath));
            }

            try
            {
                await ApplyFieldWithFallbackSelectorAsync(targetDoc, operation, value, cancellationToken);
            }
            catch (Exception ex)
            {
                RestoreRawSnapshot(targetDoc.Raw, preApplyBytes);
                return new SavePatchApplyResult(
                    SavePatchApplyClassification.ValidationFailed,
                    Applied: false,
                    Message: $"Field apply failed for '{operation.FieldId}': {ex.Message}",
                    Failure: new SavePatchApplyFailure("field_apply_failed_all_selectors", ex.Message, operation.FieldId, operation.FieldPath));
            }
        }

        var validation = await _saveCodec.ValidateAsync(targetDoc, cancellationToken);
        if (!validation.IsValid)
        {
            RestoreRawSnapshot(targetDoc.Raw, preApplyBytes);
            return new SavePatchApplyResult(
                SavePatchApplyClassification.ValidationFailed,
                Applied: false,
                Message: $"Validation failed: {string.Join(" | ", validation.Errors)}",
                Failure: new SavePatchApplyFailure("validation_failed", string.Join(" | ", validation.Errors)));
        }

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
            if (File.Exists(tempOutputPath))
            {
                File.Delete(tempOutputPath);
            }

            try
            {
                await File.WriteAllBytesAsync(normalizedTargetPath, preApplyBytes, cancellationToken);
                return new SavePatchApplyResult(
                    SavePatchApplyClassification.RolledBack,
                    Applied: false,
                    Message: $"Write failed and rollback restored original bytes: {ex.Message}",
                    BackupPath: File.Exists(backupPath) ? backupPath : null,
                    ReceiptPath: File.Exists(receiptPath) ? receiptPath : null,
                    Failure: new SavePatchApplyFailure("write_failed_rolled_back", ex.Message));
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Rollback failed after write failure for {TargetSavePath}", normalizedTargetPath);
                return new SavePatchApplyResult(
                    SavePatchApplyClassification.WriteFailed,
                    Applied: false,
                    Message: $"Write failed and rollback failed: {rollbackEx.Message}",
                    BackupPath: File.Exists(backupPath) ? backupPath : null,
                    ReceiptPath: File.Exists(receiptPath) ? receiptPath : null,
                    Failure: new SavePatchApplyFailure("write_failed", rollbackEx.Message));
            }
        }
    }

    public async Task<SaveRollbackResult> RestoreLastBackupAsync(string targetSavePath, CancellationToken cancellationToken = default)
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

    private static string NormalizeTargetPath(string path)
    {
        var normalized = TrustedPathPolicy.NormalizeAbsolute(path);
        TrustedPathPolicy.EnsureAllowedExtension(normalized, ".sav");
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

    private static async Task<string?> ResolveLatestBackupPathAsync(string targetPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath);
        var fileName = Path.GetFileName(targetPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var receiptPattern = $"{fileName}.apply-receipt.*.json";
        var latestReceiptPath = Directory.EnumerateFiles(directory, receiptPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(latestReceiptPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(latestReceiptPath, cancellationToken);
                var receipt = JsonSerializer.Deserialize<SavePatchApplyReceipt>(json, ReceiptJsonOptions);
                if (receipt is not null && !string.IsNullOrWhiteSpace(receipt.BackupPath))
                {
                    return receipt.BackupPath;
                }
            }
            catch
            {
                // Fall back to backup file scan.
            }
        }

        var backupPattern = $"{fileName}.bak.*.sav";
        return Directory.EnumerateFiles(directory, backupPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }

    private async Task ApplyFieldWithFallbackSelectorAsync(
        SaveDocument targetDoc,
        SavePatchOperation operation,
        object? value,
        CancellationToken cancellationToken)
    {
        Exception? pathError = null;

        if (!string.IsNullOrWhiteSpace(operation.FieldPath))
        {
            try
            {
                await _saveCodec.EditAsync(targetDoc, operation.FieldPath, value, cancellationToken);
                return;
            }
            catch (Exception ex) when (IsSelectorMismatchError(ex))
            {
                pathError = ex;
                _logger.LogDebug(ex, "FieldPath selector failed for {FieldId}. Falling back to fieldId selector.", operation.FieldId);
            }
        }

        try
        {
            await _saveCodec.EditAsync(targetDoc, operation.FieldId, value, cancellationToken);
            return;
        }
        catch (Exception idError)
        {
            if (pathError is null)
            {
                throw;
            }

            throw new InvalidOperationException(
                $"Both selectors failed for field '{operation.FieldId}'. " +
                $"fieldPath='{operation.FieldPath}' error='{pathError.Message}', " +
                $"fieldId error='{idError.Message}'.",
                idError);
        }
    }

    private static bool IsSelectorMismatchError(Exception exception)
        => exception is InvalidOperationException &&
           exception.Message.Contains("not found in schema", StringComparison.OrdinalIgnoreCase);

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
