using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SwfocTrainer.App.Infrastructure;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Saves.Services;

namespace SwfocTrainer.App.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private const string UniversalProfileId = "universal_auto";
    private const string UnknownValue = "unknown";
    private const string DecimalPrecision3 = "0.###";
    private const string ActionSetCredits = MainViewModelDefaults.ActionSetCredits;
    private const string ActionFreezeTimer = MainViewModelDefaults.ActionFreezeTimer;
    private const string ActionToggleFogReveal = MainViewModelDefaults.ActionToggleFogReveal;
    private const string ActionToggleAi = MainViewModelDefaults.ActionToggleAi;
    private const string ActionSetUnitCap = MainViewModelDefaults.ActionSetUnitCap;
    private const string ActionToggleInstantBuildPatch = MainViewModelDefaults.ActionToggleInstantBuildPatch;
    private const string ActionToggleTacticalGodMode = MainViewModelDefaults.ActionToggleTacticalGodMode;
    private const string ActionToggleTacticalOneHitMode = MainViewModelDefaults.ActionToggleTacticalOneHitMode;
    private const string ActionSetGameSpeed = MainViewModelDefaults.ActionSetGameSpeed;
    private const string ActionFreezeSymbol = MainViewModelDefaults.ActionFreezeSymbol;
    private const string ActionUnfreezeSymbol = MainViewModelDefaults.ActionUnfreezeSymbol;
    private const string PayloadKeySymbol = MainViewModelDefaults.PayloadKeySymbol;
    private const string PayloadKeyIntValue = MainViewModelDefaults.PayloadKeyIntValue;
    private const string PayloadKeyBoolValue = MainViewModelDefaults.PayloadKeyBoolValue;
    private const string PayloadKeyEnable = MainViewModelDefaults.PayloadKeyEnable;
    private const string PayloadKeyFloatValue = MainViewModelDefaults.PayloadKeyFloatValue;
    private const string PayloadKeyFreeze = MainViewModelDefaults.PayloadKeyFreeze;
    private const string PayloadKeyLockCredits = MainViewModelDefaults.PayloadKeyLockCredits;
    private const string SymbolCredits = MainViewModelDefaults.SymbolCredits;
    private const string SymbolGameTimerFreeze = MainViewModelDefaults.SymbolGameTimerFreeze;
    private const string SymbolFogReveal = MainViewModelDefaults.SymbolFogReveal;
    private const string SymbolAiEnabled = MainViewModelDefaults.SymbolAiEnabled;
    private const string SymbolUnitCap = MainViewModelDefaults.SymbolUnitCap;
    private const string SymbolInstantBuildNop = MainViewModelDefaults.SymbolInstantBuildNop;
    private const string SymbolTacticalGodMode = MainViewModelDefaults.SymbolTacticalGodMode;
    private const string SymbolTacticalOneHitMode = MainViewModelDefaults.SymbolTacticalOneHitMode;
    private const string SymbolGameSpeed = MainViewModelDefaults.SymbolGameSpeed;
    private const string BaseSwfocProfileId = MainViewModelDefaults.BaseSwfocProfileId;
    private const int DefaultCreditsValue = MainViewModelDefaults.DefaultCreditsValue;
    private const int DefaultUnitCapValue = MainViewModelDefaults.DefaultUnitCapValue;
    private const float DefaultGameSpeedValue = MainViewModelDefaults.DefaultGameSpeedValue;

    private readonly IProfileRepository _profiles;
    private readonly IProcessLocator _processLocator;
    private readonly ILaunchContextResolver _launchContextResolver;
    private readonly IProfileVariantResolver _profileVariantResolver;
    private readonly IRuntimeAdapter _runtime;
    private readonly TrainerOrchestrator _orchestrator;
    private readonly ICatalogService _catalog;
    private readonly ISaveCodec _saveCodec;
    private readonly ISavePatchPackService _savePatchPackService;
    private readonly ISavePatchApplyService _savePatchApplyService;
    private readonly IHelperModService _helper;
    private readonly IProfileUpdateService _updates;
    private readonly IModOnboardingService _modOnboarding;
    private readonly IModCalibrationService _modCalibration;
    private readonly ISupportBundleService _supportBundles;
    private readonly ITelemetrySnapshotService _telemetry;
    private readonly IValueFreezeService _freezeService;
    private readonly IActionReliabilityService _actionReliability;
    private readonly ISelectedUnitTransactionService _selectedUnitTransactions;
    private readonly ISpawnPresetService _spawnPresets;
    private readonly DispatcherTimer _freezeUiTimer;

    private string? _selectedProfileId;
    private string _status = "Ready";
    private string _selectedActionId = string.Empty;
    private string _payloadJson = MainViewModelDefaults.DefaultPayloadJsonTemplate;
    private RuntimeMode _runtimeMode = RuntimeMode.Unknown;
    private string _savePath = string.Empty;
    private string _saveNodePath = string.Empty;
    private string _saveEditValue = string.Empty;
    private string _saveSearchQuery = string.Empty;
    private string _savePatchPackPath = string.Empty;
    private string _savePatchMetadataSummary = "No patch pack loaded.";
    private string _savePatchApplySummary = string.Empty;
    private string _creditsValue = MainViewModelDefaults.DefaultCreditsValueText;
    private bool _creditsFreeze;
    private int _resolvedSymbolsCount;
    private HotkeyBindingItem? _selectedHotkey;
    private string _selectedUnitHp = string.Empty;
    private string _selectedUnitShield = string.Empty;
    private string _selectedUnitSpeed = string.Empty;
    private string _selectedUnitDamageMultiplier = string.Empty;
    private string _selectedUnitCooldownMultiplier = string.Empty;
    private string _selectedUnitVeterancy = string.Empty;
    private string _selectedUnitOwnerFaction = string.Empty;
    private string _selectedEntryMarker = "AUTO";
    private string _selectedFaction = "EMPIRE";
    private string _spawnQuantity = "1";
    private string _spawnDelayMs = "125";
    private bool _spawnStopOnFailure = true;
    private bool _isStrictPatchApply = true;
    private string _onboardingBaseProfileId = BaseSwfocProfileId;
    private string _onboardingDraftProfileId = "custom_my_mod";
    private string _onboardingDisplayName = "Custom Mod Draft";
    private string _onboardingNamespaceRoot = "custom";
    private string _onboardingLaunchSample = string.Empty;
    private string _onboardingSummary = string.Empty;
    private string _calibrationNotes = string.Empty;
    private string _modCompatibilitySummary = string.Empty;
    private string _opsArtifactSummary = string.Empty;
    private string _supportBundleOutputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwfocTrainer",
        "support");
    private SpawnPresetViewItem? _selectedSpawnPreset;

    private SaveDocument? _loadedSave;
    private byte[]? _loadedSaveOriginal;
    private SavePatchPack? _loadedPatchPack;
    private SavePatchPreview? _loadedPatchPreview;

    private IReadOnlyDictionary<string, ActionSpec> _loadedActionSpecs =
        new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions SavePatchJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly IReadOnlyDictionary<string, string> DefaultSymbolByActionId =
        MainViewModelDefaults.DefaultSymbolByActionId;

    private static readonly IReadOnlyDictionary<string, string> DefaultHelperHookByActionId =
        MainViewModelDefaults.DefaultHelperHookByActionId;

    public MainViewModel(MainViewModelDependencies dependencies)
    {
        (_profiles, _processLocator, _launchContextResolver, _profileVariantResolver, _runtime, _orchestrator, _catalog, _saveCodec,
            _savePatchPackService, _savePatchApplyService, _helper, _updates, _modOnboarding, _modCalibration, _supportBundles, _telemetry,
            _freezeService, _actionReliability, _selectedUnitTransactions, _spawnPresets) = CreateDependencyTuple(dependencies);

        (Profiles, Actions, CatalogSummary, Updates, SaveDiffPreview, Hotkeys, SaveFields, FilteredSaveFields,
            SavePatchOperations, SavePatchCompatibility, ActionReliability, SelectedUnitTransactions, SpawnPresets, LiveOpsDiagnostics,
            ModCompatibilityRows, ActiveFreezes) = MainViewModelFactories.CreateCollections();

        var commandContexts = CreateCommandContexts();
        (LoadProfilesCommand, AttachCommand, DetachCommand, LoadActionsCommand, ExecuteActionCommand, LoadCatalogCommand,
            DeployHelperCommand, VerifyHelperCommand, CheckUpdatesCommand, InstallUpdateCommand, RollbackProfileUpdateCommand) =
            MainViewModelFactories.CreateCoreCommands(commandContexts.Core);
        (BrowseSaveCommand, LoadSaveCommand, EditSaveCommand, ValidateSaveCommand, RefreshDiffCommand, WriteSaveCommand,
            BrowsePatchPackCommand, ExportPatchPackCommand, LoadPatchPackCommand, PreviewPatchPackCommand, ApplyPatchPackCommand,
            RestoreBackupCommand, LoadHotkeysCommand, SaveHotkeysCommand, AddHotkeyCommand, RemoveHotkeyCommand) =
            MainViewModelFactories.CreateSaveCommands(commandContexts.Save);
        (RefreshActionReliabilityCommand, CaptureSelectedUnitBaselineCommand, ApplySelectedUnitDraftCommand, RevertSelectedUnitTransactionCommand,
            RestoreSelectedUnitBaselineCommand, LoadSpawnPresetsCommand, RunSpawnBatchCommand, ScaffoldModProfileCommand,
            ExportCalibrationArtifactCommand, BuildCompatibilityReportCommand, ExportSupportBundleCommand,
            ExportTelemetrySnapshotCommand) = MainViewModelFactories.CreateLiveOpsCommands(commandContexts.LiveOps);
        (QuickSetCreditsCommand, QuickFreezeTimerCommand, QuickToggleFogCommand, QuickToggleAiCommand,
            QuickInstantBuildCommand, QuickUnitCapCommand, QuickGodModeCommand, QuickOneHitCommand, QuickUnfreezeAllCommand) =
            MainViewModelFactories.CreateQuickCommands(commandContexts.Quick);

        _freezeUiTimer = CreateFreezeUiTimer();
    }

    private static (
        IProfileRepository Profiles,
        IProcessLocator ProcessLocator,
        ILaunchContextResolver LaunchContextResolver,
        IProfileVariantResolver ProfileVariantResolver,
        IRuntimeAdapter Runtime,
        TrainerOrchestrator Orchestrator,
        ICatalogService Catalog,
        ISaveCodec SaveCodec,
        ISavePatchPackService SavePatchPackService,
        ISavePatchApplyService SavePatchApplyService,
        IHelperModService Helper,
        IProfileUpdateService Updates,
        IModOnboardingService ModOnboarding,
        IModCalibrationService ModCalibration,
        ISupportBundleService SupportBundles,
        ITelemetrySnapshotService Telemetry,
        IValueFreezeService FreezeService,
        IActionReliabilityService ActionReliability,
        ISelectedUnitTransactionService SelectedUnitTransactions,
        ISpawnPresetService SpawnPresets) CreateDependencyTuple(MainViewModelDependencies dependencies)
    {
        return (
            dependencies.Profiles,
            dependencies.ProcessLocator,
            dependencies.LaunchContextResolver,
            dependencies.ProfileVariantResolver,
            dependencies.Runtime,
            dependencies.Orchestrator,
            dependencies.Catalog,
            dependencies.SaveCodec,
            dependencies.SavePatchPackService,
            dependencies.SavePatchApplyService,
            dependencies.Helper,
            dependencies.Updates,
            dependencies.ModOnboarding,
            dependencies.ModCalibration,
            dependencies.SupportBundles,
            dependencies.Telemetry,
            dependencies.FreezeService,
            dependencies.ActionReliability,
            dependencies.SelectedUnitTransactions,
            dependencies.SpawnPresets);
    }

    private (
        MainViewModelCoreCommandContext Core,
        MainViewModelSaveCommandContext Save,
        MainViewModelLiveOpsCommandContext LiveOps,
        MainViewModelQuickCommandContext Quick) CreateCommandContexts()
    {
        return (
            CreateCoreCommandContext(),
            CreateSaveCommandContext(),
            CreateLiveOpsCommandContext(),
            CreateQuickCommandContext());
    }

    private MainViewModelCoreCommandContext CreateCoreCommandContext()
    {
        return new MainViewModelCoreCommandContext
        {
            LoadProfilesAsync = LoadProfilesAsync,
            AttachAsync = AttachAsync,
            DetachAsync = DetachAsync,
            LoadActionsAsync = LoadActionsAsync,
            ExecuteActionAsync = ExecuteActionAsync,
            LoadCatalogAsync = LoadCatalogAsync,
            DeployHelperAsync = DeployHelperAsync,
            VerifyHelperAsync = VerifyHelperAsync,
            CheckUpdatesAsync = CheckUpdatesAsync,
            InstallUpdateAsync = InstallUpdateAsync,
            RollbackProfileUpdateAsync = RollbackProfileUpdateAsync,
            CanUseSelectedProfile = () => !string.IsNullOrWhiteSpace(SelectedProfileId),
            CanExecuteSelectedAction = () => _runtime.IsAttached && !string.IsNullOrWhiteSpace(SelectedActionId),
            IsAttached = () => _runtime.IsAttached
        };
    }

    private MainViewModelSaveCommandContext CreateSaveCommandContext()
    {
        return new MainViewModelSaveCommandContext
        {
            BrowseSaveAsync = BrowseSaveAsync,
            LoadSaveAsync = LoadSaveAsync,
            EditSaveFieldAsync = EditSaveAsync,
            ValidateSaveAsync = ValidateSaveAsync,
            RefreshSaveDiffPreviewAsync = RefreshDiffAsync,
            WriteSaveAsync = WriteSaveAsync,
            BrowsePatchPackAsync = BrowsePatchPackAsync,
            ExportPatchPackAsync = ExportPatchPackAsync,
            LoadPatchPackAsync = LoadPatchPackAsync,
            PreviewPatchPackAsync = PreviewPatchPackAsync,
            ApplyPatchPackAsync = ApplyPatchPackAsync,
            RestoreSaveBackupAsync = RestoreBackupAsync,
            LoadHotkeysAsync = LoadHotkeysAsync,
            SaveHotkeysAsync = SaveHotkeysAsync,
            AddHotkeyAsync = AddHotkeyAsync,
            RemoveHotkeyAsync = RemoveHotkeyAsync,
            CanLoadSave = CanLoadSaveContext,
            CanEditSave = CanEditSaveContext,
            CanValidateSave = CanValidateSaveContext,
            CanRefreshDiff = CanRefreshDiffContext,
            CanWriteSave = CanWriteSaveContext,
            CanExportPatchPack = CanExportPatchPackContext,
            CanLoadPatchPack = CanLoadPatchPackContext,
            CanPreviewPatchPack = CanPreviewPatchPackContext,
            CanApplyPatchPack = CanApplyPatchPackContext,
            CanRestoreBackup = CanRestoreBackupContext,
            CanRemoveHotkey = CanRemoveHotkeyContext
        };
    }

    private bool CanLoadSaveContext()
        => !string.IsNullOrWhiteSpace(SavePath) && !string.IsNullOrWhiteSpace(SelectedProfileId);

    private bool CanEditSaveContext()
        => _loadedSave is not null && !string.IsNullOrWhiteSpace(SaveNodePath);

    private bool CanValidateSaveContext()
        => _loadedSave is not null;

    private bool CanRefreshDiffContext()
        => _loadedSave is not null && _loadedSaveOriginal is not null;

    private bool CanWriteSaveContext()
        => _loadedSave is not null;

    private bool CanExportPatchPackContext()
        => _loadedSave is not null &&
           _loadedSaveOriginal is not null &&
           !string.IsNullOrWhiteSpace(SelectedProfileId);

    private bool CanLoadPatchPackContext()
        => !string.IsNullOrWhiteSpace(SavePatchPackPath);

    private bool CanPreviewPatchPackContext()
        => _loadedSave is not null &&
           _loadedPatchPack is not null &&
           !string.IsNullOrWhiteSpace(SelectedProfileId);

    private bool CanApplyPatchPackContext()
        => _loadedPatchPack is not null &&
           !string.IsNullOrWhiteSpace(SavePath) &&
           !string.IsNullOrWhiteSpace(SelectedProfileId);

    private bool CanRestoreBackupContext()
        => !string.IsNullOrWhiteSpace(SavePath);

    private bool CanRemoveHotkeyContext()
        => SelectedHotkey is not null;

    private MainViewModelLiveOpsCommandContext CreateLiveOpsCommandContext()
    {
        return new MainViewModelLiveOpsCommandContext
        {
            RefreshActionReliabilityAsync = RefreshActionReliabilityAsync,
            CaptureSelectedUnitBaselineAsync = CaptureSelectedUnitBaselineAsync,
            ApplySelectedUnitDraftAsync = ApplySelectedUnitDraftAsync,
            RevertSelectedUnitTransactionAsync = RevertSelectedUnitTransactionAsync,
            RestoreSelectedUnitBaselineAsync = RestoreSelectedUnitBaselineAsync,
            LoadSpawnPresetsAsync = LoadSpawnPresetsAsync,
            RunSpawnBatchAsync = RunSpawnBatchAsync,
            ScaffoldModProfileAsync = ScaffoldModProfileAsync,
            ExportCalibrationArtifactAsync = ExportCalibrationArtifactAsync,
            BuildModCompatibilityReportAsync = BuildCompatibilityReportAsync,
            ExportSupportBundleAsync = ExportSupportBundleAsync,
            ExportTelemetrySnapshotAsync = ExportTelemetrySnapshotAsync,
            CanRunSpawnBatch = () =>
                _runtime.IsAttached &&
                SelectedSpawnPreset is not null &&
                !string.IsNullOrWhiteSpace(SelectedProfileId),
            CanScaffoldModProfile = () =>
                !string.IsNullOrWhiteSpace(OnboardingDraftProfileId) &&
                !string.IsNullOrWhiteSpace(OnboardingDisplayName),
            CanUseSupportBundleOutputDirectory = () => !string.IsNullOrWhiteSpace(SupportBundleOutputDirectory),
            IsAttached = () => _runtime.IsAttached,
            CanUseSelectedProfile = () => !string.IsNullOrWhiteSpace(SelectedProfileId)
        };
    }

    private MainViewModelQuickCommandContext CreateQuickCommandContext()
    {
        return new MainViewModelQuickCommandContext
        {
            QuickSetCreditsAsync = QuickSetCreditsAsync,
            QuickFreezeTimerAsync = QuickFreezeTimerAsync,
            QuickToggleFogAsync = QuickToggleFogAsync,
            QuickToggleAiAsync = QuickToggleAiAsync,
            QuickInstantBuildAsync = QuickInstantBuildAsync,
            QuickUnitCapAsync = QuickUnitCapAsync,
            QuickGodModeAsync = QuickGodModeAsync,
            QuickOneHitAsync = QuickOneHitAsync,
            QuickUnfreezeAllAsync = QuickUnfreezeAllAsync,
            IsAttached = () => _runtime.IsAttached
        };
    }

    private DispatcherTimer CreateFreezeUiTimer()
    {
        // Periodically refresh the active-freezes list so the UI stays current.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => RefreshActiveFreezes();
        timer.Start();
        return timer;
    }

}
