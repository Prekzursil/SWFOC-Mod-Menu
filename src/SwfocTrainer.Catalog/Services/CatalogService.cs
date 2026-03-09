#nullable disable

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
    private const string NullOrWhitespaceMessage = "Value cannot be null or whitespace.";

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

    private readonly record struct CatalogMetadataContext(
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
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        var normalizedProfileId = profileId!.Trim();
        var profile = await _profiles.ResolveInheritedProfileAsync(normalizedProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            throw new InvalidOperationException($"Profile '{normalizedProfileId}' could not be resolved.");
        }

        var snapshot = await LoadTypedCatalogAsync(profile, cancellationToken).ConfigureAwait(false);
        return ProjectLegacyCatalog(snapshot, profile);
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        return LoadCatalogAsync(profileId, CancellationToken.None);
    }

    public async Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        var normalizedProfileId = profileId!.Trim();
        var profile = await _profiles.ResolveInheritedProfileAsync(normalizedProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            throw new InvalidOperationException($"Profile '{normalizedProfileId}' could not be resolved.");
        }

        return await LoadTypedCatalogAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        var normalizedProfileId = profileId.Trim();
        return LoadTypedCatalogAsync(normalizedProfileId, CancellationToken.None);
    }

    private async Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(TrainerProfile profile, CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var sourceProfile = profile;
        var profileId = sourceProfile.Id;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new InvalidOperationException("Profile id is required for catalog loading.");
        }

        var normalizedProfileId = profileId!.Trim();
        var catalogSources = sourceProfile.CatalogSources ?? Array.Empty<CatalogSource>();
        var prebuilt = await LoadPrebuiltCatalogAsync(normalizedProfileId, cancellationToken).ConfigureAwait(false);
        if (prebuilt.Count > 0)
        {
            return EntityCatalogSnapshot.FromLegacy(normalizedProfileId, prebuilt);
        }

        var records = new Dictionary<string, EntityCatalogRecord>(StringComparer.OrdinalIgnoreCase);
        var parsed = 0;
        foreach (var source in catalogSources)
        {
            if (!TryParseCatalogSource(normalizedProfileId, source, records))
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
            ProfileId = normalizedProfileId,
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
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (records is null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        var normalizedProfileId = profileId.Trim();
        if (string.IsNullOrWhiteSpace(source.Path) || string.IsNullOrWhiteSpace(source.Type))
        {
            return false;
        }
        var sourcePath = source.Path.Trim();
        var sourceType = source.Type.Trim();

        if (!sourceType.Equals("xml", StringComparison.OrdinalIgnoreCase))
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
                if (!TryCreateRecord(normalizedProfileId, sourcePath, element, out var parsedRecord))
                {
                    continue;
                }

                AddOrMergeRecord(records, parsedRecord);
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
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(sourcePath));
        }

        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var normalizedProfileId = profileId!.Trim();
        var normalizedSourcePath = sourcePath!.Trim();
        var sourceElement = element!;
        record = default!;
        var entityId = GetEntityId(sourceElement);
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return false;
        }

        var kind = CatalogEntityKindClassifier.ResolveKind(sourceElement.Name.LocalName, entityId);
        var textId = GetElementValue(sourceElement, "Text_ID") ?? entityId;
        var encyclopediaTextKey = GetElementValue(sourceElement, "Encyclopedia_Text");
        var rawVisualRef = VisualReferenceNames
            .Select(name => GetElementValue(sourceElement, name))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var resolvedVisualRef = ResolveVisualReference(normalizedSourcePath, rawVisualRef);
        var affiliations = ResolveAffiliations(sourceElement, kind, entityId);
        var populationValue = ParseOptionalInt(GetElementValue(sourceElement, "Population_Value"));
        var buildCostCredits = ParseOptionalInt(GetElementValue(sourceElement, "Build_Cost_Credits"));
        var dependencyRefs = CollectDependencies(sourceElement, rawVisualRef);
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
            SourceProfileId = normalizedProfileId,
            SourcePath = normalizedSourcePath,
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
                CreateMetadataContext(
                    textId,
                    encyclopediaTextKey,
                    rawVisualRef,
                    resolvedVisualRef,
                    visualState,
                    populationValue,
                    buildCostCredits))
        };

        return true;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ProjectLegacyCatalog(
        EntityCatalogSnapshot snapshot,
        TrainerProfile profile)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var sourceSnapshot = snapshot!;
        var sourceProfile = profile!;
        var entities = sourceSnapshot.Entities ?? Array.Empty<EntityCatalogRecord>();
        var actions = sourceProfile.Actions ?? new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase);

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

        var entityCatalog = BuildLegacyEntityCatalogEntries(entities);
        var typedEntityCatalog = BuildTypedEntityCatalogEntries(entities);
        var actionConstraints = actions.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray();

        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = unitCatalog,
            ["planet_catalog"] = planetCatalog,
            ["hero_catalog"] = heroCatalog,
            ["faction_catalog"] = factionCatalog,
            ["building_catalog"] = buildingCatalog,
            ["entity_catalog"] = entityCatalog,
            ["entity_catalog_typed"] = typedEntityCatalog,
            ["action_constraints"] = actionConstraints
        };
    }

    private static string BuildLegacyEntityEntry(EntityCatalogRecord record)
    {
        var sourceRecord = record;
        return $"{CatalogEntityKindClassifier.ToLegacyToken(sourceRecord.Kind)}|{sourceRecord.EntityId}";
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> LoadPrebuiltCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        var catalogRootPath = _options.CatalogRootPath;
        if (string.IsNullOrWhiteSpace(catalogRootPath))
        {
            throw new InvalidOperationException("Catalog root path is required.");
        }

        var normalizedProfileId = profileId is null ? string.Empty : profileId.Trim();
        var path = Path.Combine(catalogRootPath, normalizedProfileId, "catalog.json");
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
        if (records is null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        var incomingEntityId = incoming.EntityId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(incomingEntityId))
        {
            throw new InvalidOperationException("Incoming catalog record id is required.");
        }

        if (!records.TryGetValue(incomingEntityId, out var existing))
        {
            records[incomingEntityId] = incoming;
            return;
        }

        var existingRecord = existing!;
        var incomingRecord = incoming;
        var existingAffiliations = existingRecord.Affiliations ?? Array.Empty<string>();
        var incomingAffiliations = incomingRecord.Affiliations ?? Array.Empty<string>();
        var existingDependencies = existingRecord.DependencyRefs ?? Array.Empty<string>();
        var incomingDependencies = incomingRecord.DependencyRefs ?? Array.Empty<string>();
        var existingMetadata = existingRecord.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var incomingMetadata = incomingRecord.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var mergedAffiliations = existingAffiliations
            .Concat(incomingAffiliations)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedDependencies = existingDependencies
            .Concat(incomingDependencies)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedMetadata = existingMetadata
            .Concat(incomingMetadata)
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase);

        records[incomingEntityId] = existingRecord with
        {
            Kind = CatalogEntityKindClassifier.SelectMoreSpecificKind(existingRecord.Kind, incomingRecord.Kind),
            DisplayNameKey = ChooseValue(existingRecord.DisplayNameKey, incomingRecord.DisplayNameKey, existingRecord.EntityId) ?? existingRecord.EntityId,
            DisplayName = ChooseValue(existingRecord.DisplayName, incomingRecord.DisplayName, existingRecord.EntityId) ?? existingRecord.EntityId,
            EncyclopediaTextKey = ChooseValue(existingRecord.EncyclopediaTextKey, incomingRecord.EncyclopediaTextKey, null),
            SourcePath = ChooseValue(existingRecord.SourcePath, incomingRecord.SourcePath, null),
            Affiliations = mergedAffiliations.Length == 0 ? existingRecord.Affiliations : mergedAffiliations,
            VisualRef = ChooseValue(existingRecord.VisualRef, incomingRecord.VisualRef, null),
            VisualState = SelectVisualState(existingRecord.VisualState, incomingRecord.VisualState),
            CompatibilityState = SelectCompatibilityState(existingRecord.CompatibilityState, incomingRecord.CompatibilityState),
            PopulationValue = existingRecord.PopulationValue ?? incomingRecord.PopulationValue,
            BuildCostCredits = existingRecord.BuildCostCredits ?? incomingRecord.BuildCostCredits,
            DependencyRefs = mergedDependencies.Length == 0 ? existingRecord.DependencyRefs : mergedDependencies,
            Metadata = mergedMetadata
        };
    }

    private static string? GetEntityId(XElement element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var sourceElement = element!;
        foreach (var attributeName in EntityIdentifierAttributes)
        {
            var attributeValue = sourceElement.Attribute(attributeName)?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(attributeValue) && attributeValue.Length <= 128)
            {
                return attributeValue;
            }
        }

        return null;
    }

    private static string? GetElementValue(XElement element, string name)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(name));
        }

        var normalizedName = name!.Trim();

        var sourceElement = element!;
        var directElement = sourceElement.Elements()
            .FirstOrDefault(candidate => candidate.Name.LocalName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
        if (directElement is not null)
        {
            var value = directElement.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        var attribute = sourceElement.Attribute(normalizedName);
        var attributeValue = attribute?.Value?.Trim();
        return string.IsNullOrWhiteSpace(attributeValue) ? null : attributeValue;
    }

    private static IReadOnlyList<string> CollectDependencies(XElement element, string? visualRef)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var sourceElement = element!;
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in sourceElement.Elements())
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
            values.Remove(visualRef!);
        }

        return values
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldTreatAsDependency(string localName)
    {
        if (string.IsNullOrWhiteSpace(localName))
        {
            return false;
        }

        var normalizedLocalName = localName!.Trim();
        return DependencyNames.Any(name => name.Equals(normalizedLocalName, StringComparison.OrdinalIgnoreCase)) ||
               normalizedLocalName.StartsWith("Required_", StringComparison.OrdinalIgnoreCase) ||
               normalizedLocalName.EndsWith("_Model", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(
        XElement element,
        CatalogMetadataContext context)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var sourceElement = element!;
        var metadataContext = context;

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["elementName"] = sourceElement.Name.LocalName,
            ["displayNameKey"] = metadataContext.DisplayNameKey
        };

        if (!string.IsNullOrWhiteSpace(metadataContext.EncyclopediaTextKey))
        {
            metadata["encyclopediaTextKey"] = metadataContext.EncyclopediaTextKey;
        }

        if (!string.IsNullOrWhiteSpace(metadataContext.RawVisualRef))
        {
            metadata["visualRef"] = metadataContext.RawVisualRef;
            metadata["visualState"] = metadataContext.VisualState.ToString();
        }

        if (!string.IsNullOrWhiteSpace(metadataContext.ResolvedVisualRef))
        {
            metadata["resolvedVisualRef"] = metadataContext.ResolvedVisualRef;
        }

        if (metadataContext.PopulationValue.HasValue)
        {
            metadata["populationValue"] = metadataContext.PopulationValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (metadataContext.BuildCostCredits.HasValue)
        {
            metadata["buildCostCredits"] = metadataContext.BuildCostCredits.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static string? ResolveVisualReference(string sourcePath, string? visualRef)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(visualRef))
        {
            return null;
        }

        var normalizedVisualRef = visualRef!.Trim();

        if (Path.IsPathRooted(normalizedVisualRef))
        {
            return File.Exists(normalizedVisualRef) ? normalizedVisualRef : null;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath!);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return null;
        }

        foreach (var root in BuildCandidateRoots(sourceDirectory))
        {
            var resolved = ResolveVisualReferenceFromRoot(root, normalizedVisualRef);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static CatalogMetadataContext CreateMetadataContext(
        string displayNameKey,
        string? encyclopediaTextKey,
        string? rawVisualRef,
        string? resolvedVisualRef,
        CatalogEntityVisualState visualState,
        int? populationValue,
        int? buildCostCredits)
    {
        return new CatalogMetadataContext(
            displayNameKey,
            encyclopediaTextKey,
            rawVisualRef,
            resolvedVisualRef,
            visualState,
            populationValue,
            buildCostCredits);
    }

    private static IReadOnlyList<string> ResolveAffiliations(XElement element, CatalogEntityKind kind, string entityId)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            return Array.Empty<string>();
        }

        var sourceElement = element;
        var affiliations = ParseListValue(GetElementValue(sourceElement, "Affiliation"));
        if (affiliations.Count == 0 && kind == CatalogEntityKind.Faction)
        {
            return new[] { entityId };
        }

        return affiliations;
    }

    private static IReadOnlyList<string> BuildLegacyEntityCatalogEntries(IReadOnlyList<EntityCatalogRecord> entities)
    {
        if (entities is null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        var catalogEntities = entities;
        return catalogEntities
            .Where(static record => record.Kind is not CatalogEntityKind.Faction)
            .Select(BuildLegacyEntityEntry)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .Take(20000)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildTypedEntityCatalogEntries(IReadOnlyList<EntityCatalogRecord> entities)
    {
        if (entities is null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        var typedEntries = new List<string>(entities.Count);
        foreach (var entity in entities)
        {
            typedEntries.Add(JsonSerializer.Serialize(entity, TypedCatalogJsonOptions));
        }

        return typedEntries;
    }

    private static IReadOnlyList<string> BuildCandidateRoots(string sourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return Array.Empty<string>();
        }

        var normalizedSourceDirectory = sourceDirectory!.Trim();
        var sourceParent = Directory.GetParent(normalizedSourceDirectory);
        var sourceGrandParent = sourceParent is null
            ? null
            : Directory.GetParent(sourceParent.FullName);

        return new[]
        {
            normalizedSourceDirectory,
            sourceParent?.FullName,
            sourceGrandParent?.FullName
        }
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(static value => value!)
        .ToArray();
    }

    private static string? ResolveVisualReferenceFromRoot(string root, string visualRef)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(visualRef))
        {
            return null;
        }

        var normalizedRoot = root!.Trim();
        var normalizedVisualRef = visualRef!.Trim();
        foreach (var relativeDirectory in VisualSearchDirectories)
        {
            var candidate = string.IsNullOrWhiteSpace(relativeDirectory)
                ? Path.Combine(normalizedRoot, normalizedVisualRef)
                : Path.Combine(normalizedRoot, relativeDirectory, normalizedVisualRef);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string[] SelectEntityIds(
        IEnumerable<EntityCatalogRecord> entities,
        Func<EntityCatalogRecord, bool> predicate,
        int limit)
    {
        if (entities is null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

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

        return raw!
            .Split(new[] { ',', ';', '|', '\r', '\n', '\t' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int? ParseOptionalInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalizedRaw = raw!.Trim();
        return int.TryParse(normalizedRaw, out var value) ? value : null;
    }

    private static string? ChooseValue(string? existing, string? incoming, string? fallback)
    {
        var normalizedExisting = existing ?? string.Empty;
        var normalizedIncoming = incoming ?? string.Empty;
        var normalizedFallback = fallback ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedExisting) ||
            (!string.IsNullOrWhiteSpace(normalizedFallback) &&
             normalizedExisting.Equals(normalizedFallback, StringComparison.OrdinalIgnoreCase)))
        {
            return string.IsNullOrWhiteSpace(normalizedIncoming) ? fallback : normalizedIncoming;
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
