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

public sealed class AppWave9CoverageTests
{
    [Fact]
    public void CanPreviewPatchPackContext_AllSet_ShouldReturnTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        vm.SelectedProfileId = "test";
        var method = typeof(MainViewModel).GetMethod("CanPreviewPatchPackContext", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        ((bool)method!.Invoke(vm, null)!).Should().BeTrue();
    }

    [Fact]
    public void CanApplyPatchPackContext_AllSet_ShouldReturnTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        SetField(vm, "_savePath", @"C:\test.sav");
        vm.SelectedProfileId = "test";
        var method = typeof(MainViewModel).GetMethod("CanApplyPatchPackContext", BindingFlags.NonPublic | BindingFlags.Instance);
        ((bool)method!.Invoke(vm, null)!).Should().BeTrue();
    }

    [Fact]
    public void CanExportPatchPackContext_AllSet_ShouldReturnTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", BuildSaveDocument());
        SetField(vm, "_loadedSaveOriginal", new byte[] { 1, 2, 3 });
        vm.SelectedProfileId = "test";
        var method = typeof(MainViewModel).GetMethod("CanExportPatchPackContext", BindingFlags.NonPublic | BindingFlags.Instance);
        ((bool)method!.Invoke(vm, null)!).Should().BeTrue();
    }

    [Fact]
    public void ApplyAttachSessionStatus_WithMixedSymbols_ShouldCountCorrectly()
    {
        var vm = CreateViewModel();
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy),
            ["timer"] = new("timer", (nint)0x2000, SymbolValueType.Int32, AddressSource.Fallback, HealthStatus: SymbolHealthStatus.Degraded),
            ["broken"] = new("broken", nint.Zero, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy),
            ["unresolved_addr"] = new("unresolved_addr", (nint)0x3000, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Unresolved),
        };
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(symbols), DateTimeOffset.UtcNow);
        Invoke(vm, "ApplyAttachSessionStatus", session);
        vm.ResolvedSymbolsCount.Should().Be(4);
        vm.Status.Should().Contain("sig=3").And.Contain("fallback=1").And.Contain("unresolved=2");
    }

    [Fact]
    public void ApplyPayloadTemplate_EmptyRequiredArray_ShouldNotChangePayload()
    {
        var vm = CreateViewModel();
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["no_payload"] = new("no_payload", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Sdk,
                new JsonObject { ["required"] = new JsonArray() }, false, 0)
        };
        SetField(vm, "_loadedActionSpecs", (IReadOnlyDictionary<string, ActionSpec>)actions);
        SetField(vm, "_selectedActionId", "no_payload");
        var original = vm.PayloadJson;
        Invoke(vm, "ApplyPayloadTemplateForSelectedAction");
        vm.PayloadJson.Should().Be(original);
    }

    [Fact]
    public async Task RecommendProfileIdAsync_WithRecommendation_ShouldReturnId()
    {
        var vm = CreateViewModel();
        var processes = new[] { new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic) };
        SetField(vm, "_processLocator", new StubProcessLocator(processes));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "base_swfoc" }));
        SetField(vm, "_launchContextResolver", new RecommendingLaunchContextResolver("base_swfoc"));
        var result = await InvokeAsyncWithResult<string?>(vm, "RecommendProfileIdAsync");
        result.Should().Be("base_swfoc");
    }

    [Fact]
    public async Task RecommendProfileIdAsync_NoProcesses_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        var result = await InvokeAsyncWithResult<string?>(vm, "RecommendProfileIdAsync");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAttachProfileAsync_NonUniversal_ShouldReturnSameId()
    {
        var vm = CreateViewModel();
        var result = await InvokeAsyncWithResult<(string, ProfileVariantResolution?)>(vm, "ResolveAttachProfileAsync", "base_swfoc");
        result.Item1.Should().Be("base_swfoc");
        result.Item2.Should().BeNull();
    }

    [Fact]
    public async Task EnsureActionAvailable_Unavailable_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        var session = BuildSessionWithUnresolvedSymbol();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = new("set_credits", ActionCategory.Economy, RuntimeMode.Unknown, ExecutionKind.Sdk,
                new JsonObject { ["required"] = new JsonArray("symbol", "intValue") }, false, 0)
        };
        SetField(vm, "_loadedActionSpecs", (IReadOnlyDictionary<string, ActionSpec>)actions);
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        vm.SelectedProfileId = "test";
        var result = await InvokeAsyncWithResult<bool>(vm, "EnsureActionAvailableForCurrentSessionAsync", "set_credits", "Test");
        result.Should().BeFalse();
        vm.Status.Should().Contain("Test");
    }

    [Fact]
    public void ToggleQuickActionState_NullKey_ShouldNotThrow()
    {
        var vm = CreateViewModel();
        var method = FindMethod(vm, "ToggleQuickActionState", new object?[] { null, true });
        var act = () => method.Invoke(vm, new object?[] { null, true });
        act.Should().NotThrow();
    }

    [Fact]
    public void ToggleQuickActionState_Failed_ShouldNotModifyToggles()
    {
        var vm = CreateViewModel();
        var method = FindMethod(vm, "ToggleQuickActionState", new object?[] { "test_key", false });
        var act = () => method.Invoke(vm, new object?[] { "test_key", false });
        act.Should().NotThrow();
    }

    [Fact]
    public void ResolveHotkeyBinding_NoMatch_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        vm.Hotkeys.Clear();
        FindMethod(vm, "ResolveHotkeyBinding", new object[] { "Ctrl+Z" }).Invoke(vm, new object[] { "Ctrl+Z" }).Should().BeNull();
    }

    [Fact]
    public void ResolveHotkeyBinding_EmptyActionId_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        vm.Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Z", ActionId = "" });
        FindMethod(vm, "ResolveHotkeyBinding", new object[] { "Ctrl+Z" }).Invoke(vm, new object[] { "Ctrl+Z" }).Should().BeNull();
    }

    [Fact]
    public void RuntimeModeOverrideHelpers_SaveAndLoad_Roundtrip()
    {
        MainViewModelRuntimeModeOverrideHelpers.Save("Galactic");
        MainViewModelRuntimeModeOverrideHelpers.Load().Should().Be("Galactic");
        MainViewModelRuntimeModeOverrideHelpers.Save("Auto");
    }

    [Theory]
    [InlineData("Galactic", RuntimeMode.Galactic)]
    [InlineData("AnyTactical", RuntimeMode.AnyTactical)]
    [InlineData("TacticalLand", RuntimeMode.TacticalLand)]
    [InlineData("TacticalSpace", RuntimeMode.TacticalSpace)]
    [InlineData("Auto", RuntimeMode.Galactic)]
    [InlineData(null, RuntimeMode.Galactic)]
    public void ResolveEffectiveRuntimeMode_ShouldReturnExpected(string? modeOverride, RuntimeMode expected)
    {
        MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Galactic, modeOverride).Should().Be(expected);
    }

    [Fact]
    public async Task WriteSaveAsync_NoLoadedSave_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);
        await InvokeAsync(vm, "WriteSaveAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public void PreparePatchPreview_VariantMismatch_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["resolvedVariant"] = "base_swfoc" };
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: md),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
        SetField(vm, "_runtime", new StubRuntime(session: session));
        Invoke<bool>(vm, "PreparePatchPreview", "custom_mod").Should().BeFalse();
    }

    [Fact]
    public async Task ApplyPatchPackAsync_VariantMismatch_ShouldBlock()
    {
        var vm = CreateViewModel();
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["resolvedVariant"] = "base_swfoc" };
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: md),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_loadedPatchPack", BuildPatchPack());
        vm.SelectedProfileId = "custom_mod";
        await InvokeAsync(vm, "ApplyPatchPackAsync");
        vm.Status.Should().Contain("save_variant_mismatch");
    }

    [Fact]
    public async Task ApplyPatchPackAsync_NoPack_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", null);
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "ApplyPatchPackAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task ScaffoldModProfileAsync_EmptyBase_ShouldFallback()
    {
        var vm = CreateViewModel();
        SetField(vm, "_modOnboarding", new StubModOnboarding());
        SetField(vm, "_onboardingDraftProfileId", "ct");
        SetField(vm, "_onboardingDisplayName", "CT");
        SetField(vm, "_onboardingLaunchSample", @"C:\game.exe");
        SetField(vm, "_onboardingBaseProfileId", "");
        SetField(vm, "_onboardingNamespaceRoot", "c");
        await InvokeAsync(vm, "ScaffoldModProfileAsync");
        vm.Status.Should().Contain("Draft profile scaffolded");
    }

    [Fact]
    public async Task ScaffoldModProfileAsync_Warnings_ShouldInclude()
    {
        var vm = CreateViewModel();
        SetField(vm, "_modOnboarding", new StubModOnboardingWithWarnings());
        SetField(vm, "_onboardingDraftProfileId", "ct");
        SetField(vm, "_onboardingDisplayName", "CT");
        SetField(vm, "_onboardingLaunchSample", "");
        SetField(vm, "_onboardingBaseProfileId", "base_swfoc");
        SetField(vm, "_onboardingNamespaceRoot", "c");
        await InvokeAsync(vm, "ScaffoldModProfileAsync");
        vm.OnboardingSummary.Should().Contain("warn1");
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_EmptyProfile_UsesDraft()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "cd" }));
        SetField(vm, "_modCalibration", new StubModCalibration());
        SetField(vm, "_runtime", new StubRuntime(session: null));
        vm.SelectedProfileId = "";
        SetField(vm, "_onboardingDraftProfileId", "cd");
        await InvokeAsync(vm, "BuildCompatibilityReportAsync");
        vm.Status.Should().Contain("cd");
    }

    [Fact]
    public async Task RefreshActionReliability_CatalogJsonException_ShouldContinue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_catalog", new ThrowingCatalog(new JsonException("bad")));
        SetField(vm, "_actionReliability", new StubActionReliability());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "RefreshActionReliabilityAsync");
    }

    [Fact]
    public async Task RefreshActionReliability_CatalogIOException_ShouldContinue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_catalog", new ThrowingCatalog(new IOException("fail")));
        SetField(vm, "_actionReliability", new StubActionReliability());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "RefreshActionReliabilityAsync");
    }

    [Fact]
    public async Task RefreshActionReliability_CatalogInvalidOp_ShouldContinue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_catalog", new ThrowingCatalog(new InvalidOperationException("fail")));
        SetField(vm, "_actionReliability", new StubActionReliability());
        vm.SelectedProfileId = "test";
        await InvokeAsync(vm, "RefreshActionReliabilityAsync");
    }

    [Fact]
    public async Task ApplySelectedUnitDraftAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "ApplySelectedUnitDraftAsync");
        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task LoadSpawnPresetsAsync_EmptyProfile_ReturnsEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "LoadSpawnPresetsAsync");
        vm.SpawnPresets.Should().BeEmpty();
    }

    [Fact]
    public async Task RunSpawnBatchAsync_NoProfile_ShouldFail()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "";
        await InvokeAsync(vm, "RunSpawnBatchAsync");
        vm.Status.Should().NotBe("Ready");
    }

    [Fact]
    public void GetMetadataValueOrDefault_KeyExists_ReturnsValue()
    {
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["k"] = "v" };
        var method = typeof(MainViewModelLiveOpsBase).GetMethod("GetMetadataValueOrDefault", BindingFlags.NonPublic | BindingFlags.Static);
        ((string)method!.Invoke(null, new object[] { md, "k", "fb" })!).Should().Be("v");
    }

    [Fact]
    public void GetMetadataValueOrDefault_KeyMissing_ReturnsFallback()
    {
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var method = typeof(MainViewModelLiveOpsBase).GetMethod("GetMetadataValueOrDefault", BindingFlags.NonPublic | BindingFlags.Static);
        ((string)method!.Invoke(null, new object[] { md, "x", "fb" })!).Should().Be("fb");
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_WhitespaceVariant_ReturnsNull()
    {
        var vm = CreateViewModel();
        var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["resolvedVariant"] = "   " };
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: md),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
        SetField(vm, "_runtime", new StubRuntime(session: session));
        Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "test").Should().BeNull();
    }

    [Fact]
    public void CreateLiveOpsCommands_NotAttached_CanExecuteFalse()
    {
        var ctx = BuildLiveOpsContext(false, false);
        MainViewModelFactories.CreateLiveOpsCommands(ctx).RefreshActionReliability.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CreateLiveOpsCommands_Attached_CanExecuteTrue()
    {
        var ctx = BuildLiveOpsContext(true, true);
        MainViewModelFactories.CreateLiveOpsCommands(ctx).RefreshActionReliability.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncCommand_InvalidOp_Catches()
    {
        var cmd = new AsyncCommand(() => throw new InvalidOperationException("t"));
        var m = typeof(AsyncCommand).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)m!.Invoke(cmd, new object?[] { null })!;
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncCommand_Win32_Catches()
    {
        var cmd = new AsyncCommand(() => throw new System.ComponentModel.Win32Exception("t"));
        var m = typeof(AsyncCommand).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)m!.Invoke(cmd, new object?[] { null })!;
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncCommand_IO_Catches()
    {
        var cmd = new AsyncCommand(() => throw new IOException("t"));
        var m = typeof(AsyncCommand).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)m!.Invoke(cmd, new object?[] { null })!;
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task AsyncCommand_KeyNotFound_Catches()
    {
        var cmd = new AsyncCommand(() => throw new KeyNotFoundException("t"));
        var m = typeof(AsyncCommand).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)m!.Invoke(cmd, new object?[] { null })!;
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AsyncCommand_IsRunning_CanExecuteFalse()
    {
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncCommand(() => tcs.Task);
        cmd.Execute(null);
        cmd.CanExecute(null).Should().BeFalse();
        tcs.SetResult();
    }

    [Fact]
    public void FeatureGate_NonGated_ReturnsNull()
    {
        var m = typeof(MainViewModel).GetMethod("ResolveProfileFeatureGateReason", BindingFlags.NonPublic | BindingFlags.Static)!;
        m.Invoke(null, new object[] { "set_credits", BuildMinimalProfile() }).Should().BeNull();
    }

    [Fact]
    public void FeatureGate_Enabled_ReturnsNull()
    {
        var m = typeof(MainViewModel).GetMethod("ResolveProfileFeatureGateReason", BindingFlags.NonPublic | BindingFlags.Static)!;
        var ff = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["allow_fog_patch_fallback"] = true };
        m.Invoke(null, new object[] { "toggle_fog_reveal_patch_fallback", BuildMinimalProfile(ff) }).Should().BeNull();
    }

    [Fact]
    public void FeatureGate_Disabled_ReturnsMessage()
    {
        var m = typeof(MainViewModel).GetMethod("ResolveProfileFeatureGateReason", BindingFlags.NonPublic | BindingFlags.Static)!;
        var ff = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["allow_fog_patch_fallback"] = false };
        ((string?)m.Invoke(null, new object[] { "toggle_fog_reveal_patch_fallback", BuildMinimalProfile(ff) })).Should().Contain("fallback action");
    }

    [Fact]
    public void FeatureGate_Extender_Gates()
    {
        var m = typeof(MainViewModel).GetMethod("ResolveProfileFeatureGateReason", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string?)m.Invoke(null, new object[] { "set_credits_extender_experimental", BuildMinimalProfile() })).Should().Contain("allow_extender_credits");
    }

    [Fact]
    public void FeatureGate_UnitCap_Gates()
    {
        var m = typeof(MainViewModel).GetMethod("ResolveProfileFeatureGateReason", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((string?)m.Invoke(null, new object[] { "set_unit_cap_patch_fallback", BuildMinimalProfile() })).Should().Contain("fallback action");
    }

    [Fact]
    public void RefreshSelectedUnitTransactions_WithHistory_Populates()
    {
        var vm = CreateViewModel();
        SetField(vm, "_selectedUnitTransactions", new StubSelectedUnitTransactionsWithHistory());
        Invoke(vm, "RefreshSelectedUnitTransactions");
        vm.SelectedUnitTransactions.Should().NotBeEmpty();
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
        => new("test", "Test", null, ExeTarget.Swfoc, null, Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), ff ?? new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(), new Dictionary<string, string>());

    private static MainViewModelLiveOpsCommandContext BuildLiveOpsContext(bool attached, bool profile) => new()
    {
        RefreshActionReliabilityAsync = () => Task.CompletedTask, CaptureSelectedUnitBaselineAsync = () => Task.CompletedTask,
        ApplySelectedUnitDraftAsync = () => Task.CompletedTask, RevertSelectedUnitTransactionAsync = () => Task.CompletedTask,
        RestoreSelectedUnitBaselineAsync = () => Task.CompletedTask, LoadSpawnPresetsAsync = () => Task.CompletedTask,
        RunSpawnBatchAsync = () => Task.CompletedTask, ScaffoldModProfileAsync = () => Task.CompletedTask,
        ExportCalibrationArtifactAsync = () => Task.CompletedTask, BuildModCompatibilityReportAsync = () => Task.CompletedTask,
        ExportSupportBundleAsync = () => Task.CompletedTask, ExportTelemetrySnapshotAsync = () => Task.CompletedTask,
        CanRunSpawnBatch = () => attached, CanScaffoldModProfile = () => attached, CanUseSupportBundleOutputDirectory = () => attached,
        IsAttached = () => attached, CanUseSelectedProfile = () => profile
    };

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

    private static void SetProp(object o, string n, object v)
    {
        var t = o.GetType(); PropertyInfo? p = null;
        while (t is not null && p is null) { p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); t = t.BaseType; }
        p!.SetValue(o, v);
    }

    private static void SetField(object o, string n, object? v)
    {
        var t = o.GetType(); FieldInfo? f = null;
        while (t is not null && f is null) { f = t.GetField(n, BindingFlags.Instance | BindingFlags.NonPublic); t = t.BaseType; }
        f!.SetValue(o, v);
    }

    private static void Invoke(object o, string n, params object?[] a) => FindMethod(o, n, a).Invoke(o, a.Length == 0 ? null : a);
    private static T? Invoke<T>(object o, string n, params object?[] a) { var r = FindMethod(o, n, a).Invoke(o, a.Length == 0 ? null : a); return r is T t ? t : default; }
    private static async Task InvokeAsync(object o, string n, params object?[] a) => await (Task)FindMethod(o, n, a).Invoke(o, a.Length == 0 ? null : a)!;
    private static async Task<T?> InvokeAsyncWithResult<T>(object o, string n, params object?[] a)
    {
        var task = FindMethod(o, n, a).Invoke(o, a.Length == 0 ? null : a);
        if (task is Task<T> tt) return await tt;
        await (Task)task!;
        var rp = task.GetType().GetProperty("Result");
        return rp is not null ? (T?)rp.GetValue(task) : default;
    }

    private static MethodInfo FindMethod(object o, string n, object?[] a)
    {
        var t = o.GetType(); MethodInfo? m = null;
        while (t is not null && m is null) { var c = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(x => x.Name == n).ToArray(); m = c.FirstOrDefault(x => x.GetParameters().Length == a.Length) ?? c.FirstOrDefault(x => x.GetParameters().Length == 0 && a.Length == 0); t = t.BaseType; }
        m.Should().NotBeNull($"method '{n}' should exist"); return m!;
    }

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

    private static TrainerOrchestrator CreateOrchestrator(bool ok) => new(new FullStubProfiles(new[] { "test" }), new StubExecutionRuntime(ok), new StubFreezeService(), new StubAuditLogger(), new StubTelemetry());

    // ── Stubs ──
    private sealed class StubRuntime : IRuntimeAdapter
    {
        private readonly AttachSession? _s; public StubRuntime(AttachSession? session) => _s = session;
        public bool IsAttached => _s is not null; public AttachSession? CurrentSession => _s;
        public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class StubExecutionRuntime : IRuntimeAdapter
    {
        private readonly bool _ok; public StubExecutionRuntime(bool ok) => _ok = ok;
        public bool IsAttached => true; public AttachSession? CurrentSession => null;
        public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest r, CancellationToken ct) => Task.FromResult(new ActionExecutionResult(_ok, _ok ? "ok" : "failed", AddressSource.Signature));
        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class FullStubProfiles : IProfileRepository
    {
        private readonly IReadOnlyList<string> _ids; public FullStubProfiles(IReadOnlyList<string> ids) => _ids = ids;
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build(id));
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build(id));
        public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct) => Task.FromResult(_ids);
        private static TrainerProfile Build(string id) => new(id, id, null, ExeTarget.Swfoc, null, Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new("set_credits", ActionCategory.Economy, RuntimeMode.Unknown, ExecutionKind.Sdk, new JsonObject { ["required"] = new JsonArray("symbol", "intValue") }, false, 0),
                ["freeze_timer"] = new("freeze_timer", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Sdk, new JsonObject { ["required"] = new JsonArray("symbol", "boolValue") }, false, 0),
            }, new Dictionary<string, bool>(), Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(), new Dictionary<string, string>());
    }
    private sealed class StubProcessLocator : IProcessLocator
    {
        private readonly IReadOnlyList<ProcessMetadata> _p; public StubProcessLocator(IReadOnlyList<ProcessMetadata> p) => _p = p;
        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken ct) => Task.FromResult(_p);
        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget t, CancellationToken ct) => Task.FromResult(_p.FirstOrDefault(x => x.ExeTarget == t));
    }
    private sealed class StubLaunchContextResolver : ILaunchContextResolver
    { public LaunchContext Resolve(ProcessMetadata p, IReadOnlyList<TrainerProfile> pr) => new(LaunchKind.Unknown, false, Array.Empty<string>(), null, null, "stub", new ProfileRecommendation(null, "none", 0.0)); }
    private sealed class RecommendingLaunchContextResolver : ILaunchContextResolver
    {
        private readonly string _id; public RecommendingLaunchContextResolver(string id) => _id = id;
        public LaunchContext Resolve(ProcessMetadata p, IReadOnlyList<TrainerProfile> pr) => new(LaunchKind.Workshop, true, Array.Empty<string>(), null, null, "found", new ProfileRecommendation(_id, "matched", 0.95));
    }
    private sealed class StubProfileVariantResolver : IProfileVariantResolver
    {
        private readonly ProfileVariantResolution? _r; public StubProfileVariantResolver(ProfileVariantResolution? r) => _r = r;
        public Task<ProfileVariantResolution> ResolveAsync(string id, CancellationToken ct) => Task.FromResult(_r ?? new ProfileVariantResolution(id, id, "none", 0.0));
        public Task<ProfileVariantResolution> ResolveAsync(string id, IReadOnlyList<ProcessMetadata>? p, CancellationToken ct) => Task.FromResult(_r ?? new ProfileVariantResolution(id, id, "none", 0.0));
    }
    private sealed class StubGameLauncher : IGameLaunchService { private readonly bool _ok; public StubGameLauncher(bool ok) => _ok = ok; public Task<GameLaunchResult> LaunchAsync(GameLaunchRequest r, CancellationToken ct) => Task.FromResult(new GameLaunchResult(_ok, _ok ? "ok" : "failed", 123, @"C:\game\swfoc.exe", "")); }
    private sealed class StubCatalog : ICatalogService { public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string id, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(new Dictionary<string, IReadOnlyList<string>> { ["units"] = new[] { "a", "b" } }); }
    private sealed class ThrowingCatalog : ICatalogService { private readonly Exception _ex; public ThrowingCatalog(Exception ex) => _ex = ex; public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string id, CancellationToken ct) => throw _ex; }
    private sealed class StubSaveCodec : ISaveCodec { public Task<SaveDocument> LoadAsync(string p, string s, CancellationToken ct) => throw new NotImplementedException(); public Task EditAsync(SaveDocument d, string n, object? v, CancellationToken ct) => Task.CompletedTask; public Task<SaveValidationResult> ValidateAsync(SaveDocument d, CancellationToken ct) => throw new NotImplementedException(); public Task WriteAsync(SaveDocument d, string o, CancellationToken ct) => Task.CompletedTask; public Task<bool> RoundTripCheckAsync(SaveDocument d, CancellationToken ct) => Task.FromResult(true); }
    private sealed class StubSavePatchPackService : ISavePatchPackService { public Task<SavePatchPack> ExportAsync(SaveDocument o, SaveDocument m, string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchPack> LoadPackAsync(string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => throw new NotImplementedException(); public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pk, SaveDocument t, string p, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class StubSavePatchApplyService : ISavePatchApplyService { public Task<SavePatchApplyResult> ApplyAsync(string s, SavePatchPack pk, string p, bool st, CancellationToken ct) => throw new NotImplementedException(); public Task<SaveRollbackResult> RestoreLastBackupAsync(string s, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class StubHelperMod : IHelperModService { public Task<string> DeployAsync(string id, CancellationToken ct) => Task.FromResult(@"C:\h.dll"); public Task<bool> VerifyAsync(string id, CancellationToken ct) => Task.FromResult(true); }
    private sealed class StubProfileUpdates : IProfileUpdateService
    {
        private readonly IReadOnlyList<string> _u; public StubProfileUpdates(IReadOnlyList<string> u) => _u = u;
        public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken ct) => Task.FromResult(_u);
        public Task<string> InstallProfileAsync(string id, CancellationToken ct) => Task.FromResult(@"C:\p.json");
        public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string id, CancellationToken ct) => Task.FromResult(new ProfileInstallResult(true, id, @"C:\p.json", @"C:\b.json", @"C:\r.json", "ok", null));
        public Task<ProfileRollbackResult> RollbackLastInstallAsync(string id, CancellationToken ct) => Task.FromResult(new ProfileRollbackResult(true, id, @"C:\p.json", @"C:\b.json", "ok", null));
    }
    private sealed class StubModOnboarding : IModOnboardingService { public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest r, CancellationToken ct) => Task.FromResult(new ModOnboardingResult(true, r.DraftProfileId, @"C:\d.json", new[] { "ws" }, new[] { @"C:\" }, new[] { r.DraftProfileId }, Array.Empty<string>())); public Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(ModOnboardingSeedBatchRequest r, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class StubModOnboardingWithWarnings : IModOnboardingService { public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest r, CancellationToken ct) => Task.FromResult(new ModOnboardingResult(true, r.DraftProfileId, @"C:\d.json", new[] { "ws" }, new[] { @"C:\" }, new[] { r.DraftProfileId }, new[] { "warn1", "warn2" })); public Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(ModOnboardingSeedBatchRequest r, CancellationToken ct) => throw new NotImplementedException(); }
    private sealed class StubModCalibration : IModCalibrationService { public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest r, CancellationToken ct) => Task.FromResult(new ModCalibrationArtifactResult(true, @"C:\c.json", "X", Array.Empty<CalibrationCandidate>(), Array.Empty<string>())); public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(TrainerProfile p, AttachSession? s, DependencyValidationResult? d, IReadOnlyDictionary<string, IReadOnlyList<string>>? c, CancellationToken ct) => Task.FromResult(new ModCompatibilityReport(p.Id, DateTimeOffset.UtcNow, RuntimeMode.Unknown, DependencyValidationStatus.Pass, 0, true, Array.Empty<ModActionCompatibility>(), Array.Empty<string>())); }
    private sealed class StubSupportBundles : ISupportBundleService { private readonly bool _ok; public StubSupportBundles(bool ok) => _ok = ok; public Task<SupportBundleResult> ExportAsync(SupportBundleRequest r, CancellationToken ct) => Task.FromResult(new SupportBundleResult(_ok, @"C:\b.zip", @"C:\m.json", Array.Empty<string>(), Array.Empty<string>())); }
    private sealed class StubFreezeService : IValueFreezeService { public void FreezeInt(string s, int v) { } public void FreezeIntAggressive(string s, int v) { } public void FreezeFloat(string s, float v) { } public void FreezeBool(string s, bool v) { } public bool Unfreeze(string s) => false; public void UnfreezeAll() { } public bool IsFrozen(string s) => false; public IReadOnlyCollection<string> GetFrozenSymbols() => Array.Empty<string>(); public void Dispose() { } }
    private sealed class StubAuditLogger : IAuditLogger { public Task WriteAsync(ActionAuditRecord r, CancellationToken ct) => Task.CompletedTask; }
    private sealed class StubTelemetry : ITelemetrySnapshotService { public void RecordAction(string a, AddressSource s, bool ok) { } public TelemetrySnapshot CreateSnapshot() => new(DateTimeOffset.UtcNow, new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), 0, 0, 0, 0); public Task<string> ExportSnapshotAsync(string d, CancellationToken ct) => Task.FromResult(Path.Combine(d, "t.json")); public void Reset() { } }
    private sealed class StubActionReliability : IActionReliabilityService { public IReadOnlyList<ActionReliabilityInfo> Evaluate(TrainerProfile p, AttachSession s, IReadOnlyDictionary<string, IReadOnlyList<string>>? c) => Array.Empty<ActionReliabilityInfo>(); }
    private sealed class StubSelectedUnitTransactions : ISelectedUnitTransactionService
    {
        public SelectedUnitSnapshot? Baseline => null; public IReadOnlyList<SelectedUnitTransactionRecord> History => Array.Empty<SelectedUnitTransactionRecord>();
        public Task<SelectedUnitSnapshot> CaptureAsync(CancellationToken ct) => Task.FromResult(new SelectedUnitSnapshot(100f, 50f, 10f, 1f, 1f, 0, 0, DateTimeOffset.UtcNow));
        public Task<SelectedUnitTransactionResult> ApplyAsync(string p, SelectedUnitDraft d, RuntimeMode m, CancellationToken ct) => Task.FromResult(new SelectedUnitTransactionResult(true, "ok", "tx1", Array.Empty<ActionExecutionResult>()));
        public Task<SelectedUnitTransactionResult> RevertLastAsync(string p, RuntimeMode m, CancellationToken ct) => Task.FromResult(new SelectedUnitTransactionResult(true, "ok", "tx1", Array.Empty<ActionExecutionResult>()));
        public Task<SelectedUnitTransactionResult> RestoreBaselineAsync(string p, RuntimeMode m, CancellationToken ct) => Task.FromResult(new SelectedUnitTransactionResult(true, "ok", "tx1", Array.Empty<ActionExecutionResult>()));
    }
    private sealed class StubSelectedUnitTransactionsWithHistory : ISelectedUnitTransactionService
    {
        private static readonly SelectedUnitSnapshot S = new(100f, 50f, 10f, 1f, 1f, 0, 0, DateTimeOffset.UtcNow);
        public SelectedUnitSnapshot? Baseline => S; public IReadOnlyList<SelectedUnitTransactionRecord> History => new[] { new SelectedUnitTransactionRecord("tx1", DateTimeOffset.UtcNow, S, S, false, "Applied", new[] { "a" }), new SelectedUnitTransactionRecord("tx2", DateTimeOffset.UtcNow.AddSeconds(-1), S, S, true, "Reverted", new[] { "r" }) };
        public Task<SelectedUnitSnapshot> CaptureAsync(CancellationToken ct) => Task.FromResult(S);
        public Task<SelectedUnitTransactionResult> ApplyAsync(string p, SelectedUnitDraft d, RuntimeMode m, CancellationToken ct) => Task.FromResult(new SelectedUnitTransactionResult(true, "ok", "tx1", Array.Empty<ActionExecutionResult>()));
        public Task<SelectedUnitTransactionResult> RevertLastAsync(string p, RuntimeMode m, CancellationToken ct) => Task.FromResult(new SelectedUnitTransactionResult(true, "ok", "tx1", Array.Empty<ActionExecutionResult>()));
        public Task<SelectedUnitTransactionResult> RestoreBaselineAsync(string p, RuntimeMode m, CancellationToken ct) => Task.FromResult(new SelectedUnitTransactionResult(true, "ok", "tx1", Array.Empty<ActionExecutionResult>()));
    }
    private sealed class StubSpawnPresets : ISpawnPresetService { public Task<IReadOnlyList<SpawnPreset>> LoadPresetsAsync(string p, CancellationToken ct) => Task.FromResult<IReadOnlyList<SpawnPreset>>(Array.Empty<SpawnPreset>()); public SpawnBatchPlan BuildBatchPlan(string p, SpawnPreset pr, int q, int d, string? f, string? e, bool s) => throw new NotImplementedException(); public Task<SpawnBatchExecutionResult> ExecuteBatchAsync(string p, SpawnBatchPlan pl, RuntimeMode m, CancellationToken ct) => throw new NotImplementedException(); }
}
