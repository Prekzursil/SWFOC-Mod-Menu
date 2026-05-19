using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Saves.Services;

namespace SwfocTrainer.App.ViewModels;

public sealed class MainViewModelDependencies
{
    public required IProfileRepository Profiles { get; init; }

    public required IProcessLocator ProcessLocator { get; init; }

    public required ILaunchContextResolver LaunchContextResolver { get; init; }

    public required IProfileVariantResolver ProfileVariantResolver { get; init; }

    public required IGameLaunchService GameLauncher { get; init; }

    public required IRuntimeAdapter Runtime { get; init; }

    public required TrainerOrchestrator Orchestrator { get; init; }

    public required ICatalogService Catalog { get; init; }

    public required ISaveCodec SaveCodec { get; init; }

    public required ISavePatchPackService SavePatchPackService { get; init; }

    public required ISavePatchApplyService SavePatchApplyService { get; init; }

    public required IHelperModService Helper { get; init; }

    public required IProfileUpdateService Updates { get; init; }

    public required IModOnboardingService ModOnboarding { get; init; }

    public required IModCalibrationService ModCalibration { get; init; }

    public required ISupportBundleService SupportBundles { get; init; }

    public required ITelemetrySnapshotService Telemetry { get; init; }

    public required IValueFreezeService FreezeService { get; init; }

    public required IActionReliabilityService ActionReliability { get; init; }

    public required ISelectedUnitTransactionService SelectedUnitTransactions { get; init; }

    public required ISpawnPresetService SpawnPresets { get; init; }

    public required IRosterBrowserService RosterBrowser { get; init; }

    public required IFactionDashboardService FactionDashboard { get; init; }

    public required IEnhancedSpawnService EnhancedSpawn { get; init; }

    // Wave 2
    public required IOwnershipTransferService OwnershipTransfer { get; init; }
    public required IPlanetManagerService PlanetManager { get; init; }
    public required IFleetManagerService FleetManager { get; init; }
    public required IFactionSwitchService FactionSwitch { get; init; }

    // Wave 3
    public required IAiControlService AiControl { get; init; }
    public required ICooldownManagerService CooldownManager { get; init; }

    // Wave 4
    public required ICameraDirectorService CameraDirector { get; init; }
    public required IStoryEventService StoryEvents { get; init; }

    // Wave 5
    public required IModConflictDetectorService ModConflicts { get; init; }
    public required IDamageLogService DamageLog { get; init; }

    // Wave 6
    public required IDiplomacyService Diplomacy { get; init; }
    public required ICorruptionService Corruption { get; init; }
}
