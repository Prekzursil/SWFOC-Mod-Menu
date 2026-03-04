namespace SwfocTrainer.Core.Models;

public enum RosterEntityKind
{
    Unknown = 0,
    Unit,
    Hero,
    Building,
    SpaceStructure,
    AbilityCarrier
}

public enum SpawnPersistencePolicy
{
    Normal = 0,
    EphemeralBattleOnly,
    PersistentGalactic
}

public enum PopulationCostPolicy
{
    Normal = 0,
    ForceZeroTactical
}

public enum RosterEntityVisualState
{
    Unknown = 0,
    Resolved,
    Missing
}

public enum RosterEntityCompatibilityState
{
    Unknown = 0,
    Native,
    Compatible,
    RequiresTransplant,
    Blocked
}

public sealed record RosterEntityRecord(
    string EntityId,
    string DisplayName,
    string SourceProfileId,
    string? SourceWorkshopId,
    RosterEntityKind EntityKind,
    string DefaultFaction,
    IReadOnlyList<RuntimeMode> AllowedModes,
    string? VisualRef = null,
    IReadOnlyList<string>? DependencyRefs = null,
    string? TransplantState = null,
    RosterEntityVisualState VisualState = RosterEntityVisualState.Unknown,
    RosterEntityCompatibilityState CompatibilityState = RosterEntityCompatibilityState.Unknown,
    IReadOnlyDictionary<string, string>? MechanicFlags = null,
    string? TransplantReportId = null);
