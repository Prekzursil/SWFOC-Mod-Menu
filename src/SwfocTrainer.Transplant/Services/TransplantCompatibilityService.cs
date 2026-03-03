using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Transplant.Services;

public sealed class TransplantCompatibilityService : ITransplantCompatibilityService
{
    public Task<TransplantValidationReport> ValidateAsync(
        string targetProfileId,
        IReadOnlyList<string> activeWorkshopIds,
        IReadOnlyList<RosterEntityRecord> entities,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var activeSet = activeWorkshopIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<TransplantEntityValidation>(entities.Count);
        foreach (var entity in entities)
        {
            results.Add(EvaluateEntity(entity, activeSet));
        }

        var blocking = results.Where(static entity => !entity.Resolved).ToArray();
        var report = new TransplantValidationReport(
            TargetProfileId: targetProfileId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            AllResolved: blocking.Length == 0,
            TotalEntities: results.Count,
            BlockingEntityCount: blocking.Length,
            Entities: results,
            Diagnostics: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["activeWorkshopIds"] = activeSet.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                ["requiresTransplantCount"] = results.Count(static entity => entity.RequiresTransplant),
                ["resolvedCount"] = results.Count(static entity => entity.Resolved),
                ["blockingEntityIds"] = blocking.Select(static entity => entity.EntityId).ToArray()
            });

        return Task.FromResult(report);
    }

    private static TransplantEntityValidation EvaluateEntity(
        RosterEntityRecord entity,
        IReadOnlySet<string> activeWorkshopIds)
    {
        var sourceWorkshopId = NormalizeOrNull(entity.SourceWorkshopId);
        var requiresTransplant = RequiresTransplant(sourceWorkshopId, activeWorkshopIds);

        if (!requiresTransplant)
        {
            return BuildActiveChainValidation(entity, sourceWorkshopId);
        }

        var visualRef = NormalizeOrNull(entity.VisualRef);
        var dependencies = NormalizeDependencies(entity.DependencyRefs);

        if (string.IsNullOrWhiteSpace(visualRef))
        {
            return BuildMissingVisualValidation(entity, sourceWorkshopId, dependencies);
        }

        if (dependencies.Length == 0)
        {
            return BuildMissingDependencyValidation(entity, sourceWorkshopId, visualRef);
        }

        return BuildResolvedTransplantValidation(entity, sourceWorkshopId, visualRef);
    }

    private static bool RequiresTransplant(string? sourceWorkshopId, IReadOnlySet<string> activeWorkshopIds)
    {
        return !string.IsNullOrWhiteSpace(sourceWorkshopId) && !activeWorkshopIds.Contains(sourceWorkshopId);
    }

    private static string[] NormalizeDependencies(IReadOnlyList<string>? dependencyRefs)
    {
        return dependencyRefs is null
            ? Array.Empty<string>()
            : dependencyRefs
                .Where(static dep => !string.IsNullOrWhiteSpace(dep))
                .Select(static dep => dep.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static TransplantEntityValidation BuildActiveChainValidation(RosterEntityRecord entity, string? sourceWorkshopId)
    {
        return new TransplantEntityValidation(
            EntityId: entity.EntityId,
            SourceProfileId: entity.SourceProfileId,
            SourceWorkshopId: sourceWorkshopId,
            RequiresTransplant: false,
            Resolved: true,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "Entity belongs to active workshop chain.",
            VisualRef: NormalizeOrNull(entity.VisualRef),
            MissingDependencies: Array.Empty<string>());
    }

    private static TransplantEntityValidation BuildMissingVisualValidation(
        RosterEntityRecord entity,
        string? sourceWorkshopId,
        IReadOnlyList<string> dependencies)
    {
        return new TransplantEntityValidation(
            EntityId: entity.EntityId,
            SourceProfileId: entity.SourceProfileId,
            SourceWorkshopId: sourceWorkshopId,
            RequiresTransplant: true,
            Resolved: false,
            ReasonCode: RuntimeReasonCode.ROSTER_VISUAL_MISSING,
            Message: "Cross-mod transplant is blocked because visual reference is missing.",
            VisualRef: null,
            MissingDependencies: dependencies);
    }

    private static TransplantEntityValidation BuildMissingDependencyValidation(
        RosterEntityRecord entity,
        string? sourceWorkshopId,
        string visualRef)
    {
        return new TransplantEntityValidation(
            EntityId: entity.EntityId,
            SourceProfileId: entity.SourceProfileId,
            SourceWorkshopId: sourceWorkshopId,
            RequiresTransplant: true,
            Resolved: false,
            ReasonCode: RuntimeReasonCode.TRANSPLANT_DEPENDENCY_MISSING,
            Message: "Cross-mod transplant is blocked because dependency references are missing.",
            VisualRef: visualRef,
            MissingDependencies: Array.Empty<string>());
    }

    private static TransplantEntityValidation BuildResolvedTransplantValidation(
        RosterEntityRecord entity,
        string? sourceWorkshopId,
        string visualRef)
    {
        return new TransplantEntityValidation(
            EntityId: entity.EntityId,
            SourceProfileId: entity.SourceProfileId,
            SourceWorkshopId: sourceWorkshopId,
            RequiresTransplant: true,
            Resolved: true,
            ReasonCode: RuntimeReasonCode.TRANSPLANT_APPLIED,
            Message: "Cross-mod transplant requirements resolved.",
            VisualRef: visualRef,
            MissingDependencies: Array.Empty<string>());
    }

    private static string? NormalizeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
