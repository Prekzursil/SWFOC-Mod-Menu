using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModel session gating:
/// ResolveProfileFeatureGateReason all action/flag combinations,
/// CanXxx context predicates via reflection, ResolveActionUnavailableReason
/// edge cases (dependency disabled, healthy/degraded symbol states).
/// </summary>
public sealed class MainViewModelSessionGatingWave5Tests
{
    [Fact]
    public void ResolveProfileFeatureGateReason_UnrecognizedAction_ShouldReturnNull()
    {
        var profile = BuildProfile(new Dictionary<string, bool>());
        var reason = InvokeResolveProfileFeatureGateReason("unrecognized_action", profile);
        reason.Should().BeNull();
    }

    [Fact]
    public void ResolveProfileFeatureGateReason_FogPatchFallback_MissingFlag_ShouldReturnBlockedReason()
    {
        var profile = BuildProfile(new Dictionary<string, bool>());
        var reason = InvokeResolveProfileFeatureGateReason("toggle_fog_reveal_patch_fallback", profile);
        reason.Should().Contain("allow_fog_patch_fallback");
        reason.Should().Contain("fallback action");
    }

    [Fact]
    public void ResolveProfileFeatureGateReason_UnitCapPatchFallback_Disabled_ShouldReturnBlockedReason()
    {
        var profile = BuildProfile(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["allow_unit_cap_patch_fallback"] = false
        });
        var reason = InvokeResolveProfileFeatureGateReason("set_unit_cap_patch_fallback", profile);
        reason.Should().Contain("allow_unit_cap_patch_fallback");
        reason.Should().Contain("fallback action");
    }

    [Fact]
    public void ResolveProfileFeatureGateReason_ExtenderCredits_ShouldReturnActionKind()
    {
        var profile = BuildProfile(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["allow_extender_credits"] = false
        });
        var reason = InvokeResolveProfileFeatureGateReason("set_credits_extender_experimental", profile);
        reason.Should().Contain("action 'set_credits_extender_experimental'");
        reason.Should().NotContain("fallback action");
    }

    [Fact]
    public void ResolveActionUnavailableReason_NoDependencyDisabledNoSymbol_ShouldReturnNull()
    {
        var spec = new ActionSpec(
            Id: "custom", Category: ActionCategory.Global, Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject { ["required"] = new JsonArray(JsonValue.Create("intValue")!) },
            VerifyReadback: false, CooldownMs: 0);
        var session = BuildSession(RuntimeMode.Galactic);

        var reason = InvokeResolveActionUnavailableReason("custom", spec, session);
        reason.Should().BeNull();
    }

    [Fact]
    public void ResolveActionUnavailableReason_DependencyDisabled_ShouldReturnDependencyReason()
    {
        var spec = new ActionSpec(
            Id: "set_credits", Category: ActionCategory.Global, Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false, CooldownMs: 0);
        var session = BuildSession(
            RuntimeMode.Galactic,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyDisabledActions"] = "set_credits,freeze_timer"
            });

        var reason = InvokeResolveActionUnavailableReason("set_credits", spec, session);
        reason.Should().Be("action is disabled by dependency validation for this attachment.");
    }

    [Fact]
    public void ResolveActionUnavailableReason_DependencyDisabledActionsEmpty_ShouldNotBlock()
    {
        var spec = new ActionSpec(
            Id: "set_credits", Category: ActionCategory.Global, Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false, CooldownMs: 0);
        var session = BuildSession(
            RuntimeMode.Galactic,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyDisabledActions"] = ""
            });

        var reason = InvokeResolveActionUnavailableReason("set_credits", spec, session);
        reason.Should().BeNull();
    }

    [Fact]
    public void CanLoadSaveContext_BothEmpty_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        vm.SavePath = string.Empty;
        vm.SelectedProfileId = null;

        var result = InvokePrivateMethod<bool>(vm, "CanLoadSaveContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanLoadSaveContext_OnlySavePath_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        vm.SavePath = @"C:\save.sav";
        vm.SelectedProfileId = null;

        var result = InvokePrivateMethod<bool>(vm, "CanLoadSaveContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanLoadSaveContext_BothSet_ShouldReturnTrue()
    {
        var vm = CreateUninitializedViewModel();
        vm.SavePath = @"C:\save.sav";
        vm.SelectedProfileId = "base_swfoc";

        var result = InvokePrivateMethod<bool>(vm, "CanLoadSaveContext");
        result.Should().BeTrue();
    }

    [Fact]
    public void CanEditSaveContext_NullSave_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        SetPrivateField(vm, "_loadedSave", null);
        vm.SaveNodePath = "path";

        var result = InvokePrivateMethod<bool>(vm, "CanEditSaveContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanEditSaveContext_EmptySaveNodePath_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        SetPrivateField(vm, "_loadedSave", BuildSaveDocumentStub());
        vm.SaveNodePath = "";

        var result = InvokePrivateMethod<bool>(vm, "CanEditSaveContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanValidateSaveContext_NullSave_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        SetPrivateField(vm, "_loadedSave", null);

        var result = InvokePrivateMethod<bool>(vm, "CanValidateSaveContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanRefreshDiffContext_NullOriginal_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        SetPrivateField(vm, "_loadedSave", BuildSaveDocumentStub());
        SetPrivateField(vm, "_loadedSaveOriginal", null);

        var result = InvokePrivateMethod<bool>(vm, "CanRefreshDiffContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanWriteSaveContext_NullSave_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        SetPrivateField(vm, "_loadedSave", null);

        var result = InvokePrivateMethod<bool>(vm, "CanWriteSaveContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanExportPatchPackContext_AllNull_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        SetPrivateField(vm, "_loadedSave", null);
        SetPrivateField(vm, "_loadedSaveOriginal", null);
        vm.SelectedProfileId = null;

        var result = InvokePrivateMethod<bool>(vm, "CanExportPatchPackContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanLoadPatchPackContext_EmptyPath_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        vm.SavePatchPackPath = "";

        var result = InvokePrivateMethod<bool>(vm, "CanLoadPatchPackContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanLoadPatchPackContext_WithPath_ShouldReturnTrue()
    {
        var vm = CreateUninitializedViewModel();
        vm.SavePatchPackPath = @"C:\patch.json";

        var result = InvokePrivateMethod<bool>(vm, "CanLoadPatchPackContext");
        result.Should().BeTrue();
    }

    [Fact]
    public void CanPreviewPatchPackContext_NullPatchPack_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        SetPrivateField(vm, "_loadedSave", BuildSaveDocumentStub());
        SetPrivateField(vm, "_loadedPatchPack", null);
        vm.SelectedProfileId = "profile";

        var result = InvokePrivateMethod<bool>(vm, "CanPreviewPatchPackContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanApplyPatchPackContext_NullPatchPack_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        SetPrivateField(vm, "_loadedPatchPack", null);
        vm.SavePath = @"C:\save.sav";
        vm.SelectedProfileId = "profile";

        var result = InvokePrivateMethod<bool>(vm, "CanApplyPatchPackContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanRestoreBackupContext_EmptySavePath_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        vm.SavePath = "";

        var result = InvokePrivateMethod<bool>(vm, "CanRestoreBackupContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanRestoreBackupContext_WithSavePath_ShouldReturnTrue()
    {
        var vm = CreateUninitializedViewModel();
        vm.SavePath = @"C:\save.sav";

        var result = InvokePrivateMethod<bool>(vm, "CanRestoreBackupContext");
        result.Should().BeTrue();
    }

    [Fact]
    public void CanRemoveHotkeyContext_NullHotkey_ShouldReturnFalse()
    {
        var vm = CreateUninitializedViewModel();
        vm.SelectedHotkey = null;

        var result = InvokePrivateMethod<bool>(vm, "CanRemoveHotkeyContext");
        result.Should().BeFalse();
    }

    [Fact]
    public void CanRemoveHotkeyContext_WithHotkey_ShouldReturnTrue()
    {
        var vm = CreateUninitializedViewModel();
        vm.SelectedHotkey = new SwfocTrainer.App.Models.HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = "set_credits",
            PayloadJson = "{}"
        };

        var result = InvokePrivateMethod<bool>(vm, "CanRemoveHotkeyContext");
        result.Should().BeTrue();
    }

    private static MainViewModel CreateUninitializedViewModel()
    {
#pragma warning disable SYSLIB0050
        return (MainViewModel)FormatterServices.GetUninitializedObject(typeof(MainViewModel));
#pragma warning restore SYSLIB0050
    }

    private static T InvokePrivateMethod<T>(MainViewModel vm, string methodName)
    {
        var method = typeof(MainViewModel).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull($"method '{methodName}' should exist");
        return (T)method!.Invoke(vm, Array.Empty<object?>())!;
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
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

    private static string? InvokeResolveProfileFeatureGateReason(string actionId, TrainerProfile profile)
    {
        var method = typeof(MainViewModel).GetMethod(
            "ResolveProfileFeatureGateReason",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.Invoke(null, new object?[] { actionId, profile }) as string;
    }

    private static string? InvokeResolveActionUnavailableReason(
        string actionId, ActionSpec spec, AttachSession session)
    {
        var method = typeof(MainViewModel).GetMethod(
            "ResolveActionUnavailableReason",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.Invoke(null, new object?[] { actionId, spec, session }) as string;
    }

    private static AttachSession BuildSession(
        RuntimeMode mode,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new AttachSession(
            ProfileId: "test_profile",
            Process: new ProcessMetadata(
                ProcessId: 100, ProcessName: "swfoc.exe",
                ProcessPath: @"C:\Games\swfoc.exe",
                CommandLine: null, ExeTarget: ExeTarget.Swfoc,
                Mode: mode, Metadata: metadata),
            Build: new ProfileBuild("test_profile", "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    private static TrainerProfile BuildProfile(IReadOnlyDictionary<string, bool> featureFlags)
    {
        return new TrainerProfile(
            Id: "test_profile", DisplayName: "test", Inherits: null,
            ExeTarget: ExeTarget.Swfoc, SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: featureFlags,
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());
    }

    private static SaveDocument BuildSaveDocumentStub()
    {
        return new SaveDocument(
            Path: @"C:\test.sav",
            SchemaId: "test",
            Raw: new byte[] { 0x00 },
            Root: new SaveNode("/", "root", "root", null));
    }
}
