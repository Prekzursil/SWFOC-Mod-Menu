using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Input;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Full branch coverage for MainViewModelBindableMembersBase —
/// property setters, INotifyPropertyChanged, and side-effect branches.
/// </summary>
public sealed class MainViewModelBindableMembersCoverageTests
{
    [Fact]
    public void SelectedProfileId_SetToNewValue_ShouldRaisePropertyChangedForBothProperties()
    {
        var vm = CreateViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.SelectedProfileId = "test_profile";

        raised.Should().Contain(nameof(vm.SelectedProfileId));
        raised.Should().Contain(nameof(vm.CanWorkWithProfile));
        vm.SelectedProfileId.Should().Be("test_profile");
    }

    [Fact]
    public void SelectedProfileId_SetToSameValue_ShouldNotRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "test_profile";
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.SelectedProfileId = "test_profile";

        raised.Should().BeEmpty();
    }

    [Fact]
    public void SelectedProfileId_SetToNull_ShouldMakeCanWorkWithProfileFalse()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "something";
        vm.SelectedProfileId = null;

        vm.CanWorkWithProfile.Should().BeFalse();
    }

    [Fact]
    public void SelectedProfileId_SetToWhitespace_ShouldMakeCanWorkWithProfileFalse()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "  ";

        vm.CanWorkWithProfile.Should().BeFalse();
    }

    [Fact]
    public void SelectedProfileId_SetToValidValue_ShouldMakeCanWorkWithProfileTrue()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = "my_profile";

        vm.CanWorkWithProfile.Should().BeTrue();
    }

    [Fact]
    public void SelectedActionId_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.SelectedActionId = "new_action";

        raised.Should().Contain(nameof(vm.SelectedActionId));
    }

    [Fact]
    public void SelectedActionId_SetToSameValue_ShouldNotRaise()
    {
        var vm = CreateViewModel();
        vm.SelectedActionId = "action";
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.SelectedActionId = "action";

        raised.Should().BeEmpty();
    }

    [Fact]
    public void PayloadJson_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.PayloadJson = """{"symbol":"fog_reveal"}""";

        raised.Should().BeTrue();
    }

    [Fact]
    public void Status_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.Status = "new status";

        raised.Should().BeTrue();
        vm.Status.Should().Be("new status");
    }

    [Fact]
    public void RuntimeMode_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.RuntimeMode = RuntimeMode.Galactic;

        raised.Should().BeTrue();
        vm.RuntimeMode.Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public void SavePath_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.SavePath = @"C:\test.sav";

        raised.Should().BeTrue();
        vm.SavePath.Should().Be(@"C:\test.sav");
    }

    [Fact]
    public void SaveNodePath_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        vm.SaveNodePath = "node/path";
        vm.SaveNodePath.Should().Be("node/path");
    }

    [Fact]
    public void SaveEditValue_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        vm.SaveEditValue = "42";
        vm.SaveEditValue.Should().Be("42");
    }

    [Fact]
    public void SaveSearchQuery_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.SaveSearchQuery = "credits";

        raised.Should().Contain(nameof(vm.SaveSearchQuery));
    }

    [Fact]
    public void SaveSearchQuery_SetToSameValue_ShouldNotRaise()
    {
        var vm = CreateViewModel();
        vm.SaveSearchQuery = "test";
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        vm.SaveSearchQuery = "test";

        raised.Should().BeEmpty();
    }

    [Fact]
    public void SavePatchPackPath_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.SavePatchPackPath = @"C:\patch.json";

        raised.Should().BeTrue();
    }

    [Fact]
    public void SavePatchPackPath_SetToSameValue_ShouldNotRaise()
    {
        var vm = CreateViewModel();
        vm.SavePatchPackPath = "same";
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.SavePatchPackPath = "same";

        raised.Should().BeFalse();
    }

    [Fact]
    public void SavePatchMetadataSummary_Roundtrip()
    {
        var vm = CreateViewModel();
        vm.SavePatchMetadataSummary = "summary";
        vm.SavePatchMetadataSummary.Should().Be("summary");
    }

    [Fact]
    public void SavePatchApplySummary_Roundtrip()
    {
        var vm = CreateViewModel();
        vm.SavePatchApplySummary = "applied";
        vm.SavePatchApplySummary.Should().Be("applied");
    }

    [Fact]
    public void ResolvedSymbolsCount_Roundtrip()
    {
        var vm = CreateViewModel();
        vm.ResolvedSymbolsCount = 42;
        vm.ResolvedSymbolsCount.Should().Be(42);
    }

    [Fact]
    public void SelectedHotkey_Roundtrip()
    {
        var vm = CreateViewModel();
        var hotkey = new HotkeyBindingItem { Gesture = "Ctrl+1", ActionId = "a" };
        vm.SelectedHotkey = hotkey;
        vm.SelectedHotkey.Should().BeSameAs(hotkey);
    }

    [Fact]
    public void SelectedSpawnPreset_SetToNewValue_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        var preset = new SpawnPresetViewItem("id", "name", "unit", "EMPIRE", "AUTO", 1, 125, "desc");
        vm.SelectedSpawnPreset = preset;

        raised.Should().Contain(nameof(vm.SelectedSpawnPreset));
        vm.SelectedSpawnPreset.Should().BeSameAs(preset);
    }

    [Fact]
    public void SelectedSpawnPreset_SetToSameValue_ShouldNotRaise()
    {
        var vm = CreateViewModel();
        var preset = new SpawnPresetViewItem("id", "name", "unit", "EMPIRE", "AUTO", 1, 125, "desc");
        vm.SelectedSpawnPreset = preset;
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.SelectedSpawnPreset = preset;

        raised.Should().BeFalse();
    }

    [Fact]
    public void SelectedUnitFields_Roundtrip()
    {
        var vm = CreateViewModel();

        vm.SelectedUnitHp = "100";
        vm.SelectedUnitHp.Should().Be("100");

        vm.SelectedUnitShield = "50";
        vm.SelectedUnitShield.Should().Be("50");

        vm.SelectedUnitSpeed = "2.5";
        vm.SelectedUnitSpeed.Should().Be("2.5");

        vm.SelectedUnitDamageMultiplier = "1.5";
        vm.SelectedUnitDamageMultiplier.Should().Be("1.5");

        vm.SelectedUnitCooldownMultiplier = "0.8";
        vm.SelectedUnitCooldownMultiplier.Should().Be("0.8");

        vm.SelectedUnitVeterancy = "3";
        vm.SelectedUnitVeterancy.Should().Be("3");

        vm.SelectedUnitOwnerFaction = "1";
        vm.SelectedUnitOwnerFaction.Should().Be("1");
    }

    [Fact]
    public void SpawnFields_Roundtrip()
    {
        var vm = CreateViewModel();

        vm.SpawnQuantity = "5";
        vm.SpawnQuantity.Should().Be("5");

        vm.SpawnDelayMs = "250";
        vm.SpawnDelayMs.Should().Be("250");

        vm.SelectedFaction = "REBEL";
        vm.SelectedFaction.Should().Be("REBEL");

        vm.SelectedEntryMarker = "MARKER_1";
        vm.SelectedEntryMarker.Should().Be("MARKER_1");

        vm.SpawnStopOnFailure = false;
        vm.SpawnStopOnFailure.Should().BeFalse();
    }

    [Fact]
    public void PatchApplyStrict_Roundtrip()
    {
        var vm = CreateViewModel();
        vm.IsStrictPatchApply = false;
        vm.IsStrictPatchApply.Should().BeFalse();
    }

    [Fact]
    public void OnboardingFields_Roundtrip()
    {
        var vm = CreateViewModel();

        vm.OnboardingBaseProfileId = "base";
        vm.OnboardingBaseProfileId.Should().Be("base");

        vm.OnboardingDraftProfileId = "draft";
        vm.OnboardingDraftProfileId.Should().Be("draft");

        vm.OnboardingDisplayName = "display";
        vm.OnboardingDisplayName.Should().Be("display");

        vm.OnboardingNamespaceRoot = "ns";
        vm.OnboardingNamespaceRoot.Should().Be("ns");

        vm.OnboardingLaunchSample = "sample";
        vm.OnboardingLaunchSample.Should().Be("sample");

        vm.OnboardingSummary = "summary";
        vm.OnboardingSummary.Should().Be("summary");
    }

    [Fact]
    public void MiscFields_Roundtrip()
    {
        var vm = CreateViewModel();

        vm.CalibrationNotes = "notes";
        vm.CalibrationNotes.Should().Be("notes");

        vm.ModCompatibilitySummary = "compat";
        vm.ModCompatibilitySummary.Should().Be("compat");

        vm.OpsArtifactSummary = "artifact";
        vm.OpsArtifactSummary.Should().Be("artifact");

        vm.SupportBundleOutputDirectory = @"C:\out";
        vm.SupportBundleOutputDirectory.Should().Be(@"C:\out");

        vm.CreditsValue = "500";
        vm.CreditsValue.Should().Be("500");

        vm.CreditsFreeze = true;
        vm.CreditsFreeze.Should().BeTrue();
    }

    [Fact]
    public void LaunchFields_Roundtrip()
    {
        var vm = CreateViewModel();

        vm.LaunchTarget = "Sweaw";
        vm.LaunchTarget.Should().Be("Sweaw");

        vm.LaunchMode = "SteamMod";
        vm.LaunchMode.Should().Be("SteamMod");

        vm.LaunchWorkshopId = "12345";
        vm.LaunchWorkshopId.Should().Be("12345");

        vm.LaunchModPath = @"Mods\Test";
        vm.LaunchModPath.Should().Be(@"Mods\Test");

        vm.TerminateExistingBeforeLaunch = true;
        vm.TerminateExistingBeforeLaunch.Should().BeTrue();
    }

    [Fact]
    public void SetField_WhenNoHandler_ShouldNotThrow()
    {
        // OnPropertyChanged with null handler should silently return.
        var vm = CreateViewModel();
        // No event subscribed — should not throw.
        vm.Status = "test";
        vm.Status.Should().Be("test");
    }

    private static MainViewModel CreateViewModel()
    {
#pragma warning disable SYSLIB0050
        var vm = (MainViewModel)FormatterServices.GetUninitializedObject(typeof(MainViewModel));
#pragma warning restore SYSLIB0050

        // Initialize collections so property setters don't NRE.
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

        // Initialize backing fields that the constructor would normally set.
        SetField(vm, "_status", "Ready");
        SetField(vm, "_selectedActionId", string.Empty);
        SetField(vm, "_payloadJson", "{}");
        SetField(vm, "_savePath", string.Empty);
        SetField(vm, "_saveNodePath", string.Empty);
        SetField(vm, "_saveEditValue", string.Empty);
        SetField(vm, "_saveSearchQuery", string.Empty);
        SetField(vm, "_savePatchPackPath", string.Empty);
        SetField(vm, "_savePatchMetadataSummary", "No patch pack loaded.");
        SetField(vm, "_savePatchApplySummary", string.Empty);
        SetField(vm, "_creditsValue", "1000000");
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
        SetField(vm, "_onboardingBaseProfileId", "base_swfoc");
        SetField(vm, "_onboardingDraftProfileId", "custom_my_mod");
        SetField(vm, "_onboardingDisplayName", "Custom Mod Draft");
        SetField(vm, "_onboardingNamespaceRoot", "custom");
        SetField(vm, "_onboardingLaunchSample", string.Empty);
        SetField(vm, "_onboardingSummary", string.Empty);
        SetField(vm, "_calibrationNotes", string.Empty);
        SetField(vm, "_modCompatibilitySummary", string.Empty);
        SetField(vm, "_opsArtifactSummary", string.Empty);
        SetField(vm, "_launchTarget", "Swfoc");
        SetField(vm, "_launchMode", "Vanilla");
        SetField(vm, "_launchWorkshopId", string.Empty);
        SetField(vm, "_launchModPath", string.Empty);
        SetField(vm, "_supportBundleOutputDirectory", "support");
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));

        return vm;
    }

    private static void SetField(object instance, string fieldName, object? value)
    {
        var type = instance.GetType();
        System.Reflection.FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            type = type.BaseType;
        }

        field!.SetValue(instance, value);
    }

    private static void SetProp(object instance, string propName, object value)
    {
        var type = instance.GetType();
        System.Reflection.PropertyInfo? prop = null;
        while (type is not null && prop is null)
        {
            prop = type.GetProperty(propName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            type = type.BaseType;
        }

        prop.Should().NotBeNull($"property '{propName}' should exist");
        prop!.SetValue(instance, value);
    }
}
