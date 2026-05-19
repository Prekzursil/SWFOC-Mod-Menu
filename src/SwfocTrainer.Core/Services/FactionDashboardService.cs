using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class FactionDashboardService : IFactionDashboardService
{
    private const string FactionCatalogKey = "faction_catalog";

    private readonly ICatalogService _catalog;
    private readonly ILogger<FactionDashboardService> _logger;

    public FactionDashboardService(
        ICatalogService catalog,
        ILogger<FactionDashboardService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>
    /// Builds the Lua command string for live polling of a faction's credits.
    /// </summary>
    internal static string BuildFactionQueryLuaCommand(string factionName)
    {
        ArgumentNullException.ThrowIfNull(factionName);
        return $"local p = Find_Player(\"{factionName}\"); if p then return tostring(p:Get_Credits()) else return \"0\" end";
    }

    public async Task<IReadOnlyList<FactionDashboardSnapshot>> CaptureSnapshotsAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var catalog = await _catalog.LoadCatalogAsync(profileId, cancellationToken);

        var factions = catalog.TryGetValue(FactionCatalogKey, out var factionList)
            ? factionList
            : Array.Empty<string>();

        if (factions.Count == 0)
        {
            _logger.LogDebug("No factions found in catalog for profile {ProfileId}", profileId);
            return Array.Empty<FactionDashboardSnapshot>();
        }

        var now = DateTimeOffset.UtcNow;
        var snapshots = new List<FactionDashboardSnapshot>(factions.Count);
        foreach (var faction in factions)
        {
            snapshots.Add(new FactionDashboardSnapshot(
                FactionName: faction,
                Credits: 0,
                UnitCount: 0,
                PlanetCount: 0,
                TechLevel: 0,
                CapturedAt: now));
        }

        _logger.LogInformation(
            "Captured {Count} faction dashboard snapshots for profile {ProfileId}",
            snapshots.Count, profileId);

        return snapshots.AsReadOnly();
    }
}
