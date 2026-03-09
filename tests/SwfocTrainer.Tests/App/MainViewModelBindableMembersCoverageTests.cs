using System.ComponentModel;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelBindableMembersCoverageTests
{
    [Fact]
    public void PropertySetters_ShouldUpdateState_AndInvokeHooksOnlyOnChange()
    {
        var vm = new BindableMembersTestDouble();

        vm.SelectedProfileId = "base_swfoc";
        vm.CanWorkWithProfile.Should().BeTrue();

        vm.SelectedActionId = "set_credits";
        vm.SelectedActionId = "set_credits";

        vm.SaveSearchQuery = "credits";
        vm.SaveSearchQuery = "credits";

        vm.PayloadJson = "{\"intValue\":1000}";
        vm.Status = "Ready";
        vm.RuntimeMode = RuntimeMode.Galactic;
        vm.SavePath = @"C:\\test\\save.sav";
        vm.SaveNodePath = "root.money";
        vm.SaveEditValue = "42";
        vm.SavePatchPackPath = @"C:\\test\\patch.json";
        vm.SavePatchMetadataSummary = "meta";
        vm.SavePatchApplySummary = "summary";
        vm.ResolvedSymbolsCount = 12;
        vm.HelperBridgeState = "ready";
        vm.HelperBridgeReasonCode = "CAPABILITY_PROBE_PASS";
        vm.HelperBridgeFeatures = "spawn_tactical_entity, set_context_allegiance";
        vm.HelperLastOperationToken = "token-123";
        vm.HelperLastOperationKind = "SpawnTacticalEntity";
        vm.HelperLastVerifyState = "applied";
        vm.HelperLastEntryPoint = "SWFOC_Trainer_Spawn_Context";
        vm.HelperLastAppliedEntityId = "EMPIRE_STORMTROOPER_SQUAD";

        vm.SelectedHotkey = new HotkeyBindingItem { Gesture = "Ctrl+1", ActionId = "set_credits", PayloadJson = "{}" };
        vm.SelectedSpawnPreset = new SpawnPresetViewItem("id", "label", "u", "Empire", "AUTO", 1, 0, "desc");

        vm.SelectedUnitHp = "100";
        vm.SelectedUnitShield = "50";
        vm.SelectedUnitSpeed = "1.5";
        vm.SelectedUnitDamageMultiplier = "2.0";
        vm.SelectedUnitCooldownMultiplier = "0.5";
        vm.SelectedUnitVeterancy = "4";
        vm.SelectedUnitOwnerFaction = "2";

        vm.SpawnQuantity = "3";
        vm.SpawnDelayMs = "250";
        vm.SelectedFaction = "REBELLION";
        vm.SelectedEntryMarker = "ENTRY_A";
        vm.SpawnStopOnFailure = false;
        vm.IsStrictPatchApply = false;

        vm.OnboardingBaseProfileId = "base_sweaw";
        vm.OnboardingDraftProfileId = "custom_foo";
        vm.OnboardingDisplayName = "Custom Foo";
        vm.OnboardingNamespaceRoot = "foo";
        vm.OnboardingLaunchSample = "STEAMMOD=123";
        vm.OnboardingSummary = "ok";
        vm.CalibrationNotes = "notes";
        vm.ModCompatibilitySummary = "compat";

        vm.HeroSupportsRespawn = "true";
        vm.HeroSupportsPermadeath = "false";
        vm.HeroSupportsRescue = "true";
        vm.HeroDefaultRespawnTime = "300";
        vm.HeroDuplicatePolicy = "warn";

        vm.OpsArtifactSummary = "artifact";
        vm.SupportBundleOutputDirectory = @"C:\\out";
        vm.LaunchTarget = "Swfoc";
        vm.LaunchMode = "SteamMod";
        vm.LaunchWorkshopId = "1397421866";
        vm.LaunchModPath = "Mods\\Foo";
        vm.TerminateExistingBeforeLaunch = true;
        vm.CreditsValue = "9000";
        vm.CreditsFreeze = true;

        vm.PayloadTemplateApplyCount.Should().Be(1);
        vm.SaveSearchApplyCount.Should().Be(1);
        vm.SelectedProfileId.Should().Be("base_swfoc");
        vm.SelectedActionId.Should().Be("set_credits");
        vm.SaveSearchQuery.Should().Be("credits");
        vm.SelectedSpawnPreset.Should().NotBeNull();
        vm.HelperBridgeSummary.Should().Be("ready (CAPABILITY_PROBE_PASS)");
        vm.HelperLastOperationSummary.Should().Be("SpawnTacticalEntity (applied)");
        vm.HelperLastOperationToken.Should().Be("token-123");
        vm.HelperLastEntryPoint.Should().Be("SWFOC_Trainer_Spawn_Context");
        vm.HelperLastAppliedEntityId.Should().Be("EMPIRE_STORMTROOPER_SQUAD");
        vm.SpawnStopOnFailure.Should().BeFalse();
        vm.IsStrictPatchApply.Should().BeFalse();
        vm.HeroDefaultRespawnTime.Should().Be("300");
        vm.TerminateExistingBeforeLaunch.Should().BeTrue();
        vm.CreditsFreeze.Should().BeTrue();
    }

    [Fact]
    public void SelectedProfileId_ShouldRaisePropertyChanged_ForProfileAndCanWorkWithProfile()
    {
        var vm = new BindableMembersTestDouble();
        var events = new List<string>();
        vm.PropertyChanged += (_, args) => events.Add(args.PropertyName ?? string.Empty);

        vm.SelectedProfileId = "base_swfoc";

        events.Should().Contain(nameof(vm.SelectedProfileId));
        events.Should().Contain(nameof(vm.CanWorkWithProfile));
    }

    [Fact]
    public void SelectedProfileId_ShouldSupportClearingToWhitespace()
    {
        var vm = new BindableMembersTestDouble { SelectedProfileId = "base_swfoc" };

        vm.SelectedProfileId = " ";

        vm.CanWorkWithProfile.Should().BeFalse();
    }

    private sealed class BindableMembersTestDouble : MainViewModelBindableMembersBase
    {
        public BindableMembersTestDouble()
            : base(CreateNullDependencies())
        {
        }

        public int PayloadTemplateApplyCount { get; private set; }

        public int SaveSearchApplyCount { get; private set; }

        protected override void ApplyPayloadTemplateForSelectedAction()
        {
            PayloadTemplateApplyCount++;
        }

        protected override void ApplySaveSearch()
        {
            SaveSearchApplyCount++;
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
    }
}
