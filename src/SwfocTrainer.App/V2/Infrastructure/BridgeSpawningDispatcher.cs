using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Spawning tab) — adapter for ISpawningDispatcher.
/// Routes through SWFOC_SpawnUnit(type_id, slot, x, y, z, count) registered
/// in lua_bridge.cpp's RegisterAll. Phase-1 mirror today — the live engine
/// call to Spawn_Unit is pending IDA-pin (see phase2_hook_backlog).
///
/// Type id is single-quoted; embedded apostrophes are escaped via Lua
/// backslash conventions. The bridge harness verifies the wire format.
/// </summary>
public sealed class BridgeSpawningDispatcher : ISpawningDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeSpawningDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<bool> SpawnUnitAsync(
        string typeId, int slot, float x, float y, float z, int count, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        var safeId = typeId.Replace("\\", "\\\\", StringComparison.Ordinal)
                           .Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv,
            "return SWFOC_SpawnUnit('{0}', {1}, {2}, {3}, {4}, {5})",
            safeId, slot, x, y, z, count);
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
