using System.Globalization;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Wraps the C++ bridge's hero-respawn helpers: <c>SWFOC_SetHeroRespawn</c>
/// for custom durations and <c>SWFOC_HeroInstantRespawn</c> for the
/// instant-respawn hook toggle.
/// </summary>
/// <remarks>
/// <c>SWFOC_SetHeroRespawn</c> takes a floating-point seconds argument and
/// returns <c>"OK: prev=A.A new=B.B"</c>. <c>SWFOC_HeroInstantRespawn</c>
/// takes a 0/1 argument and returns <c>"1"</c> on success.
/// </remarks>
public sealed class HeroRespawnService : IHeroRespawnService
{
    internal const string CustomFeatureId = "v5_hero_respawn_custom";
    internal const string InstantFeatureId = "v5_hero_respawn_instant";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<HeroRespawnService> _logger;

    /// <summary>
    /// Creates a live hero-respawn service.
    /// </summary>
    public HeroRespawnService(
        ILuaBridgeExecutor bridge,
        ILogger<HeroRespawnService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>
    /// Creates an offline hero-respawn service.
    /// </summary>
    public HeroRespawnService(ILogger<HeroRespawnService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> SetCustomRespawnAsync(
        string profileId, double seconds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "Respawn seconds must be a finite non-negative value",
                AddressSource: AddressSource.None);
        }

        var luaCommand = BuildSetCustomRespawnLuaCommand(seconds);

        _logger.LogInformation(
            "HeroRespawn custom executing: seconds={Seconds} via {LuaCommand} for profile {Profile}",
            seconds, luaCommand, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, CustomFeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Hero respawn duration set to {seconds}s (offline)",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["seconds"] = seconds
            });
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> SetInstantRespawnAsync(
        string profileId, bool enable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var luaCommand = BuildSetInstantRespawnLuaCommand(enable);

        _logger.LogInformation(
            "HeroRespawn instant executing: enable={Enable} via {LuaCommand} for profile {Profile}",
            enable, luaCommand, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, InstantFeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: enable ? "Instant hero respawn enabled (offline)" : "Instant hero respawn disabled (offline)",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["enable"] = enable
            });
    }

    /// <summary>
    /// Builds the Lua command that invokes <c>SWFOC_SetHeroRespawn</c>.
    /// </summary>
    internal static string BuildSetCustomRespawnLuaCommand(double seconds)
    {
        return "return SWFOC_SetHeroRespawn(" + seconds.ToString("R", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Builds the Lua command that invokes <c>SWFOC_HeroInstantRespawn</c>.
    /// </summary>
    internal static string BuildSetInstantRespawnLuaCommand(bool enable)
    {
        return enable ? "return SWFOC_HeroInstantRespawn(1)" : "return SWFOC_HeroInstantRespawn(0)";
    }
}
