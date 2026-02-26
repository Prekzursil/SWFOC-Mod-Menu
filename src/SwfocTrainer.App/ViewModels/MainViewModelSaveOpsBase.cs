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

public abstract class MainViewModelSaveOpsBase : MainViewModelQuickActionsBase
{
    protected MainViewModelSaveOpsBase(MainViewModelDependencies dependencies)
        : base(dependencies)
    {
    }

    protected Task BrowseSaveAsync()
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
    protected Task LoadSaveAsync()
        => LoadSaveAsync(clearPatchSummary: true);
    protected async Task LoadSaveAsync(bool clearPatchSummary)
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
    protected async Task EditSaveAsync()
    {
        if (_loadedSave is null)
        {
            return;
        }

        object? value = MainViewModelDiagnostics.ParsePrimitive(SaveEditValue);
        await _saveCodec.EditAsync(_loadedSave, SaveNodePath, value);
        RebuildSaveFieldRows();
        await RefreshDiffAsync();
        ClearPatchPreviewState(clearLoadedPack: false);
        Status = $"Edited save field: {SaveNodePath}";
    }
    protected async Task ValidateSaveAsync()
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
    protected async Task WriteSaveAsync()
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
    protected Task BrowsePatchPackAsync()
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
    protected async Task ExportPatchPackAsync()
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
    protected async Task LoadPatchPackAsync()
    {
        var pack = await _savePatchPackService.LoadPackAsync(SavePatchPackPath);
        SetLoadedPatchPack(pack, SavePatchPackPath);
        SavePatchApplySummary = string.Empty;
        Status = $"Loaded patch pack ({pack.Operations.Count} op(s)).";
    }
    protected async Task PreviewPatchPackAsync()
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
        SavePatchMetadataSummary = MainViewModelDiagnostics.BuildPatchMetadataSummary(_loadedPatchPack);
        SavePatchApplySummary = string.Empty;
        Status = preview.IsCompatible && compatibility.IsCompatible
            ? $"Patch preview ready: {SavePatchOperations.Count} operation(s) would be applied."
            : "Patch preview blocked by compatibility/validation errors.";
    }
    protected bool PreparePatchPreview(string selectedProfileId)
    {
        var variantMessage = ValidateSaveRuntimeVariant(selectedProfileId);
        if (variantMessage is null)
        {
            return true;
        }

        SavePatchCompatibility.Clear();
        SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("error", "save_variant_mismatch", variantMessage));
        SavePatchApplySummary = variantMessage;
        Status = variantMessage;
        return false;
    }
    protected void PopulatePatchPreviewOperations(SavePatchPreview preview)
    {
        SavePatchOperations.Clear();
        foreach (var operation in preview.OperationsToApply)
        {
            SavePatchOperations.Add(new SavePatchOperationViewItem(
                operation.Kind.ToString(),
                operation.FieldPath,
                operation.FieldId,
                operation.ValueType,
                MainViewModelDiagnostics.FormatPatchValue(operation.OldValue),
                MainViewModelDiagnostics.FormatPatchValue(operation.NewValue)));
        }
    }
    protected void PopulatePatchCompatibilityRows(SavePatchCompatibilityResult compatibility, SavePatchPreview preview)
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
    protected void AppendPatchCompatibilityRows(string severity, string reasonCode, IEnumerable<string> messages)
    {
        foreach (var message in messages) { SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem(severity, reasonCode, message)); }
    }
    protected async Task ApplyPatchPackAsync()
    {
        if (_loadedPatchPack is null || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        var variantMessage = ValidateSaveRuntimeVariant(SelectedProfileId);
        if (variantMessage is not null)
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
    protected async Task RestoreBackupAsync()
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
    protected string? ValidateSaveRuntimeVariant(string requestedProfileId)
    {
        var session = _runtime.CurrentSession;
        if (session?.Process.Metadata is null)
        {
            return null;
        }

        if (!session.Process.Metadata.TryGetValue("resolvedVariant", out var runtimeVariant) ||
            string.IsNullOrWhiteSpace(runtimeVariant))
        {
            return null;
        }

        if (requestedProfileId.Equals(UniversalProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (runtimeVariant.Equals(requestedProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"Blocked by runtime/save variant mismatch (reasonCode=save_variant_mismatch): runtime={runtimeVariant}, selected={requestedProfileId}.";
    }
    protected Task RefreshDiffAsync()
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
    protected void RebuildSaveFieldRows()
    {
        SaveFields.Clear();
        if (_loadedSave is null) { return; }

        foreach (var row in FlattenNodes(_loadedSave.Root)) { SaveFields.Add(row); }

        ApplySaveSearch();
    }
    protected IEnumerable<SaveFieldViewItem> FlattenNodes(SaveNode root)
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
    protected override void ApplySaveSearch()
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
    protected void SetLoadedPatchPack(SavePatchPack pack, string path)
    {
        _loadedPatchPack = pack;
        SavePatchPackPath = path;
        SavePatchMetadataSummary =
            $"Patch {pack.Metadata.SchemaVersion} | profile={pack.Metadata.ProfileId} | schema={pack.Metadata.SchemaId} | ops={pack.Operations.Count}";
        ClearPatchPreviewState(clearLoadedPack: false);
        CommandManager.InvalidateRequerySuggested();
    }
    protected void ClearPatchPreviewState(bool clearLoadedPack)
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
    protected void AppendPatchArtifactRows(string? backupPath, string? receiptPath)
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

    protected async Task LoadCatalogAsync()
    {
        if (SelectedProfileId is null) { return; }

        CatalogSummary.Clear();
        var catalog = await _catalog.LoadCatalogAsync(SelectedProfileId);
        foreach (var kv in catalog) { CatalogSummary.Add($"{kv.Key}: {kv.Value.Count}"); }

        Status = $"Catalog loaded for {SelectedProfileId}";
    }

    protected async Task DeployHelperAsync()
    {
        if (SelectedProfileId is null) { return; }

        var path = await _helper.DeployAsync(SelectedProfileId);
        Status = $"Helper deployed to: {path}";
    }

    protected async Task VerifyHelperAsync()
    {
        if (SelectedProfileId is null) { return; }

        var ok = await _helper.VerifyAsync(SelectedProfileId);
        Status = ok ? "Helper verification passed" : "Helper verification failed";
    }

    protected async Task CheckUpdatesAsync()
    {
        Updates.Clear();
        var updates = await _updates.CheckForUpdatesAsync();
        foreach (var profile in updates) { Updates.Add(profile); }

        Status = updates.Count > 0 ? $"Updates available for {updates.Count} profile(s)" : "No profile updates";
    }

    protected async Task InstallUpdateAsync()
    {
        if (SelectedProfileId is null) { return; }
        var result = await _updates.InstallProfileTransactionalAsync(SelectedProfileId);
        if (!result.Succeeded)
        {
            Status = $"Profile update failed: {result.Message}";
            OpsArtifactSummary = $"install failed ({result.ReasonCode ?? UnknownValue})";
            return;
        }

        Status = $"Installed profile update: {result.InstalledPath}";
        var receiptPart = string.IsNullOrWhiteSpace(result.ReceiptPath) ? "no receipt" : result.ReceiptPath;
        var backupPart = string.IsNullOrWhiteSpace(result.BackupPath) ? "no backup" : result.BackupPath;
        OpsArtifactSummary = $"install receipt: {receiptPart} | backup: {backupPart}";
    }

    protected async Task RollbackProfileUpdateAsync()
    {
        if (SelectedProfileId is null) { return; }
        var rollback = await _updates.RollbackLastInstallAsync(SelectedProfileId);
        if (!rollback.Restored)
        {
            Status = $"Rollback failed: {rollback.Message}";
            OpsArtifactSummary = $"rollback failed ({rollback.ReasonCode ?? UnknownValue})";
            return;
        }

        Status = rollback.Message;
        OpsArtifactSummary = $"rollback source: {rollback.BackupPath ?? "n/a"}";
    }

    protected async Task ScaffoldModProfileAsync()
    {
        var launchSamples = OnboardingLaunchSample
            .Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new ModLaunchSample(ProcessName: null, ProcessPath: null, CommandLine: line))
            .ToArray();

        var request = new ModOnboardingRequest(
            DraftProfileId: OnboardingDraftProfileId,
            DisplayName: OnboardingDisplayName,
            BaseProfileId: string.IsNullOrWhiteSpace(OnboardingBaseProfileId) ? BaseSwfocProfileId : OnboardingBaseProfileId,
            LaunchSamples: launchSamples,
            ProfileAliases: new[] { OnboardingDraftProfileId, OnboardingDisplayName },
            NamespaceRoot: OnboardingNamespaceRoot,
            Notes: "Generated by Mod Compatibility Studio");

        var result = await _modOnboarding.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        var warnings = result.Warnings.Count == 0 ? "none" : string.Join("; ", result.Warnings);
        OnboardingSummary = $"draft={result.ProfileId} output={result.OutputPath} workshop=[{string.Join(',', result.InferredWorkshopIds)}] hints=[{string.Join(',', result.InferredPathHints)}] warnings={warnings}";
        Status = $"Draft profile scaffolded: {result.ProfileId}";
    }

    protected async Task ExportCalibrationArtifactAsync()
    {
        var profileId = SelectedProfileId ?? OnboardingDraftProfileId;
        var outputDir = Path.Combine(SupportBundleOutputDirectory, "calibration");
        Directory.CreateDirectory(outputDir);

        var request = new ModCalibrationArtifactRequest(
            ProfileId: profileId,
            OutputDirectory: outputDir,
            Session: _runtime.CurrentSession,
            OperatorNotes: CalibrationNotes);

        var result = await _modCalibration.ExportCalibrationArtifactAsync(request);
        OpsArtifactSummary = result.ArtifactPath;
        Status = result.Succeeded
            ? $"Calibration artifact exported: {result.ArtifactPath}"
            : "Calibration artifact export failed.";
    }

    protected async Task BuildCompatibilityReportAsync()
    {
        var profileId = SelectedProfileId ?? OnboardingDraftProfileId;
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId);
        var report = await _modCalibration.BuildCompatibilityReportAsync(profile, _runtime.CurrentSession);

        ModCompatibilityRows.Clear();
        foreach (var action in report.Actions) { ModCompatibilityRows.Add($"{action.ActionId} | {action.State} | {action.ReasonCode} | {action.Confidence:0.00}"); }

        ModCompatibilitySummary = $"promotionReady={report.PromotionReady} dependency={report.DependencyStatus} unresolvedCritical={report.UnresolvedCriticalSymbols}";
        Status = $"Compatibility report generated for {profileId}";
    }

    protected async Task ExportSupportBundleAsync()
    {
        var result = await _supportBundles.ExportAsync(new SupportBundleRequest(OutputDirectory: SupportBundleOutputDirectory, ProfileId: SelectedProfileId, Notes: "Exported from Profiles & Updates tab"));

        OpsArtifactSummary = result.BundlePath;
        Status = result.Succeeded
            ? $"Support bundle exported: {result.BundlePath}"
            : "Support bundle export failed.";
    }

    protected async Task ExportTelemetrySnapshotAsync()
    {
        var telemetryDir = Path.Combine(SupportBundleOutputDirectory, "telemetry");
        Directory.CreateDirectory(telemetryDir);
        var path = await _telemetry.ExportSnapshotAsync(telemetryDir);
        OpsArtifactSummary = path;
        Status = $"Telemetry snapshot exported: {path}";
    }
}
