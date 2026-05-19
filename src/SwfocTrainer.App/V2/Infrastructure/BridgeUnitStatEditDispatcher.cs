using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Unit Stat Editor tab) — adapter for IUnitStatEditDispatcher.
/// Routes through SWFOC_SetUnitField(addr, field, value) — the generic
/// per-field dispatcher registered in lua_bridge.cpp's RegisterAll. The
/// 13-field taxonomy (hull/max_hull/shield/max_shield/speed/max_speed/
/// attack_power floats, respawn_ms int, invuln_flag/prevent_death/is_hero/
/// respawn_enabled bools, owner_slot read-only peek) is enforced bridge-side;
/// unknown fields surface as "ERR:" and the dispatcher returns false.
///
/// 2026-04-29 (iter 136) — per-field LIVE branches added bridge-side for
/// hull (direct GameObj::HP write), shield (SetFrontShield + SetRearShield),
/// and speed (SetSpeedOverride). Other 10 fields stay Phase-1 mirror
/// pending RTTI-driven per-field offset table. UnitStatEditor's "Apply
/// staged edits" button now mutates the engine for the 3 LIVE fields.
/// Bridge also gates on IsObjOwnedByHuman so enemy units stay READ-ONLY.
/// </summary>
public sealed class BridgeUnitStatEditDispatcher : IUnitStatEditDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeUnitStatEditDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<bool> SetUnitFieldAsync(long objAddr, string field, float value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);
        var safeField = field.Replace("\\", "\\\\", StringComparison.Ordinal)
                             .Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_SetUnitField({0}, '{1}', {2})",
            objAddr, safeField, value);
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
