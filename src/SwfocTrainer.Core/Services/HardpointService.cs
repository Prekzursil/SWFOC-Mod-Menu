using System.Globalization;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// A single hardpoint entry parsed out of the <c>SWFOC_GetHardpoints</c>
/// bridge response.
/// </summary>
/// <param name="Address">Absolute runtime address of the child GameObject.
/// Stored as <see cref="long"/> so the SwfocTrainer.Core assembly stays
/// CLS-compliant. User-space pointer addresses on x64 fit in 48 bits and
/// are always positive when cast, so no information is lost.</param>
/// <param name="Hp">Current hardpoint HP.</param>
public sealed record HardpointEntry(long Address, float Hp);

/// <summary>
/// Parsed response for <see cref="HardpointService.ParseHardpointResult"/>.
/// </summary>
/// <param name="Count">Number of hardpoints reported by the bridge.</param>
/// <param name="Entries">Resolved hardpoint entries. May contain fewer than
/// <paramref name="Count"/> entries if parsing failed partway through.</param>
public sealed record HardpointResult(int Count, IReadOnlyList<HardpointEntry> Entries);

/// <summary>
/// Wraps the C++ bridge's <c>SWFOC_GetHardpoints</c> Lua helper.
/// </summary>
/// <remarks>
/// The helper returns a string shaped like
/// <c>"count=3 child0=0x1234 hp0=600 child1=0x5678 hp1=450 child2=0x9abc hp2=0"</c>.
/// This service exposes a simple split-based parser that converts the
/// response into a <see cref="HardpointResult"/>. The parser intentionally
/// avoids regular expressions per the Part 3.4 constraints.
/// </remarks>
public sealed class HardpointService : IHardpointService
{
    internal const string FeatureId = "v5_hardpoints";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<HardpointService> _logger;

    /// <summary>
    /// Creates a live hardpoint service.
    /// </summary>
    public HardpointService(
        ILuaBridgeExecutor bridge,
        ILogger<HardpointService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>
    /// Creates an offline hardpoint service.
    /// </summary>
    public HardpointService(ILogger<HardpointService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> GetHardpointsAsync(
        string profileId, long objAddr, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var luaCommand = BuildGetHardpointsLuaCommand(objAddr);

        _logger.LogInformation(
            "GetHardpoints executing for profile {Profile}: {LuaCommand}",
            profileId, luaCommand);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"GetHardpoints offline for 0x{objAddr:X}",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["obj_addr"] = objAddr
            });
    }

    /// <summary>
    /// Builds the Lua command string that invokes <c>SWFOC_GetHardpoints</c>.
    /// </summary>
    internal static string BuildGetHardpointsLuaCommand(long objAddr)
    {
        return "return SWFOC_GetHardpoints(" + objAddr.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Parses the bridge response into a <see cref="HardpointResult"/>. The
    /// parser walks the tokens linearly without regex.
    /// </summary>
    /// <param name="result">Raw response from the bridge. May be null or empty.</param>
    public static HardpointResult ParseHardpointResult(string? result)
    {
        var entries = new List<HardpointEntry>();

        if (string.IsNullOrWhiteSpace(result))
        {
            return new HardpointResult(0, entries);
        }

        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        var tokens = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var eq = token.IndexOf('=');
            if (eq <= 0 || eq == token.Length - 1)
            {
                continue;
            }
            raw[token.Substring(0, eq)] = token.Substring(eq + 1);
        }

        var count = 0;
        if (raw.TryGetValue("count", out var countText) &&
            int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount))
        {
            count = parsedCount;
        }

        for (var i = 0; i < count; i++)
        {
            var childKey = "child" + i.ToString(CultureInfo.InvariantCulture);
            var hpKey = "hp" + i.ToString(CultureInfo.InvariantCulture);

            if (!raw.TryGetValue(childKey, out var childText) ||
                !raw.TryGetValue(hpKey, out var hpText))
            {
                break;
            }

            if (!TryParseUnsigned(childText, out var address))
            {
                break;
            }

            if (!TryParseFloat(hpText, out var hp))
            {
                break;
            }

            entries.Add(new HardpointEntry(address, hp));
        }

        return new HardpointResult(count, entries);
    }

    private static bool TryParseUnsigned(string text, out long value)
    {
        // Parse as ulong to accept the full 64-bit unsigned address space the
        // bridge emits in hex, then cast to long. User-space pointers on
        // Windows x64 fit in 48 bits so the round trip is lossless; any
        // address that would overflow a signed long (>= 2^63) is rejected.
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexU)
                && hexU <= long.MaxValue)
            {
                value = (long)hexU;
                return true;
            }
            value = 0;
            return false;
        }
        if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decU)
            && decU <= long.MaxValue)
        {
            value = (long)decU;
            return true;
        }
        value = 0;
        return false;
    }

    private static bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
