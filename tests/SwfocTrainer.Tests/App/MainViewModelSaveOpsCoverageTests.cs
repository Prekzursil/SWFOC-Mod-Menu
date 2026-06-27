using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Serialization;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Full branch coverage for MainViewModelSaveOpsBase — tests every if/else, null guard,
/// early return, and conditional path in save-related operations.
/// </summary>
public sealed class MainViewModelSaveOpsCoverageTests
{
    // ── LoadSaveAsync ──

    [Fact]
    public async Task LoadSaveAsync_NullProfileId_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeProtectedAsync(vm, "LoadSaveAsync", new object[] { true });

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task LoadSaveAsync_WhitespaceProfileId_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";

        await InvokeProtectedAsync(vm, "LoadSaveAsync", new object[] { true });

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task LoadSaveAsync_WithProfile_ShouldLoadAndSetStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SavePath = @"C:\test.sav";
        SetField(vm, "_profiles", new StubProfileRepository());
        SetField(vm, "_saveCodec", new StubSaveCodec());

        await InvokeProtectedAsync(vm, "LoadSaveAsync", new object[] { true });

        vm.Status.Should().Contain("Loaded save with schema");
    }

    [Fact]
    public async Task LoadSaveAsync_ClearPatchSummaryFalse_ShouldNotClearSummary()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SavePatchApplySummary = "existing summary";
        SetField(vm, "_profiles", new StubProfileRepository());
        SetField(vm, "_saveCodec", new StubSaveCodec());

        await InvokeProtectedAsync(vm, "LoadSaveAsync", new object[] { false });

        vm.SavePatchApplySummary.Should().Be("existing summary");
    }

    [Fact]
    public async Task LoadSaveAsync_ClearPatchSummaryTrue_ShouldClearSummary()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SavePatchApplySummary = "existing summary";
        SetField(vm, "_profiles", new StubProfileRepository());
        SetField(vm, "_saveCodec", new StubSaveCodec());

        await InvokeProtectedAsync(vm, "LoadSaveAsync", new object[] { true });

        vm.SavePatchApplySummary.Should().BeEmpty();
    }

    // ── EditSaveAsync ──

    [Fact]
    public async Task EditSaveAsync_NullLoadedSave_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);

        await InvokeProtectedAsync(vm, "EditSaveAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task EditSaveAsync_WithLoadedSave_ShouldEditAndSetStatus()
    {
        var vm = CreateViewModel();
        var doc = BuildSaveDocument();
        SetField(vm, "_loadedSave", doc);
        SetField(vm, "_loadedSaveOriginal", doc.Raw.ToArray());
        SetField(vm, "_saveCodec", new StubSaveCodec());
        vm.SaveNodePath = "credits";
        vm.SaveEditValue = "5000";

        await InvokeProtectedAsync(vm, "EditSaveAsync");

        vm.Status.Should().Contain("Edited save field: credits");
    }

    // ── ValidateSaveAsync ──

    [Fact]
    public async Task ValidateSaveAsync_NullLoadedSave_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);

        await InvokeProtectedAsync(vm, "ValidateSaveAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task ValidateSaveAsync_Valid_ShouldSetPassedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_saveCodec", new StubSaveCodec(isValid: true));

        await InvokeProtectedAsync(vm, "ValidateSaveAsync");

        vm.Status.Should().Contain("validation passed");
    }

    [Fact]
    public async Task ValidateSaveAsync_Invalid_ShouldSetFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_saveCodec", new StubSaveCodec(isValid: false));

        await InvokeProtectedAsync(vm, "ValidateSaveAsync");

        vm.Status.Should().Contain("validation failed");
    }

    // ── WriteSaveAsync ──

    [Fact]
    public async Task WriteSaveAsync_NullLoadedSave_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);

        await InvokeProtectedAsync(vm, "WriteSaveAsync");

        vm.Status.Should().Be("Ready");
    }

    // ── PreviewPatchPackAsync ──

    [Fact]
    public async Task PreviewPatchPackAsync_NullPatchPack_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", null);
        SetField(vm, "_loadedSave", BuildSaveDocument());
        vm.SelectedProfileId = "test_profile";

        await InvokeProtectedAsync(vm, "PreviewPatchPackAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task PreviewPatchPackAsync_NullLoadedSave_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_loadedSave", null);
        vm.SelectedProfileId = "test_profile";

        await InvokeProtectedAsync(vm, "PreviewPatchPackAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task PreviewPatchPackAsync_WhitespaceProfileId_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_loadedSave", BuildSaveDocument());
        vm.SelectedProfileId = "  ";

        await InvokeProtectedAsync(vm, "PreviewPatchPackAsync");

        vm.Status.Should().Be("Ready");
    }

    // ── PreparePatchPreview ──

    [Fact]
    public void PreparePatchPreview_NullProfileId_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => vm.GetType().GetMethod("PreparePatchPreview",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { null! });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void PreparePatchPreview_NoVariantMismatch_ShouldReturnTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: null));

        var result = InvokeProtected<bool>(vm, "PreparePatchPreview", "test_profile");

        result.Should().BeTrue();
    }

    [Fact]
    public void PreparePatchPreview_WithVariantMismatch_ShouldReturnFalseAndPopulateCompatibility()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "roe"
        });
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: session));

        var result = InvokeProtected<bool>(vm, "PreparePatchPreview", "different_profile");

        result.Should().BeFalse();
        vm.SavePatchCompatibility.Should().HaveCount(1);
        vm.SavePatchCompatibility[0].Code.Should().Be("save_variant_mismatch");
    }

    // ── ValidateSaveRuntimeVariant ──

    [Fact]
    public void ValidateSaveRuntimeVariant_NullProfileId_ShouldThrow()
    {
        var vm = CreateViewModel();
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "roe"
        });
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: session));

        // Use direct reflection to pass null — the generic FindMethod helper doesn't handle null args.
        var method = typeof(MainViewModelSaveOpsBase).GetMethod(
            "ValidateSaveRuntimeVariant",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var act = () => method!.Invoke(vm, new object?[] { null });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_NullSession_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: null));

        var result = InvokeProtected<string?>(vm, "ValidateSaveRuntimeVariant", "test");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_NullMetadata_ShouldReturnNull()
    {
        var session = BuildSession(metadata: null);
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: session));

        var result = InvokeProtected<string?>(vm, "ValidateSaveRuntimeVariant", "test");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_NoResolvedVariant_ShouldReturnNull()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: session));

        var result = InvokeProtected<string?>(vm, "ValidateSaveRuntimeVariant", "test");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_EmptyResolvedVariant_ShouldReturnNull()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "  "
        });
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: session));

        var result = InvokeProtected<string?>(vm, "ValidateSaveRuntimeVariant", "test");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_UniversalProfileId_ShouldReturnNull()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "roe"
        });
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: session));

        var result = InvokeProtected<string?>(vm, "ValidateSaveRuntimeVariant", "universal_auto");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_MatchingVariant_ShouldReturnNull()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "roe"
        });
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: session));

        var result = InvokeProtected<string?>(vm, "ValidateSaveRuntimeVariant", "roe");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_Mismatch_ShouldReturnMessage()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "roe"
        });
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: session));

        var result = InvokeProtected<string?>(vm, "ValidateSaveRuntimeVariant", "aotr");
        result.Should().Contain("save_variant_mismatch");
        result.Should().Contain("runtime=roe");
        result.Should().Contain("selected=aotr");
    }

    // ── ApplyPatchPackAsync ──

    [Fact]
    public async Task ApplyPatchPackAsync_NullPatchPack_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", null);
        vm.SelectedProfileId = "test_profile";

        await InvokeProtectedAsync(vm, "ApplyPatchPackAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task ApplyPatchPackAsync_WhitespaceProfileId_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        vm.SelectedProfileId = "  ";

        await InvokeProtectedAsync(vm, "ApplyPatchPackAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task ApplyPatchPackAsync_VariantMismatch_ShouldSetSummaryAndStatus()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "roe"
        });
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: session));
        vm.SelectedProfileId = "different_profile";

        await InvokeProtectedAsync(vm, "ApplyPatchPackAsync");

        vm.SavePatchApplySummary.Should().Contain("save_variant_mismatch");
        vm.Status.Should().Contain("save_variant_mismatch");
    }

    [Fact]
    public async Task ApplyPatchPackAsync_ApplySucceeded_ShouldLoadSaveAndSetStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: null));
        SetField(vm, "_savePatchApplyService", new StubSavePatchApplyService(applied: true));
        SetField(vm, "_profiles", new StubProfileRepository());
        SetField(vm, "_saveCodec", new StubSaveCodec());
        vm.SelectedProfileId = "test_profile";

        await InvokeProtectedAsync(vm, "ApplyPatchPackAsync");

        vm.Status.Should().Contain("Patch applied successfully");
    }

    [Fact]
    public async Task ApplyPatchPackAsync_ApplyFailed_ShouldSetFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: null));
        SetField(vm, "_savePatchApplyService", new StubSavePatchApplyService(applied: false));
        vm.SelectedProfileId = "test_profile";

        await InvokeProtectedAsync(vm, "ApplyPatchPackAsync");

        vm.Status.Should().Contain("Patch apply failed");
    }

    // ── RestoreBackupAsync ──

    [Fact]
    public async Task RestoreBackupAsync_Restored_WithProfile_ShouldReloadSave()
    {
        var vm = CreateViewModel();
        SetField(vm, "_savePatchApplyService", new StubSavePatchApplyService(restored: true));
        SetField(vm, "_profiles", new StubProfileRepository());
        SetField(vm, "_saveCodec", new StubSaveCodec());
        vm.SelectedProfileId = "test_profile";

        await InvokeProtectedAsync(vm, "RestoreBackupAsync");

        vm.Status.Should().Contain("Backup restored");
    }

    [Fact]
    public async Task RestoreBackupAsync_Restored_WithoutProfile_ShouldNotReloadSave()
    {
        var vm = CreateViewModel();
        SetField(vm, "_savePatchApplyService", new StubSavePatchApplyService(restored: true));
        vm.SelectedProfileId = "  ";

        await InvokeProtectedAsync(vm, "RestoreBackupAsync");

        // Restored is true but SelectedProfileId is whitespace — skips LoadSaveAsync but
        // the final status uses result.Restored which is still true.
        vm.Status.Should().Contain("Backup restored");
    }

    [Fact]
    public async Task RestoreBackupAsync_NotRestored_ShouldSetSkippedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_savePatchApplyService", new StubSavePatchApplyService(restored: false));

        await InvokeProtectedAsync(vm, "RestoreBackupAsync");

        vm.Status.Should().Contain("Backup restore skipped");
    }

    // ── RefreshDiffAsync ──

    [Fact]
    public async Task RefreshDiffAsync_NullOriginal_ShouldClearAndReturn()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSaveOriginal", null);
        SetField(vm, "_loadedSave", null);

        await InvokeProtectedAsync(vm, "RefreshDiffAsync");

        vm.SaveDiffPreview.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshDiffAsync_NullLoadedSave_ShouldClearAndReturn()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSaveOriginal", new byte[] { 1, 2, 3 });
        SetField(vm, "_loadedSave", null);

        await InvokeProtectedAsync(vm, "RefreshDiffAsync");

        vm.SaveDiffPreview.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshDiffAsync_IdenticalBytes_ShouldShowNoDifferences()
    {
        var vm = CreateViewModel();
        var bytes = new byte[] { 1, 2, 3 };
        var doc = new SaveDocument(@"C:\test.sav", "schema",
            bytes.ToArray(), new SaveNode("root", "root", "root", null));
        SetField(vm, "_loadedSaveOriginal", bytes.ToArray());
        SetField(vm, "_loadedSave", doc);

        await InvokeProtectedAsync(vm, "RefreshDiffAsync");

        vm.SaveDiffPreview.Should().Contain("No differences detected.");
    }

    [Fact]
    public async Task RefreshDiffAsync_DifferentBytes_ShouldShowDifferences()
    {
        var vm = CreateViewModel();
        var original = new byte[] { 1, 2, 3 };
        var modified = new byte[] { 1, 5, 3 };
        var doc = new SaveDocument(@"C:\test.sav", "schema",
            modified, new SaveNode("root", "root", "root", null));
        SetField(vm, "_loadedSaveOriginal", original);
        SetField(vm, "_loadedSave", doc);

        await InvokeProtectedAsync(vm, "RefreshDiffAsync");

        vm.SaveDiffPreview.Should().NotBeEmpty();
        vm.SaveDiffPreview.Should().NotContain("No differences detected.");
    }

    // ── RebuildSaveFieldRows ──

    [Fact]
    public void RebuildSaveFieldRows_NullLoadedSave_ShouldClearFields()
    {
        var vm = CreateViewModel();
        vm.SaveFields.Add(new SaveFieldViewItem("p", "n", "t", "v"));
        SetField(vm, "_loadedSave", null);

        InvokeProtected(vm, "RebuildSaveFieldRows");

        vm.SaveFields.Should().BeEmpty();
    }

    [Fact]
    public void RebuildSaveFieldRows_WithLeafNode_ShouldFlatten()
    {
        var vm = CreateViewModel();
        var leaf = new SaveNode("credits", "credits", "Int32", 1000);
        var root = new SaveNode("root", "root", "root", null, new[] { leaf });
        var doc = new SaveDocument(@"C:\test.sav", "schema", new byte[4], root);
        SetField(vm, "_loadedSave", doc);

        InvokeProtected(vm, "RebuildSaveFieldRows");

        vm.SaveFields.Should().HaveCount(1);
        vm.SaveFields[0].Name.Should().Be("credits");
    }

    [Fact]
    public void RebuildSaveFieldRows_WithRootTypeLeaf_ShouldSkip()
    {
        var vm = CreateViewModel();
        var root = new SaveNode("root", "root", "root", null);
        var doc = new SaveDocument(@"C:\test.sav", "schema", new byte[4], root);
        SetField(vm, "_loadedSave", doc);

        InvokeProtected(vm, "RebuildSaveFieldRows");

        vm.SaveFields.Should().BeEmpty();
    }

    [Fact]
    public void RebuildSaveFieldRows_WithNullValueLeaf_ShouldUseEmptyString()
    {
        var vm = CreateViewModel();
        var leaf = new SaveNode("field", "field", "String", null);
        var root = new SaveNode("root", "root", "root", null, new[] { leaf });
        var doc = new SaveDocument(@"C:\test.sav", "schema", new byte[4], root);
        SetField(vm, "_loadedSave", doc);

        InvokeProtected(vm, "RebuildSaveFieldRows");

        vm.SaveFields.Should().HaveCount(1);
        vm.SaveFields[0].Value.Should().BeEmpty();
    }

    [Fact]
    public void RebuildSaveFieldRows_WithNestedChildren_ShouldFlattenRecursively()
    {
        var vm = CreateViewModel();
        var inner = new SaveNode("inner", "inner", "Int32", 42);
        var mid = new SaveNode("mid", "mid", "group", null, new[] { inner });
        var root = new SaveNode("root", "root", "root", null, new[] { mid });
        var doc = new SaveDocument(@"C:\test.sav", "schema", new byte[4], root);
        SetField(vm, "_loadedSave", doc);

        InvokeProtected(vm, "RebuildSaveFieldRows");

        vm.SaveFields.Should().HaveCount(1);
        vm.SaveFields[0].Name.Should().Be("inner");
    }

    // ── FlattenNodes ──

    [Fact]
    public void FlattenNodes_NullRoot_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => InvokeProtected(vm, "FlattenNodes", default(SaveNode));
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    // ── ApplySaveSearch ──

    [Fact]
    public void ApplySaveSearch_EmptyQuery_ShouldReturnAllFieldsUpToLimit()
    {
        var vm = CreateViewModel();
        vm.SaveFields.Add(new SaveFieldViewItem("path1", "credits", "Int32", "1000"));
        vm.SaveFields.Add(new SaveFieldViewItem("path2", "fog", "Bool", "false"));
        vm.SaveSearchQuery = string.Empty;

        // Trigger search
        InvokeProtected(vm, "ApplySaveSearch");

        vm.FilteredSaveFields.Should().HaveCount(2);
    }

    [Fact]
    public void ApplySaveSearch_WithQuery_ShouldFilterByPathNameOrValue()
    {
        var vm = CreateViewModel();
        vm.SaveFields.Add(new SaveFieldViewItem("credits_path", "credits", "Int32", "1000"));
        vm.SaveFields.Add(new SaveFieldViewItem("fog_path", "fog", "Bool", "false"));
        vm.SaveFields.Add(new SaveFieldViewItem("speed_path", "speed", "Float", "2.5"));
        SetField(vm, "_saveSearchQuery", "credits");

        InvokeProtected(vm, "ApplySaveSearch");

        vm.FilteredSaveFields.Should().HaveCount(1);
        vm.FilteredSaveFields[0].Name.Should().Be("credits");
    }

    [Fact]
    public void ApplySaveSearch_WithQuery_ShouldMatchOnValue()
    {
        var vm = CreateViewModel();
        vm.SaveFields.Add(new SaveFieldViewItem("p", "n", "t", "search_target"));
        SetField(vm, "_saveSearchQuery", "search_target");

        InvokeProtected(vm, "ApplySaveSearch");

        vm.FilteredSaveFields.Should().HaveCount(1);
    }

    // ── PopulatePatchPreviewOperations ──

    [Fact]
    public void PopulatePatchPreviewOperations_NullPreview_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => InvokeProtected(vm, "PopulatePatchPreviewOperations", default(SavePatchPreview));
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void PopulatePatchPreviewOperations_WithOperations_ShouldPopulateCollection()
    {
        var vm = CreateViewModel();
        var ops = new[]
        {
            new SavePatchOperation(SavePatchOperationKind.SetValue, "credits", "credits", "Int32", 1000, 5000, 0x10)
        };
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), ops);

        InvokeProtected(vm, "PopulatePatchPreviewOperations", preview);

        vm.SavePatchOperations.Should().HaveCount(1);
        vm.SavePatchOperations[0].FieldPath.Should().Be("credits");
    }

    // ── PopulatePatchCompatibilityRows ──

    [Fact]
    public void PopulatePatchCompatibilityRows_NullCompatibility_ShouldThrow()
    {
        var vm = CreateViewModel();
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SavePatchOperation>());
        var act = () => InvokeProtected(vm, "PopulatePatchCompatibilityRows",
            null!, preview);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_NullPreview_ShouldThrow()
    {
        var vm = CreateViewModel();
        var compat = new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>());
        var act = () => InvokeProtected(vm, "PopulatePatchCompatibilityRows",
            compat, null!);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_HashMatches_ShouldShowMatchMessage()
    {
        var vm = CreateViewModel();
        var compat = new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>());
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SavePatchOperation>());
        vm.IsStrictPatchApply = true;

        InvokeProtected(vm, "PopulatePatchCompatibilityRows", compat, preview);

        vm.SavePatchCompatibility.Should().HaveCountGreaterOrEqualTo(2);
        vm.SavePatchCompatibility[0].Message.Should().Contain("Source hash matches");
        vm.SavePatchCompatibility[1].Message.Should().Contain("Strict apply is ON");
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_HashMismatch_ShouldShowMismatchMessage()
    {
        var vm = CreateViewModel();
        var compat = new SavePatchCompatibilityResult(true, false, "hash", Array.Empty<string>(), Array.Empty<string>());
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SavePatchOperation>());
        vm.IsStrictPatchApply = false;

        InvokeProtected(vm, "PopulatePatchCompatibilityRows", compat, preview);

        vm.SavePatchCompatibility[0].Message.Should().Contain("Source hash mismatch");
        vm.SavePatchCompatibility[1].Message.Should().Contain("Strict apply is OFF");
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_WithWarningsAndErrors_ShouldAppendAll()
    {
        var vm = CreateViewModel();
        var compat = new SavePatchCompatibilityResult(false, true, "hash",
            new[] { "compat_error" }, new[] { "compat_warn" });
        var preview = new SavePatchPreview(false,
            new[] { "preview_error" }, new[] { "preview_warn" }, Array.Empty<SavePatchOperation>());

        InvokeProtected(vm, "PopulatePatchCompatibilityRows", compat, preview);

        var messages = vm.SavePatchCompatibility.Select(x => x.Message).ToList();
        messages.Should().Contain("compat_warn");
        messages.Should().Contain("preview_warn");
        messages.Should().Contain("compat_error");
        messages.Should().Contain("preview_error");
    }

    // ── AppendPatchCompatibilityRows ──

    [Fact]
    public void AppendPatchCompatibilityRows_NullSeverity_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => InvokeProtected(vm, "AppendPatchCompatibilityRows",
            null!, "code", Enumerable.Empty<string>());
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void AppendPatchCompatibilityRows_NullReasonCode_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => InvokeProtected(vm, "AppendPatchCompatibilityRows",
            "warn", null!, Enumerable.Empty<string>());
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void AppendPatchCompatibilityRows_NullMessages_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => InvokeProtected(vm, "AppendPatchCompatibilityRows",
            "warn", "code", null!);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void AppendPatchCompatibilityRows_EmptyMessages_ShouldNotAdd()
    {
        var vm = CreateViewModel();
        InvokeProtected(vm, "AppendPatchCompatibilityRows",
            "warn", "code", Enumerable.Empty<string>());

        vm.SavePatchCompatibility.Should().BeEmpty();
    }

    [Fact]
    public void AppendPatchCompatibilityRows_WithMessages_ShouldAddEach()
    {
        var vm = CreateViewModel();
        InvokeProtected(vm, "AppendPatchCompatibilityRows",
            "error", "err_code", new[] { "msg1", "msg2" });

        vm.SavePatchCompatibility.Should().HaveCount(2);
    }

    // ── SetLoadedPatchPack ──

    [Fact]
    public void SetLoadedPatchPack_NullPack_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => InvokeProtected(vm, "SetLoadedPatchPack", null!, "path");
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void SetLoadedPatchPack_NullPath_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => InvokeProtected(vm, "SetLoadedPatchPack", BuildPatchPack(), null!);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void SetLoadedPatchPack_ValidInputs_ShouldSetMetadataAndPath()
    {
        var vm = CreateViewModel();
        var pack = BuildPatchPack();

        InvokeProtected(vm, "SetLoadedPatchPack", pack, @"C:\patch.json");

        vm.SavePatchPackPath.Should().Be(@"C:\patch.json");
        vm.SavePatchMetadataSummary.Should().Contain("profile=test_profile");
    }

    // ── ClearPatchPreviewState ──

    [Fact]
    public void ClearPatchPreviewState_ClearLoadedPackTrue_ShouldResetPack()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        vm.SavePatchMetadataSummary = "old";

        InvokeProtected(vm, "ClearPatchPreviewState", true);

        vm.SavePatchMetadataSummary.Should().Be("No patch pack loaded.");
    }

    [Fact]
    public void ClearPatchPreviewState_ClearLoadedPackFalse_ShouldKeepPack()
    {
        var vm = CreateViewModel();
        var pack = BuildPatchPack();
        SetField(vm, "_loadedPatchPack", pack);
        vm.SavePatchMetadataSummary = "old";

        InvokeProtected(vm, "ClearPatchPreviewState", false);

        vm.SavePatchMetadataSummary.Should().Be("old");
    }

    // ── AppendPatchArtifactRows ──

    [Fact]
    public void AppendPatchArtifactRows_BothNull_ShouldNotAdd()
    {
        var vm = CreateViewModel();
        InvokeProtected(vm, "AppendPatchArtifactRows", null, null);

        vm.SavePatchCompatibility.Should().BeEmpty();
    }

    [Fact]
    public void AppendPatchArtifactRows_BothWhitespace_ShouldNotAdd()
    {
        var vm = CreateViewModel();
        InvokeProtected(vm, "AppendPatchArtifactRows", "  ", "  ");

        vm.SavePatchCompatibility.Should().BeEmpty();
    }

    [Fact]
    public void AppendPatchArtifactRows_OnlyBackup_ShouldAddOne()
    {
        var vm = CreateViewModel();
        InvokeProtected(vm, "AppendPatchArtifactRows", @"C:\backup.sav", default(string));

        vm.SavePatchCompatibility.Should().HaveCount(1);
        vm.SavePatchCompatibility[0].Code.Should().Be("backup_path");
    }

    [Fact]
    public void AppendPatchArtifactRows_OnlyReceipt_ShouldAddOne()
    {
        var vm = CreateViewModel();
        InvokeProtected(vm, "AppendPatchArtifactRows", null, @"C:\receipt.json");

        vm.SavePatchCompatibility.Should().HaveCount(1);
        vm.SavePatchCompatibility[0].Code.Should().Be("receipt_path");
    }

    [Fact]
    public void AppendPatchArtifactRows_BothPresent_ShouldAddTwo()
    {
        var vm = CreateViewModel();
        InvokeProtected(vm, "AppendPatchArtifactRows", @"C:\backup.sav", @"C:\receipt.json");

        vm.SavePatchCompatibility.Should().HaveCount(2);
    }

    // ── Catalog/Helper/Updates/Onboarding guards ──

    [Fact]
    public async Task LoadCatalogAsync_WhitespaceProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";

        await InvokeProtectedAsync(vm, "LoadCatalogAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task DeployHelperAsync_WhitespaceProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";

        await InvokeProtectedAsync(vm, "DeployHelperAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task VerifyHelperAsync_WhitespaceProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";

        await InvokeProtectedAsync(vm, "VerifyHelperAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task InstallUpdateAsync_WhitespaceProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";

        await InvokeProtectedAsync(vm, "InstallUpdateAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_WhitespaceProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";

        await InvokeProtectedAsync(vm, "RollbackProfileUpdateAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task InstallUpdateAsync_Failed_ShouldSetFailedStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_updates", new StubProfileUpdateService(succeeded: false, reasonCode: "version_conflict"));

        await InvokeProtectedAsync(vm, "InstallUpdateAsync");

        vm.Status.Should().Contain("Profile update failed");
        vm.OpsArtifactSummary.Should().Contain("version_conflict");
    }

    [Fact]
    public async Task InstallUpdateAsync_Failed_NullReasonCode_ShouldUseUnknown()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_updates", new StubProfileUpdateService(succeeded: false, reasonCode: null));

        await InvokeProtectedAsync(vm, "InstallUpdateAsync");

        vm.OpsArtifactSummary.Should().Contain("unknown");
    }

    [Fact]
    public async Task InstallUpdateAsync_Succeeded_ShouldSetSuccessStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_updates", new StubProfileUpdateService(succeeded: true));

        await InvokeProtectedAsync(vm, "InstallUpdateAsync");

        vm.Status.Should().Contain("Installed profile update");
    }

    [Fact]
    public async Task InstallUpdateAsync_Succeeded_WithReceipt_ShouldIncludeReceipt()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_updates", new StubProfileUpdateService(succeeded: true, receiptPath: @"C:\receipt.json", backupPath: @"C:\backup"));

        await InvokeProtectedAsync(vm, "InstallUpdateAsync");

        vm.OpsArtifactSummary.Should().Contain(@"C:\receipt.json");
        vm.OpsArtifactSummary.Should().Contain(@"C:\backup");
    }

    [Fact]
    public async Task InstallUpdateAsync_Succeeded_WithoutReceipt_ShouldShowNoReceipt()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_updates", new StubProfileUpdateService(succeeded: true, receiptPath: "", backupPath: ""));

        await InvokeProtectedAsync(vm, "InstallUpdateAsync");

        vm.OpsArtifactSummary.Should().Contain("no receipt");
        vm.OpsArtifactSummary.Should().Contain("no backup");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_Failed_ShouldSetFailedStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_updates", new StubProfileUpdateService(rollbackRestored: false, rollbackReasonCode: "no_backup"));

        await InvokeProtectedAsync(vm, "RollbackProfileUpdateAsync");

        vm.Status.Should().Contain("Rollback failed");
        vm.OpsArtifactSummary.Should().Contain("no_backup");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_Failed_NullReasonCode_ShouldUseUnknown()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_updates", new StubProfileUpdateService(rollbackRestored: false, rollbackReasonCode: null));

        await InvokeProtectedAsync(vm, "RollbackProfileUpdateAsync");

        vm.OpsArtifactSummary.Should().Contain("unknown");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_Succeeded_ShouldSetStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_updates", new StubProfileUpdateService(rollbackRestored: true, rollbackBackupPath: @"C:\bk"));

        await InvokeProtectedAsync(vm, "RollbackProfileUpdateAsync");

        vm.OpsArtifactSummary.Should().Contain(@"C:\bk");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_Succeeded_NullBackupPath_ShouldShowNA()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_updates", new StubProfileUpdateService(rollbackRestored: true, rollbackBackupPath: null));

        await InvokeProtectedAsync(vm, "RollbackProfileUpdateAsync");

        vm.OpsArtifactSummary.Should().Contain("n/a");
    }

    // ── ExportCalibrationArtifactAsync ──

    [Fact]
    public async Task ExportCalibrationArtifactAsync_WithSelectedProfile_ShouldUseSelectedProfile()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: null));
        SetField(vm, "_modCalibration", new StubModCalibrationService(calibrationSucceeded: true));
        vm.SupportBundleOutputDirectory = Path.GetTempPath();

        await InvokeProtectedAsync(vm, "ExportCalibrationArtifactAsync");

        vm.Status.Should().Contain("Calibration artifact exported");
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_WithoutSelectedProfile_ShouldUseDraftProfileId()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";
        vm.OnboardingDraftProfileId = "draft_id";
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: null));
        SetField(vm, "_modCalibration", new StubModCalibrationService(calibrationSucceeded: true));
        vm.SupportBundleOutputDirectory = Path.GetTempPath();

        await InvokeProtectedAsync(vm, "ExportCalibrationArtifactAsync");

        vm.Status.Should().Contain("Calibration artifact exported");
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_Failed_ShouldSetFailedStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: null));
        SetField(vm, "_modCalibration", new StubModCalibrationService(calibrationSucceeded: false));
        vm.SupportBundleOutputDirectory = Path.GetTempPath();

        await InvokeProtectedAsync(vm, "ExportCalibrationArtifactAsync");

        vm.Status.Should().Contain("failed");
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_ShouldPopulateRows()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_profiles", new StubProfileRepository());
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: null));
        SetField(vm, "_modCalibration", new StubModCalibrationService());

        await InvokeProtectedAsync(vm, "BuildCompatibilityReportAsync");

        vm.Status.Should().Contain("Compatibility report generated");
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_WithoutSelectedProfile_ShouldUseDraftId()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";
        vm.OnboardingDraftProfileId = "draft_id";
        SetField(vm, "_profiles", new StubProfileRepository());
        SetField(vm, "_runtime", new StubRuntimeAdapter(session: null));
        SetField(vm, "_modCalibration", new StubModCalibrationService());

        await InvokeProtectedAsync(vm, "BuildCompatibilityReportAsync");

        vm.Status.Should().Contain("Compatibility report generated");
    }

    // ── Helpers ──

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
        InitBackingFields(vm);
        return vm;
    }

    private static void InitBackingFields(object vm)
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
        SetField(vm, "_spawnStopOnFailure", true);
        SetField(vm, "_isStrictPatchApply", true);
        SetField(vm, "_onboardingBaseProfileId", MainViewModelDefaults.BaseSwfocProfileId);
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
        SetField(vm, "_supportBundleOutputDirectory", Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwfocTrainer", "support"));
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
    }

    private static void SetProp(object instance, string propName, object value)
    {
        var type = instance.GetType();
        PropertyInfo? prop = null;
        while (type is not null && prop is null)
        {
            prop = type.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        prop.Should().NotBeNull($"property '{propName}' should exist");
        prop!.SetValue(instance, value);
    }

    private static void SetField(object instance, string fieldName, object? value)
    {
        var type = instance.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        field.Should().NotBeNull($"field '{fieldName}' should exist");
        field!.SetValue(instance, value);
    }

    private static void InvokeProtected(object instance, string methodName, params object?[] args)
    {
        var method = FindMethod(instance, methodName, args);
        method.Invoke(instance, args);
    }

    private static T InvokeProtected<T>(object instance, string methodName, params object?[] args)
    {
        var method = FindMethod(instance, methodName, args);
        return (T)method.Invoke(instance, args)!;
    }

    private static async Task InvokeProtectedAsync(object instance, string methodName, params object?[] args)
    {
        var method = FindMethod(instance, methodName, args);
        var task = method.Invoke(instance, args.Length == 0 ? null : args);
        task.Should().BeAssignableTo<Task>();
        await (Task)task!;
    }

    private static MethodInfo FindMethod(object instance, string methodName, object?[] args)
    {
        var type = instance.GetType();
        MethodInfo? method = null;
        while (type is not null && method is null)
        {
            var candidates = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.Name == methodName);

            if (args.Length > 0)
            {
                var argTypes = args.Select(a => a?.GetType()).ToArray();
                method = candidates.FirstOrDefault(m =>
                {
                    var p = m.GetParameters();
                    if (p.Length != argTypes.Length) return false;
                    for (var i = 0; i < p.Length; i++)
                    {
                        if (argTypes[i] is null) continue;
                        if (!p[i].ParameterType.IsAssignableFrom(argTypes[i])) return false;
                    }
                    return true;
                }) ?? candidates.FirstOrDefault(m => m.GetParameters().Length == args.Length);
            }
            else
            {
                method = candidates.FirstOrDefault(m => m.GetParameters().Length == 0);
            }

            type = type.BaseType;
        }

        method.Should().NotBeNull($"method '{methodName}' should exist");
        return method!;
    }

    private static SaveDocument BuildSaveDocument()
    {
        var leaf = new SaveNode("credits", "credits", "Int32", 1000);
        var root = new SaveNode("root", "root", "root", null, new[] { leaf });
        return new SaveDocument(@"C:\test.sav", "test_schema", new byte[] { 1, 2, 3, 4 }, root);
    }

    private static SavePatchPack BuildPatchPack()
    {
        return new SavePatchPack(
            Metadata: new SavePatchMetadata("v1", "test_profile", "test_schema", "hash", DateTimeOffset.UtcNow),
            Compatibility: new SavePatchCompatibility(new[] { "test_profile" }, "test_schema"),
            Operations: new[]
            {
                new SavePatchOperation(SavePatchOperationKind.SetValue, "credits", "credits", "Int32", 1000, 5000, 0x10)
            });
    }

    private static AttachSession BuildSession(IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new AttachSession(
            ProfileId: "test_profile",
            Process: new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null,
                ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: metadata),
            Build: new ProfileBuild("test_profile", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    // ── Stubs ──

    private sealed class StubRuntimeAdapter : IRuntimeAdapter
    {
        private readonly AttachSession? _session;
        public StubRuntimeAdapter(AttachSession? session) => _session = session;
        public bool IsAttached => _session is not null;
        public AttachSession? CurrentSession => _session;
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken ct) => Task.FromResult(BuildProfile());
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken ct) => Task.FromResult(BuildProfile());
        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "test_profile" });

        private static TrainerProfile BuildProfile() => new(
            Id: "test_profile", DisplayName: "test", Inherits: null, ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null, SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test_schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());
    }

    private sealed class StubSaveCodec : ISaveCodec
    {
        private readonly bool _isValid;
        public StubSaveCodec(bool isValid = true) => _isValid = isValid;

        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken ct)
        {
            var leaf = new SaveNode("credits", "credits", "Int32", 1000);
            var root = new SaveNode("root", "root", "root", null, new[] { leaf });
            return Task.FromResult(new SaveDocument(path, schemaId, new byte[] { 1, 2, 3, 4 }, root));
        }

        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken ct) => Task.CompletedTask;
        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken ct) => Task.CompletedTask;
        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken ct)
            => Task.FromResult(new SaveValidationResult(
                _isValid,
                _isValid ? Array.Empty<string>() : new[] { "error" },
                Array.Empty<string>()));
        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken ct)
            => Task.FromResult(true);
    }

    private sealed class StubSavePatchApplyService : ISavePatchApplyService
    {
        private readonly bool _applied;
        private readonly bool _restored;

        public StubSavePatchApplyService(bool applied = false, bool restored = false)
        {
            _applied = applied;
            _restored = restored;
        }

        public Task<SavePatchApplyResult> ApplyAsync(string savePath, SavePatchPack pack, string profileId, bool strict, CancellationToken ct)
            => Task.FromResult(new SavePatchApplyResult(
                _applied ? SavePatchApplyClassification.Applied : SavePatchApplyClassification.ValidationFailed,
                _applied, _applied ? "applied" : "failed",
                BackupPath: @"C:\backup.sav",
                ReceiptPath: @"C:\receipt.json"));

        public Task<SaveRollbackResult> RestoreLastBackupAsync(string savePath, CancellationToken ct)
            => Task.FromResult(new SaveRollbackResult(
                _restored, _restored ? "restored" : "no backup found",
                BackupPath: @"C:\backup.sav"));
    }

    private sealed class StubProfileUpdateService : IProfileUpdateService
    {
        private readonly bool _succeeded;
        private readonly string? _reasonCode;
        private readonly string? _receiptPath;
        private readonly string? _backupPath;
        private readonly bool _rollbackRestored;
        private readonly string? _rollbackReasonCode;
        private readonly string? _rollbackBackupPath;

        public StubProfileUpdateService(
            bool succeeded = false,
            string? reasonCode = null,
            string? receiptPath = null,
            string? backupPath = null,
            bool rollbackRestored = false,
            string? rollbackReasonCode = null,
            string? rollbackBackupPath = null)
        {
            _succeeded = succeeded;
            _reasonCode = reasonCode;
            _receiptPath = receiptPath;
            _backupPath = backupPath;
            _rollbackRestored = rollbackRestored;
            _rollbackReasonCode = rollbackReasonCode;
            _rollbackBackupPath = rollbackBackupPath;
        }

        public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<string> InstallProfileAsync(string profileId, CancellationToken ct)
            => Task.FromResult(@"C:\installed");

        public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId, CancellationToken ct)
            => Task.FromResult(new ProfileInstallResult(
                Succeeded: _succeeded,
                ProfileId: profileId,
                InstalledPath: @"C:\installed",
                BackupPath: _backupPath,
                ReceiptPath: _receiptPath,
                Message: _succeeded ? "installed" : "failed",
                ReasonCode: _reasonCode));

        public Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken ct)
            => Task.FromResult(new ProfileRollbackResult(
                Restored: _rollbackRestored,
                ProfileId: profileId,
                RestoredPath: @"C:\restored",
                BackupPath: _rollbackBackupPath,
                Message: _rollbackRestored ? "rolled back" : "rollback failed",
                ReasonCode: _rollbackReasonCode));
    }

    private sealed class StubModCalibrationService : IModCalibrationService
    {
        private readonly bool _calibrationSucceeded;
        public StubModCalibrationService(bool calibrationSucceeded = true)
            => _calibrationSucceeded = calibrationSucceeded;

        public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(
            ModCalibrationArtifactRequest request, CancellationToken ct)
            => Task.FromResult(new ModCalibrationArtifactResult(
                Succeeded: _calibrationSucceeded,
                ArtifactPath: @"C:\artifact.json",
                ModuleFingerprint: "fingerprint",
                Candidates: Array.Empty<CalibrationCandidate>(),
                Warnings: Array.Empty<string>()));

        public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(
            TrainerProfile profile, AttachSession? session,
            DependencyValidationResult? dependencyValidation,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken ct)
            => Task.FromResult(new ModCompatibilityReport(
                ProfileId: "test",
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                RuntimeMode: RuntimeMode.Unknown,
                DependencyStatus: DependencyValidationStatus.Pass,
                UnresolvedCriticalSymbols: 0,
                PromotionReady: true,
                Actions: Array.Empty<ModActionCompatibility>(),
                Notes: Array.Empty<string>()));
    }
}
