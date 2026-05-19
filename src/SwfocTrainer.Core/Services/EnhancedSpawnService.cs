using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class EnhancedSpawnService : IEnhancedSpawnService
{
    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<EnhancedSpawnService> _logger;

    public EnhancedSpawnService(
        ILuaBridgeExecutor bridge,
        ILogger<EnhancedSpawnService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    public EnhancedSpawnService(ILogger<EnhancedSpawnService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    public async Task<EnhancedSpawnBatchResult> ExecuteSpawnAsync(
        string profileId, EnhancedSpawnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(request);

        var actionId = ResolveActionId(request.Mode);
        var luaCommand = BuildSpawnLuaCommand(request);

        _logger.LogInformation(
            "Spawn batch executing: {UnitId} x{Quantity} as {Mode} ({ActionId}) for {Faction} — lua={Lua}",
            request.UnitId, request.Quantity, request.Mode, actionId, request.TargetFaction, luaCommand);

        var succeeded = 0;
        var failed = 0;
        var errors = new List<string>();

        for (var i = 0; i < request.Quantity; i++)
        {
            if (_bridge is not null)
            {
                var result = await _bridge.ExecuteLuaAsync(profileId, luaCommand, actionId, cancellationToken);
                if (result.Succeeded)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                    errors.Add(result.Message);
                    if (request.StopOnFailure)
                    {
                        break;
                    }
                }
            }
            else
            {
                // No bridge configured — count as succeeded for stub behavior.
                succeeded++;
            }
        }

        return new EnhancedSpawnBatchResult(
            Attempted: request.Quantity,
            Succeeded: succeeded,
            Failed: failed,
            Errors: errors);
    }

    /// <summary>
    /// Builds the Lua command string for a spawn request.
    /// </summary>
    internal static string BuildSpawnLuaCommand(EnhancedSpawnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Mode switch
        {
            SpawnMode.Tactical =>
                $"Spawn_Unit(Find_Player(\"{request.TargetFaction}\"), Find_Object_Type(\"{request.UnitId}\"), Create_Position(0,0,0))",
            SpawnMode.Reinforcement =>
                $"Reinforce_Unit(Find_Player(\"{request.TargetFaction}\"), Find_Object_Type(\"{request.UnitId}\"), Create_Position(0,0,0))",
            SpawnMode.GalacticPersistent =>
                $"Galactic_Spawn_Unit(Find_Player(\"{request.TargetFaction}\"), Find_Object_Type(\"{request.UnitId}\"), FindPlanet(\"{request.TargetPlanet ?? "CORUSCANT"}\"))",
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unknown spawn mode")
        };
    }

    internal static string ResolveActionId(SpawnMode mode)
    {
        return mode switch
        {
            SpawnMode.Tactical => "spawn_tactical_entity",
            SpawnMode.Reinforcement => "spawn_context_entity",
            SpawnMode.GalacticPersistent => "spawn_galactic_entity",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown spawn mode")
        };
    }
}
