using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Speed tab) — adapter for ISpeedDispatcher.
/// Bridge entry points (per RegisterAll in lua_bridge.cpp):
///   SWFOC_SetGameSpeed(speed)                    (PHASE 2 PENDING — UI command disabled)
///   SWFOC_SetPerFactionSpeedMultiplier(slot, mult)
///   SWFOC_SetUnitSpeed(obj_addr, speed)
///
/// All three land as Phase-1 mirrors today (CapabilityStatus.Phase2HookPending).
/// The detour pins for the live engine effect are tracked in
/// knowledge-base/phase2_hook_backlog_2026-04-26.md (game-tick scheduler,
/// per-faction locomotor multiplier, locomotor two-deref).
/// </summary>
public sealed class BridgeSpeedDispatcher : ISpeedDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeSpeedDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public Task<bool> SetGameSpeedAsync(float speed, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetGameSpeed({0})", speed), ct);

    public Task<bool> SetFactionSpeedMultiplierAsync(int slot, float mult, CancellationToken ct) =>
        Send(string.Format(Inv,
            "return SWFOC_SetPerFactionSpeedMultiplier({0}, {1})", slot, mult), ct);

    public Task<bool> SetUnitSpeedAsync(long objAddr, float speed, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetUnitSpeed({0}, {1})", objAddr, speed), ct);

    // 2026-04-28 (iter 100): revert helper. Calls ClearSpeedOverride @
    // RVA 0x38F8B0 via the bridge dispatcher.
    public Task<bool> ClearUnitSpeedOverrideAsync(long objAddr, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_ClearUnitSpeedOverride({0})", objAddr), ct);

    private async Task<bool> Send(string lua, CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
