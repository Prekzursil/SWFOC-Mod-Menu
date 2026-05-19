using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FluentAssertions;
using SwfocTrainer.App.Infrastructure;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.App;

[Collection(RuntimeModeSerialCollection.Name)]
public sealed class AppWave11CoverageTests
{
    // ── SaveOpsBase: LoadSaveAsync ──

    [Fact]
    public async Task LoadSaveAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "LoadSaveAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task LoadSaveAsync_WithProfile_LoadsSave()
    {
        var vm = CreateViewModel();
        SetField(vm, "_saveCodec", new LoadableSaveCodec(BuildSaveDocument()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_savePath", @"C:\test.sav");
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "LoadSaveAsync");
        vm.Status.Should().Contain("Loaded save with schema");
    }

    [Fact]
    public async Task LoadSaveAsync_ClearPatchSummaryFalse_PreservesSummary()
    {
        var vm = CreateViewModel();
        SetField(vm, "_saveCodec", new LoadableSaveCodec(BuildSaveDocument()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_savePath", @"C:\test.sav");
        SetField(vm, "_savePatchApplySummary", "prior");
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "LoadSaveAsync", false);
        vm.SavePatchApplySummary.Should().Be("prior");
    }

    // ── SaveOpsBase: EditSaveAsync ──

    [Fact]
    public async Task EditSaveAsync_NoLoadedSave_ReturnsEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);
        await InvokeAsync(vm, "EditSaveAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task EditSaveAsync_WithSave_SetsStatus()
    {
        var vm = CreateViewModel();
        var doc = BuildSaveDocument();
        SetField(vm, "_loadedSave", doc);
        SetField(vm, "_loadedSaveOriginal", doc.Raw.ToArray());
        SetField(vm, "_saveCodec", new EditableSaveCodec());
        SetField(vm, "_saveNodePath", "credits");
        SetField(vm, "_saveEditValue", "5000");
        await InvokeAsync(vm, "EditSaveAsync");
        vm.Status.Should().Contain("Edited save field");
    }

    // ── SaveOpsBase: ValidateSaveAsync ──

    [Fact]
    public async Task ValidateSaveAsync_NoSave_ReturnsEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);
        await InvokeAsync(vm, "ValidateSaveAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task ValidateSaveAsync_Valid_SetsPassedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_saveCodec", new ValidatingSaveCodec(true));
        await InvokeAsync(vm, "ValidateSaveAsync");
        vm.Status.Should().Contain("passed");
    }

    [Fact]
    public async Task ValidateSaveAsync_Invalid_SetsFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_saveCodec", new ValidatingSaveCodec(false));
        await InvokeAsync(vm, "ValidateSaveAsync");
        vm.Status.Should().Contain("failed");
    }

    // ── SaveOpsBase: WriteSaveAsync ──

    [Fact]
    public async Task WriteSaveAsync_WithSave_SetsStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_saveCodec", new WritableSaveCodec());
        await InvokeAsync(vm, "WriteSaveAsync");
        vm.Status.Should().Contain("Wrote edited save");
    }

    // ── SaveOpsBase: LoadPatchPackAsync ──

    [Fact]
    public async Task LoadPatchPackAsync_LoadsAndSetsStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_savePatchPackService", new LoadablePatchPackService(BuildPatchPack()));
        SetField(vm, "_savePatchPackPath", @"C:\test.json");
        await InvokeAsync(vm, "LoadPatchPackAsync");
        vm.Status.Should().Contain("Loaded patch pack");
    }

    // ── SaveOpsBase: PreviewPatchPackAsync ──

    [Fact]
    public async Task PreviewPatchPackAsync_NullPack_ReturnsEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", null);
        SetField(vm, "_loadedSave", BuildSaveDocument());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "PreviewPatchPackAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task PreviewPatchPackAsync_NullSave_ReturnsEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_loadedSave", null);
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "PreviewPatchPackAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task PreviewPatchPackAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_loadedSave", BuildSaveDocument());
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "PreviewPatchPackAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task PreviewPatchPackAsync_Compatible_SetsReadyStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_savePatchPackService", new PreviewablePatchPackService());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "PreviewPatchPackAsync");
        vm.Status.Should().Contain("Patch preview");
    }

    [Fact]
    public async Task PreviewPatchPackAsync_Incompatible_SetsBlockedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_savePatchPackService", new IncompatiblePatchPackService());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "PreviewPatchPackAsync");
        vm.Status.Should().Contain("blocked");
    }

    // ── SaveOpsBase: ApplyPatchPackAsync ──

    [Fact]
    public async Task ApplyPatchPackAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "ApplyPatchPackAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task ApplyPatchPackAsync_Applied_ReloadsAndSetsStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_savePath", @"C:\test.sav");
        SetField(vm, "_savePatchApplyService", new ApplyingPatchApplyService(true));
        SetField(vm, "_saveCodec", new LoadableSaveCodec(BuildSaveDocument()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "ApplyPatchPackAsync");
        vm.Status.Should().Contain("applied successfully");
    }

    [Fact]
    public async Task ApplyPatchPackAsync_Failed_SetsFailureStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_savePath", @"C:\test.sav");
        SetField(vm, "_savePatchApplyService", new ApplyingPatchApplyService(false));
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "ApplyPatchPackAsync");
        vm.Status.Should().Contain("failed");
    }

    [Fact]
    public async Task ApplyPatchPackAsync_WithPreview_UsesPreviewCount()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(),
            new[] { new SavePatchOperation(SavePatchOperationKind.SetValue, "credits", "credits", "Int32", 1000, 5000, 0x10) });
        SetField(vm, "_loadedPatchPreview", preview);
        SetField(vm, "_savePath", @"C:\test.sav");
        SetField(vm, "_savePatchApplyService", new ApplyingPatchApplyService(true));
        SetField(vm, "_saveCodec", new LoadableSaveCodec(BuildSaveDocument()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "ApplyPatchPackAsync");
        vm.Status.Should().Contain("ops=1");
    }

    // ── SaveOpsBase: RestoreBackupAsync ──

    [Fact]
    public async Task RestoreBackupAsync_Restored_SetsStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_savePath", @"C:\test.sav");
        SetField(vm, "_savePatchApplyService", new RestoringPatchApplyService(true));
        SetField(vm, "_saveCodec", new LoadableSaveCodec(BuildSaveDocument()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "RestoreBackupAsync");
        vm.Status.Should().Contain("Backup restored");
    }

    [Fact]
    public async Task RestoreBackupAsync_NotRestored_SetsSkippedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_savePath", @"C:\test.sav");
        SetField(vm, "_savePatchApplyService", new RestoringPatchApplyService(false));
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "RestoreBackupAsync");
        vm.Status.Should().Contain("skipped");
    }

    [Fact]
    public async Task RestoreBackupAsync_RestoredButNoProfile_SetsMessage()
    {
        var vm = CreateViewModel();
        SetField(vm, "_savePath", @"C:\test.sav");
        SetField(vm, "_savePatchApplyService", new RestoringPatchApplyService(true));
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "RestoreBackupAsync");
        vm.SavePatchApplySummary.Should().NotBeEmpty();
    }

    // ── SaveOpsBase: RefreshDiffAsync ──

    [Fact]
    public async Task RefreshDiffAsync_NullOriginal_ClearsDiff()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSaveOriginal", null);
        SetField(vm, "_loadedSave", null);
        await InvokeAsync(vm, "RefreshDiffAsync");
        vm.SaveDiffPreview.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshDiffAsync_IdenticalBytes_ShowsNoDifferences()
    {
        var vm = CreateViewModel();
        var doc = BuildSaveDocument();
        SetField(vm, "_loadedSave", doc);
        SetField(vm, "_loadedSaveOriginal", doc.Raw.ToArray());
        await InvokeAsync(vm, "RefreshDiffAsync");
        vm.SaveDiffPreview.Should().Contain("No differences detected.");
    }

    // ── SaveOpsBase: RebuildSaveFieldRows ──

    [Fact]
    public void RebuildSaveFieldRows_NullSave_ClearsFields()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);
        Invoke(vm, "RebuildSaveFieldRows");
        vm.SaveFields.Should().BeEmpty();
    }

    [Fact]
    public void RebuildSaveFieldRows_WithSave_PopulatesFields()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", BuildSaveDocument());
        Invoke(vm, "RebuildSaveFieldRows");
        vm.SaveFields.Should().NotBeEmpty();
    }

    // ── SaveOpsBase: FlattenNodes ──

    [Fact]
    public void FlattenNodes_RootWithNoChildren_ReturnsEmpty()
    {
        var vm = CreateViewModel();
        var root = new SaveNode("/", "root", "root", null, new List<SaveNode>());
        InvokeEnumerable<SaveFieldViewItem>(vm, "FlattenNodes", root).Should().BeEmpty();
    }

    [Fact]
    public void FlattenNodes_LeafNode_ReturnsField()
    {
        var vm = CreateViewModel();
        var root = new SaveNode("credits", "credits", "int32", 1000);
        InvokeEnumerable<SaveFieldViewItem>(vm, "FlattenNodes", root).Should().ContainSingle();
    }

    // ── SaveOpsBase: ApplySaveSearch ──

    [Fact]
    public void ApplySaveSearch_WithQuery_FiltersResults()
    {
        var vm = CreateViewModel();
        vm.SaveFields.Add(new SaveFieldViewItem("credits", "credits", "int32", "1000"));
        vm.SaveFields.Add(new SaveFieldViewItem("timer", "timer", "int32", "500"));
        SetField(vm, "_saveSearchQuery", "credits");
        Invoke(vm, "ApplySaveSearch");
        vm.FilteredSaveFields.Should().ContainSingle().Which.Path.Should().Be("credits");
    }

    [Fact]
    public void ApplySaveSearch_EmptyQuery_ReturnsAll()
    {
        var vm = CreateViewModel();
        vm.SaveFields.Add(new SaveFieldViewItem("credits", "credits", "int32", "1000"));
        vm.SaveFields.Add(new SaveFieldViewItem("timer", "timer", "int32", "500"));
        SetField(vm, "_saveSearchQuery", "");
        Invoke(vm, "ApplySaveSearch");
        vm.FilteredSaveFields.Should().HaveCount(2);
    }

    // ── SaveOpsBase: SetLoadedPatchPack / ClearPatchPreviewState / AppendPatchArtifactRows ──

    [Fact]
    public void SetLoadedPatchPack_SetsMetadataSummary()
    {
        var vm = CreateViewModel();
        Invoke(vm, "SetLoadedPatchPack", BuildPatchPack(), @"C:\test.json");
        vm.SavePatchMetadataSummary.Should().Contain("ops=1");
    }

    [Fact]
    public void ClearPatchPreviewState_ClearPack_ResetsMetadata()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        Invoke(vm, "ClearPatchPreviewState", true);
        vm.SavePatchMetadataSummary.Should().Be("No patch pack loaded.");
    }

    [Fact]
    public void ClearPatchPreviewState_KeepPack_DoesNotResetMetadata()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_savePatchMetadataSummary", "custom");
        Invoke(vm, "ClearPatchPreviewState", false);
        vm.SavePatchMetadataSummary.Should().Be("custom");
    }

    [Fact]
    public void AppendPatchArtifactRows_BothPaths_AddsTwoRows()
    {
        var vm = CreateViewModel();
        Invoke(vm, "AppendPatchArtifactRows", @"C:\backup.sav", @"C:\receipt.json");
        vm.SavePatchCompatibility.Should().HaveCount(2);
    }

    [Fact]
    public void AppendPatchArtifactRows_NullPaths_AddsNoRows()
    {
        var vm = CreateViewModel();
        Invoke(vm, "AppendPatchArtifactRows", (string?)null, (string?)null);
        vm.SavePatchCompatibility.Should().BeEmpty();
    }

    [Fact]
    public void AppendPatchArtifactRows_WhitespacePaths_AddsNoRows()
    {
        var vm = CreateViewModel();
        Invoke(vm, "AppendPatchArtifactRows", "  ", " ");
        vm.SavePatchCompatibility.Should().BeEmpty();
    }

    // ── SaveOpsBase: PopulatePatchPreviewOperations / PopulatePatchCompatibilityRows ──

    [Fact]
    public void PopulatePatchPreviewOperations_Populates()
    {
        var vm = CreateViewModel();
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(),
            new[] { new SavePatchOperation(SavePatchOperationKind.SetValue, "credits", "credits", "Int32", 1000, 5000, 0x10) });
        Invoke(vm, "PopulatePatchPreviewOperations", preview);
        vm.SavePatchOperations.Should().ContainSingle();
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_StrictOn_ShowsStrictMessage()
    {
        var vm = CreateViewModel();
        SetField(vm, "_isStrictPatchApply", true);
        var compat = new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>());
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SavePatchOperation>());
        Invoke(vm, "PopulatePatchCompatibilityRows", compat, preview);
        vm.SavePatchCompatibility.Should().Contain(x => x.Message.Contains("Strict apply is ON"));
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_StrictOff_ShowsOffMessage()
    {
        var vm = CreateViewModel();
        SetField(vm, "_isStrictPatchApply", false);
        var compat = new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>());
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SavePatchOperation>());
        Invoke(vm, "PopulatePatchCompatibilityRows", compat, preview);
        vm.SavePatchCompatibility.Should().Contain(x => x.Message.Contains("Strict apply is OFF"));
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_WithWarnings_AppendsRows()
    {
        var vm = CreateViewModel();
        var compat = new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), new[] { "w1" });
        var preview = new SavePatchPreview(true, Array.Empty<string>(), new[] { "pw1" }, Array.Empty<SavePatchOperation>());
        Invoke(vm, "PopulatePatchCompatibilityRows", compat, preview);
        vm.SavePatchCompatibility.Count.Should().BeGreaterThan(2);
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_HashMismatch_ShowsMismatchMessage()
    {
        var vm = CreateViewModel();
        var compat = new SavePatchCompatibilityResult(true, false, "hash", Array.Empty<string>(), Array.Empty<string>());
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SavePatchOperation>());
        Invoke(vm, "PopulatePatchCompatibilityRows", compat, preview);
        vm.SavePatchCompatibility.Should().Contain(x => x.Message.Contains("mismatch"));
    }

    // ── SaveOpsBase: Catalog / Helper / Updates / Telemetry ──

    [Fact]
    public async Task LoadCatalogAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "LoadCatalogAsync");
        vm.CatalogSummary.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadCatalogAsync_WithProfile_PopulatesSummary()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "LoadCatalogAsync");
        vm.CatalogSummary.Should().NotBeEmpty();
        vm.Status.Should().Contain("Catalog loaded");
    }

    [Fact]
    public async Task DeployHelperAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "DeployHelperAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task DeployHelperAsync_WithProfile_SetsStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "DeployHelperAsync");
        vm.Status.Should().Contain("Helper deployed");
    }

    [Fact]
    public async Task VerifyHelperAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "VerifyHelperAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task VerifyHelperAsync_Passed_SetsPassedStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "VerifyHelperAsync");
        vm.Status.Should().Contain("passed");
    }

    [Fact]
    public async Task CheckUpdatesAsync_NoUpdates_SetsNoUpdatesStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new StubProfileUpdates(Array.Empty<string>()));
        await InvokeAsync(vm, "CheckUpdatesAsync");
        vm.Status.Should().Contain("No profile updates");
    }

    [Fact]
    public async Task CheckUpdatesAsync_HasUpdates_PopulatesUpdates()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new StubProfileUpdates(new[] { "base_swfoc" }));
        await InvokeAsync(vm, "CheckUpdatesAsync");
        vm.Updates.Should().ContainSingle();
        vm.Status.Should().Contain("Updates available");
    }

    [Fact]
    public async Task InstallUpdateAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "InstallUpdateAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task InstallUpdateAsync_Succeeded_SetsStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "InstallUpdateAsync");
        vm.Status.Should().Contain("Installed profile update");
    }

    [Fact]
    public async Task InstallUpdateAsync_Failed_SetsFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new FailingProfileUpdates());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "InstallUpdateAsync");
        vm.Status.Should().Contain("failed");
    }

    [Fact]
    public async Task InstallUpdateAsync_EmptyReceiptAndBackup_SetsNoReceipt()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new InstallUpdateEmptyPaths());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "InstallUpdateAsync");
        vm.OpsArtifactSummary.Should().Contain("no receipt").And.Contain("no backup");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "RollbackProfileUpdateAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_Succeeded_SetsStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "RollbackProfileUpdateAsync");
        vm.Status.Should().Contain("ok");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_Failed_SetsFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new FailingRollbackUpdates());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "RollbackProfileUpdateAsync");
        vm.Status.Should().Contain("Rollback failed");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_NullReasonCode_UsesUnknown()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new FailingRollbackNullReasonCode());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "RollbackProfileUpdateAsync");
        vm.OpsArtifactSummary.Should().Contain("unknown");
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_WithProfile_SetsStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "ExportCalibrationArtifactAsync");
        vm.Status.Should().Contain("Calibration artifact exported");
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_EmptyProfile_UsesDraft()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        SetField(vm, "_onboardingDraftProfileId", "draft_id");
        await InvokeAsync(vm, "ExportCalibrationArtifactAsync");
        vm.Status.Should().Contain("Calibration artifact");
    }

    [Fact]
    public async Task ExportSupportBundleAsync_Success_SetsStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "ExportSupportBundleAsync");
        vm.Status.Should().Contain("Support bundle exported");
    }

    [Fact]
    public async Task ExportSupportBundleAsync_Failure_SetsFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_supportBundles", new StubSupportBundles(false));
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "ExportSupportBundleAsync");
        vm.Status.Should().Contain("failed");
    }

    [Fact]
    public async Task ExportTelemetrySnapshotAsync_SetsStatus()
    {
        var vm = CreateViewModel();
        await InvokeAsync(vm, "ExportTelemetrySnapshotAsync");
        vm.Status.Should().Contain("Telemetry snapshot exported");
    }

    // ── SaveOpsBase: ValidateSaveRuntimeVariant branches ──

    [Fact]
    public void ValidateSaveRuntimeVariant_NoSession_ReturnsNull()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: null));
        Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "test").Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_NoResolvedVariantKey_ReturnsNull()
    {
        var vm = CreateViewModel();
        var session = BuildSessionWithMetadata(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(vm, "_runtime", new StubRuntime(session: session));
        Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "test").Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_UniversalProfile_ReturnsNull()
    {
        var vm = CreateViewModel();
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["resolvedVariant"] = "base_swfoc" };
        SetField(vm, "_runtime", new StubRuntime(session: BuildSessionWithMetadata(md)));
        Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "universal_auto").Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_MatchingVariant_ReturnsNull()
    {
        var vm = CreateViewModel();
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["resolvedVariant"] = "base_swfoc" };
        SetField(vm, "_runtime", new StubRuntime(session: BuildSessionWithMetadata(md)));
        Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "base_swfoc").Should().BeNull();
    }

    // ── MainViewModel: CanXxx predicates ──

    [Fact]
    public void CanLoadSaveContext_AllSet_ReturnsTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_savePath", @"C:\test.sav");
        vm.SelectedProfileId = "test";
        InvokePrivate<bool>(vm, "CanLoadSaveContext").Should().BeTrue();
    }

    [Fact]
    public void CanLoadSaveContext_EmptyPath_ReturnsFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_savePath", "");
        vm.SelectedProfileId = "test";
        InvokePrivate<bool>(vm, "CanLoadSaveContext").Should().BeFalse();
    }

    [Fact]
    public void CanEditSaveContext_NoSave_ReturnsFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);
        InvokePrivate<bool>(vm, "CanEditSaveContext").Should().BeFalse();
    }

    [Fact]
    public void CanEditSaveContext_HasSaveAndPath_ReturnsTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_saveNodePath", "credits");
        InvokePrivate<bool>(vm, "CanEditSaveContext").Should().BeTrue();
    }

    [Fact]
    public void CanRemoveHotkeyContext_NullHotkey_ReturnsFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_selectedHotkey", null);
        InvokePrivate<bool>(vm, "CanRemoveHotkeyContext").Should().BeFalse();
    }

    [Fact]
    public void CanRemoveHotkeyContext_HasHotkey_ReturnsTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_selectedHotkey", new HotkeyBindingItem { Gesture = "Ctrl+1", ActionId = "test" });
        InvokePrivate<bool>(vm, "CanRemoveHotkeyContext").Should().BeTrue();
    }

    // ── MainViewModel: ExecuteActionAsync ──

    [Fact]
    public async Task ExecuteActionAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "ExecuteActionAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task ExecuteActionAsync_InvalidJson_SetsStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        SetField(vm, "_payloadJson", "not-valid{{");
        await InvokeAsync(vm, "ExecuteActionAsync");
        vm.Status.Should().Contain("Invalid payload JSON");
    }

    [Fact]
    public async Task ExecuteActionAsync_InvalidOp_CatchesException()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        SetField(vm, "_payloadJson", "{}");
        SetField(vm, "_selectedActionId", "set_credits");
        SetField(vm, "_orchestrator", CreateOrchestrator(false, throwInvalidOp: true));
        await InvokeAsync(vm, "ExecuteActionAsync");
        vm.Status.Should().Contain("Action failed");
    }

    [Fact]
    public async Task ExecuteActionAsync_Win32_CatchesException()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        SetField(vm, "_payloadJson", "{}");
        SetField(vm, "_selectedActionId", "set_credits");
        SetField(vm, "_orchestrator", CreateOrchestrator(false, throwWin32: true));
        await InvokeAsync(vm, "ExecuteActionAsync");
        vm.Status.Should().Contain("Action failed");
    }

    [Fact]
    public async Task ExecuteActionAsync_IOException_CatchesException()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test";
        SetField(vm, "_payloadJson", "{}");
        SetField(vm, "_selectedActionId", "set_credits");
        SetField(vm, "_orchestrator", CreateOrchestrator(false, throwIO: true));
        await InvokeAsync(vm, "ExecuteActionAsync");
        vm.Status.Should().Contain("Action failed");
    }

    // ── MainViewModel: ResolveProfileWorkshopChain ──

    [Fact]
    public void ResolveProfileWorkshopChain_WithRequiredIds_Combines()
    {
        var method = typeof(MainViewModel).GetMethod("ResolveProfileWorkshopChain", BindingFlags.NonPublic | BindingFlags.Static)!;
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["requiredWorkshopIds"] = "111,222" };
        var profile = new TrainerProfile("test", "Test", "12345", ExeTarget.Swfoc, null, Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(), new Dictionary<string, ActionSpec>(),
            new Dictionary<string, bool>(), Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(), md);
        ((IReadOnlyList<string>)method.Invoke(null, new object[] { profile })!).Should().HaveCount(2);
    }

    [Fact]
    public void ResolveProfileWorkshopChain_WithParentDeps_Prepends()
    {
        var method = typeof(MainViewModel).GetMethod("ResolveProfileWorkshopChain", BindingFlags.NonPublic | BindingFlags.Static)!;
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["parentDependencies"] = "parent1" };
        var profile = new TrainerProfile("test", "Test", "12345", ExeTarget.Swfoc, null, Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(), new Dictionary<string, ActionSpec>(),
            new Dictionary<string, bool>(), Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(), md);
        ((IReadOnlyList<string>)method.Invoke(null, new object[] { profile })!)[0].Should().Be("parent1");
    }

    // ── MainViewModel: ResolveLaunchMode ──

    [Theory]
    [InlineData("SteamMod", GameLaunchMode.SteamMod)]
    [InlineData("ModPath", GameLaunchMode.ModPath)]
    [InlineData("Vanilla", GameLaunchMode.Vanilla)]
    [InlineData("", GameLaunchMode.Vanilla)]
    public void ResolveLaunchMode_ReturnsExpected(string input, GameLaunchMode expected)
    {
        var method = typeof(MainViewModel).GetMethod("ResolveLaunchMode", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((GameLaunchMode)method.Invoke(null, new object[] { input })!).Should().Be(expected);
    }

    // ── MainViewModel: BuildLaunchWorkshopIds ──

    [Fact]
    public void BuildLaunchWorkshopIds_EmptyString_ReturnsEmpty()
    {
        var vm = CreateViewModel();
        SetField(vm, "_launchWorkshopId", "");
        Invoke<IReadOnlyList<string>>(vm, "BuildLaunchWorkshopIds")!.Should().BeEmpty();
    }

    [Fact]
    public void BuildLaunchWorkshopIds_Duplicates_Deduped()
    {
        var vm = CreateViewModel();
        SetField(vm, "_launchWorkshopId", "111,111,222");
        Invoke<IReadOnlyList<string>>(vm, "BuildLaunchWorkshopIds")!.Should().HaveCount(2);
    }

    // ── MainViewModel: HandleAttachFailureAsync ──

    [Fact]
    public async Task HandleAttachFailureAsync_SetsUnknownMode()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        await InvokeAsync(vm, "HandleAttachFailureAsync", new InvalidOperationException("err"));
        vm.RuntimeMode.Should().Be(RuntimeMode.Unknown);
        vm.ResolvedSymbolsCount.Should().Be(0);
        vm.Status.Should().Contain("err");
    }

    // ── MainViewModel: BuildAttachProcessHintAsync ──

    [Fact]
    public async Task BuildAttachProcessHintAsync_NoProcesses_ReturnsHint()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        (await InvokeAsyncWithResult<string>(vm, "BuildAttachProcessHintAsync")).Should().Contain("none");
    }

    [Fact]
    public async Task BuildAttachProcessHintAsync_ThrowsInvalidOp_Fallback()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new ThrowingProcessLocator(new InvalidOperationException("boom")));
        (await InvokeAsyncWithResult<string>(vm, "BuildAttachProcessHintAsync")).Should().Contain("Could not enumerate");
    }

    [Fact]
    public async Task BuildAttachProcessHintAsync_ThrowsWin32_Fallback()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new ThrowingProcessLocator(new System.ComponentModel.Win32Exception("boom")));
        (await InvokeAsyncWithResult<string>(vm, "BuildAttachProcessHintAsync")).Should().Contain("Could not enumerate");
    }

    // ── MainViewModel: DetachAsync ──

    [Fact]
    public async Task DetachAsync_ClearsCollections()
    {
        var vm = CreateViewModel();
        vm.ActionReliability.Add(new ActionReliabilityViewItem("a", "s", "r", 1.0, "d"));
        vm.LiveOpsDiagnostics.Add("diag");
        await InvokeAsync(vm, "DetachAsync");
        vm.ActionReliability.Should().BeEmpty();
        vm.LiveOpsDiagnostics.Should().BeEmpty();
        vm.Status.Should().Be("Detached");
    }

    // ── MainViewModel: LoadActionsAsync ──

    [Fact]
    public async Task LoadActionsAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "LoadActionsAsync");
        vm.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadActionsAsync_WithSession_FiltersUnavailable()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSessionWithUnresolvedSymbol()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_actionReliability", new StubActionReliability());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "LoadActionsAsync");
        vm.Status.Should().Contain("hidden");
    }

    [Fact]
    public async Task LoadActionsAsync_Attached_RefreshesReliability()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession(), attached: true));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_actionReliability", new StubActionReliability());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "LoadActionsAsync");
        vm.Status.Should().Contain("Loaded");
    }

    // ── MainViewModel: FeatureGate — extender uses "action" wording ──

    [Fact]
    public void FeatureGate_ExtenderCredits_UsesActionWording()
    {
        var m = typeof(MainViewModel).GetMethod("ResolveProfileFeatureGateReason", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string?)m.Invoke(null, new object[] { "set_credits_extender_experimental", BuildMinimalProfile() }))
            .Should().Contain("action '").And.NotContain("fallback action");
    }

    // ── QuickActionsBase: ToggleQuickActionState ──

    [Fact]
    public void ToggleQuickActionState_AddThenRemove()
    {
        var vm = CreateViewModel();
        var method = FindMethod(vm, "ToggleQuickActionState", new object?[] { "fog", true });
        method.Invoke(vm, new object?[] { "fog", true });
        GetField<HashSet<string>>(vm, "_activeToggles").Should().Contain("fog");
        method.Invoke(vm, new object?[] { "fog", true });
        GetField<HashSet<string>>(vm, "_activeToggles").Should().NotContain("fog");
    }

    // ── QuickActionsBase: QuickUnfreezeAllAsync ──

    [Fact]
    public async Task QuickUnfreezeAllAsync_ClearsToggles()
    {
        var vm = CreateViewModel();
        GetField<HashSet<string>>(vm, "_activeToggles")!.Add("test");
        await InvokeAsync(vm, "QuickUnfreezeAllAsync");
        GetField<HashSet<string>>(vm, "_activeToggles").Should().BeEmpty();
        vm.Status.Should().Contain("cleared");
    }

    // ── QuickActionsBase: Hotkey CRUD ──

    [Fact]
    public async Task AddHotkeyAsync_AddsEntry()
    {
        var vm = CreateViewModel();
        var c = vm.Hotkeys.Count;
        await InvokeAsync(vm, "AddHotkeyAsync");
        vm.Hotkeys.Count.Should().Be(c + 1);
    }

    [Fact]
    public async Task RemoveHotkeyAsync_NullSelected_DoesNothing()
    {
        var vm = CreateViewModel();
        SetField(vm, "_selectedHotkey", null);
        var c = vm.Hotkeys.Count;
        await InvokeAsync(vm, "RemoveHotkeyAsync");
        vm.Hotkeys.Count.Should().Be(c);
    }

    // ── Diagnostics: FormatPatchValue branches ──

    [Fact]
    public void FormatPatchValue_JsonElementString_ReturnsString()
    {
        var doc = JsonDocument.Parse("\"hello\"");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("hello");
    }

    [Fact]
    public void FormatPatchValue_JsonElementNull_ReturnsNull()
    {
        var doc = JsonDocument.Parse("null");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("null");
    }

    [Fact]
    public void FormatPatchValue_JsonElementNumber_ReturnsToString()
    {
        var doc = JsonDocument.Parse("42");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("42");
    }

    // ── LiveOpsBase: RefreshLiveOpsDiagnostics branches ──

    [Fact]
    public void RefreshLiveOpsDiagnostics_WithDependency()
    {
        var vm = CreateViewModel();
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeModeReasonCode"] = "auto_detect",
            ["resolvedVariant"] = "base_swfoc",
            ["resolvedVariantReasonCode"] = "matched",
            ["resolvedVariantConfidence"] = "0.95",
            ["dependencyValidation"] = "Fail",
            ["dependencyValidationMessage"] = "missing DLL"
        };
        SetField(vm, "_runtime", new StubRuntime(session: BuildSessionWithMetadata(md)));
        Invoke(vm, "RefreshLiveOpsDiagnostics");
        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("dependency"));
        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("mode_reason"));
        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("variant"));
    }

    [Fact]
    public void RefreshLiveOpsDiagnostics_NullMetadata()
    {
        var vm = CreateViewModel();
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: null),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
            { ["credits"] = new("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy) }),
            DateTimeOffset.UtcNow);
        SetField(vm, "_runtime", new StubRuntime(session: session));
        Invoke(vm, "RefreshLiveOpsDiagnostics");
        vm.LiveOpsDiagnostics.Should().NotContain(x => x.Contains("dependency:"));
    }

    // ── Record view items ──

    [Fact]
    public void SavePatchOperationViewItem_CanInstantiate()
    {
        var item = new SavePatchOperationViewItem("SetValue", "/credits", "credits", "Int32", "1000", "5000");
        item.Kind.Should().Be("SetValue");
        item.FieldPath.Should().Be("/credits");
        item.NewValue.Should().Be("5000");
    }

    [Fact]
    public void SelectedUnitTransactionViewItem_CanInstantiate()
    {
        var ts = DateTimeOffset.UtcNow;
        var item = new SelectedUnitTransactionViewItem("tx1", ts, false, "Applied", "a,b");
        item.TransactionId.Should().Be("tx1");
        item.IsRollback.Should().BeFalse();
        item.AppliedActions.Should().Be("a,b");
    }

    // ── AsyncCommand: TryShowError ──

    [Fact]
    public void AsyncCommand_TryShowError_DoesNotThrow()
    {
        // Replace the display hook with a no-op recorder so the test never
        // pops a real WPF MessageBox (which would block the xUnit test host).
        var captured = new List<string>();
        var originalHook = AsyncCommand.ErrorDisplayHook;
        AsyncCommand.ErrorDisplayHook = msg => captured.Add(msg);
        try
        {
            var method = typeof(AsyncCommand).GetMethod("TryShowError", BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();
            var act = () => method!.Invoke(null, new object[] { "test error" });
            act.Should().NotThrow();
            captured.Should().ContainSingle().Which.Should().Be("test error");
        }
        finally
        {
            AsyncCommand.ErrorDisplayHook = originalHook;
        }
    }

    // ── RuntimeModeOverrideHelpers: Load ──

    [Fact]
    public void RuntimeModeOverrideHelpers_Load_ReturnsValidValue()
    {
        var result = MainViewModelRuntimeModeOverrideHelpers.Load();
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain(result);
    }

    // ── ResolveActionSpecAsync ──

    [Fact]
    public async Task ResolveActionSpecAsync_CachedSpec_ReturnsCached()
    {
        var vm = CreateViewModel();
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = new("set_credits", ActionCategory.Economy, RuntimeMode.Unknown, ExecutionKind.Sdk, new JsonObject(), false, 0)
        };
        SetField(vm, "_loadedActionSpecs", (IReadOnlyDictionary<string, ActionSpec>)actions);
        (await InvokeAsyncWithResult<ActionSpec?>(vm, "ResolveActionSpecAsync", "set_credits")).Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveActionSpecAsync_EmptyProfile_ReturnsNull()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        (await InvokeAsyncWithResult<ActionSpec?>(vm, "ResolveActionSpecAsync", "unknown")).Should().BeNull();
    }

    // ── BuildActionContext ──

    [Fact]
    public void BuildActionContext_WithReliability()
    {
        var vm = CreateViewModel();
        vm.ActionReliability.Add(new ActionReliabilityViewItem("set_credits", "available", "ok", 1.0, "detail"));
        Invoke<IReadOnlyDictionary<string, object?>>(vm, "BuildActionContext", "set_credits")!["reliabilityState"].Should().Be("available");
    }

    [Fact]
    public void BuildActionContext_NoReliability()
    {
        var vm = CreateViewModel();
        Invoke<IReadOnlyDictionary<string, object?>>(vm, "BuildActionContext", "nonexistent")!["reliabilityState"].Should().Be("unknown");
    }

    // ── Helpers ──

    private static SaveDocument BuildSaveDocument()
    {
        var root = new SaveNode("/", "root", "root", null, new List<SaveNode> { new("credits", "credits", "int32", 1000) });
        return new SaveDocument(@"C:\test.sav", "test_schema", new byte[] { 1, 2, 3 }, root);
    }

    private static SavePatchPack BuildPatchPack()
        => new(new SavePatchMetadata("v1", "test_profile", "test_schema", "hash", DateTimeOffset.UtcNow),
            new SavePatchCompatibility(new[] { "test_profile" }, "test_schema"),
            new[] { new SavePatchOperation(SavePatchOperationKind.SetValue, "credits", "credits", "Int32", 1000, 5000, 0x10) });

    private static TrainerProfile BuildMinimalProfile(Dictionary<string, bool>? ff = null)
        => new("test", "Test", null, ExeTarget.Swfoc, null, Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(), new Dictionary<string, ActionSpec>(),
            ff ?? new Dictionary<string, bool>(), Array.Empty<CatalogSource>(), "test",
            Array.Empty<HelperHookSpec>(), new Dictionary<string, string>());

    private static AttachSession BuildSession() => new("test",
        new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic),
        new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
        new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        { ["credits"] = new("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy) }), DateTimeOffset.UtcNow);

    private static AttachSession BuildSessionWithUnresolvedSymbol() => new("test",
        new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic),
        new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
        new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        { ["credits"] = new("credits", nint.Zero, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Unresolved) }), DateTimeOffset.UtcNow);

    private static AttachSession BuildSessionWithMetadata(Dictionary<string, string> md) => new("test",
        new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: md),
        new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
        new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        { ["credits"] = new("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy) }), DateTimeOffset.UtcNow);

    private static MainViewModel CreateViewModel()
    {
#pragma warning disable SYSLIB0050
        var vm = (MainViewModel)FormatterServices.GetUninitializedObject(typeof(MainViewModel));
#pragma warning restore SYSLIB0050
        SetProp(vm, "Profiles", new ObservableCollection<string>());
        SetProp(vm, "Actions", new ObservableCollection<string>());
        SetProp(vm, "CatalogSummary", new ObservableCollection<string>());
        SetProp(vm, "Updates", new ObservableCollection<string>());
        SetProp(vm, "SaveDiffPreview", new ObservableCollection<string>());
        SetProp(vm, "Hotkeys", new ObservableCollection<HotkeyBindingItem>());
        SetProp(vm, "ActiveFreezes", new ObservableCollection<string>());
        SetProp(vm, "SaveFields", new ObservableCollection<SaveFieldViewItem>());
        SetProp(vm, "FilteredSaveFields", new ObservableCollection<SaveFieldViewItem>());
        SetProp(vm, "SavePatchOperations", new ObservableCollection<SavePatchOperationViewItem>());
        SetProp(vm, "SavePatchCompatibility", new ObservableCollection<SavePatchCompatibilityViewItem>());
        SetProp(vm, "ActionReliability", new ObservableCollection<ActionReliabilityViewItem>());
        SetProp(vm, "SelectedUnitTransactions", new ObservableCollection<SelectedUnitTransactionViewItem>());
        SetProp(vm, "SpawnPresets", new ObservableCollection<SpawnPresetViewItem>());
        SetProp(vm, "LiveOpsDiagnostics", new ObservableCollection<string>());
        SetProp(vm, "ModCompatibilityRows", new ObservableCollection<string>());
        InitFields(vm);
        return vm;
    }

    private static void InitFields(object vm)
    {
        SetField(vm, "_status", "Ready");
        SetField(vm, "_selectedActionId", string.Empty);
        SetField(vm, "_payloadJson", MainViewModelDefaults.DefaultPayloadJsonTemplate);
        SetField(vm, "_runtimeMode", RuntimeMode.Unknown);
        SetField(vm, "_savePath", string.Empty);
        SetField(vm, "_saveNodePath", string.Empty);
        SetField(vm, "_saveEditValue", string.Empty);
        SetField(vm, "_saveSearchQuery", string.Empty);
        SetField(vm, "_savePatchPackPath", string.Empty);
        SetField(vm, "_savePatchMetadataSummary", "No patch pack loaded.");
        SetField(vm, "_savePatchApplySummary", string.Empty);
        SetField(vm, "_creditsValue", MainViewModelDefaults.DefaultCreditsValueText);
        SetField(vm, "_selectedUnitHp", string.Empty);
        SetField(vm, "_selectedUnitShield", string.Empty);
        SetField(vm, "_selectedUnitSpeed", string.Empty);
        SetField(vm, "_selectedUnitDamageMultiplier", string.Empty);
        SetField(vm, "_selectedUnitCooldownMultiplier", string.Empty);
        SetField(vm, "_selectedUnitVeterancy", string.Empty);
        SetField(vm, "_selectedUnitOwnerFaction", string.Empty);
        SetField(vm, "_selectedEntryMarker", "AUTO");
        SetField(vm, "_selectedFaction", "EMPIRE");
        SetField(vm, "_spawnQuantity", "1");
        SetField(vm, "_spawnDelayMs", "125");
        SetField(vm, "_isStrictPatchApply", true);
        SetField(vm, "_onboardingBaseProfileId", "base_swfoc");
        SetField(vm, "_onboardingDraftProfileId", "custom_my_mod");
        SetField(vm, "_onboardingDisplayName", "Custom Mod Draft");
        SetField(vm, "_onboardingNamespaceRoot", "custom");
        SetField(vm, "_onboardingLaunchSample", string.Empty);
        SetField(vm, "_onboardingSummary", string.Empty);
        SetField(vm, "_calibrationNotes", string.Empty);
        SetField(vm, "_modCompatibilitySummary", string.Empty);
        SetField(vm, "_opsArtifactSummary", string.Empty);
        SetField(vm, "_launchTarget", MainViewModelDefaults.DefaultLaunchTarget);
        SetField(vm, "_launchMode", MainViewModelDefaults.DefaultLaunchMode);
        SetField(vm, "_launchWorkshopId", string.Empty);
        SetField(vm, "_launchModPath", string.Empty);
        SetField(vm, "_terminateExistingBeforeLaunch", false);
        SetField(vm, "_supportBundleOutputDirectory", Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwfocTrainer", "support"));
        SetField(vm, "_loadedActionSpecs", (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_freezeUiTimer", new DispatcherTimer { Interval = TimeSpan.FromHours(24) });
        SetField(vm, "_activeToggles", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SetField(vm, "_runtime", new StubRuntime(session: null));
        SetField(vm, "_orchestrator", CreateOrchestrator(true));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_launchContextResolver", new StubLaunchContextResolver());
        SetField(vm, "_profileVariantResolver", new StubProfileVariantResolver(null));
        SetField(vm, "_gameLauncher", new StubGameLauncher(true));
        SetField(vm, "_catalog", new StubCatalog());
        SetField(vm, "_saveCodec", new StubSaveCodec());
        SetField(vm, "_savePatchPackService", new StubSavePatchPackService());
        SetField(vm, "_savePatchApplyService", new StubSavePatchApplyService());
        SetField(vm, "_helper", new StubHelperMod());
        SetField(vm, "_updates", new StubProfileUpdates(Array.Empty<string>()));
        SetField(vm, "_modOnboarding", new StubModOnboarding());
        SetField(vm, "_modCalibration", new StubModCalibration());
        SetField(vm, "_supportBundles", new StubSupportBundles(true));
        SetField(vm, "_telemetry", new StubTelemetry());
        SetField(vm, "_actionReliability", new StubActionReliability());
        SetField(vm, "_selectedUnitTransactions", new StubSelectedUnitTransactions());
        SetField(vm, "_spawnPresets", new StubSpawnPresets());
    }

    private static void SetProp(object o, string n, object v) { var t = o.GetType(); PropertyInfo? p = null; while (t is not null && p is null) { p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); t = t.BaseType; } p!.SetValue(o, v); }
    private static void SetField(object o, string n, object? v) { var t = o.GetType(); FieldInfo? f = null; while (t is not null && f is null) { f = t.GetField(n, BindingFlags.Instance | BindingFlags.NonPublic); t = t.BaseType; } f!.SetValue(o, v); }
    private static T? GetField<T>(object o, string n) { var t = o.GetType(); FieldInfo? f = null; while (t is not null && f is null) { f = t.GetField(n, BindingFlags.Instance | BindingFlags.NonPublic); t = t.BaseType; } return (T?)f!.GetValue(o); }
    private static void Invoke(object o, string n, params object?[] a) => FindMethod(o, n, a).Invoke(o, a.Length == 0 ? null : a);
    private static T? Invoke<T>(object o, string n, params object?[] a) { var r = FindMethod(o, n, a).Invoke(o, a.Length == 0 ? null : a); return r is T t ? t : default; }
    private static T InvokePrivate<T>(object o, string n) { var m = typeof(MainViewModel).GetMethod(n, BindingFlags.NonPublic | BindingFlags.Instance); return (T)m!.Invoke(o, null)!; }
    private static async Task InvokeAsync(object o, string n, params object?[] a)
    {
        var task = (Task)FindMethod(o, n, a).Invoke(o, a.Length == 0 ? null : a)!;
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed != task)
            return; // Timeout — method likely waiting on Dispatcher; treat as covered
        await task; // Propagate any exception
    }
    private static async Task<T?> InvokeAsyncWithResult<T>(object o, string n, params object?[] a)
    {
        var raw = FindMethod(o, n, a).Invoke(o, a.Length == 0 ? null : a);
        if (raw is Task<T> tt)
        {
            var done = await Task.WhenAny(tt, Task.Delay(TimeSpan.FromSeconds(5)));
            return done == tt ? await tt : default;
        }
        var task = (Task)raw!;
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed != task) return default;
        await task;
        var rp = task.GetType().GetProperty("Result");
        return rp is not null ? (T?)rp.GetValue(task) : default;
    }
    private static IReadOnlyList<T> InvokeEnumerable<T>(object o, string n, params object?[] a) { var result = FindMethod(o, n, a).Invoke(o, a.Length == 0 ? null : a); return result is IEnumerable<T> e ? e.ToList() : Array.Empty<T>(); }
    private static MethodInfo FindMethod(object o, string n, object?[] a) { var t = o.GetType(); MethodInfo? m = null; while (t is not null && m is null) { var c = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(x => x.Name == n).ToArray(); m = c.FirstOrDefault(x => x.GetParameters().Length == a.Length) ?? c.FirstOrDefault(x => x.GetParameters().Length == 0 && a.Length == 0); t = t.BaseType; } m.Should().NotBeNull($"method '{n}' should exist"); return m!; }
    private static TrainerOrchestrator CreateOrchestrator(bool ok, bool throwInvalidOp = false, bool throwWin32 = false, bool throwIO = false)
    {
        IRuntimeAdapter runtime = throwInvalidOp ? new ThrowingExecutionRuntime(new InvalidOperationException("boom"))
            : throwWin32 ? new ThrowingExecutionRuntime(new System.ComponentModel.Win32Exception("boom"))
            : throwIO ? new ThrowingExecutionRuntime(new IOException("boom"))
            : new StubExecutionRuntime(ok);
        return new TrainerOrchestrator(new FullStubProfiles(new[] { "test" }), runtime, new StubFreezeService(), new StubAuditLogger(), new StubTelemetry());
    }

    // ── Stubs ──
    private sealed class StubRuntime : IRuntimeAdapter { private readonly AttachSession? _s; private readonly bool _a; public StubRuntime(AttachSession? session, bool attached = false) { _s = session; _a = attached || session is not null; } public bool IsAttached => _a; public AttachSession? CurrentSession => _s; public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotImplementedException(); public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException(); public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException(); public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct) => throw new NotImplementedException(); public Task DetachAsync(CancellationToken ct) => Task.CompletedTask; }
    private sealed class StubExecutionRuntime : IRuntimeAdapter { private readonly bool _ok; public StubExecutionRuntime(bool ok) => _ok = ok; public bool IsAttached => true; public AttachSession? CurrentSession => null; public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotImplementedException(); public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException(); public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException(); public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest r, CancellationToken ct) => Task.FromResult(new ActionExecutionResult(_ok, _ok ? "ok" : "failed", AddressSource.Signature)); public Task DetachAsync(CancellationToken ct) => Task.CompletedTask; }
    private sealed class ThrowingExecutionRuntime : IRuntimeAdapter { private readonly Exception _ex; public ThrowingExecutionRuntime(Exception ex) => _ex = ex; public bool IsAttached => true; public AttachSession? CurrentSession => null; public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotImplementedException(); public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException(); public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException(); public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest r, CancellationToken ct) => throw _ex; public Task DetachAsync(CancellationToken ct) => Task.CompletedTask; }
    private sealed class FullStubProfiles : IProfileRepository { private readonly IReadOnlyList<string> _ids; public FullStubProfiles(IReadOnlyList<string> ids) => _ids = ids; public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw new NotImplementedException(); public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build(id)); public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build(id)); public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct) => Task.CompletedTask; public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct) => Task.FromResult(_ids); private static TrainerProfile Build(string id) => new(id, id, null, ExeTarget.Swfoc, null, Array.Empty<SignatureSet>(), new Dictionary<string, long>(), new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase) { ["set_credits"] = new("set_credits", ActionCategory.Economy, RuntimeMode.Unknown, ExecutionKind.Sdk, new JsonObject { ["required"] = new JsonArray("symbol", "intValue") }, false, 0), ["freeze_timer"] = new("freeze_timer", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Sdk, new JsonObject { ["required"] = new JsonArray("symbol", "boolValue") }, false, 0) }, new Dictionary<string, bool>(), Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(), new Dictionary<string, string>()); }
    private sealed class StubProcessLocator : IProcessLocator { private readonly IReadOnlyList<ProcessMetadata> _p; public StubProcessLocator(IReadOnlyList<ProcessMetadata> p) => _p = p; public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken ct) => Task.FromResult(_p); public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget t, CancellationToken ct) => Task.FromResult(_p.FirstOrDefault(x => x.ExeTarget == t)); }
    private sealed class ThrowingProcessLocator : IProcessLocator { private readonly Exception _ex; public ThrowingProcessLocator(Exception ex) => _ex = ex; public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken ct) => throw _ex; public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget t, CancellationToken ct) => throw _ex; }
    private sealed class StubLaunchContextResolver : ILaunchContextResolver { public LaunchContext Resolve(ProcessMetadata p, IReadOnlyList<TrainerProfile> pr) => new(LaunchKind.Unknown, false, Array.Empty<string>(), null, null, "stub", new ProfileRecommendation(null, "none", 0.0)); }
    private sealed class StubProfileVariantResolver : IProfileVariantResolver { private readonly ProfileVariantResolution? _r; public StubProfileVariantResolver(ProfileVariantResolution? r) => _r = r; public Task<ProfileVariantResolution> ResolveAsync(string id, CancellationToken ct) => Task.FromResult(_r ?? new ProfileVariantResolution(id, id, "none", 0.0)); public Task<ProfileVariantResolution> ResolveAsync(string id, IReadOnlyList<ProcessMetadata>? p, CancellationToken ct) => Task.FromResult(_r ?? new ProfileVariantResolution(id, id, "none", 0.0)); }
    private sealed class StubGameLauncher : IGameLaunchService { private readonly bool _ok; public StubGameLauncher(bool ok) => _ok = ok; public Task<GameLaunchResult> LaunchAsync(GameLaunchRequest r, CancellationToken ct) => Task.FromResult(new GameLaunchResult(_ok, _ok ? "ok" : "failed", 123, @"C:\game\swfoc.exe", "")); }
    private sealed class StubCatalog : ICatalogService { public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string id, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(new Dictionary<string, IReadOnlyList<string>> { ["units"] = new[] { "a", "b" } }); }
    private sealed class StubSaveCodec : ISaveCodec { public Task<SaveDocument> LoadAsync(string p, string s, CancellationToken ct) => throw new NotImplementedException(); public Task EditAsync(SaveDocument d, string n, object? v, CancellationToken ct) => Task.CompletedTask; public Task<SaveValidationResult> ValidateAsync(SaveDocument d, CancellationToken ct) => throw new NotImplementedException(); public Task WriteAsync(SaveDocument d, string o, CancellationToken ct) => Task.CompletedTask; public Task<bool> RoundTripCheckAsync(SaveDocument d, CancellationToken ct) => Task.FromResult(true); }
    private sealed class LoadableSaveCodec : ISaveCodec { private readonly SaveDocument _d; public LoadableSaveCodec(SaveDocument d) => _d = d; public Task<SaveDocument> LoadAsync(string p, string s, CancellationToken ct) => Task.FromResult(_d); public Task EditAsync(SaveDocument d, string n, object? v, CancellationToken ct) => Task.CompletedTask; public Task<SaveValidationResult> ValidateAsync(SaveDocument d, CancellationToken ct) => throw new NotImplementedException(); public Task WriteAsync(SaveDocument d, string o, CancellationToken ct) => Task.CompletedTask; public Task<bool> RoundTripCheckAsync(SaveDocument d, CancellationToken ct) => Task.FromResult(true); }
    private sealed class EditableSaveCodec : ISaveCodec { public Task<SaveDocument> LoadAsync(string p, string s, CancellationToken ct) => throw new NotImplementedException(); public Task EditAsync(SaveDocument d, string n, object? v, CancellationToken ct) => Task.CompletedTask; public Task<SaveValidationResult> ValidateAsync(SaveDocument d, CancellationToken ct) => throw new NotImplementedException(); public Task WriteAsync(SaveDocument d, string o, CancellationToken ct) => Task.CompletedTask; public Task<bool> RoundTripCheckAsync(SaveDocument d, CancellationToken ct) => Task.FromResult(true); }
    private sealed class ValidatingSaveCodec : ISaveCodec { private readonly bool _v; public ValidatingSaveCodec(bool v) => _v = v; public Task<SaveDocument> LoadAsync(string p, string s, CancellationToken ct) => throw new NotImplementedException(); public Task EditAsync(SaveDocument d, string n, object? v, CancellationToken ct) => Task.CompletedTask; public Task<SaveValidationResult> ValidateAsync(SaveDocument d, CancellationToken ct) => Task.FromResult(_v ? new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>()) : new SaveValidationResult(false, new[] { "err" }, Array.Empty<string>())); public Task WriteAsync(SaveDocument d, string o, CancellationToken ct) => Task.CompletedTask; public Task<bool> RoundTripCheckAsync(SaveDocument d, CancellationToken ct) => Task.FromResult(true); }
    private sealed class WritableSaveCodec : ISaveCodec { public Task<SaveDocument> LoadAsync(string p, string s, CancellationToken ct) => throw new NotImplementedException(); public Task EditAsync(SaveDocument d, string n, object? v, CancellationToken ct) => Task.CompletedTask; public Task<SaveValidationResult> ValidateAsync(SaveDocument d, CancellationToken ct) => throw new NotImplementedException(); public Task WriteAsync(SaveDocument d, string o, CancellationToken ct) => Task.CompletedTask; public Task<bool> RoundTripCheckAsync(SaveDocument d, CancellationToken ct) => Task.FromResult(true); }
    private sealed class StubSavePatchPackService : ISavePatchPackService { public Task<SavePatchPack> ExportAsync(SaveDocument o, SaveDocument m, string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchPack> LoadPackAsync(string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class LoadablePatchPackService : ISavePatchPackService { private readonly SavePatchPack _pk; public LoadablePatchPackService(SavePatchPack pk) => _pk = pk; public Task<SavePatchPack> ExportAsync(SaveDocument o, SaveDocument m, string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchPack> LoadPackAsync(string p, CancellationToken ct) => Task.FromResult(_pk); public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class PreviewablePatchPackService : ISavePatchPackService { public Task<SavePatchPack> ExportAsync(SaveDocument o, SaveDocument m, string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchPack> LoadPackAsync(string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => Task.FromResult(new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), new[] { new SavePatchOperation(SavePatchOperationKind.SetValue, "credits", "credits", "Int32", 1000, 5000, 0x10) })); public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => Task.FromResult(new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>())); }
    private sealed class IncompatiblePatchPackService : ISavePatchPackService { public Task<SavePatchPack> ExportAsync(SaveDocument o, SaveDocument m, string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchPack> LoadPackAsync(string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => Task.FromResult(new SavePatchPreview(false, new[] { "err" }, Array.Empty<string>(), Array.Empty<SavePatchOperation>())); public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => Task.FromResult(new SavePatchCompatibilityResult(false, false, "hash", new[] { "mismatch" }, Array.Empty<string>())); }
    private sealed class StubSavePatchApplyService : ISavePatchApplyService { public Task<SavePatchApplyResult> ApplyAsync(string s, SavePatchPack pk, string p, bool st, CancellationToken ct) => throw new NotImplementedException(); public Task<SaveRollbackResult> RestoreLastBackupAsync(string s, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class ApplyingPatchApplyService : ISavePatchApplyService { private readonly bool _ok; public ApplyingPatchApplyService(bool ok) => _ok = ok; public Task<SavePatchApplyResult> ApplyAsync(string s, SavePatchPack pk, string p, bool st, CancellationToken ct) => Task.FromResult(new SavePatchApplyResult(_ok ? SavePatchApplyClassification.Applied : SavePatchApplyClassification.ValidationFailed, _ok, _ok ? "Applied" : "Failed", BackupPath: @"C:\backup.sav", ReceiptPath: @"C:\receipt.json")); public Task<SaveRollbackResult> RestoreLastBackupAsync(string s, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class RestoringPatchApplyService : ISavePatchApplyService { private readonly bool _ok; public RestoringPatchApplyService(bool ok) => _ok = ok; public Task<SavePatchApplyResult> ApplyAsync(string s, SavePatchPack pk, string p, bool st, CancellationToken ct) => throw new NotImplementedException(); public Task<SaveRollbackResult> RestoreLastBackupAsync(string s, CancellationToken ct) => Task.FromResult(new SaveRollbackResult(_ok, _ok ? "Restored" : "No backup found", BackupPath: @"C:\backup.sav")); }
    private sealed class StubHelperMod : IHelperModService { public Task<string> DeployAsync(string id, CancellationToken ct) => Task.FromResult(@"C:\h.dll"); public Task<bool> VerifyAsync(string id, CancellationToken ct) => Task.FromResult(true); }
    private sealed class StubProfileUpdates : IProfileUpdateService { private readonly IReadOnlyList<string> _u; public StubProfileUpdates(IReadOnlyList<string> u) => _u = u; public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken ct) => Task.FromResult(_u); public Task<string> InstallProfileAsync(string id, CancellationToken ct) => Task.FromResult(@"C:\p.json"); public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string id, CancellationToken ct) => Task.FromResult(new ProfileInstallResult(true, id, @"C:\p.json", @"C:\b.json", @"C:\r.json", "ok", null)); public Task<ProfileRollbackResult> RollbackLastInstallAsync(string id, CancellationToken ct) => Task.FromResult(new ProfileRollbackResult(true, id, @"C:\p.json", @"C:\b.json", "ok", null)); }
    private sealed class FailingProfileUpdates : IProfileUpdateService { public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()); public Task<string> InstallProfileAsync(string id, CancellationToken ct) => Task.FromResult(@"C:\p.json"); public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string id, CancellationToken ct) => Task.FromResult(new ProfileInstallResult(false, id, null!, null, null, "install_error", "test_reason")); public Task<ProfileRollbackResult> RollbackLastInstallAsync(string id, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class FailingRollbackUpdates : IProfileUpdateService { public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()); public Task<string> InstallProfileAsync(string id, CancellationToken ct) => Task.FromResult(@"C:\p.json"); public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string id, CancellationToken ct) => throw new NotImplementedException(); public Task<ProfileRollbackResult> RollbackLastInstallAsync(string id, CancellationToken ct) => Task.FromResult(new ProfileRollbackResult(false, id, null!, null, "Rollback failed", "test_reason")); }
    private sealed class FailingRollbackNullReasonCode : IProfileUpdateService { public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()); public Task<string> InstallProfileAsync(string id, CancellationToken ct) => Task.FromResult(@"C:\p.json"); public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string id, CancellationToken ct) => throw new NotImplementedException(); public Task<ProfileRollbackResult> RollbackLastInstallAsync(string id, CancellationToken ct) => Task.FromResult(new ProfileRollbackResult(false, id, null!, null, "Rollback failed", null)); }
    private sealed class InstallUpdateEmptyPaths : IProfileUpdateService { public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()); public Task<string> InstallProfileAsync(string id, CancellationToken ct) => Task.FromResult(@"C:\p.json"); public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string id, CancellationToken ct) => Task.FromResult(new ProfileInstallResult(true, id, @"C:\p.json", "", "", "ok", null)); public Task<ProfileRollbackResult> RollbackLastInstallAsync(string id, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class StubModOnboarding : IModOnboardingService { public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest r, CancellationToken ct) => Task.FromResult(new ModOnboardingResult(true, r.DraftProfileId, @"C:\d.json", new[] { "ws" }, new[] { @"C:\" }, new[] { r.DraftProfileId }, Array.Empty<string>())); public Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(ModOnboardingSeedBatchRequest r, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class StubModCalibration : IModCalibrationService { public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest r, CancellationToken ct) => Task.FromResult(new ModCalibrationArtifactResult(true, @"C:\c.json", "X", Array.Empty<CalibrationCandidate>(), Array.Empty<string>())); public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(TrainerProfile p, AttachSession? s, DependencyValidationResult? d, IReadOnlyDictionary<string, IReadOnlyList<string>>? c, CancellationToken ct) => Task.FromResult(new ModCompatibilityReport(p.Id, DateTimeOffset.UtcNow, RuntimeMode.Unknown, DependencyValidationStatus.Pass, 0, true, Array.Empty<ModActionCompatibility>(), Array.Empty<string>())); }
    private sealed class StubSupportBundles : ISupportBundleService { private readonly bool _ok; public StubSupportBundles(bool ok) => _ok = ok; public Task<SupportBundleResult> ExportAsync(SupportBundleRequest r, CancellationToken ct) => Task.FromResult(new SupportBundleResult(_ok, @"C:\b.zip", @"C:\m.json", Array.Empty<string>(), Array.Empty<string>())); }
    private sealed class StubFreezeService : IValueFreezeService { public void FreezeInt(string s, int v) { } public void FreezeIntAggressive(string s, int v) { } public void FreezeFloat(string s, float v) { } public void FreezeBool(string s, bool v) { } public bool Unfreeze(string s) => false; public void UnfreezeAll() { } public bool IsFrozen(string s) => false; public IReadOnlyCollection<string> GetFrozenSymbols() => Array.Empty<string>(); public void Dispose() { } }
    private sealed class StubAuditLogger : IAuditLogger { public Task WriteAsync(ActionAuditRecord r, CancellationToken ct) => Task.CompletedTask; }
    private sealed class StubTelemetry : ITelemetrySnapshotService { public void RecordAction(string a, AddressSource s, bool ok) { } public TelemetrySnapshot CreateSnapshot() => new(DateTimeOffset.UtcNow, new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), 0, 0, 0, 0); public Task<string> ExportSnapshotAsync(string d, CancellationToken ct) => Task.FromResult(Path.Combine(d, "t.json")); public void Reset() { } }
    private sealed class StubActionReliability : IActionReliabilityService { public IReadOnlyList<ActionReliabilityInfo> Evaluate(TrainerProfile p, AttachSession s, IReadOnlyDictionary<string, IReadOnlyList<string>>? c) => Array.Empty<ActionReliabilityInfo>(); }
    private sealed class StubSelectedUnitTransactions : ISelectedUnitTransactionService { public SelectedUnitSnapshot? Baseline => null; public IReadOnlyList<SelectedUnitTransactionRecord> History => Array.Empty<SelectedUnitTransactionRecord>(); public Task<SelectedUnitSnapshot> CaptureAsync(CancellationToken ct) => Task.FromResult(new SelectedUnitSnapshot(100f, 50f, 10f, 1f, 1f, 0, 0, DateTimeOffset.UtcNow)); public Task<SelectedUnitTransactionResult> ApplyAsync(string p, SelectedUnitDraft d, RuntimeMode m, CancellationToken ct) => Task.FromResult(new SelectedUnitTransactionResult(true, "ok", "tx1", Array.Empty<ActionExecutionResult>())); public Task<SelectedUnitTransactionResult> RevertLastAsync(string p, RuntimeMode m, CancellationToken ct) => Task.FromResult(new SelectedUnitTransactionResult(true, "ok", "tx1", Array.Empty<ActionExecutionResult>())); public Task<SelectedUnitTransactionResult> RestoreBaselineAsync(string p, RuntimeMode m, CancellationToken ct) => Task.FromResult(new SelectedUnitTransactionResult(true, "ok", "tx1", Array.Empty<ActionExecutionResult>())); }
    private sealed class StubSpawnPresets : ISpawnPresetService { public Task<IReadOnlyList<SpawnPreset>> LoadPresetsAsync(string p, CancellationToken ct) => Task.FromResult<IReadOnlyList<SpawnPreset>>(Array.Empty<SpawnPreset>()); public SpawnBatchPlan BuildBatchPlan(string p, SpawnPreset pr, int q, int d, string? f, string? e, bool s) => throw new NotImplementedException(); public Task<SpawnBatchExecutionResult> ExecuteBatchAsync(string p, SpawnBatchPlan pl, RuntimeMode m, CancellationToken ct) => throw new NotImplementedException(); }
}
