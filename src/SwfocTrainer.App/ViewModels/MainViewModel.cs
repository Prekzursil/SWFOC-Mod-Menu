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
    private string _payloadJson = "{\n  \"symbol\": \"credits\",\n  \"intValue\": 1000000,\n  \"lockCredits\": false\n}";
    private RuntimeMode _runtimeMode = RuntimeMode.Unknown;
    private string _savePath = string.Empty;
    private string _saveNodePath = string.Empty;
    private string _saveEditValue = string.Empty;
    private string _saveSearchQuery = string.Empty;
    private string _savePatchPackPath = string.Empty;
    private string _savePatchMetadataSummary = "No patch pack loaded.";
    private string _savePatchApplySummary = string.Empty;
    private string _creditsValue = "1000000";
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
    private string _onboardingBaseProfileId = "base_swfoc";
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
            _freezeService, _actionReliability, _selectedUnitTransactions, _spawnPresets) =
            (dependencies.Profiles, dependencies.ProcessLocator, dependencies.LaunchContextResolver, dependencies.ProfileVariantResolver,
                dependencies.Runtime, dependencies.Orchestrator, dependencies.Catalog, dependencies.SaveCodec,
                dependencies.SavePatchPackService, dependencies.SavePatchApplyService, dependencies.Helper, dependencies.Updates,
                dependencies.ModOnboarding, dependencies.ModCalibration, dependencies.SupportBundles, dependencies.Telemetry,
                dependencies.FreezeService, dependencies.ActionReliability, dependencies.SelectedUnitTransactions, dependencies.SpawnPresets);

        (Profiles, Actions, CatalogSummary, Updates, SaveDiffPreview, Hotkeys, SaveFields, FilteredSaveFields,
            SavePatchOperations, SavePatchCompatibility, ActionReliability, SelectedUnitTransactions, SpawnPresets, LiveOpsDiagnostics,
            ModCompatibilityRows, ActiveFreezes) = MainViewModelFactories.CreateCollections();

        (LoadProfilesCommand, AttachCommand, DetachCommand, LoadActionsCommand, ExecuteActionCommand, LoadCatalogCommand,
            DeployHelperCommand, VerifyHelperCommand, CheckUpdatesCommand, InstallUpdateCommand, RollbackProfileUpdateCommand) =
            MainViewModelFactories.CreateCoreCommands(new MainViewModelCoreCommandContext
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
            });
        (BrowseSaveCommand, LoadSaveCommand, EditSaveCommand, ValidateSaveCommand, RefreshDiffCommand, WriteSaveCommand,
            BrowsePatchPackCommand, ExportPatchPackCommand, LoadPatchPackCommand, PreviewPatchPackCommand, ApplyPatchPackCommand,
            RestoreBackupCommand, LoadHotkeysCommand, SaveHotkeysCommand, AddHotkeyCommand, RemoveHotkeyCommand) =
            MainViewModelFactories.CreateSaveCommands(new MainViewModelSaveCommandContext
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
                CanLoadSave = () => !string.IsNullOrWhiteSpace(SavePath) && !string.IsNullOrWhiteSpace(SelectedProfileId),
                CanEditSave = () => _loadedSave is not null && !string.IsNullOrWhiteSpace(SaveNodePath),
                CanValidateSave = () => _loadedSave is not null,
                CanRefreshDiff = () => _loadedSave is not null && _loadedSaveOriginal is not null,
                CanWriteSave = () => _loadedSave is not null,
                CanExportPatchPack = () =>
                    _loadedSave is not null &&
                    _loadedSaveOriginal is not null &&
                    !string.IsNullOrWhiteSpace(SelectedProfileId),
                CanLoadPatchPack = () => !string.IsNullOrWhiteSpace(SavePatchPackPath),
                CanPreviewPatchPack = () =>
                    _loadedSave is not null &&
                    _loadedPatchPack is not null &&
                    !string.IsNullOrWhiteSpace(SelectedProfileId),
                CanApplyPatchPack = () =>
                    _loadedPatchPack is not null &&
                    !string.IsNullOrWhiteSpace(SavePath) &&
                    !string.IsNullOrWhiteSpace(SelectedProfileId),
                CanRestoreBackup = () => !string.IsNullOrWhiteSpace(SavePath),
                CanRemoveHotkey = () => SelectedHotkey is not null
            });
        (RefreshActionReliabilityCommand, CaptureSelectedUnitBaselineCommand, ApplySelectedUnitDraftCommand, RevertSelectedUnitTransactionCommand,
            RestoreSelectedUnitBaselineCommand, LoadSpawnPresetsCommand, RunSpawnBatchCommand, ScaffoldModProfileCommand,
            ExportCalibrationArtifactCommand, BuildCompatibilityReportCommand, ExportSupportBundleCommand,
            ExportTelemetrySnapshotCommand) = MainViewModelFactories.CreateLiveOpsCommands(new MainViewModelLiveOpsCommandContext
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
            });
        (QuickSetCreditsCommand, QuickFreezeTimerCommand, QuickToggleFogCommand, QuickToggleAiCommand,
            QuickInstantBuildCommand, QuickUnitCapCommand, QuickGodModeCommand, QuickOneHitCommand, QuickUnfreezeAllCommand) =
            MainViewModelFactories.CreateQuickCommands(new MainViewModelQuickCommandContext
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
            });

        _freezeUiTimer = CreateFreezeUiTimer();
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
