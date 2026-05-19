using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class FleetManagerService : IFleetManagerService
{
    private readonly ILogger<FleetManagerService> _logger;

    public FleetManagerService(ILogger<FleetManagerService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<IReadOnlyList<FleetInfo>> LoadFleetsAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        _logger.LogDebug(
            "Fleet enumeration requires active bridge connection (profile: {ProfileId})",
            profileId);

        IReadOnlyList<FleetInfo> empty = Array.Empty<FleetInfo>();
        return Task.FromResult(empty);
    }

    /// <summary>
    /// Builds the Lua command string for assembling a fleet at a planet.
    /// </summary>
    internal static string BuildAssembleFleetLuaCommand(string faction, string planet)
    {
        ArgumentNullException.ThrowIfNull(faction);
        ArgumentNullException.ThrowIfNull(planet);
        return $"Assemble_Fleet(Find_Player(\"{faction}\"), FindPlanet(\"{planet}\"))";
    }
}
