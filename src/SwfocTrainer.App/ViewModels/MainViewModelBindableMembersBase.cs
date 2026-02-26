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
        set => SetField(ref _creditsValue, value);
    }

    public bool CreditsFreeze
    {
        get => _creditsFreeze;
        set => SetField(ref _creditsFreeze, value);
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
