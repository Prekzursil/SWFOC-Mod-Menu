using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelRosterHelpers
{
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
            if (!TryParseEntityRow(entry, selectedProfileId, selectedWorkshopId, out var row))
            {
                continue;
            }

            rows.Add(row);
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

        var segments = raw.Split('|', StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || string.IsNullOrWhiteSpace(segments[1]))
        {
            return false;
        }

        var kind = string.IsNullOrWhiteSpace(segments[0]) ? "Unit" : segments[0];
        var entityId = segments[1].Trim();
        var sourceProfileId = segments.Length >= 3 && !string.IsNullOrWhiteSpace(segments[2])
            ? segments[2].Trim()
            : selectedProfileId;
        var sourceWorkshopId = segments.Length >= 4 && !string.IsNullOrWhiteSpace(segments[3])
            ? segments[3].Trim()
            : selectedWorkshopId ?? string.Empty;
        var visualRef = segments.Length >= 5 && !string.IsNullOrWhiteSpace(segments[4])
            ? segments[4].Trim()
            : string.Empty;
        var dependencySummary = segments.Length >= 6 && !string.IsNullOrWhiteSpace(segments[5])
            ? segments[5].Trim()
            : string.Empty;

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
            return "HeroOwner";
        }

        if (kind.Equals("Building", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("SpaceStructure", StringComparison.OrdinalIgnoreCase))
        {
            return "PlanetOwner";
        }

        return "Empire";
    }
}
