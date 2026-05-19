using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Cross-Faction Recruitment tab) — adapter for
/// ICrossFactionDispatcher. No dedicated bridge helper today; routes through
/// SWFOC_DoString that attempts to call the engine's Change_Owner side-effect
/// chain on the unit object. Phase 2-pending: dedicated SWFOC_TransferOwnership
/// helper that wraps the safe Change_Owner sequence (cleanup of selection,
/// hardpoint reattach, AI brain re-attach).
///
/// The Core VM enforces the local-only source rule before the dispatcher is
/// ever called, so the bridge call only fires for guaranteed-safe transfers.
/// </summary>
public sealed class BridgeCrossFactionDispatcher : ICrossFactionDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeCrossFactionDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<bool> TransferOwnershipAsync(long objAddr, int targetSlot, CancellationToken ct)
    {
        var lua = string.Format(Inv,
            "return SWFOC_DoString(\"local u = ObjectByAddr and ObjectByAddr({0}) or nil; if u and u.Change_Owner then u:Change_Owner({1}) return 'OK: ownership transferred' else return 'ERR: ObjectByAddr or Change_Owner missing' end\")",
            objAddr, targetSlot);
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return false;
        var resp = rt.Response ?? string.Empty;
        return !resp.StartsWith("ERR:", StringComparison.Ordinal);
    }
}
