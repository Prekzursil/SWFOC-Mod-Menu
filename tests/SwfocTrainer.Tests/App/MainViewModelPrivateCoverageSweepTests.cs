using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelPrivateCoverageSweepTests
{
    [Fact]
    public void SaveContextPredicates_ShouldReflectCurrentViewModelState()
    {
        var vm = new MainViewModel(CreateNullDependencies());

        InvokePrivate<bool>(vm, "CanLoadSaveContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanEditSaveContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanValidateSaveContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanRefreshDiffContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanWriteSaveContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanExportPatchPackContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanLoadPatchPackContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanPreviewPatchPackContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanApplyPatchPackContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanRestoreBackupContext").Should().BeFalse();
        InvokePrivate<bool>(vm, "CanRemoveHotkeyContext").Should().BeFalse();

        vm.SavePath = "campaign.sav";
        vm.SelectedProfileId = "base_swfoc";
        vm.SaveNodePath = "/economy/credits_empire";
        vm.SavePatchPackPath = "patch.json";
        vm.SelectedHotkey = new SwfocTrainer.App.Models.HotkeyBindingItem { Gesture = "Ctrl+1", ActionId = "set_credits", PayloadJson = "{}" };

        var saveDoc = new SaveDocument("campaign.sav", "schema", new byte[16], new SaveNode("root", "root", "root", null));
        SetField(vm, "_loadedSave", saveDoc);
        SetField(vm, "_loadedSaveOriginal", saveDoc.Raw);
        SetField(vm, "_loadedPatchPack", new SavePatchPack(
            new SavePatchMetadata("1.0", "base_swfoc", "schema", "hash", DateTimeOffset.UtcNow),
            new SavePatchCompatibility(["base_swfoc"], "schema"),
            Array.Empty<SavePatchOperation>()));

        InvokePrivate<bool>(vm, "CanLoadSaveContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanEditSaveContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanValidateSaveContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanRefreshDiffContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanWriteSaveContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanExportPatchPackContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanLoadPatchPackContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanPreviewPatchPackContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanApplyPatchPackContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanRestoreBackupContext").Should().BeTrue();
        InvokePrivate<bool>(vm, "CanRemoveHotkeyContext").Should().BeTrue();
    }

    [Fact]
    public void ApplyAttachSessionStatus_AndFeatureGateHelpers_ShouldPopulateDiagnostics()
    {
        var vm = new MainViewModel(CreateNullDependencies());
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy),
            ["fog"] = new SymbolInfo("fog", nint.Zero, SymbolValueType.Bool, AddressSource.None, HealthStatus: SymbolHealthStatus.Unresolved),
            ["unit_cap"] = new SymbolInfo("unit_cap", (nint)0x2000, SymbolValueType.Int32, AddressSource.Fallback, HealthStatus: SymbolHealthStatus.Degraded)
        };

        var session = new AttachSession(
            ProfileId: "base_swfoc",
            Process: new ProcessMetadata(
                ProcessId: Environment.ProcessId,
                ProcessName: "swfoc.exe",
                ProcessPath: @"C:\Games\swfoc.exe",
                CommandLine: "STEAMMOD=1125571106",
                ExeTarget: ExeTarget.Swfoc,
                Mode: RuntimeMode.Galactic,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["resolvedVariant"] = "base_swfoc",
                    ["resolvedVariantReasonCode"] = "variant_match"
                }),
            Build: new ProfileBuild("base_swfoc", "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc, ProcessId: Environment.ProcessId),
            Symbols: new SymbolMap(symbols),
            AttachedAt: DateTimeOffset.UtcNow);

        InvokePrivate(vm, "ApplyAttachSessionStatus", session);

        vm.RuntimeMode.Should().Be(RuntimeMode.Galactic);
        vm.ResolvedSymbolsCount.Should().Be(3);
        vm.Status.Should().Contain("Attached to PID");
        vm.Status.Should().Contain("sig=1");
        vm.Status.Should().Contain("fallback=1");

        var profile = new TrainerProfile(
            Id: "base_swfoc",
            DisplayName: "Base",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow_extender_credits"] = true
            },
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());

        InvokePrivateStatic<string?>("ResolveProfileFeatureGateReason", "set_credits_extender_experimental", profile).Should().BeNull();
        InvokePrivateStatic<string?>("ResolveProfileFeatureGateReason", "set_unit_cap_patch_fallback", profile)
            .Should().Contain("allow_unit_cap_patch_fallback");
        InvokePrivateStatic<string?>("ResolveProfileFeatureGateReason", "unknown_action", profile).Should().BeNull();
    }

    [Fact]
    public void PayloadTemplateHelpers_ShouldHandleMissingSpecs_AndRequiredKeys()
    {
        var vm = new MainViewModel(CreateNullDependencies());

        vm.SelectedActionId = "";
        InvokePrivate(vm, "ApplyPayloadTemplateForSelectedAction");

        vm.SelectedActionId = "set_hero_state_helper";
        SetField(vm, "_loadedActionSpecs", new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_hero_state_helper"] = new ActionSpec(
                "set_hero_state_helper",
                ActionCategory.Hero,
                RuntimeMode.Galactic,
                ExecutionKind.Helper,
                new JsonObject { ["required"] = new JsonArray("heroId", "state") },
                VerifyReadback: false,
                CooldownMs: 0)
        });

        InvokePrivate(vm, "ApplyPayloadTemplateForSelectedAction");
        vm.PayloadJson.Should().Contain("heroId");
        vm.PayloadJson.Should().Contain("state");

        SetField(vm, "_loadedActionSpecs", new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_hero_state_helper"] = new ActionSpec(
                "set_hero_state_helper",
                ActionCategory.Hero,
                RuntimeMode.Galactic,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0)
        });

        vm.PayloadJson = "{}";
        InvokePrivate(vm, "ApplyPayloadTemplateForSelectedAction");
        vm.PayloadJson.Should().Be("{}");
    }

    private static MainViewModelDependencies CreateNullDependencies()
    {
        return new MainViewModelDependencies
        {
            Profiles = null!,
            ProcessLocator = null!,
            LaunchContextResolver = null!,
            ProfileVariantResolver = null!,
            GameLauncher = null!,
            Runtime = null!,
            Orchestrator = null!,
            Catalog = null!,
            SaveCodec = null!,
            SavePatchPackService = null!,
            SavePatchApplyService = null!,
            Helper = null!,
            Updates = null!,
            ModOnboarding = null!,
            ModCalibration = null!,
            SupportBundles = null!,
            Telemetry = null!,
            FreezeService = null!,
            ActionReliability = null!,
            SelectedUnitTransactions = null!,
            SpawnPresets = null!
        };
    }

    private static void SetField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"Expected field '{fieldName}'");
        field!.SetValue(instance, value);
    }

    private static void InvokePrivate(object instance, string methodName, params object?[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull($"Expected private method '{methodName}'");
        _ = method!.Invoke(instance, args);
    }

    private static T InvokePrivate<T>(object instance, string methodName, params object?[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull($"Expected private method '{methodName}'");
        return (T)method!.Invoke(instance, args)!;
    }

    private static T InvokePrivateStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(MainViewModel).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull($"Expected private static method '{methodName}'");
        return (T)method!.Invoke(null, args)!;
    }
}

