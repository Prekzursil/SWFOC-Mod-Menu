using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Saves.Services;

namespace SwfocTrainer.App.ViewModels;

public abstract class MainViewModelCoreStateBase : INotifyPropertyChanged
{
    protected const string UniversalProfileId = "universal_auto";
    protected const string UnknownValue = "unknown";
    protected const string DecimalPrecision3 = "0.###";
    protected const string ActionSetCredits = MainViewModelDefaults.ActionSetCredits;
    protected const string ActionFreezeTimer = MainViewModelDefaults.ActionFreezeTimer;
    protected const string ActionToggleFogReveal = MainViewModelDefaults.ActionToggleFogReveal;
    protected const string ActionToggleAi = MainViewModelDefaults.ActionToggleAi;
    protected const string ActionSetUnitCap = MainViewModelDefaults.ActionSetUnitCap;
    protected const string ActionToggleInstantBuildPatch = MainViewModelDefaults.ActionToggleInstantBuildPatch;
    protected const string ActionToggleTacticalGodMode = MainViewModelDefaults.ActionToggleTacticalGodMode;
    protected const string ActionToggleTacticalOneHitMode = MainViewModelDefaults.ActionToggleTacticalOneHitMode;
    protected const string ActionSetGameSpeed = MainViewModelDefaults.ActionSetGameSpeed;
    protected const string ActionFreezeSymbol = MainViewModelDefaults.ActionFreezeSymbol;
    protected const string ActionUnfreezeSymbol = MainViewModelDefaults.ActionUnfreezeSymbol;
    protected const string PayloadKeySymbol = MainViewModelDefaults.PayloadKeySymbol;
    protected const string PayloadKeyIntValue = MainViewModelDefaults.PayloadKeyIntValue;
    protected const string PayloadKeyBoolValue = MainViewModelDefaults.PayloadKeyBoolValue;
    protected const string PayloadKeyEnable = MainViewModelDefaults.PayloadKeyEnable;
    protected const string PayloadKeyFloatValue = MainViewModelDefaults.PayloadKeyFloatValue;
    protected const string PayloadKeyFreeze = MainViewModelDefaults.PayloadKeyFreeze;
    protected const string PayloadKeyLockCredits = MainViewModelDefaults.PayloadKeyLockCredits;
    protected const string SymbolCredits = MainViewModelDefaults.SymbolCredits;
    protected const string SymbolGameTimerFreeze = MainViewModelDefaults.SymbolGameTimerFreeze;
    protected const string SymbolFogReveal = MainViewModelDefaults.SymbolFogReveal;
    protected const string SymbolAiEnabled = MainViewModelDefaults.SymbolAiEnabled;
    protected const string SymbolUnitCap = MainViewModelDefaults.SymbolUnitCap;
    protected const string SymbolInstantBuildNop = MainViewModelDefaults.SymbolInstantBuildNop;
    protected const string SymbolTacticalGodMode = MainViewModelDefaults.SymbolTacticalGodMode;
    protected const string SymbolTacticalOneHitMode = MainViewModelDefaults.SymbolTacticalOneHitMode;
    protected const string SymbolGameSpeed = MainViewModelDefaults.SymbolGameSpeed;
    protected const string BaseSwfocProfileId = MainViewModelDefaults.BaseSwfocProfileId;
    protected const int DefaultCreditsValue = MainViewModelDefaults.DefaultCreditsValue;
    protected const int DefaultUnitCapValue = MainViewModelDefaults.DefaultUnitCapValue;
    protected const float DefaultGameSpeedValue = MainViewModelDefaults.DefaultGameSpeedValue;

    private protected readonly IProfileRepository _profiles;
    private protected readonly IProcessLocator _processLocator;
    private protected readonly ILaunchContextResolver _launchContextResolver;
    private protected readonly IProfileVariantResolver _profileVariantResolver;
    private protected readonly IGameLaunchService _gameLauncher;
    private protected readonly IRuntimeAdapter _runtime;
    private protected readonly TrainerOrchestrator _orchestrator;
    private protected readonly ICatalogService _catalog;
    private protected readonly ISaveCodec _saveCodec;
    private protected readonly ISavePatchPackService _savePatchPackService;
    private protected readonly ISavePatchApplyService _savePatchApplyService;
    private protected readonly IHelperModService _helper;
    private protected readonly IProfileUpdateService _updates;
    private protected readonly IModOnboardingService _modOnboarding;
    private protected readonly IModCalibrationService _modCalibration;
    private protected readonly ISupportBundleService _supportBundles;
    private protected readonly ITelemetrySnapshotService _telemetry;
    private protected readonly IValueFreezeService _freezeService;
    private protected readonly IActionReliabilityService _actionReliability;
    private protected readonly ISelectedUnitTransactionService _selectedUnitTransactions;
    private protected readonly ISpawnPresetService _spawnPresets;
    private protected DispatcherTimer _freezeUiTimer = null!;

    private protected string? _selectedProfileId;
    private protected string _status = "Ready";
    private protected string _selectedActionId = string.Empty;
    private protected string _payloadJson = MainViewModelDefaults.DefaultPayloadJsonTemplate;
    private protected RuntimeMode _runtimeMode = RuntimeMode.Unknown;
    private protected string _savePath = string.Empty;
    private protected string _saveNodePath = string.Empty;
    private protected string _saveEditValue = string.Empty;
    private protected string _saveSearchQuery = string.Empty;
    private protected string _savePatchPackPath = string.Empty;
    private protected string _savePatchMetadataSummary = "No patch pack loaded.";
    private protected string _savePatchApplySummary = string.Empty;
    private protected string _creditsValue = MainViewModelDefaults.DefaultCreditsValueText;
    private protected bool _creditsFreeze;
    private protected int _resolvedSymbolsCount;
    private protected HotkeyBindingItem? _selectedHotkey;
    private protected string _selectedUnitHp = string.Empty;
    private protected string _selectedUnitShield = string.Empty;
    private protected string _selectedUnitSpeed = string.Empty;
    private protected string _selectedUnitDamageMultiplier = string.Empty;
    private protected string _selectedUnitCooldownMultiplier = string.Empty;
    private protected string _selectedUnitVeterancy = string.Empty;
    private protected string _selectedUnitOwnerFaction = string.Empty;
    private protected string _selectedEntryMarker = "AUTO";
    private protected string _selectedFaction = "EMPIRE";
    private protected string _spawnQuantity = "1";
    private protected string _spawnDelayMs = "125";
    private protected bool _spawnStopOnFailure = true;
    private protected bool _isStrictPatchApply = true;
    private protected string _onboardingBaseProfileId = BaseSwfocProfileId;
    private protected string _onboardingDraftProfileId = "custom_my_mod";
    private protected string _onboardingDisplayName = "Custom Mod Draft";
    private protected string _onboardingNamespaceRoot = "custom";
    private protected string _onboardingLaunchSample = string.Empty;
    private protected string _onboardingSummary = string.Empty;
    private protected string _calibrationNotes = string.Empty;
    private protected string _modCompatibilitySummary = string.Empty;
    private protected string _opsArtifactSummary = string.Empty;
    private protected string _launchTarget = MainViewModelDefaults.DefaultLaunchTarget;
    private protected string _launchMode = MainViewModelDefaults.DefaultLaunchMode;
    private protected string _launchWorkshopId = string.Empty;
    private protected string _launchModPath = string.Empty;
    private protected bool _terminateExistingBeforeLaunch;
    private protected string _supportBundleOutputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwfocTrainer",
        "support");
    private protected SpawnPresetViewItem? _selectedSpawnPreset;

    private protected SaveDocument? _loadedSave;
    private protected byte[]? _loadedSaveOriginal;
    private protected SavePatchPack? _loadedPatchPack;
    private protected SavePatchPreview? _loadedPatchPreview;

    private protected IReadOnlyDictionary<string, ActionSpec> _loadedActionSpecs =
        new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase);

    protected static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true
    };

    protected static readonly JsonSerializerOptions SavePatchJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    protected static readonly IReadOnlyDictionary<string, string> DefaultSymbolByActionId =
        MainViewModelDefaults.DefaultSymbolByActionId;

    protected static readonly IReadOnlyDictionary<string, string> DefaultHelperHookByActionId =
        MainViewModelDefaults.DefaultHelperHookByActionId;

    protected MainViewModelCoreStateBase(MainViewModelDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        (_profiles, _processLocator, _launchContextResolver, _profileVariantResolver, _gameLauncher, _runtime, _orchestrator, _catalog, _saveCodec,
            _savePatchPackService, _savePatchApplyService, _helper, _updates, _modOnboarding, _modCalibration, _supportBundles, _telemetry,
            _freezeService, _actionReliability, _selectedUnitTransactions, _spawnPresets) = CreateDependencyTuple(dependencies);
    }

    private static (
        IProfileRepository Profiles,
        IProcessLocator ProcessLocator,
        ILaunchContextResolver LaunchContextResolver,
        IProfileVariantResolver ProfileVariantResolver,
        IGameLaunchService GameLauncher,
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
            dependencies.GameLauncher,
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(
        T currentValue,
        T value,
        Action<T> assign,
        [CallerMemberName] string? memberName = null)
    {
        ArgumentNullException.ThrowIfNull(assign);
        if (EqualityComparer<T>.Default.Equals(currentValue, value))
        {
            return false;
        }

        assign(value);
        OnPropertyChanged(memberName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? memberName = null)
    {
        var handler = PropertyChanged;
        if (handler is null)
        {
            return;
        }

        handler(this, new PropertyChangedEventArgs(memberName));
    }
}
