using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Galactic Map tab) — INPC wrapper around GalacticTabState.
/// Surfaces the planet roster (DataGrid-bound), planet-owner change, the
/// reveal-all toggle (with auto-cleanup on disable), and the diplomacy
/// pair editor.
/// </summary>
public sealed class GalacticTabViewModel : ObservableBase
{
    private readonly V2BridgeAdapter _bridge;
    private readonly V2UnitMutationDispatcher _unitMutator;
    private readonly GalacticTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly FeatureToggleCoordinator _toggles;
    private readonly ObservableCollection<PlanetRow> _planets = new();

    // 2026-05-07 (iter 317, first UI consumer of iter-315 ResolvePlanetIcon):
    // parallel collection with optional IconPath per planet. Bound by the
    // Galactic tab DataGrid ItemsSource. _iconResolver is mutable (not
    // readonly) so MainViewModelV2 can hot-swap it via SetIconResolver
    // when the operator changes Settings.IconsRoot — same pattern as
    // iter-312 SpawningTabViewModel.SetIconResolver.
    private readonly ObservableCollection<PlanetRowWithIcon> _planetRows = new();
    private UnitIconResolver? _iconResolver;

    private string _selectedPlanetId = string.Empty;
    private string _newOwnerFaction = string.Empty;
    private string _diplomacySlotA = string.Empty;
    private string _diplomacySlotB = string.Empty;
    private DiplomacyRelation _diplomacyRelation = DiplomacyRelation.Neutral;
    private string _lastStatus = "(idle)";

    // 2026-05-05 (iter 200): FOW reveal wire UI fields.
    // FOWPlayerLuaExpr is shared across all 3 FOW buttons. FOWPositionLuaExpr
    // and FOWRadiusLuaExpr are only used by the partial-reveal button.
    private string _fowPlayerLuaExpr = "Find_Player(\"REBEL\")";
    private string _fowPositionLuaExpr = "FindPlanet(\"Yavin\"):Get_Position()";
    private string _fowRadiusLuaExpr = "500";

    public GalacticTabViewModel(
        V2BridgeAdapter bridge,
        V2UnitMutationDispatcher unitMutator,
        UnitIconResolver? iconResolver = null)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(unitMutator);
        _bridge = bridge;
        _unitMutator = unitMutator;
        // iter-317: optional resolver — null is the no-icons default. Existing
        // callers that pass only (bridge, unitMutator) keep working unchanged
        // via the optional default-null ctor extension pattern (iter-301/308/311).
        _iconResolver = iconResolver;
        _sink = new RecordingFeedbackSink();
        _toggles = new FeatureToggleCoordinator(_sink);
        var dispatcher = new BridgeGalacticDispatcher(bridge);
        _state = new GalacticTabState(dispatcher, _sink, _toggles);

        RefreshPlanetsCommand = new AsyncRelayCommand(RefreshPlanetsCore, onError: HandleError);
        ChangeOwnerCommand = new AsyncRelayCommand(ChangeOwnerCore, () => false, HandleError);
        ToggleRevealAllCommand = new AsyncRelayCommand(ToggleRevealAllCore, onError: HandleError);
        SetDiplomacyCommand = new AsyncRelayCommand(SetDiplomacyCore, onError: HandleError);
        // 2026-04-27 (iter 33) — Overlay Feature 3 modes wired to UI buttons.
        ChangeOwnerConvertCommand = new AsyncRelayCommand(
            () => ChangeOwnerWithModeCore(PlanetFlipMode.Convert), () => false, HandleError);
        ChangeOwnerPureKickCommand = new AsyncRelayCommand(
            () => ChangeOwnerWithModeCore(PlanetFlipMode.PureKick), () => false, HandleError);
        // 2026-04-27 (iter 34) — Overlay Feature 2 surfaced as a button.
        SpawnAsStoryArrivalCommand = new AsyncRelayCommand(
            SpawnAsStoryArrivalCore, () => false, HandleError);
        // 2026-04-27: clipboard export of the planet DataGrid for offline
        // analysis. Same pattern as Tactical Units export.
        ExportPlanetsCsvCommand = new RelayCommand(ExportPlanetsToCsv);

        // 2026-05-05 (iter 200): Fog-of-War reveal wires. Three new commands
        // route through V2UnitMutationDispatcher — iter-180 FOWRevealAll/Undo
        // (LIVE) for the whole-map cinematic-toggle workflow plus iter-184
        // partial-area FOWReveal (LIVE). All three are LIVE so no
        // PHASE 2 PENDING gating is needed; they appear under a new "Fog of
        // War" GroupBox parallel to the existing Diplomacy/Reveal-toggle
        // sections.
        FOWRevealAllLuaCommand = new AsyncRelayCommand(FOWRevealAllLuaCore, onError: HandleError);
        FOWUndoRevealAllLuaCommand = new AsyncRelayCommand(FOWUndoRevealAllLuaCore, onError: HandleError);
        FOWRevealLuaCommand = new AsyncRelayCommand(FOWRevealLuaCore, onError: HandleError);

        // 2026-04-27 (iter 57): per-button capability metadata. The Galactic
        // tab spans LIVE (RevealAll) and PHASE 2 PENDING (GetPlanets,
        // ChangePlanetOwner, ChangePlanetOwnerWithMode, SpawnAsStoryArrival,
        // SetDiplomacy). Per-button badges + tab-level Phase-2-pending
        // banner expose the truth without burying it in group headers.
        // Note: Diplomacy is implemented in Core via engine-native
        // Find_Player + :Make_Ally / :Make_Enemy (NOT a SWFOC_ wrapper),
        // but the catalogued SWFOC_SetDiplomacy entry represents the
        // operator-facing capability — both backings are PHASE 2 PENDING
        // until the galactic state API is wired live.
        RefreshPlanets = new CapabilityAwareAction("Refresh planets", "SWFOC_GetPlanets");
        ChangeOwner = new CapabilityAwareAction("Change planet owner", "SWFOC_ChangePlanetOwner");
        ChangeOwnerConvert = new CapabilityAwareAction("Flip & convert garrison",
            "SWFOC_ChangePlanetOwnerWithMode");
        ChangeOwnerPureKick = new CapabilityAwareAction("Flip & destroy garrison",
            "SWFOC_ChangePlanetOwnerWithMode");
        ToggleRevealAll = new CapabilityAwareAction("Toggle reveal-all", "SWFOC_RevealAll");
        SetDiplomacy = new CapabilityAwareAction("Set diplomacy", "SWFOC_SetDiplomacy");
        SpawnAsStoryArrival = new CapabilityAwareAction("Story-arrival spawn",
            "SWFOC_SpawnAsStoryArrival");
        // 2026-05-05 (iter 200): FOW capability actions — all LIVE.
        FOWRevealAllLua = new CapabilityAwareAction("FOW reveal map", "SWFOC_FOWRevealAllLua");
        FOWUndoRevealAllLua = new CapabilityAwareAction("FOW restore fog", "SWFOC_FOWUndoRevealAllLua");
        FOWRevealLua = new CapabilityAwareAction("FOW reveal at position", "SWFOC_FOWRevealLua");

        // 2026-05-06 (iter 215): TaskForce write-side mega-batch (iter-175 + 176).
        // 8 wires, 9 buttons (Set_As_Goal_System_Removable on/off pair).
        // All anchor on TaskForceLuaExpr (new field). Secondary args use
        // TaskForceTargetLuaExpr (new field) for target/planet/type.
        TaskForceMoveToLuaCommand = new AsyncRelayCommand(TaskForceMoveToCore, onError: HandleError);
        TaskForceReinforceLuaCommand = new AsyncRelayCommand(TaskForceReinforceCore, onError: HandleError);
        TaskForceReleaseReinforcementsLuaCommand = new AsyncRelayCommand(TaskForceReleaseReinforcementsCore, onError: HandleError);
        TaskForceLaunchUnitsLuaCommand = new AsyncRelayCommand(TaskForceLaunchUnitsCore, onError: HandleError);
        TaskForceAttackTargetLuaCommand = new AsyncRelayCommand(TaskForceAttackTargetCore, onError: HandleError);
        TaskForceGuardTargetLuaCommand = new AsyncRelayCommand(TaskForceGuardTargetCore, onError: HandleError);
        TaskForceLandUnitsLuaCommand = new AsyncRelayCommand(TaskForceLandUnitsCore, onError: HandleError);
        TaskForceSetAsGoalSystemRemovableOnLuaCommand = new AsyncRelayCommand(() => TaskForceSetAsGoalSystemRemovableCore("1"), onError: HandleError);
        TaskForceSetAsGoalSystemRemovableOffLuaCommand = new AsyncRelayCommand(() => TaskForceSetAsGoalSystemRemovableCore("0"), onError: HandleError);

        TaskForceMoveToLuaAction = new CapabilityAwareAction("TaskForce move to (Lua)", "SWFOC_TaskForceMoveToLua");
        TaskForceReinforceLuaAction = new CapabilityAwareAction("TaskForce reinforce (Lua)", "SWFOC_TaskForceReinforceLua");
        TaskForceReleaseReinforcementsLuaAction = new CapabilityAwareAction("TaskForce release reinforcements (Lua)", "SWFOC_TaskForceReleaseReinforcementsLua");
        TaskForceLaunchUnitsLuaAction = new CapabilityAwareAction("TaskForce launch units (Lua)", "SWFOC_TaskForceLaunchUnitsLua");
        TaskForceAttackTargetLuaAction = new CapabilityAwareAction("TaskForce attack target (Lua)", "SWFOC_TaskForceAttackTargetLua");
        TaskForceGuardTargetLuaAction = new CapabilityAwareAction("TaskForce guard target (Lua)", "SWFOC_TaskForceGuardTargetLua");
        TaskForceLandUnitsLuaAction = new CapabilityAwareAction("TaskForce land units (Lua)", "SWFOC_TaskForceLandUnitsLua");
        TaskForceSetAsGoalSystemRemovableOnLuaAction = new CapabilityAwareAction("TaskForce: goal-system-removable on (Lua)", "SWFOC_TaskForceSetAsGoalSystemRemovableLua");
        TaskForceSetAsGoalSystemRemovableOffLuaAction = new CapabilityAwareAction("TaskForce: goal-system-removable off (Lua)", "SWFOC_TaskForceSetAsGoalSystemRemovableLua");

        // 2026-05-06 (iter 218): TaskForceMoveToTarget single-wire extension
        // (iter-179). TaskForceClass-only method distinct from iter-215
        // SWFOC_TaskForceMoveToLua — Move_To_Target takes a target object,
        // Move_To takes a position. Reuses iter-215 TaskForceLuaExpr +
        // TaskForceTargetLuaExpr — zero new fields.
        TaskForceMoveToTargetLuaCommand = new AsyncRelayCommand(TaskForceMoveToTargetCore, onError: HandleError);
        TaskForceMoveToTargetLuaAction = new CapabilityAwareAction("TaskForce move to target (Lua)", "SWFOC_TaskForceMoveToTargetLua");

        // 2026-05-19: startup auto-refresh moved to MainViewModelV2.OnWindowLoadedAsync.
        // Constructors stay side-effect-free so simulator and XAML tests do not race
        // against an overlapping bridge refresh.
    }

    public CapabilityAwareAction RefreshPlanets { get; }
    public CapabilityAwareAction ChangeOwner { get; }
    public CapabilityAwareAction ChangeOwnerConvert { get; }
    public CapabilityAwareAction ChangeOwnerPureKick { get; }
    public CapabilityAwareAction ToggleRevealAll { get; }
    public CapabilityAwareAction SetDiplomacy { get; }
    public CapabilityAwareAction SpawnAsStoryArrival { get; }
    // 2026-05-05 (iter 200): Fog-of-War reveal capability actions.
    public CapabilityAwareAction FOWRevealAllLua { get; }
    public CapabilityAwareAction FOWUndoRevealAllLua { get; }
    public CapabilityAwareAction FOWRevealLua { get; }

    // 2026-05-06 (iter 215): TaskForce write-side commands + capability actions.
    public ICommand TaskForceMoveToLuaCommand { get; }
    public ICommand TaskForceReinforceLuaCommand { get; }
    public ICommand TaskForceReleaseReinforcementsLuaCommand { get; }
    public ICommand TaskForceLaunchUnitsLuaCommand { get; }
    public ICommand TaskForceAttackTargetLuaCommand { get; }
    public ICommand TaskForceGuardTargetLuaCommand { get; }
    public ICommand TaskForceLandUnitsLuaCommand { get; }
    public ICommand TaskForceSetAsGoalSystemRemovableOnLuaCommand { get; }
    public ICommand TaskForceSetAsGoalSystemRemovableOffLuaCommand { get; }
    public CapabilityAwareAction TaskForceMoveToLuaAction { get; }
    public CapabilityAwareAction TaskForceReinforceLuaAction { get; }
    public CapabilityAwareAction TaskForceReleaseReinforcementsLuaAction { get; }
    public CapabilityAwareAction TaskForceLaunchUnitsLuaAction { get; }
    public CapabilityAwareAction TaskForceAttackTargetLuaAction { get; }
    public CapabilityAwareAction TaskForceGuardTargetLuaAction { get; }
    public CapabilityAwareAction TaskForceLandUnitsLuaAction { get; }
    public CapabilityAwareAction TaskForceSetAsGoalSystemRemovableOnLuaAction { get; }
    public CapabilityAwareAction TaskForceSetAsGoalSystemRemovableOffLuaAction { get; }

    // 2026-05-06 (iter 218): TaskForceMoveToTarget single-wire extension command + capability action.
    public ICommand TaskForceMoveToTargetLuaCommand { get; }
    public CapabilityAwareAction TaskForceMoveToTargetLuaAction { get; }

    /// <summary>
    /// 2026-05-06 (iter 215): TaskForce receiver Lua expression. Operator types
    /// e.g. <c>Find_TaskForce("MyForce")</c> or any TaskForce handle.
    /// </summary>
    private string _taskForceLuaExpr = string.Empty;
    public string TaskForceLuaExpr
    {
        get => _taskForceLuaExpr;
        set => SetField(ref _taskForceLuaExpr, value ?? string.Empty);
    }

    /// <summary>
    /// 2026-05-06 (iter 215): TaskForce secondary arg — semantically polymorphic
    /// (target unit/position for Move_To/Attack/Guard, planet handle for
    /// Launch_Units/Land_Units, unit-type for Reinforce). Operator types the
    /// appropriate Lua expression depending on which button is clicked.
    /// </summary>
    private string _taskForceTargetLuaExpr = string.Empty;
    public string TaskForceTargetLuaExpr
    {
        get => _taskForceTargetLuaExpr;
        set => SetField(ref _taskForceTargetLuaExpr, value ?? string.Empty);
    }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        RefreshPlanets, ChangeOwner, ChangeOwnerConvert, ChangeOwnerPureKick,
        ToggleRevealAll, SetDiplomacy, SpawnAsStoryArrival,
        FOWRevealAllLua, FOWUndoRevealAllLua, FOWRevealLua,
        // iter 215: TaskForce write-side
        TaskForceMoveToLuaAction, TaskForceReinforceLuaAction,
        TaskForceReleaseReinforcementsLuaAction, TaskForceLaunchUnitsLuaAction,
        TaskForceAttackTargetLuaAction, TaskForceGuardTargetLuaAction,
        TaskForceLandUnitsLuaAction,
        TaskForceSetAsGoalSystemRemovableOnLuaAction,
        TaskForceSetAsGoalSystemRemovableOffLuaAction,
        // iter 218: TaskForceMoveToTarget single-wire extension
        TaskForceMoveToTargetLuaAction,
    };

    public bool HasPhase2PendingAction => AllActions.Any(a => !a.IsAllLive);

    public string Phase2PendingWarning
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "Some actions on this tab are PHASE 2 PENDING; mutating buttons without a live "
                + "engine hook are disabled. Affected: "
                + string.Join("; ", parts);
        }
    }

    public RelayCommand ExportPlanetsCsvCommand { get; }

    private void ExportPlanetsToCsv()
    {
        var sb = new System.Text.StringBuilder(8 * 1024);
        sb.AppendLine("planet_id,owner_faction,tech_level");
        foreach (var p in _planets)
        {
            sb.Append(p.PlanetId).Append(',');
            sb.Append(p.OwnerFaction).Append(',');
            sb.Append(p.TechLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine();
        }
        try
        {
            System.Windows.Clipboard.SetText(sb.ToString());
            LastStatus = $"Exported {_planets.Count} planet(s) to clipboard as CSV.";
        }
        catch (Exception ex)
        {
            LastStatus = $"CSV export failed: {ex.Message}";
        }
    }

    public ObservableCollection<PlanetRow> Planets => _planets;

    /// <summary>
    /// 2026-05-07 (iter 317, first UI consumer of iter-315 ResolvePlanetIcon):
    /// parallel collection bound by the Galactic tab DataGrid ItemsSource.
    /// Each row's IconPath is resolved via iter-307 ThumbnailCache + iter-315
    /// UnitIconResolver.ResolvePlanetIcon (default size 96); null when no
    /// resolver is wired OR the operator hasn't extracted the planet DDS yet.
    /// </summary>
    public ObservableCollection<PlanetRowWithIcon> PlanetRows => _planetRows;

    /// <summary>
    /// 2026-05-07 (iter 317): hot-swap the icon resolver. Composition root
    /// (MainViewModelV2) calls this when the operator changes
    /// Settings.IconsRoot so Galactic tab rows re-resolve immediately — no
    /// editor restart required. Mirrors iter-312
    /// SpawningTabViewModel.SetIconResolver. Pass null to disable icons
    /// (clears all IconPaths to null on next refresh).
    /// </summary>
    public void SetIconResolver(UnitIconResolver? iconResolver)
    {
        _iconResolver = iconResolver;
        // Rebuild rows from the existing _planets list so the operator sees
        // the change without waiting for the next bridge-driven RefreshPlanets.
        // Without this, IconPaths wouldn't update until SWFOC_GetPlanets ran
        // again — which only happens on tab activate or manual refresh button.
        RebuildPlanetRows();
    }

    public string SelectedPlanetId
    {
        get => _selectedPlanetId;
        set { if (SetField(ref _selectedPlanetId, value ?? string.Empty)) _state.SelectedPlanetId = _selectedPlanetId; }
    }

    public string NewOwnerFaction
    {
        get => _newOwnerFaction;
        set { if (SetField(ref _newOwnerFaction, value ?? string.Empty)) _state.NewOwnerFaction = _newOwnerFaction; }
    }

    public string DiplomacySlotA
    {
        get => _diplomacySlotA;
        set { if (SetField(ref _diplomacySlotA, value ?? string.Empty)) _state.DiplomacySlotA = _diplomacySlotA; }
    }

    public string DiplomacySlotB
    {
        get => _diplomacySlotB;
        set { if (SetField(ref _diplomacySlotB, value ?? string.Empty)) _state.DiplomacySlotB = _diplomacySlotB; }
    }

    public DiplomacyRelation DiplomacyRelation
    {
        get => _diplomacyRelation;
        set { if (SetField(ref _diplomacyRelation, value)) _state.DiplomacyRelation = value; }
    }

    // 2026-04-27: expose the FULL DiplomacyRelation enum, not just
    // Allied/Hostile. Tab-inventory audit found Neutral and other entries
    // were silently hidden from the operator. Built dynamically from
    // Enum.GetValues so adding a new enum value (e.g. Tributary) shows up
    // automatically without an XAML edit.
    public IReadOnlyList<DiplomacyRelation> DiplomacyOptions { get; } =
        Enum.GetValues<DiplomacyRelation>();

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge(
        "SWFOC_GetPlanets", "SWFOC_ChangePlanetOwner", "SWFOC_RevealAll", "SWFOC_SetDiplomacy");

    public bool IsRevealAllEnabled => _toggles.IsEnabled("reveal_all");

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand RefreshPlanetsCommand { get; }
    public ICommand ChangeOwnerCommand { get; }
    public ICommand ToggleRevealAllCommand { get; }
    public ICommand SetDiplomacyCommand { get; }
    public ICommand ChangeOwnerConvertCommand { get; }
    public ICommand ChangeOwnerPureKickCommand { get; }
    public ICommand SpawnAsStoryArrivalCommand { get; }

    // 2026-05-05 (iter 200): Fog-of-War reveal commands. These are LIVE
    // engine-Lua-API wires that compose FOWManager.Reveal_All(player) /
    // FOWManager.Undo_Reveal_All(player) / FOWManager.Reveal(player,
    // position, radius). Useful for cinematic/debug workflows where the
    // operator wants to inspect enemy positions or set up Director Mode
    // shots without the FOW obscuring the camera target.
    public ICommand FOWRevealAllLuaCommand { get; }
    public ICommand FOWUndoRevealAllLuaCommand { get; }
    public ICommand FOWRevealLuaCommand { get; }

    public string FOWPlayerLuaExpr
    {
        get => _fowPlayerLuaExpr;
        set => SetField(ref _fowPlayerLuaExpr, value ?? string.Empty);
    }

    public string FOWPositionLuaExpr
    {
        get => _fowPositionLuaExpr;
        set => SetField(ref _fowPositionLuaExpr, value ?? string.Empty);
    }

    public string FOWRadiusLuaExpr
    {
        get => _fowRadiusLuaExpr;
        set => SetField(ref _fowRadiusLuaExpr, value ?? string.Empty);
    }

    // 2026-04-27 (iter 34) — bound inputs for the story-arrival spawn UI.
    private string _storyArrivalTypeId = string.Empty;
    private string _storyArrivalPlanetId = string.Empty;
    private string _storyArrivalFaction = string.Empty;

    public string StoryArrivalTypeId
    {
        get => _storyArrivalTypeId;
        set { if (SetField(ref _storyArrivalTypeId, value ?? string.Empty)) _state.StoryArrivalTypeId = _storyArrivalTypeId; }
    }

    public string StoryArrivalPlanetId
    {
        get => _storyArrivalPlanetId;
        set { if (SetField(ref _storyArrivalPlanetId, value ?? string.Empty)) _state.StoryArrivalPlanetId = _storyArrivalPlanetId; }
    }

    public string StoryArrivalFaction
    {
        get => _storyArrivalFaction;
        set { if (SetField(ref _storyArrivalFaction, value ?? string.Empty)) _state.StoryArrivalFaction = _storyArrivalFaction; }
    }

    private async Task SpawnAsStoryArrivalCore() =>
        ApplyFeedback(await _state.SpawnAsStoryArrivalAsync());

    public Task RefreshPlanetsAsync() => RefreshPlanetsCore();

    private async Task RefreshPlanetsCore()
    {
        ApplyFeedback(await _state.RefreshPlanetsAsync());
        _planets.Clear();
        foreach (var p in _state.Planets) _planets.Add(p);
        // iter-317: populate the parallel icon-aware projection alongside
        // the existing string-keyed Planets list. Order preserved 1:1 so
        // operator-visible row ordering doesn't drift.
        RebuildPlanetRows();
    }

    /// <summary>
    /// 2026-05-07 (iter 317): rebuild the icon-aware projection from the
    /// current <see cref="Planets"/> list. Called both after RefreshPlanets
    /// (bridge-driven) and SetIconResolver (Settings-driven hot-swap).
    /// Resolver returns null gracefully when not wired OR the planet DDS
    /// isn't extracted/cached yet — null IconPath hides the Image control
    /// in WPF (no broken-image placeholder, no error noise).
    /// </summary>
    private void RebuildPlanetRows()
    {
        _planetRows.Clear();
        // iter-317: snapshot _planets via ToList() before iterating so a
        // user-initiated overlap of refresh + Settings.IconsRoot edit can't
        // mutate the source collection mid-enumeration. Cheap defensive copy.
        foreach (var p in _planets.ToList())
        {
            var iconPath = _iconResolver?.ResolvePlanetIcon(p.PlanetId);
            _planetRows.Add(new PlanetRowWithIcon(
                p.PlanetId, p.OwnerFaction, p.TechLevel, iconPath));
        }
    }

    private async Task ChangeOwnerCore() => ApplyFeedback(await _state.ChangePlanetOwnerAsync());

    private async Task ChangeOwnerWithModeCore(PlanetFlipMode mode) =>
        ApplyFeedback(await _state.ChangePlanetOwnerWithModeAsync(mode));

    private async Task ToggleRevealAllCore()
    {
        var next = !_toggles.IsEnabled("reveal_all");
        ApplyFeedback(await _state.ToggleRevealAllAsync(next));
        OnPropertyChanged(nameof(IsRevealAllEnabled));
    }

    private async Task SetDiplomacyCore() => ApplyFeedback(await _state.SetDiplomacyAsync());

    // 2026-05-05 (iter 200): FOW reveal handlers. Each composes the right
    // dispatcher call from the operator's Lua expression(s). The bridge
    // captures the engine return value (FOWManager.Reveal_All returns nil
    // but the wrapper's payload includes "OK: ..." status) so operators
    // see the full bridge round-trip in LastStatus.
    private async Task FOWRevealAllLuaCore()
    {
        var result = await _unitMutator.FOWRevealAllLuaAsync(_fowPlayerLuaExpr, default);
        LastStatus = result.Succeeded
            ? $"FOW reveal-all: OK — {result.Response}"
            : $"FOW reveal-all: FAIL — {result.ErrorMessage}";
    }

    private async Task FOWUndoRevealAllLuaCore()
    {
        var result = await _unitMutator.FOWUndoRevealAllLuaAsync(_fowPlayerLuaExpr, default);
        LastStatus = result.Succeeded
            ? $"FOW undo-reveal-all: OK — {result.Response}"
            : $"FOW undo-reveal-all: FAIL — {result.ErrorMessage}";
    }

    private async Task FOWRevealLuaCore()
    {
        var result = await _unitMutator.FOWRevealLuaAsync(
            _fowPlayerLuaExpr, _fowPositionLuaExpr, _fowRadiusLuaExpr, default);
        LastStatus = result.Succeeded
            ? $"FOW partial-reveal: OK — {result.Response}"
            : $"FOW partial-reveal: FAIL — {result.ErrorMessage}";
    }

    // 2026-05-06 (iter 215): TaskForce write-side handlers (iter-175 + iter-176).
    // All anchor on TaskForceLuaExpr; secondary args use TaskForceTargetLuaExpr.
    // SetAsGoalSystemRemovable takes hardcoded bool string (iter-204 pattern).
    private void RequireTaskForceArgs(out bool ok, bool needsTarget = true)
    {
        if (string.IsNullOrWhiteSpace(_taskForceLuaExpr))
        {
            LastStatus = "(no TaskForce Lua expression — type one above first)";
            ok = false;
            return;
        }
        if (needsTarget && string.IsNullOrWhiteSpace(_taskForceTargetLuaExpr))
        {
            LastStatus = "(no target/planet/type — type a Lua expression into the Target field first)";
            ok = false;
            return;
        }
        ok = true;
    }

    private async Task TaskForceMoveToCore()
    {
        RequireTaskForceArgs(out var ok); if (!ok) return;
        var r = await _unitMutator.TaskForceMoveToLuaAsync(_taskForceLuaExpr, _taskForceTargetLuaExpr, default);
        LastStatus = r.Succeeded ? $"TaskForce Move_To: OK — {r.Response}" : $"TaskForce Move_To: FAIL — {r.ErrorMessage}";
    }

    private async Task TaskForceReinforceCore()
    {
        RequireTaskForceArgs(out var ok); if (!ok) return;
        var r = await _unitMutator.TaskForceReinforceLuaAsync(_taskForceLuaExpr, _taskForceTargetLuaExpr, default);
        LastStatus = r.Succeeded ? $"TaskForce Reinforce: OK — {r.Response}" : $"TaskForce Reinforce: FAIL — {r.ErrorMessage}";
    }

    private async Task TaskForceReleaseReinforcementsCore()
    {
        RequireTaskForceArgs(out var ok, needsTarget: false); if (!ok) return;
        var r = await _unitMutator.TaskForceReleaseReinforcementsLuaAsync(_taskForceLuaExpr, default);
        LastStatus = r.Succeeded ? $"TaskForce Release_Reinforcements: OK — {r.Response}" : $"TaskForce Release_Reinforcements: FAIL — {r.ErrorMessage}";
    }

    private async Task TaskForceLaunchUnitsCore()
    {
        RequireTaskForceArgs(out var ok); if (!ok) return;
        var r = await _unitMutator.TaskForceLaunchUnitsLuaAsync(_taskForceLuaExpr, _taskForceTargetLuaExpr, default);
        LastStatus = r.Succeeded ? $"TaskForce Launch_Units: OK — {r.Response}" : $"TaskForce Launch_Units: FAIL — {r.ErrorMessage}";
    }

    private async Task TaskForceAttackTargetCore()
    {
        RequireTaskForceArgs(out var ok); if (!ok) return;
        var r = await _unitMutator.TaskForceAttackTargetLuaAsync(_taskForceLuaExpr, _taskForceTargetLuaExpr, default);
        LastStatus = r.Succeeded ? $"TaskForce Attack_Target: OK — {r.Response}" : $"TaskForce Attack_Target: FAIL — {r.ErrorMessage}";
    }

    private async Task TaskForceGuardTargetCore()
    {
        RequireTaskForceArgs(out var ok); if (!ok) return;
        var r = await _unitMutator.TaskForceGuardTargetLuaAsync(_taskForceLuaExpr, _taskForceTargetLuaExpr, default);
        LastStatus = r.Succeeded ? $"TaskForce Guard_Target: OK — {r.Response}" : $"TaskForce Guard_Target: FAIL — {r.ErrorMessage}";
    }

    private async Task TaskForceLandUnitsCore()
    {
        RequireTaskForceArgs(out var ok); if (!ok) return;
        var r = await _unitMutator.TaskForceLandUnitsLuaAsync(_taskForceLuaExpr, _taskForceTargetLuaExpr, default);
        LastStatus = r.Succeeded ? $"TaskForce Land_Units: OK — {r.Response}" : $"TaskForce Land_Units: FAIL — {r.ErrorMessage}";
    }

    private async Task TaskForceSetAsGoalSystemRemovableCore(string boolStringArg)
    {
        RequireTaskForceArgs(out var ok, needsTarget: false); if (!ok) return;
        var r = await _unitMutator.TaskForceSetAsGoalSystemRemovableLuaAsync(_taskForceLuaExpr, boolStringArg, default);
        LastStatus = r.Succeeded ? $"TaskForce Set_As_Goal_System_Removable({boolStringArg}): OK — {r.Response}" : $"TaskForce Set_As_Goal_System_Removable({boolStringArg}): FAIL — {r.ErrorMessage}";
    }

    // 2026-05-06 (iter 218): TaskForceMoveToTarget single-wire handler.
    // Reuses iter-215 RequireTaskForceArgs validation + TaskForceLuaExpr +
    // TaskForceTargetLuaExpr fields. Distinct from iter-215 TaskForceMoveToCore
    // (which targets a position via Move_To); this one targets a unit/object
    // via Move_To_Target.
    private async Task TaskForceMoveToTargetCore()
    {
        RequireTaskForceArgs(out var ok); if (!ok) return;
        var r = await _unitMutator.TaskForceMoveToTargetLuaAsync(_taskForceLuaExpr, _taskForceTargetLuaExpr, default);
        LastStatus = r.Succeeded ? $"TaskForce Move_To_Target: OK — {r.Response}" : $"TaskForce Move_To_Target: FAIL — {r.ErrorMessage}";
    }

    private void ApplyFeedback(UxFeedback fb)
    {
        LastStatus = string.Format(CultureInfo.InvariantCulture,
            "{0}: {1} — {2}", fb.Severity, fb.Title, fb.Message);
    }

    private void HandleError(Exception ex)
    {
        LastStatus = $"command failed: {ex.Message}";
    }
}
