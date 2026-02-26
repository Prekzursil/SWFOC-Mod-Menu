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
    {
        var handler = PropertyChanged;
        if (handler is null)
        {
            return;
        }

        handler(this, new PropertyChangedEventArgs(memberName));
    }

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

            var bestRecommendation = await TryResolveLaunchContextRecommendationAsync(processes);
            if (!string.IsNullOrWhiteSpace(bestRecommendation))
            {
                return bestRecommendation;
            }

            return MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(processes, BaseSwfocProfileId);
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
            var resolution = await ResolveAttachProfileAsync(requestedProfileId);

            Status = MainViewModelAttachHelpers.BuildAttachStartStatus(resolution.EffectiveProfileId, resolution.Variant);
            var session = await _runtime.AttachAsync(resolution.EffectiveProfileId);
            if (resolution.Variant is not null)
            {
                SelectedProfileId = resolution.EffectiveProfileId;
            }

            ApplyAttachSessionStatus(session);

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
            await HandleAttachFailureAsync(ex);
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

            return MainViewModelAttachHelpers.BuildAttachProcessHintSummary(all, UnknownValue);
        }
        catch
        {
            return "Could not enumerate process diagnostics.";
        }
    }

    private async Task<string?> TryResolveLaunchContextRecommendationAsync(IReadOnlyList<ProcessMetadata> processes)
    {
        var resolvedProfiles = await LoadResolvedProfilesForLaunchContextAsync();
        var contexts = processes
            .Select(process => process.LaunchContext ?? _launchContextResolver.Resolve(process, resolvedProfiles))
            .ToArray();

        return contexts
            .Where(context => !string.IsNullOrWhiteSpace(context.Recommendation.ProfileId))
            .OrderByDescending(context => context.Recommendation.Confidence)
            .ThenByDescending(context => context.LaunchKind == LaunchKind.Workshop || context.LaunchKind == LaunchKind.Mixed)
            .Select(context => context.Recommendation.ProfileId)
            .FirstOrDefault();
    }

    private async Task<(string EffectiveProfileId, ProfileVariantResolution? Variant)> ResolveAttachProfileAsync(string requestedProfileId)
    {
        if (!string.Equals(requestedProfileId, UniversalProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return (requestedProfileId, null);
        }

        var processes = await _processLocator.FindSupportedProcessesAsync();
        var variant = await _profileVariantResolver.ResolveAsync(requestedProfileId, processes, CancellationToken.None);
        return (variant.ResolvedProfileId, variant);
    }

    private void ApplyAttachSessionStatus(AttachSession session)
    {
        RuntimeMode = session.Process.Mode;
        ResolvedSymbolsCount = session.Symbols.Symbols.Count;
        var signatureCount = session.Symbols.Symbols.Values.Count(x => x.Source == AddressSource.Signature);
        var fallbackCount = session.Symbols.Symbols.Values.Count(x => x.Source == AddressSource.Fallback);
        var healthyCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Healthy);
        var degradedCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Degraded);
        var unresolvedCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Unresolved || x.Address == nint.Zero);
        Status = $"Attached to PID {session.Process.ProcessId} ({session.Process.ProcessName}) | " +
                 $"{MainViewModelDiagnostics.BuildProcessDiagnosticSummary(session.Process, UnknownValue)} | symbols: sig={signatureCount}, fallback={fallbackCount}, healthy={healthyCount}, degraded={degradedCount}, unresolved={unresolvedCount}";
    }

    private async Task HandleAttachFailureAsync(Exception ex)
    {
        RuntimeMode = RuntimeMode.Unknown;
        ResolvedSymbolsCount = 0;
        var processHint = await BuildAttachProcessHintAsync();
        Status = $"Attach failed: {ex.Message}. {processHint}";
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

        if (MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
                actionId,
                actionSpec,
                session,
                DefaultSymbolByActionId,
                out var unavailableReason))
        {
            return true;
        }

        var reason = string.IsNullOrWhiteSpace(unavailableReason)
            ? "action is unavailable for this attachment."
            : unavailableReason;
        Status = $"✗ {statusPrefix}: {reason}";
        return false;
    }

    // Kept as a compatibility shim for reflection-based tests that assert gating semantics.
    private static string? ResolveActionUnavailableReason(string actionId, ActionSpec spec, AttachSession session)
    {
        MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            actionId,
            spec,
            session,
            DefaultSymbolByActionId,
            out var unavailableReason);
        return unavailableReason;
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
                !MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
                    actionId,
                    actionSpec,
                    _runtime.CurrentSession,
                    DefaultSymbolByActionId,
                    out _))
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
                ? $"Action succeeded: {result.Message}{MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result)}"
                : $"Action failed: {result.Message}{MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result)}";
        }
        catch (Exception ex)
        {
            Status = $"Action failed: {ex.Message}";
        }
    }

    private void ApplyPayloadTemplateForSelectedAction()
    {
        if (!TryGetRequiredPayloadKeysForSelectedAction(out var required))
        {
            return;
        }

        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            SelectedActionId,
            required,
            DefaultSymbolByActionId,
            DefaultHelperHookByActionId);
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(SelectedActionId, payload);

        // Only apply a template when it would actually help. Don't clobber the user's JSON with "{}".
        if (payload.Count == 0)
        {
            return;
        }

        PayloadJson = payload.ToJsonString(PrettyJson);
    }

    private bool TryGetRequiredPayloadKeysForSelectedAction(out JsonArray required)
    {
        required = new JsonArray();
        if (string.IsNullOrWhiteSpace(SelectedActionId))
        {
            return false;
        }

        if (!_loadedActionSpecs.TryGetValue(SelectedActionId, out var action))
        {
            return false;
        }

        if (!action.PayloadSchema.TryGetPropertyValue("required", out var requiredNode) || requiredNode is not JsonArray requiredKeys)
        {
            return false;
        }

        required = requiredKeys;
        return true;
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
            OpsArtifactSummary = $"install failed ({result.ReasonCode ?? UnknownValue})";
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
            OpsArtifactSummary = $"rollback failed ({rollback.ReasonCode ?? UnknownValue})";
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
            BaseProfileId: string.IsNullOrWhiteSpace(OnboardingBaseProfileId) ? BaseSwfocProfileId : OnboardingBaseProfileId,
            LaunchSamples: launchSamples,
            ProfileAliases: new[] { OnboardingDraftProfileId, OnboardingDisplayName },
            NamespaceRoot: OnboardingNamespaceRoot,
            Notes: "Generated by Mod Compatibility Studio");

        var result = await _modOnboarding.ScaffoldDraftProfileAsync(request, CancellationToken.None);
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

        if (dialog.ShowDialog().GetValueOrDefault())
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

        object? value = MainViewModelDiagnostics.ParsePrimitive(SaveEditValue);
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

        if (dialog.ShowDialog().GetValueOrDefault())
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

        if (!dialog.ShowDialog().GetValueOrDefault())
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

        if (!PreparePatchPreview(SelectedProfileId))
        {
            return;
        }

        var compatibility = await _savePatchPackService.ValidateCompatibilityAsync(_loadedPatchPack, _loadedSave, SelectedProfileId);
        var preview = await _savePatchPackService.PreviewApplyAsync(_loadedPatchPack, _loadedSave, SelectedProfileId);
        _loadedPatchPreview = preview;

        PopulatePatchPreviewOperations(preview);
        PopulatePatchCompatibilityRows(compatibility, preview);
        SavePatchMetadataSummary = MainViewModelDiagnostics.BuildPatchMetadataSummary(_loadedPatchPack);
        SavePatchApplySummary = string.Empty;
        Status = preview.IsCompatible && compatibility.IsCompatible
            ? $"Patch preview ready: {SavePatchOperations.Count} operation(s) would be applied."
            : "Patch preview blocked by compatibility/validation errors.";
    }

    private bool PreparePatchPreview(string selectedProfileId)
    {
        if (ValidateSaveRuntimeVariant(selectedProfileId, out var variantMessage))
        {
            return true;
        }

        SavePatchCompatibility.Clear();
        SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem("error", "save_variant_mismatch", variantMessage));
        SavePatchApplySummary = variantMessage;
        Status = variantMessage;
        return false;
    }

    private void PopulatePatchPreviewOperations(SavePatchPreview preview)
    {
        SavePatchOperations.Clear();
        foreach (var operation in preview.OperationsToApply)
        {
            SavePatchOperations.Add(new SavePatchOperationViewItem(
                operation.Kind.ToString(),
                operation.FieldPath,
                operation.FieldId,
                operation.ValueType,
                MainViewModelDiagnostics.FormatPatchValue(operation.OldValue),
                MainViewModelDiagnostics.FormatPatchValue(operation.NewValue)));
        }
    }

    private void PopulatePatchCompatibilityRows(SavePatchCompatibilityResult compatibility, SavePatchPreview preview)
    {
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
        AppendPatchCompatibilityRows("warning", "compatibility_warning", compatibility.Warnings);
        AppendPatchCompatibilityRows("warning", "preview_warning", preview.Warnings);
        AppendPatchCompatibilityRows("error", "compatibility_error", compatibility.Errors);
        AppendPatchCompatibilityRows("error", "preview_error", preview.Errors);
    }

    private void AppendPatchCompatibilityRows(string severity, string reasonCode, IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            SavePatchCompatibility.Add(new SavePatchCompatibilityViewItem(severity, reasonCode, message));
        }
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

}
