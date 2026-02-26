using Microsoft.Extensions.Logging;
using System.Text.Json;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Internal;

namespace SwfocTrainer.Saves.Services;

internal sealed class SavePatchApplyServiceHelper
{
    private readonly ISaveCodec _saveCodec;
    private readonly ILogger _logger;
    private readonly string _selectorNotFoundInSchemaText;
    private readonly string _selectorUnknownFieldText;

    public SavePatchApplyServiceHelper(
        ISaveCodec saveCodec,
        ILogger logger,
        string selectorNotFoundInSchemaText,
        string selectorUnknownFieldText)
    {
        _saveCodec = saveCodec;
        _logger = logger;
        _selectorNotFoundInSchemaText = selectorNotFoundInSchemaText;
        _selectorUnknownFieldText = selectorUnknownFieldText;
    }

    public async Task<string?> ResolveLatestBackupPathAsync(string targetPath, CancellationToken cancellationToken)
    {
        if (!TryGetTargetLocation(targetPath, out var directory, out var fileName))
        {
            return null;
        }

        var backupFromReceipt = await TryResolveBackupFromReceiptsAsync(directory, fileName, cancellationToken);
        if (!string.IsNullOrWhiteSpace(backupFromReceipt))
        {
            return backupFromReceipt;
        }

        return ResolveBackupFromCandidates(directory, fileName);
    }

    public (object? Value, SavePatchApplyResult? Failure) TryNormalizePatchValue(SavePatchOperation operation, string reasonValueNormalizationFailed)
    {
        try
        {
            return (SavePatchFieldCodec.NormalizePatchValue(operation.NewValue, operation.ValueType), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Patch value normalization failed for field {FieldId}", operation.FieldId);
            return (
                null,
                BuildFailure(
                    SavePatchApplyClassification.ValidationFailed,
                    reasonValueNormalizationFailed,
                    "Patch operation value could not be normalized.",
                    operation.FieldId,
                    operation.FieldPath));
        }
    }

    public async Task<SavePatchApplyResult?> TryApplyOperationValueAsync(
        SaveDocument targetDoc,
        SavePatchOperation operation,
        object? value,
        string reasonFieldApplyFailed,
        CancellationToken cancellationToken)
    {
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
                reasonFieldApplyFailed,
                "Patch operation could not be applied to target field.",
                operation.FieldId,
                operation.FieldPath);
        }
    }

    public void TryDeleteTempOutput(string tempOutputPath)
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

    private async Task ApplyFieldWithFallbackSelectorAsync(
        SaveDocument targetDoc,
        SavePatchOperation operation,
        object? value,
        CancellationToken cancellationToken)
    {
        var fieldIdAttempt = await TryApplySelectorAsync(
            targetDoc,
            operation.FieldId,
            value,
            operation.FieldId,
            "FieldId selector failed for {FieldId}. Attempting fieldPath fallback.",
            cancellationToken);
        if (fieldIdAttempt.WasApplied)
        {
            return;
        }

        var fieldPathAttempt = await TryApplySelectorAsync(
            targetDoc,
            operation.FieldPath,
            value,
            operation.FieldId,
            "FieldPath fallback selector failed for {FieldId}.",
            cancellationToken);
        if (fieldPathAttempt.WasApplied)
        {
            return;
        }

        if (fieldIdAttempt.MismatchError is not null || fieldPathAttempt.MismatchError is not null)
        {
            throw new InvalidOperationException(
                $"All selectors failed for field '{operation.FieldId}'.",
                fieldPathAttempt.MismatchError ?? fieldIdAttempt.MismatchError);
        }

        throw new InvalidOperationException($"No valid selector was provided for field '{operation.FieldId}'.");
    }

    private static bool TryGetTargetLocation(string targetPath, out string directory, out string fileName)
    {
        directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        fileName = Path.GetFileName(targetPath);
        return !string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(fileName);
    }

    private async Task<string?> TryResolveBackupFromReceiptsAsync(string directory, string fileName, CancellationToken cancellationToken)
    {
        var receiptPattern = $"{fileName}.apply-receipt.*.json";
        var receiptPaths = Directory.EnumerateFiles(directory, receiptPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName);

        foreach (var receiptPath in receiptPaths)
        {
            var backupPath = await TryResolveReceiptBackupPathAsync(receiptPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                return backupPath;
            }
        }

        return null;
    }

    private async Task<string?> TryResolveReceiptBackupPathAsync(string receiptPath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(receiptPath, cancellationToken);
            var receipt = JsonSerializer.Deserialize<SavePatchApplyReceipt>(json, SavePatchApplyService.ReceiptJsonOptions);
            if (receipt is null)
            {
                _logger.LogWarning("Receipt {ReceiptPath} could not be parsed into expected contract. Continuing backup lookup.", receiptPath);
                return null;
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

        return null;
    }

    private static string? ResolveBackupFromCandidates(string directory, string fileName)
    {
        var backupPattern = $"{fileName}.bak.*.sav";
        var backupCandidates = Directory.EnumerateFiles(directory, backupPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName);

        foreach (var candidate in backupCandidates)
        {
            if (TryNormalizeBackupCandidatePath(candidate, out var candidatePath, out _))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private async Task<SelectorApplyAttempt> TryApplySelectorAsync(
        SaveDocument targetDoc,
        string? selector,
        object? value,
        string fieldIdForLogging,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return SelectorApplyAttempt.NotAttempted;
        }

        try
        {
            await _saveCodec.EditAsync(targetDoc, selector, value, cancellationToken);
            return SelectorApplyAttempt.AppliedAttempt;
        }
        catch (Exception ex) when (IsSelectorMismatchError(ex))
        {
            _logger.LogDebug(ex, failureMessage, fieldIdForLogging);
            return SelectorApplyAttempt.Mismatch(ex);
        }
    }

    private bool IsSelectorMismatchError(Exception exception)
    {
        if (exception is not InvalidOperationException)
        {
            return false;
        }

        return exception.Message.Contains(_selectorNotFoundInSchemaText, StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains(_selectorUnknownFieldText, StringComparison.OrdinalIgnoreCase);
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

    private sealed record SelectorApplyAttempt(bool WasApplied, Exception? MismatchError)
    {
        public static SelectorApplyAttempt NotAttempted { get; } = new(WasApplied: false, MismatchError: null);
        public static SelectorApplyAttempt AppliedAttempt { get; } = new(WasApplied: true, MismatchError: null);
        public static SelectorApplyAttempt Mismatch(Exception error) => new(WasApplied: false, MismatchError: error);
    }
}

internal sealed record SavePatchApplyReceipt(
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
