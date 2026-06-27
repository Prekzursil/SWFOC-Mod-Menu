using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class PlanetManagerService : IPlanetManagerService
{
    private const string PlanetCatalogKey = "planet_catalog";
    private const string PlanetsKey = "planets";
    internal const string SetOwnerFeatureId = "set_planet_owner";

    private readonly ICatalogService _catalog;
    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<PlanetManagerService> _logger;

    public PlanetManagerService(
        ICatalogService catalog,
        ILuaBridgeExecutor bridge,
        ILogger<PlanetManagerService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _bridge = bridge;
        _logger = logger;
    }

    public PlanetManagerService(
        ICatalogService catalog,
        ILogger<PlanetManagerService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _bridge = null;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlanetInfo>> LoadPlanetsAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var catalog = await _catalog.LoadCatalogAsync(profileId, cancellationToken);

        IReadOnlyList<string> planetIds;
        if (catalog.TryGetValue(PlanetCatalogKey, out var byKey))
        {
            planetIds = byKey;
        }
        else if (catalog.TryGetValue(PlanetsKey, out var byAlt))
        {
            planetIds = byAlt;
        }
        else
        {
            _logger.LogDebug(
                "No planet catalog found for profile {ProfileId}", profileId);
            return Array.Empty<PlanetInfo>();
        }

        var planets = new List<PlanetInfo>(planetIds.Count);
        foreach (var planetId in planetIds)
        {
            if (string.IsNullOrWhiteSpace(planetId))
            {
                continue;
            }

            planets.Add(new PlanetInfo(
                PlanetId: planetId,
                DisplayName: FormatDisplayName(planetId),
                OwnerFaction: "Unknown",
                SpaceStationLevel: 0,
                Buildings: Array.Empty<string>(),
                CorruptionLevel: 0,
                CorruptionKind: CorruptionType.None));
        }

        _logger.LogInformation(
            "Loaded {Count} planets for profile {ProfileId}",
            planets.Count, profileId);

        return planets.AsReadOnly();
    }

    public async Task<ActionExecutionResult> SetPlanetOwnerAsync(
        string profileId, string planetId, string newOwner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(planetId);
        ArgumentNullException.ThrowIfNull(newOwner);

        var luaCommand = BuildSetPlanetOwnerLuaCommand(planetId, newOwner);

        _logger.LogInformation(
            "Set planet owner executing: {PlanetId} -> {NewOwner} for profile {ProfileId}",
            planetId, newOwner, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, SetOwnerFeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Planet owner change prepared: {planetId} -> {newOwner}",
            AddressSource: AddressSource.None);
    }

    /// <summary>
    /// Builds the Lua command string for changing a planet's owner.
    /// </summary>
    internal static string BuildSetPlanetOwnerLuaCommand(string planetId, string newOwner)
    {
        ArgumentNullException.ThrowIfNull(planetId);
        ArgumentNullException.ThrowIfNull(newOwner);
        return $"FindPlanet(\"{planetId}\"):Change_Owner(Find_Player(\"{newOwner}\"))";
    }

    internal static string FormatDisplayName(string planetId)
    {
        ArgumentNullException.ThrowIfNull(planetId);

        if (planetId.Length == 0)
        {
            return string.Empty;
        }

        var words = planetId.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            words[i] = word.Length <= 1
                ? word.ToUpperInvariant()
                : string.Concat(
                    char.ToUpperInvariant(word[0]).ToString(),
                    word[1..].ToLowerInvariant());
        }

        return string.Join(' ', words);
    }
}
