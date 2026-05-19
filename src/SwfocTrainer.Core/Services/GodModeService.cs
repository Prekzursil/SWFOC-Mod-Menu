using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Wraps the C++ bridge's <c>SWFOC_GodMode</c> Lua helper so the editor can
/// toggle the god-mode memory hook from the UI without embedding raw Lua.
/// </summary>
/// <remarks>
/// The underlying helper lives in <c>swfoc_lua_bridge/lua_bridge.cpp</c> and
/// installs / removes a damage-handler hook at runtime. The Lua call pattern is
/// <c>return SWFOC_GodMode(1)</c> for enable and <c>return SWFOC_GodMode(0)</c>
/// for disable. The helper returns either <c>"OK: god mode enabled"</c>,
/// <c>"OK: god mode disabled"</c>, or <c>"ERR: ..."</c> on failure.
/// </remarks>
public sealed class GodModeService : IGodModeService
{
    internal const string FeatureId = "v5_god_mode";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<GodModeService> _logger;

    /// <summary>
    /// Creates a live service that routes calls through the Lua bridge.
    /// </summary>
    public GodModeService(
        ILuaBridgeExecutor bridge,
        ILogger<GodModeService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>
    /// Creates an offline service that only logs the Lua command (used by
    /// unit tests and design-time mocks).
    /// </summary>
    public GodModeService(ILogger<GodModeService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> SetGodModeAsync(
        string profileId, bool enable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var luaCommand = BuildGodModeLuaCommand(enable);

        _logger.LogInformation(
            "GodMode executing: enable={Enable} via {LuaCommand} for profile {Profile}",
            enable, luaCommand, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: enable ? "God mode enabled (offline)" : "God mode disabled (offline)",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["enable"] = enable
            });
    }

    /// <summary>
    /// Builds the Lua command string that invokes <c>SWFOC_GodMode</c>.
    /// </summary>
    internal static string BuildGodModeLuaCommand(bool enable)
    {
        return enable ? "return SWFOC_GodMode(1)" : "return SWFOC_GodMode(0)";
    }
}
