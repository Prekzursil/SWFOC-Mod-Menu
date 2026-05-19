using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Camera and Debug tab) — INPC wrapper around
/// CameraDebugTabState. Free-cam toggle (with cleanup), apply-pos / apply-zoom
/// commands, and a raw-Lua escape hatch that always emits Warning severity.
/// </summary>
public sealed class CameraDebugTabViewModel : ObservableBase
{
    private readonly CameraDebugTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly FeatureToggleCoordinator _toggles;

    private float _camX;
    private float _camY;
    private float _camZ;
    private float _camRot;
    private float _camZoom = 1.0f;
    private string _rawLuaCommand = string.Empty;
    private string _scrollTargetExpr = string.Empty;  // iter 107
    private string _lastStatus = "(idle)";

    public CameraDebugTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        _toggles = new FeatureToggleCoordinator(_sink);
        var dispatcher = new BridgeCameraDebugDispatcher(bridge);
        _state = new CameraDebugTabState(dispatcher, _sink, _toggles);

        ToggleFreeCamCommand = new AsyncRelayCommand(ToggleFreeCamCore, () => false, HandleError);
        SetCameraPosCommand = new AsyncRelayCommand(SetCameraPosCore, onError: HandleError);
        // 2026-05-06 (iter 239): GetCameraPos LIVE pair-flip with iter-237
        // SetCameraPos. Reads the current engine camera position and surfaces
        // it via LastStatus. Closes A1.x SetCameraPos arc at editor-UX level.
        GetCameraPosCommand = new AsyncRelayCommand(GetCameraPosCore, onError: HandleError);
        SetCameraZoomCommand = new AsyncRelayCommand(SetCameraZoomCore, onError: HandleError);
        SubmitRawCommand = new AsyncRelayCommand(SubmitRawCore, onError: HandleError);
        // 2026-04-28 (iter 107): LIVE camera target via engine Lua API.
        ScrollCameraToTargetCommand = new AsyncRelayCommand(
            ScrollCameraToTargetCore, onError: HandleError);

        // 2026-04-29 (iter 148): camera arc native UX commands.
        CameraFollowCommand = new AsyncRelayCommand(CameraFollowCore, onError: HandleError);
        RotateCameraToCommand = new AsyncRelayCommand(RotateCameraToCore, onError: HandleError);
        StartCinematicCameraCommand = new AsyncRelayCommand(StartCinematicCameraCore, onError: HandleError);
        EndCinematicCameraCommand = new AsyncRelayCommand(EndCinematicCameraCore, onError: HandleError);
        SetCinematicCameraKeyCommand = new AsyncRelayCommand(SetCinematicCameraKeyCore, onError: HandleError);
        TransitionCinematicCameraKeyCommand = new AsyncRelayCommand(TransitionCinematicCameraKeyCore, onError: HandleError);

        // 2026-05-05 (iter 192): additional camera primitive native UX (queued
        // since iter 162/165 shipped LIVE). All take a single string arg.
        ZoomCameraCommand = new AsyncRelayCommand(ZoomCameraCore, onError: HandleError);
        FadeScreenOutCommand = new AsyncRelayCommand(FadeScreenOutCore, onError: HandleError);
        RotateCameraByCommand = new AsyncRelayCommand(RotateCameraByCore, onError: HandleError);
        PointCameraAtCommand = new AsyncRelayCommand(PointCameraAtCore, onError: HandleError);

        // 2026-04-27 (iter 58): per-button capability metadata. FreeCam +
        // SetCameraPos are PHASE 2 PENDING (Phase-1-mirror only — no IDA
        // pin yet). Zoom routes through SWFOC_DoString (LIVE) — same with
        // raw Lua submit (raw escape hatch for any engine global).
        ToggleFreeCam = new CapabilityAwareAction("Toggle free cam", "SWFOC_FreeCam");
        // 2026-05-06 (iter 237/239): SetCameraPos flipped to LIVE via direct
        // call to CameraClass::SetTransformMatrix @ 0x261BD0 (was Phase-1
        // mirror in iter 107; iter 237 RE design picked the inline-matrix
        // setter). Tactical-only — galactic mode returns ERR.
        SetCameraPos = new CapabilityAwareAction("Set camera pos (LIVE)", "SWFOC_SetCameraPos");
        // 2026-05-06 (iter 237/239): NEW LIVE pair-flip via direct call to
        // CameraClass::GetPosition @ 0x261A40. Returns current "X,Y,Z" string.
        GetCameraPos = new CapabilityAwareAction("Read camera pos (LIVE)", "SWFOC_GetCameraPos");
        SetCameraZoom = new CapabilityAwareAction("Set camera zoom", "SWFOC_DoString");
        SubmitRaw = new CapabilityAwareAction("Submit raw Lua", "SWFOC_DoString");
        // 2026-04-28 (iter 107) LIVE: Scroll camera to a planet/unit/object.
        ScrollCameraToTarget = new CapabilityAwareAction(
            "Scroll camera to target", "SWFOC_ScrollCameraToTarget");
        // 2026-04-29 (iter 148): camera arc LIVE actions.
        CameraFollow = new CapabilityAwareAction("Follow target", "SWFOC_CameraFollow");
        RotateCameraTo = new CapabilityAwareAction("Rotate to face target", "SWFOC_RotateCameraTo");
        StartCinematicCamera = new CapabilityAwareAction("Start cinematic mode", "SWFOC_StartCinematicCamera");
        EndCinematicCamera = new CapabilityAwareAction("End cinematic mode", "SWFOC_EndCinematicCamera");
        SetCinematicCameraKey = new CapabilityAwareAction("Set cinematic key", "SWFOC_SetCinematicCameraKey");
        TransitionCinematicCameraKey = new CapabilityAwareAction("Transition cinematic key", "SWFOC_TransitionCinematicCameraKey");

        // 2026-05-05 (iter 192): per-button capability metadata for the new primitives.
        ZoomCamera = new CapabilityAwareAction("Zoom camera", "SWFOC_ZoomCameraLua");
        FadeScreenOut = new CapabilityAwareAction("Fade screen out", "SWFOC_FadeScreenOutLua");
        RotateCameraBy = new CapabilityAwareAction("Rotate camera by", "SWFOC_RotateCameraByLua");
        PointCameraAt = new CapabilityAwareAction("Point camera at", "SWFOC_PointCameraAtLua");
    }

    public CapabilityAwareAction ToggleFreeCam { get; }
    public CapabilityAwareAction SetCameraPos { get; }
    /// <summary>2026-05-06 (iter 239): NEW LIVE pair-flip read sibling to SetCameraPos.</summary>
    public CapabilityAwareAction GetCameraPos { get; }
    public CapabilityAwareAction SetCameraZoom { get; }
    public CapabilityAwareAction SubmitRaw { get; }
    public CapabilityAwareAction ScrollCameraToTarget { get; }

    // 2026-04-29 (iter 148) — camera arc actions.
    public CapabilityAwareAction CameraFollow { get; }
    public CapabilityAwareAction RotateCameraTo { get; }
    public CapabilityAwareAction StartCinematicCamera { get; }
    public CapabilityAwareAction EndCinematicCamera { get; }
    public CapabilityAwareAction SetCinematicCameraKey { get; }
    public CapabilityAwareAction TransitionCinematicCameraKey { get; }

    // 2026-05-05 (iter 192) — additional camera primitive actions.
    public CapabilityAwareAction ZoomCamera { get; }
    public CapabilityAwareAction FadeScreenOut { get; }
    public CapabilityAwareAction RotateCameraBy { get; }
    public CapabilityAwareAction PointCameraAt { get; }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        ToggleFreeCam, SetCameraPos, ScrollCameraToTarget, SetCameraZoom, SubmitRaw,
        // iter 148: camera arc actions
        CameraFollow, RotateCameraTo,
        StartCinematicCamera, EndCinematicCamera,
        SetCinematicCameraKey, TransitionCinematicCameraKey,
        // iter 192: extra camera primitives
        ZoomCamera, FadeScreenOut, RotateCameraBy, PointCameraAt,
        // iter 239: NEW LIVE GetCameraPos pair-flip with iter-107/237 SetCameraPos.
        // Closes A1.x SetCameraPos arc at editor-UX level.
        GetCameraPos,
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

    public float CamX
    {
        get => _camX;
        set { if (SetField(ref _camX, value)) _state.CamX = value; }
    }

    public float CamY
    {
        get => _camY;
        set { if (SetField(ref _camY, value)) _state.CamY = value; }
    }

    public float CamZ
    {
        get => _camZ;
        set { if (SetField(ref _camZ, value)) _state.CamZ = value; }
    }

    public float CamRot
    {
        get => _camRot;
        set { if (SetField(ref _camRot, value)) _state.CamRot = value; }
    }

    public float CamZoom
    {
        get => _camZoom;
        set { if (SetField(ref _camZoom, value)) _state.CamZoom = value; }
    }

    public string RawLuaCommand
    {
        get => _rawLuaCommand;
        set { if (SetField(ref _rawLuaCommand, value ?? string.Empty)) _state.RawLuaCommand = _rawLuaCommand; }
    }

    /// <summary>
    /// 2026-04-28 (iter 107): Lua expression for the engine's
    /// <c>Scroll_Camera_To</c> API. Operator types e.g.
    /// <c>Find_Planet("Yavin")</c> or
    /// <c>Find_First_Object("Empire_AT_AT")</c>.
    /// </summary>
    public string ScrollTargetExpr
    {
        get => _scrollTargetExpr;
        set
        {
            if (SetField(ref _scrollTargetExpr, value ?? string.Empty))
                _state.ScrollTargetExpr = _scrollTargetExpr;
        }
    }

    private string _cameraTargetExpr = string.Empty;
    private string _cinematicKeyArgsExpr = string.Empty;
    private string _cameraExtraArg = string.Empty;  // iter 192

    /// <summary>
    /// 2026-04-29 (iter 148): shared TextBox backing the iter-143 Follow
    /// + iter-144 Rotate buttons. Operator pastes a Lua object/find expr.
    /// </summary>
    public string CameraTargetExpr
    {
        get => _cameraTargetExpr;
        set
        {
            if (SetField(ref _cameraTargetExpr, value ?? string.Empty))
                _state.CameraTargetExpr = _cameraTargetExpr;
        }
    }

    /// <summary>
    /// 2026-04-29 (iter 148): shared TextBox backing the iter-145 SetKey
    /// + TransitionKey buttons. Operator pastes the args expression
    /// (e.g. '1, Find_Planet("Yavin"), 5.0').
    /// </summary>
    public string CinematicKeyArgsExpr
    {
        get => _cinematicKeyArgsExpr;
        set
        {
            if (SetField(ref _cinematicKeyArgsExpr, value ?? string.Empty))
                _state.CinematicKeyArgsExpr = _cinematicKeyArgsExpr;
        }
    }

    /// <summary>
    /// 2026-05-05 (iter 192): single-arg field shared across the iter-162/165
    /// camera primitive buttons (Zoom_Camera time, Fade_Screen_Out time,
    /// Rotate_Camera_By degrees, Point_Camera_At target Lua expression).
    /// </summary>
    public string CameraExtraArg
    {
        get => _cameraExtraArg;
        set
        {
            if (SetField(ref _cameraExtraArg, value ?? string.Empty))
                _state.CameraExtraArg = _cameraExtraArg;
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge(
        "SWFOC_FreeCam", "SWFOC_SetCameraPos",
        "SWFOC_ScrollCameraToTarget", "SWFOC_DoString");

    public bool IsFreeCamEnabled => _toggles.IsEnabled("free_cam");

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand ToggleFreeCamCommand { get; }
    public ICommand SetCameraPosCommand { get; }
    /// <summary>2026-05-06 (iter 239) — LIVE read-back of current camera X/Y/Z via SWFOC_GetCameraPos.</summary>
    public ICommand GetCameraPosCommand { get; }
    public ICommand SetCameraZoomCommand { get; }
    public ICommand SubmitRawCommand { get; }
    /// <summary>
    /// 2026-04-28 (iter 107) — LIVE camera target. Calls
    /// <c>SWFOC_ScrollCameraToTarget(<see cref="ScrollTargetExpr"/>)</c>.
    /// </summary>
    public ICommand ScrollCameraToTargetCommand { get; }

    // 2026-04-29 (iter 148) — camera arc commands.
    public ICommand CameraFollowCommand { get; }
    public ICommand RotateCameraToCommand { get; }
    public ICommand StartCinematicCameraCommand { get; }
    public ICommand EndCinematicCameraCommand { get; }
    public ICommand SetCinematicCameraKeyCommand { get; }
    public ICommand TransitionCinematicCameraKeyCommand { get; }

    // 2026-05-05 (iter 192) — additional camera primitive commands.
    public ICommand ZoomCameraCommand { get; }
    public ICommand FadeScreenOutCommand { get; }
    public ICommand RotateCameraByCommand { get; }
    public ICommand PointCameraAtCommand { get; }

    private async Task ToggleFreeCamCore()
    {
        var next = !IsFreeCamEnabled;
        ApplyFeedback(await _state.ToggleFreeCamAsync(next));
        OnPropertyChanged(nameof(IsFreeCamEnabled));
    }

    private async Task SetCameraPosCore() => ApplyFeedback(await _state.SetCameraPosAsync());
    // 2026-05-06 (iter 239) — read-back current camera position. UxFeedback.Message
    // shows the engine response ("X,Y,Z") so the operator sees the live camera state.
    private async Task GetCameraPosCore() => ApplyFeedback(await _state.GetCameraPosAsync());
    private async Task SetCameraZoomCore() => ApplyFeedback(await _state.SetCameraZoomAsync());
    private async Task SubmitRawCore() => ApplyFeedback(await _state.SubmitRawCommandAsync());
    private async Task ScrollCameraToTargetCore() =>
        ApplyFeedback(await _state.ScrollCameraToTargetAsync());

    // 2026-04-29 (iter 148) — camera arc command handlers.
    private async Task CameraFollowCore() =>
        ApplyFeedback(await _state.CameraFollowAsync());
    private async Task RotateCameraToCore() =>
        ApplyFeedback(await _state.RotateCameraToAsync());
    private async Task StartCinematicCameraCore() =>
        ApplyFeedback(await _state.StartCinematicCameraAsync());
    private async Task EndCinematicCameraCore() =>
        ApplyFeedback(await _state.EndCinematicCameraAsync());
    private async Task SetCinematicCameraKeyCore() =>
        ApplyFeedback(await _state.SetCinematicCameraKeyAsync());
    private async Task TransitionCinematicCameraKeyCore() =>
        ApplyFeedback(await _state.TransitionCinematicCameraKeyAsync());

    // 2026-05-05 (iter 192) — additional camera primitive command handlers.
    private async Task ZoomCameraCore() =>
        ApplyFeedback(await _state.ZoomCameraAsync());
    private async Task FadeScreenOutCore() =>
        ApplyFeedback(await _state.FadeScreenOutAsync());
    private async Task RotateCameraByCore() =>
        ApplyFeedback(await _state.RotateCameraByAsync());
    private async Task PointCameraAtCore() =>
        ApplyFeedback(await _state.PointCameraAtAsync());

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
