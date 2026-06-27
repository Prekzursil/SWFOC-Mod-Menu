using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Lua Playground tab) — adapter for ILuaPlaygroundDispatcher.
/// The playground is the modder's escape hatch — it forwards script text
/// through SWFOC_DoString verbatim. Bridge response convention is preserved:
/// 'OK: ...' on success, 'ERR: ...' on failure. The VM wraps the response
/// in a Warning severity so the operator treats every result as "did what I
/// just type actually do?".
/// </summary>
public sealed class BridgeLuaPlaygroundDispatcher : ILuaPlaygroundDispatcher
{
    private readonly V2BridgeAdapter _bridge;

    public BridgeLuaPlaygroundDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<string?> ExecuteLuaAsync(string script, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(script);
        var safe = script.Replace("\\", "\\\\", StringComparison.Ordinal)
                         .Replace("\"", "\\\"", StringComparison.Ordinal);
        var lua = $"return SWFOC_DoString(\"{safe}\")";
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        return rt.Succeeded ? rt.Response : null;
    }
}
