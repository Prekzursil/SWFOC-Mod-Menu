using System.Globalization;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-26 (Unit D — Inspector tab) — adapter for IInspectorDispatcher.
///
/// Bridge format from <c>SWFOC_InspectUnit</c> is a space-delimited
/// key=value string covering hull / owner / obj_id / parent_idx /
/// status_flags / prevent_death / invuln_flag / hardpoint_flag /
/// components_ptr. The richer fields the InspectorDetailSnapshot record
/// requires (TypeName / MaxHull / Shield / MaxShield / Speed / MaxSpeed /
/// IsHero) aren't exposed by the current bridge — they default to zero /
/// empty until the bridge gets an extended inspect helper. The UI's
/// capability badge surfaces this as "Phase 2 partial".
/// </summary>
public sealed class BridgeInspectorDispatcher : IInspectorDispatcher
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly V2BridgeAdapter _bridge;

    public BridgeInspectorDispatcher(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public async Task<InspectorDetailSnapshot?> InspectUnitAsync(long objAddr, CancellationToken ct)
    {
        var lua = string.Format(Inv, "return SWFOC_InspectUnit({0})", objAddr);
        var rt = await _bridge.SendRawAsync(lua, ct).ConfigureAwait(false);
        if (!rt.Succeeded) return null;
        var resp = rt.Response ?? string.Empty;
        if (resp.StartsWith("ERR:", StringComparison.Ordinal)) return null;
        var fields = ParseKeyValueSpaceList(resp);
        var hull = ReadFloat(fields, "hull");
        var owner = ReadInt(fields, "owner");
        var invuln = ReadByte(fields, "invuln_flag") != 0;
        var preventDeath = ReadByte(fields, "prevent_death") != 0;
        return new InspectorDetailSnapshot(
            ObjAddr: objAddr,
            TypeName: string.Empty,
            OwnerSlot: owner,
            Hull: hull,
            MaxHull: 0f,
            Shield: 0f,
            MaxShield: 0f,
            Speed: 0f,
            MaxSpeed: 0f,
            IsHero: false,
            InvulnFlag: invuln,
            PreventDeath: preventDeath);
    }

    private static Dictionary<string, string> ParseKeyValueSpaceList(string resp)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in resp.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = token.IndexOf('=');
            if (eq <= 0) continue;
            dict[token[..eq]] = token[(eq + 1)..];
        }
        return dict;
    }

    private static float ReadFloat(Dictionary<string, string> dict, string key) =>
        dict.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, Inv, out var f) ? f : 0f;
    private static int ReadInt(Dictionary<string, string> dict, string key) =>
        dict.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, Inv, out var i) ? i : 0;
    private static byte ReadByte(Dictionary<string, string> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v)) return 0;
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            byte.TryParse(v[2..], NumberStyles.HexNumber, Inv, out var hx)) return hx;
        return byte.TryParse(v, NumberStyles.Integer, Inv, out var b) ? b : (byte)0;
    }
}
