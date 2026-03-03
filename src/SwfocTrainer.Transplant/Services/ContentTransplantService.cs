using System.Text.Json;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Transplant.Services;

public sealed class ContentTransplantService : IContentTransplantService
{
    private readonly ITransplantCompatibilityService _compatibilityService;

    public ContentTransplantService(ITransplantCompatibilityService compatibilityService)
    {
        _compatibilityService = compatibilityService;
    }

    public async Task<TransplantResult> ExecuteAsync(TransplantPlan plan, CancellationToken cancellationToken)
    {
        var report = await _compatibilityService.ValidateAsync(
            plan.TargetProfileId,
            plan.ActiveWorkshopIds,
            plan.Entities,
            cancellationToken);

        var reasonCode = report.AllResolved
            ? RuntimeReasonCode.TRANSPLANT_APPLIED
            : RuntimeReasonCode.TRANSPLANT_VALIDATION_FAILED;
        var message = report.AllResolved
            ? "Transplant compatibility validation passed for all entities."
            : "Transplant compatibility validation found blocking entities.";

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["totalEntities"] = report.TotalEntities,
            ["blockingEntityCount"] = report.BlockingEntityCount,
            ["allResolved"] = report.AllResolved
        };

        string? artifactPath = null;
        if (!string.IsNullOrWhiteSpace(plan.OutputDirectory))
        {
            var outputDirectory = Path.GetFullPath(plan.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);
            artifactPath = Path.Combine(outputDirectory, "transplant-report.json");

            var artifact = new
            {
                schemaVersion = "1.0",
                generatedAtUtc = DateTimeOffset.UtcNow,
                targetProfileId = report.TargetProfileId,
                activeWorkshopIds = plan.ActiveWorkshopIds,
                totalEntities = report.TotalEntities,
                blockingEntityCount = report.BlockingEntityCount,
                allResolved = report.AllResolved,
                entities = report.Entities,
                diagnostics = report.Diagnostics
            };

            await File.WriteAllTextAsync(
                artifactPath,
                JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }

        return new TransplantResult(
            Succeeded: report.AllResolved,
            ReasonCode: reasonCode,
            Message: message,
            Report: report,
            ArtifactPath: artifactPath,
            Diagnostics: diagnostics);
    }
}
