using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Director Mode tab) — adapter for IDirectorDispatcher.
/// Bridge entry points (per RegisterAll in lua_bridge.cpp):
///   SWFOC_SetGameSpeed(scale)        (PHASE 2 PENDING — UI command disabled)
///   SWFOC_SetCameraPos(x, y, z)      (Phase-1 mirror — pin pending)
///
/// SetUiVisible / SetCameraZoom are not registered as dedicated helpers;
/// they route through SWFOC_DoString to call engine globals (Hide_HUD,
/// Camera_Set_Zoom). If those globals are also missing the bridge returns
/// "ERR:" and the dispatcher returns false — the honest fallback.
/// </summary>
public sealed class BridgeDirectorDispatcher : IDirectorDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeDirectorDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public Task<bool> SetUiVisibleAsync(bool visible, CancellationToken ct)
    {
        var lua = string.Format(Inv,
            "return SWFOC_DoString(\"if Hide_HUD then Hide_HUD({0}) return 'OK: ui={0}' else return 'ERR: Hide_HUD missing' end\")",
            visible ? "false" : "true");
        return Send(lua, ct);
    }

    public Task<bool> SetGameSpeedAsync(float scale, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetGameSpeed({0})", scale), ct);

    public Task<bool> SetCameraPosAsync(float x, float y, float z, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetCameraPos({0}, {1}, {2})", x, y, z), ct);

    public Task<bool> SetCameraZoomAsync(float zoom, CancellationToken ct)
    {
        var lua = string.Format(Inv,
            "return SWFOC_DoString(\"if Camera_Set_Zoom then Camera_Set_Zoom({0}) return 'OK: zoom={0}' else return 'ERR: Camera_Set_Zoom missing' end\")",
            zoom);
        return Send(lua, ct);
    }

    private async Task<bool> Send(string lua, CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
