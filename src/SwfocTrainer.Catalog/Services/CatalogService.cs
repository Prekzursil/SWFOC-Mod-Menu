#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    private static readonly string[] TextSearchPatterns =
    [
        "MasterTextFile*.dat",
        "MasterTextFile*.txt",
        "MasterTextFile*.xml",
        "*.dat",
        "*.txt"
    ];
    private static readonly string[] SupportedPreviewExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".ico"
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
    private static readonly Regex TextAssignmentRegex = new(
        "(?im)^\\s*([A-Z0-9_]+)\\s*(?:=|:)\\s*\"([^\"]+)\"\\s*$",
        RegexOptions.Compiled);
    private static readonly Regex TextInlineRegex = new(
        "(?im)^\\s*([A-Z0-9_]+)\\s+\"([^\"]+)\"\\s*$",
        RegexOptions.Compiled);

    private readonly record struct CatalogMetadataContext(
        string DisplayNameKey,
        string? DisplayNameSourcePath,
        string? EncyclopediaTextKey,
        string? RawVisualRef,
        string? ResolvedVisualRef,
        string? IconCachePath,
        CatalogEntityVisualState VisualState,
        int? PopulationValue,
        int? BuildCostCredits);

    private readonly CatalogOptions _options;
    private readonly IProfileRepository _profiles;
    private readonly ILogger<CatalogService> _logger;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _textLookupCache =
        new(StringComparer.OrdinalIgnoreCase);

    public CatalogService(CatalogOptions options, IProfileRepository profiles, ILogger<CatalogService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        var profileIdValue = profileId;
        if (string.IsNullOrWhiteSpace(profileIdValue))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        var normalizedProfileId = profileIdValue.Trim();
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
        var profileIdValue = profileId;
        if (string.IsNullOrWhiteSpace(profileIdValue))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        var normalizedProfileId = profileIdValue.Trim();
        var profile = await _profiles.ResolveInheritedProfileAsync(normalizedProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            throw new InvalidOperationException($"Profile '{normalizedProfileId}' could not be resolved.");
        }

        return await LoadTypedCatalogAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId)
    {
        var profileIdValue = profileId;
        if (string.IsNullOrWhiteSpace(profileIdValue))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        var normalizedProfileId = profileIdValue.Trim();
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

        var normalizedProfileId = profileId.Trim();
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
        var normalizedProfileId = NormalizeRequiredValue(profileId, nameof(profileId));
        var sourceValue = source ?? throw new ArgumentNullException(nameof(source));
        _ = records ?? throw new ArgumentNullException(nameof(records));

        var sourcePath = NormalizeNonEmpty(sourceValue.Path);
        var sourceType = NormalizeNonEmpty(sourceValue.Type);
        if (sourcePath is null || sourceType is null)
        {
            return false;
        }

        var sourcePathValue = sourcePath;
        var sourceTypeValue = sourceType;

        if (!sourceTypeValue.Equals("xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!SourceExists(sourceValue, sourcePathValue))
        {
            return false;
        }

        try
        {
            AppendXmlRecords(normalizedProfileId, sourcePathValue, records);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse catalog source: {Path}", sourcePathValue);
            return false;
        }
    }

    private bool SourceExists(CatalogSource source, string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            return true;
        }

        if (source.Required)
        {
            _logger.LogWarning("Required catalog source not found: {Path}", sourcePath);
        }

        return false;
    }

    private void AppendXmlRecords(
        string profileId,
        string sourcePath,
        IDictionary<string, EntityCatalogRecord> records)
    {
        var normalizedProfileId = NormalizeRequiredValue(profileId, nameof(profileId));
        if (sourcePath is null)
        {
            throw new ArgumentNullException(nameof(sourcePath));
        }

        var sourcePathValue = sourcePath.Trim();
        if (sourcePathValue.Length == 0)
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(sourcePath));
        }

        if (records is null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        var document = XDocument.Load(sourcePathValue, LoadOptions.None);
        foreach (var element in document.Descendants())
        {
            if (!TryCreateRecord(normalizedProfileId, sourcePathValue, element, out var parsedRecord))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(parsedRecord.EntityId))
            {
                continue;
            }

            AddOrMergeRecord(records, parsedRecord);
        }
    }

    private bool TryCreateRecord(
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
        var (resolvedDisplayName, displayNameSourcePath) = ResolveDisplayName(normalizedSourcePath, textId);
        var encyclopediaTextKey = GetElementValue(sourceElement, "Encyclopedia_Text");
        var rawVisualRef = VisualReferenceNames
            .Select(name => GetElementValue(sourceElement, name))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var resolvedVisualRef = ResolveVisualReference(normalizedSourcePath, rawVisualRef);
        var iconCachePath = ResolveIconCachePath(resolvedVisualRef);
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
            DisplayName = resolvedDisplayName,
            DisplayNameSourcePath = displayNameSourcePath,
            Kind = kind,
            SourceProfileId = normalizedProfileId,
            SourcePath = normalizedSourcePath,
            Affiliations = affiliations,
            VisualRef = resolvedVisualRef ?? rawVisualRef,
            IconCachePath = iconCachePath,
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
                    displayNameSourcePath,
                    encyclopediaTextKey,
                    rawVisualRef,
                    resolvedVisualRef,
                    iconCachePath,
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
        var profileIdValue = profileId;
        if (string.IsNullOrWhiteSpace(profileIdValue))
        {
            throw new ArgumentException(NullOrWhitespaceMessage, nameof(profileId));
        }

        var normalizedProfileId = profileIdValue.Trim();

        var catalogRootPath = _options.CatalogRootPath;
        if (string.IsNullOrWhiteSpace(catalogRootPath))
        {
            throw new InvalidOperationException("Catalog root path is required.");
        }

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

        var incomingRecord = incoming;
        var incomingEntityIdRaw = incomingRecord.EntityId;
        if (incomingEntityIdRaw is null)
        {
            throw new InvalidOperationException("Incoming catalog record id is required.");
        }

        var incomingEntityId = incomingEntityIdRaw.Trim();
        if (incomingEntityId.Length == 0)
        {
            throw new InvalidOperationException("Incoming catalog record id is required.");
        }

        if (!records.TryGetValue(incomingEntityId, out var existing))
        {
            records[incomingEntityId] = incomingRecord;
            return;
        }

        var existingRecord = existing;
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
            DisplayNameSourcePath = ChooseValue(existingRecord.DisplayNameSourcePath, incomingRecord.DisplayNameSourcePath, null),
            EncyclopediaTextKey = ChooseValue(existingRecord.EncyclopediaTextKey, incomingRecord.EncyclopediaTextKey, null),
            SourcePath = ChooseValue(existingRecord.SourcePath, incomingRecord.SourcePath, null),
            Affiliations = mergedAffiliations.Length == 0 ? existingAffiliations : mergedAffiliations,
            VisualRef = ChooseValue(existingRecord.VisualRef, incomingRecord.VisualRef, null),
            IconCachePath = ChooseValue(existingRecord.IconCachePath, incomingRecord.IconCachePath, null),
            VisualState = SelectVisualState(existingRecord.VisualState, incomingRecord.VisualState),
            CompatibilityState = SelectCompatibilityState(existingRecord.CompatibilityState, incomingRecord.CompatibilityState),
            PopulationValue = existingRecord.PopulationValue ?? incomingRecord.PopulationValue,
            BuildCostCredits = existingRecord.BuildCostCredits ?? incomingRecord.BuildCostCredits,
            DependencyRefs = mergedDependencies.Length == 0 ? existingDependencies : mergedDependencies,
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

        if (!string.IsNullOrWhiteSpace(metadataContext.DisplayNameSourcePath))
        {
            metadata["displayNameSourcePath"] = metadataContext.DisplayNameSourcePath;
        }

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

        if (!string.IsNullOrWhiteSpace(metadataContext.IconCachePath))
        {
            metadata["iconCachePath"] = metadataContext.IconCachePath;
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

    private (string DisplayName, string? SourcePath) ResolveDisplayName(string sourcePath, string textId)
    {
        var normalizedTextId = NormalizeNonEmpty(textId);
        if (normalizedTextId is null)
        {
            return (textId, null);
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return (normalizedTextId, null);
        }

        foreach (var root in BuildCandidateRoots(sourceDirectory))
        {
            foreach (var textSource in EnumerateTextSources(root))
            {
                var lookup = LoadTextLookup(textSource);
                if (lookup.TryGetValue(normalizedTextId, out var displayName) &&
                    !string.IsNullOrWhiteSpace(displayName))
                {
                    return (displayName.Trim(), textSource);
                }
            }
        }

        return (normalizedTextId, null);
    }

    private IEnumerable<string> EnumerateTextSources(string root)
    {
        foreach (var pattern in TextSearchPatterns)
        {
            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var candidate in candidates
                         .Where(static path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return candidate;
            }
        }
    }

    private IReadOnlyDictionary<string, string> LoadTextLookup(string textSourcePath)
    {
        if (_textLookupCache.TryGetValue(textSourcePath, out var cached))
        {
            return cached;
        }

        var lookup = BuildTextLookup(textSourcePath);
        _textLookupCache[textSourcePath] = lookup;
        return lookup;
    }

    private static IReadOnlyDictionary<string, string> BuildTextLookup(string textSourcePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(textSourcePath);
            if (bytes.Length == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var content in DecodeCandidateTextRepresentations(bytes))
            {
                var lookup = ParseTextLookup(content);
                if (lookup.Count > 0)
                {
                    return lookup;
                }
            }
        }
        catch
        {
            // Best effort only.
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> DecodeCandidateTextRepresentations(byte[] bytes)
    {
        yield return System.Text.Encoding.UTF8.GetString(bytes);
        yield return System.Text.Encoding.Unicode.GetString(bytes);
        yield return System.Text.Encoding.BigEndianUnicode.GetString(bytes);
        yield return System.Text.Encoding.Latin1.GetString(bytes);
    }

    private static IReadOnlyDictionary<string, string> ParseTextLookup(string content)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(content))
        {
            return lookup;
        }

        AppendRegexMatches(lookup, TextAssignmentRegex.Matches(content));
        AppendRegexMatches(lookup, TextInlineRegex.Matches(content));
        return lookup;
    }

    private static void AppendRegexMatches(
        IDictionary<string, string> lookup,
        MatchCollection matches)
    {
        foreach (Match match in matches)
        {
            if (!match.Success || match.Groups.Count < 3)
            {
                continue;
            }

            var key = NormalizeNonEmpty(match.Groups[1].Value);
            var value = NormalizeNonEmpty(match.Groups[2].Value);
            if (key is null || value is null)
            {
                continue;
            }

            lookup[key] = value;
        }
    }

    private static string? ResolveIconCachePath(string? resolvedVisualRef)
    {
        var normalizedPath = NormalizeNonEmpty(resolvedVisualRef);
        if (normalizedPath is null)
        {
            return null;
        }

        var extension = Path.GetExtension(normalizedPath);
        return SupportedPreviewExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? normalizedPath
            : null;
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
        string? displayNameSourcePath,
        string? encyclopediaTextKey,
        string? rawVisualRef,
        string? resolvedVisualRef,
        string? iconCachePath,
        CatalogEntityVisualState visualState,
        int? populationValue,
        int? buildCostCredits)
    {
        return new CatalogMetadataContext(
            displayNameKey,
            displayNameSourcePath,
            encyclopediaTextKey,
            rawVisualRef,
            resolvedVisualRef,
            iconCachePath,
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

        var sourceEntities = entities;
        var typedEntries = new List<string>(sourceEntities.Count);
        foreach (var entity in sourceEntities)
        {
            var entityValue = entity;
            typedEntries.Add(JsonSerializer.Serialize(entityValue, TypedCatalogJsonOptions));
        }

        return typedEntries;
    }

    private static IReadOnlyList<string> BuildCandidateRoots(string sourceDirectory)
    {
        var sourceDirectoryValue = sourceDirectory;
        if (string.IsNullOrWhiteSpace(sourceDirectoryValue))
        {
            return Array.Empty<string>();
        }

        var normalizedSourceDirectory = sourceDirectoryValue.Trim();

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
        var rootValue = root;
        var visualRefValue = visualRef;
        if (string.IsNullOrWhiteSpace(rootValue) || string.IsNullOrWhiteSpace(visualRefValue))
        {
            return null;
        }

        var normalizedRoot = rootValue.Trim();
        var normalizedVisualRef = visualRefValue.Trim();

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

        if (limit <= 0)
        {
            return Array.Empty<string>();
        }

        var sourceEntities = entities;
        var sourcePredicate = predicate;

        var distinctEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedEntityIds = new List<string>();
        foreach (var entity in sourceEntities)
        {
            var entityValue = entity;
            if (!sourcePredicate(entityValue))
            {
                continue;
            }

            var entityId = NormalizeNonEmpty(entityValue.EntityId);
            if (entityId is null)
            {
                continue;
            }

            if (distinctEntityIds.Add(entityId))
            {
                selectedEntityIds.Add(entityId);
            }
        }

        selectedEntityIds.Sort(StringComparer.OrdinalIgnoreCase);
        var cappedCount = Math.Min(limit, selectedEntityIds.Count);
        if (cappedCount == selectedEntityIds.Count)
        {
            return selectedEntityIds.ToArray();
        }

        var limitedEntityIds = new string[cappedCount];
        for (var index = 0; index < cappedCount; index++)
        {
            limitedEntityIds[index] = selectedEntityIds[index];
        }

        return limitedEntityIds;
    }

    private static CatalogEntityVisualState ResolveVisualState(string? rawVisualRef, string? resolvedVisualRef)
    {
        if (string.IsNullOrWhiteSpace(rawVisualRef))
        {
            return CatalogEntityVisualState.Unknown;
        }

        var normalizedResolvedVisualRef = resolvedVisualRef ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedResolvedVisualRef)
            ? CatalogEntityVisualState.Missing
            : CatalogEntityVisualState.Resolved;
    }

    private static string? NormalizeNonEmpty(string? value)
    {
        var safeValue = value ?? string.Empty;
        if (safeValue.Length == 0)
        {
            return null;
        }

        var trimmed = safeValue.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string NormalizeRequiredValue(string? value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentException(NullOrWhitespaceMessage, paramName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException(NullOrWhitespaceMessage, paramName);
        }

        return trimmed;
    }

    private static IReadOnlyList<string> ParseListValue(string? raw)
    {
        var rawValue = raw;
        if (rawValue is null)
        {
            return Array.Empty<string>();
        }

        var trimmedRaw = rawValue.Trim();
        if (trimmedRaw.Length == 0)
        {
            return Array.Empty<string>();
        }

        return trimmedRaw
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
