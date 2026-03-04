using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.Models;

[System.CLSCompliant(false)]
public sealed record RosterEntityViewItem(
    string EntityId,
    string DisplayName,
    string EntityKind,
    string SourceProfileId,
    string SourceWorkshopId,
    string DefaultFaction,
    string VisualRef,
    RosterEntityVisualState VisualState,
    RosterEntityCompatibilityState CompatibilityState,
    string TransplantReportId,
    string DependencySummary);
