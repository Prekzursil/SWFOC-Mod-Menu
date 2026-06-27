using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Event Stream view) — adapter for IEventStreamDispatcher.
/// Bridge entry point: SWFOC_EventStreamDrain() returns CSV
///   "timestamp_ms;obj_addr;owner_slot;requested_hp;current_hp\n..."
/// Empty response (no rows) is treated as "no new events" by the VM.
/// </summary>
public sealed class BridgeEventStreamDispatcher : IEventStreamDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeEventStreamDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<IReadOnlyList<DamageEventRow>> DrainEventStreamAsync(CancellationToken ct)
    {
        var rt = await _bridge.SendRawAsync("return SWFOC_EventStreamDrain()", ct)
                              .ConfigureAwait(false);
        if (!rt.Succeeded) return Array.Empty<DamageEventRow>();
        var resp = rt.Response ?? string.Empty;
        if (resp.StartsWith("ERR:", StringComparison.Ordinal)) return Array.Empty<DamageEventRow>();
        var rows = new List<DamageEventRow>();
        foreach (var line in resp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(';');
            if (parts.Length < 5) continue;
            if (!long.TryParse(parts[0], NumberStyles.Integer, Inv, out var ts)) continue;
            if (!long.TryParse(parts[1], NumberStyles.Integer, Inv, out var addr)) continue;
            if (!int.TryParse(parts[2], NumberStyles.Integer, Inv, out var owner)) continue;
            if (!float.TryParse(parts[3], NumberStyles.Float, Inv, out var req)) continue;
            if (!float.TryParse(parts[4], NumberStyles.Float, Inv, out var cur)) continue;
            rows.Add(new DamageEventRow(ts, addr, owner, req, cur));
        }
        return rows;
    }
}
