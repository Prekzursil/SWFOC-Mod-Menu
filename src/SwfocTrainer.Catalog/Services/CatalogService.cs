using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Catalog.Parsing;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Catalog.Services;

public sealed class CatalogService : ICatalogService
{
    private readonly CatalogOptions _options;
    private readonly IProfileRepository _profiles;
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(CatalogOptions options, IProfileRepository profiles, ILogger<CatalogService> logger)
    {
        _options = options;
        _profiles = profiles;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken);

        var prebuilt = await LoadPrebuiltCatalogAsync(profileId, cancellationToken);
        if (prebuilt.Count > 0)
        {
            foreach (var kv in prebuilt)
            {
                result[kv.Key] = kv.Value;
            }

            EnsureDerivedCatalogs(result, profile);
            return result;
        }

        var unitList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planetList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var heroList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var factionList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var parsed = 0;
        foreach (var source in profile.CatalogSources)
        {
            if (!TryParseCatalogSource(source, unitList, planetList, heroList, factionList))
            {
                continue;
            }

            parsed++;
            if (parsed >= _options.MaxParsedXmlFiles)
            {
                break;
            }
        }

        result["unit_catalog"] = unitList.OrderBy(x => x).Take(10000).ToArray();
        result["planet_catalog"] = planetList.OrderBy(x => x).Take(2000).ToArray();
        result["hero_catalog"] = heroList.OrderBy(x => x).Take(2000).ToArray();
        result["faction_catalog"] = factionList.OrderBy(x => x).Take(300).ToArray();
        EnsureDerivedCatalogs(result, profile);

        return result;
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId)
    {
        return LoadCatalogAsync(profileId, CancellationToken.None);
    }

    private bool TryParseCatalogSource(
        CatalogSource source,
        ISet<string> unitList,
        ISet<string> planetList,
        ISet<string> heroList,
        ISet<string> factionList)
    {
        if (!source.Type.Equals("xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(source.Path))
        {
            if (source.Required)
            {
                _logger.LogWarning("Required catalog source not found: {Path}", source.Path);
            }

            return false;
        }

        foreach (var name in XmlObjectExtractor.ExtractObjectNames(source.Path))
        {
            AddCatalogName(name, unitList, planetList, heroList, factionList);
        }

        return true;
    }

    private static void AddCatalogName(
        string name,
        ISet<string> unitList,
        ISet<string> planetList,
        ISet<string> heroList,
        ISet<string> factionList)
    {
        unitList.Add(name);

        if (name.Contains("PLANET", StringComparison.OrdinalIgnoreCase))
        {
            planetList.Add(name);
        }

        if (IsHeroName(name))
        {
            heroList.Add(name);
        }

        if (IsFactionName(name))
        {
            factionList.Add(name);
        }
    }

    private static bool IsHeroName(string name)
    {
        return name.Contains("HERO", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("VADER", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PALPATINE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFactionName(string name)
    {
        return name.Contains("EMPIRE", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("REBEL", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("UNDERWORLD", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("CIS", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> LoadPrebuiltCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_options.CatalogRootPath, profileId, "catalog.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<Dictionary<string, string[]>>(stream, cancellationToken: cancellationToken)
            ?? new Dictionary<string, string[]>();

        return catalog.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureDerivedCatalogs(
        IDictionary<string, IReadOnlyList<string>> catalog,
        TrainerProfile profile)
    {
        var units = GetCatalogSet(catalog, "unit_catalog");
        var planets = GetCatalogSet(catalog, "planet_catalog");
        var heroes = GetCatalogSet(catalog, "hero_catalog");
        var buildings = GetCatalogSet(catalog, "building_catalog");

        foreach (var unit in units)
        {
            if (IsBuildingName(unit))
            {
                buildings.Add(unit);
            }
        }

        var entities = GetCatalogSet(catalog, "entity_catalog");
        foreach (var unit in units)
        {
            entities.Add($"Unit|{unit}");
        }

        foreach (var building in buildings)
        {
            entities.Add($"Building|{building}");
        }

        foreach (var planet in planets)
        {
            entities.Add($"Planet|{planet}");
        }

        foreach (var hero in heroes)
        {
            entities.Add($"Hero|{hero}");
        }

        catalog["building_catalog"] = buildings.OrderBy(x => x).Take(4000).ToArray();
        catalog["entity_catalog"] = entities.OrderBy(x => x).Take(20000).ToArray();
        catalog["action_constraints"] = profile.Actions.Keys.OrderBy(x => x).ToArray();
    }

    private static HashSet<string> GetCatalogSet(
        IDictionary<string, IReadOnlyList<string>> catalog,
        string key)
    {
        if (!catalog.TryGetValue(key, out var values) || values is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBuildingName(string name)
    {
        return name.Contains("BARRACK", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("FACTORY", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("BASE", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("SHIPYARD", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("YARD", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("STATION", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("STAR_BASE", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("STARBASE", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PLATFORM", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("MINE", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("TURRET", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("DEFENSE", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ACADEMY", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("OUTPOST", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("REFINERY", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PALACE", StringComparison.OrdinalIgnoreCase);
    }
}
