using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Wraps the C++ bridge's <c>SWFOC_OneHitKill</c> Lua helper so the editor can
/// toggle the damage-amplification hook from the UI.
/// </summary>
/// <remarks>
/// The Lua call pattern is <c>return SWFOC_OneHitKill(1)</c> for enable and
/// <c>return SWFOC_OneHitKill(0)</c> for disable. The helper returns either
/// <c>"OK: one-hit kill enabled"</c>, <c>"OK: one-hit kill disabled"</c>, or
/// <c>"ERR: ..."</c> on failure.
/// </remarks>
public sealed class OneHitKillService : IOneHitKillService
{
    internal const string FeatureId = "v5_one_hit_kill";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<OneHitKillService> _logger;

    /// <summary>
    /// Creates a live service that routes calls through the Lua bridge.
    /// </summary>
    public OneHitKillService(
        ILuaBridgeExecutor bridge,
        ILogger<OneHitKillService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>
    /// Creates an offline service that only logs the Lua command.
    /// </summary>
    public OneHitKillService(ILogger<OneHitKillService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> SetOneHitKillAsync(
        string profileId, bool enable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var luaCommand = BuildOneHitKillLuaCommand(enable);

        _logger.LogInformation(
            "OneHitKill executing: enable={Enable} via {LuaCommand} for profile {Profile}",
            enable, luaCommand, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: enable ? "One-hit kill enabled (offline)" : "One-hit kill disabled (offline)",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["enable"] = enable
            });
    }

    /// <summary>
    /// Builds the Lua command string that invokes <c>SWFOC_OneHitKill</c>.
    /// </summary>
    internal static string BuildOneHitKillLuaCommand(bool enable)
    {
        return enable ? "return SWFOC_OneHitKill(1)" : "return SWFOC_OneHitKill(0)";
    }
}
