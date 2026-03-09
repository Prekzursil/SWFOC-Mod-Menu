#nullable enable

namespace SwfocTrainer.Core.Models;

public enum CatalogEntityKind
{
    Unknown = 0,
    Unit,
    Hero,
    Building,
    SpaceStructure,
    AbilityCarrier,
    Planet,
    Faction
}

public enum CatalogEntityVisualState
{
    Unknown = 0,
    Resolved,
    Missing
}

public enum CatalogEntityCompatibilityState
{
    Unknown = 0,
    Native,
    Compatible,
    RequiresTransplant,
    Blocked
}

public readonly record struct EntityCatalogRecord
{
    public EntityCatalogRecord()
    {
    }

    public string EntityId { get; init; } = string.Empty;

    public string DisplayNameKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public CatalogEntityKind Kind { get; init; }

    public string SourceProfileId { get; init; } = string.Empty;

    public string? SourcePath { get; init; }

    public IReadOnlyList<string> Affiliations { get; init; } = Array.Empty<string>();

    public string? VisualRef { get; init; }

    public IReadOnlyList<string> DependencyRefs { get; init; } = Array.Empty<string>();

    public CatalogEntityVisualState VisualState { get; init; }

    public CatalogEntityCompatibilityState CompatibilityState { get; init; }

    public int? PopulationValue { get; init; }

    public int? BuildCostCredits { get; init; }

    public string? EncyclopediaTextKey { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string DefaultAffiliation => Affiliations.FirstOrDefault() ?? string.Empty;
}

public sealed record EntityCatalogSnapshot
{
    public string ProfileId { get; init; } = string.Empty;

    public IReadOnlyList<EntityCatalogRecord> Entities { get; init; } = Array.Empty<EntityCatalogRecord>();

    public static EntityCatalogSnapshot FromLegacy(
        string profileId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var records = new Dictionary<string, EntityCatalogRecord>(StringComparer.OrdinalIgnoreCase);

        AddLegacyCategory(records, profileId, catalog, "unit_catalog", CatalogEntityKind.Unit);
        AddLegacyCategory(records, profileId, catalog, "planet_catalog", CatalogEntityKind.Planet);
        AddLegacyCategory(records, profileId, catalog, "hero_catalog", CatalogEntityKind.Hero);
        AddLegacyCategory(records, profileId, catalog, "faction_catalog", CatalogEntityKind.Faction);
        AddLegacyCategory(records, profileId, catalog, "building_catalog", CatalogEntityKind.Building);

        if (catalog.TryGetValue("entity_catalog", out var entityEntries) && entityEntries is not null)
        {
            foreach (var rawEntry in entityEntries)
            {
                if (!TryParseLegacyEntityEntry(rawEntry, out var kind, out var entityId))
                {
                    continue;
                }

                AddOrMergeRecord(records, CreateLegacyRecord(profileId, entityId, kind));
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

    private static void AddLegacyCategory(
        IDictionary<string, EntityCatalogRecord> records,
        string profileId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> catalog,
        string key,
        CatalogEntityKind kind)
    {
        if (!catalog.TryGetValue(key, out var values) || values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            var entityId = value?.Trim();
            if (string.IsNullOrWhiteSpace(entityId))
            {
                continue;
            }

            AddOrMergeRecord(records, CreateLegacyRecord(profileId, entityId, kind));
        }
    }

    private static EntityCatalogRecord CreateLegacyRecord(
        string profileId,
        string entityId,
        CatalogEntityKind kind)
    {
        var normalizedKind = kind == CatalogEntityKind.Unit
            ? CatalogEntityKindClassifier.ResolveKind(entityId, entityId)
            : kind;

        var affiliations = normalizedKind == CatalogEntityKind.Faction
            ? new[] { entityId }
            : CatalogEntityKindClassifier.InferAffiliations(entityId);

        return new EntityCatalogRecord
        {
            EntityId = entityId,
            DisplayNameKey = entityId,
            DisplayName = entityId,
            Kind = normalizedKind,
            SourceProfileId = profileId,
            Affiliations = affiliations,
            VisualState = CatalogEntityVisualState.Unknown,
            CompatibilityState = CatalogEntityCompatibilityState.Unknown
        };
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

        records[incoming.EntityId] = existing with
        {
            Kind = CatalogEntityKindClassifier.SelectMoreSpecificKind(existing.Kind, incoming.Kind),
            Affiliations = mergedAffiliations.Length == 0 ? existing.Affiliations : mergedAffiliations,
            DisplayNameKey = ChooseValue(existing.DisplayNameKey, incoming.DisplayNameKey, existing.EntityId),
            DisplayName = ChooseValue(existing.DisplayName, incoming.DisplayName, existing.EntityId)
        };
    }

    private static string ChooseValue(string existing, string incoming, string fallback)
    {
        if (string.IsNullOrWhiteSpace(existing) || existing.Equals(fallback, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(incoming) ? fallback : incoming;
        }

        return existing;
    }

    private static bool TryParseLegacyEntityEntry(
        string? rawEntry,
        out CatalogEntityKind kind,
        out string entityId)
    {
        kind = CatalogEntityKind.Unknown;
        entityId = string.Empty;

        if (string.IsNullOrWhiteSpace(rawEntry))
        {
            return false;
        }

        var segments = rawEntry.Split('|', StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || string.IsNullOrWhiteSpace(segments[1]))
        {
            return false;
        }

        kind = CatalogEntityKindClassifier.ParseLegacyToken(segments[0]);
        entityId = segments[1];
        return true;
    }
}

public static class CatalogEntityKindClassifier
{
    private static readonly string[] BuildingNameMarkers =
    [
        "BARRACK",
        "FACTORY",
        "BASE",
        "SHIPYARD",
        "YARD",
        "MINE",
        "TURRET",
        "DEFENSE",
        "ACADEMY",
        "OUTPOST",
        "REFINERY",
        "PALACE"
    ];

    private static readonly string[] SpaceStructureMarkers =
    [
        "STATION",
        "STAR_BASE",
        "STARBASE",
        "PLATFORM"
    ];

    private static readonly string[] FactionMarkers =
    [
        "EMPIRE",
        "REBEL",
        "UNDERWORLD",
        "CIS",
        "REPUBLIC",
        "PIRATE"
    ];

    public static CatalogEntityKind ResolveKind(string elementName, string entityId)
    {
        if (ContainsToken(elementName, "planet") || ContainsToken(entityId, "PLANET"))
        {
            return CatalogEntityKind.Planet;
        }

        if (ContainsToken(elementName, "hero") || IsHeroName(entityId))
        {
            return CatalogEntityKind.Hero;
        }

        if (ContainsToken(elementName, "ability") || ContainsToken(entityId, "ABILITY"))
        {
            return CatalogEntityKind.AbilityCarrier;
        }

        if (IsSpaceStructureName(entityId) || ContainsToken(elementName, "space_structure"))
        {
            return CatalogEntityKind.SpaceStructure;
        }

        if (ContainsToken(elementName, "structure") || IsBuildingName(entityId))
        {
            return CatalogEntityKind.Building;
        }

        if (ContainsToken(elementName, "faction") || IsFactionName(entityId))
        {
            return CatalogEntityKind.Faction;
        }

        return CatalogEntityKind.Unit;
    }

    public static CatalogEntityKind ParseLegacyToken(string token)
    {
        return token switch
        {
            var value when value.Equals("Hero", StringComparison.OrdinalIgnoreCase) => CatalogEntityKind.Hero,
            var value when value.Equals("Building", StringComparison.OrdinalIgnoreCase) => CatalogEntityKind.Building,
            var value when value.Equals("SpaceStructure", StringComparison.OrdinalIgnoreCase) => CatalogEntityKind.SpaceStructure,
            var value when value.Equals("AbilityCarrier", StringComparison.OrdinalIgnoreCase) => CatalogEntityKind.AbilityCarrier,
            var value when value.Equals("Planet", StringComparison.OrdinalIgnoreCase) => CatalogEntityKind.Planet,
            var value when value.Equals("Faction", StringComparison.OrdinalIgnoreCase) => CatalogEntityKind.Faction,
            _ => CatalogEntityKind.Unit
        };
    }

    public static string ToLegacyToken(CatalogEntityKind kind)
    {
        return kind switch
        {
            CatalogEntityKind.Hero => "Hero",
            CatalogEntityKind.Building => "Building",
            CatalogEntityKind.SpaceStructure => "SpaceStructure",
            CatalogEntityKind.AbilityCarrier => "AbilityCarrier",
            CatalogEntityKind.Planet => "Planet",
            CatalogEntityKind.Faction => "Faction",
            _ => "Unit"
        };
    }

    public static CatalogEntityKind SelectMoreSpecificKind(
        CatalogEntityKind existing,
        CatalogEntityKind incoming)
    {
        var existingSpecificity = existing switch
        {
            CatalogEntityKind.Faction => 7,
            CatalogEntityKind.Planet => 6,
            CatalogEntityKind.AbilityCarrier => 5,
            CatalogEntityKind.SpaceStructure => 4,
            CatalogEntityKind.Building => 3,
            CatalogEntityKind.Hero => 2,
            CatalogEntityKind.Unit => 1,
            _ => 0
        };
        var incomingSpecificity = incoming switch
        {
            CatalogEntityKind.Faction => 7,
            CatalogEntityKind.Planet => 6,
            CatalogEntityKind.AbilityCarrier => 5,
            CatalogEntityKind.SpaceStructure => 4,
            CatalogEntityKind.Building => 3,
            CatalogEntityKind.Hero => 2,
            CatalogEntityKind.Unit => 1,
            _ => 0
        };
        return incomingSpecificity > existingSpecificity ? incoming : existing;
    }

    public static IReadOnlyList<string> InferAffiliations(string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return Array.Empty<string>();
        }

        var normalizedEntityId = entityId.Trim();
        return FactionMarkers
            .Where(marker => normalizedEntityId.Contains(marker, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsHeroName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = value.Trim();
        return normalizedValue.Contains("HERO", StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.Contains("VADER", StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.Contains("PALPATINE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFactionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = value.Trim();
        return FactionMarkers.Any(marker => normalizedValue.Equals(marker, StringComparison.OrdinalIgnoreCase)) ||
               normalizedValue.EndsWith("_FACTION", StringComparison.OrdinalIgnoreCase) ||
               normalizedValue.StartsWith("FACTION_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBuildingName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = value.Trim();
        return BuildingNameMarkers.Any(marker => normalizedValue.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSpaceStructureName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalizedValue = value.Trim();
        return SpaceStructureMarkers.Any(marker => normalizedValue.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsToken(string? value, string? token)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalizedValue = value.Trim();
        var normalizedToken = token.Trim();
        return normalizedValue.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetSpecificity(CatalogEntityKind kind)
    {
        return kind switch
        {
            CatalogEntityKind.Faction => 7,
            CatalogEntityKind.Planet => 6,
            CatalogEntityKind.Hero => 5,
            CatalogEntityKind.SpaceStructure => 4,
            CatalogEntityKind.Building => 3,
            CatalogEntityKind.AbilityCarrier => 2,
            CatalogEntityKind.Unit => 1,
            _ => 0
        };
    }
}
