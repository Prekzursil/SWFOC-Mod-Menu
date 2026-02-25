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

        var prebuilt = await LoadPrebuiltCatalogAsync(profileId, cancellationToken);
        if (prebuilt.Count > 0)
        {
            foreach (var kv in prebuilt)
            {
                result[kv.Key] = kv.Value;
            }

            return result;
        }

        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken);
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
        result["action_constraints"] = profile.Actions.Keys.OrderBy(x => x).ToArray();

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
}
