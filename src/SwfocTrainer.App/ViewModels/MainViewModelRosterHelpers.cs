using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

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
        if (catalog is null ||
            !catalog.TryGetValue("entity_catalog", out var entries) ||
            entries.Count == 0)
        {
            return Array.Empty<RosterEntityViewItem>();
        }

        var rows = new List<RosterEntityViewItem>(entries.Count);
        foreach (var entry in entries)
        {
            if (TryParseEntityRow(entry, selectedProfileId, selectedWorkshopId, out var row))
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
        string? selectedWorkshopId,
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
        var sourceWorkshopId = ResolveSegmentOrDefault(segments, 3, selectedWorkshopId ?? string.Empty);
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
        if (segments.Count < 2 || string.IsNullOrWhiteSpace(segments[1]))
        {
            return false;
        }

        entityId = segments[1].Trim();
        return true;
    }

    private static string ResolveSegmentOrDefault(IReadOnlyList<string> segments, int index, string fallback)
    {
        if (index >= segments.Count || string.IsNullOrWhiteSpace(segments[index]))
        {
            return fallback;
        }

        return segments[index].Trim();
    }

    private static RosterEntityCompatibilityState ResolveCompatibilityState(string sourceWorkshopId, string? selectedWorkshopId)
    {
        if (string.IsNullOrWhiteSpace(sourceWorkshopId) ||
            string.IsNullOrWhiteSpace(selectedWorkshopId) ||
            sourceWorkshopId.Equals(selectedWorkshopId, StringComparison.OrdinalIgnoreCase))
        {
            return RosterEntityCompatibilityState.Native;
        }

        return RosterEntityCompatibilityState.RequiresTransplant;
    }

    private static string ResolveDefaultFaction(string kind)
    {
        if (kind.Equals("Hero", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultFactionHeroOwner;
        }

        if (kind.Equals("Building", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("SpaceStructure", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultFactionPlanetOwner;
        }

        return DefaultFactionEmpire;
    }
}