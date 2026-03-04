using System.Reflection;
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
        collections.EntityRoster.Should().BeEmpty();
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

