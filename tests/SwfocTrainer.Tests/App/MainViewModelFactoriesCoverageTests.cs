using System.Reflection;
using System.Windows.Input;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelFactoriesCoverageTests
{
    [Fact]
    public void CreateCollections_ShouldInitializeAllCollections()
    {
        var collections = MainViewModelFactories.CreateCollections();

        collections.Profiles.Should().BeEmpty();
        collections.Actions.Should().BeEmpty();
        collections.CatalogSummary.Should().BeEmpty();
        collections.Updates.Should().BeEmpty();
        collections.SaveDiffPreview.Should().BeEmpty();
        collections.Hotkeys.Should().BeEmpty();
        collections.SaveFields.Should().BeEmpty();
        collections.FilteredSaveFields.Should().BeEmpty();
        collections.SavePatchOperations.Should().BeEmpty();
        collections.SavePatchCompatibility.Should().BeEmpty();
        collections.ActionReliability.Should().BeEmpty();
        collections.SelectedUnitTransactions.Should().BeEmpty();
        collections.SpawnPresets.Should().BeEmpty();
        collections.LiveOpsDiagnostics.Should().BeEmpty();
        collections.ModCompatibilityRows.Should().BeEmpty();
        collections.ActiveFreezes.Should().BeEmpty();
    }

    [Fact]
    public void CreateCoreCommands_ShouldRespectContextPredicates()
    {
        var context = new MainViewModelCoreCommandContext
        {
            LoadProfilesAsync = () => Task.CompletedTask,
            LaunchAndAttachAsync = () => Task.CompletedTask,
            AttachAsync = () => Task.CompletedTask,
            DetachAsync = () => Task.CompletedTask,
            LoadActionsAsync = () => Task.CompletedTask,
            ExecuteActionAsync = () => Task.CompletedTask,
            LoadCatalogAsync = () => Task.CompletedTask,
            DeployHelperAsync = () => Task.CompletedTask,
            VerifyHelperAsync = () => Task.CompletedTask,
            CheckUpdatesAsync = () => Task.CompletedTask,
            InstallUpdateAsync = () => Task.CompletedTask,
            RollbackProfileUpdateAsync = () => Task.CompletedTask,
            CanUseSelectedProfile = () => true,
            CanExecuteSelectedAction = () => false,
            IsAttached = () => false
        };

        var commands = MainViewModelFactories.CreateCoreCommands(context);

        commands.LoadProfiles.CanExecute(null).Should().BeTrue();
        commands.LaunchAndAttach.CanExecute(null).Should().BeTrue();
        commands.Attach.CanExecute(null).Should().BeTrue();
        commands.Detach.CanExecute(null).Should().BeFalse();
        commands.ExecuteAction.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CoreStateConstructor_ShouldWireDependenciesTuple_WithoutMutatingDefaults()
    {
        var dependencies = CreateNullDependencies();
        var instance = new CoreStateTestDouble(dependencies);

        instance.Should().NotBeNull();
        instance.ExportedLaunchTarget.Should().Be(MainViewModelDefaults.DefaultLaunchTarget);
        instance.ExportedLaunchMode.Should().Be(MainViewModelDefaults.DefaultLaunchMode);
        instance.ExportedLaunchWorkshopId.Should().BeEmpty();
    }

    [Fact]
    public void CreateDependencyTuple_ShouldReturnAllMembersInStableOrder()
    {
        var dependencies = CreateNullDependencies();
        var method = typeof(MainViewModelCoreStateBase).GetMethod(
            "CreateDependencyTuple",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tuple = method!.Invoke(null, new object?[] { dependencies });
        tuple.Should().NotBeNull();
        var tupleType = tuple!.GetType();
        tupleType.GetField("Item1", BindingFlags.Instance | BindingFlags.Public).Should().NotBeNull();
        tupleType.GetField("Item2", BindingFlags.Instance | BindingFlags.Public).Should().NotBeNull();
        tupleType.GetField("Rest", BindingFlags.Instance | BindingFlags.Public).Should().NotBeNull();
    }

    [Fact]
    public void MainViewModelConstructor_ShouldWireLaunchAndAttachCommand()
    {
        var instance = new MainViewModel(CreateNullDependencies());

        instance.LaunchAndAttachCommand.Should().NotBeNull();
    }

    [Fact]
    public void CreateSaveCommands_ShouldRespectContextPredicates()
    {
        var context = new MainViewModelSaveCommandContext
        {
            BrowseSaveAsync = () => Task.CompletedTask,
            LoadSaveAsync = () => Task.CompletedTask,
            EditSaveFieldAsync = () => Task.CompletedTask,
            ValidateSaveAsync = () => Task.CompletedTask,
            RefreshSaveDiffPreviewAsync = () => Task.CompletedTask,
            WriteSaveAsync = () => Task.CompletedTask,
            BrowsePatchPackAsync = () => Task.CompletedTask,
            ExportPatchPackAsync = () => Task.CompletedTask,
            LoadPatchPackAsync = () => Task.CompletedTask,
            PreviewPatchPackAsync = () => Task.CompletedTask,
            ApplyPatchPackAsync = () => Task.CompletedTask,
            RestoreSaveBackupAsync = () => Task.CompletedTask,
            LoadHotkeysAsync = () => Task.CompletedTask,
            SaveHotkeysAsync = () => Task.CompletedTask,
            AddHotkeyAsync = () => Task.CompletedTask,
            RemoveHotkeyAsync = () => Task.CompletedTask,
            CanLoadSave = () => false,
            CanEditSave = () => true,
            CanValidateSave = () => false,
            CanRefreshDiff = () => true,
            CanWriteSave = () => false,
            CanExportPatchPack = () => true,
            CanLoadPatchPack = () => false,
            CanPreviewPatchPack = () => true,
            CanApplyPatchPack = () => false,
            CanRestoreBackup = () => true,
            CanRemoveHotkey = () => false
        };

        var commands = MainViewModelFactories.CreateSaveCommands(context);

        commands.BrowseSave.CanExecute(null).Should().BeTrue();
        commands.LoadSave.CanExecute(null).Should().BeFalse();
        commands.EditSave.CanExecute(null).Should().BeTrue();
        commands.ValidateSave.CanExecute(null).Should().BeFalse();
        commands.RefreshDiff.CanExecute(null).Should().BeTrue();
        commands.WriteSave.CanExecute(null).Should().BeFalse();
        commands.BrowsePatchPack.CanExecute(null).Should().BeTrue();
        commands.ExportPatchPack.CanExecute(null).Should().BeTrue();
        commands.LoadPatchPack.CanExecute(null).Should().BeFalse();
        commands.PreviewPatchPack.CanExecute(null).Should().BeTrue();
        commands.ApplyPatchPack.CanExecute(null).Should().BeFalse();
        commands.RestoreBackup.CanExecute(null).Should().BeTrue();
        commands.LoadHotkeys.CanExecute(null).Should().BeTrue();
        commands.SaveHotkeys.CanExecute(null).Should().BeTrue();
        commands.AddHotkey.CanExecute(null).Should().BeTrue();
        commands.RemoveHotkey.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CreateLiveOpsCommands_ShouldEvaluateDynamicGuards()
    {
        var isAttached = false;
        var canUseProfile = false;
        var canRunSpawnBatch = false;
        var canUseSupportBundleOutputDirectory = false;
        var canScaffoldProfile = true;
        var context = new MainViewModelLiveOpsCommandContext
        {
            RefreshActionReliabilityAsync = () => Task.CompletedTask,
            CaptureSelectedUnitBaselineAsync = () => Task.CompletedTask,
            ApplySelectedUnitDraftAsync = () => Task.CompletedTask,
            RevertSelectedUnitTransactionAsync = () => Task.CompletedTask,
            RestoreSelectedUnitBaselineAsync = () => Task.CompletedTask,
            LoadSpawnPresetsAsync = () => Task.CompletedTask,
            RunSpawnBatchAsync = () => Task.CompletedTask,
            ScaffoldModProfileAsync = () => Task.CompletedTask,
            ExportCalibrationArtifactAsync = () => Task.CompletedTask,
            BuildModCompatibilityReportAsync = () => Task.CompletedTask,
            ExportSupportBundleAsync = () => Task.CompletedTask,
            ExportTelemetrySnapshotAsync = () => Task.CompletedTask,
            CanRunSpawnBatch = () => canRunSpawnBatch,
            CanScaffoldModProfile = () => canScaffoldProfile,
            CanUseSupportBundleOutputDirectory = () => canUseSupportBundleOutputDirectory,
            IsAttached = () => isAttached,
            CanUseSelectedProfile = () => canUseProfile
        };

        var commands = MainViewModelFactories.CreateLiveOpsCommands(context);

        commands.RefreshActionReliability.CanExecute(null).Should().BeFalse();
        commands.CaptureSelectedUnitBaseline.CanExecute(null).Should().BeFalse();
        commands.ApplySelectedUnitDraft.CanExecute(null).Should().BeFalse();
        commands.RevertSelectedUnitTransaction.CanExecute(null).Should().BeFalse();
        commands.RestoreSelectedUnitBaseline.CanExecute(null).Should().BeFalse();
        commands.LoadSpawnPresets.CanExecute(null).Should().BeFalse();
        commands.RunSpawnBatch.CanExecute(null).Should().BeFalse();
        commands.ScaffoldModProfile.CanExecute(null).Should().BeTrue();
        commands.ExportCalibrationArtifact.CanExecute(null).Should().BeFalse();
        commands.BuildCompatibilityReport.CanExecute(null).Should().BeFalse();
        commands.ExportSupportBundle.CanExecute(null).Should().BeFalse();
        commands.ExportTelemetrySnapshot.CanExecute(null).Should().BeFalse();

        isAttached = true;
        canUseProfile = true;
        canRunSpawnBatch = true;
        canUseSupportBundleOutputDirectory = true;

        commands.RefreshActionReliability.CanExecute(null).Should().BeTrue();
        commands.CaptureSelectedUnitBaseline.CanExecute(null).Should().BeTrue();
        commands.ApplySelectedUnitDraft.CanExecute(null).Should().BeTrue();
        commands.RevertSelectedUnitTransaction.CanExecute(null).Should().BeTrue();
        commands.RestoreSelectedUnitBaseline.CanExecute(null).Should().BeTrue();
        commands.LoadSpawnPresets.CanExecute(null).Should().BeTrue();
        commands.RunSpawnBatch.CanExecute(null).Should().BeTrue();
        commands.ScaffoldModProfile.CanExecute(null).Should().BeTrue();
        commands.ExportCalibrationArtifact.CanExecute(null).Should().BeTrue();
        commands.BuildCompatibilityReport.CanExecute(null).Should().BeTrue();
        commands.ExportSupportBundle.CanExecute(null).Should().BeTrue();
        commands.ExportTelemetrySnapshot.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CreateQuickCommands_ShouldGateAllCommandsOnAttachmentState()
    {
        var isAttached = false;
        var context = new MainViewModelQuickCommandContext
        {
            QuickSetCreditsAsync = () => Task.CompletedTask,
            QuickFreezeTimerAsync = () => Task.CompletedTask,
            QuickToggleFogAsync = () => Task.CompletedTask,
            QuickToggleAiAsync = () => Task.CompletedTask,
            QuickInstantBuildAsync = () => Task.CompletedTask,
            QuickUnitCapAsync = () => Task.CompletedTask,
            QuickGodModeAsync = () => Task.CompletedTask,
            QuickOneHitAsync = () => Task.CompletedTask,
            QuickUnfreezeAllAsync = () => Task.CompletedTask,
            IsAttached = () => isAttached
        };

        var commands = MainViewModelFactories.CreateQuickCommands(context);

        GetQuickCommands(commands).Should().OnlyContain(command => !command.CanExecute(null));

        isAttached = true;

        GetQuickCommands(commands).Should().OnlyContain(command => command.CanExecute(null));
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

    private static IReadOnlyList<ICommand> GetQuickCommands((
        ICommand QuickSetCredits,
        ICommand QuickFreezeTimer,
        ICommand QuickToggleFog,
        ICommand QuickToggleAi,
        ICommand QuickInstantBuild,
        ICommand QuickUnitCap,
        ICommand QuickGodMode,
        ICommand QuickOneHit,
        ICommand QuickUnfreezeAll) commands)
    {
        return
        [
            commands.QuickSetCredits,
            commands.QuickFreezeTimer,
            commands.QuickToggleFog,
            commands.QuickToggleAi,
            commands.QuickInstantBuild,
            commands.QuickUnitCap,
            commands.QuickGodMode,
            commands.QuickOneHit,
            commands.QuickUnfreezeAll
        ];
    }

    private sealed class CoreStateTestDouble : MainViewModelCoreStateBase
    {
        public CoreStateTestDouble(MainViewModelDependencies dependencies)
            : base(dependencies)
        {
        }

        public string ExportedLaunchTarget => _launchTarget;
        public string ExportedLaunchMode => _launchMode;
        public string ExportedLaunchWorkshopId => _launchWorkshopId;
    }
}
