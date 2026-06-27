using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab 10 (Camera and Debug). Task #154 — camera pos/rot/zoom,
/// free-cam toggle, teleport, cam speed, bridge status, advanced
/// raw-command escape hatch.
/// </summary>
public sealed class CameraDebugTabState
{
    private readonly ICameraDebugDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly FeatureToggleCoordinator _toggles;

    public CameraDebugTabState(ICameraDebugDispatcher dispatcher,
                                IUxFeedbackSink feedback,
                                FeatureToggleCoordinator toggles)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(toggles);
        _dispatcher = dispatcher;
        _feedback = feedback;
        _toggles = toggles;
    }

    public float CamX { get; set; }
    public float CamY { get; set; }
    public float CamZ { get; set; }
    public float CamRot { get; set; }
    public float CamZoom { get; set; } = 1.0f;
    public string RawLuaCommand { get; set; } = string.Empty;

    /// <summary>
    /// 2026-04-28 (iter 107): Lua expression for the LIVE
    /// <c>SWFOC_ScrollCameraToTarget</c> wire. Spliced verbatim into the
    /// engine's <c>Scroll_Camera_To(&lt;expr&gt;)</c> call. Operator drives
    /// it with planet handles, Find_Object lookups, or position userdata.
    /// </summary>
    public string ScrollTargetExpr { get; set; } = string.Empty;

    // 2026-04-29 (iter 148): camera arc native UX state. Two TextBoxes
    // back the 6 new buttons: TargetExpr (used by Follow + Rotate),
    // CinematicKeyArgsExpr (used by SetKey + TransitionKey).
    public string CameraTargetExpr { get; set; } = string.Empty;
    public string CinematicKeyArgsExpr { get; set; } = string.Empty;
    /// <summary>
    /// 2026-05-05 (iter 192): single-arg field shared across the iter-162/165
    /// camera primitive buttons. Operator pastes a number for time/degrees,
    /// or a Lua expression for target. Each VM command validates emptiness
    /// before dispatching.
    /// </summary>
    public string CameraExtraArg { get; set; } = string.Empty;

    public Task<UxFeedback> ToggleFreeCamAsync(bool enable, CancellationToken ct = default)
    {
        return _toggles.ToggleAsync("free_cam", enable,
            action: async cancel =>
            {
                var ok = await _dispatcher.SetFreeCamAsync(enable, cancel);
                return ok
                    ? UxFeedback.Success("free_cam",
                        enable ? "free-cam unlocked" : "engine camera restored", "free_cam")
                    : UxFeedback.Error("free_cam", "bridge rejected", "free_cam");
            },
            disableAction: enable
                ? async cancel =>
                {
                    var ok = await _dispatcher.SetFreeCamAsync(false, cancel);
                    return ok
                        ? UxFeedback.Info("free_cam", "engine camera restored (cleanup)", "free_cam")
                        : UxFeedback.Warning("free_cam", "cleanup failed", "free_cam");
                }
        : null,
            cancellationToken: ct);
    }

    public async Task<UxFeedback> SetCameraPosAsync(CancellationToken ct = default)
    {
        var ok = await _dispatcher.SetCameraPosAsync(CamX, CamY, CamZ, ct);
        return Emit(ok
            ? UxFeedback.Success("set_camera_pos",
                $"({CamX:0.0},{CamY:0.0},{CamZ:0.0})", "set_camera_pos")
            : UxFeedback.Error("set_camera_pos", "bridge rejected", "set_camera_pos"));
    }

    /// <summary>
    /// 2026-05-06 (iter 239): LIVE read-back of current camera position via
    /// SWFOC_GetCameraPos (iter-237 wired to CameraClass::GetPosition @
    /// 0x261A40). Returns the bridge response string ("X,Y,Z") for display.
    /// Falls back to a sentinel string when the bridge returns null.
    /// </summary>
    public async Task<UxFeedback> GetCameraPosAsync(CancellationToken ct = default)
    {
        var resp = await _dispatcher.GetCameraPosAsync(ct);
        if (resp is null)
        {
            return Emit(UxFeedback.Error("get_camera_pos",
                "bridge rejected (no GetCameraPos)", "get_camera_pos"));
        }
        return Emit(UxFeedback.Success("get_camera_pos",
            $"current pos: {resp}", "get_camera_pos"));
    }

    /// <summary>
    /// 2026-04-28 (iter 107) — LIVE camera target via engine
    /// <c>Scroll_Camera_To</c> Lua API. Empty <see cref="ScrollTargetExpr"/>
    /// is rejected with an Error UxFeedback; otherwise the dispatcher
    /// forwards the expression to the bridge's
    /// <c>SWFOC_ScrollCameraToTarget</c>. Bridge returns "OK:" on engine
    /// success, "ERR:" on engine error — we surface both honestly.
    /// </summary>
    public async Task<UxFeedback> ScrollCameraToTargetAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ScrollTargetExpr))
        {
            return Emit(UxFeedback.Error("scroll_camera_to_target",
                "target expression required (e.g. Find_Planet(\"Yavin\"))",
                "scroll_camera_to_target"));
        }
        var ok = await _dispatcher.ScrollCameraToTargetAsync(ScrollTargetExpr, ct);
        return Emit(ok
            ? UxFeedback.Success("scroll_camera_to_target",
                $"target: {Truncate(ScrollTargetExpr, 64)}",
                "scroll_camera_to_target")
            : UxFeedback.Error("scroll_camera_to_target",
                "bridge rejected (engine returned ERR or pipe failed)",
                "scroll_camera_to_target"));
    }

    // 2026-04-29 (iter 148) — camera arc native UX state methods.
    // Each calls the dispatcher's iter-143-145 LIVE wire and emits a
    // success/error UxFeedback mirroring iter 107's pattern.

    public async Task<UxFeedback> CameraFollowAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CameraTargetExpr))
            return Emit(UxFeedback.Error("camera_follow",
                "target expression required (e.g. Find_First_Object(\"Empire_AT_AT\"))",
                "camera_follow"));
        var ok = await _dispatcher.CameraFollowAsync(CameraTargetExpr, ct);
        return Emit(ok
            ? UxFeedback.Success("camera_follow",
                $"following: {Truncate(CameraTargetExpr, 64)}", "camera_follow")
            : UxFeedback.Error("camera_follow", "bridge rejected (engine returned ERR)", "camera_follow"));
    }

    public async Task<UxFeedback> RotateCameraToAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CameraTargetExpr))
            return Emit(UxFeedback.Error("rotate_camera_to",
                "target expression required", "rotate_camera_to"));
        var ok = await _dispatcher.RotateCameraToAsync(CameraTargetExpr, ct);
        return Emit(ok
            ? UxFeedback.Success("rotate_camera_to",
                $"facing: {Truncate(CameraTargetExpr, 64)}", "rotate_camera_to")
            : UxFeedback.Error("rotate_camera_to", "bridge rejected", "rotate_camera_to"));
    }

    public async Task<UxFeedback> StartCinematicCameraAsync(CancellationToken ct = default)
    {
        var ok = await _dispatcher.StartCinematicCameraAsync(ct);
        return Emit(ok
            ? UxFeedback.Success("start_cinematic_camera", "cinematic mode entered", "start_cinematic_camera")
            : UxFeedback.Error("start_cinematic_camera", "bridge rejected", "start_cinematic_camera"));
    }

    public async Task<UxFeedback> EndCinematicCameraAsync(CancellationToken ct = default)
    {
        var ok = await _dispatcher.EndCinematicCameraAsync(ct);
        return Emit(ok
            ? UxFeedback.Success("end_cinematic_camera", "cinematic mode exited", "end_cinematic_camera")
            : UxFeedback.Error("end_cinematic_camera", "bridge rejected", "end_cinematic_camera"));
    }

    public async Task<UxFeedback> SetCinematicCameraKeyAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CinematicKeyArgsExpr))
            return Emit(UxFeedback.Error("set_cinematic_camera_key",
                "args expression required (e.g. '1, Find_Planet(\"Yavin\"), 5.0')",
                "set_cinematic_camera_key"));
        var ok = await _dispatcher.SetCinematicCameraKeyAsync(CinematicKeyArgsExpr, ct);
        return Emit(ok
            ? UxFeedback.Success("set_cinematic_camera_key",
                $"key: {Truncate(CinematicKeyArgsExpr, 64)}", "set_cinematic_camera_key")
            : UxFeedback.Error("set_cinematic_camera_key", "bridge rejected", "set_cinematic_camera_key"));
    }

    public async Task<UxFeedback> TransitionCinematicCameraKeyAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CinematicKeyArgsExpr))
            return Emit(UxFeedback.Error("transition_cinematic_camera_key",
                "args expression required", "transition_cinematic_camera_key"));
        var ok = await _dispatcher.TransitionCinematicCameraKeyAsync(CinematicKeyArgsExpr, ct);
        return Emit(ok
            ? UxFeedback.Success("transition_cinematic_camera_key",
                $"transition: {Truncate(CinematicKeyArgsExpr, 64)}", "transition_cinematic_camera_key")
            : UxFeedback.Error("transition_cinematic_camera_key", "bridge rejected", "transition_cinematic_camera_key"));
    }

    // 2026-05-05 (iter 192) — additional camera primitives. Each takes the
    // CameraExtraArg (single string arg field shared across iter-192 buttons —
    // operators paste a number for time/degrees, or a Lua expr for target).

    public async Task<UxFeedback> ZoomCameraAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CameraExtraArg))
            return Emit(UxFeedback.Error("zoom_camera",
                "time argument required (e.g. 2.0 for 2 seconds)", "zoom_camera"));
        var ok = await _dispatcher.ZoomCameraAsync(CameraExtraArg, ct);
        return Emit(ok
            ? UxFeedback.Success("zoom_camera",
                $"zoom over {Truncate(CameraExtraArg, 32)}s", "zoom_camera")
            : UxFeedback.Error("zoom_camera", "bridge rejected", "zoom_camera"));
    }

    public async Task<UxFeedback> FadeScreenOutAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CameraExtraArg))
            return Emit(UxFeedback.Error("fade_screen_out",
                "time argument required (e.g. 1.5 for 1.5-sec fade)", "fade_screen_out"));
        var ok = await _dispatcher.FadeScreenOutAsync(CameraExtraArg, ct);
        return Emit(ok
            ? UxFeedback.Success("fade_screen_out",
                $"fade over {Truncate(CameraExtraArg, 32)}s", "fade_screen_out")
            : UxFeedback.Error("fade_screen_out", "bridge rejected", "fade_screen_out"));
    }

    public async Task<UxFeedback> RotateCameraByAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CameraExtraArg))
            return Emit(UxFeedback.Error("rotate_camera_by",
                "degrees argument required (e.g. 45 for 45-degree rotation)", "rotate_camera_by"));
        var ok = await _dispatcher.RotateCameraByAsync(CameraExtraArg, ct);
        return Emit(ok
            ? UxFeedback.Success("rotate_camera_by",
                $"rotated by {Truncate(CameraExtraArg, 32)}°", "rotate_camera_by")
            : UxFeedback.Error("rotate_camera_by", "bridge rejected", "rotate_camera_by"));
    }

    public async Task<UxFeedback> PointCameraAtAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(CameraExtraArg))
            return Emit(UxFeedback.Error("point_camera_at",
                "target Lua expression required (e.g. Find_Planet(\"Yavin\"))", "point_camera_at"));
        var ok = await _dispatcher.PointCameraAtAsync(CameraExtraArg, ct);
        return Emit(ok
            ? UxFeedback.Success("point_camera_at",
                $"pointing at: {Truncate(CameraExtraArg, 64)}", "point_camera_at")
            : UxFeedback.Error("point_camera_at", "bridge rejected", "point_camera_at"));
    }

    public async Task<UxFeedback> SetCameraZoomAsync(CancellationToken ct = default)
    {
        if (CamZoom <= 0)
        {
            return Emit(UxFeedback.Error("set_camera_zoom",
                $"zoom must be > 0, got {CamZoom}", "set_camera_zoom"));
        }
        var ok = await _dispatcher.SetCameraZoomAsync(CamZoom, ct);
        return Emit(ok
            ? UxFeedback.Success("set_camera_zoom", $"{CamZoom:0.00}×", "set_camera_zoom")
            : UxFeedback.Error("set_camera_zoom", "bridge rejected", "set_camera_zoom"));
    }

    /// <summary>
    /// Submit a raw Lua command directly to the bridge. THE ESCAPE
    /// HATCH for the modder; carries a Warning severity even on
    /// success because the editor can't validate what the operator
    /// just sent.
    /// </summary>
    public async Task<UxFeedback> SubmitRawCommandAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(RawLuaCommand))
        {
            return Emit(UxFeedback.Error("raw_lua",
                "no command entered", "raw_lua"));
        }
        var response = await _dispatcher.ExecuteRawLuaAsync(RawLuaCommand, ct);
        var success = response is not null && !response.StartsWith("ERR", StringComparison.OrdinalIgnoreCase);
        return Emit(success
            ? UxFeedback.Warning("raw_lua",
                $"sent: {Truncate(RawLuaCommand, 64)} | recv: {Truncate(response ?? string.Empty, 64)}",
                "raw_lua")
            : UxFeedback.Error("raw_lua",
                $"bridge response: {response ?? "<null>"}", "raw_lua"));
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface ICameraDebugDispatcher
{
    Task<bool> SetFreeCamAsync(bool enable, CancellationToken ct);
    Task<bool> SetCameraPosAsync(float x, float y, float z, CancellationToken ct);
    Task<bool> SetCameraZoomAsync(float zoom, CancellationToken ct);
    Task<string?> ExecuteRawLuaAsync(string lua, CancellationToken ct);

    /// <summary>
    /// 2026-05-06 (iter 239) — LIVE camera read via SWFOC_GetCameraPos
    /// (iter-237 wired to CameraClass::GetPosition @ 0x261A40). Default
    /// impl returns null to keep older mocks compiling.
    /// </summary>
    Task<string?> GetCameraPosAsync(CancellationToken ct)
        => Task.FromResult<string?>(null);

    /// <summary>
    /// 2026-04-28 (iter 107) — LIVE camera target via engine
    /// <c>Scroll_Camera_To</c> Lua API. Default impl returns false to keep
    /// older mocks compiling; the real <c>BridgeCameraDebugDispatcher</c>
    /// in App.V2.Infrastructure overrides with the DoString dispatch.
    /// </summary>
    Task<bool> ScrollCameraToTargetAsync(string targetExpr, CancellationToken ct)
        => Task.FromResult(false);

    // 2026-04-29 (iter 148) — camera arc native UX surfaces. Each method
    // routes through the iter 143-145 LIVE bridge wires. Default impls
    // return false to keep older mocks compiling.

    /// <summary>iter 143 LIVE — Camera_To_Follow tracks target as it moves.</summary>
    Task<bool> CameraFollowAsync(string targetExpr, CancellationToken ct)
        => Task.FromResult(false);

    /// <summary>iter 144 LIVE — Rotate_Camera_To rotates camera to face target.</summary>
    Task<bool> RotateCameraToAsync(string targetExpr, CancellationToken ct)
        => Task.FromResult(false);

    /// <summary>iter 145 LIVE — Start_Cinematic_Camera enters cinematic mode (zero-arg).</summary>
    Task<bool> StartCinematicCameraAsync(CancellationToken ct)
        => Task.FromResult(false);

    /// <summary>iter 145 LIVE — End_Cinematic_Camera exits cinematic mode (zero-arg).</summary>
    Task<bool> EndCinematicCameraAsync(CancellationToken ct)
        => Task.FromResult(false);

    /// <summary>iter 145 LIVE — Set_Cinematic_Camera_Key sets a keyframe.</summary>
    Task<bool> SetCinematicCameraKeyAsync(string argsExpr, CancellationToken ct)
        => Task.FromResult(false);

    /// <summary>iter 145 LIVE — Transition_Cinematic_Camera_Key transitions between keys.</summary>
    Task<bool> TransitionCinematicCameraKeyAsync(string argsExpr, CancellationToken ct)
        => Task.FromResult(false);

    // 2026-05-05 (iter 192) — additional camera primitives shipped LIVE in
    // iter 162/165 but queued for native UX since then. All take a single
    // string-arg (time/degrees/target Lua expression).

    /// <summary>iter 162 LIVE — Zoom_Camera(time) zooms over the given duration.</summary>
    Task<bool> ZoomCameraAsync(string timeExpr, CancellationToken ct)
        => Task.FromResult(false);

    /// <summary>iter 165 LIVE — Fade_Screen_Out(time) fades the screen to black.</summary>
    Task<bool> FadeScreenOutAsync(string timeExpr, CancellationToken ct)
        => Task.FromResult(false);

    /// <summary>iter 165 LIVE — Rotate_Camera_By(degrees) rotates camera by relative degrees.</summary>
    Task<bool> RotateCameraByAsync(string degreesExpr, CancellationToken ct)
        => Task.FromResult(false);

    /// <summary>iter 165 LIVE — Point_Camera_At(target) points camera at a Lua target expression.</summary>
    Task<bool> PointCameraAtAsync(string targetExpr, CancellationToken ct)
        => Task.FromResult(false);
}
