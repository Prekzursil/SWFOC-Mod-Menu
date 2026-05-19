using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Hero Lab tab) — adapter for IHeroLabDispatcher.
/// Bridge entry points (per RegisterAll in lua_bridge.cpp):
///   SWFOC_ListHeroes()                           → CSV "addr;type;owner;alive;respawn_ms;respawn_enabled\n..."
///   SWFOC_SetHeroRespawn(seconds)                (global live default)
///   SWFOC_SetPermadeath(addr, enable)
///   SWFOC_KillUnit(addr)        (heroes are units; the helper handles hero detection)
///   SWFOC_ReviveUnit(addr)      (heroes are units; the helper enforces local-only revive)
///   SWFOC_HeroStatEdit(addr, field, value)
///
/// ListHeroes and per-hero permadeath/stat routes still depend on the hero
/// detection table. Respawn intentionally uses the live global default helper.
/// </summary>
public sealed class BridgeHeroLabDispatcher : IHeroLabDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeHeroLabDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<IReadOnlyList<HeroRow>> ListHeroesAsync(CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync("return SWFOC_ListHeroes()", ct)
                              .ConfigureAwait(false);
        if (!rt.Succeeded) return Array.Empty<HeroRow>();
        var resp = rt.Response ?? string.Empty;
        if (resp.StartsWith("ERR:", StringComparison.Ordinal)) return Array.Empty<HeroRow>();
        var rows = new List<HeroRow>();
        foreach (var line in resp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(';');
            if (parts.Length < 6) continue;
            if (!long.TryParse(parts[0], NumberStyles.Integer, Inv, out var addr)) continue;
            var typeName = parts[1];
            if (!int.TryParse(parts[2], NumberStyles.Integer, Inv, out var owner)) continue;
            var alive = parts[3] != "0";
            if (!int.TryParse(parts[4], NumberStyles.Integer, Inv, out var respawnMs)) continue;
            var respawnEnabled = parts[5] != "0";
            rows.Add(new HeroRow(addr, typeName, owner, alive, respawnMs, respawnEnabled));
        }
        return rows;
    }

    public Task<bool> SetHeroRespawnTimerAsync(long addr, int ms, CancellationToken ct)
    {
        // The per-hero timer helper remains Phase 2. Route this UI action
        // through the confirmed live global default instead, preserving the
        // existing state contract while avoiding a replay-only button.
        _ = addr;
        var seconds = ms / 1000.0;
        return Send(string.Format(Inv, "return SWFOC_SetHeroRespawn({0})", seconds), ct);
    }

    public Task<bool> SetPermadeathAsync(long addr, bool permadeath, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetPermadeath({0}, {1})",
            addr, permadeath ? 1 : 0), ct);

    public Task<bool> KillHeroAsync(long addr, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_KillUnit({0})", addr), ct);

    public Task<bool> ReviveHeroAsync(long addr, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_ReviveUnit({0})", addr), ct);

    public Task<bool> EditHeroStatAsync(long addr, string field, float value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);
        var safeField = field.Replace("\\", "\\\\", StringComparison.Ordinal)
                             .Replace("'", "\\'", StringComparison.Ordinal);
        var lua = string.Format(Inv, "return SWFOC_HeroStatEdit({0}, '{1}', {2})",
            addr, safeField, value);
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
