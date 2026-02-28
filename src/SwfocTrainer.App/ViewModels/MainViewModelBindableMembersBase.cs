using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

public abstract class MainViewModelBindableMembersBase : MainViewModelCoreStateBase
{
    protected MainViewModelBindableMembersBase(MainViewModelDependencies dependencies)
        : base(dependencies)
    {
    }

    public ObservableCollection<string> Profiles { get; protected set; } = null!;
    public ObservableCollection<string> Actions { get; protected set; } = null!;
    public ObservableCollection<string> CatalogSummary { get; protected set; } = null!;
    public ObservableCollection<string> Updates { get; protected set; } = null!;
    public ObservableCollection<string> SaveDiffPreview { get; protected set; } = null!;
    public ObservableCollection<HotkeyBindingItem> Hotkeys { get; protected set; } = null!;
    public ObservableCollection<string> ActiveFreezes { get; protected set; } = null!;
    public ObservableCollection<SaveFieldViewItem> SaveFields { get; protected set; } = null!;
    public ObservableCollection<SaveFieldViewItem> FilteredSaveFields { get; protected set; } = null!;
    public ObservableCollection<SavePatchOperationViewItem> SavePatchOperations { get; protected set; } = null!;
    public ObservableCollection<SavePatchCompatibilityViewItem> SavePatchCompatibility { get; protected set; } = null!;
    public ObservableCollection<ActionReliabilityViewItem> ActionReliability { get; protected set; } = null!;
    public ObservableCollection<SelectedUnitTransactionViewItem> SelectedUnitTransactions { get; protected set; } = null!;
    public ObservableCollection<SpawnPresetViewItem> SpawnPresets { get; protected set; } = null!;
    public ObservableCollection<string> LiveOpsDiagnostics { get; protected set; } = null!;
    public ObservableCollection<string> ModCompatibilityRows { get; protected set; } = null!;
    public string? SelectedProfileId
    {
        get => _selectedProfileId;
        set
        {
            if (SetField(_selectedProfileId, value, newValue => _selectedProfileId = newValue))
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
            if (SetField(_selectedActionId, value, newValue => _selectedActionId = newValue))
            {
                ApplyPayloadTemplateForSelectedAction();
            }
        }
    }

    public string PayloadJson
    {
        get => _payloadJson;
        set => SetField(_payloadJson, value, newValue => _payloadJson = newValue);
    }

    public string Status
    {
        get => _status;
        set => SetField(_status, value, newValue => _status = newValue);
    }

    public RuntimeMode RuntimeMode
    {
        get => _runtimeMode;
        set => SetField(_runtimeMode, value, newValue => _runtimeMode = newValue);
    }

    public RuntimeMode RuntimeModeOverride
    {
        get => _runtimeModeOverride;
        set => SetField(_runtimeModeOverride, value, newValue => _runtimeModeOverride = newValue);
    }

    public string SavePath
    {
        get => _savePath;
        set => SetField(_savePath, value, newValue => _savePath = newValue);
    }

    public string SaveNodePath
    {
        get => _saveNodePath;
        set => SetField(_saveNodePath, value, newValue => _saveNodePath = newValue);
    }

    public string SaveEditValue
    {
        get => _saveEditValue;
        set => SetField(_saveEditValue, value, newValue => _saveEditValue = newValue);
    }

    public string SaveSearchQuery
    {
        get => _saveSearchQuery;
        set
        {
            if (SetField(_saveSearchQuery, value, newValue => _saveSearchQuery = newValue))
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
            if (SetField(_savePatchPackPath, value, newValue => _savePatchPackPath = newValue))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string SavePatchMetadataSummary
    {
        get => _savePatchMetadataSummary;
        set => SetField(_savePatchMetadataSummary, value, newValue => _savePatchMetadataSummary = newValue);
    }

    public string SavePatchApplySummary
    {
        get => _savePatchApplySummary;
        set => SetField(_savePatchApplySummary, value, newValue => _savePatchApplySummary = newValue);
    }

    public int ResolvedSymbolsCount
    {
        get => _resolvedSymbolsCount;
        set => SetField(_resolvedSymbolsCount, value, newValue => _resolvedSymbolsCount = newValue);
    }

    public bool CanWorkWithProfile => !string.IsNullOrWhiteSpace(SelectedProfileId);

    public HotkeyBindingItem? SelectedHotkey
    {
        get => _selectedHotkey;
        set => SetField(_selectedHotkey, value, newValue => _selectedHotkey = newValue);
    }

    public SpawnPresetViewItem? SelectedSpawnPreset
    {
        get => _selectedSpawnPreset;
        set
        {
            if (SetField(_selectedSpawnPreset, value, newValue => _selectedSpawnPreset = newValue))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string SelectedUnitHp
    {
        get => _selectedUnitHp;
        set => SetField(_selectedUnitHp, value, newValue => _selectedUnitHp = newValue);
    }

    public string SelectedUnitShield
    {
        get => _selectedUnitShield;
        set => SetField(_selectedUnitShield, value, newValue => _selectedUnitShield = newValue);
    }

    public string SelectedUnitSpeed
    {
        get => _selectedUnitSpeed;
        set => SetField(_selectedUnitSpeed, value, newValue => _selectedUnitSpeed = newValue);
    }

    public string SelectedUnitDamageMultiplier
    {
        get => _selectedUnitDamageMultiplier;
        set => SetField(_selectedUnitDamageMultiplier, value, newValue => _selectedUnitDamageMultiplier = newValue);
    }

    public string SelectedUnitCooldownMultiplier
    {
        get => _selectedUnitCooldownMultiplier;
        set => SetField(_selectedUnitCooldownMultiplier, value, newValue => _selectedUnitCooldownMultiplier = newValue);
    }

    public string SelectedUnitVeterancy
    {
        get => _selectedUnitVeterancy;
        set => SetField(_selectedUnitVeterancy, value, newValue => _selectedUnitVeterancy = newValue);
    }

    public string SelectedUnitOwnerFaction
    {
        get => _selectedUnitOwnerFaction;
        set => SetField(_selectedUnitOwnerFaction, value, newValue => _selectedUnitOwnerFaction = newValue);
    }

    public string SpawnQuantity
    {
        get => _spawnQuantity;
        set => SetField(_spawnQuantity, value, newValue => _spawnQuantity = newValue);
    }

    public string SpawnDelayMs
    {
        get => _spawnDelayMs;
        set => SetField(_spawnDelayMs, value, newValue => _spawnDelayMs = newValue);
    }

    public string SelectedFaction
    {
        get => _selectedFaction;
        set => SetField(_selectedFaction, value, newValue => _selectedFaction = newValue);
    }

    public string SelectedEntryMarker
    {
        get => _selectedEntryMarker;
        set => SetField(_selectedEntryMarker, value, newValue => _selectedEntryMarker = newValue);
    }

    public bool SpawnStopOnFailure
    {
        get => _spawnStopOnFailure;
        set => SetField(_spawnStopOnFailure, value, newValue => _spawnStopOnFailure = newValue);
    }

    public bool IsStrictPatchApply
    {
        get => _isStrictPatchApply;
        set => SetField(_isStrictPatchApply, value, newValue => _isStrictPatchApply = newValue);
    }

    public string OnboardingBaseProfileId
    {
        get => _onboardingBaseProfileId;
        set => SetField(_onboardingBaseProfileId, value, newValue => _onboardingBaseProfileId = newValue);
    }

    public string OnboardingDraftProfileId
    {
        get => _onboardingDraftProfileId;
        set => SetField(_onboardingDraftProfileId, value, newValue => _onboardingDraftProfileId = newValue);
    }

    public string OnboardingDisplayName
    {
        get => _onboardingDisplayName;
        set => SetField(_onboardingDisplayName, value, newValue => _onboardingDisplayName = newValue);
    }

    public string OnboardingNamespaceRoot
    {
        get => _onboardingNamespaceRoot;
        set => SetField(_onboardingNamespaceRoot, value, newValue => _onboardingNamespaceRoot = newValue);
    }

    public string OnboardingLaunchSample
    {
        get => _onboardingLaunchSample;
        set => SetField(_onboardingLaunchSample, value, newValue => _onboardingLaunchSample = newValue);
    }

    public string OnboardingSummary
    {
        get => _onboardingSummary;
        set => SetField(_onboardingSummary, value, newValue => _onboardingSummary = newValue);
    }

    public string CalibrationNotes
    {
        get => _calibrationNotes;
        set => SetField(_calibrationNotes, value, newValue => _calibrationNotes = newValue);
    }

    public string ModCompatibilitySummary
    {
        get => _modCompatibilitySummary;
        set => SetField(_modCompatibilitySummary, value, newValue => _modCompatibilitySummary = newValue);
    }

    public string OpsArtifactSummary
    {
        get => _opsArtifactSummary;
        set => SetField(_opsArtifactSummary, value, newValue => _opsArtifactSummary = newValue);
    }

    public string SupportBundleOutputDirectory
    {
        get => _supportBundleOutputDirectory;
        set => SetField(_supportBundleOutputDirectory, value, newValue => _supportBundleOutputDirectory = newValue);
    }

    public ICommand LoadProfilesCommand { get; protected set; } = null!;
    public ICommand AttachCommand { get; protected set; } = null!;
    public ICommand DetachCommand { get; protected set; } = null!;
    public ICommand LoadActionsCommand { get; protected set; } = null!;
    public ICommand ExecuteActionCommand { get; protected set; } = null!;
    public ICommand LoadCatalogCommand { get; protected set; } = null!;
    public ICommand DeployHelperCommand { get; protected set; } = null!;
    public ICommand VerifyHelperCommand { get; protected set; } = null!;
    public ICommand CheckUpdatesCommand { get; protected set; } = null!;
    public ICommand InstallUpdateCommand { get; protected set; } = null!;
    public ICommand RollbackProfileUpdateCommand { get; protected set; } = null!;
    public ICommand BrowseSaveCommand { get; protected set; } = null!;
    public ICommand LoadSaveCommand { get; protected set; } = null!;
    public ICommand EditSaveCommand { get; protected set; } = null!;
    public ICommand ValidateSaveCommand { get; protected set; } = null!;
    public ICommand RefreshDiffCommand { get; protected set; } = null!;
    public ICommand WriteSaveCommand { get; protected set; } = null!;
    public ICommand BrowsePatchPackCommand { get; protected set; } = null!;
    public ICommand ExportPatchPackCommand { get; protected set; } = null!;
    public ICommand LoadPatchPackCommand { get; protected set; } = null!;
    public ICommand PreviewPatchPackCommand { get; protected set; } = null!;
    public ICommand ApplyPatchPackCommand { get; protected set; } = null!;
    public ICommand RestoreBackupCommand { get; protected set; } = null!;
    public ICommand LoadHotkeysCommand { get; protected set; } = null!;
    public ICommand SaveHotkeysCommand { get; protected set; } = null!;
    public ICommand AddHotkeyCommand { get; protected set; } = null!;
    public ICommand RemoveHotkeyCommand { get; protected set; } = null!;
    public ICommand RefreshActionReliabilityCommand { get; protected set; } = null!;
    public ICommand CaptureSelectedUnitBaselineCommand { get; protected set; } = null!;
    public ICommand ApplySelectedUnitDraftCommand { get; protected set; } = null!;
    public ICommand RevertSelectedUnitTransactionCommand { get; protected set; } = null!;
    public ICommand RestoreSelectedUnitBaselineCommand { get; protected set; } = null!;
    public ICommand LoadSpawnPresetsCommand { get; protected set; } = null!;
    public ICommand RunSpawnBatchCommand { get; protected set; } = null!;
    public ICommand ScaffoldModProfileCommand { get; protected set; } = null!;
    public ICommand ExportCalibrationArtifactCommand { get; protected set; } = null!;
    public ICommand BuildCompatibilityReportCommand { get; protected set; } = null!;
    public ICommand ExportSupportBundleCommand { get; protected set; } = null!;
    public ICommand ExportTelemetrySnapshotCommand { get; protected set; } = null!;
    public string CreditsValue
    {
        get => _creditsValue;
        set => SetField(_creditsValue, value, newValue => _creditsValue = newValue);
    }

    public bool CreditsFreeze
    {
        get => _creditsFreeze;
        set => SetField(_creditsFreeze, value, newValue => _creditsFreeze = newValue);
    }

    // Quick-action commands
    public ICommand QuickSetCreditsCommand { get; protected set; } = null!;
    public ICommand QuickFreezeTimerCommand { get; protected set; } = null!;
    public ICommand QuickToggleFogCommand { get; protected set; } = null!;
    public ICommand QuickToggleAiCommand { get; protected set; } = null!;
    public ICommand QuickInstantBuildCommand { get; protected set; } = null!;
    public ICommand QuickUnitCapCommand { get; protected set; } = null!;
    public ICommand QuickGodModeCommand { get; protected set; } = null!;
    public ICommand QuickOneHitCommand { get; protected set; } = null!;
    public ICommand QuickUnfreezeAllCommand { get; protected set; } = null!;

    protected abstract void ApplyPayloadTemplateForSelectedAction();

    protected abstract void ApplySaveSearch();
}
