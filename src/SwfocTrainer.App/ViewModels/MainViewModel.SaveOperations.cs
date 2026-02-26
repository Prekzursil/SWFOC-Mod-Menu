using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Services;

namespace SwfocTrainer.App.ViewModels;

public sealed partial class MainViewModel
{
    private Task BrowseSaveAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Save files (*.sav)|*.sav|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog().GetValueOrDefault())
        {
            SavePath = dialog.FileName;
            Status = $"Selected save: {SavePath}";
        }

        return Task.CompletedTask;
    }

    private Task LoadSaveAsync()
        => LoadSaveAsync(clearPatchSummary: true);

    private async Task LoadSaveAsync(bool clearPatchSummary)
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var profile = await _profiles.ResolveInheritedProfileAsync(SelectedProfileId);
        _loadedSave = await _saveCodec.LoadAsync(SavePath, profile.SaveSchemaId);
        _loadedSaveOriginal = _loadedSave.Raw.ToArray();
        RebuildSaveFieldRows();
        await RefreshDiffAsync();
        ClearPatchPreviewState(clearLoadedPack: false);
        if (clearPatchSummary)
        {
            SavePatchApplySummary = string.Empty;
        }

        Status = $"Loaded save with schema {profile.SaveSchemaId} ({_loadedSave.Raw.Length} bytes)";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task EditSaveAsync()
    {
        if (_loadedSave is null)
        {
            return;
        }

        object? value = ParsePrimitive(SaveEditValue);
        await _saveCodec.EditAsync(_loadedSave, SaveNodePath, value);
        RebuildSaveFieldRows();
        await RefreshDiffAsync();
        ClearPatchPreviewState(clearLoadedPack: false);
        Status = $"Edited save field: {SaveNodePath}";
    }

    private async Task ValidateSaveAsync()
    {
        if (_loadedSave is null)
        {
            return;
        }

        var result = await _saveCodec.ValidateAsync(_loadedSave);
        Status = result.IsValid
            ? $"Save validation passed ({result.Warnings.Count} warning(s))"
            : $"Save validation failed ({result.Errors.Count} error(s))";
    }

    private async Task WriteSaveAsync()
    {
        if (_loadedSave is null)
        {
            return;
        }

        var output = TrustedPathPolicy.BuildSiblingFilePath(_loadedSave.Path, ".edited");
        TrustedPathPolicy.EnsureAllowedExtension(output, ".sav");

        await _saveCodec.WriteAsync(_loadedSave, output);
        Status = $"Wrote edited save: {output}";
    }

    private Task BrowsePatchPackAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Patch pack (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog().GetValueOrDefault())
        {
            SavePatchPackPath = dialog.FileName;
            Status = $"Selected patch pack: {SavePatchPackPath}";
        }

        return Task.CompletedTask;
    }

    private async Task ExportPatchPackAsync()
    {
        if (_loadedSave is null || _loadedSaveOriginal is null || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        var originalDocument = _loadedSave with { Raw = _loadedSaveOriginal.ToArray() };
        var pack = await _savePatchPackService.ExportAsync(originalDocument, _loadedSave, SelectedProfileId);

        var dialog = new SaveFileDialog
        {
            Filter = "Patch pack (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(_loadedSave.Path)}.patch.json",
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (!dialog.ShowDialog().GetValueOrDefault())
        {
            Status = "Patch-pack export canceled.";
            return;
        }

        var outputPath = TrustedPathPolicy.NormalizeAbsolute(dialog.FileName);
        TrustedPathPolicy.EnsureAllowedExtension(outputPath, ".json");
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Patch-pack export path has no parent directory.");
        }

        Directory.CreateDirectory(outputDirectory);
        var json = JsonSerializer.Serialize(pack, SavePatchJson);
        await File.WriteAllTextAsync(outputPath, json);

        SetLoadedPatchPack(pack, outputPath);
        SavePatchApplySummary = string.Empty;
        Status = $"Exported patch pack ({pack.Operations.Count} op(s)): {outputPath}";
    }

    private async Task LoadPatchPackAsync()
    {
        var pack = await _savePatchPackService.LoadPackAsync(SavePatchPackPath);
        SetLoadedPatchPack(pack, SavePatchPackPath);
        SavePatchApplySummary = string.Empty;
        Status = $"Loaded patch pack ({pack.Operations.Count} op(s)).";
    }

    private async Task PreviewPatchPackAsync()
    {
        if (_loadedPatchPack is null || _loadedSave is null || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (!PreparePatchPreview(SelectedProfileId))
        {
            return;
        }

        var compatibility = await _savePatchPackService.ValidateCompatibilityAsync(_loadedPatchPack, _loadedSave, SelectedProfileId);
        var preview = await _savePatchPackService.PreviewApplyAsync(_loadedPatchPack, _loadedSave, SelectedProfileId);
        _loadedPatchPreview = preview;

        PopulatePatchPreviewOperations(preview);
        PopulatePatchCompatibilityRows(compatibility, preview);
        SavePatchMetadataSummary = BuildPatchMetadataSummary(_loadedPatchPack);
        SavePatchApplySummary = string.Empty;
        Status = preview.IsCompatible && compatibility.IsCompatible
            ? $"Patch preview ready: {SavePatchOperations.Count} operation(s) would be applied."
            : "Patch preview blocked by compatibility/validation errors.";
    }

    private bool PreparePatchPreview(string selectedProfileId)
    {
        if (ValidateSaveRuntimeVariant(selectedProfileId, out var variantMessage))
        {
            return true;
        }

        SavePatchCompatibility.Clear();
        SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("error", "save_variant_mismatch", variantMessage));
        SavePatchApplySummary = variantMessage;
        Status = variantMessage;
        return false;
    }

    private void PopulatePatchPreviewOperations(SavePatchPreview preview)
    {
        SavePatchOperations.Clear();
        foreach (var operation in preview.OperationsToApply)
        {
            SavePatchOperations.Add(new SavePatchOperationViewItem(
                operation.Kind.ToString(),
                operation.FieldPath,
                operation.FieldId,
                operation.ValueType,
                FormatPatchValue(operation.OldValue),
                FormatPatchValue(operation.NewValue)));
        }
    }

    private void PopulatePatchCompatibilityRows(SavePatchCompatibilityResult compatibility, SavePatchPreview preview)
    {
        SavePatchCompatibility.Clear();
        SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem(
            "info",
            "source_hash_match",
            compatibility.SourceHashMatches ? "Source hash matches target save." : "Source hash mismatch (strict apply blocks this)."));
        SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem(
            "info",
            "strict_apply_mode",
            IsStrictPatchApply
                ? "Strict apply is ON: source hash mismatch blocks apply."
                : "Strict apply is OFF: source hash mismatch warning will not block apply."));
        AppendPatchCompatibilityRows("warning", "compatibility_warning", compatibility.Warnings);
        AppendPatchCompatibilityRows("warning", "preview_warning", preview.Warnings);
        AppendPatchCompatibilityRows("error", "compatibility_error", compatibility.Errors);
        AppendPatchCompatibilityRows("error", "preview_error", preview.Errors);
    }

    private void AppendPatchCompatibilityRows(string severity, string reasonCode, IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem(severity, reasonCode, message));
        }
    }

    private static string BuildPatchMetadataSummary(SavePatchPack pack)
    {
        return $"Patch {(pack.Metadata.SchemaVersion)} | profile={pack.Metadata.ProfileId} | schema={pack.Metadata.SchemaId} | ops={pack.Operations.Count}";
    }

    private async Task ApplyPatchPackAsync()
    {
        if (_loadedPatchPack is null || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (!ValidateSaveRuntimeVariant(SelectedProfileId, out var variantMessage))
        {
            SavePatchApplySummary = variantMessage;
            Status = variantMessage;
            return;
        }

        var expectedOperationCount = _loadedPatchPreview?.OperationsToApply.Count ?? _loadedPatchPack.Operations.Count;
        var result = await _savePatchApplyService.ApplyAsync(SavePath, _loadedPatchPack, SelectedProfileId, strict: IsStrictPatchApply);
        var summary = $"{result.Classification}: {result.Message}";
        if (result.Applied)
        {
            await LoadSaveAsync(clearPatchSummary: false);
            SavePatchApplySummary = summary;
            AppendPatchArtifactRows(result.BackupPath, result.ReceiptPath);
        }
        else
        {
            SavePatchApplySummary = summary;
        }

        Status = result.Applied
            ? $"Patch applied successfully ({result.Classification}, ops={expectedOperationCount})."
            : $"Patch apply failed ({result.Classification}): {result.Message}";
    }

    private async Task RestoreBackupAsync()
    {
        var result = await _savePatchApplyService.RestoreLastBackupAsync(SavePath);
        var summary = result.Message;
        if (result.Restored && !string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            await LoadSaveAsync(clearPatchSummary: false);
            SavePatchApplySummary = summary;
            AppendPatchArtifactRows(result.BackupPath, null);
        }
        else
        {
            SavePatchApplySummary = summary;
        }

        Status = result.Restored
            ? $"Backup restored: {result.BackupPath}"
            : $"Backup restore skipped: {result.Message}";
    }

    private bool ValidateSaveRuntimeVariant(string requestedProfileId, out string message)
    {
        message = string.Empty;
        var session = _runtime.CurrentSession;
        if (session?.Process.Metadata is null)
        {
            return true;
        }

        if (!session.Process.Metadata.TryGetValue("resolvedVariant", out var runtimeVariant) ||
            string.IsNullOrWhiteSpace(runtimeVariant))
        {
            return true;
        }

        if (requestedProfileId.Equals(UniversalProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (runtimeVariant.Equals(requestedProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        message = $"Blocked by runtime/save variant mismatch (reasonCode=save_variant_mismatch): runtime={runtimeVariant}, selected={requestedProfileId}.";
        return false;
    }

    private Task RefreshDiffAsync()
    {
        SaveDiffPreview.Clear();

        if (_loadedSaveOriginal is null || _loadedSave is null)
        {
            return Task.CompletedTask;
        }

        var diff = SaveDiffService.BuildDiffPreview(_loadedSaveOriginal, _loadedSave.Raw, 400);
        foreach (var line in diff)
        {
            SaveDiffPreview.Add(line);
        }

        if (SaveDiffPreview.Count == 0)
        {
            SaveDiffPreview.Add("No differences detected.");
        }

        return Task.CompletedTask;
    }

    private void RebuildSaveFieldRows()
    {
        SaveFields.Clear();
        if (_loadedSave is null)
        {
            return;
        }

        foreach (var row in FlattenNodes(_loadedSave.Root))
        {
            SaveFields.Add(row);
        }

        ApplySaveSearch();
    }

    private IEnumerable<SaveFieldViewItem> FlattenNodes(SaveNode root)
    {
        if (root.Children is null || root.Children.Count == 0)
        {
            if (!string.Equals(root.ValueType, "root", StringComparison.OrdinalIgnoreCase))
            {
                yield return new SaveFieldViewItem(root.Path, root.Name, root.ValueType, root.Value?.ToString() ?? string.Empty);
            }

            yield break;
        }

        foreach (var child in root.Children)
        {
            foreach (var nested in FlattenNodes(child))
            {
                yield return nested;
            }
        }
    }

    private void ApplySaveSearch()
    {
        FilteredSaveFields.Clear();
        IEnumerable<SaveFieldViewItem> source = SaveFields;

        if (!string.IsNullOrWhiteSpace(SaveSearchQuery))
        {
            source = source.Where(x =>
                x.Path.Contains(SaveSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains(SaveSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                x.Value.Contains(SaveSearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in source.Take(5000))
        {
            FilteredSaveFields.Add(row);
        }
    }

    private void SetLoadedPatchPack(SavePatchPack pack, string path)
    {
        _loadedPatchPack = pack;
        SavePatchPackPath = path;
        SavePatchMetadataSummary =
            $"Patch {pack.Metadata.SchemaVersion} | profile={pack.Metadata.ProfileId} | schema={pack.Metadata.SchemaId} | ops={pack.Operations.Count}";
        ClearPatchPreviewState(clearLoadedPack: false);
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearPatchPreviewState(bool clearLoadedPack)
    {
        if (clearLoadedPack)
        {
            _loadedPatchPack = null;
            SavePatchMetadataSummary = "No patch pack loaded.";
        }

        _loadedPatchPreview = null;
        SavePatchOperations.Clear();
        SavePatchCompatibility.Clear();
        CommandManager.InvalidateRequerySuggested();
    }

    private void AppendPatchArtifactRows(string? backupPath, string? receiptPath)
    {
        if (!string.IsNullOrWhiteSpace(backupPath))
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("info", "backup_path", backupPath));
        }

        if (!string.IsNullOrWhiteSpace(receiptPath))
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("info", "receipt_path", receiptPath));
        }
    }

    private static string FormatPatchValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Null => "null",
                _ => element.ToString()
            };
        }

        return value.ToString() ?? string.Empty;
    }

    private static object ParsePrimitive(string input)
    {
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (bool.TryParse(input, out var boolValue))
        {
            return boolValue;
        }

        var trimmed = input.Trim();
        if (trimmed.EndsWith("f", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(trimmed[..^1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var floatValue))
        {
            return floatValue;
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        return input;
    }
}
