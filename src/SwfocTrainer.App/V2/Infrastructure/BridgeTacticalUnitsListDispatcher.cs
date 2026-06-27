using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// Phase 1 (thread A) — adapter that satisfies <see cref="ITacticalUnitsListDispatcher"/>
/// by sending <c>return SWFOC_ListTacticalUnits()</c> through the V2 bridge
/// and parsing the CSV with <see cref="TacticalUnitListParser"/>.
///
/// Failure modes (bridge unavailable, parse error, malformed rows) collapse to
/// an empty list — the ViewModel surfaces "loaded 0 units" via the feedback
/// sink rather than throwing into the WPF dispatcher.
/// </summary>
public sealed class BridgeTacticalUnitsListDispatcher : ITacticalUnitsListDispatcher
{
    private readonly V2BridgeAdapter _bridge;

    public BridgeTacticalUnitsListDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<IReadOnlyList<TacticalUnitRow>> ListTacticalUnitsAsync(
        CancellationToken ct)
    {
        var roundTrip = await _bridge
            .SendRawAsync("return SWFOC_ListTacticalUnits()", ct)
            .ConfigureAwait(false);
        if (!roundTrip.Succeeded)
        {
            return Array.Empty<TacticalUnitRow>();
        }
        return TacticalUnitListParser.Parse(roundTrip.Response).Rows;
    }
}
