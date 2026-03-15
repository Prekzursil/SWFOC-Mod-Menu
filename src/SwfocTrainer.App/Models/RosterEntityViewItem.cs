using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.Models;

[System.CLSCompliant(false)]
public sealed record RosterEntityViewItem(
    string EntityId,
    string DisplayName,
    string DisplayNameKey,
    string DisplayNameSourcePath,
    string EntityKind,
    string SourceProfileId,
    string SourceWorkshopId,
    string SourceLabel,
    string DefaultFaction,
    string AffiliationSummary,
    string PopulationCostText,
    string BuildCostText,
    string IconPath,
    string VisualRef,
    string VisualSummary,
    RosterEntityVisualState VisualState,
    string CompatibilitySummary,
    RosterEntityCompatibilityState CompatibilityState,
    string TransplantReportId,
    string DependencySummary);
