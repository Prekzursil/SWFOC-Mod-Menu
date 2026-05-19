using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Battle Control tab) — adapter for IBattleControlDispatcher.
/// Bridge entry points (per RegisterAll in lua_bridge.cpp):
///   SWFOC_SuspendAiLua(seconds)
///   SWFOC_HealAllLocal()
///   SWFOC_SetUnitCapOverride(slot, cap)         (-1 = unlimited; -2 = clear override)
///
/// "Kill all enemies" is composed client-side via SWFOC_DoString iterating
/// SWFOC_ListTacticalUnits + SWFOC_KillUnit. Phase 2-pending dedicated helper
/// (see phase2_hook_backlog) — until then this Lua snippet runs in-bridge so
/// it survives focus-loss drain.
/// </summary>
public sealed class BridgeBattleControlDispatcher : IBattleControlDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeBattleControlDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public Task<bool> SetFreezeAiAsync(int slot, bool enable, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SuspendAiLua('{0}')",
            enable ? 3600 : 0), ct);

    public Task<bool> KillAllEnemiesAsync(CancellationToken ct)
    {
        const string lua = """
            local s = SWFOC_ListTacticalUnits() or ''
            local n = 0
            local pos = 1
            while pos <= string.len(s) do
              local nl = string.find(s, '\n', pos, 1)
              local line = nl and string.sub(s, pos, nl - 1) or string.sub(s, pos)
              if string.len(line) > 0 then
                local sep1 = string.find(line, ';', 1, 1)
                local sep2 = sep1 and string.find(line, ';', sep1 + 1, 1) or nil
                if sep1 and sep2 then
                  local addr = tonumber(string.sub(line, 1, sep1 - 1)) or 0
                  local slot = tonumber(string.sub(line, sep1 + 1, sep2 - 1)) or -1
                  if addr ~= 0 and slot ~= -1 then
                    SWFOC_KillUnit(addr)
                    n = n + 1
                  end
                end
              end
              pos = nl and (nl + 1) or (string.len(s) + 1)
            end
            return 'OK: killed ' .. n
            """;
        return Send(lua, ct);
    }

    public Task<bool> HealAllLocalAsync(CancellationToken ct) =>
        Send("return SWFOC_HealAllLocal()", ct);

    public Task<bool> SetUnitCapOverrideAsync(int slot, int cap, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetUnitCapOverride({0}, {1})", slot, cap), ct);

    public Task<bool> ClearUnitCapOverrideAsync(int slot, CancellationToken ct) =>
        Send(string.Format(Inv, "return SWFOC_SetUnitCapOverride({0}, -2)", slot), ct);

    private async Task<bool> Send(string lua, CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
