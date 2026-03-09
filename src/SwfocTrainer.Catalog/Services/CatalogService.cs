using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Catalog.Services;

public sealed class CatalogService : ICatalogService
{
    private static readonly string[] EntityIdentifierAttributes = ["Name", "ID", "Id", "Object_Name", "Type"];
    private static readonly string[] VisualReferenceNames = ["Icon_Name", "IconName", "Portrait"];
    private static readonly string[] VisualSearchDirectories =
    [
        string.Empty,
        "Art",
        Path.Combine("Art", "Textures"),
        Path.Combine("Art", "Textures", "UI"),
        Path.Combine("Art", "Textures", "Gui"),
        Path.Combine("Textures"),
        Path.Combine("Textures", "UI")
    ];
    private static readonly string[] DependencyNames =
    [
        "Required_Structures",
        "Required_Prerequisites",
        "Required_Units",
        "Required_Planets",
        "Company_Unit",
        "Squadron_Units",
        "Variant_Of",
        "Model_Name",
        "Space_Model",
        "Land_Model",
        "Tactical_Override_Model"
    ];
    private static readonly JsonSerializerOptions TypedCatalogJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

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
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        var snapshot = await LoadTypedCatalogAsync(profile, cancellationToken).ConfigureAwait(false);
        return ProjectLegacyCatalog(snapshot, profile);
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId)
    {
        return LoadCatalogAsync(profileId, CancellationToken.None);
    }

    public async Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        return await LoadTypedCatalogAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId)
    {
        return LoadTypedCatalogAsync(profileId, CancellationToken.None);
    }

    private async Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(TrainerProfile profile, CancellationToken cancellationToken)
    {
        var prebuilt = await LoadPrebuiltCatalogAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (prebuilt.Count > 0)
        {
            return EntityCatalogSnapshot.FromLegacy(profile.Id, prebuilt);
        }

        var records = new Dictionary<string, EntityCatalogRecord>(StringComparer.OrdinalIgnoreCase);
        var parsed = 0;
        foreach (var source in profile.CatalogSources)
        {
            if (!TryParseCatalogSource(profile.Id, source, records))
            {
                continue;
            }

            parsed++;
            if (parsed >= _options.MaxParsedXmlFiles)
            {
                break;
            }
        }

        return new EntityCatalogSnapshot
        {
            ProfileId = profile.Id,
            Entities = records.Values
                .OrderBy(static record => record.EntityId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private bool TryParseCatalogSource(
        string profileId,
        CatalogSource source,
        IDictionary<string, EntityCatalogRecord> records)
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

        try
        {
            var document = XDocument.Load(source.Path, LoadOptions.None);
            foreach (var element in document.Descendants())
            {
                if (!TryCreateRecord(profileId, source.Path, element, out var record))
                {
                    continue;
                }

                AddOrMergeRecord(records, record);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse catalog source: {Path}", source.Path);
            return false;
        }
    }

    private static bool TryCreateRecord(
        string profileId,
        string sourcePath,
        XElement element,
        out EntityCatalogRecord record)
    {
        record = default!;
        var entityId = GetEntityId(element);
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return false;
        }

        var kind = CatalogEntityKindClassifier.ResolveKind(element.Name.LocalName, entityId);
        var textId = GetElementValue(element, "Text_ID") ?? entityId;
        var encyclopediaTextKey = GetElementValue(element, "Encyclopedia_Text");
        var rawVisualRef = VisualReferenceNames
            .Select(name => GetElementValue(element, name))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var resolvedVisualRef = ResolveVisualReference(sourcePath, rawVisualRef);
        var affiliations = ParseListValue(GetElementValue(element, "Affiliation"));
        if (affiliations.Count == 0 && kind == CatalogEntityKind.Faction)
        {
            affiliations = new[] { entityId };
        }

        var populationValue = ParseOptionalInt(GetElementValue(element, "Population_Value"));
        var buildCostCredits = ParseOptionalInt(GetElementValue(element, "Build_Cost_Credits"));
        var dependencyRefs = CollectDependencies(element, rawVisualRef);
        var visualState = ResolveVisualState(rawVisualRef, resolvedVisualRef);
        var compatibilityState = visualState == CatalogEntityVisualState.Missing
            ? CatalogEntityCompatibilityState.Blocked
            : CatalogEntityCompatibilityState.Unknown;

        record = new EntityCatalogRecord
        {
            EntityId = entityId,
            DisplayNameKey = textId,
            DisplayName = textId,
            Kind = kind,
            SourceProfileId = profileId,
            SourcePath = sourcePath,
            Affiliations = affiliations,
            VisualRef = resolvedVisualRef ?? rawVisualRef,
            DependencyRefs = dependencyRefs,
            VisualState = visualState,
            CompatibilityState = compatibilityState,
            PopulationValue = populationValue,
            BuildCostCredits = buildCostCredits,
            EncyclopediaTextKey = encyclopediaTextKey,
            Metadata = BuildMetadata(
                element,
                textId,
                encyclopediaTextKey,
                rawVisualRef,
                resolvedVisualRef,
                visualState,
                populationValue,
                buildCostCredits)
        };

        return true;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ProjectLegacyCatalog(
        EntityCatalogSnapshot snapshot,
        TrainerProfile profile)
    {
        var unitCatalog = snapshot.Entities
            .Where(static record => record.Kind is not CatalogEntityKind.Planet and not CatalogEntityKind.Faction)
            .Select(static record => record.EntityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static entityId => entityId, StringComparer.OrdinalIgnoreCase)
            .Take(10000)
            .ToArray();

        var planetCatalog = snapshot.Entities
            .Where(static record => record.Kind == CatalogEntityKind.Planet)
            .Select(static record => record.EntityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static entityId => entityId, StringComparer.OrdinalIgnoreCase)
            .Take(2000)
            .ToArray();

        var heroCatalog = snapshot.Entities
            .Where(static record => record.Kind == CatalogEntityKind.Hero)
            .Select(static record => record.EntityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static entityId => entityId, StringComparer.OrdinalIgnoreCase)
            .Take(2000)
            .ToArray();

        var factionCatalog = snapshot.Entities
            .Where(static record => record.Kind == CatalogEntityKind.Faction)
            .Select(static record => record.EntityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static entityId => entityId, StringComparer.OrdinalIgnoreCase)
            .Take(300)
            .ToArray();

        var buildingCatalog = snapshot.Entities
            .Where(static record => record.Kind is CatalogEntityKind.Building or CatalogEntityKind.SpaceStructure)
            .Select(static record => record.EntityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static entityId => entityId, StringComparer.OrdinalIgnoreCase)
            .Take(4000)
            .ToArray();

        var entityCatalog = snapshot.Entities
            .Where(static record => record.Kind is not CatalogEntityKind.Faction)
            .Select(BuildLegacyEntityEntry)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .Take(20000)
            .ToArray();
        var typedEntityCatalog = snapshot.Entities
            .Select(static record => JsonSerializer.Serialize(record, TypedCatalogJsonOptions))
            .ToArray();

        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = unitCatalog,
            ["planet_catalog"] = planetCatalog,
            ["hero_catalog"] = heroCatalog,
            ["faction_catalog"] = factionCatalog,
            ["building_catalog"] = buildingCatalog,
            ["entity_catalog"] = entityCatalog,
            ["entity_catalog_typed"] = typedEntityCatalog,
            ["action_constraints"] = profile.Actions.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static string BuildLegacyEntityEntry(EntityCatalogRecord record)
    {
        return $"{CatalogEntityKindClassifier.ToLegacyToken(record.Kind)}|{record.EntityId}";
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> LoadPrebuiltCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_options.CatalogRootPath, profileId, "catalog.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<Dictionary<string, string[]>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? new Dictionary<string, string[]>();

        return catalog.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AddOrMergeRecord(
        IDictionary<string, EntityCatalogRecord> records,
        EntityCatalogRecord incoming)
    {
        if (!records.TryGetValue(incoming.EntityId, out var existing))
        {
            records[incoming.EntityId] = incoming;
            return;
        }

        var mergedAffiliations = existing.Affiliations
            .Concat(incoming.Affiliations)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedDependencies = existing.DependencyRefs
            .Concat(incoming.DependencyRefs)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedMetadata = existing.Metadata
            .Concat(incoming.Metadata)
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase);

        records[incoming.EntityId] = existing with
        {
            Kind = CatalogEntityKindClassifier.SelectMoreSpecificKind(existing.Kind, incoming.Kind),
            DisplayNameKey = ChooseValue(existing.DisplayNameKey, incoming.DisplayNameKey, existing.EntityId) ?? existing.EntityId,
            DisplayName = ChooseValue(existing.DisplayName, incoming.DisplayName, existing.EntityId) ?? existing.EntityId,
            EncyclopediaTextKey = ChooseValue(existing.EncyclopediaTextKey, incoming.EncyclopediaTextKey, null),
            SourcePath = ChooseValue(existing.SourcePath, incoming.SourcePath, null),
            Affiliations = mergedAffiliations.Length == 0 ? existing.Affiliations : mergedAffiliations,
            VisualRef = ChooseValue(existing.VisualRef, incoming.VisualRef, null),
            VisualState = SelectVisualState(existing.VisualState, incoming.VisualState),
            CompatibilityState = SelectCompatibilityState(existing.CompatibilityState, incoming.CompatibilityState),
            PopulationValue = existing.PopulationValue ?? incoming.PopulationValue,
            BuildCostCredits = existing.BuildCostCredits ?? incoming.BuildCostCredits,
            DependencyRefs = mergedDependencies.Length == 0 ? existing.DependencyRefs : mergedDependencies,
            Metadata = mergedMetadata
        };
    }

    private static string? GetEntityId(XElement element)
    {
        foreach (var attributeName in EntityIdentifierAttributes)
        {
            var attributeValue = element.Attribute(attributeName)?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(attributeValue) && attributeValue.Length <= 128)
            {
                return attributeValue;
            }
        }

        return null;
    }

    private static string? GetElementValue(XElement element, string name)
    {
        var directElement = element.Elements()
            .FirstOrDefault(candidate => candidate.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (directElement is not null)
        {
            var value = directElement.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        var attribute = element.Attribute(name);
        var attributeValue = attribute?.Value?.Trim();
        return string.IsNullOrWhiteSpace(attributeValue) ? null : attributeValue;
    }

    private static IReadOnlyList<string> CollectDependencies(XElement element, string? visualRef)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in element.Elements())
        {
            if (!ShouldTreatAsDependency(child.Name.LocalName))
            {
                continue;
            }

            foreach (var value in ParseListValue(child.Value))
            {
                values.Add(value);
            }
        }

        if (!string.IsNullOrWhiteSpace(visualRef))
        {
            values.Remove(visualRef);
        }

        return values
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldTreatAsDependency(string localName)
    {
        return DependencyNames.Any(name => name.Equals(localName, StringComparison.OrdinalIgnoreCase)) ||
               localName.StartsWith("Required_", StringComparison.OrdinalIgnoreCase) ||
               localName.EndsWith("_Model", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(
        XElement element,
        string displayNameKey,
        string? encyclopediaTextKey,
        string? rawVisualRef,
        string? resolvedVisualRef,
        CatalogEntityVisualState visualState,
        int? populationValue,
        int? buildCostCredits)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["elementName"] = element.Name.LocalName,
            ["displayNameKey"] = displayNameKey
        };

        if (!string.IsNullOrWhiteSpace(encyclopediaTextKey))
        {
            metadata["encyclopediaTextKey"] = encyclopediaTextKey;
        }

        if (!string.IsNullOrWhiteSpace(rawVisualRef))
        {
            metadata["visualRef"] = rawVisualRef;
            metadata["visualState"] = visualState.ToString();
        }

        if (!string.IsNullOrWhiteSpace(resolvedVisualRef))
        {
            metadata["resolvedVisualRef"] = resolvedVisualRef;
        }

        if (populationValue.HasValue)
        {
            metadata["populationValue"] = populationValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (buildCostCredits.HasValue)
        {
            metadata["buildCostCredits"] = buildCostCredits.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static string? ResolveVisualReference(string sourcePath, string? visualRef)
    {
        if (string.IsNullOrWhiteSpace(visualRef))
        {
            return null;
        }

        if (Path.IsPathRooted(visualRef))
        {
            return File.Exists(visualRef) ? visualRef : null;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return null;
        }

        var sourceParent = Directory.GetParent(sourceDirectory);
        var sourceGrandParent = sourceParent is null
            ? null
            : Directory.GetParent(sourceParent.FullName);
        var candidateRoots = new[]
        {
            sourceDirectory,
            sourceParent?.FullName,
            sourceGrandParent?.FullName
        }
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        foreach (var root in candidateRoots)
        {
            foreach (var relativeDirectory in VisualSearchDirectories)
            {
                var candidate = string.IsNullOrWhiteSpace(relativeDirectory)
                    ? Path.Combine(root!, visualRef)
                    : Path.Combine(root!, relativeDirectory, visualRef);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static CatalogEntityVisualState ResolveVisualState(string? rawVisualRef, string? resolvedVisualRef)
    {
        if (string.IsNullOrWhiteSpace(rawVisualRef))
        {
            return CatalogEntityVisualState.Unknown;
        }

        return string.IsNullOrWhiteSpace(resolvedVisualRef)
            ? CatalogEntityVisualState.Missing
            : CatalogEntityVisualState.Resolved;
    }

    private static IReadOnlyList<string> ParseListValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(new[] { ',', ';', '|', '\r', '\n', '\t' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int? ParseOptionalInt(string? raw)
    {
        return int.TryParse(raw, out var value) ? value : null;
    }

    private static string? ChooseValue(string? existing, string? incoming, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(existing) || (!string.IsNullOrWhiteSpace(fallback) && existing.Equals(fallback, StringComparison.OrdinalIgnoreCase)))
        {
            return string.IsNullOrWhiteSpace(incoming) ? fallback : incoming;
        }

        return existing;
    }

    private static CatalogEntityVisualState SelectVisualState(
        CatalogEntityVisualState existing,
        CatalogEntityVisualState incoming)
    {
        return incoming > existing ? incoming : existing;
    }

    private static CatalogEntityCompatibilityState SelectCompatibilityState(
        CatalogEntityCompatibilityState existing,
        CatalogEntityCompatibilityState incoming)
    {
        return incoming > existing ? incoming : existing;
    }
}
