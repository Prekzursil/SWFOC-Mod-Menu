using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using SwfocTrainer.App.Infrastructure;
using SwfocTrainer.App.Models;

namespace SwfocTrainer.App.ViewModels;

internal sealed class MainViewModelCoreCommandContext
{
    public required Func<Task> LoadProfilesAsync { get; init; }
    public required Func<Task> LaunchAndAttachAsync { get; init; }
    public required Func<Task> AttachAsync { get; init; }
    public required Func<Task> DetachAsync { get; init; }
    public required Func<Task> LoadActionsAsync { get; init; }
    public required Func<Task> ExecuteActionAsync { get; init; }
    public required Func<Task> LoadCatalogAsync { get; init; }
    public required Func<Task> DeployHelperAsync { get; init; }
    public required Func<Task> VerifyHelperAsync { get; init; }
    public required Func<Task> CheckUpdatesAsync { get; init; }
    public required Func<Task> InstallUpdateAsync { get; init; }
    public required Func<Task> RollbackProfileUpdateAsync { get; init; }
    public required Func<bool> CanUseSelectedProfile { get; init; }
    public required Func<bool> CanExecuteSelectedAction { get; init; }
    public required Func<bool> IsAttached { get; init; }
}

internal sealed class MainViewModelSaveCommandContext
{
    public required Func<Task> BrowseSaveAsync { get; init; }
    public required Func<Task> LoadSaveAsync { get; init; }
    public required Func<Task> EditSaveFieldAsync { get; init; }
    public required Func<Task> ValidateSaveAsync { get; init; }
    public required Func<Task> RefreshSaveDiffPreviewAsync { get; init; }
    public required Func<Task> WriteSaveAsync { get; init; }
    public required Func<Task> BrowsePatchPackAsync { get; init; }
    public required Func<Task> ExportPatchPackAsync { get; init; }
    public required Func<Task> LoadPatchPackAsync { get; init; }
    public required Func<Task> PreviewPatchPackAsync { get; init; }
    public required Func<Task> ApplyPatchPackAsync { get; init; }
    public required Func<Task> RestoreSaveBackupAsync { get; init; }
    public required Func<Task> LoadHotkeysAsync { get; init; }
    public required Func<Task> SaveHotkeysAsync { get; init; }
    public required Func<Task> AddHotkeyAsync { get; init; }
    public required Func<Task> RemoveHotkeyAsync { get; init; }
    public required Func<bool> CanLoadSave { get; init; }
    public required Func<bool> CanEditSave { get; init; }
    public required Func<bool> CanValidateSave { get; init; }
    public required Func<bool> CanRefreshDiff { get; init; }
    public required Func<bool> CanWriteSave { get; init; }
    public required Func<bool> CanExportPatchPack { get; init; }
    public required Func<bool> CanLoadPatchPack { get; init; }
    public required Func<bool> CanPreviewPatchPack { get; init; }
    public required Func<bool> CanApplyPatchPack { get; init; }
    public required Func<bool> CanRestoreBackup { get; init; }
    public required Func<bool> CanRemoveHotkey { get; init; }
}

internal sealed class MainViewModelLiveOpsCommandContext
{
    public required Func<Task> RefreshActionReliabilityAsync { get; init; }
    public required Func<Task> CaptureSelectedUnitBaselineAsync { get; init; }
    public required Func<Task> ApplySelectedUnitDraftAsync { get; init; }
    public required Func<Task> RevertSelectedUnitTransactionAsync { get; init; }
    public required Func<Task> RestoreSelectedUnitBaselineAsync { get; init; }
    public required Func<Task> LoadSpawnPresetsAsync { get; init; }
    public required Func<Task> RunSpawnBatchAsync { get; init; }
    public required Func<Task> ScaffoldModProfileAsync { get; init; }
    public required Func<Task> ExportCalibrationArtifactAsync { get; init; }
    public required Func<Task> BuildModCompatibilityReportAsync { get; init; }
    public required Func<Task> ExportSupportBundleAsync { get; init; }
    public required Func<Task> ExportTelemetrySnapshotAsync { get; init; }
    public required Func<bool> CanRunSpawnBatch { get; init; }
    public required Func<bool> CanScaffoldModProfile { get; init; }
    public required Func<bool> CanUseSupportBundleOutputDirectory { get; init; }
    public required Func<bool> IsAttached { get; init; }
    public required Func<bool> CanUseSelectedProfile { get; init; }
}

internal sealed class MainViewModelQuickCommandContext
{
    public required Func<Task> QuickSetCreditsAsync { get; init; }
    public required Func<Task> QuickFreezeTimerAsync { get; init; }
    public required Func<Task> QuickToggleFogAsync { get; init; }
    public required Func<Task> QuickToggleAiAsync { get; init; }
    public required Func<Task> QuickInstantBuildAsync { get; init; }
    public required Func<Task> QuickUnitCapAsync { get; init; }
    public required Func<Task> QuickGodModeAsync { get; init; }
    public required Func<Task> QuickOneHitAsync { get; init; }
    public required Func<Task> QuickUnfreezeAllAsync { get; init; }
    public required Func<bool> IsAttached { get; init; }
}

internal static class MainViewModelFactories
{
    internal static (
        ObservableCollection<string> Profiles,
        ObservableCollection<string> Actions,
        ObservableCollection<string> CatalogSummary,
        ObservableCollection<string> Updates,
        ObservableCollection<string> SaveDiffPreview,
        ObservableCollection<HotkeyBindingItem> Hotkeys,
        ObservableCollection<SaveFieldViewItem> SaveFields,
        ObservableCollection<SaveFieldViewItem> FilteredSaveFields,
        ObservableCollection<SavePatchOperationViewItem> SavePatchOperations,
        ObservableCollection<SavePatchCompatibilityViewItem> SavePatchCompatibility,
        ObservableCollection<ActionReliabilityViewItem> ActionReliability,
        ObservableCollection<SelectedUnitTransactionViewItem> SelectedUnitTransactions,
        ObservableCollection<SpawnPresetViewItem> SpawnPresets,
        ObservableCollection<string> LiveOpsDiagnostics,
        ObservableCollection<string> ModCompatibilityRows,
        ObservableCollection<string> ActiveFreezes) CreateCollections()
    {
        return (
            new ObservableCollection<string>(),
            new ObservableCollection<string>(),
            new ObservableCollection<string>(),
            new ObservableCollection<string>(),
            new ObservableCollection<string>(),
            new ObservableCollection<HotkeyBindingItem>(),
            new ObservableCollection<SaveFieldViewItem>(),
            new ObservableCollection<SaveFieldViewItem>(),
            new ObservableCollection<SavePatchOperationViewItem>(),
            new ObservableCollection<SavePatchCompatibilityViewItem>(),
            new ObservableCollection<ActionReliabilityViewItem>(),
            new ObservableCollection<SelectedUnitTransactionViewItem>(),
            new ObservableCollection<SpawnPresetViewItem>(),
            new ObservableCollection<string>(),
            new ObservableCollection<string>(),
            new ObservableCollection<string>());
    }

    internal static (
        ICommand LoadProfiles,
        ICommand LaunchAndAttach,
        ICommand Attach,
        ICommand Detach,
        ICommand LoadActions,
        ICommand ExecuteAction,
        ICommand LoadCatalog,
        ICommand DeployHelper,
        ICommand VerifyHelper,
        ICommand CheckUpdates,
        ICommand InstallUpdate,
        ICommand RollbackProfileUpdate) CreateCoreCommands(MainViewModelCoreCommandContext context)
    {
        return (
            new AsyncCommand(context.LoadProfilesAsync),
            new AsyncCommand(context.LaunchAndAttachAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.AttachAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.DetachAsync, context.IsAttached),
            new AsyncCommand(context.LoadActionsAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.ExecuteActionAsync, context.CanExecuteSelectedAction),
            new AsyncCommand(context.LoadCatalogAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.DeployHelperAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.VerifyHelperAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.CheckUpdatesAsync),
            new AsyncCommand(context.InstallUpdateAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.RollbackProfileUpdateAsync, context.CanUseSelectedProfile));
    }

    internal static (
        ICommand BrowseSave,
        ICommand LoadSave,
        ICommand EditSave,
        ICommand ValidateSave,
        ICommand RefreshDiff,
        ICommand WriteSave,
        ICommand BrowsePatchPack,
        ICommand ExportPatchPack,
        ICommand LoadPatchPack,
        ICommand PreviewPatchPack,
        ICommand ApplyPatchPack,
        ICommand RestoreBackup,
        ICommand LoadHotkeys,
        ICommand SaveHotkeys,
        ICommand AddHotkey,
        ICommand RemoveHotkey) CreateSaveCommands(MainViewModelSaveCommandContext context)
    {
        return (
            new AsyncCommand(context.BrowseSaveAsync),
            new AsyncCommand(context.LoadSaveAsync, context.CanLoadSave),
            new AsyncCommand(context.EditSaveFieldAsync, context.CanEditSave),
            new AsyncCommand(context.ValidateSaveAsync, context.CanValidateSave),
            new AsyncCommand(context.RefreshSaveDiffPreviewAsync, context.CanRefreshDiff),
            new AsyncCommand(context.WriteSaveAsync, context.CanWriteSave),
            new AsyncCommand(context.BrowsePatchPackAsync),
            new AsyncCommand(context.ExportPatchPackAsync, context.CanExportPatchPack),
            new AsyncCommand(context.LoadPatchPackAsync, context.CanLoadPatchPack),
            new AsyncCommand(context.PreviewPatchPackAsync, context.CanPreviewPatchPack),
            new AsyncCommand(context.ApplyPatchPackAsync, context.CanApplyPatchPack),
            new AsyncCommand(context.RestoreSaveBackupAsync, context.CanRestoreBackup),
            new AsyncCommand(context.LoadHotkeysAsync),
            new AsyncCommand(context.SaveHotkeysAsync),
            new AsyncCommand(context.AddHotkeyAsync),
            new AsyncCommand(context.RemoveHotkeyAsync, context.CanRemoveHotkey));
    }

    internal static (
        ICommand RefreshActionReliability,
        ICommand CaptureSelectedUnitBaseline,
        ICommand ApplySelectedUnitDraft,
        ICommand RevertSelectedUnitTransaction,
        ICommand RestoreSelectedUnitBaseline,
        ICommand LoadSpawnPresets,
        ICommand RunSpawnBatch,
        ICommand ScaffoldModProfile,
        ICommand ExportCalibrationArtifact,
        ICommand BuildCompatibilityReport,
        ICommand ExportSupportBundle,
        ICommand ExportTelemetrySnapshot) CreateLiveOpsCommands(MainViewModelLiveOpsCommandContext context)
    {
        return (
            new AsyncCommand(context.RefreshActionReliabilityAsync, () => context.IsAttached() && context.CanUseSelectedProfile()),
            new AsyncCommand(context.CaptureSelectedUnitBaselineAsync, context.IsAttached),
            new AsyncCommand(context.ApplySelectedUnitDraftAsync, context.IsAttached),
            new AsyncCommand(context.RevertSelectedUnitTransactionAsync, context.IsAttached),
            new AsyncCommand(context.RestoreSelectedUnitBaselineAsync, context.IsAttached),
            new AsyncCommand(context.LoadSpawnPresetsAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.RunSpawnBatchAsync, context.CanRunSpawnBatch),
            new AsyncCommand(context.ScaffoldModProfileAsync, context.CanScaffoldModProfile),
            new AsyncCommand(context.ExportCalibrationArtifactAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.BuildModCompatibilityReportAsync, context.CanUseSelectedProfile),
            new AsyncCommand(context.ExportSupportBundleAsync, context.CanUseSupportBundleOutputDirectory),
            new AsyncCommand(context.ExportTelemetrySnapshotAsync, context.CanUseSupportBundleOutputDirectory));
    }

    internal static (
        ICommand QuickSetCredits,
        ICommand QuickFreezeTimer,
        ICommand QuickToggleFog,
        ICommand QuickToggleAi,
        ICommand QuickInstantBuild,
        ICommand QuickUnitCap,
        ICommand QuickGodMode,
        ICommand QuickOneHit,
        ICommand QuickUnfreezeAll) CreateQuickCommands(MainViewModelQuickCommandContext context)
    {
        return (
            new AsyncCommand(context.QuickSetCreditsAsync, context.IsAttached),
            new AsyncCommand(context.QuickFreezeTimerAsync, context.IsAttached),
            new AsyncCommand(context.QuickToggleFogAsync, context.IsAttached),
            new AsyncCommand(context.QuickToggleAiAsync, context.IsAttached),
            new AsyncCommand(context.QuickInstantBuildAsync, context.IsAttached),
            new AsyncCommand(context.QuickUnitCapAsync, context.IsAttached),
            new AsyncCommand(context.QuickGodModeAsync, context.IsAttached),
            new AsyncCommand(context.QuickOneHitAsync, context.IsAttached),
            new AsyncCommand(context.QuickUnfreezeAllAsync, context.IsAttached));
    }
}
