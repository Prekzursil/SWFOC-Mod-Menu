using System.Globalization;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class RosterBrowserService : IRosterBrowserService
{
    private readonly ICatalogService _catalog;
    private readonly ILogger<RosterBrowserService> _logger;

    public RosterBrowserService(ICatalogService catalog, ILogger<RosterBrowserService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RosterBrowserEntry>> LoadRosterAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var catalog = await _catalog.LoadCatalogAsync(profileId, cancellationToken);
        var entries = new List<RosterBrowserEntry>();

        foreach (var (category, entityIds) in catalog)
        {
            var kind = ClassifyCategory(category);
            foreach (var entityId in entityIds)
            {
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    continue;
                }

                entries.Add(new RosterBrowserEntry(
                    EntityId: entityId,
                    DisplayName: FormatDisplayName(entityId),
                    Faction: InferFaction(entityId),
                    Category: category,
                    Kind: kind));
            }
        }

        _logger.LogInformation(
            "Loaded {Count} roster entries for profile {ProfileId}",
            entries.Count,
            profileId);

        return entries.AsReadOnly();
    }

    internal static RosterEntityKind ClassifyCategory(string category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return category.ToUpperInvariant() switch
        {
            "HEROES" or "HERO" or "HERO_CATALOG" => RosterEntityKind.Hero,
            "BUILDINGS" or "BUILDING" or "BUILDING_CATALOG"
                or "STRUCTURES" or "STRUCTURE" => RosterEntityKind.Building,
            "UNITS" or "UNIT" or "UNIT_CATALOG" => RosterEntityKind.Unit,
            _ => RosterEntityKind.Unknown
        };
    }

    internal static string FormatDisplayName(string entityId)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        if (entityId.Length == 0)
        {
            return string.Empty;
        }

        var words = entityId.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = ToTitleCase(words[i]);
        }

        return string.Join(' ', words);
    }

    internal static string InferFaction(string entityId)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        var upper = entityId.ToUpperInvariant();

        if (upper.StartsWith("EMPIRE_", StringComparison.Ordinal)
            || upper.StartsWith("IMPERIAL_", StringComparison.Ordinal))
        {
            return "Empire";
        }

        if (upper.StartsWith("REBEL_", StringComparison.Ordinal))
        {
            return "Rebel";
        }

        if (upper.StartsWith("UNDERWORLD_", StringComparison.Ordinal))
        {
            return "Underworld";
        }

        if (upper.StartsWith("REPUBLIC_", StringComparison.Ordinal))
        {
            return "Republic";
        }

        if (upper.StartsWith("CIS_", StringComparison.Ordinal))
        {
            return "CIS";
        }

        return "Unknown";
    }

    /// <summary>
    /// Builds the Lua command string for live discovery of entity types by category.
    /// </summary>
    internal static string BuildDiscoverTypesLuaCommand(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return $"local t = Find_Object_Type(\"{category}\"); if t then local units = Find_All_Objects_Of_Type(t); if units then for _, u in pairs(units) do if TestValid(u) and u:Get_Type() and u:Get_Type():Get_Name() then SWFOC_Log(u:Get_Type():Get_Name()) end end end end";
    }

    private static string ToTitleCase(string word)
    {
        if (word.Length <= 1)
        {
            return word.ToUpperInvariant();
        }

        return string.Concat(
            char.ToUpper(word[0], CultureInfo.InvariantCulture).ToString(),
            word[1..].ToLower(CultureInfo.InvariantCulture));
    }
}
