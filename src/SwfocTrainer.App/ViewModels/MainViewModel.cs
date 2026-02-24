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

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string UniversalProfileId = "universal_auto";
    private const string DefaultBaseSwfocProfileId = "base_swfoc";
    private const string UnknownReason = "unknown";
    private const string CompactFloatFormat = "0.###";

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
    private string _onboardingBaseProfileId = DefaultBaseSwfocProfileId;
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
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_symbol"] = "credits",
            ["set_credits"] = "credits",
            ["freeze_timer"] = "game_timer_freeze",
            ["toggle_fog_reveal"] = "fog_reveal",
            ["toggle_ai"] = "ai_enabled",
            ["set_instant_build_multiplier"] = "instant_build",

            ["set_selected_hp"] = "selected_hp",
            ["set_selected_shield"] = "selected_shield",
            ["set_selected_speed"] = "selected_speed",
            ["set_selected_damage_multiplier"] = "selected_damage_multiplier",
            ["set_selected_cooldown_multiplier"] = "selected_cooldown_multiplier",
            ["set_selected_veterancy"] = "selected_veterancy",
            ["set_selected_owner_faction"] = "selected_owner_faction",

            ["set_planet_owner"] = "planet_owner",
            ["set_hero_respawn_timer"] = "hero_respawn_timer",

            ["toggle_tactical_god_mode"] = "tactical_god_mode",
            ["toggle_tactical_one_hit_mode"] = "tactical_one_hit_mode",
            ["set_game_speed"] = "game_speed",
            ["freeze_symbol"] = "credits",
            ["unfreeze_symbol"] = "credits",
        };

    private static readonly IReadOnlyDictionary<string, string> DefaultHelperHookByActionId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn_unit_helper"] = "spawn_bridge",
            ["set_hero_state_helper"] = "aotr_hero_state_bridge",
            ["toggle_roe_respawn_helper"] = "roe_respawn_bridge",
        };

    public MainViewModel(
        IProfileRepository profiles,
        IProcessLocator processLocator,
        ILaunchContextResolver launchContextResolver,
        IProfileVariantResolver profileVariantResolver,
        IRuntimeAdapter runtime,
        TrainerOrchestrator orchestrator,
        ICatalogService catalog,
        ISaveCodec saveCodec,
        ISavePatchPackService savePatchPackService,
        ISavePatchApplyService savePatchApplyService,
        IHelperModService helper,
        IProfileUpdateService updates,
        IModOnboardingService modOnboarding,
        IModCalibrationService modCalibration,
        ISupportBundleService supportBundles,
        ITelemetrySnapshotService telemetry,
        IValueFreezeService freezeService,
        IActionReliabilityService actionReliability,
        ISelectedUnitTransactionService selectedUnitTransactions,
        ISpawnPresetService spawnPresets)
    {
        _profiles = profiles;
        _processLocator = processLocator;
        _launchContextResolver = launchContextResolver;
        _profileVariantResolver = profileVariantResolver;
        _runtime = runtime;
        _orchestrator = orchestrator;
        _catalog = catalog;
        _saveCodec = saveCodec;
        _savePatchPackService = savePatchPackService;
        _savePatchApplyService = savePatchApplyService;
        _helper = helper;
        _updates = updates;
        _modOnboarding = modOnboarding;
        _modCalibration = modCalibration;
        _supportBundles = supportBundles;
        _telemetry = telemetry;
        _freezeService = freezeService;
        _actionReliability = actionReliability;
        _selectedUnitTransactions = selectedUnitTransactions;
        _spawnPresets = spawnPresets;

        Profiles = new ObservableCollection<string>();
        Actions = new ObservableCollection<string>();
        CatalogSummary = new ObservableCollection<string>();
        Updates = new ObservableCollection<string>();
        SaveDiffPreview = new ObservableCollection<string>();
        Hotkeys = new ObservableCollection<HotkeyBindingItem>();
        SaveFields = new ObservableCollection<SaveFieldViewItem>();
        FilteredSaveFields = new ObservableCollection<SaveFieldViewItem>();
        SavePatchOperations = new ObservableCollection<SavePatchOperationViewItem>();
        SavePatchCompatibility = new ObservableCollection<SavePatchCompatibilityViewItem>();
        ActionReliability = new ObservableCollection<ActionReliabilityViewItem>();
        SelectedUnitTransactions = new ObservableCollection<SelectedUnitTransactionViewItem>();
        SpawnPresets = new ObservableCollection<SpawnPresetViewItem>();
        LiveOpsDiagnostics = new ObservableCollection<string>();
        ModCompatibilityRows = new ObservableCollection<string>();

        LoadProfilesCommand = new AsyncCommand(LoadProfilesAsync);
        AttachCommand = new AsyncCommand(AttachAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        DetachCommand = new AsyncCommand(DetachAsync, () => _runtime.IsAttached);
        LoadActionsCommand = new AsyncCommand(LoadActionsAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        ExecuteActionCommand = new AsyncCommand(ExecuteActionAsync, () => _runtime.IsAttached && !string.IsNullOrWhiteSpace(SelectedActionId));
        LoadCatalogCommand = new AsyncCommand(LoadCatalogAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        DeployHelperCommand = new AsyncCommand(DeployHelperAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        VerifyHelperCommand = new AsyncCommand(VerifyHelperAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        CheckUpdatesCommand = new AsyncCommand(CheckUpdatesAsync);
        InstallUpdateCommand = new AsyncCommand(InstallUpdateAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        RollbackProfileUpdateCommand = new AsyncCommand(RollbackProfileUpdateAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        BrowseSaveCommand = new AsyncCommand(BrowseSaveAsync);
        LoadSaveCommand = new AsyncCommand(LoadSaveAsync, () => !string.IsNullOrWhiteSpace(SavePath) && !string.IsNullOrWhiteSpace(SelectedProfileId));
        EditSaveCommand = new AsyncCommand(EditSaveAsync, () => _loadedSave is not null && !string.IsNullOrWhiteSpace(SaveNodePath));
        ValidateSaveCommand = new AsyncCommand(ValidateSaveAsync, () => _loadedSave is not null);
        RefreshDiffCommand = new AsyncCommand(RefreshDiffAsync, () => _loadedSave is not null && _loadedSaveOriginal is not null);
        WriteSaveCommand = new AsyncCommand(WriteSaveAsync, () => _loadedSave is not null);
        BrowsePatchPackCommand = new AsyncCommand(BrowsePatchPackAsync);
        ExportPatchPackCommand = new AsyncCommand(ExportPatchPackAsync, () => _loadedSave is not null && _loadedSaveOriginal is not null && !string.IsNullOrWhiteSpace(SelectedProfileId));
        LoadPatchPackCommand = new AsyncCommand(LoadPatchPackAsync, () => !string.IsNullOrWhiteSpace(SavePatchPackPath));
        PreviewPatchPackCommand = new AsyncCommand(PreviewPatchPackAsync, () => _loadedSave is not null && _loadedPatchPack is not null && !string.IsNullOrWhiteSpace(SelectedProfileId));
        ApplyPatchPackCommand = new AsyncCommand(ApplyPatchPackAsync, () => _loadedPatchPack is not null && !string.IsNullOrWhiteSpace(SavePath) && !string.IsNullOrWhiteSpace(SelectedProfileId));
        RestoreBackupCommand = new AsyncCommand(RestoreBackupAsync, () => !string.IsNullOrWhiteSpace(SavePath));
        LoadHotkeysCommand = new AsyncCommand(LoadHotkeysAsync);
        SaveHotkeysCommand = new AsyncCommand(SaveHotkeysAsync);
        AddHotkeyCommand = new AsyncCommand(AddHotkeyAsync);
        RemoveHotkeyCommand = new AsyncCommand(RemoveHotkeyAsync, () => SelectedHotkey is not null);
        RefreshActionReliabilityCommand = new AsyncCommand(RefreshActionReliabilityAsync, () => _runtime.IsAttached && !string.IsNullOrWhiteSpace(SelectedProfileId));
        CaptureSelectedUnitBaselineCommand = new AsyncCommand(CaptureSelectedUnitBaselineAsync, () => _runtime.IsAttached);
        ApplySelectedUnitDraftCommand = new AsyncCommand(ApplySelectedUnitDraftAsync, () => _runtime.IsAttached);
        RevertSelectedUnitTransactionCommand = new AsyncCommand(RevertSelectedUnitTransactionAsync, () => _runtime.IsAttached);
        RestoreSelectedUnitBaselineCommand = new AsyncCommand(RestoreSelectedUnitBaselineAsync, () => _runtime.IsAttached);
        LoadSpawnPresetsCommand = new AsyncCommand(LoadSpawnPresetsAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        RunSpawnBatchCommand = new AsyncCommand(RunSpawnBatchAsync, () => _runtime.IsAttached && SelectedSpawnPreset is not null && !string.IsNullOrWhiteSpace(SelectedProfileId));
        ScaffoldModProfileCommand = new AsyncCommand(ScaffoldModProfileAsync, () => !string.IsNullOrWhiteSpace(OnboardingDraftProfileId) && !string.IsNullOrWhiteSpace(OnboardingDisplayName));
        ExportCalibrationArtifactCommand = new AsyncCommand(ExportCalibrationArtifactAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        BuildCompatibilityReportCommand = new AsyncCommand(BuildCompatibilityReportAsync, () => !string.IsNullOrWhiteSpace(SelectedProfileId));
        ExportSupportBundleCommand = new AsyncCommand(ExportSupportBundleAsync, () => !string.IsNullOrWhiteSpace(SupportBundleOutputDirectory));
        ExportTelemetrySnapshotCommand = new AsyncCommand(ExportTelemetrySnapshotAsync, () => !string.IsNullOrWhiteSpace(SupportBundleOutputDirectory));

        // Quick-action commands (one-click trainer buttons)
        QuickSetCreditsCommand = new AsyncCommand(QuickSetCreditsAsync, () => _runtime.IsAttached);
        QuickFreezeTimerCommand = new AsyncCommand(QuickFreezeTimerAsync, () => _runtime.IsAttached);
        QuickToggleFogCommand = new AsyncCommand(QuickToggleFogAsync, () => _runtime.IsAttached);
        QuickToggleAiCommand = new AsyncCommand(QuickToggleAiAsync, () => _runtime.IsAttached);
        QuickInstantBuildCommand = new AsyncCommand(QuickInstantBuildAsync, () => _runtime.IsAttached);
        QuickUnitCapCommand = new AsyncCommand(QuickUnitCapAsync, () => _runtime.IsAttached);
        QuickGodModeCommand = new AsyncCommand(QuickGodModeAsync, () => _runtime.IsAttached);
        QuickOneHitCommand = new AsyncCommand(QuickOneHitAsync, () => _runtime.IsAttached);
        QuickUnfreezeAllCommand = new AsyncCommand(QuickUnfreezeAllAsync, () => _runtime.IsAttached);

        ActiveFreezes = new ObservableCollection<string>();

        // Periodically refresh the active-freezes list so the UI stays current.
        _freezeUiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _freezeUiTimer.Tick += (_, _) => RefreshActiveFreezes();
        _freezeUiTimer.Start();
    }

    public ObservableCollection<string> Profiles { get; }

    public ObservableCollection<string> Actions { get; }

    public ObservableCollection<string> CatalogSummary { get; }

    public ObservableCollection<string> Updates { get; }

    public ObservableCollection<string> SaveDiffPreview { get; }

    public ObservableCollection<HotkeyBindingItem> Hotkeys { get; }

    public ObservableCollection<string> ActiveFreezes { get; }

    public ObservableCollection<SaveFieldViewItem> SaveFields { get; }

    public ObservableCollection<SaveFieldViewItem> FilteredSaveFields { get; }

    public ObservableCollection<SavePatchOperationViewItem> SavePatchOperations { get; }

    public ObservableCollection<SavePatchCompatibilityViewItem> SavePatchCompatibility { get; }

    public ObservableCollection<ActionReliabilityViewItem> ActionReliability { get; }

    public ObservableCollection<SelectedUnitTransactionViewItem> SelectedUnitTransactions { get; }

    public ObservableCollection<SpawnPresetViewItem> SpawnPresets { get; }

    public ObservableCollection<string> LiveOpsDiagnostics { get; }

    public ObservableCollection<string> ModCompatibilityRows { get; }

    public string? SelectedProfileId
    {
        get => _selectedProfileId;
        set
        {
            if (SetField(ref _selectedProfileId, value))
            {
                OnPropertyChanged(nameof(CanWorkWithProfile));
            }
        }
    }

    public string SelectedActionId
    {
        get => _selectedActionId;
        set
        {
            if (SetField(ref _selectedActionId, value))
            {
                ApplyPayloadTemplateForSelectedAction();
            }
        }
    }

    public string PayloadJson
    {
        get => _payloadJson;
        set => SetField(ref _payloadJson, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public RuntimeMode RuntimeMode
    {
        get => _runtimeMode;
        set => SetField(ref _runtimeMode, value);
    }

    public string SavePath
    {
        get => _savePath;
        set => SetField(ref _savePath, value);
    }

    public string SaveNodePath
    {
        get => _saveNodePath;
        set => SetField(ref _saveNodePath, value);
    }

    public string SaveEditValue
    {
        get => _saveEditValue;
        set => SetField(ref _saveEditValue, value);
    }

    public string SaveSearchQuery
    {
        get => _saveSearchQuery;
        set
        {
            if (SetField(ref _saveSearchQuery, value))
            {
                ApplySaveSearch();
            }
        }
    }

    public string SavePatchPackPath
    {
        get => _savePatchPackPath;
        set
        {
            if (SetField(ref _savePatchPackPath, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string SavePatchMetadataSummary
    {
        get => _savePatchMetadataSummary;
        set => SetField(ref _savePatchMetadataSummary, value);
    }

    public string SavePatchApplySummary
    {
        get => _savePatchApplySummary;
        set => SetField(ref _savePatchApplySummary, value);
    }

    public int ResolvedSymbolsCount
    {
        get => _resolvedSymbolsCount;
        set => SetField(ref _resolvedSymbolsCount, value);
    }

    public bool CanWorkWithProfile => !string.IsNullOrWhiteSpace(SelectedProfileId);

    public HotkeyBindingItem? SelectedHotkey
    {
        get => _selectedHotkey;
        set => SetField(ref _selectedHotkey, value);
    }

    public SpawnPresetViewItem? SelectedSpawnPreset
    {
        get => _selectedSpawnPreset;
        set
        {
            if (SetField(ref _selectedSpawnPreset, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string SelectedUnitHp
    {
        get => _selectedUnitHp;
        set => SetField(ref _selectedUnitHp, value);
    }

    public string SelectedUnitShield
    {
        get => _selectedUnitShield;
        set => SetField(ref _selectedUnitShield, value);
    }

    public string SelectedUnitSpeed
    {
        get => _selectedUnitSpeed;
        set => SetField(ref _selectedUnitSpeed, value);
    }

    public string SelectedUnitDamageMultiplier
    {
        get => _selectedUnitDamageMultiplier;
        set => SetField(ref _selectedUnitDamageMultiplier, value);
    }

    public string SelectedUnitCooldownMultiplier
    {
        get => _selectedUnitCooldownMultiplier;
        set => SetField(ref _selectedUnitCooldownMultiplier, value);
    }

    public string SelectedUnitVeterancy
    {
        get => _selectedUnitVeterancy;
        set => SetField(ref _selectedUnitVeterancy, value);
    }

    public string SelectedUnitOwnerFaction
    {
        get => _selectedUnitOwnerFaction;
        set => SetField(ref _selectedUnitOwnerFaction, value);
    }

    public string SpawnQuantity
    {
        get => _spawnQuantity;
        set => SetField(ref _spawnQuantity, value);
    }

    public string SpawnDelayMs
    {
        get => _spawnDelayMs;
        set => SetField(ref _spawnDelayMs, value);
    }

    public string SelectedFaction
    {
        get => _selectedFaction;
        set => SetField(ref _selectedFaction, value);
    }

    public string SelectedEntryMarker
    {
        get => _selectedEntryMarker;
        set => SetField(ref _selectedEntryMarker, value);
    }

    public bool SpawnStopOnFailure
    {
        get => _spawnStopOnFailure;
        set => SetField(ref _spawnStopOnFailure, value);
    }

    public bool IsStrictPatchApply
    {
        get => _isStrictPatchApply;
        set => SetField(ref _isStrictPatchApply, value);
    }

    public string OnboardingBaseProfileId
    {
        get => _onboardingBaseProfileId;
        set => SetField(ref _onboardingBaseProfileId, value);
    }

    public string OnboardingDraftProfileId
    {
        get => _onboardingDraftProfileId;
        set => SetField(ref _onboardingDraftProfileId, value);
    }

    public string OnboardingDisplayName
    {
        get => _onboardingDisplayName;
        set => SetField(ref _onboardingDisplayName, value);
    }

    public string OnboardingNamespaceRoot
    {
        get => _onboardingNamespaceRoot;
        set => SetField(ref _onboardingNamespaceRoot, value);
    }

    public string OnboardingLaunchSample
    {
        get => _onboardingLaunchSample;
        set => SetField(ref _onboardingLaunchSample, value);
    }

    public string OnboardingSummary
    {
        get => _onboardingSummary;
        set => SetField(ref _onboardingSummary, value);
    }

    public string CalibrationNotes
    {
        get => _calibrationNotes;
        set => SetField(ref _calibrationNotes, value);
    }

    public string ModCompatibilitySummary
    {
        get => _modCompatibilitySummary;
        set => SetField(ref _modCompatibilitySummary, value);
    }

    public string OpsArtifactSummary
    {
        get => _opsArtifactSummary;
        set => SetField(ref _opsArtifactSummary, value);
    }

    public string SupportBundleOutputDirectory
    {
        get => _supportBundleOutputDirectory;
        set => SetField(ref _supportBundleOutputDirectory, value);
    }

    public ICommand LoadProfilesCommand { get; }

    public ICommand AttachCommand { get; }

    public ICommand DetachCommand { get; }

    public ICommand LoadActionsCommand { get; }

    public ICommand ExecuteActionCommand { get; }

    public ICommand LoadCatalogCommand { get; }

    public ICommand DeployHelperCommand { get; }

    public ICommand VerifyHelperCommand { get; }

    public ICommand CheckUpdatesCommand { get; }

    public ICommand InstallUpdateCommand { get; }

    public ICommand RollbackProfileUpdateCommand { get; }

    public ICommand BrowseSaveCommand { get; }

    public ICommand LoadSaveCommand { get; }

    public ICommand EditSaveCommand { get; }

    public ICommand ValidateSaveCommand { get; }

    public ICommand RefreshDiffCommand { get; }

    public ICommand WriteSaveCommand { get; }

    public ICommand BrowsePatchPackCommand { get; }

    public ICommand ExportPatchPackCommand { get; }

    public ICommand LoadPatchPackCommand { get; }

    public ICommand PreviewPatchPackCommand { get; }

    public ICommand ApplyPatchPackCommand { get; }

    public ICommand RestoreBackupCommand { get; }

    public ICommand LoadHotkeysCommand { get; }

    public ICommand SaveHotkeysCommand { get; }

    public ICommand AddHotkeyCommand { get; }

    public ICommand RemoveHotkeyCommand { get; }

    public ICommand RefreshActionReliabilityCommand { get; }

    public ICommand CaptureSelectedUnitBaselineCommand { get; }

    public ICommand ApplySelectedUnitDraftCommand { get; }

    public ICommand RevertSelectedUnitTransactionCommand { get; }

    public ICommand RestoreSelectedUnitBaselineCommand { get; }

    public ICommand LoadSpawnPresetsCommand { get; }

    public ICommand RunSpawnBatchCommand { get; }

    public ICommand ScaffoldModProfileCommand { get; }

    public ICommand ExportCalibrationArtifactCommand { get; }

    public ICommand BuildCompatibilityReportCommand { get; }

    public ICommand ExportSupportBundleCommand { get; }

    public ICommand ExportTelemetrySnapshotCommand { get; }

    public string CreditsValue
    {
        get => _creditsValue;
        set => SetField(ref _creditsValue, value);
    }

    public bool CreditsFreeze
    {
        get => _creditsFreeze;
        set => SetField(ref _creditsFreeze, value);
    }

    // Quick-action commands
    public ICommand QuickSetCreditsCommand { get; }
    public ICommand QuickFreezeTimerCommand { get; }
    public ICommand QuickToggleFogCommand { get; }
    public ICommand QuickToggleAiCommand { get; }
    public ICommand QuickInstantBuildCommand { get; }
    public ICommand QuickUnitCapCommand { get; }
    public ICommand QuickGodModeCommand { get; }
    public ICommand QuickOneHitCommand { get; }
    public ICommand QuickUnfreezeAllCommand { get; }

    private async Task LoadProfilesAsync()
    {
        Profiles.Clear();
        var ids = await _profiles.ListAvailableProfilesAsync();
        foreach (var id in ids)
        {
            Profiles.Add(id);
        }

        var recommended = await RecommendProfileIdAsync();
        if (string.IsNullOrWhiteSpace(SelectedProfileId) || !Profiles.Contains(SelectedProfileId))
        {
            var resolvedProfileId = Profiles.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(recommended) && Profiles.Contains(recommended))
            {
                resolvedProfileId = recommended;
            }

            if (Profiles.Contains(UniversalProfileId))
            {
                resolvedProfileId = UniversalProfileId;
            }

            SelectedProfileId = resolvedProfileId;
        }

        Status = !string.IsNullOrWhiteSpace(recommended)
            ? $"Loaded {Profiles.Count} profiles (recommended: {recommended})"
            : $"Loaded {Profiles.Count} profiles";

        // Reduce friction: show actions for the selected profile immediately.
        if (!string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            await LoadActionsAsync();
            await LoadSpawnPresetsAsync();
        }
    }

    private async Task<string?> RecommendProfileIdAsync()
    {
        // Prefer attaching to whatever is actually running, and select a profile accordingly.
        // This avoids a common mistake: defaulting to base_sweaw when only swfoc.exe is running.
        try
        {
            var processes = await _processLocator.FindSupportedProcessesAsync();
            if (processes.Count == 0)
            {
                return null;
            }

            var resolvedProfiles = await LoadResolvedProfilesForLaunchContextAsync();
            var contexts = processes
                .Select(process =>
                {
                    var context = process.LaunchContext ?? _launchContextResolver.Resolve(process, resolvedProfiles);
                    return new { Process = process, Context = context };
                })
                .ToArray();

            var bestRecommendation = contexts
                .Where(x => !string.IsNullOrWhiteSpace(x.Context.Recommendation.ProfileId))
                .OrderByDescending(x => x.Context.Recommendation.Confidence)
                .ThenByDescending(x => x.Context.LaunchKind == LaunchKind.Workshop || x.Context.LaunchKind == LaunchKind.Mixed)
                .Select(x => x.Context.Recommendation.ProfileId)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(bestRecommendation))
            {
                return bestRecommendation;
            }

            // First priority: explicit mod IDs in command line or parsed metadata.
            if (HasSteamModId(processes, "3447786229"))
            {
                return "roe_3447786229_swfoc";
            }

            if (HasSteamModId(processes, "1397421866"))
            {
                return "aotr_1397421866_swfoc";
            }

            var swfoc = processes.FirstOrDefault(x => x.ExeTarget == ExeTarget.Swfoc);
            if (swfoc is not null)
            {
                return DefaultBaseSwfocProfileId;
            }

            var starWarsG = processes.FirstOrDefault(IsStarWarsGProcess);
            if (starWarsG is not null)
            {
                // FoC-safe default when StarWarsG is running but command-line hints are unavailable.
                return DefaultBaseSwfocProfileId;
            }

            var sweaw = processes.FirstOrDefault(x => x.ExeTarget == ExeTarget.Sweaw);
            if (sweaw is not null)
            {
                return "base_sweaw";
            }
        }
        catch
        {
            // If process enumeration fails (permissions/WMI), don't block the UI.
        }

        return null;
    }

    private async Task<IReadOnlyList<TrainerProfile>> LoadResolvedProfilesForLaunchContextAsync()
    {
        var ids = await _profiles.ListAvailableProfilesAsync();
        var profiles = new List<TrainerProfile>(ids.Count);
        foreach (var id in ids)
        {
            profiles.Add(await _profiles.ResolveInheritedProfileAsync(id));
        }

        return profiles;
    }

    private async Task AttachAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        try
        {
            var requestedProfileId = SelectedProfileId;
            var effectiveProfileId = requestedProfileId;
            ProfileVariantResolution? variant = null;

            if (string.Equals(requestedProfileId, UniversalProfileId, StringComparison.OrdinalIgnoreCase))
            {
                var processes = await _processLocator.FindSupportedProcessesAsync();
                variant = await _profileVariantResolver.ResolveAsync(requestedProfileId, processes, CancellationToken.None);
                effectiveProfileId = variant.ResolvedProfileId;
            }

            Status = variant is null
                ? $"Attaching using profile '{effectiveProfileId}'..."
                : $"Attaching using universal profile -> '{effectiveProfileId}' ({variant.ReasonCode}, conf={variant.Confidence:0.00})...";

            var session = await _runtime.AttachAsync(effectiveProfileId);
            if (variant is not null)
            {
                SelectedProfileId = effectiveProfileId;
            }
            RuntimeMode = session.Process.Mode;
            ResolvedSymbolsCount = session.Symbols.Symbols.Count;
            var signatureCount = session.Symbols.Symbols.Values.Count(x => x.Source == AddressSource.Signature);
            var fallbackCount = session.Symbols.Symbols.Values.Count(x => x.Source == AddressSource.Fallback);
            var healthyCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Healthy);
            var degradedCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Degraded);
            var unresolvedCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Unresolved || x.Address == nint.Zero);
            Status = $"Attached to PID {session.Process.ProcessId} ({session.Process.ProcessName}) | " +
                     $"{BuildProcessDiagnosticSummary(session.Process)} | symbols: sig={signatureCount}, fallback={fallbackCount}, healthy={healthyCount}, degraded={degradedCount}, unresolved={unresolvedCount}";

            // Most people expect the Action dropdown to be usable immediately after attach.
            // Loading actions is profile-driven and doesn't require a process attach, but
            // doing it here avoids a common "Action is empty" confusion.
            await LoadActionsAsync();
            await LoadSpawnPresetsAsync();
            RefreshLiveOpsDiagnostics();
            await RefreshActionReliabilityAsync();
        }
        catch (Exception ex)
        {
            RuntimeMode = RuntimeMode.Unknown;
            ResolvedSymbolsCount = 0;
            var processHint = await BuildAttachProcessHintAsync();
            Status = $"Attach failed: {ex.Message}. {processHint}";
        }
    }

    private async Task<string> BuildAttachProcessHintAsync()
    {
        try
        {
            var all = await _processLocator.FindSupportedProcessesAsync();
            if (all.Count == 0)
            {
                return "Detected game processes: none. Ensure the game is running and try launching trainer as Administrator.";
            }

            var summary = string.Join(", ", all
                .Take(3)
                .Select(x =>
                {
                    var cmd = x.Metadata is not null &&
                              x.Metadata.TryGetValue("commandLineAvailable", out var cmdValue)
                        ? cmdValue
                        : "False";
                    var mods = x.Metadata is not null &&
                               x.Metadata.TryGetValue("steamModIdsDetected", out var ids)
                        ? ids
                        : string.Empty;
                    var via = x.Metadata is not null &&
                              x.Metadata.TryGetValue("detectedVia", out var detectedVia)
                        ? detectedVia
                        : UnknownReason;
                    var ctx = x.LaunchContext;
                    var launch = ctx is null ? "n/a" : ctx.LaunchKind.ToString();
                    var recommended = ctx?.Recommendation.ProfileId ?? string.Empty;
                    var reason = ctx?.Recommendation.ReasonCode ?? UnknownReason;
                    var confidence = ctx is null ? "0.00" : ctx.Recommendation.Confidence.ToString("0.00");
                    return $"{x.ProcessName}:{x.ProcessId}:{x.ExeTarget}:cmd={cmd}:mods={mods}:launch={launch}:rec={recommended}:{reason}:{confidence}:via={via}";
                }));
            var more = all.Count > 3 ? $", +{all.Count - 3} more" : string.Empty;
            return $"Detected game processes: {summary}{more}";
        }
        catch
        {
            return "Could not enumerate process diagnostics.";
        }
    }

    private static bool HasSteamModId(IEnumerable<ProcessMetadata> processes, string workshopId)
    {
        foreach (var process in processes)
        {
            if (process.LaunchContext is not null &&
                process.LaunchContext.SteamModIds.Any(id => id.Equals(workshopId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (process.CommandLine?.Contains(workshopId, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            if (process.Metadata is not null &&
                process.Metadata.TryGetValue("steamModIdsDetected", out var ids) &&
                !string.IsNullOrWhiteSpace(ids))
            {
                var split = ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (split.Any(x => x.Equals(workshopId, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsStarWarsGProcess(ProcessMetadata process)
    {
        if (process.ProcessName.Equals("StarWarsG", StringComparison.OrdinalIgnoreCase) ||
            process.ProcessName.Equals("StarWarsG.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (process.Metadata is not null &&
            process.Metadata.TryGetValue("isStarWarsG", out var raw) &&
            bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return process.ProcessPath.Contains("StarWarsG.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProcessDiagnosticSummary(ProcessMetadata process)
    {
        var cmdAvailable = process.Metadata is not null &&
                           process.Metadata.TryGetValue("commandLineAvailable", out var cmdRaw)
            ? cmdRaw
            : "False";
        var mods = "none";
        if (process.Metadata is not null &&
            process.Metadata.TryGetValue("steamModIdsDetected", out var ids) &&
            !string.IsNullOrWhiteSpace(ids))
        {
            mods = ids;
        }
        var via = process.Metadata is not null &&
                  process.Metadata.TryGetValue("detectedVia", out var detectedVia)
            ? detectedVia
            : UnknownReason;
        var dependencyState = process.Metadata is not null &&
                              process.Metadata.TryGetValue("dependencyValidation", out var validation)
            ? validation
            : "Pass";
        var dependencyMessage = process.Metadata is not null &&
                                process.Metadata.TryGetValue("dependencyValidationMessage", out var message)
            ? message
            : string.Empty;

        var dependencySegment = dependencyState.Equals("Pass", StringComparison.OrdinalIgnoreCase) ||
                                string.IsNullOrWhiteSpace(dependencyMessage)
            ? $"dependency={dependencyState}"
            : $"dependency={dependencyState} ({dependencyMessage})";
        var fallbackRate = process.Metadata is not null &&
                           process.Metadata.TryGetValue("fallbackHitRate", out var fallbackRateRaw)
            ? fallbackRateRaw
            : "n/a";
        var unresolvedRate = process.Metadata is not null &&
                             process.Metadata.TryGetValue("unresolvedSymbolRate", out var unresolvedRateRaw)
            ? unresolvedRateRaw
            : "n/a";
        var resolvedVariant = process.Metadata is not null &&
                              process.Metadata.TryGetValue("resolvedVariant", out var resolvedVariantRaw)
            ? resolvedVariantRaw
            : "n/a";
        var resolvedVariantReason = process.Metadata is not null &&
                                    process.Metadata.TryGetValue("resolvedVariantReasonCode", out var resolvedVariantReasonRaw)
            ? resolvedVariantReasonRaw
            : "n/a";
        var resolvedVariantConfidence = process.Metadata is not null &&
                                        process.Metadata.TryGetValue("resolvedVariantConfidence", out var resolvedVariantConfidenceRaw)
            ? resolvedVariantConfidenceRaw
            : "0.00";
        var launchKind = process.LaunchContext?.LaunchKind.ToString() ?? "Unknown";
        var modPath = process.LaunchContext?.ModPathNormalized;
        var recProfile = process.LaunchContext?.Recommendation.ProfileId ?? "none";
        var recReason = process.LaunchContext?.Recommendation.ReasonCode ?? UnknownReason;
        var recConfidence = process.LaunchContext is null
            ? "0.00"
            : process.LaunchContext.Recommendation.Confidence.ToString("0.00");
        var hostRole = process.HostRole.ToString().ToLowerInvariant();
        var moduleSize = process.MainModuleSize > 0 ? process.MainModuleSize.ToString(CultureInfo.InvariantCulture) : "n/a";
        var workshopMatchCount = process.WorkshopMatchCount.ToString(CultureInfo.InvariantCulture);
        var selectionScore = process.SelectionScore.ToString("0.00", CultureInfo.InvariantCulture);
        var modPathSegment = string.IsNullOrWhiteSpace(modPath) ? "modPath=none" : $"modPath={modPath}";
        return $"target={process.ExeTarget} | launch={launchKind} | hostRole={hostRole} | score={selectionScore} | module={moduleSize} | workshopMatches={workshopMatchCount} | cmdLine={cmdAvailable} | mods={mods} | {modPathSegment} | rec={recProfile}:{recReason}:{recConfidence} | variant={resolvedVariant}:{resolvedVariantReason}:{resolvedVariantConfidence} | via={via} | {dependencySegment} | fallbackRate={fallbackRate} | unresolvedRate={unresolvedRate}";
    }

    private static bool IsActionAvailableForCurrentSession(string actionId, ActionSpec spec, AttachSession session)
    {
        return IsActionAvailableForCurrentSession(actionId, spec, session, out _);
    }

    private static bool IsActionAvailableForCurrentSession(
        string actionId,
        ActionSpec spec,
        AttachSession session,
        out string? unavailableReason)
    {
        unavailableReason = ResolveActionUnavailableReason(actionId, spec, session);
        return string.IsNullOrWhiteSpace(unavailableReason);
    }

    private static string? ResolveActionUnavailableReason(
        string actionId,
        ActionSpec spec,
        AttachSession session)
    {
        if (IsDependencyDisabledAction(actionId, session))
        {
            return "action is disabled by dependency validation for this attachment.";
        }

        var requiredSymbol = ResolveRequiredSymbolForSessionGate(actionId, spec);
        if (string.IsNullOrWhiteSpace(requiredSymbol))
        {
            return null;
        }

        if (!session.Symbols.TryGetValue(requiredSymbol, out var symbolInfo) ||
            symbolInfo is null ||
            symbolInfo.Address == nint.Zero ||
            symbolInfo.HealthStatus == SymbolHealthStatus.Unresolved)
        {
            return $"required symbol '{requiredSymbol}' is unresolved for this attachment.";
        }

        return null;
    }

    private static bool IsDependencyDisabledAction(string actionId, AttachSession session)
    {
        if (session.Process.Metadata is null ||
            !session.Process.Metadata.TryGetValue("dependencyDisabledActions", out var disabledIdsRaw) ||
            string.IsNullOrWhiteSpace(disabledIdsRaw))
        {
            return false;
        }

        var disabledIds = disabledIdsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return disabledIds.Any(x => x.Equals(actionId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveRequiredSymbolForSessionGate(string actionId, ActionSpec spec)
    {
        if (spec.ExecutionKind is not (ExecutionKind.Memory or ExecutionKind.CodePatch or ExecutionKind.Freeze or ExecutionKind.Sdk))
        {
            return null;
        }

        if (!spec.PayloadSchema.TryGetPropertyValue("required", out var requiredNode) || requiredNode is not JsonArray required)
        {
            return null;
        }

        var requiresSymbol = required.Any(x => string.Equals(x?.GetValue<string>(), "symbol", StringComparison.OrdinalIgnoreCase));
        if (!requiresSymbol)
        {
            return null;
        }

        return DefaultSymbolByActionId.TryGetValue(actionId, out var symbol) && !string.IsNullOrWhiteSpace(symbol)
            ? symbol
            : null;
    }

    private async Task<ActionSpec?> ResolveActionSpecAsync(string actionId)
    {
        if (_loadedActionSpecs.TryGetValue(actionId, out var actionSpec))
        {
            return actionSpec;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return null;
        }

        try
        {
            var profile = await _profiles.ResolveInheritedProfileAsync(SelectedProfileId);
            _loadedActionSpecs = profile.Actions;
            return _loadedActionSpecs.TryGetValue(actionId, out actionSpec) ? actionSpec : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> EnsureActionAvailableForCurrentSessionAsync(string actionId, string statusPrefix)
    {
        var session = _runtime.CurrentSession;
        if (session is null)
        {
            return true;
        }

        var actionSpec = await ResolveActionSpecAsync(actionId);
        if (actionSpec is null)
        {
            return true;
        }

        if (IsActionAvailableForCurrentSession(actionId, actionSpec, session, out var unavailableReason))
        {
            return true;
        }

        var reason = string.IsNullOrWhiteSpace(unavailableReason)
            ? "action is unavailable for this attachment."
            : unavailableReason;
        Status = $"✗ {statusPrefix}: {reason}";
        return false;
    }

    private async Task DetachAsync()
    {
        _orchestrator.UnfreezeAll();
        await _runtime.DetachAsync();
        ActionReliability.Clear();
        SelectedUnitTransactions.Clear();
        LiveOpsDiagnostics.Clear();
        Status = "Detached";
    }

    private async Task LoadActionsAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        Actions.Clear();
        var profile = await _profiles.ResolveInheritedProfileAsync(SelectedProfileId);
        _loadedActionSpecs = profile.Actions;
        var filteredOut = 0;
        foreach (var (actionId, actionSpec) in profile.Actions.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (_runtime.CurrentSession is not null &&
                !IsActionAvailableForCurrentSession(actionId, actionSpec, _runtime.CurrentSession))
            {
                filteredOut++;
                continue;
            }

            Actions.Add(actionId);
        }

        SelectedActionId = Actions.FirstOrDefault() ?? string.Empty;
        Status = filteredOut > 0
            ? $"Loaded {Actions.Count} actions ({filteredOut} hidden: unresolved symbols)"
            : $"Loaded {Actions.Count} actions";

        if (_runtime.IsAttached)
        {
            await RefreshActionReliabilityAsync();
        }
    }

    private async Task ExecuteActionAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        try
        {
            JsonObject payloadNode;
            try
            {
                payloadNode = JsonNode.Parse(PayloadJson) as JsonObject ?? new JsonObject();
            }
            catch (Exception ex)
            {
                Status = $"Invalid payload JSON: {ex.Message}";
                return;
            }

            var result = await _orchestrator.ExecuteAsync(
                SelectedProfileId,
                SelectedActionId,
                payloadNode,
                RuntimeMode,
                BuildActionContext(SelectedActionId));
            Status = result.Succeeded
                ? $"Action succeeded: {result.Message}{BuildDiagnosticsStatusSuffix(result)}"
                : $"Action failed: {result.Message}{BuildDiagnosticsStatusSuffix(result)}";
        }
        catch (Exception ex)
        {
            Status = $"Action failed: {ex.Message}";
        }
    }

    private void ApplyPayloadTemplateForSelectedAction()
    {
        if (string.IsNullOrWhiteSpace(SelectedActionId))
        {
            return;
        }

        if (!_loadedActionSpecs.TryGetValue(SelectedActionId, out var action))
        {
            return;
        }

        if (!action.PayloadSchema.TryGetPropertyValue("required", out var requiredNode) || requiredNode is not JsonArray required)
        {
            return;
        }

        var payload = new JsonObject();

        foreach (var node in required)
        {
            var key = node?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            payload[key] = key switch
            {
                "symbol" => DefaultSymbolByActionId.TryGetValue(SelectedActionId, out var sym) ? sym : string.Empty,
                "intValue" => SelectedActionId switch
                {
                    "set_credits" => 1000000,
                    "set_unit_cap" => 99999,
                    _ => 0
                },
                "floatValue" => (float)1.0,
                "boolValue" => true,
                "enable" => true,
                "freeze" => !SelectedActionId.Equals("unfreeze_symbol", StringComparison.OrdinalIgnoreCase),
                "patchBytes" => "90 90 90 90 90",
                "originalBytes" => "48 8B 74 24 68",
                "helperHookId" => DefaultHelperHookByActionId.TryGetValue(SelectedActionId, out var hook) ? hook : SelectedActionId,
                "unitId" => string.Empty,
                "entryMarker" => string.Empty,
                "faction" => string.Empty,
                "globalKey" => string.Empty,
                "nodePath" => string.Empty,
                "value" => string.Empty,
                _ => string.Empty
            };
        }

        if (SelectedActionId.Equals("set_credits", StringComparison.OrdinalIgnoreCase))
        {
            payload["lockCredits"] = false;
        }

        // For freeze_symbol, include a default intValue so the user has a working template.
        if (SelectedActionId.Equals("freeze_symbol", StringComparison.OrdinalIgnoreCase) && !payload.ContainsKey("intValue"))
        {
            payload["intValue"] = 1000000;
        }

        // Only apply a template when it would actually help. Don't clobber the user's JSON with "{}".
        if (payload.Count == 0)
        {
            return;
        }

        PayloadJson = payload.ToJsonString(PrettyJson);
    }

    private async Task LoadCatalogAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        CatalogSummary.Clear();
        var catalog = await _catalog.LoadCatalogAsync(SelectedProfileId);
        foreach (var kv in catalog)
        {
            CatalogSummary.Add($"{kv.Key}: {kv.Value.Count}");
        }

        Status = $"Catalog loaded for {SelectedProfileId}";
    }

    private async Task DeployHelperAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var path = await _helper.DeployAsync(SelectedProfileId);
        Status = $"Helper deployed to: {path}";
    }

    private async Task VerifyHelperAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var ok = await _helper.VerifyAsync(SelectedProfileId);
        Status = ok ? "Helper verification passed" : "Helper verification failed";
    }

    private async Task CheckUpdatesAsync()
    {
        Updates.Clear();
        var updates = await _updates.CheckForUpdatesAsync();
        foreach (var profile in updates)
        {
            Updates.Add(profile);
        }

        Status = updates.Count > 0 ? $"Updates available for {updates.Count} profile(s)" : "No profile updates";
    }

    private async Task InstallUpdateAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var result = await _updates.InstallProfileTransactionalAsync(SelectedProfileId);
        if (!result.Succeeded)
        {
            Status = $"Profile update failed: {result.Message}";
            OpsArtifactSummary = $"install failed ({result.ReasonCode ?? UnknownReason})";
            return;
        }

        Status = $"Installed profile update: {result.InstalledPath}";
        var receiptPart = string.IsNullOrWhiteSpace(result.ReceiptPath) ? "no receipt" : result.ReceiptPath;
        var backupPart = string.IsNullOrWhiteSpace(result.BackupPath) ? "no backup" : result.BackupPath;
        OpsArtifactSummary = $"install receipt: {receiptPart} | backup: {backupPart}";
    }

    private async Task RollbackProfileUpdateAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var rollback = await _updates.RollbackLastInstallAsync(SelectedProfileId);
        if (!rollback.Restored)
        {
            Status = $"Rollback failed: {rollback.Message}";
            OpsArtifactSummary = $"rollback failed ({rollback.ReasonCode ?? UnknownReason})";
            return;
        }

        Status = rollback.Message;
        OpsArtifactSummary = $"rollback source: {rollback.BackupPath ?? "n/a"}";
    }

    private async Task ScaffoldModProfileAsync()
    {
        var launchLines = OnboardingLaunchSample
            .Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        var launchSamples = launchLines
            .Select(line => new ModLaunchSample(ProcessName: null, ProcessPath: null, CommandLine: line))
            .ToArray();

        var request = new ModOnboardingRequest(
            DraftProfileId: OnboardingDraftProfileId,
            DisplayName: OnboardingDisplayName,
            BaseProfileId: string.IsNullOrWhiteSpace(OnboardingBaseProfileId) ? DefaultBaseSwfocProfileId : OnboardingBaseProfileId,
            LaunchSamples: launchSamples,
            ProfileAliases: new[] { OnboardingDraftProfileId, OnboardingDisplayName },
            NamespaceRoot: OnboardingNamespaceRoot,
            Notes: "Generated by Mod Compatibility Studio");

        var result = await _modOnboarding.ScaffoldDraftProfileAsync(request);
        var warnings = result.Warnings.Count == 0
            ? "none"
            : string.Join("; ", result.Warnings);

        OnboardingSummary = $"draft={result.ProfileId} output={result.OutputPath} workshop=[{string.Join(',', result.InferredWorkshopIds)}] hints=[{string.Join(',', result.InferredPathHints)}] warnings={warnings}";
        Status = $"Draft profile scaffolded: {result.ProfileId}";
    }

    private async Task ExportCalibrationArtifactAsync()
    {
        var profileId = SelectedProfileId ?? OnboardingDraftProfileId;
        var outputDir = Path.Combine(SupportBundleOutputDirectory, "calibration");
        Directory.CreateDirectory(outputDir);

        var request = new ModCalibrationArtifactRequest(
            ProfileId: profileId,
            OutputDirectory: outputDir,
            Session: _runtime.CurrentSession,
            OperatorNotes: CalibrationNotes);

        var result = await _modCalibration.ExportCalibrationArtifactAsync(request);
        OpsArtifactSummary = result.ArtifactPath;
        Status = result.Succeeded
            ? $"Calibration artifact exported: {result.ArtifactPath}"
            : "Calibration artifact export failed.";
    }

    private async Task BuildCompatibilityReportAsync()
    {
        var profileId = SelectedProfileId ?? OnboardingDraftProfileId;
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId);
        var report = await _modCalibration.BuildCompatibilityReportAsync(
            profile,
            _runtime.CurrentSession);

        ModCompatibilityRows.Clear();
        foreach (var action in report.Actions)
        {
            ModCompatibilityRows.Add($"{action.ActionId} | {action.State} | {action.ReasonCode} | {action.Confidence:0.00}");
        }

        ModCompatibilitySummary = $"promotionReady={report.PromotionReady} dependency={report.DependencyStatus} unresolvedCritical={report.UnresolvedCriticalSymbols}";
        Status = $"Compatibility report generated for {profileId}";
    }

    private async Task ExportSupportBundleAsync()
    {
        var result = await _supportBundles.ExportAsync(new SupportBundleRequest(
            OutputDirectory: SupportBundleOutputDirectory,
            ProfileId: SelectedProfileId,
            Notes: "Exported from Profiles & Updates tab"));

        OpsArtifactSummary = result.BundlePath;
        Status = result.Succeeded
            ? $"Support bundle exported: {result.BundlePath}"
            : "Support bundle export failed.";
    }

    private async Task ExportTelemetrySnapshotAsync()
    {
        var telemetryDir = Path.Combine(SupportBundleOutputDirectory, "telemetry");
        Directory.CreateDirectory(telemetryDir);
        var path = await _telemetry.ExportSnapshotAsync(telemetryDir);
        OpsArtifactSummary = path;
        Status = $"Telemetry snapshot exported: {path}";
    }

    private Task BrowseSaveAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Save files (*.sav)|*.sav|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SavePath = dialog.FileName;
            Status = $"Selected save: {SavePath}";
        }

        return Task.CompletedTask;
    }

    private Task LoadSaveAsync()
        => LoadSaveAsync(clearPatchSummary: true);

    private async Task LoadSaveAsync(bool clearPatchSummary)
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var profile = await _profiles.ResolveInheritedProfileAsync(SelectedProfileId);
        _loadedSave = await _saveCodec.LoadAsync(SavePath, profile.SaveSchemaId);
        _loadedSaveOriginal = _loadedSave.Raw.ToArray();
        RebuildSaveFieldRows();
        await RefreshDiffAsync();
        ClearPatchPreviewState(clearLoadedPack: false);
        if (clearPatchSummary)
        {
            SavePatchApplySummary = string.Empty;
        }

        Status = $"Loaded save with schema {profile.SaveSchemaId} ({_loadedSave.Raw.Length} bytes)";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task EditSaveAsync()
    {
        if (_loadedSave is null)
        {
            return;
        }

        object? value = ParsePrimitive(SaveEditValue);
        await _saveCodec.EditAsync(_loadedSave, SaveNodePath, value);
        RebuildSaveFieldRows();
        await RefreshDiffAsync();
        ClearPatchPreviewState(clearLoadedPack: false);
        Status = $"Edited save field: {SaveNodePath}";
    }

    private async Task ValidateSaveAsync()
    {
        if (_loadedSave is null)
        {
            return;
        }

        var result = await _saveCodec.ValidateAsync(_loadedSave);
        Status = result.IsValid
            ? $"Save validation passed ({result.Warnings.Count} warning(s))"
            : $"Save validation failed ({result.Errors.Count} error(s))";
    }

    private async Task WriteSaveAsync()
    {
        if (_loadedSave is null)
        {
            return;
        }

        var output = TrustedPathPolicy.BuildSiblingFilePath(_loadedSave.Path, ".edited");
        TrustedPathPolicy.EnsureAllowedExtension(output, ".sav");

        await _saveCodec.WriteAsync(_loadedSave, output);
        Status = $"Wrote edited save: {output}";
    }

    private Task BrowsePatchPackAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Patch pack (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SavePatchPackPath = dialog.FileName;
            Status = $"Selected patch pack: {SavePatchPackPath}";
        }

        return Task.CompletedTask;
    }

    private async Task ExportPatchPackAsync()
    {
        if (_loadedSave is null || _loadedSaveOriginal is null || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        var originalDocument = _loadedSave with { Raw = _loadedSaveOriginal.ToArray() };
        var pack = await _savePatchPackService.ExportAsync(originalDocument, _loadedSave, SelectedProfileId);

        var dialog = new SaveFileDialog
        {
            Filter = "Patch pack (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(_loadedSave.Path)}.patch.json",
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() != true)
        {
            Status = "Patch-pack export canceled.";
            return;
        }

        var outputPath = TrustedPathPolicy.NormalizeAbsolute(dialog.FileName);
        TrustedPathPolicy.EnsureAllowedExtension(outputPath, ".json");
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Patch-pack export path has no parent directory.");
        }

        Directory.CreateDirectory(outputDirectory);
        var json = JsonSerializer.Serialize(pack, SavePatchJson);
        await File.WriteAllTextAsync(outputPath, json);

        SetLoadedPatchPack(pack, outputPath);
        SavePatchApplySummary = string.Empty;
        Status = $"Exported patch pack ({pack.Operations.Count} op(s)): {outputPath}";
    }

    private async Task LoadPatchPackAsync()
    {
        var pack = await _savePatchPackService.LoadPackAsync(SavePatchPackPath);
        SetLoadedPatchPack(pack, SavePatchPackPath);
        SavePatchApplySummary = string.Empty;
        Status = $"Loaded patch pack ({pack.Operations.Count} op(s)).";
    }

    private async Task PreviewPatchPackAsync()
    {
        if (_loadedPatchPack is null || _loadedSave is null || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (!ValidateSaveRuntimeVariant(SelectedProfileId, out var variantMessage))
        {
            SavePatchCompatibility.Clear();
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("error", "save_variant_mismatch", variantMessage));
            SavePatchApplySummary = variantMessage;
            Status = variantMessage;
            return;
        }

        var compatibility = await _savePatchPackService.ValidateCompatibilityAsync(_loadedPatchPack, _loadedSave, SelectedProfileId);
        var preview = await _savePatchPackService.PreviewApplyAsync(_loadedPatchPack, _loadedSave, SelectedProfileId);
        _loadedPatchPreview = preview;

        SavePatchOperations.Clear();
        foreach (var operation in preview.OperationsToApply)
        {
            SavePatchOperations.Add(new SavePatchOperationViewItem(
                operation.Kind.ToString(),
                operation.FieldPath,
                operation.FieldId,
                operation.ValueType,
                FormatPatchValue(operation.OldValue),
                FormatPatchValue(operation.NewValue)));
        }

        SavePatchCompatibility.Clear();
        SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem(
            "info",
            "source_hash_match",
            compatibility.SourceHashMatches ? "Source hash matches target save." : "Source hash mismatch (strict apply blocks this)."));
        SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem(
            "info",
            "strict_apply_mode",
            IsStrictPatchApply
                ? "Strict apply is ON: source hash mismatch blocks apply."
                : "Strict apply is OFF: source hash mismatch warning will not block apply."));
        foreach (var warning in compatibility.Warnings)
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("warning", "compatibility_warning", warning));
        }

        foreach (var warning in preview.Warnings)
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("warning", "preview_warning", warning));
        }

        foreach (var error in compatibility.Errors)
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("error", "compatibility_error", error));
        }

        foreach (var error in preview.Errors)
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("error", "preview_error", error));
        }

        SavePatchMetadataSummary =
            $"Patch {(_loadedPatchPack.Metadata.SchemaVersion)} | profile={_loadedPatchPack.Metadata.ProfileId} | schema={_loadedPatchPack.Metadata.SchemaId} | ops={_loadedPatchPack.Operations.Count}";
        SavePatchApplySummary = string.Empty;
        Status = preview.IsCompatible && compatibility.IsCompatible
            ? $"Patch preview ready: {SavePatchOperations.Count} operation(s) would be applied."
            : "Patch preview blocked by compatibility/validation errors.";
    }

    private async Task ApplyPatchPackAsync()
    {
        if (_loadedPatchPack is null || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (!ValidateSaveRuntimeVariant(SelectedProfileId, out var variantMessage))
        {
            SavePatchApplySummary = variantMessage;
            Status = variantMessage;
            return;
        }

        var expectedOperationCount = _loadedPatchPreview?.OperationsToApply.Count ?? _loadedPatchPack.Operations.Count;
        var result = await _savePatchApplyService.ApplyAsync(SavePath, _loadedPatchPack, SelectedProfileId, strict: IsStrictPatchApply);
        var summary = $"{result.Classification}: {result.Message}";
        if (result.Applied)
        {
            await LoadSaveAsync(clearPatchSummary: false);
            SavePatchApplySummary = summary;
            AppendPatchArtifactRows(result.BackupPath, result.ReceiptPath);
        }
        else
        {
            SavePatchApplySummary = summary;
        }

        Status = result.Applied
            ? $"Patch applied successfully ({result.Classification}, ops={expectedOperationCount})."
            : $"Patch apply failed ({result.Classification}): {result.Message}";
    }

    private async Task RestoreBackupAsync()
    {
        var result = await _savePatchApplyService.RestoreLastBackupAsync(SavePath);
        var summary = result.Message;
        if (result.Restored && !string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            await LoadSaveAsync(clearPatchSummary: false);
            SavePatchApplySummary = summary;
            AppendPatchArtifactRows(result.BackupPath, null);
        }
        else
        {
            SavePatchApplySummary = summary;
        }

        Status = result.Restored
            ? $"Backup restored: {result.BackupPath}"
            : $"Backup restore skipped: {result.Message}";
    }

    private bool ValidateSaveRuntimeVariant(string requestedProfileId, out string message)
    {
        message = string.Empty;
        var session = _runtime.CurrentSession;
        if (session?.Process.Metadata is null)
        {
            return true;
        }

        if (!session.Process.Metadata.TryGetValue("resolvedVariant", out var runtimeVariant) ||
            string.IsNullOrWhiteSpace(runtimeVariant))
        {
            return true;
        }

        if (requestedProfileId.Equals(UniversalProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (runtimeVariant.Equals(requestedProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        message = $"Blocked by runtime/save variant mismatch (reasonCode=save_variant_mismatch): runtime={runtimeVariant}, selected={requestedProfileId}.";
        return false;
    }

    private Task RefreshDiffAsync()
    {
        SaveDiffPreview.Clear();

        if (_loadedSaveOriginal is null || _loadedSave is null)
        {
            return Task.CompletedTask;
        }

        var diff = SaveDiffService.BuildDiffPreview(_loadedSaveOriginal, _loadedSave.Raw, 400);
        foreach (var line in diff)
        {
            SaveDiffPreview.Add(line);
        }

        if (SaveDiffPreview.Count == 0)
        {
            SaveDiffPreview.Add("No differences detected.");
        }

        return Task.CompletedTask;
    }

    private void RebuildSaveFieldRows()
    {
        SaveFields.Clear();
        if (_loadedSave is null)
        {
            return;
        }

        foreach (var row in FlattenNodes(_loadedSave.Root))
        {
            SaveFields.Add(row);
        }

        ApplySaveSearch();
    }

    private IEnumerable<SaveFieldViewItem> FlattenNodes(SaveNode root)
    {
        if (root.Children is null || root.Children.Count == 0)
        {
            if (!string.Equals(root.ValueType, "root", StringComparison.OrdinalIgnoreCase))
            {
                yield return new SaveFieldViewItem(root.Path, root.Name, root.ValueType, root.Value?.ToString() ?? string.Empty);
            }

            yield break;
        }

        foreach (var child in root.Children)
        {
            foreach (var nested in FlattenNodes(child))
            {
                yield return nested;
            }
        }
    }

    private void ApplySaveSearch()
    {
        FilteredSaveFields.Clear();
        IEnumerable<SaveFieldViewItem> source = SaveFields;

        if (!string.IsNullOrWhiteSpace(SaveSearchQuery))
        {
            source = source.Where(x =>
                x.Path.Contains(SaveSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains(SaveSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                x.Value.Contains(SaveSearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in source.Take(5000))
        {
            FilteredSaveFields.Add(row);
        }
    }

    private void SetLoadedPatchPack(SavePatchPack pack, string path)
    {
        _loadedPatchPack = pack;
        SavePatchPackPath = path;
        SavePatchMetadataSummary =
            $"Patch {pack.Metadata.SchemaVersion} | profile={pack.Metadata.ProfileId} | schema={pack.Metadata.SchemaId} | ops={pack.Operations.Count}";
        ClearPatchPreviewState(clearLoadedPack: false);
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearPatchPreviewState(bool clearLoadedPack)
    {
        if (clearLoadedPack)
        {
            _loadedPatchPack = null;
            SavePatchMetadataSummary = "No patch pack loaded.";
        }

        _loadedPatchPreview = null;
        SavePatchOperations.Clear();
        SavePatchCompatibility.Clear();
        CommandManager.InvalidateRequerySuggested();
    }

    private void AppendPatchArtifactRows(string? backupPath, string? receiptPath)
    {
        if (!string.IsNullOrWhiteSpace(backupPath))
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("info", "backup_path", backupPath));
        }

        if (!string.IsNullOrWhiteSpace(receiptPath))
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("info", "receipt_path", receiptPath));
        }
    }

    private static string FormatPatchValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Null => "null",
                _ => element.ToString()
            };
        }

        return value.ToString() ?? string.Empty;
    }

    private static object ParsePrimitive(string input)
    {
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (bool.TryParse(input, out var boolValue))
        {
            return boolValue;
        }

        var trimmed = input.Trim();
        if (trimmed.EndsWith("f", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(trimmed[..^1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var floatValue))
        {
            return floatValue;
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        return input;
    }

    private async Task RefreshActionReliabilityAsync()
    {
        ActionReliability.Clear();
        if (SelectedProfileId is null || _runtime.CurrentSession is null)
        {
            return;
        }

        RefreshLiveOpsDiagnostics();

        var profile = await _profiles.ResolveInheritedProfileAsync(SelectedProfileId);
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog = null;
        try
        {
            catalog = await _catalog.LoadCatalogAsync(SelectedProfileId);
        }
        catch
        {
            // Catalog is optional for reliability scoring.
        }

        var reliability = _actionReliability.Evaluate(profile, _runtime.CurrentSession, catalog);
        foreach (var item in reliability)
        {
            ActionReliability.Add(new ActionReliabilityViewItem(
                item.ActionId,
                item.State.ToString().ToLowerInvariant(),
                item.ReasonCode,
                item.Confidence,
                item.Detail ?? string.Empty));
        }
    }

    private void RefreshLiveOpsDiagnostics()
    {
        LiveOpsDiagnostics.Clear();
        var session = _runtime.CurrentSession;
        if (session is null)
        {
            return;
        }

        var metadata = session.Process.Metadata;
        LiveOpsDiagnostics.Add($"mode: {session.Process.Mode}");
        if (metadata is not null && metadata.TryGetValue("runtimeModeReasonCode", out var modeReason))
        {
            LiveOpsDiagnostics.Add($"mode_reason: {modeReason}");
        }

        LiveOpsDiagnostics.Add($"launch: {session.Process.LaunchContext?.LaunchKind ?? LaunchKind.Unknown}");
        LiveOpsDiagnostics.Add($"recommendation: {session.Process.LaunchContext?.Recommendation.ProfileId ?? "none"}");
        if (metadata is not null && metadata.TryGetValue("resolvedVariant", out var resolvedVariant))
        {
            var reason = GetMetadataValueOrDefault(metadata, "resolvedVariantReasonCode", UnknownReason);
            var confidence = GetMetadataValueOrDefault(metadata, "resolvedVariantConfidence", "0.00");
            LiveOpsDiagnostics.Add($"variant: {resolvedVariant} ({reason}, conf={confidence})");
        }

        if (metadata is not null && metadata.TryGetValue("dependencyValidation", out var dependency))
        {
            var dependencyMessage = GetMetadataValueOrDefault(metadata, "dependencyValidationMessage", string.Empty);
            LiveOpsDiagnostics.Add(BuildDependencyDiagnostic(dependency, dependencyMessage));
        }

        var healthy = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Healthy);
        var degraded = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Degraded);
        var unresolved = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Unresolved || x.Address == nint.Zero);
        LiveOpsDiagnostics.Add($"symbols: healthy={healthy}, degraded={degraded}, unresolved={unresolved}");
    }

    private static string GetMetadataValueOrDefault(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string fallback)
    {
        return metadata.TryGetValue(key, out var value) ? value : fallback;
    }

    private static string BuildDependencyDiagnostic(string dependency, string dependencyMessage)
    {
        return string.IsNullOrWhiteSpace(dependencyMessage)
            ? $"dependency: {dependency}"
            : $"dependency: {dependency} ({dependencyMessage})";
    }

    private async Task CaptureSelectedUnitBaselineAsync()
    {
        if (!_runtime.IsAttached)
        {
            Status = "✗ Not attached to game.";
            return;
        }

        try
        {
            var snapshot = await _selectedUnitTransactions.CaptureAsync();
            ApplyDraftFromSnapshot(snapshot);
            RefreshSelectedUnitTransactions();
            Status = $"Selected-unit baseline captured at {snapshot.CapturedAt:HH:mm:ss} UTC.";
        }
        catch (Exception ex)
        {
            Status = $"✗ Capture selected-unit baseline failed: {ex.Message}";
        }
    }

    private async Task ApplySelectedUnitDraftAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var draftResult = BuildSelectedUnitDraft();
        if (!draftResult.Succeeded)
        {
            Status = $"✗ {draftResult.Message}";
            return;
        }

        var result = await _selectedUnitTransactions.ApplyAsync(SelectedProfileId, draftResult.Draft!, RuntimeMode);
        RefreshSelectedUnitTransactions();
        if (result.Succeeded)
        {
            var latest = await _selectedUnitTransactions.CaptureAsync();
            ApplyDraftFromSnapshot(latest);
        }

        Status = result.Succeeded
            ? $"✓ Selected-unit transaction applied ({result.TransactionId})."
            : $"✗ Selected-unit apply failed: {result.Message}";
    }

    private async Task RevertSelectedUnitTransactionAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var result = await _selectedUnitTransactions.RevertLastAsync(SelectedProfileId, RuntimeMode);
        RefreshSelectedUnitTransactions();
        if (result.Succeeded)
        {
            var latest = await _selectedUnitTransactions.CaptureAsync();
            ApplyDraftFromSnapshot(latest);
        }

        Status = result.Succeeded
            ? $"✓ Reverted selected-unit transaction ({result.TransactionId})."
            : $"✗ Revert failed: {result.Message}";
    }

    private async Task RestoreSelectedUnitBaselineAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var result = await _selectedUnitTransactions.RestoreBaselineAsync(SelectedProfileId, RuntimeMode);
        RefreshSelectedUnitTransactions();
        if (result.Succeeded)
        {
            var latest = await _selectedUnitTransactions.CaptureAsync();
            ApplyDraftFromSnapshot(latest);
        }

        Status = result.Succeeded
            ? $"✓ Selected-unit baseline restored ({result.TransactionId})."
            : $"✗ Baseline restore failed: {result.Message}";
    }

    private async Task LoadSpawnPresetsAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        SpawnPresets.Clear();
        var presets = await _spawnPresets.LoadPresetsAsync(SelectedProfileId);
        foreach (var preset in presets)
        {
            SpawnPresets.Add(new SpawnPresetViewItem(
                preset.Id,
                preset.Name,
                preset.UnitId,
                preset.Faction,
                preset.EntryMarker,
                preset.DefaultQuantity,
                preset.DefaultDelayMs,
                preset.Description ?? string.Empty));
        }

        SelectedSpawnPreset = SpawnPresets.FirstOrDefault();
        Status = $"Loaded {SpawnPresets.Count} spawn preset(s).";
    }

    private async Task RunSpawnBatchAsync()
    {
        if (SelectedProfileId is null || SelectedSpawnPreset is null)
        {
            return;
        }

        if (RuntimeMode == RuntimeMode.Unknown)
        {
            Status = "✗ Spawn batch blocked: runtime mode is unknown.";
            return;
        }

        if (!int.TryParse(SpawnQuantity, out var quantity) || quantity <= 0)
        {
            Status = "✗ Invalid spawn quantity.";
            return;
        }

        if (!int.TryParse(SpawnDelayMs, out var delayMs) || delayMs < 0)
        {
            Status = "✗ Invalid spawn delay (ms).";
            return;
        }

        var preset = SelectedSpawnPreset.ToCorePreset();
        var plan = _spawnPresets.BuildBatchPlan(
            SelectedProfileId,
            preset,
            quantity,
            delayMs,
            SelectedFaction,
            SelectedEntryMarker,
            SpawnStopOnFailure);

        var result = await _spawnPresets.ExecuteBatchAsync(SelectedProfileId, plan, RuntimeMode);
        Status = result.Succeeded
            ? $"✓ {result.Message}"
            : $"✗ {result.Message}";
    }

    private void ApplyDraftFromSnapshot(SelectedUnitSnapshot snapshot)
    {
        SelectedUnitHp = snapshot.Hp.ToString(CompactFloatFormat);
        SelectedUnitShield = snapshot.Shield.ToString(CompactFloatFormat);
        SelectedUnitSpeed = snapshot.Speed.ToString(CompactFloatFormat);
        SelectedUnitDamageMultiplier = snapshot.DamageMultiplier.ToString(CompactFloatFormat);
        SelectedUnitCooldownMultiplier = snapshot.CooldownMultiplier.ToString(CompactFloatFormat);
        SelectedUnitVeterancy = snapshot.Veterancy.ToString();
        SelectedUnitOwnerFaction = snapshot.OwnerFaction.ToString();
    }

    private void RefreshSelectedUnitTransactions()
    {
        SelectedUnitTransactions.Clear();
        foreach (var item in _selectedUnitTransactions.History.OrderByDescending(x => x.Timestamp))
        {
            SelectedUnitTransactions.Add(new SelectedUnitTransactionViewItem(
                item.TransactionId,
                item.Timestamp,
                item.IsRollback,
                item.Message,
                string.Join(",", item.AppliedActions)));
        }
    }

    private DraftBuildResult BuildSelectedUnitDraft()
    {
        if (!TryParseOptionalFloat(SelectedUnitHp, out var hp))
        {
            return DraftBuildResult.Failed("HP must be a number.");
        }

        if (!TryParseOptionalFloat(SelectedUnitShield, out var shield))
        {
            return DraftBuildResult.Failed("Shield must be a number.");
        }

        if (!TryParseOptionalFloat(SelectedUnitSpeed, out var speed))
        {
            return DraftBuildResult.Failed("Speed must be a number.");
        }

        if (!TryParseOptionalFloat(SelectedUnitDamageMultiplier, out var damage))
        {
            return DraftBuildResult.Failed("Damage multiplier must be a number.");
        }

        if (!TryParseOptionalFloat(SelectedUnitCooldownMultiplier, out var cooldown))
        {
            return DraftBuildResult.Failed("Cooldown multiplier must be a number.");
        }

        if (!TryParseOptionalInt(SelectedUnitVeterancy, out var veterancy))
        {
            return DraftBuildResult.Failed("Veterancy must be an integer.");
        }

        if (!TryParseOptionalInt(SelectedUnitOwnerFaction, out var ownerFaction))
        {
            return DraftBuildResult.Failed("Owner faction must be an integer.");
        }

        var draft = new SelectedUnitDraft(
            Hp: hp,
            Shield: shield,
            Speed: speed,
            DamageMultiplier: damage,
            CooldownMultiplier: cooldown,
            Veterancy: veterancy,
            OwnerFaction: ownerFaction);

        return draft.IsEmpty
            ? DraftBuildResult.Failed("No selected-unit values entered.")
            : DraftBuildResult.FromDraft(draft);
    }

    private static bool TryParseOptionalFloat(string input, out float? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (float.TryParse(input, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryParseOptionalInt(string input, out int? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (int.TryParse(input, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private IReadOnlyDictionary<string, object?> BuildActionContext(string actionId)
    {
        var reliability = ActionReliability.FirstOrDefault(x => x.ActionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        return new Dictionary<string, object?>
        {
            ["reliabilityState"] = reliability?.State ?? UnknownReason,
            ["reliabilityReasonCode"] = reliability?.ReasonCode ?? UnknownReason,
            ["bundleGateResult"] = ResolveBundleGateResult(reliability)
        };
    }

    private static string ResolveBundleGateResult(ActionReliabilityViewItem? reliability)
    {
        if (reliability is null)
        {
            return UnknownReason;
        }

        return reliability.State == "unavailable" ? "blocked" : "bundle_pass";
    }

    private static string BuildDiagnosticsStatusSuffix(ActionExecutionResult result)
    {
        if (result.Diagnostics is null)
        {
            return string.Empty;
        }

        var segments = new List<string>(capacity: 5);
        AppendDiagnosticSegment(segments, result.Diagnostics, "backend", "backend", "backendRoute");
        AppendDiagnosticSegment(segments, result.Diagnostics, "routeReasonCode", "routeReasonCode", "reasonCode");
        AppendDiagnosticSegment(segments, result.Diagnostics, "capabilityProbeReasonCode", "capabilityProbeReasonCode", "probeReasonCode");
        AppendDiagnosticSegment(segments, result.Diagnostics, "hookState", "hookState");
        AppendDiagnosticSegment(segments, result.Diagnostics, "hybridExecution", "hybridExecution");

        return segments.Count == 0 ? string.Empty : $" [{string.Join(", ", segments)}]";
    }

    private static void AppendDiagnosticSegment(
        ICollection<string> segments,
        IReadOnlyDictionary<string, object?> diagnostics,
        string segmentKey,
        params string[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            var value = TryGetDiagnosticString(diagnostics, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                segments.Add($"{segmentKey}={value}");
                return;
            }
        }
    }

    private static string? TryGetDiagnosticString(IReadOnlyDictionary<string, object?> diagnostics, string key)
    {
        if (!diagnostics.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? value.ToString();
    }

    // ── Quick-Action Methods ──────────────────────────────────────────────

    /// <summary>Toggle tracking to know which bool/toggle cheats are currently "on".</summary>
    private readonly HashSet<string> _activeToggles = new(StringComparer.OrdinalIgnoreCase);

    private async Task QuickRunActionAsync(string actionId, JsonObject payload, string? toggleKey = null)
    {
        if (!_runtime.IsAttached || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (!await EnsureActionAvailableForCurrentSessionAsync(actionId, actionId))
        {
            return;
        }

        try
        {
            var result = await _orchestrator.ExecuteAsync(
                SelectedProfileId,
                actionId,
                payload,
                RuntimeMode,
                BuildActionContext(actionId));
            if (toggleKey is not null && result.Succeeded)
            {
                if (_activeToggles.Contains(toggleKey))
                    _activeToggles.Remove(toggleKey);
                else
                    _activeToggles.Add(toggleKey);
            }
            Status = result.Succeeded
                ? $"✓ {actionId}: {result.Message}{BuildDiagnosticsStatusSuffix(result)}"
                : $"✗ {actionId}: {result.Message}{BuildDiagnosticsStatusSuffix(result)}";
        }
        catch (Exception ex)
        {
            Status = $"✗ {actionId}: {ex.Message}";
        }
    }

    private async Task QuickSetCreditsAsync()
    {
        if (!int.TryParse(CreditsValue, out var value) || value < 0)
        {
            Status = "✗ Invalid credits value. Enter a positive whole number.";
            return;
        }

        if (!_runtime.IsAttached || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            Status = "✗ Not attached to game.";
            return;
        }

        if (!await EnsureActionAvailableForCurrentSessionAsync("set_credits", "Credits"))
        {
            return;
        }

        // Clear any existing freeze / hook lock first.
        if (_freezeService.IsFrozen("credits"))
        {
            _freezeService.Unfreeze("credits");
        }

        // Route through the full action pipeline which installs a trampoline hook
        // on the game's cvttss2si instruction to force the FLOAT source value.
        // This is the only reliable way — writing the int alone is useless because
        // the game overwrites it from the float every frame.
        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = value,
            ["lockCredits"] = CreditsFreeze
        };

        try
        {
            var result = await _orchestrator.ExecuteAsync(
                SelectedProfileId,
                "set_credits",
                payload,
                RuntimeMode,
                BuildActionContext("set_credits"));
            var diagnosticsSuffix = BuildDiagnosticsStatusSuffix(result);

            if (!result.Succeeded)
            {
                Status = $"✗ Credits: {result.Message}{diagnosticsSuffix}";
                return;
            }

            var stateTag = ReadDiagnosticString(result.Diagnostics, "creditsStateTag");
            if (string.IsNullOrWhiteSpace(stateTag))
            {
                stateTag = CreditsFreeze ? "HOOK_LOCK" : "HOOK_ONESHOT";
            }

            if (CreditsFreeze)
            {
                if (stateTag.Equals("HOOK_LOCK", StringComparison.OrdinalIgnoreCase))
                {
                    // Hook lock is active — the cave code forces the float every frame.
                    // Register with freeze service only for UI/diagnostics visibility.
                    _freezeService.FreezeInt("credits", value);
                    RefreshActiveFreezes();
                    Status = $"✓ [HOOK_LOCK] Credits locked to {value:N0} (float+int hook active){diagnosticsSuffix}";
                }
                else
                {
                    Status = $"✗ Credits: unexpected state '{stateTag}' for lock mode.{diagnosticsSuffix}";
                }
            }
            else
            {
                if (stateTag.Equals("HOOK_ONESHOT", StringComparison.OrdinalIgnoreCase))
                {
                    Status = $"✓ [HOOK_ONESHOT] Credits set to {value:N0} (float+int sync){diagnosticsSuffix}";
                }
                else
                {
                    Status = $"✗ Credits: unexpected state '{stateTag}' for one-shot mode.{diagnosticsSuffix}";
                }
            }
        }
        catch (Exception ex)
        {
            Status = $"✗ Credits: {ex.Message}";
        }
    }

    private static string ReadDiagnosticString(IReadOnlyDictionary<string, object?>? diagnostics, string key)
    {
        if (diagnostics is null || !diagnostics.TryGetValue(key, out var raw) || raw is null)
        {
            return string.Empty;
        }

        if (raw is string s)
        {
            return s;
        }

        return raw.ToString() ?? string.Empty;
    }

    private Task QuickFreezeTimerAsync()
    {
        var currentValue = !_activeToggles.Contains("game_timer_freeze");
        return QuickRunActionAsync("freeze_timer",
            new JsonObject { ["symbol"] = "game_timer_freeze", ["boolValue"] = currentValue },
            "game_timer_freeze");
    }

    private Task QuickToggleFogAsync()
    {
        var currentValue = !_activeToggles.Contains("fog_reveal");
        return QuickRunActionAsync("toggle_fog_reveal",
            new JsonObject { ["symbol"] = "fog_reveal", ["boolValue"] = currentValue },
            "fog_reveal");
    }

    private Task QuickToggleAiAsync()
    {
        // ai_enabled: toggling to false disables AI, true re-enables
        var currentValue = _activeToggles.Contains("ai_enabled"); // flip: if active (=disabled), re-enable
        return QuickRunActionAsync("toggle_ai",
            new JsonObject { ["symbol"] = "ai_enabled", ["boolValue"] = currentValue },
            "ai_enabled");
    }

    private Task QuickInstantBuildAsync()
    {
        var enable = !_activeToggles.Contains("instant_build_nop");
        return QuickRunActionAsync("toggle_instant_build_patch",
            new JsonObject { ["enable"] = enable },
            "instant_build_nop");
    }

    private Task QuickUnitCapAsync()
        => QuickRunActionAsync("set_unit_cap",
            new JsonObject { ["symbol"] = "unit_cap", ["intValue"] = 99999, ["enable"] = true });

    private Task QuickGodModeAsync()
    {
        var currentValue = !_activeToggles.Contains("tactical_god_mode");
        return QuickRunActionAsync("toggle_tactical_god_mode",
            new JsonObject { ["symbol"] = "tactical_god_mode", ["boolValue"] = currentValue },
            "tactical_god_mode");
    }

    private Task QuickOneHitAsync()
    {
        var currentValue = !_activeToggles.Contains("tactical_one_hit_mode");
        return QuickRunActionAsync("toggle_tactical_one_hit_mode",
            new JsonObject { ["symbol"] = "tactical_one_hit_mode", ["boolValue"] = currentValue },
            "tactical_one_hit_mode");
    }

    private Task QuickUnfreezeAllAsync()
    {
        _freezeService.UnfreezeAll();
        _activeToggles.Clear();
        RefreshActiveFreezes();
        Status = "✓ All freezes and toggles cleared";
        return Task.CompletedTask;
    }

    private void RefreshActiveFreezes()
    {
        if (!_freezeUiTimer.IsEnabled)
        {
            _freezeUiTimer.Start();
        }

        ActiveFreezes.Clear();
        foreach (var symbol in _freezeService.GetFrozenSymbols())
        {
            ActiveFreezes.Add($"❄️ {symbol}");
        }
        foreach (var toggle in _activeToggles)
        {
            ActiveFreezes.Add($"🔒 {toggle}");
        }
        if (ActiveFreezes.Count == 0)
        {
            ActiveFreezes.Add("(none)");
        }
    }

    private static string HotkeyFilePath => TrustedPathPolicy.CombineUnderRoot(
        TrustedPathPolicy.GetOrCreateAppDataRoot(),
        "hotkeys.json");

    private async Task LoadHotkeysAsync()
    {
        Hotkeys.Clear();
        var path = HotkeyFilePath;
        TrustedPathPolicy.EnsureSubPath(TrustedPathPolicy.GetOrCreateAppDataRoot(), path);
        if (!File.Exists(path))
        {
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+1", ActionId = "set_credits", PayloadJson = "{\"symbol\":\"credits\",\"intValue\":1000000,\"lockCredits\":false}" });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+2", ActionId = "freeze_timer", PayloadJson = "{\"symbol\":\"game_timer_freeze\",\"boolValue\":true}" });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+3", ActionId = "toggle_fog_reveal", PayloadJson = "{\"symbol\":\"fog_reveal\",\"boolValue\":true}" });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+4", ActionId = "toggle_instant_build_patch", PayloadJson = "{\"enable\":true}" });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+5", ActionId = "freeze_symbol", PayloadJson = "{\"symbol\":\"credits\",\"freeze\":true,\"intValue\":1000000}" });
            Status = "Created default hotkey bindings in memory";
            return;
        }

        var json = await File.ReadAllTextAsync(path);
        var items = JsonSerializer.Deserialize<List<HotkeyBindingItem>>(json) ?? new List<HotkeyBindingItem>();
        foreach (var item in items)
        {
            Hotkeys.Add(item);
        }

        Status = $"Loaded {Hotkeys.Count} hotkey bindings";
    }

    private async Task SaveHotkeysAsync()
    {
        var hotkeyPath = HotkeyFilePath;
        TrustedPathPolicy.EnsureSubPath(TrustedPathPolicy.GetOrCreateAppDataRoot(), hotkeyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(hotkeyPath)!);
        var json = JsonSerializer.Serialize(Hotkeys, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(hotkeyPath, json);
        Status = $"Saved {Hotkeys.Count} hotkey bindings";
    }

    private Task AddHotkeyAsync()
    {
        Hotkeys.Add(new HotkeyBindingItem
        {
            Gesture = "Ctrl+Shift+0",
            ActionId = SelectedActionId,
            PayloadJson = "{}"
        });
        return Task.CompletedTask;
    }

    private Task RemoveHotkeyAsync()
    {
        if (SelectedHotkey is not null)
        {
            Hotkeys.Remove(SelectedHotkey);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> ExecuteHotkeyAsync(string gesture)
    {
        if (!_runtime.IsAttached || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return false;
        }

        var binding = Hotkeys.FirstOrDefault(x => string.Equals(x.Gesture, gesture, StringComparison.OrdinalIgnoreCase));
        if (binding is null || string.IsNullOrWhiteSpace(binding.ActionId))
        {
            return false;
        }

        if (!await EnsureActionAvailableForCurrentSessionAsync(binding.ActionId, $"Hotkey {gesture}"))
        {
            return true;
        }

        JsonObject payloadNode;
        try
        {
            payloadNode = JsonNode.Parse(binding.PayloadJson ?? "{}") as JsonObject
                ?? BuildDefaultHotkeyPayload(binding.ActionId);
        }
        catch
        {
            payloadNode = BuildDefaultHotkeyPayload(binding.ActionId);
        }

        var result = await _orchestrator.ExecuteAsync(
            SelectedProfileId,
            binding.ActionId,
            payloadNode,
            RuntimeMode,
            BuildActionContext(binding.ActionId));
        Status = result.Succeeded
            ? $"Hotkey {gesture}: {binding.ActionId} succeeded{BuildDiagnosticsStatusSuffix(result)}"
            : $"Hotkey {gesture}: {binding.ActionId} failed ({result.Message}){BuildDiagnosticsStatusSuffix(result)}";

        return true;
    }

    private static JsonObject BuildDefaultHotkeyPayload(string actionId)
    {
        return actionId switch
        {
            "set_credits" => new JsonObject { ["symbol"] = "credits", ["intValue"] = 1000000, ["lockCredits"] = false },
            "freeze_timer" => new JsonObject { ["symbol"] = "game_timer_freeze", ["boolValue"] = true },
            "toggle_fog_reveal" => new JsonObject { ["symbol"] = "fog_reveal", ["boolValue"] = true },
            "set_unit_cap" => new JsonObject { ["symbol"] = "unit_cap", ["intValue"] = 99999, ["enable"] = true },
            "toggle_instant_build_patch" => new JsonObject { ["enable"] = true },
            "set_game_speed" => new JsonObject { ["symbol"] = "game_speed", ["floatValue"] = (float)2.0 },
            "freeze_symbol" => new JsonObject { ["symbol"] = "credits", ["freeze"] = true, ["intValue"] = 1000000 },
            "unfreeze_symbol" => new JsonObject { ["symbol"] = "credits", ["freeze"] = false },
            _ => new JsonObject()
        };
    }

    private sealed record DraftBuildResult(bool Succeeded, string Message, SelectedUnitDraft? Draft)
    {
        public static DraftBuildResult Failed(string message) => new(false, message, null);

        public static DraftBuildResult FromDraft(SelectedUnitDraft draft) => new(true, "ok", draft);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? memberName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(memberName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? memberName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
}
