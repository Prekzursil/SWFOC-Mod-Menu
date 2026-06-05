using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Full branch coverage for MainViewModelLiveOpsBase —
/// RefreshActionReliability, RefreshLiveOpsDiagnostics, SelectedUnit ops,
/// spawn operations, and BuildActionContext.
/// </summary>
public sealed class MainViewModelLiveOpsCoverageTests
{
    // ── RefreshActionReliabilityAsync ──

    [Fact]
    public async Task RefreshActionReliabilityAsync_NullProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;
        SetField(vm, "_runtime", new StubRuntime(session: null));

        await InvokeAsync(vm, "RefreshActionReliabilityAsync");

        vm.ActionReliability.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshActionReliabilityAsync_WhitespaceProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";
        SetField(vm, "_runtime", new StubRuntime(session: null));

        await InvokeAsync(vm, "RefreshActionReliabilityAsync");

        vm.ActionReliability.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshActionReliabilityAsync_NullSession_ShouldReturnEarlyAfterProfileCheck()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_runtime", new StubRuntime(session: null));

        await InvokeAsync(vm, "RefreshActionReliabilityAsync");

        vm.ActionReliability.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshActionReliabilityAsync_WithSession_ShouldPopulateReliability()
    {
        var session = BuildSession();
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_profiles", new StubProfiles());
        SetField(vm, "_catalog", new StubCatalog());
        SetField(vm, "_actionReliability", new StubActionReliability());

        await InvokeAsync(vm, "RefreshActionReliabilityAsync");

        vm.ActionReliability.Should().HaveCount(1);
        vm.ActionReliability[0].ActionId.Should().Be("set_credits");
    }

    [Fact]
    public async Task RefreshActionReliabilityAsync_CatalogIOException_ShouldContinue()
    {
        var session = BuildSession();
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_profiles", new StubProfiles());
        SetField(vm, "_catalog", new ThrowingCatalog(new IOException("disk error")));
        SetField(vm, "_actionReliability", new StubActionReliability());

        await InvokeAsync(vm, "RefreshActionReliabilityAsync");

        vm.ActionReliability.Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshActionReliabilityAsync_CatalogInvalidOperationException_ShouldContinue()
    {
        var session = BuildSession();
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_profiles", new StubProfiles());
        SetField(vm, "_catalog", new ThrowingCatalog(new InvalidOperationException("bad state")));
        SetField(vm, "_actionReliability", new StubActionReliability());

        await InvokeAsync(vm, "RefreshActionReliabilityAsync");

        vm.ActionReliability.Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshActionReliabilityAsync_CatalogJsonException_ShouldContinue()
    {
        var session = BuildSession();
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_profiles", new StubProfiles());
        SetField(vm, "_catalog", new ThrowingCatalog(new System.Text.Json.JsonException("bad json")));
        SetField(vm, "_actionReliability", new StubActionReliability());

        await InvokeAsync(vm, "RefreshActionReliabilityAsync");

        vm.ActionReliability.Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshActionReliabilityAsync_SessionBecomesNull_ShouldReturnEarly()
    {
        // Simulates session going null between profile load and reliability eval
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        var runtime = new StubRuntime(session: BuildSession());
        SetField(vm, "_runtime", runtime);
        SetField(vm, "_profiles", new StubProfiles());
        // Catalog that nulls out the session mid-way
        SetField(vm, "_catalog", new SessionNullingCatalog(runtime));
        SetField(vm, "_actionReliability", new StubActionReliability());

        await InvokeAsync(vm, "RefreshActionReliabilityAsync");

        // Session was nulled after catalog load, so reliability should be empty
        vm.ActionReliability.Should().BeEmpty();
    }

    // ── RefreshLiveOpsDiagnostics ──

    [Fact]
    public void RefreshLiveOpsDiagnostics_NullSession_ShouldClearAndReturn()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: null));

        Invoke(vm, "RefreshLiveOpsDiagnostics");

        vm.LiveOpsDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void RefreshLiveOpsDiagnostics_WithSession_ShouldPopulateModeAndLaunch()
    {
        var session = BuildSession();
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: session));

        Invoke(vm, "RefreshLiveOpsDiagnostics");

        vm.LiveOpsDiagnostics.Should().Contain(x => x.StartsWith("mode:"));
        vm.LiveOpsDiagnostics.Should().Contain(x => x.StartsWith("launch:"));
        vm.LiveOpsDiagnostics.Should().Contain(x => x.StartsWith("symbols:"));
    }

    [Fact]
    public void RefreshLiveOpsDiagnostics_WithMetadataAndModeReason_ShouldIncludeModeReason()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeModeReasonCode"] = "auto_detect"
        });
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: session));

        Invoke(vm, "RefreshLiveOpsDiagnostics");

        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("mode_reason: auto_detect"));
    }

    [Fact]
    public void RefreshLiveOpsDiagnostics_WithResolvedVariant_ShouldIncludeVariantLine()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "roe",
            ["resolvedVariantReasonCode"] = "workshop_match",
            ["resolvedVariantConfidence"] = "0.95"
        });
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: session));

        Invoke(vm, "RefreshLiveOpsDiagnostics");

        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("variant: roe"));
        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("workshop_match"));
    }

    [Fact]
    public void RefreshLiveOpsDiagnostics_WithDependencyValidation_ShouldIncludeDependencyLine()
    {
        var session = BuildSession(metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dependencyValidation"] = "SoftFail",
            ["dependencyValidationMessage"] = "missing parent"
        });
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: session));

        Invoke(vm, "RefreshLiveOpsDiagnostics");

        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("dependency: SoftFail (missing parent)"));
    }

    [Fact]
    public void RefreshLiveOpsDiagnostics_NullMetadata_ShouldNotIncludeModeReasonOrVariant()
    {
        var session = BuildSession(metadata: null);
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: session));

        Invoke(vm, "RefreshLiveOpsDiagnostics");

        vm.LiveOpsDiagnostics.Should().NotContain(x => x.Contains("mode_reason:"));
        vm.LiveOpsDiagnostics.Should().NotContain(x => x.Contains("variant:"));
    }

    [Fact]
    public void RefreshLiveOpsDiagnostics_WithNullLaunchContext_ShouldShowUnknownLaunchKind()
    {
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null,
            ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: null, LaunchContext: null);
        var session = new AttachSession("test", process,
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: session));

        Invoke(vm, "RefreshLiveOpsDiagnostics");

        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("launch: Unknown"));
        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("recommendation: none"));
    }

    // ── CaptureSelectedUnitBaselineAsync ──

    [Fact]
    public async Task CaptureSelectedUnitBaselineAsync_NotAttached_ShouldSetNotAttachedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: null));

        await InvokeAsync(vm, "CaptureSelectedUnitBaselineAsync");

        vm.Status.Should().Contain("Not attached to game");
    }

    [Fact]
    public async Task CaptureSelectedUnitBaselineAsync_Success_ShouldApplySnapshot()
    {
        var session = BuildSession();
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_selectedUnitTransactions", new StubSelectedUnitService());

        await InvokeAsync(vm, "CaptureSelectedUnitBaselineAsync");

        vm.Status.Should().Contain("Selected-unit baseline captured");
    }

    [Fact]
    public async Task CaptureSelectedUnitBaselineAsync_InvalidOperation_ShouldSetFailureStatus()
    {
        var session = BuildSession();
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_selectedUnitTransactions",
            new StubSelectedUnitService(captureThrows: new InvalidOperationException("not ready")));

        await InvokeAsync(vm, "CaptureSelectedUnitBaselineAsync");

        vm.Status.Should().Contain("Capture selected-unit baseline failed");
    }

    [Fact]
    public async Task CaptureSelectedUnitBaselineAsync_Win32Exception_ShouldSetFailureStatus()
    {
        var session = BuildSession();
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_selectedUnitTransactions",
            new StubSelectedUnitService(captureThrows: new System.ComponentModel.Win32Exception("access denied")));

        await InvokeAsync(vm, "CaptureSelectedUnitBaselineAsync");

        vm.Status.Should().Contain("Capture selected-unit baseline failed");
    }

    // ── ApplySelectedUnitDraftAsync ──

    [Fact]
    public async Task ApplySelectedUnitDraftAsync_NullProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "ApplySelectedUnitDraftAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task ApplySelectedUnitDraftAsync_EmptyDraft_ShouldSetErrorStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        // All fields empty — draft build should produce "No selected-unit values entered"
        vm.SelectedUnitHp = "";
        vm.SelectedUnitShield = "";
        vm.SelectedUnitSpeed = "";
        vm.SelectedUnitDamageMultiplier = "";
        vm.SelectedUnitCooldownMultiplier = "";
        vm.SelectedUnitVeterancy = "";
        vm.SelectedUnitOwnerFaction = "";

        await InvokeAsync(vm, "ApplySelectedUnitDraftAsync");

        vm.Status.Should().Contain("No selected-unit values entered");
    }

    [Fact]
    public async Task ApplySelectedUnitDraftAsync_InvalidFloat_ShouldSetErrorStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SelectedUnitHp = "not_a_number";

        await InvokeAsync(vm, "ApplySelectedUnitDraftAsync");

        vm.Status.Should().Contain("HP must be a number");
    }

    [Fact]
    public async Task ApplySelectedUnitDraftAsync_InvalidVeterancy_ShouldSetErrorStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SelectedUnitHp = "100";
        vm.SelectedUnitShield = "";
        vm.SelectedUnitSpeed = "";
        vm.SelectedUnitDamageMultiplier = "";
        vm.SelectedUnitCooldownMultiplier = "";
        vm.SelectedUnitVeterancy = "not_int";
        vm.SelectedUnitOwnerFaction = "";

        await InvokeAsync(vm, "ApplySelectedUnitDraftAsync");

        vm.Status.Should().Contain("Veterancy must be an integer");
    }

    [Fact]
    public async Task ApplySelectedUnitDraftAsync_Succeeded_ShouldRecaptureAndSetStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SelectedUnitHp = "100";
        vm.SelectedUnitShield = "";
        vm.SelectedUnitSpeed = "";
        vm.SelectedUnitDamageMultiplier = "";
        vm.SelectedUnitCooldownMultiplier = "";
        vm.SelectedUnitVeterancy = "";
        vm.SelectedUnitOwnerFaction = "";
        SetField(vm, "_selectedUnitTransactions",
            new StubSelectedUnitService(applySucceeded: true));

        await InvokeAsync(vm, "ApplySelectedUnitDraftAsync");

        vm.Status.Should().Contain("Selected-unit transaction applied");
    }

    [Fact]
    public async Task ApplySelectedUnitDraftAsync_Failed_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SelectedUnitHp = "100";
        vm.SelectedUnitShield = "";
        vm.SelectedUnitSpeed = "";
        vm.SelectedUnitDamageMultiplier = "";
        vm.SelectedUnitCooldownMultiplier = "";
        vm.SelectedUnitVeterancy = "";
        vm.SelectedUnitOwnerFaction = "";
        SetField(vm, "_selectedUnitTransactions",
            new StubSelectedUnitService(applySucceeded: false));

        await InvokeAsync(vm, "ApplySelectedUnitDraftAsync");

        vm.Status.Should().Contain("Selected-unit apply failed");
    }

    // ── RevertSelectedUnitTransactionAsync ──

    [Fact]
    public async Task RevertSelectedUnitTransactionAsync_NullProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "RevertSelectedUnitTransactionAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task RevertSelectedUnitTransactionAsync_Succeeded_ShouldRecaptureSnapshot()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_selectedUnitTransactions", new StubSelectedUnitService(revertSucceeded: true));

        await InvokeAsync(vm, "RevertSelectedUnitTransactionAsync");

        vm.Status.Should().Contain("Reverted selected-unit transaction");
    }

    [Fact]
    public async Task RevertSelectedUnitTransactionAsync_Failed_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_selectedUnitTransactions", new StubSelectedUnitService(revertSucceeded: false));

        await InvokeAsync(vm, "RevertSelectedUnitTransactionAsync");

        vm.Status.Should().Contain("Revert failed");
    }

    // ── RestoreSelectedUnitBaselineAsync ──

    [Fact]
    public async Task RestoreSelectedUnitBaselineAsync_Succeeded_ShouldRecaptureSnapshot()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_selectedUnitTransactions", new StubSelectedUnitService(restoreSucceeded: true));

        await InvokeAsync(vm, "RestoreSelectedUnitBaselineAsync");

        vm.Status.Should().Contain("Selected-unit baseline restored");
    }

    [Fact]
    public async Task RestoreSelectedUnitBaselineAsync_Failed_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_selectedUnitTransactions", new StubSelectedUnitService(restoreSucceeded: false));

        await InvokeAsync(vm, "RestoreSelectedUnitBaselineAsync");

        vm.Status.Should().Contain("Baseline restore failed");
    }

    // ── LoadSpawnPresetsAsync ──

    [Fact]
    public async Task LoadSpawnPresetsAsync_NullProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "LoadSpawnPresetsAsync");

        vm.SpawnPresets.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadSpawnPresetsAsync_WithPresets_ShouldPopulateAndSelectFirst()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        SetField(vm, "_spawnPresets", new StubSpawnPresetService());

        await InvokeAsync(vm, "LoadSpawnPresetsAsync");

        vm.SpawnPresets.Should().HaveCount(1);
        vm.SelectedSpawnPreset.Should().NotBeNull();
        vm.Status.Should().Contain("Loaded 1 spawn preset(s)");
    }

    // ── RunSpawnBatchAsync ──

    [Fact]
    public async Task RunSpawnBatchAsync_NullProfile_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "RunSpawnBatchAsync");

        vm.Status.Should().Contain("select profile and preset");
    }

    [Fact]
    public async Task RunSpawnBatchAsync_InvalidQuantity_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SelectedSpawnPreset = new SpawnPresetViewItem("id", "name", "unit", "EMPIRE", "AUTO", 1, 125, "");
        vm.RuntimeMode = RuntimeMode.Galactic;
        vm.SpawnQuantity = "invalid";
        vm.SpawnDelayMs = "125";

        await InvokeAsync(vm, "RunSpawnBatchAsync");

        vm.Status.Should().Contain("Invalid spawn quantity");
    }

    [Fact]
    public async Task RunSpawnBatchAsync_Succeeded_ShouldSetSuccessStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SelectedSpawnPreset = new SpawnPresetViewItem("id", "name", "unit", "EMPIRE", "AUTO", 1, 125, "");
        vm.RuntimeMode = RuntimeMode.Galactic;
        vm.SpawnQuantity = "3";
        vm.SpawnDelayMs = "100";
        vm.SelectedFaction = "EMPIRE";
        vm.SelectedEntryMarker = "AUTO";
        vm.SpawnStopOnFailure = true;
        SetField(vm, "_spawnPresets", new StubSpawnPresetService(batchSucceeded: true));

        await InvokeAsync(vm, "RunSpawnBatchAsync");

        vm.Status.Should().StartWith("\u2713");
    }

    [Fact]
    public async Task RunSpawnBatchAsync_Failed_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        vm.SelectedSpawnPreset = new SpawnPresetViewItem("id", "name", "unit", "EMPIRE", "AUTO", 1, 125, "");
        vm.RuntimeMode = RuntimeMode.Galactic;
        vm.SpawnQuantity = "1";
        vm.SpawnDelayMs = "100";
        SetField(vm, "_spawnPresets", new StubSpawnPresetService(batchSucceeded: false));

        await InvokeAsync(vm, "RunSpawnBatchAsync");

        vm.Status.Should().StartWith("\u2717");
    }

    // ── ApplyDraftFromSnapshot ──

    [Fact]
    public void ApplyDraftFromSnapshot_NullSnapshot_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => Invoke(vm, "ApplyDraftFromSnapshot", (SelectedUnitSnapshot)null!);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void ApplyDraftFromSnapshot_ValidSnapshot_ShouldSetAllFields()
    {
        var vm = CreateViewModel();
        var snapshot = new SelectedUnitSnapshot(100f, 50f, 2.5f, 1.5f, 0.8f, 3, 1, DateTimeOffset.UtcNow);

        Invoke(vm, "ApplyDraftFromSnapshot", snapshot);

        vm.SelectedUnitHp.Should().Be("100");
        vm.SelectedUnitShield.Should().Be("50");
        vm.SelectedUnitSpeed.Should().Be("2.5");
        vm.SelectedUnitDamageMultiplier.Should().Be("1.5");
        vm.SelectedUnitCooldownMultiplier.Should().Be("0.8");
        vm.SelectedUnitVeterancy.Should().Be("3");
        vm.SelectedUnitOwnerFaction.Should().Be("1");
    }

    // ── RefreshSelectedUnitTransactions ──

    [Fact]
    public void RefreshSelectedUnitTransactions_EmptyHistory_ShouldClear()
    {
        var vm = CreateViewModel();
        SetField(vm, "_selectedUnitTransactions", new StubSelectedUnitService());

        Invoke(vm, "RefreshSelectedUnitTransactions");

        vm.SelectedUnitTransactions.Should().BeEmpty();
    }

    // ── BuildActionContext ──

    [Fact]
    public void BuildActionContext_NullActionId_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = () => Invoke(vm, "BuildActionContext", (string)null!);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void BuildActionContext_NoReliabilityMatch_ShouldReturnUnknownDefaults()
    {
        var vm = CreateViewModel();
        var result = InvokeReturn<IReadOnlyDictionary<string, object?>>(vm, "BuildActionContext", "missing_action");

        result["reliabilityState"].Should().Be("unknown");
        result["reliabilityReasonCode"].Should().Be("unknown");
        result["bundleGateResult"].Should().Be("unknown");
    }

    [Fact]
    public void BuildActionContext_WithReliabilityMatch_ShouldReturnMatchedValues()
    {
        var vm = CreateViewModel();
        vm.ActionReliability.Add(new ActionReliabilityViewItem("set_credits", "stable", "PROBE_PASS", 1.0, "ok"));

        var result = InvokeReturn<IReadOnlyDictionary<string, object?>>(vm, "BuildActionContext", "set_credits");

        result["reliabilityState"].Should().Be("stable");
        result["reliabilityReasonCode"].Should().Be("PROBE_PASS");
        result["bundleGateResult"].Should().Be("bundle_pass");
    }

    [Fact]
    public void BuildActionContext_WithUnavailableReliability_ShouldReturnBlocked()
    {
        var vm = CreateViewModel();
        vm.ActionReliability.Add(new ActionReliabilityViewItem("set_credits", "unavailable", "MISSING", 0.5, "no"));

        var result = InvokeReturn<IReadOnlyDictionary<string, object?>>(vm, "BuildActionContext", "set_credits");

        result["bundleGateResult"].Should().Be("blocked");
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
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
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

    private static void Invoke(object instance, string methodName, params object?[] args)
    {
        FindMethod(instance, methodName, args).Invoke(instance, args.Length == 0 ? null : args);
    }

    private static T InvokeReturn<T>(object instance, string methodName, params object?[] args)
    {
        return (T)FindMethod(instance, methodName, args).Invoke(instance, args)!;
    }

    private static async Task InvokeAsync(object instance, string methodName, params object?[] args)
    {
        var task = FindMethod(instance, methodName, args).Invoke(instance, args.Length == 0 ? null : args);
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
                .Where(m => m.Name == methodName).ToArray();
            method = candidates.FirstOrDefault(m => m.GetParameters().Length == args.Length)
                     ?? candidates.FirstOrDefault(m => m.GetParameters().Length == 0 && args.Length == 0);
            type = type.BaseType;
        }

        method.Should().NotBeNull($"method '{methodName}' should exist");
        return method!;
    }

    private static AttachSession BuildSession(IReadOnlyDictionary<string, string>? metadata = null)
    {
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature),
            ["fog_reveal"] = new("fog_reveal", (nint)0x2000, SymbolValueType.Bool, AddressSource.Fallback,
                HealthStatus: SymbolHealthStatus.Degraded),
            ["broken"] = new("broken", nint.Zero, SymbolValueType.Int32, AddressSource.Signature,
                HealthStatus: SymbolHealthStatus.Unresolved)
        };

        return new AttachSession("test", new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null,
                ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: metadata,
                LaunchContext: new LaunchContext(LaunchKind.Workshop, true, new[] { "1" }, null, null, "cmd",
                    new ProfileRecommendation("test", "match", 0.9))),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(symbols), DateTimeOffset.UtcNow);
    }

    // ── Stubs ──

    private sealed class StubRuntime : IRuntimeAdapter
    {
        private AttachSession? _session;
        public StubRuntime(AttachSession? session) => _session = session;
        public bool IsAttached => _session is not null;
        public AttachSession? CurrentSession => _session;
        public void NullifySession() => _session = null;
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubProfiles : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build());
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build());
        public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "test_profile" });

        private static TrainerProfile Build() => new("test_profile", "test", null, ExeTarget.Swfoc, null,
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(),
            new Dictionary<string, string>());
    }

    private sealed class StubCatalog : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, IReadOnlyList<string>>());
    }

    private sealed class ThrowingCatalog : ICatalogService
    {
        private readonly Exception _ex;
        public ThrowingCatalog(Exception ex) => _ex = ex;
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken ct)
            => throw _ex;
    }

    private sealed class SessionNullingCatalog : ICatalogService
    {
        private readonly StubRuntime _runtime;
        public SessionNullingCatalog(StubRuntime runtime) => _runtime = runtime;
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken ct)
        {
            _runtime.NullifySession();
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, IReadOnlyList<string>>());
        }
    }

    private sealed class StubActionReliability : IActionReliabilityService
    {
        public IReadOnlyList<ActionReliabilityInfo> Evaluate(TrainerProfile profile, AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
            => new[] { new ActionReliabilityInfo("set_credits", ActionReliabilityState.Stable, "OK", 1.0, "detail") };
    }

    private sealed class StubSelectedUnitService : ISelectedUnitTransactionService
    {
        private readonly Exception? _captureThrows;
        private readonly bool _applySucceeded;
        private readonly bool _revertSucceeded;
        private readonly bool _restoreSucceeded;

        public StubSelectedUnitService(
            Exception? captureThrows = null,
            bool applySucceeded = true,
            bool revertSucceeded = true,
            bool restoreSucceeded = true)
        {
            _captureThrows = captureThrows;
            _applySucceeded = applySucceeded;
            _revertSucceeded = revertSucceeded;
            _restoreSucceeded = restoreSucceeded;
        }

        public SelectedUnitSnapshot? Baseline => null;
        public IReadOnlyList<SelectedUnitTransactionRecord> History => Array.Empty<SelectedUnitTransactionRecord>();

        public Task<SelectedUnitSnapshot> CaptureAsync(CancellationToken ct)
        {
            if (_captureThrows is not null) throw _captureThrows;
            return Task.FromResult(new SelectedUnitSnapshot(100, 50, 2.5f, 1.0f, 1.0f, 0, 0, DateTimeOffset.UtcNow));
        }

        public Task<SelectedUnitTransactionResult> ApplyAsync(string profileId, SelectedUnitDraft draft, RuntimeMode mode, CancellationToken ct)
            => Task.FromResult(new SelectedUnitTransactionResult(_applySucceeded, _applySucceeded ? "ok" : "err", "txn1", Array.Empty<ActionExecutionResult>()));

        public Task<SelectedUnitTransactionResult> RevertLastAsync(string profileId, RuntimeMode mode, CancellationToken ct)
            => Task.FromResult(new SelectedUnitTransactionResult(_revertSucceeded, _revertSucceeded ? "ok" : "err", "txn2", Array.Empty<ActionExecutionResult>()));

        public Task<SelectedUnitTransactionResult> RestoreBaselineAsync(string profileId, RuntimeMode mode, CancellationToken ct)
            => Task.FromResult(new SelectedUnitTransactionResult(_restoreSucceeded, _restoreSucceeded ? "ok" : "err", "txn3", Array.Empty<ActionExecutionResult>()));
    }

    private sealed class StubSpawnPresetService : ISpawnPresetService
    {
        private readonly bool _batchSucceeded;
        public StubSpawnPresetService(bool batchSucceeded = true) => _batchSucceeded = batchSucceeded;

        public Task<IReadOnlyList<SpawnPreset>> LoadPresetsAsync(string profileId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SpawnPreset>>(new[]
            {
                new SpawnPreset("id", "name", "unit", "EMPIRE", "AUTO", 1, 125, "desc")
            });

        public SpawnBatchPlan BuildBatchPlan(string profileId, SpawnPreset preset, int quantity, int delayMs,
            string? factionOverride, string? entryMarkerOverride, bool stopOnFailure)
            => new(profileId, preset.Id, stopOnFailure,
                Enumerable.Range(0, quantity).Select(i => new SpawnBatchItem(i, preset.UnitId, preset.Faction, preset.EntryMarker, delayMs)).ToList());

        public Task<SpawnBatchExecutionResult> ExecuteBatchAsync(string profileId, SpawnBatchPlan plan, RuntimeMode mode, CancellationToken ct)
            => Task.FromResult(new SpawnBatchExecutionResult(
                _batchSucceeded, _batchSucceeded ? "spawned" : "failed", 1, _batchSucceeded ? 1 : 0,
                _batchSucceeded ? 0 : 1, false, Array.Empty<SpawnBatchItemResult>()));
    }
}
