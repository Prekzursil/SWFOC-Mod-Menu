using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
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


    public event PropertyChangedEventHandler? PropertyChanged;
}
