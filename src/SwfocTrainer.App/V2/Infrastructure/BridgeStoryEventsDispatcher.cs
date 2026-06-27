using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Story Events tab) — adapter for IStoryEventsDispatcher.
/// No dedicated bridge helpers exist yet for story events / flags; these route
/// through SWFOC_DoString and call the engine globals (Story_Event,
/// Set_Game_Flag) directly. Phase 2-pending: SWFOC_FireStoryEvent +
/// SWFOC_SetStoryFlag in the bridge for typed validation.
///
/// Bridge response convention: SWFOC_DoString returns 'OK: ...' or 'ERR: ...'.
/// </summary>
public sealed class BridgeStoryEventsDispatcher : IStoryEventsDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeStoryEventsDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public Task<bool> FireStoryEventAsync(string eventId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(eventId);
        var safe = EscapeLua(eventId);
        var lua = string.Format(Inv,
            "return SWFOC_DoString(\"if Story_Event then Story_Event('{0}') return 'OK: dispatched' else return 'ERR: Story_Event missing' end\")",
            safe);
        return Send(lua, ct);
    }

    public Task<bool> SetStoryFlagAsync(string flag, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentNullException.ThrowIfNull(value);
        var safeFlag = EscapeLua(flag);
        var safeValue = EscapeLua(value);
        var lua = string.Format(Inv,
            "return SWFOC_DoString(\"if Set_Game_Flag then Set_Game_Flag('{0}', '{1}') return 'OK: flag set' else return 'ERR: Set_Game_Flag missing' end\")",
            safeFlag, safeValue);
        return Send(lua, ct);
    }

    private static string EscapeLua(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("'", "\\'", StringComparison.Ordinal)
         .Replace("\"", "\\\"", StringComparison.Ordinal);

    private async Task<bool> Send(string lua, CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
