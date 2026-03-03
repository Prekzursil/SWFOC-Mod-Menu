namespace SwfocTrainer.Core.Models;

public sealed record TransplantEntityValidation(
    string EntityId,
    string SourceProfileId,
    string? SourceWorkshopId,
    bool RequiresTransplant,
    bool Resolved,
    RuntimeReasonCode ReasonCode,
    string Message,
    string? VisualRef,
    IReadOnlyList<string> MissingDependencies);

public sealed record TransplantValidationReport(
    string TargetProfileId,
    DateTimeOffset GeneratedAtUtc,
    bool AllResolved,
    int TotalEntities,
    int BlockingEntityCount,
    IReadOnlyList<TransplantEntityValidation> Entities,
    IReadOnlyDictionary<string, object?> Diagnostics)
{
    public static TransplantValidationReport Empty(string targetProfileId) =>
        new(
            TargetProfileId: targetProfileId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            AllResolved: true,
            TotalEntities: 0,
            BlockingEntityCount: 0,
            Entities: Array.Empty<TransplantEntityValidation>(),
            Diagnostics: new Dictionary<string, object?>());
}

public sealed record TransplantPlan(
    string TargetProfileId,
    IReadOnlyList<string> ActiveWorkshopIds,
    IReadOnlyList<RosterEntityRecord> Entities,
    string? OutputDirectory = null);

public sealed record TransplantResult(
    bool Succeeded,
    RuntimeReasonCode ReasonCode,
    string Message,
    TransplantValidationReport Report,
    string? ArtifactPath = null,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);
