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

    internal static IReadOnlyList<RosterEntityViewItem> BuildEntityRoster(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        string selectedProfileId,
        string? selectedWorkshopId)
    {
        if (string.IsNullOrWhiteSpace(selectedProfileId))
        {
            selectedProfileId = string.Empty;
        }

        var safeCatalog = catalog;
        if (safeCatalog is null)
        {
            return Array.Empty<RosterEntityViewItem>();
        }

        if (!safeCatalog.TryGetValue("entity_catalog", out var entries) || entries is null || entries.Count == 0)
        {
            return Array.Empty<RosterEntityViewItem>();
        }

        var rows = new List<RosterEntityViewItem>(entries.Count);
        foreach (var entry in entries)
        {
            if (TryParseEntityRow(entry, selectedProfileId, selectedWorkshopId ?? string.Empty, out var row))
            {
                rows.Add(row);
            }
        }

        return rows
            .OrderBy(static row => row.EntityKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryParseEntityRow(
        string raw,
        string selectedProfileId,
        string selectedWorkshopId,
        out RosterEntityViewItem row)
    {
        row = default!;
        var rawEntry = raw;
        if (string.IsNullOrWhiteSpace(rawEntry))
        {
            return false;
        }

        var segments = rawEntry.Split(RosterSeparator, StringSplitOptions.TrimEntries);
        if (!TryResolveEntityId(segments, out var entityId))
        {
            return false;
        }

        var kind = ResolveSegmentOrDefault(segments, 0, DefaultKind);
        var normalizedProfileId = selectedProfileId;
        var sourceProfileId = ResolveSegmentOrDefault(segments, 2, normalizedProfileId);
        var sourceWorkshopId = ResolveSegmentOrDefault(segments, 3, selectedWorkshopId);
        var visualRef = ResolveSegmentOrDefault(segments, 4, string.Empty);
        var dependencySummary = ResolveSegmentOrDefault(segments, 5, string.Empty);

        var visualState = string.IsNullOrWhiteSpace(visualRef)
            ? RosterEntityVisualState.Missing
            : RosterEntityVisualState.Resolved;

        var compatibilityState = ResolveCompatibilityState(sourceWorkshopId, selectedWorkshopId);
        var transplantReportId = compatibilityState == RosterEntityCompatibilityState.RequiresTransplant
            ? $"transplant:{sourceWorkshopId}:{entityId}"
            : string.Empty;

        row = new RosterEntityViewItem(
            EntityId: entityId,
            DisplayName: entityId,
            EntityKind: kind,
            SourceProfileId: sourceProfileId,
            SourceWorkshopId: sourceWorkshopId,
            DefaultFaction: ResolveDefaultFaction(kind),
            VisualRef: visualRef,
            VisualState: visualState,
            CompatibilityState: compatibilityState,
            TransplantReportId: transplantReportId,
            DependencySummary: dependencySummary);
        return true;
    }

    private static bool TryResolveEntityId(IReadOnlyList<string> segments, out string entityId)
    {
        entityId = string.Empty;
        var safeSegments = segments;
        if (safeSegments is null || safeSegments.Count < 2)
        {
            return false;
        }

        var segment = safeSegments[1];
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        entityId = segment.Trim();
        return true;
    }

    private static string ResolveSegmentOrDefault(IReadOnlyList<string> segments, int index, string fallback)
    {
        var safeSegments = segments;
        if (safeSegments is null || index >= safeSegments.Count)
        {
            return fallback;
        }

        var segment = safeSegments[index];
        if (string.IsNullOrWhiteSpace(segment))
        {
            return fallback;
        }

        return segment.Trim();
    }

    private static RosterEntityCompatibilityState ResolveCompatibilityState(string sourceWorkshopId, string selectedWorkshopId)
    {
        sourceWorkshopId = sourceWorkshopId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceWorkshopId))
        {
            return RosterEntityCompatibilityState.Native;
        }

        if (string.IsNullOrWhiteSpace(selectedWorkshopId))
        {
            return RosterEntityCompatibilityState.Native;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(sourceWorkshopId, selectedWorkshopId)
            ? RosterEntityCompatibilityState.Native
            : RosterEntityCompatibilityState.RequiresTransplant;
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
            StringComparer.OrdinalIgnoreCase.Equals(kind, "SpaceStructure"))
        {
            return DefaultFactionPlanetOwner;
        }

        return DefaultFactionEmpire;
    }
}
