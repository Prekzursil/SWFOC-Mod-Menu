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
    private const string ArtDirectory = "Art";
    private const string TexturesDirectory = "Textures";
    private const string UiDirectory = "UI";
    private const string GuiDirectory = "Gui";

    private static readonly string[] EntityIdentifierAttributes = ["Name", "ID", "Id", "Object_Name", "Type"];
    private static readonly string[] VisualReferenceNames = ["Icon_Name", "IconName", "Portrait"];
    private static readonly string[] VisualSearchDirectories =
    [
        string.Empty,
        ArtDirectory,
        Path.Combine(ArtDirectory, TexturesDirectory),
        Path.Combine(ArtDirectory, TexturesDirectory, UiDirectory),
        Path.Combine(ArtDirectory, TexturesDirectory, GuiDirectory),
        TexturesDirectory,
        Path.Combine(TexturesDirectory, UiDirectory)
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

    private sealed record CatalogMetadataContext(
        string DisplayNameKey,
        string? EncyclopediaTextKey,
        string? RawVisualRef,
        string? ResolvedVisualRef,
        CatalogEntityVisualState VisualState,
        int? PopulationValue,
        int? BuildCostCredits);

    private readonly CatalogOptions _options;
    private readonly IProfileRepository _profiles;
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(CatalogOptions options, IProfileRepository profiles, ILogger<CatalogService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(profile);
        var snapshot = await LoadTypedCatalogAsync(profile, cancellationToken).ConfigureAwait(false);
        return ProjectLegacyCatalog(snapshot, profile);
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId)
    {
        return LoadCatalogAsync(profileId, CancellationToken.None);
    }

    public async Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(profile);
        return await LoadTypedCatalogAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId)
    {
        return LoadTypedCatalogAsync(profileId, CancellationToken.None);
    }

    private async Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(TrainerProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var profileId = profile.Id;
        var catalogSources = profile.CatalogSources ?? Array.Empty<CatalogSource>();
        var prebuilt = await LoadPrebuiltCatalogAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (prebuilt.Count > 0)
        {
            return EntityCatalogSnapshot.FromLegacy(profileId, prebuilt);
        }

        var records = new Dictionary<string, EntityCatalogRecord>(StringComparer.OrdinalIgnoreCase);
        var parsed = 0;
        foreach (var source in catalogSources)
        {
            if (!TryParseCatalogSource(profileId, source, records))
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
            ProfileId = profileId,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(records);

        var sourcePath = source.Path;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        if (!source.Type.Equals("xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(sourcePath))
        {
            if (source.Required)
            {
                _logger.LogWarning("Required catalog source not found: {Path}", sourcePath);
            }

            return false;
        }

        try
        {
            var document = XDocument.Load(sourcePath, LoadOptions.None);
            foreach (var element in document.Descendants())
            {
                if (!TryCreateRecord(profileId, sourcePath, element, out var record))
                {
                    continue;
                }

                AddOrMergeRecord(records, record);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse catalog source: {Path}", sourcePath);
            return false;
        }
    }

    private static bool TryCreateRecord(
        string profileId,
        string sourcePath,
        XElement element,
        out EntityCatalogRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(element);

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

        var metadataContext = new CatalogMetadataContext(
            textId,
            encyclopediaTextKey,
            rawVisualRef,
            resolvedVisualRef,
            visualState,
            populationValue,
            buildCostCredits);

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
            Metadata = BuildMetadata(element, metadataContext)
        };

        return true;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ProjectLegacyCatalog(
        EntityCatalogSnapshot snapshot,
        TrainerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(profile);

        var entities = snapshot.Entities ?? Array.Empty<EntityCatalogRecord>();
        var actions = profile.Actions ?? new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase);

        var unitCatalog = SelectEntityIds(
            entities,
            static record => record.Kind is not CatalogEntityKind.Planet and not CatalogEntityKind.Faction,
            10000);

        var planetCatalog = SelectEntityIds(
            entities,
            static record => record.Kind == CatalogEntityKind.Planet,
            2000);

        var heroCatalog = SelectEntityIds(
            entities,
            static record => record.Kind == CatalogEntityKind.Hero,
            2000);

        var factionCatalog = SelectEntityIds(
            entities,
            static record => record.Kind == CatalogEntityKind.Faction,
            300);

        var buildingCatalog = SelectEntityIds(
            entities,
            static record => record.Kind is CatalogEntityKind.Building or CatalogEntityKind.SpaceStructure,
            4000);

        var entityCatalog = entities
            .Where(static record => record.Kind is not CatalogEntityKind.Faction)
            .Select(BuildLegacyEntityEntry)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .Take(20000)
            .ToArray();
        var typedEntityCatalog = entities
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
            ["action_constraints"] = actions.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static string BuildLegacyEntityEntry(EntityCatalogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return $"{CatalogEntityKindClassifier.ToLegacyToken(record.Kind)}|{record.EntityId}";
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> LoadPrebuiltCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

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
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(incoming);

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
        ArgumentNullException.ThrowIfNull(element);

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
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

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
        ArgumentNullException.ThrowIfNull(element);

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
        CatalogMetadataContext context)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["elementName"] = element.Name.LocalName,
            ["displayNameKey"] = context.DisplayNameKey
        };

        if (!string.IsNullOrWhiteSpace(context.EncyclopediaTextKey))
        {
            metadata["encyclopediaTextKey"] = context.EncyclopediaTextKey;
        }

        if (!string.IsNullOrWhiteSpace(context.RawVisualRef))
        {
            metadata["visualRef"] = context.RawVisualRef;
            metadata["visualState"] = context.VisualState.ToString();
        }

        if (!string.IsNullOrWhiteSpace(context.ResolvedVisualRef))
        {
            metadata["resolvedVisualRef"] = context.ResolvedVisualRef;
        }

        if (context.PopulationValue.HasValue)
        {
            metadata["populationValue"] = context.PopulationValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (context.BuildCostCredits.HasValue)
        {
            metadata["buildCostCredits"] = context.BuildCostCredits.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static string? ResolveVisualReference(string sourcePath, string? visualRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

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

    private static string[] SelectEntityIds(
        IEnumerable<EntityCatalogRecord> entities,
        Func<EntityCatalogRecord, bool> predicate,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(predicate);

        return entities
            .Where(predicate)
            .Select(static record => record.EntityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static entityId => entityId, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
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
