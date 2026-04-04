using System.Collections.ObjectModel;
using System.Windows.Input;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelFactories:
/// CreateCollections tuple, CreateCoreCommands/CreateSaveCommands/CreateLiveOpsCommands/
/// CreateQuickCommands with null context guards, CanExecute behavior of returned commands.
/// </summary>
public sealed class MainViewModelFactoriesWave5Tests
{
    [Fact]
    public void CreateCollections_ShouldReturnNonNullCollections()
    {
        var (profiles, actions, catalog, updates, diff, hotkeys, saveFields, filteredSaveFields,
            patchOps, patchCompat, reliability, unitTx, spawnPresets, liveOps, modCompat, freezes) =
            MainViewModelFactories.CreateCollections();

        profiles.Should().NotBeNull().And.BeEmpty();
        actions.Should().NotBeNull().And.BeEmpty();
        catalog.Should().NotBeNull().And.BeEmpty();
        updates.Should().NotBeNull().And.BeEmpty();
        diff.Should().NotBeNull().And.BeEmpty();
        hotkeys.Should().NotBeNull().And.BeEmpty();
        saveFields.Should().NotBeNull().And.BeEmpty();
        filteredSaveFields.Should().NotBeNull().And.BeEmpty();
        patchOps.Should().NotBeNull().And.BeEmpty();
        patchCompat.Should().NotBeNull().And.BeEmpty();
        reliability.Should().NotBeNull().And.BeEmpty();
        unitTx.Should().NotBeNull().And.BeEmpty();
        spawnPresets.Should().NotBeNull().And.BeEmpty();
        liveOps.Should().NotBeNull().And.BeEmpty();
        modCompat.Should().NotBeNull().And.BeEmpty();
        freezes.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void CreateCoreCommands_NullContext_ShouldThrow()
    {
        var act = () => MainViewModelFactories.CreateCoreCommands(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateCoreCommands_ShouldReturnAllCommands()
    {
        var context = BuildCoreCommandContext();
        var (loadProfiles, launchAttach, attach, detach, loadActions, executeAction,
            loadCatalog, deployHelper, verifyHelper, checkUpdates, installUpdate, rollback) =
            MainViewModelFactories.CreateCoreCommands(context);

        loadProfiles.Should().NotBeNull();
        launchAttach.Should().NotBeNull();
        attach.Should().NotBeNull();
        detach.Should().NotBeNull();
        loadActions.Should().NotBeNull();
        executeAction.Should().NotBeNull();
        loadCatalog.Should().NotBeNull();
        deployHelper.Should().NotBeNull();
        verifyHelper.Should().NotBeNull();
        checkUpdates.Should().NotBeNull();
        installUpdate.Should().NotBeNull();
        rollback.Should().NotBeNull();
    }

    [Fact]
    public void CreateSaveCommands_NullContext_ShouldThrow()
    {
        var act = () => MainViewModelFactories.CreateSaveCommands(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateSaveCommands_ShouldReturnAllCommands()
    {
        var context = BuildSaveCommandContext();
        var (browse, load, edit, validate, refreshDiff, write,
            browsePatch, exportPatch, loadPatch, previewPatch, applyPatch, restoreBackup,
            loadHotkeys, saveHotkeys, addHotkey, removeHotkey) =
            MainViewModelFactories.CreateSaveCommands(context);

        browse.Should().NotBeNull();
        load.Should().NotBeNull();
        edit.Should().NotBeNull();
        validate.Should().NotBeNull();
        refreshDiff.Should().NotBeNull();
        write.Should().NotBeNull();
        browsePatch.Should().NotBeNull();
        exportPatch.Should().NotBeNull();
        loadPatch.Should().NotBeNull();
        previewPatch.Should().NotBeNull();
        applyPatch.Should().NotBeNull();
        restoreBackup.Should().NotBeNull();
        loadHotkeys.Should().NotBeNull();
        saveHotkeys.Should().NotBeNull();
        addHotkey.Should().NotBeNull();
        removeHotkey.Should().NotBeNull();
    }

    [Fact]
    public void CreateLiveOpsCommands_NullContext_ShouldThrow()
    {
        var act = () => MainViewModelFactories.CreateLiveOpsCommands(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateLiveOpsCommands_ShouldReturnAllCommands()
    {
        var context = BuildLiveOpsCommandContext();
        var (reliability, capture, apply, revert, restore,
            loadPresets, runBatch, scaffold, exportCalibration,
            buildCompat, exportBundle, exportTelemetry) =
            MainViewModelFactories.CreateLiveOpsCommands(context);

        reliability.Should().NotBeNull();
        capture.Should().NotBeNull();
        apply.Should().NotBeNull();
        revert.Should().NotBeNull();
        restore.Should().NotBeNull();
        loadPresets.Should().NotBeNull();
        runBatch.Should().NotBeNull();
        scaffold.Should().NotBeNull();
        exportCalibration.Should().NotBeNull();
        buildCompat.Should().NotBeNull();
        exportBundle.Should().NotBeNull();
        exportTelemetry.Should().NotBeNull();
    }

    [Fact]
    public void CreateQuickCommands_NullContext_ShouldThrow()
    {
        var act = () => MainViewModelFactories.CreateQuickCommands(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateQuickCommands_ShouldReturnAllCommands()
    {
        var context = BuildQuickCommandContext();
        var (credits, freezeTimer, fog, ai, instantBuild, unitCap, godMode, oneHit, unfreezeAll) =
            MainViewModelFactories.CreateQuickCommands(context);

        credits.Should().NotBeNull();
        freezeTimer.Should().NotBeNull();
        fog.Should().NotBeNull();
        ai.Should().NotBeNull();
        instantBuild.Should().NotBeNull();
        unitCap.Should().NotBeNull();
        godMode.Should().NotBeNull();
        oneHit.Should().NotBeNull();
        unfreezeAll.Should().NotBeNull();
    }

    [Fact]
    public void CreateCoreCommands_CanExecuteSelectedAction_ShouldReflectDelegate()
    {
        var canExecute = false;
        var context = BuildCoreCommandContext(canExecuteAction: () => canExecute);
        var (_, _, _, _, _, executeAction, _, _, _, _, _, _) =
            MainViewModelFactories.CreateCoreCommands(context);

        executeAction.CanExecute(null).Should().BeFalse();
        canExecute = true;
        executeAction.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CreateSaveCommands_CanRemoveHotkey_ShouldReflectDelegate()
    {
        var canRemove = false;
        var context = BuildSaveCommandContext(canRemoveHotkey: () => canRemove);
        var (_, _, _, _, _, _, _, _, _, _, _, _, _, _, _, removeHotkey) =
            MainViewModelFactories.CreateSaveCommands(context);

        removeHotkey.CanExecute(null).Should().BeFalse();
        canRemove = true;
        removeHotkey.CanExecute(null).Should().BeTrue();
    }

    private static MainViewModelCoreCommandContext BuildCoreCommandContext(
        Func<bool>? canExecuteAction = null)
    {
        return new MainViewModelCoreCommandContext
        {
            LoadProfilesAsync = () => Task.CompletedTask,
            LaunchAndAttachAsync = () => Task.CompletedTask,
            AttachAsync = () => Task.CompletedTask,
            DetachAsync = () => Task.CompletedTask,
            LoadActionsAsync = () => Task.CompletedTask,
            ExecuteActionAsync = () => Task.CompletedTask,
            LoadCatalogAsync = () => Task.CompletedTask,
            DeployHelperAsync = () => Task.CompletedTask,
            VerifyHelperAsync = () => Task.CompletedTask,
            CheckUpdatesAsync = () => Task.CompletedTask,
            InstallUpdateAsync = () => Task.CompletedTask,
            RollbackProfileUpdateAsync = () => Task.CompletedTask,
            CanUseSelectedProfile = () => true,
            CanExecuteSelectedAction = canExecuteAction ?? (() => true),
            IsAttached = () => true
        };
    }

    private static MainViewModelSaveCommandContext BuildSaveCommandContext(
        Func<bool>? canRemoveHotkey = null)
    {
        return new MainViewModelSaveCommandContext
        {
            BrowseSaveAsync = () => Task.CompletedTask,
            LoadSaveAsync = () => Task.CompletedTask,
            EditSaveFieldAsync = () => Task.CompletedTask,
            ValidateSaveAsync = () => Task.CompletedTask,
            RefreshSaveDiffPreviewAsync = () => Task.CompletedTask,
            WriteSaveAsync = () => Task.CompletedTask,
            BrowsePatchPackAsync = () => Task.CompletedTask,
            ExportPatchPackAsync = () => Task.CompletedTask,
            LoadPatchPackAsync = () => Task.CompletedTask,
            PreviewPatchPackAsync = () => Task.CompletedTask,
            ApplyPatchPackAsync = () => Task.CompletedTask,
            RestoreSaveBackupAsync = () => Task.CompletedTask,
            LoadHotkeysAsync = () => Task.CompletedTask,
            SaveHotkeysAsync = () => Task.CompletedTask,
            AddHotkeyAsync = () => Task.CompletedTask,
            RemoveHotkeyAsync = () => Task.CompletedTask,
            CanLoadSave = () => true,
            CanEditSave = () => true,
            CanValidateSave = () => true,
            CanRefreshDiff = () => true,
            CanWriteSave = () => true,
            CanExportPatchPack = () => true,
            CanLoadPatchPack = () => true,
            CanPreviewPatchPack = () => true,
            CanApplyPatchPack = () => true,
            CanRestoreBackup = () => true,
            CanRemoveHotkey = canRemoveHotkey ?? (() => true)
        };
    }

    private static MainViewModelLiveOpsCommandContext BuildLiveOpsCommandContext()
    {
        return new MainViewModelLiveOpsCommandContext
        {
            RefreshActionReliabilityAsync = () => Task.CompletedTask,
            CaptureSelectedUnitBaselineAsync = () => Task.CompletedTask,
            ApplySelectedUnitDraftAsync = () => Task.CompletedTask,
            RevertSelectedUnitTransactionAsync = () => Task.CompletedTask,
            RestoreSelectedUnitBaselineAsync = () => Task.CompletedTask,
            LoadSpawnPresetsAsync = () => Task.CompletedTask,
            RunSpawnBatchAsync = () => Task.CompletedTask,
            ScaffoldModProfileAsync = () => Task.CompletedTask,
            ExportCalibrationArtifactAsync = () => Task.CompletedTask,
            BuildModCompatibilityReportAsync = () => Task.CompletedTask,
            ExportSupportBundleAsync = () => Task.CompletedTask,
            ExportTelemetrySnapshotAsync = () => Task.CompletedTask,
            CanRunSpawnBatch = () => true,
            CanScaffoldModProfile = () => true,
            CanUseSupportBundleOutputDirectory = () => true,
            IsAttached = () => true,
            CanUseSelectedProfile = () => true
        };
    }

    private static MainViewModelQuickCommandContext BuildQuickCommandContext()
    {
        return new MainViewModelQuickCommandContext
        {
            QuickSetCreditsAsync = () => Task.CompletedTask,
            QuickFreezeTimerAsync = () => Task.CompletedTask,
            QuickToggleFogAsync = () => Task.CompletedTask,
            QuickToggleAiAsync = () => Task.CompletedTask,
            QuickInstantBuildAsync = () => Task.CompletedTask,
            QuickUnitCapAsync = () => Task.CompletedTask,
            QuickGodModeAsync = () => Task.CompletedTask,
            QuickOneHitAsync = () => Task.CompletedTask,
            QuickUnfreezeAllAsync = () => Task.CompletedTask,
            IsAttached = () => true
        };
    }
}
