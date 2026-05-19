using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Win32;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;

namespace SwfocTrainer.App.V2.ViewModels;

// ============================================================================
// Tab 4 — World State
//
// Planet corruption toggle, diplomacy state setter, story event firing,
// galactic defenses / maphack toggle, dump-state snapshot.
//
// Calls through ICorruptionService, IDiplomacyService, IStoryEventService,
// IMaphackService, ICrashAnalyzerService.
// ============================================================================

public sealed class WorldStateTabViewModel : ObservableBase
{
    private readonly V2Settings _settings;
    private readonly ICorruptionService _corruption;
    private readonly IDiplomacyService _diplomacy;
    private readonly IStoryEventService _storyEvents;
    private readonly IMaphackService _maphack;
    private readonly ICrashAnalyzerService _crashAnalyzer;
    private readonly V2UnitMutationDispatcher _unitMutator;

    private readonly ObservableCollection<string> _output = new();
    private string _planetIdInput = "ALDERAAN";
    private CorruptionType _corruptionType = CorruptionType.Racketeering;
    private string _corruptionLevelInput = "1";
    private string _factionA = "EMPIRE";
    private string _factionB = "REBEL";
    private DiplomacyRelation _diplomacyRelation = DiplomacyRelation.Hostile;
    private string _storyEventId = "STORY_TEST_EVENT";
    private bool _maphackEnabled;

    // 2026-05-05 (iter 201): single shared name input for the 4 iter-159
    // wires (Story_Event / Add_Objective / Play_Music / Play_SFX_Event).
    // Each is a global Lua API call taking one string arg. Default value
    // matches the existing _storyEventId so the operator's "fire something
    // generic" workflow lands a recognizable name in the bridge log.
    private string _storyAudioNameLuaExpr = "STORY_TEST_EVENT";

    // 2026-05-07 (iter 461): SWFOC_TriggerVictory native UX. The bridge
    // wrapper validates input against a 14-name allow-list (kKnownVictoryTypes
    // in lua_bridge.cpp). Default selection mirrors that list ordering so
    // a pristine tab opens with the most-used Galactic_Conquer victory type.
    private string _selectedVictoryType = "Galactic_Conquer";

    public WorldStateTabViewModel(
        V2Settings settings,
        ICorruptionService corruption,
        IDiplomacyService diplomacy,
        IStoryEventService storyEvents,
        IMaphackService maphack,
        ICrashAnalyzerService crashAnalyzer,
        V2FactionRegistry factionRegistry,
        V2UnitMutationDispatcher unitMutator)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(corruption);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(storyEvents);
        ArgumentNullException.ThrowIfNull(maphack);
        ArgumentNullException.ThrowIfNull(crashAnalyzer);
        ArgumentNullException.ThrowIfNull(factionRegistry);
        ArgumentNullException.ThrowIfNull(unitMutator);

        _settings = settings;
        _corruption = corruption;
        _diplomacy = diplomacy;
        _storyEvents = storyEvents;
        _maphack = maphack;
        _crashAnalyzer = crashAnalyzer;
        _unitMutator = unitMutator;

        // 2026-04-27: faction list now sourced from V2FactionRegistry —
        // shared with PlayerState / UnitControl / Galactic / Economy.
        Factions = factionRegistry.Factions;
        CorruptionTypes = new ObservableCollection<CorruptionType>(Enum.GetValues<CorruptionType>());
        DiplomacyRelations = new ObservableCollection<DiplomacyRelation>(Enum.GetValues<DiplomacyRelation>());
        // 2026-04-27: editable Story Event suggestions. Story event IDs are
        // free-form strings the engine looks up via Story_Event(name); this
        // is a curated list of generic test/diagnostic strings the operator
        // might use repeatedly. The ComboBox is IsEditable=True so any mod
        // event name still works — these are autocomplete shortcuts, not
        // a constraint.
        StoryEventSuggestions = new ObservableCollection<string>
        {
            "STORY_TEST_EVENT",
            "EVENT_GAME_WON",
            "EVENT_GAME_LOST",
            "EVENT_TUTORIAL_COMPLETE",
            "EVENT_RESEARCH_COMPLETE",
        };

        // 2026-05-07 (iter 461): SWFOC_TriggerVictory 14-name allow-list.
        // Source-of-truth is lua_bridge.cpp::kKnownVictoryTypes[]; mirror
        // here drives the operator-facing ComboBox. Triple-source consistency
        // audit (iter-459) keeps this list in sync with bridge + simulator.
        VictoryTypes = new ObservableCollection<string>
        {
            "Galactic_Conquer",
            "Galactic_Conquer_Hero",
            "Galactic_Story_Win",
            "Galactic_Story_Lose",
            "Skirmish_Tactical_Win",
            "Skirmish_Tactical_Lose",
            "Skirmish_Galactic_Win",
            "Skirmish_Galactic_Lose",
            "Skirmish_Last_Stand_Win",
            "Skirmish_Last_Stand_Lose",
            "Skirmish_Control_Win",
            "Skirmish_Control_Lose",
            "Sub_Tactical_Story_Win",
            "Sub_Tactical_Story_Lose",
        };

        SetCorruptionCommand = Async(SetCorruptionAsync);
        RemoveCorruptionCommand = Async(RemoveCorruptionAsync);
        SetDiplomacyCommand = Async(SetDiplomacyAsync);
        FireStoryEventCommand = Async(FireStoryEventAsync);
        ToggleMaphackCommand = Async(ToggleMaphackAsync);
        DumpStateCommand = Async(DumpStateAsync);

        // 2026-05-05 (iter 201): iter-159 string-arg global wires.
        StoryEventLuaCommand = Async(StoryEventLuaAsync);
        AddObjectiveLuaCommand = Async(AddObjectiveLuaAsync);
        PlayMusicLuaCommand = Async(PlayMusicLuaAsync);
        PlaySfxEventLuaCommand = Async(PlaySfxEventLuaAsync);

        // 2026-05-05 (iter 202): iter-166 audio + iter-160 story-trigger.
        StopAllMusicLuaCommand = Async(StopAllMusicLuaAsync);
        ResumeModeBasedMusicLuaCommand = Async(ResumeModeBasedMusicLuaAsync);
        StoryEventTriggerLuaCommand = Async(StoryEventTriggerLuaAsync);

        // 2026-05-05 (iter 204): iter-181 SFX VO toggle. **PRESERVES ENGINE
        // TYPO "Reponse"** at every layer (catalog → dispatcher → VM →
        // XAML). The two commands hardcode their bool-string args so
        // operators don't need a separate input field.
        SfxAllowUnitReponseVoOnCommand = Async(() => SfxAllowUnitReponseVoLuaAsync("1"));
        SfxAllowUnitReponseVoOffCommand = Async(() => SfxAllowUnitReponseVoLuaAsync("0"));

        // 2026-05-05 (iter 208): cinematic input-lock pair. Lock_Controls(bool)
        // is iter-160 (1-arg) — exposed as two hardcoded buttons (lock-on/lock-off).
        // Unlock_Controls() is iter-180 (no-arg) — third button. Pair-completion
        // across two iter shapes; cinematic recording workflow.
        LockControlsOnCommand = Async(() => LockControlsLuaAsync("1"));
        LockControlsOffCommand = Async(() => LockControlsLuaAsync("0"));
        UnlockControlsLuaCommand = Async(UnlockControlsLuaAsync);

        // 2026-05-07 (iter 461): SWFOC_TriggerVictory native UX command.
        // Emits `return SWFOC_TriggerVictory('<SelectedVictoryType>')` via
        // the dispatcher. Bridge wrapper currently returns PHASE2_PENDING
        // (iter-450 DORMANT MinHook scaffolding); UX surfaces this via the
        // PHASE 2 PENDING capability badge.
        TriggerVictoryLuaCommand = AsyncDisabled(TriggerVictoryLuaAsync);

        // 2026-04-27 (iter 59): per-button capability metadata. WorldState
        // dispatches via service interfaces (not raw SWFOC_X), but every
        // service ultimately calls one of: engine-native via DoString
        // (Story_Event, Make_Ally / Make_Enemy — both LIVE), or one of
        // SetDiplomacy / GetSelectedUnits primitives (PHASE 2 PENDING).
        // DumpState routes through SWFOC_DumpState (LIVE).
        SetCorruption = new CapabilityAwareAction("Set corruption", "SWFOC_DoString");
        RemoveCorruption = new CapabilityAwareAction("Remove corruption", "SWFOC_DoString");
        SetDiplomacy = new CapabilityAwareAction("Set diplomacy", "SWFOC_DoString");
        FireStoryEvent = new CapabilityAwareAction("Fire story event", "SWFOC_DoString");
        ToggleMaphack = new CapabilityAwareAction("Toggle maphack", "SWFOC_DoString");
        DumpState = new CapabilityAwareAction("Dump state", "SWFOC_DumpState");

        // 2026-05-05 (iter 201): iter-159 capability actions — all LIVE.
        StoryEventLua = new CapabilityAwareAction("Engine Lua: Story_Event", "SWFOC_StoryEventLua");
        AddObjectiveLua = new CapabilityAwareAction("Engine Lua: Add_Objective", "SWFOC_AddObjectiveLua");
        PlayMusicLua = new CapabilityAwareAction("Engine Lua: Play_Music", "SWFOC_PlayMusicLua");
        PlaySfxEventLua = new CapabilityAwareAction("Engine Lua: Play_SFX_Event", "SWFOC_PlaySfxEventLua");

        // 2026-05-05 (iter 202): iter-166 audio + iter-160 story-trigger.
        StopAllMusicLua = new CapabilityAwareAction("Engine Lua: Stop_All_Music", "SWFOC_StopAllMusicLua");
        ResumeModeBasedMusicLua = new CapabilityAwareAction("Engine Lua: Resume_Mode_Based_Music", "SWFOC_ResumeModeBasedMusicLua");
        StoryEventTriggerLua = new CapabilityAwareAction("Engine Lua: Story_Event_Trigger", "SWFOC_StoryEventTriggerLua");

        // 2026-05-05 (iter 204): iter-181 SFX VO toggle. CapabilityAwareAction
        // names + SWFOC catalog name preserve the engine TYPO "Reponse" verbatim.
        SfxAllowUnitReponseVoOn = new CapabilityAwareAction(
            "Engine Lua: SFX VO on (typo: Reponse)", "SWFOC_SFXAllowUnitReponseVoLua");
        SfxAllowUnitReponseVoOff = new CapabilityAwareAction(
            "Engine Lua: SFX VO off (typo: Reponse)", "SWFOC_SFXAllowUnitReponseVoLua");

        // 2026-05-05 (iter 208): cinematic input-lock capability actions.
        LockControlsOn = new CapabilityAwareAction(
            "Engine Lua: Lock_Controls(true)", "SWFOC_LockControlsLua");
        LockControlsOff = new CapabilityAwareAction(
            "Engine Lua: Lock_Controls(false)", "SWFOC_LockControlsLua");
        UnlockControlsLua = new CapabilityAwareAction(
            "Engine Lua: Unlock_Controls", "SWFOC_UnlockControlsLua");

        // 2026-05-07 (iter 461): SWFOC_TriggerVictory PHASE 2 PENDING badge.
        // Catalog status set by iter-450 to Phase2HookPending (DORMANT
        // MinHook scaffolding); UX badge auto-derives from catalog so no
        // hardcoded status here. Activates via iter-450c+ when the
        // capture-on-CTOR hook flips MH_EnableHook.
        TriggerVictoryLua = new CapabilityAwareAction(
            "Trigger victory (engine)", "SWFOC_TriggerVictory");
    }

    public CapabilityAwareAction SetCorruption { get; }
    public CapabilityAwareAction RemoveCorruption { get; }
    public CapabilityAwareAction SetDiplomacy { get; }
    public CapabilityAwareAction FireStoryEvent { get; }
    public CapabilityAwareAction ToggleMaphack { get; }
    public CapabilityAwareAction DumpState { get; }
    // 2026-05-05 (iter 201): iter-159 string-arg global capability actions.
    public CapabilityAwareAction StoryEventLua { get; }
    public CapabilityAwareAction AddObjectiveLua { get; }
    public CapabilityAwareAction PlayMusicLua { get; }
    public CapabilityAwareAction PlaySfxEventLua { get; }
    // 2026-05-05 (iter 202): iter-166 audio + iter-160 story-trigger capability actions.
    public CapabilityAwareAction StopAllMusicLua { get; }
    public CapabilityAwareAction ResumeModeBasedMusicLua { get; }
    public CapabilityAwareAction StoryEventTriggerLua { get; }
    // 2026-05-05 (iter 204): iter-181 SFX VO toggle (TYPO PRESERVED: "Reponse").
    public CapabilityAwareAction SfxAllowUnitReponseVoOn { get; }
    public CapabilityAwareAction SfxAllowUnitReponseVoOff { get; }
    // 2026-05-05 (iter 208): iter-160 + iter-180 cinematic input-lock pair.
    public CapabilityAwareAction LockControlsOn { get; }
    public CapabilityAwareAction LockControlsOff { get; }
    public CapabilityAwareAction UnlockControlsLua { get; }
    // 2026-05-07 (iter 461): iter-450 SWFOC_TriggerVictory PHASE 2 PENDING wire.
    public CapabilityAwareAction TriggerVictoryLua { get; }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        SetCorruption, RemoveCorruption, SetDiplomacy, FireStoryEvent, ToggleMaphack, DumpState,
        StoryEventLua, AddObjectiveLua, PlayMusicLua, PlaySfxEventLua,
        StopAllMusicLua, ResumeModeBasedMusicLua, StoryEventTriggerLua,
        SfxAllowUnitReponseVoOn, SfxAllowUnitReponseVoOff,
        LockControlsOn, LockControlsOff, UnlockControlsLua,
        TriggerVictoryLua,
    };

    public bool HasPhase2PendingAction => AllActions.Any(a => !a.IsAllLive);

    public string Phase2PendingWarning
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "Some actions on this tab are PHASE 2 PENDING; their buttons are disabled "
                + "until a live engine hook exists. Affected: "
                + string.Join("; ", parts);
        }
    }

    public ObservableCollection<string> Factions { get; }
    public ObservableCollection<CorruptionType> CorruptionTypes { get; }
    public ObservableCollection<DiplomacyRelation> DiplomacyRelations { get; }
    public ObservableCollection<string> StoryEventSuggestions { get; }
    // 2026-05-07 (iter 461): SWFOC_TriggerVictory 14-name allow-list source.
    public ObservableCollection<string> VictoryTypes { get; }

    public string SelectedVictoryType
    {
        get => _selectedVictoryType;
        set => SetField(ref _selectedVictoryType, value ?? "Galactic_Conquer");
    }

    public string PlanetIdInput
    {
        get => _planetIdInput;
        set => SetField(ref _planetIdInput, value);
    }

    public CorruptionType SelectedCorruptionType
    {
        get => _corruptionType;
        set => SetField(ref _corruptionType, value);
    }

    public string CorruptionLevelInput
    {
        get => _corruptionLevelInput;
        set => SetField(ref _corruptionLevelInput, value);
    }

    public string FactionA
    {
        get => _factionA;
        set => SetField(ref _factionA, value);
    }

    public string FactionB
    {
        get => _factionB;
        set => SetField(ref _factionB, value);
    }

    public DiplomacyRelation SelectedDiplomacyRelation
    {
        get => _diplomacyRelation;
        set => SetField(ref _diplomacyRelation, value);
    }

    public string StoryEventId
    {
        get => _storyEventId;
        set => SetField(ref _storyEventId, value);
    }

    public bool MaphackEnabled
    {
        get => _maphackEnabled;
        set => SetField(ref _maphackEnabled, value);
    }

    // 2026-05-05 (iter 201): shared name input for the iter-159 Story+Audio
    // GroupBox. Each of the 4 buttons reads this single field — same pattern
    // as iter-200 FOWPlayerLuaExpr in the Galactic tab. Default value
    // intentionally matches the existing _storyEventId so an operator who
    // already typed a story name in the upper half doesn't have to retype.
    public string StoryAudioNameLuaExpr
    {
        get => _storyAudioNameLuaExpr;
        set => SetField(ref _storyAudioNameLuaExpr, value ?? string.Empty);
    }

    public ObservableCollection<string> Output => _output;

    public AsyncRelayCommand SetCorruptionCommand { get; }
    public AsyncRelayCommand RemoveCorruptionCommand { get; }
    public AsyncRelayCommand SetDiplomacyCommand { get; }
    public AsyncRelayCommand FireStoryEventCommand { get; }
    public AsyncRelayCommand ToggleMaphackCommand { get; }
    public AsyncRelayCommand DumpStateCommand { get; }
    // 2026-05-05 (iter 201): iter-159 string-arg global commands.
    public AsyncRelayCommand StoryEventLuaCommand { get; }
    public AsyncRelayCommand AddObjectiveLuaCommand { get; }
    public AsyncRelayCommand PlayMusicLuaCommand { get; }
    public AsyncRelayCommand PlaySfxEventLuaCommand { get; }
    // 2026-05-05 (iter 202): iter-166 audio + iter-160 story-trigger commands.
    public AsyncRelayCommand StopAllMusicLuaCommand { get; }
    public AsyncRelayCommand ResumeModeBasedMusicLuaCommand { get; }
    public AsyncRelayCommand StoryEventTriggerLuaCommand { get; }
    // 2026-05-05 (iter 204): iter-181 SFX VO on/off commands (TYPO PRESERVED).
    public AsyncRelayCommand SfxAllowUnitReponseVoOnCommand { get; }
    public AsyncRelayCommand SfxAllowUnitReponseVoOffCommand { get; }
    // 2026-05-05 (iter 208): iter-160 + iter-180 cinematic input-lock commands.
    public AsyncRelayCommand LockControlsOnCommand { get; }
    public AsyncRelayCommand LockControlsOffCommand { get; }
    public AsyncRelayCommand UnlockControlsLuaCommand { get; }
    // 2026-05-07 (iter 461): iter-450 SWFOC_TriggerVictory native UX command.
    public AsyncRelayCommand TriggerVictoryLuaCommand { get; }

    private AsyncRelayCommand Async(Func<Task> run) =>
        new(run, onError: ex => Append($"[error] {ex.Message}"));

    private AsyncRelayCommand AsyncDisabled(Func<Task> run) =>
        new(run, () => false, ex => Append($"[error] {ex.Message}"));

    private async Task SetCorruptionAsync()
    {
        if (!int.TryParse(_corruptionLevelInput, NumberStyles.Any, CultureInfo.InvariantCulture, out var level))
        {
            Append($"[error] Corruption level must be integer, got '{_corruptionLevelInput}'.");
            return;
        }

        var entry = new CorruptionEntry(_planetIdInput, _corruptionType, level);
        var result = await _corruption
            .SetCorruptionAsync(_settings.ProfileId, entry, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("SetCorruption", result);
    }

    private async Task RemoveCorruptionAsync()
    {
        var result = await _corruption
            .RemoveCorruptionAsync(_settings.ProfileId, _planetIdInput, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("RemoveCorruption", result);
    }

    private async Task SetDiplomacyAsync()
    {
        var state = new DiplomacyState(_factionA, _factionB, _diplomacyRelation);
        var result = await _diplomacy
            .SetRelationAsync(_settings.ProfileId, state, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("SetDiplomacy", result);
    }

    private async Task FireStoryEventAsync()
    {
        var result = await _storyEvents
            .FireEventAsync(_settings.ProfileId, _storyEventId, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("FireStoryEvent", result);
    }

    // 2026-05-05 (iter 201): iter-159 string-arg global handlers. Each
    // composes a single-arg engine-Lua-API call via the dispatcher and
    // appends the bridge round-trip result to the output log. Distinct
    // from FireStoryEventAsync above — that one routes through
    // IStoryEventService (catalog/profile-mediated). These four hit the
    // engine Lua API directly.
    private async Task StoryEventLuaAsync()
    {
        var result = await _unitMutator
            .StoryEventLuaAsync(_storyAudioNameLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendBridgeResult("Engine Lua: Story_Event", result);
    }

    private async Task AddObjectiveLuaAsync()
    {
        var result = await _unitMutator
            .AddObjectiveLuaAsync(_storyAudioNameLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendBridgeResult("Engine Lua: Add_Objective", result);
    }

    private async Task PlayMusicLuaAsync()
    {
        var result = await _unitMutator
            .PlayMusicLuaAsync(_storyAudioNameLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendBridgeResult("Engine Lua: Play_Music", result);
    }

    private async Task PlaySfxEventLuaAsync()
    {
        var result = await _unitMutator
            .PlaySfxEventLuaAsync(_storyAudioNameLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendBridgeResult("Engine Lua: Play_SFX_Event", result);
    }

    // 2026-05-05 (iter 202): iter-166 audio + iter-160 story-trigger handlers.
    // Stop_All_Music + Resume_Mode_Based_Music are no-arg; Story_Event_Trigger
    // takes the shared StoryAudioNameLuaExpr (same field as the iter-201 buttons).
    private async Task StopAllMusicLuaAsync()
    {
        var result = await _unitMutator
            .StopAllMusicLuaAsync(CancellationToken.None)
            .ConfigureAwait(true);
        AppendBridgeResultNoArg("Engine Lua: Stop_All_Music", result);
    }

    private async Task ResumeModeBasedMusicLuaAsync()
    {
        var result = await _unitMutator
            .ResumeModeBasedMusicLuaAsync(CancellationToken.None)
            .ConfigureAwait(true);
        AppendBridgeResultNoArg("Engine Lua: Resume_Mode_Based_Music", result);
    }

    private async Task StoryEventTriggerLuaAsync()
    {
        var result = await _unitMutator
            .StoryEventTriggerLuaAsync(_storyAudioNameLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendBridgeResult("Engine Lua: Story_Event_Trigger", result);
    }

    // 2026-05-05 (iter 204): iter-181 SFX VO toggle handler. The arg is
    // hardcoded to "1" (on) or "0" (off) by the wiring above — no input
    // field. Per docs section 6, the engine accepts integer-as-bool here
    // (1/0) instead of true/false because the underlying Lua bridge passes
    // the argument as a raw string.
    private async Task SfxAllowUnitReponseVoLuaAsync(string boolStringArg)
    {
        var result = await _unitMutator
            .SfxAllowUnitReponseVoLuaAsync(boolStringArg, CancellationToken.None)
            .ConfigureAwait(true);
        var label = boolStringArg == "1"
            ? "Engine Lua: SFXManager.Allow_Unit_Reponse_VO(true)"
            : "Engine Lua: SFXManager.Allow_Unit_Reponse_VO(false)";
        Append(result.Succeeded
            ? $"[ok] {label} -> {result.Response}"
            : $"[err] {label} -> {result.ErrorMessage}");
    }

    // 2026-05-05 (iter 208): cinematic input-lock pair handlers. Lock_Controls
    // takes hardcoded bool string ("1" / "0"); Unlock_Controls is no-arg.
    private async Task LockControlsLuaAsync(string boolStringArg)
    {
        var result = await _unitMutator
            .LockControlsLuaAsync(boolStringArg, CancellationToken.None)
            .ConfigureAwait(true);
        var label = boolStringArg == "1"
            ? "Engine Lua: Lock_Controls(true)"
            : "Engine Lua: Lock_Controls(false)";
        Append(result.Succeeded
            ? $"[ok] {label} -> {result.Response}"
            : $"[err] {label} -> {result.ErrorMessage}");
    }

    private async Task UnlockControlsLuaAsync()
    {
        var result = await _unitMutator
            .UnlockControlsLuaAsync(CancellationToken.None)
            .ConfigureAwait(true);
        AppendBridgeResultNoArg("Engine Lua: Unlock_Controls", result);
    }

    // 2026-05-07 (iter 461): SWFOC_TriggerVictory native UX handler.
    // Sends the SelectedVictoryType (14-name allow-list source) through the
    // dispatcher; bridge wrapper validates and currently returns
    // PHASE2_PENDING per iter-450 DORMANT MinHook scaffolding. Operator
    // sees [ok] PHASE2_PENDING to confirm the wire reached the bridge but
    // engine state remains unchanged until iter-450c+ activates the hook.
    private async Task TriggerVictoryLuaAsync()
    {
        var result = await _unitMutator
            .TriggerVictoryLuaAsync(_selectedVictoryType, CancellationToken.None)
            .ConfigureAwait(true);
        var label = $"Engine: SWFOC_TriggerVictory('{_selectedVictoryType}')";
        Append(result.Succeeded
            ? $"[ok] {label} -> {result.Response}"
            : $"[err] {label} -> {result.ErrorMessage}");
    }

    private void AppendBridgeResult(string label, BridgeRoundTripResult result)
    {
        Append(result.Succeeded
            ? $"[ok] {label}('{_storyAudioNameLuaExpr}') -> {result.Response}"
            : $"[err] {label}('{_storyAudioNameLuaExpr}') -> {result.ErrorMessage}");
    }

    private void AppendBridgeResultNoArg(string label, BridgeRoundTripResult result)
    {
        Append(result.Succeeded
            ? $"[ok] {label}() -> {result.Response}"
            : $"[err] {label}() -> {result.ErrorMessage}");
    }

    private async Task ToggleMaphackAsync()
    {
        var result = _maphackEnabled
            ? await _maphack.UndoRevealAsync(_settings.ProfileId, CancellationToken.None).ConfigureAwait(true)
            : await _maphack.RevealAllAsync(_settings.ProfileId, CancellationToken.None).ConfigureAwait(true);
        if (result.Succeeded)
        {
            MaphackEnabled = !_maphackEnabled;
        }

        AppendResult(_maphackEnabled ? "Maphack.Reveal" : "Maphack.Undo", result);
    }

    private async Task DumpStateAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Dump SWFOC state",
            FileName = $"swfoc_state_{DateTime.Now:yyyyMMdd_HHmmss}.snap",
            Filter = "Snapshot files (*.snap;*.swfocsnap)|*.snap;*.swfocsnap|All files (*.*)|*.*"
        };

        var selected = dialog.ShowDialog();
        if (selected != true)
        {
            return;
        }

        var result = await _crashAnalyzer
            .CaptureSnapshotAsync(_settings.ProfileId, dialog.FileName, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("DumpState", result);
    }

    private void AppendResult(string label, ActionExecutionResult result)
    {
        var prefix = result.Succeeded ? "[ok]" : "[err]";
        Append($"{prefix} {label} -> {result.Message}");
    }

    private void Append(string line)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _output.Add($"{timestamp} {line}");
        while (_output.Count > 200)
        {
            _output.RemoveAt(0);
        }
    }
}
