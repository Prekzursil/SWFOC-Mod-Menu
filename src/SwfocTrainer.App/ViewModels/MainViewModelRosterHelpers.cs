using System.Globalization;
using System.Text.Json.Nodes;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

[System.CLSCompliant(false)]
internal static class MainViewModelRosterHelpers
{
    private const char RosterSeparator = '|';
    private const string DefaultKind = "Unit";
    private const string DefaultFactionEmpire = "Empire";
    private const string DefaultFactionHeroOwner = "HeroOwner";
    private const string DefaultFactionPlanetOwner = "PlanetOwner";
    private const string UnknownValue = "unknown";
    private const string NotAvailableValue = "n/a";

    private static readonly string[] TypedCatalogKeys =
    [
        "entity_catalog_typed",
        "entity_catalog_records",
        "typed_entity_catalog"
    ];

    internal static IReadOnlyList<RosterEntityViewItem> BuildEntityRoster(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        string selectedProfileId,
        string? selectedWorkshopId)
    {
        selectedProfileId = string.IsNullOrWhiteSpace(selectedProfileId)
            ? string.Empty
            : selectedProfileId.Trim();

        if (catalog is null)
        {
            return Array.Empty<RosterEntityViewItem>();
        }

        var normalizedWorkshopId = selectedWorkshopId?.Trim() ?? string.Empty;
        var rows = new Dictionary<string, RosterEntityViewItem>(StringComparer.OrdinalIgnoreCase);
        AddTypedRows(catalog, selectedProfileId, normalizedWorkshopId, rows);
        AddLegacyRows(catalog, selectedProfileId, normalizedWorkshopId, rows);

        return rows.Count == 0
            ? Array.Empty<RosterEntityViewItem>()
            : OrderRows(rows.Values);
    }

    private static void AddTypedRows(
        IReadOnlyDictionary<string, IReadOnlyList<string>> catalog,
        string selectedProfileId,
        string selectedWorkshopId,
        IDictionary<string, RosterEntityViewItem> rows)
    {
        foreach (var key in TypedCatalogKeys)
        {
            if (!catalog.TryGetValue(key, out var typedEntries) || typedEntries is null)
            {
                continue;
            }

            foreach (var entry in typedEntries)
            {
                if (!TryParseTypedEntityRow(entry, selectedProfileId, selectedWorkshopId, out var row))
                {
                    continue;
                }

                rows[BuildRowKey(row)] = row;
            }
        }
    }

    private static void AddLegacyRows(
        IReadOnlyDictionary<string, IReadOnlyList<string>> catalog,
        string selectedProfileId,
        string selectedWorkshopId,
        IDictionary<string, RosterEntityViewItem> rows)
    {
        if (!catalog.TryGetValue("entity_catalog", out var entries) || entries is null || entries.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (!TryParseCatalogRow(entry, selectedProfileId, selectedWorkshopId, out var row))
            {
                continue;
            }

            rows.TryAdd(BuildRowKey(row), row);
        }
    }

    private static bool TryParseCatalogRow(
        string raw,
        string selectedProfileId,
        string selectedWorkshopId,
        out RosterEntityViewItem row)
    {
        if (TryParseTypedEntityRow(raw, selectedProfileId, selectedWorkshopId, out var typedRow))
        {
            row = typedRow;
            return true;
        }

        return TryParseLegacyEntityRow(raw, selectedProfileId, selectedWorkshopId, out row);
    }

    private static IReadOnlyList<RosterEntityViewItem> OrderRows(IEnumerable<RosterEntityViewItem> rows)
    {
        return rows
            .OrderBy(static row => row.EntityKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildRowKey(RosterEntityViewItem row)
    {
        return string.Join(
            RosterSeparator,
            row.SourceProfileId,
            row.SourceWorkshopId,
            row.EntityId);
    }

    private static bool TryParseLegacyEntityRow(
        string raw,
        string selectedProfileId,
        string selectedWorkshopId,
        out RosterEntityViewItem row)
    {
        row = default!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var segments = raw.Split(RosterSeparator, StringSplitOptions.TrimEntries);
        if (!TryResolveEntityId(segments, out var entityId))
        {
            return false;
        }

        var kind = ResolveSegmentOrDefault(segments, 0, DefaultKind);
        var sourceProfileId = ResolveSegmentOrDefault(segments, 2, selectedProfileId);
        var sourceWorkshopId = ResolveSegmentOrDefault(segments, 3, selectedWorkshopId);
        var visualRef = ResolveSegmentOrDefault(segments, 4, string.Empty);
        var dependencySummary = ResolveSegmentOrDefault(segments, 5, string.Empty);
        var displayName = entityId;
        var displayNameKey = string.Empty;
        var defaultFaction = ResolveDefaultFaction(kind);
        var visualState = InferVisualState(visualRef, null);
        var compatibilityState = ResolveCompatibilityState(sourceWorkshopId, selectedWorkshopId, null);
        var transplantReportId = ResolveTransplantReportId(compatibilityState, sourceWorkshopId, entityId, string.Empty);

        row = new RosterEntityViewItem(
            EntityId: entityId,
            DisplayName: displayName,
            DisplayNameKey: displayNameKey,
            EntityKind: kind,
            SourceProfileId: sourceProfileId,
            SourceWorkshopId: sourceWorkshopId,
            SourceLabel: BuildSourceLabel(sourceProfileId, sourceWorkshopId),
            DefaultFaction: defaultFaction,
            AffiliationSummary: defaultFaction,
            PopulationCostText: NotAvailableValue,
            BuildCostText: NotAvailableValue,
            VisualRef: visualRef,
            VisualSummary: BuildVisualSummary(visualState, visualRef),
            VisualState: visualState,
            CompatibilitySummary: BuildCompatibilitySummary(compatibilityState, sourceWorkshopId),
            CompatibilityState: compatibilityState,
            TransplantReportId: transplantReportId,
            DependencySummary: dependencySummary);
        return true;
    }

    private static bool TryParseTypedEntityRow(
        string raw,
        string selectedProfileId,
        string selectedWorkshopId,
        out RosterEntityViewItem row)
    {
        row = default!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        JsonObject? json;
        try
        {
            json = JsonNode.Parse(raw) as JsonObject;
        }
        catch
        {
            return false;
        }

        if (json is null)
        {
            return false;
        }

        var entityId = ReadString(json, "entityId", "EntityId", "id", "Id");
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return false;
        }

        var kind = ReadString(json, "kind", "Kind", "entityKind", "EntityKind");
        if (string.IsNullOrWhiteSpace(kind))
        {
            kind = DefaultKind;
        }

        var displayNameKey = ReadString(json, "displayNameKey", "DisplayNameKey", "textId", "TextId");
        var displayName = FirstNonEmpty(
            ReadString(json, "displayName", "DisplayName", "resolvedDisplayName", "ResolvedDisplayName", "name", "Name"),
            displayNameKey,
            entityId);
        var sourceProfileId = FirstNonEmpty(
            ReadString(json, "sourceProfileId", "SourceProfileId", "profileId", "ProfileId"),
            selectedProfileId);
        var sourceWorkshopId = FirstNonEmpty(
            ReadString(json, "sourceWorkshopId", "SourceWorkshopId", "workshopId", "WorkshopId"),
            selectedWorkshopId);
        var affiliations = ReadStringList(json, "affiliations", "Affiliations");
        var defaultFaction = FirstNonEmpty(
            ReadString(json, "defaultFaction", "DefaultFaction"),
            affiliations.FirstOrDefault(),
            ResolveDefaultFaction(kind));
        var visualRef = FirstNonEmpty(
            ReadString(json, "visualRef", "VisualRef", "iconPath", "IconPath", "iconCachePath", "IconCachePath"),
            string.Empty);
        var dependencyRefs = ReadStringList(json, "dependencyRefs", "DependencyRefs", "dependencies", "Dependencies");
        var compatibilityState = ParseEnum(ReadString(json, "compatibilityState", "CompatibilityState"), ResolveCompatibilityState(sourceWorkshopId, selectedWorkshopId, null));
        var visualState = ParseEnum(ReadString(json, "visualState", "VisualState"), InferVisualState(visualRef, null));
        var transplantReportId = ResolveTransplantReportId(
            compatibilityState,
            sourceWorkshopId,
            entityId,
            ReadString(json, "transplantReportId", "TransplantReportId"));

        row = new RosterEntityViewItem(
            EntityId: entityId,
            DisplayName: displayName,
            DisplayNameKey: displayNameKey,
            EntityKind: kind,
            SourceProfileId: sourceProfileId,
            SourceWorkshopId: sourceWorkshopId,
            SourceLabel: FirstNonEmpty(ReadString(json, "sourceLabel", "SourceLabel"), BuildSourceLabel(sourceProfileId, sourceWorkshopId)),
            DefaultFaction: defaultFaction,
            AffiliationSummary: BuildAffiliationSummary(affiliations, defaultFaction),
            PopulationCostText: ReadScalarText(json, "populationValue", "PopulationValue", "population", "Population"),
            BuildCostText: ReadScalarText(json, "buildCostCredits", "BuildCostCredits", "buildCost", "BuildCost"),
            VisualRef: visualRef,
            VisualSummary: BuildVisualSummary(visualState, visualRef),
            VisualState: visualState,
            CompatibilitySummary: FirstNonEmpty(
                ReadString(json, "compatibilitySummary", "CompatibilitySummary"),
                BuildCompatibilitySummary(compatibilityState, sourceWorkshopId)),
            CompatibilityState: compatibilityState,
            TransplantReportId: transplantReportId,
            DependencySummary: NormalizeDependencySummary(string.Join("; ", dependencyRefs)));
        return true;
    }

    private static string ReadScalarText(JsonObject json, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!json.TryGetPropertyValue(key, out var node) || node is null)
            {
                continue;
            }

            if (TryReadScalarText(node, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return NotAvailableValue;
    }

    private static bool TryReadScalarText(JsonNode node, out string value)
    {
        value = string.Empty;
        if (node is not JsonValue jsonValue)
        {
            value = node.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        if (jsonValue.TryGetValue<string>(out var stringValue))
        {
            value = stringValue;
            return true;
        }

        if (jsonValue.TryGetValue<int>(out var intValue))
        {
            value = intValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            value = longValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            value = doubleValue.ToString("0.###", CultureInfo.InvariantCulture);
            return true;
        }

        if (jsonValue.TryGetValue<bool>(out var boolValue))
        {
            value = boolValue ? bool.TrueString : bool.FalseString;
            return true;
        }

        value = node.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static List<string> ReadStringList(JsonObject json, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!json.TryGetPropertyValue(key, out var node) || node is null)
            {
                continue;
            }

            var values = ReadStringList(node);

            if (values.Count > 0)
            {
                return values;
            }
        }

        return [];
    }

    private static List<string> ReadStringList(JsonNode node)
    {
        return node switch
        {
            JsonArray array => ReadArrayStringList(array),
            _ => ReadDelimitedStringList(node.ToString())
        };
    }

    private static List<string> ReadArrayStringList(JsonArray array)
    {
        var values = new List<string>();
        foreach (var item in array)
        {
            var text = item?.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(text);
            }
        }

        return values;
    }

    private static List<string> ReadDelimitedStringList(string raw)
    {
        return raw
            .Split(new[] { ';', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(static value => value.Length > 0)
            .ToList();
    }

    private static string ReadString(JsonObject json, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!json.TryGetPropertyValue(key, out var node) || node is null)
            {
                continue;
            }

            var value = node.ToString().Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static TEnum ParseEnum<TEnum>(string raw, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryResolveEntityId(IReadOnlyList<string> segments, out string entityId)
    {
        entityId = string.Empty;
        if (segments is null || segments.Count < 2)
        {
            return false;
        }

        var segment = segments[1];
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        entityId = segment.Trim();
        return true;
    }

    private static string ResolveSegmentOrDefault(IReadOnlyList<string> segments, int index, string fallback)
    {
        if (segments is null || index >= segments.Count)
        {
            return fallback;
        }

        var segment = segments[index];
        return string.IsNullOrWhiteSpace(segment)
            ? fallback
            : segment.Trim();
    }

    private static RosterEntityCompatibilityState ResolveCompatibilityState(
        string sourceWorkshopId,
        string selectedWorkshopId,
        RosterEntityCompatibilityState? declaredState)
    {
        if (declaredState.HasValue && declaredState.Value != RosterEntityCompatibilityState.Unknown)
        {
            return declaredState.Value;
        }

        if (string.IsNullOrWhiteSpace(sourceWorkshopId) || string.IsNullOrWhiteSpace(selectedWorkshopId))
        {
            return RosterEntityCompatibilityState.Native;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(sourceWorkshopId, selectedWorkshopId)
            ? RosterEntityCompatibilityState.Native
            : RosterEntityCompatibilityState.RequiresTransplant;
    }

    private static RosterEntityVisualState InferVisualState(string visualRef, RosterEntityVisualState? declaredState)
    {
        if (declaredState.HasValue && declaredState.Value != RosterEntityVisualState.Unknown)
        {
            return declaredState.Value;
        }

        return string.IsNullOrWhiteSpace(visualRef)
            ? RosterEntityVisualState.Missing
            : RosterEntityVisualState.Resolved;
    }

    private static string ResolveTransplantReportId(
        RosterEntityCompatibilityState compatibilityState,
        string sourceWorkshopId,
        string entityId,
        string declaredReportId)
    {
        if (!string.IsNullOrWhiteSpace(declaredReportId))
        {
            return declaredReportId;
        }

        return compatibilityState == RosterEntityCompatibilityState.RequiresTransplant
            ? $"transplant:{sourceWorkshopId}:{entityId}"
            : string.Empty;
    }

    private static string ResolveDefaultFaction(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return DefaultFactionEmpire;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(kind, "Hero"))
        {
            return DefaultFactionHeroOwner;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(kind, "Building") ||
            StringComparer.OrdinalIgnoreCase.Equals(kind, "SpaceStructure") ||
            StringComparer.OrdinalIgnoreCase.Equals(kind, "Planet"))
        {
            return DefaultFactionPlanetOwner;
        }

        return DefaultFactionEmpire;
    }

    private static string BuildSourceLabel(string sourceProfileId, string sourceWorkshopId)
    {
        var profile = string.IsNullOrWhiteSpace(sourceProfileId) ? UnknownValue : sourceProfileId.Trim();
        var workshop = string.IsNullOrWhiteSpace(sourceWorkshopId) ? "native" : sourceWorkshopId.Trim();
        return $"{profile} | {workshop}";
    }

    private static string BuildAffiliationSummary(IReadOnlyList<string> affiliations, string fallback)
    {
        return affiliations.Count == 0
            ? fallback
            : string.Join(", ", affiliations.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildVisualSummary(RosterEntityVisualState visualState, string visualRef)
    {
        return visualState switch
        {
            RosterEntityVisualState.Resolved when !string.IsNullOrWhiteSpace(visualRef) => $"resolved: {visualRef}",
            RosterEntityVisualState.Resolved => "resolved",
            RosterEntityVisualState.Missing => "missing",
            _ => UnknownValue
        };
    }

    private static string BuildCompatibilitySummary(RosterEntityCompatibilityState compatibilityState, string sourceWorkshopId)
    {
        return compatibilityState switch
        {
            RosterEntityCompatibilityState.RequiresTransplant when !string.IsNullOrWhiteSpace(sourceWorkshopId)
                => $"{compatibilityState} ({sourceWorkshopId})",
            RosterEntityCompatibilityState.Blocked when !string.IsNullOrWhiteSpace(sourceWorkshopId)
                => $"{compatibilityState} ({sourceWorkshopId})",
            _ => compatibilityState.ToString()
        };
    }

    private static string NormalizeDependencySummary(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            raw.Split(new[] { ';', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
