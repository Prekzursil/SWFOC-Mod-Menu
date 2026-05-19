using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class CooldownManagerService : ICooldownManagerService
{
    internal const string FeatureId = "v5_cooldown_reset";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<CooldownManagerService> _logger;

    public CooldownManagerService(
        ILuaBridgeExecutor bridge,
        ILogger<CooldownManagerService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    public CooldownManagerService(ILogger<CooldownManagerService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    public async Task<ActionExecutionResult> ResetCooldownsAsync(
        string profileId, CooldownResetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Scope == CooldownResetScope.SelectedUnit && string.IsNullOrEmpty(request.UnitId))
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "SelectedUnit scope requires a non-null UnitId",
                AddressSource: AddressSource.None);
        }

        var luaCommand = BuildCooldownResetLuaCommand(request);

        _logger.LogInformation(
            "Cooldown reset executing for scope {Scope} in profile {Profile}",
            request.Scope, profileId);

        if (_bridge is not null && !luaCommand.StartsWith("--", StringComparison.Ordinal))
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        // Fallback: return prepared result when no bridge or comment-only Lua.
        var diagnostics = new Dictionary<string, object?>
        {
            ["lua_call"] = luaCommand,
            ["scope"] = request.Scope.ToString()
        };

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Cooldown reset prepared for scope '{request.Scope}'",
            AddressSource: AddressSource.None,
            Diagnostics: diagnostics);
    }

    /// <summary>
    /// Builds the Lua command string for resetting ability cooldowns.
    /// </summary>
    internal static string BuildCooldownResetLuaCommand(CooldownResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Scope switch
        {
            CooldownResetScope.SelectedUnit => $"Find_First_Object(\"{request.UnitId}\"):Reset_Ability_Counter()",
            CooldownResetScope.AllPlayerUnits => "-- Reset all player unit cooldowns (requires iteration)",
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
    }
}
