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

    protected readonly IProfileRepository _profiles;
    protected readonly IProcessLocator _processLocator;
    protected readonly ILaunchContextResolver _launchContextResolver;
    protected readonly IProfileVariantResolver _profileVariantResolver;
    protected readonly IRuntimeAdapter _runtime;
    protected readonly TrainerOrchestrator _orchestrator;
    protected readonly ICatalogService _catalog;
    protected readonly ISaveCodec _saveCodec;
    protected readonly ISavePatchPackService _savePatchPackService;
    protected readonly ISavePatchApplyService _savePatchApplyService;
    protected readonly IHelperModService _helper;
    protected readonly IProfileUpdateService _updates;
    protected readonly IModOnboardingService _modOnboarding;
    protected readonly IModCalibrationService _modCalibration;
    protected readonly ISupportBundleService _supportBundles;
    protected readonly ITelemetrySnapshotService _telemetry;
    protected readonly IValueFreezeService _freezeService;
    protected readonly IActionReliabilityService _actionReliability;
    protected readonly ISelectedUnitTransactionService _selectedUnitTransactions;
    protected readonly ISpawnPresetService _spawnPresets;
    protected DispatcherTimer _freezeUiTimer = null!;

    protected string? _selectedProfileId;
    protected string _status = "Ready";
    protected string _selectedActionId = string.Empty;
    protected string _payloadJson = MainViewModelDefaults.DefaultPayloadJsonTemplate;
    protected RuntimeMode _runtimeMode = RuntimeMode.Unknown;
    protected RuntimeMode _runtimeModeOverride = RuntimeMode.Unknown;
    protected string _savePath = string.Empty;
    protected string _saveNodePath = string.Empty;
    protected string _saveEditValue = string.Empty;
    protected string _saveSearchQuery = string.Empty;
    protected string _savePatchPackPath = string.Empty;
    protected string _savePatchMetadataSummary = "No patch pack loaded.";
    protected string _savePatchApplySummary = string.Empty;
    protected string _creditsValue = MainViewModelDefaults.DefaultCreditsValueText;
    protected bool _creditsFreeze;
    protected int _resolvedSymbolsCount;
    protected HotkeyBindingItem? _selectedHotkey;
    protected string _selectedUnitHp = string.Empty;
    protected string _selectedUnitShield = string.Empty;
    protected string _selectedUnitSpeed = string.Empty;
    protected string _selectedUnitDamageMultiplier = string.Empty;
    protected string _selectedUnitCooldownMultiplier = string.Empty;
    protected string _selectedUnitVeterancy = string.Empty;
    protected string _selectedUnitOwnerFaction = string.Empty;
    protected string _selectedEntryMarker = "AUTO";
    protected string _selectedFaction = "EMPIRE";
    protected string _spawnQuantity = "1";
    protected string _spawnDelayMs = "125";
    protected bool _spawnStopOnFailure = true;
    protected bool _isStrictPatchApply = true;
    protected string _onboardingBaseProfileId = BaseSwfocProfileId;
    protected string _onboardingDraftProfileId = "custom_my_mod";
    protected string _onboardingDisplayName = "Custom Mod Draft";
    protected string _onboardingNamespaceRoot = "custom";
    protected string _onboardingLaunchSample = string.Empty;
    protected string _onboardingSummary = string.Empty;
    protected string _calibrationNotes = string.Empty;
    protected string _modCompatibilitySummary = string.Empty;
    protected string _opsArtifactSummary = string.Empty;
    protected string _supportBundleOutputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwfocTrainer",
        "support");
    protected SpawnPresetViewItem? _selectedSpawnPreset;

    protected SaveDocument? _loadedSave;
    protected byte[]? _loadedSaveOriginal;
    protected SavePatchPack? _loadedPatchPack;
    protected SavePatchPreview? _loadedPatchPreview;

    protected IReadOnlyDictionary<string, ActionSpec> _loadedActionSpecs =
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
        (_profiles, _processLocator, _launchContextResolver, _profileVariantResolver, _runtime, _orchestrator, _catalog, _saveCodec,
            _savePatchPackService, _savePatchApplyService, _helper, _updates, _modOnboarding, _modCalibration, _supportBundles, _telemetry,
            _freezeService, _actionReliability, _selectedUnitTransactions, _spawnPresets) = CreateDependencyTuple(dependencies);
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(
        T currentValue,
        T value,
        Action<T> assign,
        [CallerMemberName] string? memberName = null)
    {
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
