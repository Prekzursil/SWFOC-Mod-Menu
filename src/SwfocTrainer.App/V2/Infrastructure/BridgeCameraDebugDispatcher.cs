using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Camera and Debug tab) — adapter for ICameraDebugDispatcher.
/// Bridge entry points (per RegisterAll in lua_bridge.cpp):
///   SWFOC_FreeCam(enable)
///   SWFOC_SetCameraPos(x, y, z)
///   SWFOC_DoString(lua)              (escape hatch for raw Lua, returns ERR: or OK: prefixed string)
///
/// SWFOC_SetCameraZoom is NOT registered today (Phase 2-pending — see
/// phase2_hook_backlog). The dispatcher routes through DoString that calls
/// the engine global (Camera_Set_Zoom or similar) directly; if that engine
/// global also doesn't exist the bridge surfaces "ERR:" and the UI shows
/// "bridge rejected" — the honest fallback path.
/// </summary>
public sealed class BridgeCameraDebugDispatcher : ICameraDebugDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeCameraDebugDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public Task<bool> SetFreeCamAsync(bool enable, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_FreeCam({0})", enable ? 1 : 0), ct);

    public Task<bool> SetCameraPosAsync(float x, float y, float z, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetCameraPos({0}, {1}, {2})", x, y, z), ct);

    /// <summary>
    /// 2026-05-06 (iter 239): LIVE camera read via direct call to
    /// CameraClass::GetPosition @ 0x261A40 (per iter-237 bridge wire).
    /// Returns "X,Y,Z" string from the per-frame camera matrix-pointer.
    /// Falls back to "0.000,0.000,0.000" when no active tactical camera
    /// (mode != 2 → galactic mode or no game loaded).
    /// </summary>
    public async Task<string?> GetCameraPosAsync(CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(
            "return SWFOC_GetCameraPos()", ct).ConfigureAwait(false);
        if (!rt.Succeeded) return null;
        return rt.Response;
    }

    /// <summary>
    /// 2026-04-28 (iter 107) — LIVE. Calls the engine's
    /// <c>Scroll_Camera_To</c> Lua API via the bridge's DoString helper.
    /// The <paramref name="targetExpr"/> is a Lua expression spliced
    /// verbatim into <c>Scroll_Camera_To(&lt;expr&gt;)</c>. Caller is
    /// responsible for safe Lua syntax — typical examples:
    /// <list type="bullet">
    ///   <item><c>Find_Planet("Yavin")</c></item>
    ///   <item><c>Find_First_Object("Empire_AT_AT")</c></item>
    ///   <item><c>Find_Object_Type("Rebel_Trooper_Squad")[0]</c></item>
    /// </list>
    /// </summary>
    public Task<bool> ScrollCameraToTargetAsync(string targetExpr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targetExpr);
        // 2026-04-28 (iter 107): wrap with SINGLE quotes so embedded
        // double-quotes (typical: Find_Planet("Yavin")) survive without
        // escape sequences. The simulator's regex doesn't honour Lua
        // escapes, so single-quote wrapping is the path that works on
        // both surfaces (real bridge AND simulator). If the operator's
        // expression itself contains a single quote, escape it as `''`
        // (Lua's literal apostrophe inside a single-quoted string is
        // not standard — Lua accepts only `\'`). We use the simpler
        // double-up because it's also what mod-tools tend to use; if
        // the operator ever passes a Lua expression with `\'` they need
        // to use SubmitRawCommand instead.
        var safe = targetExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv,
            "return SWFOC_ScrollCameraToTarget('{0}')", safe);
        return Send(lua, ct);
    }

    /// <summary>2026-04-29 (iter 148) — LIVE camera follow via Camera_To_Follow Lua API (iter 143).</summary>
    public Task<bool> CameraFollowAsync(string targetExpr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targetExpr);
        var safe = targetExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_CameraFollow('{0}')", safe);
        return Send(lua, ct);
    }

    /// <summary>2026-04-29 (iter 148) — LIVE camera rotation via Rotate_Camera_To Lua API (iter 144).</summary>
    public Task<bool> RotateCameraToAsync(string targetExpr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targetExpr);
        var safe = targetExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_RotateCameraTo('{0}')", safe);
        return Send(lua, ct);
    }

    /// <summary>2026-04-29 (iter 148) — LIVE cinematic-mode start via Start_Cinematic_Camera Lua API (iter 145).</summary>
    public Task<bool> StartCinematicCameraAsync(CancellationToken ct) =>
        Send("return SWFOC_StartCinematicCamera()", ct);

    /// <summary>2026-04-29 (iter 148) — LIVE cinematic-mode end via End_Cinematic_Camera Lua API (iter 145).</summary>
    public Task<bool> EndCinematicCameraAsync(CancellationToken ct) =>
        Send("return SWFOC_EndCinematicCamera()", ct);

    /// <summary>2026-04-29 (iter 148) — LIVE cinematic keyframe set via Set_Cinematic_Camera_Key Lua API (iter 145).</summary>
    public Task<bool> SetCinematicCameraKeyAsync(string argsExpr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(argsExpr);
        var safe = argsExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_SetCinematicCameraKey('{0}')", safe);
        return Send(lua, ct);
    }

    /// <summary>2026-04-29 (iter 148) — LIVE cinematic keyframe transition via Transition_Cinematic_Camera_Key Lua API (iter 145).</summary>
    public Task<bool> TransitionCinematicCameraKeyAsync(string argsExpr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(argsExpr);
        var safe = argsExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_TransitionCinematicCameraKey('{0}')", safe);
        return Send(lua, ct);
    }

    /// <summary>2026-05-05 (iter 192) — LIVE Zoom_Camera(time) (iter 162). Time arg as raw Lua.</summary>
    public Task<bool> ZoomCameraAsync(string timeExpr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(timeExpr);
        var safe = timeExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_ZoomCameraLua('{0}')", safe);
        return Send(lua, ct);
    }

    /// <summary>2026-05-05 (iter 192) — LIVE Fade_Screen_Out(time) (iter 165). Time arg as raw Lua.</summary>
    public Task<bool> FadeScreenOutAsync(string timeExpr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(timeExpr);
        var safe = timeExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_FadeScreenOutLua('{0}')", safe);
        return Send(lua, ct);
    }

    /// <summary>2026-05-05 (iter 192) — LIVE Rotate_Camera_By(degrees) (iter 165). Degrees arg as raw Lua.</summary>
    public Task<bool> RotateCameraByAsync(string degreesExpr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(degreesExpr);
        var safe = degreesExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_RotateCameraByLua('{0}')", safe);
        return Send(lua, ct);
    }

    /// <summary>2026-05-05 (iter 192) — LIVE Point_Camera_At(target) (iter 165). Target as Lua expression.</summary>
    public Task<bool> PointCameraAtAsync(string targetExpr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targetExpr);
        var safe = targetExpr.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_PointCameraAtLua('{0}')", safe);
        return Send(lua, ct);
    }

    public Task<bool> SetCameraZoomAsync(float zoom, CancellationToken ct)
    {
        // No dedicated bridge helper; route through DoString to attempt
        // the engine global. Returns ERR: if the global is also missing.
        var lua = string.Format(Inv,
            "return SWFOC_DoString(\"if Camera_Set_Zoom then Camera_Set_Zoom({0}) return 'OK: zoom={0}' else return 'ERR: Camera_Set_Zoom missing' end\")",
            zoom);
        return Send(lua, ct);
    }

    public async Task<string?> ExecuteRawLuaAsync(string lua, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lua);
        var safe = lua.Replace("\\", "\\\\", StringComparison.Ordinal)
                      .Replace("\"", "\\\"", StringComparison.Ordinal);
        var wrapped = string.Format(Inv, "return SWFOC_DoString(\"{0}\")", safe);
        var rt = await _bridge.SendRawAsync(wrapped, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return null;
        return rt.Response;
    }

    private async Task<bool> Send(string lua, CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
