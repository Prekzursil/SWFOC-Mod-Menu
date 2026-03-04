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
        ArgumentNullException.ThrowIfNull(selectedProfileId);

        if (catalog is null)
        {
            return Array.Empty<RosterEntityViewItem>();
        }

        if (!catalog.TryGetValue("entity_catalog", out var entries) || entries is null || entries.Count == 0)
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
        var normalizedProfileId = selectedProfileId ?? string.Empty;
        var sourceProfileId = ResolveSegmentOrDefault(segments, 2, normalizedProfileId);
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
        if (segments.Count < 2)
        {
            return false;
        }

        var segment = segments[1] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        entityId = segment.Trim();
        return true;
    }

    private static string ResolveSegmentOrDefault(IReadOnlyList<string> segments, int index, string fallback)
    {
        if (index >= segments.Count)
        {
            return fallback;
        }

        var segment = segments[index] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return fallback;
        }

        return segment.Trim();
    }

    private static RosterEntityCompatibilityState ResolveCompatibilityState(string sourceWorkshopId, string? selectedWorkshopId)
    {
        sourceWorkshopId ??= string.Empty;
        if (string.IsNullOrWhiteSpace(sourceWorkshopId))
        {
            return RosterEntityCompatibilityState.Native;
        }

        if (string.IsNullOrWhiteSpace(selectedWorkshopId))
        {
            return RosterEntityCompatibilityState.Native;
        }

        return sourceWorkshopId.Equals(selectedWorkshopId, StringComparison.OrdinalIgnoreCase)
            ? RosterEntityCompatibilityState.Native
            : RosterEntityCompatibilityState.RequiresTransplant;
    }

    private static string ResolveDefaultFaction(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return DefaultFactionEmpire;
        }

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
