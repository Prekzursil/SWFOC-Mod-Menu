using System.Globalization;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Parsed snapshot of a unit as returned by <c>SWFOC_InspectUnit</c>.
/// Any field that was missing or malformed in the bridge output is left null.
/// </summary>
/// <remarks>
/// <c>Owner</c> and <c>ComponentsPtr</c> use <see cref="long"/> so the
/// SwfocTrainer.Core assembly stays CLS-compliant. User-space pointer
/// addresses on Windows x64 fit in 48 bits and round-trip losslessly.
/// </remarks>
public sealed record InspectUnitResult(
    float? Hull,
    long? Owner,
    int? ObjectId,
    int? ParentIndex,
    int? StatusFlags,
    int? PreventDeath,
    int? InvulnFlag,
    int? HardpointFlag,
    long? ComponentsPtr,
    IReadOnlyDictionary<string, string> RawFields);

/// <summary>
/// Wraps the C++ bridge's <c>SWFOC_InspectUnit</c> Lua helper which dumps the
/// critical fields of a GameObject instance as a space-delimited
/// <c>key=value</c> string.
/// </summary>
/// <remarks>
/// The bridge helper returns a string shaped like
/// <c>"hull=600 owner=0x... obj_id=42 parent_idx=0 status_flags=0x0 prevent_death=0 invuln_flag=0 hardpoint_flag=0 components_ptr=0x..."</c>
/// and this service exposes a simple split-based parser that converts the
/// response into an <see cref="InspectUnitResult"/> record. The parser
/// intentionally avoids regular expressions per the Part 3.4 constraints.
/// </remarks>
public sealed class UnitInspectorService : IUnitInspectorService
{
    internal const string FeatureId = "v5_inspect_unit";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<UnitInspectorService> _logger;

    /// <summary>
    /// Creates a live inspector service.
    /// </summary>
    public UnitInspectorService(
        ILuaBridgeExecutor bridge,
        ILogger<UnitInspectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>
    /// Creates an offline inspector service.
    /// </summary>
    public UnitInspectorService(ILogger<UnitInspectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> InspectUnitAsync(
        string profileId, long objAddr, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var luaCommand = BuildInspectUnitLuaCommand(objAddr);

        _logger.LogInformation(
            "InspectUnit executing for profile {Profile}: {LuaCommand}",
            profileId, luaCommand);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"InspectUnit offline for 0x{objAddr:X}",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["obj_addr"] = objAddr
            });
    }

    /// <summary>
    /// Builds the Lua command string that invokes <c>SWFOC_InspectUnit</c>.
    /// </summary>
    internal static string BuildInspectUnitLuaCommand(long objAddr)
    {
        return "return SWFOC_InspectUnit(" + objAddr.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Parses the space-delimited <c>key=value</c> string returned by the
    /// bridge helper into a strongly-typed record.
    /// </summary>
    /// <param name="result">Raw response from the bridge. May be null or empty.</param>
    public static InspectUnitResult ParseInspectResult(string? result)
    {
        var raw = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(result))
        {
            return new InspectUnitResult(null, null, null, null, null, null, null, null, null, raw);
        }

        var tokens = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var eq = token.IndexOf('=');
            if (eq <= 0 || eq == token.Length - 1)
            {
                continue;
            }

            var key = token.Substring(0, eq);
            var value = token.Substring(eq + 1);
            raw[key] = value;
        }

        return new InspectUnitResult(
            Hull: TryParseFloat(raw, "hull"),
            Owner: TryParseUnsigned(raw, "owner"),
            ObjectId: TryParseInt(raw, "obj_id"),
            ParentIndex: TryParseInt(raw, "parent_idx"),
            StatusFlags: TryParseInt(raw, "status_flags"),
            PreventDeath: TryParseInt(raw, "prevent_death"),
            InvulnFlag: TryParseInt(raw, "invuln_flag"),
            HardpointFlag: TryParseInt(raw, "hardpoint_flag"),
            ComponentsPtr: TryParseUnsigned(raw, "components_ptr"),
            RawFields: raw);
    }

    private static float? TryParseFloat(IReadOnlyDictionary<string, string> raw, string key)
    {
        if (!raw.TryGetValue(key, out var text))
        {
            return null;
        }
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : (float?)null;
    }

    private static int? TryParseInt(IReadOnlyDictionary<string, string> raw, string key)
    {
        if (!raw.TryGetValue(key, out var text))
        {
            return null;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                ? hex
                : (int?)null;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec)
            ? dec
            : (int?)null;
    }

    private static long? TryParseUnsigned(IReadOnlyDictionary<string, string> raw, string key)
    {
        // Parse as ulong to accept the full 64-bit unsigned hex range the
        // bridge may emit, then cast to long. Values above long.MaxValue
        // (2^63) are treated as parse failure — user-space x64 pointers
        // never reach that magnitude, so this is purely defensive.
        if (!raw.TryGetValue(key, out var text))
        {
            return null;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                && hex <= long.MaxValue)
            {
                return (long)hex;
            }
            return null;
        }

        if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec)
            && dec <= long.MaxValue)
        {
            return (long)dec;
        }
        return null;
    }
}
