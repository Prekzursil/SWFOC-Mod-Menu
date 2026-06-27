using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Galactic tab) — adapter for IGalacticDispatcher.
/// Bridge entry points (per RegisterAll in lua_bridge.cpp):
///   SWFOC_GetPlanets()           → CSV "id;owner;tech\n..."
///   SWFOC_ChangePlanetOwner(id, owner)
///   SWFOC_RevealAll(enable)
///   SWFOC_SetDiplomacy(slotA, slotB, relation)  (relation: 0=Allied, 1=Hostile)
///
/// SetRevealAll and SetDiplomacy are LIVE. Planet ownership and story-arrival
/// mutation helpers remain Phase-2 pending until the galactic-state APIs are
/// pinned live.
/// </summary>
public sealed class BridgeGalacticDispatcher : IGalacticDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeGalacticDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<IReadOnlyList<PlanetRow>> GetPlanetsAsync(CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync("return SWFOC_GetPlanets()", ct)
                              .ConfigureAwait(false);
        if (!rt.Succeeded) return Array.Empty<PlanetRow>();
        var resp = rt.Response ?? string.Empty;
        if (resp.StartsWith("ERR:", StringComparison.Ordinal)) return Array.Empty<PlanetRow>();
        var rows = new List<PlanetRow>();
        foreach (var line in resp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(';');
            if (parts.Length < 3) continue;
            var id = parts[0].Trim();
            var owner = parts[1].Trim();
            if (!int.TryParse(parts[2], NumberStyles.Integer, Inv, out var tech)) continue;
            if (!string.IsNullOrEmpty(id)) rows.Add(new PlanetRow(id, owner, tech));
        }
        return rows;
    }

    public Task<bool> ChangePlanetOwnerAsync(string planetId, string newOwner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(planetId);
        ArgumentNullException.ThrowIfNull(newOwner);
        var lua = string.Format(Inv, "return SWFOC_ChangePlanetOwner('{0}', '{1}')",
            EscapeLua(planetId), EscapeLua(newOwner));
        return Send(lua, ct);
    }

    public Task<bool> ChangePlanetOwnerWithModeAsync(
        string planetId, string newOwner, PlanetFlipMode mode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(planetId);
        ArgumentNullException.ThrowIfNull(newOwner);
        var modeToken = mode switch
        {
            PlanetFlipMode.Convert => "convert",
            PlanetFlipMode.PureKick => "pure_kick",
            _ => "default",
        };
        var lua = string.Format(Inv,
            "return SWFOC_ChangePlanetOwnerWithMode('{0}', '{1}', '{2}')",
            EscapeLua(planetId), EscapeLua(newOwner), modeToken);
        return Send(lua, ct);
    }

    public Task<bool> SpawnAsStoryArrivalAsync(
        string typeId, string planetId, string faction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        ArgumentNullException.ThrowIfNull(planetId);
        ArgumentNullException.ThrowIfNull(faction);
        var lua = string.Format(Inv,
            "return SWFOC_SpawnAsStoryArrival('{0}', '{1}', '{2}')",
            EscapeLua(typeId), EscapeLua(planetId), EscapeLua(faction));
        return Send(lua, ct);
    }

    public Task<bool> SetRevealAllAsync(bool enable, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_RevealAll({0})", enable ? 1 : 0), ct);

    public Task<bool> SetDiplomacyAsync(string a, string b, DiplomacyRelation rel, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var relCode = rel switch
        {
            DiplomacyRelation.Allied => 1,
            DiplomacyRelation.Hostile => 2,
            _ => 0,
        };
        var lua = string.Format(Inv, "return SWFOC_SetDiplomacy('{0}', '{1}', {2})",
            EscapeLua(a), EscapeLua(b), relCode);
        return Send(lua, ct);
    }

    private static string EscapeLua(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("'", "\\'", StringComparison.Ordinal);

    private async Task<bool> Send(string lua, CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
